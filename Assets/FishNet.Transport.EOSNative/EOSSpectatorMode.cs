using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using FishNet;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Spectator mode support. Allows players to watch games without participating.
    /// Spectators join the lobby but don't spawn player objects.
    /// </summary>
    public class EOSSpectatorMode : MonoBehaviour
    {
        #region Singleton

        private static EOSSpectatorMode _instance;
        public static EOSSpectatorMode Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<EOSSpectatorMode>();
                    if (_instance == null)
                    {
                        var go = new GameObject("EOSSpectatorMode");
                        _instance = go.AddComponent<EOSSpectatorMode>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Events

        /// <summary>Fired when spectator mode is enabled/disabled.</summary>
        public event Action<bool> OnSpectatorModeChanged;

        /// <summary>Fired when spectator target changes.</summary>
        public event Action<NetworkObject> OnSpectatorTargetChanged;

        #endregion

        #region Serialized Fields

        [Header("Spectator Settings")]
        [Tooltip("When spectating, follow this far behind the target.")]
        [SerializeField] private float _followDistance = 10f;

        [Tooltip("When spectating, stay this high above the target.")]
        [SerializeField] private float _followHeight = 5f;

        [Tooltip("Camera smoothing when following target.")]
        [SerializeField] private float _followSmoothing = 5f;

        [Tooltip("Enable free camera mode (WASD to fly around).")]
        [SerializeField] private bool _allowFreeCamera = true;

        [Tooltip("Free camera movement speed.")]
        [SerializeField] private float _freeCameraSpeed = 10f;

        #endregion

        #region Public Properties

        /// <summary>Whether currently in spectator mode.</summary>
        public bool IsSpectating { get; private set; }

        /// <summary>Whether using free camera (vs following a target).</summary>
        public bool IsFreeCameraMode { get; private set; }

        /// <summary>Current spectator target (player being watched).</summary>
        public NetworkObject CurrentTarget { get; private set; }

        /// <summary>Index of current target in the list.</summary>
        public int CurrentTargetIndex { get; private set; }

        /// <summary>All spectatable targets.</summary>
        public IReadOnlyList<NetworkObject> SpectateTargets => _spectateTargets;

        /// <summary>Spectator camera (created when spectating).</summary>
        public Camera SpectatorCamera => _spectatorCamera;

        #endregion

        #region Private Fields

        private List<NetworkObject> _spectateTargets = new();
        private Camera _spectatorCamera;
        private GameObject _cameraHolder;
        private NetworkManager _networkManager;
        private Vector3 _freeCameraVelocity;

        // External target provider for replay mode
        private Func<List<Transform>> _externalTargetProvider;
        private List<Transform> _externalTargets = new();

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
            _networkManager = InstanceFinder.NetworkManager;
        }

        private void Update()
        {
            if (!IsSpectating) return;

            // Handle input for cycling targets
            if (Input.GetKeyDown(KeyCode.Mouse0) || Input.GetKeyDown(KeyCode.RightArrow))
            {
                CycleTarget(1);
            }
            else if (Input.GetKeyDown(KeyCode.Mouse1) || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                CycleTarget(-1);
            }

            // Toggle free camera
            if (_allowFreeCamera && Input.GetKeyDown(KeyCode.F))
            {
                IsFreeCameraMode = !IsFreeCameraMode;
                EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", $"Free camera: {IsFreeCameraMode}");
            }

            // Update camera
            UpdateCamera();
        }

        private void LateUpdate()
        {
            if (!IsSpectating || _spectatorCamera == null) return;

            if (IsFreeCameraMode)
            {
                UpdateFreeCamera();
            }
            else if (CurrentTarget != null)
            {
                UpdateFollowCamera();
            }
        }

        private void OnDestroy()
        {
            if (IsSpectating) ExitSpectatorMode();

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Join a lobby as a spectator.
        /// </summary>
        public async Task<(Result result, LobbyData? lobby)> JoinAsSpectatorAsync(string lobbyCode)
        {
            var transport = FindAnyObjectByType<EOSNativeTransport>();
            if (transport == null) return (Result.NotConfigured, null);

            // Join the lobby normally
            var joinResult = await transport.JoinLobbyAsync(lobbyCode);

            if (joinResult.Item1 == Result.Success)
            {
                // Enter spectator mode (don't spawn player)
                EnterSpectatorMode();
            }

            return (joinResult.Item1, joinResult.Item2);
        }

        /// <summary>
        /// Enter spectator mode. Call after joining a lobby if you want to spectate.
        /// </summary>
        public void EnterSpectatorMode()
        {
            if (IsSpectating) return;

            IsSpectating = true;

            // Create spectator camera
            CreateSpectatorCamera();

            // Find spectatable targets
            RefreshTargets();

            // Start with first target
            if (_spectateTargets.Count > 0)
            {
                SetTarget(0);
            }
            else
            {
                IsFreeCameraMode = true;
            }

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", "Entered spectator mode");
            OnSpectatorModeChanged?.Invoke(true);
        }

        /// <summary>
        /// Exit spectator mode.
        /// </summary>
        public void ExitSpectatorMode()
        {
            if (!IsSpectating) return;

            IsSpectating = false;
            IsFreeCameraMode = false;
            CurrentTarget = null;

            // Destroy spectator camera
            if (_cameraHolder != null)
            {
                Destroy(_cameraHolder);
                _cameraHolder = null;
                _spectatorCamera = null;
            }

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", "Exited spectator mode");
            OnSpectatorModeChanged?.Invoke(false);
        }

        /// <summary>
        /// Set spectator target by index.
        /// </summary>
        public void SetTarget(int index)
        {
            if (_spectateTargets.Count == 0) return;

            CurrentTargetIndex = Mathf.Clamp(index, 0, _spectateTargets.Count - 1);
            CurrentTarget = _spectateTargets[CurrentTargetIndex];
            IsFreeCameraMode = false;

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", $"Spectating: {CurrentTarget.name}");
            OnSpectatorTargetChanged?.Invoke(CurrentTarget);
        }

        /// <summary>
        /// Set spectator target by NetworkObject.
        /// </summary>
        public void SetTarget(NetworkObject target)
        {
            if (target == null) return;

            int index = _spectateTargets.IndexOf(target);
            if (index >= 0)
            {
                SetTarget(index);
            }
        }

        /// <summary>
        /// Cycle to next/previous target.
        /// </summary>
        public void CycleTarget(int direction)
        {
            // Handle replay mode (external targets)
            if (_externalTargetProvider != null)
            {
                if (_externalTargets.Count == 0) return;

                int newIndex = (CurrentTargetIndex + direction + _externalTargets.Count) % _externalTargets.Count;
                SetTargetTransform(newIndex);
                return;
            }

            // Handle live mode
            if (_spectateTargets.Count == 0) return;

            int newIndex = (CurrentTargetIndex + direction + _spectateTargets.Count) % _spectateTargets.Count;
            SetTarget(newIndex);
        }

        /// <summary>
        /// Refresh the list of spectatable targets.
        /// </summary>
        public void RefreshTargets()
        {
            _spectateTargets.Clear();
            _externalTargets.Clear();

            // Check for external target provider (replay mode)
            if (_externalTargetProvider != null)
            {
                var targets = _externalTargetProvider();
                if (targets != null)
                {
                    _externalTargets.AddRange(targets);
                }

                EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", $"Found {_externalTargets.Count} replay targets");

                // Handle external target list changes
                if (CurrentTarget != null)
                {
                    bool found = false;
                    foreach (var t in _externalTargets)
                    {
                        if (t != null && t.gameObject == CurrentTarget.gameObject)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        if (_externalTargets.Count > 0)
                        {
                            SetTargetTransform(0);
                        }
                        else
                        {
                            CurrentTarget = null;
                            IsFreeCameraMode = true;
                        }
                    }
                }
                return;
            }

            // Find all player objects (live mode)
            if (_networkManager == null) return;

            foreach (var nob in _networkManager.ServerManager.Objects.Spawned.Values)
            {
                // Look for objects with player ownership
                if (nob.Owner.IsValid && !nob.IsOwner)
                {
                    _spectateTargets.Add(nob);
                }
            }

            EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", $"Found {_spectateTargets.Count} spectatable targets");

            // If current target is gone, switch
            if (CurrentTarget != null && !_spectateTargets.Contains(CurrentTarget))
            {
                if (_spectateTargets.Count > 0)
                {
                    SetTarget(0);
                }
                else
                {
                    CurrentTarget = null;
                    IsFreeCameraMode = true;
                }
            }
        }

        /// <summary>
        /// Set an external target provider for replay mode.
        /// Pass null to return to live targeting.
        /// </summary>
        public void SetExternalTargetProvider(Func<List<Transform>> provider)
        {
            _externalTargetProvider = provider;
            _externalTargets.Clear();

            if (provider != null)
            {
                RefreshTargets();
            }
        }

        /// <summary>
        /// Set spectator target by index from external targets (replay mode).
        /// </summary>
        private void SetTargetTransform(int index)
        {
            if (_externalTargets.Count == 0) return;

            CurrentTargetIndex = Mathf.Clamp(index, 0, _externalTargets.Count - 1);
            var targetTransform = _externalTargets[CurrentTargetIndex];

            if (targetTransform != null)
            {
                // Try to get NetworkObject for compatibility
                CurrentTarget = targetTransform.GetComponent<NetworkObject>();
                IsFreeCameraMode = false;

                EOSDebugLogger.Log(DebugCategory.PlayerBall, "EOSSpectatorMode", $"Spectating replay target: {targetTransform.name}");
                OnSpectatorTargetChanged?.Invoke(CurrentTarget);
            }
        }

        /// <summary>
        /// Get name of current target (for UI display).
        /// </summary>
        public string GetCurrentTargetName()
        {
            // Handle replay mode (external targets)
            if (_externalTargetProvider != null && _externalTargets.Count > 0)
            {
                if (CurrentTargetIndex >= 0 && CurrentTargetIndex < _externalTargets.Count)
                {
                    var target = _externalTargets[CurrentTargetIndex];
                    if (target != null) return target.name;
                }
                return "No Target";
            }

            if (CurrentTarget == null) return "Free Camera";

            // Try to get player name from owner
            if (CurrentTarget.Owner.IsValid)
            {
                int clientId = CurrentTarget.Owner.ClientId;
                // Try to find PUID from client ID
                var transport = FindAnyObjectByType<EOSNativeTransport>();
                if (transport != null)
                {
                    string puid = transport.GetPuidForConnection(clientId);
                    if (!string.IsNullOrEmpty(puid))
                    {
                        string name = EOSPlayerRegistry.Instance?.GetPlayerName(puid);
                        if (!string.IsNullOrEmpty(name)) return name;
                    }
                }
            }

            return CurrentTarget.name;
        }

        #endregion

        #region Private Methods

        private void CreateSpectatorCamera()
        {
            _cameraHolder = new GameObject("SpectatorCamera");
            _spectatorCamera = _cameraHolder.AddComponent<Camera>();
            _spectatorCamera.tag = "MainCamera";
            _spectatorCamera.clearFlags = CameraClearFlags.Skybox;
            _spectatorCamera.depth = 100; // Render on top

            // Position at origin initially
            _cameraHolder.transform.position = Vector3.up * 10f;
            _cameraHolder.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

            // Disable any existing main camera
            var mainCam = Camera.main;
            if (mainCam != null && mainCam != _spectatorCamera)
            {
                mainCam.enabled = false;
            }
        }

        private void UpdateCamera()
        {
            // Periodically refresh targets
            if (Time.frameCount % 60 == 0)
            {
                RefreshTargets();
            }
        }

        private void UpdateFollowCamera()
        {
            if (_spectatorCamera == null) return;

            Transform targetTransform = null;

            // Get target transform (replay mode or live mode)
            if (_externalTargetProvider != null && _externalTargets.Count > 0)
            {
                if (CurrentTargetIndex >= 0 && CurrentTargetIndex < _externalTargets.Count)
                {
                    targetTransform = _externalTargets[CurrentTargetIndex];
                }
            }
            else if (CurrentTarget != null)
            {
                targetTransform = CurrentTarget.transform;
            }

            if (targetTransform == null) return;

            Vector3 targetPos = targetTransform.position;
            Vector3 targetForward = targetTransform.forward;

            // Calculate desired camera position
            Vector3 desiredPos = targetPos - targetForward * _followDistance + Vector3.up * _followHeight;

            // Smooth follow
            _cameraHolder.transform.position = Vector3.Lerp(
                _cameraHolder.transform.position,
                desiredPos,
                Time.deltaTime * _followSmoothing
            );

            // Look at target
            _cameraHolder.transform.LookAt(targetPos + Vector3.up);
        }

        private void UpdateFreeCamera()
        {
            if (_spectatorCamera == null) return;

            // WASD movement
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            float y = 0f;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) y = 1f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) y = -1f;

            Vector3 move = new Vector3(h, y, v);
            move = _cameraHolder.transform.TransformDirection(move);
            _cameraHolder.transform.position += move * _freeCameraSpeed * Time.deltaTime;

            // Mouse look (right click held)
            if (Input.GetMouseButton(1))
            {
                float mouseX = Input.GetAxis("Mouse X") * 3f;
                float mouseY = Input.GetAxis("Mouse Y") * 3f;

                Vector3 euler = _cameraHolder.transform.eulerAngles;
                euler.y += mouseX;
                euler.x -= mouseY;
                euler.x = Mathf.Clamp(euler.x > 180 ? euler.x - 360 : euler.x, -89f, 89f);
                _cameraHolder.transform.eulerAngles = euler;
            }
        }

        #endregion
    }
}
