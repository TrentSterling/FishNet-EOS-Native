using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Storage;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Lobbies
{
    /// <summary>
    /// Manages text chat via EOS lobby member attributes.
    /// Chat works without a host - EOS lobby infrastructure handles sync.
    /// Chat persists through host migration!
    /// </summary>
    public class EOSLobbyChatManager : MonoBehaviour
    {
        #region Singleton

        private static EOSLobbyChatManager _instance;
        public static EOSLobbyChatManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSLobbyChatManager>();
#else
                    _instance = FindObjectOfType<EOSLobbyChatManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when a chat message is received.
        /// Parameters: sender PUID, sender display name (if known), message text, timestamp.
        /// </summary>
        public event Action<string, string, string, long> OnChatMessageReceived;

        #endregion

        #region Serialized Fields

        [Header("Settings")]
        [Tooltip("Maximum number of messages to keep in history.")]
        [SerializeField]
        private int _maxMessages = 100;

        [Tooltip("Your display name for chat. Set before joining lobby. Leave empty for random name.")]
        [SerializeField]
        private string _displayName = "";

        [Tooltip("Auto-generate a random name if display name is empty.")]
        [SerializeField]
        private bool _autoGenerateName = true;

        #endregion

        #region Public Properties

        /// <summary>
        /// Your display name for chat messages.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => _displayName = value;
        }

        /// <summary>
        /// Chat message history (newest last).
        /// </summary>
        public IReadOnlyList<ChatMessage> Messages => _messages;

        #endregion

        #region Private Fields

        private readonly List<ChatMessage> _messages = new();
        private readonly Dictionary<string, long> _lastMessageTimestamp = new();
        private readonly Dictionary<string, string> _displayNameCache = new(); // PUID -> DisplayName (session cache)
        private EOSLobbyManager _lobbyManager;
        private EOSPlayerRegistry _playerRegistry;

        // Chat history persistence
        private string _currentLobbyCode;
        private const string CHAT_HISTORY_PREFIX = "chat_history_";
        private const int MAX_PERSISTED_MESSAGES = 50;

        #endregion

        #region Unity Lifecycle

        // Fun name words - deterministic based on PUID hash
        private static readonly string[] _nameAdjectives = {
            "Swift", "Brave", "Mighty", "Silent", "Happy", "Lucky", "Fierce", "Cool", "Wild", "Epic",
            "Angry", "Sneaky", "Sleepy", "Hungry", "Dizzy", "Fuzzy", "Grumpy", "Jolly", "Crazy", "Lazy",
            "Cosmic", "Turbo", "Ultra", "Mega", "Super", "Hyper", "Plasma", "Neon", "Shadow", "Storm"
        };
        private static readonly string[] _nameNouns = {
            "Wolf", "Eagle", "Tiger", "Dragon", "Phoenix", "Ninja", "Pirate", "Knight", "Wizard", "Hero",
            "Panda", "Otter", "Falcon", "Shark", "Cobra", "Panther", "Raven", "Badger", "Moose", "Gator",
            "Samurai", "Viking", "Spartan", "Ranger", "Hunter", "Mage", "Rogue", "Warrior", "Scout", "Chief"
        };

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            _lobbyManager = EOSLobbyManager.Instance;
            _playerRegistry = EOSPlayerRegistry.Instance;

            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberAttributeUpdated += OnMemberAttributeUpdated;
                _lobbyManager.OnLobbyJoined += OnLobbyJoined;
                _lobbyManager.OnLobbyLeft += OnLobbyLeft;
            }

            // Auto-generate name based on PUID if empty (after EOS login)
            StartCoroutine(InitializeDisplayNameCoroutine());
        }

        private System.Collections.IEnumerator InitializeDisplayNameCoroutine()
        {
            // Wait for EOS login
            while (EOSManager.Instance == null || !EOSManager.Instance.IsLoggedIn)
            {
                yield return new WaitForSeconds(0.5f);
            }

            // Generate name from PUID if not set OR if still default "Player"
            if ((string.IsNullOrEmpty(_displayName) || _displayName == "Player") && _autoGenerateName)
            {
                string puid = EOSManager.Instance.LocalProductUserId?.ToString();
                _displayName = GenerateNameFromPuid(puid);
                EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Generated name from PUID: {_displayName}");
            }
        }

        /// <summary>
        /// Generates a deterministic fun name from a PUID.
        /// Same PUID always produces the same name (e.g., "AngryPanda42").
        /// </summary>
        public static string GenerateNameFromPuid(string puid)
        {
            if (string.IsNullOrEmpty(puid))
            {
                return $"Player{UnityEngine.Random.Range(1, 1000)}";
            }

            // Use PUID hash for deterministic randomness
            int hash = puid.GetHashCode();
            System.Random rng = new System.Random(hash);

            string adj = _nameAdjectives[rng.Next(_nameAdjectives.Length)];
            string noun = _nameNouns[rng.Next(_nameNouns.Length)];
            int num = rng.Next(1, 100);

            return $"{adj}{noun}{num}";
        }

        /// <summary>
        /// Gets a display name for a PUID - checks session cache, persistent registry, then generates.
        /// </summary>
        public string GetOrGenerateDisplayName(string puid)
        {
            // Check if it's us
            if (puid == EOSManager.Instance?.LocalProductUserId?.ToString())
            {
                // Only use _displayName if it's actually set and not just "Player"
                if (!string.IsNullOrEmpty(_displayName) && _displayName != "Player")
                {
                    return _displayName;
                }
                // Otherwise generate from our own PUID
                return GenerateNameFromPuid(puid);
            }

            // Check session cache first (fastest)
            if (_displayNameCache.TryGetValue(puid, out string cachedName) && !string.IsNullOrEmpty(cachedName))
            {
                return cachedName;
            }

            // Check persistent registry (survives sessions)
            if (_playerRegistry != null)
            {
                string persistedName = _playerRegistry.GetPlayerName(puid);
                if (!string.IsNullOrEmpty(persistedName))
                {
                    _displayNameCache[puid] = persistedName; // Also cache in session
                    return persistedName;
                }
            }

            // Generate, cache in session, and persist
            string generated = GenerateNameFromPuid(puid);
            _displayNameCache[puid] = generated;
            _playerRegistry?.RegisterPlayer(puid, generated);
            return generated;
        }

        private void OnDestroy()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnMemberAttributeUpdated -= OnMemberAttributeUpdated;
                _lobbyManager.OnLobbyJoined -= OnLobbyJoined;
                _lobbyManager.OnLobbyLeft -= OnLobbyLeft;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Send a chat message to all lobby members.
        /// </summary>
        public async void SendChatMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            if (_lobbyManager == null || !_lobbyManager.IsInLobby)
            {
                EOSDebugLogger.LogWarning(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", "Not in a lobby - cannot send chat.");
                return;
            }

            var result = await _lobbyManager.SendChatMessageAsync(message);
            if (result != Epic.OnlineServices.Result.Success)
            {
                Debug.LogWarning($"[EOSLobbyChatManager] Failed to send message: {result}");
            }
        }

        /// <summary>
        /// Clear chat history.
        /// </summary>
        public void ClearHistory()
        {
            _messages.Clear();
            _lastMessageTimestamp.Clear();
        }

        /// <summary>
        /// Get display name for a PUID.
        /// Priority: 1) Local name, 2) Cached name from member attribute, 3) Generated fun name from PUID.
        /// </summary>
        public string GetDisplayName(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return "Unknown";
            return GetOrGenerateDisplayName(puid);
        }

        /// <summary>
        /// Set a cached display name for a PUID (also persists to registry).
        /// </summary>
        public void SetCachedDisplayName(string puid, string name)
        {
            if (!string.IsNullOrEmpty(puid) && !string.IsNullOrEmpty(name))
            {
                _displayNameCache[puid] = name;
                _playerRegistry?.RegisterPlayer(puid, name);
            }
        }

        #endregion

        #region Chat History Persistence

        /// <summary>
        /// Save chat history for a lobby to cloud storage.
        /// </summary>
        public async Task SaveChatHistoryAsync(string lobbyCode)
        {
            if (string.IsNullOrEmpty(lobbyCode) || _messages.Count == 0) return;

            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return;

            try
            {
                // Only persist non-system messages, up to max
                var toSave = new List<ChatMessage>();
                for (int i = Math.Max(0, _messages.Count - MAX_PERSISTED_MESSAGES); i < _messages.Count; i++)
                {
                    if (!_messages[i].IsSystem)
                        toSave.Add(_messages[i]);
                }

                if (toSave.Count == 0) return;

                var history = new ChatHistoryData
                {
                    LobbyCode = lobbyCode,
                    Messages = toSave.ToArray(),
                    SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                string filename = $"{CHAT_HISTORY_PREFIX}{lobbyCode}.json";
                var result = await storage.WriteFileAsJsonAsync(filename, history);

                if (result == Epic.OnlineServices.Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $"Saved {toSave.Count} messages for lobby {lobbyCode}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSLobbyChatManager] Failed to save chat history: {e.Message}");
            }
        }

        /// <summary>
        /// Load chat history for a lobby from cloud storage.
        /// </summary>
        public async Task LoadChatHistoryAsync(string lobbyCode)
        {
            if (string.IsNullOrEmpty(lobbyCode)) return;

            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return;

            try
            {
                string filename = $"{CHAT_HISTORY_PREFIX}{lobbyCode}.json";

                var (result, data) = await storage.ReadFileAsJsonAsync<ChatHistoryData>(filename);

                if (result == Epic.OnlineServices.Result.Success && data.Messages != null && data.Messages.Length > 0)
                {
                    // Clear current messages and add loaded ones
                    _messages.Clear();

                    // Add system message about loaded history
                    AddSystemMessage($"Loaded {data.Messages.Length} messages from history");

                    foreach (var msg in data.Messages)
                    {
                        _messages.Add(msg);
                        // Update timestamp tracking to avoid duplicates
                        if (!msg.IsSystem && !string.IsNullOrEmpty(msg.SenderPuid))
                        {
                            if (!_lastMessageTimestamp.ContainsKey(msg.SenderPuid) || _lastMessageTimestamp[msg.SenderPuid] < msg.Timestamp)
                            {
                                _lastMessageTimestamp[msg.SenderPuid] = msg.Timestamp;
                            }
                        }
                    }

                    EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $"Loaded {data.Messages.Length} messages for lobby {lobbyCode}");
                }
            }
            catch (Exception e)
            {
                EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $"No chat history for lobby {lobbyCode}: {e.Message}");
            }
        }

        /// <summary>
        /// Delete chat history for a lobby.
        /// </summary>
        public async Task DeleteChatHistoryAsync(string lobbyCode)
        {
            if (string.IsNullOrEmpty(lobbyCode)) return;

            var storage = EOSPlayerDataStorage.Instance;
            if (storage == null || !storage.IsReady) return;

            string filename = $"{CHAT_HISTORY_PREFIX}{lobbyCode}.json";
            await storage.DeleteFileAsync(filename);
        }

        #endregion

        #region Private Methods

        private async void OnLobbyJoined(LobbyData lobby)
        {
            // Store lobby code for history persistence
            _currentLobbyCode = lobby.JoinCode;

            // Clear local cache but try to load history from cloud first
            _displayNameCache.Clear();
            _lastMessageTimestamp.Clear();

            // Try to load chat history for this lobby from cloud
            await LoadChatHistoryAsync(lobby.JoinCode);

            string puid = EOSManager.Instance?.LocalProductUserId?.ToString();

            // Always ensure we have a valid display name (not empty or generic "Player")
            if ((string.IsNullOrEmpty(_displayName) || _displayName == "Player") && _autoGenerateName)
            {
                if (!string.IsNullOrEmpty(puid))
                {
                    _displayName = GenerateNameFromPuid(puid);
                    EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Generated name on join: {_displayName}");
                }
            }

            // Share our display name with other lobby members
            if (_lobbyManager != null && !string.IsNullOrEmpty(_displayName))
            {
                var result = await _lobbyManager.SetMemberAttributeAsync(MemberAttributes.DISPLAY_NAME, _displayName);
                if (result == Epic.OnlineServices.Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Shared display name: {_displayName}");
                }
            }

            // Share our platform with other lobby members
            if (_lobbyManager != null)
            {
                string platformId = EOSPlatformHelper.PlatformId;
                var result = await _lobbyManager.SetMemberAttributeAsync(MemberAttributes.PLATFORM, platformId);
                if (result == Epic.OnlineServices.Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Shared platform: {platformId}");
                }
            }

            // Read existing members' display names and platforms
            ReadExistingMemberNames(lobby.LobbyId);

            // Add system message
            AddSystemMessage($"Joined lobby: {lobby.JoinCode}");
        }

        /// <summary>
        /// Reads DISPLAY_NAME attributes from all existing lobby members.
        /// Called when joining a lobby to populate the name cache.
        /// </summary>
        private void ReadExistingMemberNames(string lobbyId)
        {
            var lobbyInterface = EOSManager.Instance?.LobbyInterface;
            if (lobbyInterface == null) return;

            var detailsOptions = new Epic.OnlineServices.Lobby.CopyLobbyDetailsHandleOptions
            {
                LocalUserId = EOSManager.Instance.LocalProductUserId,
                LobbyId = lobbyId
            };

            if (lobbyInterface.CopyLobbyDetailsHandle(ref detailsOptions, out var details) != Epic.OnlineServices.Result.Success || details == null)
                return;

            var countOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberCountOptions();
            uint memberCount = details.GetMemberCount(ref countOptions);

            for (uint i = 0; i < memberCount; i++)
            {
                var memberOptions = new Epic.OnlineServices.Lobby.LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                var memberId = details.GetMemberByIndex(ref memberOptions);
                if (memberId == null) continue;

                string memberPuid = memberId.ToString();

                // Try to read their DISPLAY_NAME attribute
                var attrOptions = new Epic.OnlineServices.Lobby.LobbyDetailsCopyMemberAttributeByKeyOptions
                {
                    TargetUserId = memberId,
                    AttrKey = MemberAttributes.DISPLAY_NAME
                };

                if (details.CopyMemberAttributeByKey(ref attrOptions, out var attr) == Epic.OnlineServices.Result.Success && attr.HasValue)
                {
                    string displayName = attr.Value.Data?.Value.AsUtf8;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        _displayNameCache[memberPuid] = displayName;
                        _playerRegistry?.RegisterPlayer(memberPuid, displayName);
                        EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Cached name for {memberPuid.Substring(0, 8)}: {displayName}");
                    }
                }

                // Try to read their PLATFORM attribute
                var platformOptions = new Epic.OnlineServices.Lobby.LobbyDetailsCopyMemberAttributeByKeyOptions
                {
                    TargetUserId = memberId,
                    AttrKey = MemberAttributes.PLATFORM
                };

                if (details.CopyMemberAttributeByKey(ref platformOptions, out var platformAttr) == Epic.OnlineServices.Result.Success && platformAttr.HasValue)
                {
                    string platform = platformAttr.Value.Data?.Value.AsUtf8;
                    if (!string.IsNullOrEmpty(platform))
                    {
                        _playerRegistry?.RegisterPlatform(memberPuid, platform);
                        EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Cached platform for {memberPuid.Substring(0, 8)}: {platform}");
                    }
                }
            }

            details.Release();
        }

        private async void OnLobbyLeft()
        {
            AddSystemMessage("Left lobby");

            // Save chat history to cloud before leaving
            if (!string.IsNullOrEmpty(_currentLobbyCode))
            {
                await SaveChatHistoryAsync(_currentLobbyCode);
                _currentLobbyCode = null;
            }
        }

        private void OnMemberAttributeUpdated(string puid, string key, string value)
        {
            // Handle DISPLAY_NAME attribute - cache it for chat display and persist
            if (key == MemberAttributes.DISPLAY_NAME && !string.IsNullOrEmpty(value))
            {
                _displayNameCache[puid] = value;
                _playerRegistry?.RegisterPlayer(puid, value);
                EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Player: {value}");
                return;
            }

            // Handle PLATFORM attribute - cache player's platform
            if (key == MemberAttributes.PLATFORM && !string.IsNullOrEmpty(value))
            {
                _playerRegistry?.RegisterPlatform(puid, value);
                EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" Platform: {value}");
                return;
            }

            if (key != MemberAttributes.CHAT || string.IsNullOrEmpty(value))
                return;

            // Parse timestamp:message format
            var colonIndex = value.IndexOf(':');
            if (colonIndex <= 0) return;

            if (!long.TryParse(value.Substring(0, colonIndex), out long timestamp))
                return;

            string message = value.Substring(colonIndex + 1);

            // Skip if we've already seen this message (duplicate notification)
            if (_lastMessageTimestamp.TryGetValue(puid, out long lastTs) && lastTs >= timestamp)
                return;

            _lastMessageTimestamp[puid] = timestamp;

            // Skip own messages (we already show them locally when sent)
            // Actually, let's show them to confirm they were received
            // if (puid == EOSManager.Instance?.LocalProductUserId?.ToString()) return;

            // Add to history
            var chatMsg = new ChatMessage
            {
                SenderPuid = puid,
                SenderName = GetDisplayName(puid),
                Message = message,
                Timestamp = timestamp,
                IsSystem = false
            };

            AddMessage(chatMsg);

            EOSDebugLogger.Log(DebugCategory.LobbyChatManager, "EOSLobbyChatManager", $" {chatMsg.SenderName}: {message}");
            OnChatMessageReceived?.Invoke(puid, chatMsg.SenderName, message, timestamp);
        }

        private void AddMessage(ChatMessage msg)
        {
            _messages.Add(msg);

            // Trim history if too long
            while (_messages.Count > _maxMessages)
            {
                _messages.RemoveAt(0);
            }
        }

        private void AddSystemMessage(string text)
        {
            var msg = new ChatMessage
            {
                SenderPuid = null,
                SenderName = "System",
                Message = text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                IsSystem = true
            };
            AddMessage(msg);
        }

        #endregion
    }

    /// <summary>
    /// A chat message.
    /// </summary>
    [Serializable]
    public struct ChatMessage
    {
        public string SenderPuid;
        public string SenderName;
        public string Message;
        public long Timestamp;
        public bool IsSystem;

        public DateTime LocalTime => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime;

        public override string ToString()
        {
            if (IsSystem)
                return $"[{LocalTime:HH:mm}] * {Message}";
            return $"[{LocalTime:HH:mm}] {SenderName}: {Message}";
        }
    }

    /// <summary>
    /// Serializable container for chat history persistence.
    /// </summary>
    [Serializable]
    public struct ChatHistoryData
    {
        public string LobbyCode;
        public ChatMessage[] Messages;
        public long SavedAt;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSLobbyChatManager))]
    public class EOSLobbyChatManagerEditor : Editor
    {
        private Vector2 _chatScrollPos;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSLobbyChatManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                // Display name
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Your Name");
                var nameStyle = new GUIStyle(EditorStyles.label);
                nameStyle.fontStyle = FontStyle.Bold;
                nameStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);
                EditorGUILayout.LabelField(manager.DisplayName ?? "(not set)", nameStyle);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.IntField("Message Count", manager.Messages?.Count ?? 0);
            }

            // Show recent messages
            if (Application.isPlaying && manager.Messages != null && manager.Messages.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Recent Messages", EditorStyles.boldLabel);

                _chatScrollPos = EditorGUILayout.BeginScrollView(_chatScrollPos, GUILayout.Height(100));
                int startIdx = Mathf.Max(0, manager.Messages.Count - 10);
                for (int i = startIdx; i < manager.Messages.Count; i++)
                {
                    var msg = manager.Messages[i];
                    var msgStyle = new GUIStyle(EditorStyles.miniLabel);
                    msgStyle.wordWrap = true;
                    if (msg.IsSystem)
                    {
                        msgStyle.normal.textColor = Color.gray;
                        EditorGUILayout.LabelField($"* {msg.Message}", msgStyle);
                    }
                    else
                    {
                        msgStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                        EditorGUILayout.LabelField($"{msg.SenderName}: {msg.Message}", msgStyle);
                    }
                }
                EditorGUILayout.EndScrollView();

                EditorUtility.SetDirty(target);
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see chat status.", MessageType.Info);
            }
        }
    }
#endif
}
