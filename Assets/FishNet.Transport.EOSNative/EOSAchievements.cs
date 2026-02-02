using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Achievements;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// EOS Achievements manager.
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

        #endregion

        #region Private Fields

        private AchievementsInterface _achievementsInterface;
        private ProductUserId _localUserId;
        private List<AchievementDefinition> _definitions = new();
        private List<PlayerAchievementData> _playerAchievements = new();
        private ulong _achievementUnlockedHandle;

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
            OnAchievementUnlocked?.Invoke(achievementId);

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
