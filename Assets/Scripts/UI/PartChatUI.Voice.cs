using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AuraSync.AI;

namespace AuraSync.UI
{
    /// <summary>
    /// Speaking a question on the Ask tab: tap the mic, ask, and the question is sent and answered
    /// exactly as if it had been typed - no extra tap. The words heard appear as your message, so
    /// you can see what the AI was asked.
    ///
    /// Uses the same recorder as Find's voice search (SpeechInput) and the same speech-to-text, with
    /// the part's name and the model's part names as a hint. While listening, the question box's
    /// hint shows what is happening and the mic turns rose with a stop square.
    /// </summary>
    public partial class PartChatUI
    {
        private const string AskHint = "Ask about this part…";

        private Button _askMicButton;
        private Image _askMicImage, _askMicIcon;
        private Coroutine _askListening;
        private Coroutine _askHintReset;
        private AiClient.Pending _pendingAskSpeech;

        private void BuildAskMic(Transform row)
        {
            _askMicButton = AuraWidgets.Round(row, AuraIcons.Mic, 104f, false, OnAskMicPressed, out _askMicIcon);
            _askMicImage = (Image)_askMicButton.targetGraphic;
        }

        private void OnAskMicPressed()
        {
            SpeechInput speech = SpeechInput.Ensure();
            if (speech.IsRecording)
            {
                speech.StopRecording();
                return;
            }
            if (_thread == null || _thread.waiting || _pendingAskSpeech != null) return;

            _input.DeactivateInputField();
            _input.text = "";
            ChatThread thread = _thread;

            speech.StartRecording((wav, error) => OnAskRecorded(thread, wav, error));
            if (speech.IsRecording) _askListening = StartCoroutine(ShowAskListening(speech));
        }

        private IEnumerator ShowAskListening(SpeechInput speech)
        {
            SetMicLook(_askMicImage, _askMicIcon, true);
            _sendButton.interactable = false;
            while (speech != null && speech.IsRecording)
            {
                int bars = Mathf.Clamp(Mathf.RoundToInt(speech.Level * 60f), 0, 10);
                SetAskHint($"Listening… {Mathf.Max(0f, SpeechInput.MaxSeconds - speech.Elapsed):0}s  {new string('|', bars)}");
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

            SetAskHint("Working out what you said…");
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
            SetMicLook(_askMicImage, _askMicIcon, false);
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
