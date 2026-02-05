using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// DIY Discord Rich Presence - Zero external dependencies.
    /// Uses raw named pipes and JSON to communicate with Discord client.
    /// </summary>
    public class EOSDiscordPresence : MonoBehaviour
    {
        public static EOSDiscordPresence Instance { get; private set; }

        [Header("Discord Application")]
        [Tooltip("Your Discord Application ID from discord.com/developers")]
        public string ApplicationId = "";

        [Header("Presence Settings")]
        [Tooltip("Large image key from Discord Developer Portal")]
        public string LargeImageKey = "game_logo";
        [Tooltip("Small image key for status indicator")]
        public string SmallImageKey = "status_online";

        [Header("Auto-Update")]
        [Tooltip("Automatically update presence based on lobby state")]
        public bool AutoUpdate = true;
        [Tooltip("Update interval in seconds")]
        public float UpdateInterval = 15f;

        // Connection state
        private NamedPipeClientStream _pipe;
        private Thread _readThread;
        private bool _connected;
        private bool _disposed;
        private readonly object _lock = new object();

        // Presence state
        private string _currentState = "";
        private string _currentDetails = "";
        private int _currentPartySize;
        private int _currentPartyMax;
        private string _currentPartyId = "";
        private long _startTimestamp;

        // Events
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        /// <summary>
        /// Whether currently connected to Discord.
        /// </summary>
        public bool IsConnected => _connected;

        // Discord IPC opcodes
        private const int OP_HANDSHAKE = 0;
        private const int OP_FRAME = 1;
        private const int OP_CLOSE = 2;
        private const int OP_PING = 3;
        private const int OP_PONG = 4;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(ApplicationId))
            {
                Connect();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (AutoUpdate)
            {
                SubscribeToEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        #region Connection

        /// <summary>
        /// Connect to Discord client via named pipe.
        /// </summary>
        public void Connect()
        {
            if (_connected || string.IsNullOrEmpty(ApplicationId))
                return;

            // Try pipes 0-9 (Discord may use any of them)
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    string pipeName = $"discord-ipc-{i}";

                    #if UNITY_STANDALONE_WIN
                    _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    #else
                    // Unix-like systems use different path
                    string tmpPath = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")
                        ?? Environment.GetEnvironmentVariable("TMPDIR")
                        ?? Environment.GetEnvironmentVariable("TMP")
                        ?? "/tmp";
                    _pipe = new NamedPipeClientStream(".", Path.Combine(tmpPath, pipeName), PipeDirection.InOut, PipeOptions.Asynchronous);
                    #endif

                    _pipe.Connect(1000); // 1 second timeout

                    if (_pipe.IsConnected)
                    {
                        _disposed = false;
                        SendHandshake();
                        StartReadThread();
                        _connected = true;
                        Debug.Log($"[EOSDiscordPresence] Connected to Discord on pipe {i}");
                        OnConnected?.Invoke();
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    // Try next pipe
                    _pipe?.Dispose();
                    _pipe = null;
                }
                catch (Exception ex)
                {
                    _pipe?.Dispose();
                    _pipe = null;
                    Debug.LogWarning($"[EOSDiscordPresence] Pipe {i} error: {ex.Message}");
                }
            }

            Debug.LogWarning("[EOSDiscordPresence] Could not connect to Discord - is Discord running?");
            OnError?.Invoke("Could not connect to Discord");
        }

        /// <summary>
        /// Disconnect from Discord.
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                _disposed = true;
                _connected = false;

                if (_pipe != null)
                {
                    try
                    {
                        if (_pipe.IsConnected)
                        {
                            // Send close frame
                            SendFrame(OP_CLOSE, "{}");
                        }
                        _pipe.Dispose();
                    }
                    catch { }
                    _pipe = null;
                }
            }

            OnDisconnected?.Invoke();
            Debug.Log("[EOSDiscordPresence] Disconnected from Discord");
        }

        private void SendHandshake()
        {
            string payload = $"{{\"v\":1,\"client_id\":\"{ApplicationId}\"}}";
            SendFrame(OP_HANDSHAKE, payload);
        }

        private void SendFrame(int opcode, string payload)
        {
            if (_pipe == null || !_pipe.IsConnected)
                return;

            try
            {
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
                byte[] header = new byte[8];

                // Little-endian opcode (4 bytes)
                header[0] = (byte)(opcode & 0xFF);
                header[1] = (byte)((opcode >> 8) & 0xFF);
                header[2] = (byte)((opcode >> 16) & 0xFF);
                header[3] = (byte)((opcode >> 24) & 0xFF);

                // Little-endian length (4 bytes)
                int length = payloadBytes.Length;
                header[4] = (byte)(length & 0xFF);
                header[5] = (byte)((length >> 8) & 0xFF);
                header[6] = (byte)((length >> 16) & 0xFF);
                header[7] = (byte)((length >> 24) & 0xFF);

                lock (_lock)
                {
                    if (_pipe != null && _pipe.IsConnected)
                    {
                        _pipe.Write(header, 0, 8);
                        _pipe.Write(payloadBytes, 0, payloadBytes.Length);
                        _pipe.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EOSDiscordPresence] Send error: {ex.Message}");
                HandleDisconnect();
            }
        }

        private void StartReadThread()
        {
            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "Discord RPC Reader"
            };
            _readThread.Start();
        }

        private void ReadLoop()
        {
            byte[] header = new byte[8];

            while (!_disposed && _pipe != null && _pipe.IsConnected)
            {
                try
                {
                    int bytesRead = _pipe.Read(header, 0, 8);
                    if (bytesRead < 8)
                    {
                        if (!_disposed)
                            HandleDisconnect();
                        return;
                    }

                    int opcode = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                    int length = header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24);

                    if (length > 0 && length < 1024 * 64) // Sanity check
                    {
                        byte[] payload = new byte[length];
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int read = _pipe.Read(payload, totalRead, length - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }

                        string json = Encoding.UTF8.GetString(payload, 0, totalRead);
                        HandleMessage(opcode, json);
                    }

                    if (opcode == OP_PING)
                    {
                        SendFrame(OP_PONG, "{}");
                    }
                    else if (opcode == OP_CLOSE)
                    {
                        HandleDisconnect();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (!_disposed)
                    {
                        Debug.LogWarning($"[EOSDiscordPresence] Read error: {ex.Message}");
                        HandleDisconnect();
                    }
                    return;
                }
            }
        }

        private void HandleMessage(int opcode, string json)
        {
            // Parse response for errors
            if (json.Contains("\"code\":"))
            {
                Debug.LogWarning($"[EOSDiscordPresence] Discord error: {json}");
            }
        }

        private void HandleDisconnect()
        {
            if (!_disposed)
            {
                _connected = false;
                MainThreadDispatcher.Enqueue(() => OnDisconnected?.Invoke());
            }
        }

        #endregion

        #region Presence Updates

        /// <summary>
        /// Update Discord Rich Presence with full control.
        /// </summary>
        public void UpdatePresence(string details, string state = null, int partySize = 0, int partyMax = 0,
            string partyId = null, long startTimestamp = 0, string largeImageKey = null, string largeImageText = null,
            string smallImageKey = null, string smallImageText = null)
        {
            if (!_connected)
            {
                Connect();
                if (!_connected) return;
            }

            _currentDetails = details;
            _currentState = state;
            _currentPartySize = partySize;
            _currentPartyMax = partyMax;
            _currentPartyId = partyId;
            _startTimestamp = startTimestamp;

            var activity = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(details))
                activity["details"] = details;

            if (!string.IsNullOrEmpty(state))
                activity["state"] = state;

            // Timestamps
            if (startTimestamp > 0)
            {
                activity["timestamps"] = new Dictionary<string, object>
                {
                    ["start"] = startTimestamp
                };
            }

            // Assets (images)
            var assets = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(largeImageKey ?? LargeImageKey))
            {
                assets["large_image"] = largeImageKey ?? LargeImageKey;
                if (!string.IsNullOrEmpty(largeImageText))
                    assets["large_text"] = largeImageText;
            }
            if (!string.IsNullOrEmpty(smallImageKey ?? SmallImageKey))
            {
                assets["small_image"] = smallImageKey ?? SmallImageKey;
                if (!string.IsNullOrEmpty(smallImageText))
                    assets["small_text"] = smallImageText;
            }
            if (assets.Count > 0)
                activity["assets"] = assets;

            // Party
            if (partySize > 0 && partyMax > 0 && !string.IsNullOrEmpty(partyId))
            {
                activity["party"] = new Dictionary<string, object>
                {
                    ["id"] = partyId,
                    ["size"] = new int[] { partySize, partyMax }
                };
            }

            // Build the SET_ACTIVITY command
            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new Dictionary<string, object>
                {
                    ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["activity"] = activity
                },
                ["nonce"] = Guid.NewGuid().ToString()
            };

            string json = DictToJson(payload);
            SendFrame(OP_FRAME, json);
        }

        /// <summary>
        /// Clear Discord Rich Presence.
        /// </summary>
        public void ClearPresence()
        {
            if (!_connected) return;

            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new Dictionary<string, object>
                {
                    ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                    ["activity"] = null
                },
                ["nonce"] = Guid.NewGuid().ToString()
            };

            string json = DictToJson(payload);
            SendFrame(OP_FRAME, json);

            _currentDetails = "";
            _currentState = "";
            _currentPartySize = 0;
            _currentPartyMax = 0;
            _currentPartyId = "";
            _startTimestamp = 0;
        }

        /// <summary>
        /// Simple presence update - just details and state.
        /// </summary>
        public void SetPresence(string details, string state = null)
        {
            UpdatePresence(details, state);
        }

        /// <summary>
        /// Set presence showing party/lobby info.
        /// </summary>
        public void SetLobbyPresence(string details, string lobbyCode, int currentPlayers, int maxPlayers)
        {
            UpdatePresence(
                details: details,
                state: $"Lobby: {lobbyCode}",
                partyId: lobbyCode,
                partySize: currentPlayers,
                partyMax: maxPlayers,
                startTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            );
        }

        #endregion

        #region Auto-Integration

        private float _lastUpdateTime;

        private void Update()
        {
            if (!AutoUpdate || !_connected) return;

            // Periodic refresh
            if (Time.time - _lastUpdateTime > UpdateInterval)
            {
                _lastUpdateTime = Time.time;
                RefreshPresenceFromLobby();
            }
        }

        private void SubscribeToEvents()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined += HandleLobbyJoined;
                lobbyManager.OnLobbyLeft += HandleLobbyLeft;
                lobbyManager.OnMemberJoined += HandleMemberChanged;
                lobbyManager.OnMemberLeft += HandleMemberChanged;
            }
        }

        private void UnsubscribeFromEvents()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined -= HandleLobbyJoined;
                lobbyManager.OnLobbyLeft -= HandleLobbyLeft;
                lobbyManager.OnMemberJoined -= HandleMemberChanged;
                lobbyManager.OnMemberLeft -= HandleMemberChanged;
            }
        }

        private void HandleLobbyJoined(LobbyData lobby)
        {
            SetLobbyPresence(
                details: "In Lobby",
                lobbyCode: lobby.Code,
                currentPlayers: lobby.MemberCount,
                maxPlayers: lobby.MaxMembers
            );
        }

        private void HandleLobbyLeft()
        {
            SetPresence("In Menu");
        }

        private void HandleMemberChanged(string puid)
        {
            RefreshPresenceFromLobby();
        }

        private void RefreshPresenceFromLobby()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null || !lobbyManager.IsInLobby)
            {
                if (!string.IsNullOrEmpty(_currentPartyId))
                {
                    SetPresence("In Menu");
                }
                return;
            }

            var lobby = lobbyManager.CurrentLobby;
            SetLobbyPresence(
                details: "In Lobby",
                lobbyCode: lobby.Code,
                currentPlayers: lobby.MemberCount,
                maxPlayers: lobby.MaxMembers
            );
        }

        #endregion

        #region JSON Helpers (No external dependencies)

        private string DictToJson(Dictionary<string, object> dict)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{kvp.Key}\":");
                sb.Append(ObjectToJson(kvp.Value));
            }
            sb.Append("}");
            return sb.ToString();
        }

        private string ObjectToJson(object obj)
        {
            if (obj == null)
                return "null";

            if (obj is string s)
                return $"\"{EscapeString(s)}\"";

            if (obj is bool b)
                return b ? "true" : "false";

            if (obj is int i)
                return i.ToString();

            if (obj is long l)
                return l.ToString();

            if (obj is float f)
                return f.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (obj is double d)
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (obj is int[] arr)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                for (int idx = 0; idx < arr.Length; idx++)
                {
                    if (idx > 0) sb.Append(",");
                    sb.Append(arr[idx]);
                }
                sb.Append("]");
                return sb.ToString();
            }

            if (obj is Dictionary<string, object> dict)
                return DictToJson(dict);

            return $"\"{obj}\"";
        }

        private string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        #endregion
    }

    /// <summary>
    /// Helper to dispatch actions to main thread from background threads.
    /// </summary>
    internal static class MainThreadDispatcher
    {
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static bool _initialized;

        public static void Enqueue(Action action)
        {
            lock (_queue)
            {
                _queue.Enqueue(action);
            }

            if (!_initialized)
            {
                Initialize();
            }
        }

        private static void Initialize()
        {
            _initialized = true;
            var go = new GameObject("MainThreadDispatcher");
            go.AddComponent<MainThreadDispatcherBehaviour>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        private class MainThreadDispatcherBehaviour : MonoBehaviour
        {
            private void Update()
            {
                lock (_queue)
                {
                    while (_queue.Count > 0)
                    {
                        try
                        {
                            _queue.Dequeue()?.Invoke();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                        }
                    }
                }
            }
        }
    }
}
