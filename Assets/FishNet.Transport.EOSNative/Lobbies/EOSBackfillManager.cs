using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Managing;
using FishNet.Connection;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Current game phase for JIP eligibility
    /// </summary>
    public enum GamePhase
    {
        Lobby,          // Pre-game lobby
        Loading,        // Loading/transitioning
        Warmup,         // Warmup period
        InProgress,     // Active gameplay
        Overtime,       // Overtime period
        PostGame,       // Match ended
        Custom          // Custom phase
    }

    /// <summary>
    /// Backfill request status
    /// </summary>
    public enum BackfillStatus
    {
        None,           // No backfill active
        Requesting,     // Looking for players
        Filling,        // Players joining
        Complete,       // Slots filled
        Cancelled,      // Cancelled by host
        Failed          // Failed to fill
    }

    /// <summary>
    /// Join-in-progress result for a connecting player
    /// </summary>
    public enum JoinInProgressResult
    {
        Allowed,        // Can join
        Denied_Phase,   // Wrong game phase
        Denied_Full,    // Game full
        Denied_Timeout, // JIP timeout exceeded
        Denied_Locked,  // Game locked
        Denied_Banned,  // Player banned
        Denied_Other    // Other reason
    }

    /// <summary>
    /// Data for a backfill request
    /// </summary>
    [Serializable]
    public class BackfillRequest
    {
        public string RequestId;
        public int SlotsNeeded;
        public int SlotsFilled;
        public float RequestedTime;
        public float TimeoutSeconds;
        public int PreferredTeam;           // -1 for any
        public string GameMode;
        public string Region;
        public Dictionary<string, string> Requirements;
        public BackfillStatus Status;

        public BackfillRequest()
        {
            RequestId = Guid.NewGuid().ToString("N")[..8];
            Requirements = new Dictionary<string, string>();
            RequestedTime = Time.time;
            TimeoutSeconds = 60f;
            PreferredTeam = -1;
            Status = BackfillStatus.None;
        }

        public bool IsExpired => Time.time - RequestedTime > TimeoutSeconds;
        public float TimeRemaining => Mathf.Max(0, TimeoutSeconds - (Time.time - RequestedTime));
        public bool IsComplete => SlotsFilled >= SlotsNeeded;
    }

    /// <summary>
    /// Data for a player joining in progress
    /// </summary>
    [Serializable]
    public class JoinInProgressData
    {
        public string PlayerPuid;
        public string PlayerName;
        public float JoinedTime;
        public int AssignedTeam;
        public Vector3 SpawnPosition;
        public bool IsBackfill;             // Came from backfill request
        public string BackfillRequestId;
        public GamePhase JoinedDuringPhase;

        public JoinInProgressData()
        {
            JoinedTime = Time.time;
        }
    }

    /// <summary>
    /// Manages backfill requests and join-in-progress for active games
    /// </summary>
    public class EOSBackfillManager : NetworkBehaviour
    {
        public static EOSBackfillManager Instance { get; private set; }

        [Header("Join-in-Progress Settings")]
        [SerializeField] private bool _allowJoinInProgress = true;
        [SerializeField] private float _jipTimeout = 300f;          // Seconds after game start
        [SerializeField] private float _jipMinTimeRemaining = 60f;  // Min time left in game
        [SerializeField] private bool _lockAfterStart = false;      // Lock immediately on start
        [SerializeField] private List<GamePhase> _allowedPhases = new()
        {
            GamePhase.Lobby, GamePhase.Warmup, GamePhase.InProgress
        };

        [Header("Backfill Settings")]
        [SerializeField] private bool _autoBackfill = false;        // Auto-request when players leave
        [SerializeField] private float _backfillDelay = 5f;         // Delay before backfill
        [SerializeField] private float _backfillTimeout = 60f;      // Time to find players
        [SerializeField] private int _minPlayersForBackfill = 2;    // Min players to trigger
        [SerializeField] private bool _balanceTeams = true;         // Fill smaller team first

        [Header("Spawn Settings")]
        [SerializeField] private Transform[] _jipSpawnPoints;       // Spawn points for JIP
        [SerializeField] private bool _useSpecialJipSpawns = false; // Use JIP-specific spawns
        [SerializeField] private float _spawnProtectionTime = 3f;   // Invuln on JIP spawn

        // Runtime state
        private GamePhase _currentPhase = GamePhase.Lobby;
        private float _gameStartTime;
        private bool _gameLocked = false;
        private BackfillRequest _activeBackfill;
        private readonly List<JoinInProgressData> _jipHistory = new();
        private readonly Dictionary<string, int> _teamPlayerCounts = new();
        private int _maxPlayers = 8;
        private int _currentPlayers = 0;
        private float _estimatedTimeRemaining = -1f;

        // Events
        public event Action<GamePhase, GamePhase> OnPhaseChanged;
        public event Action<JoinInProgressData> OnPlayerJoinedInProgress;
        public event Action<string, JoinInProgressResult> OnJoinDenied;
        public event Action<BackfillRequest> OnBackfillStarted;
        public event Action<BackfillRequest> OnBackfillComplete;
        public event Action<BackfillRequest> OnBackfillFailed;
        public event Action<BackfillRequest, string> OnBackfillPlayerJoined;
        public event Action OnGameLocked;
        public event Action OnGameUnlocked;

        // Properties
        public bool AllowJoinInProgress { get => _allowJoinInProgress; set => _allowJoinInProgress = value; }
        public float JipTimeout { get => _jipTimeout; set => _jipTimeout = value; }
        public float JipMinTimeRemaining { get => _jipMinTimeRemaining; set => _jipMinTimeRemaining = value; }
        public bool LockAfterStart { get => _lockAfterStart; set => _lockAfterStart = value; }
        public bool AutoBackfill { get => _autoBackfill; set => _autoBackfill = value; }
        public float BackfillDelay { get => _backfillDelay; set => _backfillDelay = value; }
        public float BackfillTimeout { get => _backfillTimeout; set => _backfillTimeout = value; }
        public bool BalanceTeams { get => _balanceTeams; set => _balanceTeams = value; }
        public float SpawnProtectionTime { get => _spawnProtectionTime; set => _spawnProtectionTime = value; }

        public GamePhase CurrentPhase => _currentPhase;
        public bool IsGameLocked => _gameLocked;
        public bool IsBackfillActive => _activeBackfill != null && _activeBackfill.Status == BackfillStatus.Requesting;
        public BackfillRequest ActiveBackfill => _activeBackfill;
        public IReadOnlyList<JoinInProgressData> JipHistory => _jipHistory;
        public int CurrentPlayers => _currentPlayers;
        public int MaxPlayers => _maxPlayers;
        public int AvailableSlots => Mathf.Max(0, _maxPlayers - _currentPlayers);
        public float TimeSinceGameStart => _gameStartTime > 0 ? Time.time - _gameStartTime : 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            UpdateBackfillStatus();
        }

        #region Game Phase Management

        /// <summary>
        /// Set the current game phase
        /// </summary>
        public void SetPhase(GamePhase phase)
        {
            if (_currentPhase == phase) return;

            var oldPhase = _currentPhase;
            _currentPhase = phase;

            if (phase == GamePhase.InProgress && _gameStartTime == 0)
            {
                _gameStartTime = Time.time;

                if (_lockAfterStart)
                {
                    LockGame();
                }
            }

            OnPhaseChanged?.Invoke(oldPhase, phase);

            if (IsServerInitialized)
            {
                RpcSyncPhase((int)phase);
            }
        }

        /// <summary>
        /// Set estimated time remaining in the game
        /// </summary>
        public void SetEstimatedTimeRemaining(float seconds)
        {
            _estimatedTimeRemaining = seconds;
        }

        /// <summary>
        /// Lock the game to prevent new joins
        /// </summary>
        public void LockGame()
        {
            if (_gameLocked) return;
            _gameLocked = true;
            OnGameLocked?.Invoke();

            // Cancel any active backfill
            CancelBackfill();

            // Update lobby to not accept joins
            UpdateLobbyJoinability(false);
        }

        /// <summary>
        /// Unlock the game to allow joins
        /// </summary>
        public void UnlockGame()
        {
            if (!_gameLocked) return;
            _gameLocked = false;
            OnGameUnlocked?.Invoke();

            UpdateLobbyJoinability(true);
        }

        /// <summary>
        /// Set the allowed phases for JIP
        /// </summary>
        public void SetAllowedPhases(params GamePhase[] phases)
        {
            _allowedPhases.Clear();
            _allowedPhases.AddRange(phases);
        }

        /// <summary>
        /// Add a phase to allowed JIP phases
        /// </summary>
        public void AddAllowedPhase(GamePhase phase)
        {
            if (!_allowedPhases.Contains(phase))
            {
                _allowedPhases.Add(phase);
            }
        }

        /// <summary>
        /// Remove a phase from allowed JIP phases
        /// </summary>
        public void RemoveAllowedPhase(GamePhase phase)
        {
            _allowedPhases.Remove(phase);
        }

        #endregion

        #region Join-in-Progress

        /// <summary>
        /// Check if a player can join in progress
        /// </summary>
        public JoinInProgressResult CanJoinInProgress(string puid = null)
        {
            if (!_allowJoinInProgress)
                return JoinInProgressResult.Denied_Locked;

            if (_gameLocked)
                return JoinInProgressResult.Denied_Locked;

            if (!_allowedPhases.Contains(_currentPhase))
                return JoinInProgressResult.Denied_Phase;

            if (_currentPlayers >= _maxPlayers)
                return JoinInProgressResult.Denied_Full;

            if (_gameStartTime > 0 && TimeSinceGameStart > _jipTimeout)
                return JoinInProgressResult.Denied_Timeout;

            if (_estimatedTimeRemaining > 0 && _estimatedTimeRemaining < _jipMinTimeRemaining)
                return JoinInProgressResult.Denied_Timeout;

            // Check if player is banned
            if (!string.IsNullOrEmpty(puid) && EOSPlayerRegistry.Instance?.IsBlocked(puid) == true)
                return JoinInProgressResult.Denied_Banned;

            return JoinInProgressResult.Allowed;
        }

        /// <summary>
        /// Process a player joining in progress (call from server)
        /// </summary>
        public JoinInProgressData ProcessJoinInProgress(string puid, string playerName, bool isBackfill = false, string backfillRequestId = null)
        {
            var result = CanJoinInProgress(puid);
            if (result != JoinInProgressResult.Allowed)
            {
                OnJoinDenied?.Invoke(puid, result);
                return null;
            }

            var jipData = new JoinInProgressData
            {
                PlayerPuid = puid,
                PlayerName = playerName,
                JoinedDuringPhase = _currentPhase,
                IsBackfill = isBackfill,
                BackfillRequestId = backfillRequestId
            };

            // Assign team
            jipData.AssignedTeam = GetTeamForNewPlayer();

            // Get spawn position
            jipData.SpawnPosition = GetJipSpawnPosition(jipData.AssignedTeam);

            // Track
            _jipHistory.Add(jipData);
            _currentPlayers++;
            IncrementTeamCount(jipData.AssignedTeam);

            // Update backfill
            if (isBackfill && _activeBackfill != null && _activeBackfill.RequestId == backfillRequestId)
            {
                _activeBackfill.SlotsFilled++;
                OnBackfillPlayerJoined?.Invoke(_activeBackfill, puid);

                if (_activeBackfill.IsComplete)
                {
                    _activeBackfill.Status = BackfillStatus.Complete;
                    OnBackfillComplete?.Invoke(_activeBackfill);
                }
            }

            OnPlayerJoinedInProgress?.Invoke(jipData);

            return jipData;
        }

        /// <summary>
        /// Get the best team for a new player (for balancing)
        /// </summary>
        public int GetTeamForNewPlayer()
        {
            if (!_balanceTeams || _teamPlayerCounts.Count == 0)
            {
                return 0;
            }

            // Find team with fewest players
            return _teamPlayerCounts.OrderBy(kvp => kvp.Value).First().Key;
        }

        /// <summary>
        /// Get spawn position for JIP player
        /// </summary>
        public Vector3 GetJipSpawnPosition(int team)
        {
            if (_useSpecialJipSpawns && _jipSpawnPoints != null && _jipSpawnPoints.Length > 0)
            {
                // Use JIP-specific spawn points
                var validSpawns = _jipSpawnPoints.Where(s => s != null).ToArray();
                if (validSpawns.Length > 0)
                {
                    return validSpawns[UnityEngine.Random.Range(0, validSpawns.Length)].position;
                }
            }

            // Default spawn position (can be overridden by game)
            return Vector3.zero;
        }

        /// <summary>
        /// Set team player counts for balancing
        /// </summary>
        public void SetTeamCounts(Dictionary<int, int> counts)
        {
            _teamPlayerCounts.Clear();
            foreach (var kvp in counts)
            {
                _teamPlayerCounts[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Update team count
        /// </summary>
        public void SetTeamCount(int team, int count)
        {
            _teamPlayerCounts[team] = count;
        }

        private void IncrementTeamCount(int team)
        {
            if (!_teamPlayerCounts.ContainsKey(team))
            {
                _teamPlayerCounts[team] = 0;
            }
            _teamPlayerCounts[team]++;
        }

        #endregion

        #region Backfill

        /// <summary>
        /// Request backfill for empty slots
        /// </summary>
        public BackfillRequest RequestBackfill(int slots, int preferredTeam = -1, string gameMode = null, string region = null)
        {
            if (_activeBackfill != null && _activeBackfill.Status == BackfillStatus.Requesting)
            {
                return _activeBackfill; // Already have active request
            }

            if (CanJoinInProgress() != JoinInProgressResult.Allowed)
            {
                return null; // Can't accept new players
            }

            _activeBackfill = new BackfillRequest
            {
                SlotsNeeded = slots,
                TimeoutSeconds = _backfillTimeout,
                PreferredTeam = preferredTeam,
                GameMode = gameMode,
                Region = region,
                Status = BackfillStatus.Requesting
            };

            // Mark lobby as needing players
            UpdateLobbyBackfillStatus(true, slots);

            OnBackfillStarted?.Invoke(_activeBackfill);

            return _activeBackfill;
        }

        /// <summary>
        /// Request backfill for all available slots
        /// </summary>
        public BackfillRequest RequestBackfillForAllSlots()
        {
            int slots = AvailableSlots;
            if (slots <= 0) return null;

            return RequestBackfill(slots);
        }

        /// <summary>
        /// Cancel active backfill request
        /// </summary>
        public void CancelBackfill()
        {
            if (_activeBackfill == null) return;

            _activeBackfill.Status = BackfillStatus.Cancelled;
            UpdateLobbyBackfillStatus(false, 0);
            _activeBackfill = null;
        }

        /// <summary>
        /// Handle player leaving during game
        /// </summary>
        public void OnPlayerLeft(string puid, int team = -1)
        {
            _currentPlayers = Mathf.Max(0, _currentPlayers - 1);

            if (team >= 0 && _teamPlayerCounts.ContainsKey(team))
            {
                _teamPlayerCounts[team] = Mathf.Max(0, _teamPlayerCounts[team] - 1);
            }

            // Auto-backfill if enabled
            if (_autoBackfill && _currentPlayers >= _minPlayersForBackfill)
            {
                if (_activeBackfill == null || _activeBackfill.Status != BackfillStatus.Requesting)
                {
                    // Delay backfill in case player reconnects
                    _ = DelayedBackfillRequest(team);
                }
            }
        }

        private async Task DelayedBackfillRequest(int preferredTeam)
        {
            await Task.Delay((int)(_backfillDelay * 1000));

            // Check if still need backfill
            if (AvailableSlots > 0 && CanJoinInProgress() == JoinInProgressResult.Allowed)
            {
                RequestBackfill(1, preferredTeam);
            }
        }

        private void UpdateBackfillStatus()
        {
            if (_activeBackfill == null) return;
            if (_activeBackfill.Status != BackfillStatus.Requesting) return;

            // Check timeout
            if (_activeBackfill.IsExpired)
            {
                _activeBackfill.Status = BackfillStatus.Failed;
                OnBackfillFailed?.Invoke(_activeBackfill);
                UpdateLobbyBackfillStatus(false, 0);
                _activeBackfill = null;
            }
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Set max players for backfill calculations
        /// </summary>
        public void SetMaxPlayers(int max)
        {
            _maxPlayers = max;
        }

        /// <summary>
        /// Set current player count
        /// </summary>
        public void SetCurrentPlayers(int count)
        {
            _currentPlayers = count;
        }

        /// <summary>
        /// Set JIP spawn points
        /// </summary>
        public void SetJipSpawnPoints(Transform[] spawnPoints)
        {
            _jipSpawnPoints = spawnPoints;
        }

        /// <summary>
        /// Reset for new game
        /// </summary>
        public void ResetForNewGame()
        {
            _currentPhase = GamePhase.Lobby;
            _gameStartTime = 0;
            _gameLocked = false;
            _activeBackfill = null;
            _jipHistory.Clear();
            _estimatedTimeRemaining = -1f;
        }

        #endregion

        #region Lobby Integration

        private async void UpdateLobbyJoinability(bool allowJoin)
        {
            var transport = FindFirstObjectByType<EOSNativeTransport>();
            if (transport == null || !transport.IsInLobby) return;

            // Update lobby attribute to indicate if JIP is allowed
            await transport.SetLobbyAttributeAsync("JIP_ALLOWED", allowJoin ? "1" : "0");
        }

        private async void UpdateLobbyBackfillStatus(bool needsBackfill, int slots)
        {
            var transport = FindFirstObjectByType<EOSNativeTransport>();
            if (transport == null || !transport.IsInLobby) return;

            await transport.SetLobbyAttributeAsync("BACKFILL_NEEDED", needsBackfill ? "1" : "0");
            await transport.SetLobbyAttributeAsync("BACKFILL_SLOTS", slots.ToString());
        }

        #endregion

        #region Network Sync

        [ObserversRpc]
        private void RpcSyncPhase(int phase)
        {
            if (IsServerInitialized) return; // Server already set it

            var oldPhase = _currentPhase;
            _currentPhase = (GamePhase)phase;
            OnPhaseChanged?.Invoke(oldPhase, _currentPhase);
        }

        #endregion

        #region Static Helpers

        /// <summary>
        /// Get display name for game phase
        /// </summary>
        public static string GetPhaseName(GamePhase phase)
        {
            return phase switch
            {
                GamePhase.Lobby => "Lobby",
                GamePhase.Loading => "Loading",
                GamePhase.Warmup => "Warmup",
                GamePhase.InProgress => "In Progress",
                GamePhase.Overtime => "Overtime",
                GamePhase.PostGame => "Post Game",
                GamePhase.Custom => "Custom",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get display name for JIP result
        /// </summary>
        public static string GetJipResultMessage(JoinInProgressResult result)
        {
            return result switch
            {
                JoinInProgressResult.Allowed => "Join allowed",
                JoinInProgressResult.Denied_Phase => "Game phase does not allow joining",
                JoinInProgressResult.Denied_Full => "Game is full",
                JoinInProgressResult.Denied_Timeout => "Too late to join",
                JoinInProgressResult.Denied_Locked => "Game is locked",
                JoinInProgressResult.Denied_Banned => "You are banned",
                JoinInProgressResult.Denied_Other => "Cannot join",
                _ => "Unknown"
            };
        }

        #endregion
    }
}
