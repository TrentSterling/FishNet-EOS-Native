# Classes & Architecture

Technical documentation for FishNet-EOS-Native class structure.

## Overview

```
┌─────────────────────────────────────────────────────────────┐
│                      Unity Scene                             │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐    ┌──────────────────────────────────┐   │
│  │ EOSManager  │    │       NetworkManager             │   │
│  │ (Singleton) │    │  ┌────────────────────────────┐  │   │
│  │             │    │  │   EOSNativeTransport       │  │   │
│  │ - Platform  │◄───┤  │                            │  │   │
│  │ - Tick()    │    │  │  ┌─────────┐ ┌─────────┐  │  │   │
│  │ - Auth      │    │  │  │EOSServer│ │EOSClient│  │  │   │
│  └─────────────┘    │  │  └─────────┘ └─────────┘  │  │   │
│                     │  │       ┌────────────┐      │  │   │
│                     │  │       │EOSClientHost│     │  │   │
│                     │  │       └────────────┘      │  │   │
│                     │  └────────────────────────────┘  │   │
│                     └──────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                    EOS SDK (P2PInterface)                   │
└─────────────────────────────────────────────────────────────┘
```

---

## Core Classes

### EOSManager

Singleton responsible for EOS SDK lifecycle management.

**Responsibilities:**
- Initialize and shutdown EOS SDK
- Create and manage PlatformInterface
- Call `Tick()` every frame
- Handle platform-specific library loading
- Manage Connect Interface login

**Location:** `Assets/FishNet.Transport.EOSNative/EOSManager.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSManager : MonoBehaviour
    {
        // Singleton
        public static EOSManager Instance { get; private set; }

        // SDK State
        public static PlatformInterface Platform { get; private set; }
        public static ProductUserId LocalProductUserId { get; private set; }
        public static bool IsInitialized { get; private set; }

        // Configuration (serialized)
        [SerializeField] private string productId;
        [SerializeField] private string sandboxId;
        [SerializeField] private string deploymentId;
        [SerializeField] private string clientId;
        [SerializeField] private string clientSecret;
        [SerializeField] private string encryptionKey;

        // Lifecycle
        public void Initialize();
        public void Shutdown();

        // Auth
        public void LoginWithDeviceToken(string displayName, Action<Result> callback);
        public void Logout();

        // Interfaces (cached)
        public static P2PInterface GetP2PInterface();
        public static ConnectInterface GetConnectInterface();
        public static LobbyInterface GetLobbyInterface();
    }
}
```

---

### EOSNativeTransport

Main FishNet Transport implementation.

**Responsibilities:**
- Implement FishNet Transport interface
- Manage server/client peer instances
- Route data between FishNet and EOS P2P
- Handle connection state changes

**Location:** `Assets/FishNet.Transport.EOSNative/EOSNativeTransport.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    [AddComponentMenu("FishNet/Transport/EOS Native")]
    public class EOSNativeTransport : Transport
    {
        // Configuration
        [SerializeField] private string socketName = "FishNetEOS";
        [SerializeField] private string remoteProductUserId;
        [SerializeField] private int maxClients = 100;

        // Peers
        private EOSServer _server;
        private EOSClient _client;
        private EOSClientHost _clientHost;

        // Constants
        public const int CLIENT_HOST_ID = short.MaxValue;

        // Properties
        public string SocketName { get; set; }
        public string LocalProductUserId { get; }
        public string RemoteProductUserId { get; set; }

        // Transport Interface Implementation
        public override void Initialize(NetworkManager nm, int transportIndex);
        public override bool StartConnection(bool server);
        public override bool StopConnection(bool server);
        public override void SendToServer(byte channel, ArraySegment<byte> data);
        public override void SendToClient(byte channel, ArraySegment<byte> data, int connectionId);
        public override void IterateIncoming(bool server);
        public override void IterateOutgoing(bool server);
        // ... etc
    }
}
```

---

### EOSServer

Server-side peer handling.

**Responsibilities:**
- Listen for incoming P2P connection requests
- Accept/reject connections
- Map connectionId ↔ ProductUserId
- Send packets to specific clients
- Receive packets from clients
- Handle client disconnections

**Location:** `Assets/FishNet.Transport.EOSNative/EOSServer.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSServer
    {
        // Connection mapping
        private Dictionary<int, ProductUserId> _connectionToUserId;
        private Dictionary<string, int> _userIdToConnection;
        private int _nextConnectionId = 1;

        // Events
        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;
        public event Action<int, ArraySegment<byte>, Channel> OnDataReceived;

        // Lifecycle
        public void Initialize(EOSNativeTransport transport);
        public bool Start();
        public void Stop();

        // Operations
        public void SendToClient(int connectionId, ArraySegment<byte> data, Channel channel);
        public void DisconnectClient(int connectionId);
        public void ReceiveMessages();

        // Helpers
        public ProductUserId GetProductUserId(int connectionId);
        public int GetConnectionId(ProductUserId userId);
    }
}
```

---

### EOSClient

Client-side peer handling.

**Responsibilities:**
- Connect to remote server by ProductUserId
- Send packets to server
- Receive packets from server
- Handle server disconnection

**Location:** `Assets/FishNet.Transport.EOSNative/EOSClient.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSClient
    {
        // State
        private ProductUserId _remoteUserId;
        private ConnectionState _state;

        // Events
        public event Action<ConnectionState> OnConnectionStateChanged;
        public event Action<ArraySegment<byte>, Channel> OnDataReceived;

        // Lifecycle
        public void Initialize(EOSNativeTransport transport);
        public void Connect(string remoteProductUserId);
        public void Disconnect();

        // Operations
        public void Send(ArraySegment<byte> data, Channel channel);
        public void ReceiveMessages();
    }
}
```

---

### EOSClientHost

Special peer for host acting as both server and client.

**Responsibilities:**
- Route local client packets directly to server (bypass P2P)
- Route server packets to local client
- Use CLIENT_HOST_ID for identification

