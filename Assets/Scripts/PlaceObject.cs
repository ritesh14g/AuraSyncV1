using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class PlaceObject : MonoBehaviour
{
    public GameObject prefab;   // 👈 YE LINE IMPORTANT

    private ARRaycastManager raycastManager;
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();

    void Start()
    {
        raycastManager = GetComponent<ARRaycastManager>();
    }

    void Update()
{
    if (PhotonNetwork.InRoom && Input.touchCount > 0)
    {
        Touch touch = Input.GetTouch(0);

        if (raycastManager.Raycast(touch.position, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose pose = hits[0].pose;

            PhotonNetwork.Instantiate(prefab.name, pose.position, pose.rotation);
        }
    }
}
}
