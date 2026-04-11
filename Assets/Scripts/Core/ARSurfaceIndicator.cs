using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AuraSync.Core
{
    /// <summary>
    /// Shows a placement reticle at the center of screen when in placement mode.
    /// SETUP: Attach to XR Origin. Assign reticleObject (a simple quad/ring prefab).
    /// </summary>
    public class ARSurfaceIndicator : MonoBehaviour
    {
        [SerializeField] private GameObject reticleObject;
        [SerializeField] private float smoothSpeed = 12f;

        private ARRaycastManager _raycastManager;
        private static List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        private bool _isVisible;

        void Awake()
        {
            _raycastManager = GetComponent<ARRaycastManager>();
            if (reticleObject != null) reticleObject.SetActive(false);
        }

        void Update()
        {
            var mode = AppStateManager.Instance?.CurrentMode ?? AppMode.Idle;
            bool inPlacementMode = (mode == AppMode.PlacingNote || mode == AppMode.PlacingObject);

            if (!inPlacementMode)
            {
                SetVisible(false);
                return;
            }

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (_raycastManager != null && _raycastManager.Raycast(center, _hits,
                TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds |
                TrackableType.FeaturePoint))
            {
                Pose hitPose = _hits[0].pose;
                SetVisible(true);
                reticleObject.transform.position = Vector3.Lerp(
                    reticleObject.transform.position,
                    hitPose.position,
                    Time.deltaTime * smoothSpeed
                );
                reticleObject.transform.rotation = hitPose.rotation;
            }
            else
            {
                SetVisible(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_isVisible == visible) return;
            _isVisible = visible;
            if (reticleObject != null) reticleObject.SetActive(visible);
        }
    }
}
