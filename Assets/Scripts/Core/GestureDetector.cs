using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace AuraSync.Core
{
    public class GestureDetector : MonoBehaviour
    {
        [Header("Gesture Thresholds")]
        [SerializeField] private float longPressThreshold = 0.6f;
        [SerializeField] private float dragThreshold      = 20f;

        [Header("Double Tap")]
        [SerializeField] private float doubleTapWindow = 0.30f; // seconds between two taps
        [SerializeField] private float doubleTapRadius = 60f;   // max pixel distance between taps

        // ── Events ──
        public event Action<Vector2>          OnTap;
        public event Action<Vector2>          OnDoubleTap;
        public event Action<Vector2>          OnLongPress;
        public event Action<Vector2, Vector2> OnDrag;
        public event Action                   OnDragEnd;

        // ── Touch tracking ──
        private bool    _isTracking;
        private float   _touchStartTime;
        private Vector2 _touchStartPos;
        private bool    _isDragging;
        private bool    _longPressFired;

        // ── Pending single-tap (held until double-tap window expires) ──
        private bool    _pendingTap;
        private float   _pendingTapTime;
        private Vector2 _pendingTapPos;

        void OnEnable()  => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            // Flush the pending single tap once the double-tap window has expired
            // and no second tap arrived to upgrade it to a double-tap.
            if (_pendingTap && Time.time - _pendingTapTime > doubleTapWindow)
            {
                _pendingTap = false;
                OnTap?.Invoke(_pendingTapPos);
                Debug.Log($"[AuraSync] Single tap fired at {_pendingTapPos}");
            }

            var touches = Touch.activeTouches;

            if (touches.Count == 0)
            {
                if (_isTracking) HandleTouchEnd();
                return;
            }

            var touch = touches[0];

            switch (touch.phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    HandleTouchStart(touch);
                    break;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    HandleTouchContinue(touch);
                    break;
                case UnityEngine.InputSystem.TouchPhase.Ended:
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    HandleTouchEnd();
                    break;
            }
        }

        private void HandleTouchStart(Touch touch)
        {
            var mode = AppStateManager.Instance != null
                ? AppStateManager.Instance.CurrentMode
                : AppMode.Idle;

            if (mode == AppMode.Idle && IsPointerOverUI(touch.screenPosition))
            {
                Debug.Log("[AuraSync] Gesture: Touch over UI, blocking");
                return;
            }

            // While placing, the world takes taps through see-through UI, but never a tap on a
            // button: pressing Back (or the mode chip) must not also drop the item there.
            if (mode != AppMode.Idle && IsPointerOverButton(touch.screenPosition))
            {
                Debug.Log("[AuraSync] Gesture: Touch on a button during placement, blocking");
                return;
            }

            _isTracking     = true;
            _touchStartTime = Time.time;
            _touchStartPos  = touch.screenPosition;
            _isDragging     = false;
            _longPressFired = false;

            Debug.Log($"[AuraSync] Gesture: Touch started at {_touchStartPos}, mode={mode}");
        }

        private void HandleTouchContinue(Touch touch)
        {
            if (!_isTracking) return;

            float distance = Vector2.Distance(touch.screenPosition, _touchStartPos);
            float elapsed  = Time.time - _touchStartTime;

            if (distance > dragThreshold)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _pendingTap = false; // drag cancels any pending single-tap
                }
                OnDrag?.Invoke(touch.screenPosition, touch.delta);
            }
            else if (!_isDragging && !_longPressFired && elapsed >= longPressThreshold)
            {
                _longPressFired = true;
                _pendingTap     = false; // long-press cancels pending tap
                OnLongPress?.Invoke(touch.screenPosition);
            }
        }

        private void HandleTouchEnd()
        {
            if (!_isTracking) return;

            if (_isDragging)
            {
                OnDragEnd?.Invoke();
            }
            else if (!_longPressFired)
            {
                float elapsed = Time.time - _touchStartTime;
                if (elapsed < longPressThreshold)
                {
                    bool isSecondTap = _pendingTap
                        && (Time.time - _pendingTapTime) <= doubleTapWindow
                        && Vector2.Distance(_touchStartPos, _pendingTapPos) <= doubleTapRadius;

                    if (isSecondTap)
                    {
                        // Second tap arrived in time — fire double tap, cancel queued single tap
                        _pendingTap = false;
                        OnDoubleTap?.Invoke(_touchStartPos);
                        Debug.Log($"[AuraSync] Double tap fired at {_touchStartPos}");
                    }
                    else
                    {
                        // First tap — store it and wait for potential second tap
                        _pendingTap     = true;
                        _pendingTapTime = Time.time;
                        _pendingTapPos  = _touchStartPos;
                    }
                }
            }

            _isTracking     = false;
            _isDragging     = false;
            _longPressFired = false;
        }

        /// <summary>True when the point is over anything clickable (a Selectable such as a Button).</summary>
        public static bool IsPointerOverButton(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var eventData = new PointerEventData(EventSystem.current) { position = screenPosition };
            var results   = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            foreach (var r in results)
            {
                if (r.gameObject != null && r.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null)
                    return true;
            }
            return false;
        }

        public static bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var eventData = new PointerEventData(EventSystem.current) { position = screenPosition };
            var results   = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            return results.Count > 0;
        }
    }
}