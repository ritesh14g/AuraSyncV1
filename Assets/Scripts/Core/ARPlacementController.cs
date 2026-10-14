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
            ARAnchor anchor = noteObj.AddComponent<ARAnchor>(); // Create local anchor
            
            noteObj.transform.localScale = config.GetScale();
            config.SetPosition(pos);
            config.SetRotation(rotation);

            var ctrl = noteObj.GetComponent<Notes.NoteController>();
            if (ctrl != null) ctrl.Initialize(config);

            _activeItems[config.id] = noteObj;
            PersistenceManager.Instance.AddNote(config);
            Networking.ZoneManager.Instance?.RegisterPlacedItem(config);
            AppStateManager.Instance.ReturnToIdle();
            Debug.Log($"[AuraSync] Note placed at {pos}");

            ShareItem(anchor, config.isPrivate, "RpcPlaceNote", "Note",
                      cloudId =>
                      {
                          config.cloudAnchorId = cloudId;
                          PersistenceManager.Instance?.UpdateNote(config); // keep the cloud ID across restarts
                      },
                      () => JsonUtility.ToJson(config));
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
            ARAnchor anchor = obj.AddComponent<ARAnchor>(); // Create local anchor

            obj.transform.localScale = config.GetScale();
            config.SetPosition(pos);
            config.SetRotation(hitPose.rotation);

            var ctrl = obj.GetComponent<Objects.ObjectController>();
            if (ctrl != null) ctrl.Initialize(config);

            _activeItems[config.id] = obj;
            PersistenceManager.Instance.AddObject(config);
            Networking.ZoneManager.Instance?.RegisterPlacedItem(config);
            AppStateManager.Instance.ReturnToIdle();
            Debug.Log($"[AuraSync] Object placed: {config.prefabName} at {pos}");

            ShareItem(anchor, config.isPrivate, "RpcPlaceObject", "Object",
                      cloudId =>
                      {
                          config.cloudAnchorId = cloudId;
                          PersistenceManager.Instance?.UpdateObject(config); // keep the cloud ID across restarts
                      },
                      () => JsonUtility.ToJson(config));
        }

        // ──────────────────────────────────────────
        //  Network Sharing
        // ──────────────────────────────────────────

        /// <summary>
        /// Shares a freshly placed item with the room: hosts a Cloud Anchor (when available) so
        /// other devices can align it, then sends the item over Photon.
        /// Does nothing for private items or when not in a room, so solo placement never touches
        /// the cloud or the network.
        /// </summary>
        /// <param name="onCloudId">Called with the hosted Cloud Anchor ID before the item JSON is built.</param>
        /// <param name="toJson">Builds the payload lazily so it includes the cloud ID once known.</param>
        private void ShareItem(ARAnchor anchor, bool isPrivate, string rpcName, string label,
                               System.Action<string> onCloudId, System.Func<string> toJson)
        {
            if (isPrivate || !Photon.Pun.PhotonNetwork.InRoom) return;

            var cloud = Networking.CloudAnchorManager.Instance;
            if (cloud == null)
            {
                SendPlacementRpc(rpcName, toJson);
                return;
            }

            // Snapshot the pose now: the item can move (for example notes turn to face the camera) while hosting.
            Pose hostedPose = new Pose(anchor.transform.position, anchor.transform.rotation);

            ShowPlacementFeedback($"Hosting {label} to Cloud...");
            cloud.HostAnchor(anchor, cloudId =>
            {
                onCloudId?.Invoke(cloudId);
                ClaimSharedOrigin(cloudId, hostedPose);
                SendPlacementRpc(rpcName, toJson);
                ShowPlacementFeedback($"{label} Cloud Anchor Hosted!");
            }, error =>
            {
                ShowPlacementFeedback("Failed to host Cloud Anchor. Sending local position only.");
                SendPlacementRpc(rpcName, toJson);
            });
        }

        /// <summary>
        /// The presenter's first hosted anchor becomes the room's shared origin, so pointers and
        /// gaze can be exchanged in a frame every device agrees on.
        /// </summary>
        private void ClaimSharedOrigin(string cloudId, Pose hostedPose)
        {
            if (Networking.ZoneManager.Instance == null || !Networking.ZoneManager.Instance.IsAdmin) return;
            if (Networking.SharedSpaceOrigin.HasOrigin) return;

            Transform frame = new GameObject("SharedOriginFrame").transform;
            frame.SetPositionAndRotation(hostedPose.position, hostedPose.rotation);
            Networking.SharedSpaceOrigin.RegisterAnchor(cloudId, frame);
            Networking.SharedSpaceOrigin.TryClaimAsAdmin(cloudId);
        }

        /// <summary>
        /// Brings a newly joined player up to date: re-sends every shared item that has a Cloud
        /// Anchor (items without one cannot be placed correctly on another device).
        /// Called on the master client only. The receiver ignores IDs it already has.
        /// </summary>
        public void SendSharedItemsTo(Photon.Realtime.Player target)
        {
            var env = PersistenceManager.Instance != null ? PersistenceManager.Instance.ActiveEnvironment : null;
            var pv  = Networking.NetworkManager.Instance != null
                ? Networking.NetworkManager.Instance.GetComponent<Photon.Pun.PhotonView>() : null;
            if (env == null || pv == null || target == null) return;

            int sent = 0;
            foreach (var n in env.notes)
            {
                if (n.isPrivate || string.IsNullOrEmpty(n.cloudAnchorId)) continue;
                pv.RPC("RpcPlaceNote", target, JsonUtility.ToJson(n));
                sent++;
            }
            foreach (var o in env.objects)
            {
                if (o.isPrivate || string.IsNullOrEmpty(o.cloudAnchorId)) continue;
                pv.RPC("RpcPlaceObject", target, JsonUtility.ToJson(o));
                sent++;
            }
            Debug.Log($"[AuraSync] Sent {sent} shared item(s) to {Networking.NetworkManager.DisplayName(target)}");
        }

        private void SendPlacementRpc(string rpcName, System.Func<string> toJson)
        {
            // Hosting is asynchronous; the user may have left the room in the meantime.
            if (!Photon.Pun.PhotonNetwork.InRoom) return;

            var pv = Networking.NetworkManager.Instance?.GetComponent<Photon.Pun.PhotonView>();
            if (pv == null) return;

            pv.RPC(rpcName, Photon.Pun.RpcTarget.Others, toJson());
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
                    Networking.ZoneManager.Instance?.UnregisterItem(id);
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
                    Networking.ZoneManager.Instance?.UnregisterItem(id);
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
                noteObj.AddComponent<ARAnchor>(); // re-anchor so restored items stay put as tracking refines
                noteObj.transform.localScale = noteData.GetScale();
                var ctrl = noteObj.GetComponent<Notes.NoteController>();
                if (ctrl != null) ctrl.Initialize(noteData);
                _activeItems[noteData.id] = noteObj;
                Networking.ZoneManager.Instance?.RegisterPlacedItem(noteData);
            }

            foreach (var objData in env.objects)
            {
                GameObject prefab = GetObjectPrefab(objData.prefabName);
                if (prefab == null) continue;

                GameObject obj = Instantiate(prefab, objData.GetPosition(), objData.GetRotation());
                obj.AddComponent<ARAnchor>(); // re-anchor so restored items stay put as tracking refines
                obj.transform.localScale = objData.GetScale();
                var ctrl = obj.GetComponent<Objects.ObjectController>();
                if (ctrl != null) ctrl.Initialize(objData);
                _activeItems[objData.id] = obj;
                Networking.ZoneManager.Instance?.RegisterPlacedItem(objData);
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

        // ──────────────────────────────────────────
        //  Network Placement (called by RPC)
        // ──────────────────────────────────────────
        // ──────────────────────────────────────────
        //  Incoming edits and deletions
        // ──────────────────────────────────────────

        /// <summary>Applies an edit another player made to a note.</summary>
        public void NetworkUpdateNote(NoteItemData config)
        {
            if (config == null) return;

            // Saving must not bounce the change straight back to whoever sent it.
            Networking.ItemSync.ApplyingRemoteChange = true;
            try
            {
                PersistenceManager.Instance?.UpdateNote(config);

                if (_activeItems.TryGetValue(config.id, out GameObject go) && go != null)
                {
                    var ctrl = go.GetComponent<Notes.NoteController>();
                    if (ctrl != null) ctrl.Initialize(config);
                    go.transform.localScale = config.GetScale();
                }
            }
            finally { Networking.ItemSync.ApplyingRemoteChange = false; }

            Debug.Log($"[AuraSync] Note updated from the room: {config.id}");
        }

        /// <summary>Applies an edit another player made to a 3D object.</summary>
        public void NetworkUpdateObject(ObjectItemData config)
        {
            if (config == null) return;

            Networking.ItemSync.ApplyingRemoteChange = true;
            try
            {
                PersistenceManager.Instance?.UpdateObject(config);

                if (_activeItems.TryGetValue(config.id, out GameObject go) && go != null)
                {
                    var ctrl = go.GetComponent<Objects.ObjectController>();
                    if (ctrl != null) ctrl.Initialize(config);
                    go.transform.localScale = config.GetScale();
                }
            }
            finally { Networking.ItemSync.ApplyingRemoteChange = false; }

            Debug.Log($"[AuraSync] Object updated from the room: {config.id}");
        }

        /// <summary>Removes an item another player deleted.</summary>
        public void NetworkDeleteItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;

            Networking.ItemSync.ApplyingRemoteChange = true;
            try
            {
                PersistenceManager.Instance?.RemoveNote(itemId);
                PersistenceManager.Instance?.RemoveObject(itemId);
                Networking.ZoneManager.Instance?.UnregisterItem(itemId);

                if (_activeItems.TryGetValue(itemId, out GameObject go))
                {
                    if (go != null) Destroy(go);
                    _activeItems.Remove(itemId);
                }
            }
            finally { Networking.ItemSync.ApplyingRemoteChange = false; }

            Debug.Log($"[AuraSync] Item deleted from the room: {itemId}");
        }

        public void NetworkPlaceNote(NoteItemData config)
        {
            if (_activeItems.ContainsKey(config.id)) return; // already placed (for example a late-join re-send)

            if (!string.IsNullOrEmpty(config.cloudAnchorId) && Networking.CloudAnchorManager.Instance != null)
            {
                ShowPlacementFeedback("Resolving note from Cloud Anchor...");
                Networking.CloudAnchorManager.Instance.ResolveAnchor(config.cloudAnchorId, (resolvedTransform) => 
                {
                    Networking.SharedSpaceOrigin.RegisterAnchor(config.cloudAnchorId, resolvedTransform);
                    int prefabIndex = GetNotePrefabIndex(config.noteStyle, config.noteType);
                    if (prefabIndex < 0 || prefabIndex >= notePrefabs.Length) return;

                    GameObject noteObj = Instantiate(notePrefabs[prefabIndex], resolvedTransform);
                    noteObj.transform.localPosition = Vector3.zero;
                    noteObj.transform.localRotation = Quaternion.identity;
                    noteObj.transform.localScale = config.GetScale();

                    var ctrl = noteObj.GetComponent<Notes.NoteController>();
                    if (ctrl != null) ctrl.Initialize(config);

                    _activeItems[config.id] = noteObj;
                    PersistenceManager.Instance.AddNote(config);
            Networking.ZoneManager.Instance?.RegisterPlacedItem(config);
                    Debug.Log($"[AuraSync] Network Note perfectly aligned via Cloud Anchor!");
                }, (error) => 
                {
                    ShowPlacementFeedback("Cloud Anchor Resolve Failed. Falling back to relative coords.");
                    SpawnNoteFallback(config);
                });
            }
            else
            {
                SpawnNoteFallback(config);
            }
        }

        private void SpawnNoteFallback(NoteItemData config)
        {
            int prefabIndex = GetNotePrefabIndex(config.noteStyle, config.noteType);
            if (prefabIndex < 0 || prefabIndex >= notePrefabs.Length) return;

            Vector3 pos = config.GetPosition();
            Quaternion rotation = config.GetRotation();
            
            GameObject noteObj = Instantiate(notePrefabs[prefabIndex], pos, rotation);
            noteObj.transform.localScale = config.GetScale();

            var ctrl = noteObj.GetComponent<Notes.NoteController>();
            if (ctrl != null) ctrl.Initialize(config);

            _activeItems[config.id] = noteObj;
            PersistenceManager.Instance.AddNote(config);
            Networking.ZoneManager.Instance?.RegisterPlacedItem(config);
            Debug.Log($"[AuraSync] Network Note placed at {pos} (Fallback/No Cloud ID)");
        }

        public void NetworkPlaceObject(ObjectItemData config)
        {
            if (_activeItems.ContainsKey(config.id)) return; // already placed (for example a late-join re-send)

            if (!string.IsNullOrEmpty(config.cloudAnchorId) && Networking.CloudAnchorManager.Instance != null)
            {
                ShowPlacementFeedback("Resolving object from Cloud Anchor...");
                Networking.CloudAnchorManager.Instance.ResolveAnchor(config.cloudAnchorId, (resolvedTransform) => 
                {
                    Networking.SharedSpaceOrigin.RegisterAnchor(config.cloudAnchorId, resolvedTransform);
                    GameObject prefab = GetObjectPrefab(config.prefabName);
                    if (prefab == null) return;

                    GameObject obj = Instantiate(prefab, resolvedTransform);
                    obj.transform.localPosition = Vector3.zero;
                    obj.transform.localRotation = Quaternion.identity;
                    obj.transform.localScale = config.GetScale();

                    var ctrl = obj.GetComponent<Objects.ObjectController>();
                    if (ctrl != null) ctrl.Initialize(config);

                    _activeItems[config.id] = obj;
                    PersistenceManager.Instance.AddObject(config);
            Networking.ZoneManager.Instance?.RegisterPlacedItem(config);
                    Debug.Log($"[AuraSync] Network Object perfectly aligned via Cloud Anchor!");
                }, (error) => 
                {
                    ShowPlacementFeedback("Cloud Anchor Resolve Failed. Falling back to relative coords.");
                    SpawnObjectFallback(config);
                });
            }
            else
            {
                SpawnObjectFallback(config);
            }
        }

        private void SpawnObjectFallback(ObjectItemData config)
        {
            GameObject prefab = GetObjectPrefab(config.prefabName);
            if (prefab == null) return;

            Vector3 pos = config.GetPosition();
            Quaternion rotation = config.GetRotation();

            GameObject obj = Instantiate(prefab, pos, rotation);
            obj.transform.localScale = config.GetScale();

            var ctrl = obj.GetComponent<Objects.ObjectController>();
            if (ctrl != null) ctrl.Initialize(config);

            _activeItems[config.id] = obj;
            PersistenceManager.Instance.AddObject(config);
            Networking.ZoneManager.Instance?.RegisterPlacedItem(config);
            Debug.Log($"[AuraSync] Network Object placed: {config.prefabName} at {pos} (Fallback/No Cloud ID)");
        }
    }

    [System.Serializable]
    public class ObjectPrefabEntry
    {
        public string     name;
        public GameObject prefab;
    }
}