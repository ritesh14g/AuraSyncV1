using System.Collections.Generic;
using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// Flattens a loaded model's hierarchy into an ordered list of pickable parts, for the Model
    /// Parts drawer.
    ///
    /// Only nodes that contain something visible are listed: a glTF file carries plenty of empty
    /// transforms, and a list of those is no use to anyone. Each entry keeps the same child-index
    /// path the raycaster and the selector use, so a part picked from the list and the same part
    /// tapped in the 3D view are one and the same selection, on every device.
    ///
    /// Very large models are cut off at <see cref="MaxEntries"/>. A drawer with thousands of rows
    /// would stutter on a phone and nobody could find anything in it anyway; the drawer's filter is
    /// the way into a big model.
    /// </summary>
    public static class ModelPartCatalog
    {
        /// <summary>Hard cap on listed parts. Anything past this is counted, not built.</summary>
        public const int MaxEntries = 400;

        private const int MaxNameLength = 40;

        public struct Entry
        {
            /// <summary>Child-index path from the model root, e.g. "0/3/1".</summary>
            public string path;
            /// <summary>Name shown in the drawer.</summary>
            public string name;
            /// <summary>Nesting level below the root, for indentation.</summary>
            public int depth;
            /// <summary>True when this node holds a mesh itself, false when it only groups others.</summary>
            public bool hasMesh;
        }

        /// <summary>
        /// Walks the model in hierarchy order.
        /// </summary>
        /// <param name="root">The loaded model's root (SpaceInteractionController.ModelRoot).</param>
        /// <param name="skipped">How many parts were left out because of the cap.</param>
        public static List<Entry> Build(Transform root, out int skipped)
        {
            var list = new List<Entry>();
            skipped = 0;
            if (root == null) return list;

            for (int i = 0; i < root.childCount; i++)
                Walk(root.GetChild(i), i.ToString(), 0, list, ref skipped);

            return list;
        }

        private static void Walk(Transform t, string path, int depth, List<Entry> list, ref int skipped)
        {
            if (t == null) return;
            if (t.name == ModelRaycaster.HighlightName) return; // our own selection box
            if (!ContainsVisible(t)) return;                    // empty transform, nothing to pick

            if (list.Count >= MaxEntries)
            {
                skipped++;
            }
            else
            {
                list.Add(new Entry
                {
                    path    = path,
                    name    = DisplayName(t),
                    depth   = depth,
                    hasMesh = HasOwnMesh(t)
                });
            }

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), path + "/" + i, depth + 1, list, ref skipped);
        }

        /// <summary>True if this node or anything under it draws something.</summary>
        private static bool ContainsVisible(Transform t)
        {
            // GetComponentInChildren stops at the first hit, so this does not allocate an array
            // per node the way GetComponentsInChildren would.
            Renderer r = t.GetComponentInChildren<Renderer>(false);
            return r != null;
        }

        private static bool HasOwnMesh(Transform t)
        {
            if (t.GetComponent<SkinnedMeshRenderer>() != null) return true;

            MeshFilter mf = t.GetComponent<MeshFilter>();
            return mf != null && mf.sharedMesh != null;
        }

        private static string DisplayName(Transform t)
        {
            string n = t.name;
            if (string.IsNullOrWhiteSpace(n)) return "(unnamed)";

            // Plain ASCII only: TMP's default font has no glyphs for much else.
            return n.Length <= MaxNameLength ? n : n.Substring(0, MaxNameLength - 3) + "...";
        }
    }
}
