using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// Attach this to AuraSyncUI. 
    /// It auto-fixes the EventSystem for Unity 2022 and finds what is blocking your clicks.
    /// </summary>
    public class UIDebugger : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("[AuraSync Debug] UI Debugger Starting...");
            FixEventSystem();
            CheckForBlockers();
        }

        void FixEventSystem()
        {
            EventSystem es = FindObjectOfType<EventSystem>();
            if (es == null)
            {
                Debug.LogError("[AuraSync Debug] MISSING EVENTSYSTEM! Adding one now.");
                GameObject go = new GameObject("EventSystem");
                es = go.AddComponent<EventSystem>();
            }

            // Check for the new Input System module
            var oldModule = es.GetComponent("StandaloneInputModule");
            if (oldModule != null && UnityEngine.InputSystem.InputSystem.settings != null)
            {
                Debug.LogWarning("[AuraSync Debug] Old Input Module detected with New Input System. Upgrading...");
                DestroyImmediate(oldModule);
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        void CheckForBlockers()
        {
            Graphic[] allGraphics = Object.FindObjectsOfType<Graphic>();
            foreach (var g in allGraphics)
            {
                if (g.raycastTarget && g.canvasRenderer.GetAlpha() > 0.01f && g.enabled)
                {
                    // If it's a giant full-screen image that isn't a button, it's a blocker!
                    RectTransform rt = g.rectTransform;
                    if (rt.rect.width > Screen.width * 0.9f && rt.rect.height > Screen.height * 0.9f)
                    {
                        if (g.GetComponent<Button>() == null && g.name.ToLower().Contains("backdrop"))
                        {
                            Debug.LogWarning($"[AuraSync Debug] Possible Blocker Found: {g.name} on {g.gameObject.name}. Forcing raycastTarget OFF.");
                            g.raycastTarget = false;
                        }
                    }
                }
            }
        }

        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current);
                eventData.position = Input.mousePosition;
                var results = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(eventData, results);

                if (results.Count > 0)
                {
                    Debug.Log($"[AuraSync Debug] Click Hit: {results[0].gameObject.name}");
                }
                else
                {
                    Debug.LogWarning("[AuraSync Debug] Click Hit NOTHING. The EventSystem is likely broken or the Camera is wrong.");
                }
            }
        }
    }
}
