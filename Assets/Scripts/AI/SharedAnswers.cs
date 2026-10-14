using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using AuraSync.Networking;
using AuraSync.Space;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.AI
{
    /// <summary>One AI answer someone shared with the room.</summary>
    public class SharedAnswer
    {
        public string id = "";
        public string key = "";        // the room property it lives in
        public string path = "";       // part path, "" = the whole model
        public string part = "";       // the chat's title when it was shared ("Fan rotor")
        public string question = "";
        public string answer = "";
        public string askerName = "";
        public int    askerActor;
        public double time;            // Photon server time (seconds), for ordering
    }

    /// <summary>
    /// AI answers shared to the room (V3 Phase 3b). Anyone in a room can share an answer from their
    /// chat; everyone sees it on a card beside the part (SharedAnswerCard). The asker or the
    /// presenter can remove it for everyone; anyone can dismiss it on their own phone.
    ///
    /// **One room property per answer**, <c>"SA:" + model hash + ":" + id</c>, rather than one list:
    /// two people sharing at the same moment each add their own key, so neither can overwrite the
    /// other's. Removing sets the key to null. A late joiner simply reads the keys.
    ///
    /// **Remembered per model:** the model hash is part of the key, so switching model hides the
    /// old model's answers and going back to it shows them again, for as long as the room lasts.
    /// At most <see cref="MaxPerModel"/> are kept per model; sharing one more drops the oldest.
    /// </summary>
    public class SharedAnswers : MonoBehaviourPunCallbacks
    {
        public static SharedAnswers Instance { get; private set; }

        public const int MaxPerModel      = 10;
        public const int MaxAnswerChars   = 1200; // answers are asked for in 120 words, ~800 chars
        public const int MaxQuestionChars = 300;

        /// <summary>Raised when answers are shared, removed or dismissed, and on joining or leaving a room.</summary>
        public static event Action OnChanged;

        // Dismissed on this phone only. Kept for the app session.
        private static readonly HashSet<string> _dismissed = new HashSet<string>();

        public static void EnsureExists()
        {
            if (Instance != null) return;
            new GameObject("AuraSync_SharedAnswers").AddComponent<SharedAnswers>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Reading ────────────────────────────────────────────────────────────

        private static string PrefixFor(string modelKey) =>
            RoomKeys.SharedAnswerPrefix + ModelStateMemory.ModelHash(modelKey ?? "") + ":";

        /// <summary>The model's shared answers, oldest first (including ones dismissed here).</summary>
        public static List<SharedAnswer> ForModel(string modelKey)
        {
            var list = new List<SharedAnswer>();
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(modelKey)) return list;

            string prefix = PrefixFor(modelKey);
            foreach (var kv in PhotonNetwork.CurrentRoom.CustomProperties)
            {
                if (!(kv.Key is string key) || !key.StartsWith(prefix)) continue;
                if (!(kv.Value is Hashtable h)) continue; // null = removed

                list.Add(new SharedAnswer
                {
                    key        = key,
                    id         = key.Substring(prefix.Length),
                    path       = Str(h, "p"),
                    part       = Str(h, "t"),
                    question   = Str(h, "q"),
                    answer     = Str(h, "a"),
                    askerName  = Str(h, "n"),
                    askerActor = h.TryGetValue("w", out object w) && w is int a ? a : 0,
                    time       = h.TryGetValue("s", out object s) && s is double d ? d : 0,
                });
            }
            return list.OrderBy(x => x.time).ToList();
        }

        /// <summary>The ones still to show on this phone, oldest first.</summary>
        public static List<SharedAnswer> VisibleFor(string modelKey) =>
            ForModel(modelKey).Where(a => !_dismissed.Contains(a.id)).ToList();

        /// <summary>True while an answer this phone shared is still up.</summary>
        public static bool IsUp(string id, string modelKey) =>
            !string.IsNullOrEmpty(id) && ForModel(modelKey).Any(a => a.id == id);

        private static string Str(Hashtable h, string k) => h.TryGetValue(k, out object v) ? v as string ?? "" : "";

        // ── Changing ───────────────────────────────────────────────────────────

        /// <summary>Shares an answer with the room. Returns its id, or null when not in a room.</summary>
        public static string Share(string modelKey, string path, string part, string question, string answer)
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(modelKey) || string.IsNullOrEmpty(answer)) return null;

            string id = Guid.NewGuid().ToString("N").Substring(0, 10);
            var entry = new Hashtable
            {
                { "p", path ?? "" },
                { "t", Clip(part, 80) },
                { "q", Clip(question, MaxQuestionChars) },
                { "a", Clip(answer, MaxAnswerChars) },
                { "n", NetworkManager.DisplayName(PhotonNetwork.LocalPlayer) },
                { "w", PhotonNetwork.LocalPlayer.ActorNumber },
                { "s", PhotonNetwork.Time },
            };

            var props = new Hashtable { { PrefixFor(modelKey) + id, entry } };

            // Over the limit: the oldest makes room, in the same update.
            List<SharedAnswer> existing = ForModel(modelKey);
            for (int i = 0; i <= existing.Count - MaxPerModel; i++) props[existing[i].key] = null;

            PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Debug.Log($"[SharedAnswers] Shared {id} about '{path}' ({answer.Length} chars).");
            return id;
        }

        /// <summary>The asker and the presenter may take a shared answer down for everyone.</summary>
        public static bool CanRemove(SharedAnswer a)
        {
            if (a == null || !PhotonNetwork.InRoom) return false;
            bool presenter = NetworkManager.Instance != null && NetworkManager.Instance.IsLocalAdmin;
            return presenter || a.askerActor == PhotonNetwork.LocalPlayer.ActorNumber;
        }

        public static void Remove(SharedAnswer a)
        {
            if (!CanRemove(a)) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { a.key, null } });
        }

        /// <summary>Hides an answer on this phone only.</summary>
        public static void Dismiss(string id)
        {
            if (string.IsNullOrEmpty(id) || !_dismissed.Add(id)) return;
            OnChanged?.Invoke();
        }

        private static string Clip(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length <= max ? s : s.Substring(0, max - 3).TrimEnd() + "...";
        }

        // ── Photon callbacks ───────────────────────────────────────────────────

        public override void OnRoomPropertiesUpdate(Hashtable changed)
        {
            foreach (object k in changed.Keys)
            {
                if (k is string key && key.StartsWith(RoomKeys.SharedAnswerPrefix))
                {
                    OnChanged?.Invoke();
                    return;
                }
            }
        }

        public override void OnJoinedRoom() => OnChanged?.Invoke();
        public override void OnLeftRoom()   => OnChanged?.Invoke();
    }
}
