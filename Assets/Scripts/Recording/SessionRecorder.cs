using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.AI;
using AuraSync.Networking;
using AuraSync.Notes;
using AuraSync.Simulation;
using AuraSync.Space;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Recording
{
    /// <summary>
    /// Records a session on the presenter's phone (V3 Phase 9), or a solo user's (D6).
    ///
    /// It writes down **what this phone shows**, not video: the model's pose (the presenter's
    /// "camera"), explode, isolation, the highlighted part, the section cut, the simulation, notes,
    /// shared AI answers, everyone's view cones and pings, who was speaking, who joined and left,
    /// and FIND tasks. **The presenter's voice** goes into a WAV beside it (<see cref="VoiceTrack"/>). States are polled every frame and written only when they change (the pose at
    /// most 10 times a second); momentary things arrive from <see cref="SharedStateBus"/>.
    ///
    /// While it records, this phone carries the player property <see cref="PlayerKeys.Recording"/>,
    /// so everyone in the room sees "REC" (and it vanishes by itself if this phone drops out).
    /// Switching model, or leaving the 3D view, stops the recording (one recording = one model).
    /// </summary>
    public class SessionRecorder : MonoBehaviourPunCallbacks
    {
        public static SessionRecorder Instance { get; private set; }

        private const float PoseInterval  = 0.1f;
        private const float SlowInterval  = 0.2f; // speaking, players
        private const float FlushInterval = 1f;

        public bool  IsRecording => _writer != null;
        public float Elapsed => IsRecording ? Time.realtimeSinceStartup - _startTime : 0f;

        /// <summary>Recording started or stopped.</summary>
        public static event Action OnChanged;

        private StreamWriter _writer;
        private VoiceTrackWriter _voice;
        private readonly SoloMic _soloMic = new SoloMic();
        private string _file;
        private string _modelKey;
        private string _modelName;
        private float  _startTime;
        private float  _lastFlush, _lastPose, _lastSlow;

        // Last values written, so only changes are recorded
        private Vector3 _pos, _scale; private Quaternion _rot; private bool _havePose;
        private float  _explode = -1f;
        private string _isolate, _select;
        private SectionCutter.CutState _section; private bool _haveSection;
        private SimState _sim; private bool _haveSim;
        private readonly Dictionary<int, bool> _speaking = new Dictionary<int, bool>();
        private readonly Dictionary<int, string> _players = new Dictionary<int, string>();
        private readonly Dictionary<string, string> _notes = new Dictionary<string, string>();   // id -> json
        private readonly HashSet<string> _answers = new HashSet<string>();

        /// <summary>The file just stopped, waiting for its name (STOP asks for one).</summary>
        public string PendingFile { get; private set; }
        public string PendingDefaultName { get; private set; }
        private float _pendingSeconds;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnBus;
            PartNoteManager.OnChanged += OnNotesChanged;
            SharedAnswers.OnChanged += OnAnswersChanged;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnBus;
            PartNoteManager.OnChanged -= OnNotesChanged;
            SharedAnswers.OnChanged -= OnAnswersChanged;
            // Leaving the 3D view: keep what was recorded, under its default name.
            if (IsRecording) Stop(askName: false);
            FinishPending(null);
            if (Instance == this) Instance = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) _writer?.Flush(); // the app may be killed in the background
        }

        // ── Start / stop ───────────────────────────────────────────────────────

        /// <summary>Who may record: the presenter, or anyone alone (D6).</summary>
        public static bool LocalMayRecord =>
            !PhotonNetwork.InRoom || (ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin);

        public bool Begin()
        {
            if (IsRecording || SessionPlayer.IsReplaying) return false;
            string model = SpaceSceneManager.CurrentModelKey;
            SpaceInteractionController sic = SpaceInteractionController.Instance;
            if (string.IsNullOrEmpty(model) || sic == null || sic.ModelRoot == null) return false;

            FinishPending(null); // a previous STOP still waiting for its name keeps the default

            _file = RecordingStore.NewPath();
            try { _writer = new StreamWriter(_file, false, new System.Text.UTF8Encoding(false)); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recording] Cannot start: {ex.Message}");
                _writer = null;
                return false;
            }

            _modelKey  = model;
            _modelName = ModelName(model);
            _startTime = Time.realtimeSinceStartup;
            _lastFlush = _lastPose = _lastSlow = -999f;
            ResetLastValues();

            int me = PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
            var header = new RecHeader
            {
                model = model, modelName = _modelName, started = DateTime.UtcNow.ToString("o"),
                room = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "",
                recorder = me, recorderName = PhotonNetwork.InRoom ? NetworkManager.DisplayName(PhotonNetwork.LocalPlayer) : "You",
            };
            _writer.WriteLine(JsonUtility.ToJson(header));

            // The presenter's voice, beside it: Photon Voice's outgoing stream in a room, the
            // microphone directly when alone.
            try { _voice = new VoiceTrackWriter(VoiceTrack.PathFor(_file)); VoiceTrack.Sink = _voice; }
            catch (Exception ex) { Debug.LogWarning($"[Recording] No voice track: {ex.Message}"); _voice = null; }

            // Everything as it stands now, at t = 0
            if (!PhotonNetwork.InRoom) Write(Kinds.Join, a: 0, s: header.recorderName);
            PollPlayers();
            PollStates(force: true);
            OnNotesChanged();
            OnAnswersChanged();
            _writer.Flush();

            SetPlayerFlag(true);
            Debug.Log($"[Recording] Started: {_file}");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Ends the recording. With askName, the file waits in <see cref="PendingFile"/> for
        /// <see cref="FinishPending"/> (the name box); otherwise it is closed under its default name.
        /// </summary>
        public void Stop(bool askName)
        {
            if (!IsRecording) return;
            float seconds = Elapsed;
            try { _writer.Flush(); _writer.Dispose(); } catch { /* closed below */ }
            _writer = null;

            VoiceTrack.Sink = null;
            _soloMic.Stop();
            bool heard = _voice != null && _voice.Close();
            _voice = null;
            if (!heard) Debug.Log("[Recording] No voice was heard; the recording has no sound track.");
            SetPlayerFlag(false);

            DateTime started = DateTime.Now.AddSeconds(-seconds);
            PendingFile = _file;
            PendingDefaultName = RecordingStore.DefaultName(_modelName, started);
            _pendingSeconds = seconds;
            Debug.Log($"[Recording] Stopped after {seconds:0} s ({RecordingStore.SizeText(new FileInfo(_file).Length)}).");

            if (!askName) FinishPending(null);
            OnChanged?.Invoke();
        }

        /// <summary>Writes the "end" line with the chosen name (null or blank = the default).</summary>
        public void FinishPending(string name)
        {
            if (string.IsNullOrEmpty(PendingFile)) return;
            string file = PendingFile;
            PendingFile = null;
            name = string.IsNullOrWhiteSpace(name) ? PendingDefaultName : name.Trim();
            try
            {
                var end = new RecLine { t = _pendingSeconds, k = Kinds.End, s = name, f = new[] { _pendingSeconds } };
                File.AppendAllText(file, JsonUtility.ToJson(end) + "\n");
            }
            catch (Exception ex) { Debug.LogWarning($"[Recording] Could not finish {Path.GetFileName(file)}: {ex.Message}"); }
            OnChanged?.Invoke();
        }

        private static void SetPlayerFlag(bool on)
        {
            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PlayerKeys.Recording, on ? (object)true : null } });
        }

        /// <summary>The first player in the room who is recording, or null.</summary>
        public static Player RecordingPlayer()
        {
            if (!PhotonNetwork.InRoom) return null;
            foreach (Player p in PhotonNetwork.PlayerList)
                if (p.CustomProperties.TryGetValue(PlayerKeys.Recording, out object v) && v is bool b && b) return p;
            return null;
        }

        private static string ModelName(string key)
        {
            string n = Path.GetFileNameWithoutExtension(key ?? "");
            int q = n.IndexOf('?');
            return q > 0 ? n.Substring(0, q) : n;
        }

        // ── Every frame ────────────────────────────────────────────────────────

        void Update()
        {
            if (!IsRecording) return;

            // A different model on screen (or none): this recording is over.
            if (SpaceSceneManager.CurrentModelKey != _modelKey)
            {
                Stop(askName: false);
                AuraSync.UI.Toast.Show("Recording stopped: the model changed. It is saved under Settings > Recordings.");
                return;
            }

            float now = Time.realtimeSinceStartup;
            PollStates(force: false);
            TickVoice();
            if (now - _lastSlow >= SlowInterval) { _lastSlow = now; PollSpeaking(); PollPlayers(); }
            if (now - _lastFlush >= FlushInterval) { _lastFlush = now; _writer.Flush(); _voice?.Flush(); }
        }

        /// <summary>Feeds the voice track: the solo microphone when alone, Photon Voice's tap in a room.</summary>
        private void TickVoice()
        {
            if (_voice == null) return;
            VoiceChatManager vc = VoiceChatManager.Instance;
            VoiceTrack.Muted = PhotonNetwork.InRoom && vc != null && vc.IsMuted;

            if (PhotonNetwork.InRoom) { if (_soloMic.Live) _soloMic.Stop(); } // the room's voice owns the mic now
            else _soloMic.Tick(_voice);

            _voice.Drain();
        }

        /// <summary>True when this recording is keeping sound right now (for the REC hint).</summary>
        public bool VoiceLive => _voice != null && (PhotonNetwork.InRoom
            ? VoiceChatManager.Instance != null && VoiceChatManager.Instance.UsingPhotonVoice && !VoiceChatManager.Instance.IsMuted
            : _soloMic.Live);

        private float T => Time.realtimeSinceStartup - _startTime;

        private void Write(string kind, int a = 0, string s = "", float[] f = null)
        {
            if (_writer == null) return;
            var line = new RecLine { t = (float)Math.Round(T, 3), k = kind, a = a, s = s ?? "", f = f };
            try { _writer.WriteLine(JsonUtility.ToJson(line)); }
            catch (Exception ex) { Debug.LogWarning($"[Recording] Write failed, stopping: {ex.Message}"); Stop(askName: false); }
        }

        private void ResetLastValues()
        {
            _havePose = _haveSection = _haveSim = false;
            _explode = -1f;
            _isolate = _select = null;
            _speaking.Clear(); _players.Clear(); _notes.Clear(); _answers.Clear();
        }

        private void PollStates(bool force)
        {
            float now = Time.realtimeSinceStartup;
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;

            if (root != null && (force || now - _lastPose >= PoseInterval))
            {
                bool moved = !_havePose || (root.position - _pos).sqrMagnitude > 1e-8f ||
                             (root.localScale - _scale).sqrMagnitude > 1e-8f || Quaternion.Angle(root.rotation, _rot) > 0.2f;
                if (moved || force)
                {
                    _lastPose = now;
                    _pos = root.position; _rot = root.rotation; _scale = root.localScale; _havePose = true;
                    Write(Kinds.Pose, f: new[] { _pos.x, _pos.y, _pos.z, _rot.x, _rot.y, _rot.z, _rot.w, _scale.x, _scale.y, _scale.z });
                }
            }

            float explode = ModelExploder.Instance != null ? ModelExploder.Instance.Amount : 0f;
            if (force || Mathf.Abs(explode - _explode) > 0.002f) { _explode = explode; Write(Kinds.Explode, f: new[] { explode }); }

            string iso = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "";
            if (force || iso != _isolate) { _isolate = iso; Write(Kinds.Isolate, s: iso); }

            string sel = SelectedOnScreen();
            if (force || sel != _select) { _select = sel; Write(Kinds.Select, s: sel); }

            if (SectionCutter.Instance != null)
            {
                SectionCutter.CutState c = SectionCutter.Instance.State;
                if (force || !_haveSection || !c.SameAs(_section))
                {
                    _section = c; _haveSection = true;
                    Write(Kinds.Section, f: c.Pack());
                }
            }

            SimulationController sim = SimulationController.Instance;
            if (sim != null)
            {
                SimState s = sim.State;
                if (force || !_haveSim || !s.SameAs(_sim))
                {
                    _sim = s; _haveSim = true;
                    SimState now0 = sim.Rebased(s); // its phase at this moment
                    Write(Kinds.Sim, f: new[] { (float)s.mode, s.playing ? 1f : 0f, s.speed, s.value, now0.basePhase, s.assemble ? 1f : 0f });
                }
            }
        }

        /// <summary>The part highlighted on this phone: the room's pick, or this phone's own.</summary>
        private static string SelectedOnScreen()
        {
            ModelPartSelector sel = ModelPartSelector.Instance;
            if (sel == null) return "";
            if (PhotonNetwork.InRoom && !string.IsNullOrEmpty(sel.PresenterPath)) return sel.PresenterPath;
            return sel.LocalPath ?? "";
        }

        private void PollSpeaking()
        {
            VoiceChatManager voice = VoiceChatManager.Instance;
            if (voice == null || !PhotonNetwork.InRoom) return;
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                bool now = voice.IsSpeaking(p.ActorNumber);
                _speaking.TryGetValue(p.ActorNumber, out bool was);
                if (now == was) continue;
                _speaking[p.ActorNumber] = now;
                Write(Kinds.Speak, a: p.ActorNumber, f: new[] { now ? 1f : 0f });
            }
        }

        private void PollPlayers()
        {
            if (!PhotonNetwork.InRoom) return;
            var present = new HashSet<int>();
            foreach (Player p in PhotonNetwork.PlayerList)
            {
                present.Add(p.ActorNumber);
                string name = NetworkManager.DisplayName(p);
                if (_players.TryGetValue(p.ActorNumber, out string known) && known == name) continue;
                _players[p.ActorNumber] = name;
                Write(Kinds.Join, a: p.ActorNumber, s: name);
            }
            foreach (int gone in _players.Keys.Where(a => !present.Contains(a)).ToList())
            {
                _players.Remove(gone);
                _speaking.Remove(gone);
                Write(Kinds.Leave, a: gone);
            }
        }

        // ── Things that happen ─────────────────────────────────────────────────

        private void OnBus(StateChange c)
        {
            if (!IsRecording) return;
            switch (c.key)
            {
                case StateKeys.AttentionView when !c.isLocal && c.value is AttentionSample v:
                    Write(Kinds.View, a: v.actor, s: v.path, f: new[]
                    {
                        v.point.x, v.point.y, v.point.z, v.normal.x, v.normal.y, v.normal.z,
                        v.dir.x, v.dir.y, v.dir.z, v.distance,
                    });
                    break;

                case StateKeys.AttentionPing when c.value is AttentionPing p:
                    Write(Kinds.Ping, a: p.actor, s: p.path,
                          f: new[] { p.point.x, p.point.y, p.point.z, p.normal.x, p.normal.y, p.normal.z });
                    break;

                case StateKeys.FindTask when c.value is string text:
                    Write(Kinds.Find, s: text);
                    break;
            }
        }

        private void OnNotesChanged()
        {
            if (!IsRecording || PartNoteManager.Instance == null) return;
            var seen = new HashSet<string>();
            foreach (PartNote n in PartNoteManager.Instance.All())
            {
                seen.Add(n.id);
                string json = JsonUtility.ToJson(n);
                if (_notes.TryGetValue(n.id, out string old) && old == json) continue;
                _notes[n.id] = json;
                Write(Kinds.Note, a: n.authorActor, s: json);
            }
            foreach (string gone in _notes.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _notes.Remove(gone);
                Write(Kinds.NoteGone, s: gone);
            }
        }

        private void OnAnswersChanged()
        {
            if (!IsRecording) return;
            var seen = new HashSet<string>();
            foreach (SharedAnswer a in SharedAnswers.ForModel(_modelKey))
            {
                seen.Add(a.id);
                if (!_answers.Add(a.id)) continue;
                var line = new SharedAnswerLine { id = a.id, path = a.path, part = a.part, question = a.question, answer = a.answer, asker = a.askerName };
                Write(Kinds.Answer, a: a.askerActor, s: JsonUtility.ToJson(line));
            }
            foreach (string gone in _answers.Where(id => !seen.Contains(id)).ToList())
            {
                _answers.Remove(gone);
                Write(Kinds.AnswerGone, s: gone);
            }
        }

        public override void OnJoinedRoom()
        {
            if (IsRecording) SetPlayerFlag(true); // a solo recording carries on into the room
        }
    }
}
