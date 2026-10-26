using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using AuraSync.Challenge;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The screens around the reassembly challenge (ReassemblyGame):
    ///
    ///   * PLAY (dock tab) - only when you are not in a room, with a model loaded.
    ///   * Level sheet - a three-stop slider (Easy, Medium, Hard); the line under it says what the
    ///     chosen level means (or why it can't start); the header shows the best score; Start.
    ///   * While playing - everything else steps aside: one slim bar at the top with the time,
    ///     parts fitted, mistakes (Hard), Hint and Quit; each fitted part flashes a short toast.
    ///   * Results - stars, three key numbers, and Play again / Harder level.
    ///
    /// Back (on screen or the phone's) closes the sheet or the results, and quits a game in progress.
    /// </summary>
    public class ChallengeUI : MonoBehaviour
    {
        public static ChallengeUI Instance { get; private set; }

        private const string RailId = "play";
        private const string NavMenu = "challenge-menu";
        private const string NavGame = "challenge";
        private const string NavResult = "challenge-result";

        private static readonly string[] Names = { "Easy", "Medium", "Hard" };

        private AuraSlidePanel _menu;
        private Slider _level;
        private TextMeshProUGUI _levelText, _bestText, _startLabel;
        private Button _start;
        private Image _startFill;
        private readonly TextMeshProUGUI[] _stopLabels = new TextMeshProUGUI[3];
        private int _chosen = 1;

        private AuraSlidePanel _bar;
        private TextMeshProUGUI _barText;
        private GameObject _toast;
        private TextMeshProUGUI _toastText;
        private string _lastMessage = "";

        private GameObject _results;
        private TextMeshProUGUI _resultTitle, _resultBest;
        private readonly Image[] _stars = new Image[3];
        private readonly TextMeshProUGUI[] _stats = new TextMeshProUGUI[3];
        private Button _harder;
        private Difficulty _lastDifficulty;

        private ReassemblyGame _game;
        private bool _railShown;

        private bool MenuOpen => _menu != null && _menu.IsShown;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
        }

        void Start() => StartCoroutine(Bind());

        void OnDestroy()
        {
            if (_game != null) { _game.OnStateChanged -= OnGameChanged; _game.OnFinished -= ShowResults; }
            PanelNavigator.Remove(NavMenu);
            PanelNavigator.Remove(NavGame);
            PanelNavigator.Remove(NavResult);
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(RailId, "Play", ToggleMenu);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);

            while (ReassemblyGame.Instance == null) yield return null;
            _game = ReassemblyGame.Instance;
            _game.OnStateChanged += OnGameChanged;
            _game.OnFinished += ShowResults;
        }

        void Update()
        {
            // Play only with a model, no room, and no game running.
            bool show = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) && !PhotonNetwork.InRoom &&
                        !ReassemblyGame.IsActive;
            if (show != _railShown && SpaceSessionHUD.Instance != null)
            {
                _railShown = show;
                SpaceSessionHUD.Instance.SetSlotVisible(RailId, show);
                if (!show) CloseMenu();
            }

            if (ReassemblyGame.IsActive) RefreshBar();
        }

        // ── Level sheet ────────────────────────────────────────────────

        private void ToggleMenu()
        {
            if (MenuOpen) CloseMenu();
            else OpenMenu();
        }

        private void OpenMenu()
        {
            if (_game == null) return;
            ViewerPanels.Opened(RailId, CloseMenu);
            PanelNavigator.Push(NavMenu, this, CloseMenu);
            _level.SetValueWithoutNotify(_chosen);
            ShowLevel(_chosen);
            _menu.Show();
        }

        private void CloseMenu()
        {
            if (!MenuOpen) return;
            _menu.Hide();
            ViewerPanels.Closed(RailId);
            PanelNavigator.Remove(NavMenu);
        }

        private void ShowLevel(int i)
        {
            _chosen = Mathf.Clamp(i, 0, 2);
            var d = (Difficulty)_chosen;
            string why = "";
            bool can = _game != null && _game.CanStart(d, out why);
            _levelText.text = can ? LevelDescription(d) : $"<color=#D46A82>{why}</color>";
            int best = ReassemblyGame.BestScore(d);
            _bestText.text = best > 0 ? $"BEST {best}" : "";
            _startLabel.text = $"Start · {Names[_chosen]}";
            _start.interactable = can;
            _startFill.color = can ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Slate, 0.45f);
            for (int k = 0; k < 3; k++)
            {
                _stopLabels[k].color = k == _chosen ? AuraTheme.Text : AuraTheme.TextDim;
                _stopLabels[k].fontStyle = k == _chosen ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private static string LevelDescription(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return "The main parts only. A faint outline shows where each goes, with generous snapping.";
                case Difficulty.Medium: return "Every part. The outline still shows where each one goes.";
                default: return "Every part, in the real assembly order, with no outline. A part fitted too early bounces back.";
            }
        }

        private void StartLevel(Difficulty d)
        {
            CloseMenu();
            _game.Begin(d);
            if (ReassemblyGame.IsActive) PanelNavigator.Push(NavGame, this, () => _game.Quit());
        }

        // ── While playing ──────────────────────────────────────────────

        private void OnGameChanged()
        {
            bool active = ReassemblyGame.IsActive;
            SpaceSessionHUD.Instance?.SetChromeHidden(active); // everything but Back steps aside
            if (active) { ViewerPanels.CloseAll(); _bar.Show(); RefreshBar(); }
            else { _bar.Hide(); _toast.SetActive(false); PanelNavigator.Remove(NavGame); }
        }

        private void RefreshBar()
        {
            int s = Mathf.FloorToInt(_game.Elapsed);
            _barText.text = $"<color=#A9A8CC>Time</color> <b>{s / 60}:{s % 60:00}</b>    <color=#A9A8CC>Fitted</color> <b>{_game.Placed}/{_game.Total}</b>" +
                            (_game.Mode == Difficulty.Hard ? $"    <color=#A9A8CC>Mistakes</color> <b>{_game.Mistakes}</b>" : "");

            bool fresh = Time.unscaledTime - _game.MessageTime < 2.5f && !string.IsNullOrEmpty(_game.Message);
            if (fresh && _game.Message != _lastMessage)
            {
                _lastMessage = _game.Message;
                _toastText.text = Esc(_game.Message);
            }
            if (_toast.activeSelf != fresh) _toast.SetActive(fresh);
            if (!fresh) _lastMessage = "";
        }

        // ── Results ────────────────────────────────────────────────────

        private void ShowResults(ReassemblyGame.Result r)
        {
            _lastDifficulty = r.difficulty;
            _resultTitle.text = r.stars == 3 ? "Perfect rebuild!" : "Model rebuilt";
            for (int i = 0; i < 3; i++) _stars[i].color = i < r.stars ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Slate, 0.6f);
            int s = Mathf.RoundToInt(r.seconds);
            _stats[0].text = $"<b>{s / 60}:{s % 60:00}</b>\n<size=70%><color=#A9A8CC>TIME</color></size>";
            _stats[1].text = $"<b>{r.hints}</b>\n<size=70%><color=#A9A8CC>HINTS</color></size>";
            _stats[2].text = $"<b>{r.score}</b>\n<size=70%><color=#A9A8CC>SCORE</color></size>";
            _resultBest.text = $"{r.difficulty} · {r.pieces} parts · {r.moves} moves" +
                               (r.difficulty == Difficulty.Hard ? $" · {r.mistakes} mistakes" : "") +
                               (r.newBest ? "\n<color=#9894C4>New best!</color>" : $"\n<color=#A9A8CC>Best {r.best}</color>");
            _harder.gameObject.SetActive(r.difficulty != Difficulty.Hard);

            _results.SetActive(true);
            var g = _results.GetComponent<CanvasGroup>();
            g.alpha = 0f;
            AuraMotion.Fade(g, 1f, 0.2f);
            PanelNavigator.Push(NavResult, this, CloseResults);
        }

        private void CloseResults()
        {
            _results.SetActive(false);
            PanelNavigator.Remove(NavResult);
        }

        private void PlayHarder()
        {
            CloseResults();
            _chosen = Mathf.Min(2, (int)_lastDifficulty + 1);
            OpenMenu();
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "ChallengeCanvas", 99, out _);
            BuildMenu(safe);
            BuildBar(safe);
            BuildResults();
        }

        private void BuildMenu(RectTransform safe)
        {
            var sheet = AuraViewer.ToolSheet(safe, "ChallengeSheet");
            AuraViewer.SheetHeader(sheet, "Reassembly challenge", out _bestText);
            AuraKit.Text(sheet, "The parts fly apart. Drag each one back to where it belongs; it snaps in when close. Drag empty space to turn the model, pinch to zoom.",
                         AuraTheme.SizeCaption, AuraTheme.TextDim);

            // Three-stop level slider.
            var holder = AuraKit.Rect(sheet, "Level");
            AuraKit.VStack(holder, 4f);
            _level = AuraWidgets.Slider(holder, 1f, v => ShowLevel(Mathf.RoundToInt(v)), null, 0f, 2f, 96f);
            _level.wholeNumbers = true;
            AddStops(_level);
            var labels = AuraKit.Rect(holder, "Labels");
            AuraKit.HStack(labels, 0f).childForceExpandWidth = true;
            AuraKit.Size(labels, -1, 44f);
            for (int i = 0; i < 3; i++)
            {
                var t = AuraKit.Text(labels, Names[i], AuraTheme.SizeSmall, AuraTheme.TextDim, AuraWeight.SemiBold,
                                     i == 0 ? TextAlignmentOptions.MidlineLeft : i == 1 ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineRight);
                AuraKit.Cell(t);
                _stopLabels[i] = t;
            }

            _levelText = AuraKit.Text(sheet, "", AuraTheme.SizeSmall, AuraTheme.Text);
            _levelText.richText = true;
            AuraKit.Size(_levelText, -1, 96f);

            _start = AuraKit.Pill(sheet, "Start", AuraIcons.PlayFill, AuraTheme.Lavender, AuraTheme.Ink,
                                  () => StartLevel((Difficulty)_chosen), AuraTheme.SlotHeight - 14f);
            _startFill = _start.GetComponent<Image>();
            _startLabel = _start.GetComponentInChildren<TextMeshProUGUI>();

            _menu = AuraViewer.SlideUp(sheet);
        }

        /// <summary>Three dots on the slider's track, one per level, under the knob.</summary>
        private static void AddStops(Slider s)
        {
            var root = (RectTransform)s.transform;
            var stops = AuraKit.Rect(root, "Stops");
            AuraKit.Fill(stops, 31, 0, 31, 0);
            stops.SetSiblingIndex(2); // over the track and fill, under the knob
            for (int i = 0; i < 3; i++)
            {
                var dot = AuraKit.Rect(stops, "Stop").gameObject.AddComponent<Image>();
                dot.sprite = AuraSprites.Disc;
                dot.color = AuraTheme.Text;
                dot.raycastTarget = false;
                AuraKit.Pin(dot.rectTransform, new Vector2(i * 0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26, 26));
            }
        }

        private void BuildBar(RectTransform safe)
        {
            var bar = AuraKit.Box(safe, "GameBar", AuraTheme.A(AuraTheme.Ink, 0.9f), 56f, true);
            AuraKit.Border(bar, AuraTheme.Slate, 56f);
            var rt = bar.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(AuraViewer.Gutter, -(AuraViewer.Gutter + 112f));
            rt.offsetMax = new Vector2(-AuraViewer.Gutter, -AuraViewer.Gutter);
            var h = AuraKit.HStack(bar, 12f);
            h.padding = new RectOffset(34, 12, 10, 10);
            h.childForceExpandHeight = true;
            _barText = AuraKit.Text(bar.transform, "", AuraTheme.SizeSmall, AuraTheme.Text);
            _barText.richText = true;
            _barText.enableWordWrapping = false;
            _barText.enableAutoSizing = true;
            _barText.fontSizeMin = 20f;
            _barText.fontSizeMax = AuraTheme.SizeSmall;
            AuraKit.Size(_barText, 0f, -1, 1f);
            AuraWidgets.Chip(bar.transform, "Hint", AuraIcons.Hint, true, () => _game?.Hint(), out _, 92f);
            AuraWidgets.Chip(bar.transform, "Quit", AuraIcons.Close, false, () => _game?.Quit(), out Button quit, 92f);
            var qt = quit.GetComponentInChildren<TextMeshProUGUI>();
            if (qt != null) qt.color = AuraTheme.Rose;
            var y = rt.anchoredPosition;
            _bar = AuraSlidePanel.Attach(rt, y, y + new Vector2(0, 160f));

            // A short toast for each fitted part, above Back.
            var toast = AuraKit.Box(safe, "Fitted", AuraTheme.A(AuraTheme.Ink, 0.9f), 40f);
            AuraKit.Border(toast, new Color(0.44f, 0.76f, 0.63f), 40f);
            AuraKit.Pin(toast.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                        new Vector2(0, AuraViewer.BottomReserve + 20f), new Vector2(760, 84));
            _toastText = AuraKit.Text(toast.transform, "", AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.SemiBold, TextAlignmentOptions.Center);
            AuraKit.Fill(_toastText.rectTransform, 24, 0, 24, 0);
            _toast = toast.gameObject;
            _toast.SetActive(false);
        }

        private void BuildResults()
        {
            var canvas = AuraKit.CreateCanvas(transform, "ChallengeResults", 101);
            _results = canvas.gameObject;
            _results.AddComponent<CanvasGroup>();
            var dim = AuraKit.Box(canvas.transform, "Dim", AuraTheme.A(AuraTheme.Ink, 0.6f), 0f, true);
            AuraKit.Fill(dim.rectTransform);
            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();

            var card = AuraKit.Box(safe, "Card", AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 8f);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.55f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            card.gameObject.AddComponent<MatchParentWidth>().max = 900f;
            var v = AuraKit.VStack(card, 22f, 0, TextAnchor.UpperCenter);
            v.padding = new RectOffset(44, 44, 44, 44);
            AuraKit.FitHeight(card);

            _resultTitle = AuraKit.Text(card.transform, "", AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold, TextAlignmentOptions.Center);

            var starRow = AuraKit.Rect(card.transform, "Stars");
            AuraKit.HStack(starRow, 18f, 0, TextAnchor.MiddleCenter);
            AuraKit.Size(starRow, -1, 110f);
            for (int i = 0; i < 3; i++) _stars[i] = AuraKit.Icon(starRow, AuraIcons.Star, 100f, AuraTheme.Lavender);

            var stats = AuraKit.EqualRow(card.transform, 150f, 16f);
            for (int i = 0; i < 3; i++)
            {
                var tile = AuraKit.Box(stats, "Stat", AuraTheme.Ink, AuraTheme.Radius - 6f);
                AuraKit.Cell(tile);
                var t = AuraKit.Text(tile.transform, "", AuraTheme.SizeTitle - 6f, AuraTheme.Text, AuraWeight.Regular, TextAlignmentOptions.Center);
                t.richText = true;
                AuraKit.Fill(t.rectTransform, 8, 8, 8, 8);
                _stats[i] = t;
            }

            _resultBest = AuraKit.Text(card.transform, "", AuraTheme.SizeSmall, AuraTheme.TextDim, AuraWeight.Regular, TextAlignmentOptions.Center);
            _resultBest.richText = true;

            var row = AuraKit.EqualRow(card.transform, AuraTheme.MinTouch, 16f);
            var again = AuraKit.Pill(row, "Play again", AuraIcons.Orbit, AuraTheme.Ink, AuraTheme.Lavender,
                                     () => { CloseResults(); OpenMenu(); }, AuraTheme.MinTouch, AuraTheme.Slate);
            AuraKit.Cell(again);
            _harder = AuraKit.Pill(row, "Harder level", AuraIcons.Trophy, AuraTheme.Lavender, AuraTheme.Ink, PlayHarder, AuraTheme.MinTouch);
            AuraKit.Cell(_harder);

            var back = AuraKit.Pill(safe, "Back", AuraIcons.Back, AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Text,
                                    CloseResults, AuraViewer.BackHeight, AuraTheme.Slate);
            AuraKit.Pin((RectTransform)back.transform, Vector2.zero, Vector2.zero,
                        new Vector2(AuraViewer.Gutter, AuraViewer.Gutter), new Vector2(270, AuraViewer.BackHeight));

            _results.SetActive(false);
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
