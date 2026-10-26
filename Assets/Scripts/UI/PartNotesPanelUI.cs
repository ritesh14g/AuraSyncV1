using System;
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
    ///   NOTES (dock tab)   a card beside the tab, only as tall as its notes (one note = a small
    ///                      card; it grows with more, up to half the screen, then scrolls). Add
    ///                      starts placing a note; ⋯ holds Hide all / Show all. Tapping a note turns
    ///                      the model so it faces you, and opens it.
    ///   Note card          the whole note, who wrote it and on which part: Edit and Delete for its
    ///                      author (and the presenter for shared notes), Hide for anyone on their
    ///                      own phone. Delete asks for a second tap.
    ///   Editor             text (up to 300 characters), colour, and Everyone / Only me.
    ///
    /// The note card and the editor sit over a dimmed screen with Back in its usual place
    /// (bottom-left) and, in the editor, Save bottom-right.
    /// </summary>
    public class PartNotesPanelUI : MonoBehaviour
    {
        public static PartNotesPanelUI Instance { get; private set; }

        private const string RailId = "notes";
        private const string NavList = "notes", NavCard = "note-card", NavEditor = "note-editor";

        // List
        private AuraSlidePanel _list;
        private RectTransform _listRt;
        private TextMeshProUGUI _listTitle, _empty, _hint;
        private RectTransform _rows;
        private GameObject _menu;
        private TextMeshProUGUI _hideAllLabel;

        // Modal layer (card + editor)
        private GameObject _modal;
        private RectTransform _modalContent;
        private GameObject _saveSlot;
        private Button _saveBtn;

        // Card
        private GameObject _card;
        private Image _cardStrip;
        private TextMeshProUGUI _cardWho, _cardPart, _cardText;
        private GameObject _editBtn, _deleteBtn;
        private TextMeshProUGUI _deleteLabel, _hideLabel;
        private string _cardId = "";
        private float _deleteArmedUntil;

        // Editor
        private GameObject _editor;
        private TextMeshProUGUI _editorTitle, _editorHint;
        private TMP_InputField _input;
        private Action<int> _setSwatch, _setVis;
        private GameObject _visRow;
        private string _editColor = PartNote.Colors[0];
        private bool _editPrivate;
        private PartNote _editing;                      // null = a new note
        private string _newPath; private Vector3 _newPoint, _newNormal;

        private bool _railVisible;

        private bool ListOpen => _list != null && _list.IsShown;

        // ── Lifecycle ──────────────────────────────────────────────────

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
            SpaceSessionHUD.Instance.AddRailButton(RailId, "Notes", ToggleList);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);
            RefreshRail();
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
                if (_deleteLabel != null) _deleteLabel.text = "Delete";
            }
        }

        private void OnNotesChanged()
        {
            RefreshRail();
            if (ListOpen) RebuildRows();

            if (_card.activeSelf && _modal.activeSelf)
            {
                PartNote n = PartNoteManager.Instance != null ? PartNoteManager.Instance.Find(_cardId) : null;
                if (n == null) CloseModal();
                else FillCard(n);
            }
        }

        private void RefreshRail()
        {
            if (SpaceSessionHUD.Instance == null) return;
            SpaceSessionHUD.Instance.SetSlotLabel(RailId, PartNoteManager.HideAll ? "Hidden" : "Notes");
            int count = PartNoteManager.Instance != null ? PartNoteManager.Instance.All().Count : 0;
            SpaceSessionHUD.Instance.SetSlotCount(RailId, count);
        }

        // ── List ───────────────────────────────────────────────────────

        private void ToggleList()
        {
            if (ListOpen) CloseList();
            else OpenList();
        }

        private void OpenList()
        {
            ViewerPanels.Opened(RailId, CloseList);
            PanelNavigator.Push(NavList, this, CloseList);
            RebuildRows();
            _list.Show();
        }

        /// <summary>Closes the notes list.</summary>
        public void CloseList()
        {
            if (!ListOpen) return;
            _list.Hide();
            if (_menu != null) _menu.SetActive(false);
            ViewerPanels.Closed(RailId);
            PanelNavigator.Remove(NavList);
        }

        private void RebuildRows()
        {
            AuraKit.ClearChildren(_rows);

            List<PartNote> notes = PartNoteManager.Instance != null ? PartNoteManager.Instance.All() : new List<PartNote>();
            _listTitle.text = notes.Count == 0 ? "Notes" : $"Notes · {notes.Count}";
            _hideAllLabel.text = PartNoteManager.HideAll ? "Show all notes" : "Hide all notes";
            _empty.gameObject.SetActive(notes.Count == 0);
            _hint.gameObject.SetActive(notes.Count > 0);

            foreach (PartNote n in notes)
            {
                PartNote note = n;
                var row = AuraKit.Box(_rows, "Note", AuraTheme.Ink, AuraTheme.Radius - 8f, true);
                AuraKit.Size(row, -1, 128f);
                var h = AuraKit.HStack(row, 18f);
                h.padding = new RectOffset(20, 26, 16, 16);
                AuraKit.MakeButton(row.gameObject, row, () => OnRowPressed(note));

                var strip = AuraKit.Box(row.transform, "Colour", n.Color, 5f);
                AuraKit.Size(strip, 10f, 80f);

                var words = AuraKit.Rect(row.transform, "Words");
                AuraKit.VStack(words, 4f, 0, TextAnchor.MiddleLeft);
                AuraKit.Size(words, 0f, -1, 1f);
                var t = AuraKit.Text(words, Esc((n.text ?? "").Replace("\n", " ")), AuraTheme.SizeSmall + 2f, AuraTheme.Text, AuraWeight.SemiBold);
                t.enableWordWrapping = false;
                string tags = (n.isPrivate ? " · only you" : "") + (PartNoteManager.IsHiddenOne(n) ? " · hidden" : "");
                var meta = AuraKit.Text(words, $"{Esc(PartName(n.path))} · {Esc(n.authorName)}{tags}", AuraTheme.SizeCaption - 2f, AuraTheme.TextDim);
                meta.enableWordWrapping = false;

                var go = AuraKit.Text(row.transform, "Show", AuraTheme.SizeCaption, AuraTheme.Lavender, AuraWeight.SemiBold,
                                      TextAlignmentOptions.MidlineRight);
                AuraKit.Size(go, 90f);
            }
        }

        private void OnRowPressed(PartNote n)
        {
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
            if (_menu != null) _menu.SetActive(false);
            PartNoteManager.SetHideAll(!PartNoteManager.HideAll);
            Toast.Show(PartNoteManager.HideAll ? "Notes hidden on your phone." : "Notes shown again.");
            RebuildRows();
        }

        // ── Card ───────────────────────────────────────────────────────

        public void ShowCard(PartNote n)
        {
            if (n == null) return;
            _cardId = n.id;
            _deleteArmedUntil = 0f;
            FillCard(n);
            _editor.SetActive(false);
            _card.SetActive(true);
            _saveSlot.SetActive(false);
            OpenModal();
            PanelNavigator.Remove(NavEditor);
            PanelNavigator.Push(NavCard, this, CloseModal);
        }

        private void FillCard(PartNote n)
        {
            _cardStrip.color = n.Color;
            _cardWho.text = $"Note by <b>{Esc(n.authorName)}</b>" + (n.isPrivate ? "  <color=#9894C4>only you</color>" : "");
            _cardPart.text = "On " + Esc(PartName(n.path));
            _cardText.text = Esc(n.text);
            bool canEdit = PartNoteManager.CanEdit(n);
            _editBtn.SetActive(canEdit);
            _deleteBtn.SetActive(canEdit);
            if (_deleteArmedUntil <= 0f) _deleteLabel.text = "Delete";
            _hideLabel.text = PartNoteManager.IsHiddenOne(n) ? "Unhide" : "Hide";
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
            _deleteLabel.text = "Tap again";
        }

        private void OnHidePressed()
        {
            PartNote n = CardNote;
            if (n == null) return;
            bool hide = !PartNoteManager.IsHiddenOne(n);
            PartNoteManager.SetHidden(n, hide);
            if (hide) { CloseModal(); Toast.Show("Hidden on your phone. Unhide it from the Notes list."); }
        }

        // ── Editor ─────────────────────────────────────────────────────

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

            // Only the author decides who sees it.
            _visRow.SetActive(mine);
            _editorHint.gameObject.SetActive(mine);
            RefreshEditor();

            _card.SetActive(false);
            _editor.SetActive(true);
            _saveSlot.SetActive(true);
            OpenModal();
            PanelNavigator.Remove(NavCard);
            PanelNavigator.Push(NavEditor, this, CloseModal);
            _input.ActivateInputField();
        }

        private void RefreshEditor()
        {
            int idx = Array.IndexOf(PartNote.Colors, _editColor);
            _setSwatch(idx < 0 ? 0 : idx);
            _setVis(_editPrivate ? 1 : 0);
            _editorHint.text = _editPrivate
                ? "Only me: kept on this phone, never sent to anyone."
                : PhotonNetwork.InRoom ? "Everyone: everyone in this room sees it while you are here."
                                       : "Everyone: shared whenever you are in a room on this model.";
            RefreshSave();
        }

        private void RefreshSave()
        {
            bool ok = !string.IsNullOrWhiteSpace(_input.text);
            _saveBtn.interactable = ok;
            var img = _saveBtn.GetComponent<Image>();
            if (img != null) img.color = ok ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Slate, 0.5f);
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

        private void OpenModal()
        {
            if (_modal.activeSelf) return;
            _modal.SetActive(true);
            var g = _modal.GetComponent<CanvasGroup>();
            g.alpha = 0f;
            AuraMotion.Fade(g, 1f, 0.18f);
        }

        private void CloseModal()
        {
            if (_input != null) _input.DeactivateInputField();
            if (_modal != null) _modal.SetActive(false);
            PanelNavigator.Remove(NavCard);
            PanelNavigator.Remove(NavEditor);
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static string PartName(string path)
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Transform part = ModelRaycaster.FindPart(root, path);
            return AuraSync.AI.PartNames.Display(path, part != null ? part.name : "a part");
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");

        private static Color[] NoteColors()
        {
            var cs = new Color[PartNote.Colors.Length];
            for (int i = 0; i < cs.Length; i++)
                cs[i] = ColorUtility.TryParseHtmlString(PartNote.Colors[i], out Color c) ? c : Color.yellow;
            return cs;
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "NotesCanvas", 97, out _);
            BuildList(safe);
            BuildModal();
        }

        private void BuildList(RectTransform safe)
        {
            _listRt = AuraViewer.ToolSheet(safe, "NotesCard");

            var head = AuraKit.Rect(_listRt, "Header");
            AuraKit.HStack(head, 14f);
            AuraKit.Size(head, -1, 96f);
            _listTitle = AuraKit.Text(head, "Notes", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            AuraKit.Flex(_listTitle);
            AuraWidgets.Chip(head, "Add", AuraIcons.Plus, true, OnAddPressed, out _, 92f);
            AuraWidgets.Round(head, AuraIcons.More, 92f, false, () => _menu.SetActive(!_menu.activeSelf), out _);

            // ⋯ menu: one switch.
            var menu = AuraKit.Rect(_listRt, "Menu");
            AuraKit.VStack(menu, 0f);
            var hide = AuraKit.Pill(menu, "Hide all notes", AuraIcons.EyeOff, AuraTheme.Ink, AuraTheme.Text, OnHideAllPressed,
                                    96f, AuraTheme.Slate);
            _hideAllLabel = hide.GetComponentInChildren<TextMeshProUGUI>();
            _menu = menu.gameObject;
            _menu.SetActive(false);

            _empty = AuraKit.Text(_listRt, "No notes on this model yet. Tap Add, then tap the spot on the model.",
                                  AuraTheme.SizeSmall, AuraTheme.TextDim);

            // The rows scroll only once they pass half the screen.
            var host = AuraKit.Rect(_listRt, "List");
            _rows = AuraWidgets.Scroll(host, out ScrollRect scroll, 12f);
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var fit = host.gameObject.AddComponent<FitToContent>();
            fit.content = _rows;
            fit.maxFraction = 0.42f;

            _hint = AuraKit.Text(_listRt, "Tap a note to turn the model to it", AuraTheme.SizeCaption - 2f, AuraTheme.TextDim,
                                 AuraWeight.Regular, TextAlignmentOptions.Center);

            _list = AuraViewer.SlideUp(_listRt);
        }

        private void BuildModal()
        {
            var canvas = AuraKit.CreateCanvas(transform, "NoteModal", 102); // above the chat and room card
            _modal = canvas.gameObject;
            _modal.AddComponent<CanvasGroup>();
            var dim = AuraKit.Box(canvas.transform, "Dim", AuraTheme.A(AuraTheme.Ink, 0.7f), 0f, true);
            AuraKit.Fill(dim.rectTransform);
            AuraKit.MakeButton(dim.gameObject, dim, CloseModal).transition = Selectable.Transition.None;

            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();
            _modalContent = AuraKit.Fill(AuraKit.Rect(safe, "Lift"));
            _modalContent.gameObject.AddComponent<KeyboardLift>().target = _modalContent;

            BuildCard();
            BuildEditor();

            // Back and Save in the same places as everywhere else.
            var back = AuraKit.Pill(_modalContent, "Back", AuraIcons.Back, AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Text,
                                    CloseModal, AuraViewer.BackHeight, AuraTheme.Slate);
            AuraKit.Pin((RectTransform)back.transform, new Vector2(0, 0), new Vector2(0, 0),
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));
            _saveBtn = AuraKit.Pill(_modalContent, "Save", AuraIcons.Ok, AuraTheme.Lavender, AuraTheme.Ink, OnSavePressed, AuraViewer.BackHeight);
            AuraKit.Pin((RectTransform)_saveBtn.transform, new Vector2(1, 0), new Vector2(1, 0),
                        new Vector2(-AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(300, AuraViewer.BackHeight));
            _saveSlot = _saveBtn.gameObject;

            _modal.SetActive(false);
        }

        private RectTransform ModalCard(string name)
        {
            var box = AuraKit.Box(_modalContent, name, AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(box, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.56f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            box.gameObject.AddComponent<MatchParentWidth>().max = 940f;
            var v = AuraKit.VStack(box, 18f);
            v.padding = new RectOffset(44, 44, 0, 40);
            AuraKit.FitHeight(box);
            return rt;
        }

        private void BuildCard()
        {
            var card = ModalCard("NoteCard");
            _card = card.gameObject;

            _cardStrip = AuraKit.Box(card, "Strip", Color.yellow, 6f);
            AuraKit.Size(_cardStrip, -1, 14f);

            _cardWho = AuraKit.Text(card, "", AuraTheme.SizeBody, AuraTheme.Text);
            _cardWho.richText = true;
            _cardPart = AuraKit.Text(card, "", AuraTheme.SizeSmall, AuraTheme.TextDim);

            var body = AuraKit.Rect(card, "Body");
            _cardText = AuraKit.Text(AuraWidgets.Scroll(body, out _, 0f), "", AuraTheme.SizeBody, AuraTheme.Text);
            _cardText.enableWordWrapping = true;
            var fit = body.gameObject.AddComponent<FitToContent>();
            fit.content = (RectTransform)_cardText.transform.parent;
            fit.maxFraction = 0.35f;

            var row = AuraKit.EqualRow(card, 100f, 16f);
            AuraWidgets.Chip(row, "Edit", AuraIcons.Edit, true, OnEditPressed, out Button edit, 100f);
            AuraKit.Cell(edit);
            _editBtn = edit.gameObject;
            AuraWidgets.Chip(row, "Delete", AuraIcons.Trash, false, OnDeletePressed, out Button del, 100f);
            AuraKit.Cell(del);
            _deleteBtn = del.gameObject;
            _deleteLabel = del.GetComponentInChildren<TextMeshProUGUI>();
            if (_deleteLabel != null) _deleteLabel.color = AuraTheme.Rose;
            AuraWidgets.Chip(row, "Hide", AuraIcons.EyeOff, false, OnHidePressed, out Button hide, 100f);
            AuraKit.Cell(hide);
            _hideLabel = hide.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void BuildEditor()
        {
            var card = ModalCard("NoteEditor");
            _editor = card.gameObject;

            var strip = AuraKit.Box(card, "Strip", AuraTheme.Lavender, 6f);
            AuraKit.Size(strip, -1, 14f);

            _editorTitle = AuraKit.Text(card, "New note", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            _editorTitle.enableWordWrapping = false;
            _editorTitle.overflowMode = TextOverflowModes.Ellipsis;

            _input = AuraWidgets.Input(card, "Write your note…", "", true, 250f, PartNoteManager.MaxTextChars);
            _input.richText = false;
            _input.onValueChanged.AddListener(_ => RefreshSave());

            AuraKit.Caps(card, "Colour");
            _setSwatch = AuraWidgets.Swatches(card, NoteColors(), 0, i => { _editColor = PartNote.Colors[i]; });

            var vis = AuraKit.Rect(card, "Visibility");
            AuraKit.VStack(vis, 10f);
            AuraKit.Caps(vis, "Who can see it");
            _setVis = AuraWidgets.Segmented(vis, new[] { "Everyone", "Only me" }, 0, i => { _editPrivate = i == 1; RefreshEditor(); });
            _visRow = vis.gameObject;
            _editorHint = AuraKit.Text(card, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
        }
    }

    /// <summary>
    /// Sizes a scroll area to its content, up to a fraction of the screen: short lists take only
    /// the room they need, long ones stop growing and scroll.
    /// </summary>
    public class FitToContent : MonoBehaviour
    {
        public RectTransform content;
        public float maxFraction = 0.4f;
        private LayoutElement _le;

        void Awake()
        {
            _le = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        }

        void LateUpdate()
        {
            if (content == null) return;
            var canvas = GetComponentInParent<Canvas>();
            float max = canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.height * maxFraction : 800f;
            float h = Mathf.Min(content.rect.height, max);
            if (!Mathf.Approximately(_le.preferredHeight, h))
            {
                _le.minHeight = h;
                _le.preferredHeight = h;
            }
        }
    }
}
