using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;

namespace AuraSync.Space
{
    /// <summary>
    /// Collaborative attention awareness (V3 Phase 8), the drawing half:
    ///
    ///   - **A view cone per person:** a small see-through beam in their colour (30%), fading towards
    ///     its tip, floating off the model on the side they look from and pointing at the spot at the
    ///     centre of their screen, which a faint ring marks. The beam floats farther out the more the
    ///     person has zoomed out (their camera distance travels with the view). Their name sits at
    ///     the beam's outer end. People looking at the same spot from the same angle (viewers who
    ///     follow the presenter) share one cone: "Asha + 2". A cone that matches this phone's own
    ///     view is drawn as its ring only, tagged "(same view)". A spot on the far side of its part
    ///     gets a faint tag, "behind".
    ///   - **Who sees whom** (2026-10-14): the presenter sees every viewer's cone; a viewer sees only
    ///     the presenter's, and only after they stop following. The presenter can switch cones off
    ///     for everyone, and anyone can hide them on their own phone (VIEWS).
    ///   - **"Look here" pings:** two rings pulsing out from the spot for 3 s, with "Asha: look here".
    ///     Kept on screen even when the spot is not: the ring then sits at the screen edge nearest
    ///     to it.
    ///
    /// Cones and rings are plain objects in the scene, not children of the model, so part paths,
    /// the parts drawer and the raycaster never see them. Their place is worked out every frame from
    /// the spot in its part's own space, so they follow explode, isolate and the simulation. A spot
    /// on a part hidden by isolation, or removed by the section cut, has no cone. Tags and pings are
    /// screen-space, under the HUD.
    /// </summary>
    public class AttentionVisuals : MonoBehaviour
    {
        public static AttentionVisuals Instance { get; private set; }

        private const float ConeLength   = 0.225f;  // × model radius
        private const float ConeRadius   = 0.0325f; // × model radius, at the wide end and the ring
        private const float SurfaceGap   = 0.015f;  // × model radius: ring floats just off the surface
        private const float Opacity      = 0.30f;
        // The cone's wide end stands off the spot by this share of the viewer's camera distance,
        // within these limits (× model radius). Zooming out pushes it out, zooming in pulls it in.
        private const float GapShare     = 0.22f;
        private const float MinGap       = 0.06f;
        private const float MaxGap       = 2.0f;
        private const float DefaultGap   = 0.45f;   // a view from an older build carries no distance
        private const float MergeDist    = 0.06f;  // × model radius
        private const float MergeAngle   = 8f;     // degrees
        private const float Smoothing    = 12f;    // views arrive 5 times a second; glide between them
        private const float EdgeMargin   = 70f;    // px kept clear when a ping is pinned to the edge

        private class ConeObj
        {
            public GameObject cone, ring;
            public Material   coneMat, ringMat;
            public Color      color = Color.clear;
        }

        private class Tag
        {
            public RectTransform   rt;
            public CanvasGroup     group;
            public Image           bg;
            public TextMeshProUGUI text;
        }

        private class PingObj
        {
            public RectTransform   rt;
            public CanvasGroup     group;
            public Image           ringA, ringB, dot;
            public Image           pillBg;
            public TextMeshProUGUI text;
        }

        private class Cluster
        {
            public readonly List<int> actors = new List<int>();
            public Vector3 point, dir, normal; // world
            public float distance;             // world: the viewer's camera to the spot
            public int primary;
        }

        private Canvas _canvas;
        private RectTransform _layer;
        private Mesh _coneMesh, _ringMesh;
        private static Sprite _ringSprite, _dotSprite;

        private readonly List<ConeObj> _cones = new List<ConeObj>();
        private readonly List<Tag> _tags = new List<Tag>();
        private readonly List<PingObj> _pingObjs = new List<PingObj>();
        private readonly List<Cluster> _clusters = new List<Cluster>();

