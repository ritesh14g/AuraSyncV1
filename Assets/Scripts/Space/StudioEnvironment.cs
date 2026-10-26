using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using AuraSync.UI;

namespace AuraSync.Space
{
    /// <summary>
    /// The 3D viewer's studio: a dark backdrop with a soft glow behind the model, a fine grid floor
    /// that fades into the distance (one rose line marks the axis), a soft contact shadow, and
    /// lighting that adapts to each model so the model is always what stands out.
    ///
    /// How it adapts: when a model loads, the average colour and brightness of its materials
    /// (colour factor x texture average, weighted by part size) set
    ///   * the backdrop - lifted for a dark model, deepest ink for a bright one;
    ///   * the key light - stronger for a dark model, softer for a bright one;
    ///   * the rim light behind the model - lavender for dark models, cool for bright ones, near
    ///     neutral for colourful ones so their own colours are not tinted;
    ///   * the ambient fill.
    ///
    /// Only built-in shaders that always ship with the app are used (Sprites/Default), and no
    /// collider is added, so taps and part picking are unaffected. The key light is the scene's own
    /// sun, so the House "time of day" simulation still drives it.
    /// </summary>
    public class StudioEnvironment : MonoBehaviour
    {
        public static StudioEnvironment Instance { get; private set; }

        private const float FloorSize   = 24f;  // world units across
        private const float CellSize    = 0.5f; // grid cell, in world units (the model is ~2 units)
        private const int   FloorTex    = 1024;

        private Camera _cam;
        private Light _key, _rim;
        private Transform _backdrop, _floor, _shadow;
        private Material _backdropMat, _floorMat, _shadowMat;
        private Texture2D _backdropTex;
        private readonly Dictionary<Texture, Color> _texAverage = new Dictionary<Texture, Color>();

        // The look for an average model, until the first model is measured.
        // One colour top to bottom (the user asked for no separate floor colour), with a soft glow
        // behind the model; the floor itself is see-through apart from its faint grid and the shadow.
        private Color _glow = AuraTheme.Hex(0x1B2147), _top = AuraTheme.Hex(0x0E1330), _bottom = AuraTheme.Hex(0x0E1330);

        public static StudioEnvironment EnsureExists()
        {
            if (Instance != null) return Instance;
            return new GameObject("AuraSync_Studio").AddComponent<StudioEnvironment>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_backdropTex != null) Destroy(_backdropTex);
        }

        void LateUpdate()
        {
            // The backdrop always fills the view, whatever the screen shape.
            if (_cam == null || _backdrop == null) return;
            float d = _backdrop.localPosition.z;
            float h = 2f * d * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            float w = h * Mathf.Max(0.3f, _cam.aspect);
            var s = new Vector3(w, h, 1f);
            if (_backdrop.localScale != s) _backdrop.localScale = s;
        }

        // ── Build ──────────────────────────────────────────────────────

