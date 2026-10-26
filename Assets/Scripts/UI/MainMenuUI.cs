using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// FAB + slide-up creation sheet.
    ///
    /// FIX v2.0 — CRITICAL:
    ///   The old guard `if (mode != Idle) return` made the FAB permanently dead
    ///   after entering PlacingNote or PlacingObject mode. The user could tap the
    ///   + button forever and nothing would happen.
    ///
    ///   New behaviour:
    ///     • Idle mode   → FAB opens/closes the creation sheet as before.
    ///     • Placing mode → FAB CANCELS the current placement, returns to Idle,
    ///       and optionally re-opens the sheet so the user can try again.
    ///     • Any other mode → FAB is a no-op (safe fallback).
    ///
    ///   This mirrors how every good AR app works — the same button that starts
    ///   placement also cancels it.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Button fabButton;
        [SerializeField] private RectTransform bottomSheet;
        [SerializeField] private CanvasGroup   sheetGroup;
        [SerializeField] private Image         backdrop;
        [SerializeField] private CanvasGroup   backdropGroup;
        [SerializeField] private Button        noteButton;
        [SerializeField] private Button        objectButton;
        [SerializeField] private Button        cancelButton;

        [Header("Other Panels")]
        [SerializeField] private NoteTypeSelectorUI noteTypeSelector;
        [SerializeField] private ObjectMenuUI       objectMenu;

        [Header("Animation")]
        [SerializeField] private float animDuration = 0.28f;

        private bool  _isOpen = false;
        private float _sheetHeight;
        private AppStateManager _appState;

        // FAB icon references — set if you have a TextMeshPro label on the FAB
        private TextMeshProUGUI _fabLabel;

        void Awake()
        {
            _appState = FindObjectOfType<AppStateManager>();

            fabButton.onClick.AddListener(OnFABPressed);
            cancelButton?.onClick.AddListener(CloseSheet);
            noteButton?.onClick.AddListener(OnNotePressed);
            objectButton?.onClick.AddListener(OnObjectPressed);

            // Backdrop tap closes sheet — but does NOT block raycasts until opened
            if (backdrop != null)
            {
                backdrop.raycastTarget = false; // OFF by default so it never blocks clicks
                var bd = backdrop.gameObject.GetComponent<Button>()
                      ?? backdrop.gameObject.AddComponent<Button>();
                bd.transition = Selectable.Transition.None;
                bd.onClick.AddListener(CloseSheet);
            }

            // Cache FAB label if present (optional)
            _fabLabel = fabButton.GetComponentInChildren<TextMeshProUGUI>();

            // The new launch screen replaces the + button and its sheet.
            if (OnboardingFlow.UseNewOnboarding) fabButton.gameObject.SetActive(false);
        }

        void Start()
        {
            // CRITICAL: sizeDelta is 0 during Awake because the layout hasn't run.
            // We must read it in Start (or later) after Unity has computed it.
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(
                bottomSheet.GetComponent<RectTransform>() ?? bottomSheet);
            _sheetHeight = bottomSheet.sizeDelta.y;
            if (_sheetHeight <= 0f) _sheetHeight = 320f; // safe fallback

            // Now we can safely hide the sheet below the screen
            HideImmediate();
        }

        // ─────────────────────────────────────────────────────────────
        //  Sheet lifecycle
        // ─────────────────────────────────────────────────────────────

        private void HideImmediate()
        {
            bottomSheet.anchoredPosition = new Vector2(0, -_sheetHeight);
            sheetGroup.alpha    = 0f; sheetGroup.interactable    = false; sheetGroup.blocksRaycasts    = false;
            backdropGroup.alpha = 0f; backdropGroup.interactable = false; backdropGroup.blocksRaycasts = false;
        }

        public void OpenSheet()
        {
            if (_isOpen) return;
            _isOpen = true;
            StopAllCoroutines();
            StartCoroutine(AnimateSheet(true));
        }

        public void CloseSheet()
        {
            if (!_isOpen) return;
            _isOpen = false;
            StopAllCoroutines();
            StartCoroutine(AnimateSheet(false));
        }

        // ─────────────────────────────────────────────────────────────
        //  FAB press — THE KEY FIX
        // ─────────────────────────────────────────────────────────────

        private void OnFABPressed()
        {
            if (_appState == null) { ToggleSheet(); return; }

            switch (_appState.CurrentMode)
            {
                case AppMode.Idle:
                    // Normal toggle
                    ToggleSheet();
                    break;

                case AppMode.PlacingNote:
                case AppMode.PlacingObject:
                    // ── FIX: cancel placement, return to idle ──
                    // The user pressed + again to bail out of placement mode.
                    Debug.Log("[AuraSync] FAB pressed during placement — cancelling placement, returning to Idle.");
                    _appState.ReturnToIdle();
                    // Also close any open panels just in case
                    if (_isOpen) CloseSheet();
                    break;

                // (Selection is handled automatically when tapping items)

                default:
                    // Safe fallback
                    break;
            }
        }

        private void ToggleSheet()
        {
            if (_isOpen) CloseSheet();
            else OpenSheet();
        }

        // ─────────────────────────────────────────────────────────────
        //  Sheet animation
        // ─────────────────────────────────────────────────────────────

        private IEnumerator AnimateSheet(bool open)
        {
            if (open)
            {
                backdropGroup.alpha          = 1f;
                backdropGroup.interactable   = false;
                backdropGroup.blocksRaycasts = true;
                if (backdrop != null) backdrop.raycastTarget = true;
                sheetGroup.interactable      = true;
                sheetGroup.blocksRaycasts    = true;
            }

            float startY      = bottomSheet.anchoredPosition.y;
            float targetY     = open ? 0f : -_sheetHeight;
            float startAlpha  = sheetGroup.alpha;
            float targetAlpha = open ? 1f : 0f;
            float startBD     = backdropGroup.alpha;
            float targetBD    = open ? 1f : 0f;

            float elapsed = 0f;
            while (elapsed < animDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / animDuration);
                bottomSheet.anchoredPosition = new Vector2(0, Mathf.Lerp(startY, targetY, t));
                sheetGroup.alpha    = Mathf.Lerp(startAlpha, targetAlpha, t);
                backdropGroup.alpha = Mathf.Lerp(startBD, targetBD, t);
                yield return null;
            }

            bottomSheet.anchoredPosition = new Vector2(0, targetY);
            sheetGroup.alpha    = targetAlpha;
            backdropGroup.alpha = targetBD;

            if (!open)
            {
                sheetGroup.interactable      = false;
                sheetGroup.blocksRaycasts    = false;
                backdropGroup.interactable   = false;
                backdropGroup.blocksRaycasts = false;
                if (backdrop != null) backdrop.raycastTarget = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Button handlers
        // ─────────────────────────────────────────────────────────────

        private void OnNotePressed()
        {
            CloseSheet();
            noteTypeSelector?.OpenSheet();
        }

        private void OnObjectPressed()
        {
            CloseSheet();
            objectMenu?.OpenSheet();
        }

        // ─────────────────────────────────────────────────────────────
        //  FAB rotation — rotates to × when sheet is open OR in placement mode
        // ─────────────────────────────────────────────────────────────

        void Update()
        {
            bool showX = _isOpen
                || (_appState != null && (
                    _appState.CurrentMode == AppMode.PlacingNote ||
                    _appState.CurrentMode == AppMode.PlacingObject));

            float targetRot = showX ? 45f : 0f;
            fabButton.transform.localEulerAngles = new Vector3(0, 0,
                Mathf.LerpAngle(fabButton.transform.localEulerAngles.z,
                                targetRot, Time.deltaTime * 14f));
        }
    }
}