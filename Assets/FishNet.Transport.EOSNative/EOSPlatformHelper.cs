using System;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Platform-specific helper for EOS transport.
    /// Handles Android, Quest, iOS, Windows, macOS, Linux differences.
    /// </summary>
    public static class EOSPlatformHelper
    {
        #region Platform Detection

        /// <summary>Current platform type.</summary>
        public static EOSPlatformType CurrentPlatform
        {
            get
            {
#if UNITY_EDITOR
                return EOSPlatformType.Editor;
#elif UNITY_ANDROID
                return IsQuest ? EOSPlatformType.Quest : EOSPlatformType.Android;
#elif UNITY_IOS
                return EOSPlatformType.iOS;
#elif UNITY_STANDALONE_WIN
                return EOSPlatformType.Windows;
#elif UNITY_STANDALONE_OSX
                return EOSPlatformType.macOS;
#elif UNITY_STANDALONE_LINUX
                return EOSPlatformType.Linux;
#elif UNITY_WEBGL
                return EOSPlatformType.WebGL;
#else
                return EOSPlatformType.Unknown;
#endif
            }
        }

        /// <summary>True if running on Quest VR headset.</summary>
        public static bool IsQuest
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // Check for Oculus/Meta device
                return SystemInfo.deviceModel.Contains("Quest") ||
                       SystemInfo.deviceModel.Contains("Oculus");
#else
                return false;
#endif
            }
        }

        /// <summary>True if running on mobile platform (Android/iOS).</summary>
        public static bool IsMobile => CurrentPlatform == EOSPlatformType.Android ||
                                       CurrentPlatform == EOSPlatformType.iOS ||
                                       CurrentPlatform == EOSPlatformType.Quest;

        /// <summary>True if running on VR platform.</summary>
        public static bool IsVR => IsQuest; // Extend for other VR platforms as needed

        /// <summary>True if running in editor.</summary>
        public static bool IsEditor => CurrentPlatform == EOSPlatformType.Editor;

        /// <summary>True if running as standalone build.</summary>
        public static bool IsStandalone => CurrentPlatform == EOSPlatformType.Windows ||
                                           CurrentPlatform == EOSPlatformType.macOS ||
                                           CurrentPlatform == EOSPlatformType.Linux;

        /// <summary>True if platform supports EOS overlay.</summary>
        public static bool SupportsOverlay
        {
            get
            {
                // Overlay only works on desktop platforms
                return CurrentPlatform == EOSPlatformType.Windows ||
                       CurrentPlatform == EOSPlatformType.macOS;
            }
        }

        /// <summary>True if platform supports voice chat.</summary>
        public static bool SupportsVoice
        {
            get
            {
                // Voice works on most platforms
                return CurrentPlatform != EOSPlatformType.WebGL &&
                       CurrentPlatform != EOSPlatformType.Unknown;
            }
        }

        #endregion

        #region Platform-Specific Settings

        /// <summary>Get recommended max packet size for current platform.</summary>
        public static int RecommendedMaxPacketSize
        {
            get
            {
                // Mobile has lower bandwidth, use smaller packets
                if (IsMobile)
                    return 1000;

                // Desktop can use full packet size
                return 1170; // EOS MAX_PACKET_SIZE
            }
        }

        /// <summary>Get recommended tick rate for current platform.</summary>
        public static int RecommendedTickRate
        {
            get
            {
                if (IsQuest)
                    return 60; // Quest runs at 72-120Hz but network can be slower

                if (IsMobile)
                    return 30; // Mobile devices may struggle with high tick rates

                return 60; // Desktop default
            }
        }

        /// <summary>Get recommended heartbeat interval.</summary>
        public static float RecommendedHeartbeatInterval
        {
            get
            {
                if (IsMobile)
                    return 2f; // Mobile has more latency, use longer interval

                return 1f; // Desktop default
            }
        }

        /// <summary>Get device model string for device ID creation.</summary>
        public static string DeviceModel
        {
            get
            {
                string model = SystemInfo.deviceModel;

#if UNITY_EDITOR && PARREL_SYNC
                // ParrelSync clone support
                if (ParrelSync.ClonesManager.IsClone())
                {
                    model += "_" + ParrelSync.ClonesManager.GetCurrentProjectPath().GetHashCode();
                }
#endif

                return model;
            }
        }

        #endregion

        #region Android Permissions

        /// <summary>Request microphone permission for voice chat (Android only).</summary>
        public static void RequestMicrophonePermission(Action<bool> callback = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += (perm) => callback?.Invoke(true);
                callbacks.PermissionDenied += (perm) => callback?.Invoke(false);
                callbacks.PermissionDeniedAndDontAskAgain += (perm) => callback?.Invoke(false);
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            }
            else
            {
                callback?.Invoke(true);
            }
#else
            callback?.Invoke(true); // Non-Android platforms don't need explicit permission
#endif
        }

        /// <summary>Check if microphone permission is granted.</summary>
        public static bool HasMicrophonePermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
                return true;
