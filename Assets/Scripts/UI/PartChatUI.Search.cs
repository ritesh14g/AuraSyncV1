using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.UI
{
    /// <summary>
    /// The FIND tab (Phase 4): find a part by typing or saying what you are after.
    ///
    ///   * As you type, the list fills from the phone alone (PartSearch.Local): instant, offline,
    ///     tolerant of typos and plurals.
    ///   * Search (or Enter): if the phone is not sure, the AI matches by meaning
    ///     ("the part that burns fuel").
    ///   * Mic: say it instead. The recording becomes text (Groq Whisper, through the AI server),
    ///     is put in the box, and searched as if typed.
    ///   * Tap a result to select it; Ask opens the chat about it; Isolate shows it on its own.
    ///
    /// In a room, picking a result goes through the normal selection rules.
    /// </summary>
    public partial class PartChatUI
    {
        private const int MaxQueryChars = 120;
        private static readonly Color ListeningColor = AuraTheme.Rose;

        private TMP_InputField _findInput;
        private Button _micButton;
        private Image _micImage, _micIcon;
        private Button _goButton;
        private TextMeshProUGUI _findStatus;
        private ScrollRect _findScroll;
        private RectTransform _findList;

        private string _findPath = "";
        private List<SearchHit> _hits = new List<SearchHit>();
        private string _searchId = "";
        private float _searchStartedAt;
        private string _searchMethod = "text";
        private long _speechMs;
        private bool _picked;
        private AiClient.Pending _pendingFind;
        private AiClient.Pending _pendingSpeech;
        private Coroutine _listening;

        // ── Opening ────────────────────────────────────────────────────

        /// <summary>Opens the panel on the FIND tab.</summary>
        public void OpenFind()
        {
            if (string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) return;
            ShowPanel();
            SetTab(Tab.Find);
            RefreshFindAvailability();
        }

        private bool CanSearch => _knowledge != null && _knowledge.HasParts;

        private void RefreshFindAvailability()
        {
            bool can = CanSearch;
            _findInput.interactable = can;
            _micButton.interactable = can;
            _goButton.interactable = can;
            if (!can)
            {
                ClearResults();
                SetFindStatus("This model has no part names to search. Use Ask to ask about the whole model.");
            }
            else if (_hits.Count == 0 && string.IsNullOrWhiteSpace(_findInput.text))
            {
                SetFindStatus("Type or say a part's name, what it does, or where it is.");
            }
        }

        // ── Searching ──────────────────────────────────────────────────

        private void OnFindTyped(string text)
        {
            if (!CanSearch) return;
            CancelAiFind();

            if (string.IsNullOrWhiteSpace(text))
            {
                ClearResults();
                SetFindStatus("Type or say a part's name, what it does, or where it is.");
                return;
            }

            StartSearchIfNew("text");
            _hits = PartSearch.Local(text);
            RenderResults();
            SetFindStatus(_hits.Count == 0
                ? "No name matches yet. Tap search to look by meaning."
                : $"{_hits.Count} match{(_hits.Count == 1 ? "" : "es")}. Tap one to select it.");
        }

        private void OnFindSubmitted(string text) => Submit(text, _searchMethod);

        private void OnGoPressed() => Submit(_findInput.text, "text");

        /// <summary>
        /// A finished query (search, Enter, or what was heard). The phone's matches are used if it is
        /// sure; otherwise the AI matches by meaning. A single clear winner is selected straight away.
        /// </summary>
        private void Submit(string text, string method)
        {
            if (!CanSearch) return;
            text = (text ?? "").Trim();
            if (text.Length == 0) return;

            StartSearchIfNew(method);
            _searchMethod = method;
            _hits = PartSearch.Local(text);

            bool confident = _hits.Count > 0 && _hits[0].score >= PartSearch.Confident;
            bool clearWinner = confident && (_hits.Count == 1 || _hits[0].score - _hits[1].score >= 0.15f);

            if (confident)
            {
                SearchLog.Search(_searchId, method, text, _hits.Count, true, false, 0, _speechMs);
                RenderResults();
                if (clearWinner) Pick(_hits[0], 1);
                else SetFindStatus($"{_hits.Count} matches. Tap the one you mean.");
                return;
            }

            RunAiFind(text, method);
        }

        private void RunAiFind(string text, string method)
        {
            CancelAiFind();
            int localHits = _hits.Count;
            RenderResults();
            SetFindStatus("Not sure from the names; searching by meaning…");

            string id = _searchId;
            _pendingFind = AiClient.Ensure().Find(text, PartSearch.ForAi(), r =>
            {
                _pendingFind = null;
                if (this == null || id != _searchId) return;

                SearchLog.Search(id, method, text, localHits, false, true, r.latencyMs, _speechMs);

                if (!r.ok)
                {
                    SetFindStatus(_hits.Count > 0 ? $"Searching by meaning failed ({r.error}) Showing name matches." : r.error);
                    return;
                }

                var aiHits = new List<SearchHit>();
                foreach (FindMatch m in r.matches)
                {
                    if (_knowledge == null || !_knowledge.TryGet(m.path, out PartKnowledgeEntry e)) continue;
                    aiHits.Add(new SearchHit
                    {
                        path = m.path,
                        label = PartNames.SentenceCase(e.label),
                        detail = string.IsNullOrEmpty(m.why) ? e.function : m.why,
                        byMeaning = true,
                    });
                }

                var seen = new HashSet<string>(aiHits.Select(h => h.path));
                _hits = aiHits.Concat(_hits.Where(h => !seen.Contains(h.path))).Take(5).ToList();
                RenderResults();

                if (aiHits.Count == 1) Pick(aiHits[0], 1);
                else if (_hits.Count == 0) SetFindStatus("No part matches that. Try other words, or Ask about the whole model.");
                else SetFindStatus($"{_hits.Count} match{(_hits.Count == 1 ? "" : "es")}. Tap the one you mean.");
            });
        }

        private void CancelAiFind()
        {
            _pendingFind?.Cancel();
            _pendingFind = null;
        }

        private void StartSearchIfNew(string method)
        {
            if (!string.IsNullOrEmpty(_searchId) && !_picked) return;
            _searchId = Guid.NewGuid().ToString("N").Substring(0, 12);
            _searchStartedAt = Time.realtimeSinceStartup;
            _searchMethod = method;
            _speechMs = 0;
            _picked = false;
        }

        // ── Picking a result ───────────────────────────────────────────

        private void Pick(SearchHit hit, int rank)
        {
            _findPath = hit.path;

            if (_selector != null)
            {
                string current = _selector.OwnPath;
                if (current != hit.path) _selector.SelectByPath(hit.path);
            }

            if (!_picked)
            {
                _picked = true;
                SearchLog.Pick(_searchId, rank, hit.byMeaning, Time.realtimeSinceStartup - _searchStartedAt);
            }

            SetFindStatus($"Selected: {hit.label}. Ask to talk about it, or Isolate to see it alone.");
            RenderResults();
        }

        // ── Voice ──────────────────────────────────────────────────────

        private void OnMicPressed()
        {
            SpeechInput speech = SpeechInput.Ensure();
            if (speech.IsRecording)
            {
                speech.StopRecording();
                return;
            }

            CancelAiFind();
            _pendingSpeech?.Cancel();
            _picked = true;
            StartSearchIfNew("voice");
            _findInput.DeactivateInputField();

            speech.StartRecording(OnRecorded);
            if (speech.IsRecording) _listening = StartCoroutine(ShowListening(speech));
        }

        private IEnumerator ShowListening(SpeechInput speech)
        {
            SetMicLook(_micImage, _micIcon, true);
            while (speech != null && speech.IsRecording)
            {
                int bars = Mathf.Clamp(Mathf.RoundToInt(speech.Level * 60f), 0, 10);
                SetFindStatus($"Listening… {Mathf.Max(0f, SpeechInput.MaxSeconds - speech.Elapsed):0}s  " +
                              new string('|', bars) + "\nSay a part's name or what it does. Tap the mic again when done.");
                yield return null;
            }
            ResetMicButton();
            _listening = null;
        }

        private void OnRecorded(byte[] wav, string error)
        {
            if (this == null) return;
            ResetMicButton();

            if (wav == null)
            {
                SetFindStatus(error);
                return;
            }

            SetFindStatus("Working out what you said…");
            string id = _searchId;
            _pendingSpeech = AiClient.Ensure().Transcribe(wav, PartSearch.Vocabulary(), r =>
            {
                _pendingSpeech = null;
                if (this == null || id != _searchId) return;

                if (!r.ok) { SetFindStatus(r.error); return; }
                if (string.IsNullOrWhiteSpace(r.text))
                {
                    SetFindStatus("Didn't catch that. Try again, or type.");
                    return;
                }

                _speechMs = r.latencyMs;
                string heard = r.text.Length > MaxQueryChars ? r.text.Substring(0, MaxQueryChars) : r.text;
                _findInput.SetTextWithoutNotify(heard);
                Submit(heard, "voice");
            });
        }

        /// <summary>Stops any recording and pending voice or meaning search (the panel is closing).</summary>
        private void StopListening()
        {
            if (SpeechInput.Instance != null && SpeechInput.Instance.IsRecording) SpeechInput.Instance.Cancel();
            if (_listening != null) { StopCoroutine(_listening); _listening = null; }
            _pendingSpeech?.Cancel();
            _pendingSpeech = null;
            CancelAiFind();
            if (_micImage != null) ResetMicButton();
        }

        private void ResetMicButton() => SetMicLook(_micImage, _micIcon, false);

        /// <summary>A round mic button turns rose with a stop square while it is listening.</summary>
        private static void SetMicLook(Image disc, Image icon, bool listening)
        {
            if (disc != null) disc.color = listening ? ListeningColor : AuraTheme.Indigo;
            if (icon != null)
            {
                icon.sprite = AuraIcons.Get(listening ? AuraIcons.Stop : AuraIcons.Mic);
                icon.color = listening ? AuraTheme.Ink : AuraTheme.Text;
            }
        }

        // ── Rendering ──────────────────────────────────────────────────

        private void ClearResults()
        {
            _hits.Clear();
            RenderResults();
        }

        private void SetFindStatus(string text)
        {
            if (_findStatus != null) _findStatus.text = Esc(text ?? "");
        }

        private void RenderResults()
        {
            if (_findList == null) return;
            AuraKit.ClearChildren(_findList);

            for (int i = 0; i < _hits.Count; i++)
            {
                SearchHit hit = _hits[i];
                int rank = i + 1;
                bool selected = hit.path == _findPath;

                var row = AuraKit.Box(_findList, "Result", selected ? AuraTheme.A(AuraTheme.Lavender, 0.22f) : AuraTheme.A(AuraTheme.Indigo, 0.9f),
                                      AuraTheme.Radius - 6f, true);
                AuraKit.Border(row, selected ? AuraTheme.Lavender : AuraTheme.A(AuraTheme.Slate, 0.8f), AuraTheme.Radius - 6f);
                AuraKit.Size(row, -1, 150f);
                var hg = AuraKit.HStack(row, 12f);
                hg.padding = new RectOffset(28, 18, 14, 14);
                AuraKit.MakeButton(row.gameObject, row, () => Pick(hit, rank));

                var texts = AuraKit.Rect(row.transform, "Texts");
                AuraKit.VStack(texts, 4f, 0, TextAnchor.MiddleLeft);
                AuraKit.Size(texts, 0f, -1, 1f);
                var name = AuraKit.Text(texts, Esc(hit.label), AuraTheme.SizeBody - 2f, AuraTheme.Text, AuraWeight.SemiBold);
                name.enableWordWrapping = false;
                string detail = hit.byMeaning ? $"<color=#9894C4>by meaning ·</color> {Esc(hit.detail)}" : Esc(hit.detail);
                var sub = AuraKit.Text(texts, detail, AuraTheme.SizeCaption - 2f, AuraTheme.TextDim);
                sub.richText = true;
                sub.enableWordWrapping = false;

                AuraWidgets.Chip(row.transform, "Ask", AuraIcons.AI, true, () => { Pick(hit, rank); Open(hit.path); }, out _, 88f);
                AuraWidgets.Chip(row.transform, "Isolate", AuraIcons.Target, false, () =>
                {
                    Pick(hit, rank);
                    if (_isolator == null) return;
                    if (_isolator.IsolatedPath == hit.path) _isolator.ShowAll();
                    else _isolator.Isolate(hit.path);
                }, out _, 88f);
            }

            StartCoroutine(ScrollFindToTop());
        }

        private IEnumerator ScrollFindToTop()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_findScroll != null) _findScroll.verticalNormalizedPosition = 1f;
        }

        // ── Building ───────────────────────────────────────────────────

        private void BuildFindPage(RectTransform parent)
        {
            var page = AuraKit.Rect(parent, "FindPage");
            AuraKit.Size(page, -1, 200f, -1, 1f);
            AuraKit.VStack(page, 14f).childForceExpandHeight = false;
            _findPage = page.gameObject;

            var row = AuraKit.Rect(page, "SearchRow");
            AuraKit.HStack(row, 12f).childForceExpandHeight = false;
            AuraKit.Size(row, -1, 112f);
            _findInput = AuraWidgets.Input(row, "Name, job or where it is", "", false, 112f, MaxQueryChars);
            AuraKit.Flex(_findInput);
            _findInput.onValueChanged.AddListener(OnFindTyped);
            _findInput.onSubmit.AddListener(OnFindSubmitted);
            _micButton = AuraWidgets.Round(row, AuraIcons.Mic, 104f, false, OnMicPressed, out _micIcon);
            _micImage = (Image)_micButton.targetGraphic;
            _goButton = AuraWidgets.Round(row, AuraIcons.Search, 104f, true, OnGoPressed, out _);

            _findStatus = AuraKit.Text(page, "", AuraTheme.SizeSmall - 2f, AuraTheme.TextDim);
            _findStatus.enableWordWrapping = true;

            var results = AuraKit.Rect(page, "Results");
            AuraKit.Size(results, -1, 200f, -1, 1f);
            _findList = AuraWidgets.Scroll(results, out _findScroll, 12f);
            _findScroll.movementType = ScrollRect.MovementType.Clamped;

            var note = AuraKit.Text(page, "Voice is sent to Groq only to turn it into text. Searching by name works offline.",
                                    AuraTheme.SizeCaption - 5f, AuraTheme.A(AuraTheme.TextDim, 0.8f));
            note.enableWordWrapping = true;
        }
    }
}
