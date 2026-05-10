// ═══════════════════════════════════════════════════════════════════
// SpaceSceneManager_PATCH.cs

// DO NOT replace your SpaceSceneManager.cs with this file.
// This shows ONLY the changes to make inside your existing file.
// ═══════════════════════════════════════════════════════════════════

// ── CHANGE 1: Add this field at the top of the class ────────────────

//   [SerializeField] private GLTFastMaterialHelper materialHelper;


// ── CHANGE 2: After GLTFast finishes loading ─────────────────────────

// Find wherever your code does something like:

//   await gltf.InstantiateMainSceneAsync(parent);
//   // or
//   gltf.InstantiateScene(parent);
//   // or a callback/event that fires after load

// Immediately AFTER that line, add:

//   materialHelper?.FixAllMaterials(loadedModelRoot);

// Where "loadedModelRoot" is the GameObject that was passed to
// InstantiateMainSceneAsync / InstantiateScene as the parent,
// OR the root of whatever GameObject GLTFast returns.


// ── CHANGE 3: Also wire SpaceInteractionController ───────────────────

// If you have a separate reference to SpaceInteractionController, 
// after load also call:

//   interactionController?.SetModel(loadedModelRoot.transform);


// ── EXAMPLE of what your load method should look like after patch ───

// private async void LoadModel(string path)
// {
//     var gltf = new GLTFast.GltfImport();
//     bool success = await gltf.Load(path);

//     if (!success)
//     {
//         Debug.LogError("[SpaceScene] GLTFast load failed: " + path);
//         return;
//     }

//     GameObject modelRoot = new GameObject("LoadedModel");
//     await gltf.InstantiateMainSceneAsync(modelRoot.transform);

//     // ← PATCH: Fix all materials
//     materialHelper?.FixAllMaterials(modelRoot);

//     // ← PATCH: Wire interaction controller
//     interactionController?.SetModel(modelRoot.transform);

//     // Your existing optimization call stays here unchanged:
//     // optimizer?.OptimizeMeshes(modelRoot);
// }

// ═══════════════════════════════════════════════════════════════════

// NOTHING IN THIS FILE COMPILES — it is documentation only.
// Read it, apply the changes, then delete this file.
