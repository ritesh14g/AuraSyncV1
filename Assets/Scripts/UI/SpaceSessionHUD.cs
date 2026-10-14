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
    ///   [TOP BAR]         Room code | Copy link | Players | Mic toggle | Presenter/Viewer badge
    ///                     Visible only when in a Photon room.
    ///
    ///   [PART CHIP]       Under the top bar: the part the presenter (and you) have selected.
    ///
    ///   [SHARE BUTTON]    Blue pill, top-right. Opens the room panel (create / join / invite / leave).
    ///
    ///   [PLAYER BADGES]   Right edge, one per other player: colour dot, name, mic state, the
    ///                     controls they drive (Phase 1), and a speaking highlight. The presenter
    ///                     taps a badge for PlayerControlMenu: hand over controls, or the
    ///                     presenter role.
    ///
    ///   [CATCH UP BTN]    Bottom-right, viewers only. "Catch up" snaps to the presenter's view and
    ///                     follows it; while following it reads "Following" and a tap breaks away.
    ///
    ///   [LEAVE BTN]       Bottom-left, leave the room.
    ///
    ///   [V3 SLOTS]        Left feature rail and top-right corner row: see SpaceSessionHUD.Slots.cs.
    /// </summary>
    public partial class SpaceSessionHUD : MonoBehaviour
    {
        public static SpaceSessionHUD Instance { get; private set; }

        private static readonly Color CatchUpColor   = new Color(0.90f, 0.55f, 0.10f, 0.95f); // orange
        private static readonly Color FollowingColor = new Color(0.15f, 0.60f, 0.35f, 0.95f); // green
        private static readonly Color PulseColor     = new Color(1.00f, 0.85f, 0.35f, 1.00f); // bright amber

        // ── Runtime UI references ──────────────────────────────────────────────
        private Canvas          _canvas;
        private GameObject      _topBar;
        private TextMeshProUGUI _roomCodeText;
        private TextMeshProUGUI _playerCountText;
        private TextMeshProUGUI _adminBadgeText;
        private Button          _micButton;
        private TextMeshProUGUI _micBtnText;
        private Button          _listenButton;
        private TextMeshProUGUI _listenBtnText;
        private Button          _copyButton;

        private Button          _shareButton;

        private GameObject      _partChip;
        private TextMeshProUGUI _partChipText;

        private GameObject      _catchUpButton;
        private Image           _catchUpImage;
        private TextMeshProUGUI _catchUpText;

        private GameObject      _leaveButton;

        private GameObject      _badgePanel;

        private class BadgeView
        {
            public int            actor;
            public Color          color;
            public string         name;
            public bool           isPresenter;
            public Button         button;
            public Image          background;
            public Image          dot;
            public TextMeshProUGUI label;
        }
        private readonly Dictionary<int, BadgeView> _badges = new Dictionary<int, BadgeView>();

        private bool   _modelLoaded = false;
        private string _currentModelPath = "";

        private NetworkManager         _boundNetwork;
        private SpaceInteractionController _boundSic;
        private ModelPartSelector      _boundSelector;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildHUD();
        }

        void Start()
        {
            if (NetworkManager.Instance != null)
            {
                _boundNetwork = NetworkManager.Instance;
                _boundNetwork.OnRoomJoinedSuccess += OnRoomJoined;
                _boundNetwork.OnPlayerListUpdated += RefreshAll;
            }

            if (VoiceChatManager.Instance != null)
            {
                VoiceChatManager.Instance.OnMicMuteStateChanged += OnMicStateChanged;
                VoiceChatManager.Instance.OnIncomingMuteChanged += OnIncomingMuteChanged;
            }

            // Sync-lock changes drive the Catch Up / Following button
            if (SpaceInteractionController.Instance != null)
            {
                _boundSic = SpaceInteractionController.Instance;
                _boundSic.OnSyncLockChanged += OnSyncLockChanged;
            }

            StartCoroutine(BindSelectorWhenReady());
            StartCoroutine(BadgeStateLoop());
            ControlOwnership.OnChanged += RefreshPartChip;

            OnMicStateChanged(VoiceChatManager.Instance != null && VoiceChatManager.Instance.IsMuted);
            OnIncomingMuteChanged(VoiceChatManager.IncomingMuted);
            RefreshAll();
        }

        void OnDestroy()
        {
            if (_boundNetwork != null)
            {
                _boundNetwork.OnRoomJoinedSuccess -= OnRoomJoined;
                _boundNetwork.OnPlayerListUpdated -= RefreshAll;
            }
            if (VoiceChatManager.Instance != null)
            {
                VoiceChatManager.Instance.OnMicMuteStateChanged -= OnMicStateChanged;
                VoiceChatManager.Instance.OnIncomingMuteChanged -= OnIncomingMuteChanged;
            }
            if (_boundSic != null)      _boundSic.OnSyncLockChanged -= OnSyncLockChanged;
            if (_boundSelector != null) _boundSelector.OnSelectionChanged -= RefreshPartChip;
            ControlOwnership.OnChanged -= RefreshPartChip;
            if (Instance == this) Instance = null;
        }

        private IEnumerator BindSelectorWhenReady()
        {
            // SpaceSceneManager creates the selector just after this HUD.
            while (ModelPartSelector.Instance == null) yield return null;

            _boundSelector = ModelPartSelector.Instance;
            _boundSelector.OnSelectionChanged += RefreshPartChip;
            RefreshPartChip();
        }

        // Called by SpaceSceneManager once the GLB finishes loading
        public void OnModelLoaded(string displayName, string modelPath)
        {
            _modelLoaded      = true;
            _currentModelPath = modelPath;

            // Reveal the Share button now that a model is loaded
            if (_shareButton != null)
                _shareButton.gameObject.SetActive(true);

            RefreshAll();
            Debug.Log("[SpaceSessionHUD] Model loaded - Share button activated.");
        }

        // ── Build HUD ──────────────────────────────────────────────────────────

        private void BuildHUD()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 95;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            BuildTopBar();
            BuildPartChip();
            BuildShareButton();
            BuildBadgePanel();
            BuildCatchUpButton();
            BuildLeaveButton();
            BuildBackButton();
            BuildSlots();
        }

        // ── Back to the model list ─────────────────────────────────────────────

        /// <summary>
        /// The viewer had no way out: SpaceScene's own Back button is unassigned, and even where it
        /// was wired it pointed at a scene that is not in Build Settings. This one sits in the
        /// bottom row beside ROOM and PARTS and always works.
        /// </summary>
        private void BuildBackButton()
        {
            GameObject go = new GameObject("BackToModelsButton");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.48f, 0.135f);
            rt.anchorMax = new Vector2(0.71f, 0.205f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.22f, 0.26f, 0.36f, 0.95f);

            Button btn = go.AddComponent<Button>();
            btn.onClick.AddListener(OnBackPressed);

            ColorBlock cb = btn.colors;
            cb.highlightedColor = new Color(0.32f, 0.38f, 0.50f);
            cb.pressedColor     = new Color(0.16f, 0.19f, 0.26f);
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = "BACK";
            tmp.fontSize  = 23;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        /// <summary>
        /// Back closes the panel on top first (the parts drawer, the room panel, any V3 panel), the
        /// same as the device's back button. Only with nothing open does it leave the viewer.
        /// </summary>
        private void OnBackPressed()
        {
            if (PanelNavigator.Back()) return;
            SpaceSceneManager.ReturnToModelList();
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

            _roomCodeText = MakeText(_topBar.transform, "RoomCode", "Offline", 24, Color.white);
            AddLayoutEl(_roomCodeText.gameObject, flexW: 1f);

            MakeText(_topBar.transform, "Sep1", "|", 22, new Color(1, 1, 1, 0.25f));

            _copyButton = MakePillBtn(_topBar.transform, "CopyBtn", "Copy Link", new Color(0.2f, 0.2f, 0.28f), OnCopyCode, minW: 110f);

            MakeText(_topBar.transform, "Sep2", "|", 22, new Color(1, 1, 1, 0.25f));

            _playerCountText = MakeText(_topBar.transform, "Players", "0/5", 24, new Color(0.4f, 1f, 0.85f));
            AddLayoutEl(_playerCountText.gameObject, minW: 60f);

            MakeText(_topBar.transform, "Sep3", "|", 22, new Color(1, 1, 1, 0.25f));

            _micButton  = MakePillBtn(_topBar.transform, "MicBtn", "MIC ON", new Color(0.15f, 0.55f, 0.30f), OnMicToggle, minW: 110f);
            _micBtnText = _micButton.GetComponentInChildren<TextMeshProUGUI>();

            _listenButton  = MakePillBtn(_topBar.transform, "ListenBtn", "HEAR ON", new Color(0.15f, 0.45f, 0.55f), OnListenToggle, minW: 120f);
            _listenBtnText = _listenButton.GetComponentInChildren<TextMeshProUGUI>();

            MakeText(_topBar.transform, "Sep4", "|", 22, new Color(1, 1, 1, 0.25f));

            _adminBadgeText = MakeText(_topBar.transform, "Badge", "[-]", 22, Color.gray);
            AddLayoutEl(_adminBadgeText.gameObject, minW: 110f);

            _topBar.SetActive(false); // Hidden until in a room
        }

        // ── Selected-part chip ─────────────────────────────────────────────────

        private void BuildPartChip()
        {
            _partChip = new GameObject("PartChip");
            _partChip.transform.SetParent(transform, false);

            RectTransform rt = _partChip.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.845f);
            rt.anchorMax = new Vector2(0.66f, 0.905f); // clear of the V3 corner row (Settings / Reports)
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = _partChip.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.08f, 0.12f, 0.80f);
            bg.raycastTarget = false;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(_partChip.transform, false);
            RectTransform trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 4f); trt.offsetMax = new Vector2(-14f, -4f);

            _partChipText = textGo.AddComponent<TextMeshProUGUI>();
            _partChipText.fontSize = 22;
            _partChipText.alignment = TextAlignmentOptions.MidlineLeft;
            _partChipText.color = Color.white;
            _partChipText.raycastTarget = false;
            _partChipText.enableWordWrapping = false;
            _partChipText.overflowMode = TextOverflowModes.Ellipsis;

            _partChip.SetActive(false);
        }

        private void RefreshPartChip()
        {
            if (_partChip == null) return;

            string presenter = _boundSelector != null ? _boundSelector.PresenterPartName : "";
            string local     = _boundSelector != null ? _boundSelector.LocalPartName : "";

            if (string.IsNullOrEmpty(presenter) && string.IsNullOrEmpty(local))
            {
                _partChip.SetActive(false);
                return;
            }

            // The room's pick is whoever drives Highlight: the presenter, or the student given it.
            string who = ControlOwnership.IsHandedOut(Control.Highlight)
                ? ControlOwnership.HolderName(Control.Highlight).Replace("<", "")
                : "Presenter";

            string text = "";
            if (!string.IsNullOrEmpty(presenter)) text += $"<color=#FFD133>{who}:</color> {presenter}";
            if (!string.IsNullOrEmpty(local))
            {
                if (text.Length > 0) text += "   ";
                text += $"<color=#33D9FF>You:</color> {local}";
            }

            _partChipText.text = text;
            _partChip.SetActive(true);
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

            Image img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.48f, 1.0f);

            _shareButton = go.AddComponent<Button>();
            _shareButton.onClick.AddListener(OnSharePressed);

            ColorBlock cb = _shareButton.colors;
            cb.highlightedColor = new Color(0.35f, 0.60f, 1.0f);
            cb.pressedColor     = new Color(0.15f, 0.35f, 0.80f);
            _shareButton.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = "Share";
            tmp.fontSize  = 26;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            go.SetActive(false); // hidden until a model loads
        }

        // ── Right-Edge Player Badges ───────────────────────────────────────────

        private void BuildBadgePanel()
        {
            _badgePanel = new GameObject("BadgePanel");
            _badgePanel.transform.SetParent(transform, false);

            RectTransform rt = _badgePanel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.72f, 0.25f);
            rt.anchorMax = new Vector2(1.00f, 0.84f);
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

            foreach (var kv in _badges)
                if (kv.Value.background != null) Destroy(kv.Value.background.gameObject);
            _badges.Clear();

            if (!PhotonNetwork.InRoom) return;

            int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
            int adminActor = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;

            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (player.ActorNumber == localActor) continue; // do not show yourself

                BadgeView view = BuildBadge(player.ActorNumber, NetworkManager.DisplayName(player),
                                            UserPresence.ColorForActor(player.ActorNumber),
                                            player.ActorNumber == adminActor);
                view.background.transform.SetParent(_badgePanel.transform, false);
                _badges[player.ActorNumber] = view;
            }

            UpdateBadgeStates();
        }

        private BadgeView BuildBadge(int actor, string name, Color color, bool isPresenter)
        {
            GameObject go = new GameObject("Badge");

            Image bg = go.AddComponent<Image>();

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None; // we colour the badge ourselves
            btn.onClick.AddListener(() => OnBadgePressed(actor));

            HorizontalLayoutGroup hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(10, 10, 5, 5);
            hg.childAlignment = TextAnchor.MiddleCenter;
            hg.childControlWidth = true;
            hg.childControlHeight = true;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 52f;

            GameObject dot = new GameObject("Dot");
            dot.transform.SetParent(go.transform, false);
            Image dotImg = dot.AddComponent<Image>();
            dotImg.raycastTarget = false;
            LayoutElement dotLe = dot.AddComponent<LayoutElement>();
            dotLe.preferredWidth  = 14f;
            dotLe.preferredHeight = 14f;

            GameObject nameGo = new GameObject("Name");
            nameGo.transform.SetParent(go.transform, false);
            TextMeshProUGUI tmp = nameGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize  = 21;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            LayoutElement nameLe = nameGo.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;

            return new BadgeView
            {
                actor = actor, color = color, name = name, isPresenter = isPresenter,
                button = btn, background = bg, dot = dotImg, label = tmp
            };
        }

        /// <summary>Redraws each badge's text and colours (mic state, speaking, controls held).</summary>
        private void UpdateBadgeStates()
        {
            if (!PhotonNetwork.InRoom) return;

            var voice = VoiceChatManager.Instance;

            foreach (var kv in _badges)
            {
                BadgeView b = kv.Value;
                if (b.background == null) continue;

                Player player = PhotonNetwork.CurrentRoom.GetPlayer(b.actor);
                bool muted    = VoiceChatManager.IsPlayerMuted(player);
                bool speaking = voice != null && !muted && voice.IsSpeaking(b.actor);

                string text = (b.isPresenter ? "* " : "") + b.name;
                if (muted) text += "  (muted)";

                // What a student drives for the room: "Asha · Explode", or "· 2 controls".
                var held = ControlOwnership.HeldBy(b.actor);
                if (held.Count == 1)     text += $"  <color=#FFD133>· {ControlOwnership.Label(held[0])}</color>";
                else if (held.Count > 1) text += $"  <color=#FFD133>· {held.Count} controls</color>";
                b.label.text = text;

                float k = speaking ? 0.75f : 0.40f;
                b.background.color = new Color(b.color.r * k, b.color.g * k, b.color.b * k, 0.90f);
                b.dot.color = speaking ? Color.white : b.color;
            }
        }

        private IEnumerator BadgeStateLoop()
        {
            var wait = new WaitForSecondsRealtime(0.15f);
            while (true)
            {
                if (_badges.Count > 0) UpdateBadgeStates();
                yield return wait;
            }
        }

        private void OnBadgePressed(int actor)
        {
            // Only the presenter hands out controls or the role, and not to themselves.
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsLocalAdmin) return;
            if (actor == NetworkManager.Instance.AdminActorNumber) return;

            PlayerControlMenu.Open(actor);
        }

        // ── Catch Up Button ────────────────────────────────────────────────────

        private void BuildCatchUpButton()
        {
            _catchUpButton = new GameObject("CatchUpButton");
            _catchUpButton.transform.SetParent(transform, false);

            RectTransform rt = _catchUpButton.AddComponent<RectTransform>();
            rt.anchorMin  = new Vector2(0.50f, 0.04f);
            rt.anchorMax  = new Vector2(0.98f, 0.11f);
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;

            _catchUpImage = _catchUpButton.AddComponent<Image>();
            _catchUpImage.color = CatchUpColor;

            Button btn = _catchUpButton.AddComponent<Button>();
            btn.onClick.AddListener(OnCatchUpPressed);

            ColorBlock cb = btn.colors;
            cb.highlightedColor = new Color(1f, 0.65f, 0.20f);
            cb.pressedColor     = new Color(0.70f, 0.40f, 0.05f);
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_catchUpButton.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-8f, 0f);

            _catchUpText = labelGo.AddComponent<TextMeshProUGUI>();
            _catchUpText.text      = "CATCH UP WITH PRESENTER";
            _catchUpText.fontSize  = 24;
            _catchUpText.fontStyle = FontStyles.Bold;
            _catchUpText.color     = Color.white;
            _catchUpText.alignment = TextAlignmentOptions.Center;
            _catchUpText.raycastTarget = false;

            _catchUpButton.SetActive(false); // shown to viewers in a room
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
            bg.color = new Color(0.75f, 0.10f, 0.10f, 0.95f);

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
            tmp.text      = "LEAVE MEETING";
            tmp.fontSize  = 24;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            _leaveButton.SetActive(false); // only shown when in a room
        }

        // ── Refresh / Event Handlers ───────────────────────────────────────────

        private void OnRoomJoined(string roomCode)
        {
            RefreshAll();

            // The presenter starts broadcasting and shares the active model as soon as they are in the room.
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
            if (_topBar != null) _topBar.SetActive(inRoom);

            if (!inRoom)
            {
                if (_roomCodeText)    _roomCodeText.text    = "Offline";
                if (_playerCountText) _playerCountText.text = $"0/{NetworkManager.MaxPlayers}";
                if (_adminBadgeText)  _adminBadgeText.text  = "[-]";
                return;
            }

            string code   = PhotonNetwork.CurrentRoom.Name;
            int    count  = PhotonNetwork.CurrentRoom.PlayerCount;
            bool   isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

            if (_roomCodeText)    _roomCodeText.text    = $"<b><color=#6290FF>{code}</color></b>";
            if (_playerCountText) _playerCountText.text = $"{count}/{NetworkManager.MaxPlayers}";
            if (_adminBadgeText)
            {
                _adminBadgeText.text  = isAdmin ? "<color=#FFD700>PRESENTER</color>" : "<color=#AAAAAA>VIEWER</color>";
                _adminBadgeText.color = isAdmin ? new Color(1f, 0.85f, 0.0f) : Color.gray;
            }
        }

        private void RefreshCatchUpButton()
        {
            bool inRoom  = PhotonNetwork.InRoom;
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

            if (_catchUpButton != null)
            {
                // Only viewers in a room have a presenter to follow.
                _catchUpButton.SetActive(inRoom && !isAdmin);

                bool following = _boundSic != null && _boundSic.IsSyncLocked;
                if (_catchUpText  != null) _catchUpText.text   = following ? "FOLLOWING PRESENTER - TAP TO EXPLORE" : "CATCH UP WITH PRESENTER";
                if (_catchUpImage != null) _catchUpImage.color = following ? FollowingColor : CatchUpColor;
            }

            if (_leaveButton != null) _leaveButton.SetActive(inRoom);
        }

        // Called by SpaceInteractionController when this device breaks or restores sync
        private void OnSyncLockChanged(bool isLocked)
        {
            RefreshCatchUpButton();
        }

        // ── Catch Up pulse ─────────────────────────────────────────────────────

        /// <summary>How long after the presenter last moved the button keeps pulsing.</summary>
        private const float PulseWindowSeconds = 2.5f;

        /// <summary>
        /// Pulses the Catch Up button while the presenter is moving the model and this viewer is off
        /// exploring on their own. Without it there is nothing on screen to say the presentation has
        /// moved on - the viewer's own model simply sits still and everything looks fine.
        ///
        /// It deliberately does not pulse while following, or while the presenter is idle.
        /// </summary>
        void Update()
        {
            if (_catchUpButton == null || !_catchUpButton.activeSelf || _catchUpImage == null) return;

            bool following = _boundSic != null && _boundSic.IsSyncLocked;
            if (following) return; // already in step; FollowingColor is set by RefreshCatchUpButton

            bool presenterMoving = _boundSic != null &&
                                   Time.unscaledTime - _boundSic.LastAdminMoveTime < PulseWindowSeconds;

            if (!presenterMoving)
            {
                _catchUpImage.color = CatchUpColor;
                return;
            }

            // Mathf.PingPong gives a steady triangle wave; 2.2 Hz reads as "attention" without
            // becoming a strobe on a phone held close to the face.
            float k = Mathf.PingPong(Time.unscaledTime * 2.2f, 1f);
            _catchUpImage.color = Color.Lerp(CatchUpColor, PulseColor, k);
        }

        private void OnMicStateChanged(bool isMuted)
        {
            if (_micBtnText == null || _micButton == null) return;

            bool noMic = VoiceChatManager.Instance != null && !VoiceChatManager.Instance.IsMicAvailable;
            _micBtnText.text = noMic ? "NO MIC" : (isMuted ? "MIC OFF" : "MIC ON");
            _micButton.GetComponent<Image>().color = noMic
                ? new Color(0.35f, 0.35f, 0.38f)
                : (isMuted ? new Color(0.55f, 0.15f, 0.15f) : new Color(0.15f, 0.55f, 0.30f));
        }

        // ── Button Callbacks ───────────────────────────────────────────────────

        private void OnSharePressed()
        {
            RoomLobbyUI.Instance?.ShowModal();
        }

        private void OnCopyCode()
        {
            if (!PhotonNetwork.InRoom) return;
            RoomLinkManager.CopyInviteLink(PhotonNetwork.CurrentRoom.Name);
            StartCoroutine(FlashCopyFeedback());
        }

        private IEnumerator FlashCopyFeedback()
        {
            if (_copyButton == null) yield break;
            TextMeshProUGUI label = _copyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label == null) yield break;
            string orig = "Copy Link";
            label.text = "Copied!";
            yield return new WaitForSecondsRealtime(1.5f);
            if (label != null) label.text = orig;
        }

        private void OnMicToggle()
        {
            VoiceChatManager.Instance?.ToggleMute();
        }

        private void OnListenToggle()
        {
            VoiceChatManager.Instance?.ToggleIncomingMute();
        }

        /// <summary>Silencing the room is a separate thing from muting your own microphone.</summary>
        private void OnIncomingMuteChanged(bool muted)
        {
            if (_listenBtnText == null || _listenButton == null) return;

            _listenBtnText.text = muted ? "HEAR OFF" : "HEAR ON";
            _listenButton.GetComponent<Image>().color = muted
                ? new Color(0.55f, 0.15f, 0.15f)
                : new Color(0.15f, 0.45f, 0.55f);
        }

        private void OnCatchUpPressed()
        {
            if (_boundSic == null) return;

            if (_boundSic.IsSyncLocked) _boundSic.BreakSync(); // tap while following = explore freely
            else                        _boundSic.SnapToAdmin();
        }

        private void OnLeavePressed()
        {
            AdminBroadcastManager.Instance?.StopBroadcasting();

            if (NetworkManager.Instance != null) NetworkManager.Instance.LeaveRoom();
            else if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();

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
