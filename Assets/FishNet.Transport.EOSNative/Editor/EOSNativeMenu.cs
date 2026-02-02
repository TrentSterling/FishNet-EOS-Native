using UnityEngine;
using UnityEditor;
using FishNet.Transport.EOSNative.Migration;

namespace FishNet.Transport.EOSNative.Editor
{
    /// <summary>
    /// Tools menu for FishNet EOS Native setup and utilities.
    /// </summary>
    public static class EOSNativeMenu
    {
        private const string MenuRoot = "Tools/FishNet EOS Native/";

        /// <summary>
        /// Sets up the scene with all required EOS Native components.
        /// Creates EOSNativeTransport which triggers full auto-setup.
        /// </summary>
        [MenuItem(MenuRoot + "Setup Scene", priority = 0)]
        public static void SetupScene()
        {
            // Check if transport already exists
            var existingTransport = UnityEngine.Object.FindAnyObjectByType<EOSNativeTransport>();
            if (existingTransport != null)
            {
                Debug.Log("[EOSNativeMenu] EOSNativeTransport already exists in scene. Triggering Reset to re-run AutoSetup...");
                // Trigger AutoSetup by calling Reset
                existingTransport.SendMessage("Reset", SendMessageOptions.DontRequireReceiver);
                Selection.activeGameObject = existingTransport.gameObject;
                EditorGUIUtility.PingObject(existingTransport.gameObject);
                return;
            }

            // Create new GameObject with transport - AutoSetup handles everything else
            var go = new GameObject("NetworkManager");
            Undo.RegisterCreatedObjectUndo(go, "Setup EOS Native Scene");

            var transport = go.AddComponent<EOSNativeTransport>();

            // Select the new object
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log("[EOSNativeMenu] Scene setup complete! All required components have been created.");
        }

        /// <summary>
        /// Validates Setup Scene menu item - always available.
        /// </summary>
        [MenuItem(MenuRoot + "Setup Scene", true)]
        public static bool SetupSceneValidate()
        {
            return true;
        }

        /// <summary>
        /// Selects and pings the SampleEOSConfig asset in the Project window.
        /// </summary>
        [MenuItem(MenuRoot + "Select Config", priority = 1)]
        public static void SelectConfig()
        {
            var guids = AssetDatabase.FindAssets("SampleEOSConfig t:EOSConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    Debug.Log($"[EOSNativeMenu] Selected config: {path}");
                    return;
                }
            }

            // No SampleEOSConfig found - try to find any EOSConfig
            guids = AssetDatabase.FindAssets("t:EOSConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var asset = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    Debug.Log($"[EOSNativeMenu] Selected config: {path}");
                    return;
                }
            }

            // No config found - offer to create one
            if (EditorUtility.DisplayDialog(
                "EOSConfig Not Found",
                "No EOSConfig asset found in the project.\n\nWould you like to create one?",
                "Create Config",
                "Cancel"))
            {
                CreateEOSConfig();
            }
        }

        /// <summary>
        /// Creates a new EOSConfig asset.
        /// </summary>
        [MenuItem(MenuRoot + "Create New Config", priority = 2)]
        public static void CreateEOSConfig()
        {
            var config = ScriptableObject.CreateInstance<EOSConfig>();

            // Ensure directory exists
            var directory = "Assets/FishNet.Transport.EOSNative";
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder("Assets", "FishNet.Transport.EOSNative");
            }

            var path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/NewEOSConfig.asset");
            AssetDatabase.CreateAsset(config, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);

            Debug.Log($"[EOSNativeMenu] Created new EOSConfig at {path}. Configure your EOS credentials in the Inspector.");
        }

        [MenuItem(MenuRoot + "Create New Config", true)]
        public static bool CreateEOSConfigValidate()
        {
            return true;
        }

        /// <summary>
        /// Logs platform info to console (useful for crossplay debugging).
        /// </summary>
        [MenuItem(MenuRoot + "Log Platform Info", priority = 51)]
        public static void LogPlatformInfo()
        {
            EOSPlatformHelper.LogPlatformInfo();
        }

        #region Validation Utilities

        /// <summary>
        /// Validates the current scene setup and reports any issues.
        /// </summary>
        [MenuItem(MenuRoot + "Validate Setup", priority = 50)]
        public static void ValidateSetup()
        {
            var issues = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();

            // Check for EOSNativeTransport
            var transport = UnityEngine.Object.FindAnyObjectByType<EOSNativeTransport>();
            if (transport == null)
            {
                issues.Add("EOSNativeTransport not found in scene");
            }
            else
            {
                // Check for config
                var configField = transport.GetType().GetField("_eosConfig",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (configField != null)
                {
                    var config = configField.GetValue(transport) as EOSConfig;
                    if (config == null)
                    {
                        issues.Add("EOSConfig not assigned on EOSNativeTransport");
                    }
                }
            }

            // Check for NetworkManager
            var networkManager = UnityEngine.Object.FindAnyObjectByType<FishNet.Managing.NetworkManager>();
            if (networkManager == null)
            {
                issues.Add("NetworkManager not found in scene");
            }

            // Check for EOSManager
            var eosManager = UnityEngine.Object.FindAnyObjectByType<EOSManager>();
            if (eosManager == null)
            {
                issues.Add("EOSManager not found in scene");
            }

            // Check for lobby manager
            var lobbyManager = UnityEngine.Object.FindAnyObjectByType<Lobbies.EOSLobbyManager>();
            if (lobbyManager == null)
            {
                warnings.Add("EOSLobbyManager not found (required for lobby features)");
            }

            // Check for voice manager
            var voiceManager = UnityEngine.Object.FindAnyObjectByType<Voice.EOSVoiceManager>();
            if (voiceManager == null)
            {
                warnings.Add("EOSVoiceManager not found (required for voice features)");
            }

            // Check for player spawner
            var playerSpawner = UnityEngine.Object.FindAnyObjectByType<HostMigrationPlayerSpawner>();
            if (playerSpawner == null)
            {
                warnings.Add("HostMigrationPlayerSpawner not found (required for player spawning)");
            }

            // Report results
            if (issues.Count == 0 && warnings.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation Passed",
                    "All required components are properly configured!", "OK");
                Debug.Log("[EOSNativeMenu] Validation passed - all components configured correctly.");
            }
            else
            {
                var message = "";
                if (issues.Count > 0)
                {
                    message += "ERRORS:\n";
                    foreach (var issue in issues)
                    {
                        message += $"  - {issue}\n";
                        Debug.LogError($"[EOSNativeMenu] {issue}");
                    }
                }
                if (warnings.Count > 0)
                {
                    if (issues.Count > 0) message += "\n";
                    message += "WARNINGS:\n";
                    foreach (var warning in warnings)
                    {
                        message += $"  - {warning}\n";
                        Debug.LogWarning($"[EOSNativeMenu] {warning}");
                    }
                }

                message += "\nUse 'Tools > FishNet EOS Native > Setup Scene' to fix these issues.";

                EditorUtility.DisplayDialog("Validation Issues Found", message, "OK");
            }
        }

        #endregion
    }
}
