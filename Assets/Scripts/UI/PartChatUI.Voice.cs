using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.AI;

namespace AuraSync.UI
{
    /// <summary>
    /// Speaking a question on the ASK tab: tap MIC, ask, and the question is sent and answered
    /// exactly as if it had been typed and sent - no extra tap. The words heard appear as your
    /// message in the conversation, so you can see what the AI was asked.
    ///
    /// Uses the same recorder as FIND's voice search (SpeechInput) and the same speech-to-text
    /// (the AI server's /transcribe), with the part's name and the model's part names as a hint.
    /// While listening and working, the question box's grey hint shows what is happening.
    /// </summary>
    public partial class PartChatUI
    {
        private const string AskHint = "Ask your own question...";

        private Button          _askMicButton;
        private Image           _askMicImage;
        private TextMeshProUGUI _askMicLabel;
        private Coroutine       _askListening;
        private Coroutine       _askHintReset;
        private AiClient.Pending _pendingAskSpeech;

        private void BuildAskMic(Transform row)
        {
            _askMicButton = MakeButton(row, "Mic", "MIC", ButtonColor, OnAskMicPressed, height: 60f, fontSize: 19, minWidth: 92f);
            _askMicImage = _askMicButton.GetComponent<Image>();
            _askMicLabel = _askMicButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void OnAskMicPressed()
        {
            SpeechInput speech = SpeechInput.Ensure();
            if (speech.IsRecording)
            {
                speech.StopRecording(); // the callback carries on from here
                return;
            }
            if (_thread == null || _thread.waiting || _pendingAskSpeech != null) return;

            _input.DeactivateInputField();
            _input.text = ""; // the hint area shows the listening state
            ChatThread thread = _thread;

            speech.StartRecording((wav, error) => OnAskRecorded(thread, wav, error));
            if (speech.IsRecording) _askListening = StartCoroutine(ShowAskListening(speech));
        }

        private IEnumerator ShowAskListening(SpeechInput speech)
        {
            _askMicLabel.text = "STOP";
            _askMicImage.color = ListeningColor;
            _sendButton.interactable = false;
            while (speech != null && speech.IsRecording)
            {
                int bars = Mathf.Clamp(Mathf.RoundToInt(speech.Level * 60f), 0, 10);
                SetAskHint($"Listening... {Mathf.Max(0f, SpeechInput.MaxSeconds - speech.Elapsed):0}s  {new string('|', bars)}");
                yield return null;
            }
            ResetAskMic();
            _askListening = null;
        }

        private void OnAskRecorded(ChatThread thread, byte[] wav, string error)
        {
            if (this == null) return;
            ResetAskMic();

            if (wav == null)
            {
                FlashAskHint(error);
                return;
            }

            SetAskHint("Working out what you said...");
            _askMicButton.interactable = false;
            _sendButton.interactable = false;

            _pendingAskSpeech = AiClient.Ensure().Transcribe(wav, AskVocabulary(thread), r =>
            {
                _pendingAskSpeech = null;
                if (this == null) return;
                _askMicButton.interactable = true;
                _sendButton.interactable = _thread == null || !_thread.waiting;

                if (!r.ok) { FlashAskHint(r.error); return; }
                if (string.IsNullOrWhiteSpace(r.text)) { FlashAskHint("Didn't catch that. Try again, or type."); return; }

                SetAskHint(AskHint);
                // Only send into the conversation it was spoken for, and only if it is still on screen.
                if (!IsOpen || _tab != Tab.Ask || _thread != thread) return;
                Send(r.text, addUserMessage: true);
            });
        }

        /// <summary>The part being discussed and every part name, so technical words are heard right.</summary>
        private string AskVocabulary(ChatThread thread)
        {
            string names = PartSearch.Vocabulary();
            string title = thread != null ? thread.title : "";
            return string.IsNullOrEmpty(title) ? names : $"{title}. {names}";
        }

        private void SetAskHint(string text)
        {
            if (_askHintReset != null) { StopCoroutine(_askHintReset); _askHintReset = null; }
            if (_input != null && _input.placeholder is TextMeshProUGUI hint) hint.text = Esc(text);
        }

        /// <summary>Shows a voice problem in the hint for a few seconds, then puts the normal hint back.</summary>
        private void FlashAskHint(string text)
        {
            SetAskHint(text);
            _askHintReset = StartCoroutine(ResetAskHintLater());
        }

        private IEnumerator ResetAskHintLater()
        {
            yield return new WaitForSecondsRealtime(4f);
            _askHintReset = null;
            SetAskHint(AskHint);
        }

        private void ResetAskMic()
        {
            if (_askMicLabel == null) return;
            _askMicLabel.text = "MIC";
            _askMicImage.color = ButtonColor;
            if (_sendButton != null) _sendButton.interactable = _thread == null || !_thread.waiting;
        }

        /// <summary>Drops any voice question in progress (panel closing or tab changing).</summary>
        private void StopAskListening()
        {
            if (_askListening != null)
            {
                StopCoroutine(_askListening);
                _askListening = null;
                if (SpeechInput.Instance != null && SpeechInput.Instance.IsRecording) SpeechInput.Instance.Cancel();
            }
            _pendingAskSpeech?.Cancel();
            _pendingAskSpeech = null;
            if (_askMicButton != null) _askMicButton.interactable = true;
            ResetAskMic();
            SetAskHint(AskHint);
        }
    }
}
