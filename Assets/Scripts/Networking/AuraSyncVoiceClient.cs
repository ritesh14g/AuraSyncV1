#if PHOTON_VOICE_R4
using System;
using UnityEngine;
using Photon.Voice;
using Photon.Voice.PUN;
using Photon.Voice.Unity;

namespace AuraSync.Networking
{
    /// <summary>
    /// The Photon Voice connection for a room, built entirely in code by <see cref="VoiceChatManager"/>.
    ///
    /// Stock PunVoiceClient only gives a remote voice a Speaker if the sender's voice is tied to a
    /// PhotonView (avatars) or a SpeakerPrefab is set up in the inspector. AuraSync has neither, so
    /// without this override every remote voice would arrive and simply never play. Here each
    /// remote voice gets its own 2D speaker, created on arrival and destroyed when that voice ends.
    ///
    /// The sender puts its PUN actor number in the voice's UserData, so speakers are named after
    /// the PUN player (Photon Voice runs in a separate room with its own actor numbers).
    /// </summary>
    public class AuraSyncVoiceClient : PunVoiceClient
    {
        /// <summary>
        /// How many frames a remote voice is held back before decoding. 0 (decode on arrival) since the
        /// real-time pass of 2026-10-16: holding 3 frames (60 ms) for the XOR recovery packets cost
        /// delay on every word, while the losses actually seen were either single frames, which Opus's
        /// own in-band FEC rebuilds with no extra delay, or multi-second outages that no FEC covers.
        /// </summary>
        public const int RemoteDelayFrames = 0;

        protected override void Awake()
        {
            base.Awake(); // creates VoiceClient
            VoiceClient.SetRemoteVoiceDelayFrames(Codec.AudioOpus, RemoteDelayFrames);
        }

        protected override Speaker InstantiateSpeakerForRemoteVoice(int playerId, byte voiceId, object userData)
        {
            int punActor = userData is int a ? a : -1;
            var go = new GameObject(punActor > 0 ? $"VoiceSpeaker_actor{punActor}"
                                                 : $"VoiceSpeaker_p{playerId}v{voiceId}");
            go.transform.SetParent(transform, false);

            // AudioSource first: Speaker requires one.
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // a call, not a positional sound
            src.priority     = 0;    // never let other sounds steal this voice
            src.playOnAwake  = false;
            src.volume       = VoiceChatManager.IncomingMuted ? 0f : 1f;
            // The voice is written straight into Unity's audio callback (SpeakerAudioFilterRead), and
            // that callback only runs while the source plays something: a silent looping clip.
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            src.clip = AudioClip.Create("voice_carrier", rate, 1, rate, false);
            src.loop = true;
            src.Play();

            // SpeakerAudioFilterRead, not Speaker: the stock Speaker writes decoded audio into an
            // AudioClip from the main thread, at frame rate, and tracks the play head through
            // AudioSource.timeSamples, which only moves in whole mixer buffers. Both add delay and
            // jitter. This one hands frames from the decoder straight to the audio thread.
            var speaker = go.AddComponent<SpeakerAudioFilterRead>();
            speaker.PlayDelayConfig = AdaptivePlayDelay.ConfigFor(AdaptivePlayDelay.StartDelayMs);
            go.AddComponent<OutputLevel>(); // for the on-screen voice line: is this voice actually audible?
            go.AddComponent<AdaptivePlayDelay>().ActorLabel = punActor > 0 ? $"actor {punActor}" : $"p#{playerId}";
            speaker.OnRemoteVoiceRemoveAction += _ => { if (go != null) Destroy(go); };

            Debug.Log($"[AuraSync Voice] Speaker created for PUN actor {punActor} (voice p#{playerId} v#{voiceId}).");
            return speaker;
        }
    }

    /// <summary>
    /// Peak level of what a speaker's AudioSource is actually outputting, over the last ~0.5 s.
    /// Measured on Unity's audio thread after the clip is read, so it shows real playback rather
    /// than frames received.
    /// </summary>
    public class OutputLevel : MonoBehaviour
    {
        /// <summary>Above this, a remote voice counts as audible from this phone's loudspeaker.</summary>
        private const float AudiblePeak = 0.02f;

