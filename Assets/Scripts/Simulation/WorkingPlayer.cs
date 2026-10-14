using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.Simulation
{
    /// <summary>
    /// The working animation (Phase 7, Level B).
    ///
    /// **Machines** (a knowledge file with motion entries, like the jet engine): every part with a
    /// motion moves - <c>rotate</c> spins about its axis through its pivot, <c>slide</c> moves back
    /// and forth along its axis, <c>hinge</c> swings open and shut - all from one shared phase, so
    /// parts in the same group (the fan and turbine on the low spool) stay locked together. The
    /// throttle sets the rate. A machine with a rotating group also gets <see cref="AirflowEffect"/>.
    ///
    /// **Everything else** (the House) has nothing that moves, so the working animation is the time
    /// of day: the scene's sun turns through the sky, warm at dawn and dusk, dim at night, and the
    /// procedural sky follows it.
    ///
    /// Axis and pivot are in each part's own space (as the Part Labeler measured them). The spin is
    /// applied on the Simulation layer of <see cref="PartPoseComposer"/> as a rotation plus the
    /// offset that keeps the pivot still, so an isolated or exploded rotor keeps spinning in place.
    /// </summary>
    public class WorkingPlayer
    {
        /// <summary>Hours of daylight cycle per second at speed 1 (a full day in a minute).</summary>
        public const float HoursPerSecond = 0.4f;

        /// <summary>Spin rate of a speed-1 part: idle to full throttle.</summary>
        public static float RevolutionsPerSecond(float throttle) => Mathf.Lerp(0.15f, 2.4f, Mathf.Clamp01(throttle));

        private class Mover
        {
            public Transform part;
            public string    type;
            public Vector3   axis, pivot; // part space
            public float     speed, range;
            public string    label;
        }

        private readonly List<Mover> _movers = new List<Mover>();
        private bool _built, _running;

        /// <summary>True when the model has nothing that moves: the slider is the time of day.</summary>
        public bool IsDayCycle { get; private set; }

        public float LastPhase { get; private set; }

        /// <summary>"Fan rotor, turbine rotor" - what moves, for the panel.</summary>
        public string MovingNames => string.Join(", ", _movers.Select(m => m.label));

        public bool HasAirflow => !IsDayCycle && _movers.Any(m => m.type == "rotate");

        private AirflowEffect _airflow;
        private bool _airflowTried; // built (or found impossible) once per run, not every frame
        public bool AirflowOn { get; set; } = true; // this phone's preference

        // Sun: the scene's directional light, put back exactly on Stop.
        private Light _sun;
        private Quaternion _sunRot;
        private float _sunIntensity;
        private Color _sunColor;
        private float _ambient;

        public void Begin()
        {
            Prepare();
            _running = true;
            if (IsDayCycle) CaptureSun();
        }

        public void Stop()
        {
            foreach (Mover m in _movers) if (m.part != null) PartPoseComposer.Clear(m.part, PoseLayer.Simulation);
            if (_airflow != null) { _airflow.Destroy(); _airflow = null; }
            _airflowTried = false;
            RestoreSun();
            _running = false;
        }

        public void Forget()
        {
            if (_airflow != null) { _airflow.Destroy(); _airflow = null; }
            RestoreSun();
            _movers.Clear();
            _built = false;
            _running = false;
        }

        /// <summary>Finds what moves (once per model). Also tells the panel whether this is a day cycle.</summary>
        public void Prepare()
        {
            if (_built) return;
            _movers.Clear();
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            PartKnowledge pk = PartKnowledge.Instance;

            if (root != null && pk != null && pk.HasParts)
            {
                foreach (PartKnowledgeEntry e in pk.AllParts)
                {
                    string type = e.motion != null ? e.motion.type : "none";
                    if (type != "rotate" && type != "slide" && type != "hinge") continue;
                    if (e.motion.axis.sqrMagnitude < 1e-6f) continue;
                    Transform t = ModelRaycaster.FindPart(root, e.path);
                    if (t == null) continue;
                    _movers.Add(new Mover
                    {
                        part = t, type = type, axis = e.motion.axis.normalized, pivot = e.motion.pivot,
                        speed = e.motion.speed > 0f ? e.motion.speed : 1f, range = e.motion.range,
                        label = PartNames.SentenceCase(e.label),
                    });
                }
            }

            IsDayCycle = _movers.Count == 0;
            _built = true;
        }

        // ── Every frame ────────────────────────────────────────────────────────

        /// <param name="phase">Revolutions of a speed-1 part, or the hour of day.</param>
        /// <param name="flowRate">0 when paused; the playback speed otherwise (for the airflow).</param>
        public void Tick(float phase, float throttle, float flowRate)
        {
            if (!_running) return;
            LastPhase = phase;

            if (IsDayCycle) { UpdateSun(phase); return; }

            foreach (Mover m in _movers)
            {
                if (m.part == null) continue;
                float turns = phase * m.speed;
                Quaternion r = Quaternion.identity;
                Vector3 shift = Vector3.zero; // in part space

                switch (m.type)
                {
                    case "rotate":
                        r = Quaternion.AngleAxis(Mathf.Repeat(turns, 1f) * 360f, m.axis);
                        break;
                    case "slide":
                        shift = m.axis * (m.range > 0f ? m.range : 0.1f) * Mathf.Sin(turns * Mathf.PI * 2f);
                        break;
                    case "hinge":
                        float open = 0.5f * (1f - Mathf.Cos(turns * Mathf.PI * 2f));
                        r = Quaternion.AngleAxis(open * (m.range > 0f ? m.range : 90f), m.axis);
                        break;
                }

                // Turning about the part's own origin would swing it round; this keeps the pivot
                // still. The transform scales before it rotates, so the pivot is taken into scaled
                // space first: x' = R(Sx - SP) + SP = R(Sx) + (SP - R(SP)). The composer wants the
                // shift in parent space (rest rotation applied).
                Vector3 scale = m.part.localScale; // current: includes isolation's enlargement
                Vector3 sp = Vector3.Scale(scale, m.pivot);
                Vector3 local = (sp - r * sp) + Vector3.Scale(scale, shift);
                PartPoseComposer.GetRestPose(m.part, out _, out Quaternion restRot, out _);
                Vector3 offset = restRot * local;

                PartPoseComposer.SetRotation(m.part, PoseLayer.Simulation, r);
                PartPoseComposer.SetOffset(m.part, PoseLayer.Simulation, offset);
            }

            if (HasAirflow && AirflowOn)
            {
                if (_airflow == null && !_airflowTried)
                {
                    _airflowTried = true;
                    _airflow = AirflowEffect.Create(_movers.Where(m => m.type == "rotate").Select(m => (m.part, m.axis)).ToList());
                }
                _airflow?.Tick(throttle, flowRate);
            }
            else if (_airflow != null) { _airflow.Destroy(); _airflow = null; _airflowTried = false; }
        }

        /// <summary>Degrees the first rotating part has turned, for the paper's sync check.</summary>
        public float FirstRotorAngle(float phase)
        {
            Mover m = _movers.FirstOrDefault(x => x.type == "rotate");
            return m == null ? 0f : Mathf.Repeat(phase * m.speed, 1f) * 360f;
        }

        // ── The sun ────────────────────────────────────────────────────────────

        private void CaptureSun()
        {
            _sun = RenderSettings.sun;
            if (_sun == null)
                _sun = Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
            if (_sun == null) return;
            _sunRot = _sun.transform.rotation;
            _sunIntensity = _sun.intensity;
            _sunColor = _sun.color;
            _ambient = RenderSettings.ambientIntensity;
        }

        private void RestoreSun()
        {
            if (_sun == null) return;
            _sun.transform.rotation = _sunRot;
            _sun.intensity = _sunIntensity;
            _sun.color = _sunColor;
            RenderSettings.ambientIntensity = _ambient;
            _sun = null;
        }

        /// <summary>
        /// Sunrise at 6, noon overhead at 12 (from the south), sunset at 18. Below the horizon the
        /// light fades to a dim blue moonlight rather than going black, so the model stays visible.
        /// </summary>
        private void UpdateSun(float hour)
        {
            if (_sun == null) return;
            float elevation = Mathf.Sin((hour - 6f) / 12f * Mathf.PI);   // 1 at noon, -1 at midnight
            float pitch = (hour - 6f) / 12f * 180f;                      // across the sky, east to west
            _sun.transform.rotation = Quaternion.Euler(pitch, -30f, 0f);

            float day = Mathf.Clamp01(elevation * 3f + 0.15f);
            float golden = Mathf.Clamp01(1f - Mathf.Abs(elevation) * 4f); // near the horizon
            Color dayCol = Color.Lerp(new Color(1f, 0.96f, 0.88f), new Color(1f, 0.62f, 0.32f), golden);
            _sun.color = Color.Lerp(new Color(0.45f, 0.55f, 0.85f), dayCol, day);
            _sun.intensity = Mathf.Lerp(0.18f, _sunIntensity > 0f ? _sunIntensity : 1f, day);
            RenderSettings.ambientIntensity = Mathf.Lerp(0.35f, _ambient > 0f ? _ambient : 1f, day);
        }

        public static string HourText(float hour)
        {
            int h = Mathf.FloorToInt(Mathf.Repeat(hour, 24f));
            int min = Mathf.FloorToInt((Mathf.Repeat(hour, 24f) - h) * 60f);
            return $"{h:00}:{min:00}";
        }
    }
}
