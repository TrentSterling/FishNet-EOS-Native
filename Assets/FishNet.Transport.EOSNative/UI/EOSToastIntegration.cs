using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Social;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Automatically shows toast notifications for common EOS events.
    /// Attach to the same GameObject as EOSToastManager or auto-created.
    /// </summary>
    public class EOSToastIntegration : MonoBehaviour
    {
        #region Singleton

        private static EOSToastIntegration _instance;
        public static EOSToastIntegration Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSToastIntegration>();
                    if (_instance == null)
                    {
                        // Attach to ToastManager if it exists
                        var toastManager = EOSToastManager.Instance;
                        if (toastManager != null)
                        {
                            _instance = toastManager.gameObject.AddComponent<EOSToastIntegration>();
                        }
                        else
                        {
                            var go = new GameObject("EOSToastIntegration");
                            _instance = go.AddComponent<EOSToastIntegration>();
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Settings

        [Header("Toast Settings")]
        [SerializeField] private bool _showLobbyToasts = true;
        [SerializeField] private bool _showInviteToasts = true;
        [SerializeField] private bool _showFriendToasts = true;
        [SerializeField] private bool _showConnectionToasts = true;

        #endregion

        #region Public Properties

        public bool ShowLobbyToasts { get => _showLobbyToasts; set => _showLobbyToasts = value; }
        public bool ShowInviteToasts { get => _showInviteToasts; set => _showInviteToasts = value; }
        public bool ShowFriendToasts { get => _showFriendToasts; set => _showFriendToasts = value; }
        public bool ShowConnectionToasts { get => _showConnectionToasts; set => _showConnectionToasts = value; }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            SubscribeToEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Event Subscriptions

        private void SubscribeToEvents()
        {
            // Lobby events
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined += OnLobbyJoined;
                lobbyManager.OnLobbyLeft += OnLobbyLeft;
                lobbyManager.OnMemberJoined += OnMemberJoined;
                lobbyManager.OnMemberLeft += OnMemberLeft;
                lobbyManager.OnOwnerChanged += OnOwnerChanged;
            }

            // Invite events
            var invitesManager = EOSCustomInvites.Instance;
            if (invitesManager != null)
            {
                invitesManager.OnInviteReceived += OnInviteReceived;
            }

            // Friend events
            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                registry.OnFriendChanged += OnFriendChanged;
            }
        }

        private void UnsubscribeFromEvents()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined -= OnLobbyJoined;
                lobbyManager.OnLobbyLeft -= OnLobbyLeft;
                lobbyManager.OnMemberJoined -= OnMemberJoined;
                lobbyManager.OnMemberLeft -= OnMemberLeft;
                lobbyManager.OnOwnerChanged -= OnOwnerChanged;
            }

            var invitesManager = EOSCustomInvites.Instance;
            if (invitesManager != null)
            {
                invitesManager.OnInviteReceived -= OnInviteReceived;
            }

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                registry.OnFriendChanged -= OnFriendChanged;
            }
        }

        #endregion

        #region Event Handlers

        private void OnLobbyJoined(LobbyData lobby)
        {
            if (!_showLobbyToasts) return;
            EOSToastManager.Success("Lobby Joined", $"Code: {lobby.JoinCode}");
        }

        private void OnLobbyLeft()
        {
            if (!_showLobbyToasts) return;
            EOSToastManager.Info("Left Lobby");
        }

        private void OnMemberJoined(LobbyMemberData member)
        {
            if (!_showLobbyToasts) return;

            string name = member.DisplayName;
            if (string.IsNullOrEmpty(name))
            {
                name = EOSPlayerRegistry.Instance?.GetPlayerName(member.Puid) ?? "Player";
            }

            EOSToastManager.Info("Player Joined", name);
        }

        private void OnMemberLeft(string puid)
        {
            if (!_showLobbyToasts) return;

            string name = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Player";
            EOSToastManager.Info("Player Left", name);
        }

        private void OnOwnerChanged(string newOwnerPuid)
        {
            if (!_showLobbyToasts) return;

            string name = EOSPlayerRegistry.Instance?.GetPlayerName(newOwnerPuid) ?? "Player";
            EOSToastManager.Warning("Host Migration", $"{name} is now host");
        }

        private void OnInviteReceived(InviteData invite)
        {
            if (!_showInviteToasts) return;

            string senderId = invite.SenderId?.ToString() ?? "";
            string name = EOSPlayerRegistry.Instance?.GetPlayerName(senderId) ?? "Player";
            EOSToastManager.Info("Invite Received", $"From: {name}");
        }

        private void OnFriendChanged(string puid, bool isNowFriend)
        {
            if (!_showFriendToasts) return;

            string name = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Player";
            if (isNowFriend)
            {
                EOSToastManager.Success("Friend Added", name);
            }
            else
            {
                EOSToastManager.Info("Friend Removed", name);
            }
        }

        #endregion
    }
}
