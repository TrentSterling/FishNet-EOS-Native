using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Manages player reputation/karma with commendations and reports.
    /// </summary>
    public class EOSReputationManager : MonoBehaviour
    {
        #region Singleton

        private static EOSReputationManager _instance;
        public static EOSReputationManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSReputationManager>();
#else
                    _instance = FindObjectOfType<EOSReputationManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string REPUTATION_DATA_FILE = "reputation_data.json";
        private const int DATA_VERSION = 1;
        private const int MAX_RECENT_FEEDBACK = 50;

        #endregion

        #region Events

        /// <summary>Fired when reputation score changes.</summary>
        public event Action<int, int> OnReputationChanged; // old, new

        /// <summary>Fired when receiving a commendation.</summary>
        public event Action<ReputationFeedback> OnCommendationReceived;

        /// <summary>Fired when receiving a report.</summary>
        public event Action<ReputationFeedback> OnReportReceived;

        /// <summary>Fired when reputation level changes.</summary>
        public event Action<ReputationLevel> OnLevelChanged;

        /// <summary>Fired when data is loaded.</summary>
        public event Action<ReputationData> OnDataLoaded;

        #endregion

        #region Inspector Settings

        [Header("Reputation Settings")]
        [Tooltip("Points gained per commendation")]
        [SerializeField] private int _commendPoints = 10;

        [Tooltip("Points lost per report")]
        [SerializeField] private int _reportPenalty = 5;

        [Tooltip("Minimum reputation score")]
        [SerializeField] private int _minReputation = -100;

        [Tooltip("Maximum reputation score")]
        [SerializeField] private int _maxReputation = 1000;

        [Tooltip("Starting reputation for new players")]
        [SerializeField] private int _defaultReputation = 100;

        [Header("Feedback Categories")]
        [Tooltip("Commendation categories")]
        [SerializeField] private List<string> _commendCategories = new()
        {
            "Teamwork",
            "Skill",
            "Communication",
            "Sportsmanship",
            "Leadership"
        };

        [Tooltip("Report categories")]
        [SerializeField] private List<string> _reportCategories = new()
        {
            "Toxicity",
            "AFK",
            "Griefing",
            "Cheating",
            "Harassment"
        };

        [Header("Cooldowns")]
        [Tooltip("Hours before can feedback same player again")]
        [SerializeField] private int _feedbackCooldownHours = 24;

        [Tooltip("Max commends per day")]
        [SerializeField] private int _maxCommendsPerDay = 10;

        [Tooltip("Max reports per day")]
        [SerializeField] private int _maxReportsPerDay = 5;

        #endregion

        #region Public Properties

        /// <summary>Local player's reputation data.</summary>
        public ReputationData PlayerData { get; private set; }

        /// <summary>Current reputation score.</summary>
        public int CurrentReputation => PlayerData?.Score ?? _defaultReputation;

        /// <summary>Current reputation level.</summary>
        public ReputationLevel CurrentLevel => GetReputationLevel(CurrentReputation);

        /// <summary>Whether data is loaded.</summary>
        public bool IsDataLoaded { get; private set; }

        /// <summary>Available commend categories.</summary>
        public IReadOnlyList<string> CommendCategories => _commendCategories;

        /// <summary>Available report categories.</summary>
        public IReadOnlyList<string> ReportCategories => _reportCategories;

        /// <summary>Recent feedback received.</summary>
        public IReadOnlyList<ReputationFeedback> RecentFeedback => PlayerData?.RecentFeedback ?? new List<ReputationFeedback>();

        #endregion

        #region Private Fields

        private bool _isDirty;
        private string _localPuid;
        private readonly Dictionary<string, long> _feedbackCooldowns = new();
        private int _dailyCommends;
        private int _dailyReports;
        private long _dailyResetTime;

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

        private void Start()
        {
            StartCoroutine(InitializeCoroutine());
        }

        private IEnumerator InitializeCoroutine()
        {
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _localPuid = EOSManager.Instance.LocalProductUserId?.ToString();
            _ = LoadReputationDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager", "Initialized");
        }

        private void OnDestroy()
        {
            if (_isDirty)
            {
                _ = SaveReputationDataAsync();
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Data

        /// <summary>
        /// Load reputation data from cloud storage.
        /// </summary>
        public async Task<Result> LoadReputationDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                InitializeDefaultData();
                return Result.NotConfigured;
            }

            try
            {
                var (result, data) = await storage.ReadFileAsJsonAsync<ReputationDataContainer>(REPUTATION_DATA_FILE);

                if (result == Result.Success)
                {
                    PlayerData = data.PlayerData;
                    IsDataLoaded = true;

                    EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager",
                        $"Loaded reputation: {PlayerData.Score} ({CurrentLevel})");

                    OnDataLoaded?.Invoke(PlayerData);
                    return Result.Success;
                }
                else if (result == Result.NotFound)
                {
                    InitializeDefaultData();
                    return Result.Success;
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReputationManager] Failed to load data: {e.Message}");
                InitializeDefaultData();
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Save reputation data to cloud storage.
        /// </summary>
        public async Task<Result> SaveReputationDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
                return Result.NotConfigured;

            try
            {
                var container = new ReputationDataContainer
                {
                    Version = DATA_VERSION,
                    PlayerData = PlayerData,
                    SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var result = await storage.WriteFileAsJsonAsync(REPUTATION_DATA_FILE, container);
                if (result == Result.Success)
                {
                    _isDirty = false;
                    EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager", "Saved reputation data");
                }
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReputationManager] Failed to save data: {e.Message}");
                return Result.UnexpectedError;
            }
        }

        private void InitializeDefaultData()
        {
            PlayerData = new ReputationData
            {
                Score = _defaultReputation,
                TotalCommends = 0,
                TotalReports = 0,
                RecentFeedback = new List<ReputationFeedback>(),
                CategoryCommends = new Dictionary<string, int>(),
                Version = DATA_VERSION
            };
            IsDataLoaded = true;
            _isDirty = true;
        }

        #endregion

        #region Public API - Feedback

        /// <summary>
        /// Commend a player.
        /// </summary>
        public async Task<Result> CommendPlayerAsync(string targetPuid, string category, string comment = null)
        {
            if (targetPuid == _localPuid)
            {
                return Result.InvalidParameters; // Can't commend yourself
            }

            if (!CanGiveFeedback(targetPuid, true))
            {
                EOSDebugLogger.LogWarning("EOSReputationManager",
                    "Cannot commend: cooldown or daily limit");
                return Result.LimitExceeded;
            }

            CheckDailyReset();
            if (_dailyCommends >= _maxCommendsPerDay)
            {
                return Result.LimitExceeded;
            }

            var feedback = new ReputationFeedback
            {
                Id = Guid.NewGuid().ToString(),
                FromPuid = _localPuid,
                FromName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player",
                Category = category,
                Comment = comment,
                IsPositive = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // Update cooldown
            _feedbackCooldowns[targetPuid] = DateTimeOffset.UtcNow.AddHours(_feedbackCooldownHours).ToUnixTimeMilliseconds();
            _dailyCommends++;

            // In a real implementation, this would send to the target player
            EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager",
                $"Commended player for {category}");

            return Result.Success;
        }

        /// <summary>
        /// Report a player.
        /// </summary>
        public async Task<Result> ReportPlayerAsync(string targetPuid, string category, string comment = null)
        {
            if (targetPuid == _localPuid)
            {
                return Result.InvalidParameters;
            }

            if (!CanGiveFeedback(targetPuid, false))
            {
                return Result.LimitExceeded;
            }

            CheckDailyReset();
            if (_dailyReports >= _maxReportsPerDay)
            {
                return Result.LimitExceeded;
            }

            var feedback = new ReputationFeedback
            {
                Id = Guid.NewGuid().ToString(),
                FromPuid = _localPuid,
                FromName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player",
                Category = category,
                Comment = comment,
                IsPositive = false,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _feedbackCooldowns[targetPuid] = DateTimeOffset.UtcNow.AddHours(_feedbackCooldownHours).ToUnixTimeMilliseconds();
            _dailyReports++;

            EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager",
                $"Reported player for {category}");

            return Result.Success;
        }

        /// <summary>
        /// Commend a player with host-authority validation.
        /// Use this instead of CommendPlayerAsync for secure multiplayer games.
        /// The host validates the feedback before it's delivered.
        /// </summary>
        public void CommendPlayerSecure(string targetPuid, string category, string comment = null)
        {
            var validator = Security.HostAuthorityValidator.Instance;
            if (validator != null)
            {
                validator.RequestReputationValidation(targetPuid, category, true, comment);
            }
            else
            {
                // Fallback to direct call if no validator
                EOSDebugLogger.LogWarning("EOSReputationManager",
                    "No HostAuthorityValidator found - using direct feedback (not secure)");
                _ = CommendPlayerAsync(targetPuid, category, comment);
            }
        }

        /// <summary>
        /// Report a player with host-authority validation.
        /// Use this instead of ReportPlayerAsync for secure multiplayer games.
        /// The host validates the feedback before it's delivered.
        /// </summary>
        public void ReportPlayerSecure(string targetPuid, string category, string comment = null)
        {
            var validator = Security.HostAuthorityValidator.Instance;
            if (validator != null)
            {
                validator.RequestReputationValidation(targetPuid, category, false, comment);
            }
            else
            {
                EOSDebugLogger.LogWarning("EOSReputationManager",
                    "No HostAuthorityValidator found - using direct feedback (not secure)");
                _ = ReportPlayerAsync(targetPuid, category, comment);
            }
        }

        /// <summary>
        /// Check if can give feedback to a player.
        /// </summary>
        public bool CanGiveFeedback(string targetPuid, bool isCommend)
        {
            if (targetPuid == _localPuid) return false;

            // Check cooldown
            if (_feedbackCooldowns.TryGetValue(targetPuid, out long cooldownEnd))
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < cooldownEnd)
                    return false;
            }

            // Check daily limits
            CheckDailyReset();
            if (isCommend && _dailyCommends >= _maxCommendsPerDay) return false;
            if (!isCommend && _dailyReports >= _maxReportsPerDay) return false;

            return true;
        }

        /// <summary>
        /// Get remaining daily commends.
        /// </summary>
        public int GetRemainingDailyCommends()
        {
            CheckDailyReset();
            return Math.Max(0, _maxCommendsPerDay - _dailyCommends);
        }

        /// <summary>
        /// Get remaining daily reports.
        /// </summary>
        public int GetRemainingDailyReports()
        {
            CheckDailyReset();
            return Math.Max(0, _maxReportsPerDay - _dailyReports);
        }

        #endregion

        #region Public API - Receiving Feedback (Simulated)

        /// <summary>
        /// Receive a commendation (call this when feedback arrives from another player).
        /// </summary>
        public void ReceiveCommendation(ReputationFeedback feedback)
        {
            if (!feedback.IsPositive) return;

            int oldScore = PlayerData.Score;
            PlayerData.Score = Mathf.Clamp(PlayerData.Score + _commendPoints, _minReputation, _maxReputation);
            PlayerData.TotalCommends++;

            // Track by category
            if (!string.IsNullOrEmpty(feedback.Category))
            {
                if (!PlayerData.CategoryCommends.ContainsKey(feedback.Category))
                    PlayerData.CategoryCommends[feedback.Category] = 0;
                PlayerData.CategoryCommends[feedback.Category]++;
            }

            AddToRecentFeedback(feedback);
            _isDirty = true;

            var oldLevel = GetReputationLevel(oldScore);
            var newLevel = GetReputationLevel(PlayerData.Score);

            OnReputationChanged?.Invoke(oldScore, PlayerData.Score);
            OnCommendationReceived?.Invoke(feedback);

            if (newLevel != oldLevel)
            {
                OnLevelChanged?.Invoke(newLevel);
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager",
                $"Received commendation: +{_commendPoints} ({PlayerData.Score})");
        }

        /// <summary>
        /// Receive a report (call this when feedback arrives from another player).
        /// </summary>
        public void ReceiveReport(ReputationFeedback feedback)
        {
            if (feedback.IsPositive) return;

            int oldScore = PlayerData.Score;
            PlayerData.Score = Mathf.Clamp(PlayerData.Score - _reportPenalty, _minReputation, _maxReputation);
            PlayerData.TotalReports++;

            AddToRecentFeedback(feedback);
            _isDirty = true;

            var oldLevel = GetReputationLevel(oldScore);
            var newLevel = GetReputationLevel(PlayerData.Score);

            OnReputationChanged?.Invoke(oldScore, PlayerData.Score);
            OnReportReceived?.Invoke(feedback);

            if (newLevel != oldLevel)
            {
                OnLevelChanged?.Invoke(newLevel);
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSReputationManager",
                $"Received report: -{_reportPenalty} ({PlayerData.Score})");
        }

        #endregion

        #region Public API - Queries

        /// <summary>
        /// Get reputation level for a score.
        /// </summary>
        public ReputationLevel GetReputationLevel(int score)
        {
            if (score >= 500) return ReputationLevel.Exemplary;
            if (score >= 300) return ReputationLevel.Excellent;
            if (score >= 150) return ReputationLevel.Good;
            if (score >= 50) return ReputationLevel.Neutral;
            if (score >= 0) return ReputationLevel.Caution;
            if (score >= -50) return ReputationLevel.Poor;
            return ReputationLevel.Restricted;
        }

        /// <summary>
        /// Get commend count for a category.
        /// </summary>
        public int GetCategoryCommends(string category)
        {
            if (PlayerData.CategoryCommends.TryGetValue(category, out int count))
                return count;
            return 0;
        }

        /// <summary>
        /// Get top commendation categories.
        /// </summary>
        public List<(string category, int count)> GetTopCategories(int count = 3)
        {
            return PlayerData.CategoryCommends
                .OrderByDescending(kvp => kvp.Value)
                .Take(count)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        /// <summary>
        /// Get progress to next level.
        /// </summary>
        public (int current, int needed, float progress) GetLevelProgress()
        {
            var level = CurrentLevel;
            int currentThreshold = GetLevelThreshold(level);
            int nextThreshold = GetLevelThreshold(level + 1);

            if (nextThreshold == int.MaxValue)
            {
                return (PlayerData.Score, PlayerData.Score, 1f);
            }

            int needed = nextThreshold - currentThreshold;
            int progress = PlayerData.Score - currentThreshold;
            float percent = needed > 0 ? (float)progress / needed : 1f;

            return (progress, needed, Mathf.Clamp01(percent));
        }

        #endregion

        #region Public API - Display

        /// <summary>
        /// Get display name for a reputation level.
        /// </summary>
        public static string GetLevelName(ReputationLevel level)
        {
            return level switch
            {
                ReputationLevel.Exemplary => "Exemplary",
                ReputationLevel.Excellent => "Excellent",
                ReputationLevel.Good => "Good",
                ReputationLevel.Neutral => "Neutral",
                ReputationLevel.Caution => "Caution",
                ReputationLevel.Poor => "Poor",
                ReputationLevel.Restricted => "Restricted",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get icon/color indicator for level.
        /// </summary>
        public static string GetLevelIcon(ReputationLevel level)
        {
            return level switch
            {
                ReputationLevel.Exemplary => "++",
                ReputationLevel.Excellent => "+",
                ReputationLevel.Good => "o",
                ReputationLevel.Neutral => "~",
                ReputationLevel.Caution => "-",
                ReputationLevel.Poor => "--",
                ReputationLevel.Restricted => "X",
                _ => "?"
            };
        }

        /// <summary>
        /// Get formatted reputation string.
        /// </summary>
        public string GetFormattedReputation()
        {
            var level = CurrentLevel;
            return $"[{GetLevelIcon(level)}] {GetLevelName(level)} ({PlayerData.Score})";
        }

        #endregion

        #region Private Methods

        private void CheckDailyReset()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Reset at midnight UTC
            var today = DateTime.UtcNow.Date;
            long todayMs = new DateTimeOffset(today, TimeSpan.Zero).ToUnixTimeMilliseconds();

            if (_dailyResetTime < todayMs)
            {
                _dailyCommends = 0;
                _dailyReports = 0;
                _dailyResetTime = todayMs;
            }
        }

        private void AddToRecentFeedback(ReputationFeedback feedback)
        {
            PlayerData.RecentFeedback.Insert(0, feedback);
            while (PlayerData.RecentFeedback.Count > MAX_RECENT_FEEDBACK)
            {
                PlayerData.RecentFeedback.RemoveAt(PlayerData.RecentFeedback.Count - 1);
            }
        }

        private int GetLevelThreshold(ReputationLevel level)
        {
            return level switch
            {
                ReputationLevel.Exemplary => 500,
                ReputationLevel.Excellent => 300,
                ReputationLevel.Good => 150,
                ReputationLevel.Neutral => 50,
                ReputationLevel.Caution => 0,
                ReputationLevel.Poor => -50,
                ReputationLevel.Restricted => _minReputation,
                _ => int.MaxValue
            };
        }

        #endregion
    }

    #region Data Types

    /// <summary>
    /// Reputation levels.
    /// </summary>
    public enum ReputationLevel
    {
        Restricted = 0,
        Poor = 1,
        Caution = 2,
        Neutral = 3,
        Good = 4,
        Excellent = 5,
        Exemplary = 6
    }

    /// <summary>
    /// Feedback record.
    /// </summary>
    [Serializable]
    public class ReputationFeedback
    {
        public string Id;
        public string FromPuid;
        public string FromName;
        public string Category;
        public string Comment;
        public bool IsPositive;
        public long Timestamp;
    }

    /// <summary>
    /// Player reputation data.
    /// </summary>
    [Serializable]
    public class ReputationData
    {
        public int Score;
        public int TotalCommends;
        public int TotalReports;
        public List<ReputationFeedback> RecentFeedback;
        public Dictionary<string, int> CategoryCommends;
        public int Version;

        public float CommendRatio => TotalCommends + TotalReports > 0
            ? (float)TotalCommends / (TotalCommends + TotalReports)
            : 1f;
    }

    /// <summary>
    /// Cloud storage container.
    /// </summary>
    [Serializable]
    public class ReputationDataContainer
    {
        public int Version;
        public ReputationData PlayerData;
        public long SavedAt;
    }

    #endregion
}
