using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// Bandwidth-aware model loading (V3 Phase 13): which version of a model to show, and when to
    /// fetch the full one. SpaceSceneManager does the loading; this decides and keeps the state the
    /// "Preview quality" pill shows.
    ///
    ///   - The full (mobile) file already on the phone: it opens straight away, no preview.
    ///   - Otherwise, if the catalogue lists a preview (small textures, fewer triangles, the SAME
    ///     part tree), the preview downloads and shows first, and its download speed is measured.
    ///   - Then the full file: at once on Wi-Fi, or on mobile data when it is small (8 MB or less)
    ///     or the connection is fast (1.5 MB/s or more); otherwise only when the person taps GET
    ///     FULL. **Save data** (a setting on the pill's card) keeps the preview until GET FULL.
    ///   - The full file swaps in where the preview was, and every feature carries on (state is kept
    ///     by part path, and the model's key - its full URL - never changes).
    ///   - Offline with only the preview downloaded before: the preview opens, marked offline.
    /// </summary>
    public static class ModelVariants
    {
        public enum Stage { Full, Preview, DownloadingFull, Swapping }

        private const string SaveDataKey = "AuraSync.SaveData";
        private const string PreviewForKey = "AuraSync.PreviewFor.";
        private const long SmallEnough = 8L * 1024 * 1024;
        private const double FastEnough = 1.5 * 1024 * 1024; // bytes per second

        public static Stage Current { get; private set; } = Stage.Full;
        public static long FullBytes { get; private set; }
        public static long PreviewBytes { get; private set; }
        public static float FullProgress { get; set; }
        public static bool Offline { get; private set; }
        public static string Note { get; private set; } = "";
        public static event Action OnChanged;

        /// <summary>Set by SpaceSceneManager: start downloading the full file now (GET FULL).</summary>
        public static Action RequestFull;

        public static bool SaveData
        {
            get => PlayerPrefs.GetInt(SaveDataKey, 0) == 1;
            set { PlayerPrefs.SetInt(SaveDataKey, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        public static bool ShowingPreview => Current != Stage.Full;

        // ── Which preview goes with which model, remembered for offline ───────

        public static void RememberPreview(string fullUrl, string previewUrl)
        {
            if (string.IsNullOrEmpty(fullUrl) || string.IsNullOrEmpty(previewUrl)) return;
            PlayerPrefs.SetString(PreviewForKey + ModelStateMemory.ModelHash(fullUrl), previewUrl);
        }

        public static string RememberedPreview(string fullUrl) =>
            PlayerPrefs.GetString(PreviewForKey + ModelStateMemory.ModelHash(fullUrl ?? ""), "");

        // ── The load ───────────────────────────────────────────────────────────

        private static float _loadStart;
        private static string _model = "";
        private static long _downloaded;
        private static float _downloadSeconds;
        private static bool _fromCache;
        private static bool _previewLoad;

        public static void LoadStarted(string fullUrl)
        {
            _loadStart = Time.realtimeSinceStartup;
            _model = fullUrl ?? "";
            _downloaded = 0; _downloadSeconds = 0f; _fromCache = true; _previewLoad = false;
            Current = Stage.Full; Offline = false; Note = ""; FullProgress = 0f; FullBytes = 0; PreviewBytes = 0;
            OnChanged?.Invoke();
        }

        public static void UsePreview(long previewBytes, long fullBytes, bool offline)
        {
            _previewLoad = true;
            PreviewBytes = previewBytes;
            FullBytes = fullBytes;
            Offline = offline;
            Current = Stage.Preview;
            OnChanged?.Invoke();
        }

        public static void Downloaded(long bytes, float seconds)
        {
            _downloaded = bytes;
            _downloadSeconds = Mathf.Max(0.001f, seconds);
            _fromCache = false;
        }

        /// <summary>Bytes a second the first download managed (0 = it came from the cache).</summary>
        public static double Throughput => _fromCache ? 0 : _downloaded / (double)_downloadSeconds;

        /// <summary>The first version is on screen and usable.</summary>
        public static void Shown()
        {
            LoadLog.Write(_model, _previewLoad ? "preview" : "mobile", _fromCache, _downloaded, _downloadSeconds,
                          Time.realtimeSinceStartup - _loadStart, 0f);
        }

        /// <summary>Fetch the full file now (true), or wait for GET FULL (false, with the reason in <see cref="Note"/>).</summary>
        public static bool ShouldFetchFullNow()
        {
            if (Offline) { Note = "offline"; return false; }
            if (SaveData) { Note = "saving data"; return false; }
            switch (Application.internetReachability)
            {
                case NetworkReachability.NotReachable:
                    Note = "offline"; Offline = true; return false;
                case NetworkReachability.ReachableViaLocalAreaNetwork:
                    Note = "Wi-Fi"; return true;
                default:
                    if (FullBytes > 0 && FullBytes <= SmallEnough) { Note = "small download"; return true; }
                    if (Throughput >= FastEnough) { Note = "fast connection"; return true; }
                    Note = "mobile data";
                    return false;
            }
        }

        public static void DownloadingFull()
        {
            _fullStart = Time.realtimeSinceStartup;
            Current = Stage.DownloadingFull;
            FullProgress = 0f;
            OnChanged?.Invoke();
        }

        public static void FullFailed(string error)
        {
            Current = Stage.Preview;
            Note = "full download failed: " + error;
            OnChanged?.Invoke();
        }

        private static float _fullStart;

        public static void Swapping() { Current = Stage.Swapping; OnChanged?.Invoke(); }

        /// <summary>The full version replaced the preview.</summary>
        public static void Swapped(long fullBytes)
        {
            LoadLog.Write(_model, "mobile-after-preview", false, fullBytes, Time.realtimeSinceStartup - _fullStart,
                          0f, Time.realtimeSinceStartup - _loadStart);
            Current = Stage.Full;
            OnChanged?.Invoke();
        }

        public static void Changed() => OnChanged?.Invoke();

        public static string Network =>
            Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork ? "wifi"
            : Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork ? "mobile" : "offline";
    }

    /// <summary>
    /// The paper's loading numbers (<c>LoadLog.jsonl</c>): time to first render (the model on
    /// screen and usable) for the preview and for the full mobile file, and time until the full
    /// file replaced the preview, with the network type and download speed.
    /// </summary>
    public static class LoadLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "LoadLog.jsonl");

        [Serializable]
        private class Line
        {
            public string time, model, variant, network;
            public bool fromCache;
            public long bytes;
            public float downloadSec, firstRenderSec, fullSec, kbPerSec;
        }

        public static void Write(string model, string variant, bool fromCache, long bytes, float downloadSec, float firstRenderSec, float fullSec)
        {
            var l = new Line
            {
                time = DateTime.UtcNow.ToString("o"), model = Path.GetFileName(model ?? ""), variant = variant,
                network = ModelVariants.Network, fromCache = fromCache, bytes = bytes, downloadSec = downloadSec,
                firstRenderSec = firstRenderSec, fullSec = fullSec,
                kbPerSec = downloadSec > 0f && !fromCache ? bytes / 1024f / downloadSec : 0f,
            };
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(l) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[LoadLog] {ex.Message}"); }
            Debug.Log($"[LoadLog] {variant} {l.model}: first render {firstRenderSec:0.0} s, full {fullSec:0.0} s, {bytes / 1024} KB on {l.network}{(fromCache ? " (cached)" : "")}.");
        }

        public static string Summary()
        {
            if (!File.Exists(FilePath)) return "Loading: no measurements yet.";
            var lines = new List<Line>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { var l = JsonUtility.FromJson<Line>(raw); if (l != null) lines.Add(l); } catch { /* skip */ }
            }
            string Median(IEnumerable<float> xs)
            {
                var v = xs.OrderBy(x => x).ToList();
                return v.Count == 0 ? "n/a" : $"{v[v.Count / 2]:0.0} s (n={v.Count})";
            }
            var fresh = lines.Where(l => !l.fromCache).ToList();
            return "Loading (downloads, not cache): first render with preview " +
                   Median(fresh.Where(l => l.variant == "preview").Select(l => l.firstRenderSec)) +
                   ", with the full file " + Median(fresh.Where(l => l.variant == "mobile").Select(l => l.firstRenderSec)) +
                   "; full file in place after a preview " + Median(lines.Where(l => l.variant == "mobile-after-preview").Select(l => l.fullSec)) +
                   $". File: {FilePath}";
        }
    }
}
