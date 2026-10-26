using System;
using UnityEngine;
using AuraSync.Core;

namespace AuraSync.Space
{
    /// <summary>
    /// Handles touch/mouse interactions for 3D model viewer and real-time Admin sync.
    ///
    /// SYNC LOCK PATTERN:
    ///   - IsSyncLocked = true  → Viewer model smoothly mirrors Admin's position, rotation, scale in real-time.
    ///   - Viewer dragging model → BreakSync() unlocks viewer for manual inspection.
    ///   - Catch Up button     → SnapToAdmin() instantly snaps model to Admin and re-locks sync.
    /// </summary>
    public class SpaceInteractionController : MonoBehaviour
    {
        public static SpaceInteractionController Instance { get; private set; }

        /// <summary>The loaded model's root (null until a model has loaded).</summary>
        public Transform ModelRoot => modelRoot;

        /// <summary>A short, still touch (or click) that was not over UI. Argument = screen position.</summary>
        public event Action<Vector2> OnTap;

        [Header("Target")]
        [SerializeField] private Transform modelRoot;

        [Header("Rotation")]
        [SerializeField] private float rotationSpeed     = 0.3f;
        [SerializeField] private float rotationSmoothing = 10f;

        [Header("Zoom (Pinch)")]
        [SerializeField] private float zoomSpeed         = 0.01f;
        [SerializeField] private float minScale          = 0.05f;
        [SerializeField] private float maxScale          = 5f;

        [Header("Pan (Two-finger drag)")]
        [SerializeField] private float panSpeed          = 0.003f;

        [Header("Smoothing")]
        [Tooltip("Lerp speed for mirroring remote Admin transforms.")]
        [SerializeField] private float remoteInterpolationSpeed = 18f;

        /// <summary>
        /// True = viewer is locked to Admin's transform (mirror mode).
        /// False = viewer is controlling model freely.
        /// Admin always has IsSyncLocked = false.
        /// </summary>
        public bool IsSyncLocked { get; private set; } = true;

        public event Action<bool> OnSyncLockChanged;

        private Vector3    _lastAdminPos;
        private Quaternion _lastAdminRot = Quaternion.identity;
        private Vector3    _lastAdminScale = Vector3.one;
        private bool       _hasAdminTransform = false;

        private Vector2 _lastSinglePos;
        private float   _lastPinchDistance;
        private Vector2 _lastTwoFingerMidpoint;
        private bool    _wasTwoFinger = false;
        private Vector2 _lastMousePos;

        // Rotation is a free quaternion, not yaw + pitch: a drag turns the model about the axis at
        // right angles to the drag on screen, so left/right, up/down and every slant in between
        // turn it the way the finger goes, from any angle and right over the top.
        private Quaternion _targetRot  = Quaternion.identity;
        private Quaternion _currentRot = Quaternion.identity;

        // Zoom limits are relative to the scale the model was fitted at: models arrive anything
        // from 0.0006 (the Common Press, in millimetres) to 5x (the Antikythera, in metres), so
        // fixed limits made the first pinch jump the model to the nearest limit.
        private float _fitScale;
        private const float MinZoom = 0.25f, MaxZoom = 6f;
        private bool _twoFingerTracking;

        // Tap detection
        private const float TapMaxSeconds    = 0.3f;
        private const float TapMaxMovePixels = 24f;
        private bool    _pointerDown;
        private bool    _tapPossible;
        private Vector2 _tapStart;
        private float   _tapStartTime;
        private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _uiHits =
            new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();

        private AuraSync.Networking.ZoneManager _zone;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            _zone = AuraSync.Networking.ZoneManager.Instance;
            if (_zone != null) _zone.OnAdminStatusChanged += HandleAdminChanged;
        }

