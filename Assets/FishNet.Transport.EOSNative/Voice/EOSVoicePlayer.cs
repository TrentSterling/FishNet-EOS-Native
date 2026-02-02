using System.Collections.Concurrent;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Voice
{
    /// <summary>
    /// Plays voice audio for a specific participant.
    /// Add to a player prefab for 3D spatial audio, or to a UI element for non-spatial voice.
    ///
    /// For 3D spatial audio:
    /// 1. Set AudioSource.spatialBlend = 1.0
    /// 2. Attach this component to the player's GameObject
    /// 3. Set ParticipantPuid to the player's ProductUserId
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EOSVoicePlayer : MonoBehaviour
    {
        #region Serialized Fields

        [Tooltip("The ProductUserId (PUID) of the participant to play audio for. Set at runtime.")]
        [SerializeField]
        private string _participantPuid;

        [Tooltip("Sample rate of the EOS RTC audio (usually 48000).")]
        [SerializeField]
        private int _sampleRate = 48000;

        [Tooltip("Number of audio channels (1 = mono, which is what EOS provides).")]
        [SerializeField]
        private int _channels = 1;

        [Tooltip("Buffer size in samples for the streaming audio clip.")]
        [SerializeField]
        private int _bufferSize = 48000;  // 1 second buffer

        [Tooltip("Maximum queue size before dropping old frames (prevents memory buildup on lag).")]
        [SerializeField]
        private int _maxQueueSize = 100;

        [Tooltip("Queue size at which we start catching up (dropping frames).")]
        [SerializeField]
        private int _catchupThreshold = 50;

        [Tooltip("Queue size at which we stop catching up.")]
        [SerializeField]
        private int _catchupStopThreshold = 20;

        [Tooltip("Automatically play when audio frames arrive.")]
        [SerializeField]
        private bool _autoPlay = true;

        [Header("3D Audio Settings")]
        [Tooltip("0 = 2D (no spatialization), 1 = full 3D spatial audio.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _spatialBlend = 1f;

        [Tooltip("Doppler effect intensity. 0 = off, 1 = normal, higher = exaggerated.")]
        [Range(0f, 5f)]
        [SerializeField]
        private float _dopplerLevel = 1f;

        [Tooltip("Minimum distance before volume starts to attenuate.")]
        [SerializeField]
        private float _minDistance = 1f;

        [Tooltip("Maximum distance where sound is still audible.")]
        [SerializeField]
        private float _maxDistance = 50f;

        [Tooltip("How the volume attenuates over distance.")]
        [SerializeField]
        private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Voice Effects - Pitch")]
        [Tooltip("Enable STFT-based pitch shifting for voice effects.")]
        [SerializeField]
        private bool _enablePitchShift = false;

        [Tooltip("Pitch shift factor. 0.5 = one octave down, 1.0 = normal, 2.0 = one octave up.")]
        [Range(0.5f, 2f)]
        [SerializeField]
        private float _pitchShift = 1f;

        [Tooltip("FFT quality. Higher = better quality but more CPU. 4=fast, 10=balanced, 32=best.")]
        [Range(4, 32)]
        [SerializeField]
        private int _pitchShiftQuality = 10;

        [Header("Voice Effects - Reverb")]
        [Tooltip("Enable reverb effect on voice. Makes voices sound like they're in different environments.")]
        [SerializeField]
        private bool _enableReverb = true;

        [Tooltip("Reverb environment preset. Cave and Hallway are most dramatic.")]
        [SerializeField]
        private AudioReverbPreset _reverbPreset = AudioReverbPreset.Cave;

        [Tooltip("Dry mix level in mB (-10000 to 0). Higher = more original signal.")]
        [Range(-10000f, 0f)]
        [SerializeField]
        private float _reverbDryLevel = 0f;

        [Tooltip("Reverb mix level in mB (-10000 to 0). Higher = more reverb. 0 = full reverb.")]
        [Range(-10000f, 0f)]
        [SerializeField]
        private float _reverbLevel = 0f;

        #endregion

        #region Public Properties

        /// <summary>
        /// The ProductUserId (PUID) of the participant this player renders audio for.
        /// </summary>
        public string ParticipantPuid
        {
            get => _participantPuid;
            set => SetParticipant(value);
        }

        /// <summary>
        /// Whether this player is actively receiving audio frames.
        /// </summary>
        public bool IsReceivingAudio => _audioQueue.Count > 0;

        /// <summary>
        /// Current queue size (buffered frames waiting to play).
        /// </summary>
        public int QueuedFrames => _audioQueue.Count;

        /// <summary>
        /// Whether this participant is currently speaking (according to EOSVoiceManager).
        /// </summary>
        public bool IsSpeaking => EOSVoiceManager.Instance?.IsSpeaking(_participantPuid) ?? false;

        /// <summary>
        /// Spatial blend (0 = 2D, 1 = 3D). Changes are applied immediately.
        /// </summary>
        public float SpatialBlend
        {
            get => _spatialBlend;
            set { _spatialBlend = value; if (_audioSource != null) _audioSource.spatialBlend = value; }
        }

        /// <summary>
        /// Doppler effect level (0 = off, 1 = normal, higher = exaggerated).
        /// </summary>
        public float DopplerLevel
        {
            get => _dopplerLevel;
            set { _dopplerLevel = value; if (_audioSource != null) _audioSource.dopplerLevel = value; }
        }

        /// <summary>
        /// The AudioSource used for playback. Useful for advanced audio manipulation.
        /// </summary>
        public AudioSource AudioSource => _audioSource;

        /// <summary>
        /// Enable/disable pitch shifting effect.
        /// </summary>
        public bool EnablePitchShift
        {
            get => _enablePitchShift;
            set
            {
                _enablePitchShift = value;
                if (!value && _pitchShifter != null)
                {
                    _pitchShifter.Reset();
                }
            }
        }

        /// <summary>
        /// Pitch shift factor (0.5 = octave down, 1.0 = normal, 2.0 = octave up).
        /// </summary>
        public float PitchShift
        {
            get => _pitchShift;
            set => _pitchShift = Mathf.Clamp(value, 0.5f, 2f);
        }

        /// <summary>
        /// Enable/disable reverb effect.
        /// </summary>
        public bool EnableReverb
        {
            get => _enableReverb;
            set
            {
                _enableReverb = value;
                if (_reverbFilter != null)
                {
                    _reverbFilter.enabled = value;
                }
            }
        }

        /// <summary>
        /// Reverb environment preset.
        /// </summary>
        public AudioReverbPreset ReverbPreset
        {
            get => _reverbPreset;
            set
            {
                _reverbPreset = value;
                if (_reverbFilter != null)
                {
                    _reverbFilter.reverbPreset = value;
                }
            }
        }

        /// <summary>
        /// Reverb mix level in mB (-10000 to 0). Higher = more reverb.
        /// </summary>
        public float ReverbLevel
        {
            get => _reverbLevel;
            set
            {
                _reverbLevel = Mathf.Clamp(value, -10000f, 0f);
                if (_reverbFilter != null)
                {
                    _reverbFilter.reverbLevel = _reverbLevel;
                }
            }
        }

        /// <summary>
        /// The AudioReverbFilter used for reverb. Useful for advanced configuration.
        /// </summary>
        public AudioReverbFilter ReverbFilter => _reverbFilter;

        #endregion

        #region Private Fields

        private AudioSource _audioSource;
        private AudioReverbFilter _reverbFilter;
        private ConcurrentQueue<short[]> _audioQueue = new();
        private short[] _currentFrame;
        private int _frameIndex;
        private bool _catchingUp;
        private bool _hasStartedPlaying;
        private SMBPitchShifter _pitchShifter;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            Apply3DAudioSettings();
            SetupReverbFilter();
        }

        /// <summary>
        /// Set up or update the reverb filter component.
        /// </summary>
        private void SetupReverbFilter()
        {
            // Get or add the reverb filter
            _reverbFilter = GetComponent<AudioReverbFilter>();
            if (_reverbFilter == null)
            {
                _reverbFilter = gameObject.AddComponent<AudioReverbFilter>();
            }

            ApplyReverbSettings();
        }

        /// <summary>
        /// Apply reverb settings to the filter.
        /// Call this if you change settings at runtime.
        /// </summary>
        public void ApplyReverbSettings()
        {
            if (_reverbFilter == null) return;

            _reverbFilter.enabled = _enableReverb;
            _reverbFilter.reverbPreset = _reverbPreset;
            _reverbFilter.dryLevel = _reverbDryLevel;
            _reverbFilter.reverbLevel = _reverbLevel;
        }

        /// <summary>
        /// Apply 3D audio settings to the AudioSource.
        /// Call this if you change settings at runtime.
        /// </summary>
        public void Apply3DAudioSettings()
        {
            if (_audioSource == null) return;

            _audioSource.spatialBlend = _spatialBlend;
            _audioSource.dopplerLevel = _dopplerLevel;
            _audioSource.minDistance = _minDistance;
            _audioSource.maxDistance = _maxDistance;
            _audioSource.rolloffMode = _rolloffMode;
        }

        private void Start()
        {
            // Create streaming audio clip
            _audioSource.clip = AudioClip.Create(
                "voice_" + (_participantPuid ?? "unknown"),
                _bufferSize,
                _channels,
                _sampleRate,
                true,  // streaming
                OnAudioRead
            );
            _audioSource.loop = true;
        }

        private void OnEnable()
        {
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnAudioFrameReceived += OnAudioFrameReceived;
            }
        }

        private void OnDisable()
        {
            if (EOSVoiceManager.Instance != null)
            {
                EOSVoiceManager.Instance.OnAudioFrameReceived -= OnAudioFrameReceived;
            }

            // Stop playback
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
            _hasStartedPlaying = false;
        }

        private void Update()
        {
            // Auto-play when frames arrive
            if (_autoPlay && _audioQueue.Count > 0 && !_audioSource.isPlaying && !string.IsNullOrEmpty(_participantPuid))
            {
                _audioSource.Play();
                _hasStartedPlaying = true;
            }

            // Stop if no more audio and we've played through
            if (_hasStartedPlaying && _audioQueue.Count == 0 && _currentFrame == null && _audioSource.isPlaying)
            {
                // Let it play out the buffer, don't stop abruptly
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set the participant this player should render audio for.
        /// </summary>
        public void SetParticipant(string puid)
        {
            if (_participantPuid == puid)
            {
                return;
            }

            _participantPuid = puid;

            // Clear existing audio state
            _audioQueue = new ConcurrentQueue<short[]>();
            _currentFrame = null;
            _frameIndex = 0;
            _catchingUp = false;
            _hasStartedPlaying = false;
            _pitchShifter?.Reset();

            // Stop playback if currently playing
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            // Rename audio clip for debugging
            if (_audioSource != null && _audioSource.clip != null)
            {
                // Can't rename existing clip, but this is just for debugging anyway
            }

            EOSDebugLogger.Log(DebugCategory.VoicePlayer, "EOSVoicePlayer", $" Set participant: {puid}");
        }

        /// <summary>
        /// Clear all buffered audio.
        /// </summary>
        public void ClearBuffer()
        {
            _audioQueue = new ConcurrentQueue<short[]>();
            _currentFrame = null;
            _frameIndex = 0;
            _pitchShifter?.Reset();
        }

        /// <summary>
        /// Manually start playback.
        /// </summary>
        public void Play()
        {
            if (_audioSource != null && !_audioSource.isPlaying)
            {
                _audioSource.Play();
                _hasStartedPlaying = true;
            }
        }

        /// <summary>
        /// Manually stop playback.
        /// </summary>
        public void Stop()
        {
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
            _hasStartedPlaying = false;
        }

        #endregion

        #region Audio Callbacks

        private void OnAudioFrameReceived(string puid, short[] frames)
        {
            // Only buffer frames for our participant
            if (puid != _participantPuid)
            {
                return;
            }

            // Limit queue size to prevent memory growth
            while (_audioQueue.Count >= _maxQueueSize)
            {
                _audioQueue.TryDequeue(out _);
            }

            // Copy frames (callback might reuse buffer)
            var copy = new short[frames.Length];
            System.Array.Copy(frames, copy, frames.Length);
            _audioQueue.Enqueue(copy);
        }

        private void OnAudioRead(float[] data)
        {
            // Catchup logic to prevent lag buildup
            if (_audioQueue.Count > _catchupThreshold || _catchingUp)
            {
                _catchingUp = true;
                // Drop a frame to catch up
                _audioQueue.TryDequeue(out _);
                _catchingUp = _audioQueue.Count > _catchupStopThreshold;
            }

            bool hasAudio = false;
            for (int i = 0; i < data.Length; i++)
            {
                // Default to silence
                data[i] = 0f;

                // Get next frame if needed
                if (_currentFrame == null || _frameIndex >= _currentFrame.Length)
                {
                    if (!_audioQueue.TryDequeue(out _currentFrame))
                    {
                        // No more frames, output silence
                        continue;
                    }
                    _frameIndex = 0;
                }

                // Convert int16 to float32 (normalized to -1.0 to 1.0)
                data[i] = _currentFrame[_frameIndex++] / (float)short.MaxValue;
                hasAudio = true;
            }

            // Apply pitch shifting if enabled and we have audio
            if (_enablePitchShift && hasAudio && System.Math.Abs(_pitchShift - 1f) > 0.01f)
            {
                // Create pitch shifter on demand
                _pitchShifter ??= new SMBPitchShifter();
                _pitchShifter.Process(_pitchShift, data.Length, SMBPitchShifter.DEFAULT_FFT_FRAME_SIZE,
                    _pitchShiftQuality, _sampleRate, data);
            }
        }

        #endregion
    }
}
