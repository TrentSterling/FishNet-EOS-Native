using UnityEngine;
using FishNet.Transport.EOSNative.Lobbies;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Spawns confetti particle effect when players join the lobby.
    /// Add to your scene for a fun visual effect.
    /// </summary>
    public class EOSConfettiEffect : MonoBehaviour
    {
        #region Settings
        [Header("Confetti Settings")]
        [SerializeField]
        [Tooltip("Enable confetti effects.")]
        private bool _enabled = true;

        [SerializeField]
        [Tooltip("Confetti particle system prefab. If null, creates a default one.")]
        private ParticleSystem _confettiPrefab;

        [SerializeField]
        [Tooltip("Spawn confetti when local player joins a lobby.")]
        private bool _onLocalJoin = true;

        [SerializeField]
        [Tooltip("Spawn confetti when other players join.")]
        private bool _onOtherJoin = true;

        [SerializeField]
        [Tooltip("Duration of the confetti burst.")]
        [Range(0.5f, 5f)]
        private float _duration = 2f;

        [SerializeField]
        [Tooltip("Number of particles to emit.")]
        [Range(10, 500)]
        private int _particleCount = 100;
        #endregion

        #region Public Properties
        public bool Enabled { get => _enabled; set => _enabled = value; }
        #endregion

        #region Private Fields
        private ParticleSystem _activeConfetti;
        private EOSLobbyManager _lobbyManager;
        #endregion

        #region Unity Lifecycle
        private void Start()
        {
            _lobbyManager = EOSLobbyManager.Instance;
            if (_lobbyManager != null)
            {
                _lobbyManager.OnJoinedLobby += OnJoinedLobby;
                _lobbyManager.OnMemberJoined += OnMemberJoined;
            }

            // Create default confetti system if none provided
            if (_confettiPrefab == null)
            {
                CreateDefaultConfetti();
            }
        }

        private void OnDestroy()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnJoinedLobby -= OnJoinedLobby;
                _lobbyManager.OnMemberJoined -= OnMemberJoined;
            }

            if (_activeConfetti != null)
            {
                Destroy(_activeConfetti.gameObject);
            }
        }
        #endregion

        #region Event Handlers
        private void OnJoinedLobby(LobbyData lobby)
        {
            if (_enabled && _onLocalJoin)
            {
                SpawnConfetti();
                EOSToastManager.Success("Welcome!", $"Joined lobby {lobby.JoinCode}");
            }
        }

        private void OnMemberJoined(string puid)
        {
            if (_enabled && _onOtherJoin)
            {
                string name = EOSPlayerRegistry.Instance?.GetDisplayName(puid) ?? "Player";
                SpawnConfetti();
                EOSToastManager.Info("Player Joined", $"{name} joined the lobby");
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// Manually trigger confetti effect.
        /// </summary>
        public void SpawnConfetti()
        {
            if (_activeConfetti == null) return;

            // Position at screen center (world space)
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 pos = mainCam.transform.position + mainCam.transform.forward * 5f;
                _activeConfetti.transform.position = pos;
            }

            _activeConfetti.Emit(_particleCount);
        }

        /// <summary>
        /// Spawn confetti at a specific world position.
        /// </summary>
        public void SpawnConfettiAt(Vector3 worldPosition)
        {
            if (_activeConfetti == null) return;

            _activeConfetti.transform.position = worldPosition;
            _activeConfetti.Emit(_particleCount);
        }
        #endregion

        #region Private Methods
        private void CreateDefaultConfetti()
        {
            var go = new GameObject("ConfettiSystem");
            go.transform.SetParent(transform);
            _activeConfetti = go.AddComponent<ParticleSystem>();

            var main = _activeConfetti.main;
            main.duration = _duration;
            main.loop = false;
            main.startLifetime = 3f;
            main.startSpeed = 8f;
            main.startSize = 0.15f;
            main.gravityModifier = 0.5f;
            main.maxParticles = 1000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Colorful particles
            var colorOverLifetime = _activeConfetti.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            // Random colors
            var startColor = main.startColor;
            startColor.mode = ParticleSystemGradientMode.RandomColor;
            var colorGradient = new Gradient();
            colorGradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.3f, 0.3f), 0f),    // Red
                    new GradientColorKey(new Color(0.3f, 1f, 0.3f), 0.25f), // Green
                    new GradientColorKey(new Color(0.3f, 0.3f, 1f), 0.5f),  // Blue
                    new GradientColorKey(new Color(1f, 1f, 0.3f), 0.75f),   // Yellow
                    new GradientColorKey(new Color(1f, 0.3f, 1f), 1f)       // Magenta
                },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            startColor.gradient = colorGradient;
            main.startColor = startColor;

            // Emission burst
            var emission = _activeConfetti.emission;
            emission.enabled = true;
            emission.rateOverTime = 0;

            // Shape - cone
            var shape = _activeConfetti.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 45f;
            shape.radius = 0.5f;

            // Rotation
            var rotationOverLifetime = _activeConfetti.rotationOverLifetime;
            rotationOverLifetime.enabled = true;
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

            // Renderer
            var renderer = _activeConfetti.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = new Material(Shader.Find("Particles/Standard Unlit"));

            // Disable auto-play
            _activeConfetti.Stop();
        }
        #endregion
    }
}
