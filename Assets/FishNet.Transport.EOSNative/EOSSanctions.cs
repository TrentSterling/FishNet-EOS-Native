using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Sanctions;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Query player sanctions (bans, restrictions).
    /// Use to check if a player should be allowed to join.
    /// Works with DeviceID (no Epic Account needed).
    /// </summary>
    public class EOSSanctions : MonoBehaviour
    {
        #region Singleton

        private static EOSSanctions _instance;
        public static EOSSanctions Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSSanctions>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSSanctions");
                        _instance = go.AddComponent<EOSSanctions>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when sanctions are queried for a player.</summary>
        public event Action<string, List<SanctionInfo>> OnSanctionsQueried; // targetPuid, sanctions

        #endregion

        #region Private Fields

        private SanctionsInterface _sanctionsInterface;
        private ProductUserId _localUserId;
        private Dictionary<string, List<SanctionInfo>> _sanctionCache = new();

        #endregion

        #region Public Properties

        /// <summary>Whether sanctions are ready to use.</summary>
        public bool IsReady => _sanctionsInterface != null && _localUserId != null;

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

            _sanctionsInterface = EOSManager.Instance.Platform?.GetSanctionsInterface();
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_sanctionsInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSSanctions", "Initialized");
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Sanctions, "EOSSanctions", "SanctionsInterface not available");
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
        /// Query active sanctions for a player.
        /// </summary>
        /// <param name="targetPuid">PUID of player to check.</param>
        /// <returns>Result and list of active sanctions.</returns>
        public async Task<(Result result, List<SanctionInfo> sanctions)> QuerySanctionsAsync(string targetPuid)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (string.IsNullOrEmpty(targetPuid))
                return (Result.InvalidParameters, null);

            var targetUserId = ProductUserId.FromString(targetPuid);
            if (targetUserId == null || !targetUserId.IsValid())
                return (Result.InvalidParameters, null);

            var options = new QueryActivePlayerSanctionsOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<QueryActivePlayerSanctionsCallbackInfo>();
            _sanctionsInterface.QueryActivePlayerSanctions(ref options, null, (ref QueryActivePlayerSanctionsCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSSanctions] Query failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Get sanctions from cache
            var sanctions = new List<SanctionInfo>();
            var countOptions = new GetPlayerSanctionCountOptions { TargetUserId = targetUserId };
            uint count = _sanctionsInterface.GetPlayerSanctionCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new CopyPlayerSanctionByIndexOptions
                {
                    TargetUserId = targetUserId,
                    SanctionIndex = i
                };

                if (_sanctionsInterface.CopyPlayerSanctionByIndex(ref copyOptions, out var sanction) == Result.Success && sanction.HasValue)
                {
                    // Convert PlayerSanction to our SanctionInfo
                    sanctions.Add(new SanctionInfo
                    {
                        ReferenceId = sanction.Value.ReferenceId,
                        Action = sanction.Value.Action,
                        TimePlaced = sanction.Value.TimePlaced,
                        TimeExpires = sanction.Value.TimeExpires
                    });
                }
            }

            // Cache results
            _sanctionCache[targetPuid] = sanctions;

            EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSSanctions", $" Found {sanctions.Count} sanctions for {targetPuid.Substring(0, 8)}...");
            OnSanctionsQueried?.Invoke(targetPuid, sanctions);
            return (Result.Success, sanctions);
        }

        /// <summary>
        /// Check if a player is banned (has any active sanctions).
        /// </summary>
        public async Task<bool> IsPlayerBannedAsync(string targetPuid)
        {
            var (result, sanctions) = await QuerySanctionsAsync(targetPuid);
            return result == Result.Success && sanctions != null && sanctions.Count > 0;
        }

        /// <summary>
        /// Check if the local player is banned.
        /// </summary>
        public async Task<bool> IsLocalPlayerBannedAsync()
        {
            if (_localUserId == null)
                return false;

            return await IsPlayerBannedAsync(_localUserId.ToString());
        }

        /// <summary>
        /// Get cached sanctions for a player (call QuerySanctionsAsync first).
        /// </summary>
        public List<SanctionInfo> GetCachedSanctions(string targetPuid)
        {
            return _sanctionCache.TryGetValue(targetPuid, out var sanctions) ? sanctions : null;
        }

        /// <summary>
        /// Clear the sanctions cache.
        /// </summary>
        public void ClearCache()
        {
            _sanctionCache.Clear();
        }

        #endregion

        #region Helper

        /// <summary>
        /// Get human-readable description of a sanction.
        /// </summary>
        public static string DescribeSanction(SanctionInfo sanction)
        {
            string action = sanction.Action ?? "Unknown";
            var expireTime = DateTimeOffset.FromUnixTimeSeconds(sanction.TimeExpires).LocalDateTime;
            bool isPermanent = sanction.TimeExpires == 0 || expireTime.Year > 2100;

            if (isPermanent)
                return $"{action} (Permanent)";

            return $"{action} (Until {expireTime:yyyy-MM-dd HH:mm})";
        }

        #endregion
    }

    /// <summary>
    /// Sanction information.
    /// </summary>
    [Serializable]
    public struct SanctionInfo
    {
        public string ReferenceId;
        public string Action;
        public long TimePlaced;
        public long TimeExpires;

        public DateTime PlacedDate => DateTimeOffset.FromUnixTimeSeconds(TimePlaced).LocalDateTime;
        public DateTime? ExpiresDate => TimeExpires > 0 ? DateTimeOffset.FromUnixTimeSeconds(TimeExpires).LocalDateTime : null;
        public bool IsPermanent => TimeExpires == 0 || ExpiresDate?.Year > 2100;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSSanctions))]
    public class EOSSanctionsEditor : Editor
    {
        private string _testPuid = "";

        public override void OnInspectorGUI()
        {
            var sanctions = (EOSSanctions)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Player Sanctions", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", sanctions.IsReady);
            }

            if (Application.isPlaying && sanctions.IsReady)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Query Sanctions", EditorStyles.boldLabel);

                _testPuid = EditorGUILayout.TextField("Target PUID", _testPuid);

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = !string.IsNullOrEmpty(_testPuid);
                if (GUILayout.Button("Query"))
                {
                    _ = sanctions.QuerySanctionsAsync(_testPuid);
                }
                GUI.enabled = true;

                if (GUILayout.Button("Check Self"))
                {
                    _ = CheckSelfAsync(sanctions);
                }
                EditorGUILayout.EndHorizontal();

                // Show cached results
                if (!string.IsNullOrEmpty(_testPuid))
                {
                    var cached = sanctions.GetCachedSanctions(_testPuid);
                    if (cached != null)
                    {
                        EditorGUILayout.Space(5);
                        if (cached.Count == 0)
                        {
                            EditorGUILayout.LabelField("No active sanctions", EditorStyles.miniLabel);
                        }
                        else
                        {
                            EditorGUILayout.LabelField($"{cached.Count} active sanction(s):", EditorStyles.miniLabel);
                            foreach (var s in cached)
                            {
                                EditorGUILayout.LabelField($"  • {EOSSanctions.DescribeSanction(s)}", EditorStyles.miniLabel);
                            }
                        }
                    }
                }

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to query sanctions.", MessageType.Info);
            }
        }

        private async Task CheckSelfAsync(EOSSanctions sanctions)
        {
            bool banned = await sanctions.IsLocalPlayerBannedAsync();
            EOSDebugLogger.Log(DebugCategory.Sanctions, "EOSSanctions", $" Local player banned: {banned}");
        }
    }
#endif
}
