using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The card beside a part that shows an AI answer someone shared with the room (V3 Phase 3b):
    /// who asked, the question, the answer, with a line to the part it is about.
    ///
    ///   _           minimize: the card folds into a small SHARED ANSWERS pill in the top-right
    ///               corner so the model is clear; tap the pill to open it again (added 2026-10-13)
    ///   X           dismiss this answer on this phone only
    ///   ‹ 2 of 3 ›  step through the model's shared answers (a new one jumps to the front)
    ///   REMOVE      take it down for everyone (the asker and the presenter only)
    ///   ASK MORE    open your own chat on that part, carrying on from the shared answer
    ///
    /// The card sits on the side of the part with more room and follows it as the model turns.
    /// An answer about the whole model, or a part that is off screen or hidden by isolation, gets
    /// the top-right corner and no line. Hidden while the chat panel is open (ASK MORE opens it) and
    /// during the reassembly challenge.
    /// </summary>
    public class SharedAnswerCard : MonoBehaviour
    {
        public static SharedAnswerCard Instance { get; private set; }

        private const float CardW = 500f, CardH = 560f; // reference units (1080 x 1920)

        private static readonly Color CardColor   = new Color(0.08f, 0.10f, 0.15f, 0.96f);
        private static readonly Color HeaderColor = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color RemoveColor = new Color(0.50f, 0.18f, 0.18f, 1f);
        private static readonly Color LeaderColor = new Color(0.62f, 0.56f, 1f, 0.85f);

        private Canvas          _canvas;
        private RectTransform   _card;
        private RectTransform   _leader;
        private TextMeshProUGUI _header;
        private TextMeshProUGUI _question;
        private TextMeshProUGUI _answer;
        private TextMeshProUGUI _page;
        private ScrollRect      _scroll;
        private Button          _prev, _next, _remove;

        private List<SharedAnswer> _visible = new List<SharedAnswer>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private string _currentId = "";

        // Minimized stays minimized for the app session, across models; a new answer only lights
        // the pill up instead of popping the card open over the model again.
        private static bool _minimized;
        private bool _unseenWhileMinimized;
        private GameObject      _pill;
        private Image           _pillImage;
        private TextMeshProUGUI _pillText;
        private int    _side = -1; // 0 = left of the part, 1 = right; -1 = not placed yet

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            SharedAnswers.OnChanged += Refresh;
            SharedStateBus.OnChange += OnStateChange;
        }

        void Start() => Refresh();

        void OnDestroy()
        {
            SharedAnswers.OnChanged -= Refresh;
            SharedStateBus.OnChange -= OnStateChange;
            if (Instance == this) Instance = null;
        }

        private void OnStateChange(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;
            _side = -1;
            _currentId = "";
            Refresh();
        }

        private SharedAnswer Current => _visible.FirstOrDefault(a => a.id == _currentId);

        // ── Content ────────────────────────────────────────────────────────────

        private void Refresh()
        {
            _visible = SharedAnswers.VisibleFor(SpaceSceneManager.CurrentModelKey);

            // A newly shared answer comes to the front (the newest, if several arrived together).
            SharedAnswer fresh = _visible.LastOrDefault(a => !_seen.Contains(a.id));
            foreach (SharedAnswer a in _visible) _seen.Add(a.id);
            if (fresh != null) { _currentId = fresh.id; _side = -1; if (_minimized) _unseenWhileMinimized = true; }

            if (Current == null && _visible.Count > 0) _currentId = _visible[_visible.Count - 1].id;
            Fill();
        }

        private void Fill()
        {
            SharedAnswer a = Current;
            if (a == null) return;

            string about = string.IsNullOrEmpty(a.path) ? "the whole model"
                         : string.IsNullOrEmpty(a.part) ? "a part" : a.part;
            _header.text   = $"<b>{Esc(a.askerName)}</b> asked about {Esc(about)}";
            _question.text = Esc(a.question);
            _answer.text   = Esc(a.answer);

            int index = _visible.IndexOf(a);
            _page.text = _visible.Count > 1 ? $"{index + 1} of {_visible.Count}" : "";
            _prev.gameObject.SetActive(_visible.Count > 1);
            _next.gameObject.SetActive(_visible.Count > 1);
            _prev.interactable = index > 0;
            _next.interactable = index < _visible.Count - 1;
            _remove.gameObject.SetActive(SharedAnswers.CanRemove(a));

            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private void Step(int delta)
        {
            int i = _visible.IndexOf(Current) + delta;
            if (i < 0 || i >= _visible.Count) return;
            _currentId = _visible[i].id;
            _side = -1;
            Fill();
        }

        private void OnDismiss()
        {
            SharedAnswer a = Current;
            if (a == null) return;
            int i = _visible.IndexOf(a);
            // Show the next older one rather than jumping to the newest.
            _currentId = i > 0 ? _visible[i - 1].id : "";
            SharedAnswers.Dismiss(a.id);
        }

        private void OnRemove()
        {
            SharedAnswer a = Current;
            if (a == null) return;
            SharedAnswers.Remove(a);
            Toast.Show("Removed for everyone.");
        }

        private void OnAskMore()
        {
            SharedAnswer a = Current;
            if (a == null || PartChatUI.Instance == null) return;
            PartChatUI.Instance.OpenShared(a);
        }

        // ── Placement ──────────────────────────────────────────────────────────

        void LateUpdate()
        {
            bool show = _visible.Count > 0 && Current != null &&
                        !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) &&
                        !AuraSync.Challenge.ReassemblyGame.IsActive &&
                        !(PartChatUI.Instance != null && PartChatUI.Instance.IsOpen);

            bool pill = show && _minimized;
            if (_pill.activeSelf != pill) _pill.SetActive(pill);
            if (pill) RefreshPill();

            show &= !_minimized;
            if (_card.gameObject.activeSelf != show) _card.gameObject.SetActive(show);
            if (!show) { _leader.gameObject.SetActive(false); return; }

            Place(Current);
        }

        private void SetMinimized(bool minimized)
        {
            _minimized = minimized;
            _unseenWhileMinimized = false;
            if (!minimized) { _side = -1; Fill(); }
        }

        private void RefreshPill()
        {
            int n = _visible.Count;
            _pillText.text = _unseenWhileMinimized ? $"SHARED ANSWERS ({n})  <color=#FFD133>NEW</color>" : $"SHARED ANSWERS ({n})";

            // A gentle pulse while something new is waiting, so it is noticed without covering anything.
            float k = _unseenWhileMinimized ? Mathf.PingPong(Time.unscaledTime * 1.6f, 1f) : 0f;
            _pillImage.color = Color.Lerp(HeaderColor, new Color(0.55f, 0.48f, 1f, 1f), k);
        }

        private void Place(SharedAnswer a)
        {
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float w = CardW * scale, h = CardH * scale;
            float W = Screen.width, H = Screen.height;

            bool havePart = !string.IsNullOrEmpty(a.path) && PartChatUI.TryGetScreenRect(a.path, out _);
            Rect part = default;
            if (havePart) PartChatUI.TryGetScreenRect(a.path, out part);

            float x, y;
            if (!havePart)
            {
                x = W * 0.98f - w;
                y = H * 0.83f - h;
            }
            else
            {
                // The side with more room, with some stickiness so it does not flip near the middle.
                float nx = part.center.x / W;
                if (_side < 0) _side = nx < 0.5f ? 1 : 0;
                else if (nx < 0.38f) _side = 1;
                else if (nx > 0.62f) _side = 0;

                float gap = 24f * scale;
                x = _side == 1 ? part.xMax + gap : part.xMin - gap - w;
                y = part.center.y - h * 0.5f;
            }

            // Clear of the left rail, the top bar and the bottom buttons.
            x = Mathf.Clamp(x, W * 0.15f, Mathf.Max(W * 0.15f, W * 0.98f - w));
            y = Mathf.Clamp(y, H * 0.22f, Mathf.Max(H * 0.22f, H * 0.84f - h));
            _card.position = new Vector3(x, y, 0f); // pivot is bottom-left; overlay world space = pixels

            DrawLeader(havePart, part, Rect.MinMaxRect(x, y, x + w, y + h), scale);
        }

        private void DrawLeader(bool havePart, Rect part, Rect card, float scale)
        {
            Vector2 target = part.center;
            if (!havePart || card.Contains(target))
            {
                _leader.gameObject.SetActive(false);
                return;
            }

            Vector2 from = new Vector2(Mathf.Clamp(target.x, card.xMin, card.xMax), Mathf.Clamp(target.y, card.yMin, card.yMax));
            Vector2 d = target - from;
            _leader.position = (from + target) * 0.5f;
            _leader.sizeDelta = new Vector2(d.magnitude / scale, 4f);
            _leader.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _leader.gameObject.SetActive(true);
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 96; // above the Space HUD (95), below the parts drawer (97) and chat (98)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var leaderGo = new GameObject("Leader", typeof(RectTransform));
            leaderGo.transform.SetParent(transform, false);
            _leader = (RectTransform)leaderGo.transform;
            var li = leaderGo.AddComponent<Image>();
            li.color = LeaderColor;
            li.raycastTarget = false;
            leaderGo.SetActive(false);

            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(transform, false);
            _card = (RectTransform)cardGo.transform;
            _card.anchorMin = _card.anchorMax = Vector2.zero;
            _card.pivot = Vector2.zero;
            _card.sizeDelta = new Vector2(CardW, CardH);
            cardGo.AddComponent<Image>().color = CardColor;
            cardGo.AddComponent<TouchBlocker>(); // reading or scrolling the card never turns the model

            var vg = cardGo.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(0, 0, 0, 12);
            vg.spacing = 8;
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            // Header: who asked about what, and X.
            var header = Row(cardGo.transform, "Header", 56f, HeaderColor, new RectOffset(14, 8, 6, 6));
            _header = MakeText(header.transform, "Who", 20f, Color.white, flexWidth: 1f);
            _header.enableWordWrapping = false;
            _header.overflowMode = TextOverflowModes.Ellipsis;
            MakeButton(header.transform, "Minimize", "_", new Color(0f, 0f, 0f, 0.25f), () => SetMinimized(true), 52f);
            MakeButton(header.transform, "X", "X", new Color(0f, 0f, 0f, 0.25f), OnDismiss, 52f);

            var qRow = Row(cardGo.transform, "QuestionRow", 64f, Color.clear, new RectOffset(16, 16, 0, 0));
            _question = MakeText(qRow.transform, "Question", 21f, new Color(0.75f, 0.82f, 1f, 1f), flexWidth: 1f);
            _question.fontStyle = FontStyles.Bold;
            _question.enableAutoSizing = true;
            _question.fontSizeMin = 15f;
            _question.fontSizeMax = 21f;

            BuildAnswerScroll(cardGo.transform);

            // Footer: paging, remove, ask more.
            var footer = Row(cardGo.transform, "Footer", 52f, Color.clear, new RectOffset(12, 12, 0, 0));
            _prev = MakeButton(footer.transform, "Prev", "<", ButtonColor, () => Step(-1), 52f);
            _page = MakeText(footer.transform, "Page", 18f, new Color(1f, 1f, 1f, 0.6f), flexWidth: 1f);
            _page.alignment = TextAlignmentOptions.Center;
            _page.enableWordWrapping = false;
            _next = MakeButton(footer.transform, "Next", ">", ButtonColor, () => Step(1), 52f);
            _remove = MakeButton(footer.transform, "Remove", "REMOVE", RemoveColor, OnRemove, 110f);
            MakeButton(footer.transform, "AskMore", "ASK MORE", HeaderColor, OnAskMore, 124f);

            cardGo.SetActive(false);

            BuildPill();
        }

        /// <summary>The minimized card: a small pill clear of the top bar, the corner row and the note banner.</summary>
        private void BuildPill()
        {
            _pill = new GameObject("MinimizedPill", typeof(RectTransform));
            _pill.transform.SetParent(transform, false);
            var rt = (RectTransform)_pill.transform;
            rt.anchorMin = new Vector2(0.62f, 0.705f);
            rt.anchorMax = new Vector2(0.98f, 0.755f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _pillImage = _pill.AddComponent<Image>();
            _pillImage.color = HeaderColor;
            var b = _pill.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = _pillImage;
            b.onClick.AddListener(() => SetMinimized(false));

            _pillText = MakeText(_pill.transform, "Label", 20f, Color.white, 0f);
            _pillText.fontStyle = FontStyles.Bold;
            _pillText.alignment = TextAlignmentOptions.Center;
            _pillText.enableAutoSizing = true;
            _pillText.fontSizeMin = 13f;
            _pillText.fontSizeMax = 20f;
            _pillText.enableWordWrapping = false;
            var trt = _pillText.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 0f); trt.offsetMax = new Vector2(-10f, 0f);

            _pill.SetActive(false);
        }

        private void BuildAnswerScroll(Transform parent)
        {
            var scrollGo = new GameObject("Answer", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 120f;

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 28f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(16f, 0f); vrt.offsetMax = new Vector2(-16f, 0f);
            viewport.AddComponent<RectMask2D>();
            var vimg = viewport.AddComponent<Image>();
            vimg.color = new Color(0f, 0f, 0f, 0.001f); // something to catch the scroll drag
            _scroll.viewport = vrt;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            var cvg = content.AddComponent<VerticalLayoutGroup>();
            cvg.childControlWidth = true; cvg.childControlHeight = true;
            cvg.childForceExpandWidth = true; cvg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = crt;

            _answer = MakeText(content.transform, "Text", 21f, Color.white, flexWidth: 0f);
            _answer.enableWordWrapping = true;
        }

        private static GameObject Row(Transform parent, string name, float height, Color color, RectOffset padding)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;
            if (color.a > 0f) go.AddComponent<Image>().color = color;
            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.padding = padding;
            hg.spacing = 8;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = true; hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;
            return go;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size, Color color, float flexWidth)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.richText = true;
            t.raycastTarget = false;
            if (flexWidth > 0f) go.AddComponent<LayoutElement>().flexibleWidth = flexWidth;
            return t;
        }

        private static Button MakeButton(Transform parent, string name, string label, Color color,
                                     UnityEngine.Events.UnityAction onClick, float width)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var t = MakeText(go.transform, "Label", 19f, Color.white, 0f);
            t.text = label;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.enableAutoSizing = true;
            t.fontSizeMin = 13f;
            t.fontSizeMax = 19f;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 0f); rt.offsetMax = new Vector2(-4f, 0f);
            return btn;
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
