using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.Recording;

namespace AuraSync.UI
{
    /// <summary>
    /// The recordings list and the replay bar (V3 Phase 9).
    ///
    ///   RECORDINGS (Settings)     each recording: name, model, date, length, size and size per
    ///                             minute; PLAY, SHARE, DELETE (tap twice). In a room PLAY is off:
    ///                             "Leave the room to replay".
    ///
    ///   Replay bar (bottom)       REPLAY · name                                  EXIT
    ///                             0:42 ⟵ what just happened  ·  Speaking: Asha
    ///                             [timeline 0:00 ──o────── 10:03]
    ///                             PAUSE   1x   PRESENTER VIEW / FREE CAMERA
    ///
    /// Back (on screen or the phone's) leaves the replay, and the model goes back to how it was.
    /// </summary>
    public class RecordingsUI : MonoBehaviour
    {
        public static RecordingsUI Instance { get; private set; }

        private const string ListNavId   = "recordings";
        private const string ReplayNavId = "replay";

        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color RowColor    = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color DangerColor = new Color(0.55f, 0.18f, 0.18f, 1f);
        private static readonly Color DimText     = new Color(1f, 1f, 1f, 0.6f);

        private GameObject _list;
        private RectTransform _rows;
        private TextMeshProUGUI _listNote, _empty;
        private string _armedDelete;
        private float _armedAt;

        private GameObject _bar;
        private TextMeshProUGUI _barTitle, _barInfo, _playText, _speedText, _camText;
        private RectTransform _timelineRow;
        private Slider _timeline;
        private TextMeshProUGUI _bubble;
        private bool _settingSlider;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            SessionPlayer.OnChanged += OnReplayChanged;
        }

        void OnDestroy()
        {
            SessionPlayer.OnChanged -= OnReplayChanged;
            PanelNavigator.Remove(ListNavId);
            PanelNavigator.Remove(ReplayNavId);
            if (Instance == this) Instance = null;
        }

        // ── List ───────────────────────────────────────────────────────────────

        public void OpenList()
        {
            _list.SetActive(true);
            PanelNavigator.Push(ListNavId, this, CloseList);
            Fill();
        }

        public void CloseList()
        {
            if (_list == null || !_list.activeSelf) return;
            _list.SetActive(false);
            PanelNavigator.Remove(ListNavId);
        }

        private void Fill()
        {
            for (int i = _rows.childCount - 1; i >= 0; i--) Destroy(_rows.GetChild(i).gameObject);

            List<RecordingInfo> all = RecordingStore.List();
            _empty.gameObject.SetActive(all.Count == 0);
            bool inRoom = PhotonNetwork.InRoom;
            _listNote.text = inRoom ? "Leave the room to replay a recording. You can still share one." : "Replays play on this phone only.";

            foreach (RecordingInfo info in all) AddRow(info, inRoom);
        }

