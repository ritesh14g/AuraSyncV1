using System;
using UnityEngine;
using AuraSync.Data;

namespace AuraSync.Core
{
    public class SelectionManager : MonoBehaviour
    {
        public static SelectionManager Instance { get; private set; }

        // ── Events ──
        public event Action<GameObject, PlacedItemData> OnItemSelected;
        public event Action OnItemDeselected;

        // ── Selection State ──
        public GameObject  SelectedObject { get; private set; }
        public PlacedItemData SelectedData  { get; private set; }
        public bool HasSelection => SelectedObject != null;

        // ── Interaction Mode ──
        // Resize is default. After user taps "Rotate" in the choice overlay,
        // drag rotates the object instead of scaling it. Resets on deselect.
        public enum InteractionMode { Resize, Rotate }
        private InteractionMode _interactionMode = InteractionMode.Resize;

        [Header("Resize Settings")]
        [SerializeField] private float resizeSpeed = 0.005f;
        [SerializeField] private float minScale    = 0.03f;
        [SerializeField] private float maxScale    = 2f;

        [Header("Rotate Settings")]
        [SerializeField] private float rotateSpeed = 0.4f; // degrees per pixel of drag

        private GestureDetector _gestureDetector;
        private Renderer        _selectedRenderer;
        private bool            _isTransforming; // used for both resize and rotate

        // ─────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            _gestureDetector = FindObjectOfType<GestureDetector>();
            if (_gestureDetector == null)
            {
                Debug.LogError("[AuraSync] SelectionManager: GestureDetector not found!");
                return;
            }
            _gestureDetector.OnDrag    += HandleDrag;
            _gestureDetector.OnDragEnd += HandleDragEnd;
            Debug.Log("[AuraSync] SelectionManager: Subscribed to GestureDetector");
        }

        void OnDisable()
        {
            if (_gestureDetector != null)
            {
                _gestureDetector.OnDrag    -= HandleDrag;
                _gestureDetector.OnDragEnd -= HandleDragEnd;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ──────────────────────────────────────────
        //  Selection
        // ──────────────────────────────────────────

        public void Select(GameObject obj, PlacedItemData data)
        {
            if (HasSelection && SelectedObject != obj) Deselect();

            SelectedObject = obj;
            SelectedData   = data;
            ApplyHighlight(obj);
            OnItemSelected?.Invoke(obj, data);
            Debug.Log($"[AuraSync] Selected: {data.id} ({data.itemType})");
        }

        public void Deselect()
        {
            if (!HasSelection) return;
            RemoveHighlight();
            SelectedObject   = null;
            SelectedData     = null;
            _interactionMode = InteractionMode.Resize; // always reset on deselect
            OnItemDeselected?.Invoke();
            Debug.Log("[AuraSync] Deselected");
        }

        // ──────────────────────────────────────────
        //  Interaction Mode
        // ──────────────────────────────────────────

        /// <summary>Switch to resize mode. Drag up/down will scale the item.</summary>
        public void BeginResizeMode()
        {
            _interactionMode = InteractionMode.Resize;
            Debug.Log("[AuraSync] SelectionManager: Resize mode");
        }

        /// <summary>Switch to rotate mode. Drag left/right will rotate the item around Y axis.</summary>
        public void BeginRotateMode()
        {
            _interactionMode = InteractionMode.Rotate;
            Debug.Log("[AuraSync] SelectionManager: Rotate mode");
        }

        // ──────────────────────────────────────────
        //  Drag: Resize or Rotate
        // ──────────────────────────────────────────

        private void HandleDrag(Vector2 position, Vector2 delta)
        {
            if (!HasSelection) return;

            if (_interactionMode == InteractionMode.Resize)
            {
                // Vertical drag scales the object
                float    scaleChange = delta.y * resizeSpeed;
                Vector3  newScale    = SelectedObject.transform.localScale + Vector3.one * scaleChange;
                newScale.x = Mathf.Clamp(newScale.x, minScale, maxScale);
                newScale.y = Mathf.Clamp(newScale.y, minScale, maxScale);
                newScale.z = Mathf.Clamp(newScale.z, minScale, maxScale);
                SelectedObject.transform.localScale = newScale;
            }
            else // Rotate
            {
                // Horizontal drag rotates around world Y axis
                float yRotation = delta.x * rotateSpeed;
                SelectedObject.transform.Rotate(Vector3.up, yRotation, Space.World);
            }

            _isTransforming = true;
        }

        private void HandleDragEnd()
        {
            if (!_isTransforming || !HasSelection) return;

            // Save both scale and rotation — whichever changed
            SelectedData.SetScale(SelectedObject.transform.localScale);
            SelectedData.SetRotation(SelectedObject.transform.rotation);

            if (SelectedData is NoteItemData noteData)
                PersistenceManager.Instance?.UpdateNote(noteData);
            else if (SelectedData is ObjectItemData objData)
                PersistenceManager.Instance?.UpdateObject(objData);

            _isTransforming = false;
            Debug.Log($"[AuraSync] Transform saved: {SelectedData.id} " +
                      $"scale={SelectedObject.transform.localScale} " +
                      $"rot={SelectedObject.transform.rotation.eulerAngles}");
        }

        // ──────────────────────────────────────────
        //  Visual Feedback
        // ──────────────────────────────────────────

        private void ApplyHighlight(GameObject obj)
        {
            _selectedRenderer = obj.GetComponentInChildren<Renderer>();
        }

        private void RemoveHighlight()
        {
            _selectedRenderer = null;
        }

        // ──────────────────────────────────────────
        //  Re-Edit (called by InteractionChoiceUI)
        // ──────────────────────────────────────────

        public void EditSelectedItem()
        {
            if (!HasSelection) return;

            if (SelectedData is NoteItemData noteData)
            {
                var noteEditor = FindObjectOfType<UI.NoteEditorUI>();
                noteEditor?.OpenForEdit(noteData);
            }
            else if (SelectedData is ObjectItemData objData)
            {
                var objCustomizer = FindObjectOfType<UI.ObjectCustomizerUI>();
                objCustomizer?.OpenForEdit(objData);
            }
        }
    }
}