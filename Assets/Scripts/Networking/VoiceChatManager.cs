using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Networking
{
    /// <summary>
    /// Room voice chat. Two backends behind one API (mute, speaking state, mic availability):
    ///
    /// 1. Photon Voice 2 (used whenever the package is installed and App Id Voice is set)
    ///    The same stack as a WhatsApp-style call: Opus at 48 kHz, Android's call-mode microphone
    ///    with hardware echo cancellation / noise suppression / gain control, WebRTC processing
    ///    for anything the phone lacks, a double-talk-safe send gate (VoiceTalkGate), forward error
    ///    correction and an adaptive playback buffer. Built entirely in code (no prefab); see
    ///    BuildPhotonVoiceClient() and AttachPhotonRecorder().
    ///    The package defines PHOTON_VOICE_R4 itself, so no scripting define has to be added.
    ///
    /// 2. Built-in fallback (no package, or App Id Voice left empty)
    ///    Unity Microphone -> 16 kHz -> adaptive silence gate -> G.711 mu-law -> Photon events
    ///    (20 ms packets, flushed immediately) -> jitter buffer -> OnAudioFilterRead.
    ///    Usable, but Unity's Android microphone adds latency and there is no proper echo
    ///    cancellation, so it cannot match a real VoIP stack.
    ///
    /// Either way the microphone and the permission prompt only start once the player is in a
    /// room. Mute state is a player property ("Muted"); speaking state is shared so every HUD can
    /// show who is talking. A diagnostics line is logged every 5 s while in a room.
    /// </summary>
    public class VoiceChatManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        public static VoiceChatManager Instance { get; private set; }

        public event Action<bool> OnMicMuteStateChanged;
        public event Action<int, bool> OnSpeakingChanged; // (actor number, speaking)

        public const byte   EventCodeVoice    = EventCodes.Voice;
        public const byte   EventCodeSpeaking = EventCodes.Speaking;
        public const string PropMuted         = "Muted";

        [Header("Photon Voice 2")]
        [Tooltip("Opus bitrate. WhatsApp sends roughly 16-24 kbps; 32 kbps at 48 kHz is clearer still " +
                 "and only ~4 KB/s while speaking (nothing while silent).")]
        [SerializeField] private int opusBitrate = 32000;
        [Tooltip("Capture through Photon's native Android call-mode microphone (hardware echo cancellation) " +
                 "instead of Unity's microphone. OFF: on 2026-10-11 it made received voices inaudible - it " +
                 "takes Android audio focus for a voice call away from Unity and moves the phone's audio " +
                 "route (often to the earpiece). WebRTC's software echo cancellation is used instead.")]
        [SerializeField] private bool useAndroidCallModeMic = false;

        [Header("Built-in Voice")]
        [Tooltip("Mic sample rate to request. The device may pick another; it is resampled.")]
        [SerializeField] private int micSampleRate = 16000;
        [Tooltip("Rate sent over the network: 16000 = wideband (clearer), 8000 = narrowband (half the data).")]
        [SerializeField] private int networkSampleRate = 16000;
        [Tooltip("20 ms of audio per packet.")]
        [SerializeField] private float chunkDuration = 0.020f;
        [Tooltip("Switch Android into call mode. Gives hardware echo cancellation, but on some " +
                 "phones it reroutes capture and the microphone then records pure silence, which " +
                 "is a common cause of one-way audio. Off by default; the watchdog also turns it " +
                 "off automatically if the mic goes silent.")]
        [SerializeField] private bool useAndroidCommunicationMode = false;

        [Header("Silence Gate (built-in voice)")]
        [Tooltip("Never treat audio louder than this (RMS 0..1) as silence.")]
        [SerializeField] private float minSilenceThreshold = 0.002f;
        [Tooltip("Speech must be this many times louder than the measured background noise.")]
        [SerializeField] private float noiseFloorMultiplier = 2.0f;
        [Tooltip("Keep sending this long after speech stops, so word endings are not clipped.")]
        [SerializeField] private float hangoverSeconds = 0.4f;
        [Tooltip("Send all audio while unmuted (no silence gate). Uses more data.")]
        [SerializeField] private bool alwaysTransmit = false;

        [Header("Diagnostics")]
        [SerializeField] private bool logDiagnostics = true;

        private bool _isMuted = false;
        public  bool IsMuted  => _isMuted;

        /// <summary>False when the microphone permission was denied or no device exists.</summary>
        public bool IsMicAvailable { get; private set; } = true;

        /// <summary>True when the Photon Voice 2 backend is active.</summary>
        public bool UsingPhotonVoice { get; private set; } = false;

        // Built-in capture state
        private AudioClip _micClip;
        private string    _selectedDevice;
        private int       _actualSampleRate = 16000;
        private int       _lastSamplePos    = 0;
        private bool      _isRecording      = false;
        private bool      _permissionGranted = false;
        private bool      _sessionActive    = false;
        private float     _voiceUntil       = 0f;
        private float     _noiseFloor       = 0.01f;

        private float[] _readBuf = new float[0];
        private float[] _wrapA   = new float[0];
        private float[] _wrapB   = new float[0];
        private float[] _netBuf  = new float[0];

        private readonly Dictionary<int, RemoteVoiceReceiver> _receivers = new Dictionary<int, RemoteVoiceReceiver>();
        private readonly Dictionary<int, bool> _speaking       = new Dictionary<int, bool>(); // built-in, derived
        private readonly Dictionary<int, bool> _remoteSpeaking = new Dictionary<int, bool>(); // reported by senders
        private float _speakingPollTimer = 0f;

        // Diagnostics
        private float _diagTimer;
        private int   _diagSent;
        private float _diagRmsSum;
        private int   _diagRmsCount;
        private float _diagPeak;
        // Longest frame in the window. Photon Voice reads the mic and sends from the main thread, so
        // a long frame on the SENDER holds its voice back and releases it in a burst: on the other
        // phone that is heard as lag or break-up even on a perfect network.
        private float _diagHitch;
        private readonly Dictionary<int, int> _diagReceived = new Dictionary<int, int>();

        // Microphone watchdog: some Android devices hand Unity a microphone that runs but only
        // ever produces digital silence (call mode, a busy capture session, or an unsupported
        // sample rate). That looks identical to "nobody is talking", so it is detected and
        // recovered from here instead of being reported as working.
        private float _micStartedAt       = -999f;
        private float _lastNonSilentAt    = -999f;
        private int   _lastMicPos         = -1;
        private float _lastMicPosChangeAt = -999f;
        private int   _recoveryStage      = 0;
        private float _watchdogTimer      = 0f;
        private bool  _gateForcedOpen     = false;
        private float _gateWatchTimer     = 0f;
        private float _gateWatchPeak      = 0f;
        private int   _gateWatchSent      = 0;

        /// <summary>Live microphone level (RMS 0..1) — for the on-screen voice panel.</summary>
        public float MicLevel { get; private set; }
        /// <summary>The level speech must exceed right now to be transmitted.</summary>
        public float GateThreshold { get; private set; }
        /// <summary>Voice packets this device has sent since joining the room.</summary>
        public int PacketsSentTotal { get; private set; }
        /// <summary>Voice packets this device has received since joining the room.</summary>
        public int PacketsReceivedTotal { get; private set; }
        /// <summary>Short human-readable state for the on-screen voice panel.</summary>
        public string VoiceStatus { get; private set; } = "not in a room";

#if PHOTON_VOICE_R4
        private GameObject _pvRoot;
        private AuraSyncVoiceClient _pvClient;
        private Photon.Voice.Unity.Recorder _pvRecorder;
        private Photon.Voice.Unity.WebRtcAudioDsp _pvDsp;
        private VoiceTalkGate _pvGate;
        private string _pvDuplex = "-"; // last 5 s of double-talk, for the on-screen line
        private bool  _pvSpeaking;
        private float _pvLastVoiceAt  = -999f;
        private float _pvRejoinTimer;
        private float _pvRejoinDelay  = 5f;
        private bool  _hwAec, _hwAgc, _hwNs; // what this phone's own call-mode microphone provides
#endif

        // ── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>
        /// Low-latency audio output, set once at launch before anything plays (a reset stops all
        /// sounds). The project's mixer buffer was 1024 samples ("best performance"); Unity stacks
        /// several of those before the phone's own output path, which adds up to well over 100 ms on
        /// every voice heard. 512 halves that while staying safe for heavier 3D scenes. 48 kHz output
        /// matches Opus and the echo canceller; one phone was found running at 24 kHz, so every voice
        /// and the echo reference were being resampled (2026-10-16).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureLowLatencyAudio()
        {
            AudioConfiguration cfg = AudioSettings.GetConfiguration();
            int oldBuffer = cfg.dspBufferSize, oldRate = cfg.sampleRate;
            bool change = false;
            if (cfg.dspBufferSize == 0 || cfg.dspBufferSize > 512) { cfg.dspBufferSize = 512; change = true; }
            if (cfg.sampleRate != 48000)                             { cfg.sampleRate    = 48000; change = true; }
            if (!change) return;

            bool ok = AudioSettings.Reset(cfg);
            Debug.Log($"[AuraSync Voice] Audio output {(ok ? "set" : "could NOT be set")} for low latency: buffer " +
                      $"{oldBuffer} -> {AudioSettings.GetConfiguration().dspBufferSize}, rate {oldRate} -> {AudioSettings.outputSampleRate} Hz.");
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
#if PHOTON_VOICE_R4
            // WebRTC echo cancellation listens to what the phone plays through the scene's
            // AudioListener. A scene change destroys that listener, and with it the echo reference,
            // so re-attach it to the new scene's listener (toggling AEC does exactly that).
            if (_pvDsp != null && _pvDsp.AEC)
            {
                _pvDsp.AEC = false;
                _pvDsp.AEC = true;
            }
#endif
        }

        // MonoBehaviourPunCallbacks already registers this object (and its IOnEventCallback)
        // with Photon in OnEnable, so no manual AddCallbackTarget is needed.

        void Start()
        {
            // Nothing happens at app launch: no permission prompt, no audio-mode change.
            if (PhotonNetwork.InRoom) StartVoiceSession();
        }

        public override void OnJoinedRoom()  => StartVoiceSession();
        public override void OnLeftRoom()    => StopVoiceSession();
        public override void OnDisconnected(DisconnectCause cause) => StopVoiceSession();

        private void StartVoiceSession()
        {
            if (_sessionActive) return;
            _sessionActive = true;

            _recoveryStage       = 0;
            _gateForcedOpen      = false;
            PacketsSentTotal     = 0;
            PacketsReceivedTotal = 0;
            MicLevel             = 0f;
            VoiceStatus          = "starting";

            PublishMuteState();
            StartCoroutine(RequestMicAndInit());
        }

        private void StopVoiceSession()
        {
            if (!_sessionActive) return;
            _sessionActive = false;

            StopPhotonVoice();
            StopMicrophone();
            if (useAndroidCommunicationMode) DisableAndroidAEC();
            CleanAllReceivers();
            _remoteSpeaking.Clear();
            MicLevel    = 0f;
            VoiceStatus = "not in a room";
        }

        // ── Permission & backend selection ──────────────────────────────────────

        private IEnumerator RequestMicAndInit()
        {
            // Photon Voice connects and plays everyone else straight away. Listening needs no
            // microphone, so a phone still showing the permission prompt (or one that refused it)
            // hears the room anyway; its own microphone is attached once permission arrives.
            bool photonVoice = TryStartPhotonVoice();

#if UNITY_ANDROID && !UNITY_EDITOR
            // The permission is asked for on joining a room, which is also the moment a presenter is
            // busy reading out the room code. An earlier version gave up after 12 seconds and left
            // that phone with no microphone for the whole session — silent to everyone else while
            // still hearing them perfectly. So this waits for as long as the player is in the room.
            if (!MicPermissionGranted())
            {
                VoiceStatus = "waiting for mic permission";
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);

                float waited = 0f;
                while (!MicPermissionGranted() && _sessionActive)
                {
                    waited += Time.unscaledDeltaTime;

                    // Still nothing after a minute: say so on screen rather than failing quietly.
                    if (waited > 60f)
                    {
                        VoiceStatus = "MIC PERMISSION DENIED";
                        if (IsMicAvailable) SetMicAvailable(false);
                    }
                    yield return null;
                }
            }

            if (!_sessionActive) yield break; // left the room while waiting
            _permissionGranted = MicPermissionGranted();
#else
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            _permissionGranted = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif

            if (!_permissionGranted)
            {
                Debug.LogWarning("[AuraSync Voice] Microphone permission denied.");
                VoiceStatus = "MIC PERMISSION DENIED";
                SetMicAvailable(false);
                yield break;
            }

            if (!_sessionActive) yield break; // left the room while waiting

            if (photonVoice)
            {
                AttachPhotonRecorder();
                yield break;
            }

            // Built-in backend
            if (useAndroidCommunicationMode) EnableAndroidAEC();
            yield return new WaitForSeconds(0.2f);
            if (!_sessionActive) yield break;
            InitializeMicrophone();
        }

        /// <summary>Android microphone permission state. Always true where no runtime grant exists.</summary>
        private static bool MicPermissionGranted()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                       UnityEngine.Android.Permission.Microphone);
