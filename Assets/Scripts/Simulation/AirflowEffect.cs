using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.Simulation
{
    /// <summary>
    /// Air flowing through a machine with rotors (Phase 7): small glowing particles stream along the
    /// rotors' shared axis from intake to exhaust, cold blue at the front, white as the air is
    /// compressed, orange through the combustor and red out of the back. They run faster with the
    /// throttle and stop when the simulation is paused. Cut the model open (Phase 6) to watch them
    /// inside.
    ///
    /// **Path, from the model rather than a hand-made one:** the line is the rotors' axis through
    /// their centres, from one end of the model to the other. The intake is the end nearer the
    /// largest rotor (a turbofan's fan is its biggest rotor). The stream is about a third of the
    /// smallest rotor's radius thick: the core, not the bypass.
    ///
    /// **Built for phones:** about a hundred two-triangle quads turned to face the camera, eight
    /// shared colour materials, and Unity's built-in Standard material underneath (always in the
    /// build; particle shaders may be stripped). The quads are not children of the model, so part
    /// paths, the drawer and the raycaster never see them; each frame they are placed from the
    /// model root.
    /// </summary>
    public class AirflowEffect
    {
        private const int Count = 110;
        private const int Bands = 8;

        private Transform _root;
        private Vector3 _origin, _axis, _u, _v; // root space: start of the path, direction, two across
        private float _length, _radius, _size;
        private readonly List<Transform> _quads = new List<Transform>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly float[] _t = new float[Count], _angle = new float[Count], _r = new float[Count], _jitter = new float[Count];
        private readonly Material[] _bands = new Material[Bands];
        private int[] _band;
        private GameObject _holder;

        /// <summary>Builds the flow for these rotors (part and its local spin axis). Null if there is nothing to build from.</summary>
        public static AirflowEffect Create(List<(Transform part, Vector3 axis)> rotors)
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            if (root == null || rotors == null || rotors.Count == 0) return null;
            if (!ModelRaycaster.TryGetLocalBounds(root, out Bounds model)) return null;

            var fx = new AirflowEffect { _root = root };

            // The shared axis, in the root's space.
            var (first, firstAxis) = rotors[0];
            if (first == null) return null;
            Vector3 axis = root.InverseTransformDirection(first.TransformDirection(firstAxis)).normalized;

            // Rotor centres and sizes in the root's space.
            var rot = new List<(Vector3 centre, float size)>();
            foreach (var (part, _) in rotors)
                if (part != null && BoundsIn(root, part, out Bounds b)) rot.Add((b.center, b.size.magnitude));
            if (rot.Count == 0) return null;

            Vector3 line = rot.Aggregate(Vector3.zero, (s, x) => s + x.centre) / rot.Count;

            // Model's extent along the axis, measured from the line point.
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = model.center + Vector3.Scale(model.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float s = Vector3.Dot(c - line, axis);
                min = Mathf.Min(min, s); max = Mathf.Max(max, s);
            }

            // Intake: the end nearer the biggest rotor.
            var biggest = rot.OrderByDescending(x => x.size).First();
            float sBig = Vector3.Dot(biggest.centre - line, axis);
            if (Mathf.Abs(sBig - min) > Mathf.Abs(sBig - max)) { axis = -axis; float t = min; min = -max; max = -t; }

            fx._axis = axis;
            fx._origin = line + axis * min;
            fx._length = Mathf.Max(max - min, 1e-4f);
            fx._u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            fx._v = Vector3.Cross(axis, fx._u);
            fx._radius = rot.Min(x => x.size) * 0.5f * 0.35f;
            fx._size = Mathf.Max(model.size.x, Mathf.Max(model.size.y, model.size.z)) * 0.009f;
            fx.Build();
            return fx;
        }

        private void Build()
        {
            _holder = new GameObject("AuraSync_Airflow");
            for (int b = 0; b < Bands; b++) _bands[b] = PointerVisuals.CreateGlowing(BandColor((b + 0.5f) / Bands));
            _band = new int[Count];

            for (int i = 0; i < Count; i++)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Air";
                Object.Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(_holder.transform, false);
                Renderer r = q.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _quads.Add(q.transform);
                _renderers.Add(r);
                Respawn(i, Random.value);
                _band[i] = -1;
            }
        }

        private void Respawn(int i, float t)
        {
            _t[i] = t;
            _angle[i] = Random.value * Mathf.PI * 2f;
            _r[i] = Mathf.Sqrt(Random.value) * _radius;
            _jitter[i] = Random.Range(0.8f, 1.2f);
        }

        /// <summary>Cold intake, compression, combustion, hot exhaust.</summary>
        private static Color BandColor(float t)
        {
            if (t < 0.30f) return new Color(0.35f, 0.70f, 1.00f);
            if (t < 0.50f) return Color.Lerp(new Color(0.35f, 0.70f, 1.00f), new Color(0.95f, 0.95f, 1.00f), (t - 0.30f) / 0.20f);
            if (t < 0.62f) return new Color(1.00f, 0.75f, 0.25f);
            if (t < 0.80f) return new Color(1.00f, 0.38f, 0.10f);
            return new Color(0.75f, 0.25f, 0.18f);
        }

        /// <param name="flowRate">0 = paused; otherwise the playback speed.</param>
        public void Tick(float throttle, float flowRate)
        {
            if (_root == null || _holder == null) return;
            Camera cam = Camera.main;
            float dt = Time.deltaTime;
            float speed = flowRate * Mathf.Lerp(0.12f, 0.75f, Mathf.Clamp01(throttle)); // path lengths per second
            float worldScale = _root.lossyScale.x;
            bool show = !AuraSync.Challenge.ReassemblyGame.IsActive;
            if (_holder.activeSelf != show) _holder.SetActive(show);
            if (!show) return;

            for (int i = 0; i < Count; i++)
            {
                _t[i] += dt * speed * _jitter[i];
                if (_t[i] >= 1f) Respawn(i, _t[i] - 1f);

                Vector3 local = _origin + _axis * (_t[i] * _length)
                              + (_u * Mathf.Cos(_angle[i]) + _v * Mathf.Sin(_angle[i])) * _r[i];
                Transform q = _quads[i];
                q.position = _root.TransformPoint(local);
                if (cam != null) q.rotation = cam.transform.rotation;
                q.localScale = Vector3.one * (_size * worldScale);

                int band = Mathf.Clamp((int)(_t[i] * Bands), 0, Bands - 1);
                if (band != _band[i]) { _band[i] = band; _renderers[i].sharedMaterial = _bands[band]; }
            }
        }

        public void Destroy()
        {
            if (_holder != null) Object.Destroy(_holder);
            foreach (Material m in _bands) if (m != null) Object.Destroy(m);
            _holder = null;
        }

        private static bool BoundsIn(Transform root, Transform part, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                Vector3 c = root.InverseTransformPoint(r.bounds.center);
                Vector3 e = root.InverseTransformVector(r.bounds.extents);
                var local = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (!any) { b = local; any = true; } else b.Encapsulate(local);
            }
            return any;
        }
    }
}
