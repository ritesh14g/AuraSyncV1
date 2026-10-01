using System;
using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(PhotonView))]
    public class NetworkManager : MonoBehaviourPunCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        public const string KeyAdminActor = "AdminActorNumber";
        public const string KeyRoomCode   = "RoomCode";

        public event Action<string> OnRoomJoinedSuccess;
        public event Action<string> OnRoomJoinFailedError;
        public event Action OnPlayerListUpdated;

        [SerializeField] private string avatarPrefabName = "PlayerAvatar"; // Must be in a Resources folder

        private string _pendingRoomToJoin = null;
        private bool _isConnectingToCreate = false;

        public string CurrentRoomCode { get; private set; } = "";
        public int AdminActorNumber { get; private set; } = -1;

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
            // Force fixed region and app version so all devices connect to the exact same matchmaking pool
            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "asia";
            PhotonNetwork.PhotonServerSettings.AppSettings.AppVersion = "1.0";

            if (!PhotonNetwork.IsConnected)
            {
                Debug.Log("[AuraSync Network] Pre-connecting to Photon (Region: asia, AppVersion: 1.0)...");
                PhotonNetwork.ConnectUsingSettings();
            }
        }

        public void CreateRoom(string customCode = null)
        {
            string code = string.IsNullOrEmpty(customCode) ? RoomCodeGenerator.GenerateCode() : RoomCodeGenerator.FormatCode(customCode);
            CurrentRoomCode = code;

            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "asia";
            PhotonNetwork.PhotonServerSettings.AppSettings.AppVersion = "1.0";

            if (!PhotonNetwork.IsConnectedAndReady)
            {
                _isConnectingToCreate = true;
                PhotonNetwork.ConnectUsingSettings();
                return;
            }

            DoCreateRoom(code);
        }

        private void DoCreateRoom(string code)
        {
            RoomOptions roomOptions = new RoomOptions
            {
                MaxPlayers = 5,
                PublishUserId = true,
                IsVisible = true,
                IsOpen = true,
                CustomRoomProperties = new ExitGames.Client.Photon.Hashtable
                {
                    { KeyRoomCode, code },
                    { KeyAdminActor, PhotonNetwork.LocalPlayer.ActorNumber }
                },
                CustomRoomPropertiesForLobby = new string[] { KeyRoomCode }
            };

            Debug.Log($"[AuraSync Network] Creating Room: {code} (Max 5 Players, Region asia)...");
            PhotonNetwork.CreateRoom(code, roomOptions, TypedLobby.Default);
        }

        public void JoinRoom(string roomCode)
        {
            // Clean input in case a full invite URL (e.g. https://aurasync.app/join?room=AURA-ZV7Y) was passed
            string extracted = RoomLinkManager.ExtractRoomCode(roomCode);
            string formattedCode = RoomCodeGenerator.FormatCode(extracted);

            if (!RoomCodeGenerator.IsValidCode(formattedCode))
            {
                OnRoomJoinFailedError?.Invoke("Invalid Room Code! Format must be AURA-XXXX");
                return;
            }

            CurrentRoomCode = formattedCode;
            _pendingRoomToJoin = formattedCode;

            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "asia";
            PhotonNetwork.PhotonServerSettings.AppSettings.AppVersion = "1.0";

            if (!PhotonNetwork.IsConnectedAndReady)
            {
                _isConnectingToCreate = false;
                PhotonNetwork.ConnectUsingSettings();
                return;
            }

            DoJoinRoom(formattedCode);
        }

        private void DoJoinRoom(string code)
        {
            Debug.Log($"[AuraSync Network] Joining Room: {code} (Region asia)...");
            PhotonNetwork.JoinRoom(code);
        }

        public override void OnConnectedToMaster()
        {
            Debug.Log("[AuraSync Network] Connected to Master.");
            if (_isConnectingToCreate)
            {
                _isConnectingToCreate = false;
                DoCreateRoom(CurrentRoomCode);
            }
            else if (!string.IsNullOrEmpty(_pendingRoomToJoin))
            {
                string target = _pendingRoomToJoin;
                _pendingRoomToJoin = null;
                DoJoinRoom(target);
            }
        }

        public override void OnJoinedRoom()
        {
            CurrentRoomCode = PhotonNetwork.CurrentRoom.Name;
            Debug.Log($"[AuraSync Network] Joined room: {CurrentRoomCode} ({PhotonNetwork.CurrentRoom.PlayerCount}/5 Players)");

            if (PhotonNetwork.IsMasterClient)
            {
                AdminActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable { { KeyAdminActor, AdminActorNumber } };
                PhotonNetwork.CurrentRoom.SetCustomProperties(props);
                if (ZoneManager.Instance != null) ZoneManager.Instance.SetAdminStatus(true);
            }
            else
            {
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyAdminActor, out object adminActor))
                {
                    AdminActorNumber = (int)adminActor;
                    bool isAdmin = (PhotonNetwork.LocalPlayer.ActorNumber == AdminActorNumber);
                    if (ZoneManager.Instance != null) ZoneManager.Instance.SetAdminStatus(isAdmin);
                }
            }

            // Spawn avatar pointer prefab if found in Resources
            if (!string.IsNullOrEmpty(avatarPrefabName))
            {
                try
                {
                    PhotonNetwork.Instantiate(avatarPrefabName, Vector3.zero, Quaternion.identity);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AuraSync Network] PlayerAvatar instantiation note: {ex.Message}. Procedural pointers will be used as fallback.");
                }
            }

            OnRoomJoinedSuccess?.Invoke(CurrentRoomCode);
            OnPlayerListUpdated?.Invoke();
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            Debug.LogError($"[AuraSync Network] Create Room Failed ({returnCode}): {message}");
            OnRoomJoinFailedError?.Invoke($"Create Room Failed: {message}");
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            Debug.LogError($"[AuraSync Network] Join Room Failed ({returnCode}): {message}");
            string err = message;
            if (message.Contains("Full")) err = "Room is Full! Maximum 5 players allowed.";
            else if (message.Contains("exist")) err = $"Room {CurrentRoomCode} not found! Make sure the host has created the room.";

            OnRoomJoinFailedError?.Invoke($"Join Failed: {err}");
        }

        public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
        {
            if (propertiesThatChanged.ContainsKey(KeyAdminActor))
            {
                AdminActorNumber = (int)propertiesThatChanged[KeyAdminActor];
                bool isAdmin = (PhotonNetwork.LocalPlayer.ActorNumber == AdminActorNumber);
                if (ZoneManager.Instance != null) ZoneManager.Instance.SetAdminStatus(isAdmin);
            }
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            Debug.Log($"[AuraSync Network] Player Joined: {newPlayer.NickName} (Actor {newPlayer.ActorNumber})");
            OnPlayerListUpdated?.Invoke();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            Debug.Log($"[AuraSync Network] Player Left: {otherPlayer.NickName} (Actor {otherPlayer.ActorNumber})");
            OnPlayerListUpdated?.Invoke();
        }

        // RPCs for Note & Object placement sync
        [PunRPC]
        public void RpcPlaceNote(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.NoteItemData>(jsonConfig);
            if (config == null) return;
            Core.ARPlacementController.Instance?.NetworkPlaceNote(config);
        }

        [PunRPC]
        public void RpcPlaceObject(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.ObjectItemData>(jsonConfig);
            if (config == null) return;
            Core.ARPlacementController.Instance?.NetworkPlaceObject(config);
        }
    }
}

