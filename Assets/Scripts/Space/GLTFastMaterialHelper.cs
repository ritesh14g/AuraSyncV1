using UnityEngine;

namespace AuraSync.Space
{
    public class GLTFastMaterialHelper : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("The material created in Step 1 (StandardFallback)")]
        [SerializeField] private Material fallbackMaterial;
        [SerializeField] private bool debugLogging = true;

        public void FixAllMaterials(Transform root)
        {
            if (root == null) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (debugLogging) Debug.Log($"[AuraSync] MaterialHelper processing {renderers.Length} renderers.");

            foreach (var renderer in renderers)
            {
                Material[] mats = renderer.materials; 
                for (int i = 0; i < mats.Length; i++)
                {
                    FixMaterial(mats[i]);
                }
                renderer.materials = mats;
            }
            if (debugLogging) Debug.Log("[GLTFastMaterialHelper] Done. Fixed: " + renderers.Length + " renderers.");
        }

        private void FixMaterial(Material mat)
        {
            if (mat == null) return;

            // 1. Try to find the texture in the current material
            Texture mainTex = mat.mainTexture;
            if (mainTex == null)
            {
                string[] props = { "_BaseMap", "_BaseColorMap", "_MainTex", "baseColorTexture" };
                foreach (var p in props)
                {
                    if (mat.HasProperty(p))
                    {
                        mainTex = mat.GetTexture(p);
                        if (mainTex != null) break;
                    }
                }
            }

            // 2. Use the fallback shader if provided
            if (fallbackMaterial != null && fallbackMaterial.shader != null)
            {
                mat.shader = fallbackMaterial.shader;
            }

            // 3. Re-bind the texture
            if (mainTex != null)
            {
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", mainTex);
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
                mat.mainTexture = mainTex;
                mat.color = Color.white;
            }
            else
            {
                mat.color = new Color(0.9f, 0.9f, 0.9f);
            }
        }
    }
}
