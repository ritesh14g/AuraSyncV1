using UnityEngine;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// Shape sprites drawn in code (no image assets): a rounded rectangle and its outline, both
    /// 9-sliced so one texture gives any size and any corner radius, plus discs and rings.
    /// Edges are anti-aliased from a signed distance, so they stay smooth on any screen.
    /// </summary>
    public static class AuraSprites
    {
        private const int   RectTex     = 128;
        private const float RectRadius  = 48f;   // corner radius inside the texture, in pixels
        private const float RingWidth   = 4.5f;  // outline thickness inside the texture, in pixels

        private static Sprite _rounded, _roundedRing, _disc, _discRing, _thickRing;

        /// <summary>Filled rounded rectangle. Use with <see cref="SetRadius"/>.</summary>
        public static Sprite Rounded     => _rounded     ??= MakeRounded(false);
        /// <summary>Outline of the same rounded rectangle, for borders drawn over a fill.</summary>
        public static Sprite RoundedRing => _roundedRing ??= MakeRounded(true);
        public static Sprite Disc        => _disc        ??= MakeDisc(0f);
        public static Sprite DiscRing    => _discRing    ??= MakeDisc(7f);
        /// <summary>A thick ring for radial progress (Image.Type.Filled, Radial360).</summary>
        public static Sprite ThickRing   => _thickRing   ??= MakeDisc(16f);

        private static Sprite _hfade;
        /// <summary>
        /// A horizontal fade: clear on the left, solid on the right (tint it with Image.color).
        /// The AI panel's background, so the chat reads as see-through towards the model.
        /// </summary>
        public static Sprite HFade => _hfade ??= MakeHFade();

        private static Sprite MakeHFade()
        {
            const int w = 128, h = 4;
            var tex = NewTexture(w, "AuraHFade");
            tex.Reinitialize(w, h);
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float t = x / (w - 1f);
                byte a = (byte)(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.35f)) * 255f);
                for (int y = 0; y < h; y++) px[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>
        /// Gives a sliced rounded image the requested corner radius in canvas units.
        /// The canvas uses 100 reference pixels per unit, like the sprite, so the drawn border is
        /// texture radius / multiplier.
        /// </summary>
        public static void SetRadius(Image img, float radius)
        {
            if (img == null) return;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = RectRadius / Mathf.Max(1f, radius);
        }

        private static Sprite MakeRounded(bool ringOnly)
        {
            var tex = NewTexture(RectTex, ringOnly ? "AuraRoundedRing" : "AuraRounded");
            var px  = new Color32[RectTex * RectTex];
            float half = RectTex / 2f;

            for (int y = 0; y < RectTex; y++)
            for (int x = 0; x < RectTex; x++)
            {
                // Signed distance to a rounded box that fills the texture.
                float qx = Mathf.Abs(x + 0.5f - half) - (half - RectRadius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - RectRadius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - RectRadius;

                float a = Mathf.Clamp01(0.5f - d);
                if (ringOnly) a *= Mathf.Clamp01(d + RingWidth + 0.5f);
                px[y * RectTex + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);

            float b = RectRadius + 2f;
            return Sprite.Create(tex, new Rect(0, 0, RectTex, RectTex), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static Sprite MakeDisc(float ringWidth)
        {
            const int size = 128;
            var tex = NewTexture(size, ringWidth > 0 ? "AuraDiscRing" : "AuraDisc");
            var px  = new Color32[size * size];
            float c = size / 2f, r = size / 2f - 1f;

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = new Vector2(x + 0.5f - c, y + 0.5f - c).magnitude - r;
                float a = Mathf.Clamp01(0.5f - d);
                if (ringWidth > 0f) a *= Mathf.Clamp01(d + ringWidth + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Texture2D NewTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = name,
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags  = HideFlags.DontSave
            };
        }
    }
}
