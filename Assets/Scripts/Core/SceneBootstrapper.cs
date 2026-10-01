using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using AuraSync.Core;
using AuraSync.Data;

namespace AuraSync
{
    /// <summary>
    /// Bootstraps the entire AuraSync system on app startup.
    /// Ensures all managers are initialized and restores saved items.
    /// 
    /// SETUP: Attach to a dedicated "AuraSyncBootstrapper" GameObject in the scene.
    /// This should run AFTER AR Session and XR Origin are initialized.
    ///
    /// STATUS (v1.1): No changes needed here.
    /// _hasInitialized is correctly declared as instance (non-static) bool,
    /// so it resets to false on every fresh app launch. ✓
    /// </summary>
    public class SceneBootstrapper : MonoBehaviour
    {
        [Header("Environment")]
        [Tooltip("Default environment name. Change to support multiple environments.")]
        [SerializeField] private string defaultEnvironmentName = "Default";

        // ✓ CORRECT — non-static: resets to false on every app start
        private bool _hasInitialized = false;

        void Start()
        {
            if (_hasInitialized) return;
            _hasInitialized = true;

            // Lobby must always be available immediately.
            if (UI.RoomLobbyUI.Instance == null)
            {
                GameObject lobbyGo = new GameObject("AuraSync_RoomLobby");
                lobbyGo.AddComponent<UI.RoomLobbyUI>();
                Debug.Log("[AuraSync] Room Lobby created immediately.");
            }

            Debug.Log("[AuraSync] ═══════════════════════════════════════");
            Debug.Log("[AuraSync]  AuraSync AR Ecosystem v1.0 Starting");
            Debug.Log("[AuraSync] ═══════════════════════════════════════");
            StartCoroutine(InitializeWithDelay());
        }

        private IEnumerator InitializeWithDelay()
        {
            // --- EDITOR BYPASS ---
            // Force UI to be interactive immediately in the Editor
            #if UNITY_EDITOR
            if (AppStateManager.Instance != null)
            {
                AppStateManager.Instance.SetMode(AppMode.Idle);
                Debug.Log("[AuraSync] Editor Bypass: UI forced to Idle mode.");
            }
            
            // AUTO-FIX EventSystem for Unity 2022
            var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (es != null && es.GetComponent("StandaloneInputModule") != null && UnityEngine.InputSystem.InputSystem.settings != null)
            {
                Destroy(es.GetComponent("StandaloneInputModule"));
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                Debug.Log("[AuraSync] Editor Bypass: Upgraded EventSystem to New Input System Module.");
            }
            #endif

            Debug.Log("[AuraSync] SceneBootstrapper: Waiting for AR session to begin tracking...");

            // Delegate platform-specific AR wait to a helper so Play Mode works immediately
            yield return StartCoroutine(WaitForARSession());

            // Wait one extra frame so all Awake()/Start() on other scripts have run
            yield return null;

            // Start collaboration systems before persistence restore.
            EnsureNetworkingManagers();
            Debug.Log("[AuraSync] Collaboration managers initialized.");

            if (PersistenceManager.Instance == null)
            {
                Debug.LogError("[AuraSync] PersistenceManager not found!");
                yield break;
            }

            EnvironmentSaveData env = PersistenceManager.Instance.GetOrCreateEnvironment(defaultEnvironmentName);
            AppStateManager.Instance?.SetActiveEnvironment(env.environmentId);
            ARPlacementController.Instance?.RestoreSavedItems(env);
            AppStateManager.Instance?.SetMode(AppMode.Idle);

            Debug.Log($"[AuraSync] Initialized: {env.environmentName} | Notes: {env.notes.Count} | Objects: {env.objects.Count}");
        }