#else
            return true;
#endif
        }

        private bool TryStartPhotonVoice()
        {
#if PHOTON_VOICE_R4
            var settings = PhotonNetwork.PhotonServerSettings;
            if (settings == null || string.IsNullOrEmpty(settings.AppSettings.AppIdVoice))
            {
                // Every phone runs the same build with the same settings, so all of them fall back
                // together and can still hear each other.
                Debug.LogError("[AuraSync Voice] App Id Voice is empty in PhotonServerSettings, so the Opus " +
                               "backend cannot connect. Falling back to built-in voice.");
                return false;
            }

            StopPhotonVoice();
            BuildPhotonVoiceClient();

            UsingPhotonVoice = true;
            Debug.Log("[AuraSync Voice] Using Photon Voice 2 (Opus). Connecting; the microphone is added " +
                      "once permission is granted.");
            return true;
#else
            return false;
#endif
        }

#if PHOTON_VOICE_R4
        /// <summary>
        /// Creates the voice connection for this room. Awake creates the client; it connects in
        /// Start once it sees PUN is in a room, and follows PUN from then on (same region, voice
        /// room named after the PUN room). Incoming voices play from then on.
        /// </summary>
        private void BuildPhotonVoiceClient()
        {
            _pvRoot = new GameObject("AuraSync_PhotonVoice");
            DontDestroyOnLoad(_pvRoot);

            _pvClient = _pvRoot.AddComponent<AuraSyncVoiceClient>();
            _pvClient.AutoConnectAndJoin = true;
            _pvClient.UsePunAppSettings  = true;
            _pvClient.UsePunAuthValues   = true;

            _pvSpeaking    = false;
            _pvLastVoiceAt = -999f;
            _pvRejoinTimer = 0f;
            _pvRejoinDelay = 5f;
        }

        /// <summary>
        /// Adds this phone's microphone and audio processing to the voice connection.
        ///
        /// Capture is Unity's microphone, the same one the old voice used; it leaves Android's audio
        /// mode, routing and audio focus alone, so Unity keeps playing the voices it receives through
        /// the loudspeaker. Echo cancellation, noise suppression and gain control are then WebRTC's
        /// (AEC3, the canceller in Chrome and WebRTC calls), referenced to exactly what Unity plays.
        ///
        /// With <see cref="useAndroidCallModeMic"/> on, each effect is instead done once: by the phone's
        /// call-mode microphone when it has it, otherwise by WebRTC. Running both would fight: two echo
        /// cancellers smear speech, two gain controls pump the volume.
        /// </summary>
        private void AttachPhotonRecorder()
        {
            if (_pvClient == null || _pvRecorder != null) return;

            bool callModeMic = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            callModeMic = useAndroidCallModeMic;
#endif
            if (callModeMic) QueryAndroidCallEffects(out _hwAec, out _hwAgc, out _hwNs);
            else             _hwAec = _hwAgc = _hwNs = false;

            var recGo = new GameObject("Recorder");
            recGo.transform.SetParent(_pvRoot.transform, false);

            var rec = recGo.AddComponent<Photon.Voice.Unity.Recorder>();
            rec.RecordingEnabled = false; // started below, or by RecordWhenJoined on joining the voice room
            rec.RecordWhenJoined = true;
            rec.SourceType       = Photon.Voice.Unity.Recorder.InputSourceType.Microphone;
            if (callModeMic)
            {
                rec.MicrophoneType = Photon.Voice.Unity.Recorder.MicType.Photon;
                rec.SetAndroidNativeMicrophoneSettings(aec: true, agc: true, ns: true);
            }
            else
            {
                // Unity's microphone, through our own reader: Photon's MicWrapper could fall a second
                // behind for good after a long frame (see RealtimeMicReader).
                rec.SourceType   = Photon.Voice.Unity.Recorder.InputSourceType.Factory;
                rec.InputFactory = () => new RealtimeMicReader(null, 48000);
            }
            // No silent switch to the other microphone type: falling back from Unity's mic would bring
            // back call mode and its audio-focus/routing side effects without anyone noticing.
            rec.UseMicrophoneTypeFallback = false;
            rec.SamplingRate     = POpusCodec.Enums.SamplingRate.Sampling48000;
            rec.FrameDuration    = Photon.Voice.OpusCodec.FrameDuration.Frame20ms;
            rec.Bitrate          = Mathf.Clamp(opusBitrate, 16000, 64000);
            rec.VoiceDetection   = false; // WebRTC's detector below is better and does not clip words
            rec.ReliableMode     = false; // late audio is useless; FEC covers losses instead
            rec.StopRecordingWhenPaused = true; // release the microphone while the app is in the background
            rec.TransmitEnabled  = !_isMuted;
            rec.UserData         = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;

            var dsp = recGo.AddComponent<Photon.Voice.Unity.WebRtcAudioDsp>();
            dsp.AEC                   = !_hwAec;
            dsp.AGC                   = !_hwAgc;
            dsp.NoiseSuppression      = true;
            dsp.NoiseSuppressionLevel = _hwNs ? Photon.Voice.WebRTCAudioProcessor.NSLevel.Low
                                              : Photon.Voice.WebRTCAudioProcessor.NSLevel.Moderate;
            dsp.HighPass              = true;  // removes handling rumble from a phone held in the hand
            // No WebRTC voice detector. It was the cause of calls being one-way at a time (2026-10-16):
            // it runs after the echo canceller, so while the other person's voice played here this
            // phone's own, partly suppressed speech failed it and was never sent. (VeryLow, which the
            // old code chose as "most permissive", is in fact its most aggressive setting.)
            // VoiceTalkGate below decides what is sent instead.
            dsp.VAD                   = false;

            recGo.AddComponent<VoiceFecTuner>();
            recGo.AddComponent<VoiceRecordingTap>(); // session recordings keep the presenter's voice (Phase 9)
            var gate = recGo.AddComponent<VoiceTalkGate>(); // after the DSP: judges the echo-cancelled signal

            _pvRecorder = rec;
            _pvDsp      = dsp;
            _pvGate     = gate;
            _pvDuplex   = "-";
            _pvClient.AddRecorder(rec);

            // RecordWhenJoined only fires on the join itself; if the voice room was joined while the
            // permission prompt was still open, start now.
            if (_pvClient.ClientState == ClientState.Joined && !_pausedForOtherUse) rec.RecordingEnabled = true;

            SetMicAvailable(true);
            Debug.Log($"[AuraSync Voice] Microphone added (Opus {rec.Bitrate / 1000} kbps, 48 kHz, " +
                      $"{(callModeMic ? "Android call-mode mic" : "Unity mic")}). Phone effects: AEC={_hwAec} " +
                      $"AGC={_hwAgc} NS={_hwNs}; WebRTC: AEC={dsp.AEC} AGC={dsp.AGC} NS={dsp.NoiseSuppression}.");
        }

        /// <summary>Which effects this phone's call-mode microphone provides. All false off Android.</summary>
        private static void QueryAndroidCallEffects(out bool aec, out bool agc, out bool ns)
        {
            aec = agc = ns = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var probe = new AndroidJavaObject("com.exitgames.photon.audioinaec.AudioInAEC"))
                {
                    aec = probe.Call<bool>("AECIsAvailable");
                    agc = probe.Call<bool>("AGCIsAvailable");
                    ns  = probe.Call<bool>("NSIsAvailable");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AuraSync Voice] Could not query the phone's call effects ({e.Message}); " +
                                 "using WebRTC processing for all of them.");
            }
