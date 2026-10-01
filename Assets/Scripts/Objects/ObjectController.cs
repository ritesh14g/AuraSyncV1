using UnityEngine;
using AuraSync.Data;

namespace AuraSync.Objects
{
    /// <summary>
    /// Component attached to every placed 3D object in the scene.
    /// Manages the object's material properties (color, opacity, border/outline).
    /// 
    /// PREFAB SETUP:
    /// - Root: with this script + MeshFilter + MeshRenderer + Collider
    /// - Material: should support _Color and _Alpha properties
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(Collider))]
    public class ObjectController : MonoBehaviour
    {
        // ── Data ──
        private ObjectItemData _data;
        public ObjectItemData Data => _data;

        // ── Components ──
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propBlock;

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _propBlock = new MaterialPropertyBlock();
        }

        // ──────────────────────────────────────────
        //  Initialization
        // ──────────────────────────────────────────

        /// <summary>Initialize with data (on placement or restoration).</summary>
        public void Initialize(ObjectItemData data)
        {
            _data = data;
            ApplyVisuals();
        }

        // ──────────────────────────────────────────
        //  Visual Application
        // ──────────────────────────────────────────

        /// <summary>Apply color, opacity, and border from data.</summary>
        public void ApplyVisuals()
        {
            if (_data == null || _renderer == null) return;

            // Parse color
            if (ColorUtility.TryParseHtmlString(_data.colorHex, out Color baseColor))
            {
                baseColor.a = _data.opacity;

                _renderer.material.color = baseColor;

                // If opacity < 1, we need to switch to transparent rendering
                if (_data.opacity < 1f)
                {
                    SetMaterialTransparent();
                }
                else
                {
                    SetMaterialOpaque();
                }
            }

            // Border / Outline
            if (_data.hasBorder)
            {
                EnableOutline();
            }
            else
            {
                DisableOutline();
            }
        }

        private void SetMaterialTransparent()
        {
            if (_renderer.material == null) return;
            Material mat = _renderer.material;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        private void SetMaterialOpaque()
        {
            if (_renderer.material == null) return;
            Material mat = _renderer.material;
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = -1;
        }

        // ──────────────────────────────────────────
        //  Outline (simple approach: second scaled mesh)
        // ──────────────────────────────────────────

        private GameObject _outlineObj;

        private void EnableOutline()
        {
            if (_outlineObj != null) return;

            // Create a slightly larger copy as outline
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;

            _outlineObj = new GameObject("Outline");
            _outlineObj.transform.SetParent(transform, false);
            _outlineObj.transform.localScale = Vector3.one * 1.05f;

            var outlineMf = _outlineObj.AddComponent<MeshFilter>();
            outlineMf.sharedMesh = mf.sharedMesh;

            var outlineRenderer = _outlineObj.AddComponent<MeshRenderer>();

            // Create outline material
            Material outlineMat = new Material(Shader.Find("Unlit/Color"));
            if (ColorUtility.TryParseHtmlString(_data.borderColorHex, out Color borderColor))
            {
                outlineMat.color = borderColor;
            }

            // Render back faces only for outline effect
            outlineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);
            outlineRenderer.material = outlineMat;
        }

        private void DisableOutline()
        {
            if (_outlineObj != null)
            {
                Destroy(_outlineObj);
                _outlineObj = null;
            }
        }

        // ──────────────────────────────────────────
        //  Update Data (after re-edit)
        // ──────────────────────────────────────────

        /// <summary>Update data and refresh visuals.</summary>
        public void UpdateData(ObjectItemData newData)
        {
            _data = newData;
            DisableOutline(); // reset outline before reapplying
            ApplyVisuals();

            _data.SetPosition(transform.position);
            _data.SetRotation(transform.rotation);
            _data.SetScale(transform.localScale);

            Core.PersistenceManager.Instance?.UpdateObject(_data);
        }

        /// <summary>
        /// Apply a new colour to this object at runtime (called by ObjectCustomizerUI.OpenForEdit).
        /// </summary>
        public void ApplyColor(Color color)
        {
            if (_data == null) return;
    
            _data.colorHex = "#" + ColorUtility.ToHtmlStringRGB(color);
            _data.opacity = color.a;
    
            ApplyVisuals(); // reuse the same path as creation
    
            Core.PersistenceManager.Instance?.UpdateObject(_data);
        }
    }
}