        // Smoothed view per person, in the model root's space (so turning one's own model never lags).
        private readonly Dictionary<int, Vector3> _smoothPoint = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _smoothDir   = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float>   _smoothDist  = new Dictionary<int, float>();
        private Shader _beam;
        private readonly Dictionary<string, Renderer[]> _renderers = new Dictionary<string, Renderer[]>();

        /// <summary>Set by a replay (Phase 9): names from the recording, for people no longer in any room.</summary>
        public static System.Func<int, string> NameResolver;

        /// <summary>Cones drawn last frame (merged groups count once). For the tests' stats line.</summary>
        public int ConesDrawn { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // Our own see-through shader (Always Included). Without it (an old build), Standard's
            // fade mode, which a phone build may draw solid; then both sides need their own faces.
            _beam = Shader.Find("AuraSync/AttentionBeam");
            bool twoSided = _beam == null;
            _coneMesh = BuildCone(24, twoSided);
            _ringMesh = BuildRing(32, 0.72f, twoSided);
            BuildCanvas();
            SharedStateBus.OnChange += OnStateChange;
        }

        void OnDestroy()
        {
            SharedStateBus.OnChange -= OnStateChange;
            if (Instance == this) Instance = null;
        }

        private void OnStateChange(StateChange c)
        {
            if (c.key != StateKeys.ModelLoaded) return;
            _renderers.Clear();
            _smoothPoint.Clear();
            _smoothDir.Clear();
            _smoothDist.Clear();
        }

        // ── Every frame ────────────────────────────────────────────────────────

        void LateUpdate()
        {
            AttentionSync sync = AttentionSync.Instance;
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Camera cam = Camera.main;
            bool on = sync != null && root != null && cam != null && AttentionSync.ModelShown;

            int cones = 0, tags = 0;
            bool suppressed = FindTask.SuppressAttention;

            // Outside a room only the debug menu's test viewers can be there.
            if (on && (PhotonNetwork.InRoom || sync.Views.Count > 0) && !AttentionSync.ConesHidden &&
                !AttentionSync.ConesOffForRoom && !suppressed)
                DrawCones(sync, root, cam, ref cones, ref tags);

            ConesDrawn = cones;
            for (int i = cones; i < _cones.Count; i++) SetActive(_cones[i], false);
            for (int i = tags; i < _tags.Count; i++) if (_tags[i].rt.gameObject.activeSelf) _tags[i].rt.gameObject.SetActive(false);

            int pings = 0;
            if (on && !suppressed) DrawPings(sync, root, cam, ref pings);
            for (int i = pings; i < _pingObjs.Count; i++) if (_pingObjs[i].rt.gameObject.activeSelf) _pingObjs[i].rt.gameObject.SetActive(false);
        }

        // ── Cones ──────────────────────────────────────────────────────────────

        private void DrawCones(AttentionSync sync, Transform root, Camera cam, ref int coneCount, ref int tagCount)
        {
            float R = AttentionSync.ModelRadiusWorld(root);
            float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime);

            // Each person's spot this frame (smoothed), in world space
            _clusters.Clear();
            var seen = new HashSet<int>();

            // The presenter sees everyone; a viewer only the presenter, and only once they have
            // stopped following (while following, the presenter's view is their own).
            bool presenterView = AttentionSync.LocalIsPresenter;
            int presenterActor = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            SpaceInteractionController sic = SpaceInteractionController.Instance;
            if (!presenterView && (sic == null || sic.IsSyncLocked)) { DropUnseen(seen); return; }

