using System.IO;
using FishNet.Transport.EOSNative.Logging;
using UnityEditor;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Editor
{
    /// <summary>
    /// Editor window for configuring EOS Native debug logging settings.
    /// Provides per-category toggles organized by system groups.
    /// </summary>
    public class EOSDebugSettingsWindow : EditorWindow
    {
        private EOSDebugSettings _settings;
        private Vector2 _scrollPosition;

        // Foldout states for category groups
        private bool _coreExpanded = true;
        private bool _lobbyExpanded = true;
        private bool _voiceExpanded = true;
        private bool _migrationExpanded = true;
        private bool _socialExpanded = true;
        private bool _statsExpanded = true;
        private bool _storageExpanded = true;
        private bool _moderationExpanded = true;
        private bool _demoExpanded = true;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _enabledStyle;
        private GUIStyle _disabledStyle;
        private GUIStyle _titleStyle;
        private bool _stylesInitialized;

        // Icon labels (Unicode symbols)
        private const string ICON_CORE = "●";
        private const string ICON_LOBBY = "◈";
        private const string ICON_VOICE = "🔊";
        private const string ICON_MIGRATION = "↻";
        private const string ICON_SOCIAL = "👥";
        private const string ICON_STATS = "📊";
        private const string ICON_STORAGE = "📁";
        private const string ICON_MODERATION = "⚠";
        private const string ICON_DEMO = "🎮";

        [MenuItem("Tools/FishNet EOS Native/Debug Settings", priority = 200)]
        public static void ShowWindow()
        {
            var window = GetWindow<EOSDebugSettingsWindow>("EOS Debug Settings");
            window.minSize = new Vector2(350, 500);
            window.Show();
        }

        private void OnEnable()
        {
            LoadOrCreateSettings();
        }

        private void LoadOrCreateSettings()
        {
            // Try to load from Resources first
            _settings = Resources.Load<EOSDebugSettings>("EOSDebugSettings");

            // If not found, try to find any in project
            if (_settings == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:EOSDebugSettings");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _settings = AssetDatabase.LoadAssetAtPath<EOSDebugSettings>(path);
                }
            }
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };

            _enabledStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.3f, 0.8f, 0.3f) }
            };

            _disabledStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeStyles();

            // Header banner
            DrawHeader();

            EditorGUILayout.Space(5);

            // Settings asset reference
            EditorGUI.BeginChangeCheck();
            _settings = (EOSDebugSettings)EditorGUILayout.ObjectField("Settings Asset", _settings, typeof(EOSDebugSettings), false);
            if (EditorGUI.EndChangeCheck() && _settings != null)
            {
                EOSDebugSettings.SetInstance(_settings);
            }

            EditorGUILayout.Space(5);

            // Create settings button if none exists
            if (_settings == null)
            {
                EditorGUILayout.HelpBox("No EOSDebugSettings asset found. Create one to configure debug logging.", MessageType.Warning);
                if (GUILayout.Button("Create Debug Settings Asset", GUILayout.Height(30)))
                {
                    CreateSettingsAsset();
                }
                return;
            }

            // Global enable toggle with colored status
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Global Logging:", GUILayout.Width(100));

            GUIStyle statusStyle = _settings.GlobalEnabled ? _enabledStyle : _disabledStyle;
            EditorGUILayout.LabelField(_settings.GlobalEnabled ? "ENABLED" : "DISABLED", statusStyle, GUILayout.Width(80));

            EditorGUI.BeginChangeCheck();
            bool newGlobalEnabled = EditorGUILayout.Toggle(_settings.GlobalEnabled, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, "Toggle Global Logging");
                _settings.GlobalEnabled = newGlobalEnabled;
                EditorUtility.SetDirty(_settings);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Enable/Disable All buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Enable All Categories"))
            {
                Undo.RecordObject(_settings, "Enable All Categories");
                _settings.EnableAllCategories();
                EditorUtility.SetDirty(_settings);
            }
            if (GUILayout.Button("Disable All Categories"))
            {
                Undo.RecordObject(_settings, "Disable All Categories");
                _settings.DisableAllCategories();
                EditorUtility.SetDirty(_settings);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Category count
            int enabledCount = _settings.GetEnabledCategoryCount();
            EditorGUILayout.LabelField($"Enabled Categories: {enabledCount}/31", EditorStyles.centeredGreyMiniLabel);

            // Show muted groups warning if any are muted
            if (_settings.MutedGroups != DebugCategory.None)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("Some groups are muted", MessageType.Info);
                if (GUILayout.Button("Unmute All", GUILayout.Width(80), GUILayout.Height(38)))
                {
                    Undo.RecordObject(_settings, "Unmute All Groups");
                    _settings.MutedGroups = DebugCategory.None;
                    EditorUtility.SetDirty(_settings);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            // Scrollable categories
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawCategoryGroup("Core", ref _coreExpanded, DebugCategory.AllCore, new[]
            {
                (DebugCategory.EOSManager, "EOS Manager", "SDK initialization, login, platform"),
                (DebugCategory.Transport, "Transport", "Main transport lifecycle"),
                (DebugCategory.Server, "Server", "Server-side P2P operations"),
                (DebugCategory.Client, "Client", "Client-side P2P operations"),
                (DebugCategory.ClientHost, "Client Host", "Host acting as client"),
                (DebugCategory.PacketFragmenter, "Packet Fragmenter", "Large packet splitting"),
                (DebugCategory.PlayerRegistry, "Player Registry", "PUID to display name cache")
            }, ICON_CORE);

            DrawCategoryGroup("Lobby", ref _lobbyExpanded, DebugCategory.AllLobby, new[]
            {
                (DebugCategory.LobbyManager, "Lobby Manager", "Lobby create/join/leave"),
                (DebugCategory.LobbyChatManager, "Lobby Chat", "Text chat via lobby attributes")
            }, ICON_LOBBY);

            DrawCategoryGroup("Voice", ref _voiceExpanded, DebugCategory.AllVoice, new[]
            {
                (DebugCategory.VoiceManager, "Voice Manager", "RTC/voice setup"),
                (DebugCategory.VoicePlayer, "Voice Player", "Per-player voice playback"),
                (DebugCategory.FishNetVoicePlayer, "FishNet Voice Player", "Networked voice sync")
            }, ICON_VOICE);

            DrawCategoryGroup("Migration", ref _migrationExpanded, DebugCategory.AllMigration, new[]
            {
                (DebugCategory.HostMigrationManager, "Migration Manager", "State save/restore orchestration"),
                (DebugCategory.HostMigratable, "Migratable", "Per-object migration state"),
                (DebugCategory.HostMigrationPlayerSpawner, "Migration Spawner", "Player respawn after migration")
            }, ICON_MIGRATION);

            DrawCategoryGroup("Social", ref _socialExpanded, DebugCategory.AllSocial, new[]
            {
                (DebugCategory.Friends, "Friends", "Friend list operations"),
                (DebugCategory.Presence, "Presence", "Online status"),
                (DebugCategory.UserInfo, "User Info", "Display name lookup"),
                (DebugCategory.CustomInvites, "Custom Invites", "Game invitations")
            }, ICON_SOCIAL);

            DrawCategoryGroup("Stats", ref _statsExpanded, DebugCategory.AllStats, new[]
            {
                (DebugCategory.Stats, "Stats", "Player statistics"),
                (DebugCategory.Leaderboards, "Leaderboards", "Leaderboard queries"),
                (DebugCategory.Achievements, "Achievements", "Achievement tracking")
            }, ICON_STATS);

            DrawCategoryGroup("Storage", ref _storageExpanded, DebugCategory.AllStorage, new[]
            {
                (DebugCategory.PlayerDataStorage, "Player Data", "Per-player cloud storage"),
                (DebugCategory.TitleStorage, "Title Storage", "Game-wide cloud storage")
            }, ICON_STORAGE);

            DrawCategoryGroup("Moderation", ref _moderationExpanded, DebugCategory.AllModeration, new[]
            {
                (DebugCategory.Reports, "Reports", "Player reports"),
                (DebugCategory.Sanctions, "Sanctions", "Bans and penalties"),
                (DebugCategory.Metrics, "Metrics", "Analytics events")
            }, ICON_MODERATION);

            DrawCategoryGroup("Demo", ref _demoExpanded, DebugCategory.AllDemo, new[]
            {
                (DebugCategory.NetworkPhysicsObject, "Network Physics Object", "Physics sync demo"),
                (DebugCategory.PlayerBall, "Player Ball", "Player controller demo"),
                (DebugCategory.PhysicsNetworkTransform, "Physics Network Transform", "Spring-based sync"),
                (DebugCategory.SimpleCamera, "Simple Camera", "Top-down camera following local player")
            }, ICON_DEMO);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // Save button
            if (GUILayout.Button("Save Settings", GUILayout.Height(25)))
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                Debug.Log("[EOSDebugSettingsWindow] Settings saved.");
            }

            EditorGUILayout.Space(5);
        }

        private void DrawCategoryGroup(string groupName, ref bool expanded, DebugCategory groupFlag,
            (DebugCategory category, string name, string description)[] categories, string icon = "")
        {
            int groupEnabledCount = 0;
            foreach (var cat in categories)
            {
                if ((_settings.EnabledCategories & cat.category) != 0) groupEnabledCount++;
            }

            bool isMuted = _settings.IsGroupMuted(groupFlag);

            // Dim the whole group if muted
            if (isMuted) GUI.color = new Color(0.7f, 0.7f, 0.7f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.color = Color.white;

            // Group header with foldout
            EditorGUILayout.BeginHorizontal();

            // Mute toggle (appears before foldout)
            EditorGUI.BeginChangeCheck();
            bool newActive = EditorGUILayout.Toggle(!isMuted, GUILayout.Width(18));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_settings, $"Toggle {groupName} Mute");
                if (newActive)
                    _settings.UnmuteGroup(groupFlag);
                else
                    _settings.MuteGroup(groupFlag);
                EditorUtility.SetDirty(_settings);
            }

            // Foldout with icon and muted indicator
            string mutedLabel = isMuted ? " [MUTED]" : "";
            string iconPrefix = !string.IsNullOrEmpty(icon) ? icon + " " : "";
            expanded = EditorGUILayout.Foldout(expanded, $"{iconPrefix}{groupName} ({groupEnabledCount}/{categories.Length}){mutedLabel}", true, EditorStyles.foldoutHeader);

            GUILayout.FlexibleSpace();

            // All/None buttons for the group
            if (GUILayout.Button("All", GUILayout.Width(40)))
            {
                Undo.RecordObject(_settings, $"Enable All {groupName}");
                _settings.EnableCategory(groupFlag);
                EditorUtility.SetDirty(_settings);
            }
            if (GUILayout.Button("None", GUILayout.Width(40)))
            {
                Undo.RecordObject(_settings, $"Disable All {groupName}");
                _settings.DisableCategory(groupFlag);
                EditorUtility.SetDirty(_settings);
            }
            EditorGUILayout.EndHorizontal();

            // Individual categories
            if (expanded)
            {
                EditorGUI.indentLevel++;
                foreach (var cat in categories)
                {
                    EditorGUILayout.BeginHorizontal();

                    bool isEnabled = (_settings.EnabledCategories & cat.category) != 0;

                    EditorGUI.BeginChangeCheck();
                    bool newEnabled = EditorGUILayout.ToggleLeft(
                        new GUIContent(cat.name, cat.description),
                        isEnabled);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_settings, $"Toggle {cat.name}");
                        if (newEnabled)
                            _settings.EnableCategory(cat.category);
                        else
                            _settings.DisableCategory(cat.category);
                        EditorUtility.SetDirty(_settings);
                    }

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }


        private void DrawHeader()
        {
            // Dark header background
            var headerRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, position.width, 50), new Color(0.15f, 0.15f, 0.15f));

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // FishNet icon (or fallback to settings icon)
            var fishIcon = EditorGUIUtility.IconContent("d_NetworkAnimator Icon");
            if (fishIcon != null && fishIcon.image != null)
            {
                GUILayout.Label(fishIcon, GUILayout.Width(32), GUILayout.Height(32));
            }

            // Title
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
            GUILayout.Label("FishNet EOS Native", titleStyle, GUILayout.Height(32));

            // Separator
            GUILayout.Label(" | ", titleStyle, GUILayout.Height(32));

            // Subtitle
            var subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 0.8f, 1f) }
            };
            GUILayout.Label("Debug Settings", subtitleStyle, GUILayout.Height(32));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }

        private void CreateSettingsAsset()
        {
            // Ensure Resources folder exists
            string resourcesPath = "Assets/FishNet.Transport.EOSNative/Resources";
            if (!Directory.Exists(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
                AssetDatabase.Refresh();
            }

            // Create the asset
            _settings = ScriptableObject.CreateInstance<EOSDebugSettings>();
            string assetPath = $"{resourcesPath}/EOSDebugSettings.asset";

            AssetDatabase.CreateAsset(_settings, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EOSDebugSettings.SetInstance(_settings);

            Debug.Log($"[EOSDebugSettingsWindow] Created debug settings at: {assetPath}");
            EditorGUIUtility.PingObject(_settings);
        }
    }
}
