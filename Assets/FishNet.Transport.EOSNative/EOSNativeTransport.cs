using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using FishNet.Managing;
using FishNet.Managing.Logging;
using FishNet.Transporting;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Migration;
using FishNet.Transport.EOSNative.Voice;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// FishNet Transport implementation using Epic Online Services P2P.
    /// </summary>
    [AddComponentMenu("FishNet/Transport/EOS Native Transport")]
    public class EOSNativeTransport : FishNet.Transporting.Transport
    {
        #region Constants

        /// <summary>
        /// Special connection ID for the host acting as a client.
        /// </summary>
        public const int CLIENT_HOST_ID = short.MaxValue;

        /// <summary>
        /// Maximum packet size for EOS P2P.
        /// </summary>
        public const int MAX_PACKET_SIZE = 1170;

        #endregion

        #region Serialized Fields

        [Header("EOS Configuration")]
        [SerializeField]
        [Tooltip("The EOSConfig asset containing EOS credentials.")]
        private EOSConfig _eosConfig;

        [SerializeField]
        [Tooltip("The socket name used for P2P connections.")]
        private string _socketName = "FishNetEOS";

        [SerializeField]
        [Tooltip("The ProductUserId of the server to connect to (client only).")]
        private string _remoteProductUserId;

        [Header("Connection Settings")]
        [SerializeField]
        [Tooltip("Maximum number of clients that can connect to the server.")]
        private int _maxClients = 64;

        [SerializeField]
        [Tooltip("Connection timeout in seconds.")]
        private float _timeout = 25f;

        [SerializeField]
        [Tooltip("Relay server usage. ForceRelays (default) protects user IP addresses but adds latency. AllowRelays tries direct first. NoRelays exposes IPs.")]
        private RelayControl _relayControl = RelayControl.ForceRelays;

        [Header("Auto-Initialization")]
        [SerializeField]
        [Tooltip("Automatically initialize EOS and login on Start.")]
        private bool _autoInitialize = true;

        [Header("Lobby Settings")]
        [SerializeField]
        [Tooltip("Default max players for created lobbies.")]
        private uint _defaultMaxPlayers = 4;

        [SerializeField]
        [Tooltip("Version bucket for matchmaking. Different versions won't see each other.")]
        private string _lobbyBucket = "v1";

        [SerializeField]
        [Tooltip("Default room code used when hosting. If empty, a random 4-digit code is generated. Can be any string.")]
        private string _defaultRoomCode = "";

        [Header("Heartbeat Settings")]
        [SerializeField]
        [Tooltip("Seconds without packets before disconnecting a client. Set lower for faster detection.")]
        private float _heartbeatTimeout = 5f;

        [Header("Moderation")]
        [SerializeField]
        [Tooltip("When enabled, checks EOS Sanctions before accepting connections. Banned players will be rejected.")]
        private bool _checkSanctionsBeforeAccept = false;

        #endregion

        #region Lobby State

        private EOSLobbyManager _lobbyManager;

        /// <summary>
        /// Gets or creates the lobby manager instance.
        /// </summary>
        public EOSLobbyManager LobbyManager
        {
            get
            {
                // Use the singleton Instance which auto-creates if needed
                if (_lobbyManager == null)
                {
                    _lobbyManager = EOSLobbyManager.Instance;
                }
                return _lobbyManager;
            }
        }

        /// <summary>
        /// Whether we're currently in a lobby.
        /// </summary>
        public bool IsInLobby => LobbyManager?.IsInLobby ?? false;

        /// <summary>
        /// Whether we're the owner of the current lobby.
        /// </summary>
        public bool IsLobbyOwner => LobbyManager?.IsOwner ?? false;

        /// <summary>
        /// Current lobby data, if in a lobby.
        /// </summary>
        public LobbyData CurrentLobby => LobbyManager?.CurrentLobby ?? default;

        /// <summary>
        /// Default max players for lobbies.
        /// </summary>
        public uint DefaultMaxPlayers => _defaultMaxPlayers;

        /// <summary>
        /// Lobby version bucket.
        /// </summary>
        public string LobbyBucket => _lobbyBucket;

        /// <summary>
        /// Default room code for hosting. If empty, a random code will be generated.
        /// </summary>
        public string DefaultRoomCode
        {
            get => _defaultRoomCode;
            set => _defaultRoomCode = value;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// The socket name used for P2P connections.
        /// </summary>
        public string SocketName
        {
            get => _socketName;
            set => _socketName = value;
        }

        /// <summary>
        /// The ProductUserId of the server to connect to (as string).
        /// </summary>
        public string RemoteProductUserId
        {
            get => _remoteProductUserId;
            set => _remoteProductUserId = value;
        }

        /// <summary>
        /// The local ProductUserId after login.
        /// </summary>
        public ProductUserId LocalProductUserId => EOSManager.Instance?.LocalProductUserId;

        #endregion

        #region Private Fields

        private EOSServer _server;
        private EOSClient _client;
        private EOSClientHost _clientHost;

        private LocalConnectionState _serverState = LocalConnectionState.Stopped;
        private LocalConnectionState _clientState = LocalConnectionState.Stopped;

        #endregion

        #region Bandwidth Stats

        /// <summary>
        /// Total bytes sent (server + client combined).
        /// </summary>
        public long TotalBytesSent => (_server?.TotalBytesSent ?? 0) + (_client?.TotalBytesSent ?? 0);

        /// <summary>
        /// Total bytes received (server + client combined).
        /// </summary>
        public long TotalBytesReceived => (_server?.TotalBytesReceived ?? 0) + (_client?.TotalBytesReceived ?? 0);

        /// <summary>
        /// Server bytes sent.
        /// </summary>
        public long ServerBytesSent => _server?.TotalBytesSent ?? 0;

        /// <summary>
        /// Server bytes received.
        /// </summary>
        public long ServerBytesReceived => _server?.TotalBytesReceived ?? 0;

        /// <summary>
        /// Client bytes sent.
        /// </summary>
        public long ClientBytesSent => _client?.TotalBytesSent ?? 0;

        /// <summary>
        /// Client bytes received.
        /// </summary>
        public long ClientBytesReceived => _client?.TotalBytesReceived ?? 0;

        /// <summary>
        /// Gets the number of P2P connections on the server (excludes ClientHost).
        /// </summary>
        public int ServerP2PConnectionCount => _server?.ConnectionCount ?? 0;

        /// <summary>
        /// Gets all server connection info for debugging.
        /// Returns list of (connectionId, puid, lastPacketAge).
        /// </summary>
        public List<(int connectionId, string puid, float lastPacketAge)> GetServerConnectionInfo()
        {
            return _server?.GetAllConnectionInfo() ?? new List<(int, string, float)>();
        }

        /// <summary>
        /// Whether ClientHost is active (host acting as local client).
        /// </summary>
        public bool HasClientHost => _clientHost != null && _serverState == LocalConnectionState.Started;

        #endregion

        #region Events

        public override event Action<ClientConnectionStateArgs> OnClientConnectionState;
        public override event Action<ServerConnectionStateArgs> OnServerConnectionState;
        public override event Action<RemoteConnectionStateArgs> OnRemoteConnectionState;
        public override event Action<ClientReceivedDataArgs> OnClientReceivedData;
        public override event Action<ServerReceivedDataArgs> OnServerReceivedData;

        #endregion

        #region Initialization

        public override void Initialize(NetworkManager networkManager, int transportIndex)
        {
            base.Initialize(networkManager, transportIndex);
        }

        private async void Start()
        {
            if (_autoInitialize && _eosConfig != null)
            {
                await AutoInitializeAsync();
            }
        }

        private async Task AutoInitializeAsync()
        {
            // Ensure EOSManager exists
            if (EOSManager.Instance == null)
            {
                EOSDebugLogger.LogError("EOSNativeTransport", "EOSManager not found in scene. Auto-initialization failed.");
                return;
            }

            // Initialize EOS SDK
            if (!EOSManager.Instance.IsInitialized)
            {
                var result = EOSManager.Instance.Initialize(_eosConfig);
                if (result != Result.Success && result != Result.AlreadyConfigured)
                {
                    Debug.LogError($"[EOSNativeTransport] EOS initialization failed: {result}");
                    return;
                }
            }

            // Login with device token
            if (!EOSManager.Instance.IsLoggedIn)
            {
                var displayName = _eosConfig.DefaultDisplayName;
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = "Player";
                }

                var loginResult = await EOSManager.Instance.LoginWithDeviceTokenAsync(displayName);
                if (loginResult != Result.Success)
                {
                    Debug.LogError($"[EOSNativeTransport] Login failed: {loginResult}");
                    return;
                }
            }

            EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Auto-initialization complete. Ready to connect.");
        }

        private void Update()
        {
            // Check for stale connections (heartbeat timeout)
            _server?.CheckHeartbeats();
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            // Skip if we already handled this in OnPlayModeStateChanged
            if (_isExitingPlayMode) return;
#endif
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            // Leave lobby synchronously - async won't complete before Unity tears down
            if (IsInLobby && _lobbyManager != null)
            {
                _lobbyManager.LeaveLobbySync();
            }
            Shutdown();
        }

#if UNITY_EDITOR
        private bool _isExitingPlayMode;

        private void OnEnable()
        {
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                _isExitingPlayMode = true;

                // CRITICAL: Stop FishNet connections FIRST to prevent null refs during shutdown
                // FishNet's TimeManager continues ticking and tries to send data - we must stop it
                var networkManager = GetComponent<NetworkManager>();
                if (networkManager != null)
                {
                    if (networkManager.IsClientStarted)
                    {
                        networkManager.ClientManager.StopConnection();
                    }
                    if (networkManager.IsServerStarted)
                    {
                        networkManager.ServerManager.StopConnection(true);
                    }
                }

                // Then leave lobby synchronously
                if (IsInLobby && _lobbyManager != null)
                {
                    _lobbyManager.LeaveLobbySync();
                }

                // Finally shutdown transport
                Shutdown();
            }
        }
