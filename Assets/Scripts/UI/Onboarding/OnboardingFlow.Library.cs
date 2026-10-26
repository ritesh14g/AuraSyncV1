using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Interactive mode: the three-option hub and Available models (two columns of five on screen,
    /// scrolling for more; tap to open, long-press for Delete on your own scans).
    /// </summary>
    public partial class OnboardingFlow
    {
        // ── Hub ────────────────────────────────────────────────────────

        private View BuildHub()
        {
            var v = NewView("hub", Mode.Interactive, 0.55f);
            var card = AuraKit.Card(v.root, "Interactive mode", "Study real 3D models, alone or together", out var body);

            AuraKit.ListRow(body, AuraIcons.Scan, "Make model", "Turn a real object into 3D",
                            () => Push(BuildScanTips()), out _);
            AuraKit.ListRow(body, AuraIcons.Library, "Available models", "Open a model to study",
                            () => Push(BuildLibrary(null)), out TextMeshProUGUI countText);
            AuraKit.ListRow(body, AuraIcons.Join, "Join room", "Enter your presenter's code",
                            () => Push(BuildJoin()), out _);
            AuraKit.ListRow(body, AuraIcons.Record, "Recordings", "Replay or import a recorded session",
                            () => Push(BuildRecordings()), out _);
            CentreCard(card);

            v.onShow = () =>
            {
                if (ModelCatalog.Instance == null) return;
                ModelCatalog.Instance.GetAllModels(all =>
                {
                    if (countText == null) return;
                    int n = all != null ? all.Count : 0;
                    countText.text = n == 1 ? "1 model ready to open" : $"{n} models ready to open";
                });
            };
            return v;
        }

        // ── Available models ───────────────────────────────────────────

        private View BuildLibrary(string highlightId)
        {
            var v = NewView("library", Mode.Interactive, 1f);
            var col = PageColumn(v);

            // Header (its own row under the chip): title and count on the left, Reports on the right.
            var head = AuraKit.Rect(col, "Header");
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1); head.pivot = new Vector2(0.5f, 1);
            head.offsetMin = new Vector2(0, -AuraTheme.ChipSize); head.offsetMax = Vector2.zero;
            AuraKit.HStack(head, 20f).childForceExpandHeight = false;

            var words = AuraKit.Rect(head, "Words");
            AuraKit.VStack(words, 2f, 0, TextAnchor.MiddleLeft);
            AuraKit.Flex(words);
            var title = AuraKit.Text(words, "Available models", AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold);
            title.enableWordWrapping = false;
            var count = AuraKit.Caps(words, "Loading…");

            var reports = AuraKit.Pill(head, "Reports", AuraIcons.Reports, AuraTheme.Ink, AuraTheme.Lavender,
                                       () => ReportsUI.Open(), AuraTheme.MinTouch, AuraTheme.Slate);
            var rle = reports.GetComponent<LayoutElement>();
            rle.preferredWidth = 290f;
            ((HorizontalLayoutGroup)reports.GetComponent<HorizontalLayoutGroup>()).padding = new RectOffset(22, 22, 0, 0);

            // Grid area.
            var area = AuraKit.Rect(col, "GridArea");
            AuraKit.Fill(area, 0, 0, 0, AuraTheme.ChipSize + 40f);

            var view = AuraKit.Fill(AuraKit.Rect(area, "Viewport"));
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            var content = AuraKit.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.spacing = new Vector2(AuraTheme.Gap, AuraTheme.Gap);
            grid.childAlignment = TextAnchor.UpperLeft;
            AuraKit.FitHeight(content);

            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;

            var fit = area.gameObject.AddComponent<LibraryGridFit>();
            fit.grid = grid;
            fit.viewport = view;

            var empty = AuraKit.Text(area, "", AuraTheme.SizeBody, AuraTheme.TextDim, AuraWeight.Regular,
                                     TextAlignmentOptions.Center);
            AuraKit.Fill(empty.rectTransform, 40, 0, 40, 0);

            void Rebuild()
            {
                if (ModelCatalog.Instance == null)
                {
                    count.text = "UNAVAILABLE";
                    empty.text = "The model list isn't available on this screen.";
                    return;
                }
                ModelCatalog.Instance.GetAllModels(all =>
                {
                    if (content == null) return;
                    foreach (Transform c in content) Destroy(c.gameObject);
                    fit.previews.Clear();

                    int n = all != null ? all.Count : 0;
                    count.text = n == 1 ? "1 MODEL" : $"{n} MODELS";
                    empty.text = n == 0
                        ? "No models yet.\nMake one from a real object with Make model."
                        : "";
                    if (all == null) return;
                    foreach (var m in all) ModelTile(content, fit, m, m.id == highlightId, Rebuild);
                    if (!string.IsNullOrEmpty(highlightId)) StartCoroutine(ScrollToEnd(scroll));
                });
            }

            v.onShow = Rebuild;
            return v;
        }

        private void ModelTile(RectTransform parent, LibraryGridFit fit, ModelData m, bool highlight,
                               System.Action rebuild)
        {
            var box = AuraKit.Box(parent, "Model_" + m.name, AuraTheme.A(AuraTheme.Indigo, 0.92f), AuraTheme.Radius - 6f, true);
            AuraKit.Border(box, highlight ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Slate, 0.75f), AuraTheme.Radius - 6f);
            var h = AuraKit.HStack(box, 14f, 0, TextAnchor.MiddleLeft);
            h.padding = new RectOffset(28, 18, 18, 18);

            var words = AuraKit.Rect(box.transform, "Words");
            AuraKit.VStack(words, 4f, 0, TextAnchor.MiddleLeft);
            AuraKit.Size(words, 0f, -1, 1f);
            var name = AuraKit.Text(words, m.name, AuraTheme.SizeSmall + 2f, AuraTheme.Text, AuraWeight.SemiBold);
            name.enableWordWrapping = false;
            bool isScan = IsScan(m);
            string sub = highlight ? "Your scan · new" : (isScan ? "Your scan" : (m.description ?? ""));
            var subText = AuraKit.Text(words, sub, AuraTheme.SizeCaption - 2f, highlight ? AuraTheme.Lavender : AuraTheme.TextDim);
            subText.enableWordWrapping = false;

            // Preview: rendered thumbnail when there is one, an icon until then.
            var pv = AuraKit.Box(box.transform, "Preview", AuraTheme.Ink, 22f);
            pv.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var pvLe = AuraKit.Size(pv, 120f, 120f);
            fit.previews.Add(pvLe);
            var icon = AuraKit.Icon(pv.transform, AuraIcons.ForModelName(m.name, isScan), 64f, AuraTheme.Lavender);
            icon.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64, 64));
            var raw = AuraKit.Rect(pv.transform, "Thumb").gameObject.AddComponent<RawImage>();
            AuraKit.Fill(raw.rectTransform);
            raw.raycastTarget = false;
            raw.enabled = false;

            void Show(Texture2D tex)
            {
                if (raw == null || tex == null) return;
                raw.texture = tex;
                raw.enabled = true;
                icon.enabled = false;
            }
            Texture2D have = ModelThumbnails.TryGet(ModelThumbnails.PictureId(m));
            if (have != null) Show(have);
            else ModelThumbnails.Request(m, Show);

            AuraKit.MakeButton(box.gameObject, box, () => OpenModel(m));
            var lp = box.gameObject.AddComponent<AuraLongPress>();
            lp.onLongPress = () => Push(BuildModelActions(m, rebuild));
        }

        /// <summary>New scans are added last; bring them into view once the grid has laid out.</summary>
        private static System.Collections.IEnumerator ScrollToEnd(ScrollRect scroll)
        {
            yield return null;
            yield return null;
            if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        }

        private static bool IsScan(ModelData m) => m != null && !string.IsNullOrEmpty(m.id) && m.id.StartsWith("local-");

        /// <summary>Long-press menu for one model: Open, and Delete for your own scans.</summary>
        private View BuildModelActions(ModelData m, System.Action rebuild)
        {
            bool isScan = IsScan(m);
            var v = NewView("library.actions", Mode.Interactive, 0.85f);
            v.hideChip = true;
            var card = AuraKit.Card(v.root, m.name, isScan ? "Your scan" : "Built-in models can't be deleted.", out var body);

            AuraKit.ListRow(body, AuraIcons.Open, "Open", "Study it in 3D", () => OpenModel(m), out _);
            if (isScan)
            {
                AuraKit.ListRow(body, AuraIcons.Trash, "Delete", "Remove it from this phone", () =>
                {
                    Confirm($"Delete {m.name}?", "This removes the scan from this phone. It can't be undone.",
                            "Delete", true, () => DeleteModel(m, rebuild));
                }, out _);
            }
            CentreCard(card);
            return v;
        }

        private void DeleteModel(ModelData m, System.Action rebuild)
        {
            if (ModelCatalog.Instance == null) return;
            ModelCatalog.Instance.DeleteModel(m.id, ok =>
            {
                if (!ok) { Toast.Show("Couldn't delete that model. Try again."); return; }

                // Another scan may point at the same file (mock scans all do), so only free the
                // download and the picture when nothing else uses that URL.
                ModelCatalog.Instance.GetAllModels(all =>
                {
                    bool stillUsed = false;
                    if (all != null) foreach (var o in all) if (o != null && o.glbUrl == m.glbUrl) { stillUsed = true; break; }
                    if (!stillUsed && IsScan(m))
                    {
                        if (PlayerPrefs.GetString("SelectedModelFile", "") == m.glbUrl)
                        {
                            PlayerPrefs.DeleteKey("SelectedModelFile");
                            PlayerPrefs.Save();
                        }
                    }
                });

                Toast.Show($"Deleted {m.name}");
                // Back to the list (the actions card is under the confirmation that just closed);
                // the list rebuilds itself when it shows again.
                if (Top != null && Top.id == "library.actions") Pop();
                else rebuild?.Invoke();
            });
        }

        private void OpenModel(ModelData m)
        {
            if (m == null || string.IsNullOrEmpty(m.glbUrl)) return;
            PlayerPrefs.SetString("SelectedModelFile", m.glbUrl);
            PlayerPrefs.Save();
            Toast.Show($"Opening {m.name}…");
            SceneManager.LoadScene(SpaceSceneName);
        }
    }

    /// <summary>
    /// Sizes the model grid so five rows fill the visible area (ten models on screen), with a sensible
    /// minimum and maximum tile height, and keeps each tile's preview square.
    /// </summary>
    public class LibraryGridFit : MonoBehaviour
    {
        public GridLayoutGroup grid;
        public RectTransform viewport;
        public readonly List<LayoutElement> previews = new List<LayoutElement>();
        private Vector2 _last;
        private int _lastCount = -1;

        void LateUpdate()
        {
            if (grid == null || viewport == null) return;
            Vector2 size = viewport.rect.size;
            if (size == _last && previews.Count == _lastCount) return;
            _last = size;
            _lastCount = previews.Count;

            float gap = grid.spacing.x;
            float w = Mathf.Floor((size.x - gap) / 2f);
            float h = Mathf.Clamp(Mathf.Floor((size.y - 4f * grid.spacing.y) / 5f), 160f, 300f);
            grid.cellSize = new Vector2(Mathf.Max(100f, w), h);

            float p = Mathf.Clamp(h - 36f, 90f, 200f);
            foreach (var le in previews)
            {
                if (le == null) continue;
                le.minWidth = le.preferredWidth = p;
                le.minHeight = le.preferredHeight = p;
            }
        }
    }
}
