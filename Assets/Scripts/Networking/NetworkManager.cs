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

        /// <summary>
        /// How long the server keeps a dropped player's place in the room (ms). Without it, a phone
        /// whose connection timed out was removed for good: the presenter's session simply ended
        /// while the viewers stayed, and a viewer took over as presenter (2026-10-16, twice on 4G).
        /// Now the dropped player shows as inactive, keeps the presenter role, and rejoins as the
        /// same player. Must cover timeout detection (~10 s) plus the reconnect attempts below.
        /// </summary>
        public const int PlayerTtlMs    = 60000;
        /// <summary>Keeps the room alive if everyone drops at once (e.g. both phones on one bad network).</summary>
        public const int EmptyRoomTtlMs = 60000;
        /// <summary>Give up reconnecting after this long and report the disconnect.</summary>
        private const float ReconnectGiveUpSeconds = 50f;

        /// <summary>Raised when this device loses the room and starts (true) or stops (false) trying to get back.</summary>
        public event Action<bool> OnReconnectingChanged;
        /// <summary>True while this device is reconnecting to the room it dropped out of.</summary>
        public bool IsReconnecting => _rejoinRoom != null;

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

        // Reconnect state: the room being returned to (null when not reconnecting).
        private string _rejoinRoom;
        private float  _rejoinStartedAt;
        private int    _rejoinAttempt;
        private bool   _rejoinAsNewPlayer; // the old place is gone: join again as a new player
        private Coroutine _rejoinRetry;

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
                PlayerTtl = PlayerTtlMs,
                EmptyRoomTtl = EmptyRoomTtlMs,
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
            bool wasReconnecting = IsReconnecting;
            StopReconnecting();
            // becomeInactive: false. With PlayerTtl, a plain LeaveRoom() would only mark this player
            // inactive, and everyone else would wait a minute for a presenter who left on purpose.
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom(false);
            else if (wasReconnecting)
            {
                if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();
                ResetRoomState();
            }
        }

        // ──────────────────────────────────────────
        //  Reconnecting after a dropped connection
        // ──────────────────────────────────────────

        /// <summary>Drops worth retrying: the network, not the server refusing this client.</summary>
        private static bool IsRecoverable(DisconnectCause cause)
        {
            switch (cause)
            {
                case DisconnectCause.ClientTimeout:
                case DisconnectCause.ServerTimeout:
                case DisconnectCause.Exception:
                case DisconnectCause.ExceptionOnConnect:
                case DisconnectCause.DnsExceptionOnConnect:
                case DisconnectCause.DisconnectByServerReasonUnknown:
                    return true;
                default:
                    return false;
            }
        }

        private void BeginReconnect(string room, DisconnectCause cause)
        {
            _rejoinRoom        = room;
            _rejoinStartedAt   = Time.unscaledTime;
            _rejoinAttempt     = 0;
            _rejoinAsNewPlayer = false;
            Debug.LogWarning($"[AuraSync Network] Lost room {room} ({cause}). Reconnecting...");
            AuraSync.UI.Toast.Show("Connection lost. Reconnecting...");
            OnReconnectingChanged?.Invoke(true);
            TryReconnect();
        }

        /// <summary>One reconnect attempt; a failure comes back through OnDisconnected or OnJoinRoomFailed.</summary>
        private void TryReconnect()
        {
            _rejoinRetry = null;
            if (_rejoinRoom == null) return;
            _rejoinAttempt++;

            if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom)
            {
                RejoinOnMaster(); // already back on the master server
                return;
            }
            if (PhotonNetwork.IsConnected) return; // a connection is in progress; its callback carries on

            // Straight back to the room's game server first; otherwise via the master server.
            bool started = !_rejoinAsNewPlayer && PhotonNetwork.ReconnectAndRejoin();
            if (!started) started = PhotonNetwork.Reconnect();
            if (!started) started = PhotonNetwork.ConnectUsingSettings();
            Debug.Log($"[AuraSync Network] Reconnect attempt {_rejoinAttempt} ({(started ? "started" : "could not start")}).");
            if (!started) ScheduleReconnect();
        }

        private void RejoinOnMaster()
        {
            if (_rejoinAsNewPlayer) PhotonNetwork.JoinRoom(_rejoinRoom);
            else                    PhotonNetwork.RejoinRoom(_rejoinRoom);
        }

        /// <summary>Next attempt after 1, 2, 4, 5, 5... s, until the give-up time.</summary>
        private void ScheduleReconnect()
        {
            if (_rejoinRoom == null) return;
            if (Time.unscaledTime - _rejoinStartedAt > ReconnectGiveUpSeconds)
            {
                GiveUpReconnecting();
                return;
            }
            float delay = Mathf.Min(5f, Mathf.Pow(2f, Mathf.Max(0, _rejoinAttempt - 1)));
            if (_rejoinRetry != null) StopCoroutine(_rejoinRetry);
            _rejoinRetry = StartCoroutine(RetryAfter(delay));
        }

        private IEnumerator RetryAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            TryReconnect();
        }

        private void StopReconnecting()
        {
            if (_rejoinRetry != null) StopCoroutine(_rejoinRetry);
            _rejoinRetry = null;
            bool was = _rejoinRoom != null;
            _rejoinRoom = null;
            if (was) OnReconnectingChanged?.Invoke(false);
        }

        private void GiveUpReconnecting()
        {
            Debug.LogWarning($"[AuraSync Network] Could not get back into {_rejoinRoom} within {ReconnectGiveUpSeconds:F0} s. Giving up.");
            StopReconnecting();
            ResetRoomState();
            OnRoomJoinFailedError?.Invoke("Disconnected from room (connection lost).");
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
            if (IsReconnecting)
            {
                RejoinOnMaster();
                return;
            }
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
            if (IsReconnecting)
            {
                Debug.Log($"[AuraSync Network] Back in room {PhotonNetwork.CurrentRoom.Name} after " +
                          $"{Time.unscaledTime - _rejoinStartedAt:F1} s (attempt {_rejoinAttempt}, " +
                          $"{(PhotonNetwork.LocalPlayer.HasRejoined ? "same player" : "as a new player")}).");
                StopReconnecting();
                AuraSync.UI.Toast.Show("Reconnected.");
            }

            CurrentRoomCode = PhotonNetwork.CurrentRoom.Name;
            Debug.Log($"[AuraSync Network] Joined room: {CurrentRoomCode} ({PhotonNetwork.CurrentRoom.PlayerCount}/{MaxPlayers} Players)");

            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyAdminActor, out object existingAdmin) &&
                existingAdmin is int adminNr && PhotonNetwork.CurrentRoom.GetPlayer(adminNr) != null)
            {
                // The room already has a presenter - after a rejoin, usually this player again. (The
                // master client may now be someone else: it moved on while this player was away.)
                ApplyAdminActor(adminNr);
            }
            else if (PhotonNetwork.IsMasterClient)
            {
                // First person in the room is the presenter.
                SetAdminActor(PhotonNetwork.LocalPlayer.ActorNumber);
            }
            else if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyAdminActor, out object adminActor))
            {
                ApplyAdminActor((int)adminActor);
            }

            SharedSpaceOrigin.Refresh();

            // Spawn avatar pointer prefab if found in Resources. A rejoining player gets its old one
            // back from the room's cache, so a second one would be a duplicate.
            if (!string.IsNullOrEmpty(avatarPrefabName) && !PhotonNetwork.LocalPlayer.HasRejoined)
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

            // A reconnect attempt that failed: try again (the room state is kept meanwhile).
            if (IsReconnecting)
            {
                ScheduleReconnect();
                return;
            }

            // Dropped out of a room by the network: keep the room state (presenter role, model, HUD)
            // and get back in, instead of ending the session.
            if (!string.IsNullOrEmpty(CurrentRoomCode) && IsRecoverable(cause))
            {
                BeginReconnect(CurrentRoomCode, cause);
                return;
            }

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
            if (IsReconnecting)
            {
                Debug.LogWarning($"[AuraSync Network] Rejoin attempt {_rejoinAttempt} failed ({returnCode}): {message}");
                if (returnCode == ErrorCode.JoinFailedFoundActiveJoiner)
                {
                    // The server has not noticed the drop yet and still counts the old connection.
                    ScheduleReconnect();
                }
                else if (returnCode == ErrorCode.JoinFailedWithRejoinerNotFound && !_rejoinAsNewPlayer)
                {
                    // The old place is gone (a room made before PlayerTtl existed, or it expired): join afresh.
                    _rejoinAsNewPlayer = true;
                    ScheduleReconnect();
                }
                else
                {
                    GiveUpReconnecting(); // the room is gone or full
                }
                return;
            }

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
            // Inactive = their connection dropped and the room holds their place (PlayerTtl). They stay
            // in the player list, so the presenter role is not handed over while they reconnect.
            Debug.Log($"[AuraSync Network] Player {(otherPlayer.IsInactive ? "dropped (may rejoin)" : "Left")}: " +
                      $"{DisplayName(otherPlayer)} (Actor {otherPlayer.ActorNumber})");
            if (otherPlayer.IsInactive && otherPlayer.ActorNumber == AdminActorNumber)
                AuraSync.UI.Toast.Show($"{DisplayName(otherPlayer)}'s connection dropped. Waiting for them to reconnect...");
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