        private void Build()
        {
            _cam = Camera.main;
            if (_cam == null) { Debug.LogWarning("[Studio] No main camera."); return; }

            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = _bottom;
            if (_cam.farClipPlane < 120f) _cam.farClipPlane = 120f;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            // SpaceScene's own grey floor plane would hide the studio floor and split the screen into
            // two colours. Only its look goes: its collider stays for anything that raycasts it.
            var sceneFloor = GameObject.Find("Floor");
            if (sceneFloor != null && sceneFloor.TryGetComponent(out Renderer floorRenderer)) floorRenderer.enabled = false;

            Shader sprite = Shader.Find("Sprites/Default");
            if (sprite == null) { Debug.LogWarning("[Studio] Sprites/Default missing; studio skipped."); return; }

            // Backdrop: a camera-facing quad far behind everything, drawn first.
            _backdropMat = new Material(sprite) { renderQueue = (int)RenderQueue.Background };
            _backdrop = Quad("StudioBackdrop", _backdropMat).transform;
            _backdrop.SetParent(_cam.transform, false);
            _backdrop.localPosition = new Vector3(0, 0, 100f);
            _backdrop.localRotation = Quaternion.identity;
            RebuildBackdrop();

            // Floor: one grid texture with the fade baked in, flat on y = 0.
            _floorMat = new Material(sprite) { mainTexture = MakeFloorTexture(), renderQueue = (int)RenderQueue.Transparent - 10 };
            _floor = Quad("StudioFloor", _floorMat).transform;
            _floor.SetParent(transform, false);
            _floor.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _floor.localScale = new Vector3(FloorSize, FloorSize, 1f);
            _floor.position = new Vector3(0f, -0.002f, 0f);

            // Contact shadow: a soft dark blob under the model.
            _shadowMat = new Material(sprite) { mainTexture = MakeShadowTexture(), renderQueue = (int)RenderQueue.Transparent - 9 };
            _shadow = Quad("StudioShadow", _shadowMat).transform;
            _shadow.SetParent(transform, false);
            _shadow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _shadow.position = new Vector3(0f, 0.001f, 0f);
            _shadow.gameObject.SetActive(false);

            // Key = the scene's sun (the House simulation drives it); rim = ours, from behind.
            _key = RenderSettings.sun;
            if (_key == null)
                foreach (var l in FindObjectsOfType<Light>())
                    if (l.type == LightType.Directional) { _key = l; break; }
            if (_key == null)
            {
                _key = new GameObject("StudioKey").AddComponent<Light>();
                _key.type = LightType.Directional;
            }
            RenderSettings.sun = _key;
            _key.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            _key.color = new Color(1f, 0.97f, 0.93f);
            _key.intensity = 1.15f;

            _rim = new GameObject("StudioRim").AddComponent<Light>();
            _rim.transform.SetParent(transform, false);
            _rim.type = LightType.Directional;
            _rim.shadows = LightShadows.None;
            _rim.transform.rotation = Quaternion.Euler(18f, 160f, 0f);
            _rim.intensity = 0.7f;
            _rim.color = AuraTheme.Lavender;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.31f, 0.38f);
        }

        private static GameObject Quad(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // ── Textures ───────────────────────────────────────────────────

        private void RebuildBackdrop()
        {
            const int w = 64, h = 128;
            if (_backdropTex == null)
                _backdropTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = y / (h - 1f);
                Color c = Color.Lerp(_bottom, _top, Mathf.SmoothStep(0f, 1f, v));
                // The glow sits a little below the middle, where the model is.
                float dx = (x / (w - 1f) - 0.5f) * 1.2f, dy = v - 0.45f;
                float g = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy * 1.6f) / 0.75f);
                c = Color.Lerp(c, _glow, g * g);
                px[y * w + x] = c;
            }
            _backdropTex.SetPixels32(px);
            _backdropTex.Apply(false);
            _backdropMat.mainTexture = _backdropTex;
            if (_cam != null) _cam.backgroundColor = _bottom;
        }

        private static Texture2D MakeFloorTexture()
        {
            var tex = new Texture2D(FloorTex, FloorTex, TextureFormat.RGBA32, true)
            {
                name = "StudioGrid", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                anisoLevel = 8, hideFlags = HideFlags.DontSave
            };
            var px = new Color32[FloorTex * FloorTex];
            float cellPx = FloorTex / (FloorSize / CellSize);
            Color line = AuraTheme.Lavender, axis = AuraTheme.Rose;
            float half = FloorTex / 2f;

            for (int y = 0; y < FloorTex; y++)
            for (int x = 0; x < FloorTex; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                // Distance to the nearest grid line on each axis, in pixels.
                float gx = Mathf.Abs(Mathf.Repeat(fx - half + cellPx * 0.5f, cellPx) - cellPx * 0.5f);
                float gy = Mathf.Abs(Mathf.Repeat(fy - half + cellPx * 0.5f, cellPx) - cellPx * 0.5f);
                int ix = Mathf.RoundToInt((fx - half) / cellPx), iy = Mathf.RoundToInt((fy - half) / cellPx);
                bool majorX = ix % 4 == 0, majorY = iy % 4 == 0;

                float ax = Mathf.Clamp01(1.1f - gx) * (majorX ? 0.34f : 0.17f);
                float ay = Mathf.Clamp01(1.1f - gy) * (majorY ? 0.34f : 0.17f);
                float a = Mathf.Max(ax, ay);
                Color c = line;

                // The axis: one rose line along x (the floor's centre row).
                float ad = Mathf.Abs(fy - half);
                float axisA = Mathf.Clamp01(1.6f - ad) * 0.7f;
                if (axisA > a) { a = axisA; c = axis; }

                // Fade towards the edges so the floor melts into the backdrop.
                float r = new Vector2(fx - half, fy - half).magnitude / half;
                a *= 1f - Mathf.SmoothStep(0.25f, 1f, r);

                c.a = a;
                px[y * FloorTex + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D MakeShadowTexture()
        {
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = new Vector2(x + 0.5f - s / 2f, y + 0.5f - s / 2f).magnitude / (s / 2f);
                float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f) * 0.7f;
                px[y * s + x] = new Color(0f, 0f, 0.02f, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ── Adapting to the model ──────────────────────────────────────

        /// <summary>Call once a model is placed (and again when its full version replaces the preview).</summary>
        public void OnModelLoaded(Transform modelRoot)
        {
            if (modelRoot == null || _floorMat == null) return;

            var renderers = modelRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            // Floor and shadow sit right under the model, so it stands on the floor, not above it.
            float groundY = b.min.y;
            _floor.position = new Vector3(b.center.x, groundY - 0.004f, b.center.z);
            float foot = Mathf.Max(b.size.x, b.size.z) * 1.2f;
            _shadow.position = new Vector3(b.center.x, groundY - 0.002f, b.center.z);
            _shadow.localScale = new Vector3(foot, foot, 1f);
            _shadow.gameObject.SetActive(true);

            Color albedo = AverageAlbedo(renderers);
            Adapt(albedo);
        }

        private void Adapt(Color albedo)
        {
            float lum = 0.299f * albedo.r + 0.587f * albedo.g + 0.114f * albedo.b;
            float max = Mathf.Max(albedo.r, Mathf.Max(albedo.g, albedo.b));
            float min = Mathf.Min(albedo.r, Mathf.Min(albedo.g, albedo.b));
            float sat = max > 0.001f ? (max - min) / max : 0f;

            // 0 = dark model, 1 = bright model.
            float t = Mathf.InverseLerp(0.18f, 0.65f, lum);
            float colourful = Mathf.InverseLerp(0.25f, 0.55f, sat);

            _glow   = Color.Lerp(AuraTheme.Hex(0x2C3462), AuraTheme.Hex(0x141A3A), t);
            _top    = Color.Lerp(AuraTheme.Hex(0x141A3E), AuraTheme.Hex(0x0A0E24), t);
            _bottom = _top; // one colour throughout
            // A colourful model gets a more neutral backdrop, so its colours read true.
            Color grey = new Color(0.08f, 0.085f, 0.11f);
            _glow = Color.Lerp(_glow, Color.Lerp(grey, _glow, 0.5f), colourful * 0.6f);
            RebuildBackdrop();

            if (_key != null)
            {
                _key.intensity = Mathf.Lerp(1.4f, 0.95f, t);
                _key.color = Color.Lerp(new Color(1f, 0.96f, 0.9f), Color.white, colourful);
            }
            if (_rim != null)
            {
                Color rimCol = Color.Lerp(AuraTheme.Lavender, new Color(0.45f, 0.56f, 0.86f), t);
                _rim.color = Color.Lerp(rimCol, new Color(0.82f, 0.82f, 0.86f), colourful);
                _rim.intensity = Mathf.Lerp(1.0f, 0.6f, t) * Mathf.Lerp(1f, 0.6f, colourful);
            }
            RenderSettings.ambientLight = Color.Lerp(new Color(0.36f, 0.37f, 0.46f), new Color(0.22f, 0.23f, 0.30f), t);

            Debug.Log($"[Studio] Model albedo {albedo} (brightness {lum:0.00}, saturation {sat:0.00}) → backdrop {(t < 0.4f ? "lifted" : t > 0.7f ? "deep" : "mid")}");
        }

        /// <summary>The model's average surface colour: each material's colour x its texture average, weighted by part size.</summary>
        private Color AverageAlbedo(Renderer[] renderers)
        {
            Color sum = Color.black;
            float weight = 0f;
            int sampled = 0;

            foreach (var r in renderers)
            {
                if (r == null || !r.enabled) continue;
                Vector3 s = r.bounds.size;
                float w = s.x * s.y + s.y * s.z + s.x * s.z + 1e-4f;

                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    Color c = Color.white;
                    if (m.HasProperty("baseColorFactor")) c = m.GetColor("baseColorFactor");
                    else if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                    else if (m.HasProperty("_Color")) c = m.GetColor("_Color");

                    Texture tex = null;
                    if (m.HasProperty("baseColorTexture")) tex = m.GetTexture("baseColorTexture");
                    if (tex == null && m.HasProperty("_MainTex")) tex = m.GetTexture("_MainTex");
                    if (tex != null && (sampled < 32 || _texAverage.ContainsKey(tex)))
                    {
                        if (!_texAverage.ContainsKey(tex)) sampled++;
                        c *= TextureAverage(tex);
                    }

                    sum += new Color(c.r, c.g, c.b) * w;
                    weight += w;
                }
            }
            if (weight <= 0f) return new Color(0.5f, 0.5f, 0.5f);
            Color avg = sum / weight;
            avg.a = 1f;
            return avg;
        }

        /// <summary>A texture's average colour, read through a tiny render texture (works on unreadable textures too).</summary>
        private Color TextureAverage(Texture tex)
        {
            if (_texAverage.TryGetValue(tex, out Color cached)) return cached;
            Color avg = Color.white;
            RenderTexture rt = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var read = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                read.Apply(false);
                Color s = Color.black;
                foreach (var p in read.GetPixels()) s += p;
                avg = s / 64f;
                avg.a = 1f;
                Destroy(read);
            }
            catch (System.Exception ex) { Debug.LogWarning($"[Studio] Could not sample {tex.name}: {ex.Message}"); }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            _texAverage[tex] = avg;
            return avg;
        }
    }
}
