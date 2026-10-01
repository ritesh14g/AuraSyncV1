using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

namespace AuraSync.Networking
{
    /// <summary>
    /// Phone-call-quality 2-way voice chat engine.
    ///
    /// Three root causes of the reverb/repeat/delay bugs — all fixed here:
    ///
    /// 1. ECHO / REVERB
    ///    Cause:  Android plays audio through speaker; mic picks it up and sends it back.
    ///    Fix:    Set AudioManager.MODE_IN_COMMUNICATION at startup.
    ///            This enables Android's hardware Acoustic Echo Cancellation (AEC),
    ///            Noise Suppression (NS), and Automatic Gain Control (AGC) — exactly
    ///            what WhatsApp / Google Meet use.
    ///
    /// 2. SAME WORDS REPEATING
    ///    Cause:  3-second ring buffer + burst network delivery = engine plays old audio
    ///            alongside new audio.
    ///    Fix:    150ms ring buffer max. Any overflow drops OLDEST samples immediately so
    ///            latency never builds up.
    ///
    /// 3. DELAY
    ///    Cause:  40ms chunks + 3-second buffer = potentially 3+ seconds of latency.
    ///    Fix:    20ms chunks + 150ms jitter buffer = ~120ms total (phone call grade).
    ///
    /// Audio injection uses Unity's OnAudioFilterRead on a per-player MonoBehaviour.
    /// The audio engine calls it directly from its hardware thread — no timing mismatch,
    /// no GC, no play/pause cycles.
    /// </summary>
    public class VoiceChatManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static VoiceChatManager Instance { get; private set; }

        public event Action<bool> OnMicMuteStateChanged;

        public const byte EventCodeVoice = 2;

        [Header("Voice Settings")]
        [SerializeField] private int targetSampleRate = 16000;

        [Tooltip("20ms = ~25 packets/s. Phone-call latency.")]
        [SerializeField] private float chunkDuration = 0.020f;

        private bool   _isMuted = false;
        public  bool   IsMuted  => _isMuted;

        private AudioClip _micClip;
        private string    _selectedDevice;
        private int       _actualSampleRate = 16000;
        private int       _lastSamplePos    = 0;
        private bool      _isRecording      = false;
        private bool      _permissionGranted = false;

        private readonly Dictionary<int, RemoteVoiceReceiver> _receivers
            = new Dictionary<int, RemoteVoiceReceiver>();

        // ── Lifecycle ───────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public override void OnEnable()  { base.OnEnable();  PhotonNetwork.AddCallbackTarget(this); }
        public override void OnDisable() { base.OnDisable(); PhotonNetwork.RemoveCallbackTarget(this); }

        void Start()
        {
            EnableAndroidAEC();
            StartCoroutine(RequestMicAndInit());
        }

        // ── Android AEC ─────────────────────────────────────────────────────────

        private void EnableAndroidAEC()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player  = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var audioMgr = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                {
                    // MODE_IN_COMMUNICATION = 3 → enables hardware AEC/NS/AGC
                    audioMgr.Call("setMode", 3);
                    // Keep audio on SPEAKER (not earpiece) so users don't need to hold phone to ear
                    audioMgr.Call("setSpeakerphoneOn", true);
                    Debug.Log("[AuraSync Voice] Android MODE_IN_COMMUNICATION + speakerphone set — hardware AEC active.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AuraSync Voice] AEC setup failed: {e.Message}");
            }
#endif
        }

        // Reset audio mode when leaving (so other apps aren't affected)
        private void DisableAndroidAEC()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var audioMgr = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                {
                    audioMgr.Call("setMode", 0); // MODE_NORMAL
                }
            }
            catch { /* best effort */ }
#endif
        }

        // ── Permission & Mic Init ───────────────────────────────────────────────

        private IEnumerator RequestMicAndInit()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);

                float waited = 0f;
                while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                           UnityEngine.Android.Permission.Microphone) && waited < 12f)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }
            _permissionGranted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                                     UnityEngine.Android.Permission.Microphone);
