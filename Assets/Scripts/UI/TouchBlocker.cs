using UnityEngine;

namespace AuraSync.UI
{
    /// <summary>
    /// Marks a UI element (for example a modal backdrop) as blocking touches to the 3D model.
    /// Ordinary panels and labels let touches through; buttons and other controls always block.
    /// </summary>
    public class TouchBlocker : MonoBehaviour { }
}
