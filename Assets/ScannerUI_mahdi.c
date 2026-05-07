using UnityEngine;

/// <summary>
/// ScannerUI.cs
/// Self-instantiating scanner UI. Drop into any Unity project — no scene setup required.
/// Scales automatically for high-resolution mobile screens.
/// </summary>
public class ScannerUI : MonoBehaviour
{
    // ─── Auto-instantiation ───────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        GameObject go = new GameObject("ScannerUI");
        go.AddComponent<ScannerUI>();
        DontDestroyOnLoad(go);
    }

    // ─── State machine ────────────────────────────────────────────────────────

    private enum ScanState
    {
        Idle,           // "Scan Environment" button
        Ready,          // "Start Capture" button (red)
        Recording,      // "Stop Capture" + REC timer
        Processing,     // Overlay with status messages
        Complete        // "Scan Complete." + Close button
    }

    private ScanState _state = ScanState.Idle;

    // ─── Recording timer ──────────────────────────────────────────────────────

    private float  _recElapsed   = 0f;
    private bool   _dotVisible   = true;
    private float  _dotTimer     = 0f;
    private const float DOT_BLINK_INTERVAL = 1f;

    // ─── Processing overlay ───────────────────────────────────────────────────

    private float  _procElapsed  = 0f;
    private string _procMessage  = "Initializing Cloud Architecture...";

    private readonly (float time, string msg)[] _procSteps = new[]
    {
        (0f,  "Initializing Cloud Architecture..."),
        (3f,  "Uploading Payload..."),
        (6f,  "Running Pose Estimation..."),
        (9f,  "Generating Gaussian Splats..."),
        (10f, "__COMPLETE__")          // sentinel → transition to Complete
    };

    // ─── UI scale ─────────────────────────────────────────────────────────────

    // Design canvas height. Scale factor = Screen.height / DESIGN_HEIGHT.
    private const float DESIGN_HEIGHT = 1920f;

    // ─── Cached GUIStyles (built once in OnGUI on first call) ─────────────────

    private GUIStyle _styleLargeBtn;
    private GUIStyle _styleLabel;
    private GUIStyle _styleSmallBtn;
    private GUIStyle _styleRecLabel;
    private bool     _stylesBuilt = false;

    // ─── Texture helpers ──────────────────────────────────────────────────────

    private Texture2D _texRed;
    private Texture2D _texDark;
    private Texture2D _texGray;
    private Texture2D _texWhite;

    // ═════════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ═════════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _texRed   = MakeTex(Color.red);
        _texDark  = MakeTex(new Color(0f, 0f, 0f, 0.82f));
        _texGray  = MakeTex(new Color(0.25f, 0.25f, 0.25f, 1f));
        _texWhite = MakeTex(Color.white);
    }

    private void Update()
    {
        if (_state == ScanState.Recording)
        {
            _recElapsed += Time.deltaTime;

            _dotTimer += Time.deltaTime;
            if (_dotTimer >= DOT_BLINK_INTERVAL)
            {
                _dotTimer  -= DOT_BLINK_INTERVAL;
                _dotVisible = !_dotVisible;
            }
        }

        if (_state == ScanState.Processing)
        {
            _procElapsed += Time.deltaTime;

            // Walk through timed steps
            for (int i = _procSteps.Length - 1; i >= 0; i--)
            {
                if (_procElapsed >= _procSteps[i].time)
                {
                    if (_procSteps[i].msg == "__COMPLETE__")
                    {
                        _state = ScanState.Complete;
                    }
                    else
                    {
                        _procMessage = _procSteps[i].msg;
                    }
                    break;
                }
            }
        }
    }

    private void OnDestroy()
    {
        // Clean up procedural textures to avoid leaks
        Destroy(_texRed);
        Destroy(_texDark);
        Destroy(_texGray);
        Destroy(_texWhite);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // OnGUI — everything drawn here
    // ═════════════════════════════════════════════════════════════════════════

    private void OnGUI()
    {
        BuildStyles();

        // Compute uniform scale factor so the UI looks the same on any DPI
        float scale = Screen.height / DESIGN_HEIGHT;

        // Push the scale matrix; we pivot around (0,0) (top-left)
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), Vector2.zero);

        // All layout below uses DESIGN-space coordinates (assume 1080 × 1920)
        float dw = Screen.width  / scale;   // design-space width  (≈1080)
        float dh = Screen.height / scale;   // design-space height (== DESIGN_HEIGHT)

        switch (_state)
        {
            case ScanState.Idle:
                DrawIdleState(dw, dh);
                break;

            case ScanState.Ready:
                DrawReadyState(dw, dh);
                break;

            case ScanState.Recording:
                DrawRecordingState(dw, dh);
                break;

            case ScanState.Processing:
                DrawProcessingOverlay(dw, dh);
                break;

            case ScanState.Complete:
                DrawCompleteOverlay(dw, dh);
                break;
        }

        // Restore matrix
        GUI.matrix = Matrix4x4.identity;
    }

    // ─── State drawers ────────────────────────────────────────────────────────

    private void DrawIdleState(float dw, float dh)
    {
        float btnW = dw * 0.8f;
        float btnH = 180f;
        float btnX = (dw - btnW) * 0.5f;
        float btnY = dh - btnH - 120f;

        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "Scan Environment", _styleLargeBtn))
        {
            _state = ScanState.Ready;
        }
    }

    private void DrawReadyState(float dw, float dh)
    {
        float btnW = dw * 0.8f;
        float btnH = 180f;
        float btnX = (dw - btnW) * 0.5f;
        float btnY = dh - btnH - 120f;

        // Temporarily swap background to red
        _styleLargeBtn.normal.background   = _texRed;
        _styleLargeBtn.hover.background    = _texRed;
        _styleLargeBtn.active.background   = _texRed;

        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "Start Capture", _styleLargeBtn))
        {
            _recElapsed = 0f;
            _dotTimer   = 0f;
            _dotVisible = true;
            _state      = ScanState.Recording;
        }

        // Restore background
        _styleLargeBtn.normal.background   = _texGray;
        _styleLargeBtn.hover.background    = _texGray;
        _styleLargeBtn.active.background   = _texGray;
    }

    private void DrawRecordingState(float dw, float dh)
    {
        // ── REC indicator (top-right) ─────────────────────────────────────────
        int totalSec  = Mathf.FloorToInt(_recElapsed);
        int minutes   = totalSec / 60;
        int seconds   = totalSec % 60;
        string timeStr = string.Format("{0:00}:{1:00}", minutes, seconds);

        // Draw dot in red or invisible
        Color dotColor  = _dotVisible ? Color.red : new Color(0f, 0f, 0f, 0f);
        Color prevColor = GUI.color;
        GUI.color = dotColor;
        GUI.Label(new Rect(dw - 480f, 40f, 80f, 80f), "●", _styleRecLabel);
        GUI.color = prevColor;

        // Draw "REC mm:ss" in white
        GUI.color = Color.white;
        GUI.Label(new Rect(dw - 420f, 40f, 400f, 80f), " REC " + timeStr, _styleRecLabel);
        GUI.color = prevColor;

        // ── Stop Capture button ───────────────────────────────────────────────
        float btnW = dw * 0.8f;
        float btnH = 180f;
        float btnX = (dw - btnW) * 0.5f;
        float btnY = dh - btnH - 120f;

        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), "Stop Capture", _styleLargeBtn))
        {
            _procElapsed = 0f;
            _procMessage = _procSteps[0].msg;
            _state       = ScanState.Processing;
        }
    }

    private void DrawProcessingOverlay(float dw, float dh)
    {
        // Full-screen dark overlay
        GUI.DrawTexture(new Rect(0, 0, dw, dh), _texDark);

        // Main status message — centred vertically with a slight upward bias
        float labelW = dw * 0.85f;
        float labelH = 200f;
        float labelX = (dw - labelW) * 0.5f;
        float labelY = dh * 0.40f;

        GUI.Label(new Rect(labelX, labelY, labelW, labelH), _procMessage, _styleLabel);

        // Abort button
        float abortW = 320f;
        float abortH = 100f;
        float abortX = (dw - abortW) * 0.5f;
        float abortY = dh * 0.62f;

        if (GUI.Button(new Rect(abortX, abortY, abortW, abortH), "Abort", _styleSmallBtn))
        {
            ResetToIdle();
        }
    }

    private void DrawCompleteOverlay(float dw, float dh)
    {
        // Full-screen dark overlay
        GUI.DrawTexture(new Rect(0, 0, dw, dh), _texDark);

        float labelW = dw * 0.85f;
        float labelH = 200f;
        float labelX = (dw - labelW) * 0.5f;
        float labelY = dh * 0.40f;

        GUI.Label(new Rect(labelX, labelY, labelW, labelH), "Scan Complete.", _styleLabel);

        // Close button
        float closeW = 320f;
        float closeH = 100f;
        float closeX = (dw - closeW) * 0.5f;
        float closeY = dh * 0.58f;

        if (GUI.Button(new Rect(closeX, closeY, closeW, closeH), "Close", _styleSmallBtn))
        {
            ResetToIdle();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void ResetToIdle()
    {
        _state       = ScanState.Idle;
        _recElapsed  = 0f;
        _dotTimer    = 0f;
        _dotVisible  = true;
        _procElapsed = 0f;
        _procMessage = _procSteps[0].msg;
    }

    /// <summary>
    /// Build GUIStyles once. Called from OnGUI so GUI skin is ready.
    /// Font sizes are relative to DESIGN_HEIGHT so they stay sharp on any device.
    /// </summary>
    private void BuildStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        // Base font size relative to the design canvas height (1920)
        // These map to DESIGN-space pixels, which are later scaled uniformly.
        int largeBtnFontSize = Mathf.RoundToInt(DESIGN_HEIGHT * 0.040f); // ~77 px
        int labelFontSize    = Mathf.RoundToInt(DESIGN_HEIGHT * 0.038f); // ~73 px
        int smallBtnFontSize = Mathf.RoundToInt(DESIGN_HEIGHT * 0.028f); // ~54 px
        int recFontSize      = Mathf.RoundToInt(DESIGN_HEIGHT * 0.030f); // ~58 px

        // ── Large action button ───────────────────────────────────────────────
        _styleLargeBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize  = largeBtnFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _styleLargeBtn.normal.background   = _texGray;
        _styleLargeBtn.hover.background    = _texGray;
        _styleLargeBtn.active.background   = _texGray;
        _styleLargeBtn.normal.textColor    = Color.white;
        _styleLargeBtn.hover.textColor     = Color.white;
        _styleLargeBtn.active.textColor    = Color.white;
        _styleLargeBtn.border              = new RectOffset(12, 12, 12, 12);
        _styleLargeBtn.padding             = new RectOffset(20, 20, 20, 20);

        // ── Overlay label ─────────────────────────────────────────────────────
        _styleLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize  = labelFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = true
        };
        _styleLabel.normal.textColor = Color.white;

        // ── Small button (Abort / Close) ──────────────────────────────────────
        _styleSmallBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize  = smallBtnFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _styleSmallBtn.normal.background  = _texGray;
        _styleSmallBtn.hover.background   = _texGray;
        _styleSmallBtn.active.background  = _texGray;
        _styleSmallBtn.normal.textColor   = Color.white;
        _styleSmallBtn.hover.textColor    = Color.white;
        _styleSmallBtn.active.textColor   = Color.white;
        _styleSmallBtn.border             = new RectOffset(8, 8, 8, 8);
        _styleSmallBtn.padding            = new RectOffset(14, 14, 14, 14);

        // ── REC indicator label ───────────────────────────────────────────────
        _styleRecLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize  = recFontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        _styleRecLabel.normal.textColor = Color.white;
    }

    /// <summary>Creates a 4×4 solid-color Texture2D (small footprint, tiling-safe).</summary>
    private static Texture2D MakeTex(Color c)
    {
        Color[] pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        Texture2D tex = new Texture2D(4, 4, TextureFormat.ARGB32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