        void OnDestroy()
        {
            if (_zone != null) _zone.OnAdminStatusChanged -= HandleAdminChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>The presenter role moved. The new presenter drives; everyone else mirrors.</summary>
        private void HandleAdminChanged(bool isAdmin)
        {
            bool wasLocked = IsSyncLocked;

            _catchingUp = false; // the pose we were gliding towards belongs to the old presenter

            if (isAdmin)
            {
                SeedRotationFromModel(); // continue from what is on screen, no jump
                IsSyncLocked = false;
            }
            else
            {
                _hasAdminTransform = false; // stale pose from before; wait for the new presenter's
                IsSyncLocked = true;
            }

            if (wasLocked != IsSyncLocked) OnSyncLockChanged?.Invoke(IsSyncLocked);
        }

        /// <summary>Make the free-rotation state match the model's current rotation.</summary>
        private void SeedRotationFromModel()
        {
            if (modelRoot == null) return;

            _targetRot = _currentRot = modelRoot.rotation;
        }

        /// <summary>The scale the model was fitted at when it opened (SpaceSceneManager.AutoFitModel).</summary>
        public void SetFitScale(float scale) => _fitScale = scale;

        /// <summary>
        /// The turn a model opens at (the catalogue's openRotation: some files face away from the
        /// camera). Set before <see cref="OnModelLoaded"/>; a later RestorePose (this person's last
        /// view of the model, or the presenter's) still wins.
        /// </summary>
        public void SetOpenRotation(Quaternion rotation) => _openRot = rotation;
        private Quaternion _openRot = Quaternion.identity;

        private float ClampScale(float s) =>
            _fitScale > 0f ? Mathf.Clamp(s, _fitScale * MinZoom, _fitScale * MaxZoom) : Mathf.Clamp(s, minScale, maxScale);

        public void OnModelLoaded(Transform root)
        {
            modelRoot = root;
            _targetRot = _currentRot = _openRot;
            if (root != null) root.rotation = _openRot;

            bool isAdmin = AuraSync.Networking.ZoneManager.Instance != null &&
                           AuraSync.Networking.ZoneManager.Instance.IsAdmin;
            IsSyncLocked = !isAdmin;
        }

        /// <summary>
        /// Puts the model back where this device's user last left it (ModelStateMemory, when a model
        /// is reopened). Rotation continues from the restored pose, with no jump on the next drag.
        /// </summary>
        public void RestorePose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (modelRoot == null) return;
            modelRoot.position   = position;
            modelRoot.rotation   = rotation;
            modelRoot.localScale = scale;
            SeedRotationFromModel();
        }

        /// <summary>
        /// When the presenter last actually moved the model. The HUD uses this to nudge a viewer who
        /// has wandered off - there is no point pulsing "catch up" at someone while the presenter is
        /// sitting still, and the heartbeat keeps arriving either way.
        /// </summary>
        public float LastAdminMoveTime { get; private set; } = -999f;

        public void ApplyRemoteTransform(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            if (_hasAdminTransform)
            {
                bool moved = (pos - _lastAdminPos).sqrMagnitude > 1e-6f
                          || (scale - _lastAdminScale).sqrMagnitude > 1e-6f
                          || Quaternion.Angle(rot, _lastAdminRot) > 0.5f;

                if (moved) LastAdminMoveTime = Time.unscaledTime;
            }

            _lastAdminPos   = pos;
            _lastAdminRot   = rot;
            _lastAdminScale = scale;
            _hasAdminTransform = true;
        }

        public void ApplyRemoteTransform(Vector3 pos, Vector3 rotEuler, Vector3 scale)
        {
            ApplyRemoteTransform(pos, Quaternion.Euler(rotEuler), scale);
        }

