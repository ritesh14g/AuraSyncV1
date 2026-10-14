using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Networking
{
    /// <summary>
    /// The shared controls the presenter can hand to one student without giving up the presenter
    /// role (V3 Phase 1). Section joined in Phase 6, Simulation in Phase 7.
    /// </summary>
    public enum Control : byte
    {
        Explode    = 0,
        Isolate    = 1,
        Highlight  = 2,
        Section    = 3,
        Simulation = 4,
    }

    /// <summary>
    /// Fine-grained ownership: who drives each shared control (explode, isolate, highlight) for the
    /// room.
    ///
    /// **State.** One room property per control, <c>"CT:" + id</c>, holding the actor number of the
    /// student who drives it, or 0 for "the presenter". Storing 0 rather than the presenter's actor
    /// number means handing over the presenter role moves every control the presenter held along
    /// with it, with nothing to rewrite; a student keeps what they were given. A holder who is no
    /// longer in the room counts as 0 straight away, before anyone has tidied the property.
    ///
    /// **No race conditions.** Every write is a check-and-set: Photon applies it only if the
    /// property still holds the value this device last saw (<c>expectedProperties</c>). If two
    /// writes cross (two Allows for the same control, a revoke crossing the holder leaving), exactly
    /// one lands, and the server rejects the other. Rejections are counted for the paper.
    ///
    /// **Messages** (event code 20, reliable): a viewer's request goes to the presenter, and the
    /// presenter's Deny goes back to the viewer. A grant needs no message: the property change is
    /// the grant, and the new holder hears about it from the room like everyone else.
    ///
    /// Outside a room there is nobody to share with, so this device holds everything.
    /// </summary>
    public class ControlOwnership : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static ControlOwnership Instance { get; private set; }

        /// <summary>The controls that exist so far, in the order the UI lists them.</summary>
        public static readonly Control[] Active = { Control.Explode, Control.Isolate, Control.Highlight, Control.Section, Control.Simulation };

        /// <summary>How long the presenter has to answer a request before it is declined.</summary>
        public const float RequestTimeoutSeconds = 20f;

        /// <summary>Raised whenever any control changes hands (or the room or presenter changes).</summary>
        public static event Action OnChanged;

        /// <summary>Raised when one of this device's requests is turned down or times out.</summary>
        public static event Action<Control> OnRequestDeclined;

        private const byte OpRequest = 1; // viewer -> presenter
        private const byte OpDenied  = 2; // presenter -> viewer
        private const byte OpBusy    = 3; // presenter -> viewer: it changed hands while they were asking

        private struct PendingWrite
        {
            public string key;
            public int    value;
            public float  sentAt;
            public Action onRejected;
            public bool   userAction; // false for housekeeping, which the conflict count leaves out
        }

        private readonly List<PendingWrite> _pendingWrites = new List<PendingWrite>();

        // This device's open requests: control -> when it was sent (realtime seconds).
        private static readonly Dictionary<Control, float> _myRequests = new Dictionary<Control, float>();

        // What this device held the last time anything changed, to spot gains and losses.
        private readonly HashSet<Control> _heldBefore = new HashSet<Control>();
        private bool _wasPresenter;
        private int  _lastPresenter = int.MinValue;

        private LoadBalancingClient _client;

        // ── Creating ───────────────────────────────────────────────────────────

        /// <summary>Creates the (DontDestroyOnLoad) instance if there is none yet.</summary>
        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("AuraSync_ControlOwnership").AddComponent<ControlOwnership>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public override void OnEnable()
        {
            // MonoBehaviourPunCallbacks registers this object (and its IOnEventCallback).
            base.OnEnable();
            _client = PhotonNetwork.NetworkingClient;
            if (_client != null) _client.OpResponseReceived += OnOpResponse;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (_client != null) _client.OpResponseReceived -= OnOpResponse;
            _client = null;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Reading ────────────────────────────────────────────────────────────

        public static string KeyFor(Control c) => RoomKeys.ControlPrefix + Id(c);

        public static string Id(Control c)
        {
            switch (c)
            {
                case Control.Explode:    return "explode";
                case Control.Isolate:    return "isolate";
                case Control.Highlight:  return "highlight";
                case Control.Section:    return "section";
                case Control.Simulation: return "simulation";
                default:                 return c.ToString().ToLowerInvariant();
            }
        }

        /// <summary>The name people see: "Section cut", not "section".</summary>
        public static string Label(Control c)
        {
            switch (c)
            {
                case Control.Explode:    return "Explode";
                case Control.Isolate:    return "Isolate";
                case Control.Highlight:  return "Highlight";
                case Control.Section:    return "Section cut";
                case Control.Simulation: return "Simulation";
                default:                 return c.ToString();
            }
        }

        private static int PresenterActor =>
            NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;

        private static bool LocalIsPresenter =>
            PhotonNetwork.InRoom && PresenterActor >= 0 && PhotonNetwork.LocalPlayer.ActorNumber == PresenterActor;

        /// <summary>The property's stored value: a student's actor number, or 0 for the presenter.</summary>
        private static int RawHolder(Control c)
        {
            if (!PhotonNetwork.InRoom) return 0;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyFor(c), out object v) && v is int a ? a : 0;
        }

        /// <summary>
        /// Actor number of whoever drives this control: the student it was given to, or the
        /// presenter. -1 outside a room.
        /// </summary>
        public static int HolderOf(Control c)
        {
            if (!PhotonNetwork.InRoom) return -1;
            int raw = RawHolder(c);
            if (raw > 0 && PhotonNetwork.CurrentRoom.GetPlayer(raw) != null) return raw;
            return PresenterActor; // 0, or a holder who has left
        }

        /// <summary>True when a student (not the presenter) drives this control.</summary>
        public static bool IsHandedOut(Control c)
        {
            if (!PhotonNetwork.InRoom) return false;
            int h = HolderOf(c);
            return h >= 0 && h != PresenterActor;
        }

        /// <summary>True when this device's changes to the control go to the room (always true outside a room).</summary>
        public static bool LocalHolds(Control c)
        {
            if (!PhotonNetwork.InRoom) return true;
            return HolderOf(c) == PhotonNetwork.LocalPlayer.ActorNumber;
        }

        /// <summary>True when this device, in a room, drives the control for everyone.</summary>
        public static bool LocalDrivesRoom(Control c) => PhotonNetwork.InRoom && LocalHolds(c);

        /// <summary>The holder's display name, or "Presenter" while the presenter has it.</summary>
        public static string HolderName(Control c)
        {
            if (!PhotonNetwork.InRoom) return "";
            int h = HolderOf(c);
            if (h == PresenterActor) return "Presenter";
            Player p = PhotonNetwork.CurrentRoom.GetPlayer(h);
            return p != null ? NetworkManager.DisplayName(p) : "Presenter";
        }

        /// <summary>The controls a player drives as a student (empty for the presenter, who drives the rest).</summary>
        public static List<Control> HeldBy(int actor)
        {
            var list = new List<Control>();
            if (!PhotonNetwork.InRoom || actor == PresenterActor) return list;
            foreach (Control c in Active)
                if (HolderOf(c) == actor) list.Add(c);
            return list;
        }

        /// <summary>True while one of this device's writes to the control is still on its way to the server.</summary>
        public static bool IsWritePending(Control c) =>
            Instance != null && Instance._pendingWrites.Exists(w => w.key == KeyFor(c));

        /// <summary>True while this device is waiting for the presenter to answer a request.</summary>
        public static bool IsRequestPending(Control c)
        {
            if (!_myRequests.TryGetValue(c, out float at)) return false;
            return Time.realtimeSinceStartup - at < RequestTimeoutSeconds + 2f;
        }

        // ── Presenter actions ──────────────────────────────────────────────────

        /// <summary>
        /// The presenter gives a control to a student (or takes it back with the presenter's own
        /// actor number). Check-and-set against the holder this device currently sees, so a grant
        /// that crosses another change is rejected rather than silently overwriting it.
        /// </summary>
        public static bool Grant(Control c, int actor, Action onRejected = null)
        {
            if (!LocalIsPresenter) return false;
            if (actor != PresenterActor && PhotonNetwork.CurrentRoom.GetPlayer(actor) == null) return false;

            int value = actor == PresenterActor ? 0 : actor;
            return Instance != null && Instance.CheckAndSet(c, RawHolder(c), value, onRejected, "grant");
        }

        /// <summary>The presenter takes a control back from whoever has it.</summary>
        public static bool TakeBack(Control c) => Grant(c, PresenterActor);

        /// <summary>A student hands back a control they hold.</summary>
        public static bool GiveBack(Control c)
        {
            if (!PhotonNetwork.InRoom || LocalIsPresenter) return false;
            int me = PhotonNetwork.LocalPlayer.ActorNumber;
            if (RawHolder(c) != me) return false;
            return Instance != null && Instance.CheckAndSet(c, me, 0, null, "give-back");
        }

        // ── Viewer actions ─────────────────────────────────────────────────────

        /// <summary>A viewer asks the presenter for a control. The answer arrives as a grant or a decline.</summary>
        public static bool Request(Control c)
        {
            if (!PhotonNetwork.InRoom || LocalIsPresenter || LocalHolds(c)) return false;
            if (IsRequestPending(c)) return false;

            int presenter = PresenterActor;
            if (presenter < 0) return false;

            _myRequests[c] = Time.realtimeSinceStartup;
            Send(OpRequest, c, presenter);
            ControlLog.Write("request", c, PhotonNetwork.LocalPlayer.ActorNumber, 0);
            OnChanged?.Invoke();
            return true;
        }

        private static void Send(byte op, Control c, int target)
        {
            PhotonNetwork.RaiseEvent(EventCodes.ControlOwnership,
                new object[] { op, (byte)c, RawHolder(c) },
                new RaiseEventOptions { TargetActors = new[] { target } },
                SendOptions.SendReliable);
        }

        public void OnEvent(EventData e)
        {
            if (e.Code != EventCodes.ControlOwnership) return;
            if (!(e.CustomData is object[] data) || data.Length < 2) return;
            if (!(data[0] is byte op) || !(data[1] is byte cb)) return;

            Control c = (Control)cb;
            int seenHolder = data.Length > 2 && data[2] is int h ? h : 0;

            switch (op)
            {
                case OpRequest: OnRequestReceived(c, e.Sender, seenHolder); break;
                case OpDenied:
                case OpBusy:    OnDeclined(c, op == OpBusy); break;
            }
        }

        // ── Presenter: answering a request ─────────────────────────────────────

        private void OnRequestReceived(Control c, int requester, int seenHolder)
        {
            if (!LocalIsPresenter) return;
            Player p = PhotonNetwork.CurrentRoom.GetPlayer(requester);
            if (p == null) return;
            if (HolderOf(c) == requester) return; // they already have it

            string who  = NetworkManager.DisplayName(p);
            string what = Label(c);
            string from = IsHandedOut(c) ? $" It is taken from {HolderName(c)}." : "";
            float  asked = Time.realtimeSinceStartup;

            AuraSync.UI.ShareWithRoomPrompt.Ask(new AuraSync.UI.ShareWithRoomPrompt.Options
            {
                id          = $"req:{requester}:{Id(c)}",
                title       = $"{who} asks to drive {what}",
                body        = $"Allow: {who} controls {what} for everyone until you take it back.{from}\nDeny: you keep it.",
                acceptLabel = "ALLOW",
                denyLabel   = "DENY",
                timeoutSeconds = RequestTimeoutSeconds,
                onAccept = () =>
                {
                    // Check-and-set against the holder the requester saw when asking: if the control
                    // has changed hands since, this Allow is out of date and the server rejects it.
                    if (!LocalIsPresenter || PhotonNetwork.CurrentRoom.GetPlayer(requester) == null) return;
                    if (HolderOf(c) == requester) return; // given from the badge menu in the meantime
                    CheckAndSet(c, seenHolder, requester, onRejected: () =>
                    {
                        AuraSync.UI.Toast.Show($"{what} changed hands meanwhile, so {who} was not given it.");
                        Send(OpBusy, c, requester);
                    }, reason: "allow");
                    ControlLog.Write("allow", c, requester, (Time.realtimeSinceStartup - asked) * 1000f);
                },
                onDeny = () =>
                {
                    if (!PhotonNetwork.InRoom) return;
                    Send(OpDenied, c, requester);
                    ControlLog.Write("deny", c, requester, (Time.realtimeSinceStartup - asked) * 1000f);
                },
            });
        }

        private static void OnDeclined(Control c, bool busy)
        {
            _myRequests.Remove(c);
            AuraSync.UI.Toast.Show(busy
                ? $"{Label(c)} changed hands while you were asking. Try again."
                : $"The presenter kept {Label(c)}.");
            OnRequestDeclined?.Invoke(c);
            OnChanged?.Invoke();
        }

        // ── Check-and-set ──────────────────────────────────────────────────────

        private bool CheckAndSet(Control c, int expected, int value, Action onRejected, string reason)
        {
            if (!PhotonNetwork.InRoom) return false;
            string key = KeyFor(c);

            // The same write twice in flight (a double tap, a repeated tidy-up) would expect the
            // old value and be rejected by our own first copy, which is not a conflict between
            // people. A *different* write (Allow for Ben right after Allow for Asha) still goes to
            // the server, which lets exactly one of them land.
            if (_pendingWrites.Exists(w => w.key == key && w.value == value))
            {
                Debug.Log($"[ControlOwnership] {reason}: {Id(c)} skipped, a write is still on its way.");
                return false;
            }

            var props = new Hashtable { { key, value } };

            // A key nobody has written yet cannot be "expected" (the server has nothing to compare),
            // so the very first write is plain. Tidy() writes every key on room creation, so this
            // only covers a room made by a build from before Phase 1.
            bool exists = PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(key);
            Hashtable expect = exists ? new Hashtable { { key, expected } } : null;

            bool sent = PhotonNetwork.CurrentRoom.SetCustomProperties(props, expect);
            if (sent && expect != null)
            {
                _pendingWrites.Add(new PendingWrite
                {
                    key = key, value = value, sentAt = Time.realtimeSinceStartup,
                    onRejected = onRejected, userAction = reason != "tidy",
                });
            }

            Debug.Log($"[ControlOwnership] {reason}: {Id(c)} {expected} -> {value} (sent: {sent}).");
            return sent;
        }

        /// <summary>
        /// A rejected check-and-set comes back as a failed SetProperties response; nothing else in
        /// AuraSync makes conditional writes, and plain ones do not fail, so the oldest write still
        /// waiting is the one that lost.
        /// </summary>
        private void OnOpResponse(OperationResponse r)
        {
            if (r.OperationCode != OperationCode.SetProperties || r.ReturnCode == 0) return;
            if (_pendingWrites.Count == 0) return;

            PendingWrite lost = _pendingWrites[0];
            _pendingWrites.RemoveAt(0);

            if (lost.userAction)
            {
                ControlLog.ConflictsPrevented++;
                ControlLog.Write("conflict-rejected", ControlFromKey(lost.key), lost.value, 0);
            }
            Debug.Log($"[ControlOwnership] Check-and-set rejected for {lost.key} -> {lost.value}: {r.DebugMessage}");

            try { lost.onRejected?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        private static Control ControlFromKey(string key)
        {
            foreach (Control c in Enum.GetValues(typeof(Control)))
                if (KeyFor(c) == key) return c;
            return Control.Explode;
        }

        void Update()
        {
            // Writes that were neither confirmed nor rejected (a dropped connection) stop waiting.
            for (int i = _pendingWrites.Count - 1; i >= 0; i--)
                if (Time.realtimeSinceStartup - _pendingWrites[i].sentAt > 10f) _pendingWrites.RemoveAt(i);

            // A request the presenter never answered (left, app in background) times out here too.
            if (_myRequests.Count > 0)
            {
                foreach (Control c in _myRequests.Keys.ToList())
                {
                    if (IsRequestPending(c)) continue;
                    _myRequests.Remove(c);
                    AuraSync.UI.Toast.Show($"No answer about {Label(c)}. You can ask again.");
                    OnRequestDeclined?.Invoke(c);
                    OnChanged?.Invoke();
                }
            }

            // The presenter changing hands is watched here rather than in a property callback,
            // because NetworkManager may handle that same callback after this component does, and
            // until it has, "who is presenter" still reads the old answer.
            int presenter = PhotonNetwork.InRoom ? PresenterActor : -1;
            if (presenter != _lastPresenter)
            {
                _lastPresenter = presenter;
                if (PhotonNetwork.InRoom) Tidy();
                Evaluate();
            }
        }

        // ── Keeping the state tidy ─────────────────────────────────────────────

        /// <summary>
        /// The presenter writes 0 into every key that has none, so later check-and-sets have a value
        /// to compare against; and resets any control whose holder has left, or who has since become
        /// the presenter (their controls are now simply the presenter's).
        /// </summary>
        private void Tidy()
        {
            if (!LocalIsPresenter) return;

            var seed = new Hashtable();
            foreach (Control c in Enum.GetValues(typeof(Control)))
            {
                string key = KeyFor(c);
                if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(key)) { seed[key] = 0; continue; }

                int raw = RawHolder(c);
                if (raw == 0) continue;
                if (raw == PresenterActor || PhotonNetwork.CurrentRoom.GetPlayer(raw) == null)
                    CheckAndSet(c, raw, 0, null, "tidy");
            }
            if (seed.Count > 0) PhotonNetwork.CurrentRoom.SetCustomProperties(seed);
        }

        /// <summary>Works out what this device gained or lost, tells the user, and lets listeners redraw.</summary>
        private void Evaluate()
        {
            bool inRoom = PhotonNetwork.InRoom;
            bool presenter = LocalIsPresenter;

            foreach (Control c in Active)
            {
                bool holds = inRoom && LocalHolds(c);
                bool held  = _heldBefore.Contains(c);

                // The presenter changing hands is announced by the HUD, not per control; only a
                // student being given or losing a control gets a message here.
                if (inRoom && holds != held && !presenter && !_wasPresenter)
                {
                    if (holds)
                    {
                        if (_myRequests.TryGetValue(c, out float at))
                        {
                            float ms = (Time.realtimeSinceStartup - at) * 1000f;
                            ControlLog.Write("granted", c, PhotonNetwork.LocalPlayer.ActorNumber, ms);
                            Debug.Log($"[ControlOwnership] Request-to-grant for {Id(c)}: {ms:0} ms.");
                        }
                        AuraSync.UI.Toast.Show($"You now control: {Label(c)}");
                    }
                    else
                    {
                        AuraSync.UI.Toast.Show(IsHandedOut(c)
                            ? $"{Label(c)} was handed to {HolderName(c)}."
                            : $"{Label(c)} went back to the presenter.");
                    }
                }

                // A "show this to everyone?" still open for a control just lost no longer applies.
                if (held && !holds) AuraSync.UI.ShareWithRoomPrompt.Cancel(Id(c));

                if (holds) { _heldBefore.Add(c); _myRequests.Remove(c); }
                else _heldBefore.Remove(c);
            }

            _wasPresenter = presenter;
            OnChanged?.Invoke();
        }

        // ── Photon callbacks ───────────────────────────────────────────────────

        public override void OnJoinedRoom()
        {
            _heldBefore.Clear();
            _myRequests.Clear();
            _wasPresenter = LocalIsPresenter;
            foreach (Control c in Active) if (LocalHolds(c)) _heldBefore.Add(c);
            Tidy();
            OnChanged?.Invoke();
        }

        public override void OnLeftRoom()
        {
            // Open questions (share this? / Allow Asha?) no longer mean anything outside the room.
            AuraSync.UI.ShareWithRoomPrompt.CancelAll();
            _pendingWrites.Clear();
            _myRequests.Clear();
            _heldBefore.Clear();
            _wasPresenter = false;
            OnChanged?.Invoke();
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            bool relevant = changed.ContainsKey(RoomKeys.AdminActor);
            foreach (Control c in Enum.GetValues(typeof(Control)))
            {
                string key = KeyFor(c);
                if (!changed.ContainsKey(key)) continue;
                relevant = true;

                // Confirms one of this device's writes.
                int value = changed[key] is int v ? v : 0;
                int i = _pendingWrites.FindIndex(w => w.key == key && w.value == value);
                if (i >= 0) _pendingWrites.RemoveAt(i);
            }

            if (!relevant) return;

            // A new presenter is handled in Update (see there), once NetworkManager has caught up.
            Evaluate();
        }

        public override void OnPlayerLeftRoom(Player other)
        {
            Tidy();
            Evaluate();
        }

        public override void OnMasterClientSwitched(Player newMaster) => Evaluate();
    }

    /// <summary>
    /// The paper's ownership numbers, on the device only: requests, Allow / Deny with the time the
    /// presenter took, the requester's request-to-grant time, and check-and-set rejections
    /// (conflicts prevented). <c>&lt;persistentDataPath&gt;/ControlLog.jsonl</c>, one JSON object per line.
    /// </summary>
    public static class ControlLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "ControlLog.jsonl");

        /// <summary>Rejected check-and-sets on this device since the app started.</summary>
        public static int ConflictsPrevented;

        [Serializable]
        private class Line
        {
            public string time, evt, control, room;
            public int actor;
            public float ms;
        }

        public static void Write(string evt, Control c, int actor, float ms)
        {
            var line = new Line
            {
                time = DateTime.UtcNow.ToString("o"), evt = evt, control = ControlOwnership.Id(c),
                room = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "", actor = actor, ms = ms,
            };
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(line) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[ControlLog] Could not write the log: {ex.Message}"); }
        }

        public static string Summary()
        {
            if (!File.Exists(FilePath)) return $"No control hand-overs yet ({FilePath}).";
            var lines = new List<Line>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { var l = JsonUtility.FromJson<Line>(raw); if (l != null) lines.Add(l); } catch { /* skip */ }
            }
            if (lines.Count == 0) return $"No control hand-overs yet ({FilePath}).";

            int Count(string e) => lines.Count(l => l.evt == e);
            var grants = lines.Where(l => l.evt == "granted").Select(l => l.ms).OrderBy(x => x).ToList();
            string median = grants.Count > 0 ? $"{grants[grants.Count / 2]:0} ms" : "n/a";

            return $"Controls: {Count("request")} request(s), {Count("allow")} allowed, {Count("deny")} denied, " +
                   $"{Count("conflict-rejected")} conflict(s) prevented (this session: {ConflictsPrevented}), " +
                   $"median request-to-grant {median}. File: {FilePath}";
        }
    }
}
