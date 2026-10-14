using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Space;
using AuraSync.AI;

namespace AuraSync.UI
{
    /// <summary>
    /// The Model Parts drawer: a browsable list of the loaded model's parts, built procedurally at
    /// runtime like the rest of the Space HUD.
    ///
    /// Why a list at all, when parts can be tapped in the 3D view: a part can be hidden behind
    /// another, too small to hit on a phone, or known only by name. The list reaches all of them.
    ///
    /// Picking a row goes through <see cref="ModelPartSelector.SelectByPath"/>, so it obeys exactly
    /// the same rules as a tap on the model - the presenter's pick is shared with the room in gold,
    /// anyone else's stays on their own device in cyan - and the two stay in step: tap a part in the
    /// 3D view and its row lights up and scrolls into view.
    ///
    /// Layout: the drawer sits on the LEFT, above the corner ROOM button and clear of the Space
    /// HUD's player badges on the right. There is deliberately no dark backdrop behind it - the
    /// whole point is to watch the model highlight as you move down the list - so it closes from its
    /// own X, or from the PARTS button that opens it, rather than by tapping away.
    /// </summary>
    public partial class ModelPartsDrawerUI : MonoBehaviour
    {
        public static ModelPartsDrawerUI Instance { get; private set; }

        // Rows past this are not built even when they pass the filter. 400 TMP rows on a phone is a
        // visible stutter, and a list that long is browsed by filtering, not by scrolling.
        private const int   MaxRows    = 150;
        private const float RowHeight  = 52f;
        private const float RowSpacing = 4f;

        [Header("Placement (screen fractions)")]
        [Tooltip("The PARTS button. The default sits beside the corner ROOM button, above the " +
                 "Leave / Catch Up row. Move it if it lands on one of SpaceScene's own controls.")]
        [SerializeField] private Vector2 buttonAnchorMin = new Vector2(0.23f, 0.135f);
        [SerializeField] private Vector2 buttonAnchorMax = new Vector2(0.46f, 0.205f);

        [Tooltip("The drawer itself. The default keeps the right half of the screen clear so the " +
                 "model and the player badges stay visible while you read the list.")]
        // Starts at 0.16, right of the V3 feature rail (SpaceSessionHUD, 0.02-0.14), so an open
        // drawer never covers the rail's buttons.
        [SerializeField] private Vector2 panelAnchorMin = new Vector2(0.16f, 0.22f);
        [SerializeField] private Vector2 panelAnchorMax = new Vector2(0.64f, 0.84f);

        private static readonly Color PresenterRowColor = new Color(0.60f, 0.48f, 0.06f, 0.95f); // gold
        private static readonly Color LocalRowColor     = new Color(0.07f, 0.42f, 0.55f, 0.95f); // cyan
        private static readonly Color RowColor          = new Color(0.14f, 0.16f, 0.22f, 0.90f);
        private static readonly Color GroupLabelColor   = new Color(1f, 1f, 1f, 0.62f);

        private GameObject      _panel;
        private Button          _toggleButton;
        private TextMeshProUGUI _toggleLabel;
        private ScrollRect      _scroll;
        private RectTransform   _content;
        private TMP_InputField  _filter;
        private TextMeshProUGUI _statusText;

        private class RowView
        {
            public string           path;
            public int              index;   // position in the built list, for scrolling
            public GameObject       go;
            public Image            bg;
            public TextMeshProUGUI  label;
            public bool             isGroup;
        }

        private readonly List<RowView>              _rows   = new List<RowView>();
        private readonly Dictionary<string, RowView> _byPath = new Dictionary<string, RowView>();

        private List<ModelPartCatalog.Entry> _entries = new List<ModelPartCatalog.Entry>();
        private int  _skipped = 0;
        private bool _modelLoaded = false;

        // A pick made in the 3D view should pull its row into view; one made in the list, or by the
        // presenter on another device, should not yank the list out from under the reader.
        private bool _scrollOnNextChange = false;

        private Slider          _explodeSlider;
        private TextMeshProUGUI _explodeLabel;
        private bool            _suppressExplodeCallback;

