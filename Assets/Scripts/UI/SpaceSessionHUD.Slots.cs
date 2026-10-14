using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Button slots for the V3 features, so each feature adds its own button instead of every one
    /// of them editing the HUD's layout.
    ///
    ///   [FEATURE RAIL]   Left edge, a column of square buttons: AI, Simulate, Section, Notes,
    ///                    attention eye. On the left because the right edge already holds the
    ///                    player badges. The parts drawer opens to the right of it.
    ///
    ///   [CORNER ROW]     Top right, under the Share button: Settings and Reports. A slot can show
    ///                    a red count badge (Reports: unseen reports).
    ///
    /// A feature calls <see cref="AddRailButton"/> or <see cref="AddCornerButton"/> once, then
    /// <see cref="SetSlotVisible"/> to show or hide it. Buttons a user may not use are hidden, not
    /// greyed out. Hidden buttons take no space: the rail closes up around them.
    ///
    /// Nothing adds a slot in Phase 0. To see the layout, use the component's context menu (the
    /// three dots on SpaceSessionHUD in the Inspector, in Play mode): "V3 debug: toggle placeholder
    /// buttons". The placeholders open stackable test panels for checking Back.
    /// </summary>
    public partial class SpaceSessionHUD
    {
        private static readonly Color SlotColor        = new Color(0.22f, 0.26f, 0.36f, 0.95f);
        private static readonly Color SlotHighlight    = new Color(0.32f, 0.38f, 0.50f);
        private static readonly Color SlotPressed      = new Color(0.16f, 0.19f, 0.26f);
        private static readonly Color CountBadgeColor  = new Color(0.86f, 0.18f, 0.18f, 1f);

        private RectTransform _rail;
        private RectTransform _cornerRow;

        private class Slot
        {
            public Button          button;
            public TextMeshProUGUI label;
            public GameObject      countBadge;
            public TextMeshProUGUI countText;
        }
        private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>();

        // ── Build ──────────────────────────────────────────────────────────────

        private void BuildSlots()
        {
            _rail = new GameObject("FeatureRail").AddComponent<RectTransform>();
            _rail.SetParent(transform, false);
            _rail.anchorMin = new Vector2(0.02f, 0.30f);
            _rail.anchorMax = new Vector2(0.14f, 0.80f);
            _rail.offsetMin = Vector2.zero;
            _rail.offsetMax = Vector2.zero;

            VerticalLayoutGroup vg = _rail.gameObject.AddComponent<VerticalLayoutGroup>();
            vg.spacing                = 14;
            vg.childAlignment         = TextAnchor.UpperCenter;
            vg.childControlWidth      = true;
            vg.childControlHeight     = true;
            vg.childForceExpandWidth  = true;
            vg.childForceExpandHeight = false;

            _cornerRow = new GameObject("CornerRow").AddComponent<RectTransform>();
            _cornerRow.SetParent(transform, false);
            _cornerRow.anchorMin = new Vector2(0.68f, 0.85f);
            _cornerRow.anchorMax = new Vector2(0.98f, 0.905f);
            _cornerRow.offsetMin = Vector2.zero;
            _cornerRow.offsetMax = Vector2.zero;

            HorizontalLayoutGroup hg = _cornerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hg.spacing                = 10;
            hg.childAlignment         = TextAnchor.MiddleRight;
            hg.childControlWidth      = true;
            hg.childControlHeight     = true;
            hg.childForceExpandWidth  = false;
            hg.childForceExpandHeight = true;
        }

        // ── Public API for features ────────────────────────────────────────────

        /// <summary>Adds a square button to the left rail. Visible straight away; hide it with <see cref="SetSlotVisible"/>.</summary>
        public Button AddRailButton(string id, string label, Action onClick)
        {
            Slot s = MakeSlot(_rail, id, label, onClick);
            if (s == null) return null;

            LayoutElement le = s.button.gameObject.AddComponent<LayoutElement>();
            le.minHeight       = 110f;
            le.preferredHeight = 110f;
            return s.button;
        }

        /// <summary>Adds a button to the top-right corner row. Visible straight away.</summary>
        public Button AddCornerButton(string id, string label, Action onClick)
        {
            Slot s = MakeSlot(_cornerRow, id, label, onClick);
            if (s == null) return null;

            // 100 wide: REC, SETTINGS and REPORTS fit the row together (the labels shrink to fit).
            LayoutElement le = s.button.gameObject.AddComponent<LayoutElement>();
            le.minWidth       = 100f;
            le.preferredWidth = 100f;
            return s.button;
        }

        public bool HasSlot(string id) => _slots.ContainsKey(id);

        /// <summary>Hides the whole feature rail (a replay drives the model; Phase 9).</summary>
        public void SetRailVisible(bool visible)
        {
            if (_rail != null && _rail.gameObject.activeSelf != visible) _rail.gameObject.SetActive(visible);
        }

        public void SetSlotVisible(string id, bool visible)
        {
            if (_slots.TryGetValue(id, out Slot s) && s.button != null)
                s.button.gameObject.SetActive(visible);
        }

        public void SetSlotLabel(string id, string label)
        {
            if (_slots.TryGetValue(id, out Slot s) && s.label != null) s.label.text = label;
        }

        /// <summary>Shows a red count badge on the slot; 0 hides it. Counts above 9 read "9+".</summary>
        public void SetSlotCount(string id, int count)
        {
            if (!_slots.TryGetValue(id, out Slot s) || s.countBadge == null) return;

            s.countBadge.SetActive(count > 0);
            s.countText.text = count > 9 ? "9+" : count.ToString();
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private Slot MakeSlot(Transform parent, string id, string label, Action onClick)
        {
            if (parent == null || string.IsNullOrEmpty(id)) return null;
            if (_slots.ContainsKey(id))
            {
                Debug.LogWarning($"[SpaceSessionHUD] Slot '{id}' already exists; not adding it twice.");
                return null;
            }

            GameObject go = new GameObject("Slot_" + id);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            Image bg = go.AddComponent<Image>();
            bg.color = SlotColor;

            Button btn = go.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            ColorBlock cb = btn.colors;
            cb.highlightedColor = SlotHighlight;
            cb.pressedColor     = SlotPressed;
            btn.colors = cb;

            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 4f); lrt.offsetMax = new Vector2(-4f, -4f);

            TextMeshProUGUI tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text               = label;
            tmp.enableAutoSizing   = true;   // "SIMULATE" has to fit a narrow rail button
            tmp.fontSizeMin        = 13;
            tmp.fontSizeMax        = 21;
            tmp.fontStyle          = FontStyles.Bold;
            tmp.color              = Color.white;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget      = false;

            // Red count badge in the top-right corner, hidden until a count is set.
            GameObject badge = new GameObject("Count");
            badge.transform.SetParent(go.transform, false);
            RectTransform brt = badge.AddComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot     = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(40f, 40f);
            brt.anchoredPosition = new Vector2(-6f, -6f);

            Image bimg = badge.AddComponent<Image>();
            bimg.color = CountBadgeColor;
            bimg.raycastTarget = false;
            badge.AddComponent<LayoutElement>().ignoreLayout = true;

            GameObject ctGo = new GameObject("Text");
            ctGo.transform.SetParent(badge.transform, false);
            RectTransform crt = ctGo.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one; crt.sizeDelta = Vector2.zero;

            TextMeshProUGUI ct = ctGo.AddComponent<TextMeshProUGUI>();
            ct.fontSize      = 20;
            ct.fontStyle     = FontStyles.Bold;
            ct.color         = Color.white;
            ct.alignment     = TextAlignmentOptions.Center;
            ct.raycastTarget = false;

            badge.SetActive(false);

            var slot = new Slot { button = btn, label = tmp, countBadge = badge, countText = ct };
            _slots[id] = slot;
            return slot;
        }

        // ── Phase 0 debug: placeholders and test panels ────────────────────────

        private static readonly string[] PlaceholderRail   = { "AI", "SIMULATE", "SECTION", "NOTES", "VIEWS" };
        private static readonly string[] PlaceholderCorner = { "SETTINGS", "REPORTS" };

        private bool          _placeholdersOn;
        private RectTransform _debugLayer;
        private int           _debugSerial;

        [ContextMenu("V3 debug: toggle placeholder buttons")]
        private void TogglePlaceholders()
        {
            _placeholdersOn = !_placeholdersOn;

            foreach (string label in PlaceholderRail)
            {
                string id = "debug-" + label;
                if (!HasSlot(id)) AddRailButton(id, label, () => OpenTestPanel(label));
                SetSlotVisible(id, _placeholdersOn);
            }
            foreach (string label in PlaceholderCorner)
            {
                string id = "debug-" + label;
                if (!HasSlot(id)) AddCornerButton(id, label, () => OpenTestPanel(label));
                SetSlotVisible(id, _placeholdersOn);
            }
            SetSlotCount("debug-REPORTS", 1);

            Debug.Log($"[SpaceSessionHUD] V3 placeholder buttons {(_placeholdersOn ? "shown" : "hidden")}.");
        }

        [ContextMenu("V3 debug: toggle state-bus log")]
        private void ToggleStateBusLog()
        {
            SharedStateBus.LogToConsole = !SharedStateBus.LogToConsole;
            Debug.Log($"[SpaceSessionHUD] State-bus logging {(SharedStateBus.LogToConsole ? "ON" : "OFF")}.");
        }

        [ContextMenu("V3 debug: log part knowledge")]
        private void LogPartKnowledge()
        {
            var pk = AuraSync.AI.PartKnowledge.Instance;
            if (pk == null) { Debug.Log("[SpaceSessionHUD] Part knowledge: not created yet (no model loaded)."); return; }

            var sb = new System.Text.StringBuilder($"[SpaceSessionHUD] Part knowledge: {pk.Status}");
            if (pk.Current != null)
            {
                sb.Append($"\n  context: {pk.Current.context}\n  summary: {pk.Current.summary}");
                foreach (var e in pk.AllParts)
                    sb.Append($"\n  {e.path,-10} {e.node,-12} {e.label} | motion {e.motion.type} | step {e.assemblyStep}");
            }
            Debug.Log(sb.ToString());
        }

        [ContextMenu("V3 debug: AI, search, challenge and control stats")]
        private void LogAiChatStats()
        {
            Debug.Log($"[SpaceSessionHUD] AI configured: {AuraSync.AI.AiConfig.Load().IsValid}. " +
                      $"Conversations this session: {AuraSync.AI.PartChatStore.Count}." +
                      System.Environment.NewLine + AuraSync.AI.AiFeedbackLog.Summary() +
                      System.Environment.NewLine + AuraSync.AI.SearchLog.Summary() +
                      System.Environment.NewLine + AuraSync.Challenge.ChallengeLog.Summary() +
                      System.Environment.NewLine + AuraSync.Networking.ControlLog.Summary() +
                      System.Environment.NewLine + AuraSync.Notes.NotesLog.Summary() +
                      System.Environment.NewLine + (SectionCutter.Instance != null ? SectionCutter.Instance.Stats() : "Section cut: not created.") +
                      System.Environment.NewLine + AuraSync.Simulation.SimLog.Summary() +
                      System.Environment.NewLine + AuraSync.Networking.AttentionLog.Summary() +
                      System.Environment.NewLine + AuraSync.Recording.RecordingLog.Summary() +
                      System.Environment.NewLine + AuraSync.Quiz.QuizLog.Summary() +
                      System.Environment.NewLine + LoadLog.Summary() +
                      (AttentionVisuals.Instance != null ? $" Cones drawn now: {AttentionVisuals.Instance.ConesDrawn}." : ""));
        }

        [ContextMenu("V3 debug: measure note drift")]
        private void LogNoteDrift()
        {
            var notes = AuraSync.Notes.PartNoteManager.Instance;
            Debug.Log("[SpaceSessionHUD] " + (notes != null ? notes.MeasureDrift() : "No notes manager."));
        }

        [ContextMenu("V3 debug: toggle section-cut fallback")]
        private void ToggleSectionFallback() => SectionCutter.Instance?.ToggleCameraFallback();

        [ContextMenu("V3 debug: simulation sync check")]
        private void LogSimulationSync() =>
            Debug.Log("[SpaceSessionHUD] " + AuraSync.Simulation.SimLog.DriftCheck(AuraSync.Simulation.SimulationController.Instance));

        [ContextMenu("V3 debug: attention - toggle test viewers")]
        private void ToggleTestViewers() =>
            Debug.Log("[SpaceSessionHUD] " + (AuraSync.Networking.AttentionSync.Instance != null
                ? AuraSync.Networking.AttentionSync.Instance.ToggleTestViewers() : "No attention sync."));

        [ContextMenu("V3 debug: attention - test ping")]
        private void TestPing() =>
            Debug.Log("[SpaceSessionHUD] " + (AuraSync.Networking.AttentionSync.Instance != null
                ? AuraSync.Networking.AttentionSync.Instance.TestPing() : "No attention sync."));

        [ContextMenu("V3 debug: log back stack")]
        private void LogBackStack()
        {
            Debug.Log($"[SpaceSessionHUD] Back stack: {PanelNavigator.Describe()}  " +
                      $"| parts moved by the composer: {PartPoseComposer.MovedPartCount}");
        }

        /// <summary>
        /// A throwaway panel for testing the back stack: OPEN ANOTHER stacks a second one on top,
        /// CLOSE closes this one, and Back (on-screen or device) closes whichever is on top.
        /// </summary>
        private void OpenTestPanel(string title)
        {
            EnsureDebugLayer();

            int    n  = ++_debugSerial;
            string id = "test-" + n;

            GameObject card = new GameObject("TestPanel_" + n);
            card.transform.SetParent(_debugLayer, false);

            // Each new panel sits a little lower and to the right, so the stack is visible.
            float shift = (n % 4) * 0.025f;
            RectTransform rt = card.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.18f + shift, 0.38f - shift);
            rt.anchorMax = new Vector2(0.82f + shift, 0.62f - shift);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image bg = card.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.18f, 0.98f);
            card.AddComponent<TouchBlocker>(); // the model must not rotate under the panel

            VerticalLayoutGroup vg = card.AddComponent<VerticalLayoutGroup>();
            vg.padding                = new RectOffset(24, 24, 20, 20);
            vg.spacing                = 12;
            vg.childControlWidth      = true;
            vg.childControlHeight     = true;
            vg.childForceExpandWidth  = true;
            vg.childForceExpandHeight = false;

            TextMeshProUGUI head = MakeLabel(card.transform, "Title", $"{title}  (test panel {n})", 26);
            head.fontStyle = FontStyles.Bold;
            MakeLabel(card.transform, "Body",
                "Phase 0 back-stack test. BACK, or the device back button, closes the top panel.", 20);

            GameObject row = new GameObject("Buttons");
            row.transform.SetParent(card.transform, false);
            HorizontalLayoutGroup hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.spacing                = 12;
            hg.childControlWidth      = true;
            hg.childControlHeight     = true;
            hg.childForceExpandWidth  = true;
            hg.childForceExpandHeight = false;

            MakePillBtn(row.transform, "OpenAnother", "OPEN ANOTHER", SlotColor, () => OpenTestPanel(title), minW: 200f);
            MakePillBtn(row.transform, "Close", "CLOSE", new Color(0.45f, 0.20f, 0.22f), () =>
            {
                PanelNavigator.Remove(id);
                Destroy(card);
            }, minW: 140f);

            PanelNavigator.Push(id, card, () => Destroy(card));
            Debug.Log($"[SpaceSessionHUD] Opened {id}. Back stack: {PanelNavigator.Describe()}");
        }

        private void EnsureDebugLayer()
        {
            if (_debugLayer != null) return;

            _debugLayer = new GameObject("V3DebugPanels").AddComponent<RectTransform>();
            _debugLayer.SetParent(transform, false);
            _debugLayer.anchorMin = Vector2.zero;
            _debugLayer.anchorMax = Vector2.one;
            _debugLayer.offsetMin = Vector2.zero;
            _debugLayer.offsetMax = Vector2.zero;

            // Its own sorting order, so test panels draw above the parts drawer (97) like real
            // V3 panels will, and below the room panel (100).
            Canvas c = _debugLayer.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder    = 98;
            _debugLayer.gameObject.AddComponent<GraphicRaycaster>();
        }

        private static TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = text;
            tmp.fontSize      = size;
            tmp.color         = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