        private void AddRow(RecordingInfo info, bool inRoom)
        {
            var row = new GameObject("Recording", typeof(RectTransform));
            row.transform.SetParent(_rows, false);
            row.AddComponent<Image>().color = RowColor;
            var vg = row.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(14, 14, 10, 10);
            vg.spacing = 6;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var name = Text(row.transform, Esc(info.name), 22f, Color.white);
            name.fontStyle = FontStyles.Bold;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.enableWordWrapping = false;

            string meta = $"{Esc(info.header.modelName)}  ·  {info.started:d MMM HH:mm}  ·  {RecordingStore.Clock(info.seconds)}  ·  " +
                          (info.HasVoice ? "with voice  ·  " : "no sound  ·  ") +
                          $"{RecordingStore.SizeText(info.TotalBytes)}" + (info.seconds >= 30f ? $" ({info.KbPerMinute:0} KB/min)" : "");
            var m = Text(row.transform, meta, 17f, DimText);
            m.enableWordWrapping = true;

            var buttons = Row(row.transform, 54f);
            GameObject play = Button(buttons.transform, "PLAY", inRoom ? ButtonColor : AccentColor, () => Play(info), 100f);
            play.GetComponent<Button>().interactable = !inRoom;
            Button(buttons.transform, "SHARE", ButtonColor, () => Toast.Show(RecordingExport.Export(info)), 110f);
            Button(buttons.transform, "HEATMAP", ButtonColor, () => Heatmap(info), 130f);
            GameObject spacer = new GameObject("Space", typeof(RectTransform));
            spacer.transform.SetParent(buttons.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
            TextMeshProUGUI delText = null;
            GameObject del = Button(buttons.transform, "DELETE", DangerColor, () => Delete(info, delText), 110f);
            delText = del.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void Play(RecordingInfo info)
        {
            string problem = SessionPlayer.Play(info);
            if (problem != null) { Toast.Show(problem); return; }
            CloseList();
        }

        /// <summary>A heatmap of who looked at what in the recording (Phase 10), on its own model.</summary>
        private void Heatmap(RecordingInfo info)
        {
            if (info.header.model != AuraSync.Space.SpaceSceneManager.CurrentModelKey)
            {
                Toast.Show($"Open {info.header.modelName} first: the heatmap is painted on the recording's model.");
                return;
            }
            LoadedRecording rec;
            try { rec = RecordingStore.Load(info); }
            catch (System.Exception ex) { Toast.Show("Could not read the recording: " + ex.Message); return; }
            CloseList();
            HeatmapUI.Instance?.OpenFromRecording(rec);
        }

        /// <summary>First tap arms it ("SURE?"), a second within 3 s deletes.</summary>
        private void Delete(RecordingInfo info, TextMeshProUGUI label)
        {
            if (_armedDelete != info.path || Time.unscaledTime - _armedAt > 3f)
            {
                _armedDelete = info.path;
                _armedAt = Time.unscaledTime;
                if (label != null) label.text = "SURE?";
                return;
            }
            _armedDelete = null;
            if (RecordingStore.Delete(info)) Toast.Show("Recording deleted.");
            Fill();
        }

        // ── Replay bar ─────────────────────────────────────────────────────────

        private void OnReplayChanged()
        {
            SessionPlayer p = SessionPlayer.Instance;
            bool on = p != null && p.Current != null;

            if (on && !_bar.activeSelf)
            {
                _bar.SetActive(true);
                PanelNavigator.Push(ReplayNavId, this, () => SessionPlayer.Instance?.Exit());
                BuildTimeline(p.Current.Duration);
                _barTitle.text = "REPLAY  ·  " + Esc(p.Current.info.name);
            }
            else if (!on && _bar.activeSelf)
            {
                _bar.SetActive(false);
                PanelNavigator.Remove(ReplayNavId);
            }
            if (!on) return;

            _playText.text  = p.Playing ? "PAUSE" : (p.T >= p.Current.Duration - 0.05f ? "REPLAY" : "PLAY");
            _speedText.text = p.Speed == 0.5f ? "0.5x" : p.Speed == 2f ? "2x" : "1x";
            _camText.text   = p.FreeCamera ? "FREE CAMERA" : "PRESENTER VIEW";
        }

        void Update()
        {
            SessionPlayer p = SessionPlayer.Instance;
            if (p == null || p.Current == null || !_bar.activeSelf) return;

            if (_timeline != null)
            {
                _settingSlider = true;
                _timeline.value = p.T;
                _settingSlider = false;
                _bubble.text = RecordingStore.Clock(p.T);
            }

            string info = p.Caption;
            if (!string.IsNullOrEmpty(p.Speaking)) info += (info.Length > 0 ? "\n" : "") + "Speaking: " + p.Speaking;
            _barInfo.text = info.Length > 0 ? Esc(info) : " ";

            // PLAY / PAUSE changes by itself at the end.
            string play = p.Playing ? "PAUSE" : (p.T >= p.Current.Duration - 0.05f ? "REPLAY" : "PLAY");
            if (_playText.text != play) _playText.text = play;
        }

        private void BuildTimeline(float duration)
        {
            for (int i = _timelineRow.childCount - 1; i >= 0; i--) Destroy(_timelineRow.GetChild(i).gameObject);
            _timeline = SleekSlider.Create(_timelineRow, 0f, Mathf.Max(0.1f, duration), "0:00", RecordingStore.Clock(duration), out _bubble);
            _timeline.onValueChanged.AddListener(v =>
            {
                if (!_settingSlider) SessionPlayer.Instance?.Seek(v);
            });
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99; // with Settings: over the drawer and chat, under the room panel
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            BuildList();
            BuildBar();
        }

        private void BuildList()
        {
            _list = new GameObject("RecordingsList", typeof(RectTransform));
            _list.transform.SetParent(transform, false);
            var rt = (RectTransform)_list.transform;
            rt.anchorMin = new Vector2(0.04f, 0.22f);
            rt.anchorMax = new Vector2(0.96f, 0.84f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _list.AddComponent<Image>().color = PanelColor;
            _list.AddComponent<TouchBlocker>();
            var vg = _list.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(14, 14, 12, 12);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var head = Row(_list.transform, 52f);
            var title = Text(head.transform, "RECORDINGS", 24f, Color.white);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(head.transform, "X", new Color(0.45f, 0.14f, 0.14f), CloseList, 52f);

            _listNote = Text(_list.transform, "", 17f, DimText);
            _listNote.enableWordWrapping = true;
            _empty = Text(_list.transform, "No recordings yet. Settings > START RECORDING.", 19f, DimText);
            _empty.enableWordWrapping = true;

            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(_list.transform, false);
            var sle = scrollGo.AddComponent<LayoutElement>();
            sle.minHeight = 100f; sle.flexibleHeight = 1f;
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            scroll.viewport = vrt;
            var content = new GameObject("Rows", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _rows = (RectTransform)content.transform;
            _rows.anchorMin = new Vector2(0f, 1f); _rows.anchorMax = new Vector2(1f, 1f);
            _rows.pivot = new Vector2(0.5f, 1f);
            _rows.offsetMin = _rows.offsetMax = Vector2.zero;
            var cvg = content.AddComponent<VerticalLayoutGroup>();
            cvg.spacing = 8;
            cvg.childControlWidth = cvg.childControlHeight = true;
            cvg.childForceExpandWidth = true; cvg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _rows;

            _list.SetActive(false);
        }

        private void BuildBar()
        {
            _bar = new GameObject("ReplayBar", typeof(RectTransform));
            _bar.transform.SetParent(transform, false);
            var rt = (RectTransform)_bar.transform;
            // Above the bottom buttons, full width (the rail is hidden during a replay).
            rt.anchorMin = new Vector2(0.02f, 0.215f);
            rt.anchorMax = new Vector2(0.98f, 0.215f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _bar.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _bar.AddComponent<Image>().color = PanelColor;
            _bar.AddComponent<TouchBlocker>();
            var vg = _bar.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(14, 14, 10, 12);
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var head = Row(_bar.transform, 50f);
            _barTitle = Text(head.transform, "REPLAY", 21f, Color.white);
            _barTitle.fontStyle = FontStyles.Bold;
            _barTitle.overflowMode = TextOverflowModes.Ellipsis;
            _barTitle.enableWordWrapping = false;
            _barTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(head.transform, "EXIT", DangerColor, () => SessionPlayer.Instance?.Exit(), 100f);

            _barInfo = Text(_bar.transform, " ", 18f, new Color(1f, 1f, 1f, 0.85f));
            _barInfo.enableWordWrapping = true;
            var ile = _barInfo.gameObject.AddComponent<LayoutElement>();
            ile.minHeight = 48f;

            var tl = Row(_bar.transform, SleekSlider.RowHeight);
            tl.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            _timelineRow = (RectTransform)tl.transform;

            var ctr = Row(_bar.transform, 58f);
            _playText  = Flex(Button(ctr.transform, "PAUSE", AccentColor, () => SessionPlayer.Instance?.TogglePlay(), 100f)).GetComponentInChildren<TextMeshProUGUI>();
            _speedText = Button(ctr.transform, "1x", ButtonColor, () => SessionPlayer.Instance?.CycleSpeed(), 90f).GetComponentInChildren<TextMeshProUGUI>();
            _camText   = Flex(Button(ctr.transform, "PRESENTER VIEW", ButtonColor, () =>
            {
                SessionPlayer p = SessionPlayer.Instance;
                if (p != null) p.SetFreeCamera(!p.FreeCamera);
            }, 160f)).GetComponentInChildren<TextMeshProUGUI>();

            _bar.SetActive(false);
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        private static GameObject Flex(GameObject go)
        {
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
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
            hg.spacing = 10;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;
            return go;
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

        private static GameObject Button(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float width)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            var img = go.AddComponent<Image>();
            img.color = color;
            var b = go.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Text(go.transform, label, 20f, Color.white);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 20f;
            return go;
        }
    }
}
