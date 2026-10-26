using System;

/// <summary>
/// One entry in the model catalogue - either a curated model from the Hugging Face manifest or a
/// scan saved on this device. See <see cref="ModelCatalog"/> for where each kind lives.
/// </summary>
[Serializable]
public class ModelData {
    public string id;
    public string name;

    /// <summary>
    /// Either a full https URL or a path relative to the manifest's `baseUrl`; ModelCatalog expands
    /// the relative form on load, so by the time anything else sees this it is absolute. A bare
    /// filename with no baseUrl still falls through to StreamingAssets/Models, which is what keeps
    /// locally bundled models working.
    /// </summary>
    public string glbUrl;

    /// <summary>Full URL of a preview image, or empty. Not expanded against `baseUrl`.</summary>
    public string thumbnailUrl;

    /// <summary>
    /// The subtitle shown on a model card. This used to be crammed into thumbnailUrl, so entries
    /// written before the Hugging Face move may still carry their text there - readers fall back.
    /// </summary>
    public string description;

    public long createdAt;

    /// <summary>
    /// The model's part knowledge file (labels, functions, motion, assembly order), made by the
    /// Part Labeler in the Unity Editor. Relative to the manifest's `baseUrl`, like glbUrl, and
    /// expanded on load. Empty for models with no AI part features.
    /// </summary>
    public string partsUrl;

    /// <summary>
    /// V3 Phase 13: a much lighter version of the same model (small textures, fewer triangles) with
    /// the IDENTICAL part tree, made by `optimize-model.py --preview`. Shown first on a slow
    /// connection while the full file downloads; relative to `baseUrl` like glbUrl. Empty = none.
    /// </summary>
    public string previewUrl;

    /// <summary>File sizes in bytes (Phase 13), so the app can judge a download before it starts. 0 = unknown.</summary>
    public long previewBytes;
    public long glbBytes;

    /// <summary>
    /// True for models already optimised at publish time (optimize-model.py): the app then skips its
    /// runtime mesh simplifier. That one cuts every mesh over 20k triangles to 30% on the main thread,
    /// which takes minutes on a model with a 475k-triangle plate (the Antikythera) and destroys fine
    /// detail such as engraved text. False/absent keeps the old behaviour (House, Jet Engine).
    /// </summary>
    public bool skipRuntimeSimplify;

    /// <summary>
    /// How the model is turned when it opens, as Euler angles in degrees (x, y, z), for files whose
    /// front faces away from the camera (the watch movement opened showing its case back:
    /// [0, 180, 0]). Applied to the model's anchor, not baked into its parts, so explode
    /// directions, notes and motion axes (all in the model's own space) are unaffected.
    /// Empty = as the file has it.
    /// </summary>
    public float[] openRotation;
}
