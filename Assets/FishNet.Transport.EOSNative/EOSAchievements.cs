using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Achievements;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Stat-based achievement trigger configuration
    /// </summary>
    [Serializable]
    public class AchievementStatTrigger
    {
        public string AchievementId;
        public string StatName;
        public int TargetValue;
        public bool IsProgressive;      // Update progress as stat increases
    }

    /// <summary>
    /// EOS Achievements manager with progress tracking, stat triggers, and popups.
    /// Requires achievements to be defined in the EOS Developer Portal.
    /// Works with DeviceID auth (Connect Interface).
    /// </summary>
    public class EOSAchievements : MonoBehaviour
    {
        #region Singleton

        private static EOSAchievements _instance;
        public static EOSAchievements Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSAchievements>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSAchievements");
                        _instance = go.AddComponent<EOSAchievements>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when achievements are loaded/refreshed.</summary>
        public event Action OnAchievementsLoaded;
        /// <summary>Fired when an achievement is unlocked.</summary>
        public event Action<string> OnAchievementUnlocked;
        /// <summary>Fired when achievement progress changes (id, oldProgress, newProgress).</summary>
        public event Action<string, float, float> OnProgressChanged;

        #endregion

        #region Settings

        [Header("Popup Settings")]
        [SerializeField] private bool _showPopups = true;
        [SerializeField] private float _popupDuration = 5f;

        [Header("Offline Caching")]
        [SerializeField] private bool _enableOfflineCache = true;

        [Header("Stat Triggers")]
        [SerializeField] private List<AchievementStatTrigger> _statTriggers = new();

        #endregion

        #region Private Fields

        private AchievementsInterface _achievementsInterface;
        private ProductUserId _localUserId;
        private List<AchievementDefinition> _definitions = new();
        private List<PlayerAchievementData> _playerAchievements = new();
        private ulong _achievementUnlockedHandle;
        private readonly Dictionary<string, Texture2D> _iconCache = new();
        private readonly Dictionary<string, int> _localStats = new();
        private const string CACHE_PREFIX = "EOS_ACH_";

        #endregion

        #region Public Properties

        public bool IsReady
        {
            get
            {
                if (_achievementsInterface == null || _localUserId == null)
                    return false;
                try { return _localUserId.IsValid(); }
                catch { return false; }
            }
        }
        public IReadOnlyList<AchievementDefinition> Definitions => _definitions;
        public IReadOnlyList<PlayerAchievementData> PlayerAchievements => _playerAchievements;
        public int TotalAchievements => _definitions.Count;
        public int UnlockedCount => _playerAchievements.FindAll(a => a.IsUnlocked).Count;
        public bool ShowPopups { get => _showPopups; set => _showPopups = value; }
        public float PopupDuration { get => _popupDuration; set => _popupDuration = value; }
        public bool EnableOfflineCache { get => _enableOfflineCache; set => _enableOfflineCache = value; }
        public IReadOnlyList<AchievementStatTrigger> StatTriggers => _statTriggers;

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
            StartCoroutine(InitializeCoroutine());
        }

        private System.Collections.IEnumerator InitializeCoroutine()
        {
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _achievementsInterface = EOSManager.Instance.Platform?.GetAchievementsInterface();
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_achievementsInterface != null)
            {
                SubscribeToNotifications();
                EOSDebugLogger.Log(DebugCategory.Achievements, "EOSAchievements", "Initialized");

                // Auto-load achievements
                _ = RefreshAsync();
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Achievements, "EOSAchievements", "AchievementsInterface not available");
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromNotifications();
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Refresh all achievement data (definitions + player progress).
        /// </summary>
        public async Task<Result> RefreshAsync()
        {
            if (!IsReady)
                return Result.NotConfigured;

            // Query definitions
            var defResult = await QueryDefinitionsAsync();
            if (defResult != Result.Success)
                return defResult;

            // Query player achievements
            var playerResult = await QueryPlayerAchievementsAsync();
            if (playerResult != Result.Success)
                return playerResult;

            // Load offline cache
            LoadOfflineCache();

            EOSDebugLogger.Log(DebugCategory.Achievements, "EOSAchievements", $" Loaded {_definitions.Count} achievements, {UnlockedCount} unlocked");
            OnAchievementsLoaded?.Invoke();
            return Result.Success;
        }

        /// <summary>
        /// Query achievement definitions from server.
        /// </summary>
        public async Task<Result> QueryDefinitionsAsync()
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new QueryDefinitionsOptions
            {
                LocalUserId = _localUserId
            };

            var tcs = new TaskCompletionSource<OnQueryDefinitionsCompleteCallbackInfo>();
            _achievementsInterface.QueryDefinitions(ref options, null, (ref OnQueryDefinitionsCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSAchievements] QueryDefinitions failed: {result.ResultCode}");
                return result.ResultCode;
            }

            // Cache definitions
            _definitions.Clear();
            var countOptions = new GetAchievementDefinitionCountOptions();
            uint count = _achievementsInterface.GetAchievementDefinitionCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyAchievementDefinitionV2ByIndexOptions
                {
                    AchievementIndex = i
                };

                if (_achievementsInterface.CopyAchievementDefinitionV2ByIndex(ref copyOptions, out var def) == Result.Success && def.HasValue)
                {
                    _definitions.Add(new AchievementDefinition
                    {
                        Id = def.Value.AchievementId,
                        DisplayName = def.Value.UnlockedDisplayName,
                        Description = def.Value.UnlockedDescription,
                        LockedDisplayName = def.Value.LockedDisplayName,
                        LockedDescription = def.Value.LockedDescription,
                        UnlockedIconUrl = def.Value.UnlockedIconURL,
                        LockedIconUrl = def.Value.LockedIconURL,
                        IsHidden = def.Value.IsHidden
                    });
                }
            }

            return Result.Success;
        }

        /// <summary>
        /// Query player's achievement progress from server.
        /// </summary>
        public async Task<Result> QueryPlayerAchievementsAsync()
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new QueryPlayerAchievementsOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = _localUserId
            };

            var tcs = new TaskCompletionSource<OnQueryPlayerAchievementsCompleteCallbackInfo>();
            _achievementsInterface.QueryPlayerAchievements(ref options, null, (ref OnQueryPlayerAchievementsCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSAchievements] QueryPlayerAchievements failed: {result.ResultCode}");
                return result.ResultCode;
            }

            // Cache player achievements
            _playerAchievements.Clear();
            var countOptions = new GetPlayerAchievementCountOptions
            {
                UserId = _localUserId
            };
            uint count = _achievementsInterface.GetPlayerAchievementCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyPlayerAchievementByIndexOptions
                {
                    AchievementIndex = i,
                    LocalUserId = _localUserId,
                    TargetUserId = _localUserId
                };

                if (_achievementsInterface.CopyPlayerAchievementByIndex(ref copyOptions, out var achievement) == Result.Success && achievement.HasValue)
                {
                    _playerAchievements.Add(new PlayerAchievementData
                    {
                        Id = achievement.Value.AchievementId,
                        Progress = achievement.Value.Progress,
                        UnlockTime = achievement.Value.UnlockTime,
                        IsUnlocked = achievement.Value.UnlockTime.HasValue
                    });
                }
            }

            return Result.Success;
        }

        /// <summary>
        /// Unlock an achievement.
        /// </summary>
        public async Task<Result> UnlockAchievementAsync(string achievementId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (string.IsNullOrEmpty(achievementId))
                return Result.InvalidParameters;

            var options = new UnlockAchievementsOptions
            {
                UserId = _localUserId,
                AchievementIds = new Utf8String[] { achievementId }
            };

            var tcs = new TaskCompletionSource<OnUnlockAchievementsCompleteCallbackInfo>();
            _achievementsInterface.UnlockAchievements(ref options, null, (ref OnUnlockAchievementsCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.Achievements, "EOSAchievements", $" Unlocked: {achievementId}");
                // Refresh to update local cache
                await QueryPlayerAchievementsAsync();
            }
            else
            {
                Debug.LogWarning($"[EOSAchievements] UnlockAchievement failed: {result.ResultCode}");
            }

            return result.ResultCode;
        }

        /// <summary>
        /// Unlock an achievement with host-authority validation.
        /// Use this instead of UnlockAchievementAsync for secure multiplayer games.
        /// The host must validate the unlock criteria before it's applied.
        /// </summary>
        public void UnlockAchievementSecure(string achievementId, Dictionary<string, object> context = null)
        {
            var validator = Security.HostAuthorityValidator.Instance;
            if (validator != null)
            {
                validator.RequestAchievementValidation(achievementId, context);
            }
            else
            {
                // Fallback to direct call if no validator (offline mode, etc.)
                Logging.EOSDebugLogger.LogWarning("EOSAchievements",
                    "No HostAuthorityValidator found - using direct unlock (not secure)");
                _ = UnlockAchievementAsync(achievementId);
            }
        }

        /// <summary>
        /// Get an achievement definition by ID.
        /// </summary>
        public AchievementDefinition? GetDefinition(string achievementId)
        {
            return _definitions.Find(d => d.Id == achievementId);
        }

        /// <summary>
        /// Get player achievement progress by ID.
        /// </summary>
        public PlayerAchievementData? GetPlayerAchievement(string achievementId)
        {
            return _playerAchievements.Find(a => a.Id == achievementId);
        }

        /// <summary>
        /// Check if an achievement is unlocked.
        /// </summary>
        public bool IsUnlocked(string achievementId)
        {
            var achievement = GetPlayerAchievement(achievementId);
            return achievement?.IsUnlocked ?? false;
        }

        /// <summary>
        /// Get progress for an achievement (0.0 to 1.0).
        /// </summary>
        public float GetProgress(string achievementId)
        {
            var achievement = GetPlayerAchievement(achievementId);
            return (float)(achievement?.Progress ?? 0.0);
        }

        /// <summary>
        /// Set progress for an achievement (0.0 to 1.0). Auto-unlocks at 1.0.
        /// </summary>
        public async Task<Result> SetProgressAsync(string achievementId, float progress)
        {
            progress = Mathf.Clamp01(progress);
            float oldProgress = GetProgress(achievementId);

            if (progress >= 1f)
            {
                return await UnlockAchievementAsync(achievementId);
            }

            // Update local cache
            var index = _playerAchievements.FindIndex(a => a.Id == achievementId);
            if (index >= 0)
            {
                var updated = _playerAchievements[index];
                updated.Progress = progress;
                _playerAchievements[index] = updated;
            }

            // Cache offline
            if (_enableOfflineCache)
            {
                PlayerPrefs.SetFloat(CACHE_PREFIX + achievementId, progress);
                PlayerPrefs.Save();
            }

            OnProgressChanged?.Invoke(achievementId, oldProgress, progress);
            return Result.Success;
        }

        /// <summary>
        /// Increment progress for an achievement.
        /// </summary>
        public async Task<Result> IncrementProgressAsync(string achievementId, float amount)
        {
            float current = GetProgress(achievementId);
            return await SetProgressAsync(achievementId, current + amount);
        }

        /// <summary>
        /// Increment progress based on current/target values (e.g., 50 kills out of 100).
        /// </summary>
        public async Task<Result> SetProgressCountAsync(string achievementId, int current, int target)
        {
            if (target <= 0) return Result.InvalidParameters;
            float progress = (float)current / target;
            return await SetProgressAsync(achievementId, progress);
        }

        #endregion

        #region Stat Triggers

        /// <summary>
        /// Register a stat trigger for auto-unlock/progress.
        /// </summary>
        public void RegisterStatTrigger(string achievementId, string statName, int targetValue, bool isProgressive = true)
        {
            var existing = _statTriggers.FindIndex(t => t.AchievementId == achievementId);
            var trigger = new AchievementStatTrigger
            {
                AchievementId = achievementId,
                StatName = statName,
                TargetValue = targetValue,
                IsProgressive = isProgressive
            };

            if (existing >= 0)
                _statTriggers[existing] = trigger;
            else
                _statTriggers.Add(trigger);
        }

        /// <summary>
        /// Report a stat value change. Checks triggers and updates achievements.
        /// </summary>
        public async void ReportStat(string statName, int value)
        {
            _localStats[statName] = value;

            foreach (var trigger in _statTriggers)
            {
                if (trigger.StatName != statName) continue;
                if (IsUnlocked(trigger.AchievementId)) continue;

                if (value >= trigger.TargetValue)
                {
                    await UnlockAchievementAsync(trigger.AchievementId);
                }
                else if (trigger.IsProgressive)
                {
                    await SetProgressCountAsync(trigger.AchievementId, value, trigger.TargetValue);
                }
            }
        }

        /// <summary>
        /// Increment a stat value. Checks triggers and updates achievements.
        /// </summary>
        public void IncrementStat(string statName, int amount = 1)
        {
            int current = _localStats.TryGetValue(statName, out var val) ? val : 0;
            ReportStat(statName, current + amount);
        }

        /// <summary>
        /// Get current stat value.
        /// </summary>
        public int GetStat(string statName)
        {
            return _localStats.TryGetValue(statName, out var val) ? val : 0;
        }

        #endregion

        #region Icon Loading

        /// <summary>
        /// Load achievement icon texture (cached).
        /// </summary>
        public void LoadIcon(string achievementId, bool unlocked, Action<Texture2D> callback)
        {
            var def = GetDefinition(achievementId);
            if (!def.HasValue)
            {
                callback?.Invoke(null);
                return;
            }

            string url = unlocked ? def.Value.UnlockedIconUrl : def.Value.LockedIconUrl;
            if (string.IsNullOrEmpty(url))
            {
                callback?.Invoke(null);
                return;
            }

            string cacheKey = $"{achievementId}_{(unlocked ? "u" : "l")}";
            if (_iconCache.TryGetValue(cacheKey, out var cached))
            {
                callback?.Invoke(cached);
                return;
            }

            StartCoroutine(LoadIconCoroutine(url, cacheKey, callback));
        }

        private IEnumerator LoadIconCoroutine(string url, string cacheKey, Action<Texture2D> callback)
        {
            using var request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var texture = DownloadHandlerTexture.GetContent(request);
                _iconCache[cacheKey] = texture;
                callback?.Invoke(texture);
            }
            else
            {
                callback?.Invoke(null);
            }
        }

        /// <summary>
        /// Get cached icon if available.
        /// </summary>
        public Texture2D GetCachedIcon(string achievementId, bool unlocked)
        {
            string cacheKey = $"{achievementId}_{(unlocked ? "u" : "l")}";
            return _iconCache.TryGetValue(cacheKey, out var tex) ? tex : null;
        }

        #endregion

        #region Offline Cache

        private void LoadOfflineCache()
        {
            if (!_enableOfflineCache) return;

            foreach (var def in _definitions)
            {
                string key = CACHE_PREFIX + def.Id;
                if (PlayerPrefs.HasKey(key))
                {
                    float progress = PlayerPrefs.GetFloat(key);
                    var index = _playerAchievements.FindIndex(a => a.Id == def.Id);
                    if (index >= 0)
                    {
                        var current = _playerAchievements[index];
                        // Only use cache if server progress is lower
                        if (current.Progress < progress && !current.IsUnlocked)
                        {
                            current.Progress = progress;
                            _playerAchievements[index] = current;
                        }
                    }
                }
            }
        }

        private void SaveOfflineCache()
        {
            if (!_enableOfflineCache) return;

            foreach (var achievement in _playerAchievements)
            {
                PlayerPrefs.SetFloat(CACHE_PREFIX + achievement.Id, (float)achievement.Progress);
            }
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Clear offline cache.
        /// </summary>
        public void ClearOfflineCache()
        {
            foreach (var def in _definitions)
            {
                PlayerPrefs.DeleteKey(CACHE_PREFIX + def.Id);
            }
            PlayerPrefs.Save();
        }

        #endregion

        #region Popup Integration

        private void ShowUnlockPopup(string achievementId)
        {
            if (!_showPopups) return;

            var def = GetDefinition(achievementId);
            if (!def.HasValue) return;

            // Use toast system if available
            if (EOSToastManager.Instance != null)
            {
                EOSToastManager.Success(
                    "Achievement Unlocked!",
                    def.Value.DisplayName,
                    _popupDuration
                );
            }

            // Also fire event for custom UI
            EOSDebugLogger.Log(DebugCategory.Achievements, "EOSAchievements",
                $"[POPUP] Achievement Unlocked: {def.Value.DisplayName}");
        }

        #endregion

        #region Notifications

        private void SubscribeToNotifications()
        {
            var options = new AddNotifyAchievementsUnlockedV2Options();
            _achievementUnlockedHandle = _achievementsInterface.AddNotifyAchievementsUnlockedV2(
                ref options, null, OnAchievementUnlockedCallback);
        }

        private void UnsubscribeFromNotifications()
        {
            if (_achievementUnlockedHandle != 0 && _achievementsInterface != null)
            {
                try
                {
                    _achievementsInterface.RemoveNotifyAchievementsUnlocked(_achievementUnlockedHandle);
                }
                catch { /* SDK may already be shut down */ }
                _achievementUnlockedHandle = 0;
            }
        }

        private void OnAchievementUnlockedCallback(ref OnAchievementsUnlockedCallbackV2Info info)
        {
            // Copy values from ref parameter to avoid lambda capture issues
            string achievementId = info.AchievementId;
            var unlockTime = info.UnlockTime;

            EOSDebugLogger.Log(DebugCategory.Achievements, "EOSAchievements", $" Achievement unlocked: {achievementId}");

            // Update local cache
            var existing = _playerAchievements.FindIndex(a => a.Id == achievementId);
            if (existing >= 0)
            {
                var updated = _playerAchievements[existing];
                updated.IsUnlocked = true;
                updated.UnlockTime = unlockTime;
                updated.Progress = 1.0;
                _playerAchievements[existing] = updated;
            }

            // Show popup
            ShowUnlockPopup(achievementId);

            // Fire event
            OnAchievementUnlocked?.Invoke(achievementId);
        }

        #endregion
    }

    /// <summary>
    /// Achievement definition data.
    /// </summary>
    [Serializable]
    public struct AchievementDefinition
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string LockedDisplayName;
        public string LockedDescription;
        public string UnlockedIconUrl;
        public string LockedIconUrl;
        public bool IsHidden;
    }

    /// <summary>
    /// Player achievement progress data.
    /// </summary>
    [Serializable]
    public struct PlayerAchievementData
    {
        public string Id;
        public double Progress;
        public DateTimeOffset? UnlockTime;
        public bool IsUnlocked;

        public DateTime? UnlockDateTime => UnlockTime?.LocalDateTime;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSAchievements))]
    public class EOSAchievementsEditor : Editor
    {
        private Vector2 _scrollPos;
        private string _testAchievementId = "";

        public override void OnInspectorGUI()
        {
            var achievements = (EOSAchievements)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("EOS Achievements", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", achievements.IsReady);
                EditorGUILayout.IntField("Total Achievements", achievements.TotalAchievements);
                EditorGUILayout.IntField("Unlocked", achievements.UnlockedCount);
            }

            if (!achievements.IsReady)
            {
                EditorGUILayout.HelpBox(
                    "Achievements require:\n" +
                    "1. EOS login (DeviceID or Epic Account)\n" +
                    "2. Achievements defined in Developer Portal\n\n" +
                    "Note: Using default EOS demo keys means no achievements are configured.",
                    MessageType.Info);
            }

            if (Application.isPlaying && achievements.IsReady)
            {
                EditorGUILayout.Space(10);
                if (GUILayout.Button("Refresh Achievements"))
                {
                    _ = achievements.RefreshAsync();
                }

                // Show definitions
                if (achievements.TotalAchievements > 0)
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.LabelField("Achievement List", EditorStyles.boldLabel);

                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(150));
                    foreach (var def in achievements.Definitions)
                    {
                        var playerData = achievements.GetPlayerAchievement(def.Id);
                        bool unlocked = playerData?.IsUnlocked ?? false;
                        string status = unlocked ? "[UNLOCKED]" : $"[{(playerData?.Progress ?? 0) * 100:F0}%]";

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{status} {def.DisplayName}", EditorStyles.miniLabel);
                        GUI.enabled = !unlocked;
                        if (GUILayout.Button("Unlock", GUILayout.Width(60)))
                        {
                            _ = achievements.UnlockAchievementAsync(def.Id);
                        }
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox("No achievements found. Make sure achievements are configured in the Developer Portal.", MessageType.Warning);
                }

                // Manual unlock
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Manual Unlock", EditorStyles.boldLabel);
                _testAchievementId = EditorGUILayout.TextField("Achievement ID", _testAchievementId);
                GUI.enabled = !string.IsNullOrEmpty(_testAchievementId);
                if (GUILayout.Button("Unlock by ID"))
                {
                    _ = achievements.UnlockAchievementAsync(_testAchievementId);
                }
                GUI.enabled = true;

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
