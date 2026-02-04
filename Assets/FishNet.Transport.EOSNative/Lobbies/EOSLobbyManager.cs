using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Voice;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Lobbies
{
    /// <summary>
    /// Manages EOS lobby operations: create, search, join, leave.
    /// Supports custom join codes (default: 4-digit) or EOS-generated LobbyIds.
    /// </summary>
    public class EOSLobbyManager : MonoBehaviour
    {
        #region Singleton

        private static EOSLobbyManager _instance;
        public static EOSLobbyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSLobbyManager>();

                    // Auto-create if not found - lobby manager is integral to the transport
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSLobbyManager");
                        _instance = go.AddComponent<EOSLobbyManager>();
                        DontDestroyOnLoad(go);
                        EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "Auto-created singleton instance");
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when we successfully join a lobby.
        /// </summary>
        public event Action<LobbyData> OnLobbyJoined;

        /// <summary>
        /// Fired when we leave a lobby.
        /// </summary>
        public event Action OnLobbyLeft;

        /// <summary>
        /// Fired when a member joins the current lobby.
        /// </summary>
        public event Action<LobbyMemberData> OnMemberJoined;

        /// <summary>
        /// Fired when a member leaves the current lobby.
        /// </summary>
        public event Action<string> OnMemberLeft; // PUID

        /// <summary>
        /// Fired when the lobby owner changes (host migration).
        /// </summary>
        public event Action<string> OnOwnerChanged; // New owner PUID

        /// <summary>
        /// Fired when lobby attributes are updated.
        /// </summary>
        public event Action<LobbyData> OnLobbyUpdated;

        /// <summary>
        /// Fired when a member's attributes are updated (including chat).
        /// </summary>
        public event Action<string, string, string> OnMemberAttributeUpdated; // PUID, key, value

        #endregion

        #region Properties

        /// <summary>
        /// The current lobby we're in, if any.
        /// </summary>
        public LobbyData CurrentLobby { get; private set; }

        /// <summary>
        /// Whether we're currently in a lobby.
        /// </summary>
        public bool IsInLobby => CurrentLobby.IsValid;

        /// <summary>
        /// Whether we're the owner of the current lobby.
        /// </summary>
        public bool IsOwner => IsInLobby && CurrentLobby.OwnerPuid == LocalPuid;

        /// <summary>
        /// Local player's ProductUserId string.
        /// </summary>
        private string LocalPuid => EOSManager.Instance?.LocalProductUserId?.ToString();

        /// <summary>
        /// Local ProductUserId.
        /// </summary>
        private ProductUserId LocalProductUserId => EOSManager.Instance?.LocalProductUserId;

        /// <summary>
        /// The lobby interface.
        /// </summary>
        private LobbyInterface LobbyInterface => EOSManager.Instance?.LobbyInterface;

        #endregion

        #region Private Fields

        private NotifyEventHandle _lobbyUpdateHandle;
        private NotifyEventHandle _memberUpdateHandle;
        private NotifyEventHandle _memberStatusHandle;

        private static readonly System.Random _random = new System.Random();

        // Guard against re-entrant refresh calls (prevents StackOverflow from recursive callbacks)
        private bool _isRefreshing;
        private bool _refreshPending;

        // First-to-join tracking (PUID of first non-host member to join)
        private string _firstToJoinPuid;
        private List<string> _joinOrder = new List<string>();

        #endregion

        #region First-to-Join API

        /// <summary>
        /// The PUID of the first non-host player to join the lobby.
        /// </summary>
        public string FirstToJoinPuid => _firstToJoinPuid;

        /// <summary>
        /// Check if a player was the first to join (non-host).
        /// </summary>
        public bool IsFirstToJoin(string puid)
        {
            return !string.IsNullOrEmpty(_firstToJoinPuid) && _firstToJoinPuid == puid;
        }

        /// <summary>
        /// Get the join order number for a player (1 = first, 2 = second, etc.)
        /// Returns 0 for host, -1 if not found.
        /// </summary>
        public int GetJoinOrder(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return -1;
            if (IsInLobby && puid == CurrentLobby.OwnerPuid) return 0;
            int index = _joinOrder.IndexOf(puid);
            return index >= 0 ? index + 1 : -1;
        }

        /// <summary>
        /// Get the join order list (excluding host).
        /// </summary>
        public IReadOnlyList<string> JoinOrder => _joinOrder;

        private void TrackJoin(string puid)
        {
            // Don't track host
            if (IsInLobby && puid == CurrentLobby.OwnerPuid) return;
            if (_joinOrder.Contains(puid)) return;

            _joinOrder.Add(puid);
            if (_firstToJoinPuid == null)
            {
                _firstToJoinPuid = puid;
            }
        }

        private void ResetJoinTracking()
        {
            _firstToJoinPuid = null;
            _joinOrder.Clear();
        }

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

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (!_isExitingPlayMode)
            {
                UnsubscribeFromNotifications();
            }
#else
            UnsubscribeFromNotifications();
#endif

            if (_instance == this)
            {
                _instance = null;
            }
        }

#if UNITY_EDITOR
        private bool _isExitingPlayMode;

        /// <summary>
        /// Editor safety pattern: clean up before Unity tears things down.
        /// </summary>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _isExitingPlayMode = true;
                // Properly unsubscribe while EOSManager is still valid
                UnsubscribeFromNotifications();
                CurrentLobby = default;
            }
        }
