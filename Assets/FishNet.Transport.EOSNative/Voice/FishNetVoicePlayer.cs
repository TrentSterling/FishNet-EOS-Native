using FishNet.Object;
using FishNet.Connection;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Voice
{
    /// <summary>
    /// FishNet-aware voice player that automatically wires up the participant PUID
    /// based on the NetworkObject's owner connection.
    ///
    /// Add this to your player prefab alongside an AudioSource.
    /// The EOSVoicePlayer will be added automatically if not present.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FishNetVoicePlayer : NetworkBehaviour
    {
        [Header("3D Audio Settings")]
        [Tooltip("Set to 1.0 for full 3D spatial audio, 0.0 for 2D.")]
        [Range(0f, 1f)]
        [SerializeField]
        private float _spatialBlend = 1f;

        [Tooltip("Doppler effect intensity. 0 = off, 1 = normal, higher = exaggerated.")]
        [Range(0f, 5f)]
        [SerializeField]
        private float _dopplerLevel = 1f;

        [Tooltip("Minimum distance for 3D audio rolloff.")]
        [SerializeField]
        private float _minDistance = 1f;

        [Tooltip("Maximum distance for 3D audio rolloff.")]
        [SerializeField]
        private float _maxDistance = 50f;

        [Tooltip("How the volume attenuates over distance.")]
        [SerializeField]
        private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Voice Effects")]
        [Tooltip("Enable reverb effect on voice.")]
        [SerializeField]
        private bool _enableReverb = true;

        [Tooltip("Reverb environment preset. Cave and Hallway are most dramatic.")]
        [SerializeField]
        private AudioReverbPreset _reverbPreset = AudioReverbPreset.Cave;

        [Tooltip("Enable pitch shifting effect.")]
        [SerializeField]
        private bool _enablePitchShift = false;

        [Tooltip("Pitch shift factor. 0.5 = octave down, 1.0 = normal, 2.0 = octave up.")]
        [Range(0.5f, 2f)]
        [SerializeField]
        private float _pitchShift = 1f;

        [Header("Debug")]
        [SerializeField]
        private bool _showDebugLogs = false;

        private EOSVoicePlayer _voicePlayer;
        private AudioSource _audioSource;
        private string _ownerPuid;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();

            // Configure AudioSource for voice
            _audioSource.spatialBlend = _spatialBlend;
            _audioSource.dopplerLevel = _dopplerLevel;
            _audioSource.minDistance = _minDistance;
            _audioSource.maxDistance = _maxDistance;
            _audioSource.rolloffMode = _rolloffMode;
            _audioSource.playOnAwake = false;
            _audioSource.loop = true;

            // Get or add EOSVoicePlayer
            _voicePlayer = GetComponent<EOSVoicePlayer>();
            if (_voicePlayer == null)
            {
                _voicePlayer = gameObject.AddComponent<EOSVoicePlayer>();
            }

            // Apply voice effects settings
            ApplyVoiceEffectsSettings();
        }

        /// <summary>
        /// Apply voice effect settings to the underlying EOSVoicePlayer.
        /// Call this if you change settings at runtime.
        /// </summary>
        public void ApplyVoiceEffectsSettings()
        {
            if (_voicePlayer == null) return;

            _voicePlayer.EnableReverb = _enableReverb;
            _voicePlayer.ReverbPreset = _reverbPreset;
            _voicePlayer.EnablePitchShift = _enablePitchShift;
            _voicePlayer.PitchShift = _pitchShift;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Don't play our own voice back to us
            if (IsOwner)
            {
                if (_showDebugLogs)
                    Debug.Log("[FishNetVoicePlayer] This is local player - disabling voice playback");

                _voicePlayer.enabled = false;
                _audioSource.enabled = false;
                return;
            }

            // Get the owner's PUID from the transport
            SetupVoiceForOwner();
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);

            // Ownership changed - update voice player
            if (!IsOwner)
            {
                SetupVoiceForOwner();
            }
        }

        private void SetupVoiceForOwner()
        {
            // Get the transport
            var transport = NetworkManager?.TransportManager?.Transport as EOSNativeTransport;
            if (transport == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.FishNetVoicePlayer, "FishNetVoicePlayer", "EOSNativeTransport not found");
                return;
            }

            // Get the owner's connection ID
            var ownerConnection = Owner;
            if (ownerConnection == null || !ownerConnection.IsValid)
            {
                if (_showDebugLogs)
                    Debug.Log("[FishNetVoicePlayer] No valid owner connection yet");
                return;
            }

            int connectionId = ownerConnection.ClientId;

            // Get the PUID for this connection ID from the server's connection map
            // The server tracks connectionId <-> PUID mappings
            string puid = transport.GetPuidForConnection(connectionId);

            if (string.IsNullOrEmpty(puid))
            {
                if (_showDebugLogs)
                    EOSDebugLogger.Log(DebugCategory.FishNetVoicePlayer, "FishNetVoicePlayer", $" No PUID found for connection {connectionId}");
                return;
            }

            _ownerPuid = puid;
            _voicePlayer.SetParticipant(puid);

            if (_showDebugLogs)
                EOSDebugLogger.Log(DebugCategory.FishNetVoicePlayer, "FishNetVoicePlayer", $" Set voice player for connection {connectionId} -> PUID {puid}");
        }

        /// <summary>
        /// Manually set the participant PUID (if you have it from another source).
        /// </summary>
        public void SetParticipantPuid(string puid)
        {
            _ownerPuid = puid;
            if (_voicePlayer != null)
            {
                _voicePlayer.SetParticipant(puid);
            }
        }

        /// <summary>
        /// The PUID of the player this voice player is rendering audio for.
        /// </summary>
        public string OwnerPuid => _ownerPuid;

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
        /// The underlying EOSVoicePlayer component.
        /// </summary>
        public EOSVoicePlayer VoicePlayer => _voicePlayer;

        /// <summary>
        /// Enable/disable reverb effect.
        /// </summary>
        public bool EnableReverb
        {
            get => _enableReverb;
            set { _enableReverb = value; if (_voicePlayer != null) _voicePlayer.EnableReverb = value; }
        }

        /// <summary>
        /// Reverb environment preset.
        /// </summary>
        public AudioReverbPreset ReverbPreset
        {
            get => _reverbPreset;
            set { _reverbPreset = value; if (_voicePlayer != null) _voicePlayer.ReverbPreset = value; }
        }

        /// <summary>
        /// Enable/disable pitch shifting.
        /// </summary>
        public bool EnablePitchShift
        {
            get => _enablePitchShift;
            set { _enablePitchShift = value; if (_voicePlayer != null) _voicePlayer.EnablePitchShift = value; }
        }

        /// <summary>
        /// Pitch shift factor (0.5 = octave down, 1.0 = normal, 2.0 = octave up).
        /// </summary>
        public float PitchShift
        {
            get => _pitchShift;
            set { _pitchShift = value; if (_voicePlayer != null) _voicePlayer.PitchShift = value; }
        }
    }
}
