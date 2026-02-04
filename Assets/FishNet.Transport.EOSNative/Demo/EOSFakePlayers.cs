using System.Collections.Generic;
using UnityEngine;
using FishNet;
using FishNet.Object;
using FishNet.Managing;

namespace FishNet.Transport.EOSNative.Demo
{
    /// <summary>
    /// Spawns fake player objects for testing without ParrelSync.
    /// Fake players move randomly and can be used to test multiplayer features.
    /// Server only - add to your scene and spawn bots via the Inspector or API.
    /// </summary>
    public class EOSFakePlayers : MonoBehaviour
    {
        #region Singleton
        private static EOSFakePlayers _instance;
        public static EOSFakePlayers Instance => _instance;
        #endregion

        #region Settings
        [Header("Fake Player Settings")]
        [SerializeField]
        [Tooltip("Prefab to spawn for fake players. Should have NetworkObject.")]
        private NetworkObject _fakePlayerPrefab;

        [SerializeField]
        [Tooltip("Maximum number of fake players.")]
        [Range(1, 20)]
        private int _maxFakePlayers = 5;

        [SerializeField]
        [Tooltip("Movement speed of fake players.")]
        [Range(1f, 20f)]
        private float _moveSpeed = 5f;

        [SerializeField]
        [Tooltip("How often to change direction (seconds).")]
        [Range(0.5f, 5f)]
        private float _directionChangeInterval = 2f;

        [SerializeField]
        [Tooltip("Movement bounds radius from spawn point.")]
        [Range(5f, 50f)]
        private float _movementRadius = 10f;

        [SerializeField]
        [Tooltip("Spawn point for fake players.")]
        private Transform _spawnPoint;
        #endregion

        #region Public Properties
        public int FakePlayerCount => _fakePlayers.Count;
        public int MaxFakePlayers => _maxFakePlayers;
        public IReadOnlyList<FakePlayerData> FakePlayers => _fakePlayers;
        #endregion

        #region Private Fields
        private List<FakePlayerData> _fakePlayers = new List<FakePlayerData>();
        private NetworkManager _networkManager;
        private Vector3 _spawnCenter;

        private static readonly string[] _fakeNames = {
            "TestBot_Alpha", "TestBot_Beta", "TestBot_Gamma", "TestBot_Delta",
            "TestBot_Echo", "TestBot_Foxtrot", "TestBot_Golf", "TestBot_Hotel",
            "Bot_Steve", "Bot_Alex", "Bot_Sam", "Bot_Max",
            "FakePlayer_1", "FakePlayer_2", "FakePlayer_3", "FakePlayer_4",
            "DummyUser_A", "DummyUser_B", "DummyUser_C", "DummyUser_D"
        };
        #endregion

        #region Data Classes
        public class FakePlayerData
        {
            public string Name;
            public NetworkObject NetworkObject;
            public Vector3 TargetPosition;
            public float DirectionChangeTime;
            public Color Color;
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            _networkManager = InstanceFinder.NetworkManager;
            _spawnCenter = _spawnPoint != null ? _spawnPoint.position : Vector3.zero;
        }

        private void Update()
        {
            if (_networkManager == null || !_networkManager.IsServerStarted) return;

            // Update fake player movement
            foreach (var fakePlayer in _fakePlayers)
            {
                if (fakePlayer.NetworkObject == null) continue;

                var transform = fakePlayer.NetworkObject.transform;

                // Change direction periodically
                if (Time.time >= fakePlayer.DirectionChangeTime)
                {
                    fakePlayer.TargetPosition = GetRandomPosition();
                    fakePlayer.DirectionChangeTime = Time.time + _directionChangeInterval + Random.Range(-0.5f, 0.5f);
                }

                // Move towards target
                Vector3 direction = (fakePlayer.TargetPosition - transform.position).normalized;
                if (Vector3.Distance(transform.position, fakePlayer.TargetPosition) > 0.5f)
                {
                    transform.position += direction * _moveSpeed * Time.deltaTime;

                    // Face movement direction
                    if (direction != Vector3.zero)
                    {
                        transform.forward = Vector3.Lerp(transform.forward, direction, Time.deltaTime * 5f);
                    }
                }
            }
        }

