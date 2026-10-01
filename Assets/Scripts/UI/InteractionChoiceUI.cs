using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// Floating "✎ Edit | ↻ Rotate" choice pill shown above a selected item
    /// when the user double-taps it.
    ///
    /// SCENE SETUP (create these GameObjects under your Canvas):
    ///
    ///   InteractionChoicePanel   — RectTransform 240x56, add CanvasGroup
    ///   └── PillBg               — Image, RoundedRect sprite, color #0D0D14CC
    ///       ├── EditButton       — Button, TMP child "✎  Edit", 14pt white
    ///       ├── Divider          — Image, width=1 height=32, color #FFFFFF22
    ///       └── RotateButton     — Button, TMP child "↻  Rotate", 14pt white
    ///
    /// Assign all fields in the Inspector then attach this script to
    /// InteractionChoicePanel.
    /// </summary>
    public class InteractionChoiceUI : MonoBehaviour
    {
        public static InteractionChoiceUI Instance { get; private set; }

        [Header("References")]
        [SerializeField] private RectTransform choiceRect;
        [SerializeField] private CanvasGroup   canvasGroup;
        [SerializeField] private Button        editButton;
        [SerializeField] private Button        rotateButton;

        [Header("Positioning")]
        [SerializeField] private float verticalOffset = 120f; // px above the selected object

        [Header("Animation")]
        [SerializeField] private float fadeDuration = 0.15f;

        // ── Cached ──
        private Camera        _arCamera;
        private RectTransform _canvasRT;
        private bool          _isVisible;

        // ─────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            editButton.onClick.AddListener(OnEditPressed);
            rotateButton.onClick.AddListener(OnRotatePressed);

            // Start hidden
            canvasGroup.alpha          = 0f;
            canvasGroup.interactable   = false;
            canvasGroup.blocksRaycasts = false;
            _isVisible = false;
        }

        void Start()
        {
            _arCamera = Camera.main;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) _canvasRT = canvas.GetComponent<RectTransform>();
        }

        void OnEnable()
        {
            if (SelectionManager.Instance != null)
                SelectionManager.Instance.OnItemDeselected += OnItemDeselected;
        }

        void OnDisable()
        {
            if (SelectionManager.Instance != null)
                SelectionManager.Instance.OnItemDeselected -= OnItemDeselected;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!_isVisible)                   return;
            if (SelectionManager.Instance == null) return;
            if (!SelectionManager.Instance.HasSelection) return;

            var selectedObj = SelectionManager.Instance.SelectedObject;
            if (selectedObj == null) { Hide(); return; }

            // Follow the selected object in screen space
            Vector3 worldPos  = selectedObj.transform.position + Vector3.up * 0.25f;
            Vector3 screenPos = _arCamera.WorldToScreenPoint(worldPos);

            // Object went behind camera — hide temporarily
            if (screenPos.z < 0) { canvasGroup.alpha = 0f; return; }
            else                 { canvasGroup.alpha = 1f; }

            if (_canvasRT == null) return;

            Camera eventCamera = _canvasRT.GetComponentInParent<Canvas>().worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRT, screenPos, eventCamera, out Vector2 localPos))
            {
                choiceRect.anchoredPosition = new Vector2(localPos.x, localPos.y + verticalOffset);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────

        /// <summary>Show the choice pill above the currently selected object.</summary>
        public void Show()
        {
            _isVisible = true;
            StopAllCoroutines();
            StartCoroutine(Fade(1f));
            Debug.Log("[AuraSync] InteractionChoiceUI: Shown");
        }

        /// <summary>Hide the choice pill.</summary>
        public void Hide()
        {
            _isVisible = false;
            StopAllCoroutines();
            StartCoroutine(Fade(0f));
        }

        // ─────────────────────────────────────────────────────────────
        //  Button handlers
        // ─────────────────────────────────────────────────────────────

        private void OnEditPressed()
        {
            Debug.Log("[AuraSync] InteractionChoice: Edit");
            Hide();
            SelectionManager.Instance?.EditSelectedItem();
        }

        private void OnRotatePressed()
        {
            Debug.Log("[AuraSync] InteractionChoice: Rotate mode activated");
            Hide();
            SelectionManager.Instance?.BeginRotateMode();
            // Rotate mode persists until the item is deselected or
            // the user double-taps again and picks Edit.
        }

        // ─────────────────────────────────────────────────────────────
        //  Selection events
        // ─────────────────────────────────────────────────────────────

        private void OnItemDeselected()
        {
            Hide();
        }

        // ─────────────────────────────────────────────────────────────
        //  Animation
        // ─────────────────────────────────────────────────────────────

        private IEnumerator Fade(float target)
        {
            float start   = canvasGroup.alpha;
            float elapsed = 0f;

            canvasGroup.interactable   = target > 0f;
            canvasGroup.blocksRaycasts = target > 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, elapsed / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = target;
        }
    }
}