using UnityEngine;
using System;

public class KiriManager : MonoBehaviour {
    public static KiriManager Instance;

    private void Awake() {
        if (Instance == null) Instance = this;
    }

    public void StartCapture() {
        // We will add the mock timer here in the next step
    }

    public event Action<string> OnModelReady;  // returns .glb URL — M4 listens
    public event Action<float>  OnProgress;    // 0-1 progress — UI listens
}
