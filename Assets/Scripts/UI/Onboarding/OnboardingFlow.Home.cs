using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>The launch screen and the frame ↔ chip animation.</summary>
    public partial class OnboardingFlow
    {
        private View _home;
        private RectTransform _homeFrame;
        private CanvasGroup _homeFrameGroup;
        private TextMeshProUGUI _brand, _tagline;

        private View BuildHome()
        {
            var v = new View { id = "home", isHome = true, mode = Mode.None, scrim = 0.62f, chrome = false };
            v.root = AuraKit.Fill(AuraKit.Rect(_homeLayer, "View_home"));
            v.group = v.root.gameObject.AddComponent<CanvasGroup>();
            _home = v;

            _brand = AuraKit.Text(v.root, "AURASYNC", AuraTheme.SizeLabel, AuraTheme.Lavender, AuraWeight.Bold,
                                  TextAlignmentOptions.Center);
            _brand.characterSpacing = 30f;
            _brand.enableWordWrapping = false;

            _tagline = AuraKit.Text(v.root, "See it in 3D. Understand it faster.", AuraTheme.SizeCaption,
                                    AuraTheme.TextDim, AuraWeight.Regular, TextAlignmentOptions.Center);
            _tagline.enableWordWrapping = false;

            _homeFrame = AuraKit.Rect(v.root, "ModeFrame");
            _homeFrameGroup = _homeFrame.gameObject.AddComponent<CanvasGroup>();
            var stack = AuraKit.VStack(_homeFrame, 28f);
            stack.childForceExpandHeight = true;

            ModeHalf(_homeFrame, AuraIcons.Play, "Play mode",
                     "Pin notes and 3D shapes onto the real world around you",
                     "NOTES  ·  CHECKLISTS  ·  3D SHAPES", AuraTheme.Indigo, EnterPlay);
            ModeHalf(_homeFrame, AuraIcons.Interactive, "Interactive mode",
                     "Scan, open and study 3D models, alone or in a live class",
                     "SCAN  ·  EXPLORE  ·  JOIN A CLASS", AuraTheme.IndigoLift, EnterInteractive);

            LayoutHome();
            return v;
        }

        /// <summary>
        /// The frame sits one physical centimetre in from every screen edge (more where a notch or
        /// gesture bar needs it). The gap between the halves is fixed, so it is always exactly in
        /// the middle of the frame.
        /// </summary>
        private void LayoutHome()
        {
            if (_homeFrame == null) return;
            Vector4 m = HomeMargins();
            AuraKit.Fill(_homeFrame, m.x, m.y, m.z, m.w);

            // Wordmark centred in the band above the frame, tagline in the band below it.
            Vector4 safe = SafeInsets();
            AuraKit.Pin(_brand.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f),
                        new Vector2(0, -(safe.w + (m.w - safe.w) / 2f)), new Vector2(900, BrandHeight));
            AuraKit.Pin(_tagline.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f),
                        new Vector2(0, safe.y + (m.y - safe.y) / 2f), new Vector2(1000, BrandHeight));
        }

        private const float BrandHeight = 44f;

        /// <summary>Left, bottom, right, top margins of the launch frame in canvas units.</summary>
        private Vector4 HomeMargins()
        {
            // Canvas width from the screen, not the canvas rect: on the first frame the rect is not sized yet.
            float canvasWidth = UnityEngine.Screen.width / AuraKit.CanvasScale;
            float cm = Mathf.Clamp(AuraKit.Centimetre, 60f, Mathf.Max(60f, canvasWidth * 0.14f));
            Vector4 safe = SafeInsets();
            // Top and bottom also keep a band for the wordmark and tagline (text height + breathing
            // room on both sides), so neither can touch or overlap the frame.
            const float band = BrandHeight + 2f * 26f;
            return new Vector4(Mathf.Max(cm, safe.x + AuraTheme.Gutter), Mathf.Max(cm, safe.y + band),
                               Mathf.Max(cm, safe.z + AuraTheme.Gutter), Mathf.Max(cm, safe.w + band));
        }

        private void ModeHalf(RectTransform parent, string icon, string title, string sub, string tags,
                              Color fill, Action onTap)
        {
            var box = AuraKit.Box(parent, "Mode_" + title, fill, AuraTheme.Radius + 12f, true);
            AuraKit.Border(box, AuraTheme.Slate, AuraTheme.Radius + 12f);
            var le = AuraKit.Size(box, -1, -1, 1f, 1f);
            le.minHeight = 0f;
            var rt = box.rectTransform;

            // Nothing drawn inside a half may spill past its edge.
            box.gameObject.AddComponent<RectMask2D>();

            // A large, faint version of the icon gives each half some depth. It sits wholly inside
            // the half, against its right edge.
            var mark = AuraKit.Icon(rt, icon, 360f, AuraTheme.A(AuraTheme.Lavender, 0.07f));
            mark.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(mark.rectTransform, new Vector2(1, 0.5f), new Vector2(1f, 0.5f), new Vector2(-36, 40),
                        new Vector2(360, 360));

            var badge = AuraKit.Box(rt, "Badge", AuraTheme.A(AuraTheme.Lavender, 0.16f), 34f);
            AuraKit.Pin(badge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(52, -52),
                        new Vector2(140, 140));
            var bi = AuraKit.Icon(badge.transform, icon, 76f, AuraTheme.Lavender);
            AuraKit.Pin(bi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(76, 76));

            var chev = AuraKit.Icon(rt, AuraIcons.Forward, 56f, AuraTheme.Lavender);
            AuraKit.Pin(chev.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-52, -94),
                        new Vector2(56, 56));

            var words = AuraKit.Rect(rt, "Words");
            words.anchorMin = new Vector2(0, 0); words.anchorMax = new Vector2(1, 0); words.pivot = new Vector2(0.5f, 0);
            words.offsetMin = new Vector2(52, 48); words.offsetMax = new Vector2(-52, 48);
            AuraKit.VStack(words, 12f);
            AuraKit.FitHeight(words);

            var tagText = AuraKit.Caps(words, tags, AuraTheme.Lavender);
            tagText.fontSize = AuraTheme.SizeLabel - 2f;
            var t = AuraKit.Text(words, title, AuraTheme.SizeDisplay, AuraTheme.Text, AuraWeight.Bold);
            t.enableWordWrapping = false;
            AuraKit.Text(words, sub, AuraTheme.SizeBody, AuraTheme.TextDim);

            AuraKit.MakeButton(box.gameObject, box, () => { if (!_animating) onTap(); });
        }

        // ── Entering a mode ────────────────────────────────────────────

        private void EnterPlay()
        {
            SetChipMode(Mode.Play);
            StartCoroutine(SwoopOut(0.3f, () => Push(BuildPlayChooser())));
        }

        private void EnterInteractive()
        {
            SetChipMode(Mode.Interactive);
            StartCoroutine(SwoopOut(0.55f, () => Push(BuildHub())));
        }

        // ── Frame ↔ chip ───────────────────────────────────────────────

        /// <summary>The launch frame shrinks into the top-right chip, then the mode's first screen opens.</summary>
        private IEnumerator SwoopOut(float toScrim, Action then)
        {
            _animating = true;
            _shield.enabled = true;
            _home.onHide?.Invoke();

            Canvas.ForceUpdateCanvases();
            var parent = (RectTransform)_homeFrame.parent;
            Rect from = LocalRect(_homeFrame, parent);
            Rect to = LocalRect(_chip, parent);

            FreeAnchor(_homeFrame);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / AuraTheme.Swoop;
                float k = Mathf.Clamp01(t);
                float e = k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
                Place(_homeFrame, parent, Lerp(from, to, e));
                _homeFrameGroup.alpha = 1f - Mathf.SmoothStep(0.25f, 1f, k);
                _home.group.alpha = 1f;
                _scrim.color = AuraTheme.A(AuraTheme.Ink, Mathf.Lerp(0.62f, toScrim, e));
                _chipGroup.alpha = Mathf.SmoothStep(0f, 1f, (k - 0.6f) / 0.4f); // the chip takes over as the frame arrives
                yield return null;
            }

            _home.root.gameObject.SetActive(false);
            _homeFrameGroup.alpha = 1f;
            LayoutHome();

            _shield.enabled = false;
            _animating = false;
            then?.Invoke();
        }

        /// <summary>The chip grows back into the launch frame.</summary>
        private void SwoopIn() => StartCoroutine(SwoopInRoutine());

        private IEnumerator SwoopInRoutine()
        {
            _animating = true;
            _shield.enabled = true;

            ApplyChrome(_home);                 // scrim, slots off, chip fading out
            _home.root.gameObject.SetActive(true);
            _home.group.alpha = 1f;

            var parent = (RectTransform)_homeFrame.parent;
            LayoutHome();
            Canvas.ForceUpdateCanvases();
            Rect to = LocalRect(_homeFrame, parent);
            Rect from = LocalRect(_chip, parent);

            FreeAnchor(_homeFrame);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / AuraTheme.Swoop;
                float k = Mathf.Clamp01(t);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                Place(_homeFrame, parent, Lerp(from, to, e));
                _homeFrameGroup.alpha = Mathf.SmoothStep(0f, 1f, k * 1.6f);
                yield return null;
            }

            LayoutHome();
            _homeFrameGroup.alpha = 1f;
            _home.onShow?.Invoke();
            _shield.enabled = false;
            _animating = false;
        }

        private static Rect LocalRect(RectTransform rt, RectTransform space)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 a = space.InverseTransformPoint(c[0]);
            Vector2 b = space.InverseTransformPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static void FreeAnchor(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void Place(RectTransform rt, RectTransform parent, Rect r)
        {
            rt.anchoredPosition = r.center - parent.rect.center;
            rt.sizeDelta = r.size;
        }

        private static Rect Lerp(Rect a, Rect b, float t) =>
            Rect.MinMaxRect(Mathf.Lerp(a.xMin, b.xMin, t), Mathf.Lerp(a.yMin, b.yMin, t),
                            Mathf.Lerp(a.xMax, b.xMax, t), Mathf.Lerp(a.yMax, b.yMax, t));
    }
}
