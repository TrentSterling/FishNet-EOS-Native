using FishNet.Connection;
using FishNet.Object;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Makes a networked rigidbody interactive for all clients.
    /// - Forces non-kinematic so physics works locally
    /// - Steals ownership on collision with player for responsive interaction
    /// - Works with NetworkTransform for position sync
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPhysicsObject : NetworkBehaviour
    {
        [Header("Physics")]
        [Tooltip("Force non-kinematic every frame so physics always works.")]
        [SerializeField] private bool _forceNonKinematic = true;

        [Header("Ownership")]
        [Tooltip("Take ownership when a player collides with this object.")]
        [SerializeField] private bool _stealOwnershipOnCollision = true;

        [Tooltip("Only steal ownership if we don't already own it and the collision is strong enough.")]
        [SerializeField] private float _minCollisionForce = 0.5f;

        [Tooltip("Cooldown before ownership can be stolen again (prevents rapid swapping).")]
        [SerializeField] private float _ownershipCooldown = 0.25f;

        [Header("Debug")]
        [SerializeField] private bool _showDebugLogs = false;

        private Rigidbody _rb;
        private float _lastOwnershipChangeTime;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            // Start non-kinematic
            if (_forceNonKinematic)
                _rb.isKinematic = false;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned) return;

            // Force non-kinematic every frame - NetworkTransform sets kinematic on non-owners
            if (_forceNonKinematic && _rb.isKinematic)
            {
                _rb.isKinematic = false;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsSpawned) return;
            if (!_stealOwnershipOnCollision) return;

            // Check cooldown
            if (Time.time < _lastOwnershipChangeTime + _ownershipCooldown)
                return;

            // Check if we collided with a player
            var playerBall = collision.gameObject.GetComponent<PlayerBall>();
            if (playerBall == null) return;

            // Only the player's owner should request ownership
            if (!playerBall.IsOwner) return;

            // Check collision force
            if (collision.relativeVelocity.magnitude < _minCollisionForce)
                return;

            // Already own it? No need to steal
            if (IsOwner) return;

            // Request ownership from server
            RequestOwnership();
        }

        private void OnCollisionStay(Collision collision)
        {
            // Also check during sustained contact (pushing)
            if (!IsSpawned) return;
            if (!_stealOwnershipOnCollision) return;

            // Less frequent checks during stay
            if (Time.time < _lastOwnershipChangeTime + _ownershipCooldown * 2f)
                return;

            var playerBall = collision.gameObject.GetComponent<PlayerBall>();
            if (playerBall == null) return;
            if (!playerBall.IsOwner) return;
            if (IsOwner) return;

            // Check if actually being pushed (has velocity)
            if (collision.relativeVelocity.magnitude < _minCollisionForce * 0.5f)
                return;

            RequestOwnership();
        }

        /// <summary>
        /// Request ownership of this object.
        /// </summary>
        public void RequestOwnership()
        {
            if (!IsSpawned) return;
            if (IsOwner) return;

            if (_showDebugLogs)
                EOSDebugLogger.Log(DebugCategory.NetworkPhysicsObject, "NetworkPhysicsObject", $" Requesting ownership of {gameObject.name}");

            _lastOwnershipChangeTime = Time.time;

            // Request ownership via server RPC
            ServerRequestOwnership();
        }

        /// <summary>
        /// Server RPC to transfer ownership.
        /// Called when a client wants to take ownership.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerRequestOwnership(NetworkConnection caller = null)
        {
            if (caller == null) return;

            if (_showDebugLogs)
                EOSDebugLogger.Log(DebugCategory.NetworkPhysicsObject, "NetworkPhysicsObject", $" Server granting ownership of {gameObject.name} to {caller.ClientId}");

            // Give ownership to the requesting client
            GiveOwnership(caller);
        }

        /// <summary>
        /// Release ownership back to server.
        /// Call this when done interacting.
        /// </summary>
        public void ReleaseOwnership()
        {
            if (!IsSpawned) return;
            if (!IsOwner) return;

            if (_showDebugLogs)
                EOSDebugLogger.Log(DebugCategory.NetworkPhysicsObject, "NetworkPhysicsObject", $" Releasing ownership of {gameObject.name}");

            NetworkObject.RemoveOwnership();
        }

        /// <summary>
        /// Auto-release ownership when object comes to rest.
        /// </summary>
        private void Update()
        {
            if (!IsSpawned) return;
            if (!IsOwner) return;

            // If we own it and it's nearly stopped, consider releasing ownership
            // This prevents one player from hogging ownership of a stationary object
            if (_rb.linearVelocity.magnitude < 0.1f && _rb.angularVelocity.magnitude < 0.1f)
            {
                // Object is at rest - could release ownership here if desired
                // For now, keep ownership until someone else touches it
            }
        }
    }
}
