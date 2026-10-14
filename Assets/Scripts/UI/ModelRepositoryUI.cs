using System.Collections;
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
            public string id;
            public string displayName;
            public string fileName; // e.g. "House.glb"
            public string description;
        }

        private List<ModelEntry> _models = new List<ModelEntry>();
        private bool _isOpen = false;

        /// <summary>
        /// Set by the 3D viewer's Back button so this panel reopens on arrival, instead of dropping
        /// the user into the bare AR camera and making them navigate back here by hand.
        /// </summary>
        public const string OpenOnLoadKey = "AuraSync.OpenModelListOnLoad";

        void Awake()
        {
            if (backButton != null) backButton.onClick.AddListener(ClosePanel);

            // REPORTS on the model list too, so reports open with no model and no room (Phase 11).
            ReportsUI.AddButtonTo(panel);

            if (ConsumeOpenOnLoadRequest())
            {
                OpenPanel();
                return; // opening already set the panel up; hiding it here would undo that
            }

            // Force hide on startup so it doesn't block the main AR view
            SetPanelActive(false);
        }

        private static bool ConsumeOpenOnLoadRequest()
        {
            if (PlayerPrefs.GetInt(OpenOnLoadKey, 0) != 1) return false;

            PlayerPrefs.DeleteKey(OpenOnLoadKey);
            PlayerPrefs.Save();
            return true;
        }

        public void OpenPanel()
        {
            if (_isOpen) return;
            _isOpen = true;

            gameObject.SetActive(true);
            SetPanelActive(true);

            StartCoroutine(RefreshWhenDatabaseReady());
            Debug.Log("[AuraSync] ModelRepository: Panel Opened");
        }

        /// <summary>
        /// On a fresh scene load this panel can wake before ModelCatalog does, which used to
        /// leave the grid empty with only a warning in the log.
        /// </summary>
        private IEnumerator RefreshWhenDatabaseReady()
        {
            float deadline = Time.unscaledTime + 5f;
            while (ModelCatalog.Instance == null && Time.unscaledTime < deadline) yield return null;

            RefreshModelsFromDatabase();
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
            if (ModelCatalog.Instance == null)
            {
                Debug.LogWarning("[ModelRepositoryUI] ModelCatalog is not in the scene! Cannot load models.");
                return;
            }

            // The default models are no longer seeded from here - they are whatever models.json on
            // Hugging Face lists, so an empty result means the manifest is empty or unreachable and
            // the right response is an empty grid, not writing rows back.
            ModelCatalog.Instance.GetAllModels((catalogue) =>
            {
                _models.Clear();
                foreach (var model in catalogue)
                {
                    _models.Add(new ModelEntry
                    {
                        id = model.id,
                        displayName = model.name,
                        fileName = model.glbUrl,
                        // Entries written before the move still carry their subtitle in thumbnailUrl.
                        description = !string.IsNullOrEmpty(model.description)
                            ? model.description
                            : model.thumbnailUrl
                    });
                }
                BuildGrid();
            });
        }

        // Link this method to your "Create Model" button in the Unity Inspector
        public void CreateNewModel()
        {
            Debug.Log("[ModelRepositoryUI] Creating new model and saving to this device...");

            ScanNaming.BuildTodaysName(scanName =>
            {
                if (this == null || ModelCatalog.Instance == null) return;

                ModelData newModel = new ModelData
                {
                    name = scanName,
                    glbUrl = "NewModel.glb",
                    description = "Newly Captured 3D Model"
                };

                // Saving is a PlayerPrefs write now, so the grid can be rebuilt straight away
                // instead of guessing at how long a round trip takes.
                ModelCatalog.Instance.SaveModel(newModel);
                RefreshModelsFromDatabase();
            });
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
                    ModelEntry captured = entry; // per-card copy for the closure
                    card.Setup(entry.displayName, entry.description, entry.fileName, OnCardSelected,
                               () => DeleteModel(captured, card));
                }
            }
        }

        /// <summary>
        /// Removes a model from the list and frees its downloaded copy on this phone.
        /// This is a device-local action: a scan is deleted outright, while a catalogue model is
        /// only hidden here, since models.json is shared and read-only. Nobody else's list changes.
        /// </summary>
        private void DeleteModel(ModelEntry entry, ModelCardUI card)
        {
            if (ModelCatalog.Instance == null || string.IsNullOrEmpty(entry.id))
            {
                card?.ResetDelete();
                return;
            }

            ModelCatalog.Instance.DeleteModel(entry.id, ok =>
            {
                if (this == null) return;

                if (!ok)
                {
                    if (card != null) card.ResetDelete();
                    return;
                }

                AuraSync.Space.SpaceSceneManager.DeleteCachedModel(entry.fileName);

                // Don't leave the viewer pointing at a model that no longer exists.
                if (PlayerPrefs.GetString("SelectedModelFile", "") == entry.fileName)
                {
                    PlayerPrefs.DeleteKey("SelectedModelFile");
                    PlayerPrefs.Save();
                }

                _models.Remove(entry);
                BuildGrid();
            });
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
