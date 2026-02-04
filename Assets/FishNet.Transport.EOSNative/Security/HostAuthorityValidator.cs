using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Social;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Security
{
    /// <summary>
    /// NetworkBehaviour for host-authoritative validation of game actions.
    /// Attach to a NetworkObject that exists in all scenes (e.g., NetworkManager).
    /// </summary>
    public class HostAuthorityValidator : NetworkBehaviour
    {
        #region Singleton

        private static HostAuthorityValidator _instance;
        public static HostAuthorityValidator Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<HostAuthorityValidator>();
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a validation request is rejected.</summary>
        public event Action<string, string, string> OnValidationRejected; // requesterPuid, actionType, reason

        /// <summary>Fired when a match result is validated by host.</summary>
        public event Action<string, MatchOutcome, int> OnMatchResultValidated; // puid, outcome, ratingChange

        /// <summary>Fired when an achievement unlock is validated by host.</summary>
        public event Action<string, string> OnAchievementValidated; // puid, achievementId

        /// <summary>Fired when reputation feedback is validated by host.</summary>
        public event Action<string, string, bool> OnReputationValidated; // fromPuid, toPuid, isPositive

        #endregion

        #region Settings

        [Header("Validation Settings")]
        [Tooltip("Enable host authority validation")]
        [SerializeField] private bool _enableValidation = true;

        [Tooltip("Log validation events")]
        [SerializeField] private bool _logValidation = true;

        [Tooltip("Allow offline mode to bypass validation")]
        [SerializeField] private bool _allowOfflineBypass = true;

        #endregion

        #region Private Fields

        // Track pending validations
        private readonly Dictionary<string, PendingValidation> _pendingValidations = new();

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
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Match Results

        /// <summary>
        /// Request match result validation from host.
        /// Call this instead of directly recording match results.
        /// </summary>
        public void RequestMatchResultValidation(
            MatchOutcome outcome,
            int opponentRating,
            string matchId = null)
        {
            if (!_enableValidation || ShouldBypassValidation())
            {
                // Direct execution in offline/bypass mode
                _ = EOSRankedMatchmaking.Instance?.RecordMatchResultAsync(outcome, opponentRating);
                return;
            }

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return;

            if (IsServerStarted)
            {
                // We are host - validate locally
                ValidateMatchResultInternal(localPuid, outcome, opponentRating, matchId);
            }
            else
            {
                // Send to host for validation
                ServerRpcRequestMatchValidation(localPuid, (int)outcome, opponentRating, matchId ?? "");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRpcRequestMatchValidation(
            string requesterPuid,
            int outcomeInt,
            int opponentRating,
            string matchId,
            NetworkConnection conn = null)
        {
            var outcome = (MatchOutcome)outcomeInt;
            ValidateMatchResultInternal(requesterPuid, outcome, opponentRating, matchId, conn);
        }

        private void ValidateMatchResultInternal(
            string requesterPuid,
            MatchOutcome outcome,
            int opponentRating,
            string matchId,
            NetworkConnection conn = null)
        {
            // Validate the request
            var (isValid, error) = SecurityValidator.ValidateMatchResult(requesterPuid, opponentRating, matchId);

            if (!isValid)
            {
                LogValidation($"Match result rejected for {requesterPuid}: {error}");
                OnValidationRejected?.Invoke(requesterPuid, "MatchResult", error);

                if (conn != null)
                {
                    TargetRpcMatchValidationResult(conn, false, 0, error);
                }
                return;
            }

            // If we have match tracking, verify participation
            if (!string.IsNullOrEmpty(matchId))
            {
                if (!SecurityValidator.WasPlayerInMatch(matchId, requesterPuid))
                {
                    LogValidation($"Match result rejected: {requesterPuid} was not in match {matchId}");
                    OnValidationRejected?.Invoke(requesterPuid, "MatchResult", "Not in match");

                    if (conn != null)
                    {
                        TargetRpcMatchValidationResult(conn, false, 0, "Not in match");
                    }
                    return;
                }
            }

            // Validation passed - notify client to proceed
            LogValidation($"Match result validated for {requesterPuid}: {outcome}");

            if (conn != null)
            {
                TargetRpcMatchValidationResult(conn, true, opponentRating, null);
            }
            else
            {
                // Local host - apply directly
                _ = ApplyMatchResultLocally(outcome, opponentRating);
            }

            OnMatchResultValidated?.Invoke(requesterPuid, outcome, 0);
        }

        [TargetRpc]
        private void TargetRpcMatchValidationResult(
            NetworkConnection conn,
            bool approved,
            int opponentRating,
            string error)
        {
            if (approved)
            {
                // Host approved - now we can record locally
                // Get the pending outcome from when we requested
                _ = ApplyMatchResultLocally(MatchOutcome.Win, opponentRating); // TODO: track pending outcome
            }
            else
            {
                Debug.LogWarning($"[Security] Match result validation rejected: {error}");
            }
        }

        private async System.Threading.Tasks.Task ApplyMatchResultLocally(MatchOutcome outcome, int opponentRating)
        {
            var ranked = EOSRankedMatchmaking.Instance;
            if (ranked != null)
            {
                await ranked.RecordMatchResultAsync(outcome, opponentRating);
            }
        }

        #endregion

        #region Public API - Achievements

        /// <summary>
        /// Request achievement unlock validation from host.
        /// </summary>
        public void RequestAchievementValidation(string achievementId, Dictionary<string, object> context = null)
        {
            if (!_enableValidation || ShouldBypassValidation())
            {
                _ = EOSAchievements.Instance?.UnlockAchievementAsync(achievementId);
                return;
            }

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return;

            if (IsServerStarted)
            {
                ValidateAchievementInternal(localPuid, achievementId);
            }
            else
            {
                ServerRpcRequestAchievementValidation(localPuid, achievementId);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRpcRequestAchievementValidation(
            string requesterPuid,
            string achievementId,
            NetworkConnection conn = null)
        {
            ValidateAchievementInternal(requesterPuid, achievementId, conn);
        }

        private void ValidateAchievementInternal(
            string requesterPuid,
            string achievementId,
            NetworkConnection conn = null)
        {
            var (isValid, error) = SecurityValidator.ValidateAchievementUnlock(requesterPuid, achievementId);

            if (!isValid)
            {
                LogValidation($"Achievement rejected for {requesterPuid}: {error}");
                OnValidationRejected?.Invoke(requesterPuid, "Achievement", error);

                if (conn != null)
                {
                    TargetRpcAchievementValidationResult(conn, false, achievementId, error);
                }
                return;
            }

            LogValidation($"Achievement validated for {requesterPuid}: {achievementId}");

            if (conn != null)
            {
                TargetRpcAchievementValidationResult(conn, true, achievementId, null);
            }
            else
            {
                _ = EOSAchievements.Instance?.UnlockAchievementAsync(achievementId);
            }

            OnAchievementValidated?.Invoke(requesterPuid, achievementId);
        }

        [TargetRpc]
        private void TargetRpcAchievementValidationResult(
            NetworkConnection conn,
            bool approved,
            string achievementId,
            string error)
        {
            if (approved)
            {
                _ = EOSAchievements.Instance?.UnlockAchievementAsync(achievementId);
            }
            else
            {
                Debug.LogWarning($"[Security] Achievement validation rejected: {error}");
            }
        }

        #endregion

        #region Public API - Reputation

        /// <summary>
        /// Request reputation feedback validation from host.
        /// </summary>
        public void RequestReputationValidation(string targetPuid, string category, bool isPositive, string comment = null)
        {
            if (!_enableValidation || ShouldBypassValidation())
            {
                // Direct execution
                var rep = EOSReputationManager.Instance;
                if (rep != null)
                {
                    if (isPositive)
                        _ = rep.CommendPlayerAsync(targetPuid, category, comment);
                    else
                        _ = rep.ReportPlayerAsync(targetPuid, category, comment);
                }
                return;
            }

            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (string.IsNullOrEmpty(localPuid)) return;

            if (IsServerStarted)
            {
                ValidateReputationInternal(localPuid, targetPuid, category, isPositive, comment);
            }
            else
            {
                ServerRpcRequestReputationValidation(localPuid, targetPuid, category, isPositive, comment ?? "");
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRpcRequestReputationValidation(
            string fromPuid,
            string toPuid,
            string category,
            bool isPositive,
            string comment,
            NetworkConnection conn = null)
        {
            ValidateReputationInternal(fromPuid, toPuid, category, isPositive, comment, conn);
        }

        private void ValidateReputationInternal(
            string fromPuid,
            string toPuid,
            string category,
            bool isPositive,
            string comment,
            NetworkConnection conn = null)
        {
            var (isValid, error) = SecurityValidator.ValidateReputationFeedback(fromPuid, toPuid, isPositive);

            if (!isValid)
            {
                LogValidation($"Reputation feedback rejected from {fromPuid} to {toPuid}: {error}");
                OnValidationRejected?.Invoke(fromPuid, "Reputation", error);

                if (conn != null)
                {
                    TargetRpcReputationValidationResult(conn, false, error);
                }
                return;
            }

            LogValidation($"Reputation feedback validated: {fromPuid} -> {toPuid} ({(isPositive ? "+" : "-")})");

            // Broadcast to target player
            BroadcastReputationFeedback(fromPuid, toPuid, category, isPositive, comment);

            if (conn != null)
            {
                TargetRpcReputationValidationResult(conn, true, null);
            }

            OnReputationValidated?.Invoke(fromPuid, toPuid, isPositive);
        }

        [ObserversRpc]
        private void BroadcastReputationFeedback(
            string fromPuid,
            string toPuid,
            string category,
            bool isPositive,
            string comment)
        {
            // Only apply if we are the target
            string localPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            if (localPuid != toPuid) return;

            var rep = EOSReputationManager.Instance;
            if (rep == null) return;

            var feedback = new ReputationFeedback
            {
                Id = Guid.NewGuid().ToString(),
                FromPuid = fromPuid,
                FromName = EOSPlayerRegistry.Instance?.GetDisplayName(fromPuid) ?? "Player",
                Category = category,
                Comment = comment,
                IsPositive = isPositive,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (isPositive)
                rep.ReceiveCommendation(feedback);
            else
                rep.ReceiveReport(feedback);
        }

        [TargetRpc]
        private void TargetRpcReputationValidationResult(
            NetworkConnection conn,
            bool approved,
            string error)
        {
            if (!approved)
            {
                Debug.LogWarning($"[Security] Reputation feedback rejected: {error}");
            }
        }

        #endregion

        #region Helpers

        private bool ShouldBypassValidation()
        {
            if (!_allowOfflineBypass) return false;

            var transport = EOSNativeTransport.Instance;
            return transport != null && transport.IsOfflineMode;
        }

        private void LogValidation(string message)
        {
            if (_logValidation)
            {
                EOSDebugLogger.Log(DebugCategory.Core, "HostAuthorityValidator", message);
            }
        }

        #endregion

        #region Data Types

        private class PendingValidation
        {
            public string RequestId;
            public string ActionType;
            public long RequestTime;
            public object Data;
        }

        #endregion
    }
}
