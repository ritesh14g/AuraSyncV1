using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AuraSync.EditorTools
{
    /// <summary>
    /// Sends pictures and a prompt to Gemini and returns its JSON answer as text.
    ///
    /// Used only by the Part Labeler, inside the Unity Editor, on the developer's computer. The key
    /// lives in EditorPrefs on that computer: it is never compiled into the app and never uploaded.
    /// The app's own AI calls go through the Cloudflare Worker instead (ai-server/).
    /// </summary>
    internal static class GeminiVisionClient
    {
        private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/";

        internal class GeminiException : Exception
        {
            public readonly long status;
            public GeminiException(long status, string message) : base(message) { this.status = status; }
            public bool IsQuota => status == 429;
            public bool IsBadKey => (status == 400 && Message.Contains("API_KEY")) || status == 401 || status == 403;

            /// <summary>The model no longer exists for this key (Google retires free models).</summary>
            public bool IsRetired => status == 404;

            /// <summary>Google's side is busy or briefly down ("high demand"): worth waiting and retrying.</summary>
            public bool IsTransient => status == 0 || status == 500 || status == 502 || status == 503 || status == 504;
        }

        public static async Task<string> GenerateJson(string apiKey, string model, string prompt,
                                                      IList<byte[]> jpegs, CancellationToken cancel)
        {
            string body = BuildBody(prompt, jpegs);

            using (var req = new UnityWebRequest(Endpoint + UnityWebRequest.EscapeURL(model) + ":generateContent", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-goog-api-key", apiKey);
                req.timeout = 120;

                UnityWebRequestAsyncOperation op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        req.Abort();
                        throw new OperationCanceledException();
                    }
                    await Task.Delay(100);
                }

                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (req.result != UnityWebRequest.Result.Success)
                {
                    string detail = text.Length > 300 ? text.Substring(0, 300) : text;
                    throw new GeminiException(req.responseCode, $"HTTP {req.responseCode}: {req.error}. {detail}");
                }

                GenResponse res = null;
                try { res = JsonUtility.FromJson<GenResponse>(text); } catch { /* handled below */ }

                if (res == null || res.candidates == null || res.candidates.Length == 0)
                {
                    string why = res != null && res.promptFeedback != null && !string.IsNullOrEmpty(res.promptFeedback.blockReason)
                        ? $"blocked ({res.promptFeedback.blockReason})"
                        : "no answer";
                    throw new GeminiException(0, $"Gemini gave {why}.");
                }

                var sb = new StringBuilder();
                Content content = res.candidates[0].content;
                if (content != null && content.parts != null)
                    foreach (ContentPart p in content.parts) sb.Append(p.text);

                string answer = StripFence(sb.ToString());
                if (string.IsNullOrWhiteSpace(answer))
                    throw new GeminiException(0, $"Gemini's answer was empty (finishReason {res.candidates[0].finishReason}).");
                return answer;
            }
        }

        /// <summary>
        /// Built by hand: JsonUtility would write every field of every part, and Gemini rejects a
        /// part that has both "text" and "inlineData" set, even when one is empty.
        /// </summary>
        private static string BuildBody(string prompt, IList<byte[]> jpegs)
        {
            var sb = new StringBuilder();
            sb.Append("{\"contents\":[{\"role\":\"user\",\"parts\":[");
            sb.Append("{\"text\":").Append(JsonString(prompt)).Append('}');
            if (jpegs != null)
            {
                foreach (byte[] jpg in jpegs)
                {
                    sb.Append(",{\"inlineData\":{\"mimeType\":\"image/jpeg\",\"data\":\"");
                    sb.Append(Convert.ToBase64String(jpg));
                    sb.Append("\"}}");
                }
            }
            sb.Append("]}],\"generationConfig\":{\"temperature\":0.2,\"responseMimeType\":\"application/json\"}}");
            return sb.ToString();
        }

        internal static string JsonString(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 16);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string StripFence(string s)
        {
            s = (s ?? "").Trim();
            if (!s.StartsWith("```")) return s;
            int firstNewline = s.IndexOf('\n');
            int lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline < 0 || lastFence <= firstNewline) return s;
            return s.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
        }

        // ── Response shape (only the fields we read) ───────────────────────────

        [Serializable] private class GenResponse { public Candidate[] candidates; public PromptFeedback promptFeedback; }
        [Serializable] private class Candidate { public Content content; public string finishReason; }
        [Serializable] private class Content { public ContentPart[] parts; }
        [Serializable] private class ContentPart { public string text; }
        [Serializable] private class PromptFeedback { public string blockReason; }
    }
}
