using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using GLTFast;

namespace AuraSync.Space
{
    /// <summary>
    /// Real preview pictures for the model list. Each model is loaded once, off-screen, framed by a
    /// small camera and saved as a PNG on the phone, so later launches show it straight away.
    ///
    /// The lightest file available is used: the model's preview GLB (Phase 13) when it has one, then
    /// a catalogue entry's preview with the same model URL (scans point at catalogue models), then
    /// the full model if it is already downloaded or reasonably small. One model renders at a time,
    /// in the background, so the list never waits for it.
    ///
    /// Files: persistentDataPath/Thumbnails/&lt;md5 of the model URL&gt;.png
    /// </summary>
    public class ModelThumbnails : MonoBehaviour
    {
        private const int  OutSize        = 256;
        private const int  RenderSize     = 512;              // rendered at 2x, then downsampled: smooth edges
        private const long MaxFullModel   = 40L * 1024 * 1024; // never download a bigger model just for a picture
        private static readonly Vector3 StagePosition = new Vector3(0f, -5000f, 0f);

        private static ModelThumbnails _instance;
        private static readonly Dictionary<string, Texture2D> _memory = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> _failed = new HashSet<string>();

        private class Job
        {
            public string key;
            public ModelData model;
            public readonly List<Action<Texture2D>> callbacks = new List<Action<Texture2D>>();
        }

        private readonly Queue<Job> _queue = new Queue<Job>();
        private readonly Dictionary<string, Job> _pending = new Dictionary<string, Job>();
        private bool _busy;

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// What a model's picture is filed under: its URL, plus its opening turn when it has one
        /// (ModelData.openRotation), so a model given a new turn gets a fresh picture rather than
        /// the one saved on the phone before.
        /// </summary>
        public static string PictureId(ModelData m)
        {
            if (m == null) return null;
            float[] r = m.openRotation;
            return r != null && r.Length == 3 ? $"{m.glbUrl}#open{r[0]:0},{r[1]:0},{r[2]:0}" : m.glbUrl;
        }

        /// <summary>The saved picture for this model URL (or <see cref="PictureId"/>), or null if none has been made yet.</summary>
        public static Texture2D TryGet(string glbUrl)
        {
            if (string.IsNullOrEmpty(glbUrl)) return null;
            string key = KeyFor(glbUrl);
            if (_memory.TryGetValue(key, out Texture2D t) && t != null) return t;

            string path = PathFor(key);
            if (!File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "Thumb_" + key };
                if (!tex.LoadImage(File.ReadAllBytes(path))) { Destroy(tex); return null; }
                tex.wrapMode = TextureWrapMode.Clamp;
                _memory[key] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModelThumbnails] Could not read {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calls back with the model's picture: at once if it exists, otherwise after it is rendered
        /// (or with null if it cannot be made, e.g. offline and not downloaded).
        /// </summary>
        public static void Request(ModelData model, Action<Texture2D> onReady)
        {
            if (model == null || string.IsNullOrEmpty(model.glbUrl)) { onReady?.Invoke(null); return; }

            Texture2D have = TryGet(PictureId(model));
            if (have != null) { onReady?.Invoke(have); return; }

            string key = KeyFor(PictureId(model));
            if (_failed.Contains(key)) { onReady?.Invoke(null); return; }

            var self = Ensure();
            if (self._pending.TryGetValue(key, out Job existing))
            {
                if (onReady != null) existing.callbacks.Add(onReady);
                return;
            }

            var job = new Job { key = key, model = model };
            if (onReady != null) job.callbacks.Add(onReady);
            self._pending[key] = job;
            self._queue.Enqueue(job);
            self.Pump();
        }

        /// <summary>Deletes a model's picture (when the model itself is deleted).</summary>
        public static void Forget(string glbUrl)
        {
            if (string.IsNullOrEmpty(glbUrl)) return;
            string key = KeyFor(glbUrl);
            if (_memory.TryGetValue(key, out Texture2D t) && t != null) Destroy(t);
            _memory.Remove(key);
            _failed.Remove(key);
            try { if (File.Exists(PathFor(key))) File.Delete(PathFor(key)); } catch { /* best effort */ }
        }

        // ── Queue ──────────────────────────────────────────────────────

        private static ModelThumbnails Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("AuraSync_ModelThumbnails");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ModelThumbnails>();
            return _instance;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private async void Pump()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                while (_queue.Count > 0 && this != null)
                {
                    Job job = _queue.Dequeue();
                    Texture2D tex = null;
                    try { tex = await Render(job.model, job.key); }
                    catch (Exception ex) { Debug.LogWarning($"[ModelThumbnails] {job.model.name}: {ex.Message}"); }

                    if (this == null) return;
                    _pending.Remove(job.key);
                    if (tex == null) _failed.Add(job.key);

                    foreach (var cb in job.callbacks)
                    {
                        try { cb?.Invoke(tex); }
                        catch (Exception ex) { Debug.LogWarning($"[ModelThumbnails] Callback failed: {ex.Message}"); }
                    }
                    await Task.Yield(); // let a frame pass between models
                }
            }
            finally { _busy = false; }
        }

