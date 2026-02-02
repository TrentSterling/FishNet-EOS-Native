using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Leaderboards;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// EOS Leaderboards manager for ranking queries.
    /// Requires leaderboards to be defined in the EOS Developer Portal.
    /// Works with DeviceID auth (Connect Interface).
    /// </summary>
    public class EOSLeaderboards : MonoBehaviour
    {
        #region Singleton

        private static EOSLeaderboards _instance;
        public static EOSLeaderboards Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSLeaderboards>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSLeaderboards");
                        _instance = go.AddComponent<EOSLeaderboards>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when leaderboard definitions are loaded.</summary>
        public event Action<List<LeaderboardDefinitionData>> OnDefinitionsLoaded;
        /// <summary>Fired when leaderboard ranks are queried.</summary>
        public event Action<string, List<LeaderboardEntry>> OnRanksLoaded;

        #endregion

        #region Private Fields

        private LeaderboardsInterface _leaderboardsInterface;
        private ProductUserId _localUserId;
        private List<LeaderboardDefinitionData> _definitions = new();

        #endregion

        #region Public Properties

        public bool IsReady => _leaderboardsInterface != null && _localUserId != null && _localUserId.IsValid();
        public IReadOnlyList<LeaderboardDefinitionData> Definitions => _definitions;
        public int DefinitionCount => _definitions.Count;

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

            _leaderboardsInterface = EOSManager.Instance.LeaderboardsInterface;
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_leaderboardsInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.Leaderboards, "EOSLeaderboards", "Initialized");
                // Auto-load definitions
                _ = QueryDefinitionsAsync();
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Leaderboards, "EOSLeaderboards", "LeaderboardsInterface not available");
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
        /// Query all leaderboard definitions.
        /// </summary>
        public async Task<Result> QueryDefinitionsAsync()
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new QueryLeaderboardDefinitionsOptions
            {
                LocalUserId = _localUserId
            };

            var tcs = new TaskCompletionSource<OnQueryLeaderboardDefinitionsCompleteCallbackInfo>();
            _leaderboardsInterface.QueryLeaderboardDefinitions(ref options, null, (ref OnQueryLeaderboardDefinitionsCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSLeaderboards] QueryDefinitions failed: {result.ResultCode}");
                return result.ResultCode;
            }

            // Cache definitions
            _definitions.Clear();
            var countOptions = new GetLeaderboardDefinitionCountOptions();
            uint count = _leaderboardsInterface.GetLeaderboardDefinitionCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyLeaderboardDefinitionByIndexOptions
                {
                    LeaderboardIndex = i
                };

                if (_leaderboardsInterface.CopyLeaderboardDefinitionByIndex(ref copyOptions, out var def) == Result.Success && def.HasValue)
                {
                    _definitions.Add(new LeaderboardDefinitionData
                    {
                        LeaderboardId = def.Value.LeaderboardId,
                        StatName = def.Value.StatName,
                        Aggregation = def.Value.Aggregation,
                        StartTime = def.Value.StartTime?.ToUnixTimeSeconds() ?? 0,
                        EndTime = def.Value.EndTime?.ToUnixTimeSeconds() ?? 0
                    });
                }
            }

            EOSDebugLogger.Log(DebugCategory.Leaderboards, "EOSLeaderboards", $" Loaded {_definitions.Count} leaderboard definitions");
            OnDefinitionsLoaded?.Invoke(_definitions);
            return Result.Success;
        }

        /// <summary>
        /// Query top N ranks for a leaderboard.
        /// </summary>
        /// <param name="leaderboardId">The leaderboard ID (from Developer Portal).</param>
        /// <param name="maxResults">Maximum number of results (default 100).</param>
        public async Task<(Result result, List<LeaderboardEntry> entries)> QueryRanksAsync(string leaderboardId, uint maxResults = 100)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (string.IsNullOrEmpty(leaderboardId))
                return (Result.InvalidParameters, null);

            var options = new QueryLeaderboardRanksOptions
            {
                LeaderboardId = leaderboardId,
                LocalUserId = _localUserId
            };

            var tcs = new TaskCompletionSource<OnQueryLeaderboardRanksCompleteCallbackInfo>();
            _leaderboardsInterface.QueryLeaderboardRanks(ref options, null, (ref OnQueryLeaderboardRanksCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSLeaderboards] QueryRanks failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Read records from cache
            var entries = new List<LeaderboardEntry>();
            var countOptions = new GetLeaderboardRecordCountOptions();
            uint count = _leaderboardsInterface.GetLeaderboardRecordCount(ref countOptions);
            count = Math.Min(count, maxResults);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyLeaderboardRecordByIndexOptions
                {
                    LeaderboardRecordIndex = i
                };

                if (_leaderboardsInterface.CopyLeaderboardRecordByIndex(ref copyOptions, out var record) == Result.Success && record.HasValue)
                {
                    entries.Add(new LeaderboardEntry
                    {
                        UserId = record.Value.UserId,
                        Rank = record.Value.Rank,
                        Score = record.Value.Score,
                        DisplayName = record.Value.UserDisplayName
                    });
                }
            }

            EOSDebugLogger.Log(DebugCategory.Leaderboards, "EOSLeaderboards", $" Queried {entries.Count} entries for {leaderboardId}");
            OnRanksLoaded?.Invoke(leaderboardId, entries);
            return (Result.Success, entries);
        }

        /// <summary>
        /// Query scores for specific users on a leaderboard.
        /// </summary>
        public async Task<(Result result, List<LeaderboardEntry> entries)> QueryUserScoresAsync(string leaderboardId, ProductUserId[] userIds, string statName)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (string.IsNullOrEmpty(leaderboardId) || userIds == null || userIds.Length == 0)
                return (Result.InvalidParameters, null);

            var statInfos = new UserScoresQueryStatInfo[]
            {
                new UserScoresQueryStatInfo
                {
                    StatName = statName,
                    Aggregation = LeaderboardAggregation.Max // Will use the leaderboard's aggregation
                }
            };

            var options = new QueryLeaderboardUserScoresOptions
            {
                LocalUserId = _localUserId,
                UserIds = userIds,
                StatInfo = statInfos
            };

            var tcs = new TaskCompletionSource<OnQueryLeaderboardUserScoresCompleteCallbackInfo>();
            _leaderboardsInterface.QueryLeaderboardUserScores(ref options, null, (ref OnQueryLeaderboardUserScoresCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSLeaderboards] QueryUserScores failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Read scores from cache
            var entries = new List<LeaderboardEntry>();
            var countOptions = new GetLeaderboardUserScoreCountOptions { StatName = statName };
            uint count = _leaderboardsInterface.GetLeaderboardUserScoreCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyLeaderboardUserScoreByIndexOptions
                {
                    LeaderboardUserScoreIndex = i,
                    StatName = statName
                };

                if (_leaderboardsInterface.CopyLeaderboardUserScoreByIndex(ref copyOptions, out var score) == Result.Success && score.HasValue)
                {
                    entries.Add(new LeaderboardEntry
                    {
                        UserId = score.Value.UserId,
                        Score = score.Value.Score,
                        Rank = 0 // Not available from user score query
                    });
                }
            }

            EOSDebugLogger.Log(DebugCategory.Leaderboards, "EOSLeaderboards", $" Queried {entries.Count} user scores for {leaderboardId}");
            return (Result.Success, entries);
        }

        /// <summary>
        /// Query the local user's score on a leaderboard.
        /// </summary>
        public async Task<(Result result, LeaderboardEntry? entry)> QueryMyScoreAsync(string leaderboardId, string statName)
        {
            var (queryResult, entries) = await QueryUserScoresAsync(leaderboardId, new[] { _localUserId }, statName);
            if (queryResult != Result.Success || entries == null || entries.Count == 0)
                return (queryResult, null);

            return (Result.Success, entries[0]);
        }

        /// <summary>
        /// Get a leaderboard definition by ID.
        /// </summary>
        public LeaderboardDefinitionData? GetDefinition(string leaderboardId)
        {
            return _definitions.Find(d => d.LeaderboardId == leaderboardId);
        }

        #endregion
    }

    /// <summary>
    /// Leaderboard definition data.
    /// </summary>
    [Serializable]
    public struct LeaderboardDefinitionData
    {
        public string LeaderboardId;
        public string StatName;
        public LeaderboardAggregation Aggregation;
        public long StartTime;
        public long EndTime;
    }

    /// <summary>
    /// Leaderboard entry (rank record).
    /// </summary>
    [Serializable]
    public struct LeaderboardEntry
    {
        public ProductUserId UserId;
        public uint Rank;
        public int Score;
        public string DisplayName;

        public string ShortUserId => UserId?.ToString()?.Length > 16
            ? UserId.ToString().Substring(0, 8) + "..."
            : UserId?.ToString() ?? "(unknown)";
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSLeaderboards))]
    public class EOSLeaderboardsEditor : Editor
    {
        private string _testLeaderboardId = "";
        private Vector2 _defScrollPos;
        private Vector2 _rankScrollPos;
        private List<LeaderboardEntry> _lastRanks = new();

        public override void OnInspectorGUI()
        {
            var leaderboards = (EOSLeaderboards)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("EOS Leaderboards", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", leaderboards.IsReady);
                EditorGUILayout.IntField("Definition Count", leaderboards.DefinitionCount);
            }

            if (!leaderboards.IsReady)
            {
                EditorGUILayout.HelpBox(
                    "Leaderboards require:\n" +
                    "1. EOS login\n" +
                    "2. Leaderboards defined in Developer Portal\n\n" +
                    "Note: Using default EOS demo keys means no leaderboards are configured.",
                    MessageType.Info);
            }

            if (Application.isPlaying && leaderboards.IsReady)
            {
                EditorGUILayout.Space(10);
                if (GUILayout.Button("Refresh Definitions"))
                {
                    _ = leaderboards.QueryDefinitionsAsync();
                }

                // Show definitions
                if (leaderboards.DefinitionCount > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Leaderboard Definitions", EditorStyles.boldLabel);
                    _defScrollPos = EditorGUILayout.BeginScrollView(_defScrollPos, GUILayout.Height(100));
                    foreach (var def in leaderboards.Definitions)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(def.LeaderboardId, GUILayout.Width(150));
                        EditorGUILayout.LabelField($"Stat: {def.StatName}", EditorStyles.miniLabel);
                        if (GUILayout.Button("Query", GUILayout.Width(50)))
                        {
                            QueryAndDisplay(leaderboards, def.LeaderboardId);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox("No leaderboards found. Make sure leaderboards are configured in the Developer Portal.", MessageType.Warning);
                }

                // Manual query
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Query Ranks", EditorStyles.boldLabel);
                _testLeaderboardId = EditorGUILayout.TextField("Leaderboard ID", _testLeaderboardId);
                GUI.enabled = !string.IsNullOrEmpty(_testLeaderboardId);
                if (GUILayout.Button("Query Top 10"))
                {
                    QueryAndDisplay(leaderboards, _testLeaderboardId);
                }
                GUI.enabled = true;

                // Show last query results
                if (_lastRanks.Count > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField($"Results ({_lastRanks.Count})", EditorStyles.boldLabel);
                    _rankScrollPos = EditorGUILayout.BeginScrollView(_rankScrollPos, GUILayout.Height(150));
                    foreach (var entry in _lastRanks)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"#{entry.Rank}", GUILayout.Width(40));
                        EditorGUILayout.LabelField(entry.DisplayName ?? entry.ShortUserId, GUILayout.Width(120));
                        EditorGUILayout.IntField(entry.Score);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }

                EditorUtility.SetDirty(target);
            }
        }

        private async void QueryAndDisplay(EOSLeaderboards leaderboards, string leaderboardId)
        {
            var (result, entries) = await leaderboards.QueryRanksAsync(leaderboardId, 10);
            if (result == Result.Success && entries != null)
            {
                _lastRanks = entries;
            }
        }
    }
#endif
}
