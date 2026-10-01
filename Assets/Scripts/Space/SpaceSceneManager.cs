using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using TMPro;
using GLTFast;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.UI;
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
            string filename = PlayerPrefs.GetString("SelectedModelFile", "House.glb").Trim();
            
            // Clean up common URL prefix errors
            if (filename.StartsWith("co/datasets"))
            {
                filename = "huggingface.co/datasets" + filename.Substring("co/datasets".Length);
            }
            
            if (filename.Contains("huggingface.co") || filename.Contains("firebasestorage.googleapis.com"))
            {
                if (!filename.StartsWith("http://") && !filename.StartsWith("https://"))
                {
                    filename = "https://" + filename;
                }
            }
            
            // Ensure remote URLs have a proper extension if needed
            if (filename.StartsWith("http://") || filename.StartsWith("https://"))
            {
                string urlWithoutQuery = filename;
                int queryIndex = filename.IndexOf('?');
                if (queryIndex >= 0)
                {
                    urlWithoutQuery = filename.Substring(0, queryIndex);
                }

                if (!urlWithoutQuery.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && 
                    !urlWithoutQuery.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                {
                    if (queryIndex >= 0)
                    {
                        filename = filename.Insert(queryIndex, ".glb");
                    }
                    else
                    {
                        filename += ".glb";
                    }
                }
            }

            string fullPath;
            
            // If the database gave us a web URL, use it directly. Otherwise, look in StreamingAssets.
            if (filename.StartsWith("http://") || filename.StartsWith("https://"))
            {
                fullPath = filename;
            }
            else
            {
                fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Models", filename);
            }
            
            string displayName = filename;
            if (displayName.StartsWith("http://") || displayName.StartsWith("https://"))
            {
                try
                {
                    var uri = new Uri(displayName);
                    displayName = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                }
                catch
                {
                    displayName = System.IO.Path.GetFileNameWithoutExtension(displayName);
                }
            }
            else
            {
                displayName = System.IO.Path.GetFileNameWithoutExtension(displayName);
            }

            if (modelNameLabel) modelNameLabel.text = displayName;
            if (loadingOverlay) loadingOverlay.SetActive(true);

            // Ensure SpaceSessionHUD exists in SpaceScene
            if (SpaceSessionHUD.Instance == null)
            {
                GameObject hudGo = new GameObject("AuraSync_SpaceSessionHUD");
                hudGo.AddComponent<SpaceSessionHUD>();
            }
            
            LoadModelAsync(fullPath);

            backButton?.onClick.AddListener(() => SceneManager.LoadScene(mainSceneName));

            // adminToggle is legacy — SpaceSessionHUD owns admin controls now.
            // Keep for backwards compatibility if still wired in Inspector.
            if (adminToggle)
            {
                adminToggle.onValueChanged.AddListener(OnAdminToggleChanged);
                if (ZoneManager.Instance != null)
                    adminToggle.isOn = ZoneManager.Instance.IsAdmin;
            }

            if (ModelSyncManager.Instance != null)
            {
                ModelSyncManager.Instance.OnModelSyncRequested += OnSyncedModelRequested;
            }
        }

        void OnDestroy()
        {
            if (ModelSyncManager.Instance != null)
            {
                ModelSyncManager.Instance.OnModelSyncRequested -= OnSyncedModelRequested;
            }
        }

        private void OnSyncedModelRequested(string modelUrl, string modelName)
        {
            if (!string.IsNullOrEmpty(modelUrl))
            {
                if (modelNameLabel && !string.IsNullOrEmpty(modelName)) modelNameLabel.text = modelName;
                if (loadingOverlay) loadingOverlay.SetActive(true);
                Debug.Log($"[SpaceSceneManager] Synced model load triggered: {modelUrl}");
                LoadModelAsync(modelUrl);
            }
        }

        private class GLTFLogger : GLTFast.Logging.ICodeLogger
        {
            public string LastError { get; private set; } = "";

            public void Error(GLTFast.Logging.LogCode code, params string[] messages)
            {
                LastError = (messages != null && messages.Length > 0) ? string.Join(", ", messages) : code.ToString();
                Debug.LogError($"[GLTFast Error] {code}: {LastError}");
            }

            public void Warning(GLTFast.Logging.LogCode code, params string[] messages)
            {
                string msg = (messages != null && messages.Length > 0) ? string.Join(", ", messages) : code.ToString();
                Debug.LogWarning($"[GLTFast Warning] {code}: {msg}");
            }

            public void Info(GLTFast.Logging.LogCode code, params string[] messages)
            {
                string msg = (messages != null && messages.Length > 0) ? string.Join(", ", messages) : code.ToString();
                Debug.Log($"[GLTFast Info] {code}: {msg}");
            }

            public void Error(string message)
            {
                LastError = message;
                Debug.LogError($"[GLTFast Error] {message}");
            }

            public void Warning(string message)
            {
                Debug.LogWarning($"[GLTFast Warning] {message}");
            }

            public void Info(string message)
            {
                Debug.Log($"[GLTFast Info] {message}");
            }
        }

        private string FormatPathForDisplay(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            if (fullPath.Length <= 60) return fullPath;
            
            // Truncate middle for long URLs
            return fullPath.Substring(0, 30) + "..." + fullPath.Substring(fullPath.Length - 25);
        }

        private void UpdateLoadingText(string status, string path, string error = "")
        {
            if (loadingLabel == null) return;

            string displayPath = FormatPathForDisplay(path);
            if (string.IsNullOrEmpty(error))
            {
                loadingLabel.text = $"<b>LOADING MODEL</b>\n" +
                                   $"<size=20><color=#AAAAAA>{displayPath}</color></size>\n\n" +
                                   $"<size=26><color=#6290FF>{status}</color></size>";
            }
            else
            {
                loadingLabel.text = $"<b><color=#FF5555>LOADING ERROR</color></b>\n" +
                                   $"<size=20><color=#AAAAAA>{displayPath}</color></size>\n\n" +
                                   $"<size=24><color=#FF8888>Failed at: {status}</color></size>\n" +
                                   $"<size=20><color=#FF3333>Error: {error}</color></size>";
            }
        }

        private string GetCacheFilePath(string url)
        {
            try
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(url);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);
                    
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int i = 0; i < hashBytes.Length; i++)
                    {
                        sb.Append(hashBytes[i].ToString("X2"));
                    }
                    
                    string extension = url.Contains(".gltf") ? ".gltf" : ".glb";
                    return System.IO.Path.Combine(Application.persistentDataPath, "ModelCache", sb.ToString() + extension);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpaceSceneManager] Hash calculation failed: {ex.Message}");
                // Fallback to a simple filename replacing invalid characters
                string safeName = url;
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    safeName = safeName.Replace(c, '_');
                }
                if (safeName.Length > 100) safeName = safeName.Substring(safeName.Length - 100);
                return System.IO.Path.Combine(Application.persistentDataPath, "ModelCache", safeName);
            }
        }

        private async void LoadModelAsync(string path)
        {
            UpdateLoadingText("Initializing loader...", path);
            QualitySettings.globalTextureMipmapLimit = 2; 

            // Clear any existing loaded models under the anchor
            if (modelAnchor != null)
            {
                foreach (Transform child in modelAnchor)
                {
                    Destroy(child.gameObject);
                }
            }

            var logger = new GLTFLogger();
            var gltf = new GltfImport(logger: logger);
            string uri = path;
            
            bool isRemote = uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            
            string cachePath = "";
            
            if (isRemote)
            {
                cachePath = GetCacheFilePath(uri);
                if (System.IO.File.Exists(cachePath))
                {
                    uri = cachePath; // Load from local cached file instead of downloading!
                    isRemote = false;
                    Debug.Log($"[SpaceSceneManager] Model found in local cache: {cachePath}");
                }
            }
            else if (!uri.Contains("://"))
            {
                try
                {
                    uri = new Uri(System.IO.Path.GetFullPath(uri)).AbsoluteUri;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SpaceSceneManager] Failed to convert path to Uri: {ex.Message}. Falling back to file:// prefix.");
                    uri = "file://" + uri;
                }
            }
            
            try
            {
                byte[] data = null;

                if (isRemote)
                {
                    Debug.Log($"[SpaceSceneManager] Starting download for URI: {uri}");
                    
                    using (UnityWebRequest request = UnityWebRequest.Get(uri))
                    {
                        // Set a 3-minute timeout to allow large models to download
                        request.timeout = 180;
                        
                        var operation = request.SendWebRequest();
                        
                        while (!operation.isDone)
                        {
                            if (this == null) return;
                            
                            float progress = request.downloadProgress;
                            ulong downloadedBytes = request.downloadedBytes;
                            double downloadedMB = downloadedBytes / (1024.0 * 1024.0);
                            
                            string contentLengthHeader = request.GetResponseHeader("Content-Length");
                            if (!string.IsNullOrEmpty(contentLengthHeader) && double.TryParse(contentLengthHeader, out double totalBytes))
                            {
                                double totalMB = totalBytes / (1024.0 * 1024.0);
                                double pct = progress * 100.0;
                                UpdateLoadingText($"Downloading: {downloadedMB:F1} MB / {totalMB:F1} MB ({pct:F0}%)", uri);
                            }
                            else
                            {
                                double pct = progress * 100.0;
                                if (pct > 0)
                                {
                                    UpdateLoadingText($"Downloading: {downloadedMB:F1} MB ({pct:F0}%)", uri);
                                }
                                else
                                {
                                    UpdateLoadingText($"Downloading: {downloadedMB:F1} MB", uri);
                                }
                            }
                            
                            await System.Threading.Tasks.Task.Yield();
                        }
                        
                        if (this == null) return;
                        
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            Debug.LogError($"[SpaceSceneManager] Download failed: {request.error}");
                            UpdateLoadingText("Downloading GLTF", uri, request.error);
                            return;
                        }
                        
                        data = request.downloadHandler.data;
                        
                        // Cache the downloaded bytes to local file
                        try
                        {
                            string dir = System.IO.Path.GetDirectoryName(cachePath);
                            if (!System.IO.Directory.Exists(dir))
                            {
                                System.IO.Directory.CreateDirectory(dir);
                            }
                            System.IO.File.WriteAllBytes(cachePath, data);
                            Debug.Log($"[SpaceSceneManager] Cached downloaded model bytes to: {cachePath}");
                        }
                        catch (Exception cacheEx)
                        {
                            Debug.LogWarning($"[SpaceSceneManager] Failed to save downloaded model to cache: {cacheEx.Message}");
                        }
                    }
                }
                else
                {
                    // If it is a local file (StreamingAssets or local Cache file)
                    UpdateLoadingText("Loading local model file...", uri);
                }

                bool success = false;
                
                if (data != null)
                {
                    UpdateLoadingText("Parsing GLTF binary data...", uri);
                    
                    var parseTask = gltf.Load(data, new Uri(path)); // updated to gltf.Load to fix deprecation warning
                    var parseTimeoutTask = System.Threading.Tasks.Task.Delay(15000); // 15 seconds parsing timeout
                    
                    var completedParseTask = await System.Threading.Tasks.Task.WhenAny(parseTask, parseTimeoutTask);
                    if (this == null) return;
                    
                    if (completedParseTask == parseTimeoutTask)
                    {
                        Debug.LogError($"[SpaceSceneManager] Parsing timed out for URI: {uri}");
                        UpdateLoadingText("Parsing GLTF binary data", uri, "Parsing timeout (15 seconds limit exceeded).");
                        return;
                    }
                    
                    success = await parseTask;
                }
                else
                {
                    // Direct load from local file URI
                    UpdateLoadingText("Parsing GLTF from file...", uri);
                    
                    var loadTask = gltf.Load(new Uri(uri));
                    var loadTimeoutTask = System.Threading.Tasks.Task.Delay(15000); // 15 seconds timeout
                    
                    var completedLoadTask = await System.Threading.Tasks.Task.WhenAny(loadTask, loadTimeoutTask);
                    if (this == null) return;
                    
                    if (completedLoadTask == loadTimeoutTask)
                    {
                        Debug.LogError($"[SpaceSceneManager] Parsing timed out for local URI: {uri}");
                        UpdateLoadingText("Parsing GLTF", uri, "Loading timeout (15 seconds limit exceeded).");
                        return;
                    }
                    
                    success = await loadTask;
                }

                if (!success)
                {
                    Debug.LogError($"[SpaceSceneManager] GLTFast failed to parse data for URI: {uri}");
                    string errorMsg = string.IsNullOrEmpty(logger.LastError) ? "GLTF parsing failed" : logger.LastError;
                    UpdateLoadingText("Parsing GLTF data", uri, errorMsg);
                    return;
                }

                UpdateLoadingText("Instantiating model...", uri);
                var instantiateTask = gltf.InstantiateMainSceneAsync(modelAnchor);
                await instantiateTask;
                if (this == null) return;

                UpdateLoadingText("Applying material fixes...", uri);
                if (materialHelper == null)
                {
                    materialHelper = GetComponent<GLTFastMaterialHelper>() ?? gameObject.AddComponent<GLTFastMaterialHelper>();
                }
                materialHelper.FixAllMaterials(modelAnchor);

                UpdateLoadingText("Optimizing mesh simplicity...", uri);
                SimplifyModel(modelAnchor);
                
                UpdateLoadingText("Fitting model to camera view...", uri);
                AutoFitModel(modelAnchor);
                
                if (loadingOverlay) loadingOverlay.SetActive(false);

                // Notify the interaction controller that the model is ready
                var interactionCtrl = GetComponent<SpaceInteractionController>() ?? FindObjectOfType<SpaceInteractionController>();
                interactionCtrl?.OnModelLoaded(modelAnchor);

                // Set the broadcast target for Admin sync
                if (AdminBroadcastManager.Instance != null)
                {
                    AdminBroadcastManager.Instance.SetBroadcastTarget(modelAnchor);
                }

                // If Admin is already in a room, start broadcasting immediately
                bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
                if (isAdmin && PhotonNetwork.InRoom)
                {
                    AdminBroadcastManager.Instance?.StartBroadcasting();
                    ModelSyncManager.Instance?.BroadcastModelChange(path, System.IO.Path.GetFileNameWithoutExtension(path));
                }

                // Extract display name from path for UI label
                string modelDisplayName = System.IO.Path.GetFileNameWithoutExtension(path);

                // Tell SpaceSessionHUD the model is loaded — shows the Share button
                SpaceSessionHUD.Instance?.OnModelLoaded(modelDisplayName, path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpaceSceneManager] Exception loading model: {ex}");
                UpdateLoadingText("Processing Model", uri, ex.Message);
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
            if (anchor == null) return;
            
            // Reset anchor properties first so bounds calculation starts from identity
            anchor.localScale = Vector3.one;
            anchor.position = Vector3.zero;
            anchor.rotation = Quaternion.identity;

            Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float targetSize = 2.0f; 
            float scaleFactor = targetSize / (maxDim > 0 ? maxDim : 1f);

            anchor.localScale = Vector3.one * scaleFactor;
            
            // Now position the model so its bottom aligns with y = 0
            float bottomOffset = bounds.min.y; // since anchor.position.y is 0
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