        private Button          _isolateButton;
        private TextMeshProUGUI _isolateLabel;
        private Image           _isolateImage;
        private ModelIsolator   _boundIsolator;

        private ModelPartSelector          _boundSelector;
        private SpaceInteractionController _boundSic;
        private ModelExploder              _boundExploder;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
        }

        void Start()
        {
            StartCoroutine(BindWhenReady());
            StartCoroutine(BindKnowledgeWhenReady());
            BindControls();
        }

        void Update() => TickControls();

        private PartKnowledge _boundKnowledge;

        /// <summary>
        /// The part knowledge file arrives a moment after the model (it is downloaded separately).
        /// When it does, the rows switch from file names ("Object_6.1") to reviewed labels ("Fan rotor").
        /// </summary>
        private IEnumerator BindKnowledgeWhenReady()
        {
            while (PartKnowledge.Instance == null) yield return null;
            _boundKnowledge = PartKnowledge.Instance;
            _boundKnowledge.OnChanged += RebuildRows;
            RebuildRows();
        }

        void OnDestroy()
        {
            if (_boundSelector != null) _boundSelector.OnSelectionChanged -= OnSelectionChanged;
            if (_boundSic != null)      _boundSic.OnTap -= OnModelTapped;
            if (_boundExploder != null) _boundExploder.OnAmountChanged -= RefreshExplodeRow;
            if (_boundIsolator != null) _boundIsolator.OnIsolationChanged -= RefreshIsolateButton;
            if (_boundKnowledge != null) _boundKnowledge.OnChanged -= RebuildRows;
            UnbindControls();
            if (Instance == this) Instance = null;
        }

        private IEnumerator BindWhenReady()
        {
            while (ModelPartSelector.Instance == null) yield return null;
            _boundSelector = ModelPartSelector.Instance;
            _boundSelector.OnSelectionChanged += OnSelectionChanged;

            while (SpaceInteractionController.Instance == null) yield return null;
            _boundSic = SpaceInteractionController.Instance;
            _boundSic.OnTap += OnModelTapped;

            while (ModelExploder.Instance == null) yield return null;
            _boundExploder = ModelExploder.Instance;
            _boundExploder.OnAmountChanged += RefreshExplodeRow;
            RefreshExplodeRow();

            while (ModelIsolator.Instance == null) yield return null;
            _boundIsolator = ModelIsolator.Instance;
            _boundIsolator.OnIsolationChanged += RefreshIsolateButton;
            RefreshIsolateButton();
        }

        /// <summary>Called by SpaceSceneManager once a model has loaded and been fitted.</summary>
        public void OnModelLoaded()
        {
            _modelLoaded = true;

            Transform root = SpaceInteractionController.Instance != null
                ? SpaceInteractionController.Instance.ModelRoot
                : null;

            _entries = ModelPartCatalog.Build(root, out _skipped);

            if (_filter != null) _filter.SetTextWithoutNotify("");
            RebuildRows();

            if (_toggleButton != null) _toggleButton.gameObject.SetActive(true);

            // A new model means new parts; an open drawer would be showing the old ones.
            SetOpen(false);

            Debug.Log($"[PartsDrawer] Catalogued {_entries.Count} parts" +
                      (_skipped > 0 ? $" ({_skipped} past the {ModelPartCatalog.MaxEntries} cap)" : ""));
        }

        // ── Build ──────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 97; // above the Space HUD (95), below the room panel (100)

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();

