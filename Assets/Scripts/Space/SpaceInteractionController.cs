using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch      = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

namespace AuraSync.Space
{
    /// <summary>
    /// Handles touch/mouse interactions for 3D model viewer and real-time Admin sync.
    ///
    /// SYNC LOCK PATTERN:
    ///   - IsSyncLocked = true  → Viewer model smoothly mirrors Admin's position, rotation, scale in real-time.
    ///   - Viewer dragging model → BreakSync() unlocks viewer for manual inspection.
    ///   - Catch Up button     → SnapToAdmin() instantly snaps model to Admin and re-locks sync.
    /// </summary>
    public class SpaceInteractionController : MonoBehaviour
    {
        public static SpaceInteractionController Instance { get; private set; }

        [Header("Target")]
        [SerializeField] private Transform modelRoot;

        [Header("Rotation")]
        [SerializeField] private float rotationSpeed     = 0.3f;
        [SerializeField] private float rotationSmoothing = 10f;

        [Header("Zoom (Pinch)")]
        [SerializeField] private float zoomSpeed         = 0.01f;
        [SerializeField] private float minScale          = 0.05f;
        [SerializeField] private float maxScale          = 5f;

        [Header("Pan (Two-finger drag)")]
        [SerializeField] private float panSpeed          = 0.003f;

        [Header("Smoothing")]
        [Tooltip("Lerp speed for mirroring remote Admin transforms.")]
        [SerializeField] private float remoteInterpolationSpeed = 18f;

        /// <summary>
        /// True = viewer is locked to Admin's transform (mirror mode).
        /// False = viewer is controlling model freely.
        /// Admin always has IsSyncLocked = false.
        /// </summary>
        public bool IsSyncLocked { get; private set; } = true;

        public event Action<bool> OnSyncLockChanged;

        private Vector3    _lastAdminPos;
        private Quaternion _lastAdminRot = Quaternion.identity;
        private Vector3    _lastAdminScale = Vector3.one;
        private bool       _hasAdminTransform = false;

        private Vector2 _lastSinglePos;
        private float   _lastPinchDistance;
        private Vector2 _lastTwoFingerMidpoint;
        private bool    _wasTwoFinger = false;
        private Vector2 _lastMousePos;

        private float   _targetRotX;
        private float   _targetRotY;
        private float   _currentRotX;
        private float   _currentRotY;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

#if ENABLE_INPUT_SYSTEM
        void OnEnable()  { EnhancedTouchSupport.Enable();  }
        void OnDisable() { EnhancedTouchSupport.Disable(); }
#endif

        public void OnModelLoaded(Transform root)
        {
            modelRoot = root;
            _targetRotX  = 0f;
            _targetRotY  = 0f;
            _currentRotX = 0f;
            _currentRotY = 0f;

            bool isAdmin = AuraSync.Networking.ZoneManager.Instance != null &&
                           AuraSync.Networking.ZoneManager.Instance.IsAdmin;
            IsSyncLocked = !isAdmin;
        }

        public void ApplyRemoteTransform(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            _lastAdminPos   = pos;
            _lastAdminRot   = rot;
            _lastAdminScale = scale;
            _hasAdminTransform = true;
        }

        public void ApplyRemoteTransform(Vector3 pos, Vector3 rotEuler, Vector3 scale)
        {
            ApplyRemoteTransform(pos, Quaternion.Euler(rotEuler), scale);
        }

        public void BreakSync()
        {
            bool isAdmin = AuraSync.Networking.ZoneManager.Instance != null &&
                           AuraSync.Networking.ZoneManager.Instance.IsAdmin;
            if (isAdmin) return; // Admin never sync-locks

            if (IsSyncLocked)
            {
                IsSyncLocked = false;
                OnSyncLockChanged?.Invoke(false);
                Debug.Log("[SpaceInteractionController] Viewer broke sync with Admin.");
            }
        }

        public void SnapToAdmin()
        {
            if (modelRoot == null) return;

            if (_hasAdminTransform)
            {
                modelRoot.position   = _lastAdminPos;
                modelRoot.rotation   = _lastAdminRot;
                modelRoot.localScale = _lastAdminScale;

                Vector3 euler = _lastAdminRot.eulerAngles;
                float rx = euler.x > 180f ? euler.x - 360f : euler.x;
                float ry = euler.y > 180f ? euler.y - 360f : euler.y;

                _targetRotX  = rx;
                _targetRotY  = ry;
                _currentRotX = rx;
                _currentRotY = ry;
            }

            IsSyncLocked = true;
            OnSyncLockChanged?.Invoke(true);
            Debug.Log("[SpaceInteractionController] Viewer snapped to Admin — mirror re-locked.");
        }

