using System;
using System.Collections.Generic;
using System.Reflection;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Migration
{
    /// <summary>
    /// Marks a NetworkBehaviour for host migration support.
    /// All SyncVars on this object and children will be saved/restored during migration.
    /// </summary>
    [Serializable]
    public class HostMigratable : NetworkBehaviour
    {
        #region Static

        /// <summary>
        /// Objects waiting for their original owner to reconnect.
        /// Key = owner PUID, Value = list of migratable objects.
        /// </summary>
        public static readonly Dictionary<string, List<HostMigratable>> PendingRepossessions = new();

        /// <summary>
        /// Enable debug logging for all HostMigratable instances.
        /// </summary>
        public static bool EnableDebugLogs = true;

        /// <summary>
        /// Clears all pending repossessions (call on session end).
        /// </summary>
        public static void ClearPendingRepossessions()
        {
            PendingRepossessions.Clear();
        }

        private static void Log(string message)
        {
            if (!EnableDebugLogs) return;
            EOSDebugLogger.Log(DebugCategory.HostMigratable, "HostMigratable", message);
        }

        #endregion

        #region Fields

        /// <summary>
        /// The owner's ProductUserId. Synced so new host knows original owner.
        /// </summary>
        public readonly SyncVar<string> OwnerPuidSyncVar = new(
            new SyncTypeSettings(WritePermission.ServerOnly)
        );

        /// <summary>
        /// State to load on spawn (set by HostMigrationManager during migration).
        /// </summary>
        [NonSerialized]
        public MigratableObjectState? LoadState;

        /// <summary>
        /// Cached owner PUID for use during disconnect when SyncVar may be cleared.
        /// </summary>
        private string _cachedOwnerPuid;

        /// <summary>
        /// Cached owner PUID for use during disconnect when SyncVar may be cleared.
        /// Use this when SyncVar.Value may have been cleared by FishNet.
        /// </summary>
        public string CachedOwnerPuid => _cachedOwnerPuid;

        /// <summary>
        /// Cached position for use during disconnect.
        /// </summary>
        private Vector3 _cachedPosition;

        /// <summary>
        /// Cached complete state including all SyncVars.
        /// Updated continuously so we have valid data when FishNet clears SyncVars.
        /// </summary>
        private MigratableObjectState? _cachedState;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Subscribe to SyncVar changes to cache the PUID
            OwnerPuidSyncVar.OnChange += OnOwnerPuidChanged;
        }

        private void OnOwnerPuidChanged(string prev, string next, bool asServer)
        {
            if (!string.IsNullOrEmpty(next))
            {
                _cachedOwnerPuid = next;
                _cachedPosition = transform.position;
                Log($"Cached owner PUID for {gameObject.name}: {next.Substring(0, Math.Min(8, next.Length))}... at {_cachedPosition}");
            }
        }

        private void Update()
        {
            // Keep state cached while object is alive and has valid owner
            // This captures SyncVar values BEFORE FishNet clears them on disconnect
            if (!string.IsNullOrEmpty(_cachedOwnerPuid))
            {
                _cachedPosition = transform.position;

                // Cache complete state including all SyncVars
                // Only do this if we have a valid PUID (meaning SyncVars should be valid)
                _cachedState = CaptureCurrentState();
            }
        }

        /// <summary>
        /// Captures current state by reading all SyncVars via reflection.
        /// Called continuously in Update() to maintain a valid snapshot.
        /// </summary>
        private MigratableObjectState CaptureCurrentState()
        {
            var syncData = new Dictionary<string, Dictionary<string, object>>();
            var behaviours = GetComponentsInChildren<NetworkBehaviour>(true);

            foreach (var behaviour in behaviours)
            {
                var type = behaviour.GetType();
                string relPath = GetRelativePath(behaviour.transform);
                string key = $"{relPath}|{type.Name}_{behaviour.GetInstanceID()}";
                var compData = new Dictionary<string, object>();

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                    {
                        var syncVarInstance = field.GetValue(behaviour);
                        var valueProp = field.FieldType.GetProperty("Value");
                        if (valueProp != null)
                        {
                            var value = valueProp.GetValue(syncVarInstance);
                            compData[field.Name] = value;
                        }
                    }
                }

                if (compData.Count > 0)
                {
                    syncData[key] = compData;
                }
            }

            return new MigratableObjectState
            {
                PrefabName = gameObject.name,
                Position = transform.position,
                Rotation = transform.rotation,
                OwnerPuid = _cachedOwnerPuid,
                SyncVarData = syncData
            };
        }

        private void OnEnable()
        {
            // Strip "(Clone)" from name for prefab lookup
            gameObject.name = gameObject.name.Replace("(Clone)", "");
            HostMigrationManager.Instance?.AddMigratableObject(this);
        }

        private void OnDisable()
        {
            // Save state BEFORE removing from tracking
            // This captures the state when objects are destroyed during disconnect
            if (HostMigrationManager.Instance != null)
            {
                // Use cached PUID if SyncVar is empty (can happen during disconnect)
                string effectivePuid = !string.IsNullOrEmpty(OwnerPuidSyncVar.Value)
                    ? OwnerPuidSyncVar.Value
                    : _cachedOwnerPuid;

                // Log diagnostic info to understand save conditions
                Log($"OnDisable for {gameObject.name}: SyncVar='{OwnerPuidSyncVar.Value}', Cached='{_cachedOwnerPuid}', Using='{effectivePuid}', Pos={_cachedPosition}");

                // Save if we have a valid PUID (from SyncVar or cache)
                if (!string.IsNullOrEmpty(effectivePuid))
                {
                    HostMigrationManager.Instance.SaveObjectStateOnDisable(this);
                }
                else
                {
                    Log($"Skipping save for {gameObject.name}: No owner PUID (not owned or scene object)");
                }

                HostMigrationManager.Instance.RemoveMigratableObject(this);
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from SyncVar changes
            OwnerPuidSyncVar.OnChange -= OnOwnerPuidChanged;
        }

        #endregion

        #region FishNet Callbacks

        public override void OnStartServer()
        {
            base.OnStartServer();

            // NOTE: We do NOT clear LoadState here anymore.
            // MigrationFinish() in HostMigrationManager handles calling LoadDataFromStateStruct
            // and other components (like PlayerBall) need to check LoadState.HasValue
            // to know whether this is a migration spawn vs normal spawn.

            if (LoadState.HasValue)
            {
                Log($"OnStartServer: LoadState present for {gameObject.name}, will be restored by MigrationFinish");
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Only run PUID logic on the server
            if (!IsServerInitialized) return;

            // NOTE: We do NOT clear LoadState here anymore.
            // MigrationFinish() handles restoration, and other components need LoadState.HasValue
            // to differentiate migration spawns from normal spawns.

            if (LoadState.HasValue)
            {
                Log($"OnStartClient: LoadState present for {gameObject.name}, will be restored by MigrationFinish");
            }

            // Set PUID for normally spawned objects (has owner with address)
            // OwnerId -1 means no owner (scene object or migration-spawned waiting for claim)
            if (OwnerId != -1 && Owner != null)
            {
                string ownerPuid = Owner.GetAddress();

                // Client host doesn't have network address, use local PUID
                if (string.IsNullOrEmpty(ownerPuid) && Owner.IsLocalClient)
                {
                    ownerPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
                }

                if (!string.IsNullOrEmpty(ownerPuid))
                {
                    OwnerPuidSyncVar.Value = ownerPuid;
                    Log($"Set owner PUID for {gameObject.name}: {ownerPuid?.Substring(0, 8)}...");
                }
            }
            // else: No owner - likely spawned via migration, PUID will be set from LoadState
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);

            // Update PUID when ownership changes (e.g., repossession)
            if (Owner == null) return;

            string ownerPuid = Owner.GetAddress();

            // Client host doesn't have network address, use local PUID
            if (string.IsNullOrEmpty(ownerPuid) && Owner.IsLocalClient)
            {
                ownerPuid = EOSManager.Instance?.LocalProductUserId?.ToString();
            }

            if (!string.IsNullOrEmpty(ownerPuid))
            {
                OwnerPuidSyncVar.Value = ownerPuid;
                Log($"Updated owner PUID for {gameObject.name}: {ownerPuid?.Substring(0, 8)}...");
            }
        }

        #endregion

        #region Repossession

        /// <summary>
        /// Mark this object for repossession when its owner reconnects.
        /// Object is deactivated until the owner claims it.
        /// </summary>
        public void MarkForRepossession()
        {
            string ownerKey = OwnerPuidSyncVar.Value;
            if (string.IsNullOrEmpty(ownerKey))
            {
                Debug.LogWarning($"[HostMigratable] Cannot mark {gameObject.name} for repossession - no owner PUID");
                return;
            }

            if (!PendingRepossessions.ContainsKey(ownerKey))
            {
                PendingRepossessions[ownerKey] = new List<HostMigratable>();
            }
            PendingRepossessions[ownerKey].Add(this);

            // Deactivate until owner reconnects and claims this object
            // HostMigrationPlayerSpawner will reactivate when repossessing
            gameObject.SetActive(false);

            Log($"{gameObject.name} marked for repossession by {ownerKey.Substring(0, 8)}... (deactivated)");
        }

        /// <summary>
        /// Transfer ownership to the reconnecting owner.
        /// </summary>
        public void Repossess(NetworkConnection newOwner)
        {
            if (NetworkObject == null)
            {
                Debug.LogError("[HostMigratable] NetworkObject is null during repossession");
                return;
            }
            if (newOwner == null)
            {
                Debug.LogError("[HostMigratable] newOwner is null during repossession");
                return;
            }

            Log($"Repossessing {gameObject.name} to connection {newOwner.ClientId}");
            NetworkObject.GiveOwnership(newOwner);
            Log($"Repossession complete for {gameObject.name}");
        }

        #endregion

        #region State Serialization

        /// <summary>
        /// Saves all SyncVar data from this object and its children.
        /// Uses cached state if available (SyncVars may be zeroed by FishNet on disconnect).
        /// </summary>
        public MigratableObjectState SaveDataToStateStruct()
        {
            // Use cached values as fallback (SyncVar may be cleared during disconnect)
            string effectivePuid = !string.IsNullOrEmpty(OwnerPuidSyncVar.Value)
                ? OwnerPuidSyncVar.Value
                : _cachedOwnerPuid;

            // If we have a cached state with the same owner, use it!
            // This is critical because FishNet clears SyncVars BEFORE OnDisable fires
            if (_cachedState.HasValue && _cachedState.Value.OwnerPuid == effectivePuid)
            {
                var cached = _cachedState.Value;
                // Update position to latest cached value
                cached.Position = _cachedPosition;
                cached.Rotation = transform.rotation;

                Log($"Using CACHED state for {gameObject.name} (SyncVars preserved from Update)");
                if (cached.SyncVarData != null)
                {
                    foreach (var kvp in cached.SyncVarData)
                    {
                        string typeName = kvp.Key.Split('|')[1].Split('_')[0];
                        Log($"Saving SyncVars for {typeName}: {string.Join(", ", kvp.Value.Keys)}");
                        foreach (var data in kvp.Value)
                        {
                            Log($"  {data.Key} = {data.Value}");
                        }
                    }
                }
                return cached;
            }

            // Fallback: read current values (may be zeroed if called during disconnect)
            Log($"WARNING: No cached state for {gameObject.name}, reading current SyncVars (may be zeroed)");

            var syncData = new Dictionary<string, Dictionary<string, object>>();
            var behaviours = GetComponentsInChildren<NetworkBehaviour>(true);

            foreach (var behaviour in behaviours)
            {
                var type = behaviour.GetType();
                string relPath = GetRelativePath(behaviour.transform);
                string key = $"{relPath}|{type.Name}_{behaviour.GetInstanceID()}";
                var compData = new Dictionary<string, object>();

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                    {
                        var syncVarInstance = field.GetValue(behaviour);
                        var valueProp = field.FieldType.GetProperty("Value");
                        if (valueProp != null)
                        {
                            var value = valueProp.GetValue(syncVarInstance);
                            compData[field.Name] = value;
                        }
                    }
                }

                if (compData.Count > 0)
                {
                    syncData[key] = compData;
                    Log($"Saving SyncVars for {type.Name}: {string.Join(", ", compData.Keys)}");
                    foreach (var data in compData)
                    {
                        Log($"  {data.Key} = {data.Value}");
                    }
                }
            }

            // Use cached position if we have one (more reliable during disconnect)
            Vector3 effectivePosition = !string.IsNullOrEmpty(_cachedOwnerPuid)
                ? _cachedPosition
                : transform.position;

            return new MigratableObjectState
            {
                PrefabName = gameObject.name,
                Position = effectivePosition,
                Rotation = transform.rotation,
                OwnerPuid = effectivePuid,
                SyncVarData = syncData
            };
        }

        /// <summary>
        /// Restores all SyncVar data to this object and its children.
        /// </summary>
        public void LoadDataFromStateStruct(MigratableObjectState state)
        {
            Log($"LoadDataFromStateStruct: Restoring {gameObject.name} at {state.Position}, owner={state.OwnerPuid?.Substring(0, Math.Min(8, state.OwnerPuid?.Length ?? 0))}...");
            transform.position = state.Position;
            transform.rotation = state.Rotation;
            OwnerPuidSyncVar.Value = state.OwnerPuid;

            if (state.SyncVarData == null)
            {
                Log($"LoadDataFromStateStruct: No SyncVarData to restore");
                return;
            }

            Log($"LoadDataFromStateStruct: {state.SyncVarData.Count} component(s) to restore");

            foreach (var kvp in state.SyncVarData)
            {
                string[] parts = kvp.Key.Split('|');
                if (parts.Length != 2)
                {
                    Debug.LogWarning($"[HostMigratable] Invalid key format: {kvp.Key}");
                    continue;
                }

                string relPath = parts[0];
                string[] typeParts = parts[1].Split('_');
                if (typeParts.Length < 2)
                {
                    Debug.LogWarning($"[HostMigratable] Invalid component key format: {parts[1]}");
                    continue;
                }

                string typeName = typeParts[0];
                var targetTransform = FindChildByPath(relPath);
                if (targetTransform == null)
                {
                    Debug.LogWarning($"[HostMigratable] Could not find transform at path {relPath}");
                    continue;
                }

                var comps = targetTransform.GetComponents<NetworkBehaviour>();
                NetworkBehaviour targetComp = null;
                foreach (var comp in comps)
                {
                    if (comp.GetType().Name == typeName)
                    {
                        targetComp = comp;
                        break;
                    }
                }

                if (targetComp == null)
                {
                    Debug.LogWarning($"[HostMigratable] No NetworkBehaviour of type {typeName} found at path {relPath}");
                    continue;
                }

                Log($"Loading SyncVars for {typeName}: found={targetComp != null}");

                var compData = kvp.Value;
                var fields = targetComp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var field in fields)
                {
                    if (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
                    {
                        if (compData.TryGetValue(field.Name, out var value))
                        {
                            var syncVarInstance = field.GetValue(targetComp);
                            var valueProp = field.FieldType.GetProperty("Value");
                            valueProp?.SetValue(syncVarInstance, value);
                            Log($"  Restoring {field.Name} = {value}");
                        }
                    }
                }
            }

            Log($"LoadDataFromStateStruct: Complete for {gameObject.name}");
        }

        #endregion

        #region Path Helpers

        private string GetRelativePath(Transform child)
        {
            if (child == transform) return "ROOT";

            var names = new List<string>();
            var current = child;
            while (current != null && current != transform)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private Transform FindChildByPath(string path)
        {
            if (path == "ROOT") return transform;
            return transform.Find(path);
        }

        #endregion
    }

    /// <summary>
    /// Serializable state for a migratable object.
    /// </summary>
    [Serializable]
    public struct MigratableObjectState
    {
        public string PrefabName;
        public Vector3 Position;
        public Quaternion Rotation;
        public string OwnerPuid;

        /// <summary>
        /// SyncVar data. Key: "relativePath|TypeName_InstanceID", Value: field name -> value.
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> SyncVarData;
    }
}
