using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class FirestoreManager : MonoBehaviour {
    public static FirestoreManager Instance;
    
    private const string PROJECT_ID = "aura-sync-ca2b9";
    private const string COLLECTION_NAME = "models";
    private readonly string BASE_URL = $"https://firestore.googleapis.com/v1/projects/{PROJECT_ID}/databases/(default)/documents/{COLLECTION_NAME}";

    private string currentSpaceId = "DefaultSpace";

    private void Awake() {
        if (Instance == null) {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else {
            Destroy(gameObject);
        }
    }

    public void SaveModel(ModelData data) {
        if (string.IsNullOrEmpty(data.id)) {
            data.id = Guid.NewGuid().ToString();
        }
        StartCoroutine(SaveModelCoroutine(data));
    }

    private IEnumerator SaveModelCoroutine(ModelData data) {
        FirestoreDocument doc = new FirestoreDocument();
        doc.fields = new FirestoreFields {
            id = new StringValue { stringValue = data.id },
            name = new StringValue { stringValue = data.name },
            glbUrl = new StringValue { stringValue = data.glbUrl },
            thumbnailUrl = new StringValue { stringValue = data.thumbnailUrl },
            createdAt = new IntegerValue { integerValue = data.createdAt.ToString() }
        };

        string json = JsonUtility.ToJson(doc);
        
        // Use PATCH or POST. If we PATCH with document ID in URL, it creates/updates.
        string url = $"{BASE_URL}/{data.id}";
        
        using (UnityWebRequest request = new UnityWebRequest(url, "PATCH")) {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) {
                Debug.Log($"[FirestoreManager] Successfully saved model: {data.id}");
            } else {
                Debug.LogError($"[FirestoreManager] Error saving model: {request.error}\n{request.downloadHandler.text}");
            }
        }
    }

    public void GetAllModels(Action<List<ModelData>> callback) {
        StartCoroutine(GetAllModelsCoroutine(callback));
    }

    private IEnumerator GetAllModelsCoroutine(Action<List<ModelData>> callback) {
        using (UnityWebRequest request = UnityWebRequest.Get(BASE_URL)) {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) {
                List<ModelData> models = new List<ModelData>();
                string json = request.downloadHandler.text;
                
                FirestoreListResponse response = JsonUtility.FromJson<FirestoreListResponse>(json);
                if (response != null && response.documents != null) {
                    foreach (var doc in response.documents) {
                        if (doc.fields != null) {
                            ModelData data = new ModelData {
                                id = doc.fields.id?.stringValue,
                                name = doc.fields.name?.stringValue,
                                glbUrl = doc.fields.glbUrl?.stringValue,
                                thumbnailUrl = doc.fields.thumbnailUrl?.stringValue,
                                createdAt = long.Parse(doc.fields.createdAt?.integerValue ?? "0")
                            };
                            models.Add(data);
                        }
                    }
                }
                Debug.Log($"[FirestoreManager] Retrieved {models.Count} models.");
                callback?.Invoke(models);
            } else {
                Debug.LogError($"[FirestoreManager] Error getting models: {request.error}\n{request.downloadHandler.text}");
                callback?.Invoke(new List<ModelData>()); // Return empty list on error
            }
        }
    }

    public string GetActiveSpaceId() {
        return currentSpaceId;
    }

    public void SetActiveSpaceId(string spaceId) {
        currentSpaceId = spaceId;
    }

    #region Firestore JSON Serialization Classes
    [Serializable]
    private class FirestoreListResponse {
        public FirestoreDocument[] documents;
    }

    [Serializable]
    private class FirestoreDocument {
        public string name;
        public FirestoreFields fields;
    }

    [Serializable]
    private class FirestoreFields {
        public StringValue id;
        public StringValue name;
        public StringValue glbUrl;
        public StringValue thumbnailUrl;
        public IntegerValue createdAt;
    }

    [Serializable]
    private class StringValue {
        public string stringValue;
    }

    [Serializable]
    private class IntegerValue {
        public string integerValue; // Firestore represents integerValue as string in JSON
    }
    #endregion
}
