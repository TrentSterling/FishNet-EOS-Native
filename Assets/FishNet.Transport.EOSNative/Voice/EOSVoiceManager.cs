using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.RTC;
using Epic.OnlineServices.RTCAudio;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Voice
{
    /// <summary>
    /// Manages EOS RTC voice chat for lobbies.
    /// Independent of FishNet - can work with any networking solution.
    /// Voice is tied to lobbies (not P2P), so it persists through host migration.
    /// </summary>
    public class EOSVoiceManager : MonoBehaviour
    {
        #region Singleton

        private static EOSVoiceManager _instance;

        /// <summary>
        /// The singleton instance of EOSVoiceManager.
        /// </summary>
        public static EOSVoiceManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSVoiceManager>();
#else
                    _instance = FindObjectOfType<EOSVoiceManager>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when voice connection state changes.
        /// </summary>
        public event Action<bool> OnVoiceConnectionChanged;

        /// <summary>
        /// Fired when a participant starts/stops speaking.
        /// Parameters: PUID string, isSpeaking bool.
        /// </summary>
        public event Action<string, bool> OnParticipantSpeaking;

        /// <summary>
        /// Fired when audio frames are received for a participant.
        /// Subscribe to get raw audio for custom playback.
        /// WARNING: Called from audio thread - do not perform heavy operations!
        /// Parameters: PUID string, audio frames (int16 samples).
        /// </summary>
        public event Action<string, short[]> OnAudioFrameReceived;

        /// <summary>
        /// Fired when a participant's audio status changes (muted/unmuted).
        /// Parameters: PUID string, RTCAudioStatus.
        /// </summary>
        public event Action<string, RTCAudioStatus> OnParticipantAudioStatusChanged;

        #endregion

        #region Public Properties

        /// <summary>
        /// Whether voice is currently connected to the RTC room.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Whether local microphone is muted.
        /// </summary>
        public bool IsMuted { get; private set; }

        /// <summary>
        /// Current RTC room name (from lobby).
        /// </summary>
        public string CurrentRoomName { get; private set; }

        /// <summary>
        /// Whether voice is enabled for the current lobby.
        /// </summary>
        public bool IsVoiceEnabled => !string.IsNullOrEmpty(_currentLobbyId) && !string.IsNullOrEmpty(CurrentRoomName);

        #endregion

        #region Private Fields

        private string _currentLobbyId;
        private NotifyEventHandle _rtcConnectionHandle;
        private NotifyEventHandle _audioBeforeRenderHandle;
        private NotifyEventHandle _participantUpdatedHandle;
        private NotifyEventHandle _participantStatusHandle;

        // Per-participant audio buffers (thread-safe)
        private readonly ConcurrentDictionary<string, ConcurrentQueue<short[]>> _audioBuffers = new();

        // Participant speaking state (main thread only)
        private readonly Dictionary<string, bool> _speakingState = new();

        // Participant audio status (main thread only)
        private readonly Dictionary<string, RTCAudioStatus> _audioStatus = new();

        private LobbyInterface Lobby => EOSManager.Instance?.LobbyInterface;
        private RTCInterface RTC => EOSManager.Instance?.RTCInterface;
        private RTCAudioInterface RTCAudio => EOSManager.Instance?.RTCAudioInterface;
        private ProductUserId LocalUserId => EOSManager.Instance?.LocalProductUserId;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                EOSDebugLogger.LogWarning(DebugCategory.VoiceManager, "EOSVoiceManager", "Duplicate instance detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            // Only call DontDestroyOnLoad if we're a root object (not a child of NetworkManager)
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private void OnDestroy()
        {
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (!_isExitingPlayMode)
            {
                Cleanup();
            }
#else
            Cleanup();
#endif

            if (_instance == this)
            {
                _instance = null;
            }
        }

#if UNITY_EDITOR
        private bool _isExitingPlayMode;

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _isExitingPlayMode = true;
                Cleanup();
            }
        }
#endif

        #endregion

        #region Public API - Called by EOSLobbyManager

        /// <summary>
        /// Called by EOSLobbyManager when a lobby with voice is created.
        /// </summary>
        internal void OnLobbyCreated(string lobbyId)
        {
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Lobby created with voice: {lobbyId}");
            _currentLobbyId = lobbyId;
            RegisterRTCConnectionNotification();
        }

        /// <summary>
        /// Called by EOSLobbyManager when joining a lobby with voice.
        /// </summary>
        internal void OnLobbyJoined(string lobbyId)
        {
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Joined lobby with voice: {lobbyId}");
            _currentLobbyId = lobbyId;
            RegisterRTCConnectionNotification();

            // Check if RTC is already connected (callback may have fired before we registered)
            CheckExistingRTCConnection();
        }

        /// <summary>
        /// Check if RTC is already connected (handles timing issue where callback fires before registration).
        /// </summary>
        private void CheckExistingRTCConnection()
        {
            if (Lobby == null || string.IsNullOrEmpty(_currentLobbyId) || LocalUserId == null)
                return;

            var options = new IsRTCRoomConnectedOptions
            {
                LobbyId = _currentLobbyId,
                LocalUserId = LocalUserId
            };

            var result = Lobby.IsRTCRoomConnected(ref options, out bool isConnected);
            if (result == Result.Success && isConnected && !IsConnected)
            {
                EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", "RTC already connected - initializing audio notifications");
                IsConnected = true;
                GetRTCRoomName();
                RegisterAudioNotifications();
                OnVoiceConnectionChanged?.Invoke(true);
            }
        }

        /// <summary>
        /// Called by EOSLobbyManager when leaving a lobby.
        /// </summary>
        internal void OnLobbyLeft()
        {
            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", "Left lobby - cleaning up voice");
            Cleanup();
        }

        #endregion

        #region Public API - Mute/Volume Controls

        /// <summary>
        /// Mute or unmute the local microphone.
        /// </summary>
        public void SetMuted(bool muted)
        {
            if (RTCAudio == null || string.IsNullOrEmpty(CurrentRoomName))
            {
                EOSDebugLogger.LogWarning(DebugCategory.VoiceManager, "EOSVoiceManager", "Cannot set mute - not connected to voice room.");
                return;
            }

            var options = new UpdateSendingOptions
            {
                LocalUserId = LocalUserId,
                RoomName = CurrentRoomName,
                AudioStatus = muted ? RTCAudioStatus.Disabled : RTCAudioStatus.Enabled
            };

            RTCAudio.UpdateSending(ref options, null, (ref UpdateSendingCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    IsMuted = muted;
                    EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Mic {(muted ? "muted" : "unmuted")}");
                }
                else
                {
                    Debug.LogWarning($"[EOSVoiceManager] Failed to {(muted ? "mute" : "unmute")}: {data.ResultCode}");
                }
            });
        }

        /// <summary>
        /// Toggle the local microphone mute state.
        /// </summary>
        public void ToggleMute()
        {
            SetMuted(!IsMuted);
        }

        /// <summary>
        /// Set volume for a specific participant (0-100, 50 = normal).
        /// </summary>
        public void SetParticipantVolume(string puid, float volume)
        {
            if (RTCAudio == null || string.IsNullOrEmpty(CurrentRoomName))
            {
                EOSDebugLogger.LogWarning(DebugCategory.VoiceManager, "EOSVoiceManager", "Cannot set volume - not connected to voice room.");
                return;
            }

            var participantId = ProductUserId.FromString(puid);
            if (participantId == null || !participantId.IsValid())
            {
                Debug.LogWarning($"[EOSVoiceManager] Invalid participant PUID: {puid}");
                return;
            }

            var options = new UpdateParticipantVolumeOptions
            {
                LocalUserId = LocalUserId,
                RoomName = CurrentRoomName,
                ParticipantId = participantId,
                Volume = Mathf.Clamp(volume, 0f, 100f)
            };

            RTCAudio.UpdateParticipantVolume(ref options, null, (ref UpdateParticipantVolumeCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Volume set for {puid}: {volume}");
                }
                else
                {
                    Debug.LogWarning($"[EOSVoiceManager] Failed to set volume for {puid}: {data.ResultCode}");
                }
            });
        }

        /// <summary>
        /// Mute or unmute a specific participant for local playback.
        /// </summary>
        public void SetParticipantMuted(string puid, bool muted)
        {
            if (RTCAudio == null || string.IsNullOrEmpty(CurrentRoomName))
            {
                EOSDebugLogger.LogWarning(DebugCategory.VoiceManager, "EOSVoiceManager", "Cannot set participant mute - not connected to voice room.");
                return;
            }

            var participantId = ProductUserId.FromString(puid);
            if (participantId == null || !participantId.IsValid())
            {
                Debug.LogWarning($"[EOSVoiceManager] Invalid participant PUID: {puid}");
                return;
            }

            var options = new UpdateReceivingOptions
            {
                LocalUserId = LocalUserId,
                RoomName = CurrentRoomName,
                ParticipantId = participantId,
                AudioEnabled = !muted
            };

            RTCAudio.UpdateReceiving(ref options, null, (ref UpdateReceivingCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Participant {puid} {(muted ? "muted" : "unmuted")} locally");
                }
                else
                {
                    Debug.LogWarning($"[EOSVoiceManager] Failed to {(muted ? "mute" : "unmute")} participant {puid}: {data.ResultCode}");
                }
            });
        }

        #endregion

        #region Public API - Audio Buffers

        /// <summary>
        /// Get queued audio frames for a participant (for custom playback).
        /// Returns false if no frames available.
        /// </summary>
        public bool TryGetAudioFrames(string puid, out short[] frames)
        {
            frames = null;
            if (_audioBuffers.TryGetValue(puid, out var queue))
            {
                return queue.TryDequeue(out frames);
            }
            return false;
        }

        /// <summary>
        /// Get the number of queued audio frames for a participant.
        /// </summary>
        public int GetQueuedFrameCount(string puid)
        {
            if (_audioBuffers.TryGetValue(puid, out var queue))
            {
                return queue.Count;
            }
            return 0;
        }

        /// <summary>
        /// Clear all queued audio frames for a participant.
        /// </summary>
        public void ClearAudioBuffer(string puid)
        {
            if (_audioBuffers.TryGetValue(puid, out var queue))
            {
                while (queue.TryDequeue(out _)) { }
            }
        }

        #endregion

        #region Public API - Participant State

        /// <summary>
        /// Check if a participant is currently speaking.
        /// </summary>
        public bool IsSpeaking(string puid)
        {
            return _speakingState.TryGetValue(puid, out var speaking) && speaking;
        }

        /// <summary>
        /// Get all currently speaking participants.
        /// </summary>
        public List<string> GetSpeakingParticipants()
        {
            var result = new List<string>();
            foreach (var kvp in _speakingState)
            {
                if (kvp.Value)
                {
                    result.Add(kvp.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Get the audio status of a participant.
        /// </summary>
        public RTCAudioStatus GetParticipantAudioStatus(string puid)
        {
            return _audioStatus.TryGetValue(puid, out var status) ? status : RTCAudioStatus.Disabled;
        }

        /// <summary>
        /// Get all known participants in the RTC room (excluding local user).
        /// </summary>
        public List<string> GetAllParticipants()
        {
            // Return all PUIDs we've seen (from _audioStatus which tracks all participants)
            return new List<string>(_audioStatus.Keys);
        }

        /// <summary>
        /// Get the count of participants in the RTC room (excluding local user).
        /// </summary>
        public int ParticipantCount => _audioStatus.Count;

        #endregion

        #region RTC Notifications

        private void RegisterRTCConnectionNotification()
        {
            if (Lobby == null)
            {
                EOSDebugLogger.LogError("EOSVoiceManager", "Lobby interface not available.");
                return;
            }

            // Dispose existing handle if any
            _rtcConnectionHandle?.Dispose();

            var options = new AddNotifyRTCRoomConnectionChangedOptions();

            ulong handle = Lobby.AddNotifyRTCRoomConnectionChanged(ref options, null, OnRTCRoomConnectionChanged);
            _rtcConnectionHandle = new NotifyEventHandle(handle, h => Lobby?.RemoveNotifyRTCRoomConnectionChanged(h));

            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", "Registered for RTC connection notifications");
        }

        private void OnRTCRoomConnectionChanged(ref RTCRoomConnectionChangedCallbackInfo data)
        {
            // Filter to our lobby
            if (data.LobbyId != _currentLobbyId)
            {
                return;
            }

            bool wasConnected = IsConnected;
            IsConnected = data.IsConnected;

            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" RTC Room {(data.IsConnected ? "connected" : "disconnected")} - Lobby: {data.LobbyId}, Reason: {data.DisconnectReason}");

            if (data.IsConnected)
            {
                // Get RTC room name from lobby
                GetRTCRoomName();

                // Start listening for audio
                RegisterAudioNotifications();
            }
            else
            {
                // Cleanup audio notifications
                CleanupAudioNotifications();
                CurrentRoomName = null;

                // Clear participant state
                _speakingState.Clear();
                _audioStatus.Clear();
            }

            // Fire event only if state actually changed
            if (wasConnected != IsConnected)
            {
                OnVoiceConnectionChanged?.Invoke(data.IsConnected);
            }
        }

        private void GetRTCRoomName()
        {
            if (Lobby == null || string.IsNullOrEmpty(_currentLobbyId))
            {
                return;
            }

            var options = new GetRTCRoomNameOptions
            {
                LobbyId = _currentLobbyId,
                LocalUserId = LocalUserId
            };

            var result = Lobby.GetRTCRoomName(ref options, out Utf8String roomName);
            if (result == Result.Success)
            {
                CurrentRoomName = roomName;
                EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" RTC Room Name: {CurrentRoomName}");
            }
            else
            {
                Debug.LogWarning($"[EOSVoiceManager] Failed to get RTC room name: {result}");
            }
        }

        private void RegisterAudioNotifications()
        {
            if (RTCAudio == null || string.IsNullOrEmpty(CurrentRoomName))
            {
                EOSDebugLogger.LogWarning(DebugCategory.VoiceManager, "EOSVoiceManager", "Cannot register audio notifications - RTC not ready.");
                return;
            }

            // Dispose existing handles
            CleanupAudioNotifications();

            // Register for audio frames (per-participant, unmixed)
            var audioOptions = new AddNotifyAudioBeforeRenderOptions
            {
                LocalUserId = LocalUserId,
                RoomName = CurrentRoomName,
                UnmixedAudio = true  // Get per-participant audio for custom playback
            };

            ulong audioHandle = RTCAudio.AddNotifyAudioBeforeRender(ref audioOptions, null, OnAudioBeforeRender);
            _audioBeforeRenderHandle = new NotifyEventHandle(audioHandle, h => RTCAudio?.RemoveNotifyAudioBeforeRender(h));

            // Register for participant status updates (speaking, mute status)
            var participantOptions = new AddNotifyParticipantUpdatedOptions
            {
                LocalUserId = LocalUserId,
                RoomName = CurrentRoomName
            };

            ulong participantHandle = RTCAudio.AddNotifyParticipantUpdated(ref participantOptions, null, OnParticipantUpdated);
            _participantUpdatedHandle = new NotifyEventHandle(participantHandle, h => RTCAudio?.RemoveNotifyParticipantUpdated(h));

            // Register for participant join/leave in RTC room
            if (RTC != null)
            {
                var statusOptions = new AddNotifyParticipantStatusChangedOptions
                {
                    LocalUserId = LocalUserId,
                    RoomName = CurrentRoomName
                };

                ulong statusHandle = RTC.AddNotifyParticipantStatusChanged(ref statusOptions, null, OnParticipantStatusChanged);
                _participantStatusHandle = new NotifyEventHandle(statusHandle, h => RTC?.RemoveNotifyParticipantStatusChanged(h));
            }

            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", "Registered for audio notifications");
        }

        private void OnAudioBeforeRender(ref AudioBeforeRenderCallbackInfo data)
        {
            // CRITICAL: Called from AUDIO THREAD - only buffer, don't process or call Unity APIs!
            if (!data.Buffer.HasValue || data.ParticipantId == null || !data.ParticipantId.IsValid())
            {
                return;
            }

            string puid = data.ParticipantId.ToString();

            // Get or create buffer for this participant
            var buffer = _audioBuffers.GetOrAdd(puid, _ => new ConcurrentQueue<short[]>());

            // Limit buffer size to prevent memory growth (~2 seconds at 48kHz)
            while (buffer.Count > 100)
            {
                buffer.TryDequeue(out _);
            }

            // Copy frames (they're only valid during callback)
            var audioBuffer = data.Buffer.Value;
            if (audioBuffer.Frames != null && audioBuffer.Frames.Length > 0)
            {
                var frames = new short[audioBuffer.Frames.Length];
                Array.Copy(audioBuffer.Frames, frames, frames.Length);
                buffer.Enqueue(frames);

                // Fire event for custom handling (WARNING: on audio thread!)
                OnAudioFrameReceived?.Invoke(puid, frames);
            }
        }

        private void OnParticipantUpdated(ref ParticipantUpdatedCallbackInfo data)
        {
            if (data.ParticipantId == null || !data.ParticipantId.IsValid())
            {
                return;
            }

            string puid = data.ParticipantId.ToString();
            bool wasSpeaking = _speakingState.TryGetValue(puid, out var prev) && prev;
            bool isSpeaking = data.Speaking;

            // Update speaking state
            _speakingState[puid] = isSpeaking;

            // Update audio status
            var prevStatus = _audioStatus.TryGetValue(puid, out var ps) ? ps : RTCAudioStatus.Disabled;
            _audioStatus[puid] = data.AudioStatus;

            // Fire speaking event if changed
            if (wasSpeaking != isSpeaking)
            {
                OnParticipantSpeaking?.Invoke(puid, isSpeaking);
            }

            // Fire audio status event if changed
            if (prevStatus != data.AudioStatus)
            {
                OnParticipantAudioStatusChanged?.Invoke(puid, data.AudioStatus);
            }
        }

        private void OnParticipantStatusChanged(ref ParticipantStatusChangedCallbackInfo data)
        {
            if (data.ParticipantId == null || !data.ParticipantId.IsValid())
            {
                return;
            }

            string puid = data.ParticipantId.ToString();

            switch (data.ParticipantStatus)
            {
                case RTCParticipantStatus.Joined:
                    EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Participant joined voice: {puid}");
                    // Initialize their state so they show up in participant list
                    if (!_audioStatus.ContainsKey(puid))
                    {
                        _audioStatus[puid] = RTCAudioStatus.Enabled;
                        _speakingState[puid] = false;
                    }
                    break;

                case RTCParticipantStatus.Left:
                    EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", $" Participant left voice: {puid}");
                    // Clear their state
                    _speakingState.Remove(puid);
                    _audioStatus.Remove(puid);
                    if (_audioBuffers.TryRemove(puid, out var queue))
                    {
                        while (queue.TryDequeue(out _)) { }
                    }
                    break;
            }
        }

        #endregion

        #region Cleanup

        private void CleanupAudioNotifications()
        {
            _audioBeforeRenderHandle?.Dispose();
            _participantUpdatedHandle?.Dispose();
            _participantStatusHandle?.Dispose();

            _audioBeforeRenderHandle = null;
            _participantUpdatedHandle = null;
            _participantStatusHandle = null;
        }

        private void Cleanup()
        {
            // Dispose all notification handles
            _rtcConnectionHandle?.Dispose();
            _rtcConnectionHandle = null;

            CleanupAudioNotifications();

            // Clear all state
            _audioBuffers.Clear();
            _speakingState.Clear();
            _audioStatus.Clear();

            IsConnected = false;
            IsMuted = false;
            CurrentRoomName = null;
            _currentLobbyId = null;

            EOSDebugLogger.Log(DebugCategory.VoiceManager, "EOSVoiceManager", "Cleaned up");
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSVoiceManager))]
    public class EOSVoiceManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSVoiceManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.Toggle("Connected", manager.IsConnected);
                EditorGUILayout.Toggle("Muted", manager.IsMuted);
                EditorGUILayout.Toggle("Voice Enabled", manager.IsVoiceEnabled);
                EditorGUILayout.TextField("Room Name", manager.CurrentRoomName ?? "(none)");
            }

            if (Application.isPlaying && manager.IsConnected)
            {
                EditorGUILayout.Space(5);
                var speaking = manager.GetSpeakingParticipants();
                EditorGUILayout.LabelField($"Speaking: {speaking?.Count ?? 0} participants");

                if (speaking != null && speaking.Count > 0)
                {
                    EditorGUI.indentLevel++;
                    foreach (var puid in speaking)
                    {
                        string shortPuid = puid?.Length > 12 ? puid.Substring(0, 8) + "..." : puid;
                        EditorGUILayout.LabelField(shortPuid, EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(manager.IsMuted ? "Unmute" : "Mute"))
                {
                    manager.ToggleMute();
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Join a lobby with voice enabled to see status.", MessageType.Info);
            }

            if (Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
