using System;
using System.Collections;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Automatically attempts to reconnect when disconnected unexpectedly.
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
        private int _maxAttempts = 3;

        [SerializeField]
        [Tooltip("Delay between reconnection attempts in seconds.")]
        [Range(1f, 30f)]
        private float _retryDelay = 3f;

        [SerializeField]
        [Tooltip("Show toast notifications for reconnect status.")]
        private bool _showNotifications = true;
        #endregion

        #region Public Properties
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public int MaxAttempts { get => _maxAttempts; set => _maxAttempts = Mathf.Max(1, value); }
        public float RetryDelay { get => _retryDelay; set => _retryDelay = Mathf.Max(1f, value); }
        public bool ShowNotifications { get => _showNotifications; set => _showNotifications = value; }
        public bool IsReconnecting => _isReconnecting;
        public int CurrentAttempt => _currentAttempt;
        #endregion

        #region Events
        /// <summary>Fired when reconnection attempt starts.</summary>
        public event Action<int> OnReconnectAttempt; // attempt number

        /// <summary>Fired when reconnection succeeds.</summary>
        public event Action OnReconnected;

        /// <summary>Fired when all reconnection attempts fail.</summary>
        public event Action OnReconnectFailed;
        #endregion

        #region Private State
        private NetworkManager _networkManager;
        private EOSNativeTransport _transport;
        private string _lastLobbyCode;
        private bool _wasConnected;
        private bool _isReconnecting;
        private int _currentAttempt;
        private Coroutine _reconnectCoroutine;
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
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            }

            if (_instance == this)
                _instance = null;
        }
        #endregion

        #region Connection Handling
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
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                // Disconnected
                if (_enabled && _wasConnected && !_isReconnecting)
                {
                    // Unexpected disconnect - try to reconnect
                    StartReconnect();
                }
                _wasConnected = false;
            }
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
                yield return new WaitForSeconds(_retryDelay);

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

                    if (_showNotifications)
                    {
                        EOSToastManager.Success("Reconnected!", "Connection restored");
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
        #endregion
    }
}