        // ── Rendering ──────────────────────────────────────────────────

        private async Task<Texture2D> Render(ModelData model, string key)
        {
            // 1. The lightest file we can get.
            var sources = new List<string>();
            if (!string.IsNullOrEmpty(model.previewUrl)) sources.Add(model.previewUrl);

            ModelData entry = await FindCatalogueEntry(model.glbUrl);
            if (this == null) return null;
            if (entry != null && !string.IsNullOrEmpty(entry.previewUrl) && !sources.Contains(entry.previewUrl))
                sources.Add(entry.previewUrl);
            long fullBytes = model.glbBytes > 0 ? model.glbBytes : (entry != null ? entry.glbBytes : 0);
            sources.Add(model.glbUrl);

            byte[] bytes = null; string from = null;
            foreach (string src in sources)
            {
                bool isFull = src == model.glbUrl;
                bytes = await Fetch(src, isFull ? fullBytes : 0, allowDownload: !isFull || fullBytes <= MaxFullModel);
                if (this == null) return null;
                if (bytes != null) { from = src; break; }
            }
            if (bytes == null) return null;

            // 2. Load it off-screen.
            var gltf = new GltfImport();
            GameObject stage = null, camGo = null, keyLight = null, fillLight = null;
            RenderTexture big = null, small = null;
            try
            {
                Uri uri;
                try { uri = new Uri(from); } catch { uri = null; }
                bool ok = await gltf.Load(bytes, uri);
                if (this == null || !ok) return null;

                stage = new GameObject("ThumbnailStage");
                stage.transform.position = StagePosition;
                ok = await gltf.InstantiateMainSceneAsync(stage.transform);
                if (this == null || !ok) return null;

                var helper = stage.AddComponent<GLTFastMaterialHelper>();
                helper.FixAllMaterials(stage.transform);

                // Turned the way it opens in the viewer (the watch's file faces its case back forward).
                float[] open = model.openRotation != null && model.openRotation.Length == 3 ? model.openRotation
                             : entry != null ? entry.openRotation : null;
                if (open != null && open.Length == 3) stage.transform.rotation = Quaternion.Euler(open[0], open[1], open[2]);

                int layer = FreeLayer();
                foreach (Transform t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                var renderers = stage.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                float radius = Mathf.Max(0.0001f, b.extents.magnitude);

                // 3. Frame it: three-quarter view from slightly above.
                const float fov = 28f;
                Quaternion look = Quaternion.Euler(22f, -35f, 0f);
                float dist = radius / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 1.05f;

                camGo = new GameObject("ThumbnailCamera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 1 << layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = AuraSync.UI.AuraTheme.Ink;
                cam.fieldOfView = fov;
                cam.nearClipPlane = Mathf.Max(0.001f, dist - radius * 1.5f);
                cam.farClipPlane = dist + radius * 1.5f;
                cam.transform.rotation = look;
                cam.transform.position = b.center - look * Vector3.forward * dist;

                keyLight = MakeLight("ThumbnailKey", Quaternion.Euler(40f, -30f, 0f), 1.15f, layer);
                fillLight = MakeLight("ThumbnailFill", Quaternion.Euler(20f, 150f, 0f), 0.45f, layer);

                big = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGB32);
                small = new RenderTexture(OutSize, OutSize, 0, RenderTextureFormat.ARGB32);
                big.Create(); small.Create();
                cam.targetTexture = big;
                cam.Render();
                cam.targetTexture = null;
                Graphics.Blit(big, small);

                var prev = RenderTexture.active;
                RenderTexture.active = small;
                var tex = new Texture2D(OutSize, OutSize, TextureFormat.RGB24, false) { name = "Thumb_" + key };
                tex.ReadPixels(new Rect(0, 0, OutSize, OutSize), 0, 0);
                tex.Apply();
                tex.wrapMode = TextureWrapMode.Clamp;
                RenderTexture.active = prev;

                // 4. Keep it.
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(PathFor(key)));
                    File.WriteAllBytes(PathFor(key), tex.EncodeToPNG());
                }
                catch (Exception ex) { Debug.LogWarning($"[ModelThumbnails] Could not save: {ex.Message}"); }

                _memory[key] = tex;
                Debug.Log($"[ModelThumbnails] Made a picture for '{model.name}' from {from}");
                return tex;
            }
            finally
            {
                if (stage != null) Destroy(stage);
                if (camGo != null) Destroy(camGo);
                if (keyLight != null) Destroy(keyLight);
                if (fillLight != null) Destroy(fillLight);
                if (big != null) { big.Release(); Destroy(big); }
                if (small != null) { small.Release(); Destroy(small); }
                gltf.Dispose();
            }
        }

        private static GameObject MakeLight(string name, Quaternion rot, float intensity, int layer)
        {
            var go = new GameObject(name);
            go.transform.rotation = rot;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.color = Color.white;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << layer;
            return go;
        }

        /// <summary>
        /// The model's bytes from this phone's model cache, the app bundle, or the network.
        /// Returns null when it cannot be had (offline, too big, missing).
        /// </summary>
        private async Task<byte[]> Fetch(string key, long knownBytes, bool allowDownload)
        {
            string fullPath;
            try { SpaceSceneManager.ResolveModel(key, out fullPath, out _); }
            catch { return null; }

            bool remote = fullPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          fullPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (remote)
            {
                string cached = SpaceSceneManager.CachePathFor(fullPath);
                if (!string.IsNullOrEmpty(cached) && File.Exists(cached))
                {
                    try { return File.ReadAllBytes(cached); } catch { return null; }
                }
                if (!allowDownload || Application.internetReachability == NetworkReachability.NotReachable)
                    return null;
            }
            else if (!fullPath.Contains("://"))
            {
                try { return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null; } catch { return null; }
            }

            // Remote, or inside the APK (jar:file://), both read through a web request.
            using (var req = UnityWebRequest.Get(fullPath))
            {
                req.timeout = 90;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (this == null) { req.Abort(); return null; }
                    if (req.downloadedBytes > (ulong)MaxFullModel + 1) { req.Abort(); return null; }
                    await Task.Yield();
                }
                if (req.result != UnityWebRequest.Result.Success) return null;
                return req.downloadHandler.data;
            }
        }

        private static Task<ModelData> FindCatalogueEntry(string glbUrl)
        {
            var tcs = new TaskCompletionSource<ModelData>();
            if (ModelCatalog.Instance == null) { tcs.SetResult(null); return tcs.Task; }
            ModelCatalog.Instance.FindByGlbUrl(glbUrl, m => tcs.TrySetResult(m));
            return tcs.Task;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static int _layer = -1;

        /// <summary>An unused layer, so the thumbnail camera sees only the model being pictured.</summary>
        private static int FreeLayer()
        {
            if (_layer >= 0) return _layer;
            for (int i = 31; i >= 8; i--)
            {
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { _layer = i; return i; }
            }
            _layer = 31;
            return _layer;
        }

        private static string KeyFor(string glbUrl)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(glbUrl.Trim()));
                var sb = new System.Text.StringBuilder();
                foreach (byte x in h) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string PathFor(string key) =>
            Path.Combine(Application.persistentDataPath, "Thumbnails", key + ".png");
    }
}