            float rootScale = Mathf.Max(1e-6f, Mathf.Abs(root.lossyScale.x));
            foreach (var kv in sync.Views)
            {
                if (!presenterView && kv.Key != presenterActor) continue;
                AttentionSample s = kv.Value.sample;
                if (!AttentionSync.ToWorld(root, s.path, s.point, s.normal, out Transform part, out Vector3 world, out Vector3 normal)) continue;
                if (!PartVisible(s.path, part)) continue;
                if (SectionCutter.Instance != null && SectionCutter.Instance.IsCut(world)) continue;

                int actor = kv.Key;
                seen.Add(actor);
                Vector3 targetP = root.InverseTransformPoint(world);
                Vector3 targetD = s.dir;
                if (_smoothPoint.TryGetValue(actor, out Vector3 sp))
                {
                    targetP = Vector3.Lerp(sp, targetP, k);
                    targetD = Vector3.Slerp(_smoothDir[actor], targetD, k).normalized;
                }
                float targetDist = s.distance;
                if (_smoothDist.TryGetValue(actor, out float sd)) targetDist = Mathf.Lerp(sd, targetDist, k);
                _smoothPoint[actor] = targetP;
                _smoothDir[actor]   = targetD;
                _smoothDist[actor]  = targetDist;

                Vector3 p = root.TransformPoint(targetP);
                Vector3 d = root.TransformDirection(targetD).normalized;
                AddToCluster(actor, p, d, normal, targetDist * rootScale, R);
            }
            DropUnseen(seen);

            // This phone's own view, to recognise "they see what I see"
            bool haveMine = false;
            Vector3 myPoint = Vector3.zero, myDir = cam.transform.forward;
            AttentionSample mine = sync.LocalView;
            if (mine != null && AttentionSync.ToWorld(root, mine.path, mine.point, mine.normal, out _, out Vector3 mp, out _))
            {
                haveMine = true;
                myPoint = mp;
            }

            int presenter = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            foreach (Cluster c in _clusters)
            {
                c.primary = c.actors.Contains(presenter) ? presenter : Min(c.actors);
                Color color = UserPresence.ColorForActor(c.primary);

                bool same = haveMine && Vector3.Angle(c.dir, myDir) < MergeAngle && (c.point - myPoint).magnitude < MergeDist * R;
                bool behind = Vector3.Dot(c.normal, (cam.transform.position - c.point).normalized) < -0.1f;

                ConeObj o = coneCount < _cones.Count ? _cones[coneCount] : NewCone();
                coneCount++;
                Vector3 tagAt = Place(o, c.point, c.dir, c.distance, R, color, showCone: !same);

                string name = NameOf(c.primary) + (c.actors.Count > 1 ? $" + {c.actors.Count - 1}" : "");
                if (same) name += "  (same view)";
                else if (behind) name += "  · behind";

                Tag t = tagCount < _tags.Count ? _tags[tagCount] : NewTag();
                tagCount++;
                PlaceTag(t, cam, tagAt, name, color, behind ? 0.45f : 1f);
            }
        }

        private void AddToCluster(int actor, Vector3 p, Vector3 d, Vector3 n, float distance, float R)
        {
            foreach (Cluster c in _clusters)
            {
                if (Vector3.Angle(c.dir, d) < MergeAngle && (c.point - p).magnitude < MergeDist * R)
                {
                    c.actors.Add(actor);
                    return;
                }
            }
            var nc = new Cluster { point = p, dir = d, normal = n, distance = distance };
            nc.actors.Add(actor);
            _clusters.Add(nc);
        }

        private void DropUnseen(HashSet<int> seen)
        {
            if (_smoothPoint.Count == seen.Count) return;
            var drop = new List<int>();
            foreach (int a in _smoothPoint.Keys) if (!seen.Contains(a)) drop.Add(a);
            foreach (int a in drop) { _smoothPoint.Remove(a); _smoothDir.Remove(a); _smoothDist.Remove(a); }
        }

        private static int Min(List<int> xs)
        {
            int m = int.MaxValue;
            foreach (int x in xs) if (x < m) m = x;
            return m;
        }

