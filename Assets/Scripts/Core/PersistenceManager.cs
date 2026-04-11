using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AuraSync.Data;

namespace AuraSync.Core
{
    /// <summary>
    /// Manages saving and loading of all placed items to/from local JSON files.
    /// Each environment is stored as a separate JSON file in Application.persistentDataPath/AuraSync/.
    /// 
    /// v2 NOTE: This class is designed with a clean interface so that Cloud Anchors
    /// can be integrated later for multi-user shared persistence.
    /// 
    /// FIX (v1.1): Added OnDestroy() to null the static Instance reference.
    /// Without this, Android process resume pointed Instance at a destroyed object,
    /// causing all persistence calls to silently fail on app restart.
    /// </summary>
    public class PersistenceManager : MonoBehaviour
    {
        public static PersistenceManager Instance { get; private set; }

        // ── Events ──
        public event Action OnDataLoaded;
        public event Action OnDataSaved;

        // ── Runtime Data ──
        private AppSaveData _saveData;
        private string _saveDirectory;

        // ── Currently Active Environment ──
        public EnvironmentSaveData ActiveEnvironment { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _saveDirectory = Path.Combine(Application.persistentDataPath, "AuraSync");
            if (!Directory.Exists(_saveDirectory))
            {
                Directory.CreateDirectory(_saveDirectory);
            }

            LoadAllData();
        }

        // ── FIX: Null the static reference when this object is destroyed ──
        // This ensures a fresh app start always gets a new, fully-initialized
        // PersistenceManager instead of a stale reference to a destroyed object.
        // The save FILE on disk is NOT affected — data is always safe.
        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ──────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────

        /// <summary>Get or create an environment with the given name.</summary>
        public EnvironmentSaveData GetOrCreateEnvironment(string name = "Default")
        {
            // Try to find existing
            foreach (var env in _saveData.environments)
            {
                if (env.environmentName == name)
                {
                    ActiveEnvironment = env;
                    _saveData.activeEnvironmentId = env.environmentId;
                    return env;
                }
            }

            // Create new
            var newEnv = new EnvironmentSaveData
            {
                environmentName = name
            };
            _saveData.environments.Add(newEnv);
            ActiveEnvironment = newEnv;
            _saveData.activeEnvironmentId = newEnv.environmentId;

            SaveAllData();
            Debug.Log($"[AuraSync] Created new environment: {name} ({newEnv.environmentId})");
            return newEnv;
        }

        /// <summary>Set active environment by ID.</summary>
        public void SetActiveEnvironment(string envId)
        {
            foreach (var env in _saveData.environments)
            {
                if (env.environmentId == envId)
                {
                    ActiveEnvironment = env;
                    _saveData.activeEnvironmentId = envId;
                    return;
                }
            }
            Debug.LogWarning($"[AuraSync] Environment not found: {envId}");
        }

        /// <summary>Get all environments.</summary>
        public List<EnvironmentSaveData> GetAllEnvironments()
        {
            return _saveData.environments;
        }

        // ──────────────────────────────────────────
        //  Note Operations
        // ──────────────────────────────────────────

        /// <summary>Register a new note in the active environment.</summary>
        public void AddNote(NoteItemData note)
        {
            if (ActiveEnvironment == null)
            {
                Debug.LogError("[AuraSync] No active environment! Call GetOrCreateEnvironment first.");
                return;
            }

            ActiveEnvironment.notes.Add(note);
            ActiveEnvironment.lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveAllData();
            Debug.Log($"[AuraSync] Note saved: {note.id}");
        }

        /// <summary>Update an existing note's data.</summary>
        public void UpdateNote(NoteItemData updated)
        {
            if (ActiveEnvironment == null) return;

            for (int i = 0; i < ActiveEnvironment.notes.Count; i++)
            {
                if (ActiveEnvironment.notes[i].id == updated.id)
                {
                    ActiveEnvironment.notes[i] = updated;
                    ActiveEnvironment.lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    SaveAllData();
                    Debug.Log($"[AuraSync] Note updated: {updated.id}");
                    return;
                }
            }
        }

        /// <summary>Remove a note by ID.</summary>
        public void RemoveNote(string noteId)
        {
            if (ActiveEnvironment == null) return;

            ActiveEnvironment.notes.RemoveAll(n => n.id == noteId);
            ActiveEnvironment.lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveAllData();
            Debug.Log($"[AuraSync] Note removed: {noteId}");
        }

        // ──────────────────────────────────────────
        //  Object Operations
        // ──────────────────────────────────────────

        /// <summary>Register a new 3D object in the active environment.</summary>
        public void AddObject(ObjectItemData obj)
        {
            if (ActiveEnvironment == null)
            {
                Debug.LogError("[AuraSync] No active environment! Call GetOrCreateEnvironment first.");
                return;
            }

            ActiveEnvironment.objects.Add(obj);
            ActiveEnvironment.lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveAllData();
            Debug.Log($"[AuraSync] Object saved: {obj.id} ({obj.prefabName})");
        }

        /// <summary>Update an existing object's data.</summary>
        public void UpdateObject(ObjectItemData updated)
        {
            if (ActiveEnvironment == null) return;

            for (int i = 0; i < ActiveEnvironment.objects.Count; i++)
            {
                if (ActiveEnvironment.objects[i].id == updated.id)
                {
                    ActiveEnvironment.objects[i] = updated;
                    ActiveEnvironment.lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    SaveAllData();
                    Debug.Log($"[AuraSync] Object updated: {updated.id}");
                    return;
                }
            }
        }

        /// <summary>Remove a 3D object by ID.</summary>
        public void RemoveObject(string objId)
        {
            if (ActiveEnvironment == null) return;

            ActiveEnvironment.objects.RemoveAll(o => o.id == objId);
            ActiveEnvironment.lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SaveAllData();
            Debug.Log($"[AuraSync] Object removed: {objId}");
        }

        // ──────────────────────────────────────────
        //  JSON Serialization
        // ──────────────────────────────────────────

        private string GetSaveFilePath()
        {
            return Path.Combine(_saveDirectory, "aurasync_data.json");
        }

        public void SaveAllData()
        {
            try
            {
                string json = JsonUtility.ToJson(_saveData, true);
                File.WriteAllText(GetSaveFilePath(), json);
                OnDataSaved?.Invoke();
                Debug.Log("[AuraSync] Data saved to disk.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AuraSync] Failed to save: {e.Message}");
            }
        }

        public void LoadAllData()
        {
            string path = GetSaveFilePath();

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    _saveData = JsonUtility.FromJson<AppSaveData>(json);
                    Debug.Log($"[AuraSync] Loaded {_saveData.environments.Count} environment(s).");

                    // Restore active environment
                    if (!string.IsNullOrEmpty(_saveData.activeEnvironmentId))
                    {
                        SetActiveEnvironment(_saveData.activeEnvironmentId);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[AuraSync] Failed to load: {e.Message}");
                    _saveData = new AppSaveData();
                }
            }
            else
            {
                _saveData = new AppSaveData();
                Debug.Log("[AuraSync] No save file found — starting fresh.");
            }

            OnDataLoaded?.Invoke();
        }

        /// <summary>Wipe all saved data (for debugging).</summary>
        public void ClearAllData()
        {
            _saveData = new AppSaveData();
            SaveAllData();
            Debug.Log("[AuraSync] All data cleared.");
        }
    }
}