        private void EnsureNetworkingManagers()
        {
            if (Networking.NetworkManager.Instance == null)
            {
                GameObject netGo = new GameObject("AuraSync_NetworkManager");
                var pv = netGo.AddComponent<Photon.Pun.PhotonView>();
                pv.ViewID = 999;
                Photon.Pun.PhotonNetwork.RegisterPhotonView(pv);
                netGo.AddComponent<Networking.NetworkManager>();
            }

            if (Networking.UserPresence.Instance == null)
            {
                GameObject presGo = new GameObject("AuraSync_UserPresence");
                var pv = presGo.AddComponent<Photon.Pun.PhotonView>();
                pv.ViewID = 998;
                Photon.Pun.PhotonNetwork.RegisterPhotonView(pv);
                presGo.AddComponent<Networking.UserPresence>();
            }

            if (Networking.VoiceChatManager.Instance == null)
            {
                GameObject voiceGo = new GameObject("AuraSync_VoiceChatManager");
                voiceGo.AddComponent<Networking.VoiceChatManager>();
            }

            if (Networking.AdminBroadcastManager.Instance == null)
            {
                GameObject adminGo = new GameObject("AuraSync_AdminBroadcastManager");
                adminGo.AddComponent<Networking.AdminBroadcastManager>();
            }

            if (Networking.ModelSyncManager.Instance == null)
            {
                GameObject modelSyncGo = new GameObject("AuraSync_ModelSyncManager");
                var pv = modelSyncGo.AddComponent<Photon.Pun.PhotonView>();
                pv.ViewID = 995;
                Photon.Pun.PhotonNetwork.RegisterPhotonView(pv);
                modelSyncGo.AddComponent<Networking.ModelSyncManager>();
            }

            if (Networking.FollowAdminController.Instance == null)
            {
                GameObject followGo = new GameObject("AuraSync_FollowAdminController");
                followGo.AddComponent<Networking.FollowAdminController>();
            }

            if (UI.RoomLobbyUI.Instance == null)
            {
                GameObject lobbyGo = new GameObject("AuraSync_RoomLobby");
                lobbyGo.AddComponent<UI.RoomLobbyUI>();
            }
        }

        /// <summary>
        /// Handles the AR session wait in a platform-aware way:
        ///  - Unity Editor (Play Mode): skips immediately so the UI is interactive right away.
        ///  - Android device: two-phase wait — first for ARCore to be Ready (handles the
        ///    camera-permission dialog race), then for SessionTracking.
        /// </summary>
        private IEnumerator WaitForARSession()
        {
#if UNITY_EDITOR
            // No AR hardware in the Editor — skip the wait so Play Mode is immediately usable.
            Debug.LogWarning("[AuraSync] SceneBootstrapper: Unity Editor detected — skipping AR wait. " +
                             "UI will be interactive immediately.");
            yield break;
#else
            // ── Phase 1: Wait up to 30 s for ARCore to be Ready ──
            // (covers the camera-permission dialog appearing right after launch)
            float readyTimeout = 30f;
            float readyElapsed = 0f;

            while (ARSession.state < ARSessionState.Ready
                   && ARSession.state != ARSessionState.Unsupported
                   && readyElapsed < readyTimeout)
            {
                readyElapsed += Time.deltaTime;
                yield return null;
            }

            if (ARSession.state == ARSessionState.Unsupported)
            {
                Debug.LogWarning("[AuraSync] SceneBootstrapper: ARCore not supported on this device. Enabling UI for non-AR features.");
                // We don't yield break here anymore! We let it fall through to initialize the UI.
            }

            if (ARSession.state < ARSessionState.Ready)
            {
                Debug.LogWarning($"[AuraSync] SceneBootstrapper: ARCore not Ready after {readyTimeout}s " +
                                 $"(state={ARSession.state}). Camera permission may have been denied.");
                yield break;   // proceed to restore anyway
            }

            Debug.Log($"[AuraSync] SceneBootstrapper: ARCore Ready after {readyElapsed:F1}s. " +
                      "Now waiting for plane tracking...");

            // ── Phase 2: Wait up to 15 s for SessionTracking ──
            float trackTimeout = 15f;
            float trackElapsed = 0f;

            while (ARSession.state < ARSessionState.SessionTracking && trackElapsed < trackTimeout)
            {
                trackElapsed += Time.deltaTime;
                yield return null;
            }

            if (ARSession.state < ARSessionState.SessionTracking)
                Debug.LogWarning("[AuraSync] SceneBootstrapper: AR tracking timeout — restoring anyway.");
            else
                Debug.Log("[AuraSync] SceneBootstrapper: AR session is tracking!");
#endif
        }
    }
}




