using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The SECTION toolbar (V3 Phase 6), above the bottom buttons:
    ///
    ///   [X] [Y] [Z]  FLIP  FILL  OFF  X      which way the cut faces; flip the removed half; filled
    ///                                         cut faces on/off (this phone only); remove the cut; close
    ///   MOVE  ----o--------                   slide the cut through the model
    ///   TILT  --------o----                   tilt it up to 60 degrees either way
    ///
    /// SECTION on the left rail opens it (and starts a cut if there is none). Closing the toolbar
    /// keeps the cut; OFF removes it. One finger on the model still turns the model, which is why
    /// the cut is steered from here rather than by handles on the model. While the toolbar is open a
    /// thin frame shows where the cut is.
    ///
    /// Sliders share on release, buttons at once, through SectionCutter.Commit (the "show to
    /// everyone?" question, or nothing with KEEP SHARING). A presenter who handed the Section control
    /// to a student sees the toolbar follow the student's cut, locked, until they take it back.
    /// </summary>
    public class SectionCutUI : MonoBehaviour
    {
        public static SectionCutUI Instance { get; private set; }

        private const string RailId = "section";
        private const string NavId  = "section";

        private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f, 0.96f);
        private static readonly Color OnColor    = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color OffColor   = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color DangerColor = new Color(0.55f, 0.18f, 0.18f, 1f);

        private GameObject _toolbar;
        private readonly Image[] _axisImg = new Image[3];
        private Image _flipImg, _fillImg;
        private Slider _move, _tilt;
        private TextMeshProUGUI _moveLabel, _tiltLabel, _status;
        private CanvasGroup _controls;
        private bool _suppress;
        private bool _railVisible;
        private SectionCutter _bound;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void Start() => StartCoroutine(Bind());

        void OnDestroy()
        {
            if (_bound != null) _bound.OnChanged -= Refresh;
            ControlOwnership.OnChanged -= Refresh;
            PanelNavigator.Remove(NavId);
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(RailId, "SECTION", ToggleToolbar);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);

            while (SectionCutter.Instance == null) yield return null;
            _bound = SectionCutter.Instance;
            _bound.OnChanged += Refresh;
            ControlOwnership.OnChanged += Refresh;
            Refresh();
        }

        void Update()
        {
            bool modelShown = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) &&
                              !AuraSync.Challenge.ReassemblyGame.IsActive;
            if (modelShown != _railVisible && SpaceSessionHUD.Instance != null)
            {
                _railVisible = modelShown;
                SpaceSessionHUD.Instance.SetSlotVisible(RailId, modelShown);
                if (!modelShown) CloseToolbar();
            }
        }

        // ── Opening ────────────────────────────────────────────────────────────

        private void ToggleToolbar()
        {
            if (_toolbar.activeSelf) CloseToolbar();
            else OpenToolbar();
        }

        private void OpenToolbar()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null) return;

            ModelPartsDrawerUI.Instance?.SetOpen(false);
            PartNotesPanelUI.Instance?.CloseList();
            SimulationPanelUI.Instance?.Hide(); // the simulation keeps running under the cut

            _toolbar.SetActive(true);
            cut.ShowFrame = true;
            PanelNavigator.Push(NavId, this, CloseToolbar);

            // Opening SECTION with no cut starts one, half way through the model.
            if (!cut.State.active && SectionCutter.LocalPlayerMayCut)
            {
                cut.SetState(SectionCutter.CutState.Default, dragging: false);
                cut.Commit();
            }
            Refresh();
        }

        /// <summary>Closes the toolbar; the cut stays.</summary>
        public void CloseToolbar()
        {
            if (_toolbar == null || !_toolbar.activeSelf) return;
            _toolbar.SetActive(false);
            if (SectionCutter.Instance != null) SectionCutter.Instance.ShowFrame = false;
            PanelNavigator.Remove(NavId);
        }

        // ── Changes ────────────────────────────────────────────────────────────

        private SectionCutter.CutState Current()
        {
            var s = SectionCutter.Instance != null ? SectionCutter.Instance.State : SectionCutter.CutState.Default;
            if (!s.active) s = SectionCutter.CutState.Default;
            return s;
        }

        private void Change(System.Func<SectionCutter.CutState, SectionCutter.CutState> edit, bool dragging)
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null || _suppress) return;
            var s = edit(Current());
            s.active = true;
            cut.SetState(s, dragging);
            if (!dragging) cut.Commit();
        }

        private void OnAxis(int axis) => Change(s => { s.axis = axis; s.tilt = 0f; return s; }, false);
        private void OnFlip() => Change(s => { s.flip = !s.flip; return s; }, false);

        private void OnFill() => SectionCutter.SetFill(!SectionCutter.Fill);

        private void OnOff()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null) return;
            cut.SetState(SectionCutter.CutState.Off, dragging: false);
            cut.Commit();
            CloseToolbar();
        }

        private void Refresh()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null) return;
            var s = cut.State;

            if (SpaceSessionHUD.Instance != null)
                SpaceSessionHUD.Instance.SetSlotLabel(RailId, s.active ? "SECTION\n(on)" : "SECTION");

            for (int i = 0; i < 3; i++) _axisImg[i].color = s.active && s.axis == i ? OnColor : OffColor;
            _flipImg.color = s.active && s.flip ? OnColor : OffColor;
            _fillImg.color = SectionCutter.Fill ? OnColor : OffColor;

            _suppress = true;
            _move.SetValueWithoutNotify(s.active ? s.move : 0f);
            _tilt.SetValueWithoutNotify(s.active ? s.tilt : 0f);
            _suppress = false;
            // The values ride in the bubbles above the knobs.
            _moveLabel.text = $"{Mathf.RoundToInt((s.active ? s.move : 0f) * 100f)}";
            _tiltLabel.text = $"{Mathf.RoundToInt(s.active ? s.tilt : 0f)}";

            bool may = SectionCutter.LocalPlayerMayCut;
            _controls.interactable = may;
            _controls.alpha = may ? 1f : 0.5f;

            string status = "";
            if (!may)
                status = $"{ControlOwnership.HolderName(Control.Section)} drives the cut. TAKE BACK is in the Parts drawer.";
            else if (!cut.ShaderCutAvailable)
                status = "Simple cut on this phone: cut faces are not filled, and it shows from the cut side only.";
            else if (PhotonNetwork.InRoom && LiveSharing.IsLive(Control.Section))
                status = "Shared live with the room.";
            _status.text = status;
            _status.gameObject.SetActive(status.Length > 0);
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 97; // the drawer's level; they never show together
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _toolbar = new GameObject("SectionToolbar", typeof(RectTransform));
            _toolbar.transform.SetParent(transform, false);
            var rt = (RectTransform)_toolbar.transform;
            // Pinned above the bottom buttons and exactly as tall as its rows (no empty band below).
            rt.anchorMin = new Vector2(0.16f, 0.215f);
            rt.anchorMax = new Vector2(0.98f, 0.215f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _toolbar.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _toolbar.AddComponent<Image>().color = PanelColor;
            _toolbar.AddComponent<TouchBlocker>(); // sliding the cut must not turn the model

            var vg = _toolbar.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(12, 12, 10, 10);
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            // Buttons row. Close stays usable when the rest is locked, so it sits outside the group.
            var top = Row(_toolbar.transform, 56f);
            var group = new GameObject("Controls", typeof(RectTransform));
            group.transform.SetParent(top.transform, false);
            group.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var gh = group.AddComponent<HorizontalLayoutGroup>();
            gh.spacing = 8;
            gh.childControlWidth = gh.childControlHeight = true;
            gh.childForceExpandWidth = false;
            var topGroup = group.AddComponent<CanvasGroup>();

            string[] axes = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int a = i;
                _axisImg[i] = Button(group.transform, axes[i], OffColor, () => OnAxis(a), 64f).GetComponent<Image>();
            }
            _flipImg = Button(group.transform, "FLIP", OffColor, OnFlip, 96f).GetComponent<Image>();
            Button(group.transform, "OFF", DangerColor, OnOff, 90f);
            // FILL is how this phone shows a cut, not a change to the cut, so it is never locked.
            _fillImg = Button(top.transform, "FILL", OnColor, OnFill, 96f).GetComponent<Image>();
            Button(top.transform, "X", new Color(0.45f, 0.14f, 0.14f), CloseToolbar, 56f);

            var sliders = new GameObject("Sliders", typeof(RectTransform));
            sliders.transform.SetParent(_toolbar.transform, false);
            var sv = sliders.AddComponent<VerticalLayoutGroup>();
            sv.spacing = 8;
            sv.childControlWidth = sv.childControlHeight = true;
            sv.childForceExpandWidth = true; sv.childForceExpandHeight = false;
            _controls = sliders.AddComponent<CanvasGroup>();

            _move = SliderRow(sliders.transform, "MOVE", -1f, 1f, "-100", "100", out _moveLabel,
                v => Change(s => { s.move = v; return s; }, true));
            _tilt = SliderRow(sliders.transform, "TILT", -SectionCutter.MaxTilt, SectionCutter.MaxTilt,
                              $"-{SectionCutter.MaxTilt:0}", $"{SectionCutter.MaxTilt:0}", out _tiltLabel,
                v => Change(s => { s.tilt = v; return s; }, true));

            _status = Text(_toolbar.transform, "", 18f, new Color(1f, 1f, 1f, 0.65f));
            _status.enableWordWrapping = true;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.minHeight = sle.preferredHeight = 30f;

            // The buttons row's own group follows the same lock as the sliders.
            _controlsTop = topGroup;
            _toolbar.SetActive(false);
        }

        private CanvasGroup _controlsTop;

        void LateUpdate()
        {
            if (_controlsTop == null || _controls == null) return;
            _controlsTop.interactable = _controls.interactable;
            _controlsTop.alpha = _controls.alpha;
        }

        /// <summary>
        /// One slider row: the name on the left, then a SleekSlider (thin rounded track, fill to a
        /// round knob, the value in a bubble above it, the ends labelled). Restyled 2026-10-13.
        /// </summary>
        private Slider SliderRow(Transform parent, string name, float min, float max, string minText, string maxText,
                                 out TextMeshProUGUI valueText, System.Action<float> onChange)
        {
            var row = Row(parent, SleekSlider.RowHeight);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;

            TextMeshProUGUI label = Text(row.transform, name, 19f, new Color(1f, 1f, 1f, 0.85f));
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.margin = new Vector4(0f, 0f, 0f, SleekSlider.RowHeight * 0.30f - 12f); // level with the track
            var lle = label.gameObject.AddComponent<LayoutElement>();
            lle.preferredWidth = lle.minWidth = 76f;

            Slider slider = SleekSlider.Create(row.transform, min, max, minText, maxText, out valueText);
            slider.onValueChanged.AddListener(v => onChange(v));

            // Letting go is when the cut is offered to the room.
            slider.gameObject.AddComponent<PointerUpRelay>().Released += () => SectionCutter.Instance?.Commit();
            return slider;
        }

        private static GameObject Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;
            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 8;
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
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 21f;
            return go;
        }
    }
}
