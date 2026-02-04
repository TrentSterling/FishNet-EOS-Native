using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Plays back recorded voice during replay viewing.
    /// </summary>
    public class EOSReplayVoicePlayer : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayVoicePlayer _instance;
        public static EOSReplayVoicePlayer Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<EOSReplayVoicePlayer>();
#else
                    _instance = FindObjectOfType<EOSReplayVoicePlayer>();
#endif
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when voice playback starts.</summary>
        public event Action OnPlaybackStarted;

        /// <summary>Fired when voice playback stops.</summary>
        public event Action OnPlaybackStopped;

        /// <summary>Fired when a speaker starts talking.</summary>
        public event Action<string, string> OnSpeakerStarted; // puid, name

        /// <summary>Fired when a speaker stops talking.</summary>
        public event Action<string> OnSpeakerStopped; // puid

        #endregion

        #region Inspector Settings

        [Header("Playback Settings")]
        [Tooltip("Master volume for voice playback (0-1)")]
        [SerializeField, Range(0f, 1f)] private float _masterVolume = 1f;

        [Tooltip("Enable voice playback during replays")]
        [SerializeField] private bool _enablePlayback = true;

        [Header("Per-Speaker Settings")]
        [Tooltip("Default volume for speakers")]
        [SerializeField, Range(0f, 1f)] private float _defaultSpeakerVolume = 1f;

        #endregion

        #region Public Properties

        /// <summary>Whether voice playback is enabled.</summary>
        public bool Enabled
        {
            get => _enablePlayback;
            set => _enablePlayback = value;
        }

        /// <summary>Master volume (0-1).</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Clamp01(value);
        }

        /// <summary>Whether currently playing back voice.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Current voice track being played.</summary>
        public ReplayVoiceTrack CurrentTrack { get; private set; }

        /// <summary>Current playback time in seconds.</summary>
        public float CurrentTime { get; private set; }

        /// <summary>Currently playing speakers.</summary>
        public IReadOnlyList<string> CurrentSpeakers => _currentSpeakers;

        #endregion

        #region Private Fields

        private readonly List<string> _currentSpeakers = new();
        private readonly Dictionary<string, float> _speakerVolumes = new();
        private readonly Dictionary<string, bool> _speakerMuted = new();
        private readonly Dictionary<string, AudioSource> _audioSources = new();
        private int _currentSegmentIndex;
        private bool _isPaused;
        private float _playbackSpeed = 1f;

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
            // Subscribe to replay player events
            if (EOSReplayPlayer.Instance != null)
            {
                EOSReplayPlayer.Instance.OnTimeChanged += OnReplayTimeChanged;
                EOSReplayPlayer.Instance.OnStateChanged += OnReplayStateChanged;
            }
        }

        private void OnDisable()
        {
            if (EOSReplayPlayer.Instance != null)
            {
                EOSReplayPlayer.Instance.OnTimeChanged -= OnReplayTimeChanged;
                EOSReplayPlayer.Instance.OnStateChanged -= OnReplayStateChanged;
            }
        }

        private void OnDestroy()
        {
            StopPlayback();
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (!IsPlaying || _isPaused || CurrentTrack == null) return;

            // Check for segments that should start/stop
            UpdateSegmentPlayback();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Load a voice track for playback.
        /// </summary>
        public void LoadTrack(ReplayVoiceTrack track)
        {
            StopPlayback();
            CurrentTrack = track;
            _currentSegmentIndex = 0;

            if (track != null)
            {
                EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayVoicePlayer",
                    $"Loaded voice track: {track.Segments.Count} segments, {track.Duration:F1}s");
            }
        }

        /// <summary>
        /// Load a voice track from serialized data.
        /// </summary>
        public void LoadTrack(byte[] data, bool isCompressed = true)
        {
            var recorder = EOSReplayVoiceRecorder.Instance;
            if (recorder != null)
            {
                var track = recorder.DeserializeTrack(data, isCompressed);
                LoadTrack(track);
            }
        }

        /// <summary>
        /// Start voice playback.
        /// </summary>
        public void StartPlayback()
        {
            if (!_enablePlayback || CurrentTrack == null) return;
            if (IsPlaying) return;

            IsPlaying = true;
            _isPaused = false;
            CurrentTime = 0f;
            _currentSegmentIndex = 0;

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayVoicePlayer", "Started voice playback");
            OnPlaybackStarted?.Invoke();
        }

        /// <summary>
        /// Stop voice playback.
        /// </summary>
        public void StopPlayback()
        {
            if (!IsPlaying) return;

            IsPlaying = false;
            _isPaused = false;

            // Stop all audio sources
            foreach (var source in _audioSources.Values)
            {
                if (source != null)
                {
                    source.Stop();
                    Destroy(source.gameObject);
                }
            }
            _audioSources.Clear();
            _currentSpeakers.Clear();

            EOSDebugLogger.Log(DebugCategory.Replay, "EOSReplayVoicePlayer", "Stopped voice playback");
            OnPlaybackStopped?.Invoke();
        }

        /// <summary>
        /// Pause voice playback.
        /// </summary>
        public void Pause()
        {
            if (!IsPlaying) return;
            _isPaused = true;

            foreach (var source in _audioSources.Values)
            {
                if (source != null && source.isPlaying)
                    source.Pause();
            }
        }

        /// <summary>
        /// Resume voice playback.
        /// </summary>
        public void Resume()
        {
            if (!IsPlaying) return;
            _isPaused = false;

            foreach (var source in _audioSources.Values)
            {
                if (source != null)
                    source.UnPause();
            }
        }

        /// <summary>
        /// Seek to a specific time.
        /// </summary>
        public void Seek(float time)
        {
            if (CurrentTrack == null) return;

            CurrentTime = Mathf.Clamp(time, 0f, CurrentTrack.Duration);

            // Stop all current playback
            foreach (var source in _audioSources.Values)
            {
                if (source != null)
                    source.Stop();
            }
            _currentSpeakers.Clear();

            // Find the segment index for this time
            _currentSegmentIndex = 0;
            for (int i = 0; i < CurrentTrack.Segments.Count; i++)
            {
                if (CurrentTrack.Segments[i].Timestamp <= CurrentTime)
                    _currentSegmentIndex = i;
                else
                    break;
            }
        }

        /// <summary>
        /// Set playback speed.
        /// </summary>
        public void SetSpeed(float speed)
        {
            _playbackSpeed = Mathf.Clamp(speed, 0.25f, 4f);

            foreach (var source in _audioSources.Values)
            {
                if (source != null)
                    source.pitch = _playbackSpeed;
            }
        }

        /// <summary>
        /// Set volume for a specific speaker.
        /// </summary>
        public void SetSpeakerVolume(string puid, float volume)
        {
            _speakerVolumes[puid] = Mathf.Clamp01(volume);

            if (_audioSources.TryGetValue(puid, out var source) && source != null)
            {
                source.volume = GetEffectiveVolume(puid);
            }
        }

        /// <summary>
        /// Mute/unmute a specific speaker.
        /// </summary>
        public void SetSpeakerMuted(string puid, bool muted)
        {
            _speakerMuted[puid] = muted;

            if (_audioSources.TryGetValue(puid, out var source) && source != null)
            {
                source.mute = muted;
            }
        }

        /// <summary>
        /// Get speaker name from PUID.
        /// </summary>
        public string GetSpeakerName(string puid)
        {
            if (CurrentTrack != null && CurrentTrack.Speakers.TryGetValue(puid, out string name))
                return name;
            return puid?.Substring(0, 8) ?? "Unknown";
        }

        #endregion

        #region Event Handlers

        private void OnReplayTimeChanged(float time)
        {
            if (!IsPlaying) return;
            CurrentTime = time;
        }

        private void OnReplayStateChanged(ReplayState state)
        {
            switch (state)
            {
                case ReplayState.Playing:
                    if (!IsPlaying && CurrentTrack != null)
                        StartPlayback();
                    else
                        Resume();
                    break;
                case ReplayState.Paused:
                    Pause();
                    break;
                case ReplayState.Stopped:
                    StopPlayback();
                    break;
            }
        }

        #endregion

        #region Playback Logic

        private void UpdateSegmentPlayback()
        {
            if (CurrentTrack == null || CurrentTrack.Segments.Count == 0) return;

            // Check segments that should start
            while (_currentSegmentIndex < CurrentTrack.Segments.Count)
            {
                var segment = CurrentTrack.Segments[_currentSegmentIndex];

                if (segment.Timestamp <= CurrentTime && segment.Timestamp + segment.Duration > CurrentTime)
                {
                    // This segment should be playing
                    PlaySegment(segment);
                    _currentSegmentIndex++;
                }
                else if (segment.Timestamp > CurrentTime)
                {
                    // Haven't reached this segment yet
                    break;
                }
                else
                {
                    // Segment has passed
                    _currentSegmentIndex++;
                }
            }

            // Check which speakers should stop
            var finishedSpeakers = new List<string>();
            foreach (var puid in _currentSpeakers)
            {
                if (_audioSources.TryGetValue(puid, out var source))
                {
                    if (source == null || !source.isPlaying)
                    {
                        finishedSpeakers.Add(puid);
                    }
                }
            }

            foreach (var puid in finishedSpeakers)
            {
                _currentSpeakers.Remove(puid);
                OnSpeakerStopped?.Invoke(puid);
            }
        }

        private void PlaySegment(ReplayVoiceSegment segment)
        {
            if (string.IsNullOrEmpty(segment.SpeakerPuid)) return;
            if (segment.CompressedAudio == null || segment.CompressedAudio.Length == 0) return;

            try
            {
                // Decompress audio
                byte[] audioBytes = DecompressAudio(segment.CompressedAudio);
                if (audioBytes == null) return;

                // Convert to samples
                short[] samples = BytesToSamples(audioBytes);
                if (samples == null || samples.Length == 0) return;

                // Create AudioClip
                float[] floatSamples = SamplesToFloat(samples);
                var clip = AudioClip.Create(
                    $"Voice_{segment.SpeakerPuid}_{segment.Timestamp:F1}",
                    floatSamples.Length,
                    segment.Channels,
                    segment.SampleRate,
                    false
                );
                clip.SetData(floatSamples, 0);

                // Get or create AudioSource
                var source = GetOrCreateAudioSource(segment.SpeakerPuid);
                source.clip = clip;
                source.volume = GetEffectiveVolume(segment.SpeakerPuid);
                source.pitch = _playbackSpeed;
                source.mute = _speakerMuted.TryGetValue(segment.SpeakerPuid, out bool muted) && muted;

                // Calculate offset if we're joining mid-segment
                float offset = CurrentTime - segment.Timestamp;
                if (offset > 0 && offset < segment.Duration)
                {
                    source.time = offset;
                }

                source.Play();

                if (!_currentSpeakers.Contains(segment.SpeakerPuid))
                {
                    _currentSpeakers.Add(segment.SpeakerPuid);
                    string name = GetSpeakerName(segment.SpeakerPuid);
                    OnSpeakerStarted?.Invoke(segment.SpeakerPuid, name);
                }
            }
            catch (Exception e)
            {
                EOSDebugLogger.LogError("EOSReplayVoicePlayer", $"Failed to play segment: {e.Message}");
            }
        }

        private AudioSource GetOrCreateAudioSource(string puid)
        {
            if (_audioSources.TryGetValue(puid, out var existing) && existing != null)
                return existing;

            var go = new GameObject($"VoiceSource_{puid.Substring(0, 8)}");
            go.transform.SetParent(transform);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // 2D audio for replay
            _audioSources[puid] = source;
            return source;
        }

        private float GetEffectiveVolume(string puid)
        {
            float speakerVol = _speakerVolumes.TryGetValue(puid, out float v) ? v : _defaultSpeakerVolume;
            return speakerVol * _masterVolume;
        }

        private byte[] DecompressAudio(byte[] compressed)
        {
            try
            {
                using var input = new MemoryStream(compressed);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                // May not be compressed
                return compressed;
            }
        }

        private short[] BytesToSamples(byte[] bytes)
        {
            short[] samples = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            return samples;
        }

        private float[] SamplesToFloat(short[] samples)
        {
            float[] floats = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                floats[i] = samples[i] / 32768f;
            }
            return floats;
        }

        #endregion
    }

    /// <summary>
    /// Replay playback states.
    /// </summary>
    public enum ReplayState
    {
        Stopped,
        Playing,
        Paused
    }
}
