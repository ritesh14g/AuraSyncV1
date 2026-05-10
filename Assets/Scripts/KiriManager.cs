using System;
using System.Collections;
using UnityEngine;

public class KiriManager : MonoBehaviour
{
    public event Action<string> OnModelReady;
    public event Action<float> OnProgress;

    public void StartCapture()
    {
        Debug.Log("[KiriManager] StartCapture called. Beginning mock 3D model processing pipeline...");
        StartCoroutine(MockProcessingRoutine());
    }

    private IEnumerator MockProcessingRoutine()
    {
        float duration = 10f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            
            // Fire the progress event
            OnProgress?.Invoke(progress);
            
            // Wait for the next frame
            yield return null;
        }

        Debug.Log("[KiriManager] Mock processing complete. Firing OnModelReady event.");
        
        // Fire the model ready event with the hardcoded mock file path
        OnModelReady?.Invoke("file://C:/MockData/sample_model.glb");
    }
}
