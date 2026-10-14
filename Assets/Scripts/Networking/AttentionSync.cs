using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Core;
using AuraSync.Space;

namespace AuraSync.Networking
{
    /// <summary>
    /// Where one person is looking on the model: a spot on a part, in that part's own space, and the
    /// direction they look from, in the model root's space. Stored like that, the spot rides with
    /// its part through explode, isolate and the simulation, and the direction turns with each
    /// phone's own copy of the model.
    /// </summary>
    public class AttentionSample
    {
        public int     actor;
        /// <summary>Part the spot is on; "" = beside the model (then the point is in the root's space).</summary>
        public string  path = "";
        public Vector3 point;
        /// <summary>Surface direction at the spot, part's space. Tells "on the far side" apart.</summary>
        public Vector3 normal;
        /// <summary>Viewing direction, root's space.</summary>
        public Vector3 dir;
        /// <summary>
        /// How far the viewer's camera is from the spot, in the root's space: grows as they zoom out.
        /// Cones float off the model in proportion, so a zoomed-out viewer's cone sits farther away.
        /// </summary>
        public float   distance;
    }

    /// <summary>A "Look here" ping: a spot (as in <see cref="AttentionSample"/>), who sent it and when.</summary>
    public class AttentionPing
    {
        public int     actor;
        public string  path = "";
        public Vector3 point;
        public Vector3 normal;
        public float   startTime;
    }

    /// <summary>
    /// Collaborative attention awareness (V3 Phase 8), the network half:
    ///
    ///   - **Views (code 25).** Five times a second each phone works out the spot at the centre of
    ///     its screen and sends it, with the viewing direction and distance, in the model's
    ///     coordinates. Only when it has changed, plus a heartbeat every second, unreliable: about
    ///     60 bytes, so five people cost almost nothing. A view not refreshed for 2.5 s is dropped.
    ///     **Who sees whom** (2026-10-14, after the Phase 8 test): the presenter sees every viewer's
    ///     cone; a viewer sees only the presenter's, and only while not following. So viewers send
    ///     to the presenter alone, and the presenter to everyone.
    ///   - **Cones for everyone** can be switched off by the presenter (room property
    ///     <see cref="RoomKeys.AttentionCones"/>); each phone can also hide them for itself (VIEWS).
    ///   - **"Look here" pings (code 26).** Holding one finger still on the model for 0.6 s pings that
    ///     spot on every phone for 3 s. Reliable.
    ///
    /// AttentionVisuals draws both. Cones can be hidden per phone (VIEWS on the rail); pings always
    /// show, because they are a deliberate call for attention (unless a FIND task runs without them).
    /// </summary>
    public class AttentionSync : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static AttentionSync Instance { get; private set; }

        public const float SendInterval   = 0.2f;
        public const float StaleSeconds   = 2.5f;
        public const float PingSeconds    = 3f;
        private const float Heartbeat         = 1f;
        private const float LongPressSeconds  = 0.6f;
        private const float LongPressMovePx   = 24f;

        /// <summary>This phone has hidden everyone's cones (VIEWS). Pings still show. Not shared.</summary>
        public static bool ConesHidden { get; private set; }
        public static event Action OnConesToggled;

        /// <summary>A ping arrived or was sent here (FindTask counts them).</summary>
        public static event Action<AttentionPing> OnPing;

        /// <summary>Pings sent from this phone since the app started.</summary>
        public static int PingsSent { get; private set; }

        public class RemoteView
        {
            public AttentionSample sample;
            public string modelHash;
            public float  lastSeen;
            /// <summary>A pretend viewer from the debug menu: never stale, no player behind it.</summary>
            public bool   test;
        }

        private readonly Dictionary<int, RemoteView> _views = new Dictionary<int, RemoteView>();
        private readonly List<AttentionPing> _pings = new List<AttentionPing>();

        public IReadOnlyDictionary<int, RemoteView> Views => _views;
        public IReadOnlyList<AttentionPing> Pings => _pings;

        /// <summary>This phone's own view, recomputed every send; null when there is no model.</summary>
        public AttentionSample LocalView { get; private set; }

        private float _sendTimer;
        private float _lastSentAt = -999f;
        private AttentionSample _lastSent;

