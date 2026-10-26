using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.Networking;

namespace AuraSync.UI
{
    /// <summary>
    /// Room-code entry without the phone keyboard: AURA- and four boxes, typed on a QWERTY-style
    /// keypad holding only the 32 characters a code can contain (no 0/O or 1/I), with Paste link and
    /// Delete. Tap any box to put the cursor there, so one wrong character is fixed without deleting
    /// the ones after it. Used by the 3D viewer's Room card; onboarding's Join page uses the same
    /// <see cref="CodeSlots"/> and <see cref="CodeKeys"/>.
    /// </summary>
    public class AuraCodePad
    {
        public const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public RectTransform Root { get; private set; }
        public string Code => _slots.Code;
        public bool IsComplete => _slots.IsComplete;

        /// <summary>Raised whenever the code changes.</summary>
        public event Action<string> OnChanged;

        /// <summary>Raised with a short message when Paste finds no code.</summary>
        public event Action<string> OnMessage;

        private CodeSlots _slots;
        private readonly List<Button> _keys = new List<Button>();

        public static AuraCodePad Build(Transform parent, float keyHeight = 96f)
        {
            var pad = new AuraCodePad();
            pad.BuildInto(parent, keyHeight);
            return pad;
        }

        private void BuildInto(Transform parent, float keyH)
        {
            Root = AuraKit.Rect(parent, "CodePad");
            AuraKit.VStack(Root, 22f);

            var row = AuraKit.Rect(Root, "Code");
            AuraKit.HStack(row, 14f, 0, TextAnchor.MiddleCenter);
            AuraKit.Size(row, -1, 150f);
            var pre = AuraKit.Text(row, "AURA-", 50f, AuraTheme.TextDim, AuraWeight.SemiBold, TextAlignmentOptions.MidlineRight);
            pre.enableWordWrapping = false;
            AuraKit.Size(pre, 200f, 150f);
            _slots = CodeSlots.Build(row, 124f, 150f, 62f, 24f);
            _slots.OnChanged += () => OnChanged?.Invoke(Code);

            var keypad = AuraKit.Box(Root, "Keypad", AuraTheme.A(AuraTheme.Ink, 0.6f), AuraTheme.Radius - 6f);
            var kv = AuraKit.VStack(keypad, 10f, 12);
            kv.childForceExpandHeight = false;
            _keys.AddRange(CodeKeys.Build(keypad.transform, keyH, 10f, 40f, c => _slots.Type(c)));

            var last = AuraKit.Rect(keypad.transform, "RowLast");
            var lh = AuraKit.HStack(last, 10f);
            lh.childForceExpandWidth = true; lh.childForceExpandHeight = true;
            AuraKit.Size(last, -1, keyH);
            var paste = AuraKit.Pill(last, "Paste link", AuraIcons.Paste, AuraTheme.Indigo, AuraTheme.Lavender, Paste, keyH);
            AuraKit.Cell(paste);
            var del = AuraKit.Pill(last, "Delete", AuraIcons.Backspace, AuraTheme.Indigo, AuraTheme.Text, () => _slots.Delete(), keyH);
            AuraKit.Cell(del);
            _keys.Add(paste);
            _keys.Add(del);
        }

        public void SetInteractable(bool on)
        {
            _slots.Enabled = on;
            foreach (var k in _keys) if (k != null) k.interactable = on;
            _slots.Paint();
        }

        public void Clear() => _slots.Clear();

        private void Paste()
        {
            if (!_slots.Enabled) return;
            string clip = GUIUtility.systemCopyBuffer ?? "";
            string found = RoomCodeGenerator.FormatCode(RoomLinkManager.ExtractRoomCode(clip));
            if (!RoomCodeGenerator.IsValidCode(found))
            {
                OnMessage?.Invoke("No room code found in what you copied.");
                return;
            }
            _slots.Set(found.Substring(found.Length - 4));
        }
    }

    /// <summary>
    /// The four code boxes and their cursor. Typing fills the box with the cursor and moves on to the
    /// next empty one; tapping a box moves the cursor there (to fix just that character); Delete
    /// clears the box with the cursor, or the one before it when that box is already empty.
    /// </summary>
    public class CodeSlots
    {
        public const int Length = 4;

        private readonly char[] _c = new char[Length];
        private int _cursor;
        private readonly List<Image> _fills = new List<Image>();
        private readonly List<Image> _borders = new List<Image>();
        private readonly List<TextMeshProUGUI> _letters = new List<TextMeshProUGUI>();

        public bool Enabled = true;
        public event Action OnChanged;

        /// <summary>The characters typed so far, in order (complete when it is 4 long).</summary>
        public string Code
        {
            get
            {
                var sb = new StringBuilder(Length);
                foreach (char ch in _c) if (ch != '\0') sb.Append(ch);
                return sb.ToString();
            }
        }

        public bool IsComplete
        {
            get { foreach (char ch in _c) if (ch == '\0') return false; return true; }
        }

