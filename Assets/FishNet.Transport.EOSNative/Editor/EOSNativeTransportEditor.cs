using UnityEngine;
using UnityEditor;
using FishNet.Transport.EOSNative.Lobbies;

namespace FishNet.Transport.EOSNative.Editor
{
    /// <summary>
    /// Custom editor for EOSNativeTransport with runtime status display,
    /// lobby controls, and connection test controls.
    /// </summary>
    [CustomEditor(typeof(EOSNativeTransport))]
    public class EOSNativeTransportEditor : UnityEditor.Editor
    {
        private EOSNativeTransport _transport;
        private GUIStyle _headerStyle;
        private GUIStyle _statusBoxStyle;
        private GUIStyle _puidStyle;
        private GUIStyle _codeStyle;
        private bool _showTestControls = false; // Collapsed by default (advanced)
        private bool _showLobbyControls = true;
        private string _joinCode = "";
        private string _lobbyStatus = "";
        private bool _lobbyOperationInProgress;
        private bool _autoConnectOnJoin = true;

        private void OnEnable()
        {
            _transport = (EOSNativeTransport)target;
        }

        public override void OnInspectorGUI()
        {
            InitializeStyles();

            // Runtime Status Section (Play Mode Only)
            if (Application.isPlaying)
            {
                DrawRuntimeStatus();
                EditorGUILayout.Space(10);
                DrawLobbyControls();
                EditorGUILayout.Space(10);
            }

            // Draw default inspector
            DrawDefaultInspector();

            // Test Controls Section (Play Mode Only)
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                DrawTestControls();
            }

