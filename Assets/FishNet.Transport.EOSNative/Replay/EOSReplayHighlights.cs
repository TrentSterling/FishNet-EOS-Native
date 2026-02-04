using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Detects and manages replay highlights (significant moments).
    /// </summary>
    public class EOSReplayHighlights : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayHighlights _instance;
        public static EOSReplayHighlights Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSReplayHighlights>();
#else
                    _instance = FindObjectOfType<EOSReplayHighlights>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a highlight is detected.</summary>
        public event Action<ReplayHighlight> OnHighlightDetected;

        /// <summary>Fired when a manual highlight is added.</summary>
        public event Action<ReplayHighlight> OnHighlightAdded;

        /// <summary>Fired when a highlight is removed.</summary>
        public event Action<string> OnHighlightRemoved;

        #endregion

        #region Inspector Settings

        [Header("Detection Settings")]
        [Tooltip("Enable automatic highlight detection")]
        [SerializeField] private bool _autoDetect = true;

        [Tooltip("Minimum time between auto-detected highlights (seconds)")]
        [SerializeField] private float _minHighlightInterval = 5f;

        [Header("Detection Thresholds")]
        [Tooltip("Kills within this time window count as multi-kill")]
        [SerializeField] private float _multiKillWindow = 4f;

        [Tooltip("Minimum kills for multi-kill highlight")]
        [SerializeField] private int _multiKillThreshold = 2;

        [Tooltip("Health percentage threshold for clutch detection")]
        [SerializeField, Range(0f, 1f)] private float _clutchHealthThreshold = 0.1f;

        [Tooltip("Score change threshold for comeback detection")]
        [SerializeField] private int _comebackScoreThreshold = 5;

        [Header("Manual Highlights")]
        [Tooltip("Duration before/after timestamp for manual highlights")]
        [SerializeField] private float _manualHighlightPadding = 5f;

        #endregion

        #region Public Properties

        /// <summary>All detected highlights for current recording.</summary>
        public IReadOnlyList<ReplayHighlight> Highlights => _highlights;

        /// <summary>Whether auto-detection is enabled.</summary>
        public bool AutoDetect
        {
            get => _autoDetect;
            set => _autoDetect = value;
        }

        /// <summary>Whether currently recording.</summary>
        public bool IsRecording { get; private set; }

        #endregion

        #region Private Fields

        private readonly List<ReplayHighlight> _highlights = new();
        private float _lastHighlightTime;
        private readonly List<float> _recentKillTimes = new();
        private int _lastTeamScore;
        private int _lastEnemyScore;
        private float _recordingStartTime;

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
            // Subscribe to replay recorder events
            if (EOSReplayRecorder.Instance != null)
            {
                EOSReplayRecorder.Instance.OnRecordingStarted += OnRecordingStarted;
                EOSReplayRecorder.Instance.OnRecordingStopped += OnRecordingStopped;
            }
        }

        private void OnDisable()
        {
            if (EOSReplayRecorder.Instance != null)
            {
                EOSReplayRecorder.Instance.OnRecordingStarted -= OnRecordingStarted;
                EOSReplayRecorder.Instance.OnRecordingStopped -= OnRecordingStopped;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Manual Highlights

        /// <summary>
        /// Add a manual highlight at current time.
        /// </summary>
        public ReplayHighlight AddHighlight(string title, HighlightType type = HighlightType.Manual)
        {
            if (!IsRecording)
            {
                EOSDebugLogger.LogWarning("EOSReplayHighlights", "Not recording");
                return null;
            }

            float timestamp = Time.time - _recordingStartTime;
            return AddHighlightAtTime(timestamp, title, type);
        }

        /// <summary>
        /// Add a highlight at a specific timestamp.
        /// </summary>
        public ReplayHighlight AddHighlightAtTime(float timestamp, string title, HighlightType type = HighlightType.Manual)
        {
            var highlight = new ReplayHighlight
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = timestamp,
                Title = title,
                Type = type,
                Duration = _manualHighlightPadding * 2,
                StartOffset = _manualHighlightPadding,
                Importance = GetDefaultImportance(type)
            };

            _highlights.Add(highlight);

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayHighlights",
                $"Added highlight: {title} at {timestamp:F1}s");

            OnHighlightAdded?.Invoke(highlight);
            return highlight;
        }

        /// <summary>
        /// Remove a highlight.
        /// </summary>
        public bool RemoveHighlight(string highlightId)
        {
            int removed = _highlights.RemoveAll(h => h.Id == highlightId);
            if (removed > 0)
            {
                OnHighlightRemoved?.Invoke(highlightId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clear all highlights.
        /// </summary>
        public void ClearHighlights()
        {
            _highlights.Clear();
        }

        /// <summary>
        /// Favorite/unfavorite a highlight.
        /// </summary>
        public void SetHighlightFavorite(string highlightId, bool favorite)
        {
            var highlight = _highlights.FirstOrDefault(h => h.Id == highlightId);
            if (highlight != null)
            {
                highlight.IsFavorite = favorite;
            }
        }

        #endregion

        #region Public API - Auto Detection Events

        /// <summary>
        /// Report a kill for multi-kill detection.
        /// </summary>
        public void ReportKill(string killerPuid, string victimPuid, bool isHeadshot = false)
        {
            if (!IsRecording || !_autoDetect) return;

            float currentTime = Time.time - _recordingStartTime;
            _recentKillTimes.Add(currentTime);

            // Clean old kills
            _recentKillTimes.RemoveAll(t => currentTime - t > _multiKillWindow);

            // Check for multi-kill
            if (_recentKillTimes.Count >= _multiKillThreshold)
            {
                DetectHighlight(currentTime, HighlightType.MultiKill,
                    $"{_recentKillTimes.Count}x Kill!",
                    _recentKillTimes.Count >= 4 ? HighlightImportance.Epic :
                    _recentKillTimes.Count >= 3 ? HighlightImportance.High :
                    HighlightImportance.Medium);
                _recentKillTimes.Clear();
            }
            else if (isHeadshot)
            {
                DetectHighlight(currentTime, HighlightType.Headshot, "Headshot!", HighlightImportance.Low);
            }
        }

        /// <summary>
        /// Report a clutch (win while low health or outnumbered).
        /// </summary>
        public void ReportClutch(float healthPercent, int aliveTeammates, int aliveEnemies)
        {
            if (!IsRecording || !_autoDetect) return;

            bool isLowHealth = healthPercent <= _clutchHealthThreshold;
            bool isOutnumbered = aliveTeammates == 1 && aliveEnemies >= 2;

            if (isLowHealth || isOutnumbered)
            {
                float currentTime = Time.time - _recordingStartTime;
                string title = isOutnumbered
                    ? $"1v{aliveEnemies} Clutch!"
                    : "Low Health Clutch!";

                DetectHighlight(currentTime, HighlightType.Clutch, title,
                    isOutnumbered && isLowHealth ? HighlightImportance.Epic :
                    HighlightImportance.High);
            }
        }

        /// <summary>
        /// Report score update for comeback detection.
        /// </summary>
        public void ReportScoreUpdate(int teamScore, int enemyScore)
        {
            if (!IsRecording || !_autoDetect) return;

            // Detect comeback (was behind, now ahead or tied)
            int previousDiff = _lastEnemyScore - _lastTeamScore;
            int currentDiff = enemyScore - teamScore;

            if (previousDiff >= _comebackScoreThreshold && currentDiff <= 0)
            {
                float currentTime = Time.time - _recordingStartTime;
                DetectHighlight(currentTime, HighlightType.Comeback,
                    "Comeback!", HighlightImportance.High);
            }

            _lastTeamScore = teamScore;
            _lastEnemyScore = enemyScore;
        }

        /// <summary>
        /// Report an objective completion.
        /// </summary>
        public void ReportObjective(string objectiveName)
        {
            if (!IsRecording || !_autoDetect) return;

            float currentTime = Time.time - _recordingStartTime;
            DetectHighlight(currentTime, HighlightType.Objective,
                $"Objective: {objectiveName}", HighlightImportance.Medium);
        }

        /// <summary>
        /// Report match end (win/loss).
        /// </summary>
        public void ReportMatchEnd(bool won)
        {
            if (!IsRecording) return;

            float currentTime = Time.time - _recordingStartTime;
            AddHighlightAtTime(currentTime,
                won ? "Victory!" : "Defeat",
                won ? HighlightType.Victory : HighlightType.Custom);
        }

        /// <summary>
        /// Report a custom significant event.
        /// </summary>
        public void ReportCustomEvent(string eventName, HighlightImportance importance = HighlightImportance.Medium)
        {
            if (!IsRecording || !_autoDetect) return;

            float currentTime = Time.time - _recordingStartTime;
            DetectHighlight(currentTime, HighlightType.Custom, eventName, importance);
        }

        #endregion

        #region Public API - Queries

        /// <summary>
        /// Get highlights sorted by timestamp.
        /// </summary>
        public List<ReplayHighlight> GetHighlightsByTime()
        {
            return _highlights.OrderBy(h => h.Timestamp).ToList();
        }

        /// <summary>
        /// Get highlights sorted by importance.
        /// </summary>
        public List<ReplayHighlight> GetHighlightsByImportance()
        {
            return _highlights.OrderByDescending(h => h.Importance).ToList();
        }

        /// <summary>
        /// Get highlights of a specific type.
        /// </summary>
        public List<ReplayHighlight> GetHighlightsByType(HighlightType type)
        {
            return _highlights.Where(h => h.Type == type).ToList();
        }

        /// <summary>
        /// Get favorite highlights only.
        /// </summary>
        public List<ReplayHighlight> GetFavoriteHighlights()
        {
            return _highlights.Where(h => h.IsFavorite).ToList();
        }

        /// <summary>
        /// Get top highlights by importance.
        /// </summary>
        public List<ReplayHighlight> GetTopHighlights(int count = 5)
        {
            return _highlights
                .OrderByDescending(h => h.Importance)
                .ThenByDescending(h => h.Timestamp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Find nearest highlight to a timestamp.
        /// </summary>
        public ReplayHighlight FindNearestHighlight(float timestamp)
        {
            return _highlights
                .OrderBy(h => Mathf.Abs(h.Timestamp - timestamp))
                .FirstOrDefault();
        }

        /// <summary>
        /// Get highlight at or near a timestamp.
        /// </summary>
        public ReplayHighlight GetHighlightAt(float timestamp, float tolerance = 2f)
        {
            return _highlights.FirstOrDefault(h =>
                Mathf.Abs(h.Timestamp - timestamp) <= tolerance);
        }

        #endregion

        #region Public API - Serialization

        /// <summary>
        /// Export highlights for saving with replay.
        /// </summary>
        public List<ReplayHighlight> ExportHighlights()
        {
            return new List<ReplayHighlight>(_highlights);
        }

        /// <summary>
        /// Import highlights from saved replay.
        /// </summary>
        public void ImportHighlights(List<ReplayHighlight> highlights)
        {
            _highlights.Clear();
            if (highlights != null)
            {
                _highlights.AddRange(highlights);
            }
        }

        #endregion

        #region Public API - Display

        /// <summary>
        /// Get icon for highlight type.
        /// </summary>
        public static string GetTypeIcon(HighlightType type)
        {
            return type switch
            {
                HighlightType.MultiKill => "MK",
                HighlightType.Clutch => "CL",
                HighlightType.Headshot => "HS",
                HighlightType.Comeback => "CB",
                HighlightType.Objective => "OB",
                HighlightType.Victory => "W",
                HighlightType.Manual => "M",
                HighlightType.Custom => "*",
                _ => "?"
            };
        }

        /// <summary>
        /// Get display name for highlight type.
        /// </summary>
        public static string GetTypeName(HighlightType type)
        {
            return type switch
            {
                HighlightType.MultiKill => "Multi-Kill",
                HighlightType.Clutch => "Clutch",
                HighlightType.Headshot => "Headshot",
                HighlightType.Comeback => "Comeback",
                HighlightType.Objective => "Objective",
                HighlightType.Victory => "Victory",
                HighlightType.Manual => "Bookmark",
                HighlightType.Custom => "Event",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get importance display string.
        /// </summary>
        public static string GetImportanceString(HighlightImportance importance)
        {
            return importance switch
            {
                HighlightImportance.Epic => "!!!",
                HighlightImportance.High => "!!",
                HighlightImportance.Medium => "!",
                HighlightImportance.Low => "",
                _ => ""
            };
        }

        /// <summary>
        /// Format timestamp for display.
        /// </summary>
        public static string FormatTimestamp(float seconds)
        {
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins}:{secs:D2}";
        }

        #endregion

        #region Private Methods

        private void OnRecordingStarted(string matchId)
        {
            IsRecording = true;
            _recordingStartTime = Time.time;
            _highlights.Clear();
            _recentKillTimes.Clear();
            _lastHighlightTime = -999f;
            _lastTeamScore = 0;
            _lastEnemyScore = 0;

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayHighlights",
                "Highlight detection started");
        }

        private void OnRecordingStopped(object replay)
        {
            IsRecording = false;

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayHighlights",
                $"Highlight detection stopped ({_highlights.Count} highlights)");
        }

        private void DetectHighlight(float timestamp, HighlightType type, string title, HighlightImportance importance)
        {
            // Check minimum interval
            if (timestamp - _lastHighlightTime < _minHighlightInterval)
            {
                // Merge with previous highlight if same type
                var prev = _highlights.LastOrDefault();
                if (prev != null && prev.Type == type && importance > prev.Importance)
                {
                    prev.Title = title;
                    prev.Importance = importance;
                }
                return;
            }

            var highlight = new ReplayHighlight
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = timestamp,
                Title = title,
                Type = type,
                Duration = _multiKillWindow,
                StartOffset = 2f,
                Importance = importance
            };

            _highlights.Add(highlight);
            _lastHighlightTime = timestamp;

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayHighlights",
                $"Auto-detected: [{GetTypeIcon(type)}] {title} at {timestamp:F1}s");

            OnHighlightDetected?.Invoke(highlight);
        }

        private HighlightImportance GetDefaultImportance(HighlightType type)
        {
            return type switch
            {
                HighlightType.Victory => HighlightImportance.High,
                HighlightType.MultiKill => HighlightImportance.High,
                HighlightType.Clutch => HighlightImportance.High,
                HighlightType.Comeback => HighlightImportance.High,
                HighlightType.Objective => HighlightImportance.Medium,
                HighlightType.Headshot => HighlightImportance.Low,
                _ => HighlightImportance.Medium
            };
        }

        #endregion
    }

    #region Data Types

    /// <summary>
    /// Highlight types.
    /// </summary>
    public enum HighlightType
    {
        Manual,
        MultiKill,
        Clutch,
        Headshot,
        Comeback,
        Objective,
        Victory,
        Custom
    }

    /// <summary>
    /// Highlight importance levels.
    /// </summary>
    public enum HighlightImportance
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Epic = 3
    }

    /// <summary>
    /// Replay highlight data.
    /// </summary>
    [Serializable]
    public class ReplayHighlight
    {
        /// <summary>Unique ID.</summary>
        public string Id;

        /// <summary>Timestamp in replay (seconds from start).</summary>
        public float Timestamp;

        /// <summary>Display title.</summary>
        public string Title;

        /// <summary>Type of highlight.</summary>
        public HighlightType Type;

        /// <summary>Importance level.</summary>
        public HighlightImportance Importance;

        /// <summary>Duration of the highlight clip.</summary>
        public float Duration;

        /// <summary>How many seconds before timestamp to start clip.</summary>
        public float StartOffset;

        /// <summary>User marked as favorite.</summary>
        public bool IsFavorite;

        /// <summary>Optional player PUID associated with highlight.</summary>
        public string PlayerPuid;

        /// <summary>Optional additional data.</summary>
        public string Metadata;

        /// <summary>Get the start time for playback.</summary>
        public float ClipStartTime => Mathf.Max(0, Timestamp - StartOffset);

        /// <summary>Get the end time for playback.</summary>
        public float ClipEndTime => Timestamp + Duration - StartOffset;
    }

    #endregion
}
