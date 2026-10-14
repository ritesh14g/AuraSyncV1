using System;
using UnityEngine;
using AuraSync.Networking;

namespace AuraSync.AI
{
    /// <summary>
    /// Records a short spoken query from the microphone and hands it back as a WAV file, ready for
    /// the AI server's /transcribe (Groq Whisper, decision D3).
    ///
    /// * Up to <see cref="MaxSeconds"/> seconds. It stops by itself about a second after you stop
    ///   talking, or when you tap the mic again.
    /// * Recorded at 16 kHz mono (what speech recognition uses); resampled if the device gives
    ///   another rate. Five seconds is about 160 KB.
    /// * Asks for the microphone permission on Android if the app does not have it yet.
    /// * Unity can only record one clip per microphone, so room voice chat's capture is paused for
    ///   the few seconds of recording and given back afterwards.
    /// * Silence is never sent: speech recognisers invent words ("Thank you.") for silent clips.
    /// </summary>
    public class SpeechInput : MonoBehaviour
    {
        public static SpeechInput Instance { get; private set; }

        public const float MaxSeconds = 5f;
        private const int SampleRate = 16000;
        private const float SpeechLevel = 0.04f;    // loud enough to count as talking
        private const float QuietLevel = 0.015f;    // quiet enough to count as having stopped
        private const float StopAfterQuiet = 1.1f;  // seconds of quiet after talking that end it

        public bool IsRecording { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>Live loudness 0..1, for a level meter.</summary>
        public float Level { get; private set; }

        private string _device;
        private AudioClip _clip;
        private bool _voiceChatPaused;
        private bool _heardSpeech;
        private float _quietFor;
        private float _peak;
        private Action<byte[], string> _done;

        public static SpeechInput Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AuraSync_SpeechInput");
            DontDestroyOnLoad(go);
            return go.AddComponent<SpeechInput>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (IsRecording) Finish(cancelled: true);
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Starts listening. <paramref name="done"/> gets the WAV bytes, or null and a message a
        /// student can act on ("Didn't hear anything").
        /// </summary>
        public void StartRecording(Action<byte[], string> done)
        {
            if (IsRecording) return;
            _done = done;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => BeginCapture();
                callbacks.PermissionDenied += _ => Fail("Microphone permission is off. Allow it in the phone's Settings, or type instead.");
                callbacks.PermissionDeniedAndDontAskAgain += _ => Fail("Microphone permission is off. Allow it in the phone's Settings, or type instead.");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
                return;
            }
#endif
            BeginCapture();
        }

        /// <summary>Stops now and processes what was said so far.</summary>
        public void StopRecording()
        {
            if (IsRecording) Finish(cancelled: false);
        }

        /// <summary>Stops and throws the recording away; the callback is not called.</summary>
        public void Cancel()
        {
            if (!IsRecording) return;
            _done = null;
            Finish(cancelled: true);
        }

        private void BeginCapture()
        {
            if (Microphone.devices.Length == 0)
            {
                Fail("No microphone found on this device. Type instead.");
                return;
            }
            _device = Microphone.devices[0];

            // Borrow the microphone from room voice chat, if it is using it.
            _voiceChatPaused = VoiceChatManager.Instance != null && VoiceChatManager.Instance.PauseCaptureForOtherUse();
            if (Microphone.IsRecording(_device)) Microphone.End(_device);

            // One second longer than the limit, so the limit is reached before the clip ends.
            _clip = Microphone.Start(_device, false, Mathf.CeilToInt(MaxSeconds) + 1, SampleRate);
            if (_clip == null)
            {
                GiveMicBack();
                Fail("The microphone could not be started. Type instead.");
                return;
            }

            IsRecording = true;
            Elapsed = 0f;
            Level = 0f;
            _peak = 0f;
            _heardSpeech = false;
            _quietFor = 0f;
        }

        private readonly float[] _window = new float[512];

