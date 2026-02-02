using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.CustomInvites;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Cross-platform game invitations.
    /// Send invites containing lobby join codes or custom data.
    /// Works with DeviceID auth.
    /// </summary>
    public class EOSCustomInvites : MonoBehaviour
    {
        #region Singleton

        private static EOSCustomInvites _instance;
        public static EOSCustomInvites Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSCustomInvites>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSCustomInvites");
                        _instance = go.AddComponent<EOSCustomInvites>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when an invite is received.</summary>
        public event Action<InviteData> OnInviteReceived;
        /// <summary>Fired when an invite is accepted (by local user or via overlay).</summary>
        public event Action<InviteData> OnInviteAccepted;
        /// <summary>Fired when an invite is rejected.</summary>
        public event Action<InviteData> OnInviteRejected;
        /// <summary>Fired when a request to join is received (someone wants to join your game).</summary>
        public event Action<RequestToJoinData> OnRequestToJoinReceived;

        #endregion

        #region Private Fields

        private CustomInvitesInterface _customInvitesInterface;
        private ProductUserId _localUserId;
        private Dictionary<string, InviteData> _pendingInvites = new();
        private Dictionary<string, RequestToJoinData> _pendingRequests = new();
        private string _currentPayload = "";

        private ulong _inviteReceivedHandle;
        private ulong _inviteAcceptedHandle;
        private ulong _inviteRejectedHandle;
        private ulong _requestToJoinHandle;

        #endregion

        #region Public Properties

        public bool IsReady
        {
            get
            {
                if (_customInvitesInterface == null || _localUserId == null)
                    return false;
                try { return _localUserId.IsValid(); }
                catch { return false; }
            }
        }
        public string CurrentPayload => _currentPayload;
        public IReadOnlyDictionary<string, InviteData> PendingInvites => _pendingInvites;
        public IReadOnlyDictionary<string, RequestToJoinData> PendingRequests => _pendingRequests;

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

            _customInvitesInterface = EOSManager.Instance.Platform?.GetCustomInvitesInterface();
            _localUserId = EOSManager.Instance.LocalProductUserId;

            if (_customInvitesInterface != null)
            {
                SubscribeToNotifications();
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", "Initialized");
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.CustomInvites, "EOSCustomInvites", "CustomInvitesInterface not available");
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
        /// Set the payload that will be sent with invites.
        /// Usually contains lobby join code or connection info.
        /// </summary>
        public Result SetPayload(string payload)
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new SetCustomInviteOptions
            {
                LocalUserId = _localUserId,
                Payload = payload ?? ""
            };

            var result = _customInvitesInterface.SetCustomInvite(ref options);
            if (result == Result.Success)
            {
                _currentPayload = payload ?? "";
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Payload set: {(_currentPayload.Length > 0 ? _currentPayload : "(cleared)")}");
            }
            else
            {
                Debug.LogWarning($"[EOSCustomInvites] SetPayload failed: {result}");
            }
            return result;
        }

        /// <summary>
        /// Clear the current invite payload.
        /// </summary>
        public Result ClearPayload()
        {
            return SetPayload("");
        }

        /// <summary>
        /// Set the payload to the current lobby join code (if in a lobby).
        /// </summary>
        public Result SetLobbyPayload()
        {
            var transport = FindAnyObjectByType<EOSNativeTransport>();
            if (transport?.CurrentLobby == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.CustomInvites, "EOSCustomInvites", "Not in a lobby");
                return Result.InvalidState;
            }
            return SetPayload(transport.CurrentLobby.JoinCode);
        }

        /// <summary>
        /// Send an invite to another player.
        /// </summary>
        public async Task<Result> SendInviteAsync(ProductUserId recipientId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (recipientId == null || !recipientId.IsValid())
                return Result.InvalidParameters;

            var options = new SendCustomInviteOptions
            {
                LocalUserId = _localUserId,
                TargetUserIds = new ProductUserId[] { recipientId }
            };

            var tcs = new TaskCompletionSource<SendCustomInviteCallbackInfo>();
            _customInvitesInterface.SendCustomInvite(ref options, null, (ref SendCustomInviteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Invite sent to {recipientId}");
            }
            else
            {
                Debug.LogWarning($"[EOSCustomInvites] SendInvite failed: {result.ResultCode}");
            }
            return result.ResultCode;
        }

        /// <summary>
        /// Send an invite to multiple players.
        /// </summary>
        public async Task<Result> SendInviteAsync(ProductUserId[] recipientIds)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (recipientIds == null || recipientIds.Length == 0)
                return Result.InvalidParameters;

            var options = new SendCustomInviteOptions
            {
                LocalUserId = _localUserId,
                TargetUserIds = recipientIds
            };

            var tcs = new TaskCompletionSource<SendCustomInviteCallbackInfo>();
            _customInvitesInterface.SendCustomInvite(ref options, null, (ref SendCustomInviteCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Invite sent to {recipientIds.Length} players");
            }
            return result.ResultCode;
        }

        /// <summary>
        /// Send an invite by PUID string.
        /// </summary>
        public Task<Result> SendInviteAsync(string recipientPuid)
        {
            var puid = ProductUserId.FromString(recipientPuid);
            return SendInviteAsync(puid);
        }

        /// <summary>
        /// Accept a pending invite.
        /// </summary>
        public void AcceptInvite(string inviteId)
        {
            if (!_pendingInvites.TryGetValue(inviteId, out var invite))
            {
                Debug.LogWarning($"[EOSCustomInvites] Invite not found: {inviteId}");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Accepting invite: {inviteId}");
            OnInviteAccepted?.Invoke(invite);
            FinalizeInvite(invite);
        }

        /// <summary>
        /// Reject a pending invite.
        /// </summary>
        public void RejectInvite(string inviteId)
        {
            if (!_pendingInvites.TryGetValue(inviteId, out var invite))
            {
                Debug.LogWarning($"[EOSCustomInvites] Invite not found: {inviteId}");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Rejecting invite: {inviteId}");
            OnInviteRejected?.Invoke(invite);
            FinalizeInvite(invite);
        }

        /// <summary>
        /// Dismiss an invite without accepting or rejecting.
        /// </summary>
        public void DismissInvite(string inviteId)
        {
            if (!_pendingInvites.TryGetValue(inviteId, out var invite))
            {
                Debug.LogWarning($"[EOSCustomInvites] Invite not found: {inviteId}");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Dismissing invite: {inviteId}");
            FinalizeInvite(invite);
        }

        /// <summary>
        /// Send a request to join another player's game.
        /// </summary>
        public async Task<Result> SendRequestToJoinAsync(ProductUserId targetUserId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            if (targetUserId == null || !targetUserId.IsValid())
                return Result.InvalidParameters;

            var options = new SendRequestToJoinOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = targetUserId
            };

            var tcs = new TaskCompletionSource<SendRequestToJoinCallbackInfo>();
            _customInvitesInterface.SendRequestToJoin(ref options, null, (ref SendRequestToJoinCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Request to join sent to {targetUserId}");
            }
            return result.ResultCode;
        }

        /// <summary>
        /// Accept a request to join (let the player join your game).
        /// </summary>
        public async Task<Result> AcceptRequestToJoinAsync(ProductUserId fromUserId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new AcceptRequestToJoinOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = fromUserId
            };

            var tcs = new TaskCompletionSource<AcceptRequestToJoinCallbackInfo>();
            _customInvitesInterface.AcceptRequestToJoin(ref options, null, (ref AcceptRequestToJoinCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                string key = fromUserId.ToString();
                _pendingRequests.Remove(key);
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Accepted request from {fromUserId}");
            }
            return result.ResultCode;
        }

        /// <summary>
        /// Reject a request to join.
        /// </summary>
        public async Task<Result> RejectRequestToJoinAsync(ProductUserId fromUserId)
        {
            if (!IsReady)
                return Result.NotConfigured;

            var options = new RejectRequestToJoinOptions
            {
                LocalUserId = _localUserId,
                TargetUserId = fromUserId
            };

            var tcs = new TaskCompletionSource<RejectRequestToJoinCallbackInfo>();
            _customInvitesInterface.RejectRequestToJoin(ref options, null, (ref RejectRequestToJoinCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode == Result.Success)
            {
                string key = fromUserId.ToString();
                _pendingRequests.Remove(key);
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Rejected request from {fromUserId}");
            }
            return result.ResultCode;
        }

        #endregion

        #region Notifications

        private void SubscribeToNotifications()
        {
            // Invite received
            var receiveOptions = new AddNotifyCustomInviteReceivedOptions();
            _inviteReceivedHandle = _customInvitesInterface.AddNotifyCustomInviteReceived(
                ref receiveOptions, null, OnInviteReceivedCallback);

            // Invite accepted (via overlay)
            var acceptOptions = new AddNotifyCustomInviteAcceptedOptions();
            _inviteAcceptedHandle = _customInvitesInterface.AddNotifyCustomInviteAccepted(
                ref acceptOptions, null, OnInviteAcceptedCallback);

            // Invite rejected (via overlay)
            var rejectOptions = new AddNotifyCustomInviteRejectedOptions();
            _inviteRejectedHandle = _customInvitesInterface.AddNotifyCustomInviteRejected(
                ref rejectOptions, null, OnInviteRejectedCallback);

            // Request to join received
            var requestOptions = new AddNotifyRequestToJoinReceivedOptions();
            _requestToJoinHandle = _customInvitesInterface.AddNotifyRequestToJoinReceived(
                ref requestOptions, null, OnRequestToJoinReceivedCallback);
        }

        private void UnsubscribeFromNotifications()
        {
            if (_customInvitesInterface == null) return;

            try
            {
                if (_inviteReceivedHandle != 0)
                    _customInvitesInterface.RemoveNotifyCustomInviteReceived(_inviteReceivedHandle);
                if (_inviteAcceptedHandle != 0)
                    _customInvitesInterface.RemoveNotifyCustomInviteAccepted(_inviteAcceptedHandle);
                if (_inviteRejectedHandle != 0)
                    _customInvitesInterface.RemoveNotifyCustomInviteRejected(_inviteRejectedHandle);
                if (_requestToJoinHandle != 0)
                    _customInvitesInterface.RemoveNotifyRequestToJoinReceived(_requestToJoinHandle);
            }
            catch { /* SDK may already be shut down */ }
        }

        private void OnInviteReceivedCallback(ref OnCustomInviteReceivedCallbackInfo data)
        {
            string id = data.CustomInviteId;
            if (_pendingInvites.ContainsKey(id))
            {
                EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Invite already pending: {id}");
                return;
            }

            var invite = new InviteData
            {
                InviteId = id,
                Payload = data.Payload,
                SenderId = data.TargetUserId,
                RecipientId = data.LocalUserId,
                ReceivedTime = DateTime.Now
            };

            _pendingInvites[id] = invite;
            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Invite received from {data.TargetUserId}: {data.Payload}");
            OnInviteReceived?.Invoke(invite);
        }

        private void OnInviteAcceptedCallback(ref OnCustomInviteAcceptedCallbackInfo data)
        {
            string id = data.CustomInviteId;
            if (!_pendingInvites.TryGetValue(id, out var invite))
            {
                Debug.LogWarning($"[EOSCustomInvites] Accepted invite not found: {id}");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Invite accepted (overlay): {id}");
            OnInviteAccepted?.Invoke(invite);
            FinalizeInvite(invite);
        }

        private void OnInviteRejectedCallback(ref CustomInviteRejectedCallbackInfo data)
        {
            string id = data.CustomInviteId;
            if (!_pendingInvites.TryGetValue(id, out var invite))
            {
                Debug.LogWarning($"[EOSCustomInvites] Rejected invite not found: {id}");
                return;
            }

            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Invite rejected (overlay): {id}");
            OnInviteRejected?.Invoke(invite);
            FinalizeInvite(invite);
        }

        private void OnRequestToJoinReceivedCallback(ref RequestToJoinReceivedCallbackInfo data)
        {
            string key = data.FromUserId.ToString();
            var request = new RequestToJoinData
            {
                FromUserId = data.FromUserId,
                ToUserId = data.ToUserId,
                ReceivedTime = DateTime.Now
            };

            _pendingRequests[key] = request;
            EOSDebugLogger.Log(DebugCategory.CustomInvites, "EOSCustomInvites", $" Request to join from {data.FromUserId}");
            OnRequestToJoinReceived?.Invoke(request);
        }

        private void FinalizeInvite(InviteData invite)
        {
            var options = new FinalizeInviteOptions
            {
                CustomInviteId = invite.InviteId,
                TargetUserId = invite.SenderId,
                LocalUserId = invite.RecipientId,
                ProcessingResult = Result.Success
            };

            var result = _customInvitesInterface.FinalizeInvite(ref options);
            if (result != Result.Success)
            {
                Debug.LogWarning($"[EOSCustomInvites] FinalizeInvite failed: {result}");
            }

            _pendingInvites.Remove(invite.InviteId);
        }

        #endregion
    }

    /// <summary>
    /// Data for a received invite.
    /// </summary>
    [Serializable]
    public struct InviteData
    {
        public string InviteId;
        public string Payload;
        public ProductUserId SenderId;
        public ProductUserId RecipientId;
        public DateTime ReceivedTime;

        /// <summary>
        /// Try to parse payload as a lobby join code.
        /// </summary>
        public bool TryGetLobbyCode(out string code)
        {
            // Assume 4-digit codes for now
            if (!string.IsNullOrEmpty(Payload) && Payload.Length == 4)
            {
                code = Payload;
                return true;
            }
            code = null;
            return false;
        }
    }

    /// <summary>
    /// Data for a request to join.
    /// </summary>
    [Serializable]
    public struct RequestToJoinData
    {
        public ProductUserId FromUserId;
        public ProductUserId ToUserId;
        public DateTime ReceivedTime;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSCustomInvites))]
    public class EOSCustomInvitesEditor : Editor
    {
        private string _testPayload = "";
        private string _testRecipient = "";

        public override void OnInspectorGUI()
        {
            var invites = (EOSCustomInvites)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Custom Invites", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Ready", invites.IsReady);
                EditorGUILayout.TextField("Current Payload", invites.CurrentPayload);
                EditorGUILayout.IntField("Pending Invites", invites.PendingInvites.Count);
                EditorGUILayout.IntField("Pending Requests", invites.PendingRequests.Count);
            }

            if (Application.isPlaying && invites.IsReady)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Set Payload", EditorStyles.boldLabel);

                _testPayload = EditorGUILayout.TextField("Payload", _testPayload);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Set Payload"))
                {
                    invites.SetPayload(_testPayload);
                }
                if (GUILayout.Button("Set Lobby Code"))
                {
                    invites.SetLobbyPayload();
                }
                if (GUILayout.Button("Clear"))
                {
                    invites.ClearPayload();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Send Invite", EditorStyles.boldLabel);

                _testRecipient = EditorGUILayout.TextField("Recipient PUID", _testRecipient);
                GUI.enabled = !string.IsNullOrEmpty(_testRecipient);
                if (GUILayout.Button("Send Invite"))
                {
                    _ = invites.SendInviteAsync(_testRecipient);
                }
                GUI.enabled = true;

                // Show pending invites
                if (invites.PendingInvites.Count > 0)
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.LabelField("Pending Invites", EditorStyles.boldLabel);
                    foreach (var kvp in invites.PendingInvites)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{kvp.Value.Payload} from {kvp.Value.SenderId}", EditorStyles.miniLabel);
                        if (GUILayout.Button("Accept", GUILayout.Width(60)))
                        {
                            invites.AcceptInvite(kvp.Key);
                        }
                        if (GUILayout.Button("Reject", GUILayout.Width(60)))
                        {
                            invites.RejectInvite(kvp.Key);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                // Show pending requests
                if (invites.PendingRequests.Count > 0)
                {
                    EditorGUILayout.Space(10);
                    EditorGUILayout.LabelField("Pending Requests to Join", EditorStyles.boldLabel);
                    foreach (var kvp in invites.PendingRequests)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"From {kvp.Value.FromUserId}", EditorStyles.miniLabel);
                        if (GUILayout.Button("Accept", GUILayout.Width(60)))
                        {
                            _ = invites.AcceptRequestToJoinAsync(kvp.Value.FromUserId);
                        }
                        if (GUILayout.Button("Reject", GUILayout.Width(60)))
                        {
                            _ = invites.RejectRequestToJoinAsync(kvp.Value.FromUserId);
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use custom invites.", MessageType.Info);
            }
        }
    }
#endif
}
