using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using AuraSync.Data;

namespace AuraSync.Core
{
    [RequireComponent(typeof(ARRaycastManager))]
    [RequireComponent(typeof(GestureDetector))]
    public class ARPlacementController : MonoBehaviour
    {
        public static ARPlacementController Instance { get; private set; }

        [Header("Prefab Registry")]
        [Tooltip("Index 0 = Sticky PlainText, 1 = Sticky Checklist, 2 = Paper PlainText, 3 = Paper Checklist")]
        [SerializeField] private GameObject[] notePrefabs;
        [SerializeField] private ObjectPrefabEntry[] objectPrefabs;

        [Header("Placement Settings")]
        [SerializeField] private float placementOffset = 0.02f;

        private ARRaycastManager _raycastManager;
        private GestureDetector  _gestureDetector;
        private static List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        private Dictionary<string, GameObject> _activeItems = new Dictionary<string, GameObject>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _raycastManager  = GetComponent<ARRaycastManager>();
            _gestureDetector = GetComponent<GestureDetector>();

            if (_raycastManager  == null) Debug.LogError("[AuraSync] ARRaycastManager MISSING on " + gameObject.name);
            if (_gestureDetector == null) Debug.LogError("[AuraSync] GestureDetector MISSING on " + gameObject.name);
            Debug.Log("[AuraSync] GestureDetector found: " + (_gestureDetector != null));
        }

        void OnEnable()
        {
            _gestureDetector.OnTap        += HandleTap;
            _gestureDetector.OnDoubleTap  += HandleDoubleTap;
            _gestureDetector.OnLongPress  += HandleLongPress;
        }

        void OnDisable()
        {
            _gestureDetector.OnTap        -= HandleTap;
            _gestureDetector.OnDoubleTap  -= HandleDoubleTap;
            _gestureDetector.OnLongPress  -= HandleLongPress;
        }

        // ──────────────────────────────────────────
        //  Tap — single tap (fires after 300ms if no double-tap follows)
        // ──────────────────────────────────────────

        private void HandleTap(Vector2 screenPos)
        {
            var mode = AppStateManager.Instance.CurrentMode;
            Debug.Log($"[AuraSync] HandleTap — Mode={mode}, screenPos={screenPos}");

            switch (mode)
            {
                case AppMode.PlacingNote:
                    TryPlaceNote(screenPos);
                    break;
                case AppMode.PlacingObject:
                    TryPlaceObject(screenPos);
                    break;
                case AppMode.Idle:
                    TrySelectItem(screenPos);
                    break;
            }
        }

        // ──────────────────────────────────────────
        //  Double Tap — show Edit / Rotate choice overlay
        // ──────────────────────────────────────────

