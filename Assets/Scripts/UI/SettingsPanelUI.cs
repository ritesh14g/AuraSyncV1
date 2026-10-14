using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;
using AuraSync.Recording;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// SETTINGS, top right (V3 Phase 9), for the presenter, or anyone alone (D6):
    ///
    ///     SETTINGS                                  X
    ///     RECORD & REPLAY
    ///     [ START RECORDING ]   or   [ STOP RECORDING  2:13 ]
    ///     Records the 3D session and your voice on this phone...
    ///     [ RECORDINGS (3) ]
    ///
    ///     CLASS TOOLS
    ///     [ HEATMAP ]  [ AI QUIZ ]       AI QUIZ only in a room: there is nobody to send it to alone (D6)
    ///
    /// Also **REC** in the same corner row while anyone in the room records: red, with the time on
    /// the recording phone (tap = stop), "REC" on everyone else's (tap = who is recording). STOP asks
    /// for a name; Back or SAVE with the box empty keeps the suggested one.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        public static SettingsPanelUI Instance { get; private set; }

        private const string SettingsId = "settings";
        private const string RecId      = "rec";
        private const string NavId      = "settings";
        private const string NameNavId  = "rec-name";

        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color RecColor    = new Color(0.80f, 0.12f, 0.14f, 1f);

        private GameObject _panel;
        private TextMeshProUGUI _recBtnText, _listBtnText, _recHint;
        private Image _recBtnImg;

        private GameObject _quizBtn;
        private GameObject _nameModal;
        private TMP_InputField _nameField;

        private Button _recSlot;
        private bool _settingsShown, _recShown;
        private int _recordingActor = -1;
        private float _nextText;
        private int _fileCount;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void Start() => StartCoroutine(Bind());

        void OnDestroy()
        {
            SessionRecorder.OnChanged -= Recount;
            PanelNavigator.Remove(NavId);
            PanelNavigator.Remove(NameNavId);
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD hud = SpaceSessionHUD.Instance;
            hud.AddCornerButton(RecId, "REC", OnRecSlot);
            hud.SetSlotVisible(RecId, false);
            hud.AddCornerButton(SettingsId, "SETTINGS", TogglePanel);
            hud.SetSlotVisible(SettingsId, false);

            // The REC slot is red.
            _recSlot = FindSlot(hud, RecId);
            if (_recSlot != null)
            {
                _recSlot.GetComponent<Image>().color = RecColor;
                ColorBlock cb = _recSlot.colors;
                cb.highlightedColor = new Color(0.9f, 0.25f, 0.25f);
                cb.pressedColor = new Color(0.6f, 0.08f, 0.1f);
                _recSlot.colors = cb;
            }
            SessionRecorder.OnChanged += Recount;
        }

        /// <summary>Counts the recordings on disk (only when it can have changed, not every refresh).</summary>
        private void Recount()
        {
            _fileCount = RecordingStore.List().Count;
            Refresh();
        }

        private static Button FindSlot(SpaceSessionHUD hud, string id)
        {
            Transform t = FindDeep(hud.transform, "Slot_" + id);
            return t != null ? t.GetComponent<Button>() : null;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        void Update()
        {
            SpaceSessionHUD hud = SpaceSessionHUD.Instance;
            if (hud == null || !hud.HasSlot(SettingsId)) return;

            bool model = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) && !AuraSync.Challenge.ReassemblyGame.IsActive;
            bool settings = model && SessionRecorder.LocalMayRecord && !SessionPlayer.IsReplaying;
            if (settings != _settingsShown)
            {
                _settingsShown = settings;
                hud.SetSlotVisible(SettingsId, settings);
                if (!settings) ClosePanel();
            }

            SessionRecorder rec = SessionRecorder.Instance;
            bool mine = rec != null && rec.IsRecording;
            Player other = mine ? null : SessionRecorder.RecordingPlayer();
            bool showRec = mine || other != null;
            if (showRec != _recShown)
            {
                _recShown = showRec;
                hud.SetSlotVisible(RecId, showRec);
            }

            // Tell everyone when somebody starts recording.
            int actor = mine ? -2 : other != null ? other.ActorNumber : -1;
            if (actor != _recordingActor)
            {
                if (other != null && PhotonNetwork.LocalPlayer != null && other.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                    Toast.Show($"{NetworkManager.DisplayName(other)} is recording this session, with their own voice. Other voices are not recorded.");
                _recordingActor = actor;
            }

            if (Time.unscaledTime >= _nextText)
            {
                _nextText = Time.unscaledTime + 0.25f;
                if (showRec) hud.SetSlotLabel(RecId, mine ? "REC " + RecordingStore.Clock(rec.Elapsed) : "REC");
                if (_panel.activeSelf) Refresh();
            }
        }

        // ── REC slot ───────────────────────────────────────────────────────────

        private void OnRecSlot()
        {
            SessionRecorder rec = SessionRecorder.Instance;
            if (rec != null && rec.IsRecording) { StopAndName(); return; }
            Player p = SessionRecorder.RecordingPlayer();
            Toast.Show(p != null
                ? $"{NetworkManager.DisplayName(p)} is recording this session: the model, notes, views, pings and {NetworkManager.DisplayName(p)}'s voice. Your voice is not recorded."
                : "Nobody is recording.");
        }

        // ── Panel ──────────────────────────────────────────────────────────────

        private void TogglePanel()
        {
            if (_panel.activeSelf) ClosePanel();
            else OpenPanel();
        }

        private void OpenPanel()
        {
            _panel.SetActive(true);
            PanelNavigator.Push(NavId, this, ClosePanel);
            Recount();
        }

        public void ClosePanel()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        private void Refresh()
        {
            if (_recBtnText == null) return;
            SessionRecorder rec = SessionRecorder.Instance;
            bool on = rec != null && rec.IsRecording;
            _recBtnText.text = on ? $"STOP RECORDING   {RecordingStore.Clock(rec.Elapsed)}" : "START RECORDING";
            _recBtnImg.color = on ? RecColor : AccentColor;
            _recHint.text = PhotonNetwork.InRoom
                ? "Records the 3D session on this phone: the model, notes, everyone's views and pings, and your voice while your mic is on " +
                  "(other people's voices are not recorded, only when they spoke). Everyone in the room sees REC."
                : "Records what you do with the model, and your voice, on this phone: a narrated walkthrough to replay or share.";
            _listBtnText.text = $"RECORDINGS ({_fileCount})";
            if (_quizBtn != null) _quizBtn.SetActive(PhotonNetwork.InRoom);
        }

        private void OnRecordPressed()
        {
            SessionRecorder rec = SessionRecorder.Instance;
            if (rec == null) return;
            if (rec.IsRecording) { StopAndName(); return; }

            if (rec.Begin())
            {
                bool muted = PhotonNetwork.InRoom && VoiceChatManager.Instance != null && VoiceChatManager.Instance.IsMuted;
                Toast.Show(PhotonNetwork.InRoom
                    ? (muted ? "Recording. Your mic is off: turn it on to record your voice. Everyone sees REC." : "Recording, with your voice. Everyone in the room sees REC.")
                    : "Recording, with your voice.");
                ClosePanel();
            }
            else Toast.Show("Could not start recording.");
            Refresh();
        }

        private void OpenRecordings()
        {
            ClosePanel();
            RecordingsUI.Instance?.OpenList();
        }

        // ── Naming ─────────────────────────────────────────────────────────────

        private void StopAndName()
        {
            SessionRecorder rec = SessionRecorder.Instance;
            if (rec == null || !rec.IsRecording) return;
            rec.Stop(askName: true);
            ClosePanel();

            _nameField.text = rec.PendingDefaultName ?? "";
            _nameModal.SetActive(true);
            PanelNavigator.Push(NameNavId, this, () => SaveName(useDefault: true));
        }

        private void SaveName(bool useDefault)
        {
            if (!_nameModal.activeSelf) return;
            _nameModal.SetActive(false);
            PanelNavigator.Remove(NameNavId);
            SessionRecorder.Instance?.FinishPending(useDefault ? null : _nameField.text);
            Toast.Show("Recording saved. Settings > Recordings to replay or share it.");
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99; // over the drawer (97) and chat (98), under the room panel (100)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Panel under the corner row, right-aligned
            _panel = new GameObject("SettingsPanel", typeof(RectTransform));
            _panel.transform.SetParent(transform, false);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = new Vector2(0.30f, 0.84f);
            rt.anchorMax = new Vector2(0.98f, 0.84f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _panel.AddComponent<Image>().color = PanelColor;
            _panel.AddComponent<TouchBlocker>();
            var vg = _panel.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 12, 16);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var head = Row(_panel.transform, 52f);
            var title = Text(head.transform, "SETTINGS", 24f, Color.white);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(head.transform, "X", new Color(0.45f, 0.14f, 0.14f), ClosePanel, 52f);

            var section = Text(_panel.transform, "RECORD & REPLAY", 19f, new Color(1f, 1f, 1f, 0.6f));
            section.fontStyle = FontStyles.Bold;

            var recRow = Row(_panel.transform, 64f);
            GameObject recBtn = Button(recRow.transform, "START RECORDING", AccentColor, OnRecordPressed, 100f);
            recBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _recBtnImg = recBtn.GetComponent<Image>();
            _recBtnText = recBtn.GetComponentInChildren<TextMeshProUGUI>();

            _recHint = Text(_panel.transform, "", 17f, new Color(1f, 1f, 1f, 0.65f));
            _recHint.enableWordWrapping = true;

            var listRow = Row(_panel.transform, 60f);
            GameObject listBtn = Button(listRow.transform, "RECORDINGS", ButtonColor, OpenRecordings, 100f);
            listBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _listBtnText = listBtn.GetComponentInChildren<TextMeshProUGUI>();

            var tools = Text(_panel.transform, "CLASS TOOLS", 19f, new Color(1f, 1f, 1f, 0.6f));
            tools.fontStyle = FontStyles.Bold;
            var toolRow = Row(_panel.transform, 60f);
            GameObject heat = Button(toolRow.transform, "HEATMAP", ButtonColor, () => { ClosePanel(); HeatmapUI.Instance?.Open(); }, 100f);
            heat.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _quizBtn = Button(toolRow.transform, "AI QUIZ", ButtonColor, () => { ClosePanel(); QuizTeacherUI.Instance?.Open(); }, 100f);
            _quizBtn.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _panel.SetActive(false);
            BuildNameModal();
        }

        private void BuildNameModal()
        {
            _nameModal = new GameObject("NameModal", typeof(RectTransform));
            _nameModal.transform.SetParent(transform, false);
            var mrt = (RectTransform)_nameModal.transform;
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one; mrt.offsetMin = mrt.offsetMax = Vector2.zero;
            var mc = _nameModal.AddComponent<Canvas>();
            mc.overrideSorting = true;
            mc.sortingOrder = 102;
            _nameModal.AddComponent<GraphicRaycaster>();
            _nameModal.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            _nameModal.AddComponent<TouchBlocker>();

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(_nameModal.transform, false);
            var crt = (RectTransform)card.transform;
            crt.anchorMin = new Vector2(0.08f, 0.5f); crt.anchorMax = new Vector2(0.92f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.AddComponent<Image>().color = PanelColor;
            var vg = card.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(24, 24, 20, 20);
            vg.spacing = 14;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var t = Text(card.transform, "Name this recording", 26f, Color.white);
            t.fontStyle = FontStyles.Bold;
            _nameField = Input(card.transform);

            var row = Row(card.transform, 64f);
            GameObject keep = Button(row.transform, "KEEP SUGGESTED", ButtonColor, () => SaveName(useDefault: true), 100f);
            keep.GetComponent<LayoutElement>().flexibleWidth = 1f;
            GameObject save = Button(row.transform, "SAVE", AccentColor, () => SaveName(useDefault: false), 100f);
            save.GetComponent<LayoutElement>().flexibleWidth = 1f;

            _nameModal.SetActive(false);
        }

        private static TMP_InputField Input(Transform parent)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(false); // TMP_InputField wires itself up on enable; configure it first
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 70f;
            go.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 1f);

            var area = new GameObject("TextArea", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(16f, 8f); art.offsetMax = new Vector2(-16f, -8f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = Text(area.transform, "Recording name", 24f, new Color(1f, 1f, 1f, 0.3f));
            Stretch(placeholder.rectTransform);
            TextMeshProUGUI text = Text(area.transform, "", 24f, Color.white);
            Stretch(text.rectTransform);
            text.richText = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = art;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 60;
            field.richText = false;
            go.SetActive(true);
            return field;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static GameObject Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;
            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 10;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;
            return go;
        }

        private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
            return t;
        }

        private static GameObject Button(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float width)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            var img = go.AddComponent<Image>();
            img.color = color;
            var b = go.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Text(go.transform, label, 21f, Color.white);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 21f;
            return go;
        }
    }
}
