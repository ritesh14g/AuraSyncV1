using System;
using System.Collections.Generic;
using AuraSync.UI;

namespace AuraSync.Networking
{
    /// <summary>
    /// KEEP SHARING: whoever drives a control for the room (the presenter, or a student it was
    /// handed to) can stop being asked "show this to everyone?" for it. The question gets a third
    /// button; choosing it shares the change and turns the control live, so every later change goes
    /// straight to the room. LIVE · STOP in the Parts drawer turns it back to asking.
    ///
    /// Per control (explode and isolate are separate), and only while this device drives that
    /// control: losing it (taken back, handed over, presenter role passed on) or leaving the room
    /// ends live sharing, so the next person to get the control starts from asking.
    /// </summary>
    public static class LiveSharing
    {
        private static readonly HashSet<Control> _live = new HashSet<Control>();

        /// <summary>Raised when a control turns live or stops being live.</summary>
        public static event Action OnChanged;

        static LiveSharing()
        {
            ControlOwnership.OnChanged += Prune;
        }

        public static bool IsLive(Control c) => _live.Contains(c) && ControlOwnership.LocalDrivesRoom(c);

        public static void Stop(Control c)
        {
            if (_live.Remove(c)) OnChanged?.Invoke();
        }

        /// <summary>
        /// A change this device's user made to a control it drives: shared at once when live,
        /// otherwise asked about with ACCEPT / DENY / KEEP SHARING. <paramref name="share"/> must
        /// send the state as it is when called, since the question may be answered later.
        /// </summary>
        public static void Offer(Control c, string title, string body, Action share)
        {
            if (!ControlOwnership.LocalDrivesRoom(c) || share == null) return;

            if (IsLive(c)) { share(); return; }

            ShareWithRoomPrompt.Ask(new ShareWithRoomPrompt.Options
            {
                id       = ControlOwnership.Id(c),
                title    = title,
                body     = body + "\nKeep sharing: share this and every change after it, without asking.",
                onAccept = share,
                onDeny   = null,
                onKeep   = () =>
                {
                    if (!ControlOwnership.LocalDrivesRoom(c)) return;
                    _live.Add(c);
                    OnChanged?.Invoke();
                    share();
                    Toast.Show($"{ControlOwnership.Label(c)} is shared live. Stop it in the Parts drawer.");
                },
            });
        }

        private static void Prune()
        {
            if (_live.Count == 0) return;
            int before = _live.Count;
            _live.RemoveWhere(c => !ControlOwnership.LocalDrivesRoom(c));
            if (_live.Count != before) OnChanged?.Invoke();
        }
    }
}
