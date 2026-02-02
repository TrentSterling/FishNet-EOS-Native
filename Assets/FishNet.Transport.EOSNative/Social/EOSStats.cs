using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Stats;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// EOS Stats manager for player statistics.
    /// Stats are used to track player progress and feed into leaderboards/achievements.
    /// Requires stats to be defined in the EOS Developer Portal.
    /// Works with DeviceID auth (Connect Interface).
    /// </summary>
    public class EOSStats : MonoBehaviour
    {
        #region Singleton

        private static EOSStats _instance;
        public static EOSStats Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSStats>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSStats");
                        _instance = go.AddComponent<EOSStats>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when stats are queried/refreshed.</summary>
        public event Action<List<StatData>> OnStatsUpdated;
        /// <summary>Fired when a stat is ingested.</summary>
        public event Action<string, int> OnStatIngested;

        #endregion

        #region Private Fields

        private StatsInterface _statsInterface;
        private ProductUserId _localUserId;
        private Dictionary<string, StatData> _cachedStats = new();

        #endregion

        #region Public Properties

        public bool IsReady => _statsInterface != null && _localUserId != null && _localUserId.IsValid();
        public IReadOnlyDictionary<string, StatData> CachedStats => _cachedStats;
        public int CachedStatsCount => _cachedStats.Count;

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

            _statsInterface = EOSManager.Instance.StatsInterface;
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_statsInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.Stats, "EOSStats", "Initialized");
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSStats", "StatsInterface not available");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Ingest a single stat value.
        /// </summary>
        /// <param name="statName">The stat name (as defined in Developer Portal).</param>
        /// <param name="amount">The amount to add (for SUM) or set (for LATEST/MIN/MAX).</param>
        public async Task<Result> IngestStatAsync(string statName, int amount)
        {
            return await IngestStatsAsync(new[] { (statName, amount) });
        }

        /// <summary>
        /// Ingest multiple stat values in a single call.
        /// </summary>
        public async Task<Result> IngestStatsAsync(params (string statName, int amount)[] stats)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (stats == null || stats.Length == 0)
                return Result.InvalidParameters;

            var ingestData = new IngestData[stats.Length];
            for (int i = 0; i < stats.Length; i++)
            {
                ingestData[i] = new IngestData
                {
                    StatName = stats[i].statName,
                    IngestAmount = stats[i].amount
                };
            }

            var options = new IngestStatOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = _localUserId,
                Stats = ingestData
            };

            var tcs = new TaskCompletionSource<IngestStatCompleteCallbackInfo>();
            _statsInterface.IngestStat(ref options, null, (ref IngestStatCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                foreach (var stat in stats)
                {
                    EOSDebugLogger.Log(DebugCategory.Stats, "EOSStats", $" Ingested: {stat.statName} += {stat.amount}");
                    OnStatIngested?.Invoke(stat.statName, stat.amount);
                }
            }
            else
            {
                Debug.LogWarning($"[EOSStats] IngestStat failed: {result.ResultCode}");
            }

            return result.ResultCode;
        }

        /// <summary>
        /// Query stats for the local player.
        /// </summary>
        public Task<(Result result, List<StatData> stats)> QueryMyStatsAsync(params string[] statNames)
        {
            return QueryStatsAsync(_localUserId, statNames);
        }

        /// <summary>
        /// Query stats for a specific player.
        /// </summary>
        public async Task<(Result result, List<StatData> stats)> QueryStatsAsync(ProductUserId targetUserId, params string[] statNames)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (targetUserId == null || !targetUserId.IsValid())
                return (Result.InvalidParameters, null);

            var options = new QueryStatsOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = targetUserId,
                StatNames = statNames?.Length > 0 ? statNames.Select(s => new Utf8String(s)).ToArray() : null // null = all stats
            };

            var tcs = new TaskCompletionSource<OnQueryStatsCompleteCallbackInfo>();
            _statsInterface.QueryStats(ref options, null, (ref OnQueryStatsCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSStats] QueryStats failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Read stats from cache
            var stats = new List<StatData>();
            var countOptions = new GetStatCountOptions { TargetUserId = targetUserId };
            uint count = _statsInterface.GetStatsCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyStatByIndexOptions
                {
                    TargetUserId = targetUserId,
                    StatIndex = i
                };

                if (_statsInterface.CopyStatByIndex(ref copyOptions, out var stat) == Result.Success && stat.HasValue)
                {
                    var statData = new StatData
                    {
                        Name = stat.Value.Name,
                        Value = stat.Value.Value,
                        StartTime = stat.Value.StartTime?.ToUnixTimeSeconds() ?? 0,
                        EndTime = stat.Value.EndTime?.ToUnixTimeSeconds() ?? 0
                    };
                    stats.Add(statData);

                    // Cache for local user
                    if (targetUserId == _localUserId)
                    {
                        _cachedStats[stat.Value.Name] = statData;
                    }
                }
            }

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSStats", $" Queried {stats.Count} stats for {targetUserId}");

            if (targetUserId == _localUserId)
            {
                OnStatsUpdated?.Invoke(stats);
            }

            return (Result.Success, stats);
        }

        /// <summary>
        /// Get a cached stat value by name.
        /// Returns null if not cached (call QueryMyStatsAsync first).
        /// </summary>
        public StatData? GetCachedStat(string statName)
        {
            return _cachedStats.TryGetValue(statName, out var stat) ? stat : null;
        }

        /// <summary>
        /// Get a cached stat value or default.
        /// </summary>
        public int GetCachedStatValue(string statName, int defaultValue = 0)
        {
            return _cachedStats.TryGetValue(statName, out var stat) ? stat.Value : defaultValue;
        }

        #endregion
    }

    /// <summary>
    /// Player stat data.
    /// </summary>
    [Serializable]
    public struct StatData
    {
        public string Name;
        public int Value;
        public long StartTime;
        public long EndTime;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSStats))]
    public class EOSStatsEditor : Editor
    {
        private string _testStatName = "test_stat";
        private int _testAmount = 1;
        private Vector2 _scrollPos;

        public override void OnInspectorGUI()
        {
            var stats = (EOSStats)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("EOS Stats", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", stats.IsReady);
                EditorGUILayout.IntField("Cached Stats", stats.CachedStatsCount);
            }

            if (!stats.IsReady)
            {
                EditorGUILayout.HelpBox(
                    "Stats require:\n" +
                    "1. EOS login\n" +
                    "2. Stats defined in Developer Portal\n\n" +
                    "Note: Using default EOS demo keys means no stats are configured.",
                    MessageType.Info);
            }

            if (Application.isPlaying && stats.IsReady)
            {
                EditorGUILayout.Space(10);
                if (GUILayout.Button("Query My Stats"))
                {
                    _ = stats.QueryMyStatsAsync();
                }

                // Cached stats display
                if (stats.CachedStatsCount > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Cached Stats", EditorStyles.boldLabel);
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(100));
                    foreach (var kvp in stats.CachedStats)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(120));
                        EditorGUILayout.IntField(kvp.Value.Value);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Ingest Stat", EditorStyles.boldLabel);
                _testStatName = EditorGUILayout.TextField("Stat Name", _testStatName);
                _testAmount = EditorGUILayout.IntField("Amount", _testAmount);
                if (GUILayout.Button("Ingest"))
                {
                    _ = stats.IngestStatAsync(_testStatName, _testAmount);
                }

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
