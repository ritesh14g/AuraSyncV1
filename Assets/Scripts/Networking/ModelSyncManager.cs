using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using ExitGames.Client.Photon;

namespace AuraSync.Networking
{
    public class ModelSyncManager : MonoBehaviourPunCallbacks
    {
        public static ModelSyncManager Instance { get; private set; }

        public const string KeyCurrentModelUrl = RoomKeys.CurrentModelUrl;
        public const string KeyModelName = RoomKeys.CurrentModelName;
        public const string KeySelectedPart = RoomKeys.SelectedPart; // presenter's highlighted part ("" = none)

        public event Action<string, string> OnModelSyncRequested; // (model key, name)

        [Header("Viewer Routing")]
        [Tooltip("When the room's model changes and this device is outside the Space scene, load the Space scene.")]
        [SerializeField] private bool autoEnterSpace = true;
        [SerializeField] private string spaceSceneName = "SpaceScene";

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

            // The model and the state it was last left in travel in one update, so no viewer ever sees
            // the new model with the old model's explosion, isolation or selection (part paths from
            // one model mean nothing on another). A model not opened before starts clean.
            Hashtable props = AuraSync.Space.ModelStateMemory.SharedStateFor(glbUrl);
            props[KeyCurrentModelUrl] = glbUrl;
            props[KeyModelName]       = displayName;

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log($"[AuraSync ModelSync] Admin set Room Model URL: {glbUrl}");
        }

        /// <summary>
        /// Hands the room's model to whoever can show it. In the Space scene that is
        /// SpaceSceneManager (via the event). Anywhere else, a viewer is sent to the Space scene
        /// with the model pre-selected; SpaceSceneManager picks it up on Start.
        /// </summary>
        private void DeliverModel(string key, string name)
        {
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            bool inSpace = SceneManager.GetActiveScene().name == spaceSceneName;

            if (!inSpace && autoEnterSpace && !isAdmin)
            {
                PlayerPrefs.SetString("SelectedModelFile", key);
                PlayerPrefs.Save();
                Debug.Log($"[AuraSync ModelSync] Entering {spaceSceneName} for room model: {key}");
                SceneManager.LoadScene(spaceSceneName);
                return;
            }

            OnModelSyncRequested?.Invoke(key, name);
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
                    DeliverModel(url, name);
                }
            }

            if (propertiesThatChanged.TryGetValue(AuraSync.Space.ModelExploder.KeyExplode, out object rawExplode)
                && rawExplode is float explode)
            {
                AuraSync.Space.ModelExploder.Instance?.OnRoomAmountChanged(explode);
            }

            if (propertiesThatChanged.TryGetValue(RoomKeys.IsolatedPart, out object rawIsolated))
            {
                AuraSync.Space.ModelIsolator.Instance?.OnRoomIsolationChanged(rawIsolated as string ?? "");
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
                    DeliverModel(url, name);
                }
            }
        }
    }
}
