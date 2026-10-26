using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.AI;
using AuraSync.Quiz;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Analysis → Ask quiz, the teacher's side (V3 Phase 11). A bottom sheet, as tall as what it
    /// shows (it scrolls once it passes about half the screen), in four steps:
    ///
    ///   SETUP     about the whole model or the selected part; 5 / 10 / 15 questions; easy /
    ///             medium / hard; time limit none / 2 / 5 / 10 min. [Generate]
    ///   WORKING   while the AI writes; Back cancels.
    ///   REVIEW    every question with its options (the right one marked): Edit, New one (ask the
    ///             AI for a replacement), Delete; Start over (top). [Send to N]
    ///   LIVE      the clock and each student's progress. [End quiz]. Then the class average and
    ///             each score; New quiz (top). [Open report]
    ///
    /// The main action is the wide button at the bottom of the sheet. The sheet can be closed while
    /// a quiz runs (Back or ✕); Ask quiz opens it on the live step.
    /// </summary>
    public class QuizTeacherUI : MonoBehaviour
    {
        public static QuizTeacherUI Instance { get; private set; }

        private const string PanelId = "quiz";
        private const string NavId = "quiz-teacher";
        private const string EditNavId = "quiz-edit";

        private static readonly int[] Counts = { 5, 10, 15 };
        private static readonly string[] Difficulties = { "easy", "medium", "hard" };
        private static readonly int[] Limits = { 0, 120, 300, 600 };

        private AuraSlidePanel _sheet;
        private TextMeshProUGUI _title, _subtitle;
        private ScrollRect _scroll;
        private Button _primary;
        private Image _primaryFill;
        private TextMeshProUGUI _primaryLabel;

        private GameObject _setup, _working, _review, _live;
        private TextMeshProUGUI _status, _workingText, _reviewHead, _liveText;
        private RectTransform _reviewRows, _liveRows;
        private System.Action<int> _setScope, _setCount, _setDiff, _setLimit;
        private GameObject _scopeRow;
        private TextMeshProUGUI _scopeNote;
        private GameObject _startOver, _newQuiz;

        private string _scopePath = "";
        private int _count = 1, _diff = 1, _limit = 0; // indices
        private AiClient.Pending _pending;

        private class Draft { public QuizQuestion q; public bool edited, regenerated, busy; }
        private readonly List<Draft> _drafts = new List<Draft>();
        private int _generated, _deleted;

        // Edit modal
        private GameObject _editModal;
        private TMP_InputField _editQ;
        private readonly TMP_InputField[] _editO = new TMP_InputField[4];
        private System.Action<int> _setRight;
        private int _editIndex, _editAnswer;
        private float _nextLive;
        private string _liveSig;

        private string _selectedPath = "";

        public bool IsOpen => _sheet != null && _sheet.IsShown;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            QuizSession.OnChanged += RefreshPage;
        }

        void OnDestroy()
        {
            QuizSession.OnChanged -= RefreshPage;
            _pending?.Cancel();
            PanelNavigator.Remove(NavId);
            PanelNavigator.Remove(EditNavId);
            if (Instance == this) Instance = null;
        }

        public void Open()
        {
            if (!QuizSession.LocalIsTeacher) { Toast.Show("The AI quiz needs a room: you send it to the students in it."); return; }
            if (!IsOpen)
            {
                ViewerPanels.Opened(PanelId, Close);
                PanelNavigator.Push(NavId, this, OnBack);
            }

            ModelPartSelector sel = ModelPartSelector.Instance;
            string selected = sel != null ? sel.OwnPath : "";
            _selectedPath = selected;
            _scopeNote.text = string.IsNullOrEmpty(selected) ? "Select a part on the model to quiz on just that part."
                                                             : "Selected part: " + Esc(QuizGenerator.ScopeLabel(selected));
            if (string.IsNullOrEmpty(selected) || (_scopePath.Length > 0 && _scopePath != selected)) _scopePath = "";
            RefreshPage();
            _sheet.Show();
        }

        public void Close()
        {
            if (!IsOpen) return;
            _sheet.Hide();
            ViewerPanels.Closed(PanelId);
            PanelNavigator.Remove(NavId);
        }

        /// <summary>Back: cancels writing, otherwise closes (a running quiz carries on).</summary>
        private void OnBack()
        {
            if (_pending != null) { CancelGenerate(); PanelNavigator.Push(NavId, this, OnBack); return; }
            Close();
        }

        void Update()
        {
            if (!IsOpen) return;
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) { Close(); return; }
            if (_live.activeSelf && Time.unscaledTime >= _nextLive) { _nextLive = Time.unscaledTime + 0.5f; RefreshLive(); }
        }

        // ── Pages ──────────────────────────────────────────────────────

        private void ShowPage(GameObject page)
        {
            bool changed = !page.activeSelf;
            foreach (GameObject p in new[] { _setup, _working, _review, _live }) p.SetActive(p == page);
            _startOver.SetActive(page == _review);
            _newQuiz.SetActive(false);
            if (changed) { _liveSig = null; if (_scroll != null) _scroll.verticalNormalizedPosition = 1f; }
        }

        private void RefreshPage()
        {
            if (_sheet == null) return;
            QuizSession s = QuizSession.Instance;
            if (s != null && s.Hosting != null) { ShowPage(_live); RefreshLive(); return; }
            if (_pending != null)
            {
                ShowPage(_working);
                SetHead("Writing the quiz", "");
                SetPrimary(null, null);
                return;
            }
            if (_drafts.Count > 0) { ShowPage(_review); RenderReview(); return; }
            ShowPage(_setup);
            RefreshSetup();
        }

        private void SetHead(string title, string sub)
        {
            _title.text = title;
            _subtitle.text = sub;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(sub));
        }

        /// <summary>The wide button at the bottom of the sheet (null hides it).</summary>
        private void SetPrimary(string label, System.Action onTap, bool enabled = true, bool danger = false)
        {
            _primary.onClick.RemoveAllListeners();
            if (string.IsNullOrEmpty(label)) { _primary.gameObject.SetActive(false); return; }
            _primary.gameObject.SetActive(true);
            _primaryLabel.text = label;
            _primary.interactable = enabled;
            _primaryFill.color = !enabled ? AuraTheme.A(AuraTheme.Slate, 0.45f) : danger ? AuraTheme.Rose : AuraTheme.Lavender;
            if (onTap != null) _primary.onClick.AddListener(() => onTap());
        }

        private void RefreshSetup()
        {
            SetHead("AI quiz", "Multiple-choice questions from the model's part facts");
            _setScope(_scopePath.Length == 0 ? 0 : 1);
            _scopeRow.SetActive(!string.IsNullOrEmpty(_selectedPath));
            _setCount(_count);
            _setDiff(_diff);
            _setLimit(_limit);
            SetPrimary("Generate", Generate);
        }

        // ── Generating ─────────────────────────────────────────────────

        private void Generate()
        {
            if (!AiClient.Ensure().IsConfigured) { _status.text = "The AI server is not set up on this build."; return; }
            int n = Counts[_count];
            _workingText.text = $"Writing {n} questions about {Esc(QuizGenerator.ScopeLabel(_scopePath))}…\nThis takes up to a minute. Back cancels.";
            _status.text = "";
            _pending = QuizGenerator.Generate(_scopePath, n, Difficulties[_diff], r =>
            {
                _pending = null;
                if (!r.ok) { _status.text = "Could not write the quiz: " + r.error; RefreshPage(); return; }
                _drafts.Clear();
                foreach (QuizQuestion q in r.questions) _drafts.Add(new Draft { q = q });
                _generated = _drafts.Count;
                _deleted = 0;
                if (_drafts.Count < n) Toast.Show($"The AI wrote {_drafts.Count} usable question(s) of {n}.");
                RefreshPage();
            });
            RefreshPage();
        }

        private void CancelGenerate()
        {
            _pending?.Cancel();
            _pending = null;
            RefreshPage();
        }

        // ── Review ─────────────────────────────────────────────────────

        private void RenderReview()
        {
            SetHead("Review", "");
            AuraKit.ClearChildren(_reviewRows);
            _reviewHead.text = $"{_drafts.Count} question(s) about {Esc(QuizGenerator.ScopeLabel(_scopePath))}, {Difficulties[_diff]}" +
                               (Limits[_limit] > 0 ? $", {Limits[_limit] / 60} min" : ", no time limit") + ". Check them, then send.";

            for (int i = 0; i < _drafts.Count; i++)
            {
                int idx = i;
                Draft d = _drafts[i];
                var card = AuraKit.Box(_reviewRows, "Question", AuraTheme.Ink, AuraTheme.Radius - 6f);
                AuraKit.Border(card, AuraTheme.A(AuraTheme.Slate, 0.8f), AuraTheme.Radius - 6f);
                var v = AuraKit.VStack(card, 10f);
                v.padding = new RectOffset(26, 26, 20, 20);

                var sb = new StringBuilder();
                sb.Append($"<b>{i + 1}. {Esc(d.q.question)}</b>");
                if (d.edited) sb.Append("  <color=#9894C4>(edited)</color>");
                if (d.regenerated) sb.Append("  <color=#9894C4>(new)</color>");
                for (int k = 0; k < d.q.options.Length; k++)
                    sb.Append(k == d.q.answer
                        ? $"\n<color=#6FC2A0>{(char)('A' + k)}. {Esc(d.q.options[k])}   ✓</color>"
                        : $"\n<color=#A9A8CC>{(char)('A' + k)}.</color> {Esc(d.q.options[k])}");
                string part = string.IsNullOrEmpty(d.q.partPath) ? "Whole model" : QuizGenerator.ScopeLabel(d.q.partPath);
                sb.Append($"\n<size=85%><color=#A9A8CC>Part: {Esc(part)}</color></size>");
                var t = AuraKit.Text(card.transform, sb.ToString(), AuraTheme.SizeSmall, AuraTheme.Text);
                t.enableWordWrapping = true;
                t.richText = true;

                var btns = AuraKit.Rect(card.transform, "Buttons");
                AuraKit.HStack(btns, 12f).childForceExpandHeight = true;
                AuraKit.Size(btns, -1, 84f);
                AuraWidgets.Chip(btns, "Edit", AuraIcons.Edit, false, () => OpenEdit(idx), out _, 84f);
                AuraWidgets.Chip(btns, d.busy ? "Writing…" : "New one", AuraIcons.AI, true, () => Regenerate(idx), out _, 84f);
                AuraKit.Flex(AuraKit.Rect(btns, "Space"));
                AuraWidgets.Chip(btns, "Delete", AuraIcons.Trash, false, () => DeleteDraft(idx), out Button del, 84f);
                var dt = del.GetComponentInChildren<TextMeshProUGUI>();
                if (dt != null) dt.color = AuraTheme.Rose;
            }

            int students = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount - 1 : 0;
            bool can = students > 0 && _drafts.Count > 0 && !_drafts.Any(d => d.busy);
            SetPrimary(students > 0 ? $"Send to {students} student{(students == 1 ? "" : "s")}" : "Nobody to send to", Send, can);
        }

        private void DeleteDraft(int i)
        {
            if (i < 0 || i >= _drafts.Count) return;
            _drafts.RemoveAt(i);
            _deleted++;
            if (_drafts.Count == 0) Toast.Show("Every question deleted: generate again.");
            RefreshPage();
        }

        private void Regenerate(int i)
        {
            if (i < 0 || i >= _drafts.Count || _drafts[i].busy) return;
            Draft d = _drafts[i];
            d.busy = true;
            RenderReview();
            string scope = !string.IsNullOrEmpty(d.q.partPath) ? d.q.partPath : _scopePath;
            QuizGenerator.Generate(scope, 1, Difficulties[_diff], r =>
            {
                d.busy = false;
                if (r.ok && r.questions.Length > 0) { d.q = r.questions[0]; d.regenerated = true; d.edited = false; }
                else Toast.Show("Could not write a new one: " + r.error);
                if (_review.activeSelf) RenderReview();
            });
        }

        private void BackToSetup()
        {
            _drafts.Clear();
            RefreshPage();
        }

        private void Send()
        {
            QuizSession s = QuizSession.Instance;
            if (s == null || _drafts.Count == 0) return;
            int edited = _drafts.Count(d => d.edited && !d.regenerated), regenerated = _drafts.Count(d => d.regenerated);
            if (!s.Send(_drafts.Select(d => d.q).ToList(), Limits[_limit], Difficulties[_diff])) { Toast.Show("Could not send the quiz."); return; }
            QuizLog.Review(_generated, edited, _deleted, regenerated);
            _drafts.Clear();
            Toast.Show("Quiz sent.");
            RefreshPage();
        }

        // ── Live / results ─────────────────────────────────────────────

        private void RefreshLive()
        {
            QuizSession s = QuizSession.Instance;
            QuizSession.Hosted h = s != null ? s.Hosting : null;
            if (h == null) return;

            int n = h.full.Count;
            if (h.state.Running)
            {
                float left = QuizSession.SecondsLeft(h.state);
                SetHead("Quiz running", $"{n} question(s) · " + (left >= 0 ? $"{(int)left / 60}:{(int)left % 60:00} left" : "no time limit"));
                _liveText.text = h.students.Count == 0 ? "Waiting for students…" : "";
                _liveText.gameObject.SetActive(_liveText.text.Length > 0);

                // Rows rebuild only when a student's progress changes, not every clock tick.
                string sig = "R" + string.Join("|", h.students.Values.Select(x => $"{x.name}:{x.Answered}:{x.finished}:{x.present}"));
                if (sig != _liveSig)
                {
                    _liveSig = sig;
                    AuraKit.ClearChildren(_liveRows);
                    foreach (var st in h.students.Values.OrderBy(x => x.name))
                        LiveRow(st.name, st.finished ? "finished" : $"{st.Answered} of {n}",
                                n > 0 ? (float)st.Answered / n : 0f, st.finished, !st.present);
                }
                SetPrimary("End quiz", () => QuizSession.Instance?.End("ended by the teacher"), true, danger: true);
                _newQuiz.SetActive(false);
            }
            else
            {
                var r = h.report;
                SetHead("Quiz over", $"Ended: {h.state.reason} · class average {(r != null ? r.Average * 100f : 0f):0}% · saved to Reports");
                _liveText.gameObject.SetActive(false);
                string sig = "E" + (r != null ? r.students.Count : -1);
                if (sig != _liveSig)
                {
                    _liveSig = sig;
                    AuraKit.ClearChildren(_liveRows);
                    if (r != null)
                        foreach (var st in r.students)
                            LiveRow(st.name + (st.note.Length > 0 ? $" ({st.note})" : ""), $"{st.correct} / {st.total}",
                                    st.total > 0 ? (float)st.correct / st.total : 0f, true, false);
                }
                SetPrimary("Open report", OpenReport);
                _newQuiz.SetActive(true);
            }
        }

        private void LiveRow(string name, string value, float fraction, bool done, bool left)
        {
            SimpleBarChart.Bar(_liveRows, Esc(name) + (left ? " (left)" : ""), fraction, value,
                               done ? new Color(0.44f, 0.76f, 0.63f) : AuraTheme.Lavender);
        }

        private void OpenReport()
        {
            QuizSession s = QuizSession.Instance;
            var r = s != null && s.Hosting != null ? s.Hosting.report : null;
            Close();
            if (r != null) ReportsUI.Open(r);
        }

        // ── Edit modal ─────────────────────────────────────────────────

        private void OpenEdit(int i)
        {
            if (i < 0 || i >= _drafts.Count) return;
            _editIndex = i;
            QuizQuestion q = _drafts[i].q;
            _editQ.text = q.question;
            for (int k = 0; k < 4; k++) _editO[k].text = k < q.options.Length ? q.options[k] : "";
            _editAnswer = Mathf.Clamp(q.answer, 0, 3);
            _setRight(_editAnswer);
            _editModal.SetActive(true);
            PanelNavigator.Push(EditNavId, this, CloseEdit);
        }

        private void SaveEdit()
        {
            if (_editIndex < 0 || _editIndex >= _drafts.Count) { CloseEdit(); return; }
            if (string.IsNullOrWhiteSpace(_editQ.text) || _editO.Any(o => string.IsNullOrWhiteSpace(o.text)))
            {
                Toast.Show("Fill in the question and all four options.");
                return;
            }
            Draft d = _drafts[_editIndex];
            d.q.question = _editQ.text.Trim();
            d.q.options = _editO.Select(o => o.text.Trim()).ToArray();
            d.q.answer = _editAnswer;
            d.edited = true;
            CloseEdit();
            RenderReview();
        }

        private void CloseEdit()
        {
            if (!_editModal.activeSelf) return;
            foreach (var f in _editO) f.DeactivateInputField();
            _editQ.DeactivateInputField();
            _editModal.SetActive(false);
            PanelNavigator.Remove(EditNavId);
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "QuizTeacherCanvas", 99, out _);
            var sheet = AuraViewer.ToolSheet(safe, "QuizSheet");
            sheet.gameObject.AddComponent<TouchBlocker>();

            // Header: title, Start over / New quiz, close.
            var head = AuraKit.Rect(sheet, "Header");
            AuraKit.HStack(head, 12f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(head, -1, 88f);
            _title = AuraKit.Text(head, "AI quiz", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            AuraKit.Flex(_title);
            AuraWidgets.Chip(head, "Start over", AuraIcons.Orbit, false, BackToSetup, out Button so, 84f);
            _startOver = so.gameObject;
            AuraWidgets.Chip(head, "New quiz", AuraIcons.Plus, true, () => QuizSession.Instance?.ClearFinished(), out Button nq, 84f);
            _newQuiz = nq.gameObject;
            AuraWidgets.Round(head, AuraIcons.Close, 84f, false, Close, out _);

            _subtitle = AuraKit.Text(sheet, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _subtitle.enableWordWrapping = true;

            // Body: as tall as the step it shows, scrolling past about half the screen.
            var host = AuraKit.Rect(sheet, "Body");
            var body = AuraWidgets.Scroll(host, out _scroll, 0f);
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            var fit = host.gameObject.AddComponent<FitToContent>();
            fit.content = body;
            fit.maxFraction = 0.5f;

            BuildSetup(body);
            BuildWorking(body);
            BuildReview(body);
            BuildLive(body);

            // The main action, full width along the bottom of the sheet.
            _primary = AuraKit.Pill(sheet, "Generate", null, AuraTheme.Lavender, AuraTheme.Ink, null, AuraTheme.MinTouch);
            AuraKit.Size(_primary, -1, AuraTheme.MinTouch);
            _primaryFill = _primary.GetComponent<Image>();
            _primaryLabel = _primary.GetComponentInChildren<TextMeshProUGUI>();

            _sheet = AuraViewer.SlideUp(sheet);
            BuildEditModal();
        }

        private RectTransform Page(RectTransform parent, string name)
        {
            var rt = AuraKit.Rect(parent, name);
            AuraKit.VStack(rt, 20f);
            return rt;
        }

        private System.Action<int> Choice(RectTransform parent, string label, string[] options, System.Action<int> pick)
        {
            var block = AuraKit.Rect(parent, label);
            AuraKit.VStack(block, 8f);
            AuraKit.Caps(block, label);
            return AuraWidgets.Segmented(block, options, 0, pick);
        }

        private void BuildSetup(RectTransform parent)
        {
            var p = Page(parent, "Setup");
            _setup = p.gameObject;

            var scope = AuraKit.Rect(p, "Scope");
            AuraKit.VStack(scope, 8f);
            _scopeRow = scope.gameObject;
            AuraKit.Caps(scope, "About");
            _setScope = AuraWidgets.Segmented(scope, new[] { "Whole model", "Selected part" }, 0,
                                              k => { _scopePath = k == 0 ? "" : _selectedPath; RefreshSetup(); });
            _scopeNote = AuraKit.Text(p, "", AuraTheme.SizeCaption, AuraTheme.TextDim);

            _setCount = Choice(p, "Questions", new[] { "5", "10", "15" }, k => { _count = k; RefreshSetup(); });
            _setDiff = Choice(p, "Difficulty", new[] { "Easy", "Medium", "Hard" }, k => { _diff = k; RefreshSetup(); });
            _setLimit = Choice(p, "Time limit", new[] { "None", "2 min", "5 min", "10 min" }, k => { _limit = k; RefreshSetup(); });

            _status = AuraKit.Text(p, "", AuraTheme.SizeSmall, AuraTheme.Rose);
        }

        private void BuildWorking(RectTransform parent)
        {
            var p = Page(parent, "Working");
            _working = p.gameObject;
            var row = AuraKit.Rect(p, "Row");
            AuraKit.HStack(row, 20f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(row, -1, 140f);
            var icon = AuraKit.Icon(row, AuraIcons.AI, 72f, AuraTheme.Lavender);
            icon.gameObject.AddComponent<Blink>();
            _workingText = AuraKit.Text(row, "", AuraTheme.SizeSmall, AuraTheme.Text);
            _workingText.enableWordWrapping = true;
            AuraKit.Flex(_workingText);
        }

        private void BuildReview(RectTransform parent)
        {
            var p = Page(parent, "Review");
            _review = p.gameObject;
            _reviewHead = AuraKit.Text(p, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _reviewRows = AuraKit.Rect(p, "Questions");
            AuraKit.VStack(_reviewRows, 14f);
        }

        private void BuildLive(RectTransform parent)
        {
            var p = Page(parent, "Live");
            _live = p.gameObject;
            _liveText = AuraKit.Text(p, "", AuraTheme.SizeSmall, AuraTheme.TextDim);
            _liveRows = AuraKit.Rect(p, "Students");
            AuraKit.VStack(_liveRows, 8f);
        }

        private void BuildEditModal()
        {
            var canvas = AuraKit.CreateCanvas(transform, "QuizEdit", 102);
            _editModal = canvas.gameObject;
            var dim = AuraKit.Box(canvas.transform, "Dim", AuraTheme.A(AuraTheme.Ink, 0.75f), 0f, true);
            AuraKit.Fill(dim.rectTransform);
            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();
            var lift = AuraKit.Fill(AuraKit.Rect(safe, "Lift"));
            lift.gameObject.AddComponent<KeyboardLift>().target = lift;

            var card = AuraKit.Box(lift, "Card", AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.56f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            card.gameObject.AddComponent<MatchParentWidth>().max = 980f;
            var v = AuraKit.VStack(card, 16f);
            v.padding = new RectOffset(40, 40, 36, 36);
            AuraKit.FitHeight(card);

            AuraKit.Text(card.transform, "Edit question", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            _editQ = AuraWidgets.Input(card.transform, "Question", "", true, 180f, 300);
            for (int k = 0; k < 4; k++)
                _editO[k] = AuraWidgets.Input(card.transform, $"Option {(char)('A' + k)}", "", false, 112f, 160);
            AuraKit.Caps(card.transform, "Right answer");
            _setRight = AuraWidgets.Segmented(card.transform, new[] { "A", "B", "C", "D" }, 0, k => _editAnswer = k);

            var back = AuraKit.Pill(lift, "Back", AuraIcons.Back, AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Text,
                                    CloseEdit, AuraViewer.BackHeight, AuraTheme.Slate);
            AuraKit.Pin((RectTransform)back.transform, Vector2.zero, Vector2.zero,
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));
            var save = AuraKit.Pill(lift, "Save", AuraIcons.Ok, AuraTheme.Lavender, AuraTheme.Ink, SaveEdit, AuraViewer.BackHeight);
            AuraKit.Pin((RectTransform)save.transform, new Vector2(1, 0), new Vector2(1, 0),
                        new Vector2(-AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(300, AuraViewer.BackHeight));

            _editModal.SetActive(false);
        }
    }
}
