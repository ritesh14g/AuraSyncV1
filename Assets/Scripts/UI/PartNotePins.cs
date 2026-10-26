using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Notes;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Draws part notes on the model (V3 Phase 5): a round dot in the note's colour on its exact
    /// spot, with the first few words in a small pill beside it. Tapping either opens the note.
    ///
    /// Pins are screen-space buttons placed every frame from the note's spot in its part's own
    /// coordinates, rather than objects inside the model. Nothing is added to the model's
    /// hierarchy, so part paths, the parts drawer, exploding and isolating never see them, and a
    /// pin cannot drift: wherever the part goes, the spot goes with it.
    ///
    /// A note on the far side of its part is drawn faint without its label; one on a part hidden by
    /// isolation, or hidden by this person, is not drawn. Also shows the "tap the spot" banner while
    /// a note is being placed.
    /// </summary>
    public class PartNotePins : MonoBehaviour
    {
        public static PartNotePins Instance { get; private set; }

        private const int LabelChars = 22;

        private class Pin
        {
            public GameObject      go;
            public RectTransform   rt;
            public Image           dot;
            public GameObject      labelGo;
            public TextMeshProUGUI label;
            public CanvasGroup     group;
            public string          id;
        }

        private RectTransform _pinLayer;
        private readonly Dictionary<string, Pin> _pins = new Dictionary<string, Pin>();
        private List<PartNote> _notes = new List<PartNote>();
        private readonly Dictionary<string, Renderer[]> _renderers = new Dictionary<string, Renderer[]>();
        private GameObject _banner;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            PartNoteManager.OnChanged += Refresh;
            PartNoteManager.OnPlacingChanged += RefreshBanner;
            SharedStateBus.OnChange += OnStateChange;
        }

        void OnDestroy()
        {
            PartNoteManager.OnChanged -= Refresh;
            PartNoteManager.OnPlacingChanged -= RefreshBanner;
            SharedStateBus.OnChange -= OnStateChange;
            PanelNavigator.Remove("note-placing");
            if (Instance == this) Instance = null;
        }

        private void OnStateChange(StateChange c)
        {
            if (c.key == StateKeys.ModelLoaded) _renderers.Clear(); // the old model's renderers are gone
        }

        private void Refresh()
        {
            _notes = PartNoteManager.Instance != null ? PartNoteManager.Instance.All() : new List<PartNote>();

            var keep = new HashSet<string>();
            foreach (PartNote n in _notes)
            {
                keep.Add(n.id);
                if (!_pins.TryGetValue(n.id, out Pin p)) { p = MakePin(n.id); _pins[n.id] = p; }
                p.dot.color = n.Color;
                string t = (n.text ?? "").Replace("\n", " ");
                p.label.text = Esc(t.Length > LabelChars ? t.Substring(0, LabelChars).TrimEnd() + "…" : t);
            }

            var drop = new List<string>();
            foreach (var kv in _pins) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (string id in drop) { Destroy(_pins[id].go); _pins.Remove(id); }
        }

        // ── Every frame: where each pin goes ───────────────────────────

        void LateUpdate()
        {
            Transform root = SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;
            Camera cam = Camera.main;
            bool on = root != null && cam != null &&
                      !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) &&
                      !AuraSync.Challenge.ReassemblyGame.IsActive;

            foreach (PartNote n in _notes)
            {
                if (!_pins.TryGetValue(n.id, out Pin p)) continue;
                bool show = on && !PartNoteManager.IsHidden(n) && Place(p, n, root, cam);
                if (p.go.activeSelf != show) p.go.SetActive(show);
            }
        }

        private bool Place(Pin p, PartNote n, Transform root, Camera cam)
        {
            Transform part = ModelRaycaster.FindPart(root, n.path);
            if (part == null || !PartVisible(n.path, part)) return false;

            Vector3 world = part.TransformPoint(n.LocalPoint);
            if (SectionCutter.Instance != null && SectionCutter.Instance.IsCut(world)) return false; // cut away
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f || screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height) return false;

            p.rt.position = new Vector3(screen.x, screen.y, 0f);

            // On the far side of the part: faint, and no label in the way of the near side's notes.
            Vector3 normal = part.TransformDirection(n.LocalNormal).normalized;
            bool facing = Vector3.Dot(normal, (cam.transform.position - world).normalized) > -0.1f;
            p.group.alpha = facing ? 1f : 0.35f;
            if (p.labelGo.activeSelf != facing) p.labelGo.SetActive(facing);
            return true;
        }

        /// <summary>False when isolation (or anything else) has switched every renderer of the part off.</summary>
        private bool PartVisible(string path, Transform part)
        {
            if (!_renderers.TryGetValue(path, out Renderer[] rs) || rs == null || rs.Length == 0 || rs[0] == null)
            {
                rs = part.GetComponentsInChildren<Renderer>(true);
                _renderers[path] = rs;
            }
            foreach (Renderer r in rs)
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.gameObject.name != ModelRaycaster.HighlightName)
                    return true;
            return false;
        }

        private void OnPinPressed(string id)
        {
            PartNote n = PartNoteManager.Instance != null ? PartNoteManager.Instance.Find(id) : null;
            if (n != null) PartNotesPanelUI.Instance?.ShowCard(n);
        }

        // ── Placing banner ─────────────────────────────────────────────

        private void RefreshBanner()
        {
            bool placing = PartNoteManager.IsPlacing;
            _banner.SetActive(placing);
            if (placing) PanelNavigator.Push("note-placing", this, () => PartNoteManager.Instance?.StopPlacing());
            else PanelNavigator.Remove("note-placing");
        }

        // ── Building ───────────────────────────────────────────────────

        private void Build()
        {
            // Under the HUD (95): its buttons always win over a pin.
            var canvas = AuraKit.CreateCanvas(transform, "NotePins", 94);
            _pinLayer = AuraKit.Fill(AuraKit.Rect(canvas.transform, "Pins"));

            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();
            BuildBanner(safe);
        }

        private Pin MakePin(string id)
        {
            var go = new GameObject("NotePin", typeof(RectTransform));
            go.transform.SetParent(_pinLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(84f, 84f); // a finger-sized target around the small dot
            var group = go.AddComponent<CanvasGroup>();

            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => OnPinPressed(id));

            // A dark ring, then the coloured dot inside it.
            Dot(go.transform, "Edge", 48f, AuraTheme.A(AuraTheme.Ink, 0.95f));
            Image dot = Dot(go.transform, "Fill", 32f, Color.yellow);

            var label = AuraKit.Box(go.transform, "Label", AuraTheme.A(AuraTheme.Ink, 0.88f), 26f, true);
            AuraKit.Border(label, AuraTheme.A(AuraTheme.Slate, 0.9f), 26f).raycastTarget = false;
            var labelGo = label.gameObject;
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(-12f, 0f);
            var lbtn = labelGo.AddComponent<Button>();
            lbtn.targetGraphic = label;
            lbtn.transition = Selectable.Transition.None;
            lbtn.onClick.AddListener(() => OnPinPressed(id));
            var hg = AuraKit.HStack(label, 0f, 0, TextAnchor.MiddleLeft);
            hg.padding = new RectOffset(22, 22, 10, 10);
            var fit = labelGo.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var t = AuraKit.Text(label.transform, "", AuraTheme.SizeLabel + 2f, AuraTheme.Text, AuraWeight.SemiBold);
            t.enableWordWrapping = false;
            t.raycastTarget = false;

            return new Pin { go = go, rt = rt, dot = dot, labelGo = labelGo, label = t, group = group, id = id };
        }

        private static Image Dot(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = AuraSprites.Disc;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>"Tap the spot" card, top centre under the model name, with Cancel.</summary>
        private void BuildBanner(RectTransform safe)
        {
            var card = AuraKit.Box(safe, "PlacingBanner", AuraTheme.A(AuraTheme.Indigo, 0.97f), AuraTheme.Radius + 6f, true);
            AuraKit.Border(card, AuraTheme.Lavender, AuraTheme.Radius + 6f);
            card.gameObject.AddComponent<TouchBlocker>();
            _banner = card.gameObject;
            var rt = card.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            float top = AuraViewer.TopReserve + 110f;
            rt.offsetMin = new Vector2(AuraViewer.Gutter + 60f, -top - 130f);
            rt.offsetMax = new Vector2(-(AuraViewer.Gutter + 60f), -top);
            var h = AuraKit.HStack(card, 20f, 0, TextAnchor.MiddleLeft);
            h.padding = new RectOffset(30, 20, 18, 18);

            AuraKit.Icon(card.transform, AuraIcons.Note, 56f, AuraTheme.Lavender);
            var t = AuraKit.Text(card.transform, "Tap the spot on the model where the note goes.", AuraTheme.SizeSmall, AuraTheme.Text);
            t.enableWordWrapping = true;
            AuraKit.Flex(t);
            AuraWidgets.Chip(card.transform, "Cancel", AuraIcons.Close, false, () => PartNoteManager.Instance?.StopPlacing(), out _, 92f);

            _banner.SetActive(false);
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