#endif
            }
        }

        /// <summary>Request internet permission (usually auto-granted on Android).</summary>
        public static bool HasInternetPermission
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // Internet permission is usually auto-granted via manifest
                return true;
#else
                return true;
#endif
            }
        }

        #endregion

        #region Crossplay Support

        /// <summary>
        /// Get platform ID string for EOS crossplay filtering.
        /// Use this with lobby AllowedPlatformIds.
        /// </summary>
        public static string PlatformId
        {
            get
            {
                return CurrentPlatform switch
                {
                    EOSPlatformType.Windows => "WIN",
                    EOSPlatformType.macOS => "MAC",
                    EOSPlatformType.Linux => "LNX",
                    EOSPlatformType.Android => "AND",
                    EOSPlatformType.iOS => "IOS",
                    EOSPlatformType.Quest => "OVR", // Oculus/Meta VR
                    EOSPlatformType.Editor => "WIN", // Assume Windows editor
                    _ => "UNK"
                };
            }
        }

        /// <summary>
        /// Get all platform IDs for full crossplay.
        /// </summary>
        public static string[] AllPlatformIds => new[]
        {
            "WIN", "MAC", "LNX", "AND", "IOS", "OVR"
        };

        /// <summary>
        /// Get desktop platform IDs (PC crossplay only).
        /// </summary>
        public static string[] DesktopPlatformIds => new[]
        {
            "WIN", "MAC", "LNX"
        };

        /// <summary>
        /// Get mobile platform IDs (mobile crossplay only).
        /// </summary>
        public static string[] MobilePlatformIds => new[]
        {
            "AND", "IOS", "OVR"
        };

        #endregion

        #region Quest-Specific

        /// <summary>
        /// Get Quest-specific settings for optimal performance.
        /// </summary>
        public static QuestSettings GetQuestSettings()
        {
            return new QuestSettings
            {
                // Quest has limited bandwidth and processing
                MaxPlayersRecommended = 8,
                MaxVoiceParticipants = 4,
                NetworkTickRate = 60,
                HeartbeatInterval = 2f,
                // Quest 2/3 have different capabilities
                IsQuest3 = SystemInfo.deviceModel.Contains("Quest 3"),
                IsQuest2 = SystemInfo.deviceModel.Contains("Quest 2") && !SystemInfo.deviceModel.Contains("Quest 3"),
                IsQuestPro = SystemInfo.deviceModel.Contains("Quest Pro")
            };
        }

        /// <summary>
        /// Initialize Quest-specific EOS settings.
        /// Call before EOSManager.Initialize().
        /// </summary>
        public static void InitializeQuestSettings()
        {
            if (!IsQuest) return;

            // Quest-specific optimizations
            var settings = GetQuestSettings();

            // Log Quest device info
            Debug.Log($"[EOSPlatformHelper] Quest detected: {SystemInfo.deviceModel}");
            Debug.Log($"[EOSPlatformHelper] Quest settings: maxPlayers={settings.MaxPlayersRecommended}, tickRate={settings.NetworkTickRate}");

            // Request microphone permission early for voice chat
            RequestMicrophonePermission(granted =>
            {
                if (!granted)
                {
                    Debug.LogWarning("[EOSPlatformHelper] Microphone permission denied - voice chat will be unavailable");
                }
            });
        }

        #endregion

        #region Utility

        /// <summary>
        /// Log current platform information.
        /// </summary>
        public static void LogPlatformInfo()
        {
            Debug.Log($"[EOSPlatformHelper] Platform: {CurrentPlatform}");
            Debug.Log($"[EOSPlatformHelper] Device: {SystemInfo.deviceModel}");
            Debug.Log($"[EOSPlatformHelper] OS: {SystemInfo.operatingSystem}");
            Debug.Log($"[EOSPlatformHelper] Platform ID: {PlatformId}");
            Debug.Log($"[EOSPlatformHelper] IsQuest: {IsQuest}, IsMobile: {IsMobile}, IsVR: {IsVR}");
            Debug.Log($"[EOSPlatformHelper] Supports Overlay: {SupportsOverlay}, Voice: {SupportsVoice}");
        }

        #endregion
    }

    /// <summary>
    /// Platform types for EOS.
    /// </summary>
    public enum EOSPlatformType
    {
        Unknown,
        Windows,
        macOS,
        Linux,
        Android,
        iOS,
        Quest,
        WebGL,
        Editor
    }

    /// <summary>
    /// Quest-specific settings.
    /// </summary>
    [Serializable]
    public struct QuestSettings
    {
        public int MaxPlayersRecommended;
        public int MaxVoiceParticipants;
        public int NetworkTickRate;
        public float HeartbeatInterval;
        public bool IsQuest2;
        public bool IsQuest3;
        public bool IsQuestPro;
    }

    // Editor window removed - use LogPlatformInfo() or check F1 debug UI instead
}
