using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(PhotonView))]
    public class UserPresence : MonoBehaviourPunCallbacks
    {
        public static UserPresence Instance { get; private set; }

        [Header("Broadcast Settings")]
        [SerializeField] private float broadcastInterval = 0.1f; // 100ms (10Hz)

        [Header("Pointer Prefab (Optional)")]
        [Tooltip("If unassigned, a 3D pointer mesh with label will be generated automatically.")]
        [SerializeField] private GameObject pointerPrefab;

        private float _broadcastTimer;
        private Dictionary<int, GameObject> _remotePointers = new Dictionary<int, GameObject>();

        public static readonly Color[] PointerColors = new Color[]
        {
            new Color(0.25f, 0.60f, 1.00f), // Blue (Player 1)
            new Color(0.95f, 0.30f, 0.30f), // Red (Player 2)
            new Color(0.30f, 0.85f, 0.45f), // Green (Player 3)
            new Color(1.00f, 0.85f, 0.20f), // Yellow (Player 4)
            new Color(0.70f, 0.35f, 0.95f), // Purple (Player 5)
        };

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            var pv = GetComponent<PhotonView>();
            if (pv != null && pv.ViewID == 0)
            {
                pv.ViewID = 998;
            }
        }

        public GameObject GetPointerObject(int actorNumber)
        {
            _remotePointers.TryGetValue(actorNumber, out GameObject pointer);
            return pointer;
        }

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
                GameObject go;
                if (pointerPrefab != null)
                {
                    go = Instantiate(pointerPrefab, position, Quaternion.identity);
                }
                else
                {
                    go = CreateProceduralPointer(actorNumber, info.Sender);
                }

                _remotePointers[actorNumber] = go;

                Color col = PointerColors[(actorNumber - 1) % PointerColors.Length];
                Renderer r = go.GetComponentInChildren<Renderer>();
                if (r != null && r.material != null)
                {
                    r.material.color = col;
                }

                Debug.Log($"[AuraSync Presence] Created pointer for Actor {actorNumber}");
            }

            GameObject pointer = _remotePointers[actorNumber];
            if (pointer != null)
            {
                pointer.transform.position = Vector3.Lerp(pointer.transform.position, position, Time.deltaTime * 15f);
                if (forward.sqrMagnitude > 0.001f)
                {
                    pointer.transform.rotation = Quaternion.Slerp(pointer.transform.rotation, Quaternion.LookRotation(forward), Time.deltaTime * 15f);
                }
            }
        }

        private GameObject CreateProceduralPointer(int actorNumber, Player player)
        {
            GameObject container = new GameObject($"Pointer_Player_{actorNumber}");
            
            // Pointer Cone/Pyramid
            GameObject cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            if (cone == null) cone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            
            cone.transform.SetParent(container.transform);
            cone.transform.localPosition = Vector3.zero;
            cone.transform.localRotation = Quaternion.Euler(90, 0, 0);
            cone.transform.localScale = new Vector3(0.12f, 0.25f, 0.12f);

            // Destroy collider to avoid raycast interference
            Collider c = cone.GetComponent<Collider>();
            if (c != null) Destroy(c);

            Color playerColor = PointerColors[(actorNumber - 1) % PointerColors.Length];
            Renderer rend = cone.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Standard"));
                rend.material.color = playerColor;
            }

            // Name Label
            GameObject labelGo = new GameObject("PlayerLabel");
            labelGo.transform.SetParent(container.transform);
            labelGo.transform.localPosition = new Vector3(0, 0.2f, 0);
            
            var text = labelGo.AddComponent<TextMeshPro>();
            string roleTag = (actorNumber == NetworkManager.Instance.AdminActorNumber) ? " (Host)" : "";
            string displayName = string.IsNullOrEmpty(player.NickName) ? $"Player {actorNumber}" : player.NickName;
            text.text = $"{displayName}{roleTag}";
            text.fontSize = 2.5f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;

            // Face Camera helper
            labelGo.AddComponent<Notes.FaceCamera>();

            return container;
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            int actorNumber = otherPlayer.ActorNumber;
            if (_remotePointers.TryGetValue(actorNumber, out GameObject pointer))
            {
                if (pointer != null) Destroy(pointer);
                _remotePointers.Remove(actorNumber);
                Debug.Log($"[AuraSync Presence] Removed pointer for Actor {actorNumber}");
            }
        }
    }
}