        // Environment.TickCount of the last audible remote audio, from any speaker. Written on the
        // audio thread, read on the voice encoding thread by VoiceTalkGate's double-talk statistics.
        private static volatile int _lastAudibleTick = Environment.TickCount - 100000;

        /// <summary>True if any remote voice played audibly in the last <paramref name="ms"/> milliseconds.</summary>
        public static bool HeardRemoteWithinMs(int ms) => unchecked(Environment.TickCount - _lastAudibleTick) < ms;

        // Loudest remote playback per 50 ms slot (all speakers), so the send gate can tell how loud the
        // echo of what this phone is playing could be right now. Written on the audio thread.
        private const int SlotMs = 50, Slots = 16;
        private static readonly float[] _slotPeak = new float[Slots];
        private static readonly int[]   _slotId   = new int[Slots];

        /// <summary>Loudest remote playback (sample peak 0..1) over the last <paramref name="ms"/> milliseconds.</summary>
        public static float RecentPlaybackPeak(int ms)
        {
            int now = Environment.TickCount / SlotMs, span = Mathf.Clamp(ms / SlotMs, 1, Slots - 1);
            float max = 0f;
            for (int i = 0; i < Slots; i++)
                if (now - _slotId[i] <= span && _slotPeak[i] > max) max = _slotPeak[i];
            return max;
        }

        private static void NotePlayback(float peak)
        {
            int id = Environment.TickCount / SlotMs, i = ((id % Slots) + Slots) % Slots;
            if (_slotId[i] != id) { _slotId[i] = id; _slotPeak[i] = peak; }
            else if (peak > _slotPeak[i]) _slotPeak[i] = peak;
        }

        private volatile float _peak;
        private float _shown;
        private float _shownAt;

        public float Peak => _shown;

