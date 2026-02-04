using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Managing;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Current rematch state
    /// </summary>
    public enum RematchState
    {
        None,           // No rematch available
        Proposed,       // Rematch proposed, waiting for votes
        Accepted,       // Enough votes, starting rematch
        Declined,       // Not enough votes
        Starting,       // Rematch is starting
        Cancelled       // Cancelled by host or timeout
    }

    /// <summary>
    /// How the rematch was initiated
    /// </summary>
    public enum RematchInitiator
    {
        Vote,           // Player voted
        Host,           // Host forced
        Auto            // Automatic after match
    }

    /// <summary>
    /// Data for a rematch vote/request
    /// </summary>
    [Serializable]
    public class RematchData
    {
        public string MatchId;              // Original match ID
        public string ProposedBy;           // Who proposed (PUID)
        public RematchInitiator Initiator;
        public RematchState State;
        public float ProposedTime;
        public float TimeoutSeconds;
        public bool SwapTeams;
        public bool KeepSettings;
        public HashSet<string> VotesYes;
        public HashSet<string> VotesNo;
        public List<string> EligiblePlayers;
        public int RequiredVotes;
        public string NewLobbyCode;

        public RematchData()
        {
            MatchId = Guid.NewGuid().ToString("N")[..8];
            VotesYes = new HashSet<string>();
            VotesNo = new HashSet<string>();
            EligiblePlayers = new List<string>();
            ProposedTime = Time.time;
            TimeoutSeconds = 30f;
            State = RematchState.None;
            KeepSettings = true;
        }

        public int TotalVotes => VotesYes.Count + VotesNo.Count;
        public int YesCount => VotesYes.Count;
        public int NoCount => VotesNo.Count;
        public bool IsExpired => Time.time - ProposedTime > TimeoutSeconds;
        public float TimeRemaining => Mathf.Max(0, TimeoutSeconds - (Time.time - ProposedTime));
        public bool HasEnoughYes => YesCount >= RequiredVotes;
        public bool HasEnoughNo => NoCount > EligiblePlayers.Count - RequiredVotes;
        public float YesPercent => EligiblePlayers.Count > 0 ? (float)YesCount / EligiblePlayers.Count : 0;
    }

    /// <summary>
    /// Manages rematch voting and creation after games
    /// </summary>
    public class EOSRematchManager : NetworkBehaviour
    {
        public static EOSRematchManager Instance { get; private set; }

        [Header("Rematch Settings")]
        [SerializeField] private bool _allowRematch = true;
        [SerializeField] private float _rematchTimeout = 30f;
        [SerializeField] private float _rematchVoteThreshold = 0.5f;     // 50% required
        [SerializeField] private bool _hostCanForceRematch = true;
        [SerializeField] private bool _autoOfferRematch = true;
        [SerializeField] private float _autoOfferDelay = 3f;

        [Header("Options")]
        [SerializeField] private bool _defaultSwapTeams = false;
        [SerializeField] private bool _defaultKeepSettings = true;
        [SerializeField] private bool _allowSwapTeamsToggle = true;
        [SerializeField] private int _maxConsecutiveRematches = 5;

        // Runtime state
        private RematchData _currentRematch;
        private int _consecutiveRematches = 0;
        private string _lastMatchId;
        private bool _isMatchEnded = false;

        // Events
        public event Action<RematchData> OnRematchProposed;
        public event Action<RematchData, string, bool> OnVoteCast;        // data, puid, votedYes
        public event Action<RematchData> OnRematchAccepted;
        public event Action<RematchData> OnRematchDeclined;
        public event Action<RematchData> OnRematchCancelled;
        public event Action<RematchData> OnRematchStarting;
        public event Action<float> OnRematchTimerTick;
        public event Action<string> OnRematchLobbyCreated;

        // Properties
        public bool AllowRematch { get => _allowRematch; set => _allowRematch = value; }
        public float RematchTimeout { get => _rematchTimeout; set => _rematchTimeout = value; }
        public float RematchVoteThreshold { get => _rematchVoteThreshold; set => _rematchVoteThreshold = value; }
        public bool HostCanForceRematch { get => _hostCanForceRematch; set => _hostCanForceRematch = value; }
        public bool AutoOfferRematch { get => _autoOfferRematch; set => _autoOfferRematch = value; }
        public float AutoOfferDelay { get => _autoOfferDelay; set => _autoOfferDelay = value; }
        public bool DefaultSwapTeams { get => _defaultSwapTeams; set => _defaultSwapTeams = value; }
        public bool DefaultKeepSettings { get => _defaultKeepSettings; set => _defaultKeepSettings = value; }
        public bool AllowSwapTeamsToggle { get => _allowSwapTeamsToggle; set => _allowSwapTeamsToggle = value; }
        public int MaxConsecutiveRematches { get => _maxConsecutiveRematches; set => _maxConsecutiveRematches = value; }

        public bool IsRematchActive => _currentRematch != null && _currentRematch.State == RematchState.Proposed;
        public RematchData CurrentRematch => _currentRematch;
        public RematchState CurrentState => _currentRematch?.State ?? RematchState.None;
        public int ConsecutiveRematches => _consecutiveRematches;
        public bool IsMatchEnded => _isMatchEnded;

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
            UpdateRematchState();
        }

        #region Match Lifecycle

        /// <summary>
        /// Call when a match ends to enable rematch
        /// </summary>
        public void OnMatchEnded(string matchId = null)
        {
            _isMatchEnded = true;
            _lastMatchId = matchId ?? Guid.NewGuid().ToString("N")[..8];

            if (_autoOfferRematch && _allowRematch)
            {
                _ = AutoOfferRematchDelayed();
            }
        }

        /// <summary>
        /// Call when starting a new match (from rematch)
        /// </summary>
        public void OnRematchStarted()
        {
            _isMatchEnded = false;
            _consecutiveRematches++;
            _currentRematch = null;
        }

        /// <summary>
        /// Reset rematch state for new session
        /// </summary>
        public void Reset()
        {
            _isMatchEnded = false;
            _consecutiveRematches = 0;
            _currentRematch = null;
            _lastMatchId = null;
        }

        private async Task AutoOfferRematchDelayed()
        {
            await Task.Delay((int)(_autoOfferDelay * 1000));

            if (_isMatchEnded && _currentRematch == null)
            {
                var localPuid = GetLocalPuid();
                if (IsHost() && !string.IsNullOrEmpty(localPuid))
                {
                    ProposeRematch(localPuid, RematchInitiator.Auto);
                }
            }
        }

        #endregion

        #region Proposing Rematch

        /// <summary>
        /// Propose a rematch (can be called by any player)
        /// </summary>
        public bool ProposeRematch(string proposerPuid = null, RematchInitiator initiator = RematchInitiator.Vote)
        {
            if (!CanProposeRematch()) return false;

            proposerPuid ??= GetLocalPuid();
            if (string.IsNullOrEmpty(proposerPuid)) return false;

            _currentRematch = new RematchData
            {
                MatchId = _lastMatchId ?? Guid.NewGuid().ToString("N")[..8],
                ProposedBy = proposerPuid,
                Initiator = initiator,
                State = RematchState.Proposed,
                TimeoutSeconds = _rematchTimeout,
                SwapTeams = _defaultSwapTeams,
                KeepSettings = _defaultKeepSettings,
                EligiblePlayers = GetEligiblePlayers(),
                RequiredVotes = CalculateRequiredVotes()
            };

            // Proposer automatically votes yes
            _currentRematch.VotesYes.Add(proposerPuid);

            OnRematchProposed?.Invoke(_currentRematch);

            // Sync to other clients
            if (IsServerInitialized)
            {
                RpcSyncRematchProposed(
                    _currentRematch.MatchId,
                    _currentRematch.ProposedBy,
                    (int)_currentRematch.Initiator,
                    _currentRematch.TimeoutSeconds,
                    _currentRematch.SwapTeams,
                    _currentRematch.KeepSettings,
                    _currentRematch.EligiblePlayers.ToArray(),
                    _currentRematch.RequiredVotes
                );
            }

            return true;
        }

        /// <summary>
        /// Host force rematch (no vote required)
        /// </summary>
        public bool ForceRematch()
        {
            if (!_hostCanForceRematch || !IsHost()) return false;
            if (!_isMatchEnded) return false;
            if (_consecutiveRematches >= _maxConsecutiveRematches) return false;

            var localPuid = GetLocalPuid();

            _currentRematch = new RematchData
            {
                MatchId = _lastMatchId ?? Guid.NewGuid().ToString("N")[..8],
                ProposedBy = localPuid,
                Initiator = RematchInitiator.Host,
                State = RematchState.Accepted,
                SwapTeams = _defaultSwapTeams,
                KeepSettings = _defaultKeepSettings,
                EligiblePlayers = GetEligiblePlayers()
            };

            OnRematchAccepted?.Invoke(_currentRematch);
            StartRematch();

            return true;
        }

        /// <summary>
        /// Check if rematch can be proposed
        /// </summary>
        public bool CanProposeRematch()
        {
            if (!_allowRematch) return false;
            if (!_isMatchEnded) return false;
            if (_currentRematch != null && _currentRematch.State == RematchState.Proposed) return false;
            if (_consecutiveRematches >= _maxConsecutiveRematches) return false;
            return true;
        }

        #endregion

        #region Voting

        /// <summary>
        /// Vote on rematch proposal
        /// </summary>
        public bool VoteRematch(bool voteYes, string voterPuid = null)
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed)
                return false;

            voterPuid ??= GetLocalPuid();
            if (string.IsNullOrEmpty(voterPuid)) return false;

            // Check if eligible
            if (!_currentRematch.EligiblePlayers.Contains(voterPuid))
                return false;

            // Remove any existing vote
            _currentRematch.VotesYes.Remove(voterPuid);
            _currentRematch.VotesNo.Remove(voterPuid);

            // Cast vote
            if (voteYes)
            {
                _currentRematch.VotesYes.Add(voterPuid);
            }
            else
            {
                _currentRematch.VotesNo.Add(voterPuid);
            }

            OnVoteCast?.Invoke(_currentRematch, voterPuid, voteYes);

            // Sync to server
            if (IsClientInitialized)
            {
                CmdVoteRematch(voterPuid, voteYes);
            }

            // Check if decision reached
            CheckVoteResult();

            return true;
        }

        /// <summary>
        /// Check if local player has voted
        /// </summary>
        public bool HasVoted()
        {
            var puid = GetLocalPuid();
            if (_currentRematch == null || string.IsNullOrEmpty(puid)) return false;
            return _currentRematch.VotesYes.Contains(puid) || _currentRematch.VotesNo.Contains(puid);
        }

        /// <summary>
        /// Get local player's vote
        /// </summary>
        public bool? GetMyVote()
        {
            var puid = GetLocalPuid();
            if (_currentRematch == null || string.IsNullOrEmpty(puid)) return null;

            if (_currentRematch.VotesYes.Contains(puid)) return true;
            if (_currentRematch.VotesNo.Contains(puid)) return false;
            return null;
        }

        private void CheckVoteResult()
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed)
                return;

            if (_currentRematch.HasEnoughYes)
            {
                _currentRematch.State = RematchState.Accepted;
                OnRematchAccepted?.Invoke(_currentRematch);
                StartRematch();
            }
            else if (_currentRematch.HasEnoughNo)
            {
                _currentRematch.State = RematchState.Declined;
                OnRematchDeclined?.Invoke(_currentRematch);
            }
        }

        #endregion

        #region Rematch Options

        /// <summary>
        /// Toggle swap teams option
        /// </summary>
        public void SetSwapTeams(bool swap)
        {
            if (_currentRematch != null && _allowSwapTeamsToggle)
            {
                _currentRematch.SwapTeams = swap;

                if (IsServerInitialized)
                {
                    RpcSyncSwapTeams(swap);
                }
            }
        }

        /// <summary>
        /// Toggle keep settings option
        /// </summary>
        public void SetKeepSettings(bool keep)
        {
            if (_currentRematch != null)
            {
                _currentRematch.KeepSettings = keep;

                if (IsServerInitialized)
                {
                    RpcSyncKeepSettings(keep);
                }
            }
        }

        #endregion

        #region Cancel

        /// <summary>
        /// Cancel the current rematch proposal
        /// </summary>
        public bool CancelRematch()
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed)
                return false;

            // Only host or proposer can cancel
            var localPuid = GetLocalPuid();
            if (!IsHost() && _currentRematch.ProposedBy != localPuid)
                return false;

            _currentRematch.State = RematchState.Cancelled;
            OnRematchCancelled?.Invoke(_currentRematch);
            _currentRematch = null;

            if (IsServerInitialized)
            {
                RpcSyncRematchCancelled();
            }

            return true;
        }

        #endregion

        #region Starting Rematch

        private async void StartRematch()
        {
            if (_currentRematch == null) return;

            _currentRematch.State = RematchState.Starting;
            OnRematchStarting?.Invoke(_currentRematch);

            // If host, create new lobby or reset current
            if (IsHost())
            {
                await CreateRematchLobby();
            }
        }

        private async Task CreateRematchLobby()
        {
            var transport = FindFirstObjectByType<EOSNativeTransport>();
            if (transport == null) return;

            // Option 1: Reset current lobby (simpler)
            // Just keep everyone in lobby and restart game

            // Option 2: Create new lobby
            // var (result, lobby) = await transport.HostLobbyAsync(new LobbyOptions { ... });

            // For now, just signal that rematch is starting
            // Game code should handle the actual restart/lobby recreation

            _currentRematch.NewLobbyCode = transport.CurrentLobbyCode;
            OnRematchLobbyCreated?.Invoke(_currentRematch.NewLobbyCode);

            // Sync
            if (IsServerInitialized)
            {
                RpcSyncRematchStarting(_currentRematch.NewLobbyCode);
            }
        }

        #endregion

        #region Update Loop

        private void UpdateRematchState()
        {
            if (_currentRematch == null || _currentRematch.State != RematchState.Proposed)
                return;

            // Check timeout
            if (_currentRematch.IsExpired)
            {
                // Timeout - check if enough yes votes
                if (_currentRematch.HasEnoughYes)
                {
                    _currentRematch.State = RematchState.Accepted;
                    OnRematchAccepted?.Invoke(_currentRematch);
                    StartRematch();
                }
                else
                {
                    _currentRematch.State = RematchState.Declined;
                    OnRematchDeclined?.Invoke(_currentRematch);
                }
            }
            else
            {
                // Tick timer
                OnRematchTimerTick?.Invoke(_currentRematch.TimeRemaining);
            }
        }

        #endregion

        #region Helpers

        private List<string> GetEligiblePlayers()
        {
            var players = new List<string>();

            // Get all players in lobby
            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                foreach (var (puid, name) in registry.RecentlyPlayedWith)
                {
                    players.Add(puid);
                }
            }

            // Add local player
            var localPuid = GetLocalPuid();
            if (!string.IsNullOrEmpty(localPuid) && !players.Contains(localPuid))
            {
                players.Add(localPuid);
            }

            return players;
        }

        private int CalculateRequiredVotes()
        {
            if (_currentRematch == null) return 1;

            int total = _currentRematch.EligiblePlayers.Count;
            return Mathf.Max(1, Mathf.CeilToInt(total * _rematchVoteThreshold));
        }

        private bool IsHost()
        {
            var transport = FindFirstObjectByType<EOSNativeTransport>();
            return transport != null && transport.IsLobbyOwner;
        }

        private string GetLocalPuid()
        {
            return EOSManager.Instance?.LocalProductUserId?.ToString();
        }

        #endregion

        #region Network RPCs

        [ServerRpc(RequireOwnership = false)]
        private void CmdVoteRematch(string voterPuid, bool voteYes)
        {
            if (_currentRematch == null) return;

            _currentRematch.VotesYes.Remove(voterPuid);
            _currentRematch.VotesNo.Remove(voterPuid);

            if (voteYes)
            {
                _currentRematch.VotesYes.Add(voterPuid);
            }
            else
            {
                _currentRematch.VotesNo.Add(voterPuid);
            }

            // Broadcast to all
            RpcSyncVote(voterPuid, voteYes);

            // Check result
            CheckVoteResult();
        }

        [ObserversRpc]
        private void RpcSyncRematchProposed(string matchId, string proposedBy, int initiator,
            float timeout, bool swapTeams, bool keepSettings, string[] eligible, int required)
        {
            if (IsServerInitialized) return;

            _currentRematch = new RematchData
            {
                MatchId = matchId,
                ProposedBy = proposedBy,
                Initiator = (RematchInitiator)initiator,
                State = RematchState.Proposed,
                TimeoutSeconds = timeout,
                SwapTeams = swapTeams,
                KeepSettings = keepSettings,
                EligiblePlayers = eligible.ToList(),
                RequiredVotes = required
            };

            OnRematchProposed?.Invoke(_currentRematch);
        }

        [ObserversRpc]
        private void RpcSyncVote(string voterPuid, bool voteYes)
        {
            if (_currentRematch == null) return;

            _currentRematch.VotesYes.Remove(voterPuid);
            _currentRematch.VotesNo.Remove(voterPuid);

            if (voteYes)
            {
                _currentRematch.VotesYes.Add(voterPuid);
            }
            else
            {
                _currentRematch.VotesNo.Add(voterPuid);
            }

            OnVoteCast?.Invoke(_currentRematch, voterPuid, voteYes);
        }

        [ObserversRpc]
        private void RpcSyncSwapTeams(bool swap)
        {
            if (_currentRematch != null)
            {
                _currentRematch.SwapTeams = swap;
            }
        }

        [ObserversRpc]
        private void RpcSyncKeepSettings(bool keep)
        {
            if (_currentRematch != null)
            {
                _currentRematch.KeepSettings = keep;
            }
        }

        [ObserversRpc]
        private void RpcSyncRematchCancelled()
        {
            if (_currentRematch != null)
            {
                _currentRematch.State = RematchState.Cancelled;
                OnRematchCancelled?.Invoke(_currentRematch);
                _currentRematch = null;
            }
        }

        [ObserversRpc]
        private void RpcSyncRematchStarting(string lobbyCode)
        {
            if (_currentRematch != null)
            {
                _currentRematch.State = RematchState.Starting;
                _currentRematch.NewLobbyCode = lobbyCode;
                OnRematchStarting?.Invoke(_currentRematch);
                OnRematchLobbyCreated?.Invoke(lobbyCode);
            }
        }

        #endregion

        #region Static Helpers

        /// <summary>
        /// Get display name for rematch state
        /// </summary>
        public static string GetStateName(RematchState state)
        {
            return state switch
            {
                RematchState.None => "No Rematch",
                RematchState.Proposed => "Voting",
                RematchState.Accepted => "Accepted",
                RematchState.Declined => "Declined",
                RematchState.Starting => "Starting",
                RematchState.Cancelled => "Cancelled",
                _ => "Unknown"
            };
        }

        #endregion
    }
}
