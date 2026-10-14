using System;
using System.Collections.Generic;
using UnityEngine;
using AuraSync.Space;

namespace AuraSync.EditorTools
{
    /// <summary>
    /// Takes pictures of a loaded model, or of chosen parts of it, for the AI to look at.
    ///
    /// The model is moved onto an unused layer for the shot and a temporary camera renders only that
    /// layer, so nothing else in the scene (UI, floor, other objects) gets into the picture. Every
    /// renderer's visibility and layer is put back afterwards, even if rendering fails.
    /// </summary>
    internal static class PartRenderer
    {
        private static readonly Color Background = new Color(0.92f, 0.93f, 0.95f);
        private const float FieldOfView = 30f;

        /// <summary>Views used for a part on its own, in the model's own space.</summary>
        public static readonly Vector3[] PartViews =
        {
            new Vector3( 1.0f, 0.6f,  1.0f),
            new Vector3(-1.0f, 0.4f, -0.3f),
            new Vector3( 0.2f, 1.0f,  0.4f),
        };

        /// <summary>Views used for the whole model.</summary>
        public static readonly Vector3[] ModelViews =
        {
            new Vector3( 1.0f, 0.6f,  1.0f),
            new Vector3(-1.0f, 0.5f,  0.2f),
            new Vector3( 0.0f, 0.3f, -1.0f),
            new Vector3( 0.3f, 1.0f,  0.3f),
        };

        /// <summary>A layer no object in the project uses, or -1.</summary>
        public static int FindFreeLayer()
        {
            for (int i = 31; i >= 8; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        /// <summary>
        /// Renders the model from one direction, showing only the renderers <paramref name="show"/>
        /// accepts, framed on what is shown. Returns JPEG bytes and the texture (for a preview).
        /// </summary>
        public static byte[] Render(Transform root, Func<Renderer, bool> show, Vector3 viewDirInModel,
                                    int size, int layer, out Texture2D preview)
        {
            preview = null;
            var all = root.GetComponentsInChildren<Renderer>(true);
            var savedEnabled = new bool[all.Length];
            var savedLayer = new int[all.Length];

            GameObject camGo = null;
            RenderTexture rt = null;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                Bounds bounds = default;
                bool any = false;

                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i];
                    savedEnabled[i] = r.enabled;
                    savedLayer[i] = r.gameObject.layer;

                    bool visible = r.gameObject.name != ModelRaycaster.HighlightName && show(r);
                    r.enabled = visible;
                    if (!visible) continue;

                    r.gameObject.layer = layer;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                if (!any) return null;

                Vector3 dir = root.TransformDirection(viewDirInModel).normalized;
                Vector3 up = Mathf.Abs(Vector3.Dot(dir, root.up)) > 0.95f ? root.forward : root.up;
                float radius = Mathf.Max(bounds.extents.magnitude, 1e-4f);
                float distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;

                camGo = new GameObject("PartLabelerCamera") { hideFlags = HideFlags.HideAndDontSave };
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false; // rendered by hand below, never by the scene
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Background;
                cam.cullingMask = 1 << layer;
                cam.fieldOfView = FieldOfView;
                cam.nearClipPlane = Mathf.Max(distance * 0.01f, distance - radius * 1.5f);
                cam.farClipPlane = distance + radius * 2f;
                camGo.transform.position = bounds.center + dir * distance;
                camGo.transform.LookAt(bounds.center, up);

                rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                preview = new Texture2D(size, size, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                preview.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                preview.Apply();
                return preview.EncodeToJPG(85);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);

                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    all[i].enabled = savedEnabled[i];
                    all[i].gameObject.layer = savedLayer[i];
                }
            }
        }

        /// <summary>True if this renderer belongs to the part (the part itself or anything under it).</summary>
        public static bool IsUnder(Renderer r, Transform part) => r.transform.IsChildOf(part);
    }
}
