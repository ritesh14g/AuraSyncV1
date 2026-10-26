using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>Interactive controls built on <see cref="AuraKit"/>: segmented toggle, colour dots,
    /// slider, text input, scroll area. Each one is self-contained and styled from the theme.</summary>
    public static class AuraWidgets
    {
        // ── Segmented toggle ───────────────────────────────────────────

        /// <summary>Two or more options, one selected. Returns a setter for the selection.</summary>
        public static Action<int> Segmented(Transform parent, string[] labels, int selected, Action<int> onChange)
        {
            var track = AuraKit.Box(parent, "Segmented", AuraTheme.Ink, 30f);
            AuraKit.Size(track, -1, 112f);
            var h = AuraKit.HStack(track, 6f);
            h.padding = new RectOffset(7, 7, 7, 7);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            var fills = new List<Image>();
            var texts = new List<TextMeshProUGUI>();
            int current = selected;

            void Paint()
            {
                for (int i = 0; i < fills.Count; i++)
                {
                    bool on = i == current;
                    fills[i].color = on ? AuraTheme.Lavender : new Color(0, 0, 0, 0);
                    texts[i].color = on ? AuraTheme.Ink : AuraTheme.TextDim;
                }
            }

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var seg = AuraKit.Box(track.transform, "Seg_" + labels[i], new Color(0, 0, 0, 0), 24f, true);
                AuraKit.Cell(seg);
                var t = AuraKit.Text(seg.transform, labels[i], AuraTheme.SizeSmall, AuraTheme.TextDim,
                                     AuraWeight.SemiBold, TextAlignmentOptions.Center);
                t.enableWordWrapping = false;
                AuraKit.Fill(t.rectTransform, 12, 0, 12, 0);
                fills.Add(seg);
                texts.Add(t);
                AuraKit.MakeButton(seg.gameObject, seg, () =>
                {
                    if (current == idx) return;
                    current = idx;
                    Paint();
                    onChange?.Invoke(idx);
                });
            }

            Paint();
            return i => { current = Mathf.Clamp(i, 0, labels.Length - 1); Paint(); };
        }

        // ── Colour dots ────────────────────────────────────────────────

        /// <summary>The note/object palette, shared by both editors.</summary>
        public static readonly Color[] Palette =
        {
            new Color(1.00f, 0.85f, 0.20f), // yellow
            new Color(1.00f, 0.55f, 0.20f), // orange
            new Color(0.95f, 0.27f, 0.27f), // red
            new Color(0.65f, 0.35f, 0.95f), // purple
            new Color(0.35f, 0.60f, 1.00f), // blue
            new Color(0.20f, 0.80f, 0.75f), // teal
            new Color(0.30f, 0.85f, 0.45f), // green
            new Color(0.95f, 0.95f, 0.95f), // white
            new Color(0.15f, 0.15f, 0.20f), // dark
        };

        /// <summary>Index of the palette colour nearest to c (for reopening an item's editor).</summary>
        public static int NearestPaletteIndex(Color c)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                Color p = Palette[i];
                float d = (p.r - c.r) * (p.r - c.r) + (p.g - c.g) * (p.g - c.g) + (p.b - c.b) * (p.b - c.b);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// A row of round colour dots that always fits its width: dots shrink on narrow screens
        /// but never below a comfortable touch size, and the selected one gets a lavender ring.
        /// </summary>
        public static Action<int> Swatches(Transform parent, Color[] colors, int selected, Action<int> onPick)
        {
            var row = AuraKit.Rect(parent, "Swatches");
            var h = AuraKit.HStack(row, 0f);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true; // every dot's touch cell is the row's full height
            h.childAlignment = TextAnchor.MiddleCenter;
            AuraKit.Size(row, -1, 112f);

            var rings = new List<Image>();
            int current = selected;

            void Paint()
            {
                for (int i = 0; i < rings.Count; i++) rings[i].enabled = i == current;
            }

            for (int i = 0; i < colors.Length; i++)
            {
                int idx = i;
                // A touch cell (equal share of the row) with the dot centred in it.
                var cell = AuraKit.Rect(row, "Swatch");
                var hit = cell.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                AuraKit.Cell(hit);

                var ring = AuraKit.Rect(cell, "Ring").gameObject.AddComponent<Image>();
                ring.sprite = AuraSprites.DiscRing;
                ring.color = AuraTheme.Lavender;
                ring.raycastTarget = false;
                ring.preserveAspect = true;
                AuraKit.Pin(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                            new Vector2(98, 98));

                var dot = AuraKit.Rect(cell, "Dot").gameObject.AddComponent<Image>();
                dot.sprite = AuraSprites.Disc;
                dot.color = colors[i];
                dot.raycastTarget = false;
                dot.preserveAspect = true;
                AuraKit.Pin(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                            new Vector2(72, 72));
                var fit = cell.gameObject.AddComponent<FitSquareChildren>();
                fit.items = new[] { ring.rectTransform, dot.rectTransform };
                fit.sizes = new[] { 98f, 72f };

                rings.Add(ring);
                AuraKit.MakeButton(cell.gameObject, hit, () =>
                {
                    current = idx;
                    Paint();
                    onPick?.Invoke(idx);
                });
            }

            Paint();
            return i => { current = i; Paint(); };
        }

        // ── Slider ─────────────────────────────────────────────────────

        /// <summary>
        /// The app's slider: a rounded track, a lavender fill, and a light knob with a soft glow
        /// that grows while it is held. onRelease fires when the finger lifts (for "share this?"
        /// prompts that should wait until the user lets go).
        /// </summary>
        public static Slider Slider(Transform parent, float value, Action<float> onChange, Action onRelease = null,
                                    float min = 0f, float max = 1f, float height = 100f)
        {
            var root = AuraKit.Rect(parent, "Slider");
            AuraKit.Size(root, -1, height);
            root.gameObject.SetActive(false); // configure before the Slider wires itself up

            const float knob = 62f, inset = knob / 2f;

            var track = AuraKit.Box(root, "Track", AuraTheme.A(AuraTheme.Slate, 0.75f), 9f);
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(0, 0.5f); trt.anchorMax = new Vector2(1, 0.5f);
            trt.offsetMin = new Vector2(inset - 6f, -9f); trt.offsetMax = new Vector2(-(inset - 6f), 9f);

            var fillArea = AuraKit.Rect(root, "FillArea");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.offsetMin = new Vector2(inset - 6f, -9f); fillArea.offsetMax = new Vector2(-inset, 9f);
            var fill = AuraKit.Box(fillArea, "Fill", AuraTheme.Lavender, 9f);
            AuraKit.Fill(fill.rectTransform);

            var handleArea = AuraKit.Rect(root, "HandleArea");
            AuraKit.Fill(handleArea, inset, 0, inset, 0);
            var handle = AuraKit.Rect(handleArea, "Handle");
            handle.sizeDelta = new Vector2(knob, 0);

            var halo = AuraKit.Rect(handle, "Halo").gameObject.AddComponent<Image>();
            halo.sprite = AuraSprites.Disc;
            halo.color = AuraTheme.A(AuraTheme.Lavender, 0.28f);
            halo.raycastTarget = false;
            halo.preserveAspect = true;
            AuraKit.Pin(halo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96));

            var dot = AuraKit.Rect(handle, "Knob").gameObject.AddComponent<Image>();
            dot.sprite = AuraSprites.Disc;
            dot.color = AuraTheme.Text;
            dot.preserveAspect = true;
            AuraKit.Pin(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(knob, knob));

            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle;
            s.targetGraphic = dot;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max;
            s.value = value;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            var cb = s.colors; cb.pressedColor = new Color(0.85f, 0.85f, 0.95f); s.colors = cb;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));

            var grip = root.gameObject.AddComponent<SliderGrip>();
            grip.halo = halo.rectTransform;
            grip.onRelease = onRelease;

            root.gameObject.SetActive(true);
            return s;
        }

        // ── Chips and round buttons ────────────────────────────────────

        /// <summary>
        /// A compact pill (speed, Airflow, Explain, Flip...). Returns a painter that shows it on
        /// (lavender outline and text) or off (slate).
        /// </summary>
        public static Action<bool> Chip(Transform parent, string label, string icon, bool on, Action onTap,
                                        out Button button, float height = 104f)
        {
            var b = AuraKit.Pill(parent, label, icon, AuraTheme.Ink, AuraTheme.TextDim, onTap, height, AuraTheme.Slate);
            var h = b.GetComponent<HorizontalLayoutGroup>();
            if (h != null) h.padding = new RectOffset(26, 26, 0, 0);
            var fill = b.GetComponent<Image>();
            var border = b.transform.Find("Border")?.GetComponent<Image>();
            var text = b.GetComponentInChildren<TextMeshProUGUI>();
            Image ic = null;
            foreach (var img in b.GetComponentsInChildren<Image>()) if (img.name.StartsWith("Icon_")) ic = img;
            if (text != null) { text.fontSize = AuraTheme.SizeSmall; text.enableAutoSizing = true; text.fontSizeMin = 18f; text.fontSizeMax = AuraTheme.SizeSmall; }
            button = b;

            void Paint(bool o)
            {
                Color ink = o ? AuraTheme.Lavender : AuraTheme.TextDim;
                fill.color = o ? AuraTheme.A(AuraTheme.Lavender, 0.18f) : AuraTheme.Ink;
                if (border != null) border.color = o ? AuraTheme.Lavender : AuraTheme.Slate;
                if (text != null) text.color = ink;
                if (ic != null) ic.color = ink;
            }
            Paint(on);
            return Paint;
        }

        /// <summary>A round icon button (transport controls). primary = the big lavender one.</summary>
        public static Button Round(Transform parent, string icon, float size, bool primary, Action onTap, out Image iconImage)
        {
            var holder = AuraKit.Rect(parent, "Round_" + icon);
            AuraKit.Size(holder, size, size);
            var disc = holder.gameObject.AddComponent<Image>();
            disc.sprite = AuraSprites.Disc;
            disc.color = primary ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Indigo, 1f);
            disc.preserveAspect = true;
            if (!primary)
            {
                var ring = AuraKit.Rect(holder, "Ring").gameObject.AddComponent<Image>();
                ring.sprite = AuraSprites.DiscRing;
                ring.color = AuraTheme.Slate;
                ring.raycastTarget = false;
                AuraKit.Fill(ring.rectTransform);
            }
            iconImage = AuraKit.Icon(holder, icon, size * 0.42f, primary ? AuraTheme.Ink : AuraTheme.Text);
            iconImage.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(iconImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(size * 0.42f, size * 0.42f));
            return AuraKit.MakeButton(holder.gameObject, disc, onTap);
        }

        // ── Text input ─────────────────────────────────────────────────

        /// <summary>
        /// A rounded text field. Unity's extra text bar above the phone keyboard is hidden, and the
        /// open panel is lifted above the keyboard by <see cref="KeyboardLift"/>.
        /// </summary>
        public static TMP_InputField Input(Transform parent, string placeholder, string value, bool multiline,
                                           float height, int charLimit = 0)
        {
            var box = AuraKit.Box(parent, "Input", AuraTheme.Ink, AuraTheme.Radius - 10f, true);
            var border = AuraKit.Border(box, AuraTheme.Slate, AuraTheme.Radius - 10f);
            AuraKit.Size(box, -1, height);

            var area = AuraKit.Rect(box.transform, "TextArea");
            AuraKit.Fill(area, 30, 18, 30, 18);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = AuraKit.Text(area, placeholder, AuraTheme.SizeBody, AuraTheme.A(AuraTheme.TextDim, 0.7f));
            AuraKit.Fill(ph.rectTransform);
            ph.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            ph.overflowMode = TextOverflowModes.Overflow;
            var txt = AuraKit.Text(area, "", AuraTheme.SizeBody, AuraTheme.Text);
            AuraKit.Fill(txt.rectTransform);
            txt.alignment = ph.alignment;
            txt.overflowMode = TextOverflowModes.Overflow;
            txt.enableWordWrapping = multiline;

            var f = box.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = txt;
            f.placeholder = ph;
            f.targetGraphic = box;
            f.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            f.characterLimit = charLimit;
            f.shouldHideMobileInput = true;
            f.caretColor = AuraTheme.Lavender;
            f.customCaretColor = true;
            f.selectionColor = AuraTheme.A(AuraTheme.Lavender, 0.35f);
            f.pointSize = AuraTheme.SizeBody;
            if (AuraTheme.Regular != null) f.fontAsset = AuraTheme.Regular;
            f.text = value ?? "";
            var nav = f.navigation; nav.mode = Navigation.Mode.None; f.navigation = nav;

            // Lavender border while typing, Slate otherwise.
            f.onSelect.AddListener(_ => border.color = AuraTheme.Lavender);
            f.onDeselect.AddListener(_ => border.color = AuraTheme.Slate);
            return f;
        }

        // ── Scroll area ────────────────────────────────────────────────

        /// <summary>A vertical scroll view filling its parent. Returns the content to add children to.</summary>
        public static RectTransform Scroll(RectTransform parent, out ScrollRect scroll, float spacing = 22f)
        {
            var view = AuraKit.Rect(parent, "Viewport");
            AuraKit.Fill(view);
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            var content = AuraKit.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            AuraKit.VStack(content, spacing);
            AuraKit.FitHeight(content);

            scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            scroll.inertia = true;
            return content;
        }
    }

    /// <summary>Grows a slider knob's glow while it is held, and reports the release.</summary>
    public class SliderGrip : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public RectTransform halo;
        public Action onRelease;

        public void OnPointerDown(PointerEventData e)
        {
            if (halo != null) halo.sizeDelta = new Vector2(118, 118);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (halo != null) halo.sizeDelta = new Vector2(96, 96);
            onRelease?.Invoke();
        }
    }

    /// <summary>Keeps square children (rings, dots) inside a cell that may be narrower than their design size.</summary>
    public class FitSquareChildren : MonoBehaviour
    {
        public RectTransform[] items;
        public float[] sizes;
        private float _lastW = -1f, _lastH = -1f;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            float w = rt.rect.width, h = rt.rect.height;
            if (Mathf.Approximately(w, _lastW) && Mathf.Approximately(h, _lastH)) return;
            _lastW = w; _lastH = h;

            float room = Mathf.Min(w, h) - 4f;
            if (items == null || sizes == null || sizes.Length == 0) return;
            float k = Mathf.Min(1f, room / sizes[0]);
            for (int i = 0; i < items.Length && i < sizes.Length; i++)
                if (items[i] != null) items[i].sizeDelta = Vector2.one * sizes[i] * k;
        }
    }

    /// <summary>
    /// Fires a long press on a button without stealing scrolling from a parent ScrollRect: the press
    /// is cancelled as soon as the list moves. When the long press fires, the button's normal click
    /// for that touch is swallowed.
    /// </summary>
    public class AuraLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action onLongPress;
        public float seconds = 0.5f;

        private bool _down, _fired;
        private float _t0;
        private ScrollRect _scroll;
        private Vector2 _content0;
        private Button _button;

        void Awake()
        {
            _scroll = GetComponentInParent<ScrollRect>();
            _button = GetComponent<Button>();
        }

        public void OnPointerDown(PointerEventData e)
        {
            _down = true; _fired = false; _t0 = Time.unscaledTime;
            if (_scroll != null && _scroll.content != null) _content0 = _scroll.content.anchoredPosition;
            if (_button != null) _button.enabled = true;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _down = false;
            if (_fired && _button != null) StartCoroutine(ReenableNextFrame());
        }

        public void OnPointerExit(PointerEventData e) => _down = false;

        private System.Collections.IEnumerator ReenableNextFrame()
        {
            yield return null;
            if (_button != null) _button.enabled = true;
        }

        void Update()
        {
            if (!_down || _fired) return;
            if (_scroll != null && _scroll.content != null &&
                (_scroll.content.anchoredPosition - _content0).sqrMagnitude > 100f)
            {
                _down = false;
                return;
            }
            if (Time.unscaledTime - _t0 < seconds) return;

            _fired = true;
            if (_button != null) _button.enabled = false; // the release must not also count as a tap
            onLongPress?.Invoke();
        }
    }

    /// <summary>
    /// Lifts a panel above the phone keyboard while one of its text fields is being typed into, by
    /// just enough to keep that field visible. Android does not resize a Unity app for the keyboard,
    /// so without this a field near the bottom would end up hidden behind it.
    /// </summary>
    /// <summary>
    /// For a tall panel laid out top to bottom (the AI chat): while the phone keyboard is up for a
    /// field inside it, the panel's bottom padding grows to the keyboard's height, so the input row
    /// sits just above the keyboard and the list above it shrinks. The panel itself does not move,
    /// so its header stays put and its slide animation is untouched.
    /// </summary>
    public class KeyboardPadding : MonoBehaviour
    {
        public LayoutGroup group;
        public int basePadding;
        public float gap = 20f;
        private float _applied = -1f;
        private readonly Vector3[] _corners = new Vector3[4];

        void LateUpdate()
        {
            if (group == null) return;
            float want = basePadding;

#if !UNITY_EDITOR
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var field = sel != null && sel.transform.IsChildOf(transform) ? sel.GetComponent<TMP_InputField>() : null;
            if (field != null && field.isFocused && TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible)
            {
                float k = AuraKit.CanvasScale;
                float kbPx = TouchScreenKeyboard.area.height;
                if (kbPx <= 1f) kbPx = Screen.height * 0.40f; // some phones report no area; assume a typical keyboard
                ((RectTransform)transform).GetWorldCorners(_corners); // overlay canvas: screen pixels
                float panelBottomPx = _corners[0].y;
                want = Mathf.Max(basePadding, (kbPx - panelBottomPx) / k + gap);
            }
#endif
            if (_applied < 0f) _applied = basePadding;
            float next = Mathf.Lerp(_applied, want, Time.unscaledDeltaTime * 16f);
            if (Mathf.Abs(next - want) < 1f) next = want;
            if (Mathf.Approximately(next, _applied) && group.padding.bottom == Mathf.RoundToInt(next)) return;
            _applied = next;
            group.padding.bottom = Mathf.RoundToInt(next);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }

    public class KeyboardLift : MonoBehaviour
    {
        public RectTransform target;
        private float _applied;
        private readonly Vector3[] _corners = new Vector3[4];

        void LateUpdate()
        {
            if (target == null) return;

            float lift = 0f;
            float k = AuraKit.CanvasScale;
            TMP_InputField field = null;
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (sel != null && sel.transform.IsChildOf(target))
            {
                field = sel.GetComponent<TMP_InputField>();
                if (field != null && !field.isFocused) field = null;
            }

#if !UNITY_EDITOR
            if (field != null && TouchScreenKeyboard.isSupported && TouchScreenKeyboard.visible)
            {
                float kbPx = TouchScreenKeyboard.area.height;
                if (kbPx <= 1f) kbPx = Screen.height * 0.40f; // some phones report no area; assume a typical keyboard

                // Overlay canvas: world space is screen pixels. Remove the lift already applied.
                ((RectTransform)field.transform).GetWorldCorners(_corners);
                float bottomPx = _corners[0].y - _applied * k;
                float neededPx = kbPx + 24f * k - bottomPx;
                lift = Mathf.Clamp(neededPx / k, 0f, kbPx / k);
            }
#endif
            float next = Mathf.Lerp(_applied, lift, Time.unscaledDeltaTime * 14f);
            if (Mathf.Abs(next - lift) < 0.5f) next = lift;
            if (Mathf.Approximately(next, _applied)) return;
            _applied = next;
            target.anchoredPosition = new Vector2(target.anchoredPosition.x, _applied);
        }
    }
}
