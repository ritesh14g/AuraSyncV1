using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using AuraSync.AI;
using AuraSync.Networking;
using AuraSync.Notes;
using AuraSync.Simulation;
using AuraSync.Space;

namespace AuraSync.Recording
{
    /// <summary>
    /// Replays a recording in 3D on this phone (V3 Phase 9), outside a room only, so it can never
    /// fight a live session.
    ///
    /// Every feature is driven through the same "quiet" entry points the room already uses
    /// (ApplyQuietly and friends): outside a room nothing is shared, so the replay stays on this
    /// phone. Seeking works by state, not by playing forward: for each kind of state the last
    /// recorded value at or before the moment is applied, so scrubbing anywhere is instant. Cones,
    /// pings and who is speaking are worked out for the moment the same way.
    ///
    /// **Presenter view** puts the model where the presenter had it (pose interpolated between
    /// samples); **free camera** leaves turning and zooming to you while everything else plays.
    /// Leaving the replay puts the model back exactly as it was before it started.
    ///
    /// **The presenter's voice** plays from the WAV beside the recording, kept on the timeline:
    /// scrubbing, pausing and the speed move it too (at 0.5x / 2x the pitch shifts with it).
    /// </summary>
    public class SessionPlayer : MonoBehaviour
    {
        public static SessionPlayer Instance { get; private set; }

        public static bool IsReplaying => Instance != null && Instance.Current != null;

        /// <summary>A replay waiting for its model to load (the scene is reloaded on another model).</summary>
        private static string _pendingPath;

        private const string SpaceSceneName = "SpaceScene";

        public static event Action OnChanged;

        public LoadedRecording Current { get; private set; }
        public float Time01 => Current != null && Current.Duration > 0f ? T / Current.Duration : 0f;
        public float T { get; private set; }
        public bool  Playing { get; private set; }
        public float Speed { get; private set; } = 1f;
        public bool  FreeCamera { get; private set; }
        public string Caption { get; private set; } = "";
        public string Speaking { get; private set; } = "";
        public bool  HasVoice => _voiceData != null;

        // The voice track: µ-law bytes, decoded as the streaming clip asks for them.
        private byte[] _voiceData;
        private volatile int _voiceRead;
        private AudioSource _voiceSource;
        private AudioClip _voiceClip;

        public static readonly float[] Speeds = { 0.5f, 1f, 2f };
        private const float ViewLife = AttentionSync.StaleSeconds;
        private const float CaptionSeconds = 5f;

        // Lines by kind, in time order
        private readonly Dictionary<string, List<RecLine>> _byKind = new Dictionary<string, List<RecLine>>();
        private readonly Dictionary<int, List<RecLine>> _views = new Dictionary<int, List<RecLine>>();
        private readonly List<KeyValuePair<float, string>> _captions = new List<KeyValuePair<float, string>>();
        private List<RecLine> _noteEvents = new List<RecLine>();
        private readonly Dictionary<string, int> _applied = new Dictionary<string, int>();
        private int _notesApplied = -2;
        private bool _force;

        // How the model was before the replay, put back at the end
        private float _was_explode; private string _was_isolate = "", _was_select = "";
        private SectionCutter.CutState _was_section; private SimState _was_sim;
        private Vector3 _was_pos, _was_scale; private Quaternion _was_rot; private bool _was_pose;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnBus;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnBus;
            if (Current != null) Exit(restore: false);
            if (Instance == this) Instance = null;
        }

        // ── Starting ───────────────────────────────────────────────────────────

