using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AuraSync.Reports
{
    [Serializable] public class StudentScore { public string name = ""; public int actor; public int correct; public int answered; public int total; public bool finished; public string note = ""; }
    [Serializable] public class QuestionStat { public string text = ""; public string partPath = ""; public string partLabel = ""; public string[] options = new string[0]; public int answer; public int correct; public int answered; public string explanation = ""; }
    [Serializable] public class PartRow { public string path = ""; public string label = ""; public float attention; public int questions; public int asked; public int missed; }

    /// <summary>
    /// A report (V3 Phases 10 and 11): a quiz's results, or a heatmap snapshot. Saved as JSON on the
    /// phone that made it, readable any time, with no room and no model.
    /// </summary>
    [Serializable]
    public class Report
    {
        public string id = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string kind = "quiz";          // "quiz" or "heatmap"
        public string title = "";
        public string model = "";             // model key
        public string modelName = "";
        public string created = DateTime.UtcNow.ToString("o");
        public string room = "";
        public bool   seen;

        // Quiz
        public string difficulty = "";
        public List<StudentScore> students = new List<StudentScore>();
        public List<QuestionStat> questions = new List<QuestionStat>();

        // Both: per part. Quiz: attention (from the heatmap counts, if any) beside how often that
        // part's questions were missed. Heatmap: attention only.
        public List<PartRow> parts = new List<PartRow>();
        public string scope = "";             // heatmap: "Everyone" or a person's name

        public DateTime CreatedLocal => DateTime.TryParse(created, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d) ? d.ToLocalTime() : DateTime.Now;

        /// <summary>Class average, as a share of questions right (finished or not).</summary>
        public float Average => students.Count == 0 ? 0f : students.Average(s => s.total > 0 ? (float)s.correct / s.total : 0f);
    }

    public static class ReportStore
    {
        public static event Action OnChanged;

        public static string Folder
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Reports");
                try { Directory.CreateDirectory(dir); } catch { /* reported on write */ }
                return dir;
            }
        }

        private static string FileFor(string id) => Path.Combine(Folder, id + ".json");

        public static void Save(Report r)
        {
            try { File.WriteAllText(FileFor(r.id), JsonUtility.ToJson(r, true)); }
            catch (Exception ex) { Debug.LogWarning($"[Reports] Could not save: {ex.Message}"); }
            OnChanged?.Invoke();
        }

        public static List<Report> List()
        {
            var list = new List<Report>();
            try
            {
                foreach (string f in Directory.GetFiles(Folder, "*.json"))
                {
                    try { var r = JsonUtility.FromJson<Report>(File.ReadAllText(f)); if (r != null) list.Add(r); }
                    catch { /* skip a broken file */ }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Reports] Cannot list: {ex.Message}"); }
            return list.OrderByDescending(r => r.CreatedLocal).ToList();
        }

        private static int _unseenCache = -1;

        /// <summary>Reports not opened yet: the red badge on REPORTS.</summary>
        public static int Unseen
        {
            get
            {
                if (_unseenCache < 0) _unseenCache = List().Count(r => !r.seen);
                return _unseenCache;
            }
        }

        public static int Count => List().Count;

        public static void MarkSeen(Report r)
        {
            if (r == null || r.seen) return;
            r.seen = true;
            Save(r);
        }

        public static void Delete(Report r)
        {
            try { File.Delete(FileFor(r.id)); } catch (Exception ex) { Debug.LogWarning($"[Reports] Cannot delete: {ex.Message}"); }
            OnChanged?.Invoke();
        }

        static ReportStore() { OnChanged += () => _unseenCache = -1; }

        // ── CSV ────────────────────────────────────────────────────────────────

        public static string ToCsv(Report r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Report,{Q(r.title)}");
            sb.AppendLine($"Model,{Q(r.modelName)}");
            sb.AppendLine($"Created,{r.CreatedLocal:yyyy-MM-dd HH:mm}");
            if (r.kind == "quiz")
            {
                sb.AppendLine($"Class average,{r.Average * 100f:0}%");
                sb.AppendLine();
                sb.AppendLine("Student,Correct,Answered,Questions,Score %,Finished,Note");
                foreach (StudentScore s in r.students)
                    sb.AppendLine($"{Q(s.name)},{s.correct},{s.answered},{s.total},{(s.total > 0 ? 100f * s.correct / s.total : 0f):0},{(s.finished ? "yes" : "no")},{Q(s.note)}");
                sb.AppendLine();
                sb.AppendLine("Question,Part,Correct answer,Answered,Correct,Correct %");
                for (int i = 0; i < r.questions.Count; i++)
                {
                    QuestionStat q = r.questions[i];
                    string right = q.answer >= 0 && q.answer < q.options.Length ? q.options[q.answer] : "";
                    sb.AppendLine($"{Q($"{i + 1}. {q.text}")},{Q(q.partLabel)},{Q(right)},{q.answered},{q.correct},{(q.answered > 0 ? 100f * q.correct / q.answered : 0f):0}");
                }
            }
            sb.AppendLine();
            sb.AppendLine(r.kind == "quiz" ? "Part,Attention,Questions,Answers,Missed,Missed %" : $"Part,Attention ({Q(r.scope)})");
            foreach (PartRow p in r.parts)
            {
                if (r.kind == "quiz")
                    sb.AppendLine($"{Q(p.label)},{p.attention:0.0},{p.questions},{p.asked},{p.missed},{(p.asked > 0 ? 100f * p.missed / p.asked : 0f):0}");
                else
                    sb.AppendLine($"{Q(p.label)},{p.attention:0.0}");
            }
            return sb.ToString();
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
