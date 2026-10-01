using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Networking;

namespace AuraSync.UI
{
    public class RoomLobbyUI : MonoBehaviour
    {
        public static RoomLobbyUI Instance { get; private set; }

        [Header("References")]
        [SerializeField] private GameObject modalContainer;
        [SerializeField] private TMP_InputField roomCodeInput;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private TextMeshProUGUI statusText;

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
        }

        void Start()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomJoinedSuccess += HandleJoinSuccess;
                NetworkManager.Instance.OnRoomJoinFailedError += HandleJoinError;
            }

            createRoomButton?.onClick.AddListener(OnCreatePressed);
            joinRoomButton?.onClick.AddListener(OnJoinPressed);
            closeButton?.onClick.AddListener(HideModal);
        }

        void OnDestroy()
        {
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomJoinedSuccess -= HandleJoinSuccess;
                NetworkManager.Instance.OnRoomJoinFailedError -= HandleJoinError;
            }
        }

        private void EnsureProceduralLobby()
        {
            if (modalContainer != null) return;

            GameObject canvasGo = new GameObject("RoomLobbyCanvas");
            canvasGo.transform.SetParent(transform);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);

            canvasGo.AddComponent<GraphicRaycaster>();

            // Dark Backdrop
            GameObject backdrop = new GameObject("Backdrop");
            backdrop.transform.SetParent(canvasGo.transform, false);

            RectTransform bdRt = backdrop.AddComponent<RectTransform>();
            bdRt.anchorMin = Vector2.zero;
            bdRt.anchorMax = Vector2.one;
            bdRt.sizeDelta = Vector2.zero;

            Image bdImg = backdrop.AddComponent<Image>();
            bdImg.color = new Color(0, 0, 0, 0.75f);
            bdImg.raycastTarget = false;

            // Modal Card
            GameObject card = new GameObject("LobbyCard");
            card.transform.SetParent(canvasGo.transform, false);

            RectTransform cardRt = card.AddComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.1f, 0.25f);
            cardRt.anchorMax = new Vector2(0.9f, 0.75f);
            cardRt.offsetMin = Vector2.zero;
            cardRt.offsetMax = Vector2.zero;

            Image cardImg = card.AddComponent<Image>();
            cardImg.color = new Color(0.12f, 0.14f, 0.20f, 0.95f);
            cardImg.raycastTarget = false;

            VerticalLayoutGroup vgroup = card.AddComponent<VerticalLayoutGroup>();
            vgroup.padding = new RectOffset(30, 30, 30, 30);
            vgroup.spacing = 20;
            vgroup.childAlignment = TextAnchor.MiddleCenter;
            vgroup.childControlWidth = true;
            vgroup.childControlHeight = false;

            // Title
            GameObject title = new GameObject("Title");
            title.transform.SetParent(card.transform, false);
            var tmpTitle = title.AddComponent<TextMeshProUGUI>();
            tmpTitle.text = "<b>AURASYNC MULTI-USER</b>";
            tmpTitle.fontSize = 36;
            tmpTitle.color = new Color(0.38f, 0.56f, 1.0f);
            tmpTitle.alignment = TextAlignmentOptions.Center;

            // Create Room Button
            createRoomButton = CreateLobbyButton(card.transform, "CreateBtn", "CREATE NEW ROOM", new Color(0.2f, 0.6f, 0.4f), OnCreatePressed);

            // Divider / Text
            GameObject div = new GameObject("DividerText");
            div.transform.SetParent(card.transform, false);
            var tmpDiv = div.AddComponent<TextMeshProUGUI>();
            tmpDiv.text = "— OR JOIN WITH ROOM CODE —";
            tmpDiv.fontSize = 20;
            tmpDiv.color = Color.gray;
            tmpDiv.alignment = TextAlignmentOptions.Center;

            // Code Input Field
            GameObject inputGo = new GameObject("CodeInputField");
            inputGo.transform.SetParent(card.transform, false);

            Image inputImg = inputGo.AddComponent<Image>();
            inputImg.color = new Color(0.2f, 0.22f, 0.28f);

            roomCodeInput = inputGo.AddComponent<TMP_InputField>();
            roomCodeInput.characterLimit = 0;

            GameObject textComponentGo = new GameObject("Text");
            textComponentGo.transform.SetParent(inputGo.transform, false);
            RectTransform textRt = textComponentGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero; textRt.anchorMax = Vector2.one; textRt.sizeDelta = Vector2.zero;
            var textComp = textComponentGo.AddComponent<TextMeshProUGUI>();
            textComp.fontSize = 30;
            textComp.color = Color.white;
            textComp.alignment = TextAlignmentOptions.Center;

            roomCodeInput.textComponent = textComp;
            roomCodeInput.placeholder = null;

            LayoutElement inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.preferredHeight = 70;

            // Join Room Button
            joinRoomButton = CreateLobbyButton(card.transform, "JoinBtn", "JOIN ROOM", new Color(0.25f, 0.45f, 0.85f), OnJoinPressed);

            // Status Text
            GameObject statusGo = new GameObject("StatusText");
            statusGo.transform.SetParent(card.transform, false);
            statusText = statusGo.AddComponent<TextMeshProUGUI>();
            statusText.text = "Max 5 Players per session";
            statusText.fontSize = 22;
            statusText.color = Color.yellow;
            statusText.alignment = TextAlignmentOptions.Center;

            // Close Button
            closeButton = CreateLobbyButton(card.transform, "CloseBtn", "CLOSE", new Color(0.3f, 0.3f, 0.35f), HideModal);

            modalContainer = canvasGo;
            modalContainer.SetActive(false); // Hidden by default; shown via ShareButton → ShowModal()
        }

        private Button CreateLobbyButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick)
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

        public void ShowModal()
        {
            if (modalContainer != null) modalContainer.SetActive(true);
            if (statusText) statusText.text = "Max 5 Players per session";
        }

        public void HideModal()
        {
            if (modalContainer != null) modalContainer.SetActive(false);
        }

        private void OnCreatePressed()
        {
            Debug.Log("[CLICK TEST] CREATE ROOM BUTTON CLICKED!");

            if (statusText) statusText.text = "<color=#6290FF>Creating room...</color>";

            if (NetworkManager.Instance == null)
            {
                Debug.LogError("[CLICK TEST] NetworkManager.Instance is NULL!");
                return;
            }

            Debug.Log("[CLICK TEST] NetworkManager found - calling CreateRoom()");
            NetworkManager.Instance.CreateRoom();
        }

        private void OnJoinPressed()
        {
            string input = roomCodeInput != null ? roomCodeInput.text : "";
            string code = RoomLinkManager.ExtractRoomCode(input);
            if (!RoomCodeGenerator.IsValidCode(code))
            {
                if (statusText) statusText.text = "<color=#FF5555>Please enter a valid AURA-XXXX code or invite link!</color>";
                return;
            }

            if (statusText) statusText.text = $"<color=#6290FF>Joining {code}...</color>";
            NetworkManager.Instance?.JoinRoom(code);
        }

        private void HandleJoinSuccess(string roomCode)
        {
            if (statusText) statusText.text = $"<color=#00FFCC>Joined Room {roomCode}!</color>";
            StartCoroutine(HideModalDelayed());
        }

        private void HandleJoinError(string error)
        {
            if (statusText) statusText.text = $"<color=#FF5555>{error}</color>";
        }

        private IEnumerator HideModalDelayed()
        {
            yield return new WaitForSeconds(1.2f);
            HideModal();
        }
    }
}






