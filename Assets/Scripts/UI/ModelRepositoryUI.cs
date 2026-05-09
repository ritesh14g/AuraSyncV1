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

        void Awake()
        {
            HideImmediate();
            backButton?.onClick.AddListener(ClosePanel);
        }

        private void HideImmediate()
        {
            if (panelGroup == null) return;
            panelGroup.alpha = 0;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
        }

        public void OpenPanel()
        {
            if (panelGroup == null) return;
            panelGroup.alpha = 1;
            panelGroup.interactable = true;
            panelGroup.blocksRaycasts = true;
            
            BuildGrid();
            Debug.Log("[AuraSync] ModelRepository: Panel Opened");
        }

        public void ClosePanel()
        {
            if (panelGroup == null) return;
            panelGroup.alpha = 0;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;
            Debug.Log("[AuraSync] ModelRepository: Panel Closed");
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
