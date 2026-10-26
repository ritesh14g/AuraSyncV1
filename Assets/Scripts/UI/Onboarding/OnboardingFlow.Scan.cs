using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AuraSync.Core;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// Make model, in four guided steps: tips → capture → processing → ready. The KIRI pipeline is
    /// still the mock (KiriManager); these screens show what each stage does, which is the
    /// educational point, and save the result to Available models.
    /// </summary>
    public partial class OnboardingFlow
    {
        private const int ScanTarget = 60;   // photos for a full ring
        private const int ScanMinimum = 20;  // Finish unlocks here
        private const float MockProcessingSeconds = 9f;

        // ── Step 1: tips ───────────────────────────────────────────────

        private View BuildScanTips()
        {
            var v = NewView("scan.tips", Mode.Interactive, 1f);
            v.primary = "Start capture";
            v.primaryIcon = AuraIcons.Camera;
            var body = ScanPage(v, "Scan a real object", 1, "Get ready");

            Tip(body, AuraIcons.Still, "Pick something still", "Matte, palm-sized to desk-sized. Avoid glass and mirrors.");
            Tip(body, AuraIcons.Light, "Use even light", "No hard shadows. Daylight works best.");
            Tip(body, AuraIcons.Orbit, "Walk a full circle", "Two rounds: one from above, one from lower down.");
            Why(body, "Why it works",
                "KIRI Engine finds the same points in many overlapping photos and works out where each one sits in 3D. This is called photogrammetry.");

            v.onPrimary = () => Push(BuildScanCapture());
            return v;
        }

        // ── Step 2: capture ────────────────────────────────────────────

        private View BuildScanCapture()
        {
            var v = NewView("scan.capture", Mode.Interactive, 0.15f, underChip: true);
            v.primary = "Finish";
            v.primaryIcon = AuraIcons.Ok;
            v.primaryEnabled = false;
            int photos = 0;

            var counter = HintPill(v, "");
            counter.alignment = TextAlignmentOptions.MidlineLeft;

            // Coverage ring.
            var ringHolder = AuraKit.Rect(v.root, "Ring");
            AuraKit.Pin(ringHolder, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(470, 470));
            var track = ringHolder.gameObject.AddComponent<Image>();
            track.sprite = AuraSprites.ThickRing;
            track.color = AuraTheme.A(AuraTheme.Slate, 0.55f);
            track.raycastTarget = false;
            var fill = AuraKit.Rect(ringHolder, "Fill").gameObject.AddComponent<Image>();
            AuraKit.Fill(fill.rectTransform);
            fill.sprite = AuraSprites.ThickRing;
            fill.color = AuraTheme.Lavender;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;
            var pct = AuraKit.Text(ringHolder, "0%", 92f, AuraTheme.Text, AuraWeight.Bold, TextAlignmentOptions.Center);
            AuraKit.Pin(pct.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(400, 110));
            var cov = AuraKit.Caps(ringHolder, "coverage");
            cov.alignment = TextAlignmentOptions.Center;
            AuraKit.Pin(cov.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -56), new Vector2(400, 40));

            var guide = AuraKit.Text(v.root, "Move slowly around the object.\nTap to take a photo, or hold to keep shooting.",
                                     AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.Regular, TextAlignmentOptions.Center);
            AuraKit.Pin(guide.rectTransform, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 1f), new Vector2(0, -270), new Vector2(940, 120));

            // Brief white flash on each photo.
            var flash = AuraKit.Box(v.root, "Flash", new Color(1, 1, 1, 0), 0f);
            AuraKit.Fill(flash.rectTransform, -400, -600, -400, -400);

            // Shutter.
            var shutter = AuraKit.Rect(v.root, "Shutter");
            AuraKit.Pin(shutter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(210, 210));
            var outer = shutter.gameObject.AddComponent<Image>();
            outer.sprite = AuraSprites.DiscRing;
            outer.color = AuraTheme.Text;
            var inner = AuraKit.Rect(shutter, "Inner").gameObject.AddComponent<Image>();
            inner.sprite = AuraSprites.Disc;
            inner.color = AuraTheme.Lavender;
            inner.raycastTarget = false;
            AuraKit.Pin(inner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(158, 158));

            void Paint()
            {
                float k = Mathf.Clamp01(photos / (float)ScanTarget);
                fill.fillAmount = k;
                pct.text = Mathf.RoundToInt(k * 100f) + "%";
                counter.text = $"Photos  {Lav($"<b>{photos} / {ScanTarget}</b>")}" +
                               (photos < ScanMinimum ? $"   ·   {ScanMinimum - photos} more to finish" : "");
                bool ready = photos >= ScanMinimum;
                if (ready != v.primaryEnabled) { v.primaryEnabled = ready; RefreshPrimary(v); }
            }

            void Snap()
            {
                if (photos >= ScanTarget * 2) return;
                photos++;
                flash.color = new Color(1, 1, 1, 0.35f);
                Paint();
            }

            var hold = shutter.gameObject.AddComponent<AuraHold>();
            hold.onPress = Snap;
            v.onUpdate = () =>
            {
                if (flash.color.a > 0f) flash.color = new Color(1, 1, 1, Mathf.Max(0f, flash.color.a - Time.unscaledDeltaTime * 2.5f));
                inner.rectTransform.localScale = Vector3.one * (hold.IsDown ? 0.88f : 1f);
            };

            v.onPrimary = () => ReplaceAbove("hub", BuildScanProcessing(photos));
            v.onBack = () =>
            {
                if (photos == 0) { Pop(); return; }
                Confirm("Discard these photos?", "You'll start this scan again.", "Discard", true, Pop);
            };
            v.onChip = () => Confirm("Stop scanning?", "Your photos will be discarded.", "Stop", true, GoHome);

            Paint();
            return v;
        }

        // ── Step 3: processing ─────────────────────────────────────────

        private static readonly string[] StageNames =
            { "Uploading photos", "Matching photos", "Building the mesh", "Painting the texture" };

        private static readonly string[] StageWhy =
        {
            "Your photos travel to KIRI Engine's servers, where the heavy maths runs.",
            "The same features are found in overlapping photos, which places every camera in 3D.",
            "Matched points are joined into a surface made of thousands of small triangles.",
            "Colour from the photos is projected back onto that surface, so it looks real.",
        };

        private View BuildScanProcessing(int photos)
        {
            var v = NewView("scan.process", Mode.Interactive, 1f);
            var body = ScanPage(v, "Building your model", 3, "Building");

            var barBg = AuraKit.Box(body, "Bar", AuraTheme.Ink, 9f);
            AuraKit.Size(barBg, -1, 18f);
            var bar = AuraKit.Box(barBg.transform, "Fill", AuraTheme.Lavender, 9f);
            var brt = bar.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = new Vector2(0, 1); brt.offsetMin = brt.offsetMax = Vector2.zero;

            var dots = new List<Image>();
            var ticks = new List<Image>();
            var values = new List<TextMeshProUGUI>();
            var names = new List<TextMeshProUGUI>();
            for (int i = 0; i < StageNames.Length; i++)
            {
                var row = AuraKit.Rect(body, "Stage" + i);
                AuraKit.HStack(row, 24f);
                AuraKit.Size(row, -1, 110f);
                var dotHolder = AuraKit.Rect(row, "Dot");
                AuraKit.Size(dotHolder, 56f, 56f);
                var dot = dotHolder.gameObject.AddComponent<Image>();
                dot.sprite = AuraSprites.DiscRing;
                dot.color = AuraTheme.Slate;
                var tick = AuraKit.Icon(dotHolder, AuraIcons.Ok, 34f, AuraTheme.Ink);
                tick.GetComponent<LayoutElement>().ignoreLayout = true;
                AuraKit.Pin(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
                tick.enabled = false;
                string label = i == 0 ? $"Uploading {photos} photos" : StageNames[i];
                var n = AuraKit.Text(row, label, AuraTheme.SizeBody, AuraTheme.TextDim);
                n.enableWordWrapping = false;
                AuraKit.Flex(n);
                var val = AuraKit.Text(row, "", AuraTheme.SizeSmall, AuraTheme.TextDim, AuraWeight.SemiBold,
                                       TextAlignmentOptions.MidlineRight);
                AuraKit.Size(val, 160f);
                dots.Add(dot); ticks.Add(tick); values.Add(val); names.Add(n);
            }
            var now = Why(body, "Now", StageWhy[0]);

            float progress = 0f;
            string readyUrl = null;
            bool started = false, done = false;

            void Paint()
            {
                brt.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
                int stage = Mathf.Min(3, Mathf.FloorToInt(progress * 4f));
                float within = Mathf.Clamp01(progress * 4f - stage);
                for (int i = 0; i < dots.Count; i++)
                {
                    bool isDone = i < stage || progress >= 1f;
                    bool isNow = i == stage && progress < 1f;
                    dots[i].sprite = isDone ? AuraSprites.Disc : AuraSprites.DiscRing;
                    dots[i].color = isDone || isNow ? AuraTheme.Lavender : AuraTheme.Slate;
                    ticks[i].enabled = isDone;
                    names[i].color = isDone || isNow ? AuraTheme.Text : AuraTheme.TextDim;
                    values[i].text = isDone ? "done" : isNow ? Mathf.RoundToInt(within * 100f) + "%" : "—";
                    values[i].color = isNow ? AuraTheme.Lavender : AuraTheme.TextDim;
                }
                now.text = $"<color=#9894C4><b>Now ·</b></color> {StageWhy[stage]}";
            }

            Action<float> onProgress = p => { progress = p; Paint(); };
            Action<string> onReady = url =>
            {
                done = true;
                readyUrl = url;
                if (Top == v) ReplaceAbove("hub", BuildScanReady(url));
            };

            var kiri = KiriManager.Instance != null ? KiriManager.Instance
                     : new GameObject("KiriManager").AddComponent<KiriManager>();
            kiri.OnProgress += onProgress;
            kiri.OnModelReady += onReady;

            v.onShow = () =>
            {
                if (!started) { started = true; kiri.StartCapture(MockProcessingSeconds); }
                else if (readyUrl != null) ReplaceAbove("hub", BuildScanReady(readyUrl)); // finished while a confirmation was open
            };
            v.onClose = () =>
            {
                kiri.OnProgress -= onProgress;
                kiri.OnModelReady -= onReady;
                if (!done) kiri.CancelCapture();
            };
            v.onBack = () => Confirm("Stop building this model?", "The photos will be discarded.", "Stop", true,
                                     () => PopTo("hub"));
            v.onChip = () => Confirm("Stop building this model?", "The photos will be discarded.", "Stop", true, GoHome);

            Paint();
            return v;
        }

        // ── Step 4: ready ──────────────────────────────────────────────

        private View BuildScanReady(string url)
        {
            var v = NewView("scan.ready", Mode.Interactive, 1f);
            v.primary = "Save";
            v.primaryIcon = AuraIcons.Ok;
            var body = ScanPage(v, "Your model is ready", 4, "Ready");

            var pv = AuraKit.Box(body, "Preview", AuraTheme.Ink, AuraTheme.Radius);
            AuraKit.Border(pv, AuraTheme.Slate);
            AuraKit.Size(pv, -1, 460f);
            pv.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var icon = AuraKit.Icon(pv.transform, AuraIcons.Scan, 150f, AuraTheme.Lavender);
            icon.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(150, 150));
            var making = AuraKit.Text(pv.transform, "Making a preview…", AuraTheme.SizeCaption, AuraTheme.TextDim,
                                      AuraWeight.Regular, TextAlignmentOptions.Center);
            AuraKit.Pin(making.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(700, 44));
            var holder = AuraKit.Rect(pv.transform, "ThumbHolder");
            AuraKit.Pin(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440, 440));
            var raw = holder.gameObject.AddComponent<RawImage>();
            raw.enabled = false;
            raw.raycastTarget = false;

            ModelThumbnails.Request(new ModelData { name = "New scan", glbUrl = url }, tex =>
            {
                if (raw == null) return;
                if (tex == null) { if (making != null) making.text = ""; return; }
                raw.texture = tex;
                raw.enabled = true;
                icon.enabled = false;
                making.text = "";
            });

            AuraKit.Caps(body, "Name");
            var nameInput = AuraWidgets.Input(body, "Name your model", "", false, 150f, 40);
            AuraKit.Text(body, "It will appear in Available models.", AuraTheme.SizeCaption, AuraTheme.TextDim);
            ScanNaming.BuildTodaysName(n => { if (nameInput != null && string.IsNullOrEmpty(nameInput.text)) nameInput.text = n; });

            nameInput.onValueChanged.AddListener(s =>
            {
                v.primaryEnabled = !string.IsNullOrWhiteSpace(s);
                RefreshPrimary(v);
            });

            v.onPrimary = () =>
            {
                string name = nameInput.text.Trim();
                if (name.Length == 0 || ModelCatalog.Instance == null) return;
                v.primaryEnabled = false;
                RefreshPrimary(v);
                ModelCatalog.Instance.GetAllModels(all =>
                {
                    var data = new ModelData
                    {
                        name = ScanNaming.MakeUnique(name, all),
                        glbUrl = url,
                        description = "Your scan",
                    };
                    ModelCatalog.Instance.SaveModel(data);
                    Toast.Show($"Saved {data.name} to Available models");
                    ReplaceAbove("hub", BuildLibrary(data.id));
                });
            };
            v.onBack = () => Confirm("Discard this scan?", "It won't be saved.", "Discard", true, () => PopTo("hub"));
            v.onChip = () => Confirm("Discard this scan?", "It won't be saved.", "Discard", true, GoHome);
            return v;
        }

        // ── Page parts ─────────────────────────────────────────────────

        /// <summary>Title (in its own row under the chip), the four-step bar and step label, then a scrolling body.</summary>
        private RectTransform ScanPage(View v, string title, int step, string stepName)
        {
            var col = PageColumn(v);
            var t = AuraKit.Text(col, title, AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold);
            t.enableWordWrapping = false;
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(1, 1); trt.pivot = new Vector2(0.5f, 1);
            trt.offsetMin = new Vector2(0, -AuraTheme.ChipSize);
            trt.offsetMax = Vector2.zero;

            var area = AuraKit.Rect(col, "Area");
            AuraKit.Fill(area, 0, 0, 0, AuraTheme.ChipSize + 40f);
            var body = AuraWidgets.Scroll(area, out _, 30f);

            var steps = AuraKit.Rect(body, "Steps");
            var sh = AuraKit.HStack(steps, 12f);
            sh.childForceExpandWidth = true; sh.childForceExpandHeight = true;
            AuraKit.Size(steps, -1, 12f);
            for (int i = 1; i <= 4; i++)
            {
                var seg = AuraKit.Box(steps, "Step" + i, i <= step ? AuraTheme.Lavender : AuraTheme.Slate, 6f);
                AuraKit.Cell(seg);
            }
            AuraKit.Caps(body, $"Step {step} of 4 · {stepName}");
            return body;
        }

        private static void Tip(RectTransform body, string icon, string title, string text)
        {
            var row = AuraKit.Rect(body, "Tip");
            var h = AuraKit.HStack(row, 30f, 0, TextAnchor.UpperLeft);
            h.childForceExpandHeight = false;
            var badge = AuraKit.Box(row, "Badge", AuraTheme.A(AuraTheme.Lavender, 0.15f), 26f);
            AuraKit.Size(badge, 110f, 110f);
            var bi = AuraKit.Icon(badge.transform, icon, 60f, AuraTheme.Lavender);
            bi.GetComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(bi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 60));
            var words = AuraKit.Rect(row, "Words");
            AuraKit.VStack(words, 6f);
            AuraKit.Flex(words);
            AuraKit.Text(words, title, AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            AuraKit.Text(words, text, AuraTheme.SizeSmall, AuraTheme.TextDim);
        }

        /// <summary>The "why it works" box: the science behind the step, in one or two sentences.</summary>
        private static TextMeshProUGUI Why(RectTransform body, string lead, string text)
        {
            var box = AuraKit.Box(body, "Why", AuraTheme.A(AuraTheme.Lavender, 0.09f), AuraTheme.Radius - 8f);
            AuraKit.Border(box, AuraTheme.Slate, AuraTheme.Radius - 8f);
            var v = AuraKit.VStack(box, 0f);
            v.padding = new RectOffset(36, 36, 30, 30);
            var t = AuraKit.Text(box.transform, $"<color=#9894C4><b>{lead} ·</b></color> {text}", AuraTheme.SizeSmall,
                                 AuraTheme.TextDim);
            t.richText = true;
            return t;
        }

        /// <summary>Removes views above the one with this id (without showing it), then pushes next.</summary>
        private void ReplaceAbove(string id, View next)
        {
            while (_stack.Count > 1 && Top.id != id)
            {
                var top = Top;
                _stack.RemoveAt(_stack.Count - 1);
                Close(top);
            }
            Push(next);
        }
    }

    /// <summary>Shutter behaviour: one press = one photo; holding keeps taking them.</summary>
    public class AuraHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action onPress;
        public float repeatAfter = 0.35f;
        public float repeatEvery = 0.18f;

        public bool IsDown { get; private set; }
        private float _next;

        public void OnPointerDown(PointerEventData e)
        {
            IsDown = true;
            onPress?.Invoke();
            _next = Time.unscaledTime + repeatAfter;
        }

        public void OnPointerUp(PointerEventData e) => IsDown = false;
        public void OnPointerExit(PointerEventData e) => IsDown = false;

        void OnDisable() => IsDown = false;

        void Update()
        {
            if (!IsDown || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + repeatEvery;
            onPress?.Invoke();
        }
    }
}
