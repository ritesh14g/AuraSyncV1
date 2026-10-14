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
        [Tooltip("Per-frame input logging. Off by default: it printed a line every second on the " +
                 "phone, which buried the voice and material diagnostics that actually matter.")]
        [SerializeField] private bool verboseInputLogging = false;

        private float _nextInputLogTime = 0f;

        void Start()
        {
            FixEventSystem();
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

        // CheckForBlockers() was removed. It force-disabled raycastTarget on any full-screen graphic
        // whose name contained "backdrop" and had no Button. The room panel's backdrop survived only
        // because it happens to carry one for tap-to-dismiss; any future backdrop without a Button
        // would have silently stopped blocking touches, which would be a miserable bug to trace.

        void Update()
        {
            if (!verboseInputLogging) return;

            if (Time.unscaledTime >= _nextInputLogTime)
            {
                _nextInputLogTime = Time.unscaledTime + 1f;

                Debug.Log("[INPUT TEST] UPDATE RUNNING | Mouse=" +
                    (UnityEngine.InputSystem.Mouse.current != null) +
                    " | EventSystem=" + (EventSystem.current != null) +
                    " | Active=" + gameObject.activeInHierarchy +
                    " | Enabled=" + enabled);
            }
            if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current);
                eventData.position = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
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








