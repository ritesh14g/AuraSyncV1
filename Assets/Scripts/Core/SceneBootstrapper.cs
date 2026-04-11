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

            Debug.Log("[AuraSync] ═══════════════════════════════════════");
            Debug.Log("[AuraSync]  AuraSync AR Ecosystem v1.0 Starting");
            Debug.Log("[AuraSync] ═══════════════════════════════════════");
            StartCoroutine(InitializeWithDelay());
        }

        private IEnumerator InitializeWithDelay()
        {
            // Wait until AR session is tracking (not just a couple of frames)
            float timeout = 10f;
            float elapsed = 0f;

            Debug.Log("[AuraSync] SceneBootstrapper: Waiting for AR session to begin tracking...");

            while (ARSession.state < ARSessionState.SessionTracking && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (ARSession.state < ARSessionState.SessionTracking)
                Debug.LogWarning("[AuraSync] SceneBootstrapper: AR session tracking timeout — restoring anyway");
            else
                Debug.Log("[AuraSync] SceneBootstrapper: AR session ready. Restoring saved items.");

            // Wait one extra frame so all Awake()/Start() on other scripts have run
            yield return new WaitForEndOfFrame();

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
    }
}