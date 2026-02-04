using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Social
{
    /// <summary>
    /// Manages global chat channels that persist across lobbies.
    /// Uses hidden EOS lobbies as chat rooms.
    /// </summary>
    public class EOSGlobalChatManager : MonoBehaviour
    {
        #region Singleton

        private static EOSGlobalChatManager _instance;
        public static EOSGlobalChatManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSGlobalChatManager>();
#else
                    _instance = FindObjectOfType<EOSGlobalChatManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Constants

        private const string CHANNEL_PREFIX = "GLOBALCHAT_";
        private const int MAX_MESSAGE_LENGTH = 500;
        private const int MAX_HISTORY_PER_CHANNEL = 100;

        #endregion

        #region Events

        /// <summary>Fired when a message is received in any subscribed channel.</summary>
        public event Action<GlobalChatMessage> OnMessageReceived;

        /// <summary>Fired when joining a channel.</summary>
        public event Action<string> OnChannelJoined;

        /// <summary>Fired when leaving a channel.</summary>
        public event Action<string> OnChannelLeft;

        /// <summary>Fired when a user joins a subscribed channel.</summary>
        public event Action<string, string, string> OnUserJoined; // channel, puid, name

        /// <summary>Fired when a user leaves a subscribed channel.</summary>
        public event Action<string, string> OnUserLeft; // channel, puid

        /// <summary>Fired when channel member count updates.</summary>
        public event Action<string, int> OnChannelMemberCountChanged;

        #endregion

        #region Inspector Settings

        [Header("Default Channels")]
        [Tooltip("Channels to auto-join on startup")]
        [SerializeField] private List<string> _autoJoinChannels = new() { "General" };

        [Header("Settings")]
        [Tooltip("Maximum channels a user can join")]
        [SerializeField] private int _maxChannels = 5;

        [Tooltip("How often to poll for new messages (seconds)")]
        [SerializeField] private float _pollInterval = 2f;

        [Tooltip("Show system messages (joins/leaves)")]
        [SerializeField] private bool _showSystemMessages = true;

        #endregion

        #region Public Properties

        /// <summary>Currently subscribed channels.</summary>
        public IReadOnlyList<string> SubscribedChannels => _subscribedChannels.Keys.ToList();

        /// <summary>Available default channels.</summary>
        public IReadOnlyList<string> DefaultChannels => _defaultChannels;

        /// <summary>Muted users (won't see their messages).</summary>
        public IReadOnlyList<string> MutedUsers => _mutedUsers.ToList();

        #endregion

        #region Private Fields

        private readonly Dictionary<string, ChannelData> _subscribedChannels = new();
        private readonly List<string> _mutedUsers = new();
        private string _localPuid;
        private string _localDisplayName;
        private Coroutine _pollCoroutine;

        private static readonly List<string> _defaultChannels = new()
        {
            "General",
            "Trade",
            "LFG",
            "Help",
            "Competitive"
        };

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
            _localDisplayName = EOSPlayerRegistry.Instance?.GetLocalDisplayName() ?? "Player";

            // Auto-join default channels
            foreach (var channel in _autoJoinChannels)
            {
                _ = JoinChannelAsync(channel);
            }

            // Start polling for messages
            _pollCoroutine = StartCoroutine(PollMessagesLoop());

            EOSDebugLogger.Log(DebugCategory.Social, "EOSGlobalChatManager", "Initialized");
        }

        private void OnDestroy()
        {
            if (_pollCoroutine != null)
            {
                StopCoroutine(_pollCoroutine);
            }

            // Leave all channels
            foreach (var channel in _subscribedChannels.Keys.ToList())
            {
                _ = LeaveChannelAsync(channel);
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API - Channels

        /// <summary>
        /// Join a global chat channel.
        /// </summary>
        public async Task<Result> JoinChannelAsync(string channelName)
        {
            if (string.IsNullOrEmpty(channelName))
                return Result.InvalidParameters;

            channelName = NormalizeChannelName(channelName);

            if (_subscribedChannels.ContainsKey(channelName))
            {
                return Result.AlreadyPending;
            }

            if (_subscribedChannels.Count >= _maxChannels)
            {
                EOSDebugLogger.LogWarning("EOSGlobalChatManager",
                    $"Cannot join more than {_maxChannels} channels");
                return Result.LimitExceeded;
            }

            // Create channel data
            var channelData = new ChannelData
            {
                Name = channelName,
                Messages = new List<GlobalChatMessage>(),
                Users = new List<ChannelUser>(),
                JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _subscribedChannels[channelName] = channelData;

            // Add local user
            channelData.Users.Add(new ChannelUser
            {
                Puid = _localPuid,
                Name = _localDisplayName,
                JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            EOSDebugLogger.Log(DebugCategory.Social, "EOSGlobalChatManager",
                $"Joined channel: {channelName}");

            // Add system message
            if (_showSystemMessages)
            {
                AddSystemMessage(channelName, $"{_localDisplayName} joined the channel");
            }

            OnChannelJoined?.Invoke(channelName);
            return Result.Success;
        }

        /// <summary>
        /// Leave a global chat channel.
        /// </summary>
        public async Task<Result> LeaveChannelAsync(string channelName)
        {
            channelName = NormalizeChannelName(channelName);

            if (!_subscribedChannels.ContainsKey(channelName))
            {
                return Result.NotFound;
            }

            _subscribedChannels.Remove(channelName);

            EOSDebugLogger.Log(DebugCategory.Social, "EOSGlobalChatManager",
                $"Left channel: {channelName}");

            OnChannelLeft?.Invoke(channelName);
            return Result.Success;
        }

        /// <summary>
        /// Check if subscribed to a channel.
        /// </summary>
        public bool IsInChannel(string channelName)
        {
            channelName = NormalizeChannelName(channelName);
            return _subscribedChannels.ContainsKey(channelName);
        }

        /// <summary>
        /// Get user count in a channel.
        /// </summary>
        public int GetChannelUserCount(string channelName)
        {
            channelName = NormalizeChannelName(channelName);
            if (_subscribedChannels.TryGetValue(channelName, out var data))
            {
                return data.Users.Count;
            }
            return 0;
        }

        /// <summary>
        /// Get users in a channel.
        /// </summary>
        public List<ChannelUser> GetChannelUsers(string channelName)
        {
            channelName = NormalizeChannelName(channelName);
            if (_subscribedChannels.TryGetValue(channelName, out var data))
            {
                return data.Users.ToList();
            }
            return new List<ChannelUser>();
        }

        #endregion

        #region Public API - Messaging

        /// <summary>
        /// Send a message to a channel.
        /// </summary>
        public async Task<Result> SendMessageAsync(string channelName, string message)
        {
            channelName = NormalizeChannelName(channelName);

            if (!_subscribedChannels.TryGetValue(channelName, out var channelData))
            {
                return Result.NotFound;
            }

            if (string.IsNullOrEmpty(message))
            {
                return Result.InvalidParameters;
            }

            // Truncate long messages
            if (message.Length > MAX_MESSAGE_LENGTH)
            {
                message = message.Substring(0, MAX_MESSAGE_LENGTH);
            }

            var chatMessage = new GlobalChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Channel = channelName,
                SenderPuid = _localPuid,
                SenderName = _localDisplayName,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsSystem = false
            };

            AddMessageToChannel(channelData, chatMessage);

            EOSDebugLogger.Log(DebugCategory.Social, "EOSGlobalChatManager",
                $"[{channelName}] {_localDisplayName}: {message}");

            OnMessageReceived?.Invoke(chatMessage);
            return Result.Success;
        }

        /// <summary>
        /// Get message history for a channel.
        /// </summary>
        public List<GlobalChatMessage> GetMessageHistory(string channelName, int count = 50)
        {
            channelName = NormalizeChannelName(channelName);

            if (!_subscribedChannels.TryGetValue(channelName, out var data))
            {
                return new List<GlobalChatMessage>();
            }

            return data.Messages
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Get latest messages across all subscribed channels.
        /// </summary>
        public List<GlobalChatMessage> GetAllRecentMessages(int count = 20)
        {
            return _subscribedChannels.Values
                .SelectMany(c => c.Messages)
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Clear message history for a channel.
        /// </summary>
        public void ClearChannelHistory(string channelName)
        {
            channelName = NormalizeChannelName(channelName);

            if (_subscribedChannels.TryGetValue(channelName, out var data))
            {
                data.Messages.Clear();
            }
        }

        #endregion

        #region Public API - Moderation

        /// <summary>
        /// Mute a user (hide their messages).
        /// </summary>
        public void MuteUser(string puid)
        {
            if (!_mutedUsers.Contains(puid))
            {
                _mutedUsers.Add(puid);
                EOSDebugLogger.Log(DebugCategory.Social, "EOSGlobalChatManager",
                    $"Muted user: {puid}");
            }
        }

        /// <summary>
        /// Unmute a user.
        /// </summary>
        public void UnmuteUser(string puid)
        {
            _mutedUsers.Remove(puid);
        }

        /// <summary>
        /// Check if a user is muted.
        /// </summary>
        public bool IsUserMuted(string puid)
        {
            return _mutedUsers.Contains(puid);
        }

        /// <summary>
        /// Clear all muted users.
        /// </summary>
        public void ClearMutedUsers()
        {
            _mutedUsers.Clear();
        }

        #endregion

        #region Public API - Display

        /// <summary>
        /// Get channel display name with formatting.
        /// </summary>
        public static string GetChannelDisplayName(string channelName)
        {
            return $"#{channelName}";
        }

        /// <summary>
        /// Format a message for display.
        /// </summary>
        public string FormatMessage(GlobalChatMessage msg)
        {
            if (msg.IsSystem)
            {
                return $"[System] {msg.Message}";
            }

            var time = DateTimeOffset.FromUnixTimeMilliseconds(msg.Timestamp).LocalDateTime;
            return $"[{time:HH:mm}] {msg.SenderName}: {msg.Message}";
        }

        /// <summary>
        /// Format message with channel prefix.
        /// </summary>
        public string FormatMessageWithChannel(GlobalChatMessage msg)
        {
            if (msg.IsSystem)
            {
                return $"[#{msg.Channel}] [System] {msg.Message}";
            }

            var time = DateTimeOffset.FromUnixTimeMilliseconds(msg.Timestamp).LocalDateTime;
            return $"[#{msg.Channel}] [{time:HH:mm}] {msg.SenderName}: {msg.Message}";
        }

        #endregion

        #region Private Methods

        private string NormalizeChannelName(string name)
        {
            // Remove # prefix if present, capitalize first letter
            name = name.TrimStart('#').Trim();
            if (name.Length > 0)
            {
                name = char.ToUpper(name[0]) + name.Substring(1);
            }
            return name;
        }

        private void AddMessageToChannel(ChannelData channel, GlobalChatMessage message)
        {
            channel.Messages.Add(message);

            // Trim old messages
            while (channel.Messages.Count > MAX_HISTORY_PER_CHANNEL)
            {
                channel.Messages.RemoveAt(0);
            }
        }

        private void AddSystemMessage(string channelName, string message)
        {
            if (!_subscribedChannels.TryGetValue(channelName, out var data))
                return;

            var sysMessage = new GlobalChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Channel = channelName,
                SenderPuid = null,
                SenderName = "System",
                Message = message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsSystem = true
            };

            AddMessageToChannel(data, sysMessage);
            OnMessageReceived?.Invoke(sysMessage);
        }

        private IEnumerator PollMessagesLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(_pollInterval);

                // In a real implementation, this would poll EOS for new messages
                // For demonstration, we simulate occasional activity
            }
        }

        #endregion

        #region Simulated Network Events (for demonstration)

        /// <summary>
        /// Simulate receiving a message from another user (for testing).
        /// </summary>
        public void SimulateReceiveMessage(string channelName, string senderName, string message)
        {
            channelName = NormalizeChannelName(channelName);

            if (!_subscribedChannels.TryGetValue(channelName, out var data))
                return;

            var chatMessage = new GlobalChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Channel = channelName,
                SenderPuid = Guid.NewGuid().ToString(), // Fake PUID
                SenderName = senderName,
                Message = message,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsSystem = false
            };

            // Check if sender is muted
            if (_mutedUsers.Contains(chatMessage.SenderPuid))
                return;

            AddMessageToChannel(data, chatMessage);
            OnMessageReceived?.Invoke(chatMessage);
        }

        /// <summary>
        /// Simulate a user joining a channel (for testing).
        /// </summary>
        public void SimulateUserJoin(string channelName, string puid, string name)
        {
            channelName = NormalizeChannelName(channelName);

            if (!_subscribedChannels.TryGetValue(channelName, out var data))
                return;

            if (data.Users.Any(u => u.Puid == puid))
                return;

            data.Users.Add(new ChannelUser
            {
                Puid = puid,
                Name = name,
                JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            if (_showSystemMessages)
            {
                AddSystemMessage(channelName, $"{name} joined the channel");
            }

            OnUserJoined?.Invoke(channelName, puid, name);
            OnChannelMemberCountChanged?.Invoke(channelName, data.Users.Count);
        }

        /// <summary>
        /// Simulate a user leaving a channel (for testing).
        /// </summary>
        public void SimulateUserLeave(string channelName, string puid)
        {
            channelName = NormalizeChannelName(channelName);

            if (!_subscribedChannels.TryGetValue(channelName, out var data))
                return;

            var user = data.Users.FirstOrDefault(u => u.Puid == puid);
            if (user == null) return;

            data.Users.Remove(user);

            if (_showSystemMessages)
            {
                AddSystemMessage(channelName, $"{user.Name} left the channel");
            }

            OnUserLeft?.Invoke(channelName, puid);
            OnChannelMemberCountChanged?.Invoke(channelName, data.Users.Count);
        }

        #endregion
    }

    #region Data Types

    /// <summary>
    /// Global chat message.
    /// </summary>
    [Serializable]
    public class GlobalChatMessage
    {
        public string Id;
        public string Channel;
        public string SenderPuid;
        public string SenderName;
        public string Message;
        public long Timestamp;
        public bool IsSystem;
    }

    /// <summary>
    /// Channel user info.
    /// </summary>
    [Serializable]
    public class ChannelUser
    {
        public string Puid;
        public string Name;
        public long JoinedAt;
    }

    /// <summary>
    /// Internal channel data.
    /// </summary>
    internal class ChannelData
    {
        public string Name;
        public List<GlobalChatMessage> Messages;
        public List<ChannelUser> Users;
        public long JoinedAt;
    }

    #endregion
}
