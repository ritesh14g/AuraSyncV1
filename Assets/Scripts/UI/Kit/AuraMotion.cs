using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.UI
{
    /// <summary>
    /// The one animation style for every panel: a short ease-out on unscaled time, moving only a
    /// panel's position and transparency (never its layout), so slides stay smooth on a mid-range
    /// phone. Starting a new animation on the same target cancels the old one, so rapid taps never
    /// leave two animations fighting.
    /// </summary>
    public class AuraMotion : MonoBehaviour
    {
        public const float Duration = 0.22f;

        private static AuraMotion _runner;
        private readonly Dictionary<int, Coroutine> _running = new Dictionary<int, Coroutine>();

        private static AuraMotion Runner
        {
            get
            {
                if (_runner != null) return _runner;
                var go = new GameObject("AuraSync_Motion");
                DontDestroyOnLoad(go);
                _runner = go.AddComponent<AuraMotion>();
                return _runner;
            }
        }

        /// <summary>Moves a rect to an anchored position and fades its group, together.</summary>
        public static void Move(RectTransform rt, Vector2 to, CanvasGroup group = null, float alpha = -1f,
                                float duration = Duration, Action done = null)
        {
            if (rt == null) { done?.Invoke(); return; }
            Runner.Begin(rt.GetInstanceID(), Tween(rt, to, group, alpha, duration, done));
        }

        /// <summary>Fades a group only.</summary>
        public static void Fade(CanvasGroup group, float alpha, float duration = Duration, Action done = null)
        {
            if (group == null) { done?.Invoke(); return; }
            Runner.Begin(group.GetInstanceID(), FadeOnly(group, alpha, duration, done));
        }

        /// <summary>Cancels any animation on this rect or group (it stays where it is).</summary>
        public static void Stop(UnityEngine.Object target)
        {
            if (target == null || _runner == null) return;
            int id = target.GetInstanceID();
            if (_runner._running.TryGetValue(id, out Coroutine c) && c != null) _runner.StopCoroutine(c);
            _runner._running.Remove(id);
        }

        private void Begin(int id, IEnumerator routine)
        {
            if (_running.TryGetValue(id, out Coroutine old) && old != null) StopCoroutine(old);
            _running[id] = StartCoroutine(Wrap(id, routine));
        }

        private IEnumerator Wrap(int id, IEnumerator routine)
        {
            yield return routine;
            _running.Remove(id);
        }

        public static float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        private static IEnumerator Tween(RectTransform rt, Vector2 to, CanvasGroup group, float alpha,
                                         float duration, Action done)
        {
            Vector2 from = rt.anchoredPosition;
            float a0 = group != null ? group.alpha : 0f;
            float t = 0f;
            while (t < 1f)
            {
                if (rt == null) yield break;
                t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
                float e = Ease(t);
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                if (group != null && alpha >= 0f) group.alpha = Mathf.Lerp(a0, alpha, e);
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = to;
            if (group != null && alpha >= 0f) group.alpha = alpha;
            done?.Invoke();
        }

        private static IEnumerator FadeOnly(CanvasGroup g, float alpha, float duration, Action done)
        {
            float a0 = g.alpha, t = 0f;
            while (t < 1f)
            {
                if (g == null) yield break;
                t += Time.unscaledDeltaTime / Mathf.Max(0.01f, duration);
                g.alpha = Mathf.Lerp(a0, alpha, Ease(t));
                yield return null;
            }
            if (g != null) g.alpha = alpha;
            done?.Invoke();
        }
    }

    /// <summary>
    /// A panel that is built once and then only slides and fades. It sits on its own nested canvas,
    /// so moving it never makes the rest of the screen rebuild. Hidden = transparent and not taking
    /// taps; it is never deactivated, which would force a full rebuild on the next open.
    /// </summary>
    public class AuraSlidePanel : MonoBehaviour
    {
        public RectTransform rect;
        public CanvasGroup group;
        public Vector2 shownPos;
        public Vector2 hiddenPos;
        public bool IsShown { get; private set; }

        public static AuraSlidePanel Attach(RectTransform rt, Vector2 shown, Vector2 hidden)
        {
            var p = rt.gameObject.AddComponent<AuraSlidePanel>();
            p.rect = rt;
            p.group = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
            if (rt.GetComponent<Canvas>() == null)
            {
                rt.gameObject.AddComponent<Canvas>();
                rt.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }
            p.shownPos = shown;
            p.hiddenPos = hidden;
            p.SetInstant(false);
            return p;
        }

        public void SetInstant(bool shown)
        {
            AuraMotion.Stop(rect);
            IsShown = shown;
            rect.anchoredPosition = shown ? shownPos : hiddenPos;
            group.alpha = shown ? 1f : 0f;
            group.blocksRaycasts = shown;
            group.interactable = shown;
        }

        public void Show(Action done = null)
        {
            IsShown = true;
            group.blocksRaycasts = true;
            group.interactable = true;
            AuraMotion.Move(rect, shownPos, group, 1f, AuraMotion.Duration, done);
        }

        public void Hide(Action done = null)
        {
            IsShown = false;
            group.blocksRaycasts = false;
            group.interactable = false;
            AuraMotion.Move(rect, hiddenPos, group, 0f, AuraMotion.Duration, done);
        }
    }

    /// <summary>Keeps a rect on the screen's safe area (clear of notches and rounded corners).</summary>
    public class AuraSafeArea : MonoBehaviour
    {
        private Rect _last;

        void OnEnable() => Apply(true);
        void Update() => Apply(false);

        private void Apply(bool force)
        {
            Rect sa = Screen.safeArea;
            if (!force && sa == _last) return;
            _last = sa;
            var rt = (RectTransform)transform;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            rt.anchorMin = new Vector2(sa.xMin / w, sa.yMin / h);
            rt.anchorMax = new Vector2(sa.xMax / w, sa.yMax / h);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