#endif

        #endregion

        #region Public API - Create

        /// <summary>
        /// Creates a new lobby with the given options.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> CreateLobbyAsync(LobbyCreateOptions options = null)
        {
            options ??= new LobbyCreateOptions();

            if (LobbyInterface == null || LocalProductUserId == null)
            {
                EOSDebugLogger.LogError("EOSLobbyManager", "Not initialized. Ensure EOSManager is logged in.");
                return (Result.NotConfigured, default);
            }

            // Determine join code strategy
            bool useEosLobbyId = options.UseEosLobbyId;
            string joinCode = null;

            if (!useEosLobbyId)
            {
                // Generate join code if not provided
                joinCode = options.JoinCode ?? GenerateJoinCode();

                // Check if code is already in use
                var (searchResult, existingLobbies) = await SearchLobbiesAsync(new LobbySearchOptions
                {
                    JoinCode = joinCode,
                    MaxResults = 1
                });

                if (searchResult == Result.Success && existingLobbies.Count > 0)
                {
                    Debug.LogWarning($"[EOSLobbyManager] Join code {joinCode} already in use. Generating new one.");
                    joinCode = GenerateJoinCode();
                }
            }
            // If useEosLobbyId, joinCode stays null - we'll set it to LobbyId after creation

            // Note: AllowedPlatformIds requires uint[] with EOS platform constants (e.g., Common.OPT_EPIC = 100)
            // For now, we leave it null (unrestricted) - proper crossplay filtering needs platform ID mapping
            uint[] allowedPlatforms = null;
            // TODO: Implement proper platform ID mapping for crossplay filtering
            // if (!options.AllowCrossplay) { allowedPlatforms = new[] { GetCurrentPlatformId() }; }
            // null = all platforms allowed

            // Create lobby - matching tank demo pattern for RTC
            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = LocalProductUserId,
                MaxLobbyMembers = options.MaxPlayers,
                PermissionLevel = options.IsPublic ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Joinviapresence,
                BucketId = options.BucketId,
                EnableJoinById = true,
                AllowInvites = false,
                RejoinAfterKickRequiresInvite = false,
                EnableRTCRoom = options.EnableVoice,
                // Manual audio output for 3D spatial audio support
                LocalRTCOptions = options.EnableVoice ? new LocalRTCOptions
                {
                    UseManualAudioOutput = true  // We handle playback (for per-player 3D audio)
                } : null,
                PresenceEnabled = false,
                DisableHostMigration = !options.AllowHostMigration,
                AllowedPlatformIds = allowedPlatforms,
                CrossplayOptOut = !options.AllowCrossplay
            };

            var tcs = new TaskCompletionSource<CreateLobbyCallbackInfo>();
            LobbyInterface.CreateLobby(ref createOptions, null, (ref CreateLobbyCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to create lobby: {result.ResultCode}");
                return (result.ResultCode, default);
            }

            string lobbyId = result.LobbyId;
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Lobby created: {lobbyId}");

            // If using EOS LobbyId as code, set it now
            if (useEosLobbyId)
            {
                joinCode = lobbyId;
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Using EOS LobbyId as join code: {joinCode}");
            }
            else
            {
                // Set the join code attribute (only for custom codes)
                var setCodeResult = await SetLobbyAttributeAsync(lobbyId, LobbyAttributes.JOIN_CODE, joinCode);
                if (setCodeResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSLobbyManager] Failed to set join code attribute: {setCodeResult}");
                }
            }

            // Set migration support attribute
            if (options.AllowHostMigration)
            {
                await SetLobbyAttributeAsync(lobbyId, LobbyAttributes.MIGRATION_SUPPORT, "true");
            }

            // Set all attributes (includes convenience properties + custom attributes)
            var allAttributes = options.BuildAttributes();
            foreach (var kvp in allAttributes)
            {
                var attrResult = await SetLobbyAttributeAsync(lobbyId, kvp.Key, kvp.Value);
                if (attrResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSLobbyManager] Failed to set attribute {kvp.Key}: {attrResult}");
                }
            }

            // Get lobby details and cache
            var lobbyData = await GetLobbyDataAsync(lobbyId);
            lobbyData.JoinCode = joinCode;
            lobbyData.IsEosLobbyIdCode = useEosLobbyId;
            CurrentLobby = lobbyData;

            // Subscribe to notifications
            SubscribeToNotifications(lobbyId);

            // Notify voice manager if voice is enabled
            if (options.EnableVoice)
            {
                EOSVoiceManager.Instance?.OnLobbyCreated(lobbyId);
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Lobby ready with code: {joinCode}{(useEosLobbyId ? " (EOS LobbyId)" : "")}");
            OnLobbyJoined?.Invoke(CurrentLobby);

            return (Result.Success, CurrentLobby);
        }

        #endregion

        #region Public API - Search

        /// <summary>
        /// Searches for lobbies matching the given options (attribute-based search).
        /// This uses SetParameter internally.
        /// </summary>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchLobbiesAsync(LobbySearchOptions options = null)
        {
            options ??= new LobbySearchOptions();

            if (LobbyInterface == null || LocalProductUserId == null)
            {
                EOSDebugLogger.LogError("EOSLobbyManager", "Not initialized.");
                return (Result.NotConfigured, null);
            }

            // Create search handle
            var createSearchOptions = new CreateLobbySearchOptions { MaxResults = options.MaxResults };
            var createResult = LobbyInterface.CreateLobbySearch(ref createSearchOptions, out LobbySearch searchHandle);

            if (createResult != Result.Success)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to create search: {createResult}");
                return (createResult, null);
            }

            // Set search parameters
            if (!string.IsNullOrEmpty(options.JoinCode))
            {
                // Search by specific join code
                var paramOptions = new LobbySearchSetParameterOptions
                {
                    ComparisonOp = ComparisonOp.Equal,
                    Parameter = new AttributeData
                    {
                        Key = LobbyAttributes.JOIN_CODE,
                        Value = new AttributeDataValue { AsUtf8 = options.JoinCode }
                    }
                };
                var paramResult = searchHandle.SetParameter(ref paramOptions);
                if (paramResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSLobbyManager] Failed to set JOIN_CODE parameter: {paramResult}");
                }
            }
            else
            {
                // No specific join code - search for all lobbies with a non-empty JOIN_CODE
                // This pattern comes from FishyEOS samples: use Notequal with empty string
                var paramOptions = new LobbySearchSetParameterOptions
                {
                    ComparisonOp = ComparisonOp.Notequal,
                    Parameter = new AttributeData
                    {
                        Key = LobbyAttributes.JOIN_CODE,
                        Value = new AttributeDataValue { AsUtf8 = "" }
                    }
                };
                var paramResult = searchHandle.SetParameter(ref paramOptions);
                if (paramResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSLobbyManager] Failed to set search parameter: {paramResult}");
                    // Continue anyway - search may still work without this filter
                }
            }

            // Filter by bucket if specified
            if (!string.IsNullOrEmpty(options.BucketId))
            {
                var bucketParam = new LobbySearchSetParameterOptions
                {
                    ComparisonOp = ComparisonOp.Equal,
                    Parameter = new AttributeData
                    {
                        Key = "bucket",
                        Value = new AttributeDataValue { AsUtf8 = options.BucketId }
                    }
                };
                var bucketResult = searchHandle.SetParameter(ref bucketParam);
                if (bucketResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSLobbyManager] Failed to set bucket parameter: {bucketResult}");
                }
            }

            // Additional filters (legacy Dictionary<string, string> format - equality only)
            if (options.Filters != null)
            {
                foreach (var kvp in options.Filters)
                {
                    var filterParam = new LobbySearchSetParameterOptions
                    {
                        ComparisonOp = ComparisonOp.Equal,
                        Parameter = new AttributeData
                        {
                            Key = kvp.Key,
                            Value = new AttributeDataValue { AsUtf8 = kvp.Value }
                        }
                    };
                    var filterResult = searchHandle.SetParameter(ref filterParam);
                    if (filterResult != Result.Success)
                    {
                        Debug.LogWarning($"[EOSLobbyManager] Failed to set filter '{kvp.Key}': {filterResult}");
                    }
                }
            }

            // Advanced attribute filters (with comparison operators)
            if (options.AttributeFilters != null && options.AttributeFilters.Count > 0)
            {
                foreach (var filter in options.AttributeFilters)
                {
                    var filterParam = new LobbySearchSetParameterOptions
                    {
                        ComparisonOp = ToEOSComparison(filter.Comparison),
                        Parameter = new AttributeData
                        {
                            Key = filter.Key,
                            Value = new AttributeDataValue { AsUtf8 = filter.Value }
                        }
                    };
                    var filterResult = searchHandle.SetParameter(ref filterParam);
                    if (filterResult != Result.Success)
                    {
                        Debug.LogWarning($"[EOSLobbyManager] Failed to set filter '{filter}': {filterResult}");
                    }
                }
            }

            // Execute search
            var findOptions = new LobbySearchFindOptions { LocalUserId = LocalProductUserId };
            var tcs = new TaskCompletionSource<LobbySearchFindCallbackInfo>();
            searchHandle.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success && result.ResultCode != Result.NotFound)
            {
                Debug.LogError($"[EOSLobbyManager] Search failed: {result.ResultCode}");
                searchHandle.Release();
                return (result.ResultCode, null);
            }

            // Process results
            var lobbies = ProcessSearchResults(searchHandle, options);
            searchHandle.Release();

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Found {lobbies.Count} lobbies");
            return (Result.Success, lobbies);
        }

        /// <summary>
        /// Searches for a lobby by its exact EOS lobby ID.
        /// This is the fastest lookup method when you know the lobby ID.
        /// Uses SetLobbyId internally (mutually exclusive with SetParameter/SetTargetUserId).
        /// </summary>
        /// <param name="lobbyId">The EOS lobby ID to search for.</param>
        /// <returns>Result and lobby data if found.</returns>
        public async Task<(Result result, LobbyData? lobby)> SearchByLobbyIdAsync(string lobbyId)
        {
            if (string.IsNullOrEmpty(lobbyId))
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSLobbyManager", "SearchByLobbyId: lobbyId is null or empty");
                return (Result.InvalidParameters, null);
            }

            if (LobbyInterface == null || LocalProductUserId == null)
            {
                EOSDebugLogger.LogError("EOSLobbyManager", "Not initialized.");
                return (Result.NotConfigured, null);
            }

            // Create search handle
            var createSearchOptions = new CreateLobbySearchOptions { MaxResults = 1 };
            var createResult = LobbyInterface.CreateLobbySearch(ref createSearchOptions, out LobbySearch searchHandle);

            if (createResult != Result.Success)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to create search: {createResult}");
                return (createResult, null);
            }

            // Set lobby ID (this uses the dedicated SetLobbyId path)
            var setLobbyIdOptions = new LobbySearchSetLobbyIdOptions { LobbyId = lobbyId };
            var setResult = searchHandle.SetLobbyId(ref setLobbyIdOptions);
            if (setResult != Result.Success)
            {
                Debug.LogWarning($"[EOSLobbyManager] Failed to set lobby ID: {setResult}");
                searchHandle.Release();
                return (setResult, null);
            }

            // Execute search
            var findOptions = new LobbySearchFindOptions { LocalUserId = LocalProductUserId };
            var tcs = new TaskCompletionSource<LobbySearchFindCallbackInfo>();
            searchHandle.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" SearchByLobbyId: {result.ResultCode}");
                searchHandle.Release();
                return (result.ResultCode, null);
            }

            // Get result
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = searchHandle.GetSearchResultCount(ref countOptions);

            if (count == 0)
            {
                searchHandle.Release();
                return (Result.NotFound, null);
            }

            var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
            var copyResult = searchHandle.CopySearchResultByIndex(ref copyOptions, out LobbyDetails details);

            if (copyResult != Result.Success || details == null)
            {
                searchHandle.Release();
                return (copyResult, null);
            }

            var lobbyData = ExtractLobbyData(details);
            details.Release();
            searchHandle.Release();

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" SearchByLobbyId found: {lobbyData}");
            return (Result.Success, lobbyData);
        }

        /// <summary>
        /// Searches for all PUBLIC lobbies that contain a specific user.
        /// Uses SetTargetUserId internally (mutually exclusive with SetParameter/SetLobbyId).
        /// Note: Only finds PUBLIC lobbies - presence-only lobbies will not be returned.
        /// </summary>
        /// <param name="memberPuid">The ProductUserId string of the user to search for.</param>
        /// <param name="maxResults">Maximum number of results (default: 10).</param>
        /// <returns>Result and list of lobbies containing the user.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchByMemberAsync(string memberPuid, uint maxResults = 10)
        {
            if (string.IsNullOrEmpty(memberPuid))
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSLobbyManager", "SearchByMember: memberPuid is null or empty");
                return (Result.InvalidParameters, null);
            }

            // Convert string to ProductUserId
            var productUserId = ProductUserId.FromString(memberPuid);
            if (productUserId == null || !productUserId.IsValid())
            {
                Debug.LogWarning($"[EOSLobbyManager] SearchByMember: Invalid PUID format: {memberPuid}");
                return (Result.InvalidParameters, null);
            }

            return await SearchByMemberAsync(productUserId, maxResults);
        }

        /// <summary>
        /// Searches for all PUBLIC lobbies that contain a specific user.
        /// Uses SetTargetUserId internally (mutually exclusive with SetParameter/SetLobbyId).
        /// Note: Only finds PUBLIC lobbies - presence-only lobbies will not be returned.
        /// </summary>
        /// <param name="memberPuid">The ProductUserId of the user to search for.</param>
        /// <param name="maxResults">Maximum number of results (default: 10).</param>
        /// <returns>Result and list of lobbies containing the user.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchByMemberAsync(ProductUserId memberPuid, uint maxResults = 10)
        {
            if (memberPuid == null || !memberPuid.IsValid())
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSLobbyManager", "SearchByMember: memberPuid is null or invalid");
                return (Result.InvalidParameters, null);
            }

            if (LobbyInterface == null || LocalProductUserId == null)
            {
                EOSDebugLogger.LogError("EOSLobbyManager", "Not initialized.");
                return (Result.NotConfigured, null);
            }

            // Create search handle
            var createSearchOptions = new CreateLobbySearchOptions { MaxResults = maxResults };
            var createResult = LobbyInterface.CreateLobbySearch(ref createSearchOptions, out LobbySearch searchHandle);

            if (createResult != Result.Success)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to create search: {createResult}");
                return (createResult, null);
            }

            // Set target user ID (this uses the dedicated SetTargetUserId path)
            var setTargetOptions = new LobbySearchSetTargetUserIdOptions { TargetUserId = memberPuid };
            var setResult = searchHandle.SetTargetUserId(ref setTargetOptions);
            if (setResult != Result.Success)
            {
                Debug.LogWarning($"[EOSLobbyManager] Failed to set target user ID: {setResult}");
                searchHandle.Release();
                return (setResult, null);
            }

            // Execute search
            var findOptions = new LobbySearchFindOptions { LocalUserId = LocalProductUserId };
            var tcs = new TaskCompletionSource<LobbySearchFindCallbackInfo>();
            searchHandle.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success && result.ResultCode != Result.NotFound)
            {
                Debug.LogError($"[EOSLobbyManager] SearchByMember failed: {result.ResultCode}");
                searchHandle.Release();
                return (result.ResultCode, null);
            }

            // Process results (no filtering - we want all lobbies containing this user)
            var lobbies = new List<LobbyData>();
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = searchHandle.GetSearchResultCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                var copyResult = searchHandle.CopySearchResultByIndex(ref copyOptions, out LobbyDetails details);

                if (copyResult == Result.Success && details != null)
                {
                    var lobbyData = ExtractLobbyData(details);
                    lobbies.Add(lobbyData);
                    details.Release();
                }
            }

            searchHandle.Release();

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" SearchByMember found {lobbies.Count} lobbies containing {memberPuid}");
            return (Result.Success, lobbies);
        }

        /// <summary>
        /// Finds lobbies where a friend is currently playing.
        /// Convenience wrapper around SearchByMemberAsync.
        /// </summary>
        /// <param name="friendPuid">The friend's ProductUserId string.</param>
        /// <returns>Result and list of joinable lobbies.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> FindFriendLobbiesAsync(string friendPuid)
        {
            var (result, lobbies) = await SearchByMemberAsync(friendPuid);

            if (result != Result.Success || lobbies == null)
            {
                return (result, lobbies);
            }

            // Filter to only joinable lobbies (not full, not in progress)
            var joinable = lobbies.FindAll(l => l.AvailableSlots > 0 && !l.IsInProgress);
            return (Result.Success, joinable);
        }

        /// <summary>
        /// Searches for a lobby by its join code (custom code or EOS LobbyId).
        /// Automatically detects if the code is an EOS LobbyId and uses the appropriate search method.
        /// </summary>
        public async Task<(Result result, LobbyData? lobby)> FindLobbyByCodeAsync(string joinCode)
        {
            // Check if this looks like an EOS LobbyId
            if (IsEosLobbyIdFormat(joinCode))
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" FindLobbyByCode: Detected EOS LobbyId format, searching by LobbyId");
                return await SearchByLobbyIdAsync(joinCode);
            }

            // Standard search by JOIN_CODE attribute
            var (result, lobbies) = await SearchLobbiesAsync(new LobbySearchOptions
            {
                JoinCode = joinCode,
                MaxResults = 1,
                OnlyAvailable = false // Show even if full (for better error messages)
            });

            if (result != Result.Success)
            {
                return (result, null);
            }

            if (lobbies == null || lobbies.Count == 0)
            {
                return (Result.NotFound, null);
            }

            return (Result.Success, lobbies[0]);
        }

        /// <summary>
        /// Checks if a string appears to be an EOS LobbyId format.
        /// EOS LobbyIds are typically 32+ character hex strings or contain specific patterns.
        /// </summary>
        public static bool IsEosLobbyIdFormat(string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;

            // EOS LobbyIds are typically 32+ characters (hex or alphanumeric)
            // Custom codes are typically shorter and simpler
            if (code.Length >= 24)
            {
                // Check if it's all hex characters (typical EOS format)
                foreach (char c in code)
                {
                    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                        return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Searches for lobbies by name (exact match).
        /// </summary>
        /// <param name="lobbyName">The exact lobby name to search for.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>Result and list of matching lobbies.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchByNameAsync(string lobbyName, uint maxResults = 10)
        {
            if (string.IsNullOrEmpty(lobbyName))
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSLobbyManager", "SearchByName: name is null or empty");
                return (Result.InvalidParameters, null);
            }

            var options = new LobbySearchOptions()
                .WithLobbyName(lobbyName)
                .WithMaxResults(maxResults);

            return await SearchLobbiesAsync(options);
        }

        /// <summary>
        /// Searches for lobbies with names containing a substring.
        /// </summary>
        /// <param name="substring">The substring to search for in lobby names.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>Result and list of matching lobbies.</returns>
        public async Task<(Result result, List<LobbyData> lobbies)> SearchByNameContainingAsync(string substring, uint maxResults = 10)
        {
            if (string.IsNullOrEmpty(substring))
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSLobbyManager", "SearchByNameContaining: substring is null or empty");
                return (Result.InvalidParameters, null);
            }

            var options = new LobbySearchOptions()
                .WithLobbyNameContaining(substring)
                .WithMaxResults(maxResults);

            return await SearchLobbiesAsync(options);
        }

        /// <summary>
        /// Finds a single lobby by exact name.
        /// </summary>
        /// <param name="lobbyName">The exact lobby name to find.</param>
        /// <returns>Result and lobby data if found.</returns>
        public async Task<(Result result, LobbyData? lobby)> FindLobbyByNameAsync(string lobbyName)
        {
            var (result, lobbies) = await SearchByNameAsync(lobbyName, 1);

            if (result != Result.Success)
            {
                return (result, null);
            }

            if (lobbies == null || lobbies.Count == 0)
            {
                return (Result.NotFound, null);
            }

            return (Result.Success, lobbies[0]);
        }

        /// <summary>
        /// Joins a lobby by its name.
        /// If multiple lobbies have the same name, joins the first one found.
        /// </summary>
        /// <param name="lobbyName">The lobby name to search for and join.</param>
        /// <returns>Result and lobby data.</returns>
        public async Task<(Result result, LobbyData lobby)> JoinLobbyByNameAsync(string lobbyName)
        {
            var (findResult, lobbyData) = await FindLobbyByNameAsync(lobbyName);

            if (findResult != Result.Success || !lobbyData.HasValue)
            {
                Debug.LogWarning($"[EOSLobbyManager] Lobby with name '{lobbyName}' not found");
                return (findResult == Result.Success ? Result.NotFound : findResult, default);
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" JoinByName: Found lobby '{lobbyName}' ({lobbyData.Value.JoinCode})");
            return await JoinLobbyByIdAsync(lobbyData.Value.LobbyId);
        }

        /// <summary>
        /// Quick match - finds and joins the first available lobby.
        /// Excludes password-protected and in-progress games.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> QuickMatchAsync()
        {
            var (searchResult, lobbies) = await SearchLobbiesAsync(LobbySearchOptions.QuickMatch());

            if (searchResult != Result.Success)
            {
                return (searchResult, default);
            }

            if (lobbies == null || lobbies.Count == 0)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "QuickMatch: No available lobbies found");
                return (Result.NotFound, default);
            }

            // Pick a random lobby from results for better distribution
            var random = new System.Random();
            var selectedLobby = lobbies[random.Next(lobbies.Count)];

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" QuickMatch: Joining lobby {selectedLobby.JoinCode}");
            return await JoinLobbyByIdAsync(selectedLobby.LobbyId);
        }

        /// <summary>
        /// Finds and joins the first lobby matching the given filters.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> JoinFirstMatchingAsync(LobbySearchOptions options)
        {
            var (searchResult, lobbies) = await SearchLobbiesAsync(options);

            if (searchResult != Result.Success)
            {
                return (searchResult, default);
            }

            if (lobbies == null || lobbies.Count == 0)
            {
                return (Result.NotFound, default);
            }

            // Join the first matching lobby
            var selectedLobby = lobbies[0];
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" JoinFirstMatching: Joining lobby {selectedLobby.JoinCode}");
            return await JoinLobbyByIdAsync(selectedLobby.LobbyId);
        }

        /// <summary>
        /// Quick match OR auto-host - finds any available lobby and joins, OR hosts a new one if none found.
        /// This is the recommended way to implement "Play Now" functionality.
        /// </summary>
        /// <param name="hostOptions">Options to use if hosting is needed. If null, uses defaults.</param>
        /// <param name="searchOptions">Search filters (game mode, region, etc.). If null, finds any available lobby.</param>
        /// <returns>Result, lobby data, and whether we became the host.</returns>
        public async Task<(Result result, LobbyData lobby, bool didHost)> QuickMatchOrHostAsync(
            LobbyCreateOptions hostOptions = null,
            LobbySearchOptions searchOptions = null)
        {
            // Build search options - use provided or default to QuickMatch
            var effectiveSearchOptions = searchOptions ?? LobbySearchOptions.QuickMatch();

            // Ensure we exclude full/passworded/in-progress for quick match
            effectiveSearchOptions.OnlyAvailable = true;
            effectiveSearchOptions.ExcludePasswordProtected = true;
            effectiveSearchOptions.ExcludeInProgress = true;

            // First try to find a lobby
            var (searchResult, lobbies) = await SearchLobbiesAsync(effectiveSearchOptions);

            if (searchResult == Result.Success && lobbies != null && lobbies.Count > 0)
            {
                // Filter by min/max players if specified
                if (searchOptions?.MinPlayersFilter.HasValue == true || searchOptions?.MaxPlayersFilter.HasValue == true)
                {
                    lobbies = lobbies.FindAll(l =>
                    {
                        bool passesMin = !searchOptions.MinPlayersFilter.HasValue || l.MemberCount >= searchOptions.MinPlayersFilter.Value;
                        bool passesMax = !searchOptions.MaxPlayersFilter.HasValue || l.MaxMembers <= searchOptions.MaxPlayersFilter.Value;
                        return passesMin && passesMax;
                    });
                }

                if (lobbies.Count > 0)
                {
                    // Found lobbies - join random one
                    var random = new System.Random();
                    var selectedLobby = lobbies[random.Next(lobbies.Count)];
                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" QuickMatchOrHost: Found {lobbies.Count} lobbies, joining {selectedLobby.JoinCode}");
                    var (joinResult, lobby) = await JoinLobbyByIdAsync(selectedLobby.LobbyId);
                    return (joinResult, lobby, false); // didHost = false
                }
            }

            // No lobbies found - create and host one
            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "QuickMatchOrHost: No lobbies found, hosting...");
            hostOptions ??= new LobbyCreateOptions();
            var (createResult, newLobby) = await CreateLobbyAsync(hostOptions);
            return (createResult, newLobby, true); // didHost = true
        }

        /// <summary>
        /// Finds and joins a random lobby matching the game mode.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> JoinByGameModeAsync(string gameMode)
        {
            var options = LobbySearchOptions.ForGameMode(gameMode)
                .OnlyWithAvailableSlots()
                .ExcludePassworded()
                .ExcludeGamesInProgress();

            var (searchResult, lobbies) = await SearchLobbiesAsync(options);

            if (searchResult != Result.Success)
            {
                return (searchResult, default);
            }

            if (lobbies == null || lobbies.Count == 0)
            {
                return (Result.NotFound, default);
            }

            // Pick random for better distribution
            var random = new System.Random();
            var selectedLobby = lobbies[random.Next(lobbies.Count)];

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" JoinByGameMode({gameMode}): Joining lobby {selectedLobby.JoinCode}");
            return await JoinLobbyByIdAsync(selectedLobby.LobbyId);
        }

        #endregion

        #region Public API - Join

        /// <summary>
        /// Joins a lobby by its join code (custom code or EOS LobbyId).
        /// Automatically detects if the code is an EOS LobbyId and uses direct join.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> JoinLobbyByCodeAsync(string joinCode)
        {
            // If it looks like an EOS LobbyId, try direct join first (faster)
            if (IsEosLobbyIdFormat(joinCode))
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" JoinByCode: Detected EOS LobbyId format, joining directly");
                return await JoinLobbyByIdAsync(joinCode);
            }

            // Standard flow: search by attribute, then join
            var (findResult, lobbyData) = await FindLobbyByCodeAsync(joinCode);

            if (findResult != Result.Success || !lobbyData.HasValue)
            {
                Debug.LogWarning($"[EOSLobbyManager] Lobby with code {joinCode} not found");
                return (findResult == Result.Success ? Result.NotFound : findResult, default);
            }

            return await JoinLobbyByIdAsync(lobbyData.Value.LobbyId);
        }

        /// <summary>
        /// Joins a lobby by its EOS lobby ID.
        /// </summary>
        public async Task<(Result result, LobbyData lobby)> JoinLobbyByIdAsync(string lobbyId)
        {
            if (LobbyInterface == null || LocalProductUserId == null)
            {
                EOSDebugLogger.LogError("EOSLobbyManager", "Not initialized.");
                return (Result.NotConfigured, default);
            }

            // Get lobby details first
            var detailsOptions = new CopyLobbyDetailsHandleOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = lobbyId
            };

            var detailsResult = LobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out LobbyDetails details);

            // If we can't get details directly, the lobby might not be in our cache - that's fine, try to join anyway
            if (detailsResult != Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Couldn't get lobby details ({detailsResult}), attempting direct join...");
            }

            // Join the lobby
            var joinOptions = new JoinLobbyByIdOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = lobbyId,
                PresenceEnabled = false
            };

            var tcs = new TaskCompletionSource<JoinLobbyByIdCallbackInfo>();
            LobbyInterface.JoinLobbyById(ref joinOptions, null, (ref JoinLobbyByIdCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;

            if (result.ResultCode != Result.Success && result.ResultCode != Result.LobbyLobbyAlreadyExists)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to join lobby: {result.ResultCode}");
                details?.Release();
                return (result.ResultCode, default);
            }

            // Get updated lobby data
            var lobbyData = await GetLobbyDataAsync(lobbyId);
            CurrentLobby = lobbyData;

            details?.Release();

            // Subscribe to notifications
            SubscribeToNotifications(lobbyId);

            // Notify voice manager (it will check if RTC room exists)
            EOSVoiceManager.Instance?.OnLobbyJoined(lobbyId);

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Joined lobby: {lobbyData}");
            OnLobbyJoined?.Invoke(CurrentLobby);

            return (Result.Success, CurrentLobby);
        }

        #endregion

        #region Public API - Leave

        /// <summary>
        /// Leaves the current lobby synchronously (fire-and-forget).
        /// Use this when leaving during application quit where async won't complete in time.
        /// </summary>
        public void LeaveLobbySync()
        {
            if (!IsInLobby)
            {
                return;
            }

            // SDK may already be shut down during exit
            if (LobbyInterface == null)
            {
                CurrentLobby = default;
                return;
            }

            try
            {
                var leaveOptions = new LeaveLobbyOptions
                {
                    LocalUserId = LocalProductUserId,
                    LobbyId = CurrentLobby.LobbyId
                };

                // Fire and forget - EOS SDK will send the leave notification to other members
                // Note: EOS requires a non-null callback even if we don't need the result
                LobbyInterface.LeaveLobby(ref leaveOptions, null, (ref LeaveLobbyCallbackInfo data) =>
                {
                    // Intentionally empty - we don't wait for this during sync leave
                });
            }
            catch
            {
                // SDK may be shutting down
            }

            // Unsubscribe from notifications immediately
            UnsubscribeFromNotifications();

            // Notify voice manager
            EOSVoiceManager.Instance?.OnLobbyLeft();

            // Clear current lobby
            CurrentLobby = default;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "Left lobby (sync)");
        }

        /// <summary>
        /// Leaves the current lobby.
        /// </summary>
        public async Task<Result> LeaveLobbyAsync()
        {
            if (!IsInLobby)
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyManager, "EOSLobbyManager", "Not in a lobby.");
                return Result.NotFound;
            }

            string lobbyId = CurrentLobby.LobbyId;

            var leaveOptions = new LeaveLobbyOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = lobbyId
            };

            var tcs = new TaskCompletionSource<LeaveLobbyCallbackInfo>();
            LobbyInterface.LeaveLobby(ref leaveOptions, null, (ref LeaveLobbyCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;

            // Unsubscribe from notifications
            UnsubscribeFromNotifications();

            // Notify voice manager
            EOSVoiceManager.Instance?.OnLobbyLeft();

            // Clear current lobby
            CurrentLobby = default;

            if (result.ResultCode != Result.Success && result.ResultCode != Result.NotFound)
            {
                Debug.LogWarning($"[EOSLobbyManager] Leave lobby result: {result.ResultCode}");
            }

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "Left lobby");
            ResetJoinTracking();
            OnLobbyLeft?.Invoke();

            return result.ResultCode;
        }

        #endregion

        #region Public API - Attributes

        /// <summary>
        /// Sets a lobby attribute (owner only).
        /// </summary>
        public async Task<Result> SetLobbyAttributeAsync(string lobbyId, string key, string value)
        {
            var modifyOptions = new UpdateLobbyModificationOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = lobbyId
            };

            var modifyResult = LobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification modification);
            if (modifyResult != Result.Success)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to create modification: {modifyResult}");
                return modifyResult;
            }

            var addAttrOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = new AttributeData
                {
                    Key = key,
                    Value = new AttributeDataValue { AsUtf8 = value }
                },
                Visibility = LobbyAttributeVisibility.Public
            };

            var addResult = modification.AddAttribute(ref addAttrOptions);
            if (addResult != Result.Success)
            {
                Debug.LogError($"[EOSLobbyManager] Failed to add attribute: {addResult}");
                modification.Release();
                return addResult;
            }

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            var tcs = new TaskCompletionSource<UpdateLobbyCallbackInfo>();
            LobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;
            modification.Release();

            return result.ResultCode;
        }

        /// <summary>
        /// Sets a member attribute on the local player.
        /// </summary>
        public async Task<Result> SetMemberAttributeAsync(string key, string value)
        {
            if (!IsInLobby)
            {
                return Result.NotFound;
            }

            var modifyOptions = new UpdateLobbyModificationOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = CurrentLobby.LobbyId
            };

            var modifyResult = LobbyInterface.UpdateLobbyModification(ref modifyOptions, out LobbyModification modification);
            if (modifyResult != Result.Success)
            {
                return modifyResult;
            }

            var addMemberAttrOptions = new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = new AttributeData
                {
                    Key = key,
                    Value = new AttributeDataValue { AsUtf8 = value }
                },
                Visibility = LobbyAttributeVisibility.Public
            };

            var addResult = modification.AddMemberAttribute(ref addMemberAttrOptions);
            if (addResult != Result.Success)
            {
                modification.Release();
                return addResult;
            }

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            var tcs = new TaskCompletionSource<UpdateLobbyCallbackInfo>();
            LobbyInterface.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var result = await tcs.Task;
            modification.Release();

            return result.ResultCode;
        }

        /// <summary>
        /// Sends a chat message via member attribute.
        /// </summary>
        public async Task<Result> SendChatMessageAsync(string message)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return await SetMemberAttributeAsync(MemberAttributes.CHAT, $"{timestamp}:{message}");
        }

        #endregion

        #region Public API - Utility

        /// <summary>
        /// Generates a random 4-digit join code.
        /// </summary>
        public static string GenerateJoinCode()
        {
            return _random.Next(0, 10000).ToString("D4");
        }

        /// <summary>
        /// Gets the host's ProductUserId for the current lobby.
        /// Use this to set EOSNativeTransport.RemoteProductUserId before connecting.
        /// </summary>
        public string GetHostPuid()
        {
            return CurrentLobby.OwnerPuid;
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Converts our SearchComparison enum to EOS ComparisonOp.
        /// </summary>
        private static ComparisonOp ToEOSComparison(SearchComparison comparison)
        {
            return comparison switch
            {
                SearchComparison.Equal => ComparisonOp.Equal,
                SearchComparison.NotEqual => ComparisonOp.Notequal,
                SearchComparison.GreaterThan => ComparisonOp.Greaterthan,
                SearchComparison.GreaterThanOrEqual => ComparisonOp.Greaterthanorequal,
                SearchComparison.LessThan => ComparisonOp.Lessthan,
                SearchComparison.LessThanOrEqual => ComparisonOp.Lessthanorequal,
                SearchComparison.Contains => ComparisonOp.Contains,
                SearchComparison.AnyOf => ComparisonOp.Anyof,
                SearchComparison.NotAnyOf => ComparisonOp.Notanyof,
                SearchComparison.Distance => ComparisonOp.Distance,
                _ => ComparisonOp.Equal
            };
        }

        /// <summary>
        /// Processes search results and applies client-side filters.
        /// </summary>
        private List<LobbyData> ProcessSearchResults(LobbySearch searchHandle, LobbySearchOptions options)
        {
            var lobbies = new List<LobbyData>();
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = searchHandle.GetSearchResultCount(ref countOptions);

            for (uint i = 0; i < count; i++)
            {
                var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                var copyResult = searchHandle.CopySearchResultByIndex(ref copyOptions, out LobbyDetails details);

                if (copyResult == Result.Success && details != null)
                {
                    var lobbyData = ExtractLobbyData(details);

                    // Filter out self-owned lobbies
                    if (lobbyData.OwnerPuid == LocalPuid)
                    {
                        details.Release();
                        continue;
                    }

                    // Filter out full lobbies if requested
                    if (options.OnlyAvailable && lobbyData.AvailableSlots <= 0)
                    {
                        details.Release();
                        continue;
                    }

                    // Filter out empty lobbies (ghost lobbies)
                    if (lobbyData.MemberCount == 0)
                    {
                        details.Release();
                        continue;
                    }

                    // Filter out password-protected lobbies if requested
                    if (options.ExcludePasswordProtected && lobbyData.IsPasswordProtected)
                    {
                        details.Release();
                        continue;
                    }

                    // Filter out in-progress games if requested
                    if (options.ExcludeInProgress && lobbyData.IsInProgress)
                    {
                        details.Release();
                        continue;
                    }

                    lobbies.Add(lobbyData);
                    details.Release();
                }
            }

            return lobbies;
        }

        /// <summary>
        /// Gets a member attribute value from the current lobby.
        /// </summary>
        private string GetMemberAttribute(ProductUserId memberId, string attrKey)
        {
            if (!IsInLobby || memberId == null) return null;

            var options = new CopyLobbyDetailsHandleOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = CurrentLobby.LobbyId
            };

            var result = LobbyInterface.CopyLobbyDetailsHandle(ref options, out LobbyDetails details);
            if (result != Result.Success || details == null) return null;

            var attrOptions = new LobbyDetailsCopyMemberAttributeByKeyOptions
            {
                TargetUserId = memberId,
                AttrKey = attrKey
            };

            string value = null;
            if (details.CopyMemberAttributeByKey(ref attrOptions, out var attr) == Result.Success
                && attr.HasValue && attr.Value.Data.HasValue)
            {
                var data = attr.Value.Data.Value;
                if (data.Value.ValueType == AttributeType.String)
                {
                    value = data.Value.AsUtf8;
                }
            }

            details.Release();
            return value;
        }

        private async Task<LobbyData> GetLobbyDataAsync(string lobbyId)
        {
            var options = new CopyLobbyDetailsHandleOptions
            {
                LocalUserId = LocalProductUserId,
                LobbyId = lobbyId
            };

            // Small delay to let EOS sync
            await Task.Delay(100);

            var result = LobbyInterface.CopyLobbyDetailsHandle(ref options, out LobbyDetails details);
            if (result != Result.Success || details == null)
            {
                Debug.LogWarning($"[EOSLobbyManager] Failed to get lobby details: {result}");
                return default;
            }

            var lobbyData = ExtractLobbyData(details);
            details.Release();

            return lobbyData;
        }

        private LobbyData ExtractLobbyData(LobbyDetails details)
        {
            var lobbyData = new LobbyData();

            // Get info
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (details.CopyInfo(ref infoOptions, out LobbyDetailsInfo? info) == Result.Success && info.HasValue)
            {
                lobbyData.LobbyId = info.Value.LobbyId;
                lobbyData.MaxMembers = (int)info.Value.MaxMembers;
                lobbyData.AvailableSlots = (int)info.Value.AvailableSlots;
                lobbyData.BucketId = info.Value.BucketId;
                lobbyData.IsPublic = info.Value.PermissionLevel == LobbyPermissionLevel.Publicadvertised;
            }

            // Get owner
            var ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
            var owner = details.GetLobbyOwner(ref ownerOptions);
            lobbyData.OwnerPuid = owner?.ToString();

            // Get member count
            var memberCountOptions = new LobbyDetailsGetMemberCountOptions();
            lobbyData.MemberCount = (int)details.GetMemberCount(ref memberCountOptions);

            // Get attributes
            lobbyData.Attributes = new Dictionary<string, string>();
            var attrCountOptions = new LobbyDetailsGetAttributeCountOptions();
            uint attrCount = details.GetAttributeCount(ref attrCountOptions);

            for (uint i = 0; i < attrCount; i++)
            {
                var copyAttrOptions = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = i };
                if (details.CopyAttributeByIndex(ref copyAttrOptions, out Epic.OnlineServices.Lobby.Attribute? attr) == Result.Success
                    && attr.HasValue && attr.Value.Data.HasValue)
                {
                    var data = attr.Value.Data.Value;
                    if (data.Value.ValueType == AttributeType.String)
                    {
                        lobbyData.Attributes[data.Key] = data.Value.AsUtf8;

                        // Extract join code
                        if (data.Key == LobbyAttributes.JOIN_CODE)
                        {
                            lobbyData.JoinCode = data.Value.AsUtf8;
                        }
                    }
                }
            }

            return lobbyData;
        }

        private void SubscribeToNotifications(string lobbyId)
        {
            UnsubscribeFromNotifications();

            // Lobby update notifications
            var lobbyUpdateOptions = new AddNotifyLobbyUpdateReceivedOptions();
            ulong lobbyUpdateHandle = LobbyInterface.AddNotifyLobbyUpdateReceived(ref lobbyUpdateOptions, null, OnLobbyUpdateReceived);
            _lobbyUpdateHandle = new NotifyEventHandle(lobbyUpdateHandle, h => LobbyInterface?.RemoveNotifyLobbyUpdateReceived(h));

            // Member update notifications
            var memberUpdateOptions = new AddNotifyLobbyMemberUpdateReceivedOptions();
            ulong memberUpdateHandle = LobbyInterface.AddNotifyLobbyMemberUpdateReceived(ref memberUpdateOptions, null, OnMemberUpdateReceived);
            _memberUpdateHandle = new NotifyEventHandle(memberUpdateHandle, h => LobbyInterface?.RemoveNotifyLobbyMemberUpdateReceived(h));

            // Member status notifications (join/leave/promoted)
            var memberStatusOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
            ulong memberStatusHandle = LobbyInterface.AddNotifyLobbyMemberStatusReceived(ref memberStatusOptions, null, OnMemberStatusReceived);
            _memberStatusHandle = new NotifyEventHandle(memberStatusHandle, h => LobbyInterface?.RemoveNotifyLobbyMemberStatusReceived(h));

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "Subscribed to lobby notifications");
        }

        private void UnsubscribeFromNotifications()
        {
            _lobbyUpdateHandle?.Dispose();
            _memberUpdateHandle?.Dispose();
            _memberStatusHandle?.Dispose();

            _lobbyUpdateHandle = null;
            _memberUpdateHandle = null;
            _memberStatusHandle = null;

            // Reset refresh guards
            _isRefreshing = false;
            _refreshPending = false;
        }

        #endregion

        #region Notification Callbacks

        private void OnLobbyUpdateReceived(ref LobbyUpdateReceivedCallbackInfo data)
        {
            if (!IsInLobby || data.LobbyId != CurrentLobby.LobbyId)
                return;

            EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Lobby updated: {data.LobbyId}");

            // Refresh lobby data
            _ = RefreshCurrentLobbyAsync();
        }

        private void OnMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo data)
        {
            if (!IsInLobby || data.LobbyId != CurrentLobby.LobbyId)
                return;

            string memberPuid = data.TargetUserId?.ToString();

            // Read display name attribute
            var displayName = GetMemberAttribute(data.TargetUserId, MemberAttributes.DISPLAY_NAME);
            if (!string.IsNullOrEmpty(displayName))
            {
                OnMemberAttributeUpdated?.Invoke(memberPuid, MemberAttributes.DISPLAY_NAME, displayName);
            }

            // Read chat attribute
            var chatValue = GetMemberAttribute(data.TargetUserId, MemberAttributes.CHAT);
            if (!string.IsNullOrEmpty(chatValue))
            {
                OnMemberAttributeUpdated?.Invoke(memberPuid, MemberAttributes.CHAT, chatValue);
            }
        }

        private void OnMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo data)
        {
            if (!IsInLobby || data.LobbyId != CurrentLobby.LobbyId)
                return;

            string memberPuid = data.TargetUserId?.ToString();

            switch (data.CurrentStatus)
            {
                case LobbyMemberStatus.Joined:
                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Member joined: {memberPuid}");
                    TrackJoin(memberPuid);
                    OnMemberJoined?.Invoke(new LobbyMemberData { Puid = memberPuid });
                    break;

                case LobbyMemberStatus.Left:
                case LobbyMemberStatus.Disconnected:
                case LobbyMemberStatus.Kicked:
                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Member left: {memberPuid} ({data.CurrentStatus})");
                    OnMemberLeft?.Invoke(memberPuid);
                    break;

                case LobbyMemberStatus.Promoted:
                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" New owner: {memberPuid}");
                    var lobby = CurrentLobby;
                    lobby.OwnerPuid = memberPuid;
                    CurrentLobby = lobby;
                    OnOwnerChanged?.Invoke(memberPuid);
                    break;

                case LobbyMemberStatus.Closed:
                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", "Lobby closed");
                    CurrentLobby = default;
                    UnsubscribeFromNotifications();
                    EOSVoiceManager.Instance?.OnLobbyLeft();
                    OnLobbyLeft?.Invoke();
                    break;
            }

            // Refresh lobby data
            _ = RefreshCurrentLobbyAsync();
        }

        private async Task RefreshCurrentLobbyAsync()
        {
            if (!IsInLobby) return;

            // Guard against re-entrant calls (can cause StackOverflow during Tick callbacks)
            if (_isRefreshing)
            {
                _refreshPending = true;
                return;
            }

            _isRefreshing = true;
            _refreshPending = false;

            try
            {
                string lobbyId = CurrentLobby.LobbyId;
                var lobbyData = await GetLobbyDataAsync(lobbyId);

                if (lobbyData.IsValid)
                {
                    CurrentLobby = lobbyData;
                    OnLobbyUpdated?.Invoke(CurrentLobby);
                }
            }
            finally
            {
                _isRefreshing = false;

                // If another refresh was requested while we were refreshing, do one more
                if (_refreshPending && IsInLobby)
                {
                    _refreshPending = false;
                    _ = RefreshCurrentLobbyAsync();
                }
            }
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSLobbyManager))]
    public class EOSLobbyManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSLobbyManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                // Lobby state
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("In Lobby");
                var inLobbyStyle = new GUIStyle(EditorStyles.label);
                inLobbyStyle.normal.textColor = manager.IsInLobby ? Color.green : Color.gray;
                EditorGUILayout.LabelField(manager.IsInLobby ? "Yes" : "No", inLobbyStyle);
                EditorGUILayout.EndHorizontal();

                if (manager.IsInLobby)
                {
                    var lobby = manager.CurrentLobby;

                    // Role
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Role");
                    var roleStyle = new GUIStyle(EditorStyles.label);
                    roleStyle.normal.textColor = manager.IsOwner ? new Color(0.4f, 1f, 0.4f) : new Color(0.4f, 0.8f, 1f);
                    roleStyle.fontStyle = FontStyle.Bold;
                    EditorGUILayout.LabelField(manager.IsOwner ? "HOST" : "CLIENT", roleStyle);
                    EditorGUILayout.EndHorizontal();

                    // Join code (big and prominent)
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel("Join Code");
                    var codeStyle = new GUIStyle(EditorStyles.label);
                    codeStyle.fontSize = 18;
                    codeStyle.fontStyle = FontStyle.Bold;
                    codeStyle.normal.textColor = new Color(0.3f, 1f, 0.5f);
                    EditorGUILayout.LabelField(lobby.JoinCode ?? "????", codeStyle);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.TextField("Lobby ID", lobby.LobbyId?.Substring(0, Mathf.Min(20, lobby.LobbyId?.Length ?? 0)) + "...");
                    EditorGUILayout.IntField("Members", lobby.MemberCount);
                    EditorGUILayout.IntField("Max", lobby.MaxMembers);
                    EditorGUILayout.IntField("Available Slots", lobby.AvailableSlots);

                    // Owner PUID
                    string ownerShort = lobby.OwnerPuid?.Length > 16 ? lobby.OwnerPuid.Substring(0, 8) + "..." : lobby.OwnerPuid;
                    EditorGUILayout.TextField("Owner", ownerShort ?? "(unknown)");

                    // Attributes preview
                    if (lobby.Attributes != null && lobby.Attributes.Count > 0)
                    {
                        EditorGUILayout.Space(5);
                        EditorGUILayout.LabelField($"Attributes ({lobby.Attributes.Count})", EditorStyles.miniLabel);
                        EditorGUI.indentLevel++;
                        int shown = 0;
                        foreach (var kvp in lobby.Attributes)
                        {
                            if (shown >= 5) // Limit display
                            {
                                EditorGUILayout.LabelField($"... and {lobby.Attributes.Count - 5} more", EditorStyles.miniLabel);
                                break;
                            }
                            string val = kvp.Value?.Length > 20 ? kvp.Value.Substring(0, 20) + "..." : kvp.Value;
                            EditorGUILayout.LabelField($"{kvp.Key}: {val}", EditorStyles.miniLabel);
                            shown++;
                        }
                        EditorGUI.indentLevel--;
                    }
                }
            }

            if (Application.isPlaying && manager.IsInLobby)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

                if (GUILayout.Button("Leave Lobby"))
                {
                    _ = manager.LeaveLobbyAsync();
                }

                if (GUILayout.Button("Copy Join Code"))
                {
                    GUIUtility.systemCopyBuffer = manager.CurrentLobby.JoinCode;
                    EOSDebugLogger.Log(DebugCategory.LobbyManager, "EOSLobbyManager", $" Join code copied: {manager.CurrentLobby.JoinCode}");
                }

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see runtime status.", MessageType.Info);
            }
        }
    }
#endif
}