        /// <summary>
        /// Turns the model (smoothly) so that a direction on it, such as the surface a part note
        /// stands on, points at the camera. A viewer who was following has started exploring, as if
        /// they had turned it by hand; the presenter's turn reaches their followers as usual.
        /// </summary>
        public void TurnToFace(Vector3 worldDirection)
        {
            Camera cam = Camera.main;
            if (modelRoot == null || cam == null || worldDirection.sqrMagnitude < 1e-8f) return;

            if (IsSyncLocked) BreakSync();
            else SeedRotationFromModel();

            Vector3 n = modelRoot.InverseTransformDirection(worldDirection).normalized; // on the model
            Vector3 c = (cam.transform.position - modelRoot.position).normalized;      // towards the viewer

            // The rotation is yaw then pitch (no roll), with pitch limited like dragging is; a coarse
            // search and a fine one find the best pair without having to get the signs right by hand.
            Vector3 start = _targetRot.eulerAngles;
            float bestX = start.x > 180f ? start.x - 360f : start.x, bestY = start.y, bestDot = float.MinValue;
            void Try(float x, float y)
            {
                float d = Vector3.Dot(Quaternion.Euler(x, y, 0f) * n, c);
                if (d > bestDot) { bestDot = d; bestX = x; bestY = y; }
            }
            for (float x = -85f; x <= 85f; x += 5f)
                for (float y = 0f; y < 360f; y += 5f) Try(x, y);
            float cx = bestX, cy = bestY;
            for (float x = cx - 5f; x <= cx + 5f; x += 1f)
                for (float y = cy - 5f; y <= cy + 5f; y += 1f) Try(Mathf.Clamp(x, -85f, 85f), y);

            _targetRot = Quaternion.Euler(bestX, bestY, 0f); // the Slerp in ApplySmoothedRotation goes the short way round
        }

        public void BreakSync()
        {
            bool isAdmin = AuraSync.Networking.ZoneManager.Instance != null &&
                           AuraSync.Networking.ZoneManager.Instance.IsAdmin;
            if (isAdmin) return; // Admin never sync-locks

            if (IsSyncLocked)
            {
                _catchingUp = false; // a touch mid-flight cancels the catch-up
                SeedRotationFromModel();
                IsSyncLocked = false;
                OnSyncLockChanged?.Invoke(false);
                Debug.Log("[SpaceInteractionController] Viewer broke sync with Admin.");
            }
        }

        [Header("Catch Up")]
        [Tooltip("Seconds to glide to the presenter's view when Catch Up is pressed.")]
        [SerializeField] private float catchUpDuration = 0.45f;

        private bool       _catchingUp;
        private float      _catchUpStartTime;
        private Vector3    _catchUpFromPos;
        private Quaternion _catchUpFromRot = Quaternion.identity;
        private Vector3    _catchUpFromScale = Vector3.one;

        /// <summary>True while the model is gliding to the presenter's view.</summary>
        public bool IsCatchingUp => _catchingUp;

        public void SnapToAdmin()
        {
            if (modelRoot == null) return;

            if (_hasAdminTransform)
            {
                // Glide rather than teleport. Jumping straight to the presenter's pose left no clue
                // which way the model turned, so people lost track of what they were looking at.
                _catchingUp       = true;
                _catchUpStartTime = Time.unscaledTime;
                _catchUpFromPos   = modelRoot.position;
                _catchUpFromRot   = modelRoot.rotation;
                _catchUpFromScale = modelRoot.localScale;
            }

            IsSyncLocked = true;

            // Catching up means seeing what the presenter sees, which includes how far apart they
            // have pulled the model and which part they have isolated - not only their angle.
            ModelExploder.Instance?.SyncFromRoom();
            ModelIsolator.Instance?.SyncFromRoom();
            SectionCutter.Instance?.SyncFromRoom();
            AuraSync.Simulation.SimulationController.Instance?.SyncFromRoom();

            OnSyncLockChanged?.Invoke(true);
            Debug.Log("[SpaceInteractionController] Viewer catching up to Admin — mirror re-locked.");
        }

        /// <summary>
        /// Eases from where the viewer was towards the presenter's *live* pose, so the presenter
        /// carrying on moving during the glide is followed rather than fought.
        /// </summary>
        /// <returns>False once the glide is over and ordinary mirroring should resume.</returns>
        private bool ApplyCatchUp()
        {
            float duration = Mathf.Max(0.05f, catchUpDuration);
            float t = (Time.unscaledTime - _catchUpStartTime) / duration;

            if (t >= 1f)
            {
                _catchingUp = false;
                SeedRotationFromModel();
                return false;
            }

            float eased = Mathf.SmoothStep(0f, 1f, t);
            modelRoot.position   = Vector3.Lerp(_catchUpFromPos,   _lastAdminPos,   eased);
            modelRoot.rotation   = Quaternion.Slerp(_catchUpFromRot, _lastAdminRot,  eased);
            modelRoot.localScale = Vector3.Lerp(_catchUpFromScale, _lastAdminScale, eased);
            return true;
        }

