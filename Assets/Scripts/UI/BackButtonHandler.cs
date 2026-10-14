using System;
using UnityEngine;
using AuraSync.Core;

namespace AuraSync.UI
{
    /// <summary>
    /// Turns the Android back button (Escape in the Editor) into <see cref="PanelNavigator.Back"/>.
    ///
    /// With a panel open, back closes it. With nothing open, <see cref="onEmptyStack"/> runs: in the
    /// 3D viewer that is the same as the on-screen BACK button, leaving to the model list.
    ///
    /// Created by SpaceSceneManager, so it only acts in the 3D viewer. The AR scene keeps its
    /// existing behaviour.
    /// </summary>
    public class BackButtonHandler : MonoBehaviour
    {
        public Action onEmptyStack;

        void Update()
        {
            if (!InputCompat.BackPressedThisFrame) return;

            string before = PanelNavigator.Describe();
            if (PanelNavigator.Back())
            {
                Debug.Log($"[Back] Closed top panel. Stack was: {before}, now: {PanelNavigator.Describe()}");
                return;
            }

            Debug.Log("[Back] No panel open.");
            onEmptyStack?.Invoke();
        }
    }
}
