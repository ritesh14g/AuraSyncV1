using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

namespace AuraSync.Networking
{
    /// <summary>
    /// AR gaze presence: shows where the other players' cameras are.
    ///
    /// Each phone has its own AR world coordinates, so positions are exchanged relative to the
    /// room's shared origin (a Cloud Anchor, see SharedSpaceOrigin). Until that origin exists on
    /// this device, nothing is sent and remote pointers stay hidden: showing them at raw
    /// coordinates would put them in the wrong place.
    /// In the Space scene there is no shared origin, so this stays idle there; the model-surface
    /// pointers (CanvaPointerManager) take over.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public class UserPresence : MonoBehaviourPunCallbacks
    {
        public static UserPresence Instance { get; private set; }

        [Header("Broadcast Settings")]
        [SerializeField] private float broadcastInterval = 0.1f; // 100ms (10Hz)

        [Header("Pointer Prefab (Optional)")]
        [Tooltip("If unassigned, a 3D pointer mesh with label will be generated automatically.")]
        [SerializeField] private GameObject pointerPrefab;

        [Header("Smoothing")]
        [SerializeField] private float smoothing = 15f;

        private class RemotePointer
        {
            public GameObject  go;
            public Vector3     targetLocalPos;     // relative to the shared origin
            public Vector3     targetLocalForward;
            public bool        hasTarget;
        }

        private float _broadcastTimer;
        private readonly Dictionary<int, RemotePointer> _remotePointers = new Dictionary<int, RemotePointer>();

        public static readonly Color[] PointerColors = new Color[]
        {
            new Color(0.25f, 0.60f, 1.00f), // Blue (Player 1)
            new Color(0.95f, 0.30f, 0.30f), // Red (Player 2)
            new Color(0.30f, 0.85f, 0.45f), // Green (Player 3)
            new Color(1.00f, 0.85f, 0.20f), // Yellow (Player 4)
            new Color(0.70f, 0.35f, 0.95f), // Purple (Player 5)
        };

        public static Color ColorForActor(int actorNumber)
        {
            int i = Mathf.Max(0, actorNumber - 1) % PointerColors.Length;
            return PointerColors[i];
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // This PhotonView lives on V1's AuraSyncManagers, which is kept across scenes. In the
            // scene file it carries sceneViewId 1, and every return to V1 loads a fresh copy of that
            // object with the same id. PUN treats a duplicate id as a leftover and *destroys the
            // older object* (PhotonNetwork.RegisterPhotonView -> RemoveInstantiatedGO) - which was
            // the live one, taking ZoneManager, NetworkManager, PersistenceManager and AppStateManager
            // with it. The presenter then stopped being presenter, so picking another model reopened
            // the room's model and nobody followed. Moving the kept view to its own fixed id means
            // the copy's scene id 1 no longer collides; the copy then removes itself as usual.
            // Every device runs this, so 998 matches across the room for the presence RPC.
            var pv = GetComponent<PhotonView>();
            if (pv != null && pv.ViewID != PresenceViewId)
            {
                if (pv.ViewID != 0) pv.ViewID = 0; // unregister the scene id first (PUN refuses id -> id)
                pv.ViewID = PresenceViewId;
            }
        }

        /// <summary>Fixed PhotonView id for presence, the same on every device.</summary>
        public const int PresenceViewId = 998;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public GameObject GetPointerObject(int actorNumber)
        {
            return _remotePointers.TryGetValue(actorNumber, out RemotePointer p) ? p.go : null;
        }

        void Update()
        {
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;

            // Broadcast our own gaze (only meaningful once we know the shared origin).
            _broadcastTimer += Time.deltaTime;
            if (_broadcastTimer >= broadcastInterval)
            {
                _broadcastTimer = 0;
                BroadcastPresence();
            }

            UpdateRemotePointers();
        }

        private void BroadcastPresence()
        {
            if (Camera.main == null || !SharedSpaceOrigin.HasOrigin) return;

            Vector3 pos = SharedSpaceOrigin.ToLocalPoint(Camera.main.transform.position);
            Vector3 fwd = SharedSpaceOrigin.ToLocalDirection(Camera.main.transform.forward);

            float[] posArr = new float[] { pos.x, pos.y, pos.z };
            float[] fwdArr = new float[] { fwd.x, fwd.y, fwd.z };

            photonView.RPC("RpcUpdatePresence", RpcTarget.Others, posArr, fwdArr);
        }

        [PunRPC]
        public void RpcUpdatePresence(float[] pos, float[] fwd, PhotonMessageInfo info)
        {
            if (!SharedSpaceOrigin.HasOrigin) return; // cannot place it correctly yet
            if (pos == null || fwd == null || pos.Length < 3 || fwd.Length < 3) return;

            int actorNumber = info.Sender.ActorNumber;

            if (!_remotePointers.TryGetValue(actorNumber, out RemotePointer remote))
            {
                remote = new RemotePointer();
                remote.go = pointerPrefab != null
                    ? Instantiate(pointerPrefab)
                    : CreateProceduralPointer(actorNumber, info.Sender);

                Renderer r = remote.go.GetComponentInChildren<Renderer>();
                if (r != null) r.material = PointerVisuals.Create(ColorForActor(actorNumber));

                _remotePointers[actorNumber] = remote;
                Debug.Log($"[AuraSync Presence] Created pointer for Actor {actorNumber}");
            }

            remote.targetLocalPos     = new Vector3(pos[0], pos[1], pos[2]);
            remote.targetLocalForward = new Vector3(fwd[0], fwd[1], fwd[2]);

            // Snap the first sample instead of gliding in from the origin.
            if (!remote.hasTarget)
            {
                remote.hasTarget = true;
                remote.go.transform.position = SharedSpaceOrigin.ToWorldPoint(remote.targetLocalPos);
            }
        }

        private void UpdateRemotePointers()
        {
            bool haveOrigin = SharedSpaceOrigin.HasOrigin;
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

            foreach (var kv in _remotePointers)
            {
                RemotePointer p = kv.Value;
                if (p.go == null) continue;

                p.go.SetActive(haveOrigin && p.hasTarget);
                if (!haveOrigin || !p.hasTarget) continue;

                Vector3 worldPos = SharedSpaceOrigin.ToWorldPoint(p.targetLocalPos);
                Vector3 worldFwd = SharedSpaceOrigin.ToWorldDirection(p.targetLocalForward);

                p.go.transform.position = Vector3.Lerp(p.go.transform.position, worldPos, t);
                if (worldFwd.sqrMagnitude > 0.001f)
                {
                    p.go.transform.rotation = Quaternion.Slerp(
                        p.go.transform.rotation, Quaternion.LookRotation(worldFwd), t);
                }
            }
        }

        private GameObject CreateProceduralPointer(int actorNumber, Player player)
        {
            GameObject container = new GameObject($"Pointer_Player_{actorNumber}");

            // Pointer body
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.transform.SetParent(container.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(90, 0, 0);
            body.transform.localScale = new Vector3(0.12f, 0.25f, 0.12f);

            // Remove the collider so pointers never interfere with raycasts
            Collider c = body.GetComponent<Collider>();
            if (c != null) Destroy(c);

            // Name label
            GameObject labelGo = new GameObject("PlayerLabel");
            labelGo.transform.SetParent(container.transform, false);
            labelGo.transform.localPosition = new Vector3(0, 0.2f, 0);

            var text = labelGo.AddComponent<TextMeshPro>();
            bool isHost = NetworkManager.Instance != null && actorNumber == NetworkManager.Instance.AdminActorNumber;
            text.text = NetworkManager.DisplayName(player) + (isHost ? " (Host)" : "");
            text.fontSize = 2.5f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;

            labelGo.AddComponent<Notes.FaceCamera>();

            return container;
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            int actorNumber = otherPlayer.ActorNumber;
            if (_remotePointers.TryGetValue(actorNumber, out RemotePointer pointer))
            {
                if (pointer.go != null) Destroy(pointer.go);
                _remotePointers.Remove(actorNumber);
                Debug.Log($"[AuraSync Presence] Removed pointer for Actor {actorNumber}");
            }
        }

        public override void OnLeftRoom()
        {
            foreach (var kv in _remotePointers)
                if (kv.Value.go != null) Destroy(kv.Value.go);
            _remotePointers.Clear();
        }
    }
}
