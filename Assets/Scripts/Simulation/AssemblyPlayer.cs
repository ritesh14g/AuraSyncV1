using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.Simulation
{
    /// <summary>
    /// The assembly animation (Phase 7, Level A): parts come off one at a time, or go back on.
    ///
    /// **Order.** A model with a reviewed assembly order (the jet engine's knowledge file) uses it:
    /// taking apart removes the last-fitted part first, each sliding out along its reviewed
    /// <c>removeDirection</c> with a caption naming it. A model without one (the House, a scan cut
    /// into chunks) gets an order made from its shape, as you chose on 2026-10-13: lowest chunk
    /// first, each dropping in from above, so assembling reads like the building going up.
    ///
    /// The position is a number of steps done (0..steps, fractions mid-slide), worked out by
    /// SimulationController from the shared clock, so every phone is on the same step.
    /// </summary>
    public class AssemblyPlayer
    {
        private const float TravelFactor = 0.9f; // how far a removed part goes, against the model's size

        public class Step
        {
            public string    path;
            public Transform part;
            public string    label;        // "" for a generated (shape-based) order
            public Vector3   offsetOut;    // in the part's parent space, fully removed
        }

        /// <summary>The steps in assembly order (first fitted first).</summary>
        public readonly List<Step> Steps = new List<Step>();

        public int StepCount => Steps.Count;

        /// <summary>True when the order came from the model's shape, not a reviewed knowledge file.</summary>
        public bool GeneratedOrder { get; private set; }

        public float LastPosition { get; private set; }

        private bool _running;

        public void Begin()
        {
            Build();
            _running = true;
        }

        /// <summary>Takes the layer off every part: the model is exactly as it was.</summary>
        public void Stop()
        {
            foreach (Step s in Steps) if (s.part != null) PartPoseComposer.Clear(s.part, PoseLayer.Simulation);
            _running = false;
        }

        /// <summary>A new model: the old parts are gone, nothing to put back.</summary>
        public void Forget()
        {
            Steps.Clear();
            _running = false;
        }

        // ── Building the steps ─────────────────────────────────────────────────

        private void Build()
        {
            Steps.Clear();
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            if (root == null || !ModelRaycaster.TryGetLocalBounds(root, out Bounds model)) return;

            float travel = Mathf.Max(model.size.x, Mathf.Max(model.size.y, model.size.z)) * TravelFactor;
            PartKnowledge pk = PartKnowledge.Instance;

            if (pk != null && pk.HasParts && pk.Current.assemblyOrder != null && pk.Current.assemblyOrder.Count >= 2)
            {
                GeneratedOrder = false;
                foreach (string path in pk.Current.assemblyOrder)
                {
                    Transform t = ModelRaycaster.FindPart(root, path);
                    if (t == null || t.parent == null) continue;
                    pk.TryGet(path, out PartKnowledgeEntry e);
                    Vector3 dir = e != null && e.removeDirection.sqrMagnitude > 1e-6f ? e.removeDirection.normalized : Vector3.up;
                    Steps.Add(new Step
                    {
                        path = path, part = t,
                        label = e != null && !string.IsNullOrEmpty(e.label) ? PartNames.SentenceCase(e.label) : PartNames.Display(path, t.name),
                        offsetOut = ToParent(root, t, dir * travel),
                    });
                }
                return;
            }

            // No reviewed order: build it from the shape, lowest first, each dropping from above.
            GeneratedOrder = true;
            var pieces = new List<(Step step, float bottom)>();
            foreach (ModelPartCatalog.Entry entry in ModelPartCatalog.Build(root, out _))
            {
                if (!entry.hasMesh) continue;
                Transform t = ModelRaycaster.FindPart(root, entry.path);
                if (t == null || t.parent == null || t.GetComponent<Renderer>() == null) continue;
                if (!TryBoundsIn(root, t, out Bounds b)) continue;
                pieces.Add((new Step { path = entry.path, part = t, label = "", offsetOut = ToParent(root, t, Vector3.up * travel) }, b.min.y));
            }
            Steps.AddRange(pieces.OrderBy(p => p.bottom).Select(p => p.step));
        }

        private static Vector3 ToParent(Transform root, Transform part, Vector3 inRoot) =>
            part.parent.InverseTransformVector(root.TransformVector(inRoot));

        /// <summary>A part's bounds in the root's space (from its renderers' world bounds).</summary>
        private static bool TryBoundsIn(Transform root, Transform part, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                Bounds w = r.bounds;
                Vector3 c = root.InverseTransformPoint(w.center);
                Vector3 e = root.InverseTransformVector(w.extents);
                var local = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (!any) { b = local; any = true; } else b.Encapsulate(local);
            }
            return any;
        }

        // ── Playing ────────────────────────────────────────────────────────────

        /// <summary>
        /// Puts every part where it belongs at this position. Taking apart, step i removes the
        /// (i+1)-th part from the end of the order; putting together, step i fits the i-th part.
        /// </summary>
        public void Tick(bool assemble, float position)
        {
            if (!_running) return;
            LastPosition = position;
            int n = Steps.Count;

            for (int k = 0; k < n; k++)
            {
                Step s = Steps[k];
                if (s.part == null) continue;

                // Which step moves this part, in this direction.
                int stepIndex = assemble ? k : n - 1 - k;
                float p = Mathf.Clamp01(position - stepIndex);
                float out01 = assemble ? 1f - p : p;
                out01 = out01 * out01 * (3f - 2f * out01); // ease in and out

                if (out01 <= 0f) PartPoseComposer.Clear(s.part, PoseLayer.Simulation);
                else PartPoseComposer.SetOffset(s.part, PoseLayer.Simulation, s.offsetOut * out01);
            }
        }

        /// <summary>The part the current step is about (for the caption and AI assist), or null when done.</summary>
        public Step CurrentStep(bool assemble, float position, out int number)
        {
            int n = Steps.Count;
            number = Mathf.Min(Mathf.FloorToInt(position), n - 1) + 1;
            if (n == 0 || position >= n) return null;
            int i = number - 1;
            return assemble ? Steps[i] : Steps[n - 1 - i];
        }

        public string Caption(bool assemble, float position)
        {
            int n = Steps.Count;
            if (n == 0) return "This model has no parts to take apart.";
            if (position >= n) return assemble ? "Done: the model is assembled." : "Done: every part is off.";

            Step s = CurrentStep(assemble, position, out int number);
            if (GeneratedOrder)
                return assemble ? $"Step {number} of {n}: section {number} goes on" : $"Step {number} of {n}: section {n - number + 1} comes off";
            return assemble ? $"Step {number} of {n}: fit the {s.label.ToLowerInvariant()}"
                            : $"Step {number} of {n}: remove the {s.label.ToLowerInvariant()}";
        }
    }
}