#endif

        #endregion

        #region Editor Auto-Setup

#if UNITY_EDITOR
        private void Reset()
        {
            AutoSetup();
        }

        [ContextMenu("Auto-Setup EOS Transport")]
        private void AutoSetup()
        {
            // 1. Ensure NetworkManager on this GameObject
            var networkManager = GetComponent<NetworkManager>();
            if (networkManager == null)
            {
                networkManager = gameObject.AddComponent<NetworkManager>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created NetworkManager");
            }

            // 2. Wire transport reference
            var transportManager = networkManager.TransportManager;
            if (transportManager != null)
            {
                // Use SerializedObject to set the transport reference
                var so = new SerializedObject(transportManager);
                var transportProp = so.FindProperty("_transport");
                if (transportProp != null && transportProp.objectReferenceValue != this)
                {
                    transportProp.objectReferenceValue = this;
                    so.ApplyModifiedProperties();
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Wired transport reference");
                }
            }

            // 3-7. Create EOS subsystems as children of NetworkManager for clean hierarchy
            var eosManager = FindAnyObjectByType<EOSManager>();
            if (eosManager == null)
            {
                var eosGO = new GameObject("EOSManager");
                eosGO.transform.SetParent(transform);
                eosGO.AddComponent<EOSManager>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created EOSManager");
            }

            var lobbyManager = FindAnyObjectByType<EOSLobbyManager>();
            if (lobbyManager == null)
            {
                var lobbyGO = new GameObject("EOSLobbyManager");
                lobbyGO.transform.SetParent(transform);
                lobbyGO.AddComponent<EOSLobbyManager>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created EOSLobbyManager");
            }

            var voiceManager = FindAnyObjectByType<EOSVoiceManager>();
            if (voiceManager == null)
            {
                var voiceGO = new GameObject("EOSVoiceManager");
                voiceGO.transform.SetParent(transform);
                voiceGO.AddComponent<EOSVoiceManager>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created EOSVoiceManager");
            }

            var chatManager = FindAnyObjectByType<EOSLobbyChatManager>();
            if (chatManager == null)
            {
                var chatGO = new GameObject("EOSLobbyChatManager");
                chatGO.transform.SetParent(transform);
                chatGO.AddComponent<EOSLobbyChatManager>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created EOSLobbyChatManager");
            }

            var migrationManager = FindAnyObjectByType<HostMigrationManager>();
            if (migrationManager == null)
            {
                var migrationGO = new GameObject("HostMigrationManager");
                migrationGO.transform.SetParent(transform);
                migrationGO.AddComponent<HostMigrationManager>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created HostMigrationManager");
            }

            // 8. Add HostMigrationPlayerSpawner to NetworkManager
            var playerSpawner = FindAnyObjectByType<HostMigrationPlayerSpawner>();
            if (playerSpawner == null)
            {
                playerSpawner = gameObject.AddComponent<HostMigrationPlayerSpawner>();

                // Auto-assign PlayerBallPrefab if it exists
                var prefabGuids = AssetDatabase.FindAssets("PlayerBallPrefab t:Prefab");
                if (prefabGuids.Length > 0)
                {
                    var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[0]);
                    var prefab = AssetDatabase.LoadAssetAtPath<FishNet.Object.NetworkObject>(prefabPath);
                    if (prefab != null)
                    {
                        var so = new SerializedObject(playerSpawner);
                        so.FindProperty("_playerPrefab").objectReferenceValue = prefab;
                        so.ApplyModifiedProperties();
                        EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Created HostMigrationPlayerSpawner with {prefab.name}");
                    }
                    else
                    {
                        EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created HostMigrationPlayerSpawner (assign player prefab manually)");
                    }
                }
                else
                {
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Created HostMigrationPlayerSpawner (assign player prefab manually)");
                }
            }

            // 9. Add debug UI components
            // Main UI (F1)
            var nativeUI = GetComponent<EOSNativeUI>();
            if (nativeUI == null)
            {
                nativeUI = gameObject.AddComponent<EOSNativeUI>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Added EOSNativeUI (F1 to toggle)");
            }

            // Voice debug panel (F3)
            var voiceDebug = GetComponent<FishNet.Transport.EOSNative.DebugUI.EOSVoiceDebugPanel>();
            if (voiceDebug == null)
            {
                voiceDebug = gameObject.AddComponent<FishNet.Transport.EOSNative.DebugUI.EOSVoiceDebugPanel>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Added EOSVoiceDebugPanel (F3 to toggle)");
            }

            // Network debug panel (F4)
            var networkDebug = GetComponent<FishNet.Transport.EOSNative.DebugUI.EOSNetworkDebugPanel>();
            if (networkDebug == null)
            {
                networkDebug = gameObject.AddComponent<FishNet.Transport.EOSNative.DebugUI.EOSNetworkDebugPanel>();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Added EOSNetworkDebugPanel (F4 to toggle)");
            }

            // 10. Auto-assign SampleEOSConfig if available and _eosConfig is null
            if (_eosConfig == null)
            {
                var guids = AssetDatabase.FindAssets("SampleEOSConfig t:EOSConfig");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _eosConfig = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
                    if (_eosConfig != null)
                    {
                        EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Auto-assigned {path}");
                        EditorUtility.SetDirty(this);
                    }
                }
            }

            EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", "Auto-setup complete!");
        }
