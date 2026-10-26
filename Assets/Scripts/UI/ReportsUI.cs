using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Recording;
using AuraSync.Reports;

namespace AuraSync.UI
{
    /// <summary>
    /// REPORTS (V3 Phase 11; Analysis → Reports, or the model list): every quiz report and heatmap
    /// snapshot saved on this phone, openable any time, with no room and no model.
    ///
    ///   list     newest first; unopened ones carry a NEW chip (the count on the Analysis button).
    ///            Delete asks for a second tap.
    ///   quiz     the class average; a bar per student; correct % per question; the weak parts
    ///            (missed most); and attention against correct % per part when the heatmap
    ///            counted anything (Phase 10).
    ///   heatmap  attention per part.
    ///   CSV and Image export (top-right of a report) go through the share sheet.
    ///
    /// Back (bottom-left, or the phone's) goes from a report to the list, and from the list out.
    /// </summary>
    public class ReportsUI : MonoBehaviour
    {
        private static ReportsUI _instance;
        private const string NavId = "reports";

        private AuraPage _page;
        private GameObject _exports;
        private Report _open;
        private string _armedDelete;
        private float _armedAt;

        /// <summary>Opens the list (or straight onto one report).</summary>
        public static void Open(Report report = null)
        {
            if (_instance == null) _instance = new GameObject("AuraSync_Reports").AddComponent<ReportsUI>();
            _instance.Show(report);
        }

        void Awake() => Build();

        void OnDestroy()
        {
            PanelNavigator.Remove(NavId);
            if (_instance == this) _instance = null;
        }

        private void Show(Report report)
        {
            _page.Open();
            if (!PanelNavigator.IsOpen(NavId)) PanelNavigator.Push(NavId, this, Back);
            if (report != null) ShowDetail(report);
            else ShowList();
        }

        /// <summary>Back: from a report to the list, from the list to closed.</summary>
        private void Back()
        {
            if (_open != null) { ShowList(); PanelNavigator.Push(NavId, this, Back); return; }
            Close();
        }

        private void Close()
        {
            _page.Close();
            PanelNavigator.Remove(NavId);
        }

        // ── List ───────────────────────────────────────────────────────

        private void ShowList()
        {
            _open = null;
            _exports.SetActive(false);
            _page.Title.text = "Reports";
            _page.ClearBody();

            List<Report> all = ReportStore.List();
            int unseen = all.Count(r => !r.seen);
            _page.Subtitle.text = all.Count == 0 ? "Nothing saved yet" : unseen > 0 ? $"{all.Count} saved · {unseen} new" : $"{all.Count} saved";

            if (all.Count == 0)
            {
                AuraKit.Text(_page.Body, "No reports yet. A quiz saves one when it ends; a heatmap when you tap Save to reports.",
                             AuraTheme.SizeBody, AuraTheme.TextDim);
            }
            foreach (Report r in all)
            {
                Report rr = r;
                var row = AuraKit.Box(_page.Body, "Report", AuraTheme.A(AuraTheme.Indigo, 0.9f), AuraTheme.Radius, true);
                AuraKit.Border(row, r.seen ? AuraTheme.A(AuraTheme.Slate, 0.8f) : AuraTheme.Lavender);
                var h = AuraKit.HStack(row, 22f);
                h.padding = new RectOffset(28, 22, 24, 24);
                AuraKit.MakeButton(row.gameObject, row, () => ShowDetail(rr));

                var badge = AuraKit.Box(row.transform, "Kind", AuraTheme.A(AuraTheme.Lavender, 0.15f), 28f);
                AuraKit.Size(badge, 110f, 110f);
                var bi = AuraKit.Icon(badge.transform, r.kind == "quiz" ? AuraIcons.Quiz : AuraIcons.Fire, 58f, AuraTheme.Lavender);
                bi.GetComponent<LayoutElement>().ignoreLayout = true;
                AuraKit.Pin(bi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58, 58));

                var words = AuraKit.Rect(row.transform, "Words");
                AuraKit.VStack(words, 6f, 0, TextAnchor.MiddleLeft);
                AuraKit.Size(words, 0f, -1, 1f);
                var title = AuraKit.Text(words, (r.seen ? "" : "<color=#9894C4>NEW</color>  ") + Esc(r.title),
                                         AuraTheme.SizeBody - 2f, AuraTheme.Text, AuraWeight.SemiBold);
                title.richText = true;
                title.enableWordWrapping = true;
                string meta = r.kind == "quiz"
                    ? $"{r.students.Count} student(s) · class average {r.Average * 100f:0}% · {r.CreatedLocal:d MMM HH:mm}"
                    : $"Heatmap ({Esc(r.scope)}) · {r.CreatedLocal:d MMM HH:mm}";
                AuraKit.Text(words, meta, AuraTheme.SizeCaption - 2f, AuraTheme.TextDim).enableWordWrapping = true;

                TextMeshProUGUI delText = null;
                AuraWidgets.Chip(row.transform, "Delete", AuraIcons.Trash, false, () => Delete(rr, delText), out Button del, 92f);
                delText = del.GetComponentInChildren<TextMeshProUGUI>();
                if (delText != null) delText.color = AuraTheme.Rose;
            }
            _page.ScrollToTop();
        }

