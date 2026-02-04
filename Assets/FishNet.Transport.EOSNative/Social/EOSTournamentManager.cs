using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Manages tournament brackets with single/double elimination formats.
    /// </summary>
    public class EOSTournamentManager : MonoBehaviour
    {
        #region Singleton

        private static EOSTournamentManager _instance;
        public static EOSTournamentManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSTournamentManager>();
#else
                    _instance = FindObjectOfType<EOSTournamentManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a tournament is created.</summary>
        public event Action<Tournament> OnTournamentCreated;

        /// <summary>Fired when a tournament starts.</summary>
        public event Action<Tournament> OnTournamentStarted;

        /// <summary>Fired when a tournament ends.</summary>
        public event Action<Tournament, TournamentParticipant> OnTournamentEnded; // winner

        /// <summary>Fired when a match is ready to be played.</summary>
        public event Action<TournamentMatch> OnMatchReady;

        /// <summary>Fired when a match result is reported.</summary>
        public event Action<TournamentMatch> OnMatchCompleted;

        /// <summary>Fired when a participant is eliminated.</summary>
        public event Action<TournamentParticipant> OnParticipantEliminated;

        /// <summary>Fired when bracket advances to next round.</summary>
        public event Action<int> OnRoundAdvanced; // round number

        #endregion

        #region Inspector Settings

        [Header("Tournament Settings")]
        [Tooltip("Default format for new tournaments")]
        [SerializeField] private TournamentFormat _defaultFormat = TournamentFormat.SingleElimination;

        [Tooltip("Default seeding method")]
        [SerializeField] private SeedingMethod _defaultSeeding = SeedingMethod.Random;

        [Tooltip("Allow byes for non-power-of-2 participant counts")]
        [SerializeField] private bool _allowByes = true;

        [Tooltip("Auto-advance when match results are reported")]
        [SerializeField] private bool _autoAdvance = true;

        #endregion

        #region Public Properties

        /// <summary>Currently active tournament.</summary>
        public Tournament ActiveTournament { get; private set; }

        /// <summary>Whether a tournament is currently in progress.</summary>
        public bool IsTournamentActive => ActiveTournament != null && ActiveTournament.State == TournamentState.InProgress;

        /// <summary>Default tournament format.</summary>
        public TournamentFormat DefaultFormat
        {
            get => _defaultFormat;
            set => _defaultFormat = value;
        }

        /// <summary>Default seeding method.</summary>
        public SeedingMethod DefaultSeeding
        {
            get => _defaultSeeding;
            set => _defaultSeeding = value;
        }

        #endregion

        #region Private Fields

        private readonly List<Tournament> _tournamentHistory = new();

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

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Tournament Creation

        /// <summary>
        /// Create a new tournament.
        /// </summary>
        public Tournament CreateTournament(string name, TournamentFormat format = TournamentFormat.SingleElimination)
        {
            if (ActiveTournament != null && ActiveTournament.State != TournamentState.Completed)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Cannot create tournament while one is active");
                return null;
            }

            var tournament = new Tournament
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Format = format,
                State = TournamentState.Registration,
                CreatedAt = DateTime.UtcNow,
                SeedingMethod = _defaultSeeding
            };

            ActiveTournament = tournament;
            EOSDebugLogger.Log(DebugCategory.Social, "EOSTournamentManager",
                $"Created tournament: {name} ({format})");

            OnTournamentCreated?.Invoke(tournament);
            return tournament;
        }

        /// <summary>
        /// Create a tournament with options.
        /// </summary>
        public Tournament CreateTournament(TournamentOptions options)
        {
            var tournament = CreateTournament(options.Name, options.Format);
            if (tournament != null)
            {
                tournament.Description = options.Description;
                tournament.MaxParticipants = options.MaxParticipants;
                tournament.SeedingMethod = options.SeedingMethod;
                tournament.BestOf = options.BestOf;
                tournament.GrandFinalsBestOf = options.GrandFinalsBestOf;
                tournament.AllowLateRegistration = options.AllowLateRegistration;
            }
            return tournament;
        }

        #endregion

        #region Public API - Participant Management

        /// <summary>
        /// Register a participant for the tournament.
        /// </summary>
        public bool RegisterParticipant(string participantId, string name, int? seed = null)
        {
            if (ActiveTournament == null)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "No active tournament");
                return false;
            }

            if (ActiveTournament.State != TournamentState.Registration)
            {
                if (!ActiveTournament.AllowLateRegistration || ActiveTournament.State != TournamentState.InProgress)
                {
                    EOSDebugLogger.LogWarning("EOSTournamentManager", "Registration is closed");
                    return false;
                }
            }

            if (ActiveTournament.MaxParticipants > 0 &&
                ActiveTournament.Participants.Count >= ActiveTournament.MaxParticipants)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Tournament is full");
                return false;
            }

            if (ActiveTournament.Participants.Any(p => p.Id == participantId))
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Participant already registered");
                return false;
            }

            var participant = new TournamentParticipant
            {
                Id = participantId,
                Name = name,
                Seed = seed ?? 0,
                RegisteredAt = DateTime.UtcNow
            };

            ActiveTournament.Participants.Add(participant);
            EOSDebugLogger.Log(DebugCategory.Social, "EOSTournamentManager",
                $"Registered participant: {name}");

            return true;
        }

        /// <summary>
        /// Register a team for the tournament.
        /// </summary>
        public bool RegisterTeam(string teamId, string teamName, List<string> memberIds, int? seed = null)
        {
            if (ActiveTournament == null) return false;

            var participant = new TournamentParticipant
            {
                Id = teamId,
                Name = teamName,
                Seed = seed ?? 0,
                IsTeam = true,
                TeamMemberIds = memberIds,
                RegisteredAt = DateTime.UtcNow
            };

            ActiveTournament.Participants.Add(participant);
            return true;
        }

        /// <summary>
        /// Unregister a participant.
        /// </summary>
        public bool UnregisterParticipant(string participantId)
        {
            if (ActiveTournament == null) return false;
            if (ActiveTournament.State == TournamentState.InProgress)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Cannot unregister during active tournament");
                return false;
            }

            return ActiveTournament.Participants.RemoveAll(p => p.Id == participantId) > 0;
        }

        /// <summary>
        /// Get participant by ID.
        /// </summary>
        public TournamentParticipant GetParticipant(string participantId)
        {
            return ActiveTournament?.Participants.FirstOrDefault(p => p.Id == participantId);
        }

        #endregion

        #region Public API - Tournament Flow

        /// <summary>
        /// Start the tournament and generate brackets.
        /// </summary>
        public bool StartTournament()
        {
            if (ActiveTournament == null)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "No active tournament");
                return false;
            }

            if (ActiveTournament.State != TournamentState.Registration)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Tournament already started");
                return false;
            }

            if (ActiveTournament.Participants.Count < 2)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Need at least 2 participants");
                return false;
            }

            // Apply seeding
            ApplySeeding();

            // Generate bracket
            GenerateBracket();

            ActiveTournament.State = TournamentState.InProgress;
            ActiveTournament.StartedAt = DateTime.UtcNow;
            ActiveTournament.CurrentRound = 1;

            EOSDebugLogger.Log(DebugCategory.Social, "EOSTournamentManager",
                $"Tournament started: {ActiveTournament.Participants.Count} participants");

            OnTournamentStarted?.Invoke(ActiveTournament);

            // Fire match ready events for first round
            foreach (var match in GetCurrentRoundMatches())
            {
                if (match.State == MatchState.Ready)
                    OnMatchReady?.Invoke(match);
            }

            return true;
        }

        /// <summary>
        /// Report match result.
        /// </summary>
        public bool ReportMatchResult(string matchId, string winnerId, int winnerScore = 0, int loserScore = 0)
        {
            if (ActiveTournament == null || ActiveTournament.State != TournamentState.InProgress)
                return false;

            var match = FindMatch(matchId);
            if (match == null)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", $"Match not found: {matchId}");
                return false;
            }

            if (match.State != MatchState.Ready && match.State != MatchState.InProgress)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Match is not ready to report");
                return false;
            }

            // Validate winner is in the match
            if (match.Participant1Id != winnerId && match.Participant2Id != winnerId)
            {
                EOSDebugLogger.LogWarning("EOSTournamentManager", "Winner not in match");
                return false;
            }

            match.WinnerId = winnerId;
            match.LoserId = match.Participant1Id == winnerId ? match.Participant2Id : match.Participant1Id;
            match.WinnerScore = winnerScore;
            match.LoserScore = loserScore;
            match.State = MatchState.Completed;
            match.CompletedAt = DateTime.UtcNow;

            // Update participant stats
            var winner = GetParticipant(winnerId);
            var loser = GetParticipant(match.LoserId);
            if (winner != null) winner.Wins++;
            if (loser != null)
            {
                loser.Losses++;
                if (ActiveTournament.Format == TournamentFormat.SingleElimination ||
                    (ActiveTournament.Format == TournamentFormat.DoubleElimination && loser.Losses >= 2))
                {
                    loser.IsEliminated = true;
                    loser.FinalPlacement = CalculatePlacement(loser);
                    OnParticipantEliminated?.Invoke(loser);
                }
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSTournamentManager",
                $"Match completed: {winner?.Name} defeats {loser?.Name}");

            OnMatchCompleted?.Invoke(match);

            if (_autoAdvance)
                AdvanceBracket();

            return true;
        }

        /// <summary>
        /// Advance bracket after match completions.
        /// </summary>
        public void AdvanceBracket()
        {
            if (ActiveTournament == null) return;

            // Check if all matches in current round are complete
            var currentMatches = GetCurrentRoundMatches();
            if (currentMatches.All(m => m.State == MatchState.Completed || m.State == MatchState.Bye))
            {
                // Progress winners to next round
                foreach (var match in currentMatches.Where(m => m.State == MatchState.Completed))
                {
                    AdvanceWinner(match);
                }

                // Check if tournament is complete
                if (IsTournamentComplete())
                {
                    CompleteTournament();
                    return;
                }

                // Move to next round
                ActiveTournament.CurrentRound++;
                OnRoundAdvanced?.Invoke(ActiveTournament.CurrentRound);

                // Fire ready events for new matches
                foreach (var match in GetCurrentRoundMatches())
                {
                    UpdateMatchReadiness(match);
                    if (match.State == MatchState.Ready)
                        OnMatchReady?.Invoke(match);
                }
            }
        }

        /// <summary>
        /// Cancel the active tournament.
        /// </summary>
        public void CancelTournament()
        {
            if (ActiveTournament == null) return;

            ActiveTournament.State = TournamentState.Cancelled;
            _tournamentHistory.Add(ActiveTournament);
            ActiveTournament = null;

            EOSDebugLogger.Log(DebugCategory.Social, "EOSTournamentManager", "Tournament cancelled");
        }

        #endregion

        #region Public API - Queries

        /// <summary>
        /// Get matches for current round.
        /// </summary>
        public List<TournamentMatch> GetCurrentRoundMatches()
        {
            if (ActiveTournament == null) return new List<TournamentMatch>();
            return ActiveTournament.Matches
                .Where(m => m.Round == ActiveTournament.CurrentRound && m.Bracket == BracketType.Winners)
                .ToList();
        }

        /// <summary>
        /// Get all matches for a specific round.
        /// </summary>
        public List<TournamentMatch> GetRoundMatches(int round, BracketType bracket = BracketType.Winners)
        {
            if (ActiveTournament == null) return new List<TournamentMatch>();
            return ActiveTournament.Matches
                .Where(m => m.Round == round && m.Bracket == bracket)
                .ToList();
        }

        /// <summary>
        /// Get all matches for a participant.
        /// </summary>
        public List<TournamentMatch> GetParticipantMatches(string participantId)
        {
            if (ActiveTournament == null) return new List<TournamentMatch>();
            return ActiveTournament.Matches
                .Where(m => m.Participant1Id == participantId || m.Participant2Id == participantId)
                .OrderBy(m => m.Round)
                .ToList();
        }

        /// <summary>
        /// Get bracket data for visualization.
        /// </summary>
        public BracketData GetBracketData(BracketType bracket = BracketType.Winners)
        {
            if (ActiveTournament == null) return null;

            var matches = ActiveTournament.Matches.Where(m => m.Bracket == bracket).ToList();
            var rounds = matches.Select(m => m.Round).Distinct().OrderBy(r => r).ToList();

            return new BracketData
            {
                TournamentId = ActiveTournament.Id,
                TournamentName = ActiveTournament.Name,
                BracketType = bracket,
                TotalRounds = rounds.Count,
                Rounds = rounds.Select(r => new BracketRound
                {
                    RoundNumber = r,
                    RoundName = GetRoundName(r, rounds.Count, bracket),
                    Matches = matches.Where(m => m.Round == r).ToList()
                }).ToList()
            };
        }

        /// <summary>
        /// Get tournament standings/rankings.
        /// </summary>
        public List<TournamentParticipant> GetStandings()
        {
            if (ActiveTournament == null) return new List<TournamentParticipant>();

            return ActiveTournament.Participants
                .OrderBy(p => p.FinalPlacement > 0 ? p.FinalPlacement : int.MaxValue)
                .ThenByDescending(p => p.Wins)
                .ThenBy(p => p.Losses)
                .ToList();
        }

        /// <summary>
        /// Get match by ID.
        /// </summary>
        public TournamentMatch FindMatch(string matchId)
        {
            return ActiveTournament?.Matches.FirstOrDefault(m => m.Id == matchId);
        }

        /// <summary>
        /// Get total number of rounds.
        /// </summary>
        public int GetTotalRounds()
        {
            if (ActiveTournament == null) return 0;
            return ActiveTournament.Matches.Max(m => m.Round);
        }

        #endregion

        #region Private Methods - Bracket Generation

        private void ApplySeeding()
        {
            var participants = ActiveTournament.Participants;

            switch (ActiveTournament.SeedingMethod)
            {
                case SeedingMethod.Random:
                    // Shuffle participants
                    var rng = new System.Random();
                    for (int i = participants.Count - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (participants[i], participants[j]) = (participants[j], participants[i]);
                    }
                    // Assign seeds
                    for (int i = 0; i < participants.Count; i++)
                        participants[i].Seed = i + 1;
                    break;

                case SeedingMethod.ByRating:
                    // Sort by rating (descending)
                    var ordered = participants.OrderByDescending(p => p.Rating).ToList();
                    for (int i = 0; i < ordered.Count; i++)
                        ordered[i].Seed = i + 1;
                    ActiveTournament.Participants = ordered;
                    break;

                case SeedingMethod.Manual:
                    // Seeds already set manually, just sort
                    ActiveTournament.Participants = participants.OrderBy(p => p.Seed).ToList();
                    break;
            }
        }

        private void GenerateBracket()
        {
            ActiveTournament.Matches.Clear();

            switch (ActiveTournament.Format)
            {
                case TournamentFormat.SingleElimination:
                    GenerateSingleEliminationBracket();
                    break;
                case TournamentFormat.DoubleElimination:
                    GenerateDoubleEliminationBracket();
                    break;
                case TournamentFormat.RoundRobin:
                    GenerateRoundRobinBracket();
                    break;
            }
        }

        private void GenerateSingleEliminationBracket()
        {
            var participants = ActiveTournament.Participants;
            int count = participants.Count;
            int bracketSize = GetNextPowerOfTwo(count);
            int totalRounds = (int)Math.Log2(bracketSize);
            int byeCount = bracketSize - count;

            // Generate first round matches with seeding consideration
            var seededOrder = GetSeededMatchOrder(bracketSize);
            int matchIndex = 0;
            int round1MatchCount = bracketSize / 2;

            for (int i = 0; i < round1MatchCount; i++)
            {
                var match = new TournamentMatch
                {
                    Id = Guid.NewGuid().ToString(),
                    Round = 1,
                    MatchNumber = i + 1,
                    Bracket = BracketType.Winners,
                    BestOf = ActiveTournament.BestOf
                };

                int seed1 = seededOrder[i * 2];
                int seed2 = seededOrder[i * 2 + 1];

                // Assign participants or byes
                if (seed1 <= count)
                    match.Participant1Id = participants[seed1 - 1].Id;
                if (seed2 <= count)
                    match.Participant2Id = participants[seed2 - 1].Id;

                // Handle byes
                if (match.Participant1Id == null && match.Participant2Id != null)
                {
                    match.WinnerId = match.Participant2Id;
                    match.State = MatchState.Bye;
                }
                else if (match.Participant2Id == null && match.Participant1Id != null)
                {
                    match.WinnerId = match.Participant1Id;
                    match.State = MatchState.Bye;
                }
                else if (match.Participant1Id != null && match.Participant2Id != null)
                {
                    match.State = MatchState.Ready;
                }

                ActiveTournament.Matches.Add(match);
                matchIndex++;
            }

            // Generate subsequent rounds
            for (int round = 2; round <= totalRounds; round++)
            {
                int matchCount = bracketSize / (int)Math.Pow(2, round);
                for (int i = 0; i < matchCount; i++)
                {
                    var match = new TournamentMatch
                    {
                        Id = Guid.NewGuid().ToString(),
                        Round = round,
                        MatchNumber = i + 1,
                        Bracket = BracketType.Winners,
                        State = MatchState.Pending,
                        BestOf = round == totalRounds ? ActiveTournament.GrandFinalsBestOf : ActiveTournament.BestOf
                    };
                    ActiveTournament.Matches.Add(match);
                }
            }

            // Link matches (which matches feed into which)
            LinkMatches();
        }

        private void GenerateDoubleEliminationBracket()
        {
            // First generate winners bracket
            GenerateSingleEliminationBracket();

            int winnersRounds = GetTotalRounds();

            // Generate losers bracket
            // Losers bracket has roughly 2x-1 rounds
            int losersRounds = (winnersRounds - 1) * 2;
            int losersMatchNumber = 1;

            for (int round = 1; round <= losersRounds; round++)
            {
                // Calculate matches in this losers round
                int matchCount = CalculateLosersRoundMatchCount(round, winnersRounds);

                for (int i = 0; i < matchCount; i++)
                {
                    var match = new TournamentMatch
                    {
                        Id = Guid.NewGuid().ToString(),
                        Round = round,
                        MatchNumber = losersMatchNumber++,
                        Bracket = BracketType.Losers,
                        State = MatchState.Pending,
                        BestOf = ActiveTournament.BestOf
                    };
                    ActiveTournament.Matches.Add(match);
                }
            }

            // Grand finals (winner of winners vs winner of losers)
            var grandFinals = new TournamentMatch
            {
                Id = Guid.NewGuid().ToString(),
                Round = winnersRounds + 1,
                MatchNumber = 1,
                Bracket = BracketType.GrandFinals,
                State = MatchState.Pending,
                BestOf = ActiveTournament.GrandFinalsBestOf
            };
            ActiveTournament.Matches.Add(grandFinals);

            // Potential reset match if losers bracket winner wins
            var grandFinalsReset = new TournamentMatch
            {
                Id = Guid.NewGuid().ToString(),
                Round = winnersRounds + 2,
                MatchNumber = 1,
                Bracket = BracketType.GrandFinalsReset,
                State = MatchState.Pending,
                BestOf = ActiveTournament.GrandFinalsBestOf
            };
            ActiveTournament.Matches.Add(grandFinalsReset);
        }

        private void GenerateRoundRobinBracket()
        {
            var participants = ActiveTournament.Participants;
            int count = participants.Count;

            // Generate all matchups
            int round = 1;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    var match = new TournamentMatch
                    {
                        Id = Guid.NewGuid().ToString(),
                        Round = round,
                        MatchNumber = ActiveTournament.Matches.Count + 1,
                        Bracket = BracketType.RoundRobin,
                        Participant1Id = participants[i].Id,
                        Participant2Id = participants[j].Id,
                        State = MatchState.Ready,
                        BestOf = ActiveTournament.BestOf
                    };
                    ActiveTournament.Matches.Add(match);
                }
            }
        }

        private void LinkMatches()
        {
            var winnerMatches = ActiveTournament.Matches
                .Where(m => m.Bracket == BracketType.Winners)
                .OrderBy(m => m.Round)
                .ThenBy(m => m.MatchNumber)
                .ToList();

            for (int i = 0; i < winnerMatches.Count; i++)
            {
                var match = winnerMatches[i];
                if (match.Round > 1)
                {
                    // Find feeder matches from previous round
                    int prevRoundStart = winnerMatches
                        .TakeWhile(m => m.Round < match.Round)
                        .Count();
                    int prevRoundMatchCount = winnerMatches
                        .Count(m => m.Round == match.Round - 1);

                    int feeder1Index = prevRoundStart + (match.MatchNumber - 1) * 2;
                    int feeder2Index = feeder1Index + 1;

                    if (feeder1Index < winnerMatches.Count)
                        match.Feeder1MatchId = winnerMatches[feeder1Index].Id;
                    if (feeder2Index < winnerMatches.Count)
                        match.Feeder2MatchId = winnerMatches[feeder2Index].Id;
                }
            }
        }

        #endregion

        #region Private Methods - Match Progression

        private void AdvanceWinner(TournamentMatch completedMatch)
        {
            if (completedMatch.WinnerId == null) return;

            // Find the next match this feeds into
            var nextMatch = ActiveTournament.Matches.FirstOrDefault(m =>
                m.Feeder1MatchId == completedMatch.Id || m.Feeder2MatchId == completedMatch.Id);

            if (nextMatch != null)
            {
                if (nextMatch.Feeder1MatchId == completedMatch.Id)
                    nextMatch.Participant1Id = completedMatch.WinnerId;
                else
                    nextMatch.Participant2Id = completedMatch.WinnerId;

                UpdateMatchReadiness(nextMatch);
            }
        }

        private void UpdateMatchReadiness(TournamentMatch match)
        {
            if (match.State == MatchState.Completed || match.State == MatchState.Bye)
                return;

            if (!string.IsNullOrEmpty(match.Participant1Id) && !string.IsNullOrEmpty(match.Participant2Id))
            {
                match.State = MatchState.Ready;
            }
            else if (!string.IsNullOrEmpty(match.Participant1Id) || !string.IsNullOrEmpty(match.Participant2Id))
            {
                // Check if this should be a bye (other participant eliminated without a match)
                // For now, keep as pending
                match.State = MatchState.Pending;
            }
        }

        private bool IsTournamentComplete()
        {
            if (ActiveTournament.Format == TournamentFormat.RoundRobin)
            {
                return ActiveTournament.Matches.All(m => m.State == MatchState.Completed);
            }

            // For elimination, check if we have a single undefeated participant
            var stillAlive = ActiveTournament.Participants.Where(p => !p.IsEliminated).ToList();
            return stillAlive.Count <= 1;
        }

        private void CompleteTournament()
        {
            var winner = ActiveTournament.Participants.FirstOrDefault(p => !p.IsEliminated);
            if (winner != null)
                winner.FinalPlacement = 1;

            ActiveTournament.State = TournamentState.Completed;
            ActiveTournament.EndedAt = DateTime.UtcNow;
            ActiveTournament.WinnerId = winner?.Id;

            _tournamentHistory.Add(ActiveTournament);

            EOSDebugLogger.Log(DebugCategory.Social, "EOSTournamentManager",
                $"Tournament completed! Winner: {winner?.Name}");

            OnTournamentEnded?.Invoke(ActiveTournament, winner);
        }

        private int CalculatePlacement(TournamentParticipant eliminated)
        {
            // Count remaining participants + 1
            int remaining = ActiveTournament.Participants.Count(p => !p.IsEliminated);
            return remaining + 1;
        }

        #endregion

        #region Private Methods - Helpers

        private int GetNextPowerOfTwo(int n)
        {
            int power = 1;
            while (power < n) power *= 2;
            return power;
        }

        private List<int> GetSeededMatchOrder(int bracketSize)
        {
            // Generate standard seeded bracket order
            // 1 vs 16, 8 vs 9, 5 vs 12, 4 vs 13, etc.
            var order = new List<int> { 1 };

            while (order.Count < bracketSize)
            {
                var newOrder = new List<int>();
                int sum = order.Count * 2 + 1;
                foreach (int seed in order)
                {
                    newOrder.Add(seed);
                    newOrder.Add(sum - seed);
                }
                order = newOrder;
            }

            return order;
        }

        private int CalculateLosersRoundMatchCount(int losersRound, int winnersRounds)
        {
            // Simplified calculation - actual implementation would be more complex
            int baseCount = (int)Math.Pow(2, winnersRounds - 1);
            return Math.Max(1, baseCount / (int)Math.Pow(2, (losersRound + 1) / 2));
        }

        private string GetRoundName(int round, int totalRounds, BracketType bracket)
        {
            if (bracket == BracketType.GrandFinals)
                return "Grand Finals";
            if (bracket == BracketType.GrandFinalsReset)
                return "Grand Finals (Reset)";

            int remaining = totalRounds - round;
            return remaining switch
            {
                0 => bracket == BracketType.Winners ? "Winners Finals" : "Losers Finals",
                1 => bracket == BracketType.Winners ? "Winners Semi-Finals" : "Losers Semi-Finals",
                2 => bracket == BracketType.Winners ? "Winners Quarter-Finals" : "Losers Quarter-Finals",
                _ => $"Round {round}"
            };
        }

        #endregion
    }

    #region Data Types

    /// <summary>
    /// Tournament data.
    /// </summary>
    [Serializable]
    public class Tournament
    {
        public string Id;
        public string Name;
        public string Description;
        public TournamentFormat Format;
        public TournamentState State;
        public SeedingMethod SeedingMethod;
        public int MaxParticipants;
        public int BestOf = 1;
        public int GrandFinalsBestOf = 3;
        public bool AllowLateRegistration;
        public int CurrentRound;
        public string WinnerId;
        public DateTime CreatedAt;
        public DateTime? StartedAt;
        public DateTime? EndedAt;
        public List<TournamentParticipant> Participants = new();
        public List<TournamentMatch> Matches = new();
    }

    /// <summary>
    /// Tournament participant (player or team).
    /// </summary>
    [Serializable]
    public class TournamentParticipant
    {
        public string Id;
        public string Name;
        public int Seed;
        public int Rating;
        public int Wins;
        public int Losses;
        public bool IsEliminated;
        public int FinalPlacement;
        public bool IsTeam;
        public List<string> TeamMemberIds;
        public DateTime RegisteredAt;
    }

    /// <summary>
    /// Tournament match.
    /// </summary>
    [Serializable]
    public class TournamentMatch
    {
        public string Id;
        public int Round;
        public int MatchNumber;
        public BracketType Bracket;
        public MatchState State;
        public string Participant1Id;
        public string Participant2Id;
        public string WinnerId;
        public string LoserId;
        public int WinnerScore;
        public int LoserScore;
        public int BestOf;
        public string Feeder1MatchId;
        public string Feeder2MatchId;
        public DateTime? ScheduledAt;
        public DateTime? StartedAt;
        public DateTime? CompletedAt;
    }

    /// <summary>
    /// Options for creating a tournament.
    /// </summary>
    public class TournamentOptions
    {
        public string Name;
        public string Description;
        public TournamentFormat Format = TournamentFormat.SingleElimination;
        public SeedingMethod SeedingMethod = SeedingMethod.Random;
        public int MaxParticipants;
        public int BestOf = 1;
        public int GrandFinalsBestOf = 3;
        public bool AllowLateRegistration;

        public TournamentOptions WithName(string name) { Name = name; return this; }
        public TournamentOptions WithDescription(string desc) { Description = desc; return this; }
        public TournamentOptions WithFormat(TournamentFormat format) { Format = format; return this; }
        public TournamentOptions WithSeeding(SeedingMethod method) { SeedingMethod = method; return this; }
        public TournamentOptions WithMaxParticipants(int max) { MaxParticipants = max; return this; }
        public TournamentOptions WithBestOf(int bestOf) { BestOf = bestOf; return this; }
        public TournamentOptions WithGrandFinalsBestOf(int bestOf) { GrandFinalsBestOf = bestOf; return this; }
        public TournamentOptions AllowLatejoin(bool allow = true) { AllowLateRegistration = allow; return this; }
    }

    /// <summary>
    /// Bracket visualization data.
    /// </summary>
    public class BracketData
    {
        public string TournamentId;
        public string TournamentName;
        public BracketType BracketType;
        public int TotalRounds;
        public List<BracketRound> Rounds = new();
    }

    /// <summary>
    /// Single round in a bracket.
    /// </summary>
    public class BracketRound
    {
        public int RoundNumber;
        public string RoundName;
        public List<TournamentMatch> Matches = new();
    }

    /// <summary>
    /// Tournament formats.
    /// </summary>
    public enum TournamentFormat
    {
        SingleElimination,
        DoubleElimination,
        RoundRobin
    }

    /// <summary>
    /// Tournament states.
    /// </summary>
    public enum TournamentState
    {
        Registration,
        InProgress,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Match states.
    /// </summary>
    public enum MatchState
    {
        Pending,
        Ready,
        InProgress,
        Completed,
        Bye
    }

    /// <summary>
    /// Bracket types.
    /// </summary>
    public enum BracketType
    {
        Winners,
        Losers,
        GrandFinals,
        GrandFinalsReset,
        RoundRobin
    }

    /// <summary>
    /// Seeding methods.
    /// </summary>
    public enum SeedingMethod
    {
        Random,
        ByRating,
        Manual
    }

    #endregion
}
