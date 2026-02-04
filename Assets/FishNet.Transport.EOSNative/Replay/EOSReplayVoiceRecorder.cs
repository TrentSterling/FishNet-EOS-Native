using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Voice;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Voice segment for replay storage.
    /// </summary>
    [Serializable]
    public struct ReplayVoiceSegment
    {
        /// <summary>Start timestamp in seconds from recording start.</summary>
        public float Timestamp;

        /// <summary>Duration of this segment in seconds.</summary>
        public float Duration;

        /// <summary>PUID of the speaker.</summary>
        public string SpeakerPuid;

        /// <summary>Compressed audio data (Opus or raw PCM compressed with GZip).</summary>
        public byte[] CompressedAudio;

        /// <summary>Sample rate of the audio.</summary>
        public int SampleRate;

        /// <summary>Number of channels (1 = mono, 2 = stereo).</summary>
        public int Channels;
    }

    /// <summary>
    /// Voice track containing all segments for a replay.
    /// </summary>
    [Serializable]
    public class ReplayVoiceTrack
    {
        /// <summary>Version for format compatibility.</summary>
        public int Version = 1;

        /// <summary>Total duration covered by voice segments.</summary>
        public float Duration;

        /// <summary>All voice segments in chronological order.</summary>
        public List<ReplayVoiceSegment> Segments = new();

        /// <summary>Map of PUID to display name for speakers.</summary>
        public Dictionary<string, string> Speakers = new();
    }

    /// <summary>
    /// Records voice chat during gameplay for replay playback.
    /// Integrates with EOSReplayRecorder automatically.
    /// </summary>
    public class EOSReplayVoiceRecorder : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayVoiceRecorder _instance;
        public static EOSReplayVoiceRecorder Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSReplayVoiceRecorder>();
