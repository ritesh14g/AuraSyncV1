using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AuraSync.Notes
{
    /// <summary>
    /// A note stuck to one spot on one part of a 3D model (V3 Phase 5).
    ///
    /// The spot is stored in the part's **own** coordinates (point and surface direction), so the
    /// note follows the part through everything that moves it - turning, exploding, isolating,
    /// later the section cut and simulation - with nothing to recalculate.
    /// </summary>
    [Serializable]
    public class PartNote
    {
        public string  id = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string  modelKey = "";
        public string  path = "";                    // part path (ModelRaycaster.GetPartPath)
        public float[] point  = new float[3];        // in the part's local space
        public float[] normal = { 0f, 0f, 1f };      // in the part's local space
        public string  text = "";
        public string  colorHex = "#FFE066";
        public bool    isPrivate;                    // "Only me": never leaves the author's phone
        public string  authorName = "";
        public string  authorDevice = "";            // which phone wrote it, across sessions
        public long    created;
        public long    edited;

        /// <summary>Actor number of the author in the current room (runtime only, 0 = not in a room).</summary>
        [NonSerialized] public int authorActor;

        public Vector3 LocalPoint
        {
            get => new Vector3(point[0], point[1], point[2]);
            set { point = new[] { value.x, value.y, value.z }; }
        }

        public Vector3 LocalNormal
        {
            get => new Vector3(normal[0], normal[1], normal[2]);
            set { normal = new[] { value.x, value.y, value.z }; }
        }

        public PartNote Clone() => (PartNote)MemberwiseClone();

        /// <summary>The note colours, the same sticky-note family the AR notes use.</summary>
        public static readonly string[] Colors = { "#FFE066", "#8EE4AF", "#8FD3FE", "#FFB3C7", "#FFC48C" };

        public Color Color => ColorUtility.TryParseHtmlString(colorHex, out Color c) ? c : new Color(1f, 0.88f, 0.4f);
    }

    /// <summary>
    /// The notes this phone wrote, one file per model:
    /// <c>&lt;persistentDataPath&gt;/AuraSync/PartNotes/&lt;model hash&gt;.json</c>.
    ///
    /// Decision D4 (2026-10-13): notes live on their author's phone. There is no shared database;
    /// an author's "Everyone" notes are shared again whenever they are in a room on that model.
    /// Other people's notes are never written here.
    /// </summary>
    public static class PartNoteStore
    {
        [Serializable]
        private class FileData { public List<PartNote> notes = new List<PartNote>(); }

        private const string DeviceIdKey = "AuraSync.DeviceId";
        private static string _deviceId;

        /// <summary>A random id for this phone, kept for good, so its notes stay its own across sessions.</summary>
        public static string DeviceId
        {
            get
            {
                if (!string.IsNullOrEmpty(_deviceId)) return _deviceId;
                _deviceId = PlayerPrefs.GetString(DeviceIdKey, "");
                if (string.IsNullOrEmpty(_deviceId))
                {
                    _deviceId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(DeviceIdKey, _deviceId);
                    PlayerPrefs.Save();
                }
                return _deviceId;
            }
        }

        private static string Folder => Path.Combine(Application.persistentDataPath, "AuraSync", "PartNotes");

        private static string FileFor(string modelKey) =>
            Path.Combine(Folder, AuraSync.Space.ModelStateMemory.ModelHash(modelKey) + ".json");

        public static List<PartNote> Load(string modelKey)
        {
            try
            {
                string f = FileFor(modelKey);
                if (!File.Exists(f)) return new List<PartNote>();
                var data = JsonUtility.FromJson<FileData>(File.ReadAllText(f));
                return data?.notes ?? new List<PartNote>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PartNotes] Could not read the notes for '{modelKey}': {ex.Message}");
                return new List<PartNote>();
            }
        }

        public static void Save(string modelKey, List<PartNote> notes)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FileFor(modelKey), JsonUtility.ToJson(new FileData { notes = notes }, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PartNotes] Could not save the notes for '{modelKey}': {ex.Message}");
            }
        }
    }
}
