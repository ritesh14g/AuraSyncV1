using System;
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
    /// SECTION (dock tab), a sheet beside the tab:
    ///
    ///   Section                         [● Cut on]
    ///   [ X | Y | Z ]   (⇄ Flip)  (▧ Fill)
    ///   Position  ─────────●──────  +12
    ///   Tilt      ───────●────────   0°
    ///
    /// Opening it starts a cut half way through the model if there is none; closing keeps the cut;
    /// "Cut on" in the header removes it. One finger on the model still turns the model, which is
    /// why the cut is steered from here. While the sheet is open a thin lavender frame shows where
    /// the cut is.
    ///
    /// Sliders share on release, buttons at once, through SectionCutter.Commit. A presenter who
    /// handed the Section control to a student sees the sheet follow the student's cut, locked.
    /// </summary>
    public class SectionCutUI : MonoBehaviour
    {
        public static SectionCutUI Instance { get; private set; }

        private const string RailId = "section";
        private const string NavId = "section";

        private AuraSlidePanel _sheet;
        private RectTransform _sheetRt;
        private TextMeshProUGUI _status, _note;
        private Action<int> _setAxis;
        private Action<bool> _flipPaint, _fillPaint, _onPaint;
        private Slider _move, _tilt;
        private TextMeshProUGUI _moveValue, _tiltValue;
        private CanvasGroup _controls;
        private bool _suppress;
        private bool _railVisible;
        private SectionCutter _bound;

        private bool IsOpen => _sheet != null && _sheet.IsShown;

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
            SpaceSessionHUD.Instance.AddRailButton(RailId, "Section", ToggleToolbar);
            SpaceSessionHUD.Instance.SetSlotDoubleTap(RailId, OffAndClose);
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

        // ── Opening ────────────────────────────────────────────────────

        private void ToggleToolbar()
        {
            if (IsOpen) CloseToolbar();
            else OpenToolbar();
        }

        private void OpenToolbar()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null) return;

            ViewerPanels.Opened(RailId, CloseToolbar);
            cut.ShowFrame = true;
            PanelNavigator.Push(NavId, this, CloseToolbar);

            // Opening Section with no cut starts one, half way through the model.
            if (!cut.State.active && SectionCutter.LocalPlayerMayCut)
            {
                cut.SetState(SectionCutter.CutState.Default, dragging: false);
                cut.Commit();
            }
            Refresh();
            _sheet.Show();
        }

        /// <summary>Closes the sheet; the cut stays.</summary>
        public void CloseToolbar()
        {
            if (!IsOpen) return;
            _sheet.Hide();
            ViewerPanels.Closed(RailId);
            if (SectionCutter.Instance != null) SectionCutter.Instance.ShowFrame = false;
            PanelNavigator.Remove(NavId);
        }

        // ── Changes ────────────────────────────────────────────────────

        private SectionCutter.CutState Current()
        {
            var s = SectionCutter.Instance != null ? SectionCutter.Instance.State : SectionCutter.CutState.Default;
            if (!s.active) s = SectionCutter.CutState.Default;
            return s;
        }

        private void Change(Func<SectionCutter.CutState, SectionCutter.CutState> edit, bool dragging)
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

        /// <summary>The header switch: removes the cut (and closes), or starts one again.</summary>
        /// <summary>Double tap on the Section tab: the model is whole again and the sheet goes away.</summary>
        public void OffAndClose()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut != null && cut.State.active && SectionCutter.LocalPlayerMayCut)
            {
                cut.SetState(SectionCutter.CutState.Off, dragging: false);
                cut.Commit();
                Toast.Show("Section off.");
            }
            CloseToolbar();
        }

        private void OnCutSwitch()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null) return;
            if (cut.State.active)
            {
                cut.SetState(SectionCutter.CutState.Off, dragging: false);
                cut.Commit();
                CloseToolbar();
            }
            else if (SectionCutter.LocalPlayerMayCut)
            {
                cut.SetState(SectionCutter.CutState.Default, dragging: false);
                cut.Commit();
            }
        }

        private void Refresh()
        {
            SectionCutter cut = SectionCutter.Instance;
            if (cut == null) return;
            var s = cut.State;

            SpaceSessionHUD.Instance?.SetSlotLabel(RailId, s.active ? "Cut on" : "Section");

            _setAxis(s.active ? s.axis : 0);
            _flipPaint(s.active && s.flip);
            _fillPaint(SectionCutter.Fill);
            _onPaint(s.active);
            _status.text = "";

            _suppress = true;
            _move.SetValueWithoutNotify(s.active ? s.move : 0f);
            _tilt.SetValueWithoutNotify(s.active ? s.tilt : 0f);
            _suppress = false;
            int mv = Mathf.RoundToInt((s.active ? s.move : 0f) * 100f);
            _moveValue.text = mv > 0 ? "+" + mv : mv.ToString();
            _tiltValue.text = $"{Mathf.RoundToInt(s.active ? s.tilt : 0f)}°";

            bool may = SectionCutter.LocalPlayerMayCut;
            _controls.interactable = may;
            _controls.alpha = may ? 1f : 0.5f;

            string note = "";
            if (!may) note = $"{ControlOwnership.HolderName(Control.Section)} drives the cut. Take back is in Parts.";
            else if (!cut.ShaderCutAvailable) note = "Simple cut on this phone: cut faces are not filled, and it shows from the cut side only.";
            else if (PhotonNetwork.InRoom && LiveSharing.IsLive(Control.Section)) note = "Shared live with the room.";
            _note.text = note;
            _note.gameObject.SetActive(note.Length > 0);
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "SectionCanvas", 97, out _);
            _sheetRt = AuraViewer.ToolSheet(safe, "SectionSheet");

            // Header: title, and the cut's on/off switch on the right.
            var head = AuraKit.Rect(_sheetRt, "Header");
            AuraKit.HStack(head, 12f);
            AuraKit.Size(head, -1, 92f);
            var title = AuraKit.Text(head, "Section", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            AuraKit.Flex(title);
            _status = AuraKit.Caps(head, "", AuraTheme.Lavender);
            _status.gameObject.SetActive(false);
            _onPaint = AuraWidgets.Chip(head, "Cut on", AuraIcons.Section, true, OnCutSwitch, out _, 92f);

            var controls = AuraKit.Rect(_sheetRt, "Controls");
            AuraKit.VStack(controls, 16f);
            _controls = controls.gameObject.AddComponent<CanvasGroup>();

            var row = AuraKit.Rect(controls, "AxisRow");
            AuraKit.HStack(row, 18f);
            AuraKit.Size(row, -1, 112f);
            var axisHost = AuraKit.Rect(row, "Axis");
            AuraKit.Flex(axisHost);
            AuraKit.VStack(axisHost, 0f);
            _setAxis = AuraWidgets.Segmented(axisHost, new[] { "X", "Y", "Z" }, 0, OnAxis);
            _flipPaint = AuraWidgets.Chip(row, "Flip", AuraIcons.Flip, false, OnFlip, out _, 112f);

            _move = SliderRow(controls, "Position", -1f, 1f, out _moveValue, v => Change(s => { s.move = v; return s; }, true));
            _tilt = SliderRow(controls, "Tilt", -SectionCutter.MaxTilt, SectionCutter.MaxTilt, out _tiltValue,
                              v => Change(s => { s.tilt = v; return s; }, true));

            // Fill is how this phone shows a cut, not a change to the cut, so it is never locked.
            var fillRow = AuraKit.Rect(_sheetRt, "FillRow");
            AuraKit.HStack(fillRow, 14f);
            AuraKit.Size(fillRow, -1, 96f);
            _fillPaint = AuraWidgets.Chip(fillRow, "Filled cut faces", AuraIcons.Fill, true, OnFill, out _, 96f);
            AuraKit.Flex(AuraKit.Rect(fillRow, "Spacer"));

            _note = AuraKit.Text(_sheetRt, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _note.gameObject.SetActive(false);

            _sheet = AuraViewer.SlideUp(_sheetRt);
        }

        /// <summary>A labelled slider with its value on the right; letting go offers the cut to the room.</summary>
        private Slider SliderRow(RectTransform parent, string name, float min, float max,
                                 out TextMeshProUGUI value, Action<float> onChange)
        {
            var head = AuraKit.Rect(parent, name + "Head");
            AuraKit.HStack(head, 10f);
            AuraKit.Size(head, -1, 44f);
            AuraKit.Flex(AuraKit.Text(head, name, AuraTheme.SizeSmall + 2f, AuraTheme.Text, AuraWeight.SemiBold));
            value = AuraKit.Text(head, "", AuraTheme.SizeSmall + 2f, AuraTheme.Lavender, AuraWeight.Bold, TextAlignmentOptions.MidlineRight);
            AuraKit.Size(value, 140f);
            return AuraWidgets.Slider(parent, 0f, onChange, () => SectionCutter.Instance?.Commit(), min, max, 88f);
        }
    }
}
