using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Replay;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Tracks match history - games played, participants, outcomes.
    /// Persists to cloud storage for cross-device access.
    /// </summary>
    public class EOSMatchHistory : MonoBehaviour
    {
        #region Singleton

        private static EOSMatchHistory _instance;
        public static EOSMatchHistory Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSMatchHistory>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSMatchHistory");
                        _instance = go.AddComponent<EOSMatchHistory>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string HISTORY_FILE = "match_history.json";
        private const int MAX_HISTORY_ENTRIES = 100;

        #endregion

        #region Events

        /// <summary>Fired when a match is recorded.</summary>
        public event Action<MatchRecord> OnMatchRecorded;

        /// <summary>Fired when history is loaded from cloud.</summary>
        public event Action<List<MatchRecord>> OnHistoryLoaded;

        #endregion

        #region Private Fields

        private List<MatchRecord> _history = new();
        private bool _isDirty;
        private MatchRecord _currentMatch;
        private bool _matchInProgress;

        #endregion

        #region Public Properties

        /// <summary>All match history (newest first).</summary>
        public IReadOnlyList<MatchRecord> History => _history;

        /// <summary>Number of matches in history.</summary>
        public int MatchCount => _history.Count;

        /// <summary>Whether a match is currently being tracked.</summary>
        public bool IsMatchInProgress => _matchInProgress;

        /// <summary>Current match being tracked (if any).</summary>
        public MatchRecord? CurrentMatch => _matchInProgress ? _currentMatch : null;

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
            // Subscribe to lobby events
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined += OnLobbyJoined;
                lobbyManager.OnLobbyLeft += OnLobbyLeft;
            }

            // Load history from cloud
            _ = LoadHistoryAsync();
        }

        private void OnDestroy()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null)
            {
                lobbyManager.OnLobbyJoined -= OnLobbyJoined;
                lobbyManager.OnLobbyLeft -= OnLobbyLeft;
            }

            // Save if dirty
            if (_isDirty)
            {
                _ = SaveHistoryAsync();
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start tracking a new match.
        /// Call this when the game actually starts (not just lobby join).
        /// </summary>
        public void StartMatch(string gameMode = null, string mapName = null)
        {
            if (_matchInProgress)
            {
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", "Match already in progress, ending previous");
                EndMatch(MatchOutcome.Abandoned);
            }

            var lobbyManager = EOSLobbyManager.Instance;
            bool inLobby = lobbyManager != null && lobbyManager.IsInLobby;
            var lobby = inLobby ? lobbyManager.CurrentLobby : default;

            _currentMatch = new MatchRecord
            {
                MatchId = Guid.NewGuid().ToString("N").Substring(0, 12),
                StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LobbyCode = inLobby ? lobby.JoinCode : "Unknown",
                GameMode = gameMode ?? (inLobby ? lobby.GameMode : "Unknown"),
                MapName = mapName ?? (inLobby ? lobby.Map : "Unknown"),
                Participants = new List<MatchParticipant>(),
                Outcome = MatchOutcome.InProgress
            };

            // Add local player as participant
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (!string.IsNullOrEmpty(localPuid))
            {
                string localName = EOSPlayerRegistry.Instance?.GetPlayerName(localPuid) ?? "Local Player";
                _currentMatch.Participants.Add(new MatchParticipant
                {
                    Puid = localPuid,
                    DisplayName = localName,
                    Team = 0,
                    Score = 0,
                    IsLocalPlayer = true
                });
            }

            _matchInProgress = true;
            EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", $"Match started: {_currentMatch.MatchId}");

            // Auto-start replay recording
            var recorder = EOSReplayRecorder.Instance;
            if (recorder != null && recorder.AutoRecordEnabled)
            {
                recorder.StartRecording(_currentMatch.MatchId);
            }
        }

        /// <summary>
        /// Add a participant to the current match.
        /// </summary>
        public void AddParticipant(string puid, string displayName = null, int team = 0)
        {
            if (!_matchInProgress) return;
            if (_currentMatch.Participants.Exists(p => p.Puid == puid)) return;

            string name = displayName ?? EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Unknown";
            _currentMatch.Participants.Add(new MatchParticipant
            {
                Puid = puid,
                DisplayName = name,
                Team = team,
                Score = 0,
                IsLocalPlayer = puid == EOSManager.Instance?.LocalProductUserId?.ToString()
            });
        }

        /// <summary>
        /// Update a participant's score during the match.
        /// </summary>
        public void UpdateParticipantScore(string puid, int score, int? team = null)
        {
            if (!_matchInProgress) return;

            var participant = _currentMatch.Participants.Find(p => p.Puid == puid);
            if (participant.Puid != null)
            {
                var index = _currentMatch.Participants.IndexOf(participant);
                participant.Score = score;
                if (team.HasValue) participant.Team = team.Value;
                _currentMatch.Participants[index] = participant;
            }
        }

        /// <summary>
        /// Update the local player's score.
        /// </summary>
        public void UpdateLocalScore(int score, int? team = null)
        {
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (!string.IsNullOrEmpty(localPuid))
            {
                UpdateParticipantScore(localPuid, score, team);
            }
        }

        /// <summary>
        /// End the current match and record it to history.
        /// </summary>
        public void EndMatch(MatchOutcome outcome, string winnerPuid = null)
        {
            if (!_matchInProgress) return;

            _currentMatch.EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _currentMatch.Outcome = outcome;
            _currentMatch.WinnerPuid = winnerPuid;

            // Calculate duration
            _currentMatch.DurationSeconds = (int)((_currentMatch.EndTime - _currentMatch.StartTime) / 1000);

            // Add to history
            _history.Insert(0, _currentMatch);

            // Trim if too many
            while (_history.Count > MAX_HISTORY_ENTRIES)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            _isDirty = true;
            _matchInProgress = false;

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory",
                $"Match ended: {_currentMatch.MatchId} - {outcome} ({_currentMatch.DurationSeconds}s)");

            // Stop replay recording
            var recorder = EOSReplayRecorder.Instance;
            if (recorder != null && recorder.IsRecording)
            {
                _ = recorder.StopAndSaveAsync();
            }

            OnMatchRecorded?.Invoke(_currentMatch);

            // Auto-save
            _ = SaveHistoryAsync();
        }

        /// <summary>
        /// Get recent matches (last N).
        /// </summary>
        public List<MatchRecord> GetRecentMatches(int count = 10)
        {
            return _history.GetRange(0, Math.Min(count, _history.Count));
        }

        /// <summary>
        /// Get matches with a specific player.
        /// </summary>
        public List<MatchRecord> GetMatchesWithPlayer(string puid)
        {
            var result = new List<MatchRecord>();
            foreach (var match in _history)
            {
                if (match.Participants.Exists(p => p.Puid == puid))
                {
                    result.Add(match);
                }
            }
            return result;
        }

        /// <summary>
        /// Get win/loss statistics for local player.
        /// </summary>
        public (int wins, int losses, int draws, int total) GetLocalStats()
        {
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return (0, 0, 0, 0);

            int wins = 0, losses = 0, draws = 0;

            foreach (var match in _history)
            {
                if (match.Outcome == MatchOutcome.InProgress || match.Outcome == MatchOutcome.Abandoned)
                    continue;

                bool isWinner = match.WinnerPuid == localPuid;
                bool participated = match.Participants.Exists(p => p.Puid == localPuid);

                if (!participated) continue;

                switch (match.Outcome)
                {
                    case MatchOutcome.Win when isWinner:
                    case MatchOutcome.Loss when !isWinner && !string.IsNullOrEmpty(match.WinnerPuid):
                        wins++;
                        break;
                    case MatchOutcome.Loss when isWinner:
                    case MatchOutcome.Win when !isWinner:
                        losses++;
                        break;
                    case MatchOutcome.Draw:
                        draws++;
                        break;
                }
            }

            return (wins, losses, draws, wins + losses + draws);
        }

        /// <summary>
        /// Clear all match history.
        /// </summary>
        public async Task ClearHistoryAsync()
        {
            _history.Clear();
            _isDirty = true;
            await SaveHistoryAsync();
            EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", "History cleared");
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Save match history to cloud.
        /// </summary>
        public async Task<Result> SaveHistoryAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return Result.NotConfigured;

            try
            {
                var data = new MatchHistoryData
                {
                    Version = 1,
                    Matches = _history.ToArray(),
                    SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var result = await storage.WriteFileAsJsonAsync(HISTORY_FILE, data);
                if (result == Result.Success)
                {
                    _isDirty = false;
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", $"Saved {_history.Count} matches");
                }
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSMatchHistory] Failed to save: {e.Message}");
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Load match history from cloud.
        /// </summary>
        public async Task<Result> LoadHistoryAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return Result.NotConfigured;

            try
            {
                var (result, data) = await storage.ReadFileAsJsonAsync<MatchHistoryData>(HISTORY_FILE);

                if (result == Result.Success && data.Matches != null)
                {
                    _history = new List<MatchRecord>(data.Matches);
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", $"Loaded {_history.Count} matches");
                    OnHistoryLoaded?.Invoke(_history);
                }

                return result;
            }
            catch (Exception e)
            {
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", $"No history found: {e.Message}");
                return Result.NotFound;
            }
        }

        #endregion

        #region Event Handlers

        private void OnLobbyJoined(LobbyData lobby)
        {
            // Don't auto-start match on lobby join - games should call StartMatch explicitly
            EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", $"Lobby joined: {lobby.JoinCode}");
        }

        private void OnLobbyLeft()
        {
            // If match was in progress, mark as abandoned
            if (_matchInProgress)
            {
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSMatchHistory", "Left lobby during match - marking abandoned");
                EndMatch(MatchOutcome.Abandoned);
            }
        }

        #endregion
    }

    /// <summary>
    /// A single match record.
    /// </summary>
    [Serializable]
    public struct MatchRecord
    {
        public string MatchId;
        public long StartTime;
        public long EndTime;
        public int DurationSeconds;
        public string LobbyCode;
        public string GameMode;
        public string MapName;
        public List<MatchParticipant> Participants;
        public MatchOutcome Outcome;
        public string WinnerPuid;

        public DateTime StartDateTime => DateTimeOffset.FromUnixTimeMilliseconds(StartTime).LocalDateTime;
        public DateTime EndDateTime => DateTimeOffset.FromUnixTimeMilliseconds(EndTime).LocalDateTime;
    }

    /// <summary>
    /// A participant in a match.
    /// </summary>
    [Serializable]
    public struct MatchParticipant
    {
        public string Puid;
        public string DisplayName;
        public int Team;
        public int Score;
        public bool IsLocalPlayer;
    }

    /// <summary>
    /// Match outcome.
    /// </summary>
    public enum MatchOutcome
    {
        InProgress,
        Win,
        Loss,
        Draw,
        Abandoned
    }

    /// <summary>
    /// Serializable container for match history.
    /// </summary>
    [Serializable]
    public struct MatchHistoryData
    {
        public int Version;
        public MatchRecord[] Matches;
        public long SavedAt;
    }
}
