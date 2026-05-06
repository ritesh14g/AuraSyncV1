using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Google.XR.ARCoreExtensions;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(ARAnchorManager))]
    public class CloudAnchorManager : MonoBehaviour
    {
        public static CloudAnchorManager Instance { get; private set; }

        private ARAnchorManager _anchorManager;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _anchorManager = GetComponent<ARAnchorManager>();
        }

        /// <summary>
        /// Hosts a local ARAnchor to the Google Cloud.
        /// Once successful, invokes the callback with the new Cloud Anchor ID.
        /// </summary>
        public void HostAnchor(ARAnchor localAnchor, Action<string> onSuccess, Action<string> onFailure)
        {
            if (localAnchor == null)
            {
                onFailure?.Invoke("Local anchor is null.");
                return;
            }

            StartCoroutine(HostAnchorRoutine(localAnchor, onSuccess, onFailure));
        }

        private IEnumerator HostAnchorRoutine(ARAnchor localAnchor, Action<string> onSuccess, Action<string> onFailure)
        {
            // The integer is the Time-to-Live in days. For API Key auth, 1 day is the max.
            HostCloudAnchorPromise promise = _anchorManager.HostCloudAnchorAsync(localAnchor, 1);
            
            yield return promise;

            var result = promise.Result;
            if (promise.State == PromiseState.Done && result.CloudAnchorState == CloudAnchorState.Success)
            {
                Debug.Log($"[AuraSync] Cloud Anchor Hosted Successfully! ID: {result.CloudAnchorId}");
                onSuccess?.Invoke(result.CloudAnchorId);
            }
            else
            {
                Debug.LogError($"[AuraSync] Failed to host Cloud Anchor. State: {result.CloudAnchorState}");
                onFailure?.Invoke(result.CloudAnchorState.ToString());
            }
        }

        /// <summary>
        /// Resolves a Cloud Anchor ID from the network into a local Transform in the physical room.
        /// Once successful, invokes the callback with the resolved Transform.
        /// </summary>
        public void ResolveAnchor(string cloudAnchorId, Action<Transform> onSuccess, Action<string> onFailure)
        {
            if (string.IsNullOrEmpty(cloudAnchorId))
            {
                onFailure?.Invoke("Cloud Anchor ID is empty.");
                return;
            }

            StartCoroutine(ResolveAnchorRoutine(cloudAnchorId, onSuccess, onFailure));
        }

        private IEnumerator ResolveAnchorRoutine(string cloudAnchorId, Action<Transform> onSuccess, Action<string> onFailure)
        {
            ResolveCloudAnchorPromise promise = _anchorManager.ResolveCloudAnchorAsync(cloudAnchorId);
            
            yield return promise;

            var result = promise.Result;
            if (promise.State == PromiseState.Done && result.CloudAnchorState == CloudAnchorState.Success)
            {
                Debug.Log($"[AuraSync] Cloud Anchor Resolved Successfully! ID: {cloudAnchorId}");
                onSuccess?.Invoke(result.Anchor.transform);
            }
            else
            {
                Debug.LogError($"[AuraSync] Failed to resolve Cloud Anchor. State: {result.CloudAnchorState}");
                onFailure?.Invoke(result.CloudAnchorState.ToString());
            }
        }
    }
}
