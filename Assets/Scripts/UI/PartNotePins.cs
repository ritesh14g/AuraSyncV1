using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AuraSync.Notes;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Draws part notes on the model (V3 Phase 5): a small diamond in the note's colour on its exact
    /// spot, with the first few words beside it. Tapping either opens the note.
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
            public Image           diamond;
            public GameObject      labelGo;
            public TextMeshProUGUI label;
            public CanvasGroup     group;
            public string          id;
        }

        private Canvas _canvas;
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
                p.diamond.color = n.Color;
                string t = (n.text ?? "").Replace("\n", " ");
                p.label.text = Esc(t.Length > LabelChars ? t.Substring(0, LabelChars).TrimEnd() + "..." : t);
            }

            var drop = new List<string>();
            foreach (var kv in _pins) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (string id in drop) { Destroy(_pins[id].go); _pins.Remove(id); }
        }

        // ── Every frame: where each pin goes ───────────────────────────────────

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

        // ── Placing banner ─────────────────────────────────────────────────────

        private void RefreshBanner()
        {
            bool placing = PartNoteManager.IsPlacing;
            _banner.SetActive(placing);
            if (placing) PanelNavigator.Push("note-placing", this, () => PartNoteManager.Instance?.StopPlacing());
            else PanelNavigator.Remove("note-placing");
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 94; // under the HUD (95): its buttons always win over a pin
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _pinLayer = new GameObject("Pins", typeof(RectTransform)).GetComponent<RectTransform>();
            _pinLayer.SetParent(transform, false);
            _pinLayer.anchorMin = Vector2.zero; _pinLayer.anchorMax = Vector2.one;
            _pinLayer.offsetMin = _pinLayer.offsetMax = Vector2.zero;

            BuildBanner();
        }

        private Pin MakePin(string id)
        {
            var go = new GameObject("NotePin", typeof(RectTransform));
            go.transform.SetParent(_pinLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(44f, 44f); // a finger-sized target around the small diamond
            var group = go.AddComponent<CanvasGroup>();

            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => OnPinPressed(id));

            // Dark edge, then the coloured diamond on top.
            Diamond(go.transform, "Edge", 30f, new Color(0.05f, 0.06f, 0.09f, 0.95f));
            Image diamond = Diamond(go.transform, "Fill", 22f, Color.yellow);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(2f, 0f);
            var bg = labelGo.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.09f, 0.82f);
            var lbtn = labelGo.AddComponent<Button>();
            lbtn.targetGraphic = bg;
            lbtn.transition = Selectable.Transition.None;
            lbtn.onClick.AddListener(() => OnPinPressed(id));
            var hg = labelGo.AddComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(10, 10, 4, 4);
            hg.childControlWidth = hg.childControlHeight = true;
            var fit = labelGo.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(labelGo.transform, false);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.fontSize = 19f;
            t.color = Color.white;
            t.enableWordWrapping = false;
            t.raycastTarget = false;

            return new Pin { go = go, rt = rt, diamond = diamond, labelGo = labelGo, label = t, group = group, id = id };
        }

        private static Image Diamond(Transform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private void BuildBanner()
        {
            _banner = new GameObject("PlacingBanner", typeof(RectTransform));
            _banner.transform.SetParent(transform, false);
            var rt = (RectTransform)_banner.transform;
            rt.anchorMin = new Vector2(0.15f, 0.765f);
            rt.anchorMax = new Vector2(0.98f, 0.835f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _banner.AddComponent<Image>().color = new Color(0.36f, 0.30f, 0.85f, 0.97f);
            _banner.AddComponent<TouchBlocker>();

            var hg = _banner.AddComponent<HorizontalLayoutGroup>();
            hg.padding = new RectOffset(20, 12, 10, 10);
            hg.spacing = 12;
            hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = hg.childControlHeight = true;
            hg.childForceExpandWidth = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_banner.transform, false);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.text = "Tap the spot on the model where the note goes.";
            t.fontSize = 24f;
            t.enableAutoSizing = true; t.fontSizeMin = 15f; t.fontSizeMax = 24f;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.raycastTarget = false;
            textGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var cancel = new GameObject("Cancel", typeof(RectTransform));
            cancel.transform.SetParent(_banner.transform, false);
            cancel.AddComponent<LayoutElement>().preferredWidth = 150f;
            var img = cancel.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.3f);
            var b = cancel.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => PartNoteManager.Instance?.StopPlacing());
            var ct = new GameObject("Label", typeof(RectTransform));
            ct.transform.SetParent(cancel.transform, false);
            var crt = (RectTransform)ct.transform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.offsetMin = crt.offsetMax = Vector2.zero;
            var ctext = ct.AddComponent<TextMeshProUGUI>();
            ctext.text = "CANCEL";
            ctext.fontSize = 22f;
            ctext.fontStyle = FontStyles.Bold;
            ctext.color = Color.white;
            ctext.alignment = TextAlignmentOptions.Center;
            ctext.raycastTarget = false;

            _banner.SetActive(false);
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
