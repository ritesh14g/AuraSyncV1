using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.Analytics
{
    /// <summary>
    /// Recolours the model by attention (V3 Phase 10): red = most, through yellow and green, to
    /// blue = least; grey = never touched.
    ///
    /// The colour goes on through a MaterialPropertyBlock per renderer, overriding the base colour
    /// (and turning the texture, metal and occlusion maps off so the colour reads clearly). The
    /// materials themselves are never swapped, so the section cut (which swaps materials) keeps
    /// working underneath, and Done gives every part its look back by clearing the block.
    ///
    /// A part's heat is its own score plus its groups' (a pick in the parts list of a whole group
    /// warms every mesh in it). The scale is relative: the hottest part is always red.
    /// </summary>
    public static class HeatmapView
    {
        public static bool Showing { get; private set; }

        private static readonly List<Renderer> _painted = new List<Renderer>();
        private static MaterialPropertyBlock _block;

        public static readonly Color Never = new Color(0.55f, 0.55f, 0.58f);

        /// <summary>Blue (0) → cyan → green → yellow → red (1).</summary>
        public static Color Ramp(float t)
        {
            t = Mathf.Clamp01(t);
            Color[] stops =
            {
                new Color(0.15f, 0.35f, 1.00f), new Color(0.10f, 0.85f, 0.95f), new Color(0.20f, 0.85f, 0.30f),
                new Color(1.00f, 0.85f, 0.10f), new Color(0.95f, 0.15f, 0.10f),
            };
            float x = t * (stops.Length - 1);
            int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(x));
            return Color.Lerp(stops[i], stops[i + 1], x - i);
        }

        /// <summary>Paints the model from scores by part path (paths with no score stay grey).</summary>
        public static void Show(Dictionary<string, float> scores)
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            if (root == null) return;
            Clear();
            _block = _block ?? new MaterialPropertyBlock();

            float max = scores.Count > 0 ? Mathf.Max(0.0001f, scores.Values.Max()) : 1f;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

                float s = ScoreFor(ModelRaycaster.GetPartPath(root, r.transform), scores);
                Color c = s > 0f ? Ramp(s / max) : Never;

                _block.Clear();
                _block.SetColor("baseColorFactor", c);
                _block.SetColor("diffuseFactor", c);   // glTF specular-glossiness materials
                _block.SetColor("_Color", c);          // the Standard repair fallback
                _block.SetTexture("baseColorTexture", Texture2D.whiteTexture);
                _block.SetTexture("diffuseTexture", Texture2D.whiteTexture);
                _block.SetTexture("_MainTex", Texture2D.whiteTexture);
                _block.SetTexture("metallicRoughnessTexture", Texture2D.whiteTexture);
                _block.SetTexture("occlusionTexture", Texture2D.whiteTexture);
                _block.SetFloat("metallicFactor", 0f);
                _block.SetFloat("roughnessFactor", 0.75f);
                r.SetPropertyBlock(_block);
                _painted.Add(r);
            }
            Showing = true;
        }

        /// <summary>A mesh's heat: its own score and every group above it.</summary>
        public static float ScoreFor(string path, Dictionary<string, float> scores)
        {
            float s = 0f;
            string p = path;
            while (!string.IsNullOrEmpty(p))
            {
                if (scores.TryGetValue(p, out float v)) s += v;
                int cut = p.LastIndexOf('/');
                p = cut > 0 ? p.Substring(0, cut) : "";
            }
            return s;
        }

        public static void Clear()
        {
            foreach (Renderer r in _painted) if (r != null) r.SetPropertyBlock(null);
            _painted.Clear();
            Showing = false;
        }

        /// <summary>
        /// The parts to rank: the knowledge file's labelled parts when the model has them, else every
        /// mesh part. Each gets the score of everything at or under its path.
        /// </summary>
        public static List<KeyValuePair<string, float>> Rank(Dictionary<string, float> scores)
        {
            var list = new List<KeyValuePair<string, float>>();
            PartKnowledge pk = PartKnowledge.Instance;
            if (pk != null && pk.HasParts)
            {
                foreach (PartKnowledgeEntry e in pk.AllParts)
                    if (!string.IsNullOrEmpty(e.path))
                        list.Add(new KeyValuePair<string, float>(e.path, Under(e.path, scores)));
            }
            else
            {
                Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
                foreach (ModelPartCatalog.Entry e in ModelPartCatalog.Build(root, out _))
                    if (e.hasMesh) list.Add(new KeyValuePair<string, float>(e.path, Under(e.path, scores)));
            }
            return list.OrderByDescending(kv => kv.Value).ToList();
        }

        private static float Under(string path, Dictionary<string, float> scores)
        {
            float s = 0f;
            foreach (var kv in scores)
                if (kv.Key == path || kv.Key.StartsWith(path + "/")) s += kv.Value;
            return s;
        }

        public static string Label(string path)
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Transform t = root != null ? ModelRaycaster.FindPart(root, path) : null;
            return PartNames.Display(path, t != null ? t.name : path);
        }
    }
}
