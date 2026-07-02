using UnityEngine;
using System;

public class FirestoreTester : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("[FirestoreTester] Press 'S' to Save a dummy model to Firebase.");
        Debug.Log("[FirestoreTester] Press 'L' to Load all models from Firebase.");
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.S))
        {
            Debug.Log("[FirestoreTester] Saving dummy model...");
            ModelData dummyModel = new ModelData
            {
                id = Guid.NewGuid().ToString(),
                name = "Test Model " + UnityEngine.Random.Range(100, 999),
                glbUrl = "https://example.com/test.glb",
                thumbnailUrl = "https://example.com/thumb.png",
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            
            // This calls the SaveModel function we just created
            FirestoreManager.Instance.SaveModel(dummyModel);
        }

        if (Input.GetKeyDown(KeyCode.L))
        {
            Debug.Log("[FirestoreTester] Loading models...");
            
            // This calls the GetAllModels function we just created
            FirestoreManager.Instance.GetAllModels((models) =>
            {
                Debug.Log($"[FirestoreTester] Successfully loaded {models.Count} models!");
                foreach (var model in models)
                {
                    Debug.Log($" - {model.name} (ID: {model.id})");
                }
            });
        }
    }
}
