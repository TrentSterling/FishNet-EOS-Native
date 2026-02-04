using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Voice
{
    /// <summary>
    /// Voice chat zone modes.
    /// </summary>
    public enum VoiceZoneMode
    {
        /// <summary>All players can hear each other equally.</summary>
        Global,
        /// <summary>Volume scales with distance from other players.</summary>
        Proximity,
        /// <summary>Only teammates can hear each other.</summary>
        Team,
        /// <summary>Proximity within team only (team + distance).</summary>
        TeamProximity,
        /// <summary>Custom zones defined by triggers/areas.</summary>
        Custom
    }

    /// <summary>
    /// Manages voice chat zones for proximity-based, team-based, or global voice.
    /// Works alongside EOSVoiceManager to dynamically adjust per-participant volumes.
    /// </summary>
    public class EOSVoiceZoneManager : MonoBehaviour
    {
        #region Singleton

        private static EOSVoiceZoneManager _instance;

        public static EOSVoiceZoneManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSVoiceZoneManager>();
#else
                    _instance = FindObjectOfType<EOSVoiceZoneManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when zone mode changes.</summary>
        public event Action<VoiceZoneMode> OnZoneModeChanged;

        /// <summary>Fired when a player's effective voice volume changes significantly.</summary>
        public event Action<string, float> OnPlayerVolumeChanged;

        /// <summary>Fired when a player enters hearing range (proximity mode).</summary>
        public event Action<string> OnPlayerEnteredRange;

        /// <summary>Fired when a player exits hearing range (proximity mode).</summary>
        public event Action<string> OnPlayerExitedRange;

        #endregion

        #region Inspector Settings

        [Header("Zone Mode")]
        [Tooltip("Current voice zone mode")]
        [SerializeField] private VoiceZoneMode _zoneMode = VoiceZoneMode.Global;

        [Header("Proximity Settings")]
        [Tooltip("Maximum distance to hear other players (units)")]
        [SerializeField] private float _maxHearingDistance = 30f;

        [Tooltip("Distance at which volume starts to fade")]
        [SerializeField] private float _fadeStartDistance = 10f;

        [Tooltip("Minimum volume at max distance (0-100)")]
        [SerializeField] private float _minVolume = 0f;

        [Tooltip("Maximum volume when close (0-100)")]
        [SerializeField] private float _maxVolume = 100f;

        [Tooltip("Volume falloff curve (1 = linear, 2 = quadratic, 0.5 = sqrt)")]
        [SerializeField] private float _falloffExponent = 1f;

        [Header("Team Settings")]
        [Tooltip("Local player's team (set this or use SetTeam())")]
        [SerializeField] private int _localTeam = 0;

        [Tooltip("Allow hearing enemies in team mode (at reduced volume)")]
        [SerializeField] private bool _allowCrossTeamAudio = false;

        [Tooltip("Volume multiplier for cross-team audio (0-1)")]
        [SerializeField, Range(0f, 1f)] private float _crossTeamVolumeMultiplier = 0.25f;

        [Header("Update Settings")]
        [Tooltip("How often to update volumes (seconds)")]
        [SerializeField] private float _updateInterval = 0.1f;

        [Tooltip("Only update if volume changed by this much")]
        [SerializeField] private float _volumeChangeThreshold = 2f;

        [Header("Position Source")]
        [Tooltip("Tag to identify player objects for position tracking")]
        [SerializeField] private string _playerTag = "Player";

        [Tooltip("Use FishNet NetworkObjects for position tracking")]
        [SerializeField] private bool _useFishNetPositions = true;

        #endregion

        #region Public Properties

        /// <summary>Current voice zone mode.</summary>
        public VoiceZoneMode ZoneMode
        {
            get => _zoneMode;
            set => SetZoneMode(value);
        }

        /// <summary>Maximum hearing distance for proximity mode.</summary>
        public float MaxHearingDistance
        {
            get => _maxHearingDistance;
            set => _maxHearingDistance = Mathf.Max(1f, value);
        }

        /// <summary>Distance at which volume starts fading.</summary>
        public float FadeStartDistance
        {
            get => _fadeStartDistance;
            set => _fadeStartDistance = Mathf.Clamp(value, 0f, _maxHearingDistance);
        }

        /// <summary>Local player's team number.</summary>
        public int LocalTeam
        {
            get => _localTeam;
            set => SetTeam(value);
        }

        /// <summary>Whether the manager is actively adjusting volumes.</summary>
        public bool IsActive => _zoneMode != VoiceZoneMode.Global && EOSVoiceManager.Instance?.IsConnected == true;

        #endregion

        #region Private Fields

        private float _lastUpdateTime;
        private Transform _localPlayerTransform;
        private readonly Dictionary<string, Transform> _playerTransforms = new();
        private readonly Dictionary<string, float> _lastVolumes = new();
        private readonly Dictionary<string, int> _playerTeams = new();
        private readonly HashSet<string> _playersInRange = new();

        // Custom zone support
        private readonly Dictionary<string, string> _playerZones = new();
        private string _localZone = "default";

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
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            // Subscribe to voice events
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnVoiceConnectionChanged += OnVoiceConnectionChanged;
            }
        }

        private void OnDisable()
        {
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnVoiceConnectionChanged -= OnVoiceConnectionChanged;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!IsActive) return;

            if (Time.time - _lastUpdateTime >= _updateInterval)
            {
                _lastUpdateTime = Time.time;
                UpdateVoiceVolumes();
            }
        }

        #endregion

        #region Public API - Zone Mode

        /// <summary>
        /// Set the voice zone mode.
        /// </summary>
        public void SetZoneMode(VoiceZoneMode mode)
        {
            if (_zoneMode == mode) return;

            var oldMode = _zoneMode;
            _zoneMode = mode;

            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                $"Zone mode changed: {oldMode} -> {mode}");

            // Reset all volumes when switching to/from global
            if (mode == VoiceZoneMode.Global || oldMode == VoiceZoneMode.Global)
            {
                ResetAllVolumes();
            }

            OnZoneModeChanged?.Invoke(mode);
        }

        /// <summary>
        /// Set the local player's team.
        /// </summary>
        public void SetTeam(int team)
        {
            if (_localTeam == team) return;

            _localTeam = team;
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                $"Local team set to: {team}");

            // Update volumes immediately if in team mode
            if (_zoneMode == VoiceZoneMode.Team || _zoneMode == VoiceZoneMode.TeamProximity)
            {
                UpdateVoiceVolumes();
            }
        }

        /// <summary>
        /// Set a player's team.
        /// </summary>
        public void SetPlayerTeam(string puid, int team)
        {
            _playerTeams[puid] = team;

            if (_zoneMode == VoiceZoneMode.Team || _zoneMode == VoiceZoneMode.TeamProximity)
            {
                UpdatePlayerVolume(puid);
            }
        }

        /// <summary>
        /// Get a player's team.
        /// </summary>
        public int GetPlayerTeam(string puid)
        {
            return _playerTeams.TryGetValue(puid, out int team) ? team : 0;
        }

        #endregion

        #region Public API - Position Tracking

        /// <summary>
        /// Register the local player's transform for position tracking.
        /// </summary>
        public void RegisterLocalPlayer(Transform playerTransform)
        {
            _localPlayerTransform = playerTransform;
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                "Local player registered");
        }

        /// <summary>
        /// Register a remote player's transform for position tracking.
        /// </summary>
        public void RegisterPlayer(string puid, Transform playerTransform)
        {
            _playerTransforms[puid] = playerTransform;
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceZoneManager",
                $"Player registered: {puid.Substring(0, 8)}...");
        }

        /// <summary>
        /// Unregister a player from position tracking.
        /// </summary>
        public void UnregisterPlayer(string puid)
        {
            _playerTransforms.Remove(puid);
            _playerTeams.Remove(puid);
            _lastVolumes.Remove(puid);
            _playersInRange.Remove(puid);
            _playerZones.Remove(puid);
        }

        /// <summary>
        /// Clear all tracked players.
        /// </summary>
        public void ClearAllPlayers()
        {
            _playerTransforms.Clear();
            _playerTeams.Clear();
            _lastVolumes.Clear();
            _playersInRange.Clear();
            _playerZones.Clear();
            _localPlayerTransform = null;
        }

        /// <summary>
        /// Auto-discover players using FishNet NetworkObjects.
        /// Call this periodically or after spawns.
        /// </summary>
        public void AutoDiscoverPlayers()
        {
            if (!_useFishNetPositions) return;
            if (!InstanceFinder.IsClientStarted) return;

            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return;

            // Find all spawned network objects that could be players
            foreach (var nob in InstanceFinder.ClientManager.Objects.Spawned.Values)
            {
                if (nob == null || nob.Owner == null || !nob.Owner.IsValid) continue;

                // Check if it's tagged as player or has player-like components
                bool isPlayer = !string.IsNullOrEmpty(_playerTag) && nob.CompareTag(_playerTag);
                if (!isPlayer) continue;

                // Get PUID for this connection
                string puid = registry.GetPuid(nob.OwnerId);
                if (string.IsNullOrEmpty(puid)) continue;

                // Don't track local player this way
                string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
                if (puid == localPuid)
                {
                    if (_localPlayerTransform == null)
                        _localPlayerTransform = nob.transform;
                    continue;
                }

                // Register if not already tracked
                if (!_playerTransforms.ContainsKey(puid))
                {
                    RegisterPlayer(puid, nob.transform);
                }
            }
        }

        #endregion

        #region Public API - Custom Zones

        /// <summary>
        /// Set the local player's current zone (for Custom mode).
        /// </summary>
        public void SetLocalZone(string zoneName)
        {
            _localZone = zoneName ?? "default";
            if (_zoneMode == VoiceZoneMode.Custom)
            {
                UpdateVoiceVolumes();
            }
        }

        /// <summary>
        /// Set a player's current zone (for Custom mode).
        /// </summary>
        public void SetPlayerZone(string puid, string zoneName)
        {
            _playerZones[puid] = zoneName ?? "default";
            if (_zoneMode == VoiceZoneMode.Custom)
            {
                UpdatePlayerVolume(puid);
            }
        }

        /// <summary>
        /// Get a player's current zone.
        /// </summary>
        public string GetPlayerZone(string puid)
        {
            return _playerZones.TryGetValue(puid, out string zone) ? zone : "default";
        }

        #endregion

        #region Public API - Volume Queries

        /// <summary>
        /// Get the current effective volume for a player.
        /// </summary>
        public float GetPlayerVolume(string puid)
        {
            return _lastVolumes.TryGetValue(puid, out float vol) ? vol : _maxVolume;
        }

        /// <summary>
        /// Check if a player is currently in hearing range.
        /// </summary>
        public bool IsPlayerInRange(string puid)
        {
            return _playersInRange.Contains(puid);
        }

        /// <summary>
        /// Get all players currently in hearing range.
        /// </summary>
        public List<string> GetPlayersInRange()
        {
            return new List<string>(_playersInRange);
        }

        /// <summary>
        /// Get distance to a player (or -1 if unknown).
        /// </summary>
        public float GetDistanceToPlayer(string puid)
        {
            if (_localPlayerTransform == null) return -1f;
            if (!_playerTransforms.TryGetValue(puid, out var transform)) return -1f;
            if (transform == null) return -1f;

            return Vector3.Distance(_localPlayerTransform.position, transform.position);
        }

        #endregion

        #region Public API - Configuration

        /// <summary>
        /// Configure proximity settings.
        /// </summary>
        public void ConfigureProximity(float maxDistance, float fadeStart, float minVol = 0f, float maxVol = 100f)
        {
            _maxHearingDistance = Mathf.Max(1f, maxDistance);
            _fadeStartDistance = Mathf.Clamp(fadeStart, 0f, _maxHearingDistance);
            _minVolume = Mathf.Clamp(minVol, 0f, 100f);
            _maxVolume = Mathf.Clamp(maxVol, 0f, 100f);
        }

        /// <summary>
        /// Configure team settings.
        /// </summary>
        public void ConfigureTeam(bool allowCrossTeam, float crossTeamMultiplier = 0.25f)
        {
            _allowCrossTeamAudio = allowCrossTeam;
            _crossTeamVolumeMultiplier = Mathf.Clamp01(crossTeamMultiplier);
        }

        #endregion

        #region Volume Calculation

        private void UpdateVoiceVolumes()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected) return;

            // Auto-discover if using FishNet
            if (_useFishNetPositions)
            {
                AutoDiscoverPlayers();
            }

            // Update each tracked player
            foreach (var puid in voiceManager.GetAllParticipants())
            {
                UpdatePlayerVolume(puid);
            }
        }

        private void UpdatePlayerVolume(string puid)
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            float newVolume = CalculateVolume(puid);

            // Check if volume changed significantly
            float lastVolume = _lastVolumes.TryGetValue(puid, out float lv) ? lv : -999f;
            if (Mathf.Abs(newVolume - lastVolume) < _volumeChangeThreshold) return;

            // Update EOS volume
            voiceManager.SetParticipantVolume(puid, newVolume);
            _lastVolumes[puid] = newVolume;

            // Track in-range state for proximity modes
            bool wasInRange = _playersInRange.Contains(puid);
            bool isInRange = newVolume > _minVolume + 0.1f;

            if (isInRange && !wasInRange)
            {
                _playersInRange.Add(puid);
                OnPlayerEnteredRange?.Invoke(puid);
            }
            else if (!isInRange && wasInRange)
            {
                _playersInRange.Remove(puid);
                OnPlayerExitedRange?.Invoke(puid);
            }

            // Fire volume changed event
            if (Mathf.Abs(newVolume - lastVolume) > 5f)
            {
                OnPlayerVolumeChanged?.Invoke(puid, newVolume);
            }
        }

        private float CalculateVolume(string puid)
        {
            switch (_zoneMode)
            {
                case VoiceZoneMode.Global:
                    return _maxVolume;

                case VoiceZoneMode.Proximity:
                    return CalculateProximityVolume(puid);

                case VoiceZoneMode.Team:
                    return CalculateTeamVolume(puid);

                case VoiceZoneMode.TeamProximity:
                    float teamVol = CalculateTeamVolume(puid);
                    if (teamVol <= 0) return 0;
                    return CalculateProximityVolume(puid) * (teamVol / _maxVolume);

                case VoiceZoneMode.Custom:
                    return CalculateCustomZoneVolume(puid);

                default:
                    return _maxVolume;
            }
        }

        private float CalculateProximityVolume(string puid)
        {
            if (_localPlayerTransform == null) return _maxVolume;

            if (!_playerTransforms.TryGetValue(puid, out var playerTransform) || playerTransform == null)
            {
                return _maxVolume; // Can't determine position, use max
            }

            float distance = Vector3.Distance(_localPlayerTransform.position, playerTransform.position);

            // Beyond max distance = silent
            if (distance >= _maxHearingDistance)
            {
                return _minVolume;
            }

            // Within fade start = full volume
            if (distance <= _fadeStartDistance)
            {
                return _maxVolume;
            }

            // Calculate falloff
            float fadeRange = _maxHearingDistance - _fadeStartDistance;
            float fadeDistance = distance - _fadeStartDistance;
            float t = fadeDistance / fadeRange;

            // Apply falloff curve
            t = Mathf.Pow(t, _falloffExponent);

            // Lerp between max and min
            return Mathf.Lerp(_maxVolume, _minVolume, t);
        }

        private float CalculateTeamVolume(string puid)
        {
            int playerTeam = GetPlayerTeam(puid);

            // Same team = full volume
            if (playerTeam == _localTeam)
            {
                return _maxVolume;
            }

            // Different team
            if (_allowCrossTeamAudio)
            {
                return _maxVolume * _crossTeamVolumeMultiplier;
            }

            return 0f; // Muted
        }

        private float CalculateCustomZoneVolume(string puid)
        {
            string playerZone = GetPlayerZone(puid);

            // Same zone = full volume
            if (playerZone == _localZone)
            {
                return _maxVolume;
            }

            // Different zone = muted (custom logic can override)
            return 0f;
        }

        private void ResetAllVolumes()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            foreach (var puid in voiceManager.GetAllParticipants())
            {
                voiceManager.SetParticipantVolume(puid, _maxVolume);
            }

            _lastVolumes.Clear();
            _playersInRange.Clear();
        }

        #endregion

        #region Event Handlers

        private void OnVoiceConnectionChanged(bool connected)
        {
            if (!connected)
            {
                _lastVolumes.Clear();
                _playersInRange.Clear();
            }
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSVoiceZoneManager))]
    public class EOSVoiceZoneManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSVoiceZoneManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.EnumPopup("Zone Mode", manager.ZoneMode);
                EditorGUILayout.Toggle("Active", manager.IsActive);
                EditorGUILayout.IntField("Local Team", manager.LocalTeam);
            }

            if (Application.isPlaying && manager.IsActive)
            {
                EditorGUILayout.Space(5);
                var inRange = manager.GetPlayersInRange();
                EditorGUILayout.LabelField($"Players in Range: {inRange.Count}");

                if (inRange.Count > 0)
                {
                    EditorGUI.indentLevel++;
                    foreach (var puid in inRange)
                    {
                        float vol = manager.GetPlayerVolume(puid);
                        float dist = manager.GetDistanceToPlayer(puid);
                        string shortPuid = puid.Length > 12 ? puid.Substring(0, 8) + "..." : puid;
                        EditorGUILayout.LabelField($"{shortPuid}: Vol={vol:0}%, Dist={dist:0.0}m");
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Global")) manager.SetZoneMode(VoiceZoneMode.Global);
                if (GUILayout.Button("Proximity")) manager.SetZoneMode(VoiceZoneMode.Proximity);
                if (GUILayout.Button("Team")) manager.SetZoneMode(VoiceZoneMode.Team);
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
