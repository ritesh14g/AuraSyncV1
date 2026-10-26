using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AuraSync.AI
{
    public class ChatMessage
    {
        public bool fromUser;
        public string text = "";

        /// <summary>The part's facts shown first, from the knowledge file (no AI involved).</summary>
        public bool isIntro;

        /// <summary>A failed request, shown to the student but never sent back to the AI as history.</summary>
        public bool isError;

        public string provider = "";
        public string model = "";
        public long latencyMs;

        /// <summary>The student's "Helpful?" answer: 1 yes, -1 no, 0 not answered.</summary>
        public int rating;

        /// <summary>Phase 3b: id of the room's copy once this answer is shared ("" = not shared).</summary>
        public string sharedId = "";

        /// <summary>Phase 3b: someone else's shared question or answer, brought in by ASK MORE.</summary>
        public bool fromRoom;

        public string id = Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    /// <summary>One conversation: about one part, or (key "") about the whole model.</summary>
    public class ChatThread
    {
        public string key = "";
        public string title = "";
        public readonly List<ChatMessage> messages = new List<ChatMessage>();
        public List<string> suggestions = new List<string>();

        /// <summary>A question is out with the AI. The answer lands here even if the window was closed.</summary>
        public bool waiting;
        public AiClient.Pending pending;
        public string lastQuestion = "";
    }

    /// <summary>
    /// Every conversation for the model on screen, kept on the phone so it carries on next time:
    /// closing the chat and reopening it on the same part continues where it left off, each part
    /// keeps its own conversation, and coming back to the model (even after restarting the app)
    /// brings them all back. One file per model:
    /// <c>&lt;persistentDataPath&gt;/Chats/&lt;md5 of the model URL&gt;.json</c>.
    /// </summary>
    public static class PartChatStore
    {
        private static readonly Dictionary<string, ChatThread> _threads = new Dictionary<string, ChatThread>();
        private static string _modelKey = null;

        [Serializable] private class SavedMessage
        {
            public bool fromUser, isIntro, isError, fromRoom;
            public string text = "", provider = "", model = "", sharedId = "", id = "";
            public long latencyMs;
            public int rating;
        }
        [Serializable] private class SavedThread
        {
            public string key = "", title = "", lastQuestion = "";
            public List<string> suggestions = new List<string>();
            public List<SavedMessage> messages = new List<SavedMessage>();
        }
        [Serializable] private class SavedFile { public List<SavedThread> threads = new List<SavedThread>(); }

        public static ChatThread Get(string modelKey, string threadKey, Func<ChatThread> create)
        {
            Switch(modelKey);
            if (!_threads.TryGetValue(threadKey, out ChatThread thread))
            {
                thread = create();
                thread.key = threadKey;
                _threads[threadKey] = thread;
                Save();
            }
            return thread;
        }

        public static int Count => _threads.Count;

        /// <summary>Writes this model's conversations to the phone (after each question, answer or rating).</summary>
        public static void Save()
        {
            if (string.IsNullOrEmpty(_modelKey)) return;
            try
            {
                var file = new SavedFile();
                foreach (ChatThread t in _threads.Values)
                {
                    var st = new SavedThread { key = t.key, title = t.title, lastQuestion = t.lastQuestion,
                                               suggestions = new List<string>(t.suggestions ?? new List<string>()) };
                    foreach (ChatMessage m in t.messages)
                        st.messages.Add(new SavedMessage
                        {
                            fromUser = m.fromUser, isIntro = m.isIntro, isError = m.isError, fromRoom = m.fromRoom,
                            text = m.text, provider = m.provider, model = m.model, sharedId = m.sharedId, id = m.id,
                            latencyMs = m.latencyMs, rating = m.rating,
                        });
                    file.threads.Add(st);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(PathFor(_modelKey)));
                File.WriteAllText(PathFor(_modelKey), JsonUtility.ToJson(file));
            }
            catch (Exception ex) { Debug.LogWarning($"[PartChatStore] Could not save chats: {ex.Message}"); }
        }

        /// <summary>Forgets this model's conversations, on screen and on the phone ("Clear chat").</summary>
        public static void Clear(string modelKey)
        {
            Switch(modelKey);
            foreach (ChatThread t in _threads.Values) t.pending?.Cancel();
            _threads.Clear();
            try { if (File.Exists(PathFor(modelKey))) File.Delete(PathFor(modelKey)); } catch { }
        }

        private static void Switch(string modelKey)
        {
            if (modelKey == _modelKey) return;
            foreach (ChatThread t in _threads.Values) t.pending?.Cancel();
            _threads.Clear();
            _modelKey = modelKey;
            Load();
        }

        private static void Load()
        {
            if (string.IsNullOrEmpty(_modelKey)) return;
            string path = PathFor(_modelKey);
            if (!File.Exists(path)) return;
            try
            {
                var file = JsonUtility.FromJson<SavedFile>(File.ReadAllText(path));
                if (file == null || file.threads == null) return;
                foreach (SavedThread st in file.threads)
                {
                    var t = new ChatThread { key = st.key ?? "", title = st.title ?? "", lastQuestion = st.lastQuestion ?? "",
                                             suggestions = st.suggestions ?? new List<string>() };
                    foreach (SavedMessage sm in st.messages)
                        t.messages.Add(new ChatMessage
                        {
                            fromUser = sm.fromUser, isIntro = sm.isIntro, isError = sm.isError, fromRoom = sm.fromRoom,
                            text = sm.text ?? "", provider = sm.provider ?? "", model = sm.model ?? "",
                            sharedId = sm.sharedId ?? "", id = string.IsNullOrEmpty(sm.id) ? Guid.NewGuid().ToString("N").Substring(0, 12) : sm.id,
                            latencyMs = sm.latencyMs, rating = sm.rating,
                        });
                    _threads[t.key] = t;
                }
                Debug.Log($"[PartChatStore] Restored {_threads.Count} conversation(s) for this model.");
            }
            catch (Exception ex) { Debug.LogWarning($"[PartChatStore] Could not read saved chats: {ex.Message}"); }
        }

        private static string PathFor(string modelKey)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(modelKey ?? ""));
                var sb = new System.Text.StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return Path.Combine(Application.persistentDataPath, "Chats", sb + ".json");
            }
        }
    }

    /// <summary>
    /// Records every AI answer and the student's "Helpful?" rating to a file on the device, for the
    /// paper's numbers (answer time, how often answers help, which model answered). Nothing is sent
    /// anywhere: <c>&lt;persistentDataPath&gt;/AiFeedback.jsonl</c>, one JSON object per line.
    /// </summary>
    public static class AiFeedbackLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "AiFeedback.jsonl");

        [Serializable]
        private class Line
        {
            public string kind;      // "answer", "rating" or "share"
            public string id;        // message id, ties a rating to its answer
            public string time;
            public string thread;    // part path, or "" for the whole model
            public string provider;
            public string model;
            public long ms;
            public int rating;
            public int questionChars;
            public int answerWords;
        }

        public static void Answer(ChatThread thread, ChatMessage answer, string question)
        {
            Write(new Line
            {
                kind = "answer", id = answer.id, time = DateTime.UtcNow.ToString("o"), thread = thread.key,
                provider = answer.provider, model = answer.model, ms = answer.latencyMs,
                questionChars = question?.Length ?? 0,
                answerWords = answer.text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length,
            });
        }

        public static void Rating(ChatThread thread, ChatMessage answer)
        {
            Write(new Line
            {
                kind = "rating", id = answer.id, time = DateTime.UtcNow.ToString("o"), thread = thread.key,
                provider = answer.provider, model = answer.model, ms = answer.latencyMs, rating = answer.rating,
            });
        }

        /// <summary>An answer shared to the room (Phase 3b).</summary>
        public static void Shared(ChatThread thread, ChatMessage answer)
        {
            Write(new Line
            {
                kind = "share", id = answer.id, time = DateTime.UtcNow.ToString("o"), thread = thread.key,
                provider = answer.provider, model = answer.model, ms = answer.latencyMs,
            });
        }

        private static void Write(Line line)
        {
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(line) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[AI] Could not write the feedback log: {ex.Message}"); }
        }

        /// <summary>A short summary of the log: answers, typical answer time, helpfulness.</summary>
        public static string Summary()
        {
            if (!File.Exists(FilePath)) return $"No AI answers logged yet ({FilePath}).";

            var answers = new List<Line>();
            var ratings = new Dictionary<string, int>(); // latest rating per answer
            int shares = 0;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                Line l;
                try { l = JsonUtility.FromJson<Line>(raw); } catch { continue; }
                if (l == null) continue;
                if (l.kind == "answer") answers.Add(l);
                else if (l.kind == "rating") ratings[l.id] = l.rating;
                else if (l.kind == "share") shares++;
            }
            if (answers.Count == 0) return $"No AI answers logged yet ({FilePath}).";

            var ms = answers.Select(a => a.ms).OrderBy(x => x).ToList();
            int yes = ratings.Values.Count(r => r > 0), no = ratings.Values.Count(r => r < 0);
            string byProvider = string.Join(", ", answers.GroupBy(a => a.provider).Select(g => $"{g.Key} {g.Count()}"));

            return $"{answers.Count} AI answer(s): median {ms[ms.Count / 2]} ms, slowest {ms[ms.Count - 1]} ms; " +
                   $"answered by {byProvider}. Rated: {yes} helpful, {no} not helpful" +
                   (yes + no > 0 ? $" ({100 * yes / (yes + no)}% helpful)" : "") + $". Shared to a room: {shares}. File: {FilePath}";
        }
    }
}
