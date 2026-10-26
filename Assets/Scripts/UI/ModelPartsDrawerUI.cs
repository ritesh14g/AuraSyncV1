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
    /// PARTS (top-left): the model explorer, sliding in from the left.
    ///
    ///   [Explode ─────●────── 64%]        always on top; dragging it fades the rest of the drawer
    ///   Together                  Apart   away so the explosion is fully visible
    ///   ROOM CONTROLS (in a room)
    ///   [🔍 Filter 14 parts]
    ///   Turbine casing                    large, readable names; indent shows the tree
    ///     Fan rotor                isolated
    ///   Tap to select · double-tap to isolate
    ///
    /// The drawer comes back when Parts is tapped again or the explosion is dragged back to 0.
    /// Double-tapping a part, in the list or on the model, isolates it; double-tapping it again,
    /// or double-tapping empty space, shows everything. While isolated, an "Isolated · Fan rotor ✕"
    /// chip stays at the top as a visible way out (the old CLEAR button is gone).
    ///
    /// Picking a row goes through <see cref="ModelPartSelector.SelectByPath"/>, so it obeys the same
    /// rules as tapping the model: the room's pick (the presenter's, or whoever holds Highlight) is
    /// shared, anyone else's stays on their own phone.
    /// </summary>
    public partial class ModelPartsDrawerUI : MonoBehaviour
    {
        public static ModelPartsDrawerUI Instance { get; private set; }

        private const int   MaxRows    = 150;   // more than this is browsed by filtering, not scrolling
        private const float RowHeight  = 104f;
        private const float RowSpacing = 6f;
        private const string SlotId    = "parts";
        private const string NavId     = "parts";

        private static readonly Color PanelColor = AuraTheme.A(AuraTheme.Indigo, 0.74f);

        private Canvas _canvas;
        private RectTransform _safe;
        private AuraSlidePanel _drawer;
        private Image _panelBg, _panelBorder;
        private CanvasGroup _rest;               // everything but the explode block
        private ScrollRect _scroll;
        private RectTransform _content;
        private TMP_InputField _filter;
        private TextMeshProUGUI _statusText;
        private bool _faded;
        private Coroutine _bgFade;

        private class RowView
        {
            public string path;
            public int index;
            public GameObject go;
            public Image bg, accent;
            public TextMeshProUGUI label;
            public GameObject isoTag;
            public bool isGroup;
            public float lastClick = -10f;
        }

        private readonly List<RowView> _rows = new List<RowView>();
        private readonly Dictionary<string, RowView> _byPath = new Dictionary<string, RowView>();

        private List<ModelPartCatalog.Entry> _entries = new List<ModelPartCatalog.Entry>();
        private int _skipped;
        private bool _modelLoaded;
        private bool _scrollOnNextChange;

        private Slider _explodeSlider;
        private TextMeshProUGUI _explodeValue, _explodeNote;
        private bool _suppressExplodeCallback;

        private GameObject _isoChip;
        private TextMeshProUGUI _isoChipText;

        private ModelPartSelector _boundSelector;
        private SpaceInteractionController _boundSic;
        private ModelExploder _boundExploder;
        private ModelIsolator _boundIsolator;
        private PartKnowledge _boundKnowledge;

        // ── Lifecycle ──────────────────────────────────────────────────

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
            StartCoroutine(AddSlot());
            BindControls();
        }

        void Update() => TickControls();

        void OnDestroy()
        {
            if (_boundSelector != null)
            {
                _boundSelector.OnSelectionChanged -= OnSelectionChanged;
                _boundSelector.OnPartDoubleTapped -= OnPartDoubleTapped;
            }
            if (_boundSic != null) _boundSic.OnTap -= OnModelTapped;
            if (_boundExploder != null) _boundExploder.OnAmountChanged -= RefreshExplodeRow;
            if (_boundIsolator != null) _boundIsolator.OnIsolationChanged -= OnIsolationChanged;
            if (_boundKnowledge != null) _boundKnowledge.OnChanged -= RebuildRows;
            UnbindControls();
            PanelNavigator.Remove(NavId);
            if (Instance == this) Instance = null;
        }

        private IEnumerator AddSlot()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddTopLeftButton(SlotId, "Parts", AuraIcons.Parts, OnPartsButton);
            SpaceSessionHUD.Instance.SetSlotVisible(SlotId, _modelLoaded);
        }

        private IEnumerator BindKnowledgeWhenReady()
        {
            while (PartKnowledge.Instance == null) yield return null;
            _boundKnowledge = PartKnowledge.Instance;
            _boundKnowledge.OnChanged += RebuildRows;
            RebuildRows();
        }

        private IEnumerator BindWhenReady()
        {
            while (ModelPartSelector.Instance == null) yield return null;
            _boundSelector = ModelPartSelector.Instance;
            _boundSelector.OnSelectionChanged += OnSelectionChanged;
            _boundSelector.OnPartDoubleTapped += OnPartDoubleTapped;

            while (SpaceInteractionController.Instance == null) yield return null;
            _boundSic = SpaceInteractionController.Instance;
            _boundSic.OnTap += OnModelTapped;

            while (ModelExploder.Instance == null) yield return null;
            _boundExploder = ModelExploder.Instance;
            _boundExploder.OnAmountChanged += RefreshExplodeRow;
            RefreshExplodeRow();

            while (ModelIsolator.Instance == null) yield return null;
            _boundIsolator = ModelIsolator.Instance;
            _boundIsolator.OnIsolationChanged += OnIsolationChanged;
            OnIsolationChanged();
        }

        /// <summary>Called by SpaceSceneManager once a model has loaded and been fitted.</summary>
        public void OnModelLoaded()
        {
            _modelLoaded = true;
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            _entries = ModelPartCatalog.Build(root, out _skipped);
            if (_filter != null) _filter.SetTextWithoutNotify("");
            RebuildRows();
            SpaceSessionHUD.Instance?.SetSlotVisible(SlotId, true);
            SetOpen(false); // the open drawer would be showing the old model's parts
            Debug.Log($"[PartsDrawer] Catalogued {_entries.Count} parts" +
                      (_skipped > 0 ? $" ({_skipped} past the {ModelPartCatalog.MaxEntries} cap)" : ""));
        }

        // ── Build ──────────────────────────────────────────────────────

        private void BuildUI()
        {
            _safe = AuraViewer.CreateCanvas(transform, "PartsCanvas", 97, out _canvas); // above the HUD (95)

            var panel = AuraKit.Rect(_safe, "Drawer");
            panel.anchorMin = new Vector2(0, 0);
            panel.anchorMax = new Vector2(0, 1);
            panel.pivot = new Vector2(0, 0.5f);
            panel.offsetMin = new Vector2(0, AuraViewer.BottomReserve);
            panel.offsetMax = new Vector2(0, -AuraViewer.TopReserve);
            panel.gameObject.AddComponent<DrawerWidth>();

            _panelBg = panel.gameObject.AddComponent<Image>();
            _panelBg.sprite = AuraSprites.Rounded;
            AuraSprites.SetRadius(_panelBg, AuraTheme.Radius + 10f);
            _panelBg.color = PanelColor;
            _panelBorder = AuraKit.Border(_panelBg, AuraTheme.A(AuraTheme.Slate, 0.7f), AuraTheme.Radius + 10f);

            var v = AuraKit.VStack(panel, 22f);
            v.padding = new RectOffset(40, 32, 34, 30);
            v.childForceExpandHeight = false;

            BuildExplodeBlock(panel);

            // Everything below the slider fades together while exploding.
            var rest = AuraKit.Rect(panel, "Rest");
            AuraKit.Size(rest, -1, 200f, -1, 1f);
            _rest = rest.gameObject.AddComponent<CanvasGroup>();
            var rv = AuraKit.VStack(rest, 18f);
            rv.childForceExpandHeight = false;

            BuildControlsBox(rest);
            _filter = BuildFilter(rest);
            BuildList(rest);
            _statusText = AuraKit.Text(rest, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _statusText.richText = true;
            AuraKit.Size(_statusText, -1, 40f);

            // Slides in from the left edge (the y keeps the top/bottom reserves set above).
            float y = panel.anchoredPosition.y;
            _drawer = AuraSlidePanel.Attach(panel, new Vector2(0, y), new Vector2(-900f, y));

            BuildIsolationChip();
        }

        private void BuildExplodeBlock(RectTransform parent)
        {
            var block = AuraKit.Box(parent, "Explode", AuraTheme.A(AuraTheme.Ink, 0.55f), AuraTheme.Radius, true);
            var bv = AuraKit.VStack(block, 2f);
            bv.padding = new RectOffset(28, 28, 18, 14);
            AuraKit.FitHeight(block);

            var head = AuraKit.Rect(block.transform, "Head");
            AuraKit.HStack(head, 10f);
            AuraKit.Size(head, -1, 44f);
            AuraKit.Icon(head, AuraIcons.Explode, 38f, AuraTheme.Lavender);
            var title = AuraKit.Text(head, "Explode", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            AuraKit.Flex(title);
            _explodeValue = AuraKit.Text(head, "0%", AuraTheme.SizeBody, AuraTheme.Lavender, AuraWeight.Bold,
                                         TextAlignmentOptions.MidlineRight);
            AuraKit.Size(_explodeValue, 140f);

            _explodeSlider = AuraWidgets.Slider(block.transform, 0f, OnExplodeChanged,
                                                () => ModelExploder.Instance?.CommitInteraction(), 0f, 1f, 92f);

            var ticks = AuraKit.Rect(block.transform, "Ticks");
            AuraKit.HStack(ticks, 0f);
            AuraKit.Size(ticks, -1, 34f);
            AuraKit.Flex(AuraKit.Text(ticks, "Together", AuraTheme.SizeCaption - 2f, AuraTheme.TextDim));
            AuraKit.Flex(AuraKit.Text(ticks, "Apart", AuraTheme.SizeCaption - 2f, AuraTheme.TextDim,
                                      AuraWeight.Regular, TextAlignmentOptions.MidlineRight));

            _explodeNote = AuraKit.Text(block.transform, "", AuraTheme.SizeCaption - 2f, AuraTheme.Lavender);
            _explodeNote.gameObject.SetActive(false);
        }

        private TMP_InputField BuildFilter(RectTransform parent)
        {
            var f = AuraWidgets.Input(parent, "Filter parts", "", false, 112f, 40);
            // A search icon at the start of the field.
            var area = f.textViewport;
            area.offsetMin = new Vector2(90, area.offsetMin.y);
            var icon = AuraKit.Icon(f.transform, AuraIcons.Search, 42f, AuraTheme.TextDim);
            icon.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(42, 42));
            f.onValueChanged.AddListener(_ => RebuildRows());
            return f;
        }

        private void BuildList(RectTransform parent)
        {
            var host = AuraKit.Rect(parent, "List");
            AuraKit.Size(host, -1, 200f, -1, 1f);
            _content = AuraWidgets.Scroll(host, out _scroll, RowSpacing);
            _scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        private void BuildIsolationChip()
        {
            var chip = AuraKit.Box(_safe, "Isolated", AuraTheme.A(AuraTheme.Ink, 0.9f), 34f, true);
            AuraKit.Border(chip, AuraTheme.Lavender, 34f);
            _isoChip = chip.gameObject;
            AuraKit.Pin(chip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                        new Vector2(0, -(AuraViewer.TopReserve + 110f)), new Vector2(620, 84));
            var h = AuraKit.HStack(chip, 12f, 0, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(28, 18, 0, 0);
            _isoChipText = AuraKit.Text(chip.transform, "", AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.Regular);
            _isoChipText.richText = true;
            _isoChipText.enableWordWrapping = false;
            AuraKit.Flex(_isoChipText);
            AuraKit.Icon(chip.transform, AuraIcons.Close, 40f, AuraTheme.Lavender);
            AuraKit.MakeButton(_isoChip, chip, () => ModelIsolator.Instance?.ShowAll());
            _isoChip.SetActive(false);
        }

        // ── Explode ────────────────────────────────────────────────────

        private void OnExplodeChanged(float value)
        {
            if (_suppressExplodeCallback) return;
            ModelExploder.Instance?.SetAmount(value);
            RefreshExplodeRow();

            // Dragging the slider out fades the drawer so the explosion is fully visible;
            // dragging it back to 0 brings the drawer back.
            SetFaded(value > 0.001f);
        }

        /// <summary>Follows the exploder, including when someone else drives it for the room.</summary>
        private void RefreshExplodeRow()
        {
            if (_explodeSlider == null) return;
            ModelExploder exploder = ModelExploder.Instance;
            bool can = exploder != null && exploder.CanExplode;
            bool may = can && ModelExploder.LocalPlayerMayExplode;
            _explodeSlider.interactable = may;

            if (exploder != null && !Mathf.Approximately(_explodeSlider.value, exploder.Amount))
            {
                _suppressExplodeCallback = true;
                _explodeSlider.SetValueWithoutNotify(exploder.Amount);
                _suppressExplodeCallback = false;
            }
            _explodeValue.text = can ? $"{Mathf.RoundToInt((exploder != null ? exploder.Amount : 0f) * 100f)}%" : "—";

            string note = !can ? "This model doesn't come apart."
                        : !may ? $"{NoTags(Networking.ControlOwnership.HolderName(Networking.Control.Explode))} drives Explode"
                        : "";
            _explodeNote.text = note;
            _explodeNote.gameObject.SetActive(note.Length > 0);
        }

        /// <summary>Fades everything but the explode block (and lets touches through to the model).</summary>
        private void SetFaded(bool faded)
        {
            if (_faded == faded) return;
            _faded = faded;
            float a = faded ? 0f : 1f;
            AuraMotion.Fade(_rest, a, 0.2f);
            _rest.blocksRaycasts = !faded;
            _rest.interactable = !faded;
            _panelBg.raycastTarget = !faded;
            if (_bgFade != null) StopCoroutine(_bgFade);
            _bgFade = StartCoroutine(FadeBackground(faded ? 0f : PanelColor.a, faded ? 0f : 0.7f));
        }

        private IEnumerator FadeBackground(float bgA, float borderA)
        {
            Color b0 = _panelBg.color, r0 = _panelBorder.color;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.2f;
                float e = AuraMotion.Ease(t);
                _panelBg.color = AuraTheme.A(b0, Mathf.Lerp(b0.a, bgA, e));
                _panelBorder.color = AuraTheme.A(r0, Mathf.Lerp(r0.a, borderA, e));
                yield return null;
            }
        }

        // ── Rows ───────────────────────────────────────────────────────

        private void RebuildRows()
        {
            if (_content == null) return;

            AuraKit.ClearChildren(_content);
            _rows.Clear();
            _byPath.Clear();

            string filter = _filter != null ? _filter.text.Trim() : "";
            int matched = 0;

            // Unlabelled containers at the top of the tree ("Sketchfab_model",
            // "jet_engine_remaster.obj…") are file plumbing, not parts: their rows are left out
            // (their children still show), and indentation starts from the first real level.
            int baseDepth = int.MaxValue;
            foreach (var e in _entries) if (!IsPlumbing(e)) { baseDepth = Mathf.Min(baseDepth, e.depth); }
            if (baseDepth == int.MaxValue) baseDepth = 0;

            foreach (var e in _entries)
            {
                if (filter.Length == 0 && IsPlumbing(e)) continue;
                if (filter.Length > 0 && !PartNames.Matches(e.path, e.name, filter)) continue;
                matched++;
                if (_rows.Count >= MaxRows) continue;

                RowView row = BuildRow(e, filter.Length > 0 ? 0 : Mathf.Max(0, e.depth - baseDepth));
                row.index = _rows.Count;
                _rows.Add(row);
                _byPath[e.path] = row;
            }

            UpdateStatus(matched, filter);
            RefreshRowColors();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private static bool IsPlumbing(ModelPartCatalog.Entry e) =>
            !e.hasMesh && e.depth <= 1 && PartNames.Display(e.path, e.name) == e.name;

        private RowView BuildRow(ModelPartCatalog.Entry e, int level)
        {
            var bg = AuraKit.Box(_content, "Row", new Color(0, 0, 0, 0), AuraTheme.Radius - 12f, true);
            AuraKit.Size(bg, -1, RowHeight);

            var accent = AuraKit.Box(bg.transform, "Accent", AuraTheme.Lavender, 4f);
            AuraKit.Pin(accent.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(8, 60));
            accent.enabled = false;

            string shown = PartNames.Display(e.path, e.name);
            var label = AuraKit.Text(bg.transform, NoTags(shown), AuraTheme.SizeBody, AuraTheme.Text,
                                     e.hasMesh ? AuraWeight.SemiBold : AuraWeight.Regular);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            float indent = 26f + Mathf.Min(level, 5) * 34f;
            AuraKit.Fill(label.rectTransform, indent, 0, 150, 0);

            var tag = AuraKit.Text(bg.transform, "isolated", AuraTheme.SizeCaption - 2f, AuraTheme.Ink, AuraWeight.Bold,
                                   TextAlignmentOptions.MidlineRight);
            AuraKit.Fill(tag.rectTransform, 0, 0, 20, 0);
            tag.gameObject.SetActive(false);

            var row = new RowView { path = e.path, go = bg.gameObject, bg = bg, accent = accent, label = label,
                                    isoTag = tag.gameObject, isGroup = !e.hasMesh };
            AuraKit.MakeButton(bg.gameObject, bg, () => OnRowPressed(row));
            return row;
        }

        private void UpdateStatus(int matched, string filter)
        {
            if (_statusText == null) return;
            if (_entries.Count == 0) { _statusText.text = "This model has no named parts."; return; }

            string text;
            if (filter.Length > 0)
                text = matched == 0 ? $"No part matches \"{NoTags(filter)}\"." : $"{matched} of {_entries.Count} match";
            else
                text = "Tap to select · <color=#9894C4>double-tap to isolate</color>";
            if (matched > MaxRows) text += $" · showing {MaxRows}";
            _statusText.text = text;

            if (_filter != null && _filter.placeholder is TextMeshProUGUI ph)
                ph.text = $"Filter {_entries.Count} parts";
        }

        /// <summary>
        /// The room's pick (presenter, or whoever holds Highlight) is a solid lavender row; this
        /// phone's own pick is a lavender tint with an accent bar. The isolated part is tagged.
        /// </summary>
        private void RefreshRowColors()
        {
            string presenterPath = _boundSelector != null ? _boundSelector.PresenterPath : "";
            string localPath = _boundSelector != null ? _boundSelector.LocalPath : "";
            string iso = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "";

            foreach (RowView r in _rows)
            {
                if (r.bg == null) continue;
                bool room = !string.IsNullOrEmpty(presenterPath) && r.path == presenterPath;
                bool mine = !string.IsNullOrEmpty(localPath) && r.path == localPath;
                bool isolated = !string.IsNullOrEmpty(iso) && r.path == iso;

                r.bg.color = room ? AuraTheme.Lavender
                           : mine ? AuraTheme.A(AuraTheme.Lavender, 0.22f)
                           : new Color(0, 0, 0, 0);
                r.accent.enabled = mine && !room;
                r.label.color = room ? AuraTheme.Ink : (r.isGroup ? AuraTheme.TextDim : AuraTheme.Text);
                r.isoTag.SetActive(isolated);
                var tagText = r.isoTag.GetComponent<TextMeshProUGUI>();
                if (tagText != null) tagText.color = room ? AuraTheme.Ink : AuraTheme.Lavender;
            }
        }

        // ── Open / close ───────────────────────────────────────────────

        /// <summary>No Parts during the reassembly challenge: exploding or isolating would pull pieces from under the player.</summary>
        void LateUpdate()
        {
            if (!_modelLoaded) return;
            bool game = AuraSync.Challenge.ReassemblyGame.IsActive;
            var hud = SpaceSessionHUD.Instance;
            if (hud != null) hud.SetSlotVisible(SlotId, !game);
            if (game && IsOpen) SetOpen(false);
        }

        public bool IsOpen => _drawer != null && _drawer.IsShown;

        public void ToggleOpen() => SetOpen(!IsOpen);

        /// <summary>The Parts button: brings a faded drawer back, otherwise opens or closes it.</summary>
        private void OnPartsButton()
        {
            if (IsOpen && _faded) { SetFaded(false); return; }
            ToggleOpen();
        }

        public void SetOpen(bool open)
        {
            if (_drawer == null) return;
            if (open && !_modelLoaded) return;
            if (open == IsOpen) return;

            if (open)
            {
                ViewerPanels.Opened(NavId, () => SetOpen(false));
                PanelNavigator.Push(NavId, this, () => SetOpen(false));
                SetFaded(false);
                RefreshRowColors();
                RefreshExplodeRow();
                RefreshControls();
                _drawer.Show();
                ScrollSelectionIntoView();
            }
            else
            {
                _drawer.Hide();
                ViewerPanels.Closed(NavId);
                PanelNavigator.Remove(NavId);
                if (_filter != null && _filter.isFocused) _filter.DeactivateInputField();
            }
        }

        // ── Isolate ────────────────────────────────────────────────────

        /// <summary>The part isolate acts on: this phone's own pick, else the room's.</summary>
        private string SelectionForIsolate()
        {
            if (_boundSelector == null) return "";
            string path = _boundSelector.LocalPath;
            return string.IsNullOrEmpty(path) ? _boundSelector.PresenterPath : path;
        }

        /// <summary>
        /// Double tap: isolate that part; the isolated part again (or anything inside it), or empty
        /// space, shows everything.
        /// </summary>
        private void ToggleIsolation(string path)
        {
            var iso = ModelIsolator.Instance;
            if (iso == null || AuraSync.Challenge.ReassemblyGame.IsActive) return;

            if (string.IsNullOrEmpty(path))
            {
                if (iso.IsActive) iso.ShowAll();
                return;
            }
            bool sameOrInside = iso.IsActive && (path == iso.IsolatedPath || path.StartsWith(iso.IsolatedPath + "/"));
            if (sameOrInside) iso.ShowAll();
            else iso.Isolate(path);
        }

        private void OnPartDoubleTapped(string path) => ToggleIsolation(path);

        private void OnIsolationChanged()
        {
            RefreshRowColors();
            var iso = ModelIsolator.Instance;
            bool active = iso != null && iso.IsActive;
            if (_isoChip == null) return;
            if (active)
            {
                string name = "";
                if (_byPath.TryGetValue(iso.IsolatedPath, out RowView r) && r.label != null) name = r.label.text;
                if (string.IsNullOrEmpty(name))
                    foreach (var e in _entries) if (e.path == iso.IsolatedPath) { name = PartNames.Display(e.path, e.name); break; }
                _isoChipText.text = $"<color=#A9A8CC>Isolated ·</color> <b>{NoTags(name)}</b>";
            }
            _isoChip.SetActive(active);
        }

        // ── Events ─────────────────────────────────────────────────────

        private void OnRowPressed(RowView row)
        {
            float now = Time.unscaledTime;
            bool isDouble = now - row.lastClick < 0.35f;
            row.lastClick = isDouble ? -10f : now;

            if (isDouble)
            {
                ToggleIsolation(row.path);
                // The first tap toggled the selection; a double tap leaves the part selected.
                if (_boundSelector != null && _boundSelector.OwnPath != row.path) _boundSelector.SelectByPath(row.path);
                return;
            }
            _boundSelector?.SelectByPath(row.path);
        }

        private void OnModelTapped(Vector2 screenPos)
        {
            if (IsOpen) _scrollOnNextChange = true;
        }

        private void OnSelectionChanged()
        {
            RefreshRowColors();

            // While isolated, picking a different part moves the isolation to it. Clearing the
            // selection (or a double tap in progress) does not end isolation: that is the double
            // tap's or the chip's job.
            string sel = SelectionForIsolate();
            if (!ModelPartSelector.InDoubleTap && !string.IsNullOrEmpty(sel))
                ModelIsolator.Instance?.Reapply(sel);

            if (_scrollOnNextChange)
            {
                _scrollOnNextChange = false;
                if (IsOpen) ScrollSelectionIntoView();
            }
        }

        private void ScrollSelectionIntoView()
        {
            if (_boundSelector == null) return;
            string path = _boundSelector.LocalPath;
            if (string.IsNullOrEmpty(path)) path = _boundSelector.PresenterPath;
            if (string.IsNullOrEmpty(path)) return;
            if (_byPath.TryGetValue(path, out RowView row)) ScrollRowIntoView(row);
        }

        private void ScrollRowIntoView(RowView row)
        {
            if (_scroll == null || row == null || _rows.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            float viewportHeight = _scroll.viewport != null ? _scroll.viewport.rect.height : 0f;
            if (viewportHeight <= 1f) return;

            float pitch = RowHeight + RowSpacing;
            float contentHeight = _rows.Count * pitch - RowSpacing;
            float scrollable = contentHeight - viewportHeight;
            if (scrollable <= 1f) return;

            float desired = Mathf.Clamp(row.index * pitch - viewportHeight * 0.35f, 0f, scrollable);
            _scroll.verticalNormalizedPosition = 1f - (desired / scrollable);
        }

        private static string NoTags(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }

    /// <summary>The drawer is two-thirds of the screen wide, within comfortable limits.</summary>
    public class DrawerWidth : MonoBehaviour
    {
        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float w = Mathf.Clamp(parent.rect.width * 0.68f, 620f, 820f);
            if (!Mathf.Approximately(rt.sizeDelta.x, w)) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }
    }
}
