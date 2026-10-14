using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AuraSync.Recording
{
    /// <summary>
    /// The presenter's voice in a session recording (added to Phase 9 on 2026-10-14, at your
    /// request: without it a replay shows what was done but not what was explained).
    ///
    /// **Whose voice:** the recording phone's own, only (your choice). In a room it is the very
    /// stream Photon Voice sends, after echo cancelling and noise suppression, tapped by
    /// <c>VoiceRecordingTap</c> on the voice Recorder: no second microphone, and nothing at all
    /// while the presenter is muted (Photon does not even process a muted microphone). Alone, the
    /// phone's microphone is opened directly (<see cref="SoloMic"/>), so a teacher can record a
    /// narrated walkthrough.
    ///
    /// **Format:** a standard WAV file beside the recording (<c>rec_….wav</c>): 16 kHz mono G.711
    /// µ-law, 8 bits a sample, about 0.94 MB a minute, which any phone's player opens. Byte n is
    /// recording time n / 16000 s: gaps (muted, silent, the mic lent to voice search) are filled
    /// with silence as audio arrives, so the voice never drifts from the 3D timeline.
    /// </summary>
    public static class VoiceTrack
    {
        public const int Rate = 16000;

        private static volatile VoiceTrackWriter _sink;
        /// <summary>Set on the main thread every frame: true while the room's mic is muted. A second guard.</summary>
        public static volatile bool Muted;

        public static VoiceTrackWriter Sink { get => _sink; set => _sink = value; }

        public static string PathFor(string recordingPath) => Path.ChangeExtension(recordingPath, ".wav");

        /// <summary>From Photon Voice's encoder thread (or the main thread): audio to keep, if recording.</summary>
        public static void Feed(float[] buf, int rate, int channels)
        {
            VoiceTrackWriter s = _sink;
            if (s == null || Muted || buf == null || buf.Length == 0) return;
            s.Enqueue(buf, rate, channels);
        }

        public static void Feed(short[] buf, int rate, int channels)
        {
            VoiceTrackWriter s = _sink;
            if (s == null || Muted || buf == null || buf.Length == 0) return;
            var f = new float[buf.Length];
            for (int i = 0; i < buf.Length; i++) f[i] = buf[i] / 32768f;
            s.Enqueue(f, rate, channels);
        }

        // ── G.711 µ-law ────────────────────────────────────────────────────────

        public const byte Silence = 0xFF;

        public static byte Encode(float sample)
        {
            int s = Mathf.Clamp(Mathf.RoundToInt(sample * 32767f), -32767, 32767);
            int sign = 0;
            if (s < 0) { s = -s; sign = 0x80; }
            if (s > 32635) s = 32635;
            s += 0x84;
            int exponent = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (s >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static float Decode(byte mu)
        {
            int u = ~mu & 0xFF;
            int sign = u & 0x80, exponent = (u >> 4) & 0x07, mantissa = u & 0x0F;
            int s = (((mantissa << 3) + 0x84) << exponent) - 0x84;
            return (sign != 0 ? -s : s) / 32768f;
        }

        // ── Reading back ───────────────────────────────────────────────────────

        /// <summary>The µ-law samples of a voice file (null if there is none). Tolerates a file cut short by a crash.</summary>
        public static byte[] ReadSamples(string wavPath)
        {
            try
            {
                if (!File.Exists(wavPath)) return null;
                byte[] all = File.ReadAllBytes(wavPath);
                if (all.Length <= 12 || Encoding.ASCII.GetString(all, 0, 4) != "RIFF") return null;
                int p = 12;
                while (p + 8 <= all.Length)
                {
                    string id = Encoding.ASCII.GetString(all, p, 4);
                    int size = BitConverter.ToInt32(all, p + 4);
                    if (id == "data")
                    {
                        int start = p + 8;
                        int len = all.Length - start; // the size field is only right after a clean close
                        if (len <= 0) return null;
                        var data = new byte[len];
                        Buffer.BlockCopy(all, start, data, 0, len);
                        return data;
                    }
                    p += 8 + Math.Max(0, size) + (size & 1);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Recording] Cannot read the voice track: {ex.Message}"); }
            return null;
        }

        /// <summary>Bytes of sound in a voice file (0 = none worth keeping).</summary>
        public static long SoundBytes(string wavPath)
        {
            try { return File.Exists(wavPath) ? Math.Max(0, new FileInfo(wavPath).Length - VoiceTrackWriter.HeaderBytes) : 0; }
            catch { return 0; }
        }
    }

    /// <summary>Writes the voice track while recording. Enqueue from any thread; Drain and Close on the main thread.</summary>
    public class VoiceTrackWriter
    {
        public const int HeaderBytes = 58;
        private const float PadThreshold = 0.2f; // seconds behind before silence is filled in

        private struct Chunk { public float[] data; public int rate, channels; public double at; }

        private readonly object _lock = new object();
        private List<Chunk> _queue = new List<Chunk>();
        private List<Chunk> _spare = new List<Chunk>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private FileStream _fs;
        private long _written;           // samples in the file
        private readonly List<float> _pending = new List<float>(); // mono, not yet resampled
        private int _pendingRate;
        private double _srcPos;          // resampler position in _pending

        public string Path { get; }
        public long Samples => _written;

        public VoiceTrackWriter(string path)
        {
            Path = path;
            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            WriteHeader(0);
        }

        public void Enqueue(float[] buf, int rate, int channels)
        {
            if (rate <= 0) return;
            var copy = new float[buf.Length];
            Array.Copy(buf, copy, buf.Length);
            double at = _clock.Elapsed.TotalSeconds;
            lock (_lock) _queue.Add(new Chunk { data = copy, rate = rate, channels = Math.Max(1, channels), at = at });
        }

        /// <summary>Resamples, encodes and writes what has arrived.</summary>
        public void Drain()
        {
            if (_fs == null) return;
            List<Chunk> work;
            lock (_lock) { work = _queue; _queue = _spare; }
            foreach (Chunk c in work) Write(c);
            work.Clear();
            _spare = work;
        }

        private void Write(Chunk c)
        {
            if (c.rate != _pendingRate) { _pending.Clear(); _srcPos = 0; _pendingRate = c.rate; }
            for (int i = 0; i < c.data.Length; i += c.channels) _pending.Add(c.data[i]); // first channel

            // Down to 16 kHz, averaging each output sample's span (a cheap low-pass).
            double step = (double)c.rate / VoiceTrack.Rate;
            var outBuf = new List<byte>((int)(_pending.Count / step) + 2);
            while (_srcPos + step <= _pending.Count)
            {
                int a = (int)_srcPos, b = Math.Max(a + 1, (int)(_srcPos + step));
                float sum = 0f;
                for (int k = a; k < b; k++) sum += _pending[k];
                outBuf.Add(VoiceTrack.Encode(sum / (b - a)));
                _srcPos += step;
            }
            int used = (int)_srcPos;
            if (used > 0) { _pending.RemoveRange(0, used); _srcPos -= used; }
            if (outBuf.Count == 0) return;

            // Keep it on the recording's timeline: this audio ended at c.at.
            long start = (long)(c.at * VoiceTrack.Rate) - outBuf.Count;
            if (start - _written > PadThreshold * VoiceTrack.Rate) Pad(start - _written);

            byte[] bytes = outBuf.ToArray();
            _fs.Write(bytes, 0, bytes.Length);
            _written += bytes.Length;
        }

        private void Pad(long samples)
        {
            var silence = new byte[4096];
            for (int i = 0; i < silence.Length; i++) silence[i] = VoiceTrack.Silence;
            while (samples > 0)
            {
                int n = (int)Math.Min(samples, silence.Length);
                _fs.Write(silence, 0, n);
                _written += n;
                samples -= n;
            }
        }

        public void Flush() => _fs?.Flush();

        /// <summary>Writes what is left, fixes the header's sizes, closes. Returns false if nothing was heard.</summary>
        public bool Close()
        {
            if (_fs == null) return false;
            Drain();
            bool any = _written > 0;
            try
            {
                _fs.Seek(0, SeekOrigin.Begin);
                WriteHeader(_written);
                _fs.Dispose();
            }
            catch (Exception ex) { Debug.LogWarning($"[Recording] Voice track close: {ex.Message}"); }
            _fs = null;
            if (!any) { try { File.Delete(Path); } catch { /* left empty */ } }
            return any;
        }

        /// <summary>WAV header for 16 kHz mono 8-bit µ-law (format 7), with the fact chunk non-PCM formats carry.</summary>
        private void WriteHeader(long dataBytes)
        {
            var w = new BinaryWriter(_fs, Encoding.ASCII, leaveOpen: true);
            int data = (int)Math.Min(dataBytes, int.MaxValue - HeaderBytes);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(HeaderBytes - 8 + data); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(18);
            w.Write((short)7); w.Write((short)1); w.Write(VoiceTrack.Rate); w.Write(VoiceTrack.Rate);
            w.Write((short)1); w.Write((short)8); w.Write((short)0);
            w.Write(Encoding.ASCII.GetBytes("fact")); w.Write(4); w.Write(data);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
            w.Flush();
        }
    }

    /// <summary>
    /// The phone's microphone for a recording made alone (no room, so Photon Voice is not running).
    /// Steps aside while voice search uses the microphone, and comes back after it.
    /// </summary>
    public class SoloMic
    {
        private string _device;
        private AudioClip _clip;
        private int _lastPos;
        private float _retryAt;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool _asked;
#endif

        public bool Live => _clip != null;

        public void Tick(VoiceTrackWriter writer)
        {
            bool searchHasMic = AuraSync.AI.SpeechInput.Instance != null && AuraSync.AI.SpeechInput.Instance.IsRecording;
            if (searchHasMic) { _clip = null; return; } // voice search ended ours and started its own

            if (_clip == null || string.IsNullOrEmpty(_device) || !Microphone.IsRecording(_device))
            {
                _clip = null;
                if (Time.unscaledTime >= _retryAt) { _retryAt = Time.unscaledTime + 1f; Open(); }
                return;
            }

            int pos = Microphone.GetPosition(_device);
            int len = _clip.samples;
            int count = (pos - _lastPos + len) % len;
            if (count <= 0) return;
            var buf = new float[count * _clip.channels];
            _clip.GetData(buf, _lastPos); // wraps round the looping clip
            _lastPos = pos;
            writer?.Enqueue(buf, _clip.frequency, _clip.channels);
        }

        private void Open()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                if (!_asked) { _asked = true; UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); }
                return;
            }
#endif
            if (Microphone.devices.Length == 0) return;
            _device = Microphone.devices[0];
            if (Microphone.IsRecording(_device)) return; // someone else has it; try again shortly
            _clip = Microphone.Start(_device, true, 2, VoiceTrack.Rate);
            _lastPos = 0;
        }

        public void Stop()
        {
            if (_clip != null && !string.IsNullOrEmpty(_device) && Microphone.IsRecording(_device)) Microphone.End(_device);
            _clip = null;
        }
    }
}
