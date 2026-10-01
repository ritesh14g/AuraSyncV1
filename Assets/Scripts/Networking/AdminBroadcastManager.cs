using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Space;

namespace AuraSync.Networking
{
    /// <summary>
    /// Admin broadcasts 3D model transform updates using PhotonNetwork.RaiseEvent.
    /// Sends position, quaternion rotation, and scale reliably.
    /// Viewers mirror Admin's transform in real-time when IsSyncLocked = true.
    /// </summary>
    public class AdminBroadcastManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static AdminBroadcastManager Instance { get; private set; }

        public const byte EventCodeTransform = 1;

        [Header("Broadcast Settings")]
        [Tooltip("Interval in seconds between transform broadcasts (0.033 = ~30fps).")]
        [SerializeField] private float broadcastInterval = 0.033f;

        private Transform _modelTransform;
        private float     _broadcastTimer;
        private bool      _isBroadcasting = false;

        // Last known Admin transform — cached for Catch Up.
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

        public override void OnEnable()  { base.OnEnable();  PhotonNetwork.AddCallbackTarget(this); }
        public override void OnDisable() { base.OnDisable(); PhotonNetwork.RemoveCallbackTarget(this); }

        public void SetBroadcastTarget(Transform modelTransform)
        {
            _modelTransform = modelTransform;
            Debug.Log($"[AuraSync] AdminBroadcast: Target set → {(_modelTransform != null ? _modelTransform.name : "null")}");
        }

        public void StartBroadcasting()
        {
            _isBroadcasting = true;
            Debug.Log("[AuraSync] AdminBroadcast: Broadcasting STARTED");
            SendTransformUpdate();
        }

        public void StopBroadcasting()
        {
            _isBroadcasting = false;
            Debug.Log("[AuraSync] AdminBroadcast: Broadcasting STOPPED");
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            if (isAdmin && _modelTransform != null && PhotonNetwork.InRoom)
            {
                // Instantly update newly joined viewer with current model transform
                SendTransformUpdate();
            }
        }

        void Update()
        {
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            bool shouldBroadcast = (_isBroadcasting || isAdmin) && _modelTransform != null && PhotonNetwork.InRoom;

            if (!shouldBroadcast) return;

            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= broadcastInterval)
            {
                _broadcastTimer = 0f;
                SendTransformUpdate();
            }
        }

        public void SendTransformUpdate()
        {
            if (_modelTransform == null || !PhotonNetwork.InRoom) return;

            Vector3    pos   = _modelTransform.position;
            Quaternion rot   = _modelTransform.rotation;
            Vector3    scale = _modelTransform.localScale;

            // 10 floats payload: pos (3), quat rot (4), scale (3)
            float[] data = new float[]
            {
                pos.x,   pos.y,   pos.z,
                rot.x,   rot.y,   rot.z,   rot.w,
                scale.x, scale.y, scale.z
            };

            RaiseEventOptions opts = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            };

            PhotonNetwork.RaiseEvent(EventCodeTransform, data, opts, SendOptions.SendReliable);
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCodeTransform) return;

            float[] data = photonEvent.CustomData as float[];
            if (data == null || data.Length < 9) return;

            Vector3    adminPos;
            Quaternion adminRot;
            Vector3    adminScale;

            if (data.Length >= 10)
            {
                // Quaternion format (10 floats)
                adminPos   = new Vector3(data[0], data[1], data[2]);
                adminRot   = new Quaternion(data[3], data[4], data[5], data[6]);
                adminScale = new Vector3(data[7], data[8], data[9]);
            }
            else
            {
                // Legacy Euler format (9 floats)
                adminPos   = new Vector3(data[0], data[1], data[2]);
                adminRot   = Quaternion.Euler(data[3], data[4], data[5]);
                adminScale = new Vector3(data[6], data[7], data[8]);
            }

            LastAdminPosition        = adminPos;
            LastAdminRotation        = adminRot;
            LastAdminScale           = adminScale;
            HasReceivedAdminTransform = true;

            SpaceInteractionController sic = SpaceInteractionController.Instance;
            if (sic != null)
            {
                sic.ApplyRemoteTransform(adminPos, adminRot, adminScale);
            }
        }
    }
}
