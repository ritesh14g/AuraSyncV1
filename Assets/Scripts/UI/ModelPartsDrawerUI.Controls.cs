using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// ROOM CONTROLS (V3 Phase 1), in the Parts drawer under the explode slider while in a room:
    /// one line per shared control saying who drives it, with whatever this person can do about it:
    ///
    ///   viewer                             Ask (Asked… while the presenter decides)
    ///   student who drives it              Give back, and Stop live while it is shared live
    ///   presenter, while a student has it  Take back
    ///   presenter, while they have it      Stop live while it is shared live
    /// </summary>
    public partial class ModelPartsDrawerUI
    {
        private class ControlRow
        {
            public Control control;
            public TextMeshProUGUI label;
            public Button a, b;
            public TextMeshProUGUI aText, bText;
            public Image aFill;
        }

        private GameObject _controlsBox;
        private readonly List<ControlRow> _controlRows = new List<ControlRow>();
        private float _nextControlsRefresh;

        private void BuildControlsBox(RectTransform parent)
        {
            var box = AuraKit.Box(parent, "RoomControls", AuraTheme.A(AuraTheme.Ink, 0.6f), AuraTheme.Radius - 6f);
            _controlsBox = box.gameObject;
            var v = AuraKit.VStack(box, 8f);
            v.padding = new RectOffset(24, 20, 18, 18);

            AuraKit.Caps(box.transform, "Room controls");
            foreach (Control c in ControlOwnership.Active)
                _controlRows.Add(BuildControlRow(box.transform, c));
            _controlsBox.SetActive(false);
        }

        private ControlRow BuildControlRow(Transform parent, Control c)
        {
            var row = AuraKit.Rect(parent, "Row_" + ControlOwnership.Id(c));
            AuraKit.HStack(row, 10f).childForceExpandHeight = true;
            AuraKit.Size(row, -1, 84f);

            var r = new ControlRow { control = c };
            r.label = AuraKit.Text(row, ControlOwnership.Label(c), AuraTheme.SizeSmall - 2f, AuraTheme.Text);
            r.label.richText = true;
            r.label.enableWordWrapping = false;
            r.label.enableAutoSizing = true;
            r.label.fontSizeMin = 18f;
            r.label.fontSizeMax = AuraTheme.SizeSmall - 2f;
            AuraKit.Size(r.label, 0f, -1, 1f);

            r.a = AuraKit.Pill(row, "", null, AuraTheme.A(AuraTheme.Lavender, 0.2f), AuraTheme.Lavender,
                               () => OnControlButton(c, first: true), 84f, AuraTheme.Lavender);
            r.a.GetComponent<LayoutElement>().preferredWidth = 190f;
            r.aText = r.a.GetComponentInChildren<TextMeshProUGUI>();
            r.aFill = r.a.GetComponent<Image>();
            r.b = AuraKit.Pill(row, "", null, AuraTheme.Ink, AuraTheme.Text,
                               () => OnControlButton(c, first: false), 84f, AuraTheme.Slate);
            r.b.GetComponent<LayoutElement>().preferredWidth = 170f;
            r.bText = r.b.GetComponentInChildren<TextMeshProUGUI>();
            foreach (var t in new[] { r.aText, r.bText })
            {
                t.fontSize = AuraTheme.SizeCaption;
                t.enableAutoSizing = true;
                t.fontSizeMin = 16f;
                t.fontSizeMax = AuraTheme.SizeCaption;
            }
            return r;
        }

        private void BindControls()
        {
            ControlOwnership.OnChanged += RefreshControls;
            LiveSharing.OnChanged += RefreshControls;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnPlayerListUpdated += RefreshControls;
        }

        private void UnbindControls()
        {
            ControlOwnership.OnChanged -= RefreshControls;
            LiveSharing.OnChanged -= RefreshControls;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnPlayerListUpdated -= RefreshControls;
        }

        /// <summary>A request's "Asked…" has to turn back into "Ask" when it times out.</summary>
        private void TickControls()
        {
            if (!IsOpen || Time.unscaledTime < _nextControlsRefresh) return;
            _nextControlsRefresh = Time.unscaledTime + 0.5f;
            RefreshControls();
        }

        private void RefreshControls()
        {
            if (_controlsBox == null) return;
            bool inRoom = PhotonNetwork.InRoom;
            if (_controlsBox.activeSelf != inRoom) _controlsBox.SetActive(inRoom);
            if (!inRoom) return;

            bool presenter = NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;

            foreach (ControlRow r in _controlRows)
            {
                Control c = r.control;
                bool holds = ControlOwnership.LocalHolds(c);
                bool live = LiveSharing.IsLive(c);
                string state;
                string a = null, b = null;
                bool aOn = true, aDanger = false;

                if (holds)
                {
                    state = presenter ? "you" : "<color=#9894C4>you drive it</color>";
                    if (live) { state += "  <color=#6FC2A0>LIVE</color>"; a = "Stop live"; aDanger = true; }
                    if (!presenter) b = "Give back";
                }
                else
                {
                    state = ControlOwnership.IsHandedOut(c)
                        ? $"<color=#9894C4>{NoTags(ControlOwnership.HolderName(c))}</color>"
                        : "presenter";
                    if (presenter) a = "Take back";
                    else if (ControlOwnership.IsRequestPending(c)) { a = "Asked…"; aOn = false; }
                    else a = "Ask";
                }

                r.label.text = $"<b>{ControlOwnership.Label(c)}</b> <color=#A9A8CC>·</color> {state}";

                r.a.gameObject.SetActive(a != null);
                if (a != null)
                {
                    r.aText.text = a;
                    r.aText.color = aDanger ? AuraTheme.Rose : AuraTheme.Lavender;
                    r.aFill.color = aDanger ? AuraTheme.A(AuraTheme.Rose, 0.18f) : AuraTheme.A(AuraTheme.Lavender, 0.2f);
                    r.a.interactable = aOn;
                }
                r.b.gameObject.SetActive(b != null);
                if (b != null) r.bText.text = b;
            }
        }

        private void OnControlButton(Control c, bool first)
        {
            bool presenter = NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;
            bool holds = ControlOwnership.LocalHolds(c);

            if (!first) { if (holds && !presenter) ControlOwnership.GiveBack(c); }
            else if (holds) LiveSharing.Stop(c);
            else if (presenter) ControlOwnership.TakeBack(c);
            else if (ControlOwnership.Request(c)) Toast.Show($"Asked the presenter for {ControlOwnership.Label(c)}.");

            RefreshControls();
        }
    }
}
