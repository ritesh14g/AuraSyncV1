using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using AuraSync.AI;
using AuraSync.Core;
using AuraSync.Networking;
using AuraSync.Space;

namespace AuraSync.Challenge
{
    public enum Difficulty { Easy, Medium, Hard }

    /// <summary>
    /// The reassembly challenge (Phase 12, single device only): the model's parts are scattered
    /// around it, and the student drags each one back to where it belongs.
    ///
    /// * **Easy:** only the main parts come loose, snapping is generous, and a faint ghost of the
    ///   assembled model shows where everything goes.
    /// * **Medium:** every part (bar tiny ones) comes loose, normal snapping, ghost shown.
    /// * **Hard:** the real assembly order must be followed (a part fitted too early bounces back),
    ///   and there is no ghost. Needs the part knowledge file's assembly order.
    ///
    /// Parts move through PartPoseComposer's Challenge layer, so ending the game (finished or quit)
    /// puts every part back exactly. Dragging a loose part claims the gesture from the viewer
    /// (SpaceInteractionController.GestureClaimer), so it moves the part instead of turning the
    /// model; dragging anywhere else still turns it, and pinching still zooms.
    /// </summary>
    public class ReassemblyGame : MonoBehaviour
    {
        public static ReassemblyGame Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance._active;

        private const int EasyPieces = 6;
        private const float MinPieceFraction = 0.04f;   // parts smaller than this (of the model) stay put
        private const float FollowSpeed = 14f;
        private const float ShrinkWhilePlaying = 0.65f; // zoom out so the scattered ring fits on screen

        public class Piece
        {
            public string path;
            public Transform t;
            public string label;
            public int order;                 // 1 = fitted first; 0 = not in the assembly order
            public Vector3 scatter;           // offset (parent space) where it waits to be picked up
            public Vector3 offset;            // offset now
            public Vector3 goal;              // offset it is heading to
            public bool placed;
            public GameObject ghost;
            public float pulseUntil;
        }

        public class Result
        {
            public Difficulty difficulty;
            public int pieces, moves, hints, mistakes, score, stars, best;
            public float seconds, par;
            public bool newBest;
        }

        public event Action OnStateChanged;
        public event Action<Result> OnFinished;

        public Difficulty Mode { get; private set; }
        public int Total => _pieces.Count;
        public int Placed => _pieces.Count(p => p.placed);
        public float Elapsed { get; private set; }
        public int Moves { get; private set; }
        public int Hints { get; private set; }
        public int Mistakes { get; private set; }

        /// <summary>A short line for the game bar ("Fitted: Fan rotor"); empty when nothing to say.</summary>
        public string Message { get; private set; } = "";
        public float MessageTime { get; private set; }

        private bool _active;
        private readonly List<Piece> _pieces = new List<Piece>();
        private Transform _root;
        private float _modelSizeLocal;     // in the model root's own units
        private Vector3 _savedRootScale;
        private Material _ghostMaterial;
        private Material _hintMaterial;

        private Piece _drag;
        private Plane _dragPlane;
        private Vector3 _dragLast;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            SpaceInteractionController.GestureClaimer = ClaimGesture;
        }

        void OnDestroy()
        {
            if (_active) EndGame(completed: false);
            if (SpaceInteractionController.GestureClaimer == (Func<Vector2, bool>)ClaimGesture)
                SpaceInteractionController.GestureClaimer = null;
            if (Instance == this) Instance = null;
        }

        // ── Starting ───────────────────────────────────────────────────────────

