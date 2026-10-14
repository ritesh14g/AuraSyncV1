using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.Simulation;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The SIMULATE panel (V3 Phase 7), above the bottom buttons:
    ///
    ///   Chooser    [ASSEMBLY ANIMATION]  [WORKING ANIMATION]  [X]
    ///   Assembly   [TAKE APART|PUT TOGETHER]  [1x]  [AI]  [BACK]
    ///              [|<] [PLAY] [>|]  "Step 3 of 11: remove the exhaust nozzle"
    ///              STEP  0 ━━━━━●──────── 11
    ///   Working    [PLAY] [1x] [AIRFLOW] [AI] [BACK]
    ///              THROTTLE 0 ━━━━●────── 100     (machines)
    ///              TIME     0 ━━━━●────── 24      (the House: the sun's position)
    ///
    /// BACK on a mode stops it and returns to the chooser; Back (or X) on the chooser closes the
    /// panel. Opening PARTS, NOTES or SECTION hides the panel without stopping anything, so the cut
    /// or the drawer can be used while the simulation runs; SIMULATE brings the panel back on the
    /// running mode.
    /// </summary>
    public class SimulationPanelUI : MonoBehaviour
    {
        public static SimulationPanelUI Instance { get; private set; }

        private const string RailId = "simulate";
        private const string NavPanel = "sim", NavMode = "sim-mode";

        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.96f);
        private static readonly Color OnColor     = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color OffColor    = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color PlayColor   = new Color(0.16f, 0.55f, 0.34f, 1f);
        private static readonly Color BackColor   = new Color(0.45f, 0.14f, 0.14f, 1f);

        private GameObject _panel, _chooser, _assembly, _working;
        private CanvasGroup _assemblyControls, _workingControls;

        // Assembly page
        private Image _apartImg, _togetherImg;
        private TextMeshProUGUI _aSpeed, _aPlay, _caption, _stepValue;
        private Slider _stepSlider;

        // Working page
        private TextMeshProUGUI _wSpeed, _wPlay, _wCaption, _knobValue, _knobName, _minEnd, _maxEnd;
        private Image _airflowImg;
        private GameObject _airflowBtn;
        private Slider _knob;

        private TextMeshProUGUI _status;
        private bool _dragging;
        private bool _railVisible;

        private SimulationController Sim => SimulationController.Instance;

        // ── Lifecycle ──────────────────────────────────────────────────────────

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
            SpaceSessionHUD.Instance.AddRailButton(RailId, "SIMULATE", Toggle);
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

            if (_panel.activeSelf) Live();
        }

        // ── Opening and closing ────────────────────────────────────────────────

        private void Toggle()
        {
            if (_panel.activeSelf) Hide();
            else Show();
        }

        private void Show()
        {
            ModelPartsDrawerUI.Instance?.SetOpen(false);
            PartNotesPanelUI.Instance?.CloseList();
            SectionCutUI.Instance?.CloseToolbar();
            _panel.SetActive(true);
            PanelNavigator.Push(NavPanel, this, Hide);
            if (Sim != null && Sim.State.mode != SimMode.None) PanelNavigator.Push(NavMode, this, OnBack);
            Refresh();
        }

        /// <summary>Hides the panel; whatever is running keeps running.</summary>
        public void Hide()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
            PanelNavigator.Remove(NavMode);
            PanelNavigator.Remove(NavPanel);
        }

        // ── Actions ────────────────────────────────────────────────────────────

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

        /// <summary>BACK on a mode: stop it (the model goes back exactly as it was) and show the chooser.</summary>
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
                if (done) { s.basePhase = 0f; s.playing = true; } // replay from the start
                else s.playing = !s.playing;
                return s;
            });
        }

        private void OnSpeed()
        {
            Sim?.Change(s =>
            {
                int i = System.Array.IndexOf(SimulationController.Speeds, s.speed);
                s.speed = SimulationController.Speeds[(i + 1) % SimulationController.Speeds.Length];
                return s;
            });
        }

        private void OnStep(int delta)
        {
            Sim?.Change(s =>
            {
                float pos = s.basePhase; // already brought up to now
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

            // Working: the selected part, or the whole model.
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

        // ── Showing the state ──────────────────────────────────────────────────

        private void Refresh()
        {
            if (Sim == null) return;
            SimState s = Sim.State;

            if (SpaceSessionHUD.Instance != null)
                SpaceSessionHUD.Instance.SetSlotLabel(RailId, s.mode == SimMode.None ? "SIMULATE" : "SIMULATE\n(on)");

            _chooser.SetActive(s.mode == SimMode.None);
            _assembly.SetActive(s.mode == SimMode.Assembly);
            _working.SetActive(s.mode == SimMode.Working);
            if (s.mode == SimMode.None) PanelNavigator.Remove(NavMode);

            bool may = SimulationController.LocalPlayerMaySimulate;
            foreach (CanvasGroup g in new[] { _assemblyControls, _workingControls })
            {
                g.interactable = may;
                g.alpha = may ? 1f : 0.5f;
            }

            if (s.mode == SimMode.Assembly)
            {
                _apartImg.color = s.assemble ? OffColor : OnColor;
                _togetherImg.color = s.assemble ? OnColor : OffColor;
                _aSpeed.text = $"{s.speed:0.#}x";
                _stepSlider.maxValue = Mathf.Max(1, Sim.Assembly.StepCount);
                _maxStepEnd.text = Sim.Assembly.StepCount.ToString();
            }
            else if (s.mode == SimMode.Working)
            {
                bool day = Sim.Working.IsDayCycle;
                _wSpeed.text = $"{s.speed:0.#}x";
                _knobName.text = day ? "TIME" : "THROTTLE";
                _knob.minValue = 0f;
                _knob.maxValue = day ? 24f : 1f;
                _minEnd.text = day ? "0" : "0";
                _maxEnd.text = day ? "24" : "100";
                _airflowBtn.SetActive(Sim.Working.HasAirflow);
                _airflowImg.color = Sim.Working.AirflowOn ? OnColor : OffColor;
                _wCaption.text = day ? "Time of day: the sun crosses the sky." : $"Moving: {Sim.Working.MovingNames}";
            }

            string status = "";
            if (!may) status = $"{ControlOwnership.HolderName(Control.Simulation)} drives the simulation. TAKE BACK is in the Parts drawer.";
            else if (PhotonNetwork.InRoom && LiveSharing.IsLive(Control.Simulation)) status = "Shared live with the room.";
            _status.text = status;
            _status.gameObject.SetActive(status.Length > 0);

            Live();
        }

        /// <summary>Things that change while it plays: the caption, the play button, the sliders.</summary>
        private void Live()
        {
            if (Sim == null) return;
            SimState s = Sim.State;

            if (s.mode == SimMode.Assembly)
            {
                float pos = Sim.AssemblyPosition(s);
                bool done = pos >= Sim.Assembly.StepCount;
                _aPlay.text = done ? "REPLAY" : s.playing ? "PAUSE" : "PLAY";
                _caption.text = Sim.Assembly.Caption(s.assemble, pos);
                if (!_dragging) _stepSlider.SetValueWithoutNotify(pos);
                _stepValue.text = Mathf.FloorToInt(pos).ToString();
            }
            else if (s.mode == SimMode.Working)
            {
                bool day = Sim.Working.IsDayCycle;
                _wPlay.text = s.playing ? "PAUSE" : "PLAY";
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
            }
        }

        // ── Building ───────────────────────────────────────────────────────────

        private TextMeshProUGUI _maxStepEnd;

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

            _panel = new GameObject("SimulationPanel", typeof(RectTransform));
            _panel.transform.SetParent(transform, false);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = new Vector2(0.16f, 0.215f);
            rt.anchorMax = new Vector2(0.98f, 0.215f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _panel.AddComponent<Image>().color = PanelColor;
            _panel.AddComponent<TouchBlocker>();
            var vg = _panel.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(12, 12, 10, 10);
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            _panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildChooser();
            BuildAssembly();
            BuildWorking();

            _status = Text(_panel.transform, "", 18f, new Color(1f, 1f, 1f, 0.65f));
            _status.enableWordWrapping = true;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.minHeight = sle.preferredHeight = 30f;

            _panel.SetActive(false);
        }

        private void BuildChooser()
        {
            _chooser = Page("Chooser");
            var row = Row(_chooser.transform, 64f);
            Button(row.transform, "ASSEMBLY\nANIMATION", OnColor, OnAssemblyChosen, 0f, flex: true);
            Button(row.transform, "WORKING\nANIMATION", OnColor, OnWorkingChosen, 0f, flex: true);
            Button(row.transform, "X", BackColor, Hide, 60f);
        }

        private void BuildAssembly()
        {
            _assembly = Page("Assembly");

            var top = Row(_assembly.transform, 56f);
            var g1 = Group(top.transform, out _assemblyControls);
            _apartImg = Button(g1.transform, "TAKE APART", OnColor, () => OnDirection(false), 150f).GetComponent<Image>();
            _togetherImg = Button(g1.transform, "PUT TOGETHER", OffColor, () => OnDirection(true), 170f).GetComponent<Image>();
            _aSpeed = Button(g1.transform, "1x", OffColor, OnSpeed, 70f).GetComponentInChildren<TextMeshProUGUI>();
            Button(top.transform, "AI", OnColor, OnAi, 70f);
            Button(top.transform, "BACK", BackColor, OnBack, 90f);

            var mid = Row(_assembly.transform, 56f);
            var g2 = Group(mid.transform, out CanvasGroup midGroup);
            g2.GetComponent<LayoutElement>().flexibleWidth = 0f;
            Button(g2.transform, "|<", OffColor, () => OnStep(-1), 64f);
            _aPlay = Button(g2.transform, "PLAY", PlayColor, OnPlay, 120f).GetComponentInChildren<TextMeshProUGUI>();
            Button(g2.transform, ">|", OffColor, () => OnStep(1), 64f);
            _caption = Text(mid.transform, "", 19f, Color.white);
            _caption.enableWordWrapping = true;
            _caption.enableAutoSizing = true; _caption.fontSizeMin = 13f; _caption.fontSizeMax = 19f;
            _caption.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            _assemblyMid = midGroup;

            var steps = Row(_assembly.transform, SleekSlider.RowHeight);
            steps.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            NameLabel(steps.transform, "STEP");
            _stepSlider = SleekSlider.Create(steps.transform, 0f, 1f, "0", "1", out _stepValue);
            _maxStepEnd = _stepSlider.transform.parent.GetChild(_stepSlider.transform.parent.childCount - 1).GetComponent<TextMeshProUGUI>();
            _stepSlider.onValueChanged.AddListener(OnStepDragged);
            _stepSlider.gameObject.AddComponent<PointerUpRelay>().Released += OnSliderReleased;
        }

        private CanvasGroup _assemblyMid, _workingKnob;

        private void BuildWorking()
        {
            _working = Page("Working");

            var top = Row(_working.transform, 56f);
            var g = Group(top.transform, out _workingControls);
            _wPlay = Button(g.transform, "PAUSE", PlayColor, OnPlay, 120f).GetComponentInChildren<TextMeshProUGUI>();
            _wSpeed = Button(g.transform, "1x", OffColor, OnSpeed, 70f).GetComponentInChildren<TextMeshProUGUI>();
            // AIRFLOW is how this phone shows it, not part of the shared state: never locked.
            _airflowBtn = Button(top.transform, "AIRFLOW", OnColor, OnAirflow, 120f);
            _airflowImg = _airflowBtn.GetComponent<Image>();
            Button(top.transform, "AI", OnColor, OnAi, 70f);
            Button(top.transform, "BACK", BackColor, OnBack, 90f);

            _wCaption = Text(_working.transform, "", 18f, new Color(1f, 1f, 1f, 0.8f));
            _wCaption.enableWordWrapping = false;
            _wCaption.overflowMode = TextOverflowModes.Ellipsis;
            var cle = _wCaption.gameObject.AddComponent<LayoutElement>();
            cle.minHeight = cle.preferredHeight = 28f;

            var knobRow = Row(_working.transform, SleekSlider.RowHeight);
            knobRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            _workingKnob = knobRow.AddComponent<CanvasGroup>();
            _knobName = NameLabel(knobRow.transform, "THROTTLE");
            _knob = SleekSlider.Create(knobRow.transform, 0f, 1f, "0", "100", out _knobValue);
            Transform group = _knob.transform.parent;
            _minEnd = group.GetChild(0).GetComponent<TextMeshProUGUI>();
            _maxEnd = group.GetChild(group.childCount - 1).GetComponent<TextMeshProUGUI>();
            _knob.onValueChanged.AddListener(OnKnobDragged);
            _knob.gameObject.AddComponent<PointerUpRelay>().Released += OnSliderReleased;
        }

        void LateUpdate()
        {
            // Rows outside the main groups follow the same lock.
            if (_assemblyControls == null) return;
            foreach (CanvasGroup g in new[] { _assemblyMid, _workingKnob })
            {
                if (g == null) continue;
                g.interactable = _assemblyControls.interactable;
                g.alpha = _assemblyControls.alpha;
            }
            if (_stepSlider != null) _stepSlider.interactable = _assemblyControls.interactable;
        }

        private GameObject Page(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_panel.transform, false);
            var vg = go.AddComponent<VerticalLayoutGroup>();
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            go.SetActive(false);
            return go;
        }

        private static GameObject Group(Transform parent, out CanvasGroup canvasGroup)
        {
            var go = new GameObject("Controls", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 8;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false;
            canvasGroup = go.AddComponent<CanvasGroup>();
            return go;
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

        private static TextMeshProUGUI NameLabel(Transform parent, string name)
        {
            TextMeshProUGUI label = Text(parent, name, 18f, new Color(1f, 1f, 1f, 0.85f));
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.margin = new Vector4(0f, 0f, 0f, SleekSlider.RowHeight * 0.30f - 12f);
            label.enableAutoSizing = true; label.fontSizeMin = 12f; label.fontSizeMax = 18f;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.minWidth = 96f;
            return label;
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

        private static GameObject Button(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick,
                                         float width, bool flex = false)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            if (flex) le.flexibleWidth = 1f;
            var img = go.AddComponent<Image>();
            img.color = color;
            var b = go.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Text(go.transform, label, 20f, Color.white);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4f, 0f); trt.offsetMax = new Vector2(-4f, 0f);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true; t.fontSizeMin = 12f; t.fontSizeMax = 20f;
            return go;
        }
    }
}
