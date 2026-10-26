using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.Analytics;
using AuraSync.Recording;
using AuraSync.Reports;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Analysis → Heatmap (V3 Phase 10). Two pieces, so the recoloured model stays in view:
    ///
    ///   top card      Heatmap · Live                        (x)
    ///                 (<)          Everyone           (>)       whose attention
    ///                 [] Never   Least ============ Most        the legend
    ///
    ///   bottom sheet  Most looked at        Least looked at
    ///                 1 Fan rotor    34     1 Spinner   never
    ///                 [Save to reports]
    ///
    /// The model is recoloured while it is open (HeatmapView) and back to normal when it closes.
    /// Live counts refresh every 2 s. A heatmap can also be made from a recording (Recordings →
    /// Heatmap), on the recording's model.
    /// </summary>
    public class HeatmapUI : MonoBehaviour
    {
        public static HeatmapUI Instance { get; private set; }

        private const string NavId = "heatmap";
        private const string PanelId = "heatmap";

        private AuraSlidePanel _top, _sheet;
        private TextMeshProUGUI _status, _who;
        private RectTransform _most, _least;

        private List<PersonStats> _people = new List<PersonStats>();
        private int _personIndex = -1; // -1 = everyone
        private bool _fromRecording;
        private string _recordingName = "";
        private float _nextRefresh;
        private string _shown;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            SharedStateBus.OnChange += OnBus;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnBus;
            HeatmapView.Clear();
            PanelNavigator.Remove(NavId);
            if (Instance == this) Instance = null;
        }

        private void OnBus(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded || !IsOpen) return;
            // The same model again (the full version replacing the preview): paint it again.
            // Another model: a recording's heatmap no longer fits it.
            if (_fromRecording) Close();
            else Refresh();
        }

        public bool IsOpen => _sheet != null && _sheet.IsShown;

        // ── Opening ────────────────────────────────────────────────────

        public void Open()
        {
            if (InteractionTracker.Instance == null) return;
            _fromRecording = false;
            _people = InteractionTracker.Instance.People();
            _personIndex = -1;
            Show();
        }

        public void OpenFromRecording(LoadedRecording rec)
        {
            _fromRecording = true;
            _recordingName = rec.info.name;
            _people = InteractionTracker.FromRecording(rec);
            _personIndex = -1;
            Show();
        }

        private void Show()
        {
            ModelPartsDrawerUI.Instance?.SetOpen(false);
            bool wasOpen = IsOpen;
            ViewerPanels.Opened(PanelId, Close);
            if (!wasOpen) PanelNavigator.Push(NavId, this, Close);
            _shown = null;
            Refresh();
            _top.Show();
            _sheet.Show();
        }

        public void Close()
        {
            HeatmapView.Clear();
            if (!IsOpen) return;
            _top.Hide();
            _sheet.Hide();
            ViewerPanels.Closed(PanelId);
            PanelNavigator.Remove(NavId);
        }

        void Update()
        {
            if (!IsOpen) return;
            if (string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) { Close(); return; }
            if (!_fromRecording && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 2f;
                if (InteractionTracker.Instance != null) _people = InteractionTracker.Instance.People();
                Refresh();
            }
        }

        // ── Content ────────────────────────────────────────────────────

        private Dictionary<string, float> Scores()
        {
            int actor = _personIndex >= 0 && _personIndex < _people.Count ? _people[_personIndex].actor : -1;
            return InteractionTracker.Combine(_people, actor).ToDictionary(kv => kv.Key, kv => kv.Value.Score);
        }

        private string WhoName => _personIndex >= 0 && _personIndex < _people.Count ? _people[_personIndex].name : "Everyone";

        private void Refresh()
        {
            if (_personIndex >= _people.Count) _personIndex = -1;
            Dictionary<string, float> scores = Scores();
            HeatmapView.Show(scores);

            _status.text = _fromRecording ? Esc(_recordingName) : "Live";
            _who.text = Esc(WhoName) + (_personIndex >= 0 && !_people[_personIndex].present ? "  (left)" : "");

            List<KeyValuePair<string, float>> ranked = HeatmapView.Rank(scores);
            var most = ranked.Take(5).ToList();
            var least = Enumerable.Reverse(ranked).Take(5).ToList();

            // The lists rebuild only when they change (live refreshes come every 2 s).
            string sig = string.Join("|", most.Concat(least).Select(kv => kv.Key + ":" + kv.Value.ToString("0")));
            if (sig == _shown) return;
            _shown = sig;
            Fill(_most, most);
            Fill(_least, least);
        }

        private static void Fill(RectTransform list, List<KeyValuePair<string, float>> rows)
        {
            AuraKit.ClearChildren(list, "Head");
            if (rows.Count == 0) { AuraKit.Text(list, "No parts.", AuraTheme.SizeCaption, AuraTheme.TextDim); return; }
            float max = Mathf.Max(1f, rows.Max(r => r.Value));
            int i = 1;
            foreach (var kv in rows)
            {
                var row = AuraKit.Rect(list, "Row");
                AuraKit.HStack(row, 12f, 0, TextAnchor.MiddleLeft);
                AuraKit.Size(row, -1, 56f);
                var dot = AuraKit.Box(row, "Dot", kv.Value > 0f ? HeatmapView.Ramp(kv.Value / max) : HeatmapView.Never, 9f);
                AuraKit.Size(dot, 18f, 18f);
                var n = AuraKit.Text(row, $"{i++}  {Esc(HeatmapView.Label(kv.Key))}", AuraTheme.SizeCaption, AuraTheme.Text);
                n.enableWordWrapping = false;
                n.overflowMode = TextOverflowModes.Ellipsis;
                AuraKit.Flex(n);
                AuraKit.Text(row, kv.Value > 0f ? kv.Value.ToString("0") : "never", AuraTheme.SizeCaption,
                             kv.Value > 0f ? AuraTheme.Lavender : AuraTheme.TextDim, AuraWeight.SemiBold,
                             TextAlignmentOptions.MidlineRight).enableWordWrapping = false;
            }
        }

        private void Step(int d)
        {
            int n = _people.Count + 1; // + everyone
            _personIndex = ((_personIndex + 1 + d) % n + n) % n - 1;
            Refresh();
        }

        private void SaveReport()
        {
            Dictionary<string, float> scores = Scores();
            var r = new Report
            {
                kind = "heatmap",
                model = SpaceSceneManager.CurrentModelKey,
                modelName = System.IO.Path.GetFileNameWithoutExtension(SpaceSceneManager.CurrentModelKey ?? ""),
                room = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : "",
                scope = WhoName,
                seen = true, // made here, by the person looking at it
            };
            r.title = $"Heatmap · {r.modelName} · {WhoName}" + (_fromRecording ? $" (recording {_recordingName})" : "");
            foreach (var kv in HeatmapView.Rank(scores))
                r.parts.Add(new PartRow { path = kv.Key, label = HeatmapView.Label(kv.Key), attention = kv.Value });
            ReportStore.Save(r);
            Toast.Show("Heatmap saved to Reports.");
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "HeatmapCanvas", 97, out _);
            BuildTop(safe);
            BuildSheet(safe);
        }

        private void BuildTop(RectTransform safe)
        {
            var card = AuraKit.Box(safe, "HeatmapTop", AuraTheme.A(AuraTheme.Indigo, 0.95f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 6f);
            card.gameObject.AddComponent<TouchBlocker>();
            var rt = card.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(AuraViewer.Gutter, -AuraViewer.TopReserve - 300f);
            rt.offsetMax = new Vector2(-AuraViewer.Gutter, -AuraViewer.TopReserve);
            var v = AuraKit.VStack(card, 14f);
            v.padding = new RectOffset(32, 28, 22, 26);
            AuraKit.FitHeight(card);

            var head = AuraKit.Rect(card.transform, "Header");
            AuraKit.HStack(head, 14f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(head, -1, 84f);
            AuraKit.Icon(head, AuraIcons.Chart, 46f, AuraTheme.Lavender);
            var title = AuraKit.Text(head, "Heatmap", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            title.enableWordWrapping = false;
            _status = AuraKit.Caps(head, "", AuraTheme.Lavender);
            _status.enableWordWrapping = false;
            _status.overflowMode = TextOverflowModes.Ellipsis;
            AuraKit.Flex(_status);
            AuraWidgets.Round(head, AuraIcons.Close, 80f, false, Close, out _);

            var who = AuraKit.Rect(card.transform, "Who");
            AuraKit.HStack(who, 14f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(who, -1, 92f);
            AuraWidgets.Round(who, AuraIcons.Prev, 88f, false, () => Step(-1), out _);
            _who = AuraKit.Text(who, "Everyone", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold, TextAlignmentOptions.Center);
            _who.enableWordWrapping = false;
            _who.overflowMode = TextOverflowModes.Ellipsis;
            AuraKit.Flex(_who);
            AuraWidgets.Round(who, AuraIcons.Next, 88f, false, () => Step(1), out _);

            // Legend: grey = never, then the ramp from least to most.
            var legend = AuraKit.Rect(card.transform, "Legend");
            AuraKit.HStack(legend, 12f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(legend, -1, 44f);
            var never = AuraKit.Box(legend, "Never", HeatmapView.Never, 10f);
            AuraKit.Size(never, 32f, 22f);
            AuraKit.Text(legend, "Never", AuraTheme.SizeLabel, AuraTheme.TextDim).enableWordWrapping = false;
            AuraKit.Size(AuraKit.Rect(legend, "Gap"), 18f);
            AuraKit.Text(legend, "Least", AuraTheme.SizeLabel, AuraTheme.TextDim).enableWordWrapping = false;
            var holder = AuraKit.Rect(legend, "RampHolder");
            AuraKit.Flex(holder);
            var ramp = AuraKit.Box(holder, "Ramp", AuraTheme.A(AuraTheme.Slate, 0.6f), 11f);
            var rrt = ramp.rectTransform;
            rrt.anchorMin = new Vector2(0, 0.5f); rrt.anchorMax = new Vector2(1, 0.5f);
            rrt.offsetMin = new Vector2(0, -11f); rrt.offsetMax = new Vector2(0, 11f);
            var grad = AuraKit.Rect(ramp.transform, "Gradient");
            AuraKit.Fill(grad, 3, 3, 3, 3);
            var raw = grad.gameObject.AddComponent<RawImage>();
            raw.texture = RampTexture();
            raw.raycastTarget = false;
            AuraKit.Text(legend, "Most", AuraTheme.SizeLabel, AuraTheme.TextDim).enableWordWrapping = false;

            _top = AuraSlidePanel.Attach(rt, rt.anchoredPosition, rt.anchoredPosition + new Vector2(0, 70f));
        }

        private void BuildSheet(RectTransform safe)
        {
            var sheet = AuraViewer.ToolSheet(safe, "HeatmapSheet");
            sheet.gameObject.AddComponent<TouchBlocker>();

            var cols = AuraKit.Rect(sheet, "Lists");
            var ch = AuraKit.HStack(cols, 30f, 0, TextAnchor.UpperLeft);
            ch.childForceExpandWidth = true;
            ch.childForceExpandHeight = false;
            _most = Column(cols, "Most looked at");
            _least = Column(cols, "Least looked at");

            var foot = AuraKit.Rect(sheet, "Foot");
            AuraKit.HStack(foot, 14f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(foot, -1, 92f);
            AuraWidgets.Chip(foot, "Save to reports", AuraIcons.Reports, false, SaveReport, out _, 92f);
            AuraKit.Flex(AuraKit.Rect(foot, "Space"));

            AuraKit.Text(sheet, "Picks, notes and AI questions count most, then time selected or isolated, then time looked at.",
                         AuraTheme.SizeLabel, AuraTheme.TextDim);

            _sheet = AuraViewer.SlideUp(sheet);
        }

        private static RectTransform Column(RectTransform parent, string title)
        {
            var col = AuraKit.Rect(parent, title);
            AuraKit.VStack(col, 4f);
            AuraKit.Flex(col);
            var head = AuraKit.Caps(col, title, AuraTheme.Lavender);
            head.name = "Head";
            AuraKit.Size(head, -1, 48f);
            return col;
        }

        private static Texture2D _ramp;
        private static Texture2D RampTexture()
        {
            if (_ramp != null) return _ramp;
            _ramp = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) _ramp.SetPixel(x, 0, HeatmapView.Ramp(x / 63f));
            _ramp.Apply();
            return _ramp;
        }
    }

    /// <summary>The small UI builders the Phase 10 and 11 panels share (each older panel has its own copies).</summary>
    public static class UIBits
    {
        public static GameObject Row(Transform parent, float height)
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

        public static GameObject Column(Transform parent, float spacing = 8f)
        {
            var go = new GameObject("Column", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vg = go.AddComponent<VerticalLayoutGroup>();
            vg.spacing = spacing;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            return go;
        }

        public static GameObject Flex(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            return go;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color)
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

        public static GameObject Button(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float width)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            var img = go.AddComponent<Image>();
            img.color = color;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Text(go.transform, label, 20f, Color.white);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true; t.fontSizeMin = 12f; t.fontSizeMax = 20f;
            return go;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>A scrolling vertical list; returns the content to add rows to.</summary>
        public static RectTransform Scroll(Transform parent, float minHeight)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var sle = scrollGo.AddComponent<LayoutElement>();
            sle.minHeight = minHeight; sle.flexibleHeight = 1f;
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            Stretch(vrt);
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            scroll.viewport = vrt;
            var content = new GameObject("Rows", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var rows = (RectTransform)content.transform;
            rows.anchorMin = new Vector2(0f, 1f); rows.anchorMax = new Vector2(1f, 1f);
            rows.pivot = new Vector2(0.5f, 1f);
            rows.offsetMin = rows.offsetMax = Vector2.zero;
            var cvg = content.AddComponent<VerticalLayoutGroup>();
            cvg.spacing = 8;
            cvg.childControlWidth = cvg.childControlHeight = true;
            cvg.childForceExpandWidth = true; cvg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rows;
            return rows;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, float height, bool multiLine, int limit)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(false); // TMP_InputField wires itself up on enable; configure it first
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            go.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 1f);

            var area = new GameObject("TextArea", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(12f, 6f); art.offsetMax = new Vector2(-12f, -6f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI ph = Text(area.transform, placeholder, 20f, new Color(1f, 1f, 1f, 0.3f));
            Stretch(ph.rectTransform);
            ph.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            TextMeshProUGUI text = Text(area.transform, "", 20f, Color.white);
            Stretch(text.rectTransform);
            text.alignment = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = multiLine;
            text.richText = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = art;
            field.textComponent = text;
            field.placeholder = ph;
            field.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            field.characterLimit = limit;
            field.richText = false;
            go.SetActive(true);
            return field;
        }

        /// <summary>A canvas for a self-contained panel (sorting order as the other panels use).</summary>
        public static void Canvas(GameObject go, int sortingOrder)
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }
    }
}