#endif
        }
#endif

        private void StopPhotonVoice()
        {
#if PHOTON_VOICE_R4
            if (_pvRoot != null) Destroy(_pvRoot); // the client disconnects from the voice server in OnDestroy
            _pvRoot     = null;
            _pvClient   = null;
            _pvRecorder = null;
            _pvDsp      = null;
            _pvGate     = null;
            _pvSpeaking = false;
#endif
            UsingPhotonVoice = false;
        }

        private void SetMicAvailable(bool available)
        {
            if (IsMicAvailable == available) return;
            IsMicAvailable = available;
            OnMicMuteStateChanged?.Invoke(_isMuted); // HUDs re-read IsMicAvailable
        }

        // ── Android call mode (built-in backend) ────────────────────────────────

        private void EnableAndroidAEC()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var audioMgr = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                {
                    audioMgr.Call("setMode", 3);               // MODE_IN_COMMUNICATION: hardware AEC/NS/AGC
                    audioMgr.Call("setSpeakerphoneOn", true);  // loudspeaker, not the earpiece
                    Debug.Log("[AuraSync Voice] Android call mode + speakerphone on.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AuraSync Voice] Call mode setup failed: {e.Message}");
            }
#endif
        }

        private void DisableAndroidAEC()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var audioMgr = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
                {
                    audioMgr.Call("setMode", 0); // MODE_NORMAL, so other apps are not affected
                }
            }
            catch { /* best effort */ }
