using System;
using System.Linq;

namespace AuraSync.AI
{
    /// <summary>
    /// What to call a part on screen: its reviewed label from the part knowledge file ("Fan rotor")
    /// when the model has one, otherwise the node name from the model file ("Object_6.1").
    ///
    /// One place for this, so the parts drawer, the selected-part chip and the AI chat never
    /// disagree about a part's name.
    /// </summary>
    public static class PartNames
    {
        public static string Display(string path, string fallback)
        {
            PartKnowledge pk = PartKnowledge.Instance;
            if (pk != null && pk.TryGet(path, out PartKnowledgeEntry e) && !string.IsNullOrWhiteSpace(e.label))
                return SentenceCase(e.label);
            return fallback ?? "";
        }

        /// <summary>
        /// Labels arrive in mixed styles ("Fan Rotor", "turbine casing"). Show them all one way:
        /// first letter capital, ordinary words lower case, acronyms (APU, HP) left alone.
        /// </summary>
        public static string SentenceCase(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "";
            string[] words = label.Trim().Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (w.Length == 0) continue;
                bool acronym = w.Length > 1 && w.All(c => !char.IsLetter(c) || char.IsUpper(c));
                if (acronym) continue;
                words[i] = i == 0
                    ? char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()
                    : w.ToLowerInvariant();
            }
            return string.Join(" ", words);
        }

        /// <summary>True if the filter text appears in the part's label, other names, or node name.</summary>
        public static bool Matches(string path, string nodeName, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (Contains(nodeName, filter)) return true;

            PartKnowledge pk = PartKnowledge.Instance;
            if (pk == null || !pk.TryGet(path, out PartKnowledgeEntry e)) return false;
            if (Contains(e.label, filter)) return true;
            return e.aliases != null && e.aliases.Any(a => Contains(a, filter));
        }

        private static bool Contains(string s, string filter) =>
            !string.IsNullOrEmpty(s) && s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
