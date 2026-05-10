using System;
using UnityEngine;

// ── Compatibility: works whether New Input System, Old, or Both is active ────
// We use a compile-time check. If the New Input System package is installed,
// we use EnhancedTouch. Otherwise we fall back to Input.GetTouch.
// This way the same script compiles cleanly in both AR (New IS) and
// non-AR (Old IS or Both) projects.
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch      = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

namespace AuraSync.Space
{
    /// <summary>
    /// Handles all touch interactions for the 3D Virtual Space viewer.
    /// - Single finger drag  → Rotate model
    /// - Two finger pinch    → Scale (zoom)
    /// - Two finger drag     → Pan model
    ///
    /// Works on:
    ///   • Non-AR Android devices (Old Input System / Both)
    ///   • AR Android devices (New Input System / Both)
    ///
    /// SETUP:
    ///   Attach to any persistent GameObject in the Space scene.
    ///   Assign modelRoot (the loaded model's root transform) in Inspector,
    ///   or call SetModel(transform) after loading.
    /// </summary>
    public class SpaceInteractionController : MonoBehaviour
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Target")]
        [Tooltip("Root transform of the loaded 3D model. Assign after load.")]
        [SerializeField] private Transform modelRoot;

        [Header("Rotation")]
        [SerializeField] private float rotationSpeed    = 0.3f;
        [SerializeField] private float rotationSmoothing = 8f;

        [Header("Zoom (Pinch)")]
        [SerializeField] private float zoomSpeed        = 0.01f;
        [SerializeField] private float minScale         = 0.05f;
        [SerializeField] private float maxScale         = 5f;

        [Header("Pan (Two-finger drag)")]
        [SerializeField] private float panSpeed         = 0.003f;

        // ── State ──────────────────────────────────────────────────────────────
        private Vector2 _lastSinglePos;
        private float   _lastPinchDistance;
        private Vector2 _lastTwoFingerMidpoint;
        private bool    _wasTwoFinger = false;

        // Smoothed rotation deltas
        private float   _targetRotX;
        private float   _targetRotY;
        private float   _currentRotX;
        private float   _currentRotY;

        // ── Lifecycle ──────────────────────────────────────────────────────────

#if ENABLE_INPUT_SYSTEM
        void OnEnable()  { EnhancedTouchSupport.Enable();  }
        void OnDisable() { EnhancedTouchSupport.Disable(); }
#endif

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>Assign the loaded model at runtime after GLTFast finishes.</summary>
        public void OnModelLoaded(Transform root)
        {
            modelRoot = root;
            // Reset accumulated rotation so the model starts upright
            _targetRotX  = 0f;
            _targetRotY  = 0f;
            _currentRotX = 0f;
            _currentRotY = 0f;
        }

        // ── Update ─────────────────────────────────────────────────────────────

        void Update()
        {
            if (modelRoot == null) return;

            int touchCount = GetTouchCount();

            if (touchCount == 0)
            {
                _wasTwoFinger = false;
                return;
            }

            if (touchCount == 1 && !_wasTwoFinger)
            {
                HandleSingleTouch();
            }
            else if (touchCount >= 2)
            {
                _wasTwoFinger = true;
                HandleTwoFingerTouch();
            }

            // Apply smoothed rotation every frame
            ApplySmoothedRotation();
        }

        // ── Single Touch: Rotate ───────────────────────────────────────────────

        private void HandleSingleTouch()
        {
            Vector2 pos   = GetTouchPosition(0);
            Vector2 delta = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            var touches = Touch.activeTouches;
            if (touches.Count > 0)
            {
                var t = touches[0];
                if (t.phase == TouchPhase.Began)
                {
                    _lastSinglePos = pos;
                    return;
                }
                delta = pos - _lastSinglePos;
                _lastSinglePos = pos;
            }
#else
            UnityEngine.Touch t = Input.GetTouch(0);
            if (t.phase == UnityEngine.TouchPhase.Began)
            {
                _lastSinglePos = pos;
                return;
            }
            delta = t.deltaPosition;
#endif

            // Accumulate rotation targets (not applied directly — smoothed in LateUpdate)
            _targetRotY += delta.x * rotationSpeed;   // horizontal swipe → Y axis
            _targetRotX -= delta.y * rotationSpeed;   // vertical swipe   → X axis

            // Clamp vertical tilt so model doesn't flip
            _targetRotX = Mathf.Clamp(_targetRotX, -85f, 85f);
        }

        private void ApplySmoothedRotation()
        {
            _currentRotX = Mathf.LerpAngle(_currentRotX, _targetRotX, Time.deltaTime * rotationSmoothing);
            _currentRotY = Mathf.LerpAngle(_currentRotY, _targetRotY, Time.deltaTime * rotationSmoothing);

            modelRoot.rotation = Quaternion.Euler(_currentRotX, _currentRotY, 0f);
        }

        // ── Two Touch: Pinch Zoom + Pan ────────────────────────────────────────

        private void HandleTwoFingerTouch()
        {
            Vector2 pos0 = GetTouchPosition(0);
            Vector2 pos1 = GetTouchPosition(1);

            bool isBegan = IsTouchBegan(0) || IsTouchBegan(1);

            if (isBegan)
            {
                // Store initial state for both gestures
                _lastPinchDistance      = Vector2.Distance(pos0, pos1);
                _lastTwoFingerMidpoint  = (pos0 + pos1) * 0.5f;
                return;
            }

            // ── Pinch to scale ─────────────────────────────────────────────────
            float currentDistance = Vector2.Distance(pos0, pos1);
            float pinchDelta      = currentDistance - _lastPinchDistance;
            _lastPinchDistance    = currentDistance;

            float currentScale    = modelRoot.localScale.x;
            float newScale        = Mathf.Clamp(currentScale + pinchDelta * zoomSpeed, minScale, maxScale);
            modelRoot.localScale  = Vector3.one * newScale;

            // ── Two-finger pan ─────────────────────────────────────────────────
            Vector2 midpoint     = (pos0 + pos1) * 0.5f;
            Vector2 panDelta     = midpoint - _lastTwoFingerMidpoint;
            _lastTwoFingerMidpoint = midpoint;

            modelRoot.position  += new Vector3(panDelta.x * panSpeed, panDelta.y * panSpeed, 0f);
        }

        // ── Input Abstraction Layer ────────────────────────────────────────────
        // These methods compile correctly whether New IS, Old IS, or Both is active.

        private int GetTouchCount()
        {
#if ENABLE_INPUT_SYSTEM
            return Touch.activeTouches.Count;
#else
            return Input.touchCount;
#endif
        }

        private Vector2 GetTouchPosition(int index)
        {
#if ENABLE_INPUT_SYSTEM
            var touches = Touch.activeTouches;
            if (index < touches.Count)
                return touches[index].screenPosition;
            return Vector2.zero;
#else
            if (index < Input.touchCount)
                return Input.GetTouch(index).position;
            return Vector2.zero;
#endif
        }

        private bool IsTouchBegan(int index)
        {
#if ENABLE_INPUT_SYSTEM
            var touches = Touch.activeTouches;
            if (index < touches.Count)
                return touches[index].phase == TouchPhase.Began;
            return false;
#else
            if (index < Input.touchCount)
                return Input.GetTouch(index).phase == UnityEngine.TouchPhase.Began;
            return false;
#endif
        }
    }
}
