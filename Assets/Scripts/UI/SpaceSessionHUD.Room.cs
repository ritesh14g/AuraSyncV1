using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// ROOM in the top-right corner: everything the old top bar, Share button and room panel did,
    /// in one card that drops down under the button.
    ///
    ///   Not in a room:  your name (if not chosen yet), CREATE ROOM, and JOIN WITH A CODE (the same
    ///                   4-box keypad as onboarding; no phone keyboard).
    ///   In a room:      the code and INVITE (copies the link); everyone in the room with their
    ///                   role, controls held, mic and speaking state (the presenter taps a person
    ///                   for their controls); MIC and HEAR switches; "see where others look"
    ///                   (and, for the presenter, "show cones to everyone"); LEAVE ROOM.
    ///
    /// In a room the button shows the code, the mic button appears beside it, and the other people
    /// show as coloured dots under it.
    /// </summary>
    public partial class SpaceSessionHUD
    {
        private const string RoomPanelId = "room";
        private const string MicId = "mic";

        private Slot _roomSlot, _micSlot;
        private RectTransform _avatarRow;
        private AuraSlidePanel _roomCard;
        private RectTransform _roomBody;
        private TextMeshProUGUI _roomStatus;
        private NetworkManager _boundNetwork;
        private VoiceChatManager _boundVoice;
        private bool _joinMode, _busy;
        private float _nextRoomTick;
        private bool _wasInRoom;

        private class Person
        {
            public int actor;
            public Image dot, ring;
            public TextMeshProUGUI status;
        }
        private readonly List<Person> _avatars = new List<Person>();
        private readonly List<Person> _rows = new List<Person>();

        // ── Build ──────────────────────────────────────────────────────

        private void BuildRoom()
        {
            _roomSlot = MakeTopButton(_rightRow, RoomPanelId, "Room", AuraIcons.Room, ToggleRoomCard, SlotKind.TopRight);
            _slots[RoomPanelId] = _roomSlot;
            _micSlot = MakeTopButton(_rightRow, MicId, null, AuraIcons.Mic, OnMicToggle, SlotKind.TopRight);
            _slots[MicId] = _micSlot;
            SetSlotVisible(MicId, false);
            _micSlot.visible = false;
            _micSlot.rect.gameObject.SetActive(false);

            _avatarRow = AuraKit.Rect(_chrome, "People");
            AuraKit.Pin(_avatarRow, new Vector2(1, 1), new Vector2(1, 1),
                        new Vector2(-AuraViewer.Gutter - 8f, -(AuraViewer.Gutter + AuraViewer.TopButton + 14f)),
                        new Vector2(600, 56));
            var h = AuraKit.HStack(_avatarRow, -10f, 0, TextAnchor.MiddleRight);
            h.reverseArrangement = true;

            // The card: under the top row, right-aligned, on its own nested canvas.
            var card = AuraKit.Box(_chrome, "RoomCard", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1, 1);
            crt.pivot = new Vector2(1, 1);
            crt.sizeDelta = new Vector2(920, 600);
            var top = new Vector2(-AuraViewer.Gutter, -(AuraViewer.Gutter + AuraViewer.TopButton + 20f));
            crt.anchoredPosition = top;
            var v = AuraKit.VStack(card, 22f);
            v.padding = new RectOffset(40, 40, 40, 40);
            AuraKit.FitHeight(card);
            _roomBody = crt;
            _roomCard = AuraSlidePanel.Attach(crt, top, top + new Vector2(0, 50f));
        }

        private void StartRoom()
        {
            _boundNetwork = NetworkManager.Instance;
            if (_boundNetwork != null)
            {
                _boundNetwork.OnRoomJoinedSuccess += OnRoomJoined;
                _boundNetwork.OnRoomJoinFailedError += OnRoomError;
                _boundNetwork.OnPlayerListUpdated += OnPlayersChanged;
            }
            _boundVoice = VoiceChatManager.Instance;
            if (_boundVoice != null)
            {
                _boundVoice.OnMicMuteStateChanged += OnMicState;
                _boundVoice.OnIncomingMuteChanged += OnHearState;
            }
            AttentionSync.OnConesToggled += OnConesToggled;
        }

        private void StopRoom()
        {
            if (_boundNetwork != null)
            {
                _boundNetwork.OnRoomJoinedSuccess -= OnRoomJoined;
                _boundNetwork.OnRoomJoinFailedError -= OnRoomError;
                _boundNetwork.OnPlayerListUpdated -= OnPlayersChanged;
            }
            if (_boundVoice != null)
            {
                _boundVoice.OnMicMuteStateChanged -= OnMicState;
                _boundVoice.OnIncomingMuteChanged -= OnHearState;
            }
            AttentionSync.OnConesToggled -= OnConesToggled;
            PanelNavigator.Remove(RoomPanelId);
        }

        // ── Events ─────────────────────────────────────────────────────

        private void OnRoomJoined(string code)
        {
            _busy = false;
            _joinMode = false;

            // The presenter starts broadcasting and shares the model as soon as they are in the room.
            bool isAdmin = ZoneManager.Instance != null && ZoneManager.Instance.IsAdmin;
            if (isAdmin && _modelLoaded)
            {
                AdminBroadcastManager.Instance?.StartBroadcasting();
                if (!string.IsNullOrEmpty(_currentModelPath))
                    ModelSyncManager.Instance?.BroadcastModelChange(_currentModelPath,
                        System.IO.Path.GetFileNameWithoutExtension(_currentModelPath));
            }
            RefreshAll();
        }

        private void OnRoomError(string message)
        {
            _busy = false;
            if (_roomStatus != null) { _roomStatus.text = Friendly(message); _roomStatus.color = AuraTheme.Rose; }
            if (_roomCard != null && _roomCard.IsShown && !PhotonNetwork.InRoom) RebuildRoomCardKeepStatus();
            RefreshAll();
        }

        private void OnPlayersChanged() => RefreshAll();
        private void OnMicState(bool muted)
        {
            RefreshMic();
            if (_roomCard != null && _roomCard.IsShown && PhotonNetwork.InRoom) RebuildRoomCard();
        }
        private void OnHearState(bool muted) { if (_roomCard != null && _roomCard.IsShown) RebuildRoomCard(); }
        private void OnConesToggled() { if (_roomCard != null && _roomCard.IsShown && PhotonNetwork.InRoom) RebuildRoomCard(); }

        private static string Friendly(string message)
        {
            if (string.IsNullOrEmpty(message)) return "Something went wrong. Try again.";
            string m = message.ToLowerInvariant();
            if (m.Contains("not found") || m.Contains("exist")) return "No room with that code. Check it with your presenter.";
            if (m.Contains("full")) return $"That room is full ({NetworkManager.MaxPlayers} people).";
            if (m.Contains("disconnect")) return "Lost the connection. Check your internet and try again.";
            return message;
        }

        // ── Refresh ────────────────────────────────────────────────────

        private void RefreshRoom()
        {
            bool inRoom = PhotonNetwork.InRoom;

            SetSlotLabel(RoomPanelId, inRoom ? ShortCode(PhotonNetwork.CurrentRoom.Name) : "Room");
            SetSlotVisible(MicId, inRoom);
            if (inRoom && _micSlot != null) _micSlot.rect.SetAsLastSibling(); // leftmost (reverse row)
            RefreshMic();
            RebuildAvatars();

            if (_roomCard != null && _roomCard.IsShown && inRoom != _wasInRoom) RebuildRoomCard();
            _wasInRoom = inRoom;
        }

        private static string ShortCode(string code) =>
            string.IsNullOrEmpty(code) ? "Room" : code.StartsWith("AURA-") ? code.Substring(5) : code;

        private void RefreshMic()
        {
            var voice = VoiceChatManager.Instance;
            bool noMic = voice != null && !voice.IsMicAvailable;
            bool muted = voice == null || voice.IsMuted || noMic;
            SetSlotIcon(MicId, muted ? AuraIcons.MicOff : AuraIcons.Mic);
            SetSlotTint(MicId, muted ? (Color?)AuraTheme.A(AuraTheme.Rose, 0.85f) : null);
        }

        /// <summary>Speaking rings and status lines, a few times a second.</summary>
        private void UpdateRoom()
        {
            if (!PhotonNetwork.InRoom || Time.unscaledTime < _nextRoomTick) return;
            _nextRoomTick = Time.unscaledTime + 0.15f;
            var voice = VoiceChatManager.Instance;
            foreach (var p in _avatars)
            {
                if (p.ring == null) continue;
                Player pl = PhotonNetwork.CurrentRoom.GetPlayer(p.actor);
                bool speaking = voice != null && !VoiceChatManager.IsPlayerMuted(pl) && voice.IsSpeaking(p.actor);
                p.ring.enabled = speaking;
            }
            foreach (var p in _rows) if (p.status != null) p.status.text = StatusFor(p.actor);
        }

        private void RebuildAvatars()
        {
            AuraKit.ClearChildren(_avatarRow);
            _avatars.Clear();
            if (!PhotonNetwork.InRoom) return;

            int me = PhotonNetwork.LocalPlayer.ActorNumber;
            foreach (var pl in PhotonNetwork.PlayerList)
            {
                if (pl.ActorNumber == me) continue;
                int actor = pl.ActorNumber;
                var holder = AuraKit.Rect(_avatarRow, "Person");
                AuraKit.Size(holder, 56f, 56f);
                var ring = holder.gameObject.AddComponent<Image>();
                ring.sprite = AuraSprites.DiscRing;
                ring.color = AuraTheme.Lavender;
                ring.enabled = false;
                var dot = AuraKit.Rect(holder, "Dot").gameObject.AddComponent<Image>();
                dot.sprite = AuraSprites.Disc;
                dot.color = UserPresence.ColorForActor(actor);
                AuraKit.Pin(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 44));
                string name = NetworkManager.DisplayName(pl);
                var initial = AuraKit.Text(dot.transform, name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?",
                                           24f, AuraTheme.Ink, AuraWeight.Bold, TextAlignmentOptions.Center);
                AuraKit.Fill(initial.rectTransform);
                AuraKit.MakeButton(holder.gameObject, dot, () => OnPersonPressed(actor));
                _avatars.Add(new Person { actor = actor, dot = dot, ring = ring });
            }
        }

        // ── The card ───────────────────────────────────────────────────

        private void ToggleRoomCard()
        {
            if (_roomCard.IsShown) CloseRoomCard();
            else OpenRoomCard();
        }

        private void OpenRoomCard()
        {
            _joinMode = false;
            RebuildRoomCard();
            _roomCard.Show();
            ViewerPanels.Opened(RoomPanelId, CloseRoomCard);
            PanelNavigator.Push(RoomPanelId, this, CloseRoomCard);
        }

        private void CloseRoomCard()
        {
            if (_roomCard == null || !_roomCard.IsShown) return;
            _roomCard.Hide();
            ViewerPanels.Closed(RoomPanelId);
            PanelNavigator.Remove(RoomPanelId);
        }

        private void RebuildRoomCardKeepStatus()
        {
            string s = _roomStatus != null ? _roomStatus.text : "";
            Color c = _roomStatus != null ? _roomStatus.color : AuraTheme.TextDim;
            RebuildRoomCard();
            if (_roomStatus != null) { _roomStatus.text = s; _roomStatus.color = c; }
        }

        private void RebuildRoomCard()
        {
            AuraKit.ClearChildren(_roomBody, "Border");
            _rows.Clear();
            _roomStatus = null;
            if (PhotonNetwork.InRoom) BuildInRoom();
            else BuildOutOfRoom();
        }

        private void BuildOutOfRoom()
        {
            var head = AuraKit.Rect(_roomBody, "Head");
            AuraKit.VStack(head, 6f);
            AuraKit.Text(head, "Room", AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold);
            AuraKit.Text(head, "Study this model together, live. Up to " + NetworkManager.MaxPlayers + " people.",
                         AuraTheme.SizeSmall, AuraTheme.TextDim);

            var net = NetworkManager.EnsureExists();
            TMP_InputField nameInput = null;
            if (PlayerPrefs.GetInt(OnboardingFlow.NameConfirmedKey, 0) == 0)
            {
                AuraKit.Caps(_roomBody, "Your name");
                string current = net.GetPlayerName() ?? "";
                bool generated = current.StartsWith("User") && current.Length <= 7;
                nameInput = AuraWidgets.Input(_roomBody, "What others will see", generated ? "" : current, false, 130f, 16);
            }

            bool NameOk()
            {
                if (nameInput == null) return true;
                if (string.IsNullOrWhiteSpace(nameInput.text))
                {
                    if (_roomStatus != null) { _roomStatus.text = "Type your name first."; _roomStatus.color = AuraTheme.Rose; }
                    return false;
                }
                net.SetPlayerName(nameInput.text);
                PlayerPrefs.SetInt(OnboardingFlow.NameConfirmedKey, 1);
                PlayerPrefs.Save();
                return true;
            }

            if (!_joinMode)
            {
                AuraKit.Pill(_roomBody, "Create room", AuraIcons.Plus, AuraTheme.Lavender, AuraTheme.Ink, () =>
                {
                    if (_busy || !NameOk()) return;
                    _busy = true;
                    SetRoomStatus(PhotonNetwork.IsConnectedAndReady ? "Creating your room…" : "Connecting…", AuraTheme.TextDim);
                    net.CreateRoom();
                }, AuraTheme.MinTouch + 10f);
                AuraKit.Pill(_roomBody, "Join with a code", AuraIcons.Join, AuraTheme.Ink, AuraTheme.Lavender, () =>
                {
                    if (_busy || !NameOk()) return;
                    _joinMode = true;
                    RebuildRoomCard();
                }, AuraTheme.MinTouch + 10f, AuraTheme.Slate);
            }
            else
            {
                var pad = AuraCodePad.Build(_roomBody, 92f);
                var row = AuraKit.EqualRow(_roomBody, AuraTheme.MinTouch, 18f);
                var back = AuraKit.Pill(row, "Back", AuraIcons.Back, AuraTheme.Ink, AuraTheme.Text, () =>
                {
                    _joinMode = false;
                    RebuildRoomCard();
                }, AuraTheme.MinTouch, AuraTheme.Slate);
                AuraKit.Cell(back);
                Button join = null;
                join = AuraKit.Pill(row, "Join", AuraIcons.Join, AuraTheme.Lavender, AuraTheme.Ink, () =>
                {
                    if (_busy || !pad.IsComplete) return;
                    _busy = true;
                    pad.SetInteractable(false);
                    SetRoomStatus(PhotonNetwork.IsConnectedAndReady ? $"Joining AURA-{pad.Code}…" : "Connecting…", AuraTheme.TextDim);
                    net.JoinRoom("AURA-" + pad.Code);
                }, AuraTheme.MinTouch);
                AuraKit.Cell(join);
                join.interactable = false;
                pad.OnChanged += code => join.interactable = code.Length == 4;
                pad.OnMessage += msg => SetRoomStatus(msg, AuraTheme.Rose);
            }

            _roomStatus = AuraKit.Text(_roomBody, _joinMode ? "Ask your presenter for the 4-character code." : "",
                                       AuraTheme.SizeSmall, AuraTheme.TextDim, AuraWeight.Regular, TextAlignmentOptions.Center);
        }

        private void BuildInRoom()
        {
            var room = PhotonNetwork.CurrentRoom;
            bool isPresenter = NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;

            // Code and invite.
            var head = AuraKit.Rect(_roomBody, "Head");
            AuraKit.HStack(head, 20f, 0, TextAnchor.MiddleLeft).childForceExpandHeight = false;
            var words = AuraKit.Rect(head, "Code");
            AuraKit.VStack(words, 2f);
            AuraKit.Flex(words);
            AuraKit.Caps(words, "Room code");
            var code = AuraKit.Text(words, room.Name, 56f, AuraTheme.Text, AuraWeight.Bold);
            code.enableWordWrapping = false;
            code.characterSpacing = 4f;
            Button invite = null;
            invite = AuraKit.Pill(head, "Invite", AuraIcons.Link, AuraTheme.Ink, AuraTheme.Lavender, () =>
            {
                RoomLinkManager.CopyInviteLink(room.Name);
                var label = invite.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = "Copied";
                Toast.Show("Invite link copied. Paste it to anyone you want in the room.");
            }, AuraTheme.MinTouch, AuraTheme.Slate);
            invite.GetComponent<LayoutElement>().preferredWidth = 250f;

            // People.
            AuraKit.Caps(_roomBody, $"In this room · {room.PlayerCount}/{NetworkManager.MaxPlayers}");
            int me = PhotonNetwork.LocalPlayer.ActorNumber;
            int admin = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            foreach (var pl in PhotonNetwork.PlayerList)
            {
                int actor = pl.ActorNumber;
                var rowBox = AuraKit.Box(_roomBody, "Person", AuraTheme.Ink, 24f, true);
                AuraKit.Size(rowBox, -1, 104f);
                var rh = AuraKit.HStack(rowBox, 18f, 0, TextAnchor.MiddleLeft);
                rh.padding = new RectOffset(26, 26, 0, 0);

                var dot = AuraKit.Rect(rowBox.transform, "Dot").gameObject.AddComponent<Image>();
                dot.sprite = AuraSprites.Disc;
                dot.color = UserPresence.ColorForActor(actor);
                AuraKit.Size(dot, 30f, 30f);

                string name = NetworkManager.DisplayName(pl) + (actor == me ? " (you)" : "");
                var nt = AuraKit.Text(rowBox.transform, name, AuraTheme.SizeBody - 2f, AuraTheme.Text, AuraWeight.SemiBold);
                nt.enableWordWrapping = false;
                AuraKit.Flex(nt);
                var st = AuraKit.Text(rowBox.transform, StatusFor(actor), AuraTheme.SizeCaption, AuraTheme.TextDim,
                                      AuraWeight.Regular, TextAlignmentOptions.MidlineRight);
                st.richText = true;
                st.enableWordWrapping = false;
                AuraKit.Size(st, 330f);
                if (actor == admin) AuraKit.Icon(rowBox.transform, AuraIcons.Crown, 40f, AuraTheme.Lavender);

                _rows.Add(new Person { actor = actor, status = st });
                if (isPresenter && actor != me) AuraKit.MakeButton(rowBox.gameObject, rowBox, () => OnPersonPressed(actor));
            }
            if (isPresenter && room.PlayerCount > 1)
                AuraKit.Text(_roomBody, "Tap a person to hand them a control or make them presenter.",
                             AuraTheme.SizeCaption, AuraTheme.TextDim);

            // Voice.
            var voice = VoiceChatManager.Instance;
            bool noMic = voice != null && !voice.IsMicAvailable;
            bool micOn = voice != null && !voice.IsMuted && !noMic;
            bool hearOn = !VoiceChatManager.IncomingMuted;
            var vrow = AuraKit.EqualRow(_roomBody, AuraTheme.MinTouch, 18f);
            Toggle(vrow, noMic ? "No mic" : micOn ? "Mic on" : "Mic off", micOn ? AuraIcons.Mic : AuraIcons.MicOff, micOn, OnMicToggle);
            Toggle(vrow, hearOn ? "Hearing others" : "Others muted", hearOn ? AuraIcons.Ear : AuraIcons.EarOff, hearOn,
                   () => VoiceChatManager.Instance?.ToggleIncomingMute());

            // Where others look.
            bool cones = !AttentionSync.ConesHidden;
            var crow = AuraKit.EqualRow(_roomBody, AuraTheme.MinTouch, 18f);
            Toggle(crow, "See where others look", cones ? AuraIcons.Eye : AuraIcons.EyeOff, cones,
                   () => AttentionSync.SetConesHidden(!AttentionSync.ConesHidden));
            if (isPresenter)
            {
                bool forRoom = !AttentionSync.ConesOffForRoom;
                Toggle(crow, "Show to everyone", AuraIcons.Room, forRoom,
                       () => AttentionSync.SetConesForRoom(AttentionSync.ConesOffForRoom));
            }

            AuraKit.Pill(_roomBody, "Leave room", AuraIcons.Back, AuraTheme.Ink, AuraTheme.Rose, LeaveRoom,
                         AuraTheme.MinTouch, AuraTheme.Rose);

            if (voice != null)
            {
                var diag = AuraKit.Text(_roomBody, voice.DiagnosticLine(), AuraTheme.SizeCaption - 4f, AuraTheme.A(AuraTheme.TextDim, 0.7f),
                                        AuraWeight.Regular, TextAlignmentOptions.Center);
                diag.enableWordWrapping = true;
            }
        }

        /// <summary>A two-state chip button: lavender outline when on, slate when off.</summary>
        private static void Toggle(RectTransform row, string label, string icon, bool on, System.Action onTap)
        {
            var b = AuraKit.Pill(row, label, icon, on ? AuraTheme.A(AuraTheme.Lavender, 0.18f) : AuraTheme.Ink,
                                 on ? AuraTheme.Lavender : AuraTheme.TextDim, onTap, AuraTheme.MinTouch,
                                 on ? AuraTheme.Lavender : AuraTheme.Slate);
            AuraKit.Cell(b);
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null)
            {
                t.enableAutoSizing = true;
                t.fontSizeMin = 20f;
                t.fontSizeMax = AuraTheme.SizeSmall;
            }
        }

        private void SetRoomStatus(string text, Color color)
        {
            if (_roomStatus == null) return;
            _roomStatus.text = text;
            _roomStatus.color = color;
        }

        /// <summary>"presenter · speaking", "Explode", "muted"...</summary>
        private static string StatusFor(int actor)
        {
            if (!PhotonNetwork.InRoom) return "";
            Player pl = PhotonNetwork.CurrentRoom.GetPlayer(actor);
            if (pl == null) return "";
            var parts = new List<string>();
            int admin = NetworkManager.Instance != null ? NetworkManager.Instance.AdminActorNumber : -1;
            if (actor == admin) parts.Add("presenter");
            var held = ControlOwnership.HeldBy(actor);
            if (held.Count == 1) parts.Add($"<color=#9894C4>{ControlOwnership.Label(held[0])}</color>");
            else if (held.Count > 1) parts.Add($"<color=#9894C4>{held.Count} controls</color>");
            var voice = VoiceChatManager.Instance;
            if (VoiceChatManager.IsPlayerMuted(pl)) parts.Add("muted");
            else if (voice != null && voice.IsSpeaking(actor)) parts.Add("<color=#9894C4>speaking</color>");
            return string.Join(" · ", parts);
        }

        private void OnPersonPressed(int actor)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsLocalAdmin) return;
            if (actor == NetworkManager.Instance.AdminActorNumber) return;
            PlayerControlMenu.Open(actor);
        }

        private void OnMicToggle() => VoiceChatManager.Instance?.ToggleMute();

        private void LeaveRoom()
        {
            AdminBroadcastManager.Instance?.StopBroadcasting();
            if (NetworkManager.Instance != null) NetworkManager.Instance.LeaveRoom();
            else if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            CloseRoomCard();
            Toast.Show("You left the room.");
        }
    }
}
