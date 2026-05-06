using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(PhotonView))]
    public class NetworkManager : MonoBehaviourPunCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        [SerializeField] private string roomName = "AuraSyncRoom";
        [SerializeField] private string avatarPrefabName = "PlayerAvatar"; // Must be in a Resources folder

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            Debug.Log("[AuraSync Network] Connecting to Photon...");
            
            // Force TCP instead of UDP to bypass Windows Firewall/Network timeouts
            if (PhotonNetwork.PhotonServerSettings != null)
            {
                PhotonNetwork.PhotonServerSettings.AppSettings.Protocol = ExitGames.Client.Photon.ConnectionProtocol.Tcp;
            }
            
            PhotonNetwork.ConnectUsingSettings();
        }

        public override void OnConnectedToMaster()
        {
            Debug.Log("[AuraSync Network] Connected to Master. Joining room...");
            RoomOptions roomOptions = new RoomOptions { MaxPlayers = 20 };
            PhotonNetwork.JoinOrCreateRoom(roomName, roomOptions, TypedLobby.Default);
        }

        public override void OnJoinedRoom()
        {
            Debug.Log($"[AuraSync Network] Joined room: {PhotonNetwork.CurrentRoom.Name}");
            
            if (!string.IsNullOrEmpty(avatarPrefabName))
            {
                // Instantiate our player avatar (pointer) for other players to see
                PhotonNetwork.Instantiate(avatarPrefabName, Vector3.zero, Quaternion.identity);
            }
        }
        
        // ──────────────────────────────────────────
        //  RPCs for Real-Time Item Placement
        // ──────────────────────────────────────────
        
        [PunRPC]
        public void RpcPlaceNote(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.NoteItemData>(jsonConfig);
            if (config == null) return;
            Debug.Log($"[AuraSync Network] Received RPC to place Note {config.id}");
            Core.ARPlacementController.Instance?.NetworkPlaceNote(config);
        }

        [PunRPC]
        public void RpcPlaceObject(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.ObjectItemData>(jsonConfig);
            if (config == null) return;
            Debug.Log($"[AuraSync Network] Received RPC to place Object {config.id}");
            Core.ARPlacementController.Instance?.NetworkPlaceObject(config);
        }
    }
}
