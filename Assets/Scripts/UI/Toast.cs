using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// A short message that appears above the bottom buttons for a few seconds and goes away by
    /// itself: "You now control: Explode". Never blocks a touch. Messages that arrive together are
    /// shown one after another, and the same message twice in a row is shown once.
    /// </summary>
    public class Toast : MonoBehaviour
    {
        private const float ShowSeconds = 2.8f;
        private const float FadeSeconds = 0.25f;

        private static Toast _instance;

        private readonly Queue<string> _queue = new Queue<string>();
        private CanvasGroup     _group;
        private TextMeshProUGUI _text;
        private string _current;
        private float  _shownAt = -1f;

        public static void Show(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_instance == null)
            {
                var go = new GameObject("AuraSync_Toast");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Toast>();
            }
            _instance.Enqueue(message);
            Debug.Log($"[Toast] {message}");
        }

        void Awake() => Build();

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Enqueue(string message)
        {
            if (message == _current && _shownAt >= 0f) { _shownAt = Time.unscaledTime; return; } // refresh, not repeat
            foreach (string m in _queue) if (m == message) return;
            _queue.Enqueue(message);
            if (_shownAt < 0f) Next();
        }

        private void Next()
        {
            if (_queue.Count == 0)
            {
                _current = null;
                _shownAt = -1f;
                _group.alpha = 0f;
                return;
            }
            _current = _queue.Dequeue();
            _text.text = _current;
            _shownAt = Time.unscaledTime;
        }

        void Update()
        {
            if (_shownAt < 0f) return;

            float t = Time.unscaledTime - _shownAt;
            if (t >= ShowSeconds) { Next(); return; }

            float fadeIn  = Mathf.Clamp01(t / FadeSeconds);
            float fadeOut = Mathf.Clamp01((ShowSeconds - t) / FadeSeconds);
            _group.alpha = Mathf.Min(fadeIn, fadeOut);
        }

        private void Build()
        {
            // A pill above Back and the bottom buttons, in the app's theme. Sized to its text.
            var canvas = AuraKit.CreateCanvas(transform, "ToastCanvas", 106); // over the share prompt too
            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();

            var box = AuraKit.Box(safe, "Toast", AuraTheme.A(AuraTheme.Ink, 0.94f), 44f);
            AuraKit.Border(box, AuraTheme.A(AuraTheme.Lavender, 0.6f), 44f);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, AuraViewer.BottomReserve + 30f);
            var h = AuraKit.HStack(box, 0f, 0, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(40, 40, 22, 22);
            var fit = box.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _group = box.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _text = AuraKit.Text(box.transform, "", AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.SemiBold, TextAlignmentOptions.Center);
            _text.enableWordWrapping = true;
            _text.gameObject.AddComponent<ToastWidth>();
        }
    }

    /// <summary>Lets a toast hug short messages and wrap long ones at a comfortable width.</summary>
    public class ToastWidth : MonoBehaviour, ILayoutElement
    {
        private TextMeshProUGUI _t;
        void Awake() => _t = GetComponent<TextMeshProUGUI>();
        public float minWidth => -1;
        public float preferredWidth => _t == null ? -1 : Mathf.Min(_t.GetPreferredValues(_t.text, 9999f, 0f).x, 860f);
        public float flexibleWidth => -1;
        public float minHeight => -1;
        public float preferredHeight => _t == null ? -1 : _t.GetPreferredValues(_t.text, preferredWidth, 0f).y;
        public float flexibleHeight => -1;
        public int layoutPriority => 2;
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
    }
}
