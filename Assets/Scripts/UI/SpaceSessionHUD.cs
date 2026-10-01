using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Space-Scene-only multi-user HUD. Builds itself procedurally at runtime.
    ///
    /// Layout:
    ///   [TOP BAR]         Room code | Players | Mic toggle | Admin/Viewer badge
    ///                     Visible only when in a Photon room.
    ///
    ///   [SHARE BUTTON]    Blue pill button, top-right corner.
    ///                     Visible after model loads. Opens RoomLobbyUI.
    ///
    ///   [VIEWER BADGES]   Colored name chips, right-edge of screen.
    ///                     One per connected player (Admin sees all viewers; viewers see Admin).
    ///
    ///   [CATCH UP BTN]    Bottom-right corner, viewers only.
    ///                     Appears when the viewer manually breaks sync with Admin.
    ///                     Tapping snaps model to Admin's current state and re-locks mirror mode.
    /// </summary>
    public class SpaceSessionHUD : MonoBehaviour
    {
        public static SpaceSessionHUD Instance { get; private set; }

        // ── Player badge colors (matches UserPresence.PointerColors) ──────────
        private static readonly Color[] PlayerColors = new Color[]
        {
            new Color(0.25f, 0.60f, 1.00f), // Blue
            new Color(0.95f, 0.30f, 0.30f), // Red
            new Color(0.30f, 0.85f, 0.45f), // Green
            new Color(1.00f, 0.85f, 0.20f), // Yellow
            new Color(0.70f, 0.35f, 0.95f), // Purple
        };

        // ── Runtime UI references ──────────────────────────────────────────────
        private Canvas          _canvas;
        private GameObject      _topBar;
        private TextMeshProUGUI _roomCodeText;
        private TextMeshProUGUI _playerCountText;
        private TextMeshProUGUI _adminBadgeText;
        private Button          _micButton;
        private TextMeshProUGUI _micBtnText;
        private Button          _copyButton;

        private Button          _shareButton;       // Top-right blue pill

        private GameObject      _catchUpButton;     // Bottom-right, viewers only
        private TextMeshProUGUI _catchUpText;

        private GameObject      _leaveButton;       // Bottom-left, leave the room

        private GameObject      _badgePanel;        // Right-edge viewer badges
        private Dictionary<int, GameObject> _playerBadges = new Dictionary<int, GameObject>();

        private bool _modelLoaded = false;
        private string _currentModelPath = "";

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildHUD();
        }

        void Start()
        {
            // Subscribe to network events
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomJoinedSuccess  += OnRoomJoined;
                NetworkManager.Instance.OnPlayerListUpdated  += RefreshAll;
            }

            if (VoiceChatManager.Instance != null)
                VoiceChatManager.Instance.OnMicMuteStateChanged += OnMicStateChanged;

            // Subscribe to sync-lock changes so Catch Up button can appear/hide
            if (SpaceInteractionController.Instance != null)
                SpaceInteractionController.Instance.OnSyncLockChanged += OnSyncLockChanged;

            RefreshAll();
        }

        void OnDestroy()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomJoinedSuccess -= OnRoomJoined;
                NetworkManager.Instance.OnPlayerListUpdated -= RefreshAll;
            }
            if (VoiceChatManager.Instance != null)
                VoiceChatManager.Instance.OnMicMuteStateChanged -= OnMicStateChanged;
            if (SpaceInteractionController.Instance != null)
                SpaceInteractionController.Instance.OnSyncLockChanged -= OnSyncLockChanged;
        }

        // Called by SpaceSceneManager once the GLB finishes loading
        public void OnModelLoaded(string displayName, string modelPath)
        {
            _modelLoaded    = true;
            _currentModelPath = modelPath;

            // Reveal the Share button now that a model is loaded
            if (_shareButton != null)
                _shareButton.gameObject.SetActive(true);

            RefreshAll();
            Debug.Log("[SpaceSessionHUD] Model loaded — Share button activated.");
        }

        // ── Build HUD ──────────────────────────────────────────────────────────

        private void BuildHUD()
        {
            // Root canvas
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 95;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            BuildTopBar();
            BuildShareButton();
            BuildBadgePanel();
            BuildCatchUpButton();
            BuildLeaveButton();
        }

        // ── Top Bar ────────────────────────────────────────────────────────────

        private void BuildTopBar()
        {
            _topBar = new GameObject("TopBar");
            _topBar.transform.SetParent(transform, false);

            RectTransform rt = _topBar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.91f);
            rt.anchorMax = new Vector2(0.82f, 0.985f); // Leave room for Share button on right
            rt.offsetMin = new Vector2(10f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);

            Image bg = _topBar.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.08f, 0.12f, 0.90f);

            HorizontalLayoutGroup hg = _topBar.AddComponent<HorizontalLayoutGroup>();
            hg.padding            = new RectOffset(14, 14, 8, 8);
            hg.spacing            = 10;
            hg.childAlignment     = TextAnchor.MiddleLeft;
            hg.childControlWidth  = true;
            hg.childControlHeight = true;
            hg.childForceExpandWidth = false;

            // Room Code
            _roomCodeText = MakeText(_topBar.transform, "RoomCode", "Offline", 24, Color.white);
            AddLayoutEl(_roomCodeText.gameObject, flexW: 1f);

            // Separator
            MakeText(_topBar.transform, "Sep1", "|", 22, new Color(1,1,1,0.25f));

            // Copy button
            _copyButton = MakePillBtn(_topBar.transform, "CopyBtn", "Copy", new Color(0.2f, 0.2f, 0.28f), OnCopyCode, minW: 80f);

            // Separator
            MakeText(_topBar.transform, "Sep2", "|", 22, new Color(1,1,1,0.25f));

            // Player count
            _playerCountText = MakeText(_topBar.transform, "Players", "0/5", 24, new Color(0.4f, 1f, 0.85f));
            AddLayoutEl(_playerCountText.gameObject, minW: 60f);

            // Separator
            MakeText(_topBar.transform, "Sep3", "|", 22, new Color(1,1,1,0.25f));

            // Mic button
            _micButton  = MakePillBtn(_topBar.transform, "MicBtn", "🎤 ON", new Color(0.15f, 0.55f, 0.30f), OnMicToggle, minW: 90f);
            _micBtnText = _micButton.GetComponentInChildren<TextMeshProUGUI>();

            // Separator
            MakeText(_topBar.transform, "Sep4", "|", 22, new Color(1,1,1,0.25f));

            // Admin/Viewer badge
            _adminBadgeText = MakeText(_topBar.transform, "Badge", "[—]", 22, Color.gray);
            AddLayoutEl(_adminBadgeText.gameObject, minW: 90f);

            _topBar.SetActive(false); // Hidden until in a room
        }

        // ── Share Button ───────────────────────────────────────────────────────

        private void BuildShareButton()
        {
            GameObject go = new GameObject("ShareButton");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin   = new Vector2(0.84f, 0.93f);
            rt.anchorMax   = new Vector2(0.98f, 0.985f);
            rt.offsetMin   = Vector2.zero;
            rt.offsetMax   = Vector2.zero;

            // Vibrant blue pill background
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.48f, 1.0f);

            _shareButton = go.AddComponent<Button>();
            _shareButton.onClick.AddListener(OnSharePressed);

            // Add subtle scale animation on press
            ColorBlock cb = _shareButton.colors;
            cb.highlightedColor = new Color(0.35f, 0.60f, 1.0f);
            cb.pressedColor     = new Color(0.15f, 0.35f, 0.80f);
            _shareButton.colors = cb;

            // Icon + label
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = "👥 Share";
            tmp.fontSize  = 26;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            // Hidden until model loads
            go.SetActive(false);
        }

        // ── Right-Edge Viewer Badges ───────────────────────────────────────────

        private void BuildBadgePanel()
        {
            _badgePanel = new GameObject("BadgePanel");
            _badgePanel.transform.SetParent(transform, false);

            RectTransform rt = _badgePanel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.80f, 0.25f);
            rt.anchorMax = new Vector2(1.00f, 0.88f);
            rt.offsetMin = new Vector2(4f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);

            VerticalLayoutGroup vg = _badgePanel.AddComponent<VerticalLayoutGroup>();
            vg.padding           = new RectOffset(4, 4, 8, 8);
            vg.spacing           = 8;
            vg.childAlignment    = TextAnchor.UpperRight;
            vg.childControlWidth = true;
            vg.childForceExpandHeight = false;

            ContentSizeFitter csf = _badgePanel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RefreshPlayerBadges()
        {
            if (_badgePanel == null) return;

            // Remove stale badges
            foreach (var kv in new Dictionary<int, GameObject>(_playerBadges))
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            _playerBadges.Clear();

            if (!PhotonNetwork.InRoom) return;

            int localActor = PhotonNetwork.LocalPlayer.ActorNumber;

            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (player.ActorNumber == localActor) continue; // Don't show self

                int    idx   = (player.ActorNumber - 1) % PlayerColors.Length;
                Color  col   = PlayerColors[idx];
                string name  = string.IsNullOrEmpty(player.NickName) ? $"Player {player.ActorNumber}" : player.NickName;
                bool   isAdm = NetworkManager.Instance != null && player.ActorNumber == NetworkManager.Instance.AdminActorNumber;
                string label = isAdm ? $"👑 {name}" : name;

                GameObject badge = BuildBadge(label, col);
                badge.transform.SetParent(_badgePanel.transform, false);
                _playerBadges[player.ActorNumber] = badge;
            }
        }

        private GameObject BuildBadge(string label, Color color)
        {
            GameObject go = new GameObject("Badge");

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.4f, 0.88f);

            HorizontalLayoutGroup hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(10, 10, 5, 5);
            hg.childAlignment = TextAnchor.MiddleCenter;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 52f;

            // Color dot
            GameObject dot = new GameObject("Dot");
            dot.transform.SetParent(go.transform, false);
            Image dotImg = dot.AddComponent<Image>();
            dotImg.color = color;
            LayoutElement dotLe = dot.AddComponent<LayoutElement>();
            dotLe.preferredWidth  = 14f;
            dotLe.preferredHeight = 14f;

            // Name text
            GameObject nameGo = new GameObject("Name");
            nameGo.transform.SetParent(go.transform, false);
            TextMeshProUGUI tmp = nameGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = "  " + label;
            tmp.fontSize  = 22;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;

            LayoutElement nameLe = nameGo.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;

            return go;
        }

        // ── Catch Up Button ────────────────────────────────────────────────────

        private void BuildCatchUpButton()
        {
            _catchUpButton = new GameObject("CatchUpButton");
            _catchUpButton.transform.SetParent(transform, false);

            RectTransform rt = _catchUpButton.AddComponent<RectTransform>();
            rt.anchorMin  = new Vector2(0.52f, 0.04f);
            rt.anchorMax  = new Vector2(0.98f, 0.11f);
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;

            Image bg = _catchUpButton.AddComponent<Image>();
            bg.color = new Color(0.90f, 0.55f, 0.10f, 0.95f); // Warm orange — stands out

            Button btn = _catchUpButton.AddComponent<Button>();
            btn.onClick.AddListener(OnCatchUpPressed);

            ColorBlock cb = btn.colors;
            cb.highlightedColor = new Color(1f, 0.65f, 0.20f);
            cb.pressedColor     = new Color(0.70f, 0.40f, 0.05f);
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_catchUpButton.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;

            _catchUpText = labelGo.AddComponent<TextMeshProUGUI>();
            _catchUpText.text      = "⚡ Catch Up with Admin";
            _catchUpText.fontSize  = 28;
            _catchUpText.fontStyle = FontStyles.Bold;
            _catchUpText.color     = Color.white;
            _catchUpText.alignment = TextAlignmentOptions.Center;
            _catchUpText.raycastTarget = false;

            _catchUpButton.SetActive(false); // Shown whenever viewer is in a room
        }

        // ── Leave Button ───────────────────────────────────────────────────────

        private void BuildLeaveButton()
        {
            _leaveButton = new GameObject("LeaveButton");
            _leaveButton.transform.SetParent(transform, false);

            RectTransform rt = _leaveButton.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.02f, 0.04f);
            rt.anchorMax = new Vector2(0.46f, 0.11f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = _leaveButton.AddComponent<Image>();
            bg.color = new Color(0.75f, 0.10f, 0.10f, 0.95f); // Deep red

            Button btn = _leaveButton.AddComponent<Button>();
            btn.onClick.AddListener(OnLeavePressed);

            ColorBlock cb = btn.colors;
            cb.highlightedColor = new Color(0.90f, 0.20f, 0.20f);
            cb.pressedColor     = new Color(0.55f, 0.05f, 0.05f);
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_leaveButton.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = "🚪 Leave Meeting";
            tmp.fontSize  = 26;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            _leaveButton.SetActive(false); // Only shown when in a room
        }

        // ── Refresh / Event Handlers ───────────────────────────────────────────

        private void OnRoomJoined(string roomCode)
        {
            RefreshAll();

            // If Admin, start broadcasting immediately after joining and broadcast active model
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            if (isAdmin && _modelLoaded)
            {
                AdminBroadcastManager.Instance?.StartBroadcasting();
                if (!string.IsNullOrEmpty(_currentModelPath))
                {
                    ModelSyncManager.Instance?.BroadcastModelChange(_currentModelPath, System.IO.Path.GetFileNameWithoutExtension(_currentModelPath));
                }
            }
        }

        private void RefreshAll()
        {
            RefreshTopBar();
            RefreshPlayerBadges();
            RefreshCatchUpButton();
        }

        private void RefreshTopBar()
        {
            bool inRoom = PhotonNetwork.InRoom;
            _topBar?.SetActive(inRoom);

            if (!inRoom)
            {
                if (_roomCodeText)    _roomCodeText.text    = "Offline";
                if (_playerCountText) _playerCountText.text = "0/5";
                if (_adminBadgeText)  _adminBadgeText.text  = "[—]";
                return;
            }

            string code   = PhotonNetwork.CurrentRoom.Name;
            int    count  = PhotonNetwork.CurrentRoom.PlayerCount;
            bool   isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

            if (_roomCodeText)    _roomCodeText.text    = $"<b><color=#6290FF>{code}</color></b>";
            if (_playerCountText) _playerCountText.text = $"{count}/5";
            if (_adminBadgeText)
            {
                _adminBadgeText.text  = isAdmin ? "<color=#FFD700>ADMIN</color>" : "<color=#AAAAAA>VIEWER</color>";
                _adminBadgeText.color = isAdmin ? new Color(1f, 0.85f, 0.0f) : Color.gray;
            }
        }

        private void RefreshCatchUpButton()
        {
            if (_catchUpButton == null) return;

            bool inRoom  = PhotonNetwork.InRoom;
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

            // Catch Up button STAYS VISIBLE permanently for all viewers.
            // It allows viewers to snap back to Admin at any time.
            bool showCatchUp = !isAdmin;
            _catchUpButton.SetActive(showCatchUp);

            // Leave button shown when in a room
            if (_leaveButton != null)
                _leaveButton.SetActive(inRoom);
        }

        // Called by SpaceInteractionController when viewer breaks or restores sync
        private void OnSyncLockChanged(bool isLocked)
        {
            RefreshCatchUpButton();
        }

        private void OnMicStateChanged(bool isMuted)
        {
            if (_micBtnText == null || _micButton == null) return;
            _micBtnText.text = isMuted ? "🔇 OFF" : "🎤 ON";
            _micButton.GetComponent<Image>().color = isMuted
                ? new Color(0.55f, 0.15f, 0.15f)
                : new Color(0.15f, 0.55f, 0.30f);
        }

        // ── Button Callbacks ───────────────────────────────────────────────────

        private void OnSharePressed()
        {
            RoomLobbyUI.Instance?.ShowModal();
        }

        private void OnCopyCode()
        {
            if (!PhotonNetwork.InRoom) return;
            string code = PhotonNetwork.CurrentRoom.Name;
            GUIUtility.systemCopyBuffer = code;
            StartCoroutine(FlashCopyFeedback());
        }

        private IEnumerator FlashCopyFeedback()
        {
            if (_copyButton == null) yield break;
            TextMeshProUGUI label = _copyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null) yield break;
            string orig = label.text;
            label.text = "✓ Copied!";
            yield return new WaitForSeconds(1.5f);
            label.text = orig;
        }

        private void OnMicToggle()
        {
            VoiceChatManager.Instance?.ToggleMute();
        }

        private void OnCatchUpPressed()
        {
            SpaceInteractionController.Instance?.SnapToAdmin();
            // Button stays visible — viewer can press again whenever they drift.
        }

        private void OnLeavePressed()
        {
            // Stop broadcasting (Admin) or just cleanly leave
            AdminBroadcastManager.Instance?.StopBroadcasting();

            if (PhotonNetwork.InRoom)
                PhotonNetwork.LeaveRoom();

            RefreshAll();
        }

        // ── UI Helpers ─────────────────────────────────────────────────────────

        private TextMeshProUGUI MakeText(Transform parent, string name, string text, float fontSize, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;

            return tmp;
        }

        private Button MakePillBtn(Transform parent, string name, string label, Color color,
                                    UnityEngine.Events.UnityAction onClick, float minW = 100f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            img.color = color;

            Button btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth  = minW;
            le.minHeight = 50f;

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            RectTransform rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 22;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            return btn;
        }

        private void AddLayoutEl(GameObject go, float flexW = 0f, float minW = 0f)
        {
            LayoutElement le = go.AddComponent<LayoutElement>();
            if (flexW > 0f) le.flexibleWidth = flexW;
            if (minW  > 0f) le.minWidth      = minW;
        }
    }
}
