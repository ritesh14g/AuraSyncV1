using UnityEngine;
using AuraSync.Core; // For KiriManager

public class CaptureScreenUI : MonoBehaviour
{
    private enum State
    {
        Idle,
        Processing,
        Ready
    }

    private State currentState = State.Idle;
    private float currentProgress = 0f;
    private string modelUrl = "";
    private bool isVisible = false; // Controls whether the GUI is drawn

    // Reference resolution for scaling the legacy GUI
    private readonly Vector2 referenceResolution = new Vector2(1080, 1920);

    private void Start()
    {
        // Don't show automatically
        isVisible = false;

        // The new onboarding UI runs the Make model steps and saves the scan itself; listening
        // here too would save every scan twice.
        if (AuraSync.UI.OnboardingFlow.UseNewOnboarding) return;

        if (KiriManager.Instance != null)
        {
            KiriManager.Instance.OnProgress += HandleProgress;
            KiriManager.Instance.OnModelReady += HandleModelReady;
        }
        else
        {
            Debug.LogWarning("[CaptureScreenUI] KiriManager.Instance is null on Start! Make sure you added it to your scene.");
        }
    }

    private void OnDestroy()
    {
        if (KiriManager.Instance != null)
        {
            KiriManager.Instance.OnProgress -= HandleProgress;
            KiriManager.Instance.OnModelReady -= HandleModelReady;
        }
    }

    // Call this from your "Make Model Button" in the Inspector!
    public void Show()
    {
        isVisible = true;
        currentState = State.Idle;
        currentProgress = 0f;
        modelUrl = "";
        Debug.Log("[CaptureScreenUI] Interface opened.");
    }

    public void Close()
    {
        isVisible = false;
    }

    private void HandleProgress(float progress)
    {
        currentProgress = progress;
        if (currentState != State.Processing)
        {
            currentState = State.Processing;
        }
    }

    private void HandleModelReady(string url)
    {
        modelUrl = url;
        currentState = State.Ready;

        // --- PIPELINE INTEGRATION ---
        // The scan is stored on this device. The shared catalogue is a read-only manifest on
        // Hugging Face, so a capture cannot write to it - see ModelCatalog for why.
        if (ModelCatalog.Instance != null)
        {
            Debug.Log("[CaptureScreenUI] Model captured successfully. Saving to the catalogue...");

            string capturedUrl = modelUrl;
            ScanNaming.BuildTodaysName(scanName =>
            {
                if (ModelCatalog.Instance == null) return;

                ModelData newModel = new ModelData
                {
                    name = scanName,
                    glbUrl = capturedUrl,
                    description = "Freshly Captured Model"
                };
                ModelCatalog.Instance.SaveModel(newModel);
                Debug.Log($"[CaptureScreenUI] Saved scan as '{scanName}'.");
            });
        }
        else
        {
            Debug.LogWarning("[CaptureScreenUI] Cannot save the scan: ModelCatalog.Instance is missing!");
        }
    }

    private void OnGUI()
    {
        if (!isVisible) return;

        // Scale the GUI matrix so it is readable on high resolution screens
        float scaleX = Screen.width / referenceResolution.x;
        float scaleY = Screen.height / referenceResolution.y;
        float scale = Mathf.Max(scaleX, scaleY);
        
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1.0f));

        // Use the logical width and height after scaling
        float logicalWidth = Screen.width / scale;
        float logicalHeight = Screen.height / scale;

        // Dark background overlay
        GUI.color = new Color(0, 0, 0, 0.85f);
        GUI.DrawTexture(new Rect(0, 0, logicalWidth, logicalHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Set up large, readable styles
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 45,
            wordWrap = true
        };

        GUIStyle textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 55,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = Color.white }
        };

        // Draw based on the current state
        if (currentState == State.Idle)
        {
            float btnWidth = 800f;
            float btnHeight = 250f;
            Rect btnRect = new Rect((logicalWidth - btnWidth) / 2f, (logicalHeight - btnHeight) / 2f, btnWidth, btnHeight);

            if (GUI.Button(btnRect, "Start Camera Capture", buttonStyle))
            {
                if (KiriManager.Instance != null)
                {
                    currentState = State.Processing;
                    currentProgress = 0f;
                    KiriManager.Instance.StartCapture();
                }
                else
                {
                    Debug.LogError("Cannot start capture, KiriManager.Instance is null!");
                }
            }

            // Optional close button for idle state
            Rect closeBtnRect = new Rect((logicalWidth - 400f) / 2f, logicalHeight - 300f, 400f, 150f);
            if (GUI.Button(closeBtnRect, "Cancel", buttonStyle))
            {
                Close();
            }
        }
        else if (currentState == State.Processing)
        {
            Rect textRect = new Rect(100f, logicalHeight / 2f - 150f, logicalWidth - 200f, 300f);
            int percentage = Mathf.RoundToInt(currentProgress * 100f);
            GUI.Label(textRect, $"Uploading to KIRI: {percentage}%", textStyle);
        }
        else if (currentState == State.Ready)
        {
            Rect textRect = new Rect(100f, logicalHeight / 2f - 300f, logicalWidth - 200f, 400f);
            GUI.Label(textRect, $"Model Saved to Database!\nURL: {modelUrl}", textStyle);

            float btnWidth = 800f;
            float btnHeight = 250f;
            Rect btnRect = new Rect((logicalWidth - btnWidth) / 2f, logicalHeight / 2f + 150f, btnWidth, btnHeight);

            if (GUI.Button(btnRect, "Finish", buttonStyle))
            {
                Close();
            }
        }
    }
}
