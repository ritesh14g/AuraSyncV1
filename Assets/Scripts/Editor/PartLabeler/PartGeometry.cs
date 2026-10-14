using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuraSync.EditorTools
{
    /// <summary>
    /// The parts of the labelling that geometry does better than an AI looking at pictures:
    ///
    /// * **Which parts touch** (connectsTo): spheres dropped around each part's surface.
    /// * **Which way a part turns or slides** (motion axis): the shape of its vertex cloud. A fan is
    ///   a flat disc, so it spins about its thinnest direction; a shaft is long and thin, so it spins
    ///   about its longest one.
    /// * **The order the model comes apart in**: a part can come out when it can slide in some
    ///   direction without hitting any part still in place. Remove every part that can, repeat. The
    ///   reverse is the assembly order.
    ///
    /// Works on temporary MeshColliders on an unused layer, all removed again by <see cref="Dispose"/>.
    /// </summary>
    internal sealed class PartGeometry : IDisposable
    {
        private readonly Transform _root;
        private readonly int _layer;
        private readonly Dictionary<Transform, MeshCollider> _colliders = new Dictionary<Transform, MeshCollider>();
        private readonly Dictionary<Collider, Transform> _owner = new Dictionary<Collider, Transform>();
        private readonly Dictionary<Transform, int> _savedLayer = new Dictionary<Transform, int>();
        private readonly bool _savedBackfaces;

        public readonly float ModelSize;

        public PartGeometry(Transform root, IEnumerable<Transform> parts, int layer)
        {
            _root = root;
            _layer = layer;
            _savedBackfaces = Physics.queriesHitBackfaces;

            // Rays start inside casings and travel outward, which meets the inside (back) of the
            // casing's faces. Without this they would pass straight through.
            Physics.queriesHitBackfaces = true;

            Bounds all = default;
            bool any = false;
            foreach (Transform part in parts)
            {
                MeshFilter mf = part.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                _savedLayer[part] = part.gameObject.layer;
                part.gameObject.layer = layer;

                MeshCollider mc = part.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.hideFlags = HideFlags.DontSave;
                _colliders[part] = mc;
                _owner[mc] = part;

                Renderer r = part.GetComponent<Renderer>();
                if (r != null)
                {
                    if (!any) { all = r.bounds; any = true; }
                    else all.Encapsulate(r.bounds);
                }
            }

            ModelSize = any ? all.size.magnitude : 1f;
            Physics.SyncTransforms();
        }

        public void Dispose()
        {
            foreach (var kv in _colliders)
                if (kv.Value != null) UnityEngine.Object.DestroyImmediate(kv.Value);
            foreach (var kv in _savedLayer)
                if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
            Physics.queriesHitBackfaces = _savedBackfaces;
            Physics.SyncTransforms();
        }

        // ── Touching parts ─────────────────────────────────────────────────────

        /// <summary>Parts within 1% of the model's size of this part's surface.</summary>
        public List<Transform> Neighbours(Transform part)
        {
            var found = new HashSet<Transform>();
            float radius = ModelSize * 0.01f;
            foreach (Vector3 p in SampleWorld(part, 150))
            {
                foreach (Collider c in Physics.OverlapSphere(p, radius, 1 << _layer, QueryTriggerInteraction.Ignore))
                {
                    if (_owner.TryGetValue(c, out Transform other) && other != part) found.Add(other);
                }
            }
            return found.ToList();
        }

        // ── Motion axis ────────────────────────────────────────────────────────

        /// <summary>
        /// A motion axis and pivot in the part's local space, from the shape of its vertices.
        /// Snapped to the nearest local axis when within 8 degrees of it, which is almost always
        /// what the modeller intended.
        /// </summary>
        public static void EstimateMotion(Transform part, string motionType, out Vector3 axis, out Vector3 pivot)
        {
            axis = Vector3.up;
            pivot = Vector3.zero;

            MeshFilter mf = part.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            Vector3[] v = mf.sharedMesh.vertices;
            if (v.Length < 3) return;

            int step = Mathf.Max(1, v.Length / 5000);
            Vector3 mean = Vector3.zero;
            int n = 0;
            for (int i = 0; i < v.Length; i += step) { mean += v[i]; n++; }
            mean /= n;

            // Covariance of the vertex cloud.
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int i = 0; i < v.Length; i += step)
            {
                Vector3 d = v[i] - mean;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z;
                yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            var m = new double[3, 3] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };
            Eigen3(m, out double[] values, out Vector3[] vectors); // ascending

            switch (motionType)
            {
                case "rotate":
                    // A disc (fan) has one small spread and two similar large ones: spin about the
                    // small one. A shaft has two small and one large: spin about the large one.
                    axis = (values[2] - values[1]) > (values[1] - values[0]) ? vectors[2] : vectors[0];
                    pivot = mean;
                    break;
                case "slide":
                    axis = vectors[2];
                    pivot = mean;
                    break;
                case "hinge":
                    // A door: thin along its normal, tall along the hinge. Hinge on one long edge.
                    axis = vectors[2];
                    float halfWidth = Mathf.Sqrt((float)Math.Max(values[1], 0)) * 1.7f;
                    pivot = mean - vectors[1] * halfWidth;
                    break;
                default:
                    axis = Vector3.zero;
                    pivot = mean;
                    return;
            }

            axis = Snap(axis.normalized, 8f);
        }

        private static Vector3 Snap(Vector3 a, float maxDegrees)
        {
            foreach (Vector3 c in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                if (Vector3.Angle(a, c) <= maxDegrees) return c;
                if (Vector3.Angle(a, -c) <= maxDegrees) return c;
            }
            return a;
        }

        /// <summary>Jacobi eigen-decomposition of a symmetric 3x3 matrix; values ascending.</summary>
        private static void Eigen3(double[,] a, out double[] values, out Vector3[] vectors)
        {
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            var m = (double[,])a.Clone();

            for (int sweep = 0; sweep < 50; sweep++)
            {
                double off = Math.Abs(m[0, 1]) + Math.Abs(m[0, 2]) + Math.Abs(m[1, 2]);
                if (off < 1e-12) break;

                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(m[p, q]) < 1e-15) continue;
                    double theta = (m[q, q] - m[p, p]) / (2 * m[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;

                    for (int k = 0; k < 3; k++)
                    {
                        double mkp = m[k, p], mkq = m[k, q];
                        m[k, p] = c * mkp - s * mkq;
                        m[k, q] = s * mkp + c * mkq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double mpk = m[p, k], mqk = m[q, k];
                        m[p, k] = c * mpk - s * mqk;
                        m[q, k] = s * mpk + c * mqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }

            var order = new[] { 0, 1, 2 }.OrderBy(i => m[i, i]).ToArray();
            values = order.Select(i => m[i, i]).ToArray();
            vectors = order.Select(i => new Vector3((float)v[0, i], (float)v[1, i], (float)v[2, i]).normalized).ToArray();
        }

        // ── Assembly order ─────────────────────────────────────────────────────

        public class Removal
        {
            public Transform part;
            public Vector3 directionInModel; // zero when it had to be forced out
            public bool forced;
        }

        /// <summary>
        /// The order the parts come out in (first removed first), each with the direction it slides.
        /// </summary>
        public List<Removal> PlanDisassembly(IList<Transform> parts)
        {
            var remaining = new HashSet<Transform>(parts.Where(p => _colliders.ContainsKey(p)));
            var order = new List<Removal>();

            // Prefer sliding along the model's longest axis: that is how most machines come apart.
            Vector3[] dirs = PreferredDirections();
            Bounds modelBounds = WorldBounds(remaining);

            while (remaining.Count > 0)
            {
                var free = new List<Removal>();
                Transform leastBlocked = null;
                float leastBlockedFraction = float.MaxValue;
                Vector3 leastBlockedDir = Vector3.zero;

                foreach (Transform part in remaining)
                {
                    Vector3[] samples = SampleWorld(part, 120);
                    foreach (Vector3 d in dirs)
                    {
                        float blocked = BlockedFraction(part, samples, _root.TransformDirection(d), remaining);
                        if (blocked == 0f)
                        {
                            free.Add(new Removal { part = part, directionInModel = d });
                            break;
                        }
                        if (blocked < leastBlockedFraction)
                        {
                            leastBlockedFraction = blocked;
                            leastBlocked = part;
                            leastBlockedDir = d;
                        }
                    }
                }

                if (free.Count == 0)
                {
                    // Interlocked (or the mesh has gaps that rays slip through oddly): take out the
                    // part that is least in the way and flag it for review.
                    free.Add(new Removal { part = leastBlocked, directionInModel = leastBlockedDir, forced = true });
                }

                // Outermost first within a round, so the order reads naturally.
                foreach (Removal r in free.OrderByDescending(r => (WorldCentre(r.part) - modelBounds.center).sqrMagnitude))
                {
                    order.Add(r);
                    remaining.Remove(r.part);
                    if (_colliders.TryGetValue(r.part, out MeshCollider mc)) mc.enabled = false;
                }
                Physics.SyncTransforms();
            }

            foreach (var mc in _colliders.Values) if (mc != null) mc.enabled = true;
            Physics.SyncTransforms();
            return order;
        }

        private Vector3[] PreferredDirections()
        {
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            Bounds local = default;
            bool any = false;
            foreach (var part in _colliders.Keys)
            {
                Renderer r = part.GetComponent<Renderer>();
                if (r == null) continue;
                Vector3 c = _root.InverseTransformPoint(r.bounds.center);
                if (!any) { local = new Bounds(c, Vector3.zero); any = true; }
                else local.Encapsulate(c);
            }
            Vector3 size = local.size;
            var ordered = axes.OrderByDescending(a => Mathf.Abs(Vector3.Dot(a, size))).ToList();
            var result = new List<Vector3>();
            foreach (Vector3 a in ordered) { result.Add(a); result.Add(-a); }
            return result.ToArray();
        }

        private float BlockedFraction(Transform part, Vector3[] samples, Vector3 dirWorld, HashSet<Transform> remaining)
        {
            if (samples.Length == 0) return 0f;
            float maxDist = ModelSize * 2f;
            float start = ModelSize * 0.001f;
            int blocked = 0;

            foreach (Vector3 s in samples)
            {
                foreach (RaycastHit hit in Physics.RaycastAll(s + dirWorld * start, dirWorld, maxDist, 1 << _layer, QueryTriggerInteraction.Ignore))
                {
                    if (_owner.TryGetValue(hit.collider, out Transform other) && other != part && remaining.Contains(other))
                    {
                        blocked++;
                        break;
                    }
                }
            }
            return blocked / (float)samples.Length;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        public static Vector3[] SampleWorld(Transform part, int max)
        {
            MeshFilter mf = part.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return Array.Empty<Vector3>();
            Vector3[] v = mf.sharedMesh.vertices;
            int step = Mathf.Max(1, v.Length / max);
            var list = new List<Vector3>(max + 1);
            for (int i = 0; i < v.Length; i += step) list.Add(part.TransformPoint(v[i]));
            return list.ToArray();
        }

        private static Vector3 WorldCentre(Transform part)
        {
            Renderer r = part.GetComponent<Renderer>();
            return r != null ? r.bounds.center : part.position;
        }

        private static Bounds WorldBounds(IEnumerable<Transform> parts)
        {
            Bounds b = default;
            bool any = false;
            foreach (Transform p in parts)
            {
                Renderer r = p.GetComponent<Renderer>();
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }
    }
}
