using System;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Reports;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Player behavior reporting system.
    /// Reports go to Developer Portal moderation queue.
    /// Works with DeviceID (no Epic Account needed).
    /// </summary>
    public class EOSReports : MonoBehaviour
    {
        #region Singleton

        private static EOSReports _instance;
        public static EOSReports Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSReports>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSReports");
                        _instance = go.AddComponent<EOSReports>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a report is sent.</summary>
        public event Action<string, PlayerReportsCategory, bool> OnReportSent; // targetPuid, category, success

        #endregion

        #region Private Fields

        private ReportsInterface _reportsInterface;
        private ProductUserId _localUserId;
        private int _reportsSentThisSession;

        #endregion

        #region Public Properties

        /// <summary>Whether reports are ready to use.</summary>
        public bool IsReady => _reportsInterface != null && _localUserId != null;

        /// <summary>Reports sent this session.</summary>
        public int ReportsSentThisSession => _reportsSentThisSession;

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

            _reportsInterface = EOSManager.Instance.ReportsInterface;
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_reportsInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.Reports, "EOSReports", "Initialized");
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Reports, "EOSReports", "ReportsInterface not available");
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
        /// Report a player for bad behavior.
        /// </summary>
        /// <param name="targetPuid">The PUID of the player to report.</param>
        /// <param name="category">The type of offense.</param>
        /// <param name="message">Optional message with details.</param>
        /// <returns>Result of the report submission.</returns>
        public async Task<Result> ReportPlayerAsync(string targetPuid, PlayerReportsCategory category, string message = null)
        {
            if (!IsReady)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Reports, "EOSReports", "Not ready");
                return Result.NotConfigured;
            }

            if (string.IsNullOrEmpty(targetPuid))
            {
                EOSDebugLogger.LogWarning(DebugCategory.Reports, "EOSReports", "Target PUID is required");
                return Result.InvalidParameters;
            }

            // Convert string to ProductUserId
            var targetUserId = ProductUserId.FromString(targetPuid);
            if (targetUserId == null || !targetUserId.IsValid())
            {
                Debug.LogWarning($"[EOSReports] Invalid target PUID: {targetPuid}");
                return Result.InvalidParameters;
            }

            return await ReportPlayerAsync(targetUserId, category, message);
        }

        /// <summary>
        /// Report a player for bad behavior.
        /// </summary>
        /// <param name="targetUserId">The ProductUserId of the player to report.</param>
        /// <param name="category">The type of offense.</param>
        /// <param name="message">Optional message with details.</param>
        /// <returns>Result of the report submission.</returns>
        public async Task<Result> ReportPlayerAsync(ProductUserId targetUserId, PlayerReportsCategory category, string message = null)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (targetUserId == null || !targetUserId.IsValid())
                return Result.InvalidParameters;

            // Can't report yourself
            if (targetUserId.ToString() == _localUserId.ToString())
            {
                EOSDebugLogger.LogWarning(DebugCategory.Reports, "EOSReports", "Cannot report yourself");
                return Result.InvalidParameters;
            }

            var options = new SendPlayerBehaviorReportOptions
            {
                ReporterUserId = _localUserId,
                ReportedUserId = targetUserId,
                Category = category,
                Message = message
            };

            var tcs = new TaskCompletionSource<SendPlayerBehaviorReportCompleteCallbackInfo>();
            _reportsInterface.SendPlayerBehaviorReport(ref options, null, (ref SendPlayerBehaviorReportCompleteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            if (result.ResultCode == Result.Success)
            {
                _reportsSentThisSession++;
                EOSDebugLogger.Log(DebugCategory.Reports, "EOSReports", $" Report sent: {category} for {targetUserId}");
                OnReportSent?.Invoke(targetUserId.ToString(), category, true);
            }
            else
            {
                Debug.LogWarning($"[EOSReports] Report failed: {result.ResultCode}");
                OnReportSent?.Invoke(targetUserId.ToString(), category, false);
            }

            return result.ResultCode;
        }

        /// <summary>
        /// Report a player for cheating/exploiting.
        /// </summary>
        public Task<Result> ReportCheatingAsync(string targetPuid, string details = null)
        {
            return ReportPlayerAsync(targetPuid, PlayerReportsCategory.Cheating, details);
        }

        /// <summary>
        /// Report a player for exploiting bugs.
        /// </summary>
        public Task<Result> ReportExploitingAsync(string targetPuid, string details = null)
        {
            return ReportPlayerAsync(targetPuid, PlayerReportsCategory.Exploiting, details);
        }

        /// <summary>
        /// Report a player for offensive behavior.
        /// </summary>
        public Task<Result> ReportOffensiveAsync(string targetPuid, string details = null)
        {
            return ReportPlayerAsync(targetPuid, PlayerReportsCategory.OffensiveProfile, details);
        }

        /// <summary>
        /// Report a player for verbal abuse.
        /// </summary>
        public Task<Result> ReportVerbalAbuseAsync(string targetPuid, string details = null)
        {
            return ReportPlayerAsync(targetPuid, PlayerReportsCategory.VerbalAbuse, details);
        }

        /// <summary>
        /// Report a player for spamming.
        /// </summary>
        public Task<Result> ReportSpammingAsync(string targetPuid, string details = null)
        {
            return ReportPlayerAsync(targetPuid, PlayerReportsCategory.Spamming, details);
        }

        /// <summary>
        /// Report a player with custom category.
        /// </summary>
        public Task<Result> ReportOtherAsync(string targetPuid, string details = null)
        {
            return ReportPlayerAsync(targetPuid, PlayerReportsCategory.Other, details);
        }

        #endregion

        #region Helper

        /// <summary>
        /// Get a user-friendly name for a report category.
        /// </summary>
        public static string GetCategoryDisplayName(PlayerReportsCategory category)
        {
            return category switch
            {
                PlayerReportsCategory.Cheating => "Cheating",
                PlayerReportsCategory.Exploiting => "Exploiting",
                PlayerReportsCategory.OffensiveProfile => "Offensive Profile",
                PlayerReportsCategory.VerbalAbuse => "Verbal Abuse",
                PlayerReportsCategory.Scamming => "Scamming",
                PlayerReportsCategory.Spamming => "Spamming",
                PlayerReportsCategory.Other => "Other",
                _ => category.ToString()
            };
        }

        /// <summary>
        /// Get all available report categories.
        /// </summary>
        public static PlayerReportsCategory[] GetAllCategories()
        {
            return new[]
            {
                PlayerReportsCategory.Cheating,
                PlayerReportsCategory.Exploiting,
                PlayerReportsCategory.OffensiveProfile,
                PlayerReportsCategory.VerbalAbuse,
                PlayerReportsCategory.Scamming,
                PlayerReportsCategory.Spamming,
                PlayerReportsCategory.Other
            };
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSReports))]
    public class EOSReportsEditor : Editor
    {
        private string _testPuid = "";
        private int _categoryIndex = 0;
        private string _testMessage = "";

        public override void OnInspectorGUI()
        {
            var reports = (EOSReports)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Player Reports", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", reports.IsReady);
                EditorGUILayout.IntField("Reports Sent (Session)", reports.ReportsSentThisSession);
            }

            if (Application.isPlaying && reports.IsReady)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Test Report", EditorStyles.boldLabel);

                _testPuid = EditorGUILayout.TextField("Target PUID", _testPuid);

                var categories = EOSReports.GetAllCategories();
                var categoryNames = new string[categories.Length];
                for (int i = 0; i < categories.Length; i++)
                    categoryNames[i] = EOSReports.GetCategoryDisplayName(categories[i]);

                _categoryIndex = EditorGUILayout.Popup("Category", _categoryIndex, categoryNames);
                _testMessage = EditorGUILayout.TextField("Message", _testMessage);

                EditorGUILayout.Space(5);

                GUI.enabled = !string.IsNullOrEmpty(_testPuid);
                if (GUILayout.Button("Send Test Report"))
                {
                    _ = reports.ReportPlayerAsync(_testPuid, categories[_categoryIndex], _testMessage);
                }
                GUI.enabled = true;

                EditorGUILayout.HelpBox("Reports are sent to Developer Portal moderation queue.", MessageType.Info);

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to test reports.", MessageType.Info);
            }
        }
    }
#endif
}
