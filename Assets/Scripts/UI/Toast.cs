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
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 106; // over the share prompt too: an answer's result shows on top
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            var box = new GameObject("Box", typeof(RectTransform));
            box.transform.SetParent(transform, false);
            var rt = (RectTransform)box.transform;
            rt.anchorMin = new Vector2(0.10f, 0.215f);
            rt.anchorMax = new Vector2(0.90f, 0.275f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var bg = box.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.09f, 0.92f);
            bg.raycastTarget = false;

            _group = box.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable   = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(box.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(18f, 4f);
            trt.offsetMax = new Vector2(-18f, -4f);

            _text = textGo.AddComponent<TextMeshProUGUI>();
            _text.fontSize  = 28f;
            _text.color     = Color.white;
            _text.alignment = TextAlignmentOptions.Center;
            _text.enableAutoSizing = true;
            _text.fontSizeMin = 18f;
            _text.fontSizeMax = 28f;
            _text.raycastTarget = false;
        }
    }
}