#endif
        }

        // ── Built-in microphone ─────────────────────────────────────────────────

        public void InitializeMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                StartCoroutine(RetryInit());
                return;
            }
            _selectedDevice = Microphone.devices[0];
            Debug.Log($"[AuraSync Voice] Mic device: {_selectedDevice}");
            SetMicAvailable(true);
            if (!_isMuted) StartMicrophone();
        }

        private IEnumerator RetryInit()
        {
            yield return new WaitForSeconds(1f);
            if (!_sessionActive) yield break;

            if (Microphone.devices.Length > 0)
            {
                _selectedDevice = Microphone.devices[0];
                SetMicAvailable(true);
                if (!_isMuted) StartMicrophone();
            }
            else
            {
                SetMicAvailable(false);
            }
        }

        private void StartMicrophone()
        {
            if (string.IsNullOrEmpty(_selectedDevice)) return;
            if (Microphone.IsRecording(_selectedDevice))
                Microphone.End(_selectedDevice);

            // Recovery stage 3 asks the device for its own preferred rate instead of ours: a phone
            // that cannot resample internally returns silence for an unsupported request.
            int requestRate = _recoveryStage >= 3 ? NativeMicRate() : micSampleRate;

            _micClip = Microphone.Start(_selectedDevice, true, 2, requestRate);
            if (_micClip == null)
            {
                Debug.LogError("[AuraSync Voice] Microphone.Start returned null!");
                VoiceStatus = "mic failed to start";
                return;
            }

            _actualSampleRate = _micClip.frequency;
            _lastSamplePos    = Mathf.Max(0, Microphone.GetPosition(_selectedDevice));
            _isRecording      = true;

            // Give the watchdog a fresh window to judge this attempt.
            _micStartedAt       = Time.unscaledTime;
            _lastNonSilentAt    = Time.unscaledTime;
            _lastMicPos         = -1;
            _lastMicPosChangeAt = Time.unscaledTime;

            Debug.Log($"[AuraSync Voice] Mic started - requested {requestRate}Hz, actual {_actualSampleRate}Hz, " +
                      $"sending at {networkSampleRate}Hz (recovery stage {_recoveryStage})");
        }

        /// <summary>The microphone's own preferred sample rate, or 16 kHz if the device will not say.</summary>
        private int NativeMicRate()
        {
            try
            {
                Microphone.GetDeviceCaps(_selectedDevice, out int minFreq, out int maxFreq);
                if (minFreq == 0 && maxFreq == 0) return 48000; // "any rate"
                return Mathf.Clamp(maxFreq, 8000, 48000);
            }
            catch
            {
                return 16000;
            }
        }

        // ── Lending the microphone (voice search) ──────────────────────────────

        private bool _pausedForOtherUse;

        /// <summary>
        /// Stops voice-chat capture so something else can record from the microphone for a moment.
        /// Unity can only record one clip per microphone: starting a second recording would end the
        /// voice chat's. Photon Voice's native call-mode capture has to be released as well: while
        /// Android hands the mic to a voice-communication recording, any other recording gets only
        /// silence. Returns true if voice chat was actually recording (and so must be resumed).
        /// </summary>
        public bool PauseCaptureForOtherUse()
        {
#if PHOTON_VOICE_R4
            if (UsingPhotonVoice)
            {
                if (_pvRecorder == null || !_pvRecorder.RecordingEnabled) return false;
                _pvRecorder.RecordingEnabled = false; // stops and releases the native microphone now
                _pausedForOtherUse = true;
                Debug.Log("[AuraSync Voice] Mic lent out (voice search); Photon Voice capture paused.");
                return true;
            }
#endif
            if (UsingPhotonVoice || !_isRecording) return false;
            StopMicrophone();
            _pausedForOtherUse = true;
            Debug.Log("[AuraSync Voice] Mic lent out (voice search); voice chat paused.");
            return true;
        }

        /// <summary>Gives the microphone back to voice chat after <see cref="PauseCaptureForOtherUse"/>.</summary>
        public void ResumeCaptureAfterOtherUse()
        {
            if (!_pausedForOtherUse) return;
            _pausedForOtherUse = false;
#if PHOTON_VOICE_R4
            // Only back on if the voice room is still joined; otherwise RecordWhenJoined restarts it.
            if (UsingPhotonVoice && _pvRecorder != null && _pvClient != null)
                _pvRecorder.RecordingEnabled = _pvClient.ClientState == ClientState.Joined;
#endif
            if (_sessionActive && !_isMuted && !UsingPhotonVoice) StartMicrophone();
            Debug.Log("[AuraSync Voice] Mic returned; voice chat resumed.");
        }

        private void StopMicrophone()
        {
            if (!string.IsNullOrEmpty(_selectedDevice) && Microphone.IsRecording(_selectedDevice))
                Microphone.End(_selectedDevice);
            _isRecording = false;
            _micClip = null;
        }

        // ── Per frame ───────────────────────────────────────────────────────────

        void Update()
        {
            if (Time.unscaledDeltaTime > _diagHitch) _diagHitch = Time.unscaledDeltaTime;
            PollSpeaking();
            LogDiagnostics();

            if (UsingPhotonVoice)
            {
                UpdatePhotonVoice();
                return;
            }

            CaptureAndSend();
            RunMicWatchdog();
            RunGateWatchdog();
        }

        /// <summary>
        /// Detects a microphone that is "recording" but delivering nothing, and works through a
        /// ladder of recoveries. Without this, a device whose capture is silently broken simply
        /// looks like a person who is not talking — which is exactly how one-way audio presents.
        /// </summary>
        private void RunMicWatchdog()
        {
            if (!_sessionActive || UsingPhotonVoice || _isMuted || !PhotonNetwork.InRoom)
            {
                VoiceStatus = !PhotonNetwork.InRoom ? "not in a room"
                            : _isMuted              ? "muted"
                            : VoiceStatus;
                return;
            }

            if (!_isRecording)
            {
                VoiceStatus = IsMicAvailable ? "mic not running" : "no microphone";
                return;
            }

            _watchdogTimer += Time.unscaledDeltaTime;
            if (_watchdogTimer < 1f) return;
            _watchdogTimer = 0f;

            float now = Time.unscaledTime;
            if (now - _micStartedAt < 3f) { VoiceStatus = "starting microphone"; return; }

            bool stalled = now - _lastMicPosChangeAt > 2.5f;  // the record head is not moving
            bool silent  = now - _lastNonSilentAt    > 3.5f;  // it moves, but every sample is zero

            if (!stalled && !silent)
            {
                VoiceStatus = _gateForcedOpen ? "live (gate forced open)" : "live";
                return;
            }

            string problem = stalled ? "microphone stalled" : "microphone is recording silence";

            switch (_recoveryStage)
            {
                case 0:
                    _recoveryStage = 1;
                    Debug.LogWarning($"[AuraSync Voice] {problem}. Restarting the microphone.");
                    VoiceStatus = "recovering: restarting mic";
                    StartMicrophone();
                    break;

                case 1:
                    _recoveryStage = 2;
                    Debug.LogWarning($"[AuraSync Voice] {problem} after a restart. Leaving Android " +
                                     "call mode (it can reroute capture) and restarting.");
                    VoiceStatus = "recovering: leaving call mode";
                    useAndroidCommunicationMode = false;
                    DisableAndroidAEC();
                    StartMicrophone();
                    break;

                case 2:
                    _recoveryStage = 3;
                    Debug.LogWarning($"[AuraSync Voice] {problem}. Retrying at the device's own " +
                                     "sample rate instead of a requested one.");
                    VoiceStatus = "recovering: native sample rate";
                    StartMicrophone();
                    break;

                default:
                    Debug.LogError($"[AuraSync Voice] {problem} and every recovery has been tried. " +
                                   "This device is not giving Unity any audio: check that no other " +
                                   "app holds the microphone, and that the permission is granted.");
                    VoiceStatus = "MIC GIVES NO AUDIO";
                    break;
            }
        }

        /// <summary>
        /// If the microphone is clearly picking up sound but the silence gate has let nothing
        /// through for a while, the gate is wrong for this device. Open it and keep it open.
        /// </summary>
        private void RunGateWatchdog()
        {
            if (!_sessionActive || UsingPhotonVoice || _isMuted || _gateForcedOpen || alwaysTransmit) return;

            _gateWatchTimer += Time.unscaledDeltaTime;
            if (_gateWatchTimer < 8f) return;

            bool heardSomething = _gateWatchPeak > 0.01f;
            if (heardSomething && _gateWatchSent == 0)
            {
                _gateForcedOpen = true;
                alwaysTransmit  = true;
                Debug.LogWarning($"[AuraSync Voice] The mic reached {_gateWatchPeak:F3} but the silence " +
                                 "gate sent nothing in 8 s. Gate disabled for this session.");
            }

            _gateWatchTimer = 0f;
            _gateWatchPeak  = 0f;
            _gateWatchSent  = 0;
        }

        private void CaptureAndSend()
        {
            if (!_sessionActive || !_isRecording || _isMuted || !PhotonNetwork.InRoom || _micClip == null) return;

            int micPos = Microphone.GetPosition(_selectedDevice);
            if (micPos < 0) { StartMicrophone(); return; }

            if (micPos != _lastMicPos)
            {
                _lastMicPos         = micPos;
                _lastMicPosChangeAt = Time.unscaledTime;
            }

            int chunkSize = Mathf.FloorToInt(_actualSampleRate * chunkDuration);
            int netChunk  = Mathf.Max(1, Mathf.RoundToInt(networkSampleRate * chunkDuration));
            if (chunkSize <= 0) return;

            int clipLen   = _micClip.samples;
            int available = (micPos >= _lastSamplePos)
                                ? micPos - _lastSamplePos
                                : clipLen - _lastSamplePos + micPos;

            // A slow frame (heavy model) can leave a backlog. Send up to 400 ms of it; only beyond
            // that is old audio dropped (it would arrive too late to be useful).
            int maxBacklog = chunkSize * 20;
            if (available > maxBacklog)
            {
                _lastSamplePos = (micPos - maxBacklog + clipLen) % clipLen;
                available = maxBacklog;
            }

            EnsureBuffers(chunkSize, netChunk);
            bool sentAny = false;

            while (available >= chunkSize)
            {
                ReadMicChunk(chunkSize, clipLen);
                _lastSamplePos = (_lastSamplePos + chunkSize) % clipLen;
                available     -= chunkSize;

                Downsample(_readBuf, chunkSize, _netBuf, netChunk);

                float rms = ComputeRms(_netBuf, netChunk);
                TrackLevels(rms);

                MicLevel = rms;
                if (rms > _gateWatchPeak) _gateWatchPeak = rms;
                // Anything above the noise of a working mic. Digital silence is exactly 0.
                if (rms > 0.0004f) _lastNonSilentAt = Time.unscaledTime;

                if (!PassesGate(rms)) continue; // silence: send nothing

                // Packet: [rate in kHz][mu-law samples...]
                byte[] payload = new byte[netChunk + 1];
                payload[0] = (byte)(networkSampleRate / 1000);
                for (int i = 0; i < netChunk; i++)
                    payload[i + 1] = LinearToMuLaw(FloatToPcm16(_netBuf[i]));

                PhotonNetwork.RaiseEvent(
                    EventCodeVoice,
                    payload,
                    new RaiseEventOptions { Receivers = ReceiverGroup.Others },
                    SendOptions.SendUnreliable);
                sentAny = true;
                _diagSent++;
                _gateWatchSent++;
                PacketsSentTotal++;
            }

            // Photon normally batches outgoing data at its send rate (~33 ms). Voice should not wait.
            if (sentAny) PhotonNetwork.SendAllOutgoingCommands();
        }

        /// <summary>
        /// Adaptive silence gate: tracks the background noise level and opens when the voice is
        /// clearly above it. A fixed threshold silenced quiet microphones completely.
        /// </summary>
        private bool PassesGate(float rms)
        {
            if (alwaysTransmit) { GateThreshold = 0f; return true; }

            // Fall quickly to quieter levels, rise slowly (so speech does not raise the floor).
            _noiseFloor = rms < _noiseFloor
                ? Mathf.Lerp(_noiseFloor, rms, 0.2f)
                : Mathf.Lerp(_noiseFloor, rms, 0.002f);

            float threshold = Mathf.Max(minSilenceThreshold, _noiseFloor * noiseFloorMultiplier);
            GateThreshold = threshold;
            if (rms >= threshold) _voiceUntil = Time.unscaledTime + hangoverSeconds;
            return Time.unscaledTime <= _voiceUntil;
        }

        private void EnsureBuffers(int chunkSize, int netChunk)
        {
            if (_readBuf.Length != chunkSize) _readBuf = new float[chunkSize];
            if (_netBuf.Length  != netChunk)  _netBuf  = new float[netChunk];
        }

        private void ReadMicChunk(int chunkSize, int clipLen)
        {
            int end = _lastSamplePos + chunkSize;

            if (end <= clipLen)
            {
                _micClip.GetData(_readBuf, _lastSamplePos);
                return;
            }

            int part1 = clipLen - _lastSamplePos;
            int part2 = chunkSize - part1;
            if (_wrapA.Length != part1) _wrapA = new float[part1];
            if (_wrapB.Length != part2) _wrapB = new float[part2];

            _micClip.GetData(_wrapA, _lastSamplePos);
            _micClip.GetData(_wrapB, 0);
            Array.Copy(_wrapA, 0, _readBuf, 0,     part1);
            Array.Copy(_wrapB, 0, _readBuf, part1, part2);
        }

        /// <summary>Box-filter resample (averages the source samples under each output sample).</summary>
        private static void Downsample(float[] src, int srcCount, float[] dst, int dstCount)
        {
            if (srcCount == dstCount)
            {
                Array.Copy(src, dst, dstCount);
                return;
            }

            float ratio = (float)srcCount / dstCount;
            for (int i = 0; i < dstCount; i++)
            {
                int s0 = (int)(i * ratio);
                int s1 = Mathf.Min(srcCount, Mathf.Max(s0 + 1, (int)((i + 1) * ratio)));
                float sum = 0f;
                for (int s = s0; s < s1; s++) sum += src[s];
                dst[i] = sum / (s1 - s0);
            }
        }

        private static float ComputeRms(float[] buf, int count)
        {
            if (count <= 0) return 0f;
            float acc = 0f;
            for (int i = 0; i < count; i++) acc += buf[i] * buf[i];
            return Mathf.Sqrt(acc / count);
        }

        // ── G.711 mu-law ────────────────────────────────────────────────────────

        private static short FloatToPcm16(float f)
        {
            return (short)(Mathf.Clamp(f, -1f, 1f) * 32767f);
        }

        private static byte LinearToMuLaw(short pcm)
        {
            const int Bias = 0x84;
            const int Clip = 32635;

            int sample = pcm;
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = -sample;
            if (sample > Clip) sample = Clip;
            sample += Bias;

            int exponent = 7;
            for (int mask = 0x4000; (sample & mask) == 0 && exponent > 0; exponent--, mask >>= 1) { }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        private static readonly float[] MuLawTable = BuildMuLawTable();

        private static float[] BuildMuLawTable()
        {
            float[] table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                int mu = ~i & 0xFF;
                int sign = mu & 0x80;
                int exponent = (mu >> 4) & 0x07;
                int mantissa = mu & 0x0F;
                int sample = ((mantissa << 3) + 0x84) << exponent;
                sample -= 0x84;
                table[i] = (sign != 0 ? -sample : sample) / 32768f;
            }
            return table;
        }

        // ── Receive ─────────────────────────────────────────────────────────────

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == EventCodeSpeaking)
            {
                bool speaking = photonEvent.CustomData is bool b && b;
                _remoteSpeaking[photonEvent.Sender] = speaking;
                OnSpeakingChanged?.Invoke(photonEvent.Sender, speaking);
                return;
            }

            if (photonEvent.Code != EventCodeVoice) return;
            if (!_sessionActive || UsingPhotonVoice) return;

            byte[] payload = photonEvent.CustomData as byte[];
            if (payload == null || payload.Length < 2) return;

            int senderActor = photonEvent.Sender;
            if (PhotonNetwork.LocalPlayer != null && senderActor == PhotonNetwork.LocalPlayer.ActorNumber) return;

            int rate = payload[0] * 1000;
            if (rate <= 0) rate = 8000;

            float[] samples = new float[payload.Length - 1];
            for (int i = 0; i < samples.Length; i++) samples[i] = MuLawTable[payload[i + 1]];

            if (!_receivers.TryGetValue(senderActor, out RemoteVoiceReceiver rcv) || rcv == null)
            {
                var go = new GameObject($"VoiceReceiver_{senderActor}");
                go.transform.SetParent(transform, false);
                // Add the AudioSource BEFORE RemoteVoiceReceiver: [RequireComponent] only
                // auto-adds dependencies in the editor, not for runtime AddComponent.
                go.AddComponent<AudioSource>();
                rcv = go.AddComponent<RemoteVoiceReceiver>();
                rcv.Initialize(senderActor);
                _receivers[senderActor] = rcv;
                Debug.Log($"[AuraSync Voice] Created receiver for actor {senderActor}");
            }

            rcv.Feed(samples, rate);

            PacketsReceivedTotal++;
            _diagReceived.TryGetValue(senderActor, out int n);
            _diagReceived[senderActor] = n + 1;
        }

        // ── Speaking state ──────────────────────────────────────────────────────

        public bool IsSpeaking(int actorNumber)
        {
            if (_remoteSpeaking.TryGetValue(actorNumber, out bool reported) && reported) return true;
            return _receivers.TryGetValue(actorNumber, out RemoteVoiceReceiver rcv) && rcv != null && rcv.IsActive;
        }

        private void PollSpeaking()
        {
            _speakingPollTimer += Time.unscaledDeltaTime;
            if (_speakingPollTimer < 0.1f) return;
            _speakingPollTimer = 0f;

            foreach (var kv in _receivers)
            {
                bool now = kv.Value != null && kv.Value.IsActive;
                _speaking.TryGetValue(kv.Key, out bool was);
                if (now == was) continue;

                _speaking[kv.Key] = now;
                OnSpeakingChanged?.Invoke(kv.Key, now);
            }
        }

        /// <summary>Photon Voice, every frame: status for the voice panel, reconnection, speaking state.</summary>
        private void UpdatePhotonVoice()
        {
#if PHOTON_VOICE_R4
            if (_pvClient == null) return;

            ClientState state = _pvClient.ClientState;
            bool joined = state == ClientState.Joined;

            var meter = _pvRecorder != null ? _pvRecorder.LevelMeter : null;
            MicLevel             = meter != null ? meter.CurrentPeakAmp : 0f; // peak: reads clearly while talking
            PacketsSentTotal     = _pvClient.VoiceClient.FramesSent;
            PacketsReceivedTotal = _pvClient.VoiceClient.FramesReceived;

            VoiceStatus = joined && _pvRecorder == null     ? (_permissionGranted ? "listening (adding mic)" : "listening only - no mic permission")
                        : joined                            ? (_isMuted ? "muted" : _pausedForOtherUse ? "mic lent to voice search" : "live")
                        : state == ClientState.Disconnected ? $"voice offline ({_pvClient.Client.DisconnectedCause})"
                        :                                     "connecting voice";

            RunPhotonVoiceRejoin(state);
            UpdatePhotonVoiceSpeaking();
#endif
        }

