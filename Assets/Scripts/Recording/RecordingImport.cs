using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using AuraSync.Space;

namespace AuraSync.Recording
{
    /// <summary>
    /// One file per shared recording. A recording with sound is two files on this phone (the 3D
    /// session and its WAV); for sharing they are packed into a single <c>.aurarec</c>:
    ///
    ///   AURAREC-BUNDLE 1\n
    ///   {"session":N,"voice":M}\n
    ///   N bytes of the session file, then M bytes of the WAV
    ///
    /// A recording without sound is shared as its plain session file (it starts with "{"). Import
    /// takes either kind.
    /// </summary>
    public static class RecordingBundle
    {
        public const string Magic = "AURAREC-BUNDLE 1\n";

        [Serializable]
        private class BundleHeader { public long session; public long voice; }

        /// <summary>The single file to share for a recording: the session itself, or a bundle with its voice.</summary>
        public static string FileToShare(RecordingInfo info, string dir, string fileName)
        {
            if (!info.HasVoice) return info.path;
            Directory.CreateDirectory(dir);
            string bundle = Path.Combine(dir, fileName);
            long sessionLen = new FileInfo(info.path).Length, voiceLen = new FileInfo(info.VoicePath).Length;
            using (var outStream = new FileStream(bundle, FileMode.Create, FileAccess.Write))
            {
                byte[] head = Encoding.ASCII.GetBytes(Magic + JsonUtility.ToJson(new BundleHeader { session = sessionLen, voice = voiceLen }) + "\n");
                outStream.Write(head, 0, head.Length);
                using (var s = new FileStream(info.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) s.CopyTo(outStream);
                using (var v = new FileStream(info.VoicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) v.CopyTo(outStream);
            }
            return bundle;
        }

        /// <summary>
        /// Unpacks a shared file into the Recordings folder (session + voice beside it). Returns the
        /// new session path, or null with a reason.
        /// </summary>
        public static string Unpack(string source, string destSession, out string problem)
        {
            problem = null;
            using (var fs = new FileStream(source, FileMode.Open, FileAccess.Read))
            {
                var magic = new byte[Magic.Length];
                int got = fs.Read(magic, 0, magic.Length);
                if (got == magic.Length && Encoding.ASCII.GetString(magic) == Magic)
                {
                    string line = ReadLine(fs, 512);
                    BundleHeader h = null;
                    try { h = JsonUtility.FromJson<BundleHeader>(line); } catch { /* reported below */ }
                    if (h == null || h.session <= 0 || h.voice < 0 || Magic.Length + line.Length + 1 + h.session + h.voice > fs.Length)
                    {
                        problem = "That recording file is damaged (it may not have finished downloading).";
                        return null;
                    }
                    CopyBytes(fs, destSession, h.session);
                    if (h.voice > 0) CopyBytes(fs, VoiceTrack.PathFor(destSession), h.voice);
                    return destSession;
                }

                // A plain session file: JSON lines, starting with the header.
                fs.Position = 0;
                int first = fs.ReadByte();
                while (first == 0xEF || first == 0xBB || first == 0xBF || first == ' ' || first == '\r' || first == '\n') first = fs.ReadByte();
                if (first != '{')
                {
                    problem = "That file isn't an AuraSync recording.";
                    return null;
                }
                fs.Position = 0;
                using (var outStream = new FileStream(destSession, FileMode.Create, FileAccess.Write)) fs.CopyTo(outStream);
                return destSession;
            }
        }

        private static string ReadLine(Stream s, int max)
        {
            var sb = new StringBuilder();
            int b;
            while (sb.Length < max && (b = s.ReadByte()) >= 0 && b != '\n') sb.Append((char)b);
            return sb.ToString();
        }

        private static void CopyBytes(Stream from, string toPath, long count)
        {
            using (var outStream = new FileStream(toPath, FileMode.Create, FileAccess.Write))
            {
                var buf = new byte[64 * 1024];
                while (count > 0)
                {
                    int n = from.Read(buf, 0, (int)Math.Min(buf.Length, count));
                    if (n <= 0) throw new EndOfStreamException("The recording file ended early.");
                    outStream.Write(buf, 0, n);
                    count -= n;
                }
            }
        }
    }

    /// <summary>
    /// Recordings → Import: a recording someone shared (WhatsApp, Drive, email...) is picked with
    /// Android's file picker, copied into this phone's Recordings, and replays like any other.
    /// </summary>
    public static class RecordingImport
    {
        /// <summary>Where picked files land before they are unpacked (the app's own storage).</summary>
        public static string Inbox => Path.Combine(RecordingStore.Folder, "Inbox");

        /// <summary>Imported recordings are named rec_..._in so the list can say where they came from.</summary>
        public static bool IsImported(RecordingInfo info) =>
            info != null && Path.GetFileNameWithoutExtension(info.path).Contains("_in");

        /// <summary>Imports a picked file. Returns a message for the person; the file in the Inbox is removed.</summary>
        public static string Import(string pickedFile)
        {
            string dest = NewPath();
            try
            {
                string path = RecordingBundle.Unpack(pickedFile, dest, out string problem);
                if (path == null) { Discard(dest); return problem; }

                RecordingInfo info = RecordingStore.Describe(dest);
                if (info == null) { Discard(dest); return "That file isn't an AuraSync recording."; }

                // The same recording twice (shared again, or imported twice): keep one.
                foreach (RecordingInfo other in RecordingStore.List())
                {
                    if (other.path == dest) continue;
                    if (other.header.started == info.header.started && other.header.model == info.header.model &&
                        other.header.recorderName == info.header.recorderName)
                    {
                        Discard(dest);
                        return $"\"{other.name}\" is already in your recordings.";
                    }
                }

                string from = string.IsNullOrEmpty(info.header.recorderName) ? "" : $" from {info.header.recorderName}";
                return $"Imported \"{info.name}\"{from}" + (info.HasVoice ? ", with voice." : ".");
            }
            catch (Exception ex)
            {
                Discard(dest);
                Debug.LogWarning($"[Recording] Import failed: {ex}");
                return "Could not import: " + ex.Message;
            }
            finally
            {
                try { if (pickedFile.StartsWith(Inbox) && File.Exists(pickedFile)) File.Delete(pickedFile); } catch { /* left in the Inbox */ }
            }
        }

        private static string NewPath()
        {
            string stamp = "rec_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_in";
            string path = Path.Combine(RecordingStore.Folder, stamp + RecordingStore.Extension);
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(RecordingStore.Folder, $"{stamp}{n}{RecordingStore.Extension}");
            return path;
        }

        private static void Discard(string sessionPath)
        {
            try { if (File.Exists(sessionPath)) File.Delete(sessionPath); } catch { }
            try { string v = VoiceTrack.PathFor(sessionPath); if (File.Exists(v)) File.Delete(v); } catch { }
        }

        // ── Can this phone open the recording's model? ─────────────────

        /// <summary>
        /// Answers whether the recording's model is one this phone can open: a model of the
        /// catalogue, one of this phone's own scans, or one that ships with the app. A scan made on
        /// someone else's phone is not, so its recordings cannot be replayed here.
        /// </summary>
        public static void CheckModel(RecordingInfo info, Action<bool> answer)
        {
            string key = info?.header?.model ?? "";
            if (string.IsNullOrEmpty(key)) { answer(false); return; }
            bool remote = key.StartsWith("http://") || key.StartsWith("https://") || key.Contains("huggingface.co");
            if (!remote) { answer(true); return; } // ships with the app (StreamingAssets)
            if (ModelCatalog.Instance == null) { answer(true); return; } // cannot tell: let Play try

            ModelCatalog.Instance.GetAllModels(all =>
            {
                foreach (ModelData m in all)
                {
                    if (m == null || string.IsNullOrEmpty(m.glbUrl)) continue;
                    if (string.Equals(m.glbUrl, key, StringComparison.OrdinalIgnoreCase)) { answer(true); return; }
                    try
                    {
                        if (string.Equals(SpaceSceneManager.ResolveModel(m.glbUrl, out _, out _), key, StringComparison.OrdinalIgnoreCase))
                        { answer(true); return; }
                    }
                    catch { /* a malformed entry: skip it */ }
                }
                answer(false);
            });
        }
    }

    /// <summary>
    /// Android's file picker, for Import. On a phone it goes through AuraFilePicker.java (in
    /// Assets/Plugins/Android); in the Editor it is the Editor's open-file dialog.
    /// </summary>
    public static class RecordingFilePicker
    {
        /// <summary>Runs until a file is picked (its path, copied into the Inbox), cancelled (null) or failed (error).</summary>
        public static IEnumerator Pick(Action<string> picked, Action<string> failed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaClass cls;
            try
            {
                Directory.CreateDirectory(RecordingImport.Inbox);
                cls = new AndroidJavaClass("com.aurasync.files.AuraFilePicker");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    cls.CallStatic("pick", activity, RecordingImport.Inbox);
            }
            catch (Exception ex)
            {
                failed("The file picker could not open: " + ex.Message);
                yield break;
            }

            using (cls)
            {
                while (true)
                {
                    yield return null;
                    string r = cls.CallStatic<string>("takeResult");
                    if (r == null) continue;               // still picking or copying
                    if (r.Length == 0) { picked(null); yield break; }  // cancelled
                    if (r[0] == '!') { failed("Could not read that file: " + r.Substring(1)); yield break; }
                    picked(r);
                    yield break;
                }
            }
#elif UNITY_EDITOR
            yield return null;
            string path = UnityEditor.EditorUtility.OpenFilePanel("Import recording", "", "aurarec");
            if (string.IsNullOrEmpty(path)) { picked(null); yield break; }
            Directory.CreateDirectory(RecordingImport.Inbox);
            string copy = Path.Combine(RecordingImport.Inbox, Path.GetFileName(path));
            File.Copy(path, copy, true);
            picked(copy);
#else
            yield return null;
            failed("Import works on Android.");
#endif
        }
    }
}
