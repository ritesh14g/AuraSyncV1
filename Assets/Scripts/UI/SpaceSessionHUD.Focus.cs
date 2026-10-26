using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AuraSync.UI
{
    /// <summary>
    /// Focus: the eye beside Parts hides every piece of UI so the model has the whole screen.
    /// Turning, zooming and picking parts still work. Only a faint eye stays, in the same place;
    /// tapping it (or Back) brings everything back exactly as it was.
    /// </summary>
    public partial class SpaceSessionHUD
    {
        private const string FocusId = "focus";

        private Canvas _focusCanvas;
        private RectTransform _focusEye;
        private bool _focus;
        private readonly List<Canvas> _hiddenByFocus = new List<Canvas>();

        public bool InFocus => _focus;

        private void BuildFocus()
        {
            var s = MakeTopButton(_leftRow, FocusId, null, AuraIcons.Eye, EnterFocus, SlotKind.TopLeft);
            _slots[FocusId] = s;

            var safe = AuraViewer.CreateCanvas(transform, "FocusCanvas", 140, out _focusCanvas);
            var eye = AuraKit.Box(safe, "ShowUI", AuraTheme.A(AuraTheme.Ink, 0.35f), AuraTheme.Radius, true);
            AuraKit.Border(eye, AuraTheme.A(AuraTheme.Lavender, 0.35f));
            _focusEye = eye.rectTransform;
            AuraKit.Pin(_focusEye, new Vector2(0, 1), new Vector2(0, 1),
                        new Vector2(AuraViewer.Gutter, -AuraViewer.Gutter),
                        new Vector2(AuraViewer.TopButton, AuraViewer.TopButton));
            var ic = AuraKit.Icon(eye.transform, AuraIcons.EyeOff, 56f, AuraTheme.A(AuraTheme.Lavender, 0.75f));
            AuraKit.Pin(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            AuraKit.MakeButton(eye.gameObject, eye, ExitFocus);
            _focusCanvas.gameObject.SetActive(false);
        }

        /// <summary>Hides all UI (every screen canvas) except the faint eye.</summary>
        public void EnterFocus()
        {
            if (_focus) return;
            ViewerPanels.CloseAll();
            _focus = true;

            // The faint eye sits exactly where the eye button was.
            if (_slots.TryGetValue(FocusId, out Slot s) && s.rect != null)
            {
                _focusEye.pivot = s.rect.pivot;
                _focusEye.position = s.rect.position;
                _focusEye.sizeDelta = s.rect.rect.size;
            }

            _hiddenByFocus.Clear();
            foreach (var c in FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas || !c.enabled || c == _focusCanvas) continue;
                if (c.renderMode == RenderMode.WorldSpace) continue;
                c.enabled = false;
                _hiddenByFocus.Add(c);
            }
            _focusCanvas.gameObject.SetActive(true);
            PanelNavigator.Push(FocusId, this, ExitFocus);
        }

        public void ExitFocus()
        {
            if (!_focus) return;
            ExitFocusQuietly();
            PanelNavigator.Remove(FocusId);
        }

        private void ExitFocusQuietly()
        {
            if (!_focus) return;
            _focus = false;
            foreach (var c in _hiddenByFocus) if (c != null) c.enabled = true;
            _hiddenByFocus.Clear();
            if (_focusCanvas != null) _focusCanvas.gameObject.SetActive(false);
        }
    }
}
