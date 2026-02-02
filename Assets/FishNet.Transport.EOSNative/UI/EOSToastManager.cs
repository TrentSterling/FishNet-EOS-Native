using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Manages toast notifications - non-intrusive popup messages.
    /// Use for events like: invite received, friend joined, connection issues, etc.
    /// </summary>
    public class EOSToastManager : MonoBehaviour
    {
        #region Singleton

        private static EOSToastManager _instance;
        public static EOSToastManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSToastManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSToastManager");
                        _instance = go.AddComponent<EOSToastManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Settings

        [Header("Toast Settings")]
        [SerializeField] private float _defaultDuration = 3f;
        [SerializeField] private int _maxVisibleToasts = 3;
        [SerializeField] private ToastPosition _position = ToastPosition.TopRight;
        [SerializeField] private float _toastWidth = 280f;
        [SerializeField] private float _toastHeight = 50f;
        [SerializeField] private float _toastSpacing = 8f;
        [SerializeField] private float _margin = 20f;

        #endregion

        #region Private Fields

        private Queue<Toast> _pendingToasts = new();
        private List<Toast> _activeToasts = new();
        private GUIStyle _toastStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _messageStyle;
        private bool _stylesInitialized;

        #endregion

        #region Public Properties

        public bool Enabled { get; set; } = true;
        public float DefaultDuration { get => _defaultDuration; set => _defaultDuration = value; }
        public ToastPosition Position { get => _position; set => _position = value; }

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

        private void Update()
        {
            // Update active toasts
            for (int i = _activeToasts.Count - 1; i >= 0; i--)
            {
                var toast = _activeToasts[i];
                toast.TimeRemaining -= Time.unscaledDeltaTime;

                if (toast.TimeRemaining <= 0)
                {
                    _activeToasts.RemoveAt(i);
                }
            }

            // Promote pending toasts
            while (_activeToasts.Count < _maxVisibleToasts && _pendingToasts.Count > 0)
            {
                _activeToasts.Add(_pendingToasts.Dequeue());
            }
        }

        private void OnGUI()
        {
            if (!Enabled || _activeToasts.Count == 0) return;

            InitStyles();

            float x, y;
            int direction; // 1 = down, -1 = up

            switch (_position)
            {
                case ToastPosition.TopLeft:
                    x = _margin;
                    y = _margin;
                    direction = 1;
                    break;
                case ToastPosition.TopRight:
                    x = Screen.width - _toastWidth - _margin;
                    y = _margin;
                    direction = 1;
                    break;
                case ToastPosition.BottomLeft:
                    x = _margin;
                    y = Screen.height - _toastHeight - _margin;
                    direction = -1;
                    break;
                case ToastPosition.BottomRight:
                    x = Screen.width - _toastWidth - _margin;
                    y = Screen.height - _toastHeight - _margin;
                    direction = -1;
                    break;
                case ToastPosition.TopCenter:
                    x = (Screen.width - _toastWidth) / 2f;
                    y = _margin;
                    direction = 1;
                    break;
                case ToastPosition.BottomCenter:
                    x = (Screen.width - _toastWidth) / 2f;
                    y = Screen.height - _toastHeight - _margin;
                    direction = -1;
                    break;
                default:
                    x = Screen.width - _toastWidth - _margin;
                    y = _margin;
                    direction = 1;
                    break;
            }

            for (int i = 0; i < _activeToasts.Count; i++)
            {
                var toast = _activeToasts[i];
                float toastY = y + (i * direction * (_toastHeight + _toastSpacing));

                // Fade out animation
                float alpha = Mathf.Clamp01(toast.TimeRemaining / 0.5f);
                Color bgColor = GetToastColor(toast.Type);
                bgColor.a *= alpha;

                DrawToast(new Rect(x, toastY, _toastWidth, _toastHeight), toast, bgColor, alpha);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Show a toast notification.
        /// </summary>
        public static void Show(string message, ToastType type = ToastType.Info, float? duration = null)
        {
            Instance.ShowToast(null, message, type, duration ?? Instance._defaultDuration);
        }

        /// <summary>
        /// Show a toast with title and message.
        /// </summary>
        public static void Show(string title, string message, ToastType type = ToastType.Info, float? duration = null)
        {
            Instance.ShowToast(title, message, type, duration ?? Instance._defaultDuration);
        }

        /// <summary>
        /// Show an info toast.
        /// </summary>
        public static void Info(string message) => Show(message, ToastType.Info);
        public static void Info(string title, string message) => Show(title, message, ToastType.Info);

        /// <summary>
        /// Show a success toast.
        /// </summary>
        public static void Success(string message) => Show(message, ToastType.Success);
        public static void Success(string title, string message) => Show(title, message, ToastType.Success);

        /// <summary>
        /// Show a warning toast.
        /// </summary>
        public static void Warning(string message) => Show(message, ToastType.Warning);
        public static void Warning(string title, string message) => Show(title, message, ToastType.Warning);

        /// <summary>
        /// Show an error toast.
        /// </summary>
        public static void Error(string message) => Show(message, ToastType.Error);
        public static void Error(string title, string message) => Show(title, message, ToastType.Error);

        /// <summary>
        /// Clear all toasts.
        /// </summary>
        public static void ClearAll()
        {
            Instance._activeToasts.Clear();
            Instance._pendingToasts.Clear();
        }

        #endregion

        #region Private Methods

        private void ShowToast(string title, string message, ToastType type, float duration)
        {
            var toast = new Toast
            {
                Title = title,
                Message = message,
                Type = type,
                Duration = duration,
                TimeRemaining = duration
            };

            if (_activeToasts.Count < _maxVisibleToasts)
            {
                _activeToasts.Add(toast);
            }
            else
            {
                _pendingToasts.Enqueue(toast);
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _toastStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8),
                fontSize = 12
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            _messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };

            _stylesInitialized = true;
        }

        private void DrawToast(Rect rect, Toast toast, Color bgColor, float alpha)
        {
            // Background
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;

            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, bgColor);
            tex.Apply();

            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill);
            GUI.backgroundColor = oldColor;

            // Icon
            string icon = toast.Type switch
            {
                ToastType.Success => "\u2714", // Checkmark
                ToastType.Warning => "\u26A0", // Warning
                ToastType.Error => "\u2718",   // X
                _ => "\u2139"                   // Info
            };

            var iconColor = _titleStyle.normal.textColor;
            iconColor.a = alpha;
            var iconStyle = new GUIStyle(_titleStyle) { fontSize = 16, normal = { textColor = iconColor } };
            GUI.Label(new Rect(rect.x + 8, rect.y + 8, 20, 20), icon, iconStyle);

            // Content
            float contentX = rect.x + 32;
            float contentWidth = rect.width - 44;

            var titleColor = _titleStyle.normal.textColor;
            titleColor.a = alpha;
            var messageColor = _messageStyle.normal.textColor;
            messageColor.a = alpha;

            if (!string.IsNullOrEmpty(toast.Title))
            {
                var titleStyleAlpha = new GUIStyle(_titleStyle) { normal = { textColor = titleColor } };
                var messageStyleAlpha = new GUIStyle(_messageStyle) { normal = { textColor = messageColor } };

                GUI.Label(new Rect(contentX, rect.y + 6, contentWidth, 18), toast.Title, titleStyleAlpha);
                GUI.Label(new Rect(contentX, rect.y + 24, contentWidth, rect.height - 30), toast.Message, messageStyleAlpha);
            }
            else
            {
                var messageStyleAlpha = new GUIStyle(_messageStyle) { normal = { textColor = messageColor } };
                GUI.Label(new Rect(contentX, rect.y + 8, contentWidth, rect.height - 16), toast.Message, messageStyleAlpha);
            }
        }

        private Color GetToastColor(ToastType type)
        {
            return type switch
            {
                ToastType.Success => new Color(0.2f, 0.5f, 0.2f, 0.95f),
                ToastType.Warning => new Color(0.6f, 0.5f, 0.1f, 0.95f),
                ToastType.Error => new Color(0.6f, 0.2f, 0.2f, 0.95f),
                _ => new Color(0.2f, 0.3f, 0.5f, 0.95f) // Info
            };
        }

        #endregion

        #region Nested Types

        private class Toast
        {
            public string Title;
            public string Message;
            public ToastType Type;
            public float Duration;
            public float TimeRemaining;
        }

        #endregion
    }

    /// <summary>
    /// Toast notification type.
    /// </summary>
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Screen position for toast notifications.
    /// </summary>
    public enum ToastPosition
    {
        TopLeft,
        TopCenter,
        TopRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }
}
