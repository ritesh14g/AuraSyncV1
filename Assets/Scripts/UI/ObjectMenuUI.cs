using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// Bottom-sheet shape selector grid.
    ///
    /// FIX v1.2:
    ///   - GridLayoutGroup cell sizes are now set from the panel's actual width at
    ///     runtime, so the 2-column grid always fills the sheet correctly regardless
    ///     of Canvas resolution.
    ///   - ShapeNames array matched to Inspector button order.
    /// </summary>
    public class ObjectMenuUI : MonoBehaviour
    {
        [Header("Sheet")]
        [SerializeField] private RectTransform bottomSheet;
        [SerializeField] private CanvasGroup   sheetGroup;
        [SerializeField] private Image         backdrop;
        [SerializeField] private CanvasGroup   backdropGroup;

        [Header("Grid")]
        [SerializeField] private GridLayoutGroup shapeGrid;
        [SerializeField] private Button[] shapeButtons;
        [SerializeField] private string[] shapeNames = { "Cube", "Sphere", "Cylinder", "Capsule", "Plane" };

        [Header("Navigation")]
        [SerializeField] private Button            backButton;
        [SerializeField] private ObjectCustomizerUI customizerUI;

        [Header("Animation")]
        [SerializeField] private float animDuration = 0.25f;

        private bool  _isOpen = false;
        private float _sheetHeight;

        void Awake()
        {
            _sheetHeight = bottomSheet.sizeDelta.y;

            for (int i = 0; i < shapeButtons.Length; i++)
            {
                int idx = i;
                shapeButtons[i].onClick.AddListener(() => OnShapeSelected(idx));
            }

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

        private void OnShapeSelected(int index)
        {
            if (index >= shapeNames.Length) return;
            string name = shapeNames[index];
            CloseSheet();
            customizerUI?.OpenSheet(name);
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