using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Ranked/Skill-based matchmaking system.
    /// Supports ELO, Glicko-2, and Simple MMR algorithms.
    /// Persists ratings to EOS cloud storage.
    /// </summary>
    public class EOSRankedMatchmaking : MonoBehaviour
    {
        #region Singleton

        private static EOSRankedMatchmaking _instance;
        public static EOSRankedMatchmaking Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSRankedMatchmaking>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSRankedMatchmaking");
                        _instance = go.AddComponent<EOSRankedMatchmaking>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string RANKED_DATA_FILE = "ranked_data.json";
        private const int DATA_VERSION = 1;

        // Glicko-2 constants
        private const float GLICKO2_TAU = 0.5f; // System constant (constrains volatility changes)
        private const float GLICKO2_CONVERGENCE = 0.000001f;

        #endregion

        #region Serialized Fields

        [Header("Rating Algorithm")]
        [SerializeField] private RatingAlgorithm _algorithm = RatingAlgorithm.ELO;
        [SerializeField] private int _defaultRating = 1200;
        [SerializeField] private int _minRating = 0;
        [SerializeField] private int _maxRating = 3000;

        [Header("ELO Settings")]
        [SerializeField] private int _kFactorNew = 40;
        [SerializeField] private int _kFactorStandard = 32;
        [SerializeField] private int _kFactorEstablished = 24;
        [SerializeField] private int _provisionalGames = 30;
        [SerializeField] private int _establishedGames = 100;

        [Header("Simple MMR Settings")]
        [SerializeField] private int _winPoints = 25;
        [SerializeField] private int _lossPoints = 20;
        [SerializeField] private int _streakBonusPerWin = 2;
        [SerializeField] private int _maxStreakBonus = 10;
        [SerializeField] private int _streakPenaltyPerLoss = 2;
        [SerializeField] private int _maxStreakPenalty = 6;

        [Header("Glicko-2 Settings")]
        [SerializeField] private float _defaultRD = 350f;
        [SerializeField] private float _defaultVolatility = 0.06f;
        [SerializeField] private float _rdIncreasePerDay = 5f;
        [SerializeField] private float _maxRD = 350f;

        [Header("Tier Display")]
        [SerializeField] private TierDisplayMode _tierDisplayMode = TierDisplayMode.SixTier;

        [Header("Matchmaking")]
        [SerializeField] private QueueMode _queueMode = QueueMode.SearchJoin;
        [SerializeField] private int _initialSkillRange = 200;
        [SerializeField] private int _skillRangeExpansion = 50;
        [SerializeField] private int _maxSkillRange = 1000;
        [SerializeField] private int _maxSearchAttempts = 5;
        [SerializeField] private float _searchRetryDelay = 2f;
        [SerializeField] private float _queuePollInterval = 5f;
        [SerializeField] private uint _rankedLobbySize = 8;

        [Header("Placement")]
        [SerializeField] private int _placementGamesRequired = 10;
        [SerializeField] private bool _hideTierDuringPlacement = true;

        #endregion

        #region Events

        /// <summary>Fired when rating changes after a match.</summary>
        public event Action<RatingChange> OnRatingChanged;

        /// <summary>Fired on tier promotion.</summary>
        public event Action<RankTier, RankDivision> OnPromotion;

        /// <summary>Fired on tier demotion.</summary>
        public event Action<RankTier, RankDivision> OnDemotion;

        /// <summary>Fired when placement is completed.</summary>
        public event Action<int, RankTier, RankDivision> OnPlacementCompleted;

        /// <summary>Fired when entering matchmaking queue.</summary>
        public event Action OnQueueEntered;

        /// <summary>Fired when leaving matchmaking queue.</summary>
        public event Action OnQueueLeft;

        /// <summary>Fired with queue time updates.</summary>
        public event Action<float> OnQueueTimeUpdated;

        /// <summary>Fired when a ranked match is found.</summary>
        public event Action<LobbyData> OnMatchFound;

        /// <summary>Fired during search with current attempt number.</summary>
        public event Action<int> OnSearchAttempt;

        /// <summary>Fired when skill range expands during search.</summary>
        public event Action<int> OnSkillRangeExpanded;

        /// <summary>Fired when ranked data is loaded from cloud.</summary>
        public event Action<RankedPlayerData> OnDataLoaded;

        #endregion

        #region Private Fields

        private RankedPlayerData _playerData;
        private bool _dataLoaded;
        private bool _isDirty;
        private bool _inQueue;
        private QueueEntry _localQueueEntry;
        private float _queueStartTime;
        private Coroutine _queueCoroutine;

        #endregion

        #region Public Properties

        /// <summary>Current rating algorithm.</summary>
        public RatingAlgorithm Algorithm => _algorithm;

        /// <summary>Current queue mode.</summary>
        public QueueMode QueueMode => _queueMode;

        /// <summary>Current tier display mode.</summary>
        public TierDisplayMode TierDisplay => _tierDisplayMode;

        /// <summary>Local player's ranked data.</summary>
        public RankedPlayerData PlayerData => _playerData;

        /// <summary>Whether ranked data has been loaded.</summary>
        public bool IsDataLoaded => _dataLoaded;

        /// <summary>Whether player is in matchmaking queue.</summary>
        public bool IsInQueue => _inQueue;

        /// <summary>Current queue time in seconds.</summary>
        public float QueueTime => _inQueue ? Time.realtimeSinceStartup - _queueStartTime : 0f;

        /// <summary>Whether player has completed placement matches.</summary>
        public bool IsPlaced => _playerData.IsPlaced;

        /// <summary>Current rating.</summary>
        public int CurrentRating => _playerData.Rating;

        /// <summary>Current tier.</summary>
        public RankTier CurrentTier => GetTier(_playerData.Rating);

        /// <summary>Current division.</summary>
        public RankDivision CurrentDivision => GetDivision(_playerData.Rating);

        /// <summary>Default rating for new players.</summary>
        public int DefaultRating => _defaultRating;

        #endregion

        #region Tier Thresholds

        private static readonly (RankTier tier, int minRating)[] SixTierThresholds =
        {
            (RankTier.Champion, 2200),
            (RankTier.Diamond, 1900),
            (RankTier.Platinum, 1600),
            (RankTier.Gold, 1300),
            (RankTier.Silver, 1000),
            (RankTier.Bronze, 0)
        };

        private static readonly (RankTier tier, int minRating)[] EightTierThresholds =
        {
            (RankTier.Grandmaster, 2400),
            (RankTier.Master, 2100),
            (RankTier.Diamond, 1800),
            (RankTier.Platinum, 1500),
            (RankTier.Gold, 1200),
            (RankTier.Silver, 900),
            (RankTier.Bronze, 600),
            (RankTier.Iron, 0)
        };

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

        private IEnumerator InitializeCoroutine()
        {
            // Wait for EOS login
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            // Load ranked data
            _ = LoadPlayerDataAsync();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking", "Initialized");
        }

        private void OnDestroy()
        {
            if (_isDirty)
            {
                _ = SavePlayerDataAsync();
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Data

        /// <summary>
        /// Load player ranked data from cloud storage.
        /// </summary>
        public async Task<Result> LoadPlayerDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                // Initialize with defaults
                InitializeDefaultData();
                return Result.NotConfigured;
            }

            try
            {
                var (result, data) = await storage.ReadFileAsJsonAsync<RankedDataContainer>(RANKED_DATA_FILE);

                if (result == Result.Success)
                {
                    _playerData = data.PlayerData;

                    // Handle Glicko-2 RD decay for inactivity
                    if (_algorithm == RatingAlgorithm.Glicko2 && _playerData.DaysSinceLastMatch > 0)
                    {
                        _playerData.RatingDeviation = UpdateRDForInactivity(
                            _playerData.RatingDeviation,
                            _playerData.DaysSinceLastMatch);
                    }

                    _dataLoaded = true;
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                        $"Loaded ranked data: {_playerData.Rating} ({_playerData.GamesPlayed} games)");
                    OnDataLoaded?.Invoke(_playerData);
                    return Result.Success;
                }
                else if (result == Result.NotFound)
                {
                    // New player - initialize defaults
                    InitializeDefaultData();
                    return Result.Success;
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSRankedMatchmaking] Failed to load data: {e.Message}");
                InitializeDefaultData();
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Save player ranked data to cloud storage.
        /// </summary>
        public async Task<Result> SavePlayerDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
                return Result.NotConfigured;

            try
            {
                var container = new RankedDataContainer
                {
                    Version = DATA_VERSION,
                    PlayerData = _playerData,
                    SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var result = await storage.WriteFileAsJsonAsync(RANKED_DATA_FILE, container);
                if (result == Result.Success)
                {
                    _isDirty = false;
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking", "Saved ranked data");
                }
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSRankedMatchmaking] Failed to save data: {e.Message}");
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Reset ranked data to defaults (for testing).
        /// </summary>
        public async Task<Result> ResetDataAsync()
        {
            InitializeDefaultData();
            return await SavePlayerDataAsync();
        }

        private void InitializeDefaultData()
        {
            _playerData = new RankedPlayerData
            {
                Rating = _defaultRating,
                PeakRating = _defaultRating,
                RatingDeviation = _defaultRD,
                Volatility = _defaultVolatility,
                Version = DATA_VERSION
            };
            _dataLoaded = true;
            _isDirty = true;
        }

        #endregion

        #region Public API - Rating Calculations

        /// <summary>
        /// Get the local player's current rating.
        /// </summary>
        public int GetLocalPlayerRating()
        {
            return _playerData.Rating;
        }

        /// <summary>
        /// Calculate rating change for a match result (preview only, does not apply).
        /// </summary>
        public int PreviewRatingChange(int opponentRating, bool won)
        {
            return _algorithm switch
            {
                RatingAlgorithm.ELO => CalculateEloChange(_playerData.Rating, opponentRating, won, _playerData.GamesPlayed),
                RatingAlgorithm.Glicko2 => CalculateGlicko2ChangeSimple(_playerData.Rating, _playerData.RatingDeviation, opponentRating, won),
                RatingAlgorithm.SimpleMMR => CalculateSimpleMMRChange(won, _playerData.WinStreak, _playerData.LossStreak),
                _ => won ? 25 : -25
            };
        }

        /// <summary>
        /// Record a match result and update rating.
        /// </summary>
        public async Task<RatingChange> RecordMatchResultAsync(MatchOutcome outcome, int opponentRating, int teamSize = 1)
        {
            if (outcome == MatchOutcome.InProgress || outcome == MatchOutcome.Abandoned)
            {
                return default;
            }

            var oldRating = _playerData.Rating;
            var oldTier = GetTier(oldRating);
            var oldDivision = GetDivision(oldRating);

            bool won = outcome == MatchOutcome.Win;
            bool draw = outcome == MatchOutcome.Draw;

            int change = 0;

            if (!draw)
            {
                // Calculate rating change based on algorithm
                change = _algorithm switch
                {
                    RatingAlgorithm.ELO => CalculateEloChange(_playerData.Rating, opponentRating, won, _playerData.GamesPlayed),
                    RatingAlgorithm.Glicko2 => CalculateGlicko2ChangeSimple(_playerData.Rating, _playerData.RatingDeviation, opponentRating, won),
                    RatingAlgorithm.SimpleMMR => CalculateSimpleMMRChange(won, _playerData.WinStreak, _playerData.LossStreak),
                    _ => won ? 25 : -25
                };

                // Apply change
                _playerData.Rating = Mathf.Clamp(_playerData.Rating + change, _minRating, _maxRating);
            }

            // Update stats
            _playerData.GamesPlayed++;

            if (won)
            {
                _playerData.Wins++;
                _playerData.WinStreak++;
                _playerData.LossStreak = 0;
            }
            else if (!draw)
            {
                _playerData.Losses++;
                _playerData.LossStreak++;
                _playerData.WinStreak = 0;
            }
            else
            {
                // Draw - reset streaks
                _playerData.WinStreak = 0;
                _playerData.LossStreak = 0;
            }

            // Update peak rating
            if (_playerData.Rating > _playerData.PeakRating)
            {
                _playerData.PeakRating = _playerData.Rating;
            }

            _playerData.LastMatchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Check placement completion
            if (!_playerData.IsPlaced)
            {
                _playerData.PlacementGamesPlayed++;
                if (won) _playerData.PlacementWins++;

                if (_playerData.PlacementGamesPlayed >= _placementGamesRequired)
                {
                    _playerData.IsPlaced = true;
                    var placedTier = GetTier(_playerData.Rating);
                    var placedDivision = GetDivision(_playerData.Rating);
                    OnPlacementCompleted?.Invoke(_playerData.Rating, placedTier, placedDivision);
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                        $"Placement complete: {_playerData.Rating} ({placedTier} {placedDivision})");
                }
            }

            // Update Glicko-2 specific fields
            if (_algorithm == RatingAlgorithm.Glicko2)
            {
                // Reduce RD slightly after each game (confidence increases)
                _playerData.RatingDeviation = Mathf.Max(30f, _playerData.RatingDeviation * 0.95f);
            }

            _isDirty = true;

            // Save to cloud
            await SavePlayerDataAsync();

            // Update EOS stats for leaderboard
            var statsManager = EOSStats.Instance;
            if (statsManager != null && statsManager.IsReady)
            {
                await statsManager.IngestStatsAsync(
                    ("ranked_rating", _playerData.Rating),
                    ("ranked_games", _playerData.GamesPlayed),
                    ("ranked_wins", _playerData.Wins)
                );
            }

            var newTier = GetTier(_playerData.Rating);
            var newDivision = GetDivision(_playerData.Rating);

            var result = new RatingChange
            {
                OldRating = oldRating,
                NewRating = _playerData.Rating,
                Change = change,
                OldTier = oldTier,
                NewTier = newTier,
                OldDivision = oldDivision,
                NewDivision = newDivision,
                IsPromotion = newTier > oldTier || (newTier == oldTier && newDivision < oldDivision),
                IsDemotion = newTier < oldTier || (newTier == oldTier && newDivision > oldDivision)
            };

            OnRatingChanged?.Invoke(result);

            if (result.IsPromotion)
            {
                OnPromotion?.Invoke(newTier, newDivision);
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                    $"Promoted to {newTier} {newDivision}!");
            }
            else if (result.IsDemotion)
            {
                OnDemotion?.Invoke(newTier, newDivision);
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                    $"Demoted to {newTier} {newDivision}");
            }

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                $"Match result: {outcome} vs {opponentRating} -> {result}");

            return result;
        }

        #endregion

        #region Public API - Tier Display

        /// <summary>
        /// Get the rank tier for a rating.
        /// </summary>
        public RankTier GetTier(int rating)
        {
            if (!_playerData.IsPlaced && _hideTierDuringPlacement)
                return RankTier.Unranked;

            var thresholds = _tierDisplayMode == TierDisplayMode.EightTier ? EightTierThresholds : SixTierThresholds;

            foreach (var (tier, minRating) in thresholds)
            {
                if (rating >= minRating)
                    return tier;
            }

            return thresholds[^1].tier;
        }

        /// <summary>
        /// Get the division within a tier.
        /// </summary>
        public RankDivision GetDivision(int rating)
        {
            var thresholds = _tierDisplayMode == TierDisplayMode.EightTier ? EightTierThresholds : SixTierThresholds;
            int tierMinRating = 0;

            foreach (var (tier, minRating) in thresholds)
            {
                if (rating >= minRating)
                {
                    tierMinRating = minRating;
                    break;
                }
            }

            int inTier = rating - tierMinRating;
            if (inTier >= 300) return RankDivision.I;
            if (inTier >= 200) return RankDivision.II;
            if (inTier >= 100) return RankDivision.III;
            return RankDivision.IV;
        }

        /// <summary>
        /// Get tier and division together.
        /// </summary>
        public (RankTier tier, RankDivision division) GetTierAndDivision(int rating)
        {
            return (GetTier(rating), GetDivision(rating));
        }

        /// <summary>
        /// Get display name for current rank (e.g., "Gold II" or "1450").
        /// </summary>
        public string GetRankDisplayName(int rating)
        {
            if (_tierDisplayMode == TierDisplayMode.NumbersOnly)
                return rating.ToString();

            if (!_playerData.IsPlaced && _hideTierDuringPlacement)
                return $"Placement ({_playerData.PlacementGamesPlayed}/{_placementGamesRequired})";

            var tier = GetTier(rating);
            var division = GetDivision(rating);
            return $"{GetTierName(tier)} {GetDivisionName(division)}";
        }

        /// <summary>
        /// Get display name for local player's current rank.
        /// </summary>
        public string GetCurrentRankDisplayName()
        {
            return GetRankDisplayName(_playerData.Rating);
        }

        /// <summary>
        /// Get human-readable tier name.
        /// </summary>
        public static string GetTierName(RankTier tier)
        {
            return tier switch
            {
                RankTier.Unranked => "Unranked",
                RankTier.Iron => "Iron",
                RankTier.Bronze => "Bronze",
                RankTier.Silver => "Silver",
                RankTier.Gold => "Gold",
                RankTier.Platinum => "Platinum",
                RankTier.Diamond => "Diamond",
                RankTier.Master => "Master",
                RankTier.Champion => "Champion",
                RankTier.Grandmaster => "Grandmaster",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get tier icon/emoji.
        /// </summary>
        public static string GetTierIcon(RankTier tier)
        {
            return tier switch
            {
                RankTier.Unranked => "?",
                RankTier.Iron => "Fe",
                RankTier.Bronze => "Cu",
                RankTier.Silver => "Ag",
                RankTier.Gold => "Au",
                RankTier.Platinum => "Pt",
                RankTier.Diamond => "Di",
                RankTier.Master => "Ma",
                RankTier.Champion => "Ch",
                RankTier.Grandmaster => "GM",
                _ => "?"
            };
        }

        /// <summary>
        /// Get division as Roman numeral string.
        /// </summary>
        public static string GetDivisionName(RankDivision division)
        {
            return division switch
            {
                RankDivision.I => "I",
                RankDivision.II => "II",
                RankDivision.III => "III",
                RankDivision.IV => "IV",
                _ => ""
            };
        }

        #endregion

        #region Public API - Matchmaking

        /// <summary>
        /// Find a ranked match using search and join.
        /// </summary>
        public async Task<(Result result, LobbyData? lobby)> FindRankedMatchAsync(
            string gameMode = "ranked",
            int? skillRangeOverride = null)
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null)
                return (Result.NotConfigured, null);

            var playerRating = GetLocalPlayerRating();
            int range = skillRangeOverride ?? _initialSkillRange;

            for (int attempt = 0; attempt < _maxSearchAttempts; attempt++)
            {
                OnSearchAttempt?.Invoke(attempt + 1);

                var searchOptions = new LobbySearchOptions()
                    .WithGameMode(gameMode)
                    .WithSkillRange(playerRating - range, playerRating + range)
                    .ExcludePassworded()
                    .ExcludeGamesInProgress()
                    .OnlyWithAvailableSlots()
                    .WithMaxResults(10);

                var (result, lobbies) = await lobbyManager.SearchLobbiesAsync(searchOptions);

                if (result == Result.Success && lobbies.Count > 0)
                {
                    // Pick closest skill match
                    LobbyData bestMatch = default;
                    int bestDiff = int.MaxValue;

                    foreach (var lobby in lobbies)
                    {
                        int diff = Mathf.Abs(lobby.SkillLevel - playerRating);
                        if (diff < bestDiff)
                        {
                            bestDiff = diff;
                            bestMatch = lobby;
                        }
                    }

                    if (bestMatch.IsValid)
                    {
                        var joinResult = await lobbyManager.JoinLobbyByIdAsync(bestMatch.LobbyId);
                        if (joinResult.result == Result.Success)
                        {
                            OnMatchFound?.Invoke(joinResult.lobby);
                            return joinResult;
                        }
                    }
                }

                // Expand range
                range = Mathf.Min(range + _skillRangeExpansion, _maxSkillRange);
                OnSkillRangeExpanded?.Invoke(range);

                EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                    $"Search attempt {attempt + 1}/{_maxSearchAttempts}, expanding range to {range}");

                await Task.Delay((int)(_searchRetryDelay * 1000));
            }

            // No match found
            return (Result.NotFound, null);
        }

        /// <summary>
        /// Host a ranked lobby.
        /// </summary>
        public async Task<(Result result, LobbyData? lobby)> HostRankedLobbyAsync(string gameMode = "ranked")
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null)
                return (Result.NotConfigured, null);

            var rating = GetLocalPlayerRating();
            var options = new LobbyCreateOptions
            {
                GameMode = gameMode,
                SkillLevel = rating,
                MaxPlayers = _rankedLobbySize
            };

            var result = await lobbyManager.CreateLobbyAsync(options);
            if (result.result == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                    $"Hosted ranked lobby with skill level {rating}");
            }
            return result;
        }

        /// <summary>
        /// Find ranked match or host if none found.
        /// </summary>
        public async Task<(Result result, LobbyData? lobby, bool didHost)> FindOrHostRankedMatchAsync(string gameMode = "ranked")
        {
            // Try to find first
            var findResult = await FindRankedMatchAsync(gameMode);
            if (findResult.result == Result.Success)
            {
                return (Result.Success, findResult.lobby, false);
            }

            // Host if not found
            var hostResult = await HostRankedLobbyAsync(gameMode);
            return (hostResult.result, hostResult.lobby, true);
        }

        #endregion

        #region Public API - Queue

        /// <summary>
        /// Enter the matchmaking queue.
        /// </summary>
        public Result EnterQueue(string gameMode = "ranked")
        {
            if (_inQueue)
                return Result.AlreadyPending;

            if (_queueMode == QueueMode.SearchJoin)
            {
                Debug.LogWarning("[EOSRankedMatchmaking] Queue mode is SearchJoin only, use FindRankedMatchAsync instead");
                return Result.InvalidParameters;
            }

            var rating = GetLocalPlayerRating();
            _localQueueEntry = new QueueEntry
            {
                Puid = EOSManager.Instance?.LocalProductUserId?.ToString(),
                Rating = rating,
                QueueTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameMode = gameMode
            };

            _inQueue = true;
            _queueStartTime = Time.realtimeSinceStartup;

            OnQueueEntered?.Invoke();

            _queueCoroutine = StartCoroutine(QueueSearchLoop());

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking",
                $"Entered queue for {gameMode} at rating {rating}");

            return Result.Success;
        }

        /// <summary>
        /// Leave the matchmaking queue.
        /// </summary>
        public void LeaveQueue()
        {
            if (!_inQueue) return;

            _inQueue = false;

            if (_queueCoroutine != null)
            {
                StopCoroutine(_queueCoroutine);
                _queueCoroutine = null;
            }

            OnQueueLeft?.Invoke();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSRankedMatchmaking", "Left queue");
        }

        private IEnumerator QueueSearchLoop()
        {
            while (_inQueue)
            {
                // Update queue time
                float queueTime = Time.realtimeSinceStartup - _queueStartTime;
                OnQueueTimeUpdated?.Invoke(queueTime);

                // Calculate expanded range based on time in queue
                int expandedRange = _initialSkillRange + (int)(queueTime / 10f) * _skillRangeExpansion;
                expandedRange = Mathf.Min(expandedRange, _maxSkillRange);

                // Search for matches
                var task = FindRankedMatchAsync(_localQueueEntry.GameMode, expandedRange);
                yield return new WaitUntil(() => task.IsCompleted);

                if (task.Result.result == Result.Success && task.Result.lobby.HasValue)
                {
                    _inQueue = false;
                    OnMatchFound?.Invoke(task.Result.lobby.Value);
                    yield break;
                }

                yield return new WaitForSeconds(_queuePollInterval);
            }
        }

        #endregion

        #region Rating Algorithm Implementations

        /// <summary>
        /// ELO rating change calculation.
        /// </summary>
        private int CalculateEloChange(int playerRating, int opponentRating, bool won, int gamesPlayed)
        {
            // Expected score
            float expected = 1f / (1f + Mathf.Pow(10f, (opponentRating - playerRating) / 400f));
            float actual = won ? 1f : 0f;

            // K-factor based on games played
            int kFactor = GetKFactor(gamesPlayed);

            return Mathf.RoundToInt(kFactor * (actual - expected));
        }

        private int GetKFactor(int gamesPlayed)
        {
            if (gamesPlayed < _provisionalGames)
                return _kFactorNew;
            if (gamesPlayed < _establishedGames)
                return _kFactorStandard;
            return _kFactorEstablished;
        }

        /// <summary>
        /// Simple MMR change calculation with streak bonuses.
        /// </summary>
        private int CalculateSimpleMMRChange(bool won, int winStreak, int lossStreak)
        {
            int baseChange = won ? _winPoints : -_lossPoints;

            // Streak bonus/penalty
            if (won && winStreak >= 3)
            {
                int bonus = Mathf.Min(winStreak - 2, _maxStreakBonus / _streakBonusPerWin) * _streakBonusPerWin;
                baseChange += bonus;
            }
            else if (!won && lossStreak >= 3)
            {
                int penalty = Mathf.Min(lossStreak - 2, _maxStreakPenalty / _streakPenaltyPerLoss) * _streakPenaltyPerLoss;
                baseChange -= penalty;
            }

            return baseChange;
        }

        /// <summary>
        /// Simplified Glicko-2 change calculation.
        /// Full Glicko-2 involves complex iterative calculations; this is a practical approximation.
        /// </summary>
        private int CalculateGlicko2ChangeSimple(int rating, float rd, int opponentRating, bool won)
        {
            // Convert to Glicko-2 scale (mu, phi)
            float mu = (rating - 1500f) / 173.7178f;
            float phi = rd / 173.7178f;
            float muOpp = (opponentRating - 1500f) / 173.7178f;
            float phiOpp = _defaultRD / 173.7178f; // Assume default RD for opponent

            // g function
            float g = 1f / Mathf.Sqrt(1f + 3f * phiOpp * phiOpp / (Mathf.PI * Mathf.PI));

            // Expected score
            float E = 1f / (1f + Mathf.Exp(-g * (mu - muOpp)));

            // Actual score
            float s = won ? 1f : 0f;

            // Variance
            float v = 1f / (g * g * E * (1f - E));

            // Delta
            float delta = v * g * (s - E);

            // Simplified new rating (ignoring volatility updates)
            float newPhi = 1f / Mathf.Sqrt(1f / (phi * phi) + 1f / v);
            float newMu = mu + newPhi * newPhi * g * (s - E);

            // Convert back to rating scale
            int newRating = Mathf.RoundToInt(173.7178f * newMu + 1500f);
            return newRating - rating;
        }

        /// <summary>
        /// Update RD for inactivity (Glicko-2).
        /// </summary>
        private float UpdateRDForInactivity(float rd, int daysSinceLastMatch)
        {
            // RD increases over time when inactive
            float newRD = Mathf.Sqrt(rd * rd + _rdIncreasePerDay * _rdIncreasePerDay * daysSinceLastMatch);
            return Mathf.Min(_maxRD, newRD);
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Set the rating algorithm.
        /// </summary>
        public void SetAlgorithm(RatingAlgorithm algorithm)
        {
            _algorithm = algorithm;
        }

        /// <summary>
        /// Set the queue mode.
        /// </summary>
        public void SetQueueMode(QueueMode mode)
        {
            _queueMode = mode;
        }

        /// <summary>
        /// Set the tier display mode.
        /// </summary>
        public void SetTierDisplayMode(TierDisplayMode mode)
        {
            _tierDisplayMode = mode;
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSRankedMatchmaking))]
    public class EOSRankedMatchmakingEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var ranked = (EOSRankedMatchmaking)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("EOS Ranked Matchmaking", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Data Loaded", ranked.IsDataLoaded);
                EditorGUILayout.Toggle("Is Placed", ranked.IsPlaced);
                EditorGUILayout.IntField("Rating", ranked.CurrentRating);
                EditorGUILayout.TextField("Rank", ranked.GetCurrentRankDisplayName());
                EditorGUILayout.Toggle("In Queue", ranked.IsInQueue);
                if (ranked.IsInQueue)
                {
                    EditorGUILayout.FloatField("Queue Time (s)", ranked.QueueTime);
                }
            }

            EditorGUILayout.Space(10);
            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Testing", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Simulate Win"))
                {
                    _ = ranked.RecordMatchResultAsync(MatchOutcome.Win, ranked.CurrentRating + 50);
                }
                if (GUILayout.Button("Simulate Loss"))
                {
                    _ = ranked.RecordMatchResultAsync(MatchOutcome.Loss, ranked.CurrentRating - 50);
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Reset Data"))
                {
                    _ = ranked.ResetDataAsync();
                }

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
