using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.Data
{
    // ──────────────────────────────────────────────
    //  Enums
    // ──────────────────────────────────────────────

    public enum PlacedItemType
    {
        Note,
        Object3D
    }

    public enum NoteType
    {
        PlainText,
        Checklist
    }

    public enum NoteStyle
    {
        StickyNote,
        Paper
    }

    // ──────────────────────────────────────────────
    //  Base Data
    // ──────────────────────────────────────────────

    [Serializable]
    public class PlacedItemData
    {
        public string id;
        public PlacedItemType itemType;
        public float[] worldPosition = new float[3];  // x, y, z
        public float[] worldRotation = new float[4];   // quaternion x, y, z, w
        public float[] localScale    = new float[3];   // x, y, z
        public long   timestamp;
        public bool   isPrivate = false;

        /// <summary>Helper to convert Unity Vector3 to float array for serialization.</summary>
        public void SetPosition(Vector3 pos)
        {
            worldPosition[0] = pos.x;
            worldPosition[1] = pos.y;
            worldPosition[2] = pos.z;
        }

        public Vector3 GetPosition()
        {
            return new Vector3(worldPosition[0], worldPosition[1], worldPosition[2]);
        }

        public void SetRotation(Quaternion rot)
        {
            worldRotation[0] = rot.x;
            worldRotation[1] = rot.y;
            worldRotation[2] = rot.z;
            worldRotation[3] = rot.w;
        }

        public Quaternion GetRotation()
        {
            return new Quaternion(worldRotation[0], worldRotation[1], worldRotation[2], worldRotation[3]);
        }

        public void SetScale(Vector3 scale)
        {
            localScale[0] = scale.x;
            localScale[1] = scale.y;
            localScale[2] = scale.z;
        }

        public Vector3 GetScale()
        {
            return new Vector3(localScale[0], localScale[1], localScale[2]);
        }
    }

    // ──────────────────────────────────────────────
    //  Note Data
    // ──────────────────────────────────────────────

    [Serializable]
    public class ChecklistItem
    {
        public string text;
        public bool   isChecked;

        public ChecklistItem() { }
        public ChecklistItem(string text, bool isChecked = false)
        {
            this.text = text;
            this.isChecked = isChecked;
        }
    }

    [Serializable]
    public class NoteItemData : PlacedItemData
    {
        public NoteType  noteType  = NoteType.PlainText;
        public NoteStyle noteStyle = NoteStyle.StickyNote;
        public string    text      = "";
        public string    colorHex  = "#FFE066";        // default sticky-note yellow
        public string    borderColorHex = "#E6C84D";
        public List<ChecklistItem> checklistItems = new List<ChecklistItem>();

        public NoteItemData()
        {
            itemType = PlacedItemType.Note;
            id = Guid.NewGuid().ToString();
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SetScale(Vector3.one);
        }
    }

    // ──────────────────────────────────────────────
    //  3D Object Data
    // ──────────────────────────────────────────────

    [Serializable]
    public class ObjectItemData : PlacedItemData
    {
        public string prefabName     = "Cube";
        public string colorHex       = "#FFFFFF";
        public string borderColorHex = "#000000";
        public float  opacity        = 1f;
        public bool   hasBorder      = false;

        public ObjectItemData()
        {
            itemType = PlacedItemType.Object3D;
            id = Guid.NewGuid().ToString();
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SetScale(Vector3.one * 0.1f); // 10 cm default size
        }
    }

    // ──────────────────────────────────────────────
    //  Environment Save Data (groups items per space)
    // ──────────────────────────────────────────────

    [Serializable]
    public class EnvironmentSaveData
    {
        public string environmentId;
        public string environmentName;
        public long   lastModified;
        public List<NoteItemData>   notes   = new List<NoteItemData>();
        public List<ObjectItemData> objects = new List<ObjectItemData>();

        public EnvironmentSaveData()
        {
            environmentId = Guid.NewGuid().ToString();
            environmentName = "Default";
            lastModified = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    // ──────────────────────────────────────────────
    //  Master Save File (all environments)
    // ──────────────────────────────────────────────

    [Serializable]
    public class AppSaveData
    {
        public string activeEnvironmentId;
        public List<EnvironmentSaveData> environments = new List<EnvironmentSaveData>();
    }
}
