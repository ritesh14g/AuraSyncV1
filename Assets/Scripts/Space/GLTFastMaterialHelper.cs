using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// Exhaustive Material & Texture Recovery System for GLTFast 6.x on Unity Built-in RP.
    /// Uses Unity Shader Reflection API to discover embedded textures and force-bind them to Unlit/Texture or Standard shaders.
    /// </summary>
    public class GLTFastMaterialHelper : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool debugLogging = true;
        [SerializeField] private bool useUnlitShader = true; // Unlit shader ensures full bright textures regardless of scene lighting

        private static readonly string[] ExtensiveTexturePropNames = {
            "baseColorTexture", "_BaseColorTexture", "_BaseColorMap", "_BaseMap",
            "_MainTex", "mainTexture", "_MainTexture", "_ColorMap", "_Albedo",
            "_AlbedoMap", "_Tex", "_Texture", "_BaseColor", "_Diffuse", "_DiffuseMap",
            "_BaseColorTex", "_BaseColor_Tex"
        };

        private static readonly string[] ColorPropNames = {
            "_BaseColor", "_Color", "_BaseColorFactor", "baseColorFactor", "_ColorFactor"
        };

        public void FixAllMaterials(Transform root)
        {
            if (root == null) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (debugLogging)
                Debug.Log($"[GLTFastMaterialHelper] Starting exhaustive material recovery on {renderers.Length} renderers.");

            int fixedCount = 0;
            int textureCount = 0;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                bool hasVertexColors = CheckHasVertexColors(renderer);
                Material[] mats = renderer.materials;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat == null) continue;

                    bool foundTex = FixSingleMaterial(mat, renderer, hasVertexColors);
                    if (foundTex) textureCount++;
                    fixedCount++;
                }
                renderer.materials = mats;
            }

            if (debugLogging)
                Debug.Log($"[GLTFastMaterialHelper] Recovery complete! Processed {fixedCount} materials. Found textures on {textureCount} materials.");
        }

        private bool FixSingleMaterial(Material mat, Renderer renderer, bool hasVertexColors)
        {
            // 1. Exhaustive Texture Search using Shader Reflection API + Fallbacks
            Texture mainTex = FindTextureExhaustive(mat);

            // 2. Harvest Base Color
            Color baseColor = FindColor(mat);

            if (debugLogging)
            {
                Debug.Log($"[GLTFastMaterialHelper] Renderer '{renderer.name}' | Mat '{mat.name}' | Shader '{mat.shader?.name}' | " +
                          $"HasTex={(mainTex != null)} ({(mainTex != null ? mainTex.name : "None")}) | HasVertexColors={hasVertexColors} | Color={baseColor}");
            }

            // 3. Apply appropriate shader & rebind properties
            if (hasVertexColors)
            {
                // Vertex colors (photogrammetry 3D scan without texture)
                Shader vertexShader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Unlit/Texture");
                if (vertexShader != null) mat.shader = vertexShader;

                if (mainTex != null)
                {
                    mat.SetTexture("_MainTex", mainTex);
                    mat.mainTexture = mainTex;
                }
                mat.SetColor("_Color", baseColor);
                return mainTex != null;
            }
            else if (mainTex != null)
            {
                // Textured 3D Model (e.g. House.glb with 13MB JPEG texture)
                Shader targetShader = null;
                if (useUnlitShader)
                {
                    targetShader = Shader.Find("Unlit/Texture") ?? Shader.Find("Mobile/Unlit (Supports Lightmap)");
                }
                if (targetShader == null)
                {
                    targetShader = Shader.Find("Standard") ?? Shader.Find("Mobile/Diffuse");
                }

                if (targetShader != null) mat.shader = targetShader;

                mat.SetTexture("_MainTex", mainTex);
                mat.mainTexture = mainTex;
                mat.SetColor("_Color", Color.white);
                return true;
            }
            else
            {
                // Untextured colored model
                Shader targetShader = Shader.Find("Standard") ?? Shader.Find("Mobile/Diffuse");
                if (targetShader != null) mat.shader = targetShader;

                mat.SetColor("_Color", baseColor);
                return false;
            }
        }

        private Texture FindTextureExhaustive(Material mat)
        {
            if (mat == null) return null;

            // 1. Direct mainTexture check
            if (mat.mainTexture != null) return mat.mainTexture;

            // 2. Use Unity Shader Reflection API to inspect every texture property
            if (mat.shader != null)
            {
                try
                {
                    int count = mat.shader.GetPropertyCount();
                    for (int i = 0; i < count; i++)
                    {
                        if (mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                        {
                            string propName = mat.shader.GetPropertyName(i);
                            Texture t = mat.GetTexture(propName);
                            if (t != null) return t;
                        }
                    }
                }
                catch { }
            }

            // 3. Exhaustive fallback name list
            foreach (var prop in ExtensiveTexturePropNames)
            {
                if (mat.HasProperty(prop))
                {
                    Texture t = mat.GetTexture(prop);
                    if (t != null) return t;
                }
            }

            return null;
        }

        private Color FindColor(Material mat)
        {
            if (mat == null) return Color.white;

            foreach (var p in ColorPropNames)
            {
                if (mat.HasProperty(p))
                {
                    Color c = mat.GetColor(p);
                    if (c.a > 0.01f) return c;
                }
            }

            return Color.white;
        }

        private bool CheckHasVertexColors(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }
            else if (renderer is SkinnedMeshRenderer smr)
            {
                mesh = smr.sharedMesh;
            }

            if (mesh != null)
            {
                if (mesh.colors != null && mesh.colors.Length > 0) return true;
                if (mesh.colors32 != null && mesh.colors32.Length > 0) return true;
            }

            return false;
        }
    }
}
