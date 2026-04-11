using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Core;
using AuraSync.Data;

namespace AuraSync.UI
{
    /// <summary>
    /// Colour / opacity customiser sheet for 3D objects.
    ///
    /// FIX v2.0:
    ///   - Color swatches built entirely from code — colorSwatchPrefab Inspector
    ///     slot no longer needed. If assigned it's used as an override, otherwise
    ///     plain square Image+Button GameObjects are created at runtime.
    ///   - Added OpenForEdit(ObjectItemData) so SelectionManager.EditSelectedItem()
    ///     can reopen this panel to modify a placed object's colour/opacity.
    ///   - In edit mode the "Place ->" button becomes "Apply" and updates the
    ///     existing ObjectController's material rather than spawning a new object.
    /// </summary>
    public class ObjectCustomizerUI : MonoBehaviour
    {
        [Header("Sheet")]
        [SerializeField] private RectTransform   bottomSheet;
        [SerializeField] private CanvasGroup     sheetGroup;
        [SerializeField] private Image           backdrop;
        [SerializeField] private CanvasGroup     backdropGroup;
        [SerializeField] private TextMeshProUGUI shapeTitle;

        [Header("Color")]
        [SerializeField] private Transform  colorRow;
        [SerializeField] private GameObject colorSwatchPrefab;  // optional override

        [Header("Opacity")]
        [SerializeField] private Slider          opacitySlider;
        [SerializeField] private TextMeshProUGUI opacityLabel;

        [Header("Footer")]
        [SerializeField] private Button      backButton;
        [SerializeField] private Button      placeButton;
        [SerializeField] private TextMeshProUGUI placeButtonLabel; // optional label swap
        [SerializeField] private ObjectMenuUI objectMenu;

        [Header("Animation")]
        [SerializeField] private float animDuration = 0.25f;

        // ── Swatch constants ──
        private const float SwatchSize    = 44f;
        private const float SwatchSpacing = 8f;

        // ── State ──
        private bool   _isOpen       = false;
        private bool   _isEditMode   = false;
        private float  _sheetHeight;
        private string _pendingShape = "Cube";
        private Color  _selectedColor;
        private float  _opacity = 1f;
        private string _configId;

        // Edit mode references
        private ObjectItemData _editingData;
        private GameObject     _editingObject;

        private AppStateManager       _appState;
        private ARPlacementController _placement;

        private readonly List<(string label, Color color)> _palette = new()
        {
            ("Yellow", new Color(1.00f, 0.85f, 0.20f)),
            ("Orange", new Color(1.00f, 0.55f, 0.20f)),
            ("Red",    new Color(0.95f, 0.27f, 0.27f)),
            ("Purple", new Color(0.65f, 0.35f, 0.95f)),
            ("Blue",   new Color(0.35f, 0.60f, 1.00f)),
            ("Teal",   new Color(0.20f, 0.80f, 0.75f)),
            ("Green",  new Color(0.30f, 0.85f, 0.45f)),
            ("White",  new Color(0.95f, 0.95f, 0.95f)),
            ("Dark",   new Color(0.15f, 0.15f, 0.20f)),
        };

        // ─────────────────────────────────────────────────────────────
        void Awake()
        {
            _appState     = FindObjectOfType<AppStateManager>();
            _placement    = FindObjectOfType<ARPlacementController>();
            _sheetHeight  = bottomSheet.sizeDelta.y;
            _selectedColor = _palette[4].color;

            backButton.onClick.AddListener(OnBack);
            placeButton.onClick.AddListener(OnPlace);

            if (opacitySlider) opacitySlider.onValueChanged.AddListener(OnOpacityChanged);

            backdrop.raycastTarget = true;
            var bd = backdrop.gameObject.GetComponent<Button>()
                  ?? backdrop.gameObject.AddComponent<Button>();
            bd.transition = Selectable.Transition.None;
            bd.onClick.AddListener(CloseSheet);

            EnsureLayoutGroup(colorRow);
            BuildColorRow();
            HideImmediate();
        }

        // ─────────────────────────────────────────────────────────────
        //  Sheet lifecycle
        // ─────────────────────────────────────────────────────────────

        private void HideImmediate()
        {
            bottomSheet.anchoredPosition = new Vector2(0, -_sheetHeight);
            sheetGroup.alpha    = 0f;    sheetGroup.interactable    = false;    sheetGroup.blocksRaycasts    = false;
            backdropGroup.alpha = 0f;    backdropGroup.interactable = false;    backdropGroup.blocksRaycasts = false;
        }

        /// <summary>Open for placing a new object of the given shape.</summary>
        public void OpenSheet(string shapeName)
        {
            if (_isOpen) return;
            _isEditMode    = false;
            _editingData   = null;
            _editingObject = null;
            _pendingShape  = shapeName;
            _configId      = Guid.NewGuid().ToString();
            _opacity       = 1f;
            _selectedColor = _palette[4].color;

            if (shapeTitle)   shapeTitle.text    = shapeName;
            if (opacitySlider) opacitySlider.value = 1f;
            if (opacityLabel)  opacityLabel.text  = "Opacity: 100%";
            if (placeButtonLabel) placeButtonLabel.text = "Place →";

            _isOpen = true;
            StopAllCoroutines();
            StartCoroutine(AnimateSheet(true));
        }

