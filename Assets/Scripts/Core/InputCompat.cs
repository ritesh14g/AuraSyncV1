using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using NewTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#endif

namespace AuraSync.Core
{
    /// <summary>
    /// One place to read touches, the mouse and the scroll wheel, whichever input backend the
    /// project is set to.
    ///
    /// WHY THIS EXISTS: calling UnityEngine.Input.* while Player Settings &gt; Active Input Handling
    /// is "Input System Package (New)" throws InvalidOperationException. An exception thrown part
    /// way through Update() silently aborts the rest of that Update, so code below it never runs.
    /// That is what made the model only mirror the presenter while a finger was held on the screen:
    /// with no finger down, the legacy mouse read threw and the mirroring code below it was skipped.
    ///
    /// Every script must read input through this class, never through UnityEngine.Input directly.
    /// </summary>
    public static class InputCompat
    {
#if ENABLE_INPUT_SYSTEM
        // Checked rather than remembered: EnhancedTouchSupport is reference counted, and other
        // scripts (GestureDetector) release their reference when their scene unloads.
        private static void EnsureEnhancedTouch()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }
#endif

        /// <summary>
        /// True on platforms with a real mouse. On phones the mouse APIs mirror the touchscreen,
        /// so reading both would handle every touch twice.
        /// </summary>
        public static bool HasMouse
        {
            get
            {
#if UNITY_ANDROID || UNITY_IOS
                return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePresent;
#elif ENABLE_INPUT_SYSTEM
                return Mouse.current != null;
#else
                return false;
#endif
            }
        }

        // ── Touch ───────────────────────────────────────────────────────────────

        public static int TouchCount
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                EnsureEnhancedTouch();
                return NewTouch.activeTouches.Count;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.touchCount;
#else
                return 0;
#endif
            }
        }

        public static Vector2 GetTouchPosition(int index)
        {
#if ENABLE_INPUT_SYSTEM
            EnsureEnhancedTouch();
            var touches = NewTouch.activeTouches;
            return index < touches.Count ? touches[index].screenPosition : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return index < Input.touchCount ? Input.GetTouch(index).position : Vector2.zero;
#else
            return Vector2.zero;
#endif
        }

        public static bool IsTouchBegan(int index)
        {
#if ENABLE_INPUT_SYSTEM
            EnsureEnhancedTouch();
            var touches = NewTouch.activeTouches;
            return index < touches.Count &&
                   touches[index].phase == UnityEngine.InputSystem.TouchPhase.Began;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return index < Input.touchCount &&
                   Input.GetTouch(index).phase == UnityEngine.TouchPhase.Began;
#else
            return false;
#endif
        }

        // ── Mouse ───────────────────────────────────────────────────────────────

        public static Vector2 MousePosition
        {
            get
            {
                if (!HasMouse) return Vector2.zero;
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>button: 0 = left, 1 = right, 2 = middle.</summary>
        public static bool GetMouseButton(int button)
        {
            if (!HasMouse) return false;
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            switch (button)
            {
                case 0:  return m.leftButton.isPressed;
                case 1:  return m.rightButton.isPressed;
                case 2:  return m.middleButton.isPressed;
                default: return false;
            }
#else
            return false;
#endif
        }

        public static bool GetMouseButtonDown(int button)
        {
            if (!HasMouse) return false;
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            switch (button)
            {
                case 0:  return m.leftButton.wasPressedThisFrame;
                case 1:  return m.rightButton.wasPressedThisFrame;
                case 2:  return m.middleButton.wasPressedThisFrame;
                default: return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Scroll wheel movement this frame, normalised to roughly the legacy -1..1 range.</summary>
        public static float ScrollDelta
        {
            get
            {
                if (!HasMouse) return 0f;
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetAxis("Mouse ScrollWheel");
#elif ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null ? m.scroll.ReadValue().y / 120f : 0f;
#else
                return 0f;
#endif
            }
        }

        // ── Back ────────────────────────────────────────────────────────────────

        /// <summary>
        /// The Android back button (or back gesture) this frame. Both input backends report it as
        /// the Escape key, which is also what stands in for it in the Editor.
        /// </summary>
        public static bool BackPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current;
                return k != null && k.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetKeyDown(KeyCode.Escape);
#else
                return false;
#endif
            }
        }

        // ── Combined pointer (a finger, or the left mouse button) ────────────────

        /// <summary>Where the single active pointer is, if exactly one is down.</summary>
        public static bool TryGetPointer(out Vector2 position)
        {
            if (TouchCount == 1)
            {
                position = GetTouchPosition(0);
                return true;
            }
            if (TouchCount == 0 && GetMouseButton(0))
            {
                position = MousePosition;
                return true;
            }
            position = Vector2.zero;
            return false;
        }
    }
}
