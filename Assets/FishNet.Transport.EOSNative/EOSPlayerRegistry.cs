using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Persistent registry of discovered players (PUID → DisplayName).
    /// Saves to PlayerPrefs and survives across sessions.
    /// </summary>
    public class EOSPlayerRegistry : MonoBehaviour
    {
        #region Singleton

        private static EOSPlayerRegistry _instance;
        public static EOSPlayerRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSPlayerRegistry>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSPlayerRegistry");
                        _instance = go.AddComponent<EOSPlayerRegistry>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string PREFS_KEY = "EOSPlayerRegistry_Cache";
        private const string PREFS_TIMESTAMPS_KEY = "EOSPlayerRegistry_Timestamps";
        private const string PREFS_FRIENDS_KEY = "EOSPlayerRegistry_Friends";
        private const string PREFS_BLOCKED_KEY = "EOSPlayerRegistry_Blocked";
        private const string PREFS_NOTES_KEY = "EOSPlayerRegistry_Notes";
        private const string PREFS_TIMEPLAYED_KEY = "EOSPlayerRegistry_TimePlayed";
        private const string PREFS_COLORS_KEY = "EOSPlayerRegistry_Colors";
        private const string PREFS_TAGS_KEY = "EOSPlayerRegistry_Tags";
        private const string CLOUD_FRIENDS_FILE = "local_friends.json";
        private const string CLOUD_BLOCKED_FILE = "blocked_players.json";
        private const int MAX_CACHED_PLAYERS = 500; // Limit to prevent bloat
        private const int CACHE_EXPIRY_DAYS = 30; // Remove entries older than this

        #endregion

        #region Events

        /// <summary>
        /// Fired when a new player is discovered (first time seeing this PUID).
        /// </summary>
        public event Action<string, string> OnPlayerDiscovered; // PUID, DisplayName

        /// <summary>
        /// Fired when a player's name is updated.
        /// </summary>
        public event Action<string, string, string> OnPlayerNameChanged; // PUID, OldName, NewName

        /// <summary>
        /// Fired when a friend is added or removed.
        /// </summary>
        public event Action<string, bool> OnFriendChanged; // PUID, isNowFriend

        /// <summary>
        /// Fired when a player is blocked or unblocked.
        /// </summary>
        public event Action<string, bool> OnBlockedChanged; // PUID, isNowBlocked

        #endregion

        #region Private Fields

        // PUID → DisplayName
        private Dictionary<string, string> _cache = new();

        // PUID → LastSeenTimestamp (Unix seconds)
        private Dictionary<string, long> _timestamps = new();

        // PUIDs marked as local friends (persists forever until removed)
        private HashSet<string> _friends = new();

        // PUIDs marked as blocked (persists forever until removed)
        private HashSet<string> _blocked = new();

        // Personal notes for players (PUID -> note text)
        private Dictionary<string, string> _notes = new();

        // Platform IDs for players (PUID -> platform code like "WIN", "AND", etc.)
        private Dictionary<string, string> _platforms = new();

        // Time played together in seconds (PUID -> total seconds)
        private Dictionary<string, float> _timePlayed = new();

        // Player colors (PUID -> color index 0-11)
        private Dictionary<string, int> _playerColors = new();

        // Player tags assigned by host (PUID -> tag string)
        private Dictionary<string, string> _playerTags = new();

        // Session start times for time tracking (PUID -> session start)
        private Dictionary<string, float> _sessionStartTimes = new();

        // Available player colors
        private static readonly Color[] PlayerColors = new Color[]
        {
            new Color(0.9f, 0.3f, 0.3f),  // Red
            new Color(0.3f, 0.6f, 0.9f),  // Blue
            new Color(0.3f, 0.9f, 0.4f),  // Green
            new Color(0.9f, 0.9f, 0.3f),  // Yellow
            new Color(0.9f, 0.5f, 0.2f),  // Orange
            new Color(0.7f, 0.3f, 0.9f),  // Purple
            new Color(0.3f, 0.9f, 0.9f),  // Cyan
            new Color(0.9f, 0.4f, 0.7f),  // Pink
            new Color(0.6f, 0.4f, 0.2f),  // Brown
            new Color(0.5f, 0.8f, 0.5f),  // Lime
            new Color(0.4f, 0.4f, 0.7f),  // Indigo
            new Color(0.8f, 0.8f, 0.8f),  // White/Gray
        };

        private int _nextColorIndex = 0;

        private bool _isDirty;
        private bool _friendsDirty;
        private bool _blockedDirty;
        private bool _notesDirty;
        private bool _timePlayedDirty;
        private bool _colorsDirty;
        private bool _tagsDirty;

        // Cloud sync state
        private bool _cloudSyncEnabled = true;
        private bool _cloudSyncInProgress;
        private DateTime _lastCloudSync = DateTime.MinValue;

        #endregion

        #region Public Properties

        /// <summary>
        /// Number of players in the persistent cache.
        /// </summary>
        public int CachedPlayerCount => _cache.Count;

        /// <summary>
        /// All cached players (read-only copy).
        /// </summary>
        public IReadOnlyDictionary<string, string> AllPlayers => _cache;

        /// <summary>
        /// Number of local friends.
        /// </summary>
        public int FriendCount => _friends.Count;

        /// <summary>
        /// Number of blocked players.
        /// </summary>
        public int BlockedCount => _blocked.Count;

        /// <summary>
        /// Whether cloud sync is in progress.
        /// </summary>
        public bool IsCloudSyncInProgress => _cloudSyncInProgress;

        /// <summary>
        /// Last time friends were synced to/from cloud.
        /// </summary>
        public DateTime LastCloudSync => _lastCloudSync;

        /// <summary>
        /// Enable/disable automatic cloud sync on friend changes.
        /// </summary>
        public bool CloudSyncEnabled
        {
            get => _cloudSyncEnabled;
            set => _cloudSyncEnabled = value;
        }

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

            LoadFromPrefs();
            LoadFriends();
            LoadBlocked();
            LoadNotes();
            LoadColors();
            LoadTags();
            LoadTimePlayed();
            CleanupExpiredEntries();
        }

        private void Start()
        {
            // Try to sync friends from cloud after storage is ready
            StartCoroutine(TryCloudSyncOnStart());
        }

        private System.Collections.IEnumerator TryCloudSyncOnStart()
        {
            // Wait for cloud storage to be ready
            float timeout = 10f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                var storage = EOSPlayerDataStorage.Instance;
                if (storage != null && storage.IsReady)
                {
                    // Cloud storage ready - load friends from cloud
                    _ = LoadFriendsFromCloudAsync();
                    yield break;
                }

                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cloud storage not ready, using local friends only");
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                if (_isDirty) SaveToPrefs();
                if (_friendsDirty) SaveFriends();
                if (_blockedDirty) SaveBlocked();
                if (_notesDirty) SaveNotes();
            }
        }

        private void OnApplicationQuit()
        {
            if (_isDirty) SaveToPrefs();
            if (_friendsDirty) SaveFriends();
            if (_blockedDirty) SaveBlocked();
            if (_notesDirty) SaveNotes();
        }

        private void OnDestroy()
        {
            if (_isDirty) SaveToPrefs();
            if (_friendsDirty) SaveFriends();
            if (_blockedDirty) SaveBlocked();
            if (_notesDirty) SaveNotes();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Register or update a player in the cache.
        /// </summary>
        /// <param name="puid">Player's ProductUserId string.</param>
        /// <param name="displayName">Player's display name.</param>
        /// <returns>True if this was a new player, false if updated existing.</returns>
        public bool RegisterPlayer(string puid, string displayName)
        {
            if (string.IsNullOrEmpty(puid) || string.IsNullOrEmpty(displayName))
                return false;

            // Truncate PUID for storage efficiency (first 32 chars is unique enough)
            string key = TruncatePuid(puid);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            bool isNew = !_cache.ContainsKey(key);
            string oldName = isNew ? null : _cache[key];

            _cache[key] = displayName;
            _timestamps[key] = now;
            _isDirty = true;

            if (isNew)
            {
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"New player: {displayName} ({key.Substring(0, 8)}...)");
                OnPlayerDiscovered?.Invoke(puid, displayName);

                // Trim if over limit
                if (_cache.Count > MAX_CACHED_PLAYERS)
                {
                    TrimOldestEntries(MAX_CACHED_PLAYERS / 10); // Remove 10%
                }
            }
            else if (oldName != displayName)
            {
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Name change: {oldName} → {displayName}");
                OnPlayerNameChanged?.Invoke(puid, oldName, displayName);
            }

            return isNew;
        }

        /// <summary>
        /// Get a player's display name from cache.
        /// </summary>
        /// <param name="puid">Player's ProductUserId string.</param>
        /// <returns>Display name if found, null otherwise.</returns>
        public string GetPlayerName(string puid)
        {
            if (string.IsNullOrEmpty(puid))
                return null;

            string key = TruncatePuid(puid);
            return _cache.TryGetValue(key, out string name) ? name : null;
        }

        /// <summary>
        /// Get a player's display name, or generate one if not cached.
        /// </summary>
        public string GetOrGenerateName(string puid)
        {
            string cached = GetPlayerName(puid);
            if (!string.IsNullOrEmpty(cached))
                return cached;

            // Generate and cache
            string generated = Lobbies.EOSLobbyChatManager.GenerateNameFromPuid(puid);
            RegisterPlayer(puid, generated);
            return generated;
        }

        /// <summary>
        /// Check if a player is in the cache.
        /// </summary>
        public bool HasPlayer(string puid)
        {
            if (string.IsNullOrEmpty(puid))
                return false;
            return _cache.ContainsKey(TruncatePuid(puid));
        }

        /// <summary>
        /// Get when a player was last seen.
        /// </summary>
        public DateTime? GetLastSeen(string puid)
        {
            if (string.IsNullOrEmpty(puid))
                return null;

            string key = TruncatePuid(puid);
            if (_timestamps.TryGetValue(key, out long ts))
            {
                return DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            }
            return null;
        }

        /// <summary>
        /// Get recently seen players (within last N days).
        /// </summary>
        public List<(string puid, string name, DateTime lastSeen)> GetRecentPlayers(int days = 7)
        {
            var result = new List<(string, string, DateTime)>();
            long cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();

            foreach (var kvp in _cache)
            {
                if (_timestamps.TryGetValue(kvp.Key, out long ts) && ts >= cutoff)
                {
                    var lastSeen = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
                    result.Add((kvp.Key, kvp.Value, lastSeen));
                }
            }

            // Sort by most recent first
            result.Sort((a, b) => b.Item3.CompareTo(a.Item3));
            return result;
        }

        /// <summary>
        /// Force save to PlayerPrefs.
        /// </summary>
        public void ForceSave()
        {
            SaveToPrefs();
            _isDirty = false;
        }

        /// <summary>
        /// Clear all cached players.
        /// </summary>
        public void ClearCache()
        {
            _cache.Clear();
            _timestamps.Clear();
            _isDirty = true;
            SaveToPrefs();
            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cache cleared");
        }

        #endregion

        #region Platform API

        /// <summary>
        /// Register a player's platform.
        /// </summary>
        public void RegisterPlatform(string puid, string platformId)
        {
            if (string.IsNullOrEmpty(puid) || string.IsNullOrEmpty(platformId)) return;

            string key = TruncatePuid(puid);
            _platforms[key] = platformId;
        }

        /// <summary>
        /// Get a player's platform ID (e.g., "WIN", "AND", "IOS").
        /// </summary>
        public string GetPlatform(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return null;
            string key = TruncatePuid(puid);
            return _platforms.TryGetValue(key, out string platform) ? platform : null;
        }

        /// <summary>
        /// Get platform icon/emoji for display.
        /// </summary>
        public static string GetPlatformIcon(string platformId)
        {
            return platformId switch
            {
                "WIN" => "\U0001F5A5", // Desktop/PC emoji
                "MAC" => "\U0001F34E", // Apple emoji
                "LNX" => "\U0001F427", // Penguin emoji
                "AND" => "\U0001F4F1", // Mobile phone emoji
                "IOS" => "\U0001F4F1", // Mobile phone emoji
                "OVR" => "\U0001F453", // Glasses emoji (VR)
                _ => "\U00002753"      // Question mark
            };
        }

        /// <summary>
        /// Get platform display name.
        /// </summary>
        public static string GetPlatformName(string platformId)
        {
            return platformId switch
            {
                "WIN" => "Windows",
                "MAC" => "macOS",
                "LNX" => "Linux",
                "AND" => "Android",
                "IOS" => "iOS",
                "OVR" => "Quest",
                _ => "Unknown"
            };
        }

        #endregion

        #region Player Colors API

        /// <summary>
        /// Get the color assigned to a player. Auto-assigns if not set.
        /// </summary>
        public Color GetPlayerColor(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return PlayerColors[0];

            string key = TruncatePuid(puid);
            if (!_playerColors.TryGetValue(key, out int colorIndex))
            {
                colorIndex = AssignNextColor(key);
            }
            return PlayerColors[colorIndex % PlayerColors.Length];
        }

        /// <summary>
        /// Get the color index for a player.
        /// </summary>
        public int GetPlayerColorIndex(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return 0;
            string key = TruncatePuid(puid);
            return _playerColors.TryGetValue(key, out int idx) ? idx : 0;
        }

        /// <summary>
        /// Set a specific color for a player.
        /// </summary>
        public void SetPlayerColor(string puid, int colorIndex)
        {
            if (string.IsNullOrEmpty(puid)) return;
            string key = TruncatePuid(puid);
            _playerColors[key] = colorIndex % PlayerColors.Length;
            _colorsDirty = true;
            SaveColors();
        }

        /// <summary>
        /// Get all available player colors.
        /// </summary>
        public static Color[] GetAvailableColors() => PlayerColors;

        private int AssignNextColor(string key)
        {
            int colorIndex = _nextColorIndex;
            _playerColors[key] = colorIndex;
            _nextColorIndex = (_nextColorIndex + 1) % PlayerColors.Length;
            _colorsDirty = true;
            SaveColors();
            return colorIndex;
        }

        private void LoadColors()
        {
            string json = PlayerPrefs.GetString(PREFS_COLORS_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                _playerColors = DeserializeDictInt(json);
                _nextColorIndex = _playerColors.Count % PlayerColors.Length;
            }
        }

        private void SaveColors()
        {
            if (!_colorsDirty) return;
            PlayerPrefs.SetString(PREFS_COLORS_KEY, SerializeDictInt(_playerColors));
            PlayerPrefs.Save();
            _colorsDirty = false;
        }

        #endregion

        #region Player Tags API

        /// <summary>
        /// Get the tag assigned to a player by the host.
        /// </summary>
        public string GetPlayerTag(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return null;
            string key = TruncatePuid(puid);
            return _playerTags.TryGetValue(key, out string tag) ? tag : null;
        }

        /// <summary>
        /// Set a tag for a player (host only typically).
        /// </summary>
        public void SetPlayerTag(string puid, string tag)
        {
            if (string.IsNullOrEmpty(puid)) return;
            string key = TruncatePuid(puid);

            if (string.IsNullOrEmpty(tag))
            {
                _playerTags.Remove(key);
            }
            else
            {
                _playerTags[key] = tag;
            }
            _tagsDirty = true;
            SaveTags();
        }

        /// <summary>
        /// Clear a player's tag.
        /// </summary>
        public void ClearPlayerTag(string puid)
        {
            SetPlayerTag(puid, null);
        }

        /// <summary>
        /// Get all players with tags.
        /// </summary>
        public IReadOnlyDictionary<string, string> GetAllTags() => _playerTags;

        private void LoadTags()
        {
            string json = PlayerPrefs.GetString(PREFS_TAGS_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                _playerTags = DeserializeDictString(json);
            }
        }

        private void SaveTags()
        {
            if (!_tagsDirty) return;
            PlayerPrefs.SetString(PREFS_TAGS_KEY, SerializeDictString(_playerTags));
            PlayerPrefs.Save();
            _tagsDirty = false;
        }

        #endregion

        #region Time Played Together API

        /// <summary>
        /// Start tracking play time with a player.
        /// </summary>
        public void StartPlaySession(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;
            string key = TruncatePuid(puid);
            _sessionStartTimes[key] = Time.realtimeSinceStartup;
        }

        /// <summary>
        /// End tracking play time with a player and accumulate time.
        /// </summary>
        public void EndPlaySession(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;
            string key = TruncatePuid(puid);

            if (_sessionStartTimes.TryGetValue(key, out float startTime))
            {
                float sessionDuration = Time.realtimeSinceStartup - startTime;
                if (!_timePlayed.ContainsKey(key))
                    _timePlayed[key] = 0f;

                _timePlayed[key] += sessionDuration;
                _sessionStartTimes.Remove(key);
                _timePlayedDirty = true;
                SaveTimePlayed();
            }
        }

        /// <summary>
        /// Get total time played with a player in seconds.
        /// </summary>
        public float GetTimePlayedWith(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return 0f;
            string key = TruncatePuid(puid);

            float total = _timePlayed.TryGetValue(key, out float saved) ? saved : 0f;

            // Add current session if active
            if (_sessionStartTimes.TryGetValue(key, out float startTime))
            {
                total += Time.realtimeSinceStartup - startTime;
            }

            return total;
        }

        /// <summary>
        /// Get formatted time played string (e.g., "2h 30m").
        /// </summary>
        public string GetTimePlayedFormatted(string puid)
        {
            float seconds = GetTimePlayedWith(puid);
            if (seconds < 60) return "< 1m";

            int hours = (int)(seconds / 3600);
            int minutes = (int)((seconds % 3600) / 60);

            if (hours > 0)
                return $"{hours}h {minutes}m";
            return $"{minutes}m";
        }

        /// <summary>
        /// Get players sorted by time played together.
        /// </summary>
        public List<(string puid, string name, float seconds)> GetPlayersByTimePlayed()
        {
            var result = new List<(string, string, float)>();
            foreach (var kvp in _timePlayed)
            {
                string name = GetDisplayName(kvp.Key);
                result.Add((kvp.Key, name, kvp.Value));
            }
            result.Sort((a, b) => b.seconds.CompareTo(a.seconds));
            return result;
        }

        private void LoadTimePlayed()
        {
            string json = PlayerPrefs.GetString(PREFS_TIMEPLAYED_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                _timePlayed = DeserializeDictFloat(json);
            }
        }

        private void SaveTimePlayed()
        {
            if (!_timePlayedDirty) return;
            PlayerPrefs.SetString(PREFS_TIMEPLAYED_KEY, SerializeDictFloat(_timePlayed));
            PlayerPrefs.Save();
            _timePlayedDirty = false;
        }

        #endregion

        #region Local Friends API

        /// <summary>
        /// Check if a player is a local friend.
        /// </summary>
        public bool IsFriend(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return false;
            return _friends.Contains(TruncatePuid(puid));
        }

        /// <summary>
        /// Add a player as a local friend.
        /// </summary>
        public void AddFriend(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;

            string key = TruncatePuid(puid);
            if (_friends.Add(key))
            {
                _friendsDirty = true;
                SaveFriends();
                string name = GetPlayerName(puid) ?? "Unknown";
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Added friend: {name}");
                OnFriendChanged?.Invoke(puid, true);
                AutoSyncToCloud();
            }
        }

        /// <summary>
        /// Remove a player from local friends.
        /// </summary>
        public void RemoveFriend(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;

            string key = TruncatePuid(puid);
            if (_friends.Remove(key))
            {
                _friendsDirty = true;
                SaveFriends();
                string name = GetPlayerName(puid) ?? "Unknown";
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Removed friend: {name}");
                OnFriendChanged?.Invoke(puid, false);
                AutoSyncToCloud();
            }
        }

        /// <summary>
        /// Toggle friend status for a player.
        /// </summary>
        public void ToggleFriend(string puid)
        {
            if (IsFriend(puid))
                RemoveFriend(puid);
            else
                AddFriend(puid);
        }

        /// <summary>
        /// Get all local friends with their cached display names.
        /// </summary>
        public List<(string puid, string name)> GetFriends()
        {
            var result = new List<(string puid, string name)>();

            foreach (var key in _friends)
            {
                string friendName = _cache.TryGetValue(key, out string n) ? n : "Unknown";
                result.Add((key, friendName));
            }

            // Sort alphabetically by name
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Clear all local friends.
        /// </summary>
        public void ClearFriends()
        {
            _friends.Clear();
            _friendsDirty = true;
            SaveFriends();
            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Friends cleared");
            AutoSyncToCloud();
        }

        #endregion

        #region Block List API

        /// <summary>
        /// Check if a player is blocked.
        /// </summary>
        public bool IsBlocked(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return false;
            return _blocked.Contains(TruncatePuid(puid));
        }

        /// <summary>
        /// Block a player.
        /// </summary>
        public void BlockPlayer(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;

            string key = TruncatePuid(puid);
            if (_blocked.Add(key))
            {
                // Also remove from friends if they were a friend
                if (_friends.Remove(key))
                {
                    _friendsDirty = true;
                    SaveFriends();
                    OnFriendChanged?.Invoke(puid, false);
                }

                _blockedDirty = true;
                SaveBlocked();
                string name = GetPlayerName(puid) ?? "Unknown";
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Blocked player: {name}");
                OnBlockedChanged?.Invoke(puid, true);
                AutoSyncBlockedToCloud();
            }
        }

        /// <summary>
        /// Unblock a player.
        /// </summary>
        public void UnblockPlayer(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return;

            string key = TruncatePuid(puid);
            if (_blocked.Remove(key))
            {
                _blockedDirty = true;
                SaveBlocked();
                string name = GetPlayerName(puid) ?? "Unknown";
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Unblocked player: {name}");
                OnBlockedChanged?.Invoke(puid, false);
                AutoSyncBlockedToCloud();
            }
        }

        /// <summary>
        /// Get all blocked players with their cached display names.
        /// </summary>
        public List<(string puid, string name)> GetBlockedPlayers()
        {
            var result = new List<(string puid, string name)>();

            foreach (var key in _blocked)
            {
                string name = _cache.TryGetValue(key, out string n) ? n : "Unknown";
                result.Add((key, name));
            }

            // Sort alphabetically by name
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Clear all blocked players.
        /// </summary>
        public void ClearBlocked()
        {
            _blocked.Clear();
            _blockedDirty = true;
            SaveBlocked();
            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Block list cleared");
            AutoSyncBlockedToCloud();
        }

        #endregion

        #region Friend Notes API

        /// <summary>
        /// Get the personal note for a player.
        /// </summary>
        public string GetNote(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return null;
            string key = TruncatePuid(puid);
            return _notes.TryGetValue(key, out string note) ? note : null;
        }

        /// <summary>
        /// Set a personal note for a player.
        /// </summary>
        public void SetNote(string puid, string note)
        {
            if (string.IsNullOrEmpty(puid)) return;

            string key = TruncatePuid(puid);

            if (string.IsNullOrEmpty(note))
            {
                // Clear the note
                if (_notes.Remove(key))
                {
                    _notesDirty = true;
                    SaveNotes();
                }
            }
            else
            {
                // Set or update the note
                _notes[key] = note.Length > 200 ? note.Substring(0, 200) : note; // Limit to 200 chars
                _notesDirty = true;
                SaveNotes();
            }
        }

        /// <summary>
        /// Check if a player has a note.
        /// </summary>
        public bool HasNote(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return false;
            return _notes.ContainsKey(TruncatePuid(puid));
        }

        /// <summary>
        /// Clear all notes.
        /// </summary>
        public void ClearAllNotes()
        {
            _notes.Clear();
            _notesDirty = true;
            SaveNotes();
            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "All notes cleared");
        }

        #endregion

        #region Cloud Sync

        /// <summary>
        /// Sync friends list to EOS cloud storage.
        /// Call this to backup friends across devices.
        /// </summary>
        public async Task<Result> SyncFriendsToCloudAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                EOSDebugLogger.LogWarning(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cloud storage not ready");
                return Result.NotConfigured;
            }

            if (_cloudSyncInProgress)
            {
                EOSDebugLogger.LogWarning(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cloud sync already in progress");
                return Result.RequestInProgress;
            }

            _cloudSyncInProgress = true;

            try
            {
                // Build cloud data - include names for display on other devices
                var cloudData = new CloudFriendsData
                {
                    version = 1,
                    friends = new CloudFriendEntry[_friends.Count]
                };

                int i = 0;
                foreach (var puid in _friends)
                {
                    string name = _cache.TryGetValue(puid, out string n) ? n : "Unknown";
                    cloudData.friends[i++] = new CloudFriendEntry { puid = puid, name = name };
                }

                var result = await storage.WriteFileAsJsonAsync(CLOUD_FRIENDS_FILE, cloudData);

                if (result == Result.Success)
                {
                    _lastCloudSync = DateTime.Now;
                    EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Synced {_friends.Count} friends to cloud");
                }
                else
                {
                    EOSDebugLogger.LogWarning(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Cloud sync failed: {result}");
                }

                return result;
            }
            finally
            {
                _cloudSyncInProgress = false;
            }
        }

        /// <summary>
        /// Load friends list from EOS cloud storage.
        /// Merges with local friends (union of both).
        /// </summary>
        public async Task<Result> LoadFriendsFromCloudAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                EOSDebugLogger.LogWarning(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cloud storage not ready");
                return Result.NotConfigured;
            }

            if (_cloudSyncInProgress)
            {
                EOSDebugLogger.LogWarning(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cloud sync already in progress");
                return Result.RequestInProgress;
            }

            _cloudSyncInProgress = true;

            try
            {
                var (result, cloudData) = await storage.ReadFileAsJsonAsync<CloudFriendsData>(CLOUD_FRIENDS_FILE);

                if (result == Result.NotFound)
                {
                    EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "No cloud friends file found");
                    return Result.Success; // Not an error, just no data yet
                }

                if (result != Result.Success)
                {
                    EOSDebugLogger.LogWarning(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Cloud load failed: {result}");
                    return result;
                }

                if (cloudData.friends == null)
                {
                    return Result.Success;
                }

                // Merge cloud friends with local (union)
                int added = 0;
                foreach (var entry in cloudData.friends)
                {
                    if (!string.IsNullOrEmpty(entry.puid) && _friends.Add(entry.puid))
                    {
                        added++;
                        // Also cache the name if we don't have it
                        if (!_cache.ContainsKey(entry.puid) && !string.IsNullOrEmpty(entry.name))
                        {
                            _cache[entry.puid] = entry.name;
                            _timestamps[entry.puid] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            _isDirty = true;
                        }
                    }
                }

                if (added > 0)
                {
                    _friendsDirty = true;
                    SaveFriends();
                    if (_isDirty) SaveToPrefs();
                    EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Loaded {added} new friends from cloud (total: {_friends.Count})");
                }
                else
                {
                    EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", "Cloud friends already in sync");
                }

                _lastCloudSync = DateTime.Now;
                return Result.Success;
            }
            finally
            {
                _cloudSyncInProgress = false;
            }
        }

        /// <summary>
        /// Full two-way sync: load from cloud, merge, then upload.
        /// </summary>
        public async Task<Result> FullCloudSyncAsync()
        {
            var loadResult = await LoadFriendsFromCloudAsync();
            if (loadResult != Result.Success && loadResult != Result.NotFound)
                return loadResult;

            var friendsResult = await SyncFriendsToCloudAsync();

            // Also sync blocked list
            await LoadBlockedFromCloudAsync();
            await SyncBlockedToCloudAsync();

            return friendsResult;
        }

        /// <summary>
        /// Sync blocked list to EOS cloud storage.
        /// </summary>
        public async Task<Result> SyncBlockedToCloudAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
                return Result.NotConfigured;

            if (_cloudSyncInProgress)
                return Result.RequestInProgress;

            _cloudSyncInProgress = true;

            try
            {
                var cloudData = new CloudFriendsData
                {
                    version = 1,
                    friends = new CloudFriendEntry[_blocked.Count]
                };

                int i = 0;
                foreach (var puid in _blocked)
                {
                    string name = _cache.TryGetValue(puid, out string n) ? n : "Unknown";
                    cloudData.friends[i++] = new CloudFriendEntry { puid = puid, name = name };
                }

                var result = await storage.WriteFileAsJsonAsync(CLOUD_BLOCKED_FILE, cloudData);

                if (result == Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Synced {_blocked.Count} blocked players to cloud");
                }

                return result;
            }
            finally
            {
                _cloudSyncInProgress = false;
            }
        }

        /// <summary>
        /// Load blocked list from EOS cloud storage.
        /// </summary>
        public async Task<Result> LoadBlockedFromCloudAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
                return Result.NotConfigured;

            if (_cloudSyncInProgress)
                return Result.RequestInProgress;

            _cloudSyncInProgress = true;

            try
            {
                var (result, cloudData) = await storage.ReadFileAsJsonAsync<CloudFriendsData>(CLOUD_BLOCKED_FILE);

                if (result == Result.NotFound)
                    return Result.Success;

                if (result != Result.Success)
                    return result;

                if (cloudData.friends == null)
                    return Result.Success;

                int added = 0;
                foreach (var entry in cloudData.friends)
                {
                    if (!string.IsNullOrEmpty(entry.puid) && _blocked.Add(entry.puid))
                    {
                        added++;
                        if (!_cache.ContainsKey(entry.puid) && !string.IsNullOrEmpty(entry.name))
                        {
                            _cache[entry.puid] = entry.name;
                        }
                    }
                }

                if (added > 0)
                {
                    _blockedDirty = true;
                    SaveBlocked();
                    EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Loaded {added} blocked players from cloud");
                }

                return Result.Success;
            }
            finally
            {
                _cloudSyncInProgress = false;
            }
        }

        // Auto-sync helper called after friend changes
        private async void AutoSyncToCloud()
        {
            if (!_cloudSyncEnabled) return;

            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return;

            // Debounce: don't sync more than once per 5 seconds
            if ((DateTime.Now - _lastCloudSync).TotalSeconds < 5) return;

            await SyncFriendsToCloudAsync();
        }

        #endregion

        #region Friend Status

        // Cache for friend status to avoid spamming lobby searches
        private Dictionary<string, (FriendStatus status, string lobbyCode, DateTime checkedAt)> _statusCache = new();
        private const float STATUS_CACHE_SECONDS = 30f; // How long to cache status

        /// <summary>
        /// Get a friend's online status.
        /// </summary>
        /// <param name="puid">The friend's PUID.</param>
        /// <returns>Current status (may be cached).</returns>
        public FriendStatus GetFriendStatus(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return FriendStatus.Unknown;

            string key = TruncatePuid(puid);

            // Check if in current lobby (fast, always accurate)
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                if (IsMemberInCurrentLobby(key))
                {
                    return FriendStatus.InLobby;
                }
            }

            // Check cache
            if (_statusCache.TryGetValue(key, out var cached))
            {
                if ((DateTime.Now - cached.checkedAt).TotalSeconds < STATUS_CACHE_SECONDS)
                {
                    return cached.status;
                }
            }

            return FriendStatus.Unknown;
        }

        /// <summary>
        /// Get a friend's status with lobby code if they're in a game.
        /// </summary>
        public (FriendStatus status, string lobbyCode) GetFriendStatusWithLobby(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return (FriendStatus.Unknown, null);

            string key = TruncatePuid(puid);

            // Check if in current lobby
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                if (IsMemberInCurrentLobby(key))
                {
                    return (FriendStatus.InLobby, lobbyManager.CurrentLobby.JoinCode);
                }
            }

            // Check cache
            if (_statusCache.TryGetValue(key, out var cached))
            {
                if ((DateTime.Now - cached.checkedAt).TotalSeconds < STATUS_CACHE_SECONDS)
                {
                    return (cached.status, cached.lobbyCode);
                }
            }

            return (FriendStatus.Unknown, null);
        }

        /// <summary>
        /// Query a friend's status by searching for their lobbies.
        /// Results are cached for STATUS_CACHE_SECONDS.
        /// </summary>
        public async Task<(FriendStatus status, string lobbyCode)> QueryFriendStatusAsync(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return (FriendStatus.Unknown, null);

            string key = TruncatePuid(puid);

            // Check if in current lobby first
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                if (IsMemberInCurrentLobby(key))
                {
                    _statusCache[key] = (FriendStatus.InLobby, lobbyManager.CurrentLobby.JoinCode, DateTime.Now);
                    return (FriendStatus.InLobby, lobbyManager.CurrentLobby.JoinCode);
                }
            }

            // Search for lobbies containing this user
            if (lobbyManager != null)
            {
                var (result, lobbies) = await lobbyManager.SearchByMemberAsync(puid, 1);
                if (result == Result.Success && lobbies != null && lobbies.Count > 0)
                {
                    var lobby = lobbies[0];
                    _statusCache[key] = (FriendStatus.InGame, lobby.JoinCode, DateTime.Now);
                    return (FriendStatus.InGame, lobby.JoinCode);
                }
            }

            // Not found in any lobby
            _statusCache[key] = (FriendStatus.Offline, null, DateTime.Now);
            return (FriendStatus.Offline, null);
        }

        /// <summary>
        /// Query status for all friends (batched).
        /// </summary>
        public async Task RefreshAllFriendStatusesAsync()
        {
            var friends = GetFriends();
            foreach (var (puid, name) in friends)
            {
                await QueryFriendStatusAsync(puid);
                // Small delay to avoid rate limiting
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// Clear the status cache (forces fresh queries).
        /// </summary>
        public void ClearStatusCache()
        {
            _statusCache.Clear();
        }

        private bool IsMemberInCurrentLobby(string puid)
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null || !lobbyManager.IsInLobby) return false;

            // Check if the puid matches the owner
            if (lobbyManager.CurrentLobby.OwnerPuid?.StartsWith(puid) == true)
                return true;

            // For full member list, we'd need to iterate lobby members
            // For now, we'll rely on the owner check and lobby search
            return false;
        }

        #endregion

        #region Private Methods

        private string TruncatePuid(string puid)
        {
            // EOS PUIDs are long - truncate to 32 chars for storage
            return puid.Length > 32 ? puid.Substring(0, 32) : puid;
        }

        private void LoadFromPrefs()
        {
            _cache.Clear();
            _timestamps.Clear();

            string cacheJson = PlayerPrefs.GetString(PREFS_KEY, "");
            string timestampsJson = PlayerPrefs.GetString(PREFS_TIMESTAMPS_KEY, "");

            if (!string.IsNullOrEmpty(cacheJson))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<SerializableDict>(cacheJson);
                    if (wrapper?.keys != null && wrapper?.values != null)
                    {
                        for (int i = 0; i < Mathf.Min(wrapper.keys.Length, wrapper.values.Length); i++)
                        {
                            _cache[wrapper.keys[i]] = wrapper.values[i];
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EOSPlayerRegistry] Failed to load cache: {e.Message}");
                }
            }

            if (!string.IsNullOrEmpty(timestampsJson))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<SerializableLongDict>(timestampsJson);
                    if (wrapper?.keys != null && wrapper?.values != null)
                    {
                        for (int i = 0; i < Mathf.Min(wrapper.keys.Length, wrapper.values.Length); i++)
                        {
                            _timestamps[wrapper.keys[i]] = wrapper.values[i];
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EOSPlayerRegistry] Failed to load timestamps: {e.Message}");
                }
            }

            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Loaded {_cache.Count} players from cache");
        }

        private void SaveToPrefs()
        {
            try
            {
                var cacheWrapper = new SerializableDict
                {
                    keys = new string[_cache.Count],
                    values = new string[_cache.Count]
                };

                int i = 0;
                foreach (var kvp in _cache)
                {
                    cacheWrapper.keys[i] = kvp.Key;
                    cacheWrapper.values[i] = kvp.Value;
                    i++;
                }

                var timestampWrapper = new SerializableLongDict
                {
                    keys = new string[_timestamps.Count],
                    values = new long[_timestamps.Count]
                };

                i = 0;
                foreach (var kvp in _timestamps)
                {
                    timestampWrapper.keys[i] = kvp.Key;
                    timestampWrapper.values[i] = kvp.Value;
                    i++;
                }

                PlayerPrefs.SetString(PREFS_KEY, JsonUtility.ToJson(cacheWrapper));
                PlayerPrefs.SetString(PREFS_TIMESTAMPS_KEY, JsonUtility.ToJson(timestampWrapper));
                PlayerPrefs.Save();

                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Saved {_cache.Count} players to cache");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EOSPlayerRegistry] Failed to save: {e.Message}");
            }
        }

        private void LoadFriends()
        {
            _friends.Clear();

            string json = PlayerPrefs.GetString(PREFS_FRIENDS_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<SerializableStringArray>(json);
                    if (wrapper?.values != null)
                    {
                        foreach (var puid in wrapper.values)
                        {
                            _friends.Add(puid);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EOSPlayerRegistry] Failed to load friends: {e.Message}");
                }
            }

            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Loaded {_friends.Count} local friends");
        }

        private void SaveFriends()
        {
            try
            {
                var wrapper = new SerializableStringArray
                {
                    values = new string[_friends.Count]
                };

                int i = 0;
                foreach (var puid in _friends)
                {
                    wrapper.values[i++] = puid;
                }

                PlayerPrefs.SetString(PREFS_FRIENDS_KEY, JsonUtility.ToJson(wrapper));
                PlayerPrefs.Save();
                _friendsDirty = false;

                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Saved {_friends.Count} local friends");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EOSPlayerRegistry] Failed to save friends: {e.Message}");
            }
        }

        private void LoadBlocked()
        {
            _blocked.Clear();

            string json = PlayerPrefs.GetString(PREFS_BLOCKED_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<SerializableStringArray>(json);
                    if (wrapper?.values != null)
                    {
                        foreach (var puid in wrapper.values)
                        {
                            _blocked.Add(puid);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EOSPlayerRegistry] Failed to load blocked: {e.Message}");
                }
            }

            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Loaded {_blocked.Count} blocked players");
        }

        private void SaveBlocked()
        {
            try
            {
                var wrapper = new SerializableStringArray
                {
                    values = new string[_blocked.Count]
                };

                int i = 0;
                foreach (var puid in _blocked)
                {
                    wrapper.values[i++] = puid;
                }

                PlayerPrefs.SetString(PREFS_BLOCKED_KEY, JsonUtility.ToJson(wrapper));
                PlayerPrefs.Save();
                _blockedDirty = false;

                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Saved {_blocked.Count} blocked players");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EOSPlayerRegistry] Failed to save blocked: {e.Message}");
            }
        }

        private async void AutoSyncBlockedToCloud()
        {
            if (!_cloudSyncEnabled) return;

            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return;

            // Debounce: don't sync more than once per 5 seconds
            if ((DateTime.Now - _lastCloudSync).TotalSeconds < 5) return;

            await SyncBlockedToCloudAsync();
        }

        private void LoadNotes()
        {
            _notes.Clear();

            string json = PlayerPrefs.GetString(PREFS_NOTES_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var wrapper = JsonUtility.FromJson<SerializableDict>(json);
                    if (wrapper?.keys != null && wrapper?.values != null)
                    {
                        int count = Mathf.Min(wrapper.keys.Length, wrapper.values.Length);
                        for (int i = 0; i < count; i++)
                        {
                            if (!string.IsNullOrEmpty(wrapper.keys[i]) && !string.IsNullOrEmpty(wrapper.values[i]))
                            {
                                _notes[wrapper.keys[i]] = wrapper.values[i];
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EOSPlayerRegistry] Failed to load notes: {e.Message}");
                }
            }

            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Loaded {_notes.Count} player notes");
        }

        private void SaveNotes()
        {
            try
            {
                var wrapper = new SerializableDict
                {
                    keys = new string[_notes.Count],
                    values = new string[_notes.Count]
                };

                int i = 0;
                foreach (var kvp in _notes)
                {
                    wrapper.keys[i] = kvp.Key;
                    wrapper.values[i] = kvp.Value;
                    i++;
                }

                PlayerPrefs.SetString(PREFS_NOTES_KEY, JsonUtility.ToJson(wrapper));
                PlayerPrefs.Save();
                _notesDirty = false;

                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Saved {_notes.Count} player notes");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EOSPlayerRegistry] Failed to save notes: {e.Message}");
            }
        }

        private void CleanupExpiredEntries()
        {
            long cutoff = DateTimeOffset.UtcNow.AddDays(-CACHE_EXPIRY_DAYS).ToUnixTimeSeconds();
            var toRemove = new List<string>();

            foreach (var kvp in _timestamps)
            {
                if (kvp.Value < cutoff)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
            {
                _cache.Remove(key);
                _timestamps.Remove(key);
            }

            if (toRemove.Count > 0)
            {
                _isDirty = true;
                EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Cleaned up {toRemove.Count} expired entries");
            }
        }

        private void TrimOldestEntries(int count)
        {
            // Find oldest entries by timestamp
            var sorted = new List<KeyValuePair<string, long>>(_timestamps);
            sorted.Sort((a, b) => a.Value.CompareTo(b.Value));

            for (int i = 0; i < Mathf.Min(count, sorted.Count); i++)
            {
                string key = sorted[i].Key;
                _cache.Remove(key);
                _timestamps.Remove(key);
            }

            EOSDebugLogger.Log(DebugCategory.PlayerRegistry, "EOSPlayerRegistry", $"Trimmed {count} oldest entries");
        }

        #endregion

        #region Serialization Helpers

        [Serializable]
        private class SerializableDict
        {
            public string[] keys;
            public string[] values;
        }

        [Serializable]
        private class SerializableLongDict
        {
            public string[] keys;
            public long[] values;
        }

        [Serializable]
        private class SerializableStringArray
        {
            public string[] values;
        }

        // Cloud sync data structures
        [Serializable]
        private class CloudFriendsData
        {
            public int version;
            public CloudFriendEntry[] friends;
        }

        [Serializable]
        private class CloudFriendEntry
        {
            public string puid;
            public string name;
        }

        [Serializable]
        private class SerializableIntDict
        {
            public string[] keys;
            public int[] values;
        }

        [Serializable]
        private class SerializableFloatDict
        {
            public string[] keys;
            public float[] values;
        }

        private string SerializeDictInt(Dictionary<string, int> dict)
        {
            var wrapper = new SerializableIntDict
            {
                keys = new string[dict.Count],
                values = new int[dict.Count]
            };
            int i = 0;
            foreach (var kvp in dict)
            {
                wrapper.keys[i] = kvp.Key;
                wrapper.values[i] = kvp.Value;
                i++;
            }
            return JsonUtility.ToJson(wrapper);
        }

        private Dictionary<string, int> DeserializeDictInt(string json)
        {
            var result = new Dictionary<string, int>();
            try
            {
                var wrapper = JsonUtility.FromJson<SerializableIntDict>(json);
                if (wrapper?.keys != null && wrapper.values != null)
                {
                    for (int i = 0; i < wrapper.keys.Length && i < wrapper.values.Length; i++)
                    {
                        result[wrapper.keys[i]] = wrapper.values[i];
                    }
                }
            }
            catch { }
            return result;
        }

        private string SerializeDictFloat(Dictionary<string, float> dict)
        {
            var wrapper = new SerializableFloatDict
            {
                keys = new string[dict.Count],
                values = new float[dict.Count]
            };
            int i = 0;
            foreach (var kvp in dict)
            {
                wrapper.keys[i] = kvp.Key;
                wrapper.values[i] = kvp.Value;
                i++;
            }
            return JsonUtility.ToJson(wrapper);
        }

        private Dictionary<string, float> DeserializeDictFloat(string json)
        {
            var result = new Dictionary<string, float>();
            try
            {
                var wrapper = JsonUtility.FromJson<SerializableFloatDict>(json);
                if (wrapper?.keys != null && wrapper.values != null)
                {
                    for (int i = 0; i < wrapper.keys.Length && i < wrapper.values.Length; i++)
                    {
                        result[wrapper.keys[i]] = wrapper.values[i];
                    }
                }
            }
            catch { }
            return result;
        }

        private string SerializeDictString(Dictionary<string, string> dict)
        {
            var wrapper = new SerializableDict
            {
                keys = new string[dict.Count],
                values = new string[dict.Count]
            };
            int i = 0;
            foreach (var kvp in dict)
            {
                wrapper.keys[i] = kvp.Key;
                wrapper.values[i] = kvp.Value;
                i++;
            }
            return JsonUtility.ToJson(wrapper);
        }

        private Dictionary<string, string> DeserializeDictString(string json)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var wrapper = JsonUtility.FromJson<SerializableDict>(json);
                if (wrapper?.keys != null && wrapper.values != null)
                {
                    for (int i = 0; i < wrapper.keys.Length && i < wrapper.values.Length; i++)
                    {
                        result[wrapper.keys[i]] = wrapper.values[i];
                    }
                }
            }
            catch { }
            return result;
        }

        #endregion
    }

    /// <summary>
    /// Online status of a friend.
    /// </summary>
    public enum FriendStatus
    {
        /// <summary>Status unknown (not yet queried or query failed).</summary>
        Unknown,
        /// <summary>Friend is offline (not in any lobby).</summary>
        Offline,
        /// <summary>Friend is in a game (found in a lobby search).</summary>
        InGame,
        /// <summary>Friend is in the same lobby as us.</summary>
        InLobby
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSPlayerRegistry))]
    public class EOSPlayerRegistryEditor : Editor
    {
        private Vector2 _scrollPos;
        private bool _showPlayers = true;

        public override void OnInspectorGUI()
        {
            var registry = (EOSPlayerRegistry)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Player Registry", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.IntField("Cached Players", registry.CachedPlayerCount);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);

                _showPlayers = EditorGUILayout.Foldout(_showPlayers, $"Players ({registry.CachedPlayerCount})", true);
                if (_showPlayers && registry.CachedPlayerCount > 0)
                {
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(150));

                    var recent = registry.GetRecentPlayers(30);
                    foreach (var (puid, name, lastSeen) in recent)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(name, GUILayout.Width(120));
                        EditorGUILayout.LabelField(puid.Substring(0, 8) + "...", EditorStyles.miniLabel, GUILayout.Width(80));
                        EditorGUILayout.LabelField(lastSeen.ToString("MM/dd HH:mm"), EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Force Save"))
                {
                    registry.ForceSave();
                }
                if (GUILayout.Button("Clear Cache"))
                {
                    if (EditorUtility.DisplayDialog("Clear Player Cache",
                        "Are you sure you want to clear all cached players?", "Yes", "No"))
                    {
                        registry.ClearCache();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see cached players.", MessageType.Info);
            }
        }
    }
#endif
}