**Location:** `Assets/FishNet.Transport.EOSNative/EOSClientHost.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSClientHost
    {
        // Packet queues (local routing, no P2P)
        private Queue<ArraySegment<byte>> _outgoingToServer;
        private Queue<ArraySegment<byte>> _incomingFromServer;

        // Lifecycle
        public void Initialize(EOSNativeTransport transport);
        public void Start();
        public void Stop();

        // Operations
        public void SendToServer(ArraySegment<byte> data);
        public void SendToClient(ArraySegment<byte> data);
        public void ReceiveFromServer(out ArraySegment<byte> data);
        public void ReceiveFromClient(out ArraySegment<byte> data);
    }
}
```

---

## Utility Classes

### PacketFragmenter

Handles packet fragmentation and reassembly for EOS P2P's 1170-byte limit.

**Location:** `Assets/FishNet.Transport.EOSNative/PacketFragmenter.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class PacketFragmenter
    {
        // Header format (7 bytes):
        // - packetId (uint, 4 bytes): Identifies the original packet
        // - fragmentId (ushort, 2 bytes): Fragment index (0, 1, 2...)
        // - lastFragment (byte, 1 byte): 1 if final fragment

        public const int HeaderSize = 7;
        public const int MaxPacketSize = 1170;
        public const int MaxPayloadSize = 1163; // 1170 - 7

        // Fragment outgoing data
        public IEnumerable<ArraySegment<byte>> Fragment(ArraySegment<byte> data);

        // Reassemble incoming fragments
        public byte[] ProcessIncoming(int senderId, ArraySegment<byte> data, byte channel);

        // Cleanup
        public void ClearPendingForSender(int senderId);
        public void ClearAll();
    }
}
```

---

### NotifyEventHandle

Auto-dispose wrapper for EOS notification handles. Prevents memory leaks by automatically unsubscribing when disposed.

**Location:** `Assets/FishNet.Transport.EOSNative/NotifyEventHandle.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class NotifyEventHandle : IDisposable
    {
        public NotifyEventHandle(ulong handle, Action<ulong> removeAction);
        public void Dispose();
    }
}
```

**Usage:**
```csharp
// Create handle with cleanup action
_connectionRequestHandle = new NotifyEventHandle(
    P2P.AddNotifyPeerConnectionRequest(ref options, null, OnConnectionRequest),
    h => P2P.RemoveNotifyPeerConnectionRequest(h)
);

// Handle is automatically cleaned up when disposed
_connectionRequestHandle?.Dispose();
```

---

### EOSNativeUI

Main runtime UI with lobby controls, chat, and stats display.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSNativeUI.cs`

- Toggle with F1 key
- Dark theme with color-coded status indicators
- Lobby controls (host, join, leave, quick match)
- Connection stats (bandwidth, player counts)
- Lobby chat integration
- Mismatch detection (lobby vs FishNet sync status)

---

### EOSVoiceDebugPanel

Voice chat debug panel with RTC status and participant management.

**Location:** `Assets/FishNet.Transport.EOSNative/DebugUI/EOSVoiceDebugPanel.cs`

- Toggle with F3 key
- RTC room connection status
- Local mic status with level indicator
- Participant list with speaking indicators
- Per-participant mute controls
- Audio level bars with peak detection

---

### EOSNetworkDebugPanel

Network debug panel showing P2P connections, bandwidth graph, and migration status.

**Location:** `Assets/FishNet.Transport.EOSNative/DebugUI/EOSNetworkDebugPanel.cs`

- Toggle with F4 key
- P2P connection status (server/client, socket, relay)
- Live bandwidth graph (in/out KB/s)
- Total data transferred
- Host migration status and debug controls
- Connection list with PUIDs
- Lobby/FishNet sync status

---

### EOSStatsDebugger (Deprecated)

Legacy stats overlay. Stats are now integrated into EOSNativeUI.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSStatsDebugger.cs`

- Toggle with F2 key
- Disabled by default
- Kept for backwards compatibility

---

## Lobby Classes

### EOSLobbyManager

Lobby management system.

**Location:** `Assets/FishNet.Transport.EOSNative/Lobbies/EOSLobbyManager.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSLobbyManager : MonoBehaviour
    {
        // Current lobby state
        public string CurrentLobbyId { get; }
        public bool IsInLobby { get; }
        public bool IsLobbyOwner { get; }

        // Events
        public event Action<string> OnLobbyCreated;
        public event Action<string> OnLobbyJoined;
        public event Action OnLobbyLeft;
        public event Action<ProductUserId> OnMemberJoined;
        public event Action<ProductUserId> OnMemberLeft;
        public event Action<ProductUserId> OnMemberUpdated;
        public event Action<ProductUserId> OnHostChanged;

        // Operations (async Task versions)
        public Task<Result> CreateLobbyAsync(string lobbyName, int maxMembers);
        public Task<Result> JoinLobbyAsync(string lobbyId);
        public Task<Result> LeaveLobbyAsync();
        public Task<LobbySearchResult[]> SearchLobbiesAsync(string bucketId);

        // Attributes
        public Task<Result> SetLobbyAttributeAsync(string key, string value);
        public Task<Result> SetMemberAttributeAsync(string key, string value);
        public string GetLobbyAttribute(string key);
        public string GetMemberAttribute(ProductUserId userId, string key);
    }
}
```

---

### LobbyData / LobbyCreateOptions / LobbySearchOptions

Data structures and options for lobby operations.

**Location:** `Assets/FishNet.Transport.EOSNative/Lobbies/LobbyData.cs`