#if PHOTON_VOICE_R4
        /// <summary>
        /// The voice connection is separate from PUN's. Photon Voice re-follows PUN on its own after
        /// most drops, but stops for good after an authentication or join error. Without this, a
        /// phone whose voice connection failed once stays silent for the rest of the session while
        /// everything else (model sync, HUD) works, which is exactly the "sometimes" failure.
        /// Retries back off from 5 s to 30 s.
        /// </summary>
        private void RunPhotonVoiceRejoin(ClientState state)
        {
            if (state == ClientState.Joined)
            {
                _pvRejoinTimer = 0f;
                _pvRejoinDelay = 5f;
                return;
            }
            if (!PhotonNetwork.InRoom || state != ClientState.Disconnected) { _pvRejoinTimer = 0f; return; }

            _pvRejoinTimer += Time.unscaledDeltaTime;
            if (_pvRejoinTimer < _pvRejoinDelay) return;

            Debug.LogWarning($"[AuraSync Voice] Voice server disconnected ({_pvClient.Client.DisconnectedCause}) while " +
                             $"in the room for {_pvRejoinDelay:F0} s. Reconnecting.");
            _pvRejoinTimer = 0f;
            _pvRejoinDelay = Mathf.Min(_pvRejoinDelay * 2f, 30f);
            _pvClient.ConnectAndJoinRoom();
        }

        /// <summary>
        /// Tell the room when this player starts or stops talking. The WebRTC voice detector stops
        /// sending between words, so "stopped" waits 400 ms of quiet before it is announced: the
        /// speaking badge stays steady through a sentence instead of flickering per syllable.
        /// </summary>
        private void UpdatePhotonVoiceSpeaking()
        {
            if (_pvRecorder == null || !PhotonNetwork.InRoom) return;

            // The gate holds the line open through pauses, so "transmitting" is not "talking": the
            // badge follows the processed speech level instead.
            bool talking = _pvGate != null ? _pvGate.SpokeWithinMs(100) : _pvRecorder.IsCurrentlyTransmitting;
            if (!_isMuted && _pvRecorder.TransmitEnabled && talking) _pvLastVoiceAt = Time.unscaledTime;
            bool speaking = Time.unscaledTime - _pvLastVoiceAt < 0.4f;
            if (speaking == _pvSpeaking) return;
            _pvSpeaking = speaking;

            PhotonNetwork.RaiseEvent(EventCodeSpeaking, speaking,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
        }
#endif

        // ── Controls ────────────────────────────────────────────────────────────

        // ── Incoming audio (listening) ──────────────────────────────────────────

        /// <summary>
        /// True when this device has silenced everyone else. Separate from <see cref="IsMuted"/>,
        /// which is about this device's own microphone: you can keep talking while muting the room,
        /// which is what you want when the people around you are also in the meeting and you are
        /// getting the same voices twice, once over the air.
        ///
        /// Static because receivers are created as voices arrive and need the current state at
        /// construction, not only when it changes.
        /// </summary>
        public static bool IncomingMuted { get; private set; }

        /// <summary>Raised when this device starts or stops listening to the room.</summary>
        public event Action<bool> OnIncomingMuteChanged;

        public void ToggleIncomingMute() => SetIncomingMute(!IncomingMuted);

        public void SetIncomingMute(bool mute)
        {
            IncomingMuted = mute;
            ApplyIncomingMute();

            Debug.Log($"[AuraSync Voice] Incoming audio muted = {mute}");
            OnIncomingMuteChanged?.Invoke(mute);
        }

        private void ApplyIncomingMute()
        {
            float volume = IncomingMuted ? 0f : 1f;

            foreach (var kv in _receivers)
            {
                if (kv.Value == null) continue;
                var src = kv.Value.GetComponent<AudioSource>();
                if (src != null) src.volume = volume;
            }

#if PHOTON_VOICE_R4
            // Photon Voice builds its own speakers, one per remote player, outside _receivers.
            foreach (var speaker in FindObjectsOfType<Photon.Voice.Unity.Speaker>())
            {
                var src = speaker.GetComponent<AudioSource>();
                if (src != null) src.volume = volume;
            }
#endif
        }

        public void ToggleMute() => SetMute(!_isMuted);

        public void SetMute(bool mute)
        {
            _isMuted = mute;

#if PHOTON_VOICE_R4
            if (UsingPhotonVoice && _pvRecorder != null) _pvRecorder.TransmitEnabled = !mute;
#endif
            if (!UsingPhotonVoice)
            {
                if (mute) StopMicrophone();
                // While voice search has the mic, unmuting is remembered and applied when it is given back.
                else if (_sessionActive && _permissionGranted && !_pausedForOtherUse) StartMicrophone();
            }

            PublishMuteState();
            Debug.Log($"[AuraSync Voice] Muted = {_isMuted}");
            OnMicMuteStateChanged?.Invoke(_isMuted);
        }

        private void PublishMuteState()
        {
            if (!PhotonNetwork.InRoom) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PropMuted, _isMuted } });
        }

        /// <summary>
        /// One compact line for the on-screen voice panel. Reading it on both phones says which
        /// side of a one-way call is failing, with no cable and no logcat:
        ///   tx counts up but the other phone's rx does not  -> packets are not getting across
        ///   lvl stays 0.000                                 -> that phone's mic is giving nothing
        ///   lvl is healthy but tx stays 0                   -> the silence gate is holding it back
        /// </summary>
        public string DiagnosticLine()
        {
#if PHOTON_VOICE_R4
            // tx/rx are Opus frames; "lost" is the share of incoming frames that never arrived even
            // after error correction. A few percent is fine; double digits means the network.
            if (UsingPhotonVoice && _pvClient != null)
                return $"VOICE {VoiceStatus} | Opus | lvl {MicLevel:F2} | " +
                       $"tx {PacketsSentTotal} rx {PacketsReceivedTotal} lost {_pvClient.FramesLostPercent:F0}% | " +
                       $"spk {SpeakerSummary()} | over {_pvDuplex}";
#endif
            if (UsingPhotonVoice)
                return $"VOICE {VoiceStatus} | Opus backend";

            return $"VOICE {VoiceStatus} | lvl {MicLevel:F3} gate {GateThreshold:F3} | " +
                   $"tx {PacketsSentTotal} rx {PacketsReceivedTotal}";
        }

