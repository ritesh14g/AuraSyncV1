using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// Bottom-sheet for choosing Plain or Checklist note type.
    /// Passes the choice to NoteEditorUI.OpenSheet(isChecklist).
    /// </summary>
    public class NoteTypeSelectorUI : MonoBehaviour
    {
        [Header("Sheet")]
        [SerializeField] private RectTransform bottomSheet;
        [SerializeField] private CanvasGroup   sheetGroup;
        [SerializeField] private Image         backdrop;
        [SerializeField] private CanvasGroup   backdropGroup;

        [Header("Buttons")]
        [SerializeField] private Button plainButton;
        [SerializeField] private Button checklistButton;
        [SerializeField] private Button backButton;

        [Header("Next Panel")]
        [SerializeField] private NoteEditorUI noteEditor;

        [Header("Animation")]
        [SerializeField] private float animDuration = 0.25f;

        private bool  _isOpen = false;
        private float _sheetHeight;

        void Awake()
        {
            _sheetHeight = bottomSheet.sizeDelta.y;

            plainButton.onClick.AddListener(() => OpenEditor(false));
            checklistButton.onClick.AddListener(() => OpenEditor(true));
            backButton.onClick.AddListener(CloseSheet);

            backdrop.raycastTarget = true;
            var bd = backdrop.gameObject.GetComponent<Button>()
                  ?? backdrop.gameObject.AddComponent<Button>();
            bd.transition = Selectable.Transition.None;
            bd.onClick.AddListener(CloseSheet);

            HideImmediate();
        }

        private void HideImmediate()
        {
            bottomSheet.anchoredPosition = new Vector2(0, -_sheetHeight);
            sheetGroup.alpha    = 0f;  sheetGroup.interactable    = false;  sheetGroup.blocksRaycasts    = false;
            backdropGroup.alpha = 0f;  backdropGroup.interactable = false;  backdropGroup.blocksRaycasts = false;
            if (backdrop != null) backdrop.raycastTarget = false;
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

        private void OpenEditor(bool isChecklist)
        {
            CloseSheet();
            noteEditor?.OpenSheet(isChecklist);
        }

        private IEnumerator AnimateSheet(bool open)
        {
            if (open)
            {
                backdropGroup.alpha = 1f; backdropGroup.blocksRaycasts = true; backdropGroup.interactable = false;
                if (backdrop != null) backdrop.raycastTarget = true;
                sheetGroup.interactable = true; sheetGroup.blocksRaycasts = true;
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
                sheetGroup.alpha             = Mathf.Lerp(startAlpha, targetAlpha, t);
                backdropGroup.alpha          = Mathf.Lerp(startBD,    targetBD,    t);
                yield return null;
            }

            bottomSheet.anchoredPosition = new Vector2(0, targetY);
            sheetGroup.alpha    = targetAlpha;
            backdropGroup.alpha = targetBD;

            if (!open)
            {
                sheetGroup.interactable = false;    sheetGroup.blocksRaycasts = false;
                backdropGroup.interactable = false; backdropGroup.blocksRaycasts = false;
                if (backdrop != null) backdrop.raycastTarget = false;
            }
        }
    }
}