```csharp
namespace FishNet.Transport.EOSNative.Lobbies
{
    // Lightweight lobby data container
    public struct LobbyData
    {
        public string LobbyId;
        public string JoinCode;
        public string OwnerPuid;
        public int MemberCount;
        public int MaxMembers;
        public Dictionary<string, string> Attributes;

        // Typed accessors
        public string LobbyName { get; }
        public string GameMode { get; }
        public string Map { get; }
        public string Region { get; }
    }

    // Options for creating a lobby
    public class LobbyCreateOptions
    {
        public uint MaxPlayers = 4;
        public bool IsPublic = true;
        public string JoinCode = null;  // Auto-generated if null
        public string LobbyName = null;
        public string GameMode = null;
        public string Map = null;
        public string Region = null;
        public string Password = null;
        public int? SkillLevel = null;
    }

    // Fluent builder for lobby search filters
    public class LobbySearchOptions
    {
        public uint MaxResults = 10;
        public string JoinCode = null;
        public bool OnlyAvailable = true;

        // Fluent methods
        public LobbySearchOptions WithGameMode(string mode);
        public LobbySearchOptions WithRegion(string region);
        public LobbySearchOptions ExcludePassworded(bool exclude = true);
        public LobbySearchOptions ExcludeGamesInProgress(bool exclude = true);

        // Static factories
        public static LobbySearchOptions QuickMatch();
        public static LobbySearchOptions ForGameMode(string mode);
        public static LobbySearchOptions ForRegion(string region);
    }
}
```

---

---

## Voice Classes

### EOSVoiceManager

Manages EOS RTC voice chat for lobby-based voice.

**Location:** `Assets/FishNet.Transport.EOSNative/Voice/EOSVoiceManager.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSVoiceManager : MonoBehaviour
    {
        // State
        public bool IsInVoiceRoom { get; }
        public bool IsMuted { get; set; }
        public float MasterVolume { get; set; }

        // Events
        public event Action<ProductUserId> OnParticipantJoined;
        public event Action<ProductUserId> OnParticipantLeft;
        public event Action<ProductUserId, bool> OnParticipantSpeaking;

        // Operations
        public void JoinVoiceRoom(string roomName);
        public void LeaveVoiceRoom();
        public void MuteParticipant(ProductUserId userId, bool mute);
        public void SetParticipantVolume(ProductUserId userId, float volume);

        // Participant management
        public EOSVoicePlayer GetVoicePlayer(ProductUserId userId);
        public IEnumerable<ProductUserId> GetParticipants();
    }
}
```

---

### EOSVoicePlayer

Per-participant audio playback with 3D spatial and voice effects support.

**Location:** `Assets/FishNet.Transport.EOSNative/Voice/EOSVoicePlayer.cs`

```csharp
namespace FishNet.Transport.EOSNative.Voice
{
    public class EOSVoicePlayer : MonoBehaviour
    {
        // Properties
        public string ParticipantPuid { get; set; }
        public bool IsReceivingAudio { get; }
        public int QueuedFrames { get; }
        public bool IsSpeaking { get; }

        // 3D Audio Settings (Inspector)
        [SerializeField] private float _spatialBlend = 1f;      // 0=2D, 1=3D
        [SerializeField] private float _dopplerLevel = 1f;      // 0=off, 1=normal, 5=exaggerated
        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 50f;
        [SerializeField] private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        // Voice Effects Settings (Inspector)
        [SerializeField] private bool _enablePitchShift = false;
        [SerializeField] private float _pitchShift = 1f;        // 0.5=octave down, 2.0=octave up
        [SerializeField] private int _pitchShiftQuality = 10;   // 4=fast, 10=balanced, 32=best

        // 3D Audio Properties (runtime)
        public float SpatialBlend { get; set; }
        public float DopplerLevel { get; set; }
        public AudioSource AudioSource { get; }

        // Voice Effects Properties (runtime)
        public bool EnablePitchShift { get; set; }
        public float PitchShift { get; set; }  // 0.5-2.0

        // Methods
        public void SetParticipant(string puid);
        public void Apply3DAudioSettings();
        public void ClearBuffer();
        public void Play();
        public void Stop();
    }
}
```

---

### SMBPitchShifter

STFT-based pitch shifter for voice effects (instance-based for multi-participant support).

**Location:** `Assets/FishNet.Transport.EOSNative/Voice/SMBPitchShifter.cs`

```csharp
namespace FishNet.Transport.EOSNative.Voice
{
    public class SMBPitchShifter
    {
        // Constants
        public const int MAX_FRAME_LENGTH = 16000;
        public const int DEFAULT_FFT_FRAME_SIZE = 2048;
        public const int DEFAULT_OSAMP = 10;  // Quality: 4=fast, 10=balanced, 32=best

        // Constructor
        public SMBPitchShifter();

        // Methods
        public void Reset();  // Reset state (call when switching participants)
        public void Process(float pitchShift, float sampleRate, float[] data);
        public void Process(float pitchShift, long numSamples, long fftFrameSize,
                           long osamp, float sampleRate, float[] data);
    }
}
```

**Pitch shift values:**
- 0.5 = one octave down (chipmunk → deep voice)
- 1.0 = no change
- 2.0 = one octave up (deep voice → chipmunk)

---

### FishNetVoicePlayer

NetworkBehaviour that auto-wires voice from PUID to player GameObject.

**Location:** `Assets/FishNet.Transport.EOSNative/Voice/FishNetVoicePlayer.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class FishNetVoicePlayer : NetworkBehaviour
    {
        // 3D Audio Settings (Inspector)
        [SerializeField] private float _spatialBlend = 1f;      // 0=2D, 1=3D
        [SerializeField] private float _dopplerLevel = 1f;      // 0=off, 1=normal, 5=exaggerated
        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 50f;
        [SerializeField] private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        // 3D Audio Properties (runtime)
        public float SpatialBlend { get; set; }
        public float DopplerLevel { get; set; }
        public AudioSource AudioSource { get; }

        // Auto-wired from owner's PUID
        public EOSVoicePlayer VoicePlayer { get; }
        public string OwnerPuid { get; }

        // Methods
        public void SetParticipantPuid(string puid);
    }
}
```

**Usage:**
```csharp
// Add to player prefab - voice automatically follows player position
// Owner's PUID is resolved from NetworkObject ownership
// Doppler effect works automatically as players move relative to listener
```

---

## Chat Classes

### EOSLobbyChatManager

Text chat via lobby member attributes (survives host migration).