        /// <summary>Plays a recording: at once if its model is on screen, else after loading it.</summary>
        public static string Play(RecordingInfo info)
        {
            if (PhotonNetwork.InRoom) return "Leave the room to replay a recording.";
            if (info == null) return "That recording could not be read.";
            if (SessionRecorder.Instance != null && SessionRecorder.Instance.IsRecording) return "Stop recording first.";

            if (info.header.model == SpaceSceneManager.CurrentModelKey && Instance != null)
                return Instance.Begin(info) ? null : "That recording has nothing to play.";

            // Another model, or no model open at all (Interactive mode → Recordings, in the V1 scene):
            // open it the way the model list does, in SpaceScene, then start once it has loaded.
            _pendingPath = info.path;
            PlayerPrefs.SetString("SelectedModelFile", info.header.model);
            PlayerPrefs.Save();
            SceneManager.LoadScene(SpaceSceneName);
            return null;
        }

        private void OnBus(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;

            if (Current != null && (string)c.value != Current.info.header.model) Exit(restore: false);

            if (string.IsNullOrEmpty(_pendingPath)) return;
            string path = _pendingPath;
            _pendingPath = null;
            RecordingInfo info = RecordingStore.Describe(path);
            if (info == null || info.header.model != (string)c.value)
            {
                AuraSync.UI.Toast.Show("The recording's model did not open, so it cannot be replayed.");
                return;
            }
            Begin(info);
        }

        private bool Begin(RecordingInfo info)
        {
            LoadedRecording rec;
            try { rec = RecordingStore.Load(info); }
            catch (Exception ex) { Debug.LogWarning($"[Replay] Cannot load: {ex.Message}"); return false; }
            if (rec.lines.Count == 0) return false;

            if (Current != null) Exit(restore: true);
            Remember();
            Index(rec);
            Current = rec;
            T = 0f; Playing = true; Speed = 1f; FreeCamera = false;
            _force = true;
            LoadVoice(info);

            AttentionVisuals.NameResolver = NameOf;
            AuraSync.UI.SpaceSessionHUD.Instance?.SetRailVisible(false);
            AuraSync.UI.ModelPartsDrawerUI.Instance?.SetOpen(false);

            Debug.Log($"[Replay] '{info.name}': {rec.lines.Count} events over {rec.Duration:0.0} s.");
            OnChanged?.Invoke();
            return true;
        }

        private void Index(LoadedRecording rec)
        {
            _byKind.Clear(); _views.Clear(); _captions.Clear(); _applied.Clear();
            _notesApplied = -2;
            foreach (RecLine l in rec.lines)
            {
                if (l.k == Kinds.View)
                {
                    if (!_views.TryGetValue(l.a, out var v)) _views[l.a] = v = new List<RecLine>();
                    v.Add(l);
                    continue;
                }
                if (!_byKind.TryGetValue(l.k, out var list)) _byKind[l.k] = list = new List<RecLine>();
                list.Add(l);
            }
            _noteEvents = rec.lines.Where(l => l.k == Kinds.Note || l.k == Kinds.NoteGone).ToList();
            BuildCaptions(rec);
        }

        // ── Controls ───────────────────────────────────────────────────────────

        public void TogglePlay()
        {
            if (Current == null) return;
            if (!Playing && T >= Current.Duration - 0.05f) T = 0f; // replay from the start
            Playing = !Playing;
            _force = true;
            OnChanged?.Invoke();
        }

        public void CycleSpeed()
        {
            int i = Array.IndexOf(Speeds, Speed);
            Speed = Speeds[(i + 1) % Speeds.Length];
            _force = true;
            OnChanged?.Invoke();
        }

        public void Seek(float seconds)
        {
            if (Current == null) return;
            T = Mathf.Clamp(seconds, 0f, Current.Duration);
            _force = true;
        }

        public void SetFreeCamera(bool free)
        {
            FreeCamera = free;
            OnChanged?.Invoke();
        }

        /// <summary>Ends the replay; restore puts the model back as it was before it started.</summary>
        public void Exit(bool restore = true)
        {
            if (Current == null) return;
            Current = null;
            Playing = false;
            UnloadVoice();

            AttentionSync.Instance?.ClearReplay();
            AttentionVisuals.NameResolver = null;
            PartNoteManager.SetReplayNotes(null);
            AuraSync.UI.SpaceSessionHUD.Instance?.SetRailVisible(true);
            if (restore) Restore();

            Debug.Log("[Replay] Ended.");
            OnChanged?.Invoke();
        }

