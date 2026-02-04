using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Manages persistent clans/teams with membership, roles, and chat.
    /// </summary>
    public class EOSClanManager : MonoBehaviour
    {
        #region Singleton

        private static EOSClanManager _instance;
        public static EOSClanManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSClanManager>();
#else
                    _instance = FindObjectOfType<EOSClanManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string CLAN_DATA_FILE = "clan_data.json";
        private const string CLAN_REGISTRY_FILE = "clan_registry.json";
        private const int DATA_VERSION = 1;
        private const int MAX_CLAN_TAG_LENGTH = 6;
        private const int MAX_CLAN_NAME_LENGTH = 32;
        private const int MAX_CHAT_HISTORY = 100;

        #endregion

        #region Events

        /// <summary>Fired when a clan is created.</summary>
        public event Action<Clan> OnClanCreated;

        /// <summary>Fired when joining a clan.</summary>
        public event Action<Clan> OnClanJoined;

        /// <summary>Fired when leaving a clan.</summary>
        public event Action OnClanLeft;

        /// <summary>Fired when clan data is updated.</summary>
        public event Action<Clan> OnClanUpdated;

        /// <summary>Fired when a member joins the clan.</summary>
        public event Action<ClanMember> OnMemberJoined;

        /// <summary>Fired when a member leaves the clan.</summary>
        public event Action<string> OnMemberLeft; // puid

        /// <summary>Fired when member role changes.</summary>
        public event Action<string, ClanRole> OnMemberRoleChanged;

        /// <summary>Fired when receiving a clan invite.</summary>
        public event Action<ClanInvite> OnInviteReceived;

        /// <summary>Fired when receiving a join request.</summary>
        public event Action<ClanJoinRequest> OnJoinRequestReceived;

        /// <summary>Fired when clan chat message received.</summary>
        public event Action<ClanChatMessage> OnChatMessageReceived;

        /// <summary>Fired when clan data is loaded.</summary>
        public event Action<ClanPlayerData> OnDataLoaded;

        #endregion

        #region Inspector Settings

        [Header("Clan Settings")]
        [Tooltip("Maximum members per clan")]
        [SerializeField] private int _maxClanSize = 50;

        [Tooltip("Allow open join without invite")]
        [SerializeField] private bool _defaultAllowOpenJoin = false;

        [Tooltip("Require approval for join requests")]
        [SerializeField] private bool _defaultRequireApproval = true;

        [Header("Limits")]
        [Tooltip("Maximum pending invites")]
        [SerializeField] private int _maxPendingInvites = 10;

        [Tooltip("Maximum pending join requests")]
        [SerializeField] private int _maxPendingRequests = 20;

        [Tooltip("Invite expiry time in hours")]
        [SerializeField] private int _inviteExpiryHours = 72;

        #endregion

        #region Public Properties

        /// <summary>Local player's clan data.</summary>
        public ClanPlayerData PlayerData { get; private set; }

        /// <summary>Current clan (if any).</summary>
        public Clan CurrentClan => PlayerData?.CurrentClan;

        /// <summary>Whether player is in a clan.</summary>
        public bool IsInClan => CurrentClan != null;

        /// <summary>Local player's role in clan.</summary>
        public ClanRole MyRole => GetMyRole();

        /// <summary>Whether player is clan leader.</summary>
        public bool IsLeader => MyRole == ClanRole.Leader;

        /// <summary>Whether player is officer or higher.</summary>
        public bool IsOfficer => MyRole >= ClanRole.Officer;

        /// <summary>Whether data is loaded.</summary>
        public bool IsDataLoaded { get; private set; }

        /// <summary>Pending invites to clans.</summary>
        public IReadOnlyList<ClanInvite> PendingInvites => PlayerData?.PendingInvites ?? new List<ClanInvite>();

        /// <summary>Pending join requests (if leader/officer).</summary>
        public IReadOnlyList<ClanJoinRequest> PendingRequests => CurrentClan?.PendingRequests ?? new List<ClanJoinRequest>();

        /// <summary>Clan chat messages.</summary>
        public IReadOnlyList<ClanChatMessage> ChatHistory => CurrentClan?.ChatHistory ?? new List<ClanChatMessage>();

        #endregion

        #region Private Fields

        private bool _isDirty;
        private string _localPuid;

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

        private void Start()
        {
            StartCoroutine(InitializeCoroutine());
        }

        private IEnumerator InitializeCoroutine()
        {
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            _localPuid = EOSManager.Instance.LocalProductUserId?.ToString();
            _ = LoadClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager", "Initialized");
        }

        private void OnDestroy()
        {
            if (_isDirty)
            {
                _ = SaveClanDataAsync();
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Data

        /// <summary>
        /// Load clan data from cloud storage.
        /// </summary>
        public async Task<Result> LoadClanDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
            {
                InitializeDefaultData();
                return Result.NotConfigured;
            }

            try
            {
                var (result, data) = await storage.ReadFileAsJsonAsync<ClanDataContainer>(CLAN_DATA_FILE);

                if (result == Result.Success)
                {
                    PlayerData = data.PlayerData;
                    IsDataLoaded = true;

                    // Clean up expired invites
                    CleanupExpiredInvites();

                    EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                        $"Loaded clan data: {(IsInClan ? CurrentClan.Name : "No clan")}");

                    OnDataLoaded?.Invoke(PlayerData);
                    return Result.Success;
                }
                else if (result == Result.NotFound)
                {
                    InitializeDefaultData();
                    return Result.Success;
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSClanManager] Failed to load data: {e.Message}");
                InitializeDefaultData();
                return Result.UnexpectedError;
            }
        }

        /// <summary>
        /// Save clan data to cloud storage.
        /// </summary>
        public async Task<Result> SaveClanDataAsync()
        {
            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady)
                return Result.NotConfigured;

            try
            {
                var container = new ClanDataContainer
                {
                    Version = DATA_VERSION,
                    PlayerData = PlayerData,
                    SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var result = await storage.WriteFileAsJsonAsync(CLAN_DATA_FILE, container);
                if (result == Result.Success)
                {
                    _isDirty = false;
                    EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager", "Saved clan data");
                }
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSClanManager] Failed to save data: {e.Message}");
                return Result.UnexpectedError;
            }
        }

        private void InitializeDefaultData()
        {
            PlayerData = new ClanPlayerData
            {
                PendingInvites = new List<ClanInvite>(),
                ClanHistory = new List<ClanHistoryEntry>(),
                Version = DATA_VERSION
            };
            IsDataLoaded = true;
        }

        #endregion

        #region Public API - Clan Management

        /// <summary>
        /// Create a new clan.
        /// </summary>
        public async Task<(Result result, Clan clan)> CreateClanAsync(string name, string tag, string description = null)
        {
            if (IsInClan)
            {
                EOSDebugLogger.LogWarning("EOSClanManager", "Already in a clan");
                return (Result.AlreadyPending, null);
            }

            if (string.IsNullOrEmpty(name) || name.Length > MAX_CLAN_NAME_LENGTH)
            {
                return (Result.InvalidParameters, null);
            }

            if (string.IsNullOrEmpty(tag) || tag.Length > MAX_CLAN_TAG_LENGTH)
            {
                return (Result.InvalidParameters, null);
            }

            var localName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player";

            var clan = new Clan
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Tag = tag.ToUpperInvariant(),
                Description = description,
                LeaderPuid = _localPuid,
                LeaderName = localName,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MaxMembers = _maxClanSize,
                AllowOpenJoin = _defaultAllowOpenJoin,
                RequireApproval = _defaultRequireApproval,
                Members = new List<ClanMember>
                {
                    new ClanMember
                    {
                        Puid = _localPuid,
                        Name = localName,
                        Role = ClanRole.Leader,
                        JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }
                },
                PendingRequests = new List<ClanJoinRequest>(),
                ChatHistory = new List<ClanChatMessage>()
            };

            PlayerData.CurrentClan = clan;
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Created clan: [{clan.Tag}] {clan.Name}");

            OnClanCreated?.Invoke(clan);
            return (Result.Success, clan);
        }

        /// <summary>
        /// Disband the clan (leader only).
        /// </summary>
        public async Task<Result> DisbandClanAsync()
        {
            if (!IsInClan || !IsLeader)
            {
                return Result.InvalidUser;
            }

            var clanName = CurrentClan.Name;

            // Add to history
            AddClanToHistory(CurrentClan, "Disbanded");

            PlayerData.CurrentClan = null;
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Disbanded clan: {clanName}");

            OnClanLeft?.Invoke();
            return Result.Success;
        }

        /// <summary>
        /// Leave the current clan.
        /// </summary>
        public async Task<Result> LeaveClanAsync()
        {
            if (!IsInClan)
            {
                return Result.NotFound;
            }

            if (IsLeader && CurrentClan.Members.Count > 1)
            {
                EOSDebugLogger.LogWarning("EOSClanManager",
                    "Leader must promote someone else or disband clan");
                return Result.InvalidParameters;
            }

            var clanName = CurrentClan.Name;
            AddClanToHistory(CurrentClan, "Left");

            PlayerData.CurrentClan = null;
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Left clan: {clanName}");

            OnClanLeft?.Invoke();
            return Result.Success;
        }

        /// <summary>
        /// Update clan settings (leader/officer only).
        /// </summary>
        public async Task<Result> UpdateClanSettingsAsync(ClanSettings settings)
        {
            if (!IsInClan || !IsOfficer)
            {
                return Result.InvalidUser;
            }

            if (!string.IsNullOrEmpty(settings.Name) && settings.Name.Length <= MAX_CLAN_NAME_LENGTH)
                CurrentClan.Name = settings.Name;

            if (!string.IsNullOrEmpty(settings.Description))
                CurrentClan.Description = settings.Description;

            if (settings.AllowOpenJoin.HasValue)
                CurrentClan.AllowOpenJoin = settings.AllowOpenJoin.Value;

            if (settings.RequireApproval.HasValue)
                CurrentClan.RequireApproval = settings.RequireApproval.Value;

            if (settings.MaxMembers.HasValue && settings.MaxMembers.Value >= CurrentClan.Members.Count)
                CurrentClan.MaxMembers = settings.MaxMembers.Value;

            _isDirty = true;
            await SaveClanDataAsync();

            OnClanUpdated?.Invoke(CurrentClan);
            return Result.Success;
        }

        #endregion

        #region Public API - Invites & Requests

        /// <summary>
        /// Invite a player to the clan (officer+).
        /// </summary>
        public async Task<Result> InvitePlayerAsync(string targetPuid, string targetName)
        {
            if (!IsInClan || !IsOfficer)
            {
                return Result.InvalidUser;
            }

            if (CurrentClan.Members.Any(m => m.Puid == targetPuid))
            {
                return Result.AlreadyPending; // Already a member
            }

            // Create invite (would need to send to target player somehow)
            var invite = new ClanInvite
            {
                ClanId = CurrentClan.Id,
                ClanName = CurrentClan.Name,
                ClanTag = CurrentClan.Tag,
                InviterPuid = _localPuid,
                InviterName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player",
                InvitedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(_inviteExpiryHours).ToUnixTimeMilliseconds()
            };

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Invited {targetName} to clan");

            // In a real implementation, this would send the invite to the target player
            // For now, we'll store it locally as a demonstration

            return Result.Success;
        }

        /// <summary>
        /// Request to join a clan.
        /// </summary>
        public async Task<Result> RequestJoinAsync(string clanId, string message = null)
        {
            if (IsInClan)
            {
                return Result.AlreadyPending;
            }

            // Create join request
            var request = new ClanJoinRequest
            {
                RequesterPuid = _localPuid,
                RequesterName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player",
                Message = message,
                RequestedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Requested to join clan {clanId}");

            // In a real implementation, this would send the request to the clan

            return Result.Success;
        }

        /// <summary>
        /// Accept a clan invite.
        /// </summary>
        public async Task<Result> AcceptInviteAsync(ClanInvite invite)
        {
            if (IsInClan)
            {
                return Result.AlreadyPending;
            }

            // Remove from pending
            PlayerData.PendingInvites.RemoveAll(i => i.ClanId == invite.ClanId);

            // In a real implementation, this would join the actual clan
            // For demonstration, we'll create a placeholder clan

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Accepted invite to [{invite.ClanTag}] {invite.ClanName}");

            _isDirty = true;
            await SaveClanDataAsync();

            return Result.Success;
        }

        /// <summary>
        /// Decline a clan invite.
        /// </summary>
        public async Task<Result> DeclineInviteAsync(ClanInvite invite)
        {
            PlayerData.PendingInvites.RemoveAll(i => i.ClanId == invite.ClanId);
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Declined invite to {invite.ClanName}");

            return Result.Success;
        }

        /// <summary>
        /// Accept a join request (officer+).
        /// </summary>
        public async Task<Result> AcceptJoinRequestAsync(ClanJoinRequest request)
        {
            if (!IsInClan || !IsOfficer)
            {
                return Result.InvalidUser;
            }

            if (CurrentClan.Members.Count >= CurrentClan.MaxMembers)
            {
                return Result.LimitExceeded;
            }

            // Add member
            var newMember = new ClanMember
            {
                Puid = request.RequesterPuid,
                Name = request.RequesterName,
                Role = ClanRole.Member,
                JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            CurrentClan.Members.Add(newMember);
            CurrentClan.PendingRequests.RemoveAll(r => r.RequesterPuid == request.RequesterPuid);

            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Accepted join request from {request.RequesterName}");

            OnMemberJoined?.Invoke(newMember);
            return Result.Success;
        }

        /// <summary>
        /// Reject a join request (officer+).
        /// </summary>
        public async Task<Result> RejectJoinRequestAsync(ClanJoinRequest request)
        {
            if (!IsInClan || !IsOfficer)
            {
                return Result.InvalidUser;
            }

            CurrentClan.PendingRequests.RemoveAll(r => r.RequesterPuid == request.RequesterPuid);
            _isDirty = true;
            await SaveClanDataAsync();

            return Result.Success;
        }

        #endregion

        #region Public API - Member Management

        /// <summary>
        /// Promote a member (leader only, or officer promoting to member).
        /// </summary>
        public async Task<Result> PromoteMemberAsync(string memberPuid)
        {
            if (!IsInClan)
            {
                return Result.NotFound;
            }

            var member = CurrentClan.Members.FirstOrDefault(m => m.Puid == memberPuid);
            if (member == null)
            {
                return Result.NotFound;
            }

            ClanRole newRole;
            if (member.Role == ClanRole.Member && IsOfficer)
            {
                newRole = ClanRole.Officer;
            }
            else if (member.Role == ClanRole.Officer && IsLeader)
            {
                // Transfer leadership
                newRole = ClanRole.Leader;
                var me = CurrentClan.Members.FirstOrDefault(m => m.Puid == _localPuid);
                if (me != null) me.Role = ClanRole.Officer;
                CurrentClan.LeaderPuid = memberPuid;
                CurrentClan.LeaderName = member.Name;
            }
            else
            {
                return Result.InvalidParameters;
            }

            member.Role = newRole;
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Promoted {member.Name} to {newRole}");

            OnMemberRoleChanged?.Invoke(memberPuid, newRole);
            return Result.Success;
        }

        /// <summary>
        /// Demote a member (leader only for officers).
        /// </summary>
        public async Task<Result> DemoteMemberAsync(string memberPuid)
        {
            if (!IsInClan || !IsLeader)
            {
                return Result.InvalidUser;
            }

            var member = CurrentClan.Members.FirstOrDefault(m => m.Puid == memberPuid);
            if (member == null || member.Role == ClanRole.Leader || member.Role == ClanRole.Member)
            {
                return Result.InvalidParameters;
            }

            member.Role = ClanRole.Member;
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Demoted {member.Name} to Member");

            OnMemberRoleChanged?.Invoke(memberPuid, ClanRole.Member);
            return Result.Success;
        }

        /// <summary>
        /// Kick a member (officer+ for members, leader for officers).
        /// </summary>
        public async Task<Result> KickMemberAsync(string memberPuid)
        {
            if (!IsInClan || !IsOfficer)
            {
                return Result.InvalidUser;
            }

            var member = CurrentClan.Members.FirstOrDefault(m => m.Puid == memberPuid);
            if (member == null)
            {
                return Result.NotFound;
            }

            // Officers can only kick members, leaders can kick anyone except themselves
            if (member.Role >= ClanRole.Officer && !IsLeader)
            {
                return Result.InvalidUser;
            }

            if (member.Puid == _localPuid)
            {
                return Result.InvalidParameters; // Can't kick yourself
            }

            CurrentClan.Members.Remove(member);
            _isDirty = true;
            await SaveClanDataAsync();

            EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                $"Kicked {member.Name} from clan");

            OnMemberLeft?.Invoke(memberPuid);
            return Result.Success;
        }

        /// <summary>
        /// Get member by PUID.
        /// </summary>
        public ClanMember GetMember(string puid)
        {
            return CurrentClan?.Members.FirstOrDefault(m => m.Puid == puid);
        }

        #endregion

        #region Public API - Chat

        /// <summary>
        /// Send a chat message to the clan.
        /// </summary>
        public async Task<Result> SendChatAsync(string message)
        {
            if (!IsInClan || string.IsNullOrEmpty(message))
            {
                return Result.InvalidParameters;
            }

            var chatMessage = new ClanChatMessage
            {
                SenderPuid = _localPuid,
                SenderName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player",
                Message = message,
                SentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            CurrentClan.ChatHistory.Add(chatMessage);

            // Trim history if too long
            while (CurrentClan.ChatHistory.Count > MAX_CHAT_HISTORY)
            {
                CurrentClan.ChatHistory.RemoveAt(0);
            }

            _isDirty = true;
            await SaveClanDataAsync();

            OnChatMessageReceived?.Invoke(chatMessage);
            return Result.Success;
        }

        /// <summary>
        /// Clear chat history (leader/officer only).
        /// </summary>
        public async Task<Result> ClearChatAsync()
        {
            if (!IsInClan || !IsOfficer)
            {
                return Result.InvalidUser;
            }

            CurrentClan.ChatHistory.Clear();
            _isDirty = true;
            await SaveClanDataAsync();

            return Result.Success;
        }

        #endregion

        #region Public API - Display

        /// <summary>
        /// Get formatted clan tag with brackets.
        /// </summary>
        public string GetFormattedTag()
        {
            if (!IsInClan) return "";
            return $"[{CurrentClan.Tag}]";
        }

        /// <summary>
        /// Get formatted clan tag for a member.
        /// </summary>
        public string GetMemberTag(string puid)
        {
            var member = GetMember(puid);
            if (member == null || CurrentClan == null) return "";
            return $"[{CurrentClan.Tag}]";
        }

        /// <summary>
        /// Get role display name.
        /// </summary>
        public static string GetRoleName(ClanRole role)
        {
            return role switch
            {
                ClanRole.Leader => "Leader",
                ClanRole.Officer => "Officer",
                ClanRole.Member => "Member",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get role icon.
        /// </summary>
        public static string GetRoleIcon(ClanRole role)
        {
            return role switch
            {
                ClanRole.Leader => "C",  // Crown
                ClanRole.Officer => "S", // Shield
                ClanRole.Member => "M",
                _ => "?"
            };
        }

        #endregion

        #region Private Methods

        private ClanRole GetMyRole()
        {
            if (!IsInClan) return ClanRole.Member;
            var me = CurrentClan.Members.FirstOrDefault(m => m.Puid == _localPuid);
            return me?.Role ?? ClanRole.Member;
        }

        private void AddClanToHistory(Clan clan, string reason)
        {
            PlayerData.ClanHistory.Add(new ClanHistoryEntry
            {
                ClanId = clan.Id,
                ClanName = clan.Name,
                ClanTag = clan.Tag,
                JoinedAt = clan.Members.FirstOrDefault(m => m.Puid == _localPuid)?.JoinedAt ?? 0,
                LeftAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LeaveReason = reason
            });
        }

        private void CleanupExpiredInvites()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int removed = PlayerData.PendingInvites.RemoveAll(i => i.ExpiresAt < now);
            if (removed > 0)
            {
                _isDirty = true;
                EOSDebugLogger.Log(DebugCategory.Social, "EOSClanManager",
                    $"Cleaned up {removed} expired invites");
            }
        }

        #endregion
    }

    #region Data Types

    /// <summary>
    /// Clan data.
    /// </summary>
    [Serializable]
    public class Clan
    {
        public string Id;
        public string Name;
        public string Tag;
        public string Description;
        public string LeaderPuid;
        public string LeaderName;
        public long CreatedAt;
        public int MaxMembers;
        public bool AllowOpenJoin;
        public bool RequireApproval;
        public List<ClanMember> Members;
        public List<ClanJoinRequest> PendingRequests;
        public List<ClanChatMessage> ChatHistory;

        public int MemberCount => Members?.Count ?? 0;
        public bool IsFull => MemberCount >= MaxMembers;
    }

    /// <summary>
    /// Clan member data.
    /// </summary>
    [Serializable]
    public class ClanMember
    {
        public string Puid;
        public string Name;
        public ClanRole Role;
        public long JoinedAt;
        public int ContributionPoints;
        public string Note;
    }

    /// <summary>
    /// Clan roles.
    /// </summary>
    public enum ClanRole
    {
        Member = 0,
        Officer = 1,
        Leader = 2
    }

    /// <summary>
    /// Clan invite.
    /// </summary>
    [Serializable]
    public class ClanInvite
    {
        public string ClanId;
        public string ClanName;
        public string ClanTag;
        public string InviterPuid;
        public string InviterName;
        public long InvitedAt;
        public long ExpiresAt;

        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > ExpiresAt;
    }

    /// <summary>
    /// Join request to a clan.
    /// </summary>
    [Serializable]
    public class ClanJoinRequest
    {
        public string RequesterPuid;
        public string RequesterName;
        public string Message;
        public long RequestedAt;
    }

    /// <summary>
    /// Clan chat message.
    /// </summary>
    [Serializable]
    public class ClanChatMessage
    {
        public string SenderPuid;
        public string SenderName;
        public string Message;
        public long SentAt;
    }

    /// <summary>
    /// Player's clan data.
    /// </summary>
    [Serializable]
    public class ClanPlayerData
    {
        public Clan CurrentClan;
        public List<ClanInvite> PendingInvites;
        public List<ClanHistoryEntry> ClanHistory;
        public int Version;
    }

    /// <summary>
    /// Historical clan membership record.
    /// </summary>
    [Serializable]
    public class ClanHistoryEntry
    {
        public string ClanId;
        public string ClanName;
        public string ClanTag;
        public long JoinedAt;
        public long LeftAt;
        public string LeaveReason;
    }

    /// <summary>
    /// Clan settings for updates.
    /// </summary>
    public class ClanSettings
    {
        public string Name;
        public string Description;
        public bool? AllowOpenJoin;
        public bool? RequireApproval;
        public int? MaxMembers;
    }

    /// <summary>
    /// Cloud storage container.
    /// </summary>
    [Serializable]
    public class ClanDataContainer
    {
        public int Version;
        public ClanPlayerData PlayerData;
        public long SavedAt;
    }

    #endregion
}
