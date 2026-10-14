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
    ///   * Press GO (or Enter): if the phone is not sure, the AI matches by meaning
    ///     ("the part that burns fuel").
    ///   * MIC: say it instead. The recording becomes text (Groq Whisper, through the AI server),
    ///     is put in the box, and searched as if typed and sent.
    ///   * Tap a result to select it (highlighted, with the panel's line pointing at it); ASK opens
    ///     the chat about it; ISOLATE shows it on its own.
    ///
    /// In a room, picking a result goes through the normal selection rules: the presenter's pick is
    /// shared (gold), anyone else's stays on their own phone (cyan).
    /// </summary>
    public partial class PartChatUI
    {
        private const int MaxQueryChars = 120;
        private static readonly Color ListeningColor = new Color(0.75f, 0.22f, 0.25f, 1f);
        private static readonly Color MeaningTag = new Color(0.62f, 0.56f, 1f, 1f);

        private TMP_InputField  _findInput;
        private Button          _micButton;
        private Image           _micImage;
        private TextMeshProUGUI _micLabel;
        private Button          _goButton;
        private TextMeshProUGUI _findStatus;
        private ScrollRect      _findScroll;
        private RectTransform   _findList;

        private string _findPath = "";           // the part Find last picked (the panel's line points at it)
        private List<SearchHit> _hits = new List<SearchHit>();
        private string _searchId = "";
        private float  _searchStartedAt;
        private string _searchMethod = "text";
        private long   _speechMs;
        private bool   _picked;
        private AiClient.Pending _pendingFind;
        private AiClient.Pending _pendingSpeech;
        private Coroutine _listening;

        // ── Opening ────────────────────────────────────────────────────────────

        /// <summary>Opens the panel on the FIND tab.</summary>
        public void OpenFind()
        {
            if (string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) return;

            if (!IsOpen)
            {
                _panel.SetActive(true);
                PanelNavigator.Push(NavId, this, Close);
            }
            SetTab(Tab.Find);
            RefreshFindAvailability();
            if (_findInput.interactable) _findInput.ActivateInputField();
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
                SetFindStatus("This model has no part names to search. Use ASK to ask about the whole model.");
            }
            else if (_hits.Count == 0 && string.IsNullOrWhiteSpace(_findInput.text))
            {
                SetFindStatus("Type or say a part's name, what it does, or where it is.");
            }
        }

        // ── Searching ──────────────────────────────────────────────────────────

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
                ? "No name matches yet. Press GO to search by meaning."
                : $"{_hits.Count} match{(_hits.Count == 1 ? "" : "es")}. Tap one to select it.");
        }

        private void OnFindSubmitted(string text) => Submit(text, _searchMethod);

        private void OnGoPressed() => Submit(_findInput.text, "text");

        /// <summary>
        /// A finished query (GO, Enter, or what was heard). The phone's matches are used if it is sure;
        /// otherwise the AI matches by meaning. A single clear winner is selected straight away.
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
                if (clearWinner)
                {
                    Pick(_hits[0], 1);
                }
                else
                {
                    SetFindStatus($"{_hits.Count} matches. Tap the one you mean.");
                }
                return;
            }

            RunAiFind(text, method);
        }

        private void RunAiFind(string text, string method)
        {
            CancelAiFind();
            int localHits = _hits.Count;
            RenderResults();
            SetFindStatus("Not sure from the names; searching by meaning...");

            string id = _searchId;
            _pendingFind = AiClient.Ensure().Find(text, PartSearch.ForAi(), r =>
            {
                _pendingFind = null;
                if (this == null || id != _searchId) return; // a newer search has started

                SearchLog.Search(id, method, text, localHits, false, true, r.latencyMs, _speechMs);

                if (!r.ok)
                {
                    SetFindStatus(_hits.Count > 0
                        ? $"Searching by meaning failed ({r.error}) Showing name matches."
                        : r.error);
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

                // The AI's matches first, then any name matches it did not already give.
                var seen = new HashSet<string>(aiHits.Select(h => h.path));
                _hits = aiHits.Concat(_hits.Where(h => !seen.Contains(h.path))).Take(5).ToList();
                RenderResults();

                if (aiHits.Count == 1) Pick(aiHits[0], 1);
                else if (_hits.Count == 0) SetFindStatus("No part matches that. Try other words, or ASK about the whole model.");
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
            // One "search" lasts from the first keystroke (or the mic) until a part is picked.
            if (!string.IsNullOrEmpty(_searchId) && !_picked) return;
            _searchId = Guid.NewGuid().ToString("N").Substring(0, 12);
            _searchStartedAt = Time.realtimeSinceStartup;
            _searchMethod = method;
            _speechMs = 0;
            _picked = false;
        }

        // ── Picking a result ───────────────────────────────────────────────────

        private void Pick(SearchHit hit, int rank)
        {
            _findPath = hit.path;
            _placement = -1;

            // Selection obeys the room's rules (presenter shares, others keep it local). Picking what
            // is already selected must not toggle it off, so only select when it is not.
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

            SetFindStatus($"Selected: {hit.label}. ASK to talk about it, or ISOLATE to see it alone.");
            RenderResults();
        }

        // ── Voice ──────────────────────────────────────────────────────────────

        private void OnMicPressed()
        {
            SpeechInput speech = SpeechInput.Ensure();
            if (speech.IsRecording)
            {
                speech.StopRecording(); // the callback carries on from here
                return;
            }

            CancelAiFind();
            _pendingSpeech?.Cancel();
            _picked = true;               // a voice query is a new search
            StartSearchIfNew("voice");
            _findInput.DeactivateInputField();

            speech.StartRecording(OnRecorded);
            if (speech.IsRecording) _listening = StartCoroutine(ShowListening(speech));
        }

        private IEnumerator ShowListening(SpeechInput speech)
        {
            _micLabel.text = "STOP";
            _micImage.color = ListeningColor;
            while (speech != null && speech.IsRecording)
            {
                int bars = Mathf.Clamp(Mathf.RoundToInt(speech.Level * 60f), 0, 10);
                SetFindStatus($"Listening... {Mathf.Max(0f, SpeechInput.MaxSeconds - speech.Elapsed):0}s  " +
                              new string('|', bars) + "\nSay a part's name or what it does. Tap STOP when done.");
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

            SetFindStatus("Working out what you said...");
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
            if (_micLabel != null) ResetMicButton();
        }

        private void ResetMicButton()
        {
            _micLabel.text = "MIC";
            _micImage.color = ButtonColor;
        }

        // ── Rendering ──────────────────────────────────────────────────────────

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
            for (int i = _findList.childCount - 1; i >= 0; i--) Destroy(_findList.GetChild(i).gameObject);

            for (int i = 0; i < _hits.Count; i++)
            {
                SearchHit hit = _hits[i];
                int rank = i + 1;
                bool selected = hit.path == _findPath;

                var row = new GameObject("Result", typeof(RectTransform));
                row.transform.SetParent(_findList, false);
                var bg = row.AddComponent<Image>();
                bg.color = selected ? new Color(0.20f, 0.30f, 0.42f, 1f) : AiBubble;
                var btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(() => Pick(hit, rank));
                AddLayout(row, height: 92f);

                var hg = row.AddComponent<HorizontalLayoutGroup>();
                hg.padding = new RectOffset(14, 10, 8, 8);
                hg.spacing = 8;
                hg.childAlignment = TextAnchor.MiddleLeft;
                hg.childControlWidth = true; hg.childControlHeight = true;
                hg.childForceExpandWidth = false; hg.childForceExpandHeight = false;

                var texts = new GameObject("Texts", typeof(RectTransform));
                texts.transform.SetParent(row.transform, false);
                AddLayout(texts, flexWidth: 1f);
                var tv = texts.AddComponent<VerticalLayoutGroup>();
                tv.spacing = 2;
                tv.childControlWidth = true; tv.childControlHeight = true;
                tv.childForceExpandWidth = true; tv.childForceExpandHeight = false;

                TextMeshProUGUI name = MakeText(texts.transform, "Name", Esc(hit.label), 22, Color.white);
                name.fontStyle = FontStyles.Bold;
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;

                string detail = hit.byMeaning
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(MeaningTag)}>by meaning:</color> {Esc(hit.detail)}"
                    : Esc(hit.detail);
                TextMeshProUGUI sub = MakeText(texts.transform, "Detail", detail, 17, DimText);
                sub.enableWordWrapping = false;
                sub.overflowMode = TextOverflowModes.Ellipsis;

                MakeButton(row.transform, "Ask", "ASK", ButtonColor, () => { Pick(hit, rank); Open(hit.path); }, height: 56f, fontSize: 17, minWidth: 74f);
                MakeButton(row.transform, "Isolate", "ISOLATE", ButtonColor, () =>
                {
                    Pick(hit, rank);
                    if (_isolator == null) return;
                    if (_isolator.IsolatedPath == hit.path) _isolator.ShowAll();
                    else _isolator.Isolate(hit.path);
                }, height: 56f, fontSize: 16, minWidth: 104f);
            }

            StartCoroutine(ScrollFindToTop());
        }

        private IEnumerator ScrollFindToTop()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_findScroll != null) _findScroll.verticalNormalizedPosition = 1f;
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void BuildFindPage(Transform parent)
        {
            _findPage = new GameObject("FindPage", typeof(RectTransform));
            _findPage.transform.SetParent(parent, false);
            AddLayout(_findPage, height: 120f, flexHeight: 1f);
            var vg = _findPage.AddComponent<VerticalLayoutGroup>();
            vg.spacing = 8;
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var row = new GameObject("SearchRow", typeof(RectTransform));
            row.transform.SetParent(_findPage.transform, false);
            AddLayout(row, height: 60f);
            var hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 8;
            hg.childControlWidth = true; hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;

            _findInput = BuildInputField(row.transform, "Find a part: name, job, or where it is", MaxQueryChars, OnFindSubmitted);
            _findInput.onValueChanged.AddListener(OnFindTyped);

            _micButton = MakeButton(row.transform, "Mic", "MIC", ButtonColor, OnMicPressed, height: 60f, fontSize: 19, minWidth: 92f);
            _micImage = _micButton.GetComponent<Image>();
            _micLabel = _micButton.GetComponentInChildren<TextMeshProUGUI>();
            _goButton = MakeButton(row.transform, "Go", "GO", AskButtonColor, OnGoPressed, height: 60f, fontSize: 19, minWidth: 80f);

            _findStatus = MakeText(_findPage.transform, "Status", "", 18, DimText);
            _findStatus.enableWordWrapping = true;

            var scrollGo = new GameObject("Results", typeof(RectTransform));
            scrollGo.transform.SetParent(_findPage.transform, false);
            AddLayout(scrollGo, height: 100f, flexHeight: 1f);
            _findScroll = scrollGo.AddComponent<ScrollRect>();
            _findScroll.horizontal = false;
            _findScroll.vertical = true;
            _findScroll.movementType = ScrollRect.MovementType.Clamped;
            _findScroll.scrollSensitivity = 28f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero; vrt.pivot = new Vector2(0f, 1f);
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _findList = (RectTransform)content.transform;
            _findList.anchorMin = new Vector2(0f, 1f);
            _findList.anchorMax = new Vector2(1f, 1f);
            _findList.pivot = new Vector2(0f, 1f);
            _findList.sizeDelta = Vector2.zero;
            var cv = content.AddComponent<VerticalLayoutGroup>();
            cv.spacing = 8;
            cv.childControlWidth = true; cv.childControlHeight = true;
            cv.childForceExpandWidth = true; cv.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _findScroll.viewport = vrt;
            _findScroll.content = _findList;

            TextMeshProUGUI note = MakeText(_findPage.transform, "Note",
                "Voice is sent to Groq only to turn it into text. Searching by name works offline.", 15, DimText);
            note.enableWordWrapping = true;
        }
    }
}
