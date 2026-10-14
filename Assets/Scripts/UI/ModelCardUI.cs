using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// Represents a single model card in the repository grid.
    /// </summary>
    public class ModelCardUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameLabel;
        [SerializeField] private TextMeshProUGUI descLabel;
        [SerializeField] private Button          cardButton;
        [SerializeField] private Image           cardBackground;

        private const float ConfirmWindowSeconds = 3f;

        private static readonly Color DeleteIdleColor    = new Color(0.45f, 0.14f, 0.14f, 0.95f);
        private static readonly Color DeleteConfirmColor = new Color(0.85f, 0.10f, 0.10f, 1f);

        private string _fileName;
        private Action<string> _onSelected;

        private Action _onDelete;
        private Image _deleteImage;
        private TextMeshProUGUI _deleteLabel;
        private Coroutine _confirmTimer;
        private bool _armed;

        public void Setup(string name, string desc, string fileName, Action<string> onSelected, Action onDelete = null)
        {
            _fileName = fileName;
            _onSelected = onSelected;
            _onDelete = onDelete;

            if (nameLabel) nameLabel.text = name;
            if (descLabel) descLabel.text = desc;

            cardButton?.onClick.RemoveAllListeners();
            cardButton?.onClick.AddListener(() => _onSelected?.Invoke(_fileName));

            // Set specific card theme color as per Day 1 specs
            if (cardBackground)
            {
                cardBackground.color = new Color(1f, 1f, 1f, 0.06f); // Faint white overlay
            }

            if (_onDelete != null) BuildDeleteButton();
        }

        // The card prefab is a scene asset, so the delete control is built here rather than baked in.
        private void BuildDeleteButton()
        {
            GameObject go = new GameObject("DeleteButton", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.62f, 0.05f);
            rt.anchorMax = new Vector2(0.97f, 0.28f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _deleteImage = go.AddComponent<Image>();
            _deleteImage.color = DeleteIdleColor;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = _deleteImage;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(OnDeletePressed);

            GameObject labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            RectTransform lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4f, 0f); lrt.offsetMax = new Vector2(-4f, 0f);

            _deleteLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _deleteLabel.text = "DELETE";
            _deleteLabel.fontStyle = FontStyles.Bold;
            _deleteLabel.color = Color.white;
            _deleteLabel.alignment = TextAlignmentOptions.Center;
            _deleteLabel.enableAutoSizing = true;
            _deleteLabel.fontSizeMin = 8f;
            _deleteLabel.fontSizeMax = 22f;
            _deleteLabel.raycastTarget = false;
        }

        private void OnDeletePressed()
        {
            // First tap arms it, second tap within the window deletes.
            if (!_armed)
            {
                _armed = true;
                _deleteLabel.text = "SURE?";
                _deleteImage.color = DeleteConfirmColor;
                _confirmTimer = StartCoroutine(DisarmAfterDelay());
                return;
            }

            if (_confirmTimer != null) StopCoroutine(_confirmTimer);
            _deleteLabel.text = "...";
            _onDelete?.Invoke();
        }

        private IEnumerator DisarmAfterDelay()
        {
            yield return new WaitForSecondsRealtime(ConfirmWindowSeconds);
            _armed = false;
            if (_deleteLabel != null) _deleteLabel.text = "DELETE";
            if (_deleteImage != null) _deleteImage.color = DeleteIdleColor;
        }

        /// <summary>Called when the delete could not complete, so the card is usable again.</summary>
        public void ResetDelete()
        {
            _armed = false;
            if (_deleteLabel != null) _deleteLabel.text = "FAILED";
            if (_deleteImage != null) _deleteImage.color = DeleteIdleColor;
        }
    }
}
