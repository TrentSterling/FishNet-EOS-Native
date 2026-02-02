using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Metrics;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// EOS Metrics for player session telemetry.
    /// Tracks when players start/end game sessions for Developer Portal analytics.
    /// Auto-tracks FishNet connections when enabled.
    /// Works with DeviceID auth.
    /// </summary>
    public class EOSMetrics : MonoBehaviour
    {
        #region Singleton

        private static EOSMetrics _instance;
        public static EOSMetrics Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSMetrics>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSMetrics");
                        _instance = go.AddComponent<EOSMetrics>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Serialized Fields

        [SerializeField] private bool _autoTrackSessions = true;
        [SerializeField] private UserControllerType _controllerType = UserControllerType.Unknown;

        #endregion

        #region Events

        public event Action OnSessionStarted;
        public event Action OnSessionEnded;

        #endregion

        #region Private Fields

        private MetricsInterface _metricsInterface;
        private ProductUserId _localUserId;
        private bool _sessionActive;
        private string _currentSessionId;
        private DateTime _sessionStartTime;

        #endregion

        #region Public Properties

        public bool IsReady
        {
            get
            {
                if (_metricsInterface == null || _localUserId == null)
                    return false;
                try { return _localUserId.IsValid(); }
                catch { return false; }
            }
        }
        public bool IsSessionActive => _sessionActive;
        public string CurrentSessionId => _currentSessionId;
        public TimeSpan SessionDuration => _sessionActive ? DateTime.Now - _sessionStartTime : TimeSpan.Zero;
        public bool AutoTrackSessions { get => _autoTrackSessions; set => _autoTrackSessions = value; }
        public UserControllerType ControllerType { get => _controllerType; set => _controllerType = value; }

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

        private System.Collections.IEnumerator InitializeCoroutine()
        {
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _metricsInterface = EOSManager.Instance.Platform?.GetMetricsInterface();
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_metricsInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.Metrics, "EOSMetrics", "Initialized");

                // Subscribe to FishNet connection events if auto-tracking
                if (_autoTrackSessions)
                {
                    SubscribeToFishNet();
                }
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Metrics, "EOSMetrics", "MetricsInterface not available");
            }
        }

        private void OnDestroy()
        {
            // End session on destroy
            if (_sessionActive)
            {
                EndSession();
            }

            UnsubscribeFromFishNet();

            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit()
        {
            if (_sessionActive)
            {
                EndSession();
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Begin a new player session.
        /// </summary>
        /// <param name="displayName">Display name for metrics (uses generated name if null).</param>
        /// <param name="serverIp">Server IP address (null for local/host).</param>
        /// <param name="sessionId">Custom session identifier (auto-generated if null).</param>
        public Result BeginSession(string displayName = null, string serverIp = null, string sessionId = null)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (_sessionActive)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Metrics, "EOSMetrics", "Session already active, ending previous session");
                EndSession();
            }

            // Generate session ID if not provided
            sessionId ??= Guid.NewGuid().ToString("N").Substring(0, 16);

            // Get display name from lobby chat manager if not provided
            if (string.IsNullOrEmpty(displayName))
            {
                var chatManager = FindAnyObjectByType<Lobbies.EOSLobbyChatManager>();
                displayName = chatManager?.GetOrGenerateDisplayName(_localUserId.ToString()) ?? "Player";
            }

            var accountId = new BeginPlayerSessionOptionsAccountId
            {
                External = _localUserId.ToString()
            };

            var options = new BeginPlayerSessionOptions
            {
                AccountId = accountId,
                DisplayName = displayName,
                ControllerType = _controllerType,
                ServerIp = serverIp,
                GameSessionId = sessionId
            };

            var result = _metricsInterface.BeginPlayerSession(ref options);
            if (result == Result.Success)
            {
                _sessionActive = true;
                _currentSessionId = sessionId;
                _sessionStartTime = DateTime.Now;
                EOSDebugLogger.Log(DebugCategory.Metrics, "EOSMetrics", $" Session started: {sessionId}");
                OnSessionStarted?.Invoke();
            }
            else
            {
                Debug.LogWarning($"[EOSMetrics] BeginSession failed: {result}");
            }

            return result;
        }

        /// <summary>
        /// End the current player session.
        /// </summary>
        public Result EndSession()
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (!_sessionActive)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Metrics, "EOSMetrics", "No active session to end");
                return Result.InvalidState;
            }

            var accountId = new EndPlayerSessionOptionsAccountId
            {
                External = _localUserId.ToString()
            };

            var options = new EndPlayerSessionOptions
            {
                AccountId = accountId
            };

            var result = _metricsInterface.EndPlayerSession(ref options);
            if (result == Result.Success)
            {
                var duration = DateTime.Now - _sessionStartTime;
                EOSDebugLogger.Log(DebugCategory.Metrics, "EOSMetrics", $" Session ended: {_currentSessionId} (duration: {duration:hh\\:mm\\:ss})");
                _sessionActive = false;
                _currentSessionId = null;
                OnSessionEnded?.Invoke();
            }
            else
            {
                Debug.LogWarning($"[EOSMetrics] EndSession failed: {result}");
            }

            return result;
        }

        /// <summary>
        /// Set controller type for future sessions.
        /// </summary>
        public void SetControllerType(UserControllerType type)
        {
            _controllerType = type;
        }

        #endregion

        #region FishNet Integration

        private EOSNativeTransport _transport;

        private void SubscribeToFishNet()
        {
            _transport = FindAnyObjectByType<EOSNativeTransport>();
            if (_transport == null) return;

            // Get the NetworkManager to subscribe to events
            var networkManager = _transport.GetComponent<FishNet.Managing.NetworkManager>();
            if (networkManager != null)
            {
                networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
                networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            }
        }

        private void UnsubscribeFromFishNet()
        {
            if (_transport == null) return;

            var networkManager = _transport.GetComponent<FishNet.Managing.NetworkManager>();
            if (networkManager != null)
            {
                networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            }
        }

        private void OnClientConnectionState(FishNet.Transporting.ClientConnectionStateArgs args)
        {
            if (!_autoTrackSessions) return;

            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {
                // Client connected - start session (skip if already active from server start on same host)
                if (!_sessionActive)
                    BeginSession(serverIp: null);
            }
            else if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                // Client disconnected - end session
                if (_sessionActive)
                {
                    EndSession();
                }
            }
        }

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            if (!_autoTrackSessions) return;

            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {
                // Server started - begin session as host
                BeginSession(serverIp: null); // Local/host session
            }
            else if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                // Server stopped - end session
                if (_sessionActive)
                {
                    EndSession();
                }
            }
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSMetrics))]
    public class EOSMetricsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var metrics = (EOSMetrics)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("EOS Metrics", EditorStyles.boldLabel);

            DrawDefaultInspector();

            EditorGUILayout.Space(5);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", metrics.IsReady);
                EditorGUILayout.Toggle("Session Active", metrics.IsSessionActive);

                if (metrics.IsSessionActive)
                {
                    EditorGUILayout.TextField("Session ID", metrics.CurrentSessionId);
                    EditorGUILayout.TextField("Duration", metrics.SessionDuration.ToString(@"hh\:mm\:ss"));
                }
            }

            if (Application.isPlaying && metrics.IsReady)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Manual Control", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !metrics.IsSessionActive;
                if (GUILayout.Button("Begin Session"))
                {
                    metrics.BeginSession();
                }
                GUI.enabled = metrics.IsSessionActive;
                if (GUILayout.Button("End Session"))
                {
                    metrics.EndSession();
                }
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to track metrics.", MessageType.Info);
            }
        }
    }
#endif
}