        void Update()
        {
            if (modelRoot == null) return;

            int touchCount = GetTouchCount();

            if (touchCount > 0)
            {
                if (touchCount == 1 && !_wasTwoFinger)
                {
                    HandleSingleTouch();
                }
                else if (touchCount >= 2)
                {
                    _wasTwoFinger = true;
                    HandleTwoFingerTouch();
                }
            }
            else
            {
                _wasTwoFinger = false;
                HandleMouseInput();
            }

            // Real-time mirror mode for locked viewers
            if (IsSyncLocked)
            {
                if (_hasAdminTransform)
                {
                    float speed = Time.deltaTime * remoteInterpolationSpeed;
                    modelRoot.position   = Vector3.Lerp(modelRoot.position, _lastAdminPos, speed);
                    modelRoot.rotation   = Quaternion.Slerp(modelRoot.rotation, _lastAdminRot, speed);
                    modelRoot.localScale = Vector3.Lerp(modelRoot.localScale, _lastAdminScale, speed);
                }
            }
            else
            {
                ApplySmoothedRotation();
            }
        }

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
                if (t.phase == TouchPhase.Moved)
                {
                    delta = pos - _lastSinglePos;
                    _lastSinglePos = pos;
                }
            }
#else
            UnityEngine.Touch t = Input.GetTouch(0);
            if (t.phase == UnityEngine.TouchPhase.Began)
            {
                _lastSinglePos = pos;
                return;
            }
            if (t.phase == UnityEngine.TouchPhase.Moved)
            {
                delta = t.deltaPosition;
            }
#endif

            if (delta.sqrMagnitude > 1f)
            {
                if (IsSyncLocked) BreakSync();

                float deltaXNormalized = delta.x / Screen.width;
                float deltaYNormalized = delta.y / Screen.height;

                _targetRotY -= deltaXNormalized * rotationSpeed * 1800f;
                _targetRotX -= deltaYNormalized * rotationSpeed * 1800f;
                _targetRotX = Mathf.Clamp(_targetRotX, -85f, 85f);
            }
        }

        private void ApplySmoothedRotation()
        {
            _currentRotX = Mathf.LerpAngle(_currentRotX, _targetRotX, Time.deltaTime * rotationSmoothing);
            _currentRotY = Mathf.LerpAngle(_currentRotY, _targetRotY, Time.deltaTime * rotationSmoothing);

            modelRoot.rotation = Quaternion.Euler(_currentRotX, _currentRotY, 0f);
        }

        private void HandleMouseInput()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.08f)
            {
                if (IsSyncLocked) BreakSync();
                float currentScale = modelRoot.localScale.x;
                float newScale = Mathf.Clamp(currentScale + scroll * zoomSpeed * 50f, minScale, maxScale);
                modelRoot.localScale = Vector3.one * newScale;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            {
                Vector2 currentMousePos = Input.mousePosition;
                Vector2 delta = currentMousePos - _lastMousePos;
                _lastMousePos = currentMousePos;

                if (delta.sqrMagnitude > 4f)
                {
                    if (IsSyncLocked) BreakSync();

                    float deltaXNormalized = delta.x / Screen.width;
                    float deltaYNormalized = delta.y / Screen.height;

                    _targetRotY -= deltaXNormalized * rotationSpeed * 1800f;
                    _targetRotX -= deltaYNormalized * rotationSpeed * 1800f;
                    _targetRotX = Mathf.Clamp(_targetRotX, -85f, 85f);
                }
            }

            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                _lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                Vector2 currentMousePos = Input.mousePosition;
                Vector2 delta = currentMousePos - _lastMousePos;
                _lastMousePos = currentMousePos;

                if (delta.sqrMagnitude > 4f)
                {
                    if (IsSyncLocked) BreakSync();

                    float panXNormalized = delta.x / Screen.width;
                    float panYNormalized = delta.y / Screen.height;

                    modelRoot.position += new Vector3(panXNormalized * panSpeed * 300f, panYNormalized * panSpeed * 300f, 0f);
                }
            }
        }

        private void HandleTwoFingerTouch()
        {
            Vector2 pos0 = GetTouchPosition(0);
            Vector2 pos1 = GetTouchPosition(1);

            bool isBegan = IsTouchBegan(0) || IsTouchBegan(1);

            if (isBegan)
            {
                _lastPinchDistance     = Vector2.Distance(pos0, pos1);
                _lastTwoFingerMidpoint = (pos0 + pos1) * 0.5f;
                return;
            }

            if (IsSyncLocked) BreakSync();

            float currentDistance = Vector2.Distance(pos0, pos1);
            float pinchDelta      = currentDistance - _lastPinchDistance;
            _lastPinchDistance    = currentDistance;

            float currentScale    = modelRoot.localScale.x;
            float pinchDeltaNorm  = pinchDelta / Screen.width;
            float newScale        = Mathf.Clamp(currentScale + pinchDeltaNorm * zoomSpeed * 200f, minScale, maxScale);
            modelRoot.localScale  = Vector3.one * newScale;

            Vector2 midpoint     = (pos0 + pos1) * 0.5f;
            Vector2 panDelta     = midpoint - _lastTwoFingerMidpoint;
            _lastTwoFingerMidpoint = midpoint;

            float panXNorm = panDelta.x / Screen.width;
            float panYNorm = panDelta.y / Screen.height;

            modelRoot.position  += new Vector3(panXNorm * panSpeed * 300f, panYNorm * panSpeed * 300f, 0f);
        }

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
