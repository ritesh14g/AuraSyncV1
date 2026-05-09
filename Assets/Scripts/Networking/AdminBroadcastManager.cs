using UnityEngine;
using Photon.Pun;

namespace AuraSync.Networking
{
    /// <summary>
    /// Admin broadcasts 3D model transform updates to all participants.
    /// Attach to same GameObject as NetworkManager.
    /// </summary>
    public class AdminBroadcastManager : MonoBehaviourPun
    {
        public static AdminBroadcastManager Instance { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Note: We don't necessarily want to Destroy here if there are multiple managers in different scenes,
                // but for AuraSync's pattern, a single global instance is expected.
                Instance = this;
                return;
            }
            Instance = this;
        }
        [Header("Broadcast Target")]
        [Tooltip("The 3D model in the scene that admin controls. Set at runtime via SetBroadcastTarget().")]
        private Transform _modelTransform;

        [Header("Broadcast Settings")]
        [SerializeField] private float broadcastInterval = 0.05f; // 50ms for smooth sync
        
        private float _broadcastTimer;
        private bool  _isBroadcasting = false;

        void Start()
        {
            // MOCK: find any object tagged "MockModel" for testing before M2 delivers real model
            var mock = GameObject.FindWithTag("MockModel");
            if (mock != null) SetBroadcastTarget(mock.transform);
        }

        public void SetBroadcastTarget(Transform modelTransform)
        {
            _modelTransform = modelTransform;
            Debug.Log($"[AuraSync] AdminBroadcast: Target set to {(_modelTransform != null ? _modelTransform.name : "null")}");
        }

        public void StartBroadcasting()
        {
            if (ZoneManager.Instance != null && !ZoneManager.Instance.CanBroadcast())
            {
                Debug.LogWarning("[AuraSync] AdminBroadcast: Only Admin can start broadcasting!");
                return;
            }

            _isBroadcasting = true;
            Debug.Log("[AuraSync] AdminBroadcast: Broadcasting started");
        }

        public void StopBroadcasting()
        {
            _isBroadcasting = false;
            Debug.Log("[AuraSync] AdminBroadcast: Broadcasting stopped");
        }

        void Update()
        {
            if (!_isBroadcasting || _modelTransform == null || !PhotonNetwork.InRoom) return;

            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= broadcastInterval)
            {
                _broadcastTimer = 0;
                SendTransformUpdate();
            }
        }

        private void SendTransformUpdate()
        {
            Vector3 pos = _modelTransform.position;
            Vector3 rot = _modelTransform.eulerAngles;
            Vector3 scale = _modelTransform.localScale;

            float[] posArr = { pos.x, pos.y, pos.z };
            float[] rotArr = { rot.x, rot.y, rot.z };
            float[] scaleArr = { scale.x, scale.y, scale.z };

            photonView.RPC("RpcReceiveModelTransform", RpcTarget.Others, posArr, rotArr, scaleArr);
        }

        [PunRPC]
        public void RpcReceiveModelTransform(float[] pos, float[] rot, float[] scale)
        {
            if (_modelTransform == null)
            {
                // Try to find it if not set
                var mock = GameObject.FindWithTag("MockModel");
                if (mock != null) 
                {
                    _modelTransform = mock.transform;
                }
                else
                {
                    Debug.LogWarning("[AuraSync] AdminBroadcast: Model transform is null on receiver. No MockModel found.");
                    return;
                }
            }

            _modelTransform.position = new Vector3(pos[0], pos[1], pos[2]);
            _modelTransform.rotation = Quaternion.Euler(rot[0], rot[1], rot[2]);
            _modelTransform.localScale = new Vector3(scale[0], scale[1], scale[2]);

            Debug.Log("[AuraSync] AdminBroadcast: Received transform update");
        }
    }
}
