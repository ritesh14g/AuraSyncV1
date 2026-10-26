using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using AuraSync.Networking;
using AuraSync.Recording;
using AuraSync.Reports;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// What used to be SETTINGS, now two things (UI V2):
    ///
    ///   ANALYSIS (top-right)   a card that drops down under the button:
    ///                            Heatmap     where the class has been looking
    ///                            Ask quiz    an AI quiz for the room (presenter, in a room)
    ///                            Find task   "find the part" challenge (presenter, in a room)
    ///                            Reports     saved quiz and heatmap reports (unseen count on the button)
    ///
    ///   RECORD (dock tab)      a sheet beside the tab: Start recording / Stop and save, and the
    ///                          Recordings list (replay, share, heatmap). While recording the tab
    ///                          turns rose and shows the time, and a REC pill sits at the top for
    ///                          everyone in the room. Stopping asks for a name; Back keeps the
    ///                          suggested one, Save (bottom-right) uses what was typed.
    ///
    /// Recording is for the presenter, or anyone alone (D6): others see the tab only while someone
    /// records, and tapping it says who.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        public static SettingsPanelUI Instance { get; private set; }

        private const string AnalysisId = "analysis";
        private const string RecId = "rec";
        private const string NavAnalysis = "analysis";
        private const string NavRec = "rec";
        private const string NameNavId = "rec-name";

        // Analysis
        private AuraSlidePanel _analysis;
        private RectTransform _analysisBody;
        private GameObject _quizRow, _findRow;
        private TextMeshProUGUI _reportsSub;

        // Record
        private AuraSlidePanel _recSheet;
        private Button _recButton;
        private Image _recFill;
        private TextMeshProUGUI _recLabel, _recHint, _recStatus, _listSub;
        private GameObject _recPill;
        private TextMeshProUGUI _recPillText;

        // Naming
        private GameObject _nameModal;
        private TMP_InputField _nameField;

        private bool _analysisShown, _recShown;
        private int _recordingActor = -1;
        private float _nextText;
        private int _fileCount;

        private bool AnalysisOpen => _analysis != null && _analysis.IsShown;
        private bool RecOpen => _recSheet != null && _recSheet.IsShown;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void Start() => StartCoroutine(Bind());

        void OnDestroy()
        {
            SessionRecorder.OnChanged -= Recount;
            ReportStore.OnChanged -= RefreshReports;
            PanelNavigator.Remove(NavAnalysis);
            PanelNavigator.Remove(NavRec);
            PanelNavigator.Remove(NameNavId);
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD hud = SpaceSessionHUD.Instance;
            hud.AddCornerButton(AnalysisId, "Analysis", ToggleAnalysis);
            hud.SetSlotVisible(AnalysisId, false);
            hud.AddRailButton(RecId, "Record", OnRecTab);
            hud.SetSlotVisible(RecId, false);
            SessionRecorder.OnChanged += Recount;
            ReportStore.OnChanged += RefreshReports;
            Recount();
            RefreshReports();
        }

        private void Recount()
        {
            _fileCount = RecordingStore.List().Count;
            RefreshRecord();
        }

        void Update()
        {
            SpaceSessionHUD hud = SpaceSessionHUD.Instance;
            if (hud == null || !hud.HasSlot(AnalysisId)) return;

            bool model = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) && !AuraSync.Challenge.ReassemblyGame.IsActive;
            bool analysis = model && !SessionPlayer.IsReplaying;
            if (analysis != _analysisShown)
            {
                _analysisShown = analysis;
                hud.SetSlotVisible(AnalysisId, analysis);
                if (!analysis) CloseAnalysis();
            }

            SessionRecorder rec = SessionRecorder.Instance;
            bool mine = rec != null && rec.IsRecording;
            Player other = mine ? null : SessionRecorder.RecordingPlayer();
            bool recTab = model && !SessionPlayer.IsReplaying && (SessionRecorder.LocalMayRecord || mine || other != null);
            if (recTab != _recShown)
            {
                _recShown = recTab;
                hud.SetSlotVisible(RecId, recTab);
                if (!recTab) CloseRecord();
            }

            // Tell everyone when somebody starts recording.
            int actor = mine ? -2 : other != null ? other.ActorNumber : -1;
            if (actor != _recordingActor)
            {
                if (other != null && PhotonNetwork.LocalPlayer != null && other.ActorNumber != PhotonNetwork.LocalPlayer.ActorNumber)
                    Toast.Show($"{NetworkManager.DisplayName(other)} is recording this session, with their own voice. Other voices are not recorded.");
                _recordingActor = actor;
            }

            if (Time.unscaledTime >= _nextText)
            {
                _nextText = Time.unscaledTime + 0.25f;
                bool anyone = mine || other != null;
                hud.SetSlotLabel(RecId, mine ? RecordingStore.Clock(rec.Elapsed) : anyone ? "Rec" : "Record");
                hud.SetSlotTint(RecId, anyone ? (Color?)AuraTheme.Rose : null);
                bool pill = anyone && (ModelPartsDrawerUI.Instance == null || !ModelPartsDrawerUI.Instance.IsOpen);
                if (_recPill.activeSelf != pill) _recPill.SetActive(pill);
                if (anyone)
                    _recPillText.text = mine ? $"REC  {RecordingStore.Clock(rec.Elapsed)}" : $"REC  ·  {NetworkManager.DisplayName(other)}";
                if (RecOpen) RefreshRecord();
                if (AnalysisOpen) RefreshAnalysis();
            }
        }

        // ── Analysis ───────────────────────────────────────────────────

        private void ToggleAnalysis()
        {
            if (AnalysisOpen) CloseAnalysis();
            else OpenAnalysis();
        }

        private void OpenAnalysis()
        {
            ViewerPanels.Opened(AnalysisId, CloseAnalysis);
            PanelNavigator.Push(NavAnalysis, this, CloseAnalysis);
            RefreshAnalysis();
            _analysis.Show();
        }

        public void ClosePanel() => CloseAnalysis();

        private void CloseAnalysis()
        {
            if (!AnalysisOpen) return;
            _analysis.Hide();
            ViewerPanels.Closed(AnalysisId);
            PanelNavigator.Remove(NavAnalysis);
        }

        private void RefreshAnalysis()
        {
            bool presenter = PhotonNetwork.InRoom && ModelPartSelector.IsPresenting;
            if (_quizRow.activeSelf != presenter) _quizRow.SetActive(presenter);
            if (_findRow.activeSelf != presenter) _findRow.SetActive(presenter);
        }

        private void RefreshReports()
        {
            int n = ReportStore.Count, unseen = ReportStore.Unseen;
            if (_reportsSub != null)
                _reportsSub.text = n == 0 ? "Quiz and heatmap reports" : unseen > 0 ? $"{n} saved · {unseen} new" : $"{n} saved";
            SpaceSessionHUD.Instance?.SetSlotCount(AnalysisId, unseen);
        }

        // ── Record ─────────────────────────────────────────────────────

        private void OnRecTab()
        {
            SessionRecorder rec = SessionRecorder.Instance;
            bool mine = rec != null && rec.IsRecording;
            if (!mine && !SessionRecorder.LocalMayRecord)
            {
                Player p = SessionRecorder.RecordingPlayer();
                Toast.Show(p != null
                    ? $"{NetworkManager.DisplayName(p)} is recording this session: the model, notes, views, pings and {NetworkManager.DisplayName(p)}'s voice. Your voice is not recorded."
                    : "Only the presenter records in a room.");
                return;
            }
            if (RecOpen) CloseRecord();
            else OpenRecord();
        }

        private void OpenRecord()
        {
            ViewerPanels.Opened(RecId, CloseRecord);
            PanelNavigator.Push(NavRec, this, CloseRecord);
            Recount();
            _recSheet.Show();
        }

        private void CloseRecord()
        {
            if (!RecOpen) return;
            _recSheet.Hide();
            ViewerPanels.Closed(RecId);
            PanelNavigator.Remove(NavRec);
        }

        private void RefreshRecord()
        {
            if (_recLabel == null) return;
            SessionRecorder rec = SessionRecorder.Instance;
            bool on = rec != null && rec.IsRecording;
            _recLabel.text = on ? $"Stop and save   {RecordingStore.Clock(rec.Elapsed)}" : "Start recording";
            _recFill.color = on ? AuraTheme.Rose : AuraTheme.Lavender;
            _recStatus.text = on ? "● LIVE" : "";
            _recHint.text = PhotonNetwork.InRoom
                ? "Records the session on this phone: the model, notes, everyone's views and pings, and your voice while your mic is on. Everyone in the room sees REC."
                : "Records what you do with the model, and your voice, on this phone: a narrated walkthrough to replay or share.";
            _listSub.text = _fileCount == 0 ? "None yet" : $"{_fileCount} saved · replay, share, heatmap";
        }

        private void OnRecordPressed()
        {
            SessionRecorder rec = SessionRecorder.Instance;
            if (rec == null) return;
            if (rec.IsRecording) { StopAndName(); return; }

            if (rec.Begin())
            {
                bool muted = PhotonNetwork.InRoom && VoiceChatManager.Instance != null && VoiceChatManager.Instance.IsMuted;
                Toast.Show(PhotonNetwork.InRoom
                    ? (muted ? "Recording. Your mic is off: turn it on to record your voice. Everyone sees REC." : "Recording, with your voice. Everyone in the room sees REC.")
                    : "Recording, with your voice.");
                CloseRecord();
            }
            else Toast.Show("Could not start recording.");
            RefreshRecord();
        }

        private void OpenRecordings()
        {
            CloseRecord();
            RecordingsUI.Instance?.OpenList();
        }

        // ── Naming ─────────────────────────────────────────────────────

        private void StopAndName()
        {
            SessionRecorder rec = SessionRecorder.Instance;
            if (rec == null || !rec.IsRecording) return;
            rec.Stop(askName: true);
            CloseRecord();

            _nameField.text = rec.PendingDefaultName ?? "";
            _nameModal.SetActive(true);
            PanelNavigator.Push(NameNavId, this, () => SaveName(useDefault: true));
        }

        private void SaveName(bool useDefault)
        {
            if (!_nameModal.activeSelf) return;
            _nameField.DeactivateInputField();
            _nameModal.SetActive(false);
            PanelNavigator.Remove(NameNavId);
            SessionRecorder.Instance?.FinishPending(useDefault ? null : _nameField.text);
            Toast.Show("Recording saved. Record → Recordings to replay or share it.");
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "AnalysisCanvas", 99, out _);
            BuildAnalysis(safe);
            BuildRecord(safe);
            BuildRecPill(safe);
            BuildNameModal();
        }

        private void BuildAnalysis(RectTransform safe)
        {
            var card = AuraKit.Box(safe, "AnalysisCard", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var rt = card.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(820, 400);
            var top = new Vector2(-AuraViewer.Gutter, -(AuraViewer.Gutter + AuraViewer.TopButton + 20f));
            rt.anchoredPosition = top;
            var v = AuraKit.VStack(card, 16f);
            v.padding = new RectOffset(32, 32, 32, 32);
            AuraKit.FitHeight(card);
            _analysisBody = rt;

            AuraKit.Text(rt, "Analysis", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            AuraKit.ListRow(rt, AuraIcons.Fire, "Heatmap", "Where the class has been looking",
                            () => { CloseAnalysis(); HeatmapUI.Instance?.Open(); }, out _);
            _quizRow = AuraKit.ListRow(rt, AuraIcons.Quiz, "Ask quiz", "An AI quiz for everyone in the room",
                                       () => { CloseAnalysis(); QuizTeacherUI.Instance?.Open(); }, out _).gameObject;
            _findRow = AuraKit.ListRow(rt, AuraIcons.Target, "Find task", "\"Find the part\" challenge for the room",
                                       () => { CloseAnalysis(); AttentionUI.Instance?.OpenFindTask(); }, out _).gameObject;
            AuraKit.ListRow(rt, AuraIcons.Doc, "Reports", "Quiz and heatmap reports",
                            () => { CloseAnalysis(); ReportsUI.Open(); }, out _reportsSub);

            _analysis = AuraSlidePanel.Attach(rt, top, top + new Vector2(0, 50f));
        }

        private void BuildRecord(RectTransform safe)
        {
            var sheet = AuraViewer.ToolSheet(safe, "RecordSheet");
            AuraViewer.SheetHeader(sheet, "Recording", out _recStatus);
            _recStatus.color = AuraTheme.Rose;

            _recButton = AuraKit.Pill(sheet, "Start recording", AuraIcons.Record, AuraTheme.Lavender, AuraTheme.Ink, OnRecordPressed,
                                      AuraTheme.SlotHeight - 10f);
            _recFill = _recButton.GetComponent<Image>();
            _recLabel = _recButton.GetComponentInChildren<TextMeshProUGUI>();

            AuraKit.ListRow(sheet, AuraIcons.Doc, "Recordings", "None yet", OpenRecordings, out _listSub);
            _recHint = AuraKit.Text(sheet, "", AuraTheme.SizeCaption, AuraTheme.TextDim);

            _recSheet = AuraViewer.SlideUp(sheet);
        }

        private void BuildRecPill(RectTransform safe)
        {
            var pill = AuraKit.Box(safe, "RecPill", AuraTheme.A(AuraTheme.Rose, 0.2f), 30f);
            AuraKit.Border(pill, AuraTheme.Rose, 30f);
            // Under Parts, top-left (the top centre holds the model name and the selected part).
            AuraKit.Pin(pill.rectTransform, new Vector2(0, 1), new Vector2(0, 1),
                        new Vector2(AuraViewer.Gutter, -(AuraViewer.Gutter + AuraViewer.TopButton + 14f)), new Vector2(360, 58));
            var h = AuraKit.HStack(pill, 12f, 0, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(22, 22, 0, 0);
            var dot = AuraKit.Icon(pill.transform, AuraIcons.Record, 34f, AuraTheme.Rose);
            dot.gameObject.AddComponent<Blink>();
            _recPillText = AuraKit.Text(pill.transform, "REC", AuraTheme.SizeCaption, AuraTheme.Text, AuraWeight.Bold);
            _recPillText.enableWordWrapping = false;
            _recPill = pill.gameObject;
            _recPill.SetActive(false);
        }

        private void BuildNameModal()
        {
            var canvas = AuraKit.CreateCanvas(transform, "RecordingName", 102);
            _nameModal = canvas.gameObject;
            var dim = AuraKit.Box(canvas.transform, "Dim", AuraTheme.A(AuraTheme.Ink, 0.7f), 0f, true);
            AuraKit.Fill(dim.rectTransform);
            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();
            var lift = AuraKit.Fill(AuraKit.Rect(safe, "Lift"));
            lift.gameObject.AddComponent<KeyboardLift>().target = lift;

            var card = AuraKit.Box(lift, "Card", AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.55f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            card.gameObject.AddComponent<MatchParentWidth>().max = 900f;
            var v = AuraKit.VStack(card, 18f);
            v.padding = new RectOffset(44, 44, 40, 40);
            AuraKit.FitHeight(card);
            AuraKit.Text(card.transform, "Name this recording", AuraTheme.SizeTitle - 8f, AuraTheme.Text, AuraWeight.Bold);
            _nameField = AuraWidgets.Input(card.transform, "Recording name", "", false, 130f, 60);
            _nameField.richText = false;
            AuraKit.Text(card.transform, "Back keeps the suggested name.", AuraTheme.SizeCaption, AuraTheme.TextDim);

            var back = AuraKit.Pill(lift, "Back", AuraIcons.Back, AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Text,
                                    () => SaveName(useDefault: true), AuraViewer.BackHeight, AuraTheme.Slate);
            AuraKit.Pin((RectTransform)back.transform, Vector2.zero, Vector2.zero,
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));
            var save = AuraKit.Pill(lift, "Save", AuraIcons.Ok, AuraTheme.Lavender, AuraTheme.Ink,
                                    () => SaveName(useDefault: false), AuraViewer.BackHeight);
            AuraKit.Pin((RectTransform)save.transform, new Vector2(1, 0), new Vector2(1, 0),
                        new Vector2(-AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(300, AuraViewer.BackHeight));

            _nameModal.SetActive(false);
        }
    }

    /// <summary>A soft blink for the REC dot.</summary>
    public class Blink : MonoBehaviour
    {
        private Graphic _g;
        void Awake() => _g = GetComponent<Graphic>();
        void Update()
        {
            if (_g == null) return;
            Color c = _g.color;
            c.a = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
            _g.color = c;
        }
    }
}
