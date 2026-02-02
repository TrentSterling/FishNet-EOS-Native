using System;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Rating algorithm for calculating player skill changes.
    /// </summary>
    public enum RatingAlgorithm
    {
        /// <summary>Standard ELO with configurable K-factor.</summary>
        ELO,
        /// <summary>Glicko-2 with rating deviation and volatility.</summary>
        Glicko2,
        /// <summary>Simple fixed points per win/loss with streak bonuses.</summary>
        SimpleMMR
    }

    /// <summary>
    /// Queue mode for ranked matchmaking.
    /// </summary>
    public enum QueueMode
    {
        /// <summary>Search and join existing lobbies.</summary>
        SearchJoin,
        /// <summary>Enter a queue and wait for match.</summary>
        Queue,
        /// <summary>Support both modes.</summary>
        Both
    }

    /// <summary>
    /// How to display rank tiers.
    /// </summary>
    public enum TierDisplayMode
    {
        /// <summary>6 tiers: Bronze, Silver, Gold, Platinum, Diamond, Champion.</summary>
        SixTier,
        /// <summary>8 tiers: Iron, Bronze, Silver, Gold, Platinum, Diamond, Master, Grandmaster.</summary>
        EightTier,
        /// <summary>Show raw rating number only.</summary>
        NumbersOnly
    }

    /// <summary>
    /// Rank tier (skill bracket).
    /// </summary>
    public enum RankTier
    {
        Unranked = 0,
        // 6-tier + 8-tier combined (8-tier adds Iron, Master, Grandmaster)
        Iron = 1,
        Bronze = 2,
        Silver = 3,
        Gold = 4,
        Platinum = 5,
        Diamond = 6,
        Master = 7,
        Champion = 8,      // Used in 6-tier
        Grandmaster = 9    // Used in 8-tier
    }

    /// <summary>
    /// Division within a tier (I-IV, where I is highest).
    /// </summary>
    public enum RankDivision
    {
        IV = 4,
        III = 3,
        II = 2,
        I = 1
    }

    /// <summary>
    /// Cloud-persisted ranked player data.
    /// </summary>
    [Serializable]
    public struct RankedPlayerData
    {
        /// <summary>Current MMR/ELO rating.</summary>
        public int Rating;

        /// <summary>Highest rating achieved.</summary>
        public int PeakRating;

        /// <summary>Total ranked games played.</summary>
        public int GamesPlayed;

        /// <summary>Total wins.</summary>
        public int Wins;

        /// <summary>Total losses.</summary>
        public int Losses;

        /// <summary>Current win streak.</summary>
        public int WinStreak;

        /// <summary>Current loss streak.</summary>
        public int LossStreak;

        /// <summary>Unix timestamp of last match.</summary>
        public long LastMatchTime;

        // Glicko-2 specific fields
        /// <summary>Rating deviation (uncertainty). Starts high, decreases with games.</summary>
        public float RatingDeviation;

        /// <summary>Volatility (how erratic the player's performance is).</summary>
        public float Volatility;

        // Placement
        /// <summary>Whether placement matches are complete.</summary>
        public bool IsPlaced;

        /// <summary>Games played during placement.</summary>
        public int PlacementGamesPlayed;

        /// <summary>Wins during placement.</summary>
        public int PlacementWins;

        /// <summary>Data version for migration.</summary>
        public int Version;

        /// <summary>
        /// Win rate as a percentage (0-100).
        /// </summary>
        public float WinRate => GamesPlayed > 0 ? (Wins * 100f / GamesPlayed) : 0f;

        /// <summary>
        /// Days since last match.
        /// </summary>
        public int DaysSinceLastMatch
        {
            get
            {
                if (LastMatchTime <= 0) return int.MaxValue;
                var lastMatch = DateTimeOffset.FromUnixTimeMilliseconds(LastMatchTime);
                return (int)(DateTimeOffset.UtcNow - lastMatch).TotalDays;
            }
        }
    }

    /// <summary>
    /// Result of a rating change after a match.
    /// </summary>
    public struct RatingChange
    {
        /// <summary>Rating before the match.</summary>
        public int OldRating;

        /// <summary>Rating after the match.</summary>
        public int NewRating;

        /// <summary>Change amount (positive or negative).</summary>
        public int Change;

        /// <summary>Tier before the match.</summary>
        public RankTier OldTier;

        /// <summary>Tier after the match.</summary>
        public RankTier NewTier;

        /// <summary>Division before the match.</summary>
        public RankDivision OldDivision;

        /// <summary>Division after the match.</summary>
        public RankDivision NewDivision;

        /// <summary>Whether this was a promotion.</summary>
        public bool IsPromotion;

        /// <summary>Whether this was a demotion.</summary>
        public bool IsDemotion;

        /// <summary>Whether this was a tier change (not just division).</summary>
        public bool IsTierChange => OldTier != NewTier;

        public override string ToString()
        {
            string sign = Change >= 0 ? "+" : "";
            return $"{sign}{Change} ({OldRating} → {NewRating})";
        }
    }

    /// <summary>
    /// Queue entry for queue-based matchmaking.
    /// </summary>
    public struct QueueEntry
    {
        /// <summary>Player's ProductUserId.</summary>
        public string Puid;

        /// <summary>Player's current rating.</summary>
        public int Rating;

        /// <summary>When the player entered the queue.</summary>
        public long QueueTimeMs;

        /// <summary>Game mode being queued for.</summary>
        public string GameMode;

        /// <summary>Preferred region (optional).</summary>
        public string Region;

        /// <summary>
        /// Time spent in queue.
        /// </summary>
        public TimeSpan TimeInQueue => TimeSpan.FromMilliseconds(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - QueueTimeMs);
    }

    /// <summary>
    /// Serializable container for ranked data cloud storage.
    /// </summary>
    [Serializable]
    public struct RankedDataContainer
    {
        public int Version;
        public RankedPlayerData PlayerData;
        public long SavedAt;
    }
}
