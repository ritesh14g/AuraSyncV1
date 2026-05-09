using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace AuraSync.Space
{
    /// <summary>
    /// Handles 3D model interaction (rotate, zoom, pan) using the Enhanced Touch system.
    /// </summary>
    public class SpaceInteractionController : MonoBehaviour
    {
        [Header("Sensitivity")]
        [SerializeField] private float rotateSpeed = 0.3f;   
        [SerializeField] private float zoomSpeed   = 0.01f;  
        [SerializeField] private float panSpeed    = 0.005f; 

        [Header("Limits")]
        [SerializeField] private float minScale = 0.2f;
        [SerializeField] private float maxScale = 5f;

        private Transform _modelTransform;
        private bool      _modelReady = false;

        public void OnModelLoaded(Transform modelAnchor)
        {
            _modelTransform = modelAnchor;
            _modelReady = true;
            Debug.Log("[AuraSync Space] Interaction controller initialized.");
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        void Update()
        {
            if (!_modelReady || _modelTransform == null) return;

            var touches = Touch.activeTouches;

            // ONE FINGER — Rotate
            if (touches.Count == 1)
            {
                var touch = touches[0];
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    float yaw   = touch.delta.x * rotateSpeed;
                    float pitch = touch.delta.y * rotateSpeed;

                    _modelTransform.Rotate(Vector3.up, -yaw, UnityEngine.Space.World);
                    _modelTransform.Rotate(Vector3.right, pitch, UnityEngine.Space.World);
                }
            }
            // TWO FINGERS — Pinch to Zoom & Two-Finger Pan
            else if (touches.Count == 2)
            {
                var t0 = touches[0];
                var t1 = touches[1];

                // --- Pinch Zoom ---
                float prevDist = Vector2.Distance(t0.screenPosition - t0.delta, t1.screenPosition - t1.delta);
                float currDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                float pinchDelta = currDist - prevDist;

                if (Mathf.Abs(pinchDelta) > 0.01f)
                {
                    Vector3 newScale = _modelTransform.localScale + Vector3.one * (pinchDelta * zoomSpeed);
                    
                    // Clamp scale
                    float clampedS = Mathf.Clamp(newScale.x, minScale, maxScale);
                    _modelTransform.localScale = new Vector3(clampedS, clampedS, clampedS);
                }

                // --- Two-Finger Pan ---
                if (t0.phase == UnityEngine.InputSystem.TouchPhase.Moved || t1.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    Vector2 avgDelta = (t0.delta + t1.delta) * 0.5f;
                    Vector3 panDelta = new Vector3(avgDelta.x, avgDelta.y, 0) * panSpeed;
                    _modelTransform.position += panDelta;
                }
            }
        }
    }
}
