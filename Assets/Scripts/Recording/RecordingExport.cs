using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AuraSync.Recording
{
    /// <summary>
    /// EXPORT for a recording (V3 Phase 9): a copy goes into the phone's
    /// <c>Download/AuraSync</c> folder, then Android's share sheet opens with it (WhatsApp, Drive,
    /// email...). A recording with sound sends two files: the 3D session and the voice (a WAV any
    /// player opens). The sharing itself is <see cref="FileExport"/>, which reports use too.
    /// </summary>
    public static class RecordingExport
    {
        /// <summary>Exports; returns a message for the person (where it went, or what failed).</summary>
        public static string Export(RecordingInfo info)
        {
            if (info == null || !File.Exists(info.path)) return "That recording is gone.";
            string name = FileExport.SafeName(info.name, "AuraSync recording");
            var files = new List<FileExport.Item> { new FileExport.Item(info.path, name + RecordingStore.Extension, "application/octet-stream") };
            if (info.HasVoice) files.Add(new FileExport.Item(info.VoicePath, name + ".wav", "audio/wav"));
            return FileExport.Share(files, "AuraSync recording: " + info.name, Path.Combine(RecordingStore.Folder, "Export"));
        }
    }

    /// <summary>
    /// Saves files to Download/AuraSync and opens Android's share sheet, with Android's own APIs
    /// through JNI: no plugin and no manifest change. The copy is written through MediaStore
    /// (Android 10 and later need no storage permission for that), and MediaStore's own content
    /// address is what is shared, with read access granted to the app the person picks. On
    /// Android 9 and older, or in the Editor, the files are copied to a folder of the app's and the
    /// message says where.
    /// </summary>
    public static class FileExport
    {
        public readonly struct Item
        {
            public readonly string source, fileName, mime;
            public Item(string source, string fileName, string mime) { this.source = source; this.fileName = fileName; this.mime = mime; }
        }

        public static string SafeName(string name, string fallback)
        {
            string s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Replace(':', '-').Replace('/', '_');
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        /// <summary>Exports the files; returns a message for the person.</summary>
        public static string Share(IList<Item> files, string title, string fallbackDir)
        {
            if (files == null || files.Count == 0) return "Nothing to export.";

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (SdkInt() >= 29)
                {
                    var uris = new List<AndroidJavaObject>();
                    foreach (Item f in files)
                    {
                        AndroidJavaObject uri = SaveToDownloads(f.source, f.fileName, f.mime);
                        if (uri != null) uris.Add(uri);
                    }
                    if (uris.Count > 0)
                    {
                        ShareUris(uris, files[0].mime, title);
                        return files.Count == 1
                            ? $"Saved to Download/AuraSync/{files[0].fileName}."
                            : $"Saved {uris.Count} files to Download/AuraSync.";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Export] MediaStore export failed: {ex.Message}");
            }
#endif
            // Fallback: copies in a folder of the app's, for copying off over USB.
            try
            {
                Directory.CreateDirectory(fallbackDir);
                foreach (Item f in files) File.Copy(f.source, Path.Combine(fallbackDir, f.fileName), true);
                return files.Count == 1 ? $"Saved to {Path.Combine(fallbackDir, files[0].fileName)}" : $"Saved {files.Count} files to {fallbackDir}";
            }
            catch (Exception ex)
            {
                return "Could not export: " + ex.Message;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static int SdkInt()
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                return version.GetStatic<int>("SDK_INT");
        }

        private static AndroidJavaObject Activity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        /// <summary>Writes the file into Download/AuraSync through MediaStore; returns its content address.</summary>
        private static AndroidJavaObject SaveToDownloads(string source, string fileName, string mime)
        {
            AndroidJavaObject activity = Activity();
            AndroidJavaObject resolver = activity.Call<AndroidJavaObject>("getContentResolver");

            var values = new AndroidJavaObject("android.content.ContentValues");
            values.Call("put", "_display_name", fileName);
            values.Call("put", "mime_type", mime);
            values.Call("put", "relative_path", "Download/AuraSync");

            AndroidJavaObject collection;
            using (var downloads = new AndroidJavaClass("android.provider.MediaStore$Downloads"))
                collection = downloads.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

            AndroidJavaObject uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
            if (uri == null) return null;

            byte[] bytes = File.ReadAllBytes(source);
            var signed = new sbyte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, signed, 0, bytes.Length);

            AndroidJavaObject stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
            stream.Call("write", signed);
            stream.Call("flush");
            stream.Call("close");
            return uri;
        }

        private static void ShareUris(List<AndroidJavaObject> uris, string mime, string title)
        {
            AndroidJavaObject activity = Activity();
            AndroidJavaObject intent;
            if (uris.Count == 1)
            {
                intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND");
                intent.Call<AndroidJavaObject>("setType", mime);
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.STREAM", uris[0]);
            }
            else
            {
                intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND_MULTIPLE");
                intent.Call<AndroidJavaObject>("setType", "*/*");
                var list = new AndroidJavaObject("java.util.ArrayList");
                foreach (AndroidJavaObject u in uris) list.Call<bool>("add", u);
                intent.Call<AndroidJavaObject>("putParcelableArrayListExtra", "android.intent.extra.STREAM", list);
            }
            intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.SUBJECT", title);
            intent.Call<AndroidJavaObject>("addFlags", 1); // FLAG_GRANT_READ_URI_PERMISSION

            using (var intentClass = new AndroidJavaClass("android.content.Intent"))
            {
                AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share");
                activity.Call("startActivity", chooser);
            }
        }
#endif
    }
}
