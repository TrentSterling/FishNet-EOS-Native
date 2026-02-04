using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using FishNet.Managing;
using FishNet.Connection;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Security
{
    /// <summary>
    /// Host-authority security validation utilities.
    /// Provides checks to ensure only authorized operations are allowed.
    /// </summary>
    public static class SecurityValidator
    {
        #region Constants

        // Rate limit windows
        private const int RATE_LIMIT_WINDOW_SECONDS = 60;
        private const int MAX_MATCH_RESULTS_PER_MINUTE = 5;
        private const int MAX_ACHIEVEMENT_UNLOCKS_PER_MINUTE = 10;
        private const int MAX_REPUTATION_CHANGES_PER_MINUTE = 20;

        #endregion

        #region Rate Limiting

        private static readonly Dictionary<string, List<long>> _rateLimitTimestamps = new();

        /// <summary>
        /// Check if an action is rate limited.
        /// </summary>
        public static bool IsRateLimited(string actionKey, int maxPerMinute)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long windowStart = now - RATE_LIMIT_WINDOW_SECONDS;

            if (!_rateLimitTimestamps.TryGetValue(actionKey, out var timestamps))
            {
                timestamps = new List<long>();
                _rateLimitTimestamps[actionKey] = timestamps;
            }

            // Remove old timestamps
            timestamps.RemoveAll(t => t < windowStart);

            if (timestamps.Count >= maxPerMinute)
            {
                return true;
            }

            timestamps.Add(now);
            return false;
        }

        /// <summary>
        /// Check if match result recording is rate limited.
        /// </summary>
        public static bool IsMatchResultRateLimited(string puid)
        {
            return IsRateLimited($"match_result_{puid}", MAX_MATCH_RESULTS_PER_MINUTE);
        }

        /// <summary>
        /// Check if achievement unlock is rate limited.
        /// </summary>
        public static bool IsAchievementRateLimited(string puid)
        {
            return IsRateLimited($"achievement_{puid}", MAX_ACHIEVEMENT_UNLOCKS_PER_MINUTE);
        }

        /// <summary>
        /// Check if reputation change is rate limited.
        /// </summary>
        public static bool IsReputationRateLimited(string puid)
        {
            return IsRateLimited($"reputation_{puid}", MAX_REPUTATION_CHANGES_PER_MINUTE);
        }

        #endregion

        #region Host Authority

        /// <summary>
        /// Check if the local instance is the host/server.
        /// </summary>
        public static bool IsHost()
        {
            var nm = InstanceFinder.NetworkManager;
            if (nm == null) return false;
            return nm.IsServerStarted;
        }

        /// <summary>
        /// Check if a connection is valid and authenticated.
        /// </summary>
        public static bool IsValidConnection(NetworkConnection conn)
        {
            if (conn == null) return false;
            if (!conn.IsValid) return false;
            if (!conn.IsActive) return false;
            return true;
        }

        /// <summary>
        /// Check if a PUID is in the current lobby.
        /// </summary>
        public static bool IsPlayerInLobby(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return false;

            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return false;

            return registry.IsPlayerInLobby(puid);
        }

        /// <summary>
        /// Check if a PUID belongs to a connected player.
        /// </summary>
        public static bool IsConnectedPlayer(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return false;

            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return false;

            // Check if player is in connected clients
            var nm = InstanceFinder.NetworkManager;
            if (nm == null || !nm.IsServerStarted) return false;

            // For P2P, check lobby membership
            return IsPlayerInLobby(puid);
        }

        #endregion

        #region Validation Helpers

        /// <summary>
        /// Validate a match result request.
        /// Returns (isValid, errorReason).
        /// </summary>
        public static (bool isValid, string error) ValidateMatchResult(
            string requesterPuid,
            int claimedOpponentRating,
            string matchId = null)
        {
            // Must have a requester
            if (string.IsNullOrEmpty(requesterPuid))
                return (false, "Missing requester PUID");

            // Requester must be in lobby
            if (!IsPlayerInLobby(requesterPuid))
                return (false, "Requester not in lobby");

            // Rate limit check
            if (IsMatchResultRateLimited(requesterPuid))
                return (false, "Rate limited");

            // Opponent rating sanity check (0-5000 is reasonable)
            if (claimedOpponentRating < 0 || claimedOpponentRating > 5000)
                return (false, "Invalid opponent rating");

            return (true, null);
        }

        /// <summary>
        /// Validate an achievement unlock request.
        /// Returns (isValid, errorReason).
        /// </summary>
        public static (bool isValid, string error) ValidateAchievementUnlock(
            string requesterPuid,
            string achievementId,
            Dictionary<string, object> context = null)
        {
            if (string.IsNullOrEmpty(requesterPuid))
                return (false, "Missing requester PUID");

            if (string.IsNullOrEmpty(achievementId))
                return (false, "Missing achievement ID");

            if (!IsPlayerInLobby(requesterPuid))
                return (false, "Requester not in lobby");

            if (IsAchievementRateLimited(requesterPuid))
                return (false, "Rate limited");

            return (true, null);
        }

        /// <summary>
        /// Validate a reputation feedback request.
        /// Returns (isValid, errorReason).
        /// </summary>
        public static (bool isValid, string error) ValidateReputationFeedback(
            string fromPuid,
            string toPuid,
            bool isPositive)
        {
            if (string.IsNullOrEmpty(fromPuid))
                return (false, "Missing sender PUID");

            if (string.IsNullOrEmpty(toPuid))
                return (false, "Missing target PUID");

            if (fromPuid == toPuid)
                return (false, "Cannot give feedback to self");

            if (!IsPlayerInLobby(fromPuid))
                return (false, "Sender not in lobby");

            if (!IsPlayerInLobby(toPuid))
                return (false, "Target not in lobby");

            if (IsReputationRateLimited(fromPuid))
                return (false, "Rate limited");

            return (true, null);
        }

        #endregion

        #region Signature Verification

        /// <summary>
        /// Generate a simple HMAC signature for data integrity.
        /// Note: The secret should be derived from session-specific data.
        /// </summary>
        public static string GenerateSignature(string data, string sessionSecret)
        {
            if (string.IsNullOrEmpty(sessionSecret))
            {
                // Generate a session secret from lobby ID if not provided
                var transport = EOSNativeTransport.Instance;
                sessionSecret = transport?.CurrentLobbyId ?? "default_secret";
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sessionSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Verify a signature.
        /// </summary>
        public static bool VerifySignature(string data, string signature, string sessionSecret)
        {
            if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(signature))
                return false;

            string expected = GenerateSignature(data, sessionSecret);
            return signature == expected;
        }

        #endregion

        #region Match Tracking

        // Track active matches for validation
        private static readonly Dictionary<string, MatchTrackingData> _activeMatches = new();

        /// <summary>
        /// Register a match start (call on host).
        /// </summary>
        public static string RegisterMatchStart(List<string> participants, string gameMode)
        {
            string matchId = Guid.NewGuid().ToString("N").Substring(0, 16);

            _activeMatches[matchId] = new MatchTrackingData
            {
                MatchId = matchId,
                Participants = new HashSet<string>(participants),
                GameMode = gameMode,
                StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsComplete = false
            };

            return matchId;
        }

        /// <summary>
        /// Mark a match as complete (call on host).
        /// </summary>
        public static void RegisterMatchEnd(string matchId, string winnerId)
        {
            if (_activeMatches.TryGetValue(matchId, out var match))
            {
                match.IsComplete = true;
                match.WinnerId = winnerId;
                match.EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        /// <summary>
        /// Validate that a player was in a match.
        /// </summary>
        public static bool WasPlayerInMatch(string matchId, string puid)
        {
            if (!_activeMatches.TryGetValue(matchId, out var match))
                return false;

            return match.Participants.Contains(puid);
        }

        /// <summary>
        /// Get match data for validation.
        /// </summary>
        public static MatchTrackingData GetMatchData(string matchId)
        {
            return _activeMatches.TryGetValue(matchId, out var match) ? match : null;
        }

        /// <summary>
        /// Clean up old matches (call periodically).
        /// </summary>
        public static void CleanupOldMatches(int maxAgeMinutes = 60)
        {
            long cutoff = DateTimeOffset.UtcNow.AddMinutes(-maxAgeMinutes).ToUnixTimeMilliseconds();
            var toRemove = new List<string>();

            foreach (var kvp in _activeMatches)
            {
                if (kvp.Value.StartTime < cutoff)
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
                _activeMatches.Remove(key);
        }

        #endregion

        #region Data Types

        public class MatchTrackingData
        {
            public string MatchId;
            public HashSet<string> Participants;
            public string GameMode;
            public long StartTime;
            public long EndTime;
            public bool IsComplete;
            public string WinnerId;
        }

        #endregion
    }
}
