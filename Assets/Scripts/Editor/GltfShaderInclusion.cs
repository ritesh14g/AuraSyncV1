using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace AuraSync.EditorTools
{
    /// <summary>
    /// Keeps glTFast's Built-in render pipeline shaders inside the player build.
    ///
    /// **This is the fix for models loading white and untextured on a phone.** Unity only ships a
    /// shader in a build if something in the build references it. glTFast creates its materials at
    /// runtime, and its shaders sit in a package folder with no `Resources` directory, so nothing
    /// references them at build time and the stripper drops all three. On the device
    /// `Shader.Find("glTF/PbrMetallicRoughness")` then returns null, glTFast cannot bind the base
    /// colour texture (or the normal, occlusion and emissive maps), and every model renders as a
    /// flat white solid.
    ///
    /// It never reproduces in the Editor, because the Editor has every shader loaded regardless.
    /// That is what makes it such an unpleasant bug to chase.
    ///
    /// Listing the shaders in *Project Settings > Graphics > Always Included Shaders* is the
    /// supported way to keep them. Doing it from a build hook rather than by hand means it survives
    /// a glTFast upgrade, a fresh clone, or someone clearing the list.
    /// </summary>
    public class GltfShaderInclusion : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>
        /// glTFast picks one of these per material depending on the glTF's material model, so all
        /// three have to survive.
        ///
        /// Standard is deliberately **not** listed. An always-included shader cannot have its
        /// variants stripped by usage analysis, and Standard's variant count is large enough that
        /// including it added the better part of an hour to an Android build. The one place that
        /// needed it - GLTFastMaterialHelper's repair path - now takes the shader from Unity's
        /// built-in default material via PointerVisuals, which is always in the build anyway.
        /// </summary>
        private static readonly string[] RequiredShaders =
        {
            "glTF/PbrMetallicRoughness",
            "glTF/PbrSpecularGlossiness",
            "glTF/Unlit",
            "AuraSync/SectionClip", // V3 Phase 6: the section cut swaps materials onto it at runtime
            "AuraSync/AttentionBeam", // V3 Phase 8: see-through view cones (made at runtime, so nothing references it)
        };

        /// <summary>
        /// Shaders an earlier version of this script added and that must now come back out.
        /// Leaving Standard in the list costs every one of its variants on every build.
        /// </summary>
        private static readonly string[] ObsoleteShaders = { "Standard" };

        public void OnPreprocessBuild(BuildReport report)
        {
            Ensure();
        }

        [MenuItem("AuraSync/Fix glTF Shader Inclusion")]
        private static void RunFromMenu()
        {
            int changed = Ensure();
            EditorUtility.DisplayDialog(
                "glTF shader inclusion",
                changed == 0
                    ? "Always Included Shaders was already correct; saved it to disk anyway.\n\nModels loading white is NOT caused by shader stripping in that case - check the [GLTFastMaterialHelper] log lines instead."
                    : $"Updated Always Included Shaders ({changed} change(s)). See the Console for what moved.\n\nRebuild the APK.",
                "OK");
        }

        /// <summary>Adds any missing shader to Always Included Shaders. Returns how many it added.</summary>
        public static int Ensure()
        {
            Object graphicsSettings = GraphicsSettings.GetGraphicsSettings();
            if (graphicsSettings == null)
            {
                Debug.LogError("[GltfShaderInclusion] Could not reach GraphicsSettings.");
                return 0;
            }

            var so       = new SerializedObject(graphicsSettings);
            var included = so.FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
            {
                Debug.LogError("[GltfShaderInclusion] GraphicsSettings has no m_AlwaysIncludedShaders.");
                return 0;
            }

            int removed = 0;
            var obsolete = new HashSet<Shader>();
            foreach (string name in ObsoleteShaders)
            {
                Shader s = Shader.Find(name);
                if (s != null) obsolete.Add(s);
            }

            // Walk backwards: removing an element shifts everything after it.
            for (int i = included.arraySize - 1; i >= 0; i--)
            {
                if (included.GetArrayElementAtIndex(i).objectReferenceValue is Shader s && obsolete.Contains(s))
                {
                    included.DeleteArrayElementAtIndex(i);
                    removed++;
                    Debug.Log($"[GltfShaderInclusion] Removed '{s.name}' from Always Included Shaders " +
                              "(its variants were costing build time for no benefit).");
                }
            }

            var present = new HashSet<Shader>();
            for (int i = 0; i < included.arraySize; i++)
            {
                if (included.GetArrayElementAtIndex(i).objectReferenceValue is Shader s) present.Add(s);
            }

            int added = 0;
            foreach (string name in RequiredShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    // Only worth shouting about for the glTF ones; they are what the models need.
                    Debug.LogWarning($"[GltfShaderInclusion] Shader '{name}' not found in this project - skipped.");
                    continue;
                }

                if (!present.Add(shader)) continue; // already listed

                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
                added++;
                Debug.Log($"[GltfShaderInclusion] Added '{name}' to Always Included Shaders.");
            }

            if (added > 0 || removed > 0) so.ApplyModifiedProperties();

            // Saved unconditionally, not only when something changed.
            //
            // GraphicsSettings lives in ProjectSettings/, not the asset database, so an edit applies
            // to the in-memory object and AssetDatabase.SaveAssets() does not reliably flush it. That
            // left a trap: the first run removed the shader in memory, every later run saw it as
            // already gone and reported "nothing to do", and the stale entry sat on disk forever.
            // Flushing every time makes the file match memory whatever route we took to get here.
            EditorUtility.SetDirty(graphicsSettings);
            AssetDatabase.SaveAssetIfDirty(graphicsSettings);
            AssetDatabase.SaveAssets();

            return added + removed;
        }
    }
}
