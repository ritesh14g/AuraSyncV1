using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// ROOM CONTROLS (V3 Phase 1): one row per shared control in the Parts drawer, right under the
    /// explode slider, saying who drives it for the room and offering whatever this person can do
    /// about it:
    ///
    ///   viewer                          ASK TO DRIVE (ASKED... while the presenter decides)
    ///   student who drives it           GIVE BACK, and STOP LIVE while it is shared live
    ///   presenter, while a student has it   TAKE BACK
    ///   presenter, while they have it   STOP LIVE while it is shared live (handing it out is in
    ///                                   the player's badge menu)
    ///
    /// Hidden outside a room, where there is nobody to share with.
    /// </summary>
    public partial class ModelPartsDrawerUI
    {
        private static readonly Color AskColor      = new Color(0.16f, 0.42f, 0.72f, 1f);
        private static readonly Color TakeBackColor = new Color(0.62f, 0.45f, 0.08f, 1f);
        private static readonly Color GiveBackColor = new Color(0.36f, 0.38f, 0.46f, 1f);
        private static readonly Color StopLiveColor = new Color(0.55f, 0.18f, 0.18f, 1f);

        private class ControlRow
        {
            public Control         control;
            public GameObject      go;
            public TextMeshProUGUI label;
            public Button          a, b;
            public TextMeshProUGUI aText, bText;
            public Image           aImage, bImage;
        }

        private GameObject _controlsBox;
        private readonly List<ControlRow> _controlRows = new List<ControlRow>();
        private float _nextControlsRefresh;

        private void BuildControlsBox(Transform parent)
        {
            _controlsBox = new GameObject("RoomControls");
            _controlsBox.transform.SetParent(parent, false);
            AddLayout(_controlsBox);

            Image bg = _controlsBox.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.04f, 0.07f, 0.85f);
            bg.raycastTarget = false;

            VerticalLayoutGroup vg = _controlsBox.AddComponent<VerticalLayoutGroup>();
            vg.padding            = new RectOffset(8, 8, 6, 6);
            vg.spacing            = 4;
            vg.childControlWidth  = true;
            vg.childControlHeight = true;
            vg.childForceExpandWidth  = true;
            vg.childForceExpandHeight = false;
            // No ContentSizeFitter: the panel's own layout group sizes this box from its rows.

            TextMeshProUGUI title = MakeLabel(_controlsBox.transform, "Title", "ROOM CONTROLS", 16,
                                              new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            AddLayout(title.gameObject, height: 22f);
            title.GetComponent<LayoutElement>().flexibleHeight = 0f;

            foreach (Control c in ControlOwnership.Active)
                _controlRows.Add(BuildControlRow(_controlsBox.transform, c));

            _controlsBox.SetActive(false);
        }

        private ControlRow BuildControlRow(Transform parent, Control c)
        {
            GameObject row = new GameObject("Row_" + ControlOwnership.Id(c));
            row.transform.SetParent(parent, false);
            AddLayout(row, height: 44f);
            row.GetComponent<LayoutElement>().flexibleHeight = 0f; // a fixed-height row must not grow

            HorizontalLayoutGroup hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.spacing            = 6;
            hg.childAlignment     = TextAnchor.MiddleLeft;
            hg.childControlWidth  = true;
            hg.childControlHeight = true;
            hg.childForceExpandWidth = false;

            var r = new ControlRow { control = c, go = row };

            r.label = MakeLabel(row.transform, "Label", ControlOwnership.Label(c), 19,
                                Color.white, TextAlignmentOptions.MidlineLeft);
            r.label.enableAutoSizing = true;
            r.label.fontSizeMin = 13f;
            r.label.fontSizeMax = 19f;
            r.label.enableWordWrapping = false;
            r.label.overflowMode = TextOverflowModes.Ellipsis;
            AddLayout(r.label.gameObject, flexWidth: 1f);

            r.a = MakeSmallButton(row.transform, "A", "", AskColor, () => OnControlButton(c, first: true), width: 128f);
            r.b = MakeSmallButton(row.transform, "B", "", GiveBackColor, () => OnControlButton(c, first: false), width: 116f);
            r.aText  = r.a.GetComponentInChildren<TextMeshProUGUI>();
            r.bText  = r.b.GetComponentInChildren<TextMeshProUGUI>();
            r.aImage = r.a.GetComponent<Image>();
            r.bImage = r.b.GetComponent<Image>();
            return r;
        }

        private void BindControls()
        {
            ControlOwnership.OnChanged += RefreshControls;
            LiveSharing.OnChanged      += RefreshControls;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnPlayerListUpdated += RefreshControls;
        }

        private void UnbindControls()
        {
            ControlOwnership.OnChanged -= RefreshControls;
            LiveSharing.OnChanged      -= RefreshControls;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnPlayerListUpdated -= RefreshControls;
        }

        /// <summary>A request's ASKED... has to turn back into ASK TO DRIVE when it times out.</summary>
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
                Control c    = r.control;
                bool holds   = ControlOwnership.LocalHolds(c);
                bool live    = LiveSharing.IsLive(c);
                string name  = ControlOwnership.Label(c);
                string state;
                string a = null, b = null;
                Color  aColor = AskColor;
                bool   aOn = true;

                if (holds)
                {
                    state = presenter ? "you (presenter)" : "<color=#7CD8FF>you drive it</color>";
                    if (live) { state += "  <color=#4CE08A>LIVE</color>"; a = "STOP LIVE"; aColor = StopLiveColor; }
                    if (!presenter) b = "GIVE BACK";
                }
                else
                {
                    state = ControlOwnership.IsHandedOut(c)
                        ? $"<color=#FFD133>{NoTags(ControlOwnership.HolderName(c))}</color> drives it"
                        : "presenter";

                    if (presenter) { a = "TAKE BACK"; aColor = TakeBackColor; }
                    else if (ControlOwnership.IsRequestPending(c)) { a = "ASKED..."; aOn = false; }
                    else a = "ASK TO DRIVE";
                }

                r.label.text = $"<b>{name}</b>  {state}";

                r.a.gameObject.SetActive(a != null);
                if (a != null)
                {
                    r.aText.text = a;
                    r.aImage.color = aColor;
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
            else if (holds)                        LiveSharing.Stop(c);
            else if (presenter)                    ControlOwnership.TakeBack(c);
            else if (ControlOwnership.Request(c))  Toast.Show($"Asked the presenter for {ControlOwnership.Label(c)}.");

            RefreshControls();
        }
    }
}