**Location:** `Assets/FishNet.Transport.EOSNative/Lobbies/EOSLobbyChatManager.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class EOSLobbyChatManager : MonoBehaviour
    {
        // Events
        public event Action<ProductUserId, string> OnMessageReceived;

        // Operations
        public void SendMessage(string message);
        public void ClearHistory();

        // History
        public IReadOnlyList<ChatMessage> GetHistory();
    }

    public struct ChatMessage
    {
        public ProductUserId SenderId;
        public string SenderName;
        public string Message;
        public DateTime Timestamp;
    }
}
```

**Pattern:**
Chat works by setting member attributes, which all lobby members receive via `LobbyMemberUpdateReceived` callback. This means chat survives host migration since it's lobby-based, not P2P-based.

---

### EOSChatUI

Standalone chat UI component for custom integration.

**Location:** `Assets/FishNet.Transport.EOSNative/Demo/EOSChatUI.cs`

```csharp
namespace FishNet.Transport.EOSNative.Demo
{
    public class EOSChatUI : MonoBehaviour
    {
        // Configuration
        [SerializeField] private int maxMessages = 50;
        [SerializeField] private KeyCode toggleKey = KeyCode.Return;

        // State
        public bool IsInputFocused { get; }
    }
}
```

---

## Migration Classes

### HostMigratable

NetworkBehaviour component for objects that need to survive host migration.

**Location:** `Assets/FishNet.Transport.EOSNative/Migration/HostMigratable.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class HostMigratable : NetworkBehaviour
    {
        // Owner tracking (survives migration)
        [SyncVar] public string OwnerProductUserId;

        // State
        public byte[] SerializedState { get; private set; }
        public bool NeedsRepossession { get; }

        // Operations
        public byte[] SaveState();
        public void LoadState(byte[] data);
        public void MarkForRepossession();
        public void Repossess(NetworkConnection newOwner);

        // Events
        public event Action OnStateRestored;
        public event Action<NetworkConnection> OnRepossessed;
    }
}
```

**Usage:**
```csharp
// Add to any NetworkObject that should survive host migration
// SyncVars are automatically serialized via reflection
public class PlayerData : HostMigratable
{
    [SyncVar] public int score;
    [SyncVar] public string playerName;
    // These survive host migration automatically!
}
```

---

### HostMigrationManager

Orchestrates the host migration process.

**Location:** `Assets/FishNet.Transport.EOSNative/Migration/HostMigrationManager.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class HostMigrationManager : MonoBehaviour
    {
        // State
        public bool IsMigrating { get; }
        public bool IsNewHost { get; }

        // Events
        public event Action OnMigrationStarted;
        public event Action OnMigrationCompleted;
        public event Action<ProductUserId> OnNewHostPromoted;

        // Object tracking
        public void RegisterMigratable(HostMigratable obj);
        public void UnregisterMigratable(HostMigratable obj);

        // Migration operations
        public void BeginMigration();
        public void SaveAllStates();
        public void RestoreAllStates();
    }
}
```

**Migration Flow:**
1. Old host leaves → EOS promotes new lobby owner
2. New host saves all HostMigratable object states
3. Stops client connection, stops any old server
4. **Resets scene NetworkObjects** (clears FishNet init state, NOT positions)
5. Starts new server → scene objects re-initialize at current positions
6. Restores spawned objects (players) with saved state
7. Starts clienthost, rebuilds observers

**Scene objects (crates):** Stay where they are physically. Only FishNet networking state is reset.
**Spawned objects (players):** Destroyed and respawned with saved SyncVar state.
```

---

### HostMigrationPlayerSpawner

Handles player spawning with PUID-based repossession after migration.

**Location:** `Assets/FishNet.Transport.EOSNative/Migration/HostMigrationPlayerSpawner.cs`

```csharp
namespace FishNet.Transport.EOSNative
{
    public class HostMigrationPlayerSpawner : MonoBehaviour
    {
        // Configuration
        [SerializeField] private GameObject playerPrefab;

        // Repossession
        public void SpawnPlayerForPuid(ProductUserId puid);
        public void RepossessPlayerObject(ProductUserId puid, NetworkConnection conn);

        // Events
        public event Action<ProductUserId, NetworkObject> OnPlayerSpawned;
        public event Action<ProductUserId, NetworkObject> OnPlayerRepossessed;
    }
}
```

---

## Data Structures

### Connection
```csharp
public struct Connection
{
    public int ConnectionId;
    public ProductUserId UserId;
    public string UserIdString;
    public ConnectionState State;
    public DateTime ConnectedAt;
    public NetworkConnectionType Type; // NAT or Relay
}
```

### Channel (Enum)
```csharp
public enum Channel : byte
{
    Unreliable = 0,
    Reliable = 1
}
```

Maps to EOS:
- `Unreliable` → `PacketReliability.UnreliableUnordered`
- `Reliable` → `PacketReliability.ReliableOrdered`

### ConnectionState (Enum)
```csharp
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting
}
```

---

## File Structure

```
Assets/FishNet.Transport.EOSNative/
├── CORE (10 files)
│   ├── EOSManager.cs              # SDK init, Tick, device login
│   ├── EOSConfig.cs               # ScriptableObject credentials
│   ├── EOSNativeTransport.cs      # Main transport + lobby API
│   ├── EOSServer.cs / EOSClient.cs / EOSClientHost.cs
│   ├── EOSPlayerRegistry.cs       # Player cache + local friends
│   ├── PacketFragmenter.cs        # >1170 byte packets
│   └── Connection.cs / LocalPacket.cs
│
├── Debug/ (2 files)
│   ├── EOSDebugSettings.cs        # ScriptableObject + DebugCategory enum
│   └── EOSDebugLogger.cs          # Centralized logging utility
│
├── UI (5 files)
│   ├── EOSNativeUI.cs             # F1 - main debug UI
│   ├── DebugUI/EOSVoiceDebugPanel.cs      # F3
│   ├── DebugUI/EOSNetworkDebugPanel.cs    # F4
│   └── EOSNetworkPlayer.cs
│
├── Lobbies/ (3 files) - EOSLobbyManager, EOSLobbyChatManager, LobbyData
├── Voice/ (3 files) - EOSVoiceManager, EOSVoicePlayer, FishNetVoicePlayer
├── Migration/ (4 files) - HostMigratable, HostMigrationManager, HostMigrationPlayerSpawner, HostMigrationTester
├── Social/ (10 files) - Friends, Presence, UserInfo, CustomInvites, Stats, Leaderboards, EOSMatchHistory, EOSRankedMatchmaking, RankedData
├── Storage/ (2 files) - EOSPlayerDataStorage, EOSTitleStorage
├── Party/ (1 file) - EOSPartyManager
├── Replay/ (9 files) - EOSReplayRecorder, EOSReplayPlayer, EOSReplayStorage, EOSReplayViewer, ReplayDataTypes, ReplayRecordable, ReplayGhost, ReplayMigration, EOSReplaySettings
├── AntiCheat/ (1 file) - EOSAntiCheatManager
├── EOSSpectatorMode.cs           # Spectator camera system
├── Editor/ (4 files) - EOSNativeTransportEditor, EOSNativeMenu, EOSSetupWizard, EOSDebugSettingsWindow
└── Demo/ (5 files) - PlayerBall, NetworkPhysicsObject, PlayerSpawner, etc.
```

---

## Social Classes

### EOSStats
Player statistics tracking. Requires stats defined in Developer Portal.

**Location:** `Assets/FishNet.Transport.EOSNative/Social/EOSStats.cs`

```csharp
public class EOSStats : MonoBehaviour
{
    public static EOSStats Instance { get; }