        // Long-press
        private bool    _pressing;
        private bool    _pressValid;
        private bool    _pressFired;
        private Vector2 _pressStart;
        private float   _pressTime;

        /// <summary>The presenter has switched view cones off for the whole room.</summary>
        public static bool ConesOffForRoom =>
            PhotonNetwork.InRoom &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeys.AttentionCones, out object v) &&
            v is bool on && !on;

        /// <summary>Presenter only: cones on or off on every phone.</summary>
        public static void SetConesForRoom(bool on)
        {
            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeys.AttentionCones, on } });
        }

        /// <summary>This phone is the presenter, or alone (then it sees every cone, e.g. the debug test viewers).</summary>
        public static bool LocalIsPresenter =>
            !PhotonNetwork.InRoom || (NetworkManager.Instance != null &&
                                      NetworkManager.Instance.AdminActorNumber == PhotonNetwork.LocalPlayer.ActorNumber);

        public static void SetConesHidden(bool hidden)
        {
            if (ConesHidden == hidden) return;
            ConesHidden = hidden;
            OnConesToggled?.Invoke();
        }

        // ── Lifetime ───────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SharedStateBus.OnChange += OnStateChange;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnStateChange;
            if (Instance == this) Instance = null;
        }

        private void OnStateChange(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;
            // Views and pings are on the old model's parts.
            _views.Clear();
            _pings.Clear();
            _lastSent = null;
            LocalView = null;
            _radiusRoot = null;
        }

        // ── Model helpers (shared with AttentionVisuals and FindTask) ─────────

        private static string _hashKey;
        private static string _hash = "";

        /// <summary>Hash of the model on screen ("" while loading), as the other room keys use.</summary>
        public static string CurrentModelHash
        {
            get
            {
                string key = SpaceSceneManager.CurrentModelKey;
                if (string.IsNullOrEmpty(key)) return "";
                if (key != _hashKey) { _hashKey = key; _hash = ModelStateMemory.ModelHash(key); }
                return _hash;
            }
        }

        /// <summary>True while a model is on screen and the reassembly challenge is not running.</summary>
        public static bool ModelShown =>
            !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) && !AuraSync.Challenge.ReassemblyGame.IsActive;

        private static Transform _radiusRoot;
        private static float     _radiusLocal = 1f;

        /// <summary>
        /// Half the diagonal of the assembled model, in world units at its current zoom. Cones and
        /// rings are sized from it so they look the same on a small part and a big model.
        /// </summary>
        public static float ModelRadiusWorld(Transform root)
        {
            if (root == null) return 1f;
            if (root != _radiusRoot)
            {
                _radiusRoot  = root;
                _radiusLocal = ModelRaycaster.TryGetLocalBounds(root, out Bounds b) ? Mathf.Max(1e-4f, b.extents.magnitude) : 1f;
            }
            return _radiusLocal * Mathf.Abs(root.lossyScale.x);
        }

        /// <summary>The spot's place in the world on this phone; false if its part is not in this model.</summary>
        public static bool ToWorld(Transform root, string path, Vector3 point, Vector3 normal,
                                   out Transform part, out Vector3 world, out Vector3 worldNormal)
        {
            part = string.IsNullOrEmpty(path) ? root : ModelRaycaster.FindPart(root, path);
            world = worldNormal = Vector3.zero;
            if (part == null) return false;
            world = part.TransformPoint(point);
            worldNormal = part.TransformDirection(normal).normalized;
            return true;
        }

        /// <summary>
        /// The spot under a screen position: on a part, or (with allowBeside) on a plane through
        /// the model's centre, facing the camera, when the ray misses every part.
        /// </summary>
        public static bool SpotAt(Camera cam, Transform root, Vector2 screenPos, bool allowBeside,
                                  out string path, out Vector3 point, out Vector3 normal)
        {
            path = ""; point = normal = Vector3.zero;
            Ray ray = cam.ScreenPointToRay(screenPos);

            if (ModelRaycaster.Raycast(ray, root, out Renderer hit, out Vector3 world, out Vector3 worldNormal))
            {
                Transform part = hit.transform;
                path   = ModelRaycaster.GetPartPath(root, part);
                point  = part.InverseTransformPoint(world);
                normal = part.InverseTransformDirection(worldNormal).normalized;
                return true;
            }

            if (!allowBeside || !ModelRaycaster.TryGetBoundsCenter(root, out Vector3 center)) return false;
            Plane plane = new Plane(-cam.transform.forward, center);
            if (!plane.Raycast(ray, out float d)) return false;
            point  = root.InverseTransformPoint(ray.GetPoint(d));
            normal = root.InverseTransformDirection(-cam.transform.forward).normalized;
            return true;
        }

        // ── Every frame ────────────────────────────────────────────────────────

        void Update()
        {
            SpaceInteractionController sic = SpaceInteractionController.Instance;
            Transform root = sic != null ? sic.ModelRoot : null;
            Camera cam = Camera.main;
            bool modelOn = root != null && cam != null && ModelShown;

            DetectLongPress(modelOn && !AuraSync.Recording.SessionPlayer.IsReplaying, root, cam);
            ExpirePings();

            if (!PhotonNetwork.InRoom) { LocalView = null; return; }

            _sendTimer += Time.unscaledDeltaTime;
            if (_sendTimer < SendInterval) return;
            _sendTimer = 0f;

            DropStaleViews();
            if (!modelOn) { LocalView = null; return; }

            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!SpotAt(cam, root, centre, allowBeside: true, out string path, out Vector3 point, out Vector3 normal))
            {
                LocalView = null;
                return;
            }

            var view = new AttentionSample
            {
                actor  = PhotonNetwork.LocalPlayer.ActorNumber,
                path   = path, point = point, normal = normal,
                dir    = root.InverseTransformDirection(cam.transform.forward).normalized,
            };
            Transform spotPart = string.IsNullOrEmpty(path) ? root : ModelRaycaster.FindPart(root, path);
            float scale = Mathf.Max(1e-6f, Mathf.Abs(root.lossyScale.x));
            if (spotPart != null)
                view.distance = (cam.transform.position - spotPart.TransformPoint(point)).magnitude / scale;
            LocalView = view;

            if (!ChangedEnough(root, view) && Time.unscaledTime - _lastSentAt < Heartbeat) return;

            _lastSent   = view;
            _lastSentAt = Time.unscaledTime;

            // Only the presenter shows viewers' cones, so a viewer's view goes to the presenter alone.
            int presenter = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            var options = LocalIsPresenter || presenter <= 0
                ? new RaiseEventOptions { Receivers = ReceiverGroup.Others }
                : new RaiseEventOptions { TargetActors = new[] { presenter } };
            float[] data = Pack(view.point, view.normal, view.dir);
            Array.Resize(ref data, 10);
            data[9] = view.distance;
            PhotonNetwork.RaiseEvent(EventCodes.Attention,
                new object[] { CurrentModelHash, view.path, data }, options, SendOptions.SendUnreliable);
            SharedStateBus.Publish(StateKeys.AttentionView, view, isLocal: true);
        }

        /// <summary>Moved by more than 1% of the model, or turned by more than 1.5 degrees.</summary>
        private bool ChangedEnough(Transform root, AttentionSample v)
        {
            AttentionSample last = _lastSent;
            if (last == null || last.path != v.path) return true;
            if (Vector3.Angle(last.dir, v.dir) > 1.5f) return true;

            Transform part = string.IsNullOrEmpty(v.path) ? root : ModelRaycaster.FindPart(root, v.path);
            if (part == null) return true;
            float moved = (part.TransformPoint(v.point) - part.TransformPoint(last.point)).magnitude;
            return moved > 0.01f * ModelRadiusWorld(root);
        }

        private static float[] Pack(Vector3 a, Vector3 b, Vector3 c) =>
            new[] { a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z };

        private void DropStaleViews()
        {
            List<int> gone = null;
            foreach (var kv in _views)
            {
                if (kv.Value.test) continue;
                bool left = PhotonNetwork.CurrentRoom.GetPlayer(kv.Key) == null;
                if (left || Time.unscaledTime - kv.Value.lastSeen > StaleSeconds)
                    (gone ?? (gone = new List<int>())).Add(kv.Key);
            }
            if (gone != null) foreach (int a in gone) _views.Remove(a);
        }

        private void ExpirePings()
        {
            float now = Time.unscaledTime;
            _pings.RemoveAll(p => now - p.startTime > PingSeconds);
        }

        // ── Look here ──────────────────────────────────────────────────────────

        /// <summary>
        /// One finger (or the left mouse button) held still on the model. A drag, a second finger,
        /// a press that starts on a button, note placing and the challenge all rule it out. A tap
        /// is over in under 0.3 s, so a long-press never also selects a part.
        /// </summary>
        private void DetectLongPress(bool modelOn, Transform root, Camera cam)
        {
            int touches = InputCompat.TouchCount;
            bool down;
            Vector2 pos = Vector2.zero;
            if (touches > 0) { down = true; pos = InputCompat.GetTouchPosition(0); }
            else { down = InputCompat.HasMouse && InputCompat.GetMouseButton(0); if (down) pos = InputCompat.MousePosition; }

            if (!down) { _pressing = false; return; }

            if (!_pressing)
            {
                _pressing   = true;
                _pressFired = false;
                _pressStart = pos;
                _pressTime  = Time.unscaledTime;
                _pressValid = modelOn && touches < 2 &&
                              !SpaceInteractionController.IsPointerOverUI(pos) &&
                              !AuraSync.Notes.PartNoteManager.IsPlacing;
                return;
            }

            if (touches >= 2 || (pos - _pressStart).sqrMagnitude > LongPressMovePx * LongPressMovePx) _pressValid = false;
            if (!_pressValid || _pressFired || Time.unscaledTime - _pressTime < LongPressSeconds) return;

            _pressFired = true;
            if (modelOn) Ping(cam, root, _pressStart);
        }

        private void Ping(Camera cam, Transform root, Vector2 screenPos)
        {
            if (!SpotAt(cam, root, screenPos, allowBeside: false, out string path, out Vector3 point, out Vector3 normal))
                return; // only spots on the model itself

            int me = PhotonNetwork.InRoom ? PhotonNetwork.LocalPlayer.ActorNumber : 0;
            AddPing(new AttentionPing { actor = me, path = path, point = point, normal = normal, startTime = Time.unscaledTime }, isLocal: true);
            PingsSent++;

            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.RaiseEvent(EventCodes.LookHerePing,
                new object[] { CurrentModelHash, path, new[] { point.x, point.y, point.z, normal.x, normal.y, normal.z } },
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                SendOptions.SendReliable);
        }

        /// <summary>One ping per person: a new one replaces their previous one.</summary>
        private void AddPing(AttentionPing p, bool isLocal)
        {
            _pings.RemoveAll(x => x.actor == p.actor);
            _pings.Add(p);
            OnPing?.Invoke(p);
            SharedStateBus.Publish(StateKeys.AttentionPing, p, isLocal);
        }

        // ── Receive ────────────────────────────────────────────────────────────

        public void OnEvent(EventData e)
        {
            if (e.Code != EventCodes.Attention && e.Code != EventCodes.LookHerePing) return;
            if (!(e.CustomData is object[] d) || d.Length < 3) return;
            if (!(d[0] is string hash) || !(d[1] is string path) || !(d[2] is float[] f)) return;
            if (PhotonNetwork.LocalPlayer != null && e.Sender == PhotonNetwork.LocalPlayer.ActorNumber) return;
            if (hash != CurrentModelHash) return; // a different model (or still loading) here

            if (e.Code == EventCodes.Attention)
            {
                if (f.Length < 9) return;
                var s = new AttentionSample
                {
                    actor  = e.Sender, path = path,
                    point  = new Vector3(f[0], f[1], f[2]),
                    normal = new Vector3(f[3], f[4], f[5]),
                    dir    = new Vector3(f[6], f[7], f[8]).normalized,
                    distance = f.Length >= 10 ? f[9] : 0f,
                };
                _views[e.Sender] = new RemoteView { sample = s, modelHash = hash, lastSeen = Time.unscaledTime };
                SharedStateBus.Publish(StateKeys.AttentionView, s, isLocal: false);
            }
            else
            {
                if (f.Length < 6) return;
                AddPing(new AttentionPing
                {
                    actor = e.Sender, path = path,
                    point = new Vector3(f[0], f[1], f[2]), normal = new Vector3(f[3], f[4], f[5]),
                    startTime = Time.unscaledTime,
                }, isLocal: false);
            }
        }

        // ── Debug: pretend viewers, for checking the visuals on one device ─────

        public const int TestActorA = 92, TestActorB = 93, TestActorC = 94;

        /// <summary>
        /// Adds (or removes) three pretend viewers: one looking at a spot left of centre from the
        /// side, and two sharing one view right of centre (to show merging, "+ 1"). Their spots are
        /// fixed on the parts, so they ride along when the model turns, explodes or is isolated.
        /// </summary>
        public string ToggleTestViewers()
        {
            if (_views.Remove(TestActorA) | _views.Remove(TestActorB) | _views.Remove(TestActorC))
                return "Test viewers removed.";

            SpaceInteractionController sic = SpaceInteractionController.Instance;
            Transform root = sic != null ? sic.ModelRoot : null;
            Camera cam = Camera.main;
            if (root == null || cam == null) return "No model loaded.";

            int added = 0;
            if (AddTest(TestActorA, cam, root, new Vector2(0.40f, 0.55f), 35f)) added++;
            if (AddTest(TestActorB, cam, root, new Vector2(0.60f, 0.45f), -25f)) added++;
            if (AddTest(TestActorC, cam, root, new Vector2(0.60f, 0.45f), -25f)) added++;
            return added > 0 ? $"Added {added} test viewer(s)." : "No part under the test spots: turn the model to face you and try again.";
        }

        private bool AddTest(int actor, Camera cam, Transform root, Vector2 viewport, float yaw)
        {
            Vector2 screen = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
            if (!SpotAt(cam, root, screen, allowBeside: false, out string path, out Vector3 point, out Vector3 normal)) return false;
            Vector3 dir = Quaternion.AngleAxis(yaw, cam.transform.up) * cam.transform.forward;
            Transform part = string.IsNullOrEmpty(path) ? root : ModelRaycaster.FindPart(root, path);
            float dist = part == null ? 0f
                : (cam.transform.position - part.TransformPoint(point)).magnitude / Mathf.Max(1e-6f, Mathf.Abs(root.lossyScale.x));
            _views[actor] = new RemoteView
            {
                sample = new AttentionSample
                {
                    actor = actor, path = path, point = point, normal = normal,
                    dir = root.InverseTransformDirection(dir).normalized, distance = dist,
                },
                modelHash = CurrentModelHash, lastSeen = Time.unscaledTime, test = true,
            };
            return true;
        }

        /// <summary>A pretend "look here" from another player, at the spot in the middle of the screen.</summary>
        public string TestPing()
        {
            SpaceInteractionController sic = SpaceInteractionController.Instance;
            Transform root = sic != null ? sic.ModelRoot : null;
            Camera cam = Camera.main;
            if (root == null || cam == null) return "No model loaded.";
            Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (!SpotAt(cam, root, centre, allowBeside: false, out string path, out Vector3 point, out Vector3 normal))
                return "No part in the middle of the screen.";
            AddPing(new AttentionPing { actor = TestActorA, path = path, point = point, normal = normal, startTime = Time.unscaledTime }, isLocal: false);
            return "Test ping added.";
        }

        // ── Replay (Phase 9) ───────────────────────────────────────────────────

        /// <summary>The recorded views and pings at the replay's moment (replaces whatever was shown).</summary>
        public void SetReplay(List<AttentionSample> views, List<AttentionPing> pings)
        {
            _views.Clear();
            foreach (AttentionSample v in views)
                _views[v.actor] = new RemoteView { sample = v, modelHash = CurrentModelHash, lastSeen = Time.unscaledTime, test = true };
            _pings.Clear();
            _pings.AddRange(pings);
        }

        public void ClearReplay()
        {
            _views.Clear();
            _pings.Clear();
        }

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!changed.ContainsKey(RoomKeys.AttentionCones)) return;
            OnConesToggled?.Invoke();
            if (!LocalIsPresenter)
                AuraSync.UI.Toast.Show(ConesOffForRoom
                    ? "The presenter turned view cones off for everyone."
                    : "The presenter turned view cones back on.");
        }

        public override void OnPlayerLeftRoom(Player other)
        {
            _views.Remove(other.ActorNumber);
            _pings.RemoveAll(p => p.actor == other.ActorNumber);
        }

        public override void OnLeftRoom()
        {
            _views.Clear();
            _pings.Clear();
            _lastSent = null;
        }
    }
}