#if PHOTON_VOICE_R4
        /// <summary>
        /// Double-talk over the last 5 s, i.e. while a remote voice was audible on this phone:
        ///   sent       share of this phone's frames that went out (should be ~100% while you talk over them)
        ///   raw        loudest mic level before processing (your voice plus the loudspeaker's echo)
        ///   clean      loudest level after echo cancelling (your voice alone; ~0 if you stayed quiet)
        /// raw high + clean ~0 while you were talking = the echo canceller is suppressing your speech;
        /// clean high + sent low = frames are being held back (the old one-way fault).
        /// </summary>
        private string LogDuplexStats()
        {
            if (_pvGate == null) return "gate=none";
            VoiceTalkGate.Stats s = _pvGate.TakeStats();
            int pct = s.FramesHearing > 0 ? Mathf.RoundToInt(100f * s.SentHearing / s.FramesHearing) : -1;
            _pvDuplex = s.FramesHearing > 0 ? $"{pct}% {s.RawPeakHearing:F2}>{s.PeakHearing:F2}" : "-";
            return $"gate sent={s.Sent}/{s.Frames} peak={s.Peak:F3} noise={_pvGate.NoiseFloor:F3} " +
                   $"echo={_pvGate.EchoCoupling:F3} | while hearing: frames={s.FramesHearing} " +
                   $"sent={s.SentHearing} ({(pct < 0 ? "-" : pct + "%")}) raw={s.RawPeakHearing:F3} clean={s.PeakHearing:F3}";
        }

        /// <summary>
        /// "playing/linked" speakers, the loudest one's level, and the largest playback buffer / jitter. Separates "nothing arrives" (rx
        /// flat) from "arrives but is not played" (linked but not playing) from "plays but is
        /// silent/inaudible" (playing, out 0.00 = the sender sent silence; out > 0 = it is playing,
        /// so check volume/route on this phone).
        /// </summary>
        private string SpeakerSummary()
        {
            int linked = 0, playing = 0, buf = 0, jit = 0;
            float peak = 0f;
            if (_pvRoot != null)
            {
                foreach (var sp in _pvRoot.GetComponentsInChildren<Photon.Voice.Unity.Speaker>())
                {
                    if (!sp.IsLinked) continue;
                    linked++;
                    if (sp.IsPlaying) playing++;
                    if (sp.TryGetComponent(out OutputLevel lvl) && lvl.Peak > peak) peak = lvl.Peak;
                    if (sp.TryGetComponent(out AdaptivePlayDelay apd))
                    {
                        buf = Mathf.Max(buf, apd.DelayMs);
                        jit = Mathf.Max(jit, apd.JitterMs);
                    }
                }
            }
            // buf = playback buffer now, jit = worst arrival lateness in the last 30 s (both ms).
            return $"{playing}/{linked} out {peak:F2} buf {buf} jit {jit}";
        }
