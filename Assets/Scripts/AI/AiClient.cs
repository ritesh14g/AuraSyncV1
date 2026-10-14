using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AuraSync.AI
{
    /// <summary>
    /// Where the AI server is, and the app key it expects. Read from
    /// <c>Assets/Resources/AuraSyncAI.json</c>:
    /// <code>{ "serverUrl": "https://aurasync-ai.&lt;name&gt;.workers.dev", "appKey": "..." }</code>
    /// The file is listed in .gitignore so the key does not end up in the repository. The key only
    /// deters casual misuse (it can be extracted from an APK); the AI providers are on free tiers,
    /// so the worst misuse can do is use up the day's quota.
    /// </summary>
    [Serializable]
    public class AiConfig
    {
        public string serverUrl = "";
        public string appKey = "";

        public bool IsValid => !string.IsNullOrWhiteSpace(serverUrl) && !string.IsNullOrWhiteSpace(appKey);

        private static AiConfig _cached;

        public static AiConfig Load()
        {
            if (_cached != null) return _cached;

            TextAsset file = Resources.Load<TextAsset>("AuraSyncAI");
            AiConfig cfg = null;
            if (file != null)
            {
                try { cfg = JsonUtility.FromJson<AiConfig>(file.text); }
                catch (Exception ex) { Debug.LogWarning($"[AI] AuraSyncAI.json could not be read: {ex.Message}"); }
            }
            if (cfg == null || !cfg.IsValid)
                Debug.LogWarning("[AI] No AI server configured: add Assets/Resources/AuraSyncAI.json with serverUrl and appKey.");

            _cached = cfg ?? new AiConfig();
            _cached.serverUrl = (_cached.serverUrl ?? "").Trim().TrimEnd('/');
            _cached.appKey = (_cached.appKey ?? "").Trim();
            return _cached;
        }
    }

    // ── Request and response shapes (field names match ai-server/src/index.js) ─

    [Serializable] public class AskModel { public string name = ""; public string context = ""; public string summary = ""; }
    [Serializable] public class AskPart { public string label = ""; public string function = ""; public string material = ""; public string[] connectsTo = new string[0]; }
    [Serializable] public class AskTurn { public string role = "user"; public string text = ""; }

    [Serializable]
    public class AskBody
    {
        public AskModel model = new AskModel();
        public AskPart part;          // null (sent as an empty object) = a whole-model question
        public AskTurn[] history = new AskTurn[0];
        public string question = "";
    }

    /// <summary>What a question came back as. <see cref="error"/> is a message fit to show a student.</summary>
    public class AskResult
    {
        public bool ok;
        public string answer = "";
        public string[] suggestions = new string[0];
        public string provider = "";
        public string model = "";
        public long latencyMs;
        public string error = "";
    }

    /// <summary>
    /// Talks to the AuraSync AI server (ai-server/, a Cloudflare Worker). Every AI call the app
    /// makes goes through here; the app itself never holds an AI provider's key.
    /// </summary>
    public class AiClient : MonoBehaviour
    {
        public static AiClient Instance { get; private set; }

        private const int TimeoutSeconds = 30;

        public static AiClient Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AuraSync_AiClient");
            DontDestroyOnLoad(go);
            return go.AddComponent<AiClient>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>A request in flight. Cancel() drops it: its callback never runs.</summary>
        public class Pending
        {
            internal UnityWebRequest web;
            public bool Cancelled { get; private set; }
            public void Cancel()
            {
                Cancelled = true;
                try { web?.Abort(); } catch { /* already finished */ }
            }
        }

        public bool IsConfigured => AiConfig.Load().IsValid;

        // ── Ask (Phase 3) ──────────────────────────────────────────────────────

        public Pending Ask(AskBody body, Action<AskResult> done)
        {
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            return Send("/ask", json, "application/json", null, (web, text, ms) =>
            {
                var result = new AskResult { latencyMs = ms };
                AskResponse res = Parse<AskResponse>(text);
                if (web != null && web.result == UnityWebRequest.Result.Success && res != null && res.ok && !string.IsNullOrWhiteSpace(res.answer))
                {
                    result.ok = true;
                    result.answer = TextForTmp.Clean(res.answer);
                    result.suggestions = res.suggestions ?? new string[0];
                    for (int i = 0; i < result.suggestions.Length; i++) result.suggestions[i] = TextForTmp.Clean(result.suggestions[i]);
                    result.provider = res.provider ?? "";
                    result.model = res.model ?? "";
                }
                else
                {
                    result.error = FriendlyError(web, res != null ? res.message : "");
                }
                done?.Invoke(result);
            });
        }

        // ── Find by meaning (Phase 4) ──────────────────────────────────────────

        public Pending Find(string query, FindPart[] parts, Action<FindResult> done)
        {
            var body = new FindBody { query = query ?? "", parts = parts ?? new FindPart[0] };
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            return Send("/find", json, "application/json", null, (web, text, ms) =>
            {
                var result = new FindResult { latencyMs = ms };
                FindResponse res = Parse<FindResponse>(text);
                if (web != null && web.result == UnityWebRequest.Result.Success && res != null && res.ok)
                {
                    result.ok = true;
                    result.matches = res.matches ?? new FindMatch[0];
                    foreach (FindMatch m in result.matches) m.why = TextForTmp.Clean(m.why);
                    result.provider = res.provider ?? "";
                }
                else
                {
                    result.error = FriendlyError(web, res != null ? res.message : "");
                }
                done?.Invoke(result);
            });
        }

        // ── Quiz (Phase 11) ────────────────────────────────────────────────────

        /// <summary>Multiple-choice questions from the part facts (the server's /quiz; Gemini first).</summary>
        public Pending Quiz(QuizBody body, Action<QuizResult> done)
        {
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));
            return Send("/quiz", json, "application/json", null, (web, text, ms) =>
            {
                var result = new QuizResult { latencyMs = ms };
                QuizResponse res = Parse<QuizResponse>(text);
                if (web != null && web.result == UnityWebRequest.Result.Success && res != null && res.ok && res.questions != null && res.questions.Length > 0)
                {
                    result.ok = true;
                    foreach (QuizQuestion q in res.questions)
                    {
                        q.question = TextForTmp.Clean(q.question);
                        q.explanation = TextForTmp.Clean(q.explanation);
                        for (int i = 0; i < q.options.Length; i++) q.options[i] = TextForTmp.Clean(q.options[i]);
                    }
                    result.questions = res.questions;
                    result.provider = res.provider ?? "";
                }
                else
                {
                    result.error = FriendlyError(web, res != null ? res.message : "");
                }
                done?.Invoke(result);
            });
        }

        // ── Speech to text (Phase 4 voice search) ──────────────────────────────

        /// <param name="vocabulary">Words to expect, such as the model's part names: helps with technical terms.</param>
        public Pending Transcribe(byte[] wav, string vocabulary, Action<TranscribeResult> done)
        {
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(vocabulary))
            {
                string v = vocabulary.Length > 600 ? vocabulary.Substring(0, 600) : vocabulary;
                headers["X-AuraSync-Vocabulary"] = Uri.EscapeDataString(v);
            }

            return Send("/transcribe", wav, "audio/wav", headers, (web, text, ms) =>
            {
                var result = new TranscribeResult { latencyMs = ms };
                TranscribeResponse res = Parse<TranscribeResponse>(text);
                if (web != null && web.result == UnityWebRequest.Result.Success && res != null && res.ok)
                {
                    result.ok = true;
                    result.text = TextForTmp.Clean(res.text ?? "").Trim();
                    result.provider = res.provider ?? "";
                }
                else
                {
                    result.error = FriendlyError(web, res != null ? res.message : "");
                }
                done?.Invoke(result);
            });
        }

        // ── Shared ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends one request to the AI server. <paramref name="handle"/> gets the finished request
        /// (null when the app has no server configured), the response text and the time taken in ms.
        /// It is not called at all if the request was cancelled.
        /// </summary>
        private Pending Send(string route, byte[] body, string contentType, Dictionary<string, string> headers,
                             Action<UnityWebRequest, string, long> handle)
        {
            var pending = new Pending();
            StartCoroutine(SendRoutine(route, body, contentType, headers, pending, handle));
            return pending;
        }

        private IEnumerator SendRoutine(string route, byte[] body, string contentType, Dictionary<string, string> headers,
                                        Pending pending, Action<UnityWebRequest, string, long> handle)
        {
            AiConfig cfg = AiConfig.Load();
            if (!cfg.IsValid)
            {
                if (!pending.Cancelled) handle(null, "", 0);
                yield break;
            }

            float started = Time.realtimeSinceStartup;
            using (var web = new UnityWebRequest(cfg.serverUrl + route, "POST"))
            {
                pending.web = web;
                web.uploadHandler = new UploadHandlerRaw(body);
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", contentType);
                web.SetRequestHeader("X-AuraSync-Key", cfg.appKey);
                if (headers != null) foreach (var kv in headers) web.SetRequestHeader(kv.Key, kv.Value);
                web.timeout = TimeoutSeconds;

                yield return web.SendWebRequest();
                if (pending.Cancelled) yield break;

                long ms = (long)((Time.realtimeSinceStartup - started) * 1000f);
                string text = web.downloadHandler != null ? web.downloadHandler.text : "";
                if (web.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning($"[AI] {route} failed: HTTP {web.responseCode} {web.error} {text}");
                handle(web, text, ms);
            }
        }

        private static T Parse<T>(string text) where T : class
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return JsonUtility.FromJson<T>(text); } catch { return null; }
        }

        /// <summary>A failure as a sentence a student can act on.</summary>
        private static string FriendlyError(UnityWebRequest web, string serverMessage)
        {
            if (web == null) return "AI features are not set up in this version of the app.";
            if (web.result == UnityWebRequest.Result.ConnectionError)
            {
                return web.error != null && web.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "The AI took too long to answer. Please try again."
                    : "No internet connection. The AI needs a connection (the part facts still work offline).";
            }
            switch (web.responseCode)
            {
                case 401: return "This app's AI key was refused. (Check Assets/Resources/AuraSyncAI.json.)";
                case 429: return "Lots of requests at once. Wait a few seconds and try again.";
                case 502:
                case 503: return !string.IsNullOrEmpty(serverMessage) ? serverMessage : "The AI is busy right now. Please try again in a minute.";
                default:  return "The AI could not answer right now. Please try again.";
            }
        }

        [Serializable]
        private class AskResponse
        {
            public bool ok;
            public string answer;
            public string[] suggestions;
            public string provider;
            public string model;
            public string error;
            public string message;
        }

        [Serializable] private class FindBody { public string query; public FindPart[] parts; }
        [Serializable] private class FindResponse { public bool ok; public FindMatch[] matches; public string provider; public string message; }
        [Serializable] private class TranscribeResponse { public bool ok; public string text; public string provider; public string message; }
        [Serializable] private class QuizResponse { public bool ok; public QuizQuestion[] questions; public string provider; public string message; }
    }

    [Serializable] public class FindPart { public string path = ""; public string label = ""; public string[] aliases = new string[0]; public string function = ""; }

    [Serializable] public class QuizPart { public string path = ""; public string label = ""; public string function = ""; public string material = ""; public string[] connectsTo = new string[0]; }
    [Serializable] public class QuizBody { public AskModel model = new AskModel(); public QuizPart[] parts = new QuizPart[0]; public int count = 5; public string difficulty = "medium"; }

    /// <summary>One multiple-choice question: four options, answer = index of the right one.</summary>
    [Serializable]
    public class QuizQuestion
    {
        public string   question = "";
        public string[] options = new string[4];
        public int      answer;
        public string   partPath = "";
        public string   explanation = "";

        public QuizQuestion Clone() => new QuizQuestion
        {
            question = question, options = (string[])options.Clone(), answer = answer, partPath = partPath, explanation = explanation,
        };
    }

    public class QuizResult
    {
        public bool ok;
        public QuizQuestion[] questions = new QuizQuestion[0];
        public string provider = "";
        public long latencyMs;
        public string error = "";
    }
    [Serializable] public class FindMatch { public string path = ""; public string why = ""; }

    public class FindResult
    {
        public bool ok;
        public FindMatch[] matches = new FindMatch[0];
        public string provider = "";
        public long latencyMs;
        public string error = "";
    }

    public class TranscribeResult
    {
        public bool ok;
        public string text = "";
        public string provider = "";
        public long latencyMs;
        public string error = "";
    }

    /// <summary>
    /// Makes AI text safe for TextMeshPro's default font, which has no glyph for typographic
    /// characters such as the non-breaking hyphen the models like to use (they would show as boxes),
    /// and drops Markdown bold markers. Escaping "&lt;" for rich text is left to whoever displays the
    /// text, so the stored conversation (which goes back to the AI as history) stays clean.
    /// </summary>
    public static class TextForTmp
    {
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '‐': case '‑': case '‒': case '–': case '−': sb.Append('-'); break;
                    case '—': case '―': sb.Append(" - "); break;
                    case '‘': case '’': case '‛': sb.Append('\''); break;
                    case '“': case '”': case '‟': sb.Append('"'); break;
                    case '…': sb.Append("..."); break;
                    case ' ': case ' ': case ' ': case ' ': sb.Append(' '); break;
                    case '•': sb.Append('-'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString().Replace("**", "");
        }
    }
}
