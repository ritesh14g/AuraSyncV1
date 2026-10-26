using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    public enum AuraWeight { Regular, SemiBold, Bold }

    /// <summary>
    /// Builders for the app's shared UI pieces (canvas, boxes, text, icons, buttons, tiles, rows),
    /// all styled from <see cref="AuraTheme"/>. Panels are assembled from these so every screen
    /// gets the same corners, sizes and colours without repeating the numbers.
    /// </summary>
    public static class AuraKit
    {
        // ── Canvas ─────────────────────────────────────────────────────

        /// <summary>
        /// A screen-space canvas on the 1080 x 1920 reference in Expand mode: the layout always has
        /// at least that much room, so nothing designed for it can be pushed off a small or unusual
        /// screen, and taller or wider screens simply give the panels more space.
        /// </summary>
        public static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>The scale the canvas above will use on this screen (canvas units → pixels).</summary>
        public static float CanvasScale => Mathf.Max(0.01f, Mathf.Min(Screen.width / 1080f, Screen.height / 1920f));

        /// <summary>One physical centimetre in canvas units, or a fallback when the phone reports no DPI.</summary>
        public static float Centimetre
        {
            get
            {
                float dpi = Screen.dpi;
                if (dpi < 50f || dpi > 1200f) return 1080f * 0.09f;
                return dpi / 2.54f / CanvasScale;
            }
        }

        // ── Rect helpers ───────────────────────────────────────────────

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Stretches to the parent with insets (left, bottom, right, top).</summary>
        public static RectTransform Fill(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// <summary>Anchors to one point of the parent with a fixed size.</summary>
        public static RectTransform Pin(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) { le.minWidth = w; le.preferredWidth = w; }
            if (h >= 0) { le.minHeight = h; le.preferredHeight = h; }
            // A fixed size means fixed: left at -1, a layout group would take its children's
            // flexible size instead, and a 100-high row could grow to fill a panel's spare space.
            le.flexibleWidth  = flexW < 0 && w >= 0 ? 0f : flexW;
            le.flexibleHeight = flexH < 0 && h >= 0 ? 0f : flexH;
            return le;
        }

        public static VerticalLayoutGroup VStack(Component c, float spacing, int pad = 0,
                                                 TextAnchor align = TextAnchor.UpperLeft)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childAlignment = align;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(Component c, float spacing, int pad = 0,
                                                   TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childAlignment = align;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        /// <summary>Makes a layout group's object size itself to its content.</summary>
        public static ContentSizeFitter FitHeight(Component c)
        {
            var f = c.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return f;
        }

        // ── Drawing ────────────────────────────────────────────────────

        /// <summary>A rounded box. radius 0 gives a plain rectangle.</summary>
        public static Image Box(Transform parent, string name, Color color, float radius = AuraTheme.Radius,
                                bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            if (radius > 0f)
            {
                img.sprite = AuraSprites.Rounded;
                AuraSprites.SetRadius(img, radius);
            }
            return img;
        }

        /// <summary>Draws a border over a rounded box (a child that ignores layout).</summary>
        public static Image Border(Image box, Color color, float radius = AuraTheme.Radius)
        {
            var ring = Box(box.transform, "Border", color, 0f);
            ring.sprite = AuraSprites.RoundedRing;
            AuraSprites.SetRadius(ring, radius);
            Fill(ring.rectTransform);
            var le = ring.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            return ring;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
                                           AuraWeight weight = AuraWeight.Regular,
                                           TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = weight == AuraWeight.Bold ? AuraTheme.Bold
                   : weight == AuraWeight.SemiBold ? AuraTheme.SemiBold : AuraTheme.Regular;
            if (!AuraTheme.HasAppFont && weight != AuraWeight.Regular) t.fontStyle = FontStyles.Bold;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>Small uppercase label with letter spacing, used above controls.</summary>
        public static TextMeshProUGUI Caps(Transform parent, string text, Color? color = null)
        {
            var t = Text(parent, text.ToUpperInvariant(), AuraTheme.SizeLabel, color ?? AuraTheme.TextDim,
                         AuraWeight.SemiBold);
            t.characterSpacing = 8f;
            t.enableWordWrapping = false;
            return t;
        }

        public static Image Icon(Transform parent, string icon, float size, Color color)
        {
            var rt = Rect(parent, "Icon_" + icon);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = AuraIcons.Get(icon);
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            Size(img, size, size);
            return img;
        }

        // ── Buttons ────────────────────────────────────────────────────

        /// <summary>Turns a graphic into a button with the shared pressed feedback.</summary>
        public static Button MakeButton(GameObject go, Graphic target, Action onClick)
        {
            var b = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            b.targetGraphic = target;
            if (target != null) target.raycastTarget = true;
            var cb = b.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = Color.white;
            cb.selectedColor    = Color.white;
            cb.pressedColor     = new Color(0.78f, 0.78f, 0.86f, 1f);
            cb.disabledColor    = new Color(1f, 1f, 1f, 0.45f);
            cb.fadeDuration     = 0.08f;
            b.colors = cb;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        /// <summary>A filled pill-ish button with an optional icon before the label.</summary>
        public static Button Pill(Transform parent, string label, string icon, Color fill, Color ink,
                                  Action onClick, float height = AuraTheme.SlotHeight, Color? border = null)
        {
            var box = Box(parent, "Button_" + label, fill, AuraTheme.Radius, true);
            if (border.HasValue) Border(box, border.Value);
            var row = HStack(box, 18f, 0, TextAnchor.MiddleCenter);
            row.padding = new RectOffset(36, 36, 0, 0);
            if (!string.IsNullOrEmpty(icon)) Icon(box.transform, icon, 46f, ink);
            var t = Text(box.transform, label, AuraTheme.SizeBody, ink, AuraWeight.SemiBold,
                         TextAlignmentOptions.Center);
            t.enableWordWrapping = false;
            Size(box, -1, height);
            return MakeButton(box.gameObject, box, onClick);
        }

        /// <summary>
        /// A square-ish choice tile: big icon over a name and one line of explanation.
        /// Used by every chooser so they all look and behave the same.
        /// </summary>
        public static Button Tile(Transform parent, string icon, string title, string sub, Action onClick)
        {
            var box = Box(parent, "Tile_" + title, AuraTheme.Ink, AuraTheme.Radius - 6f, true);
            Border(box, AuraTheme.A(AuraTheme.Slate, 0.75f), AuraTheme.Radius - 6f);
            var v = VStack(box, 14f, 0, TextAnchor.MiddleCenter);
            v.padding = new RectOffset(16, 16, 40, 34);
            v.childForceExpandWidth = false;

            Icon(box.transform, icon, 92f, AuraTheme.Lavender);
            var t = Text(box.transform, title, AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold,
                         TextAlignmentOptions.Center);
            t.enableWordWrapping = false;
            if (!string.IsNullOrEmpty(sub))
            {
                var s = Text(box.transform, sub, AuraTheme.SizeCaption, AuraTheme.TextDim, AuraWeight.Regular,
                             TextAlignmentOptions.Center);
                s.enableWordWrapping = false;
            }
            Cell(box);
            return MakeButton(box.gameObject, box, onClick);
        }

        /// <summary>
        /// Makes siblings in a row share the width equally, whatever their text lengths
        /// (preferred width 0, all space handed out by the flexible share).
        /// </summary>
        public static void Cell(Component c)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 1f;
        }

        /// <summary>A full-width row: icon square, title + one line, chevron.</summary>
        public static Button ListRow(Transform parent, string icon, string title, string sub, Action onClick,
                                     out TextMeshProUGUI subText)
        {
            var box = Box(parent, "Row_" + title, AuraTheme.Ink, AuraTheme.Radius - 6f, true);
            Border(box, AuraTheme.A(AuraTheme.Slate, 0.75f), AuraTheme.Radius - 6f);
            var h = HStack(box, 30f, 0, TextAnchor.MiddleLeft);
            h.padding = new RectOffset(28, 28, 26, 26);
            Size(box, -1, 190f);

            var badge = Box(box.transform, "Badge", AuraTheme.A(AuraTheme.Lavender, 0.15f), 28f);
            Size(badge, 120f, 120f);
            var bi = Icon(badge.transform, icon, 62f, AuraTheme.Lavender);
            bi.GetComponent<LayoutElement>().ignoreLayout = true;
            Pin(bi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62, 62));

            var col = Rect(box.transform, "Words");
            var cv = VStack(col, 4f, 0, TextAnchor.MiddleLeft);
            Size(col, -1, -1, 1f);
            var t = Text(col, title, AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            t.enableWordWrapping = false;
            subText = Text(col, sub, AuraTheme.SizeCaption, AuraTheme.TextDim);
            subText.enableWordWrapping = false;

            Icon(box.transform, AuraIcons.Forward, 44f, AuraTheme.Lavender);
            return MakeButton(box.gameObject, box, onClick);
        }

        /// <summary>
        /// The centre card every chooser sits in: Indigo, Slate border, a title and optional
        /// sub-line. Children go in the returned body.
        /// </summary>
        public static RectTransform Card(Transform parent, string title, string sub, out RectTransform body)
        {
            var box = Box(parent, "Card", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 8f, true);
            Border(box, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var v = VStack(box, 26f, 40, TextAnchor.UpperLeft);
            v.padding = new RectOffset(40, 40, 44, 40);
            FitHeight(box);

            var head = Rect(box.transform, "Head");
            VStack(head, 6f);
            Text(head, title, AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold).enableWordWrapping = true;
            if (!string.IsNullOrEmpty(sub)) Text(head, sub, AuraTheme.SizeSmall, AuraTheme.TextDim);

            body = Rect(box.transform, "Body");
            VStack(body, 22f);
            return box.rectTransform;
        }

        /// <summary>A row of equal-width cells (used for 2- and 3-across tile grids).</summary>
        public static RectTransform EqualRow(Transform parent, float height, float spacing = 22f)
        {
            var row = Rect(parent, "Row");
            var h = HStack(row, spacing);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            Size(row, -1, height);
            return row;
        }

        /// <summary>
        /// Removes children at once (detached before destroying), so a rebuilt panel never lays out
        /// the old and new content together for a frame. Children named keep are left.
        /// </summary>
        public static void ClearChildren(Transform t, string keep = null)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in t) if (keep == null || c.name != keep) list.Add(c);
            foreach (var c in list)
            {
                c.SetParent(null, false);
                UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        public static void Flex(Component c, float w = 1f)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = w;
        }
    }
}
