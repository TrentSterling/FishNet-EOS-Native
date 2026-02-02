using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FishNet.Transport.EOSNative.Voice;
using FishNet.Transport.EOSNative.Lobbies;
using Epic.OnlineServices.RTCAudio;

namespace FishNet.Transport.EOSNative.DebugUI
{
    /// <summary>
    /// DEPRECATED: Voice debug is now integrated into EOSNativeUI (F1 -> Voice tab).
    /// This component is kept for backwards compatibility but is disabled by default.
    /// </summary>
    [AddComponentMenu("")] // Hide from menu
    public class EOSVoiceDebugPanel : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("DEPRECATED: Use EOSNativeUI (F1) -> Voice tab instead.")]
        [SerializeField] private Key _toggleKey = Key.F3;
        [SerializeField] private bool _showPanel = false;
        [SerializeField] private bool _enabled = false; // Disabled by default
        [SerializeField] private Rect _windowRect = new Rect(320, 10, 280, 400);

        // Speaking detection via audio levels
        private readonly Dictionary<string, float> _audioLevels = new();
        private readonly Dictionary<string, float> _peakLevels = new();
        private float _localInputLevel;
        private float _localPeakLevel;
        private float _peakDecay = 0.95f;

        // GUI Styles
        private GUIStyle _windowStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _greenStyle;
        private GUIStyle _yellowStyle;
        private GUIStyle _redStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _smallButtonStyle;
        private GUIStyle _boxStyle;
        private GUIStyle _miniLabelStyle;
        private GUIStyle _speakingBarBgStyle;
        private bool _stylesInitialized;
        private Texture2D _darkBgTexture;
        private Texture2D _sectionBgTexture;
        private Texture2D _levelBarBgTexture;
        private Texture2D _levelBarFgTexture;
        private Texture2D _levelBarPeakTexture;

        private void Update()
        {
            if (!_enabled) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame)
            {
                _showPanel = !_showPanel;
            }

            // Update audio levels from voice players
            UpdateAudioLevels();
        }

        private void UpdateAudioLevels()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected) return;

            // Decay peak levels
            _localPeakLevel *= _peakDecay;
            foreach (var key in new List<string>(_peakLevels.Keys))
            {
                _peakLevels[key] *= _peakDecay;
            }

            // Get local input level (simulated - would need RTCAudio input callback)
            // For now, we use speaking state as a proxy
            _localInputLevel = voiceManager.IsMuted ? 0f : 0.3f; // Placeholder

            // Get remote participant levels from voice players
            var voicePlayers = FindObjectsByType<EOSVoicePlayer>(FindObjectsSortMode.None);
            foreach (var player in voicePlayers)
            {
                if (string.IsNullOrEmpty(player.ParticipantPuid)) continue;

                // Use queued frames as a proxy for audio level
                float level = Mathf.Clamp01(player.QueuedFrames / 30f);
                if (player.IsSpeaking) level = Mathf.Max(level, 0.5f);

                _audioLevels[player.ParticipantPuid] = level;

                if (!_peakLevels.ContainsKey(player.ParticipantPuid))
                    _peakLevels[player.ParticipantPuid] = 0f;

                _peakLevels[player.ParticipantPuid] = Mathf.Max(_peakLevels[player.ParticipantPuid], level);
            }
        }

        private void OnGUI()
        {
            if (!_enabled || !_showPanel) return;

            InitializeStyles();
            _windowRect = GUI.Window(100, _windowRect, DrawWindow, "", _windowStyle);

            // Keep on screen
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;

            _darkBgTexture = MakeTexture(2, 2, new Color(0.1f, 0.12f, 0.18f, 0.95f));
            _sectionBgTexture = MakeTexture(2, 2, new Color(0.15f, 0.17f, 0.22f, 1f));
            _levelBarBgTexture = MakeTexture(2, 2, new Color(0.2f, 0.2f, 0.25f, 1f));
            _levelBarFgTexture = MakeTexture(2, 2, new Color(0.3f, 0.8f, 0.4f, 1f));
            _levelBarPeakTexture = MakeTexture(2, 2, new Color(1f, 0.9f, 0.3f, 1f));

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(8, 8, 8, 8),
                normal = { background = _darkBgTexture, textColor = Color.white }
            };

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.5f, 0.9f, 1f) },
                margin = new RectOffset(0, 0, 5, 8)
            };

            _sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.85f, 0.9f) },
                margin = new RectOffset(0, 0, 6, 3)
            };

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.7f, 0.7f, 0.75f) }
            };

            _valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _greenStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(0.4f, 1f, 0.4f) } };
            _yellowStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
            _redStyle = new GUIStyle(_valueStyle) { normal = { textColor = new Color(1f, 0.4f, 0.4f) } };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 28
            };

            _smallButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fixedHeight = 22
            };

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _sectionBgTexture },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 3, 3)
            };

            _miniLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.5f, 0.5f, 0.55f) }
            };

            _speakingBarBgStyle = new GUIStyle()
            {
                normal = { background = _levelBarBgTexture }
            };

            _stylesInitialized = true;
        }

        private static Texture2D MakeTexture(int w, int h, Color color)
        {
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label("VOICE DEBUG", _headerStyle);
            GUILayout.Label($"Press {_toggleKey} to toggle", _miniLabelStyle);

            DrawRTCStatus();
            GUILayout.Space(4);
            DrawLocalMic();
            GUILayout.Space(4);
            DrawParticipants();
            GUILayout.Space(4);
            DrawControls();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 40));
        }

        private void DrawRTCStatus()
        {
            GUILayout.Label("RTC STATUS", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            var lobbyManager = EOSLobbyManager.Instance;

            if (voiceManager == null)
            {
                GUILayout.Label("EOSVoiceManager not found", _redStyle);
                GUILayout.EndVertical();
                return;
            }

            // Connection status
            GUILayout.BeginHorizontal();
            GUILayout.Label("RTC Room:", _labelStyle, GUILayout.Width(70));
            GUILayout.Label(voiceManager.IsConnected ? "Connected" : "Disconnected",
                voiceManager.IsConnected ? _greenStyle : _redStyle);
            GUILayout.EndHorizontal();

            // Room name
            if (!string.IsNullOrEmpty(voiceManager.CurrentRoomName))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Room:", _labelStyle, GUILayout.Width(70));
                string shortRoom = voiceManager.CurrentRoomName.Length > 20
                    ? voiceManager.CurrentRoomName.Substring(0, 17) + "..."
                    : voiceManager.CurrentRoomName;
                GUILayout.Label(shortRoom, _valueStyle);
                GUILayout.EndHorizontal();
            }

            // Voice enabled (check if voice manager is connected as proxy for voice being enabled)
            GUILayout.BeginHorizontal();
            GUILayout.Label("Voice:", _labelStyle, GUILayout.Width(70));
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                bool voiceEnabled = voiceManager.IsVoiceEnabled;
                GUILayout.Label(voiceEnabled ? "Enabled" : "Disabled", voiceEnabled ? _greenStyle : _yellowStyle);
            }
            else
            {
                GUILayout.Label("No Lobby", _labelStyle);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawLocalMic()
        {
            GUILayout.Label("LOCAL MIC", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected)
            {
                GUILayout.Label("Not connected to voice", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Mute status
            GUILayout.BeginHorizontal();
            GUILayout.Label("Status:", _labelStyle, GUILayout.Width(55));
            GUILayout.Label(voiceManager.IsMuted ? "MUTED" : "Active",
                voiceManager.IsMuted ? _redStyle : _greenStyle);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // Input level bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("Level:", _labelStyle, GUILayout.Width(55));
            DrawLevelBar(_localInputLevel, _localPeakLevel, 150);
            GUILayout.Label($"{(_localInputLevel * 100):F0}%", _miniLabelStyle, GUILayout.Width(35));
            GUILayout.EndHorizontal();

            // Mute button
            GUILayout.Space(4);
            GUI.backgroundColor = voiceManager.IsMuted ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.4f);
            if (GUILayout.Button(voiceManager.IsMuted ? "UNMUTE" : "MUTE", _buttonStyle))
            {
                voiceManager.ToggleMute();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }

        private void DrawParticipants()
        {
            GUILayout.Label("VOICE PARTICIPANTS", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null || !voiceManager.IsConnected)
            {
                GUILayout.Label("Not connected", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            // Get ALL participants from voice manager (not just ones with voice players)
            var participants = voiceManager.GetAllParticipants();

            if (participants.Count == 0)
            {
                GUILayout.Label("No remote participants", _labelStyle);
                GUILayout.EndVertical();
                return;
            }

            foreach (var puid in participants)
            {
                if (string.IsNullOrEmpty(puid)) continue;

                DrawParticipantRow(puid, voiceManager);
            }

            GUILayout.EndVertical();
        }

        private void DrawParticipantRow(string puid, EOSVoiceManager voiceManager)
        {
            string shortPuid = puid.Length > 8 ? puid.Substring(0, 8) + "..." : puid;
            bool isSpeaking = voiceManager.IsSpeaking(puid);
            float level = _audioLevels.TryGetValue(puid, out float l) ? l : 0f;
            float peak = _peakLevels.TryGetValue(puid, out float p) ? p : 0f;

            GUILayout.BeginVertical(_boxStyle);

            // Name + speaking indicator
            GUILayout.BeginHorizontal();
            string speakIcon = isSpeaking ? "\u25CF" : "\u25CB"; // Filled vs empty circle
            var iconStyle = new GUIStyle(_valueStyle) { normal = { textColor = isSpeaking ? new Color(0.3f, 1f, 0.3f) : new Color(0.5f, 0.5f, 0.5f) } };
            GUILayout.Label(speakIcon, iconStyle, GUILayout.Width(15));
            GUILayout.Label(shortPuid, _valueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(isSpeaking ? "Speaking" : "Silent", isSpeaking ? _greenStyle : _labelStyle);
            GUILayout.EndHorizontal();

            // Level bar
            GUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(15));
            DrawLevelBar(level, peak, 120);
            GUILayout.FlexibleSpace();

            // Mute button for this participant
            if (GUILayout.Button("Mute", _smallButtonStyle, GUILayout.Width(45)))
            {
                voiceManager.SetParticipantMuted(puid, true);
            }
            GUILayout.EndHorizontal();

            // Audio status
            var audioStatus = voiceManager.GetParticipantAudioStatus(puid);
            if (audioStatus != RTCAudioStatus.Enabled)
            {
                GUILayout.Label($"Audio: {audioStatus}", _yellowStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawLevelBar(float level, float peak, float width)
        {
            Rect bgRect = GUILayoutUtility.GetRect(width, 12);

            // Background
            GUI.DrawTexture(bgRect, _levelBarBgTexture);

            // Level fill
            if (level > 0.01f)
            {
                Rect fillRect = new Rect(bgRect.x + 1, bgRect.y + 1, (bgRect.width - 2) * level, bgRect.height - 2);
                GUI.DrawTexture(fillRect, _levelBarFgTexture);
            }

            // Peak marker
            if (peak > 0.01f)
            {
                float peakX = bgRect.x + (bgRect.width - 2) * peak;
                Rect peakRect = new Rect(peakX, bgRect.y + 1, 2, bgRect.height - 2);
                GUI.DrawTexture(peakRect, _levelBarPeakTexture);
            }
        }

        private void DrawControls()
        {
            GUILayout.Label("QUICK ACTIONS", _sectionStyle);
            GUILayout.BeginVertical(_boxStyle);

            var voiceManager = EOSVoiceManager.Instance;
            bool connected = voiceManager != null && voiceManager.IsConnected;

            GUI.enabled = connected;

            GUILayout.BeginHorizontal();

            // Mute All
            GUI.backgroundColor = new Color(0.8f, 0.5f, 0.3f);
            if (GUILayout.Button("Mute All", _smallButtonStyle))
            {
                MuteAllParticipants();
            }

            // Unmute All
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.5f);
            if (GUILayout.Button("Unmute All", _smallButtonStyle))
            {
                UnmuteAllParticipants();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            // Volume controls
            GUILayout.Space(4);
            GUILayout.Label("Master Volume (affects all participants)", _miniLabelStyle);

            GUI.enabled = true;

            GUILayout.EndVertical();
        }

        private void MuteAllParticipants()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            var players = FindObjectsByType<EOSVoicePlayer>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (!string.IsNullOrEmpty(player.ParticipantPuid))
                {
                    voiceManager.SetParticipantMuted(player.ParticipantPuid, true);
                }
            }
        }

        private void UnmuteAllParticipants()
        {
            var voiceManager = EOSVoiceManager.Instance;
            if (voiceManager == null) return;

            var players = FindObjectsByType<EOSVoicePlayer>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                if (!string.IsNullOrEmpty(player.ParticipantPuid))
                {
                    voiceManager.SetParticipantMuted(player.ParticipantPuid, false);
                }
            }
        }
    }
}
