using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Recording;

namespace AuraSync.UI
{
    /// <summary>
    /// Interactive mode → Recordings: every recording on this phone, with Play, Share and Delete, and
    /// Import at the top. Lives here so a replay can start without opening a model first: Play opens
    /// the recording's own model in SpaceScene and the replay begins once it has loaded
    /// (<see cref="SessionPlayer.Play"/>). The in-model list (Record → Recordings) stays as it was;
    /// Heatmap is only there, because it paints the open model.
    /// </summary>
    public partial class OnboardingFlow
    {
        private bool _recImporting;
        private string _recArmedDelete;
        private float _recArmedAt;

        private View BuildRecordings()
        {
            var v = NewView("recordings", Mode.Interactive, 1f);
            var col = PageColumn(v);

            // Header under the chip: title and count left, Import right (as Available models has Reports).
            var head = AuraKit.Rect(col, "Header");
            head.anchorMin = new Vector2(0, 1); head.anchorMax = new Vector2(1, 1); head.pivot = new Vector2(0.5f, 1);
            head.offsetMin = new Vector2(0, -AuraTheme.ChipSize); head.offsetMax = Vector2.zero;
            AuraKit.HStack(head, 20f).childForceExpandHeight = false;

            var words = AuraKit.Rect(head, "Words");
            AuraKit.VStack(words, 2f, 0, TextAnchor.MiddleLeft);
            AuraKit.Flex(words);
            var title = AuraKit.Text(words, "Recordings", AuraTheme.SizeTitle, AuraTheme.Text, AuraWeight.Bold);
            title.enableWordWrapping = false;
            var count = AuraKit.Caps(words, "");

            System.Action rebuild = null;
            var import = AuraKit.Pill(head, "Import", AuraIcons.Plus, AuraTheme.Ink, AuraTheme.Lavender,
                                      () => ImportRecording(() => rebuild?.Invoke()), AuraTheme.MinTouch, AuraTheme.Slate);
            import.GetComponent<LayoutElement>().preferredWidth = 270f;
            import.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(22, 22, 0, 0);

            // Scrolling list below the header.
            var area = AuraKit.Rect(col, "ListArea");
            AuraKit.Fill(area, 0, 0, 0, AuraTheme.ChipSize + 40f);
            var view = AuraKit.Fill(AuraKit.Rect(area, "Viewport"));
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            var content = AuraKit.Rect(view, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var stack = AuraKit.VStack(content, AuraTheme.Gap);
            stack.childForceExpandHeight = false;
            AuraKit.FitHeight(content);
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = view; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;

            var empty = AuraKit.Text(area, "", AuraTheme.SizeBody, AuraTheme.TextDim, AuraWeight.Regular,
                                     TextAlignmentOptions.Center);
            AuraKit.Fill(empty.rectTransform, 40, 0, 40, 0);
            empty.raycastTarget = false;

            rebuild = () =>
            {
                if (content == null) return;
                foreach (Transform c in content) Destroy(c.gameObject);
                List<RecordingInfo> all = RecordingStore.List();
                count.text = all.Count == 1 ? "1 RECORDING" : $"{all.Count} RECORDINGS";
                empty.text = all.Count == 0
                    ? "No recordings yet.\nOpen a model and use Record, or Import one someone shared with you."
                    : "";
                foreach (RecordingInfo info in all) RecordingRow(content, info, rebuild);
            };
            v.onShow = () => rebuild();
            return v;
        }

        private void RecordingRow(RectTransform parent, RecordingInfo info, System.Action rebuild)
        {
            var row = AuraKit.Box(parent, "Recording", AuraTheme.A(AuraTheme.Indigo, 0.92f), AuraTheme.Radius - 6f, true);
            AuraKit.Border(row, AuraTheme.A(AuraTheme.Slate, 0.75f), AuraTheme.Radius - 6f);
            var v = AuraKit.VStack(row, 12f);
            v.padding = new RectOffset(30, 30, 26, 26);
            v.childForceExpandHeight = false;
            AuraKit.FitHeight(row);

            var name = AuraKit.Text(row.transform, Esc(info.name), AuraTheme.SizeBody, AuraTheme.Text, AuraWeight.SemiBold);
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            bool imported = RecordingImport.IsImported(info);
            string meta = (imported ? "Imported · " : "") +
                          $"{Esc(info.header.modelName)} · {info.started:d MMM HH:mm} · {RecordingStore.Clock(info.seconds)} · " +
                          (info.HasVoice ? "with voice · " : "no sound · ") + RecordingStore.SizeText(info.TotalBytes);
            AuraKit.Text(row.transform, meta, AuraTheme.SizeCaption - 2f, AuraTheme.TextDim).enableWordWrapping = true;
            var missing = AuraKit.Text(row.transform, "", AuraTheme.SizeCaption - 2f, AuraTheme.Rose);
            missing.enableWordWrapping = true;
            missing.gameObject.SetActive(false);

            var buttons = AuraKit.Rect(row.transform, "Buttons");
            AuraKit.HStack(buttons, 12f).childForceExpandHeight = true;
            AuraKit.Size(buttons, -1, 92f);
            AuraWidgets.Chip(buttons, "Play", AuraIcons.PlayFill, true, () => PlayRecording(info), out Button play, 92f);

            // A recording replays on its own model: one made on someone else's scan cannot.
            RecordingImport.CheckModel(info, ok =>
            {
                if (ok || play == null || missing == null) return;
                play.interactable = false;
                missing.text = $"Made on {Esc(info.header.modelName)}, a scan that isn't on this phone, so it can't be replayed here.";
                missing.gameObject.SetActive(true);
            });
            AuraWidgets.Chip(buttons, "Share", AuraIcons.Link, false, () => Toast.Show(RecordingExport.Export(info)), out _, 92f);
            AuraKit.Flex(AuraKit.Rect(buttons, "Space"));
            TextMeshProUGUI delText = null;
            AuraWidgets.Chip(buttons, "Delete", AuraIcons.Trash, false, () =>
            {
                // First tap arms it ("Sure?"), a second within 3 s deletes.
                if (_recArmedDelete != info.path || Time.unscaledTime - _recArmedAt > 3f)
                {
                    _recArmedDelete = info.path;
                    _recArmedAt = Time.unscaledTime;
                    if (delText != null) delText.text = "Sure?";
                    return;
                }
                _recArmedDelete = null;
                if (RecordingStore.Delete(info)) Toast.Show("Recording deleted.");
                rebuild?.Invoke();
            }, out Button del, 92f);
            delText = del.GetComponentInChildren<TextMeshProUGUI>();
            if (delText != null) delText.color = AuraTheme.Rose;
        }

        private void PlayRecording(RecordingInfo info)
        {
            string problem = SessionPlayer.Play(info);
            if (problem != null) { Toast.Show(problem); return; }
            Toast.Show($"Opening {Esc(info.header.modelName)} to replay…");
        }

        /// <summary>Android's file picker, then the chosen recording joins the list.</summary>
        private void ImportRecording(System.Action rebuild)
        {
            if (_recImporting) return;
            _recImporting = true;
            StartCoroutine(RecordingFilePicker.Pick(
                path =>
                {
                    _recImporting = false;
                    if (string.IsNullOrEmpty(path)) return; // cancelled
                    Toast.Show(RecordingImport.Import(path));
                    rebuild?.Invoke();
                },
                error =>
                {
                    _recImporting = false;
                    Toast.Show(error);
                }));
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "").Replace(">", "");
    }
}
