using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Managing;
using FishNet.Transport.EOSNative.Migration;
using FishNet.Transport.EOSNative.Lobbies;

namespace FishNet.Transport.EOSNative.DebugUI
{
    /// <summary>
    /// DEPRECATED: Network debug is now integrated into EOSNativeUI (F1 -> Network tab).
    /// This component is kept for backwards compatibility but is disabled by default.
    /// </summary>
    [AddComponentMenu("")] // Hide from menu
    public class EOSNetworkDebugPanel : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("DEPRECATED: Use EOSNativeUI (F1) -> Network tab instead.")]
        [SerializeField] private Key _toggleKey = Key.F4;
        [SerializeField] private bool _showPanel = false;
        [SerializeField] private bool _enabled = false; // Disabled by default
        [SerializeField] private Rect _windowRect = new Rect(610, 10, 300, 450);

        [Header("References")]
        [SerializeField] private NetworkManager _networkManager;

        private EOSNativeTransport _transport;
        private Vector2 _scrollPosition;

        // Bandwidth tracking
        private long _lastBytesSent;
        private long _lastBytesReceived;
        private float _lastBandwidthUpdate;
        private float _inboundKBps;
        private float _outboundKBps;
        private readonly Queue<float> _inboundHistory = new();
        private readonly Queue<float> _outboundHistory = new();
        private const int HISTORY_SIZE = 60;
        private const float UPDATE_INTERVAL = 0.5f;

        // GUI Styles
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _greenStyle;
        private GUIStyle _yellowStyle;
        private GUIStyle _redStyle;
        private GUIStyle _orangeStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _miniLabelStyle;
        private bool _stylesInitialized;
        private Texture2D _darkBgTexture;
        private Texture2D _sectionBgTexture;
        private Texture2D _graphBgTexture;
        private Texture2D _graphLineTexture;

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = FindAnyObjectByType<NetworkManager>();

            if (_networkManager != null)
                _transport = _networkManager.TransportManager.Transport as EOSNativeTransport;
        }

        private void Update()
        {
            if (!_enabled) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
            {
                _showPanel = !_showPanel;
            }

            // Update bandwidth
            if (Time.realtimeSinceStartup - _lastBandwidthUpdate >= UPDATE_INTERVAL)
            {
                UpdateBandwidth();
                _lastBandwidthUpdate = Time.realtimeSinceStartup;
            }
        }

        private void UpdateBandwidth()
        {
            if (_transport == null)
            {
                _inboundKBps = 0;
                _outboundKBps = 0;
                return;
            }

            long currentSent = _transport.TotalBytesSent;
            long currentReceived = _transport.TotalBytesReceived;

            _outboundKBps = (currentSent - _lastBytesSent) / 1024f / UPDATE_INTERVAL;
            _inboundKBps = (currentReceived - _lastBytesReceived) / 1024f / UPDATE_INTERVAL;

            _lastBytesSent = currentSent;
            _lastBytesReceived = currentReceived;

            // Add to history
            _inboundHistory.Enqueue(_inboundKBps);
            _outboundHistory.Enqueue(_outboundKBps);

            while (_inboundHistory.Count > HISTORY_SIZE) _inboundHistory.Dequeue();
            while (_outboundHistory.Count > HISTORY_SIZE) _outboundHistory.Dequeue();
        }

        private void OnGUI()
        {
            if (!_enabled || !_showPanel) return;

            InitializeStyles();
            _windowRect = GUI.Window(101, _windowRect, DrawWindow, "", _windowStyle);

            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _darkBgTexture = MakeTexture(2, 2, new Color(0.1f, 0.12f, 0.18f, 0.95f));
            _sectionBgTexture = MakeTexture(2, 2, new Color(0.15f, 0.17f, 0.22f, 1f));
            _graphBgTexture = MakeTexture(2, 2, new Color(0.08f, 0.1f, 0.14f, 1f));
            _graphLineTexture = MakeTexture(2, 2, new Color(0.3f, 0.7f, 1f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(8, 8, 8, 8),
                normal = { background = _darkBgTexture, textColor = Color.white }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.7f, 0.4f) },
                margin = new RectOffset(0, 0, 5, 8)
            };

            _sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.85f, 0.9f) },
                margin = new RectOffset(0, 0, 6, 3)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.75f) }
            };

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _greenStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(0.4f, 1f, 0.4f) } };
            _yellowStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
            _redStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.4f, 0.4f) } };
            _orangeStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.6f, 0.3f) } };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 26
            };

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _sectionBgTexture },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 3, 3)
            };

            _miniLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
            };

            _stylesInitialized = true;
        }

        private static Texture2D MakeTexture(int w, int h, Color color)
        {
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label("NETWORK DEBUG", _headerStyle);
            GUILayout.Label($"Press {_toggleKey} to toggle", _miniLabelStyle);

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            DrawP2PSection();
            GUILayout.Space(4);
            DrawBandwidthSection();
            GUILayout.Space(4);
            DrawMigrationSection();
            GUILayout.Space(4);
            DrawConnectionsSection();

            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 40));
        }

        private void DrawP2PSection()
        {
            GUILayout.Label("P2P STATUS", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            if (_transport == null)
            {
                GUILayout.Label("Transport not found", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            // Server/Client state
            var serverState = _transport.GetConnectionState(true);
            var clientState = _transport.GetConnectionState(false);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Server:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(serverState.ToString(), GetStateStyle(serverState));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Client:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(clientState.ToString(), GetStateStyle(clientState));
            GUILayout.EndHorizontal();

            // Socket info
            GUILayout.BeginHorizontal();
            GUILayout.Label("Socket:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(_transport.SocketName, _valueStyle);
            GUILayout.EndHorizontal();

            // Relay control
            GUILayout.BeginHorizontal();
            GUILayout.Label("Relay:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(_transport.RelayControlSetting.ToString(), _valueStyle);
            GUILayout.EndHorizontal();

            // Remote PUID (if client)
            if (!string.IsNullOrEmpty(_transport.RemoteProductUserId))
            {
                string shortPuid = _transport.RemoteProductUserId.Length > 16
                    ? _transport.RemoteProductUserId.Substring(0, 8) + "..." + _transport.RemoteProductUserId.Substring(_transport.RemoteProductUserId.Length - 6)
                    : _transport.RemoteProductUserId;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Remote:", _labelStyle, GUILayout.Width(60));
                GUILayout.Label(shortPuid, _valueStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawBandwidthSection()
        {
            GUILayout.Label("BANDWIDTH", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            // Current rates
            GUILayout.BeginHorizontal();
            GUILayout.Label("In:", _labelStyle, GUILayout.Width(30));
            GUILayout.Label($"{_inboundKBps:F1} KB/s", _greenStyle, GUILayout.Width(80));
            GUILayout.Label("Out:", _labelStyle, GUILayout.Width(30));
            GUILayout.Label($"{_outboundKBps:F1} KB/s", _orangeStyle);
            GUILayout.EndHorizontal();

            // Totals
            if (_transport != null)
            {
                float totalInMB = _transport.TotalBytesReceived / (1024f * 1024f);
                float totalOutMB = _transport.TotalBytesSent / (1024f * 1024f);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Total:", _labelStyle, GUILayout.Width(40));
                GUILayout.Label($"In: {totalInMB:F2} MB  Out: {totalOutMB:F2} MB", _miniLabelStyle);
                GUILayout.EndHorizontal();
            }

            // Simple graph
            DrawBandwidthGraph();

            GUILayout.EndVertical();
        }

        private void DrawBandwidthGraph()
        {
            Rect graphRect = GUILayoutUtility.GetRect(260, 50);
            GUI.DrawTexture(graphRect, _graphBgTexture);

            if (_inboundHistory.Count < 2) return;

            float maxValue = 10f; // Min 10 KB/s scale
            foreach (float v in _inboundHistory) maxValue = Mathf.Max(maxValue, v);
            foreach (float v in _outboundHistory) maxValue = Mathf.Max(maxValue, v);

            // Draw inbound line (green)
            DrawGraphLine(graphRect, _inboundHistory, maxValue, new Color(0.3f, 0.8f, 0.4f, 0.8f));

            // Draw outbound line (orange)
            DrawGraphLine(graphRect, _outboundHistory, maxValue, new Color(1f, 0.6f, 0.3f, 0.8f));

            // Scale label
            GUI.Label(new Rect(graphRect.x + 2, graphRect.y + 2, 60, 15),
                $"Max: {maxValue:F0} KB/s", _miniLabelStyle);
        }

        private void DrawGraphLine(Rect rect, Queue<float> data, float maxVal, Color color)
        {
            if (data.Count < 2) return;

            float[] values = data.ToArray();
            float stepX = rect.width / (HISTORY_SIZE - 1);

            for (int i = 1; i < values.Length; i++)
            {
                float x1 = rect.x + (i - 1) * stepX;
                float x2 = rect.x + i * stepX;
                float y1 = rect.y + rect.height - (values[i - 1] / maxVal * rect.height);
                float y2 = rect.y + rect.height - (values[i] / maxVal * rect.height);

                DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), color, 2f);
            }
        }

        private void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var originalColor = GUI.color;
            GUI.color = color;

            float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(start, end);

            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width / 2, length, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;

            GUI.color = originalColor;
        }

        private void DrawMigrationSection()
        {
            GUILayout.Label("HOST MIGRATION", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            var migrationManager = HostMigrationManager.Instance;
            var lobbyManager = EOSLobbyManager.Instance;

            if (migrationManager == null)
            {
                GUILayout.Label("HostMigrationManager not found", _yellowStyle);
                GUILayout.EndVertical();
                return;
            }

            // Migration state
            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _labelStyle, GUILayout.Width(60));
            string migrationStatus = migrationManager.IsMigrating ? "MIGRATING" : "Ready";
            GUIStyle migrationStyle = migrationManager.IsMigrating ? _orangeStyle : _greenStyle;
            GUILayout.Label(migrationStatus, migrationStyle);
            GUILayout.EndHorizontal();

            // Host info
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Role:", _labelStyle, GUILayout.Width(60));
                GUILayout.Label(lobbyManager.IsOwner ? "HOST" : "CLIENT",
                    lobbyManager.IsOwner ? _greenStyle : _valueStyle);
                GUILayout.EndHorizontal();

                string ownerPuid = lobbyManager.CurrentLobby.OwnerPuid ?? "Unknown";
                string shortOwner = ownerPuid.Length > 12 ? ownerPuid.Substring(0, 8) + "..." : ownerPuid;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Host:", _labelStyle, GUILayout.Width(60));
                GUILayout.Label(shortOwner, _valueStyle);
                GUILayout.EndHorizontal();
            }

            // Debug controls
            GUILayout.Space(6);
            GUILayout.Label("Debug Controls", _miniLabelStyle);

            GUILayout.BeginHorizontal();

            GUI.enabled = !migrationManager.IsMigrating;
            GUI.backgroundColor = new Color(0.7f, 0.7f, 0.3f);
            if (GUILayout.Button("Save State", _buttonStyle))
            {
                migrationManager.DebugTriggerSave();
            }

            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.7f);
            if (GUILayout.Button("Finish Migration", _buttonStyle))
            {
                migrationManager.DebugTriggerFinish();
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawConnectionsSection()
        {
            GUILayout.Label("CONNECTIONS", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            if (_networkManager == null)
            {
                GUILayout.Label("NetworkManager not found", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            if (_networkManager.IsServerStarted)
            {
                int clientCount = _networkManager.ServerManager.Clients.Count;
                GUILayout.Label($"Server: {clientCount} clients connected", _greenStyle);

                // List clients with short PUIDs
                foreach (var kvp in _networkManager.ServerManager.Clients)
                {
                    int connId = kvp.Key;
                    string puid = _transport?.GetPuidForConnection(connId) ?? "Unknown";
                    string shortPuid = puid.Length > 12 ? puid.Substring(0, 8) + "..." : puid;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  [{connId}]", _labelStyle, GUILayout.Width(40));
                    GUILayout.Label(shortPuid, _miniLabelStyle);
                    GUILayout.EndHorizontal();
                }
            }
            else if (_networkManager.IsClientStarted)
            {
                GUILayout.Label("Connected as client", _greenStyle);
            }
            else
            {
                GUILayout.Label("Not connected", _labelStyle);
            }

            // Lobby sync
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                int lobbyCount = lobbyManager.CurrentLobby.MemberCount;
                int fishnetCount = _networkManager.IsServerStarted ? _networkManager.ServerManager.Clients.Count :
                                   _networkManager.IsClientStarted ? 1 : 0;

                string syncStatus;
                GUIStyle syncStyle;

                if (!_networkManager.IsServerStarted && !_networkManager.IsClientStarted)
                {
                    syncStatus = "WAITING";
                    syncStyle = _yellowStyle;
                }
                else if (lobbyCount == fishnetCount)
                {
                    syncStatus = "SYNCED";
                    syncStyle = _greenStyle;
                }
                else
                {
                    syncStatus = $"MISMATCH ({lobbyCount} lobby / {fishnetCount} fishnet)";
                    syncStyle = _orangeStyle;
                }

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Lobby Sync:", _labelStyle, GUILayout.Width(75));
                GUILayout.Label(syncStatus, syncStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private GUIStyle GetStateStyle(FishNet.Transporting.LocalConnectionState state)
        {
            return state switch
            {
                FishNet.Transporting.LocalConnectionState.Started => _greenStyle,
                FishNet.Transporting.LocalConnectionState.Starting => _yellowStyle,
                FishNet.Transporting.LocalConnectionState.Stopping => _orangeStyle,
                _ => _labelStyle
            };
        }
    }
}
