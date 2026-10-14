using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using AuraSync.Space;

namespace AuraSync.AI
{
    /// <summary>
    /// Holds the part knowledge file for the model on screen, and answers "what is this part?".
    ///
    /// When a model loads, this finds its catalogue entry (by the model's URL), downloads the
    /// entry's <c>partsUrl</c>, and checks every part in it against the model that actually loaded.
    /// The file is also cached on the device, so a model opened offline keeps its labels.
    ///
    /// **Why the check matters.** Parts are named by their position in the hierarchy ("0/0/4/0").
    /// If a model file is re-optimised and its nodes come out in a different order, those
    /// positions point at the wrong parts, and the app would confidently call the exhaust "Fan".
    /// Each entry also carries the node's name, so a mismatch is caught: the part is found again by
    /// name where possible, and dropped (with a warning) where not.
    ///
    /// Phase 2 only loads and checks. The AI chat (Phase 3), search (Phase 4), simulation
    /// (Phase 7) and quiz (Phase 11) read from it.
    /// </summary>
    public class PartKnowledge : MonoBehaviour
    {
        public static PartKnowledge Instance { get; private set; }

        /// <summary>The loaded file, or null while loading or when the model has none.</summary>
        public PartKnowledgeFile Current { get; private set; }

        /// <summary>The model's name from the catalogue ("Jet Engine"), or "" when unknown.</summary>
        public string ModelName { get; private set; } = "";

        /// <summary>One line describing the state, for logs and debug readouts.</summary>
        public string Status { get; private set; } = "No model loaded.";

        /// <summary>True when the model has real part entries (not whole-model-only, not missing).</summary>
        public bool HasParts => Current != null && !Current.wholeModelOnly && _byPath.Count > 0;

        /// <summary>Raised whenever <see cref="Current"/> changes (loaded, cleared, or failed).</summary>
        public event Action OnChanged;

        private readonly Dictionary<string, PartKnowledgeEntry> _byPath = new Dictionary<string, PartKnowledgeEntry>();
        private int _loadToken; // a model switch mid-download must not let the old file land

