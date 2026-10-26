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
    /// An AI answer someone shared with the room (V3 Phase 3b): who asked, the question and the
    /// answer, on a card at the top-right under Room, with a thin line to the part it is about.
    ///
    ///   (−)          minimise: the card folds into a "Shared answers" pill in the same corner;
    ///                tap the pill to open it again (a new answer makes the pill pulse)
    ///   (×)          dismiss this answer on this phone only
    ///   ‹ 2 of 3 ›   step through the model's shared answers (a new one jumps to the front)
    ///   Remove       take it down for everyone (the asker and the presenter only)
    ///   Ask more     open your own chat on that part, carrying on from the shared answer
    ///
    /// Hidden while the AI panel is open (Ask more opens it) and during the reassembly challenge.
    /// </summary>
    public class SharedAnswerCard : MonoBehaviour
    {
        public static SharedAnswerCard Instance { get; private set; }

        private Canvas _canvas;
        private RectTransform _card;
        private RectTransform _leader;
        private TextMeshProUGUI _header, _question, _answer, _page;
        private ScrollRect _scroll;
        private Button _prev, _next, _remove;

        private List<SharedAnswer> _visible = new List<SharedAnswer>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private string _currentId = "";

        // Minimised stays minimised for the app session, across models; a new answer only lights
        // the pill up instead of popping the card open over the model again.
        private static bool _minimized;
        private bool _unseenWhileMinimized;
        private GameObject _pill;
        private Image _pillBorder;
        private TextMeshProUGUI _pillText;

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
            _currentId = "";
            Refresh();
        }

        private SharedAnswer Current => _visible.FirstOrDefault(a => a.id == _currentId);

        // ── Content ────────────────────────────────────────────────────

        private void Refresh()
        {
            _visible = SharedAnswers.VisibleFor(SpaceSceneManager.CurrentModelKey);

            SharedAnswer fresh = _visible.LastOrDefault(a => !_seen.Contains(a.id));
            foreach (SharedAnswer a in _visible) _seen.Add(a.id);
            if (fresh != null) { _currentId = fresh.id; if (_minimized) _unseenWhileMinimized = true; }

            if (Current == null && _visible.Count > 0) _currentId = _visible[_visible.Count - 1].id;
            Fill();
        }

        private void Fill()
        {
            SharedAnswer a = Current;
            if (a == null) return;

            string about = string.IsNullOrEmpty(a.path) ? "the whole model"
                         : string.IsNullOrEmpty(a.part) ? "a part" : a.part;
            _header.text = $"<b>{Esc(a.askerName)}</b> <color=#A9A8CC>asked about</color> {Esc(about)}";
            _question.text = Esc(a.question);
            _answer.text = Esc(a.answer);

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
            Fill();
        }

        private void OnDismiss()
        {
            SharedAnswer a = Current;
            if (a == null) return;
            int i = _visible.IndexOf(a);
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

        // ── Showing ────────────────────────────────────────────────────

        void LateUpdate()
        {
            bool show = _visible.Count > 0 && Current != null &&
                        !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) &&
                        !AuraSync.Challenge.ReassemblyGame.IsActive &&
                        !(PartChatUI.Instance != null && PartChatUI.Instance.IsOpen) &&
                        // The room card and Analysis drop down in the same corner.
                        ViewerPanels.OpenId != "room" && ViewerPanels.OpenId != "analysis";

            bool pill = show && _minimized;
            if (_pill.activeSelf != pill) _pill.SetActive(pill);
            if (pill) RefreshPill();

            show &= !_minimized;
            if (_card.gameObject.activeSelf != show) _card.gameObject.SetActive(show);
            if (!show) { _leader.gameObject.SetActive(false); return; }

            DrawLeader(Current);
        }

        private void SetMinimized(bool minimized)
        {
            _minimized = minimized;
            _unseenWhileMinimized = false;
            if (!minimized) Fill();
        }

        private void RefreshPill()
        {
            int n = _visible.Count;
            _pillText.text = _unseenWhileMinimized ? $"Shared answers · {n}  <color=#9894C4>NEW</color>" : $"Shared answers · {n}";
            float k = _unseenWhileMinimized ? Mathf.PingPong(Time.unscaledTime * 1.6f, 1f) : 0f;
            _pillBorder.color = Color.Lerp(AuraTheme.Slate, AuraTheme.Lavender, _unseenWhileMinimized ? 0.4f + 0.6f * k : 0f);
        }

        /// <summary>A thin line from the card to the part it is about (none for the whole model, or off screen).</summary>
        private void DrawLeader(SharedAnswer a)
        {
            bool havePart = a != null && !string.IsNullOrEmpty(a.path) && PartChatUI.TryGetScreenRect(a.path, out _);
            if (!havePart) { _leader.gameObject.SetActive(false); return; }
            PartChatUI.TryGetScreenRect(a.path, out Rect part);

            var c = new Vector3[4];
            _card.GetWorldCorners(c);
            var card = Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
            Vector2 target = part.center;
            if (card.Contains(target)) { _leader.gameObject.SetActive(false); return; }

            Vector2 from = new Vector2(Mathf.Clamp(target.x, card.xMin, card.xMax), Mathf.Clamp(target.y, card.yMin, card.yMax));
            Vector2 d = target - from;
            float scale = AuraKit.CanvasScale;
            _leader.position = (from + target) * 0.5f;
            _leader.sizeDelta = new Vector2(d.magnitude / scale, 4f);
            _leader.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _leader.gameObject.SetActive(true);
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "SharedAnswers", 96, out _canvas); // above the HUD (95), below the drawer (97)

            var leader = AuraKit.Box(_canvas.transform, "Leader", AuraTheme.A(AuraTheme.Lavender, 0.8f), 0f);
            _leader = leader.rectTransform;
            _leader.anchorMin = _leader.anchorMax = new Vector2(0.5f, 0.5f);
            leader.gameObject.SetActive(false);

            var card = AuraKit.Box(safe, "Card", AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(card, AuraTheme.Lavender, AuraTheme.Radius + 6f);
            _card = card.rectTransform;
            _card.anchorMin = _card.anchorMax = new Vector2(1, 1);
            _card.pivot = new Vector2(1, 1);
            _card.anchoredPosition = new Vector2(-AuraViewer.Gutter, -(AuraViewer.TopReserve + 80f));
            _card.sizeDelta = new Vector2(660f, 640f);
            var v = AuraKit.VStack(card, 14f);
            v.padding = new RectOffset(30, 22, 18, 22);
            v.childForceExpandHeight = false;

            var header = AuraKit.Rect(card.transform, "Header");
            AuraKit.HStack(header, 10f).childForceExpandHeight = false;
            AuraKit.Size(header, -1, 88f);
            AuraKit.Icon(header, AuraIcons.AI, 40f, AuraTheme.Lavender);
            _header = AuraKit.Text(header, "", AuraTheme.SizeCaption, AuraTheme.Text);
            _header.richText = true;
            _header.enableWordWrapping = false;
            AuraKit.Size(_header, 0f, -1, 1f);
            AuraWidgets.Round(header, AuraIcons.Back, 80f, false, () => SetMinimized(true), out Image minIcon);
            minIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, 90f); // a down chevron: fold away
            AuraWidgets.Round(header, AuraIcons.Close, 80f, false, OnDismiss, out _);

            _question = AuraKit.Text(card.transform, "", AuraTheme.SizeSmall, AuraTheme.Lavender, AuraWeight.SemiBold);
            _question.enableWordWrapping = true;

            var answerHost = AuraKit.Rect(card.transform, "Answer");
            AuraKit.Size(answerHost, -1, 160f, -1, 1f);
            var content = AuraWidgets.Scroll(answerHost, out _scroll, 0f);
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _answer = AuraKit.Text(content, "", AuraTheme.SizeSmall, AuraTheme.Text);
            _answer.enableWordWrapping = true;

            var footer = AuraKit.Rect(card.transform, "Footer");
            AuraKit.HStack(footer, 10f).childForceExpandHeight = false;
            AuraKit.Size(footer, -1, 88f);
            _prev = AuraWidgets.Round(footer, AuraIcons.Back, 80f, false, () => Step(-1), out _);
            _page = AuraKit.Text(footer, "", AuraTheme.SizeCaption - 2f, AuraTheme.TextDim, AuraWeight.Regular, TextAlignmentOptions.Center);
            _page.enableWordWrapping = false;
            AuraKit.Size(_page, 120f);
            _next = AuraWidgets.Round(footer, AuraIcons.Forward, 80f, false, () => Step(1), out _);
            AuraKit.Flex(AuraKit.Rect(footer, "Space"));
            AuraWidgets.Chip(footer, "Remove", AuraIcons.Trash, false, OnRemove, out _remove, 84f);
            var rt = _remove.GetComponentInChildren<TextMeshProUGUI>();
            if (rt != null) rt.color = AuraTheme.Rose;
            AuraWidgets.Chip(footer, "Ask more", AuraIcons.AI, true, OnAskMore, out _, 84f);

            card.gameObject.SetActive(false);

            // The minimised card: a pill in the same corner.
            var pill = AuraKit.Box(safe, "Pill", AuraTheme.A(AuraTheme.Ink, 0.9f), 40f, true);
            _pillBorder = AuraKit.Border(pill, AuraTheme.Slate, 40f);
            AuraKit.Pin(pill.rectTransform, Vector2.one, Vector2.one,
                        new Vector2(-AuraViewer.Gutter, -(AuraViewer.TopReserve + 80f)), new Vector2(460, 92));
            var ph = AuraKit.HStack(pill, 12f, 0, TextAnchor.MiddleCenter);
            ph.padding = new RectOffset(22, 22, 0, 0);
            AuraKit.Icon(pill.transform, AuraIcons.AI, 38f, AuraTheme.Lavender);
            _pillText = AuraKit.Text(pill.transform, "Shared answers", AuraTheme.SizeCaption, AuraTheme.Text, AuraWeight.SemiBold);
            _pillText.richText = true;
            _pillText.enableWordWrapping = false;
            AuraKit.MakeButton(pill.gameObject, pill, () => SetMinimized(false));
            _pill = pill.gameObject;
            _pill.SetActive(false);
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
