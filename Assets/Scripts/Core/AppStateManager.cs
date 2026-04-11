using System;
using UnityEngine;

namespace AuraSync.Core
{
    // ──────────────────────────────────────────────
    //  App Modes
    // ──────────────────────────────────────────────

    public enum AppMode
    {
        Idle,          // default — tap to select existing items
        PlacingNote,   // tap surface to place a note
        PlacingObject  // tap surface to place an object
    }

    // ──────────────────────────────────────────────
    //  Singleton — Global App State Manager
    // ──────────────────────────────────────────────

    /// <summary>
    /// Manages global application state: current mode, active environment,
    /// and provides events for mode transitions.
    /// Attach this to a persistent GameObject in your scene.
    ///
    /// FIX (v1.1): Added OnDestroy() to null the static Instance reference.
    /// Without this, Android process resume pointed Instance at a destroyed object,
    /// causing mode transitions and environment changes to silently fail on restart.
    /// </summary>
    public class AppStateManager : MonoBehaviour
    {
        // ── Singleton ──
        public static AppStateManager Instance { get; private set; }

        // ── Events ──
        public event Action<AppMode> OnModeChanged;
        public event Action<string>  OnEnvironmentChanged;

        // ── State ──
        public AppMode CurrentMode { get; private set; } = AppMode.Idle;
        public string  ActiveEnvironmentId { get; private set; }

        // ── Placement Config (set by UI before entering placement mode) ──
        [HideInInspector] public Data.NoteItemData   pendingNoteConfig;
        [HideInInspector] public Data.ObjectItemData pendingObjectConfig;

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

        // ── FIX: Null the static reference when this object is destroyed ──
        // This ensures a fresh app start gets a new, properly-initialized
        // AppStateManager. The mode resets to Idle correctly on every launch.
        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ──────────────────────────────────────────
        //  Mode Management
        // ──────────────────────────────────────────

        /// <summary>Switch to a new app mode.</summary>
        public void SetMode(AppMode newMode)
        {
            if (CurrentMode == newMode) return;

            AppMode previous = CurrentMode;
            CurrentMode = newMode;

            Debug.Log($"[AuraSync] Mode: {previous} → {newMode}");
            OnModeChanged?.Invoke(newMode);
        }

        /// <summary>Go back to Idle mode.</summary>
        public void ReturnToIdle()
        {
            SetMode(AppMode.Idle);
        }

        // ──────────────────────────────────────────
        //  Environment Management
        // ──────────────────────────────────────────

        /// <summary>Switch active environment (room/space).</summary>
        public void SetActiveEnvironment(string environmentId)
        {
            ActiveEnvironmentId = environmentId;
            Debug.Log($"[AuraSync] Active environment: {environmentId}");
            OnEnvironmentChanged?.Invoke(environmentId);
        }

        // ──────────────────────────────────────────
        //  Placement Helpers
        // ──────────────────────────────────────────

        /// <summary>
        /// Called by the Note Editor UI when the user finishes configuring a note
        /// and is ready to place it in AR.
        /// </summary>
        public void EnterNotePlacementMode(Data.NoteItemData config)
        {
            pendingNoteConfig = config;
            SetMode(AppMode.PlacingNote);
        }

        /// <summary>
        /// Called by the Object Customizer UI when the user picks an object
        /// and is ready to place it in AR.
        /// </summary>
        public void EnterObjectPlacementMode(Data.ObjectItemData config)
        {
            pendingObjectConfig = config;
            SetMode(AppMode.PlacingObject);
        }
    }
}