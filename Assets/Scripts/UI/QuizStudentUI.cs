using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Quiz;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The student's side of the AI quiz (V3 Phase 11):
    ///
    ///   - A banner at the top: "New quiz from Asha · 10 questions. Tap to start."
    ///   - A sheet at the bottom, the model still visible above it: one question at a time, four
    ///     options; a question about a part highlights that part. No marks are shown while it runs.
    ///   - When the quiz ends: "Your score: 7 / 10", and every question with your answer, the right
    ///     one and why.
    /// </summary>
    public class QuizStudentUI : MonoBehaviour
    {
        public static QuizStudentUI Instance { get; private set; }

        private const string NavId = "quiz-student";
        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.98f);
        private static readonly Color BannerColor = new Color(0.36f, 0.30f, 0.85f, 0.97f);
        private static readonly Color OptionColor = new Color(0.22f, 0.26f, 0.36f, 1f);

        private GameObject _banner, _sheet, _asking, _results;
        private TextMeshProUGUI _bannerText, _head, _question, _resultsHead;
        private readonly TextMeshProUGUI[] _options = new TextMeshProUGUI[4];
        private RectTransform _resultRows;
        private string _highlighted;
        private string _renderedResultsFor;
        private float _nextClock;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            QuizSession.OnChanged += Refresh;
        }

        void OnDestroy()
        {
            QuizSession.OnChanged -= Refresh;
            PanelNavigator.Remove(NavId);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextClock) return;
            _nextClock = Time.unscaledTime + 0.5f;
            QuizSession.Taking t = QuizSession.Instance != null ? QuizSession.Instance.Current : null;
            if (t != null && !t.ended) Refresh(); // the clock
        }

        private void Refresh()
        {
            QuizSession.Taking t = QuizSession.Instance != null ? QuizSession.Instance.Current : null;
            bool banner = t != null && !t.started && !t.ended && !t.stopped;
            bool sheet = t != null && t.started;
            // A quiz that ended before this student opened it: nothing to show, let it go.
            if (t != null && !t.started && (t.ended || t.stopped)) { QuizSession.Instance.Dismiss(); return; }

            if (_banner.activeSelf != banner) _banner.SetActive(banner);
            if (banner)
            {
                float left = QuizSession.SecondsLeft(t.state);
                _bannerText.text = $"New quiz from {Esc(t.state.teacherName)}  ·  {t.state.questions.Count} questions" +
                                   (left >= 0 ? $"  ·  {Clock(left)}" : "") + "\n<b>Tap to start</b>";
            }

            if (_sheet.activeSelf != sheet)
            {
                _sheet.SetActive(sheet);
                if (sheet) PanelNavigator.Push(NavId, this, OnBack);
                else PanelNavigator.Remove(NavId);
            }
            if (!sheet) { Highlight(""); return; }

            if (t.ended || t.stopped) { ShowResults(t); return; }

            _results.SetActive(false);
            _asking.SetActive(true);
            float secs = QuizSession.SecondsLeft(t.state);
            string clock = secs >= 0 ? $"  ·  {Clock(secs)} left" : "";

            if (t.finished)
            {
                _head.text = "All answered" + clock;
                _question.text = "Waiting for the quiz to end. Your score shows here then.";
                for (int k = 0; k < 4; k++) _options[k].transform.parent.gameObject.SetActive(false);
                Highlight("");
                return;
            }

            QuizItem q = t.state.questions[t.index];
            _head.text = $"Question {t.index + 1} of {t.state.questions.Count}" + clock;
            _question.text = Esc(q.q);
            for (int k = 0; k < 4; k++)
            {
                bool has = q.o != null && k < q.o.Length;
                _options[k].transform.parent.gameObject.SetActive(has);
                if (has) _options[k].text = $"{(char)('A' + k)}.  {Esc(q.o[k])}";
            }
            Highlight(q.part);
        }

        private void ShowResults(QuizSession.Taking t)
        {
            _asking.SetActive(false);
            _results.SetActive(true);
            Highlight("");
            if (_renderedResultsFor == t.state.id + t.ended + t.stopped) return;
            _renderedResultsFor = t.state.id + t.ended + t.stopped;

            for (int i = _resultRows.childCount - 1; i >= 0; i--) Destroy(_resultRows.GetChild(i).gameObject);
            if (t.stopped && !t.ended)
            {
                _resultsHead.text = "The teacher left, so the quiz stopped. No marks this time.";
                return;
            }

            int n = t.state.questions.Count;
            _resultsHead.text = $"<b>Your score: {t.Correct} / {n}</b>";
            for (int i = 0; i < n; i++)
            {
                QuizItem q = t.state.questions[i];
                int mine = t.choices[i];
                bool right = mine >= 0 && mine == q.a;
                var sb = new StringBuilder();
                sb.Append($"<b>{i + 1}. {Esc(q.q)}</b>\n");
                sb.Append(mine < 0 ? "<color=#FFFFFF88>Not answered.</color>"
                    : right ? $"<color=#7CF2A0>Your answer: {Esc(Opt(q, mine))}  (right)</color>"
                            : $"<color=#FF9A9A>Your answer: {Esc(Opt(q, mine))}</color>");
                if (!right && q.a >= 0) sb.Append($"\n<color=#7CF2A0>Right answer: {Esc(Opt(q, q.a))}</color>");
                if (!string.IsNullOrEmpty(q.e)) sb.Append($"\n<color=#FFFFFFAA>{Esc(q.e)}</color>");
                var tx = UIBits.Text(_resultRows, sb.ToString(), 18f, Color.white);
                tx.enableWordWrapping = true;
                tx.richText = true;
            }
        }

        private static string Opt(QuizItem q, int k) => q.o != null && k >= 0 && k < q.o.Length ? q.o[k] : "";

        /// <summary>A question about a part lights that part on this phone only (never shared).</summary>
        private void Highlight(string path)
        {
            path = path ?? "";
            if (path == (_highlighted ?? "")) return;
            _highlighted = path;
            ModelPartSelector.Instance?.ShowLocalHighlight(path);
        }

        private void OnBack()
        {
            QuizSession.Taking t = QuizSession.Instance != null ? QuizSession.Instance.Current : null;
            if (t != null && (t.ended || t.stopped)) QuizSession.Instance.Dismiss();
            else Toast.Show("Answer the questions first; the quiz stays open until it ends.");
        }

        private static string Clock(float s) => $"{(int)s / 60}:{(int)s % 60:00}";
        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            UIBits.Canvas(gameObject, 99);

            // Banner: top centre, one big button.
            _banner = new GameObject("QuizBanner", typeof(RectTransform));
            _banner.transform.SetParent(transform, false);
            var brt = (RectTransform)_banner.transform;
            brt.anchorMin = new Vector2(0.16f, 0.835f);
            brt.anchorMax = new Vector2(0.70f, 0.835f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            brt.sizeDelta = new Vector2(0f, 120f);
            var bimg = _banner.AddComponent<Image>();
            bimg.color = BannerColor;
            var bb = _banner.AddComponent<Button>();
            bb.targetGraphic = bimg;
            bb.onClick.AddListener(() => QuizSession.Instance?.Begin());
            _bannerText = UIBits.Text(_banner.transform, "", 21f, Color.white);
            UIBits.Stretch(_bannerText.rectTransform);
            _bannerText.alignment = TextAlignmentOptions.Center;
            _bannerText.enableWordWrapping = true;
            _bannerText.richText = true;
            _bannerText.margin = new Vector4(12f, 6f, 12f, 6f);
            _banner.SetActive(false);

            // Sheet: bottom of the screen, the model visible above.
            _sheet = new GameObject("QuizSheet", typeof(RectTransform));
            _sheet.transform.SetParent(transform, false);
            var srt = (RectTransform)_sheet.transform;
            srt.anchorMin = new Vector2(0.02f, 0.02f);
            srt.anchorMax = new Vector2(0.98f, 0.47f);
            srt.offsetMin = srt.offsetMax = Vector2.zero;
            _sheet.AddComponent<Image>().color = PanelColor;
            _sheet.AddComponent<TouchBlocker>();
            var vg = _sheet.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 12, 14);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            _asking = UIBits.Column(_sheet.transform, 10f);
            _asking.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _head = UIBits.Text(_asking.transform, "", 19f, new Color(1f, 1f, 1f, 0.7f));
            _head.fontStyle = FontStyles.Bold;
            _question = UIBits.Text(_asking.transform, "", 24f, Color.white);
            _question.enableWordWrapping = true;
            _question.gameObject.AddComponent<LayoutElement>().minHeight = 80f;
            for (int k = 0; k < 4; k++)
            {
                int kk = k;
                GameObject b = UIBits.Button(_asking.transform, "", OptionColor, () => QuizSession.Instance?.Answer(kk), 100f);
                var le = b.GetComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = 76f;
                _options[k] = b.GetComponentInChildren<TextMeshProUGUI>();
                _options[k].alignment = TextAlignmentOptions.MidlineLeft;
                _options[k].fontStyle = FontStyles.Normal;
                _options[k].enableWordWrapping = true;
                _options[k].margin = new Vector4(16f, 4f, 16f, 4f);
                _options[k].fontSizeMax = 21f;
            }

            _results = UIBits.Column(_sheet.transform, 10f);
            _results.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _resultsHead = UIBits.Text(_results.transform, "", 24f, Color.white);
            _resultsHead.enableWordWrapping = true;
            _resultsHead.richText = true;
            _resultRows = UIBits.Scroll(_results.transform, 150f);
            var row = UIBits.Row(_results.transform, 60f);
            UIBits.Flex(UIBits.Button(row.transform, "CLOSE", OptionColor, () => QuizSession.Instance?.Dismiss(), 100f));
            _results.SetActive(false);

            _sheet.SetActive(false);
        }
    }
}
