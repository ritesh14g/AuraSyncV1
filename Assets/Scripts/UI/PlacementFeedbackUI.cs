using System.Collections;
using UnityEngine;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// Displays brief placement hint messages (toasts) in AR view.
    /// Also auto-shows a hint when entering placement mode.
    /// SETUP: Attach to a Canvas child with TMP_Text + CanvasGroup.
    /// </summary>
    public class PlacementFeedbackUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI feedbackText;
        [SerializeField] private float displayDuration = 2f;
        [SerializeField] private CanvasGroup canvasGroup;

        private Coroutine _fadeCoroutine;

        void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null) canvasGroup.alpha = 0f;
        }

        void OnEnable()
        {
            if (Core.AppStateManager.Instance != null)
            {
                Core.AppStateManager.Instance.OnModeChanged += OnModeChanged;
                Debug.Log("[AuraSync] PlacementFeedbackUI: Subscribed to AppStateManager");
            }
            else
            {
                // AppStateManager not ready yet — retry each frame until it is
                StartCoroutine(SubscribeWhenReady());
            }
        }

        void OnDisable()
        {
            if (Core.AppStateManager.Instance != null)
                Core.AppStateManager.Instance.OnModeChanged -= OnModeChanged;
        }

        private IEnumerator SubscribeWhenReady()
        {
            while (Core.AppStateManager.Instance == null)
                yield return null;

            Core.AppStateManager.Instance.OnModeChanged += OnModeChanged;
            Debug.Log("[AuraSync] PlacementFeedbackUI: Subscribed (delayed)");
        }

        private void OnModeChanged(Core.AppMode mode)
        {
            if (mode == Core.AppMode.PlacingNote || mode == Core.AppMode.PlacingObject)
                Show("Tap a flat surface to place");
        }

        public void Show(string message)
        {
            if (feedbackText != null) feedbackText.text = message;
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine());
        }

        private IEnumerator FadeRoutine()
        {
            if (canvasGroup == null) yield break;
            canvasGroup.alpha = 1f;
            yield return new WaitForSeconds(displayDuration);
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, t / 0.4f);
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }
    }
}
