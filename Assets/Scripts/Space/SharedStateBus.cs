using System;
using UnityEngine;
using Photon.Pun;

namespace AuraSync.Space
{
    /// <summary>Names of the state changes published on <see cref="SharedStateBus"/>.</summary>
    public static class StateKeys
    {
        public const string ModelLoaded        = "model.loaded";        // value: model key
        public const string Explode            = "model.explode";       // value: float 0..1
        public const string PresenterSelection = "part.select.room";    // value: part path, "" = none
        public const string LocalSelection     = "part.select.local";   // value: part path, "" = none
        public const string Isolation          = "part.isolate";        // value: part path, "" = whole model
        public const string Section            = "model.section";       // value: float[5] SectionCutter.CutState
        public const string Simulation         = "model.simulation";    // value: (int) SimMode, 0 = stopped
        public const string AttentionView      = "attention.view";      // value: AttentionSample (5 Hz per person, when it changes)
        public const string AttentionPing      = "attention.ping";      // value: AttentionPing
        public const string FindTask           = "attention.find";      // value: string, what happened
        public const string NoteAdded          = "note.added";          // value: part path (Phase 10 counts it)
        public const string AiQuestion         = "ai.question";         // value: part path, "" = whole model (Phase 10)
    }

    /// <summary>One published change.</summary>
    public readonly struct StateChange
    {
        /// <summary>One of <see cref="StateKeys"/>.</summary>
        public readonly string key;
        public readonly object value;

        /// <summary>True when this device's user did it; false when it arrived from the room.</summary>
        public readonly bool isLocal;

        /// <summary>Seconds since app start, on this device.</summary>
        public readonly float time;

        /// <summary>Photon server time in ms, shared by every device in a room; 0 outside a room.</summary>
        public readonly int serverTime;

        public StateChange(string key, object value, bool isLocal)
        {
            this.key     = key;
            this.value   = value;
            this.isLocal = isLocal;
            time         = Time.realtimeSinceStartup;
            serverTime   = PhotonNetwork.InRoom ? PhotonNetwork.ServerTimestamp : 0;
        }
    }

    /// <summary>
    /// One stream of "what just changed in the 3D view", that later features listen to instead of
    /// hooking into every other feature.
    ///
    /// Session recording (Phase 9) and the interaction heatmap (Phase 10) both need to know about
    /// every explode, selection, isolation, note, cut and simulation step. Without this, each of them
    /// would need a hook in every one of those features, and every new feature would have to
    /// remember to call both. With it, a feature publishes once and anything interested subscribes.
    ///
    /// Publishing is fire-and-forget and never fails the publisher: a listener that throws is
    /// logged and skipped, and the other listeners still run. (An exception escaping into a
    /// feature's Update is how V2's "sync only works while a finger is down" bug happened.)
    ///
    /// Nothing subscribes yet in Phase 0. To watch it working, turn on <see cref="LogToConsole"/>
    /// (SpaceSessionHUD's context menu: "V3 debug: toggle state-bus log").
    /// </summary>
    public static class SharedStateBus
    {
        public static event Action<StateChange> OnChange;

        /// <summary>Writes every change to the Console. Off by default: the explode slider publishes every frame it moves.</summary>
        public static bool LogToConsole = false;

        public static void Publish(string key, object value, bool isLocal)
        {
            var change = new StateChange(key, value, isLocal);

            if (LogToConsole)
                Debug.Log($"[StateBus] {key} = {Describe(value)} ({(isLocal ? "local" : "room")})");

            Action<StateChange> handlers = OnChange;
            if (handlers == null) return;

            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<StateChange>)d)(change); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static string Describe(object value)
        {
            switch (value)
            {
                case null:     return "(null)";
                case string s: return s.Length == 0 ? "\"\"" : $"\"{s}\"";
                case float f:  return f.ToString("0.###");
                default:       return value.ToString();
            }
        }
    }
}