        /// <summary>Four tappable boxes, added to a row.</summary>
        public static CodeSlots Build(Transform row, float width, float height, float fontSize, float radius)
        {
            var s = new CodeSlots();
            for (int i = 0; i < Length; i++)
            {
                int idx = i;
                var b = AuraKit.Box(row, "Box" + i, AuraTheme.Ink, radius, true);
                AuraKit.Size(b, width, height);
                s._fills.Add(b);
                s._borders.Add(AuraKit.Border(b, AuraTheme.Slate, radius));
                var l = AuraKit.Text(b.transform, "", fontSize, AuraTheme.Text, AuraWeight.Bold, TextAlignmentOptions.Center);
                l.enableWordWrapping = false;
                AuraKit.Fill(l.rectTransform);
                s._letters.Add(l);
                AuraKit.MakeButton(b.gameObject, b, () => s.Select(idx));
            }
            s.Paint();
            return s;
        }

        public void Type(char ch)
        {
            if (!Enabled || _cursor >= Length) return;
            _c[_cursor] = ch;
            _cursor = NextEmpty(_cursor + 1);
            Changed();
        }

        public void Delete()
        {
            if (!Enabled) return;
            if (_cursor < Length && _c[_cursor] != '\0')
            {
                _c[_cursor] = '\0';
            }
            else
            {
                int i = Mathf.Min(_cursor, Length) - 1;
                if (i < 0) return;
                _c[i] = '\0';
                _cursor = i;
            }
            Changed();
        }

        public void Select(int i)
        {
            if (!Enabled) return;
            _cursor = Mathf.Clamp(i, 0, Length - 1);
            Paint();
        }

        public void Set(string four)
        {
            for (int i = 0; i < Length; i++) _c[i] = four != null && i < four.Length ? char.ToUpperInvariant(four[i]) : '\0';
            _cursor = NextEmpty(0);
            Changed();
        }

        public void Clear()
        {
            for (int i = 0; i < Length; i++) _c[i] = '\0';
            _cursor = 0;
            Changed();
        }

        /// <summary>The first empty box from here on (then from the start); Length when all are full.</summary>
        private int NextEmpty(int from)
        {
            for (int i = from; i < Length; i++) if (_c[i] == '\0') return i;
            for (int i = 0; i < Mathf.Min(from, Length); i++) if (_c[i] == '\0') return i;
            return Length;
        }

        private void Changed()
        {
            Paint();
            OnChanged?.Invoke();
        }

        public void Paint()
        {
            for (int i = 0; i < Length && i < _letters.Count; i++)
            {
                bool filled = _c[i] != '\0';
                bool current = Enabled && i == _cursor;
                _letters[i].text = filled ? _c[i].ToString() : "";
                _borders[i].color = current ? AuraTheme.Lavender : filled ? AuraTheme.A(AuraTheme.Lavender, 0.5f) : AuraTheme.Slate;
                _fills[i].color = current ? Color.Lerp(AuraTheme.Ink, AuraTheme.Lavender, 0.16f) : AuraTheme.Ink;
            }
        }
    }

    /// <summary>
    /// The code keys, laid out like a phone keyboard: the digits on top, then the QWERTY rows. A
    /// code has no I, O, 0 or 1, so those keys are simply missing. Every key is the same width;
    /// shorter rows are centred, as on a real keyboard.
    /// </summary>
    public static class CodeKeys
    {
        private static readonly string[] Rows = { "23456789", "QWERTYUP", "ASDFGHJKL", "ZXCVBNM" };
        private const int Widest = 9;

        public static List<Button> Build(Transform pad, float keyH, float gap, float fontSize, Action<char> onKey)
        {
            var keys = new List<Button>();
            foreach (string chars in Rows)
            {
                var row = AuraKit.Rect(pad, "KeyRow");
                var h = AuraKit.HStack(row, gap);
                h.childForceExpandWidth = false;
                h.childForceExpandHeight = true;
                AuraKit.Size(row, -1, keyH);

                // Side spacers take the missing keys' share, so every key ends up the same width.
                float side = (Widest - chars.Length) * 0.5f;
                if (side > 0f) Spacer(row, side, gap);
                foreach (char ch in chars)
                {
                    char c = ch;
                    var key = AuraKit.Box(row, "Key_" + c, AuraTheme.Indigo, 18f, true);
                    var le = key.gameObject.AddComponent<LayoutElement>();
                    le.minWidth = 0f; le.preferredWidth = 0f; le.flexibleWidth = 1f;
                    var kt = AuraKit.Text(key.transform, c.ToString(), fontSize, AuraTheme.Text, AuraWeight.SemiBold,
                                          TextAlignmentOptions.Center);
                    AuraKit.Fill(kt.rectTransform);
                    keys.Add(AuraKit.MakeButton(key.gameObject, key, () => onKey(c)));
                }
                if (side > 0f) Spacer(row, side, gap);
            }
            return keys;
        }

        private static void Spacer(RectTransform row, float keys, float gap)
        {
            var s = AuraKit.Rect(row, "Spacer");
            var le = s.gameObject.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = keys;
            // The spacer stands for `keys` keys, minus the gaps they would have had; close enough
            // that the rows line up by eye.
        }
    }
}
