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
    /// The AI assistant (dock tab AI, or the Ask AI chip beside a selected part). It slides in from
    /// the right and takes the right side of the screen. There is no panel box: a soft fade from the
    /// screen edge, and every message on its own bubble, so it reads as see-through while the text
    /// never sits directly on the model.
    ///
    ///   [Find | Ask]                              (⋯)     Ask by default
    ///   About · Fan rotor                     (Isolate)
    ///   ┌ facts from the knowledge file ┐                 instant, offline
    ///                     ┌ your question ┐
    ///   ┌ the AI's answer · Helpful? Yes No Share ┐
    ///   (suggested question) (suggested question)
    ///   [Ask about this part…          ] (mic) (send)
    ///
    /// Conversations are kept per model and per part, on the phone (PartChatStore): closing the
    /// panel, switching parts or reopening the model later carries on where it left off. "Clear
    /// chat" under ⋯ removes them. In a room every AI answer can be shared with everyone (Phase 3b).
    /// Find (PartChatUI.Search.cs) searches for a part by name, job or voice.
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

        // ── UI references ──────────────────────────────────────────────
        private Canvas _canvas;
        private RectTransform _safe;
        private RectTransform _askChip;
        private AuraSlidePanel _panel;
        private RectTransform _panelRt;
        private System.Action<int> _setTab;
        private TextMeshProUGUI _about;
        private GameObject _isolateButton;
        private TextMeshProUGUI _isolateLabel;
        private GameObject _menu;
        private ScrollRect _scroll;
        private RectTransform _messages;
        private RectTransform _chips;
        private TMP_InputField _input;
        private Button _sendButton;

        private enum Tab { Ask, Find }
        private Tab _tab = Tab.Ask;
        private GameObject _askPage;
        private GameObject _findPage;

        // ── State ──────────────────────────────────────────────────────
        private ChatThread _thread;
        private string _path = "";        // "" = the whole model
        private bool _railVisible;

        private ModelPartSelector _selector;
        private ModelIsolator _isolator;
        private PartKnowledge _knowledge;

        public bool IsOpen => _panel != null && _panel.IsShown;

        // ── Lifecycle ──────────────────────────────────────────────────

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
            PanelNavigator.Remove(NavId);
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
                if (!modelShown) Close();
            }
            PlaceAskChip(modelShown);
        }

        // ── Opening and closing ────────────────────────────────────────

        private void OnAskPressed() => Open(CurrentSelection());

        /// <summary>The dock tab: opens on Ask (about the selected part, else the whole model).</summary>
        private void OnRailPressed()
        {
            if (IsOpen) { Close(); return; }
            Open(CurrentSelection());
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
            if (_knowledge == null || !_knowledge.HasParts) path = "";

            _path = path ?? "";
            _thread = PartChatStore.Get(SpaceSceneManager.CurrentModelKey, _path, () => NewThread(_path));

            ShowPanel();
            SetTab(Tab.Ask);
            Render();
            RefreshIsolateButton();
        }

        private void ShowPanel()
        {
            if (IsOpen) return;
            ViewerPanels.Opened(RailId, Close);
            PanelNavigator.Push(NavId, this, Close);
            SpaceSessionHUD.Instance?.SetDockHidden(true); // the panel takes the right side
            _panel.Show();
        }

        private void SetTab(Tab tab)
        {
            if (tab != Tab.Ask && _askListening != null) StopAskListening();
            if (tab != Tab.Find && _listening != null) StopListening();
            _tab = tab;
            _askPage.SetActive(tab == Tab.Ask);
            _findPage.SetActive(tab == Tab.Find);
            _setTab(tab == Tab.Ask ? 1 : 0);
            if (_menu != null) _menu.SetActive(false);
            RefreshAbout();
            RefreshIsolateButton();
        }

        private void OnTabChanged(int i)
        {
            if (i == 1)
            {
                if (_tab == Tab.Ask) return;
                // Ask about what Find just picked, else the current selection, else the whole model.
                string path = !string.IsNullOrEmpty(_findPath) ? _findPath : CurrentSelection();
                Open(path);
            }
            else OpenFind();
        }

        private void RefreshAbout()
        {
            if (_about == null) return;
            if (_tab == Tab.Find) { _about.text = "<color=#A9A8CC>Find a part by its name, job or where it is</color>"; return; }
            string title = _thread != null ? _thread.title : "";
            _about.text = string.IsNullOrEmpty(_path)
                ? $"<color=#A9A8CC>About ·</color> {Esc(title)} <color=#A9A8CC>(whole model)</color>"
                : $"<color=#A9A8CC>About ·</color> {Esc(title)}";
        }

        public void Close()
        {
            if (!IsOpen) return;
            if (_input != null) _input.DeactivateInputField();
            StopListening();
            StopAskListening();
            _panel.Hide();
            if (_menu != null) _menu.SetActive(false);
            ViewerPanels.Closed(RailId);
            PanelNavigator.Remove(NavId);
            SpaceSessionHUD.Instance?.SetDockHidden(false);
        }

        private void OnSelectionChanged()
        {
            // While the chat is open, picking another part moves the conversation to that part.
            // Clearing the selection leaves it where it is; the presenter's picks never move it.
            if (!IsOpen || _selector == null) return;
            string local = _selector.LocalPath;
            if (_tab == Tab.Ask && !string.IsNullOrEmpty(local) && local != _path && _knowledge != null && _knowledge.HasParts)
                Open(local);
        }

        private void OnKnowledgeChanged()
        {
            // A new model (or a reload): the open conversation belongs to the old one.
            Close();
            _findPath = "";
            _hits.Clear();
            if (_findInput != null) _findInput.SetTextWithoutNotify("");
        }

        // ── Threads ────────────────────────────────────────────────────

        private ChatThread NewThread(string path)
        {
            var thread = new ChatThread();
            PartKnowledgeFile file = _knowledge != null ? _knowledge.Current : null;
            string modelName = ModelName();

            if (string.IsNullOrEmpty(path))
            {
                thread.title = modelName;
                string intro = $"<b>{Esc(modelName)}</b>";
                if (file != null && !string.IsNullOrEmpty(file.context) && file.context != modelName) intro += $"\n<color=#A9A8CC>{Esc(file.context)}</color>";
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
                if (!string.IsNullOrEmpty(e.material)) intro += $"\n<color=#A9A8CC>Material:</color> {Esc(e.material)}";
                string touches = string.Join(", ", (e.connectsTo ?? new List<string>()).Select(p => PartNames.Display(p, "")).Where(s => s.Length > 0));
                if (touches.Length > 0) intro += $"\n<color=#A9A8CC>Touches:</color> {Esc(touches)}";
                string moves = MotionWords(e.motion != null ? e.motion.type : "");
                if (moves.Length > 0) intro += $"\n<color=#A9A8CC>Moves:</color> {moves} when the {Esc(modelName.ToLowerInvariant())} is working";

                thread.messages.Add(new ChatMessage { isIntro = true, text = intro });
                thread.suggestions = new List<string> { "Explain it simply", "Why is it made of that?", "What happens if it fails?" };
                if (e.motion != null && e.motion.type == "rotate") thread.suggestions[1] = "How fast does it spin?";
                return thread;
            }

            // A part with no entry of its own: usually a group of parts. Describe it by what it holds.
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
                case "slide": return "slides";
                case "hinge": return "swings open";
                default: return "";
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

        // ── Asking ─────────────────────────────────────────────────────

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
                PartChatStore.Save();
                Render();
                return;
            }
            if (chip == RetryChip)
            {
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
            PartChatStore.Save();

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
            PartChatStore.Save();

            // The answer lands in its conversation even if the chat was closed or moved meanwhile.
            if (this != null && IsOpen && _thread == thread && _tab == Tab.Ask) Render();
        }

        // ── Sharing (Phase 3b) ─────────────────────────────────────────

        private void Share(ChatMessage msg)
        {
            ChatThread thread = _thread;
            if (thread == null || !Photon.Pun.PhotonNetwork.InRoom) return;
            if (SharedAnswers.IsUp(msg.sharedId, SpaceSceneManager.CurrentModelKey)) return;

            string question = "";
            for (int i = thread.messages.IndexOf(msg) - 1; i >= 0; i--)
                if (thread.messages[i].fromUser) { question = thread.messages[i].text; break; }

            string id = SharedAnswers.Share(SpaceSceneManager.CurrentModelKey, thread.key, thread.title, question, msg.text);
            if (id == null) return;

            msg.sharedId = id;
            AiFeedbackLog.Shared(thread, msg);
            PartChatStore.Save();
            Toast.Show("Shared with the room.");
            Render(keepScroll: true);
        }

        /// <summary>
        /// Opens the chat on a part ("" = the whole model) and, if a question is given, asks it at
        /// once. The simulation's Explain button uses this.
        /// </summary>
        public void AskAbout(string path, string question)
        {
            Open(path);
            if (!string.IsNullOrWhiteSpace(question) && _thread != null && !_thread.waiting)
                Send(question, addUserMessage: true);
        }

        /// <summary>ASK MORE on a shared answer: this chat, on that part, with the shared question and answer added once.</summary>
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
                PartChatStore.Save();
            }
            Render();
        }

        private void OnSharedAnswersChanged()
        {
            if (IsOpen && _tab == Tab.Ask && _thread != null && !_thread.waiting) Render(keepScroll: true);
        }

        private void Rate(ChatMessage msg, int rating)
        {
            msg.rating = msg.rating == rating ? 0 : rating;
            if (_thread != null) AiFeedbackLog.Rating(_thread, msg);
            PartChatStore.Save();
            Render(keepScroll: true);
        }

        /// <summary>⋯ → Clear chat: forgets this model's conversations and starts this one fresh.</summary>
        private void ClearChats()
        {
            if (_menu != null) _menu.SetActive(false);
            string key = SpaceSceneManager.CurrentModelKey;
            if (string.IsNullOrEmpty(key)) return;
            PartChatStore.Clear(key);
            _thread = PartChatStore.Get(key, _path, () => NewThread(_path));
            if (_tab == Tab.Ask) Render();
            Toast.Show("Chats for this model cleared.");
        }

        // ── Isolate ────────────────────────────────────────────────────

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
                _isolateLabel.text = _isolator.IsolatedPath == _path ? "Show all" : "Isolate";
        }

        // ── Rendering the conversation ─────────────────────────────────

        private void Render(bool keepScroll = false)
        {
            if (_thread == null) return;
            RefreshAbout();

            AuraKit.ClearChildren(_messages);
            foreach (ChatMessage m in _thread.messages) AddBubble(m);
            if (_thread.waiting) AddBubble(new ChatMessage { text = "Thinking…" }, thinking: true);

            AuraKit.ClearChildren(_chips);
            IEnumerable<string> chips = _thread.waiting ? new[] { StopChip } : _thread.suggestions.Take(3);
            foreach (string chip in chips)
            {
                string c = chip;
                var row = AuraKit.Rect(_chips, "ChipRow");
                AuraKit.HStack(row, 0f, 0, TextAnchor.MiddleRight).childForceExpandHeight = true;
                AuraKit.Size(row, -1, 84f, -1, 0f);
                var b = AuraKit.Pill(row, c, null, AuraTheme.A(AuraTheme.Ink, 0.85f), AuraTheme.Lavender,
                                     () => OnChipPressed(c), 84f, AuraTheme.A(AuraTheme.Lavender, 0.6f));
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null)
                {
                    t.fontSize = AuraTheme.SizeSmall - 2f;
                    t.fontStyle = FontStyles.Normal;
                    t.overflowMode = TextOverflowModes.Ellipsis;
                }
            }

            _input.interactable = !_thread.waiting;
            _sendButton.interactable = !_thread.waiting && _askListening == null && _pendingAskSpeech == null;
            if (_askMicButton != null) _askMicButton.interactable = !_thread.waiting && _pendingAskSpeech == null;

            if (keepScroll) StartCoroutine(RestoreScroll(_scroll != null ? _scroll.verticalNormalizedPosition : 0f));
            else StartCoroutine(ScrollToEnd());
        }

        private IEnumerator RestoreScroll(float position)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = position;
        }

        private IEnumerator ScrollToEnd()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>One message on its own bubble: yours on the right in lavender, the AI's on the left.</summary>
        private void AddBubble(ChatMessage m, bool thinking = false)
        {
            var row = AuraKit.Rect(_messages, m.fromUser ? "You" : "AI");
            var hg = AuraKit.HStack(row, 0f, 0, m.fromUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft);
            hg.padding = m.fromUser ? new RectOffset(120, 0, 0, 0) : new RectOffset(0, 70, 0, 0);
            hg.childForceExpandWidth = false; // short messages hug their text; long ones wrap at the row's width

            Color fill, border; Color ink = AuraTheme.Text;
            if (m.fromUser) { fill = AuraTheme.A(AuraTheme.Lavender, 0.94f); border = fill; ink = AuraTheme.Ink; }
            else if (m.isError) { fill = AuraTheme.A(AuraTheme.Rose, 0.22f); border = AuraTheme.Rose; }
            else if (m.isIntro) { fill = AuraTheme.A(AuraTheme.Ink, 0.88f); border = AuraTheme.A(AuraTheme.Lavender, 0.55f); }
            else { fill = AuraTheme.A(AuraTheme.Indigo, 0.9f); border = AuraTheme.A(AuraTheme.Slate, 0.8f); }

            var bubble = AuraKit.Box(row, "Bubble", fill, AuraTheme.Radius, true);
            AuraKit.Border(bubble, border);
            var v = AuraKit.VStack(bubble, 12f);
            v.padding = new RectOffset(30, 30, 22, 22);

            string shown = m.isIntro ? m.text : Esc(m.text);
            var text = AuraKit.Text(bubble.transform, shown, AuraTheme.SizeSmall + 1f, thinking ? AuraTheme.TextDim : ink);
            text.richText = true;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            if (thinking) text.fontStyle = FontStyles.Italic;

            // Under real AI answers: Helpful? (the paper's number), and Share in a room.
            if (!m.fromUser && !m.isIntro && !m.isError && !thinking && !m.fromRoom)
            {
                var fb = AuraKit.Rect(bubble.transform, "Feedback");
                AuraKit.HStack(fb, 12f).childForceExpandHeight = true;
                AuraKit.Size(fb, -1, 72f);
                var q = AuraKit.Text(fb, "Helpful?", AuraTheme.SizeCaption - 2f, AuraTheme.TextDim);
                q.enableWordWrapping = false;
                ChatMessage msg = m;
                AuraWidgets.Chip(fb, "Yes", null, msg.rating > 0, () => Rate(msg, 1), out _, 72f);
                AuraWidgets.Chip(fb, "No", null, msg.rating < 0, () => Rate(msg, -1), out _, 72f);
                if (Photon.Pun.PhotonNetwork.InRoom)
                {
                    bool up = SharedAnswers.IsUp(msg.sharedId, SpaceSceneManager.CurrentModelKey);
                    AuraWidgets.Chip(fb, up ? "Shared" : "Share", AuraIcons.Room, up, () => Share(msg), out Button share, 72f);
                    share.interactable = !up;
                }
            }
            if (m.fromRoom && !m.fromUser)
                AuraKit.Text(bubble.transform, "Shared in the room", AuraTheme.SizeCaption - 4f, AuraTheme.Lavender);
        }

        // ── The Ask AI chip beside a selected part ─────────────────────

        private void PlaceAskChip(bool modelShown)
        {
            string path = CurrentSelection();
            bool show = modelShown && !IsOpen && !string.IsNullOrEmpty(path) && TryGetScreenRect(path, out _);
            if (!show)
            {
                if (_askChip.gameObject.activeSelf) _askChip.gameObject.SetActive(false);
                return;
            }

            TryGetScreenRect(path, out Rect rect);
            float k = AuraKit.CanvasScale;
            float chipW = _askChip.rect.width * k;
            // Right of the part's top-right corner, clear of the dock and the top and bottom rows.
            float maxX = Screen.width - (AuraViewer.DockWidth + 40f) * k - chipW;
            float x = Mathf.Clamp(rect.xMax + 16f * k, AuraViewer.Gutter * k, Mathf.Max(AuraViewer.Gutter * k, maxX));
            float y = Mathf.Clamp(rect.yMax, (AuraViewer.BottomReserve + 60f) * k, Screen.height - (AuraViewer.TopReserve + 140f) * k);
            _askChip.position = new Vector3(x, y, 0f);
            if (!_askChip.gameObject.activeSelf) _askChip.gameObject.SetActive(true);
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

        // ── Building ───────────────────────────────────────────────────

        private void BuildUI()
        {
            _safe = AuraViewer.CreateCanvas(transform, "AiCanvas", 98, out _canvas); // above the parts drawer (97)

            // The Ask AI chip that floats beside a selected part (positioned in screen pixels).
            var chip = AuraKit.Pill(_canvas.transform, "Ask AI", AuraIcons.AI, AuraTheme.Lavender, AuraTheme.Ink, OnAskPressed, 100f);
            _askChip = (RectTransform)chip.transform;
            _askChip.anchorMin = _askChip.anchorMax = Vector2.zero;
            _askChip.pivot = new Vector2(0f, 0.5f);
            _askChip.sizeDelta = new Vector2(250f, 100f);
            chip.gameObject.SetActive(false);

            BuildPanel();
        }

        private void BuildPanel()
        {
            _panelRt = AuraKit.Rect(_safe, "ChatPanel");
            _panelRt.anchorMin = new Vector2(1, 0);
            _panelRt.anchorMax = new Vector2(1, 1);
            _panelRt.pivot = new Vector2(1, 0.5f);
            _panelRt.offsetMin = new Vector2(-860f, AuraViewer.BottomReserve);
            _panelRt.offsetMax = new Vector2(0, -AuraViewer.TopReserve + 30f);
            _panelRt.gameObject.AddComponent<ChatPanelWidth>();

            // The fade: clear towards the model, darker at the screen edge. It takes no touches, so
            // a drag on the clear part still turns the model.
            var fade = AuraKit.Rect(_panelRt, "Fade").gameObject.AddComponent<Image>();
            fade.sprite = AuraSprites.HFade;
            fade.color = AuraTheme.A(AuraTheme.Ink, 0.86f);
            fade.raycastTarget = false;
            AuraKit.Fill(fade.rectTransform, -60, -AuraViewer.BottomReserve, -40, -60);
            fade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var v = AuraKit.VStack(_panelRt, 18f);
            v.padding = new RectOffset(70, 36, 6, 6);
            v.childForceExpandHeight = false;

            // Typing (Ask or Find): the input row rises above the phone keyboard and the chat
            // shrinks, so what is typed is always in view, in the box itself.
            var kb = _panelRt.gameObject.AddComponent<KeyboardPadding>();
            kb.group = v;
            kb.basePadding = 6;

            // Header: Find / Ask, and the ⋯ menu.
            // Every fixed row says flexibleHeight 0: a LayoutElement left at -1 passes up its
            // children's flexible height (the segments are flexible), and the rows then shared the
            // panel's spare height with the chat, growing huge.
            var header = AuraKit.Rect(_panelRt, "Header");
            AuraKit.HStack(header, 14f).childForceExpandHeight = false;
            AuraKit.Size(header, -1, 100f, -1, 0f);
            var segHost = AuraKit.Rect(header, "Tabs");
            AuraKit.Size(segHost, 340f, 100f, 0f, 0f);
            AuraKit.VStack(segHost, 0f);
            _setTab = AuraWidgets.Segmented(segHost, new[] { "Find", "Ask" }, 1, OnTabChanged);
            var track = segHost.GetChild(0).GetComponent<LayoutElement>();
            if (track != null) { track.minHeight = track.preferredHeight = 100f; track.flexibleHeight = 0f; }
            AuraKit.Flex(AuraKit.Rect(header, "Spacer"));
            AuraWidgets.Round(header, AuraIcons.More, 96f, false, () => { if (_menu != null) _menu.SetActive(!_menu.activeSelf); }, out _);

            // About · part, and Isolate.
            var aboutRow = AuraKit.Rect(_panelRt, "About");
            AuraKit.HStack(aboutRow, 12f);
            AuraKit.Size(aboutRow, -1, 84f, -1, 0f);
            _about = AuraKit.Text(aboutRow, "", AuraTheme.SizeSmall, AuraTheme.Text, AuraWeight.SemiBold);
            _about.richText = true;
            _about.enableWordWrapping = false;
            AuraKit.Size(_about, 0f, -1, 1f);
            AuraWidgets.Chip(aboutRow, "Isolate", AuraIcons.Target, false, OnIsolatePressed, out Button iso, 84f);
            _isolateButton = iso.gameObject;
            _isolateLabel = iso.GetComponentInChildren<TextMeshProUGUI>();

            BuildMenu();

            // Ask page.
            var ask = AuraKit.Rect(_panelRt, "AskPage");
            AuraKit.Size(ask, -1, 200f, -1, 1f);
            AuraKit.VStack(ask, 14f).childForceExpandHeight = false;
            _askPage = ask.gameObject;

            var list = AuraKit.Rect(ask, "Messages");
            AuraKit.Size(list, -1, 200f, -1, 1f);
            _messages = AuraWidgets.Scroll(list, out _scroll, 16f);
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            _chips = AuraKit.Rect(ask, "Suggestions");
            AuraKit.VStack(_chips, 10f);
            AuraKit.Size(_chips, -1, -1, -1, 0f);

            var inputRow = AuraKit.Rect(ask, "InputRow");
            AuraKit.HStack(inputRow, 12f).childForceExpandHeight = false;
            AuraKit.Size(inputRow, -1, 112f, -1, 0f);
            _input = AuraWidgets.Input(inputRow, AskHint, "", false, 112f, MaxQuestionChars);
            AuraKit.Flex(_input);
            _input.onSubmit.AddListener(_ => OnSendPressed());
            BuildAskMic(inputRow);
            _sendButton = AuraWidgets.Round(inputRow, AuraIcons.Send, 104f, true, OnSendPressed, out _);

            var note = AuraKit.Text(ask, "AI answers can be wrong. Questions go to Google and Groq: don't type personal details.",
                                    AuraTheme.SizeCaption - 5f, AuraTheme.A(AuraTheme.TextDim, 0.8f));
            note.enableWordWrapping = true;

            BuildFindPage(_panelRt);
            _findPage.SetActive(false);

            var y = _panelRt.anchoredPosition.y;
            _panel = AuraSlidePanel.Attach(_panelRt, new Vector2(0, y), new Vector2(980f, y));
        }

        private void BuildMenu()
        {
            var menu = AuraKit.Box(_panelRt, "Menu", AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius, true);
            AuraKit.Border(menu, AuraTheme.Slate);
            menu.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            AuraKit.Pin(menu.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -120), new Vector2(520, 150));
            var mv = AuraKit.VStack(menu, 0f, 20);
            mv.childAlignment = TextAnchor.MiddleCenter;
            AuraKit.Pill(menu.transform, "Clear chat for this model", AuraIcons.Trash, AuraTheme.Ink, AuraTheme.Rose, ClearChats,
                         AuraTheme.MinTouch, AuraTheme.Rose);
            _menu = menu.gameObject;
            _menu.SetActive(false);
        }

        /// <summary>Text from the knowledge file shown inside rich-text markup: stop it being read as tags.</summary>
        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>");
    }

    /// <summary>The AI panel takes most of the width: wide enough for comfortable bubbles.</summary>
    public class ChatPanelWidth : MonoBehaviour
    {
        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float w = Mathf.Clamp(parent.rect.width * 0.82f, 720f, 980f);
            if (!Mathf.Approximately(rt.sizeDelta.x, w)) rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        }
    }
}
