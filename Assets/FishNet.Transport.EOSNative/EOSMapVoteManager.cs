using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transport.EOSNative.Lobbies;
using Epic.OnlineServices;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Manages map/mode voting allowing players to vote on the next game settings.
    /// </summary>
    public class EOSMapVoteManager : MonoBehaviour
    {
        #region Singleton
        private static EOSMapVoteManager _instance;
        public static EOSMapVoteManager Instance => _instance;
        #endregion

        #region Settings
        [Header("Vote Settings")]
        [SerializeField]
        [Tooltip("Enable map/mode voting.")]
        private bool _enabled = true;

        [SerializeField]
        [Tooltip("Time in seconds for voting.")]
        [Range(10f, 120f)]
        private float _voteDuration = 30f;

        [SerializeField]
        [Tooltip("How to break ties.")]
        private TieBreaker _tieBreaker = TieBreaker.Random;

        [SerializeField]
        [Tooltip("Show toast notifications for vote events.")]
        private bool _showToasts = true;

        [SerializeField]
        [Tooltip("Auto-start timer when vote begins.")]
        private bool _autoStartTimer = true;

        [SerializeField]
        [Tooltip("Allow players to change their vote.")]
        private bool _allowVoteChange = true;

        [SerializeField]
        [Tooltip("Show vote counts in real-time (false = reveal at end).")]
        private bool _showLiveResults = true;
        #endregion

        #region Enums
        public enum TieBreaker
        {
            Random,         // Pick randomly from tied options
            FirstOption,    // First option in list wins
            HostChoice,     // Host decides
            Revote          // Start a new vote with only tied options
        }

        public enum VoteState
        {
            Inactive,
            Active,
            Completed
        }
        #endregion

        #region Public Properties
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public float VoteDuration { get => _voteDuration; set => _voteDuration = Mathf.Clamp(value, 10f, 120f); }
        public TieBreaker TieBreakerMode { get => _tieBreaker; set => _tieBreaker = value; }
        public bool ShowToasts { get => _showToasts; set => _showToasts = value; }
        public bool AutoStartTimer { get => _autoStartTimer; set => _autoStartTimer = value; }
        public bool AllowVoteChange { get => _allowVoteChange; set => _allowVoteChange = value; }
        public bool ShowLiveResults { get => _showLiveResults; set => _showLiveResults = value; }

        /// <summary>Whether a vote is currently active.</summary>
        public bool IsVoteActive => _state == VoteState.Active;

        /// <summary>Current vote state.</summary>
        public VoteState State => _state;

        /// <summary>The current vote data.</summary>
        public MapVoteData CurrentVote => _currentVote;

        /// <summary>Time remaining in the vote.</summary>
        public float TimeRemaining => _currentVote != null ? Mathf.Max(0f, _voteEndTime - Time.time) : 0f;
        #endregion

        #region Events
        /// <summary>Fired when a vote starts. (voteData)</summary>
        public event Action<MapVoteData> OnVoteStarted;

        /// <summary>Fired when a player casts or changes their vote. (voterPuid, optionIndex)</summary>
        public event Action<string, int> OnVoteCast;

        /// <summary>Fired every second during voting. (secondsRemaining)</summary>
        public event Action<int> OnTimerTick;

        /// <summary>Fired when vote ends. (voteData, winningOption, winningIndex)</summary>
        public event Action<MapVoteData, VoteOption, int> OnVoteEnded;

        /// <summary>Fired when a tie occurs and needs host decision. (tiedOptions)</summary>
        public event Action<List<VoteOption>> OnTieNeedsDecision;
        #endregion

        #region Data Classes
        [Serializable]
        public class VoteOption
        {
            public string Id;           // Unique identifier (e.g., "map_dust2")
            public string DisplayName;  // Shown to players (e.g., "Dust 2")
            public string Description;  // Optional description
            public string ImageUrl;     // Optional image URL
            public string Category;     // "map", "mode", or custom

            public VoteOption() { }

            public VoteOption(string id, string displayName, string category = "map")
            {
                Id = id;
                DisplayName = displayName;
                Category = category;
            }
        }

        [Serializable]
        public class MapVoteData
        {
            public string Title;                        // "Vote for Next Map"
            public List<VoteOption> Options;            // Available choices
            public Dictionary<string, int> Votes;       // puid -> optionIndex
            public float Duration;                      // Vote duration
            public string InitiatorPuid;                // Who started the vote

            public MapVoteData()
            {
                Options = new List<VoteOption>();
                Votes = new Dictionary<string, int>();
            }

            /// <summary>Get vote count for an option.</summary>
            public int GetVoteCount(int optionIndex)
            {
                return Votes.Values.Count(v => v == optionIndex);
            }

            /// <summary>Get total votes cast.</summary>
            public int TotalVotes => Votes.Count;

            /// <summary>Get the option index a player voted for (-1 if not voted).</summary>
            public int GetPlayerVote(string puid)
            {
                return Votes.TryGetValue(puid, out int index) ? index : -1;
            }
        }

        public class VoteResult
        {
            public VoteOption Winner;
            public int WinnerIndex;
            public int WinnerVotes;
            public bool WasTie;
            public List<int> TiedIndices;
        }
        #endregion

        #region Private State
        private VoteState _state = VoteState.Inactive;
        private MapVoteData _currentVote;
        private float _voteEndTime;
        private float _lastTickTime;
        private NetworkManager _networkManager;
        private EOSNativeTransport _transport;
        private EOSLobbyManager _lobbyManager;
        private string _localPuid;
        private bool _isHost;
        private List<int> _pendingTieIndices;
        #endregion

        #region Constants
        private const string VOTE_ATTR_KEY = "MAP_VOTE";
        private const string VOTE_RESPONSE_PREFIX = "MV:";
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
            }
        }

        private void OnDestroy()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberAttributeUpdated -= HandleMemberAttributeUpdated;
                _lobbyManager.OnLobbyAttributeUpdated -= HandleLobbyAttributeUpdated;
            }

            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!_enabled) return;

            UpdateLocalState();

            if (_state == VoteState.Active)
            {
                // Timer tick
                if (Time.time - _lastTickTime >= 1f)
                {
                    _lastTickTime = Time.time;
                    int remaining = Mathf.CeilToInt(TimeRemaining);
                    OnTimerTick?.Invoke(remaining);

                    // Countdown toast at key moments
                    if (_showToasts && (remaining == 10 || remaining == 5 || remaining == 3))
                    {
                        EOSToastManager.Info("Vote Ending", $"{remaining} seconds remaining");
                    }
                }

                // Check if time is up (host only processes)
                if (_isHost && Time.time >= _voteEndTime)
                {
                    EndVote();
                }
            }
        }
        #endregion

        #region Public API - Starting Votes
        /// <summary>
        /// Start a map/mode vote with the given options.
        /// </summary>
        /// <param name="title">Title shown to players (e.g., "Vote for Next Map")</param>
        /// <param name="options">List of options to vote on</param>
        /// <returns>True if vote started successfully</returns>
        public async Task<bool> StartVoteAsync(string title, List<VoteOption> options)
        {
            if (!_enabled)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Voting is disabled");
                return false;
            }

            if (_state == VoteState.Active)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Vote already in progress");
                return false;
            }

            if (options == null || options.Count < 2)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Need at least 2 options");
                return false;
            }

            if (options.Count > 8)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Maximum 8 options allowed");
                return false;
            }

            if (_lobbyManager == null || !_lobbyManager.IsInLobby)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Not in a lobby");
                return false;
            }

            _localPuid = _transport?.LocalProductUserId;

            _currentVote = new MapVoteData
            {
                Title = title,
                Options = new List<VoteOption>(options),
                Duration = _voteDuration,
                InitiatorPuid = _localPuid
            };

            _state = VoteState.Active;
            _voteEndTime = Time.time + _voteDuration;
            _lastTickTime = Time.time;
            _pendingTieIndices = null;

            // Broadcast vote via lobby attribute
            string voteJson = SerializeVote(_currentVote);
            var result = await _lobbyManager.SetLobbyAttributeAsync(VOTE_ATTR_KEY, voteJson);

            if (result != Result.Success)
            {
                _state = VoteState.Inactive;
                _currentVote = null;
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", $"Failed to broadcast vote: {result}");
                return false;
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager",
                $"Vote started: {title} with {options.Count} options");

            OnVoteStarted?.Invoke(_currentVote);

            if (_showToasts)
            {
                string optionList = string.Join(", ", options.Select(o => o.DisplayName));
                EOSToastManager.Info(title, $"Options: {optionList}\nTime: {_voteDuration}s");
            }

            return true;
        }

        /// <summary>
        /// Start a simple map vote with string names.
        /// </summary>
        public async Task<bool> StartMapVoteAsync(string title, params string[] mapNames)
        {
            var options = mapNames.Select(name => new VoteOption(name.ToLower().Replace(" ", "_"), name, "map")).ToList();
            return await StartVoteAsync(title, options);
        }

        /// <summary>
        /// Start a simple mode vote with string names.
        /// </summary>
        public async Task<bool> StartModeVoteAsync(string title, params string[] modeNames)
        {
            var options = modeNames.Select(name => new VoteOption(name.ToLower().Replace(" ", "_"), name, "mode")).ToList();
            return await StartVoteAsync(title, options);
        }
        #endregion

        #region Public API - Voting
        /// <summary>
        /// Cast a vote for an option.
        /// </summary>
        /// <param name="optionIndex">Index of the option (0-based)</param>
        public async Task<bool> CastVoteAsync(int optionIndex)
        {
            if (_state != VoteState.Active)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "No active vote");
                return false;
            }

            if (optionIndex < 0 || optionIndex >= _currentVote.Options.Count)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Invalid option index");
                return false;
            }

            _localPuid = _transport?.LocalProductUserId;
            if (string.IsNullOrEmpty(_localPuid))
                return false;

            // Check if already voted and changing is disabled
            if (!_allowVoteChange && _currentVote.Votes.ContainsKey(_localPuid))
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", "Already voted, changes not allowed");
                return false;
            }

            // Record vote locally
            _currentVote.Votes[_localPuid] = optionIndex;

            // Broadcast via member attribute
            var result = await _lobbyManager.SetMemberAttributeAsync(VOTE_RESPONSE_PREFIX + "VOTE", optionIndex.ToString());

            if (result != Result.Success)
            {
                _currentVote.Votes.Remove(_localPuid);
                return false;
            }

            string optionName = _currentVote.Options[optionIndex].DisplayName;
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager", $"Voted for: {optionName}");

            OnVoteCast?.Invoke(_localPuid, optionIndex);

            if (_showToasts)
            {
                EOSToastManager.Success("Vote Cast", $"You voted for {optionName}");
            }

            return true;
        }

        /// <summary>
        /// Cast a vote by option ID.
        /// </summary>
        public async Task<bool> CastVoteByIdAsync(string optionId)
        {
            if (_currentVote == null) return false;

            int index = _currentVote.Options.FindIndex(o => o.Id == optionId);
            if (index < 0) return false;

            return await CastVoteAsync(index);
        }

        /// <summary>
        /// Host resolves a tie by picking a winner.
        /// </summary>
        public async Task<bool> ResolveTieAsync(int winnerIndex)
        {
            if (!_isHost || _pendingTieIndices == null)
                return false;

            if (!_pendingTieIndices.Contains(winnerIndex))
                return false;

            await FinalizeWinner(winnerIndex);
            return true;
        }
        #endregion

        #region Public API - Control
        /// <summary>
        /// Cancel the current vote (host only).
        /// </summary>
        public async Task<bool> CancelVoteAsync()
        {
            if (_state != VoteState.Active)
                return false;

            _state = VoteState.Inactive;
            _currentVote = null;

            // Clear the vote attribute
            await _lobbyManager.SetLobbyAttributeAsync(VOTE_ATTR_KEY, "");

            if (_showToasts)
            {
                EOSToastManager.Warning("Vote Cancelled", "The vote has been cancelled");
            }

            return true;
        }

        /// <summary>
        /// Extend the vote timer (host only).
        /// </summary>
        public void ExtendTimer(float additionalSeconds)
        {
            if (_state != VoteState.Active || !_isHost)
                return;

            _voteEndTime += additionalSeconds;

            if (_showToasts)
            {
                EOSToastManager.Info("Time Extended", $"+{additionalSeconds:0}s added to vote");
            }
        }

        /// <summary>
        /// End the vote immediately (host only).
        /// </summary>
        public void EndVoteNow()
        {
            if (_state != VoteState.Active || !_isHost)
                return;

            _voteEndTime = Time.time;
        }
        #endregion

        #region Public API - Queries
        /// <summary>
        /// Get the local player's current vote (-1 if not voted).
        /// </summary>
        public int GetMyVote()
        {
            if (_currentVote == null) return -1;
            _localPuid = _transport?.LocalProductUserId;
            return _currentVote.GetPlayerVote(_localPuid);
        }

        /// <summary>
        /// Check if the local player has voted.
        /// </summary>
        public bool HasVoted()
        {
            return GetMyVote() >= 0;
        }

        /// <summary>
        /// Get vote counts for all options (only if ShowLiveResults is true or vote ended).
        /// </summary>
        public int[] GetVoteCounts()
        {
            if (_currentVote == null) return new int[0];

            if (!_showLiveResults && _state == VoteState.Active)
            {
                // Return zeros during active vote if live results disabled
                return new int[_currentVote.Options.Count];
            }

            var counts = new int[_currentVote.Options.Count];
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = _currentVote.GetVoteCount(i);
            }
            return counts;
        }

        /// <summary>
        /// Get the current leader(s) (may be multiple if tied).
        /// </summary>
        public List<int> GetCurrentLeaders()
        {
            if (_currentVote == null) return new List<int>();

            var counts = GetVoteCounts();
            int maxVotes = counts.Max();

            if (maxVotes == 0) return new List<int>();

            return counts.Select((count, index) => new { count, index })
                        .Where(x => x.count == maxVotes)
                        .Select(x => x.index)
                        .ToList();
        }
        #endregion

        #region Private Methods - Vote Logic
        private void EndVote()
        {
            if (_state != VoteState.Active) return;

            _state = VoteState.Completed;

            // Calculate result
            var result = CalculateResult();

            if (result.WasTie && _tieBreaker == TieBreaker.HostChoice)
            {
                // Need host decision
                _pendingTieIndices = result.TiedIndices;
                OnTieNeedsDecision?.Invoke(result.TiedIndices.Select(i => _currentVote.Options[i]).ToList());

                if (_showToasts)
                {
                    EOSToastManager.Warning("Tie!", "Host must choose the winner");
                }
                return;
            }

            if (result.WasTie && _tieBreaker == TieBreaker.Revote)
            {
                // Start new vote with only tied options
                var tiedOptions = result.TiedIndices.Select(i => _currentVote.Options[i]).ToList();
                _state = VoteState.Inactive;
                _ = StartVoteAsync(_currentVote.Title + " (Revote)", tiedOptions);
                return;
            }

            _ = FinalizeWinner(result.WinnerIndex);
        }

        private VoteResult CalculateResult()
        {
            var counts = new int[_currentVote.Options.Count];
            foreach (var vote in _currentVote.Votes.Values)
            {
                if (vote >= 0 && vote < counts.Length)
                    counts[vote]++;
            }

            int maxVotes = counts.Max();
            var tiedIndices = counts.Select((count, index) => new { count, index })
                                   .Where(x => x.count == maxVotes)
                                   .Select(x => x.index)
                                   .ToList();

            bool wasTie = tiedIndices.Count > 1;
            int winnerIndex;

            if (wasTie)
            {
                switch (_tieBreaker)
                {
                    case TieBreaker.Random:
                        winnerIndex = tiedIndices[UnityEngine.Random.Range(0, tiedIndices.Count)];
                        break;
                    case TieBreaker.FirstOption:
                        winnerIndex = tiedIndices[0];
                        break;
                    case TieBreaker.HostChoice:
                    case TieBreaker.Revote:
                    default:
                        winnerIndex = tiedIndices[0]; // Will be resolved separately
                        break;
                }
            }
            else
            {
                winnerIndex = tiedIndices.Count > 0 ? tiedIndices[0] : 0;
            }

            return new VoteResult
            {
                Winner = _currentVote.Options[winnerIndex],
                WinnerIndex = winnerIndex,
                WinnerVotes = maxVotes,
                WasTie = wasTie,
                TiedIndices = tiedIndices
            };
        }

        private async Task FinalizeWinner(int winnerIndex)
        {
            var winner = _currentVote.Options[winnerIndex];
            int winnerVotes = _currentVote.GetVoteCount(winnerIndex);

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager",
                $"Vote ended: {winner.DisplayName} wins with {winnerVotes} votes");

            // Clear vote attribute
            await _lobbyManager.SetLobbyAttributeAsync(VOTE_ATTR_KEY, "");

            OnVoteEnded?.Invoke(_currentVote, winner, winnerIndex);

            if (_showToasts)
            {
                EOSToastManager.Success("Vote Result", $"{winner.DisplayName} wins! ({winnerVotes} votes)");
            }

            _state = VoteState.Inactive;
            _currentVote = null;
            _pendingTieIndices = null;
        }
        #endregion

        #region Private Methods - Event Handlers
        private void HandleLobbyAttributeUpdated(string key, string value)
        {
            if (key != VOTE_ATTR_KEY) return;

            if (string.IsNullOrEmpty(value))
            {
                // Vote was cleared
                if (_state == VoteState.Active)
                {
                    _state = VoteState.Inactive;
                    _currentVote = null;
                }
                return;
            }

            // Parse vote data (only if we're not the initiator)
            var voteData = DeserializeVote(value);
            if (voteData == null) return;

            // Don't overwrite if we initiated
            if (_isHost && _currentVote != null && _currentVote.InitiatorPuid == _localPuid)
            {
                return;
            }

            _currentVote = voteData;
            _state = VoteState.Active;
            _voteEndTime = Time.time + voteData.Duration;
            _lastTickTime = Time.time;

            OnVoteStarted?.Invoke(voteData);

            if (_showToasts)
            {
                string optionList = string.Join(", ", voteData.Options.Select(o => o.DisplayName));
                EOSToastManager.Info(voteData.Title, $"Options: {optionList}\nTime: {voteData.Duration}s");
            }
        }

        private void HandleMemberAttributeUpdated(string memberPuid, string key, string value)
        {
            if (!key.StartsWith(VOTE_RESPONSE_PREFIX)) return;
            if (_currentVote == null || _state != VoteState.Active) return;

            // Security: Verify member is still in lobby (host-authority validation)
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && !lobbyManager.IsPlayerInLobby(memberPuid))
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSMapVoteManager",
                    $"Rejected vote from {memberPuid}: not in lobby");
                return;
            }

            // Security: Rate limit votes (prevent spam)
            if (Security.SecurityValidator.IsRateLimited($"mapvote_{memberPuid}", 20))
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSMapVoteManager",
                    $"Rejected vote from {memberPuid}: rate limited");
                return;
            }

            if (int.TryParse(value, out int optionIndex))
            {
                if (optionIndex >= 0 && optionIndex < _currentVote.Options.Count)
                {
                    _currentVote.Votes[memberPuid] = optionIndex;

                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSMapVoteManager",
                        $"Vote received from {memberPuid}: option {optionIndex}");

                    OnVoteCast?.Invoke(memberPuid, optionIndex);

                    if (_showToasts && _showLiveResults)
                    {
                        string name = GetPlayerName(memberPuid);
                        string optionName = _currentVote.Options[optionIndex].DisplayName;
                        EOSToastManager.Info("Vote", $"{name} voted for {optionName}");
                    }
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

        private string SerializeVote(MapVoteData data)
        {
            // Format: title|duration|option1Id:option1Name|option2Id:option2Name|...
            var parts = new List<string>
            {
                data.Title,
                data.Duration.ToString("F0")
            };

            foreach (var opt in data.Options)
            {
                parts.Add($"{opt.Id}:{opt.DisplayName}:{opt.Category}");
            }

            return string.Join("|", parts);
        }

        private MapVoteData DeserializeVote(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;

            try
            {
                var parts = data.Split('|');
                if (parts.Length < 4) return null; // title + duration + at least 2 options

                var vote = new MapVoteData
                {
                    Title = parts[0],
                    Duration = float.TryParse(parts[1], out float d) ? d : _voteDuration
                };

                for (int i = 2; i < parts.Length; i++)
                {
                    var optParts = parts[i].Split(':');
                    if (optParts.Length >= 2)
                    {
                        vote.Options.Add(new VoteOption
                        {
                            Id = optParts[0],
                            DisplayName = optParts[1],
                            Category = optParts.Length > 2 ? optParts[2] : "map"
                        });
                    }
                }

                return vote;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Preset Vote Templates
        /// <summary>
        /// Common map names for quick setup.
        /// </summary>
        public static class CommonMaps
        {
            public static readonly VoteOption[] FPS = {
                new VoteOption("dust2", "Dust 2", "map"),
                new VoteOption("inferno", "Inferno", "map"),
                new VoteOption("mirage", "Mirage", "map"),
                new VoteOption("nuke", "Nuke", "map"),
            };

            public static readonly VoteOption[] Arena = {
                new VoteOption("colosseum", "Colosseum", "map"),
                new VoteOption("pit", "The Pit", "map"),
                new VoteOption("tower", "Tower", "map"),
                new VoteOption("arena", "Arena", "map"),
            };
        }

        /// <summary>
        /// Common game modes for quick setup.
        /// </summary>
        public static class CommonModes
        {
            public static readonly VoteOption[] Standard = {
                new VoteOption("deathmatch", "Deathmatch", "mode"),
                new VoteOption("tdm", "Team Deathmatch", "mode"),
                new VoteOption("ctf", "Capture the Flag", "mode"),
                new VoteOption("koth", "King of the Hill", "mode"),
            };

            public static readonly VoteOption[] Casual = {
                new VoteOption("ffa", "Free For All", "mode"),
                new VoteOption("infection", "Infection", "mode"),
                new VoteOption("gungame", "Gun Game", "mode"),
            };
        }
        #endregion
    }
}