            BuildToggleButton();
            BuildPanel();
        }

        private void BuildToggleButton()
        {
            GameObject go = new GameObject("PartsButton");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = buttonAnchorMin;
            rt.anchorMax = buttonAnchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.26f, 0.36f, 0.95f);

            _toggleButton = go.AddComponent<Button>();
            _toggleButton.onClick.AddListener(ToggleOpen);

            ColorBlock cb = _toggleButton.colors;
            cb.highlightedColor = new Color(0.32f, 0.38f, 0.50f);
            cb.pressedColor     = new Color(0.16f, 0.19f, 0.26f);
            _toggleButton.colors = cb;

            _toggleLabel = MakeLabel(go.transform, "Label", "PARTS", 23, Color.white, TextAlignmentOptions.Center);
            _toggleLabel.fontStyle = FontStyles.Bold;

            go.SetActive(false); // nothing to list until a model loads
        }

        private void BuildPanel()
        {
            _panel = new GameObject("PartsPanel");
            _panel.transform.SetParent(transform, false);

            RectTransform rt = _panel.AddComponent<RectTransform>();
            rt.anchorMin = panelAnchorMin;
            rt.anchorMax = panelAnchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = _panel.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.09f, 0.13f, 0.96f);
            bg.raycastTarget = true;
            _panel.AddComponent<TouchBlocker>(); // the model must not rotate under the drawer

            VerticalLayoutGroup vg = _panel.AddComponent<VerticalLayoutGroup>();
            vg.padding            = new RectOffset(10, 10, 10, 10);
            vg.spacing            = 8;
            vg.childControlWidth  = true;
            vg.childControlHeight = true;
            vg.childForceExpandWidth  = true;
            vg.childForceExpandHeight = false;

            BuildHeader(_panel.transform);
            BuildExplodeRow(_panel.transform);
            BuildControlsBox(_panel.transform);
            _filter = BuildFilterField(_panel.transform);
            BuildList(_panel.transform);

            _statusText = MakeLabel(_panel.transform, "Status", "", 19,
                                    new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft);
            AddLayout(_statusText.gameObject, height: 26f);

            _panel.SetActive(false);
        }

        private void BuildHeader(Transform parent)
        {
            GameObject row = new GameObject("Header");
            row.transform.SetParent(parent, false);
            AddLayout(row, height: 50f);

            HorizontalLayoutGroup hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.spacing            = 8;
            hg.childAlignment     = TextAnchor.MiddleLeft;
            hg.childControlWidth  = true;
            hg.childControlHeight = true;
            hg.childForceExpandWidth = false;

            TextMeshProUGUI title = MakeLabel(row.transform, "Title", "PARTS", 22,
                                              Color.white, TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            AddLayout(title.gameObject, flexWidth: 1f);

            _isolateButton = MakeSmallButton(row.transform, "IsolateBtn", "ISOLATE",
                                             IsolateIdleColor, OnIsolatePressed, width: 118f);
            _isolateLabel  = _isolateButton.GetComponentInChildren<TextMeshProUGUI>();
            _isolateImage  = _isolateButton.GetComponent<Image>();

            MakeSmallButton(row.transform, "ClearBtn", "CLEAR", new Color(0.28f, 0.30f, 0.38f),
                            OnClearPressed, width: 84f);
            MakeSmallButton(row.transform, "CloseBtn", "X", new Color(0.45f, 0.14f, 0.14f),
                            () => SetOpen(false), width: 50f);
        }

        // ── Exploded view ──────────────────────────────────────────────────────

        /// <summary>
        /// The explode slider lives in the drawer because this is the "model explorer": sliding the
        /// parts apart and picking them by name are the same job - seeing what the model is made of.
        /// </summary>
        private void BuildExplodeRow(Transform parent)
        {
            GameObject row = new GameObject("ExplodeRow");
            row.transform.SetParent(parent, false);
            AddLayout(row, height: 44f);

            HorizontalLayoutGroup hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.spacing            = 8;
            hg.childAlignment     = TextAnchor.MiddleLeft;
            hg.childControlWidth  = true;
            hg.childControlHeight = true;
            hg.childForceExpandWidth = false;

            _explodeLabel = MakeLabel(row.transform, "ExplodeLabel", "EXPLODE", 19,
                                      new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.MidlineLeft);
            AddLayout(_explodeLabel.gameObject, flexWidth: 0f);
            _explodeLabel.GetComponent<LayoutElement>().preferredWidth = 150f;

            _explodeSlider = BuildSlider(row.transform);
            AddLayout(_explodeSlider.gameObject, flexWidth: 1f);
        }

        private Slider BuildSlider(Transform parent)
        {
            GameObject go = new GameObject("ExplodeSlider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(false); // configure before the Slider's OnEnable wires itself up

            GameObject background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(go.transform, false);
            StretchHorizontalBar((RectTransform)background.transform, 0.32f);
            Image bgImg = background.AddComponent<Image>();
            bgImg.color = new Color(0.03f, 0.04f, 0.07f, 1f);

            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            StretchHorizontalBar((RectTransform)fillArea.transform, 0.32f);

            GameObject fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.22f, 0.48f, 1.0f, 1f);

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            RectTransform hart = (RectTransform)handleArea.transform;
            hart.anchorMin = Vector2.zero; hart.anchorMax = Vector2.one;
            hart.offsetMin = Vector2.zero; hart.offsetMax = Vector2.zero;

            GameObject handle = new GameObject("Handle", typeof(RectTransform));
            handle.transform.SetParent(handleArea.transform, false);
            ((RectTransform)handle.transform).sizeDelta = new Vector2(34f, 0f);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            Slider slider = go.AddComponent<Slider>();
            slider.fillRect      = (RectTransform)fill.transform;
            slider.handleRect    = (RectTransform)handle.transform;
            slider.targetGraphic = handleImg;
            slider.direction     = Slider.Direction.LeftToRight;
            slider.minValue      = 0f;
            slider.maxValue      = 1f;
            slider.value         = 0f;
            slider.onValueChanged.AddListener(OnExplodeChanged);

            // Letting go of the slider is when the presenter is asked whether to share the explosion.
            go.AddComponent<PointerUpRelay>().Released += () => ModelExploder.Instance?.CommitInteraction();

            go.SetActive(true);
            return slider;
        }

        private static void StretchHorizontalBar(RectTransform rt, float thickness)
        {
            rt.anchorMin = new Vector2(0f, 0.5f - thickness * 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f + thickness * 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void OnExplodeChanged(float value)
        {
            if (_suppressExplodeCallback) return;
            ModelExploder.Instance?.SetAmount(value);
            RefreshExplodeRow();
        }

        /// <summary>Follows the exploder, including when the presenter is the one driving it.</summary>
        private void RefreshExplodeRow()
        {
            if (_explodeSlider == null || _explodeLabel == null) return;

            ModelExploder exploder = ModelExploder.Instance;
            bool usable = exploder != null && exploder.CanExplode && ModelExploder.LocalPlayerMayExplode;

            _explodeSlider.interactable = usable;

            if (exploder != null && !Mathf.Approximately(_explodeSlider.value, exploder.Amount))
            {
                // Writing the value back would otherwise re-enter OnExplodeChanged and rebroadcast.
                _suppressExplodeCallback = true;
                _explodeSlider.SetValueWithoutNotify(exploder.Amount);
                _suppressExplodeCallback = false;
            }

            _explodeLabel.text = (exploder == null || !exploder.CanExplode)
                ? "EXPLODE n/a"
                : $"EXPLODE {Mathf.RoundToInt(exploder.Amount * 100f)}%";

            // A presenter who handed Explode out watches the slider move under the holder's hand.
            if (exploder != null && exploder.CanExplode && !ModelExploder.LocalPlayerMayExplode)
                _explodeLabel.text += "\n<size=70%>" + NoTags(AuraSync.Networking.ControlOwnership.HolderName(AuraSync.Networking.Control.Explode)) + "</size>";
        }

        private TMP_InputField BuildFilterField(Transform parent)
        {
            GameObject go = new GameObject("Filter");
            go.transform.SetParent(parent, false);

            // Built inactive: TMP_InputField wires itself to its text parts in OnEnable, which on a
            // live GameObject would run during AddComponent, before those parts are assigned below.
            go.SetActive(false);

            AddLayout(go, height: 54f);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.04f, 0.07f, 1f);

            GameObject area = new GameObject("TextArea");
            area.transform.SetParent(go.transform, false);
            RectTransform art = area.AddComponent<RectTransform>();
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(12f, 4f);
            art.offsetMax = new Vector2(-12f, -4f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = MakeLabel(area.transform, "Placeholder", "Filter parts...", 20,
                                                    new Color(1f, 1f, 1f, 0.32f), TextAlignmentOptions.MidlineLeft);
            TextMeshProUGUI text = MakeLabel(area.transform, "Text", "", 20,
                                             Color.white, TextAlignmentOptions.MidlineLeft);

            // The input field is added last: it wires itself up to the parts above on enable.
            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport  = art;
            field.textComponent = text;
            field.placeholder   = placeholder;
            field.lineType      = TMP_InputField.LineType.SingleLine;
            field.onValueChanged.AddListener(_ => RebuildRows());

            go.SetActive(true);
            return field;
        }

        private void BuildList(Transform parent)
        {
            GameObject scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(parent, false);
            AddLayout(scrollGo, height: 160f, flexHeight: 1f); // takes whatever the panel has left

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal        = false;
            _scroll.vertical          = true;
            _scroll.movementType      = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 28f;

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGo.transform, false);
            RectTransform vrt = viewport.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            vrt.pivot     = new Vector2(0f, 1f);

            // A graphic is needed for the viewport to catch the drag that scrolls the list.
            Image vImg = viewport.AddComponent<Image>();
            vImg.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            _content = content.AddComponent<RectTransform>();
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot     = new Vector2(0f, 1f);
            _content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup vg = content.AddComponent<VerticalLayoutGroup>();
            vg.spacing               = RowSpacing; // ScrollRowIntoView assumes this pitch
            vg.childControlWidth     = true;
            vg.childControlHeight    = true;
            vg.childForceExpandWidth = true;
            vg.childForceExpandHeight = false;

            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = vrt;
            _scroll.content  = _content;
        }

        // ── Rows ───────────────────────────────────────────────────────────────

        private void RebuildRows()
        {
            if (_content == null) return; // the filter can fire before the list exists

            foreach (RowView r in _rows)
                if (r.go != null) Destroy(r.go);
            _rows.Clear();
            _byPath.Clear();

            string filter = _filter != null ? _filter.text.Trim() : "";
            int matched = 0;

            foreach (ModelPartCatalog.Entry e in _entries)
            {
                // Searches the label and its other names too: "fan" finds Object_6.1.
                if (filter.Length > 0 && !PartNames.Matches(e.path, e.name, filter)) continue;

                matched++;
                if (_rows.Count >= MaxRows) continue; // keep counting, stop building

                RowView row = BuildRow(e, filter.Length > 0);
                row.index = _rows.Count;
                _rows.Add(row);
                _byPath[e.path] = row;
            }

            UpdateStatus(matched, filter);
            RefreshRowColors();

            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f; // back to the top
        }

        private RowView BuildRow(ModelPartCatalog.Entry e, bool filtering)
        {
            GameObject go = new GameObject("Row");
            go.transform.SetParent(_content, false);

            Image bg = go.AddComponent<Image>();
            bg.color = RowColor;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition    = Selectable.Transition.None; // rows are coloured by selection, not state

            string path = e.path; // captured per row, not the loop variable
            btn.onClick.AddListener(() => OnRowPressed(path));

            AddLayout(go, height: RowHeight);

            // Indentation shows the hierarchy. While filtering, matches come from all over the tree,
            // so indenting them by depth would only look arbitrary.
            float indent = filtering ? 12f : 12f + Mathf.Min(e.depth, 6) * 18f;

            // The reviewed label when there is one, with the file's own name after it in grey.
            string shown = PartNames.Display(e.path, e.name);
            string text = shown == e.name
                ? NoTags(e.name)
                : $"{NoTags(shown)}  <size=80%><color=#7A8499>{NoTags(e.name)}</color></size>";

            TextMeshProUGUI label = MakeLabel(go.transform, "Label", text, 21,
                                             e.hasMesh ? Color.white : GroupLabelColor,
                                             TextAlignmentOptions.MidlineLeft);
            RectTransform lrt = label.rectTransform;
            lrt.offsetMin = new Vector2(indent, 0f);
            lrt.offsetMax = new Vector2(-10f, 0f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            return new RowView { path = path, go = go, bg = bg, label = label, isGroup = !e.hasMesh };
        }

        private void UpdateStatus(int matched, string filter)
        {
            if (_statusText == null) return;

            if (_entries.Count == 0)
            {
                _statusText.text = "This model reports no named parts.";
                return;
            }

            string text;
            if (filter.Length > 0)
                text = matched == 0 ? $"No part matches \"{filter}\"." : $"{matched} of {_entries.Count} match";
            else
                text = $"{_entries.Count} parts";

            if (matched > MaxRows) text += $" - showing {MaxRows}, filter to narrow";
            if (filter.Length == 0 && _skipped > 0) text += $" (+{_skipped} not listed)";

            _statusText.text = text;
        }

        /// <summary>Tints each row by what is selected: gold for the room's pick, cyan for this device's.</summary>
        private void RefreshRowColors()
        {
            string presenterPath = _boundSelector != null ? _boundSelector.PresenterPath : "";
            string localPath     = _boundSelector != null ? _boundSelector.LocalPath : "";

            foreach (RowView r in _rows)
            {
                if (r.bg == null) continue;

                bool isPresenter = !string.IsNullOrEmpty(presenterPath) && r.path == presenterPath;
                bool isLocal     = !string.IsNullOrEmpty(localPath)     && r.path == localPath;

                r.bg.color = isPresenter ? PresenterRowColor
                           : isLocal     ? LocalRowColor
                                         : RowColor;

                if (r.label != null)
                {
                    bool selected = isPresenter || isLocal;
                    r.label.color = selected ? Color.white
                                             : (r.isGroup ? GroupLabelColor : Color.white);
                    r.label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        // ── Open / close ───────────────────────────────────────────────────────

        private const string NavId = "parts";

        /// <summary>
        /// No PARTS button during the reassembly challenge: exploding or isolating mid-game would pull
        /// the parts out from under the player.
        /// </summary>
        void LateUpdate()
        {
            if (_toggleButton == null || !_modelLoaded) return;
            bool hide = AuraSync.Challenge.ReassemblyGame.IsActive;
            if (_toggleButton.gameObject.activeSelf != hide) return;
            _toggleButton.gameObject.SetActive(!hide);
            if (hide) SetOpen(false);
        }

        public bool IsOpen => _panel != null && _panel.activeSelf;

        public void ToggleOpen() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            if (_panel == null) return;
            if (open && !_modelLoaded) return;

            if (open) PartNotesPanelUI.Instance?.CloseList(); // the notes list uses the same spot
            if (open) SectionCutUI.Instance?.CloseToolbar();  // and the section toolbar overlaps it
            if (open) SimulationPanelUI.Instance?.Hide();     // so does the simulation panel (it keeps running)
            _panel.SetActive(open);
            if (_toggleLabel != null) _toggleLabel.text = open ? "CLOSE" : "PARTS";

            // Back (on screen or the device's) closes the drawer when it is the top panel.
            if (open) PanelNavigator.Push(NavId, this, () => SetOpen(false));
            else      PanelNavigator.Remove(NavId);

            if (open)
            {
                RefreshRowColors();
                RefreshExplodeRow();
                RefreshIsolateButton();
                RefreshControls();
                ScrollSelectionIntoView();
            }
        }

        // ── Isolate ────────────────────────────────────────────────────────────

        private static readonly Color IsolateIdleColor   = new Color(0.28f, 0.30f, 0.38f, 1f);
        private static readonly Color IsolateActiveColor = new Color(0.20f, 0.52f, 0.42f, 1f);

        /// <summary>The part isolate would act on: this device's own pick, else the presenter's.</summary>
        private string SelectionForIsolate()
        {
            if (_boundSelector == null) return "";

            string path = _boundSelector.LocalPath;
            return string.IsNullOrEmpty(path) ? _boundSelector.PresenterPath : path;
        }

        private void OnIsolatePressed()
        {
            ModelIsolator isolator = ModelIsolator.Instance;
            if (isolator == null) return;

            if (isolator.IsActive) isolator.ShowAll();
            else                   isolator.Isolate(SelectionForIsolate());

            RefreshIsolateButton();
        }

        private void RefreshIsolateButton()
        {
            if (_isolateButton == null) return;

            ModelIsolator isolator = ModelIsolator.Instance;
            bool active = isolator != null && isolator.IsActive;

            // Nothing picked means nothing to isolate; the way out stays available regardless.
            // A presenter who handed Isolate out cannot use it until they take it back.
            _isolateButton.interactable = ModelIsolator.LocalPlayerMayIsolate &&
                                          (active || !string.IsNullOrEmpty(SelectionForIsolate()));

            if (_isolateLabel != null) _isolateLabel.text  = active ? "SHOW ALL" : "ISOLATE";
            if (_isolateImage != null) _isolateImage.color = active ? IsolateActiveColor : IsolateIdleColor;
        }

        // ── Events ─────────────────────────────────────────────────────────────

        private void OnRowPressed(string path)
        {
            // Same path as tapping the part in the 3D view, including the toggle-off on re-pick.
            _boundSelector?.SelectByPath(path);
        }

        private void OnClearPressed()
        {
            _boundSelector?.ClearSelection();
        }

        /// <summary>A tap landed on the model itself; its row should come into view.</summary>
        private void OnModelTapped(Vector2 screenPos)
        {
            if (IsOpen) _scrollOnNextChange = true;
        }

        private void OnSelectionChanged()
        {
            RefreshRowColors();

            // Picking a different part while isolated moves the isolation to it, rather than making
            // you leave isolation and come back for every part you want to look at.
            ModelIsolator.Instance?.Reapply(SelectionForIsolate());
            RefreshIsolateButton();

            if (_scrollOnNextChange)
            {
                _scrollOnNextChange = false;
                if (IsOpen) ScrollSelectionIntoView();
            }
        }

        private void ScrollSelectionIntoView()
        {
            if (_boundSelector == null) return;

            // This device's own pick first; a presenter has only the shared one.
            string path = _boundSelector.LocalPath;
            if (string.IsNullOrEmpty(path)) path = _boundSelector.PresenterPath;
            if (string.IsNullOrEmpty(path)) return;

            if (_byPath.TryGetValue(path, out RowView row)) ScrollRowIntoView(row);
        }

        private void ScrollRowIntoView(RowView row)
        {
            if (_scroll == null || row == null || _rows.Count == 0) return;

            Canvas.ForceUpdateCanvases(); // the viewport has no height until layout has run

            float viewportHeight = _scroll.viewport != null ? _scroll.viewport.rect.height : 0f;
            if (viewportHeight <= 1f) return;

            // Every row is the same height, so its offset is arithmetic - no need to read back
            // positions the layout may not have written yet.
            float pitch        = RowHeight + RowSpacing;
            float contentHeight = _rows.Count * pitch - RowSpacing;
            float scrollable    = contentHeight - viewportHeight;
            if (scrollable <= 1f) return; // the whole list already fits

            // Park the row a little below the top edge rather than flush against it.
            float desired = Mathf.Clamp(row.index * pitch - viewportHeight * 0.35f, 0f, scrollable);
            _scroll.verticalNormalizedPosition = 1f - (desired / scrollable);
        }

        // ── UI helpers ─────────────────────────────────────────────────────────

        private static TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size,
                                                 Color color, TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = size;
            tmp.color     = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;

            return tmp;
        }

        private Button MakeSmallButton(Transform parent, string name, string label, Color color,
                                       UnityEngine.Events.UnityAction onClick, float width)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Image img = go.AddComponent<Image>();
            img.color = color;

            Button btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth        = width;
            le.preferredWidth  = width;
            le.minHeight       = 44f;

            TextMeshProUGUI tmp = MakeLabel(go.transform, "Label", label, 21, Color.white,
                                            TextAlignmentOptions.Center);
            tmp.fontStyle = FontStyles.Bold;

            // The isolate button swaps between ISOLATE and the longer SHOW ALL, so let the text
            // shrink to fit rather than clip.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 13f;
            tmp.fontSizeMax = 21f;

            return btn;
        }

        private static string NoTags(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");

        private static void AddLayout(GameObject go, float height = 0f, float flexHeight = 0f,
                                      float flexWidth = 0f)
        {
            // A bare GameObject starts with a plain Transform. Layout only means anything on a
            // RectTransform, and relying on some later Graphic to convert it is too easy to break.
            if (!(go.transform is RectTransform)) go.AddComponent<RectTransform>();

            LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (height > 0f)     { le.minHeight = height; le.preferredHeight = height; }
            if (flexHeight > 0f) le.flexibleHeight = flexHeight;
            if (flexWidth > 0f)  le.flexibleWidth  = flexWidth;
        }
    }
}
