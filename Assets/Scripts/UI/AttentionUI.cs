using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.AI;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The FIND task (V3 Phase 8), restyled for the viewer UI:
    ///
    ///   Analysis → Find task (presenter in a room) opens a tool sheet along the bottom:
    ///
    ///       Find task                                        (x)
    ///       (<)          Fan rotor           (>)   [Random]
    ///       [Hide cones and pings during the task]
    ///       [               Start task                ]
    ///
    ///     then, while it runs, each person's result with End task (and New task once it is over).
    ///
    ///   The searchers' banner, top centre: "Find: Fan rotor / Tap it on the model · 0:07", then
    ///   "Found it in 4.2 s". It takes no touches.
    ///
    /// "See where others look" (the view cones) now lives in the Room card.
    /// </summary>
    public class AttentionUI : MonoBehaviour
    {
        public static AttentionUI Instance { get; private set; }

        private const string PanelId = "find";
        private const string NavId   = "find-task";
        private static readonly Color Found = new Color(0.44f, 0.76f, 0.63f);

        private AuraSlidePanel _sheet;
        private GameObject _setup, _live;
        private TextMeshProUGUI _partLabel, _liveTitle, _liveStatus, _liveFoot;
        private RectTransform _liveRows;
        private GameObject _endBtn, _newBtn;
        private System.Action<bool> _paintConesOff;
        private bool _conesOff;

        private CanvasGroup _banner;
        private Image _bannerBg, _bannerIcon;
        private TextMeshProUGUI _bannerTitle, _bannerSub;
        private bool _bannerShown;

        private float _nextText;
        private int _shownResults = -1;

        private readonly List<KeyValuePair<string, string>> _parts = new List<KeyValuePair<string, string>>(); // path, label
        private int _partIndex;

        public bool IsOpen => _sheet != null && _sheet.IsShown;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            FindTask.OnChanged += RefreshTask;
        }

        void OnDestroy()
        {
            FindTask.OnChanged -= RefreshTask;
            PanelNavigator.Remove(NavId);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (IsOpen && (!AttentionSync.ModelShown || !ModelPartSelector.IsPresenting)) ClosePanel();

            if (Time.unscaledTime >= _nextText)
            {
                _nextText = Time.unscaledTime + 0.2f;
                if (IsOpen && _live.activeSelf) RefreshLive();
                RefreshBanner();
            }
        }

        // ── Opening ────────────────────────────────────────────────────

        /// <summary>Analysis → Find task (presenter in a room): opens the sheet.</summary>
        public void OpenFindTask()
        {
            if (IsOpen) return;
            if (!ModelPartSelector.IsPresenting) { Toast.Show("Only the presenter can set a find task."); return; }
            ViewerPanels.Opened(PanelId, ClosePanel);
            BuildPartList();
            PanelNavigator.Push(NavId, this, ClosePanel);
            _shownResults = -1;
            RefreshTask();
            _sheet.Show();
        }

        /// <summary>Closes the sheet; a running task carries on (Analysis → Find task opens it again).</summary>
        public void ClosePanel()
        {
            if (!IsOpen) return;
            _sheet.Hide();
            ViewerPanels.Closed(PanelId);
            PanelNavigator.Remove(NavId);
        }

        // ── Setup ──────────────────────────────────────────────────────

        private void BuildPartList()
        {
            string current = _parts.Count > 0 ? _parts[Mathf.Clamp(_partIndex, 0, _parts.Count - 1)].Key : null;
            _parts.Clear();

            PartKnowledge pk = PartKnowledge.Instance;
            if (pk != null && pk.HasParts)
            {
                foreach (PartKnowledgeEntry e in pk.AllParts)
                    if (!string.IsNullOrEmpty(e.path))
                        _parts.Add(new KeyValuePair<string, string>(e.path, PartNames.Display(e.path, e.node)));
            }
            else
            {
                Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
                foreach (ModelPartCatalog.Entry e in ModelPartCatalog.Build(root, out _))
                    if (e.hasMesh) _parts.Add(new KeyValuePair<string, string>(e.path, PartNames.Display(e.path, e.name)));
            }
            _parts.Sort((a, b) => string.Compare(a.Value, b.Value, System.StringComparison.OrdinalIgnoreCase));

            _partIndex = 0;
            if (current != null)
                for (int i = 0; i < _parts.Count; i++) if (_parts[i].Key == current) { _partIndex = i; break; }
            RefreshPart();
        }

        private void StepPart(int delta)
        {
            if (_parts.Count == 0) return;
            _partIndex = (_partIndex + delta + _parts.Count) % _parts.Count;
            RefreshPart();
        }

        private void RandomPart()
        {
            if (_parts.Count < 2) return;
            int next;
            do next = Random.Range(0, _parts.Count); while (next == _partIndex);
            _partIndex = next;
            RefreshPart();
        }

        private void RefreshPart()
        {
            _partLabel.text = _parts.Count > 0 ? Esc(_parts[_partIndex].Value) : "No parts";
        }

        private void ToggleConesOff()
        {
            _conesOff = !_conesOff;
            _paintConesOff(_conesOff);
        }

        private void StartTask()
        {
            FindTask task = FindTask.Instance;
            if (task == null || _parts.Count == 0) return;
            if (FindTask.Searchers < 1) { Toast.Show("Nobody else is in the room to search."); return; }

            var part = _parts[_partIndex];
            if (!task.Begin(part.Key, part.Value, _conesOff)) Toast.Show("Could not start the task.");
        }

        // ── Live / results ─────────────────────────────────────────────

        private void RefreshTask()
        {
            FindTask task = FindTask.Instance;
            bool live = task != null && task.Hosting != null;
            _setup.SetActive(!live);
            _live.SetActive(live);
            _shownResults = -1;
            if (live) RefreshLive();
            RefreshBanner();
        }

        private void RefreshLive()
        {
            FindTask.Hosted t = FindTask.Instance != null ? FindTask.Instance.Hosting : null;
            if (t == null) return;

            _liveTitle.text = "Find: " + Esc(t.label);
            _liveStatus.text = t.running ? Clock(t.Elapsed) + (t.conesOff ? " · cones off" : "")
                                         : "Over";

            // Rows rebuild only when something changed (the clock alone does not).
            int sig = t.running ? 1 : 0;
            foreach (FindTask.Result r in t.results) sig = sig * 31 + (r.found ? 3 : r.done ? 2 : 1) + r.wrongTaps * 7;
            if (sig != _shownResults)
            {
                _shownResults = sig;
                AuraKit.ClearChildren(_liveRows);
                if (t.results.Count == 0) ResultRow("Waiting for the others…", "", AuraTheme.TextDim);
                foreach (FindTask.Result r in t.results)
                {
                    string value; Color c;
                    if (r.found) { value = $"{r.seconds:0.0} s"; c = Found; }
                    else if (r.done) { value = string.IsNullOrEmpty(r.note) ? "Not found" : $"Not found ({r.note})"; c = AuraTheme.Rose; }
                    else { value = "Searching…"; c = AuraTheme.TextDim; }
                    if (r.wrongTaps > 0) value += $"  ·  {r.wrongTaps} wrong";
                    ResultRow(Esc(r.name), value, c);
                }
            }

            _liveFoot.text = t.running ? "" : (t.pings > 0 ? $"Pings during the task: {t.pings}. Saved to Reports." : "No pings during the task. Saved to Reports.");
            _liveFoot.gameObject.SetActive(!t.running);
            _endBtn.SetActive(t.running);
            _newBtn.SetActive(!t.running);
        }

        private void ResultRow(string name, string value, Color valueColor)
        {
            var row = AuraKit.Box(_liveRows, "Result", AuraTheme.Ink, AuraTheme.Radius - 10f);
            AuraKit.Border(row, AuraTheme.A(AuraTheme.Slate, 0.7f), AuraTheme.Radius - 10f);
            var h = AuraKit.HStack(row, 16f, 0, TextAnchor.MiddleLeft);
            h.padding = new RectOffset(28, 28, 0, 0);
            AuraKit.Size(row, -1, 92f);
            var n = AuraKit.Text(row.transform, name, AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.SemiBold);
            n.enableWordWrapping = false;
            n.overflowMode = TextOverflowModes.Ellipsis;
            AuraKit.Flex(n);
            var v = AuraKit.Text(row.transform, value, AuraTheme.SizeSmall, valueColor, AuraWeight.Bold, TextAlignmentOptions.MidlineRight);
            v.enableWordWrapping = false;
        }

        // ── Banner (searchers) ─────────────────────────────────────────

        private void RefreshBanner()
        {
            FindTask.Assigned a = FindTask.Instance != null ? FindTask.Instance.Searching : null;

            // Gone a few seconds after the task is over.
            if (a != null && a.ended && Time.unscaledTime - a.endedAt > 4f)
            {
                FindTask.Instance.DismissSearch();
                a = null;
            }
            ShowBanner(a != null);
            if (a == null) return;

            _bannerTitle.text = "Find: " + Esc(a.label);
            bool found = a.found;
            _bannerBg.color = found ? AuraTheme.A(Color.Lerp(AuraTheme.Indigo, Found, 0.35f), 0.97f) : AuraTheme.A(AuraTheme.Indigo, 0.97f);
            _bannerIcon.sprite = AuraIcons.Get(found ? AuraIcons.Ok : AuraIcons.Search);
            _bannerIcon.color = found ? Found : AuraTheme.Lavender;
            if (found)
                _bannerSub.text = a.ended ? $"Found it in {a.foundSeconds:0.0} s." : $"Found it in {a.foundSeconds:0.0} s. Waiting for the others…";
            else if (a.ended)
                _bannerSub.text = "The task is over.";
            else
                _bannerSub.text = $"Tap it on the model  ·  {Clock(a.Elapsed)}" + (a.wrongTaps > 0 ? $"  ·  {a.wrongTaps} wrong" : "");
        }

        private void ShowBanner(bool show)
        {
            if (show == _bannerShown) return;
            _bannerShown = show;
            if (show) _banner.gameObject.SetActive(true);
            AuraMotion.Fade(_banner, show ? 1f : 0f, 0.2f, show ? (System.Action)null : () => _banner.gameObject.SetActive(false));
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "FindCanvas", 99, out _);
            BuildSheet(safe);
            BuildBanner(safe);
        }

        private void BuildSheet(RectTransform safe)
        {
            var sheet = AuraViewer.ToolSheet(safe, "FindSheet");
            sheet.gameObject.AddComponent<TouchBlocker>(); // the model must not turn under the sheet

            // Setup page
            var setup = AuraKit.Rect(sheet, "Setup");
            AuraKit.VStack(setup, 20f);
            _setup = setup.gameObject;
            Header(setup, "Find task", out _);

            var pick = AuraKit.Rect(setup, "Pick");
            AuraKit.HStack(pick, 14f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(pick, -1, 104f);
            AuraWidgets.Round(pick, AuraIcons.Prev, 96f, false, () => StepPart(-1), out _);
            _partLabel = AuraKit.Text(pick, "", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.Bold, TextAlignmentOptions.Center);
            _partLabel.enableAutoSizing = true;
            _partLabel.fontSizeMin = 22f;
            _partLabel.fontSizeMax = AuraTheme.SizeBody;
            AuraKit.Flex(_partLabel);
            AuraWidgets.Round(pick, AuraIcons.Next, 96f, false, () => StepPart(1), out _);
            AuraWidgets.Chip(pick, "Random", AuraIcons.Puzzle, false, RandomPart, out _, 96f);

            var opt = AuraKit.Rect(setup, "Options");
            AuraKit.HStack(opt, 12f);
            AuraKit.Size(opt, -1, 92f);
            _paintConesOff = AuraWidgets.Chip(opt, "Hide cones and pings", AuraIcons.EyeOff, false, ToggleConesOff, out _, 92f);
            AuraKit.Flex(AuraKit.Rect(opt, "Space"));

            var start = AuraKit.Pill(setup, "Start task", AuraIcons.Target, AuraTheme.Lavender, AuraTheme.Ink, StartTask, AuraTheme.MinTouch);
            AuraKit.Size(start, -1, AuraTheme.MinTouch);

            AuraKit.Text(setup, "Everyone else is asked to tap this part on their model, and each time is logged.",
                         AuraTheme.SizeCaption, AuraTheme.TextDim);

            // Live / results page
            var live = AuraKit.Rect(sheet, "Live");
            AuraKit.VStack(live, 16f);
            _live = live.gameObject;
            _liveTitle = Header(live, "", out _liveStatus);
            _liveTitle.overflowMode = TextOverflowModes.Ellipsis;

            _liveRows = AuraKit.Rect(live, "Results");
            AuraKit.VStack(_liveRows, 10f);
            _liveFoot = AuraKit.Text(live, "", AuraTheme.SizeCaption, AuraTheme.TextDim);

            var btns = AuraKit.EqualRow(live, AuraTheme.MinTouch, 16f);
            var end = AuraKit.Pill(btns, "End task", AuraIcons.Stop, AuraTheme.A(AuraTheme.Rose, 0.2f), AuraTheme.Rose,
                                   () => FindTask.Instance?.End("ended"), AuraTheme.MinTouch, AuraTheme.Rose);
            AuraKit.Cell(end);
            _endBtn = end.gameObject;
            var again = AuraKit.Pill(btns, "New task", AuraIcons.Plus, AuraTheme.Lavender, AuraTheme.Ink,
                                     () => FindTask.Instance?.ClearFinished(), AuraTheme.MinTouch);
            AuraKit.Cell(again);
            _newBtn = again.gameObject;

            _live.SetActive(false);
            _sheet = AuraViewer.SlideUp(sheet);
        }

        /// <summary>Title on the left, a lavender status, and a round close button.</summary>
        private TextMeshProUGUI Header(RectTransform parent, string title, out TextMeshProUGUI status)
        {
            var row = AuraKit.Rect(parent, "Header");
            AuraKit.HStack(row, 14f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(row, -1, 92f);
            var t = AuraKit.Text(row, title, AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            t.enableWordWrapping = false;
            AuraKit.Flex(t);
            status = AuraKit.Caps(row, "", AuraTheme.Lavender);
            status.alignment = TextAlignmentOptions.MidlineRight;
            status.enableWordWrapping = false;
            AuraWidgets.Round(row, AuraIcons.Close, 84f, false, ClosePanel, out _);
            return t;
        }

        /// <summary>True while a searcher's banner is on screen (the quality pill steps aside).</summary>
        public bool BannerShown => _bannerShown;

        /// <summary>Top centre, under the top buttons; never takes a touch.</summary>
        private void BuildBanner(RectTransform safe)
        {
            _bannerBg = AuraKit.Box(safe, "FindBanner", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 6f);
            _bannerBg.raycastTarget = false;
            AuraKit.Border(_bannerBg, AuraTheme.Lavender, AuraTheme.Radius + 6f).raycastTarget = false;
            var rt = _bannerBg.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            // Under the model name and the part chip (the corners hold the top buttons).
            float side = AuraViewer.Gutter + 60f, top = AuraViewer.TopReserve + 110f;
            rt.offsetMin = new Vector2(side, -top - 200f);
            rt.offsetMax = new Vector2(-side, -top);
            var h = AuraKit.HStack(_bannerBg, 22f, 0, TextAnchor.MiddleLeft);
            h.padding = new RectOffset(30, 30, 22, 24);
            AuraKit.FitHeight(_bannerBg);

            _bannerIcon = AuraKit.Icon(_bannerBg.transform, AuraIcons.Search, 64f, AuraTheme.Lavender);
            var words = AuraKit.Rect(_bannerBg.transform, "Words");
            AuraKit.VStack(words, 4f);
            AuraKit.Flex(words);
            _bannerTitle = AuraKit.Text(words, "", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.Bold);
            _bannerTitle.enableWordWrapping = true;
            _bannerSub = AuraKit.Text(words, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _bannerSub.enableWordWrapping = true;
            foreach (var g in _bannerBg.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;

            _banner = _bannerBg.gameObject.AddComponent<CanvasGroup>();
            _banner.blocksRaycasts = false;
            _banner.alpha = 0f;
            _bannerBg.gameObject.SetActive(false);
        }
    }
}