        /// <summary>
        /// Open to edit colour/opacity of an already-placed object.
        /// Called by SelectionManager.EditSelectedItem().
        /// </summary>
        public void OpenForEdit(ObjectItemData data)
        {
            if (_isOpen) return;
            _isEditMode    = true;
            _editingData   = data;
            _editingObject = null; // resolved in OnPlace via SelectionManager

            // Try to find the live object in the scene
            var selectionManager = FindObjectOfType<SelectionManager>();
            if (selectionManager != null && selectionManager.HasSelection)
                _editingObject = selectionManager.SelectedObject;

            _pendingShape = data.prefabName ?? "Object";
            _configId     = data.id;
            _opacity      = 1f;
            _selectedColor = _palette[4].color;

            // Parse existing colour if available
            if (!string.IsNullOrEmpty(data.colorHex))
            {
                Color parsed;
                if (ColorUtility.TryParseHtmlString("#" + data.colorHex, out parsed))
                {
                    _selectedColor = new Color(parsed.r, parsed.g, parsed.b, 1f);
                    _opacity       = parsed.a;
                }
            }

            if (shapeTitle)    shapeTitle.text      = $"Edit: {_pendingShape}";
            if (opacitySlider) opacitySlider.value  = _opacity;
            if (opacityLabel)  opacityLabel.text    = $"Opacity: {Mathf.RoundToInt(_opacity * 100f)}%";
            if (placeButtonLabel) placeButtonLabel.text = "Apply";

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
        //  Footer handlers
        // ─────────────────────────────────────────────────────────────

        private void OnBack()
        {
            CloseSheet();
            if (!_isEditMode)
                objectMenu?.OpenSheet();
        }

        private void OnPlace()
        {
            var finalColor = new Color(_selectedColor.r, _selectedColor.g, _selectedColor.b, _opacity);

            if (_isEditMode)
            {
                // Apply colour change to the live object immediately
                if (_editingObject != null)
                {
                    var ctrl = _editingObject.GetComponent<AuraSync.Objects.ObjectController>();
                    if (ctrl != null) ctrl.ApplyColor(finalColor);
                    else
                    {
                        // Fallback: set renderer directly
                        var renderer = _editingObject.GetComponentInChildren<Renderer>();
                        if (renderer != null) renderer.material.color = finalColor;
                    }
                }

                // Persist
                if (_editingData != null)
                {
                    _editingData.colorHex = "#" + ColorUtility.ToHtmlStringRGBA(finalColor);
                    PersistenceManager.Instance?.UpdateObject(_editingData);
                }

                Debug.Log($"[AuraSync] ObjectCustomizerUI: Applied colour to existing object {_configId}");
                CloseSheet();
                return;
            }

            // ── New placement flow ──
            if (_placement == null) { CloseSheet(); return; }

            var config = new ObjectItemData
            {
                id         = _configId,
                prefabName = _pendingShape,
                colorHex   = "#" + ColorUtility.ToHtmlStringRGBA(finalColor)
            };
            config.SetScale(Vector3.one);

            Debug.Log($"[AuraSync] ObjectCustomizerUI: Place pressed — entering PlacingObject mode. " +
                      $"Config id={config.id}, prefab={_pendingShape}");

            _placement.SetPendingObject(config);
            _appState?.SetMode(AppMode.PlacingObject);
            CloseSheet();
        }

        private void OnOpacityChanged(float val)
        {
            _opacity = val;
            if (opacityLabel) opacityLabel.text = $"Opacity: {Mathf.RoundToInt(val * 100f)}%";
        }

        // ─────────────────────────────────────────────────────────────
        //  Color swatches — built from code, no prefab required
        // ─────────────────────────────────────────────────────────────

        private void EnsureLayoutGroup(Transform row)
        {
            if (row == null) return;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
            {
                hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing               = SwatchSpacing;
                hlg.childAlignment        = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = false;
                hlg.childControlWidth      = false;
                hlg.childControlHeight     = false;
            }
        }

        private void BuildColorRow()
        {
            if (colorRow == null) return;
            foreach (Transform child in colorRow) Destroy(child.gameObject);

            var refs = new GameObject[_palette.Count];

            for (int i = 0; i < _palette.Count; i++)
            {
                var c   = _palette[i].color;
                int idx = i;

                GameObject go = colorSwatchPrefab != null
                    ? Instantiate(colorSwatchPrefab, colorRow)
                    : BuildSwatchFromCode(colorRow, c);

                if (colorSwatchPrefab != null)
                    go.GetComponent<Image>().color = c;

                refs[i] = go;
                var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    _selectedColor = c;
                    HighlightSwatch(go);
                });
            }

            // Default selection: Blue (index 4)
            if (refs.Length > 4) HighlightSwatch(refs[4]);
        }

        private GameObject BuildSwatchFromCode(Transform parent, Color color)
        {
            var go = new GameObject("Swatch");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(SwatchSize, SwatchSize);

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.highlightedColor = Color.white;
            cb.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = cb;

            return go;
        }

        private void HighlightSwatch(GameObject selected)
        {
            if (colorRow == null) return;
            foreach (Transform child in colorRow)
            {
                var outline = child.GetComponent<Outline>()
                           ?? child.gameObject.AddComponent<Outline>();
                bool isSelected = child.gameObject == selected;
                outline.effectColor    = isSelected ? Color.white : Color.clear;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.enabled        = isSelected;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sheet animation
        // ─────────────────────────────────────────────────────────────

        private IEnumerator AnimateSheet(bool open)
        {
            if (open)
            {
                backdropGroup.alpha          = 1f;
                backdropGroup.blocksRaycasts = true;
                backdropGroup.interactable   = false;
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
            }
        }
    }
}