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
    /// The AI part assistant: select a part, tap ASK AI beside it, and a chat opens next to the part.
    ///
    ///   1. The first message is the part's facts from the knowledge file - instant, no AI, works
    ///      offline.
    ///   2. Suggested questions sit under it as tappable chips, plus a text box for your own.
    ///   3. Answers come from the AI server (ai-server/), given the part's facts, the model's summary
    ///      and the conversation so far.
    ///   4. The conversation carries on: close it and reopen it on the same part and it is still
    ///      there; open another part and that part gets its own conversation. All forgotten when a
    ///      different model loads.
    ///
    /// For a model with no part knowledge (the House) every chat is about the whole model.
    ///
    /// The panel has two tabs (Phase 4): **FIND** searches for a part by name, job or voice
    /// (PartChatUI.Search.cs), **ASK** is the chat. The AI button on the left rail opens ASK when a
    /// part is selected and FIND when nothing is; ASK AI beside a part always opens ASK.
    ///
    /// The panel sits on the opposite half of the screen from the part (top or bottom in portrait,
    /// left or right in landscape) with a thin line to it, so the part stays visible while you talk
    /// about it. It moves when the part crosses well past the middle, not on every small rotation.
    ///
    /// **Phase 3b:** in a room, every AI answer has a SHARE button: everyone then sees it on a card
    /// beside the part (SharedAnswerCard, SharedAnswers). ASK MORE on that card opens this chat on
    /// the part with the shared question and answer in it, so follow-ups carry on from there.
    /// </summary>
    public partial class PartChatUI : MonoBehaviour
    {
        public static PartChatUI Instance { get; private set; }

        private const string NavId = "chat";
        private const string RailId = "ai";
        private const int MaxQuestionChars = 300;
        private const int HistoryTurns = 8;
        private const string RetryChip = "Try again";
        private const string StopChip = "Stop waiting";

        private static readonly Color PanelColor     = new Color(0.07f, 0.09f, 0.13f, 0.97f);
        private static readonly Color AskButtonColor = new Color(0.36f, 0.30f, 0.85f, 0.97f);
        private static readonly Color UserBubble     = new Color(0.20f, 0.34f, 0.66f, 1f);
        private static readonly Color AiBubble       = new Color(0.16f, 0.18f, 0.24f, 1f);
        private static readonly Color IntroBubble    = new Color(0.13f, 0.22f, 0.24f, 1f);
        private static readonly Color ErrorBubble    = new Color(0.42f, 0.17f, 0.17f, 1f);
        private static readonly Color ChipColor      = new Color(0.22f, 0.26f, 0.36f, 1f);
        private static readonly Color ButtonColor    = new Color(0.22f, 0.26f, 0.36f, 0.95f);
        private static readonly Color LeaderColor    = new Color(0.62f, 0.56f, 1f, 0.85f);
        private static readonly Color DimText        = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color RatedColor     = new Color(0.20f, 0.52f, 0.42f, 1f);

        // ── UI references ──────────────────────────────────────────────────────
        private Canvas          _canvas;
        private RectTransform   _canvasRt;
        private RectTransform   _askButton;
        private GameObject      _panel;
        private RectTransform   _panelRt;
        private RectTransform   _leader;
        private TextMeshProUGUI _title;
        private GameObject      _isolateButton;
        private TextMeshProUGUI _isolateLabel;
        private ScrollRect      _scroll;
        private RectTransform   _messages;
        private RectTransform   _chips;
        private TMP_InputField  _input;
        private Button          _sendButton;

        private enum Tab { Ask, Find }
        private Tab             _tab = Tab.Ask;
        private GameObject      _askPage;
        private GameObject      _findPage;
        private Image           _askTabImage;
        private Image           _findTabImage;
        private static readonly Color TabOn  = new Color(0.36f, 0.30f, 0.85f, 1f);
        private static readonly Color TabOff = new Color(0.16f, 0.18f, 0.24f, 1f);

        // ── State ──────────────────────────────────────────────────────────────
        private ChatThread _thread;
        private string     _path = "";        // "" = the whole model
        private int        _placement = -1;   // which half the panel is in; -1 = not placed yet
        private bool       _railVisible;

        private ModelPartSelector _selector;
        private ModelIsolator     _isolator;
        private PartKnowledge     _knowledge;

        public bool IsOpen => _panel != null && _panel.activeSelf;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
        }

        void Start()
        {
            StartCoroutine(Bind());
            SharedAnswers.OnChanged += OnSharedAnswersChanged;
        }

        void OnDestroy()
        {
            SharedAnswers.OnChanged -= OnSharedAnswersChanged;
            if (_selector != null) _selector.OnSelectionChanged -= OnSelectionChanged;
            if (_isolator != null) _isolator.OnIsolationChanged -= RefreshIsolateButton;
            if (_knowledge != null) _knowledge.OnChanged -= OnKnowledgeChanged;
            if (Instance == this) Instance = null;
        }

        private IEnumerator Bind()
        {
            while (SpaceSessionHUD.Instance == null) yield return null;
            SpaceSessionHUD.Instance.AddRailButton(RailId, "AI", OnRailPressed);
            SpaceSessionHUD.Instance.SetSlotVisible(RailId, false);

            while (ModelPartSelector.Instance == null) yield return null;
            _selector = ModelPartSelector.Instance;
            _selector.OnSelectionChanged += OnSelectionChanged;

            while (ModelIsolator.Instance == null) yield return null;
            _isolator = ModelIsolator.Instance;
            _isolator.OnIsolationChanged += RefreshIsolateButton;

            // Created by SpaceSceneManager when the first model finishes loading.
            while (PartKnowledge.Instance == null) yield return null;
            _knowledge = PartKnowledge.Instance;
            _knowledge.OnChanged += OnKnowledgeChanged;
        }

        void LateUpdate()
        {
            // Hidden during the reassembly challenge: it is a game, not a lesson.
            bool modelShown = !string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey) &&
                              !AuraSync.Challenge.ReassemblyGame.IsActive;
            if (modelShown != _railVisible && SpaceSessionHUD.Instance != null)
            {
                _railVisible = modelShown;
                SpaceSessionHUD.Instance.SetSlotVisible(RailId, modelShown);
            }

            PlaceAskButton(modelShown);
            if (IsOpen) PlacePanel();
        }

        // ── Opening and closing ────────────────────────────────────────────────

        private void OnAskPressed() => Open(CurrentSelection());

        private void OnRailPressed()
        {
            if (IsOpen) { Close(); return; }
            string sel = CurrentSelection();
            if (!string.IsNullOrEmpty(sel) && _knowledge != null && _knowledge.HasParts) Open(sel);
            else OpenFind();
        }

        /// <summary>This device's own pick, else the presenter's, else "" (the whole model).</summary>
        private string CurrentSelection()
        {
            if (_selector == null) return "";
            return !string.IsNullOrEmpty(_selector.LocalPath) ? _selector.LocalPath : _selector.PresenterPath ?? "";
        }

        /// <summary>Opens the chat about a part ("" = the whole model), resuming its conversation.</summary>
        public void Open(string path)
        {
            if (string.IsNullOrEmpty(SpaceSceneManager.CurrentModelKey)) return;

            // A model without real part knowledge (a scan cut into chunks) only talks as a whole.
            if (_knowledge == null || !_knowledge.HasParts) path = "";

            _path = path ?? "";
            _thread = PartChatStore.Get(SpaceSceneManager.CurrentModelKey, _path, () => NewThread(_path));
            _placement = -1;

            _panel.SetActive(true);
            SetTab(Tab.Ask);
            Render();
            RefreshIsolateButton();
            PanelNavigator.Push(NavId, this, Close);
        }

        /// <summary>Shows one tab's page and lights its tab button.</summary>
        private void SetTab(Tab tab)
        {
            // A recording belongs to the tab it was started on.
            if (tab != Tab.Ask && _askListening != null) StopAskListening();
            if (tab != Tab.Find && _listening != null) StopListening();
            _tab = tab;
            _askPage.SetActive(tab == Tab.Ask);
            _findPage.SetActive(tab == Tab.Find);
            _askTabImage.color = tab == Tab.Ask ? TabOn : TabOff;
            _findTabImage.color = tab == Tab.Find ? TabOn : TabOff;
            _placement = -1; // the part the panel points at may have changed
            if (tab == Tab.Ask && _thread != null) _title.text = Esc(_thread.title);
            if (tab == Tab.Find) _title.text = "";
            RefreshIsolateButton();
        }

        private void OnAskTabPressed()
        {
            if (_tab == Tab.Ask) return;
            // Ask about what Find just picked, else the current selection, else the whole model.
            string path = !string.IsNullOrEmpty(_findPath) ? _findPath : CurrentSelection();
            Open(path);
        }

        /// <summary>The part the panel should point its line at: the chat's part, or Find's pick.</summary>
        private string FocusPath => _tab == Tab.Ask ? _path : _findPath;

        public void Close()
        {
            if (_panel == null || !_panel.activeSelf) return;
            if (_input != null) _input.DeactivateInputField();
            StopListening();
            StopAskListening();
            _panel.SetActive(false);
            _leader.gameObject.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        private void OnSelectionChanged()
        {
            // While the chat is open, picking another part moves the conversation to that part.
            // Clearing the selection (tapping empty space) leaves the chat where it is, and the
            // presenter's picks never move it - only this device's own.
            if (!IsOpen || _selector == null) return;
            string local = _selector.LocalPath;
            if (!string.IsNullOrEmpty(local) && local != _path && _knowledge != null && _knowledge.HasParts)
                Open(local);
        }

        private void OnKnowledgeChanged()
        {
            // A new model (or a reload of this one): the open conversation belongs to the old one.
            Close();
            _findPath = "";
            _hits.Clear();
            if (_findInput != null) _findInput.SetTextWithoutNotify("");
        }

        // ── Threads ────────────────────────────────────────────────────────────

        private ChatThread NewThread(string path)
        {
            var thread = new ChatThread();
            PartKnowledgeFile file = _knowledge != null ? _knowledge.Current : null;
            string modelName = ModelName();

            if (string.IsNullOrEmpty(path))
            {
                thread.title = modelName;
                string intro = $"<b>{Esc(modelName)}</b>";
                if (file != null && !string.IsNullOrEmpty(file.context) && file.context != modelName) intro += $"\n<color=#9AA4B8>{Esc(file.context)}</color>";
                intro += file != null && !string.IsNullOrEmpty(file.summary)
                    ? $"\n{Esc(file.summary)}"
                    : "\nAsk anything about this model.";
                thread.messages.Add(new ChatMessage { isIntro = true, text = intro });
                thread.suggestions = new List<string> { "How does it work?", "What is it used for?", "What are its main parts?" };
                return thread;
            }

            if (_knowledge != null && _knowledge.TryGet(path, out PartKnowledgeEntry e))
            {
                string label = PartNames.SentenceCase(e.label);
                thread.title = label;

                string intro = $"<b>{Esc(label)}</b>";
                if (!string.IsNullOrEmpty(e.function)) intro += $"\n{Esc(e.function)}";
                if (!string.IsNullOrEmpty(e.material)) intro += $"\n<color=#9AA4B8>Material:</color> {Esc(e.material)}";
                string touches = string.Join(", ", (e.connectsTo ?? new List<string>()).Select(p => PartNames.Display(p, "")).Where(s => s.Length > 0));
                if (touches.Length > 0) intro += $"\n<color=#9AA4B8>Touches:</color> {Esc(touches)}";
                string moves = MotionWords(e.motion != null ? e.motion.type : "");
                if (moves.Length > 0) intro += $"\n<color=#9AA4B8>Moves:</color> {moves} when the {Esc(modelName.ToLowerInvariant())} is working";

                thread.messages.Add(new ChatMessage { isIntro = true, text = intro });
                thread.suggestions = new List<string> { "Explain it simply", "Why is it made of that?", "What happens if it fails?" };
                if (e.motion != null && e.motion.type == "rotate") thread.suggestions[1] = "How fast does it spin?";
                return thread;
            }

            // A part with no entry of its own: usually a group of parts (the fan and turbine sit
            // under one group node). Describe it by what it contains.
            List<string> inside = PartsUnder(path);
            Transform t = ModelRaycaster.FindPart(ModelRoot(), path);
            thread.title = inside.Count > 0 ? string.Join(" and ", inside.Take(2)) + (inside.Count > 2 ? " and more" : "") : (t != null ? t.name : "Part");
            thread.messages.Add(new ChatMessage
            {
                isIntro = true,
                text = inside.Count > 0
                    ? $"<b>A group of parts</b>\nIt holds: {Esc(string.Join(", ", inside))}."
                    : $"<b>{Esc(thread.title)}</b>\nThere is no description for this part yet. You can still ask about it.",
            });
            thread.suggestions = new List<string> { "What do these parts do together?", "How does this fit into the whole model?" };
            return thread;
        }

        private static string MotionWords(string type)
        {
            switch (type)
            {
                case "rotate": return "spins";
                case "slide":  return "slides";
                case "hinge":  return "swings open";
                default:       return "";
            }
        }

        private List<string> PartsUnder(string path)
        {
            var labels = new List<string>();
            if (_knowledge == null) return labels;
            string prefix = path + "/";
            foreach (PartKnowledgeEntry e in _knowledge.AllParts)
                if (e.path.StartsWith(prefix) && !string.IsNullOrWhiteSpace(e.label)) labels.Add(PartNames.SentenceCase(e.label));
            return labels;
        }

        private string ModelName()
        {
            if (_knowledge != null && !string.IsNullOrEmpty(_knowledge.ModelName)) return _knowledge.ModelName;
            string key = SpaceSceneManager.CurrentModelKey;
            try { return System.IO.Path.GetFileNameWithoutExtension(new System.Uri(key).AbsolutePath).Replace("_mobile", ""); }
            catch { return "This model"; }
        }

        // ── Asking ─────────────────────────────────────────────────────────────

        private void OnSendPressed() => Send(_input != null ? _input.text : "", addUserMessage: true);

        private void OnChipPressed(string chip)
        {
            if (_thread == null) return;

            if (chip == StopChip)
            {
                _thread.pending?.Cancel();
                _thread.pending = null;
                _thread.waiting = false;
                _thread.messages.Add(new ChatMessage { isError = true, text = "Stopped. Ask again whenever you like." });
                _thread.suggestions = new List<string> { RetryChip };
                Render();
                return;
            }
            if (chip == RetryChip)
            {
                // Take the failure notice away and ask the same question again.
                if (_thread.messages.Count > 0 && _thread.messages[_thread.messages.Count - 1].isError)
                    _thread.messages.RemoveAt(_thread.messages.Count - 1);
                Send(_thread.lastQuestion, addUserMessage: false);
                return;
            }
            Send(chip, addUserMessage: true);
        }

        private void Send(string text, bool addUserMessage)
        {
            ChatThread thread = _thread;
            if (thread == null || thread.waiting) return;

            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxQuestionChars) text = text.Substring(0, MaxQuestionChars);

            AskBody body = BuildBody(thread, text);
            if (addUserMessage) thread.messages.Add(new ChatMessage { fromUser = true, text = text });
            thread.lastQuestion = text;
            thread.waiting = true;
            thread.pending = AiClient.Ensure().Ask(body, r => OnAnswer(thread, text, r));
            SharedStateBus.Publish(StateKeys.AiQuestion, thread.key ?? "", isLocal: true); // the heatmap counts it (Phase 10)

            if (_input != null) _input.text = "";
            Render();
        }

        private AskBody BuildBody(ChatThread thread, string question)
        {
            PartKnowledgeFile file = _knowledge != null ? _knowledge.Current : null;
            var body = new AskBody
            {
                question = question,
                model = new AskModel
                {
                    name = ModelName(),
                    context = file != null ? file.context ?? "" : "",
                    summary = file != null ? file.summary ?? "" : "",
                },
            };

            if (!string.IsNullOrEmpty(thread.key))
            {
                if (_knowledge != null && _knowledge.TryGet(thread.key, out PartKnowledgeEntry e))
                {
                    body.part = new AskPart
                    {
                        label = PartNames.SentenceCase(e.label),
                        function = e.function ?? "",
                        material = e.material ?? "",
                        connectsTo = (e.connectsTo ?? new List<string>()).Select(p => PartNames.Display(p, "")).Where(s => s.Length > 0).ToArray(),
                    };
                }
                else
                {
                    List<string> inside = PartsUnder(thread.key);
                    body.part = new AskPart
                    {
                        label = thread.title,
                        function = inside.Count > 0 ? "A group made up of: " + string.Join(", ", inside) : "",
                    };
                }
            }

            // Earlier turns, without the facts card and without failures.
            body.history = thread.messages
                .Where(m => !m.isIntro && !m.isError)
                .Select(m => new AskTurn { role = m.fromUser ? "user" : "assistant", text = m.text })
                .Reverse().Take(HistoryTurns).Reverse()
                .ToArray();
            return body;
        }

        private void OnAnswer(ChatThread thread, string question, AskResult r)
        {
            thread.waiting = false;
            thread.pending = null;

            if (r.ok)
            {
                var msg = new ChatMessage { text = r.answer, provider = r.provider, model = r.model, latencyMs = r.latencyMs };
                thread.messages.Add(msg);
                if (r.suggestions != null && r.suggestions.Length > 0) thread.suggestions = r.suggestions.Take(3).ToList();
                AiFeedbackLog.Answer(thread, msg, question);
            }
            else
            {
                thread.messages.Add(new ChatMessage { isError = true, text = r.error });
                thread.suggestions = new List<string> { RetryChip };
            }

            // The answer lands in its conversation even if the chat was closed or moved to
            // another part meanwhile; only redraw when that conversation is on screen.
            if (this != null && IsOpen && _thread == thread) Render();
        }

        // ── Sharing (Phase 3b) ─────────────────────────────────────────────────

        /// <summary>Puts one answer, with the question that led to it, on everyone's screen.</summary>
        private void Share(ChatMessage msg)
        {
            ChatThread thread = _thread;
            if (thread == null || !Photon.Pun.PhotonNetwork.InRoom) return;
            if (SharedAnswers.IsUp(msg.sharedId, SpaceSceneManager.CurrentModelKey)) return;

            // The question is the last thing the student asked before this answer.
            string question = "";
            for (int i = thread.messages.IndexOf(msg) - 1; i >= 0; i--)
                if (thread.messages[i].fromUser) { question = thread.messages[i].text; break; }

            string id = SharedAnswers.Share(SpaceSceneManager.CurrentModelKey, thread.key, thread.title, question, msg.text);
            if (id == null) return;

            msg.sharedId = id;
            AiFeedbackLog.Shared(thread, msg);
            Toast.Show("Shared with the room.");
            Render(keepScroll: true);
        }

        /// <summary>
        /// Opens the chat on a part ("" = the whole model) and, if a question is given, asks it at
        /// once. The simulation's AI button uses this to explain the current assembly step.
        /// </summary>
        public void AskAbout(string path, string question)
        {
            Open(path);
            if (!string.IsNullOrWhiteSpace(question) && _thread != null && !_thread.waiting)
                Send(question, addUserMessage: true);
        }

        /// <summary>
        /// ASK MORE on a shared answer: this chat, on that part, with the shared question and answer
        /// added (once) so the AI's next reply follows on from them.
        /// </summary>
        public void OpenShared(SharedAnswer a)
        {
            if (a == null) return;
            Open(a.path);
            if (_thread == null) return;

            if (!_thread.messages.Any(m => m.sharedId == a.id))
            {
                string who = string.IsNullOrEmpty(a.askerName) ? "Someone" : a.askerName;
                if (!string.IsNullOrEmpty(a.question))
                    _thread.messages.Add(new ChatMessage { fromUser = true, fromRoom = true, text = $"{who} asked: {a.question}" });
                _thread.messages.Add(new ChatMessage { fromRoom = true, sharedId = a.id, text = a.answer, provider = "shared" });
            }
            Render();
        }

        private void OnSharedAnswersChanged()
        {
            // A SHARE button flips to SHARED (or back, when the answer is removed).
            if (IsOpen && _tab == Tab.Ask && _thread != null && !_thread.waiting) Render(keepScroll: true);
        }

        private void Rate(ChatMessage msg, int rating)
        {
            msg.rating = msg.rating == rating ? 0 : rating; // tapping the same answer again clears it
            if (_thread != null) AiFeedbackLog.Rating(_thread, msg);
            Render(keepScroll: true);
        }

        // ── Isolate ────────────────────────────────────────────────────────────

        private void OnIsolatePressed()
        {
            if (_isolator == null || string.IsNullOrEmpty(_path)) return;
            if (_isolator.IsolatedPath == _path) _isolator.ShowAll();
            else _isolator.Isolate(_path);
        }

        private void RefreshIsolateButton()
        {
            if (_isolateButton == null) return;
            bool canIsolate = _tab == Tab.Ask && !string.IsNullOrEmpty(_path) && ModelRaycaster.FindPart(ModelRoot(), _path) != null;
            _isolateButton.SetActive(canIsolate);
            if (canIsolate && _isolator != null)
                _isolateLabel.text = _isolator.IsolatedPath == _path ? "SHOW ALL" : "ISOLATE";
        }

        // ── Rendering the conversation ─────────────────────────────────────────

        private void Render(bool keepScroll = false)
        {
            if (_thread == null) return;

            if (_tab == Tab.Ask) _title.text = Esc(_thread.title);

            for (int i = _messages.childCount - 1; i >= 0; i--) Destroy(_messages.GetChild(i).gameObject);
            foreach (ChatMessage m in _thread.messages) AddBubble(m);
            if (_thread.waiting) AddBubble(new ChatMessage { text = "Thinking..." }, thinking: true);

            for (int i = _chips.childCount - 1; i >= 0; i--) Destroy(_chips.GetChild(i).gameObject);
            IEnumerable<string> chips = _thread.waiting ? new[] { StopChip } : _thread.suggestions.Take(3);
            foreach (string chip in chips)
            {
                string c = chip;
                MakeButton(_chips, "Chip", c, ChipColor, () => OnChipPressed(c), height: 52f, fontSize: 19, alignLeft: true);
            }

            _input.interactable = !_thread.waiting;
            _sendButton.interactable = !_thread.waiting && _askListening == null && _pendingAskSpeech == null;
            if (_askMicButton != null) _askMicButton.interactable = !_thread.waiting && _pendingAskSpeech == null;

            if (keepScroll) StartCoroutine(RestoreScroll(_scroll != null ? _scroll.verticalNormalizedPosition : 0f));
            else StartCoroutine(ScrollToEnd());
        }

        private IEnumerator RestoreScroll(float position)
        {
            yield return null; // the rebuilt list has its size by now
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = position;
        }

        private void AddBubble(ChatMessage m, bool thinking = false)
        {
            // A row spanning the list, padded on one side so the bubble leans left (AI) or right (you).
            var row = new GameObject(m.fromUser ? "You" : "AI", typeof(RectTransform));
            row.transform.SetParent(_messages, false);
            var hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.padding = m.fromUser ? new RectOffset(110, 0, 0, 0) : new RectOffset(0, 110, 0, 0);
            hg.childControlWidth = true; hg.childControlHeight = true;
            hg.childForceExpandWidth = true; hg.childForceExpandHeight = false;

            Color color = m.fromUser ? UserBubble : m.isError ? ErrorBubble : m.isIntro ? IntroBubble : AiBubble;
            var bubble = new GameObject("Bubble", typeof(RectTransform));
            bubble.transform.SetParent(row.transform, false);
            bubble.AddComponent<Image>().color = color;
            var vg = bubble.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(16, 16, 12, 12);
            vg.spacing = 8;
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            // The facts card is built with rich-text markup on purpose; everything else is shown as typed.
            string shown = m.isIntro ? m.text : Esc(m.text);
            TextMeshProUGUI text = MakeText(bubble.transform, "Text", shown, 21, thinking ? DimText : Color.white);
            text.richText = true;
            text.enableWordWrapping = true;
            if (thinking) text.fontStyle = FontStyles.Italic;

            // "Helpful?" under real AI answers: the paper's helpfulness number. Plus SHARE in a room.
            // Not under someone else's shared answer: it is theirs to rate, and already shared.
            if (!m.fromUser && !m.isIntro && !m.isError && !thinking && !m.fromRoom)
            {
                var fb = new GameObject("Feedback", typeof(RectTransform));
                fb.transform.SetParent(bubble.transform, false);
                var fh = fb.AddComponent<HorizontalLayoutGroup>();
                fh.spacing = 10; fh.childAlignment = TextAnchor.MiddleLeft;
                fh.childControlWidth = true; fh.childControlHeight = true;
                fh.childForceExpandWidth = false; fh.childForceExpandHeight = false;

                TextMeshProUGUI q = MakeText(fb.transform, "Q", "Helpful?", 17, DimText);
                q.enableWordWrapping = false;
                ChatMessage msg = m;
                MakeButton(fb.transform, "Yes", "Yes", msg.rating > 0 ? RatedColor : ButtonColor, () => Rate(msg, 1), height: 40f, fontSize: 17, minWidth: 70f);
                MakeButton(fb.transform, "No", "No", msg.rating < 0 ? ErrorBubble : ButtonColor, () => Rate(msg, -1), height: 40f, fontSize: 17, minWidth: 70f);

                if (Photon.Pun.PhotonNetwork.InRoom)
                {
                    bool up = SharedAnswers.IsUp(msg.sharedId, SpaceSceneManager.CurrentModelKey);
                    Button share = MakeButton(fb.transform, "Share", up ? "SHARED" : "SHARE", up ? RatedColor : AskButtonColor,
                                              () => Share(msg), height: 40f, fontSize: 17, minWidth: 96f);
                    share.interactable = !up;
                }
            }
        }

        private IEnumerator ScrollToEnd()
        {
            yield return null; // let the layout settle first
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        // ── Placement ──────────────────────────────────────────────────────────

        private void PlaceAskButton(bool modelShown)
        {
            string path = CurrentSelection();
            bool show = modelShown && !IsOpen && !string.IsNullOrEmpty(path) && TryGetScreenRect(path, out Rect r);
            if (!show)
            {
                if (_askButton.gameObject.activeSelf) _askButton.gameObject.SetActive(false);
                return;
            }

            TryGetScreenRect(path, out Rect rect);
            // Just right of the part's top-right corner, kept clear of the rail, badges and bars.
            float x = Mathf.Clamp(rect.xMax + 12f, Screen.width * 0.16f, Screen.width * 0.70f);
            float y = Mathf.Clamp(rect.yMax, Screen.height * 0.24f, Screen.height * 0.80f);
            _askButton.position = new Vector3(x, y, 0f);
            if (!_askButton.gameObject.activeSelf) _askButton.gameObject.SetActive(true);
        }

        private void PlacePanel()
        {
            bool landscape = Screen.width > Screen.height;
            string focus = FocusPath;
            bool havePart = !string.IsNullOrEmpty(focus) && TryGetScreenRect(focus, out _);
            Rect part = default;
            if (havePart) TryGetScreenRect(focus, out part);

            // Which half: the one the part is not in. Hysteresis stops it flapping near the middle.
            int want = _placement;
            if (!havePart)
            {
                if (want < 0) want = 0;
            }
            else if (landscape)
            {
                float nx = part.center.x / Screen.width;
                if (want < 0) want = nx < 0.5f ? 3 : 2;
                else if (nx < 0.38f) want = 3;   // part on the left  -> panel right
                else if (nx > 0.62f) want = 2;   // part on the right -> panel left
            }
            else
            {
                float ny = part.center.y / Screen.height;
                if (want < 0) want = ny > 0.5f ? 0 : 1;
                else if (ny > 0.62f) want = 0;   // part high -> panel low
                else if (ny < 0.38f) want = 1;   // part low  -> panel high
            }

            if (want != _placement)
            {
                _placement = want;
                switch (want)
                {
                    case 0: SetAnchors(_panelRt, 0.04f, 0.22f, 0.96f, 0.60f); break; // bottom (portrait)
                    case 1: SetAnchors(_panelRt, 0.04f, 0.46f, 0.96f, 0.83f); break; // top (portrait)
                    case 2: SetAnchors(_panelRt, 0.16f, 0.08f, 0.56f, 0.86f); break; // left (landscape)
                    case 3: SetAnchors(_panelRt, 0.58f, 0.08f, 0.97f, 0.86f); break; // right (landscape)
                }
            }

            DrawLeader(havePart, part);
        }

        private void DrawLeader(bool havePart, Rect part)
        {
            if (!havePart)
            {
                _leader.gameObject.SetActive(false);
                return;
            }

            var corners = new Vector3[4];
            _panelRt.GetWorldCorners(corners); // an overlay canvas's world space is screen pixels
            var panel = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);

            Vector2 target = part.center;
            if (panel.Contains(target))
            {
                _leader.gameObject.SetActive(false);
                return;
            }

            Vector2 from = new Vector2(Mathf.Clamp(target.x, panel.xMin, panel.xMax), Mathf.Clamp(target.y, panel.yMin, panel.yMax));
            Vector2 d = target - from;
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

            _leader.position = (from + target) * 0.5f;
            _leader.sizeDelta = new Vector2(d.magnitude / scale, 4f);
            _leader.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            _leader.gameObject.SetActive(true);
        }

        /// <summary>The part's outline on screen, in pixels. False when it is not drawn or behind the camera.</summary>
        internal static bool TryGetScreenRect(string path, out Rect rect)
        {
            rect = default;
            Camera cam = Camera.main;
            Transform part = ModelRaycaster.FindPart(ModelRoot(), path);
            if (cam == null || part == null) return false;

            Bounds b = default;
            bool any = false;
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || r.gameObject.name == ModelRaycaster.HighlightName) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return false;

            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            bool inFront = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 s = cam.WorldToScreenPoint(c);
                if (s.z <= 0f) continue;
                inFront = true;
                xMin = Mathf.Min(xMin, s.x); xMax = Mathf.Max(xMax, s.x);
                yMin = Mathf.Min(yMin, s.y); yMax = Mathf.Max(yMax, s.y);
            }
            if (!inFront) return false;

            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return rect.xMax > 0 && rect.yMax > 0 && rect.xMin < Screen.width && rect.yMin < Screen.height;
        }

        private static Transform ModelRoot() =>
            SpaceInteractionController.Instance != null ? SpaceInteractionController.Instance.ModelRoot : null;

        // ── Building the UI ────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 98; // above the parts drawer (97), below the room panel (100)
            _canvasRt = (RectTransform)transform;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // The line from the panel to the part, drawn under the panel.
            var leader = new GameObject("Leader", typeof(RectTransform));
            leader.transform.SetParent(transform, false);
            _leader = (RectTransform)leader.transform;
            _leader.anchorMin = _leader.anchorMax = new Vector2(0.5f, 0.5f);
            _leader.pivot = new Vector2(0.5f, 0.5f);
            var li = leader.AddComponent<Image>();
            li.color = LeaderColor;
            li.raycastTarget = false;
            leader.SetActive(false);

            // The floating button beside the selected part.
            Button ask = MakeButton(transform, "AskAI", "ASK AI", AskButtonColor, OnAskPressed, height: 64f, fontSize: 22, minWidth: 150f);
            _askButton = (RectTransform)ask.transform;
            _askButton.anchorMin = _askButton.anchorMax = Vector2.zero;
            _askButton.pivot = new Vector2(0f, 0.5f);
            _askButton.sizeDelta = new Vector2(170f, 64f);
            ask.gameObject.SetActive(false);

            BuildPanel();
        }

        private void BuildPanel()
        {
            _panel = new GameObject("ChatPanel", typeof(RectTransform));
            _panel.transform.SetParent(transform, false);
            _panelRt = (RectTransform)_panel.transform;
            SetAnchors(_panelRt, 0.04f, 0.22f, 0.96f, 0.60f);
            _panel.AddComponent<Image>().color = PanelColor;
            _panel.AddComponent<TouchBlocker>(); // the model must not turn under the chat

            var vg = _panel.AddComponent<VerticalLayoutGroup>();
            vg.padding = new RectOffset(14, 14, 12, 12);
            vg.spacing = 10;
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            // Header: title, isolate, close.
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(_panel.transform, false);
            AddLayout(header, height: 58f);
            var hg = header.AddComponent<HorizontalLayoutGroup>();
            hg.spacing = 10; hg.childAlignment = TextAnchor.MiddleLeft;
            hg.childControlWidth = true; hg.childControlHeight = true;
            hg.childForceExpandWidth = false; hg.childForceExpandHeight = true;

            Button findTab = MakeButton(header.transform, "FindTab", "FIND", TabOff, OpenFind, height: 52f, fontSize: 18, minWidth: 96f);
            _findTabImage = findTab.GetComponent<Image>();
            Button askTab = MakeButton(header.transform, "AskTab", "ASK", TabOn, OnAskTabPressed, height: 52f, fontSize: 18, minWidth: 96f);
            _askTabImage = askTab.GetComponent<Image>();

            _title = MakeText(header.transform, "Title", "", 25, Color.white);
            _title.fontStyle = FontStyles.Bold;
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _title.alignment = TextAlignmentOptions.MidlineLeft;
            AddLayout(_title.gameObject, flexWidth: 1f);

            Button iso = MakeButton(header.transform, "Isolate", "ISOLATE", ButtonColor, OnIsolatePressed, height: 52f, fontSize: 18, minWidth: 130f);
            _isolateButton = iso.gameObject;
            _isolateLabel = iso.GetComponentInChildren<TextMeshProUGUI>();
            MakeButton(header.transform, "Close", "X", new Color(0.45f, 0.20f, 0.22f, 1f), Close, height: 52f, fontSize: 22, minWidth: 56f);

            _askPage = new GameObject("AskPage", typeof(RectTransform));
            _askPage.transform.SetParent(_panel.transform, false);
            AddLayout(_askPage, height: 120f, flexHeight: 1f);
            var ap = _askPage.AddComponent<VerticalLayoutGroup>();
            ap.spacing = 10;
            ap.childControlWidth = true; ap.childControlHeight = true;
            ap.childForceExpandWidth = true; ap.childForceExpandHeight = false;

            BuildMessageList(_askPage.transform);

            // Suggested questions.
            var chips = new GameObject("Suggestions", typeof(RectTransform));
            chips.transform.SetParent(_askPage.transform, false);
            _chips = (RectTransform)chips.transform;
            var cg = chips.AddComponent<VerticalLayoutGroup>();
            cg.spacing = 6;
            cg.childControlWidth = true; cg.childControlHeight = true;
            cg.childForceExpandWidth = true; cg.childForceExpandHeight = false;

            // Your own question.
            var inputRow = new GameObject("InputRow", typeof(RectTransform));
            inputRow.transform.SetParent(_askPage.transform, false);
            AddLayout(inputRow, height: 60f);
            var ig = inputRow.AddComponent<HorizontalLayoutGroup>();
            ig.spacing = 10;
            ig.childControlWidth = true; ig.childControlHeight = true;
            ig.childForceExpandWidth = false; ig.childForceExpandHeight = true;

            _input = BuildInput(inputRow.transform);
            BuildAskMic(inputRow.transform);
            _sendButton = MakeButton(inputRow.transform, "Send", "SEND", AskButtonColor, OnSendPressed, height: 60f, fontSize: 20, minWidth: 110f);

            TextMeshProUGUI note = MakeText(_askPage.transform, "Note",
                "AI answers can be wrong. Questions are sent to Google and Groq: don't type personal details.", 15, DimText);
            note.enableWordWrapping = true;

            BuildFindPage(_panel.transform);
            SetTab(Tab.Ask);

            _panel.SetActive(false);
        }

        private void BuildMessageList(Transform parent)
        {
            var scrollGo = new GameObject("Messages", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            AddLayout(scrollGo, height: 120f, flexHeight: 1f);

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 28f;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero; vrt.pivot = new Vector2(0f, 1f);
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f); // catches the scroll drag
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _messages = (RectTransform)content.transform;
            _messages.anchorMin = new Vector2(0f, 1f);
            _messages.anchorMax = new Vector2(1f, 1f);
            _messages.pivot = new Vector2(0f, 1f);
            _messages.sizeDelta = Vector2.zero;

            var vg = content.AddComponent<VerticalLayoutGroup>();
            vg.spacing = 10;
            vg.padding = new RectOffset(0, 0, 4, 4);
            vg.childControlWidth = true; vg.childControlHeight = true;
            vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = vrt;
            _scroll.content = _messages;
        }

        private TMP_InputField BuildInput(Transform parent) =>
            BuildInputField(parent, "Ask your own question...", MaxQuestionChars, _ => OnSendPressed());

        private TMP_InputField BuildInputField(Transform parent, string hint, int limit, UnityEngine.Events.UnityAction<string> onSubmit)
        {
            var go = new GameObject("Question", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(false); // TMP_InputField wires itself up in OnEnable: give it its parts first
            AddLayout(go, flexWidth: 1f);
            go.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 1f);

            var area = new GameObject("TextArea", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(12f, 4f); art.offsetMax = new Vector2(-12f, -4f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI placeholder = MakeText(area.transform, "Placeholder", hint, 20, new Color(1f, 1f, 1f, 0.32f));
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            TextMeshProUGUI text = MakeText(area.transform, "Text", "", 20, Color.white);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport = art;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = limit;
            field.onSubmit.AddListener(onSubmit);

            go.SetActive(true);
            return field;
        }

        // ── Small UI helpers ───────────────────────────────────────────────────

        private static Button MakeButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction onClick,
                                         float height, int fontSize, float minWidth = 0f, bool alignLeft = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            AddLayout(go, height: height, minWidth: minWidth);

            TextMeshProUGUI t = MakeText(go.transform, "Label", label, fontSize, Color.white);
            Stretch(t.rectTransform, alignLeft ? 14f : 6f);
            t.alignment = alignLeft ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            t.fontStyle = alignLeft ? FontStyles.Normal : FontStyles.Bold;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return btn;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        private static void AddLayout(GameObject go, float height = 0f, float flexHeight = 0f, float flexWidth = 0f, float minWidth = 0f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (height > 0f) { le.minHeight = height; le.preferredHeight = height; }
            // A fixed-height row must not grow. A row laid out to fill its own height tells its
            // parent it can stretch, and the panel then hands it spare space: that is what made the
            // header four times too tall and squeezed the conversation.
            if (height > 0f && flexHeight <= 0f) le.flexibleHeight = 0f;
            if (flexHeight > 0f) le.flexibleHeight = flexHeight;
            if (flexWidth > 0f) le.flexibleWidth = flexWidth;
            if (minWidth > 0f) { le.minWidth = minWidth; le.preferredWidth = minWidth; }
        }

        private static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, 0f); rt.offsetMax = new Vector2(-inset, 0f);
        }

        private static void SetAnchors(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>Text from the knowledge file shown inside rich-text markup: stop it being read as tags.</summary>
        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }
}
