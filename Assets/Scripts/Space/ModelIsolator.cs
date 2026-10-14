using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Space
{
    /// <summary>
    /// Isolate: hide everything except the chosen part, and stand that part up as a model in its
    /// own right so it can be turned over and inspected freely.
    ///
    /// Exploded view spreads the model out; this does the opposite job, taking everything else away.
    /// For "what does this one component actually look like" it is the clearer of the two, because
    /// nothing is left in front of or behind the part to read through.
    ///
    /// **Hiding alone is not enough.** A part sits wherever it sits inside the model, usually well
    /// off to one side of the pivot everything rotates about. Hide its neighbours and it still
    /// swings in a wide arc around a centre that is no longer visible, which reads as a fragment
    /// still bolted to something rather than an object you are holding. So isolating also brings the
    /// part to the pivot and scales it up to fill the view; undoing the isolation puts it back.
    ///
    /// **In a room** (2026-10-12) it is shared the way the explosion is:
    ///   - The presenter's isolation stays on their phone until they answer the "show to everyone?"
    ///     question with Accept; then it goes to the room (<see cref="RoomKeys.IsolatedPart"/>) and
    ///     every viewer who is following sees it. Deny keeps it on the presenter's phone only.
    ///   - A viewer may isolate on their own phone at any time. That is exploring, like dragging the
    ///     model: it breaks following, reaches nobody else, and Catch Up brings back the presenter's
    ///     shared isolation along with their view.
    /// Outside a room it is simply yours.
    ///
    /// **Phase 1:** "the presenter" above means whoever holds the Isolate control
    /// (<see cref="ControlOwnership"/>). A student holding it is asked the same question (with KEEP
    /// SHARING, see <see cref="LiveSharing"/>) and stays following; a presenter who handed it out
    /// follows the holder's isolation and gets the control back with TAKE BACK.
    ///
    /// Renderers are switched off rather than their GameObjects deactivated, which keeps the
    /// hierarchy intact for part paths, and means the raycaster and the bounds helper skip the
    /// hidden parts for free - tapping "through" a hidden part finds what is actually visible.
    /// </summary>
    public class ModelIsolator : MonoBehaviour
    {
        public static ModelIsolator Instance { get; private set; }

        /// <summary>Largest the isolated part may be blown up, so a tiny screw stays believable.</summary>
        private const float MaxFitScale = 20f;

        /// <summary>Raised when isolation starts or ends, so the drawer's button can follow.</summary>
        public event System.Action OnIsolationChanged;

        /// <summary>Part path currently isolated, or "" when the whole model is showing.</summary>
        public string IsolatedPath { get; private set; } = "";

        public bool IsActive => !string.IsNullOrEmpty(IsolatedPath);

        private readonly List<Renderer> _hidden = new List<Renderer>();

        // The one transform we move. It is moved through PartPoseComposer's Isolation layer, so
        // there is no "original" to remember: clearing the layer puts it back.
        private Transform _movedPart;

        private ModelExploder _boundExploder;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            StartCoroutine(BindExploderWhenReady());
            ControlOwnership.OnChanged += OnOwnershipChanged;
        }

        void OnDestroy()
        {
            ControlOwnership.OnChanged -= OnOwnershipChanged;
            if (_boundExploder != null) _boundExploder.OnAmountChanged -= OnExplodeChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Someone else now drives isolation: a presenter who handed it out, or a following holder
        /// who lost it, goes to what the room shows.
        /// </summary>
        private void OnOwnershipChanged()
        {
            if (FollowsRoom()) ApplyFromRoom(ReadRoomIsolation());
            OnIsolationChanged?.Invoke(); // the drawer's button locks or unlocks
        }

        private System.Collections.IEnumerator BindExploderWhenReady()
        {
            while (ModelExploder.Instance == null) yield return null;
            _boundExploder = ModelExploder.Instance;
            _boundExploder.OnAmountChanged += OnExplodeChanged;
        }

        /// <summary>
        /// The explosion moves the isolated part (or the pieces inside an isolated group) along with
        /// everything else. Re-centre it, so it stays where isolation put it: in the middle of the
        /// view. Without this, moving the explode slider while isolated slid the part out of sight.
        /// </summary>
        private void OnExplodeChanged()
        {
            if (!IsActive || _movedPart == null) return; // _movedPart is Unity-null once destroyed

            Transform root = SpaceInteractionController.Instance != null
                ? SpaceInteractionController.Instance.ModelRoot
                : null;
            if (root != null) Centre(root, _movedPart);
        }

        /// <summary>Called by SpaceSceneManager when a model loads: the old model's parts are gone.</summary>
        public void OnModelLoaded()
        {
            // Nothing to put back - those transforms and renderers no longer exist.
            _hidden.Clear();
            _movedPart   = null;
            IsolatedPath = "";

            // A viewer arriving at (or switched to) a model the presenter has isolated arrives
            // isolated. The presenter restores their own view separately (ModelStateMemory).
            if (IsFollowingViewer())
            {
                string shared = ReadRoomIsolation();
                if (!string.IsNullOrEmpty(shared)) Apply(shared, isLocal: false);
            }

            OnIsolationChanged?.Invoke();
        }

        // ── Room sharing ───────────────────────────────────────────────────────

        /// <summary>True when this device is the room's presenter (used for their own per-model memory).</summary>
        public static bool IsPresenting =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        /// <summary>True when this device's isolation can be offered to the room: it holds the Isolate control.</summary>
        public static bool DrivesRoom => ControlOwnership.LocalDrivesRoom(Control.Isolate);

        /// <summary>
        /// False for a presenter who has handed Isolate to a student: they watch the student drive
        /// it until they take it back. Everyone else may isolate (a viewer on their own phone).
        /// </summary>
        public static bool LocalPlayerMayIsolate => !(IsPresenting && !DrivesRoom);

        private static bool IsFollowingViewer()
        {
            if (!PhotonNetwork.InRoom || IsPresenting) return false;
            var sic = SpaceInteractionController.Instance;
            return sic == null || sic.IsSyncLocked;
        }

        /// <summary>
        /// True when the room's isolation should be shown here: a following viewer who does not
        /// drive it, or a presenter watching the student they handed it to.
        /// </summary>
        private static bool FollowsRoom()
        {
            if (!PhotonNetwork.InRoom || DrivesRoom) return false;
            if (IsPresenting) return true;
            var sic = SpaceInteractionController.Instance;
            return sic == null || sic.IsSyncLocked;
        }

        /// <summary>The presenter's shared isolation ("" = whole model, or not in a room).</summary>
        public static string ReadRoomIsolation()
        {
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeys.IsolatedPart, out object v))
            {
                return v as string ?? "";
            }
            return "";
        }

        /// <summary>Called by ModelSyncManager when the presenter shares a new isolation.</summary>
        public void OnRoomIsolationChanged(string path)
        {
            if (!FollowsRoom()) return; // whoever drives it, or a viewer exploring on their own
            ApplyFromRoom(path);
        }

        /// <summary>Takes the presenter's shared isolation whatever this device was doing. Catch Up calls this.</summary>
        public void SyncFromRoom()
        {
            if (IsPresenting) return;
            ApplyFromRoom(ReadRoomIsolation());
        }

        private void ApplyFromRoom(string path)
        {
            path = path ?? "";
            if (path == IsolatedPath) return;
            Apply(path, isLocal: false);
        }

        /// <summary>
        /// After this device's user changed the isolation: the presenter is asked whether to show it
        /// to everyone; a viewer has started exploring on their own.
        /// </summary>
        private void AfterUserChange()
        {
            if (!PhotonNetwork.InRoom) return;

            if (!DrivesRoom)
            {
                var sic = SpaceInteractionController.Instance;
                if (sic != null && sic.IsSyncLocked) sic.BreakSync();
                return;
            }

            if (IsolatedPath == ReadRoomIsolation()) return; // the room already shows exactly this

            bool isolating = IsActive;
            LiveSharing.Offer(Control.Isolate,
                isolating ? "Show this isolation to everyone?" : "Show the whole model to everyone?",
                isolating ? "Accept: everyone following you sees only this part.\nDeny: only you see it."
                          : "Accept: everyone following you leaves isolation too.\nDeny: only you leave it.",
                share: () =>
                {
                    // Whatever is isolated *now*: the user may have moved on while the question was open.
                    if (DrivesRoom)
                        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeys.IsolatedPart, IsolatedPath } });
                });
        }

        // ── Isolating ──────────────────────────────────────────────────────────

        /// <summary>
        /// Shows only the given part and whatever sits under it, centred and sized to fill the view.
        /// Isolating a group therefore shows the whole sub-assembly, which is usually what someone
        /// picking a group meant. A user action: in a room it is offered to the room or kept local
        /// as described above.
        /// </summary>
        public void Isolate(string partPath)
        {
            if (!LocalPlayerMayIsolate) { SayHandedOut(); return; }
            Apply(partPath, isLocal: true);
            AfterUserChange();
        }

        /// <summary>Brings the whole model back. A user action, like <see cref="Isolate"/>.</summary>
        public void ShowAll()
        {
            if (!LocalPlayerMayIsolate) { SayHandedOut(); return; }
            if (!IsActive && _hidden.Count == 0 && _movedPart == null) return;
            Apply("", isLocal: true);
            AfterUserChange();
        }

        /// <summary>Why nothing happened, for the ISOLATE buttons outside the drawer (AI chat, FIND).</summary>
        private static void SayHandedOut() =>
            Toast.Show($"{ControlOwnership.HolderName(Control.Isolate)} drives Isolate. TAKE BACK is in the Parts drawer.");

        /// <summary>
        /// Puts an isolation on screen without asking or telling anyone: a remembered view being
        /// restored. "" shows the whole model.
        /// </summary>
        public void ApplyQuietly(string partPath) => Apply(partPath ?? "", isLocal: false);

        private void Apply(string partPath, bool isLocal)
        {
            if (string.IsNullOrEmpty(partPath)) { ShowAllInternal(isLocal); return; }

            Transform root = SpaceInteractionController.Instance != null
                ? SpaceInteractionController.Instance.ModelRoot
                : null;

            Transform part = ModelRaycaster.FindPart(root, partPath);
            if (root == null || part == null)
            {
                ShowAllInternal(isLocal);
                return;
            }

            // Undo any previous isolation first, so measurements below see the whole model.
            RestorePartTransform();
            RestoreRenderers();

            if (!TryGetWorldBounds(root, out Bounds modelBounds) ||
                !TryGetWorldBounds(part, out Bounds partBounds))
            {
                ShowAllInternal(isLocal);
                return;
            }

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r.transform.IsChildOf(part)) continue; // IsChildOf counts the part itself

                r.enabled = false;
                _hidden.Add(r);
            }

            StandPartUp(root, part, modelBounds, partBounds);

            IsolatedPath = partPath;
            SharedStateBus.Publish(StateKeys.Isolation, IsolatedPath, isLocal);
            OnIsolationChanged?.Invoke();
            Debug.Log($"[ModelIsolator] Isolated '{part.name}', hiding {_hidden.Count} renderer(s).");
        }

        /// <summary>
        /// Moves the part onto the model's pivot and scales it to fill roughly the space the whole
        /// model did, so rotating and pinching act on the part itself.
        /// </summary>
        private void StandPartUp(Transform root, Transform part, Bounds modelBounds, Bounds partBounds)
        {
            _movedPart = part;

            float modelSize = Mathf.Max(modelBounds.size.x, Mathf.Max(modelBounds.size.y, modelBounds.size.z));
            float partSize  = Mathf.Max(partBounds.size.x,  Mathf.Max(partBounds.size.y,  partBounds.size.z));

            // Only ever grow: a part that already fills the view needs no help.
            float fit = 1f;
            if (partSize > 1e-6f && modelSize > 1e-6f)
                fit = Mathf.Clamp(modelSize / partSize, 1f, MaxFitScale);

            PartPoseComposer.SetScale(part, PoseLayer.Isolation, fit);
            Centre(root, part);
        }

        /// <summary>
        /// Shifts the part so its centre lands on the root's origin, which is what the viewer
        /// rotates and zooms about. Measured with the Isolation layer's own offset taken out, so it
        /// can be called again whenever another layer has moved the part.
        /// </summary>
        private static void Centre(Transform root, Transform part)
        {
            PartPoseComposer.SetOffset(part, PoseLayer.Isolation, Vector3.zero);

            // Scaling and the other layers have moved it, so measure where it is now.
            if (!TryGetWorldBounds(part, out Bounds current)) return;

            // The offset has to be expressed in the part's own parent space, since that is what
            // the composer's offsets (like localPosition) are measured in.
            Vector3 centreInRoot = root.InverseTransformPoint(current.center);
            Vector3 offsetWorld  = root.TransformVector(-centreInRoot);
            Vector3 offsetLocal  = part.parent != null
                ? part.parent.InverseTransformVector(offsetWorld)
                : offsetWorld;

            PartPoseComposer.SetOffset(part, PoseLayer.Isolation, offsetLocal);
        }

        /// <summary>Brings the whole model back, with the isolated part returned to where it belongs.</summary>
        private void ShowAllInternal(bool isLocal)
        {
            if (!IsActive && _hidden.Count == 0 && _movedPart == null) return;

            RestorePartTransform();
            RestoreRenderers();

            // No need to ask the exploder to reassert itself any more: clearing the Isolation layer
            // leaves the Explode layer exactly as it was.

            IsolatedPath = "";
            SharedStateBus.Publish(StateKeys.Isolation, IsolatedPath, isLocal);
            OnIsolationChanged?.Invoke();
        }

        /// <summary>Re-isolates the current part, for when the selection moves while isolated.</summary>
        public void Reapply(string partPath)
        {
            if (!IsActive) return;

            // A following viewer's isolation belongs to the presenter: the presenter's selection
            // moving must not count as this viewer isolating something (that would break their
            // following). The presenter's shared isolation arrives through the room instead. The
            // same goes for a presenter who has handed Isolate to a student.
            if (FollowsRoom() || !LocalPlayerMayIsolate) return;

            // Whoever drives isolation follows their own picks, not somebody else's highlight
            // moving (the student holding Highlight, or the presenter): otherwise every pick by
            // someone else would pop up "show this isolation to everyone?" on this phone.
            var sel = ModelPartSelector.Instance;
            bool someoneElsesPick = sel != null && string.IsNullOrEmpty(sel.LocalPath) &&
                                    partPath == sel.PresenterPath && !ModelPartSelector.DrivesRoom;
            if (DrivesRoom && PhotonNetwork.InRoom && someoneElsesPick) return;

            if (string.IsNullOrEmpty(partPath)) ShowAll();
            else Isolate(partPath);
        }

        private void RestoreRenderers()
        {
            foreach (Renderer r in _hidden)
            {
                if (r != null) r.enabled = true; // a destroyed renderer is Unity-null, so skipped
            }
            _hidden.Clear();
        }

        private void RestorePartTransform()
        {
            if (_movedPart != null) PartPoseComposer.Clear(_movedPart, PoseLayer.Isolation);
            _movedPart = null;
        }

        /// <summary>Combined world bounds of everything drawn under a transform, highlights aside.</summary>
        private static bool TryGetWorldBounds(Transform t, out Bounds bounds)
        {
            bounds = default;
            if (t == null) return false;

            bool any = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                if (r.gameObject.name == ModelRaycaster.HighlightName) continue;

                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return any;
        }
    }
}
