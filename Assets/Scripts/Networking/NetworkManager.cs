using System;
using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(PhotonView))]
    public class NetworkManager : MonoBehaviourPunCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        public const string KeyAdminActor = RoomKeys.AdminActor;
        public const string KeyRoomCode   = RoomKeys.RoomCode;
        public const string PrefPlayerName = "AuraSync_PlayerName";
        public const int    MaxPlayers     = 5;

        public event Action<string> OnRoomJoinedSuccess;
        public event Action<string> OnRoomJoinFailedError;
        public event Action OnPlayerListUpdated;

        [Header("Photon")]
        [Tooltip("Players must share a region to find each other. Leave empty to use the Photon settings asset.")]
        [SerializeField] private string fixedRegion = "asia";
        [SerializeField] private string appVersion  = "1.0";
        [SerializeField] private string avatarPrefabName = "PlayerAvatar"; // Must be in a Resources folder

        private string _pendingRoomToJoin = null;
        private bool _isConnectingToCreate = false;

        public string CurrentRoomCode { get; private set; } = "";
        public int AdminActorNumber { get; private set; } = -1;

        public bool IsLocalAdmin =>
            PhotonNetwork.InRoom && AdminActorNumber >= 0 &&
            PhotonNetwork.LocalPlayer.ActorNumber == AdminActorNumber;

        /// <summary>
        /// Returns the NetworkManager, creating it if it does not exist.
        ///
        /// SceneBootstrapper (which lives only in the AR scene) normally builds it at startup. This
        /// is also callable from anywhere else, so a screen that needs networking can recover when
        /// the manager is missing instead of telling the user to "try again in a moment" - which
        /// never helps, because nothing is going to create it.
        /// </summary>
        public static NetworkManager EnsureExists()
        {
            if (Instance != null) return Instance;

            GameObject go = new GameObject("AuraSync_NetworkManager");
            PhotonView pv = go.AddComponent<PhotonView>();
            pv.ViewID = 999;
            PhotonNetwork.RegisterPhotonView(pv);
            return go.AddComponent<NetworkManager>();
        }

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

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            EnsurePlayerName();
            ApplyPhotonSettings();

            if (!PhotonNetwork.IsConnected)
            {
                Debug.Log($"[AuraSync Network] Pre-connecting to Photon (Region: {fixedRegion}, AppVersion: {appVersion})...");
                PhotonNetwork.ConnectUsingSettings();
            }
        }

        // ──────────────────────────────────────────
        //  Settings & identity
        // ──────────────────────────────────────────

        private void ApplyPhotonSettings()
        {
            var settings = PhotonNetwork.PhotonServerSettings != null
                ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
            if (settings == null) return;

            // Everyone must use the same region and app version to land in the same matchmaking pool.
            if (!string.IsNullOrEmpty(fixedRegion)) settings.FixedRegion = fixedRegion;
            if (!string.IsNullOrEmpty(appVersion))  settings.AppVersion  = appVersion;
        }

        private void EnsurePlayerName()
        {
            if (!string.IsNullOrEmpty(PhotonNetwork.NickName)) return;

            string saved = PlayerPrefs.GetString(PrefPlayerName, "");
            if (string.IsNullOrWhiteSpace(saved))
            {
                saved = "User" + UnityEngine.Random.Range(100, 1000);
                PlayerPrefs.SetString(PrefPlayerName, saved);
            }
            PhotonNetwork.NickName = saved;
        }

        /// <summary>Set (and remember) the name other players see.</summary>
        public void SetPlayerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.Length > 16) name = name.Substring(0, 16);

            PlayerPrefs.SetString(PrefPlayerName, name);
            PlayerPrefs.Save();
            PhotonNetwork.NickName = name;
        }

        public string GetPlayerName() => PhotonNetwork.NickName;

        public static string DisplayName(Player p)
        {
            if (p == null) return "";
            return string.IsNullOrEmpty(p.NickName) ? $"Player {p.ActorNumber}" : p.NickName;
        }

        // ──────────────────────────────────────────
        //  Create / join / leave
        // ──────────────────────────────────────────

        public void CreateRoom(string customCode = null)
        {
            if (PhotonNetwork.InRoom)
            {
                OnRoomJoinFailedError?.Invoke("You are already in a room. Leave it first.");
                return;
            }

            string code = string.IsNullOrEmpty(customCode) ? RoomCodeGenerator.GenerateCode() : RoomCodeGenerator.FormatCode(customCode);
            CurrentRoomCode = code;
            ApplyPhotonSettings();

            if (!PhotonNetwork.IsConnectedAndReady)
            {
                _isConnectingToCreate = true;
                if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
                return;
            }

            DoCreateRoom(code);
        }

        private void DoCreateRoom(string code)
        {
            // The admin actor number is written in OnJoinedRoom; the local actor number is
            // not known until the client is actually inside the room.
            RoomOptions roomOptions = new RoomOptions
            {
                MaxPlayers = MaxPlayers,
                PublishUserId = true,
                IsVisible = true,
                IsOpen = true,
                CustomRoomProperties = new Hashtable { { KeyRoomCode, code } },
                CustomRoomPropertiesForLobby = new string[] { KeyRoomCode }
            };

            Debug.Log($"[AuraSync Network] Creating Room: {code} (Max {MaxPlayers} Players)...");
            PhotonNetwork.CreateRoom(code, roomOptions, TypedLobby.Default);
        }

        public void JoinRoom(string roomCode)
        {
            if (PhotonNetwork.InRoom)
            {
                OnRoomJoinFailedError?.Invoke("You are already in a room. Leave it first.");
                return;
            }

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
            ApplyPhotonSettings();

            if (!PhotonNetwork.IsConnectedAndReady)
            {
                _isConnectingToCreate = false;
                if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
                return;
            }

            _pendingRoomToJoin = null;
            DoJoinRoom(formattedCode);
        }

        private void DoJoinRoom(string code)
        {
            Debug.Log($"[AuraSync Network] Joining Room: {code}...");
            PhotonNetwork.JoinRoom(code);
        }

        public void LeaveRoom()
        {
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        }

        // ──────────────────────────────────────────
        //  Presenter (admin) management
        // ──────────────────────────────────────────

        /// <summary>Hand the presenter role to another player. Only the current presenter may do this.</summary>
        public bool PromoteToAdmin(int actorNumber)
        {
            if (!PhotonNetwork.InRoom || !IsLocalAdmin) return false;
            if (actorNumber == AdminActorNumber) return false;
            if (PhotonNetwork.CurrentRoom.GetPlayer(actorNumber) == null) return false;

            SetAdminActor(actorNumber);
            return true;
        }

        private void ClaimAdmin()
        {
            if (!PhotonNetwork.InRoom) return;
            SetAdminActor(PhotonNetwork.LocalPlayer.ActorNumber);
        }

        private void SetAdminActor(int actorNumber)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyAdminActor, actorNumber } });
            ApplyAdminActor(actorNumber); // apply locally right away; the property echo is idempotent
        }

        private void ApplyAdminActor(int actorNumber)
        {
            AdminActorNumber = actorNumber;
            bool isAdmin = PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer.ActorNumber == actorNumber;
            if (ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin != isAdmin)
                ZoneManager.Instance.SetAdminStatus(isAdmin);
            OnPlayerListUpdated?.Invoke();
        }

        /// <summary>If the presenter is no longer in the room, the master client takes over.</summary>
        private void EnsureAdminPresent()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
            if (AdminActorNumber < 0 || PhotonNetwork.CurrentRoom.GetPlayer(AdminActorNumber) == null)
            {
                Debug.Log("[AuraSync Network] Presenter left. Master client takes over.");
                ClaimAdmin();
            }
        }

        // ──────────────────────────────────────────
        //  Photon callbacks
        // ──────────────────────────────────────────

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
            Debug.Log($"[AuraSync Network] Joined room: {CurrentRoomCode} ({PhotonNetwork.CurrentRoom.PlayerCount}/{MaxPlayers} Players)");

            if (PhotonNetwork.IsMasterClient)
            {
                // First person in the room is the presenter.
                SetAdminActor(PhotonNetwork.LocalPlayer.ActorNumber);
            }
            else if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyAdminActor, out object adminActor))
            {
                ApplyAdminActor((int)adminActor);
            }

            SharedSpaceOrigin.Refresh();

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

        public override void OnLeftRoom()
        {
            ResetRoomState();
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            Debug.LogWarning($"[AuraSync Network] Disconnected: {cause}");
            bool hadSession = !string.IsNullOrEmpty(CurrentRoomCode);
            ResetRoomState();
            if (hadSession && cause != DisconnectCause.DisconnectByClientLogic)
                OnRoomJoinFailedError?.Invoke($"Disconnected from room ({cause}).");
        }

        private void ResetRoomState()
        {
            CurrentRoomCode = "";
            AdminActorNumber = -1;
            SharedSpaceOrigin.Clear();
            if (ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin)
                ZoneManager.Instance.SetAdminStatus(false);
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
            if (message.Contains("Full")) err = $"Room is Full! Maximum {MaxPlayers} players allowed.";
            else if (message.Contains("exist")) err = $"Room {CurrentRoomCode} not found! Make sure the host has created the room.";

            OnRoomJoinFailedError?.Invoke($"Join Failed: {err}");
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (propertiesThatChanged.ContainsKey(KeyAdminActor))
                ApplyAdminActor((int)propertiesThatChanged[KeyAdminActor]);

            if (propertiesThatChanged.ContainsKey(SharedSpaceOrigin.KeyOriginAnchor))
                SharedSpaceOrigin.Refresh();
        }

        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            // Mic mute state and similar per-player flags: let the HUDs redraw.
            OnPlayerListUpdated?.Invoke();
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            if (newMasterClient.IsLocal) EnsureAdminPresent();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            Debug.Log($"[AuraSync Network] Player Joined: {DisplayName(newPlayer)} (Actor {newPlayer.ActorNumber})");

            // One client (the master) brings the newcomer up to date with the shared AR items.
            if (PhotonNetwork.IsMasterClient)
                Core.ARPlacementController.Instance?.SendSharedItemsTo(newPlayer);

            OnPlayerListUpdated?.Invoke();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            Debug.Log($"[AuraSync Network] Player Left: {DisplayName(otherPlayer)} (Actor {otherPlayer.ActorNumber})");
            if (otherPlayer.ActorNumber == AdminActorNumber) EnsureAdminPresent();
            OnPlayerListUpdated?.Invoke();
        }

        // ──────────────────────────────────────────
        //  RPCs for Note & Object placement sync
        // ──────────────────────────────────────────

        [PunRPC]
        public void RpcPlaceNote(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.NoteItemData>(jsonConfig);
            if (config == null) return;
            Core.ARPlacementController.Instance?.NetworkPlaceNote(config);
        }

        [PunRPC]
        public void RpcUpdateNote(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.NoteItemData>(jsonConfig);
            Core.ARPlacementController.Instance?.NetworkUpdateNote(config);
        }

        [PunRPC]
        public void RpcUpdateObject(string jsonConfig)
        {
            var config = JsonUtility.FromJson<Data.ObjectItemData>(jsonConfig);
            Core.ARPlacementController.Instance?.NetworkUpdateObject(config);
        }

        [PunRPC]
        public void RpcDeleteItem(string itemId)
        {
            Core.ARPlacementController.Instance?.NetworkDeleteItem(itemId);
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
