using System;
using System.Collections.Generic;
using UnityEngine;
using AuraSync.Data;

namespace AuraSync.Networking
{
    /// <summary>
    /// Manages Personal and Shared zones for AR content.
    /// Personal: isPrivate = true. Only visible locally.
    /// Shared: isPrivate = false. Placed by admin, synced via Photon.
    /// </summary>
    public class ZoneManager : MonoBehaviour
    {
        public static ZoneManager Instance { get; private set; }

        public event Action<bool> OnAdminStatusChanged;

        [SerializeField] private bool isAdmin = false;
        public bool IsAdmin => isAdmin;

        public static readonly string PersonalZoneTag = "PersonalZone";
        public static readonly string SharedZoneTag = "SharedZone";

        private HashSet<string> _personalItems = new HashSet<string>();
        private HashSet<string> _sharedItems   = new HashSet<string>();

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

        public void SetAdminStatus(bool isAdmin)
        {
            this.isAdmin = isAdmin;
            OnAdminStatusChanged?.Invoke(isAdmin);
            Debug.Log($"[AuraSync] ZoneManager: Admin={isAdmin}");
        }

        public bool CanBroadcast()
        {
            return isAdmin;
        }

        public string GetZoneTag(bool isPrivate)
        {
            return isPrivate ? PersonalZoneTag : SharedZoneTag;
        }

        public void RegisterPlacedItem(PlacedItemData data)
        {
            if (data == null) return;

            if (data.isPrivate)
            {
                _personalItems.Add(data.id);
                Debug.Log($"[AuraSync] ZoneManager: Registered item {data.id} in PERSONAL zone");
            }
            else
            {
                _sharedItems.Add(data.id);
                Debug.Log($"[AuraSync] ZoneManager: Registered item {data.id} in SHARED zone");
            }
        }

        public void UnregisterItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _personalItems.Remove(id);
            _sharedItems.Remove(id);
            Debug.Log($"[AuraSync] ZoneManager: Unregistered item {id}");
        }

        public bool IsPersonalItem(string id) => _personalItems.Contains(id);
        public bool IsSharedItem(string id)   => _sharedItems.Contains(id);

        public List<string> GetPersonalItemIds() => new List<string>(_personalItems);
        public List<string> GetSharedItemIds()   => new List<string>(_sharedItems);
    }
}
