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
    ///   * **PLAY** on the left rail - shown only off-line from a room, with a model loaded.
    ///   * **Level menu** - Easy / Medium / Hard, each with what it means and your best score.
    ///   * **Game bar** while playing - time, parts fitted, moves, HINT and QUIT, plus a one-line
    ///     message ("Fitted: Fan rotor (3 of 6)").
    ///   * **Results** - stars, time, moves, hints, mistakes, score, best.
    ///
    /// Back (on screen or the phone's) closes the menu or results, and quits a game in progress.
    /// </summary>
    public class ChallengeUI : MonoBehaviour
    {
        public static ChallengeUI Instance { get; private set; }

        private const string RailId = "play";
        private const string NavMenu = "challenge-menu";
        private const string NavGame = "challenge";
        private const string NavResult = "challenge-result";

        private static readonly Color CardColor   = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 0.95f);
        private static readonly Color PlayColor   = new Color(0.20f, 0.52f, 0.42f, 1f);
        private static readonly Color QuitColor   = new Color(0.45f, 0.20f, 0.22f, 1f);
        private static readonly Color StarOn      = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color StarOff     = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color DimText     = new Color(1f, 1f, 1f, 0.6f);

        private GameObject _menu;
        private readonly Button[] _levelButtons = new Button[3];
        private readonly TextMeshProUGUI[] _levelInfo = new TextMeshProUGUI[3];

        private GameObject _bar;
        private TextMeshProUGUI _barText;
        private TextMeshProUGUI _barMessage;

        private GameObject _results;
        private TextMeshProUGUI _resultTitle;
        private TextMeshProUGUI _resultBody;
        private readonly Image[] _stars = new Image[3];

        private ReassemblyGame _game;
        private bool _railShown;

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
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(RailId, "PLAY", OpenMenu);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);

            while (ReassemblyGame.Instance == null) yield return null;
            _game = ReassemblyGame.Instance;
            _game.OnStateChanged += OnGameChanged;
            _game.OnFinished += ShowResults;
        }

        void Update()
        {
            // PLAY only when there is a model, no room, and nothing of the game already on screen.
            bool show = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) && !PhotonNetwork.InRoom &&
                        !ReassemblyGame.IsActive && !_menu.activeSelf && !_results.activeSelf;
            if (show != _railShown && SpaceSessionHUD.Instance != null)
            {
                _railShown = show;
                SpaceSessionHUD.Instance.SetSlotVisible(RailId, show);
            }

            if (ReassemblyGame.IsActive) RefreshBar();
        }

        // ── Menu ───────────────────────────────────────────────────────────────

        private void OpenMenu()
        {
            if (_game == null) return;
            PartChatUI.Instance?.Close();
            ModelPartsDrawerUI.Instance?.SetOpen(false);

            for (int i = 0; i < 3; i++)
            {
                var d = (Difficulty)i;
                bool can = _game.CanStart(d, out string why);
                _levelButtons[i].interactable = can;
                int best = ReassemblyGame.BestScore(d);
                _levelInfo[i].text = (can ? LevelDescription(d) : $"<color=#E08A8A>{why}</color>") +
                                     (best > 0 ? $"\n<color=#FFD140>Best: {best}</color>" : "");
            }

            _menu.SetActive(true);
            PanelNavigator.Push(NavMenu, this, CloseMenu);
        }

        private void CloseMenu()
        {
            _menu.SetActive(false);
            PanelNavigator.Remove(NavMenu);
        }

        private static string LevelDescription(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:   return "The main parts only. A faint outline shows where each goes. Generous snapping.";
                case Difficulty.Medium: return "Every part. The outline still helps.";
                default:                return "Every part, in the real assembly order, with no outline. A part fitted too early bounces back.";
            }
        }

        private void StartLevel(Difficulty d)
        {
            CloseMenu();
            _game.Begin(d);
            if (ReassemblyGame.IsActive) PanelNavigator.Push(NavGame, this, () => _game.Quit());
        }

        // ── Game bar ───────────────────────────────────────────────────────────

        private void OnGameChanged()
        {
            bool active = ReassemblyGame.IsActive;
            _bar.SetActive(active);
            if (!active) PanelNavigator.Remove(NavGame);
            if (active) RefreshBar();
        }

        private void RefreshBar()
        {
            int s = Mathf.FloorToInt(_game.Elapsed);
            _barText.text = $"<b>{s / 60}:{s % 60:00}</b>    Fitted <b>{_game.Placed}/{_game.Total}</b>    Moves {_game.Moves}" +
                            (_game.Mode == Difficulty.Hard ? $"    Mistakes {_game.Mistakes}" : "");
            bool fresh = Time.unscaledTime - _game.MessageTime < 4f;
            _barMessage.text = fresh ? Esc(_game.Message) : "";
        }

        // ── Results ────────────────────────────────────────────────────────────

        private void ShowResults(ReassemblyGame.Result r)
        {
            _resultTitle.text = r.stars == 3 ? "Perfect rebuild!" : "Model rebuilt!";
            int s = Mathf.RoundToInt(r.seconds);
            _resultBody.text =
                $"{r.difficulty}, {r.pieces} parts\n" +
                $"Time  <b>{s / 60}:{s % 60:00}</b>  <color=#9AA4B8>(target {Mathf.RoundToInt(r.par)} s)</color>\n" +
                $"Moves  <b>{r.moves}</b>     Hints  <b>{r.hints}</b>" + (r.difficulty == Difficulty.Hard ? $"     Mistakes  <b>{r.mistakes}</b>" : "") + "\n" +
                $"Score  <b>{r.score}</b>   " + (r.newBest ? "<color=#FFD140>New best!</color>" : $"<color=#9AA4B8>Best {r.best}</color>");
            for (int i = 0; i < 3; i++) _stars[i].color = i < r.stars ? StarOn : StarOff;

            _results.SetActive(true);
            PanelNavigator.Push(NavResult, this, CloseResults);
        }

        private void CloseResults()
        {
            _results.SetActive(false);
            PanelNavigator.Remove(NavResult);
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 99; // above the chat (98), below the room panel (100)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            BuildMenu();
            BuildBar();
            BuildResults();
        }

        private void BuildMenu()
        {
            _menu = Backdrop("ChallengeMenu");
            GameObject card = Card(_menu.transform, 0.08f, 0.24f, 0.92f, 0.80f);

            Title(card.transform, "Reassembly challenge");
            Body(card.transform, "The parts fly apart. Drag each one back to where it belongs: it snaps in when it is close. Drag empty space to turn the model, pinch to zoom.", 20);

            string[] names = { "EASY", "MEDIUM", "HARD" };
            for (int i = 0; i < 3; i++)
            {
                var row = Row(card.transform, 120f);
                var d = (Difficulty)i;
                _levelButtons[i] = Btn(row.transform, names[i], PlayColor, () => StartLevel(d), 180f, 100f, 22);
                _levelInfo[i] = Body(row.transform, "", 18);
                Flex(_levelInfo[i].gameObject);
            }

            Btn(card.transform, "CANCEL", QuitColor, CloseMenu, 0f, 64f, 20);
            _menu.SetActive(false);
        }

        private void BuildBar()
        {
            _bar = new GameObject("ChallengeBar", typeof(RectTransform));
            _bar.transform.SetParent(transform, false);
            Anchor((RectTransform)_bar.transform, 0.03f, 0.79f, 0.97f, 0.905f);
            _bar.AddComponent<Image>().color = CardColor;
            _bar.AddComponent<TouchBlocker>();

            var vg = _bar.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 10, 10);
            vg.spacing = 6;
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var top = Row(_bar.transform, 64f);
            _barText = Body(top.transform, "", 22);
            Flex(_barText.gameObject);
            _barText.enableWordWrapping = false;
            _barText.alignment = TextAlignmentOptions.MidlineLeft;
            Btn(top.transform, "HINT", ButtonColor, () => _game?.Hint(), 120f, 60f, 20);
            Btn(top.transform, "QUIT", QuitColor, () => _game?.Quit(), 110f, 60f, 20);

            _barMessage = Body(_bar.transform, "", 19);
            _barMessage.color = new Color(1f, 0.88f, 0.5f, 1f);

            _bar.SetActive(false);
        }

        private void BuildResults()
        {
            _results = Backdrop("ChallengeResults");
            GameObject card = Card(_results.transform, 0.10f, 0.30f, 0.90f, 0.72f);

            _resultTitle = Title(card.transform, "");

            var starRow = Row(card.transform, 70f);
            starRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < 3; i++)
            {
                // Diamonds made from rotated squares: the default font has no star glyph.
                var holder = new GameObject("Star", typeof(RectTransform));
                holder.transform.SetParent(starRow.transform, false);
                var le = holder.AddComponent<LayoutElement>();
                le.preferredWidth = le.minWidth = 70f;
                var star = new GameObject("Diamond", typeof(RectTransform));
                star.transform.SetParent(holder.transform, false);
                var srt = (RectTransform)star.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
                srt.sizeDelta = new Vector2(40f, 40f);
                srt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                _stars[i] = star.AddComponent<Image>();
            }

            _resultBody = Body(card.transform, "", 21);

            var buttons = Row(card.transform, 70f);
            Btn(buttons.transform, "PLAY AGAIN", PlayColor, () => { CloseResults(); OpenMenu(); }, 0f, 66f, 20);
            Btn(buttons.transform, "DONE", ButtonColor, CloseResults, 0f, 66f, 20);
            foreach (Transform b in buttons.transform) Flex(b.gameObject);

            _results.SetActive(false);
        }

        // ── Small helpers ──────────────────────────────────────────────────────

        private GameObject Backdrop(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Anchor((RectTransform)go.transform, 0f, 0f, 1f, 1f);
            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            go.AddComponent<TouchBlocker>();
            return go;
        }

        private static GameObject Card(Transform parent, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject("Card", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Anchor((RectTransform)go.transform, x0, y0, x1, y1);
            go.AddComponent<Image>().color = CardColor;
            var vg = go.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(26, 26, 22, 22);
            vg.spacing = 14;
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            return go;
        }

        private static GameObject Row(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;
            var hg = go.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 14;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = true; hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;
            return go;
        }

        private static TextMeshProUGUI Title(Transform parent, string text)
        {
            TextMeshProUGUI t = Body(parent, text, 30);
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            return t;
        }

        private static TextMeshProUGUI Body(Transform parent, string text, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = Color.white;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        private static Button Btn(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float width, float height, float fontSize)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;
            if (width > 0f) le.minWidth = le.preferredWidth = width;

            var t = Body(go.transform, label, fontSize);
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            return btn;
        }

        private static void Flex(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        private static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
