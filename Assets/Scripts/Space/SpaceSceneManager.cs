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
        [Tooltip("Scene to return to from the viewer. Ignored if it is not in Build Settings.")]
        [SerializeField] private string mainSceneName = DefaultMainScene;

        /// <summary>The AR scene. Build Settings holds only this and SpaceScene.</summary>
        public const string DefaultMainScene = "V1";

        // Resolved once, so the Back button can navigate without a reference to this component.
        private static string _mainScene = DefaultMainScene;

        // Normalised key of the model currently shown. This (not a device-local path) is what
        // gets broadcast to the room, so every device resolves it against its own storage.
        private string _currentModelKey = "";

        /// <summary>
        /// Key (URL) of the model on screen once it has finished loading; "" while loading and
        /// outside the 3D viewer. For tools such as the Part Labeler.
        /// </summary>
        public static string CurrentModelKey { get; private set; } = "";

        void Start()
        {
            string key = PlayerPrefs.GetString("SelectedModelFile", "House.glb").Trim();

            // Viewers in a live room load the admin's model, not their own last selection.
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            if (PhotonNetwork.InRoom && !isAdmin && ModelSyncManager.Instance != null &&
                !string.IsNullOrEmpty(ModelSyncManager.Instance.ActiveModelUrl))
            {
                key = ModelSyncManager.Instance.ActiveModelUrl;
            }

            _currentModelKey = ResolveModel(key, out string fullPath, out string displayName);

            if (modelNameLabel) modelNameLabel.text = displayName;
            if (loadingOverlay) loadingOverlay.SetActive(true);

            // Who drives explode / isolate / highlight for the room (normally made in V1 already;
            // here too for when SpaceScene is opened directly in the Editor).
            ControlOwnership.EnsureExists();

            // Simulation: assembly and working animations, and their panel (Phase 7)
            if (AuraSync.Simulation.SimulationController.Instance == null)
                new GameObject("AuraSync_Simulation").AddComponent<AuraSync.Simulation.SimulationController>();
            if (SimulationPanelUI.Instance == null)
                new GameObject("AuraSync_SimulationPanel").AddComponent<SimulationPanelUI>();

            // The section cut and its toolbar (Phase 6)
            if (SectionCutter.Instance == null)
                new GameObject("AuraSync_SectionCutter").AddComponent<SectionCutter>();
            if (SectionCutUI.Instance == null)
                new GameObject("AuraSync_SectionCutUI").AddComponent<SectionCutUI>();

            // Notes attached to parts: the notes, their pins on the model, and the NOTES panels (Phase 5)
            if (AuraSync.Notes.PartNoteManager.Instance == null)
                new GameObject("AuraSync_PartNotes").AddComponent<AuraSync.Notes.PartNoteManager>();
            if (PartNotePins.Instance == null)
                new GameObject("AuraSync_PartNotePins").AddComponent<PartNotePins>();
            if (PartNotesPanelUI.Instance == null)
                new GameObject("AuraSync_PartNotesPanel").AddComponent<PartNotesPanelUI>();

            // AI answers shared to the room, and the card that shows them beside the part (Phase 3b)
            AuraSync.AI.SharedAnswers.EnsureExists();
            if (SharedAnswerCard.Instance == null)
                new GameObject("AuraSync_SharedAnswerCard").AddComponent<SharedAnswerCard>();

            // Ensure SpaceSessionHUD exists in SpaceScene
            if (SpaceSessionHUD.Instance == null)
            {
                GameObject hudGo = new GameObject("AuraSync_SpaceSessionHUD");
                hudGo.AddComponent<SpaceSessionHUD>();
            }

            // Tap-to-highlight model parts, and live pointers on the model surface
            if (ModelPartSelector.Instance == null)
                new GameObject("AuraSync_ModelPartSelector").AddComponent<ModelPartSelector>();
            if (CanvaPointerManager.Instance == null)
                new GameObject("AuraSync_CanvaPointers").AddComponent<CanvaPointerManager>();

            // Where everyone is looking, "look here" pings and the FIND study task (Phase 8)
            if (AttentionSync.Instance == null)
                new GameObject("AuraSync_AttentionSync").AddComponent<AttentionSync>();
            if (AttentionVisuals.Instance == null)
                new GameObject("AuraSync_AttentionVisuals").AddComponent<AttentionVisuals>();
            if (FindTask.Instance == null)
                new GameObject("AuraSync_FindTask").AddComponent<FindTask>();
            if (AttentionUI.Instance == null)
                new GameObject("AuraSync_AttentionUI").AddComponent<AttentionUI>();

            // Settings (top right), session recording and replay (Phase 9)
            if (AuraSync.Recording.SessionRecorder.Instance == null)
                new GameObject("AuraSync_SessionRecorder").AddComponent<AuraSync.Recording.SessionRecorder>();
            if (AuraSync.Recording.SessionPlayer.Instance == null)
                new GameObject("AuraSync_SessionPlayer").AddComponent<AuraSync.Recording.SessionPlayer>();
            if (SettingsPanelUI.Instance == null)
                new GameObject("AuraSync_SettingsPanel").AddComponent<SettingsPanelUI>();
            if (RecordingsUI.Instance == null)
                new GameObject("AuraSync_Recordings").AddComponent<RecordingsUI>();

            // Interaction counts and the heatmap (Phase 10)
            if (AuraSync.Analytics.InteractionTracker.Instance == null)
                new GameObject("AuraSync_InteractionTracker").AddComponent<AuraSync.Analytics.InteractionTracker>();
            if (HeatmapUI.Instance == null)
                new GameObject("AuraSync_Heatmap").AddComponent<HeatmapUI>();

            // The AI quiz and Reports (Phase 11)
            if (AuraSync.Quiz.QuizSession.Instance == null)
                new GameObject("AuraSync_QuizSession").AddComponent<AuraSync.Quiz.QuizSession>();
            if (QuizTeacherUI.Instance == null)
                new GameObject("AuraSync_QuizTeacher").AddComponent<QuizTeacherUI>();
            if (QuizStudentUI.Instance == null)
                new GameObject("AuraSync_QuizStudent").AddComponent<QuizStudentUI>();
            if (FindObjectOfType<ReportsCornerButton>() == null)
                new GameObject("AuraSync_ReportsButton").AddComponent<ReportsCornerButton>();

            // "Preview quality" while the light version of the model is shown (Phase 13)
            if (ModelQualityUI.Instance == null)
                new GameObject("AuraSync_ModelQuality").AddComponent<ModelQualityUI>();

            // Browse the model's parts by name, for the ones too small or too buried to tap
            if (ModelPartsDrawerUI.Instance == null)
                new GameObject("AuraSync_PartsDrawer").AddComponent<ModelPartsDrawerUI>();

            // Show one part on its own, with the rest of the model hidden
            if (ModelIsolator.Instance == null)
                new GameObject("AuraSync_ModelIsolator").AddComponent<ModelIsolator>();

            // Slide the parts apart to see inside the model
            if (ModelExploder.Instance == null)
                new GameObject("AuraSync_ModelExploder").AddComponent<ModelExploder>();

            // Ask the AI about a part: the ASK AI button beside a selected part, and the chat window
            if (PartChatUI.Instance == null)
                new GameObject("AuraSync_PartChat").AddComponent<PartChatUI>();

            // The reassembly challenge (single device): the PLAY button, the game and its screens
            if (AuraSync.Challenge.ReassemblyGame.Instance == null)
                new GameObject("AuraSync_ReassemblyGame").AddComponent<AuraSync.Challenge.ReassemblyGame>();
            if (ChallengeUI.Instance == null)
                new GameObject("AuraSync_ChallengeUI").AddComponent<ChallengeUI>();

            // Device back button: closes the top panel, or leaves to the model list when none is open
            if (FindObjectOfType<BackButtonHandler>() == null)
                new GameObject("AuraSync_BackButton").AddComponent<BackButtonHandler>().onEmptyStack = ReturnToModelList;

            LoadModelAsync(fullPath);

            _mainScene = ResolveMainScene(mainSceneName);
            backButton?.onClick.AddListener(ReturnToModelList);

            // The old "Admin Mode" toggle let anyone declare themselves presenter locally, which
            // broke following and created a second broadcaster. The presenter is now decided by the
            // room (NetworkManager), so the toggle is hidden.
            if (adminToggle) adminToggle.gameObject.SetActive(false);

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
            CurrentModelKey = "";
        }

        private void OnSyncedModelRequested(string modelKey, string modelName)
        {
            if (string.IsNullOrEmpty(modelKey)) return;

            string normalised = ResolveModel(modelKey, out string fullPath, out string displayName);
            if (normalised == _currentModelKey) return; // already showing it

            _currentModelKey = normalised;
            CurrentModelKey = ""; // until the new model has actually loaded
            if (modelNameLabel) modelNameLabel.text = !string.IsNullOrEmpty(modelName) ? modelName : displayName;
            if (loadingOverlay) loadingOverlay.SetActive(true);
            Debug.Log($"[SpaceSceneManager] Synced model load triggered: {normalised}");
            LoadModelAsync(fullPath);
        }

        /// <summary>
        /// Normalises a model key (StreamingAssets filename or remote URL) and resolves it to a
        /// loadable path on this device. Returns the normalised key.
        /// </summary>
        public static string ResolveModel(string key, out string fullPath, out string displayName)
        {
            string filename = key;
            
            // Clean up common URL prefix errors
            if (filename.StartsWith("co/datasets"))
            {
                filename = "huggingface.co/datasets" + filename.Substring("co/datasets".Length);
            }
            
            if (filename.Contains("huggingface.co"))
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

            // If the database gave us a web URL, use it directly. Otherwise, look in StreamingAssets.
            if (filename.StartsWith("http://") || filename.StartsWith("https://"))
            {
                fullPath = filename;
            }
            else
            {
                fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Models", filename);
            }
            
            displayName = filename;
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


            return filename;
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

        /// <summary>
        /// Removes a model's downloaded copy from this phone so its storage is freed. Safe to call for
        /// a model that was never downloaded, or one that ships inside the app (nothing to remove).
        /// </summary>
        /// <returns>True if a cached file was actually deleted.</returns>
        public static bool DeleteCachedModel(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey)) return false;

            try
            {
                ResolveModel(modelKey.Trim(), out string fullPath, out _);

                bool isRemote = fullPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                fullPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                if (!isRemote) return false; // bundled with the app, not cached

                string cachePath = GetCacheFilePath(fullPath);
                if (!System.IO.File.Exists(cachePath)) return false;

                System.IO.File.Delete(cachePath);
                Debug.Log($"[SpaceSceneManager] Deleted cached model: {cachePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpaceSceneManager] Could not delete cached model: {ex.Message}");
                return false;
            }
        }

        private static string GetCacheFilePath(string url)
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

            // Phase 13: a model not on the phone yet may show its light preview first.
            int generation = ++_loadGeneration;
            StopFullDownload();
            string fullUrl = path;
            ModelVariants.LoadStarted(fullUrl);
            bool usingPreview = false;
            if (IsRemote(fullUrl) && !System.IO.File.Exists(GetCacheFilePath(fullUrl)))
            {
                UpdateLoadingText("Checking for a quick preview...", path);
                ModelData entry = await FindCatalogEntry(fullUrl);
                if (this == null || generation != _loadGeneration) return;

                string previewUrl = entry != null ? entry.previewUrl : ModelVariants.RememberedPreview(fullUrl);
                bool offline = entry == null && Application.internetReachability == NetworkReachability.NotReachable;
                bool previewUsable = !string.IsNullOrEmpty(previewUrl) &&
                                     (!offline || System.IO.File.Exists(GetCacheFilePath(previewUrl)));
                if (previewUsable)
                {
                    usingPreview = true;
                    if (entry != null) ModelVariants.RememberPreview(fullUrl, previewUrl);
                    ModelVariants.UsePreview(entry != null ? entry.previewBytes : 0, entry != null ? entry.glbBytes : 0, offline);
                    path = previewUrl;
                    Debug.Log($"[SpaceSceneManager] Showing the preview first: {previewUrl}");
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
                    try { System.IO.File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow); } catch { /* recency only */ }
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
                    
                    float downloadStart = Time.realtimeSinceStartup;
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
                        ModelVariants.Downloaded(data.Length, Time.realtimeSinceStartup - downloadStart);
                        
                        // Cache the downloaded bytes to local file
                        try
                        {
                            string dir = System.IO.Path.GetDirectoryName(cachePath);
                            if (!System.IO.Directory.Exists(dir))
                            {
                                System.IO.Directory.CreateDirectory(dir);
                            }
                            System.IO.File.WriteAllBytes(cachePath, data);
                            TrimModelCache(cachePath);
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
                ShowMaterialProblem();

                UpdateLoadingText("Optimizing mesh simplicity...", uri);
                await SimplifyModelAsync(modelAnchor);
                if (this == null) return;
                
                UpdateLoadingText("Fitting model to camera view...", uri);
                AutoFitModel(modelAnchor);
                AfterModelPlaced(swap: false);

                // Phase 13: the paper's timing, then the full file if this was the preview.
                ModelVariants.Shown();
                if (usingPreview) OnPreviewShown(fullUrl, generation);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpaceSceneManager] Exception loading model: {ex}");
                UpdateLoadingText("Processing Model", uri, ex.Message);
            }
        }


        /// <summary>
        /// Everything that follows a model being placed under the anchor: tell every feature, then
        /// the room. Also run by <see cref="SwapToFullAsync"/> (Phase 13) with swap = true, when the
        /// full file replaces the preview of the same model: then nothing is announced to the room,
        /// the per-model memory is not restored over the live state, and the part knowledge is kept.
        /// </summary>
        private void AfterModelPlaced(bool swap)
        {
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
            if (!swap && isAdmin && PhotonNetwork.InRoom) // a swap is the same model: nothing to announce
            {
                AdminBroadcastManager.Instance?.StartBroadcasting();
                ModelSyncManager.Instance?.BroadcastModelChange(_currentModelKey, System.IO.Path.GetFileNameWithoutExtension(_currentModelKey));
            }

            // Selection highlights belong to the previous model; reset (and re-apply the room's selection)
            ModelPartSelector.Instance?.OnModelLoaded();

            // Every part is now at rest: forget the previous model's moved parts. Must come
            // before the exploder, which may move the new parts straight away (room explosion).
            PartPoseComposer.Reset();

            // Measure the model for the section cut while it is still assembled (the exploder
            // below may pull it apart straight away for a viewer joining an exploded room).
            SectionCutter.Instance?.OnModelLoaded();

            // The old model's simulation is over; a viewer joins the room's on this one.
            AuraSync.Simulation.SimulationController.Instance?.OnModelLoaded();

            // The isolator is cleared first, so it is not holding the old model's part when the
            // exploder announces its amount (the isolator re-centres on that announcement).
            ModelIsolator.Instance?.OnModelLoaded();

            // Re-measure the parts for exploded view before the drawer reads its state
            ModelExploder.Instance?.OnModelLoaded();

            // Catalogue this model's parts for the drawer (the old model's list is now wrong)
            ModelPartsDrawerUI.Instance?.OnModelLoaded();

            // Back to how this model was last left (explosion, isolation, selection, pose).
            // After every tool above has reset itself; a following viewer skips this.
            if (!swap) ModelStateMemory.RestoreAfterLoad(_currentModelKey);

            // Extract display name from path for UI label
            // From the model's key, not the file loaded: a preview is still this model.
            string modelDisplayName = System.IO.Path.GetFileNameWithoutExtension(_currentModelKey.Split('?')[0]);

            // Tell SpaceSessionHUD the model is loaded — shows the Share button
            SpaceSessionHUD.Instance?.OnModelLoaded(modelDisplayName, _currentModelKey);

            CurrentModelKey = _currentModelKey;

            // Fetch this model's part knowledge file (labels, functions, motion), if it has one
            if (!swap) AuraSync.AI.PartKnowledge.Ensure().OnModelLoaded(_currentModelKey, interactionCtrl != null ? interactionCtrl.ModelRoot : modelAnchor);

            SharedStateBus.Publish(StateKeys.ModelLoaded, _currentModelKey, isLocal: true);
        }


        // ── Phase 13: preview first, full file after ───────────────────────────

        private int _loadGeneration;
        private Coroutine _fullDownload;

        private static bool IsRemote(string url) =>
            url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>The catalogue entry for a model URL, or null (offline, or not in the catalogue). Gives up after 4 s.</summary>
        private static async System.Threading.Tasks.Task<ModelData> FindCatalogEntry(string url)
        {
            ModelCatalog catalog = ModelCatalog.Instance;
            if (catalog == null) catalog = new GameObject("ModelCatalog").AddComponent<ModelCatalog>();
            var done = new System.Threading.Tasks.TaskCompletionSource<ModelData>();
            catalog.FindByGlbUrl(url, entry => done.TrySetResult(entry));
            var timeout = System.Threading.Tasks.Task.Delay(4000);
            var first = await System.Threading.Tasks.Task.WhenAny(done.Task, timeout);
            return first == done.Task ? done.Task.Result : null;
        }

        private void OnPreviewShown(string fullUrl, int generation)
        {
            ModelVariants.RequestFull = () => StartFullDownload(fullUrl, generation);
            if (ModelVariants.ShouldFetchFullNow()) StartFullDownload(fullUrl, generation);
            else ModelVariants.Changed(); // the pill offers GET FULL
        }

        private void StartFullDownload(string fullUrl, int generation)
        {
            if (generation != _loadGeneration || _fullDownload != null || !ModelVariants.ShowingPreview) return;
            _fullDownload = StartCoroutine(DownloadFull(fullUrl, generation));
        }

        private void StopFullDownload()
        {
            if (_fullDownload != null) StopCoroutine(_fullDownload);
            _fullDownload = null;
            ModelVariants.RequestFull = null;
        }

        private IEnumerator DownloadFull(string url, int generation)
        {
            ModelVariants.DownloadingFull();
            byte[] data;
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 900;
                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                    if (generation != _loadGeneration) { request.Abort(); _fullDownload = null; yield break; }
                    ModelVariants.FullProgress = request.downloadProgress;
                    yield return null;
                }
                if (generation != _loadGeneration) { _fullDownload = null; yield break; }
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[SpaceSceneManager] Full model download failed: {request.error}");
                    ModelVariants.FullFailed(request.error);
                    _fullDownload = null;
                    yield break;
                }
                data = request.downloadHandler.data;
            }

            // Kept, so next time the full model opens straight away.
            try
            {
                string cachePath = GetCacheFilePath(url);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath));
                System.IO.File.WriteAllBytes(cachePath, data);
                TrimModelCache(cachePath);
            }
            catch (Exception ex) { Debug.LogWarning($"[SpaceSceneManager] Could not cache the full model: {ex.Message}"); }

            // The reassembly game moves parts by hand; swap once it is over.
            while (AuraSync.Challenge.ReassemblyGame.IsActive)
            {
                if (generation != _loadGeneration) { _fullDownload = null; yield break; }
                yield return null;
            }
            _fullDownload = null;
            SwapToFullAsync(data, url, generation);
        }

        /// <summary>
        /// Puts the full model where the preview is, without interrupting anything: the preview's
        /// parts are replaced by the full file's (same part tree, same paths), every feature is told
        /// as on a load, and what was on screen is put back - pose, explode, isolation, this phone's
        /// selection, the section cut and the simulation. The model's key never changes, so the room,
        /// notes, recording, cones and quiz carry on as if nothing happened.
        /// </summary>
        private async void SwapToFullAsync(byte[] data, string url, int generation)
        {
            ModelVariants.Swapping();
            var logger = new GLTFLogger();
            var gltf = new GltfImport(logger: logger);
            bool ok;
            try { ok = await gltf.Load(data, new Uri(url)); }
            catch (Exception ex) { Debug.LogWarning($"[SpaceSceneManager] Full model parse failed: {ex.Message}"); ok = false; }
            if (this == null || generation != _loadGeneration) return;
            if (!ok) { ModelVariants.FullFailed(logger.LastError); return; }

            // Build it off to the side (inactive), so there is never a frame without a model.
            var holder = new GameObject("FullModelIncoming").transform;
            holder.SetParent(modelAnchor, false);
            holder.gameObject.SetActive(false);
            bool placed;
            try { placed = await gltf.InstantiateMainSceneAsync(holder); }
            catch (Exception ex) { Debug.LogWarning($"[SpaceSceneManager] Full model instantiate failed: {ex.Message}"); placed = false; }
            if (this == null) return;
            if (!placed || generation != _loadGeneration) { if (holder != null) Destroy(holder.gameObject); if (!placed) ModelVariants.FullFailed("could not build it"); return; }

            // What is on screen now
            var sic = SpaceInteractionController.Instance;
            bool wasLocked = sic == null || sic.IsSyncLocked;
            Vector3 pos = modelAnchor.position, scale = modelAnchor.localScale;
            Quaternion rot = modelAnchor.rotation;
            float explode = ModelExploder.Instance != null ? ModelExploder.Instance.Amount : 0f;
            string iso = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "";
            string localSel = ModelPartSelector.Instance != null ? ModelPartSelector.Instance.LocalPath : "";
            SectionCutter.CutState cut = SectionCutter.Instance != null ? SectionCutter.Instance.State : SectionCutter.CutState.Off;
            var simCtrl = AuraSync.Simulation.SimulationController.Instance;
            AuraSync.Simulation.SimState sim = simCtrl != null ? simCtrl.State : AuraSync.Simulation.SimState.Off;
            int partsBefore = modelAnchor.GetComponentsInChildren<Renderer>(true).Length;

            // Out with the preview (now, so child indices - the part paths - are right this frame),
            // in with the full model at the same local places.
            for (int i = modelAnchor.childCount - 1; i >= 0; i--)
            {
                Transform c = modelAnchor.GetChild(i);
                if (c != holder) DestroyImmediate(c.gameObject);
            }
            while (holder.childCount > 0) holder.GetChild(0).SetParent(modelAnchor, false);
            DestroyImmediate(holder.gameObject);

            ModelRaycaster.ClearCache();
            materialHelper?.FixAllMaterials(modelAnchor);
            await SimplifyModelAsync(modelAnchor);
            if (this == null || generation != _loadGeneration) return;

            int partsAfter = modelAnchor.GetComponentsInChildren<Renderer>(true).Length;
            if (partsAfter != partsBefore)
                Debug.LogWarning($"[SpaceSceneManager] The full model has {partsAfter} parts, the preview had {partsBefore}: re-make the preview.");

            AfterModelPlaced(swap: true);

            // Put back what was on screen.
            modelAnchor.SetPositionAndRotation(pos, rot);
            modelAnchor.localScale = scale;
            sic?.RestorePose(pos, rot, scale);
            if (sic != null && !wasLocked) sic.BreakSync();
            SectionCutter.Instance?.ApplyQuietly(cut);
            ModelExploder.Instance?.ApplyQuietly(explode);
            ModelIsolator.Instance?.ApplyQuietly(iso);
            if (!string.IsNullOrEmpty(localSel)) ModelPartSelector.Instance?.ShowLocalHighlight(localSel);
            if (simCtrl != null && sim.mode != AuraSync.Simulation.SimMode.None) simCtrl.ApplyReplay(sim);

            ModelVariants.Swapped(data.Length);
            Toast.Show("Full quality model loaded.");
            Debug.Log("[SpaceSceneManager] Swapped the preview for the full model.");
        }

        /// <summary>
        /// Puts a material failure on screen. adb will not attach to these phones reliably, so a
        /// model rendering white needs to say why on the device itself rather than only in a log.
        /// Silent when nothing is wrong.
        /// </summary>
        private void ShowMaterialProblem()
        {
            if (hintLabel == null) return;

            string problem = GLTFastMaterialHelper.LastProblem;
            hintLabel.text = problem;
            hintLabel.gameObject.SetActive(!string.IsNullOrEmpty(problem));
        }

        /// <summary>
        /// Leaves the 3D viewer and comes back to the model list, which is what someone pressing
        /// Back in the viewer is actually after - not the bare AR camera.
        /// </summary>
        public static void ReturnToModelList()
        {
            // File how this model was left, so reopening it (on any device in the room, if this is
            // the presenter) brings it back the same way.
            ModelStateMemory.CaptureCurrent();

            PlayerPrefs.SetInt(ModelRepositoryUI.OpenOnLoadKey, 1);
            PlayerPrefs.Save();
            SceneManager.LoadScene(_mainScene);
        }

        /// <summary>
        /// Keeps Back working even when the scene asset still carries an old scene name.
        /// SpaceScene was shipped pointing at "MainScene", which is not in Build Settings, so
        /// pressing Back did nothing at all.
        /// </summary>
        private static string ResolveMainScene(string configured)
        {
            if (!string.IsNullOrEmpty(configured) && Application.CanStreamedLevelBeLoaded(configured))
                return configured;

            if (!string.IsNullOrEmpty(configured))
            {
                Debug.LogWarning($"[SpaceSceneManager] Main scene '{configured}' is not in Build Settings; " +
                                 $"falling back to '{DefaultMainScene}'.");
            }

            return DefaultMainScene;
        }

        /// <summary>
        /// Decimates heavy meshes. Runs one mesh per frame so a 1M-triangle model does not freeze
        /// the app (Android ANR), and simplifies each distinct mesh only once even when several
        /// parts share it.
        /// </summary>
        private async System.Threading.Tasks.Task SimplifyModelAsync(Transform root)
        {
            var simplified = new System.Collections.Generic.Dictionary<Mesh, Mesh>();

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (this == null) return;

                Mesh sourceMesh = filter.sharedMesh;
                if (sourceMesh == null) continue;

                if (simplified.TryGetValue(sourceMesh, out Mesh already))
                {
                    filter.sharedMesh = already;
                    continue;
                }

                int indexCount = 0;
                for (int i = 0; i < sourceMesh.subMeshCount; i++) indexCount += (int)sourceMesh.GetIndexCount(i);

                Mesh result = sourceMesh;
                if (indexCount / 3 > 20000)
                {
                    float quality = 0.3f;
                    var meshSimplifier = new MeshSimplifier();
                    meshSimplifier.Initialize(sourceMesh);
                    meshSimplifier.SimplifyMesh(quality);
                    result = meshSimplifier.ToMesh();
                    filter.sharedMesh = result;

                    await System.Threading.Tasks.Task.Yield(); // let a frame render between heavy meshes
                }
                simplified[sourceMesh] = result;
            }
        }

        private const long MaxModelCacheBytes = 512L * 1024 * 1024;

        /// <summary>Deletes the least recently used cached models until the cache fits.</summary>
        private void TrimModelCache(string keepPath)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(keepPath);
                if (!System.IO.Directory.Exists(dir)) return;

                var files = new System.IO.DirectoryInfo(dir).GetFiles();
                long total = 0;
                foreach (var f in files) total += f.Length;
                if (total <= MaxModelCacheBytes) return;

                Array.Sort(files, (x, y) => x.LastWriteTimeUtc.CompareTo(y.LastWriteTimeUtc)); // oldest first
                foreach (var f in files)
                {
                    if (total <= MaxModelCacheBytes) break;
                    if (string.Equals(f.FullName, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                    total -= f.Length;
                    f.Delete();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpaceSceneManager] Cache trim failed: {ex.Message}");
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

    }
}

