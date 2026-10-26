using System;
using System.Collections;
using UnityEngine;

namespace AuraSync.Core
{
    public class KiriManager : MonoBehaviour
    {
        public static KiriManager Instance;

        public event Action<string> OnModelReady;
        public event Action<float> OnProgress;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private Coroutine _running;

        public bool IsRunning => _running != null;

        /// <summary>Starts the mock KIRI job. duration = seconds of simulated processing.</summary>
        public void StartCapture(float duration = 3f)
        {
            Debug.Log("[KiriManager] Starting Mock Capture Process...");
            CancelCapture();
            _running = StartCoroutine(MockCaptureRoutine(duration));
        }

        /// <summary>Stops a running mock job; no OnModelReady will follow.</summary>
        public void CancelCapture()
        {
            if (_running == null) return;
            StopCoroutine(_running);
            _running = null;
            Debug.Log("[KiriManager] Mock capture cancelled.");
        }

        private IEnumerator MockCaptureRoutine(float duration)
        {
            duration = Mathf.Max(0.5f, duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                OnProgress?.Invoke(progress);
                yield return null;
            }

            // Processing complete
            OnProgress?.Invoke(1.0f);
            
            // Dummy URL pointing to the Hugging Face model
            string dummyUrl = "https://huggingface.co/datasets/Ritesh121/ARModels/resolve/main/House.glb";
            
            Debug.Log($"[KiriManager] Mock Capture Complete! Returning URL: {dummyUrl}");
            _running = null;
            OnModelReady?.Invoke(dummyUrl);
        }
    }
}
