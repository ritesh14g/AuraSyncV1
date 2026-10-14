using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// Checks over the materials glTFast built for a loaded model, and repairs only what is
    /// genuinely broken.
    ///
    /// **Why this class no longer rewrites materials.** It used to force every material onto
    /// `Unlit/Texture` or `Standard` and re-bind a single texture it went looking for by name. That
    /// discarded everything else glTFast had already bound - normal, occlusion, emissive and
    /// metallic/roughness maps, vertex colours, alpha mode - and whenever the search came back
    /// empty it painted the material flat white. glTFast's own `glTF/*` shaders carry all of that
    /// on the Built-in pipeline, so for a healthy material the correct action is **nothing at all**.
    /// Unlit also meant no shading at all, which is why models looked flat.
    ///
    /// **What actually broke the textures** was shader stripping, not the materials. glTFast's
    /// Built-in RP shaders live in a package folder with no `Resources` directory and its materials
    /// are made at runtime, so a player build dropped all three. `Shader.Find` then returned null on
    /// the device, glTFast could not bind a single texture, and this class dutifully painted the
    /// result white - which is the white model everyone was looking at. It never reproduced in the
    /// Editor, where every shader resolves. `Editor/GltfShaderInclusion.cs` keeps the shaders in the
    /// build; what is left here is the detection, so the failure can never be silent again.
    /// </summary>
    public class GLTFastMaterialHelper : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Log one line per material. Leave on while chasing a texture problem.")]
        [SerializeField] private bool verboseLogging = false;

        /// <summary>The shader glTFast needs most; its absence is the classic cause of white models.</summary>
        private const string KeyShader = "glTF/PbrMetallicRoughness";

        private const string ErrorShaderName = "Hidden/InternalErrorShader";

        /// <summary>
        /// A short, human-readable verdict on the last model's materials, for showing on screen when
        /// there is no way to read a log (the team cannot reliably get adb onto these phones).
        /// Empty when everything was fine.
        /// </summary>
        public static string LastProblem { get; private set; } = "";

        /// <summary>
        /// True when glTFast's shaders survived into this build. False on a device means the model
        /// will be white and no amount of material patching can bring the textures back - the fix is
        /// Always Included Shaders, see GltfShaderInclusion.
        /// </summary>
        public static bool GltfShadersAvailable => Shader.Find(KeyShader) != null;

        public void FixAllMaterials(Transform root)
        {
            LastProblem = "";
            if (root == null) return;

            // The decisive test, and it costs one lookup: if this shader is missing, glTFast was
            // never able to bind a texture and the model is white no matter what happens below.
            bool shadersPresent = GltfShadersAvailable;
            if (!shadersPresent)
            {
                LastProblem = "glTF shaders missing from this build - textures cannot load. " +
                              "Run AuraSync > Fix glTF Shader Inclusion, then rebuild.";
                Debug.LogError($"[GLTFastMaterialHelper] '{KeyShader}' is not in this build. " +
                               "glTFast cannot bind any texture, so models render white. " +
                               "Fix: Project Settings > Graphics > Always Included Shaders (the menu " +
                               "item AuraSync > Fix glTF Shader Inclusion does it for you), then rebuild.");
            }

            var inspected = new HashSet<Material>();
            int healthy = 0, broken = 0, textured = 0;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                // sharedMaterials, never materials: the latter clones every material on access,
                // which on a 400-part model means 400 leaked copies and no batching.
                Material[] mats = renderer.sharedMaterials;
                bool slotsChanged = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];

                    if (mat == null)
                    {
                        mats[i] = MakeFallbackMaterial();
                        slotsChanged = true;
                        broken++;
                        continue;
                    }

                    if (!inspected.Add(mat)) continue; // shared with a part already handled

                    if (IsBroken(mat))
                    {
                        RepairInPlace(mat);
                        broken++;
                    }
                    else
                    {
                        healthy++;
                        if (mat.mainTexture != null) textured++;
                    }

                    if (verboseLogging)
                    {
                        Debug.Log($"[GLTFastMaterialHelper] '{renderer.name}' mat '{mat.name}' " +
                                  $"shader '{mat.shader?.name ?? "<null>"}' " +
                                  $"tex '{(mat.mainTexture != null ? mat.mainTexture.name : "none")}'");
                    }
                }

                if (slotsChanged) renderer.sharedMaterials = mats;
            }

            if (broken > 0 && string.IsNullOrEmpty(LastProblem))
            {
                LastProblem = $"{broken} material(s) had no usable shader and were replaced with a plain one.";
            }

            Debug.Log($"[GLTFastMaterialHelper] {healthy} material(s) left as glTFast built them " +
                      $"({textured} with a base colour texture), {broken} repaired. " +
                      $"glTF shaders in build: {shadersPresent}.");

            if (shadersPresent && textured == 0 && healthy > 0)
            {
                // Worth saying: the shaders are fine, so a white model now means the file itself
                // carries no texture, which is a different problem and not one to fix in code.
                Debug.LogWarning("[GLTFastMaterialHelper] No material carries a base colour texture. " +
                                 "The shaders are present, so this model most likely has none - check " +
                                 "the .glb itself (vertex colours only, or textures stripped on export).");
            }
        }

        /// <summary>A material is only "broken" when it has nothing to draw with.</summary>
        private static bool IsBroken(Material mat)
        {
            Shader s = mat.shader;
            return s == null || s.name == ErrorShaderName;
        }

        /// <summary>
        /// Last resort for a material with no shader: make it visible and lit rather than magenta.
        /// Any texture it once referenced is already gone - it was never bound - so this cannot
        /// bring colour back. It only stops the model disappearing.
        /// </summary>
        private static void RepairInPlace(Material mat)
        {
            Shader fallback = FallbackShader();
            if (fallback == null) return;

            Color keep = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture tex = mat.mainTexture;

            mat.shader = fallback;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", keep);
            if (tex != null) mat.mainTexture = tex;
        }

        private static Material MakeFallbackMaterial()
        {
            return AuraSync.Networking.PointerVisuals.Create(Color.white);
        }

        /// <summary>
        /// Taken from Unity's built-in default material rather than Shader.Find("Standard"), which
        /// would only work if Standard were in Always Included Shaders - and putting it there costs
        /// tens of thousands of shader variants at build time for a path that almost never runs.
        /// </summary>
        private static Shader FallbackShader()
        {
            return AuraSync.Networking.PointerVisuals.FallbackShader;
        }
    }
}
