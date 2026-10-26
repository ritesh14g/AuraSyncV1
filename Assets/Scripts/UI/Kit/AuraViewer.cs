using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// The 3D viewer's shared layout, so every panel lands in its agreed place:
    ///
    ///   top-left   Parts + Focus         top-right  Analysis + Room (+ mic in a room)
    ///   right edge, lower half: the tool dock (Simulate, Section, AI, Notes, Play, Record)
    ///   bottom-left Back
    ///
    /// Tool panels open as a sheet along the bottom that stops short of the dock; Parts slides in
    /// from the left and AI from the right. All in canvas units of the 1080 x 1920 Expand canvas.
    /// </summary>
    public static class AuraViewer
    {
        public const float Gutter      = 36f;
        public const float TopButton   = 132f;  // icon-over-label buttons in the top corners
        public const float BackHeight  = 120f;
        public const float DockWidth   = 150f;  // visible part of a dock tab
        public const float DockGap     = 14f;

        /// <summary>Space kept clear at the top of the safe area (the top buttons and a gap).</summary>
        public const float TopReserve = Gutter + TopButton + 30f;

        /// <summary>Space kept clear at the bottom of the safe area (Back and a gap).</summary>
        public const float BottomReserve = Gutter + BackHeight + 26f;

        /// <summary>
        /// A screen-space canvas for a viewer panel, with a safe-area child to build in. The sort
        /// order keeps the layering the V3 panels already agreed on.
        /// </summary>
        public static RectTransform CreateCanvas(Transform owner, string name, int sortingOrder, out Canvas canvas)
        {
            canvas = AuraKit.CreateCanvas(owner, name, sortingOrder);
            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();
            return safe;
        }

        /// <summary>
        /// A tool sheet: along the bottom, from the left gutter to just short of the dock, as tall
        /// as its content (it grows upwards). Children go straight into the returned rect, which has
        /// a vertical layout. Wrap it with <see cref="AuraSlidePanel"/> to slide it in and out.
        /// </summary>
        public static RectTransform ToolSheet(RectTransform safe, string name)
        {
            var sheet = AuraKit.Box(safe, name, AuraTheme.A(AuraTheme.Indigo, 0.95f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(sheet, AuraTheme.Slate, AuraTheme.Radius + 6f);
            var rt = sheet.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(Gutter, BottomReserve);
            rt.offsetMax = new Vector2(-(DockWidth + DockGap + 6f), BottomReserve + 300f);
            var v = AuraKit.VStack(sheet, 22f);
            v.padding = new RectOffset(36, 36, 30, 32);
            AuraKit.FitHeight(sheet);
            return rt;
        }

        /// <summary>The sheet's title row: title on the left, a small status on the right.</summary>
        public static TextMeshProUGUI SheetHeader(RectTransform sheet, string title, out TextMeshProUGUI status)
        {
            var row = AuraKit.Rect(sheet, "Header");
            AuraKit.HStack(row, 12f);
            AuraKit.Size(row, -1, 62f);
            var t = AuraKit.Text(row, title, AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            t.enableWordWrapping = false;
            AuraKit.Flex(t);
            status = AuraKit.Caps(row, "", AuraTheme.Lavender);
            status.alignment = TextAlignmentOptions.MidlineRight;
            AuraKit.Size(status, 360f);
            return t;
        }

        /// <summary>Slides a tool sheet up from just below its place and fades it in.</summary>
        public static AuraSlidePanel SlideUp(RectTransform sheet)
        {
            var shown = sheet.anchoredPosition;
            return AuraSlidePanel.Attach(sheet, shown, shown + new Vector2(0, -70f));
        }

        /// <summary>Height the sheet will slide down by when hidden.</summary>
        public static Vector2 SheetHiddenOffset => new Vector2(0, -60f);
    }

    /// <summary>
    /// Only one viewer panel open at a time. A panel says when it opens (with how to close it) and
    /// when it closes; opening another closes the first. The HUD lights up the matching dock tab.
    /// </summary>
    public static class ViewerPanels
    {
        private static string _openId;
        private static Action _close;

        public static string OpenId => _openId;

        /// <summary>Raised with the open panel's id, or null when none is open.</summary>
        public static event Action<string> OnChanged;

        public static void Opened(string id, Action close)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (_openId != null && _openId != id)
            {
                var previous = _close;
                _openId = null;
                _close = null;
                try { previous?.Invoke(); }
                catch (Exception ex) { Debug.LogWarning($"[ViewerPanels] Closing previous panel: {ex.Message}"); }
            }
            _openId = id;
            _close = close;
            OnChanged?.Invoke(_openId);
        }

        public static void Closed(string id)
        {
            if (_openId != id) return;
            _openId = null;
            _close = null;
            OnChanged?.Invoke(null);
        }

        /// <summary>Closes whatever is open (e.g. entering Focus or starting a game).</summary>
        public static void CloseAll()
        {
            var c = _close;
            _openId = null;
            _close = null;
            try { c?.Invoke(); } catch { }
            OnChanged?.Invoke(null);
        }

        public static bool IsOpen(string id) => _openId == id;

        /// <summary>
        /// A tap on empty space around the model: closes the open panel, if any. Returns true when
        /// it closed one (the tap is then used up, so it does not also clear the selection).
        /// </summary>
        public static bool CloseOnEmptyTap()
        {
            if (_openId == null) return false;
            CloseAll();
            return true;
        }

        /// <summary>Forgets the open panel without calling it (the viewer is going away).</summary>
        public static void Reset()
        {
            _openId = null;
            _close = null;
        }
    }

    /// <summary>Caps a rect's height to a fraction of its canvas, keeping its bottom edge.</summary>
    public class MaxHeightOfScreen : MonoBehaviour
    {
        public float fraction = 0.55f;
        public RectTransform content;  // optional: size to this content's height (+ padding)
        public float padding = 0f;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var root = canvas.rootCanvas.transform as RectTransform;
            float max = root.rect.height * fraction;
            float want = content != null ? content.rect.height + padding : rt.rect.height;
            float h = Mathf.Min(want, max);
            float cur = rt.offsetMax.y - rt.offsetMin.y;
            if (Mathf.Abs(cur - h) > 0.5f) rt.offsetMax = new Vector2(rt.offsetMax.x, rt.offsetMin.y + h);
        }
    }
}
