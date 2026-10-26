using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// A full-screen page over the viewer (Reports, Recordings, the quiz): a title row, a scrolling
    /// body, and the two fixed buttons in their usual places, Back bottom-left and the page's main
    /// action bottom-right. Built once; opening and closing only fades and nudges it.
    /// </summary>
    public class AuraPage
    {
        public Canvas Canvas { get; private set; }
        public RectTransform Content { get; private set; }   // the safe-area column the page builds in
        public RectTransform Header { get; private set; }
        public TextMeshProUGUI Title { get; private set; }
        public TextMeshProUGUI Subtitle { get; private set; }
        public RectTransform HeaderRight { get; private set; } // chips beside the title
        public RectTransform Body { get; private set; }        // scrolling content (vertical layout)
        public ScrollRect Scroll { get; private set; }
        public Button Back { get; private set; }
        public Button Primary { get; private set; }
        public bool IsOpen { get; private set; }

        private CanvasGroup _group;
        private RectTransform _inner;
        private TextMeshProUGUI _primaryLabel;
        private Image _primaryFill;

        public static AuraPage Create(Transform owner, string name, int sortingOrder, Action onBack)
        {
            var p = new AuraPage();
            p.Build(owner, name, sortingOrder, onBack);
            return p;
        }

        private void Build(Transform owner, string name, int order, Action onBack)
        {
            Canvas = AuraKit.CreateCanvas(owner, name, order);
            _group = Canvas.gameObject.AddComponent<CanvasGroup>();

            var bg = AuraKit.Box(Canvas.transform, "Background", AuraTheme.A(AuraTheme.Ink, 0.97f), 0f, true);
            AuraKit.Fill(bg.rectTransform);

            var safe = AuraKit.Rect(Canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();
            _inner = AuraKit.Fill(AuraKit.Rect(safe, "Inner"));

            Content = AuraKit.Rect(_inner, "Content");
            AuraKit.Fill(Content, AuraViewer.Gutter + 8f, AuraViewer.BottomReserve, AuraViewer.Gutter + 8f, AuraViewer.Gutter + 10f);
            Content.gameObject.AddComponent<MatchParentWidthStretch>().max = 1100f;
            var v = AuraKit.VStack(Content, 22f);
            v.childForceExpandHeight = false;

            Header = AuraKit.Rect(Content, "Header");
            AuraKit.HStack(Header, 16f).childForceExpandHeight = false;
            var words = AuraKit.Rect(Header, "Words");
            AuraKit.VStack(words, 4f);
            AuraKit.Size(words, 0f, -1, 1f);
            Title = AuraKit.Text(words, "", AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold);
            Title.enableWordWrapping = false;
            Title.overflowMode = TextOverflowModes.Ellipsis;
            Subtitle = AuraKit.Text(words, "", AuraTheme.SizeSmall - 2f, AuraTheme.TextDim);
            Subtitle.richText = true;
            HeaderRight = AuraKit.Rect(Header, "Actions");
            AuraKit.HStack(HeaderRight, 12f, 0, TextAnchor.MiddleRight).childForceExpandHeight = false;

            var host = AuraKit.Rect(Content, "BodyHost");
            AuraKit.Size(host, -1, 200f, -1, 1f);
            Body = AuraWidgets.Scroll(host, out ScrollRect scroll, 16f);
            Scroll = scroll;
            Scroll.movementType = ScrollRect.MovementType.Clamped;

            Back = AuraKit.Pill(_inner, "Back", AuraIcons.Back, AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Text,
                                onBack, AuraViewer.BackHeight, AuraTheme.Slate);
            AuraKit.Pin((RectTransform)Back.transform, Vector2.zero, Vector2.zero,
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));

            Primary = AuraKit.Pill(_inner, "Action", AuraIcons.Ok, AuraTheme.Lavender, AuraTheme.Ink, null, AuraViewer.BackHeight);
            AuraKit.Pin((RectTransform)Primary.transform, new Vector2(1, 0), new Vector2(1, 0),
                        new Vector2(-AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(340, AuraViewer.BackHeight));
            _primaryLabel = Primary.GetComponentInChildren<TextMeshProUGUI>();
            _primaryFill = Primary.GetComponent<Image>();
            Primary.gameObject.SetActive(false);

            SetShown(false, instant: true);
        }

        /// <summary>Shows the bottom-right action with this label (null hides it).</summary>
        public void SetPrimary(string label, Action onTap, bool enabled = true, bool danger = false)
        {
            Primary.onClick.RemoveAllListeners();
            if (string.IsNullOrEmpty(label)) { Primary.gameObject.SetActive(false); return; }
            Primary.gameObject.SetActive(true);
            _primaryLabel.text = label;
            Primary.interactable = enabled;
            _primaryFill.color = !enabled ? AuraTheme.A(AuraTheme.Slate, 0.45f) : danger ? AuraTheme.Rose : AuraTheme.Lavender;
            if (onTap != null) Primary.onClick.AddListener(() => onTap());
        }

        public void Open() => SetShown(true, false);
        public void Close() => SetShown(false, false);

        private void SetShown(bool on, bool instant)
        {
            IsOpen = on;
            _group.blocksRaycasts = on;
            _group.interactable = on;
            if (instant)
            {
                _group.alpha = on ? 1f : 0f;
                _inner.anchoredPosition = Vector2.zero;
                Canvas.enabled = on;
                return;
            }
            if (on)
            {
                Canvas.enabled = true;
                _inner.anchoredPosition = new Vector2(0, -40f);
                AuraMotion.Move(_inner, Vector2.zero, _group, 1f);
            }
            else
            {
                AuraMotion.Move(_inner, new Vector2(0, -40f), _group, 0f, AuraMotion.Duration,
                                () => { if (!IsOpen && Canvas != null) Canvas.enabled = false; });
            }
        }

        /// <summary>Removes the body's rows (detached at once, so the new ones lay out cleanly).</summary>
        public void ClearBody() => AuraKit.ClearChildren(Body);

        public void ScrollToTop()
        {
            if (Scroll != null) Scroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>Keeps a stretched column no wider than max on wide screens, centred.</summary>
    public class MatchParentWidthStretch : MonoBehaviour
    {
        public float max = 1100f;
        private float _last = -1f;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float w = parent.rect.width;
            if (Mathf.Approximately(w, _last)) return;
            _last = w;
            float extra = Mathf.Max(0f, (w - rt.offsetMin.x + rt.offsetMax.x) - max) * 0.5f;
            if (extra <= 0f) return;
            rt.offsetMin = new Vector2(rt.offsetMin.x + extra, rt.offsetMin.y);
            rt.offsetMax = new Vector2(rt.offsetMax.x - extra, rt.offsetMax.y);
        }
    }
}
