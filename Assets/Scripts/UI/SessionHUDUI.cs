using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;
using UnityEngine.SceneManagement;

namespace AuraSync.UI
{
    /// <summary>
    /// Thin HUD that lives in the AR (V1) scene only.
    /// In SpaceScene, SpaceSessionHUD handles the multi-user controls instead.
    /// This script guards itself: if the active scene is SpaceScene it stays hidden.
    /// </summary>
    public class SessionHUDUI : MonoBehaviour
    {
        public static SessionHUDUI Instance { get; private set; }

        [Header("Space Scene Guard")]
        [Tooltip("If true, this HUD hides itself when running in SpaceScene (SpaceSessionHUD takes over).")]
        [SerializeField] private bool hideInSpaceScene = true;
        [SerializeField] private string spaceSceneName = "SpaceScene";

        [Header("UI Canvas & Panels")]
        [SerializeField] private GameObject hudContainer;
        [SerializeField] private TextMeshProUGUI roomCodeText;
        [SerializeField] private TextMeshProUGUI playerCountText;
        [SerializeField] private TextMeshProUGUI adminBadgeText;
        [SerializeField] private Button copyCodeButton;
        [SerializeField] private Button micButton;
        [SerializeField] private Image micIconImage;
        [SerializeField] private Button followAdminButton;
        [SerializeField] private Image followAdminIconImage;
        [SerializeField] private Button lobbyButton;

        [Header("Colors")]
        [SerializeField] private Color activeColor = new Color(0.2f, 0.8f, 0.4f, 1.0f);
        [SerializeField] private Color inactiveColor = new Color(0.8f, 0.2f, 0.2f, 1.0f);
        [SerializeField] private Color defaultBtnColor = new Color(0.2f, 0.2f, 0.25f, 0.8f);

        private TextMeshProUGUI _micBtnText;
        private TextMeshProUGUI _followBtnText;
        private bool _isHiddenForSpaceScene = false;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // NOTE: No DontDestroyOnLoad — this HUD belongs to the AR scene only.
            // SpaceScene has its own SpaceSessionHUD.

            CheckSpaceSceneGuard();

            if (!_isHiddenForSpaceScene)
            {
                EnsureProceduralHUD();
            }
        }

        void Start()
        {
            if (_isHiddenForSpaceScene) return;

            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomJoinedSuccess += HandleRoomJoined;
                NetworkManager.Instance.OnPlayerListUpdated += RefreshHUD;
            }

            if (VoiceChatManager.Instance != null)
            {
                VoiceChatManager.Instance.OnMicMuteStateChanged += HandleMicStateChanged;
            }

            if (FollowAdminController.Instance != null)
            {
                FollowAdminController.Instance.OnFollowAdminStateChanged += HandleFollowStateChanged;
            }

            copyCodeButton?.onClick.AddListener(CopyRoomCode);
            micButton?.onClick.AddListener(ToggleMic);
            followAdminButton?.onClick.AddListener(ToggleFollowAdmin);
            lobbyButton?.onClick.AddListener(OpenRoomLobby);