            // Repaint during play mode to update status
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void InitializeStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 5, 5)
                };
            }

            if (_statusBoxStyle == null)
            {
                _statusBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 10, 10),
                    margin = new RectOffset(0, 0, 5, 5)
                };
            }

            if (_puidStyle == null)
            {
                _puidStyle = new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_codeStyle == null)
            {
                _codeStyle = new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        private void DrawRuntimeStatus()
        {
            EditorGUILayout.BeginVertical(_statusBoxStyle);

            // Header
            EditorGUILayout.LabelField("Runtime Status", _headerStyle);

            // EOS Status
            var eosManager = EOSManager.Instance;
            bool isInitialized = eosManager != null && eosManager.IsInitialized;
            bool isLoggedIn = eosManager != null && eosManager.IsLoggedIn;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("EOS SDK:", GUILayout.Width(80));
            DrawStatusIndicator(isInitialized, "Initialized", "Not Initialized");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Login:", GUILayout.Width(80));
            DrawStatusIndicator(isLoggedIn, "Logged In", "Not Logged In");
            EditorGUILayout.EndHorizontal();

            // Local PUID
            if (isLoggedIn && eosManager.LocalProductUserId != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Local ProductUserId:", EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                string puid = eosManager.LocalProductUserId.ToString();
                EditorGUILayout.SelectableLabel(puid, _puidStyle, GUILayout.Height(18));

                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = puid;
                    Debug.Log($"[EOSNativeTransport] Copied PUID: {puid}");
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            // Connection States
            var serverState = _transport.GetConnectionState(true);
            var clientState = _transport.GetConnectionState(false);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Server:", GUILayout.Width(80));
            DrawConnectionState(serverState);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Client:", GUILayout.Width(80));
            DrawConnectionState(clientState);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawLobbyControls()
        {
            _showLobbyControls = EditorGUILayout.Foldout(_showLobbyControls, "Lobby", true, EditorStyles.foldoutHeader);

            if (!_showLobbyControls) return;

            EditorGUILayout.BeginVertical(_statusBoxStyle);

            var eosManager = EOSManager.Instance;
            bool canUseLobby = eosManager != null && eosManager.IsLoggedIn;

            if (!canUseLobby)
            {
                EditorGUILayout.HelpBox("Waiting for EOS login...", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // Get lobby state
            bool isInLobby = _transport.IsInLobby;
            var currentLobby = _transport.CurrentLobby;

            if (isInLobby)
            {
                // Currently in a lobby - show status and leave button
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Room:", GUILayout.Width(45));

                Color originalColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                EditorGUILayout.LabelField(currentLobby.JoinCode ?? "????", _codeStyle, GUILayout.Width(60), GUILayout.Height(24));
                GUI.backgroundColor = originalColor;

                if (GUILayout.Button("Copy", GUILayout.Width(50)))
                {
                    EditorGUIUtility.systemCopyBuffer = currentLobby.JoinCode;
                    Debug.Log($"[EOSNativeTransport] Copied room code: {currentLobby.JoinCode}");
                }
                EditorGUILayout.EndHorizontal();

                // Status info
                string role = _transport.IsLobbyOwner ? "HOST" : "CLIENT";
                EditorGUILayout.LabelField($"{role} | {currentLobby.MemberCount}/{currentLobby.MaxMembers} players", EditorStyles.miniLabel);

                // Show lobby attributes if set
                string details = "";
                if (!string.IsNullOrEmpty(currentLobby.LobbyName)) details += currentLobby.LobbyName;
                if (!string.IsNullOrEmpty(currentLobby.GameMode)) details += (details.Length > 0 ? " | " : "") + currentLobby.GameMode;
                if (!string.IsNullOrEmpty(currentLobby.Map)) details += (details.Length > 0 ? " | " : "") + currentLobby.Map;
                if (!string.IsNullOrEmpty(details))
                {
                    EditorGUILayout.LabelField(details, EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(5);

                // Show "Connect to Host" button if in lobby but not connected (and not the host)
                var nm = GetNetworkManager();
                bool isConnected = nm != null && (nm.IsServerStarted || nm.IsClientStarted);
                if (!_transport.IsLobbyOwner && !isConnected && !string.IsNullOrEmpty(currentLobby.OwnerPuid))
                {
                    GUI.enabled = !_lobbyOperationInProgress;
                    GUI.backgroundColor = new Color(0.5f, 0.7f, 0.9f);
                    if (GUILayout.Button("Connect to Host", GUILayout.Height(28)))
                    {
                        _transport.ConnectToLobbyHost();
                        _lobbyStatus = "Connecting...";
                    }
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;
                }

                // Leave button (stops everything)
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.5f);
                if (GUILayout.Button("Leave", GUILayout.Height(28)))
                {
                    LeaveLobby();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
            else
            {
                // Not in a lobby - show Host/Join buttons
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Room:", GUILayout.Width(45));
                _joinCode = EditorGUILayout.TextField(_joinCode, _codeStyle, GUILayout.Width(70), GUILayout.Height(24));

                // Limit to 4 characters
                if (_joinCode.Length > 4)
                    _joinCode = _joinCode.Substring(0, 4);

                EditorGUILayout.LabelField("(blank = random)", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                // Auto-connect toggle
                _autoConnectOnJoin = EditorGUILayout.Toggle("Auto-connect on join", _autoConnectOnJoin);

                EditorGUILayout.Space(5);

                EditorGUILayout.BeginHorizontal();

                // HOST button - creates lobby and starts hosting
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(0.5f, 0.9f, 0.5f);
                if (GUILayout.Button("Host", GUILayout.Height(32)))
                {
                    HostLobby();
                }
                GUI.backgroundColor = Color.white;

                // JOIN button - joins lobby (auto-connects based on toggle)
                GUI.enabled = !_lobbyOperationInProgress && _joinCode.Length == 4;
                GUI.backgroundColor = new Color(0.5f, 0.7f, 0.9f);
                if (GUILayout.Button("Join", GUILayout.Height(32)))
                {
                    JoinLobby();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);

                // Quick Match button
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
                if (GUILayout.Button("Quick Match", GUILayout.Height(28)))
                {
                    QuickMatch();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            // Status message
            if (!string.IsNullOrEmpty(_lobbyStatus))
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(_lobbyStatus, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private async void QuickMatch()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Finding match...";
            Repaint();

            var (result, lobby, didHost) = await _transport.QuickMatchOrHostAsync();

            if (result == Epic.OnlineServices.Result.Success)
            {
                _joinCode = lobby.JoinCode;
                if (didHost)
                {
                    _lobbyStatus = $"Hosting: {lobby.JoinCode}";
                }
                else
                {
                    _lobbyStatus = $"Matched: {lobby.JoinCode}";
                }
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
            Repaint();
        }

        private async void HostLobby()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Creating lobby...";
            Repaint();

            // Use entered code or let it auto-generate
            string code = string.IsNullOrEmpty(_joinCode) ? null : _joinCode;
            var (result, lobby) = await _transport.HostLobbyAsync(code);

            if (result == Epic.OnlineServices.Result.Success)
            {
                _lobbyStatus = $"Hosting room {lobby.JoinCode}";
                _joinCode = lobby.JoinCode;
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
            Repaint();
        }

        private async void JoinLobby()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = $"Joining {_joinCode}...";
            Repaint();

            // JoinLobbyAsync with auto-connect parameter
            var (result, lobby) = await _transport.JoinLobbyAsync(_joinCode, _autoConnectOnJoin);

            if (result == Epic.OnlineServices.Result.Success)
            {
                if (_autoConnectOnJoin)
                {
                    _lobbyStatus = $"Connected to room {lobby.JoinCode}";
                }
                else
                {
                    _lobbyStatus = $"In lobby {lobby.JoinCode} (not connected)";
                }
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
            Repaint();
        }

        private async void LeaveLobby()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Leaving...";
            Repaint();

            await _transport.LeaveLobbyAsync();

            _lobbyStatus = "";
            _joinCode = "";
            _lobbyOperationInProgress = false;
            Repaint();
        }

        private void DrawStatusIndicator(bool isActive, string activeText, string inactiveText)
        {
            Color originalColor = GUI.backgroundColor;

            if (isActive)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                GUILayout.Label(activeText, EditorStyles.miniButton);
            }
            else
            {
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                GUILayout.Label(inactiveText, EditorStyles.miniButton);
            }

            GUI.backgroundColor = originalColor;
        }

        private void DrawConnectionState(FishNet.Transporting.LocalConnectionState state)
        {
            Color originalColor = GUI.backgroundColor;
            string stateText = state.ToString();

            switch (state)
            {
                case FishNet.Transporting.LocalConnectionState.Started:
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    break;
                case FishNet.Transporting.LocalConnectionState.Starting:
                    GUI.backgroundColor = new Color(0.8f, 0.8f, 0.3f);
                    break;
                case FishNet.Transporting.LocalConnectionState.Stopping:
                    GUI.backgroundColor = new Color(0.8f, 0.6f, 0.3f);
                    break;
                case FishNet.Transporting.LocalConnectionState.Stopped:
                default:
                    GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
                    break;
            }

            GUILayout.Label(stateText, EditorStyles.miniButton);
            GUI.backgroundColor = originalColor;
        }

        private void DrawTestControls()
        {
            _showTestControls = EditorGUILayout.Foldout(_showTestControls, "Advanced (Direct P2P)", true, EditorStyles.foldoutHeader);

            if (!_showTestControls) return;

            EditorGUILayout.BeginVertical(_statusBoxStyle);

            EditorGUILayout.HelpBox("For testing without lobbies. Use Lobby controls above for normal gameplay.", MessageType.Info);

            var eosManager = EOSManager.Instance;
            bool canConnect = eosManager != null && eosManager.IsLoggedIn;

            EditorGUI.BeginDisabledGroup(!canConnect);

            var serverState = _transport.GetConnectionState(true);
            var clientState = _transport.GetConnectionState(false);
            bool serverRunning = serverState == FishNet.Transporting.LocalConnectionState.Started;
            bool clientRunning = clientState == FishNet.Transporting.LocalConnectionState.Started;
            bool anyRunning = serverRunning || clientRunning;

            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(serverRunning);
            if (GUILayout.Button("Server"))
            {
                GetNetworkManager()?.ServerManager.StartConnection();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(clientRunning);
            if (GUILayout.Button("Client"))
            {
                GetNetworkManager()?.ClientManager.StartConnection();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!anyRunning);
            if (GUILayout.Button("Stop"))
            {
                _transport.StopHost();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();
        }

        private FishNet.Managing.NetworkManager GetNetworkManager()
        {
            var nm = _transport.GetComponent<FishNet.Managing.NetworkManager>();
            if (nm == null)
            {
                nm = UnityEngine.Object.FindAnyObjectByType<FishNet.Managing.NetworkManager>();
            }
            return nm;
        }
    }
}
