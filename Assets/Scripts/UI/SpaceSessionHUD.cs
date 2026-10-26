using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The 3D viewer's controls, laid out so the model owns the middle of the screen:
    ///
    ///   [top-left]    PARTS (the drawer slides in from the left) and the FOCUS eye (hides all UI)
    ///   [top-right]   ANALYSIS and ROOM (+ the mic in a room); the room's people as dots below
    ///   [right edge]  the tool dock in the lower half: edge tabs for Simulate, Section, AI, Notes,
    ///                 Play and Record, each with an icon and a short label
    ///   [bottom-left] BACK: closes the panel on top, or leaves the viewer
    ///   [bottom]      CATCH UP / FOLLOWING for viewers in a room
    ///   [top-centre]  the model's name and the selected part
    ///
    /// Features add their own buttons through the slot API (<see cref="AddRailButton"/> = a dock tab,
    /// <see cref="AddCornerButton"/> = top-right, <see cref="AddTopLeftButton"/>), so no feature
    /// edits this layout. The tab of the panel that is open is lit (see <see cref="ViewerPanels"/>).
    ///
    /// Focus mode is in SpaceSessionHUD.Focus.cs; the room button, card and people dots in
    /// SpaceSessionHUD.Room.cs.
    /// </summary>
    public partial class SpaceSessionHUD : MonoBehaviour
    {
        public static SpaceSessionHUD Instance { get; private set; }

        /// <summary>True while this layout is in use (the old floating ROOM square stays hidden).</summary>
        public static bool Active => Instance != null;

        // ── Canvas parts ───────────────────────────────────────────────
        private Canvas _canvas;
        private RectTransform _safe;
        private RectTransform _chrome;        // everything that a game hides (Back stays)
        private CanvasGroup _chromeGroup;
        private RectTransform _leftRow, _rightRow;
        private RectTransform _dock;
        private CanvasGroup _dockGroup;
        private TextMeshProUGUI _title;
        private GameObject _partChip;
        private TextMeshProUGUI _partChipText;
        private GameObject _backButton;
        private GameObject _catchUp;
        private Image _catchUpFill;
        private TextMeshProUGUI _catchUpText;

        private bool _modelLoaded;
        private string _currentModelPath = "";
        private bool _dockHidden, _railVisible = true;

        private SpaceInteractionController _boundSic;
        private ModelPartSelector _boundSelector;

        // ── Slots ──────────────────────────────────────────────────────
        private enum SlotKind { Dock, TopLeft, TopRight }

        private class Slot
        {
            public string id;
            public SlotKind kind;
            public int order;
            public RectTransform rect;
            public Button button;
            public Image fill;
            public Image icon;
            public TextMeshProUGUI label;
            public GameObject countBadge;
            public TextMeshProUGUI countText;
            public bool visible = true;
            public bool active;
            public Color? tint;
            public Action onDoubleTap;   // dock tabs: a quick second tap (Simulate / Section: switch off)
            public float lastTap = -10f;
        }

        private const float DoubleTapSeconds = 0.35f;
        private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();
        private bool _dockDirty = true;
        private float _lastSafeHeight;

        private static readonly Color SlotFill = AuraTheme.A(AuraTheme.Indigo, 0.94f);

        // Dock order (top to bottom) and icons for the known tools; anything else goes last.
        private static readonly Dictionary<string, int> DockOrder = new Dictionary<string, int>
        {
            { "simulate", 10 }, { "section", 20 }, { "ai", 30 }, { "notes", 40 }, { "play", 50 }, { "rec", 60 },
        };
        private static readonly Dictionary<string, string> SlotIcons = new Dictionary<string, string>
        {
            { "simulate", AuraIcons.Simulate }, { "section", AuraIcons.Section }, { "ai", AuraIcons.AI },
            { "notes", AuraIcons.Note }, { "play", AuraIcons.Puzzle }, { "rec", AuraIcons.Record },
            { "parts", AuraIcons.Parts }, { "analysis", AuraIcons.Chart }, { "reports", AuraIcons.Doc },
            { "views", AuraIcons.Eye }, { "find", AuraIcons.Target },
        };

        // ── Lifecycle ──────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildHUD();
        }

        void Start()
        {
            if (SpaceInteractionController.Instance != null)
            {
                _boundSic = SpaceInteractionController.Instance;
                _boundSic.OnSyncLockChanged += OnSyncLockChanged;
            }
            StartCoroutine(BindSelectorWhenReady());
            ControlOwnership.OnChanged += RefreshPartChip;
            ViewerPanels.OnChanged += OnPanelChanged;
            StartRoom();
            RefreshAll();
        }

        void OnDestroy()
        {
            if (_boundSic != null) _boundSic.OnSyncLockChanged -= OnSyncLockChanged;
            if (_boundSelector != null) _boundSelector.OnSelectionChanged -= RefreshPartChip;
            ControlOwnership.OnChanged -= RefreshPartChip;
            ViewerPanels.OnChanged -= OnPanelChanged;
            StopRoom();
            ExitFocusQuietly();
            ViewerPanels.Reset(); // the panels go with the scene; never call their closers later
            if (Instance == this) Instance = null;
        }

        private IEnumerator BindSelectorWhenReady()
        {
            while (ModelPartSelector.Instance == null) yield return null;
            _boundSelector = ModelPartSelector.Instance;
            _boundSelector.OnSelectionChanged += RefreshPartChip;
            RefreshPartChip();
        }

        /// <summary>Called by SpaceSceneManager once the model has loaded.</summary>
        public void OnModelLoaded(string displayName, string modelPath)
        {
            _modelLoaded = true;
            _currentModelPath = modelPath;
            if (_title != null) _title.text = (displayName ?? "").Replace('_', ' ').ToUpperInvariant();
            RefreshAll();
        }

        void Update()
        {
            float sh = _safe.rect.height;
            if (!Mathf.Approximately(sh, _lastSafeHeight)) { _lastSafeHeight = sh; _dockDirty = true; }
            if (_dockDirty) LayoutDock();
            UpdateCatchUpPulse();
            UpdateRoom();
        }

        // ── Build ──────────────────────────────────────────────────────

        private void BuildHUD()
        {
            _safe = AuraViewer.CreateCanvas(transform, "ViewerHUD", 95, out _canvas);

            _chrome = AuraKit.Fill(AuraKit.Rect(_safe, "Chrome"));
            _chromeGroup = _chrome.gameObject.AddComponent<CanvasGroup>();

            // Top corners: icon-over-label buttons in a row each.
            _leftRow = AuraKit.Rect(_chrome, "TopLeft");
            AuraKit.Pin(_leftRow, new Vector2(0, 1), new Vector2(0, 1),
                        new Vector2(AuraViewer.Gutter, -AuraViewer.Gutter), new Vector2(600, AuraViewer.TopButton));
            var lh = AuraKit.HStack(_leftRow, 18f, 0, TextAnchor.UpperLeft);
            lh.childForceExpandHeight = true;

            _rightRow = AuraKit.Rect(_chrome, "TopRight");
            AuraKit.Pin(_rightRow, new Vector2(1, 1), new Vector2(1, 1),
                        new Vector2(-AuraViewer.Gutter, -AuraViewer.Gutter), new Vector2(760, AuraViewer.TopButton));
            var rh = AuraKit.HStack(_rightRow, 18f, 0, TextAnchor.UpperRight);
            rh.childForceExpandHeight = true;
            rh.reverseArrangement = true; // the first added (Room) sits in the corner

            // Model name and the selected part, top centre under the corners.
            _title = AuraKit.Text(_chrome, "", AuraTheme.SizeLabel, AuraTheme.TextDim, AuraWeight.SemiBold,
                                  TextAlignmentOptions.Center);
            _title.characterSpacing = 6f;
            _title.enableWordWrapping = false;
            AuraKit.Pin(_title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                        new Vector2(0, -(AuraViewer.Gutter + AuraViewer.TopButton + 10f)), new Vector2(700, 40));

            BuildPartChip();
            BuildDock();
            BuildBack();
            BuildCatchUp();
            BuildRoom();   // Room first, so it sits in the top-right corner
            BuildFocus();
        }

        private void BuildPartChip()
        {
            var chip = AuraKit.Box(_chrome, "PartChip", AuraTheme.A(AuraTheme.Ink, 0.85f), 30f);
            AuraKit.Border(chip, AuraTheme.Slate, 30f);
            _partChip = chip.gameObject;
            AuraKit.Pin(chip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                        new Vector2(0, -(AuraViewer.Gutter + AuraViewer.TopButton + 56f)), new Vector2(620, 66));
            _partChipText = AuraKit.Text(chip.transform, "", AuraTheme.SizeSmall - 2f, AuraTheme.Text,
                                         AuraWeight.Regular, TextAlignmentOptions.Center);
            _partChipText.richText = true;
            _partChipText.enableWordWrapping = false;
            AuraKit.Fill(_partChipText.rectTransform, 24, 0, 24, 0);
            _partChip.SetActive(false);
        }

        private void BuildDock()
        {
            _dock = AuraKit.Rect(_chrome, "Dock");
            _dock.anchorMin = new Vector2(1, 0);
            _dock.anchorMax = new Vector2(1, 0);
            _dock.pivot = new Vector2(1, 0);
            _dock.sizeDelta = new Vector2(AuraViewer.DockWidth + TabOverhang + TabGrow, 1000f);
            _dock.anchoredPosition = new Vector2(0, AuraViewer.BottomReserve);
            _dockGroup = _dock.gameObject.AddComponent<CanvasGroup>();
            // A nested canvas: tabs sliding in and out never rebuild the rest of the HUD.
            _dock.gameObject.AddComponent<Canvas>();
            _dock.gameObject.AddComponent<GraphicRaycaster>();
        }

        private void BuildBack()
        {
            var back = AuraKit.Box(_safe, "Back", SlotFill, AuraTheme.Radius, true);
            AuraKit.Border(back, AuraTheme.Slate);
            _backButton = back.gameObject;
            AuraKit.Pin(back.rectTransform, new Vector2(0, 0), new Vector2(0, 0),
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));
            var h = AuraKit.HStack(back, 14f, 0, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(26, 30, 0, 0);
            AuraKit.Icon(back.transform, AuraIcons.Back, 44f, AuraTheme.Text);
            AuraKit.Text(back.transform, "Back", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold).enableWordWrapping = false;
            AuraKit.MakeButton(back.gameObject, back, OnBackPressed);
        }

        /// <summary>
        /// Catch up: just words at the bottom, between Back and the dock, that breathe in and out.
        /// The invisible strip behind them takes the tap.
        /// </summary>
        private void BuildCatchUp()
        {
            _catchUpFill = AuraKit.Box(_chrome, "CatchUp", new Color(0, 0, 0, 0), 0f, true); // tap area only
            _catchUp = _catchUpFill.gameObject;
            var rt = _catchUpFill.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(AuraViewer.Gutter + 270f + 16f, AuraViewer.Gutter);
            rt.offsetMax = new Vector2(-(AuraViewer.DockWidth + 16f), AuraViewer.Gutter + AuraViewer.BackHeight);
            _catchUpText = AuraKit.Text(_catchUpFill.transform, "Catch up", AuraTheme.SizeSmall, AuraTheme.Lavender,
                                        AuraWeight.SemiBold, TextAlignmentOptions.Center);
            _catchUpText.enableWordWrapping = true;
            _catchUpText.enableAutoSizing = true;
            _catchUpText.fontSizeMin = 20f;
            _catchUpText.fontSizeMax = AuraTheme.SizeSmall;
            AuraKit.Fill(_catchUpText.rectTransform, 8, 0, 8, 0);
            AuraKit.MakeButton(_catchUp, _catchUpFill, OnCatchUpPressed);
            _catchUp.SetActive(false);
        }

        // ── Slot API (used by the features) ────────────────────────────

        /// <summary>Adds a tool tab to the dock (the old left rail). Visible straight away.</summary>
        public Button AddRailButton(string id, string label, Action onClick)
        {
            if (!CanAdd(id)) return null;
            var s = MakeDockTab(id, label, onClick);
            _slots[id] = s;
            _dockDirty = true;
            return s.button;
        }

        /// <summary>Adds a button to the top-right row, left of Room.</summary>
        public Button AddCornerButton(string id, string label, Action onClick)
        {
            if (!CanAdd(id)) return null;
            var s = MakeTopButton(_rightRow, id, label, IconFor(id), onClick, SlotKind.TopRight);
            _slots[id] = s;
            return s.button;
        }

        /// <summary>Adds a button to the top-left row (Parts goes first, before the Focus eye).</summary>
        public Button AddTopLeftButton(string id, string label, string icon, Action onClick)
        {
            if (!CanAdd(id)) return null;
            var s = MakeTopButton(_leftRow, id, label, icon ?? IconFor(id), onClick, SlotKind.TopLeft);
            s.rect.SetSiblingIndex(0);
            _slots[id] = s;
            return s.button;
        }

        public bool HasSlot(string id) => _slots.ContainsKey(id);

        /// <summary>
        /// What a quick double tap on a dock tab does. The first tap still does the normal thing
        /// (open or close the sheet); the second, within 0.35 s, runs this instead.
        /// </summary>
        public void SetSlotDoubleTap(string id, Action onDoubleTap)
        {
            if (_slots.TryGetValue(id, out Slot s)) s.onDoubleTap = onDoubleTap;
        }

        private void OnTabClicked(Slot s, Action onClick)
        {
            float now = Time.unscaledTime;
            if (s.onDoubleTap != null && now - s.lastTap < DoubleTapSeconds)
            {
                s.lastTap = -10f;
                s.onDoubleTap();
                return;
            }
            s.lastTap = now;
            onClick?.Invoke();
        }

        /// <summary>Hides the whole tool dock (a replay drives the model; Phase 9).</summary>
        public void SetRailVisible(bool visible)
        {
            _railVisible = visible;
            ApplyDockVisibility();
        }

        /// <summary>The AI panel covers the right side: the dock steps aside while it is open.</summary>
        public void SetDockHidden(bool hidden) => HideDockFor("ai", hidden);

        /// <summary>
        /// Any panel that needs the right side or the full width (AI, the quiz) asks the dock to step
        /// aside under its own name; the dock comes back when nobody needs it gone.
        /// </summary>
        public void HideDockFor(string who, bool hide)
        {
            if (hide) _dockHiders.Add(who); else _dockHiders.Remove(who);

            // The AI panel shows "About · part" itself and its header reaches up to the model name:
            // the name and the part chip step aside while it is open.
            bool ai = _dockHiders.Contains("ai");
            if (ai != _aiOpen)
            {
                _aiOpen = ai;
                if (_title != null) _title.gameObject.SetActive(!ai);
                RefreshPartChip();
            }

            bool hidden = _dockHiders.Count > 0;
            if (hidden == _dockHidden) return;
            _dockHidden = hidden;
            ApplyDockVisibility();
        }

        private readonly HashSet<string> _dockHiders = new HashSet<string>();
        private bool _aiOpen;

        /// <summary>A game in progress: everything but Back steps aside.</summary>
        public void SetChromeHidden(bool hidden)
        {
            _chromeGroup.blocksRaycasts = !hidden;
            _chromeGroup.interactable = !hidden;
            AuraMotion.Fade(_chromeGroup, hidden ? 0f : 1f);
        }

        public void SetSlotVisible(string id, bool visible)
        {
            if (!_slots.TryGetValue(id, out Slot s)) return;
            if (s.visible == visible) return;
            s.visible = visible;
            if (s.kind == SlotKind.Dock) _dockDirty = true;
            else s.rect.gameObject.SetActive(visible);
        }

        public void SetSlotLabel(string id, string label)
        {
            if (_slots.TryGetValue(id, out Slot s) && s.label != null) s.label.text = Pretty(label);
        }

        public void SetSlotIcon(string id, string icon)
        {
            if (_slots.TryGetValue(id, out Slot s) && s.icon != null) s.icon.sprite = AuraIcons.Get(icon);
        }

        /// <summary>A special colour for a slot (Record while recording), or null for normal.</summary>
        public void SetSlotTint(string id, Color? tint)
        {
            if (!_slots.TryGetValue(id, out Slot s)) return;
            s.tint = tint;
            PaintSlot(s);
        }

        /// <summary>A small count badge on the slot; 0 hides it. Above 9 reads "9+".</summary>
        public void SetSlotCount(string id, int count)
        {
            if (!_slots.TryGetValue(id, out Slot s) || s.countBadge == null) return;
            s.countBadge.SetActive(count > 0);
            s.countText.text = count > 9 ? "9+" : count.ToString();
        }

        /// <summary>Lights a slot as "its panel is open". Normally driven by <see cref="ViewerPanels"/>.</summary>
        public void SetSlotActive(string id, bool active)
        {
            if (!_slots.TryGetValue(id, out Slot s) || s.active == active) return;
            s.active = active;
            PaintSlot(s);
            if (s.kind == SlotKind.Dock) _dockDirty = true;
        }

        private void OnPanelChanged(string openId)
        {
            foreach (var s in _slots.Values) SetSlotActive(s.id, s.id == openId);
        }

        private bool CanAdd(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!_slots.ContainsKey(id)) return true;
            Debug.LogWarning($"[SpaceSessionHUD] Slot '{id}' already exists; not adding it twice.");
            return false;
        }

        private static string IconFor(string id) =>
            id != null && SlotIcons.TryGetValue(id, out string icon) ? icon : AuraIcons.More;

        // ── Building slots ─────────────────────────────────────────────

        private const float TabOverhang = 60f;  // part of each tab past the screen edge (only left corners show)
        private const float TabGrow = 20f;      // the open tool's tab sticks out this much further

        private Slot MakeDockTab(string id, string label, Action onClick)
        {
            var fill = AuraKit.Box(_dock, "Tab_" + id, SlotFill, AuraTheme.Radius + 6f, true);
            AuraKit.Border(fill, AuraTheme.Slate, AuraTheme.Radius + 6f);
            var rt = fill.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(1, 0);
            rt.sizeDelta = new Vector2(AuraViewer.DockWidth + TabOverhang + TabGrow, 140f);
            rt.anchoredPosition = new Vector2(TabOverhang + TabGrow, 0f);

            // Content sits in the visible part of the tab.
            var content = AuraKit.Rect(rt, "Content");
            content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            content.sizeDelta = new Vector2(AuraViewer.DockWidth, 0);
            var v = AuraKit.VStack(content, 6f, 0, TextAnchor.MiddleCenter);
            v.childForceExpandWidth = false;
            v.padding = new RectOffset(4, 4, 8, 8);

            var icon = AuraKit.Icon(content, IconFor(id), 54f, AuraTheme.Lavender);
            var text = AuraKit.Text(content, Pretty(label), AuraTheme.SizeLabel - 3f, AuraTheme.TextDim, AuraWeight.Bold,
                                    TextAlignmentOptions.Center);
            text.characterSpacing = 2f;
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = AuraTheme.SizeLabel - 3f;
            AuraKit.Size(text, AuraViewer.DockWidth - 16f, 30f);

            var s = new Slot
            {
                id = id, kind = SlotKind.Dock, order = DockOrder.TryGetValue(id, out int o) ? o : 100 + _slots.Count,
                rect = rt, fill = fill, icon = icon, label = text,
            };
            s.button = AuraKit.MakeButton(fill.gameObject, fill, () => OnTabClicked(s, onClick));
            AddCountBadge(s, new Vector2(0, 1), new Vector2(22, -18));
            return s;
        }

        private Slot MakeTopButton(RectTransform row, string id, string label, string icon, Action onClick, SlotKind kind)
        {
            var fill = AuraKit.Box(row, "Top_" + id, SlotFill, AuraTheme.Radius, true);
            AuraKit.Border(fill, AuraTheme.Slate);
            AuraKit.Size(fill, string.IsNullOrEmpty(label) ? AuraViewer.TopButton : 168f, AuraViewer.TopButton);
            var v = AuraKit.VStack(fill, 4f, 0, TextAnchor.MiddleCenter);
            v.childForceExpandWidth = false;
            v.padding = new RectOffset(8, 8, 12, 10);

            var ic = AuraKit.Icon(fill.transform, icon, 52f, AuraTheme.Lavender);
            TextMeshProUGUI text = null;
            if (!string.IsNullOrEmpty(label))
            {
                text = AuraKit.Text(fill.transform, Pretty(label), AuraTheme.SizeLabel - 2f, AuraTheme.Text, AuraWeight.SemiBold,
                                    TextAlignmentOptions.Center);
                text.enableWordWrapping = false;
                text.enableAutoSizing = true;
                text.fontSizeMin = 16f;
                text.fontSizeMax = AuraTheme.SizeLabel - 2f;
                AuraKit.Size(text, 150f, 32f);
            }

            var s = new Slot { id = id, kind = kind, rect = fill.rectTransform, fill = fill, icon = ic, label = text };
            s.button = AuraKit.MakeButton(fill.gameObject, fill, onClick);
            AddCountBadge(s, new Vector2(1, 1), new Vector2(-14, -14));
            return s;
        }

        private static void AddCountBadge(Slot s, Vector2 anchor, Vector2 pos)
        {
            var badge = AuraKit.Rect(s.rect, "Count").gameObject.AddComponent<Image>();
            badge.sprite = AuraSprites.Disc;
            badge.color = AuraTheme.Rose;
            badge.raycastTarget = false;
            AuraKit.Pin(badge.rectTransform, anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(42, 42));
            badge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var t = AuraKit.Text(badge.transform, "", 22f, AuraTheme.Ink, AuraWeight.Bold, TextAlignmentOptions.Center);
            AuraKit.Fill(t.rectTransform);
            s.countBadge = badge.gameObject;
            s.countText = t;
            badge.gameObject.SetActive(false);
        }

        /// <summary>"SIMULATE" → "Simulate": tabs read in sentence case, like the rest of the app.</summary>
        private static string Pretty(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            if (label != label.ToUpperInvariant()) return label;
            return label.Length <= 2 ? label : char.ToUpperInvariant(label[0]) + label.Substring(1).ToLowerInvariant();
        }

        private void PaintSlot(Slot s)
        {
            if (s.fill == null) return;
            Color fill = s.tint ?? (s.active ? AuraTheme.Lavender : SlotFill);
            bool dark = s.active || s.tint.HasValue;
            s.fill.color = fill;
            if (s.icon != null) s.icon.color = dark ? AuraTheme.Ink : AuraTheme.Lavender;
            if (s.label != null) s.label.color = dark ? AuraTheme.Ink : (s.kind == SlotKind.Dock ? AuraTheme.TextDim : AuraTheme.Text);
        }

        // ── Dock layout ────────────────────────────────────────────────

        /// <summary>
        /// Stacks the visible tabs from the bottom up, in the lower half of the screen. Tab height
        /// shrinks on short screens so every tab always fits (never below a comfortable touch size).
        /// </summary>
        private void LayoutDock()
        {
            float safeH = _safe.rect.height;
            if (safeH <= 1f) return; // not laid out yet; try next frame
            _dockDirty = false;

            var tabs = new List<Slot>();
            foreach (var s in _slots.Values) if (s.kind == SlotKind.Dock) tabs.Add(s);
            tabs.Sort((a, b) => b.order.CompareTo(a.order)); // bottom-most first

            float avail = safeH * 0.52f - AuraViewer.BottomReserve;
            int n = 0;
            foreach (var s in tabs) if (s.visible) n++;
            float h = n > 0 ? Mathf.Clamp((avail - (n - 1) * AuraViewer.DockGap) / n, 112f, 150f) : 140f;

            float y = 0f;
            foreach (var s in tabs)
            {
                if (s.rect.gameObject.activeSelf != s.visible) s.rect.gameObject.SetActive(s.visible);
                if (!s.visible) continue;
                s.rect.sizeDelta = new Vector2(s.rect.sizeDelta.x, h);
                float x = s.active ? TabOverhang : TabOverhang + TabGrow;
                s.rect.anchoredPosition = new Vector2(s.rect.anchoredPosition.x, y);
                if (Mathf.Abs(s.rect.anchoredPosition.x - x) > 0.5f)
                    AuraMotion.Move(s.rect, new Vector2(x, y), null, -1f, 0.16f);
                y += h + AuraViewer.DockGap;
            }
        }

        private void ApplyDockVisibility()
        {
            bool show = _railVisible && !_dockHidden;
            _dockGroup.blocksRaycasts = show;
            _dockGroup.interactable = show;
            // Slide off the right edge rather than vanish.
            AuraMotion.Move(_dock, new Vector2(show ? 0f : AuraViewer.DockWidth + 80f, AuraViewer.BottomReserve),
                            _dockGroup, show ? 1f : 0f);
        }

        // ── Back, part chip, catch up ──────────────────────────────────

        /// <summary>Back closes the panel on top first; with nothing open it leaves the viewer.</summary>
        private void OnBackPressed()
        {
            if (PanelNavigator.Back()) return;
            SpaceSceneManager.ReturnToModelList();
        }

        private void RefreshPartChip()
        {
            if (_partChip == null) return;
            if (_aiOpen) { _partChip.SetActive(false); return; }
            string presenter = _boundSelector != null ? _boundSelector.PresenterPartName : "";
            string local = _boundSelector != null ? _boundSelector.LocalPartName : "";

            if (string.IsNullOrEmpty(presenter) && string.IsNullOrEmpty(local)) { _partChip.SetActive(false); return; }

            string who = ControlOwnership.IsHandedOut(Control.Highlight)
                ? ControlOwnership.HolderName(Control.Highlight).Replace("<", "")
                : "Presenter";

            string text;
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(presenter))
                text = $"<color=#A9A8CC>Selected ·</color> <b>{local}</b>";
            else
            {
                text = $"<color=#9894C4>{who}:</color> {presenter}";
                if (!string.IsNullOrEmpty(local) && local != presenter) text += $"   <color=#A9A8CC>You:</color> {local}";
            }
            _partChipText.text = text;
            _partChip.SetActive(true);
        }

        private void RefreshCatchUp()
        {
            bool inRoom = PhotonNetwork.InRoom;
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            bool show = inRoom && !isAdmin;
            if (_catchUp.activeSelf != show) _catchUp.SetActive(show);
            if (!show) return;

            bool following = _boundSic != null && _boundSic.IsSyncLocked;
            _catchUpText.text = following ? "Following presenter · tap to explore" : "Tap to catch up with presenter";
        }

        private void OnSyncLockChanged(bool isLocked) => RefreshCatchUp();

        private void OnCatchUpPressed()
        {
            if (_boundSic == null) return;
            if (_boundSic.IsSyncLocked) _boundSic.BreakSync();
            else _boundSic.SnapToAdmin();
        }

        private const float PulseWindowSeconds = 2.5f;

        /// <summary>
        /// Exploring on your own, "Tap to catch up" fades in and out (faster while the presenter is
        /// moving the model, so a viewer notices the presentation has moved on). Following, the
        /// words stay still and quieter.
        /// </summary>
        private void UpdateCatchUpPulse()
        {
            if (_catchUp == null || !_catchUp.activeSelf) return;
            bool following = _boundSic != null && _boundSic.IsSyncLocked;
            bool moving = !following && _boundSic != null &&
                          Time.unscaledTime - _boundSic.LastAdminMoveTime < PulseWindowSeconds;
            float a = following ? 0.6f
                    : Mathf.Lerp(0.25f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (moving ? 4.5f : 2.2f)));
            _catchUpText.color = AuraTheme.A(AuraTheme.Lavender, a);
        }

        private void RefreshAll()
        {
            RefreshCatchUp();
            RefreshRoom();
            RefreshPartChip();
        }

        [ContextMenu("Toggle state-bus logging")]
        private void ToggleStateBusLogging()
        {
            SharedStateBus.LogToConsole = !SharedStateBus.LogToConsole;
            Debug.Log($"[SpaceSessionHUD] State-bus logging {(SharedStateBus.LogToConsole ? "ON" : "OFF")}.");
        }
    }
}
