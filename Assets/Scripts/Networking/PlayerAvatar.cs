using UnityEngine;
using Photon.Pun;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(PhotonView))]
    public class PlayerAvatar : MonoBehaviourPun
    {
        void Start()
        {
            // Hide our own avatar so it doesn't block our camera view!
            if (photonView.IsMine)
            {
                var renderers = GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    r.enabled = false;
                }
            }
        }

        void Update()
        {
            if (photonView.IsMine && Camera.main != null)
            {
                transform.position = Camera.main.transform.position;
                transform.rotation = Camera.main.transform.rotation;
            }
        }
    }
}
