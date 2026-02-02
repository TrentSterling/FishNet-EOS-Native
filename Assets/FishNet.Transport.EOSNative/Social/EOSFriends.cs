using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Friends;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Friends list management. Requires Epic Account login.
    /// </summary>
    public class EOSFriends : MonoBehaviour
    {
        #region Singleton

        private static EOSFriends _instance;
        public static EOSFriends Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSFriends>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSFriends");
                        _instance = go.AddComponent<EOSFriends>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        public event Action<List<FriendData>> OnFriendsListUpdated;
        public event Action<EpicAccountId> OnFriendAdded;
        public event Action<EpicAccountId> OnFriendRemoved;
        public event Action<EpicAccountId, FriendsStatus> OnFriendStatusChanged;

        #endregion

        #region Private Fields

        private FriendsInterface _friendsInterface;
        private EpicAccountId _localAccountId;
        private List<FriendData> _friends = new();
        private ulong _friendsUpdateHandle;

        #endregion

        #region Public Properties

        public bool IsReady => _friendsInterface != null && _localAccountId != null && _localAccountId.IsValid();
        public IReadOnlyList<FriendData> Friends => _friends;
        public int FriendCount => _friends.Count;

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
            // Wait for Epic Account login
            while (EOSManager.Instance == null || !EOSManager.Instance.IsEpicAccountLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _friendsInterface = EOSManager.Instance.FriendsInterface;
            _localAccountId = EOSManager.Instance.LocalEpicAccountId;

            if (_friendsInterface != null)
            {
                SubscribeToUpdates();
                EOSDebugLogger.Log(DebugCategory.Friends, "EOSFriends", "Initialized");
                _ = QueryFriendsAsync();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromUpdates();
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Query the friends list.
        /// </summary>
        public async Task<(Result result, List<FriendData> friends)> QueryFriendsAsync()
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            var options = new QueryFriendsOptions { LocalUserId = _localAccountId };

            var tcs = new TaskCompletionSource<QueryFriendsCallbackInfo>();
            _friendsInterface.QueryFriends(ref options, null, (ref QueryFriendsCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSFriends] QueryFriends failed: {result.ResultCode}");
                return (result.ResultCode, null);
            }

            // Read friends from cache
            _friends.Clear();
            var countOptions = new GetFriendsCountOptions { LocalUserId = _localAccountId };
            int count = _friendsInterface.GetFriendsCount(ref countOptions);

            for (int i = 0; i < count; i++)
            {
                var atIndexOptions = new GetFriendAtIndexOptions { LocalUserId = _localAccountId, Index = i };
                var friendId = _friendsInterface.GetFriendAtIndex(ref atIndexOptions);

                if (friendId != null && friendId.IsValid())
                {
                    var statusOptions = new GetStatusOptions { LocalUserId = _localAccountId, TargetUserId = friendId };
                    var status = _friendsInterface.GetStatus(ref statusOptions);

                    _friends.Add(new FriendData
                    {
                        AccountId = friendId,
                        Status = status
                    });
                }
            }

            EOSDebugLogger.Log(DebugCategory.Friends, "EOSFriends", $" Found {_friends.Count} friends");
            OnFriendsListUpdated?.Invoke(_friends);
            return (Result.Success, _friends);
        }

        /// <summary>
        /// Send a friend request.
        /// </summary>
        public async Task<Result> SendInviteAsync(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new SendInviteOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<SendInviteCallbackInfo>();
            _friendsInterface.SendInvite(ref options, null, (ref SendInviteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.Friends, "EOSFriends", $" Invite sent to {targetUserId}");
            }
            return result.ResultCode;
        }

        /// <summary>
        /// Accept a friend request.
        /// </summary>
        public async Task<Result> AcceptInviteAsync(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new AcceptInviteOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<AcceptInviteCallbackInfo>();
            _friendsInterface.AcceptInvite(ref options, null, (ref AcceptInviteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.Friends, "EOSFriends", $" Accepted invite from {targetUserId}");
                _ = QueryFriendsAsync(); // Refresh
            }
            return result.ResultCode;
        }

        /// <summary>
        /// Reject a friend request.
        /// </summary>
        public async Task<Result> RejectInviteAsync(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new RejectInviteOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<RejectInviteCallbackInfo>();
            _friendsInterface.RejectInvite(ref options, null, (ref RejectInviteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            return (await tcs.Task).ResultCode;
        }

        /// <summary>
        /// Get friend status.
        /// </summary>
        public FriendsStatus GetFriendStatus(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return FriendsStatus.NotFriends;

            var options = new GetStatusOptions { LocalUserId = _localAccountId, TargetUserId = targetUserId };
            return _friendsInterface.GetStatus(ref options);
        }

        #endregion

        #region Notifications

        private void SubscribeToUpdates()
        {
            var options = new AddNotifyFriendsUpdateOptions();
            _friendsUpdateHandle = _friendsInterface.AddNotifyFriendsUpdate(ref options, null, OnFriendsUpdate);
        }

        private void UnsubscribeFromUpdates()
        {
            if (_friendsUpdateHandle != 0 && _friendsInterface != null)
            {
                _friendsInterface.RemoveNotifyFriendsUpdate(_friendsUpdateHandle);
                _friendsUpdateHandle = 0;
            }
        }

        private void OnFriendsUpdate(ref OnFriendsUpdateInfo info)
        {
            EOSDebugLogger.Log(DebugCategory.Friends, "EOSFriends", $" Friend update: {info.TargetUserId} -> {info.CurrentStatus}");

            switch (info.CurrentStatus)
            {
                case FriendsStatus.Friends:
                    OnFriendAdded?.Invoke(info.TargetUserId);
                    break;
                case FriendsStatus.NotFriends:
                    OnFriendRemoved?.Invoke(info.TargetUserId);
                    break;
            }

            OnFriendStatusChanged?.Invoke(info.TargetUserId, info.CurrentStatus);
            _ = QueryFriendsAsync(); // Refresh list
        }

        #endregion
    }

    [Serializable]
    public struct FriendData
    {
        public EpicAccountId AccountId;
        public FriendsStatus Status;
        public string DisplayName; // Populated by UserInfo lookup
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSFriends))]
    public class EOSFriendsEditor : Editor
    {
        private Vector2 _scrollPos;

        public override void OnInspectorGUI()
        {
            var friends = (EOSFriends)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Friends (Epic Account)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", friends.IsReady);
                EditorGUILayout.IntField("Friend Count", friends.FriendCount);
            }

            if (!friends.IsReady)
            {
                EditorGUILayout.HelpBox("Requires Epic Account login (not DeviceID).", MessageType.Info);
            }

            if (Application.isPlaying && friends.IsReady)
            {
                EditorGUILayout.Space(5);
                if (GUILayout.Button("Refresh Friends"))
                {
                    _ = friends.QueryFriendsAsync();
                }

                if (friends.FriendCount > 0)
                {
                    _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(100));
                    foreach (var f in friends.Friends)
                    {
                        EditorGUILayout.LabelField($"{f.AccountId} - {f.Status}");
                    }
                    EditorGUILayout.EndScrollView();
                }

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
