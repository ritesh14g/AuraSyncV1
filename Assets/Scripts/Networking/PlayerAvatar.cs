using UnityEngine;
using Photon.Pun;

namespace AuraSync.Networking
{
    [RequireComponent(typeof(PhotonView))]
    public class PlayerAvatar : MonoBehaviourPun
    {
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
