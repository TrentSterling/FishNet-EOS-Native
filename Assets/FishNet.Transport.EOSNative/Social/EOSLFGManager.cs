using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Types of LFG posts.
    /// </summary>
    public enum LFGType
    {
        /// <summary>Looking for players to join my group.</summary>
        LookingForPlayers,
        /// <summary>Looking for a group to join.</summary>
        LookingForGroup
    }

    /// <summary>
    /// Status of an LFG post.
    /// </summary>
    public enum LFGStatus
    {
        Open,
        Full,
        Closed,
        InGame
    }

    /// <summary>
    /// An LFG post created by a player.
    /// </summary>
    [Serializable]
    public class LFGPost
    {
        /// <summary>Unique ID for this post.</summary>
        public string PostId;

        /// <summary>PUID of the player who created the post.</summary>
        public string OwnerPuid;

        /// <summary>Display name of the owner.</summary>
        public string OwnerName;

        /// <summary>Type of post (LFP or LFG).</summary>
        public LFGType Type;

        /// <summary>Current status of the post.</summary>
        public LFGStatus Status;

        /// <summary>Title/description of what they're looking for.</summary>
        public string Title;

        /// <summary>Game mode they want to play.</summary>
        public string GameMode;

        /// <summary>Region preference.</summary>
        public string Region;

        /// <summary>Minimum skill/rank requirement (0 = any).</summary>
        public int MinRank;

        /// <summary>Maximum skill/rank requirement (0 = any).</summary>
        public int MaxRank;

        /// <summary>Current party size.</summary>
        public int CurrentSize;

        /// <summary>Desired party size.</summary>
        public int DesiredSize;

        /// <summary>Whether voice chat is required.</summary>
        public bool VoiceRequired;

        /// <summary>Whether the post allows cross-platform.</summary>
        public bool CrossPlatform;

        /// <summary>Custom tags for filtering.</summary>
        public List<string> Tags;

        /// <summary>When the post was created (Unix timestamp).</summary>
        public long CreatedAt;

        /// <summary>When the post expires (Unix timestamp).</summary>
        public long ExpiresAt;

        /// <summary>Lobby ID if the post is tied to a lobby.</summary>
        public string LobbyId;

        /// <summary>Lobby code for easy joining.</summary>
        public string LobbyCode;

        /// <summary>Additional custom data.</summary>
        public string CustomData;

        public LFGPost()
        {
            Tags = new List<string>();
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ExpiresAt = CreatedAt + 3600; // 1 hour default
        }

        /// <summary>Check if post has expired.</summary>
        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() > ExpiresAt;

        /// <summary>Check if post is joinable.</summary>
        public bool IsJoinable => Status == LFGStatus.Open && !IsExpired && CurrentSize < DesiredSize;

        /// <summary>Time remaining until expiry.</summary>
        public TimeSpan TimeRemaining => TimeSpan.FromSeconds(Math.Max(0, ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    /// <summary>
    /// A join request for an LFG post.
    /// </summary>
    [Serializable]
    public class LFGJoinRequest
    {
        /// <summary>Unique ID for this request.</summary>
        public string RequestId;

        /// <summary>Post this request is for.</summary>
        public string PostId;

        /// <summary>PUID of the requester.</summary>
        public string RequesterPuid;

        /// <summary>Display name of the requester.</summary>
        public string RequesterName;

        /// <summary>Optional message from the requester.</summary>
        public string Message;

        /// <summary>Platform of the requester.</summary>
        public string Platform;

        /// <summary>When the request was sent.</summary>
        public long SentAt;

        /// <summary>Whether the request was accepted.</summary>
        public bool? Accepted;
    }

    /// <summary>
    /// Options for searching LFG posts.
    /// </summary>
    public class LFGSearchOptions
    {
        public LFGType? Type;
        public string GameMode;
        public string Region;
        public int? MinRank;
        public int? MaxRank;
        public bool? VoiceRequired;
        public bool? CrossPlatform;
        public List<string> Tags;
        public int MaxResults = 50;
        public bool ExcludeFull = true;
        public bool ExcludeExpired = true;

        public LFGSearchOptions WithGameMode(string mode)
        {
            GameMode = mode;
            return this;
        }

        public LFGSearchOptions WithRegion(string region)
        {
            Region = region;
            return this;
        }

        public LFGSearchOptions WithRankRange(int min, int max)
        {
            MinRank = min;
            MaxRank = max;
            return this;
        }

        public LFGSearchOptions WithTags(params string[] tags)
        {
            Tags = new List<string>(tags);
            return this;
        }

        public LFGSearchOptions RequiresVoice(bool required = true)
        {
            VoiceRequired = required;
            return this;
        }

        public LFGSearchOptions AllowCrossPlay(bool allow = true)
        {
            CrossPlatform = allow;
            return this;
        }
    }

    /// <summary>
    /// Options for creating an LFG post.
    /// </summary>
    public class LFGPostOptions
    {
        public LFGType Type = LFGType.LookingForPlayers;
        public string Title = "Looking for players";
        public string GameMode;
        public string Region;
        public int MinRank;
        public int MaxRank;
        public int DesiredSize = 4;
        public bool VoiceRequired;
        public bool CrossPlatform = true;
        public List<string> Tags;
        public int DurationMinutes = 60;
        public string CustomData;

        public LFGPostOptions WithTitle(string title)
        {
            Title = title;
            return this;
        }

        public LFGPostOptions WithGameMode(string mode)
        {
            GameMode = mode;
            return this;
        }

        public LFGPostOptions WithRegion(string region)
        {
            Region = region;
            return this;
        }

        public LFGPostOptions WithDesiredSize(int size)
        {
            DesiredSize = Mathf.Clamp(size, 2, 64);
            return this;
        }

        public LFGPostOptions WithRankRange(int min, int max)
        {
            MinRank = min;
            MaxRank = max;
            return this;
        }

        public LFGPostOptions WithTags(params string[] tags)
        {
            Tags = new List<string>(tags);
            return this;
        }

        public LFGPostOptions RequiresVoice(bool required = true)
        {
            VoiceRequired = required;
            return this;
        }

        public LFGPostOptions AllowCrossPlay(bool allow = true)
        {
            CrossPlatform = allow;
            return this;
        }

        public LFGPostOptions WithDuration(int minutes)
        {
            DurationMinutes = Mathf.Clamp(minutes, 5, 480);
            return this;
        }
    }

    /// <summary>
    /// Manages LFG (Looking for Group) posts and requests.
    /// Posts are stored as special EOS lobbies with LFG attributes.
    /// </summary>
    public class EOSLFGManager : MonoBehaviour
    {
        #region Singleton

        private static EOSLFGManager _instance;

        public static EOSLFGManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSLFGManager>();
#else
                    _instance = FindObjectOfType<EOSLFGManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when a new LFG post is created by this player.</summary>
        public event Action<LFGPost> OnPostCreated;

        /// <summary>Fired when our post is updated.</summary>
        public event Action<LFGPost> OnPostUpdated;

        /// <summary>Fired when our post is closed/deleted.</summary>
        public event Action OnPostClosed;

        /// <summary>Fired when we receive a join request for our post.</summary>
        public event Action<LFGJoinRequest> OnJoinRequestReceived;

        /// <summary>Fired when our join request is accepted.</summary>
        public event Action<LFGPost> OnJoinRequestAccepted;

        /// <summary>Fired when our join request is rejected.</summary>
        public event Action<string> OnJoinRequestRejected;

        /// <summary>Fired when search results are available.</summary>
        public event Action<List<LFGPost>> OnSearchResultsReceived;

        #endregion

        #region Inspector Settings

        [Header("Settings")]
        [Tooltip("Default post duration in minutes")]
        [SerializeField] private int _defaultDurationMinutes = 60;

        [Tooltip("How often to refresh search results (seconds)")]
        [SerializeField] private float _refreshInterval = 30f;

        [Tooltip("Auto-refresh active searches")]
        [SerializeField] private bool _autoRefresh = true;

        [Tooltip("Show toast notifications for LFG events")]
        [SerializeField] private bool _showToasts = true;

        #endregion

        #region Public Properties

        /// <summary>Our currently active LFG post (if any).</summary>
        public LFGPost ActivePost { get; private set; }

        /// <summary>Whether we have an active post.</summary>
        public bool HasActivePost => ActivePost != null && !ActivePost.IsExpired;

        /// <summary>Pending join requests for our post.</summary>
        public List<LFGJoinRequest> PendingRequests { get; } = new();

        /// <summary>Last search results.</summary>
        public List<LFGPost> SearchResults { get; } = new();

        /// <summary>Posts we've requested to join.</summary>
        public List<string> SentRequests { get; } = new();

        #endregion

        #region Private Fields

        private float _lastRefreshTime;
        private LFGSearchOptions _lastSearchOptions;
        private LobbyInterface Lobby => EOSManager.Instance?.LobbyInterface;
        private ProductUserId LocalUserId => EOSManager.Instance?.LocalProductUserId;

        // LFG posts are stored as special lobbies with these attribute keys
        private const string ATTR_LFG_TYPE = "lfg_type";
        private const string ATTR_LFG_TITLE = "lfg_title";
        private const string ATTR_LFG_GAMEMODE = "lfg_mode";
        private const string ATTR_LFG_REGION = "lfg_region";
        private const string ATTR_LFG_MINRANK = "lfg_minrank";
        private const string ATTR_LFG_MAXRANK = "lfg_maxrank";
        private const string ATTR_LFG_SIZE = "lfg_size";
        private const string ATTR_LFG_VOICE = "lfg_voice";
        private const string ATTR_LFG_CROSSPLAY = "lfg_cross";
        private const string ATTR_LFG_TAGS = "lfg_tags";
        private const string ATTR_LFG_EXPIRES = "lfg_expires";
        private const string ATTR_LFG_CUSTOM = "lfg_custom";
        private const string ATTR_LFG_STATUS = "lfg_status";

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
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            // Auto-refresh searches
            if (_autoRefresh && _lastSearchOptions != null)
            {
                if (Time.time - _lastRefreshTime >= _refreshInterval)
                {
                    _lastRefreshTime = Time.time;
                    _ = RefreshSearchAsync();
                }
            }

            // Check for expired post
            if (ActivePost != null && ActivePost.IsExpired)
            {
                EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", "Active post expired");
                _ = ClosePostAsync();
            }
        }

        #endregion

        #region Public API - Create Post

        /// <summary>
        /// Create a new LFG post.
        /// </summary>
        public async Task<(Result, LFGPost)> CreatePostAsync(LFGPostOptions options = null)
        {
            options ??= new LFGPostOptions();

            if (Lobby == null || LocalUserId == null)
            {
                EOSDebugLogger.LogError("EOSLFGManager", "EOS not initialized");
                return (Result.NotConfigured, null);
            }

            if (HasActivePost)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Social, "EOSLFGManager", "Already have an active post");
                return (Result.AlreadyPending, null);
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", $"Creating LFG post: {options.Title}");

            // Create a special lobby to store the LFG post
            var tcs = new TaskCompletionSource<(Result, string)>();

            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = LocalUserId,
                MaxLobbyMembers = (uint)options.DesiredSize,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                BucketId = "lfg",
                EnableRTCRoom = false,
                bPresenceEnabled = true,
                bAllowInvites = true
            };

            Lobby.CreateLobby(ref createOptions, null, (ref CreateLobbyCallbackInfo data) =>
            {
                tcs.SetResult((data.ResultCode, data.LobbyId));
            });

            var (result, lobbyId) = await tcs.Task;
            if (result != Result.Success)
            {
                EOSDebugLogger.LogError("EOSLFGManager", $"Failed to create LFG lobby: {result}");
                return (result, null);
            }

            // Set LFG attributes
            var modifyResult = await SetLFGAttributesAsync(lobbyId, options);
            if (modifyResult != Result.Success)
            {
                // Clean up failed lobby
                await DeleteLobbyAsync(lobbyId);
                return (modifyResult, null);
            }

            // Create post object
            var registry = EOSPlayerRegistry.Instance;
            string localPuid = LocalUserId.ToString();

            var post = new LFGPost
            {
                PostId = lobbyId,
                OwnerPuid = localPuid,
                OwnerName = registry?.GetDisplayName(localPuid) ?? "Unknown",
                Type = options.Type,
                Status = LFGStatus.Open,
                Title = options.Title,
                GameMode = options.GameMode,
                Region = options.Region,
                MinRank = options.MinRank,
                MaxRank = options.MaxRank,
                CurrentSize = 1,
                DesiredSize = options.DesiredSize,
                VoiceRequired = options.VoiceRequired,
                CrossPlatform = options.CrossPlatform,
                Tags = options.Tags ?? new List<string>(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (options.DurationMinutes * 60),
                LobbyId = lobbyId,
                CustomData = options.CustomData
            };

            ActivePost = post;
            OnPostCreated?.Invoke(post);

            if (_showToasts)
                EOSToastManager.Success("LFG Post Created", options.Title);

            EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", $"Created post: {post.PostId}");
            return (Result.Success, post);
        }

        /// <summary>
        /// Create a simple LFG post with just a title.
        /// </summary>
        public Task<(Result, LFGPost)> CreatePostAsync(string title)
        {
            return CreatePostAsync(new LFGPostOptions { Title = title });
        }

        #endregion

        #region Public API - Update Post

        /// <summary>
        /// Update the active post's status.
        /// </summary>
        public async Task<Result> UpdatePostStatusAsync(LFGStatus status)
        {
            if (ActivePost == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Social, "EOSLFGManager", "No active post to update");
                return Result.NotFound;
            }

            var result = await SetAttributeAsync(ActivePost.PostId, ATTR_LFG_STATUS, ((int)status).ToString());
            if (result == Result.Success)
            {
                ActivePost.Status = status;
                OnPostUpdated?.Invoke(ActivePost);
            }

            return result;
        }

        /// <summary>
        /// Update the active post's current size.
        /// </summary>
        public async Task<Result> UpdatePostSizeAsync(int currentSize)
        {
            if (ActivePost == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Social, "EOSLFGManager", "No active post to update");
                return Result.NotFound;
            }

            ActivePost.CurrentSize = currentSize;

            // Auto-update status
            if (currentSize >= ActivePost.DesiredSize && ActivePost.Status == LFGStatus.Open)
            {
                await UpdatePostStatusAsync(LFGStatus.Full);
            }
            else if (currentSize < ActivePost.DesiredSize && ActivePost.Status == LFGStatus.Full)
            {
                await UpdatePostStatusAsync(LFGStatus.Open);
            }

            OnPostUpdated?.Invoke(ActivePost);
            return Result.Success;
        }

        /// <summary>
        /// Close/delete the active post.
        /// </summary>
        public async Task<Result> ClosePostAsync()
        {
            if (ActivePost == null)
            {
                return Result.NotFound;
            }

            var result = await DeleteLobbyAsync(ActivePost.PostId);

            ActivePost = null;
            PendingRequests.Clear();
            OnPostClosed?.Invoke();

            if (_showToasts)
                EOSToastManager.Info("LFG Post Closed");

            return result;
        }

        #endregion

        #region Public API - Search Posts

        /// <summary>
        /// Search for LFG posts matching the given options.
        /// </summary>
        public async Task<(Result, List<LFGPost>)> SearchPostsAsync(LFGSearchOptions options = null)
        {
            options ??= new LFGSearchOptions();
            _lastSearchOptions = options;
            _lastRefreshTime = Time.time;

            if (Lobby == null || LocalUserId == null)
            {
                EOSDebugLogger.LogError("EOSLFGManager", "EOS not initialized");
                return (Result.NotConfigured, new List<LFGPost>());
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", "Searching for LFG posts...");

            // Create search handle
            var createOptions = new CreateLobbySearchOptions
            {
                MaxResults = (uint)options.MaxResults
            };

            var createResult = Lobby.CreateLobbySearch(ref createOptions, out var searchHandle);
            if (createResult != Result.Success)
            {
                EOSDebugLogger.LogError("EOSLFGManager", $"Failed to create search: {createResult}");
                return (createResult, new List<LFGPost>());
            }

            try
            {
                // Add search parameters
                AddSearchParameter(searchHandle, "bucket", "lfg", ComparisonOp.Equal);

                if (options.Type.HasValue)
                    AddSearchParameter(searchHandle, ATTR_LFG_TYPE, ((int)options.Type.Value).ToString(), ComparisonOp.Equal);

                if (!string.IsNullOrEmpty(options.GameMode))
                    AddSearchParameter(searchHandle, ATTR_LFG_GAMEMODE, options.GameMode, ComparisonOp.Equal);

                if (!string.IsNullOrEmpty(options.Region))
                    AddSearchParameter(searchHandle, ATTR_LFG_REGION, options.Region, ComparisonOp.Equal);

                if (options.VoiceRequired.HasValue)
                    AddSearchParameter(searchHandle, ATTR_LFG_VOICE, options.VoiceRequired.Value ? "1" : "0", ComparisonOp.Equal);

                if (options.CrossPlatform.HasValue)
                    AddSearchParameter(searchHandle, ATTR_LFG_CROSSPLAY, options.CrossPlatform.Value ? "1" : "0", ComparisonOp.Equal);

                if (options.ExcludeFull)
                    AddSearchParameter(searchHandle, ATTR_LFG_STATUS, "0", ComparisonOp.Equal); // Open = 0

                // Execute search
                var tcs = new TaskCompletionSource<Result>();
                var findOptions = new LobbySearchFindOptions
                {
                    LocalUserId = LocalUserId
                };

                searchHandle.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo data) =>
                {
                    tcs.SetResult(data.ResultCode);
                });

                var findResult = await tcs.Task;
                if (findResult != Result.Success)
                {
                    EOSDebugLogger.LogError("EOSLFGManager", $"Search failed: {findResult}");
                    return (findResult, new List<LFGPost>());
                }

                // Get results count
                var countOptions = new LobbySearchGetSearchResultCountOptions();
                uint count = searchHandle.GetSearchResultCount(ref countOptions);

                var posts = new List<LFGPost>();

                for (uint i = 0; i < count; i++)
                {
                    var copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    var copyResult = searchHandle.CopySearchResultByIndex(ref copyOptions, out var lobbyDetails);

                    if (copyResult == Result.Success && lobbyDetails != null)
                    {
                        var post = ParseLFGPost(lobbyDetails);
                        if (post != null)
                        {
                            // Filter expired posts
                            if (options.ExcludeExpired && post.IsExpired)
                                continue;

                            // Filter by rank range
                            if (options.MinRank.HasValue || options.MaxRank.HasValue)
                            {
                                // Skip if our rank doesn't fit their requirements
                                // (This is a client-side filter - server doesn't know our rank)
                            }

                            posts.Add(post);
                        }

                        lobbyDetails.Release();
                    }
                }

                SearchResults.Clear();
                SearchResults.AddRange(posts);
                OnSearchResultsReceived?.Invoke(posts);

                EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", $"Found {posts.Count} LFG posts");
                return (Result.Success, posts);
            }
            finally
            {
                searchHandle.Release();
            }
        }

        /// <summary>
        /// Search for LFG posts by game mode.
        /// </summary>
        public Task<(Result, List<LFGPost>)> SearchByGameModeAsync(string gameMode)
        {
            return SearchPostsAsync(new LFGSearchOptions { GameMode = gameMode });
        }

        /// <summary>
        /// Refresh the last search.
        /// </summary>
        public Task<(Result, List<LFGPost>)> RefreshSearchAsync()
        {
            return SearchPostsAsync(_lastSearchOptions);
        }

        /// <summary>
        /// Stop auto-refreshing searches.
        /// </summary>
        public void StopAutoRefresh()
        {
            _lastSearchOptions = null;
        }

        #endregion

        #region Public API - Join Requests

        /// <summary>
        /// Send a join request to an LFG post.
        /// </summary>
        public async Task<Result> SendJoinRequestAsync(string postId, string message = null)
        {
            if (string.IsNullOrEmpty(postId))
            {
                return Result.InvalidParameters;
            }

            if (SentRequests.Contains(postId))
            {
                EOSDebugLogger.LogWarning(DebugCategory.Social, "EOSLFGManager", "Already sent request to this post");
                return Result.AlreadyPending;
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", $"Sending join request to: {postId}");

            // Join the LFG lobby (request to join)
            var tcs = new TaskCompletionSource<Result>();

            var joinOptions = new JoinLobbyByIdOptions
            {
                LobbyId = postId,
                LocalUserId = LocalUserId,
                bPresenceEnabled = false
            };

            Lobby.JoinLobbyById(ref joinOptions, null, (ref JoinLobbyByIdCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            var result = await tcs.Task;
            if (result == Result.Success)
            {
                SentRequests.Add(postId);
                if (_showToasts)
                    EOSToastManager.Info("Join Request Sent");
            }
            else
            {
                EOSDebugLogger.LogWarning(DebugCategory.Social, "EOSLFGManager", $"Join request failed: {result}");
            }

            return result;
        }

        /// <summary>
        /// Cancel a pending join request.
        /// </summary>
        public async Task<Result> CancelJoinRequestAsync(string postId)
        {
            if (!SentRequests.Contains(postId))
            {
                return Result.NotFound;
            }

            // Leave the lobby
            var tcs = new TaskCompletionSource<Result>();

            var leaveOptions = new LeaveLobbyOptions
            {
                LobbyId = postId,
                LocalUserId = LocalUserId
            };

            Lobby.LeaveLobby(ref leaveOptions, null, (ref LeaveLobbyCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            var result = await tcs.Task;
            if (result == Result.Success)
            {
                SentRequests.Remove(postId);
            }

            return result;
        }

        /// <summary>
        /// Accept a join request for our post.
        /// </summary>
        public async Task<Result> AcceptJoinRequestAsync(LFGJoinRequest request)
        {
            if (ActivePost == null || request == null)
            {
                return Result.InvalidParameters;
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", $"Accepting request from: {request.RequesterPuid}");

            // The requester is already in the lobby (joined when sending request)
            // We just need to mark them as accepted and update our post

            request.Accepted = true;
            PendingRequests.Remove(request);

            // Update post size
            ActivePost.CurrentSize++;
            await UpdatePostSizeAsync(ActivePost.CurrentSize);

            if (_showToasts)
                EOSToastManager.Success("Request Accepted", request.RequesterName);

            // TODO: Send custom invite or notification to inform them they're accepted

            return Result.Success;
        }

        /// <summary>
        /// Reject a join request for our post.
        /// </summary>
        public async Task<Result> RejectJoinRequestAsync(LFGJoinRequest request)
        {
            if (ActivePost == null || request == null)
            {
                return Result.InvalidParameters;
            }

            EOSDebugLogger.Log(DebugCategory.Social, "EOSLFGManager", $"Rejecting request from: {request.RequesterPuid}");

            request.Accepted = false;
            PendingRequests.Remove(request);

            // Kick them from the LFG lobby
            var kickResult = await KickMemberAsync(ActivePost.PostId, request.RequesterPuid);

            if (_showToasts)
                EOSToastManager.Info("Request Rejected");

            return kickResult;
        }

        #endregion

        #region Helper Methods

        private async Task<Result> SetLFGAttributesAsync(string lobbyId, LFGPostOptions options)
        {
            var tcs = new TaskCompletionSource<Result>();

            var modifyOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = lobbyId,
                LocalUserId = LocalUserId
            };

            var modifyResult = Lobby.UpdateLobbyModification(ref modifyOptions, out var modification);
            if (modifyResult != Result.Success)
            {
                return modifyResult;
            }

            try
            {
                // Add all LFG attributes
                AddAttribute(modification, ATTR_LFG_TYPE, ((int)options.Type).ToString());
                AddAttribute(modification, ATTR_LFG_TITLE, options.Title ?? "LFG");
                AddAttribute(modification, ATTR_LFG_SIZE, options.DesiredSize.ToString());
                AddAttribute(modification, ATTR_LFG_STATUS, "0"); // Open
                AddAttribute(modification, ATTR_LFG_VOICE, options.VoiceRequired ? "1" : "0");
                AddAttribute(modification, ATTR_LFG_CROSSPLAY, options.CrossPlatform ? "1" : "0");
                AddAttribute(modification, ATTR_LFG_EXPIRES, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + options.DurationMinutes * 60).ToString());

                if (!string.IsNullOrEmpty(options.GameMode))
                    AddAttribute(modification, ATTR_LFG_GAMEMODE, options.GameMode);

                if (!string.IsNullOrEmpty(options.Region))
                    AddAttribute(modification, ATTR_LFG_REGION, options.Region);

                if (options.MinRank > 0)
                    AddAttribute(modification, ATTR_LFG_MINRANK, options.MinRank.ToString());

                if (options.MaxRank > 0)
                    AddAttribute(modification, ATTR_LFG_MAXRANK, options.MaxRank.ToString());

                if (options.Tags != null && options.Tags.Count > 0)
                    AddAttribute(modification, ATTR_LFG_TAGS, string.Join(",", options.Tags));

                if (!string.IsNullOrEmpty(options.CustomData))
                    AddAttribute(modification, ATTR_LFG_CUSTOM, options.CustomData);

                // Apply changes
                var updateOptions = new UpdateLobbyOptions
                {
                    LobbyModificationHandle = modification
                };

                Lobby.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
                {
                    tcs.SetResult(data.ResultCode);
                });

                return await tcs.Task;
            }
            finally
            {
                modification.Release();
            }
        }

        private async Task<Result> SetAttributeAsync(string lobbyId, string key, string value)
        {
            var tcs = new TaskCompletionSource<Result>();

            var modifyOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = lobbyId,
                LocalUserId = LocalUserId
            };

            var modifyResult = Lobby.UpdateLobbyModification(ref modifyOptions, out var modification);
            if (modifyResult != Result.Success)
            {
                return modifyResult;
            }

            try
            {
                AddAttribute(modification, key, value);

                var updateOptions = new UpdateLobbyOptions
                {
                    LobbyModificationHandle = modification
                };

                Lobby.UpdateLobby(ref updateOptions, null, (ref UpdateLobbyCallbackInfo data) =>
                {
                    tcs.SetResult(data.ResultCode);
                });

                return await tcs.Task;
            }
            finally
            {
                modification.Release();
            }
        }

        private void AddAttribute(LobbyModification modification, string key, string value)
        {
            var attr = new AttributeData
            {
                Key = key,
                Value = new AttributeDataValue { AsUtf8 = value }
            };

            var addOptions = new LobbyModificationAddAttributeOptions
            {
                Attribute = attr,
                Visibility = LobbyAttributeVisibility.Public
            };

            modification.AddAttribute(ref addOptions);
        }

        private void AddSearchParameter(LobbySearch search, string key, string value, ComparisonOp op)
        {
            var attr = new AttributeData
            {
                Key = key,
                Value = new AttributeDataValue { AsUtf8 = value }
            };

            var paramOptions = new LobbySearchSetParameterOptions
            {
                Parameter = attr,
                ComparisonOp = op
            };

            search.SetParameter(ref paramOptions);
        }

        private LFGPost ParseLFGPost(LobbyDetails details)
        {
            try
            {
                var infoOptions = new LobbyDetailsCopyInfoOptions();
                var infoResult = details.CopyInfo(ref infoOptions, out var info);
                if (infoResult != Result.Success || !info.HasValue)
                    return null;

                var post = new LFGPost
                {
                    PostId = info.Value.LobbyId,
                    LobbyId = info.Value.LobbyId,
                    OwnerPuid = info.Value.LobbyOwnerUserId?.ToString(),
                    CurrentSize = (int)info.Value.AvailableSlots > 0 ? (int)(info.Value.MaxMembers - info.Value.AvailableSlots) : 1,
                    DesiredSize = (int)info.Value.MaxMembers
                };

                // Parse attributes
                var countOptions = new LobbyDetailsGetAttributeCountOptions();
                uint attrCount = details.GetAttributeCount(ref countOptions);

                for (uint i = 0; i < attrCount; i++)
                {
                    var copyOptions = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = i };
                    var copyResult = details.CopyAttributeByIndex(ref copyOptions, out var attr);

                    if (copyResult == Result.Success && attr.HasValue)
                    {
                        string key = attr.Value.Data?.Key;
                        string value = attr.Value.Data?.Value.AsUtf8;

                        if (string.IsNullOrEmpty(key)) continue;

                        switch (key)
                        {
                            case ATTR_LFG_TYPE:
                                if (int.TryParse(value, out int type))
                                    post.Type = (LFGType)type;
                                break;
                            case ATTR_LFG_TITLE:
                                post.Title = value;
                                break;
                            case ATTR_LFG_GAMEMODE:
                                post.GameMode = value;
                                break;
                            case ATTR_LFG_REGION:
                                post.Region = value;
                                break;
                            case ATTR_LFG_MINRANK:
                                if (int.TryParse(value, out int minRank))
                                    post.MinRank = minRank;
                                break;
                            case ATTR_LFG_MAXRANK:
                                if (int.TryParse(value, out int maxRank))
                                    post.MaxRank = maxRank;
                                break;
                            case ATTR_LFG_SIZE:
                                if (int.TryParse(value, out int size))
                                    post.DesiredSize = size;
                                break;
                            case ATTR_LFG_VOICE:
                                post.VoiceRequired = value == "1";
                                break;
                            case ATTR_LFG_CROSSPLAY:
                                post.CrossPlatform = value == "1";
                                break;
                            case ATTR_LFG_TAGS:
                                if (!string.IsNullOrEmpty(value))
                                    post.Tags = new List<string>(value.Split(','));
                                break;
                            case ATTR_LFG_EXPIRES:
                                if (long.TryParse(value, out long expires))
                                    post.ExpiresAt = expires;
                                break;
                            case ATTR_LFG_STATUS:
                                if (int.TryParse(value, out int status))
                                    post.Status = (LFGStatus)status;
                                break;
                            case ATTR_LFG_CUSTOM:
                                post.CustomData = value;
                                break;
                        }
                    }
                }

                // Get owner name
                var registry = EOSPlayerRegistry.Instance;
                if (registry != null && !string.IsNullOrEmpty(post.OwnerPuid))
                {
                    post.OwnerName = registry.GetDisplayName(post.OwnerPuid);
                }

                return post;
            }
            catch (Exception e)
            {
                EOSDebugLogger.LogError("EOSLFGManager", $"Failed to parse LFG post: {e.Message}");
                return null;
            }
        }

        private async Task<Result> DeleteLobbyAsync(string lobbyId)
        {
            var tcs = new TaskCompletionSource<Result>();

            var destroyOptions = new DestroyLobbyOptions
            {
                LobbyId = lobbyId,
                LocalUserId = LocalUserId
            };

            Lobby.DestroyLobby(ref destroyOptions, null, (ref DestroyLobbyCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            return await tcs.Task;
        }

        private async Task<Result> KickMemberAsync(string lobbyId, string targetPuid)
        {
            var tcs = new TaskCompletionSource<Result>();

            var targetId = ProductUserId.FromString(targetPuid);
            if (targetId == null || !targetId.IsValid())
                return Result.InvalidProductUserID;

            var kickOptions = new KickMemberOptions
            {
                LobbyId = lobbyId,
                LocalUserId = LocalUserId,
                TargetUserId = targetId
            };

            Lobby.KickMember(ref kickOptions, null, (ref KickMemberCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            return await tcs.Task;
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSLFGManager))]
    public class EOSLFGManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSLFGManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Has Active Post", manager.HasActivePost);
                if (manager.ActivePost != null)
                {
                    EditorGUILayout.TextField("Post Title", manager.ActivePost.Title);
                    EditorGUILayout.TextField("Status", manager.ActivePost.Status.ToString());
                    EditorGUILayout.TextField("Size", $"{manager.ActivePost.CurrentSize}/{manager.ActivePost.DesiredSize}");
                }

                EditorGUILayout.IntField("Pending Requests", manager.PendingRequests.Count);
                EditorGUILayout.IntField("Search Results", manager.SearchResults.Count);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Search All"))
                {
                    _ = manager.SearchPostsAsync();
                }
                if (manager.HasActivePost && GUILayout.Button("Close Post"))
                {
                    _ = manager.ClosePostAsync();
                }
                EditorGUILayout.EndHorizontal();

                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
