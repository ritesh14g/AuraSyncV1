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
    /// Play mode: Note / Object → type or shape → customise → place. After placing, the camera view
    /// is clear apart from the chip (add the next item) and Back (leave Play mode). Double-tapping a
    /// placed item and choosing Edit reopens the same editor for it.
    /// </summary>
    public partial class OnboardingFlow
    {
        private static readonly string[] ShapeNames = { "Cube", "Sphere", "Cylinder", "Capsule", "Plane" };

        // ── Choosers ───────────────────────────────────────────────────

        private View BuildPlayChooser()
        {
            var v = NewView("play.choose", Mode.Play, 0.3f);
            var card = AuraKit.Card(v.root, "Add to your space", "What do you want to place?", out var body);
            var row = AuraKit.EqualRow(body, 340f);
            AuraKit.Tile(row, AuraIcons.Note, "Note", "Text or checklist", () => Push(BuildNoteType()));
            AuraKit.Tile(row, AuraIcons.Shapes, "Object", "3D shape", () => Push(BuildShapeChooser()));
            CentreCard(card);
            return v;
        }

        private View BuildNoteType()
        {
            var v = NewView("play.notetype", Mode.Play, 0.3f);
            var card = AuraKit.Card(v.root, "Note type", "Pick how you want to write it", out var body);
            var row = AuraKit.EqualRow(body, 340f);
            AuraKit.Tile(row, AuraIcons.Plain, "Plain", "Free text", () => Push(BuildNoteEditor(null, false)));
            AuraKit.Tile(row, AuraIcons.Checklist, "Checklist", "Tick-off items", () => Push(BuildNoteEditor(null, true)));
            CentreCard(card);
            return v;
        }

        private View BuildShapeChooser()
        {
            var v = NewView("play.shapes", Mode.Play, 0.3f);
            var card = AuraKit.Card(v.root, "Pick a shape", "It appears where you tap", out var body);
            for (int r = 0; r < 2; r++)
            {
                var row = AuraKit.EqualRow(body, 290f);
                for (int c = 0; c < 3; c++)
                {
                    int i = r * 3 + c;
                    if (i < ShapeNames.Length)
                    {
                        string shape = ShapeNames[i];
                        AuraKit.Tile(row, ShapeIcon(shape), shape, null, () => Push(BuildObjectEditor(null, shape)));
                    }
                    else
                    {
                        AuraKit.Cell(AuraKit.Rect(row, "Spacer")); // keeps the last row's tiles the same size
                    }
                }
            }
            CentreCard(card);
            return v;
        }

        private static string ShapeIcon(string shape)
        {
            switch (shape)
            {
                case "Sphere":   return AuraIcons.Sphere;
                case "Cylinder": return AuraIcons.Cylinder;
                case "Capsule":  return AuraIcons.Capsule;
                case "Plane":    return AuraIcons.Plane;
                default:         return AuraIcons.Cube;
            }
        }

        // ── Note editor ────────────────────────────────────────────────

        /// <param name="editing">An already-placed note to change, or null for a new one.</param>
        private View BuildNoteEditor(NoteItemData editing, bool checklist)
        {
            bool isEdit = editing != null;
            bool isChecklist = isEdit ? editing.noteType == NoteType.Checklist : checklist;
            bool sticky = !isEdit || editing.noteStyle == NoteStyle.StickyNote;
            int bg = isEdit ? PaletteIndex(editing.colorHex, 0) : 0;
            int bd = isEdit ? PaletteIndex(editing.borderColorHex, 8) : 8;
            bool priv = isEdit && editing.isPrivate;
            string text = isEdit ? (editing.text ?? "") : "";

            var v = NewView("play.note", Mode.Play, 0.3f);
            v.primary = isEdit ? "Save" : "Done";
            v.primaryIcon = AuraIcons.Ok;

            string title = isEdit ? (isChecklist ? "Edit checklist" : "Edit note")
                                  : (isChecklist ? "New checklist" : "New note");
            var body = Sheet(v, title);

            // Live preview of the note's colours and words.
            var previewHolder = AuraKit.Rect(body, "PreviewHolder");
            AuraKit.Size(previewHolder, -1, 300f);
            var card = AuraKit.Box(previewHolder, "Preview", Color.white, 22f);
            AuraKit.Pin(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(560, 270));
            card.rectTransform.localRotation = Quaternion.Euler(0, 0, -2f);
            var cardBorder = AuraKit.Border(card, Color.black, 22f);
            var cardText = AuraKit.Text(card.transform, "", AuraTheme.SizeSmall, Color.black);
            cardText.alignment = TextAlignmentOptions.TopLeft;
            AuraKit.Fill(cardText.rectTransform, 30, 22, 30, 22);

            var fields = new List<TMP_InputField>();
            TMP_InputField noteInput = null;

            void Repaint()
            {
                Color b = AuraWidgets.Palette[bg];
                card.color = b;
                AuraSprites.SetRadius(card, sticky ? 22f : 4f);
                AuraSprites.SetRadius(cardBorder, sticky ? 22f : 4f);
                cardBorder.color = AuraWidgets.Palette[bd];
                float lum = 0.299f * b.r + 0.587f * b.g + 0.114f * b.b;
                cardText.color = lum > 0.5f ? new Color(0.12f, 0.11f, 0.08f) : new Color(0.95f, 0.95f, 0.97f);

                if (isChecklist)
                {
                    var lines = new List<string>();
                    foreach (var f in fields)
                        if (f != null && !string.IsNullOrWhiteSpace(f.text)) lines.Add("•  " + f.text.Trim());
                    cardText.text = lines.Count > 0 ? string.Join("\n", lines) : "•  Your first item";
                }
                else
                {
                    cardText.text = string.IsNullOrWhiteSpace(text) ? "Your note appears here" : text;
                }
            }

            if (!isChecklist)
            {
                noteInput = AuraWidgets.Input(body, "Write your note…", text, true, 230f, 300);
                noteInput.onValueChanged.AddListener(s => { text = s; Repaint(); });
            }
            else
            {
                var list = AuraKit.Rect(body, "Items");
                AuraKit.VStack(list, 16f);
                var add = AuraKit.Pill(body, "Add item", AuraIcons.Plus, AuraTheme.Ink, AuraTheme.Lavender, null,
                                       126f, AuraTheme.Slate);

                void AddRow(string value)
                {
                    if (fields.Count >= 12) return;
                    var row = AuraKit.Rect(list, "Item");
                    AuraKit.HStack(row, 16f).childForceExpandHeight = true;
                    AuraKit.Size(row, -1, 126f);
                    var f = AuraWidgets.Input(row, "Item " + (fields.Count + 1), value, false, 126f, 80);
                    AuraKit.Flex(f);
                    f.onValueChanged.AddListener(_ => Repaint());
                    fields.Add(f);

                    var del = AuraKit.Box(row, "Remove", AuraTheme.Ink, AuraTheme.Radius - 10f, true);
                    AuraKit.Border(del, AuraTheme.Slate, AuraTheme.Radius - 10f);
                    AuraKit.Size(del, 126f, 126f);
                    var di = AuraKit.Icon(del.transform, AuraIcons.Trash, 54f, AuraTheme.TextDim);
                    AuraKit.Pin(di.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                                new Vector2(54, 54));
                    AuraKit.MakeButton(del.gameObject, del, () =>
                    {
                        if (fields.Count <= 1) { f.text = ""; return; }
                        fields.Remove(f);
                        Destroy(row.gameObject);
                        Repaint();
                    });
                }

                if (isEdit && editing.checklistItems != null && editing.checklistItems.Count > 0)
                    foreach (var item in editing.checklistItems) AddRow(item != null ? item.text : "");
                else
                    AddRow("");

                add.onClick.AddListener(() => { AddRow(""); Repaint(); });
            }

            AuraKit.Caps(body, "Style");
            AuraWidgets.Segmented(body, new[] { "Sticky", "Paper" }, sticky ? 0 : 1, i => { sticky = i == 0; Repaint(); });
            AuraKit.Caps(body, "Background");
            AuraWidgets.Swatches(body, AuraWidgets.Palette, bg, i => { bg = i; Repaint(); });
            AuraKit.Caps(body, "Border");
            AuraWidgets.Swatches(body, AuraWidgets.Palette, bd, i => { bd = i; Repaint(); });
            AuraKit.Caps(body, "Who can see it");
            AuraWidgets.Segmented(body, new[] { "Only me", "Share with room" }, priv ? 0 : 1, i => priv = i == 0);

            Repaint();

            v.onPrimary = () =>
            {
                var items = new List<ChecklistItem>();
                if (isChecklist)
                {
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (fields[i] == null || string.IsNullOrWhiteSpace(fields[i].text)) continue;
                        bool was = isEdit && editing.checklistItems != null && i < editing.checklistItems.Count &&
                                   editing.checklistItems[i] != null && editing.checklistItems[i].isChecked;
                        items.Add(new ChecklistItem(fields[i].text.Trim(), was));
                    }
                }

                if (isEdit)
                {
                    editing.text = text;
                    editing.noteType = isChecklist ? NoteType.Checklist : NoteType.PlainText;
                    editing.noteStyle = sticky ? NoteStyle.StickyNote : NoteStyle.Paper;
                    editing.colorHex = Hex(AuraWidgets.Palette[bg]);
                    editing.borderColorHex = Hex(AuraWidgets.Palette[bd]);
                    editing.isPrivate = priv;
                    if (isChecklist) editing.checklistItems = items;

                    var go = ARPlacementController.Instance != null ? ARPlacementController.Instance.GetActiveItem(editing.id) : null;
                    var ctrl = go != null ? go.GetComponent<Notes.NoteController>() : null;
                    if (ctrl != null) ctrl.UpdateData(editing);
                    else PersistenceManager.Instance?.UpdateNote(editing);
                    Pop();
                    return;
                }

                var config = new NoteItemData
                {
                    id = Guid.NewGuid().ToString(),
                    noteType = isChecklist ? NoteType.Checklist : NoteType.PlainText,
                    noteStyle = sticky ? NoteStyle.StickyNote : NoteStyle.Paper,
                    text = text,
                    colorHex = Hex(AuraWidgets.Palette[bg]),
                    borderColorHex = Hex(AuraWidgets.Palette[bd]),
                    isPrivate = priv,
                    checklistItems = items,
                };
                BeginPlacing(config.id, isChecklist ? "checklist" : "note",
                             () => ARPlacementController.Instance?.SetPendingNote(config), AppMode.PlacingNote);
            };
            return v;
        }

        // ── Object editor ──────────────────────────────────────────────

        private View BuildObjectEditor(ObjectItemData editing, string shape)
        {
            bool isEdit = editing != null;
            if (isEdit && !string.IsNullOrEmpty(editing.prefabName)) shape = editing.prefabName;
            int col = isEdit ? PaletteIndex(editing.colorHex, 4) : 4;
            float opacity = isEdit ? Mathf.Clamp01(editing.opacity) : 1f;
            bool priv = isEdit && editing.isPrivate;

            var v = NewView("play.object", Mode.Play, 0.3f);
            v.primary = isEdit ? "Save" : "Place";
            v.primaryIcon = AuraIcons.Ok;
            var body = Sheet(v, isEdit ? "Edit " + shape.ToLowerInvariant() : shape);

            var holder = AuraKit.Rect(body, "PreviewHolder");
            AuraKit.Size(holder, -1, 220f);
            var preview = AuraKit.Icon(holder, ShapeIcon(shape), 190f, Color.white);
            preview.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(preview.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(190, 190));

            TextMeshProUGUI opacityValue = null;

            void Repaint()
            {
                Color c = AuraWidgets.Palette[col];
                c.a = Mathf.Lerp(0.25f, 1f, opacity); // stays visible on the panel even when nearly clear
                preview.color = c;
                if (opacityValue != null) opacityValue.text = Mathf.RoundToInt(opacity * 100f) + "%";
            }

            AuraKit.Caps(body, "Colour");
            AuraWidgets.Swatches(body, AuraWidgets.Palette, col, i => { col = i; Repaint(); });

            var opRow = AuraKit.Rect(body, "OpacityRow");
            AuraKit.HStack(opRow, 10f);
            AuraKit.Size(opRow, -1, 44f);
            AuraKit.Flex(AuraKit.Caps(opRow, "Opacity"));
            opacityValue = AuraKit.Text(opRow, "", AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.SemiBold,
                                        TextAlignmentOptions.MidlineRight);
            AuraKit.Size(opacityValue, 160f);
            AuraWidgets.Slider(body, opacity, x => { opacity = x; Repaint(); });

            AuraKit.Caps(body, "Who can see it");
            AuraWidgets.Segmented(body, new[] { "Only me", "Share with room" }, priv ? 0 : 1, i => priv = i == 0);

            Repaint();

            v.onPrimary = () =>
            {
                string hex = Hex(AuraWidgets.Palette[col]);
                if (isEdit)
                {
                    editing.colorHex = hex;
                    editing.opacity = opacity;
                    editing.isPrivate = priv;
                    var go = ARPlacementController.Instance != null ? ARPlacementController.Instance.GetActiveItem(editing.id) : null;
                    var ctrl = go != null ? go.GetComponent<Objects.ObjectController>() : null;
                    if (ctrl != null) ctrl.UpdateData(editing);
                    else PersistenceManager.Instance?.UpdateObject(editing);
                    Pop();
                    return;
                }

                var config = new ObjectItemData
                {
                    id = Guid.NewGuid().ToString(),
                    prefabName = shape,
                    colorHex = hex,
                    opacity = opacity,
                    isPrivate = priv,
                };
                config.SetScale(Vector3.one);
                BeginPlacing(config.id, shape.ToLowerInvariant(),
                             () => ARPlacementController.Instance?.SetPendingObject(config), AppMode.PlacingObject);
            };
            return v;
        }

        // ── Placing ────────────────────────────────────────────────────

        private void BeginPlacing(string itemId, string what, Action setPending, AppMode mode)
        {
            if (ARPlacementController.Instance == null || AppStateManager.Instance == null)
            {
                Toast.Show("Placing isn't ready yet. Give the camera a moment, then try again.");
                return;
            }
            setPending();
            Push(BuildPlacing(itemId, what));
            AppStateManager.Instance.SetMode(mode);
        }

        /// <summary>
        /// The whole camera view is free; one tap places the item. Back returns to the editor with
        /// its settings kept. A double tap (the old safety valve) also cancels back to the editor.
        /// </summary>
        private View BuildPlacing(string itemId, string what)
        {
            var v = NewView("play.placing", Mode.Play, 0f, underChip: true);
            v.blockWorld = false;
            HintPill(v, $"Tap a surface to place your {Lav(what)}");

            bool leaving = false;
            Action<AppMode> onMode = m =>
            {
                if (leaving || m != AppMode.Idle || Top != v) return;
                leaving = true;
                bool placed = ARPlacementController.Instance != null &&
                              ARPlacementController.Instance.GetActiveItem(itemId) != null;
                if (placed) ShowPlayIdle(true);
                else Pop();
            };

            v.onShow = () => { if (AppStateManager.Instance != null) AppStateManager.Instance.OnModeChanged += onMode; };
            v.onHide = () => { if (AppStateManager.Instance != null) AppStateManager.Instance.OnModeChanged -= onMode; };
            v.onBack = () =>
            {
                leaving = true;
                AppStateManager.Instance?.ReturnToIdle();
                Pop();
            };
            v.onChip = () =>
            {
                leaving = true;
                AppStateManager.Instance?.ReturnToIdle();
                ShowPlayIdle(false);
                Push(BuildPlayChooser());
            };
            return v;
        }

        /// <summary>Clears the Play stack down to the launch screen and shows the clear camera view.</summary>
        private void ShowPlayIdle(bool justPlaced)
        {
            while (_stack.Count > 1)
            {
                var top = Top;
                _stack.RemoveAt(_stack.Count - 1);
                Close(top);
            }
            _home.root.gameObject.SetActive(false);
            Push(BuildPlayIdle(justPlaced));
        }

        private View BuildPlayIdle(bool justPlaced)
        {
            var v = NewView("play.idle", Mode.Play, 0f, underChip: true);
            v.blockWorld = false;
            var hint = HintPill(v, justPlaced
                ? $"Placed. {Lav("Double-tap")} an item to edit it, or tap the chip to add more."
                : $"Tap the {Lav("chip")} to add a note or a shape.");
            var hintGroup = hint.transform.parent.gameObject.AddComponent<CanvasGroup>();
            hintGroup.blocksRaycasts = false;
            StartCoroutine(FadeAfter(hintGroup, 5f));

            v.onChip = () => Push(BuildPlayChooser());
            v.onBack = GoHome;
            return v;
        }

        private static IEnumerator FadeAfter(CanvasGroup g, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            yield return FadeGroup(g, 0f, 0.6f);
        }

        /// <summary>Opens the editor for an item placed earlier (from its Edit choice).</summary>
        public void EditPlacedItem(PlacedItemData data, GameObject go)
        {
            if (_animating || data == null) return;
            if (Top != null && Top.id == "play.placing") return;

            if (Top != null && Top.isHome) _home.root.gameObject.SetActive(false);
            if (data is NoteItemData note) Push(BuildNoteEditor(note, note.noteType == NoteType.Checklist));
            else if (data is ObjectItemData obj) Push(BuildObjectEditor(obj, obj.prefabName));
        }

        // ── Shared bits ────────────────────────────────────────────────

        /// <summary>
        /// A bottom sheet above the slot bar: title, then a scrolling body. It is as tall as its
        /// content, but never taller than the space it has, so small screens scroll instead of
        /// pushing anything off screen.
        /// </summary>
        private RectTransform Sheet(View v, string title)
        {
            var sheet = AuraKit.Box(v.root, "Sheet", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(sheet, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var srt = sheet.rectTransform;
            srt.anchorMin = new Vector2(0.5f, 0); srt.anchorMax = new Vector2(0.5f, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(1000, 600);
            sheet.gameObject.AddComponent<MatchParentWidth>().max = 1100f;

            var grab = AuraKit.Box(srt, "Grab", AuraTheme.Slate, 4f);
            AuraKit.Pin(grab.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22),
                        new Vector2(90, 9));

            var t = AuraKit.Text(srt, title, AuraTheme.SizeTitle - 4f, AuraTheme.Text, AuraWeight.Bold,
                                 TextAlignmentOptions.Center);
            t.enableWordWrapping = false;
            AuraKit.Pin(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -48),
                        new Vector2(900, 70));

            var host = AuraKit.Rect(srt, "ScrollHost");
            AuraKit.Fill(host, 40, 34, 40, SheetHeader);
            var content = AuraWidgets.Scroll(host, out _, 22f);

            var fitter = sheet.gameObject.AddComponent<SheetFitter>();
            fitter.content = content;
            fitter.chrome = SheetHeader + 34f;
            return content;
        }

        private const float SheetHeader = 140f;

        private static int PaletteIndex(string hex, int fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? AuraWidgets.NearestPaletteIndex(c) : fallback;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }

    /// <summary>Sizes a sheet to its content, capped at the height of the space it sits in.</summary>
    public class SheetFitter : MonoBehaviour
    {
        public RectTransform content;
        public float chrome;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (content == null || parent == null) return;
            float want = content.rect.height + chrome;
            float h = Mathf.Min(want, parent.rect.height);
            if (Mathf.Abs(rt.sizeDelta.y - h) > 0.5f) rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
        }
    }
}
