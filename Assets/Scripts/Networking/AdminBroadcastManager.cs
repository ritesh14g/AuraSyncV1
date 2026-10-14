using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Space;

namespace AuraSync.Networking
{
    /// <summary>
    /// The presenter broadcasts the 3D model's transform with PhotonNetwork.RaiseEvent.
    /// Viewers mirror it while their sync lock is on (see SpaceInteractionController).
    ///
    /// Bandwidth: updates are sent only while the model is actually changing (unreliable, so a
    /// dropped packet is simply replaced by the next one). Once it settles, a reliable heartbeat
    /// every second guarantees everyone converges on the final pose and late joiners catch up.
    /// </summary>
    public class AdminBroadcastManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static AdminBroadcastManager Instance { get; private set; }

        public const byte EventCodeTransform = EventCodes.AdminTransform;

        [Header("Broadcast Settings")]
        [Tooltip("Interval in seconds between transform checks while moving (0.033 = ~30fps).")]
        [SerializeField] private float broadcastInterval = 0.033f;
        [Tooltip("When nothing changes, resend the current pose reliably this often.")]
        [SerializeField] private float heartbeatInterval = 1.0f;

        private Transform _modelTransform;
        private float     _broadcastTimer;

        private Vector3    _lastSentPos;
        private Quaternion _lastSentRot = Quaternion.identity;
        private Vector3    _lastSentScale;
        private float      _lastSendTime = -999f;

        // Last known Admin transform, cached for Catch Up.
        public Vector3    LastAdminPosition  { get; private set; }
        public Quaternion LastAdminRotation  { get; private set; } = Quaternion.identity;
        public Vector3    LastAdminScale     { get; private set; } = Vector3.one;
        public bool       HasReceivedAdminTransform { get; private set; } = false;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // MonoBehaviourPunCallbacks already registers this object (and its IOnEventCallback)
        // with Photon in OnEnable, so no manual AddCallbackTarget is needed.

        public void SetBroadcastTarget(Transform modelTransform)
        {
            _modelTransform = modelTransform;
            _lastSendTime = -999f; // force an immediate send for the new target
            Debug.Log($"[AuraSync] AdminBroadcast: Target set -> {(_modelTransform != null ? _modelTransform.name : "null")}");
        }

        /// <summary>
        /// Sends the current pose right away. Broadcasting itself is automatic: whoever is the
        /// presenter broadcasts, everyone else does not (a former presenter stops by itself).
        /// </summary>
        public void StartBroadcasting()
        {
            if (IsLocalPresenter) Send(true);
        }

        /// <summary>Kept for existing callers. Nothing to stop: only the presenter ever sends.</summary>
        public void StopBroadcasting() { }

        private static bool IsLocalPresenter =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            if (isAdmin && _modelTransform != null && PhotonNetwork.InRoom)
            {
                // Instantly update the newly joined viewer with the current model transform
                Send(true);
            }
        }

        void Update()
        {
            if (!IsLocalPresenter || _modelTransform == null) return;

            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= broadcastInterval)
            {
                _broadcastTimer = 0f;
                Send(false);
            }
        }

        /// <summary>Send the model's current pose to everyone else (reliably).</summary>
        public void SendTransformUpdate() => Send(true);

        private void Send(bool force)
        {
            if (_modelTransform == null || !PhotonNetwork.InRoom) return;

            Vector3    pos   = _modelTransform.position;
            Quaternion rot   = _modelTransform.rotation;
            Vector3    scale = _modelTransform.localScale;

            bool changed =
                (pos   - _lastSentPos).sqrMagnitude   > 1e-8f ||
                (scale - _lastSentScale).sqrMagnitude > 1e-8f ||
                Quaternion.Angle(rot, _lastSentRot)   > 0.01f;

            bool heartbeatDue = Time.unscaledTime - _lastSendTime >= heartbeatInterval;
            if (!force && !changed && !heartbeatDue) return;

            // 10 floats payload: pos (3), quat rot (4), scale (3)
            float[] data = new float[]
            {
                pos.x,   pos.y,   pos.z,
                rot.x,   rot.y,   rot.z,   rot.w,
                scale.x, scale.y, scale.z
            };

            RaiseEventOptions opts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };

            // While moving, fresh data supersedes lost data, so unreliable is right. Forced sends
            // and heartbeats (nothing changed) are the "final pose" and must arrive.
            bool reliable = force || !changed;
            PhotonNetwork.RaiseEvent(EventCodeTransform, data, opts,
                reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);

            _lastSentPos   = pos;
            _lastSentRot   = rot;
            _lastSentScale = scale;
            _lastSendTime  = Time.unscaledTime;
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCodeTransform) return;

            // Only the current presenter drives the model. Ignore anyone else (for example a player
            // who was presenter a moment ago and whose last packets are still arriving).
            int presenter = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            if (presenter >= 0 && photonEvent.Sender != presenter) return;

            float[] data = photonEvent.CustomData as float[];
            if (data == null || data.Length < 10) return;

            Vector3    adminPos   = new Vector3(data[0], data[1], data[2]);
            Quaternion adminRot   = new Quaternion(data[3], data[4], data[5], data[6]);
            Vector3    adminScale = new Vector3(data[7], data[8], data[9]);

            LastAdminPosition         = adminPos;
            LastAdminRotation         = adminRot;
            LastAdminScale            = adminScale;
            HasReceivedAdminTransform = true;

            SpaceInteractionController sic = SpaceInteractionController.Instance;
            if (sic != null)
            {
                sic.ApplyRemoteTransform(adminPos, adminRot, adminScale);
            }
        }
    }
}