        public static PartKnowledge Ensure()
        {
            if (Instance != null) return Instance;
            return new GameObject("AuraSync_PartKnowledge").AddComponent<PartKnowledge>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Reading ────────────────────────────────────────────────────────────

        public bool TryGet(string path, out PartKnowledgeEntry entry)
        {
            entry = null;
            return !string.IsNullOrEmpty(path) && _byPath.TryGetValue(path, out entry);
        }

        /// <summary>Display label for a part: the knowledge file's label, else "" (callers fall back to the node name).</summary>
        public string LabelFor(string path)
        {
            return TryGet(path, out PartKnowledgeEntry e) ? e.label : "";
        }

        public IEnumerable<PartKnowledgeEntry> AllParts => _byPath.Values;

        // ── Loading ────────────────────────────────────────────────────────────

        /// <summary>Called by SpaceSceneManager once a model has loaded and been fitted.</summary>
        public void OnModelLoaded(string modelKey, Transform root)
        {
            int token = ++_loadToken;
            Clear("Looking up part knowledge...");

            ModelCatalog catalog = ModelCatalog.Instance;
            if (catalog == null)
            {
                // Opening SpaceScene directly in the Editor skips the scene that creates the
                // catalogue. Make one: it only needs its built-in manifest URL.
                catalog = new GameObject("ModelCatalog").AddComponent<ModelCatalog>();
            }

            catalog.FindByGlbUrl(modelKey, entry =>
            {
                if (token != _loadToken || this == null) return;

                ModelName = entry != null ? entry.name ?? "" : "";
                if (entry == null)
                {
                    Finish(null, root, "This model is not in the catalogue, so it has no part knowledge.");
                    return;
                }
                if (string.IsNullOrEmpty(entry.partsUrl))
                {
                    Finish(null, root, $"'{entry.name}' has no part knowledge file yet.");
                    return;
                }
                StartCoroutine(Download(entry, root, token));
            });
        }

        private IEnumerator Download(ModelData entry, Transform root, int token)
        {
            string cachePath = CachePath(entry.partsUrl);
            string json = null;
            string source;

            using (UnityWebRequest req = UnityWebRequest.Get(entry.partsUrl))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (token != _loadToken) yield break;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    json = req.downloadHandler.text;
                    source = "downloaded";
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                        File.WriteAllText(cachePath, json);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PartKnowledge] Could not cache {entry.partsUrl}: {ex.Message}");
                    }
                }
                else if (File.Exists(cachePath))
                {
                    json = File.ReadAllText(cachePath);
                    source = $"from the device cache (download failed: {req.error})";
                }
                else
                {
                    Finish(null, root, $"Could not download part knowledge for '{entry.name}': {req.error}");
                    yield break;
                }
            }

            PartKnowledgeFile file = null;
            try { file = JsonUtility.FromJson<PartKnowledgeFile>(json); }
            catch (Exception ex) { Debug.LogWarning($"[PartKnowledge] Bad JSON in {entry.partsUrl}: {ex.Message}"); }

            if (file == null)
            {
                Finish(null, root, $"The part knowledge file for '{entry.name}' could not be read.");
                yield break;
            }

            Finish(file, root, null, source, entry);
        }

        private void Finish(PartKnowledgeFile file, Transform root, string problem,
                            string source = "", ModelData entry = null)
        {
            _byPath.Clear();
            Current = file;
            ModelName = entry != null ? entry.name ?? "" : ModelName;

            if (file == null)
            {
                Status = problem;
                Debug.Log($"[PartKnowledge] {problem}");
                OnChanged?.Invoke();
                return;
            }

            if (file.wholeModelOnly)
            {
                Status = $"'{entry?.name}': whole-model knowledge only ({source}).";
                Debug.Log($"[PartKnowledge] {Status}");
                OnChanged?.Invoke();
                return;
            }

            int byPath = 0, byName = 0, missing = 0;
            var missingNames = new List<string>();

            foreach (PartKnowledgeEntry e in file.parts ?? new List<PartKnowledgeEntry>())
            {
                if (e == null) continue;

                Transform t = ModelRaycaster.FindPart(root, e.path);
                if (t != null && (string.IsNullOrEmpty(e.node) || t.name == e.node))
                {
                    byPath++;
                }
                else
                {
                    // The path points nowhere, or at a differently named node: look for the name.
                    Transform found = string.IsNullOrEmpty(e.node) ? null : FindUniqueByName(root, e.node);
                    if (found == null)
                    {
                        missing++;
                        missingNames.Add(string.IsNullOrEmpty(e.node) ? e.path : e.node);
                        continue;
                    }
                    e.path = ModelRaycaster.GetPartPath(root, found);
                    byName++;
                }

                _byPath[e.path] = e;
            }

            Status = $"'{entry?.name}': {_byPath.Count} part(s) labelled ({source})" +
                     (byName > 0 ? $", {byName} found by name because their position changed" : "") +
                     (missing > 0 ? $", {missing} not in this model" : "") + ".";

            if (missing > 0 || byName > 0)
            {
                Debug.LogWarning($"[PartKnowledge] {Status}" +
                                 (missing > 0 ? $" Missing: {string.Join(", ", missingNames)}." : "") +
                                 " The knowledge file was probably made for a different version of the model;" +
                                 " re-run the Part Labeler on the current one.");
            }
            else
            {
                Debug.Log($"[PartKnowledge] {Status}");
            }

            OnChanged?.Invoke();
        }

        private void Clear(string status)
        {
            _byPath.Clear();
            Current = null;
            ModelName = "";
            Status = status;
            OnChanged?.Invoke();
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Transform FindUniqueByName(Transform root, string name)
        {
            Transform found = null;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                if (found != null) return null; // ambiguous: better no label than the wrong one
                found = t;
            }
            return found;
        }

        private static string CachePath(string url)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
                var sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return Path.Combine(Application.persistentDataPath, "PartKnowledge", sb + ".json");
            }
        }
    }
}
