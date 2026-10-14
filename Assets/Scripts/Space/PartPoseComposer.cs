using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// The layers that may move a part, in the order they are combined.
    ///
    /// Isolation is last on purpose: it stands a part up at the pivot and scales it to fill the
    /// view, so it has to see where every other layer has put the part first.
    /// </summary>
    public enum PoseLayer
    {
        Explode    = 0, // ModelExploder: slides parts outward from the centre
        Simulation = 1, // Phase 7: assembly and working animation
        Challenge  = 2, // Phase 12: reassembly game drags
        Isolation  = 3, // ModelIsolator: centre and enlarge one part
    }

    /// <summary>
    /// The one place that writes a part's local position, rotation and scale.
    ///
    /// **Why this exists.** The exploded view and isolation both used to set
    /// <c>part.localPosition</c> directly, each remembering its own "original" position. Two
    /// writers that each think they own the transform overwrite each other: move the explode slider
    /// while a part is isolated and the exploder put the part back where it would sit in the
    /// explosion, off-centre and out of view. Simulation and the reassembly game would be a third and
    /// fourth writer.
    ///
    /// So no feature moves a part any more. Each one hands this class an *offset* on its own layer,
    /// and the composer adds the layers up on top of the part's resting pose:
    ///
    /// <code>
    ///   position = rest position + Σ layer offsets      (all in the part's parent space)
    ///   rotation = rest rotation × layer rotations, in layer order
    ///   scale    = rest scale    × Π layer scale factors
    /// </code>
    ///
    /// A layer can therefore be added, changed or removed without knowing about any other, and
    /// clearing every layer returns the part *exactly* to where the model file put it.
    ///
    /// The resting pose is captured the first time a feature touches a part. That is only correct
    /// because nothing else moves parts: the viewer rotates and scales the model's root, never its
    /// parts. Anything new that needs to move a part must come through here too.
    ///
    /// Static rather than a component: it has no per-frame work and no scene presence, and
    /// <see cref="Reset"/> is called when a new model loads.
    /// </summary>
    public static class PartPoseComposer
    {
        private const int LayerCount = 4;

        private class Pose
        {
            public Vector3    restPosition;
            public Quaternion restRotation;
            public Vector3    restScale;

            public readonly Vector3[]    offset   = new Vector3[LayerCount];
            public readonly Quaternion[] rotation = new Quaternion[LayerCount];
            public readonly float[]      scale    = new float[LayerCount];
            public int activeMask;

            public Pose(Transform t)
            {
                restPosition = t.localPosition;
                restRotation = t.localRotation;
                restScale    = t.localScale;

                for (int i = 0; i < LayerCount; i++)
                {
                    rotation[i] = Quaternion.identity;
                    scale[i]    = 1f;
                }
            }
        }

        private static readonly Dictionary<Transform, Pose> _poses = new Dictionary<Transform, Pose>();

        // ── Writing layers ─────────────────────────────────────────────────────

        /// <summary>Sets one layer's position offset, in the part's parent space.</summary>
        public static void SetOffset(Transform part, PoseLayer layer, Vector3 offsetInParent)
        {
            Pose p = GetOrCreate(part);
            if (p == null) return;

            p.offset[(int)layer] = offsetInParent;
            p.activeMask |= 1 << (int)layer;
            Write(part, p);
        }

        /// <summary>Sets one layer's rotation, applied in the part's own local frame.</summary>
        public static void SetRotation(Transform part, PoseLayer layer, Quaternion localRotation)
        {
            Pose p = GetOrCreate(part);
            if (p == null) return;

            p.rotation[(int)layer] = localRotation;
            p.activeMask |= 1 << (int)layer;
            Write(part, p);
        }

        /// <summary>Sets one layer's uniform scale factor (1 = unchanged).</summary>
        public static void SetScale(Transform part, PoseLayer layer, float factor)
        {
            Pose p = GetOrCreate(part);
            if (p == null) return;

            p.scale[(int)layer] = factor;
            p.activeMask |= 1 << (int)layer;
            Write(part, p);
        }

        /// <summary>
        /// Removes one layer's contribution. When no layer is left the part is back at exactly its
        /// resting pose and is forgotten, so the next feature to touch it captures it afresh.
        /// </summary>
        public static void Clear(Transform part, PoseLayer layer)
        {
            if (part == null || !_poses.TryGetValue(part, out Pose p)) return;

            int i = (int)layer;
            p.offset[i]   = Vector3.zero;
            p.rotation[i] = Quaternion.identity;
            p.scale[i]    = 1f;
            p.activeMask &= ~(1 << i);

            Write(part, p);
            if (p.activeMask == 0) _poses.Remove(part);
        }

        /// <summary>Removes one layer from every part it is on.</summary>
        public static void ClearLayer(PoseLayer layer)
        {
            int bit = 1 << (int)layer;

            // Copy the keys: Clear() removes entries from the dictionary as it goes.
            var parts = new List<Transform>();
            foreach (var kv in _poses)
                if ((kv.Value.activeMask & bit) != 0) parts.Add(kv.Key);

            foreach (Transform t in parts) Clear(t, layer);
        }

        // ── Reading ────────────────────────────────────────────────────────────

        /// <summary>True when the given layer is currently moving this part.</summary>
        public static bool HasLayer(Transform part, PoseLayer layer)
        {
            return part != null && _poses.TryGetValue(part, out Pose p) &&
                   (p.activeMask & (1 << (int)layer)) != 0;
        }

        /// <summary>
        /// Where the part sits with no feature moving it. For a part nothing has touched, that is
        /// simply its current pose.
        /// </summary>
        public static void GetRestPose(Transform part, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (part != null && _poses.TryGetValue(part, out Pose p))
            {
                position = p.restPosition;
                rotation = p.restRotation;
                scale    = p.restScale;
                return;
            }

            position = part != null ? part.localPosition : Vector3.zero;
            rotation = part != null ? part.localRotation : Quaternion.identity;
            scale    = part != null ? part.localScale    : Vector3.one;
        }

        /// <summary>Number of parts currently moved by at least one layer. For tests and logs.</summary>
        public static int MovedPartCount => _poses.Count;

        /// <summary>
        /// Forgets every part. Called when a model loads: the previous model's transforms are
        /// destroyed, and the new one's are all at rest.
        /// </summary>
        public static void Reset() => _poses.Clear();

        // ── Internals ──────────────────────────────────────────────────────────

        private static Pose GetOrCreate(Transform part)
        {
            if (part == null) return null; // also catches a destroyed transform

            if (!_poses.TryGetValue(part, out Pose p))
            {
                p = new Pose(part);
                _poses[part] = p;
            }
            return p;
        }

        private static void Write(Transform part, Pose p)
        {
            if (part == null) return;

            Vector3    pos   = p.restPosition;
            Quaternion rot   = p.restRotation;
            float      scale = 1f;

            for (int i = 0; i < LayerCount; i++)
            {
                if ((p.activeMask & (1 << i)) == 0) continue;
                pos   += p.offset[i];
                rot   *= p.rotation[i];
                scale *= p.scale[i];
            }

            part.localPosition = pos;
            part.localRotation = rot;
            part.localScale    = p.restScale * scale;
        }
    }
}
