using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Photon.Pun;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// The multiplayer panel. One modal, two states:
    ///   - Not in a room: set your name, create a room, or join with a code / invite link.
    ///   - In a room:     see the room code, copy the invite link, or leave.
    /// Built procedurally unless the references are assigned in the Inspector.
    /// </summary>
    public class RoomLobbyUI : MonoBehaviour
    {
        public static RoomLobbyUI Instance { get; private set; }

        [Header("References (optional - built at runtime if empty)")]
        [SerializeField] private GameObject modalContainer;
        [SerializeField] private TMP_InputField roomCodeInput;
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Behaviour")]
        [Tooltip("Open this panel on the first screen at app launch, so anyone can join a session straight away.")]
        [SerializeField] private bool showOnStartup = true;
        [SerializeField] private string spaceSceneName = "SpaceScene";

        [Header("Corner Launcher Button")]
        [Tooltip("A small always-present button that opens and closes this panel in every scene.")]
        [SerializeField] private bool showLauncherButton = true;
        [Tooltip("Launcher position, in screen fractions. Default sits at the lower-left corner, " +
                 "clear of the Space scene's top bar and bottom row.")]
        [SerializeField] private Vector2 launcherAnchorMin = new Vector2(0.02f, 0.135f);
        [SerializeField] private Vector2 launcherAnchorMax = new Vector2(0.21f, 0.205f);
        [Tooltip("Show the one-line voice readout above the launcher while in a room. It tells you " +
                 "at a glance whether this phone's microphone is working and whether voice is " +
                 "arriving from the others.")]
        [SerializeField] private bool showVoiceStatus = true;

        private GameObject      _launcherCanvas;
        private TextMeshProUGUI _voiceStatusText;

        private static bool _shownOnStartup = false;
        private TextMeshProUGUI _closeLabel;

        // In-room controls (procedural only)
        private TextMeshProUGUI _roomInfoText;
        private Button _copyLinkButton;
        private Button _copyCodeButton;
        private Button _leaveButton;

        private readonly List<GameObject> _lobbyOnly  = new List<GameObject>();
        private readonly List<GameObject> _inRoomOnly = new List<GameObject>();

        // Procedural buttons wire their own click handlers when they are created.
        // Inspector-assigned buttons are wired in Start(). Never both, or every click fires twice.
        private bool _proceduralBuilt = false;
        private NetworkManager _boundManager;

        private static readonly Color Accent = new Color(0.38f, 0.56f, 1.0f);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureProceduralLobby();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void Start()
        {
            if (!_proceduralBuilt)
            {
                createRoomButton?.onClick.AddListener(OnCreatePressed);
                joinRoomButton?.onClick.AddListener(OnJoinPressed);
                closeButton?.onClick.AddListener(HideModal);
            }

            if (nameInput != null && string.IsNullOrEmpty(nameInput.text))
                nameInput.text = PlayerPrefs.GetString(NetworkManager.PrefPlayerName, "");

            // NetworkManager may be created after this object (SceneBootstrapper), so wait for it.
            StartCoroutine(BindNetworkWhenReady());

            // First screen of the app: offer create / join right away.
            // With the new onboarding, joining lives in Interactive mode instead.
            if (showOnStartup && !_shownOnStartup && !PhotonNetwork.InRoom && !OnboardingFlow.UseNewOnboarding)
            {
                _shownOnStartup = true;
                ShowModal();
            }
        }

        private IEnumerator BindNetworkWhenReady()
        {
            while (NetworkManager.Instance == null) yield return null;

            _boundManager = NetworkManager.Instance;
            _boundManager.OnRoomJoinedSuccess   += HandleJoinSuccess;
            _boundManager.OnRoomJoinFailedError += HandleJoinError;
            _boundManager.OnPlayerListUpdated   += HandlePlayersChanged;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_boundManager != null)
            {
                _boundManager.OnRoomJoinedSuccess   -= HandleJoinSuccess;
                _boundManager.OnRoomJoinFailedError -= HandleJoinError;
                _boundManager.OnPlayerListUpdated   -= HandlePlayersChanged;
            }
            if (Instance == this) Instance = null;
        }

        // ──────────────────────────────────────────
        //  Build
        // ──────────────────────────────────────────

        private void EnsureProceduralLobby()
        {
            if (modalContainer != null) return;
            _proceduralBuilt = true;

            GameObject canvasGo = new GameObject("RoomLobbyCanvas");
            canvasGo.transform.SetParent(transform);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvasGo.AddComponent<GraphicRaycaster>();

            // Dark backdrop. It blocks touches so nothing behind the modal reacts.
            GameObject backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(canvasGo.transform, false);

            RectTransform bdRt = backdrop.AddComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero;
            bdRt.anchorMax = Vector2.one;
            bdRt.sizeDelta = Vector2.zero;

            Image bdImg = backdrop.AddComponent<Image>();
            bdImg.color = new Color(0, 0, 0, 0.75f);
            bdImg.raycastTarget = true;
            backdrop.AddComponent<TouchBlocker>(); // the 3D model behind must not react

            // Tapping anywhere outside the card closes the panel. The card below is drawn on top
            // and blocks raycasts, so taps on the panel itself never reach this.
            Button dismiss = backdrop.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(HideModal);

            // Modal card
            GameObject card = new GameObject("LobbyCard");
            card.transform.SetParent(canvasGo.transform, false);

            RectTransform cardRt = card.AddComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.07f, 0.15f);
            cardRt.anchorMax = new Vector2(0.93f, 0.85f);
            cardRt.offsetMin = Vector2.zero;
            cardRt.offsetMax = Vector2.zero;

            Image cardImg = card.AddComponent<Image>();
            cardImg.color = new Color(0.12f, 0.14f, 0.20f, 0.97f);
            cardImg.raycastTarget = true;
            card.AddComponent<TouchBlocker>();

            VerticalLayoutGroup vgroup = card.AddComponent<VerticalLayoutGroup>();
            vgroup.padding = new RectOffset(30, 30, 24, 24);
            vgroup.spacing = 14;
            vgroup.childAlignment = TextAnchor.MiddleCenter;
            vgroup.childControlWidth = true;
            vgroup.childControlHeight = false;
            vgroup.childForceExpandHeight = false;

            Transform c = card.transform;

            AddText(c, "Title", "<b>AURASYNC MULTI-USER</b>", 36, Accent, 56);

            nameInput = CreateInputField(c, "NameField", "Your name", 16);
            nameInput.text = PlayerPrefs.GetString(NetworkManager.PrefPlayerName, "");

            // ── Not in a room ──
            createRoomButton = CreateButton(c, "CreateBtn", "CREATE NEW ROOM", new Color(0.2f, 0.6f, 0.4f), OnCreatePressed);
            _lobbyOnly.Add(createRoomButton.gameObject);

            _lobbyOnly.Add(AddText(c, "DividerText", "- OR JOIN WITH A CODE -", 20, Color.gray, 34).gameObject);

            roomCodeInput = CreateInputField(c, "CodeInputField", "AURA-XXXX or invite link", 0);
            _lobbyOnly.Add(roomCodeInput.gameObject);

            joinRoomButton = CreateButton(c, "JoinBtn", "JOIN ROOM", new Color(0.25f, 0.45f, 0.85f), OnJoinPressed);
            _lobbyOnly.Add(joinRoomButton.gameObject);

            // ── In a room ──
            _roomInfoText = AddText(c, "RoomInfo", "", 30, Color.white, 80);
            _inRoomOnly.Add(_roomInfoText.gameObject);

            _copyLinkButton = CreateButton(c, "CopyLinkBtn", "COPY INVITE LINK", new Color(0.25f, 0.45f, 0.85f), OnCopyLinkPressed);
            _inRoomOnly.Add(_copyLinkButton.gameObject);

            _copyCodeButton = CreateButton(c, "CopyCodeBtn", "COPY ROOM CODE", new Color(0.2f, 0.2f, 0.28f), OnCopyCodePressed);
            _inRoomOnly.Add(_copyCodeButton.gameObject);

            _leaveButton = CreateButton(c, "LeaveBtn", "LEAVE ROOM", new Color(0.75f, 0.15f, 0.15f), OnLeavePressed);
            _inRoomOnly.Add(_leaveButton.gameObject);

            // ── Always ──
            statusText = AddText(c, "StatusText", "", 22, Color.yellow, 40);
            closeButton = CreateButton(c, "CloseBtn", "CLOSE", new Color(0.3f, 0.3f, 0.35f), HideModal);
            _closeLabel = closeButton.GetComponentInChildren<TextMeshProUGUI>();

            modalContainer = canvasGo;
            modalContainer.SetActive(false); // shown via ShowModal()

            BuildLauncher();
        }

        /// <summary>
        /// The always-present corner button. It lives on its own canvas above the modal, so it stays
        /// tappable while the panel is open and can close it again.
        /// </summary>
        private void BuildLauncher()
        {
            if (!showLauncherButton) return;

            _launcherCanvas = new GameObject("RoomLauncherCanvas");
            _launcherCanvas.transform.SetParent(transform);

            Canvas canvas = _launcherCanvas.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 101; // above the modal (100) and every HUD

            CanvasScaler scaler = _launcherCanvas.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            _launcherCanvas.AddComponent<GraphicRaycaster>();

            GameObject btnGo = new GameObject("RoomLauncherButton");
            btnGo.transform.SetParent(_launcherCanvas.transform, false);

            RectTransform rt = btnGo.AddComponent<RectTransform>();
            rt.anchorMin = launcherAnchorMin;
            rt.anchorMax = launcherAnchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = btnGo.AddComponent<Image>();
            img.color = new Color(0.16f, 0.26f, 0.46f, 0.95f);

            Button btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(ToggleModal);

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(btnGo.transform, false);
            Stretch(labelGo.AddComponent<RectTransform>());

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text          = "ROOM";
            label.fontSize      = 26;
            label.fontStyle     = FontStyles.Bold;
            label.color         = Color.white;
            label.alignment     = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            if (!showVoiceStatus) return;

            GameObject voiceGo = new GameObject("VoiceStatus");
            voiceGo.transform.SetParent(_launcherCanvas.transform, false);

            RectTransform vrt = voiceGo.AddComponent<RectTransform>();
            vrt.anchorMin = new Vector2(launcherAnchorMin.x, launcherAnchorMax.y + 0.005f);
            vrt.anchorMax = new Vector2(0.98f, launcherAnchorMax.y + 0.045f);
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;

            _voiceStatusText = voiceGo.AddComponent<TextMeshProUGUI>();
            _voiceStatusText.text          = "";
            _voiceStatusText.fontSize      = 19;
            _voiceStatusText.color         = new Color(0.6f, 0.85f, 1f, 0.9f);
            _voiceStatusText.alignment     = TextAlignmentOptions.Left;
            _voiceStatusText.raycastTarget = false;
        }

        /// <summary>Opens the panel, or closes it if it is already open.</summary>
        public void ToggleModal()
        {
            if (modalContainer != null && modalContainer.activeSelf) HideModal();
            else                                                    ShowModal();
        }

        void Update()
        {
            // The floating ROOM button stays off the onboarding screens; the 3D viewer keeps it.
            if (_launcherCanvas != null)
            {
                // The 3D viewer has its own Room button in the top-right corner.
                // With the new onboarding the old floating square is retired altogether: the
                // onboarding has Join room, and the viewer has its Room button.
                bool want = !OnboardingFlow.UseNewOnboarding && !OnboardingFlow.IsActive && !SpaceSessionHUD.Active;
                if (_launcherCanvas.activeSelf != want) _launcherCanvas.SetActive(want);
            }

            if (_voiceStatusText == null) return;

            var voice = AuraSync.Networking.VoiceChatManager.Instance;
            bool show = PhotonNetwork.InRoom && voice != null;
            if (_voiceStatusText.gameObject.activeSelf != show)
                _voiceStatusText.gameObject.SetActive(show);

            if (show) _voiceStatusText.text = voice.DiagnosticLine();
        }

        /// <summary>Adds a centred label with a fixed height.</summary>
        private TextMeshProUGUI AddText(Transform parent, string name, string text, float size, Color color, float height)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            return tmp;
        }

        private Button CreateButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            img.color = color;

            Button btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            RectTransform rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 26;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 70;

            return btn;
        }

        /// <summary>
        /// A complete TMP input field (viewport, text and placeholder). TMP_InputField needs all
        /// three to render text, show a caret and accept typing, so it is built inactive and
        /// activated only once everything is assigned.
        /// </summary>
        private TMP_InputField CreateInputField(Transform parent, string name, string placeholder, int characterLimit)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.SetActive(false);

            go.GetComponent<Image>().color = new Color(0.2f, 0.22f, 0.28f);

            GameObject viewport = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(16f, 6f); vpRt.offsetMax = new Vector2(-16f, -6f);

            GameObject phGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            phGo.transform.SetParent(viewport.transform, false);
            Stretch(phGo.GetComponent<RectTransform>());
            var ph = phGo.GetComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.fontSize = 26;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(1f, 1f, 1f, 0.35f);
            ph.alignment = TextAlignmentOptions.Center;
            ph.raycastTarget = false;

            GameObject txGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txGo.transform.SetParent(viewport.transform, false);
            Stretch(txGo.GetComponent<RectTransform>());
            var tx = txGo.GetComponent<TextMeshProUGUI>();
            tx.fontSize = 30;
            tx.color = Color.white;
            tx.alignment = TextAlignmentOptions.Center;
            tx.raycastTarget = false;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport  = vpRt;
            field.textComponent = tx;
            field.placeholder   = ph;
            field.fontAsset     = tx.font;
            field.lineType      = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 70;

            go.SetActive(true);
            return field;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // ──────────────────────────────────────────
        //  Show / hide / state
        // ──────────────────────────────────────────

        public void ShowModal()
        {
            if (modalContainer != null) modalContainer.SetActive(true);
            RefreshState();
            SetStatus("", Color.yellow);

            // Back (on screen or the device's) closes the room panel when it is on top.
            PanelNavigator.Push("room", this, HideModal);
        }

        public void HideModal()
        {
            if (modalContainer != null) modalContainer.SetActive(false);
            PanelNavigator.Remove("room");
        }

        /// <summary>Switches between the lobby and the in-room share panel.</summary>
        private void RefreshState()
        {
            bool inRoom = PhotonNetwork.InRoom;

            foreach (var go in _lobbyOnly)  if (go != null) go.SetActive(!inRoom);
            foreach (var go in _inRoomOnly) if (go != null) go.SetActive(inRoom);

            if (_closeLabel != null) _closeLabel.text = inRoom ? "CLOSE" : "CONTINUE WITHOUT A ROOM";

            if (inRoom && _roomInfoText != null)
            {
                _roomInfoText.text =
                    $"Room  <b><color=#6290FF>{PhotonNetwork.CurrentRoom.Name}</color></b>\n" +
                    $"<size=22><color=#00FFCC>{PhotonNetwork.CurrentRoom.PlayerCount}/{NetworkManager.MaxPlayers} players</color></size>";
            }
        }

        private void SetStatus(string message, Color color)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = color;
        }

        private void ApplyName()
        {
            if (nameInput != null && !string.IsNullOrWhiteSpace(nameInput.text))
                NetworkManager.Instance?.SetPlayerName(nameInput.text);
        }

        // ──────────────────────────────────────────
        //  Actions
        // ──────────────────────────────────────────

        /// <summary>
        /// Recreates the NetworkManager when it is missing, then runs the action once it has had a
        /// frame to start. "Networking is not ready yet, try again" was a dead end: the manager is
        /// created once at startup, so if it was ever absent no amount of waiting brought it back.
        /// </summary>
        private IEnumerator RecoverNetworkingThen(System.Action action)
        {
            SetStatus("Starting networking...", Accent);
            Debug.LogWarning("[AuraSync Lobby] NetworkManager was missing when the player pressed " +
                             "Create/Join; recreating it on demand.");

            NetworkManager.EnsureExists();
            yield return null; // its Start() (name + Photon connect) runs before the next Update

            if (NetworkManager.Instance == null)
            {
                SetStatus("Networking could not start. Close and reopen the app.", new Color(1f, 0.33f, 0.33f));
                yield break;
            }

            action();
        }

        private void OnCreatePressed()
        {
            if (NetworkManager.Instance == null)
            {
                StartCoroutine(RecoverNetworkingThen(OnCreatePressed));
                return;
            }

            ApplyName();
            SetStatus("Creating room...", Accent);
            NetworkManager.Instance.CreateRoom();
        }

        private void OnJoinPressed()
        {
            string input = roomCodeInput != null ? roomCodeInput.text : "";
            string code = RoomLinkManager.ExtractRoomCode(input);
            if (!RoomCodeGenerator.IsValidCode(code))
            {
                SetStatus("Please enter a valid AURA-XXXX code or invite link!", new Color(1f, 0.33f, 0.33f));
                return;
            }

            if (NetworkManager.Instance == null)
            {
                StartCoroutine(RecoverNetworkingThen(OnJoinPressed));
                return;
            }

            ApplyName();
            SetStatus($"Joining {code}...", Accent);
            NetworkManager.Instance.JoinRoom(code);
        }

        private void OnCopyLinkPressed()
        {
            if (!PhotonNetwork.InRoom) return;
            RoomLinkManager.CopyInviteLink(PhotonNetwork.CurrentRoom.Name);
            SetStatus("Invite link copied!", new Color(0f, 1f, 0.8f));
        }

        private void OnCopyCodePressed()
        {
            if (!PhotonNetwork.InRoom) return;
            GUIUtility.systemCopyBuffer = PhotonNetwork.CurrentRoom.Name;
            SetStatus("Room code copied!", new Color(0f, 1f, 0.8f));
        }

        private void OnLeavePressed()
        {
            NetworkManager.Instance?.LeaveRoom();
            SetStatus("Left the room.", Color.yellow);
            RefreshState();
        }

        // ──────────────────────────────────────────
        //  Network events
        // ──────────────────────────────────────────

        private void HandleJoinSuccess(string roomCode)
        {
            RefreshState();

            bool isPresenter = NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;
            bool roomHasModel = PhotonNetwork.CurrentRoom != null &&
                                PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(ModelSyncManager.KeyCurrentModelUrl);

            if (isPresenter)
            {
                SetStatus($"Room {roomCode} is ready. Share the code, then open a model " +
                          "(Space > Your Models). Everyone who joins will see it.", new Color(0f, 1f, 0.8f));
            }
            else if (roomHasModel)
            {
                // ModelSyncManager is already taking this player into the shared 3D view.
                SetStatus("Joined. Loading the shared model...", new Color(0f, 1f, 0.8f));
            }
            else
            {
                SetStatus("Joined. Waiting for the presenter to open a model. " +
                          "It will open here automatically.", new Color(0f, 1f, 0.8f));
            }
        }

        /// <summary>Once a joined player arrives in the shared 3D view, the panel gets out of the way.</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (PhotonNetwork.InRoom && scene.name == spaceSceneName) HideModal();
        }

        private void HandleJoinError(string error)
        {
            SetStatus(error, new Color(1f, 0.33f, 0.33f));
        }

        private void HandlePlayersChanged()
        {
            if (modalContainer != null && modalContainer.activeSelf) RefreshState();
        }
    }
}
