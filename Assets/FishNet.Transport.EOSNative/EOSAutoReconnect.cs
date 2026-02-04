using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Object;
using FishNet.Connection;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// How to handle late rejoin when game is in progress
    /// </summary>
    public enum LateRejoinMode
    {
        RejoinAsPlayer,     // Rejoin normally, respawn player
        RejoinAsSpectator,  // Join as spectator instead
        AskUser,            // Prompt user for choice
        Deny                // Don't allow late rejoin
    }

    /// <summary>
    /// Saved state for a disconnected player
    /// </summary>
    [Serializable]
    public class ReconnectSessionData
    {
        public string Puid;
        public string PlayerName;
        public string LobbyCode;
        public string ReconnectToken;
        public float DisconnectTime;
        public float ReservationExpiry;
        public Vector3 LastPosition;
        public Quaternion LastRotation;
        public int Team;
        public int Score;
        public Dictionary<string, string> CustomData;
        public bool IsReserved;

        public ReconnectSessionData()
        {
            CustomData = new Dictionary<string, string>();
            ReconnectToken = Guid.NewGuid().ToString("N")[..16];
        }

        public bool IsExpired => Time.time > ReservationExpiry;
        public float TimeUntilExpiry => Mathf.Max(0, ReservationExpiry - Time.time);
    }

    /// <summary>
    /// Automatically attempts to reconnect when disconnected unexpectedly.
    /// Supports session state preservation, slot reservation, and late rejoin options.
    /// Attach to same GameObject as EOSNativeTransport.
    /// </summary>
    public class EOSAutoReconnect : MonoBehaviour
    {
        #region Singleton
        private static EOSAutoReconnect _instance;
        public static EOSAutoReconnect Instance => _instance;
        #endregion

        #region Settings
        [Header("Auto-Reconnect Settings")]
        [SerializeField]
        [Tooltip("Enable automatic reconnection attempts.")]
        private bool _enabled = true;

        [SerializeField]
        [Tooltip("Maximum number of reconnection attempts.")]
        [Range(1, 10)]
        private int _maxAttempts = 5;

        [SerializeField]
        [Tooltip("Initial delay between reconnection attempts in seconds.")]
        [Range(1f, 10f)]
        private float _initialRetryDelay = 2f;

        [SerializeField]
        [Tooltip("Maximum delay between attempts (for exponential backoff).")]
        [Range(5f, 60f)]
        private float _maxRetryDelay = 30f;

        [SerializeField]
        [Tooltip("Use exponential backoff for retry delays.")]
        private bool _useExponentialBackoff = true;

        [SerializeField]
        [Tooltip("Show toast notifications for reconnect status.")]
        private bool _showNotifications = true;

        [Header("Session Preservation")]
        [SerializeField]
        [Tooltip("Save and restore player state on reconnect.")]
        private bool _preserveSession = true;

        [SerializeField]
        [Tooltip("How long to reserve a slot for disconnected players (seconds).")]
        [Range(30f, 600f)]
        private float _slotReservationTime = 120f;

        [Header("Late Rejoin")]
        [SerializeField]
        [Tooltip("How to handle rejoining when game is in progress.")]
        private LateRejoinMode _lateRejoinMode = LateRejoinMode.RejoinAsPlayer;

        [SerializeField]
        [Tooltip("Allow reconnect if game phase doesn't permit JIP.")]
        private bool _allowReconnectBypassJip = true;
        #endregion

        #region Public Properties
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public int MaxAttempts { get => _maxAttempts; set => _maxAttempts = Mathf.Max(1, value); }
        public float InitialRetryDelay { get => _initialRetryDelay; set => _initialRetryDelay = Mathf.Max(1f, value); }
        public float MaxRetryDelay { get => _maxRetryDelay; set => _maxRetryDelay = value; }
        public bool UseExponentialBackoff { get => _useExponentialBackoff; set => _useExponentialBackoff = value; }
        public bool ShowNotifications { get => _showNotifications; set => _showNotifications = value; }
        public bool PreserveSession { get => _preserveSession; set => _preserveSession = value; }
        public float SlotReservationTime { get => _slotReservationTime; set => _slotReservationTime = value; }
        public LateRejoinMode LateRejoinMode { get => _lateRejoinMode; set => _lateRejoinMode = value; }
        public bool AllowReconnectBypassJip { get => _allowReconnectBypassJip; set => _allowReconnectBypassJip = value; }

        public bool IsReconnecting => _isReconnecting;
        public int CurrentAttempt => _currentAttempt;
        public ReconnectSessionData LocalSession => _localSession;
        public string ReconnectToken => _localSession?.ReconnectToken;
        #endregion

        #region Events
        /// <summary>Fired when reconnection attempt starts (attempt number).</summary>
        public event Action<int> OnReconnectAttempt;

        /// <summary>Fired when reconnection succeeds.</summary>
        public event Action OnReconnected;

        /// <summary>Fired when all reconnection attempts fail.</summary>
        public event Action OnReconnectFailed;

        /// <summary>Fired when session state is saved (on disconnect).</summary>
        public event Action<ReconnectSessionData> OnSessionSaved;

        /// <summary>Fired when session state is restored (on reconnect).</summary>
        public event Action<ReconnectSessionData> OnSessionRestored;

        /// <summary>Fired when a slot is reserved for a disconnected player (server only).</summary>
        public event Action<ReconnectSessionData> OnSlotReserved;

        /// <summary>Fired when a reserved slot expires (server only).</summary>
        public event Action<string> OnSlotExpired;

        /// <summary>Fired when late rejoin choice is needed (for AskUser mode).</summary>
        public event Action<Action<bool>> OnLateRejoinChoice; // callback(true=player, false=spectator)
        #endregion

        #region Private State
        private NetworkManager _networkManager;
        private EOSNativeTransport _transport;
        private string _lastLobbyCode;
        private bool _wasConnected;
        private bool _isReconnecting;
        private int _currentAttempt;
        private Coroutine _reconnectCoroutine;
        private ReconnectSessionData _localSession;

        // Server-side: track reserved slots for disconnected players
        private readonly Dictionary<string, ReconnectSessionData> _reservedSlots = new();
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
            _networkManager = GetComponent<NetworkManager>();
            _transport = GetComponent<EOSNativeTransport>();

            if (_networkManager != null)
            {
                _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
                _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            }
        }

        private void Update()
        {
            // Clean up expired slot reservations (server only)
            if (_networkManager != null && _networkManager.IsServerStarted)
            {
                CleanupExpiredReservations();
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }

            if (_instance == this)
                _instance = null;
        }
        #endregion

        #region Connection Handling (Client)
        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                // Successfully connected - save state
                _wasConnected = true;
                _isReconnecting = false;
                _currentAttempt = 0;

                var lobbyManager = Lobbies.EOSLobbyManager.Instance;
                if (lobbyManager != null && lobbyManager.IsInLobby)
                {
                    _lastLobbyCode = lobbyManager.CurrentLobby.JoinCode;
                }

                // Create session data
                if (_preserveSession)
                {
                    CreateLocalSession();
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // Disconnected
                if (_enabled && _wasConnected && !_isReconnecting)
                {
                    // Save session state before attempting reconnect
                    if (_preserveSession && _localSession != null)
                    {
                        SaveSessionState();
                    }

                    // Unexpected disconnect - try to reconnect
                    StartReconnect();
                }
                _wasConnected = false;
            }
        }

        private void CreateLocalSession()
        {
            var puid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(puid)) return;

            _localSession = new ReconnectSessionData
            {
                Puid = puid,
                PlayerName = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Unknown",
                LobbyCode = _lastLobbyCode,
                DisconnectTime = Time.time
            };
        }

        private void SaveSessionState()
        {
            if (_localSession == null) return;

            // Find local player object and save state
            var localPlayer = FindLocalPlayerObject();
            if (localPlayer != null)
            {
                _localSession.LastPosition = localPlayer.transform.position;
                _localSession.LastRotation = localPlayer.transform.rotation;
            }

            _localSession.DisconnectTime = Time.time;
            OnSessionSaved?.Invoke(_localSession);
        }

        private NetworkObject FindLocalPlayerObject()
        {
            if (_networkManager == null) return null;

            foreach (var nob in _networkManager.ClientManager.Objects.Spawned.Values)
            {
                if (nob.IsOwner && nob.CompareTag("Player"))
                {
                    return nob;
                }
            }
            return null;
        }

        private void StartReconnect()
        {
            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
            }
            _reconnectCoroutine = StartCoroutine(ReconnectCoroutine());
        }

        private IEnumerator ReconnectCoroutine()
        {
            _isReconnecting = true;
            _currentAttempt = 0;
            float currentDelay = _initialRetryDelay;

            if (_showNotifications)
            {
                EOSToastManager.Warning("Disconnected", "Attempting to reconnect...");
            }

            while (_currentAttempt < _maxAttempts)
            {
                _currentAttempt++;

                if (_showNotifications)
                {
                    EOSToastManager.Info("Reconnecting", $"Attempt {_currentAttempt}/{_maxAttempts}...");
                }

                OnReconnectAttempt?.Invoke(_currentAttempt);

                // Wait before attempting
                yield return new WaitForSeconds(currentDelay);

                // Calculate next delay (exponential backoff)
                if (_useExponentialBackoff)
                {
                    currentDelay = Mathf.Min(currentDelay * 1.5f, _maxRetryDelay);
                }

                // Try to reconnect
                bool success = false;

                if (!string.IsNullOrEmpty(_lastLobbyCode) && _transport != null)
                {
                    // Try to rejoin the same lobby
                    var task = _transport.JoinLobbyAsync(_lastLobbyCode, autoConnect: true);
                    yield return new WaitUntil(() => task.IsCompleted);

                    if (task.Result.result == Epic.OnlineServices.Result.Success)
                    {
                        success = true;
                    }
                }

                if (success)
                {
                    _isReconnecting = false;
                    _currentAttempt = 0;

                    // Handle late rejoin mode
                    yield return HandleLateRejoin();

                    if (_showNotifications)
                    {
                        EOSToastManager.Success("Reconnected!", "Connection restored");
                    }

                    // Restore session if available
                    if (_preserveSession && _localSession != null)
                    {
                        OnSessionRestored?.Invoke(_localSession);
                    }

                    OnReconnected?.Invoke();
                    yield break;
                }
            }

            // All attempts failed
            _isReconnecting = false;

            if (_showNotifications)
            {
                EOSToastManager.Error("Connection Lost", "Failed to reconnect after all attempts");
            }

            OnReconnectFailed?.Invoke();
        }

        private IEnumerator HandleLateRejoin()
        {
            // Check if game is in progress
            var backfill = EOSBackfillManager.Instance;
            if (backfill == null) yield break;

            var jipResult = backfill.CanJoinInProgress();

            // If reconnecting player has reservation, bypass JIP check
            if (jipResult != JoinInProgressResult.Allowed && !_allowReconnectBypassJip)
            {
                switch (_lateRejoinMode)
                {
                    case LateRejoinMode.RejoinAsSpectator:
                        if (EOSSpectatorMode.Instance != null)
                        {
                            EOSSpectatorMode.Instance.EnterSpectatorMode();
                        }
                        break;

                    case LateRejoinMode.AskUser:
                        bool? choice = null;
                        OnLateRejoinChoice?.Invoke((asPlayer) => choice = asPlayer);

                        // Wait for user choice (with timeout)
                        float timeout = 10f;
                        while (choice == null && timeout > 0)
                        {
                            timeout -= Time.deltaTime;
                            yield return null;
                        }

                        if (choice == false && EOSSpectatorMode.Instance != null)
                        {
                            EOSSpectatorMode.Instance.EnterSpectatorMode();
                        }
                        break;

                    case LateRejoinMode.Deny:
                        // Disconnect again
                        _transport?.Shutdown();
                        yield break;
                }
            }
        }

        /// <summary>
        /// Cancel any ongoing reconnection attempts.
        /// </summary>
        public void CancelReconnect()
        {
            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }
            _isReconnecting = false;
            _currentAttempt = 0;

            if (_showNotifications)
            {
                EOSToastManager.Info("Reconnect Cancelled", "");
            }
        }

        /// <summary>
        /// Manually trigger a reconnection attempt.
        /// </summary>
        public void TryReconnect()
        {
            if (!_isReconnecting)
            {
                StartReconnect();
            }
        }

        /// <summary>
        /// Manually trigger reconnection to a specific lobby.
        /// </summary>
        public void TryReconnect(string lobbyCode)
        {
            _lastLobbyCode = lobbyCode;
            TryReconnect();
        }
        #endregion

        #region Connection Handling (Server - Slot Reservation)
        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (!_preserveSession) return;

            if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                // Player disconnected - reserve their slot
                var puid = GetPuidForConnection(conn);
                if (!string.IsNullOrEmpty(puid))
                {
                    ReserveSlot(puid, conn);
                }
            }
            else if (args.ConnectionState == RemoteConnectionState.Started)
            {
                // Player connected - check for reservation
                var puid = GetPuidForConnection(conn);
                if (!string.IsNullOrEmpty(puid) && _reservedSlots.TryGetValue(puid, out var session))
                {
                    // Restore their session
                    RestoreReservedSlot(puid, conn, session);
                }
            }
        }

        private string GetPuidForConnection(NetworkConnection conn)
        {
            // Try to get PUID from player registry
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return null;

            foreach (var (puid, _) in registry.RecentlyPlayedWith)
            {
                // This is a simplified lookup - in practice you'd map connection to PUID
                // through your player tracking system
            }

            return null;
        }

        /// <summary>
        /// Reserve a slot for a disconnected player (server only).
        /// </summary>
        public void ReserveSlot(string puid, NetworkConnection conn)
        {
            if (string.IsNullOrEmpty(puid)) return;

            // Find their player object to save state
            NetworkObject playerObject = null;
            foreach (var nob in _networkManager.ServerManager.Objects.Spawned.Values)
            {
                if (nob.Owner == conn && nob.CompareTag("Player"))
                {
                    playerObject = nob;
                    break;
                }
            }

            var session = new ReconnectSessionData
            {
                Puid = puid,
                PlayerName = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Unknown",
                DisconnectTime = Time.time,
                ReservationExpiry = Time.time + _slotReservationTime,
                IsReserved = true
            };

            if (playerObject != null)
            {
                session.LastPosition = playerObject.transform.position;
                session.LastRotation = playerObject.transform.rotation;
            }

            _reservedSlots[puid] = session;
            OnSlotReserved?.Invoke(session);

            Debug.Log($"[EOSAutoReconnect] Reserved slot for {session.PlayerName} ({puid}) for {_slotReservationTime}s");
        }

        /// <summary>
        /// Restore a reserved slot when player reconnects (server only).
        /// </summary>
        private void RestoreReservedSlot(string puid, NetworkConnection conn, ReconnectSessionData session)
        {
            session.IsReserved = false;
            _reservedSlots.Remove(puid);

            OnSessionRestored?.Invoke(session);

            Debug.Log($"[EOSAutoReconnect] Restored slot for {session.PlayerName} ({puid})");
        }

        /// <summary>
        /// Check if a player has a reserved slot.
        /// </summary>
        public bool HasReservedSlot(string puid)
        {
            return _reservedSlots.TryGetValue(puid, out var session) && !session.IsExpired;
        }

        /// <summary>
        /// Get reserved slot data for a player.
        /// </summary>
        public ReconnectSessionData GetReservedSlot(string puid)
        {
            return _reservedSlots.TryGetValue(puid, out var session) && !session.IsExpired ? session : null;
        }

        /// <summary>
        /// Get all currently reserved slots.
        /// </summary>
        public IReadOnlyDictionary<string, ReconnectSessionData> GetReservedSlots()
        {
            return _reservedSlots;
        }

        /// <summary>
        /// Manually release a reserved slot.
        /// </summary>
        public void ReleaseSlot(string puid)
        {
            if (_reservedSlots.Remove(puid))
            {
                OnSlotExpired?.Invoke(puid);
            }
        }

        private void CleanupExpiredReservations()
        {
            var expired = new List<string>();

            foreach (var kvp in _reservedSlots)
            {
                if (kvp.Value.IsExpired)
                {
                    expired.Add(kvp.Key);
                }
            }

            foreach (var puid in expired)
            {
                _reservedSlots.Remove(puid);
                OnSlotExpired?.Invoke(puid);
                Debug.Log($"[EOSAutoReconnect] Slot reservation expired for {puid}");
            }
        }
        #endregion

        #region Session Data Helpers

        /// <summary>
        /// Set custom data on the local session.
        /// </summary>
        public void SetSessionData(string key, string value)
        {
            if (_localSession != null)
            {
                _localSession.CustomData[key] = value;
            }
        }

        /// <summary>
        /// Get custom data from the local session.
        /// </summary>
        public string GetSessionData(string key)
        {
            if (_localSession != null && _localSession.CustomData.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Update local session position (call periodically to keep state fresh).
        /// </summary>
        public void UpdateSessionPosition(Vector3 position, Quaternion rotation)
        {
            if (_localSession != null)
            {
                _localSession.LastPosition = position;
                _localSession.LastRotation = rotation;
            }
        }

        /// <summary>
        /// Update local session team and score.
        /// </summary>
        public void UpdateSessionStats(int team, int score)
        {
            if (_localSession != null)
            {
                _localSession.Team = team;
                _localSession.Score = score;
            }
        }

        #endregion
    }
}
