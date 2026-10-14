using UnityEngine;
using Photon.Pun;
using AuraSync.Data;

namespace AuraSync.Networking
{
    /// <summary>
    /// Carries edits and deletions of AR notes and objects to the rest of the room.
    ///
    /// Until now only *placement* was shared: if someone retyped a note, recoloured an object or
    /// deleted one, everybody else carried on seeing the old version, and the room quietly drifted
    /// out of agreement with no sign that anything was wrong.
    ///
    /// It hooks into PersistenceManager rather than the individual screens because every edit and
    /// delete already ends up there to be saved - the note editor, the object customiser, the
    /// selection toolbar, the drag handler and long-press delete all included. One chokepoint
    /// means a new screen gets sync for free instead of having to remember to ask for it.
    ///
    /// Private items stay private: they are never sent, exactly as with placement.
    /// </summary>
    public static class ItemSync
    {
        /// <summary>
        /// Set while a change that arrived from the network is being written to disk, so it is not
        /// immediately sent straight back out. Without it, two devices would volley a single edit
        /// between them forever.
        /// </summary>
        public static bool ApplyingRemoteChange { get; set; }

        private static PhotonView View =>
            NetworkManager.Instance != null ? NetworkManager.Instance.GetComponent<PhotonView>() : null;

        private static bool CanSend(PlacedItemData data)
        {
            if (ApplyingRemoteChange) return false;
            if (!PhotonNetwork.InRoom) return false;
            if (data == null || data.isPrivate) return false;

            // An item that was never anchored to the shared space cannot be located on another
            // device, so there is nothing meaningful to update there either.
            return !string.IsNullOrEmpty(data.cloudAnchorId);
        }

        /// <summary>Tells the room that an item's contents changed.</summary>
        public static void BroadcastUpdate(PlacedItemData data)
        {
            if (!CanSend(data)) return;

            PhotonView pv = View;
            if (pv == null) return;

            string rpc = data is NoteItemData ? "RpcUpdateNote" : "RpcUpdateObject";
            pv.RPC(rpc, RpcTarget.Others, JsonUtility.ToJson(data));
        }

        /// <summary>Tells the room that an item is gone.</summary>
        public static void BroadcastDelete(PlacedItemData data)
        {
            if (!CanSend(data)) return;

            PhotonView pv = View;
            if (pv == null) return;

            pv.RPC("RpcDeleteItem", RpcTarget.Others, data.id);
        }
    }
}
