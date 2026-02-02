using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transport.EOSNative.Lobbies;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Attach to player prefabs to enable automatic tracking.
    /// Auto-registers to a static list for easy enumeration of all players.
    /// Stores PUID and display name for identification.
    /// </summary>
    public class EOSNetworkPlayer : NetworkBehaviour
    {
        #region Static Registry

        private static readonly List<EOSNetworkPlayer> _allPlayers = new();

        /// <summary>
        /// All currently active player instances (across all clients).
        /// </summary>
        public static IReadOnlyList<EOSNetworkPlayer> AllPlayers => _allPlayers;

        /// <summary>
        /// Number of tracked player objects.
        /// </summary>
        public static int PlayerCount => _allPlayers.Count;

        /// <summary>
        /// Gets the local player (owned by this client).
        /// </summary>
        public static EOSNetworkPlayer LocalPlayer
        {
            get
            {
                foreach (var p in _allPlayers)
                {
                    if (p.IsOwner) return p;
                }
                return null;
            }
        }

        #endregion

        #region SyncVars

        /// <summary>
        /// The owner's ProductUserId (synced to all clients).
        /// </summary>
        private readonly SyncVar<string> _ownerPuid = new SyncVar<string>("");

        /// <summary>
        /// The owner's display name (synced to all clients).
        /// </summary>
        private readonly SyncVar<string> _displayName = new SyncVar<string>("");

        #endregion

        #region Public Properties

        /// <summary>
        /// This player's PUID.
        /// </summary>
        public string Puid => _ownerPuid.Value;

        /// <summary>
        /// This player's display name.
        /// </summary>
        public string DisplayName => !string.IsNullOrEmpty(_displayName.Value) ? _displayName.Value : $"Player {OwnerId}";

        /// <summary>
        /// Short PUID for display (first 8 chars).
        /// </summary>
        public string ShortPuid => _ownerPuid.Value?.Length > 8 ? _ownerPuid.Value.Substring(0, 8) + "..." : _ownerPuid.Value;

        /// <summary>
        /// Whether this is the local player (we own it).
        /// </summary>
        public bool IsLocal => IsOwner;

        /// <summary>
        /// FishNet connection ID.
        /// </summary>
        public int ConnectionId => OwnerId;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            // Subscribe to SyncVar change events
            _ownerPuid.OnChange += OnPuidChanged;
            _displayName.OnChange += OnDisplayNameChanged;
        }

        private void OnEnable()
        {
            // Register to static list
            if (!_allPlayers.Contains(this))
            {
                _allPlayers.Add(this);
            }
        }

        private void OnDisable()
        {
            // Deregister from static list
            _allPlayers.Remove(this);
        }

        private void OnDestroy()
        {
            // Unsubscribe from SyncVar change events
            _ownerPuid.OnChange -= OnPuidChanged;
            _displayName.OnChange -= OnDisplayNameChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Server sets the owner's PUID from the transport
            var transport = FindAnyObjectByType<EOSNativeTransport>();
            if (transport != null)
            {
                // Check if this is the ClientHost (local player on server)
                if (Owner.IsLocalClient)
                {
                    _ownerPuid.Value = EOSManager.Instance?.LocalProductUserId?.ToString() ?? "";
                }
                else
                {
                    // Remote client - get PUID from server's connection map
                    string puid = transport.GetPuidForConnection(OwnerId);
                    _ownerPuid.Value = puid ?? "";
                }
            }

            // Get display name from chat manager if available
            var chatManager = EOSLobbyChatManager.Instance;
            if (chatManager != null && !string.IsNullOrEmpty(_ownerPuid.Value))
            {
                _displayName.Value = chatManager.GetOrGenerateDisplayName(_ownerPuid.Value);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // If we're the owner, try to set our display name
            if (IsOwner)
            {
                var chatManager = EOSLobbyChatManager.Instance;
                if (chatManager != null)
                {
                    // Request server to update our display name
                    string localName = chatManager.DisplayName;
                    if (!string.IsNullOrEmpty(localName))
                    {
                        CmdSetDisplayName(localName);
                    }
                }
            }
        }

        #endregion

        #region SyncVar Callbacks

        private void OnPuidChanged(string oldValue, string newValue, bool asServer)
        {
            // PUID changed - can update UI or other systems here
        }

        private void OnDisplayNameChanged(string oldValue, string newValue, bool asServer)
        {
            // Display name changed - can update UI or name tags here
        }

        #endregion

        #region Server RPCs

        /// <summary>
        /// Client requests to set their display name.
        /// </summary>
        [ServerRpc]
        private void CmdSetDisplayName(string name)
        {
            if (!string.IsNullOrEmpty(name) && name.Length <= 32)
            {
                _displayName.Value = name;
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Gets a player by their PUID.
        /// </summary>
        public static EOSNetworkPlayer GetByPuid(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return null;

            foreach (var p in _allPlayers)
            {
                if (p._ownerPuid.Value == puid) return p;
            }
            return null;
        }

        /// <summary>
        /// Gets a player by their connection ID.
        /// </summary>
        public static EOSNetworkPlayer GetByConnectionId(int connectionId)
        {
            foreach (var p in _allPlayers)
            {
                if (p.OwnerId == connectionId) return p;
            }
            return null;
        }

        public override string ToString()
        {
            return $"{DisplayName} ({ShortPuid}) [Conn:{OwnerId}]";
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSNetworkPlayer))]
    public class EOSNetworkPlayerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var player = (EOSNetworkPlayer)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.TextField("Display Name", player.DisplayName);
                EditorGUILayout.TextField("PUID", player.Puid ?? "(none)");
                EditorGUILayout.IntField("Connection ID", player.ConnectionId);
                EditorGUILayout.Toggle("Is Local (Owner)", player.IsLocal);
                EditorGUILayout.Toggle("Is Spawned", player.IsSpawned);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Static Registry", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.IntField("Total Players", EOSNetworkPlayer.PlayerCount);

                var localPlayer = EOSNetworkPlayer.LocalPlayer;
                EditorGUILayout.TextField("Local Player", localPlayer?.DisplayName ?? "(none)");
            }

            if (Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
