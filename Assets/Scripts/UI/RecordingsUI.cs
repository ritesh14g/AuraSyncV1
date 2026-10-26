using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using AuraSync.Recording;

namespace AuraSync.UI
{
    /// <summary>
    /// The recordings list and the replay bar (V3 Phase 9).
    ///
    ///   RECORDINGS (Record → Recordings)   each recording: name, model, date, length, size; Play,
    ///                                      Share, Heatmap, Delete (tap twice). In a room Play is
    ///                                      off: "Leave the room to replay". Import (top-right) brings
    ///                                      in a recording someone shared (one .aurarec file, voice
    ///                                      inside); one made on a scan this phone does not have is
    ///                                      listed, with Play off and the reason.
    ///
    ///   Replay bar (bottom, full width while the dock steps aside)
    ///       Replay · name                                  (Exit)
    ///       what just happened · Speaking: Asha
    ///       0:42 ────────●──────────── 10:03
    ///       (1×)  (▶/❚❚)  (Presenter view / Free camera)
    ///
    /// Back (bottom-left, or the phone's) leaves the replay, and the model goes back to how it was.
    /// </summary>
    public class RecordingsUI : MonoBehaviour
    {
        public static RecordingsUI Instance { get; private set; }

        private const string ListNavId = "recordings";
        private const string ReplayNavId = "replay";

        private AuraPage _page;
        private bool _importing;
        private string _armedDelete;
        private float _armedAt;

        private AuraSlidePanel _bar;
        private TextMeshProUGUI _barTitle, _barInfo, _speedText, _camText, _timeNow, _timeEnd;
        private Image _playIcon;
        private Slider _timeline;
        private bool _settingSlider;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            SessionPlayer.OnChanged += OnReplayChanged;
        }

        void OnDestroy()
        {
            SessionPlayer.OnChanged -= OnReplayChanged;
            PanelNavigator.Remove(ListNavId);
            PanelNavigator.Remove(ReplayNavId);
            if (Instance == this) Instance = null;
        }

        // ── List ───────────────────────────────────────────────────────

        public void OpenList()
        {
            _page.Open();
            PanelNavigator.Push(ListNavId, this, CloseList);
            Fill();
        }

        public void CloseList()
        {
            if (_page == null || !_page.IsOpen) return;
            _page.Close();
            PanelNavigator.Remove(ListNavId);
        }

        private void Fill()
        {
            _page.ClearBody();
            List<RecordingInfo> all = RecordingStore.List();
            bool inRoom = PhotonNetwork.InRoom;
            _page.Subtitle.text = inRoom ? "Leave the room to replay. You can still share one." : "Replays play on this phone only.";
            if (all.Count == 0)
                AuraKit.Text(_page.Body, "No recordings yet. Record → Start recording, or Import one someone shared with you.",
                             AuraTheme.SizeBody, AuraTheme.TextDim);
            foreach (RecordingInfo info in all) AddRow(info, inRoom);
            _page.ScrollToTop();
        }

