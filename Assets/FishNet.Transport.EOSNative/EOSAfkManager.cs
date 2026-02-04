using System;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Managing;
using FishNet.Connection;
using FishNet.Transport.EOSNative.Lobbies;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Tracks player activity and optionally auto-kicks AFK players.
    /// Attach to same GameObject as EOSNativeTransport.
    /// </summary>
    public class EOSAfkManager : MonoBehaviour
    {
        #region Singleton
        private static EOSAfkManager _instance;
        public static EOSAfkManager Instance => _instance;
        #endregion

        #region Settings
        [Header("AFK Detection")]
        [SerializeField]
        [Tooltip("Enable AFK detection and tracking.")]
        private bool _enabled = true;

        [SerializeField]
        [Tooltip("Time in seconds before a player is considered AFK.")]
        [Range(30f, 600f)]
        private float _afkThreshold = 120f;

        [SerializeField]
        [Tooltip("Time in seconds after AFK warning before auto-kick (0 = no auto-kick).")]
        [Range(0f, 120f)]
        private float _autoKickDelay = 30f;

        [SerializeField]
        [Tooltip("Show warning toast when player goes AFK.")]
        private bool _showAfkWarnings = true;

        [Header("Activity Detection")]
        [SerializeField]
        [Tooltip("Track mouse/keyboard input as activity.")]
        private bool _trackInput = true;

        [SerializeField]
        [Tooltip("Track network messages as activity.")]
        private bool _trackNetworkActivity = true;
        #endregion

        #region Public Properties
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public float AfkThreshold { get => _afkThreshold; set => _afkThreshold = Mathf.Max(30f, value); }
        public float AutoKickDelay { get => _autoKickDelay; set => _autoKickDelay = Mathf.Max(0f, value); }
        public bool ShowAfkWarnings { get => _showAfkWarnings; set => _showAfkWarnings = value; }
        #endregion

        #region Events
        /// <summary>Fired when a player becomes AFK. (connectionId, puid)</summary>
        public event Action<int, string> OnPlayerAfk;

        /// <summary>Fired when a player returns from AFK. (connectionId, puid)</summary>
        public event Action<int, string> OnPlayerReturned;

        /// <summary>Fired when a player is about to be auto-kicked. (connectionId, puid, secondsRemaining)</summary>
        public event Action<int, string, float> OnAutoKickWarning;

        /// <summary>Fired when a player is auto-kicked for AFK. (connectionId, puid)</summary>
        public event Action<int, string> OnPlayerAutoKicked;
        #endregion

        #region Private State
        private class PlayerAfkState
        {
            public float LastActivityTime;
            public bool IsAfk;
            public float AfkStartTime;
            public bool WarningSent;
        }

        private Dictionary<int, PlayerAfkState> _playerStates = new Dictionary<int, PlayerAfkState>();
        private NetworkManager _networkManager;
        private EOSNativeTransport _transport;
        private EOSLobbyManager _lobbyManager;
        private float _lastInputTime;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            _networkManager = GetComponent<NetworkManager>();
            _transport = GetComponent<EOSNativeTransport>();
            _lobbyManager = FindFirstObjectByType<EOSLobbyManager>();

            _lastInputTime = Time.time;

            // Subscribe to connection events
            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }

            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!_enabled) return;

            // Track local input
            if (_trackInput)
            {
                if (Input.anyKey || Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0)
                {
                    _lastInputTime = Time.time;
                    RecordLocalActivity();
                }
            }

            // Only server checks AFK status
            if (_networkManager == null || !_networkManager.IsServerStarted) return;

            UpdateAfkStates();
        }
        #endregion

        #region Public API
        /// <summary>
        /// Record activity for the local player.
        /// Call this when the player performs any meaningful action.
        /// </summary>
        public void RecordLocalActivity()
        {
            _lastInputTime = Time.time;

            // If we're a client, this could be sent to server via RPC
            // For now, local tracking only - server tracks via network activity
        }

        /// <summary>
        /// Record activity for a specific connection (server-side).
        /// </summary>
        public void RecordActivity(int connectionId)
        {
            if (_playerStates.TryGetValue(connectionId, out var state))
            {
                bool wasAfk = state.IsAfk;
                state.LastActivityTime = Time.time;
                state.IsAfk = false;
                state.WarningSent = false;

                if (wasAfk)
                {
                    string puid = GetPuidForConnection(connectionId);
                    OnPlayerReturned?.Invoke(connectionId, puid);

                    if (_showAfkWarnings)
                    {
                        string name = GetPlayerName(puid);
                        EOSToastManager.Info("Player Returned", $"{name} is back");
                    }
                }
            }
        }

        /// <summary>
        /// Check if a player is currently AFK.
        /// </summary>
        public bool IsPlayerAfk(int connectionId)
        {
            return _playerStates.TryGetValue(connectionId, out var state) && state.IsAfk;
        }

        /// <summary>
        /// Get how long a player has been AFK (0 if not AFK).
        /// </summary>
        public float GetAfkDuration(int connectionId)
        {
            if (_playerStates.TryGetValue(connectionId, out var state) && state.IsAfk)
            {
                return Time.time - state.AfkStartTime;
            }
            return 0f;
        }

        /// <summary>
        /// Get time until player will be auto-kicked (-1 if no auto-kick).
        /// </summary>
        public float GetTimeUntilKick(int connectionId)
        {
            if (_autoKickDelay <= 0) return -1f;

            if (_playerStates.TryGetValue(connectionId, out var state) && state.IsAfk)
            {
                float afkDuration = Time.time - state.AfkStartTime;
                float kickTime = _autoKickDelay;
                return Mathf.Max(0f, kickTime - afkDuration);
            }
            return -1f;
        }

        /// <summary>
        /// Get all currently AFK players.
        /// </summary>
        public List<int> GetAfkPlayers()
        {
            var afkPlayers = new List<int>();
            foreach (var kvp in _playerStates)
            {
                if (kvp.Value.IsAfk)
                    afkPlayers.Add(kvp.Key);
            }
            return afkPlayers;
        }

        /// <summary>
        /// Manually kick a player for AFK (host only).
        /// </summary>
        public void KickPlayer(int connectionId, string reason = "AFK")
        {
            if (_networkManager == null || !_networkManager.IsServerStarted) return;

            string puid = GetPuidForConnection(connectionId);
            string name = GetPlayerName(puid);

            _networkManager.ServerManager.Kick(connectionId, FishNet.Managing.Server.KickReason.Unset);

            EOSToastManager.Warning("Player Kicked", $"{name} was kicked: {reason}");
        }
        #endregion

        #region Private Methods
        private void OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Started)
            {
                // New player connected
                _playerStates[conn.ClientId] = new PlayerAfkState
                {
                    LastActivityTime = Time.time,
                    IsAfk = false,
                    AfkStartTime = 0f,
                    WarningSent = false
                };
            }
            else if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
            {
                // Player disconnected
                _playerStates.Remove(conn.ClientId);
            }
        }

        private void UpdateAfkStates()
        {
            float currentTime = Time.time;
            var toKick = new List<int>();

            foreach (var kvp in _playerStates)
            {
                int connectionId = kvp.Key;
                var state = kvp.Value;

                float idleTime = currentTime - state.LastActivityTime;

                if (!state.IsAfk && idleTime >= _afkThreshold)
                {
                    // Player just went AFK
                    state.IsAfk = true;
                    state.AfkStartTime = currentTime;
                    state.WarningSent = false;

                    string puid = GetPuidForConnection(connectionId);
                    OnPlayerAfk?.Invoke(connectionId, puid);

                    if (_showAfkWarnings)
                    {
                        string name = GetPlayerName(puid);
                        if (_autoKickDelay > 0)
                        {
                            EOSToastManager.Warning("Player AFK", $"{name} is AFK (auto-kick in {_autoKickDelay:0}s)");
                        }
                        else
                        {
                            EOSToastManager.Warning("Player AFK", $"{name} is AFK");
                        }
                    }
                }
                else if (state.IsAfk && _autoKickDelay > 0)
                {
                    float afkDuration = currentTime - state.AfkStartTime;
                    float timeUntilKick = _autoKickDelay - afkDuration;

                    // Warning at 10 seconds remaining
                    if (!state.WarningSent && timeUntilKick <= 10f && timeUntilKick > 0f)
                    {
                        state.WarningSent = true;
                        string puid = GetPuidForConnection(connectionId);
                        OnAutoKickWarning?.Invoke(connectionId, puid, timeUntilKick);

                        if (_showAfkWarnings)
                        {
                            string name = GetPlayerName(puid);
                            EOSToastManager.Warning("Auto-Kick Warning", $"{name} will be kicked in {timeUntilKick:0}s");
                        }
                    }

                    // Time to kick
                    if (afkDuration >= _autoKickDelay)
                    {
                        toKick.Add(connectionId);
                    }
                }
            }

            // Kick players outside of iteration
            foreach (int connectionId in toKick)
            {
                string puid = GetPuidForConnection(connectionId);
                OnPlayerAutoKicked?.Invoke(connectionId, puid);
                KickPlayer(connectionId, "AFK too long");
                _playerStates.Remove(connectionId);
            }
        }

        private string GetPuidForConnection(int connectionId)
        {
            if (_transport != null)
            {
                return _transport.GetRemoteProductUserId(connectionId);
            }
            return null;
        }

        private string GetPlayerName(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return $"Player";

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                string name = registry.GetDisplayName(puid);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            return puid.Length > 8 ? puid.Substring(0, 8) + "..." : puid;
        }
        #endregion

        #region Network Activity Tracking
        /// <summary>
        /// Called by transport when receiving data from a client.
        /// </summary>
        internal void OnNetworkDataReceived(int connectionId)
        {
            if (_enabled && _trackNetworkActivity)
            {
                RecordActivity(connectionId);
            }
        }
        #endregion
    }
}
