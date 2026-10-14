using System;
using UnityEngine;
using Photon.Pun;
using AuraSync.Space;

namespace AuraSync.Networking
{
    /// <summary>
    /// "Follow the presenter" for viewers in the 3D Space scene.
    ///
    /// Following is the viewer's sync lock (SpaceInteractionController): while locked, the model
    /// mirrors the presenter's rotation, zoom and position. This controller only exposes that
    /// state to the UI. It never moves the camera: in AR the camera is driven by device tracking
    /// (and each phone has its own world coordinates), and in the Space scene every camera sits
    /// at the same fixed spot, so following the presenter's camera would be meaningless.
    /// </summary>
    public class FollowAdminController : MonoBehaviour
    {
        public static FollowAdminController Instance { get; private set; }

        public event Action<bool> OnFollowAdminStateChanged;

        private bool _lastReportedState = false;

        /// <summary>True when there is a presenter to follow and a model view to follow them in.</summary>
        public bool IsAvailable =>
            PhotonNetwork.InRoom &&
            SpaceInteractionController.Instance != null &&
            !(ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin);

        public bool IsFollowing => IsAvailable && SpaceInteractionController.Instance.IsSyncLocked;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetFollowAdmin(bool follow)
        {
            if (!IsAvailable)
            {
                Debug.Log("[AuraSync Follow] Nothing to follow here (no presenter or no model view).");
                return;
            }

            if (follow) SpaceInteractionController.Instance.SnapToAdmin();
            else        SpaceInteractionController.Instance.BreakSync();
        }

        public void ToggleFollowAdmin() => SetFollowAdmin(!IsFollowing);

        void Update()
        {
            // The state lives in SpaceInteractionController, which belongs to a scene and can be
            // replaced. Poll so listeners always hear about changes.
            bool now = IsFollowing;
            if (now == _lastReportedState) return;
            _lastReportedState = now;
            OnFollowAdminStateChanged?.Invoke(now);
        }
    }
}
