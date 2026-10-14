using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AuraSync.Recording
{
    /// <summary>
    /// What a session recording is on disk (V3 Phase 9): one <c>.aurarec</c> file per recording, in
    /// <c>&lt;persistentDataPath&gt;/Recordings</c>, written as it happens, one JSON object per line:
    ///
    ///   line 1      the header: model, when, room, who recorded it
    ///   lines 2..   events: seconds since the start, a kind, and its data (see <see cref="Kinds"/>)
    ///   last line   "end": the name given at STOP and the length
    ///
    /// Lines are flushed every second, so if the app is killed the file is still good up to that
    /// moment; a file with no "end" line is listed as recovered. Events, not video: a minute of a
    /// two-phone session is in the tens of kilobytes. Voice audio is not recorded, only who was
    /// speaking when.
    /// </summary>
    public static class Kinds
    {
        public const string Pose     = "pose";     // f: position (3), rotation (4), scale (3) of the model
        public const string Explode  = "explode";  // f[0]
        public const string Isolate  = "isolate";  // s: part path, "" = whole model
        public const string Select   = "select";   // s: part path, "" = none
        public const string Section  = "section";  // f: SectionCutter.CutState.Pack()
        public const string Sim      = "sim";      // f: mode, playing, speed, value, phase now, assemble
        public const string Note     = "note";     // s: PartNote as JSON (added or edited)
        public const string NoteGone = "note-";    // s: note id
        public const string Answer   = "answer";   // s: SharedAnswerLine as JSON
        public const string AnswerGone = "answer-"; // s: answer id
        public const string View     = "view";     // a: actor; s: part path; f: point (3), normal (3), dir (3), distance
        public const string Ping     = "ping";     // a: actor; s: part path; f: point (3), normal (3)
        public const string Speak    = "speak";    // a: actor; f[0] 1 = started, 0 = stopped
        public const string Join     = "join";     // a: actor; s: name
        public const string Leave    = "leave";    // a: actor
        public const string Find     = "find";     // s: what happened in a FIND task
        public const string End      = "end";      // s: name; f[0]: length in seconds
    }

    [Serializable]
    public class RecHeader
    {
        public string kind = "header";
        public int    version = 1;
        public string model = "";       // the model key (URL), as SpaceSceneManager.CurrentModelKey
        public string modelName = "";
        public string started = "";     // ISO 8601, UTC
        public string room = "";        // "" when recorded alone
        public int    recorder;         // the recording phone's actor number (0 alone)
        public string recorderName = "";
    }

    [Serializable]
    public class RecLine
    {
        public float   t;
        public string  k = "";
        public int     a;
        public string  s = "";
        public float[] f;
    }

    [Serializable]
    public class SharedAnswerLine
    {
        public string id = "", path = "", part = "", question = "", answer = "", asker = "";
    }

    /// <summary>One recording as the Recordings list shows it.</summary>
    public class RecordingInfo
    {
        public string    path;
        public RecHeader header;
        public string    name;
        public float     seconds;
        public long      bytes;       // the event file
        public long      voiceBytes;  // the sound beside it (0 = none)
        public bool      recovered;
        public DateTime  started;

        public bool  HasVoice => voiceBytes > 0;
        public string VoicePath => VoiceTrack.PathFor(path);
        public long  TotalBytes => bytes + voiceBytes;
        public float KbPerMinute => seconds > 1f ? TotalBytes / 1024f / (seconds / 60f) : 0f;
        public float EventKbPerMinute => seconds > 1f ? bytes / 1024f / (seconds / 60f) : 0f;
    }

    /// <summary>A recording read back for replay.</summary>
    public class LoadedRecording
    {
        public RecordingInfo info;
        public readonly List<RecLine> lines = new List<RecLine>();
        public readonly Dictionary<int, string> names = new Dictionary<int, string>();
        public float Duration;
    }

    public static class RecordingStore
    {
        public const string Extension = ".aurarec";

        public static string Folder
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Recordings");
                try { Directory.CreateDirectory(dir); } catch { /* reported when writing */ }
                return dir;
            }
        }

        public static string NewPath() =>
            Path.Combine(Folder, "rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Extension);

        public static string DefaultName(string modelName, DateTime when) =>
            $"{(string.IsNullOrEmpty(modelName) ? "Session" : modelName)} {when:d MMM HH:mm}";

        /// <summary>Every recording, newest first. Reads only each file's first and last lines.</summary>
        public static List<RecordingInfo> List()
        {
            var list = new List<RecordingInfo>();
            string[] files;
            try { files = Directory.GetFiles(Folder, "*" + Extension); }
            catch (Exception ex) { Debug.LogWarning($"[Recording] Cannot list recordings: {ex.Message}"); return list; }

            foreach (string f in files)
            {
                RecordingInfo info = Describe(f);
                if (info != null) list.Add(info);
            }
            return list.OrderByDescending(i => i.started).ToList();
        }

        public static RecordingInfo Describe(string file)
        {
            try
            {
                string first = File.ReadLines(file).FirstOrDefault();
                if (string.IsNullOrEmpty(first)) return null;
                var header = JsonUtility.FromJson<RecHeader>(first);
                if (header == null || header.kind != "header") return null;

                var info = new RecordingInfo
                {
                    path = file, header = header, bytes = new FileInfo(file).Length,
                    voiceBytes = VoiceTrack.SoundBytes(VoiceTrack.PathFor(file)),
                };
                if (!DateTime.TryParse(header.started, null, System.Globalization.DateTimeStyles.RoundtripKind, out info.started))
                    info.started = File.GetCreationTime(file);
                info.started = info.started.ToLocalTime();

                RecLine last = LastLine(file);
                if (last != null && last.k == Kinds.End)
                {
                    info.name = last.s;
                    info.seconds = last.f != null && last.f.Length > 0 ? last.f[0] : last.t;
                }
                else
                {
                    info.recovered = true;
                    info.seconds = last != null ? last.t : 0f;
                    info.name = "(recovered) " + DefaultName(header.modelName, info.started);
                }
                if (string.IsNullOrWhiteSpace(info.name)) info.name = DefaultName(header.modelName, info.started);
                return info;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recording] Cannot read {Path.GetFileName(file)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>The last complete line of a file, read from its end (recordings can be megabytes).</summary>
        private static RecLine LastLine(string file)
        {
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                int take = (int)Math.Min(len, 8192);
                fs.Seek(len - take, SeekOrigin.Begin);
                var buf = new byte[take];
                int read = fs.Read(buf, 0, take);
                string tail = Encoding.UTF8.GetString(buf, 0, read);
                string[] lines = tail.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string l = lines[i].Trim();
                    if (!l.StartsWith("{") || !l.EndsWith("}")) continue; // a line cut short by a crash
                    try { var r = JsonUtility.FromJson<RecLine>(l); if (r != null && !string.IsNullOrEmpty(r.k)) return r; }
                    catch { /* keep looking */ }
                }
            }
            return null;
        }

        public static LoadedRecording Load(RecordingInfo info)
        {
            var rec = new LoadedRecording { info = info };
            bool first = true;
            foreach (string raw in File.ReadLines(info.path))
            {
                if (first) { first = false; continue; } // header
                string l = raw.Trim();
                if (l.Length == 0 || !l.EndsWith("}")) continue;
                RecLine line;
                try { line = JsonUtility.FromJson<RecLine>(l); } catch { continue; }
                if (line == null || string.IsNullOrEmpty(line.k)) continue;
                if (line.k == Kinds.End) break;
                if (line.k == Kinds.Join) rec.names[line.a] = line.s;
                rec.lines.Add(line);
                if (line.t > rec.Duration) rec.Duration = line.t;
            }
            rec.lines.Sort((x, y) => x.t.CompareTo(y.t)); // stable enough: lines are written in order
            rec.Duration = Mathf.Max(rec.Duration, info.seconds);
            if (!rec.names.ContainsKey(info.header.recorder)) rec.names[info.header.recorder] = info.header.recorderName;
            return rec;
        }

        public static bool Delete(RecordingInfo info)
        {
            try
            {
                File.Delete(info.path);
                if (File.Exists(info.VoicePath)) File.Delete(info.VoicePath);
                return true;
            }
            catch (Exception ex) { Debug.LogWarning($"[Recording] Cannot delete: {ex.Message}"); return false; }
        }

        public static string SizeText(long bytes) =>
            bytes >= 1024 * 1024 ? $"{bytes / 1024f / 1024f:0.0} MB" : $"{Mathf.Max(1, bytes / 1024)} KB";

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }

    /// <summary>The paper's recording number: size per minute (against a screen recording of the same session).</summary>
    public static class RecordingLog
    {
        public static string Summary()
        {
            List<RecordingInfo> all = RecordingStore.List();
            if (all.Count == 0) return $"Recording: none yet ({RecordingStore.Folder}).";
            var timed = all.Where(i => i.seconds >= 30f).ToList();
            string rate = timed.Count > 0
                ? $"average {timed.Average(i => i.KbPerMinute):0} KB per minute with sound ({timed.Average(i => i.EventKbPerMinute):0} KB of it the 3D events) " +
                  $"over {timed.Count} recording(s) of 30 s or more"
                : "none 30 s or longer yet";
            return $"Recording: {all.Count} recording(s), {RecordingStore.SizeText(all.Sum(i => i.TotalBytes))} in all; {rate}. " +
                   $"Folder: {RecordingStore.Folder}";
        }
    }
}
