using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Space;

namespace AuraSync.Networking
{
    /// <summary>
    /// The FIND task (V3 Phase 8): the attention user study's measuring tool.
    ///
    /// The presenter picks a part and starts the task with cones ON or OFF. Everyone else sees
    /// "Find: Fan rotor" and taps the part **on the model** (the parts drawer and search do not
    /// count). Each person's time from seeing the prompt to tapping it, and their wrong taps, go back
    /// to the presenter and into <see cref="AttentionLog"/>. With cones OFF, cones and pings are
    /// hidden on the searchers' phones for the task, so the two conditions can be compared.
    ///
    /// At the start each searcher stops following the presenter (otherwise the presenter turning
    /// the model would give the part away) and the presenter's shared selection is cleared (its
    /// highlight would too). The task ends when everyone has found it, after two minutes, or on END.
    ///
    /// Messages (event 30, reliable): start → everyone else; found → the presenter; end → everyone else.
    /// </summary>
    public class FindTask : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static FindTask Instance { get; private set; }

        public const float TimeLimit = 120f;
        private const byte KindStart = 1, KindFound = 2, KindEnd = 3;

        /// <summary>Anything changed: a task started, someone found it, it ended.</summary>
        public static event Action OnChanged;

        /// <summary>True on a searcher's phone while a cones-OFF task runs: no cones, no pings.</summary>
        public static bool SuppressAttention =>
            Instance != null && Instance.Searching != null && Instance.Searching.Running && Instance.Searching.conesOff;

        // ── Presenter side ─────────────────────────────────────────────────────

        public class Result
        {
            public int    actor;
            public string name;
            public bool   done;
            public bool   found;
            public float  seconds;
            public int    wrongTaps;
            public bool   conesShown;
            public string note = ""; // "left", "time up", "ended"
        }

        public class Hosted
        {
            public int    id;
            public string path, label, modelHash;
            public bool   conesOff;
            public float  startedAt;
            public bool   running;
            public float  endedAt;
            public int    pings;
            public readonly List<Result> results = new List<Result>();
            public float Elapsed => (running ? Time.unscaledTime : endedAt) - startedAt;
        }

        /// <summary>The task this phone runs as presenter (also kept after it ends, for the results).</summary>
        public Hosted Hosting { get; private set; }

        // ── Searcher side ──────────────────────────────────────────────────────

        public class Assigned
        {
            public int    id, owner;
            public string path, label, modelHash;
            public bool   conesOff;
            public float  receivedAt;
            public int    wrongTaps;
            public bool   found;
            public float  foundSeconds;
            public bool   ended;
            public float  endedAt;
            public bool Running => !found && !ended;
            public float Elapsed => (found ? receivedAt + foundSeconds : ended ? endedAt : Time.unscaledTime) - receivedAt;
        }

        /// <summary>The task this phone is searching in (kept a few seconds after it ends, for the banner).</summary>
        public Assigned Searching { get; private set; }

        private SpaceInteractionController _sic;

