using System.Text;
using FishNet.Transport.EOSNative.Lobbies;
using UnityEngine;
using UnityEngine.UI;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Simple chat UI for the demo.
    /// Add to a Canvas with the required UI elements.
    /// </summary>
    public class EOSChatUI : MonoBehaviour
    {
        #region Serialized Fields

        [Header("UI References")]
        [SerializeField]
        private Text _chatLog;

        [SerializeField]
        private InputField _chatInput;

        [SerializeField]
        private Button _sendButton;

        [SerializeField]
        private ScrollRect _scrollRect;

        [Header("Settings")]
        [SerializeField]
        private KeyCode _sendKey = KeyCode.Return;

        [SerializeField]
        private KeyCode _toggleKey = KeyCode.T;

        [SerializeField]
        private bool _startVisible = true;

        [SerializeField]
        private int _maxDisplayLines = 50;

        #endregion

        #region Private Fields

        private EOSLobbyChatManager _chatManager;
        private CanvasGroup _canvasGroup;
        private bool _isVisible;
        private readonly StringBuilder _logBuilder = new();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _isVisible = _startVisible;
            UpdateVisibility();
        }

        private void Start()
        {
            _chatManager = EOSLobbyChatManager.Instance;

            if (_chatManager != null)
            {
                _chatManager.OnChatMessageReceived += OnChatMessageReceived;
            }

            if (_sendButton != null)
            {
                _sendButton.onClick.AddListener(OnSendClicked);
            }

            if (_chatInput != null)
            {
                _chatInput.onEndEdit.AddListener(OnInputEndEdit);
            }

            RefreshChatLog();
        }

        private void OnDestroy()
        {
            if (_chatManager != null)
            {
                _chatManager.OnChatMessageReceived -= OnChatMessageReceived;
            }
        }

        private void Update()
        {
            // Toggle visibility
            if (Input.GetKeyDown(_toggleKey) && !IsChatInputFocused())
            {
                _isVisible = !_isVisible;
                UpdateVisibility();
            }

            // Send on Enter
            if (Input.GetKeyDown(_sendKey) && IsChatInputFocused())
            {
                SendCurrentMessage();
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Show the chat UI.
        /// </summary>
        public void Show()
        {
            _isVisible = true;
            UpdateVisibility();
        }

        /// <summary>
        /// Hide the chat UI.
        /// </summary>
        public void Hide()
        {
            _isVisible = false;
            UpdateVisibility();
        }

        /// <summary>
        /// Focus the chat input field.
        /// </summary>
        public void FocusInput()
        {
            if (_chatInput != null)
            {
                _chatInput.Select();
                _chatInput.ActivateInputField();
            }
        }

        #endregion

        #region Private Methods

        private void UpdateVisibility()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = _isVisible ? 1f : 0f;
                _canvasGroup.interactable = _isVisible;
                _canvasGroup.blocksRaycasts = _isVisible;
            }
        }

        private bool IsChatInputFocused()
        {
            return _chatInput != null && _chatInput.isFocused;
        }

        private void OnChatMessageReceived(string puid, string name, string message, long timestamp)
        {
            RefreshChatLog();
        }

        private void RefreshChatLog()
        {
            if (_chatLog == null || _chatManager == null) return;

            _logBuilder.Clear();

            var messages = _chatManager.Messages;
            int startIndex = Mathf.Max(0, messages.Count - _maxDisplayLines);

            for (int i = startIndex; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.IsSystem)
                {
                    _logBuilder.AppendLine($"<color=#888888>{msg}</color>");
                }
                else
                {
                    _logBuilder.AppendLine(msg.ToString());
                }
            }

            _chatLog.text = _logBuilder.ToString();

            // Scroll to bottom
            if (_scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void OnSendClicked()
        {
            SendCurrentMessage();
        }

        private void OnInputEndEdit(string text)
        {
            // Don't send on tab/escape, only on enter
            if (Input.GetKeyDown(_sendKey))
            {
                SendCurrentMessage();
            }
        }

        private void SendCurrentMessage()
        {
            if (_chatInput == null || _chatManager == null) return;

            string message = _chatInput.text.Trim();
            if (string.IsNullOrEmpty(message)) return;

            _chatManager.SendChatMessage(message);
            _chatInput.text = "";

            // Keep focus on input
            FocusInput();
        }

        #endregion
    }
}
