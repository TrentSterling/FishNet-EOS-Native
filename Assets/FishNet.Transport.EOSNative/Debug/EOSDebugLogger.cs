using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FishNet.Transport.EOSNative.Logging
{
    /// <summary>
    /// Static utility class for centralized debug logging.
    /// Respects EOSDebugSettings for per-category log filtering.
    /// Logs are stripped from release builds via Conditional attributes.
    /// </summary>
    public static class EOSDebugLogger
    {
        /// <summary>
        /// Log a debug message if the category is enabled.
        /// Stripped from release builds.
        /// </summary>
        /// <param name="category">The debug category.</param>
        /// <param name="className">The name of the class logging the message.</param>
        /// <param name="message">The message to log.</param>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("EOS_DEBUG")]
        public static void Log(DebugCategory category, string className, string message)
        {
            if (EOSDebugSettings.Instance.IsCategoryEnabled(category))
            {
                Debug.Log($"[{className}] {message}");
            }
        }

        /// <summary>
        /// Log a debug message with an associated object if the category is enabled.
        /// Stripped from release builds.
        /// </summary>
        /// <param name="category">The debug category.</param>
        /// <param name="className">The name of the class logging the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="context">The Unity object context for the log message.</param>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("EOS_DEBUG")]
        public static void Log(DebugCategory category, string className, string message, UnityEngine.Object context)
        {
            if (EOSDebugSettings.Instance.IsCategoryEnabled(category))
            {
                Debug.Log($"[{className}] {message}", context);
            }
        }

        /// <summary>
        /// Log a warning message if the category is enabled.
        /// Stripped from release builds.
        /// </summary>
        /// <param name="category">The debug category.</param>
        /// <param name="className">The name of the class logging the message.</param>
        /// <param name="message">The message to log.</param>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("EOS_DEBUG")]
        public static void LogWarning(DebugCategory category, string className, string message)
        {
            if (EOSDebugSettings.Instance.IsCategoryEnabled(category))
            {
                Debug.LogWarning($"[{className}] {message}");
            }
        }

        /// <summary>
        /// Log a warning message with an associated object if the category is enabled.
        /// Stripped from release builds.
        /// </summary>
        /// <param name="category">The debug category.</param>
        /// <param name="className">The name of the class logging the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="context">The Unity object context for the log message.</param>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("EOS_DEBUG")]
        public static void LogWarning(DebugCategory category, string className, string message, UnityEngine.Object context)
        {
            if (EOSDebugSettings.Instance.IsCategoryEnabled(category))
            {
                Debug.LogWarning($"[{className}] {message}", context);
            }
        }

        /// <summary>
        /// Log an error message. Errors are always logged regardless of settings.
        /// Not stripped from release builds - errors should always be visible.
        /// </summary>
        /// <param name="className">The name of the class logging the message.</param>
        /// <param name="message">The message to log.</param>
        public static void LogError(string className, string message)
        {
            Debug.LogError($"[{className}] {message}");
        }

        /// <summary>
        /// Log an error message with an associated object.
        /// Errors are always logged regardless of settings.
        /// </summary>
        /// <param name="className">The name of the class logging the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="context">The Unity object context for the log message.</param>
        public static void LogError(string className, string message, UnityEngine.Object context)
        {
            Debug.LogError($"[{className}] {message}", context);
        }

        /// <summary>
        /// Check if a category is enabled without logging.
        /// Useful for expensive logging operations that should be skipped entirely.
        /// </summary>
        /// <param name="category">The category to check.</param>
        /// <returns>True if the category is enabled.</returns>
        public static bool IsEnabled(DebugCategory category)
        {
            return EOSDebugSettings.Instance.IsCategoryEnabled(category);
        }
    }
}