        // ── Lifetime ───────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnStateChange;
            AttentionSync.OnPing += OnPing;
        }

        void Start() => StartCoroutine(BindTap());

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnStateChange;
            AttentionSync.OnPing -= OnPing;
            if (_sic != null) _sic.OnTap -= OnTap;
            if (Instance == this) Instance = null;
        }

        private System.Collections.IEnumerator BindTap()
        {
            while (SpaceInteractionController.Instance == null) yield return null;
            _sic = SpaceInteractionController.Instance;
            _sic.OnTap -= OnTap;
            _sic.OnTap += OnTap;
        }

        private void OnStateChange(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;
            // A different model: a task on the old one cannot go on.
            if (Hosting != null && Hosting.running && Hosting.modelHash != AttentionSync.CurrentModelHash) End("model changed");
            if (Searching != null && Searching.Running && Searching.modelHash != AttentionSync.CurrentModelHash) EndSearch();
        }

        private void OnPing(AttentionPing p)
        {
            if (Hosting != null && Hosting.running) Hosting.pings++;
        }

        void Update()
        {
            if (Hosting != null && Hosting.running && Hosting.Elapsed >= TimeLimit) End("time up");

            // The presenter's end message never came (they dropped out): give up a little later.
            if (Searching != null && Searching.Running && Time.unscaledTime - Searching.receivedAt > TimeLimit + 5f) EndSearch();
        }

        // ── Presenter: start and end ───────────────────────────────────────────

        /// <summary>Everyone in the room but this phone. Zero means there is nobody to ask.</summary>
        public static int Searchers => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount - 1 : 0;

        public bool Begin(string path, string label, bool conesOff)
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(path) || Searchers < 1) return false;
            if (Hosting != null && Hosting.running) End("ended");

            var task = new Hosted
            {
                id = UnityEngine.Random.Range(1, int.MaxValue), path = path, label = label ?? "",
                modelHash = AttentionSync.CurrentModelHash, conesOff = conesOff,
                startedAt = Time.unscaledTime, running = true,
            };
            foreach (Player p in PhotonNetwork.PlayerListOthers)
                task.results.Add(new Result { actor = p.ActorNumber, name = NetworkManager.DisplayName(p) });
            Hosting = task;

            // The presenter's shared highlight would point straight at the answer.
            if (AuraSync.Space.ModelPartSelector.DrivesRoom) AuraSync.Space.ModelPartSelector.Instance?.ClearSelection();

            PhotonNetwork.RaiseEvent(EventCodes.FindTask,
                new object[] { KindStart, task.id, task.modelHash, task.path, task.label, conesOff ? 1 : 0 },
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);

            SharedStateBus.Publish(StateKeys.FindTask, $"start {task.label} ({(conesOff ? "cones off" : "cones on")})", isLocal: true);
            Debug.Log($"[FindTask] Started: find '{task.label}' ({task.path}), cones {(conesOff ? "OFF" : "ON")}, {task.results.Count} searcher(s).");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Ends the hosted task; everyone who has not found the part is logged as not found.</summary>
        public void End(string reason)
        {
            Hosted t = Hosting;
            if (t == null || !t.running) return;
            t.running = false;
            t.endedAt = Time.unscaledTime;

            foreach (Result r in t.results)
            {
                if (r.done) continue;
                r.done = true;
                r.note = reason;
                AttentionLog.Write(t, r, PlayersInRoom());
            }

            if (PhotonNetwork.InRoom)
                PhotonNetwork.RaiseEvent(EventCodes.FindTask, new object[] { KindEnd, t.id },
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);

            SharedStateBus.Publish(StateKeys.FindTask, $"end ({reason})", isLocal: true);
            Debug.Log($"[FindTask] Ended ({reason}) after {t.Elapsed:0.0} s: " +
                      string.Join(", ", t.results.Select(r => r.found ? $"{r.name} {r.seconds:0.0}s" : $"{r.name} -")));
            OnChanged?.Invoke();
        }

        /// <summary>Forget the finished task's results (the panel's NEW TASK).</summary>
        public void ClearFinished()
        {
            if (Hosting != null && !Hosting.running) { Hosting = null; OnChanged?.Invoke(); }
        }

        private static int PlayersInRoom() => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount : 0;

        private void CheckAllDone()
        {
            if (Hosting != null && Hosting.running && Hosting.results.All(r => r.done)) End("all found");
        }

        // ── Searcher: tapping the part ─────────────────────────────────────────

        private void OnTap(Vector2 screenPos)
        {
            Assigned a = Searching;
            if (a == null || !a.Running) return;
            if (AuraSync.Notes.PartNoteManager.ConsumesTap || AuraSync.Challenge.ReassemblyGame.IsActive) return;

            Transform root = _sic != null ? _sic.ModelRoot : null;
            Camera cam = Camera.main;
            if (root == null || cam == null) return;
            if (!ModelRaycaster.Raycast(cam.ScreenPointToRay(screenPos), root, out Renderer hit, out _)) return; // empty space

            string path = ModelRaycaster.GetPartPath(root, hit.transform);
            bool right = path == a.path || path.StartsWith(a.path + "/", StringComparison.Ordinal);
            if (!right) { a.wrongTaps++; OnChanged?.Invoke(); return; }

            a.found = true;
            a.foundSeconds = Time.unscaledTime - a.receivedAt;
            bool conesShown = !AttentionSync.ConesHidden && !a.conesOff;

            if (PhotonNetwork.InRoom)
                PhotonNetwork.RaiseEvent(EventCodes.FindTask,
                    new object[] { KindFound, a.id, Mathf.RoundToInt(a.foundSeconds * 1000f), a.wrongTaps, conesShown ? 1 : 0 },
                    new RaiseEventOptions { TargetActors = new[] { a.owner } }, SendOptions.SendReliable);

            SharedStateBus.Publish(StateKeys.FindTask, $"found {a.label} in {a.foundSeconds:0.0}s", isLocal: true);
            Debug.Log($"[FindTask] Found '{a.label}' in {a.foundSeconds:0.00} s ({a.wrongTaps} wrong tap(s)).");
            OnChanged?.Invoke();
        }

        private void EndSearch()
        {
            if (Searching == null || Searching.ended) return;
            Searching.ended = true;
            Searching.endedAt = Time.unscaledTime;
            OnChanged?.Invoke();
        }

        /// <summary>The banner is done with the last task (shown a few seconds after it ended).</summary>
        public void DismissSearch()
        {
            if (Searching != null && !Searching.Running) { Searching = null; OnChanged?.Invoke(); }
        }

        // ── Receive ────────────────────────────────────────────────────────────

        public void OnEvent(EventData e)
        {
            if (e.Code != EventCodes.FindTask) return;
            if (!(e.CustomData is object[] d) || d.Length < 2 || !(d[0] is byte kind) || !(d[1] is int id)) return;
            if (PhotonNetwork.LocalPlayer != null && e.Sender == PhotonNetwork.LocalPlayer.ActorNumber) return;

            switch (kind)
            {
                case KindStart:
                    if (d.Length < 6 || !(d[2] is string hash) || !(d[3] is string path) || !(d[4] is string label)) return;
                    if (hash != AttentionSync.CurrentModelHash) return; // not on this model
                    ReceiveStart(e.Sender, id, hash, path, label, d[5] is int off && off != 0);
                    break;

                case KindFound:
                    if (d.Length < 5 || !(d[2] is int ms) || !(d[3] is int wrong)) return;
                    ReceiveFound(e.Sender, id, ms / 1000f, wrong, d[4] is int shown && shown != 0);
                    break;

                case KindEnd:
                    if (Searching != null && Searching.id == id) EndSearch();
                    break;
            }
        }

        private void ReceiveStart(int owner, int id, string hash, string path, string label, bool conesOff)
        {
            Searching = new Assigned
            {
                id = id, owner = owner, path = path, label = label, modelHash = hash,
                conesOff = conesOff, receivedAt = Time.unscaledTime,
            };

            // Search on one's own: the presenter turning the model must not give the part away, and
            // the parts list must not be the way it is found.
            SpaceInteractionController sic = SpaceInteractionController.Instance;
            if (sic != null && sic.IsSyncLocked) sic.BreakSync();
            AuraSync.UI.ModelPartsDrawerUI.Instance?.SetOpen(false);

            SharedStateBus.Publish(StateKeys.FindTask, $"asked to find {label}", isLocal: false);
            Debug.Log($"[FindTask] Asked to find '{label}' ({path}), cones {(conesOff ? "OFF" : "ON")}.");
            OnChanged?.Invoke();
        }

        private void ReceiveFound(int actor, int id, float seconds, int wrongTaps, bool conesShown)
        {
            Hosted t = Hosting;
            if (t == null || t.id != id || !t.running) return;
            Result r = t.results.FirstOrDefault(x => x.actor == actor);
            if (r == null || r.done) return;

            r.done = true; r.found = true; r.seconds = seconds; r.wrongTaps = wrongTaps; r.conesShown = conesShown;
            AttentionLog.Write(t, r, PlayersInRoom());
            OnChanged?.Invoke();
            CheckAllDone();
        }

        public override void OnPlayerLeftRoom(Player other)
        {
            if (Searching != null && Searching.Running && Searching.owner == other.ActorNumber) EndSearch();

            Hosted t = Hosting;
            if (t == null || !t.running) return;
            Result r = t.results.FirstOrDefault(x => x.actor == other.ActorNumber);
            if (r == null || r.done) return;
            r.done = true;
            r.note = "left";
            AttentionLog.Write(t, r, PlayersInRoom());
            OnChanged?.Invoke();
            CheckAllDone();
        }

        public override void OnLeftRoom()
        {
            if (Hosting != null && Hosting.running) End("left room");
            Hosting = null;
            EndSearch();
            Searching = null;
            OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// The paper's attention numbers, on the presenter's phone (<c>&lt;persistentDataPath&gt;/AttentionLog.jsonl</c>):
    /// one line per searcher per FIND task, with the condition (cones on / off), whether and how fast
    /// they found the part, wrong taps, and how many pings were sent during the task.
    /// </summary>
    public static class AttentionLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "AttentionLog.jsonl");

        [Serializable]
        private class Line
        {
            public string time, room, model, part, label, condition, name, note;
            public int task, actor, wrongTaps, players, pings;
            public bool found, conesShown;
            public float seconds;
        }

        public static void Write(FindTask.Hosted t, FindTask.Result r, int players)
        {
            var line = new Line
            {
                time = DateTime.UtcNow.ToString("o"),
                room = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "",
                model = SpaceSceneManager.CurrentModelKey, part = t.path, label = t.label,
                condition = t.conesOff ? "cones-off" : "cones-on",
                name = r.name, note = r.note, task = t.id, actor = r.actor,
                wrongTaps = r.wrongTaps, players = players, pings = t.pings,
                found = r.found, conesShown = r.conesShown,
                seconds = r.found ? r.seconds : t.Elapsed,
            };
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(line) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[AttentionLog] Could not write the log: {ex.Message}"); }
        }

        public static string Summary()
        {
            string pings = $"pings sent from this phone this session: {AttentionSync.PingsSent}";
            if (!File.Exists(FilePath)) return $"Attention: no FIND results yet; {pings}.";

            var lines = new List<Line>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { var l = JsonUtility.FromJson<Line>(raw); if (l != null) lines.Add(l); } catch { /* skip */ }
            }
            if (lines.Count == 0) return $"Attention: no FIND results yet; {pings}.";

            string Cond(string c)
            {
                var all = lines.Where(l => l.condition == c).ToList();
                var times = all.Where(l => l.found).Select(l => l.seconds).OrderBy(x => x).ToList();
                if (all.Count == 0) return $"{c}: none";
                string median = times.Count > 0 ? $"median {times[times.Count / 2]:0.0} s" : "none found";
                return $"{c}: {times.Count} of {all.Count} found, {median}";
            }

            return $"Attention: {lines.Count} FIND result(s); {Cond("cones-on")}; {Cond("cones-off")}; {pings}. File: {FilePath}";
        }
    }
}