        void Update()
        {
            if (modelRoot == null) return;

            int touchCount = InputCompat.TouchCount;
            UpdateTapDetection(touchCount);
            if (touchCount < 2) _twoFingerTracking = false;

            if (touchCount > 0)
            {
                if (touchCount == 1 && !_wasTwoFinger)
                {
                    HandleSingleTouch();
                }
                else if (touchCount >= 2)
                {
                    _wasTwoFinger = true;
                    HandleTwoFingerTouch();
                }
            }
            else
            {
                _wasTwoFinger = false;
                HandleMouseInput();
            }
        }

        /// <summary>
        /// Mirroring runs in LateUpdate, after all input handling and after the presenter's packets
        /// have been applied this frame. Keeping it separate from Update also means a viewer keeps
        /// following even if anything in the input path misbehaves.
        /// </summary>
        void LateUpdate()
        {
            if (modelRoot == null) return;

            if (IsSyncLocked)
            {
                if (_catchingUp && _hasAdminTransform && ApplyCatchUp()) return;

                if (_hasAdminTransform)
                {
                    float speed = Time.deltaTime * remoteInterpolationSpeed;
                    modelRoot.position   = Vector3.Lerp(modelRoot.position, _lastAdminPos, speed);
                    modelRoot.rotation   = Quaternion.Slerp(modelRoot.rotation, _lastAdminRot, speed);
                    modelRoot.localScale = Vector3.Lerp(modelRoot.localScale, _lastAdminScale, speed);
                }
            }
            else
            {
                ApplySmoothedRotation();
            }
        }

        // A gesture only becomes a drag once it has moved this far. Below that it is a tap or a
        // "point at", and it must not break a viewer's follow lock. Gestures that start on a
        // button are ignored entirely.
        private const float DragStartPixels = 24f;
        private Vector2 _gestureStart;
        private bool    _gestureIsDrag;
        private bool    _gestureOnUI;

        private float   _pinchStartDistance;
        private Vector2 _pinchStartMid;
        private bool    _pinchActive;

        /// <summary>
        /// Lets a game mode take a gesture for itself: called as each one-finger (or left-mouse)
        /// gesture starts, and returning true means "this one is mine" - the viewer then leaves the
        /// model alone until the finger lifts, as it does for a gesture that starts on a button.
        /// The reassembly challenge uses it so dragging a loose part moves the part, not the model.
        /// </summary>
        public static Func<Vector2, bool> GestureClaimer;

        private void BeginGesture(Vector2 pos)
        {
            _gestureStart  = pos;
            _gestureIsDrag = false;
            _gestureOnUI   = IsPointerOverUI(pos) || (GestureClaimer != null && GestureClaimer(pos));
        }

        /// <summary>
        /// Returns true once the gesture has travelled far enough to count as a drag. The first
        /// time it does, a following viewer leaves follow mode.
        /// </summary>
        private bool PassedDragThreshold(Vector2 pos)
        {
            if (_gestureIsDrag) return true;
            if ((pos - _gestureStart).sqrMagnitude < DragStartPixels * DragStartPixels) return false;

            _gestureIsDrag = true;
            if (IsSyncLocked) BreakSync();
            return true;
        }

        /// <summary>
        /// Turns the model about its centre (the anchor: SpaceSceneManager centres the content on it)
        /// so the surface under the finger follows the finger: drag right and the front turns right,
        /// drag up and it turns up, drag on a slant and it turns on that slant. Both directions use
        /// the same scale (degrees per screen width), so a diagonal drag turns diagonally.
        /// </summary>
        private void RotateBy(Vector2 delta)
        {
            float degreesPerPixel = rotationSpeed * 1800f / Mathf.Max(1, Screen.width);
            Vector2 d = delta * degreesPerPixel;
            float angle = d.magnitude;
            if (angle < 1e-4f) return;

            Camera cam = Camera.main;
            Vector3 up    = cam != null ? cam.transform.up    : Vector3.up;
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 axis  = (up * -d.x + right * d.y).normalized;
            _targetRot = Quaternion.AngleAxis(angle, axis) * _targetRot;
        }

