using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The "Preview quality" pill (V3 Phase 13), small and top centre under the model name, shown
    /// only while the light preview of the model is on screen:
    ///
    ///     (o) Preview · full model 45%          downloading in the background
    ///     (o) Preview · tap for full (12.8 MB)  on mobile data: waits for you
    ///     (o) Preview · offline                 only the preview is on this phone
    ///
    /// Tapping it opens a small card under it: Get full now, and Save data (on: always stay on the
    /// preview until you ask). Everything keeps working while the full model downloads and when it
    /// swaps in. The pill moves down a row while the "Isolated" chip is showing, and steps aside
    /// while a find-task banner is up.
    /// </summary>
    public class ModelQualityUI : MonoBehaviour
    {
        public static ModelQualityUI Instance { get; private set; }

        private const string NavId = "quality";
        private const float PillHeight = 76f;
        private static readonly Color Amber = new Color(0.95f, 0.70f, 0.32f);

        private RectTransform _pillRt;
        private GameObject _pill, _card;
        private RectTransform _cardRt;
        private TextMeshProUGUI _pillText, _cardText;
        private System.Action<bool> _paintSave;
        private GameObject _getFull;
        private float _next;
        private float _y = float.NaN;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            ModelVariants.OnChanged += Refresh;
        }

        void OnDestroy()
        {
            ModelVariants.OnChanged -= Refresh;
            PanelNavigator.Remove(NavId);
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            Refresh(); // the percentage, and where the pill sits
        }

        private void Refresh()
        {
            bool banner = AttentionUI.Instance != null && AttentionUI.Instance.BannerShown;
            bool show = ModelVariants.ShowingPreview && !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) && !banner;
            if (_pill.activeSelf != show) _pill.SetActive(show);
            if (!show) { CloseCard(); return; }

            // One row lower while the "Isolated" chip takes this spot.
            bool iso = ModelIsolator.Instance != null && ModelIsolator.Instance.IsActive;
            float y = -(AuraViewer.TopReserve + 110f + (iso ? 98f : 0f));
            if (y != _y)
            {
                _y = y;
                _pillRt.anchoredPosition = new Vector2(0, y);
                _cardRt.anchoredPosition = new Vector2(0, y - PillHeight - 14f);
            }

            string size = ModelVariants.FullBytes > 0 ? $" ({ModelVariants.FullBytes / 1048576f:0.0} MB)" : "";
            string state;
            switch (ModelVariants.Current)
            {
                case ModelVariants.Stage.DownloadingFull: state = $"full model {ModelVariants.FullProgress * 100f:0}%"; break;
                case ModelVariants.Stage.Swapping:        state = "putting in the full model…"; break;
                default:
                    state = ModelVariants.Offline ? "offline"
                          : ModelVariants.Note.StartsWith("full download failed") ? "download failed, tap to retry"
                          : ModelVariants.SaveData ? $"saving data, tap for full{size}"
                          : $"tap for full{size}";
                    break;
            }
            _pillText.text = "<b>Preview</b>  ·  " + state;

            if (!_card.activeSelf) return;
            bool canGet = ModelVariants.Current == ModelVariants.Stage.Preview && !ModelVariants.Offline;
            _getFull.SetActive(canGet);
            _cardText.text = ModelVariants.Offline
                ? "You are offline. This is the light preview of the model; the full one downloads when you are back online and reopen it."
                : $"This is the light preview, so the class could start straight away. The full model{size} " +
                  (ModelVariants.Current == ModelVariants.Stage.DownloadingFull ? "is downloading and will swap in by itself."
                   : $"has not been downloaded ({ModelVariants.Note}).") +
                  " Everything keeps working when it swaps in.";
            _paintSave(ModelVariants.SaveData);
        }

        private void ToggleCard()
        {
            if (_card.activeSelf) { CloseCard(); return; }
            _card.SetActive(true);
            var g = _card.GetComponent<CanvasGroup>();
            g.alpha = 0f;
            AuraMotion.Fade(g, 1f, 0.18f);
            PanelNavigator.Push(NavId, this, CloseCard);
            Refresh();
        }

        private void CloseCard()
        {
            if (_card == null || !_card.activeSelf) return;
            _card.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        private void GetFull()
        {
            ModelVariants.RequestFull?.Invoke();
            CloseCard();
        }

        private void ToggleSaveData()
        {
            ModelVariants.SaveData = !ModelVariants.SaveData;
            _paintSave(ModelVariants.SaveData);
            Toast.Show(ModelVariants.SaveData
                ? "Save data on: models open as a light preview until you tap Get full."
                : "Save data off: the full model downloads by itself on Wi-Fi or a fast connection.");
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            var safe = AuraViewer.CreateCanvas(transform, "QualityCanvas", 96, out _);

            // The pill: as wide as its words.
            var pill = AuraKit.Box(safe, "QualityPill", AuraTheme.A(AuraTheme.Ink, 0.9f), PillHeight / 2f, true);
            AuraKit.Border(pill, AuraTheme.A(Amber, 0.85f), PillHeight / 2f);
            _pill = pill.gameObject;
            _pillRt = pill.rectTransform;
            _pillRt.anchorMin = _pillRt.anchorMax = new Vector2(0.5f, 1f);
            _pillRt.pivot = new Vector2(0.5f, 1f);
            _pillRt.sizeDelta = new Vector2(400f, PillHeight);
            var h = AuraKit.HStack(pill, 12f, 0, TextAnchor.MiddleCenter);
            h.padding = new RectOffset(24, 30, 0, 0);
            h.childForceExpandHeight = false;
            var fit = _pill.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            AuraKit.Icon(pill.transform, AuraIcons.Cube, 38f, Amber);
            _pillText = AuraKit.Text(pill.transform, "", AuraTheme.SizeCaption, AuraTheme.Text);
            _pillText.enableWordWrapping = false;
            _pillText.richText = true;
            AuraKit.MakeButton(_pill, pill, ToggleCard);
            _pill.SetActive(false);

            // The card under it.
            var card = AuraKit.Box(safe, "QualityCard", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 4f, true);
            AuraKit.Border(card, AuraTheme.Slate, AuraTheme.Radius + 4f);
            card.gameObject.AddComponent<TouchBlocker>();
            card.gameObject.AddComponent<CanvasGroup>();
            _card = card.gameObject;
            _cardRt = card.rectTransform;
            _cardRt.anchorMin = _cardRt.anchorMax = new Vector2(0.5f, 1f);
            _cardRt.pivot = new Vector2(0.5f, 1f);
            _cardRt.sizeDelta = new Vector2(820f, 300f);
            _card.AddComponent<MatchParentWidth>().max = 820f;
            var v = AuraKit.VStack(card, 18f);
            v.padding = new RectOffset(34, 34, 30, 30);
            AuraKit.FitHeight(card);

            var head = AuraKit.Rect(card.transform, "Head");
            AuraKit.HStack(head, 12f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(head, -1, 84f);
            var title = AuraKit.Text(head, "Preview quality", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.Bold);
            AuraKit.Flex(title);
            AuraWidgets.Round(head, AuraIcons.Close, 80f, false, CloseCard, out _);

            _cardText = AuraKit.Text(card.transform, "", AuraTheme.SizeCaption, AuraTheme.TextDim);
            _cardText.enableWordWrapping = true;

            var row = AuraKit.Rect(card.transform, "Buttons");
            AuraKit.HStack(row, 14f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(row, -1, 96f);
            var get = AuraKit.Pill(row, "Get full now", AuraIcons.Forward, AuraTheme.Lavender, AuraTheme.Ink, GetFull, 96f);
            _getFull = get.gameObject;
            _paintSave = AuraWidgets.Chip(row, "Save data", AuraIcons.Wind, false, ToggleSaveData, out _, 96f);
            AuraKit.Flex(AuraKit.Rect(row, "Space"));

            _card.SetActive(false);
        }
    }
}