        private void Delete(Report r, TextMeshProUGUI label)
        {
            if (_armedDelete != r.id || Time.unscaledTime - _armedAt > 3f)
            {
                _armedDelete = r.id;
                _armedAt = Time.unscaledTime;
                if (label != null) label.text = "Sure?";
                return;
            }
            ReportStore.Delete(r);
            ShowList();
        }

        // ── One report ─────────────────────────────────────────────────

        private void ShowDetail(Report r)
        {
            _open = r;
            ReportStore.MarkSeen(r);
            _exports.SetActive(true);
            _page.Title.text = Esc(r.title);
            _page.Subtitle.text = $"{Esc(r.modelName)} · {r.CreatedLocal:d MMM yyyy HH:mm}" + (r.room.Length > 0 ? $" · room {Esc(r.room)}" : "");
            _page.ClearBody();

            if (r.kind == "quiz") QuizDetail(r);
            else HeatDetail(r);
            _page.ScrollToTop();
        }

        private void QuizDetail(Report r)
        {
            var avg = AuraKit.Box(_page.Body, "Average", AuraTheme.A(AuraTheme.Lavender, 0.12f), AuraTheme.Radius);
            AuraKit.Border(avg, AuraTheme.A(AuraTheme.Lavender, 0.5f));
            var av = AuraKit.HStack(avg, 24f);
            av.padding = new RectOffset(30, 30, 24, 24);
            var big = AuraKit.Text(avg.transform, $"{r.Average * 100f:0}%", 76f, AuraTheme.Lavender, AuraWeight.Bold);
            big.enableWordWrapping = false;
            AuraKit.Size(big, 230f);
            var detail = AuraKit.Text(avg.transform, $"Class average\n<color=#A9A8CC>{r.students.Count} student(s), {r.questions.Count} question(s)" +
                                                     (r.difficulty.Length > 0 ? $", {r.difficulty}" : "") + "</color>",
                                      AuraTheme.SizeBody - 2f, AuraTheme.Text, AuraWeight.SemiBold);
            detail.richText = true;
            AuraKit.Flex(detail);

            Section("Score per student");
            foreach (StudentScore s in r.students.OrderByDescending(s => s.correct))
                SimpleBarChart.Bar(_page.Body, s.name + (s.note.Length > 0 ? $" ({s.note})" : ""),
                                   s.total > 0 ? (float)s.correct / s.total : 0f, $"{s.correct}/{s.total}", AuraTheme.Lavender);
            if (r.students.Count == 0) AuraKit.Text(_page.Body, "Nobody answered.", AuraTheme.SizeSmall, AuraTheme.TextDim);

            Section("Correct per question");
            for (int i = 0; i < r.questions.Count; i++)
            {
                QuestionStat q = r.questions[i];
                float pct = q.answered > 0 ? (float)q.correct / q.answered : 0f;
                SimpleBarChart.Bar(_page.Body, $"Q{i + 1}. {Short(q.text, 40)}", pct, q.answered > 0 ? $"{pct * 100f:0}%" : "–",
                                   Color.Lerp(AuraTheme.Rose, new Color(0.44f, 0.76f, 0.63f), pct));
            }

            var weak = r.parts.Where(p => p.asked > 0 && p.missed > 0).OrderByDescending(p => (float)p.missed / p.asked).Take(5).ToList();
            Section("Weak parts (missed most)");
            foreach (PartRow p in weak)
                SimpleBarChart.Bar(_page.Body, p.label, (float)p.missed / p.asked, $"{100f * p.missed / p.asked:0}% missed", AuraTheme.Rose);
            if (weak.Count == 0)
                AuraKit.Text(_page.Body, r.parts.Count > 0 ? "No part's questions were missed." : "No question was about one part.",
                             AuraTheme.SizeSmall, AuraTheme.TextDim);

            float maxAtt = r.parts.Count > 0 ? r.parts.Max(p => p.attention) : 0f;
            if (maxAtt > 0f)
            {
                Section("Attention vs score per part");
                var legend = AuraKit.Text(_page.Body, "<color=#E5A04F>orange</color> = attention (heatmap)    <color=#6FC2A0>green</color> = answered right",
                                          AuraTheme.SizeCaption - 2f, AuraTheme.TextDim);
                legend.richText = true;
                foreach (PartRow p in r.parts.OrderByDescending(p => p.attention))
                {
                    float right = p.asked > 0 ? 1f - (float)p.missed / p.asked : 0f;
                    SimpleBarChart.Bar(_page.Body, p.label, p.attention / maxAtt, $"{p.attention:0}", new Color(0.90f, 0.63f, 0.31f));
                    SimpleBarChart.Bar(_page.Body, "", right, p.asked > 0 ? $"{right * 100f:0}%" : "–", new Color(0.44f, 0.76f, 0.63f));
                }
            }
        }

