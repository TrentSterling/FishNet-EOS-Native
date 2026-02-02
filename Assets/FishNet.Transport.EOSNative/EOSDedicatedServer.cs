using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using FishNet.Managing;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Dedicated server support for headless Unity builds.
    /// Auto-starts server when running in batch mode.
    /// Parses command line args for configuration.
    /// </summary>
    public class EOSDedicatedServer : MonoBehaviour
    {
        #region Singleton

        private static EOSDedicatedServer _instance;
        public static EOSDedicatedServer Instance => _instance;

        #endregion

        #region Serialized Fields

        [Header("Server Settings")]
        [SerializeField] private bool _autoStartInBatchMode = true;
        [SerializeField] private string _defaultLobbyName = "Dedicated Server";
        [SerializeField] private string _defaultGameMode = "default";
        [SerializeField] private string _defaultRegion = "default";
        [SerializeField] private int _defaultMaxPlayers = 16;

        [Header("Logging")]
        [SerializeField] private bool _verboseLogging = true;
        [SerializeField] private bool _logToFile = true;
        [SerializeField] private string _logFileName = "server.log";

        #endregion

        #region Public Properties

        /// <summary>True if running in batch/headless mode.</summary>
        public static bool IsBatchMode => Application.isBatchMode;

        /// <summary>True if running as dedicated server (batch mode + this component enabled).</summary>
        public bool IsDedicatedServer => IsBatchMode && enabled;

        /// <summary>Current server uptime.</summary>
        public TimeSpan Uptime => _serverStartTime.HasValue ? DateTime.Now - _serverStartTime.Value : TimeSpan.Zero;

        /// <summary>Server start time.</summary>
        public DateTime? ServerStartTime => _serverStartTime;

        /// <summary>Current player count.</summary>
        public int PlayerCount => _networkManager?.ServerManager?.Clients?.Count ?? 0;

        /// <summary>Max players setting.</summary>
        public int MaxPlayers => _maxPlayers;

        #endregion

        #region Private Fields

        private EOSNativeTransport _transport;
        private NetworkManager _networkManager;
        private DateTime? _serverStartTime;
        private int _maxPlayers;
        private string _lobbyName;
        private string _gameMode;
        private string _region;
        private System.IO.StreamWriter _logWriter;

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

            // Parse command line args
            ParseCommandLineArgs();

            // Setup file logging if enabled
            if (_logToFile && IsBatchMode)
            {
                SetupFileLogging();
            }
        }

        private void Start()
        {
            if (IsBatchMode && _autoStartInBatchMode)
            {
                Log("Dedicated server starting in batch mode...");
                StartCoroutine(AutoStartServerCoroutine());
            }
        }

        private void OnDestroy()
        {
            if (_logWriter != null)
            {
                _logWriter.Flush();
                _logWriter.Close();
                _logWriter = null;
            }

            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit()
        {
            Log("Server shutting down...");
        }

        #endregion

        #region Initialization

        private void ParseCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();
            _maxPlayers = _defaultMaxPlayers;
            _lobbyName = _defaultLobbyName;
            _gameMode = _defaultGameMode;
            _region = _defaultRegion;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-name" when i + 1 < args.Length:
                        _lobbyName = args[++i];
                        break;
                    case "-gamemode" when i + 1 < args.Length:
                        _gameMode = args[++i];
                        break;
                    case "-region" when i + 1 < args.Length:
                        _region = args[++i];
                        break;
                    case "-maxplayers" when i + 1 < args.Length:
                        int.TryParse(args[++i], out _maxPlayers);
                        break;
                    case "-logfile" when i + 1 < args.Length:
                        _logFileName = args[++i];
                        break;
                    case "-noverbose":
                        _verboseLogging = false;
                        break;
                    case "-nolog":
                        _logToFile = false;
                        break;
                }
            }

            if (IsBatchMode)
            {
                Log($"Server config: name={_lobbyName}, mode={_gameMode}, region={_region}, maxPlayers={_maxPlayers}");
            }
        }

        private void SetupFileLogging()
        {
            try
            {
                string logPath = System.IO.Path.Combine(Application.persistentDataPath, _logFileName);
                _logWriter = new System.IO.StreamWriter(logPath, append: true);
                _logWriter.AutoFlush = true;
                Log($"Logging to: {logPath}");
                Application.logMessageReceived += OnLogMessageReceived;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DedicatedServer] Failed to setup file logging: {ex.Message}");
            }
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (_logWriter == null) return;

            string prefix = type switch
            {
                LogType.Error => "[ERROR]",
                LogType.Warning => "[WARN]",
                LogType.Exception => "[EXCEPTION]",
                _ => "[INFO]"
            };

            _logWriter.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {prefix} {condition}");

            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
            {
                _logWriter.WriteLine(stackTrace);
            }
        }

        #endregion

        #region Server Control

        private IEnumerator AutoStartServerCoroutine()
        {
            // Wait for EOS to be ready
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            Log("EOS logged in, finding transport...");

            // Find transport
            _transport = FindAnyObjectByType<EOSNativeTransport>();
            if (_transport == null)
            {
                Log("ERROR: EOSNativeTransport not found!");
                yield break;
            }

            _networkManager = _transport.GetComponent<NetworkManager>();
            if (_networkManager == null)
            {
                Log("ERROR: NetworkManager not found!");
                yield break;
            }

            // Subscribe to events
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

            yield return new WaitForSeconds(0.5f);

            // Start the server
            Log($"Starting dedicated server: {_lobbyName}");
            _ = StartServerAsync(); // Fire and forget - don't await in coroutine
        }

        private async Awaitable StartServerAsync()
        {
            var options = new Lobbies.LobbyCreateOptions
            {
                LobbyName = _lobbyName,
                GameMode = _gameMode,
                Region = _region,
                MaxPlayers = (uint)_maxPlayers,
                EnableVoice = false // Dedicated servers typically don't need voice
            };

            var (result, lobby) = await _transport.HostLobbyAsync(options);

            if (result == Epic.OnlineServices.Result.Success)
            {
                _serverStartTime = DateTime.Now;
                Log($"Server started! Join code: {lobby.JoinCode}");
                Log($"Lobby ID: {lobby.LobbyId}");
            }
            else
            {
                Log($"ERROR: Failed to start server: {result}");
            }
        }

        /// <summary>
        /// Manually start the dedicated server.
        /// </summary>
        public async Awaitable StartServer(string lobbyName = null, string gameMode = null, int? maxPlayers = null)
        {
            _lobbyName = lobbyName ?? _lobbyName;
            _gameMode = gameMode ?? _gameMode;
            _maxPlayers = maxPlayers ?? _maxPlayers;

            await StartServerAsync();
        }

        /// <summary>
        /// Stop the dedicated server.
        /// </summary>
        public async Awaitable StopServer()
        {
            if (_transport != null)
            {
                Log("Stopping server...");
                await _transport.LeaveLobbyAsync();
                _serverStartTime = null;
            }
        }

        #endregion

        #region Event Handlers

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            if (_verboseLogging)
            {
                Log($"Server state: {args.ConnectionState}");
            }
        }

        private void OnRemoteConnectionState(FishNet.Connection.NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            if (_verboseLogging)
            {
                string action = args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Started
                    ? "connected" : "disconnected";
                Log($"Client {conn.ClientId} {action}. Players: {PlayerCount}");
            }
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            string formatted = $"[DedicatedServer] {message}";
            Debug.Log(formatted);

            // In batch mode, also write to console
            if (IsBatchMode)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {formatted}");
            }
        }

        #endregion

        #region Console Commands (for future extension)

        /// <summary>
        /// Process a console command.
        /// </summary>
        public string ProcessCommand(string command)
        {
            var parts = command.Split(' ');
            if (parts.Length == 0) return "Unknown command";

            return parts[0].ToLower() switch
            {
                "status" => $"Uptime: {Uptime:hh\\:mm\\:ss}, Players: {PlayerCount}/{_maxPlayers}",
                "players" => GetPlayerList(),
                "kick" when parts.Length > 1 && int.TryParse(parts[1], out int id) => KickPlayer(id),
                "say" when parts.Length > 1 => BroadcastChatMessage(string.Join(" ", parts.Skip(1))),
                "quit" or "stop" => "Use StopServer() to stop the server",
                "help" => "Commands: status, players, kick <id>, say <message>, quit, help",
                _ => $"Unknown command: {parts[0]}"
            };
        }

        private string GetPlayerList()
        {
            if (_networkManager?.ServerManager?.Clients == null)
                return "Server not running";

            var clients = _networkManager.ServerManager.Clients;
            if (clients.Count == 0)
                return "No players connected";

            return string.Join("\n", clients.Values.Select(c => $"  [{c.ClientId}] Connected"));
        }

        private string KickPlayer(int clientId)
        {
            if (_networkManager?.ServerManager == null)
                return "Server not running";

            _networkManager.ServerManager.Kick(clientId, FishNet.Managing.Server.KickReason.Unset);
            return $"Kicked client {clientId}";
        }

        private string BroadcastChatMessage(string message)
        {
            var chatManager = FindAnyObjectByType<Lobbies.EOSLobbyChatManager>();
            if (chatManager != null)
            {
                chatManager.SendChatMessage($"[SERVER] {message}");
                return $"Broadcast: {message}";
            }
            return "Chat manager not found";
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSDedicatedServer))]
    public class EOSDedicatedServerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var server = (EOSDedicatedServer)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Dedicated Server", EditorStyles.boldLabel);

            // Batch mode indicator
            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Batch Mode", EOSDedicatedServer.IsBatchMode);
            }

            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.TextField("Uptime", server.Uptime.ToString(@"hh\:mm\:ss"));
                    EditorGUILayout.IntField("Players", server.PlayerCount);
                    EditorGUILayout.IntField("Max Players", server.MaxPlayers);
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Manual Control", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Start Server"))
                {
                    _ = server.StartServer();
                }
                if (GUILayout.Button("Stop Server"))
                {
                    _ = server.StopServer();
                }
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "Command Line Args:\n" +
                "  -name <name>       Server name\n" +
                "  -gamemode <mode>   Game mode\n" +
                "  -region <region>   Region\n" +
                "  -maxplayers <n>    Max players\n" +
                "  -logfile <path>    Log file path\n" +
                "  -noverbose         Disable verbose logging\n" +
                "  -nolog             Disable file logging",
                MessageType.Info);
        }
    }
#endif
}
