using System.Threading.Tasks;
using Epic.OnlineServices;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Static helper methods for common gameplay stats operations.
    /// Provides convenient wrappers around EOSStats for common patterns.
    /// </summary>
    public static class EOSStatsHelper
    {
        /// <summary>
        /// Record stats at the end of a match.
        /// </summary>
        /// <param name="kills">Number of kills this match.</param>
        /// <param name="deaths">Number of deaths this match.</param>
        /// <param name="score">Total score this match.</param>
        /// <param name="playtime">Playtime in seconds (optional).</param>
        /// <returns>Result of the ingest operation.</returns>
        public static async Task<Result> RecordMatchEndAsync(int kills, int deaths, int score, int playtime = 0)
        {
            var stats = EOSStats.Instance;
            if (stats == null || !stats.IsReady)
            {
                Debug.LogWarning("[EOSStatsHelper] Stats not ready - cannot record match end");
                return Result.NotConfigured;
            }

            if (playtime > 0)
            {
                return await stats.IngestStatsAsync(
                    ("kills", kills),
                    ("deaths", deaths),
                    ("score", score),
                    ("playtime", playtime)
                );
            }
            else
            {
                return await stats.IngestStatsAsync(
                    ("kills", kills),
                    ("deaths", deaths),
                    ("score", score)
                );
            }
        }

        /// <summary>
        /// Increment a stat by a given amount.
        /// </summary>
        /// <param name="statName">Name of the stat to increment.</param>
        /// <param name="amount">Amount to add (default 1).</param>
        public static async Task<Result> IncrementStatAsync(string statName, int amount = 1)
        {
            var stats = EOSStats.Instance;
            if (stats == null || !stats.IsReady)
            {
                Debug.LogWarning($"[EOSStatsHelper] Stats not ready - cannot increment {statName}");
                return Result.NotConfigured;
            }

            return await stats.IngestStatAsync(statName, amount);
        }

        /// <summary>
        /// Record a win.
        /// </summary>
        public static Task<Result> RecordWinAsync() => IncrementStatAsync("wins");

        /// <summary>
        /// Record a loss.
        /// </summary>
        public static Task<Result> RecordLossAsync() => IncrementStatAsync("losses");

        /// <summary>
        /// Record a match played.
        /// </summary>
        public static Task<Result> RecordMatchPlayedAsync() => IncrementStatAsync("matches_played");

        /// <summary>
        /// Record XP earned.
        /// </summary>
        /// <param name="xp">Amount of XP to add.</param>
        public static Task<Result> RecordXPAsync(int xp) => IncrementStatAsync("xp", xp);

        /// <summary>
        /// Record currency earned.
        /// </summary>
        /// <param name="amount">Amount of currency to add.</param>
        public static Task<Result> RecordCurrencyAsync(int amount) => IncrementStatAsync("currency", amount);

        /// <summary>
        /// Get a cached stat value, or 0 if not cached.
        /// Call stats.QueryMyStatsAsync() first to populate cache.
        /// </summary>
        /// <param name="statName">Name of the stat.</param>
        /// <returns>The stat value, or 0 if not found.</returns>
        public static int GetCachedValue(string statName)
        {
            var stats = EOSStats.Instance;
            if (stats == null) return 0;
            return stats.GetCachedStatValue(statName, 0);
        }

        /// <summary>
        /// Calculate K/D ratio from cached stats.
        /// </summary>
        /// <returns>K/D ratio, or 0 if no kills/deaths tracked.</returns>
        public static float GetKDRatio()
        {
            int kills = GetCachedValue("kills");
            int deaths = GetCachedValue("deaths");
            if (deaths == 0) return kills; // Avoid division by zero
            return (float)kills / deaths;
        }

        /// <summary>
        /// Calculate win rate from cached stats.
        /// </summary>
        /// <returns>Win percentage (0-100), or 0 if no matches tracked.</returns>
        public static float GetWinRate()
        {
            int wins = GetCachedValue("wins");
            int matches = GetCachedValue("matches_played");
            if (matches == 0) return 0;
            return (float)wins / matches * 100f;
        }

        /// <summary>
        /// Query and refresh all local player stats.
        /// </summary>
        public static async Task RefreshMyStatsAsync()
        {
            var stats = EOSStats.Instance;
            if (stats == null || !stats.IsReady)
            {
                Debug.LogWarning("[EOSStatsHelper] Stats not ready - cannot refresh");
                return;
            }

            await stats.QueryMyStatsAsync();
        }
    }
}
