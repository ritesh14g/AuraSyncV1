using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// Everything before a room: the launch screen (Play mode / Interactive mode), the note and
    /// object flow, Make model, Available models and Join room. Built in code on its own canvas in
    /// the AR scene.
    ///
    /// Every screen follows one rule so the user never hunts for a button:
    ///   * slot B, bottom-left  - Back, always, and the Android back key does the same thing;
    ///   * slot C, bottom-right - the forward action (Done, Place, Join, Save), or empty;
    ///   * top-right            - the mode chip the launch frame shrinks into.
    /// Screens sit on a stack; Back pops one, so every screen has exactly one way back.
    ///
    /// Screens are split by area across partial files: Home (launch + chip animation), Play,
    /// Library (hub + models), Join and Scan.
    /// </summary>
    public partial class OnboardingFlow : MonoBehaviour
    {
        /// <summary>
        /// The one switch. false brings back the old screens (lobby on launch, + button, O button,
        /// old model list, old KIRI screens) with nothing else to change.
        /// </summary>
        public static bool UseNewOnboarding = true;

        public static OnboardingFlow Instance { get; private set; }
        public static bool IsActive => Instance != null && Instance.isActiveAndEnabled;

        public static void EnsureExists()
        {
            if (!UseNewOnboarding || Instance != null) return;
            new GameObject("AuraSync_Onboarding").AddComponent<OnboardingFlow>();
        }

        private const string SpaceSceneName = "SpaceScene";

        private enum Mode { None, Play, Interactive }

        /// <summary>One screen on the stack, with what the shared chrome should show for it.</summary>
        private class View
        {
            public string id;
            public Mode mode;
            public RectTransform root;
            public CanvasGroup group;
            public bool isHome;

            public float scrim;              // 0 = live camera, 1 = opaque page
            public bool blockWorld = true;   // taps stop at the UI instead of reaching AR items
            public bool chrome = true;       // chip + Back
            public bool hideChip;            // e.g. confirmations
            public bool underChip;           // content starts at the top; the screen leaves room for the chip

            public string backLabel = "Back";
            public string primary;           // null = slot C empty
            public string primaryIcon;
            public bool primaryEnabled = true;
            public bool primaryDanger;
            public Action onPrimary;

            public Action onBack;            // null = pop
            public Action onChip;            // null = default for the mode
            public Action onShow, onHide, onClose, onUpdate;
        }

        // ── Canvas parts ───────────────────────────────────────────────
        private Canvas _canvas;
        private RectTransform _canvasRt;
        private Image _scrim;
        private RectTransform _homeLayer;   // full screen: the launch frame measures 1cm from the real edge
        private RectTransform _safe;        // the screen's safe area
        private RectTransform _lift;        // moved up by KeyboardLift while typing
        private Image _shield;              // swallows taps during animations

        private RectTransform _chip;
        private CanvasGroup _chipGroup;
        private Image _chipIcon;

        private RectTransform _slots;
        private Button _backButton;
        private TextMeshProUGUI _backLabel;
        private Button _primaryButton;
        private Image _primaryFill;
        private Image _primaryIconImg;
        private TextMeshProUGUI _primaryLabel;

        private readonly List<View> _stack = new List<View>();
        private Rect _lastSafe;
        private bool _animating;

        private View Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        // ── Lifecycle ──────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCanvas();
        }

        void Start()
        {
            var home = BuildHome();
            _stack.Add(home);
            ApplyChrome(home);

            // Coming back from the 3D viewer: reopen the model list, as the old list did.
            if (PlayerPrefs.GetInt(ModelRepositoryUI.OpenOnLoadKey, 0) == 1)
            {
                PlayerPrefs.DeleteKey(ModelRepositoryUI.OpenOnLoadKey);
                PlayerPrefs.Save();
                try
                {
                    home.root.gameObject.SetActive(false);
                    PushNoAnim(BuildHub());
                    Push(BuildLibrary(null));
                }
                catch (System.Exception ex)
                {
                    // Never leave a blank screen: fall back to the launch frame.
                    Debug.LogError($"[Onboarding] Could not reopen the model list: {ex}");
                    while (_stack.Count > 1) { var v = Top; _stack.RemoveAt(_stack.Count - 1); if (v?.root != null) Destroy(v.root.gameObject); }
                    home.root.gameObject.SetActive(true);
                    ApplyChrome(home);
                }
            }
            Debug.Log("[Onboarding] Ready.");
        }

        void OnDestroy()
        {
            foreach (var v in _stack) { try { v.onHide?.Invoke(); v.onClose?.Invoke(); } catch { } }
            _stack.Clear();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            FitSafeArea();

            if (InputCompat.BackPressedThisFrame)
            {
                // Panels that use the shared stack (Reports) sit above this canvas: close them first.
                if (PanelNavigator.Count > 0) PanelNavigator.Back();
                else if (!_animating) Back();
            }

            Top?.onUpdate?.Invoke();
        }

        // ── Canvas ─────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            _canvas = AuraKit.CreateCanvas(transform, "OnboardingCanvas", 92); // above the AR HUD (90), below the room panel (100)
            _canvasRt = (RectTransform)_canvas.transform;

            _scrim = AuraKit.Box(_canvasRt, "Scrim", AuraTheme.A(AuraTheme.Ink, 0f), 0f);
            AuraKit.Fill(_scrim.rectTransform);

            _homeLayer = AuraKit.Fill(AuraKit.Rect(_canvasRt, "HomeLayer"));

            _safe = AuraKit.Rect(_canvasRt, "SafeArea");
            _lift = AuraKit.Fill(AuraKit.Rect(_safe, "Lift"));
            _lift.gameObject.AddComponent<KeyboardLift>().target = _lift;

            BuildSlots();
            BuildChip();

            _shield = AuraKit.Box(_canvasRt, "InputShield", new Color(0, 0, 0, 0), 0f, true);
            AuraKit.Fill(_shield.rectTransform);
            _shield.enabled = false;

            FitSafeArea(force: true);
        }

        private void FitSafeArea(bool force = false)
        {
            Rect sa = UnityEngine.Screen.safeArea;
            if (!force && sa == _lastSafe) return;
            _lastSafe = sa;

            float w = Mathf.Max(1, UnityEngine.Screen.width), h = Mathf.Max(1, UnityEngine.Screen.height);
            _safe.anchorMin = new Vector2(sa.xMin / w, sa.yMin / h);
            _safe.anchorMax = new Vector2(sa.xMax / w, sa.yMax / h);
            _safe.offsetMin = _safe.offsetMax = Vector2.zero;

            var home = _stack.Count > 0 ? _stack[0] : null;
            if (home != null && home.isHome && home.root.gameObject.activeSelf) LayoutHome();
        }

        /// <summary>Safe-area insets in canvas units (left, bottom, right, top).</summary>
        private Vector4 SafeInsets()
        {
            Rect sa = UnityEngine.Screen.safeArea;
            float k = AuraKit.CanvasScale;
            return new Vector4(sa.xMin / k, sa.yMin / k,
                               (UnityEngine.Screen.width - sa.xMax) / k, (UnityEngine.Screen.height - sa.yMax) / k);
        }

        private void BuildSlots()
        {
            _slots = AuraKit.Rect(_lift, "Slots");
            _slots.anchorMin = new Vector2(0, 0);
            _slots.anchorMax = new Vector2(1, 0);
            _slots.pivot = new Vector2(0.5f, 0);
            _slots.offsetMin = new Vector2(AuraTheme.Gutter, AuraTheme.Gutter);
            _slots.offsetMax = new Vector2(-AuraTheme.Gutter, AuraTheme.Gutter + AuraTheme.SlotHeight);

            // Slot B: Back.
            var back = AuraKit.Box(_slots, "SlotBack", AuraTheme.A(AuraTheme.Indigo, 0.96f), AuraTheme.Radius, true);
            AuraKit.Border(back, AuraTheme.Slate);
            var brt = back.rectTransform;
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0.5f, 1);
            brt.offsetMin = Vector2.zero; brt.offsetMax = new Vector2(-AuraTheme.Gap / 2f, 0);
            var bh = AuraKit.HStack(back, 16f, 0, TextAnchor.MiddleCenter);
            bh.padding = new RectOffset(24, 24, 0, 0);
            AuraKit.Icon(back.transform, AuraIcons.Back, 44f, AuraTheme.Text);
            _backLabel = AuraKit.Text(back.transform, "Back", AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            _backLabel.enableWordWrapping = false;
            _backButton = AuraKit.MakeButton(back.gameObject, back, () => { if (!_animating) Back(); });

            // Slot C: the forward action.
            _primaryFill = AuraKit.Box(_slots, "SlotPrimary", AuraTheme.Lavender, AuraTheme.Radius, true);
            var prt = _primaryFill.rectTransform;
            prt.anchorMin = new Vector2(0.5f, 0); prt.anchorMax = new Vector2(1, 1);
            prt.offsetMin = new Vector2(AuraTheme.Gap / 2f, 0); prt.offsetMax = Vector2.zero;
            var ph = AuraKit.HStack(_primaryFill, 16f, 0, TextAnchor.MiddleCenter);
            ph.padding = new RectOffset(24, 24, 0, 0);
            _primaryIconImg = AuraKit.Icon(_primaryFill.transform, AuraIcons.Ok, 44f, AuraTheme.Ink);
            _primaryLabel = AuraKit.Text(_primaryFill.transform, "Done", AuraTheme.SizeBody, AuraTheme.Ink, AuraWeight.Bold);
            _primaryLabel.enableWordWrapping = false;
            _primaryButton = AuraKit.MakeButton(_primaryFill.gameObject, _primaryFill, () =>
            {
                var top = Top;
                if (_animating || top == null || !top.primaryEnabled) return;
                top.onPrimary?.Invoke();
            });
        }

        private void BuildChip()
        {
            _chip = AuraKit.Rect(_lift, "ModeChip");
            AuraKit.Pin(_chip, new Vector2(1, 1), new Vector2(1, 1),
                        new Vector2(-AuraTheme.Gutter, -AuraTheme.Gutter),
                        new Vector2(AuraTheme.ChipSize, AuraTheme.ChipSize));
            _chipGroup = _chip.gameObject.AddComponent<CanvasGroup>();

            var glow = AuraKit.Box(_chip, "Glow", AuraTheme.A(AuraTheme.Lavender, 0.14f), AuraTheme.Radius + 12f);
            AuraKit.Fill(glow.rectTransform, -12, -12, -12, -12);

            var face = AuraKit.Box(_chip, "Face", AuraTheme.Indigo, AuraTheme.Radius, true);
            AuraKit.Fill(face.rectTransform);
            AuraKit.Border(face, AuraTheme.Lavender);
            _chipIcon = AuraKit.Icon(face.transform, AuraIcons.Play, 64f, AuraTheme.Lavender);
            AuraKit.Pin(_chipIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(64, 64));
            AuraKit.MakeButton(face.gameObject, face, OnChipPressed);

            // No caption under the chip: the icon says the mode, and the corner stays uncluttered.

            SetChipVisible(false, instant: true);
        }

        private void SetChipVisible(bool on, bool instant = false)
        {
            _chipGroup.blocksRaycasts = on;
            _chipGroup.interactable = on;
            if (instant) { _chipGroup.alpha = on ? 1f : 0f; return; }
            StartCoroutine(FadeGroup(_chipGroup, on ? 1f : 0f, AuraTheme.Quick));
        }

        private void SetChipMode(Mode m)
        {
            _chipIcon.sprite = AuraIcons.Get(m == Mode.Interactive ? AuraIcons.Interactive : AuraIcons.Play);
        }

        private void OnChipPressed()
        {
            var top = Top;
            if (_animating || top == null || top.isHome) return;
            if (top.onChip != null) { top.onChip(); return; }
            GoHome();
        }

        // ── Views ──────────────────────────────────────────────────────

        /// <summary>A new screen's root, laid out in the safe area between the chip and the slots.</summary>
        private View NewView(string id, Mode mode, float scrim, bool underChip = false)
        {
            var v = new View { id = id, mode = mode, scrim = scrim, underChip = underChip };
            v.root = AuraKit.Rect(_lift, "View_" + id);
            v.root.SetSiblingIndex(0); // under the slots and the chip
            v.group = v.root.gameObject.AddComponent<CanvasGroup>();
            return v;
        }

        /// <summary>Places a view's root for its chrome: above the slot bar, below the chip if it shows.</summary>
        private void LayoutView(View v)
        {
            if (v.isHome) return;
            bool slotsShown = v.chrome;
            float bottom = slotsShown ? AuraTheme.Gutter + AuraTheme.SlotHeight + AuraTheme.Gap : AuraTheme.Gutter;
            bool chipShown = v.chrome && !v.hideChip && v.mode != Mode.None;
            // Content starts in its own row below the chip, never beside it (except camera hints).
            float top = (v.underChip || !chipShown) ? AuraTheme.Gutter : AuraTheme.Gutter + AuraTheme.ChipSize + AuraTheme.Gap;
            AuraKit.Fill(v.root, AuraTheme.Gutter, bottom, AuraTheme.Gutter, top);
        }

        /// <summary>Shows what a view asks for: scrim, chip, Back label, slot C.</summary>
        private void ApplyChrome(View v)
        {
            if (v == null) return;

            _scrim.color = AuraTheme.A(AuraTheme.Ink, v.scrim);
            _scrim.raycastTarget = v.blockWorld;

            bool chipOn = v.chrome && !v.hideChip && v.mode != Mode.None;
            if (chipOn) SetChipMode(v.mode);
            _chipGroup.blocksRaycasts = chipOn;
            _chipGroup.interactable = chipOn;
            if ((_chipGroup.alpha > 0.5f) != chipOn)
                StartCoroutine(FadeGroup(_chipGroup, chipOn ? 1f : 0f, AuraTheme.Quick));

            _slots.gameObject.SetActive(v.chrome);
            _backLabel.text = v.backLabel;
            RefreshPrimary(v);
        }

        /// <summary>Repaints slot C after a view changes its primary label or enabled state.</summary>
        private void RefreshPrimary(View v)
        {
            if (v == null || v != Top) return;
            bool has = !string.IsNullOrEmpty(v.primary);
            _primaryFill.gameObject.SetActive(has);
            if (!has) return;

            _primaryLabel.text = v.primary;
            _primaryIconImg.gameObject.SetActive(!string.IsNullOrEmpty(v.primaryIcon));
            if (!string.IsNullOrEmpty(v.primaryIcon)) _primaryIconImg.sprite = AuraIcons.Get(v.primaryIcon);

            Color fill = v.primaryDanger ? AuraTheme.Rose : AuraTheme.Lavender;
            Color ink = AuraTheme.Ink;
            if (!v.primaryEnabled) { fill = AuraTheme.A(AuraTheme.Slate, 0.45f); ink = AuraTheme.A(AuraTheme.Text, 0.45f); }
            _primaryFill.color = fill;
            _primaryLabel.color = ink;
            _primaryIconImg.color = ink;
        }

        // ── Navigation ─────────────────────────────────────────────────

        private void Push(View v)
        {
            var below = Top;
            if (below != null)
            {
                below.onHide?.Invoke();
                if (!below.isHome) below.root.gameObject.SetActive(false);
            }

            _stack.Add(v);
            LayoutView(v);
            v.root.gameObject.SetActive(true);
            ApplyChrome(v);
            v.onShow?.Invoke();
            StartCoroutine(Appear(v));
        }

        /// <summary>Adds a view under the current flow without showing it (used to rebuild a stack).</summary>
        private void PushNoAnim(View v)
        {
            var below = Top;
            if (below != null && !below.isHome) below.root.gameObject.SetActive(false);
            _stack.Add(v);
            LayoutView(v);
            v.root.gameObject.SetActive(false);
        }

        /// <summary>Back: the view's own handler, or one step down the stack. On the launch screen, offer to exit.</summary>
        private void Back()
        {
            var top = Top;
            if (top == null) return;
            if (top.isHome) { ConfirmExit(); return; }
            if (top.onBack != null) { top.onBack(); return; }
            Pop();
        }

        private void Pop()
        {
            if (_stack.Count <= 1) return;
            var top = Top;
            _stack.RemoveAt(_stack.Count - 1);
            Close(top);

            var next = Top;
            if (next.isHome) { RevealHome(); return; }

            next.root.gameObject.SetActive(true);
            LayoutView(next);
            ApplyChrome(next);
            next.onShow?.Invoke();
            StartCoroutine(Appear(next));
        }

        /// <summary>Removes views from the top until the one with this id is on top (no animation in between).</summary>
        private void PopTo(string id)
        {
            while (_stack.Count > 1 && Top.id != id)
            {
                var top = Top;
                _stack.RemoveAt(_stack.Count - 1);
                Close(top);
            }
            var next = Top;
            if (next.isHome) { RevealHome(); return; }
            next.root.gameObject.SetActive(true);
            LayoutView(next);
            ApplyChrome(next);
            next.onShow?.Invoke();
            StartCoroutine(Appear(next));
        }

        /// <summary>Closes everything and returns to the launch screen (the chip grows back into the frame).</summary>
        private void GoHome()
        {
            while (_stack.Count > 1)
            {
                var top = Top;
                _stack.RemoveAt(_stack.Count - 1);
                Close(top);
            }
            RevealHome();
        }

        /// <summary>
        /// Back on the launch screen: the chip grows back into the frame, unless the frame never left
        /// (a confirmation shown over it), in which case it is simply uncovered.
        /// </summary>
        private void RevealHome()
        {
            if (_home.root.gameObject.activeSelf)
            {
                ApplyChrome(_home);
                _home.onShow?.Invoke();
                return;
            }
            SwoopIn();
        }

        private void Close(View v)
        {
            try { v.onHide?.Invoke(); v.onClose?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning($"[Onboarding] Closing {v.id}: {ex.Message}"); }
            if (v.root != null) Destroy(v.root.gameObject);
        }

        private IEnumerator Appear(View v)
        {
            if (v.group == null) yield break;
            v.group.alpha = 0f;
            float t = 0f;
            var rt = v.root;
            while (t < 1f && v.group != null)
            {
                t += Time.unscaledDeltaTime / AuraTheme.Quick;
                float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
                v.group.alpha = e;
                if (rt != null) rt.localScale = Vector3.one * Mathf.Lerp(0.985f, 1f, e);
                yield return null;
            }
            if (v.group != null) v.group.alpha = 1f;
            if (rt != null) rt.localScale = Vector3.one;
        }

        private static IEnumerator FadeGroup(CanvasGroup g, float to, float dur)
        {
            if (g == null) yield break;
            float from = g.alpha, t = 0f;
            while (t < 1f && g != null)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.01f, dur);
                g.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            if (g != null) g.alpha = to;
        }

        // ── Shared screens ─────────────────────────────────────────────

        /// <summary>
        /// A yes/no step that keeps the slot rule: Back (slot B) cancels, the action sits in slot C.
        /// </summary>
        private void Confirm(string title, string body, string action, bool danger, Action onYes)
        {
            var below = Top;
            var v = NewView("confirm", below != null ? below.mode : Mode.None, 0.8f);
            v.hideChip = true;
            v.primary = action;
            v.primaryDanger = danger;
            v.onPrimary = () => { Pop(); onYes?.Invoke(); };

            var card = AuraKit.Card(v.root, title, body, out _);
            CentreCard(card);
            Push(v);
        }

        private void ConfirmExit()
        {
            Confirm("Leave AuraSync?", "Your notes and shapes stay where you put them.", "Exit", false,
                    () => Application.Quit());
        }

        /// <summary>Centres a card vertically in its view, full width up to a comfortable maximum.</summary>
        private static void CentreCard(RectTransform card)
        {
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.gameObject.AddComponent<MatchParentWidth>().max = 1000f;
        }

        /// <summary>A full-height column for page screens, centred with a maximum width on big screens.</summary>
        private static RectTransform PageColumn(View v)
        {
            var col = AuraKit.Rect(v.root, "Column");
            col.anchorMin = new Vector2(0.5f, 0);
            col.anchorMax = new Vector2(0.5f, 1);
            col.pivot = new Vector2(0.5f, 0.5f);
            col.offsetMin = col.offsetMax = Vector2.zero;
            col.gameObject.AddComponent<MatchParentWidth>().max = 1100f;
            return col;
        }

        /// <summary>A pill on top of the camera with one line of guidance, left of the chip.</summary>
        private TextMeshProUGUI HintPill(View v, string text)
        {
            var pill = AuraKit.Box(v.root, "Hint", AuraTheme.A(AuraTheme.Ink, 0.88f), AuraTheme.Radius - 8f);
            AuraKit.Border(pill, AuraTheme.Slate, AuraTheme.Radius - 8f);
            var rt = pill.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -AuraTheme.ChipSize);
            rt.offsetMax = new Vector2(-(AuraTheme.ChipSize + AuraTheme.Gap), 0);
            var t = AuraKit.Text(pill.transform, text, AuraTheme.SizeSmall, AuraTheme.Text);
            t.richText = true;
            AuraKit.Fill(t.rectTransform, 32, 8, 28, 8);
            return t;
        }

        private static string Lav(string s) => $"<color=#9894C4>{s}</color>";
    }

    /// <summary>Keeps a centre-anchored element as wide as its parent, up to a maximum.</summary>
    public class MatchParentWidth : MonoBehaviour
    {
        public float max = 1000f;

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float w = Mathf.Min(parent.rect.width, max);
            if (!Mathf.Approximately(rt.sizeDelta.x, w)) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }
    }
}