        private void OnDestroy()
        {
            DespawnAllFakePlayers();
            if (_instance == this)
                _instance = null;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Spawn a fake player.
        /// </summary>
        public FakePlayerData SpawnFakePlayer(string name = null)
        {
            if (!_networkManager.IsServerStarted)
            {
                Debug.LogWarning("[EOSFakePlayers] Can only spawn fake players on server.");
                return null;
            }

            if (_fakePlayers.Count >= _maxFakePlayers)
            {
                Debug.LogWarning($"[EOSFakePlayers] Max fake players ({_maxFakePlayers}) reached.");
                return null;
            }

            if (_fakePlayerPrefab == null)
            {
                Debug.LogError("[EOSFakePlayers] No fake player prefab assigned!");
                return null;
            }

            // Generate name
            name ??= _fakeNames[_fakePlayers.Count % _fakeNames.Length];

            // Spawn position
            Vector3 spawnPos = GetRandomPosition();

            // Spawn network object
            var spawned = Instantiate(_fakePlayerPrefab, spawnPos, Quaternion.identity);
            _networkManager.ServerManager.Spawn(spawned);

            var fakePlayer = new FakePlayerData
            {
                Name = name,
                NetworkObject = spawned,
                TargetPosition = GetRandomPosition(),
                DirectionChangeTime = Time.time + _directionChangeInterval,
                Color = EOSPlayerRegistry.PlayerColors[_fakePlayers.Count % 12]
            };

            _fakePlayers.Add(fakePlayer);

            EOSToastManager.Info("Fake Player", $"Spawned {name}");
            return fakePlayer;
        }

        /// <summary>
        /// Spawn multiple fake players.
        /// </summary>
        public void SpawnFakePlayers(int count)
        {
            for (int i = 0; i < count && _fakePlayers.Count < _maxFakePlayers; i++)
            {
                SpawnFakePlayer();
            }
        }

        /// <summary>
        /// Despawn a specific fake player.
        /// </summary>
        public void DespawnFakePlayer(FakePlayerData fakePlayer)
        {
            if (fakePlayer == null) return;

            if (fakePlayer.NetworkObject != null && _networkManager.IsServerStarted)
            {
                _networkManager.ServerManager.Despawn(fakePlayer.NetworkObject);
            }

            _fakePlayers.Remove(fakePlayer);
        }

        /// <summary>
        /// Despawn all fake players.
        /// </summary>
        public void DespawnAllFakePlayers()
        {
            for (int i = _fakePlayers.Count - 1; i >= 0; i--)
            {
                DespawnFakePlayer(_fakePlayers[i]);
            }
        }

        /// <summary>
        /// Get a random position within the movement bounds.
        /// </summary>
        private Vector3 GetRandomPosition()
        {
            Vector2 randomCircle = Random.insideUnitCircle * _movementRadius;
            return _spawnCenter + new Vector3(randomCircle.x, 0, randomCircle.y);
        }
        #endregion

        #region Editor UI
#if UNITY_EDITOR
        [UnityEditor.CustomEditor(typeof(EOSFakePlayers))]
        public class EOSFakePlayersEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();

                var fakePlayers = (EOSFakePlayers)target;

                UnityEditor.EditorGUILayout.Space(10);
                UnityEditor.EditorGUILayout.LabelField("Runtime Controls", UnityEditor.EditorStyles.boldLabel);

                if (!Application.isPlaying)
                {
                    UnityEditor.EditorGUILayout.HelpBox("Enter Play Mode to spawn fake players.", UnityEditor.MessageType.Info);
                    return;
                }

                var nm = InstanceFinder.NetworkManager;
                if (nm == null || !nm.IsServerStarted)
                {
                    UnityEditor.EditorGUILayout.HelpBox("Start server to spawn fake players.", UnityEditor.MessageType.Warning);
                    return;
                }

                UnityEditor.EditorGUILayout.LabelField($"Active: {fakePlayers.FakePlayerCount}/{fakePlayers.MaxFakePlayers}");

                UnityEditor.EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Spawn 1"))
                    fakePlayers.SpawnFakePlayer();
                if (GUILayout.Button("Spawn 5"))
                    fakePlayers.SpawnFakePlayers(5);
                if (GUILayout.Button("Despawn All"))
                    fakePlayers.DespawnAllFakePlayers();
                UnityEditor.EditorGUILayout.EndHorizontal();

                // List active fake players
                if (fakePlayers.FakePlayerCount > 0)
                {
                    UnityEditor.EditorGUILayout.Space(5);
                    UnityEditor.EditorGUILayout.LabelField("Active Fake Players:");
                    foreach (var fp in fakePlayers.FakePlayers)
                    {
                        UnityEditor.EditorGUILayout.LabelField($"  - {fp.Name}");
                    }
                }
            }
        }
#endif
        #endregion
    }
}
