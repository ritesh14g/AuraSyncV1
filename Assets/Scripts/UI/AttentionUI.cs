using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.AI;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The buttons and panels of attention awareness (V3 Phase 8):
    ///
    ///   VIEWS (rail, in a room)        a viewer: hide / show the presenter's cone on this phone.
    ///                                  The presenter: a card with two switches, cones on MY PHONE
    ///                                  and cones FOR EVERYONE (the room). Pings always show. The
    ///                                  rail reads "VIEWS (off)" while this phone shows no cones.
    ///   FIND  (rail, presenter only)   the FIND task panel, top centre:
    ///
    ///       FIND TASK                                   X
    ///       [<]        Fan rotor          [>]  [RANDOM]
    ///       [START · CONES ON]   [START · CONES OFF]
    ///
    ///     then, while it runs, the part, the condition, the clock and each person's result, with
    ///     END (and NEW TASK once it is over).
    ///
    ///   The searchers' banner, top centre: "FIND: Fan rotor / Tap it on the model · 0:07", then
    ///   "Found it in 4.2 s". It takes no touches.
    /// </summary>
    public class AttentionUI : MonoBehaviour
    {
        public static AttentionUI Instance { get; private set; }

        private const string ViewsId = "views";
        private const string FindId  = "find";
        private const string NavId   = "find-task";
        private const string ViewsNavId = "views-card";

        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.96f);
        private static readonly Color OnColor     = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color OffColor    = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color DangerColor = new Color(0.55f, 0.18f, 0.18f, 1f);
        private static readonly Color FoundColor  = new Color(0.12f, 0.45f, 0.25f, 0.96f);

        private GameObject _panel, _setup, _live;
        private TextMeshProUGUI _partLabel, _liveTitle, _liveResults, _hint;
        private GameObject _endBtn, _newBtn;

        private GameObject _viewsCard;
        private Image _mineImg, _roomImg;
        private TextMeshProUGUI _mineTxt, _roomTxt;

        private GameObject _banner;
        private Image _bannerBg;
        private TextMeshProUGUI _bannerTitle, _bannerSub;

        private bool _viewsVisible, _findVisible;
        private float _nextText;

        private readonly List<KeyValuePair<string, string>> _parts = new List<KeyValuePair<string, string>>(); // path, label
        private int _partIndex;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void Start() => StartCoroutine(Bind());

        void OnDestroy()
        {
            AttentionSync.OnConesToggled -= RefreshViewsLabel;
            FindTask.OnChanged -= RefreshTask;
            PanelNavigator.Remove(NavId);
            PanelNavigator.Remove(ViewsNavId);
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(ViewsId, "VIEWS", ToggleCones);
            SpaceSessionHUD.Instance.SetSlotVisible(ViewsId, false);
            SpaceSessionHUD.Instance.AddRailButton(FindId, "FIND", TogglePanel);
            SpaceSessionHUD.Instance.SetSlotVisible(FindId, false);

            AttentionSync.OnConesToggled += RefreshViewsLabel;
            FindTask.OnChanged += RefreshTask;
            RefreshViewsLabel();
        }

        void Update()
        {
            SpaceSessionHUD hud = SpaceSessionHUD.Instance;
            bool model = AttentionSync.ModelShown;

            bool views = model && PhotonNetwork.InRoom;
            if (views != _viewsVisible && hud != null && hud.HasSlot(ViewsId))
            {
                _viewsVisible = views;
                hud.SetSlotVisible(ViewsId, views);
                if (!views) CloseViewsCard();
            }
            if (_viewsCard.activeSelf && !ModelPartSelector.IsPresenting) CloseViewsCard();

            bool find = model && ModelPartSelector.IsPresenting;
            if (find != _findVisible && hud != null && hud.HasSlot(FindId))
            {
                _findVisible = find;
                hud.SetSlotVisible(FindId, find);
                if (!find) ClosePanel();
            }

            if (Time.unscaledTime >= _nextText)
            {
                _nextText = Time.unscaledTime + 0.2f;
                if (_panel.activeSelf && _live.activeSelf) RefreshLive();
                RefreshBanner();
            }
        }

        // ── VIEWS ──────────────────────────────────────────────────────────────

        private void ToggleCones()
        {
            // The presenter gets both switches; a viewer just flips their own.
            if (ModelPartSelector.IsPresenting)
            {
                if (_viewsCard.activeSelf) CloseViewsCard();
                else OpenViewsCard();
                return;
            }

            if (AttentionSync.ConesOffForRoom)
            {
                Toast.Show("The presenter has turned view cones off for everyone. \"Look here\" pings still show.");
                return;
            }
            bool hide = !AttentionSync.ConesHidden;
            AttentionSync.SetConesHidden(hide);
            Toast.Show(hide
                ? "The presenter's view cone is hidden on this phone. \"Look here\" pings still show."
                : "When you stop following, a cone shows where the presenter is looking. Hold a finger still on the model to say \"look here\".");
        }

        private void RefreshViewsLabel()
        {
            bool off = AttentionSync.ConesHidden || AttentionSync.ConesOffForRoom;
            SpaceSessionHUD.Instance?.SetSlotLabel(ViewsId, off ? "VIEWS\n(off)" : "VIEWS");
            RefreshViewsCard();
        }

        // ── VIEWS card (presenter) ─────────────────────────────────────────────

        private void OpenViewsCard()
        {
            ClosePanel(); // the FIND panel uses the same spot
            _viewsCard.SetActive(true);
            PanelNavigator.Push(ViewsNavId, this, CloseViewsCard);
            RefreshViewsCard();
        }

        public void CloseViewsCard()
        {
            if (_viewsCard == null || !_viewsCard.activeSelf) return;
            _viewsCard.SetActive(false);
            PanelNavigator.Remove(ViewsNavId);
        }

        private void RefreshViewsCard()
        {
            if (_mineImg == null) return;
            bool mine = !AttentionSync.ConesHidden, room = !AttentionSync.ConesOffForRoom;
            _mineImg.color = mine ? OnColor : OffColor;
            _mineTxt.text  = mine ? "ON" : "OFF";
            _roomImg.color = room ? OnColor : OffColor;
            _roomTxt.text  = room ? "ON" : "OFF";
        }

        private void ToggleMine()
        {
            AttentionSync.SetConesHidden(!AttentionSync.ConesHidden);
            RefreshViewsCard();
        }

        private void ToggleRoom()
        {
            bool on = AttentionSync.ConesOffForRoom; // currently off -> turn on
            AttentionSync.SetConesForRoom(on);
            Toast.Show(on ? "View cones are on for everyone." : "View cones are off for everyone, you included. Pings still show.");
        }

        // ── FIND panel (presenter) ─────────────────────────────────────────────

        private void TogglePanel()
        {
            if (_panel.activeSelf) ClosePanel();
            else OpenPanel();
        }

        private void OpenPanel()
        {
            CloseViewsCard(); // same spot on screen
            BuildPartList();
            _panel.SetActive(true);
            PanelNavigator.Push(NavId, this, ClosePanel);
            RefreshTask();
        }

        /// <summary>Closes the panel; a running task carries on (FIND opens it again).</summary>
        public void ClosePanel()
        {
            if (_panel == null || !_panel.activeSelf) return;
            _panel.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

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
            _partLabel.text = _parts.Count > 0 ? Esc(_parts[_partIndex].Value) : "(no parts)";
        }

        private void StartTask(bool conesOff)
        {
            FindTask task = FindTask.Instance;
            if (task == null || _parts.Count == 0) return;
            if (FindTask.Searchers < 1) { Toast.Show("Nobody else is in the room to search."); return; }

            var part = _parts[_partIndex];
            if (!task.Begin(part.Key, part.Value, conesOff)) Toast.Show("Could not start the task.");
        }

        private void RefreshTask()
        {
            FindTask task = FindTask.Instance;
            bool live = task != null && task.Hosting != null;
            _setup.SetActive(!live);
            _live.SetActive(live);
            if (live) RefreshLive();
            RefreshBanner();
        }

        private void RefreshLive()
        {
            FindTask.Hosted t = FindTask.Instance != null ? FindTask.Instance.Hosting : null;
            if (t == null) return;

            _liveTitle.text = $"Find: {Esc(t.label)}  ·  cones {(t.conesOff ? "OFF" : "ON")}  ·  {Clock(t.Elapsed)}";

            var sb = new StringBuilder();
            foreach (FindTask.Result r in t.results)
            {
                sb.Append(Esc(r.name)).Append("  -  ");
                if (r.found) sb.Append($"<b>{r.seconds:0.0} s</b>");
                else if (r.done) sb.Append(string.IsNullOrEmpty(r.note) ? "not found" : $"not found ({r.note})");
                else sb.Append("searching...");
                if (r.wrongTaps > 0) sb.Append($"  ({r.wrongTaps} wrong)");
                sb.Append('\n');
            }
            if (!t.running) sb.Append(t.pings > 0 ? $"Pings during the task: {t.pings}." : "No pings during the task.");
            _liveResults.text = sb.ToString().TrimEnd();

            _endBtn.SetActive(t.running);
            _newBtn.SetActive(!t.running);
        }

        // ── Banner (searchers) ─────────────────────────────────────────────────

        private void RefreshBanner()
        {
            FindTask.Assigned a = FindTask.Instance != null ? FindTask.Instance.Searching : null;
            if (a == null) { if (_banner.activeSelf) _banner.SetActive(false); return; }

            // Gone a few seconds after the task is over.
            if (a.ended && Time.unscaledTime - a.endedAt > 4f)
            {
                FindTask.Instance.DismissSearch();
                _banner.SetActive(false);
                return;
            }

            if (!_banner.activeSelf) _banner.SetActive(true);
            _bannerTitle.text = $"FIND: {Esc(a.label)}";
            if (a.found)
            {
                _bannerBg.color = FoundColor;
                _bannerSub.text = a.ended ? $"Found it in {a.foundSeconds:0.0} s." : $"Found it in {a.foundSeconds:0.0} s. Waiting for the others...";
            }
            else if (a.ended)
            {
                _bannerBg.color = PanelColor;
                _bannerSub.text = "The task is over.";
            }
            else
            {
                _bannerBg.color = PanelColor;
                _bannerSub.text = $"Tap it on the model  ·  {Clock(a.Elapsed)}" + (a.wrongTaps > 0 ? $"  ·  {a.wrongTaps} wrong" : "");
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 96; // above the Space HUD (95), below the drawer (97) and chat (98)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            BuildPanel();
            BuildViewsCard();
            BuildBanner();
        }

        /// <summary>Top centre: right of the rail, left of the player badges, under the room name.</summary>
        private RectTransform TopCard(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.16f, 0.835f);
            rt.anchorMax = new Vector2(0.70f, 0.835f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            go.AddComponent<Image>().color = color;

            var vg = go.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(14, 14, 10, 12);
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            return rt;
        }

        private void BuildPanel()
        {
            _panel = TopCard("FindPanel", PanelColor).gameObject;
            _panel.AddComponent<TouchBlocker>(); // the model must not turn under the panel

            // Setup page
            _setup = Column(_panel.transform, "Setup");
            var head = Row(_setup.transform, 52f);
            var title = Text(head.transform, "FIND TASK", 24f, Color.white);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(head.transform, "X", new Color(0.45f, 0.14f, 0.14f), ClosePanel, 52f);

            var pick = Row(_setup.transform, 56f);
            Button(pick.transform, "<", OffColor, () => StepPart(-1), 56f);
            _partLabel = Text(pick.transform, "", 22f, Color.white);
            _partLabel.alignment = TextAlignmentOptions.Center;
            _partLabel.fontStyle = FontStyles.Bold;
            _partLabel.enableAutoSizing = true; _partLabel.fontSizeMin = 14f; _partLabel.fontSizeMax = 22f;
            _partLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(pick.transform, ">", OffColor, () => StepPart(1), 56f);
            Button(pick.transform, "RANDOM", OffColor, RandomPart, 110f);

            var go = Row(_setup.transform, 60f);
            Flex(Button(go.transform, "START · CONES ON", OnColor, () => StartTask(false), 100f));
            Flex(Button(go.transform, "START · CONES OFF", OffColor, () => StartTask(true), 100f));

            _hint = Text(_setup.transform,
                "Everyone else is asked to tap this part on their model; each time is logged. " +
                "CONES OFF hides cones and pings on their phones for the task.", 17f, new Color(1f, 1f, 1f, 0.65f));
            _hint.enableWordWrapping = true;

            // Live / results page
            _live = Column(_panel.transform, "Live");
            var lhead = Row(_live.transform, 52f);
            _liveTitle = Text(lhead.transform, "", 22f, Color.white);
            _liveTitle.fontStyle = FontStyles.Bold;
            _liveTitle.enableAutoSizing = true; _liveTitle.fontSizeMin = 14f; _liveTitle.fontSizeMax = 22f;
            _liveTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(lhead.transform, "X", new Color(0.45f, 0.14f, 0.14f), ClosePanel, 52f);

            _liveResults = Text(_live.transform, "", 20f, new Color(1f, 1f, 1f, 0.9f));
            _liveResults.enableWordWrapping = true;
            _liveResults.alignment = TextAlignmentOptions.TopLeft;

            var lbtns = Row(_live.transform, 56f);
            _endBtn = Flex(Button(lbtns.transform, "END", DangerColor, () => FindTask.Instance?.End("ended"), 100f));
            _newBtn = Flex(Button(lbtns.transform, "NEW TASK", OnColor, () => FindTask.Instance?.ClearFinished(), 100f));

            _live.SetActive(false);
            _panel.SetActive(false);
        }

        private void BuildViewsCard()
        {
            _viewsCard = TopCard("ViewsCard", PanelColor).gameObject;
            _viewsCard.AddComponent<TouchBlocker>();

            var head = Row(_viewsCard.transform, 52f);
            var title = Text(head.transform, "VIEW CONES", 24f, Color.white);
            title.fontStyle = FontStyles.Bold;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            Button(head.transform, "X", new Color(0.45f, 0.14f, 0.14f), CloseViewsCard, 52f);

            _mineImg = SwitchRow("On my phone", ToggleMine, out _mineTxt);
            _roomImg = SwitchRow("For everyone in the room", ToggleRoom, out _roomTxt);

            var hint = Text(_viewsCard.transform,
                "You see every viewer's cone. A viewer sees only yours, and only after they stop following you. " +
                "Hold a finger still on the model to say \"look here\".", 17f, new Color(1f, 1f, 1f, 0.65f));
            hint.enableWordWrapping = true;

            _viewsCard.SetActive(false);
        }

        private Image SwitchRow(string label, UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI state)
        {
            var row = Row(_viewsCard.transform, 56f);
            GameObject b = Button(row.transform, "ON", OnColor, onClick, 90f);
            state = b.GetComponentInChildren<TextMeshProUGUI>();
            var t = Text(row.transform, label, 21f, Color.white);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return b.GetComponent<Image>();
        }

        private void BuildBanner()
        {
            var rt = TopCard("FindBanner", PanelColor);
            _banner = rt.gameObject;
            _bannerBg = _banner.GetComponent<Image>();
            _bannerBg.raycastTarget = false; // never takes a touch

            _bannerTitle = Text(_banner.transform, "", 28f, Color.white);
            _bannerTitle.fontStyle = FontStyles.Bold;
            _bannerTitle.alignment = TextAlignmentOptions.Center;
            _bannerTitle.enableWordWrapping = true;
            _bannerSub = Text(_banner.transform, "", 20f, new Color(1f, 1f, 1f, 0.85f));
            _bannerSub.alignment = TextAlignmentOptions.Center;
            _bannerSub.enableWordWrapping = true;

            _banner.SetActive(false);
        }

        private static GameObject Column(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var vg = go.AddComponent<VerticalLayoutGroup>();
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
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
            hg.spacing = 8;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;
            return go;
        }

        private static GameObject Flex(GameObject go)
        {
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;
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
