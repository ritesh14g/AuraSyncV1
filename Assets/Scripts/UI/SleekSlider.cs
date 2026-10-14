using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// A slim slider built in code: a thin rounded track, a bright fill up to a round knob, the
    /// current value in a small bubble above the knob, and the minimum and maximum at either end.
    ///
    ///     -100  ━━━━━━━━━━━━━━━━●──────────────  100
    ///                         [ 25 ]
    ///
    /// The rounded shapes come from one anti-aliased circle drawn into a texture at runtime and
    /// 9-sliced, so nothing depends on Unity's built-in UI sprites (those are Editor-only to load
    /// from code). Added 2026-10-13 for the section toolbar, after the first version's fill rect
    /// kept its default 100 x 100 size and spread over the labels.
    /// </summary>
    public static class SleekSlider
    {
        public const float RowHeight = 70f;

        private const float TrackHeight = 10f;
        private const float KnobSize    = 32f;
        private const float TrackY      = 0.30f; // track and knob sit low; the bubble needs the room above

        public static readonly Color TrackColor  = new Color(0.78f, 0.88f, 1f, 0.22f);
        public static readonly Color FillColor   = new Color(0.00f, 0.60f, 1f, 1f);
        public static readonly Color BubbleColor = new Color(0.19f, 0.14f, 0.65f, 1f);
        public static readonly Color EndColor    = new Color(0.62f, 0.72f, 1f, 0.85f);

        private static Sprite _round;

        /// <summary>A white anti-aliased circle, 9-sliced so it also makes pills of any length.</summary>
        public static Sprite Round
        {
            get
            {
                if (_round != null) return _round;
                const int size = 64;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                float r = size * 0.5f;
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                        byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                        px[y * size + x] = new Color32(255, 255, 255, a);
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                float b = r - 1f;
                _round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                       SpriteMeshType.FullRect, new Vector4(b, b, b, b));
                _round.name = "AuraSync_Round";
                return _round;
            }
        }

        /// <summary>
        /// Builds the slider (with its end labels) inside <paramref name="parent"/>, which should be
        /// a horizontal layout row. <paramref name="bubble"/> is the value text; set it when the
        /// value changes. The returned slider's GameObject is where pointer-up relays belong.
        /// </summary>
        public static Slider Create(Transform parent, float min, float max, string minText, string maxText,
                                    out TextMeshProUGUI bubble)
        {
            var group = new GameObject("SleekSlider", typeof(RectTransform));
            group.transform.SetParent(parent, false);
            group.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var hg = group.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 10;
            hg.childAlignment = TextAnchor.LowerCenter;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;

            EndLabel(group.transform, minText, TextAlignmentOptions.BottomRight);

            var go = new GameObject("Slider", typeof(RectTransform));
            go.transform.SetParent(group.transform, false);
            go.SetActive(false); // configure before the Slider's OnEnable runs
            go.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // Invisible, full-height hit area: the thin track alone would be hard to grab.
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);

            float r = KnobSize * 0.5f;

            // Track.
            Image track = Shape(go.transform, "Track", TrackColor, sliced: true);
            Band(track.rectTransform, 0f, 0f);

            // Fill: inset like the knob's travel, then stretched back to the track's left end, so it
            // starts at the end of the track and stops exactly under the knob's centre.
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            Band((RectTransform)fillArea.transform, r, -r);
            Image fill = Shape(fillArea.transform, "Fill", FillColor, sliced: true);
            var frt = fill.rectTransform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(0f, 1f);
            frt.offsetMin = new Vector2(-r, 0f);
            frt.offsetMax = Vector2.zero;

            // Knob travel area, inset by the knob's radius so it never hangs off the ends.
            var slide = new GameObject("Handle Slide Area", typeof(RectTransform));
            slide.transform.SetParent(go.transform, false);
            var srt = (RectTransform)slide.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(r, 0f); srt.offsetMax = new Vector2(-r, 0f);

            Image knob = Shape(slide.transform, "Knob", FillColor, sliced: false);
            var krt = knob.rectTransform;
            krt.anchorMin = krt.anchorMax = new Vector2(0f, TrackY);
            krt.sizeDelta = new Vector2(KnobSize, KnobSize);

            // Value bubble, riding above the knob.
            Image bubbleBg = Shape(knob.transform, "Bubble", BubbleColor, sliced: true);
            bubbleBg.pixelsPerUnitMultiplier = 4f; // small corners: a label, not a pill
            var brt = bubbleBg.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 1f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 4f);
            var bhg = bubbleBg.gameObject.AddComponent<HorizontalLayoutGroup>();
            bhg.padding = new RectOffset(10, 10, 2, 2);
            bhg.childControlWidth = bhg.childControlHeight = true;
            var bfit = bubbleBg.gameObject.AddComponent<ContentSizeFitter>();
            bfit.horizontalFit = bfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Value", typeof(RectTransform));
            textGo.transform.SetParent(bubbleBg.transform, false);
            bubble = textGo.AddComponent<TextMeshProUGUI>();
            bubble.fontSize = 18f;
            bubble.fontStyle = FontStyles.Bold | FontStyles.Italic;
            bubble.color = Color.white;
            bubble.alignment = TextAlignmentOptions.Center;
            bubble.enableWordWrapping = false;
            bubble.raycastTarget = false;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = frt;
            slider.handleRect = krt;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = (min + max) * 0.5f;
            ColorBlock cb = slider.colors;
            cb.highlightedColor = new Color(0.85f, 0.95f, 1f);
            cb.pressedColor = new Color(0.75f, 0.88f, 1f);
            cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            slider.colors = cb;

            EndLabel(group.transform, maxText, TextAlignmentOptions.BottomLeft);

            go.SetActive(true);
            return slider;
        }

        /// <summary>A horizontal strip at the track's height, spanning the slider (with insets).</summary>
        private static void Band(RectTransform rt, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, TrackY);
            rt.anchorMax = new Vector2(1f, TrackY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, TrackHeight);
            rt.offsetMin = new Vector2(left, rt.offsetMin.y);
            rt.offsetMax = new Vector2(right, rt.offsetMax.y);
        }

        private static Image Shape(Transform parent, string name, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = Round;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void EndLabel(Transform parent, string text, TextAlignmentOptions align)
        {
            var go = new GameObject("End", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = 52f;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 18f;
            t.fontStyle = FontStyles.Bold | FontStyles.Italic;
            t.color = EndColor;
            t.alignment = align;
            t.margin = new Vector4(0f, 0f, 0f, RowHeight * TrackY - 12f); // level with the track
            t.enableWordWrapping = false;
            t.raycastTarget = false;
        }
    }
}