        void Update()
        {
            // Hold each half-second window's peak for display, then start a new window.
            if (Time.unscaledTime - _shownAt < 0.5f) return;
            _shown   = _peak;
            _peak    = 0f;
            _shownAt = Time.unscaledTime;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            // The voice is produced inside the filter chain (SpeakerAudioFilterRead, the component
            // before this one), where AudioSource.volume is not guaranteed to apply: mute it here.
            if (VoiceChatManager.IncomingMuted)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            float frame = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > frame) frame = a;
            }
            if (frame > _peak) _peak = frame;
            NotePlayback(frame);
            if (frame > AudiblePeak) _lastAudibleTick = Environment.TickCount;
        }
    }

    /// <summary>
    /// The microphone, read so that what is sent is always the newest audio.
    ///
    /// Replaces Photon's MicWrapper (found 2026-10-16). That reader keeps a 1 s looping mic clip and
    /// counts laps by seeing the record position go backwards. If the app's main thread stalls for
    /// over a second (a scene change, a model loading - typically right after a viewer joins and the
    /// room's model starts loading), it misses a lap. From then on it reads the oldest audio in the
    /// clip, and that phone's voice is about a second late for the rest of the call. Shorter stalls
    /// were sent too, as a late burst of old audio.
    ///
    /// Here the clip is 4 s, the read position is kept relative to the record position (no lap
    /// counting), and if more than <see cref="MaxBacklogMs"/> has piled up, the backlog is skipped:
    /// a stall costs a moment of audio instead of adding delay.
    /// Read is called on the main thread by Photon Voice's Service, like MicWrapper's.
    /// </summary>
    public class RealtimeMicReader : IAudioReader<float>
    {
        private const int ClipSeconds  = 4;
        // The M31 has 130-200 ms frames now and then; at 150 one of them cost 172 ms of speech
        // (2026-10-16). Up to 250 ms is sent late instead, which the receiver's buffer absorbs.
        public  const int MaxBacklogMs = 250;

        /// <summary>Times any reader skipped a backlog, and the audio skipped (ms), since launch.</summary>
        public static int Skips, SkippedMs;

        private readonly string _device; // null = the default microphone
        private readonly AudioClip _clip;
        private readonly int _len;
        private int _readPos = -1;
        private float[] _tail = new float[0];

        public int SamplingRate { get; private set; }
        public int Channels { get; private set; }
        public string Error { get; private set; }

        public RealtimeMicReader(string device, int wantRate)
        {
            _device = device;
            try
            {
                Microphone.GetDeviceCaps(device, out int min, out int max);
                int rate = (min == 0 && max == 0) ? wantRate : Mathf.Clamp(wantRate, min, max);
                _clip = Microphone.Start(device, true, ClipSeconds, rate);
                if (_clip == null) { Error = "Microphone.Start returned null"; return; }
                _len = _clip.samples;
                SamplingRate = _clip.frequency;
                Channels = Mathf.Max(1, _clip.channels);
                Debug.Log($"[AuraSync Voice] Realtime mic reader: {SamplingRate} Hz, {Channels} ch, {ClipSeconds} s clip.");
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        private int Wrap(int x) => ((x % _len) + _len) % _len;

        public bool Read(float[] buffer)
        {
            if (Error != null || _clip == null) return false;
            int pos = Microphone.GetPosition(_device);
            if (pos < 0) return false;

            int need = buffer.Length / Channels;
            if (_readPos < 0)
            {
                if (pos < need) return false;  // the mic has not produced a frame yet
                _readPos = Wrap(pos - need);   // start at the newest audio
            }

            int avail = Wrap(pos - _readPos);
            int maxBacklog = SamplingRate * MaxBacklogMs / 1000;
            if (avail > maxBacklog + need)
            {
                Skips++;
                SkippedMs += (avail - need) * 1000 / SamplingRate;
                _readPos = Wrap(pos - need);
                avail = need;
            }
            if (avail < need) return false;

            int first = Mathf.Min(need, _len - _readPos);
            if (first == need)
            {
                _clip.GetData(buffer, _readPos);
            }
            else
            {
                // Across the end of the looping clip: two reads.
                int firstLen = first * Channels, restLen = buffer.Length - firstLen;
                if (_tail.Length != firstLen) _tail = new float[firstLen];
                _clip.GetData(_tail, _readPos);
                Array.Copy(_tail, 0, buffer, 0, firstLen);
                var rest = new float[restLen];
                _clip.GetData(rest, 0);
                Array.Copy(rest, 0, buffer, firstLen, restLen);
            }
            _readPos = Wrap(_readPos + need);
            return true;
        }

        public void Dispose()
        {
            if (Microphone.IsRecording(_device)) Microphone.End(_device);
        }
    }

    /// <summary>
    /// Sizes one remote voice's playback buffer to the network it actually arrives over, the way a
    /// phone call's jitter buffer does.
    ///
    /// Found on 2026-10-16: on 4G the round trip swings from 25 ms to 300+ ms, so frames arrive in
    /// clumps. A fixed 150 ms buffer ran dry at every late clump, which was heard as the voice
    /// breaking up. Photon's buffer is not adaptive (after running dry it refills to the same fixed
    /// target), so this measures how late frames arrive and changes the target: up at once when
    /// frames come later than the buffer covers, and down again only after 30 s of calmer arrival,
    /// while that person is not talking, so the change (a playback restart) is not heard.
    /// On good Wi-Fi it settles near <see cref="MinDelayMs"/>.
    /// </summary>
    public class AdaptivePlayDelay : MonoBehaviour
    {
        public const int StartDelayMs = 150; // until the network has been measured
        public const int MinDelayMs   = 80;
        // Cap on the delay this adds. 2026-10-16: an uncapped-ish 600 ms made the call feel laggy;
        // beyond ~450 ms a conversation suffers more from delay than from the odd break-up.
        public const int MaxDelayMs   = 300;
        private const int MarginMs    = 30;  // on top of the worst lateness seen
        private const int WindowSec   = 30;  // lateness history used before lowering
        private const float LeakMsPerFrame = 0.3f; // absorbs clock drift between the two phones (up to 1.5%)

        public string ActorLabel = "?";

        /// <summary>Current playback buffer target, ms.</summary>
        public int DelayMs { get; private set; } = StartDelayMs;
        /// <summary>Worst arrival lateness over the last <see cref="WindowSec"/> s, ms.</summary>
        public int JitterMs { get; private set; }

        public static AudioOutDelayControl.PlayDelayConfig ConfigFor(int lowMs) => new AudioOutDelayControl.PlayDelayConfig
        {
            Low         = lowMs,
            // Real time first (2026-10-16). Photon's defaults let a burst after a stall pile up to a
            // full second of backlog and only played it 5% faster, so the call could stay most of a
            // second behind for half a minute. Now playback speeds up 10% as soon as it is 60 ms
            // behind target, and anything more than 200 ms behind is dropped outright.
            High        = lowMs + 60,
            Max         = lowMs + 200,
            SpeedUpPerc = 10,
        };

        private Speaker _speaker;
        private OutputLevel _level;
        private RemoteVoiceLink _link;

        // Written on the decode thread, read on the main thread.
        private readonly object _lock = new object();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private long  _lastArrivalMs = -1;
        private bool  _restartStream = true;
        private float _late;      // how far behind schedule the stream currently is, ms
        private float _peakLate;  // worst real lateness since the main thread last looked

        // Lateness only counts once the frames after it show it was caught up (2026-10-16). A real
        // network or sender stall is followed by a burst that brings _late back down; a pause in
        // sending (the sender's gate closed and its end-of-stream marker was lost) is followed by
        // frames at the normal pace, so _late just stays up. Measuring each frame against the
        // lowest _late of the ~0.5 s after it counts the first and ignores the second. Before this,
        // pauses read as up to 1.7 s "late" and kept resizing the buffer, and every resize was a hiccup.
        private const int LookaheadFrames = 25;
        private readonly float[] _recent = new float[LookaheadFrames];
        private int _recentStart, _recentCount;
        private float _raiseWantedSince = -1f;

        private readonly float[] _history = new float[WindowSec]; // per-second worst lateness
        private int   _historyPos;
        private float _secondTimer;
        private int   _frameMs = 20;

        void Awake()
        {
            _speaker = GetComponent<Speaker>();
            _level   = GetComponent<OutputLevel>();
        }

        void OnDestroy() => Unhook();

        private void Unhook()
        {
            if (_link != null) _link.FloatFrameDecoded -= OnFrame;
            _link = null;
        }

        // Decode thread. Frames are decoded as they arrive, so the spacing between calls is the
        // spacing at which the network delivers them.
        private void OnFrame(FrameOut<float> frame)
        {
            long now = _clock.ElapsedMilliseconds;
            lock (_lock)
            {
                long gap = _lastArrivalMs < 0 ? 0 : now - _lastArrivalMs;
                // A new stream, or a gap so long it can only be a pause in sending: start over.
                if (_restartStream || gap > 1500)
                {
                    FlushRecent();
                    _late = 0f;
                }
                else _late = Mathf.Max(0f, _late + gap - _frameMs - LeakMsPerFrame);
                PushRecent(_late);
                _lastArrivalMs = now;
                _restartStream = frame.EndOfStream; // the sender paused: the next frame starts a new stream
            }
        }

        // Under _lock. When the window is full, the oldest entry is judged against the lowest value
        // from it onwards: how much of its lateness the following frames actually caught up.
        private void PushRecent(float late)
        {
            if (_recentCount == LookaheadFrames)
            {
                float oldest = _recent[_recentStart], min = late;
                for (int i = 0; i < _recentCount; i++) min = Mathf.Min(min, _recent[(_recentStart + i) % LookaheadFrames]);
                NoteLateness(oldest - min);
                _recent[_recentStart] = late;
                _recentStart = (_recentStart + 1) % LookaheadFrames;
            }
            else
            {
                _recent[(_recentStart + _recentCount) % LookaheadFrames] = late;
                _recentCount++;
            }
        }

        // Under _lock. End of a stream: judge what is left against what followed it within the stream.
        private void FlushRecent()
        {
            for (int i = 0; i < _recentCount; i++)
            {
                float v = _recent[(_recentStart + i) % LookaheadFrames], min = v;
                for (int j = i + 1; j < _recentCount; j++) min = Mathf.Min(min, _recent[(_recentStart + j) % LookaheadFrames]);
                NoteLateness(v - min);
            }
            _recentStart = _recentCount = 0;
        }

        private void NoteLateness(float ms)
        {
            if (ms > _peakLate) _peakLate = ms;
        }

        void Update()
        {
            if (_speaker == null) return;

            // The speaker is linked to its voice after this component is created, and could be re-linked.
            if (_speaker.RemoteVoice != _link)
            {
                Unhook();
                _link = _speaker.RemoteVoice;
                if (_link != null)
                {
                    int fd = _link.VoiceInfo.FrameDurationUs / 1000;
                    _frameMs = fd > 0 ? fd : 20;
                    lock (_lock) { _restartStream = true; _lastArrivalMs = -1; _peakLate = 0f; _recentStart = _recentCount = 0; }
                    _link.FloatFrameDecoded += OnFrame;
                }
            }
            if (_link == null) return;

            _secondTimer += Time.unscaledDeltaTime;
            if (_secondTimer < 1f) return;
            _secondTimer = 0f;

            float second;
            lock (_lock) { second = _peakLate; _peakLate = 0f; }
            _history[_historyPos] = second;
            _historyPos = (_historyPos + 1) % _history.Length;

            // The second-worst second of the window: one freak stall (a sender's hiccup) should cost
            // one break-up, not 30 s of extra delay for everything said afterwards.
            float worst = 0f, second2 = 0f;
            foreach (float v in _history)
            {
                if (v > worst) { second2 = worst; worst = v; }
                else if (v > second2) second2 = v;
            }
            JitterMs = Mathf.RoundToInt(worst);

            int want = Mathf.Clamp(Mathf.CeilToInt((second2 + MarginMs) / 10f) * 10, MinDelayMs, MaxDelayMs);

            // Up: when frames came later than the buffer covers in two separate seconds of the window
            // (or this one is the second of them).
            int wantNow = Mathf.Clamp(Mathf.CeilToInt((Mathf.Min(second, second2) + MarginMs) / 10f) * 10, MinDelayMs, MaxDelayMs);
            bool quiet = _level == null || _level.Peak < 0.05f;

            // Every change restarts playback and loses what is buffered, so it is made in a pause in
            // this person's speech - or after 3 s if no pause comes and the frames keep arriving late.
            if (wantNow > DelayMs + 20)
            {
                if (_raiseWantedSince < 0f) _raiseWantedSince = Time.unscaledTime;
                if (quiet || Time.unscaledTime - _raiseWantedSince >= 3f)
                {
                    _raiseWantedSince = -1f;
                    Apply(wantNow, $"frames arrived up to {second:F0} ms late (again)");
                }
                return;
            }
            _raiseWantedSince = -1f;

            // Down: when the window allows it, while this person is quiet (pauses between sentences).
            if (want < DelayMs - 40 && quiet)
                Apply(want, $"network steadier ({second2:F0} ms, ignoring one {worst:F0} ms spike, in {WindowSec} s)");
        }

        private void Apply(int ms, string why)
        {
            Debug.Log($"[AuraSync Voice] {ActorLabel}: playback buffer {DelayMs} -> {ms} ms ({why}).");
            DelayMs = ms;
            _speaker.PlayDelayConfig = ConfigFor(ms); // restarts playback with the new target
        }
    }

    /// <summary>
    /// Decides which outgoing frames are sent. Replaces WebRTC's voice detector, which made calls
    /// one-way at a time: it ran after the echo canceller, and while the other person's voice played
    /// from this phone's loudspeaker the canceller was busy removing it, so this phone's own speech
    /// came out partly suppressed and the detector (at its most aggressive setting) threw those
    /// frames away. Whoever spoke first owned the line until they stopped.
    ///
    /// This gate looks only at loudness after echo cancelling, noise suppression and gain control,
    /// against two thresholds it learns as it goes:
    ///  - background: the level of the processed signal while nothing plays here. Speech must be
    ///    <see cref="NoiseMargin"/> times louder.
    ///  - leftover echo (while a remote voice plays from this phone's loudspeaker): how much of the
    ///    playback still leaks through the echo canceller, as a fraction of the playback level.
    ///    Speech must be <see cref="EchoMargin"/> times louder than that leak would be right now.
    /// The second one was added on 2026-10-16. On these phones WebRTC's software canceller only
    /// removes ~5-10 dB of the loudspeaker echo (Unity's microphone and output paths add long,
    /// shifting delays), so with the gate always open, each phone sent the other person's voice
    /// straight back: the presenter heard their own voice as a reverb. A voice close to the mic is
    /// far louder than that leak, so talking over each other still gets through.
    ///
    /// It also measures double-talk for the diagnostics: how loud this phone's microphone was
    /// before and after processing while a remote voice was playing, and how much of that was sent.
    /// </summary>
    public class VoiceTalkGate : MonoBehaviour
    {
        [Tooltip("Lowest open level (RMS 0..1 after processing).")]
        public float OpenLevel = 0.004f;
        [Tooltip("Speech must be this many times the learned background level.")]
        public float NoiseMargin = 3f;
        [Tooltip("While a remote voice plays, speech must be this many times the expected leftover echo.")]
        public float EchoMargin = 2.5f;
        [Tooltip("Counts as this player talking, for the speaking badge (RMS after processing).")]
        public float SpeechLevel = 0.015f;
        [Tooltip("Keep sending this long after the level last opened the gate, so pauses between " +
                 "words and quiet word endings are not cut.")]
        public int HangoverMs = 1000;
        [Tooltip("Shorter hold while a remote voice plays, so leftover echo cannot ride on it.")]
        public int HangoverHearingMs = 400;

        private const int EchoTailMs = 700;
        // Leftover echo / playback peak. Seen: 0.002-0.04 on the Galaxy M31 (its canceller works),
        // ~0.05-0.2 on the TECNO. At full playback (0.8) the cap still opens for 0.12 × 0.8 × 2.5 = 0.24.
        private const float MaxCoupling = 0.12f;

        // Learned on the encoding thread (frames arrive one at a time, in order).
        private float _noise    = 0.02f; // processed level with nothing playing and nobody talking
        private float _coupling = 0.05f; // processed level / playback peak, for echo-only frames

        /// <summary>Learned background level (RMS), for diagnostics.</summary>
        public float NoiseFloor => _noise;
        /// <summary>Learned leftover-echo ratio (processed RMS / playback peak), for diagnostics.</summary>
        public float EchoCoupling => _coupling;

        /// <summary>Counters since the last <see cref="TakeStats"/>.</summary>
        public struct Stats
        {
            public int   Frames, Sent;               // frames processed / sent
            public int   FramesHearing, SentHearing; // the same, while a remote voice was audible here
            public float RawPeakHearing;             // mic level before processing, while hearing (speech + echo)
            public float PeakHearing;                // mic level after processing, while hearing (speech that survived)
            public float Peak;                       // mic level after processing, overall
        }

        private readonly object _lock = new object();
        private Stats _stats;
        private float _rawRms; // set by the pre-processor for the frame the gate then sees
        private volatile int _lastOpenTick   = Environment.TickCount - 100000;
        private volatile int _lastSpeechTick = Environment.TickCount - 100000;

        /// <summary>True if this player's processed microphone reached speech level in the last <paramref name="ms"/> ms.</summary>
        public bool SpokeWithinMs(int ms) => unchecked(Environment.TickCount - _lastSpeechTick) < ms;

        public Stats TakeStats()
        {
            lock (_lock) { Stats s = _stats; _stats = default; return s; }
        }

        // Unity message sent by Recorder each time it (re)creates its voice. This component is added
        // after WebRtcAudioDsp, so its post-processor runs after echo cancelling. The probe is a
        // pre-processor, so it sees the raw microphone.
        private void PhotonVoiceCreated(PhotonVoiceCreatedParams p)
        {
            if (p == null || p.Voice == null) return;
            if (p.Voice is LocalVoiceAudioShort s)
            {
                s.AddPreProcessor(new ShortProbe(this));
                s.AddPostProcessor(new ShortGate(this));
            }
            else if (p.Voice is LocalVoiceAudioFloat f)
            {
                f.AddPreProcessor(new FloatProbe(this));
                f.AddPostProcessor(new FloatGate(this));
            }
        }

        private bool Decide(float rms)
        {
            int now = Environment.TickCount;
            // The echo reaches the mic some 100-500 ms after Unity plays it, so "hearing" lasts that
            // much longer than the playback itself, and the echo is compared with the loudest playback
            // over the same span (otherwise each sentence's echo tail would slip out after it).
            bool hearing  = OutputLevel.HeardRemoteWithinMs(EchoTailMs);
            float playback = hearing ? OutputLevel.RecentPlaybackPeak(EchoTailMs) : 0f;

            float threshold = Mathf.Max(OpenLevel, _noise * NoiseMargin);
            if (hearing) threshold = Mathf.Max(threshold, _coupling * playback * EchoMargin);
            bool above = rms >= threshold;

            // Learn only from frames judged not to be speech (fall fast, rise slowly), so talking
            // does not raise the thresholds and the gate cannot lock itself shut.
            if (!hearing)
            {
                if (rms < _noise) _noise = Mathf.Lerp(_noise, rms, 0.05f);
                else if (!above)  _noise = Mathf.Lerp(_noise, rms, 0.01f);
                _noise = Mathf.Clamp(_noise, 0.0005f, 0.1f);
            }
            else if (playback > 0.02f)
            {
                // A slow, symmetric step on frames judged echo: settles near their median ratio.
                // (The first version moved fast towards any non-speech frame, including quiet speech
                // under the threshold. That raised the threshold, which let more speech in, and on the
                // M31 the ratio ran up to 0.47 and cut the viewer's words while the presenter spoke.)
                // The cap keeps it from ever needing more than a normal voice to get through.
                float q = rms / playback;
                if (!above) _coupling = Mathf.Lerp(_coupling, q, 0.01f);
                _coupling = Mathf.Clamp(_coupling, 0.003f, MaxCoupling);
            }

            if (above) _lastOpenTick = now;
            if (above && rms >= SpeechLevel) _lastSpeechTick = now;
            bool send = unchecked(now - _lastOpenTick) <= (hearing ? HangoverHearingMs : HangoverMs);

            lock (_lock)
            {
                _stats.Frames++;
                if (send) _stats.Sent++;
                if (rms > _stats.Peak) _stats.Peak = rms;
                if (hearing)
                {
                    _stats.FramesHearing++;
                    if (send) _stats.SentHearing++;
                    if (rms     > _stats.PeakHearing)    _stats.PeakHearing    = rms;
                    if (_rawRms > _stats.RawPeakHearing) _stats.RawPeakHearing = _rawRms;
                }
            }
            return send;
        }

        private static float Rms(short[] b)
        {
            double acc = 0;
            for (int i = 0; i < b.Length; i++) { double v = b[i] / 32768.0; acc += v * v; }
            return b.Length > 0 ? (float)Math.Sqrt(acc / b.Length) : 0f;
        }

        private static float Rms(float[] b)
        {
            double acc = 0;
            for (int i = 0; i < b.Length; i++) acc += b[i] * b[i];
            return b.Length > 0 ? (float)Math.Sqrt(acc / b.Length) : 0f;
        }

        private class ShortProbe : IProcessor<short>
        {
            private readonly VoiceTalkGate _g;
            public ShortProbe(VoiceTalkGate g) { _g = g; }
            public short[] Process(short[] buf) { _g._rawRms = Rms(buf); return buf; }
            public void Dispose() { }
        }

        private class FloatProbe : IProcessor<float>
        {
            private readonly VoiceTalkGate _g;
            public FloatProbe(VoiceTalkGate g) { _g = g; }
            public float[] Process(float[] buf) { _g._rawRms = Rms(buf); return buf; }
            public void Dispose() { }
        }

        private class ShortGate : IProcessor<short>
        {
            private readonly VoiceTalkGate _g;
            public ShortGate(VoiceTalkGate g) { _g = g; }
            public short[] Process(short[] buf) => _g.Decide(Rms(buf)) ? buf : null;
            public void Dispose() { }
        }

        private class FloatGate : IProcessor<float>
        {
            private readonly VoiceTalkGate _g;
            public FloatGate(VoiceTalkGate g) { _g = g; }
            public float[] Process(float[] buf) => _g.Decide(Rms(buf)) ? buf : null;
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Sits next to the Recorder and turns on Photon Voice's forward error correction when the
    /// outgoing voice is created: every <see cref="Interval"/> frames an extra recovery packet is
    /// sent, so a single packet lost on Wi-Fi is rebuilt instead of heard as a gap.
    /// Costs 1/Interval extra upload (25% at 4) on an already small Opus stream.
    /// </summary>
    public class VoiceFecTuner : MonoBehaviour
    {
        /// <summary>0 = off. XOR recovery only works if the receiver holds frames back (delay); see
        /// <see cref="AuraSyncVoiceClient.RemoteDelayFrames"/>. Opus's in-band FEC stays on.</summary>
        public const int DefaultInterval = 0;
        public int Interval = DefaultInterval;

        // Unity message sent by Recorder each time it (re)creates its voice.
        private void PhotonVoiceCreated(PhotonVoiceCreatedParams p)
        {
            if (p != null && p.Voice != null) p.Voice.FEC = Interval;
        }
    }

    /// <summary>
    /// Sits next to the Recorder and hands the outgoing voice to a session recording (Phase 9):
    /// the same frames the room hears, after echo cancelling and noise suppression, so recording
    /// needs no second microphone. A post-processor runs only while the mic transmits, so nothing
    /// is kept while the presenter is muted; it passes every frame through unchanged.
    /// </summary>
    public class VoiceRecordingTap : MonoBehaviour
    {
        // Unity message sent by Recorder each time it (re)creates its voice.
        private void PhotonVoiceCreated(PhotonVoiceCreatedParams p)
        {
            if (p == null || p.Voice == null) return;
            int rate = p.Voice.Info.SamplingRate, channels = p.Voice.Info.Channels;
            if (p.Voice is Photon.Voice.LocalVoiceAudioFloat f) f.AddPostProcessor(new FloatTap(rate, channels));
            else if (p.Voice is Photon.Voice.LocalVoiceAudioShort s) s.AddPostProcessor(new ShortTap(rate, channels));
        }

        private class FloatTap : Photon.Voice.IProcessor<float>
        {
            private readonly int _rate, _channels;
            public FloatTap(int rate, int channels) { _rate = rate; _channels = channels; }
            public float[] Process(float[] buf) { AuraSync.Recording.VoiceTrack.Feed(buf, _rate, _channels); return buf; }
            public void Dispose() { }
        }

        private class ShortTap : Photon.Voice.IProcessor<short>
        {
            private readonly int _rate, _channels;
            public ShortTap(int rate, int channels) { _rate = rate; _channels = channels; }
            public short[] Process(short[] buf) { AuraSync.Recording.VoiceTrack.Feed(buf, _rate, _channels); return buf; }
            public void Dispose() { }
        }
    }
}
#endif