        /// <summary>Places the ring on the spot and the cone off it; returns where the name tag goes.</summary>
        private Vector3 Place(ConeObj o, Vector3 spot, Vector3 dir, float viewDistance, float R, Color color, bool showCone)
        {
            if (o.color != color)
            {
                o.color = color;
                Color c = new Color(color.r, color.g, color.b, Opacity);
                o.coneMat.color = c;
                o.ringMat.color = c;
            }

            Quaternion rot = Quaternion.LookRotation(dir);
            Vector3 ringAt = spot - dir * (SurfaceGap * R);
            float r = ConeRadius * R;

            if (!o.ring.activeSelf) o.ring.SetActive(true);
            o.ring.transform.SetPositionAndRotation(ringAt, rot);
            o.ring.transform.localScale = new Vector3(r, r, r);

            if (o.cone.activeSelf != showCone) o.cone.SetActive(showCone);
            if (!showCone) return spot;

            // Never touching the model: the wide end stands off the spot, farther for a viewer who
            // has zoomed out, nearer for one who has zoomed in.
            float gap = viewDistance > 0f ? Mathf.Clamp(GapShare * viewDistance, MinGap * R, MaxGap * R) : DefaultGap * R;
            float len = ConeLength * R;
            Vector3 wideEnd = spot - dir * gap;
            Vector3 tip = wideEnd - dir * len;
            o.cone.transform.SetPositionAndRotation(tip, rot);
            o.cone.transform.localScale = new Vector3(r, r, len);
            return tip;
        }

        private static void SetActive(ConeObj o, bool on)
        {
            if (o.cone.activeSelf != on) o.cone.SetActive(on);
            if (o.ring.activeSelf != on) o.ring.SetActive(on);
        }

