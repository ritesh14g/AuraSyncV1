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
    /// REPORTS (V3 Phase 11): every quiz report and heatmap snapshot saved on this phone, openable
    /// any time, with no room and no model (it is reachable from the model list too).
    ///
    ///   list     newest first, unopened ones marked NEW (the red count on the REPORTS buttons).
    ///   quiz     the class average; a bar per student; correct % per question; the weak parts
    ///            (missed most); and attention against correct % per part, when the heatmap
    ///            counted anything (Phase 10).
    ///   heatmap  attention per part.
    ///   EXPORT CSV (every number) and EXPORT IMAGE (what is on screen), through the share sheet.
    ///
    /// Charts are plain UI bars (<see cref="SimpleBarChart"/>), no chart library.
    /// </summary>
    public class ReportsUI : MonoBehaviour
    {
        private static ReportsUI _instance;
        private const string NavId = "reports";

        private static readonly Color PanelColor  = new Color(0.06f, 0.07f, 0.10f, 0.99f);
        private static readonly Color RowColor    = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color DangerColor = new Color(0.55f, 0.18f, 0.18f, 1f);
        private static readonly Color Dim         = new Color(1f, 1f, 1f, 0.6f);

        private GameObject _root, _listPage, _detailPage, _detailButtons;
        private RectTransform _listRows, _detailRows;
        private TextMeshProUGUI _detailTitle;
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
            _root.SetActive(true);
            if (!PanelNavigator.IsOpen(NavId)) PanelNavigator.Push(NavId, this, Back);
            if (report != null) ShowDetail(report);
            else ShowList();
        }

        /// <summary>Back: from a report to the list, from the list to closed.</summary>
        private void Back()
        {
            if (_detailPage.activeSelf) { ShowList(); PanelNavigator.Push(NavId, this, Back); return; }
            Close();
        }

        private void Close()
        {
            _root.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        // ── List ───────────────────────────────────────────────────────────────

        private void ShowList()
        {
            _detailPage.SetActive(false);
            _listPage.SetActive(true);
            _open = null;
            for (int i = _listRows.childCount - 1; i >= 0; i--) Destroy(_listRows.GetChild(i).gameObject);

            List<Report> all = ReportStore.List();
            if (all.Count == 0)
            {
                var t = UIBits.Text(_listRows, "No reports yet. A quiz saves one when it ends; a heatmap when you tap SAVE TO REPORTS.", 19f, Dim);
                t.enableWordWrapping = true;
            }
            foreach (Report r in all)
            {
                Report rr = r;
                var row = UIBits.Column(_listRows, 4f);
                row.AddComponent<Image>().color = RowColor;
                row.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(14, 14, 10, 10);

                var name = UIBits.Text(row.transform, (r.seen ? "" : "<color=#FF6B6B><b>NEW</b></color>  ") + "<b>" + Esc(r.title) + "</b>", 21f, Color.white);
                name.richText = true;
                name.enableWordWrapping = true;
                string meta = r.kind == "quiz"
                    ? $"Quiz  ·  {r.students.Count} student(s)  ·  class average {r.Average * 100f:0}%  ·  {r.CreatedLocal:d MMM HH:mm}"
                    : $"Heatmap ({Esc(r.scope)})  ·  {r.CreatedLocal:d MMM HH:mm}";
                UIBits.Text(row.transform, meta, 17f, Dim).enableWordWrapping = true;

                var btns = UIBits.Row(row.transform, 52f);
                UIBits.Button(btns.transform, "OPEN", AccentColor, () => ShowDetail(rr), 110f);
                UIBits.Flex(new GameObject("Space", typeof(RectTransform)).Also(g => g.transform.SetParent(btns.transform, false)));
                TextMeshProUGUI delText = null;
                delText = UIBits.Button(btns.transform, "DELETE", DangerColor, () => Delete(rr, delText), 110f).GetComponentInChildren<TextMeshProUGUI>();
            }
        }

        private void Delete(Report r, TextMeshProUGUI label)
        {
            if (_armedDelete != r.id || Time.unscaledTime - _armedAt > 3f)
            {
                _armedDelete = r.id;
                _armedAt = Time.unscaledTime;
                if (label != null) label.text = "SURE?";
                return;
            }
            ReportStore.Delete(r);
            ShowList();
        }

        // ── One report ─────────────────────────────────────────────────────────

        private void ShowDetail(Report r)
        {
            _open = r;
            ReportStore.MarkSeen(r);
            _listPage.SetActive(false);
            _detailPage.SetActive(true);
            _detailTitle.text = Esc(r.title);
            for (int i = _detailRows.childCount - 1; i >= 0; i--) Destroy(_detailRows.GetChild(i).gameObject);

            UIBits.Text(_detailRows, $"{Esc(r.modelName)}  ·  {r.CreatedLocal:d MMM yyyy HH:mm}" + (r.room.Length > 0 ? $"  ·  room {Esc(r.room)}" : ""), 17f, Dim);

            if (r.kind == "quiz") QuizDetail(r);
            else HeatDetail(r);
        }

        private void QuizDetail(Report r)
        {
            var avg = UIBits.Text(_detailRows, $"Class average  <b>{r.Average * 100f:0}%</b>   ·   {r.students.Count} student(s), {r.questions.Count} question(s)" +
                                               (r.difficulty.Length > 0 ? $", {r.difficulty}" : ""), 22f, Color.white);
            avg.richText = true;
            avg.enableWordWrapping = true;

            Section("Score per student");
            foreach (StudentScore s in r.students.OrderByDescending(s => s.correct))
                SimpleBarChart.Bar(_detailRows, s.name + (s.note.Length > 0 ? $" ({s.note})" : ""),
                                   s.total > 0 ? (float)s.correct / s.total : 0f, $"{s.correct}/{s.total}", AccentColor);
            if (r.students.Count == 0) UIBits.Text(_detailRows, "Nobody answered.", 18f, Dim);

            Section("Correct per question");
            for (int i = 0; i < r.questions.Count; i++)
            {
                QuestionStat q = r.questions[i];
                float pct = q.answered > 0 ? (float)q.correct / q.answered : 0f;
                SimpleBarChart.Bar(_detailRows, $"Q{i + 1}. {Short(q.text, 40)}", pct, q.answered > 0 ? $"{pct * 100f:0}%" : "-",
                                   Color.Lerp(new Color(0.85f, 0.3f, 0.3f), new Color(0.25f, 0.75f, 0.4f), pct));
            }

            var weak = r.parts.Where(p => p.asked > 0 && p.missed > 0).OrderByDescending(p => (float)p.missed / p.asked).Take(5).ToList();
            Section("Weak parts (missed most)");
            foreach (PartRow p in weak)
                SimpleBarChart.Bar(_detailRows, p.label, (float)p.missed / p.asked, $"{100f * p.missed / p.asked:0}% missed", new Color(0.85f, 0.35f, 0.3f));
            if (weak.Count == 0) UIBits.Text(_detailRows, r.parts.Count > 0 ? "No part's questions were missed." : "No question was about one part.", 18f, Dim);

            float maxAtt = r.parts.Count > 0 ? r.parts.Max(p => p.attention) : 0f;
            if (maxAtt > 0f)
            {
                Section("Attention vs score per part");
                var legend = UIBits.Text(_detailRows, "<color=#F2994A>orange</color> = attention (heatmap)    <color=#6FCF97>green</color> = answered right", 16f, Dim);
                legend.richText = true;
                foreach (PartRow p in r.parts.OrderByDescending(p => p.attention))
                {
                    float right = p.asked > 0 ? 1f - (float)p.missed / p.asked : 0f;
                    SimpleBarChart.Bar(_detailRows, p.label, p.attention / maxAtt, $"{p.attention:0}", new Color(0.95f, 0.6f, 0.29f));
                    SimpleBarChart.Bar(_detailRows, "", right, p.asked > 0 ? $"{right * 100f:0}%" : "-", new Color(0.44f, 0.81f, 0.59f));
                }
            }
        }

        private void HeatDetail(Report r)
        {
            Section($"Attention per part  ·  {Esc(r.scope)}");
            float max = r.parts.Count > 0 ? Mathf.Max(0.0001f, r.parts.Max(p => p.attention)) : 1f;
            foreach (PartRow p in r.parts)
                SimpleBarChart.Bar(_detailRows, p.label, p.attention / max, p.attention > 0f ? p.attention.ToString("0") : "never",
                                   AuraSync.Analytics.HeatmapView.Ramp(p.attention / max));
        }

        private void Section(string title)
        {
            var t = UIBits.Text(_detailRows, title.ToUpperInvariant(), 18f, new Color(0.62f, 0.72f, 1f));
            t.fontStyle = FontStyles.Bold;
            t.margin = new Vector4(0f, 14f, 0f, 2f);
        }

        // ── Export ─────────────────────────────────────────────────────────────

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
            _detailButtons.SetActive(false);
            yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            _detailButtons.SetActive(true);
            string tmp = Path.Combine(Application.temporaryCachePath, ExportName + ".png");
            File.WriteAllBytes(tmp, shot.EncodeToPNG());
            Destroy(shot);
            Toast.Show(FileExport.Share(new[] { new FileExport.Item(tmp, ExportName + ".png", "image/png") },
                                        "AuraSync report: " + _open.title, Path.Combine(ReportStore.Folder, "Export")));
        }

        private static string Short(string s, int n)
        {
            s = (s ?? "").Replace("\n", " ");
            return s.Length > n ? s.Substring(0, n).TrimEnd() + "..." : s;
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            UIBits.Canvas(gameObject, 104); // over the room panel (100/101), under the share prompt (105) and toasts (106)

            _root = new GameObject("Reports", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = new Vector2(0.03f, 0.04f);
            rt.anchorMax = new Vector2(0.97f, 0.93f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = PanelColor;
            _root.AddComponent<TouchBlocker>();
            var vg = _root.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 14, 14);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            // List page
            _listPage = UIBits.Column(_root.transform, 10f);
            _listPage.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var lh = UIBits.Row(_listPage.transform, 54f);
            var lt = UIBits.Text(lh.transform, "REPORTS", 26f, Color.white);
            lt.fontStyle = FontStyles.Bold;
            UIBits.Flex(lt.gameObject);
            UIBits.Button(lh.transform, "X", new Color(0.45f, 0.14f, 0.14f), Close, 54f);
            _listRows = UIBits.Scroll(_listPage.transform, 300f);

            // Detail page
            _detailPage = UIBits.Column(_root.transform, 10f);
            _detailPage.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var dh = UIBits.Row(_detailPage.transform, 54f);
            UIBits.Button(dh.transform, "<", ButtonColor, () => { ShowList(); }, 54f);
            _detailTitle = UIBits.Text(dh.transform, "", 22f, Color.white);
            _detailTitle.fontStyle = FontStyles.Bold;
            _detailTitle.overflowMode = TextOverflowModes.Ellipsis;
            _detailTitle.enableWordWrapping = false;
            UIBits.Flex(_detailTitle.gameObject);
            UIBits.Button(dh.transform, "X", new Color(0.45f, 0.14f, 0.14f), Close, 54f);
            _detailRows = UIBits.Scroll(_detailPage.transform, 300f);
            _detailRows.GetComponent<VerticalLayoutGroup>().spacing = 6;
            _detailButtons = UIBits.Row(_detailPage.transform, 60f);
            UIBits.Flex(UIBits.Button(_detailButtons.transform, "EXPORT CSV", ButtonColor, ExportCsv, 100f));
            UIBits.Flex(UIBits.Button(_detailButtons.transform, "EXPORT IMAGE", ButtonColor, ExportImage, 100f));
            _detailPage.SetActive(false);

            _root.SetActive(false);
        }

        // ── REPORTS buttons ────────────────────────────────────────────────────

        /// <summary>
        /// Adds REPORTS, with its red count, to the model list (V1), which is a scene object: the
        /// button is made in code so the scene file is not touched.
        /// </summary>
        public static void AddButtonTo(RectTransform panel)
        {
            if (panel == null || panel.Find("AuraSync_ReportsButton") != null) return;
            var go = new GameObject("AuraSync_ReportsButton", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -24f);
            rt.sizeDelta = new Vector2(200f, 72f);
            var img = go.AddComponent<Image>();
            img.color = ButtonColor;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => Open());
            var t = UIBits.Text(go.transform, "REPORTS", 22f, Color.white);
            UIBits.Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            go.AddComponent<ReportsCount>().Attach(go.transform);
        }
    }

    /// <summary>The red unseen-count badge on a REPORTS button (the model list's).</summary>
    public class ReportsCount : MonoBehaviour
    {
        private GameObject _badge;
        private TextMeshProUGUI _text;

        public void Attach(Transform button)
        {
            _badge = new GameObject("Count", typeof(RectTransform), typeof(Image));
            _badge.transform.SetParent(button, false);
            var rt = (RectTransform)_badge.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(40f, 40f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            _badge.GetComponent<Image>().color = new Color(0.86f, 0.18f, 0.18f);
            _text = UIBits.Text(_badge.transform, "", 20f, Color.white);
            UIBits.Stretch(_text.rectTransform);
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontStyle = FontStyles.Bold;
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

    /// <summary>REPORTS in the 3D view's top-right corner row, with the unseen count.</summary>
    public class ReportsCornerButton : MonoBehaviour
    {
        private const string SlotId = "reports";
        private bool _shown;
        private int _count = -1;

        IEnumerator Start()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddCornerButton(SlotId, "REPORTS", () => ReportsUI.Open());
            SpaceSessionHUD.Instance.SetSlotVisible(SlotId, false);
            ReportStore.OnChanged += Recount;
            Recount();
        }

        void OnDestroy() => ReportStore.OnChanged -= Recount;

        private void Recount()
        {
            _count = ReportStore.Unseen;
            SpaceSessionHUD.Instance?.SetSlotCount(SlotId, _count);
            _any = ReportStore.Count > 0;
        }

        private bool _any;

        void Update()
        {
            SpaceSessionHUD hud = SpaceSessionHUD.Instance;
            if (hud == null || !hud.HasSlot(SlotId)) return;
            // The presenter (or someone alone) always; anyone else once this phone has a report.
            bool show = !string.IsNullOrEmpty(AuraSync.Space.SpaceSceneManager.CurrentModelKey) &&
                        !AuraSync.Recording.SessionPlayer.IsReplaying &&
                        (SessionRecorder.LocalMayRecord || _any);
            if (show != _shown) { _shown = show; hud.SetSlotVisible(SlotId, show); }
        }
    }

    /// <summary>Horizontal bars in plain UI (no chart library): label, bar, value.</summary>
    public static class SimpleBarChart
    {
        private static readonly Color Track = new Color(1f, 1f, 1f, 0.08f);

        public static void Bar(Transform parent, string label, float fraction, string value, Color color)
        {
            var row = UIBits.Row(parent, 40f);
            row.GetComponent<HorizontalLayoutGroup>().spacing = 10;

            var l = UIBits.Text(row.transform, (label ?? "").Replace("<", "").Replace(">", ""), 17f, Color.white);
            l.overflowMode = TextOverflowModes.Ellipsis;
            l.enableWordWrapping = false;
            var lle = l.gameObject.AddComponent<LayoutElement>();
            lle.preferredWidth = lle.minWidth = 300f;

            var track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(row.transform, false);
            track.GetComponent<Image>().color = Track;
            UIBits.Flex(track);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = new Vector2(0f, 0.15f);
            frt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 0.85f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = color;

            var v = UIBits.Text(row.transform, value ?? "", 17f, new Color(1f, 1f, 1f, 0.8f));
            v.alignment = TextAlignmentOptions.MidlineRight;
            var vle = v.gameObject.AddComponent<LayoutElement>();
            vle.preferredWidth = vle.minWidth = 140f;
        }
    }
}
