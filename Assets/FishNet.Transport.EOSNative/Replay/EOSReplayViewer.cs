using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Replay
{
    /// <summary>
    /// Bridges replay playback with the spectator camera system.
    /// Provides a unified viewing experience for replays.
    /// </summary>
    public class EOSReplayViewer : MonoBehaviour
    {
        #region Singleton

        private static EOSReplayViewer _instance;
        public static EOSReplayViewer Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSReplayViewer>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSReplayViewer");
                        _instance = go.AddComponent<EOSReplayViewer>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when viewing starts.</summary>
        public event Action<ReplayHeader> OnViewingStarted;

        /// <summary>Fired when viewing stops.</summary>
        public event Action OnViewingStopped;

        #endregion

        #region Public Properties

        /// <summary>Whether currently viewing a replay.</summary>
        public bool IsViewing { get; private set; }

        /// <summary>The replay player instance.</summary>
        public EOSReplayPlayer Player => _player;

        /// <summary>Current replay header (if viewing).</summary>
        public ReplayHeader? CurrentReplay => _player?.Header;

        /// <summary>Playback state.</summary>
        public PlaybackState PlaybackState => _player?.State ?? PlaybackState.Stopped;

        /// <summary>Current playback time.</summary>
        public float CurrentTime => _player?.CurrentTime ?? 0f;

        /// <summary>Total duration.</summary>
        public float Duration => _player?.Duration ?? 0f;

        /// <summary>Playback speed.</summary>
        public float PlaybackSpeed
        {
            get => _player?.PlaybackSpeed ?? 1f;
            set { if (_player != null) _player.PlaybackSpeed = value; }
        }

        #endregion

        #region Serialized Fields

        [Header("Keyboard Controls")]
        [Tooltip("Enable keyboard shortcuts during replay viewing.")]
        [SerializeField] private bool _enableKeyboardControls = true;

        [Tooltip("Seconds to skip with arrow keys.")]
        [SerializeField] private float _skipAmount = 5f;

        #endregion

        #region Private Fields

        private EOSReplayPlayer _player;
        private EOSSpectatorMode _spectator;
        private bool _wasSpectatingBefore;

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

        private void Start()
        {
            _player = EOSReplayPlayer.Instance;
            _spectator = EOSSpectatorMode.Instance;

            // Subscribe to player events
            if (_player != null)
            {
                _player.OnReplayEnded += OnReplayEnded;
                _player.OnStateChanged += OnPlayerStateChanged;
            }
        }

        private void Update()
        {
            if (!IsViewing || !_enableKeyboardControls) return;

            // Play/Pause - Space
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TogglePlayPause();
            }

            // Skip backward - Left Arrow or A
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                Skip(-_skipAmount);
            }

            // Skip forward - Right Arrow or D
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                Skip(_skipAmount);
            }

            // Go to start - Home
            if (Input.GetKeyDown(KeyCode.Home))
            {
                Seek(0f);
            }

            // Go to end - End
            if (Input.GetKeyDown(KeyCode.End))
            {
                Seek(Duration - 0.1f);
            }

            // Speed controls - number keys
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                SetSpeed(1f);
            }
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SetSpeed(2f);
            }
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SetSpeed(0.5f);
            }
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                SetSpeed(4f);
            }

            // Cycle speed - Plus/Minus or Q/E
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.E))
            {
                CycleSpeedUp();
            }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Q))
            {
                CycleSpeedDown();
            }

            // Cycle targets - Tab or brackets
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                CycleTarget(1);
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                CycleTarget(-1);
            }

            // Stop viewing - Escape
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                StopViewing();
            }
        }

        private void OnDestroy()
        {
            if (_player != null)
            {
                _player.OnReplayEnded -= OnReplayEnded;
                _player.OnStateChanged -= OnPlayerStateChanged;
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start viewing a replay.
        /// </summary>
        public void StartViewing(ReplayFile replay)
        {
            if (IsViewing)
            {
                StopViewing();
            }

            // Load replay into player
            _player.LoadReplay(replay);

            // Setup spectator mode with replay targets
            _wasSpectatingBefore = _spectator != null && _spectator.IsSpectating;

            if (_spectator != null)
            {
                _spectator.SetExternalTargetProvider(GetReplayTargets);
                _spectator.EnterSpectatorMode();
            }

            IsViewing = true;

            // Start playback
            _player.Play();

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayViewer",
                $"Started viewing replay: {replay.Header.ReplayId}");

            OnViewingStarted?.Invoke(replay.Header);
        }

        /// <summary>
        /// Start viewing a replay by ID (loads from storage first).
        /// </summary>
        public async Task<bool> StartViewingAsync(string replayId)
        {
            var storage = EOSReplayStorage.Instance;
            if (storage == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayViewer", "Storage not available");
                return false;
            }

            var replay = await storage.LoadLocalAsync(replayId);
            if (!replay.HasValue)
            {
                EOSDebugLogger.LogWarning(DebugCategory.Stats, "EOSReplayViewer", $"Replay not found: {replayId}");
                return false;
            }

            StartViewing(replay.Value);
            return true;
        }

        /// <summary>
        /// Stop viewing the current replay.
        /// </summary>
        public void StopViewing()
        {
            if (!IsViewing) return;

            // Stop playback
            _player.Stop();
            _player.Unload();

            // Exit spectator mode (if we entered it)
            if (_spectator != null)
            {
                _spectator.SetExternalTargetProvider(null);

                if (!_wasSpectatingBefore)
                {
                    _spectator.ExitSpectatorMode();
                }
            }

            IsViewing = false;

            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayViewer", "Stopped viewing replay");
            OnViewingStopped?.Invoke();
        }

        /// <summary>
        /// Toggle play/pause.
        /// </summary>
        public void TogglePlayPause()
        {
            if (_player == null) return;

            if (_player.State == PlaybackState.Playing)
            {
                _player.Pause();
            }
            else if (_player.State == PlaybackState.Paused || _player.State == PlaybackState.Stopped)
            {
                _player.Play();
            }
        }

        /// <summary>
        /// Seek to a specific time.
        /// </summary>
        public void Seek(float time)
        {
            _player?.Seek(time);
        }

        /// <summary>
        /// Seek by percentage (0-1).
        /// </summary>
        public void SeekPercent(float percent)
        {
            if (_player == null) return;
            _player.Seek(percent * _player.Duration);
        }

        /// <summary>
        /// Skip forward/backward by seconds.
        /// </summary>
        public void Skip(float seconds)
        {
            if (_player == null) return;
            _player.Seek(_player.CurrentTime + seconds);
        }

        /// <summary>
        /// Set playback speed.
        /// </summary>
        public void SetSpeed(float speed)
        {
            if (_player != null)
            {
                _player.PlaybackSpeed = speed;
            }
        }

        /// <summary>
        /// Cycle playback speed: 0.5x -> 1x -> 2x -> 4x -> 0.5x
        /// </summary>
        public void CycleSpeed()
        {
            if (_player == null) return;

            float current = _player.PlaybackSpeed;
            if (current <= 0.5f) _player.PlaybackSpeed = 1f;
            else if (current <= 1f) _player.PlaybackSpeed = 2f;
            else if (current <= 2f) _player.PlaybackSpeed = 4f;
            else _player.PlaybackSpeed = 0.5f;
        }

        /// <summary>
        /// Increase playback speed: 0.5x -> 1x -> 2x -> 4x
        /// </summary>
        public void CycleSpeedUp()
        {
            if (_player == null) return;

            float current = _player.PlaybackSpeed;
            if (current < 1f) _player.PlaybackSpeed = 1f;
            else if (current < 2f) _player.PlaybackSpeed = 2f;
            else if (current < 4f) _player.PlaybackSpeed = 4f;
        }

        /// <summary>
        /// Decrease playback speed: 4x -> 2x -> 1x -> 0.5x
        /// </summary>
        public void CycleSpeedDown()
        {
            if (_player == null) return;

            float current = _player.PlaybackSpeed;
            if (current > 2f) _player.PlaybackSpeed = 2f;
            else if (current > 1f) _player.PlaybackSpeed = 1f;
            else if (current > 0.5f) _player.PlaybackSpeed = 0.5f;
        }

        /// <summary>
        /// Get the name of the current spectator target.
        /// </summary>
        public string GetCurrentTargetName()
        {
            return _spectator?.GetCurrentTargetName() ?? "No Target";
        }

        /// <summary>
        /// Cycle to next/previous spectator target.
        /// </summary>
        public void CycleTarget(int direction)
        {
            _spectator?.CycleTarget(direction);
        }

        #endregion

        #region Private Methods

        private List<Transform> GetReplayTargets()
        {
            var targets = new List<Transform>();

            if (_player == null) return targets;

            foreach (var obj in _player.GetPlayerObjects())
            {
                if (obj != null)
                {
                    targets.Add(obj.transform);
                }
            }

            return targets;
        }

        private void OnReplayEnded()
        {
            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayViewer", "Replay ended");

            // Auto-loop or stop based on preference
            // For now, just pause at the end
            _player?.Pause();
        }

        private void OnPlayerStateChanged(PlaybackState state)
        {
            EOSDebugLogger.Log(DebugCategory.Stats, "EOSReplayViewer", $"Playback state: {state}");
        }

        #endregion
    }
}
