using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using GLTFast;
using AuraSync.Networking;

namespace AuraSync.Space
{
    /// <summary>
    /// Entry point for the 3D SpaceScene. Loads the selected GLB model and manages UI state.
    /// </summary>
    public class SpaceSceneManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform       modelAnchor;
        [SerializeField] private TextMeshProUGUI modelNameLabel;
        [SerializeField] private Button          backButton;
        [SerializeField] private Toggle          adminToggle;
        [SerializeField] private TextMeshProUGUI hintLabel;

        [Header("Loading UI")]
        [SerializeField] private GameObject      loadingOverlay; 
        [SerializeField] private TextMeshProUGUI loadingLabel;

        [Header("Settings")]
        [SerializeField] private string mainSceneName = "MainScene"; // Change to your V1 AR scene name

        void Start()
        {
            // 1. Read filename from PlayerPrefs
            string filename = PlayerPrefs.GetString("SelectedModelFile", "House.glb");
            
            // 2. Build full path
            // Note: GLTFast can load from streaming assets using "file://" + path
            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Models", filename);
            
            // 3. Update UI
            if (modelNameLabel) modelNameLabel.text = filename.Replace(".glb", "").Replace(".gltf", "");
            
            // 4. Start Loading
            if (loadingOverlay) loadingOverlay.SetActive(true);
            StartCoroutine(LoadModelRoutine(fullPath));

            // 5. Wire buttons
            backButton?.onClick.AddListener(() => SceneManager.LoadScene(mainSceneName));
            
            if (adminToggle)
            {
                adminToggle.onValueChanged.AddListener(OnAdminToggleChanged);
                // Sync initial state if ZoneManager already exists
                if (ZoneManager.Instance != null)
                {
                    adminToggle.isOn = ZoneManager.Instance.IsAdmin;
                }
            }

            Debug.Log($"[AuraSync Space] Scene Manager started. Model: {filename}");
        }

        private IEnumerator LoadModelRoutine(string path)
        {
            if (loadingLabel) loadingLabel.text = "Initializing loader...";

            var gltf = new GltfImport();
            
            // Prepend file:// for GLTFast on most platforms
            string uri = "file://" + path;
            
            // GLTFast version check: Use Load instead of Import if using older version, 
            // but for 2022.3 we assume latest GLTFast
            var task = gltf.Load(uri);
            
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted || !task.IsCompletedSuccessfully)
            {
                Debug.LogError($"[AuraSync Space] Failed to load model at {path}");
                if (loadingLabel) loadingLabel.text = "Failed to load model.";
                yield break;
            }

            if (loadingLabel) loadingLabel.text = "Instantiating...";
            
            // Instantiate into the anchor
            var instantiateTask = gltf.InstantiateMainSceneAsync(modelAnchor);
            
            while (!instantiateTask.IsCompleted)
            {
                yield return null;
            }

            // 7. Center and scale model
            AutoFitModel(modelAnchor);

            // 8. Hide loading overlay
            if (loadingOverlay) loadingOverlay.SetActive(false);

            // 9. Hand off to Interaction
            GetComponent<SpaceInteractionController>()?.OnModelLoaded(modelAnchor);

            // 10. Sync with Networking
            if (AdminBroadcastManager.Instance != null)
            {
                AdminBroadcastManager.Instance.SetBroadcastTarget(modelAnchor);
            }

            Debug.Log("[AuraSync Space] Model loaded successfully: " + path);
        }

        private void AutoFitModel(Transform anchor)
        {
            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float targetSize = 1.5f;
            float scaleFactor = targetSize / maxDim;

            anchor.localScale = Vector3.one * scaleFactor;
            
            // Move anchor so model center is at origin
            Vector3 centerOffset = bounds.center - anchor.position;
            anchor.position = -centerOffset * scaleFactor;
        }

        private void OnAdminToggleChanged(bool isAdmin)
        {
            if (ZoneManager.Instance != null)
            {
                ZoneManager.Instance.SetAdminStatus(isAdmin);
            }

            if (isAdmin)
            {
                AdminBroadcastManager.Instance?.StartBroadcasting();
                if (hintLabel) hintLabel.text = "ADMIN MODE — Your interactions broadcast to participants";
            }
            else
            {
                AdminBroadcastManager.Instance?.StopBroadcasting();
                if (hintLabel) hintLabel.text = "Drag to rotate • Pinch to zoom • Two-finger pan";
            }
            
            Debug.Log($"[AuraSync Space] Admin Mode: {isAdmin}");
        }
    }
}
