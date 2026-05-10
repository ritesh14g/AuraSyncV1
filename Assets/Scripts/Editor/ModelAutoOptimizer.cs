using UnityEditor;
using UnityEngine;
using UnityMeshSimplifier;

namespace AuraSync.Editor
{
    /// <summary>
    /// This script runs inside the Unity Editor.
    /// It automatically simplifies any mesh that is imported into the project.
    /// </summary>
    public class ModelAutoOptimizer : AssetPostprocessor
    {
        // This runs automatically whenever a new 3D model is imported
        void OnPostprocessModel(GameObject g)
        {
            // We ONLY want to target your custom models in StreamingAssets.
            // This prevents us from accidentally "crunching" Photon or other assets.
            if (!assetPath.Contains("Assets/StreamingAssets/Models/")) return;

            Debug.Log("[AuraSync Optimizer] Automatically simplifying: " + assetPath);

            MeshFilter[] meshFilters = g.GetComponentsInChildren<MeshFilter>();
            foreach (var filter in meshFilters)
            {
                Mesh sourceMesh = filter.sharedMesh;
                if (sourceMesh == null) continue;

                int beforeCount = sourceMesh.triangles.Length / 3;

                // --- OPTIMIZATION SETTINGS ---
                float quality = 0.1f; 
                var meshSimplifier = new MeshSimplifier();
                meshSimplifier.Initialize(sourceMesh);
                meshSimplifier.SimplifyMesh(quality);

                Mesh newMesh = meshSimplifier.ToMesh();
                int afterCount = newMesh.triangles.Length / 3;

                // Replace the heavy mesh
                filter.sharedMesh = newMesh;

                Debug.Log($"[AuraSync Optimizer] {filter.name}: {beforeCount} tris -> {afterCount} tris");
            }

            Debug.Log("[AuraSync Optimizer] Optimization Complete for: " + g.name);
        }
    }
}
