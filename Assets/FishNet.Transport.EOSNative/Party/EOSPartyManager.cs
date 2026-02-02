using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Social;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Party
{
    /// <summary>
    /// Manages persistent party lobbies that follow players across game sessions.
    /// Parties are separate EOS lobbies that coordinate group movement.
    /// </summary>
    public class EOSPartyManager : MonoBehaviour
    {
        #region Singleton

        private static EOSPartyManager _instance;
        public static EOSPartyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSPartyManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSPartyManager");
                        _instance = go.AddComponent<EOSPartyManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Configuration

        [Header("Party Settings")]
        [Tooltip("Default maximum party size.")]
        [SerializeField] private int _defaultMaxSize = 4;

        [Tooltip("Maximum allowed party size (EOS limit is 64).")]
        [SerializeField] private int _maxAllowedSize = 64;

        [Tooltip("How party members follow the leader to games.")]
        [SerializeField] private PartyFollowMode _followMode = PartyFollowMode.Automatic;

        [Tooltip("Seconds to wait for AFK members before proceeding.")]
        [SerializeField] private float _afkTimeout = 10f;

        [Tooltip("What happens when leader joins a full lobby.")]
        [SerializeField] private PartyFullLobbyBehavior _fullLobbyBehavior = PartyFullLobbyBehavior.WarnAndAsk;

        [Tooltip("How party persists across sessions.")]
        [SerializeField] private PartyPersistence _persistence = PartyPersistence.SessionBased;

        [Tooltip("Whether party voice is separate from game voice.")]
        [SerializeField] private bool _separatePartyVoice = true;

        [Tooltip("Auto-promote next member when leader leaves.")]
        [SerializeField] private bool _autoPromoteOnLeaderLeave = true;

        [Tooltip("Dissolve party when last member leaves.")]
        [SerializeField] private bool _dissolveWhenEmpty = true;

        [Tooltip("Allow party to be joined via code (public).")]
        [SerializeField] private bool _allowPublicJoin = false;

        [Tooltip("Only allow friends to join party.")]
        [SerializeField] private bool _friendsOnly = true;

        [Tooltip("Seconds between checking for leader game changes.")]
        [SerializeField] private float _pollInterval = 1f;

        #endregion

        #region Events

        /// <summary>Fired when a party is created.</summary>
        public event Action<PartyData> OnPartyCreated;

        /// <summary>Fired when joining a party.</summary>
        public event Action<PartyData> OnPartyJoined;

        /// <summary>Fired when leaving a party.</summary>
        public event Action OnPartyLeft;

        /// <summary>Fired when party is dissolved.</summary>
        public event Action OnPartyDissolved;

        /// <summary>Fired when a member joins the party.</summary>
        public event Action<PartyMember> OnMemberJoined;

        /// <summary>Fired when a member leaves the party.</summary>
        public event Action<string> OnMemberLeft; // PUID

        /// <summary>Fired when party leadership changes.</summary>
        public event Action<string, string> OnLeaderChanged; // oldLeaderPuid, newLeaderPuid

        /// <summary>Fired when leader joins a game (for non-automatic modes).</summary>
        public event Action<string> OnLeaderJoinedGame; // gameCode

        /// <summary>Fired to request follow confirmation (for Confirm mode).</summary>
        public event Action<FollowRequest> OnFollowRequested;

        /// <summary>Fired when follow attempt succeeds.</summary>
        public event Action<string> OnFollowSucceeded; // gameCode

        /// <summary>Fired when follow attempt fails.</summary>
        public event Action<string, string> OnFollowFailed; // gameCode, reason

        /// <summary>Fired when ready check is initiated.</summary>
        public event Action<ReadyCheckData> OnReadyCheckStarted;

        /// <summary>Fired when a member responds to ready check.</summary>
        public event Action<string, bool> OnReadyCheckResponse; // puid, isReady

        /// <summary>Fired when ready check completes.</summary>
        public event Action<bool> OnReadyCheckCompleted; // allReady

        /// <summary>Fired when party settings change.</summary>
        public event Action<PartySettings> OnSettingsChanged;

        /// <summary>Fired when party chat message received.</summary>
        public event Action<string, string, string> OnPartyChatReceived; // puid, name, message

        #endregion

        #region Public Properties

        /// <summary>Whether currently in a party.</summary>
        public bool IsInParty => _currentParty.IsValid;

        /// <summary>Whether we are the party leader.</summary>
        public bool IsLeader => IsInParty && _currentParty.LeaderPuid == LocalPuid;

        /// <summary>Current party data.</summary>
        public PartyData CurrentParty => _currentParty;

        /// <summary>Current party settings.</summary>
        public PartySettings Settings => _settings;

        /// <summary>All party members.</summary>
        public IReadOnlyList<PartyMember> Members => _members;

        /// <summary>Number of party members.</summary>
        public int MemberCount => _members.Count;

        /// <summary>Party code for invites.</summary>
        public string PartyCode => _currentParty.PartyCode;

        /// <summary>Whether a ready check is in progress.</summary>
        public bool IsReadyCheckActive => _readyCheckActive;

        /// <summary>Whether currently following leader to a game.</summary>
        public bool IsFollowing => _isFollowing;

        // Configuration properties (read/write)
        public PartyFollowMode FollowMode { get => _followMode; set => _followMode = value; }
        public float AfkTimeout { get => _afkTimeout; set => _afkTimeout = value; }
        public PartyFullLobbyBehavior FullLobbyBehavior { get => _fullLobbyBehavior; set => _fullLobbyBehavior = value; }
        public PartyPersistence Persistence { get => _persistence; set => _persistence = value; }
        public bool SeparatePartyVoice { get => _separatePartyVoice; set => _separatePartyVoice = value; }
        public bool AutoPromoteOnLeaderLeave { get => _autoPromoteOnLeaderLeave; set => _autoPromoteOnLeaderLeave = value; }
        public bool AllowPublicJoin { get => _allowPublicJoin; set => _allowPublicJoin = value; }
        public bool FriendsOnly { get => _friendsOnly; set => _friendsOnly = value; }

        #endregion

        #region Private Fields

        private PartyData _currentParty;
        private PartySettings _settings;
        private List<PartyMember> _members = new();
        private LobbyInterface _lobbyInterface;
        private string _partyLobbyId;

        // State
        private bool _isFollowing;
        private bool _readyCheckActive;
        private Dictionary<string, bool> _readyCheckResponses = new();
        private float _readyCheckStartTime;
        private string _pendingGameCode;

        // Notifications
        private ulong _lobbyUpdateHandle;
        private ulong _memberUpdateHandle;
        private ulong _memberStatusHandle;

        // Polling
        private float _lastPollTime;
        private string _lastKnownGameCode;

        private string LocalPuid => EOSManager.Instance?.LocalProductUserId?.ToString();

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
            _lobbyInterface = EOSManager.Instance?.LobbyInterface;
            InitializeSettings();
            SubscribeToNotifications();
        }

        private void Update()
        {
            if (!IsInParty) return;

            // Poll for leader game changes (for non-leaders)
            if (!IsLeader && Time.time - _lastPollTime > _pollInterval)
            {
                _lastPollTime = Time.time;
                CheckLeaderGameStatus();
            }

            // Ready check timeout
            if (_readyCheckActive && Time.time - _readyCheckStartTime > _afkTimeout)
            {
                CompleteReadyCheck();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromNotifications();
            if (IsInParty)
            {
                _ = LeavePartyAsync();
            }
            if (_instance == this)
                _instance = null;
        }

        private void OnApplicationQuit()
        {
            if (IsInParty && _persistence == PartyPersistence.SessionBased)
            {
                _ = LeavePartyAsync();
            }
        }

        #endregion

        #region Public API - Party Management

        /// <summary>
        /// Create a new party. You become the leader.
        /// </summary>
        public async Task<(Result result, PartyData party)> CreatePartyAsync(string partyName = null, int? maxSize = null)
        {
            if (IsInParty)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", "Already in a party");
                return (Result.AlreadyPending, default);
            }

            int size = Mathf.Clamp(maxSize ?? _defaultMaxSize, 2, _maxAllowedSize);
            string name = partyName ?? $"{GetLocalDisplayName()}'s Party";

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Creating party: {name} (max {size})");

            // Create party lobby
            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                MaxLobbyMembers = (uint)size,
                PermissionLevel = _allowPublicJoin ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Joinviapresence,
                PresenceEnabled = true,
                AllowInvites = true,
                BucketId = "party",
                DisableHostMigration = false,
                EnableRTCRoom = _separatePartyVoice
            };

            var tcs = new TaskCompletionSource<CreateLobbyCallbackInfo>();
            _lobbyInterface.CreateLobby(ref createOptions, null, (ref CreateLobbyCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            if (result.ResultCode != Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Failed to create party: {result.ResultCode}");
                return (result.ResultCode, default);
            }

            _partyLobbyId = result.LobbyId;

            // Set party attributes
            await SetPartyAttributesAsync(name, size);

            // Build party data
            _currentParty = new PartyData
            {
                PartyId = _partyLobbyId,
                PartyCode = GeneratePartyCode(),
                PartyName = name,
                LeaderPuid = LocalPuid,
                MaxSize = size,
                IsValid = true
            };

            // Add self as member
            _members.Clear();
            _members.Add(new PartyMember
            {
                Puid = LocalPuid,
                DisplayName = GetLocalDisplayName(),
                IsLeader = true,
                IsReady = true,
                Status = PartyMemberStatus.Active,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Set party code attribute
            await SetLobbyAttributeAsync(PartyAttributes.PARTY_CODE, _currentParty.PartyCode);

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Party created: {_currentParty.PartyCode}");
            OnPartyCreated?.Invoke(_currentParty);

            return (Result.Success, _currentParty);
        }

        /// <summary>
        /// Join an existing party by code.
        /// </summary>
        public async Task<(Result result, PartyData party)> JoinPartyAsync(string partyCode)
        {
            if (IsInParty)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", "Already in a party");
                return (Result.AlreadyPending, default);
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Joining party: {partyCode}");

            // Search for party lobby by code
            var searchOptions = new CreateLobbySearchOptions
            {
                MaxResults = 1
            };

            var createResult = _lobbyInterface.CreateLobbySearch(ref searchOptions, out var search);
            if (createResult != Result.Success)
            {
                return (createResult, default);
            }

            // Search by party code attribute
            var paramOptions = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData
                {
                    Key = PartyAttributes.PARTY_CODE,
                    Value = new AttributeDataValue { AsUtf8 = partyCode }
                },
                ComparisonOp = ComparisonOp.Equal
            };
            search.SetParameter(ref paramOptions);

            var findTcs = new TaskCompletionSource<LobbySearchFindCallbackInfo>();
            var findOptions = new LobbySearchFindOptions { LocalUserId = EOSManager.Instance.LocalProductUserId };
            search.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo info) =>
            {
                findTcs.SetResult(info);
            });

            var findResult = await findTcs.Task;
            if (findResult.ResultCode != Result.Success)
            {
                search.Release();
                return (findResult.ResultCode, default);
            }

            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = search.GetSearchResultCount(ref countOptions);
            if (count == 0)
            {
                search.Release();
                return (Result.NotFound, default);
            }

            // Get the lobby details
            var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            var copyResult = search.CopySearchResultByIndex(ref copyOptions, out var details);
            search.Release();

            if (copyResult != Result.Success)
            {
                return (copyResult, default);
            }

            // Get lobby ID
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            details.CopyInfo(ref infoOptions, out var lobbyInfo);
            string lobbyId = lobbyInfo?.LobbyId;
            details.Release();

            if (string.IsNullOrEmpty(lobbyId))
            {
                return (Result.NotFound, default);
            }

            // Join the party lobby
            var joinOptions = new JoinLobbyOptions
            {
                LobbyDetailsHandle = details,
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                PresenceEnabled = true
            };

            var joinTcs = new TaskCompletionSource<JoinLobbyCallbackInfo>();
            _lobbyInterface.JoinLobby(ref joinOptions, null, (ref JoinLobbyCallbackInfo info) =>
            {
                joinTcs.SetResult(info);
            });

            var joinResult = await joinTcs.Task;
            if (joinResult.ResultCode != Result.Success)
            {
                return (joinResult.ResultCode, default);
            }

            _partyLobbyId = lobbyId;

            // Read party data from lobby
            RefreshPartyData();

            // Add self as member
            await SetMemberAttributeAsync(PartyMemberAttributes.STATUS, "active");
            await SetMemberAttributeAsync(PartyMemberAttributes.DISPLAY_NAME, GetLocalDisplayName());

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Joined party: {_currentParty.PartyName}");
            OnPartyJoined?.Invoke(_currentParty);

            return (Result.Success, _currentParty);
        }

        /// <summary>
        /// Leave the current party.
        /// </summary>
        public async Task<Result> LeavePartyAsync()
        {
            if (!IsInParty) return Result.NotFound;

            bool wasLeader = IsLeader;
            string partyId = _partyLobbyId;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", "Leaving party");

            // If leader and auto-promote enabled, promote before leaving
            if (wasLeader && _autoPromoteOnLeaderLeave && _members.Count > 1)
            {
                var nextLeader = _members.Find(m => m.Puid != LocalPuid);
                if (nextLeader.Puid != null)
                {
                    await PromoteToLeaderAsync(nextLeader.Puid);
                }
            }

            var leaveOptions = new LeaveLobbyOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = partyId
            };

            var tcs = new TaskCompletionSource<LeaveLobbyCallbackInfo>();
            _lobbyInterface.LeaveLobby(ref leaveOptions, null, (ref LeaveLobbyCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            // Clear state
            _currentParty = default;
            _members.Clear();
            _partyLobbyId = null;
            _isFollowing = false;
            _readyCheckActive = false;

            OnPartyLeft?.Invoke();

            return result.ResultCode;
        }

        /// <summary>
        /// Dissolve the party (leader only).
        /// </summary>
        public async Task<Result> DissolvePartyAsync()
        {
            if (!IsInParty) return Result.NotFound;
            if (!IsLeader) return Result.InvalidUser;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", "Dissolving party");

            var destroyOptions = new DestroyLobbyOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = _partyLobbyId
            };

            var tcs = new TaskCompletionSource<DestroyLobbyCallbackInfo>();
            _lobbyInterface.DestroyLobby(ref destroyOptions, null, (ref DestroyLobbyCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;

            _currentParty = default;
            _members.Clear();
            _partyLobbyId = null;

            OnPartyDissolved?.Invoke();

            return result.ResultCode;
        }

        #endregion

        #region Public API - Invites

        /// <summary>
        /// Invite a player to the party.
        /// </summary>
        public async Task<Result> InviteToPartyAsync(string targetPuid)
        {
            if (!IsInParty) return Result.NotFound;

            var invites = EOSCustomInvites.Instance;
            if (invites == null || !invites.IsReady)
            {
                return Result.NotConfigured;
            }

            // Set party code as payload before sending
            string payload = $"PARTY:{_currentParty.PartyCode}";
            invites.SetPayload(payload);

            // Send invite
            var result = await invites.SendInviteAsync(targetPuid);

            if (result == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Sent party invite to {targetPuid}");
            }

            return result;
        }

        /// <summary>
        /// Invite a friend by name (looks up PUID).
        /// </summary>
        public async Task<Result> InviteFriendAsync(string friendName)
        {
            var registry = EOSPlayerRegistry.Instance;
            if (registry == null) return Result.NotConfigured;

            var friends = registry.GetFriends();
            var friend = friends.Find(f => f.name.Equals(friendName, StringComparison.OrdinalIgnoreCase));

            if (friend.puid == null)
            {
                return Result.NotFound;
            }

            return await InviteToPartyAsync(friend.puid);
        }

        #endregion

        #region Public API - Leadership

        /// <summary>
        /// Promote a member to party leader.
        /// </summary>
        public async Task<Result> PromoteToLeaderAsync(string memberPuid)
        {
            if (!IsInParty) return Result.NotFound;
            if (!IsLeader) return Result.InvalidUser;
            if (memberPuid == LocalPuid) return Result.InvalidParameters;

            var member = _members.Find(m => m.Puid == memberPuid);
            if (member.Puid == null) return Result.NotFound;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Promoting {member.DisplayName} to leader");

            var result = await SetLobbyAttributeAsync(PartyAttributes.LEADER_PUID, memberPuid);
            if (result == Result.Success)
            {
                string oldLeader = _currentParty.LeaderPuid;
                _currentParty.LeaderPuid = memberPuid;

                // Update member states
                for (int i = 0; i < _members.Count; i++)
                {
                    var m = _members[i];
                    m.IsLeader = m.Puid == memberPuid;
                    _members[i] = m;
                }

                OnLeaderChanged?.Invoke(oldLeader, memberPuid);
            }

            return result;
        }

        /// <summary>
        /// Kick a member from the party (leader only).
        /// </summary>
        public async Task<Result> KickMemberAsync(string memberPuid)
        {
            if (!IsInParty) return Result.NotFound;
            if (!IsLeader) return Result.InvalidUser;
            if (memberPuid == LocalPuid) return Result.InvalidParameters;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Kicking member: {memberPuid}");

            var kickOptions = new KickMemberOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = _partyLobbyId,
                TargetUserId = ProductUserId.FromString(memberPuid)
            };

            var tcs = new TaskCompletionSource<KickMemberCallbackInfo>();
            _lobbyInterface.KickMember(ref kickOptions, null, (ref KickMemberCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            return result.ResultCode;
        }

        #endregion

        #region Public API - Game Following

        /// <summary>
        /// Signal that leader is joining a game (called by leader).
        /// Triggers follow behavior based on FollowMode.
        /// </summary>
        public async Task<Result> LeaderJoinGameAsync(string gameCode)
        {
            if (!IsInParty) return Result.NotFound;
            if (!IsLeader) return Result.InvalidUser;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Leader joining game: {gameCode}");

            // Check if party will fit (if we can determine lobby size)
            // This would require querying the target lobby first

            // Set game code in party lobby
            await SetLobbyAttributeAsync(PartyAttributes.GAME_CODE, gameCode);
            await SetLobbyAttributeAsync(PartyAttributes.GAME_STATUS, "joining");

            _pendingGameCode = gameCode;

            switch (_followMode)
            {
                case PartyFollowMode.Automatic:
                    // Members will auto-follow via polling
                    break;

                case PartyFollowMode.Confirm:
                    // Fire event for members to confirm
                    OnLeaderJoinedGame?.Invoke(gameCode);
                    break;

                case PartyFollowMode.ReadyCheck:
                    // Start ready check before joining
                    StartReadyCheck(gameCode);
                    break;

                case PartyFollowMode.Manual:
                    // Just notify, don't auto-follow
                    OnLeaderJoinedGame?.Invoke(gameCode);
                    break;
            }

            return Result.Success;
        }

        /// <summary>
        /// Follow the leader to their current game.
        /// </summary>
        public async Task<Result> FollowLeaderAsync()
        {
            if (!IsInParty) return Result.NotFound;
            if (IsLeader) return Result.InvalidUser;

            string gameCode = _currentParty.CurrentGameCode;
            if (string.IsNullOrEmpty(gameCode))
            {
                return Result.NotFound;
            }

            return await JoinGameAsync(gameCode);
        }

        /// <summary>
        /// Join a game lobby by code.
        /// </summary>
        public async Task<Result> JoinGameAsync(string gameCode)
        {
            if (string.IsNullOrEmpty(gameCode)) return Result.InvalidParameters;

            _isFollowing = true;
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Following to game: {gameCode}");

            var transport = FindAnyObjectByType<EOSNativeTransport>();
            if (transport == null)
            {
                _isFollowing = false;
                OnFollowFailed?.Invoke(gameCode, "Transport not found");
                return Result.NotConfigured;
            }

            var (result, lobby) = await transport.JoinLobbyAsync(gameCode);

            _isFollowing = false;

            if (result == Result.Success)
            {
                await SetMemberAttributeAsync(PartyMemberAttributes.STATUS, "in_game");
                OnFollowSucceeded?.Invoke(gameCode);
            }
            else
            {
                OnFollowFailed?.Invoke(gameCode, result.ToString());
            }

            return result;
        }

        /// <summary>
        /// Respond to a follow request (for Confirm mode).
        /// </summary>
        public async Task<Result> RespondToFollowRequestAsync(bool accept)
        {
            if (string.IsNullOrEmpty(_pendingGameCode))
            {
                return Result.NotFound;
            }

            if (accept)
            {
                return await JoinGameAsync(_pendingGameCode);
            }
            else
            {
                _pendingGameCode = null;
                return Result.Success;
            }
        }

        #endregion

        #region Public API - Ready Check

        /// <summary>
        /// Start a ready check (leader only).
        /// </summary>
        public void StartReadyCheck(string gameCode = null)
        {
            if (!IsInParty || !IsLeader) return;
            if (_readyCheckActive) return;

            _readyCheckActive = true;
            _readyCheckStartTime = Time.time;
            _pendingGameCode = gameCode;
            _readyCheckResponses.Clear();

            // Leader is automatically ready
            _readyCheckResponses[LocalPuid] = true;

            var checkData = new ReadyCheckData
            {
                GameCode = gameCode,
                InitiatorPuid = LocalPuid,
                Timeout = _afkTimeout,
                MemberCount = _members.Count
            };

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", "Ready check started");
            OnReadyCheckStarted?.Invoke(checkData);

            // Notify via lobby attribute
            _ = SetLobbyAttributeAsync(PartyAttributes.READY_CHECK, "active");
        }

        /// <summary>
        /// Respond to a ready check.
        /// </summary>
        public async Task RespondToReadyCheckAsync(bool ready)
        {
            if (!_readyCheckActive) return;

            _readyCheckResponses[LocalPuid] = ready;
            await SetMemberAttributeAsync(PartyMemberAttributes.READY, ready ? "true" : "false");

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Ready check response: {ready}");
            OnReadyCheckResponse?.Invoke(LocalPuid, ready);

            // Check if all responded
            if (_readyCheckResponses.Count >= _members.Count)
            {
                CompleteReadyCheck();
            }
        }

        /// <summary>
        /// Cancel an active ready check (leader only).
        /// </summary>
        public async Task CancelReadyCheckAsync()
        {
            if (!_readyCheckActive || !IsLeader) return;

            _readyCheckActive = false;
            _readyCheckResponses.Clear();

            await SetLobbyAttributeAsync(PartyAttributes.READY_CHECK, "cancelled");
            OnReadyCheckCompleted?.Invoke(false);
        }

        #endregion

        #region Public API - Chat

        /// <summary>
        /// Send a message to party chat.
        /// </summary>
        public async Task<Result> SendPartyChatAsync(string message)
        {
            if (!IsInParty) return Result.NotFound;
            if (string.IsNullOrEmpty(message)) return Result.InvalidParameters;

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string chatValue = $"{timestamp}:{message}";

            return await SetMemberAttributeAsync(PartyMemberAttributes.CHAT, chatValue);
        }

        #endregion

        #region Public API - Settings

        /// <summary>
        /// Update party settings (leader only).
        /// </summary>
        public async Task<Result> UpdateSettingsAsync(PartySettings newSettings)
        {
            if (!IsInParty) return Result.NotFound;
            if (!IsLeader) return Result.InvalidUser;

            _settings = newSettings;

            // Apply settings to lobby
            string settingsJson = JsonUtility.ToJson(newSettings);
            var result = await SetLobbyAttributeAsync(PartyAttributes.SETTINGS, settingsJson);

            if (result == Result.Success)
            {
                OnSettingsChanged?.Invoke(newSettings);
            }

            return result;
        }

        /// <summary>
        /// Get a member by PUID.
        /// </summary>
        public PartyMember? GetMember(string puid)
        {
            var member = _members.Find(m => m.Puid == puid);
            return member.Puid != null ? member : null;
        }

        #endregion

        #region Private Methods

        private void InitializeSettings()
        {
            _settings = new PartySettings
            {
                MaxSize = _defaultMaxSize,
                FollowMode = _followMode,
                AfkTimeout = _afkTimeout,
                FullLobbyBehavior = _fullLobbyBehavior,
                AllowPublicJoin = _allowPublicJoin,
                FriendsOnly = _friendsOnly
            };
        }

        private void SubscribeToNotifications()
        {
            if (_lobbyInterface == null) return;

            var updateOptions = new AddNotifyLobbyUpdateReceivedOptions();
            _lobbyUpdateHandle = _lobbyInterface.AddNotifyLobbyUpdateReceived(ref updateOptions, null, OnLobbyUpdateReceived);

            var memberOptions = new AddNotifyLobbyMemberUpdateReceivedOptions();
            _memberUpdateHandle = _lobbyInterface.AddNotifyLobbyMemberUpdateReceived(ref memberOptions, null, OnMemberUpdateReceived);

            var statusOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
            _memberStatusHandle = _lobbyInterface.AddNotifyLobbyMemberStatusReceived(ref statusOptions, null, OnMemberStatusReceived);
        }

        private void UnsubscribeFromNotifications()
        {
            if (_lobbyInterface == null) return;

            if (_lobbyUpdateHandle != 0)
                _lobbyInterface.RemoveNotifyLobbyUpdateReceived(_lobbyUpdateHandle);
            if (_memberUpdateHandle != 0)
                _lobbyInterface.RemoveNotifyLobbyMemberUpdateReceived(_memberUpdateHandle);
            if (_memberStatusHandle != 0)
                _lobbyInterface.RemoveNotifyLobbyMemberStatusReceived(_memberStatusHandle);
        }

        private void OnLobbyUpdateReceived(ref LobbyUpdateReceivedCallbackInfo info)
        {
            if (info.LobbyId != _partyLobbyId) return;

            // Refresh party data
            RefreshPartyData();
        }

        private void OnMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo info)
        {
            if (info.LobbyId != _partyLobbyId) return;

            string memberPuid = info.TargetUserId?.ToString();
            if (string.IsNullOrEmpty(memberPuid)) return;

            // Refresh member data
            RefreshMemberData(memberPuid);
        }

        private void OnMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo info)
        {
            if (info.LobbyId != _partyLobbyId) return;

            string memberPuid = info.TargetUserId?.ToString();

            switch (info.CurrentStatus)
            {
                case LobbyMemberStatus.Joined:
                    OnMemberJoinedParty(memberPuid);
                    break;

                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    OnMemberLeftParty(memberPuid);
                    break;

                case LobbyMemberStatus.Promoted:
                    // Leader change handled via lobby update
                    break;
            }
        }

        private void OnMemberJoinedParty(string puid)
        {
            if (_members.Exists(m => m.Puid == puid)) return;

            string name = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Unknown";
            var member = new PartyMember
            {
                Puid = puid,
                DisplayName = name,
                IsLeader = puid == _currentParty.LeaderPuid,
                IsReady = false,
                Status = PartyMemberStatus.Active,
                JoinTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _members.Add(member);
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Member joined: {name}");
            OnMemberJoined?.Invoke(member);
        }

        private void OnMemberLeftParty(string puid)
        {
            var member = _members.Find(m => m.Puid == puid);
            if (member.Puid == null) return;

            _members.Remove(member);
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Member left: {member.DisplayName}");
            OnMemberLeft?.Invoke(puid);

            // Handle ready check
            if (_readyCheckActive)
            {
                _readyCheckResponses.Remove(puid);
            }

            // Check if party is empty
            if (_dissolveWhenEmpty && _members.Count == 0)
            {
                OnPartyDissolved?.Invoke();
                _currentParty = default;
                _partyLobbyId = null;
            }
        }

        private void CheckLeaderGameStatus()
        {
            if (IsLeader) return;

            string currentGameCode = _currentParty.CurrentGameCode;
            if (currentGameCode != _lastKnownGameCode && !string.IsNullOrEmpty(currentGameCode))
            {
                _lastKnownGameCode = currentGameCode;
                _pendingGameCode = currentGameCode;

                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Leader joined game: {currentGameCode}");

                switch (_followMode)
                {
                    case PartyFollowMode.Automatic:
                        _ = FollowLeaderAsync();
                        break;

                    case PartyFollowMode.Confirm:
                        OnFollowRequested?.Invoke(new FollowRequest
                        {
                            GameCode = currentGameCode,
                            LeaderName = GetLeaderName(),
                            Timeout = _afkTimeout
                        });
                        break;

                    case PartyFollowMode.ReadyCheck:
                    case PartyFollowMode.Manual:
                        OnLeaderJoinedGame?.Invoke(currentGameCode);
                        break;
                }
            }
        }

        private void CompleteReadyCheck()
        {
            if (!_readyCheckActive) return;

            bool allReady = true;
            foreach (var member in _members)
            {
                if (!_readyCheckResponses.TryGetValue(member.Puid, out bool ready) || !ready)
                {
                    allReady = false;
                    break;
                }
            }

            _readyCheckActive = false;
            _ = SetLobbyAttributeAsync(PartyAttributes.READY_CHECK, "completed");

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSPartyManager", $"Ready check completed: allReady={allReady}");
            OnReadyCheckCompleted?.Invoke(allReady);

            // If all ready and we have a pending game, proceed
            if (allReady && !string.IsNullOrEmpty(_pendingGameCode))
            {
                // Leader should already be joining, members follow
                if (!IsLeader)
                {
                    _ = JoinGameAsync(_pendingGameCode);
                }
            }
        }

        private void RefreshPartyData()
        {
            if (string.IsNullOrEmpty(_partyLobbyId)) return;

            var detailsOptions = new CopyLobbyDetailsHandleOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = _partyLobbyId
            };

            if (_lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) != Result.Success)
                return;

            // Read attributes
            _currentParty.PartyName = ReadAttribute(details, PartyAttributes.PARTY_NAME) ?? _currentParty.PartyName;
            _currentParty.PartyCode = ReadAttribute(details, PartyAttributes.PARTY_CODE) ?? _currentParty.PartyCode;
            _currentParty.LeaderPuid = ReadAttribute(details, PartyAttributes.LEADER_PUID) ?? _currentParty.LeaderPuid;
            _currentParty.CurrentGameCode = ReadAttribute(details, PartyAttributes.GAME_CODE);
            _currentParty.GameStatus = ReadAttribute(details, PartyAttributes.GAME_STATUS);

            string settingsJson = ReadAttribute(details, PartyAttributes.SETTINGS);
            if (!string.IsNullOrEmpty(settingsJson))
            {
                try
                {
                    _settings = JsonUtility.FromJson<PartySettings>(settingsJson);
                }
                catch { }
            }

            // Read member count
            var countOptions = new LobbyDetailsGetMemberCountOptions();
            _currentParty.MemberCount = (int)details.GetMemberCount(ref countOptions);

            details.Release();
        }

        private void RefreshMemberData(string puid)
        {
            if (string.IsNullOrEmpty(_partyLobbyId) || string.IsNullOrEmpty(puid)) return;

            var detailsOptions = new CopyLobbyDetailsHandleOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = _partyLobbyId
            };

            if (_lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) != Result.Success)
                return;

            // Read member chat attribute and fire event if it's a new message
            var chatOptions = new LobbyDetailsCopyMemberAttributeByKeyOptions
            {
                AttrKey = PartyMemberAttributes.CHAT,
                TargetUserId = ProductUserId.FromString(puid)
            };

            if (details.CopyMemberAttributeByKey(ref chatOptions, out var chatAttr) == Result.Success && chatAttr.HasValue)
            {
                string chatValue = chatAttr.Value.Data?.Value.AsUtf8;
                if (!string.IsNullOrEmpty(chatValue))
                {
                    // Format: "timestamp:message"
                    int colonIndex = chatValue.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string message = chatValue.Substring(colonIndex + 1);
                        string memberName = EOSPlayerRegistry.Instance?.GetPlayerName(puid) ?? "Unknown";
                        OnPartyChatReceived?.Invoke(puid, memberName, message);
                    }
                }
            }

            details.Release();
        }

        private string ReadAttribute(LobbyDetails details, string key)
        {
            var options = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
            if (details.CopyAttributeByKey(ref options, out var attr) == Result.Success && attr.HasValue)
            {
                return attr.Value.Data?.Value.AsUtf8;
            }
            return null;
        }

        private async Task<Result> SetPartyAttributesAsync(string name, int maxSize)
        {
            await SetLobbyAttributeAsync(PartyAttributes.PARTY_NAME, name);
            await SetLobbyAttributeAsync(PartyAttributes.LEADER_PUID, LocalPuid);
            await SetLobbyAttributeAsync(PartyAttributes.MAX_SIZE, maxSize.ToString());
            await SetLobbyAttributeAsync(PartyAttributes.GAME_STATUS, "idle");
            return Result.Success;
        }

        private async Task<Result> SetLobbyAttributeAsync(string key, string value)
        {
            var modifyOptions = new UpdateLobbyModificationOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = _partyLobbyId
            };

            if (_lobbyInterface.UpdateLobbyModification(ref modifyOptions, out var modification) != Result.Success)
                return Result.InvalidParameters;

            var attrOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = new AttributeData
                {
                    Key = key,
                    Value = new AttributeDataValue { AsUtf8 = value }
                },
                Visibility = LobbyAttributeVisibility.Public
            };
            modification.AddAttribute(ref attrOptions);

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            var tcs = new TaskCompletionSource<UpdateLobbyCallbackInfo>();
            _lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            modification.Release();

            return result.ResultCode;
        }

        private async Task<Result> SetMemberAttributeAsync(string key, string value)
        {
            var modifyOptions = new UpdateLobbyModificationOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = _partyLobbyId
            };

            if (_lobbyInterface.UpdateLobbyModification(ref modifyOptions, out var modification) != Result.Success)
                return Result.InvalidParameters;

            var attrOptions = new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData
                {
                    Key = key,
                    Value = new AttributeDataValue { AsUtf8 = value }
                },
                Visibility = LobbyAttributeVisibility.Public
            };
            modification.AddMemberAttribute(ref attrOptions);

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            var tcs = new TaskCompletionSource<UpdateLobbyCallbackInfo>();
            _lobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo info) =>
            {
                tcs.SetResult(info);
            });

            var result = await tcs.Task;
            modification.Release();

            return result.ResultCode;
        }

        private string GeneratePartyCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new System.Random();
            var code = new char[6];
            for (int i = 0; i < 6; i++)
            {
                code[i] = chars[random.Next(chars.Length)];
            }
            return new string(code);
        }

        private string GetLocalDisplayName()
        {
            return EOSPlayerRegistry.Instance?.GetPlayerName(LocalPuid)
                ?? EOSLobbyChatManager.GenerateNameFromPuid(LocalPuid);
        }

        private string GetLeaderName()
        {
            var leader = _members.Find(m => m.IsLeader);
            return leader.DisplayName ?? "Leader";
        }

        #endregion
    }

    #region Data Structures

    /// <summary>
    /// Party data.
    /// </summary>
    [Serializable]
    public struct PartyData
    {
        public string PartyId;
        public string PartyCode;
        public string PartyName;
        public string LeaderPuid;
        public int MaxSize;
        public int MemberCount;
        public string CurrentGameCode;
        public string GameStatus;
        public bool IsValid;
    }

    /// <summary>
    /// Party member data.
    /// </summary>
    [Serializable]
    public struct PartyMember
    {
        public string Puid;
        public string DisplayName;
        public bool IsLeader;
        public bool IsReady;
        public PartyMemberStatus Status;
        public long JoinTime;
    }

    /// <summary>
    /// Party settings (configurable).
    /// </summary>
    [Serializable]
    public struct PartySettings
    {
        public int MaxSize;
        public PartyFollowMode FollowMode;
        public float AfkTimeout;
        public PartyFullLobbyBehavior FullLobbyBehavior;
        public bool AllowPublicJoin;
        public bool FriendsOnly;
    }

    /// <summary>
    /// Follow request data.
    /// </summary>
    public struct FollowRequest
    {
        public string GameCode;
        public string LeaderName;
        public float Timeout;
    }

    /// <summary>
    /// Ready check data.
    /// </summary>
    public struct ReadyCheckData
    {
        public string GameCode;
        public string InitiatorPuid;
        public float Timeout;
        public int MemberCount;
    }

    #endregion

    #region Enums

    /// <summary>
    /// How party members follow the leader.
    /// </summary>
    public enum PartyFollowMode
    {
        /// <summary>Members automatically follow when leader joins a game.</summary>
        Automatic,
        /// <summary>Members get a confirmation prompt before following.</summary>
        Confirm,
        /// <summary>Leader initiates ready check before everyone joins.</summary>
        ReadyCheck,
        /// <summary>Members must manually choose to follow.</summary>
        Manual
    }

    /// <summary>
    /// What happens when leader joins a full lobby.
    /// </summary>
    public enum PartyFullLobbyBehavior
    {
        /// <summary>Block leader from joining if party won't fit.</summary>
        BlockJoin,
        /// <summary>Warn leader and ask if they want to join anyway.</summary>
        WarnAndAsk,
        /// <summary>Join who fits, others wait in queue.</summary>
        PartialJoin,
        /// <summary>Leader joins alone, party stays behind.</summary>
        LeaderOnly
    }

    /// <summary>
    /// Party persistence behavior.
    /// </summary>
    public enum PartyPersistence
    {
        /// <summary>Party dissolves when leader closes game.</summary>
        SessionBased,
        /// <summary>Party persists until explicitly disbanded.</summary>
        Persistent,
        /// <summary>Party expires after period of inactivity.</summary>
        TimedExpiry
    }

    /// <summary>
    /// Party member status.
    /// </summary>
    public enum PartyMemberStatus
    {
        Active,
        Away,
        InGame,
        Disconnected
    }

    #endregion

    #region Attribute Keys

    /// <summary>
    /// Party lobby attribute keys.
    /// </summary>
    public static class PartyAttributes
    {
        public const string PARTY_NAME = "PARTY_NAME";
        public const string PARTY_CODE = "PARTY_CODE";
        public const string LEADER_PUID = "LEADER_PUID";
        public const string MAX_SIZE = "MAX_SIZE";
        public const string GAME_CODE = "GAME_CODE";
        public const string GAME_STATUS = "GAME_STATUS";
        public const string READY_CHECK = "READY_CHECK";
        public const string SETTINGS = "SETTINGS";
    }

    /// <summary>
    /// Party member attribute keys.
    /// </summary>
    public static class PartyMemberAttributes
    {
        public const string DISPLAY_NAME = "DISPLAY_NAME";
        public const string STATUS = "STATUS";
        public const string READY = "READY";
        public const string CHAT = "CHAT";
    }

    #endregion
}
