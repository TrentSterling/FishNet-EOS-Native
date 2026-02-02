using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.UserInfo;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// User profile information. Requires Epic Account login for full data.
    /// </summary>
    public class EOSUserInfo : MonoBehaviour
    {
        #region Singleton

        private static EOSUserInfo _instance;
        public static EOSUserInfo Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSUserInfo>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSUserInfo");
                        _instance = go.AddComponent<EOSUserInfo>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        public event Action<UserData> OnUserInfoReceived;

        #endregion

        #region Private Fields

        private UserInfoInterface _userInfoInterface;
        private EpicAccountId _localAccountId;
        private Dictionary<string, UserData> _userCache = new();

        #endregion

        #region Public Properties

        public bool IsReady => _userInfoInterface != null && _localAccountId != null && _localAccountId.IsValid();
        public UserData? LocalUser { get; private set; }

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
            while (EOSManager.Instance == null || !EOSManager.Instance.IsEpicAccountLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _userInfoInterface = EOSManager.Instance.UserInfoInterface;
            _localAccountId = EOSManager.Instance.LocalEpicAccountId;

            if (_userInfoInterface != null)
            {
                EOSDebugLogger.Log(DebugCategory.UserInfo, "EOSUserInfo", "Initialized");
                // Query local user info
                _ = QueryUserInfoAsync(_localAccountId);
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
        /// Query user info for an Epic Account.
        /// </summary>
        public async Task<(Result result, UserData? user)> QueryUserInfoAsync(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (targetUserId == null || !targetUserId.IsValid())
                return (Result.InvalidParameters, null);

            var options = new QueryUserInfoOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<QueryUserInfoCallbackInfo>();
            _userInfoInterface.QueryUserInfo(ref options, null, (ref QueryUserInfoCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSUserInfo] QueryUserInfo failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Copy user info
            return CopyUserInfo(targetUserId);
        }

        /// <summary>
        /// Query user info by display name.
        /// </summary>
        public async Task<(Result result, UserData? user)> QueryUserInfoByDisplayNameAsync(string displayName)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            if (string.IsNullOrEmpty(displayName))
                return (Result.InvalidParameters, null);

            var options = new QueryUserInfoByDisplayNameOptions
            {
                LocalUserId = _localAccountId,
                DisplayName = displayName
            };

            var tcs = new TaskCompletionSource<QueryUserInfoByDisplayNameCallbackInfo>();
            _userInfoInterface.QueryUserInfoByDisplayName(ref options, null, (ref QueryUserInfoByDisplayNameCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                return (result.ResultCode, null);
            }

            return CopyUserInfo(result.TargetUserId);
        }

        /// <summary>
        /// Get cached user info (call QueryUserInfoAsync first).
        /// </summary>
        public (Result result, UserData? user) CopyUserInfo(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            var options = new CopyUserInfoOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var copyResult = _userInfoInterface.CopyUserInfo(ref options, out var info);
            if (copyResult != Result.Success || !info.HasValue)
            {
                return (copyResult, null);
            }

            var userData = new UserData
            {
                AccountId = targetUserId,
                DisplayName = info.Value.DisplayName,
                DisplayNameSanitized = info.Value.DisplayNameSanitized,
                Nickname = info.Value.Nickname,
                Country = info.Value.Country,
                PreferredLanguage = info.Value.PreferredLanguage
            };

            // Cache it
            string key = targetUserId.ToString();
            _userCache[key] = userData;

            // Update local user
            if (targetUserId == _localAccountId)
            {
                LocalUser = userData;
            }

            EOSDebugLogger.Log(DebugCategory.UserInfo, "EOSUserInfo", $" Got info for {userData.DisplayName}");
            OnUserInfoReceived?.Invoke(userData);
            return (Result.Success, userData);
        }

        /// <summary>
        /// Get user info from cache.
        /// </summary>
        public UserData? GetCachedUser(EpicAccountId userId)
        {
            if (userId == null) return null;
            string key = userId.ToString();
            return _userCache.TryGetValue(key, out var data) ? data : null;
        }

        /// <summary>
        /// Get display name from cache, or return placeholder.
        /// </summary>
        public string GetDisplayName(EpicAccountId userId)
        {
            var cached = GetCachedUser(userId);
            return cached?.DisplayName ?? userId?.ToString()?.Substring(0, 8) ?? "Unknown";
        }

        #endregion
    }

    [Serializable]
    public struct UserData
    {
        public EpicAccountId AccountId;
        public string DisplayName;
        public string DisplayNameSanitized;
        public string Nickname;
        public string Country;
        public string PreferredLanguage;

        public string BestDisplayName => !string.IsNullOrEmpty(Nickname) ? Nickname :
                                          !string.IsNullOrEmpty(DisplayName) ? DisplayName :
                                          "Unknown";
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSUserInfo))]
    public class EOSUserInfoEditor : Editor
    {
        private string _testDisplayName = "";

        public override void OnInspectorGUI()
        {
            var userInfo = (EOSUserInfo)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("User Info (Epic Account)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", userInfo.IsReady);

                if (userInfo.LocalUser.HasValue)
                {
                    EditorGUILayout.TextField("Display Name", userInfo.LocalUser.Value.DisplayName ?? "(none)");
                    EditorGUILayout.TextField("Nickname", userInfo.LocalUser.Value.Nickname ?? "(none)");
                    EditorGUILayout.TextField("Country", userInfo.LocalUser.Value.Country ?? "(none)");
                }
            }

            if (!userInfo.IsReady)
            {
                EditorGUILayout.HelpBox("Requires Epic Account login (not DeviceID).", MessageType.Info);
            }

            if (Application.isPlaying && userInfo.IsReady)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Search by Name", EditorStyles.boldLabel);

                _testDisplayName = EditorGUILayout.TextField("Display Name", _testDisplayName);
                GUI.enabled = !string.IsNullOrEmpty(_testDisplayName);
                if (GUILayout.Button("Search"))
                {
                    _ = userInfo.QueryUserInfoByDisplayNameAsync(_testDisplayName);
                }
                GUI.enabled = true;

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
