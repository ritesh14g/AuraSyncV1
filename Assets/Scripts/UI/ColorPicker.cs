using System;
using UnityEngine;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// Reusable color picker component with a preset palette.
    /// 
    /// SETUP:
    /// - Attach to a panel containing color swatch buttons.
    /// - Each swatch button must have an Image component.
    /// - Assign all swatch Images to the 'colorSwatches' array in Inspector.
    /// - The selected swatch gets a highlight border.
    /// </summary>
    public class ColorPicker : MonoBehaviour
    {
        [Header("Color Palette")]
        [SerializeField] private Color[] presetColors = new Color[]
        {
            new Color(1.00f, 0.88f, 0.40f),  // Warm Yellow
            new Color(1.00f, 0.60f, 0.40f),  // Coral
            new Color(0.98f, 0.45f, 0.45f),  // Soft Red
            new Color(0.70f, 0.55f, 0.90f),  // Lavender
            new Color(0.45f, 0.70f, 0.95f),  // Sky Blue
            new Color(0.40f, 0.85f, 0.75f),  // Teal
            new Color(0.60f, 0.90f, 0.50f),  // Lime Green
            new Color(0.95f, 0.95f, 0.95f),  // White
            new Color(0.30f, 0.30f, 0.30f),  // Dark Gray
            new Color(0.10f, 0.10f, 0.10f),  // Near Black
        };

        [Header("UI References")]
        [SerializeField] private Transform swatchContainer;
        [SerializeField] private GameObject swatchPrefab; // Button with Image component
        [SerializeField] private Image selectedHighlight;

        // ── Events ──
        public event Action<Color> OnColorSelected;

        // ── State ──
        private int _selectedIndex = 0;
        private Button[] _swatchButtons;

        public Color SelectedColor => presetColors[_selectedIndex];

        public string SelectedColorHex
        {
            get => "#" + ColorUtility.ToHtmlStringRGB(SelectedColor);
        }

        void Awake()
        {
            BuildSwatches();
        }

        /// <summary>Build swatch buttons from preset colors.</summary>
        private void BuildSwatches()
        {
            if (swatchContainer == null || swatchPrefab == null) return;

            _swatchButtons = new Button[presetColors.Length];

            for (int i = 0; i < presetColors.Length; i++)
            {
                GameObject swatch = Instantiate(swatchPrefab, swatchContainer);
                Image img = swatch.GetComponent<Image>();
                if (img != null)
                {
                    img.color = presetColors[i];
                }

                Button btn = swatch.GetComponent<Button>();
                if (btn != null)
                {
                    int index = i; // capture for closure
                    btn.onClick.AddListener(() => SelectColor(index));
                    _swatchButtons[i] = btn;
                }
            }

            // Select first by default
            UpdateHighlight(0);
        }

        /// <summary>Select a color by index.</summary>
        public void SelectColor(int index)
        {
            if (index < 0 || index >= presetColors.Length) return;

            _selectedIndex = index;
            UpdateHighlight(index);
            OnColorSelected?.Invoke(presetColors[index]);
        }

        /// <summary>Set color from hex string (for re-editing).</summary>
        public void SetColorFromHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color color))
            {
                // Find closest match in preset
                float closest = float.MaxValue;
                int closestIndex = 0;

                for (int i = 0; i < presetColors.Length; i++)
                {
                    float dist = ColorDistance(color, presetColors[i]);
                    if (dist < closest)
                    {
                        closest = dist;
                        closestIndex = i;
                    }
                }

                SelectColor(closestIndex);
            }
        }

        private void UpdateHighlight(int index)
        {
            if (selectedHighlight == null || _swatchButtons == null) return;

            // Move highlight to the selected swatch
            RectTransform swatchRect = _swatchButtons[index].GetComponent<RectTransform>();
            if (swatchRect != null)
            {
                selectedHighlight.rectTransform.position = swatchRect.position;
                selectedHighlight.gameObject.SetActive(true);
            }
        }

        private float ColorDistance(Color a, Color b)
        {
            return Mathf.Pow(a.r - b.r, 2) + Mathf.Pow(a.g - b.g, 2) + Mathf.Pow(a.b - b.b, 2);
        }
    }
}
