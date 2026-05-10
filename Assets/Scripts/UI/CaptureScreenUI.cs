using UnityEngine;

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

    // Reference resolution for scaling the legacy GUI
    private readonly Vector2 referenceResolution = new Vector2(1080, 1920);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeAutomatically()
    {
        // Automatically create a GameObject, attach this script, and persist it across scenes.
        GameObject autoInitGO = new GameObject("CaptureScreenUI_AutoInit");
        autoInitGO.AddComponent<CaptureScreenUI>();
        DontDestroyOnLoad(autoInitGO);
    }

    private void Start()
    {
        if (KiriManager.Instance != null)
        {
            KiriManager.Instance.OnProgress += HandleProgress;
            KiriManager.Instance.OnModelReady += HandleModelReady;
        }
        else
        {
            Debug.LogWarning("[CaptureScreenUI] KiriManager.Instance is null on Start! Make sure it exists in the scene.");
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

    private void HandleProgress(float progress)
    {
        currentProgress = progress;
        // In case it was triggered from outside
        if (currentState != State.Processing)
        {
            currentState = State.Processing;
        }
    }

    private void HandleModelReady(string url)
    {
        modelUrl = url;
        currentState = State.Ready;
    }

    private void OnGUI()
    {
        // Scale the GUI matrix so it is readable on high resolution screens
        float scaleX = Screen.width / referenceResolution.x;
        float scaleY = Screen.height / referenceResolution.y;
        float scale = Mathf.Max(scaleX, scaleY);
        
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1.0f));

        // Use the logical width and height after scaling
        float logicalWidth = Screen.width / scale;
        float logicalHeight = Screen.height / scale;

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
            wordWrap = true
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
        }
        else if (currentState == State.Processing)
        {
            Rect textRect = new Rect(100f, logicalHeight / 2f - 150f, logicalWidth - 200f, 300f);
            int percentage = Mathf.RoundToInt(currentProgress * 100f);
            GUI.Label(textRect, $"Uploading to KIRI: {percentage}%", textStyle);
        }
        else if (currentState == State.Ready)
        {
            Rect textRect = new Rect(100f, logicalHeight / 2f - 200f, logicalWidth - 200f, 400f);
            GUI.Label(textRect, $"Model Ready:\n{modelUrl}", textStyle);
        }
    }
}
