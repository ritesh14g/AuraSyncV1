using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AuraSync.AI
{
    public class SearchHit
    {
        public string path = "";
        public string label = "";
        public string detail = "";   // what it does, or the AI's reason for the match
        public float score;          // 0..1, on-device matches only
        public bool byMeaning;       // found by the AI (search step 2)
    }

    /// <summary>
    /// Finding a part from what a student types or says: "turbine", "the fan", "inlet guide vane".
    ///
    /// **Step 1, on the phone, instant and offline:** compare the words against every part's label,
    /// other names and node name, tolerating typos ("turbin"), plurals ("vanes") and filler words
    /// ("show me the"). This answers most searches.
    ///
    /// **Step 2, the AI, only when step 1 is not sure:** "the part that burns fuel" names no part, so
    /// the AI server's /find matches it by meaning (see <see cref="AiClient.Find"/>).
    /// </summary>
    public static class PartSearch
    {
        /// <summary>A step 1 score at or above this is trusted without asking the AI.</summary>
        public const float Confident = 0.75f;

        private const float MinScore = 0.4f;

        private static readonly HashSet<string> Filler = new HashSet<string>
        {
            "a", "an", "the", "of", "to", "in", "on", "for", "and", "is", "are", "it", "its", "this", "that",
            "show", "me", "find", "where", "wheres", "which", "what", "whats", "select", "highlight", "open",
            "go", "please", "can", "you", "i", "want", "see", "look", "at", "part", "parts", "piece", "one",
        };

        /// <summary>Step 1. Best matches first, at most <paramref name="max"/>; empty when the model has no part labels.</summary>
        public static List<SearchHit> Local(string query, int max = 5)
        {
            var hits = new List<SearchHit>();
            PartKnowledge pk = PartKnowledge.Instance;
            if (pk == null || !pk.HasParts) return hits;

            string q = Normalise(query);
            List<string> qTokens = Tokens(q);
            if (qTokens.Count == 0) return hits;
            string qJoined = string.Join(" ", qTokens);

            foreach (PartKnowledgeEntry e in pk.AllParts)
            {
                float best = Field(e.label, qJoined, qTokens) * 1.0f;
                if (e.aliases != null)
                    foreach (string a in e.aliases) best = Mathf.Max(best, Field(a, qJoined, qTokens) * 0.95f);
                best = Mathf.Max(best, Field(e.node, qJoined, qTokens) * 0.6f);
                best = Mathf.Max(best, Field(e.function, qJoined, qTokens) * 0.5f);

                if (best >= MinScore)
                {
                    hits.Add(new SearchHit
                    {
                        path = e.path,
                        label = PartNames.SentenceCase(e.label),
                        detail = e.function ?? "",
                        score = best,
                    });
                }
            }

            return hits.OrderByDescending(h => h.score).ThenBy(h => h.label).Take(max).ToList();
        }

        /// <summary>The part names and other names, as a hint for speech recognition.</summary>
        public static string Vocabulary()
        {
            PartKnowledge pk = PartKnowledge.Instance;
            if (pk == null || !pk.HasParts) return "";
            var words = new List<string>();
            foreach (PartKnowledgeEntry e in pk.AllParts)
            {
                if (!string.IsNullOrWhiteSpace(e.label)) words.Add(PartNames.SentenceCase(e.label));
                if (e.aliases != null) words.AddRange(e.aliases.Where(a => !string.IsNullOrWhiteSpace(a)));
            }
            string model = pk.ModelName;
            return (string.IsNullOrEmpty(model) ? "" : model + ": ") + string.Join(", ", words.Distinct());
        }

        /// <summary>What step 2 sends: every labelled part.</summary>
        public static FindPart[] ForAi()
        {
            PartKnowledge pk = PartKnowledge.Instance;
            if (pk == null || !pk.HasParts) return new FindPart[0];
            return pk.AllParts.Select(e => new FindPart
            {
                path = e.path,
                label = PartNames.SentenceCase(e.label),
                aliases = (e.aliases ?? new List<string>()).ToArray(),
                function = e.function ?? "",
            }).ToArray();
        }

        // ── Scoring ────────────────────────────────────────────────────────────

        /// <summary>How well one field (a label, other name, node name or description) matches the query.</summary>
        private static float Field(string field, string qJoined, List<string> qTokens)
        {
            if (string.IsNullOrWhiteSpace(field)) return 0f;
            List<string> fTokens = Tokens(Normalise(field));
            if (fTokens.Count == 0) return 0f;
            string fJoined = string.Join(" ", fTokens);

            if (fJoined == qJoined) return 1f;
            if (qJoined.Length >= 3 && fJoined.Contains(qJoined)) return 0.9f;

            float sum = 0f;
            foreach (string qt in qTokens)
            {
                float t = 0f;
                foreach (string ft in fTokens) t = Mathf.Max(t, TokenMatch(qt, ft));
                sum += t;
            }
            // Every query word matched exactly scores 0.85: below an exact whole-name match, above the
            // confidence line.
            return 0.85f * sum / qTokens.Count;
        }

        private static float TokenMatch(string q, string f)
        {
            if (q == f) return 1f;
            if (q.Length >= 3 && f.Length >= 3 && (f.StartsWith(q) || q.StartsWith(f))) return 0.85f;
            int longest = Mathf.Max(q.Length, f.Length);
            if (longest >= 4)
            {
                int allowed = longest >= 7 ? 2 : 1;
                if (Mathf.Abs(q.Length - f.Length) <= allowed && Distance(q, f, allowed) <= allowed) return 0.75f;
            }
            return 0f;
        }

        /// <summary>Edit distance, stopping early once it passes <paramref name="limit"/>.</summary>
        private static int Distance(string a, string b, int limit)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                    rowMin = Math.Min(rowMin, cur[j]);
                }
                if (rowMin > limit) return limit + 1;
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }

        private static string Normalise(string s)
        {
            var sb = new StringBuilder(s?.Length ?? 0);
            foreach (char c in (s ?? "").ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            return sb.ToString();
        }

        private static List<string> Tokens(string normalised)
        {
            var list = new List<string>();
            foreach (string raw in normalised.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Filler.Contains(raw)) continue;
                list.Add(Stem(raw));
            }
            return list;
        }

        /// <summary>Plurals to singular, so "vanes" finds "vane" and "casings" finds "casing".</summary>
        private static string Stem(string w)
        {
            if (w.Length > 4 && w.EndsWith("ies")) return w.Substring(0, w.Length - 3) + "y";
            if (w.Length > 4 && (w.EndsWith("ses") || w.EndsWith("xes") || w.EndsWith("ches") || w.EndsWith("shes"))) return w.Substring(0, w.Length - 2);
            if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss")) return w.Substring(0, w.Length - 1);
            return w;
        }
    }

    /// <summary>
    /// The paper's search numbers, on the device only: how often a search ends with a part picked,
    /// how long that takes, how often the AI was needed, and how often voice was used.
    /// <c>&lt;persistentDataPath&gt;/SearchLog.jsonl</c>, one JSON object per line.
    /// </summary>
    public static class SearchLog
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "SearchLog.jsonl");

        [Serializable]
        private class Line
        {
            public string kind;     // "search" or "pick"
            public string id;
            public string time;
            public string method;   // "text" or "voice"
            public int queryChars;
            public int localHits;
            public bool confident;
            public bool usedAi;
            public long aiMs;
            public long speechMs;
            public int rank;        // pick: position in the list, 1 = top
            public bool byMeaning;  // pick: an AI match
            public float seconds;   // pick: from the search starting to the pick
        }

        public static void Search(string id, string method, string query, int localHits, bool confident, bool usedAi, long aiMs, long speechMs)
        {
            Write(new Line
            {
                kind = "search", id = id, time = DateTime.UtcNow.ToString("o"), method = method,
                queryChars = query?.Length ?? 0, localHits = localHits, confident = confident, usedAi = usedAi,
                aiMs = aiMs, speechMs = speechMs,
            });
        }

        public static void Pick(string id, int rank, bool byMeaning, float seconds)
        {
            Write(new Line { kind = "pick", id = id, time = DateTime.UtcNow.ToString("o"), rank = rank, byMeaning = byMeaning, seconds = seconds });
        }

        private static void Write(Line line)
        {
            try { File.AppendAllText(FilePath, JsonUtility.ToJson(line) + "\n"); }
            catch (Exception ex) { Debug.LogWarning($"[Search] Could not write the search log: {ex.Message}"); }
        }

        public static string Summary()
        {
            if (!File.Exists(FilePath)) return $"No searches logged yet ({FilePath}).";
            var searches = new List<Line>();
            var picks = new Dictionary<string, Line>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                Line l;
                try { l = JsonUtility.FromJson<Line>(raw); } catch { continue; }
                if (l == null) continue;
                if (l.kind == "search") searches.Add(l);
                else if (l.kind == "pick" && !picks.ContainsKey(l.id)) picks[l.id] = l;
            }
            if (searches.Count == 0) return $"No searches logged yet ({FilePath}).";

            int ids = searches.Select(s => s.id).Distinct().Count();
            int picked = picks.Count;
            var times = picks.Values.Select(p => p.seconds).OrderBy(x => x).ToList();
            int voice = searches.Count(s => s.method == "voice");
            int ai = searches.Count(s => s.usedAi);
            return $"{ids} search(es), {picked} ended with a part picked ({100 * picked / Math.Max(1, ids)}%)" +
                   (times.Count > 0 ? $", median {times[times.Count / 2]:0.0} s to pick" : "") +
                   $". Voice: {voice}, needed the AI: {ai}. File: {FilePath}";
        }
    }
}
