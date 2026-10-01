using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace AuraSync.UI
{
    /// <summary>
    /// Displays a repository of 3D models available to load into SPACE.
    /// </summary>
    public class ModelRepositoryUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private RectTransform panel;
        [SerializeField] private CanvasGroup   panelGroup;
        [SerializeField] private Button        backButton;

        [Header("Grid")]
        [SerializeField] private Transform     gridContainer;
        [SerializeField] private GameObject    modelCardPrefab;

        [Header("Scene Loading")]
        [SerializeField] private string spaceSceneName = "SpaceScene";

        [System.Serializable]
        private class ModelEntry 
        {
            public string displayName;
            public string fileName; // e.g. "House.glb"
            public string description;
        }

        private List<ModelEntry> _models = new List<ModelEntry>();
        private bool _isOpen = false;
        private bool _isInitialized = false;

        void Awake()
        {
            // Force hide on startup so it doesn't block the main AR view
            SetPanelActive(false);
            if (backButton != null) backButton.onClick.AddListener(ClosePanel);
        }

        public void OpenPanel()
        {
            if (_isOpen) return;
            _isOpen = true;
            
            gameObject.SetActive(true);
            SetPanelActive(true);
            
            // Refresh models from Firestore when panel opens
            RefreshModelsFromDatabase();
            Debug.Log("[AuraSync] ModelRepository: Panel Opened");
        }

        public void ClosePanel()
        {
            if (!_isOpen) return;
            _isOpen = false;
            SetPanelActive(false);
            Debug.Log("[AuraSync] ModelRepository: Panel Closed");
        }

        private void SetPanelActive(bool active)
        {
            if (panelGroup != null)
            {
                panelGroup.alpha = active ? 1f : 0f;
                panelGroup.interactable = active;
                panelGroup.blocksRaycasts = active;
            }
            
            // Also toggle the GameObject if no group exists, or as a safety
            if (!active && panelGroup == null) gameObject.SetActive(false);
        }

        public void RefreshModelsFromDatabase()
        {
            if (FirestoreManager.Instance == null)
            {
                Debug.LogWarning("[ModelRepositoryUI] FirestoreManager is not in the scene! Cannot load models.");
                return;
            }

            // Fetch from database
            FirestoreManager.Instance.GetAllModels((dbModels) => 
            {
                // If database is empty, seed it with the default models so they are stored there
                if (dbModels.Count == 0 && !_isInitialized)
                {
                    _isInitialized = true;
                    Debug.Log("[ModelRepositoryUI] Database is empty. Seeding default models...");
                    SeedDefaultModels();
                    return; // SeedDefaultModels will trigger a refresh when done
                }

                _models.Clear();
                foreach (var dbm in dbModels)
                {
                    _models.Add(new ModelEntry 
                    { 
                        displayName = dbm.name, 
                        fileName = dbm.glbUrl, 
                        description = dbm.thumbnailUrl 
                    });
                }
                BuildGrid();
            });
        }

        private void SeedDefaultModels()
        {
            ModelData house = new ModelData { name = "House", glbUrl = "House.glb", thumbnailUrl = "Residential Structure" };
            ModelData jet = new ModelData { name = "Jet Engine", glbUrl = "JetEngine.glb", thumbnailUrl = "Aircraft Propulsion" };
            
            FirestoreManager.Instance.SaveModel(house);
            FirestoreManager.Instance.SaveModel(jet);

            // Wait a moment for save to complete, then refresh
            Invoke(nameof(RefreshModelsFromDatabase), 1.5f);
        }

        // Link this method to your "Create Model" button in the Unity Inspector
        public void CreateNewModel()
        {
            Debug.Log("[ModelRepositoryUI] Creating new model and saving to database...");
            ModelData newModel = new ModelData 
            { 
                name = "New Scan " + Random.Range(10, 99), 
                glbUrl = "NewModel.glb", 
                thumbnailUrl = "Newly Captured 3D Model" 
            };
            
            FirestoreManager.Instance.SaveModel(newModel);
            
            // Wait briefly for save to complete before refreshing UI to show the new model
            Invoke(nameof(RefreshModelsFromDatabase), 1.0f);
        }

        private void BuildGrid()
        {
            if (gridContainer == null || modelCardPrefab == null) return;

            // Clear existing
            foreach (Transform child in gridContainer)
            {
                Destroy(child.gameObject);
            }

            // Build new
            foreach (var entry in _models)
            {
                var go = Instantiate(modelCardPrefab, gridContainer);
                var card = go.GetComponent<ModelCardUI>();
                if (card != null)
                {
                    card.Setup(entry.displayName, entry.description, entry.fileName, OnCardSelected);
                }
            }
        }

        private void OnCardSelected(string fileName)
        {
            Debug.Log($"[AuraSync] ModelRepository: Selected {fileName}. Loading {spaceSceneName}...");
            
            // Store selection for the next scene to read
            PlayerPrefs.SetString("SelectedModelFile", fileName);
            PlayerPrefs.Save();

            // Load the SPACE scene
            SceneManager.LoadScene(spaceSceneName);
        }
    }
}
