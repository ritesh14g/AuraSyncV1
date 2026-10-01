using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// UITheme — Central design token constants matching the Figma spec.
    /// Use these values when building your Unity scene hierarchy by hand.
    ///
    /// COMPLETE SCENE SETUP GUIDE
    /// ══════════════════════════
    ///
    /// 1. CANVAS
    ///    • Render Mode: Screen Space – Overlay
    ///    • Sort Order: 10
    ///    • Canvas Scaler: Scale with Screen Size, 1080x1920, Match 0.5
    ///
    /// 2. FAB BUTTON  (child of Canvas)
    ///    • Anchor: bottom-center
    ///    • Pivot: 0.5, 0
    ///    • Pos: (0, 56, 0)   Size: (72, 72)
    ///    • Image: circle sprite, Color #6290FF
    ///    • Child TextMeshPro: "+"  Size 36  Bold  Color white
    ///    • Attach: MainMenuUI.fabButton
    ///
    /// 3. BOTTOM SHEETS  (children of Canvas, one per panel)
    ///    All share the same pattern:
    ///    • Anchor: bottom-center, Pivot: 0.5, 0
    ///    • Width: 1080  Height: varies (320–560)
    ///    • Image: rounded-corner sprite  Color #0D0D14F5
    ///    • Top-left corner: radius 24, top-right: 24, bottom: 0
    ///
    ///    HANDLE (child of each sheet):
    ///    • Anchor: top-center  Pos: (0, -12)  Size: (40, 4)
    ///    • Image: Color #FFFFFF33
    ///
    /// 4. COLOUR SWATCHES
    ///    • Size: 44x44  circle sprite
    ///    • Outline component added at runtime by script (white, 2px)
    ///
    /// 5. PILL TOGGLE BUTTONS (Note style toggle)
    ///    • Size: 200x44  rounded 22px radius
    ///    • Active bg:   #6290FF   Active text:   white
    ///    • Inactive bg: #FFFFFF14  Inactive text: #FFFFFF8C
    ///
    /// 6. SHAPE GRID CARDS (ObjectMenuUI)
    ///    • GridLayoutGroup: 2 columns, cell 220x200, spacing (16,16)
    ///    • Card image: #FFFFFF0A  rounded 16px
    ///    • Label TMP: 14pt  #FFFFFFCC
    ///
    /// 7. FOOTER BUTTONS
    ///    Cancel (ghost): transparent bg, border #FFFFFF22, text #FFFFFF99
    ///    Place / Done (filled): bg #6290FF, text white
    ///    Height: 52  BorderRadius: 14
    ///
    /// 8. SELECTION TOOLBAR
    ///    • Anchor: top-center of its parent Canvas
    ///    • Size: 260x56  rounded pill (28px)
    ///    • Image: #0D0D14CC  (dark glass)
    ///    • Buttons: 72px wide each, icon TMP 20pt + label TMP 11pt
    ///
    /// 9. FONTS
    ///    Import "Inter" or "DM Sans" from Google Fonts as a TMP font asset.
    ///    Assign to every TextMeshPro component.
    ///    Weights used: Regular (400), Medium (500), SemiBold (600)
    ///
    /// 10. SHADOWS / DEPTH
    ///    Add a Shadow component to each BottomSheet Image:
    ///    effectColor #00000066, effectDistance (0, -8)
    /// </summary>
    public static class UITheme
    {
        // ── Background ────────────────────────────────────────────────
        public static readonly Color BgDeep      = new Color(0.047f, 0.047f, 0.071f, 1.00f); // #0C0C12
        public static readonly Color BgPanel     = new Color(0.051f, 0.051f, 0.078f, 0.96f); // #0D0D14F5
        public static readonly Color BgCard      = new Color(1.000f, 1.000f, 1.000f, 0.06f); // #FFFFFF0F
        public static readonly Color BgCardHover = new Color(1.000f, 1.000f, 1.000f, 0.10f); // #FFFFFF1A

        // ── Accent ────────────────────────────────────────────────────
        public static readonly Color Accent      = new Color(0.384f, 0.561f, 1.000f, 1.00f); // #6290FF
        public static readonly Color AccentDim   = new Color(0.384f, 0.561f, 1.000f, 0.25f); // #6290FF40
        public static readonly Color AccentGlow  = new Color(0.384f, 0.561f, 1.000f, 0.15f); // #6290FF26

        // ── Text ──────────────────────────────────────────────────────
        public static readonly Color TextPrimary   = new Color(1f, 1f, 1f, 1.00f); // #FFFFFF
        public static readonly Color TextSecondary = new Color(1f, 1f, 1f, 0.55f); // #FFFFFF8C
        public static readonly Color TextTertiary  = new Color(1f, 1f, 1f, 0.30f); // #FFFFFF4D

        // ── Dividers ──────────────────────────────────────────────────
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f); // #FFFFFF14

        // ── Danger ────────────────────────────────────────────────────
        public static readonly Color Danger = new Color(1.00f, 0.30f, 0.30f, 1.00f); // #FF4D4D

        // ── Backdrop ──────────────────────────────────────────────────
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.65f);

        // ── Font sizes (points) ───────────────────────────────────────
        public const float FontTitle    = 20f;
        public const float FontBody     = 15f;
        public const float FontLabel    = 13f;
        public const float FontCaption  = 11f;

        // ── Spacing ───────────────────────────────────────────────────
        public const float RadiusSheet   = 24f;
        public const float RadiusPill    = 22f;
        public const float RadiusCard    = 16f;
        public const float RadiusButton  = 14f;
        public const float SwatchSize    = 44f;
        public const float FABSize       = 72f;
        public const float ToolbarHeight = 56f;

        // ── Helper: Apply standard TMP style ──────────────────────────
        public static void ApplyTitle(TextMeshProUGUI tmp)
        {
            tmp.fontSize   = FontTitle;
            tmp.fontStyle  = FontStyles.Bold;
            tmp.color      = TextPrimary;
            tmp.alignment  = TextAlignmentOptions.Center;
        }

        public static void ApplyBody(TextMeshProUGUI tmp)
        {
            tmp.fontSize   = FontBody;
            tmp.fontStyle  = FontStyles.Normal;
            tmp.color      = TextSecondary;
        }

        public static void ApplyLabel(TextMeshProUGUI tmp)
        {
            tmp.fontSize   = FontLabel;
            tmp.fontStyle  = FontStyles.Normal;
            tmp.color      = TextTertiary;
        }

        // ── Helper: style a filled primary button ─────────────────────
        public static void StyleFilledButton(Button btn, string label)
        {
            var img = btn.GetComponent<Image>();
            if (img) img.color = Accent;

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp)
            {
                tmp.text      = label;
                tmp.fontSize  = FontBody;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color     = Color.white;
            }

            var colors = btn.colors;
            colors.normalColor      = Accent;
            colors.highlightedColor = new Color(Accent.r + 0.1f, Accent.g + 0.1f, Accent.b + 0.1f);
            colors.pressedColor     = new Color(Accent.r - 0.1f, Accent.g - 0.1f, Accent.b - 0.1f);
            btn.colors = colors;
        }

        // ── Helper: style a ghost (outline) button ────────────────────
        public static void StyleGhostButton(Button btn, string label)
        {
            var img = btn.GetComponent<Image>();
            if (img) img.color = new Color(1f, 1f, 1f, 0.06f);

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp)
            {
                tmp.text      = label;
                tmp.fontSize  = FontBody;
                tmp.color     = TextSecondary;
            }
        }
    }
}