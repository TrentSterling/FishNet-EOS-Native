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
    /// Manages competitive seasons with soft resets and rewards.
    /// </summary>
    public class EOSSeasonManager : MonoBehaviour
    {
        #region Singleton

        private static EOSSeasonManager _instance;
        public static EOSSeasonManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSSeasonManager>();
#else
                    _instance = FindObjectOfType<EOSSeasonManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string SEASON_DATA_FILE = "season_data.json";
        private const int DATA_VERSION = 1;

        #endregion

        #region Events

        /// <summary>Fired when a new season starts.</summary>
        public event Action<Season> OnSeasonStarted;

        /// <summary>Fired when current season ends.</summary>
        public event Action<Season, SeasonRewards> OnSeasonEnded;

        /// <summary>Fired when player's rating is reset for new season.</summary>
        public event Action<int, int> OnSeasonReset; // oldRating, newRating

        /// <summary>Fired when rewards are claimed.</summary>
        public event Action<SeasonRewards> OnRewardsClaimed;

        /// <summary>Fired when season data is loaded.</summary>
        public event Action<SeasonPlayerData> OnDataLoaded;

        /// <summary>Fired when time until season end updates (every minute).</summary>
        public event Action<TimeSpan> OnTimeRemainingUpdated;

        #endregion

        #region Inspector Settings

        [Header("Season Configuration")]
        [Tooltip("Duration of each season in days")]
        [SerializeField] private int _seasonDurationDays = 90;

        [Tooltip("Percentage of rating to keep after soft reset (0-1)")]
        [SerializeField, Range(0f, 1f)] private float _softResetPercentage = 0.5f;

        [Tooltip("Target rating for soft reset calculation")]
        [SerializeField] private int _softResetTarget = 1200;

        [Tooltip("Minimum rating floor after reset")]
        [SerializeField] private int _minimumResetRating = 800;

        [Tooltip("Award rewards based on peak or final rating")]
        [SerializeField] private bool _usepeakRatingForRewards = true;

        [Header("Auto Season Management")]
        [Tooltip("Automatically start new season when current ends")]
        [SerializeField] private bool _autoStartNewSeason = true;

        [Tooltip("Check for season transitions periodically")]
        [SerializeField] private bool _autoCheckSeasonEnd = true;

        [Tooltip("How often to check for season end (seconds)")]
        [SerializeField] private float _seasonCheckInterval = 60f;

        [Header("Rewards Configuration")]
        [Tooltip("Enable season rewards")]
        [SerializeField] private bool _enableRewards = true;

        #endregion

        #region Public Properties

        /// <summary>Current active season.</summary>
        public Season CurrentSeason { get; private set; }

        /// <summary>Player's season data.</summary>
        public SeasonPlayerData PlayerData { get; private set; }

        /// <summary>Whether season data is loaded.</summary>
        public bool IsDataLoaded { get; private set; }

        /// <summary>Whether currently in an active season.</summary>
        public bool IsSeasonActive => CurrentSeason != null &&
            DateTime.UtcNow >= CurrentSeason.StartDate &&
            DateTime.UtcNow < CurrentSeason.EndDate;

        /// <summary>Time remaining in current season.</summary>
        public TimeSpan TimeRemaining => CurrentSeason != null
            ? CurrentSeason.EndDate - DateTime.UtcNow
            : TimeSpan.Zero;

        /// <summary>Days remaining in current season.</summary>
        public int DaysRemaining => (int)TimeRemaining.TotalDays;

        /// <summary>Season progress (0-1).</summary>
        public float SeasonProgress
        {
            get
            {
                if (CurrentSeason == null) return 0f;
                var total = (CurrentSeason.EndDate - CurrentSeason.StartDate).TotalSeconds;
                var elapsed = (DateTime.UtcNow - CurrentSeason.StartDate).TotalSeconds;
                return Mathf.Clamp01((float)(elapsed / total));
            }
        }

        /// <summary>Rewards earned but not yet claimed.</summary>
        public SeasonRewards PendingRewards => PlayerData.PendingRewards;

        /// <summary>Has unclaimed rewards.</summary>
        public bool HasPendingRewards => PlayerData.PendingRewards != null && !PlayerData.PendingRewards.Claimed;

        /// <summary>Player's season history.</summary>
        public IReadOnlyList<SeasonRecord> SeasonHistory => PlayerData.SeasonHistory;

        #endregion

        #region Private Fields

        private bool _isDirty;
        private Coroutine _seasonCheckCoroutine;

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
            // Wait for EOS login
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            // Load data
            _ = LoadSeasonDataAsync();

            // Start periodic season check
            if (_autoCheckSeasonEnd)
            {
                _seasonCheckCoroutine = StartCoroutine(SeasonCheckLoop());
            }

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager", "Initialized");
        }

        private void OnDestroy()
        {
            if (_isDirty)
            {
                _ = SaveSeasonDataAsync();
            }

            if (_seasonCheckCoroutine != null)
            {
                StopCoroutine(_seasonCheckCoroutine);
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Data

        /// <summary>
        /// Load season data from cloud storage.
        /// </summary>
        public async Task<Result> LoadSeasonDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                InitializeDefaultData();
                return Result.NotConfigured;
            }

            try
            {
                var (result, data) = await storage.ReadFileAsJsonAsync<SeasonDataContainer>(SEASON_DATA_FILE);

                if (result == Result.Success)
                {
                    PlayerData = data.PlayerData;
                    CurrentSeason = data.CurrentSeason;
                    IsDataLoaded = true;

                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager",
                        $"Loaded season data: Season {CurrentSeason?.Number ?? 0}");

                    // Check if season has ended since last play
                    if (CurrentSeason != null && DateTime.UtcNow >= CurrentSeason.EndDate)
                    {
                        await HandleSeasonEndAsync();
                    }

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
                Debug.LogWarning($"[EOSSeasonManager] Failed to load data: {e.Message}");
                InitializeDefaultData();
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Save season data to cloud storage.
        /// </summary>
        public async Task<Result> SaveSeasonDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
                return Result.NotConfigured;

            try
            {
                var container = new SeasonDataContainer
                {
                    Version = DATA_VERSION,
                    PlayerData = PlayerData,
                    CurrentSeason = CurrentSeason,
                    SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var result = await storage.WriteFileAsJsonAsync(SEASON_DATA_FILE, container);
                if (result == Result.Success)
                {
                    _isDirty = false;
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager", "Saved season data");
                }
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSSeasonManager] Failed to save data: {e.Message}");
                return Result.UnexpectedError;
            }
        }

        private void InitializeDefaultData()
        {
            PlayerData = new SeasonPlayerData
            {
                SeasonHistory = new List<SeasonRecord>(),
                Version = DATA_VERSION
            };

            // Create first season if none exists
            if (CurrentSeason == null)
            {
                CurrentSeason = CreateNewSeason(1);
            }

            IsDataLoaded = true;
            _isDirty = true;
        }

        #endregion

        #region Public API - Season Management

        /// <summary>
        /// Start a new season manually.
        /// </summary>
        public async Task<Season> StartNewSeasonAsync(int? seasonNumber = null)
        {
            // End current season if active
            if (CurrentSeason != null && IsSeasonActive)
            {
                await HandleSeasonEndAsync();
            }

            int number = seasonNumber ?? (CurrentSeason?.Number ?? 0) + 1;
            CurrentSeason = CreateNewSeason(number);

            // Reset player stats for new season
            await PerformSoftResetAsync();

            _isDirty = true;
            await SaveSeasonDataAsync();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager",
                $"Started Season {CurrentSeason.Number}");

            OnSeasonStarted?.Invoke(CurrentSeason);
            return CurrentSeason;
        }

        /// <summary>
        /// Get current season info.
        /// </summary>
        public Season GetCurrentSeason()
        {
            return CurrentSeason;
        }

        /// <summary>
        /// Get season by number from history.
        /// </summary>
        public SeasonRecord GetSeasonRecord(int seasonNumber)
        {
            return PlayerData.SeasonHistory.FirstOrDefault(s => s.SeasonNumber == seasonNumber);
        }

        /// <summary>
        /// Check if a rating qualifies for a reward tier.
        /// </summary>
        public SeasonRewardTier GetRewardTier(int rating)
        {
            if (rating >= 2200) return SeasonRewardTier.Champion;
            if (rating >= 1900) return SeasonRewardTier.Diamond;
            if (rating >= 1600) return SeasonRewardTier.Platinum;
            if (rating >= 1300) return SeasonRewardTier.Gold;
            if (rating >= 1000) return SeasonRewardTier.Silver;
            if (rating >= 700) return SeasonRewardTier.Bronze;
            return SeasonRewardTier.None;
        }

        /// <summary>
        /// Preview what rating will be after soft reset.
        /// </summary>
        public int PreviewResetRating(int currentRating)
        {
            return CalculateSoftReset(currentRating);
        }

        #endregion

        #region Public API - Rewards

        /// <summary>
        /// Calculate rewards for current season standing.
        /// </summary>
        public SeasonRewards CalculateCurrentRewards()
        {
            if (CurrentSeason == null) return null;

            var ranked = EOSRankedMatchmaking.Instance;
            if (ranked == null || !ranked.IsDataLoaded) return null;

            var rating = _usepeakRatingForRewards
                ? ranked.PlayerData.PeakRating
                : ranked.PlayerData.Rating;

            return new SeasonRewards
            {
                SeasonNumber = CurrentSeason.Number,
                FinalRating = ranked.PlayerData.Rating,
                PeakRating = ranked.PlayerData.PeakRating,
                RewardTier = GetRewardTier(rating),
                GamesPlayed = ranked.PlayerData.GamesPlayed,
                Wins = ranked.PlayerData.Wins,
                WinRate = ranked.PlayerData.WinRate,
                Claimed = false
            };
        }

        /// <summary>
        /// Claim pending rewards.
        /// </summary>
        public async Task<bool> ClaimRewardsAsync()
        {
            if (!HasPendingRewards) return false;

            var rewards = PlayerData.PendingRewards;
            rewards.Claimed = true;
            rewards.ClaimedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            PlayerData.PendingRewards = rewards;

            _isDirty = true;
            await SaveSeasonDataAsync();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager",
                $"Claimed rewards: {rewards.RewardTier}");

            OnRewardsClaimed?.Invoke(rewards);
            return true;
        }

        /// <summary>
        /// Get reward description for a tier.
        /// </summary>
        public static string GetRewardDescription(SeasonRewardTier tier)
        {
            return tier switch
            {
                SeasonRewardTier.Champion => "Champion Border, Exclusive Title, 1000 Points",
                SeasonRewardTier.Diamond => "Diamond Border, Title, 500 Points",
                SeasonRewardTier.Platinum => "Platinum Border, Title, 300 Points",
                SeasonRewardTier.Gold => "Gold Border, 200 Points",
                SeasonRewardTier.Silver => "Silver Border, 100 Points",
                SeasonRewardTier.Bronze => "Bronze Border, 50 Points",
                _ => "No Rewards"
            };
        }

        /// <summary>
        /// Get icon for reward tier.
        /// </summary>
        public static string GetRewardTierIcon(SeasonRewardTier tier)
        {
            return tier switch
            {
                SeasonRewardTier.Champion => "Ch",
                SeasonRewardTier.Diamond => "Di",
                SeasonRewardTier.Platinum => "Pt",
                SeasonRewardTier.Gold => "Au",
                SeasonRewardTier.Silver => "Ag",
                SeasonRewardTier.Bronze => "Cu",
                _ => "--"
            };
        }

        #endregion

        #region Public API - Display

        /// <summary>
        /// Get formatted time remaining string.
        /// </summary>
        public string GetTimeRemainingString()
        {
            var time = TimeRemaining;
            if (time.TotalDays >= 1)
                return $"{(int)time.TotalDays}d {time.Hours}h";
            if (time.TotalHours >= 1)
                return $"{(int)time.TotalHours}h {time.Minutes}m";
            return $"{time.Minutes}m";
        }

        /// <summary>
        /// Get season display name.
        /// </summary>
        public string GetSeasonDisplayName()
        {
            if (CurrentSeason == null) return "Off-Season";
            return CurrentSeason.Name ?? $"Season {CurrentSeason.Number}";
        }

        #endregion

        #region Private Methods - Season Logic

        private Season CreateNewSeason(int number)
        {
            var startDate = DateTime.UtcNow;
            return new Season
            {
                Number = number,
                Name = $"Season {number}",
                StartDate = startDate,
                EndDate = startDate.AddDays(_seasonDurationDays),
                IsActive = true
            };
        }

        private async Task HandleSeasonEndAsync()
        {
            if (CurrentSeason == null) return;

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager",
                $"Season {CurrentSeason.Number} ended");

            // Calculate and store rewards
            var rewards = CalculateCurrentRewards();
            if (rewards != null && _enableRewards)
            {
                PlayerData.PendingRewards = rewards;
            }

            // Create season record
            var ranked = EOSRankedMatchmaking.Instance;
            var record = new SeasonRecord
            {
                SeasonNumber = CurrentSeason.Number,
                SeasonName = CurrentSeason.Name,
                StartDate = CurrentSeason.StartDate,
                EndDate = CurrentSeason.EndDate,
                FinalRating = ranked?.PlayerData.Rating ?? 0,
                PeakRating = ranked?.PlayerData.PeakRating ?? 0,
                GamesPlayed = ranked?.PlayerData.GamesPlayed ?? 0,
                Wins = ranked?.PlayerData.Wins ?? 0,
                RewardTier = rewards?.RewardTier ?? SeasonRewardTier.None
            };

            PlayerData.SeasonHistory.Add(record);

            // Notify end
            OnSeasonEnded?.Invoke(CurrentSeason, rewards);

            // Mark season inactive
            CurrentSeason.IsActive = false;

            // Auto-start new season if configured
            if (_autoStartNewSeason)
            {
                await StartNewSeasonAsync();
            }
        }

        private async Task PerformSoftResetAsync()
        {
            var ranked = EOSRankedMatchmaking.Instance;
            if (ranked == null || !ranked.IsDataLoaded) return;

            int oldRating = ranked.PlayerData.Rating;
            int newRating = CalculateSoftReset(oldRating);

            // Update ranked data through the ranked manager
            // This would require adding a method to EOSRankedMatchmaking
            // For now, we'll store the reset info and let ranked system handle it
            PlayerData.LastResetRating = oldRating;
            PlayerData.ResetToRating = newRating;
            PlayerData.LastResetTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSSeasonManager",
                $"Soft reset: {oldRating} -> {newRating}");

            OnSeasonReset?.Invoke(oldRating, newRating);

            // Reset placement status for new season
            // This would need integration with EOSRankedMatchmaking
        }

        private int CalculateSoftReset(int rating)
        {
            // Soft reset formula: newRating = target + (oldRating - target) * percentage
            // This pulls everyone toward the target rating
            int resetRating = _softResetTarget + (int)((rating - _softResetTarget) * _softResetPercentage);
            return Mathf.Max(_minimumResetRating, resetRating);
        }

        private IEnumerator SeasonCheckLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(_seasonCheckInterval);

                if (CurrentSeason != null)
                {
                    OnTimeRemainingUpdated?.Invoke(TimeRemaining);

                    // Check if season ended
                    if (DateTime.UtcNow >= CurrentSeason.EndDate)
                    {
                        var task = HandleSeasonEndAsync();
                        yield return new WaitUntil(() => task.IsCompleted);
                    }
                }
            }
        }

        #endregion
    }

    #region Data Types

    /// <summary>
    /// Season definition.
    /// </summary>
    [Serializable]
    public class Season
    {
        public int Number;
        public string Name;
        public DateTime StartDate;
        public DateTime EndDate;
        public bool IsActive;
        public string Theme; // Optional theme name
        public Dictionary<string, string> Metadata; // Custom data
    }

    /// <summary>
    /// Player's season-specific data.
    /// </summary>
    [Serializable]
    public class SeasonPlayerData
    {
        public List<SeasonRecord> SeasonHistory;
        public SeasonRewards PendingRewards;
        public int LastResetRating;
        public int ResetToRating;
        public long LastResetTime;
        public int Version;
    }

    /// <summary>
    /// Historical record of a completed season.
    /// </summary>
    [Serializable]
    public class SeasonRecord
    {
        public int SeasonNumber;
        public string SeasonName;
        public DateTime StartDate;
        public DateTime EndDate;
        public int FinalRating;
        public int PeakRating;
        public int GamesPlayed;
        public int Wins;
        public SeasonRewardTier RewardTier;

        public float WinRate => GamesPlayed > 0 ? (Wins * 100f / GamesPlayed) : 0f;
    }

    /// <summary>
    /// Rewards earned from a season.
    /// </summary>
    [Serializable]
    public class SeasonRewards
    {
        public int SeasonNumber;
        public int FinalRating;
        public int PeakRating;
        public SeasonRewardTier RewardTier;
        public int GamesPlayed;
        public int Wins;
        public float WinRate;
        public bool Claimed;
        public long ClaimedAt;
    }

    /// <summary>
    /// Season reward tiers.
    /// </summary>
    public enum SeasonRewardTier
    {
        None = 0,
        Bronze = 1,
        Silver = 2,
        Gold = 3,
        Platinum = 4,
        Diamond = 5,
        Champion = 6
    }

    /// <summary>
    /// Container for cloud storage.
    /// </summary>
    [Serializable]
    public class SeasonDataContainer
    {
        public int Version;
        public SeasonPlayerData PlayerData;
        public Season CurrentSeason;
        public long SavedAt;
    }

    #endregion
}
