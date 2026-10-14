using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using AuraSync.Networking;

namespace AuraSync.Space
{
    /// <summary>
    /// Live "who is pointing where" for the 3D Space scene (Figma / Canva style).
    ///
    /// While a player touches the model, the point on the model surface under their finger is
    /// sent at 10 Hz as a position in the MODEL's local space. Every other device converts it back
    /// through its own copy of the model, so the pointer lands on the same spot of the model even
    /// if each viewer has rotated or zoomed it differently. Each remote player is drawn as a
    /// coloured dot with a name pill in a screen-space overlay.
    /// </summary>
    public class CanvaPointerManager : MonoBehaviour, IOnEventCallback
    {
        public static CanvaPointerManager Instance { get; private set; }

        public const byte EventCodePointer = EventCodes.Pointer;

        [Tooltip("Seconds between position updates while the finger moves (the first touch is sent at once).")]
        [SerializeField] private float sendInterval = 1f / 15f;
        [Tooltip("A pointer is hidden if a 'touching' update is not refreshed for this long (lost release).")]
        [SerializeField] private float staleSeconds = 1.5f;
        [Tooltip("How long a pointer stays visible after the finger lifts, so quick taps are seen.")]
        [SerializeField] private float lingerSeconds = 1.0f;

        private class Pill
        {
            public RectTransform rect;
            public CanvasGroup   group;
            public TextMeshProUGUI label;
            public Vector3 localPoint;
            public bool    touching;
            public float   lastSeen;
            public float   releasedAt = -999f;
        }

        private Canvas _canvas;
        private readonly Dictionary<int, Pill> _pills = new Dictionary<int, Pill>();

        private float _sendTimer;
        private bool  _wasPointing;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
        }

