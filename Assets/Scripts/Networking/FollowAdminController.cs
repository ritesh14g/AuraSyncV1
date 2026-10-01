using System;
using UnityEngine;
using Photon.Pun;

namespace AuraSync.Networking
{
    public class FollowAdminController : MonoBehaviour
    {
        public static FollowAdminController Instance { get; private set; }

        public event Action<bool> OnFollowAdminStateChanged;

        [Header("Follow Settings")]
        [SerializeField] private float followSpeed = 5.0f;
        [SerializeField] private Vector3 offset = new Vector3(0, 0f, -0.5f);

        private bool _isFollowing = false;
        public bool IsFollowing => _isFollowing;

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

        public void SetFollowAdmin(bool follow)
        {
            if (ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin)
            {
                Debug.Log("[AuraSync Follow] Admin cannot follow self.");
                _isFollowing = false;
                OnFollowAdminStateChanged?.Invoke(false);
                return;
            }

            _isFollowing = follow;
            Debug.Log($"[AuraSync Follow] Following Admin: {_isFollowing}");
            OnFollowAdminStateChanged?.Invoke(_isFollowing);
        }

        public void ToggleFollowAdmin()
        {
            SetFollowAdmin(!_isFollowing);
        }

        void LateUpdate()
        {
            if (!_isFollowing || Camera.main == null) return;

            int adminActorNumber = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            if (adminActorNumber < 0) return;

            Transform adminTransform = null;
            if (UserPresence.Instance != null)
            {
                GameObject adminPointer = UserPresence.Instance.GetPointerObject(adminActorNumber);
                if (adminPointer != null) adminTransform = adminPointer.transform;
            }

            if (adminTransform == null) return;

            Vector3 targetPos = adminTransform.position + adminTransform.TransformDirection(offset);
            Quaternion targetRot = adminTransform.rotation;

            Transform camTransform = Camera.main.transform;
            camTransform.position = Vector3.Lerp(camTransform.position, targetPos, Time.deltaTime * followSpeed);
            camTransform.rotation = Quaternion.Slerp(camTransform.rotation, targetRot, Time.deltaTime * followSpeed);
        }
    }
}
