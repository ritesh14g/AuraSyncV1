using UnityEngine;
using System.Collections;

namespace AuraSync.Space
{
    /// <summary>
    /// STANDALONE FIX: Attach this directly to the "ModelAnchor" in your SpaceScene.
    /// It handles its own input and forces textures to appear.
    /// </summary>
    public class SpaceModelViewerFix : MonoBehaviour
    {
        [Header("Settings")]
        public float rotateSpeed = 0.5f;
        public float zoomSpeed = 0.1f;
        
        private Vector2 _lastMousePos;
        private bool _isInitialized = false;

        void Start()
        {
            StartCoroutine(DelayedFix());
        }

        IEnumerator DelayedFix()
        {
            // Wait for GLTFast to finish its background work
            yield return new WaitForSeconds(2.0f);
            
            Debug.Log("[AuraSync Fix] Starting Brute-Force Shader Fix...");
            
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            Shader unlitShader = Shader.Find("Unlit/Texture");
            if (unlitShader == null) unlitShader = Shader.Find("Mobile/Unlit (Supports Lightmap)");

            foreach (var r in renderers)
            {
                foreach (var mat in r.materials)
                {
                    // Find the texture by any means necessary
                    Texture tex = mat.mainTexture;
                    if (tex == null)
                    {
                        if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                        else if (mat.HasProperty("_BaseColorMap")) tex = mat.GetTexture("_BaseColorMap");
                        else if (mat.HasProperty("baseColorTexture")) tex = mat.GetTexture("baseColorTexture");
                    }

                    // FORCE UNLIT (This ignores bad lighting and shows the raw texture)
                    mat.shader = unlitShader;
                    if (tex != null) mat.mainTexture = tex;
                    mat.color = Color.white;
                }
            }
            
            _isInitialized = true;
            Debug.Log("[AuraSync Fix] Brute-Force Fix Complete. Total Renderers: " + renderers.Length);
        }

        void Update()
        {
            if (!_isInitialized) return;

            // MOUSE / SINGLE TOUCH ROTATE
            if (Input.GetMouseButtonDown(0))
            {
                _lastMousePos = Input.mousePosition;
            }
            else if (Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _lastMousePos;
                transform.Rotate(Vector3.up, -delta.x * rotateSpeed, UnityEngine.Space.World);
                transform.Rotate(Vector3.right, delta.y * rotateSpeed, UnityEngine.Space.World);
                _lastMousePos = Input.mousePosition;
            }

            // PINCH ZOOM (Mobile)
            if (Input.touchCount == 2)
            {
                Touch t0 = Input.GetTouch(0);
                Touch t1 = Input.GetTouch(1);
                
                float prevDist = Vector2.Distance(t0.position - t0.deltaPosition, t1.position - t1.deltaPosition);
                float currDist = Vector2.Distance(t0.position, t1.position);
                float delta = currDist - prevDist;

                transform.localScale += Vector3.one * (delta * zoomSpeed * Time.deltaTime);
            }
        }
    }
}
