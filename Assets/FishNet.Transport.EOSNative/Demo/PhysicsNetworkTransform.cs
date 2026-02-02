using System;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Spring-based physics network transform that allows objects to interact
    /// with local physics while staying synced across the network.
    ///
    /// Unlike ownership-based approaches, this uses damped spring forces to guide
    /// the rigidbody toward the networked position, allowing natural physics interactions.
    ///
    /// Credit: DrewMileham (original method), Skylar/CometDev (https://github.com/SkylarSDev) (Mirror implementation)
    /// Ported to FishNet for FishNet-EOS-Native
    /// </summary>
    public enum PhysicsNetworkTransformMode { Physics, VisualTweened, Visual }

    /// <summary>
    /// Half-precision Vector3 for bandwidth-efficient position sync (6 bytes vs 12).
    /// </summary>
    public struct Vector3Half
    {
        public ushort x, y, z;

        public Vector3Half(Vector3 v)
        {
            x = Mathf.FloatToHalf(v.x);
            y = Mathf.FloatToHalf(v.y);
            z = Mathf.FloatToHalf(v.z);
        }

        public Vector3 ToVector3()
        {
            return new Vector3(
                Mathf.HalfToFloat(x),
                Mathf.HalfToFloat(y),
                Mathf.HalfToFloat(z)
            );
        }
    }

    public class PhysicsNetworkTransform : NetworkBehaviour
    {
        public Rigidbody rigidbodyToTrack;

        [Header("Networking Settings")]
        [Range(0, 1)] public float updateRate = 0;
        private float updateCounter = 0;

        [Tooltip("Minimum position change before sending update")]
        public float positionThreshold = 0.001f;
        [Tooltip("Minimum rotation change (degrees) before sending update")]
        public float rotationThreshold = 0.1f;

        private Vector3 lastSentPosition;
        private Quaternion lastSentRotation;

        [Header("Tracking Settings")]
        public PhysicsNetworkTransformMode mode;
        public float tweenConstant = 10f;

        [Tooltip("For non-authority instances, use this mode instead (e.g., VisualTweened for players)")]
        public PhysicsNetworkTransformMode nonAuthorityMode = PhysicsNetworkTransformMode.Physics;
        [Tooltip("If true, non-authority uses nonAuthorityMode instead of regular mode")]
        public bool useNonAuthorityMode = false;

        [Header("Distance-Based Mode Switching")]
        [Tooltip("Enable automatic mode switching based on distance from main camera")]
        public bool useDistanceBasedMode = false;
        [Tooltip("Distance below which Physics mode is used")]
        public float physicsDistance = 5f;
        [Tooltip("Distance below which VisualTweened mode is used (above physicsDistance)")]
        public float tweenedDistance = 15f;

        private Camera mainCamera;
        private PhysicsNetworkTransformMode baseMode;

        /// <summary>
        /// Returns true if this instance has authority over the physics.
        /// True for: owner client, OR server for server-owned objects.
        /// Hides base NetworkBehaviour.HasAuthority to include server-owned object handling.
        /// </summary>
        private new bool HasAuthority => IsOwner || (IsServerInitialized && (Owner == null || !Owner.IsActive));

        [Header("Physics Spring Settings (EDIT CAREFULLY)")]
        public float positionSpringFrequency = 4f;
        public float positionDampingRatio = 0.65f;
        public float rotationSpringFrequency = 5.5f;
        public float rotationDampingRatio = 0.55f;

        [Header("Ownership Settings")]
        [Tooltip("Claim ownership when a player collides with this object")]
        public bool claimOwnershipOnCollision = true;
        [Tooltip("Minimum collision force required to claim ownership")]
        public float minCollisionForce = 0.5f;
        [Tooltip("Cooldown between ownership changes")]
        public float ownershipCooldown = 0.25f;
        private float lastOwnershipChangeTime;

        [Header("Debug")]
        public bool updatePositionLocally = false;
        public bool useExternalPosition = false;
        public Vector3 receivedPosition;
        public Quaternion receivedRotation;

        private void Awake()
        {
            // Auto-assign rigidbody if not set
            if (rigidbodyToTrack == null)
            {
                rigidbodyToTrack = GetComponent<Rigidbody>();
            }
        }

        private void Start()
        {
            mainCamera = Camera.main;
            baseMode = mode;

            if (rigidbodyToTrack != null)
            {
                receivedPosition = rigidbodyToTrack.position;
                receivedRotation = rigidbodyToTrack.rotation;
            }
            else
            {
                EOSDebugLogger.LogError("PhysicsNetworkTransform", $" No Rigidbody found on {gameObject.name}!");
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // If no owner assigned, give to server
            if (Owner == null || !Owner.IsValid)
            {
                // Server owns unowned physics objects
            }
        }

        private void FixedUpdate()
        {
            if (rigidbodyToTrack == null) return;

            // Don't run network logic if not properly initialized
            if (!IsSpawned) return;

            // Check that we can run network updates (prevents errors during host migration)
            if (!CanRunNetworkUpdates()) return;

            if (HasAuthority)
            {
                // We are the authority - run physics locally and send updates
                if (updatePositionLocally)
                {
                    UpdateModeBasedOnDistance();
                    UpdateTracking();
                }

                if (updateCounter > updateRate)
                {
                    if (useExternalPosition)
                    {
                        BroadcastPosition(CompressPosition(receivedPosition), CompressRotation(receivedRotation));
                    }
                    else
                    {
                        Vector3 currentPos = rigidbodyToTrack.position;
                        Quaternion currentRot = rigidbodyToTrack.rotation;

                        bool positionChanged = Vector3.SqrMagnitude(currentPos - lastSentPosition) > positionThreshold * positionThreshold;
                        bool rotationChanged = Quaternion.Angle(currentRot, lastSentRotation) > rotationThreshold;

                        if (positionChanged || rotationChanged)
                        {
                            BroadcastPosition(CompressPosition(currentPos), CompressRotation(currentRot));
                            lastSentPosition = currentPos;
                            lastSentRotation = currentRot;
                        }
                    }
                    updateCounter = 0;
                }
                updateCounter += Time.fixedDeltaTime;
            }
            else
            {
                // We are not authority - apply spring to follow received position
                UpdateModeBasedOnDistance();
                UpdateTracking();
            }
        }

        /// <summary>
        /// Check if we can run network updates. This prevents errors during host migration
        /// when the network state is transitioning.
        /// </summary>
        private bool CanRunNetworkUpdates()
        {
            // Must have at least one active connection at the global level
            if (!InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted) return false;

            // Object must be initialized for an active connection type
            if (InstanceFinder.IsServerStarted && IsServerInitialized) return true;
            if (InstanceFinder.IsClientStarted && IsClientInitialized) return true;

            return false;
        }

        /// <summary>
        /// Send position to all non-authority clients.
        /// Server broadcasts directly, clients go through ServerRpc.
        /// </summary>
        private void BroadcastPosition(Vector3Half position, uint rotation)
        {
            // If we're the server and server-initialized, broadcast directly
            // This handles server-owned objects (like crates with no player owner)
            if (IsServerInitialized && InstanceFinder.IsServerStarted)
            {
                ReceiveDataObserversRpc(position, rotation);
            }
            // If we're a proper client, send through ServerRpc
            else if (IsClientInitialized && InstanceFinder.IsClientStarted)
            {
                SendDataServerRpc(position, rotation);
            }
            // else: in transition state - skip this update
        }

        #region Compression

        private Vector3Half CompressPosition(Vector3 pos)
        {
            return new Vector3Half(pos);
        }

        private Vector3 DecompressPosition(Vector3Half compressed)
        {
            return compressed.ToVector3();
        }

        /// <summary>
        /// Smallest-three quaternion compression to 32 bits.
        /// </summary>
        private uint CompressRotation(Quaternion rot)
        {
            float absX = Mathf.Abs(rot.x);
            float absY = Mathf.Abs(rot.y);
            float absZ = Mathf.Abs(rot.z);
            float absW = Mathf.Abs(rot.w);

            int largestIndex = 0;
            float largestValue = absX;

            if (absY > largestValue) { largestIndex = 1; largestValue = absY; }
            if (absZ > largestValue) { largestIndex = 2; largestValue = absZ; }
            if (absW > largestValue) { largestIndex = 3; }

            float a, b, c;
            switch (largestIndex)
            {
                case 0: a = rot.y; b = rot.z; c = rot.w; if (rot.x < 0) { a = -a; b = -b; c = -c; } break;
                case 1: a = rot.x; b = rot.z; c = rot.w; if (rot.y < 0) { a = -a; b = -b; c = -c; } break;
                case 2: a = rot.x; b = rot.y; c = rot.w; if (rot.z < 0) { a = -a; b = -b; c = -c; } break;
                default: a = rot.x; b = rot.y; c = rot.z; if (rot.w < 0) { a = -a; b = -b; c = -c; } break;
            }

            const float scale = 1023f / 1.41421356f;
            uint ua = (uint)Mathf.Clamp((int)((a + 0.707106781f) * scale), 0, 1023);
            uint ub = (uint)Mathf.Clamp((int)((b + 0.707106781f) * scale), 0, 1023);
            uint uc = (uint)Mathf.Clamp((int)((c + 0.707106781f) * scale), 0, 1023);

            return ((uint)largestIndex << 30) | (ua << 20) | (ub << 10) | uc;
        }

        private Quaternion DecompressRotation(uint compressed)
        {
            int largestIndex = (int)(compressed >> 30);
            const float scale = 1.41421356f / 1023f;

            float a = ((compressed >> 20) & 1023) * scale - 0.707106781f;
            float b = ((compressed >> 10) & 1023) * scale - 0.707106781f;
            float c = (compressed & 1023) * scale - 0.707106781f;

            float largest = Mathf.Sqrt(1f - a * a - b * b - c * c);

            switch (largestIndex)
            {
                case 0: return new Quaternion(largest, a, b, c);
                case 1: return new Quaternion(a, largest, b, c);
                case 2: return new Quaternion(a, b, largest, c);
                default: return new Quaternion(a, b, c, largest);
            }
        }

        #endregion

        #region Network RPCs

        [ServerRpc(RequireOwnership = false)]
        private void SendDataServerRpc(Vector3Half position, uint rotation)
        {
            // Server received data, broadcast to all clients (including sender for host-as-client)
            ReceiveDataObserversRpc(position, rotation);
        }

        [ObserversRpc(BufferLast = true, ExcludeServer = false)]
        private void ReceiveDataObserversRpc(Vector3Half position, uint rotation)
        {
            // Don't apply to authority - they're the source of truth
            if (HasAuthority) return;

            receivedPosition = DecompressPosition(position);
            receivedRotation = DecompressRotation(rotation);
        }

        #endregion

        #region Mode & Tracking

        private void UpdateModeBasedOnDistance()
        {
            if (!useDistanceBasedMode || mainCamera == null)
            {
                return;
            }

            float sqrDistance = (mainCamera.transform.position - rigidbodyToTrack.position).sqrMagnitude;

            if (sqrDistance < physicsDistance * physicsDistance)
            {
                mode = PhysicsNetworkTransformMode.Physics;
            }
            else if (sqrDistance < tweenedDistance * tweenedDistance)
            {
                mode = PhysicsNetworkTransformMode.VisualTweened;
            }
            else
            {
                mode = PhysicsNetworkTransformMode.Visual;
            }
        }

        private void UpdateTracking()
        {
            // Use nonAuthorityMode if configured and we're not authority
            var activeMode = (useNonAuthorityMode && !HasAuthority) ? nonAuthorityMode : mode;

            switch (activeMode)
            {
                case PhysicsNetworkTransformMode.Physics:
                    ApplyPhysicsSpring();
                    break;
                case PhysicsNetworkTransformMode.VisualTweened:
                    ApplyVisualTweened();
                    break;
                case PhysicsNetworkTransformMode.Visual:
                    ApplyVisualSnap();
                    break;
            }
        }

        /// <summary>
        /// Apply damped spring forces to guide rigidbody toward target.
        /// Mass-aware: works correctly for both heavy and light objects.
        /// </summary>
        private void ApplyPhysicsSpring()
        {
            rigidbodyToTrack.isKinematic = false;

            // Position spring (mass-aware)
            float mass = rigidbodyToTrack.mass;
            float omega = 2f * Mathf.PI * positionSpringFrequency;
            float positionSpring = mass * omega * omega;
            float positionDamper = 2f * mass * positionDampingRatio * omega;

            Vector3 positionError = receivedPosition - rigidbodyToTrack.position;
            Vector3 velocityError = -rigidbodyToTrack.linearVelocity;
            Vector3 springForce = positionError * positionSpring + velocityError * positionDamper;

            // Skip invalid forces (object fell off map, NaN, etc.)
            if (!IsValidVector(springForce))
                return;

            rigidbodyToTrack.AddForce(springForce, ForceMode.Force);

            // Rotation spring (inertia-aware)
            float inertia = rigidbodyToTrack.inertiaTensor.magnitude;
            float rotOmega = 2f * Mathf.PI * rotationSpringFrequency;
            float rotationSpring = inertia * rotOmega * rotOmega;
            float rotationDamper = 2f * inertia * rotationDampingRatio * rotOmega;

            Quaternion rotationDelta = receivedRotation * Quaternion.Inverse(rigidbodyToTrack.rotation);
            if (rotationDelta.w < 0f)
            {
                rotationDelta = new Quaternion(-rotationDelta.x, -rotationDelta.y, -rotationDelta.z, -rotationDelta.w);
            }
            rotationDelta.ToAngleAxis(out float angle, out Vector3 axis);

            Vector3 angularError;
            if (angle < 0.001f || float.IsNaN(axis.x))
            {
                angularError = Vector3.zero;
            }
            else
            {
                angularError = axis.normalized * (angle * Mathf.Deg2Rad);
            }

            Vector3 angularVelocityError = -rigidbodyToTrack.angularVelocity;
            Vector3 springTorque = angularError * rotationSpring + angularVelocityError * rotationDamper;

            // Skip invalid torques
            if (!IsValidVector(springTorque))
                return;

            rigidbodyToTrack.AddTorque(springTorque, ForceMode.Force);
        }

        /// <summary>
        /// Returns true if vector contains no NaN or Infinity values.
        /// </summary>
        private static bool IsValidVector(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        /// <summary>
        /// Smooth lerp toward target (kinematic).
        /// </summary>
        private void ApplyVisualTweened()
        {
            rigidbodyToTrack.isKinematic = true;
            rigidbodyToTrack.MovePosition(Vector3.Lerp(rigidbodyToTrack.position, receivedPosition, Time.deltaTime * tweenConstant));
            rigidbodyToTrack.MoveRotation(Quaternion.Lerp(rigidbodyToTrack.rotation, receivedRotation, Time.deltaTime * tweenConstant));
        }

        /// <summary>
        /// Snap to target (kinematic, cheapest).
        /// </summary>
        private void ApplyVisualSnap()
        {
            rigidbodyToTrack.isKinematic = true;
            rigidbodyToTrack.MovePosition(receivedPosition);
            rigidbodyToTrack.MoveRotation(receivedRotation);
        }

        #endregion

        #region Ownership

        private void OnCollisionEnter(Collision collision)
        {
            if (!claimOwnershipOnCollision) return;
            if (Time.time - lastOwnershipChangeTime < ownershipCooldown) return;

            // Check if collision is strong enough
            if (collision.relativeVelocity.magnitude < minCollisionForce) return;

            // Check if we hit a player (has NetworkObject that is owned by local client)
            var playerNob = collision.gameObject.GetComponentInParent<NetworkObject>();
            if (playerNob == null || !playerNob.IsOwner) return;

            // Already have authority
            if (HasAuthority) return;

            // Request ownership from server
            ClaimOwnershipServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ClaimOwnershipServerRpc(FishNet.Connection.NetworkConnection sender = null)
        {
            if (sender == null) return;
            if (Time.time - lastOwnershipChangeTime < ownershipCooldown) return;

            // Give ownership to the requesting client
            GiveOwnership(sender);
            lastOwnershipChangeTime = Time.time;

            // Sync the cooldown time to clients
            SyncOwnershipChangeTimeObserversRpc(lastOwnershipChangeTime);
        }

        [ObserversRpc]
        private void SyncOwnershipChangeTimeObserversRpc(float time)
        {
            lastOwnershipChangeTime = time;
        }

        #endregion

        #region Debug

        private void OnDrawGizmos()
        {
            if (rigidbodyToTrack)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(receivedPosition, 0.1f);
                Gizmos.DrawLine(rigidbodyToTrack.position, receivedPosition);
            }
        }

        #endregion
    }
}
