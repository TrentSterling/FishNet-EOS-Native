using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// ScriptableObject storing replay system settings.
    /// Create via Assets > Create > FishNet > EOS Native > Replay Settings
    /// Place in Resources folder for auto-loading.
    /// </summary>
    [CreateAssetMenu(fileName = "EOSReplaySettings", menuName = "FishNet/EOS Native/Replay Settings")]
    public class EOSReplaySettings : ScriptableObject
    {
        private static EOSReplaySettings _instance;

        /// <summary>
        /// Singleton accessor. Loads from Resources/EOSReplaySettings if available.
        /// </summary>
        public static EOSReplaySettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<EOSReplaySettings>("EOSReplaySettings");

                    // Create runtime defaults if no settings exist
                    if (_instance == null)
                    {
                        _instance = CreateInstance<EOSReplaySettings>();
                    }
                }
                return _instance;
            }
        }

        [Header("Recording")]
        [Tooltip("Frames per second to record.")]
        [SerializeField] private float _frameRate = 20f;

        [Tooltip("Seconds between full keyframes (for seeking).")]
        [SerializeField] private float _keyframeInterval = 5f;

        [Tooltip("Automatically start recording when a match starts.")]
        [SerializeField] private bool _autoRecord = true;

        [Tooltip("Record all NetworkObjects, not just those with ReplayRecordable.")]
        [SerializeField] private bool _recordAllNetworkObjects = true;

        [Header("Recording Limits")]
        [Tooltip("Maximum recording duration in minutes. Recording stops automatically after this.")]
        [SerializeField] private float _maxDurationMinutes = 30f;

        [Tooltip("Warning threshold in minutes (shows warning when approaching limit).")]
        [SerializeField] private float _durationWarningMinutes = 25f;

        [Tooltip("Estimated max file size in MB before warning.")]
        [SerializeField] private float _maxSizeMB = 10f;

        [Tooltip("Auto-stop recording when max duration is reached.")]
        [SerializeField] private bool _autoStopAtLimit = true;

        [Header("Storage")]
        [Tooltip("Maximum number of local replays to keep.")]
        [SerializeField] private int _maxLocalReplays = 50;

        [Tooltip("Maximum number of cloud replays to keep.")]
        [SerializeField] private int _maxCloudReplays = 10;

        [Header("Playback")]
        [Tooltip("Default playback speed.")]
        [SerializeField] private float _defaultPlaybackSpeed = 1f;

        [Tooltip("Smoothing factor for position interpolation.")]
        [SerializeField] private float _positionSmoothing = 10f;

        [Tooltip("Smoothing factor for rotation interpolation.")]
        [SerializeField] private float _rotationSmoothing = 10f;

        [Header("Prefabs")]
        [Tooltip("Default prefab for player objects during replay. If null, uses placeholder.")]
        [SerializeField] private GameObject _defaultPlayerPrefab;

        [Tooltip("Default prefab for generic objects during replay. If null, uses placeholder.")]
        [SerializeField] private GameObject _defaultObjectPrefab;

        [Tooltip("Prefab mappings for specific object types.")]
        [SerializeField] private List<ReplayPrefabMapping> _prefabMappings = new();

        [Header("Placeholder Appearance")]
        [Tooltip("Color for player placeholder objects.")]
        [SerializeField] private Color _playerPlaceholderColor = new Color(0.2f, 0.6f, 1f, 1f);

        [Tooltip("Color for generic placeholder objects.")]
        [SerializeField] private Color _objectPlaceholderColor = new Color(0.8f, 0.8f, 0.8f, 1f);

        [Tooltip("Scale for placeholder spheres.")]
        [SerializeField] private float _placeholderScale = 0.5f;

        // Recording properties
        public float FrameRate => _frameRate;
        public float KeyframeInterval => _keyframeInterval;
        public bool AutoRecord => _autoRecord;
        public bool RecordAllNetworkObjects => _recordAllNetworkObjects;

        // Recording limit properties
        public float MaxDurationMinutes => _maxDurationMinutes;
        public float MaxDurationSeconds => _maxDurationMinutes * 60f;
        public float DurationWarningMinutes => _durationWarningMinutes;
        public float DurationWarningSeconds => _durationWarningMinutes * 60f;
        public float MaxSizeMB => _maxSizeMB;
        public bool AutoStopAtLimit => _autoStopAtLimit;

        // Storage properties
        public int MaxLocalReplays => _maxLocalReplays;
        public int MaxCloudReplays => _maxCloudReplays;

        // Playback properties
        public float DefaultPlaybackSpeed => _defaultPlaybackSpeed;
        public float PositionSmoothing => _positionSmoothing;
        public float RotationSmoothing => _rotationSmoothing;

        // Prefab properties
        public GameObject DefaultPlayerPrefab => _defaultPlayerPrefab;
        public GameObject DefaultObjectPrefab => _defaultObjectPrefab;
        public IReadOnlyList<ReplayPrefabMapping> PrefabMappings => _prefabMappings;

        // Placeholder properties
        public Color PlayerPlaceholderColor => _playerPlaceholderColor;
        public Color ObjectPlaceholderColor => _objectPlaceholderColor;
        public float PlaceholderScale => _placeholderScale;

        /// <summary>
        /// Get prefab for a given name. Checks mappings first, then Resources, then defaults.
        /// </summary>
        public GameObject GetPrefab(string prefabName, bool isPlayer = false)
        {
            // Check mappings first
            if (!string.IsNullOrEmpty(prefabName))
            {
                foreach (var mapping in _prefabMappings)
                {
                    if (mapping.PrefabName == prefabName && mapping.Prefab != null)
                    {
                        return mapping.Prefab;
                    }
                }

                // Try Resources
                var resourcePrefab = Resources.Load<GameObject>(prefabName);
                if (resourcePrefab != null) return resourcePrefab;
            }

            // Use defaults
            return isPlayer ? _defaultPlayerPrefab : _defaultObjectPrefab;
        }

        /// <summary>
        /// Create a placeholder object with appropriate color and features.
        /// Uses ReplayGhost for enhanced visuals (labels, trails).
        /// Players get unique colors from the palette.
        /// </summary>
        public GameObject CreatePlaceholder(bool isPlayer, Transform parent, string displayName = null)
        {
            // Players get unique colors from palette, objects use configured color
            var color = isPlayer ? ReplayGhost.GetNextPlayerColor() : _objectPlaceholderColor;

            // Use ReplayGhost for enhanced placeholder
            ReplayGhost ghost;
            if (isPlayer)
            {
                ghost = ReplayGhost.CreatePlayerGhost(parent, displayName ?? "Player", color);
            }
            else
            {
                ghost = ReplayGhost.CreateSphereGhost(parent, color, false);
            }

            ghost.transform.localScale = Vector3.one * _placeholderScale;
            return ghost.gameObject;
        }

        private void OnValidate()
        {
            _frameRate = Mathf.Max(1f, _frameRate);
            _keyframeInterval = Mathf.Max(1f, _keyframeInterval);
            _maxDurationMinutes = Mathf.Max(1f, _maxDurationMinutes);
            _durationWarningMinutes = Mathf.Clamp(_durationWarningMinutes, 1f, _maxDurationMinutes - 1f);
            _maxSizeMB = Mathf.Max(1f, _maxSizeMB);
            _maxLocalReplays = Mathf.Max(1, _maxLocalReplays);
            _maxCloudReplays = Mathf.Max(1, _maxCloudReplays);
            _defaultPlaybackSpeed = Mathf.Clamp(_defaultPlaybackSpeed, 0.1f, 4f);
            _positionSmoothing = Mathf.Max(1f, _positionSmoothing);
            _rotationSmoothing = Mathf.Max(1f, _rotationSmoothing);
            _placeholderScale = Mathf.Max(0.1f, _placeholderScale);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor: Clear cached instance to force reload.
        /// </summary>
        public static void ClearCache()
        {
            _instance = null;
        }
#endif
    }

    /// <summary>
    /// Maps a prefab name (from recording) to a prefab (for playback).
    /// </summary>
    [Serializable]
    public class ReplayPrefabMapping
    {
        [Tooltip("Name used during recording (usually the NetworkObject prefab name).")]
        public string PrefabName;

        [Tooltip("Prefab to instantiate during playback.")]
        public GameObject Prefab;

        [Tooltip("Whether this is a player object (for placeholder coloring if prefab is null).")]
        public bool IsPlayer;
    }
}
