using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace AuraSync.Networking
{
    /// <summary>
    /// A shared reference frame for AR content.
    ///
    /// Every phone has its own AR world coordinates, so raw positions (camera gaze, pointers)
    /// mean nothing on another device. A Cloud Anchor is the one thing every device resolves to
    /// the same physical spot. The first anchor the admin hosts in a room becomes the room's
    /// origin; its ID is stored in a room property. Each device registers the anchors it hosts or
    /// resolves, and once the origin ID matches one of them, positions can be exchanged relative
    /// to it.
    /// </summary>
    public static class SharedSpaceOrigin
    {
        public const string KeyOriginAnchor = RoomKeys.OriginAnchor;

        private static readonly Dictionary<string, Transform> _anchors = new Dictionary<string, Transform>();

        public static string    OriginId { get; private set; }
        public static Transform Origin   { get; private set; }
        public static bool      HasOrigin => Origin != null;

        public static event Action OnOriginChanged;

        /// <summary>Remember a hosted or resolved anchor so it can become the origin.</summary>
        public static void RegisterAnchor(string cloudAnchorId, Transform anchorTransform)
        {
            if (string.IsNullOrEmpty(cloudAnchorId) || anchorTransform == null) return;
            _anchors[cloudAnchorId] = anchorTransform;
            Refresh();
        }

        /// <summary>
        /// Admin only: if the room has no origin yet, make this anchor the origin.
        /// </summary>
        public static void TryClaimAsAdmin(string cloudAnchorId)
        {
            if (string.IsNullOrEmpty(cloudAnchorId) || !PhotonNetwork.InRoom) return;
            if (ZoneManager.Instance == null || !ZoneManager.Instance.IsAdmin) return;
            if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(KeyOriginAnchor)) return;

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyOriginAnchor, cloudAnchorId } });
            Refresh();
        }

        /// <summary>Re-evaluate against the room property. Call when the room or its properties change.</summary>
        public static void Refresh()
        {
            string id = null;
            if (PhotonNetwork.InRoom &&
                PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(KeyOriginAnchor, out object raw))
            {
                id = raw as string;
            }

            Transform t = null;
            if (!string.IsNullOrEmpty(id)) _anchors.TryGetValue(id, out t);
            if (t == null) id = null;

            if (id == OriginId && t == Origin) return;

            OriginId = id;
            Origin   = t;
            OnOriginChanged?.Invoke();
        }

        public static void Clear()
        {
            _anchors.Clear();
            if (OriginId == null && Origin == null) return;
            OriginId = null;
            Origin   = null;
            OnOriginChanged?.Invoke();
        }

        public static Vector3 ToLocalPoint(Vector3 world) => Origin.InverseTransformPoint(world);
        public static Vector3 ToWorldPoint(Vector3 local) => Origin.TransformPoint(local);
        public static Vector3 ToLocalDirection(Vector3 world) => Origin.InverseTransformDirection(world);
        public static Vector3 ToWorldDirection(Vector3 local) => Origin.TransformDirection(local);
    }
}
