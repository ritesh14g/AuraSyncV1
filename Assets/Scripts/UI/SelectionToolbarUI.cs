using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Core;
using AuraSync.Data;

namespace AuraSync.UI
{
    public class SelectionToolbarUI : MonoBehaviour
    {
        [Header("Toolbar")]
        [SerializeField] private RectTransform toolbarRect;
        [SerializeField] private CanvasGroup   canvasGroup;

        [Header("Buttons")]
        [SerializeField] private Button resizeButton;
        [SerializeField] private Button editButton;
        [SerializeField] private Button deleteButton;

        [Header("Dividers")]
        [SerializeField] private GameObject editDivider;

        [Header("Managers")]
        [SerializeField] private SelectionManager selectionManager;

        [Header("Offset")]
        [SerializeField] private float verticalOffset = 80f; // px above object

        // ── Cached references ──
        private Camera        _arCamera;
        private RectTransform _canvasRT;
        private CanvasGroup   _editButtonCg;
        private CanvasGroup   _editDividerCg;

        // ── State ──
        private bool           _isVisible    = false;
        private GameObject     _trackedObject = null;
        private PlacedItemData _trackedData   = null;

        // ─────────────────────────────────────────────────────────────
        void Awake()
        {
            if (selectionManager == null)
                selectionManager = FindObjectOfType<SelectionManager>();

            resizeButton.onClick.AddListener(OnResize);
            editButton.onClick.AddListener(OnEdit);
            deleteButton.onClick.AddListener(OnDelete);

            // Cache CanvasGroups so we never use SetActive on buttons
            _editButtonCg = editButton.GetComponent<CanvasGroup>()
                         ?? editButton.gameObject.AddComponent<CanvasGroup>();

            if (editDivider != null)
                _editDividerCg = editDivider.GetComponent<CanvasGroup>()
                              ?? editDivider.AddComponent<CanvasGroup>();

            SetVisible(false);
        }

        void OnEnable()
        {
            if (selectionManager == null) return;
            selectionManager.OnItemSelected   += OnItemSelected;
            selectionManager.OnItemDeselected += OnItemDeselected;
        }

        void OnDisable()
        {
            if (selectionManager == null) return;
            selectionManager.OnItemSelected   -= OnItemSelected;
            selectionManager.OnItemDeselected -= OnItemDeselected;
        }

        void Start()
        {
            _arCamera = Camera.main;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                _canvasRT = canvas.GetComponent<RectTransform>();
        }

        void Update()
        {
            if (!_isVisible || _trackedObject == null || _canvasRT == null) return;

            Vector3 worldPos  = _trackedObject.transform.position + Vector3.up * 0.25f;
            Vector3 screenPos = _arCamera.WorldToScreenPoint(worldPos);

            // Object behind camera — hide toolbar
            if (screenPos.z < 0)
            {
                canvasGroup.alpha = 0f;
                return;
            }
            else
            {
                canvasGroup.alpha = 1f;
            }

            // Use the canvas's event camera (null-safe)
            Camera eventCamera = _canvasRT.GetComponentInParent<Canvas>().worldCamera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRT, screenPos, eventCamera, out Vector2 localPos))
            {
                toolbarRect.anchoredPosition = new Vector2(localPos.x, 
                                                           localPos.y + verticalOffset);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Selection events
        // ─────────────────────────────────────────────────────────────

        private void OnItemSelected(GameObject obj, PlacedItemData data)
        {
            _trackedObject = obj;
            _trackedData   = data;

            // Toggle Edit button visibility using CanvasGroup — NOT SetActive
            bool isNote = data is NoteItemData;
            SetEditVisible(isNote);

            SetVisible(true);
        }

        private void OnItemDeselected()
        {
            _trackedObject = null;
            _trackedData   = null;
            SetVisible(false);
        }

        // ─────────────────────────────────────────────────────────────
        //  Button handlers
        // ─────────────────────────────────────────────────────────────

        private void OnResize()
        {
            // Resize mode is already active via drag gesture — this button
            // is a visual cue. Show a brief hint then user drags to resize.
            Debug.Log("[AuraSync] SelectionToolbar: Resize — drag the object to scale");
        }

        private void OnEdit()
        {
            selectionManager?.EditSelectedItem();
        }

        private void OnDelete()
        {
            if (_trackedData == null) return;

            if (_trackedObject != null) Destroy(_trackedObject);

            if (_trackedData is NoteItemData noteData)
                PersistenceManager.Instance?.RemoveNote(noteData.id);
            else if (_trackedData is ObjectItemData objData)
                PersistenceManager.Instance?.RemoveObject(objData.id);

            selectionManager?.Deselect();
            Debug.Log($"[AuraSync] SelectionToolbar: Deleted {_trackedData.id}");
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (canvasGroup == null) return;
            canvasGroup.alpha          = visible ? 1f : 0f;
            canvasGroup.interactable   = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        private void SetEditVisible(bool visible)
        {
            if (_editButtonCg != null)
            {
                _editButtonCg.alpha          = visible ? 1f : 0f;
                _editButtonCg.interactable   = visible;
                _editButtonCg.blocksRaycasts = visible;
            }
            if (_editDividerCg != null)
            {
                _editDividerCg.alpha          = visible ? 1f : 0f;
                _editDividerCg.blocksRaycasts = visible;
            }
        }
    }
}