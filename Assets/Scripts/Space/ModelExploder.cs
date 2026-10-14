using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using AuraSync.Networking;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Space
{
    /// <summary>
    /// Exploded view: slides every part of the model outward from its centre so the inside can be
    /// seen and the pieces told apart.
    ///
    /// Each part moves along the line from the model's centre through its own centre, by a distance
    /// proportional to the model's size, so one slider works the same on a house and on a jet engine.
    /// The offsets are stored and applied in the part's own parent space, so rotating or zooming the
    /// exploded model behaves exactly as it does when assembled.
    ///
    /// In a room, only the presenter's explosion is shared, and only when they say so: on letting go
    /// of the slider they are asked "show this to everyone?" (2026-10-12). Accept puts the amount
    /// in a room property, which followers apply and late joiners arrive at; Deny keeps it on the
    /// presenter's phone. A viewer's own explosion is exploring and stays on their phone until
    /// Catch Up. Outside a room there is no presenter, so it is simply yours to use.
    ///
    /// **Phase 1:** "the presenter" above means whoever holds the Explode control
    /// (<see cref="ControlOwnership"/>), which the presenter can hand to a student. The holder is
    /// asked the same question (with KEEP SHARING, see <see cref="LiveSharing"/>), using the slider
    /// does not break their following, and a presenter who handed it out watches the holder's
    /// explosion and cannot drag their own until they take the control back.
    ///
    /// Parts are moved through <see cref="PartPoseComposer"/> on the Explode layer, never by
    /// writing their transforms directly, so the explosion stacks with isolation and simulation
    /// instead of overwriting them.
    /// </summary>
    public class ModelExploder : MonoBehaviour
    {
        public static ModelExploder Instance { get; private set; }

        /// <summary>Room property carrying the presenter's explode amount (0..1), as a float.</summary>
        public const string KeyExplode = RoomKeys.ExplodeAmount;

        /// <summary>How far a part travels at full explosion, relative to the model's size.</summary>
        private const float SpreadFactor = 0.85f;

        /// <summary>Raised when the amount changes, so the drawer's slider can follow along.</summary>
        public event System.Action OnAmountChanged;

        /// <summary>Current explosion, 0 = assembled, 1 = fully apart.</summary>
        public float Amount { get; private set; }

        /// <summary>False when a model has no separable parts, so the UI can say so.</summary>
        public bool CanExplode => _parts.Count > 1;

        private class Piece
        {
            public Transform transform;
            public Vector3   directionInParentSpace; // unit-ish offset for Amount = 1
        }

        private readonly List<Piece> _parts = new List<Piece>();
        private Transform _root;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            ControlOwnership.OnChanged += OnOwnershipChanged;
        }

        void OnDestroy()
        {
            ControlOwnership.OnChanged -= OnOwnershipChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Someone else now drives the explosion: a presenter who handed it out, or a holder who
        /// lost it while following, goes to what the room shows (a Deny may have left them with a
        /// private explosion). Someone exploring on their own keeps theirs until Catch Up.
        /// </summary>
        private void OnOwnershipChanged()
        {
            if (!PhotonNetwork.InRoom || DrivesRoom) { OnAmountChanged?.Invoke(); return; }

            var sic = SpaceInteractionController.Instance;
            if (IsPresenting || sic == null || sic.IsSyncLocked) ApplyFromRoom(ReadRoomAmount());
            OnAmountChanged?.Invoke(); // the drawer's slider locks or unlocks
        }

        /// <summary>Called by SpaceSceneManager once a model is loaded and fitted.</summary>
        public void OnModelLoaded()
        {
            _parts.Clear();
            Amount = 0f;

            _root = SpaceInteractionController.Instance != null
                ? SpaceInteractionController.Instance.ModelRoot
                : null;

            Collect();

            // A model opened while the room is already exploded should arrive exploded. Not for the
            // presenter: their own view of each model is restored by ModelStateMemory, and on a
            // model switch the room value read here can still be the previous model's.
            float shared = IsPresenting ? 0f : ReadRoomAmount();
            if (shared > 0f)
            {
                Apply(shared);
                SharedStateBus.Publish(StateKeys.Explode, Amount, isLocal: false);
            }

            OnAmountChanged?.Invoke();
            Debug.Log($"[ModelExploder] {_parts.Count} movable part(s) on this model.");
        }

        private void Collect()
        {
            if (_root == null) return;

            if (!ModelRaycaster.TryGetLocalBounds(_root, out Bounds modelBounds)) return;
            Vector3 centre = modelBounds.center;

            // Size drives the travel distance, so the same slider reads the same on any model.
            float spread = Mathf.Max(modelBounds.size.x,
                           Mathf.Max(modelBounds.size.y, modelBounds.size.z)) * SpreadFactor;
            if (spread <= 0f) return;

            foreach (Renderer r in _root.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null) continue;
                if (r.gameObject.name == ModelRaycaster.HighlightName) continue; // our own selection box
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

                Transform part = r.transform;
                if (part == _root || part.parent == null) continue;

                if (!ModelRaycaster.TryGetLocalBounds(part, out Bounds partBounds)) continue;

                // Work out the outward direction in the model's own space, so it does not depend on
                // how the model happens to be rotated at the moment the slider is used.
                Vector3 partCentreInRoot = _root.InverseTransformPoint(part.TransformPoint(partBounds.center));
                Vector3 outward = partCentreInRoot - centre;

                // A part sitting dead on the centre has no outward direction; push it up so it still
                // separates instead of staying buried inside everything else.
                outward = outward.sqrMagnitude < 1e-8f ? Vector3.up : outward.normalized;

                Vector3 offsetInRoot = outward * spread;
                Vector3 offsetInParent = part.parent.InverseTransformVector(_root.TransformVector(offsetInRoot));

                _parts.Add(new Piece
                {
                    transform              = part,
                    directionInParentSpace = offsetInParent
                });
            }
        }

        // ── Driving it ─────────────────────────────────────────────────────────

        /// <summary>True when this device is the room's presenter (used for their own per-model memory).</summary>
        public static bool IsPresenting =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        /// <summary>True when this device's slider feeds the room: it holds the Explode control.</summary>
        public static bool DrivesRoom => ControlOwnership.LocalDrivesRoom(Control.Explode);

        /// <summary>
        /// Everyone may explode their own copy. A viewer doing so is exploring on their own, exactly
        /// as dragging the model is, and it reaches nobody else. Catch Up brings them back to the
        /// presenter's explosion along with the presenter's view.
        ///
        /// The one exception is a presenter who has handed Explode to a student: they are watching
        /// the student drive it, and get it back with TAKE BACK.
        /// </summary>
        public static bool LocalPlayerMayExplode => !(IsPresenting && !DrivesRoom);

        /// <summary>
        /// Called by the drawer's slider while it moves. Only this device changes here: the
        /// presenter is asked whether to show the result to everyone when they let go of the slider
        /// (<see cref="CommitInteraction"/>), so a drag is one question, not one per frame.
        /// <paramref name="broadcast"/> false = a programmatic change that is neither shared nor
        /// counts as the viewer exploring (the reassembly challenge resetting the model).
        /// </summary>
        public void SetAmount(float amount, bool broadcast = true)
        {
            if (broadcast && !LocalPlayerMayExplode) { OnAmountChanged?.Invoke(); return; }

            Apply(amount);

            if (broadcast && DrivesRoom)
            {
                // Shared, or not, on release: see CommitInteraction.
            }
            else if (broadcast)
            {
                // A viewer taking the slider has stopped mirroring the presenter, the same as if
                // they had dragged the model. Leaving them locked would let the presenter's next
                // change yank the model back together under their hands.
                var sic = SpaceInteractionController.Instance;
                if (PhotonNetwork.InRoom && sic != null && sic.IsSyncLocked) sic.BreakSync();
            }

            SharedStateBus.Publish(StateKeys.Explode, Amount, isLocal: true);
            OnAmountChanged?.Invoke();
        }

        /// <summary>
        /// The presenter let go of the slider. If the explosion now differs from what the room
        /// shows, ask whether to show it to everyone: Accept shares it (followers' models move
        /// apart too), Deny keeps it on the presenter's phone only.
        /// </summary>
        public void CommitInteraction()
        {
            if (!DrivesRoom) return;
            if (Mathf.Abs(Amount - ReadRoomAmount()) < 0.005f) return; // the room already shows this

            bool apart = Amount > 0.005f;
            LiveSharing.Offer(Control.Explode,
                apart ? "Show this exploded view to everyone?" : "Put the model back together for everyone?",
                apart ? "Accept: everyone following you sees the parts pulled apart like this.\nDeny: only you see it."
                      : "Accept: everyone following you sees it assembled again.\nDeny: only you do.",
                share: () =>
                {
                    // The amount *now*: the slider may have moved again while the question was open.
                    if (DrivesRoom)
                        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyExplode, Amount } });
                });
        }

        /// <summary>
        /// Puts an explosion on screen without sharing it or counting as exploring: a remembered
        /// view being restored.
        /// </summary>
        public void ApplyQuietly(float amount)
        {
            Apply(amount);
            SharedStateBus.Publish(StateKeys.Explode, Amount, isLocal: false);
            OnAmountChanged?.Invoke();
        }

        /// <summary>
        /// Re-asserts the current explosion. Before PartPoseComposer, isolation needed this to put
        /// the explosion back over a part it had moved; the composer now keeps the layers apart, so
        /// nothing in Scripts/ calls it any more. Kept so nothing outside Scripts/ breaks.
        /// </summary>
        public void Reapply() => Apply(Amount);

        private void Apply(float amount)
        {
            Amount = Mathf.Clamp01(amount);

            foreach (Piece p in _parts)
            {
                if (p.transform == null) continue;

                // At zero the layer is removed rather than set to a zero offset, so a fully
                // assembled model has nothing left on it and its parts are exactly at rest.
                if (Amount <= 0f) PartPoseComposer.Clear(p.transform, PoseLayer.Explode);
                else PartPoseComposer.SetOffset(p.transform, PoseLayer.Explode, p.directionInParentSpace * Amount);
            }
        }

        private static float ReadRoomAmount()
        {
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyExplode, out object v) &&
                v is float f)
            {
                return f;
            }
            return 0f;
        }

        /// <summary>
        /// Called by ModelSyncManager when the room's explode amount changes.
        ///
        /// Only followed while this device is still mirroring the presenter. Someone who has gone
        /// off to explore keeps the explosion they chose until they press Catch Up.
        /// </summary>
        public void OnRoomAmountChanged(float amount)
        {
            if (DrivesRoom) return;          // our own value coming back to us

            // A presenter who handed Explode out watches the holder drive it.
            if (IsPresenting) { ApplyFromRoom(amount); return; }

            var sic = SpaceInteractionController.Instance;
            if (sic != null && !sic.IsSyncLocked) return; // exploring independently

            ApplyFromRoom(amount);
        }

        /// <summary>
        /// Takes the room's explosion whatever this device was doing. Catch Up calls this, which is
        /// what makes the button bring back the presenter's whole view and not just their angle.
        /// </summary>
        public void SyncFromRoom()
        {
            if (IsPresenting) return;
            ApplyFromRoom(ReadRoomAmount());
        }

        private void ApplyFromRoom(float amount)
        {
            if (Mathf.Approximately(amount, Amount)) return;

            Apply(amount);
            SharedStateBus.Publish(StateKeys.Explode, Amount, isLocal: false);
            OnAmountChanged?.Invoke();
        }
    }
}
