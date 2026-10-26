using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.Simulation;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// SIMULATE (dock tab), a sheet beside the tab:
    ///
    ///   Chooser   Assembly animation  ›     (stacked, each with what it shows)
    ///             Working animation   ›
    ///   Assembly  [Take apart | Put together]
    ///             ┌ ‹         Remove the exhaust nozzle         › ┐   press the right half for the
    ///             │      Hold right to go forward · left back    │   next step, the left half for
    ///             └──────────────────────────────────────────────┘   the previous; holding repeats
    ///             Step ───────●────────────── 3 / 11
    ///             (1×)  (▶)  (Explain)          the card already steps, so no ◀ ▶ here
    ///   Working   Throttle 65%  ─────●─────     (Time of day for the House)
    ///             (1×)  (❚❚)  (Airflow)  (Explain)
    ///
    /// Stop (in the header, while something runs), Back (bottom-left) on a mode, or a double tap on
    /// the Simulate tab stops it and returns to the chooser; Back on the chooser closes the sheet. Opening another panel hides the sheet without stopping anything, and Simulate
    /// brings it back on the running mode.
    /// </summary>
    public class SimulationPanelUI : MonoBehaviour
    {
        public static SimulationPanelUI Instance { get; private set; }

        private const string RailId = "simulate";
        private const string NavPanel = "sim", NavMode = "sim-mode";

        private AuraSlidePanel _sheet;
        private RectTransform _sheetRt;
        private GameObject _chooser, _assembly, _working, _stopBtn;
        private TextMeshProUGUI _title, _status, _note;
        private CanvasGroup _assemblyControls, _workingControls;

        // Assembly
        private Action<int> _setDirection;
        private TextMeshProUGUI _stepText, _aSpeed;
        private Image _aPlayIcon;
        private Slider _stepSlider;
        private TextMeshProUGUI _stepValue;

        // Working
        private TextMeshProUGUI _wCaption, _knobName, _knobValue, _wSpeed;
        private Image _knobIcon, _wPlayIcon;
        private Slider _knob;
        private Action<bool> _airflowPaint;
        private GameObject _airflowBtn;
        private TextMeshProUGUI _workingSub;

        private bool _dragging;
        private bool _railVisible;

        private SimulationController Sim => SimulationController.Instance;
        private bool IsOpen => _sheet != null && _sheet.IsShown;

        // ── Lifecycle ──────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void Start() => StartCoroutine(Bind());

        void OnDestroy()
        {
            if (Sim != null) Sim.OnChanged -= Refresh;
            PanelNavigator.Remove(NavPanel);
            PanelNavigator.Remove(NavMode);
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(RailId, "Simulate", Toggle);
            SpaceSessionHUD.Instance.SetSlotDoubleTap(RailId, StopAndClose);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);
            while (Sim == null) yield return null;
            Sim.OnChanged += Refresh;
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
                if (!modelShown) Hide();
            }
            if (IsOpen) Live();
        }

        // ── Opening and closing ────────────────────────────────────────

        private void Toggle()
        {
            if (IsOpen) Hide();
            else Show();
        }

        private void Show()
        {
            ViewerPanels.Opened(RailId, Hide);
            PanelNavigator.Push(NavPanel, this, Hide);
            if (Sim != null && Sim.State.mode != SimMode.None) PanelNavigator.Push(NavMode, this, OnBack);
            Refresh();
            _sheet.Show();
        }

        /// <summary>Hides the sheet; whatever is running keeps running.</summary>
        public void Hide()
        {
            if (!IsOpen) return;
            _sheet.Hide();
            ViewerPanels.Closed(RailId);
            PanelNavigator.Remove(NavMode);
            PanelNavigator.Remove(NavPanel);
        }

        // ── Actions ────────────────────────────────────────────────────

        private void OnAssemblyChosen()
        {
            Sim?.Change(s => new SimState { mode = SimMode.Assembly, speed = 1f, basePhase = 0f, assemble = false, playing = false, start = SimulationController.Now });
            PanelNavigator.Push(NavMode, this, OnBack);
        }

        private void OnWorkingChosen()
        {
            if (Sim == null) return;
            Sim.Working.Prepare();
            bool day = Sim.Working.IsDayCycle;
            Sim.Change(s => new SimState
            {
                mode = SimMode.Working, speed = 1f, playing = true, start = SimulationController.Now,
                value = 0.5f, basePhase = day ? 8f : 0f, // a machine at half throttle; the House at 8 am
            });
            PanelNavigator.Push(NavMode, this, OnBack);
        }

        /// <summary>Double tap on the Simulate tab: switch the simulation off and put the sheet away.</summary>
        public void StopAndClose()
        {
            if (Sim != null && Sim.State.mode != SimMode.None && SimulationController.LocalPlayerMaySimulate)
            {
                OnBack();
                Toast.Show("Simulation off.");
            }
            Hide();
        }

        /// <summary>Stop / Back on a mode: stop it (the model goes back exactly as it was) and show the chooser.</summary>
        private void OnBack()
        {
            PanelNavigator.Remove(NavMode);
            Sim?.Change(s => SimState.Off);
        }

        private void OnPlay()
        {
            if (Sim == null) return;
            bool done = Sim.State.mode == SimMode.Assembly && Sim.AssemblyPosition(Sim.State) >= Sim.Assembly.StepCount;
            Sim.Change(s =>
            {
                if (done) { s.basePhase = 0f; s.playing = true; }
                else s.playing = !s.playing;
                return s;
            });
        }

        private void OnSpeed()
        {
            Sim?.Change(s =>
            {
                int i = Array.IndexOf(SimulationController.Speeds, s.speed);
                s.speed = SimulationController.Speeds[(i + 1) % SimulationController.Speeds.Length];
                return s;
            });
        }

        private void OnStep(int delta)
        {
            if (Sim == null || !SimulationController.LocalPlayerMaySimulate) return;
            Sim.Change(s =>
            {
                float pos = s.basePhase;
                float target = delta > 0 ? Mathf.Floor(pos) + 1f
                             : (pos - Mathf.Floor(pos) > 0.01f ? Mathf.Floor(pos) : Mathf.Floor(pos) - 1f);
                s.basePhase = Mathf.Clamp(target, 0f, Sim.Assembly.StepCount);
                s.playing = false;
                return s;
            });
        }

        private void OnDirection(bool assemble)
        {
            if (Sim == null || Sim.State.assemble == assemble) return;
            Sim.Change(s => { s.assemble = assemble; s.basePhase = 0f; s.playing = false; return s; });
        }

        private void OnAirflow()
        {
            if (Sim == null) return;
            Sim.Working.AirflowOn = !Sim.Working.AirflowOn;
            Refresh();
        }

        private void OnAi()
        {
            if (Sim == null || PartChatUI.Instance == null) return;
            SimState s = Sim.State;

            if (s.mode == SimMode.Assembly)
            {
                float pos = Sim.AssemblyPosition(s);
                AssemblyPlayer.Step step = Sim.Assembly.CurrentStep(s.assemble, pos, out int number);
                if (step == null || Sim.Assembly.GeneratedOrder)
                {
                    PartChatUI.Instance.AskAbout("", s.assemble ? "In what order would a building like this be put together?"
                                                                  : "How would a building like this be taken apart safely?");
                    return;
                }
                string verb = s.assemble ? "fitted" : "removed";
                PartChatUI.Instance.AskAbout(step.path,
                    $"Why is the {step.label.ToLowerInvariant()} {verb} at step {number} of {Sim.Assembly.StepCount}?");
                return;
            }

            var sel = ModelPartSelector.Instance;
            string path = sel == null ? "" : !string.IsNullOrEmpty(sel.LocalPath) ? sel.LocalPath : sel.PresenterPath ?? "";
            PartChatUI.Instance.AskAbout(path, "");
        }

        private void OnStepDragged(float v)
        {
            _dragging = true;
            Sim?.Change(s => { s.basePhase = v; s.playing = false; return s; }, dragging: true);
        }

        private void OnKnobDragged(float v)
        {
            if (Sim == null) return;
            _dragging = true;
            bool day = Sim.Working.IsDayCycle;
            Sim.Change(s => { if (day) s.basePhase = v; else s.value = v; return s; }, dragging: true);
        }

        private void OnSliderReleased()
        {
            _dragging = false;
            Sim?.Commit();
        }

        // ── Showing the state ──────────────────────────────────────────

        private void Refresh()
        {
            if (Sim == null) return;
            SimState s = Sim.State;

            SpaceSessionHUD.Instance?.SetSlotLabel(RailId, s.mode == SimMode.None ? "Simulate" : "Running");

            _chooser.SetActive(s.mode == SimMode.None);
            _assembly.SetActive(s.mode == SimMode.Assembly);
            _working.SetActive(s.mode == SimMode.Working);
            if (s.mode == SimMode.None) PanelNavigator.Remove(NavMode);

            _title.text = s.mode == SimMode.Assembly ? "Assembly" : s.mode == SimMode.Working ? "Working" : "Simulate";

            bool may = SimulationController.LocalPlayerMaySimulate;
            _stopBtn.SetActive(s.mode != SimMode.None && may);
            foreach (CanvasGroup g in new[] { _assemblyControls, _workingControls })
            {
                g.interactable = may;
                g.alpha = may ? 1f : 0.5f;
            }

            if (s.mode == SimMode.Assembly)
            {
                _setDirection(s.assemble ? 1 : 0);
                _aSpeed.text = $"{s.speed:0.#}×";
                _stepSlider.maxValue = Mathf.Max(1, Sim.Assembly.StepCount);
            }
            else if (s.mode == SimMode.Working)
            {
                bool day = Sim.Working.IsDayCycle;
                _wSpeed.text = $"{s.speed:0.#}×";
                _knobName.text = day ? "Time of day" : "Throttle";
                _knobIcon.sprite = AuraIcons.Get(day ? AuraIcons.Sun : AuraIcons.Speed);
                _knob.minValue = 0f;
                _knob.maxValue = day ? 24f : 1f;
                _airflowBtn.SetActive(Sim.Working.HasAirflow);
                _airflowPaint(Sim.Working.AirflowOn);
                _wCaption.text = day ? "The sun crosses the sky; light and shadows follow." : $"Moving: {Sim.Working.MovingNames}";
            }

            if (_workingSub != null && Sim.Working != null)
                _workingSub.text = Sim.Working.IsDayCycle ? "See a day pass over it" : "See it run, with airflow";

            string note = "";
            if (!may) note = $"{ControlOwnership.HolderName(Control.Simulation)} drives the simulation. Take back is in Parts.";
            else if (PhotonNetwork.InRoom && LiveSharing.IsLive(Control.Simulation)) note = "Shared live with the room.";
            _note.text = note;
            _note.gameObject.SetActive(note.Length > 0);

            Live();
        }

        /// <summary>What changes while it plays: the step, the play icon, the sliders, the status.</summary>
        private void Live()
        {
            if (Sim == null) return;
            SimState s = Sim.State;

            if (s.mode == SimMode.Assembly)
            {
                float pos = Sim.AssemblyPosition(s);
                int count = Sim.Assembly.StepCount;
                bool done = pos >= count;
                _aPlayIcon.sprite = AuraIcons.Get(done ? AuraIcons.Orbit : s.playing ? AuraIcons.Pause : AuraIcons.PlayFill);
                _stepText.text = StepSentence(Sim.Assembly.Caption(s.assemble, pos));
                if (!_dragging) _stepSlider.SetValueWithoutNotify(pos);
                int shown = Mathf.Clamp(Mathf.FloorToInt(pos) + (done ? 0 : 1), 1, Mathf.Max(1, count));
                _stepValue.text = $"{shown} / {count}";
                _status.text = done ? "DONE" : $"STEP {shown} / {count}";
            }
            else if (s.mode == SimMode.Working)
            {
                bool day = Sim.Working.IsDayCycle;
                _wPlayIcon.sprite = AuraIcons.Get(s.playing ? AuraIcons.Pause : AuraIcons.PlayFill);
                if (day)
                {
                    float hour = Sim.WorkingPhase(s);
                    if (!_dragging) _knob.SetValueWithoutNotify(hour);
                    _knobValue.text = WorkingPlayer.HourText(hour);
                }
                else
                {
                    if (!_dragging) _knob.SetValueWithoutNotify(s.value);
                    _knobValue.text = $"{Mathf.RoundToInt(s.value * 100f)}%";
                }
                _status.text = s.playing ? "RUNNING" : "PAUSED";
            }
            else _status.text = "";
        }

        /// <summary>"Step 3 of 11: remove the exhaust nozzle" → "Remove the exhaust nozzle".</summary>
        private static string StepSentence(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return "";
            int i = caption.IndexOf(':');
            string s = i >= 0 && i < caption.Length - 1 ? caption.Substring(i + 1).Trim() : caption.Trim();
            return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "SimulateCanvas", 97, out _);
            _sheetRt = AuraViewer.ToolSheet(safe, "SimulateSheet");
            _title = AuraViewer.SheetHeader(_sheetRt, "Simulate", out _status);

            // Stop: switches the running simulation off (the model goes back as it was).
            var header = (RectTransform)_title.transform.parent;
            AuraKit.Size(header, -1, 84f);
            AuraKit.Size(_status, 280f);
            AuraWidgets.Chip(header, "Stop", AuraIcons.Stop, false, OnBack, out Button stop, 84f);
            var stopText = stop.GetComponentInChildren<TextMeshProUGUI>();
            if (stopText != null) stopText.color = AuraTheme.Rose;
            foreach (var img in stop.GetComponentsInChildren<Image>()) if (img.name.StartsWith("Icon_")) img.color = AuraTheme.Rose;
            _stopBtn = stop.gameObject;
            _stopBtn.SetActive(false);

            BuildChooser();
            BuildAssembly();
            BuildWorking();

            _note = AuraKit.Text(_sheetRt, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _note.gameObject.SetActive(false);

            _sheet = AuraViewer.SlideUp(_sheetRt);
        }

        private void BuildChooser()
        {
            var page = AuraKit.Rect(_sheetRt, "Chooser");
            AuraKit.VStack(page, 18f);
            _chooser = page.gameObject;
            AuraKit.ListRow(page, AuraIcons.Parts, "Assembly animation", "Take it apart, step by step", OnAssemblyChosen, out _);
            AuraKit.ListRow(page, AuraIcons.Wind, "Working animation", "See it run, with airflow", OnWorkingChosen, out _workingSub);
        }

        private void BuildAssembly()
        {
            var page = AuraKit.Rect(_sheetRt, "Assembly");
            AuraKit.VStack(page, 20f);
            _assembly = page.gameObject;
            _assemblyControls = page.gameObject.AddComponent<CanvasGroup>();

            _setDirection = AuraWidgets.Segmented(page, new[] { "Take apart", "Put together" }, 0, i => OnDirection(i == 1));

            // The step card: press either half to step; hold to keep going.
            var card = AuraKit.Box(page, "StepCard", AuraTheme.Ink, AuraTheme.Radius, true);
            AuraKit.Border(card, AuraTheme.A(AuraTheme.Slate, 0.8f));
            AuraKit.Size(card, -1, 250f);
            var left = Zone(card.rectTransform, true);
            var right = Zone(card.rectTransform, false);
            var words = AuraKit.Rect(card.transform, "Words");
            AuraKit.Fill(words, 90, 24, 90, 24);
            AuraKit.VStack(words, 8f, 0, TextAnchor.MiddleCenter);
            _stepText = AuraKit.Text(words, "", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold, TextAlignmentOptions.Center);
            _stepText.enableAutoSizing = true;
            _stepText.fontSizeMin = 28f;
            _stepText.fontSizeMax = AuraTheme.SizeTitle - 8f;
            AuraKit.Size(_stepText, -1, 130f);
            AuraKit.Text(words, "Hold right to go forward · hold left to go back", AuraTheme.SizeCaption - 2f,
                         AuraTheme.TextDim, AuraWeight.Regular, TextAlignmentOptions.Center);
            var hold = card.gameObject.AddComponent<StepHold>();
            hold.left = left;
            hold.right = right;
            hold.onStep = OnStep;

            // Step scrubber.
            var stepRow = AuraKit.Rect(page, "StepRow");
            AuraKit.HStack(stepRow, 14f);
            AuraKit.Size(stepRow, -1, 92f);
            AuraKit.Caps(stepRow, "Step");
            _stepSlider = AuraWidgets.Slider(stepRow, 0f, OnStepDragged, OnSliderReleased, 0f, 1f, 92f);
            AuraKit.Flex(_stepSlider);
            _stepValue = AuraKit.Text(stepRow, "", AuraTheme.SizeSmall, AuraTheme.Lavender, AuraWeight.Bold, TextAlignmentOptions.MidlineRight);
            AuraKit.Size(_stepValue, 130f);

            // Transport.
            var t = AuraKit.Rect(page, "Transport");
            AuraKit.HStack(t, 22f, 0, TextAnchor.MiddleCenter);
            AuraKit.Size(t, -1, 140f);
            AuraWidgets.Chip(t, "1×", AuraIcons.Speed, false, OnSpeed, out Button speed);
            _aSpeed = speed.GetComponentInChildren<TextMeshProUGUI>();
            AuraWidgets.Round(t, AuraIcons.PlayFill, 136f, true, OnPlay, out _aPlayIcon);
            AuraWidgets.Chip(t, "Explain", AuraIcons.AI, true, OnAi, out _);
        }

        private Image Zone(RectTransform card, bool isLeft)
        {
            var z = AuraKit.Box(card, isLeft ? "Back" : "Forward", AuraTheme.A(AuraTheme.Lavender, 0.06f), AuraTheme.Radius);
            var rt = z.rectTransform;
            rt.anchorMin = new Vector2(isLeft ? 0f : 0.5f, 0f);
            rt.anchorMax = new Vector2(isLeft ? 0.5f : 1f, 1f);
            rt.offsetMin = new Vector2(6, 6); rt.offsetMax = new Vector2(-6, -6);
            z.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var ic = AuraKit.Icon(z.transform, isLeft ? AuraIcons.Back : AuraIcons.Forward, 54f, AuraTheme.A(AuraTheme.Lavender, 0.6f));
            AuraKit.Pin(ic.rectTransform, new Vector2(isLeft ? 0f : 1f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(isLeft ? 44f : -44f, 0f), new Vector2(54, 54));
            return z;
        }

        private void BuildWorking()
        {
            var page = AuraKit.Rect(_sheetRt, "Working");
            AuraKit.VStack(page, 18f);
            _working = page.gameObject;

            _wCaption = AuraKit.Text(page, "", AuraTheme.SizeSmall, AuraTheme.TextDim);
            _wCaption.enableWordWrapping = false;
            _wCaption.overflowMode = TextOverflowModes.Ellipsis;

            var controls = AuraKit.Rect(page, "Controls");
            AuraKit.VStack(controls, 12f);
            _workingControls = controls.gameObject.AddComponent<CanvasGroup>();

            var head = AuraKit.Rect(controls, "KnobHead");
            AuraKit.HStack(head, 12f);
            AuraKit.Size(head, -1, 50f);
            _knobIcon = AuraKit.Icon(head, AuraIcons.Speed, 40f, AuraTheme.Lavender);
            _knobName = AuraKit.Text(head, "Throttle", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            AuraKit.Flex(_knobName);
            _knobValue = AuraKit.Text(head, "", AuraTheme.SizeBody, AuraTheme.Lavender, AuraWeight.Bold, TextAlignmentOptions.MidlineRight);
            AuraKit.Size(_knobValue, 170f);
            _knob = AuraWidgets.Slider(controls, 0.5f, OnKnobDragged, OnSliderReleased, 0f, 1f, 92f);

            var t = AuraKit.Rect(page, "Transport");
            AuraKit.HStack(t, 22f, 0, TextAnchor.MiddleCenter);
            AuraKit.Size(t, -1, 140f);
            AuraWidgets.Chip(t, "1×", AuraIcons.Speed, false, OnSpeed, out Button speed);
            _wSpeed = speed.GetComponentInChildren<TextMeshProUGUI>();
            AuraWidgets.Round(t, AuraIcons.Pause, 136f, true, OnPlay, out _wPlayIcon);
            // Airflow is how this phone shows it, not part of the shared state: never locked.
            _airflowPaint = AuraWidgets.Chip(t, "Airflow", AuraIcons.Wind, true, OnAirflow, out Button air);
            _airflowBtn = air.gameObject;
            AuraWidgets.Chip(t, "Explain", AuraIcons.AI, true, OnAi, out _);
        }

        void LateUpdate()
        {
            // The step scrubber follows the same lock as the rest of the assembly controls.
            if (_stepSlider != null && _assemblyControls != null) _stepSlider.interactable = _assemblyControls.interactable;
        }
    }

    /// <summary>
    /// The assembly step card's press-and-hold: the right half steps forward, the left half back.
    /// One press = one step; holding repeats every 0.7 s until released. The pressed half lights up.
    /// </summary>
    public class StepHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Image left, right;
        public Action<int> onStep;
        public float repeat = 0.7f;

        private int _dir;
        private float _next;

        public void OnPointerDown(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local);
            _dir = local.x >= rt.rect.center.x ? 1 : -1;
            Light(true);
            onStep?.Invoke(_dir);
            _next = Time.unscaledTime + repeat;
        }

        public void OnPointerUp(PointerEventData e) { _dir = 0; Light(false); }
        public void OnPointerExit(PointerEventData e) { _dir = 0; Light(false); }
        void OnDisable() { _dir = 0; Light(false); }

        void Update()
        {
            if (_dir == 0 || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + repeat;
            onStep?.Invoke(_dir);
        }

        private void Light(bool on)
        {
            if (left != null) left.color = AuraTheme.A(AuraTheme.Lavender, on && _dir < 0 ? 0.22f : 0.06f);
            if (right != null) right.color = AuraTheme.A(AuraTheme.Lavender, on && _dir > 0 ? 0.22f : 0.06f);
        }
    }
}
