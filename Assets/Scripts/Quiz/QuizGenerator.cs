using System;
using System.Collections.Generic;
using System.Linq;
using AuraSync.AI;
using AuraSync.Space;

namespace AuraSync.Quiz
{
    /// <summary>
    /// Asks the AI server for quiz questions (V3 Phase 11) from the model's part knowledge file:
    /// the whole model, or one part (and the parts it connects to, for context). Questions only test
    /// facts in the file; the server drops any it cannot mark (not exactly four options and one
    /// right answer). Models without part entries (the House) get whole-model questions.
    /// </summary>
    public static class QuizGenerator
    {
        public static AiClient.Pending Generate(string partPath, int count, string difficulty, Action<QuizResult> done)
        {
            return AiClient.Ensure().Quiz(Body(partPath, count, difficulty), done);
        }

        public static QuizBody Body(string partPath, int count, string difficulty)
        {
            PartKnowledge pk = PartKnowledge.Instance;
            var body = new QuizBody { count = Math.Max(1, Math.Min(15, count)), difficulty = difficulty ?? "medium" };

            string key = SpaceSceneManager.CurrentModelKey ?? "";
            body.model.name = System.IO.Path.GetFileNameWithoutExtension(key);
            if (pk != null && pk.Current != null)
            {
                body.model.context = pk.Current.context ?? "";
                body.model.summary = pk.Current.summary ?? "";
            }

            var parts = new List<QuizPart>();
            if (pk != null && pk.HasParts)
            {
                IEnumerable<PartKnowledgeEntry> scope = pk.AllParts;
                if (!string.IsNullOrEmpty(partPath))
                {
                    PartKnowledgeEntry target = pk.AllParts.FirstOrDefault(e => e.path == partPath)
                                             ?? pk.AllParts.FirstOrDefault(e => partPath.StartsWith(e.path + "/") || e.path.StartsWith(partPath + "/"));
                    if (target != null)
                    {
                        var near = new HashSet<string>(target.connectsTo ?? new List<string>()) { target.path };
                        scope = pk.AllParts.Where(e => near.Contains(e.path));
                    }
                }
                foreach (PartKnowledgeEntry e in scope)
                    parts.Add(new QuizPart
                    {
                        path = e.path, label = e.label ?? "", function = e.function ?? "", material = e.material ?? "",
                        connectsTo = (e.connectsTo ?? new List<string>()).Select(c => pk.LabelFor(c) is string l && l.Length > 0 ? l : c).ToArray(),
                    });
            }
            body.parts = parts.ToArray();
            return body;
        }

        /// <summary>The scope's label for the setup page.</summary>
        public static string ScopeLabel(string partPath) =>
            string.IsNullOrEmpty(partPath) ? "Whole model" : AuraSync.Analytics.HeatmapView.Label(partPath);
    }
}
