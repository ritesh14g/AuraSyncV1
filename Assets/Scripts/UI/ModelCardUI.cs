using System;
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

        private string _fileName;
        private Action<string> _onSelected;

        public void Setup(string name, string desc, string fileName, Action<string> onSelected)
        {
            _fileName = fileName;
            _onSelected = onSelected;

            if (nameLabel) nameLabel.text = name;
            if (descLabel) descLabel.text = desc;

            cardButton?.onClick.RemoveAllListeners();
            cardButton?.onClick.AddListener(() => _onSelected?.Invoke(_fileName));

            // Set specific card theme color as per Day 1 specs
            if (cardBackground)
            {
                cardBackground.color = new Color(1f, 1f, 1f, 0.06f); // Faint white overlay
            }
        }
    }
}