    // Ingest stats
    public Task<Result> IngestStatAsync(string statName, int amount);
    public Task<Result> IngestStatsAsync(params (string name, int amount)[] stats);

    // Query stats
    public Task<(Result, List<StatData>)> QueryMyStatsAsync(params string[] statNames);
    public Task<(Result, List<StatData>)> QueryStatsAsync(ProductUserId userId, params string[] statNames);
}
```

### EOSLeaderboards
Leaderboard rankings. Requires leaderboards defined in Developer Portal.

**Location:** `Assets/FishNet.Transport.EOSNative/Social/EOSLeaderboards.cs`

```csharp
public class EOSLeaderboards : MonoBehaviour
{
    public static EOSLeaderboards Instance { get; }

    public Task<(Result, List<LeaderboardEntry>)> QueryRanksAsync(string leaderboardId, int maxResults = 10);
    public Task<(Result, LeaderboardEntry)> QueryMyScoreAsync(string leaderboardId);
}
```

### EOSFriends / EOSPresence / EOSUserInfo
Epic Account social features. Requires Epic Account login (not DeviceID).

**Location:** `Assets/FishNet.Transport.EOSNative/Social/`

```csharp
// Friends list
public class EOSFriends : MonoBehaviour
{
    public IReadOnlyList<FriendData> Friends { get; }
    public Task QueryFriendsAsync();
}

// Online status
public class EOSPresence : MonoBehaviour
{
    public Task SetPresenceAsync(Status status, string richText = null);
    public Task<(Result, PresenceInfo)> QueryPresenceAsync(EpicAccountId userId);
}

// Player profiles
public class EOSUserInfo : MonoBehaviour
{
    public Task<(Result, UserData)> QueryUserInfoAsync(EpicAccountId userId);
}
```

### EOSCustomInvites
Cross-platform game invitations.

**Location:** `Assets/FishNet.Transport.EOSNative/Social/EOSCustomInvites.cs`

```csharp
public class EOSCustomInvites : MonoBehaviour
{
    public static EOSCustomInvites Instance { get; }
    public IReadOnlyDictionary<string, InviteData> PendingInvites { get; }

    public void SetLobbyPayload();  // Uses current lobby join code
    public Task<Result> SendInviteAsync(ProductUserId recipient);
    public void AcceptInvite(string inviteId);
    public void RejectInvite(string inviteId);
}
```

---

## Storage Classes

### EOSPlayerDataStorage
Cloud saves (400MB per player). Works with DeviceID.

**Location:** `Assets/FishNet.Transport.EOSNative/Storage/EOSPlayerDataStorage.cs`

```csharp
public class EOSPlayerDataStorage : MonoBehaviour
{
    public static EOSPlayerDataStorage Instance { get; }

    public Task<Result> WriteFileAsync(string filename, string content);
    public Task<(Result, string)> ReadFileAsStringAsync(string filename);
    public Task<(Result, List<FileMetadata>)> QueryFileListAsync();
    public Task<Result> DeleteFileAsync(string filename);
    public long GetTotalStorageUsed();
}
```

### EOSTitleStorage
Read-only game config files. Configure in Developer Portal.

**Location:** `Assets/FishNet.Transport.EOSNative/Storage/EOSTitleStorage.cs`

```csharp
public class EOSTitleStorage : MonoBehaviour
{
    public static EOSTitleStorage Instance { get; }

    public Task<(Result, string)> ReadFileAsync(string filename);
    public Task<(Result, List<FileMetadata>)> QueryFileListAsync();
}
```

---

## Moderation Classes

### EOSReports
Player reporting for cheating, abuse, etc.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSReports.cs`

```csharp
public class EOSReports : MonoBehaviour
{
    public static EOSReports Instance { get; }

    public Task<Result> ReportPlayerAsync(ProductUserId target, PlayerReportsCategory category, string message = null);
    public Task<Result> ReportCheatingAsync(ProductUserId target, string details = null);
    public Task<Result> ReportSpammingAsync(ProductUserId target);
}
```

### EOSSanctions
Query player bans/sanctions.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSSanctions.cs`

```csharp
public class EOSSanctions : MonoBehaviour
{
    public static EOSSanctions Instance { get; }

    public Task<bool> IsPlayerBannedAsync(ProductUserId target);
    public Task<(Result, List<Sanction>)> QuerySanctionsAsync(ProductUserId target);
}
```

---

## Platform Classes

### EOSPlatformHelper
Platform detection and crossplay utilities.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSPlatformHelper.cs`

