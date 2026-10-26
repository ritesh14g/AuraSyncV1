using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Networking;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Space
{
    /// <summary>
    /// The section cut (V3 Phase 6): one plane through the model; everything on one side disappears
    /// so you can see inside, and the cut faces are filled with a flat colour so parts read as solid.
    ///
    /// **The cut is described in the model's own terms** - which axis (X / Y / Z), how far along it
    /// (MOVE, -1..1 across the model), a TILT angle, and FLIP - measured against the model's
    /// assembled bounds, which come out identical on every device from the same GLB. So the same
    /// numbers give the same cut through the same parts on every phone, however each viewer has
    /// turned their model, and the cut stays fixed to the model as it turns.
    ///
    /// **Rendering.** Only while a cut is on, every glTF material is swapped for a copy using
    /// <c>AuraSync/SectionClip</c> (Assets/Shaders), which clips against a global plane updated
    /// every frame, and swapped back afterwards. If that shader is missing or unsupported on a phone,
    /// a camera-based cut takes over: the camera's near plane is tilted onto the cut. It works with
    /// any material but cannot fill the cut faces, and only while the camera is on the removed side.
    ///
    /// **In a room** it is shared like the explosion: whoever holds the Section control
    /// (<see cref="ControlOwnership"/>) is asked "show this cut to everyone?" when they let go of a
    /// slider or press a button (or not, with KEEP SHARING, see <see cref="LiveSharing"/>, in which
    /// case drags also stream at up to 10 Hz on event 23). Followers take the room's cut; a viewer
    /// cutting on their own is exploring and Catch Up brings back the room's; a presenter who handed
    /// the control out watches the holder's cut.
    /// </summary>
    public class SectionCutter : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static SectionCutter Instance { get; private set; }

        public const string ShaderName = "AuraSync/SectionClip";
        public const float MaxTilt = 60f;
        private const float StreamInterval = 0.1f; // 10 Hz while dragging, when shared live

        /// <summary>Everything that defines a cut. Shared as float[5].</summary>
        [Serializable]
        public struct CutState
        {
            public bool  active;
            public int   axis;   // 0 = X, 1 = Y, 2 = Z (model space)
            public float move;   // -1..1 across the model along the cut's direction
            public float tilt;   // degrees, -MaxTilt..MaxTilt
            public bool  flip;   // which side is removed

            public static readonly CutState Off = new CutState();
            public static CutState Default => new CutState { active = true, axis = 0, move = 0f, tilt = 0f, flip = false };

            public float[] Pack() => new[] { active ? 1f : 0f, axis, move, tilt, flip ? 1f : 0f };

            public static CutState Unpack(object raw)
            {
                if (!(raw is float[] a) || a.Length < 5) return Off;
                return new CutState
                {
                    active = a[0] > 0.5f, axis = Mathf.Clamp(Mathf.RoundToInt(a[1]), 0, 2),
                    move = Mathf.Clamp(a[2], -1f, 1f), tilt = Mathf.Clamp(a[3], -MaxTilt, MaxTilt), flip = a[4] > 0.5f,
                };
            }

            public bool SameAs(CutState o) =>
                active == o.active && (!active || (axis == o.axis && flip == o.flip &&
                Mathf.Abs(move - o.move) < 0.002f && Mathf.Abs(tilt - o.tilt) < 0.2f));
        }

        public CutState State { get; private set; }

        /// <summary>Fill the cut faces with a flat colour. A viewing preference: this phone only.</summary>
        public static bool Fill { get; private set; } = true;

        /// <summary>Show the thin frame marking the cut (the toolbar turns it on while open).</summary>
        public bool ShowFrame { get; set; }

        /// <summary>False when the clipping shader is unavailable and the simple camera cut is used.</summary>
        public bool ShaderCutAvailable => !ForceCameraCut && _shader != null && _shader.isSupported;

        /// <summary>Debug: use the camera cut even where the shader works, to test the fallback in the Editor.</summary>
        public static bool ForceCameraCut { get; private set; }

        public void ToggleCameraFallback()
        {
            RestoreMaterials();
            StopCameraCut(Camera.main);
            ForceCameraCut = !ForceCameraCut;
            Apply(State, isLocal: false);
            Debug.Log($"[SectionCutter] {(ForceCameraCut ? "Camera cut forced (fallback test)." : "Shader cut back on.")}");
        }

        public event Action OnChanged;

        private static readonly Color CapColor   = new Color(0.93f, 0.56f, 0.26f, 1f);
        private static readonly Color FrameColor = new Color(0.60f, 0.58f, 0.77f, 1f); // the app's lavender (was cyan)

        private static readonly int PlaneId = Shader.PropertyToID("_AuraSectionPlane");
        private static readonly int CapId   = Shader.PropertyToID("_AuraSectionCapColor");
        private static readonly int FillId  = Shader.PropertyToID("_AuraSectionFill");

        private Shader _shader;
        private Bounds _bounds;            // the model's assembled bounds, in the root's space
        private bool   _haveBounds;
        private Vector4 _worldPlane;       // xyz towards the kept side, w offset
        private bool   _planeValid;

        private readonly Dictionary<Material, Material>   _clipMats  = new Dictionary<Material, Material>();
        private readonly Dictionary<Renderer, Material[]> _originals = new Dictionary<Renderer, Material[]>();
        private bool _cameraCutOn;

        private float _lastStream = -1f;
        private Transform[] _frame;

        // Frame-time averages for the paper's "frame rate stays usable" check.
        private float _msCut = -1f, _msNoCut = -1f;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _shader = Shader.Find(ShaderName);
            if (!ShaderCutAvailable)
                Debug.LogWarning($"[SectionCutter] '{ShaderName}' is {(_shader == null ? "not in this build" : "not supported on this device")}; " +
                                 "using the camera cut (no filled cut faces).");

            Camera.onPreCull += OnPreCullCamera;
            ControlOwnership.OnChanged += OnOwnershipChanged;
            ModelRaycaster.IsPointCut = IsCut;
            Shader.SetGlobalColor(CapId, CapColor);
            Shader.SetGlobalVector(PlaneId, Vector4.zero);
        }

        void OnDestroy()
        {
            Camera.onPreCull -= OnPreCullCamera;
            ControlOwnership.OnChanged -= OnOwnershipChanged;
            if (ModelRaycaster.IsPointCut == (Func<Vector3, bool>)IsCut) ModelRaycaster.IsPointCut = null;
            RestoreMaterials();
            foreach (Material m in _clipMats.Values) if (m != null) Destroy(m);
            _clipMats.Clear();
            StopCameraCut(Camera.main);
            Shader.SetGlobalVector(PlaneId, Vector4.zero);
            if (_frame != null) foreach (Transform t in _frame) if (t != null) Destroy(t.gameObject);
            if (Instance == this) Instance = null;
        }

        /// <summary>Called by SpaceSceneManager once a model is loaded, before anything moves its parts.</summary>
        public void OnModelLoaded()
        {
            // The old model's renderers and materials are gone.
            _originals.Clear();
            foreach (Material m in _clipMats.Values) if (m != null) Destroy(m);
            _clipMats.Clear();

            Transform root = Root;
            _haveBounds = root != null && ModelRaycaster.TryGetLocalBounds(root, out _bounds);

            // A viewer arriving at a model the room has cut arrives cut. The presenter's own view of
            // each model comes back through ModelStateMemory instead.
            CutState s = CutState.Off;
            if (PhotonNetwork.InRoom && !IsPresenting && FollowsRoom()) s = ReadRoom();
            Apply(s, isLocal: false);
        }

        private static Transform Root =>
            SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;

        // ── Who drives it ──────────────────────────────────────────────────────

        public static bool IsPresenting =>
            PhotonNetwork.InRoom && ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;

        /// <summary>True when this device's cut goes to the room: it holds the Section control.</summary>
        public static bool DrivesRoom => ControlOwnership.LocalDrivesRoom(Control.Section);

        /// <summary>False for a presenter who handed Section out: they watch the holder until TAKE BACK.</summary>
        public static bool LocalPlayerMayCut => !(IsPresenting && !DrivesRoom);

        /// <summary>The room's cut should show here: a following viewer, or a presenter watching the holder.</summary>
        private static bool FollowsRoom()
        {
            if (!PhotonNetwork.InRoom || DrivesRoom) return false;
            if (IsPresenting) return true;
            var sic = SpaceInteractionController.Instance;
            return sic == null || sic.IsSyncLocked;
        }

        public static CutState ReadRoom()
        {
            if (!PhotonNetwork.InRoom) return CutState.Off;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomKeys.SectionPlane, out object v)
                ? CutState.Unpack(v) : CutState.Off;
        }

        // ── Changing it ────────────────────────────────────────────────────────

        /// <summary>
        /// A change from this device's toolbar. <paramref name="dragging"/>: a slider is moving, so
        /// the question (if any) waits for <see cref="Commit"/> on release.
        /// </summary>
        public void SetState(CutState s, bool dragging)
        {
            if (!LocalPlayerMayCut) { OnChanged?.Invoke(); return; }
            Apply(s, isLocal: true);
            if (!PhotonNetwork.InRoom) return;

            if (DrivesRoom)
            {
                // Shared live: let followers watch the drag, a few times a second.
                if (dragging && LiveSharing.IsLive(Control.Section) && Time.unscaledTime - _lastStream >= StreamInterval)
                {
                    _lastStream = Time.unscaledTime;
                    PhotonNetwork.RaiseEvent(EventCodes.SectionPlane, State.Pack(),
                        new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendUnreliable);
                }
            }
            else
            {
                // A viewer cutting has gone off to explore, like turning the model by hand.
                var sic = SpaceInteractionController.Instance;
                if (sic != null && sic.IsSyncLocked) sic.BreakSync();
            }
        }

        /// <summary>A slider was let go or a button pressed: offer the cut to the room if it differs.</summary>
        public void Commit()
        {
            if (!DrivesRoom || State.SameAs(ReadRoom())) return;

            bool on = State.active;
            LiveSharing.Offer(Control.Section,
                on ? "Show this section cut to everyone?" : "Remove the section cut for everyone?",
                on ? "Accept: everyone following you sees the model cut open like this.\nDeny: only you see it."
                   : "Accept: everyone following you sees the whole model again.\nDeny: only you do.",
                share: () =>
                {
                    if (DrivesRoom) PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomKeys.SectionPlane, State.Pack() } });
                });
        }

        public static void SetFill(bool fill)
        {
            Fill = fill;
            Instance?.OnChanged?.Invoke();
        }

        /// <summary>A remembered cut put back on screen without asking or telling anyone (ModelStateMemory).</summary>
        public void ApplyQuietly(CutState s) => Apply(s, isLocal: false);

        /// <summary>Catch Up: take the room's cut whatever this device was doing.</summary>
        public void SyncFromRoom()
        {
            if (IsPresenting) return;
            Apply(ReadRoom(), isLocal: false);
        }

        private void Apply(CutState s, bool isLocal)
        {
            // The challenge moves parts by hand; a cut through them would only get in the way.
            if (AuraSync.Challenge.ReassemblyGame.IsActive) s = CutState.Off;

            bool changed = !s.SameAs(State);
            State = s;

            if (s.active) EnsureClipping();
            else { RestoreMaterials(); StopCameraCut(Camera.main); }

            if (changed) SharedStateBus.Publish(StateKeys.Section, s.Pack(), isLocal);
            OnChanged?.Invoke();
        }

        // ── The room ───────────────────────────────────────────────────────────

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            if (!changed.TryGetValue(RoomKeys.SectionPlane, out object raw)) return;
            if (DrivesRoom) return; // our own value coming back
            if (FollowsRoom()) Apply(CutState.Unpack(raw), isLocal: false);
        }

        public void OnEvent(EventData e)
        {
            if (e.Code != EventCodes.SectionPlane) return;
            if (DrivesRoom || !FollowsRoom()) return;
            CutState s = CutState.Unpack(e.CustomData);
            if (s.active) Apply(s, isLocal: false); // a drag in progress; the release sets the room property
        }

        private void OnOwnershipChanged()
        {
            // Someone else now drives it: a presenter who handed it out, or a following holder who
            // lost it, goes to what the room shows.
            if (PhotonNetwork.InRoom && !DrivesRoom && FollowsRoom()) Apply(ReadRoom(), isLocal: false);
            else OnChanged?.Invoke();
        }

        public override void OnLeftRoom() => OnChanged?.Invoke();

        void Update()
        {
            if (State.active && AuraSync.Challenge.ReassemblyGame.IsActive) Apply(CutState.Off, isLocal: false);

            // Smoothed frame time with and without a cut, for the frame-rate check.
            float ms = Time.unscaledDeltaTime * 1000f;
            if (State.active) _msCut = _msCut < 0f ? ms : Mathf.Lerp(_msCut, ms, 0.02f);
            else _msNoCut = _msNoCut < 0f ? ms : Mathf.Lerp(_msNoCut, ms, 0.02f);
        }

        public string Stats() =>
            $"Section cut: {(ShaderCutAvailable ? "shader cut" : "camera cut (shader unavailable)")}; " +
            $"frame time {(_msNoCut < 0f ? "n/a" : $"{_msNoCut:0.0} ms")} without a cut, " +
            $"{(_msCut < 0f ? "n/a" : $"{_msCut:0.0} ms")} with one.";

        // ── The plane ──────────────────────────────────────────────────────────

        /// <summary>The cut in the model root's space: a point on it and the direction of the removed side.</summary>
        private bool TryGetModelPlane(CutState s, out Vector3 point, out Vector3 removedDir)
        {
            point = Vector3.zero; removedDir = Vector3.right;
            if (!_haveBounds || !s.active) return false;

            Vector3 axis = s.axis == 0 ? Vector3.right : s.axis == 1 ? Vector3.up : Vector3.forward;
            Vector3 hinge = s.axis == 0 ? Vector3.forward : s.axis == 1 ? Vector3.right : Vector3.up;
            Vector3 n = Quaternion.AngleAxis(s.tilt, hinge) * axis;

            Vector3 e = _bounds.extents;
            float half = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;

            point = _bounds.center + n * (s.move * half * 1.02f); // just past the model at either end
            removedDir = s.flip ? -n : n;
            return true;
        }

        private void UpdateWorldPlane()
        {
            _planeValid = false;
            Transform root = Root;
            if (root == null || !TryGetModelPlane(State, out Vector3 p, out Vector3 removed)) return;

            Vector3 keepN = (-root.TransformDirection(removed)).normalized;
            Vector3 pw = root.TransformPoint(p);
            _worldPlane = new Vector4(keepN.x, keepN.y, keepN.z, -Vector3.Dot(keepN, pw));
            _planeValid = true;
        }

        /// <summary>True when a world-space point is on the removed side of an active cut.</summary>
        public bool IsCut(Vector3 world)
        {
            if (!State.active || !_planeValid) return false;
            return Vector3.Dot(_worldPlane, world) + _worldPlane.w < 0f;
        }

        // ── Rendering ──────────────────────────────────────────────────────────

        private int _planeFrame = -1;

        /// <summary>
        /// Right before each camera culls, after every LateUpdate (including the viewer mirroring the
        /// presenter's pose), so the cut is exactly where the model is this frame.
        /// </summary>
        private void OnPreCullCamera(Camera cam)
        {
            if (_planeFrame != Time.frameCount)
            {
                _planeFrame = Time.frameCount;
                UpdateWorldPlane();
                bool shaderCut = State.active && _planeValid && ShaderCutAvailable;
                Shader.SetGlobalVector(PlaneId, shaderCut ? _worldPlane : Vector4.zero);
                Shader.SetGlobalFloat(FillId, Fill ? 1f : 0f);
                UpdateFrame();
            }

            if (cam != Camera.main) return;
            if (State.active && _planeValid && !ShaderCutAvailable) ApplyCameraCut(cam);
            else StopCameraCut(cam);
        }

        /// <summary>Swaps the model's glTF materials for clipping copies (once per cut).</summary>
        private void EnsureClipping()
        {
            if (!ShaderCutAvailable || _originals.Count > 0) return;
            Transform root = Root;
            if (root == null) return;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                Material[] mats = r.sharedMaterials;
                var swapped = new Material[mats.Length];
                bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    swapped[i] = ClipCopy(mats[i]);
                    any |= swapped[i] != mats[i];
                }
                if (!any) continue;
                _originals[r] = mats;
                r.sharedMaterials = swapped;
            }
            Debug.Log($"[SectionCutter] Cut on: {_originals.Count} renderer(s), {_clipMats.Count} material(s) swapped.");
        }

        private Material ClipCopy(Material m)
        {
            if (m == null || m.shader == null || !m.shader.name.StartsWith("glTF/")) return m; // not ours to cut
            if (_clipMats.TryGetValue(m, out Material c) && c != null) return c;

            c = new Material(_shader) { name = m.name + " (cut)" };
            c.CopyPropertiesFromMaterial(m); // same property names: textures, factors, transforms
            c.shader = _shader;              // in case the copy brought the original shader along
            c.shaderKeywords = new string[0];
            c.renderQueue = m.renderQueue;
            c.SetFloat("_AuraUnlit", m.shader.name.Contains("Unlit") ? 1f : 0f);
            c.SetFloat("_AuraAlphaClip", m.IsKeywordEnabled("_ALPHATEST_ON") ? 1f : 0f);
            c.SetFloat("_AuraNormalMap", m.IsKeywordEnabled("_NORMALMAP") ? 1f : 0f);
            c.SetFloat("_AuraEmission", m.IsKeywordEnabled("_EMISSION") ? 1f : 0f);
            if (m.HasProperty("_CullMode")) c.SetFloat("_CullMode", m.GetFloat("_CullMode"));
            _clipMats[m] = c;
            return c;
        }

        private void RestoreMaterials()
        {
            foreach (var kv in _originals)
                if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            _originals.Clear();
        }

        /// <summary>
        /// Fallback cut: the camera's near plane laid onto the cut. Only meaningful while the camera
        /// is on the removed side; from the kept side there is nothing in front to take away.
        /// </summary>
        private void ApplyCameraCut(Camera cam)
        {
            if (cam == null) return;
            if (Vector3.Dot(_worldPlane, cam.transform.position) + _worldPlane.w >= 0f) { StopCameraCut(cam); return; }

            cam.ResetProjectionMatrix();
            Vector4 camPlane = Matrix4x4.Transpose(cam.cameraToWorldMatrix) * _worldPlane;
            cam.projectionMatrix = cam.CalculateObliqueMatrix(camPlane);
            _cameraCutOn = true;
        }

        private void StopCameraCut(Camera cam)
        {
            if (!_cameraCutOn || cam == null) return;
            cam.ResetProjectionMatrix();
            _cameraCutOn = false;
        }

        // ── The frame marking the cut ──────────────────────────────────────────

        /// <summary>
        /// Four thin bars around the cut, drawn while the toolbar is open. Kept outside the model's
        /// hierarchy (so part paths, the drawer and the raycaster never see them) and with an
        /// ordinary material, so the cut never clips its own marker.
        /// </summary>
        private void UpdateFrame()
        {
            bool show = ShowFrame && State.active && _planeValid;
            if (_frame == null)
            {
                if (!show) return;
                BuildFrame();
            }

            foreach (Transform t in _frame) if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
            if (!show) return;

            Transform root = Root;
            Vector3 n = new Vector3(_worldPlane.x, _worldPlane.y, _worldPlane.z);
            Vector3 centre = root.TransformPoint(_bounds.center);
            centre -= n * (Vector3.Dot(n, centre) + _worldPlane.w); // onto the plane

            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u);
            float r = _bounds.extents.magnitude * root.lossyScale.x * 1.02f;
            float th = r * 0.012f;

            Place(_frame[0], centre + v * r, u, r * 2f + th, th, n);
            Place(_frame[1], centre - v * r, u, r * 2f + th, th, n);
            Place(_frame[2], centre + u * r, v, r * 2f + th, th, n);
            Place(_frame[3], centre - u * r, v, r * 2f + th, th, n);
        }

        private static void Place(Transform t, Vector3 pos, Vector3 along, float length, float th, Vector3 normal)
        {
            t.position = pos;
            t.rotation = Quaternion.LookRotation(along, normal);
            t.localScale = new Vector3(th, th, length);
        }

        private void BuildFrame()
        {
            _frame = new Transform[4];
            Material mat = PointerVisuals.CreateGlowing(FrameColor);
            for (int i = 0; i < 4; i++)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = "AuraSync_SectionFrame";
                Destroy(bar.GetComponent<Collider>());
                Renderer rd = bar.GetComponent<Renderer>();
                rd.sharedMaterial = mat;
                rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rd.receiveShadows = false;
                _frame[i] = bar.transform;
            }
        }
    }
}