        private void HeatDetail(Report r)
        {
            Section($"Attention per part · {Esc(r.scope)}");
            float max = r.parts.Count > 0 ? Mathf.Max(0.0001f, r.parts.Max(p => p.attention)) : 1f;
            foreach (PartRow p in r.parts)
                SimpleBarChart.Bar(_page.Body, p.label, p.attention / max, p.attention > 0f ? p.attention.ToString("0") : "never",
                                   AuraSync.Analytics.HeatmapView.Ramp(p.attention / max));
        }

        private void Section(string title)
        {
            var t = AuraKit.Caps(_page.Body, title, AuraTheme.Lavender);
            t.margin = new Vector4(0f, 22f, 0f, 2f);
        }

        // ── Export ─────────────────────────────────────────────────────

        private string ExportName => FileExport.SafeName(_open != null ? _open.title : "report", "AuraSync report");

        private void ExportCsv()
        {
            if (_open == null) return;
            string tmp = Path.Combine(Application.temporaryCachePath, ExportName + ".csv");
            File.WriteAllText(tmp, ReportStore.ToCsv(_open));
            Toast.Show(FileExport.Share(new[] { new FileExport.Item(tmp, ExportName + ".csv", "text/csv") },
                                        "AuraSync report: " + _open.title, Path.Combine(ReportStore.Folder, "Export")));
        }

        private void ExportImage() => StartCoroutine(CaptureImage());

        /// <summary>The report as it is on screen, as a PNG (scroll first to choose what it shows).</summary>
        private IEnumerator CaptureImage()
        {
            if (_open == null) yield break;
            _exports.SetActive(false);
            _page.Back.gameObject.SetActive(false);
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            _exports.SetActive(true);
            _page.Back.gameObject.SetActive(true);
            string tmp = Path.Combine(Application.temporaryCachePath, ExportName + ".png");
            File.WriteAllBytes(tmp, shot.EncodeToPNG());
            Destroy(shot);
            Toast.Show(FileExport.Share(new[] { new FileExport.Item(tmp, ExportName + ".png", "image/png") },
                                        "AuraSync report: " + _open.title, Path.Combine(ReportStore.Folder, "Export")));
        }

