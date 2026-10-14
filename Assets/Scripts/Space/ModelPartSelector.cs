using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using AuraSync.Networking;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Space
{
    /// <summary>
    /// Tap a part of the 3D model to highlight it.
    ///
    ///  - The presenter's selection is shared: it is stored in a room property, so every viewer
    ///    sees it highlighted in gold, and a late joiner picks it up on arrival.
    ///  - A viewer's own tap highlights in cyan on their device only, so they can explore
    ///    independently without disturbing the presenter or anyone else.
    ///  - Offline (not in a room) every tap is a local selection.
    ///  - Phase 1: "the presenter" above means whoever holds the Highlight control
    ///    (<see cref="ControlOwnership"/>). A presenter who handed it to a student sees the
    ///    student's picks in gold and their own in cyan, like any viewer, until they take it back.
    ///    Highlighting is shared as it happens, with no "show to everyone?" question, as before.
    /// A part is identified by its child-index path (see ModelRaycaster.GetPartPath), which is the
    /// same on every device because every device builds the same hierarchy from the same GLB.
    /// </summary>
    public class ModelPartSelector : MonoBehaviourPunCallbacks
    {
        public static ModelPartSelector Instance { get; private set; }

        /// <summary>Raised whenever either selection changes (for the HUD label).</summary>
        public event Action OnSelectionChanged;

        public string PresenterPartName { get; private set; } = "";
        public string LocalPartName     { get; private set; } = "";

        /// <summary>Part path currently highlighted for the room, or "". For the parts drawer.</summary>
        public string PresenterPath => _presenterPath;

        /// <summary>Part path this device picked for itself, or "". For the parts drawer.</summary>
        public string LocalPath => _localPath;

        private static readonly Color PresenterColor = new Color(1.00f, 0.82f, 0.20f);
        private static readonly Color LocalColor     = new Color(0.30f, 0.95f, 1.00f);

        private SpaceInteractionController _sic;
        private string     _presenterPath = "";
        private string     _localPath     = "";
        private GameObject _presenterBox;
        private GameObject _localBox;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnDisable()
        {
            base.OnDisable();
            if (_sic != null) _sic.OnTap -= HandleTap;
            _sic = null;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            StartCoroutine(BindWhenReady());
        }

        void OnDestroy()
        {
            if (_knowledge != null) _knowledge.OnChanged -= RebuildHighlights;
            if (_sic != null) _sic.OnTap -= HandleTap;
            if (Instance == this) Instance = null;
        }

        private System.Collections.IEnumerator BindWhenReady()
        {
            while (SpaceInteractionController.Instance == null) yield return null;

            _sic = SpaceInteractionController.Instance;
            _sic.OnTap -= HandleTap; // never double-subscribe
            _sic.OnTap += HandleTap;

            // The part labels arrive a moment after the model: redraw so the chip shows them.
            while (AuraSync.AI.PartKnowledge.Instance == null) yield return null;
            _knowledge = AuraSync.AI.PartKnowledge.Instance;
            _knowledge.OnChanged -= RebuildHighlights;
            _knowledge.OnChanged += RebuildHighlights;
        }

        private AuraSync.AI.PartKnowledge _knowledge;

        /// <summary>Called by SpaceSceneManager after a model has been loaded and fitted.</summary>
        public void OnModelLoaded()
        {
            ModelRaycaster.ClearCache();

            // The old model (and the highlight boxes parented to its parts) is gone.
            _presenterBox = null;
            _localBox     = null;
            _localPath    = "";

            // A presenter selection may already be waiting in the room (late join / model switch).
            _presenterPath = ReadRoomSelection();
            RebuildHighlights();
        }

        // ── Input ──────────────────────────────────────────────────────────────

        private void HandleTap(Vector2 screenPos)
        {
            // In the reassembly challenge a tap is part of moving pieces, not picking one.
            if (AuraSync.Challenge.ReassemblyGame.IsActive) return;

            // While a note is being placed, the tap says where the note goes (Phase 5).
            if (AuraSync.Notes.PartNoteManager.ConsumesTap) return;

            Transform root = _sic != null ? _sic.ModelRoot : null;
            Camera cam = Camera.main;
            if (root == null || cam == null) return;

            string path = "";
            if (ModelRaycaster.Raycast(cam.ScreenPointToRay(screenPos), root, out Renderer hit, out _))
                path = ModelRaycaster.GetPartPath(root, hit.transform);

            SelectByPath(path);
        }

        /// <summary>True when this device is the room's presenter (used for their own per-model memory).</summary>
        public static bool IsPresenting =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        /// <summary>True when this device's picks are shared with the room: it holds the Highlight control.</summary>
        public static bool DrivesRoom => ControlOwnership.LocalDrivesRoom(Control.Highlight);

        /// <summary>The part this device picked itself: the room's pick when it drives Highlight.</summary>
        public string OwnPath => DrivesRoom ? _presenterPath : _localPath;

        /// <summary>
        /// Select a part by its path, under the same rules as tapping it in the 3D view: the
        /// Highlight holder's pick is shared with the room, everyone else's stays on their own device, and
        /// choosing what is already selected clears it. Used by the parts drawer; pass "" to clear.
        /// </summary>
        public void SelectByPath(string path)
        {
            path = path ?? "";

            if (DrivesRoom)
            {
                // Choosing the selected part again, or empty space, clears it.
                SetPresenterSelection(path == _presenterPath ? "" : path, broadcast: true);
            }
            else
            {
                _localPath = (path == _localPath) ? "" : path;
                RebuildHighlights();
                SharedStateBus.Publish(StateKeys.LocalSelection, _localPath, isLocal: true);
            }
        }

        /// <summary>
        /// Puts back a remembered selection when a model is reopened (ModelStateMemory) without
        /// re-sending it: for the presenter the room already received it with the model change.
        /// </summary>
        public void RestoreSelection(string path)
        {
            path = path ?? "";
            if (IsPresenting) SetPresenterSelection(path, broadcast: false);
            else if (!PhotonNetwork.InRoom) { _localPath = path; RebuildHighlights(); }
            // A viewer in a room follows the presenter's selection, which arrives from the room.
        }

        /// <summary>
        /// Lights a part on this phone only, without counting it as a pick or sharing it: a quiz
        /// question about that part (Phase 11). "" clears it. Ignored while this phone drives the
        /// room's highlight, so a student holding Highlight never shows the class the answer.
        /// </summary>
        public void ShowLocalHighlight(string path)
        {
            if (DrivesRoom) return;
            _localPath = path ?? "";
            RebuildHighlights();
        }

        /// <summary>Drop this device's selection outright (no toggling). For the drawer's Clear.</summary>
        public void ClearSelection()
        {
            if (DrivesRoom)
            {
                SetPresenterSelection("", broadcast: true);
            }
            else
            {
                _localPath = "";
                RebuildHighlights();
                SharedStateBus.Publish(StateKeys.LocalSelection, _localPath, isLocal: true);
            }
        }

        // ── Shared selection ───────────────────────────────────────────────────

        private void SetPresenterSelection(string path, bool broadcast)
        {
            _presenterPath = path ?? "";
            RebuildHighlights();
            SharedStateBus.Publish(StateKeys.PresenterSelection, _presenterPath, isLocal: broadcast);

            if (broadcast && PhotonNetwork.InRoom)
            {
                PhotonNetwork.CurrentRoom.SetCustomProperties(
                    new Hashtable { { ModelSyncManager.KeySelectedPart, _presenterPath } });
            }
        }

        private static string ReadRoomSelection()
        {
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(ModelSyncManager.KeySelectedPart, out object v))
            {
                return v as string ?? "";
            }
            return "";
        }

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            if (!propertiesThatChanged.ContainsKey(ModelSyncManager.KeySelectedPart)) return;

            string path = propertiesThatChanged[ModelSyncManager.KeySelectedPart] as string ?? "";
            if (path == _presenterPath) return; // our own change echoing back

            SetPresenterSelection(path, broadcast: false);
        }

        public override void OnJoinedRoom()
        {
            string path = ReadRoomSelection();
            if (path != _presenterPath) SetPresenterSelection(path, broadcast: false);
        }

        public override void OnLeftRoom()
        {
            SetPresenterSelection("", broadcast: false);
        }

        // ── Highlighting ───────────────────────────────────────────────────────

        private void RebuildHighlights()
        {
            Transform root = _sic != null ? _sic.ModelRoot : null;

            PresenterPartName = ApplyHighlight(ref _presenterBox, root, _presenterPath, PresenterColor);
            LocalPartName     = ApplyHighlight(ref _localBox,     root, _localPath,     LocalColor);

            OnSelectionChanged?.Invoke();
        }

        /// <summary>Returns the part's display name, or "" when nothing is highlighted.</summary>
        private static string ApplyHighlight(ref GameObject box, Transform root, string path, Color color)
        {
            if (box != null) Destroy(box);
            box = null;

            Transform part = ModelRaycaster.FindPart(root, path);
            if (part == null) return "";

            // Covers a single mesh and a group alike: a group is boxed around everything inside it,
            // which is what makes picking one from the parts drawer worth doing.
            string shown = AuraSync.AI.PartNames.Display(path, part.name); // "Fan rotor", not "Object_6.1"
            if (!ModelRaycaster.TryGetLocalBounds(part, out Bounds b)) return shown; // nothing to box

            box = BuildWireBox(part, b, color);
            return shown;
        }

        /// <summary>
        /// Draws the selection as the twelve edges of its bounding box rather than a solid block.
        ///
        /// A filled box hid the very part it was meant to point at - and it relied on the Standard
        /// shader's transparent variant surviving into the build, which is exactly the kind of thing
        /// a device build strips, leaving a solid slab of colour over the model. Thin opaque edges
        /// need no transparency at all and never cover anything.
        /// </summary>
        private static GameObject BuildWireBox(Transform part, Bounds b, Color color)
        {
            GameObject root = new GameObject(ModelRaycaster.HighlightName);
            Transform rt = root.transform;
            rt.SetParent(part, false);
            rt.localPosition = b.center;
            rt.localRotation = Quaternion.identity;
            rt.localScale    = Vector3.one;

            Vector3 size = Vector3.Max(b.size, Vector3.one * 0.0001f);

            // Thin against the part's own size, so it reads the same on a door handle and a fuselage.
            float thickness = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.012f;
            Material material = HighlightMaterial(color);

            Vector3 h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++)
            {
                // The four edges parallel to this axis sit at the four combinations of the other two.
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 pos   = Vector3.zero;
                    Vector3 scale = Vector3.one * thickness;

                    int a = (axis + 1) % 3;
                    int c = (axis + 2) % 3;

                    pos[a] = ((corner & 1) == 0 ? -1f : 1f) * h[a];
                    pos[c] = ((corner & 2) == 0 ? -1f : 1f) * h[c];

                    // Overshoot by the thickness so the corners meet instead of leaving gaps.
                    scale[axis] = size[axis] + thickness;

                    CreateEdge(rt, pos, scale, material);
                }
            }

            return root;
        }

        // Only two colours are ever used, and the drawer makes selection changes cheap and frequent
        // (scrolling a list and tapping rows), so building a material per highlight would pile up
        // hundreds of them over a session.
        private static readonly Dictionary<Color, Material> _highlightMaterials =
            new Dictionary<Color, Material>();

        private static Material HighlightMaterial(Color color)
        {
            if (_highlightMaterials.TryGetValue(color, out Material cached) && cached != null)
                return cached;

            Material m = PointerVisuals.CreateGlowing(color);
            _highlightMaterials[color] = m;
            return m;
        }

        private static void CreateEdge(Transform parent, Vector3 localPos, Vector3 localScale, Material material)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);

            // Named like the parent so the raycaster, the bounds helper and the parts catalogue all
            // skip it - a highlight must never be mistaken for part of the model.
            edge.name = ModelRaycaster.HighlightName;

            Collider col = edge.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Transform t = edge.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            t.localScale    = localScale;

            Renderer r = edge.GetComponent<Renderer>();
            r.sharedMaterial    = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
        }
    }
}
