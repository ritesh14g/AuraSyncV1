using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The "Preview quality" pill (V3 Phase 13), top centre, shown only while the light preview of
    /// the model is on screen:
    ///
    ///     PREVIEW QUALITY · full model 45%          downloading in the background
    ///     PREVIEW QUALITY · tap for full (12.8 MB)  on mobile data: waits for you
    ///     PREVIEW QUALITY · offline                 only the preview is on this phone
    ///
    /// Tapping it opens a card: GET FULL NOW, and SAVE DATA (on: always stay on the preview until
    /// you ask). Everything keeps working while the full model downloads and when it swaps in.
    /// </summary>
    public class ModelQualityUI : MonoBehaviour
    {
        public static ModelQualityUI Instance { get; private set; }

        private const string NavId = "quality";
        private static readonly Color PillColor   = new Color(0.85f, 0.55f, 0.10f, 0.95f);
        private static readonly Color PanelColor  = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color OnColor     = new Color(0.36f, 0.30f, 0.85f, 1f);

        private GameObject _pill, _card;
        private TextMeshProUGUI _pillText, _cardText, _saveText;
        private Image _saveImg;
        private GameObject _getFull;
        private float _next;

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
            if (ModelVariants.Current == ModelVariants.Stage.DownloadingFull) Refresh(); // the percentage
        }

        private void Refresh()
        {
            bool show = ModelVariants.ShowingPreview && !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey);
            if (_pill.activeSelf != show) _pill.SetActive(show);
            if (!show) { CloseCard(); return; }

            string size = ModelVariants.FullBytes > 0 ? $" ({ModelVariants.FullBytes / 1048576f:0.0} MB)" : "";
            string state;
            switch (ModelVariants.Current)
            {
                case ModelVariants.Stage.DownloadingFull: state = $"full model {ModelVariants.FullProgress * 100f:0}%"; break;
                case ModelVariants.Stage.Swapping:        state = "putting in the full model..."; break;
                default:
                    state = ModelVariants.Offline ? "offline"
                          : ModelVariants.Note.StartsWith("full download failed") ? "download failed, tap to retry"
                          : ModelVariants.SaveData ? $"saving data, tap for full{size}"
                          : $"tap for full{size}";
                    break;
            }
            _pillText.text = "PREVIEW QUALITY  ·  " + state;

            bool canGet = ModelVariants.Current == ModelVariants.Stage.Preview && !ModelVariants.Offline;
            _getFull.SetActive(canGet);
            _cardText.text = ModelVariants.Offline
                ? "You are offline. This is the light preview of the model; the full one downloads when you are back online and reopen it."
                : $"This is the light preview, so the class could start straight away. The full model{size} " +
                  (ModelVariants.Current == ModelVariants.Stage.DownloadingFull ? "is downloading and will swap in by itself."
                   : $"has not been downloaded ({ModelVariants.Note}).") +
                  " Everything keeps working when it swaps in.";
            _saveImg.color = ModelVariants.SaveData ? OnColor : ButtonColor;
            _saveText.text = ModelVariants.SaveData ? "SAVE DATA: ON" : "SAVE DATA: OFF";
        }

        private void ToggleCard()
        {
            if (_card.activeSelf) CloseCard();
            else
            {
                _card.SetActive(true);
                PanelNavigator.Push(NavId, this, CloseCard);
                Refresh();
            }
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
            Toast.Show(ModelVariants.SaveData
                ? "Save data on: models open as a light preview until you tap GET FULL."
                : "Save data off: the full model downloads by itself on Wi-Fi or a fast connection.");
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            UIBits.Canvas(gameObject, 96);

            _pill = new GameObject("QualityPill", typeof(RectTransform));
            _pill.transform.SetParent(transform, false);
            var prt = (RectTransform)_pill.transform;
            prt.anchorMin = new Vector2(0.16f, 0.795f);
            prt.anchorMax = new Vector2(0.70f, 0.83f);
            prt.offsetMin = prt.offsetMax = Vector2.zero;
            var img = _pill.AddComponent<Image>();
            img.color = PillColor;
            var b = _pill.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(ToggleCard);
            _pillText = UIBits.Text(_pill.transform, "", 19f, Color.white);
            UIBits.Stretch(_pillText.rectTransform);
            _pillText.alignment = TextAlignmentOptions.Center;
            _pillText.fontStyle = FontStyles.Bold;
            _pillText.enableAutoSizing = true; _pillText.fontSizeMin = 12f; _pillText.fontSizeMax = 19f;
            _pill.SetActive(false);

            _card = new GameObject("QualityCard", typeof(RectTransform));
            _card.transform.SetParent(transform, false);
            var crt = (RectTransform)_card.transform;
            crt.anchorMin = new Vector2(0.16f, 0.79f);
            crt.anchorMax = new Vector2(0.84f, 0.79f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            _card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _card.AddComponent<Image>().color = PanelColor;
            _card.AddComponent<TouchBlocker>();
            var vg = _card.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 12, 14);
            vg.spacing = 10;
            vg.childControlWidth = vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            _cardText = UIBits.Text(_card.transform, "", 18f, Color.white);
            _cardText.enableWordWrapping = true;
            var row = UIBits.Row(_card.transform, 60f);
            _getFull = UIBits.Flex(UIBits.Button(row.transform, "GET FULL NOW", OnColor, GetFull, 100f));
            GameObject save = UIBits.Flex(UIBits.Button(row.transform, "SAVE DATA: OFF", ButtonColor, ToggleSaveData, 100f));
            _saveImg = save.GetComponent<Image>();
            _saveText = save.GetComponentInChildren<TextMeshProUGUI>();
            var row2 = UIBits.Row(_card.transform, 52f);
            UIBits.Flex(UIBits.Button(row2.transform, "CLOSE", ButtonColor, CloseCard, 100f));
            _card.SetActive(false);
        }
    }
}
