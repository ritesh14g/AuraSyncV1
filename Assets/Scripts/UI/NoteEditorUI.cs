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
    /// Bottom-sheet note editor: type, text, checklist, background and border colour.
    ///
    /// FIX v1.2:
    ///   - Color swatches built 100% from code — no prefab reference required.
    ///     Swatches are plain square Image+Button GameObjects created at runtime.
    ///   - Added OpenForEdit(NoteItemData) for editing already-placed notes.
    ///   - HorizontalLayoutGroup added to color rows at runtime so swatches
    ///     lay out automatically regardless of row width.
    /// </summary>
    public class NoteEditorUI : MonoBehaviour
    {
        [Header("Sheet")]
        [SerializeField] private RectTransform   bottomSheet;
        [SerializeField] private CanvasGroup     sheetGroup;
        [SerializeField] private Image           backdrop;
        [SerializeField] private CanvasGroup     backdropGroup;
        [SerializeField] private TextMeshProUGUI sheetTitle;

        [Header("Style Toggle")]
        [SerializeField] private Button stickyToggle;
        [SerializeField] private Button paperToggle;
        [SerializeField] private Image  stickyToggleBg;
        [SerializeField] private Image  paperToggleBg;

        [Header("Input")]
        [SerializeField] private TMP_InputField noteInput;
        [SerializeField] private Toggle         privateToggle;

        [Header("Checklist")]
        [SerializeField] private RectTransform checklistContainer;
        [SerializeField] private Transform     checklistItemParent;
        [SerializeField] private GameObject    checklistItemPrefab;   // optional
        [SerializeField] private Button        addChecklistItemBtn;

        [Header("Swatch Shape")]
        [SerializeField] private Sprite swatchCircleSprite;

        [Header("Color Rows")]
        [SerializeField] private Transform bgColorRow;
        [SerializeField] private Transform bdColorRow;
        // colorSwatchPrefab is NO LONGER REQUIRED — swatches built from code
        [SerializeField] private GameObject colorSwatchPrefab;   // optional override

        [Header("Footer")]
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button doneButton;

        [Header("Animation")]
        [SerializeField] private float animDuration = 0.28f;

        // ── State ──
        private bool   _isOpen        = false;
        private bool   _isEditMode     = false;
        private bool   _isChecklist   = false;
        private bool   _isStickyStyle = true;
        private Color  _bgColor;
        private Color  _bdColor;
        private float  _sheetHeight;
        private string _currentConfigId;
        private NoteItemData _editingNote; // set when editing existing note

        private AppStateManager       _appState;
        private ARPlacementController _placement;

        // ── Pill colours ──
        private static readonly Color ActivePill   = new Color(0.384f, 0.561f, 1.00f);
        private static readonly Color InactivePill = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ActiveText   = Color.white;
        private static readonly Color InactiveText = new Color(1f, 1f, 1f, 0.55f);

        // ── Swatch size ──
        private const float SwatchSize    = 44f;
        private const float SwatchSpacing = 8f;

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
            _appState  = FindObjectOfType<AppStateManager>();
            _placement = FindObjectOfType<ARPlacementController>();
            _sheetHeight  = bottomSheet.sizeDelta.y;
            _bgColor      = _palette[0].color;
            _bdColor      = _palette[8].color;

            stickyToggle.onClick.AddListener(() => SetStyle(sticky: true));
            paperToggle.onClick.AddListener(() => SetStyle(sticky: false));

            if (addChecklistItemBtn)
                addChecklistItemBtn.onClick.AddListener(AddChecklistItem);

            cancelButton.onClick.AddListener(OnCancel);
            doneButton.onClick.AddListener(OnDone);

            backdrop.raycastTarget = true;
            var bd = backdrop.gameObject.GetComponent<Button>()
                  ?? backdrop.gameObject.AddComponent<Button>();
            bd.transition = Selectable.Transition.None;
            bd.onClick.AddListener(OnCancel);

            EnsureLayoutGroup(bgColorRow);
            EnsureLayoutGroup(bdColorRow);
            BuildColorRow(bgColorRow, c => _bgColor = c, true);
            BuildColorRow(bdColorRow, c => _bdColor = c, false);
            RefreshToggles();

            if (privateToggle != null)
            {
                privateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);
            }

            HideImmediate();
        }

        // ─────────────────────────────────────────────────────────────
        //  Sheet lifecycle
        // ─────────────────────────────────────────────────────────────

        private void HideImmediate()
        {
            bottomSheet.anchoredPosition = new Vector2(0, -_sheetHeight);
            sheetGroup.alpha    = 0f;  sheetGroup.interactable    = false;  sheetGroup.blocksRaycasts    = false;
            backdropGroup.alpha = 0f;  backdropGroup.interactable = false;  backdropGroup.blocksRaycasts = false;
            if (backdrop != null) backdrop.raycastTarget = false;
        }

        /// <summary>Open for creating a new note.</summary>
        public void OpenSheet(bool isChecklist)
        {
            if (_isOpen) return;
            _isEditMode      = false;
            _editingNote     = null;
            _isChecklist     = isChecklist;
            _currentConfigId = Guid.NewGuid().ToString();
            _bgColor         = _palette[0].color;
            _bdColor         = _palette[8].color;

            if (sheetTitle) sheetTitle.text = isChecklist ? "New Checklist" : "New Note";
            if (noteInput)  noteInput.text  = "";
            if (privateToggle) privateToggle.isOn = false;

            ClearChecklist();
            if (isChecklist) AddChecklistItem();
            SetStyle(sticky: true);
            RefreshChecklistVisibility();

            _isOpen = true;
            StopAllCoroutines();
            StartCoroutine(AnimateSheet(true));
        }

        /// <summary>Open to edit an already-placed note.</summary>
        public void OpenForEdit(NoteItemData data)
        {
            if (_isOpen) return;
            _isEditMode      = true;
            _editingNote     = data;
            _isChecklist     = data.noteType == NoteType.Checklist;
            _currentConfigId = data.id;

            if (sheetTitle) sheetTitle.text = "Edit Note";
            if (noteInput)  noteInput.text  = data.text ?? "";
            if (privateToggle) privateToggle.isOn = data.isPrivate;

            bool sticky = data.noteStyle == NoteStyle.StickyNote;
            SetStyle(sticky);
            RefreshChecklistVisibility();

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

        private IEnumerator AnimateSheet(bool open)
        {
            if (open)
            {
                backdropGroup.alpha          = 1f;
                backdropGroup.blocksRaycasts = true;
                backdropGroup.interactable   = false;
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
                sheetGroup.alpha             = Mathf.Lerp(startAlpha, targetAlpha, t);
                backdropGroup.alpha          = Mathf.Lerp(startBD,    targetBD,    t);
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
        //  Footer handlers
        // ─────────────────────────────────────────────────────────────

        private void OnCancel()
        {
            CloseSheet();
            _appState?.ReturnToIdle();
        }

        private void OnDone()
        {
            if (_isEditMode)
            {
                // Update the existing note in-place
                if (_editingNote != null)
                {
                    _editingNote.text      = noteInput ? noteInput.text : "";
                    _editingNote.noteType  = _isChecklist ? NoteType.Checklist : NoteType.PlainText;
                    _editingNote.noteStyle = _isStickyStyle ? NoteStyle.StickyNote : NoteStyle.Paper;
                    _editingNote.colorHex       = "#" + ColorUtility.ToHtmlStringRGBA(_bgColor);
                    _editingNote.borderColorHex = "#" + ColorUtility.ToHtmlStringRGBA(_bdColor);
                    if (privateToggle) _editingNote.isPrivate = privateToggle.isOn;
                    PersistenceManager.Instance?.UpdateNote(_editingNote);
                    Debug.Log($"[AuraSync] NoteEditorUI: Edited note {_editingNote.id}");
                }
                CloseSheet();
                return;
            }

            // New note — enter placement mode
            var config = new NoteItemData
            {
                id          = _currentConfigId,
                noteType    = _isChecklist ? NoteType.Checklist : NoteType.PlainText,
                noteStyle   = _isStickyStyle ? NoteStyle.StickyNote : NoteStyle.Paper,
                text        = noteInput ? noteInput.text : "",
                colorHex    = "#" + ColorUtility.ToHtmlStringRGBA(_bgColor),
                borderColorHex = "#" + ColorUtility.ToHtmlStringRGBA(_bdColor),
                isPrivate   = privateToggle ? privateToggle.isOn : false,
            };

            // Copy checklist items if applicable
            if (_isChecklist && checklistItemParent != null)
            {
                config.checklistItems = new System.Collections.Generic.List<ChecklistItem>();
                foreach (Transform child in checklistItemParent)
                {
                    var inputField = child.GetComponentInChildren<TMP_InputField>();
                    if (inputField != null)
                        config.checklistItems.Add(new ChecklistItem { text = inputField.text });
                }
            }

            Debug.Log($"[AuraSync] NoteEditorUI: Entering PlacingNote mode. id={config.id}, type={config.noteType}");
            _placement?.SetPendingNote(config);
            _appState?.SetMode(AppMode.PlacingNote);
            CloseSheet();
        }

        // ─────────────────────────────────────────────────────────────
        //  Style toggle
        // ─────────────────────────────────────────────────────────────

        private void SetStyle(bool sticky)
        {
            _isStickyStyle = sticky;
            RefreshToggles();
        }

        private void RefreshToggles()
        {
            if (stickyToggleBg) stickyToggleBg.color = _isStickyStyle ? ActivePill : InactivePill;
            if (paperToggleBg)  paperToggleBg.color  = _isStickyStyle ? InactivePill : ActivePill;

            var st = stickyToggle.GetComponentInChildren<TextMeshProUGUI>();
            var pt = paperToggle.GetComponentInChildren<TextMeshProUGUI>();
            if (st) st.color = _isStickyStyle ? ActiveText : InactiveText;
            if (pt) pt.color = _isStickyStyle ? InactiveText : ActiveText;
        }

        private void OnPrivateToggleChanged(bool isOn)
        {
            if (privateToggle == null) return;
            var bg = privateToggle.targetGraphic as Image;
            if (bg != null)
            {
                bg.color = isOn ? ActivePill : Color.white;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Checklist
        // ─────────────────────────────────────────────────────────────

        private void AddChecklistItem()
        {
            if (checklistItemParent == null) return;

            GameObject item = new GameObject("ChecklistRow");
            item.transform.SetParent(checklistItemParent, false);

            var rt = item.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 52);

            var rowImg    = item.AddComponent<Image>();
            rowImg.color  = new Color(1f, 1f, 1f, 0.07f);

            var row = item.AddComponent<HorizontalLayoutGroup>();
            row.spacing               = 0;
            row.padding               = new RectOffset(0, 0, 0, 0);
            row.childForceExpandWidth  = false;
            row.childForceExpandHeight = true;
            row.childControlWidth      = true;
            row.childControlHeight     = true;

            // ── CHECKBOX (10% width, toggleable) ──
            var checkGo  = new GameObject("CheckBox");
            checkGo.transform.SetParent(item.transform, false);
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = new Color(0.25f, 0.25f, 0.4f, 1f);
            var checkBtn = checkGo.AddComponent<Button>();
            checkBtn.targetGraphic = checkImg;

            // Checkbox symbol text
            var checkTxtGo  = new GameObject("Symbol");
            checkTxtGo.transform.SetParent(checkGo.transform, false);
            var checkTMP = checkTxtGo.AddComponent<TextMeshProUGUI>();
            checkTMP.text      = "☐";
            checkTMP.fontSize  = 22;
            checkTMP.color     = new Color(0.384f, 0.561f, 1f, 1f);
            checkTMP.alignment = TextAlignmentOptions.Center;
            var checkSymRT = checkTxtGo.GetComponent<RectTransform>();
            checkSymRT.anchorMin = Vector2.zero;
            checkSymRT.anchorMax = Vector2.one;
            checkSymRT.offsetMin = Vector2.zero;
            checkSymRT.offsetMax = Vector2.zero;

            // Toggle behaviour
            bool isChecked = false;
            checkBtn.onClick.AddListener(() =>
            {
                isChecked = !isChecked;
                checkTMP.text  = isChecked ? "☑" : "☐";
                checkImg.color = isChecked
                    ? new Color(0.384f, 0.561f, 1f, 0.7f)
                    : new Color(0.384f, 0.561f, 1f, 0.3f);
            });

            var checkLE            = checkGo.AddComponent<LayoutElement>();
            checkLE.minWidth       = 32;
            checkLE.preferredWidth = 32;
            checkLE.flexibleWidth  = 0;

            // Thin vertical separator
            var sep1Go   = new GameObject("Sep1");
            sep1Go.transform.SetParent(item.transform, false);
            var sep1Img  = sep1Go.AddComponent<Image>();
            sep1Img.color = new Color(1f, 1f, 1f, 0.12f);
            var sep1LE   = sep1Go.AddComponent<LayoutElement>();
            sep1LE.minWidth       = 1;
            sep1LE.preferredWidth = 1;
            sep1LE.flexibleWidth  = 0;

            // ── INPUT FIELD (70% width) ──
            var inputGo  = new GameObject("Input");
            inputGo.transform.SetParent(item.transform, false);
            var inputImg = inputGo.AddComponent<Image>();
            inputImg.color    = new Color(0f, 0f, 0f, 0f);
            inputImg.raycastTarget = false;
            var input = inputGo.AddComponent<TMP_InputField>();
            input.targetGraphic = inputImg;

            // Placeholder
            var placeholderGo  = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(inputGo.transform, false);
            var placeholderTMP = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholderTMP.text      = "Enter item...";
            placeholderTMP.fontSize  = 16;
            placeholderTMP.color     = new Color(1f, 1f, 1f, 0.3f);
            placeholderTMP.fontStyle = FontStyles.Italic;
            var phRT = placeholderGo.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(10, 0);
            phRT.offsetMax = new Vector2(-10, 0);

            // Text
            var textGo  = new GameObject("Text");
            textGo.transform.SetParent(inputGo.transform, false);
            var textTMP = textGo.AddComponent<TextMeshProUGUI>();
            textTMP.fontSize  = 16;
            textTMP.color     = Color.white;
            var textRT = textGo.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10, 0);
            textRT.offsetMax = new Vector2(-10, 0);

            input.textComponent   = textTMP;
            input.placeholder     = placeholderTMP;
            input.text            = "";

            var inputLE            = inputGo.AddComponent<LayoutElement>();
            inputLE.flexibleWidth  = 1f;

            // Thin vertical separator before delete
            var sep2Go   = new GameObject("Sep2");
            sep2Go.transform.SetParent(item.transform, false);
            var sep2Img  = sep2Go.AddComponent<Image>();
            sep2Img.color = new Color(1f, 1f, 1f, 0.12f);
            var sep2LE   = sep2Go.AddComponent<LayoutElement>();
            sep2LE.minWidth       = 1;
            sep2LE.preferredWidth = 1;
            sep2LE.flexibleWidth  = 0;

            // ── DELETE BUTTON (20% width) ──
            var delGo  = new GameObject("Del");
            delGo.transform.SetParent(item.transform, false);
            var delImg = delGo.AddComponent<Image>();
            delImg.color = new Color(0.7f, 0.1f, 0.1f, 1f);
            var delBtn = delGo.AddComponent<Button>();
            delBtn.targetGraphic = delImg;
            var delCb = delBtn.colors;
            delCb.highlightedColor = new Color(1f, 0.3f, 0.3f, 1f);
            delCb.pressedColor     = new Color(0.7f, 0.1f, 0.1f, 1f);
            delBtn.colors = delCb;

            var delTxtGo  = new GameObject("X");
            delTxtGo.transform.SetParent(delGo.transform, false);
            var delTMP = delTxtGo.AddComponent<TextMeshProUGUI>();
            delTMP.text      = "✕";
            delTMP.fontSize  = 18;
            delTMP.color     = Color.white;
            delTMP.alignment = TextAlignmentOptions.Center;
            var delTxtRT = delTxtGo.GetComponent<RectTransform>();
            delTxtRT.anchorMin = Vector2.zero;
            delTxtRT.anchorMax = Vector2.one;
            delTxtRT.offsetMin = Vector2.zero;
            delTxtRT.offsetMax = Vector2.zero;

            var delLE            = delGo.AddComponent<LayoutElement>();
            delLE.minWidth       = 48;
            delLE.preferredWidth = 48;
            delLE.flexibleWidth  = 0;

            // Only delete if more than 1 row remains
            delBtn.onClick.AddListener(() =>
            {
                int rowCount = 0;
                foreach (Transform child in checklistItemParent)
                    if (child.name == "ChecklistRow") rowCount++;
                if (rowCount > 1) Destroy(item);
            });
        }
        private void ClearChecklist()
        {
            if (checklistItemParent == null) return;
            foreach (Transform child in checklistItemParent)
                Destroy(child.gameObject);
        }

        private void RefreshChecklistVisibility()
        {
            if (checklistContainer == null) return;
            var cg = checklistContainer.GetComponent<CanvasGroup>()
                  ?? checklistContainer.gameObject.AddComponent<CanvasGroup>();
            cg.alpha          = _isChecklist ? 1f : 0f;
            cg.interactable   = _isChecklist;
            cg.blocksRaycasts = _isChecklist;
            // Also toggle the note input field
            if (noteInput != null)
            {
                var inputCg = noteInput.GetComponent<CanvasGroup>()
                        ?? noteInput.gameObject.AddComponent<CanvasGroup>();
                inputCg.alpha          = _isChecklist ? 0f : 1f;
                inputCg.interactable   = !_isChecklist;
                inputCg.blocksRaycasts = !_isChecklist;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Color swatch building — no prefab required
        // ─────────────────────────────────────────────────────────────

        private void EnsureLayoutGroup(Transform row)
        {
            if (row == null) return;
            var hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null)
            {
                hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing              = SwatchSpacing;
                hlg.childAlignment       = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = false;
                hlg.childControlWidth  = false;
                hlg.childControlHeight = false;
            }
        }

        private void BuildColorRow(Transform row, Action<Color> onSelect, bool isBg)
        {
            if (row == null) return;
            foreach (Transform child in row) Destroy(child.gameObject);

            GameObject[] refs = new GameObject[_palette.Count];

            for (int i = 0; i < _palette.Count; i++)
            {
                var c    = _palette[i].color;
                var idx  = i;

                GameObject go;
                if (colorSwatchPrefab != null)
                {
                    go = Instantiate(colorSwatchPrefab, row);
                    go.GetComponent<Image>().color = c;
                }
                else
                {
                    go = BuildSwatchFromCode(row, c);
                }

                refs[i] = go;
                var btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                btn.onClick.AddListener(() =>
                {
                    onSelect(c);
                    HighlightSwatch(row, go);
                });
            }

            // Highlight first swatch by default
            if (refs.Length > 0) HighlightSwatch(row, refs[isBg ? 0 : 8]);
        }

        private GameObject BuildSwatchFromCode(Transform parent, Color color)
        {
            var go = new GameObject("Swatch");
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(SwatchSize, SwatchSize);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth       = SwatchSize;
            le.preferredWidth = SwatchSize;
            le.minHeight       = SwatchSize;
            le.preferredHeight = SwatchSize;
            le.flexibleWidth  = 0;
            le.flexibleHeight = 0;

            var img = go.AddComponent<Image>();
            img.color = color;
            if (swatchCircleSprite != null) img.sprite = swatchCircleSprite;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.highlightedColor = Color.white;
            cb.pressedColor     = new Color(0.8f, 0.8f, 0.8f);
            btn.colors = cb;

            return go;
        }

        private void HighlightSwatch(Transform row, GameObject selected)
        {
            foreach (Transform child in row)
            {
                var outline = child.GetComponent<Outline>()
                           ?? child.gameObject.AddComponent<Outline>();
                if (child.gameObject == selected)
                {
                    outline.effectColor    = Color.white;
                    outline.effectDistance = new Vector2(2f, -2f);
                    outline.enabled        = true;
                }
                else
                {
                    outline.enabled = false;
                }
            }
        }
    }
}