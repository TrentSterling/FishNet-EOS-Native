using System;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Spawns player prefab when clients connect.
    /// Add to same GameObject as NetworkManager or anywhere in scene.
    /// </summary>
    [Obsolete("Use HostMigrationPlayerSpawner instead - it provides the same functionality plus host migration support.")]
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("Spawning")]
        [SerializeField]
        [Tooltip("The player prefab with NetworkObject, NetworkTransform, and PlayerBall.")]
        private NetworkObject _playerPrefab;

        [SerializeField]
        [Tooltip("Spawn radius around origin.")]
        private float _spawnRadius = 3f;

        private NetworkManager _networkManager;

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();
            if (_networkManager == null)
                _networkManager = FindAnyObjectByType<NetworkManager>();
        }

        private void OnEnable()
        {
            if (_networkManager != null)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
                _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            }
        }

        private void OnDisable()
        {
            if (_networkManager != null)
            {
                _networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
        }

        /// <summary>
        /// Called when a client finishes loading start scenes.
        /// This is the safe time to spawn their player.
        /// </summary>
        private void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
        {
            if (!asServer) return;
            if (_playerPrefab == null) return;

            // Spawn at random position in circle
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * _spawnRadius;
            Vector3 spawnPos = new Vector3(randomCircle.x, 1f, randomCircle.y);

            // Instantiate then spawn with ownership
            NetworkObject nob = _networkManager.GetPooledInstantiated(_playerPrefab, spawnPos, Quaternion.identity, true);
            _networkManager.ServerManager.Spawn(nob, conn);

            Debug.Log($"[PlayerSpawner] Spawned player for connection {conn.ClientId} at {spawnPos}");
        }

        /// <summary>
        /// Handle disconnections - despawn is automatic with FishNet.
        /// </summary>
        private void OnRemoteConnectionState(NetworkConnection conn, FishNet.Transporting.RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == FishNet.Transporting.RemoteConnectionState.Stopped)
            {
                Debug.Log($"[PlayerSpawner] Client {conn.ClientId} disconnected");
            }
        }
    }
}
