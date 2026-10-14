using System.Text.RegularExpressions;
using UnityEngine;
using Photon.Pun;

namespace AuraSync.Networking
{
    /// <summary>
    /// Joins a room when the app is opened from an invite link, e.g.
    ///   aurasync://join?room=AURA-AB12   or   https://aurasync.app/join?room=AURA-AB12
    /// Android still needs an intent filter for the scheme; see V2_BUILD_NOTES.md.
    /// </summary>
    public class DeepLinkHandler : MonoBehaviour
    {
        public static DeepLinkHandler Instance { get; private set; }

        private static readonly Regex RoomParam =
            new Regex(@"room=(AURA-[A-Z0-9]{4})", RegexOptions.IgnoreCase);

        private string _pendingCode;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.deepLinkActivated += HandleLink;
            if (!string.IsNullOrEmpty(Application.absoluteURL)) HandleLink(Application.absoluteURL);
        }

        void OnDestroy()
        {
            Application.deepLinkActivated -= HandleLink;
            if (Instance == this) Instance = null;
        }

        private void HandleLink(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            Match m = RoomParam.Match(url);
            if (!m.Success) return; // not one of our links

            _pendingCode = m.Groups[1].Value.ToUpperInvariant();
            Debug.Log($"[AuraSync DeepLink] Invite for {_pendingCode}");
        }

        void Update()
        {
            if (_pendingCode == null || NetworkManager.Instance == null) return;

            string code = _pendingCode;
            _pendingCode = null;

            if (PhotonNetwork.InRoom) return; // already in a session; ignore
            NetworkManager.Instance.JoinRoom(code);
        }
    }
}
