using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

namespace AuraSync.Networking
{
    /// <summary>
    /// Broadcasts local user presence (camera pos/gaze) and displays remote user pointers.
    /// Attach to the same GameObject as NetworkManager.
    /// </summary>
    public class UserPresence : MonoBehaviourPunCallbacks
    {
        [Header("Broadcast Settings")]
        [SerializeField] private float broadcastInterval = 0.1f; // 100ms

        [Header("Pointer Prefab")]
        [Tooltip("Assign a simple arrow/cone prefab. Must be in Resources folder.")]
        [SerializeField] private GameObject pointerPrefab;

        private float _broadcastTimer;
        private Dictionary<int, GameObject> _remotePointers = new Dictionary<int, GameObject>();

        private static readonly Color[] PointerColors = new Color[]
        {
            new Color(0.384f, 0.561f, 1.00f), // Blue  — player 0
            new Color(0.95f,  0.27f,  0.27f), // Red   — player 1
            new Color(0.30f,  0.85f,  0.45f), // Green — player 2
            new Color(1.00f,  0.85f,  0.20f), // Yellow — player 3
            new Color(0.65f,  0.35f,  0.95f), // Purple — player 4
        };

        void Update()
        {
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;

            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= broadcastInterval)
            {
                _broadcastTimer = 0;
                BroadcastPresence();
            }
        }

        private void BroadcastPresence()
        {
            if (Camera.main == null) return;

            Vector3 pos = Camera.main.transform.position;
            Vector3 fwd = Camera.main.transform.forward;

            float[] posArr = new float[] { pos.x, pos.y, pos.z };
            float[] fwdArr = new float[] { fwd.x, fwd.y, fwd.z };

            photonView.RPC("RpcUpdatePresence", RpcTarget.Others, posArr, fwdArr);
        }

        [PunRPC]
        public void RpcUpdatePresence(float[] pos, float[] fwd, PhotonMessageInfo info)
        {
            int actorNumber = info.Sender.ActorNumber;
            Vector3 position = new Vector3(pos[0], pos[1], pos[2]);
            Vector3 forward  = new Vector3(fwd[0], fwd[1], fwd[2]);

            if (!_remotePointers.ContainsKey(actorNumber))
            {
                if (pointerPrefab == null)
                {
                    Debug.LogWarning("[AuraSync] Presence: pointerPrefab is not assigned!");
                    return;
                }

                GameObject go = Instantiate(pointerPrefab, position, Quaternion.identity);
                _remotePointers[actorNumber] = go;

                var renderer = go.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = PointerColors[actorNumber % PointerColors.Length];
                }

                go.name = string.IsNullOrEmpty(info.Sender.NickName) 
                    ? $"Player {actorNumber}" 
                    : info.Sender.NickName;

                Debug.Log($"[AuraSync] Presence: Created pointer for Actor {actorNumber}");
            }

            GameObject pointer = _remotePointers[actorNumber];
            pointer.transform.position = position;
            pointer.transform.rotation = Quaternion.LookRotation(forward);

            Debug.Log($"[AuraSync] Presence: Actor {actorNumber} at {position}");
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            int actorNumber = otherPlayer.ActorNumber;
            if (_remotePointers.TryGetValue(actorNumber, out GameObject pointer))
            {
                Destroy(pointer);
                _remotePointers.Remove(actorNumber);
                Debug.Log($"[AuraSync] Presence: Removed pointer for Actor {actorNumber}");
            }
        }
    }
}
