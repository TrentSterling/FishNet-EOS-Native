using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Migration
{
    /// <summary>
    /// Spawns players and handles repossession of migrated objects.
    /// Attach to the same GameObject as NetworkManager.
    /// </summary>
    public class HostMigrationPlayerSpawner : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Player Prefab")]
        [Tooltip("The player prefab to spawn. Must have HostMigratable component if migration support is needed.")]
        [SerializeField]
        private NetworkObject _playerPrefab;

        /// <summary>
        /// The player prefab used for spawning.
        /// </summary>
        public NetworkObject PlayerPrefab => _playerPrefab;

        [Tooltip("Add spawned player to default scene.")]
        [SerializeField]
        private bool _addToDefaultScene = true;

        [Header("Spawn Settings")]
        [SerializeField]
        private Vector3 _spawnPosition = Vector3.zero;

        [SerializeField]
        private bool _useRandomSpawnOffset = true;

        [SerializeField]
        private float _randomSpawnRadius = 5f;

        [Header("Debug")]
        [SerializeField]
        private bool _enableDebugLogs = true;

        #endregion

        #region Events

        /// <summary>
        /// Fired when a player is spawned.
        /// </summary>
        public event Action<NetworkObject> OnPlayerSpawned;

        /// <summary>
        /// Fired when objects are repossessed to a reconnecting player.
        /// </summary>
        public event Action<NetworkConnection, List<HostMigratable>> OnObjectsRepossessed;

        #endregion

        #region Private Fields

        private NetworkManager _networkManager;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            _networkManager = InstanceFinder.NetworkManager;
            if (_networkManager == null)
            {
                Debug.LogWarning("[HostMigrationPlayerSpawner] NetworkManager not found.");
                return;
            }

            _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            }
        }

        #endregion

        #region Spawn Logic

        private void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer) return;

            // Get the owner's PUID from the connection address
            string ownerPuid = conn.GetAddress();

            // Client host (connection 32767) doesn't have a network address
            // Use the local PUID instead since client host is the server
            if (string.IsNullOrEmpty(ownerPuid) && conn.IsLocalClient)
            {
                ownerPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
                Log($"Client host detected, using local PUID: {ownerPuid?.Substring(0, 8)}...");
            }

            // If still no PUID (first-time host before EOS login), spawn new player
            if (string.IsNullOrEmpty(ownerPuid))
            {
                SpawnNewPlayer(conn);
                return;
            }

            // Check for migrated objects waiting for this owner
            if (HostMigratable.PendingRepossessions.TryGetValue(ownerPuid, out var migratedObjects))
            {
                Log($"Repossessing {migratedObjects.Count} objects for {ownerPuid.Substring(0, 8)}...");

                foreach (var migratable in migratedObjects)
                {
                    if (migratable == null) continue;

                    // Activate and give ownership
                    migratable.gameObject.SetActive(true);
                    migratable.Repossess(conn);

                    // Add to default scene
                    if (_addToDefaultScene && migratable.NetworkObject != null)
                    {
                        InstanceFinder.SceneManager.AddOwnerToDefaultScene(migratable.NetworkObject);
                    }
                }

                // Fire event
                OnObjectsRepossessed?.Invoke(conn, migratedObjects);

                // Remove from pending
                HostMigratable.PendingRepossessions.Remove(ownerPuid);

                // Don't spawn a new player - they got their old objects back
                return;
            }

            // No migrated objects - spawn fresh player
            SpawnNewPlayer(conn);
        }

        private void SpawnNewPlayer(NetworkConnection conn)
        {
            if (_playerPrefab == null)
            {
                Debug.LogWarning($"[HostMigrationPlayerSpawner] No player prefab set.");
                return;
            }

            // Calculate spawn position
            Vector3 position = _spawnPosition;
            Quaternion rotation = _playerPrefab.transform.rotation;

            if (_useRandomSpawnOffset)
            {
                var randomOffset = UnityEngine.Random.insideUnitCircle * _randomSpawnRadius;
                position += new Vector3(randomOffset.x, 0, randomOffset.y);
            }

            // Spawn using pooling if available
            NetworkObject nob = _networkManager.GetPooledInstantiated(_playerPrefab, position, rotation, true);
            _networkManager.ServerManager.Spawn(nob, conn);

            if (_addToDefaultScene)
            {
                _networkManager.SceneManager.AddOwnerToDefaultScene(nob);
            }

            Log($"Spawned player for connection {conn.ClientId}");
            OnPlayerSpawned?.Invoke(nob);
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (!_enableDebugLogs) return;
            EOSDebugLogger.Log(DebugCategory.HostMigrationPlayerSpawner, "HostMigrationPlayerSpawner", message);
        }

        #endregion
    }
}
