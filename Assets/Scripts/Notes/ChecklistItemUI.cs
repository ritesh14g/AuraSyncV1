using System;
using UnityEngine;
using TMPro;
using AuraSync.Data;

namespace AuraSync.Notes
{
    /// <summary>
    /// UI component for a single checklist row in a 3D AR note.
    /// Attach to the checklist item prefab.
    /// 
    /// PREFAB STRUCTURE:
    /// - Root: with this script
    ///   - Child "Checkbox": SpriteRenderer (checked/unchecked sprite)
    ///   - Child "Label": TextMeshPro with item text
    ///   - Child "BoxCollider": for tap interaction
    /// </summary>
    public class ChecklistItemUI : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer checkbox;
        [SerializeField] private TextMeshPro   label;
        [SerializeField] private Sprite        checkedSprite;
        [SerializeField] private Sprite        uncheckedSprite;
        [SerializeField] private Color         checkedTextColor  = new Color(0.5f, 0.5f, 0.5f);
        [SerializeField] private Color         uncheckedTextColor = Color.black;

        private ChecklistItem _data;
        private Action _onChanged;
        private BoxCollider _collider;

        void Awake()
        {
            _collider = GetComponent<BoxCollider>();
        }

        /// <summary>Initialize with data and callback.</summary>
        public void Setup(ChecklistItem data, Action onChanged)
        {
            _data = data;
            _onChanged = onChanged;
            Refresh();
        }

        /// <summary>Toggle the checked state (called externally or via tap).</summary>
        public void Toggle()
        {
            if (_data == null) return;
            _data.isChecked = !_data.isChecked;
            Refresh();
            _onChanged?.Invoke();
        }

        private void Refresh()
        {
            if (_data == null) return;

            // Update text
            if (label != null)
            {
                label.text = _data.text;
                label.color = _data.isChecked ? checkedTextColor : uncheckedTextColor;
                // Strikethrough for checked items
                label.fontStyle = _data.isChecked
                    ? FontStyles.Strikethrough
                    : FontStyles.Normal;
            }

            // Update checkbox sprite
            if (checkbox != null)
            {
                checkbox.sprite = _data.isChecked ? checkedSprite : uncheckedSprite;
            }
        }
    }
}
