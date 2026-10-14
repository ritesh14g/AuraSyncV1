using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// Settings → HEATMAP (V3 Phase 10), above the bottom buttons:
    ///
    ///     HEATMAP · live                                     X
    ///     [<]  Everyone  [>]                  whose attention: everyone, or one person
    ///     never ■   least ▬▬▬▬▬▬▬▬ most                      the legend
    ///     MOST                      LEAST
    ///     1. Fan rotor      34      1. Spinner        0
    ///     ...
    ///     [ SAVE TO REPORTS ]  [ DONE ]
    ///
    /// The model is recoloured while it is open (HeatmapView) and back to normal on DONE. Live
    /// counts refresh every 2 s. A heatmap can also be made from a recording (RECORDINGS →
    /// HEATMAP), on the recording's model.
    /// </summary>
    public class HeatmapUI : MonoBehaviour
    {
        public static HeatmapUI Instance { get; private set; }

        private const string NavId = "heatmap";
        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.36f, 0.30f, 0.85f, 1f);

        private GameObject _panel;
        private TextMeshProUGUI _title, _who, _top, _bottom;

        private List<PersonStats> _people = new List<PersonStats>();
        private int _personIndex = -1; // -1 = everyone
        private bool _fromRecording;
        private string _recordingName = "";
        private float _nextRefresh;

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
            if (c.key != StateKeys.ModelLoaded || !_panel.activeSelf) return;
            // The same model again (the full version replacing the preview): paint it again.
            // Another model: a recording's heatmap no longer fits it.
            if (_fromRecording) Close();
            else Refresh();
        }

        public bool IsOpen => _panel != null && _panel.activeSelf;

        // ── Opening ────────────────────────────────────────────────────────────

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
            PartNotesPanelUI.Instance?.CloseList();
            SectionCutUI.Instance?.CloseToolbar();
            SimulationPanelUI.Instance?.Hide();
            if (!_panel.activeSelf) PanelNavigator.Push(NavId, this, Close);
            _panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            HeatmapView.Clear();
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
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

        // ── Content ────────────────────────────────────────────────────────────

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

            _title.text = _fromRecording ? $"HEATMAP  ·  {Esc(_recordingName)}" : "HEATMAP  ·  live";
            _who.text = Esc(WhoName) + (_personIndex >= 0 && !_people[_personIndex].present ? "  (left)" : "");

            List<KeyValuePair<string, float>> ranked = HeatmapView.Rank(scores);
            _top.text = Lines(ranked.Take(5), "MOST");
            _bottom.text = Lines(Enumerable.Reverse(ranked).Take(5), "LEAST");
        }

        private static string Lines(IEnumerable<KeyValuePair<string, float>> rows, string head)
        {
            var sb = new StringBuilder($"<b>{head}</b>\n");
            int i = 1;
            foreach (var kv in rows)
                sb.Append($"{i++}. {Esc(HeatmapView.Label(kv.Key))}  <color=#FFFFFF99>{(kv.Value > 0f ? kv.Value.ToString("0") : "never")}</color>\n");
            if (i == 1) sb.Append("<color=#FFFFFF99>No parts.</color>");
            return sb.ToString().TrimEnd();
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
            Toast.Show("Heatmap saved to REPORTS.");
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────────────

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

            _panel = new GameObject("HeatmapPanel", typeof(RectTransform));
            _panel.transform.SetParent(transform, false);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = new Vector2(0.16f, 0.215f);
            rt.anchorMax = new Vector2(0.98f, 0.215f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _panel.AddComponent<Image>().color = PanelColor;
            _panel.AddComponent<TouchBlocker>();
            var vg = _panel.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(14, 14, 10, 12);
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var head = UIBits.Row(_panel.transform, 50f);
            _title = UIBits.Text(head.transform, "HEATMAP", 22f, Color.white);
            _title.fontStyle = FontStyles.Bold;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _title.enableWordWrapping = false;
            UIBits.Flex(_title.gameObject);
            UIBits.Button(head.transform, "X", new Color(0.45f, 0.14f, 0.14f), Close, 52f);

            var who = UIBits.Row(_panel.transform, 54f);
            UIBits.Button(who.transform, "<", ButtonColor, () => Step(-1), 56f);
            _who = UIBits.Text(who.transform, "Everyone", 22f, Color.white);
            _who.alignment = TextAlignmentOptions.Center;
            _who.fontStyle = FontStyles.Bold;
            UIBits.Flex(_who.gameObject);
            UIBits.Button(who.transform, ">", ButtonColor, () => Step(1), 56f);

            // Legend: grey = never, then the ramp from least to most.
            var legend = UIBits.Row(_panel.transform, 34f);
            var never = new GameObject("Never", typeof(RectTransform), typeof(Image));
            never.transform.SetParent(legend.transform, false);
            never.GetComponent<Image>().color = HeatmapView.Never;
            var nle = never.AddComponent<LayoutElement>(); nle.minWidth = nle.preferredWidth = 30f;
            UIBits.Text(legend.transform, "never", 17f, new Color(1f, 1f, 1f, 0.7f)).gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;
            UIBits.Text(legend.transform, "least", 17f, new Color(1f, 1f, 1f, 0.7f)).gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;
            var ramp = new GameObject("Ramp", typeof(RectTransform), typeof(Image));
            ramp.transform.SetParent(legend.transform, false);
            ramp.GetComponent<Image>().sprite = RampSprite();
            UIBits.Flex(ramp);
            UIBits.Text(legend.transform, "most", 17f, new Color(1f, 1f, 1f, 0.7f)).gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;

            var lists = new GameObject("Lists", typeof(RectTransform));
            lists.transform.SetParent(_panel.transform, false);
            var lh = lists.AddComponent<HorizontalLayoutGroup>();
            lh.spacing = 12;
            lh.childControlWidth = lh.childControlHeight = true;
            lh.childForceExpandWidth = true; lh.childForceExpandHeight = false;
            _top = UIBits.Text(lists.transform, "", 18f, Color.white);
            _bottom = UIBits.Text(lists.transform, "", 18f, Color.white);
            foreach (var t in new[] { _top, _bottom })
            {
                t.alignment = TextAlignmentOptions.TopLeft;
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.richText = true;
            }

            var hint = UIBits.Text(_panel.transform,
                "Picks and notes and AI questions count most, then time selected or isolated, then time looked at.",
                15f, new Color(1f, 1f, 1f, 0.55f));
            hint.enableWordWrapping = true;

            var btns = UIBits.Row(_panel.transform, 58f);
            UIBits.Flex(UIBits.Button(btns.transform, "SAVE TO REPORTS", ButtonColor, SaveReport, 100f));
            UIBits.Flex(UIBits.Button(btns.transform, "DONE", AccentColor, Close, 100f));

            _panel.SetActive(false);
        }

        private static Sprite _ramp;
        private static Sprite RampSprite()
        {
            if (_ramp != null) return _ramp;
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, HeatmapView.Ramp(x / 63f));
            tex.Apply();
            return _ramp = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
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
