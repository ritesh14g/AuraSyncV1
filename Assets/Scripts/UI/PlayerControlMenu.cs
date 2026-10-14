using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// What the presenter gets on tapping a player's badge (V3 Phase 1): tick the controls to hand
    /// that player (Explode, Isolate, Highlight), untick to take them back, or make them presenter.
    ///
    /// Making someone presenter asks for a second tap within 3 seconds, as the badge's double tap
    /// did before: a stray tap must not hand over the session. Ticks take effect at once (they are
    /// as easy to undo as to do).
    ///
    /// The menu follows the room live: a control changing hands elsewhere updates its tick, and the
    /// menu closes by itself if the player leaves or this device stops being the presenter.
    /// </summary>
    public class PlayerControlMenu : MonoBehaviour
    {
        private const string NavId = "player-menu";

        private static PlayerControlMenu _instance;

        private static readonly Color CardColor   = new Color(0.09f, 0.11f, 0.16f, 0.98f);
        private static readonly Color OnColor     = new Color(0.16f, 0.55f, 0.34f, 1f);
        private static readonly Color OffColor    = new Color(0.22f, 0.24f, 0.31f, 1f);
        private static readonly Color PromoteColor = new Color(0.62f, 0.45f, 0.08f, 1f);
        private static readonly Color ArmedColor  = new Color(0.85f, 0.65f, 0.10f, 1f);
        private static readonly Color CloseColor  = new Color(0.36f, 0.38f, 0.46f, 1f);

        private GameObject      _root;
        private TextMeshProUGUI _title;
        private readonly Dictionary<Control, (Image bg, TextMeshProUGUI text)> _rows =
            new Dictionary<Control, (Image, TextMeshProUGUI)>();
        private Image           _promoteBg;
        private TextMeshProUGUI _promoteText;

        private int   _actor = -1;
        private float _promoteArmedUntil;

        /// <summary>Opens the menu for a player. Only the presenter gets it, and not for themselves.</summary>
        public static void Open(int actor)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsLocalAdmin) return;
            if (!PhotonNetwork.InRoom || actor == PhotonNetwork.LocalPlayer.ActorNumber) return;
            if (PhotonNetwork.CurrentRoom.GetPlayer(actor) == null) return;

            if (_instance == null)
                _instance = new GameObject("AuraSync_PlayerControlMenu").AddComponent<PlayerControlMenu>();
            _instance.Show(actor);
        }

        public static void CloseIfOpen()
        {
            if (_instance != null) _instance.Close();
        }

        void Awake()
        {
            Build();
            _root.SetActive(false);
            ControlOwnership.OnChanged += Refresh;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnPlayerListUpdated += Refresh;
        }

        void OnDestroy()
        {
            ControlOwnership.OnChanged -= Refresh;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnPlayerListUpdated -= Refresh;
            PanelNavigator.Remove(NavId);
            if (_instance == this) _instance = null;
        }

        private void Show(int actor)
        {
            _actor = actor;
            _promoteArmedUntil = 0f;
            _root.SetActive(true);
            PanelNavigator.Push(NavId, this, Close);
            Refresh();
        }

        private void Close()
        {
            _actor = -1;
            if (_root != null) _root.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        void Update()
        {
            // The "tap again" state runs out on its own.
            if (_root != null && _root.activeSelf && _promoteArmedUntil > 0f && Time.unscaledTime >= _promoteArmedUntil)
            {
                _promoteArmedUntil = 0f;
                Refresh();
            }
        }

        private void Refresh()
        {
            if (_root == null || !_root.activeSelf || _actor < 0) return;

            Player p = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.GetPlayer(_actor) : null;
            bool presenter = NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;
            if (p == null || !presenter) { Close(); return; }

            string name = NetworkManager.DisplayName(p);
            _title.text = name;

            foreach (Control c in ControlOwnership.Active)
            {
                if (!_rows.TryGetValue(c, out var row)) continue;
                int holder = ControlOwnership.HolderOf(c);
                bool given = holder == _actor;

                string note = "";
                if (!given && ControlOwnership.IsHandedOut(c))
                    note = $"  <size=75%><color=#FFD133>({ControlOwnership.HolderName(c)} has it)</color></size>";

                row.text.text = (given ? "[x]  " : "[  ]  ") + ControlOwnership.Label(c) + note;
                row.bg.color  = given ? OnColor : OffColor;
            }

            bool armed = _promoteArmedUntil > Time.unscaledTime;
            _promoteText.text = armed ? "TAP AGAIN: MAKE PRESENTER" : "MAKE PRESENTER";
            _promoteBg.color  = armed ? ArmedColor : PromoteColor;
        }

        private void OnRowPressed(Control c)
        {
            if (_actor < 0) return;
            if (ControlOwnership.IsWritePending(c)) return; // the last tap is still on its way
            string label = ControlOwnership.Label(c);

            bool sent = ControlOwnership.HolderOf(c) == _actor
                ? ControlOwnership.TakeBack(c)
                : ControlOwnership.Grant(c, _actor, onRejected: () =>
                      Toast.Show($"{label} changed hands at the same moment. Try again."));

            if (!sent) Toast.Show($"Could not change {label} right now.");
        }

        private void OnPromotePressed()
        {
            if (_actor < 0) return;

            if (_promoteArmedUntil > Time.unscaledTime)
            {
                int actor = _actor;
                Close();
                NetworkManager.Instance?.PromoteToAdmin(actor);
                return;
            }

            _promoteArmedUntil = Time.unscaledTime + 3f;
            Refresh();
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 103; // above the HUD, drawer and room panel; below the share prompt (105)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Dimmer: a tap outside the card closes the menu, and never reaches the model.
            _root = new GameObject("Dimmer", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            Stretch((RectTransform)_root.transform);
            var dim = _root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.45f);
            _root.AddComponent<TouchBlocker>();
            var dimBtn = _root.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(_root.transform, false);
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(760f, 0f);
            card.AddComponent<Image>().color = CardColor;
            card.AddComponent<TouchBlocker>();
            card.AddComponent<Button>().transition = Selectable.Transition.None; // swallow taps on the card

            var vg = card.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(28, 28, 24, 24);
            vg.spacing = 12;
            vg.childControlWidth = true;
            vg.childControlHeight = true;
            vg.childForceExpandWidth = true;
            vg.childForceExpandHeight = false;
            var fit = card.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = MakeText(card.transform, "Title", 40f, FontStyles.Bold, 60f);

            var hint = MakeText(card.transform, "Hint", 24f, FontStyles.Normal, 34f);
            hint.text  = "Controls this player drives for everyone:";
            hint.color = new Color(1f, 1f, 1f, 0.6f);

            foreach (Control c in ControlOwnership.Active)
            {
                Control captured = c;
                var (bg, text) = MakeButton(card.transform, "Row_" + ControlOwnership.Id(c), OffColor,
                                            () => OnRowPressed(captured), TextAlignmentOptions.MidlineLeft);
                _rows[c] = (bg, text);
            }

            MakeText(card.transform, "Gap", 10f, FontStyles.Normal, 10f);

            (_promoteBg, _promoteText) = MakeButton(card.transform, "Promote", PromoteColor, OnPromotePressed,
                                                    TextAlignmentOptions.Center);
            var (_, closeText) = MakeButton(card.transform, "Close", CloseColor, Close, TextAlignmentOptions.Center);
            closeText.text = "CLOSE";
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size, FontStyles style, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize  = size;
            t.fontStyle = style;
            t.color     = Color.white;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            return t;
        }

        private static (Image, TextMeshProUGUI) MakeButton(Transform parent, string name, Color color,
                                                           UnityEngine.Events.UnityAction onClick,
                                                           TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 76f;

            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(22f, 0f);
            rt.offsetMax = new Vector2(-22f, 0f);

            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.fontSize  = 32f;
            t.fontStyle = FontStyles.Bold;
            t.color     = Color.white;
            t.alignment = align;
            t.enableAutoSizing = true;
            t.fontSizeMin = 20f;
            t.fontSizeMax = 32f;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return (img, t);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