            RefreshHUD();
        }

        void OnDestroy()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomJoinedSuccess -= HandleRoomJoined;
                NetworkManager.Instance.OnPlayerListUpdated -= RefreshHUD;
            }
        }

        private void CheckSpaceSceneGuard()
        {
            if (!hideInSpaceScene) return;

            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == spaceSceneName)
            {
                _isHiddenForSpaceScene = true;
                gameObject.SetActive(false);
                Debug.Log("[SessionHUDUI] SpaceScene detected — disabling AR HUD. SpaceSessionHUD will handle multi-user.");
            }
        }

        private void EnsureProceduralHUD()
        {
            if (hudContainer != null) return;

            // Build runtime HUD Canvas if not assigned in inspector
            GameObject canvasGo = new GameObject("SessionHUDCanvas");
            canvasGo.transform.SetParent(transform);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvasGo.AddComponent<GraphicRaycaster>();

            // Main Panel (Top Right HUD)
            GameObject panel = new GameObject("HUDPanel");
            panel.transform.SetParent(canvasGo.transform, false);

            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, 0.90f);
            rt.anchorMax = new Vector2(0.98f, 0.98f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.10f, 0.15f, 0.85f);

            HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 10, 10);
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            GameObject roomSection = CreateHUDText(panel.transform, "RoomCodeText", "Room: Offline", 28);
            roomCodeText = roomSection.GetComponent<TextMeshProUGUI>();

            copyCodeButton = CreateHUDButton(panel.transform, "CopyBtn", "Copy Invite", () => CopyRoomCode());

            GameObject playerSection = CreateHUDText(panel.transform, "PlayerCountText", "1/5 Players", 24);
            playerCountText = playerSection.GetComponent<TextMeshProUGUI>();

            micButton = CreateHUDButton(panel.transform, "MicBtn", "Mic: ON", () => ToggleMic());
            _micBtnText = micButton.GetComponentInChildren<TextMeshProUGUI>();

            followAdminButton = CreateHUDButton(panel.transform, "FollowBtn", "Follow Admin", () => ToggleFollowAdmin());
            _followBtnText = followAdminButton.GetComponentInChildren<TextMeshProUGUI>();

            lobbyButton = CreateHUDButton(panel.transform, "LobbyBtn", "Rooms", () => OpenRoomLobby());

            GameObject badgeSection = CreateHUDText(panel.transform, "AdminBadgeText", "[Viewer]", 22);
            adminBadgeText = badgeSection.GetComponent<TextMeshProUGUI>();

            hudContainer = panel;
        }

        private GameObject CreateHUDText(Transform parent, string name, string defaultText, float fontSize)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = defaultText;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            return go;
        }

        private Button CreateHUDButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            img.color = defaultBtnColor;

            Button btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);

            RectTransform rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 20;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = 120;
            le.minHeight = 50;

            return btn;
        }

        public void HandleRoomJoined(string roomCode) => RefreshHUD();

        public void RefreshHUD()
        {
            if (_isHiddenForSpaceScene) return;

            if (!PhotonNetwork.InRoom)
            {
                if (hudContainer) hudContainer.SetActive(false);
                return;
            }

            if (hudContainer) hudContainer.SetActive(true);

            string code = PhotonNetwork.CurrentRoom.Name;
            int count = PhotonNetwork.CurrentRoom.PlayerCount;
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

            if (roomCodeText) roomCodeText.text = $"Room: <color=#6290FF><b>{code}</b></color>";
            if (playerCountText) playerCountText.text = $"<color=#00FFCC>{count}/5 Players</color>";
            if (adminBadgeText) adminBadgeText.text = isAdmin ? "<color=#FFD700>[ADMIN]</color>" : "<color=#AAAAAA>[VIEWER]</color>";

            if (followAdminButton != null)
            {
                followAdminButton.gameObject.SetActive(!isAdmin);
            }
        }

        public void CopyRoomCode()
        {
            if (!PhotonNetwork.InRoom) return;
            string code = PhotonNetwork.CurrentRoom.Name;
            string inviteLink = RoomLinkManager.CreateInviteLink(code);
            if (string.IsNullOrEmpty(inviteLink)) return;
            GUIUtility.systemCopyBuffer = inviteLink;
            var feedback = FindObjectOfType<PlacementFeedbackUI>();
            if (feedback != null) feedback.Show($"Invite Link Copied! {code}");
            else Debug.Log($"[AuraSync HUD] Copied invite link: {inviteLink}");
        }

        public void ToggleMic() => VoiceChatManager.Instance?.ToggleMute();

        private void HandleMicStateChanged(bool isMuted)
        {
            if (_micBtnText != null) _micBtnText.text = isMuted ? "Mic: OFF" : "Mic: ON";
            if (micButton != null) micButton.GetComponent<Image>().color = isMuted ? inactiveColor : activeColor;
        }

        public void ToggleFollowAdmin() => FollowAdminController.Instance?.ToggleFollowAdmin();

        private void HandleFollowStateChanged(bool isFollowing)
        {
            if (_followBtnText != null) _followBtnText.text = isFollowing ? "Following" : "Follow Admin";
            if (followAdminButton != null) followAdminButton.GetComponent<Image>().color = isFollowing ? activeColor : defaultBtnColor;
        }

        public void OpenRoomLobby() => RoomLobbyUI.Instance?.ShowModal();
    }
}
