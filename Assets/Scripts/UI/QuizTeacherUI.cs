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
    /// Settings → AI QUIZ, the teacher's side (V3 Phase 11). One panel, four pages:
    ///
    ///   SETUP     about the whole model or the selected part; 5 / 10 / 15 questions; easy /
    ///             medium / hard; time limit none / 2 / 5 / 10 min; GENERATE.
    ///   REVIEW    every question with its options (the right one marked): EDIT, NEW ONE (ask the
    ///             AI for a replacement), DELETE; then SEND TO N STUDENTS.
    ///   LIVE      the clock and each student's progress; END QUIZ.
    ///   RESULTS   the class average and each score; OPEN REPORT, NEW QUIZ.
    ///
    /// The panel can be closed while a quiz runs (Back); AI QUIZ opens it on the live page.
    /// </summary>
    public class QuizTeacherUI : MonoBehaviour
    {
        public static QuizTeacherUI Instance { get; private set; }

        private const string NavId = "quiz-teacher";
        private const string EditNavId = "quiz-edit";
        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.98f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color OnColor     = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color GoColor     = new Color(0.14f, 0.50f, 0.30f, 1f);
        private static readonly Color DangerColor = new Color(0.55f, 0.18f, 0.18f, 1f);

        private static readonly int[] Counts = { 5, 10, 15 };
        private static readonly string[] Difficulties = { "easy", "medium", "hard" };
        private static readonly int[] Limits = { 0, 120, 300, 600 };

        private GameObject _panel, _setup, _working, _review, _live;
        private TextMeshProUGUI _title, _status, _workingText, _reviewHead, _liveText;
        private RectTransform _reviewRows;
        private Button _sendBtn, _selectedBtn;
        private TextMeshProUGUI _selectedText, _sendText;
        private readonly List<Image> _scopeImgs = new List<Image>(), _countImgs = new List<Image>(),
                                     _diffImgs = new List<Image>(), _limitImgs = new List<Image>();
        private GameObject _liveEnd, _liveReport, _liveNew;

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
        private readonly Image[] _editRight = new Image[4];
        private int _editIndex, _editAnswer;
        private float _nextLive;

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
            if (!_panel.activeSelf) PanelNavigator.Push(NavId, this, Close);
            _panel.SetActive(true);

            ModelPartSelector sel = ModelPartSelector.Instance;
            string selected = sel != null ? sel.OwnPath : "";
            _selectedBtn.gameObject.SetActive(!string.IsNullOrEmpty(selected));
            _selectedText.text = string.IsNullOrEmpty(selected) ? "" : "SELECTED: " + Esc(QuizGenerator.ScopeLabel(selected));
            if (string.IsNullOrEmpty(selected) || (_scopePath.Length > 0 && _scopePath != selected)) _scopePath = "";
            _selectedPath = selected;
            RefreshPage();
        }

        private string _selectedPath = "";

        public void Close()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        void Update()
        {
            if (!_panel.activeSelf) return;
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) { Close(); return; }
            if (_live.activeSelf && Time.unscaledTime >= _nextLive) { _nextLive = Time.unscaledTime + 0.5f; RefreshLive(); }
        }

        // ── Pages ──────────────────────────────────────────────────────────────

        private void ShowPage(GameObject page)
        {
            foreach (GameObject p in new[] { _setup, _working, _review, _live }) p.SetActive(p == page);
        }

        private void RefreshPage()
        {
            if (_panel == null) return;
            QuizSession s = QuizSession.Instance;
            if (s != null && s.Hosting != null) { ShowPage(_live); RefreshLive(); return; }
            if (_pending != null) { ShowPage(_working); return; }
            if (_drafts.Count > 0) { ShowPage(_review); RenderReview(); return; }
            ShowPage(_setup);
            RefreshSetup();
        }

        private void RefreshSetup()
        {
            _title.text = "AI QUIZ";
            Mark(_scopeImgs, _scopePath.Length == 0 ? 0 : 1);
            Mark(_countImgs, _count);
            Mark(_diffImgs, _diff);
            Mark(_limitImgs, _limit);
        }

        private static void Mark(List<Image> imgs, int on)
        {
            for (int i = 0; i < imgs.Count; i++) imgs[i].color = i == on ? OnColor : ButtonColor;
        }

        // ── Generating ─────────────────────────────────────────────────────────

        private void Generate()
        {
            if (!AiClient.Ensure().IsConfigured) { _status.text = "The AI server is not set up on this build."; return; }
            int n = Counts[_count];
            _workingText.text = $"Writing {n} questions about {Esc(QuizGenerator.ScopeLabel(_scopePath))}...\nThis takes up to a minute.";
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

        // ── Review ─────────────────────────────────────────────────────────────

        private void RenderReview()
        {
            _title.text = "REVIEW";
            for (int i = _reviewRows.childCount - 1; i >= 0; i--) Destroy(_reviewRows.GetChild(i).gameObject);
            _reviewHead.text = $"{_drafts.Count} question(s) about {Esc(QuizGenerator.ScopeLabel(_scopePath))}, {Difficulties[_diff]}" +
                               (Limits[_limit] > 0 ? $", {Limits[_limit] / 60} min" : ", no time limit") + ". Check them, then send.";

            for (int i = 0; i < _drafts.Count; i++)
            {
                int idx = i;
                Draft d = _drafts[i];
                var row = UIBits.Column(_reviewRows, 6f);
                row.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
                row.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(12, 12, 10, 10);

                var sb = new StringBuilder();
                sb.Append($"<b>{i + 1}. {Esc(d.q.question)}</b>");
                if (d.edited) sb.Append("  <color=#FFD27A>(edited)</color>");
                if (d.regenerated) sb.Append("  <color=#9FD7FF>(new)</color>");
                for (int k = 0; k < d.q.options.Length; k++)
                    sb.Append(k == d.q.answer
                        ? $"\n<color=#7CF2A0>{(char)('A' + k)}. {Esc(d.q.options[k])}   (right)</color>"
                        : $"\n{(char)('A' + k)}. {Esc(d.q.options[k])}");
                string part = string.IsNullOrEmpty(d.q.partPath) ? "Whole model" : QuizGenerator.ScopeLabel(d.q.partPath);
                sb.Append($"\n<color=#FFFFFF88>Part: {Esc(part)}</color>");
                var t = UIBits.Text(row.transform, sb.ToString(), 19f, Color.white);
                t.enableWordWrapping = true;
                t.richText = true;

                var btns = UIBits.Row(row.transform, 50f);
                UIBits.Button(btns.transform, "EDIT", ButtonColor, () => OpenEdit(idx), 100f);
                UIBits.Button(btns.transform, d.busy ? "WRITING..." : "NEW ONE", ButtonColor, () => Regenerate(idx), 140f);
                UIBits.Flex(new GameObject("Space", typeof(RectTransform)).Also(g => g.transform.SetParent(btns.transform, false)));
                UIBits.Button(btns.transform, "DELETE", DangerColor, () => DeleteDraft(idx), 110f);
            }

            int students = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.PlayerCount - 1 : 0;
            _sendText.text = students > 0 ? $"SEND TO {students} STUDENT{(students == 1 ? "" : "S")}" : "NOBODY TO SEND TO";
            _sendBtn.interactable = students > 0 && _drafts.Count > 0 && !_drafts.Any(d => d.busy);
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

        // ── Live / results ─────────────────────────────────────────────────────

        private void RefreshLive()
        {
            QuizSession s = QuizSession.Instance;
            QuizSession.Hosted h = s != null ? s.Hosting : null;
            if (h == null) return;

            int n = h.full.Count;
            var sb = new StringBuilder();
            if (h.state.Running)
            {
                float left = QuizSession.SecondsLeft(h.state);
                _title.text = "QUIZ RUNNING";
                sb.Append($"{n} question(s)" + (left >= 0 ? $"  ·  {(int)left / 60}:{(int)left % 60:00} left" : "  ·  no time limit") + "\n\n");
                foreach (var st in h.students.Values.OrderBy(x => x.name))
                    sb.Append($"{Esc(st.name)}  -  " + (st.finished ? "<color=#7CF2A0>finished</color>" : $"{st.Answered} of {n}") +
                              (st.present ? "" : "  <color=#FF9A9A>(left)</color>") + "\n");
                if (h.students.Count == 0) sb.Append("Waiting for students...");
            }
            else
            {
                _title.text = "QUIZ OVER";
                var r = h.report;
                sb.Append($"Ended: {h.state.reason}.  Class average <b>{(r != null ? r.Average * 100f : 0f):0}%</b>\n\n");
                if (r != null)
                    foreach (var st in r.students)
                        sb.Append($"{Esc(st.name)}  -  {st.correct} / {st.total}" + (st.note.Length > 0 ? $"  <color=#FFFFFF88>({st.note})</color>" : "") + "\n");
                sb.Append("\nSaved to REPORTS.");
            }
            _liveText.text = sb.ToString().TrimEnd();
            _liveEnd.SetActive(h.state.Running);
            _liveReport.SetActive(!h.state.Running);
            _liveNew.SetActive(!h.state.Running);
        }

        private void OpenReport()
        {
            QuizSession s = QuizSession.Instance;
            var r = s != null && s.Hosting != null ? s.Hosting.report : null;
            Close();
            if (r != null) ReportsUI.Open(r);
        }

        // ── Edit modal ─────────────────────────────────────────────────────────

        private void OpenEdit(int i)
        {
            if (i < 0 || i >= _drafts.Count) return;
            _editIndex = i;
            QuizQuestion q = _drafts[i].q;
            _editQ.text = q.question;
            for (int k = 0; k < 4; k++) _editO[k].text = k < q.options.Length ? q.options[k] : "";
            _editAnswer = Mathf.Clamp(q.answer, 0, 3);
            MarkRight();
            _editModal.SetActive(true);
            PanelNavigator.Push(EditNavId, this, CloseEdit);
        }

        private void MarkRight()
        {
            for (int k = 0; k < 4; k++) _editRight[k].color = k == _editAnswer ? GoColor : ButtonColor;
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
            _editModal.SetActive(false);
            PanelNavigator.Remove(EditNavId);
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            UIBits.Canvas(gameObject, 99);

            _panel = new GameObject("QuizTeacher", typeof(RectTransform));
            _panel.transform.SetParent(transform, false);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = new Vector2(0.04f, 0.22f);
            rt.anchorMax = new Vector2(0.96f, 0.84f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _panel.AddComponent<Image>().color = PanelColor;
            _panel.AddComponent<TouchBlocker>();
            var vg = _panel.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 12, 14);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var head = UIBits.Row(_panel.transform, 52f);
            _title = UIBits.Text(head.transform, "AI QUIZ", 24f, Color.white);
            _title.fontStyle = FontStyles.Bold;
            UIBits.Flex(_title.gameObject);
            UIBits.Button(head.transform, "X", new Color(0.45f, 0.14f, 0.14f), Close, 52f);

            BuildSetup();
            BuildWorking();
            BuildReview();
            BuildLive();
            BuildEditModal();
            _panel.SetActive(false);
        }

        private GameObject Page(string name)
        {
            var go = UIBits.Column(_panel.transform, 10f);
            go.name = name;
            go.AddComponent<LayoutElement>().flexibleHeight = 1f;
            return go;
        }

        private void Choices(Transform parent, string label, string[] options, List<Image> imgs, System.Action<int> pick)
        {
            var row = UIBits.Row(parent, 58f);
            var l = UIBits.Text(row.transform, label, 19f, new Color(1f, 1f, 1f, 0.75f));
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                GameObject b = UIBits.Button(row.transform, options[i], ButtonColor, () => pick(k), 100f);
                UIBits.Flex(b);
                imgs.Add(b.GetComponent<Image>());
            }
        }

        private void BuildSetup()
        {
            _setup = Page("Setup");

            var scopeRow = UIBits.Row(_setup.transform, 58f);
            var sl = UIBits.Text(scopeRow.transform, "About", 19f, new Color(1f, 1f, 1f, 0.75f));
            sl.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            GameObject whole = UIBits.Button(scopeRow.transform, "WHOLE MODEL", ButtonColor, () => { _scopePath = ""; RefreshSetup(); }, 100f);
            UIBits.Flex(whole);
            _scopeImgs.Add(whole.GetComponent<Image>());
            GameObject selected = UIBits.Button(scopeRow.transform, "SELECTED", ButtonColor, () => { _scopePath = _selectedPath; RefreshSetup(); }, 100f);
            UIBits.Flex(selected);
            _scopeImgs.Add(selected.GetComponent<Image>());
            _selectedBtn = selected.GetComponent<Button>();
            _selectedText = selected.GetComponentInChildren<TextMeshProUGUI>();

            Choices(_setup.transform, "Questions", new[] { "5", "10", "15" }, _countImgs, k => { _count = k; RefreshSetup(); });
            Choices(_setup.transform, "Difficulty", new[] { "EASY", "MEDIUM", "HARD" }, _diffImgs, k => { _diff = k; RefreshSetup(); });
            Choices(_setup.transform, "Time limit", new[] { "NONE", "2 MIN", "5 MIN", "10 MIN" }, _limitImgs, k => { _limit = k; RefreshSetup(); });

            var go = UIBits.Row(_setup.transform, 66f);
            UIBits.Flex(UIBits.Button(go.transform, "GENERATE", OnColor, Generate, 100f));

            _status = UIBits.Text(_setup.transform, "", 18f, new Color(1f, 0.7f, 0.7f));
            _status.enableWordWrapping = true;
            var hint = UIBits.Text(_setup.transform,
                "The AI writes multiple-choice questions from the model's part facts. You can edit, replace or delete any of them before sending.",
                17f, new Color(1f, 1f, 1f, 0.55f));
            hint.enableWordWrapping = true;
        }

        private void BuildWorking()
        {
            _working = Page("Working");
            _workingText = UIBits.Text(_working.transform, "", 22f, Color.white);
            _workingText.enableWordWrapping = true;
            _workingText.alignment = TextAlignmentOptions.Center;
            _workingText.gameObject.AddComponent<LayoutElement>().minHeight = 160f;
            var row = UIBits.Row(_working.transform, 60f);
            UIBits.Flex(UIBits.Button(row.transform, "CANCEL", ButtonColor, CancelGenerate, 100f));
        }

        private void BuildReview()
        {
            _review = Page("Review");
            _reviewHead = UIBits.Text(_review.transform, "", 17f, new Color(1f, 1f, 1f, 0.7f));
            _reviewHead.enableWordWrapping = true;
            _reviewRows = UIBits.Scroll(_review.transform, 200f);
            var btns = UIBits.Row(_review.transform, 64f);
            UIBits.Button(btns.transform, "START OVER", ButtonColor, BackToSetup, 170f);
            GameObject send = UIBits.Button(btns.transform, "SEND", GoColor, Send, 100f);
            UIBits.Flex(send);
            _sendBtn = send.GetComponent<Button>();
            _sendText = send.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void BuildLive()
        {
            _live = Page("Live");
            _liveText = UIBits.Text(_live.transform, "", 20f, Color.white);
            _liveText.enableWordWrapping = true;
            _liveText.alignment = TextAlignmentOptions.TopLeft;
            _liveText.richText = true;
            _liveText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var btns = UIBits.Row(_live.transform, 64f);
            _liveEnd = UIBits.Flex(UIBits.Button(btns.transform, "END QUIZ", DangerColor, () => QuizSession.Instance?.End("ended by the teacher"), 100f));
            _liveReport = UIBits.Flex(UIBits.Button(btns.transform, "OPEN REPORT", OnColor, OpenReport, 100f));
            _liveNew = UIBits.Flex(UIBits.Button(btns.transform, "NEW QUIZ", ButtonColor, () => QuizSession.Instance?.ClearFinished(), 100f));
        }

        private void BuildEditModal()
        {
            _editModal = new GameObject("EditModal", typeof(RectTransform));
            _editModal.transform.SetParent(transform, false);
            UIBits.Stretch((RectTransform)_editModal.transform);
            var mc = _editModal.AddComponent<Canvas>();
            mc.overrideSorting = true;
            mc.sortingOrder = 102;
            _editModal.AddComponent<GraphicRaycaster>();
            _editModal.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            _editModal.AddComponent<TouchBlocker>();

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(_editModal.transform, false);
            var crt = (RectTransform)card.transform;
            crt.anchorMin = new Vector2(0.05f, 0.5f); crt.anchorMax = new Vector2(0.95f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.AddComponent<Image>().color = PanelColor;
            var vg = card.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(20, 20, 16, 18);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var t = UIBits.Text(card.transform, "Edit question  ·  tap RIGHT beside the correct option", 20f, Color.white);
            t.fontStyle = FontStyles.Bold;
            t.enableWordWrapping = true;
            _editQ = UIBits.Input(card.transform, "Question", 120f, true, 300);
            for (int k = 0; k < 4; k++)
            {
                int kk = k;
                var row = UIBits.Row(card.transform, 64f);
                var lab = UIBits.Text(row.transform, ((char)('A' + k)).ToString(), 22f, Color.white);
                lab.gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;
                _editO[k] = UIBits.Input(row.transform, $"Option {(char)('A' + k)}", 60f, false, 160);
                UIBits.Flex(_editO[k].gameObject);
                _editRight[k] = UIBits.Button(row.transform, "RIGHT", ButtonColor, () => { _editAnswer = kk; MarkRight(); }, 100f).GetComponent<Image>();
            }
            var btns = UIBits.Row(card.transform, 62f);
            UIBits.Flex(UIBits.Button(btns.transform, "CANCEL", ButtonColor, CloseEdit, 100f));
            UIBits.Flex(UIBits.Button(btns.transform, "SAVE", OnColor, SaveEdit, 100f));

            _editModal.SetActive(false);
        }
    }

    internal static class GameObjectAlso
    {
        public static GameObject Also(this GameObject go, System.Action<GameObject> a) { a(go); return go; }
    }
}
