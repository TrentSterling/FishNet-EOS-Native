using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Social;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Captures game frames during matches for replay recording.
    /// Automatically integrates with EOSMatchHistory for auto-recording.
    /// </summary>
    public class EOSReplayRecorder : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayRecorder _instance;
        public static EOSReplayRecorder Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSReplayRecorder>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSReplayRecorder");
                        _instance = go.AddComponent<EOSReplayRecorder>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when recording starts.</summary>
        public event Action<string> OnRecordingStarted; // matchId

        /// <summary>Fired when recording stops.</summary>
        public event Action<ReplayFile> OnRecordingStopped;

        /// <summary>Fired on each frame recorded (for progress tracking).</summary>
        public event Action<int, float> OnFrameRecorded; // frameCount, timestamp

        /// <summary>Fired when connection quality degrades during recording.</summary>
        public event Action<RecordingQualityWarning> OnQualityWarning;

        /// <summary>Fired when recording is approaching duration limit.</summary>
        public event Action<float, float> OnDurationWarning; // currentDuration, maxDuration

        /// <summary>Fired when recording was auto-stopped due to limit.</summary>
        public event Action<string> OnAutoStopped; // reason

        #endregion

        #region Serialized Fields

        [Header("Settings")]
        [Tooltip("Use settings from EOSReplaySettings ScriptableObject. If true, ignores local overrides below.")]
        [SerializeField] private bool _useGlobalSettings = true;

        [Header("Local Overrides (when Use Global Settings is false)")]
        [Tooltip("Frames per second to record.")]
        [SerializeField] private float _frameRate = 20f;

        [Tooltip("Seconds between full keyframes (for seeking).")]
        [SerializeField] private float _keyframeInterval = 5f;

        [Tooltip("Automatically start recording when a match starts.")]
        [SerializeField] private bool _autoRecord = true;

        [Tooltip("Record all NetworkObjects, not just those with ReplayRecordable.")]
        [SerializeField] private bool _recordAllNetworkObjects = true;

        [Header("Compression")]
        [Tooltip("Minimum position change to record (units).")]
        [SerializeField] private float _positionThreshold = 0.001f;

        [Tooltip("Minimum rotation change to record (degrees).")]
        [SerializeField] private float _rotationThreshold = 0.1f;

        #endregion

        #region Settings Access

        private EOSReplaySettings GlobalSettings => EOSReplaySettings.Instance;
        private float EffectiveFrameRate => _useGlobalSettings ? GlobalSettings.FrameRate : _frameRate;
        private float EffectiveKeyframeInterval => _useGlobalSettings ? GlobalSettings.KeyframeInterval : _keyframeInterval;
        private bool EffectiveAutoRecord => _useGlobalSettings ? GlobalSettings.AutoRecord : _autoRecord;
        private bool EffectiveRecordAllNetworkObjects => _useGlobalSettings ? GlobalSettings.RecordAllNetworkObjects : _recordAllNetworkObjects;

        #endregion

        #region Public Properties

        /// <summary>Whether recording is currently active.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>Whether auto-record is enabled.</summary>
        public bool AutoRecordEnabled => EffectiveAutoRecord;

        /// <summary>Current match ID being recorded.</summary>
        public string CurrentMatchId { get; private set; }

        /// <summary>Number of frames recorded so far.</summary>
        public int FrameCount => _frames.Count;

        /// <summary>Current recording duration in seconds.</summary>
        public float Duration => IsRecording ? Time.time - _recordingStartTime : 0f;

        /// <summary>Maximum recording duration from settings (seconds).</summary>
        public float MaxDuration => GlobalSettings.MaxDurationSeconds;

        /// <summary>Estimated current file size in KB (rough approximation).</summary>
        public float EstimatedSizeKB
        {
            get
            {
                if (_frames.Count == 0) return 0f;
                // Rough estimate: ~50 bytes per object per frame (with compression)
                // This is conservative - actual size varies based on delta compression
                float avgObjectsPerFrame = 20f; // Rough estimate
                float bytesPerObject = 50f;
                return (_frames.Count * avgObjectsPerFrame * bytesPerObject) / 1024f;
            }
        }

        /// <summary>Whether recording has exceeded the warning threshold.</summary>
        public bool IsApproachingLimit => Duration >= GlobalSettings.DurationWarningSeconds;

        /// <summary>Frame rate setting.</summary>
        public float FrameRate
        {
            get => _frameRate;
            set => _frameRate = Mathf.Max(1f, value);
        }

        /// <summary>Auto-record setting.</summary>
        public bool AutoRecord
        {
            get => _autoRecord;
            set => _autoRecord = value;
        }

        #endregion

        #region Private Fields

        private List<ReplayFrame> _frames = new();
        private List<ReplayEvent> _pendingEvents = new();
        private List<ReplayParticipant> _participants = new();
        private float _recordingStartTime;
        private float _lastFrameTime;
        private float _lastKeyframeTime;
        private NetworkManager _networkManager;

        // Track last recorded state for delta compression
        private Dictionary<int, (Vector3 pos, Quaternion rot)> _lastRecordedState = new();

        // Recording metadata
        private string _lobbyCode;
        private string _gameMode;
        private string _mapName;

        // Quality monitoring
        private float _lastQualityCheckTime;
        private const float QUALITY_CHECK_INTERVAL = 2f;
        private RecordingQualityWarning _lastWarning = RecordingQualityWarning.None;
        private int _warningCount;
        private int _poorQualityFrames;

        // Duration monitoring
        private bool _durationWarningFired;
        private float _lastDurationCheckTime;
        private const float DURATION_CHECK_INTERVAL = 10f;

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
            _networkManager = InstanceFinder.NetworkManager;

            // Subscribe to spawn/despawn events
            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            }
        }

        private void Update()
        {
            if (!IsRecording) return;

            float currentTime = Time.time - _recordingStartTime;
            float frameInterval = 1f / EffectiveFrameRate;

            // Check if it's time for a new frame
            if (currentTime - _lastFrameTime >= frameInterval)
            {
                bool isKeyframe = (currentTime - _lastKeyframeTime) >= EffectiveKeyframeInterval;
                RecordFrame(currentTime, isKeyframe);

                _lastFrameTime = currentTime;
                if (isKeyframe)
                {
                    _lastKeyframeTime = currentTime;
                }
            }

            // Periodic quality check
            if (Time.time - _lastQualityCheckTime >= QUALITY_CHECK_INTERVAL)
            {
                CheckConnectionQuality();
                _lastQualityCheckTime = Time.time;
            }

            // Periodic duration check
            if (Time.time - _lastDurationCheckTime >= DURATION_CHECK_INTERVAL)
            {
                CheckDurationLimits(currentTime);
                _lastDurationCheckTime = Time.time;
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start recording a new replay.
        /// </summary>
        /// <param name="matchId">Match ID to associate with the replay.</param>
        public void StartRecording(string matchId)
        {
            if (IsRecording)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayRecorder", "Already recording, stopping previous recording");
                _ = StopAndSaveAsync();
            }

            CurrentMatchId = matchId;
            _frames.Clear();
            _pendingEvents.Clear();
            _participants.Clear();
            _lastRecordedState.Clear();
            _durationWarningFired = false;
            _lastDurationCheckTime = 0f;

            // Capture metadata from lobby
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                var lobby = lobbyManager.CurrentLobby;
                _lobbyCode = lobby.JoinCode;
                _gameMode = lobby.GameMode;
                _mapName = lobby.Map;
            }
            else
            {
                _lobbyCode = "Unknown";
                _gameMode = "Unknown";
                _mapName = "Unknown";
            }

            // Reset recordable states
            foreach (var recordable in FindObjectsByType<ReplayRecordable>(FindObjectsSortMode.None))
            {
                recordable.ResetRecordedState();
            }

            _recordingStartTime = Time.time;
            _lastFrameTime = 0f;
            _lastKeyframeTime = 0f;
            IsRecording = true;

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayRecorder", $"Recording started for match {matchId}");
            OnRecordingStarted?.Invoke(matchId);
        }

        /// <summary>
        /// Stop recording and save the replay.
        /// </summary>
        public async Task<ReplayFile> StopAndSaveAsync()
        {
            if (!IsRecording)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayRecorder", "Not recording");
                return default;
            }

            IsRecording = false;
            float duration = Time.time - _recordingStartTime;

            // Build participants list from registry
            BuildParticipantsList();

            // Create replay file
            var replay = new ReplayFile
            {
                Version = ReplayMigration.CURRENT_VERSION,
                Header = new ReplayHeader
                {
                    ReplayId = Guid.NewGuid().ToString("N").Substring(0, 12),
                    MatchId = CurrentMatchId,
                    RecordedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Duration = duration,
                    FrameCount = _frames.Count,
                    FrameRate = EffectiveFrameRate,
                    LobbyCode = _lobbyCode,
                    GameMode = _gameMode,
                    MapName = _mapName,
                    Participants = _participants.ToArray()
                },
                CompressedFrames = ReplayCompression.Compress(ReplayCompression.SerializeFrames(_frames))
            };

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayRecorder",
                $"Recording stopped: {_frames.Count} frames, {duration:F1}s, {replay.CompressedFrames.Length / 1024f:F1} KB");

            // Save locally
            var storage = EOSReplayStorage.Instance;
            if (storage != null)
            {
                await storage.SaveLocalAsync(replay);
            }

            OnRecordingStopped?.Invoke(replay);
            CurrentMatchId = null;

            return replay;
        }

        /// <summary>
        /// Cancel recording without saving.
        /// </summary>
        public void CancelRecording()
        {
            if (!IsRecording) return;

            IsRecording = false;
            _frames.Clear();
            _pendingEvents.Clear();
            CurrentMatchId = null;

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayRecorder", "Recording cancelled");
        }

        /// <summary>
        /// Record a custom game event.
        /// </summary>
        public void RecordEvent(string eventName, string data = null)
        {
            if (!IsRecording) return;

            _pendingEvents.Add(new ReplayEvent
            {
                Timestamp = Time.time - _recordingStartTime,
                Type = ReplayEventType.GameEvent,
                Data = $"{{\"event\":\"{eventName}\",\"data\":{data ?? "null"}}}"
            });
        }

        /// <summary>
        /// Add a participant to the recording.
        /// </summary>
        public void AddParticipant(string puid, string displayName, int team = 0, string platform = null)
        {
            if (!IsRecording) return;

            // Check for duplicate
            foreach (var p in _participants)
            {
                if (p.Puid == puid) return;
            }

            _participants.Add(new ReplayParticipant
            {
                Puid = puid,
                DisplayName = displayName,
                Team = team,
                Platform = platform ?? EOSPlatformHelper.PlatformId
            });

            // Record player joined event
            _pendingEvents.Add(new ReplayEvent
            {
                Timestamp = Time.time - _recordingStartTime,
                Type = ReplayEventType.PlayerJoined,
                OwnerPuid = puid,
                Data = displayName
            });
        }

        #endregion

        #region Private Methods

        private void RecordFrame(float timestamp, bool isKeyframe)
        {
            var snapshots = new List<ReplayObjectSnapshot>();
            var spawned = GetSpawnedObjects();

            foreach (var nob in spawned)
            {
                if (nob == null) continue;

                // Check if object should be recorded
                var recordable = nob.GetComponent<ReplayRecordable>();
                if (recordable != null && !recordable.RecordEnabled) continue;
                if (!EffectiveRecordAllNetworkObjects && recordable == null) continue;

                Vector3 pos = nob.transform.position;
                Quaternion rot = nob.transform.rotation;

                // For delta frames, only record changed objects
                if (!isKeyframe)
                {
                    bool hasChanged = false;

                    if (recordable != null)
                    {
                        hasChanged = recordable.HasChangedSinceLastRecord(pos, rot);
                    }
                    else if (_lastRecordedState.TryGetValue(nob.ObjectId, out var lastState))
                    {
                        bool posChanged = Vector3.SqrMagnitude(pos - lastState.pos) > _positionThreshold * _positionThreshold;
                        bool rotChanged = Quaternion.Angle(rot, lastState.rot) > _rotationThreshold;
                        hasChanged = posChanged || rotChanged;
                    }
                    else
                    {
                        hasChanged = true; // New object
                    }

                    if (!hasChanged) continue;
                }

                // Record snapshot
                snapshots.Add(new ReplayObjectSnapshot
                {
                    ObjectId = nob.ObjectId,
                    Position = new Vector3Half(pos),
                    CompressedRotation = ReplayCompression.CompressRotation(rot),
                    Flags = nob.gameObject.activeInHierarchy ? ReplayObjectSnapshot.FLAG_ACTIVE : (byte)0
                });

                // Update tracked state
                if (recordable != null)
                {
                    recordable.MarkRecorded(pos, rot);
                }
                _lastRecordedState[nob.ObjectId] = (pos, rot);
            }

            // Create frame
            var frame = new ReplayFrame
            {
                Timestamp = timestamp,
                IsKeyframe = isKeyframe,
                Objects = snapshots.ToArray(),
                Events = _pendingEvents.Count > 0 ? _pendingEvents.ToArray() : null
            };

            _frames.Add(frame);
            _pendingEvents.Clear();

            OnFrameRecorded?.Invoke(_frames.Count, timestamp);

            if (isKeyframe)
            {
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayRecorder",
                    $"Keyframe recorded: {snapshots.Count} objects at {timestamp:F1}s");
            }
        }

        private IEnumerable<NetworkObject> GetSpawnedObjects()
        {
            if (_networkManager == null) yield break;

            // Prefer server objects if available
            if (_networkManager.IsServerStarted)
            {
                foreach (var nob in _networkManager.ServerManager.Objects.Spawned.Values)
                {
                    yield return nob;
                }
            }
            else if (_networkManager.IsClientStarted)
            {
                foreach (var nob in _networkManager.ClientManager.Objects.Spawned.Values)
                {
                    yield return nob;
                }
            }
        }

        private void BuildParticipantsList()
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            // Add known players from registry that aren't already in the list
            foreach (var player in registry.GetAllPlayers())
            {
                bool exists = false;
                foreach (var p in _participants)
                {
                    if (p.Puid == player.puid)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    _participants.Add(new ReplayParticipant
                    {
                        Puid = player.puid,
                        DisplayName = player.name,
                        Team = 0,
                        Platform = registry.GetPlatform(player.puid)
                    });
                }
            }
        }

        private void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            // Record connection/disconnection events
            if (!IsRecording) return;

            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                // Server stopped, stop recording
                if (IsRecording)
                {
                    _ = StopAndSaveAsync();
                }
            }
        }

        private void CheckConnectionQuality()
        {
            if (_networkManager == null) return;

            // Get current RTT from FishNet
            long rtt = _networkManager.TimeManager.RoundTripTime;
            RecordingQualityWarning currentWarning = RecordingQualityWarning.None;

            // Check for high ping
            if (rtt >= 250)
            {
                currentWarning = RecordingQualityWarning.VeryHighPing;
                _poorQualityFrames++;
            }
            else if (rtt >= 150)
            {
                currentWarning = RecordingQualityWarning.HighPing;
            }

            // Check actual frame rate vs target
            if (_frames.Count > 10)
            {
                float elapsed = Time.time - _recordingStartTime;
                float actualFps = _frames.Count / elapsed;
                float targetFps = EffectiveFrameRate;

                // If we're getting less than 50% of target frame rate
                if (actualFps < targetFps * 0.5f)
                {
                    currentWarning = RecordingQualityWarning.LowFrameRate;
                    _poorQualityFrames++;
                }
            }

            // Only fire event if warning changed (avoid spam)
            if (currentWarning != _lastWarning && currentWarning != RecordingQualityWarning.None)
            {
                _warningCount++;
                _lastWarning = currentWarning;

                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayRecorder",
                    $"Recording quality warning: {currentWarning} (RTT: {rtt}ms)");

                OnQualityWarning?.Invoke(currentWarning);
            }
            else if (currentWarning == RecordingQualityWarning.None && _lastWarning != RecordingQualityWarning.None)
            {
                // Quality recovered
                _lastWarning = RecordingQualityWarning.None;
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayRecorder", "Recording quality recovered");
            }
        }

        private void CheckDurationLimits(float currentDuration)
        {
            var settings = GlobalSettings;
            float maxDuration = settings.MaxDurationSeconds;
            float warningDuration = settings.DurationWarningSeconds;

            // Check if approaching limit (fire warning once)
            if (!_durationWarningFired && currentDuration >= warningDuration)
            {
                _durationWarningFired = true;
                float remaining = maxDuration - currentDuration;

                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayRecorder",
                    $"Recording approaching limit: {remaining / 60f:F1} minutes remaining");

                OnDurationWarning?.Invoke(currentDuration, maxDuration);
            }

            // Check if hit max limit
            if (currentDuration >= maxDuration && settings.AutoStopAtLimit)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayRecorder",
                    $"Recording auto-stopped: max duration ({settings.MaxDurationMinutes} min) reached");

                OnAutoStopped?.Invoke($"Max duration ({settings.MaxDurationMinutes} min) reached");
                _ = StopAndSaveAsync();
            }
        }

        #endregion
    }
}
