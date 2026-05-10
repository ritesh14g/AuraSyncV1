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

        private List<ModelEntry> _models = new List<ModelEntry>
        {
            new ModelEntry { displayName = "House",      fileName = "House.glb",     description = "Residential Structure" },
            new ModelEntry { displayName = "Jet Engine", fileName = "JetEngine.glb", description = "Aircraft Propulsion"   },
        };

        private bool _isOpen = false;
        private Coroutine _fadeCoroutine;

        void Awake()
        {
            // Force hide on startup so it doesn't block the main AR view
            SetPanelActive(false);
            backButton?.onClick.AddListener(ClosePanel);
        }

        public void OpenPanel()
        {
            if (_isOpen) return;
            _isOpen = true;
            
            gameObject.SetActive(true);
            SetPanelActive(true);
            BuildGrid();
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
