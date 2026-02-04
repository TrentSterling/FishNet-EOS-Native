using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Input types for matchmaking.
    /// </summary>
    public enum InputType
    {
        /// <summary>Unknown or undetected input.</summary>
        Unknown,
        /// <summary>Keyboard and mouse input.</summary>
        KeyboardMouse,
        /// <summary>Gamepad/controller input.</summary>
        Controller,
        /// <summary>Touch screen input.</summary>
        Touch,
        /// <summary>VR motion controllers.</summary>
        VRController
    }

    /// <summary>
    /// Detects and tracks current input type for input-based matchmaking.
    /// </summary>
    public static class EOSInputHelper
    {
        #region Events

        /// <summary>Fired when input type changes.</summary>
        public static event Action<InputType> OnInputTypeChanged;

        #endregion

        #region Private Fields

        private static InputType _currentInputType = InputType.Unknown;
        private static bool _isInitialized;
        private static float _lastInputTime;

        #endregion

        #region Public Properties

        /// <summary>Current detected input type.</summary>
        public static InputType CurrentInputType
        {
            get
            {
                EnsureInitialized();
                return _currentInputType;
            }
        }

        /// <summary>Input type as string ID for lobby attributes.</summary>
        public static string InputTypeId
        {
            get
            {
                return CurrentInputType switch
                {
                    InputType.KeyboardMouse => "KBM",
                    InputType.Controller => "CTL",
                    InputType.Touch => "TCH",
                    InputType.VRController => "VRC",
                    _ => "UNK"
                };
            }
        }

        /// <summary>Whether current input is keyboard/mouse.</summary>
        public static bool IsKeyboardMouse => CurrentInputType == InputType.KeyboardMouse;

        /// <summary>Whether current input is controller.</summary>
        public static bool IsController => CurrentInputType == InputType.Controller;

        /// <summary>Whether current input is touch.</summary>
        public static bool IsTouch => CurrentInputType == InputType.Touch;

        /// <summary>Whether current input is VR.</summary>
        public static bool IsVR => CurrentInputType == InputType.VRController;

        /// <summary>Time since last input (seconds).</summary>
        public static float TimeSinceLastInput => Time.unscaledTime - _lastInputTime;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize input detection. Called automatically on first access.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // Detect initial input type
            DetectInitialInputType();

            // Subscribe to input events
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onActionChange += OnActionChange;

            // Subscribe to user pairing changes (for switching between devices)
            InputUser.onChange += OnInputUserChange;
        }

        private static void EnsureInitialized()
        {
            if (!_isInitialized)
                Initialize();
        }

        #endregion

        #region Input Detection

        private static void DetectInitialInputType()
        {
            // Check for VR first
            if (EOSPlatformHelper.IsVR)
            {
                SetInputType(InputType.VRController);
                return;
            }

            // Check for touch platforms
            if (Input.touchSupported && (Application.platform == RuntimePlatform.Android ||
                                         Application.platform == RuntimePlatform.IPhonePlayer))
            {
                SetInputType(InputType.Touch);
                return;
            }

            // Check connected devices
            var gamepads = Gamepad.all;
            if (gamepads.Count > 0)
            {
                // Has gamepad connected, but check if keyboard was used more recently
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                if (keyboard != null && keyboard.lastUpdateTime > 0)
                {
                    SetInputType(InputType.KeyboardMouse);
                }
                else
                {
                    SetInputType(InputType.Controller);
                }
            }
            else
            {
                // No gamepad, default to keyboard/mouse
                SetInputType(InputType.KeyboardMouse);
            }
        }

        private static void SetInputType(InputType newType)
        {
            if (_currentInputType == newType) return;

            var oldType = _currentInputType;
            _currentInputType = newType;
            _lastInputTime = Time.unscaledTime;

            Debug.Log($"[EOSInputHelper] Input type changed: {oldType} -> {newType}");
            OnInputTypeChanged?.Invoke(newType);
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
            {
                // A new device was connected
                if (device is Gamepad)
                {
                    // Gamepad connected, but don't switch automatically
                    // Wait for actual input
                }
            }
            else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                // A device was disconnected
                if (device is Gamepad && _currentInputType == InputType.Controller)
                {
                    // If we were using controller and it disconnected, switch to KB/M
                    if (Gamepad.all.Count == 0)
                    {
                        SetInputType(InputType.KeyboardMouse);
                    }
                }
            }
        }

        private static void OnActionChange(object obj, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed) return;

            if (obj is InputAction action)
            {
                var control = action.activeControl;
                if (control != null)
                {
                    UpdateInputTypeFromDevice(control.device);
                }
            }
        }

        private static void OnInputUserChange(InputUser user, InputUserChange change, InputDevice device)
        {
            if (change == InputUserChange.ControlSchemeChanged && device != null)
            {
                UpdateInputTypeFromDevice(device);
            }
        }

        private static void UpdateInputTypeFromDevice(InputDevice device)
        {
            InputType newType = _currentInputType;

            if (device is Keyboard || device is Mouse)
            {
                newType = InputType.Touch; // Could be touch with keyboard
                if (!EOSPlatformHelper.IsMobile)
                    newType = InputType.KeyboardMouse;
            }
            else if (device is Gamepad)
            {
                newType = InputType.Controller;
            }
            else if (device is Touchscreen)
            {
                newType = InputType.Touch;
            }
            else if (device.name.Contains("VR") || device.name.Contains("Oculus") ||
                     device.name.Contains("XR") || device.name.Contains("Quest"))
            {
                newType = InputType.VRController;
            }

            _lastInputTime = Time.unscaledTime;
            SetInputType(newType);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get display name for input type.
        /// </summary>
        public static string GetInputTypeName(InputType type)
        {
            return type switch
            {
                InputType.KeyboardMouse => "Keyboard & Mouse",
                InputType.Controller => "Controller",
                InputType.Touch => "Touch",
                InputType.VRController => "VR",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get display name for input type ID.
        /// </summary>
        public static string GetInputTypeName(string inputTypeId)
        {
            return inputTypeId switch
            {
                "KBM" => "Keyboard & Mouse",
                "CTL" => "Controller",
                "TCH" => "Touch",
                "VRC" => "VR",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Get icon for input type.
        /// </summary>
        public static string GetInputTypeIcon(InputType type)
        {
            return type switch
            {
                InputType.KeyboardMouse => "⌨️",
                InputType.Controller => "🎮",
                InputType.Touch => "📱",
                InputType.VRController => "👓",
                _ => "❓"
            };
        }

        /// <summary>
        /// Get icon for input type ID.
        /// </summary>
        public static string GetInputTypeIcon(string inputTypeId)
        {
            return inputTypeId switch
            {
                "KBM" => "⌨️",
                "CTL" => "🎮",
                "TCH" => "📱",
                "VRC" => "👓",
                _ => "❓"
            };
        }

        /// <summary>
        /// Parse input type from string ID.
        /// </summary>
        public static InputType ParseInputType(string inputTypeId)
        {
            return inputTypeId switch
            {
                "KBM" => InputType.KeyboardMouse,
                "CTL" => InputType.Controller,
                "TCH" => InputType.Touch,
                "VRC" => InputType.VRController,
                _ => InputType.Unknown
            };
        }

        /// <summary>
        /// Get input type ID string from InputType enum.
        /// </summary>
        public static string GetInputTypeId(InputType type)
        {
            return type switch
            {
                InputType.KeyboardMouse => "KBM",
                InputType.Controller => "CTL",
                InputType.Touch => "TCH",
                InputType.VRController => "VRC",
                _ => "UNK"
            };
        }

        /// <summary>
        /// Get all input type IDs.
        /// </summary>
        public static string[] AllInputTypeIds => new[] { "KBM", "CTL", "TCH", "VRC" };

        /// <summary>
        /// Get traditional input type IDs (non-VR).
        /// </summary>
        public static string[] TraditionalInputTypeIds => new[] { "KBM", "CTL" };

        /// <summary>
        /// Check if two input types are considered "fair" for matchmaking.
        /// By default, KBM vs Controller is considered unfair.
        /// </summary>
        public static bool AreInputTypesFair(InputType a, InputType b)
        {
            // Same type is always fair
            if (a == b) return true;

            // Touch and controller are roughly fair
            if ((a == InputType.Touch && b == InputType.Controller) ||
                (a == InputType.Controller && b == InputType.Touch))
                return true;

            // VR is only fair with VR
            if (a == InputType.VRController || b == InputType.VRController)
                return a == b;

            // KBM vs anything else is considered unfair in competitive games
            return false;
        }

        #endregion
    }
}
