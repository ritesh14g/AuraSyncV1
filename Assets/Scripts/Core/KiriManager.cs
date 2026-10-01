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

        public void StartCapture()
        {
            Debug.Log("[KiriManager] Starting Mock Capture Process...");
            StartCoroutine(MockCaptureRoutine());
        }

        private IEnumerator MockCaptureRoutine()
        {
            float duration = 3.0f; // Simulate 3 seconds of processing
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
            OnModelReady?.Invoke(dummyUrl);
        }
    }
}