        void Update()
        {
            if (!IsRecording) return;

            Elapsed += Time.unscaledDeltaTime;
            int pos = Microphone.GetPosition(_device);
            if (pos > _window.Length && _clip != null)
            {
                _clip.GetData(_window, pos - _window.Length);
                float sum = 0f;
                foreach (float s in _window) sum += s * s;
                Level = Mathf.Sqrt(sum / _window.Length);
                _peak = Mathf.Max(_peak, Level);
            }

            if (Level >= SpeechLevel) { _heardSpeech = true; _quietFor = 0f; }
            else if (_heardSpeech && Level <= QuietLevel) _quietFor += Time.unscaledDeltaTime;

            if (Elapsed >= MaxSeconds || (_heardSpeech && _quietFor >= StopAfterQuiet))
                Finish(cancelled: false);
        }

        private void Finish(bool cancelled)
        {
            int length = _clip != null ? Microphone.GetPosition(_device) : 0;
            if (Microphone.IsRecording(_device)) Microphone.End(_device);
            IsRecording = false;
            GiveMicBack();

            AudioClip clip = _clip;
            _clip = null;
            Action<byte[], string> done = _done;
            _done = null;
            if (cancelled || done == null) return;

            if (clip == null || length <= 0)
            {
                done(null, "Nothing was recorded. Try again.");
                return;
            }
            if (!_heardSpeech && _peak < SpeechLevel)
            {
                done(null, "Didn't hear anything. Hold the phone closer and try again, or type.");
                return;
            }

            var samples = new float[Mathf.Min(length, clip.samples) * clip.channels];
            clip.GetData(samples, 0);
            if (clip.channels > 1) samples = ToMono(samples, clip.channels);
            if (clip.frequency != SampleRate) samples = Resample(samples, clip.frequency, SampleRate);
            done(EncodeWav(samples, SampleRate), null);
        }

        private void GiveMicBack()
        {
            if (!_voiceChatPaused) return;
            _voiceChatPaused = false;
            VoiceChatManager.Instance?.ResumeCaptureAfterOtherUse();
        }

        private void Fail(string message)
        {
            Action<byte[], string> done = _done;
            _done = null;
            done?.Invoke(null, message);
        }

        // ── Audio helpers ──────────────────────────────────────────────────────

        private static float[] ToMono(float[] interleaved, int channels)
        {
            var mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        private static float[] Resample(float[] input, int from, int to)
        {
            if (input.Length == 0 || from == to) return input;
            int n = (int)((long)input.Length * to / from);
            var output = new float[n];
            double step = (double)from / to;
            for (int i = 0; i < n; i++)
            {
                double src = i * step;
                int a = (int)src;
                int b = Math.Min(a + 1, input.Length - 1);
                float t = (float)(src - a);
                output[i] = input[a] * (1f - t) + input[b] * t;
            }
            return output;
        }

        /// <summary>16-bit PCM mono WAV.</summary>
        public static byte[] EncodeWav(float[] samples, int sampleRate)
        {
            int dataBytes = samples.Length * 2;
            var wav = new byte[44 + dataBytes];

            void Str(int at, string s) { for (int i = 0; i < s.Length; i++) wav[at + i] = (byte)s[i]; }
            void I32(int at, int v) { wav[at] = (byte)v; wav[at + 1] = (byte)(v >> 8); wav[at + 2] = (byte)(v >> 16); wav[at + 3] = (byte)(v >> 24); }
            void I16(int at, int v) { wav[at] = (byte)v; wav[at + 1] = (byte)(v >> 8); }

            Str(0, "RIFF"); I32(4, 36 + dataBytes); Str(8, "WAVE");
            Str(12, "fmt "); I32(16, 16); I16(20, 1); I16(22, 1);
            I32(24, sampleRate); I32(28, sampleRate * 2); I16(32, 2); I16(34, 16);
            Str(36, "data"); I32(40, dataBytes);

            for (int i = 0; i < samples.Length; i++)
            {
                short v = (short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * 32767f), short.MinValue, short.MaxValue);
                wav[44 + i * 2] = (byte)v;
                wav[45 + i * 2] = (byte)(v >> 8);
            }
            return wav;
        }
    }
}
