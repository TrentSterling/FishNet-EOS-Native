using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Managing;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Migration;
using FishNet.Transport.EOSNative.Replay;
using FishNet.Transport.EOSNative.Voice;
using FishNet.Transport.EOSNative.Social;
using FishNet.Transport.EOSNative.Storage;
using FishNet.Transport.EOSNative.AntiCheat;
using Epic.OnlineServices;
using Epic.OnlineServices.Reports;
using Epic.OnlineServices.RTCAudio;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Main unified debug UI for EOS Native Transport.
    /// Toggle with F1 key. Has tabs: Main, Voice, Network.
    /// </summary>
    public class EOSNativeUI : MonoBehaviour
    {
        [Header("UI Settings")]
        [SerializeField] private bool _showUI = true;
        [SerializeField] private Key _toggleKey = Key.F1;
        [SerializeField] private float _windowWidth = 380f;
        [SerializeField] private float _windowHeightPercent = 0.9f; // 90% of screen height

        // Tab system
        private enum Tab { Main, Voice, Network }
        private Tab _currentTab = Tab.Main;

        [Header("References")]
        [SerializeField] private NetworkManager _networkManager;

        private Rect _windowRect;
        private EOSNativeTransport _transport;
        private string _remoteProductUserId = "";
        private string _joinCode = "";
        private string _lobbyStatus = "";
        private Vector2 _mainScrollPosition;
        private Vector2 _lobbyListScrollPosition;
        private Vector2 _memberScrollPosition;
        private bool _lobbyOperationInProgress;
        private List<LobbyData> _foundLobbies = new();
        private bool _showLobbyBrowser;
        private bool _autoConnectOnJoin = true;
        private bool _showAdvanced = false;
        private bool _showMembers = true;
        private bool _showVoiceStatus = true;

        // Chat
        private string _chatInput = "";
        private Vector2 _chatScrollPosition;
        private bool _showChat = true;
        private EOSLobbyChatManager _chatManager;
        private readonly StringBuilder _chatLogBuilder = new();
        private string _playerName = "";

        // Connection monitoring
        private bool _showConnections = true;
        private bool _showPlayers = true;

        // Lobby browser filters
        private string _filterGameMode = "";
        private string _filterRegion = "";
        private string _filterSkillMin = "";
        private string _filterSkillMax = "";
        private bool _filterExcludePassworded = true;
        private bool _filterExcludeInProgress = true;

        // Host migration testing
        private bool _showMigrationTest = false;

        // Recently played
        private bool _showRecentPlayers = false;

        // Custom Invites
        private bool _showInvites = false;
        private string _inviteRecipientPuid = "";
        private EOSCustomInvites _invitesManager;
        private string _inviteStatus = "";

        // Local Friends
        private bool _showFriends = false;
        private Vector2 _friendsScrollPosition;

        // Epic Friends (for Epic Account users)
        private bool _showEpicFriends = false;
        private EOSFriends _friendsManager;
        private Vector2 _epicFriendsScrollPosition;

        // Blocked Players
        private bool _showBlocked = false;
        private Vector2 _blockedScrollPosition;

        // Friend Notes
        private string _editingNotePuid = null;
        private string _editingNoteText = "";

        // Stats & Leaderboards
        private bool _showStats = false;
        private Social.EOSStats _statsManager;
        private Social.EOSLeaderboards _leaderboardsManager;
        private Vector2 _statsScrollPosition;
        private Vector2 _leaderboardScrollPosition;
        private string _selectedLeaderboardId = "";
        private List<Social.LeaderboardEntry> _currentLeaderboardEntries = new();
        private string _testStatName = "test_stat";
        private int _testStatAmount = 1;

        // Achievements
        private bool _showAchievements = false;
        private EOSAchievements _achievementsManager;
        private Vector2 _achievementsScrollPosition;

        // Storage
        private bool _showStorage = false;
        private Storage.EOSPlayerDataStorage _storageManager;
        private Vector2 _storageScrollPosition;
        private string _testFileName = "test.txt";

        // Anti-Cheat
        private bool _showAntiCheat = false;
        private EOSAntiCheatManager _antiCheatManager;
        private string _testFileContent = "Hello, EOS!";

        // Metrics
        private bool _showMetrics = false;
        private EOSMetrics _metricsManager;

        // Replay
        private bool _showReplays = false;
        private Replay.EOSReplayRecorder _replayRecorder;
        private Replay.EOSReplayPlayer _replayPlayer;
        private Replay.EOSReplayStorage _replayStorage;
        private Replay.EOSReplayViewer _replayViewer;
        private Vector2 _replayListScroll;
        private List<Replay.ReplayHeader> _cachedReplays = new();
        private float _lastReplayRefresh;
        private string _lastExportPath;
        private bool _showExportSuccess;
        private float _exportSuccessTime;
        private string _importPath = "";

        // Ranked Matchmaking
        private bool _showRanked = false;
        private Social.EOSRankedMatchmaking _rankedManager;
        private string _rankedGameMode = "ranked";
        private string _rankedStatus = "";

        // LFG (Looking for Group)
        private bool _showLFG = false;
        private Social.EOSLFGManager _lfgManager;
        private string _lfgTitle = "Looking for players";
        private string _lfgGameMode = "";
        private int _lfgDesiredSize = 4;
        private string _lfgStatus = "";
        private Vector2 _lfgSearchScrollPosition;

        // Reporting
        private EOSReports _reportsManager;
        private string _reportTargetPuid = "";
        private string _reportStatus = "";
        private bool _showReportPopup = false;
        private int _reportCategoryIndex = 0;

        // Bandwidth tracking
        private long _lastBytesSent;
        private long _lastBytesReceived;
        private float _lastBandwidthUpdate;
        private float _inboundBytesPerSec;
        private float _outboundBytesPerSec;
        private const float BANDWIDTH_UPDATE_INTERVAL = 0.5f;
        private readonly Queue<float> _inboundHistory = new();
        private readonly Queue<float> _outboundHistory = new();

        // Connection quality tracking
        private readonly Queue<long> _pingHistory = new();
        private const int PING_HISTORY_SIZE = 20;
        private float _lastPingUpdate;
        private long _avgPing;
        private long _jitter; // Standard deviation of ping
        private ConnectionQuality _connectionQuality = ConnectionQuality.Unknown;
        private const int HISTORY_SIZE = 60;

        // Voice tab state
        private readonly Dictionary<string, float> _audioLevels = new();
        private readonly Dictionary<string, float> _peakLevels = new();
        private float _localInputLevel;
        private float _localPeakLevel;
        private float _peakDecay = 0.95f;
        private Vector2 _voiceScrollPosition;

        // Network tab state
        private Vector2 _networkScrollPosition;

        // Tab button styles
        private GUIStyle _tabButtonStyle;
        private GUIStyle _tabButtonActiveStyle;
        private Texture2D _graphBgTexture;
        private Texture2D _levelBarBgTexture;
        private Texture2D _levelBarFgTexture;
        private Texture2D _levelBarPeakTexture;

        // GUI Styles
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionHeaderStyle;
        private GUIStyle _foldoutStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _greenStyle;
        private GUIStyle _yellowStyle;
        private GUIStyle _redStyle;
        private GUIStyle _orangeStyle;
        private GUIStyle _cyanStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _smallButtonStyle;
        private GUIStyle _textFieldStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _statsBoxStyle;
        private GUIStyle _miniLabelStyle;
        private GUIStyle _scrollViewStyle;
        private GUIStyle _toggleStyle;
        private bool _stylesInitialized;
        private Texture2D _darkBgTexture;
        private Texture2D _sectionBgTexture;
        private Texture2D _foldoutBgTexture;
        private Texture2D _foldoutHoverTexture;

        private void Awake()
        {
            if (_networkManager == null)
            {
                _networkManager = FindAnyObjectByType<NetworkManager>();
            }

            if (_networkManager != null)
            {
                _transport = _networkManager.TransportManager.Transport as EOSNativeTransport;
            }

            // Get or create chat manager
            _chatManager = EOSLobbyChatManager.Instance;
            if (_chatManager == null)
            {
                var chatGO = new GameObject("EOSLobbyChatManager");
                _chatManager = chatGO.AddComponent<EOSLobbyChatManager>();
            }

            // Get or create invites manager
            _invitesManager = EOSCustomInvites.Instance;

            // Get friends manager (requires Epic Account)
            _friendsManager = EOSFriends.Instance;

            // Get stats, leaderboards, achievements managers
            _statsManager = EOSStats.Instance;
            _leaderboardsManager = EOSLeaderboards.Instance;
            _achievementsManager = EOSAchievements.Instance;
            _storageManager = EOSPlayerDataStorage.Instance;
            _metricsManager = EOSMetrics.Instance;
            _reportsManager = EOSReports.Instance;
            _rankedManager = Social.EOSRankedMatchmaking.Instance;

            // Player name will be synced from chat manager once generated
            // (Chat manager generates name from PUID after EOS login)

            // Initialize window rect
            UpdateWindowRect();
        }

        private void UpdateWindowRect()
        {
            float height = Mathf.Min(Screen.height * _windowHeightPercent, Screen.height - 20);
            _windowRect = new Rect(10, 10, _windowWidth, height);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
            {
                _showUI = !_showUI;
            }

            // Update bandwidth tracking
            if (Time.realtimeSinceStartup - _lastBandwidthUpdate >= BANDWIDTH_UPDATE_INTERVAL)
            {
                UpdateBandwidth();
                UpdateConnectionQuality();
                _lastBandwidthUpdate = Time.realtimeSinceStartup;
            }

            // Update audio levels for voice tab
            if (_showUI && _currentTab == Tab.Voice)
            {
                UpdateAudioLevels();
            }
        }

        private void UpdateBandwidth()
        {
            if (_transport == null)
            {
                _inboundBytesPerSec = 0;
                _outboundBytesPerSec = 0;
                return;
            }

            long currentSent = _transport.TotalBytesSent;
            long currentReceived = _transport.TotalBytesReceived;

            _outboundBytesPerSec = (currentSent - _lastBytesSent) / BANDWIDTH_UPDATE_INTERVAL;
            _inboundBytesPerSec = (currentReceived - _lastBytesReceived) / BANDWIDTH_UPDATE_INTERVAL;

            _lastBytesSent = currentSent;
            _lastBytesReceived = currentReceived;

            // Add to history for graph
            _inboundHistory.Enqueue(_inboundBytesPerSec / 1024f); // KB/s
            _outboundHistory.Enqueue(_outboundBytesPerSec / 1024f);
            while (_inboundHistory.Count > HISTORY_SIZE) _inboundHistory.Dequeue();
            while (_outboundHistory.Count > HISTORY_SIZE) _outboundHistory.Dequeue();
        }

        private void UpdateConnectionQuality()
        {
            if (_networkManager == null || !_networkManager.IsClientStarted || _networkManager.IsServerStarted)
            {
                _connectionQuality = ConnectionQuality.Unknown;
                _pingHistory.Clear();
                return;
            }

            // Get current ping
            long currentPing = _networkManager.TimeManager.RoundTripTime;

            // Add to history
            _pingHistory.Enqueue(currentPing);
            while (_pingHistory.Count > PING_HISTORY_SIZE)
                _pingHistory.Dequeue();

            // Calculate average ping
            long sum = 0;
            foreach (var p in _pingHistory)
                sum += p;
            _avgPing = _pingHistory.Count > 0 ? sum / _pingHistory.Count : currentPing;

            // Calculate jitter (standard deviation)
            if (_pingHistory.Count > 1)
            {
                long sumSquaredDiff = 0;
                foreach (var p in _pingHistory)
                {
                    long diff = p - _avgPing;
                    sumSquaredDiff += diff * diff;
                }
                _jitter = (long)Math.Sqrt(sumSquaredDiff / _pingHistory.Count);
            }
            else
            {
                _jitter = 0;
            }

            // Determine connection quality
            // Based on ping and jitter thresholds
            if (_avgPing < 50 && _jitter < 10)
                _connectionQuality = ConnectionQuality.Excellent;
            else if (_avgPing < 100 && _jitter < 25)
                _connectionQuality = ConnectionQuality.Good;
            else if (_avgPing < 150 && _jitter < 50)
                _connectionQuality = ConnectionQuality.Fair;
            else if (_avgPing < 250)
                _connectionQuality = ConnectionQuality.Poor;
            else
                _connectionQuality = ConnectionQuality.Bad;
        }

        private void UpdateAudioLevels()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected) return;

            // Decay peak levels
            _localPeakLevel *= _peakDecay;
            foreach (var key in new List<string>(_peakLevels.Keys))
            {
                _peakLevels[key] *= _peakDecay;
            }

            // Local input placeholder
            _localInputLevel = voiceManager.IsMuted ? 0f : 0.3f;

            // Get remote participant levels from voice players
            var voicePlayers = FindObjectsByType<EOSVoicePlayer>(FindObjectsSortMode.None);
            foreach (var player in voicePlayers)
            {
                if (string.IsNullOrEmpty(player.ParticipantPuid)) continue;
                float level = Mathf.Clamp01(player.QueuedFrames / 30f);
                if (player.IsSpeaking) level = Mathf.Max(level, 0.5f);
                _audioLevels[player.ParticipantPuid] = level;
                if (!_peakLevels.ContainsKey(player.ParticipantPuid))
                    _peakLevels[player.ParticipantPuid] = 0f;
                _peakLevels[player.ParticipantPuid] = Mathf.Max(_peakLevels[player.ParticipantPuid], level);
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024f * 1024f):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024f:F1} KB";
            return $"{bytes} B";
        }

        private string FormatBandwidth(float bytesPerSec)
        {
            if (bytesPerSec >= 1024 * 1024)
                return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
            if (bytesPerSec >= 1024)
                return $"{bytesPerSec / 1024:F1} KB/s";
            return $"{bytesPerSec:F0} B/s";
        }

        private void OnGUI()
        {
            if (!_showUI) return;

            InitializeStyles();

            // Update window height if screen size changed
            float targetHeight = Mathf.Min(Screen.height * _windowHeightPercent, Screen.height - 20);
            if (Mathf.Abs(_windowRect.height - targetHeight) > 10)
            {
                _windowRect.height = targetHeight;
            }

            _windowRect = GUI.Window(0, _windowRect, DrawWindow, "", _windowStyle);

            // Keep window on screen
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);

            // Draw report popup if active
            if (_showReportPopup)
            {
                DrawReportPopup();
            }
        }

        private void DrawReportPopup()
        {
            // Darken background
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Center popup
            float popupWidth = 300;
            float popupHeight = 200;
            Rect popupRect = new Rect(
                (Screen.width - popupWidth) / 2,
                (Screen.height - popupHeight) / 2,
                popupWidth,
                popupHeight
            );

            GUI.Box(popupRect, "", _boxStyle);
            GUILayout.BeginArea(new Rect(popupRect.x + 10, popupRect.y + 10, popupWidth - 20, popupHeight - 20));

            GUILayout.Label("REPORT PLAYER", _sectionHeaderStyle);

            // Show who we're reporting
            string targetDisplay = _reportTargetPuid.Length > 16
                ? _reportTargetPuid.Substring(0, 8) + "..."
                : _reportTargetPuid;

            if (_chatManager != null)
            {
                string displayName = _chatManager.GetDisplayName(_reportTargetPuid);
                if (!string.IsNullOrEmpty(displayName))
                    targetDisplay = displayName;
            }

            GUILayout.Label($"Target: {targetDisplay}", _labelStyle);
            GUILayout.Space(10);

            // Category selection
            GUILayout.Label("Category:", _labelStyle);
            var categories = EOSReports.GetAllCategories();
            string[] categoryNames = new string[categories.Length];
            for (int i = 0; i < categories.Length; i++)
                categoryNames[i] = EOSReports.GetCategoryDisplayName(categories[i]);

            // Simple button-based selection
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Mathf.Min(4, categories.Length); i++)
            {
                bool isSelected = _reportCategoryIndex == i;
                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.6f, 1f) : Color.white;
                if (GUILayout.Button(categoryNames[i], _smallButtonStyle))
                {
                    _reportCategoryIndex = i;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int i = 4; i < categories.Length; i++)
            {
                bool isSelected = _reportCategoryIndex == i;
                GUI.backgroundColor = isSelected ? new Color(0.3f, 0.6f, 1f) : Color.white;
                if (GUILayout.Button(categoryNames[i], _smallButtonStyle))
                {
                    _reportCategoryIndex = i;
                }
            }
            GUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;

            GUILayout.Space(10);

            // Status message
            if (!string.IsNullOrEmpty(_reportStatus))
            {
                GUILayout.Label(_reportStatus, _miniLabelStyle);
            }

            GUILayout.FlexibleSpace();

            // Action buttons
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
            if (GUILayout.Button("Send Report", _buttonStyle))
            {
                SendReport(categories[_reportCategoryIndex]);
            }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Cancel", _buttonStyle))
            {
                _showReportPopup = false;
                _reportTargetPuid = "";
                _reportStatus = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private async void SendReport(PlayerReportsCategory category)
        {
            if (string.IsNullOrEmpty(_reportTargetPuid) || _reportsManager == null) return;

            _reportStatus = "Sending...";
            var result = await _reportsManager.ReportPlayerAsync(_reportTargetPuid, category);

            if (result == Result.Success)
            {
                _reportStatus = "Report sent!";
                await System.Threading.Tasks.Task.Delay(1000);
                _showReportPopup = false;
                _reportTargetPuid = "";
                _reportStatus = "";
            }
            else
            {
                _reportStatus = $"Failed: {result}";
            }
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            // Create textures
            _darkBgTexture = MakeTexture(2, 2, new Color(0.12f, 0.12f, 0.15f, 0.97f));
            _sectionBgTexture = MakeTexture(2, 2, new Color(0.18f, 0.18f, 0.22f, 1f));
            _foldoutBgTexture = MakeTexture(2, 2, new Color(0.25f, 0.25f, 0.3f, 1f));
            _foldoutHoverTexture = MakeTexture(2, 2, new Color(0.32f, 0.32f, 0.38f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 10, 10),
                normal = { background = _darkBgTexture, textColor = Color.white },
                onNormal = { background = _darkBgTexture, textColor = Color.white }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.4f, 0.8f, 1f) },
                margin = new RectOffset(0, 0, 5, 5)
            };

            _sectionHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                margin = new RectOffset(0, 0, 6, 3)
            };

            // Foldout style - looks like a clickable button/header
            _foldoutStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 24,
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 4, 2),
                normal = { background = _foldoutBgTexture, textColor = new Color(0.85f, 0.85f, 0.9f) },
                hover = { background = _foldoutHoverTexture, textColor = new Color(0.95f, 0.95f, 1f) },
                active = { background = _foldoutHoverTexture, textColor = Color.white },
                onNormal = { background = _foldoutBgTexture, textColor = new Color(0.85f, 0.85f, 0.9f) },
                onHover = { background = _foldoutHoverTexture, textColor = new Color(0.95f, 0.95f, 1f) },
                onActive = { background = _foldoutHoverTexture, textColor = Color.white }
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
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
            _orangeStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.6f, 0.2f) } };
            _cyanStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(0.3f, 0.9f, 1f) } };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 28,
                normal = { textColor = Color.white },
                hover = { textColor = Color.white },
                active = { textColor = Color.white }
            };

            _smallButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fixedHeight = 20,
                normal = { textColor = Color.white },
                hover = { textColor = Color.white },
                active = { textColor = Color.white }
            };

            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _sectionBgTexture },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 2)
            };

            _statsBoxStyle = new GUIStyle(_boxStyle)
            {
                padding = new RectOffset(8, 8, 6, 6)
            };

            _miniLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };

            _scrollViewStyle = new GUIStyle(GUI.skin.scrollView);

            // Toggle style with better visibility
            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                hover = { textColor = Color.white },
                onNormal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                onHover = { textColor = Color.white }
            };

            // Tab button styles
            var tabBg = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.25f, 1f));
            var tabActiveBg = MakeTexture(2, 2, new Color(0.3f, 0.5f, 0.7f, 1f));
            var tabHoverBg = MakeTexture(2, 2, new Color(0.28f, 0.28f, 0.35f, 1f));

            _tabButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                fixedHeight = 32,
                margin = new RectOffset(2, 2, 0, 0),
                normal = { background = tabBg, textColor = new Color(0.7f, 0.7f, 0.75f) },
                hover = { background = tabHoverBg, textColor = Color.white },
                active = { background = tabActiveBg, textColor = Color.white }
            };

            _tabButtonActiveStyle = new GUIStyle(_tabButtonStyle)
            {
                normal = { background = tabActiveBg, textColor = Color.white },
                hover = { background = tabActiveBg, textColor = Color.white }
            };

            // Textures for voice/network tabs
            _graphBgTexture = MakeTexture(2, 2, new Color(0.08f, 0.1f, 0.14f, 1f));
            _levelBarBgTexture = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.25f, 1f));
            _levelBarFgTexture = MakeTexture(2, 2, new Color(0.3f, 0.8f, 0.4f, 1f));
            _levelBarPeakTexture = MakeTexture(2, 2, new Color(1f, 0.9f, 0.3f, 1f));

            _stylesInitialized = true;
        }

        private static Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Draws a clickable foldout header with expand/collapse arrow.
        /// </summary>
        private bool DrawFoldout(bool expanded, string title)
        {
            string arrow = expanded ? "\u25BC" : "\u25B6"; // ▼ or ▶
            if (GUILayout.Button($"{arrow}  {title}", _foldoutStyle))
            {
                return !expanded;
            }
            return expanded;
        }

        private void DrawWindow(int windowId)
        {
            // Header
            GUILayout.Label("EOS DEBUG", _headerStyle);
            GUILayout.Label($"Press {_toggleKey} to toggle", _miniLabelStyle);

            GUILayout.Space(4);

            // Tab buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Main", _currentTab == Tab.Main ? _tabButtonActiveStyle : _tabButtonStyle))
                _currentTab = Tab.Main;
            if (GUILayout.Button("Voice", _currentTab == Tab.Voice ? _tabButtonActiveStyle : _tabButtonStyle))
                _currentTab = Tab.Voice;
            if (GUILayout.Button("Network", _currentTab == Tab.Network ? _tabButtonActiveStyle : _tabButtonStyle))
                _currentTab = Tab.Network;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Draw the current tab's content
            switch (_currentTab)
            {
                case Tab.Main:
                    DrawMainTab();
                    break;
                case Tab.Voice:
                    DrawVoiceTab();
                    break;
                case Tab.Network:
                    DrawNetworkTab();
                    break;
            }

            // Make window draggable from header area
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 60));
        }

        private void DrawMainTab()
        {
            // Main scroll view - always show vertical scrollbar
            _mainScrollPosition = GUILayout.BeginScrollView(_mainScrollPosition, false, true);

            DrawStatsSection();
            DrawConnectionsSection(); // Only shows for hosts
            DrawPlayersSection(); // Shows tracked player objects
            GUILayout.Space(4);
            DrawLobbySection();
            GUILayout.Space(4);
            DrawMembersSection();
            GUILayout.Space(4);
            DrawVoiceSection();
            GUILayout.Space(4);
            DrawChatSection();
            GUILayout.Space(4);
            DrawRecentPlayersSection();
            GUILayout.Space(4);
            DrawInvitesSection();
            GUILayout.Space(4);
            DrawFriendsSection();
            GUILayout.Space(4);
            DrawBlockedSection();
            GUILayout.Space(4);
            DrawEpicFriendsSection();
            GUILayout.Space(4);
            DrawStatsLeaderboardsSection();
            GUILayout.Space(4);
            DrawRankedSection();
            GUILayout.Space(4);
            DrawLFGSection();
            GUILayout.Space(4);
            DrawAchievementsSection();
            GUILayout.Space(4);
            DrawStorageSection();
            GUILayout.Space(4);
            DrawAntiCheatSection();
            GUILayout.Space(4);
            DrawReplaySection();
            GUILayout.Space(4);
            DrawMetricsSection();
            GUILayout.Space(4);
            DrawMigrationTestSection();
            GUILayout.Space(4);
            DrawAdvancedSection();

            GUILayout.EndScrollView();
        }

        private void DrawStatsSection()
        {
            GUILayout.Label("LIVE STATS", _sectionHeaderStyle);

            GUILayout.BeginVertical(_statsBoxStyle);

            var eosManager = EOSManager.Instance;
            var lobbyManager = EOSLobbyManager.Instance;
            bool isInitialized = eosManager != null && eosManager.IsInitialized;
            bool isLoggedIn = eosManager != null && eosManager.IsLoggedIn;
            bool isInLobby = lobbyManager != null && lobbyManager.IsInLobby;
            bool isServer = _networkManager != null && _networkManager.IsServerStarted;
            bool isClient = _networkManager != null && _networkManager.IsClientStarted;

            // EOS Status Row
            DrawStatRow("EOS:", isInitialized ? "Ready" : "Not Init", isInitialized ? _greenStyle : _redStyle,
                        " | ", isLoggedIn ? "Logged In" : "Not Logged In", isLoggedIn ? _greenStyle : _redStyle);

            // Role - unified view for both host and client
            if (isServer || isClient)
            {
                string role = isServer ? "HOST" : "CLIENT";
                string detail = isServer ? "(Server+Client)" : "(Client only)";
                GUIStyle roleStyle = isServer ? _yellowStyle : _cyanStyle;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Role:", _labelStyle, GUILayout.Width(70));
                GUILayout.Label(role, roleStyle);
                GUILayout.Label($" {detail}", _miniLabelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Players - unified format
            if (isInLobby)
            {
                int lobbyCount = lobbyManager.CurrentLobby.MemberCount;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Players:", _labelStyle, GUILayout.Width(70));

                if (isServer)
                {
                    // Host view - show FishNet/Lobby comparison
                    int fishnetCount = _networkManager.ServerManager.Clients.Count;
                    bool isMatch = fishnetCount == lobbyCount;
                    GUILayout.Label($"{fishnetCount}", _greenStyle);
                    GUILayout.Label($"/{lobbyCount}", isMatch ? _greenStyle : _orangeStyle);
                    GUILayout.Label(isMatch ? " [OK]" : " [MISMATCH]", isMatch ? _greenStyle : _redStyle);
                }
                else
                {
                    // Client view - show lobby count
                    GUILayout.Label($"{lobbyCount}", _greenStyle);
                    GUILayout.Label(" in lobby", _labelStyle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Connected To (for clients) or P2P connections count (for hosts)
            if (isClient && !isServer)
            {
                string remotePuid = _transport?.RemoteProductUserId;
                string shortRemote = remotePuid?.Length > 16 ? remotePuid.Substring(0, 8) + "..." : remotePuid;
                DrawStatRow("Host:", shortRemote ?? "(unknown)", _cyanStyle);
            }
            else if (isServer)
            {
                int p2pCount = _transport?.ServerP2PConnectionCount ?? 0;
                bool hasClientHost = _transport?.HasClientHost ?? false;
                string breakdown = hasClientHost ? $"{p2pCount} P2P + You" : $"{p2pCount} P2P";
                DrawStatRow("Conns:", breakdown, _cyanStyle);
            }

            // Bandwidth - always show if transport exists
            if (_transport != null && (isServer || isClient))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Bandwidth:", _labelStyle, GUILayout.Width(70));
                GUILayout.Label($"In: {FormatBandwidth(_inboundBytesPerSec)}", _cyanStyle);
                GUILayout.Label($" Out: {FormatBandwidth(_outboundBytesPerSec)}", _orangeStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // PUID - always show when logged in
            if (isLoggedIn && eosManager.LocalProductUserId != null)
            {
                string puid = eosManager.LocalProductUserId.ToString();
                string shortPuid = puid.Length > 20 ? puid.Substring(0, 10) + "..." + puid.Substring(puid.Length - 8) : puid;

                GUILayout.BeginHorizontal();
                GUILayout.Label("PUID:", _labelStyle, GUILayout.Width(70));
                GUILayout.Label(shortPuid, _valueStyle);
                if (GUILayout.Button("Copy", _smallButtonStyle, GUILayout.Width(40)))
                {
                    GUIUtility.systemCopyBuffer = puid;
                    _lobbyStatus = "PUID copied!";
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawStatRow(string label, string value, GUIStyle valueStyle, string sep = "", string value2 = "", GUIStyle value2Style = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(70));
            GUILayout.Label(value, valueStyle);
            if (!string.IsNullOrEmpty(sep))
            {
                GUILayout.Label(sep, _labelStyle);
                if (!string.IsNullOrEmpty(value2))
                    GUILayout.Label(value2, value2Style ?? _valueStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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

        private void DrawConnectionsSection()
        {
            // Only show for hosts - they have the detailed connection list
            if (_transport == null || _networkManager == null) return;
            if (!_networkManager.IsServerStarted) return;

            _showConnections = DrawFoldout(_showConnections, "CONNECTION DETAILS");
            if (!_showConnections) return;

            GUILayout.BeginVertical(_boxStyle);

            // List all P2P connections with heartbeat status
            var connections = _transport.GetServerConnectionInfo();
            if (connections.Count > 0)
            {
                GUILayout.Label("Remote Players:", _miniLabelStyle);
                foreach (var (connId, puid, age) in connections)
                {
                    string shortPuid = puid?.Length > 16 ? puid.Substring(0, 8) + "..." + puid.Substring(puid.Length - 4) : puid;
                    GUIStyle ageStyle = age < 2f ? _greenStyle : (age < 4f ? _yellowStyle : _redStyle);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"[{connId}]", _labelStyle, GUILayout.Width(30));
                    GUILayout.Label(shortPuid, _cyanStyle, GUILayout.Width(100));
                    GUILayout.Label($"{age:F1}s ago", ageStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("No remote P2P connections", _miniLabelStyle);
            }

            // Show ClientHost info
            if (_transport.HasClientHost)
            {
                GUILayout.Space(4);
                GUILayout.Label("Local (ClientHost):", _miniLabelStyle);
                string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
                string shortLocal = localPuid?.Length > 16 ? localPuid.Substring(0, 8) + "..." + localPuid.Substring(localPuid.Length - 4) : localPuid;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"[{EOSNativeTransport.CLIENT_HOST_ID}]", _labelStyle, GUILayout.Width(50));
                GUILayout.Label(shortLocal, _yellowStyle, GUILayout.Width(100));
                GUILayout.Label("(you)", _miniLabelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawPlayersSection()
        {
            // Only show if there are tracked players
            int playerCount = EOSNetworkPlayer.PlayerCount;
            if (playerCount == 0) return;

            _showPlayers = DrawFoldout(_showPlayers, $"PLAYER OBJECTS ({playerCount})");
            if (!_showPlayers) return;

            GUILayout.BeginVertical(_boxStyle);

            foreach (var player in EOSNetworkPlayer.AllPlayers)
            {
                if (player == null) continue;

                GUILayout.BeginHorizontal();

                // Connection ID
                GUILayout.Label($"[{player.ConnectionId}]", _labelStyle, GUILayout.Width(50));

                // Display name with ownership indicator
                string nameDisplay = player.DisplayName;
                GUIStyle nameStyle = player.IsLocal ? _yellowStyle : _cyanStyle;
                GUILayout.Label(nameDisplay, nameStyle, GUILayout.Width(100));

                // Ownership
                if (player.IsLocal)
                {
                    GUILayout.Label("(you)", _miniLabelStyle);
                }
                else
                {
                    // Show short PUID for remote players
                    GUILayout.Label(player.ShortPuid, _miniLabelStyle);
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawLobbySection()
        {
            GUILayout.Label("LOBBY", _sectionHeaderStyle);

            GUILayout.BeginVertical(_boxStyle);

            var eosManager = EOSManager.Instance;
            bool canUseLobby = eosManager != null && eosManager.IsLoggedIn;

            if (!canUseLobby)
            {
                GUILayout.Label("Waiting for EOS login...", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            var lobbyManager = EOSLobbyManager.Instance;

            if (lobbyManager.IsInLobby)
            {
                var lobby = lobbyManager.CurrentLobby;
                string role = lobbyManager.IsOwner ? "HOST" : "CLIENT";

                // Room code display (big and prominent) with copy button
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(lobby.JoinCode ?? "????", new GUIStyle(_headerStyle) { fontSize = 32, normal = { textColor = new Color(0.3f, 1f, 0.5f) } });
                if (GUILayout.Button("📋", GUILayout.Width(30), GUILayout.Height(30)))
                {
                    GUIUtility.systemCopyBuffer = lobby.JoinCode;
                    EOSToastManager.Success("Copied!", $"Lobby code {lobby.JoinCode} copied to clipboard");
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{role}  |  {lobby.MemberCount}/{lobby.MaxMembers} players", _labelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Lobby ID (for debugging) with screenshot button
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                string shortId = lobby.LobbyId?.Length > 16 ? lobby.LobbyId.Substring(0, 16) + "..." : lobby.LobbyId;
                GUILayout.Label($"ID: {shortId}", _miniLabelStyle);
                if (GUILayout.Button("📸", GUILayout.Width(25), GUILayout.Height(18)))
                {
                    CopyLobbyStateToClipboard(lobby);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Show lobby attributes
                string details = "";
                if (!string.IsNullOrEmpty(lobby.LobbyName)) details += lobby.LobbyName;
                if (!string.IsNullOrEmpty(lobby.GameMode)) details += (details.Length > 0 ? " | " : "") + lobby.GameMode;
                if (!string.IsNullOrEmpty(lobby.Map)) details += (details.Length > 0 ? " | " : "") + lobby.Map;
                if (!string.IsNullOrEmpty(lobby.Region)) details += (details.Length > 0 ? " | " : "") + lobby.Region;
                if (lobby.SkillLevel > 0) details += (details.Length > 0 ? " | " : "") + $"Skill:{lobby.SkillLevel}";
                if (!string.IsNullOrEmpty(details))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(details, _miniLabelStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(6);

                // Connect button if in lobby but not connected
                bool isConnected = _networkManager != null &&
                    (_networkManager.IsServerStarted || _networkManager.IsClientStarted);
                if (!lobbyManager.IsOwner && !isConnected && !string.IsNullOrEmpty(lobby.OwnerPuid))
                {
                    GUI.enabled = !_lobbyOperationInProgress;
                    GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                    if (GUILayout.Button("CONNECT TO HOST", _buttonStyle))
                    {
                        _transport.ConnectToLobbyHost();
                        _lobbyStatus = "Connecting...";
                    }
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;
                }

                // Leave button
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                if (GUILayout.Button("LEAVE", _buttonStyle))
                {
                    LeaveLobby();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
            else
            {
                // Sync player name from chat manager (it generates from PUID after login)
                if (_chatManager != null && string.IsNullOrEmpty(_playerName) && !string.IsNullOrEmpty(_chatManager.DisplayName))
                {
                    _playerName = _chatManager.DisplayName;
                }

                // Player name input
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", _labelStyle, GUILayout.Width(40));
                string newName = GUILayout.TextField(_playerName, 20, new GUIStyle(_textFieldStyle) { alignment = TextAnchor.MiddleLeft, fontSize = 11 });
                if (newName != _playerName)
                {
                    _playerName = newName;
                    if (_chatManager != null)
                    {
                        _chatManager.DisplayName = _playerName;
                    }
                }
                GUILayout.EndHorizontal();

                // Room code input
                GUILayout.BeginHorizontal();
                GUILayout.Label("Code:", _labelStyle, GUILayout.Width(40));
                _joinCode = GUILayout.TextField(_joinCode, 4, _textFieldStyle, GUILayout.Width(60));
                GUILayout.Label("(blank = random)", _miniLabelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Auto-connect toggle
                _autoConnectOnJoin = GUILayout.Toggle(_autoConnectOnJoin, " Auto-connect on join", _toggleStyle);

                GUILayout.Space(6);

                // Host / Join buttons
                GUILayout.BeginHorizontal();

                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("HOST", _buttonStyle))
                {
                    HostLobby();
                }

                GUI.enabled = !_lobbyOperationInProgress && _joinCode.Length == 4;
                GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
                if (GUILayout.Button("JOIN", _buttonStyle))
                {
                    JoinLobbyAndConnect();
                }

                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // Quick Match button
                GUI.enabled = !_lobbyOperationInProgress;
                GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
                if (GUILayout.Button("QUICK MATCH", _buttonStyle))
                {
                    QuickMatch();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                // Lobby Browser toggle
                GUILayout.Space(6);
                DrawLobbyBrowser();
            }

            // Status message
            if (!string.IsNullOrEmpty(_lobbyStatus))
            {
                GUILayout.Space(4);
                GUILayout.Label(_lobbyStatus, _yellowStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawMembersSection()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null || !lobbyManager.IsInLobby) return;

            _showMembers = DrawFoldout(_showMembers, "LOBBY MEMBERS");
            if (!_showMembers) return;

            GUILayout.BeginVertical(_boxStyle);

            var lobby = lobbyManager.CurrentLobby;
            string ownerPuid = lobby.OwnerPuid;
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();

            // Get member list from lobby details
            var lobbyInterface = EOSManager.Instance?.LobbyInterface;
            if (lobbyInterface != null)
            {
                var detailsOptions = new Epic.OnlineServices.Lobby.CopyLobbyDetailsHandleOptions
                {
                    LocalUserId = EOSManager.Instance.LocalProductUserId,
                    LobbyId = lobby.LobbyId
                };

                if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) == Result.Success && details != null)
                {
                    var countOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberCountOptions();
                    uint memberCount = details.GetMemberCount(ref countOptions);

                    _memberScrollPosition = GUILayout.BeginScrollView(_memberScrollPosition, GUILayout.Height(Mathf.Min(80, memberCount * 22 + 10)));

                    for (uint i = 0; i < memberCount; i++)
                    {
                        var memberOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                        var memberId = details.GetMemberByIndex(ref memberOptions);
                        if (memberId != null)
                        {
                            string memberPuid = memberId.ToString();

                            // Get display name (from cache or generated from PUID)
                            string displayName = _chatManager != null
                                ? _chatManager.GetDisplayName(memberPuid)
                                : (memberPuid.Length > 8 ? memberPuid.Substring(0, 8) : memberPuid);

                            bool isOwner = memberPuid == ownerPuid;
                            bool isLocal = memberPuid == localPuid;

                            GUILayout.BeginHorizontal();
                            string prefix = isOwner ? "[HOST] " : "       ";
                            GUIStyle style = isLocal ? _cyanStyle : (isOwner ? _greenStyle : _labelStyle);
                            GUILayout.Label($"{prefix}{displayName}{(isLocal ? " (you)" : "")}", style);
                            GUILayout.FlexibleSpace();

                            // Report button (not for self)
                            if (!isLocal && _reportsManager != null && _reportsManager.IsReady)
                            {
                                GUI.backgroundColor = new Color(0.9f, 0.5f, 0.2f);
                                if (GUILayout.Button("\u26A0", _smallButtonStyle, GUILayout.Width(22)))
                                {
                                    _reportTargetPuid = memberPuid;
                                    _showReportPopup = true;
                                    _reportStatus = "";
                                }
                                GUI.backgroundColor = Color.white;
                            }

                            GUILayout.EndHorizontal();
                        }
                    }

                    GUILayout.EndScrollView();
                    details.Release();
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawVoiceSection()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            _showVoiceStatus = DrawFoldout(_showVoiceStatus, "VOICE STATUS");
            if (!_showVoiceStatus) return;

            GUILayout.BeginVertical(_boxStyle);

            bool isConnected = voiceManager.IsConnected;
            bool isEnabled = voiceManager.IsVoiceEnabled;
            bool isMuted = voiceManager.IsMuted;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Room:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label(isConnected ? "Connected" : (isEnabled ? "Enabled" : "Not Connected"), isConnected ? _greenStyle : (isEnabled ? _yellowStyle : _labelStyle));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(voiceManager.CurrentRoomName))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Room ID:", _labelStyle, GUILayout.Width(70));
                string shortRoom = voiceManager.CurrentRoomName.Length > 20 ? voiceManager.CurrentRoomName.Substring(0, 20) + "..." : voiceManager.CurrentRoomName;
                GUILayout.Label(shortRoom, _miniLabelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (isConnected || isEnabled)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mic:", _labelStyle, GUILayout.Width(70));
                GUILayout.Label(isMuted ? "MUTED" : "Live", isMuted ? _redStyle : _greenStyle);
                if (GUILayout.Button(isMuted ? "Unmute" : "Mute", _smallButtonStyle, GUILayout.Width(50)))
                {
                    voiceManager.SetMuted(!isMuted);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Show who's speaking
                var speakingParticipants = voiceManager.GetSpeakingParticipants();
                if (speakingParticipants != null && speakingParticipants.Count > 0)
                {
                    GUILayout.Label($"Speaking ({speakingParticipants.Count}):", _miniLabelStyle);
                    foreach (var puid in speakingParticipants)
                    {
                        string shortPuid = puid?.Length > 12 ? puid.Substring(0, 6) + "..." : puid;
                        GUILayout.Label($"  {shortPuid} [SPEAKING]", _greenStyle);
                    }
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawLobbyBrowser()
        {
            // Lobby browser foldout
            _showLobbyBrowser = DrawFoldout(_showLobbyBrowser, $"LOBBY BROWSER {(_foundLobbies.Count > 0 ? $"({_foundLobbies.Count})" : "")}");
            if (!_showLobbyBrowser) return;

            GUILayout.BeginVertical(_boxStyle);

            // Compact filter row (always visible)
            GUILayout.BeginHorizontal();
            GUILayout.Label("\u2699", _labelStyle, GUILayout.Width(15)); // Gear icon
            _filterGameMode = GUILayout.TextField(_filterGameMode, new GUIStyle(_textFieldStyle) { fontSize = 10, alignment = TextAnchor.MiddleLeft }, GUILayout.Width(70));
            if (string.IsNullOrEmpty(_filterGameMode))
            {
                // Placeholder hint
                var lastRect = GUILayoutUtility.GetLastRect();
                GUI.Label(lastRect, "  mode...", _miniLabelStyle);
            }
            _filterRegion = GUILayout.TextField(_filterRegion, new GUIStyle(_textFieldStyle) { fontSize = 10, alignment = TextAnchor.MiddleLeft }, GUILayout.Width(55));
            if (string.IsNullOrEmpty(_filterRegion))
            {
                var lastRect = GUILayoutUtility.GetLastRect();
                GUI.Label(lastRect, "  region", _miniLabelStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Toggle filters row
            GUILayout.BeginHorizontal();
            _filterExcludePassworded = GUILayout.Toggle(_filterExcludePassworded, "\u26D4 No pwd", _toggleStyle, GUILayout.Width(75));
            _filterExcludeInProgress = GUILayout.Toggle(_filterExcludeInProgress, "\u23F8 Not live", _toggleStyle, GUILayout.Width(80));
            if (HasActiveFilters())
            {
                if (GUILayout.Button("\u2715", _smallButtonStyle, GUILayout.Width(22)))
                {
                    ClearFilters();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Search button
            GUI.enabled = !_lobbyOperationInProgress;
            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            string searchText = _lobbyOperationInProgress ? "\u23F3 Searching..." : "\u2315 SEARCH";
            if (GUILayout.Button(searchText, _buttonStyle))
            {
                SearchLobbiesWithFilters();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // Results
            if (_foundLobbies.Count == 0 && !_lobbyOperationInProgress)
            {
                GUILayout.Space(4);
                GUILayout.Label("No lobbies found", _miniLabelStyle);
                GUILayout.Label("Try different filters or host your own!", _miniLabelStyle);
            }
            else if (_foundLobbies.Count > 0)
            {
                GUILayout.Space(4);

                // Calculate dynamic height based on lobby count (min 60, max 180)
                float listHeight = Mathf.Clamp(_foundLobbies.Count * 48, 60, 180);
                _lobbyListScrollPosition = GUILayout.BeginScrollView(_lobbyListScrollPosition, GUILayout.Height(listHeight));

                foreach (var lobby in _foundLobbies)
                {
                    DrawLobbyCard(lobby);
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private bool HasActiveFilters()
        {
            return !string.IsNullOrEmpty(_filterGameMode) ||
                   !string.IsNullOrEmpty(_filterRegion) ||
                   !string.IsNullOrEmpty(_filterSkillMin) ||
                   !string.IsNullOrEmpty(_filterSkillMax) ||
                   !_filterExcludePassworded ||
                   !_filterExcludeInProgress;
        }

        private void ClearFilters()
        {
            _filterGameMode = "";
            _filterRegion = "";
            _filterSkillMin = "";
            _filterSkillMax = "";
            _filterExcludePassworded = true;
            _filterExcludeInProgress = true;
        }

        private void DrawLobbyCard(LobbyData lobby)
        {
            GUILayout.BeginHorizontal(_statsBoxStyle);

            // Join code (prominent)
            var codeStyle = new GUIStyle(_valueStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 1f, 0.5f) }
            };
            GUILayout.Label(lobby.JoinCode ?? "????", codeStyle, GUILayout.Width(45));

            // Info column
            GUILayout.BeginVertical();

            // Player count + icons row
            GUILayout.BeginHorizontal();
            GUIStyle countStyle = lobby.AvailableSlots > 0 ? _greenStyle : _redStyle;
            GUILayout.Label($"{lobby.MemberCount}/{lobby.MaxMembers}", countStyle, GUILayout.Width(30));

            if (lobby.IsPasswordProtected)
                GUILayout.Label("\u26D4", _orangeStyle, GUILayout.Width(16)); // No entry
            if (lobby.IsInProgress)
                GUILayout.Label("\u25B6", _yellowStyle, GUILayout.Width(16)); // Play icon

            // Compact details
            var details = new List<string>();
            if (!string.IsNullOrEmpty(lobby.GameMode)) details.Add(lobby.GameMode);
            if (!string.IsNullOrEmpty(lobby.Map)) details.Add(lobby.Map);
            if (!string.IsNullOrEmpty(lobby.Region)) details.Add(lobby.Region);
            if (details.Count > 0)
            {
                GUILayout.Label(string.Join(" \u2022 ", details), _miniLabelStyle);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Lobby name (if exists)
            if (!string.IsNullOrEmpty(lobby.LobbyName))
            {
                GUILayout.Label(lobby.LobbyName, _miniLabelStyle);
            }

            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Join button
            bool canJoin = lobby.AvailableSlots > 0 && !lobby.IsPasswordProtected;
            GUI.enabled = !_lobbyOperationInProgress && canJoin;
            GUI.backgroundColor = canJoin ? new Color(0.2f, 0.7f, 0.3f) : new Color(0.3f, 0.3f, 0.3f);
            string btnText = canJoin ? "\u27A1" : (lobby.IsPasswordProtected ? "\u26D4" : "\u2717"); // Arrow, lock, or X
            if (GUILayout.Button(btnText, _smallButtonStyle, GUILayout.Width(28), GUILayout.Height(28)))
            {
                if (canJoin) JoinLobbyById(lobby.LobbyId);
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }

        private void DrawChatSection()
        {
            _showChat = DrawFoldout(_showChat, "LOBBY CHAT");
            if (!_showChat) return;

            GUILayout.BeginVertical(_boxStyle);

            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null || !lobbyManager.IsInLobby)
            {
                GUILayout.Label("Join a lobby to chat", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            if (_chatManager == null)
            {
                GUILayout.Label("Chat manager not found", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            // Chat log display
            _chatLogBuilder.Clear();
            var messages = _chatManager.Messages;
            int startIdx = Mathf.Max(0, messages.Count - 20); // Show last 20 messages
            for (int i = startIdx; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.IsSystem)
                    _chatLogBuilder.AppendLine($"<color=#888888>* {msg.Message}</color>");
                else
                    _chatLogBuilder.AppendLine($"<color=#666666>[{msg.LocalTime:HH:mm}]</color> <color=#66ccff>{msg.SenderName}</color>: {msg.Message}");
            }

            _chatScrollPosition = GUILayout.BeginScrollView(_chatScrollPosition, GUILayout.Height(120));
            GUILayout.Label(_chatLogBuilder.ToString(), new GUIStyle(_labelStyle) { richText = true, wordWrap = true });
            GUILayout.EndScrollView();

            // Chat input
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("ChatInput");
            _chatInput = GUILayout.TextField(_chatInput, new GUIStyle(_textFieldStyle) { fontSize = 11, alignment = TextAnchor.MiddleLeft });
            if (GUILayout.Button("Send", _smallButtonStyle, GUILayout.Width(45)) ||
                (Event.current.isKey && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "ChatInput"))
            {
                if (!string.IsNullOrWhiteSpace(_chatInput))
                {
                    _chatManager.SendChatMessage(_chatInput);
                    _chatInput = "";
                    _chatScrollPosition.y = float.MaxValue;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Chat persists through host migration!", _miniLabelStyle);

            GUILayout.EndVertical();
        }

        private void DrawRecentPlayersSection()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            int count = registry.CachedPlayerCount;
            _showRecentPlayers = DrawFoldout(_showRecentPlayers, $"RECENTLY PLAYED ({count})");
            if (!_showRecentPlayers) return;

            GUILayout.BeginVertical(_boxStyle);

            if (count == 0)
            {
                GUILayout.Label("No players discovered yet.", _miniLabelStyle);
                GUILayout.Label("Join lobbies to build your player list!", _miniLabelStyle);
            }
            else
            {
                // Show recent players (last 7 days)
                var recent = registry.GetRecentPlayers(7);

                if (recent.Count == 0)
                {
                    GUILayout.Label("No recent players (last 7 days).", _miniLabelStyle);
                }
                else
                {
                    GUILayout.Label($"Last 7 days ({recent.Count} players):", _miniLabelStyle);

                    // Show up to 10 most recent
                    int shown = 0;
                    foreach (var (puid, name, lastSeen) in recent)
                    {
                        if (shown >= 10) break;

                        // Skip blocked players
                        if (registry.IsBlocked(puid)) continue;

                        GUILayout.BeginHorizontal(_statsBoxStyle);

                        // Platform icon
                        string platform = registry.GetPlatform(puid);
                        if (!string.IsNullOrEmpty(platform))
                        {
                            GUILayout.Label(EOSPlayerRegistry.GetPlatformIcon(platform), _miniLabelStyle, GUILayout.Width(18));
                        }

                        // Name
                        GUILayout.Label(name, _valueStyle, GUILayout.Width(platform != null ? 75 : 90));

                        // Last seen
                        string timeAgo = GetTimeAgo(lastSeen);
                        GUILayout.Label(timeAgo, _miniLabelStyle, GUILayout.Width(45));

                        GUILayout.FlexibleSpace();

                        // Friend toggle button
                        bool isFriend = registry.IsFriend(puid);
                        string starIcon = isFriend ? "\u2605" : "\u2606"; // Filled vs empty star
                        GUIStyle starStyle = isFriend ? _yellowStyle : _labelStyle;
                        if (GUILayout.Button(starIcon, _smallButtonStyle, GUILayout.Width(22)))
                        {
                            registry.ToggleFriend(puid);
                        }

                        // Block button
                        GUI.backgroundColor = new Color(0.6f, 0.3f, 0.3f);
                        if (GUILayout.Button("\u26D4", _smallButtonStyle, GUILayout.Width(22)))
                        {
                            registry.BlockPlayer(puid);
                        }
                        GUI.backgroundColor = Color.white;

                        // Invite button (only show if in lobby and invites ready)
                        if (_invitesManager != null && _invitesManager.IsReady && !string.IsNullOrEmpty(_invitesManager.CurrentPayload))
                        {
                            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
                            if (GUILayout.Button("Inv", _smallButtonStyle, GUILayout.Width(28)))
                            {
                                SendInviteToPuid(puid, name);
                            }
                            GUI.backgroundColor = Color.white;
                        }

                        GUILayout.EndHorizontal();

                        shown++;
                    }

                    if (recent.Count > 10)
                    {
                        GUILayout.Label($"  ... and {recent.Count - 10} more", _miniLabelStyle);
                    }
                }

                // Footer with total count and clear button
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Total cached: {count}", _miniLabelStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", _smallButtonStyle, GUILayout.Width(45)))
                {
                    registry.ClearCache();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private string GetTimeAgo(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MM/dd");
        }

        private void DrawInvitesSection()
        {
            if (_invitesManager == null)
            {
                _invitesManager = EOSCustomInvites.Instance;
                if (_invitesManager == null) return;
            }

            int pendingCount = _invitesManager.PendingInvites.Count + _invitesManager.PendingRequests.Count;
            string title = pendingCount > 0 ? $"INVITES ({pendingCount} pending)" : "INVITES";

            _showInvites = DrawFoldout(_showInvites, title);
            if (!_showInvites) return;

            GUILayout.BeginVertical(_boxStyle);

            if (!_invitesManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            var lobbyManager = EOSLobbyManager.Instance;
            bool isInLobby = lobbyManager != null && lobbyManager.IsInLobby;

            // Current payload status
            GUILayout.BeginHorizontal();
            GUILayout.Label("Payload:", _labelStyle, GUILayout.Width(50));
            string payload = _invitesManager.CurrentPayload;
            if (string.IsNullOrEmpty(payload))
            {
                GUILayout.Label("(not set)", _miniLabelStyle);
            }
            else
            {
                GUILayout.Label(payload, _cyanStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Set lobby code as payload button
            if (isInLobby)
            {
                if (GUILayout.Button("Set Lobby Code as Payload", _smallButtonStyle))
                {
                    _invitesManager.SetLobbyPayload();
                    _inviteStatus = "Payload set to lobby code";
                }
            }
            else
            {
                GUILayout.Label("Join/host a lobby to set invite payload", _miniLabelStyle);
            }

            GUILayout.Space(6);

            // Send invite section
            GUILayout.Label("Send Invite", _sectionHeaderStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("To:", _labelStyle, GUILayout.Width(25));
            _inviteRecipientPuid = GUILayout.TextField(_inviteRecipientPuid, new GUIStyle(_textFieldStyle) { fontSize = 9, alignment = TextAnchor.MiddleLeft });
            GUILayout.EndHorizontal();

            GUI.enabled = !string.IsNullOrWhiteSpace(_inviteRecipientPuid) && !string.IsNullOrEmpty(payload);
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
            if (GUILayout.Button("SEND INVITE", _smallButtonStyle))
            {
                SendInviteToRecipient();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (string.IsNullOrEmpty(payload))
            {
                GUILayout.Label("Set payload first (lobby code)", _miniLabelStyle);
            }

            // Quick Send to Friends section
            var registry = EOSPlayerRegistry.Instance;
            if (registry != null && registry.FriendCount > 0 && !string.IsNullOrEmpty(payload))
            {
                GUILayout.Space(6);
                GUILayout.Label("Quick Send to Friends:", _miniLabelStyle);
                GUILayout.BeginHorizontal();
                int shown = 0;
                foreach (var (puid, name) in registry.GetFriends())
                {
                    if (shown >= 4) break; // Limit to 4 buttons per row

                    // Truncate long names
                    string buttonText = name.Length > 12 ? name.Substring(0, 10) + ".." : name;
                    GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
                    if (GUILayout.Button(buttonText, _smallButtonStyle, GUILayout.Width(75)))
                    {
                        SendInviteToPuid(puid, name);
                    }
                    GUI.backgroundColor = Color.white;
                    shown++;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // Pending received invites
            if (_invitesManager.PendingInvites.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label($"Received Invites ({_invitesManager.PendingInvites.Count})", _sectionHeaderStyle);

                foreach (var kvp in _invitesManager.PendingInvites)
                {
                    var invite = kvp.Value;
                    GUILayout.BeginVertical(_statsBoxStyle);

                    // Sender info
                    string shortSender = invite.SenderId?.ToString();
                    if (shortSender?.Length > 16) shortSender = shortSender.Substring(0, 8) + "...";
                    GUILayout.Label($"From: {shortSender}", _labelStyle);

                    // Payload (lobby code)
                    if (!string.IsNullOrEmpty(invite.Payload))
                    {
                        GUILayout.Label($"Code: {invite.Payload}", _cyanStyle);
                    }

                    // Time
                    GUILayout.Label(GetTimeAgo(invite.ReceivedTime), _miniLabelStyle);

                    // Accept/Reject buttons
                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    if (GUILayout.Button("Accept", _smallButtonStyle))
                    {
                        AcceptInviteAndJoin(kvp.Key, invite);
                    }
                    GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                    if (GUILayout.Button("Reject", _smallButtonStyle))
                    {
                        _invitesManager.RejectInvite(kvp.Key);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }
            }

            // Pending join requests (someone wants to join your game)
            if (_invitesManager.PendingRequests.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label($"Join Requests ({_invitesManager.PendingRequests.Count})", _sectionHeaderStyle);

                foreach (var kvp in _invitesManager.PendingRequests)
                {
                    var request = kvp.Value;
                    GUILayout.BeginVertical(_statsBoxStyle);

                    // Requester info
                    string shortFrom = request.FromUserId?.ToString();
                    if (shortFrom?.Length > 16) shortFrom = shortFrom.Substring(0, 8) + "...";
                    GUILayout.Label($"From: {shortFrom}", _labelStyle);
                    GUILayout.Label(GetTimeAgo(request.ReceivedTime), _miniLabelStyle);

                    // Accept/Reject buttons
                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    if (GUILayout.Button("Accept", _smallButtonStyle))
                    {
                        _ = _invitesManager.AcceptRequestToJoinAsync(request.FromUserId);
                    }
                    GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                    if (GUILayout.Button("Reject", _smallButtonStyle))
                    {
                        _ = _invitesManager.RejectRequestToJoinAsync(request.FromUserId);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }
            }

            // Status message
            if (!string.IsNullOrEmpty(_inviteStatus))
            {
                GUILayout.Space(4);
                GUILayout.Label(_inviteStatus, _yellowStyle);
            }

            GUILayout.EndVertical();
        }

        private async void SendInviteToRecipient()
        {
            _inviteStatus = "Sending...";
            var result = await _invitesManager.SendInviteAsync(_inviteRecipientPuid.Trim());
            if (result == Result.Success)
            {
                _inviteStatus = "Invite sent!";
                _inviteRecipientPuid = "";
            }
            else
            {
                _inviteStatus = $"Failed: {result}";
            }
        }

        private async void SendInviteToPuid(string puid, string displayName)
        {
            if (_invitesManager == null || !_invitesManager.IsReady) return;

            _inviteStatus = $"Sending to {displayName}...";
            var result = await _invitesManager.SendInviteAsync(puid);
            if (result == Result.Success)
            {
                _inviteStatus = $"Sent to {displayName}!";
            }
            else
            {
                _inviteStatus = $"Failed: {result}";
            }
        }

        private async System.Threading.Tasks.Task JoinFriendLobby(string lobbyCode, string friendName = null)
        {
            if (_transport == null) return;

            // Check if we're already in a lobby
            var lobbyManager = Lobbies.EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                // Leave current lobby first
                EOSToastManager.Info("Leaving current lobby...");
                await _transport.LeaveLobbyAsync();
            }

            EOSToastManager.Info("Joining", friendName != null ? $"Joining {friendName}'s game..." : $"Joining lobby {lobbyCode}...");

            var (result, lobby) = await _transport.JoinLobbyAsync(lobbyCode);
            if (result == Result.Success)
            {
                EOSToastManager.Success("Joined!", friendName != null ? $"Joined {friendName}'s lobby" : $"Joined lobby {lobbyCode}");
            }
            else
            {
                EOSToastManager.Error("Join Failed", $"Could not join: {result}");
                Debug.LogWarning($"[EOSNativeUI] Failed to join friend's lobby: {result}");
            }
        }

        private async void AcceptInviteAndJoin(string inviteId, InviteData invite)
        {
            _invitesManager.AcceptInvite(inviteId);

            // If payload looks like a lobby code, try to join
            if (invite.TryGetLobbyCode(out string lobbyCode))
            {
                _inviteStatus = $"Joining {lobbyCode}...";
                var (result, lobby) = await _transport.JoinLobbyAsync(lobbyCode);
                if (result == Result.Success)
                {
                    _inviteStatus = $"Joined: {lobby.JoinCode}";
                }
                else
                {
                    _inviteStatus = $"Join failed: {result}";
                }
            }
            else
            {
                _inviteStatus = "Invite accepted (no lobby code)";
            }
        }

        private void DrawFriendsSection()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            int friendCount = registry.FriendCount;
            string title = $"LOCAL FRIENDS ({friendCount})";

            _showFriends = DrawFoldout(_showFriends, title);
            if (!_showFriends) return;

            GUILayout.BeginVertical(_boxStyle);

            if (friendCount == 0)
            {
                GUILayout.Label("No local friends yet.", _miniLabelStyle);
                GUILayout.Label("Mark players as friends from Recently Played!", _miniLabelStyle);
            }
            else
            {
                // Local friends list
                var friends = registry.GetFriends();
                float listHeight = Mathf.Clamp(friends.Count * 28, 60, 150);
                _friendsScrollPosition = GUILayout.BeginScrollView(_friendsScrollPosition, GUILayout.Height(listHeight));

                foreach (var (puid, name) in friends)
                {
                    DrawLocalFriendRow(puid, name);
                }

                GUILayout.EndScrollView();
            }

            // Footer with refresh, cloud sync and clear buttons
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();

            // Refresh status button
            GUI.backgroundColor = new Color(0.4f, 0.7f, 0.4f);
            if (GUILayout.Button("\u21BB", _smallButtonStyle, GUILayout.Width(25))) // Refresh icon
            {
                _ = registry.RefreshAllFriendStatusesAsync();
            }
            GUI.backgroundColor = Color.white;

            // Cloud sync button
            var storage = Storage.EOSPlayerDataStorage.Instance;
            bool canSync = storage != null && storage.IsReady && !registry.IsCloudSyncInProgress;

            GUI.enabled = canSync;
            GUI.backgroundColor = new Color(0.3f, 0.6f, 0.8f);
            string syncLabel = registry.IsCloudSyncInProgress ? "..." : "\u2601"; // Cloud icon
            if (GUILayout.Button(syncLabel, _smallButtonStyle, GUILayout.Width(25)))
            {
                _ = registry.FullCloudSyncAsync();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            // Show last sync time
            if (registry.LastCloudSync != DateTime.MinValue)
            {
                string syncTime = GetTimeAgo(registry.LastCloudSync);
                GUILayout.Label(syncTime, _miniLabelStyle);
            }

            GUILayout.FlexibleSpace();

            // Clear all button
            GUI.backgroundColor = new Color(0.6f, 0.3f, 0.3f);
            if (GUILayout.Button("Clear", _smallButtonStyle, GUILayout.Width(45)))
            {
                registry.ClearFriends();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawLocalFriendRow(string puid, string displayName)
        {
            var registry = EOSPlayerRegistry.Instance;
            GUILayout.BeginHorizontal(_statsBoxStyle);

            // Status indicator
            var (status, lobbyCode) = registry.GetFriendStatusWithLobby(puid);
            string statusIcon;
            GUIStyle statusStyle;
            switch (status)
            {
                case FriendStatus.InLobby:
                    statusIcon = "\u25CF"; // Filled circle
                    statusStyle = _greenStyle;
                    break;
                case FriendStatus.InGame:
                    statusIcon = "\u25CF";
                    statusStyle = _cyanStyle;
                    break;
                case FriendStatus.Offline:
                    statusIcon = "\u25CB"; // Empty circle
                    statusStyle = _labelStyle;
                    break;
                default:
                    statusIcon = "\u2022"; // Bullet
                    statusStyle = _labelStyle;
                    break;
            }
            GUILayout.Label(statusIcon, statusStyle, GUILayout.Width(14));

            // Platform icon
            string platform = registry.GetPlatform(puid);
            if (!string.IsNullOrEmpty(platform))
            {
                GUILayout.Label(EOSPlayerRegistry.GetPlatformIcon(platform), _miniLabelStyle, GUILayout.Width(18));
            }

            // Display name
            GUILayout.Label(displayName, _valueStyle, GUILayout.Width(platform != null ? 75 : 90));

            // Status text or note
            string note = registry.GetNote(puid);
            bool hasNote = !string.IsNullOrEmpty(note);
            bool isEditingThis = _editingNotePuid == puid;

            if (isEditingThis)
            {
                // Show note text field
                _editingNoteText = GUILayout.TextField(_editingNoteText, 200, GUILayout.Width(80));
                if (GUILayout.Button("\u2714", _smallButtonStyle, GUILayout.Width(20))) // Checkmark
                {
                    registry.SetNote(puid, _editingNoteText);
                    _editingNotePuid = null;
                    _editingNoteText = "";
                }
                if (GUILayout.Button("\u2718", _smallButtonStyle, GUILayout.Width(20))) // X
                {
                    _editingNotePuid = null;
                    _editingNoteText = "";
                }
            }
            else
            {
                // Show status or note preview
                string displayText;
                if (hasNote && note.Length > 0)
                {
                    displayText = note.Length > 8 ? note.Substring(0, 6) + ".." : note;
                }
                else
                {
                    displayText = status switch
                    {
                        FriendStatus.InLobby => "Here",
                        FriendStatus.InGame => lobbyCode ?? "Game",
                        FriendStatus.Offline => "--",
                        _ => "?"
                    };
                }
                GUIStyle textStyle = hasNote ? _cyanStyle : (status == FriendStatus.InLobby ? _greenStyle : _miniLabelStyle);
                GUILayout.Label(displayText, textStyle, GUILayout.Width(45));

                // Note edit button
                string noteIcon = hasNote ? "\u270E" : "\u270F"; // Pencil icons
                GUIStyle noteStyle = hasNote ? _yellowStyle : _labelStyle;
                if (GUILayout.Button(noteIcon, _smallButtonStyle, GUILayout.Width(20)))
                {
                    _editingNotePuid = puid;
                    _editingNoteText = note ?? "";
                }
            }

            GUILayout.FlexibleSpace();

            // Join button (if in game but not in our lobby)
            if (!isEditingThis && status == FriendStatus.InGame && !string.IsNullOrEmpty(lobbyCode))
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("Join", _smallButtonStyle, GUILayout.Width(35)))
                {
                    _ = JoinFriendLobby(lobbyCode, displayName);
                }
                GUI.backgroundColor = Color.white;
            }
            // Invite button (only if in lobby with payload set and friend not already here)
            else if (!isEditingThis && status != FriendStatus.InLobby && _invitesManager != null && _invitesManager.IsReady && !string.IsNullOrEmpty(_invitesManager.CurrentPayload))
            {
                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.9f);
                if (GUILayout.Button("Inv", _smallButtonStyle, GUILayout.Width(35)))
                {
                    SendInviteToPuid(puid, displayName);
                }
                GUI.backgroundColor = Color.white;
            }

            // Remove friend button (hide when editing)
            if (!isEditingThis)
            {
                GUI.backgroundColor = new Color(0.7f, 0.4f, 0.4f);
                if (GUILayout.Button("\u2718", _smallButtonStyle, GUILayout.Width(22)))
                {
                    registry?.RemoveFriend(puid);
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawBlockedSection()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            int blockedCount = registry.BlockedCount;
            if (blockedCount == 0) return; // Don't show section if no blocked players

            _showBlocked = DrawFoldout(_showBlocked, $"BLOCKED ({blockedCount})");
            if (!_showBlocked) return;

            GUILayout.BeginVertical(_boxStyle);

            var blocked = registry.GetBlockedPlayers();
            float listHeight = Mathf.Clamp(blocked.Count * 24, 50, 100);
            _blockedScrollPosition = GUILayout.BeginScrollView(_blockedScrollPosition, GUILayout.Height(listHeight));

            foreach (var (puid, name) in blocked)
            {
                GUILayout.BeginHorizontal(_statsBoxStyle);

                // Block icon
                GUILayout.Label("\u26D4", _redStyle, GUILayout.Width(18)); // No entry symbol

                // Display name
                GUILayout.Label(name, _valueStyle, GUILayout.Width(130));

                GUILayout.FlexibleSpace();

                // Unblock button
                GUI.backgroundColor = new Color(0.4f, 0.6f, 0.4f);
                if (GUILayout.Button("Unblock", _smallButtonStyle, GUILayout.Width(55)))
                {
                    registry.UnblockPlayer(puid);
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            // Clear all button
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.backgroundColor = new Color(0.5f, 0.3f, 0.3f);
            if (GUILayout.Button("Clear All", _smallButtonStyle, GUILayout.Width(60)))
            {
                registry.ClearBlocked();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        // Keep Epic Account friends as a separate collapsed section for users with Epic login
        private void DrawEpicFriendsSection()
        {
            if (_friendsManager == null)
            {
                _friendsManager = EOSFriends.Instance;
                if (_friendsManager == null) return;
            }

            // Check for Epic Account login (required for Epic Friends)
            var eosManager = EOSManager.Instance;
            bool hasEpicAccount = eosManager != null && eosManager.IsEpicAccountLoggedIn;
            if (!hasEpicAccount) return; // Don't show this section if no Epic account

            string title = _friendsManager.IsReady
                ? $"EPIC FRIENDS ({_friendsManager.FriendCount})"
                : "EPIC FRIENDS";

            _showEpicFriends = DrawFoldout(_showEpicFriends, title);
            if (!_showEpicFriends) return;

            GUILayout.BeginVertical(_boxStyle);

            if (!_friendsManager.IsReady)
            {
                GUILayout.Label("Initializing...", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Refresh button
            if (GUILayout.Button("Refresh", _smallButtonStyle))
            {
                _ = _friendsManager.QueryFriendsAsync();
            }

            GUILayout.Space(4);

            if (_friendsManager.FriendCount == 0)
            {
                GUILayout.Label("No Epic friends found.", _miniLabelStyle);
            }
            else
            {
                float listHeight = Mathf.Clamp(_friendsManager.FriendCount * 28, 60, 120);
                _epicFriendsScrollPosition = GUILayout.BeginScrollView(_epicFriendsScrollPosition, GUILayout.Height(listHeight));

                foreach (var friend in _friendsManager.Friends)
                {
                    DrawEpicFriendRow(friend);
                }

                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private void DrawEpicFriendRow(FriendData friend)
        {
            GUILayout.BeginHorizontal(_statsBoxStyle);

            // Status icon
            string statusIcon = friend.Status switch
            {
                Epic.OnlineServices.Friends.FriendsStatus.Friends => "\u2714", // Checkmark
                Epic.OnlineServices.Friends.FriendsStatus.InviteSent => "\u27A1", // Arrow
                Epic.OnlineServices.Friends.FriendsStatus.InviteReceived => "\u2709", // Envelope
                _ => "\u2022" // Bullet
            };
            GUIStyle statusStyle = friend.Status switch
            {
                Epic.OnlineServices.Friends.FriendsStatus.Friends => _greenStyle,
                Epic.OnlineServices.Friends.FriendsStatus.InviteSent => _yellowStyle,
                Epic.OnlineServices.Friends.FriendsStatus.InviteReceived => _cyanStyle,
                _ => _labelStyle
            };
            GUILayout.Label(statusIcon, statusStyle, GUILayout.Width(18));

            // Account ID (shortened)
            string accountId = friend.AccountId?.ToString() ?? "(unknown)";
            string shortId = accountId.Length > 16 ? accountId.Substring(0, 8) + "..." : accountId;

            // Display name if available, otherwise show shortened ID
            string displayText = !string.IsNullOrEmpty(friend.DisplayName) ? friend.DisplayName : shortId;
            GUILayout.Label(displayText, _valueStyle);

            GUILayout.FlexibleSpace();

            // Action buttons based on status
            if (friend.Status == Epic.OnlineServices.Friends.FriendsStatus.InviteReceived)
            {
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                if (GUILayout.Button("\u2714", _smallButtonStyle, GUILayout.Width(25)))
                {
                    _ = _friendsManager.AcceptInviteAsync(friend.AccountId);
                }
                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("\u2718", _smallButtonStyle, GUILayout.Width(25)))
                {
                    _ = _friendsManager.RejectInviteAsync(friend.AccountId);
                }
                GUI.backgroundColor = Color.white;
            }
            else if (friend.Status == Epic.OnlineServices.Friends.FriendsStatus.Friends)
            {
                // Could add "Invite to lobby" button here
                GUILayout.Label("Friend", _miniLabelStyle, GUILayout.Width(40));
            }

            GUILayout.EndHorizontal();
        }

        private void DrawStatsLeaderboardsSection()
        {
            if (_statsManager == null) _statsManager = EOSStats.Instance;
            if (_leaderboardsManager == null) _leaderboardsManager = EOSLeaderboards.Instance;

            int statsCount = _statsManager?.CachedStatsCount ?? 0;
            int leaderboardCount = _leaderboardsManager?.DefinitionCount ?? 0;
            string title = $"STATS & LEADERBOARDS ({statsCount} stats, {leaderboardCount} boards)";

            _showStats = DrawFoldout(_showStats, title);
            if (!_showStats) return;

            GUILayout.BeginVertical(_boxStyle);

            bool statsReady = _statsManager != null && _statsManager.IsReady;
            bool leaderboardsReady = _leaderboardsManager != null && _leaderboardsManager.IsReady;

            if (!statsReady && !leaderboardsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _labelStyle);
                GUILayout.Label("Stats require Developer Portal config.", _miniLabelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Stats Section
            if (statsReady)
            {
                GUILayout.Label("My Stats", _sectionHeaderStyle);

                if (GUILayout.Button("Query My Stats", _smallButtonStyle))
                {
                    _ = _statsManager.QueryMyStatsAsync();
                }

                if (_statsManager.CachedStatsCount > 0)
                {
                    _statsScrollPosition = GUILayout.BeginScrollView(_statsScrollPosition, GUILayout.Height(80));
                    foreach (var kvp in _statsManager.CachedStats)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(kvp.Key, _labelStyle, GUILayout.Width(100));
                        GUILayout.Label(kvp.Value.Value.ToString(), _valueStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                }
                else
                {
                    GUILayout.Label("No stats cached. Click Query.", _miniLabelStyle);
                }

                // Test stat ingest
                GUILayout.Space(4);
                GUILayout.Label("Ingest Test Stat", _miniLabelStyle);
                GUILayout.BeginHorizontal();
                _testStatName = GUILayout.TextField(_testStatName, GUILayout.Width(100));
                if (int.TryParse(GUILayout.TextField(_testStatAmount.ToString(), GUILayout.Width(40)), out int amt))
                    _testStatAmount = amt;
                if (GUILayout.Button("+", _smallButtonStyle, GUILayout.Width(25)))
                {
                    _ = _statsManager.IngestStatAsync(_testStatName, _testStatAmount);
                }
                GUILayout.EndHorizontal();
            }

            // Leaderboards Section
            if (leaderboardsReady)
            {
                GUILayout.Space(6);
                GUILayout.Label("Leaderboards", _sectionHeaderStyle);

                if (GUILayout.Button("Refresh Definitions", _smallButtonStyle))
                {
                    _ = _leaderboardsManager.QueryDefinitionsAsync();
                }

                if (_leaderboardsManager.DefinitionCount > 0)
                {
                    GUILayout.Label("Select leaderboard:", _miniLabelStyle);
                    foreach (var def in _leaderboardsManager.Definitions)
                    {
                        GUILayout.BeginHorizontal();
                        bool isSelected = _selectedLeaderboardId == def.LeaderboardId;
                        GUIStyle btnStyle = isSelected ? _greenStyle : _labelStyle;
                        if (GUILayout.Button(def.LeaderboardId, isSelected ? _buttonStyle : _smallButtonStyle))
                        {
                            _selectedLeaderboardId = def.LeaderboardId;
                            QuerySelectedLeaderboard();
                        }
                        GUILayout.Label($"({def.StatName})", _miniLabelStyle);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }

                    // Show rankings if we have them
                    if (_currentLeaderboardEntries.Count > 0)
                    {
                        GUILayout.Space(4);
                        GUILayout.Label($"Top {_currentLeaderboardEntries.Count}", _miniLabelStyle);
                        _leaderboardScrollPosition = GUILayout.BeginScrollView(_leaderboardScrollPosition, GUILayout.Height(100));
                        foreach (var entry in _currentLeaderboardEntries)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label($"#{entry.Rank}", _labelStyle, GUILayout.Width(30));
                            GUILayout.Label(entry.DisplayName ?? entry.ShortUserId, _valueStyle, GUILayout.Width(100));
                            GUILayout.Label(entry.Score.ToString(), _cyanStyle);
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndScrollView();
                    }
                }
                else
                {
                    GUILayout.Label("No leaderboards configured in Portal.", _miniLabelStyle);
                }
            }

            GUILayout.EndVertical();
        }

        private async void QuerySelectedLeaderboard()
        {
            if (string.IsNullOrEmpty(_selectedLeaderboardId) || _leaderboardsManager == null) return;
            var (result, entries) = await _leaderboardsManager.QueryRanksAsync(_selectedLeaderboardId, 10);
            if (result == Result.Success && entries != null)
            {
                _currentLeaderboardEntries = entries;
            }
        }

        private void DrawRankedSection()
        {
            if (_rankedManager == null) _rankedManager = Social.EOSRankedMatchmaking.Instance;

            string title = "RANKED";
            if (_rankedManager != null && _rankedManager.IsDataLoaded)
            {
                var data = _rankedManager.PlayerData;
                string rankDisplay = _rankedManager.GetCurrentRankDisplayName();
                title = $"RANKED ({rankDisplay})";
            }

            _showRanked = DrawFoldout(_showRanked, title);
            if (!_showRanked) return;

            GUILayout.BeginVertical(_boxStyle);

            if (_rankedManager == null || !_rankedManager.IsDataLoaded)
            {
                GUILayout.Label("Loading ranked data...", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            var playerData = _rankedManager.PlayerData;

            // Rating display
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rating:", _labelStyle, GUILayout.Width(55));
            GUILayout.Label(playerData.Rating.ToString(), _cyanStyle);
            GUILayout.Label($" (Peak: {playerData.PeakRating})", _miniLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Rank tier display
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rank:", _labelStyle, GUILayout.Width(55));
            string rankName = _rankedManager.GetCurrentRankDisplayName();
            GUIStyle rankStyle = GetRankStyle(_rankedManager.CurrentTier);
            GUILayout.Label(rankName, rankStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Win/Loss record
            GUILayout.BeginHorizontal();
            GUILayout.Label("Record:", _labelStyle, GUILayout.Width(55));
            GUILayout.Label($"{playerData.Wins}W", _greenStyle);
            GUILayout.Label(" - ", _labelStyle);
            GUILayout.Label($"{playerData.Losses}L", _redStyle);
            if (playerData.GamesPlayed > 0)
            {
                float winRate = playerData.WinRate;
                GUILayout.Label($" ({winRate:F0}%)", winRate >= 50 ? _greenStyle : _redStyle);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Streaks
            if (playerData.WinStreak >= 2 || playerData.LossStreak >= 2)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Streak:", _labelStyle, GUILayout.Width(55));
                if (playerData.WinStreak >= 2)
                    GUILayout.Label($"{playerData.WinStreak} wins", _greenStyle);
                else if (playerData.LossStreak >= 2)
                    GUILayout.Label($"{playerData.LossStreak} losses", _redStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);

            // Matchmaking controls
            var lobbyManager = EOSLobbyManager.Instance;
            bool isInLobby = lobbyManager != null && lobbyManager.IsInLobby;
            bool isInQueue = _rankedManager.IsInQueue;

            if (!isInLobby && !isInQueue)
            {
                // Game mode input
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mode:", _labelStyle, GUILayout.Width(40));
                _rankedGameMode = GUILayout.TextField(_rankedGameMode, GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Find/Host buttons
                GUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                if (GUILayout.Button("Find Match", _buttonStyle))
                {
                    _ = FindRankedMatchAsync();
                }
                GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);
                if (GUILayout.Button("Host Ranked", _buttonStyle))
                {
                    _ = HostRankedLobbyAsync();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                // Quick match button
                GUI.backgroundColor = new Color(0.6f, 0.3f, 0.8f);
                if (GUILayout.Button("Find or Host", _buttonStyle))
                {
                    _ = FindOrHostRankedMatchAsync();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (isInQueue)
            {
                // Queue status
                GUILayout.BeginHorizontal();
                GUILayout.Label("Queue:", _labelStyle, GUILayout.Width(50));
                GUILayout.Label($"Searching... ({_rankedManager.QueueTime:F0}s)", _yellowStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("Leave Queue", _buttonStyle))
                {
                    _rankedManager.LeaveQueue();
                    _rankedStatus = "Left queue";
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.Label("In lobby - leave to find new match", _miniLabelStyle);
            }

            // Status message
            if (!string.IsNullOrEmpty(_rankedStatus))
            {
                GUILayout.Label(_rankedStatus, _miniLabelStyle);
            }

            GUILayout.Space(4);

            // Algorithm info
            GUILayout.BeginHorizontal();
            GUILayout.Label("Algorithm:", _miniLabelStyle, GUILayout.Width(60));
            GUILayout.Label(_rankedManager.Algorithm.ToString(), _miniLabelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private GUIStyle GetRankStyle(Social.RankTier tier)
        {
            return tier switch
            {
                Social.RankTier.Grandmaster => _orangeStyle,
                Social.RankTier.Master => _orangeStyle,
                Social.RankTier.Champion => _orangeStyle,
                Social.RankTier.Diamond => _cyanStyle,
                Social.RankTier.Platinum => _cyanStyle,
                Social.RankTier.Gold => _yellowStyle,
                Social.RankTier.Silver => _valueStyle,
                Social.RankTier.Bronze => _valueStyle,
                Social.RankTier.Iron => _labelStyle,
                _ => _labelStyle
            };
        }

        private async void FindRankedMatchAsync()
        {
            _rankedStatus = "Searching...";
            var (result, lobby) = await _rankedManager.FindRankedMatchAsync(_rankedGameMode);
            if (result == Result.Success && lobby.HasValue)
            {
                _rankedStatus = $"Joined: {lobby.Value.JoinCode}";
            }
            else
            {
                _rankedStatus = $"No match found ({result})";
            }
        }

        private async void HostRankedLobbyAsync()
        {
            _rankedStatus = "Hosting...";
            var (result, lobby) = await _rankedManager.HostRankedLobbyAsync(_rankedGameMode);
            if (result == Result.Success && lobby.HasValue)
            {
                _rankedStatus = $"Hosted: {lobby.Value.JoinCode}";
            }
            else
            {
                _rankedStatus = $"Failed to host ({result})";
            }
        }

        private async void FindOrHostRankedMatchAsync()
        {
            _rankedStatus = "Finding or hosting...";
            var (result, lobby, didHost) = await _rankedManager.FindOrHostRankedMatchAsync(_rankedGameMode);
            if (result == Result.Success && lobby.HasValue)
            {
                _rankedStatus = didHost ? $"Hosted: {lobby.Value.JoinCode}" : $"Joined: {lobby.Value.JoinCode}";
            }
            else
            {
                _rankedStatus = $"Failed ({result})";
            }
        }

        private void DrawLFGSection()
        {
            if (_lfgManager == null) _lfgManager = Social.EOSLFGManager.Instance;

            string title = "LFG (LOOKING FOR GROUP)";
            if (_lfgManager != null && _lfgManager.HasActivePost)
            {
                title = $"LFG (Active: {_lfgManager.ActivePost.CurrentSize}/{_lfgManager.ActivePost.DesiredSize})";
            }

            _showLFG = DrawFoldout(_showLFG, title);
            if (!_showLFG) return;

            GUILayout.BeginVertical(_boxStyle);

            if (_lfgManager == null)
            {
                GUILayout.Label("LFG Manager not available", _labelStyle);
                if (GUILayout.Button("Add LFG Manager", _smallButtonStyle))
                {
                    var go = EOSManager.Instance?.gameObject;
                    if (go != null)
                        go.AddComponent<Social.EOSLFGManager>();
                }
                GUILayout.EndVertical();
                return;
            }

            // Show active post or create new
            if (_lfgManager.HasActivePost)
            {
                DrawActiveLFGPost();
            }
            else
            {
                DrawCreateLFGPost();
            }

            GUILayout.Space(6);

            // Search section
            DrawLFGSearch();

            // Status
            if (!string.IsNullOrEmpty(_lfgStatus))
            {
                GUILayout.Label(_lfgStatus, _miniLabelStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawActiveLFGPost()
        {
            var post = _lfgManager.ActivePost;

            GUILayout.Label("YOUR ACTIVE POST", _miniLabelStyle);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Title:", _labelStyle, GUILayout.Width(45));
            GUILayout.Label(post.Title, _greenStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", _labelStyle, GUILayout.Width(45));
            GUILayout.Label($"{post.CurrentSize}/{post.DesiredSize}", _cyanStyle);
            GUILayout.Label($"  Status: {post.Status}", _miniLabelStyle);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(post.GameMode))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Mode:", _labelStyle, GUILayout.Width(45));
                GUILayout.Label(post.GameMode, _valueStyle);
                GUILayout.EndHorizontal();
            }

            // Time remaining
            var timeLeft = post.TimeRemaining;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Expires:", _labelStyle, GUILayout.Width(45));
            GUILayout.Label($"{timeLeft.Minutes}m {timeLeft.Seconds}s", timeLeft.TotalMinutes < 5 ? _orangeStyle : _valueStyle);
            GUILayout.EndHorizontal();

            // Pending requests
            if (_lfgManager.PendingRequests.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label($"Pending Requests: {_lfgManager.PendingRequests.Count}", _yellowStyle);
                foreach (var request in _lfgManager.PendingRequests)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(request.RequesterName, _labelStyle, GUILayout.Width(100));
                    GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
                    if (GUILayout.Button("Accept", _smallButtonStyle, GUILayout.Width(55)))
                    {
                        _ = _lfgManager.AcceptJoinRequestAsync(request);
                    }
                    GUI.backgroundColor = new Color(0.7f, 0.3f, 0.3f);
                    if (GUILayout.Button("Reject", _smallButtonStyle, GUILayout.Width(55)))
                    {
                        _ = _lfgManager.RejectJoinRequestAsync(request);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(4);

            // Close button
            GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
            if (GUILayout.Button("Close Post", _buttonStyle))
            {
                _ = CloseLFGPostAsync();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawCreateLFGPost()
        {
            GUILayout.Label("CREATE POST", _miniLabelStyle);
            GUILayout.Space(2);

            // Title
            GUILayout.BeginHorizontal();
            GUILayout.Label("Title:", _labelStyle, GUILayout.Width(45));
            _lfgTitle = GUILayout.TextField(_lfgTitle, GUILayout.Width(200));
            GUILayout.EndHorizontal();

            // Game mode
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode:", _labelStyle, GUILayout.Width(45));
            _lfgGameMode = GUILayout.TextField(_lfgGameMode, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            // Size
            GUILayout.BeginHorizontal();
            GUILayout.Label("Size:", _labelStyle, GUILayout.Width(45));
            if (GUILayout.Button("-", _smallButtonStyle, GUILayout.Width(25)))
                _lfgDesiredSize = Mathf.Max(2, _lfgDesiredSize - 1);
            GUILayout.Label(_lfgDesiredSize.ToString(), _cyanStyle, GUILayout.Width(25));
            if (GUILayout.Button("+", _smallButtonStyle, GUILayout.Width(25)))
                _lfgDesiredSize = Mathf.Min(64, _lfgDesiredSize + 1);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Create button
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("Create LFG Post", _buttonStyle))
            {
                _ = CreateLFGPostAsync();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawLFGSearch()
        {
            GUILayout.Space(4);
            GUILayout.Label("BROWSE POSTS", _miniLabelStyle);

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);
            if (GUILayout.Button("Search", _smallButtonStyle, GUILayout.Width(60)))
            {
                _ = SearchLFGPostsAsync();
            }
            if (GUILayout.Button("Refresh", _smallButtonStyle, GUILayout.Width(60)))
            {
                _ = _lfgManager.RefreshSearchAsync();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Label($"{_lfgManager.SearchResults.Count} posts", _miniLabelStyle);
            GUILayout.EndHorizontal();

            // Results list
            if (_lfgManager.SearchResults.Count > 0)
            {
                _lfgSearchScrollPosition = GUILayout.BeginScrollView(_lfgSearchScrollPosition, GUILayout.Height(120));

                foreach (var post in _lfgManager.SearchResults)
                {
                    if (post.IsExpired) continue;

                    GUILayout.BeginVertical(_boxStyle);

                    // Title and owner
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(post.Title, _labelStyle, GUILayout.Width(150));
                    GUILayout.Label($"by {post.OwnerName}", _miniLabelStyle);
                    GUILayout.EndHorizontal();

                    // Info row
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{post.CurrentSize}/{post.DesiredSize}", _cyanStyle, GUILayout.Width(40));
                    if (!string.IsNullOrEmpty(post.GameMode))
                        GUILayout.Label(post.GameMode, _miniLabelStyle, GUILayout.Width(60));
                    if (post.VoiceRequired)
                        GUILayout.Label("[Voice]", _yellowStyle, GUILayout.Width(50));
                    GUILayout.FlexibleSpace();

                    // Join button
                    bool alreadySent = _lfgManager.SentRequests.Contains(post.PostId);
                    GUI.enabled = post.IsJoinable && !alreadySent;
                    GUI.backgroundColor = alreadySent ? Color.gray : new Color(0.3f, 0.7f, 0.3f);
                    if (GUILayout.Button(alreadySent ? "Sent" : "Join", _smallButtonStyle, GUILayout.Width(50)))
                    {
                        _ = _lfgManager.SendJoinRequestAsync(post.PostId);
                    }
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                }

                GUILayout.EndScrollView();
            }
        }

        private async void CreateLFGPostAsync()
        {
            _lfgStatus = "Creating post...";
            var options = new Social.LFGPostOptions()
                .WithTitle(_lfgTitle)
                .WithGameMode(_lfgGameMode)
                .WithDesiredSize(_lfgDesiredSize);

            var (result, post) = await _lfgManager.CreatePostAsync(options);
            if (result == Result.Success)
            {
                _lfgStatus = "Post created!";
            }
            else
            {
                _lfgStatus = $"Failed: {result}";
            }
        }

        private async void CloseLFGPostAsync()
        {
            _lfgStatus = "Closing post...";
            var result = await _lfgManager.ClosePostAsync();
            _lfgStatus = result == Result.Success ? "Post closed" : $"Failed: {result}";
        }

        private async void SearchLFGPostsAsync()
        {
            _lfgStatus = "Searching...";
            var options = new Social.LFGSearchOptions();
            if (!string.IsNullOrEmpty(_lfgGameMode))
                options.WithGameMode(_lfgGameMode);

            var (result, posts) = await _lfgManager.SearchPostsAsync(options);
            _lfgStatus = result == Result.Success ? $"Found {posts.Count} posts" : $"Search failed: {result}";
        }

        private void DrawAchievementsSection()
        {
            if (_achievementsManager == null) _achievementsManager = EOSAchievements.Instance;

            string title = _achievementsManager != null && _achievementsManager.IsReady
                ? $"ACHIEVEMENTS ({_achievementsManager.UnlockedCount}/{_achievementsManager.TotalAchievements})"
                : "ACHIEVEMENTS";

            _showAchievements = DrawFoldout(_showAchievements, title);
            if (!_showAchievements) return;

            GUILayout.BeginVertical(_boxStyle);

            if (_achievementsManager == null || !_achievementsManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _labelStyle);
                GUILayout.Label("Achievements require Developer Portal config.", _miniLabelStyle);
                GUILayout.EndVertical();
                return;
            }

            if (GUILayout.Button("Refresh Achievements", _smallButtonStyle))
            {
                _ = _achievementsManager.RefreshAsync();
            }

            if (_achievementsManager.TotalAchievements == 0)
            {
                GUILayout.Label("No achievements configured in Portal.", _miniLabelStyle);
                GUILayout.EndVertical();
                return;
            }

            _achievementsScrollPosition = GUILayout.BeginScrollView(_achievementsScrollPosition, GUILayout.Height(120));

            foreach (var def in _achievementsManager.Definitions)
            {
                var playerAch = _achievementsManager.GetPlayerAchievement(def.Id);
                bool unlocked = playerAch?.IsUnlocked ?? false;
                float progress = (float)(playerAch?.Progress ?? 0);

                GUILayout.BeginHorizontal(_statsBoxStyle);

                // Status icon
                string icon = unlocked ? "\u2714" : "\u25CB"; // Checkmark or circle
                GUIStyle iconStyle = unlocked ? _greenStyle : _labelStyle;
                GUILayout.Label(icon, iconStyle, GUILayout.Width(18));

                // Name
                GUILayout.Label(def.DisplayName ?? def.Id, unlocked ? _greenStyle : _valueStyle, GUILayout.Width(120));

                // Progress bar or unlock date
                if (unlocked)
                {
                    var unlockTime = playerAch?.UnlockDateTime;
                    GUILayout.Label(unlockTime?.ToString("MM/dd/yy") ?? "Unlocked", _miniLabelStyle);
                }
                else if (progress > 0)
                {
                    GUILayout.Label($"{progress * 100:F0}%", _yellowStyle);
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawStorageSection()
        {
            if (_storageManager == null) _storageManager = EOSPlayerDataStorage.Instance;

            int fileCount = _storageManager?.Files?.Count ?? 0;
            string title = $"CLOUD STORAGE ({fileCount} files)";

            _showStorage = DrawFoldout(_showStorage, title);
            if (!_showStorage) return;

            GUILayout.BeginVertical(_boxStyle);

            if (_storageManager == null || !_storageManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Usage info
            long used = _storageManager.GetTotalStorageUsed();
            GUILayout.Label($"Usage: {EOSPlayerDataStorage.FormatBytes(used)} / 400 MB", _labelStyle);

            if (GUILayout.Button("Refresh File List", _smallButtonStyle))
            {
                _ = _storageManager.QueryFileListAsync();
            }

            // File list
            if (_storageManager.Files.Count > 0)
            {
                _storageScrollPosition = GUILayout.BeginScrollView(_storageScrollPosition, GUILayout.Height(80));

                foreach (var file in _storageManager.Files)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(file.Filename, _valueStyle, GUILayout.Width(120));
                    GUILayout.Label(EOSPlayerDataStorage.FormatBytes((long)file.FileSizeBytes), _miniLabelStyle);
                    GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                    if (GUILayout.Button("\u2715", _smallButtonStyle, GUILayout.Width(22)))
                    {
                        _ = _storageManager.DeleteFileAsync(file.Filename);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("No cloud files.", _miniLabelStyle);
            }

            // Test write
            GUILayout.Space(4);
            GUILayout.Label("Test Write", _miniLabelStyle);
            GUILayout.BeginHorizontal();
            _testFileName = GUILayout.TextField(_testFileName, GUILayout.Width(80));
            _testFileContent = GUILayout.TextField(_testFileContent, GUILayout.Width(100));
            if (GUILayout.Button("Write", _smallButtonStyle, GUILayout.Width(45)))
            {
                _ = _storageManager.WriteFileAsync(_testFileName, _testFileContent);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawAntiCheatSection()
        {
            if (_antiCheatManager == null) _antiCheatManager = EOSAntiCheatManager.Instance;

            string title = "ANTI-CHEAT";
            if (_antiCheatManager != null)
            {
                switch (_antiCheatManager.Status)
                {
                    case AntiCheatStatus.Protected:
                        title = "\u2713 ANTI-CHEAT (Protected)";
                        break;
                    case AntiCheatStatus.Violated:
                        title = "\u26A0 ANTI-CHEAT (VIOLATION)";
                        break;
                    case AntiCheatStatus.NotAvailable:
                        title = "ANTI-CHEAT (N/A)";
                        break;
                    case AntiCheatStatus.Error:
                        title = "\u2717 ANTI-CHEAT (Error)";
                        break;
                }
            }

            _showAntiCheat = DrawFoldout(_showAntiCheat, title);
            if (!_showAntiCheat) return;

            GUILayout.BeginVertical(_boxStyle);

            if (_antiCheatManager == null || !_antiCheatManager.IsReady)
            {
                GUILayout.Label("Anti-cheat not available", _miniLabelStyle);
                GUILayout.Label("Configure EAC in EOS Developer Portal", _miniLabelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Status
            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _labelStyle, GUILayout.Width(60));
            GUIStyle statusStyle = _antiCheatManager.Status == AntiCheatStatus.Protected ? _greenStyle :
                                   _antiCheatManager.Status == AntiCheatStatus.Violated ? _redStyle : _valueStyle;
            GUILayout.Label(_antiCheatManager.Status.ToString(), statusStyle);
            GUILayout.EndHorizontal();

            // Session
            GUILayout.BeginHorizontal();
            GUILayout.Label("Session:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(_antiCheatManager.IsSessionActive ? "Active" : "Inactive",
                _antiCheatManager.IsSessionActive ? _greenStyle : _labelStyle);
            GUILayout.EndHorizontal();

            // Peers
            if (_antiCheatManager.IsSessionActive)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Peers:", _labelStyle, GUILayout.Width(60));
                GUILayout.Label(_antiCheatManager.RegisteredPeerCount.ToString(), _valueStyle);
                GUILayout.EndHorizontal();
            }

            // Auto-start toggle
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto-Start:", _labelStyle, GUILayout.Width(70));
            bool autoStart = GUILayout.Toggle(_antiCheatManager.AutoStartSession,
                _antiCheatManager.AutoStartSession ? "ON" : "OFF", _toggleStyle);
            if (autoStart != _antiCheatManager.AutoStartSession)
            {
                _antiCheatManager.AutoStartSession = autoStart;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Manual session controls
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_antiCheatManager.IsSessionActive;
            if (GUILayout.Button("Begin Session", _smallButtonStyle))
            {
                _antiCheatManager.BeginSession();
            }
            GUI.enabled = _antiCheatManager.IsSessionActive;
            if (GUILayout.Button("End Session", _smallButtonStyle))
            {
                _antiCheatManager.EndSession();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawReplaySection()
        {
            // Lazy init
            if (_replayRecorder == null) _replayRecorder = EOSReplayRecorder.Instance;
            if (_replayPlayer == null) _replayPlayer = EOSReplayPlayer.Instance;
            if (_replayStorage == null) _replayStorage = EOSReplayStorage.Instance;
            if (_replayViewer == null) _replayViewer = EOSReplayViewer.Instance;

            // Build title with state info
            string title = "REPLAYS";
            if (_replayViewer != null && _replayViewer.IsViewing)
            {
                title = "\u25B6 REPLAYS (Viewing)";
            }
            else if (_replayRecorder != null && _replayRecorder.IsRecording)
            {
                title = "\u25CF REPLAYS (Recording)";
            }
            else if (_replayStorage != null)
            {
                title = $"REPLAYS ({_replayStorage.LocalReplayCount})";
            }

            _showReplays = DrawFoldout(_showReplays, title);
            if (!_showReplays) return;

            GUILayout.BeginVertical(_boxStyle);

            // Recording settings
            if (_replayRecorder != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Auto-Record:", _labelStyle, GUILayout.Width(80));
                bool autoRecord = GUILayout.Toggle(_replayRecorder.AutoRecord, _replayRecorder.AutoRecord ? "ON" : "OFF", _toggleStyle);
                if (autoRecord != _replayRecorder.AutoRecord)
                {
                    _replayRecorder.AutoRecord = autoRecord;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Recording status
                if (_replayRecorder.IsRecording)
                {
                    float duration = _replayRecorder.Duration;
                    float maxDuration = _replayRecorder.MaxDuration;
                    float progress = Mathf.Clamp01(duration / maxDuration);
                    bool isApproaching = _replayRecorder.IsApproachingLimit;

                    // Status line
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("\u25CF Recording", isApproaching ? _orangeStyle : _redStyle, GUILayout.Width(80));
                    string durationText = $"{duration / 60f:F1}m / {maxDuration / 60f:F0}m";
                    GUILayout.Label(durationText, _valueStyle);
                    GUILayout.Label($"({_replayRecorder.FrameCount} frames)", _miniLabelStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // Duration progress bar
                    var progressRect = GUILayoutUtility.GetRect(0, 8, GUILayout.ExpandWidth(true));
                    GUI.Box(progressRect, GUIContent.none, _boxStyle);
                    var fillColor = isApproaching ? new Color(1f, 0.5f, 0f) : new Color(1f, 0.3f, 0.3f);
                    GUI.color = fillColor;
                    GUI.DrawTexture(new Rect(progressRect.x + 1, progressRect.y + 1, (progressRect.width - 2) * progress, progressRect.height - 2), Texture2D.whiteTexture);
                    GUI.color = Color.white;

                    // Estimated size
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"~{_replayRecorder.EstimatedSizeKB:F0} KB", _miniLabelStyle);
                    GUILayout.FlexibleSpace();
                    if (isApproaching)
                    {
                        GUILayout.Label("\u26A0 Approaching limit", _orangeStyle);
                    }
                    GUILayout.EndHorizontal();

                    if (GUILayout.Button("Stop Recording", _smallButtonStyle))
                    {
                        _ = _replayRecorder.StopAndSaveAsync();
                    }
                }
            }

            // Playback controls (when viewing)
            if (_replayViewer != null && _replayViewer.IsViewing)
            {
                GUILayout.Space(6);
                GUILayout.Label("NOW PLAYING", _sectionHeaderStyle);

                var header = _replayViewer.CurrentReplay;
                if (header.HasValue)
                {
                    GUILayout.Label($"{header.Value.GameMode} on {header.Value.MapName}", _valueStyle);
                    GUILayout.Label($"{header.Value.Participants?.Length ?? 0} players", _miniLabelStyle);
                }

                // Timeline
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{EOSReplayStorage.FormatDuration(_replayViewer.CurrentTime)}", _labelStyle, GUILayout.Width(45));

                float progress = _replayViewer.Duration > 0 ? _replayViewer.CurrentTime / _replayViewer.Duration : 0f;
                float newProgress = GUILayout.HorizontalSlider(progress, 0f, 1f);
                if (Mathf.Abs(newProgress - progress) > 0.01f)
                {
                    _replayViewer.SeekPercent(newProgress);
                }

                GUILayout.Label($"{EOSReplayStorage.FormatDuration(_replayViewer.Duration)}", _labelStyle, GUILayout.Width(45));
                GUILayout.EndHorizontal();

                // Event markers below timeline
                DrawReplayEventMarkers();

                // Playback controls
                GUILayout.BeginHorizontal();

                if (GUILayout.Button("<<", _smallButtonStyle, GUILayout.Width(30)))
                {
                    _replayViewer.Skip(-10f);
                }

                string playPauseIcon = _replayViewer.PlaybackState == PlaybackState.Playing ? "||" : "\u25B6";
                if (GUILayout.Button(playPauseIcon, _smallButtonStyle, GUILayout.Width(30)))
                {
                    _replayViewer.TogglePlayPause();
                }

                if (GUILayout.Button(">>", _smallButtonStyle, GUILayout.Width(30)))
                {
                    _replayViewer.Skip(10f);
                }

                GUILayout.Space(10);

                // Speed control
                string speedText = $"{_replayViewer.PlaybackSpeed:F1}x";
                if (GUILayout.Button(speedText, _smallButtonStyle, GUILayout.Width(40)))
                {
                    _replayViewer.CycleSpeed();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Stop", _smallButtonStyle, GUILayout.Width(40)))
                {
                    _replayViewer.StopViewing();
                }

                GUILayout.EndHorizontal();

                // Current target
                GUILayout.BeginHorizontal();
                GUILayout.Label("Viewing:", _labelStyle, GUILayout.Width(50));
                GUILayout.Label(_replayViewer.GetCurrentTargetName(), _valueStyle);

                if (GUILayout.Button("<", _smallButtonStyle, GUILayout.Width(25)))
                {
                    _replayViewer.CycleTarget(-1);
                }
                if (GUILayout.Button(">", _smallButtonStyle, GUILayout.Width(25)))
                {
                    _replayViewer.CycleTarget(1);
                }

                GUILayout.EndHorizontal();
            }

            // Replay list
            GUILayout.Space(6);

            // Refresh list periodically
            if (_replayStorage != null && Time.time - _lastReplayRefresh > 5f)
            {
                _cachedReplays = _replayStorage.GetLocalReplays();
                _lastReplayRefresh = Time.time;
            }

            if (GUILayout.Button("Refresh List", _smallButtonStyle))
            {
                _replayStorage?.RefreshLocalReplays();
                _cachedReplays = _replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
                _lastReplayRefresh = Time.time;
            }

            if (_cachedReplays.Count == 0)
            {
                GUILayout.Label("No saved replays", _miniLabelStyle);
            }
            else
            {
                GUILayout.Label($"SAVED ({_cachedReplays.Count})", _miniLabelStyle);

                _replayListScroll = GUILayout.BeginScrollView(_replayListScroll, GUILayout.Height(Mathf.Min(150, _cachedReplays.Count * 55 + 10)));

                for (int i = 0; i < _cachedReplays.Count; i++)
                {
                    var replay = _cachedReplays[i];
                    DrawReplayEntry(replay);
                }

                GUILayout.EndScrollView();
            }

            // Export success feedback
            if (_showExportSuccess && Time.time - _exportSuccessTime < 3f)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal(_boxStyle);
                GUILayout.Label("\u2713 Exported!", _greenStyle);
                if (GUILayout.Button("Open Folder", _smallButtonStyle, GUILayout.Width(80)))
                {
                    _replayStorage?.OpenExportFolder();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                _showExportSuccess = false;
            }

            // Import section
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            _importPath = GUILayout.TextField(_importPath, _textFieldStyle);
            if (GUILayout.Button("Import", _smallButtonStyle, GUILayout.Width(50)))
            {
                if (!string.IsNullOrWhiteSpace(_importPath))
                {
                    _ = ImportReplayAsync(_importPath);
                }
            }
            if (GUILayout.Button("\u21E4", _smallButtonStyle, GUILayout.Width(25))) // Open export folder
            {
                _replayStorage?.OpenExportFolder();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Paste replay file path to import, or click \u21E4 to open export folder", _miniLabelStyle);

            GUILayout.EndVertical();
        }

        private async System.Threading.Tasks.Task ImportReplayAsync(string path)
        {
            if (_replayStorage == null) return;

            bool success = await _replayStorage.ImportReplayAsync(path);
            if (success)
            {
                _importPath = "";
                _cachedReplays = _replayStorage.GetLocalReplays();
            }
        }

        private void DrawReplayEntry(ReplayHeader replay)
        {
            bool isFavorite = _replayStorage?.IsFavorite(replay.ReplayId) ?? false;

            GUILayout.BeginVertical(_boxStyle);

            // Title row
            GUILayout.BeginHorizontal();

            // Favorite star
            string starIcon = isFavorite ? "\u2605" : "\u2606"; // ★ or ☆
            GUI.color = isFavorite ? new Color(1f, 0.85f, 0.2f) : Color.gray;
            if (GUILayout.Button(starIcon, _smallButtonStyle, GUILayout.Width(25)))
            {
                _replayStorage?.ToggleFavorite(replay.ReplayId);
                _cachedReplays = _replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
            }
            GUI.color = Color.white;

            string title = $"{replay.GameMode} on {replay.MapName}";
            GUILayout.Label(title, _valueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(EOSReplayStorage.FormatDuration(replay.Duration), _labelStyle);
            GUILayout.EndHorizontal();

            // Info row
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{replay.Participants?.Length ?? 0} players", _miniLabelStyle);
            GUILayout.Label(" - ", _miniLabelStyle);
            GUILayout.Label(FormatTimeAgo(replay.RecordedAt), _miniLabelStyle);
            GUILayout.FlexibleSpace();

            // Buttons
            if (GUILayout.Button("\u25B6", _smallButtonStyle, GUILayout.Width(25)))
            {
                // Play replay
                _ = PlayReplayAsync(replay.ReplayId);
            }
            if (GUILayout.Button("\u21E5", _smallButtonStyle, GUILayout.Width(25))) // Export icon
            {
                // Export replay for sharing
                _ = ExportReplayAsync(replay.ReplayId);
            }
            if (GUILayout.Button("\u2715", _smallButtonStyle, GUILayout.Width(25)))
            {
                // Delete replay
                _replayStorage?.DeleteReplay(replay.ReplayId);
                _cachedReplays = _replayStorage?.GetLocalReplays() ?? new List<ReplayHeader>();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private async System.Threading.Tasks.Task ExportReplayAsync(string replayId)
        {
            if (_replayStorage == null) return;

            string path = await _replayStorage.ExportReplayAsync(replayId);
            if (!string.IsNullOrEmpty(path))
            {
                _lastExportPath = path;
                _showExportSuccess = true;
                _exportSuccessTime = Time.time;
            }
        }

        private async System.Threading.Tasks.Task PlayReplayAsync(string replayId)
        {
            if (_replayViewer == null || _replayStorage == null) return;

            var replay = await _replayStorage.LoadLocalAsync(replayId);
            if (replay.HasValue)
            {
                _replayViewer.StartViewing(replay.Value);
            }
        }

        private static string FormatTimeAgo(long unixTimeMs)
        {
            var recordedTime = DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMs);
            var elapsed = DateTimeOffset.UtcNow - recordedTime;

            if (elapsed.TotalMinutes < 1) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
            return recordedTime.LocalDateTime.ToString("MMM d");
        }

        private void DrawReplayEventMarkers()
        {
            if (_replayPlayer == null || !_replayPlayer.IsLoaded) return;

            var events = _replayPlayer.GetEventMarkers();
            var keyframes = _replayPlayer.GetKeyframeMarkers();

            if (events.Count == 0 && keyframes.Count <= 1) return;

            // Draw a thin bar showing markers
            GUILayout.BeginHorizontal();
            GUILayout.Space(45); // Align with slider

            var rect = GUILayoutUtility.GetRect(0, 12, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                // Background bar
                GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);

                // Keyframe markers (gray dots)
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                foreach (var kf in keyframes)
                {
                    float x = rect.x + kf * rect.width;
                    GUI.DrawTexture(new Rect(x - 1, rect.y + 4, 3, 4), Texture2D.whiteTexture);
                }

                // Event markers (colored based on type)
                foreach (var (time, type, desc) in events)
                {
                    GUI.color = type switch
                    {
                        ReplayEventType.PlayerJoined => new Color(0.4f, 1f, 0.4f, 0.9f),  // Green
                        ReplayEventType.PlayerLeft => new Color(1f, 0.4f, 0.4f, 0.9f),    // Red
                        ReplayEventType.GameEvent => new Color(1f, 1f, 0.4f, 0.9f),       // Yellow
                        _ => new Color(0.6f, 0.6f, 1f, 0.9f)  // Blue
                    };
                    float x = rect.x + time * rect.width;
                    GUI.DrawTexture(new Rect(x - 2, rect.y + 2, 5, 8), Texture2D.whiteTexture);
                }

                GUI.color = Color.white;
            }

            GUILayout.Space(45); // Align with slider
            GUILayout.EndHorizontal();

            // Legend (compact)
            if (events.Count > 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(45);
                GUILayout.Label($"{keyframes.Count} keyframes, {events.Count} events", _miniLabelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawMetricsSection()
        {
            if (_metricsManager == null) _metricsManager = EOSMetrics.Instance;

            _showMetrics = DrawFoldout(_showMetrics, "SESSION METRICS");
            if (!_showMetrics) return;

            GUILayout.BeginVertical(_boxStyle);

            if (_metricsManager == null || !_metricsManager.IsReady)
            {
                GUILayout.Label("Waiting for EOS login...", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            bool sessionActive = _metricsManager.IsSessionActive;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Session:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(sessionActive ? "Active" : "Inactive", sessionActive ? _greenStyle : _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (sessionActive)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Duration:", _labelStyle, GUILayout.Width(60));
                GUILayout.Label(_metricsManager.SessionDuration.ToString(@"hh\:mm\:ss"), _valueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_metricsManager.CurrentSessionId))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("ID:", _labelStyle, GUILayout.Width(60));
                    string shortId = _metricsManager.CurrentSessionId.Length > 12
                        ? _metricsManager.CurrentSessionId.Substring(0, 12) + "..."
                        : _metricsManager.CurrentSessionId;
                    GUILayout.Label(shortId, _miniLabelStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
            }

            // Manual control
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.enabled = !sessionActive;
            if (GUILayout.Button("Begin", _smallButtonStyle))
            {
                _metricsManager.BeginSession();
            }
            GUI.enabled = sessionActive;
            if (GUILayout.Button("End", _smallButtonStyle))
            {
                _metricsManager.EndSession();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label(_metricsManager.AutoTrackSessions ? "Auto-tracking FishNet connections" : "Manual mode", _miniLabelStyle);

            GUILayout.EndVertical();
        }

        private void DrawMigrationTestSection()
        {
            var migrationManager = HostMigrationManager.Instance;
            if (migrationManager == null) return;

            // Only show if in a lobby and connected
            var lobbyManager = EOSLobbyManager.Instance;
            bool isInLobby = lobbyManager != null && lobbyManager.IsInLobby;
            bool isConnected = _networkManager != null && (_networkManager.IsServerStarted || _networkManager.IsClientStarted);

            if (!isInLobby || !isConnected) return;

            // Title with migration indicator
            string title = migrationManager.IsMigrating ? "\u26A1 MIGRATION IN PROGRESS" : "HOST MIGRATION TEST";
            _showMigrationTest = DrawFoldout(_showMigrationTest, title);
            if (!_showMigrationTest) return;

            GUILayout.BeginVertical(_boxStyle);

            bool isHost = _networkManager.IsServerStarted;
            int memberCount = lobbyManager.CurrentLobby.MemberCount;

            // Status row with role and member count
            GUILayout.BeginHorizontal();

            // Role badge
            GUIStyle roleBadge = new GUIStyle(_smallButtonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.backgroundColor = isHost ? new Color(0.8f, 0.6f, 0.1f) : new Color(0.2f, 0.6f, 0.9f);
            GUILayout.Label(isHost ? " HOST " : " CLIENT ", roleBadge, GUILayout.Width(60));
            GUI.backgroundColor = Color.white;

            // Members
            GUIStyle memberStyle = memberCount >= 2 ? _greenStyle : _orangeStyle;
            GUILayout.Label($"  {memberCount} members", memberStyle);
            if (memberCount < 2)
            {
                GUILayout.Label("(need 2+)", _miniLabelStyle);
            }

            GUILayout.FlexibleSpace();

            // Migration status indicator
            if (migrationManager.IsMigrating)
            {
                GUILayout.Label("\u23F3", _yellowStyle); // Hourglass
            }
            else
            {
                GUILayout.Label("\u2713", _greenStyle); // Checkmark
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (isHost)
            {
                // Instructions box
                GUILayout.BeginVertical(_statsBoxStyle);
                GUILayout.Label("Test Steps:", _labelStyle);
                GUILayout.Label("1. Open ParrelSync clone", _miniLabelStyle);
                GUILayout.Label("2. Clone joins with your room code", _miniLabelStyle);
                GUILayout.Label("3. Click button below OR exit Play Mode", _miniLabelStyle);
                GUILayout.Label("4. Clone becomes new host automatically", _miniLabelStyle);
                GUILayout.EndVertical();

                GUILayout.Space(4);

                // Main action button
                GUI.enabled = memberCount >= 2;
                GUI.backgroundColor = new Color(0.9f, 0.4f, 0.2f);
                if (GUILayout.Button(memberCount >= 2 ? "\u27A1 LEAVE & TRANSFER HOST" : "Need 2+ players", _buttonStyle))
                {
                    SimulateHostLeave();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                GUILayout.Space(4);

                // Debug section (collapsed by default)
                GUILayout.BeginHorizontal();
                GUILayout.Label("Debug:", _miniLabelStyle, GUILayout.Width(45));
                if (GUILayout.Button("Save", _smallButtonStyle, GUILayout.Width(50)))
                {
                    migrationManager.DebugTriggerSave();
                    _lobbyStatus = "States saved!";
                }
                if (GUILayout.Button("Restore", _smallButtonStyle, GUILayout.Width(55)))
                {
                    migrationManager.DebugTriggerFinish();
                    _lobbyStatus = "Migration triggered!";
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                // Client view - waiting for migration
                GUILayout.BeginVertical(_statsBoxStyle);

                // Current host
                string hostPuid = lobbyManager.CurrentLobby.OwnerPuid;
                string shortHost = hostPuid?.Length > 16 ? hostPuid.Substring(0, 8) + "..." : hostPuid;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Host:", _labelStyle, GUILayout.Width(40));
                GUILayout.Label(shortHost ?? "(unknown)", _cyanStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.Space(2);
                GUILayout.Label("Waiting for host to leave...", _miniLabelStyle);
                GUILayout.Label("You'll become host automatically!", _miniLabelStyle);
                GUILayout.EndVertical();

                GUILayout.Space(4);

                // What to watch for
                GUILayout.Label("Watch console for:", _miniLabelStyle);
                GUILayout.Label("  [HostMigrationManager] logs", _cyanStyle);
            }

            GUILayout.EndVertical();
        }

        private async void SimulateHostLeave()
        {
            _lobbyStatus = "Simulating host leave...";

            // Stop FishNet server (this triggers migration for clients)
            if (_networkManager.IsServerStarted)
            {
                _networkManager.ServerManager.StopConnection(true);
            }
            if (_networkManager.IsClientStarted)
            {
                _networkManager.ClientManager.StopConnection();
            }

            // Leave the lobby (EOS will promote another member)
            await _transport.LeaveLobbyAsync();

            _lobbyStatus = "Host left - another player should become host";
        }

        private void DrawAdvancedSection()
        {
            _showAdvanced = DrawFoldout(_showAdvanced, "MANUAL CONTROLS");
            if (!_showAdvanced) return;

            GUILayout.BeginVertical(_boxStyle);

            GUILayout.Label("Manual FishNet control (lobby optional)", _miniLabelStyle);

            var eosManager = EOSManager.Instance;
            bool isLoggedIn = eosManager != null && eosManager.IsLoggedIn;

            if (_transport == null || _networkManager == null)
            {
                GUILayout.Label("Transport not found!", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            var serverState = _transport.GetConnectionState(true);
            var clientState = _transport.GetConnectionState(false);
            bool serverRunning = serverState == FishNet.Transporting.LocalConnectionState.Started;
            bool clientRunning = clientState == FishNet.Transporting.LocalConnectionState.Started;
            bool anyRunning = serverRunning || clientRunning;

            // Status
            GUILayout.BeginHorizontal();
            GUILayout.Label("FishNet:", _labelStyle, GUILayout.Width(50));
            if (serverRunning && clientRunning)
                GUILayout.Label("Host (Server+Client)", _greenStyle);
            else if (serverRunning)
                GUILayout.Label("Server Only", _cyanStyle);
            else if (clientRunning)
                GUILayout.Label("Client Only", _yellowStyle);
            else
                GUILayout.Label("Stopped", _labelStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Connection buttons row 1: Host / Stop
            GUILayout.BeginHorizontal();
            GUI.enabled = isLoggedIn && !anyRunning;
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.3f);
            if (GUILayout.Button("Host (S+C)", _smallButtonStyle))
            {
                _networkManager.ServerManager.StartConnection();
                _networkManager.ClientManager.StartConnection();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = anyRunning;
            GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
            if (GUILayout.Button("Stop All", _smallButtonStyle))
            {
                _transport.StopHost();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // Connection buttons row 2: Server / Client
            GUILayout.BeginHorizontal();
            GUI.enabled = isLoggedIn && !serverRunning;
            if (GUILayout.Button("Server", _smallButtonStyle))
            {
                _networkManager.ServerManager.StartConnection();
            }
            GUI.enabled = isLoggedIn && !clientRunning && !string.IsNullOrEmpty(_remoteProductUserId);
            if (GUILayout.Button("Client", _smallButtonStyle))
            {
                _transport.RemoteProductUserId = _remoteProductUserId;
                _networkManager.ClientManager.StartConnection();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // Remote PUID input (for client connection)
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Remote:", _labelStyle, GUILayout.Width(50));
            _remoteProductUserId = GUILayout.TextField(_remoteProductUserId, new GUIStyle(_textFieldStyle) { fontSize = 9, alignment = TextAnchor.MiddleLeft });
            GUILayout.EndHorizontal();
            GUILayout.Label("Paste host's PUID to connect as client", _miniLabelStyle);

            GUILayout.EndVertical();
        }

        #region Async Operations

        private async void SearchLobbies()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Searching...";

            var lobbyManager = EOSLobbyManager.Instance;
            var searchOptions = new LobbySearchOptions { MaxResults = 20, OnlyAvailable = false };

            var (result, lobbies) = await lobbyManager.SearchLobbiesAsync(searchOptions);

            if (result == Result.Success)
            {
                _foundLobbies = lobbies ?? new List<LobbyData>();
                _lobbyStatus = $"Found {_foundLobbies.Count} lobbies";
            }
            else
            {
                _lobbyStatus = $"Search failed: {result}";
                _foundLobbies.Clear();
            }

            _lobbyOperationInProgress = false;
        }

        private async void SearchLobbiesWithFilters()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Searching...";

            var lobbyManager = EOSLobbyManager.Instance;

            // Build search options with filters
            var searchOptions = new LobbySearchOptions
            {
                MaxResults = 50,
                OnlyAvailable = true,
                ExcludePasswordProtected = _filterExcludePassworded,
                ExcludeInProgress = _filterExcludeInProgress
            };

            // Apply text filters
            if (!string.IsNullOrWhiteSpace(_filterGameMode))
            {
                searchOptions.WithGameMode(_filterGameMode.Trim());
            }
            if (!string.IsNullOrWhiteSpace(_filterRegion))
            {
                searchOptions.WithRegion(_filterRegion.Trim());
            }

            // Apply skill range
            if (int.TryParse(_filterSkillMin, out int minSkill))
            {
                searchOptions.WithMinSkill(minSkill);
            }
            if (int.TryParse(_filterSkillMax, out int maxSkill))
            {
                searchOptions.WithMaxSkill(maxSkill);
            }

            var (result, lobbies) = await lobbyManager.SearchLobbiesAsync(searchOptions);

            if (result == Result.Success)
            {
                _foundLobbies = lobbies ?? new List<LobbyData>();
                _lobbyStatus = $"Found {_foundLobbies.Count} lobbies";
            }
            else
            {
                _lobbyStatus = $"Search failed: {result}";
                _foundLobbies.Clear();
            }

            _lobbyOperationInProgress = false;
        }

        private async void JoinLobbyById(string lobbyId)
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Joining...";

            var lobbyManager = EOSLobbyManager.Instance;
            var (result, lobby) = await lobbyManager.JoinLobbyByIdAsync(lobbyId);

            if (result == Result.Success)
            {
                _lobbyStatus = $"Connecting to {lobby.JoinCode}...";
                _remoteProductUserId = lobby.OwnerPuid;
                _showLobbyBrowser = false;
                _foundLobbies.Clear();

                if (!string.IsNullOrEmpty(lobby.OwnerPuid))
                {
                    _transport.RemoteProductUserId = lobby.OwnerPuid;
                    _networkManager.ClientManager.StartConnection();
                    _lobbyStatus = $"Connected: {lobby.JoinCode}";
                }
            }
            else
            {
                _lobbyStatus = $"Join failed: {result}";
            }

            _lobbyOperationInProgress = false;
        }

        private async void HostLobby()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Starting host...";

            string code = string.IsNullOrEmpty(_joinCode) ? null : _joinCode;
            var (result, lobby) = await _transport.HostLobbyAsync(code);

            if (result == Result.Success)
            {
                _lobbyStatus = $"Hosting: {lobby.JoinCode}";
                _joinCode = lobby.JoinCode;
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
        }

        private async void JoinLobbyAndConnect()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = $"Joining {_joinCode}...";

            var (result, lobby) = await _transport.JoinLobbyAsync(_joinCode, _autoConnectOnJoin);

            if (result == Result.Success)
            {
                _remoteProductUserId = lobby.OwnerPuid;
                _lobbyStatus = _autoConnectOnJoin ? $"Connected: {lobby.JoinCode}" : $"In lobby: {lobby.JoinCode} (not connected)";
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
        }

        private async void LeaveLobby()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Leaving...";

            await _transport.LeaveLobbyAsync();

            _lobbyStatus = "";
            _joinCode = "";
            _lobbyOperationInProgress = false;
        }

        private async void QuickMatch()
        {
            _lobbyOperationInProgress = true;
            _lobbyStatus = "Finding match...";

            var (result, lobby, didHost) = await _transport.QuickMatchOrHostAsync();

            if (result == Result.Success)
            {
                _joinCode = lobby.JoinCode;
                _lobbyStatus = didHost ? $"Hosting: {lobby.JoinCode}" : $"Matched: {lobby.JoinCode}";
            }
            else
            {
                _lobbyStatus = $"Failed: {result}";
            }

            _lobbyOperationInProgress = false;
        }

        private void CopyLobbyStateToClipboard(LobbyData lobby)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== LOBBY STATE ===");
            sb.AppendLine($"Code: {lobby.JoinCode}");
            sb.AppendLine($"ID: {lobby.LobbyId}");
            sb.AppendLine($"Owner: {lobby.OwnerPuid}");
            sb.AppendLine($"Players: {lobby.MemberCount}/{lobby.MaxMembers}");
            sb.AppendLine();

            // Attributes
            sb.AppendLine("--- Attributes ---");
            if (!string.IsNullOrEmpty(lobby.LobbyName)) sb.AppendLine($"Name: {lobby.LobbyName}");
            if (!string.IsNullOrEmpty(lobby.GameMode)) sb.AppendLine($"GameMode: {lobby.GameMode}");
            if (!string.IsNullOrEmpty(lobby.Map)) sb.AppendLine($"Map: {lobby.Map}");
            if (!string.IsNullOrEmpty(lobby.Region)) sb.AppendLine($"Region: {lobby.Region}");
            if (lobby.SkillLevel > 0) sb.AppendLine($"Skill: {lobby.SkillLevel}");
            sb.AppendLine($"InProgress: {lobby.IsInProgress}");
            sb.AppendLine($"Public: {lobby.IsPublic}");
            sb.AppendLine();

            // Members
            sb.AppendLine("--- Members ---");
            var registry = EOSPlayerRegistry.Instance;
            foreach (var memberPuid in lobby.MemberPuids)
            {
                string name = registry?.GetDisplayName(memberPuid) ?? memberPuid;
                string platform = registry?.GetPlatform(memberPuid) ?? "?";
                bool isOwner = memberPuid == lobby.OwnerPuid;
                sb.AppendLine($"  {(isOwner ? "[HOST] " : "")}{name} ({platform}) - {memberPuid}");
            }
            sb.AppendLine();

            // Network state
            sb.AppendLine("--- Network ---");
            sb.AppendLine($"Server Started: {_networkManager?.IsServerStarted}");
            sb.AppendLine($"Client Started: {_networkManager?.IsClientStarted}");
            if (_networkManager?.IsServerStarted == true)
            {
                sb.AppendLine($"Connected Clients: {_networkManager.ServerManager.Clients.Count}");
            }
            sb.AppendLine();

            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("==================");

            GUIUtility.systemCopyBuffer = sb.ToString();
            EOSToastManager.Success("Copied!", "Lobby state copied to clipboard");
        }

        #endregion

        #region Voice Tab

        private void DrawVoiceTab()
        {
            _voiceScrollPosition = GUILayout.BeginScrollView(_voiceScrollPosition, false, true);

            DrawVoiceRTCStatus();
            GUILayout.Space(4);
            DrawVoiceLocalMic();
            GUILayout.Space(4);
            DrawVoiceZoneSection();
            GUILayout.Space(4);
            DrawVoiceParticipants();
            GUILayout.Space(4);
            DrawVoiceControls();

            GUILayout.EndScrollView();
        }

        private void DrawVoiceRTCStatus()
        {
            GUILayout.Label("RTC STATUS", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            var lobbyManager = EOSLobbyManager.Instance;

            if (voiceManager == null)
            {
                GUILayout.Label("EOSVoiceManager not found", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("RTC Room:", _labelStyle, GUILayout.Width(80));
            GUILayout.Label(voiceManager.IsConnected ? "Connected" : "Disconnected",
                voiceManager.IsConnected ? _greenStyle : _redStyle);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(voiceManager.CurrentRoomName))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Room:", _labelStyle, GUILayout.Width(80));
                string shortRoom = voiceManager.CurrentRoomName.Length > 24
                    ? voiceManager.CurrentRoomName.Substring(0, 21) + "..."
                    : voiceManager.CurrentRoomName;
                GUILayout.Label(shortRoom, _valueStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Voice:", _labelStyle, GUILayout.Width(80));
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                bool voiceEnabled = voiceManager.IsVoiceEnabled;
                GUILayout.Label(voiceEnabled ? "Enabled" : "Disabled", voiceEnabled ? _greenStyle : _yellowStyle);
            }
            else
            {
                GUILayout.Label("No Lobby", _labelStyle);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawVoiceLocalMic()
        {
            GUILayout.Label("LOCAL MICROPHONE", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected)
            {
                GUILayout.Label("Not connected to voice", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _labelStyle, GUILayout.Width(60));
            GUILayout.Label(voiceManager.IsMuted ? "MUTED" : "Active",
                voiceManager.IsMuted ? _redStyle : _greenStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Level:", _labelStyle, GUILayout.Width(60));
            DrawLevelBar(_localInputLevel, _localPeakLevel, 180);
            GUILayout.Label($"{(_localInputLevel * 100):F0}%", _miniLabelStyle, GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUI.backgroundColor = voiceManager.IsMuted ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.4f);
            if (GUILayout.Button(voiceManager.IsMuted ? "UNMUTE MIC" : "MUTE MIC", _buttonStyle))
            {
                voiceManager.ToggleMute();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }

        private void DrawVoiceParticipants()
        {
            GUILayout.Label("VOICE PARTICIPANTS", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected)
            {
                GUILayout.Label("Not connected", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Use GetAllParticipants() from voice manager instead of finding scene objects
            // This ensures we see participants even if no EOSVoicePlayer objects are spawned
            var participants = voiceManager.GetAllParticipants();

            if (participants == null || participants.Count == 0)
            {
                GUILayout.Label("No remote participants", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            foreach (var puid in participants)
            {
                if (string.IsNullOrEmpty(puid)) continue;
                DrawVoiceParticipantRowByPuid(puid, voiceManager);
            }

            GUILayout.EndVertical();
        }

        private void DrawVoiceParticipantRow(EOSVoicePlayer player, EOSVoiceManager voiceManager)
        {
            string puid = player.ParticipantPuid;
            string shortPuid = puid.Length > 12 ? puid.Substring(0, 8) + "..." : puid;
            bool isSpeaking = player.IsSpeaking;
            float level = _audioLevels.TryGetValue(puid, out float l) ? l : 0f;
            float peak = _peakLevels.TryGetValue(puid, out float p) ? p : 0f;

            GUILayout.BeginVertical(_boxStyle);

            GUILayout.BeginHorizontal();
            string speakIcon = isSpeaking ? "\u25CF" : "\u25CB";
            GUILayout.Label(speakIcon, isSpeaking ? _greenStyle : _labelStyle, GUILayout.Width(15));
            GUILayout.Label(shortPuid, _valueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isSpeaking ? "Speaking" : "Silent", isSpeaking ? _greenStyle : _labelStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(15));
            DrawLevelBar(level, peak, 150);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Mute", _smallButtonStyle, GUILayout.Width(50)))
            {
                voiceManager.SetParticipantMuted(puid, true);
            }
            GUILayout.EndHorizontal();

            var audioStatus = voiceManager.GetParticipantAudioStatus(puid);
            if (audioStatus != RTCAudioStatus.Enabled)
            {
                GUILayout.Label($"Audio: {audioStatus}", _yellowStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawVoiceParticipantRowByPuid(string puid, EOSVoiceManager voiceManager)
        {
            string shortPuid = puid.Length > 12 ? puid.Substring(0, 8) + "..." : puid;
            bool isSpeaking = voiceManager.IsSpeaking(puid);
            float level = _audioLevels.TryGetValue(puid, out float l) ? l : 0f;
            float peak = _peakLevels.TryGetValue(puid, out float p) ? p : 0f;

            GUILayout.BeginVertical(_boxStyle);

            GUILayout.BeginHorizontal();
            string speakIcon = isSpeaking ? "\u25CF" : "\u25CB";
            GUILayout.Label(speakIcon, isSpeaking ? _greenStyle : _labelStyle, GUILayout.Width(15));
            GUILayout.Label(shortPuid, _valueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isSpeaking ? "Speaking" : "Silent", isSpeaking ? _greenStyle : _labelStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(15));
            DrawLevelBar(level, peak, 150);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Mute", _smallButtonStyle, GUILayout.Width(50)))
            {
                voiceManager.SetParticipantMuted(puid, true);
            }
            GUILayout.EndHorizontal();

            var audioStatus = voiceManager.GetParticipantAudioStatus(puid);
            if (audioStatus != RTCAudioStatus.Enabled)
            {
                GUILayout.Label($"Audio: {audioStatus}", _yellowStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawLevelBar(float level, float peak, float width)
        {
            Rect bgRect = GUILayoutUtility.GetRect(width, 12);
            GUI.DrawTexture(bgRect, _levelBarBgTexture);

            if (level > 0.01f)
            {
                Rect fillRect = new Rect(bgRect.x + 1, bgRect.y + 1, (bgRect.width - 2) * level, bgRect.height - 2);
                GUI.DrawTexture(fillRect, _levelBarFgTexture);
            }

            if (peak > 0.01f)
            {
                float peakX = bgRect.x + (bgRect.width - 2) * peak;
                Rect peakRect = new Rect(peakX, bgRect.y + 1, 2, bgRect.height - 2);
                GUI.DrawTexture(peakRect, _levelBarPeakTexture);
            }
        }

        private void DrawVoiceControls()
        {
            GUILayout.Label("QUICK ACTIONS", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            bool connected = voiceManager != null && voiceManager.IsConnected;

            GUI.enabled = connected;

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.8f, 0.5f, 0.3f);
            if (GUILayout.Button("Mute All", _smallButtonStyle))
            {
                MuteAllParticipants();
            }
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.5f);
            if (GUILayout.Button("Unmute All", _smallButtonStyle))
            {
                UnmuteAllParticipants();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private void MuteAllParticipants()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            var players = FindObjectsByType<EOSVoicePlayer>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (!string.IsNullOrEmpty(player.ParticipantPuid))
                    voiceManager.SetParticipantMuted(player.ParticipantPuid, true);
            }
        }

        private void UnmuteAllParticipants()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            var players = FindObjectsByType<EOSVoicePlayer>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (!string.IsNullOrEmpty(player.ParticipantPuid))
                    voiceManager.SetParticipantMuted(player.ParticipantPuid, false);
            }
        }

        private void DrawVoiceZoneSection()
        {
            GUILayout.Label("VOICE ZONES", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            var zoneManager = EOSVoiceZoneManager.Instance;
            if (zoneManager == null)
            {
                GUILayout.Label("Voice Zone Manager not found", _miniLabelStyle);
                if (GUILayout.Button("Add Voice Zone Manager", _smallButtonStyle))
                {
                    var go = EOSVoiceManager.Instance?.gameObject ?? GameObject.Find("NetworkManager");
                    if (go != null)
                        go.AddComponent<EOSVoiceZoneManager>();
                }
                GUILayout.EndVertical();
                return;
            }

            // Current mode display
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode:", _labelStyle, GUILayout.Width(50));
            GUILayout.Label(zoneManager.ZoneMode.ToString(), _greenStyle);
            GUILayout.EndHorizontal();

            // Mode selection buttons
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = zoneManager.ZoneMode == VoiceZoneMode.Global ? Color.green : Color.white;
            if (GUILayout.Button("Global", _smallButtonStyle))
                zoneManager.SetZoneMode(VoiceZoneMode.Global);

            GUI.backgroundColor = zoneManager.ZoneMode == VoiceZoneMode.Proximity ? Color.green : Color.white;
            if (GUILayout.Button("Proximity", _smallButtonStyle))
                zoneManager.SetZoneMode(VoiceZoneMode.Proximity);

            GUI.backgroundColor = zoneManager.ZoneMode == VoiceZoneMode.Team ? Color.green : Color.white;
            if (GUILayout.Button("Team", _smallButtonStyle))
                zoneManager.SetZoneMode(VoiceZoneMode.Team);

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = zoneManager.ZoneMode == VoiceZoneMode.TeamProximity ? Color.green : Color.white;
            if (GUILayout.Button("Team+Prox", _smallButtonStyle))
                zoneManager.SetZoneMode(VoiceZoneMode.TeamProximity);

            GUI.backgroundColor = zoneManager.ZoneMode == VoiceZoneMode.Custom ? Color.green : Color.white;
            if (GUILayout.Button("Custom", _smallButtonStyle))
                zoneManager.SetZoneMode(VoiceZoneMode.Custom);

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            // Show mode-specific info
            if (zoneManager.ZoneMode == VoiceZoneMode.Proximity || zoneManager.ZoneMode == VoiceZoneMode.TeamProximity)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Range:", _labelStyle, GUILayout.Width(50));
                GUILayout.Label($"{zoneManager.MaxHearingDistance:0}m", _valueStyle);
                GUILayout.EndHorizontal();

                var playersInRange = zoneManager.GetPlayersInRange();
                GUILayout.BeginHorizontal();
                GUILayout.Label("In Range:", _labelStyle, GUILayout.Width(50));
                GUILayout.Label(playersInRange.Count.ToString(), _greenStyle);
                GUILayout.EndHorizontal();
            }

            if (zoneManager.ZoneMode == VoiceZoneMode.Team || zoneManager.ZoneMode == VoiceZoneMode.TeamProximity)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Team:", _labelStyle, GUILayout.Width(50));

                // Team selection buttons
                for (int i = 0; i < 4; i++)
                {
                    GUI.backgroundColor = zoneManager.LocalTeam == i ? Color.cyan : Color.white;
                    if (GUILayout.Button(i.ToString(), _smallButtonStyle, GUILayout.Width(30)))
                        zoneManager.SetTeam(i);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        #endregion

        #region Network Tab

        private void DrawNetworkTab()
        {
            _networkScrollPosition = GUILayout.BeginScrollView(_networkScrollPosition, false, true);

            DrawNetworkP2PSection();
            GUILayout.Space(4);
            DrawNetworkBandwidthSection();
            GUILayout.Space(4);
            DrawNetworkMigrationSection();
            GUILayout.Space(4);
            DrawNetworkConnectionsSection();
            GUILayout.Space(4);
            DrawAfkSettingsSection();
            GUILayout.Space(4);
            DrawVoteKickSection();
            GUILayout.Space(4);
            DrawMapVoteSection();

            GUILayout.EndScrollView();
        }

        private void DrawNetworkP2PSection()
        {
            GUILayout.Label("P2P STATUS", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            if (_transport == null)
            {
                GUILayout.Label("Transport not found", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            var serverState = _transport.GetConnectionState(true);
            var clientState = _transport.GetConnectionState(false);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Server:", _labelStyle, GUILayout.Width(65));
            GUILayout.Label(serverState.ToString(), GetStateStyle(serverState));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Client:", _labelStyle, GUILayout.Width(65));
            GUILayout.Label(clientState.ToString(), GetStateStyle(clientState));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Socket:", _labelStyle, GUILayout.Width(65));
            GUILayout.Label(_transport.SocketName, _valueStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Relay:", _labelStyle, GUILayout.Width(65));
            GUILayout.Label(_transport.RelayControlSetting.ToString(), _valueStyle);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_transport.RemoteProductUserId))
            {
                string shortPuid = _transport.RemoteProductUserId.Length > 20
                    ? _transport.RemoteProductUserId.Substring(0, 8) + "..." + _transport.RemoteProductUserId.Substring(_transport.RemoteProductUserId.Length - 6)
                    : _transport.RemoteProductUserId;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Remote:", _labelStyle, GUILayout.Width(65));
                GUILayout.Label(shortPuid, _valueStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawNetworkBandwidthSection()
        {
            GUILayout.Label("BANDWIDTH", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            float inKBps = _inboundBytesPerSec / 1024f;
            float outKBps = _outboundBytesPerSec / 1024f;

            GUILayout.BeginHorizontal();
            GUILayout.Label("In:", _labelStyle, GUILayout.Width(35));
            GUILayout.Label($"{inKBps:F1} KB/s", _greenStyle, GUILayout.Width(80));
            GUILayout.Label("Out:", _labelStyle, GUILayout.Width(35));
            GUILayout.Label($"{outKBps:F1} KB/s", _orangeStyle);
            GUILayout.EndHorizontal();

            // Ping display with jitter and quality indicator
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ping:", _labelStyle, GUILayout.Width(35));
            if (_networkManager != null && _networkManager.IsClientStarted && !_networkManager.IsServerStarted)
            {
                // Show current ping with color coding
                long ping = _networkManager.TimeManager.RoundTripTime;
                GUIStyle pingStyle = ping < 100 ? _greenStyle : ping < 200 ? _yellowStyle : _orangeStyle;
                GUILayout.Label($"{ping}ms", pingStyle, GUILayout.Width(50));

                // Show jitter (±)
                GUILayout.Label($"±{_jitter}ms", _miniLabelStyle, GUILayout.Width(50));

                // Show quality indicator
                string qualityIcon = _connectionQuality switch
                {
                    ConnectionQuality.Excellent => "●●●●●",
                    ConnectionQuality.Good => "●●●●○",
                    ConnectionQuality.Fair => "●●●○○",
                    ConnectionQuality.Poor => "●●○○○",
                    ConnectionQuality.Bad => "●○○○○",
                    _ => "○○○○○"
                };
                GUIStyle qualityStyle = _connectionQuality switch
                {
                    ConnectionQuality.Excellent => _greenStyle,
                    ConnectionQuality.Good => _greenStyle,
                    ConnectionQuality.Fair => _yellowStyle,
                    ConnectionQuality.Poor => _orangeStyle,
                    _ => _orangeStyle
                };
                GUILayout.Label(qualityIcon, qualityStyle);
            }
            else
            {
                GUILayout.Label("--", _miniLabelStyle);
            }
            GUILayout.EndHorizontal();

            if (_transport != null)
            {
                float totalInMB = _transport.TotalBytesReceived / (1024f * 1024f);
                float totalOutMB = _transport.TotalBytesSent / (1024f * 1024f);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Total:", _labelStyle, GUILayout.Width(45));
                GUILayout.Label($"In: {totalInMB:F2} MB  Out: {totalOutMB:F2} MB", _miniLabelStyle);
                GUILayout.EndHorizontal();
            }

            DrawBandwidthGraph();

            GUILayout.EndVertical();
        }

        private void DrawBandwidthGraph()
        {
            Rect graphRect = GUILayoutUtility.GetRect(280, 60);
            GUI.DrawTexture(graphRect, _graphBgTexture);

            if (_inboundHistory.Count < 2) return;

            float maxValue = 10f;
            foreach (float v in _inboundHistory) maxValue = Mathf.Max(maxValue, v);
            foreach (float v in _outboundHistory) maxValue = Mathf.Max(maxValue, v);

            DrawGraphLine(graphRect, _inboundHistory, maxValue, new Color(0.3f, 0.8f, 0.4f, 0.8f));
            DrawGraphLine(graphRect, _outboundHistory, maxValue, new Color(1f, 0.6f, 0.3f, 0.8f));

            GUI.Label(new Rect(graphRect.x + 2, graphRect.y + 2, 80, 15), $"Max: {maxValue:F0} KB/s", _miniLabelStyle);
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

        private void DrawNetworkMigrationSection()
        {
            GUILayout.Label("HOST MIGRATION", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            var migrationManager = HostMigrationManager.Instance;
            var lobbyManager = EOSLobbyManager.Instance;

            if (migrationManager == null)
            {
                GUILayout.Label("HostMigrationManager not found", _yellowStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _labelStyle, GUILayout.Width(65));
            string migrationStatus = migrationManager.IsMigrating ? "MIGRATING" : "Ready";
            GUIStyle migrationStyle = migrationManager.IsMigrating ? _orangeStyle : _greenStyle;
            GUILayout.Label(migrationStatus, migrationStyle);
            GUILayout.EndHorizontal();

            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Role:", _labelStyle, GUILayout.Width(65));
                GUILayout.Label(lobbyManager.IsOwner ? "HOST" : "CLIENT",
                    lobbyManager.IsOwner ? _greenStyle : _valueStyle);
                GUILayout.EndHorizontal();

                string ownerPuid = lobbyManager.CurrentLobby.OwnerPuid ?? "Unknown";
                string shortOwner = ownerPuid.Length > 16 ? ownerPuid.Substring(0, 8) + "..." : ownerPuid;

                GUILayout.BeginHorizontal();
                GUILayout.Label("Host:", _labelStyle, GUILayout.Width(65));
                GUILayout.Label(shortOwner, _valueStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);
            GUILayout.Label("Debug Controls", _miniLabelStyle);

            GUILayout.BeginHorizontal();
            GUI.enabled = !migrationManager.IsMigrating;
            GUI.backgroundColor = new Color(0.7f, 0.7f, 0.3f);
            if (GUILayout.Button("Save State", _smallButtonStyle))
            {
                migrationManager.DebugTriggerSave();
            }
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.7f);
            if (GUILayout.Button("Finish Migration", _smallButtonStyle))
            {
                migrationManager.DebugTriggerFinish();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawNetworkConnectionsSection()
        {
            GUILayout.Label("CONNECTIONS", _sectionHeaderStyle);
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

                var afkManager = EOSAfkManager.Instance;
                foreach (var kvp in _networkManager.ServerManager.Clients)
                {
                    int connId = kvp.Key;
                    string puid = _transport?.GetPuidForConnection(connId) ?? "Unknown";
                    string shortPuid = puid.Length > 16 ? puid.Substring(0, 8) + "..." : puid;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"  [{connId}]", _labelStyle, GUILayout.Width(45));
                    GUILayout.Label(shortPuid, _miniLabelStyle);

                    // AFK indicator
                    if (afkManager != null && afkManager.Enabled && afkManager.IsPlayerAfk(connId))
                    {
                        float afkTime = afkManager.GetAfkDuration(connId);
                        float kickTime = afkManager.GetTimeUntilKick(connId);
                        string afkText = kickTime > 0 ? $"AFK ({kickTime:0}s)" : $"AFK ({afkTime:0}s)";
                        GUILayout.Label(afkText, _orangeStyle, GUILayout.Width(70));

                        if (GUILayout.Button("Kick", _smallButtonStyle, GUILayout.Width(40)))
                        {
                            afkManager.KickPlayer(connId, "Kicked by host");
                        }
                    }

                    // Vote kick button (for non-hosts, or host to start votes against others)
                    var voteKickManager = EOSVoteKickManager.Instance;
                    if (voteKickManager != null && voteKickManager.Enabled && !voteKickManager.IsVoteActive)
                    {
                        if (voteKickManager.CanBeVoteKicked(puid) && voteKickManager.GetCooldownRemaining() <= 0)
                        {
                            if (GUILayout.Button("VoteKick", _smallButtonStyle, GUILayout.Width(60)))
                            {
                                _ = voteKickManager.StartVoteKickAsync(connId, "Disruptive behavior");
                            }
                        }
                    }

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
                GUILayout.Label("Lobby Sync:", _labelStyle, GUILayout.Width(80));
                GUILayout.Label(syncStatus, syncStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawAfkSettingsSection()
        {
            var afkManager = EOSAfkManager.Instance;
            if (afkManager == null) return;

            GUILayout.Label("AFK DETECTION", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            // Enable toggle
            GUILayout.BeginHorizontal();
            GUILayout.Label("Enabled:", _labelStyle, GUILayout.Width(80));
            bool newEnabled = GUILayout.Toggle(afkManager.Enabled, afkManager.Enabled ? "ON" : "OFF", _smallButtonStyle, GUILayout.Width(50));
            if (newEnabled != afkManager.Enabled)
                afkManager.Enabled = newEnabled;
            GUILayout.EndHorizontal();

            if (afkManager.Enabled)
            {
                // AFK threshold
                GUILayout.BeginHorizontal();
                GUILayout.Label("AFK Threshold:", _labelStyle, GUILayout.Width(90));
                GUILayout.Label($"{afkManager.AfkThreshold:0}s", _valueStyle, GUILayout.Width(40));
                float newThreshold = GUILayout.HorizontalSlider(afkManager.AfkThreshold, 30f, 300f, GUILayout.Width(100));
                if (Mathf.Abs(newThreshold - afkManager.AfkThreshold) > 1f)
                    afkManager.AfkThreshold = Mathf.Round(newThreshold / 10f) * 10f;
                GUILayout.EndHorizontal();

                // Auto-kick delay
                GUILayout.BeginHorizontal();
                GUILayout.Label("Auto-Kick:", _labelStyle, GUILayout.Width(90));
                string kickLabel = afkManager.AutoKickDelay > 0 ? $"{afkManager.AutoKickDelay:0}s" : "OFF";
                GUILayout.Label(kickLabel, afkManager.AutoKickDelay > 0 ? _orangeStyle : _labelStyle, GUILayout.Width(40));
                float newKickDelay = GUILayout.HorizontalSlider(afkManager.AutoKickDelay, 0f, 120f, GUILayout.Width(100));
                if (Mathf.Abs(newKickDelay - afkManager.AutoKickDelay) > 1f)
                    afkManager.AutoKickDelay = Mathf.Round(newKickDelay / 5f) * 5f;
                GUILayout.EndHorizontal();

                // Show warnings toggle
                GUILayout.BeginHorizontal();
                GUILayout.Label("Show Warnings:", _labelStyle, GUILayout.Width(90));
                bool newWarnings = GUILayout.Toggle(afkManager.ShowAfkWarnings, afkManager.ShowAfkWarnings ? "ON" : "OFF", _smallButtonStyle, GUILayout.Width(50));
                if (newWarnings != afkManager.ShowAfkWarnings)
                    afkManager.ShowAfkWarnings = newWarnings;
                GUILayout.EndHorizontal();

                // AFK player count
                var afkPlayers = afkManager.GetAfkPlayers();
                if (afkPlayers.Count > 0)
                {
                    GUILayout.Space(4);
                    GUILayout.Label($"AFK Players: {afkPlayers.Count}", _orangeStyle);
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawMapVoteSection()
        {
            var mapVoteManager = EOSMapVoteManager.Instance;
            if (mapVoteManager == null) return;

            GUILayout.Label("MAP/MODE VOTING", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            // Enable toggle
            GUILayout.BeginHorizontal();
            GUILayout.Label("Enabled:", _labelStyle, GUILayout.Width(80));
            bool newEnabled = GUILayout.Toggle(mapVoteManager.Enabled, mapVoteManager.Enabled ? "ON" : "OFF", _smallButtonStyle, GUILayout.Width(50));
            if (newEnabled != mapVoteManager.Enabled)
                mapVoteManager.Enabled = newEnabled;
            GUILayout.EndHorizontal();

            if (mapVoteManager.Enabled)
            {
                // Active vote display
                if (mapVoteManager.IsVoteActive && mapVoteManager.CurrentVote != null)
                {
                    var vote = mapVoteManager.CurrentVote;
                    GUILayout.Space(4);
                    GUILayout.Label($"{vote.Title}", _orangeStyle);
                    GUILayout.Label($"Time: {mapVoteManager.TimeRemaining:0}s | Votes: {vote.TotalVotes}", _labelStyle);

                    // Options with vote buttons
                    var counts = mapVoteManager.GetVoteCounts();
                    int myVote = mapVoteManager.GetMyVote();

                    for (int i = 0; i < vote.Options.Count; i++)
                    {
                        var option = vote.Options[i];
                        bool isMyVote = (i == myVote);

                        GUILayout.BeginHorizontal();

                        // Vote count (if showing live results)
                        if (mapVoteManager.ShowLiveResults)
                        {
                            GUILayout.Label($"[{counts[i]}]", _valueStyle, GUILayout.Width(30));
                        }

                        // Option name
                        GUIStyle optionStyle = isMyVote ? _greenStyle : _labelStyle;
                        GUILayout.Label(option.DisplayName, optionStyle);

                        // Vote button
                        if (!isMyVote || mapVoteManager.AllowVoteChange)
                        {
                            string btnText = isMyVote ? "Voted" : "Vote";
                            if (GUILayout.Button(btnText, _smallButtonStyle, GUILayout.Width(50)))
                            {
                                _ = mapVoteManager.CastVoteAsync(i);
                            }
                        }

                        GUILayout.EndHorizontal();
                    }

                    // Host controls
                    if (_networkManager != null && _networkManager.IsServerStarted)
                    {
                        GUILayout.Space(4);
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("End Now", _smallButtonStyle, GUILayout.Width(70)))
                        {
                            mapVoteManager.EndVoteNow();
                        }
                        if (GUILayout.Button("+15s", _smallButtonStyle, GUILayout.Width(40)))
                        {
                            mapVoteManager.ExtendTimer(15f);
                        }
                        if (GUILayout.Button("Cancel", _smallButtonStyle, GUILayout.Width(60)))
                        {
                            _ = mapVoteManager.CancelVoteAsync();
                        }
                        GUILayout.EndHorizontal();
                    }
                }
                else
                {
                    GUILayout.Label("No active vote", _labelStyle);

                    // Quick start buttons (host only)
                    if (_networkManager != null && _networkManager.IsServerStarted)
                    {
                        GUILayout.Space(4);
                        GUILayout.Label("Quick Start:", _miniLabelStyle);

                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("Map Vote", _smallButtonStyle))
                        {
                            _ = mapVoteManager.StartMapVoteAsync("Vote for Next Map", "Map A", "Map B", "Map C");
                        }
                        if (GUILayout.Button("Mode Vote", _smallButtonStyle))
                        {
                            _ = mapVoteManager.StartModeVoteAsync("Vote for Game Mode", "Deathmatch", "Team DM", "CTF");
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawVoteKickSection()
        {
            var voteKickManager = EOSVoteKickManager.Instance;
            if (voteKickManager == null) return;

            GUILayout.Label("VOTE KICK", _sectionHeaderStyle);
            GUILayout.BeginVertical(_boxStyle);

            // Enable toggle
            GUILayout.BeginHorizontal();
            GUILayout.Label("Enabled:", _labelStyle, GUILayout.Width(80));
            bool newEnabled = GUILayout.Toggle(voteKickManager.Enabled, voteKickManager.Enabled ? "ON" : "OFF", _smallButtonStyle, GUILayout.Width(50));
            if (newEnabled != voteKickManager.Enabled)
                voteKickManager.Enabled = newEnabled;
            GUILayout.EndHorizontal();

            if (voteKickManager.Enabled)
            {
                // Threshold
                GUILayout.BeginHorizontal();
                GUILayout.Label("Threshold:", _labelStyle, GUILayout.Width(80));
                string thresholdText = voteKickManager.Threshold switch
                {
                    EOSVoteKickManager.VoteThreshold.Majority => ">50%",
                    EOSVoteKickManager.VoteThreshold.TwoThirds => "67%",
                    EOSVoteKickManager.VoteThreshold.ThreeQuarters => "75%",
                    EOSVoteKickManager.VoteThreshold.Unanimous => "100%",
                    EOSVoteKickManager.VoteThreshold.Custom => $"{voteKickManager.CustomThresholdPercent}%",
                    _ => "?"
                };
                GUILayout.Label(thresholdText, _valueStyle, GUILayout.Width(40));
                GUILayout.EndHorizontal();

                // Host immunity
                GUILayout.BeginHorizontal();
                GUILayout.Label("Host Immune:", _labelStyle, GUILayout.Width(80));
                GUILayout.Label(voteKickManager.HostImmunity ? "YES" : "NO", voteKickManager.HostImmunity ? _greenStyle : _yellowStyle);
                GUILayout.EndHorizontal();

                // Active vote display
                if (voteKickManager.IsVoteActive)
                {
                    GUILayout.Space(4);
                    var vote = voteKickManager.ActiveVote;
                    GUILayout.Label($"VOTE IN PROGRESS: Kick {vote.TargetName}?", _orangeStyle);
                    GUILayout.Label($"Reason: {vote.Reason}", _miniLabelStyle);

                    int eligible = voteKickManager.GetEligibleVoterCount();
                    int required = voteKickManager.GetRequiredYesVotes();
                    GUILayout.Label($"Votes: {vote.YesVotes} YES / {vote.NoVotes} NO (need {required}/{eligible})", _labelStyle);
                    GUILayout.Label($"Time remaining: {vote.TimeRemaining:0}s", _labelStyle);

                    // Vote buttons (if we haven't voted and we're not the target)
                    if (!voteKickManager.HasVoted() && _transport?.LocalProductUserId != vote.TargetPuid)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("Vote YES (Kick)", _smallButtonStyle))
                        {
                            _ = voteKickManager.CastVoteAsync(true);
                        }
                        if (GUILayout.Button("Vote NO (Keep)", _smallButtonStyle))
                        {
                            _ = voteKickManager.CastVoteAsync(false);
                        }
                        GUILayout.EndHorizontal();
                    }
                    else if (voteKickManager.HasVoted())
                    {
                        bool? myVote = voteKickManager.GetVote(_transport?.LocalProductUserId);
                        GUILayout.Label($"You voted: {(myVote == true ? "YES" : "NO")}", _greenStyle);
                    }

                    // Host veto button
                    if (voteKickManager.HostCanVeto && _networkManager != null && _networkManager.IsServerStarted)
                    {
                        if (GUILayout.Button("VETO (Host)", _smallButtonStyle))
                        {
                            _ = voteKickManager.VetoVoteAsync();
                        }
                    }

                    // Initiator cancel button
                    if (vote.InitiatorPuid == _transport?.LocalProductUserId)
                    {
                        if (GUILayout.Button("Cancel Vote", _smallButtonStyle))
                        {
                            _ = voteKickManager.CancelVoteAsync();
                        }
                    }
                }
                else
                {
                    // Cooldown display
                    float cooldown = voteKickManager.GetCooldownRemaining();
                    if (cooldown > 0)
                    {
                        GUILayout.Label($"Cooldown: {cooldown:0}s", _yellowStyle);
                    }
                    else
                    {
                        GUILayout.Label("No active vote", _labelStyle);
                    }
                }
            }

            GUILayout.EndVertical();
        }

        #endregion
    }

    /// <summary>
    /// Connection quality rating based on ping and jitter.
    /// </summary>
    public enum ConnectionQuality
    {
        Unknown,
        Excellent,  // <50ms ping, <10 jitter
        Good,       // <100ms ping, <25 jitter
        Fair,       // <150ms ping, <50 jitter
        Poor,       // <250ms ping
        Bad         // 250ms+ ping
    }
}
