#if PHOTON_VOICE_R4
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
        /// Playback buffer for incoming voices. Photon's default holds 200 ms; 150 ms keeps a call
        /// natural while still riding out ordinary Wi-Fi jitter. High is where playback starts
        /// speeding up slightly to catch up, Max is where old audio is dropped outright.
        /// </summary>
        public static readonly AudioOutDelayControl.PlayDelayConfig PlayDelay = new AudioOutDelayControl.PlayDelayConfig
        {
            Low         = 150,
            High        = 250,
            Max         = 1000,
            SpeedUpPerc = 5,
        };

        protected override Speaker InstantiateSpeakerForRemoteVoice(int playerId, byte voiceId, object userData)
        {
            int punActor = userData is int a ? a : -1;
            var go = new GameObject(punActor > 0 ? $"VoiceSpeaker_actor{punActor}"
                                                 : $"VoiceSpeaker_p{playerId}v{voiceId}");
            go.transform.SetParent(transform, false);

            // AudioSource first: Speaker requires one and reads it when playback starts.
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // a call, not a positional sound
            src.priority     = 0;    // never let other sounds steal this voice
            src.playOnAwake  = false;
            src.volume       = VoiceChatManager.IncomingMuted ? 0f : 1f;

            var speaker = go.AddComponent<Speaker>();
            speaker.PlayDelayConfig = PlayDelay;
            go.AddComponent<OutputLevel>(); // for the on-screen voice line: is this voice actually audible?
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
            float p = _peak;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > p) p = a;
            }
            _peak = p;
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
        public int Interval = 4;

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
