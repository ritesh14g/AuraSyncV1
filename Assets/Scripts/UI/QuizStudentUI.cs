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
    ///   - A banner above the bottom buttons: "New quiz from Asha · 10 questions · Tap to start".
    ///   - A sheet along the bottom (the dock steps aside), the model still visible above it: one
    ///     question at a time with big A–D answer cards; the clock in the header. A question about a
    ///     part highlights that part on this phone. No marks are shown while it runs.
    ///   - When the quiz ends: the score, and every question with your answer, the right one and why.
    /// </summary>
    public class QuizStudentUI : MonoBehaviour
    {
        public static QuizStudentUI Instance { get; private set; }

        private const string NavId = "quiz-student";

        private GameObject _banner;
        private AuraSlidePanel _sheet;
        private GameObject _asking, _results;
        private TextMeshProUGUI _bannerText, _head, _clock, _question, _resultsHead;
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
            SpaceSessionHUD.Instance?.HideDockFor("quiz", false);
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
            if (t != null && !t.started && (t.ended || t.stopped)) { QuizSession.Instance.Dismiss(); return; }

            if (_banner.activeSelf != banner) _banner.SetActive(banner);
            if (banner)
            {
                float left = QuizSession.SecondsLeft(t.state);
                _bannerText.text = $"<b>New quiz from {Esc(t.state.teacherName)}</b>\n<color=#A9A8CC>{t.state.questions.Count} questions" +
                                   (left >= 0 ? $" · {Clock(left)}" : "") + " · tap to start</color>";
            }

            if (_sheet.IsShown != sheet)
            {
                if (sheet)
                {
                    ViewerPanels.CloseAll();
                    _sheet.Show();
                    PanelNavigator.Push(NavId, this, OnBack);
                }
                else
                {
                    _sheet.Hide();
                    PanelNavigator.Remove(NavId);
                }
                SpaceSessionHUD.Instance?.HideDockFor("quiz", sheet);
            }
            if (!sheet) { Highlight(""); return; }

            if (t.ended || t.stopped) { ShowResults(t); return; }

            _results.SetActive(false);
            _asking.SetActive(true);
            float secs = QuizSession.SecondsLeft(t.state);
            _clock.text = secs >= 0 ? $"{Clock(secs)} LEFT" : "";

            if (t.finished)
            {
                _head.text = "All answered";
                _question.text = "Waiting for the quiz to end. Your score shows here then.";
                for (int k = 0; k < 4; k++) _options[k].transform.parent.gameObject.SetActive(false);
                Highlight("");
                return;
            }

            QuizItem q = t.state.questions[t.index];
            _head.text = $"Question {t.index + 1} of {t.state.questions.Count}";
            _question.text = Esc(q.q);
            for (int k = 0; k < 4; k++)
            {
                bool has = q.o != null && k < q.o.Length;
                _options[k].transform.parent.gameObject.SetActive(has);
                if (has) _options[k].text = Esc(q.o[k]);
            }
            Highlight(q.part);
        }

        private void ShowResults(QuizSession.Taking t)
        {
            _asking.SetActive(false);
            _results.SetActive(true);
            _clock.text = "";
            Highlight("");
            if (_renderedResultsFor == t.state.id + t.ended + t.stopped) return;
            _renderedResultsFor = t.state.id + t.ended + t.stopped;

            AuraKit.ClearChildren(_resultRows);
            if (t.stopped && !t.ended)
            {
                _head.text = "Quiz stopped";
                _resultsHead.text = "The teacher left, so the quiz stopped. No marks this time.";
                return;
            }

            int n = t.state.questions.Count;
            _head.text = "Quiz over";
            _resultsHead.text = $"<size=150%><b><color=#9894C4>{t.Correct}</color> / {n}</b></size>\n<color=#A9A8CC>your score</color>";
            for (int i = 0; i < n; i++)
            {
                QuizItem q = t.state.questions[i];
                int mine = t.choices[i];
                bool right = mine >= 0 && mine == q.a;

                var card = AuraKit.Box(_resultRows, "Result", AuraTheme.Ink, AuraTheme.Radius - 8f);
                AuraKit.Border(card, right ? new Color(0.44f, 0.76f, 0.63f) : mine < 0 ? AuraTheme.Slate : AuraTheme.Rose, AuraTheme.Radius - 8f);
                var v = AuraKit.VStack(card, 6f);
                v.padding = new RectOffset(26, 26, 18, 18);

                var sb = new StringBuilder();
                sb.Append($"<b>{i + 1}. {Esc(q.q)}</b>\n");
                sb.Append(mine < 0 ? "<color=#A9A8CC>Not answered.</color>"
                    : right ? $"<color=#6FC2A0>Your answer: {Esc(Opt(q, mine))} (right)</color>"
                            : $"<color=#D46A82>Your answer: {Esc(Opt(q, mine))}</color>");
                if (!right && q.a >= 0) sb.Append($"\n<color=#6FC2A0>Right answer: {Esc(Opt(q, q.a))}</color>");
                if (!string.IsNullOrEmpty(q.e)) sb.Append($"\n<color=#A9A8CC>{Esc(q.e)}</color>");
                var tx = AuraKit.Text(card.transform, sb.ToString(), AuraTheme.SizeCaption, AuraTheme.Text);
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
            else
            {
                Toast.Show("Answer the questions first; the quiz stays open until it ends.");
                PanelNavigator.Push(NavId, this, OnBack); // Back took the entry off: the quiz is still open
            }
        }

        private static string Clock(float s) => $"{(int)s / 60}:{(int)s % 60:00}";
        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "QuizStudent", 99, out _);

            // Banner: above the bottom buttons, one big tap target.
            var banner = AuraKit.Box(safe, "QuizBanner", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(banner, AuraTheme.Lavender, AuraTheme.Radius + 6f);
            var brt = banner.rectTransform;
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(1, 0); brt.pivot = new Vector2(0.5f, 0);
            brt.offsetMin = new Vector2(AuraViewer.Gutter, AuraViewer.BottomReserve);
            brt.offsetMax = new Vector2(-(AuraViewer.DockWidth + AuraViewer.DockGap + 6f), AuraViewer.BottomReserve + 150f);
            var bh = AuraKit.HStack(banner, 22f);
            bh.padding = new RectOffset(30, 30, 0, 0);
            AuraKit.Icon(banner.transform, AuraIcons.Quiz, 64f, AuraTheme.Lavender);
            _bannerText = AuraKit.Text(banner.transform, "", AuraTheme.SizeSmall, AuraTheme.Text);
            _bannerText.richText = true;
            AuraKit.Flex(_bannerText);
            AuraKit.Icon(banner.transform, AuraIcons.Forward, 44f, AuraTheme.Lavender);
            AuraKit.MakeButton(banner.gameObject, banner, () => QuizSession.Instance?.Begin());
            _banner = banner.gameObject;
            _banner.SetActive(false);

            // Sheet: full width along the bottom (the dock steps aside), as tall as its content.
            var sheet = AuraKit.Box(safe, "QuizSheet", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(sheet, AuraTheme.Slate, AuraTheme.Radius + 6f);
            var srt = sheet.rectTransform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.offsetMin = new Vector2(AuraViewer.Gutter, AuraViewer.BottomReserve);
            srt.offsetMax = new Vector2(-AuraViewer.Gutter, AuraViewer.BottomReserve + 300f);
            var v = AuraKit.VStack(sheet, 16f);
            v.padding = new RectOffset(36, 36, 28, 32);
            AuraKit.FitHeight(sheet);

            var head = AuraKit.Rect(srt, "Header");
            AuraKit.HStack(head, 12f);
            AuraKit.Size(head, -1, 56f);
            _head = AuraKit.Text(head, "", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.Bold);
            AuraKit.Flex(_head);
            _clock = AuraKit.Caps(head, "", AuraTheme.Lavender);
            _clock.alignment = TextAlignmentOptions.MidlineRight;
            AuraKit.Size(_clock, 300f);

            _asking = AuraKit.Rect(srt, "Asking").gameObject;
            AuraKit.VStack(_asking.GetComponent<RectTransform>(), 14f);
            _question = AuraKit.Text(_asking.transform, "", AuraTheme.SizeBody + 2f, AuraTheme.Text, AuraWeight.SemiBold);
            _question.enableWordWrapping = true;
            for (int k = 0; k < 4; k++)
            {
                int kk = k;
                var opt = AuraKit.Box(_asking.transform, "Option", AuraTheme.Ink, AuraTheme.Radius - 6f, true);
                AuraKit.Border(opt, AuraTheme.A(AuraTheme.Slate, 0.9f), AuraTheme.Radius - 6f);
                var oh = AuraKit.HStack(opt, 20f);
                oh.padding = new RectOffset(22, 26, 18, 18);
                var le = opt.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 112f;

                var letter = AuraKit.Rect(opt.transform, "Letter").gameObject.AddComponent<Image>();
                letter.sprite = AuraSprites.Disc;
                letter.color = AuraTheme.A(AuraTheme.Lavender, 0.18f);
                AuraKit.Size(letter, 66f, 66f);
                var lt = AuraKit.Text(letter.transform, ((char)('A' + k)).ToString(), AuraTheme.SizeSmall, AuraTheme.Lavender,
                                      AuraWeight.Bold, TextAlignmentOptions.Center);
                AuraKit.Fill(lt.rectTransform);

                _options[k] = AuraKit.Text(opt.transform, "", AuraTheme.SizeSmall + 2f, AuraTheme.Text);
                _options[k].enableWordWrapping = true;
                AuraKit.Size(_options[k], 0f, -1, 1f);
                AuraKit.MakeButton(opt.gameObject, opt, () => QuizSession.Instance?.Answer(kk));
            }

            _results = AuraKit.Rect(srt, "Results").gameObject;
            AuraKit.VStack(_results.GetComponent<RectTransform>(), 14f);
            _resultsHead = AuraKit.Text(_results.transform, "", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.Regular, TextAlignmentOptions.Center);
            _resultsHead.richText = true;
            var host = AuraKit.Rect(_results.transform, "Rows");
            _resultRows = AuraWidgets.Scroll(host, out ScrollRect scroll, 12f);
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var fit = host.gameObject.AddComponent<FitToContent>();
            fit.content = _resultRows;
            fit.maxFraction = 0.4f;
            AuraKit.Pill(_results.transform, "Done", AuraIcons.Ok, AuraTheme.Lavender, AuraTheme.Ink,
                         () => QuizSession.Instance?.Dismiss(), AuraTheme.MinTouch);
            _results.SetActive(false);

            _sheet = AuraViewer.SlideUp(srt);
        }
    }
}
