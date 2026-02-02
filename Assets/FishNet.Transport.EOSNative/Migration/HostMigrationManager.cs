using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Lobbies;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative.Migration
{
    /// <summary>
    /// Manages host migration for FishNet over EOS.
    /// Tracks migratable objects and handles state save/restore during migration.
    /// </summary>
    public class HostMigrationManager : MonoBehaviour
    {
        #region Singleton

        private static HostMigrationManager _instance;
        public static HostMigrationManager Instance => _instance;

        #endregion

        #region Events

        /// <summary>
        /// Fired when migration starts (old host left, we're promoted).
        /// </summary>
        public event Action OnMigrationStarted;

        /// <summary>
        /// Fired when migration completes (server restarted, objects restored).
        /// </summary>
        public event Action OnMigrationCompleted;

        #endregion

        #region Serialized Fields

        [Header("Prefab Collection")]
        [Tooltip("PrefabObjects collection containing all migratable prefabs.")]
        [SerializeField]
        private PrefabObjects _prefabCollection;

        [Header("Debug")]
        [SerializeField]
        private bool _enableDebugLogs = true;

        #endregion

        #region Properties

        /// <summary>
        /// Whether migration is currently in progress.
        /// </summary>
        public bool IsMigrating { get; private set; }

        /// <summary>
        /// Our local ProductUserId.
        /// </summary>
        private string LocalPuid => EOSManager.Instance?.LocalProductUserId?.ToString();

        #endregion

        #region Private Fields

        private readonly List<HostMigratable> _migratableObjects = new();
        private readonly List<MigratableObjectState> _savedStates = new();
        private readonly Dictionary<string, NetworkObject> _prefabDictionary = new();

        private EOSNativeTransport _transport;
        private EOSLobbyManager _lobbyManager;

        // Reconnect retry state (for clients during migration)
        private int _reconnectAttempt = 0;
        private const int MAX_RECONNECT_ATTEMPTS = 5;
        private static readonly float[] RECONNECT_DELAYS = { 1.5f, 2f, 3f, 4f, 5f };

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
        }

        private void Start()
        {
            _transport = FindAnyObjectByType<EOSNativeTransport>();
            _lobbyManager = EOSLobbyManager.Instance;

            // Build prefab lookup dictionary from PrefabObjects collection
            if (_prefabCollection != null)
            {
                for (int i = 0; i < _prefabCollection.GetObjectCount(); i++)
                {
                    var prefab = _prefabCollection.GetObject(false, i);
                    if (prefab != null)
                    {
                        RegisterPrefab(prefab);
                    }
                }
            }

            // Also register player prefab from HostMigrationPlayerSpawner if available
            var playerSpawner = FindAnyObjectByType<HostMigrationPlayerSpawner>();
            if (playerSpawner != null && playerSpawner.PlayerPrefab != null)
            {
                Log($"Found player spawner with prefab: {playerSpawner.PlayerPrefab.name}");
                RegisterPrefab(playerSpawner.PlayerPrefab);
            }
            else
            {
                Log($"WARNING: No HostMigrationPlayerSpawner found or no player prefab set!");
            }

            // Subscribe to lobby events
            if (_lobbyManager != null)
            {
                _lobbyManager.OnOwnerChanged += OnLobbyOwnerChanged;
            }

            // Subscribe to client connection state to save states BEFORE objects are despawned
            if (InstanceFinder.ClientManager != null)
            {
                InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
            }
        }

        /// <summary>
        /// Registers a prefab for migration support.
        /// </summary>
        public void RegisterPrefab(NetworkObject prefab)
        {
            if (prefab == null) return;
            string prefabName = prefab.gameObject.name.Replace("(Clone)", "");
            if (!_prefabDictionary.ContainsKey(prefabName))
            {
                _prefabDictionary[prefabName] = prefab;
                Log($"Registered migratable prefab: {prefabName}");
            }
        }

        private void OnDestroy()
        {
            if (_lobbyManager != null)
            {
                _lobbyManager.OnOwnerChanged -= OnLobbyOwnerChanged;
            }

            if (InstanceFinder.ClientManager != null)
            {
                InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Handles client connection state changes.
        /// Saves migratable object states BEFORE they get despawned on disconnect.
        /// </summary>
        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            // We care about Stopping state - this fires BEFORE objects are despawned
            if (args.ConnectionState == LocalConnectionState.Stopping)
            {
                Log($"Client stopping - saving {_migratableObjects.Count} migratable objects preemptively");

                // Only save if we haven't already saved (avoid double-save)
                if (_savedStates.Count == 0 && _migratableObjects.Count > 0)
                {
                    foreach (var migratable in _migratableObjects)
                    {
                        if (migratable == null) continue;

                        var state = migratable.SaveDataToStateStruct();
                        _savedStates.Add(state);
                        Log($"Pre-saved state for: {state.PrefabName} at {state.Position}");
                    }
                    Log($"Pre-saved {_savedStates.Count} object states before disconnect");
                }
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Registers a migratable object for tracking.
        /// Called automatically by HostMigratable.OnEnable.
        /// </summary>
        public void AddMigratableObject(HostMigratable migratable)
        {
            if (migratable == null) return;

            // Strip "(Clone)" suffix to match prefab dictionary keys
            string prefabName = migratable.gameObject.name.Replace("(Clone)", "");
            if (!_prefabDictionary.ContainsKey(prefabName))
            {
                // Prefab not registered for migration - this is fine, just skip tracking
                Log($"Object {migratable.gameObject.name} not in prefab dictionary (key: {prefabName})");
                return;
            }

            if (!_migratableObjects.Contains(migratable))
            {
                _migratableObjects.Add(migratable);
                Log($"Tracking migratable: {prefabName}");
            }
        }

        /// <summary>
        /// Unregisters a migratable object.
        /// Called automatically by HostMigratable.OnDisable.
        /// </summary>
        public void RemoveMigratableObject(HostMigratable migratable)
        {
            _migratableObjects.Remove(migratable);
        }

        /// <summary>
        /// Saves an individual object's state when it's being disabled.
        /// Called from HostMigratable.OnDisable to capture state before destruction.
        /// </summary>
        public void SaveObjectStateOnDisable(HostMigratable migratable)
        {
            if (migratable == null) return;

            // Use the effective PUID (cached value if SyncVar is empty)
            // By the time OnDisable runs, FishNet may have already cleared the SyncVar,
            // but the cached PUID is still valid. This matches what SaveDataToStateStruct() does.
            string effectivePuid = !string.IsNullOrEmpty(migratable.OwnerPuidSyncVar.Value)
                ? migratable.OwnerPuidSyncVar.Value
                : migratable.CachedOwnerPuid;

            // Check if we already have a saved state for this PUID
            // (avoid duplicates - pre-saved states from OnClientConnectionState should take precedence)
            bool alreadySaved = false;
            foreach (var state in _savedStates)
            {
                if (state.OwnerPuid == effectivePuid && state.PrefabName == migratable.gameObject.name)
                {
                    alreadySaved = true;
                    break;
                }
            }

            if (alreadySaved)
            {
                Log($"Skipping duplicate save for: {migratable.gameObject.name} (already pre-saved)");
                return;
            }

            var newState = migratable.SaveDataToStateStruct();
            _savedStates.Add(newState);
            Log($"Auto-saved state on disable for: {newState.PrefabName} (owner: {effectivePuid?.Substring(0, Math.Min(8, effectivePuid?.Length ?? 0))}...) at {newState.Position}");
        }

        /// <summary>
        /// Manually trigger migration save (for testing).
        /// </summary>
        public void DebugTriggerSave()
        {
            MigrationDetectedSaveNow();
        }

        /// <summary>
        /// Manually trigger migration finish (for testing).
        /// Simulates becoming the new host.
        /// </summary>
        public void DebugTriggerFinish()
        {
            BeginMigrationSequenceAsHost();
        }

        /// <summary>
        /// Clears all migration state. Call when intentionally leaving a lobby
        /// to prevent stale data from affecting future sessions.
        /// </summary>
        public void ClearMigrationState()
        {
            _savedStates.Clear();
            _migratableObjects.Clear();
            HostMigratable.ClearPendingRepossessions();
            IsMigrating = false;
            Log("Migration state cleared");
        }

        #endregion

        #region Migration Flow

        private void OnLobbyOwnerChanged(string newOwnerPuid)
        {
            bool weAreNewHost = LocalPuid == newOwnerPuid;

            Log($"Owner changed to {newOwnerPuid?.Substring(0, 8)}... (we are new host: {weAreNewHost})");

            // Update transport target for reconnection (both host and clients need this)
            if (_transport != null)
            {
                _transport.RemoteProductUserId = newOwnerPuid;
            }

            if (weAreNewHost)
            {
                // We're the new host - save state and begin migration as host
                MigrationDetectedSaveNow();
                BeginMigrationSequenceAsHost();
            }
            else
            {
                // We're a client - need to reconnect to the new host
                BeginMigrationSequenceAsClient();
            }
        }

        /// <summary>
        /// Save all migratable object states.
        /// </summary>
        private void MigrationDetectedSaveNow()
        {
            // Check if states were already pre-saved (by OnDisable auto-save or OnClientConnectionState)
            if (_savedStates.Count > 0)
            {
                Log($"Using {_savedStates.Count} pre-saved states (auto-saved on object disable)");
                OnMigrationStarted?.Invoke();
                return;
            }

            Log($"Saving migratable object states... (tracking {_migratableObjects.Count} objects, {_prefabDictionary.Count} prefabs registered)");

            // Log registered prefabs for debugging
            foreach (var kvp in _prefabDictionary)
            {
                Log($"Registered prefab: {kvp.Key}");
            }

            foreach (var migratable in _migratableObjects)
            {
                if (migratable == null) continue;

                var state = migratable.SaveDataToStateStruct();
                _savedStates.Add(state);
                Log($"Saved state for: {state.PrefabName} at {state.Position}");
            }

            Log($"Saved {_savedStates.Count} object states");
            OnMigrationStarted?.Invoke();
        }

        /// <summary>
        /// Begin the migration sequence AS THE NEW HOST: stop client -> stop server -> start server -> restore -> start client.
        /// </summary>
        private void BeginMigrationSequenceAsHost()
        {
            if (IsMigrating)
            {
                Log("Migration already in progress");
                return;
            }

            IsMigrating = true;
            Log("Beginning migration sequence as NEW HOST...");

            // Step 1: Stop client first
            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.OnClientConnectionState += OnHostClientStopped;
                InstanceFinder.ClientManager.StopConnection();
            }
            else
            {
                // Client wasn't running, skip to stopping server
                StopOldServer();
            }
        }

        private void OnHostClientStopped(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;

            InstanceFinder.ClientManager.OnClientConnectionState -= OnHostClientStopped;
            Log("Host: Client stopped");

            StopOldServer();
        }

        private void StopOldServer()
        {
            // Step 2: Stop server if we were running one (unlikely but handle it)
            if (InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.OnServerConnectionState += OnHostServerStopped;
                InstanceFinder.ServerManager.StopConnection(true);
            }
            else
            {
                // Server wasn't running - start fresh
                StartNewServer();
            }
        }

        private void OnHostServerStopped(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;

            InstanceFinder.ServerManager.OnServerConnectionState -= OnHostServerStopped;
            Log("Host: Old server stopped");

            StartNewServer();
        }

        private void StartNewServer()
        {
            // Reset scene NetworkObjects that still have ObjectId set from client session.
            // When host crashes, client disconnects but scene objects retain their ObjectId.
            // ResetState clears ObjectId to UNSET_OBJECTID_VALUE so server can reinitialize.
            ResetSceneNetworkObjects();

            // Start server as new host
            Log("Host: Starting server...");
            InstanceFinder.ServerManager.OnServerConnectionState += OnHostServerStarted;
            InstanceFinder.ServerManager.StartConnection();
        }

        /// <summary>
        /// Reset scene NetworkObjects so they can be re-initialized by the new server.
        /// </summary>
        private void ResetSceneNetworkObjects()
        {
            // IMPORTANT: Include inactive objects - scene objects get deactivated when client disconnects
            var sceneObjects = FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationManager", $"ResetSceneNetworkObjects: Found {sceneObjects.Length} NetworkObjects (including inactive)");

            int resetCount = 0;
            int sceneObjectCount = 0;

            foreach (var nob in sceneObjects)
            {
                // Only reset scene objects (not spawned prefabs)
                if (!nob.IsSceneObject) continue;
                sceneObjectCount++;

                EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationManager", $"Scene object {nob.name}: ObjectId={nob.ObjectId}, UNSET={NetworkObject.UNSET_OBJECTID_VALUE}, active={nob.gameObject.activeSelf}");

                // Check if ObjectId is still set (not UNSET) - indicates leftover state
                // UNSET_OBJECTID_VALUE = ushort.MaxValue (65535)
                if (nob.ObjectId == NetworkObject.UNSET_OBJECTID_VALUE)
                {
                    EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationManager", $"Skipping {nob.name}: ObjectId already UNSET ({nob.ObjectId})");
                    continue;
                }

                try
                {
                    int oldObjectId = nob.ObjectId;
                    // ResetState clears ObjectId at the end of the method
                    nob.ResetState(asServer: false);
                    EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationManager", $"Reset {nob.name}: ObjectId was {oldObjectId}, now is {nob.ObjectId}");
                    resetCount++;
                }
                catch (System.Exception e)
                {
                    EOSDebugLogger.LogError("HostMigrationManager", $"EXCEPTION resetting {nob.name}: {e}");
                }
            }

            EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationManager", $"Host: Found {sceneObjectCount} scene objects, reset {resetCount}");
        }

        private void OnHostServerStarted(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started) return;

            InstanceFinder.ServerManager.OnServerConnectionState -= OnHostServerStarted;
            Log("Host: Server started");

            // Step 4: Restore migratable objects
            MigrationFinish();

            // Step 5: Start client (clienthost)
            InstanceFinder.ClientManager.StartConnection();

            // Rebuild observers
            InstanceFinder.ServerManager.Objects.RebuildObservers();

            IsMigrating = false;
            Log("Host: Migration complete!");
            OnMigrationCompleted?.Invoke();
        }

        /// <summary>
        /// Begin the migration sequence AS A CLIENT: stop client -> wait -> reconnect.
        /// </summary>
        private void BeginMigrationSequenceAsClient()
        {
            if (IsMigrating)
            {
                Log("Migration already in progress");
                return;
            }

            IsMigrating = true;
            Log("Beginning migration sequence as CLIENT...");
            OnMigrationStarted?.Invoke();

            // Step 1: Stop client connection
            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.OnClientConnectionState += OnClientClientStopped;
                InstanceFinder.ClientManager.StopConnection();
            }
            else
            {
                // Client wasn't running - just try to connect
                ScheduleReconnect();
            }
        }

        private void OnClientClientStopped(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;

            InstanceFinder.ClientManager.OnClientConnectionState -= OnClientClientStopped;
            Log("Client: Disconnected from old host");

            ScheduleReconnect();
        }

        /// <summary>
        /// Schedules reconnection with retry logic for clients.
        /// The new host needs time to complete their migration.
        /// </summary>
        private void ScheduleReconnect()
        {
            _reconnectAttempt = 0;
            Log("Client: Waiting for new host to be ready...");

            // Start reconnect attempts with increasing delays
            // First attempt after 1.5s (host needs ~1s to migrate)
            Invoke(nameof(AttemptReconnect), 1.5f);
        }

        private void AttemptReconnect()
        {
            _reconnectAttempt++;

            if (_reconnectAttempt > MAX_RECONNECT_ATTEMPTS)
            {
                Log($"Client: Failed to reconnect after {MAX_RECONNECT_ATTEMPTS} attempts");
                IsMigrating = false;
                OnMigrationCompleted?.Invoke(); // Notify completion (failed)
                return;
            }

            Log($"Client: Reconnect attempt {_reconnectAttempt}/{MAX_RECONNECT_ATTEMPTS}...");

            // Try to start client connection
            InstanceFinder.ClientManager.OnClientConnectionState += OnReconnectResult;
            InstanceFinder.ClientManager.StartConnection();
        }

        private void OnReconnectResult(ClientConnectionStateArgs args)
        {
            // Only handle terminal states
            if (args.ConnectionState != LocalConnectionState.Started &&
                args.ConnectionState != LocalConnectionState.Stopped)
            {
                return;
            }

            InstanceFinder.ClientManager.OnClientConnectionState -= OnReconnectResult;

            if (args.ConnectionState == LocalConnectionState.Started)
            {
                // Success!
                IsMigrating = false;
                Log("Client: Successfully reconnected to new host");
                OnMigrationCompleted?.Invoke();
            }
            else
            {
                // Failed - try again with delay
                float delay = _reconnectAttempt < RECONNECT_DELAYS.Length
                    ? RECONNECT_DELAYS[_reconnectAttempt]
                    : RECONNECT_DELAYS[RECONNECT_DELAYS.Length - 1];

                Log($"Client: Connection failed, retrying in {delay}s...");
                Invoke(nameof(AttemptReconnect), delay);
            }
        }

        /// <summary>
        /// Restore all saved object states after server restart.
        /// </summary>
        private void MigrationFinish()
        {
            Log($"Restoring {_savedStates.Count} objects...");

            // Clean up any existing pending repossessions from previous migrations
            // These objects weren't claimed and would otherwise accumulate
            if (HostMigratable.PendingRepossessions.Count > 0)
            {
                Log($"Cleaning up {HostMigratable.PendingRepossessions.Count} unclaimed pending repossessions");
                foreach (var kvp in HostMigratable.PendingRepossessions)
                {
                    foreach (var migratable in kvp.Value)
                    {
                        if (migratable != null && migratable.NetworkObject != null)
                        {
                            Log($"Despawning unclaimed object: {migratable.gameObject.name}");
                            InstanceFinder.ServerManager.Despawn(migratable.NetworkObject);
                        }
                    }
                }
                HostMigratable.ClearPendingRepossessions();
            }

            // Iterate over a copy because SetActive(false) triggers OnDisable -> SaveObjectStateOnDisable
            // which would modify _savedStates during enumeration
            var statesToRestore = new List<MigratableObjectState>(_savedStates);
            _savedStates.Clear();

            foreach (var state in statesToRestore)
            {
                if (!_prefabDictionary.TryGetValue(state.PrefabName, out var prefab))
                {
                    Debug.LogWarning($"[HostMigrationManager] Prefab not found: {state.PrefabName}");
                    continue;
                }

                // Spawn the object
                var go = Instantiate(prefab, state.Position, state.Rotation);

                // Set LoadState BEFORE spawn so OnStartServer can detect migration restore
                var migratable = go.GetComponent<HostMigratable>();
                if (migratable != null)
                {
                    migratable.LoadState = state;
                }

                InstanceFinder.ServerManager.Spawn(go, null);

                // Load state and register for repossession
                if (migratable != null)
                {
                    migratable.LoadDataFromStateStruct(state);

                    // Clear LoadState now that restoration is complete
                    // Other components (like PlayerBall) have already checked LoadState.HasValue
                    // during OnStartServer to know this was a migration spawn
                    migratable.LoadState = null;

                    // Register for repossession directly using known PUID from saved state
                    // NOTE: We cannot use migratable.MarkForRepossession() here because
                    // FishNet SyncVars don't return the new value immediately after being set.
                    // The SyncVar.Value getter returns empty in the same frame it was set.
                    if (!string.IsNullOrEmpty(state.OwnerPuid))
                    {
                        if (!HostMigratable.PendingRepossessions.ContainsKey(state.OwnerPuid))
                        {
                            HostMigratable.PendingRepossessions[state.OwnerPuid] = new List<HostMigratable>();
                        }
                        HostMigratable.PendingRepossessions[state.OwnerPuid].Add(migratable);
                        migratable.gameObject.SetActive(false); // Deactivate until claimed
                        Log($"Registered for repossession: {state.PrefabName} for owner {state.OwnerPuid.Substring(0, 8)}...");
                    }
                    else
                    {
                        Log($"Restored: {state.PrefabName} (no owner PUID - scene object or unowned)");
                    }
                }
            }
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (!_enableDebugLogs) return;
            EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationManager", message);
        }

        #endregion

        #region Editor Access (for inspector)

#if UNITY_EDITOR
        internal int TrackedObjectCount => _migratableObjects.Count;
        internal int SavedStateCount => _savedStates.Count;
        internal int PrefabCount => _prefabDictionary.Count;
#endif

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(HostMigrationManager))]
    public class HostMigrationManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (HostMigrationManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                // Migration state
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Migration");
                var migrationStyle = new GUIStyle(EditorStyles.label);
                migrationStyle.normal.textColor = manager.IsMigrating ? Color.yellow : Color.green;
                EditorGUILayout.LabelField(manager.IsMigrating ? "IN PROGRESS" : "Idle", migrationStyle);
                EditorGUILayout.EndHorizontal();

                // Tracked objects
                EditorGUILayout.IntField("Tracked Objects", manager.TrackedObjectCount);
                EditorGUILayout.IntField("Saved States", manager.SavedStateCount);
                EditorGUILayout.IntField("Registered Prefabs", manager.PrefabCount);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Debug Controls", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Save States"))
                {
                    manager.DebugTriggerSave();
                }
                if (GUILayout.Button("Finish Migration"))
                {
                    manager.DebugTriggerFinish();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox("Use these buttons to test migration flow. 'Save States' captures current object state. 'Finish Migration' restores saved states.", MessageType.Info);

                EditorUtility.SetDirty(target);
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see runtime status and debug controls.", MessageType.Info);
            }
        }
    }
#endif
}