        // ── Every frame ────────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (Current == null) return;
            if (PhotonNetwork.InRoom) { Exit(); return; } // joined a room: the live session wins

            if (Playing)
            {
                T += Time.unscaledDeltaTime * Speed;
                if (T >= Current.Duration) { T = Current.Duration; Playing = false; _force = true; OnChanged?.Invoke(); }
            }
            ApplyAt(T, _force);
            SyncVoice(_force);
            _force = false;
        }

        // ── Voice ──────────────────────────────────────────────────────────────

        private void LoadVoice(RecordingInfo info)
        {
            UnloadVoice();
            _voiceData = info.HasVoice ? VoiceTrack.ReadSamples(info.VoicePath) : null;
            if (_voiceData == null || _voiceData.Length == 0) { _voiceData = null; return; }

            _voiceClip = AudioClip.Create("ReplayVoice", _voiceData.Length, 1, VoiceTrack.Rate, true, OnVoiceRead, OnVoiceSeek);
            if (_voiceSource == null)
            {
                _voiceSource = gameObject.AddComponent<AudioSource>();
                _voiceSource.playOnAwake = false;
                _voiceSource.spatialBlend = 0f; // plain, not placed in 3D
            }
            _voiceSource.clip = _voiceClip;
        }

        private void UnloadVoice()
        {
            if (_voiceSource != null) { _voiceSource.Stop(); _voiceSource.clip = null; }
            if (_voiceClip != null) Destroy(_voiceClip);
            _voiceClip = null;
            _voiceData = null;
        }

        // Audio thread: decode on demand, so a long recording costs one byte a sample in memory.
        private void OnVoiceRead(float[] buf)
        {
            byte[] data = _voiceData;
            int p = _voiceRead;
            for (int i = 0; i < buf.Length; i++, p++)
                buf[i] = data != null && p < data.Length ? VoiceTrack.Decode(data[p]) : 0f;
            _voiceRead = p;
        }

        private void OnVoiceSeek(int position) => _voiceRead = position;

        /// <summary>Plays, pauses and re-aligns the voice with the replay's clock.</summary>
        private void SyncVoice(bool force)
        {
            if (_voiceSource == null || _voiceClip == null) return;
            float length = _voiceData.Length / (float)VoiceTrack.Rate;
            bool should = Playing && T < length;
            _voiceSource.pitch = Speed;

            if (!should) { if (_voiceSource.isPlaying) _voiceSource.Pause(); return; }

            int target = Mathf.Clamp(Mathf.RoundToInt(T * VoiceTrack.Rate), 0, _voiceData.Length - 1);
            if (!_voiceSource.isPlaying)
            {
                _voiceSource.timeSamples = target;
                _voiceSource.Play();
            }
            else if (force || Mathf.Abs(_voiceSource.timeSamples - target) > VoiceTrack.Rate / 4)
            {
                _voiceSource.timeSamples = target; // drifted more than a quarter second, or scrubbed
            }
        }

        private void ApplyAt(float t, bool force)
        {
            if (!FreeCamera) ApplyPose(t);

            if (Changed(Kinds.Explode, t, force, out RecLine ex) && ex.f != null && ex.f.Length > 0)
                ModelExploder.Instance?.ApplyQuietly(ex.f[0]);
            if (Changed(Kinds.Isolate, t, force, out RecLine iso))
                ModelIsolator.Instance?.ApplyQuietly(iso.s);
            if (Changed(Kinds.Select, t, force, out RecLine sel))
                ModelPartSelector.Instance?.RestoreSelection(sel.s);
            if (Changed(Kinds.Section, t, force, out RecLine sec))
                SectionCutter.Instance?.ApplyQuietly(SectionCutter.CutState.Unpack(sec.f));
            if (Changed(Kinds.Sim, t, force, out RecLine sim))
                ApplySim(sim, t);

            ApplyNotes(t);
            ApplyAttention(t);
            Speaking = SpeakingAt(t);
            Caption = CaptionAt(t);
        }

        /// <summary>The last line of a kind at or before t; true when it differs from the one applied (or forced).</summary>
        private bool Changed(string kind, float t, bool force, out RecLine line)
        {
            line = null;
            if (!_byKind.TryGetValue(kind, out var list)) return false;
            int i = LastAtOrBefore(list, t);
            if (i < 0) return false;
            line = list[i];
            if (!force && _applied.TryGetValue(kind, out int was) && was == i) return false;
            _applied[kind] = i;
            return true;
        }

        private static int LastAtOrBefore(List<RecLine> list, float t)
        {
            int lo = 0, hi = list.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].t <= t) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best;
        }

        private void ApplyPose(float t)
        {
            if (!_byKind.TryGetValue(Kinds.Pose, out var list)) return;
            int i = LastAtOrBefore(list, t);
            if (i < 0) i = 0;
            RecLine a = list[i];
            if (a.f == null || a.f.Length < 10) return;

            Vector3 pos = new Vector3(a.f[0], a.f[1], a.f[2]);
            Quaternion rot = new Quaternion(a.f[3], a.f[4], a.f[5], a.f[6]);
            Vector3 scale = new Vector3(a.f[7], a.f[8], a.f[9]);

            // Glide between samples (they are up to 0.1 s apart while the model moves).
            if (i + 1 < list.Count && list[i + 1].f != null && list[i + 1].f.Length >= 10 && list[i + 1].t - a.t < 0.6f)
            {
                RecLine b = list[i + 1];
                float k = Mathf.InverseLerp(a.t, b.t, t);
                pos   = Vector3.Lerp(pos, new Vector3(b.f[0], b.f[1], b.f[2]), k);
                rot   = Quaternion.Slerp(rot, new Quaternion(b.f[3], b.f[4], b.f[5], b.f[6]), k);
                scale = Vector3.Lerp(scale, new Vector3(b.f[7], b.f[8], b.f[9]), k);
            }
            SpaceInteractionController.Instance?.RestorePose(pos, rot, scale);
        }

        /// <summary>
        /// The recorded simulation at replay time t, played on this phone's clock at the replay's
        /// speed: the phase is the recorded one plus the time since, so seeking lands mid-animation.
        /// </summary>
        private void ApplySim(RecLine line, float t)
        {
            SimulationController ctrl = SimulationController.Instance;
            if (ctrl == null || line.f == null || line.f.Length < 6) return;

            var s = new SimState
            {
                mode = (SimMode)Mathf.RoundToInt(line.f[0]), playing = line.f[1] > 0.5f, speed = line.f[2],
                value = line.f[3], basePhase = line.f[4], assemble = line.f[5] > 0.5f,
            };
            if (s.mode == SimMode.None) { ctrl.ApplyReplay(SimState.Off); return; }

            // Start the mode first: the step count and the House's day cycle are only known once
            // its player has begun, and the phase below is worked out from them.
            if (ctrl.State.mode != s.mode) { SimState first = s; first.playing = false; ctrl.ApplyReplay(first); }

            if (s.playing)
            {
                // Where it has got to by t, then on from here at speed × replay speed (or held, when paused).
                s.start = SimulationController.Now - Mathf.RoundToInt((t - line.t) * 1000f);
                s = ctrl.Rebased(s);
                s.playing = Playing;
                s.speed  *= Speed;
            }
            ctrl.ApplyReplay(s);
        }

        private void ApplyNotes(float t)
        {
            int i = LastAtOrBefore(_noteEvents, t);
            if (i == _notesApplied) return;
            _notesApplied = i;

            var byId = new Dictionary<string, PartNote>();
            for (int j = 0; j <= i; j++)
            {
                RecLine l = _noteEvents[j];
                if (l.k == Kinds.NoteGone) { byId.Remove(l.s); continue; }
                PartNote n;
                try { n = JsonUtility.FromJson<PartNote>(l.s); } catch { continue; }
                if (n == null) continue;
                n.authorActor = l.a;
                byId[n.id] = n;
            }
            PartNoteManager.SetReplayNotes(byId.Values.OrderBy(n => n.created).ToList());
        }

        private void ApplyAttention(float t)
        {
            var views = new List<AttentionSample>();
            foreach (var kv in _views)
            {
                int i = LastAtOrBefore(kv.Value, t);
                if (i < 0 || t - kv.Value[i].t > ViewLife || Left(kv.Key, kv.Value[i].t, t)) continue;
                RecLine l = kv.Value[i];
                if (l.f == null || l.f.Length < 9) continue;
                views.Add(new AttentionSample
                {
                    actor = l.a, path = l.s,
                    point = new Vector3(l.f[0], l.f[1], l.f[2]), normal = new Vector3(l.f[3], l.f[4], l.f[5]),
                    dir = new Vector3(l.f[6], l.f[7], l.f[8]).normalized, distance = l.f.Length >= 10 ? l.f[9] : 0f,
                });
            }

            var pings = new List<AttentionPing>();
            if (_byKind.TryGetValue(Kinds.Ping, out var plist))
            {
                for (int i = LastAtOrBefore(plist, t); i >= 0 && t - plist[i].t <= AttentionSync.PingSeconds; i--)
                {
                    RecLine l = plist[i];
                    if (l.f == null || l.f.Length < 6 || pings.Any(p => p.actor == l.a)) continue;
                    pings.Add(new AttentionPing
                    {
                        actor = l.a, path = l.s,
                        point = new Vector3(l.f[0], l.f[1], l.f[2]), normal = new Vector3(l.f[3], l.f[4], l.f[5]),
                        startTime = Time.unscaledTime - (t - l.t), // its age in replay time: it fades as it ends
                    });
                }
            }
            AttentionSync.Instance?.SetReplay(views, pings);
        }

        /// <summary>True if the actor left between two moments (their last view must not linger).</summary>
        private bool Left(int actor, float from, float to)
        {
            if (!_byKind.TryGetValue(Kinds.Leave, out var list)) return false;
            foreach (RecLine l in list) if (l.a == actor && l.t >= from && l.t <= to) return true;
            return false;
        }

        private string SpeakingAt(float t)
        {
            if (!_byKind.TryGetValue(Kinds.Speak, out var list)) return "";
            var on = new Dictionary<int, bool>();
            for (int i = 0; i < list.Count && list[i].t <= t; i++)
                on[list[i].a] = list[i].f != null && list[i].f.Length > 0 && list[i].f[0] > 0.5f;
            var names = on.Where(kv => kv.Value).Select(kv => NameOf(kv.Key)).ToList();
            return names.Count > 0 ? string.Join(", ", names) : "";
        }

        // ── Captions ───────────────────────────────────────────────────────────

        private void BuildCaptions(LoadedRecording rec)
        {
            var notesSeen = new HashSet<string>();
            string lastIso = null; bool lastCut = false; int lastSim = 0;
            foreach (RecLine l in rec.lines)
            {
                string c = null;
                bool start = l.t < 0.05f;
                switch (l.k)
                {
                    case Kinds.Ping:  c = $"{NameOf(l.a)}: look here"; break;
                    case Kinds.Join:  if (!start) c = $"{l.s} joined"; break;
                    case Kinds.Leave: c = $"{NameOf(l.a)} left"; break;
                    case Kinds.Find:  c = "FIND: " + l.s; break;
                    case Kinds.NoteGone: if (!start) c = "A note was removed"; break;
                    case Kinds.Note:
                        PartNote n = null;
                        try { n = JsonUtility.FromJson<PartNote>(l.s); } catch { /* skip */ }
                        if (n == null) break;
                        bool isNew = notesSeen.Add(n.id);
                        if (!start) c = isNew ? $"{NameOf(l.a, n.authorName)} added a note: {Short(n.text)}" : $"{NameOf(l.a, n.authorName)} edited a note";
                        break;
                    case Kinds.Answer:
                        SharedAnswerLine a = null;
                        try { a = JsonUtility.FromJson<SharedAnswerLine>(l.s); } catch { /* skip */ }
                        if (a != null && !start) c = $"{a.asker} shared an AI answer on {a.part}: {Short(a.question)}";
                        break;
                    case Kinds.Isolate:
                        if (lastIso != null && l.s != lastIso)
                            c = string.IsNullOrEmpty(l.s) ? "Whole model shown" : "Isolated: " + PartLabel(l.s);
                        lastIso = l.s;
                        break;
                    case Kinds.Section:
                        bool cut = l.f != null && l.f.Length > 0 && l.f[0] > 0.5f;
                        if (!start && cut != lastCut) c = cut ? "Section cut on" : "Section cut off";
                        lastCut = cut;
                        break;
                    case Kinds.Sim:
                        int mode = l.f != null && l.f.Length > 0 ? Mathf.RoundToInt(l.f[0]) : 0;
                        if (!start && mode != lastSim) c = mode == 1 ? "Simulation: assembly" : mode == 2 ? "Simulation: working" : "Simulation stopped";
                        lastSim = mode;
                        break;
                }
                if (c != null) _captions.Add(new KeyValuePair<float, string>(l.t, c));
            }
        }

        private string CaptionAt(float t)
        {
            for (int i = _captions.Count - 1; i >= 0; i--)
            {
                if (_captions[i].Key > t) continue;
                return t - _captions[i].Key <= CaptionSeconds ? $"{RecordingStore.Clock(_captions[i].Key)}  {_captions[i].Value}" : "";
            }
            return "";
        }

        private static string Short(string s)
        {
            s = (s ?? "").Replace("\n", " ").Trim();
            return s.Length > 60 ? s.Substring(0, 60).TrimEnd() + "..." : s;
        }

        private static string PartLabel(string path) => PartNames.Display(path, path);

        private string NameOf(int actor) => NameOf(actor, null);

        private string NameOf(int actor, string fallback)
        {
            if (Current != null && Current.names.TryGetValue(actor, out string n) && !string.IsNullOrEmpty(n)) return n;
            return !string.IsNullOrEmpty(fallback) ? fallback : $"Player {actor}";
        }

        // ── Before and after ───────────────────────────────────────────────────

        private void Remember()
        {
            _was_explode = ModelExploder.Instance != null ? ModelExploder.Instance.Amount : 0f;
            _was_isolate = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "";
            _was_select  = ModelPartSelector.Instance != null ? ModelPartSelector.Instance.LocalPath : "";
            _was_section = SectionCutter.Instance != null ? SectionCutter.Instance.State : SectionCutter.CutState.Off;
            _was_sim     = SimulationController.Instance != null ? SimulationController.Instance.Rebased(SimulationController.Instance.State) : SimState.Off;
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            _was_pose = root != null;
            if (root != null) { _was_pos = root.position; _was_rot = root.rotation; _was_scale = root.localScale; }
        }

        private void Restore()
        {
            SimulationController.Instance?.ApplyReplay(_was_sim.mode == SimMode.None ? SimState.Off : _was_sim);
            SectionCutter.Instance?.ApplyQuietly(_was_section);
            ModelExploder.Instance?.ApplyQuietly(_was_explode);
            ModelIsolator.Instance?.ApplyQuietly(_was_isolate);
            ModelPartSelector.Instance?.RestoreSelection(_was_select);
            if (_was_pose) SpaceInteractionController.Instance?.RestorePose(_was_pos, _was_rot, _was_scale);
        }
    }
}
