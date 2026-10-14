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
        private RectTransform   _denyRt, _acceptRt, _keepRt;
        private TextMeshProUGUI _denyText, _acceptText, _keepText;

        private const string NavId = "share-prompt";

        private static readonly Color Dim         = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color CardColor   = new Color(0.09f, 0.11f, 0.16f, 0.98f);
        private static readonly Color AcceptColor = new Color(0.16f, 0.62f, 0.36f, 1f);
        private static readonly Color DenyColor   = new Color(0.42f, 0.44f, 0.52f, 1f);
        private static readonly Color KeepColor   = new Color(0.13f, 0.42f, 0.78f, 1f);

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

            _acceptText.text = string.IsNullOrEmpty(o.acceptLabel) ? "ACCEPT" : o.acceptLabel;
            _denyText.text   = string.IsNullOrEmpty(o.denyLabel)   ? "DENY"   : o.denyLabel;
            _keepText.text   = string.IsNullOrEmpty(o.keepLabel)   ? "KEEP SHARING" : o.keepLabel;

            // Two buttons side by side, or three when KEEP SHARING is on offer.
            bool keep = o.onKeep != null;
            _keepRt.gameObject.SetActive(keep);
            if (keep)
            {
                SetSpan(_denyRt,   0.04f, 0.33f);
                SetSpan(_acceptRt, 0.355f, 0.645f);
                SetSpan(_keepRt,   0.67f, 0.96f);
            }
            else
            {
                SetSpan(_denyRt,   0.05f, 0.48f);
                SetSpan(_acceptRt, 0.52f, 0.95f);
            }

            _root.SetActive(true);
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

        // ── Building ───────────────────────────────────────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 105; // above every panel, including the room modal (100/101)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Full-screen dimmer: blocks touches to the model while the question is open.
            _root = new GameObject("Dimmer", typeof(RectTransform));
            _root.transform.SetParent(transform, false);
            Stretch((RectTransform)_root.transform);
            _root.AddComponent<Image>().color = Dim;
            _root.AddComponent<TouchBlocker>(); // a tap anywhere on the dimmed screen is not a tap on the model

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(_root.transform, false);
            var crt = (RectTransform)card.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(900f, 560f);
            card.AddComponent<Image>().color = CardColor;

            _title = MakeText(card.transform, "Title", 44f, FontStyles.Bold);
            _title.enableAutoSizing = true; // "Asha asks to drive Isolate" with a long name
            _title.fontSizeMin = 30f;
            _title.fontSizeMax = 44f;
            Place(_title.rectTransform, 0.75f, 0.95f);

            _body = MakeText(card.transform, "Body", 32f, FontStyles.Normal);
            _body.color = new Color(0.85f, 0.88f, 0.94f, 1f);
            _body.enableAutoSizing = true; // three lines when KEEP SHARING is explained
            _body.fontSizeMin = 22f;
            _body.fontSizeMax = 32f;
            Place(_body.rectTransform, 0.38f, 0.75f);

            _countdown = MakeText(card.transform, "Countdown", 24f, FontStyles.Italic);
            _countdown.color = new Color(1f, 1f, 1f, 0.55f);
            Place(_countdown.rectTransform, 0.30f, 0.38f);

            _denyRt   = MakeButton(card.transform, "Deny",   DenyColor,   () => Answer(Choice.Deny),   out _denyText);
            _acceptRt = MakeButton(card.transform, "Accept", AcceptColor, () => Answer(Choice.Accept), out _acceptText);
            _keepRt   = MakeButton(card.transform, "Keep",   KeepColor,   () => Answer(Choice.Keep),   out _keepText);
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize  = size;
            t.fontStyle = style;
            t.color     = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            return t;
        }

        private static RectTransform MakeButton(Transform parent, string name, Color color,
                                                UnityEngine.Events.UnityAction onClick, out TextMeshProUGUI label)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;

            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            label = MakeText(go.transform, "Label", 34f, FontStyles.Bold);
            label.enableAutoSizing = true; // KEEP SHARING is long for a third of the card
            label.fontSizeMin = 20f;
            label.fontSizeMax = 34f;
            label.enableWordWrapping = false;
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.rectTransform.offsetMax = new Vector2(-10f, 0f);
            return rt;
        }

        private static void SetSpan(RectTransform rt, float xMin, float xMax)
        {
            rt.anchorMin = new Vector2(xMin, 0.07f);
            rt.anchorMax = new Vector2(xMax, 0.28f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, float yMin, float yMax)
        {
            rt.anchorMin = new Vector2(0.06f, yMin);
            rt.anchorMax = new Vector2(0.94f, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
