using UnityEngine;

namespace AuraSync.Notes
{
    /// <summary>
    /// Makes the attached GameObject always face the AR camera.
    /// Improved version with smooth rotation and optional distance-based scaling.
    /// 
    /// SETUP: Attach to note prefab root or the visual child that should billboard.
    /// </summary>
    public class FaceCamera : MonoBehaviour
    {
        [Header("Billboard Settings")]
        [Tooltip("Smoothly rotate toward camera instead of snapping.")]
        [SerializeField] private bool smoothRotation = true;

        [Tooltip("Rotation smoothing speed (higher = faster).")]
        [SerializeField] private float rotationSpeed = 8f;

        [Header("Distance Scaling (Optional)")]
        [Tooltip("Scale the note based on distance to camera for readability.")]
        [SerializeField] private bool enableDistanceScaling = false;

        [Tooltip("Minimum distance for scaling.")]
        [SerializeField] private float minDistance = 0.5f;

        [Tooltip("Maximum distance for scaling.")]
        [SerializeField] private float maxDistance = 5f;

        [Tooltip("Scale at minimum distance.")]
        [SerializeField] private float minScale = 0.5f;

        [Tooltip("Scale at maximum distance.")]
        [SerializeField] private float maxScale = 2f;

        private Vector3 _baseScale;

        void Start()
        {
            _baseScale = transform.localScale;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // ── Billboard Rotation ──
            Vector3 directionToCamera = cam.transform.position - transform.position;
            // Y constraint removed — allows proper facing on walls and floors

            if (directionToCamera.sqrMagnitude < 0.001f) return;

            Quaternion targetRotation = Quaternion.LookRotation(-directionToCamera, Vector3.up);

            if (smoothRotation)
            {
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    Time.deltaTime * rotationSpeed
                );
            }
            else
            {
                transform.rotation = targetRotation;
            }

            // ── Distance-Based Scaling ──
            if (enableDistanceScaling)
            {
                float distance = Vector3.Distance(cam.transform.position, transform.position);
                float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
                float scaleFactor = Mathf.Lerp(minScale, maxScale, t);
                transform.localScale = _baseScale * scaleFactor;
            }
        }
    }
}
