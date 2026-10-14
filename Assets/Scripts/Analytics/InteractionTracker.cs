using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Networking;
using AuraSync.Recording;
using AuraSync.Space;

namespace AuraSync.Analytics
{
    /// <summary>How much attention one part got from one person (or from everyone, summed).</summary>
    [Serializable]
    public class PartStats
    {
        public string path = "";
        public int    picks;       // selected it (tap or the parts list)
        public float  selectSec;   // seconds it was the selected part
        public float  isolateSec;  // seconds it was isolated
        public float  viewSec;     // seconds it was at the centre of the screen
        public int    notes;       // notes added on it
        public int    questions;   // AI questions asked about it

        /// <summary>
        /// One number for the heatmap. Deliberate actions weigh most (a pick 3, a note or a
        /// question 5); time counts a little per second (selected or isolated 0.2, looked at 0.1),
        /// so a part that was only on screen still warms up, but less than one people worked with.
        /// </summary>
        public float Score => picks * 3f + notes * 5f + questions * 5f + selectSec * 0.2f + isolateSec * 0.2f + viewSec * 0.1f;

        public void Add(PartStats o)
        {
            picks += o.picks; selectSec += o.selectSec; isolateSec += o.isolateSec; viewSec += o.viewSec;
            notes += o.notes; questions += o.questions;
        }

        public float[] Pack() => new[] { picks, selectSec, isolateSec, viewSec, notes, (float)questions };

        public static PartStats Unpack(string path, float[] f, int at) => new PartStats
        {
            path = path, picks = Mathf.RoundToInt(f[at]), selectSec = f[at + 1], isolateSec = f[at + 2],
            viewSec = f[at + 3], notes = Mathf.RoundToInt(f[at + 4]), questions = Mathf.RoundToInt(f[at + 5]),
        };
    }

    /// <summary>One person's counts on the current model.</summary>
    public class PersonStats
    {
        public int    actor;
        public string name = "";
        public bool   present = true;
        public readonly Dictionary<string, PartStats> parts = new Dictionary<string, PartStats>();

        public PartStats Get(string path)
        {
            if (!parts.TryGetValue(path, out PartStats s)) parts[path] = s = new PartStats { path = path };
            return s;
        }
    }

    /// <summary>
    /// Interaction counts per part (V3 Phase 10), the data behind the heatmap.
    ///
    /// Every phone counts, for each part of the model on screen: picks (selecting it), seconds it
    /// was selected, seconds isolated, seconds at the centre of the screen (the same "where you
    /// look" as the attention cones), notes added and AI questions asked. Every 10 s a phone in a
    /// room sends its totals to the presenter (event 27), who keeps everyone's, including people
    /// who have since left. Counting starts again on another model; a replay is not counted.
    /// </summary>
    public class InteractionTracker : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static InteractionTracker Instance { get; private set; }

        private const float Tick = 0.5f;
        private const float SendEvery = 10f;

        /// <summary>Everyone's counts on this model, by actor (alone: just this phone, actor 0).</summary>
        private readonly Dictionary<int, PersonStats> _people = new Dictionary<int, PersonStats>();
        private string _modelHash = "";
        private float _tick, _send;

