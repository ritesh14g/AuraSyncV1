using UnityEngine;

namespace AuraSync.Space
{
    /// <summary>
    /// RETIRED. This was an early standalone workaround attached directly to "ModelAnchor" in
    /// SpaceScene: it handled its own rotation and pinch input and forced every material to an
    /// unlit shader.
    ///
    /// Both jobs are now done properly elsewhere — input and presenter sync by
    /// <see cref="SpaceInteractionController"/>, materials by GLTFastMaterialHelper — and running
    /// this script alongside them rotated the model a second time, which fought the presenter's
    /// transform and made real-time following look broken.
    ///
    /// The component is kept only so an existing SpaceScene does not lose a script reference. It
    /// now removes itself at startup and does nothing else. Delete it from the ModelAnchor in the
    /// scene whenever convenient.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpaceModelViewerFix : MonoBehaviour
    {
        void Awake()
        {
            Debug.LogWarning("[AuraSync] SpaceModelViewerFix is retired and is removing itself. " +
                             "Model input, presenter sync and material fixes are handled by " +
                             "SpaceInteractionController and GLTFastMaterialHelper. You can delete " +
                             "this component from the ModelAnchor in SpaceScene.");
            Destroy(this);
        }
    }
}