        private static string Short(string s, int n)
        {
            s = (s ?? "").Replace("\n", " ");
            return s.Length > n ? s.Substring(0, n).TrimEnd() + "…" : s;
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            _page = AuraPage.Create(transform, "ReportsPage", 104, () => Back()); // over the room card, under toasts (106)
            AuraWidgets.Chip(_page.HeaderRight, "CSV", AuraIcons.Doc, false, ExportCsv, out _, 96f);
            AuraWidgets.Chip(_page.HeaderRight, "Image", AuraIcons.Copy, false, ExportImage, out _, 96f);
            _exports = _page.HeaderRight.gameObject;
            _exports.SetActive(false);
        }

        // ── REPORTS button on the old model list (legacy, when the new onboarding is off) ──

        /// <summary>Adds REPORTS, with its unseen count, to the old model list (a scene object).</summary>
        public static void AddButtonTo(RectTransform panel)
        {
            if (panel == null || panel.Find("AuraSync_ReportsButton") != null) return;
            var b = AuraKit.Pill(panel, "Reports", AuraIcons.Reports, AuraTheme.Indigo, AuraTheme.Text, () => Open(), 96f, AuraTheme.Slate);
            b.gameObject.name = "AuraSync_ReportsButton";
            b.gameObject.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin((RectTransform)b.transform, Vector2.one, Vector2.one, new Vector2(-24f, -24f), new Vector2(260f, 96f));
            b.gameObject.AddComponent<ReportsCount>().Attach(b.transform);
        }
    }

    /// <summary>The unseen-count badge on the old model list's REPORTS button.</summary>
    public class ReportsCount : MonoBehaviour
    {
        private GameObject _badge;
        private TextMeshProUGUI _text;

        public void Attach(Transform button)
        {
            var badge = AuraKit.Rect(button, "Count").gameObject.AddComponent<Image>();
            badge.sprite = AuraSprites.Disc;
            badge.color = AuraTheme.Rose;
            badge.raycastTarget = false;
            badge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(badge.rectTransform, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-6f, -6f), new Vector2(40f, 40f));
            _badge = badge.gameObject;
            _text = AuraKit.Text(badge.transform, "", 20f, AuraTheme.Ink, AuraWeight.Bold, TextAlignmentOptions.Center);
            AuraKit.Fill(_text.rectTransform);
            ReportStore.OnChanged += Refresh;
            Refresh();
        }

        void OnDestroy() => ReportStore.OnChanged -= Refresh;

        private void Refresh()
        {
            if (_badge == null) return;
            int n = ReportStore.Unseen;
            _badge.SetActive(n > 0);
            _text.text = n > 9 ? "9+" : n.ToString();
        }
    }

    /// <summary>
    /// Kept so the 3D viewer's setup still finds it: Reports now lives under Analysis, which
    /// shows the unseen count (SettingsPanelUI), so this adds no button of its own.
    /// </summary>
    public class ReportsCornerButton : MonoBehaviour { }

    /// <summary>Horizontal bars in plain UI (no chart library): label, rounded bar, value.</summary>
    public static class SimpleBarChart
    {
        public static void Bar(Transform parent, string label, float fraction, string value, Color color)
        {
            var row = AuraKit.Rect(parent, "Bar");
            AuraKit.HStack(row, 16f).childForceExpandHeight = true;
            AuraKit.Size(row, -1, 64f);

            var l = AuraKit.Text(row, (label ?? "").Replace("<", "").Replace(">", ""), AuraTheme.SizeCaption, AuraTheme.Text);
            l.overflowMode = TextOverflowModes.Ellipsis;
            l.enableWordWrapping = false;
            AuraKit.Size(l, 330f);

            var holder = AuraKit.Rect(row, "TrackHolder");
            AuraKit.Flex(holder);
            var track = AuraKit.Box(holder, "Track", AuraTheme.A(AuraTheme.Slate, 0.3f), 14f);
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(0, 0.5f); trt.anchorMax = new Vector2(1, 0.5f);
            trt.offsetMin = new Vector2(0, -15f); trt.offsetMax = new Vector2(0, 15f);
            var fill = AuraKit.Box(track.transform, "Fill", color, 14f);
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            fill.enabled = fraction > 0.001f;

            var v = AuraKit.Text(row, value ?? "", AuraTheme.SizeCaption, AuraTheme.TextDim, AuraWeight.SemiBold,
                                 TextAlignmentOptions.MidlineRight);
            AuraKit.Size(v, 170f);
        }
    }
}
