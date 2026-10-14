using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The app's model catalogue, served from the Hugging Face dataset repo that already holds the
/// .glb files.
///
/// This replaces the old FirestoreManager. Firestore was holding a five-field table of roughly a
/// dozen rows, writable and deletable by anyone who read the APK, for a catalogue that only ever
/// changes when we upload a new model by hand. A `models.json` sitting next to the .glb files does
/// the same job with no backend, no credentials and no second service to keep alive.
///
/// There are two layers:
///   * the **curated catalogue** - `models.json` on Hugging Face, read-only, identical for everyone;
///   * a **local overlay** - scans produced by the KIRI mock pipeline, plus catalogue entries the
///     user has hidden. Both live in PlayerPrefs on the one device.
///
/// The overlay exists because the catalogue cannot be written to from the app: a Hugging Face repo
/// is a git repo, so a write needs a token, and a token shipped inside an APK is a token anyone can
/// extract. Keeping user-made entries on the device is the honest version of that constraint - it
/// is also what they already were in practice, since those scans are mock data.
/// </summary>
public class ModelCatalog : MonoBehaviour
{
    public static ModelCatalog Instance;

    /// <summary>
    /// Where the catalogue lives. Swapping hosts later (Cloudflare R2, a CDN, anything) is this
    /// line plus the `baseUrl` inside the manifest - no other script knows where models come from.
    /// </summary>
    public const string DefaultManifestUrl =
        "https://huggingface.co/datasets/Ritesh121/ARModels/resolve/main/models.json";

    [Tooltip("Catalogue manifest. Leave empty to use the built-in Hugging Face URL.")]
    [SerializeField] private string manifestUrl = DefaultManifestUrl;

    private const string LocalScansKey = "AuraSync.LocalScans";
    private const string HiddenIdsKey = "AuraSync.HiddenModelIds";

    /// <summary>Curated entries once fetched. Null means "not fetched yet", empty means "fetched, none".</summary>
    private List<ModelData> _curated;
    private bool _fetching;

    /// <summary>
    /// Callers that asked for the catalogue while a fetch was already in flight. Without this, the
    /// repository panel and ScanNaming opening together would fire two identical downloads.
    /// </summary>
    private readonly List<Action<List<ModelData>>> _pending = new List<Action<List<ModelData>>>();

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

    #region Reading

    /// <summary>
    /// Every model the user should see: the curated catalogue minus anything they hid, followed by
    /// their own scans. Served from memory after the first fetch.
    /// </summary>
    public void GetAllModels(Action<List<ModelData>> callback)
    {
        if (callback == null) return;

        if (_curated != null)
        {
            callback(Merge());
            return;
        }

        _pending.Add(callback);
        if (!_fetching)
        {
            _fetching = true;
            StartCoroutine(FetchManifestCoroutine());
        }
    }

    /// <summary>Drops the cached catalogue and fetches it again - for a pull-to-refresh.</summary>
    public void Refresh(Action<List<ModelData>> callback = null)
    {
        _curated = null;
        GetAllModels(callback ?? (_ => { }));
    }

    /// <summary>
    /// The catalogue entry whose model is at this URL, or null. The 3D viewer knows a model only by
    /// its URL (that is what travels between devices in a room), so this is how it finds the
    /// entry's other fields, such as the part knowledge file.
    /// </summary>
    public void FindByGlbUrl(string glbUrl, Action<ModelData> callback)
    {
        if (callback == null) return;
        if (string.IsNullOrEmpty(glbUrl)) { callback(null); return; }

        GetAllModels(all =>
        {
            ModelData match = null;
            foreach (ModelData m in all)
            {
                if (m != null && string.Equals(m.glbUrl, glbUrl, StringComparison.OrdinalIgnoreCase))
                {
                    match = m;
                    break;
                }
            }
            callback(match);
        });
    }

