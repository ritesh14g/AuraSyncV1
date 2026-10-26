using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using TMPro;
using UnityEngine.UI;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// Manages the AR placement hint text and temporary feedback messages.
    ///
    /// SETUP:
    ///   Attach to the ARHintText GameObject in the hierarchy.
    ///   Wire hintText    → ARHintText TMP
    ///   Wire feedbackText → PlacementFeedback TMP
    ///
    /// FIX v1.2:
    ///   - Text colour forced to visible yellow so it reads over the AR camera view.
    ///   - Hint shown whenever app enters PlacingNote or PlacingObject mode.
    ///   - ShowMiss() called by ARPlacementController on every raycast miss,
    ///     flashes a "No surface found" message so the user gets feedback.
    ///   - All text invisible (alpha 0) when in Idle mode.
    /// </summary>
    public class ARHintUI : MonoBehaviour
    {
        public static ARHintUI Instance { get; private set; }

        [Header("Text References")]
        [Tooltip("The permanent hint shown while in placement mode.")]
        [SerializeField] private TextMeshProUGUI hintText;

        [Tooltip("The temporary flash message shown on raycast miss.")]
        [SerializeField] private TextMeshProUGUI feedbackText;

        [Header("AR Scanning Hint")]
        [Tooltip("Assign in Inspector or it will be auto-found.")]
        [SerializeField] private ARPlaneManager arPlaneManager;
        [SerializeField] private CanvasGroup    canvasGroup;
        [SerializeField] private float          detectedMessageDuration = 2.5f;

        [Header("Hint Settings")]
        [SerializeField] private float feedbackDuration = 2.0f;

        // ── Colours ──
        private static readonly Color HintColor     = new Color(1.00f, 0.92f, 0.23f, 1f);   // bright yellow
        private static readonly Color FeedbackColor = new Color(1.00f, 0.45f, 0.35f, 1f);   // coral red
        private static readonly Color HiddenColor   = new Color(1f, 1f, 1f, 0f);

        private AppStateManager _appState;
        private Coroutine       _feedbackCoroutine;
        private bool            _planeDetected = false;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_appState != null) _appState.OnModeChanged -= OnModeChanged;
            if (arPlaneManager != null) arPlaneManager.planesChanged -= OnPlanesChanged;
        }

        void Start()
        {
            _appState = FindObjectOfType<AppStateManager>();
            if (_appState != null)
                _appState.OnModeChanged += OnModeChanged;

            // Auto-find ARPlaneManager if not assigned in Inspector
            if (arPlaneManager == null)
                arPlaneManager = FindObjectOfType<ARPlaneManager>();

            if (arPlaneManager == null)
                Debug.LogError("[AuraSync] ARHintUI: ARPlaneManager not found! Assign it in Inspector.");
            else
                arPlaneManager.planesChanged += OnPlanesChanged;

            // Set up CanvasGroup if missing
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 1f;

            // Force visible text styles — these were white (invisible) in the Inspector
            if (hintText)
            {
                // Show scanning hint immediately on app open
                hintText.text      = "Move camera slowly to detect surfaces";
                // The new onboarding UI shows its own placing hint; this one would sit under it.
                hintText.color     = OnboardingFlow.UseNewOnboarding ? HiddenColor : HintColor;
                hintText.fontSize  = 18f;
                hintText.fontStyle = FontStyles.Bold;
            }

            if (feedbackText)
            {
                feedbackText.color     = HiddenColor;
                feedbackText.fontSize  = 16f;
                feedbackText.fontStyle = FontStyles.Bold;
            }

            SetFeedbackVisible(false);
            Debug.Log("[AuraSync] ARHintUI: Started scanning hint.");
        }

        // ─────────────────────────────────────────────────────────────
        //  AR Plane Detection — scanning hint
        // ─────────────────────────────────────────────────────────────

        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            if (_planeDetected) return;
            if (args.added.Count == 0) return;

            _planeDetected = true;

            // Unsubscribe — we only care about the FIRST plane
            if (arPlaneManager != null)
                arPlaneManager.planesChanged -= OnPlanesChanged;

            StartCoroutine(ShowDetectedThenRestore());
            Debug.Log("[AuraSync] ARHintUI: First plane detected!");
        }

        private IEnumerator ShowDetectedThenRestore()
        {
            if (OnboardingFlow.UseNewOnboarding) yield break; // its fade would flash the hidden hint

            if (hintText != null)
                hintText.text = "Surface detected \u2014 tap to place";

            // Hold for a moment
            yield return new WaitForSeconds(detectedMessageDuration);

            // Fade out hint (unless we're in a placement mode that re-shows it)
            if (_appState == null || _appState.CurrentMode == AppMode.Idle)
            {
                float t = 0f;
                float fadeDuration = 0.4f;
                while (t < fadeDuration)
                {
                    t += Time.deltaTime;
                    if (hintText) hintText.color = Color.Lerp(HintColor, HiddenColor, t / fadeDuration);
                    yield return null;
                }
                if (hintText) hintText.color = HiddenColor;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Mode changes
        // ─────────────────────────────────────────────────────────────

        private void OnModeChanged(AppMode mode)
        {
            switch (mode)
            {
                case AppMode.PlacingNote:
                    ShowHint("Tap a flat surface to place note");
                    break;
                case AppMode.PlacingObject:
                    ShowHint("Tap a flat surface to place object");
                    break;
                case AppMode.Idle:
                    HideHint();
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Public API — called by ARPlacementController
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Call this from ARPlacementController every time a raycast misses a surface.
        /// Shows a temporary red "No surface found" message.
        /// </summary>
        public void ShowMiss()
        {
            if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
            _feedbackCoroutine = StartCoroutine(FlashFeedback(
                "No surface detected — move camera over a flat area"));
        }

        // ─────────────────────────────────────────────────────────────
        //  Internal helpers
        // ─────────────────────────────────────────────────────────────

        private void ShowHint(string message)
        {
            if (!hintText || OnboardingFlow.UseNewOnboarding) return;
            hintText.text  = message;
            hintText.color = HintColor;
        }

        private void HideHint()
        {
            if (!hintText) return;
            hintText.color = HiddenColor;

            // Also hide any lingering feedback
            if (feedbackText) feedbackText.color = HiddenColor;
            if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
        }

        private void SetHintVisible(bool visible)
        {
            if (hintText) hintText.color = visible ? HintColor : HiddenColor;
        }

        private void SetFeedbackVisible(bool visible)
        {
            if (feedbackText) feedbackText.color = visible ? FeedbackColor : HiddenColor;
        }

        private IEnumerator FlashFeedback(string message)
        {
            if (!feedbackText) yield break;

            feedbackText.text  = message;
            feedbackText.color = FeedbackColor;

            yield return new WaitForSeconds(feedbackDuration);

            // Fade out
            float elapsed = 0f;
            float fadeTime = 0.4f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                float a = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
                feedbackText.color = new Color(FeedbackColor.r, FeedbackColor.g, FeedbackColor.b, a);
                yield return null;
            }

            feedbackText.color = HiddenColor;
            _feedbackCoroutine = null;
        }
    }
}