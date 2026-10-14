using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AuraSync.UI
{
    /// <summary>
    /// The back stack: every panel that opens is pushed, and Back closes the top one and returns to
    /// the one under it.
    ///
    /// V3 adds panels that open from other panels (AI chat → follow-up, Simulate → Working
    /// animation → part info). Without one shared stack, each panel would need to know which panel
    /// opened it to go back correctly, and the Android back button would have no idea what to close.
    /// With it, both the on-screen Back buttons and the device's back button do the same thing.
    ///
    /// How a panel uses it:
    /// <code>
    ///   open:                   PanelNavigator.Push("parts", this, () => SetOpen(false));
    ///   closed by its own X:    PanelNavigator.Remove("parts");
    /// </code>
    /// The close callback must be safe to call when the panel is already closed, and is allowed to
    /// call <see cref="Remove"/> itself (Back has already taken the entry off by then).
    ///
    /// Entries remember the component that owns them. When a scene unloads and destroys that
    /// component, its entry is dropped automatically, so a stale panel can never swallow a Back.
    /// </summary>
    public static class PanelNavigator
    {
        private struct Entry
        {
            public string             id;
            public UnityEngine.Object owner;
            public Action             close;
        }

        private static readonly List<Entry> _stack = new List<Entry>();

        /// <summary>Raised whenever the stack changes, for debug readouts.</summary>
        public static event Action OnChanged;

        public static int Count { get { Prune(); return _stack.Count; } }

        /// <summary>Id of the panel on top, or "" when nothing is open.</summary>
        public static string Top { get { Prune(); return _stack.Count > 0 ? _stack[_stack.Count - 1].id : ""; } }

        public static bool IsOpen(string id) => IndexOf(id) >= 0;

        /// <summary>
        /// Records that a panel has opened. Opening one that is already on the stack moves it to the
        /// top rather than adding a second entry.
        /// </summary>
        public static void Push(string id, UnityEngine.Object owner, Action close)
        {
            if (string.IsNullOrEmpty(id)) return;

            int i = IndexOf(id);
            if (i >= 0) _stack.RemoveAt(i);

            _stack.Add(new Entry { id = id, owner = owner, close = close });
            OnChanged?.Invoke();
        }

        /// <summary>Records that a panel has closed by its own means. Does not call its close callback.</summary>
        public static void Remove(string id)
        {
            int i = IndexOf(id);
            if (i < 0) return;

            _stack.RemoveAt(i);
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Closes the panel on top. Returns false when nothing was open, so the caller can decide
        /// what Back means with no panel showing (in the 3D viewer: leave to the model list).
        /// </summary>
        public static bool Back()
        {
            Prune();
            if (_stack.Count == 0) return false;

            Entry top = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            OnChanged?.Invoke();

            // A panel whose close throws must not leave Back stuck on it.
            try { top.close?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }

            return true;
        }

        /// <summary>Human-readable stack, bottom to top. For logs and tests.</summary>
        public static string Describe()
        {
            Prune();
            if (_stack.Count == 0) return "(empty)";

            var ids = new string[_stack.Count];
            for (int i = 0; i < _stack.Count; i++) ids[i] = _stack[i].id;
            return string.Join(" > ", ids);
        }

        // ── Internals ──────────────────────────────────────────────────────────

        private static int IndexOf(string id)
        {
            Prune();
            for (int i = _stack.Count - 1; i >= 0; i--)
                if (_stack[i].id == id) return i;
            return -1;
        }

        /// <summary>Drops entries whose owning component has been destroyed.</summary>
        private static void Prune()
        {
            bool changed = false;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                // Unity-null: true once the owner is destroyed. An entry pushed with no owner is kept.
                if (!ReferenceEquals(_stack[i].owner, null) && _stack[i].owner == null)
                {
                    _stack.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) OnChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // With Enter Play Mode Options (no domain reload) statics survive between Play sessions.
            _stack.Clear();
            OnChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookSceneChanges()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene _) => Prune();
    }
}