        void OnEnable()  { PhotonNetwork.AddCallbackTarget(this); }
        void OnDisable() { PhotonNetwork.RemoveCallbackTarget(this); }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildCanvas()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90; // under the HUD (95) so buttons stay on top

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            // No GraphicRaycaster: pointer pills must never swallow touches.
        }

        // ── Send ────────────────────────────────────────────────────────────────

        void Update()
        {
            if (!PhotonNetwork.InRoom) { _wasPointing = false; return; }

            SendLocalPointer();
            UpdatePills();
        }

        private void SendLocalPointer()
        {
            _sendTimer += Time.unscaledDeltaTime;

            SpaceInteractionController sic = SpaceInteractionController.Instance;
            Camera cam = Camera.main;
            Transform root = sic != null ? sic.ModelRoot : null;

            bool touching = root != null && cam != null &&
                            sic.TryGetPointerScreenPos(out Vector2 screenPos) &&
                            !SpaceInteractionController.IsPointerOverUI(screenPos);

            if (!touching)
            {
                if (_wasPointing)
                {
                    // Finger lifted: tell everyone at once (reliably). They keep the pill briefly.
                    Send(new float[] { 0f, 0f, 0f, 0f }, reliable: true);
                    _wasPointing = false;
                }
                return;
            }

            // The first contact goes out immediately so short taps are never missed;
            // after that, updates are rate-limited.
            bool firstContact = !_wasPointing;
            if (!firstContact && _sendTimer < sendInterval) return;

            sic.TryGetPointerScreenPos(out Vector2 pos);
            if (!TryGetModelLocalPoint(cam, root, pos, out Vector3 local)) return;

            Send(new float[] { local.x, local.y, local.z, 1f }, reliable: firstContact);
            _wasPointing = true;
            _sendTimer = 0f;
        }

        /// <summary>
        /// The touched point in the model's local space. On the model it is the surface point;
        /// beside the model it is the point on a plane through the model's centre, facing the
        /// camera, so a pointer is shown for every touch, not only touches that land on a mesh.
        /// </summary>
        private static bool TryGetModelLocalPoint(Camera cam, Transform root, Vector2 screenPos, out Vector3 local)
        {
            local = Vector3.zero;
            Ray ray = cam.ScreenPointToRay(screenPos);

            if (ModelRaycaster.Raycast(ray, root, out _, out Vector3 worldPoint))
            {
                local = root.InverseTransformPoint(worldPoint);
                return true;
            }

            if (ModelRaycaster.TryGetBoundsCenter(root, out Vector3 center))
            {
                Plane plane = new Plane(-cam.transform.forward, center);
                if (plane.Raycast(ray, out float distance))
                {
                    local = root.InverseTransformPoint(ray.GetPoint(distance));
                    return true;
                }
            }
            return false;
        }

        private static void Send(float[] payload, bool reliable)
        {
            PhotonNetwork.RaiseEvent(
                EventCodePointer, payload,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);
        }

        // ── Receive ─────────────────────────────────────────────────────────────

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCodePointer) return;

            float[] data = photonEvent.CustomData as float[];
            if (data == null || data.Length < 4) return;

            int actor = photonEvent.Sender;
            if (PhotonNetwork.LocalPlayer != null && actor == PhotonNetwork.LocalPlayer.ActorNumber) return;

            if (!_pills.TryGetValue(actor, out Pill pill))
            {
                pill = CreatePill(actor);
                _pills[actor] = pill;
            }

            bool touching = data[3] > 0.5f;
            if (touching)
            {
                pill.localPoint = new Vector3(data[0], data[1], data[2]);
                pill.lastSeen   = Time.unscaledTime;
            }
            else if (pill.touching)
            {
                pill.releasedAt = Time.unscaledTime; // keep showing the last point briefly
            }
            pill.touching = touching;
        }

        // ── Pills ───────────────────────────────────────────────────────────────

        private void UpdatePills()
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Camera cam = Camera.main;

            List<int> gone = null;

            foreach (var kv in _pills)
            {
                Pill p = kv.Value;

                if (PhotonNetwork.CurrentRoom.GetPlayer(kv.Key) == null)
                {
                    (gone ?? (gone = new List<int>())).Add(kv.Key);
                    continue;
                }

                float now = Time.unscaledTime;
                bool active = p.touching
                    ? now - p.lastSeen < staleSeconds
                    : now - p.releasedAt < lingerSeconds;
                bool visible = active && root != null && cam != null;

                if (visible)
                {
                    Vector3 screen = cam.WorldToScreenPoint(root.TransformPoint(p.localPoint));
                    visible = screen.z > 0f; // in front of the camera
                    if (visible) p.rect.position = new Vector3(screen.x, screen.y, 0f);
                }

                // Appear almost instantly, fade out gently
                float target = visible ? 1f : 0f;
                float speed  = visible ? 20f : 4f;
                p.group.alpha = Mathf.MoveTowards(p.group.alpha, target, Time.unscaledDeltaTime * speed);
            }

            if (gone != null)
            {
                foreach (int actor in gone)
                {
                    if (_pills[actor].rect != null) Destroy(_pills[actor].rect.gameObject);
                    _pills.Remove(actor);
                }
            }
        }

        private Pill CreatePill(int actor)
        {
            Player player = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
            Color color = UserPresence.ColorForActor(actor);

            GameObject root = new GameObject($"Pointer_{actor}", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.pivot     = new Vector2(0f, 1f); // the dot hangs off the exact touch point
            rt.sizeDelta = new Vector2(0f, 0f);

            // Dot at the touch point
            GameObject dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(root.transform, false);
            RectTransform drt = dot.GetComponent<RectTransform>();
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(26f, 26f);
            var dotImg = dot.GetComponent<Image>();
            dotImg.color = color;
            dotImg.raycastTarget = false;

            // Name pill beside it
            GameObject pill = new GameObject("Pill", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            pill.transform.SetParent(root.transform, false);
            RectTransform prt = pill.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(14f, -10f);

            var bg = pill.GetComponent<Image>();
            bg.color = new Color(color.r, color.g, color.b, 0.92f);
            bg.raycastTarget = false;

            var layout = pill.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 6, 6);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = pill.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            GameObject textGo = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(pill.transform, false);
            var label = textGo.GetComponent<TextMeshProUGUI>();
            label.text = NetworkManager.DisplayName(player);
            label.fontSize = 24;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.raycastTarget = false;
            label.enableWordWrapping = false;

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            return new Pill { rect = rt, group = group, label = label, lastSeen = -999f };
        }
    }
}