        public static event Action OnChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnBus;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnBus;
            if (Instance == this) Instance = null;
        }

        private static int Me => PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : 0;

        private PersonStats Mine
        {
            get
            {
                int me = Me;
                if (!_people.TryGetValue(me, out PersonStats p)) _people[me] = p = new PersonStats { actor = me };
                p.name = PhotonNetwork.InRoom ? NetworkManager.DisplayName(PhotonNetwork.LocalPlayer) : "You";
                return p;
            }
        }

        private static bool Counting => AttentionSync.ModelShown && !SessionPlayer.IsReplaying;

        // ── Counting on this phone ─────────────────────────────────────────────

        private void OnBus(StateChange c)
        {
            switch (c.key)
            {
                case StateKeys.ModelLoaded:
                    string hash = ModelStateMemory.ModelHash(c.value as string ?? "");
                    if (hash != _modelHash) { _modelHash = hash; _people.Clear(); OnChanged?.Invoke(); }
                    break;

                case StateKeys.LocalSelection when c.isLocal:
                case StateKeys.PresenterSelection when c.isLocal:
                    if (Counting && c.value is string sel && sel.Length > 0) Mine.Get(sel).picks++;
                    break;

                case StateKeys.NoteAdded:
                    if (Counting && c.value is string np && np.Length > 0) Mine.Get(np).notes++;
                    break;

                case StateKeys.AiQuestion:
                    if (Counting && c.value is string qp && qp.Length > 0) Mine.Get(qp).questions++;
                    break;
            }
        }

        void Update()
        {
            _tick += Time.unscaledDeltaTime;
            if (_tick >= Tick)
            {
                float dt = _tick;
                _tick = 0f;
                if (Counting) CountSeconds(dt);
            }

            _send += Time.unscaledDeltaTime;
            if (_send >= SendEvery)
            {
                _send = 0f;
                SendToPresenter();
            }
        }

        private void CountSeconds(float dt)
        {
            PersonStats me = Mine;
            ModelPartSelector sel = ModelPartSelector.Instance;
            if (sel != null)
            {
                string shown = PhotonNetwork.InRoom && !string.IsNullOrEmpty(sel.PresenterPath) ? sel.PresenterPath : sel.LocalPath;
                if (!string.IsNullOrEmpty(shown)) me.Get(shown).selectSec += dt;
            }

            string iso = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "";
            if (!string.IsNullOrEmpty(iso)) me.Get(iso).isolateSec += dt;

            // Where this person is looking: the spot at the centre of the screen, on a part.
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Camera cam = Camera.main;
            if (root != null && cam != null &&
                AttentionSync.SpotAt(cam, root, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false,
                                     out string path, out _, out _) && !string.IsNullOrEmpty(path))
                me.Get(path).viewSec += dt;
        }

        // ── The room ───────────────────────────────────────────────────────────

        private void SendToPresenter()
        {
            if (!PhotonNetwork.InRoom || AttentionSync.LocalIsPresenter || NetworkManager.Instance == null) return;
            int presenter = NetworkManager.Instance.AdminActorNumber;
            if (presenter <= 0 || string.IsNullOrEmpty(_modelHash)) return;
            if (!_people.TryGetValue(Me, out PersonStats me) || me.parts.Count == 0) return;

            var paths = new string[me.parts.Count];
            var values = new float[me.parts.Count * 6];
            int i = 0;
            foreach (PartStats s in me.parts.Values)
            {
                paths[i] = s.path;
                Array.Copy(s.Pack(), 0, values, i * 6, 6);
                i++;
            }
            PhotonNetwork.RaiseEvent(EventCodes.InteractionCounts, new object[] { _modelHash, paths, values },
                new RaiseEventOptions { TargetActors = new[] { presenter } }, SendOptions.SendReliable);
        }

        public void OnEvent(EventData e)
        {
            if (e.Code != EventCodes.InteractionCounts) return;
            if (!(e.CustomData is object[] d) || d.Length < 3) return;
            if (!(d[0] is string hash) || hash != _modelHash || !(d[1] is string[] paths) || !(d[2] is float[] values)) return;
            if (values.Length < paths.Length * 6) return;

            // Totals, not increments: the latest message replaces the last.
            var p = new PersonStats { actor = e.Sender };
            Player pl = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(e.Sender) : null;
            p.name = pl != null ? NetworkManager.DisplayName(pl) : (_people.TryGetValue(e.Sender, out var old) ? old.name : $"Player {e.Sender}");
            for (int i = 0; i < paths.Length; i++) p.parts[paths[i]] = PartStats.Unpack(paths[i], values, i * 6);
            _people[e.Sender] = p;
            OnChanged?.Invoke();
        }

        public override void OnPlayerLeftRoom(Player other)
        {
            // A player who left still counts (their last totals are kept).
            if (_people.TryGetValue(other.ActorNumber, out PersonStats p)) p.present = false;
        }

        // ── Reading ────────────────────────────────────────────────────────────

        /// <summary>The people with counts, this phone first.</summary>
        public List<PersonStats> People()
        {
            _ = Mine; // make sure this phone is listed
            return _people.Values.OrderBy(p => p.actor == Me ? 0 : 1).ThenBy(p => p.name).ToList();
        }

        /// <summary>Counts per part for one person, or summed over everyone (actor = -1).</summary>
        public Dictionary<string, PartStats> Combined(int actor) => Combine(_people.Values, actor);

        public static Dictionary<string, PartStats> Combine(IEnumerable<PersonStats> people, int actor)
        {
            var sum = new Dictionary<string, PartStats>();
            foreach (PersonStats p in people)
            {
                if (actor >= 0 && p.actor != actor) continue;
                foreach (PartStats s in p.parts.Values)
                {
                    if (!sum.TryGetValue(s.path, out PartStats t)) sum[s.path] = t = new PartStats { path = s.path };
                    t.Add(s);
                }
            }
            return sum;
        }

        // ── From a recording ───────────────────────────────────────────────────

        /// <summary>
        /// The same counts worked out from a session recording (Phase 9): the presenter's picks,
        /// selected and isolated seconds and notes, and every viewer's looking time from their cones.
        /// </summary>
        public static List<PersonStats> FromRecording(LoadedRecording rec)
        {
            var people = new Dictionary<int, PersonStats>();
            PersonStats Person(int a)
            {
                if (!people.TryGetValue(a, out PersonStats p))
                    people[a] = p = new PersonStats { actor = a, name = rec.names.TryGetValue(a, out string n) ? n : $"Player {a}" };
                return p;
            }

            int recorder = rec.info.header.recorder;
            string sel = "", iso = ""; float selAt = 0f, isoAt = 0f;
            var lastView = new Dictionary<int, RecLine>();

            void CloseSel(float t) { if (sel.Length > 0) Person(recorder).Get(sel).selectSec += t - selAt; }
            void CloseIso(float t) { if (iso.Length > 0) Person(recorder).Get(iso).isolateSec += t - isoAt; }

            foreach (RecLine l in rec.lines)
            {
                switch (l.k)
                {
                    case Kinds.Select:
                        CloseSel(l.t);
                        sel = l.s ?? ""; selAt = l.t;
                        if (sel.Length > 0 && l.t > 0.05f) Person(recorder).Get(sel).picks++;
                        break;
                    case Kinds.Isolate:
                        CloseIso(l.t);
                        iso = l.s ?? ""; isoAt = l.t;
                        break;
                    case Kinds.View:
                        // Each view sample counts until the next one, at most 1 s (5 Hz while moving, 1 s heartbeat).
                        if (lastView.TryGetValue(l.a, out RecLine prev) && !string.IsNullOrEmpty(prev.s))
                            Person(l.a).Get(prev.s).viewSec += Mathf.Min(1f, l.t - prev.t);
                        lastView[l.a] = l;
                        break;
                    case Kinds.Note:
                        try
                        {
                            var n = JsonUtility.FromJson<AuraSync.Notes.PartNote>(l.s);
                            if (n != null && l.t > 0.05f && !Person(l.a).parts.ContainsKey("note:" + n.id))
                            {
                                Person(l.a).Get(n.path).notes++;
                                Person(l.a).parts["note:" + n.id] = new PartStats { path = "note:" + n.id }; // seen marker
                            }
                        }
                        catch { /* skip */ }
                        break;
                }
            }
            CloseSel(rec.Duration);
            CloseIso(rec.Duration);

            foreach (PersonStats p in people.Values)
                foreach (string k in p.parts.Keys.Where(k => k.StartsWith("note:")).ToList()) p.parts.Remove(k);
            return people.Values.OrderBy(p => p.actor == recorder ? 0 : 1).ThenBy(p => p.name).ToList();
        }
    }
}
