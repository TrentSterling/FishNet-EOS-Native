using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Migration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Networked player ball with WASD movement.
    /// Uses PhysicsNetworkTransform for sync - owner runs physics, others spring to follow.
    ///
    /// Setup:
    /// 1. Add to sphere with Rigidbody + NetworkObject
    /// 2. Add PhysicsNetworkTransform (NOT NetworkTransform!)
    /// 3. Assign rigidbodyToTrack on PhysicsNetworkTransform
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EOSNetworkPlayer))]
    public class PlayerBall : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float _acceleration = 25f;
        [SerializeField] private float _maxSpeed = 12f;
        [SerializeField] [Range(0f, 1f)] private float _friction = 0.15f;
        [SerializeField] private float _brakeMultiplier = 2f;

        [Header("Jumping")]
        [SerializeField] private float _jumpForce = 8f;
        [SerializeField] private float _groundCheckDistance = 0.15f;
        [SerializeField] private LayerMask _groundLayers = ~0;
        [SerializeField] private float _jumpCooldown = 0.1f;

        [Header("Visual")]
        [SerializeField] private Renderer _renderer;

        /// <summary>
        /// Synced player color so everyone sees the same color.
        /// </summary>
        private readonly SyncVar<Color> _playerColor = new();

        private Rigidbody _rb;
        private Vector2 _input;
        private bool _wantsJump;
        private float _lastJumpTime;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_renderer == null)
                _renderer = GetComponent<Renderer>();

            _playerColor.OnChange += OnColorChanged;
        }

        private void OnDestroy()
        {
            _playerColor.OnChange -= OnColorChanged;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Apply color on late joiners
            if (_renderer != null)
                _renderer.material.color = _playerColor.Value;

            // PhysicsNetworkTransform handles sync - rigidbody stays non-kinematic
            // so players can push each other with spring-based physics
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Skip random color if this is a migration restore - color will be restored from saved state
            var migratable = GetComponent<HostMigratable>();
            if (migratable != null && migratable.LoadState.HasValue)
            {
                return;
            }

            _playerColor.Value = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f);
        }

        private void OnColorChanged(Color oldColor, Color newColor, bool asServer)
        {
            if (_renderer != null)
                _renderer.material.color = newColor;
        }

        private void Update()
        {
            // Only owner reads input
            if (!IsOwner) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Read WASD
            _input = Vector2.zero;
            if (keyboard.wKey.isPressed) _input.y += 1f;
            if (keyboard.sKey.isPressed) _input.y -= 1f;
            if (keyboard.aKey.isPressed) _input.x -= 1f;
            if (keyboard.dKey.isPressed) _input.x += 1f;
            _input = Vector2.ClampMagnitude(_input, 1f);

            // Jump
            if (keyboard.spaceKey.wasPressedThisFrame && Time.time >= _lastJumpTime + _jumpCooldown)
            {
                _wantsJump = true;
            }
        }

        private void FixedUpdate()
        {
            // Only owner runs physics
            if (!IsOwner || !IsSpawned) return;

            // Ground check
            bool isGrounded = Physics.SphereCast(
                transform.position,
                0.4f,
                Vector3.down,
                out _,
                _groundCheckDistance,
                _groundLayers,
                QueryTriggerInteraction.Ignore
            );

            // Jump
            if (_wantsJump && isGrounded)
            {
                Vector3 vel = _rb.linearVelocity;
                vel.y = _jumpForce;
                _rb.linearVelocity = vel;
                _lastJumpTime = Time.time;
                _wantsJump = false;
            }
            else if (_wantsJump && !isGrounded)
            {
                _wantsJump = false; // Tried to jump but not grounded
            }

            // Movement
            Vector3 currentVel = _rb.linearVelocity;
            Vector3 horizontalVel = new Vector3(currentVel.x, 0f, currentVel.z);
            Vector3 inputDir = new Vector3(_input.x, 0f, _input.y);
            bool hasInput = inputDir.sqrMagnitude > 0.01f;

            if (hasInput)
            {
                Vector3 targetVel = inputDir * _maxSpeed;
                float dot = Vector3.Dot(horizontalVel.normalized, inputDir.normalized);
                bool isBraking = dot < -0.5f && horizontalVel.magnitude > 1f;

                Vector3 velDiff = targetVel - horizontalVel;
                float accel = _acceleration * (isBraking ? _brakeMultiplier : 1f);
                Vector3 velocityChange = Vector3.ClampMagnitude(velDiff, accel * Time.fixedDeltaTime);

                _rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
            else if (isGrounded)
            {
                // Friction
                if (horizontalVel.magnitude > 0.1f)
                {
                    _rb.AddForce(-horizontalVel * _friction, ForceMode.VelocityChange);
                }
                else if (horizontalVel.magnitude > 0f)
                {
                    _rb.linearVelocity = new Vector3(0f, currentVel.y, 0f);
                }
            }

            // Clamp max speed
            horizontalVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
            if (horizontalVel.magnitude > _maxSpeed)
            {
                Vector3 clamped = horizontalVel.normalized * _maxSpeed;
                _rb.linearVelocity = new Vector3(clamped.x, _rb.linearVelocity.y, clamped.z);
            }
        }

        private void OnGUI()
        {
            if (!IsSpawned || Camera.main == null) return;

            Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.5f);
            if (screenPos.z > 0)
            {
                // Get display name from EOSNetworkPlayer if available
                var networkPlayer = GetComponent<EOSNetworkPlayer>();
                string displayName = networkPlayer?.DisplayName ?? $"P{OwnerId}";
                string label = IsOwner ? "YOU" : displayName;

                // Calculate width based on text length
                float width = Mathf.Max(60, label.Length * 8);
                GUI.Label(new Rect(screenPos.x - width / 2, Screen.height - screenPos.y, width, 20), label);
            }
        }
    }
}