#else
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            _permissionGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif

            if (!_permissionGranted)
            {
                Debug.LogWarning("[AuraSync Voice] Microphone permission denied.");
                yield break;
            }

            yield return new WaitForSeconds(0.2f);
            InitializeMicrophone();
        }

        public void InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                StartCoroutine(RetryInit());
                return;
            }
            _selectedDevice = Microphone.devices[0];
            Debug.Log($"[AuraSync Voice] Mic device: {_selectedDevice}");
            StartMicrophone();
        }

        private IEnumerator RetryInit()
        {
            yield return new WaitForSeconds(1f);
            if (Microphone.devices.Length > 0)
            {
                _selectedDevice = Microphone.devices[0];
                StartMicrophone();
            }
        }

        private void StartMicrophone()
        {
            if (string.IsNullOrEmpty(_selectedDevice)) return;
            if (Microphone.IsRecording(_selectedDevice))
                Microphone.End(_selectedDevice);

            _micClip = Microphone.Start(_selectedDevice, true, 10, targetSampleRate);
            if (_micClip == null)
            {
                Debug.LogError("[AuraSync Voice] Microphone.Start returned null!");
                return;
            }

            // Use Unity's actual hardware frequency (may differ from targetSampleRate)
            _actualSampleRate = _micClip.frequency;
            _lastSamplePos    = 0;
            _isRecording      = true;
            Debug.Log($"[AuraSync Voice] Mic started — actual rate: {_actualSampleRate}Hz");
        }

        // ── Capture & Send ──────────────────────────────────────────────────────

        void Update()
        {
            if (_permissionGranted && PhotonNetwork.InRoom && !_isMuted
                && !_isRecording && !string.IsNullOrEmpty(_selectedDevice))
            {
                StartMicrophone();
            }

            if (!_isRecording || _isMuted || !PhotonNetwork.InRoom || _micClip == null) return;

            int micPos = Microphone.GetPosition(_selectedDevice);
            if (micPos < 0) { StartMicrophone(); return; }

            int chunkSize = Mathf.FloorToInt(_actualSampleRate * chunkDuration);
            if (chunkSize <= 0) return;

            int clipLen   = _micClip.samples;
            int available = (micPos >= _lastSamplePos)
                                ? micPos - _lastSamplePos
                                : clipLen - _lastSamplePos + micPos;

            while (available >= chunkSize)
            {
                float[] pcm = ReadMicChunk(chunkSize, clipLen);
                _lastSamplePos = (_lastSamplePos + chunkSize) % clipLen;
                available     -= chunkSize;

                byte[] payload = EncodeFloat32ToPCM16(pcm);

                object[] packet = new object[]
                {
                    PhotonNetwork.LocalPlayer.ActorNumber,
                    _actualSampleRate,
                    payload
                };

                PhotonNetwork.RaiseEvent(
                    EventCodeVoice,
                    packet,
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                    SendOptions.SendUnreliable);
            }
        }

        private float[] ReadMicChunk(int chunkSize, int clipLen)
        {
            float[] buf  = new float[chunkSize];
            int     end  = _lastSamplePos + chunkSize;

            if (end <= clipLen)
            {
                _micClip.GetData(buf, _lastSamplePos);
            }
            else
            {
                int part1 = clipLen - _lastSamplePos;
                int part2 = chunkSize - part1;
                float[] a = new float[part1], b = new float[part2];
                _micClip.GetData(a, _lastSamplePos);
                _micClip.GetData(b, 0);
                Array.Copy(a, 0, buf, 0,     part1);
                Array.Copy(b, 0, buf, part1, part2);
            }
            return buf;
        }

        // ── Receive ─────────────────────────────────────────────────────────────

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != EventCodeVoice) return;

            object[] packet = photonEvent.CustomData as object[];
            if (packet == null || packet.Length < 3) return;

            int    senderActor = (int)packet[0];
            int    sampleRate  = (int)packet[1];
            byte[] pcmBytes    = packet[2] as byte[];

            if (pcmBytes == null || pcmBytes.Length == 0) return;
            if (senderActor == PhotonNetwork.LocalPlayer.ActorNumber) return;

            float[] samples = DecodePCM16ToFloat32(pcmBytes);

            // Create receiver for this player on first packet
            if (!_receivers.TryGetValue(senderActor, out RemoteVoiceReceiver rcv) || rcv == null)
            {
                var go = new GameObject($"VoiceReceiver_{senderActor}");
                go.transform.SetParent(transform, false);
                // *** CRITICAL: Add AudioSource BEFORE RemoteVoiceReceiver.
                // [RequireComponent] is editor-only — at runtime AddComponent does NOT
                // auto-add dependencies, so GetComponent<AudioSource>() would return null.
                go.AddComponent<AudioSource>();
                rcv = go.AddComponent<RemoteVoiceReceiver>();
                rcv.Initialize(senderActor, sampleRate);
                _receivers[senderActor] = rcv;
                Debug.Log($"[AuraSync Voice] Created receiver for actor {senderActor} @ {sampleRate}Hz");
            }

            rcv.Feed(samples, sampleRate);
        }

        // ── PCM16 Encode / Decode ───────────────────────────────────────────────

        private byte[] EncodeFloat32ToPCM16(float[] floats)
        {
            byte[] bytes = new byte[floats.Length * 2];
            for (int i = 0; i < floats.Length; i++)
            {
                short s = (short)(Mathf.Clamp(floats[i], -1f, 1f) * 32767f);
                bytes[i * 2]     = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        private float[] DecodePCM16ToFloat32(byte[] bytes)
        {
            int count   = bytes.Length / 2;
            float[] out_ = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s  = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
                out_[i]  = s / 32768f;
            }
            return out_;
        }

        // ── Controls ────────────────────────────────────────────────────────────

        public void ToggleMute() => SetMute(!_isMuted);

        public void SetMute(bool mute)
        {
            _isMuted = mute;
            if (mute)
            {
                if (!string.IsNullOrEmpty(_selectedDevice) && Microphone.IsRecording(_selectedDevice))
                    Microphone.End(_selectedDevice);
                _isRecording = false;
            }
            else if (_permissionGranted)
            {
                StartMicrophone();
            }
            Debug.Log($"[AuraSync Voice] Muted = {_isMuted}");
            OnMicMuteStateChanged?.Invoke(_isMuted);
        }

        // ── Photon Callbacks ────────────────────────────────────────────────────

        public override void OnLeftRoom()
        {
            DisableAndroidAEC();
            CleanAllReceivers();
        }

        public override void OnPlayerLeftRoom(Player other)
        {
            if (_receivers.TryGetValue(other.ActorNumber, out RemoteVoiceReceiver rcv))
            {
                if (rcv != null) Destroy(rcv.gameObject);
                _receivers.Remove(other.ActorNumber);
            }
        }

        private void CleanAllReceivers()
        {
            foreach (var kv in _receivers)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _receivers.Clear();
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  RemoteVoiceReceiver — one per connected remote player.
    //
    //  Uses OnAudioFilterRead for audio injection:
    //   • Unity's audio hardware thread calls it every DSP buffer (~10ms).
    //   • We pull from our ring buffer; output silence when buffer is empty.
    //   • 150ms max buffer. Overflow drops OLDEST samples → latency stays ≤150ms.
    //   • No AudioClip.SetData, no Play/Pause, no GC pressure.
    // ────────────────────────────────────────────────────────────────────────────
    // Note: AudioSource is added explicitly in VoiceChatManager.OnEvent before this component.
    public class RemoteVoiceReceiver : MonoBehaviour
    {
        public int ActorNumber { get; private set; }

        private float[] _ring;
        private int     _readHead  = 0;
        private int     _writeHead = 0;
        private int     _filled    = 0;
        private readonly object _lock = new object();

        private int _sampleRate;       // Sender's mic sample rate
        private int _outputSampleRate; // Local hardware output rate (what OnAudioFilterRead uses)
        private const int MAX_BUFFER_MS = 150;

        public void Initialize(int actorNumber, int sampleRate)
        {
            ActorNumber  = actorNumber;
            _sampleRate  = sampleRate > 0 ? sampleRate : 16000;

            // CRITICAL: Ring buffer MUST be sized at the hardware OUTPUT rate,
            // not the sender's mic rate. OnAudioFilterRead is always called at
            // AudioSettings.outputSampleRate (e.g. 44100 or 48000 Hz on Android).
            // Samples stored here must be at that rate or playback speed is wrong.
            _outputSampleRate = AudioSettings.outputSampleRate;
            int ringSize      = _outputSampleRate * MAX_BUFFER_MS / 1000;
            _ring             = new float[ringSize];

            Debug.Log($"[AuraSync Voice] Receiver for actor {actorNumber}: " +
                      $"sender rate={_sampleRate}Hz, local output rate={_outputSampleRate}Hz");

            // AudioSource was added explicitly before this component by VoiceChatManager.
            var src          = GetComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.volume       = 1f;
            src.loop         = true;
            src.playOnAwake  = false;
            src.mute         = false;

            // Dummy clip at outputSampleRate so AudioSource plays at the correct rate.
            // OnAudioFilterRead fires at outputSampleRate — must match.
            int dummyLen = Mathf.Max(4096, _outputSampleRate / 4);
            var dummy    = AudioClip.Create("dummy", dummyLen, 1, _outputSampleRate, false);
            src.clip = dummy;
            src.Play();
            Debug.Log($"[AuraSync Voice] RemoteVoiceReceiver READY for actor {actorNumber}.");
        }

        // ── Feed: called from main thread when network audio arrives ────────────
        // sourceSampleRate = sender's mic Hz. Resamples to _outputSampleRate before
        // storing so OnAudioFilterRead plays at exactly the hardware output speed.

        public void Feed(float[] samples, int sourceSampleRate)
        {
            if (samples == null || samples.Length == 0) return;

            // Resample from sender's rate to local output rate if they differ
            float[] toStore = (sourceSampleRate == _outputSampleRate || sourceSampleRate <= 0)
                              ? samples
                              : LinearResample(samples, sourceSampleRate, _outputSampleRate);

            lock (_lock)
            {
                int ringLen  = _ring.Length;
                int overflow = _filled + toStore.Length - ringLen;
                if (overflow > 0)
                {
                    _readHead = (_readHead + overflow) % ringLen;
                    _filled   = Mathf.Max(0, _filled - overflow);
                }
                for (int i = 0; i < toStore.Length; i++)
                {
                    _ring[_writeHead] = toStore[i];
                    _writeHead = (_writeHead + 1) % ringLen;
                }
                _filled += toStore.Length;
            }
        }

        // Linear interpolation resampler — cheap, no allocations per sample.
        // Handles both upsample (16k→48k) and downsample (48k→16k).
        private float[] LinearResample(float[] input, int fromRate, int toRate)
        {
            if (fromRate == toRate || input.Length == 0) return input;

            int    outLen = Mathf.RoundToInt((float)input.Length * toRate / fromRate);
            float[] output = new float[outLen];
            float  ratio  = (float)(input.Length - 1) / Mathf.Max(outLen - 1, 1);

            for (int i = 0; i < outLen; i++)
            {
                float srcF  = i * ratio;
                int   srcI  = (int)srcF;
                float frac  = srcF - srcI;
                float a     = input[srcI];
                float b     = srcI + 1 < input.Length ? input[srcI + 1] : a;
                output[i]   = a + frac * (b - a);
            }
            return output;
        }

        // ── OnAudioFilterRead: called from Unity audio hardware thread ───────────
        // Replaces dummy silence with real voice samples.
        // Outputs silence when ring buffer is empty (clean pauses, no crackle).

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                int frames = data.Length / channels;
                for (int i = 0; i < frames; i++)
                {
                    float s = 0f;
                    if (_filled > 0)
                    {
                        s         = _ring[_readHead];
                        _readHead = (_readHead + 1) % _ring.Length;
                        _filled--;
                    }
                    // Write mono voice sample to all output channels
                    for (int c = 0; c < channels; c++)
                        data[i * channels + c] = s;
                }
            }
        }
    }
}