```csharp
public static class EOSPlatformHelper
{
    public static bool IsQuest { get; }
    public static bool IsAndroid { get; }
    public static bool IsIOS { get; }
    public static bool IsWindows { get; }
    public static bool IsMobile { get; }

    public static string GetPlatformId();
    public static string[] GetCrossplayPlatformIds(bool includePC, bool includeMobile);
}
```

### EOSDedicatedServer
Headless server support with command-line arguments.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSDedicatedServer.cs`

```csharp
public class EOSDedicatedServer : MonoBehaviour
{
    public bool IsHeadless { get; }
    public int Port { get; }
    public string LobbyCode { get; }

    // CLI args: -port 7777 -lobby ABC123 -gamemode deathmatch
    public void ParseCommandLineArgs();
    public Task StartServerAsync();
}
```

### EOSPlayerRegistry
Persistent PUID→DisplayName cache using PlayerPrefs.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSPlayerRegistry.cs`

```csharp
public class EOSPlayerRegistry : MonoBehaviour
{
    public static EOSPlayerRegistry Instance { get; }

    public void RegisterPlayer(ProductUserId puid, string displayName);
    public string GetDisplayName(ProductUserId puid);
    public bool IsKnownPlayer(ProductUserId puid);
    public List<string> GetAllKnownPlayers();
}
```

### EOSMetrics / EOSAchievements
Session telemetry and achievements.

**Location:** `Assets/FishNet.Transport.EOSNative/`

```csharp
public class EOSMetrics : MonoBehaviour
{
    public static EOSMetrics Instance { get; }
    public bool IsSessionActive { get; }
    public TimeSpan SessionDuration { get; }

    public void BeginSession(string displayName, string serverIp = null);
    public void EndSession();
}

public class EOSAchievements : MonoBehaviour
{
    public static EOSAchievements Instance { get; }
    public int UnlockedCount { get; }
    public int TotalAchievements { get; }

    public Task RefreshAsync();
    public bool IsUnlocked(string achievementId);
    public float GetProgress(string achievementId);
    public Task<Result> UnlockAchievementAsync(string achievementId);
}
```

---

## Editor Classes

### EOSNativeMenu
Tools menu items.

**Location:** `Assets/FishNet.Transport.EOSNative/Editor/EOSNativeMenu.cs`

```csharp
public static class EOSNativeMenu
{
    [MenuItem("Tools/FishNet EOS Native/Setup Scene")]
    public static void SetupScene();

    [MenuItem("Tools/FishNet EOS Native/Select Config")]
    public static void SelectConfig();

    [MenuItem("Tools/FishNet EOS Native/Create New Config")]
    public static void CreateNewConfig();

    [MenuItem("Tools/FishNet EOS Native/Validate Setup")]
    public static void ValidateSetup();
}
```

### EOSSetupWizard
Step-by-step credential configuration window.

**Location:** `Assets/FishNet.Transport.EOSNative/Editor/EOSSetupWizard.cs`

```csharp
public class EOSSetupWizard : EditorWindow
{
    [MenuItem("Tools/FishNet EOS Native/Setup Wizard")]
    public static void ShowWindow();

    // Step-by-step sections with tooltips:
    // 1. Product Settings (ProductId, SandboxId, DeploymentId)
    // 2. Client Credentials (ClientId, ClientSecret)
    // 3. Encryption Key (with Generate Random Key button)
    // 4. Optional Settings (DisplayName, IsServer, TickBudget)
}
```

---

## Demo Classes

### PlayerBall
Networked player with WASD movement and jumping. Uses PhysicsNetworkTransform for sync.

**Location:** `Assets/FishNet.Transport.EOSNative/Demo/PlayerBall.cs`

```csharp
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(EOSNetworkPlayer))]
public class PlayerBall : NetworkBehaviour
{
    // Movement settings
    [SerializeField] private float _acceleration = 25f;
    [SerializeField] private float _maxSpeed = 12f;
    [SerializeField] private float _jumpForce = 8f;

    // Synced color (server assigns random)
    private readonly SyncVar<Color> _playerColor = new();

    // Only owner reads input and runs physics
    // PhysicsNetworkTransform handles sync to other clients
    // Players can push each other (non-kinematic rigidbodies)
}
```

**Key design:** Owner runs physics locally, PhysicsNetworkTransform syncs position via spring forces. No input syncing needed.

### NetworkPhysicsObject (Legacy)
Pushable objects with ownership transfer on collision. Consider using PhysicsNetworkTransform instead.

**Location:** `Assets/FishNet.Transport.EOSNative/Demo/NetworkPhysicsObject.cs`

```csharp
public class NetworkPhysicsObject : NetworkBehaviour
{
    [SerializeField] private bool _stealOwnershipOnCollision = true;
    [SerializeField] private float _minCollisionForce = 0.5f;
    [SerializeField] private float _ownershipCooldown = 0.25f;

    // Forces non-kinematic so physics works on all clients
    // ServerRpc-based ownership transfer on collision with player
}
```

---

### PhysicsNetworkTransform (Recommended)
Spring-based physics sync that allows natural physics interactions without ownership swapping.

**Credit:** DrewMileham (original method), cometmb.pkg (Mirror implementation)

**Location:** `Assets/FishNet.Transport.EOSNative/Demo/PhysicsNetworkTransform.cs`

```csharp
public class PhysicsNetworkTransform : NetworkBehaviour
{
    public Rigidbody rigidbodyToTrack;

    // Modes
    public PhysicsNetworkTransformMode mode;  // Physics, VisualTweened, Visual

    // Spring settings (mass-aware, tuned defaults)
    public float positionSpringFrequency = 4f;
    public float positionDampingRatio = 0.65f;
    public float rotationSpringFrequency = 5.5f;
    public float rotationDampingRatio = 0.55f;

    // Distance-based LOD
    public bool useDistanceBasedMode = false;
    public float physicsDistance = 5f;    // Below: Physics mode
    public float tweenedDistance = 15f;   // Below: Tweened, Above: Visual
}
```