        private void AddRow(RecordingInfo info, bool inRoom)
        {
            var row = AuraKit.Box(_page.Body, "Recording", AuraTheme.A(AuraTheme.Indigo, 0.9f), AuraTheme.Radius, true);
            AuraKit.Border(row, AuraTheme.A(AuraTheme.Slate, 0.8f));
            var v = AuraKit.VStack(row, 12f);
            v.padding = new RectOffset(30, 30, 26, 26);

            var name = AuraKit.Text(row.transform, Esc(info.name), AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            name.enableWordWrapping = false;
            bool imported = RecordingImport.IsImported(info);
            string meta = (imported ? $"Imported{(string.IsNullOrEmpty(info.header.recorderName) ? "" : " from " + Esc(info.header.recorderName))} · " : "") +
                          $"{Esc(info.header.modelName)} · {info.started:d MMM HH:mm} · {RecordingStore.Clock(info.seconds)} · " +
                          (info.HasVoice ? "with voice · " : "no sound · ") +
                          $"{RecordingStore.SizeText(info.TotalBytes)}" + (info.seconds >= 30f ? $" ({info.KbPerMinute:0} KB/min)" : "");
            AuraKit.Text(row.transform, meta, AuraTheme.SizeCaption - 2f, AuraTheme.TextDim).enableWordWrapping = true;
            var missing = AuraKit.Text(row.transform, "", AuraTheme.SizeCaption - 2f, AuraTheme.Rose);
            missing.enableWordWrapping = true;
            missing.gameObject.SetActive(false);

            var buttons = AuraKit.Rect(row.transform, "Buttons");
            AuraKit.HStack(buttons, 12f).childForceExpandHeight = true;
            AuraKit.Size(buttons, -1, 92f);
            AuraWidgets.Chip(buttons, "Play", AuraIcons.PlayFill, !inRoom, () => Play(info), out Button play, 92f);
            play.interactable = !inRoom;

            // A recording replays on its own model: one made on someone else's scan cannot.
            RecordingImport.CheckModel(info, ok =>
            {
                if (ok || play == null || missing == null) return;
                play.interactable = false;
                missing.text = $"Made on {Esc(info.header.modelName)}, a scan that isn't on this phone, so it can't be replayed here.";
                missing.gameObject.SetActive(true);
            });
            AuraWidgets.Chip(buttons, "Share", AuraIcons.Link, false, () => Toast.Show(RecordingExport.Export(info)), out _, 92f);
            AuraWidgets.Chip(buttons, "Heatmap", AuraIcons.Fire, false, () => Heatmap(info), out _, 92f);
            AuraKit.Flex(AuraKit.Rect(buttons, "Space"));
            TextMeshProUGUI delText = null;
            AuraWidgets.Chip(buttons, "Delete", AuraIcons.Trash, false, () => Delete(info, delText), out Button del, 92f);
            delText = del.GetComponentInChildren<TextMeshProUGUI>();
            if (delText != null) delText.color = AuraTheme.Rose;
        }

        private void Play(RecordingInfo info)
        {
            string problem = SessionPlayer.Play(info);
            if (problem != null) { Toast.Show(problem); return; }
            CloseList();
        }

        /// <summary>A heatmap of who looked at what in the recording (Phase 10), on its own model.</summary>
        private void Heatmap(RecordingInfo info)
        {
            if (info.header.model != AuraSync.Space.SpaceSceneManager.CurrentModelKey)
            {
                Toast.Show($"Open {info.header.modelName} first: the heatmap is painted on the recording's model.");
                return;
            }
            LoadedRecording rec;
            try { rec = RecordingStore.Load(info); }
            catch (System.Exception ex) { Toast.Show("Could not read the recording: " + ex.Message); return; }
            CloseList();
            HeatmapUI.Instance?.OpenFromRecording(rec);
        }

        /// <summary>First tap arms it ("Sure?"), a second within 3 s deletes.</summary>
        private void Delete(RecordingInfo info, TextMeshProUGUI label)
        {
            if (_armedDelete != info.path || Time.unscaledTime - _armedAt > 3f)
            {
                _armedDelete = info.path;
                _armedAt = Time.unscaledTime;
                if (label != null) label.text = "Sure?";
                return;
            }
            _armedDelete = null;
            if (RecordingStore.Delete(info)) Toast.Show("Recording deleted.");
            Fill();
        }

        // ── Import ─────────────────────────────────────────────────────

        /// <summary>Import: Android's file picker, then the chosen recording joins the list.</summary>
        private void Import()
        {
            if (_importing) return;
            _importing = true;
            StartCoroutine(RecordingFilePicker.Pick(
                path =>
                {
                    _importing = false;
                    if (string.IsNullOrEmpty(path)) return; // cancelled
                    Toast.Show(RecordingImport.Import(path));
                    if (_page.IsOpen) Fill();
                },
                error =>
                {
                    _importing = false;
                    Toast.Show(error);
                }));
        }

        // ── Replay bar ─────────────────────────────────────────────────

        private void OnReplayChanged()
        {
            SessionPlayer p = SessionPlayer.Instance;
            bool on = p != null && p.Current != null;
            if (on && !_bar.IsShown)
            {
                _bar.Show();
                PanelNavigator.Push(ReplayNavId, this, () => SessionPlayer.Instance?.Exit());
                _timeline.minValue = 0f;
                _timeline.maxValue = Mathf.Max(0.1f, p.Current.Duration);
                _timeEnd.text = RecordingStore.Clock(p.Current.Duration);
                _barTitle.text = "Replay · " + Esc(p.Current.info.name);
            }
            else if (!on && _bar.IsShown)
            {
                _bar.Hide();
                PanelNavigator.Remove(ReplayNavId);
            }
            if (!on) return;

            SetPlayIcon(p);
            _speedText.text = p.Speed == 0.5f ? "0.5×" : p.Speed == 2f ? "2×" : "1×";
            _camText.text = p.FreeCamera ? "Free camera" : "Presenter view";
        }

        private void SetPlayIcon(SessionPlayer p)
        {
            string icon = p.Playing ? AuraIcons.Pause : (p.T >= p.Current.Duration - 0.05f ? AuraIcons.Orbit : AuraIcons.PlayFill);
            _playIcon.sprite = AuraIcons.Get(icon);
        }

        void Update()
        {
            SessionPlayer p = SessionPlayer.Instance;
            if (p == null || p.Current == null || !_bar.IsShown) return;

            _settingSlider = true;
            _timeline.value = p.T;
            _settingSlider = false;
            _timeNow.text = RecordingStore.Clock(p.T);

            string info = p.Caption;
            if (!string.IsNullOrEmpty(p.Speaking)) info += (info.Length > 0 ? " · " : "") + "Speaking: " + p.Speaking;
            _barInfo.text = info.Length > 0 ? Esc(info) : " ";
            SetPlayIcon(p);
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            _page = AuraPage.Create(transform, "RecordingsPage", 99, CloseList);
            _page.Title.text = "Recordings";
            AuraWidgets.Chip(_page.HeaderRight, "Import", AuraIcons.Plus, true, Import, out _, 92f);

            var safe = AuraViewer.CreateCanvas(transform, "ReplayCanvas", 99, out _);
            BuildBar(safe);
        }

        private void BuildBar(RectTransform safe)
        {
            // Full width along the bottom: the dock steps aside during a replay.
            var sheet = AuraKit.Box(safe, "ReplayBar", AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(sheet, AuraTheme.Slate, AuraTheme.Radius + 6f);
            var rt = sheet.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(AuraViewer.Gutter, AuraViewer.BottomReserve);
            rt.offsetMax = new Vector2(-AuraViewer.Gutter, AuraViewer.BottomReserve + 300f);
            var v = AuraKit.VStack(sheet, 14f);
            v.padding = new RectOffset(36, 36, 26, 28);
            AuraKit.FitHeight(sheet);

            var head = AuraKit.Rect(rt, "Head");
            AuraKit.HStack(head, 12f);
            AuraKit.Size(head, -1, 84f);
            _barTitle = AuraKit.Text(head, "Replay", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.Bold);
            _barTitle.enableWordWrapping = false;
            AuraKit.Flex(_barTitle);
            AuraWidgets.Chip(head, "Exit", AuraIcons.Close, false, () => SessionPlayer.Instance?.Exit(), out Button exit, 84f);
            var exitText = exit.GetComponentInChildren<TextMeshProUGUI>();
            if (exitText != null) exitText.color = AuraTheme.Rose;

            _barInfo = AuraKit.Text(rt, " ", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _barInfo.enableWordWrapping = true;

            var tl = AuraKit.Rect(rt, "Timeline");
            AuraKit.HStack(tl, 14f);
            AuraKit.Size(tl, -1, 88f);
            _timeNow = AuraKit.Text(tl, "0:00", AuraTheme.SizeCaption, AuraTheme.Lavender, AuraWeight.SemiBold);
            AuraKit.Size(_timeNow, 110f);
            _timeline = AuraWidgets.Slider(tl, 0f, v2 => { if (!_settingSlider) SessionPlayer.Instance?.Seek(v2); }, null, 0f, 1f, 88f);
            AuraKit.Flex(_timeline);
            _timeEnd = AuraKit.Text(tl, "0:00", AuraTheme.SizeCaption, AuraTheme.TextDim, AuraWeight.Regular, TextAlignmentOptions.MidlineRight);
            AuraKit.Size(_timeEnd, 110f);

            var ctr = AuraKit.Rect(rt, "Controls");
            AuraKit.HStack(ctr, 22f, 0, TextAnchor.MiddleCenter);
            AuraKit.Size(ctr, -1, 130f);
            AuraWidgets.Chip(ctr, "1×", AuraIcons.Speed, false, () => SessionPlayer.Instance?.CycleSpeed(), out Button speed);
            _speedText = speed.GetComponentInChildren<TextMeshProUGUI>();
            AuraWidgets.Round(ctr, AuraIcons.Pause, 126f, true, () => SessionPlayer.Instance?.TogglePlay(), out _playIcon);
            AuraWidgets.Chip(ctr, "Presenter view", AuraIcons.Follow, true, () =>
            {
                SessionPlayer p = SessionPlayer.Instance;
                if (p != null) p.SetFreeCamera(!p.FreeCamera);
            }, out Button cam);
            _camText = cam.GetComponentInChildren<TextMeshProUGUI>();

            _bar = AuraViewer.SlideUp(rt);
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");
    }
}
