using System;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Handles replay file version migration.
    /// Ensures old replays can still be played as the format evolves.
    /// </summary>
    public static class ReplayMigration
    {
        /// <summary>
        /// Current replay file format version.
        /// Increment when making breaking changes to the format.
        /// </summary>
        public const int CURRENT_VERSION = 1;

        /// <summary>
        /// Minimum supported version (older versions cannot be migrated).
        /// </summary>
        public const int MIN_SUPPORTED_VERSION = 1;

        /// <summary>
        /// Result of a migration attempt.
        /// </summary>
        public enum MigrationResult
        {
            /// <summary>No migration needed, file is current version.</summary>
            AlreadyCurrent,
            /// <summary>Migration successful.</summary>
            Success,
            /// <summary>Version too old to migrate.</summary>
            TooOld,
            /// <summary>Version newer than supported (from future app version).</summary>
            TooNew,
            /// <summary>Migration failed due to error.</summary>
            Failed
        }

        /// <summary>
        /// Check if a replay file can be loaded.
        /// </summary>
        public static bool CanLoad(ReplayFile replay)
        {
            return replay.Version >= MIN_SUPPORTED_VERSION && replay.Version <= CURRENT_VERSION;
        }

        /// <summary>
        /// Check if a replay file needs migration.
        /// </summary>
        public static bool NeedsMigration(ReplayFile replay)
        {
            return replay.Version < CURRENT_VERSION;
        }

        /// <summary>
        /// Migrate a replay file to the current version.
        /// Returns the migration result and the migrated file (or original if no migration needed).
        /// </summary>
        public static (MigrationResult result, ReplayFile replay) Migrate(ReplayFile replay)
        {
            // Check version bounds
            if (replay.Version > CURRENT_VERSION)
            {
                Debug.LogWarning($"[ReplayMigration] Replay version {replay.Version} is newer than supported {CURRENT_VERSION}");
                return (MigrationResult.TooNew, replay);
            }

            if (replay.Version < MIN_SUPPORTED_VERSION)
            {
                Debug.LogWarning($"[ReplayMigration] Replay version {replay.Version} is too old (min: {MIN_SUPPORTED_VERSION})");
                return (MigrationResult.TooOld, replay);
            }

            if (replay.Version == CURRENT_VERSION)
            {
                return (MigrationResult.AlreadyCurrent, replay);
            }

            try
            {
                var migrated = replay;

                // Apply migrations sequentially
                // Each migration upgrades from version N to N+1

                // Example migrations (add as needed):
                // if (migrated.Version == 1) migrated = MigrateV1ToV2(migrated);
                // if (migrated.Version == 2) migrated = MigrateV2ToV3(migrated);

                // For now, we only have version 1, so no migrations needed
                // This structure is ready for future migrations

                Debug.Log($"[ReplayMigration] Migrated replay from v{replay.Version} to v{CURRENT_VERSION}");
                return (MigrationResult.Success, migrated);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ReplayMigration] Migration failed: {e.Message}");
                return (MigrationResult.Failed, replay);
            }
        }

        /// <summary>
        /// Get a human-readable description of the migration result.
        /// </summary>
        public static string GetResultMessage(MigrationResult result, int fileVersion)
        {
            switch (result)
            {
                case MigrationResult.AlreadyCurrent:
                    return "Replay is up to date.";
                case MigrationResult.Success:
                    return $"Migrated from version {fileVersion} to {CURRENT_VERSION}.";
                case MigrationResult.TooOld:
                    return $"Replay version {fileVersion} is too old and cannot be played. Minimum supported: {MIN_SUPPORTED_VERSION}.";
                case MigrationResult.TooNew:
                    return $"Replay version {fileVersion} was created with a newer app version. Please update.";
                case MigrationResult.Failed:
                    return "Migration failed due to an error.";
                default:
                    return "Unknown migration result.";
            }
        }

        #region Version Migrations

        // Template for future migrations:
        //
        // private static ReplayFile MigrateV1ToV2(ReplayFile replay)
        // {
        //     // Perform migration from v1 to v2
        //     // Example: Add new field with default value
        //     // replay.Header.NewField = "default";
        //     replay.Version = 2;
        //     return replay;
        // }

        #endregion
    }
}
