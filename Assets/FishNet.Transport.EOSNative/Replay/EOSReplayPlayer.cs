using System;
using System.Collections.Generic;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Playback state for replay player.
    /// </summary>
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    /// <summary>
    /// Plays back recorded replays with interpolation and seeking support.
    /// </summary>
    public class EOSReplayPlayer : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayPlayer _instance;
        public static EOSReplayPlayer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSReplayPlayer>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSReplayPlayer");
                        _instance = go.AddComponent<EOSReplayPlayer>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when playback time changes.</summary>
        public event Action<float> OnTimeChanged;

        /// <summary>Fired when playback state changes.</summary>
        public event Action<PlaybackState> OnStateChanged;

        /// <summary>Fired when replay ends.</summary>
        public event Action OnReplayEnded;

        /// <summary>Fired when an object is spawned during playback.</summary>
        public event Action<int, GameObject> OnObjectSpawned; // objectId, gameObject

        /// <summary>Fired when an object is despawned during playback.</summary>
        public event Action<int> OnObjectDespawned; // objectId

        #endregion

        #region Serialized Fields

        [Header("Settings")]
        [Tooltip("Override settings. If null, uses EOSReplaySettings from Resources.")]
        [SerializeField] private EOSReplaySettings _settingsOverride;

        #endregion

        #region Settings Access

        private EOSReplaySettings Settings => _settingsOverride != null ? _settingsOverride : EOSReplaySettings.Instance;
        private float PositionSmoothing => Settings.PositionSmoothing;
        private float RotationSmoothing => Settings.RotationSmoothing;

        #endregion

        #region Public Properties

        /// <summary>Current playback state.</summary>
        public PlaybackState State { get; private set; } = PlaybackState.Stopped;

        /// <summary>Current playback time in seconds.</summary>
        public float CurrentTime { get; private set; }

        /// <summary>Total duration of loaded replay.</summary>
        public float Duration => _loadedReplay.HasValue ? _loadedReplay.Value.Header.Duration : 0f;

        /// <summary>Playback speed multiplier.</summary>
        public float PlaybackSpeed
        {
            get => _playbackSpeed;
            set => _playbackSpeed = Mathf.Clamp(value, 0.1f, 4f);
        }

        /// <summary>Whether a replay is loaded.</summary>
        public bool IsLoaded => _loadedReplay.HasValue;

        /// <summary>Loaded replay header.</summary>
        public ReplayHeader? Header => _loadedReplay?.Header;

        /// <summary>Normalized progress (0-1).</summary>
        public float Progress => Duration > 0 ? CurrentTime / Duration : 0f;

        /// <summary>Number of loaded frames.</summary>
        public int LoadedFrameCount => _frames.Count;

        #endregion

        #region Timeline Markers

        /// <summary>
        /// Get normalized positions (0-1) of all keyframes for timeline display.
        /// </summary>
        public List<float> GetKeyframeMarkers()
        {
            var markers = new List<float>();
            if (Duration <= 0) return markers;

            foreach (var frame in _frames)
            {
                if (frame.IsKeyframe)
                {
                    markers.Add(frame.Timestamp / Duration);
                }
            }
            return markers;
        }

        /// <summary>
        /// Get timeline markers for events (spawns, game events, etc).
        /// Returns list of (normalizedTime, eventType, description).
        /// </summary>
        public List<(float time, ReplayEventType type, string desc)> GetEventMarkers()
        {
            var markers = new List<(float, ReplayEventType, string)>();
            if (Duration <= 0) return markers;

            foreach (var frame in _frames)
            {
                if (frame.Events == null) continue;

                foreach (var evt in frame.Events)
                {
                    string desc = evt.Type switch
                    {
                        ReplayEventType.PlayerJoined => $"+ {evt.Data}",
                        ReplayEventType.PlayerLeft => $"- {evt.OwnerPuid}",
                        ReplayEventType.GameEvent => evt.Data ?? "Event",
                        _ => evt.Type.ToString()
                    };
                    markers.Add((evt.Timestamp / Duration, evt.Type, desc));
                }
            }
            return markers;
        }

        #endregion

        #region Private Fields

        private ReplayFile? _loadedReplay;
        private List<ReplayFrame> _frames = new();
        private float _playbackSpeed = 1f;
        private int _currentFrameIndex;
        private float _lastEventTime;

        // Spawned replay objects
        private Dictionary<int, GameObject> _spawnedObjects = new();
        private Dictionary<int, ReplayObjectState> _objectStates = new();
        private Transform _replayContainer;

        // Interpolation targets
        private class ReplayObjectState
        {
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
            public Vector3 CurrentPosition;
            public Quaternion CurrentRotation;
            public bool IsActive;
        }

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
            // Create container for replay objects
            _replayContainer = new GameObject("ReplayObjects").transform;
            _replayContainer.SetParent(transform);
        }

        private void Update()
        {
            if (State != PlaybackState.Playing) return;

            // Advance time
            CurrentTime += Time.deltaTime * _playbackSpeed;
            OnTimeChanged?.Invoke(CurrentTime);

            // Update frame
            UpdatePlayback();

            // Check for end
            if (CurrentTime >= Duration)
            {
                Stop();
                OnReplayEnded?.Invoke();
            }
        }

        private void LateUpdate()
        {
            if (State == PlaybackState.Stopped) return;

            // Interpolate object positions
            InterpolateObjects();
        }

        private void OnDestroy()
        {
            ClearSpawnedObjects();

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Load a replay for playback.
        /// </summary>
        public void LoadReplay(ReplayFile replay)
        {
            if (State != PlaybackState.Stopped)
            {
                Stop();
            }

            _loadedReplay = replay;

            // Decompress frames
            try
            {
                byte[] decompressed = ReplayCompression.Decompress(replay.CompressedFrames);
                _frames = ReplayCompression.DeserializeFrames(decompressed);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayPlayer] Failed to decompress frames: {e.Message}");
                _frames = new List<ReplayFrame>();
            }

            CurrentTime = 0f;
            _currentFrameIndex = 0;
            _lastEventTime = 0f;

            // Reset color palette for new replay
            ReplayGhost.ResetColorIndex();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer",
                $"Loaded replay: {replay.Header.ReplayId}, {_frames.Count} frames, {replay.Header.Duration:F1}s");
        }

        /// <summary>
        /// Start or resume playback.
        /// </summary>
        public void Play()
        {
            if (!IsLoaded) return;

            if (State == PlaybackState.Stopped)
            {
                // Starting fresh - spawn initial objects
                SpawnInitialObjects();
            }

            State = PlaybackState.Playing;
            OnStateChanged?.Invoke(State);

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer", "Playback started");
        }

        /// <summary>
        /// Pause playback.
        /// </summary>
        public void Pause()
        {
            if (State != PlaybackState.Playing) return;

            State = PlaybackState.Paused;
            OnStateChanged?.Invoke(State);

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer", "Playback paused");
        }

        /// <summary>
        /// Stop playback and reset.
        /// </summary>
        public void Stop()
        {
            if (State == PlaybackState.Stopped) return;

            State = PlaybackState.Stopped;
            CurrentTime = 0f;
            _currentFrameIndex = 0;
            _lastEventTime = 0f;

            ClearSpawnedObjects();
            OnStateChanged?.Invoke(State);

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer", "Playback stopped");
        }

        /// <summary>
        /// Seek to a specific time.
        /// </summary>
        public void Seek(float time)
        {
            if (!IsLoaded) return;

            time = Mathf.Clamp(time, 0f, Duration);

            // Find nearest keyframe before target time
            int keyframeIndex = FindNearestKeyframe(time);

            // Reset state and apply from keyframe
            ClearSpawnedObjects();
            _lastEventTime = 0f;

            if (keyframeIndex >= 0)
            {
                // Apply keyframe state
                ApplyFrame(_frames[keyframeIndex], true);
                _currentFrameIndex = keyframeIndex;
                _lastEventTime = _frames[keyframeIndex].Timestamp;

                // Process events up to target time
                for (int i = keyframeIndex + 1; i < _frames.Count; i++)
                {
                    var frame = _frames[i];
                    if (frame.Timestamp > time) break;

                    ProcessEvents(frame);
                    _currentFrameIndex = i;
                }
            }

            CurrentTime = time;
            OnTimeChanged?.Invoke(CurrentTime);

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer", $"Seeked to {time:F1}s");
        }

        /// <summary>
        /// Set playback speed.
        /// </summary>
        public void SetSpeed(float speed)
        {
            PlaybackSpeed = speed;
        }

        /// <summary>
        /// Get list of player objects (for spectator camera targeting).
        /// </summary>
        public List<GameObject> GetPlayerObjects()
        {
            var players = new List<GameObject>();

            foreach (var obj in _spawnedObjects.Values)
            {
                if (obj != null && obj.activeInHierarchy)
                {
                    // For now, return all objects - could filter by player tag
                    players.Add(obj);
                }
            }

            return players;
        }

        /// <summary>
        /// Unload the current replay.
        /// </summary>
        public void Unload()
        {
            Stop();
            _loadedReplay = null;
            _frames.Clear();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer", "Replay unloaded");
        }

        #endregion

        #region Private Methods

        private void UpdatePlayback()
        {
            // Find frames surrounding current time
            while (_currentFrameIndex < _frames.Count - 1 &&
                   _frames[_currentFrameIndex + 1].Timestamp <= CurrentTime)
            {
                _currentFrameIndex++;
                ApplyFrame(_frames[_currentFrameIndex], false);
            }

            // Interpolate between frames
            if (_currentFrameIndex < _frames.Count - 1)
            {
                var prevFrame = _frames[_currentFrameIndex];
                var nextFrame = _frames[_currentFrameIndex + 1];
                float t = Mathf.InverseLerp(prevFrame.Timestamp, nextFrame.Timestamp, CurrentTime);

                InterpolateFrame(prevFrame, nextFrame, t);
            }
        }

        private void SpawnInitialObjects()
        {
            if (_frames.Count == 0) return;

            // Find first keyframe
            var firstKeyframe = _frames.Find(f => f.IsKeyframe);
            if (firstKeyframe.Objects != null)
            {
                ApplyFrame(firstKeyframe, true);
            }
        }

        private void ApplyFrame(ReplayFrame frame, bool isFullState)
        {
            // Process events
            ProcessEvents(frame);

            // Apply object states
            if (frame.Objects == null) return;

            foreach (var snapshot in frame.Objects)
            {
                if (!_spawnedObjects.TryGetValue(snapshot.ObjectId, out var obj))
                {
                    // Spawn new object
                    obj = SpawnReplayObject(snapshot.ObjectId);
                }

                if (obj == null) continue;

                Vector3 pos = snapshot.Position.ToVector3();
                Quaternion rot = ReplayCompression.DecompressRotation(snapshot.CompressedRotation);
                bool active = (snapshot.Flags & ReplayObjectSnapshot.FLAG_ACTIVE) != 0;

                if (!_objectStates.TryGetValue(snapshot.ObjectId, out var state))
                {
                    state = new ReplayObjectState();
                    _objectStates[snapshot.ObjectId] = state;
                }

                state.TargetPosition = pos;
                state.TargetRotation = rot;
                state.IsActive = active;

                if (isFullState)
                {
                    // Snap to position
                    state.CurrentPosition = pos;
                    state.CurrentRotation = rot;
                    obj.transform.position = pos;
                    obj.transform.rotation = rot;
                }

                obj.SetActive(active);
            }
        }

        private void InterpolateFrame(ReplayFrame prevFrame, ReplayFrame nextFrame, float t)
        {
            if (nextFrame.Objects == null) return;

            foreach (var snapshot in nextFrame.Objects)
            {
                if (!_objectStates.TryGetValue(snapshot.ObjectId, out var state)) continue;

                Vector3 nextPos = snapshot.Position.ToVector3();
                Quaternion nextRot = ReplayCompression.DecompressRotation(snapshot.CompressedRotation);

                // Update targets
                state.TargetPosition = Vector3.Lerp(state.CurrentPosition, nextPos, t);
                state.TargetRotation = Quaternion.Slerp(state.CurrentRotation, nextRot, t);
            }
        }

        private void InterpolateObjects()
        {
            foreach (var kvp in _objectStates)
            {
                if (!_spawnedObjects.TryGetValue(kvp.Key, out var obj) || obj == null) continue;

                var state = kvp.Value;

                // Smooth interpolation
                state.CurrentPosition = Vector3.Lerp(state.CurrentPosition, state.TargetPosition,
                    Time.deltaTime * PositionSmoothing);
                state.CurrentRotation = Quaternion.Slerp(state.CurrentRotation, state.TargetRotation,
                    Time.deltaTime * RotationSmoothing);

                obj.transform.position = state.CurrentPosition;
                obj.transform.rotation = state.CurrentRotation;
            }
        }

        private void ProcessEvents(ReplayFrame frame)
        {
            if (frame.Events == null) return;

            foreach (var evt in frame.Events)
            {
                if (evt.Timestamp <= _lastEventTime) continue;

                switch (evt.Type)
                {
                    case ReplayEventType.ObjectSpawned:
                        SpawnReplayObject(evt.ObjectId, evt.PrefabName);
                        break;

                    case ReplayEventType.ObjectDespawned:
                        DespawnReplayObject(evt.ObjectId);
                        break;

                    case ReplayEventType.PlayerJoined:
                        EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer",
                            $"Player joined: {evt.Data}");
                        break;

                    case ReplayEventType.PlayerLeft:
                        EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer",
                            $"Player left: {evt.OwnerPuid}");
                        break;

                    case ReplayEventType.GameEvent:
                        EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayPlayer",
                            $"Game event: {evt.Data}");
                        break;
                }
            }

            _lastEventTime = frame.Timestamp;
        }

        private GameObject SpawnReplayObject(int objectId, string prefabName = null, bool isPlayer = false)
        {
            if (_spawnedObjects.ContainsKey(objectId))
            {
                return _spawnedObjects[objectId];
            }

            // Get prefab from settings (checks mappings, Resources, then defaults)
            var settings = Settings;
            GameObject prefab = settings.GetPrefab(prefabName, isPlayer);

            // Create object
            GameObject obj;
            if (prefab != null)
            {
                obj = Instantiate(prefab, _replayContainer);
            }
            else
            {
                // Create placeholder with appropriate color and name
                string displayName = !string.IsNullOrEmpty(prefabName) ? prefabName : $"Object {objectId}";
                obj = settings.CreatePlaceholder(isPlayer, _replayContainer, displayName);
            }

            obj.name = $"ReplayObject_{objectId}";
            _spawnedObjects[objectId] = obj;

            // Initialize state
            _objectStates[objectId] = new ReplayObjectState
            {
                CurrentPosition = obj.transform.position,
                CurrentRotation = obj.transform.rotation,
                TargetPosition = obj.transform.position,
                TargetRotation = obj.transform.rotation,
                IsActive = true
            };

            OnObjectSpawned?.Invoke(objectId, obj);
            return obj;
        }

        private void DespawnReplayObject(int objectId)
        {
            if (_spawnedObjects.TryGetValue(objectId, out var obj))
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
                _spawnedObjects.Remove(objectId);
                _objectStates.Remove(objectId);

                OnObjectDespawned?.Invoke(objectId);
            }
        }

        private void ClearSpawnedObjects()
        {
            foreach (var obj in _spawnedObjects.Values)
            {
                if (obj != null)
                {
                    Destroy(obj);
                }
            }

            _spawnedObjects.Clear();
            _objectStates.Clear();
        }

        private int FindNearestKeyframe(float time)
        {
            int bestIndex = -1;

            for (int i = 0; i < _frames.Count; i++)
            {
                if (_frames[i].IsKeyframe && _frames[i].Timestamp <= time)
                {
                    bestIndex = i;
                }
                else if (_frames[i].Timestamp > time)
                {
                    break;
                }
            }

            return bestIndex;
        }

        #endregion
    }
}
