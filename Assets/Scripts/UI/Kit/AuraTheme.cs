using UnityEngine;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// The one place the app's look is defined: the user's four-tone palette, type sizes, spacing and
    /// corner radius. Every onboarding panel reads from here, and the in-room panels will move onto it
    /// next, so a colour change happens once.
    ///
    /// Units are canvas units on a 1080 x 1920 reference canvas in "Expand" mode (see
    /// <see cref="AuraKit.CreateCanvas"/>): the design always has at least 1080 x 1920 units, and extra
    /// screen becomes extra space on the long side instead of making everything bigger. On a typical
    /// phone one dp is about 2.6 units.
    /// </summary>
    public static class AuraTheme
    {
        // ── Palette (the user's reference) ─────────────────────────────
        public static readonly Color Ink      = Hex(0x0A0F29); // camera scrim, tile insides, inputs
        public static readonly Color Indigo   = Hex(0x1E1A52); // every panel, sheet and Back button
        public static readonly Color Slate    = Hex(0x565E93); // borders, dividers, disabled
        public static readonly Color Lavender = Hex(0x9894C4); // primary button, icons, selection

        public static readonly Color Text     = Hex(0xECEBFA); // lavender-tinted white
        public static readonly Color TextDim  = Hex(0xA9A8CC);
        public static readonly Color Rose     = Hex(0xD46A82); // destructive actions only

        /// <summary>The interactive half of the launch frame: a step lighter than Indigo.</summary>
        public static readonly Color IndigoLift = Color.Lerp(Hex(0x1E1A52), Hex(0x565E93), 0.18f);

        // ── Sizes ──────────────────────────────────────────────────────
        public const float Dp          = 2.625f;
        public const float Gutter      = 42f;   // 16dp: the gap from every screen edge
        public const float Gap         = 26f;   // 10dp: between siblings
        public const float Radius      = 37f;   // 14dp: every corner
        public const float SlotHeight  = 150f;  // 56dp+: Back and the primary action
        public const float ChipSize    = 128f;  // the mode chip, top-right
        public const float MinTouch    = 126f;  // 48dp: nothing tappable is smaller than this

        // ── Type ───────────────────────────────────────────────────────
        public const float SizeDisplay = 66f;
        public const float SizeTitle   = 52f;
        public const float SizeBody    = 39f;
        public const float SizeSmall   = 33f;
        public const float SizeCaption = 29f;
        public const float SizeLabel   = 26f;   // the small uppercase labels

        // ── Motion ─────────────────────────────────────────────────────
        public const float Quick = 0.18f;
        public const float Swoop = 0.32f;

        public static Color A(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        // ── Fonts ──────────────────────────────────────────────────────
        // Plus Jakarta Sans, if the TMP font assets have been made (see ONBOARDING_UI_SETUP.md);
        // otherwise the project's default TMP font, so nothing breaks before the import.

        private static TMP_FontAsset _regular, _semi, _bold;
        private static bool _fontsLoaded;

        public static TMP_FontAsset Regular  { get { LoadFonts(); return _regular; } }
        public static TMP_FontAsset SemiBold { get { LoadFonts(); return _semi; } }
        public static TMP_FontAsset Bold     { get { LoadFonts(); return _bold; } }

        /// <summary>True when the app font was found. Without it, weights fall back to bold styling.</summary>
        public static bool HasAppFont { get { LoadFonts(); return _hasAppFont; } }
        private static bool _hasAppFont;

        private static void LoadFonts()
        {
            if (_fontsLoaded) return;
            _fontsLoaded = true;

            _regular = Resources.Load<TMP_FontAsset>("Fonts/PlusJakartaSans-Regular SDF");
            _semi    = Resources.Load<TMP_FontAsset>("Fonts/PlusJakartaSans-SemiBold SDF");
            _bold    = Resources.Load<TMP_FontAsset>("Fonts/PlusJakartaSans-Bold SDF");
            _hasAppFont = _regular != null;

            TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
            if (_regular == null) _regular = fallback;
            if (_semi    == null) _semi    = _bold != null ? _bold : _regular;
            if (_bold    == null) _bold    = _semi;

            if (!_hasAppFont)
                Debug.Log("[AuraTheme] Plus Jakarta Sans not found in Resources/Fonts - using the default TMP font.");
        }
    }
}