**Why it's better than ownership swapping:**
- Objects respond to local physics (can be pushed, grabbed, thrown)
- No ownership transfer latency
- Mass-aware springs (heavy and light objects both work)
- Bandwidth-efficient (half-float positions, smallest-three quaternion compression)
- Distance-based LOD (expensive Physics mode only for nearby objects)

**Authority handling:**
```csharp
// True for: owner client, OR server for server-owned objects
private bool HasAuthority => IsOwner || (IsServerInitialized && (Owner == null || !Owner.IsActive));
```

**Usage:** Add to any rigidbody, assign `rigidbodyToTrack` (auto-assigns if not set), done!

**For players vs crates:**
- Players: Owner runs physics, others see spring-synced position
- Crates (server-owned): Server is authority, all clients see spring-synced position
- Both can be pushed by players - springs guide back to networked position

---

## Party System

### EOSPartyManager
Persistent party groups that follow the leader across games.

**Location:** `Assets/FishNet.Transport.EOSNative/Party/EOSPartyManager.cs`

```csharp
public class EOSPartyManager : MonoBehaviour
{
    public static EOSPartyManager Instance { get; }

    // State
    public bool IsInParty { get; }
    public bool IsLeader { get; }
    public string PartyCode { get; }
    public List<PartyMember> Members { get; }

    // Configuration
    public PartyFollowMode FollowMode { get; set; }
    public PartyFullLobbyBehavior FullLobbyBehavior { get; set; }
    public PartyPersistence Persistence { get; set; }

    // Operations
    public Task CreatePartyAsync(string name, int maxSize);
    public Task JoinPartyAsync(string partyCode);
    public Task LeavePartyAsync();
    public Task InviteToPartyAsync(ProductUserId puid);
    public Task PromoteToLeaderAsync(ProductUserId puid);
    public Task KickMemberAsync(ProductUserId puid);
    public Task LeaderJoinGameAsync(string lobbyCode);
    public Task FollowLeaderAsync();
    public void StartReadyCheck(string gameCode);
    public Task RespondToReadyCheckAsync(bool ready);
    public Task SendPartyChatAsync(string message);

    // Events
    public event Action<PartyMember> OnMemberJoined;
    public event Action<ProductUserId> OnMemberLeft;
    public event Action<ProductUserId, ProductUserId> OnLeaderChanged;
    public event Action<string> OnLeaderJoinedGame;
    public event Action<FollowRequest> OnFollowRequested;
    public event Action<ReadyCheckData> OnReadyCheckStarted;
    public event Action<bool> OnReadyCheckCompleted;
}
```

---

## Ranked Matchmaking

### EOSRankedMatchmaking
Skill-based matchmaking with multiple rating algorithms.

**Location:** `Assets/FishNet.Transport.EOSNative/Social/EOSRankedMatchmaking.cs`

```csharp
public class EOSRankedMatchmaking : MonoBehaviour
{
    public static EOSRankedMatchmaking Instance { get; }

    // Rating info
    public int CurrentRating { get; }
    public RankTier CurrentTier { get; }
    public RankDivision CurrentDivision { get; }
    public bool IsPlaced { get; }
    public RankedPlayerData PlayerData { get; }

    // Configuration
    public void SetAlgorithm(RatingAlgorithm algorithm);
    public void SetTierDisplayMode(TierDisplayMode mode);

    // Operations
    public Task<(Result, LobbyData)> FindRankedMatchAsync(string gameMode);
    public Task<(Result, LobbyData)> HostRankedLobbyAsync(string gameMode);
    public Task<(Result, LobbyData, bool didHost)> FindOrHostRankedMatchAsync(string gameMode);
    public Task<RatingChange> RecordMatchResultAsync(MatchOutcome outcome, int opponentRating);
    public string GetCurrentRankDisplayName();

    // Events
    public event Action<RatingChange> OnRatingChanged;
    public event Action<RankTier, RankDivision> OnPromotion;
    public event Action<RankTier, RankDivision> OnDemotion;
    public event Action<int, RankTier, RankDivision> OnPlacementCompleted;
    public event Action<LobbyData> OnMatchFound;
}

public enum RatingAlgorithm { ELO, Glicko2, SimpleMMR }
public enum TierDisplayMode { SixTier, EightTier, NumbersOnly }
public enum RankTier { Bronze, Silver, Gold, Platinum, Diamond, Champion, Iron, Grandmaster }
public enum RankDivision { I, II, III, IV }
```

---

## Match History

### EOSMatchHistory
Track games played with participants and outcomes.

**Location:** `Assets/FishNet.Transport.EOSNative/Social/EOSMatchHistory.cs`

```csharp
public class EOSMatchHistory : MonoBehaviour
{
    public static EOSMatchHistory Instance { get; }

    // Operations
    public void StartMatch(string gameMode, string map);
    public void AddParticipant(ProductUserId puid, string name, int team);
    public void UpdateLocalScore(int score, int team);
    public void UpdateParticipantScore(ProductUserId puid, int score);
    public void EndMatch(MatchOutcome outcome, ProductUserId winner = null);

    // Queries
    public List<MatchRecord> GetRecentMatches(int count);
    public (int wins, int losses, int draws, int total) GetLocalStats();
}
```

---

## Spectator Mode

### EOSSpectatorMode
Watch games without participating.

**Location:** `Assets/FishNet.Transport.EOSNative/EOSSpectatorMode.cs`

```csharp
public class EOSSpectatorMode : MonoBehaviour
{
    public static EOSSpectatorMode Instance { get; }

    // State
    public bool IsSpectating { get; }
    public NetworkObject CurrentTarget { get; }

    // Operations
    public Task JoinAsSpectatorAsync(string lobbyCode);
    public void EnterSpectatorMode();
    public void ExitSpectatorMode();
    public void CycleTarget(int direction);
    public void SetTarget(NetworkObject target);
    public void ToggleFreeCamera();
    public string GetCurrentTargetName();
}
```

---

## Replay System

### EOSReplayRecorder
Records game sessions for playback.

**Location:** `Assets/FishNet.Transport.EOSNative/Replay/EOSReplayRecorder.cs`

