using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.Notes;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Part notes, the panels (V3 Phase 5):
    ///
    ///   NOTES (left rail)  the list of every note on the model. ADD starts placing a note,
    ///                      HIDE ALL / SHOW ALL hides every note on this phone. Tapping a row turns
    ///                      the model so the note faces you, and opens it.
    ///   Note card          the whole note, who wrote it and on which part. EDIT and DELETE for
    ///                      its author and (shared notes) the presenter; HIDE for anyone, on their
    ///                      own phone. DELETE asks for a second tap.
    ///   Editor             text (up to 300 characters), colour, and Everyone / Only me.
    ///
    /// The list sits where the parts drawer does (opening one closes the other); the card and the
    /// editor are centred over a dimmed screen. All three close with Back.
    /// </summary>
    public class PartNotesPanelUI : MonoBehaviour
    {
        public static PartNotesPanelUI Instance { get; private set; }

        private const string RailId = "notes";
        private const string NavList = "notes", NavCard = "note-card", NavEditor = "note-editor";

        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color AccentColor = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color DangerColor = new Color(0.55f, 0.18f, 0.18f, 1f);
        private static readonly Color RowColor    = new Color(0.14f, 0.16f, 0.22f, 0.95f);
        private static readonly Color DimText     = new Color(1f, 1f, 1f, 0.6f);

        // List
        private GameObject      _list;
        private TextMeshProUGUI _listTitle;
        private TextMeshProUGUI _hideAllLabel;
        private RectTransform   _rows;
        private TextMeshProUGUI _empty;

        // Modal layer (card + editor)
        private GameObject _modal;

        // Card
        private GameObject      _card;
        private Image           _cardStrip;
        private TextMeshProUGUI _cardWho, _cardPart, _cardText;
        private Button          _editBtn, _deleteBtn;
        private TextMeshProUGUI _deleteLabel, _hideLabel;
        private string          _cardId = "";
        private float           _deleteArmedUntil;

        // Editor
        private GameObject      _editor;
        private TextMeshProUGUI _editorTitle, _editorHint;
        private TMP_InputField  _input;
        private readonly List<Image> _swatches = new List<Image>();
        private GameObject      _visRow;
        private Image           _everyoneImg, _onlyMeImg;
        private Button          _saveBtn;
        private string          _editColor = PartNote.Colors[0];
        private bool            _editPrivate;
        private PartNote        _editing;                         // null = a new note
        private string          _newPath; private Vector3 _newPoint, _newNormal;

        private bool _railVisible;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            PartNoteManager.OnChanged += OnNotesChanged;
            PartNoteManager.OnSpotPicked += OnSpotPicked;
        }

        void Start() => StartCoroutine(AddRailButton());

        void OnDestroy()
        {
            PartNoteManager.OnChanged -= OnNotesChanged;
            PartNoteManager.OnSpotPicked -= OnSpotPicked;
            PanelNavigator.Remove(NavList);
            PanelNavigator.Remove(NavCard);
            PanelNavigator.Remove(NavEditor);
            if (Instance == this) Instance = null;
        }

        private IEnumerator AddRailButton()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(RailId, "NOTES", ToggleList);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);
        }

        void Update()
        {
            bool modelShown = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) &&
                              !AuraSync.Challenge.ReassemblyGame.IsActive;
            if (modelShown != _railVisible && SpaceSessionHUD.Instance != null)
            {
                _railVisible = modelShown;
                SpaceSessionHUD.Instance.SetSlotVisible(RailId, modelShown);
                if (!modelShown) { CloseList(); CloseModal(); }
            }

            if (_deleteArmedUntil > 0f && Time.unscaledTime > _deleteArmedUntil)
            {
                _deleteArmedUntil = 0f;
                if (_deleteLabel != null) _deleteLabel.text = "DELETE";
            }
        }

        private void OnNotesChanged()
        {
            RefreshRail();
            if (_list.activeSelf) RebuildRows();

            if (_card.activeSelf)
            {
                PartNote n = PartNoteManager.Instance != null ? PartNoteManager.Instance.Find(_cardId) : null;
                if (n == null) CloseModal(); // deleted, or its author left the room
                else FillCard(n);
            }
        }

        private void RefreshRail()
        {
            if (SpaceSessionHUD.Instance == null) return;
            SpaceSessionHUD.Instance.SetSlotLabel(RailId, PartNoteManager.HideAll ? "NOTES\n(off)" : "NOTES");
            int count = PartNoteManager.Instance != null ? PartNoteManager.Instance.All().Count : 0;
            SpaceSessionHUD.Instance.SetSlotCount(RailId, count);
        }

        // ── List ───────────────────────────────────────────────────────────────

        private void ToggleList()
        {
            if (_list.activeSelf) CloseList();
            else OpenList();
        }

        private void OpenList()
        {
            ModelPartsDrawerUI.Instance?.SetOpen(false); // they share the same spot
            SectionCutUI.Instance?.CloseToolbar();
            SimulationPanelUI.Instance?.Hide();
            _list.SetActive(true);
            PanelNavigator.Push(NavList, this, CloseList);
            RebuildRows();
        }

        /// <summary>Closes the notes list (the parts drawer calls this when it opens in the same spot).</summary>
        public void CloseList()
        {
            if (_list == null || !_list.activeSelf) return;
            _list.SetActive(false);
            PanelNavigator.Remove(NavList);
        }

        private void RebuildRows()
        {
            for (int i = _rows.childCount - 1; i >= 0; i--) Destroy(_rows.GetChild(i).gameObject);

            List<PartNote> notes = PartNoteManager.Instance != null ? PartNoteManager.Instance.All() : new List<PartNote>();
            _listTitle.text = $"NOTES ({notes.Count})";
            _hideAllLabel.text = PartNoteManager.HideAll ? "SHOW ALL" : "HIDE ALL";
            _empty.gameObject.SetActive(notes.Count == 0);

            foreach (PartNote n in notes)
            {
                PartNote note = n;
                var row = new GameObject("Row", typeof(RectTransform));
                row.transform.SetParent(_rows, false);
                Layout(row, height: 74f);
                var bg = row.AddComponent<Image>();
                bg.color = RowColor;
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => OnRowPressed(note));
                var hg = row.AddComponent<HorizontalLayoutGroup>();
                hg.padding = new RectOffset(10, 10, 6, 6);
                hg.spacing = 10;
                hg.childAlignment = TextAnchor.MiddleLeft;
                hg.childControlWidth = hg.childControlHeight = true;
                hg.childForceExpandWidth = false;

                var dot = new GameObject("Colour", typeof(RectTransform));
                dot.transform.SetParent(row.transform, false);
                dot.AddComponent<Image>().color = n.Color;
                var dle = dot.AddComponent<LayoutElement>();
                dle.preferredWidth = dle.minWidth = 14f;

                string first = (n.text ?? "").Replace("\n", " ");
                string tags = (n.isPrivate ? "  only you" : "") + (PartNoteManager.IsHiddenOne(n) ? "  hidden" : "");
                var t = Text(row.transform, $"{Esc(first)}\n<size=75%><color=#9AA4B8>{Esc(n.authorName)} · {Esc(PartName(n.path))}{tags}</color></size>", 20f, Color.white);
                t.enableWordWrapping = false;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
        }

        private void OnRowPressed(PartNote n)
        {
            // Show it: turn the model so the note faces this person, and open it.
            PartNoteManager.SetHidden(n, false);
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Transform part = ModelRaycaster.FindPart(root, n.path);
            if (part != null) SpaceInteractionController.Instance.TurnToFace(part.TransformDirection(n.LocalNormal));
            CloseList();
            ShowCard(n);
        }

        private void OnAddPressed()
        {
            CloseList();
            PartNoteManager.Instance?.BeginPlacing();
        }

        private void OnHideAllPressed()
        {
            PartNoteManager.SetHideAll(!PartNoteManager.HideAll);
            Toast.Show(PartNoteManager.HideAll ? "Notes hidden on your phone." : "Notes shown again.");
        }

        // ── Card ───────────────────────────────────────────────────────────────

        public void ShowCard(PartNote n)
        {
            if (n == null) return;
            _cardId = n.id;
            _deleteArmedUntil = 0f;
            FillCard(n);
            _editor.SetActive(false);
            _card.SetActive(true);
            _modal.SetActive(true);
            PanelNavigator.Remove(NavEditor);
            PanelNavigator.Push(NavCard, this, CloseModal);
        }

        private void FillCard(PartNote n)
        {
            _cardStrip.color = n.Color;
            _cardWho.text = $"Note by <b>{Esc(n.authorName)}</b>" + (n.isPrivate ? "  <color=#FFD133>only you</color>" : "");
            _cardPart.text = "On: " + Esc(PartName(n.path));
            _cardText.text = Esc(n.text);
            bool canEdit = PartNoteManager.CanEdit(n);
            _editBtn.gameObject.SetActive(canEdit);
            _deleteBtn.gameObject.SetActive(canEdit);
            if (_deleteArmedUntil <= 0f) _deleteLabel.text = "DELETE";
            _hideLabel.text = PartNoteManager.IsHiddenOne(n) ? "UNHIDE" : "HIDE";
        }

        private PartNote CardNote => PartNoteManager.Instance != null ? PartNoteManager.Instance.Find(_cardId) : null;

        private void OnEditPressed()
        {
            PartNote n = CardNote;
            if (n != null) OpenEditor(n, null, default, default);
        }

        private void OnDeletePressed()
        {
            PartNote n = CardNote;
            if (n == null) return;
            if (_deleteArmedUntil > Time.unscaledTime)
            {
                PartNoteManager.Instance?.Delete(n);
                CloseModal();
                Toast.Show("Note deleted.");
                return;
            }
            _deleteArmedUntil = Time.unscaledTime + 3f;
            _deleteLabel.text = "TAP AGAIN";
        }

        private void OnHidePressed()
        {
            PartNote n = CardNote;
            if (n == null) return;
            bool hide = !PartNoteManager.IsHiddenOne(n);
            PartNoteManager.SetHidden(n, hide);
            if (hide) { CloseModal(); Toast.Show("Hidden on your phone. Unhide it from the NOTES list."); }
        }

        // ── Editor ─────────────────────────────────────────────────────────────

        private void OnSpotPicked(string path, Vector3 point, Vector3 normal) => OpenEditor(null, path, point, normal);

        private void OpenEditor(PartNote editing, string path, Vector3 point, Vector3 normal)
        {
            _editing = editing;
            _newPath = path; _newPoint = point; _newNormal = normal;

            bool mine = editing == null || PartNoteManager.IsMine(editing);
            _editorTitle.text = editing == null ? $"New note on {Esc(PartName(path))}" : "Edit note";
            _input.SetTextWithoutNotify(editing != null ? editing.text : "");
            _editColor = editing != null ? editing.colorHex : PartNote.Colors[0];
            _editPrivate = editing != null && editing.isPrivate;

            // Only the author decides who sees it; the presenter editing someone's note cannot hide it.
            _visRow.SetActive(mine);
            _editorHint.gameObject.SetActive(mine);
            RefreshEditor();

            _card.SetActive(false);
            _editor.SetActive(true);
            _modal.SetActive(true);
            PanelNavigator.Remove(NavCard);
            PanelNavigator.Push(NavEditor, this, CloseModal);
            _input.ActivateInputField();
        }

        private void RefreshEditor()
        {
            for (int i = 0; i < _swatches.Count; i++)
            {
                bool on = PartNote.Colors[i] == _editColor;
                _swatches[i].transform.localScale = on ? Vector3.one * 1.18f : Vector3.one;
                var outline = _swatches[i].GetComponent<Outline>();
                if (outline != null) outline.enabled = on;
            }
            _everyoneImg.color = _editPrivate ? ButtonColor : AccentColor;
            _onlyMeImg.color   = _editPrivate ? AccentColor : ButtonColor;
            _editorHint.text = _editPrivate
                ? "Only me: kept on this phone, never sent to anyone."
                : PhotonNetwork.InRoom ? "Everyone: everyone in this room sees it while you are here."
                                       : "Everyone: shared whenever you are in a room on this model.";
            _saveBtn.interactable = !string.IsNullOrWhiteSpace(_input.text);
        }

        private void OnSavePressed()
        {
            var mgr = PartNoteManager.Instance;
            if (mgr == null || string.IsNullOrWhiteSpace(_input.text)) return;

            if (_editing == null)
            {
                PartNote n = mgr.Add(_newPath, _newPoint, _newNormal, _input.text, _editColor, _editPrivate);
                if (n == null) return;
                Toast.Show(n.isPrivate || !PhotonNetwork.InRoom ? "Note saved." : "Note added for everyone.");
            }
            else
            {
                PartNote current = mgr.Find(_editing.id);
                if (current == null) { CloseModal(); return; }
                mgr.Edit(current, _input.text, _editColor, PartNoteManager.IsMine(current) ? _editPrivate : current.isPrivate);
                Toast.Show("Note saved.");
            }
            CloseModal();
        }

        private void CloseModal()
        {
            if (_input != null) _input.DeactivateInputField();
            if (_modal != null) _modal.SetActive(false);
            PanelNavigator.Remove(NavCard);
            PanelNavigator.Remove(NavEditor);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string PartName(string path)
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Transform part = ModelRaycaster.FindPart(root, path);
            return AuraSync.AI.PartNames.Display(path, part != null ? part.name : "a part");
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 97; // the parts drawer's level; the two never show together
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            BuildList();
            BuildModal();
        }

        private void BuildList()
        {
            _list = new GameObject("NotesList", typeof(RectTransform));
            _list.transform.SetParent(transform, false);
            var rt = (RectTransform)_list.transform;
            rt.anchorMin = new Vector2(0.16f, 0.22f);
            rt.anchorMax = new Vector2(0.64f, 0.84f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _list.AddComponent<Image>().color = PanelColor;
            _list.AddComponent<TouchBlocker>();
            var vg = _list.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(10, 10, 10, 10);
            vg.spacing = 8;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var header = Row(_list.transform, 50f);
            _listTitle = Text(header.transform, "NOTES", 22f, Color.white);
            _listTitle.fontStyle = FontStyles.Bold;
            _listTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            SmallButton(header.transform, "ADD", AccentColor, OnAddPressed, 84f);
            _hideAllLabel = SmallButton(header.transform, "HIDE ALL", ButtonColor, OnHideAllPressed, 120f).GetComponentInChildren<TextMeshProUGUI>();
            SmallButton(header.transform, "X", new Color(0.45f, 0.14f, 0.14f), CloseList, 50f);

            _empty = Text(_list.transform, "No notes on this model yet. Tap ADD, then tap the spot on the model.", 19f, DimText);
            _empty.enableWordWrapping = true;
            Layout(_empty.gameObject, height: 60f);

            // Scrolling list of notes.
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(_list.transform, false);
            Layout(scrollGo, height: 100f, flexHeight: 1f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            scroll.viewport = vrt;
            var content = new GameObject("Rows", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _rows = (RectTransform)content.transform;
            _rows.anchorMin = new Vector2(0f, 1f); _rows.anchorMax = new Vector2(1f, 1f);
            _rows.pivot = new Vector2(0.5f, 1f);
            _rows.offsetMin = _rows.offsetMax = Vector2.zero;
            var cvg = content.AddComponent<VerticalLayoutGroup>();
            cvg.spacing = 4;
            cvg.childControlWidth = cvg.childControlHeight = true;
            cvg.childForceExpandWidth = true; cvg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _rows;

            _list.SetActive(false);
        }

        private void BuildModal()
        {
            _modal = new GameObject("NoteModal", typeof(RectTransform));
            _modal.transform.SetParent(transform, false);
            var mrt = (RectTransform)_modal.transform;
            mrt.anchorMin = Vector2.zero; mrt.anchorMax = Vector2.one; mrt.offsetMin = mrt.offsetMax = Vector2.zero;
            // Its own canvas, above the chat and the room panel, below the share prompt (105).
            var mc = _modal.AddComponent<Canvas>();
            mc.overrideSorting = true;
            mc.sortingOrder = 102;
            _modal.AddComponent<GraphicRaycaster>();
            _modal.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            _modal.AddComponent<TouchBlocker>();
            var dimBtn = _modal.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(CloseModal);

            BuildCard();
            BuildEditor();
            _modal.SetActive(false);
        }

        private GameObject Card(string name, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_modal.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(820f, height);
            go.AddComponent<Image>().color = PanelColor;
            go.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card stay on it
            var vg = go.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(0, 0, 0, 20);
            vg.spacing = 12;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            return go;
        }

        private void BuildCard()
        {
            _card = Card("NoteCard", 620f);

            var strip = new GameObject("Strip", typeof(RectTransform));
            strip.transform.SetParent(_card.transform, false);
            Layout(strip, height: 14f);
            _cardStrip = strip.AddComponent<Image>();

            _cardWho = Pad(Text(_card.transform, "", 26f, Color.white), 44f);
            _cardPart = Pad(Text(_card.transform, "", 21f, DimText), 32f);

            // The text, scrolling if long.
            var scrollGo = new GameObject("Body", typeof(RectTransform));
            scrollGo.transform.SetParent(_card.transform, false);
            Layout(scrollGo, height: 120f, flexHeight: 1f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(28f, 0f); vrt.offsetMax = new Vector2(-28f, 0f);
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            scroll.viewport = vrt;
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f); crt.offsetMin = crt.offsetMax = Vector2.zero;
            var cvg = content.AddComponent<VerticalLayoutGroup>();
            cvg.childControlWidth = cvg.childControlHeight = true;
            cvg.childForceExpandWidth = true; cvg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = crt;
            _cardText = Text(content.transform, "", 25f, Color.white);
            _cardText.enableWordWrapping = true;

            var buttons = Row(_card.transform, 70f);
            buttons.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(24, 24, 0, 0);
            buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            _editBtn = SmallButton(buttons.transform, "EDIT", AccentColor, OnEditPressed, 120f);
            _deleteBtn = SmallButton(buttons.transform, "DELETE", DangerColor, OnDeletePressed, 120f);
            _deleteLabel = _deleteBtn.GetComponentInChildren<TextMeshProUGUI>();
            _hideLabel = SmallButton(buttons.transform, "HIDE", ButtonColor, OnHidePressed, 120f).GetComponentInChildren<TextMeshProUGUI>();
            SmallButton(buttons.transform, "CLOSE", ButtonColor, CloseModal, 120f);
        }

        private void BuildEditor()
        {
            _editor = Card("NoteEditor", 760f);

            var strip = new GameObject("Strip", typeof(RectTransform));
            strip.transform.SetParent(_editor.transform, false);
            Layout(strip, height: 14f);
            strip.AddComponent<Image>().color = AccentColor;

            _editorTitle = Pad(Text(_editor.transform, "New note", 28f, Color.white), 48f);
            _editorTitle.fontStyle = FontStyles.Bold;
            _editorTitle.enableAutoSizing = true; _editorTitle.fontSizeMin = 18f; _editorTitle.fontSizeMax = 28f;

            _input = BuildInput(_editor.transform);

            // Colours.
            var colours = Row(_editor.transform, 70f);
            colours.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(28, 28, 6, 6);
            colours.GetComponent<HorizontalLayoutGroup>().spacing = 18;
            foreach (string hex in PartNote.Colors)
            {
                string h = hex;
                var sw = new GameObject("Swatch", typeof(RectTransform));
                sw.transform.SetParent(colours.transform, false);
                var le = sw.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = 58f;
                var img = sw.AddComponent<Image>();
                img.color = ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.yellow;
                var o = sw.AddComponent<Outline>();
                o.effectColor = Color.white;
                o.effectDistance = new Vector2(4f, 4f);
                o.enabled = false;
                var b = sw.AddComponent<Button>();
                b.targetGraphic = img;
                b.onClick.AddListener(() => { _editColor = h; RefreshEditor(); });
                _swatches.Add(img);
            }

            // Who sees it.
            _visRow = Row(_editor.transform, 66f);
            var vh = _visRow.GetComponent<HorizontalLayoutGroup>();
            vh.padding = new RectOffset(28, 28, 0, 0);
            vh.childForceExpandWidth = true;
            _everyoneImg = SmallButton(_visRow.transform, "EVERYONE", AccentColor, () => { _editPrivate = false; RefreshEditor(); }, 200f).GetComponent<Image>();
            _onlyMeImg   = SmallButton(_visRow.transform, "ONLY ME", ButtonColor, () => { _editPrivate = true; RefreshEditor(); }, 200f).GetComponent<Image>();
            _editorHint = Pad(Text(_editor.transform, "", 19f, DimText), 34f);

            var buttons = Row(_editor.transform, 72f);
            var bh = buttons.GetComponent<HorizontalLayoutGroup>();
            bh.padding = new RectOffset(28, 28, 0, 0);
            bh.childForceExpandWidth = true;
            SmallButton(buttons.transform, "CANCEL", ButtonColor, CloseModal, 200f);
            _saveBtn = SmallButton(buttons.transform, "SAVE", new Color(0.16f, 0.55f, 0.34f, 1f), OnSavePressed, 200f);
        }

        private TMP_InputField BuildInput(Transform parent)
        {
            // A padded row, so the box is inset from the card's edges like everything else.
            var row = Row(parent, 230f);
            var rh = row.GetComponent<HorizontalLayoutGroup>();
            rh.padding = new RectOffset(28, 28, 0, 0);
            rh.childForceExpandWidth = true;

            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            go.SetActive(false); // TMP_InputField wires itself up on enable; configure it first
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            go.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 1f);

            var area = new GameObject("TextArea", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(16f, 10f); art.offsetMax = new Vector2(-16f, -10f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = Text(area.transform, "Write your note...", 24f, new Color(1f, 1f, 1f, 0.3f));
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.TopLeft;
            TextMeshProUGUI text = Text(area.transform, "", 24f, Color.white);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.richText = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = art;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
            field.characterLimit = PartNoteManager.MaxTextChars;
            field.richText = false;
            field.onValueChanged.AddListener(_ => { if (_saveBtn != null) _saveBtn.interactable = !string.IsNullOrWhiteSpace(field.text); });

            go.SetActive(true);
            return field;
        }

        private static GameObject Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Layout(go, height: height);
            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 8;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;
            return go;
        }

        private static TextMeshProUGUI Pad(TextMeshProUGUI t, float height)
        {
            Layout(t.gameObject, height: height);
            t.margin = new Vector4(28f, 0f, 28f, 0f);
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        private static Button SmallButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float width)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
            le.minHeight = 48f;
            var img = go.AddComponent<Image>();
            img.color = color;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            var t = Text(go.transform, label, 21f, Color.white);
            Stretch(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 21f;
            t.enableWordWrapping = false;
            return b;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 0f); rt.offsetMax = new Vector2(-4f, 0f);
        }

        private static void Layout(GameObject go, float height = 0f, float flexHeight = 0f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (height > 0f) { le.minHeight = height; le.preferredHeight = height; }
            le.flexibleHeight = flexHeight;
        }
    }
}
