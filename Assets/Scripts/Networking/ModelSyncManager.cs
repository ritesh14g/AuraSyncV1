using System;
using UnityEngine;
using Photon.Pun;
using ExitGames.Client.Photon;

namespace AuraSync.Networking
{
    public class ModelSyncManager : MonoBehaviourPunCallbacks
    {
        public static ModelSyncManager Instance { get; private set; }

        public const string KeyCurrentModelUrl = "CurrentModelUrl";
        public const string KeyModelName = "CurrentModelName";

        public event Action<string, string> OnModelSyncRequested; // (url, name)

        private string _activeModelUrl = "";
        public string ActiveModelUrl => _activeModelUrl;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            var pv = GetComponent<PhotonView>();
            if (pv != null && pv.ViewID == 0)
            {
                pv.ViewID = 995;
            }
        }

        public void BroadcastModelChange(string glbUrl, string displayName = "")
        {
            if (!PhotonNetwork.InRoom) return;

            if (ZoneManager.Instance != null && !ZoneManager.Instance.IsAdmin)
            {
                Debug.LogWarning("[AuraSync ModelSync] Only Admin can change the active model!");
                return;
            }

            _activeModelUrl = glbUrl;

            Hashtable props = new Hashtable
            {
                { KeyCurrentModelUrl, glbUrl },
                { KeyModelName, displayName }
            };

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log($"[AuraSync ModelSync] Admin set Room Model URL: {glbUrl}");
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (propertiesThatChanged.ContainsKey(KeyCurrentModelUrl))
            {
                string url = (string)propertiesThatChanged[KeyCurrentModelUrl];
                string name = propertiesThatChanged.ContainsKey(KeyModelName) ? (string)propertiesThatChanged[KeyModelName] : "";

                if (!string.IsNullOrEmpty(url) && url != _activeModelUrl)
                {
                    _activeModelUrl = url;
                    Debug.Log($"[AuraSync ModelSync] Received updated room model: {url}");
                    OnModelSyncRequested?.Invoke(url, name);
                }
            }
        }

        public override void OnJoinedRoom()
        {
            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyCurrentModelUrl, out object rawUrl))
            {
                string url = rawUrl as string;
                if (!string.IsNullOrEmpty(url))
                {
                    _activeModelUrl = url;
                    string name = "";
                    if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyModelName, out object rawName))
                    {
                        name = rawName as string;
                    }
                    Debug.Log($"[AuraSync ModelSync] Joined room with active model: {url}");
                    OnModelSyncRequested?.Invoke(url, name);
                }
            }
        }
    }
}