#else
                    _instance = FindObjectOfType<EOSReplayVoiceRecorder>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when voice recording starts.</summary>
        public event Action OnVoiceRecordingStarted;

        /// <summary>Fired when voice recording stops.</summary>
        public event Action<ReplayVoiceTrack> OnVoiceRecordingStopped;

        /// <summary>Fired when a voice segment is captured.</summary>
        public event Action<string, float> OnSegmentCaptured; // puid, duration

        #endregion

        #region Inspector Settings

        [Header("Recording Settings")]
        [Tooltip("Enable voice recording during replays")]
        [SerializeField] private bool _enableVoiceRecording = true;

        [Tooltip("Minimum segment duration to record (seconds)")]
        [SerializeField] private float _minSegmentDuration = 0.1f;

        [Tooltip("Maximum segment duration before splitting (seconds)")]
        [SerializeField] private float _maxSegmentDuration = 5f;

        [Tooltip("Sample rate for recorded audio")]
        [SerializeField] private int _sampleRate = 48000;

        [Tooltip("Buffer size before flushing to segment")]
        [SerializeField] private int _bufferSizeFrames = 100;

        [Header("Compression")]
        [Tooltip("Compress voice data (reduces size ~60%)")]
        [SerializeField] private bool _compressAudio = true;

        [Header("Limits")]
        [Tooltip("Maximum voice data size in MB (0 = unlimited)")]
        [SerializeField] private float _maxVoiceDataMB = 50f;

        #endregion

        #region Public Properties

        /// <summary>Whether voice recording is enabled.</summary>
        public bool Enabled
        {
            get => _enableVoiceRecording;
            set => _enableVoiceRecording = value;
        }

        /// <summary>Whether currently recording voice.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>Current voice track being recorded.</summary>
        public ReplayVoiceTrack CurrentTrack { get; private set; }

        /// <summary>Estimated current voice data size in MB.</summary>
        public float EstimatedSizeMB
        {
            get
            {
                if (CurrentTrack == null) return 0f;
                long totalBytes = 0;
                foreach (var segment in CurrentTrack.Segments)
                {
                    totalBytes += segment.CompressedAudio?.Length ?? 0;
                }
                return totalBytes / (1024f * 1024f);
            }
        }

        /// <summary>Number of segments recorded.</summary>
        public int SegmentCount => CurrentTrack?.Segments.Count ?? 0;

        #endregion

        #region Private Fields

        private float _recordingStartTime;
        private readonly Dictionary<string, VoiceBuffer> _voiceBuffers = new();
        private readonly object _bufferLock = new object();
        private bool _sizeWarningFired;

        private class VoiceBuffer
        {
            public List<short> Samples = new();
            public float StartTime;
            public float LastSampleTime;
            public bool IsSpeaking;
        }

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
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            // Subscribe to replay recorder events
            if (EOSReplayRecorder.Instance != null)
            {
                EOSReplayRecorder.Instance.OnRecordingStarted += OnReplayRecordingStarted;
                EOSReplayRecorder.Instance.OnRecordingStopped += OnReplayRecordingStopped;
            }

            // Subscribe to voice events
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnAudioFrameReceived += OnAudioFrameReceived;
                EOSVoiceManager.Instance.OnParticipantSpeaking += OnParticipantSpeaking;
            }
        }

        private void OnDisable()
        {
            if (EOSReplayRecorder.Instance != null)
            {
                EOSReplayRecorder.Instance.OnRecordingStarted -= OnReplayRecordingStarted;
                EOSReplayRecorder.Instance.OnRecordingStopped -= OnReplayRecordingStopped;
            }

            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnAudioFrameReceived -= OnAudioFrameReceived;
                EOSVoiceManager.Instance.OnParticipantSpeaking -= OnParticipantSpeaking;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!IsRecording) return;

            // Flush buffers that have been silent for a while
            FlushSilentBuffers();

            // Check size limit
            if (_maxVoiceDataMB > 0 && EstimatedSizeMB >= _maxVoiceDataMB && !_sizeWarningFired)
            {
                _sizeWarningFired = true;
                EOSDebugLogger.LogWarning(DebugCategory.Replay, "EOSReplayVoiceRecorder",
                    $"Voice data approaching limit: {EstimatedSizeMB:F1}MB / {_maxVoiceDataMB}MB");
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start recording voice (called automatically by EOSReplayRecorder).
        /// </summary>
        public void StartRecording()
        {
            if (!_enableVoiceRecording) return;
            if (IsRecording) return;

            IsRecording = true;
            _recordingStartTime = Time.time;
            _sizeWarningFired = false;

            CurrentTrack = new ReplayVoiceTrack();

            lock (_bufferLock)
            {
                _voiceBuffers.Clear();
            }

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayVoiceRecorder", "Started voice recording");
            OnVoiceRecordingStarted?.Invoke();
        }

        /// <summary>
        /// Stop recording voice and return the track.
        /// </summary>
        public ReplayVoiceTrack StopRecording()
        {
            if (!IsRecording) return null;

            // Flush all remaining buffers
            FlushAllBuffers();

            IsRecording = false;
            CurrentTrack.Duration = Time.time - _recordingStartTime;

            var track = CurrentTrack;
            CurrentTrack = null;

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayVoiceRecorder",
                $"Stopped voice recording: {track.Segments.Count} segments, {EstimatedSizeMB:F1}MB");

            OnVoiceRecordingStopped?.Invoke(track);
            return track;
        }

        /// <summary>
        /// Get the voice track for saving with replay.
        /// </summary>
        public byte[] SerializeTrack(ReplayVoiceTrack track)
        {
            if (track == null) return null;

            try
            {
                string json = JsonUtility.ToJson(new SerializableVoiceTrack(track));
                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

                if (_compressAudio)
                {
                    using var output = new MemoryStream();
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    {
                        gzip.Write(jsonBytes, 0, jsonBytes.Length);
                    }
                    return output.ToArray();
                }

                return jsonBytes;
            }
            catch (Exception e)
            {
                EOSDebugLogger.LogError("EOSReplayVoiceRecorder", $"Failed to serialize voice track: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load a voice track from serialized data.
        /// </summary>
        public ReplayVoiceTrack DeserializeTrack(byte[] data, bool isCompressed = true)
        {
            if (data == null || data.Length == 0) return null;

            try
            {
                byte[] jsonBytes;

                if (isCompressed)
                {
                    using var input = new MemoryStream(data);
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    gzip.CopyTo(output);
                    jsonBytes = output.ToArray();
                }
                else
                {
                    jsonBytes = data;
                }

                string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                var serializable = JsonUtility.FromJson<SerializableVoiceTrack>(json);
                return serializable?.ToVoiceTrack();
            }
            catch (Exception e)
            {
                EOSDebugLogger.LogError("EOSReplayVoiceRecorder", $"Failed to deserialize voice track: {e.Message}");
                return null;
            }
        }

        #endregion

        #region Event Handlers

        private void OnReplayRecordingStarted(string matchId)
        {
            StartRecording();
        }

        private void OnReplayRecordingStopped(ReplayFile replay)
        {
            StopRecording();
        }

        private void OnAudioFrameReceived(string puid, short[] frames)
        {
            if (!IsRecording || !_enableVoiceRecording) return;
            if (string.IsNullOrEmpty(puid) || frames == null || frames.Length == 0) return;

            // Check size limit
            if (_maxVoiceDataMB > 0 && EstimatedSizeMB >= _maxVoiceDataMB) return;

            lock (_bufferLock)
            {
                if (!_voiceBuffers.TryGetValue(puid, out var buffer))
                {
                    buffer = new VoiceBuffer
                    {
                        StartTime = Time.time - _recordingStartTime,
                        LastSampleTime = Time.time
                    };
                    _voiceBuffers[puid] = buffer;

                    // Record speaker info
                    if (!CurrentTrack.Speakers.ContainsKey(puid))
                    {
                        var registry = EOSPlayerRegistry.Instance;
                        string name = registry?.GetDisplayName(puid) ?? puid.Substring(0, 8);
                        CurrentTrack.Speakers[puid] = name;
                    }
                }

                // Add samples to buffer
                buffer.Samples.AddRange(frames);
                buffer.LastSampleTime = Time.time;
                buffer.IsSpeaking = true;

                // Flush if buffer is too large or segment too long
                float segmentDuration = (buffer.Samples.Count / (float)_sampleRate);
                if (buffer.Samples.Count >= _bufferSizeFrames * 960 || segmentDuration >= _maxSegmentDuration)
                {
                    FlushBuffer(puid, buffer);
                }
            }
        }

        private void OnParticipantSpeaking(string puid, bool isSpeaking)
        {
            if (!IsRecording) return;

            lock (_bufferLock)
            {
                if (_voiceBuffers.TryGetValue(puid, out var buffer))
                {
                    buffer.IsSpeaking = isSpeaking;

                    // Flush when they stop speaking
                    if (!isSpeaking && buffer.Samples.Count > 0)
                    {
                        FlushBuffer(puid, buffer);
                    }
                }
            }
        }

        #endregion

        #region Buffer Management

        private void FlushSilentBuffers()
        {
            float silenceThreshold = 0.5f; // Flush after 0.5s of silence

            lock (_bufferLock)
            {
                var toFlush = new List<string>();

                foreach (var kvp in _voiceBuffers)
                {
                    if (!kvp.Value.IsSpeaking && kvp.Value.Samples.Count > 0)
                    {
                        if (Time.time - kvp.Value.LastSampleTime > silenceThreshold)
                        {
                            toFlush.Add(kvp.Key);
                        }
                    }
                }

                foreach (var puid in toFlush)
                {
                    FlushBuffer(puid, _voiceBuffers[puid]);
                }
            }
        }

        private void FlushAllBuffers()
        {
            lock (_bufferLock)
            {
                foreach (var kvp in _voiceBuffers)
                {
                    if (kvp.Value.Samples.Count > 0)
                    {
                        FlushBuffer(kvp.Key, kvp.Value);
                    }
                }
                _voiceBuffers.Clear();
            }
        }

        private void FlushBuffer(string puid, VoiceBuffer buffer)
        {
            if (buffer.Samples.Count == 0) return;

            float duration = buffer.Samples.Count / (float)_sampleRate;
            if (duration < _minSegmentDuration) return;

            // Convert to bytes
            byte[] audioBytes = SamplesToBytes(buffer.Samples.ToArray());

            // Compress if enabled
            byte[] compressedAudio;
            if (_compressAudio)
            {
                compressedAudio = CompressAudio(audioBytes);
            }
            else
            {
                compressedAudio = audioBytes;
            }

            var segment = new ReplayVoiceSegment
            {
                Timestamp = buffer.StartTime,
                Duration = duration,
                SpeakerPuid = puid,
                CompressedAudio = compressedAudio,
                SampleRate = _sampleRate,
                Channels = 1 // EOS provides mono audio
            };

            CurrentTrack.Segments.Add(segment);

            // Reset buffer
            buffer.Samples.Clear();
            buffer.StartTime = Time.time - _recordingStartTime;

            OnSegmentCaptured?.Invoke(puid, duration);

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayVoiceRecorder",
                $"Captured voice segment: {puid.Substring(0, 8)}... {duration:F1}s");
        }

        private byte[] SamplesToBytes(short[] samples)
        {
            byte[] bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private byte[] CompressAudio(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            {
                gzip.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        #endregion

        #region Serialization Helper

        [Serializable]
        private class SerializableVoiceTrack
        {
            public int Version;
            public float Duration;
            public List<SerializableVoiceSegment> Segments = new();
            public List<SpeakerEntry> Speakers = new();

            [Serializable]
            public class SerializableVoiceSegment
            {
                public float Timestamp;
                public float Duration;
                public string SpeakerPuid;
                public string CompressedAudioBase64;
                public int SampleRate;
                public int Channels;
            }

            [Serializable]
            public class SpeakerEntry
            {
                public string Puid;
                public string Name;
            }

            public SerializableVoiceTrack() { }

            public SerializableVoiceTrack(ReplayVoiceTrack track)
            {
                Version = track.Version;
                Duration = track.Duration;

                foreach (var segment in track.Segments)
                {
                    Segments.Add(new SerializableVoiceSegment
                    {
                        Timestamp = segment.Timestamp,
                        Duration = segment.Duration,
                        SpeakerPuid = segment.SpeakerPuid,
                        CompressedAudioBase64 = Convert.ToBase64String(segment.CompressedAudio ?? Array.Empty<byte>()),
                        SampleRate = segment.SampleRate,
                        Channels = segment.Channels
                    });
                }

                foreach (var kvp in track.Speakers)
                {
                    Speakers.Add(new SpeakerEntry { Puid = kvp.Key, Name = kvp.Value });
                }
            }

            public ReplayVoiceTrack ToVoiceTrack()
            {
                var track = new ReplayVoiceTrack
                {
                    Version = Version,
                    Duration = Duration
                };

                foreach (var seg in Segments)
                {
                    track.Segments.Add(new ReplayVoiceSegment
                    {
                        Timestamp = seg.Timestamp,
                        Duration = seg.Duration,
                        SpeakerPuid = seg.SpeakerPuid,
                        CompressedAudio = Convert.FromBase64String(seg.CompressedAudioBase64 ?? ""),
                        SampleRate = seg.SampleRate,
                        Channels = seg.Channels
                    });
                }

                foreach (var speaker in Speakers)
                {
                    track.Speakers[speaker.Puid] = speaker.Name;
                }

                return track;
            }
        }

        #endregion
    }
}
