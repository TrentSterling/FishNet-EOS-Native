using System;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Presence;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Online presence/status management. Requires Epic Account login.
    /// </summary>
    public class EOSPresence : MonoBehaviour
    {
        #region Singleton

        private static EOSPresence _instance;
        public static EOSPresence Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSPresence>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSPresence");
                        _instance = go.AddComponent<EOSPresence>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        public event Action<EpicAccountId, Status> OnPresenceChanged;

        #endregion

        #region Private Fields

        private PresenceInterface _presenceInterface;
        private EpicAccountId _localAccountId;
        private ulong _presenceChangedHandle;
        private Status _currentStatus = Status.Offline;
        private string _currentRichText = "";

        #endregion

        #region Public Properties

        public bool IsReady => _presenceInterface != null && _localAccountId != null && _localAccountId.IsValid();
        public Status CurrentStatus => _currentStatus;
        public string CurrentRichText => _currentRichText;

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

            _presenceInterface = EOSManager.Instance.PresenceInterface;
            _localAccountId = EOSManager.Instance.LocalEpicAccountId;

            if (_presenceInterface != null)
            {
                SubscribeToUpdates();
                EOSDebugLogger.Log(DebugCategory.Presence, "EOSPresence", "Initialized");

                // Set online by default
                _ = SetPresenceAsync(Status.Online, "Playing");
            }
        }

        private void OnDestroy()
        {
            // Set offline before destroying
            if (IsReady)
            {
                _ = SetPresenceAsync(Status.Offline);
            }
            UnsubscribeFromUpdates();

            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!IsReady) return;

            if (paused)
            {
                _ = SetPresenceAsync(Status.Away);
            }
            else
            {
                _ = SetPresenceAsync(Status.Online, _currentRichText);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set your presence status.
        /// </summary>
        public async Task<Result> SetPresenceAsync(Status status, string richText = null)
        {
            if (!IsReady)
                return Result.NotConfigured;

            // Create modification
            var createOptions = new CreatePresenceModificationOptions { LocalUserId = _localAccountId };
            var createResult = _presenceInterface.CreatePresenceModification(ref createOptions, out var modification);
            if (createResult != Result.Success)
            {
                Debug.LogWarning($"[EOSPresence] CreatePresenceModification failed: {createResult}");
                return createResult;
            }

            // Set status
            var statusOptions = new PresenceModificationSetStatusOptions { Status = status };
            modification.SetStatus(ref statusOptions);

            // Set rich text if provided
            if (!string.IsNullOrEmpty(richText))
            {
                var richTextOptions = new PresenceModificationSetRawRichTextOptions { RichText = richText };
                modification.SetRawRichText(ref richTextOptions);
            }

            // Apply
            var setOptions = new SetPresenceOptions
            {
                LocalUserId = _localAccountId,
                PresenceModificationHandle = modification
            };

            var tcs = new TaskCompletionSource<SetPresenceCallbackInfo>();
            _presenceInterface.SetPresence(ref setOptions, null, (ref SetPresenceCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            modification.Release();

            if (result.ResultCode == Result.Success)
            {
                _currentStatus = status;
                _currentRichText = richText ?? "";
                EOSDebugLogger.Log(DebugCategory.Presence, "EOSPresence", $" Status set: {status} - {richText}");
            }

            return result.ResultCode;
        }

        /// <summary>
        /// Set status to Online with rich text.
        /// </summary>
        public Task<Result> SetOnlineAsync(string activity = "Online")
        {
            return SetPresenceAsync(Status.Online, activity);
        }

        /// <summary>
        /// Set status to Away.
        /// </summary>
        public Task<Result> SetAwayAsync()
        {
            return SetPresenceAsync(Status.Away, "Away");
        }

        /// <summary>
        /// Set status to Do Not Disturb.
        /// </summary>
        public Task<Result> SetDoNotDisturbAsync()
        {
            return SetPresenceAsync(Status.DoNotDisturb, "Do Not Disturb");
        }

        /// <summary>
        /// Query another user's presence.
        /// </summary>
        public async Task<(Result result, PresenceData? presence)> QueryPresenceAsync(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            var options = new QueryPresenceOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<QueryPresenceCallbackInfo>();
            _presenceInterface.QueryPresence(ref options, null, (ref QueryPresenceCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                return (result.ResultCode, null);
            }

            // Copy presence data
            return CopyPresence(targetUserId);
        }

        /// <summary>
        /// Get cached presence for a user (call QueryPresenceAsync first).
        /// </summary>
        public (Result result, PresenceData? presence) CopyPresence(EpicAccountId targetUserId)
        {
            if (!IsReady)
                return (Result.NotConfigured, null);

            var options = new CopyPresenceOptions
            {
                LocalUserId = _localAccountId,
                TargetUserId = targetUserId
            };

            var copyResult = _presenceInterface.CopyPresence(ref options, out var info);
            if (copyResult != Result.Success || !info.HasValue)
            {
                return (copyResult, null);
            }

            var presence = new PresenceData
            {
                UserId = targetUserId,
                Status = info.Value.Status,
                RichText = info.Value.RichText,
                ProductId = info.Value.ProductId,
                Platform = info.Value.Platform
            };

            return (Result.Success, presence);
        }

        #endregion

        #region Notifications

        private void SubscribeToUpdates()
        {
            var options = new AddNotifyOnPresenceChangedOptions();
            _presenceChangedHandle = _presenceInterface.AddNotifyOnPresenceChanged(ref options, null, OnPresenceChangedCallback);
        }

        private void UnsubscribeFromUpdates()
        {
            if (_presenceChangedHandle != 0 && _presenceInterface != null)
            {
                _presenceInterface.RemoveNotifyOnPresenceChanged(_presenceChangedHandle);
                _presenceChangedHandle = 0;
            }
        }

        private void OnPresenceChangedCallback(ref PresenceChangedCallbackInfo info)
        {
            var (result, presence) = CopyPresence(info.PresenceUserId);
            if (result == Result.Success && presence.HasValue)
            {
                EOSDebugLogger.Log(DebugCategory.Presence, "EOSPresence", $" {info.PresenceUserId} is now {presence.Value.Status}");
                OnPresenceChanged?.Invoke(info.PresenceUserId, presence.Value.Status);
            }
        }

        #endregion
    }

    [Serializable]
    public struct PresenceData
    {
        public EpicAccountId UserId;
        public Status Status;
        public string RichText;
        public string ProductId;
        public string Platform;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSPresence))]
    public class EOSPresenceEditor : Editor
    {
        private string _testRichText = "Playing Game";

        public override void OnInspectorGUI()
        {
            var presence = (EOSPresence)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Presence (Epic Account)", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", presence.IsReady);
                EditorGUILayout.EnumPopup("Current Status", presence.CurrentStatus);
                EditorGUILayout.TextField("Rich Text", presence.CurrentRichText);
            }

            if (!presence.IsReady)
            {
                EditorGUILayout.HelpBox("Requires Epic Account login (not DeviceID).", MessageType.Info);
            }

            if (Application.isPlaying && presence.IsReady)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Set Presence", EditorStyles.boldLabel);

                _testRichText = EditorGUILayout.TextField("Activity", _testRichText);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Online"))
                    _ = presence.SetOnlineAsync(_testRichText);
                if (GUILayout.Button("Away"))
                    _ = presence.SetAwayAsync();
                if (GUILayout.Button("DND"))
                    _ = presence.SetDoNotDisturbAsync();
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
