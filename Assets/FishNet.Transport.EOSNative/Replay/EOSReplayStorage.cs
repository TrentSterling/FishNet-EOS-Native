using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Handles local and cloud storage for replay files.
    /// Local: Application.persistentDataPath/Replays/
    /// Cloud: EOSPlayerDataStorage (400MB limit)
    /// </summary>
    public class EOSReplayStorage : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayStorage _instance;
        public static EOSReplayStorage Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSReplayStorage>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSReplayStorage");
                        _instance = go.AddComponent<EOSReplayStorage>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when local replay list changes.</summary>
        public event Action<List<ReplayHeader>> OnLocalReplaysChanged;

        /// <summary>Fired when a replay is saved.</summary>
        public event Action<string, bool> OnReplaySaved; // replayId, success

        /// <summary>Fired when a replay is deleted.</summary>
        public event Action<string> OnReplayDeleted; // replayId

        #endregion

        #region Constants

        private const string REPLAY_FOLDER = "Replays";
        private const string REPLAY_EXTENSION = ".replay";
        private const string CLOUD_REPLAY_PREFIX = "replays/";
        private const int MAX_LOCAL_REPLAYS = 50;
        private const int MAX_CLOUD_REPLAYS = 10;

        #endregion

        #region Serialized Fields

        [Header("Storage Settings")]
        [Tooltip("Maximum number of local replays to keep.")]
        [SerializeField] private int _maxLocalReplays = 50;

        [Tooltip("Maximum number of cloud replays to keep.")]
        [SerializeField] private int _maxCloudReplays = 10;

        #endregion

        #region Public Properties

        /// <summary>Path to local replay folder.</summary>
        public string LocalReplayPath => Path.Combine(Application.persistentDataPath, REPLAY_FOLDER);

        /// <summary>Cached list of local replay headers.</summary>
        public IReadOnlyList<ReplayHeader> LocalReplays => _localReplays;

        /// <summary>Number of local replays.</summary>
        public int LocalReplayCount => _localReplays.Count;

        #endregion

        #region Private Fields

        private List<ReplayHeader> _localReplays = new();
        private Dictionary<string, string> _replayIdToPath = new();
        private HashSet<string> _favorites = new();

        private const string FAVORITES_PREFS_KEY = "EOSReplayFavorites";

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
            // Ensure directory exists
            if (!Directory.Exists(LocalReplayPath))
            {
                Directory.CreateDirectory(LocalReplayPath);
            }

            // Load favorites from PlayerPrefs
            LoadFavorites();

            // Load local replay list
            RefreshLocalReplays();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Local Storage

        /// <summary>
        /// Save a replay to local storage.
        /// </summary>
        public async Task<bool> SaveLocalAsync(ReplayFile replay)
        {
            try
            {
                string filename = $"replay_{replay.Header.MatchId}_{replay.Header.RecordedAt}{REPLAY_EXTENSION}";
                string path = Path.Combine(LocalReplayPath, filename);

                // Serialize replay
                string json = JsonUtility.ToJson(replay);
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json);

                // Write async
                await Task.Run(() => File.WriteAllBytes(path, data));

                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage",
                    $"Saved replay {replay.Header.ReplayId} ({data.Length / 1024f:F1} KB)");

                // Refresh list and enforce limit
                RefreshLocalReplays();
                EnforceLocalLimit();

                OnReplaySaved?.Invoke(replay.Header.ReplayId, true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to save replay: {e.Message}");
                OnReplaySaved?.Invoke(replay.Header.ReplayId, false);
                return false;
            }
        }

        /// <summary>
        /// Load a replay from local storage.
        /// </summary>
        public async Task<ReplayFile?> LoadLocalAsync(string replayId)
        {
            try
            {
                if (!_replayIdToPath.TryGetValue(replayId, out string path))
                {
                    // Try to find by scanning files
                    RefreshLocalReplays();
                    if (!_replayIdToPath.TryGetValue(replayId, out path))
                    {
                        EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", $"Replay not found: {replayId}");
                        return null;
                    }
                }

                if (!File.Exists(path))
                {
                    EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", $"Replay file missing: {path}");
                    return null;
                }

                byte[] data = await Task.Run(() => File.ReadAllBytes(path));
                string json = System.Text.Encoding.UTF8.GetString(data);
                var replay = JsonUtility.FromJson<ReplayFile>(json);

                // Check version compatibility and migrate if needed
                if (!ReplayMigration.CanLoad(replay))
                {
                    var message = ReplayMigration.GetResultMessage(
                        replay.Version > ReplayMigration.CURRENT_VERSION
                            ? ReplayMigration.MigrationResult.TooNew
                            : ReplayMigration.MigrationResult.TooOld,
                        replay.Version);
                    EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", message);
                    return null;
                }

                if (ReplayMigration.NeedsMigration(replay))
                {
                    var (result, migrated) = ReplayMigration.Migrate(replay);
                    if (result == ReplayMigration.MigrationResult.Success)
                    {
                        replay = migrated;
                        // Optionally save the migrated version back
                        EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage",
                            $"Migrated replay {replayId} from v{replay.Version} to v{ReplayMigration.CURRENT_VERSION}");
                    }
                    else if (result == ReplayMigration.MigrationResult.Failed)
                    {
                        EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage",
                            $"Failed to migrate replay {replayId}");
                        return null;
                    }
                }

                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage", $"Loaded replay {replayId} (v{replay.Version})");
                return replay;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to load replay: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get list of local replay headers.
        /// </summary>
        public List<ReplayHeader> GetLocalReplays()
        {
            RefreshLocalReplays();
            return new List<ReplayHeader>(_localReplays);
        }

        /// <summary>
        /// Delete a local replay.
        /// </summary>
        public bool DeleteReplay(string replayId)
        {
            try
            {
                if (!_replayIdToPath.TryGetValue(replayId, out string path))
                {
                    return false;
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage", $"Deleted replay {replayId}");
                }

                RefreshLocalReplays();
                OnReplayDeleted?.Invoke(replayId);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to delete replay: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clear all local replays.
        /// </summary>
        public void ClearLocalReplays()
        {
            try
            {
                foreach (var file in Directory.GetFiles(LocalReplayPath, $"*{REPLAY_EXTENSION}"))
                {
                    File.Delete(file);
                }

                _localReplays.Clear();
                _replayIdToPath.Clear();
                OnLocalReplaysChanged?.Invoke(_localReplays);

                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage", "Cleared all local replays");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to clear replays: {e.Message}");
            }
        }

        /// <summary>
        /// Refresh the list of local replays.
        /// </summary>
        public void RefreshLocalReplays()
        {
            _localReplays.Clear();
            _replayIdToPath.Clear();

            if (!Directory.Exists(LocalReplayPath)) return;

            try
            {
                var files = Directory.GetFiles(LocalReplayPath, $"*{REPLAY_EXTENSION}")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToArray();

                var favorites = new List<ReplayHeader>();
                var regular = new List<ReplayHeader>();

                foreach (var file in files)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var replay = JsonUtility.FromJson<ReplayFile>(json);

                        _replayIdToPath[replay.Header.ReplayId] = file;

                        // Sort into favorites vs regular
                        if (_favorites.Contains(replay.Header.ReplayId))
                        {
                            favorites.Add(replay.Header);
                        }
                        else
                        {
                            regular.Add(replay.Header);
                        }
                    }
                    catch
                    {
                        // Skip corrupted files
                    }
                }

                // Favorites first, then regular (both sorted by date)
                _localReplays.AddRange(favorites);
                _localReplays.AddRange(regular);

                OnLocalReplaysChanged?.Invoke(_localReplays);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to refresh replay list: {e.Message}");
            }
        }

        #endregion

        #region Public API - Cloud Storage

        /// <summary>
        /// Upload a replay to cloud storage.
        /// </summary>
        public async Task<Result> UploadToCloudAsync(ReplayFile replay)
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                return Result.NotConfigured;
            }

            try
            {
                string filename = $"{CLOUD_REPLAY_PREFIX}{replay.Header.ReplayId}{REPLAY_EXTENSION}";
                string json = JsonUtility.ToJson(replay);

                var result = await storage.WriteFileAsync(filename, json);

                if (result == Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage",
                        $"Uploaded replay {replay.Header.ReplayId} to cloud");

                    // Enforce cloud limit
                    await EnforceCloudLimitAsync();
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to upload replay: {e.Message}");
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Download a replay from cloud storage.
        /// </summary>
        public async Task<ReplayFile?> DownloadFromCloudAsync(string replayId)
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                return null;
            }

            try
            {
                string filename = $"{CLOUD_REPLAY_PREFIX}{replayId}{REPLAY_EXTENSION}";
                var (result, content) = await storage.ReadFileAsStringAsync(filename);

                if (result != Result.Success || string.IsNullOrEmpty(content))
                {
                    return null;
                }

                var replay = JsonUtility.FromJson<ReplayFile>(content);
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage", $"Downloaded replay {replayId} from cloud");

                return replay;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to download replay: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get list of cloud replay headers.
        /// </summary>
        public async Task<List<ReplayHeader>> GetCloudReplaysAsync()
        {
            var headers = new List<ReplayHeader>();
            var storage = EOSPlayerDataStorage.Instance;

            if (storage == null || !storage.IsReady)
            {
                return headers;
            }

            try
            {
                await storage.QueryFileListAsync();

                foreach (var file in storage.Files)
                {
                    if (file.Filename.StartsWith(CLOUD_REPLAY_PREFIX) &&
                        file.Filename.EndsWith(REPLAY_EXTENSION))
                    {
                        var (result, content) = await storage.ReadFileAsStringAsync(file.Filename);
                        if (result == Result.Success && !string.IsNullOrEmpty(content))
                        {
                            var replay = JsonUtility.FromJson<ReplayFile>(content);
                            headers.Add(replay.Header);
                        }
                    }
                }

                // Sort by date (newest first)
                headers.Sort((a, b) => b.RecordedAt.CompareTo(a.RecordedAt));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to get cloud replays: {e.Message}");
            }

            return headers;
        }

        /// <summary>
        /// Delete a cloud replay.
        /// </summary>
        public async Task<Result> DeleteCloudReplayAsync(string replayId)
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                return Result.NotConfigured;
            }

            string filename = $"{CLOUD_REPLAY_PREFIX}{replayId}{REPLAY_EXTENSION}";
            return await storage.DeleteFileAsync(filename);
        }

        #endregion

        #region Private Methods

        private void EnforceLocalLimit()
        {
            // Count non-favorite replays
            int nonFavoriteCount = 0;
            foreach (var r in _localReplays)
            {
                if (!_favorites.Contains(r.ReplayId))
                    nonFavoriteCount++;
            }

            // Only remove non-favorites when over limit
            while (nonFavoriteCount > _maxLocalReplays)
            {
                // Find oldest non-favorite (from end of list)
                for (int i = _localReplays.Count - 1; i >= 0; i--)
                {
                    if (!_favorites.Contains(_localReplays[i].ReplayId))
                    {
                        DeleteReplay(_localReplays[i].ReplayId);
                        nonFavoriteCount--;
                        break;
                    }
                }
            }
        }

        private async Task EnforceCloudLimitAsync()
        {
            var cloudReplays = await GetCloudReplaysAsync();

            while (cloudReplays.Count > _maxCloudReplays)
            {
                // Remove oldest
                var oldest = cloudReplays[cloudReplays.Count - 1];
                await DeleteCloudReplayAsync(oldest.ReplayId);
                cloudReplays.RemoveAt(cloudReplays.Count - 1);
            }
        }

        #endregion

        #region Public API - Export/Import

        /// <summary>
        /// Path to the export folder (user's Documents/Replays).
        /// </summary>
        public string ExportPath
        {
            get
            {
                string documentsPath;
#if UNITY_STANDALONE_WIN
                documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
#elif UNITY_STANDALONE_OSX
                documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
#else
                documentsPath = Application.persistentDataPath;
#endif
                return Path.Combine(documentsPath, "Replays");
            }
        }

        /// <summary>
        /// Export a replay to the user's Documents/Replays folder for sharing.
        /// </summary>
        /// <param name="replayId">The replay ID to export.</param>
        /// <returns>The exported file path, or null if failed.</returns>
        public async Task<string> ExportReplayAsync(string replayId)
        {
            try
            {
                var replay = await LoadLocalAsync(replayId);
                if (replay == null)
                {
                    EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", $"Replay not found for export: {replayId}");
                    return null;
                }

                // Ensure export directory exists
                if (!Directory.Exists(ExportPath))
                {
                    Directory.CreateDirectory(ExportPath);
                }

                // Create descriptive filename
                string safeName = SanitizeFilename($"{replay.Value.Header.GameMode}_{replay.Value.Header.MapName}");
                string timestamp = DateTimeOffset.FromUnixTimeMilliseconds(replay.Value.Header.RecordedAt)
                    .LocalDateTime.ToString("yyyy-MM-dd_HH-mm");
                string filename = $"{safeName}_{timestamp}{REPLAY_EXTENSION}";
                string exportFilePath = Path.Combine(ExportPath, filename);

                // Handle duplicate filenames
                int counter = 1;
                while (File.Exists(exportFilePath))
                {
                    filename = $"{safeName}_{timestamp}_{counter}{REPLAY_EXTENSION}";
                    exportFilePath = Path.Combine(ExportPath, filename);
                    counter++;
                }

                // Serialize and write
                string json = JsonUtility.ToJson(replay.Value);
                byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
                await Task.Run(() => File.WriteAllBytes(exportFilePath, data));

                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage",
                    $"Exported replay to: {exportFilePath} ({data.Length / 1024f:F1} KB)");

                return exportFilePath;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to export replay: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Import a replay from an external file.
        /// </summary>
        /// <param name="filePath">Path to the replay file to import.</param>
        /// <returns>True if import succeeded.</returns>
        public async Task<bool> ImportReplayAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", $"Import file not found: {filePath}");
                    return false;
                }

                byte[] data = await Task.Run(() => File.ReadAllBytes(filePath));
                string json = System.Text.Encoding.UTF8.GetString(data);
                var replay = JsonUtility.FromJson<ReplayFile>(json);

                // Validate the replay
                if (string.IsNullOrEmpty(replay.Header.ReplayId))
                {
                    EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", "Invalid replay file: missing ReplayId");
                    return false;
                }

                // Check version compatibility
                if (!ReplayMigration.CanLoad(replay))
                {
                    var message = ReplayMigration.GetResultMessage(
                        replay.Version > ReplayMigration.CURRENT_VERSION
                            ? ReplayMigration.MigrationResult.TooNew
                            : ReplayMigration.MigrationResult.TooOld,
                        replay.Version);
                    EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", message);
                    return false;
                }

                // Migrate if needed
                if (ReplayMigration.NeedsMigration(replay))
                {
                    var (result, migrated) = ReplayMigration.Migrate(replay);
                    if (result == ReplayMigration.MigrationResult.Success)
                    {
                        replay = migrated;
                    }
                    else if (result == ReplayMigration.MigrationResult.Failed)
                    {
                        EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayStorage", "Failed to migrate imported replay");
                        return false;
                    }
                }

                // Check if already exists
                if (_replayIdToPath.ContainsKey(replay.Header.ReplayId))
                {
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage",
                        $"Replay already exists locally: {replay.Header.ReplayId}");
                    return true; // Not an error, just already have it
                }

                // Save to local storage
                bool saved = await SaveLocalAsync(replay);

                if (saved)
                {
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage",
                        $"Imported replay: {replay.Header.ReplayId} ({replay.Header.GameMode} on {replay.Header.MapName})");
                }

                return saved;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSReplayStorage] Failed to import replay: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Open the export folder in the system file browser.
        /// </summary>
        public void OpenExportFolder()
        {
            if (!Directory.Exists(ExportPath))
            {
                Directory.CreateDirectory(ExportPath);
            }

#if UNITY_STANDALONE_WIN
            System.Diagnostics.Process.Start("explorer.exe", ExportPath.Replace("/", "\\"));
#elif UNITY_STANDALONE_OSX
            System.Diagnostics.Process.Start("open", ExportPath);
#elif UNITY_STANDALONE_LINUX
            System.Diagnostics.Process.Start("xdg-open", ExportPath);
#endif
        }

        private static string SanitizeFilename(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }
            return name.Length > 50 ? name.Substring(0, 50) : name;
        }

        #endregion

        #region Public API - Favorites

        /// <summary>
        /// Check if a replay is marked as favorite.
        /// </summary>
        public bool IsFavorite(string replayId)
        {
            return _favorites.Contains(replayId);
        }

        /// <summary>
        /// Toggle favorite status of a replay.
        /// </summary>
        public void ToggleFavorite(string replayId)
        {
            if (_favorites.Contains(replayId))
            {
                _favorites.Remove(replayId);
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage", $"Removed replay from favorites: {replayId}");
            }
            else
            {
                _favorites.Add(replayId);
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayStorage", $"Added replay to favorites: {replayId}");
            }
            SaveFavorites();
        }

        /// <summary>
        /// Add a replay to favorites.
        /// </summary>
        public void AddFavorite(string replayId)
        {
            if (_favorites.Add(replayId))
            {
                SaveFavorites();
            }
        }

        /// <summary>
        /// Remove a replay from favorites.
        /// </summary>
        public void RemoveFavorite(string replayId)
        {
            if (_favorites.Remove(replayId))
            {
                SaveFavorites();
            }
        }

        /// <summary>
        /// Get count of favorite replays.
        /// </summary>
        public int FavoriteCount => _favorites.Count;

        private void LoadFavorites()
        {
            _favorites.Clear();
            string saved = PlayerPrefs.GetString(FAVORITES_PREFS_KEY, "");
            if (!string.IsNullOrEmpty(saved))
            {
                foreach (var id in saved.Split(','))
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        _favorites.Add(id);
                    }
                }
            }
        }

        private void SaveFavorites()
        {
            string joined = string.Join(",", _favorites);
            PlayerPrefs.SetString(FAVORITES_PREFS_KEY, joined);
            PlayerPrefs.Save();
        }

        #endregion

        #region Utility

        /// <summary>
        /// Format file size as human-readable string.
        /// </summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }

        /// <summary>
        /// Format duration as mm:ss string.
        /// </summary>
        public static string FormatDuration(float seconds)
        {
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins}:{secs:D2}";
        }

        #endregion
    }
}
