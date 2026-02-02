using System;
using System.Collections;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.AntiCheatClient;
using Epic.OnlineServices.AntiCheatCommon;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.AntiCheat
{
    /// <summary>
    /// Manages EOS Easy Anti-Cheat (EAC) integration.
    /// Provides client-side protection and peer validation for P2P games.
    /// </summary>
    public class EOSAntiCheatManager : MonoBehaviour
    {
        #region Singleton

        private static EOSAntiCheatManager _instance;
        public static EOSAntiCheatManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSAntiCheatManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSAntiCheatManager");
                        _instance = go.AddComponent<EOSAntiCheatManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when anti-cheat session starts.</summary>
        public event Action OnSessionStarted;

        /// <summary>Fired when anti-cheat session ends.</summary>
        public event Action OnSessionEnded;

        /// <summary>Fired when a local integrity violation is detected.</summary>
        public event Action<AntiCheatClientViolationType, string> OnIntegrityViolation; // type, message

        /// <summary>Fired when action is required against a peer (kick/ban).</summary>
        public event Action<IntPtr, AntiCheatCommonClientAction, string> OnPeerActionRequired; // peerHandle, action, reason

        /// <summary>Fired when a peer's authentication status changes.</summary>
        public event Action<IntPtr, AntiCheatCommonClientAuthStatus> OnPeerAuthStatusChanged; // peerHandle, status

        #endregion

        #region Serialized Fields

        [Header("Settings")]
        [Tooltip("Automatically start anti-cheat session when joining a lobby.")]
        [SerializeField] private bool _autoStartSession = true;

        [Tooltip("Authentication timeout for peer registration (40-120 seconds).")]
        [SerializeField] private int _peerAuthTimeout = 60;

        #endregion

        #region Public Properties

        /// <summary>Whether anti-cheat is initialized and ready.</summary>
        public bool IsReady => _clientInterface != null;

        /// <summary>Whether an anti-cheat session is currently active.</summary>
        public bool IsSessionActive { get; private set; }

        /// <summary>Whether auto-start is enabled.</summary>
        public bool AutoStartSession
        {
            get => _autoStartSession;
            set => _autoStartSession = value;
        }

        /// <summary>Current protection status.</summary>
        public AntiCheatStatus Status { get; private set; } = AntiCheatStatus.NotInitialized;

        /// <summary>Number of registered peers.</summary>
        public int RegisteredPeerCount => _registeredPeers.Count;

        #endregion

        #region Private Fields

        private AntiCheatClientInterface _clientInterface;
        private ProductUserId _localUserId;

        // Notification handles
        private ulong _integrityViolatedNotification;
        private ulong _messageToPeerNotification;
        private ulong _peerActionRequiredNotification;
        private ulong _peerAuthStatusChangedNotification;

        // Peer tracking
        private Dictionary<string, IntPtr> _puidToPeerHandle = new();
        private Dictionary<IntPtr, string> _peerHandleToPuid = new();
        private HashSet<IntPtr> _registeredPeers = new();
        private int _nextPeerHandle = 1;

        // Message queue for outgoing anti-cheat messages
        private Queue<(IntPtr peer, byte[] data)> _outgoingPeerMessages = new();

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
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            StartCoroutine(InitializeCoroutine());
        }

        private IEnumerator InitializeCoroutine()
        {
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _clientInterface = EOSManager.Instance.Platform?.GetAntiCheatClientInterface();
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_clientInterface != null)
            {
                Status = AntiCheatStatus.Initialized;
                SetupNotifications();
                EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSAntiCheatManager", "Initialized");

                // Auto-start if in lobby
                if (_autoStartSession)
                {
                    var lobbyManager = EOSLobbyManager.Instance;
                    if (lobbyManager != null && lobbyManager.IsInLobby)
                    {
                        BeginSession();
                    }

                    // Subscribe to lobby events
                    if (lobbyManager != null)
                    {
                        lobbyManager.OnLobbyJoined += OnLobbyJoined;
                        lobbyManager.OnLobbyLeft += OnLobbyLeft;
                    }
                }
            }
            else
            {
                Status = AntiCheatStatus.NotAvailable;
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager",
                    "AntiCheatClientInterface not available - EAC may not be configured");
            }
        }

        private void OnDestroy()
        {
            if (IsSessionActive)
            {
                EndSession();
            }

            RemoveNotifications();

            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined -= OnLobbyJoined;
                lobbyManager.OnLobbyLeft -= OnLobbyLeft;
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Begin an anti-cheat protected session.
        /// Call when joining a game/lobby.
        /// </summary>
        public Result BeginSession()
        {
            if (!IsReady)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager", "Cannot begin session - not initialized");
                return Result.NotConfigured;
            }

            if (IsSessionActive)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager", "Session already active");
                return Result.AlreadyConfigured;
            }

            var options = new BeginSessionOptions
            {
                LocalUserId = _localUserId,
                Mode = AntiCheatClientMode.PeerToPeer // P2P mode for FishNet host migration support
            };

            var result = _clientInterface.BeginSession(ref options);

            if (result == Result.Success)
            {
                IsSessionActive = true;
                Status = AntiCheatStatus.Protected;
                EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSAntiCheatManager", "Anti-cheat session started");
                OnSessionStarted?.Invoke();
            }
            else
            {
                Status = AntiCheatStatus.Error;
                EOSDebugLogger.LogError(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Failed to begin session: {result}");
            }

            return result;
        }

        /// <summary>
        /// End the current anti-cheat session.
        /// Call when leaving a game/lobby.
        /// </summary>
        public Result EndSession()
        {
            if (!IsSessionActive)
            {
                return Result.NotConfigured;
            }

            // Unregister all peers first
            foreach (var peer in new List<IntPtr>(_registeredPeers))
            {
                UnregisterPeer(peer);
            }

            var options = new EndSessionOptions();
            var result = _clientInterface.EndSession(ref options);

            if (result == Result.Success)
            {
                IsSessionActive = false;
                Status = AntiCheatStatus.Initialized;
                _puidToPeerHandle.Clear();
                _peerHandleToPuid.Clear();
                _registeredPeers.Clear();
                EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSAntiCheatManager", "Anti-cheat session ended");
                OnSessionEnded?.Invoke();
            }
            else
            {
                EOSDebugLogger.LogError(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Failed to end session: {result}");
            }

            return result;
        }

        /// <summary>
        /// Register a peer for anti-cheat validation.
        /// Call when a player joins the game.
        /// </summary>
        /// <param name="puid">The player's PUID.</param>
        /// <param name="ipAddress">Optional IP address for additional validation.</param>
        /// <returns>Handle to the registered peer, or IntPtr.Zero on failure.</returns>
        public IntPtr RegisterPeer(string puid, string ipAddress = null)
        {
            if (!IsSessionActive)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager", "Cannot register peer - no active session");
                return IntPtr.Zero;
            }

            if (_puidToPeerHandle.ContainsKey(puid))
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Peer already registered: {puid}");
                return _puidToPeerHandle[puid];
            }

            // Create a unique handle for this peer
            IntPtr peerHandle = new IntPtr(_nextPeerHandle++);

            // Parse PUID to ProductUserId
            var productUserId = ProductUserId.FromString(puid);
            if (productUserId == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Invalid PUID: {puid}");
                return IntPtr.Zero;
            }

            var options = new RegisterPeerOptions
            {
                PeerHandle = peerHandle,
                ClientType = AntiCheatCommonClientType.ProtectedClient,
                ClientPlatform = GetClientPlatform(),
                AuthenticationTimeout = (uint)Mathf.Clamp(_peerAuthTimeout,
                    AntiCheatClientInterface.REGISTERPEER_MIN_AUTHENTICATIONTIMEOUT,
                    AntiCheatClientInterface.REGISTERPEER_MAX_AUTHENTICATIONTIMEOUT),
                AccountId_DEPRECATED = puid,
                IpAddress = ipAddress,
                PeerProductUserId = productUserId
            };

            var result = _clientInterface.RegisterPeer(ref options);

            if (result == Result.Success)
            {
                _puidToPeerHandle[puid] = peerHandle;
                _peerHandleToPuid[peerHandle] = puid;
                _registeredPeers.Add(peerHandle);
                EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Registered peer: {puid}");
                return peerHandle;
            }
            else
            {
                EOSDebugLogger.LogError(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Failed to register peer {puid}: {result}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Unregister a peer from anti-cheat validation.
        /// Call when a player leaves the game.
        /// </summary>
        public Result UnregisterPeer(string puid)
        {
            if (!_puidToPeerHandle.TryGetValue(puid, out var peerHandle))
            {
                return Result.NotFound;
            }

            return UnregisterPeer(peerHandle);
        }

        /// <summary>
        /// Unregister a peer by handle.
        /// </summary>
        public Result UnregisterPeer(IntPtr peerHandle)
        {
            if (!_registeredPeers.Contains(peerHandle))
            {
                return Result.NotFound;
            }

            var options = new UnregisterPeerOptions
            {
                PeerHandle = peerHandle
            };

            var result = _clientInterface.UnregisterPeer(ref options);

            if (result == Result.Success)
            {
                if (_peerHandleToPuid.TryGetValue(peerHandle, out var puid))
                {
                    _puidToPeerHandle.Remove(puid);
                    _peerHandleToPuid.Remove(peerHandle);
                }
                _registeredPeers.Remove(peerHandle);
                EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSAntiCheatManager", $"Unregistered peer: {peerHandle}");
            }

            return result;
        }

        /// <summary>
        /// Get the peer handle for a PUID.
        /// </summary>
        public IntPtr GetPeerHandle(string puid)
        {
            return _puidToPeerHandle.TryGetValue(puid, out var handle) ? handle : IntPtr.Zero;
        }

        /// <summary>
        /// Get the PUID for a peer handle.
        /// </summary>
        public string GetPeerPuid(IntPtr peerHandle)
        {
            return _peerHandleToPuid.TryGetValue(peerHandle, out var puid) ? puid : null;
        }

        /// <summary>
        /// Process a received anti-cheat message from a peer.
        /// Call when receiving anti-cheat data over the network.
        /// </summary>
        public Result ReceiveMessageFromPeer(IntPtr peerHandle, byte[] data)
        {
            if (!IsSessionActive || data == null || data.Length == 0)
            {
                return Result.InvalidParameters;
            }

            var dataSpan = new ArraySegment<byte>(data);
            var options = new ReceiveMessageFromPeerOptions
            {
                PeerHandle = peerHandle,
                Data = dataSpan
            };

            return _clientInterface.ReceiveMessageFromPeer(ref options);
        }

        /// <summary>
        /// Get queued outgoing anti-cheat messages to send to peers.
        /// </summary>
        public bool TryGetOutgoingMessage(out IntPtr peerHandle, out byte[] data)
        {
            if (_outgoingPeerMessages.Count > 0)
            {
                var msg = _outgoingPeerMessages.Dequeue();
                peerHandle = msg.peer;
                data = msg.data;
                return true;
            }

            peerHandle = IntPtr.Zero;
            data = null;
            return false;
        }

        /// <summary>
        /// Poll the current anti-cheat status.
        /// </summary>
        public AntiCheatClientViolationType PollStatus()
        {
            if (!IsReady)
            {
                return AntiCheatClientViolationType.Invalid;
            }

            var options = new PollStatusOptions
            {
                OutMessageLength = 256
            };

            var result = _clientInterface.PollStatus(ref options, out var violationType, out var message);

            if (result == Result.Success && violationType != AntiCheatClientViolationType.Invalid)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager",
                    $"Violation detected: {violationType} - {message}");
            }

            return violationType;
        }

        #endregion

        #region Private Methods

        private void SetupNotifications()
        {
            if (_clientInterface == null) return;

            // Integrity violation callback
            var integrityOptions = new AddNotifyClientIntegrityViolatedOptions();
            _integrityViolatedNotification = _clientInterface.AddNotifyClientIntegrityViolated(
                ref integrityOptions, null, OnClientIntegrityViolated);

            // Message to peer callback (for P2P mode)
            var messageToPeerOptions = new AddNotifyMessageToPeerOptions();
            _messageToPeerNotification = _clientInterface.AddNotifyMessageToPeer(
                ref messageToPeerOptions, null, OnMessageToPeer);

            // Peer action required callback
            var peerActionOptions = new AddNotifyPeerActionRequiredOptions();
            _peerActionRequiredNotification = _clientInterface.AddNotifyPeerActionRequired(
                ref peerActionOptions, null, OnPeerActionRequiredCallback);

            // Peer auth status callback
            var authStatusOptions = new AddNotifyPeerAuthStatusChangedOptions();
            _peerAuthStatusChangedNotification = _clientInterface.AddNotifyPeerAuthStatusChanged(
                ref authStatusOptions, null, OnPeerAuthStatusChangedCallback);
        }

        private void RemoveNotifications()
        {
            if (_clientInterface == null) return;

            if (_integrityViolatedNotification != 0)
                _clientInterface.RemoveNotifyClientIntegrityViolated(_integrityViolatedNotification);

            if (_messageToPeerNotification != 0)
                _clientInterface.RemoveNotifyMessageToPeer(_messageToPeerNotification);

            if (_peerActionRequiredNotification != 0)
                _clientInterface.RemoveNotifyPeerActionRequired(_peerActionRequiredNotification);

            if (_peerAuthStatusChangedNotification != 0)
                _clientInterface.RemoveNotifyPeerAuthStatusChanged(_peerAuthStatusChangedNotification);
        }

        private void OnClientIntegrityViolated(ref OnClientIntegrityViolatedCallbackInfo info)
        {
            Status = AntiCheatStatus.Violated;
            EOSDebugLogger.LogError(DebugCategory.Sanctions, "EOSAntiCheatManager",
                $"Integrity violation: {info.ViolationType} - {info.ViolationMessage}");

            OnIntegrityViolation?.Invoke(info.ViolationType, info.ViolationMessage);
        }

        private void OnMessageToPeer(ref OnMessageToPeerCallbackInfo info)
        {
            if (info.Data != null)
            {
                var data = new byte[info.Data.Count];
                Array.Copy(info.Data.Array, info.Data.Offset, data, 0, info.Data.Count);
                _outgoingPeerMessages.Enqueue((info.PeerHandle, data));
            }
        }

        private void OnPeerActionRequiredCallback(ref OnPeerActionRequiredCallbackInfo info)
        {
            string puid = GetPeerPuid(info.PeerHandle) ?? "Unknown";
            string reasonStr = info.ActionReasonDetailsString ?? info.ActionReasonCode.ToString();

            EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSAntiCheatManager",
                $"Peer action required: {puid} - {info.ClientAction} - {reasonStr}");

            OnPeerActionRequired?.Invoke(info.PeerHandle, info.ClientAction, reasonStr);

            // Auto-handle common actions
            if (info.ClientAction == AntiCheatCommonClientAction.RemovePlayer)
            {
                UnregisterPeer(info.PeerHandle);
            }
        }

        private void OnPeerAuthStatusChangedCallback(ref OnPeerAuthStatusChangedCallbackInfo info)
        {
            string puid = GetPeerPuid(info.PeerHandle) ?? "Unknown";
            EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSAntiCheatManager",
                $"Peer auth status changed: {puid} - {info.ClientAuthStatus}");

            OnPeerAuthStatusChanged?.Invoke(info.PeerHandle, info.ClientAuthStatus);
        }

        private void OnLobbyJoined(LobbyJoinResult result, LobbyData lobby)
        {
            if (result == LobbyJoinResult.Success && _autoStartSession && !IsSessionActive)
            {
                BeginSession();
            }
        }

        private void OnLobbyLeft()
        {
            if (IsSessionActive)
            {
                EndSession();
            }
        }

        private static AntiCheatCommonClientPlatform GetClientPlatform()
        {
#if UNITY_STANDALONE_WIN
            return AntiCheatCommonClientPlatform.Windows;
#elif UNITY_STANDALONE_OSX
            return AntiCheatCommonClientPlatform.Mac;
#elif UNITY_STANDALONE_LINUX
            return AntiCheatCommonClientPlatform.Linux;
#elif UNITY_ANDROID
            return AntiCheatCommonClientPlatform.Unknown; // Android not officially supported by EAC
#elif UNITY_IOS
            return AntiCheatCommonClientPlatform.Unknown; // iOS not officially supported by EAC
#else
            return AntiCheatCommonClientPlatform.Unknown;
#endif
        }

        #endregion
    }

    /// <summary>
    /// Current anti-cheat protection status.
    /// </summary>
    public enum AntiCheatStatus
    {
        /// <summary>Anti-cheat not yet initialized.</summary>
        NotInitialized,
        /// <summary>Anti-cheat not available on this platform.</summary>
        NotAvailable,
        /// <summary>Anti-cheat initialized but no session active.</summary>
        Initialized,
        /// <summary>Anti-cheat session active and protecting.</summary>
        Protected,
        /// <summary>Integrity violation detected.</summary>
        Violated,
        /// <summary>Error state.</summary>
        Error
    }
}