        /// <summary>False with a reason when a game cannot start right now.</summary>
        public bool CanStart(Difficulty d, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) { why = "Wait for the model to load."; return false; }
            if (PhotonNetwork.InRoom) { why = "The challenge is for one device. Leave the room to play."; return false; }
            if (d == Difficulty.Hard && !HasAssemblyOrder()) { why = "Hard needs the model's assembly order, and this model has none."; return false; }
            if (CollectCandidates().Count < 2) { why = "This model has too few separate parts to take apart."; return false; }
            return true;
        }

        public static bool HasAssemblyOrder()
        {
            PartKnowledge pk = PartKnowledge.Instance;
            return pk != null && pk.HasParts && pk.Current.assemblyOrder != null && pk.Current.assemblyOrder.Count >= 2;
        }

        public void Begin(Difficulty d)
        {
            if (_active || !CanStart(d, out _)) return;
            _root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Camera cam = Camera.main;
            if (_root == null || cam == null) return;

            // The model as authored: whole, assembled, nothing selected.
            ModelIsolator.Instance?.ApplyQuietly(""); // a local reset, not a question for the room
            if (ModelExploder.Instance != null && ModelExploder.Instance.Amount > 0f) ModelExploder.Instance.SetAmount(0f, broadcast: false);
            ModelPartSelector.Instance?.ClearSelection();

            Mode = d;
            List<Piece> candidates = CollectCandidates();
            if (d == Difficulty.Easy)
                candidates = candidates.OrderByDescending(p => Volume(p.t)).Take(EasyPieces).ToList();

            ApplyOrder(candidates);
            _pieces.Clear();
            _pieces.AddRange(candidates);

            _savedRootScale = _root.localScale;
            _root.localScale = _savedRootScale * ShrinkWhilePlaying;

            Scatter(cam);
            if (d != Difficulty.Hard) foreach (Piece p in _pieces) p.ghost = MakeGhost(p, GhostMaterial());

            Elapsed = 0f;
            Moves = Hints = Mistakes = 0;
            _active = true;
            Say(d == Difficulty.Hard
                ? "Fit the parts in the right order. Start with the part that goes in first."
                : "Drag each part onto its faint outline.");
            OnStateChanged?.Invoke();
        }

        /// <summary>Every part that can come loose: labelled parts if the model has them, otherwise every mesh.</summary>
        private List<Piece> CollectCandidates()
        {
            var list = new List<Piece>();
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            if (root == null) return list;

            float modelSize = MaxExtent(root);
            PartKnowledge pk = PartKnowledge.Instance;
            IEnumerable<string> paths = pk != null && pk.HasParts
                ? pk.AllParts.Select(e => e.path)
                : ModelPartCatalog.Build(root, out _).Where(e => e.hasMesh).Select(e => e.path);

            foreach (string path in paths)
            {
                Transform t = ModelRaycaster.FindPart(root, path);
                if (t == null || t.GetComponent<MeshFilter>() == null || t.GetComponent<Renderer>() == null) continue;
                // A tiny part (a bracket, a bolt) is too small to grab on a phone: it stays in place.
                if (modelSize > 0f && MaxExtent(t) < modelSize * MinPieceFraction) continue;
                list.Add(new Piece { path = path, t = t, label = PartNames.Display(path, t.name) });
            }
            return list;
        }

        private void ApplyOrder(List<Piece> pieces)
        {
            PartKnowledge pk = PartKnowledge.Instance;
            List<string> order = pk != null && pk.Current != null ? pk.Current.assemblyOrder : null;
            if (order == null) return;

            int n = 0;
            foreach (string path in order)
            {
                Piece p = pieces.FirstOrDefault(x => x.path == path);
                if (p != null) p.order = ++n;
            }
            // Loose parts the order does not mention go last, in any order.
            foreach (Piece p in pieces.Where(x => x.order == 0)) p.order = ++n;
        }

        /// <summary>
        /// Lays the loose parts out on a ring around the model, in the plane facing the camera, so
        /// every one is visible and none hides behind the ghost.
        /// </summary>
        private void Scatter(Camera cam)
        {
            Bounds model = LocalBounds(_root, _root);
            _modelSizeLocal = Mathf.Max(model.size.x, Mathf.Max(model.size.y, model.size.z));

            Vector3 right = _root.InverseTransformDirection(cam.transform.right).normalized;
            Vector3 up = _root.InverseTransformDirection(cam.transform.up).normalized;
            float radius = _modelSizeLocal * 0.95f;

            // Shuffle so the ring order is not the model's own order.
            var rng = new System.Random();
            List<Piece> shuffled = _pieces.OrderBy(_ => rng.Next()).ToList();

            for (int i = 0; i < shuffled.Count; i++)
            {
                Piece p = shuffled[i];
                float angle = Mathf.PI * 2f * i / shuffled.Count + 0.4f;
                Vector3 target = model.center + right * Mathf.Cos(angle) * radius * 0.75f + up * Mathf.Sin(angle) * radius;
                Vector3 restCentre = LocalBounds(p.t, _root).center;
                Vector3 offsetRoot = target - restCentre;
                p.scatter = p.t.parent.InverseTransformVector(_root.TransformVector(offsetRoot));
                p.goal = p.scatter;
                p.offset = Vector3.zero; // slides out from where it sits
            }
        }

        // ── Playing ────────────────────────────────────────────────────────────

        void Update()
        {
            if (!_active) return;

            if (PhotonNetwork.InRoom)
            {
                Say("You joined a room, so the challenge stopped.");
                EndGame(completed: false);
                return;
            }

            Elapsed += Time.unscaledDeltaTime;
            if (_drag != null) UpdateDrag();

            float k = 1f - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime);
            foreach (Piece p in _pieces)
            {
                if (p.t == null) continue;
                if (p != _drag) p.offset = Vector3.Lerp(p.offset, p.goal, k);

                if (p.placed && (p.offset - p.goal).sqrMagnitude < 1e-10f)
                    PartPoseComposer.Clear(p.t, PoseLayer.Challenge); // exactly home
                else
                    PartPoseComposer.SetOffset(p.t, PoseLayer.Challenge, p.offset);

                // A hinted part gently pulses so the student can find it.
                float pulse = p.pulseUntil > Time.unscaledTime ? 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
                if (pulse != 1f) PartPoseComposer.SetScale(p.t, PoseLayer.Challenge, pulse);
                else if (PartPoseComposer.HasLayer(p.t, PoseLayer.Challenge)) PartPoseComposer.SetScale(p.t, PoseLayer.Challenge, 1f);
            }
        }

        /// <summary>
        /// Called by the viewer when a gesture starts. Returns true (and starts a drag) when it
        /// starts on a loose part, so the viewer leaves the model alone for this gesture.
        /// </summary>
        private bool ClaimGesture(Vector2 screenPos)
        {
            if (!_active || _root == null) return false;
            bool primary = InputCompat.TouchCount == 1 || (InputCompat.TouchCount == 0 && InputCompat.GetMouseButtonDown(0));
            if (!primary) return false;

            Camera cam = Camera.main;
            if (cam == null) return false;
            if (!ModelRaycaster.Raycast(cam.ScreenPointToRay(screenPos), _root, out Renderer hit, out Vector3 point)) return false;

            Piece piece = _pieces.FirstOrDefault(p => !p.placed && p.t != null && hit.transform.IsChildOf(p.t));
            if (piece == null) return false;

            _drag = piece;
            _dragPlane = new Plane(-cam.transform.forward, point);
            _dragLast = point;
            return true;
        }

        private void UpdateDrag()
        {
            bool down = InputCompat.TouchCount > 0 || InputCompat.GetMouseButton(0);
            if (!down)
            {
                Drop(_drag);
                _drag = null;
                return;
            }
            if (!InputCompat.TryGetPointer(out Vector2 pos)) return; // two fingers: pinch, keep the part where it is

            Camera cam = Camera.main;
            Ray ray = cam.ScreenPointToRay(pos);
            if (!_dragPlane.Raycast(ray, out float d)) return;

            Vector3 world = ray.GetPoint(d);
            Vector3 deltaParent = _drag.t.parent.InverseTransformVector(world - _dragLast);
            _dragLast = world;
            _drag.offset += deltaParent;
            _drag.goal = _drag.offset;
        }

        private void Drop(Piece p)
        {
            if (p == null || p.t == null) return;
            Moves++;

            // How far its centre is from where it belongs, in the model's own units.
            Vector3 nowCentre = _root.InverseTransformPoint(WorldCentre(p.t));
            Vector3 offsetRoot = _root.InverseTransformVector(p.t.parent.TransformVector(p.offset));
            Vector3 homeCentre = nowCentre - offsetRoot;
            float distance = Vector3.Distance(nowCentre, homeCentre);
            float snap = _modelSizeLocal * (Mode == Difficulty.Easy ? 0.14f : 0.08f);

            if (distance > snap)
            {
                OnStateChanged?.Invoke(); // stays where it was dropped
                return;
            }

            if (Mode == Difficulty.Hard)
            {
                Piece next = NextInOrder();
                if (next != null && next != p)
                {
                    Mistakes++;
                    p.goal = p.scatter;
                    Say($"Not yet: something else goes in before the {p.label.ToLowerInvariant()}.");
                    OnStateChanged?.Invoke();
                    return;
                }
            }

            p.goal = Vector3.zero;
            p.placed = true;
            p.pulseUntil = 0f;
            if (p.ghost != null) Destroy(p.ghost);
            Say($"Fitted: {p.label} ({Placed} of {Total})");
            OnStateChanged?.Invoke();

            if (Placed == Total) Finish();
        }

        private Piece NextInOrder() => _pieces.Where(p => !p.placed).OrderBy(p => p.order).FirstOrDefault();

        public void Hint()
        {
            if (!_active) return;
            Piece p = Mode == Difficulty.Hard || HasAssemblyOrder()
                ? NextInOrder()
                : _pieces.Where(x => !x.placed).OrderByDescending(x => Volume(x.t)).FirstOrDefault();
            if (p == null) return;

            Hints++;
            p.pulseUntil = Time.unscaledTime + 3f;
            if (p.ghost == null) p.ghost = MakeGhost(p, HintMaterial());
            else p.ghost.GetComponent<Renderer>().sharedMaterial = HintMaterial();
            Say($"Hint: the {p.label.ToLowerInvariant()} (pulsing) goes on the yellow outline.");
            StartCoroutine(UnHint(p));
            OnStateChanged?.Invoke();
        }

        private System.Collections.IEnumerator UnHint(Piece p)
        {
            yield return new WaitForSecondsRealtime(3f);
            if (p.ghost == null || p.placed) yield break;
            if (Mode == Difficulty.Hard) { Destroy(p.ghost); p.ghost = null; }
            else p.ghost.GetComponent<Renderer>().sharedMaterial = GhostMaterial();
        }

        public void Quit()
        {
            if (!_active) return;
            ChallengeLog.Write(Mode, Total, Placed, Elapsed, Moves, Hints, Mistakes, 0, 0, completed: false);
            EndGame(completed: false);
        }

        // ── Ending ─────────────────────────────────────────────────────────────

        private void Finish()
        {
            float par = Total * (Mode == Difficulty.Easy ? 8f : 10f);
            int score = Mathf.Max(0,
                1000 + Total * 50
                - Mathf.RoundToInt(Mathf.Max(0f, Elapsed - par) * 3f)
                - Hints * 120
                - Mistakes * 60
                - Mathf.Max(0, Moves - Total * 2) * 10);
            int stars = Hints == 0 && Mistakes == 0 && Elapsed <= par ? 3
                      : Hints <= 1 && Mistakes <= 2 && Elapsed <= par * 2f ? 2
                      : 1;

            string key = BestKey(Mode);
            int best = PlayerPrefs.GetInt(key, 0);
            bool newBest = score > best;
            if (newBest) { PlayerPrefs.SetInt(key, score); PlayerPrefs.Save(); best = score; }

            var result = new Result
            {
                difficulty = Mode, pieces = Total, moves = Moves, hints = Hints, mistakes = Mistakes,
                score = score, stars = stars, best = best, newBest = newBest, seconds = Elapsed, par = par,
            };
            ChallengeLog.Write(Mode, Total, Placed, Elapsed, Moves, Hints, Mistakes, score, stars, completed: true);
            EndGame(completed: true);
            OnFinished?.Invoke(result);
        }

        private void EndGame(bool completed)
        {
            _active = false;
            _drag = null;
            PartPoseComposer.ClearLayer(PoseLayer.Challenge); // every part exactly home
            foreach (Piece p in _pieces) if (p.ghost != null) Destroy(p.ghost);
            _pieces.Clear();
            if (_root != null) _root.localScale = _savedRootScale;
            if (!completed && string.IsNullOrEmpty(Message)) Say("");
            OnStateChanged?.Invoke();
        }

        public static int BestScore(Difficulty d) => PlayerPrefs.GetInt(BestKey(d), 0);

        private static string BestKey(Difficulty d)
        {
            string model = SpaceSceneManager.CurrentModelKey ?? "";
            return $"AuraSync.Challenge.Best.{model.GetHashCode():X8}.{d}";
        }

        private void Say(string text)
        {
            Message = text;
            MessageTime = Time.unscaledTime;
        }

        // ── Ghosts ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A see-through copy of the part where it belongs. Named like the selection boxes, so the
        /// raycaster, the parts list and isolation all ignore it.
        /// </summary>
        private GameObject MakeGhost(Piece p, Material material)
        {
            MeshFilter mf = p.t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;

            PartPoseComposer.GetRestPose(p.t, out Vector3 pos, out Quaternion rot, out Vector3 scale);
            var go = new GameObject(ModelRaycaster.HighlightName);
            go.transform.SetParent(p.t.parent, false); // added last: no other part's path changes
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        private Material GhostMaterial() => _ghostMaterial != null ? _ghostMaterial
            : (_ghostMaterial = PointerVisuals.Create(new Color(0.55f, 0.75f, 1f), 0.16f));

        private Material HintMaterial() => _hintMaterial != null ? _hintMaterial
            : (_hintMaterial = PointerVisuals.Create(new Color(1f, 0.85f, 0.2f), 0.5f));

        // ── Geometry helpers ───────────────────────────────────────────────────

        private static Vector3 WorldCentre(Transform t)
        {
            Renderer r = t.GetComponent<Renderer>();
            return r != null ? r.bounds.center : t.position;
        }

        private static float Volume(Transform t)
        {
            Renderer r = t != null ? t.GetComponent<Renderer>() : null;
            if (r == null) return 0f;
            Vector3 s = r.bounds.size;
            return s.x * s.y * s.z;
        }

        private static float MaxExtent(Transform t)
        {
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 0f;
        }

        /// <summary>Bounds of everything drawn under <paramref name="t"/>, in <paramref name="space"/>'s local coordinates.</summary>
        private static Bounds LocalBounds(Transform t, Transform space)
        {
            bool any = false;
            Bounds b = default;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 l = space.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
                }
            }
            return b;
        }
    }

    /// <summary>
    /// The paper's challenge numbers, on the device only: completion time, moves, hints and mistakes
    /// per game. <c>&lt;persistentDataPath&gt;/ChallengeLog.jsonl</c>, one JSON object per line.
    /// </summary>
    public static class ChallengeLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "ChallengeLog.jsonl");

        [Serializable]
        private class Line
        {
            public string time, model, difficulty;
            public bool completed;
            public int pieces, placed, moves, hints, mistakes, score, stars;
            public float seconds;
        }

        public static void Write(Difficulty d, int pieces, int placed, float seconds, int moves, int hints, int mistakes, int score, int stars, bool completed)
        {
            var line = new Line
            {
                time = DateTime.UtcNow.ToString("o"), model = SpaceSceneManager.CurrentModelKey, difficulty = d.ToString(),
                completed = completed, pieces = pieces, placed = placed, seconds = seconds,
                moves = moves, hints = hints, mistakes = mistakes, score = score, stars = stars,
            };
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(line) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[Challenge] Could not write the log: {ex.Message}"); }
        }

        public static string Summary()
        {
            if (!File.Exists(FilePath)) return $"No challenges played yet ({FilePath}).";
            var lines = new List<Line>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                try { var l = JsonUtility.FromJson<Line>(raw); if (l != null) lines.Add(l); } catch { /* skip */ }
            }
            if (lines.Count == 0) return $"No challenges played yet ({FilePath}).";

            var done = lines.Where(l => l.completed).ToList();
            string perLevel = string.Join("; ", done.GroupBy(l => l.difficulty).Select(g =>
            {
                var secs = g.Select(l => l.seconds).OrderBy(x => x).ToList();
                return $"{g.Key}: {g.Count()} done, median {secs[secs.Count / 2]:0} s, {g.Average(l => l.hints):0.0} hints";
            }));
            return $"{lines.Count} challenge(s), {done.Count} completed. {perLevel}. File: {FilePath}";
        }
    }
}