```csharp
public class EOSReplayRecorder : MonoBehaviour
{
    public static EOSReplayRecorder Instance { get; }

    // Configuration
    public bool AutoRecord { get; set; }
    public float FrameRate { get; set; }
    public float MaxDuration { get; }

    // State
    public bool IsRecording { get; }
    public float Duration { get; }
    public float EstimatedSizeKB { get; }
    public bool IsApproachingLimit { get; }

    // Operations
    public void StartRecording(string matchId);
    public void RecordEvent(string eventType, string data);
    public Task<ReplayData> StopAndSaveAsync();

    // Events
    public event Action<string> OnRecordingStarted;
    public event Action<ReplayData> OnRecordingStopped;
    public event Action<int, float> OnFrameRecorded;
    public event Action<QualityWarning> OnQualityWarning;
    public event Action<float, float> OnDurationWarning;
    public event Action<string> OnAutoStopped;
}
```

### EOSReplayPlayer
Playback engine for recorded replays.

**Location:** `Assets/FishNet.Transport.EOSNative/Replay/EOSReplayPlayer.cs`

```csharp
public class EOSReplayPlayer : MonoBehaviour
{
    public static EOSReplayPlayer Instance { get; }

    // State
    public PlaybackState State { get; }
    public float CurrentTime { get; }
    public float TotalDuration { get; }
    public float PlaybackSpeed { get; set; }

    // Operations
    public void LoadReplay(ReplayData replay);
    public void Play();
    public void Pause();
    public void Stop();
    public void Seek(float time);
    public void SeekPercent(float percent);
    public List<NetworkObject> GetPlayerObjects();

    // Events
    public event Action<float> OnTimeChanged;
    public event Action<PlaybackState> OnStateChanged;
    public event Action OnReplayEnded;
}
```

### EOSReplayStorage
Manages local and cloud replay storage.

**Location:** `Assets/FishNet.Transport.EOSNative/Replay/EOSReplayStorage.cs`

```csharp
public class EOSReplayStorage : MonoBehaviour
{
    public static EOSReplayStorage Instance { get; }

    // Local storage
    public List<ReplayHeader> GetLocalReplays();
    public Task<ReplayData> LoadLocalAsync(string replayId);
    public void DeleteReplay(string replayId);

    // Cloud storage
    public Task UploadToCloudAsync(ReplayData replay);
    public Task<List<ReplayHeader>> GetCloudReplaysAsync();
    public Task<ReplayData> DownloadFromCloudAsync(string replayId);

    // Favorites
    public void ToggleFavorite(string replayId);
    public bool IsFavorite(string replayId);

    // Export/Import
    public Task<string> ExportReplayAsync(string replayId);
    public Task<bool> ImportReplayAsync(string filePath);
    public void OpenExportFolder();
}
```

### EOSReplayViewer
Combines playback with spectator camera.

**Location:** `Assets/FishNet.Transport.EOSNative/Replay/EOSReplayViewer.cs`

```csharp
public class EOSReplayViewer : MonoBehaviour
{
    public static EOSReplayViewer Instance { get; }

    // Operations
    public void StartViewing(ReplayData replay);
    public void StopViewing();
    public void TogglePlayPause();
    public void Seek(float time);
    public void SeekPercent(float percent);
    public void Skip(float seconds);
    public void SetSpeed(float speed);
    public void CycleSpeed();
    public void CycleTarget(int direction);

    // Events
    public event Action<ReplayHeader> OnViewingStarted;
    public event Action OnViewingStopped;
}
```

---

## Anti-Cheat

### EOSAntiCheatManager
Easy Anti-Cheat (EAC) integration.

**Location:** `Assets/FishNet.Transport.EOSNative/AntiCheat/EOSAntiCheatManager.cs`

```csharp
public class EOSAntiCheatManager : MonoBehaviour
{
    public static EOSAntiCheatManager Instance { get; }

    // Configuration
    public bool AutoStartSession { get; set; }

    // State
    public bool IsSessionActive { get; }
    public AntiCheatStatus Status { get; }

    // Operations
    public void BeginSession();
    public void EndSession();
    public IntPtr RegisterPeer(ProductUserId puid);
    public void UnregisterPeer(ProductUserId puid);
    public AntiCheatClientViolationType PollStatus();
    public void ReceiveMessageFromPeer(IntPtr peerHandle, byte[] data);
    public bool TryGetOutgoingMessage(out IntPtr peer, out byte[] data);

    // Events
    public event Action OnSessionStarted;
    public event Action OnSessionEnded;
    public event Action<AntiCheatClientViolationType, string> OnIntegrityViolation;
    public event Action<IntPtr, AntiCheatCommonClientAction, string> OnPeerActionRequired;
    public event Action<IntPtr, AntiCheatCommonClientAuthStatus> OnPeerAuthStatusChanged;
}

public enum AntiCheatStatus { NotInitialized, NotAvailable, Initialized, Protected, Violated, Error }
```

---

## Toast Notifications

### EOSToastManager
Non-intrusive popup notifications.

**Location:** `Assets/FishNet.Transport.EOSNative/UI/EOSToastManager.cs`

```csharp
public class EOSToastManager : MonoBehaviour
{
    public static EOSToastManager Instance { get; }

    // Configuration
    public ToastPosition Position { get; set; }
    public float DefaultDuration { get; set; }

    // Static methods
    public static void Info(string message, string title = null);
    public static void Success(string message, string title = null);
    public static void Warning(string message, string title = null);
    public static void Error(string message, string title = null);
    public static void ClearAll();
}

public enum ToastPosition { TopLeft, TopRight, BottomLeft, BottomRight }
```

---

## Constants

```csharp
public static class EOSConstants
{
    public const int MAX_PACKET_SIZE = 1170;        // EOS P2P limit
    public const int PACKET_HEADER_SIZE = 7;        // For fragmentation
    public const int CLIENT_HOST_ID = short.MaxValue; // 32767
    public const float DEFAULT_TIMEOUT = 25f;       // Connection timeout
    public const string DEFAULT_SOCKET_NAME = "FishNetEOS";
}
```
