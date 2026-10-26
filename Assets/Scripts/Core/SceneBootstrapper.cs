using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    /// It lives on V1's AuraSyncManagers object, which its managers make DontDestroyOnLoad. When
    /// V1 loads again (Back from the 3D viewer), the scene's fresh copy of that object is destroyed
    /// as a duplicate before its Start runs, so this surviving bootstrapper sets the reloaded scene
    /// up itself (launch screen, managers, AR wait, saved items, Idle) from SceneManager.sceneLoaded.
    /// Without that, coming back left a bare camera view with nothing working.
    /// </summary>
    public class SceneBootstrapper : MonoBehaviour
    {
        [Header("Environment")]
        [Tooltip("Default environment name. Change to support multiple environments.")]
        [SerializeField] private string defaultEnvironmentName = "Default";

        // ✓ CORRECT — non-static: resets to false on every app start
        private bool _hasInitialized = false;

        private string _homeScene;
        private Coroutine _init;

        void Start()
        {
            if (_hasInitialized) return;
            _hasInitialized = true;

            _homeScene = SceneManager.GetActiveScene().name;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SetUpScene();
        }

        void OnDestroy()
        {
            if (_hasInitialized) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>V1 again (Back from the 3D viewer): set the fresh scene up as at launch.</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (_init != null) { StopCoroutine(_init); _init = null; } // an AR wait from the last visit
            if (scene.name != _homeScene) return;
            Debug.Log($"[AuraSync] SceneBootstrapper: back in {scene.name}; setting it up again.");
            SetUpScene();
        }

        private void SetUpScene()
        {
            // The launch screen first: whatever else fails below, the person can still use the app
            // (coming back from the 3D viewer once left only a black camera view and the old ROOM
            // square when something here threw before the onboarding was created).
            Safe("onboarding", UI.OnboardingFlow.EnsureExists);

            // Networking, voice and the lobby do not depend on AR tracking, and UI scripts
            // subscribe to these managers in their own Start(). Create them first, before the
            // (up to ~45 s) AR wait, so nothing subscribes to a manager that does not exist yet.
            EnsureNetworkingManagers();
            Debug.Log("[AuraSync] Collaboration managers initialized.");

            Debug.Log("[AuraSync] ═══════════════════════════════════════");
            Debug.Log("[AuraSync]  AuraSync AR Ecosystem v1.0 Starting");
            Debug.Log("[AuraSync] ═══════════════════════════════════════");
            _init = StartCoroutine(InitializeWithDelay());
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
            yield return WaitForARSession();

            // Wait one extra frame so all Awake()/Start() on other scripts have run
            yield return null;

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
            _init = null;
        }

        /// <summary>
        /// Runs one start-up step; an exception is logged and the next step still runs, so one
        /// broken manager can no longer take the rest of the app down with it.
        /// </summary>
        private static void Safe(string what, System.Action step)
        {
            try { step(); }
            catch (System.Exception ex) { Debug.LogError($"[AuraSync] Start-up step '{what}' failed: {ex}"); }
        }

        private void EnsureNetworkingManagers()
        {
            // Admin/zone state. NetworkManager and the HUDs read ZoneManager.IsAdmin, so it must
            // exist even if the scene did not ship one.
            Safe("zone", () =>
            {
                if (Networking.ZoneManager.Instance == null)
                {
                    GameObject zoneGo = new GameObject("AuraSync_ZoneManager");
                    zoneGo.AddComponent<Networking.ZoneManager>();
                }
            });

            Safe("network", () => Networking.NetworkManager.EnsureExists());

            // Who drives explode / isolate / highlight for the room (V3 Phase 1).
            Safe("control ownership", () => Networking.ControlOwnership.EnsureExists());

            Safe("presence", () =>
            {
                if (Networking.UserPresence.Instance == null)
                {
                    GameObject presGo = new GameObject("AuraSync_UserPresence");
                    var pv = presGo.AddComponent<Photon.Pun.PhotonView>();
                    pv.ViewID = Networking.UserPresence.PresenceViewId;
                    Photon.Pun.PhotonNetwork.RegisterPhotonView(pv);
                    presGo.AddComponent<Networking.UserPresence>();
                }
            });

            Safe("voice", () =>
            {
                if (Networking.VoiceChatManager.Instance == null)
                {
                    GameObject voiceGo = new GameObject("AuraSync_VoiceChatManager");
                    voiceGo.AddComponent<Networking.VoiceChatManager>();
                }
            });

            Safe("admin broadcast", () =>
            {
                if (Networking.AdminBroadcastManager.Instance == null)
                {
                    GameObject adminGo = new GameObject("AuraSync_AdminBroadcastManager");
                    adminGo.AddComponent<Networking.AdminBroadcastManager>();
                }
            });

            Safe("model sync", () =>
            {
                if (Networking.ModelSyncManager.Instance == null)
                {
                    GameObject modelSyncGo = new GameObject("AuraSync_ModelSyncManager");
                    var pv = modelSyncGo.AddComponent<Photon.Pun.PhotonView>();
                    pv.ViewID = 995;
                    Photon.Pun.PhotonNetwork.RegisterPhotonView(pv);
                    modelSyncGo.AddComponent<Networking.ModelSyncManager>();
                }
            });

            Safe("follow presenter", () =>
            {
                if (Networking.FollowAdminController.Instance == null)
                {
                    GameObject followGo = new GameObject("AuraSync_FollowAdminController");
                    followGo.AddComponent<Networking.FollowAdminController>();
                }
            });

            Safe("room lobby", () =>
            {
                if (UI.RoomLobbyUI.Instance == null)
                {
                    GameObject lobbyGo = new GameObject("AuraSync_RoomLobby");
                    lobbyGo.AddComponent<UI.RoomLobbyUI>();
                }
            });

            // Joins a room when the app is opened from an invite link
            Safe("deep links", () =>
            {
                if (Networking.DeepLinkHandler.Instance == null)
                {
                    GameObject linkGo = new GameObject("AuraSync_DeepLinkHandler");
                    linkGo.AddComponent<Networking.DeepLinkHandler>();
                }
            });

            // The launch screen (Play / Interactive) and everything before a room. Built in code;
            // switch it off with OnboardingFlow.UseNewOnboarding to get the old screens back.
            // (Also made first thing in Start; this is a no-op when it already exists.)
            Safe("onboarding", UI.OnboardingFlow.EnsureExists);
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




