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

        private GameObject      _root;
        private TextMeshProUGUI _title;
        private Image           _titleDot;

        private class Row { public Image fill, border, check; public TextMeshProUGUI text; }
        private readonly Dictionary<Control, Row> _rows = new Dictionary<Control, Row>();
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
            _titleDot.color = UserPresence.ColorForActor(_actor);

            foreach (Control c in ControlOwnership.Active)
            {
                if (!_rows.TryGetValue(c, out Row row)) continue;
                int holder = ControlOwnership.HolderOf(c);
                bool given = holder == _actor;

                string note = "";
                if (!given && ControlOwnership.IsHandedOut(c))
                    note = $"  <size=80%><color=#A9A8CC>({ControlOwnership.HolderName(c)} has it)</color></size>";

                row.text.text = ControlOwnership.Label(c) + note;
                row.text.color = given ? AuraTheme.Text : AuraTheme.TextDim;
                row.fill.color = given ? AuraTheme.A(AuraTheme.Lavender, 0.18f) : AuraTheme.Ink;
                row.border.color = given ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Slate, 0.8f);
                row.check.enabled = given;
            }

            bool armed = _promoteArmedUntil > Time.unscaledTime;
            _promoteText.text = armed ? "Tap again to make presenter" : "Make presenter";
            _promoteText.color = armed ? AuraTheme.Ink : AuraTheme.Lavender;
            _promoteBg.color  = armed ? AuraTheme.Lavender : AuraTheme.Ink;
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
            var canvas = AuraKit.CreateCanvas(transform, "PlayerMenu", 103); // above the HUD and room card; below the share prompt (105)
            _root = canvas.gameObject;

            // Dimmer: a tap outside the card closes the menu, and never reaches the model.
            var dim = AuraKit.Box(canvas.transform, "Dim", AuraTheme.A(AuraTheme.Ink, 0.6f), 0f, true);
            AuraKit.Fill(dim.rectTransform);
            AuraKit.MakeButton(dim.gameObject, dim, Close).transition = Selectable.Transition.None;

            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();

            var card = AuraKit.Box(safe, "Card", AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.55f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            card.gameObject.AddComponent<MatchParentWidth>().max = 860f;
            var v = AuraKit.VStack(card, 16f);
            v.padding = new RectOffset(40, 40, 40, 40);
            AuraKit.FitHeight(card);

            var head = AuraKit.Rect(card.transform, "Head");
            AuraKit.HStack(head, 18f);
            AuraKit.Size(head, -1, 70f);
            _titleDot = AuraKit.Rect(head, "Dot").gameObject.AddComponent<Image>();
            _titleDot.sprite = AuraSprites.Disc;
            AuraKit.Size(_titleDot, 36f, 36f);
            _title = AuraKit.Text(head, "", AuraTheme.SizeTitle - 6f, AuraTheme.Text, AuraWeight.Bold);
            _title.enableWordWrapping = false;
            AuraKit.Flex(_title);

            AuraKit.Text(card.transform, "Tap a control to hand it to them: they drive it for everyone. Tap again to take it back.",
                         AuraTheme.SizeCaption, AuraTheme.TextDim);

            foreach (Control c in ControlOwnership.Active)
            {
                Control captured = c;
                var fill = AuraKit.Box(card.transform, "Row_" + ControlOwnership.Id(c), AuraTheme.Ink, AuraTheme.Radius - 8f, true);
                var border = AuraKit.Border(fill, AuraTheme.Slate, AuraTheme.Radius - 8f);
                AuraKit.Size(fill, -1, 110f);
                var h = AuraKit.HStack(fill, 18f);
                h.padding = new RectOffset(28, 28, 0, 0);
                var text = AuraKit.Text(fill.transform, "", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
                text.richText = true;
                text.enableWordWrapping = false;
                AuraKit.Flex(text);
                var check = AuraKit.Icon(fill.transform, AuraIcons.Ok, 48f, AuraTheme.Lavender);
                AuraKit.MakeButton(fill.gameObject, fill, () => OnRowPressed(captured));
                _rows[c] = new Row { fill = fill, border = border, check = check, text = text };
            }

            var promote = AuraKit.Pill(card.transform, "Make presenter", AuraIcons.Crown, AuraTheme.Ink, AuraTheme.Lavender,
                                       OnPromotePressed, AuraTheme.MinTouch, AuraTheme.Lavender);
            _promoteBg = promote.GetComponent<Image>();
            _promoteText = promote.GetComponentInChildren<TextMeshProUGUI>();

            var back = AuraKit.Pill(safe, "Back", AuraIcons.Back, AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Text,
                                    Close, AuraViewer.BackHeight, AuraTheme.Slate);
            AuraKit.Pin((RectTransform)back.transform, Vector2.zero, Vector2.zero,
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));
        }
    }
}
