using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FishNet.Managing;
using FishNet.Connection;
using FishNet.Transport.EOSNative.Lobbies;
using Epic.OnlineServices;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Manages vote kick functionality allowing players to vote to remove disruptive players.
    /// Server-authoritative vote counting with configurable thresholds.
    /// </summary>
    public class EOSVoteKickManager : MonoBehaviour
    {
        #region Singleton
        private static EOSVoteKickManager _instance;
        public static EOSVoteKickManager Instance => _instance;
        #endregion

        #region Settings
        [Header("Vote Settings")]
        [SerializeField]
        [Tooltip("Enable vote kick functionality.")]
        private bool _enabled = true;

        [SerializeField]
        [Tooltip("Vote threshold type.")]
        private VoteThreshold _threshold = VoteThreshold.Majority;

        [SerializeField]
        [Tooltip("Custom threshold percentage (0-100). Only used when threshold is Custom.")]
        [Range(1, 100)]
        private int _customThresholdPercent = 51;

        [SerializeField]
        [Tooltip("Time in seconds before a vote expires.")]
        [Range(15f, 120f)]
        private float _voteTimeout = 30f;

        [SerializeField]
        [Tooltip("Cooldown in seconds before the same player can start another vote.")]
        [Range(30f, 300f)]
        private float _voteCooldown = 60f;

        [SerializeField]
        [Tooltip("Minimum players required to start a vote (including initiator).")]
        [Range(2, 10)]
        private int _minPlayersForVote = 3;

        [SerializeField]
        [Tooltip("Host cannot be vote kicked.")]
        private bool _hostImmunity = true;

        [SerializeField]
        [Tooltip("Host can veto any vote kick.")]
        private bool _hostCanVeto = true;

        [SerializeField]
        [Tooltip("Host vote counts as this many votes (1 = normal, 2 = double, etc).")]
        [Range(1, 3)]
        private int _hostVoteWeight = 1;

        [SerializeField]
        [Tooltip("Show toast notifications for vote events.")]
        private bool _showToasts = true;

        [SerializeField]
        [Tooltip("Reason required when initiating vote.")]
        private bool _requireReason = false;
        #endregion

        #region Enums
        public enum VoteThreshold
        {
            Majority,       // >50%
            TwoThirds,      // >=66.7%
            ThreeQuarters,  // >=75%
            Unanimous,      // 100% (excluding target)
            Custom          // Use _customThresholdPercent
        }

        public enum VoteResult
        {
            Pending,
            Passed,
            Failed,
            Vetoed,
            TimedOut,
            Cancelled
        }
        #endregion

        #region Public Properties
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public VoteThreshold Threshold { get => _threshold; set => _threshold = value; }
        public int CustomThresholdPercent { get => _customThresholdPercent; set => _customThresholdPercent = Mathf.Clamp(value, 1, 100); }
        public float VoteTimeout { get => _voteTimeout; set => _voteTimeout = Mathf.Clamp(value, 15f, 120f); }
        public float VoteCooldown { get => _voteCooldown; set => _voteCooldown = Mathf.Clamp(value, 30f, 300f); }
        public int MinPlayersForVote { get => _minPlayersForVote; set => _minPlayersForVote = Mathf.Clamp(value, 2, 10); }
        public bool HostImmunity { get => _hostImmunity; set => _hostImmunity = value; }
        public bool HostCanVeto { get => _hostCanVeto; set => _hostCanVeto = value; }
        public int HostVoteWeight { get => _hostVoteWeight; set => _hostVoteWeight = Mathf.Clamp(value, 1, 3); }
        public bool ShowToasts { get => _showToasts; set => _showToasts = value; }
        public bool RequireReason { get => _requireReason; set => _requireReason = value; }

        /// <summary>Whether a vote is currently active.</summary>
        public bool IsVoteActive => _activeVote != null && _activeVote.Result == VoteResult.Pending;

        /// <summary>The currently active vote, if any.</summary>
        public VoteKickData ActiveVote => _activeVote;
        #endregion

        #region Events
        /// <summary>Fired when a vote kick is initiated. (voteData)</summary>
        public event Action<VoteKickData> OnVoteStarted;

        /// <summary>Fired when a player casts their vote. (voterPuid, votedYes)</summary>
        public event Action<string, bool> OnVoteCast;

        /// <summary>Fired when vote progress updates. (yesVotes, noVotes, totalEligible)</summary>
        public event Action<int, int, int> OnVoteProgress;

        /// <summary>Fired when a vote concludes. (voteData, result)</summary>
        public event Action<VoteKickData, VoteResult> OnVoteEnded;

        /// <summary>Fired when a player is kicked via vote. (puid, name)</summary>
        public event Action<string, string> OnPlayerVoteKicked;
        #endregion

        #region Data Classes
        [Serializable]
        public class VoteKickData
        {
            public string TargetPuid;
            public string TargetName;
            public string InitiatorPuid;
            public string InitiatorName;
            public string Reason;
            public float StartTime;
            public float Timeout;
            public VoteResult Result;
            public Dictionary<string, bool> Votes; // puid -> votedYes

            public VoteKickData()
            {
                Votes = new Dictionary<string, bool>();
                Result = VoteResult.Pending;
            }

            public int YesVotes => CountVotes(true);
            public int NoVotes => CountVotes(false);
            public int TotalVotes => Votes.Count;
            public float TimeRemaining => Mathf.Max(0f, Timeout - (Time.time - StartTime));

            private int CountVotes(bool yes)
            {
                int count = 0;
                foreach (var vote in Votes.Values)
                {
                    if (vote == yes) count++;
                }
                return count;
            }
        }
        #endregion

        #region Private State
        private VoteKickData _activeVote;
        private Dictionary<string, float> _cooldowns = new Dictionary<string, float>(); // initiatorPuid -> cooldownEndTime
        private NetworkManager _networkManager;
        private EOSNativeTransport _transport;
        private EOSLobbyManager _lobbyManager;
        private string _localPuid;
        private bool _isHost;
        #endregion

        #region Constants
        private const string VOTE_ATTR_KEY = "VOTE_KICK";
        private const string VOTE_RESPONSE_PREFIX = "VK:";
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            _networkManager = GetComponent<NetworkManager>();
            _transport = GetComponent<EOSNativeTransport>();
            _lobbyManager = FindFirstObjectByType<EOSLobbyManager>();

            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberAttributeUpdated += HandleMemberAttributeUpdated;
                _lobbyManager.OnLobbyAttributeUpdated += HandleLobbyAttributeUpdated;
                _lobbyManager.OnMemberLeft += HandleMemberLeft;
            }

            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            }
        }

        private void OnDestroy()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberAttributeUpdated -= HandleMemberAttributeUpdated;
                _lobbyManager.OnLobbyAttributeUpdated -= HandleLobbyAttributeUpdated;
                _lobbyManager.OnMemberLeft -= HandleMemberLeft;
            }

            if (_networkManager != null)
            {
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }

            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!_enabled) return;

            UpdateLocalState();

            // Check vote timeout (host only)
            if (_isHost && _activeVote != null && _activeVote.Result == VoteResult.Pending)
            {
                if (Time.time - _activeVote.StartTime >= _activeVote.Timeout)
                {
                    EndVote(VoteResult.TimedOut);
                }
            }
        }
        #endregion

        #region Public API - Starting Votes
        /// <summary>
        /// Start a vote to kick a player.
        /// </summary>
        /// <param name="targetPuid">The PUID of the player to kick.</param>
        /// <param name="reason">Optional reason for the kick.</param>
        /// <returns>True if vote was started successfully.</returns>
        public async Task<(bool success, string error)> StartVoteKickAsync(string targetPuid, string reason = null)
        {
            if (!_enabled)
                return (false, "Vote kick is disabled");

            if (string.IsNullOrEmpty(targetPuid))
                return (false, "Invalid target");

            if (_lobbyManager == null || !_lobbyManager.IsInLobby)
                return (false, "Not in a lobby");

            if (_activeVote != null && _activeVote.Result == VoteResult.Pending)
                return (false, "A vote is already in progress");

            if (_requireReason && string.IsNullOrEmpty(reason))
                return (false, "Reason is required");

            // Get local PUID
            _localPuid = _transport?.LocalProductUserId;
            if (string.IsNullOrEmpty(_localPuid))
                return (false, "Not connected");

            // Can't kick yourself
            if (targetPuid == _localPuid)
                return (false, "Cannot vote kick yourself");

            // Check if target is host and host has immunity
            string hostPuid = _lobbyManager.GetLobbyOwnerPuid();
            if (_hostImmunity && targetPuid == hostPuid)
                return (false, "Cannot vote kick the host");

            // Check player count
            int playerCount = _lobbyManager.GetMemberCount();
            if (playerCount < _minPlayersForVote)
                return (false, $"Need at least {_minPlayersForVote} players to start a vote");

            // Check cooldown
            if (_cooldowns.TryGetValue(_localPuid, out float cooldownEnd) && Time.time < cooldownEnd)
            {
                float remaining = cooldownEnd - Time.time;
                return (false, $"Cooldown: {remaining:0}s remaining");
            }

            // Get names
            string targetName = GetPlayerName(targetPuid);
            string initiatorName = GetPlayerName(_localPuid);

            // Create vote data
            var voteData = new VoteKickData
            {
                TargetPuid = targetPuid,
                TargetName = targetName,
                InitiatorPuid = _localPuid,
                InitiatorName = initiatorName,
                Reason = reason ?? "No reason given",
                StartTime = Time.time,
                Timeout = _voteTimeout,
                Result = VoteResult.Pending
            };

            // Initiator automatically votes yes
            voteData.Votes[_localPuid] = true;

            // Set cooldown
            _cooldowns[_localPuid] = Time.time + _voteCooldown;

            // Store locally
            _activeVote = voteData;

            // Broadcast vote start via lobby attribute
            string voteJson = SerializeVote(voteData);
            var result = await _lobbyManager.SetLobbyAttributeAsync(VOTE_ATTR_KEY, voteJson);

            if (result != Result.Success)
            {
                _activeVote = null;
                return (false, $"Failed to broadcast vote: {result}");
            }

            // Also set our vote response
            await _lobbyManager.SetMemberAttributeAsync(VOTE_RESPONSE_PREFIX + "RESP", "yes");

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                $"Vote kick started against {targetName} by {initiatorName}");

            OnVoteStarted?.Invoke(voteData);

            if (_showToasts)
            {
                string reasonText = string.IsNullOrEmpty(reason) ? "" : $"\nReason: {reason}";
                EOSToastManager.Warning("Vote Kick Started",
                    $"{initiatorName} wants to kick {targetName}{reasonText}");
            }

            return (true, null);
        }

        /// <summary>
        /// Start a vote kick by connection ID.
        /// </summary>
        public async Task<(bool success, string error)> StartVoteKickAsync(int connectionId, string reason = null)
        {
            string puid = _transport?.GetRemoteProductUserId(connectionId);
            if (string.IsNullOrEmpty(puid))
                return (false, "Invalid connection ID");

            return await StartVoteKickAsync(puid, reason);
        }
        #endregion

        #region Public API - Voting
        /// <summary>
        /// Cast your vote on the active vote kick.
        /// </summary>
        /// <param name="voteYes">True to vote yes (kick), false to vote no (keep).</param>
        public async Task<bool> CastVoteAsync(bool voteYes)
        {
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending)
                return false;

            _localPuid = _transport?.LocalProductUserId;
            if (string.IsNullOrEmpty(_localPuid))
                return false;

            // Can't vote if you're the target
            if (_localPuid == _activeVote.TargetPuid)
                return false;

            // Check if already voted
            if (_activeVote.Votes.ContainsKey(_localPuid))
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager", "Already voted");
                return false;
            }

            // Record vote locally
            _activeVote.Votes[_localPuid] = voteYes;

            // Broadcast vote via member attribute
            string voteValue = voteYes ? "yes" : "no";
            var result = await _lobbyManager.SetMemberAttributeAsync(VOTE_RESPONSE_PREFIX + "RESP", voteValue);

            if (result != Result.Success)
            {
                _activeVote.Votes.Remove(_localPuid);
                return false;
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                $"Cast vote: {voteValue}");

            OnVoteCast?.Invoke(_localPuid, voteYes);

            if (_showToasts)
            {
                EOSToastManager.Info("Vote Cast", voteYes ? "You voted YES to kick" : "You voted NO to keep");
            }

            // If we're the host, check vote result
            if (_isHost)
            {
                CheckVoteResult();
            }

            return true;
        }

        /// <summary>
        /// Host veto - cancel the vote and keep the player.
        /// </summary>
        public async Task<bool> VetoVoteAsync()
        {
            if (!_hostCanVeto || !_isHost)
                return false;

            if (_activeVote == null || _activeVote.Result != VoteResult.Pending)
                return false;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager", "Host vetoed vote");

            await EndVoteAsync(VoteResult.Vetoed);
            return true;
        }

        /// <summary>
        /// Cancel your own vote (initiator only).
        /// </summary>
        public async Task<bool> CancelVoteAsync()
        {
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending)
                return false;

            _localPuid = _transport?.LocalProductUserId;
            if (_localPuid != _activeVote.InitiatorPuid)
                return false;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager", "Vote cancelled by initiator");

            await EndVoteAsync(VoteResult.Cancelled);
            return true;
        }
        #endregion

        #region Public API - Queries
        /// <summary>
        /// Check if a player can be vote kicked.
        /// </summary>
        public bool CanBeVoteKicked(string puid)
        {
            if (!_enabled) return false;
            if (_lobbyManager == null || !_lobbyManager.IsInLobby) return false;

            // Host immunity
            if (_hostImmunity)
            {
                string hostPuid = _lobbyManager.GetLobbyOwnerPuid();
                if (puid == hostPuid) return false;
            }

            return true;
        }

        /// <summary>
        /// Check if the local player has voted.
        /// </summary>
        public bool HasVoted()
        {
            if (_activeVote == null) return false;
            _localPuid = _transport?.LocalProductUserId;
            return _activeVote.Votes.ContainsKey(_localPuid);
        }

        /// <summary>
        /// Check if a specific player has voted.
        /// </summary>
        public bool HasVoted(string puid)
        {
            if (_activeVote == null) return false;
            return _activeVote.Votes.ContainsKey(puid);
        }

        /// <summary>
        /// Get how a player voted (null if they haven't voted).
        /// </summary>
        public bool? GetVote(string puid)
        {
            if (_activeVote == null) return null;
            if (_activeVote.Votes.TryGetValue(puid, out bool vote))
                return vote;
            return null;
        }

        /// <summary>
        /// Get the required number of yes votes to pass.
        /// </summary>
        public int GetRequiredYesVotes()
        {
            if (_activeVote == null) return 0;

            int eligibleVoters = GetEligibleVoterCount();
            float thresholdPercent = GetThresholdPercent();

            return Mathf.CeilToInt(eligibleVoters * (thresholdPercent / 100f));
        }

        /// <summary>
        /// Get the number of eligible voters (all members except target).
        /// </summary>
        public int GetEligibleVoterCount()
        {
            if (_lobbyManager == null) return 0;
            int total = _lobbyManager.GetMemberCount();
            // Target can't vote
            return Mathf.Max(0, total - 1);
        }

        /// <summary>
        /// Get remaining cooldown for starting a vote (0 if no cooldown).
        /// </summary>
        public float GetCooldownRemaining()
        {
            _localPuid = _transport?.LocalProductUserId;
            if (string.IsNullOrEmpty(_localPuid)) return 0f;

            if (_cooldowns.TryGetValue(_localPuid, out float cooldownEnd))
            {
                return Mathf.Max(0f, cooldownEnd - Time.time);
            }
            return 0f;
        }
        #endregion

        #region Private Methods - Vote Logic
        private float GetThresholdPercent()
        {
            return _threshold switch
            {
                VoteThreshold.Majority => 50.01f,
                VoteThreshold.TwoThirds => 66.67f,
                VoteThreshold.ThreeQuarters => 75f,
                VoteThreshold.Unanimous => 100f,
                VoteThreshold.Custom => _customThresholdPercent,
                _ => 50.01f
            };
        }

        private void CheckVoteResult()
        {
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending)
                return;

            int eligibleVoters = GetEligibleVoterCount();
            int yesVotes = _activeVote.YesVotes;
            int noVotes = _activeVote.NoVotes;
            int totalVotes = _activeVote.TotalVotes;

            // Apply host vote weight
            string hostPuid = _lobbyManager?.GetLobbyOwnerPuid();
            if (_hostVoteWeight > 1 && !string.IsNullOrEmpty(hostPuid))
            {
                if (_activeVote.Votes.TryGetValue(hostPuid, out bool hostVote))
                {
                    int extraWeight = _hostVoteWeight - 1;
                    if (hostVote)
                        yesVotes += extraWeight;
                    else
                        noVotes += extraWeight;
                    eligibleVoters += extraWeight;
                }
            }

            int requiredYes = Mathf.CeilToInt(eligibleVoters * (GetThresholdPercent() / 100f));

            OnVoteProgress?.Invoke(yesVotes, noVotes, eligibleVoters);

            // Check if vote passed
            if (yesVotes >= requiredYes)
            {
                EndVote(VoteResult.Passed);
                return;
            }

            // Check if vote can no longer pass (too many no votes or abstains)
            int remainingVoters = eligibleVoters - totalVotes;
            if (yesVotes + remainingVoters < requiredYes)
            {
                EndVote(VoteResult.Failed);
                return;
            }

            // For unanimous, any no vote fails
            if (_threshold == VoteThreshold.Unanimous && noVotes > 0)
            {
                EndVote(VoteResult.Failed);
            }
        }

        private void EndVote(VoteResult result)
        {
            _ = EndVoteAsync(result);
        }

        private async Task EndVoteAsync(VoteResult result)
        {
            if (_activeVote == null) return;

            _activeVote.Result = result;
            var voteData = _activeVote;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                $"Vote ended: {result} ({voteData.YesVotes} yes, {voteData.NoVotes} no)");

            // Clear the vote attribute
            if (_isHost)
            {
                await _lobbyManager.SetLobbyAttributeAsync(VOTE_ATTR_KEY, "");
            }

            // Execute kick if passed
            if (result == VoteResult.Passed)
            {
                await ExecuteKick(voteData.TargetPuid, voteData.TargetName);
            }

            OnVoteEnded?.Invoke(voteData, result);

            if (_showToasts)
            {
                string message = result switch
                {
                    VoteResult.Passed => $"{voteData.TargetName} was kicked ({voteData.YesVotes}/{GetEligibleVoterCount()} votes)",
                    VoteResult.Failed => $"Vote to kick {voteData.TargetName} failed ({voteData.YesVotes}/{GetRequiredYesVotes()} needed)",
                    VoteResult.Vetoed => $"Host vetoed the vote to kick {voteData.TargetName}",
                    VoteResult.TimedOut => $"Vote to kick {voteData.TargetName} timed out",
                    VoteResult.Cancelled => $"Vote to kick {voteData.TargetName} was cancelled",
                    _ => "Vote ended"
                };

                if (result == VoteResult.Passed)
                    EOSToastManager.Success("Vote Passed", message);
                else
                    EOSToastManager.Info("Vote Ended", message);
            }

            _activeVote = null;
        }

        private async Task ExecuteKick(string targetPuid, string targetName)
        {
            if (!_isHost) return;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                $"Executing kick on {targetName}");

            // Get connection ID from PUID
            int connectionId = GetConnectionIdFromPuid(targetPuid);

            if (connectionId >= 0 && _networkManager != null && _networkManager.IsServerStarted)
            {
                _networkManager.ServerManager.Kick(connectionId, FishNet.Managing.Server.KickReason.Unset);
            }

            // Also kick from lobby
            if (_lobbyManager != null)
            {
                // Note: EOS lobby kick requires lobby owner
                // The network kick will disconnect them which removes from lobby
            }

            OnPlayerVoteKicked?.Invoke(targetPuid, targetName);
        }
        #endregion

        #region Private Methods - Event Handlers
        private void HandleLobbyAttributeUpdated(string key, string value)
        {
            if (key != VOTE_ATTR_KEY) return;

            if (string.IsNullOrEmpty(value))
            {
                // Vote was cleared
                if (_activeVote != null && _activeVote.Result == VoteResult.Pending)
                {
                    _activeVote = null;
                }
                return;
            }

            // Parse vote data
            var voteData = DeserializeVote(value);
            if (voteData == null) return;

            // Don't overwrite our local vote data if we're the host
            if (_isHost && _activeVote != null)
            {
                // Merge any new votes
                foreach (var kvp in voteData.Votes)
                {
                    if (!_activeVote.Votes.ContainsKey(kvp.Key))
                    {
                        _activeVote.Votes[kvp.Key] = kvp.Value;
                    }
                }
                return;
            }

            _activeVote = voteData;

            // If this is new to us, fire event
            if (voteData.Result == VoteResult.Pending)
            {
                OnVoteStarted?.Invoke(voteData);

                if (_showToasts)
                {
                    string reasonText = string.IsNullOrEmpty(voteData.Reason) ? "" : $"\nReason: {voteData.Reason}";
                    EOSToastManager.Warning("Vote Kick",
                        $"Vote to kick {voteData.TargetName}{reasonText}\nTimeout: {voteData.Timeout}s");
                }
            }
        }

        private void HandleMemberAttributeUpdated(string memberPuid, string key, string value)
        {
            if (!key.StartsWith(VOTE_RESPONSE_PREFIX)) return;
            if (_activeVote == null || _activeVote.Result != VoteResult.Pending) return;

            // Don't process target's "votes"
            if (memberPuid == _activeVote.TargetPuid) return;

            bool? voteValue = value?.ToLower() switch
            {
                "yes" => true,
                "no" => false,
                _ => null
            };

            if (voteValue.HasValue && !_activeVote.Votes.ContainsKey(memberPuid))
            {
                _activeVote.Votes[memberPuid] = voteValue.Value;

                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                    $"Vote received from {memberPuid}: {value}");

                OnVoteCast?.Invoke(memberPuid, voteValue.Value);

                if (_showToasts)
                {
                    string name = GetPlayerName(memberPuid);
                    EOSToastManager.Info("Vote Received",
                        $"{name} voted {(voteValue.Value ? "YES" : "NO")} ({_activeVote.TotalVotes}/{GetEligibleVoterCount()})");
                }

                // Check result if we're host
                if (_isHost)
                {
                    CheckVoteResult();
                }
            }
        }

        private void HandleMemberLeft(string memberPuid)
        {
            if (_activeVote == null) return;

            // If target left, cancel vote
            if (memberPuid == _activeVote.TargetPuid)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                    "Target left, cancelling vote");

                if (_isHost)
                {
                    _ = EndVoteAsync(VoteResult.Cancelled);
                }
                return;
            }

            // If initiator left, cancel vote
            if (memberPuid == _activeVote.InitiatorPuid)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSVoteKickManager",
                    "Initiator left, cancelling vote");

                if (_isHost)
                {
                    _ = EndVoteAsync(VoteResult.Cancelled);
                }
                return;
            }

            // Remove their vote and recheck
            _activeVote.Votes.Remove(memberPuid);

            if (_isHost)
            {
                CheckVoteResult();
            }
        }

        private void OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            // Track when players leave via network disconnect
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
            {
                string puid = _transport?.GetRemoteProductUserId(conn.ClientId);
                if (!string.IsNullOrEmpty(puid))
                {
                    HandleMemberLeft(puid);
                }
            }
        }
        #endregion

        #region Private Methods - Utility
        private void UpdateLocalState()
        {
            _localPuid = _transport?.LocalProductUserId;
            _isHost = _networkManager != null && _networkManager.IsServerStarted;
        }

        private string GetPlayerName(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return "Unknown";

            var registry = EOSPlayerRegistry.Instance;
            if (registry != null)
            {
                string name = registry.GetDisplayName(puid);
                if (!string.IsNullOrEmpty(name)) return name;
            }

            return puid.Length > 8 ? puid.Substring(0, 8) + "..." : puid;
        }

        private int GetConnectionIdFromPuid(string puid)
        {
            if (_transport == null || string.IsNullOrEmpty(puid)) return -1;

            // Search through connections
            if (_networkManager != null && _networkManager.IsServerStarted)
            {
                foreach (var conn in _networkManager.ServerManager.Clients.Values)
                {
                    string connPuid = _transport.GetRemoteProductUserId(conn.ClientId);
                    if (connPuid == puid)
                    {
                        return conn.ClientId;
                    }
                }
            }

            return -1;
        }

        private string SerializeVote(VoteKickData data)
        {
            // Simple JSON-like format
            return $"{data.TargetPuid}|{data.TargetName}|{data.InitiatorPuid}|{data.InitiatorName}|{data.Reason}|{data.Timeout}";
        }

        private VoteKickData DeserializeVote(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;

            try
            {
                var parts = data.Split('|');
                if (parts.Length < 6) return null;

                return new VoteKickData
                {
                    TargetPuid = parts[0],
                    TargetName = parts[1],
                    InitiatorPuid = parts[2],
                    InitiatorName = parts[3],
                    Reason = parts[4],
                    StartTime = Time.time,
                    Timeout = float.TryParse(parts[5], out float t) ? t : _voteTimeout,
                    Result = VoteResult.Pending
                };
            }
            catch
            {
                return null;
            }
        }
        #endregion
    }
}
