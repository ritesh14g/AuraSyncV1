using UnityEngine;
using UnityEngine.Rendering;

namespace AuraSync.Networking
{
    /// <summary>
    /// Builds simple coloured materials without Shader.Find().
    /// Shader.Find returns null on device builds when a shader was stripped (the same failure that
    /// turned GLB models white), and new Material(null) throws. Cloning the material Unity gives
    /// every primitive is always available because the primitive references it directly.
    /// </summary>
    public static class PointerVisuals
    {
        private static Material _template;

        /// <summary>
        /// Unity's built-in default material. It is always in the build because every primitive
        /// references it, which makes it a safe source of a working shader when Shader.Find is not.
        /// </summary>
        private static Material Template
        {
            get
            {
                if (_template == null)
                {
                    GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _template = probe.GetComponent<Renderer>().sharedMaterial;
                    if (Application.isPlaying) Object.Destroy(probe); else Object.DestroyImmediate(probe);
                }
                return _template;
            }
        }

        /// <summary>
        /// The Standard shader, reached without Shader.Find.
        ///
        /// Worth the indirection: listing Standard in Always Included Shaders to make Shader.Find
        /// work costs every one of its variants at build time, which is tens of thousands and added
        /// the better part of an hour to an Android build. The primitive's material gets us the same
        /// shader for nothing.
        /// </summary>
        public static Shader FallbackShader => Template != null ? Template.shader : null;

        public static Material Create(Color color, float alpha = 1f)
        {
            Material m = new Material(Template);
            color.a = alpha;
            m.color = color;
            if (alpha < 0.99f) MakeTransparent(m);
            return m;
        }

        /// <summary>
        /// An opaque material that stays its own colour whatever the scene lighting does, by driving
        /// the Standard shader's emission rather than hunting for an Unlit shader that a device
        /// build may well have stripped. Used for selection outlines, which are useless if they go
        /// dim in shadow.
        /// </summary>
        public static Material CreateGlowing(Color color)
        {
            Material m = Create(color, 1f);

            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            return m;
        }

        /// <summary>Standard-shader transparent fade mode.</summary>
        private static void MakeTransparent(Material m)
        {
            if (!m.HasProperty("_Mode")) return; // not the Standard shader

            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