    private IEnumerator FetchManifestCoroutine()
    {
        string url = string.IsNullOrEmpty(manifestUrl) ? DefaultManifestUrl : manifestUrl;
        var fetched = new List<ModelData>();

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                try
                {
                    Manifest manifest = JsonUtility.FromJson<Manifest>(json);
                    if (manifest != null && manifest.models != null)
                    {
                        foreach (ModelData entry in manifest.models)
                        {
                            if (entry == null || string.IsNullOrEmpty(entry.glbUrl)) continue;

                            entry.glbUrl = Absolute(entry.glbUrl, manifest.baseUrl);
                            entry.partsUrl = Absolute(entry.partsUrl, manifest.baseUrl);
                            entry.previewUrl = Absolute(entry.previewUrl, manifest.baseUrl);
                            if (string.IsNullOrEmpty(entry.id)) entry.id = IdFromUrl(entry.glbUrl);
                            fetched.Add(entry);
                        }
                    }
                    Debug.Log($"[ModelCatalog] Loaded {fetched.Count} catalogue models from {url}");
                }
                catch (Exception ex)
                {
                    // A malformed manifest must not take the panel down with it - the user's own
                    // scans are still perfectly loadable.
                    Debug.LogError($"[ModelCatalog] Could not parse the manifest at {url}: {ex.Message}");
                }
            }
            else
            {
                Debug.LogError($"[ModelCatalog] Could not reach the catalogue at {url}: {request.error}");
            }
        }

        _curated = fetched;
        _fetching = false;

        // Copy first: a callback is free to call back into GetAllModels.
        var waiting = new List<Action<List<ModelData>>>(_pending);
        _pending.Clear();
        foreach (var callback in waiting) callback?.Invoke(Merge());
    }

    private List<ModelData> Merge()
    {
        HashSet<string> hidden = LoadHiddenIds();
        var merged = new List<ModelData>();

        if (_curated != null)
        {
            foreach (ModelData m in _curated)
            {
                if (m != null && !hidden.Contains(m.id)) merged.Add(m);
            }
        }

        foreach (ModelData m in LoadLocalScans())
        {
            if (m != null && !hidden.Contains(m.id)) merged.Add(m);
        }

        return merged;
    }

    #endregion

    #region Writing (device-local)

    /// <summary>
    /// Stores a model on this device. The curated catalogue is read-only, so scans from the KIRI
    /// pipeline land here rather than on Hugging Face.
    /// </summary>
    public void SaveModel(ModelData data)
    {
        if (data == null) return;

        if (string.IsNullOrEmpty(data.id)) data.id = "local-" + Guid.NewGuid();
        if (data.createdAt == 0) data.createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        List<ModelData> scans = LoadLocalScans();
        scans.RemoveAll(m => m != null && m.id == data.id); // a re-save replaces, as PATCH used to
        scans.Add(data);
        SaveLocalScans(scans);

        Debug.Log($"[ModelCatalog] Saved '{data.name}' to this device ({data.id}).");
    }

    /// <summary>
    /// Removes a model from the user's list. A local scan is deleted outright; a catalogue entry
    /// can only be hidden on this device, since the manifest is shared and read-only.
    /// onDone receives true when the list actually changed.
    /// </summary>
    public void DeleteModel(string id, Action<bool> onDone)
    {
        if (string.IsNullOrEmpty(id))
        {
            onDone?.Invoke(false);
            return;
        }

        List<ModelData> scans = LoadLocalScans();
        if (scans.RemoveAll(m => m != null && m.id == id) > 0)
        {
            SaveLocalScans(scans);
            Debug.Log($"[ModelCatalog] Deleted local scan {id}.");
            onDone?.Invoke(true);
            return;
        }

        HashSet<string> hidden = LoadHiddenIds();
        if (hidden.Add(id))
        {
            SaveHiddenIds(hidden);
            Debug.Log($"[ModelCatalog] Hid catalogue model {id} on this device.");
        }
        onDone?.Invoke(true);
    }

    /// <summary>Brings back every hidden catalogue entry. Wire this to a "restore defaults" button.</summary>
    public void UnhideAll()
    {
        PlayerPrefs.DeleteKey(HiddenIdsKey);
        PlayerPrefs.Save();
    }

    #endregion

    #region PlayerPrefs storage

    private static List<ModelData> LoadLocalScans()
    {
        string json = PlayerPrefs.GetString(LocalScansKey, "");
        if (string.IsNullOrEmpty(json)) return new List<ModelData>();

        try
        {
            ModelDataList stored = JsonUtility.FromJson<ModelDataList>(json);
            if (stored != null && stored.items != null) return stored.items;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ModelCatalog] Local scans were unreadable, starting empty: {ex.Message}");
        }
        return new List<ModelData>();
    }

    private static void SaveLocalScans(List<ModelData> scans)
    {
        PlayerPrefs.SetString(LocalScansKey, JsonUtility.ToJson(new ModelDataList { items = scans }));
        PlayerPrefs.Save();
    }

    private static HashSet<string> LoadHiddenIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string raw = PlayerPrefs.GetString(HiddenIdsKey, "");
        if (string.IsNullOrEmpty(raw)) return ids;

        foreach (string id in raw.Split('\n'))
        {
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    private static void SaveHiddenIds(HashSet<string> ids)
    {
        PlayerPrefs.SetString(HiddenIdsKey, string.Join("\n", new List<string>(ids).ToArray()));
        PlayerPrefs.Save();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Expands a manifest-relative path against `baseUrl`. Entries may also carry a full URL, which
    /// is left alone - that is what lets one manifest point at more than one host.
    /// </summary>
    private static string Absolute(string value, string baseUrl)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return value;
        if (string.IsNullOrEmpty(baseUrl)) return value;

        return baseUrl.TrimEnd('/') + "/" + value.TrimStart('/');
    }

    /// <summary>A stable id for a manifest entry that did not declare one, e.g. "House" for House.glb.</summary>
    private static string IdFromUrl(string url)
    {
        try
        {
            string path = new Uri(url).AbsolutePath;
            return System.IO.Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            return url;
        }
    }

    #endregion

    #region JSON shapes

    /// <summary>
    /// The manifest. JsonUtility cannot deserialise a bare top-level array, which is why the models
    /// sit under a key rather than being the whole document.
    /// </summary>
    [Serializable]
    private class Manifest
    {
        public string baseUrl;
        public ModelData[] models;
    }

    [Serializable]
    private class ModelDataList
    {
        public List<ModelData> items;
    }

    #endregion
}
