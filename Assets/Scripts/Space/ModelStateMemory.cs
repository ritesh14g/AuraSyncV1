using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using AuraSync.Networking;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Space
{
    /// <summary>
    /// Remembers how each model was left, so going back to it later brings it back the same way.
    ///
    /// Two memories, because the presenter may have kept some of their view to themselves (they
    /// answered Deny to "show to everyone?"):
    ///   - **This device's own view** per model: explosion, isolation, selection and the model's
    ///     position / rotation / zoom. Kept in memory for the app session.
    ///   - **What the room was shown** per model, filed by the presenter as a room property
    ///     ("MS:" + hash of the model key). When the presenter switches models, the new model's
    ///     entry travels in the same room update as the model itself, so every viewer opens it in
    ///     the state it was left in, and a late joiner or a new presenter gets it too.
    ///
    /// Flow: <see cref="CaptureCurrent"/> when leaving a model (Back to the model list) ->
    /// <see cref="SharedStateFor"/> inside ModelSyncManager.BroadcastModelChange ->
    /// <see cref="RestoreAfterLoad"/> once the next model is on screen.
    /// </summary>
    public static class ModelStateMemory
    {
        private struct Snapshot
        {
            public float      explode;
            public string     isolation;
            public string     selection;
            public bool       hasPose;
            public Vector3    position;
            public Quaternion rotation;
            public Vector3    scale;
            public float[]    section; // SectionCutter.CutState, packed; null = no cut
        }

        private static readonly Dictionary<string, Snapshot> _ownView = new Dictionary<string, Snapshot>();

        private const string FieldExplode   = "e";
        private const string FieldIsolation = "i";
        private const string FieldSelection = "s";
        private const string FieldPose      = "p"; // float[10]: position xyz, rotation xyzw, scale xyz
        private const string FieldSection   = "c"; // float[5]: the section cut (Phase 6)

        private static bool IsPresenting =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        /// <summary>Room property key holding a model's shared state. Short and identical on every device.</summary>
        public static string RoomKeyFor(string modelKey) => RoomKeys.ModelStatePrefix + Fnv1a(modelKey ?? "");

        /// <summary>A short hash of a model key, identical on every device. For other per-model room keys.</summary>
        public static string ModelHash(string modelKey) => Fnv1a(modelKey ?? "");

        /// <summary>
        /// Files the model on screen before this device leaves it. Safe to call with nothing loaded.
        /// </summary>
        public static void CaptureCurrent()
        {
            // A replay's explode / isolation / pose are the recording's, not how this person left it.
            if (AuraSync.Recording.SessionPlayer.IsReplaying) return;

            string key = SpaceSceneManager.CurrentModelKey;
            if (string.IsNullOrEmpty(key)) return;

            var snap = new Snapshot
            {
                explode   = ModelExploder.Instance != null ? ModelExploder.Instance.Amount : 0f,
                isolation = ModelIsolator.Instance != null ? ModelIsolator.Instance.IsolatedPath : "",
                selection = OwnSelection(),
                section   = SectionCutter.Instance != null ? SectionCutter.Instance.State.Pack() : null,
            };

            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            if (root != null)
            {
                snap.hasPose  = true;
                snap.position = root.position;
                snap.rotation = root.rotation;
                snap.scale    = root.localScale;
            }

            _ownView[key] = snap;

            if (!IsPresenting) return;

            // What the room was shown, which can differ from the presenter's own view after a Deny.
            var props  = PhotonNetwork.CurrentRoom.CustomProperties;
            var shared = new Hashtable
            {
                { FieldExplode,   props.TryGetValue(RoomKeys.ExplodeAmount, out object e) && e is float f ? f : 0f },
                { FieldIsolation, props.TryGetValue(RoomKeys.IsolatedPart,  out object i) ? i as string ?? "" : "" },
                { FieldSelection, props.TryGetValue(RoomKeys.SelectedPart,  out object s) ? s as string ?? "" : "" },
                { FieldSection,   SectionCutter.ReadRoom().Pack() },
            };
            if (snap.hasPose) shared[FieldPose] = PackPose(snap);

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeyFor(key), shared } });
            Debug.Log($"[ModelStateMemory] Filed '{key}': explode {snap.explode:0.##}, isolated '{snap.isolation}', " +
                      $"selected '{snap.selection}' (room: explode {shared[FieldExplode]}, isolated '{shared[FieldIsolation]}').");
        }

        /// <summary>
        /// The live room values a model switch should carry: the model's remembered shared state, or
        /// a clean start (assembled, nothing isolated or selected) for a model not opened before.
        /// </summary>
        public static Hashtable SharedStateFor(string modelKey)
        {
            var h = new Hashtable
            {
                { RoomKeys.ExplodeAmount, 0f },
                { RoomKeys.IsolatedPart,  "" },
                { RoomKeys.SelectedPart,  "" },
                { RoomKeys.SectionPlane,  SectionCutter.CutState.Off.Pack() },
                { RoomKeys.Simulation,    AuraSync.Simulation.SimState.Off.ToRoom() }, // a new model starts at rest
            };

            if (TryReadRoom(modelKey, out Snapshot s))
            {
                h[RoomKeys.ExplodeAmount] = s.explode;
                h[RoomKeys.IsolatedPart]  = s.isolation;
                h[RoomKeys.SelectedPart]  = s.selection;
                if (s.section != null) h[RoomKeys.SectionPlane] = s.section;
            }
            return h;
        }

        /// <summary>
        /// Called by SpaceSceneManager once a model is loaded and every part tool has reset itself.
        /// The presenter (and anyone outside a room) gets their own view of the model back; a viewer
        /// in a room follows the presenter instead, from the room state that arrived with the switch.
        /// </summary>
        public static void RestoreAfterLoad(string modelKey)
        {
            if (string.IsNullOrEmpty(modelKey)) return;
            if (PhotonNetwork.InRoom && !IsPresenting) return;

            if (!_ownView.TryGetValue(modelKey, out Snapshot s) &&
                !(IsPresenting && TryReadRoom(modelKey, out s)))
            {
                if (!IsPresenting) return; // a model never opened before: it is already in its fresh state

                // The presenter's tools may still hold room values from the previous model (the
                // room's copy of the switch can arrive after this). A new model starts clean.
                s = new Snapshot { explode = 0f, isolation = "", selection = "" };
            }

            // Selection first, while nothing is isolated: a selection change moves an active isolation.
            ModelPartSelector.Instance?.RestoreSelection(s.selection);
            ModelExploder.Instance?.ApplyQuietly(s.explode);
            ModelIsolator.Instance?.ApplyQuietly(s.isolation);
            SectionCutter.Instance?.ApplyQuietly(SectionCutter.CutState.Unpack(s.section));

            // The angle and zoom come back; the position does not. A model always opens centred in
            // front of the camera (the user's rule, 2026-10-16), and a position saved before the
            // content was centred on its anchor would put it off-screen again.
            var sic = SpaceInteractionController.Instance;
            if (s.hasPose && sic != null && sic.ModelRoot != null)
                sic.RestorePose(sic.ModelRoot.position, s.rotation, s.scale);

            Debug.Log($"[ModelStateMemory] Restored '{modelKey}': explode {s.explode:0.##}, isolated '{s.isolation}', " +
                      $"selected '{s.selection}', pose {(s.hasPose ? "yes" : "no")}.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string OwnSelection()
        {
            var sel = ModelPartSelector.Instance;
            if (sel == null) return "";
            return IsPresenting ? sel.PresenterPath : sel.LocalPath;
        }

        private static bool TryReadRoom(string modelKey, out Snapshot s)
        {
            s = default;
            if (!PhotonNetwork.InRoom) return false;
            if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeyFor(modelKey), out object raw)) return false;
            if (!(raw is Hashtable h)) return false;

            s.explode   = h.TryGetValue(FieldExplode,   out object e) && e is float f ? f : 0f;
            s.isolation = h.TryGetValue(FieldIsolation, out object i) ? i as string ?? "" : "";
            s.selection = h.TryGetValue(FieldSelection, out object sl) ? sl as string ?? "" : "";
            s.section   = h.TryGetValue(FieldSection, out object sc) ? sc as float[] : null;

            if (h.TryGetValue(FieldPose, out object p) && p is float[] a && a.Length == 10)
            {
                s.hasPose  = true;
                s.position = new Vector3(a[0], a[1], a[2]);
                s.rotation = new Quaternion(a[3], a[4], a[5], a[6]);
                s.scale    = new Vector3(a[7], a[8], a[9]);
            }
            return true;
        }

        private static float[] PackPose(Snapshot s) => new[]
        {
            s.position.x, s.position.y, s.position.z,
            s.rotation.x, s.rotation.y, s.rotation.z, s.rotation.w,
            s.scale.x,    s.scale.y,    s.scale.z,
        };

        /// <summary>
        /// 32-bit FNV-1a as hex. Not string.GetHashCode: that is free to differ between runtimes and
        /// processes, and every device has to arrive at the same key.
        /// </summary>
        private static string Fnv1a(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash.ToString("x8");
            }
        }
    }
}
