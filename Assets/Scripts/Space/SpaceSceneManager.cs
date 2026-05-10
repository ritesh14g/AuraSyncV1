using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using GLTFast;
using AuraSync.Networking;
using UnityMeshSimplifier;

namespace AuraSync.Space
{
    public class SpaceSceneManager : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private GLTFastMaterialHelper materialHelper;
        [SerializeField] private Transform       modelAnchor;
        [SerializeField] private TextMeshProUGUI modelNameLabel;
        [SerializeField] private Button          backButton;
        [SerializeField] private Toggle          adminToggle;
        [SerializeField] private TextMeshProUGUI hintLabel;

        [Header("Loading UI")]
        [SerializeField] private GameObject      loadingOverlay; 
        [SerializeField] private TextMeshProUGUI loadingLabel;

        [Header("Settings")]
        [SerializeField] private string mainSceneName = "MainScene";

        void Start()
        {
            string filename = PlayerPrefs.GetString("SelectedModelFile", "House.glb");
            string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Models", filename);
            
            if (modelNameLabel) modelNameLabel.text = filename.Replace(".glb", "").Replace(".gltf", "");
            if (loadingOverlay) loadingOverlay.SetActive(true);
            
            StartCoroutine(LoadModelRoutine(fullPath));

            backButton?.onClick.AddListener(() => SceneManager.LoadScene(mainSceneName));
            
            if (adminToggle)
            {
                adminToggle.onValueChanged.AddListener(OnAdminToggleChanged);
                if (ZoneManager.Instance != null)
                {
                    adminToggle.isOn = ZoneManager.Instance.IsAdmin;
                }
            }
        }

        private IEnumerator LoadModelRoutine(string path)
        {
            if (loadingLabel) loadingLabel.text = "Initializing loader...";
            QualitySettings.globalTextureMipmapLimit = 2; 

            var gltf = new GltfImport();
            string uri = path;
            
            #if !UNITY_ANDROID || UNITY_EDITOR
            if (!uri.Contains("://")) uri = "file://" + uri;
            #endif
            
            var task = gltf.Load(uri);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted || !task.IsCompletedSuccessfully)
            {
                if (loadingLabel) loadingLabel.text = "Failed to load model.";
                yield break;
            }

            if (loadingLabel) loadingLabel.text = "Instantiating...";
            var instantiateTask = gltf.InstantiateMainSceneAsync(modelAnchor);
            while (!instantiateTask.IsCompleted) yield return null;

            // Apply material fixes via helper
            materialHelper?.FixAllMaterials(modelAnchor);

            if (loadingLabel) loadingLabel.text = "Optimizing Mesh...";
            SimplifyModel(modelAnchor);
            AutoFitModel(modelAnchor);
            
            if (loadingOverlay) loadingOverlay.SetActive(false);
            GetComponent<SpaceInteractionController>()?.OnModelLoaded(modelAnchor);

            if (AdminBroadcastManager.Instance != null)
            {
                AdminBroadcastManager.Instance.SetBroadcastTarget(modelAnchor);
            }
        }

        private void SimplifyModel(Transform root)
        {
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();
            foreach (var filter in meshFilters)
            {
                Mesh sourceMesh = filter.sharedMesh;
                if (sourceMesh == null) continue;

                int triangleCount = sourceMesh.triangles.Length / 3;
                if (triangleCount > 20000)
                {
                    float quality = 0.3f;
                    var meshSimplifier = new MeshSimplifier();
                    meshSimplifier.Initialize(sourceMesh);
                    meshSimplifier.SimplifyMesh(quality);
                    filter.sharedMesh = meshSimplifier.ToMesh();
                }
            }
        }

        private void AutoFitModel(Transform anchor)
        {
            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float targetSize = 2.0f; 
            float scaleFactor = targetSize / (maxDim > 0 ? maxDim : 1f);

            anchor.localScale = Vector3.one * scaleFactor;
            anchor.position = Vector3.zero;
            float bottomOffset = bounds.min.y - anchor.position.y;
            anchor.position = new Vector3(0, -bottomOffset * scaleFactor, 0);

            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(0, 1.5f, -6f);
                Camera.main.transform.LookAt(new Vector3(0, 1.0f, 0));
            }
        }

        private void OnAdminToggleChanged(bool isAdmin)
        {
            if (ZoneManager.Instance != null) ZoneManager.Instance.SetAdminStatus(isAdmin);
            if (isAdmin)
            {
                AdminBroadcastManager.Instance?.StartBroadcasting();
                if (hintLabel) hintLabel.text = "ADMIN MODE";
            }
            else
            {
                AdminBroadcastManager.Instance?.StopBroadcasting();
                if (hintLabel) hintLabel.text = "View Mode";
            }
        }
    }
}
