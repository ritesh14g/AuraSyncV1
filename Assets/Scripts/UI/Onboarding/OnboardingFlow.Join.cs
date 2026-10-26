using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// Join room: your name (asked once), then AURA- and four code boxes typed on a QWERTY-style
    /// keypad that has only the 32 characters a code can contain, so the phone's full keyboard never
    /// opens and an O/0 or I/1 mix-up is impossible. Tapping a box puts the cursor there, so one wrong
    /// character is fixed without deleting the rest (CodeSlots / CodeKeys in AuraCodePad.cs). Join unlocks when all four boxes are filled; joining a
    /// room whose presenter has a model open goes straight into it (ModelSyncManager loads it).
    /// </summary>
    public partial class OnboardingFlow
    {
        /// <summary>Set once the user has chosen the name others see (asked on first join).</summary>
        public const string NameConfirmedKey = "AuraSync_NameConfirmed";

        private enum JoinState { Name, Code, Joining, Waiting, InRoom }

        private View BuildJoin()
        {
            var v = NewView("join", Mode.Interactive, 1f);
            var col = PageColumn(v);
            var net = NetworkManager.EnsureExists();

            // Header, left of the chip.
            var title = AuraKit.Text(col, "Join a room", AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold);
            title.enableWordWrapping = false;
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(0, -AuraTheme.ChipSize);
            trt.offsetMax = Vector2.zero;

            float top = AuraTheme.ChipSize + 40f;

            // ── Name pane ──
            var namePane = AuraKit.Rect(col, "NamePane");
            AuraKit.Fill(namePane, 0, 0, 0, top);
            var nv = AuraKit.VStack(namePane, 22f);
            nv.childAlignment = TextAnchor.UpperLeft;
            AuraKit.Text(namePane, "What should others in the room call you?", AuraTheme.SizeBody, AuraTheme.Text,
                         AuraWeight.SemiBold);
            string current = net.GetPlayerName() ?? "";
            bool generated = current.StartsWith("User") && current.Length <= 7;
            var nameInput = AuraWidgets.Input(namePane, "Your name", generated ? "" : current, false, 150f, 16);
            AuraKit.Text(namePane, "Up to 16 letters. You can change it any time.", AuraTheme.SizeCaption, AuraTheme.TextDim);

            // ── Code pane ──
            var codePane = AuraKit.Rect(col, "CodePane");
            AuraKit.Fill(codePane, 0, 0, 0, top);

            var upper = AuraKit.Rect(codePane, "Upper");
            upper.anchorMin = new Vector2(0, 1); upper.anchorMax = new Vector2(1, 1); upper.pivot = new Vector2(0.5f, 1);
            upper.offsetMin = upper.offsetMax = Vector2.zero;
            AuraKit.VStack(upper, 30f);
            AuraKit.FitHeight(upper);

            // "Joining as Name · Change"
            var who = AuraKit.Rect(upper, "Who");
            AuraKit.HStack(who, 16f);
            AuraKit.Size(who, -1, AuraTheme.MinTouch);
            AuraKit.Icon(who, AuraIcons.User, 46f, AuraTheme.TextDim);
            var whoText = AuraKit.Text(who, "", AuraTheme.SizeSmall, AuraTheme.TextDim);
            whoText.richText = true;
            whoText.enableWordWrapping = false;
            AuraKit.Flex(whoText);
            var change = AuraKit.Pill(who, "Change", AuraIcons.Edit, AuraTheme.Ink, AuraTheme.Lavender, null,
                                      AuraTheme.MinTouch, AuraTheme.Slate);
            change.GetComponent<LayoutElement>().preferredWidth = 280f;

            // AURA- [ ][ ][ ][ ]
            var codeRow = AuraKit.Rect(upper, "Code");
            AuraKit.HStack(codeRow, 16f, 0, TextAnchor.MiddleCenter);
            AuraKit.Size(codeRow, -1, 190f);
            var pre = AuraKit.Text(codeRow, "AURA-", 58f, AuraTheme.TextDim, AuraWeight.SemiBold, TextAlignmentOptions.MidlineRight);
            pre.enableWordWrapping = false;
            AuraKit.Size(pre, 230f, 190f);
            var slots = CodeSlots.Build(codeRow, 150f, 186f, 74f, 26f);
            codeRow.gameObject.AddComponent<FitRowWidth>(); // shrinks the row on very narrow screens

            var status = AuraKit.Text(upper, "", AuraTheme.SizeSmall, AuraTheme.TextDim, AuraWeight.Regular,
                                      TextAlignmentOptions.Center);
            status.richText = true;
            AuraKit.Size(status, -1, 130f);

            // Keypad, pinned to the bottom of the page.
            const float keyH = 112f, keyGap = 12f;
            var pad = AuraKit.Box(codePane, "Keypad", AuraTheme.A(AuraTheme.Indigo, 0.55f), AuraTheme.Radius);
            var prt = pad.rectTransform;
            prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(1, 0); prt.pivot = new Vector2(0.5f, 0);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(0, 5 * keyH + 4 * keyGap + 2 * 16f);
            var pv = AuraKit.VStack(pad, keyGap, 16);
            pv.childForceExpandHeight = false;
            var keyButtons = new List<Button>();

            // ── State ──
            JoinState state = JoinState.Code;
            bool editingName = false;

            void SetStatus(string s, Color c) { status.text = s; status.color = c; }

            void Refresh()
            {
                bool showName = state == JoinState.Name;
                namePane.gameObject.SetActive(showName);
                codePane.gameObject.SetActive(!showName);

                whoText.text = $"Joining as <color=#ECEBFA><b>{net.GetPlayerName()}</b></color>";
                bool typing = state == JoinState.Code;
                if (slots.Enabled != typing) { slots.Enabled = typing; slots.Paint(); }
                foreach (var k in keyButtons) k.interactable = typing;
                change.interactable = typing;

                v.backLabel = state == JoinState.Waiting ? "Leave" : "Back";
                switch (state)
                {
                    case JoinState.Name:
                        v.primary = editingName ? "Save" : "Next";
                        v.primaryIcon = AuraIcons.Ok;
                        v.primaryEnabled = !string.IsNullOrWhiteSpace(nameInput.text);
                        v.primaryDanger = false;
                        break;
                    case JoinState.Code:
                        v.primary = "Join"; v.primaryIcon = AuraIcons.Join;
                        v.primaryEnabled = slots.IsComplete; v.primaryDanger = false;
                        break;
                    case JoinState.Joining:
                        v.primary = "Joining…"; v.primaryIcon = null; v.primaryEnabled = false; v.primaryDanger = false;
                        break;
                    case JoinState.Waiting:
                        v.primary = null;
                        break;
                    case JoinState.InRoom:
                        v.primary = "Leave room"; v.primaryIcon = null; v.primaryEnabled = true; v.primaryDanger = true;
                        break;
                }
                if (Top == v) { _backLabel.text = v.backLabel; RefreshPrimary(v); }
            }

            slots.OnChanged += () =>
            {
                if (state != JoinState.Code) return;
                SetStatus(slots.IsComplete ? "Tap Join" : "Ask your presenter for the 4-character code", AuraTheme.TextDim);
                Refresh();
            };

            // Keypad: digits, then the QWERTY rows, then Paste | Delete.
            keyButtons.AddRange(CodeKeys.Build(pad.transform, keyH, keyGap, 46f, c =>
            {
                if (state == JoinState.Code) slots.Type(c);
            }));
            var last = AuraKit.Rect(pad.transform, "KeyRowLast");
            var lh = AuraKit.HStack(last, keyGap);
            lh.childForceExpandWidth = true; lh.childForceExpandHeight = true;
            AuraKit.Size(last, -1, keyH);
            var paste = AuraKit.Pill(last, "Paste link", AuraIcons.Paste, AuraTheme.Indigo, AuraTheme.Lavender, null, keyH);
            AuraKit.Cell(paste);
            var del = AuraKit.Pill(last, "Delete", AuraIcons.Backspace, AuraTheme.Indigo, AuraTheme.Text, null, keyH);
            AuraKit.Cell(del);
            keyButtons.Add(paste);
            keyButtons.Add(del);

            del.onClick.AddListener(() =>
            {
                if (state == JoinState.Code) slots.Delete();
            });
            paste.onClick.AddListener(() =>
            {
                if (state != JoinState.Code) return;
                string clip = GUIUtility.systemCopyBuffer ?? "";
                string found = RoomCodeGenerator.FormatCode(RoomLinkManager.ExtractRoomCode(clip));
                if (!RoomCodeGenerator.IsValidCode(found))
                {
                    SetStatus("No room code found in what you copied.", AuraTheme.Rose);
                    return;
                }
                slots.Set(found.Substring(found.Length - 4));
                SetStatus("Code pasted. Tap Join", AuraTheme.TextDim);
            });

            nameInput.onValueChanged.AddListener(_ => { if (state == JoinState.Name) Refresh(); });
            change.onClick.AddListener(() =>
            {
                if (state != JoinState.Code) return;
                editingName = true;
                nameInput.text = net.GetPlayerName();
                state = JoinState.Name;
                Refresh();
            });

            // ── Network events ──
            Action<string> onJoined = roomCode =>
            {
                if (state != JoinState.Joining) return;
                bool hasModel = PhotonNetwork.CurrentRoom != null &&
                                PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(ModelSyncManager.KeyCurrentModelUrl);
                if (hasModel)
                {
                    SetStatus("Joined. Opening the presenter's model…", AuraTheme.Lavender);
                }
                else
                {
                    state = JoinState.Waiting;
                    SetStatus($"Joined <b>{roomCode}</b>.\nWaiting for the presenter to open a model. It opens here by itself.",
                              AuraTheme.Lavender);
                }
                Refresh();
            };
            Action<string> onFailed = message =>
            {
                if (state != JoinState.Joining && state != JoinState.Waiting) return;
                state = JoinState.Code;
                SetStatus(FriendlyJoinError(message), AuraTheme.Rose);
                Refresh();
            };

            v.onShow = () =>
            {
                net.OnRoomJoinedSuccess += onJoined;
                net.OnRoomJoinFailedError += onFailed;

                if (PhotonNetwork.InRoom && state != JoinState.Waiting)
                {
                    state = JoinState.InRoom;
                    SetStatus($"You're in room <b>{PhotonNetwork.CurrentRoom.Name}</b>.\nLeave it to join a different one.",
                              AuraTheme.TextDim);
                }
                else if (state == JoinState.Code && PlayerPrefs.GetInt(NameConfirmedKey, 0) == 0)
                {
                    state = JoinState.Name;
                }
                Refresh();
            };
            v.onHide = () =>
            {
                net.OnRoomJoinedSuccess -= onJoined;
                net.OnRoomJoinFailedError -= onFailed;
            };

            v.onPrimary = () =>
            {
                switch (state)
                {
                    case JoinState.Name:
                        if (string.IsNullOrWhiteSpace(nameInput.text)) return;
                        net.SetPlayerName(nameInput.text);
                        PlayerPrefs.SetInt(NameConfirmedKey, 1);
                        PlayerPrefs.Save();
                        editingName = false;
                        state = JoinState.Code;
                        SetStatus("Ask your presenter for the 4-character code", AuraTheme.TextDim);
                        Refresh();
                        break;

                    case JoinState.Code:
                        if (!slots.IsComplete) return;
                        string code = slots.Code;
                        state = JoinState.Joining;
                        SetStatus(PhotonNetwork.IsConnectedAndReady ? $"Joining AURA-{code}…" : "Connecting…",
                                  AuraTheme.TextDim);
                        Refresh();
                        net.JoinRoom("AURA-" + code);
                        break;

                    case JoinState.InRoom:
                        net.LeaveRoom();
                        state = JoinState.Code;
                        slots.Clear();
                        SetStatus("You left the room.", AuraTheme.TextDim);
                        Refresh();
                        break;
                }
            };

            v.onBack = () =>
            {
                if (state == JoinState.Name && editingName)
                {
                    editingName = false;
                    state = JoinState.Code;
                    Refresh();
                    return;
                }
                if ((state == JoinState.Waiting || state == JoinState.Joining) && PhotonNetwork.InRoom)
                    net.LeaveRoom();
                Pop();
            };

            SetStatus("Ask your presenter for the 4-character code", AuraTheme.TextDim);
            return v;
        }

        private static string FriendlyJoinError(string message)
        {
            if (string.IsNullOrEmpty(message)) return "Couldn't join. Check the code and try again.";
            string m = message.ToLowerInvariant();
            if (m.Contains("not found") || m.Contains("exist")) return "No room with that code. Check it with your presenter.";
            if (m.Contains("full")) return $"That room is full ({NetworkManager.MaxPlayers} people).";
            if (m.Contains("disconnect")) return "Lost the connection. Check your internet and try again.";
            return message;
        }
    }

    /// <summary>Scales a fixed-width row down to fit its parent on very narrow screens.</summary>
    public class FitRowWidth : MonoBehaviour
    {
        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float need = LayoutUtility.GetPreferredWidth(rt);
            float have = parent.rect.width;
            float k = need > 1f && have < need ? have / need : 1f;
            if (!Mathf.Approximately(rt.localScale.x, k)) rt.localScale = new Vector3(k, k, 1f);
        }
    }
}
