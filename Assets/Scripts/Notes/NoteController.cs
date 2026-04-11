using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Data;

namespace AuraSync.Notes
{
    /// <summary>
    /// Component attached to every placed note in the scene.
    /// Manages the note's visual appearance and holds its data.
    /// 
    /// PREFAB SETUP:
    /// - Root: Empty GameObject with this script + Collider (BoxCollider)
    /// - Child "Background": Quad with MeshRenderer for the note background
    /// - Child "TextContent": TMP_Text for displaying text
    /// - Child "Border": Quad with MeshRenderer for the border
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class NoteController : MonoBehaviour
    {
        [Header("Visual References")]
        [SerializeField] private Renderer background;
        [SerializeField] private Renderer border;
        [SerializeField] private TextMeshPro    textDisplay;

        [Header("Checklist References (only for checklist notes)")]
        [SerializeField] private Transform checklistContainer;
        [SerializeField] private GameObject checklistItemPrefab;

        // ── Data ──
        private NoteItemData _data;
        public NoteItemData Data => _data;

        // ── FaceCamera ──
        private FaceCamera _faceCamera;

        void Awake()
        {
            _faceCamera = GetComponent<FaceCamera>();
        }

        // ──────────────────────────────────────────
        //  Initialization
        // ──────────────────────────────────────────

        /// <summary>Initialize this note with data (called on placement or restoration).</summary>
        public void Initialize(NoteItemData data)
        {
            _data = data;
            ApplyVisuals();
        }

        // ──────────────────────────────────────────
        //  Visual Application
        // ──────────────────────────────────────────

        /// <summary>Apply all visual properties from data.</summary>
        public void ApplyVisuals()
        {
            if (_data == null) return;

            // Background color
            if (background != null && ColorUtility.TryParseHtmlString(_data.colorHex, out Color bgColor))
            {
                SetRendererColor(background, bgColor);
            }

            // Border color
            if (border != null && ColorUtility.TryParseHtmlString(_data.borderColorHex, out Color brdColor))
            {
                SetRendererColor(border, brdColor);
            }

            // Content
            if (_data.noteType == NoteType.PlainText)
            {
                ShowPlainText();
            }
            else
            {
                ShowChecklist();
            }
        }

        private void ShowPlainText()
        {
            if (textDisplay != null)
            {
                textDisplay.text = _data.text;
                textDisplay.gameObject.SetActive(true);
            }

            if (checklistContainer != null)
            {
                checklistContainer.gameObject.SetActive(false);
            }
        }

        private void ShowChecklist()
        {
            if (textDisplay != null)
            {
                textDisplay.gameObject.SetActive(false);
            }

            if (checklistContainer != null)
            {
                checklistContainer.gameObject.SetActive(true);
                PopulateChecklist();
            }
        }

        private void PopulateChecklist()
        {
            if (checklistContainer == null || checklistItemPrefab == null) return;

            // Clear existing
            foreach (Transform child in checklistContainer)
            {
                Destroy(child.gameObject);
            }

            // Build checklist items
            foreach (var item in _data.checklistItems)
            {
                GameObject row = Instantiate(checklistItemPrefab, checklistContainer);
                var checkCtrl = row.GetComponent<ChecklistItemUI>();
                if (checkCtrl != null)
                {
                    checkCtrl.Setup(item, OnChecklistItemChanged);
                }
            }
        }

        private void OnChecklistItemChanged()
        {
            // Save updated checklist state
            Core.PersistenceManager.Instance?.UpdateNote(_data);
        }

        /// <summary>Sets color on any Renderer type (MeshRenderer or SpriteRenderer).</summary>
        private void SetRendererColor(Renderer rend, Color color)
        {
            if (rend is SpriteRenderer sr)
            {
                sr.color = color;
            }
            else
            {
                // MeshRenderer (Quad) — use material color
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                rend.GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color); // URP
                rend.SetPropertyBlock(block);
            }
        }

        // ──────────────────────────────────────────
        //  Update Data (called by NoteEditorUI after re-edit)
        // ──────────────────────────────────────────

        /// <summary>Update this note's data and refresh visuals.</summary>
        public void UpdateData(NoteItemData newData)
        {
            _data = newData;
            ApplyVisuals();

            // Update saved position/rotation/scale from current transform
            _data.SetPosition(transform.position);
            _data.SetRotation(transform.rotation);
            _data.SetScale(transform.localScale);

            Core.PersistenceManager.Instance?.UpdateNote(_data);
        }
    }
}
