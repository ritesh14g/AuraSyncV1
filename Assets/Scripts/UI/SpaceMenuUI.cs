using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// Manages the SPACE entry menu and bottom sheet.
    /// </summary>
    public class SpaceMenuUI : MonoBehaviour
    {
        [Header("Entry Button")]
        [SerializeField] private Button spaceButton; // top-right corner button

        [Header("Sheet")]
        [SerializeField] private RectTransform bottomSheet;
        [SerializeField] private CanvasGroup   sheetGroup;
        [SerializeField] private Image         backdrop;
        [SerializeField] private CanvasGroup   backdropGroup;

        [Header("Menu Buttons")]
        [SerializeField] private Button makeModelButton;
        [SerializeField] private Button yourModelsButton;
        [SerializeField] private Button closeButton;

        [Header("Panels")]
        [SerializeField] private ModelRepositoryUI modelRepository;

        [Header("Animation")]
        [SerializeField] private float animDuration = 0.25f;

        private float _sheetHeight;
        private Coroutine _animCoroutine;
        private bool _isOpen = false;

        void Awake()
        {
            // Calculate sheet height for animation
            _sheetHeight = bottomSheet.rect.height;
            if (_sheetHeight <= 0) _sheetHeight = 300f; // Fallback

            // Hide immediately
            bottomSheet.anchoredPosition = new Vector2(0, -_sheetHeight);
            SetGroupActive(sheetGroup, false);
            SetGroupActive(backdropGroup, false);

            // Wire buttons
            spaceButton?.onClick.AddListener(OpenSheet);
            makeModelButton?.onClick.AddListener(OnMakeModelPressed);
            yourModelsButton?.onClick.AddListener(OnYourModelsPressed);
            closeButton?.onClick.AddListener(CloseSheet);
            
            // Backdrop tap closes sheet
            if (backdrop)
            {
                var trigger = backdrop.gameObject.GetComponent<Button>() ?? backdrop.gameObject.AddComponent<Button>();
                trigger.onClick.AddListener(CloseSheet);
            }
        }

        public void OpenSheet()
        {
            if (_isOpen) return;
            _isOpen = true;
            Animate(true);
        }

        public void CloseSheet()
        {
            if (!_isOpen) return;
            _isOpen = false;
            Animate(false);
        }

        private void Animate(bool opening)
        {
            if (_animCoroutine != null) StopCoroutine(_animCoroutine);
            _animCoroutine = StartCoroutine(AnimateSheetRoutine(opening));
        }

        private IEnumerator AnimateSheetRoutine(bool opening)
        {
            float targetAlpha = opening ? 1f : 0f;
            float targetY     = opening ? 0f : -_sheetHeight;
            float startAlpha  = sheetGroup.alpha;
            Vector2 startPos  = bottomSheet.anchoredPosition;
            Vector2 targetPos = new Vector2(0, targetY);

            if (opening)
            {
                SetGroupActive(sheetGroup, true);
                SetGroupActive(backdropGroup, true);
            }

            float t = 0f;
            while (t < animDuration)
            {
                t += Time.deltaTime;
                float progress = t / animDuration;
                // Simple ease out
                float curve = 1f - Mathf.Pow(1f - progress, 3);

                sheetGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, curve);
                backdropGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, curve);
                bottomSheet.anchoredPosition = Vector2.Lerp(startPos, targetPos, curve);
                yield return null;
            }

            sheetGroup.alpha = targetAlpha;
            backdropGroup.alpha = targetAlpha;
            bottomSheet.anchoredPosition = targetPos;

            if (!opening)
            {
                SetGroupActive(sheetGroup, false);
                SetGroupActive(backdropGroup, false);
            }

            _animCoroutine = null;
        }

        private void SetGroupActive(CanvasGroup group, bool active)
        {
            if (group == null) return;
            group.alpha = active ? 1f : 0f;
            group.interactable = active;
            group.blocksRaycasts = active;
        }

        private void OnMakeModelPressed()
        {
            CloseSheet();
            // Show "Coming Soon" toast via PlacementFeedbackUI
            var feedback = FindObjectOfType<PlacementFeedbackUI>();
            feedback?.Show("Make a Model: Coming Soon!");
            Debug.Log("[AuraSync] SpaceMenu: Make a Model pressed (Coming Soon)");
        }

        private void OnYourModelsPressed()
        {
            CloseSheet();
            modelRepository?.OpenPanel();
        }
    }
}