        private void HandleDoubleTap(Vector2 screenPos)
        {
            var mode = AppStateManager.Instance.CurrentMode;
            Debug.Log($"[AuraSync] HandleDoubleTap — Mode={mode}, screenPos={screenPos}");

            // In placement modes, double-tap cancels placement (safety valve)
            if (mode == AppMode.PlacingNote || mode == AppMode.PlacingObject)
            {
                AppStateManager.Instance.ReturnToIdle();
                Debug.Log("[AuraSync] Double-tap cancelled placement");
                return;
            }

            if (mode != AppMode.Idle) return;

            // Raycast to find which item was double-tapped
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f))
            {
                var noteCtrl = hit.collider.GetComponentInParent<Notes.NoteController>();
                if (noteCtrl != null)
                {
                    SelectionManager.Instance?.Select(noteCtrl.gameObject, noteCtrl.Data);
                    UI.InteractionChoiceUI.Instance?.Show();
                    Debug.Log($"[AuraSync] Double-tap: showing choice for note {noteCtrl.Data.id}");
                    return;
                }

                var objCtrl = hit.collider.GetComponentInParent<Objects.ObjectController>();
                if (objCtrl != null)
                {
                    SelectionManager.Instance?.Select(objCtrl.gameObject, objCtrl.Data);
                    UI.InteractionChoiceUI.Instance?.Show();
                    Debug.Log($"[AuraSync] Double-tap: showing choice for object {objCtrl.Data.id}");
                    return;
                }
            }

            // Double-tapped empty space — deselect
            SelectionManager.Instance?.Deselect();
        }

        // ──────────────────────────────────────────
        //  Long Press — delete
        // ──────────────────────────────────────────

        private void HandleLongPress(Vector2 screenPos)
        {
            if (AppStateManager.Instance.CurrentMode != AppMode.Idle) return;
            TryDeleteItem(screenPos);
        }

        // ──────────────────────────────────────────
        //  Note Placement
        // ──────────────────────────────────────────

        private void TryPlaceNote(Vector2 screenPos)
        {
            Debug.Log($"[AuraSync] TryPlaceNote — raycasting at {screenPos}");
            if (!RaycastAR(screenPos, out Pose hitPose))
            {
                ShowPlacementFeedback("Point camera at a flat surface");
                return;
            }
            Debug.Log($"[AuraSync] TryPlaceNote — AR hit at {hitPose.position}");

            NoteItemData config = AppStateManager.Instance.pendingNoteConfig;
            if (config == null) { Debug.LogWarning("[AuraSync] No pending note config!"); return; }

            int prefabIndex = GetNotePrefabIndex(config.noteStyle, config.noteType);
            if (prefabIndex < 0 || prefabIndex >= notePrefabs.Length)
            {
                Debug.LogError("[AuraSync] Note prefab index out of range!");
                return;
            }

            Vector3    pos      = hitPose.position + hitPose.up * placementOffset;
            Vector3    toCamera = Camera.main.transform.position - pos;
            Quaternion rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);

            GameObject noteObj = Instantiate(notePrefabs[prefabIndex], pos, rotation);
            noteObj.transform.localScale = config.GetScale();
            config.SetPosition(pos);
            config.SetRotation(rotation);

            var ctrl = noteObj.GetComponent<Notes.NoteController>();
            if (ctrl != null) ctrl.Initialize(config);

            _activeItems[config.id] = noteObj;
            PersistenceManager.Instance.AddNote(config);
            AppStateManager.Instance.ReturnToIdle();
            Debug.Log($"[AuraSync] Note placed at {pos}");
        }

        // ──────────────────────────────────────────
        //  Object Placement
        // ──────────────────────────────────────────

        private void TryPlaceObject(Vector2 screenPos)
        {
            Debug.Log($"[AuraSync] TryPlaceObject — raycasting at {screenPos}");
            if (!RaycastAR(screenPos, out Pose hitPose))
            {
                ShowPlacementFeedback("Point camera at a flat surface");
                return;
            }
            Debug.Log($"[AuraSync] TryPlaceObject — AR hit at {hitPose.position}");

            ObjectItemData config = AppStateManager.Instance.pendingObjectConfig;
            if (config == null) { Debug.LogWarning("[AuraSync] No pending object config!"); return; }

            GameObject prefab = GetObjectPrefab(config.prefabName);
            if (prefab == null) { Debug.LogError($"[AuraSync] Object prefab not found: {config.prefabName}"); return; }

            Vector3    pos = hitPose.position + hitPose.up * placementOffset;
            GameObject obj = Instantiate(prefab, pos, hitPose.rotation);
            obj.transform.localScale = config.GetScale();
            config.SetPosition(pos);
            config.SetRotation(hitPose.rotation);

            var ctrl = obj.GetComponent<Objects.ObjectController>();
            if (ctrl != null) ctrl.Initialize(config);

            _activeItems[config.id] = obj;
            PersistenceManager.Instance.AddObject(config);
            AppStateManager.Instance.ReturnToIdle();
            Debug.Log($"[AuraSync] Object placed: {config.prefabName} at {pos}");
        }

        // ──────────────────────────────────────────
        //  Selection & Deletion
        // ──────────────────────────────────────────

        private void TrySelectItem(Vector2 screenPos)
        {
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f))
            {
                var noteCtrl = hit.collider.GetComponentInParent<Notes.NoteController>();
                if (noteCtrl != null) { SelectionManager.Instance?.Select(noteCtrl.gameObject, noteCtrl.Data); return; }

                var objCtrl = hit.collider.GetComponentInParent<Objects.ObjectController>();
                if (objCtrl != null) { SelectionManager.Instance?.Select(objCtrl.gameObject, objCtrl.Data); return; }
            }
            SelectionManager.Instance?.Deselect();
        }

        private void TryDeleteItem(Vector2 screenPos)
        {
            Ray ray = Camera.main.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 10f))
            {
                var noteCtrl = hit.collider.GetComponentInParent<Notes.NoteController>();
                if (noteCtrl != null)
                {
                    string id = noteCtrl.Data.id;
                    PersistenceManager.Instance.RemoveNote(id);
                    _activeItems.Remove(id);
                    Destroy(noteCtrl.gameObject);
                    Handheld.Vibrate();
                    Debug.Log($"[AuraSync] Note deleted: {id}");
                    return;
                }

                var objCtrl = hit.collider.GetComponentInParent<Objects.ObjectController>();
                if (objCtrl != null)
                {
                    string id = objCtrl.Data.id;
                    PersistenceManager.Instance.RemoveObject(id);
                    _activeItems.Remove(id);
                    Destroy(objCtrl.gameObject);
                    Handheld.Vibrate();
                    Debug.Log($"[AuraSync] Object deleted: {id}");
                    return;
                }
            }
        }

        // ──────────────────────────────────────────
        //  Restore / Clear
        // ──────────────────────────────────────────

        public void RestoreSavedItems(EnvironmentSaveData env)
        {
            if (env == null) return;

            foreach (var noteData in env.notes)
            {
                int idx = GetNotePrefabIndex(noteData.noteStyle, noteData.noteType);
                if (idx < 0 || idx >= notePrefabs.Length) continue;

                GameObject noteObj = Instantiate(notePrefabs[idx], noteData.GetPosition(), noteData.GetRotation());
                noteObj.transform.localScale = noteData.GetScale();
                var ctrl = noteObj.GetComponent<Notes.NoteController>();
                if (ctrl != null) ctrl.Initialize(noteData);
                _activeItems[noteData.id] = noteObj;
            }

            foreach (var objData in env.objects)
            {
                GameObject prefab = GetObjectPrefab(objData.prefabName);
                if (prefab == null) continue;

                GameObject obj = Instantiate(prefab, objData.GetPosition(), objData.GetRotation());
                obj.transform.localScale = objData.GetScale();
                var ctrl = obj.GetComponent<Objects.ObjectController>();
                if (ctrl != null) ctrl.Initialize(objData);
                _activeItems[objData.id] = obj;
            }

            Debug.Log($"[AuraSync] Restored {env.notes.Count} notes + {env.objects.Count} objects");
        }

        public void ClearAllItems()
        {
            foreach (var kvp in _activeItems)
                if (kvp.Value != null) Destroy(kvp.Value);
            _activeItems.Clear();
        }

        public void SetPendingNote(AuraSync.Data.NoteItemData config)
        {
            if (AppStateManager.Instance != null) AppStateManager.Instance.pendingNoteConfig = config;
        }

        public void SetPendingObject(AuraSync.Data.ObjectItemData config)
        {
            if (AppStateManager.Instance != null) AppStateManager.Instance.pendingObjectConfig = config;
        }

        public GameObject GetActiveItem(string id)
        {
            _activeItems.TryGetValue(id, out GameObject obj);
            return obj;
        }

        // ──────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────

        private bool RaycastAR(Vector2 screenPos, out Pose hitPose)
        {
            hitPose = Pose.identity;

            if (_raycastManager.Raycast(screenPos, _hits,
                TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds |
                TrackableType.PlaneWithinInfinity | TrackableType.FeaturePoint))
            {
                hitPose = _hits[0].pose;
                return true;
            }

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (_raycastManager.Raycast(center, _hits,
                TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds |
                TrackableType.PlaneWithinInfinity | TrackableType.FeaturePoint))
            {
                hitPose = _hits[0].pose;
                return true;
            }

            Debug.LogWarning("[AuraSync] AR raycast MISS — no surface at " + screenPos);
            AuraSync.UI.ARHintUI.Instance?.ShowMiss();
            return false;
        }

        private void ShowPlacementFeedback(string message)
        {
            FindObjectOfType<UI.PlacementFeedbackUI>()?.Show(message);
        }

        private int GetNotePrefabIndex(NoteStyle style, NoteType type)
        {
            int styleOffset = style == NoteStyle.StickyNote ? 0 : 2;
            int typeOffset  = type  == NoteType.PlainText   ? 0 : 1;
            return styleOffset + typeOffset;
        }

        private GameObject GetObjectPrefab(string prefabName)
        {
            if (objectPrefabs == null) return null;
            foreach (var entry in objectPrefabs)
                if (entry.name == prefabName) return entry.prefab;
            return null;
        }
    }

    [System.Serializable]
    public class ObjectPrefabEntry
    {
        public string     name;
        public GameObject prefab;
    }
}