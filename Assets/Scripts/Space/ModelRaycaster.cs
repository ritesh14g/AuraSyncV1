using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// Ray vs. loaded model, without physics colliders.
    ///
    /// glTFast models have no colliders, and baking a MeshCollider per part is slow on phones.
    /// Instead: cull parts by their bounds, then test the ray against the triangles of the few
    /// parts that survive. Meshes that are not CPU-readable (or are huge) fall back to the bounds
    /// hit, which is approximate but never wrong about which part was touched.
    /// </summary>
    public static class ModelRaycaster
    {
        private const int MaxTrianglesForExactTest = 150000;

        /// <summary>Name given to selection highlight boxes; the raycaster ignores them.</summary>
        public const string HighlightName = "PartHighlight";

        private class MeshData
        {
            public Vector3[] vertices;
            public int[]     triangles;
        }

        private static readonly Dictionary<Mesh, MeshData> _cache = new Dictionary<Mesh, MeshData>();

        /// <summary>Forget cached mesh data. Call when a new model is loaded.</summary>
        public static void ClearCache() => _cache.Clear();

        /// <summary>
        /// Set by SectionCutter (Phase 6): true for a world point the section cut has removed. A tap
        /// goes through the cut-away half to whatever is actually visible behind it.
        /// </summary>
        public static Func<Vector3, bool> IsPointCut;

        /// <summary>
        /// Finds the nearest part of the model hit by the ray.
        /// </summary>
        /// <returns>True on a hit. renderer is the part, worldPoint the point on its surface.</returns>
        public static bool Raycast(Ray ray, Transform root, out Renderer renderer, out Vector3 worldPoint)
            => Raycast(ray, root, out renderer, out worldPoint, out _);

        /// <summary>
        /// As above, plus the surface direction at the hit (world space, facing the ray). Exact
        /// for meshes small enough to test triangle by triangle; otherwise it points back along the
        /// ray, which is still the side the user tapped. Part notes (Phase 5) stand on this.
        /// </summary>
        public static bool Raycast(Ray ray, Transform root, out Renderer renderer, out Vector3 worldPoint, out Vector3 worldNormal)
        {
            renderer    = null;
            worldPoint  = Vector3.zero;
            worldNormal = -ray.direction;
            Vector3 bestNormal = -ray.direction;
            if (root == null) return false;

            float best = float.MaxValue;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.gameObject.name == HighlightName) continue;

                // The distance at which the ray enters the bounds can never be beaten by a surface
                // hit inside them, so parts whose entry is already farther than the best are skipped.
                if (!r.bounds.IntersectRay(ray, out float boundsDist)) continue;
                if (boundsDist > best) continue;

                float hitDist = boundsDist; // approximate unless refined below
                Vector3 hitNormal = -ray.direction;
                Mesh mesh = GetMesh(r);
                if (mesh != null && mesh.isReadable && r is MeshRenderer)
                {
                    MeshData data = GetMeshData(mesh);
                    if (data != null && !RaycastMesh(ray, r.transform, data, out hitDist, out hitNormal)) continue;
                    if (data == null && IsPointCut != null && IsPointCut(ray.GetPoint(hitDist))) continue;
                }
                else if (IsPointCut != null && IsPointCut(ray.GetPoint(hitDist))) continue;

                if (hitDist < best)
                {
                    best       = hitDist;
                    renderer   = r;
                    bestNormal = hitNormal;
                }
            }

            if (renderer == null) return false;

            worldPoint  = ray.GetPoint(best);
            worldNormal = bestNormal;
            return true;
        }

        private static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;

            MeshFilter mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>Cached vertex/triangle arrays, or null when the mesh is too large to test exactly.</summary>
        private static MeshData GetMeshData(Mesh mesh)
        {
            if (_cache.TryGetValue(mesh, out MeshData data)) return data;

            int triCount = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) triCount += (int)mesh.GetIndexCount(i) / 3;

            data = triCount > MaxTrianglesForExactTest
                ? null
                : new MeshData { vertices = mesh.vertices, triangles = mesh.triangles };

            _cache[mesh] = data;
            return data;
        }

        private static bool RaycastMesh(Ray worldRay, Transform t, MeshData data, out float distance, out Vector3 worldNormal)
        {
            distance = 0f;
            worldNormal = -worldRay.direction;
            int bestTri = -1;

            // Work in the part's local space. The ray parameter is identical in both spaces
            // because the direction is transformed linearly (not renormalised).
            Matrix4x4 w2l = t.worldToLocalMatrix;
            Vector3 origin = w2l.MultiplyPoint3x4(worldRay.origin);
            Vector3 dir    = w2l.MultiplyVector(worldRay.direction);

            Vector3[] v   = data.vertices;
            int[]     tri = data.triangles;
            float best = float.MaxValue;

            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                if (RayTriangle(origin, dir, v[tri[i]], v[tri[i + 1]], v[tri[i + 2]], out float d) && d < best)
                {
                    if (IsPointCut != null && IsPointCut(t.localToWorldMatrix.MultiplyPoint3x4(origin + dir * d))) continue;
                    best = d;
                    bestTri = i;
                }
            }

            if (best == float.MaxValue) return false;
            distance = best;

            // The triangle's face direction, in world space, turned to face the ray (the triangle
            // test is double sided, so the winding may point either way).
            Vector3 n = Vector3.Cross(v[tri[bestTri + 1]] - v[tri[bestTri]], v[tri[bestTri + 2]] - v[tri[bestTri]]);
            n = t.localToWorldMatrix.inverse.transpose.MultiplyVector(n).normalized;
            if (n.sqrMagnitude > 0.5f) worldNormal = Vector3.Dot(n, worldRay.direction) > 0f ? -n : n;
            return true;
        }

        /// <summary>Moller-Trumbore ray/triangle intersection (double sided).</summary>
        private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a;
            Vector3 e2 = c - a;
            Vector3 p  = Vector3.Cross(d, e2);
            float det  = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f) return false;

            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;

            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        /// <summary>Centre of the whole model's world bounds (highlight boxes excluded).</summary>
        public static bool TryGetBoundsCenter(Transform root, out Vector3 center)
        {
            center = Vector3.zero;
            if (root == null) return false;

            bool any = false;
            Bounds total = default;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.gameObject.name == HighlightName) continue;
                if (!any) { total = r.bounds; any = true; }
                else total.Encapsulate(r.bounds);
            }

            if (!any) return false;
            center = total.center;
            return true;
        }

        /// <summary>
        /// Bounds of a part and everything under it, in that part's own local space.
        ///
        /// A glTF hierarchy is mostly groups that hold no mesh themselves. Tapping the model can
        /// only ever hit a mesh, but the parts drawer lets you pick a group by name, and a group is
        /// still worth boxing - around the meshes it contains.
        /// </summary>
        public static bool TryGetLocalBounds(Transform part, out Bounds bounds)
        {
            bounds = default;
            if (part == null) return false;

            bool any = false;
            Matrix4x4 w2l = part.worldToLocalMatrix;

            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                if (r.gameObject.name == HighlightName) continue;

                Bounds local;
                if (r is SkinnedMeshRenderer smr) local = smr.localBounds;
                else
                {
                    Mesh mesh = GetMesh(r);
                    if (mesh == null) continue;
                    local = mesh.bounds;
                }

                // Mesh bounds are axis-aligned in the renderer's space, so a rotated child needs
                // all eight corners carried over, not just the centre and extents.
                Matrix4x4 toPart = w2l * r.transform.localToWorldMatrix;
                Vector3 c = local.center, e = local.extents;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);

                    Vector3 p = toPart.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }

            return any;
        }

        // ── Part identification ────────────────────────────────────────────────

        /// <summary>
        /// Stable identifier for a part: the child-index path from the model root, e.g. "0/3/1".
        /// Every device builds the same hierarchy from the same GLB, so the path means the same
        /// part everywhere.
        /// </summary>
        public static string GetPartPath(Transform root, Transform part)
        {
            if (root == null || part == null) return "";

            var indices = new List<int>();
            Transform cur = part;
            while (cur != null && cur != root)
            {
                indices.Add(cur.GetSiblingIndex());
                cur = cur.parent;
            }
            if (cur != root) return ""; // not under this root

            indices.Reverse();
            return string.Join("/", indices);
        }

        public static Transform FindPart(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;

            Transform cur = root;
            foreach (string token in path.Split('/'))
            {
                if (!int.TryParse(token, out int idx) || idx < 0 || idx >= cur.childCount) return null;
                cur = cur.GetChild(idx);
            }
            return cur;
        }
    }
}