        /// <summary>False when isolation has switched every renderer of the part off.</summary>
        private bool PartVisible(string path, Transform part)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (!_renderers.TryGetValue(path, out Renderer[] rs) || rs == null || rs.Length == 0 || rs[0] == null)
            {
                rs = part.GetComponentsInChildren<Renderer>(true);
                _renderers[path] = rs;
            }
            foreach (Renderer r in rs)
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.gameObject.name != ModelRaycaster.HighlightName)
                    return true;
            return false;
        }

        // ── Pings ──────────────────────────────────────────────────────────────

        private void DrawPings(AttentionSync sync, Transform root, Camera cam, ref int count)
        {
            float now = Time.unscaledTime;
            foreach (AttentionPing p in sync.Pings)
            {
                if (!AttentionSync.ToWorld(root, p.path, p.point, p.normal, out Transform part, out Vector3 world, out Vector3 normal)) continue;

                PingObj o = count < _pingObjs.Count ? _pingObjs[count] : NewPing();
                count++;
                if (!o.rt.gameObject.activeSelf) o.rt.gameObject.SetActive(true);

                Color color = UserPresence.ColorForActor(p.actor);
                o.ringA.color = o.ringB.color = o.dot.color = color;
                o.pillBg.color = new Color(color.r, color.g, color.b, 0.92f);

                // Where it is on screen; pinned to the nearest edge when it is not.
                Vector3 s = cam.WorldToScreenPoint(world);
                bool offScreen = s.z <= 0f || s.x < 0f || s.y < 0f || s.x > Screen.width || s.y > Screen.height;
                if (s.z <= 0f) { s.x = Screen.width - s.x; s.y = Screen.height - s.y; } // behind the camera: mirror
                float m = EdgeMargin * (_canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f);
                s.x = Mathf.Clamp(s.x, m, Screen.width - m);
                s.y = Mathf.Clamp(s.y, m, Screen.height - m);
                o.rt.position = new Vector3(s.x, s.y, 0f);

                bool behind = Vector3.Dot(normal, (cam.transform.position - world).normalized) < -0.1f;
                bool hidden = !PartVisible(p.path, part) ||
                              (SectionCutter.Instance != null && SectionCutter.Instance.IsCut(world));
                string where = offScreen ? "  (off screen)" : hidden ? "  (hidden here)" : behind ? "  (other side)" : "";
                bool mine = NameResolver == null && (!PhotonNetwork.InRoom || p.actor == PhotonNetwork.LocalPlayer.ActorNumber);
                o.text.text = (mine ? "You" : NameOf(p.actor)) + ": look here" + where;

                // Two rings pulsing outwards, half a beat apart, and a fade over the last half second.
                float age = now - p.startTime;
                Pulse(o.ringA, age, 0f);
                Pulse(o.ringB, age, 0.45f);
                float life = Mathf.Clamp01((AttentionSync.PingSeconds - age) / 0.5f);
                o.group.alpha = life * (offScreen || hidden || behind ? 0.7f : 1f);
            }
        }

        private static void Pulse(Image ring, float age, float offset)
        {
            const float Period = 0.9f;
            float t = Mathf.Repeat(age + offset, Period) / Period;
            ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.6f, t);
            Color c = ring.color;
            c.a = 1f - t;
            ring.color = c;
        }

        // ── Names ──────────────────────────────────────────────────────────────

        private static string NameOf(int actor)
        {
            if (NameResolver != null) return (NameResolver(actor) ?? "").Replace("<", "").Replace(">", "");
            Player p = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
            string n = p != null ? NetworkManager.DisplayName(p)
                     : actor >= AttentionSync.TestActorA && actor <= AttentionSync.TestActorC ? $"Test {actor - AttentionSync.TestActorA + 1}"
                     : $"Player {actor}";
            return n.Replace("<", "").Replace(">", "");
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var go = new GameObject("AttentionOverlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 91; // over the touch pointers (90), under note pins (94) and the HUD (95)
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            // No GraphicRaycaster: tags and pings never take a touch.
            _layer = (RectTransform)go.transform;
        }

        private ConeObj NewCone()
        {
            var o = new ConeObj { coneMat = BeamMaterial(fadeToTip: true), ringMat = BeamMaterial(fadeToTip: false) };
            o.cone = MeshObject("ViewCone", _coneMesh, o.coneMat);
            o.ring = MeshObject("ViewRing", _ringMesh, o.ringMat);
            _cones.Add(o);
            return o;
        }

        private Material BeamMaterial(bool fadeToTip)
        {
            if (_beam == null) return PointerVisuals.Create(Color.white, Opacity);
            var m = new Material(_beam) { renderQueue = 3100 }; // after the model, as see-through things must be
            m.SetFloat("_Fade", fadeToTip ? 1f : 0f);
            m.color = new Color(1f, 1f, 1f, Opacity);
            return m;
        }

        private GameObject MeshObject(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        private Tag NewTag()
        {
            var go = new GameObject("ViewTag", typeof(RectTransform), typeof(CanvasGroup), typeof(Image),
                                    typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(_layer, false);
            var rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0.5f, 0f); // sits just above the spot

            var bg = go.GetComponent<Image>();
            bg.raycastTarget = false;
            var hg = go.GetComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(14, 14, 4, 4);
            hg.childControlWidth = hg.childControlHeight = true;
            var fit = go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = Label(go.transform, 21f);
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;

            var t = new Tag { rt = rt, group = group, bg = bg, text = text };
            _tags.Add(t);
            return t;
        }

        private void PlaceTag(Tag t, Camera cam, Vector3 world, string text, Color color, float alpha)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            bool visible = s.z > 0f && s.x >= 0f && s.y >= 0f && s.x <= Screen.width && s.y <= Screen.height;
            if (t.rt.gameObject.activeSelf != visible) t.rt.gameObject.SetActive(visible);
            if (!visible) return;

            float lift = 26f * (_canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f);
            t.rt.position = new Vector3(s.x, s.y + lift, 0f);
            if (t.text.text != text) t.text.text = text;
            t.bg.color = new Color(color.r, color.g, color.b, 0.9f);
            t.group.alpha = alpha;
        }

        private PingObj NewPing()
        {
            var go = new GameObject("Ping", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_layer, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = Vector2.zero;
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;

            Image Circle(string name, Sprite sprite, float size)
            {
                var c = new GameObject(name, typeof(RectTransform), typeof(Image));
                c.transform.SetParent(go.transform, false);
                var crt = (RectTransform)c.transform;
                crt.sizeDelta = new Vector2(size, size);
                var img = c.GetComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                return img;
            }

            var o = new PingObj
            {
                rt = rt, group = group,
                ringA = Circle("RingA", RingSprite, 150f),
                ringB = Circle("RingB", RingSprite, 150f),
                dot   = Circle("Dot", DotSprite, 26f),
            };

            var pill = new GameObject("Pill", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            pill.transform.SetParent(go.transform, false);
            var prt = (RectTransform)pill.transform;
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 64f);
            o.pillBg = pill.GetComponent<Image>();
            o.pillBg.raycastTarget = false;
            var hg = pill.GetComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(16, 16, 6, 6);
            hg.childControlWidth = hg.childControlHeight = true;
            var fit = pill.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            o.text = Label(pill.transform, 24f);

            go.SetActive(false);
            _pingObjs.Add(o);
            return o;
        }

        private static TextMeshProUGUI Label(Transform parent, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.color = Color.white;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            return t;
        }

        // ── Meshes and sprites ─────────────────────────────────────────────────

        /// <summary>
        /// Open cone, tip at the origin, rim of radius 1 at z = 1; uv.y runs 0 at the tip to 1 at
        /// the rim. twoSided adds back faces, for a shader that culls them.
        /// </summary>
        private static Mesh BuildCone(int segments, bool twoSided)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int side = 0; side < (twoSided ? 2 : 1); side++)
            {
                int start = v.Count;
                float sign = side == 0 ? 1f : -1f;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    Vector3 rim = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f);
                    Vector3 normal = new Vector3(rim.x, rim.y, -1f).normalized * sign;
                    v.Add(Vector3.zero); n.Add(normal); uv.Add(new Vector2(i / (float)segments, 0f));
                    v.Add(rim);          n.Add(normal); uv.Add(new Vector2(i / (float)segments, 1f));
                }
                for (int i = 0; i < segments; i++)
                {
                    int tip = start + i * 2, a = tip + 1, b = tip + 3;
                    if (side == 0) { tris.Add(tip); tris.Add(b); tris.Add(a); }
                    else           { tris.Add(tip); tris.Add(a); tris.Add(b); }
                }
            }
            var m = new Mesh { name = "AttentionCone" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat ring in the XY plane, outer radius 1; twoSided as for the cone.</summary>
        private static Mesh BuildRing(int segments, float inner, bool twoSided)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            for (int side = 0; side < (twoSided ? 2 : 1); side++)
            {
                int start = v.Count;
                Vector3 normal = side == 0 ? Vector3.forward : Vector3.back; // side 0 faces +Z
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v.Add(dir * inner); n.Add(normal);
                    v.Add(dir);         n.Add(normal);
                }
                for (int i = 0; i < segments; i++)
                {
                    int i0 = start + i * 2, o0 = i0 + 1, i1 = i0 + 2, o1 = i0 + 3;
                    if (side == 0) { tris.Add(i0); tris.Add(o0); tris.Add(o1); tris.Add(i0); tris.Add(o1); tris.Add(i1); }
                    else           { tris.Add(i0); tris.Add(o1); tris.Add(o0); tris.Add(i0); tris.Add(i1); tris.Add(o1); }
                }
            }
            var m = new Mesh { name = "AttentionRing" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Sprite RingSprite => _ringSprite != null ? _ringSprite : (_ringSprite = CircleSprite(0.80f));
        private static Sprite DotSprite  => _dotSprite  != null ? _dotSprite  : (_dotSprite  = CircleSprite(0f));

        /// <summary>A white circle (inner = 0) or ring, soft-edged, made in code: no texture asset needed.</summary>
        private static Sprite CircleSprite(float inner)
        {
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            float half = S * 0.5f, edge = 1.5f / half;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float r = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                    float a = Mathf.Clamp01((1f - r) / edge);
                    if (inner > 0f) a *= Mathf.Clamp01((r - inner) / edge);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }
    }
}