        /// <summary>Moves the model so the point under the fingers stays under them.</summary>
        private void PanBy(Vector2 from, Vector2 to)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                modelRoot.position += new Vector3((to.x - from.x) / Screen.width, (to.y - from.y) / Screen.height, 0f) * panSpeed * 300f;
                return;
            }
            float depth = Vector3.Dot(modelRoot.position - cam.transform.position, cam.transform.forward);
            if (depth < 0.05f) depth = 0.05f;
            Vector3 a = cam.ScreenToWorldPoint(new Vector3(from.x, from.y, depth));
            Vector3 b = cam.ScreenToWorldPoint(new Vector3(to.x, to.y, depth));
            modelRoot.position += b - a;
        }

        private void HandleSingleTouch()
        {
            Vector2 pos = InputCompat.GetTouchPosition(0);

            if (InputCompat.IsTouchBegan(0))
            {
                BeginGesture(pos);
                _lastSinglePos = pos;
                return;
            }

            if (_gestureOnUI) return;

            if (!_gestureIsDrag)
            {
                if (!PassedDragThreshold(pos)) return;
                _lastSinglePos = pos; // rotate from here, so the dead zone does not cause a jump
                return;
            }

            Vector2 delta = pos - _lastSinglePos;
            _lastSinglePos = pos;
            if (delta.sqrMagnitude > 0.01f) RotateBy(delta);
        }

        private void ApplySmoothedRotation()
        {
            _currentRot = Quaternion.Slerp(_currentRot, _targetRot, Time.deltaTime * rotationSmoothing);
            modelRoot.rotation = _currentRot;
        }

        private void HandleMouseInput()
        {
            if (!InputCompat.HasMouse) return; // phones: touches are handled above

            Vector2 mouse = InputCompat.MousePosition;

            float scroll = InputCompat.ScrollDelta;
            if (Mathf.Abs(scroll) > 0.08f && !IsPointerOverUI(mouse))
            {
                if (IsSyncLocked) BreakSync();
                float currentScale = modelRoot.localScale.x;
                float newScale = ClampScale(currentScale * (1f + scroll * zoomSpeed * 10f));
                modelRoot.localScale = Vector3.one * newScale;
            }

            if (InputCompat.GetMouseButtonDown(0) || InputCompat.GetMouseButtonDown(1) || InputCompat.GetMouseButtonDown(2))
            {
                BeginGesture(mouse);
                _lastMousePos = mouse;
                return;
            }

            bool panning  = InputCompat.GetMouseButton(1) || InputCompat.GetMouseButton(2);
            bool rotating = InputCompat.GetMouseButton(0) && !panning;
            if (!rotating && !panning) return;
            if (_gestureOnUI) return;

            if (!_gestureIsDrag)
            {
                if (!PassedDragThreshold(mouse)) return;
                _lastMousePos = mouse;
                return;
            }

            Vector2 delta = mouse - _lastMousePos;
            _lastMousePos = mouse;
            if (delta.sqrMagnitude < 0.01f) return;

            if (rotating)
            {
                RotateBy(delta);
            }
            else
            {
                PanBy(mouse - delta, mouse);
            }
        }

        private void HandleTwoFingerTouch()
        {
            Vector2 pos0 = InputCompat.GetTouchPosition(0);
            Vector2 pos1 = InputCompat.GetTouchPosition(1);
            float   distance = Vector2.Distance(pos0, pos1);
            Vector2 midpoint = (pos0 + pos1) * 0.5f;

            // A fresh two-finger gesture: either finger just landed, or two fingers are here for the
            // first time. Both can land in the same frame, and then the one-finger code never ran, so
            // the "started on a button?" answer would have been the previous gesture's - which made
            // two-finger moves fail at random until tried again.
            if (!_twoFingerTracking || InputCompat.IsTouchBegan(0) || InputCompat.IsTouchBegan(1))
            {
                _twoFingerTracking     = true;
                _gestureOnUI           = IsPointerOverUI(pos0) || IsPointerOverUI(pos1);
                _lastPinchDistance     = distance;
                _lastTwoFingerMidpoint = midpoint;
                _pinchStartDistance    = distance;
                _pinchStartMid         = midpoint;
                _pinchActive           = false;
                return;
            }

            if (_gestureOnUI) return;

            if (!_pinchActive)
            {
                bool pinched = Mathf.Abs(distance - _pinchStartDistance) >= DragStartPixels;
                bool panned  = (midpoint - _pinchStartMid).sqrMagnitude >= DragStartPixels * DragStartPixels;
                if (!pinched && !panned) return;

                _pinchActive = true;
                _lastPinchDistance     = distance;
                _lastTwoFingerMidpoint = midpoint;
                if (IsSyncLocked) BreakSync();
                return;
            }

            // Zoom by the ratio of finger spread: the model grows exactly as the fingers part,
            // whatever size it started at.
            if (_lastPinchDistance > 1f && distance > 1f)
                modelRoot.localScale = Vector3.one * ClampScale(modelRoot.localScale.x * (distance / _lastPinchDistance));
            _lastPinchDistance = distance;

            // Move so the point between the fingers stays between them.
            PanBy(_lastTwoFingerMidpoint, midpoint);
            _lastTwoFingerMidpoint = midpoint;
        }

        // ── Tap detection & pointer position ───────────────────────────────────

        private void UpdateTapDetection(int touchCount)
        {
            bool down;
            Vector2 pos;
            if (touchCount > 0) { down = true; pos = InputCompat.GetTouchPosition(0); }
            else                { down = InputCompat.GetMouseButton(0); pos = InputCompat.MousePosition; }

            if (down && !_pointerDown)
            {
                _pointerDown  = true;
                // Judged when the finger goes down, not only when it lifts: a button that closes
                // its own panel on click (the "show to everyone?" ACCEPT) is gone by the time the
                // finger lifts, and the tap used to fall through to the model - clearing the
                // presenter's selection, which undid the isolation they had just shared.
                _tapPossible  = touchCount < 2 && !IsPointerOverUI(pos);
                _tapStart     = pos;
                _tapStartTime = Time.unscaledTime;
            }
            else if (down)
            {
                if (touchCount >= 2 || (pos - _tapStart).sqrMagnitude > TapMaxMovePixels * TapMaxMovePixels)
                    _tapPossible = false;
            }
            else if (_pointerDown)
            {
                _pointerDown = false;
                bool isTap = _tapPossible && Time.unscaledTime - _tapStartTime <= TapMaxSeconds;
                _tapPossible = false;

                if (isTap && !IsPointerOverUI(_tapStart)) OnTap?.Invoke(_tapStart);
            }
        }

        /// <summary>True if the screen position is over a UI control or a touch blocker.</summary>
        public static bool IsPointerOverUI(Vector2 screenPos)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;

            var data = new UnityEngine.EventSystems.PointerEventData(es) { position = screenPos };
            _uiHits.Clear();
            es.RaycastAll(data, _uiHits);

            // Decorative panels (backgrounds, labels) must not swallow model touches, so only
            // controls and explicit blockers (for example an open modal) count.
            foreach (var hit in _uiHits)
            {
                GameObject go = hit.gameObject;
                if (go == null) continue;
                if (go.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
                if (go.GetComponentInParent<UnityEngine.UI.ScrollRect>() != null) return true;
                if (go.GetComponentInParent<AuraSync.UI.TouchBlocker>() != null) return true;
            }
            return false;
        }

        /// <summary>Where a single finger (or the pressed mouse button) currently is, if any.</summary>
        public bool TryGetPointerScreenPos(out Vector2 pos) => InputCompat.TryGetPointer(out pos);
    }
}