#endif

        /// <summary>True if the player has muted their microphone.</summary>
        public static bool IsPlayerMuted(Player player)
        {
            if (player == null) return false;
            return player.CustomProperties.TryGetValue(PropMuted, out object v) && v is bool b && b;
        }

        // ── Diagnostics ─────────────────────────────────────────────────────────

        private void TrackLevels(float rms)
        {
            _diagRmsSum += rms;
            _diagRmsCount++;
            if (rms > _diagPeak) _diagPeak = rms;
        }

        /// <summary>
        /// Every 5 s in a room: is the mic producing sound, is it being sent, is anything arriving?
        /// Compare the presenter's and a viewer's logs to see which side of a one-way call is failing.
        /// </summary>
        private void LogDiagnostics()
        {
            if (!logDiagnostics || !_sessionActive) return;

            _diagTimer += Time.unscaledDeltaTime;
            if (_diagTimer < 5f) return;

            string received = "";
            foreach (var kv in _diagReceived) received += $" actor{kv.Key}={kv.Value}";
            if (received.Length == 0) received = " none";

            if (UsingPhotonVoice)
            {
#if PHOTON_VOICE_R4
                if (_pvClient != null)
                {
                    string duplex = LogDuplexStats();
                    Debug.Log($"[AuraSync Voice] 5s | backend=PhotonVoice state={_pvClient.ClientState} " +
                              $"room='{(_pvClient.Client.CurrentRoom != null ? _pvClient.Client.CurrentRoom.Name : "-")}' " +
                              $"muted={_isMuted} recording={(_pvRecorder != null && _pvRecorder.RecordingEnabled)} " +
                              $"transmitting={(_pvRecorder != null && _pvRecorder.IsCurrentlyTransmitting)} " +
                              $"level={MicLevel:F3} framesSent={PacketsSentTotal} framesReceived={PacketsReceivedTotal} " +
                              $"rx/s={_pvClient.FramesReceivedPerSecond:F0} lost={_pvClient.FramesLostPercent:F1}% " +
                              $"spk={SpeakerSummary()} hitch={_diagHitch * 1000f:F0}ms " +
                              $"micSkips={RealtimeMicReader.Skips}/{RealtimeMicReader.SkippedMs}ms {duplex} status='{VoiceStatus}'");
                }
#endif
            }
            else
            {
                float avg = _diagRmsCount > 0 ? _diagRmsSum / _diagRmsCount : 0f;
                float gate = Mathf.Max(minSilenceThreshold, _noiseFloor * noiseFloorMultiplier);
                Debug.Log($"[AuraSync Voice] 5s | backend=builtin muted={_isMuted} micRecording={_isRecording} " +
                          $"micRate={_actualSampleRate} level avg={avg:F4} peak={_diagPeak:F3} gate={gate:F4} " +
                          $"packetsSent={_diagSent} packetsReceived:{received} " +
                          $"status='{VoiceStatus}' recovery={_recoveryStage} gateForcedOpen={_gateForcedOpen}");
            }

            _diagTimer = 0f;
            _diagSent = 0;
            _diagRmsSum = 0f;
            _diagRmsCount = 0;
            _diagPeak = 0f;
            _diagHitch = 0f;
            _diagReceived.Clear();
        }

        // ── Photon callbacks ────────────────────────────────────────────────────

        public override void OnPlayerLeftRoom(Player other)
        {
            if (_receivers.TryGetValue(other.ActorNumber, out RemoteVoiceReceiver rcv))
            {
                if (rcv != null) Destroy(rcv.gameObject);
                _receivers.Remove(other.ActorNumber);
            }
            _speaking.Remove(other.ActorNumber);
            _remoteSpeaking.Remove(other.ActorNumber);
        }

        private void CleanAllReceivers()
        {
            foreach (var kv in _receivers)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _receivers.Clear();
            _speaking.Clear();
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    //  RemoteVoiceReceiver (built-in backend): one per remote player.
    //
    //  Audio is injected with OnAudioFilterRead on Unity's audio thread. A small jitter buffer
    //  smooths network timing:
    //   - playback starts once ~60 ms is buffered and re-primes after an underrun;
    //   - if the buffer grows past ~180 ms (bursty network), it skips ahead so delay does not
    //     keep building up. Real-time beats completeness for a conversation.
    // ────────────────────────────────────────────────────────────────────────────
    public class RemoteVoiceReceiver : MonoBehaviour
    {
        public int ActorNumber { get; private set; }

        /// <summary>True while audio has arrived in the last ~350 ms.</summary>
        public bool IsActive => Time.unscaledTime - _lastPacketTime < 0.35f;

        private float[] _ring;
        private int     _readHead  = 0;
        private int     _writeHead = 0;
        private int     _filled    = 0;
        private bool    _primed    = false;
        private int     _primeSamples;
        private int     _maxLatencySamples;
        private readonly object _lock = new object();

        private float _lastPacketTime = -999f;
        private int   _outputSampleRate;

        private const int RING_MS        = 400;
        private const int PRIME_MS       = 60;
        private const int MAX_LATENCY_MS = 180;

        public void Initialize(int actorNumber)
        {
            ActorNumber = actorNumber;

            // OnAudioFilterRead runs at the hardware output rate (44100/48000 Hz on Android),
            // so everything in the ring buffer is stored at that rate.
            _outputSampleRate  = AudioSettings.outputSampleRate;
            _ring              = new float[_outputSampleRate * RING_MS / 1000];
            _primeSamples      = _outputSampleRate * PRIME_MS / 1000;
            _maxLatencySamples = _outputSampleRate * MAX_LATENCY_MS / 1000;

            var src          = GetComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.volume       = VoiceChatManager.IncomingMuted ? 0f : 1f;
            src.loop         = true;
            src.playOnAwake  = false;
            src.mute         = false;
            src.priority     = 0; // never let other sounds steal this voice

            // A silent looping clip keeps the AudioSource (and so OnAudioFilterRead) running.
            int dummyLen = Mathf.Max(4096, _outputSampleRate / 4);
            src.clip = AudioClip.Create("voice_dummy", dummyLen, 1, _outputSampleRate, false);
            src.Play();
        }

        /// <summary>Called on the main thread when network audio arrives.</summary>
        public void Feed(float[] samples, int sourceSampleRate)
        {
            if (samples == null || samples.Length == 0) return;
            _lastPacketTime = Time.unscaledTime;

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

                // Too much queued = audible delay. Jump back to a small cushion.
                if (_filled > _maxLatencySamples)
                {
                    int drop = _filled - _primeSamples;
                    _readHead = (_readHead + drop) % ringLen;
                    _filled  -= drop;
                }
            }
        }

        private static float[] LinearResample(float[] input, int fromRate, int toRate)
        {
            if (fromRate == toRate || input.Length == 0) return input;

            int    outLen = Mathf.RoundToInt((float)input.Length * toRate / fromRate);
            float[] output = new float[outLen];
            float  ratio  = (float)(input.Length - 1) / Mathf.Max(outLen - 1, 1);

            for (int i = 0; i < outLen; i++)
            {
                float srcF = i * ratio;
                int   srcI = (int)srcF;
                float frac = srcF - srcI;
                float a    = input[srcI];
                float b    = srcI + 1 < input.Length ? input[srcI + 1] : a;
                output[i]  = a + frac * (b - a);
            }
            return output;
        }

        // Called from Unity's audio thread.
        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                int frames = data.Length / channels;
                for (int i = 0; i < frames; i++)
                {
                    if (!_primed && _filled >= _primeSamples) _primed = true;

                    float s = 0f;
                    if (_primed)
                    {
                        if (_filled > 0)
                        {
                            s         = _ring[_readHead];
                            _readHead = (_readHead + 1) % _ring.Length;
                            _filled--;
                        }
                        else
                        {
                            _primed = false; // underrun: wait to rebuild a little buffer
                        }
                    }

                    for (int c = 0; c < channels; c++)
                        data[i * channels + c] = s;
                }
            }
        }
    }
}
