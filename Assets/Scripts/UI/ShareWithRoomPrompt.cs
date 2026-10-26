using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// Raises <see cref="Released"/> when a finger or mouse button lifts off this UI element. Unity
    /// delivers pointer-up to every handler on the object, so it sits beside a Slider without
    /// disturbing it. Used to ask the share question once per explode drag.
    /// </summary>
    public class PointerUpRelay : MonoBehaviour, IPointerUpHandler
    {
        public event Action Released;
        public void OnPointerUp(PointerEventData eventData) => Released?.Invoke();
    }

    /// <summary>
    /// The room's yes / no questions: the presenter's (or a control holder's) "show this to
    /// everyone?" after they explode or isolate, and the presenter's Allow / Deny when a viewer asks
    /// for a control.
    ///
    /// Share questions: Accept sends the change to the room, so every viewer who is following sees
    /// it too. Deny keeps it on this phone only; viewers carry on seeing the last shared state.
    /// KEEP SHARING (when offered) shares it and stops asking for that control (see LiveSharing).
    ///
    /// One question per id is on screen at a time: if the same thing changes again while its
    /// question is still open, the open question simply covers the newer change (the callbacks read
    /// the current state when answered), instead of stacking up popups. Questions with different
    /// ids queue. The device's Back button answers Deny, and so does running out of time on a
    /// question that has a time limit.
    /// </summary>
    public class ShareWithRoomPrompt : MonoBehaviour
    {
        /// <summary>Everything one question can say and do. Unset labels fall back to ACCEPT / DENY.</summary>
        public struct Options
        {
            public string id;
            public string title;
            public string body;
            public Action onAccept;
            public Action onDeny;

            /// <summary>A third button (KEEP SHARING) is shown only when this is set.</summary>
            public Action onKeep;

            public string acceptLabel;
            public string denyLabel;
            public string keepLabel;

            /// <summary>Seconds before the question answers itself with Deny; 0 = waits for ever.</summary>
            public float timeoutSeconds;
        }

        private struct Question
        {
            public Options opts;
            public float   expiresAt; // realtime seconds, 0 = no limit
        }

        private static ShareWithRoomPrompt _instance;
        private readonly List<Question> _queue = new List<Question>();

        private GameObject      _root;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _countdown;
        private RectTransform   _keepRt;
        private TextMeshProUGUI _denyText, _acceptText, _keepText;

        private const string NavId = "share-prompt";

        /// <summary>
        /// Shows (or queues) a question. <paramref name="id"/> identifies the feature: asking again
        /// with an id that is already waiting replaces that question rather than adding another.
        /// </summary>
        public static void Ask(string id, string title, string body, Action onAccept, Action onDeny)
        {
            Ask(new Options { id = id, title = title, body = body, onAccept = onAccept, onDeny = onDeny });
        }

        /// <summary>Shows (or queues) a question with any of the extra options.</summary>
        public static void Ask(Options opts)
        {
            if (_instance == null)
            {
                var go = new GameObject("AuraSync_ShareWithRoomPrompt");
                _instance = go.AddComponent<ShareWithRoomPrompt>();
            }

            float expires = opts.timeoutSeconds > 0f ? Time.realtimeSinceStartup + opts.timeoutSeconds : 0f;
            _instance.Enqueue(new Question { opts = opts, expiresAt = expires });
        }

        /// <summary>Drops every open or queued question without answering (model change, leaving the room).</summary>
        public static void CancelAll()
        {
            if (_instance == null) return;
            _instance._queue.Clear();
            _instance.Hide();
        }

        /// <summary>Drops one question, open or queued, without answering (it no longer applies).</summary>
        public static void Cancel(string id)
        {
            if (_instance == null) return;
            int i = _instance._queue.FindIndex(x => x.opts.id == id);
            if (i < 0) return;
            _instance._queue.RemoveAt(i);
            if (i == 0) _instance.ShowFront();
        }

        void Awake()
        {
            Build();
            _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            PanelNavigator.Remove(NavId);
        }

        void Update()
        {
            if (_queue.Count == 0) return;

            // A queued question can run out of time before it reaches the front: answer it (Deny)
            // quietly so the asker hears back, without flashing it on screen.
            for (int i = _queue.Count - 1; i >= 1; i--)
            {
                if (_queue[i].expiresAt <= 0f || Time.realtimeSinceStartup < _queue[i].expiresAt) continue;
                Question q = _queue[i];
                _queue.RemoveAt(i);
                SafeInvoke(q.opts.onDeny);
            }

            Question front = _queue[0];
            if (front.expiresAt <= 0f) return;

            float left = front.expiresAt - Time.realtimeSinceStartup;
            if (left <= 0f) { Answer(Choice.Deny); return; }
            if (_countdown != null) _countdown.text = $"Declines by itself in {Mathf.CeilToInt(left)} s";
        }

        private void Enqueue(Question q)
        {
            int existing = _queue.FindIndex(x => x.opts.id == q.opts.id);
            if (existing >= 0) _queue[existing] = q;
            else _queue.Add(q);
            ShowFront();
        }

        private void ShowFront()
        {
            if (_queue.Count == 0) { Hide(); return; }

            Options o = _queue[0].opts;
            _title.text = o.title;
            _body.text  = o.body;
            _countdown.text = "";

            _acceptText.text = Sentence(string.IsNullOrEmpty(o.acceptLabel) ? "Accept" : o.acceptLabel);
            _denyText.text   = Sentence(string.IsNullOrEmpty(o.denyLabel)   ? "Deny"   : o.denyLabel);
            _keepText.text   = Sentence(string.IsNullOrEmpty(o.keepLabel)   ? "Keep sharing" : o.keepLabel);

            // Two buttons side by side, or three when Keep sharing is on offer.
            _keepRt.gameObject.SetActive(o.onKeep != null);

            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                var g = _root.GetComponent<CanvasGroup>();
                g.alpha = 0f;
                AuraMotion.Fade(g, 1f, 0.18f);
            }
            PanelNavigator.Push(NavId, this, () => Answer(Choice.Deny));
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
            PanelNavigator.Remove(NavId);
        }

        private enum Choice { Accept, Deny, Keep }

        private void Answer(Choice choice)
        {
            if (_queue.Count == 0) { Hide(); return; }

            Question q = _queue[0];
            _queue.RemoveAt(0);
            PanelNavigator.Remove(NavId);

            switch (choice)
            {
                case Choice.Accept: SafeInvoke(q.opts.onAccept); break;
                case Choice.Deny:   SafeInvoke(q.opts.onDeny);   break;
                case Choice.Keep:   SafeInvoke(q.opts.onKeep ?? q.opts.onAccept); break;
            }

            ShowFront();
        }

        private static void SafeInvoke(Action a)
        {
            try { a?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>"KEEP SHARING" → "Keep sharing": buttons read in sentence case.</summary>
        private static string Sentence(string s)
        {
            if (string.IsNullOrEmpty(s) || s != s.ToUpperInvariant()) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = AuraKit.CreateCanvas(transform, "SharePrompt", 105); // above every panel
            _root = canvas.gameObject;
            _root.AddComponent<CanvasGroup>();

            // Full-screen dimmer: blocks touches to the model while the question is open.
            var dim = AuraKit.Box(canvas.transform, "Dim", AuraTheme.A(AuraTheme.Ink, 0.65f), 0f, true);
            AuraKit.Fill(dim.rectTransform);
            dim.gameObject.AddComponent<TouchBlocker>();

            var safe = AuraKit.Rect(canvas.transform, "SafeArea");
            safe.gameObject.AddComponent<AuraSafeArea>();

            var card = AuraKit.Box(safe, "Card", AuraTheme.A(AuraTheme.Indigo, 0.98f), AuraTheme.Radius + 8f, true);
            AuraKit.Border(card, AuraTheme.Lavender, AuraTheme.Radius + 8f);
            var crt = card.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.55f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            card.gameObject.AddComponent<MatchParentWidth>().max = 920f;
            var v = AuraKit.VStack(card, 18f);
            v.padding = new RectOffset(44, 44, 44, 40);
            AuraKit.FitHeight(card);

            var icon = AuraKit.Icon(card.transform, AuraIcons.Room, 72f, AuraTheme.Lavender);
            _title = AuraKit.Text(card.transform, "", AuraTheme.SizeTitle - 4f, AuraTheme.Text, AuraWeight.Bold);
            _title.enableWordWrapping = true;
            _body = AuraKit.Text(card.transform, "", AuraTheme.SizeSmall + 2f, AuraTheme.TextDim);
            _body.enableWordWrapping = true;
            _body.richText = true;
            _countdown = AuraKit.Text(card.transform, "", AuraTheme.SizeCaption - 2f, AuraTheme.A(AuraTheme.TextDim, 0.8f));
            _countdown.fontStyle = FontStyles.Italic;

            var row = AuraKit.EqualRow(card.transform, AuraTheme.MinTouch, 16f);
            var deny = AuraKit.Pill(row, "Deny", null, AuraTheme.Ink, AuraTheme.Text, () => Answer(Choice.Deny), AuraTheme.MinTouch, AuraTheme.Slate);
            AuraKit.Cell(deny);
            _denyText = deny.GetComponentInChildren<TextMeshProUGUI>();
            var accept = AuraKit.Pill(row, "Accept", null, AuraTheme.Lavender, AuraTheme.Ink, () => Answer(Choice.Accept), AuraTheme.MinTouch);
            AuraKit.Cell(accept);
            _acceptText = accept.GetComponentInChildren<TextMeshProUGUI>();
            var keep = AuraKit.Pill(row, "Keep sharing", null, AuraTheme.A(AuraTheme.Lavender, 0.18f), AuraTheme.Lavender,
                                    () => Answer(Choice.Keep), AuraTheme.MinTouch, AuraTheme.Lavender);
            AuraKit.Cell(keep);
            _keepRt = (RectTransform)keep.transform;
            _keepText = keep.GetComponentInChildren<TextMeshProUGUI>();
            foreach (var t in new[] { _denyText, _acceptText, _keepText })
            {
                t.enableAutoSizing = true;
                t.fontSizeMin = 20f;
                t.fontSizeMax = AuraTheme.SizeBody;
            }
        }
    }
}