#endif

        #endregion

        #region Lobby API - Simplified

        /// <summary>
        /// HOST MODE: Creates a lobby and starts hosting (server + clienthost).
        /// This is the primary way to start a session.
        /// </summary>
        /// <param name="roomCode">Join code (any string). If null/empty, uses DefaultRoomCode or generates random 4-digit code.</param>
        /// <returns>Result and lobby data with the room code.</returns>
        public async Task<(Result result, LobbyData lobby)> HostLobbyAsync(string roomCode = null)
        {
            // Use provided code, or default, or generate random
            string code = roomCode;
            if (string.IsNullOrEmpty(code))
            {
                code = _defaultRoomCode;
            }

            var options = new LobbyCreateOptions
            {
                MaxPlayers = _defaultMaxPlayers,
                IsPublic = true,
                BucketId = _lobbyBucket,
                JoinCode = string.IsNullOrEmpty(code) ? null : code
            };

            return await HostLobbyAsync(options);
        }

        /// <summary>
        /// HOST MODE: Creates a lobby with full options and starts hosting.
        /// Use this when you need to set game mode, map, region, etc.
        /// </summary>
        /// <param name="options">Full lobby creation options including attributes.</param>
        /// <returns>Result and lobby data.</returns>
        public async Task<(Result result, LobbyData lobby)> HostLobbyAsync(LobbyCreateOptions options)
        {
            // Apply defaults if not set
            if (options.MaxPlayers == 0)
                options.MaxPlayers = _defaultMaxPlayers;
            if (string.IsNullOrEmpty(options.BucketId))
                options.BucketId = _lobbyBucket;

            var (result, lobby) = await LobbyManager.CreateLobbyAsync(options);

            if (result == Result.Success)
            {
                StartHost();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Hosting lobby: {lobby.JoinCode} (Mode: {lobby.GameMode ?? "default"}, Map: {lobby.Map ?? "default"})");
            }

            return (result, lobby);
        }

        /// <summary>
        /// CLIENT MODE: Joins a lobby by room code.
        /// By default, auto-connects to the host after joining.
        /// Automatically detects if the code is an EOS LobbyId and uses direct join.
        /// </summary>
        /// <param name="roomCode">The join code (custom code or EOS LobbyId).</param>
        /// <param name="autoConnect">If true (default), automatically connects to the host. If false, just joins the lobby.</param>
        /// <returns>Result and lobby data.</returns>
        public async Task<(Result result, LobbyData lobby)> JoinLobbyAsync(string roomCode, bool autoConnect = true)
        {
            if (string.IsNullOrEmpty(roomCode))
            {
                EOSDebugLogger.LogError("EOSNativeTransport", "Room code is required to join a lobby.");
                return (Result.InvalidParameters, default);
            }

            var (result, lobby) = await LobbyManager.JoinLobbyByCodeAsync(roomCode);

            if (result == Result.Success)
            {
                if (autoConnect && !string.IsNullOrEmpty(lobby.OwnerPuid))
                {
                    RemoteProductUserId = lobby.OwnerPuid;
                    StartClientOnly();
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Joined lobby {roomCode}, connecting to host...");
                }
                else if (autoConnect)
                {
                    EOSDebugLogger.LogWarning(DebugCategory.Transport, "EOSNativeTransport", "Joined lobby but no host found!");
                }
                else
                {
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Joined lobby {roomCode} (auto-connect disabled)");
                }
            }

            return (result, lobby);
        }

        /// <summary>
        /// CLIENT MODE: Joins a lobby by its name (searches by LOBBY_NAME attribute).
        /// If multiple lobbies have the same name, joins the first one found.
        /// </summary>
        /// <param name="lobbyName">The lobby name to search for and join.</param>
        /// <param name="autoConnect">If true (default), automatically connects to the host.</param>
        /// <returns>Result and lobby data.</returns>
        public async Task<(Result result, LobbyData lobby)> JoinLobbyByNameAsync(string lobbyName, bool autoConnect = true)
        {
            if (string.IsNullOrEmpty(lobbyName))
            {
                EOSDebugLogger.LogError("EOSNativeTransport", "Lobby name is required.");
                return (Result.InvalidParameters, default);
            }

            var (result, lobby) = await LobbyManager.JoinLobbyByNameAsync(lobbyName);

            if (result == Result.Success && autoConnect)
            {
                if (!string.IsNullOrEmpty(lobby.OwnerPuid))
                {
                    RemoteProductUserId = lobby.OwnerPuid;
                    StartClientOnly();
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Joined lobby by name: {lobbyName} ({lobby.JoinCode})");
                }
                else
                {
                    EOSDebugLogger.LogWarning(DebugCategory.Transport, "EOSNativeTransport", "Joined lobby but no host found!");
                }
            }

            return (result, lobby);
        }

        /// <summary>
        /// Searches for lobbies by name (exact match or containing substring).
        /// </summary>
        /// <param name="searchTerm">The name or substring to search for.</param>
        /// <param name="exactMatch">If true, searches for exact name match. If false, searches for names containing the term.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>Result and list of matching lobbies.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchLobbiesByNameAsync(string searchTerm, bool exactMatch = false, uint maxResults = 10)
        {
            if (exactMatch)
            {
                return await LobbyManager.SearchByNameAsync(searchTerm, maxResults);
            }
            else
            {
                return await LobbyManager.SearchByNameContainingAsync(searchTerm, maxResults);
            }
        }

        /// <summary>
        /// Leaves the current lobby and stops all connections.
        /// </summary>
        public async Task LeaveLobbyAsync()
        {
            StopHost();

            // Clear migration state to prevent stale data in future sessions
            HostMigrationManager.Instance?.ClearMigrationState();

            await LobbyManager.LeaveLobbyAsync();
        }

        /// <summary>
        /// Quick match - finds any available lobby and joins + connects.
        /// Returns NotFound if no lobbies available.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> QuickMatchAsync()
        {
            var (result, lobby) = await LobbyManager.QuickMatchAsync();

            if (result == Result.Success && !string.IsNullOrEmpty(lobby.OwnerPuid))
            {
                RemoteProductUserId = lobby.OwnerPuid;
                StartClientOnly();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" QuickMatch: Connected to {lobby.JoinCode}");
            }

            return (result, lobby);
        }

        /// <summary>
        /// Quick match OR auto-host - finds any available lobby and joins, OR hosts a new one if none found.
        /// This is the recommended way to implement "Play Now" functionality.
        /// </summary>
        /// <param name="searchOptions">Optional search filters (game mode, region, etc.). If null, finds any available lobby.</param>
        /// <returns>Result, lobby data, and whether we became the host.</returns>
        public async Task<(Result result, LobbyData lobby, bool didHost)> QuickMatchOrHostAsync(LobbySearchOptions searchOptions = null)
        {
            var createOptions = new LobbyCreateOptions
            {
                MaxPlayers = _defaultMaxPlayers,
                BucketId = _lobbyBucket,
                JoinCode = string.IsNullOrEmpty(_defaultRoomCode) ? null : _defaultRoomCode
            };

            // Copy search filters to create options for hosting fallback
            if (searchOptions != null)
            {
                if (!string.IsNullOrEmpty(searchOptions.BucketId))
                    createOptions.BucketId = searchOptions.BucketId;
                if (searchOptions.Filters != null)
                {
                    if (searchOptions.Filters.TryGetValue(LobbyAttributes.GAME_MODE, out var gameMode))
                        createOptions.GameMode = gameMode;
                    if (searchOptions.Filters.TryGetValue(LobbyAttributes.REGION, out var region))
                        createOptions.Region = region;
                    if (searchOptions.Filters.TryGetValue(LobbyAttributes.MAP, out var map))
                        createOptions.Map = map;
                }
            }

            var (result, lobby, didHost) = await LobbyManager.QuickMatchOrHostAsync(createOptions, searchOptions);

            if (result == Result.Success)
            {
                if (didHost)
                {
                    StartHost();
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" QuickMatchOrHost: Hosting {lobby.JoinCode}");
                }
                else if (!string.IsNullOrEmpty(lobby.OwnerPuid))
                {
                    RemoteProductUserId = lobby.OwnerPuid;
                    StartClientOnly();
                    EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" QuickMatchOrHost: Joined {lobby.JoinCode}");
                }
            }

            return (result, lobby, didHost);
        }

        /// <summary>
        /// Finds and joins a lobby by game mode, then auto-connects.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> JoinByGameModeAsync(string gameMode)
        {
            var (result, lobby) = await LobbyManager.JoinByGameModeAsync(gameMode);

            if (result == Result.Success && !string.IsNullOrEmpty(lobby.OwnerPuid))
            {
                RemoteProductUserId = lobby.OwnerPuid;
                StartClientOnly();
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" JoinByGameMode({gameMode}): Connected to {lobby.JoinCode}");
            }

            return (result, lobby);
        }

        /// <summary>
        /// Searches for lobbies matching the given options (attribute-based search).
        /// </summary>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchLobbiesAsync(LobbySearchOptions options = null)
        {
            return await LobbyManager.SearchLobbiesAsync(options);
        }

        /// <summary>
        /// Searches for a lobby by its exact EOS lobby ID.
        /// This is the fastest lookup method when you know the lobby ID.
        /// </summary>
        /// <param name="lobbyId">The EOS lobby ID to search for.</param>
        /// <returns>Result and lobby data if found.</returns>
        public async Task<(Result result, LobbyData? lobby)> SearchByLobbyIdAsync(string lobbyId)
        {
            return await LobbyManager.SearchByLobbyIdAsync(lobbyId);
        }

        /// <summary>
        /// Searches for all PUBLIC lobbies that contain a specific user.
        /// Useful for finding friends' games.
        /// Note: Only finds PUBLIC lobbies - presence-only lobbies will not be returned.
        /// </summary>
        /// <param name="memberPuid">The ProductUserId string of the user to search for.</param>
        /// <param name="maxResults">Maximum number of results (default: 10).</param>
        /// <returns>Result and list of lobbies containing the user.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchByMemberAsync(string memberPuid, uint maxResults = 10)
        {
            return await LobbyManager.SearchByMemberAsync(memberPuid, maxResults);
        }

        /// <summary>
        /// Searches for all PUBLIC lobbies that contain a specific user.
        /// Useful for finding friends' games.
        /// Note: Only finds PUBLIC lobbies - presence-only lobbies will not be returned.
        /// </summary>
        /// <param name="memberPuid">The ProductUserId of the user to search for.</param>
        /// <param name="maxResults">Maximum number of results (default: 10).</param>
        /// <returns>Result and list of lobbies containing the user.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchByMemberAsync(ProductUserId memberPuid, uint maxResults = 10)
        {
            return await LobbyManager.SearchByMemberAsync(memberPuid, maxResults);
        }

        /// <summary>
        /// Finds lobbies where a friend is currently playing (joinable only).
        /// Convenience wrapper that filters to only available, not-in-progress lobbies.
        /// </summary>
        /// <param name="friendPuid">The friend's ProductUserId string.</param>
        /// <returns>Result and list of joinable lobbies.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> FindFriendLobbiesAsync(string friendPuid)
        {
            return await LobbyManager.FindFriendLobbiesAsync(friendPuid);
        }

        #endregion

        #region Lobby API - Advanced (Legacy)

        /// <summary>
        /// Creates a lobby without starting host. Use HostLobbyAsync() for the simple flow.
        /// </summary>
        [System.Obsolete("Use HostLobbyAsync() instead for the simplified flow.")]
        public async Task<(Result result, LobbyData lobby)> CreateLobbyAsync(string joinCode = null, bool startHost = false)
        {
            var options = new LobbyCreateOptions
            {
                MaxPlayers = _defaultMaxPlayers,
                IsPublic = true,
                BucketId = _lobbyBucket,
                JoinCode = joinCode
            };

            var (result, lobby) = await LobbyManager.CreateLobbyAsync(options);

            if (result == Result.Success && startHost)
            {
                StartHost();
            }

            return (result, lobby);
        }

        /// <summary>
        /// Starts both server and client (host mode). Called automatically by HostLobbyAsync().
        /// </summary>
        public void StartHost()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm == null) nm = FindAnyObjectByType<NetworkManager>();
            if (nm == null) return;

            nm.ServerManager.StartConnection();
            nm.ClientManager.StartConnection();
        }

        /// <summary>
        /// Stops both server and client.
        /// </summary>
        public void StopHost()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm == null) nm = FindAnyObjectByType<NetworkManager>();
            if (nm == null) return;

            if (nm.IsClientStarted)
                nm.ClientManager.StopConnection();
            if (nm.IsServerStarted)
                nm.ServerManager.StopConnection(true);
        }

        /// <summary>
        /// Connects to the current lobby's host. Called automatically by JoinLobbyAsync().
        /// </summary>
        public void ConnectToLobbyHost()
        {
            if (!IsInLobby) return;

            var hostPuid = CurrentLobby.OwnerPuid;
            if (string.IsNullOrEmpty(hostPuid)) return;

            RemoteProductUserId = hostPuid;
            StartClientOnly();
        }

        /// <summary>
        /// Starts only the client connection (via NetworkManager).
        /// </summary>
        public void StartClientOnly()
        {
            var nm = GetComponent<NetworkManager>();
            if (nm == null) nm = FindAnyObjectByType<NetworkManager>();
            nm?.ClientManager.StartConnection();
        }

        #endregion

        #region Connection State

        public override LocalConnectionState GetConnectionState(bool server)
        {
            return server ? _serverState : _clientState;
        }

        public override RemoteConnectionState GetConnectionState(int connectionId)
        {
            if (_server == null) return RemoteConnectionState.Stopped;
            return _server.GetConnectionState(connectionId);
        }

        public override string GetConnectionAddress(int connectionId)
        {
            if (_server == null) return string.Empty;
            return _server.GetConnectionAddress(connectionId);
        }

        public override void HandleClientConnectionState(ClientConnectionStateArgs connectionStateArgs)
        {
            OnClientConnectionState?.Invoke(connectionStateArgs);
        }

        public override void HandleServerConnectionState(ServerConnectionStateArgs connectionStateArgs)
        {
            OnServerConnectionState?.Invoke(connectionStateArgs);
        }

        public override void HandleRemoteConnectionState(RemoteConnectionStateArgs connectionStateArgs)
        {
            OnRemoteConnectionState?.Invoke(connectionStateArgs);
        }

        internal void SetClientState(LocalConnectionState state)
        {
            if (_clientState == state) return;
            _clientState = state;
            HandleClientConnectionState(new ClientConnectionStateArgs(state, Index));
        }

        internal void SetServerState(LocalConnectionState state)
        {
            if (_serverState == state) return;
            _serverState = state;
            HandleServerConnectionState(new ServerConnectionStateArgs(state, Index));
        }

        internal void InvokeRemoteConnectionState(RemoteConnectionState state, int connectionId)
        {
            HandleRemoteConnectionState(new RemoteConnectionStateArgs(state, connectionId, Index));
        }

        #endregion

        #region Lobby Event Subscription (Fast Disconnect Detection)

        /// <summary>
        /// Subscribes to lobby member events for instant disconnect detection.
        /// When a member leaves the lobby, we immediately disconnect their P2P connection
        /// instead of waiting for the ~25 second P2P timeout.
        /// </summary>
        private void SubscribeToLobbyEvents()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberLeft += OnLobbyMemberLeft;
            }
        }

        private void UnsubscribeFromLobbyEvents()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberLeft -= OnLobbyMemberLeft;
            }
        }

        /// <summary>
        /// Called when a lobby member leaves/disconnects.
        /// Triggers immediate FishNet disconnection instead of waiting for P2P timeout.
        /// </summary>
        private void OnLobbyMemberLeft(string memberPuid)
        {
            // Only server cares about members leaving
            if (_server == null || _serverState != LocalConnectionState.Started)
                return;

            int connectionId = _server.GetConnectionIdByPuid(memberPuid);
            if (connectionId > 0)
            {
                EOSDebugLogger.Log(DebugCategory.Transport, "EOSNativeTransport", $" Lobby member {memberPuid} left - disconnecting connection {connectionId}");
                StopConnection(connectionId, immediately: true);
            }
        }

        #endregion

        #region Connection <-> PUID Mapping

        /// <summary>
        /// Gets the ProductUserId (PUID) string for a given FishNet connection ID.
        /// Used for voice chat to map FishNet connections to EOS users.
        /// Returns null if connection not found.
        /// </summary>
        public string GetPuidForConnection(int connectionId)
        {
            return _server?.GetPuidForConnection(connectionId);
        }

        /// <summary>
        /// Gets the FishNet connection ID for a given ProductUserId string.
        /// Returns -1 if not found.
        /// </summary>
        public int GetConnectionIdForPuid(string puid)
        {
            return _server?.GetConnectionIdByPuid(puid) ?? -1;
        }

        #endregion

        #region Start and Stop

        public override bool StartConnection(bool server)
        {
            if (!EOSManager.Instance.IsInitialized)
            {
                NetworkManager.LogError("[EOSNativeTransport] EOS is not initialized. Call EOSManager.Instance.Initialize() first.");
                return false;
            }

            if (!EOSManager.Instance.IsLoggedIn)
            {
                NetworkManager.LogError("[EOSNativeTransport] Not logged in to EOS. Call EOSManager.Instance.LoginWithDeviceTokenAsync() first.");
                return false;
            }

            if (server)
            {
                return StartServer();
            }
            else
            {
                return StartClient();
            }
        }

        private bool StartServer()
        {
            if (_serverState != LocalConnectionState.Stopped)
            {
                NetworkManager.LogWarning("[EOSNativeTransport] Server is already running or starting.");
                return false;
            }

            // Apply relay control setting
            ApplyRelayControl();

            SetServerState(LocalConnectionState.Starting);

            _server = new EOSServer(this);
            _server.SetHeartbeatTimeout(_heartbeatTimeout);
            _server.CheckSanctionsBeforeAccept = _checkSanctionsBeforeAccept;
            bool success = _server.Start(_socketName, _maxClients);

            if (success)
            {
                SubscribeToLobbyEvents();
                SetServerState(LocalConnectionState.Started);
                NetworkManager.Log("[EOSNativeTransport] Server started.");
            }
            else
            {
                SetServerState(LocalConnectionState.Stopped);
                _server = null;
                NetworkManager.LogError("[EOSNativeTransport] Failed to start server.");
            }

            return success;
        }

        private bool StartClient()
        {
            if (_clientState != LocalConnectionState.Stopped)
            {
                NetworkManager.LogWarning("[EOSNativeTransport] Client is already running or starting.");
                return false;
            }

            // Check if we're starting as ClientHost (server is running)
            if (_serverState == LocalConnectionState.Started)
            {
                return StartClientHost();
            }

            // Validate remote ProductUserId
            if (string.IsNullOrEmpty(_remoteProductUserId))
            {
                NetworkManager.LogError("[EOSNativeTransport] RemoteProductUserId is not set.");
                return false;
            }

            SetClientState(LocalConnectionState.Starting);

            _client = new EOSClient(this);
            bool success = _client.Start(_socketName, _remoteProductUserId);

            if (!success)
            {
                SetClientState(LocalConnectionState.Stopped);
                _client = null;
                NetworkManager.LogError("[EOSNativeTransport] Failed to start client.");
            }

            return success;
        }

        private bool StartClientHost()
        {
            NetworkManager.Log("[EOSNativeTransport] Starting as ClientHost (host acting as client).");

            SetClientState(LocalConnectionState.Starting);

            _clientHost = new EOSClientHost(this, _server);
            _clientHost.Start();

            SetClientState(LocalConnectionState.Started);

            return true;
        }

        public override bool StopConnection(bool server)
        {
            if (server)
            {
                return StopServer();
            }
            else
            {
                return StopClient();
            }
        }

        private bool StopServer()
        {
            if (_serverState == LocalConnectionState.Stopped || _serverState == LocalConnectionState.Stopping)
            {
                return false;
            }

            SetServerState(LocalConnectionState.Stopping);

            UnsubscribeFromLobbyEvents();
            _server?.Stop();
            _server = null;

            SetServerState(LocalConnectionState.Stopped);
            NetworkManager.Log("[EOSNativeTransport] Server stopped.");

            return true;
        }

        private bool StopClient()
        {
            if (_clientState == LocalConnectionState.Stopped || _clientState == LocalConnectionState.Stopping)
            {
                return false;
            }

            SetClientState(LocalConnectionState.Stopping);

            if (_clientHost != null)
            {
                _clientHost.Stop();
                _clientHost = null;
            }
            else if (_client != null)
            {
                _client.Stop();
                _client = null;
            }

            SetClientState(LocalConnectionState.Stopped);
            NetworkManager.Log("[EOSNativeTransport] Client stopped.");

            return true;
        }

        public override bool StopConnection(int connectionId, bool immediately)
        {
            if (_server == null) return false;
            return _server.StopConnection(connectionId, immediately);
        }

        public override void Shutdown()
        {
            StopClient();
            StopServer();
        }

        #endregion

        #region Sending

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            if (_clientState != LocalConnectionState.Started) return;

            Channel channel = (Channel)channelId;

            if (_clientHost != null)
            {
                _clientHost.SendToServer(segment, channel);
            }
            else if (_client != null)
            {
                _client.Send(segment, channel);
            }
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            if (_serverState != LocalConnectionState.Started) return;
            if (_server == null) return;

            Channel channel = (Channel)channelId;

            // Check if sending to ClientHost
            if (connectionId == CLIENT_HOST_ID && _clientHost != null)
            {
                _clientHost.SendFromServer(segment, channel);
            }
            else
            {
                _server.Send(connectionId, segment, channel);
            }
        }

        #endregion

        #region Receiving

        public override void HandleClientReceivedDataArgs(ClientReceivedDataArgs receivedDataArgs)
        {
            OnClientReceivedData?.Invoke(receivedDataArgs);
        }

        public override void HandleServerReceivedDataArgs(ServerReceivedDataArgs receivedDataArgs)
        {
            OnServerReceivedData?.Invoke(receivedDataArgs);
        }

        internal void InvokeClientReceivedData(ArraySegment<byte> data, Channel channel)
        {
            HandleClientReceivedDataArgs(new ClientReceivedDataArgs(data, channel, Index));
        }

        internal void InvokeServerReceivedData(ArraySegment<byte> data, Channel channel, int connectionId)
        {
            HandleServerReceivedDataArgs(new ServerReceivedDataArgs(data, channel, connectionId, Index));
        }

        #endregion

        #region Iterating

        public override void IterateIncoming(bool server)
        {
            if (server)
            {
                // Process ClientHost incoming first
                _clientHost?.IterateIncoming();

                // Then process P2P incoming
                _server?.IterateIncoming();
            }
            else
            {
                if (_clientHost != null)
                {
                    _clientHost.IterateOutgoing(); // ClientHost receives from server's outgoing queue
                }
                else
                {
                    _client?.IterateIncoming();
                }
            }
        }

        public override void IterateOutgoing(bool server)
        {
            if (server)
            {
                _server?.IterateOutgoing();
            }
            else
            {
                // Client outgoing is handled in Send methods
            }
        }

        #endregion

        #region Configuration

        public override int GetMTU(byte channel)
        {
            // With internal fragmentation, we can handle large packets
            // Return a reasonable max that won't cause excessive fragmentation
            // 64KB is a common upper limit for networked games
            return 65535;
        }

        public override float GetTimeout(bool asServer)
        {
            return _timeout;
        }

        public override void SetTimeout(float value, bool asServer)
        {
            _timeout = value;
        }

        public override int GetMaximumClients()
        {
            return _maxClients;
        }

        public override void SetMaximumClients(int value)
        {
            _maxClients = value;
        }

        public override void SetClientAddress(string address)
        {
            _remoteProductUserId = address;
        }

        public override string GetClientAddress()
        {
            return _remoteProductUserId;
        }

        public override bool IsLocalTransport(int connectionId)
        {
            return connectionId == CLIENT_HOST_ID;
        }

        /// <summary>
        /// Gets or sets the relay control setting.
        /// ForceRelays (default) protects user IP addresses.
        /// </summary>
        public RelayControl RelayControlSetting
        {
            get => _relayControl;
            set
            {
                _relayControl = value;
                ApplyRelayControl();
            }
        }

        /// <summary>
        /// Applies the relay control setting to the P2P interface.
        /// </summary>
        private void ApplyRelayControl()
        {
            var p2p = EOSManager.Instance?.P2PInterface;
            if (p2p == null) return;

            var options = new SetRelayControlOptions { RelayControl = _relayControl };
            var result = p2p.SetRelayControl(ref options);

            if (result == Result.Success)
            {
                NetworkManager?.Log($"[EOSNativeTransport] Relay control set to: {_relayControl}");
            }
            else
            {
                NetworkManager?.LogWarning($"[EOSNativeTransport] Failed to set relay control: {result}");
            }
        }

        #endregion
    }
}
