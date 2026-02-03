# Architecture

Technical overview of FishNet EOS Native.

## The Stack

```
EOS Lobby (persistent layer)
├── Voice/RTC ──────── Survives host migration
├── Member presence ── Who's in session
├── Lobby attributes ─ Settings, chat messages
└── Host PUID ──────── P2P connection target
    │
    └── FishNet P2P Connection (transient layer)
        ├── Game state sync
        ├── NetworkObjects
        └── RPCs
```

## Core Components

### EOSManager

SDK initialization and lifecycle management.

```csharp
// Singleton, auto-created
EOSManager.Instance
```

Responsibilities:
- Initialize EOS SDK (once per process)
- Call `Tick()` every frame for callbacks
- Device ID login for anonymous auth
- Shutdown handling

### EOSNativeTransport

Main transport implementing FishNet's `Transport` base class.

```csharp
public class EOSNativeTransport : Transport
```

Responsibilities:
- P2P connection management
- Packet sending/receiving
- Lobby API surface (host, join, leave)
- Integration with FishNet NetworkManager

### EOSServer / EOSClient / EOSClientHost

Connection handlers for each role:

- **EOSServer**: Accepts connections, routes packets to clients
- **EOSClient**: Connects to host, sends/receives packets
- **EOSClientHost**: Host acting as both server and client (ID: 32767)

### EOSLobbyManager

Lobby operations and state management.

```csharp
EOSLobbyManager.Instance
```

Responsibilities:
- Create/join/leave lobbies
- Attribute management
- Member tracking
- Search operations

### PacketFragmenter

Handles packets larger than P2P limit (1170 bytes).

```csharp
// Automatic - no direct usage needed
```

Splits large packets into fragments, reassembles on receive.

## File Structure

```
Assets/FishNet.Transport.EOSNative/
├── CORE (10 files)
│   ├── EOSManager.cs              # SDK init, Tick, device login
│   ├── EOSConfig.cs               # ScriptableObject credentials
│   ├── EOSNativeTransport.cs      # Main transport + lobby API
│   ├── EOSServer.cs               # Server connection handling
│   ├── EOSClient.cs               # Client connection handling
│   ├── EOSClientHost.cs           # Host-as-client handling
│   ├── EOSPlayerRegistry.cs       # Player cache + local friends
│   ├── PacketFragmenter.cs        # >1170 byte packets
│   ├── Connection.cs              # Connection state
│   └── LocalPacket.cs             # Packet wrapper
│
├── Debug/
│   ├── EOSDebugSettings.cs        # Debug configuration
│   └── EOSDebugLogger.cs          # Centralized logging
│
├── UI/
│   ├── EOSNativeUI.cs             # F1 debug UI
│   ├── DebugUI/EOSVoiceDebugPanel.cs
│   └── DebugUI/EOSNetworkDebugPanel.cs
│
├── Lobbies/
│   ├── EOSLobbyManager.cs
│   ├── EOSLobbyChatManager.cs
│   └── LobbyData.cs
│
├── Voice/
│   ├── EOSVoiceManager.cs
│   ├── EOSVoicePlayer.cs
│   └── FishNetVoicePlayer.cs
│
├── Migration/
│   ├── HostMigratable.cs
│   ├── HostMigrationManager.cs
│   ├── HostMigrationPlayerSpawner.cs
│   └── HostMigrationTester.cs
│
├── Social/
│   ├── EOSFriends.cs
│   ├── EOSPresence.cs
│   ├── EOSCustomInvites.cs
│   ├── EOSMatchHistory.cs
│   └── EOSRankedMatchmaking.cs
│
├── Storage/
│   ├── EOSPlayerDataStorage.cs
│   └── EOSTitleStorage.cs
│
├── Party/
│   └── EOSPartyManager.cs
│
├── Replay/
│   ├── EOSReplayRecorder.cs
│   ├── EOSReplayPlayer.cs
│   ├── EOSReplayStorage.cs
│   └── EOSReplayViewer.cs
│
├── AntiCheat/
│   └── EOSAntiCheatManager.cs
│
└── Editor/
    ├── EOSNativeTransportEditor.cs
    ├── EOSNativeMenu.cs
    ├── EOSSetupWizard.cs
    └── EOSDebugSettingsWindow.cs
```

## Key Constants

| Constant | Value | Notes |
|----------|-------|-------|
| P2P Max Packet | 1170 bytes | Use PacketFragmenter for larger |
| Packet Header | 7 bytes | Reserved for internal use |
| Client Host ID | 32767 | `short.MaxValue` |
| Connection Timeout | 25 seconds | |
| Lobby Max Members | 64 | EOS limit |

## Namespace

All code uses:

```csharp
namespace FishNet.Transport.EOSNative
```

**Important**: Never use `PlayEveryWare` namespace. Use `Epic.OnlineServices` directly.

## Platform Defines

```csharp
#if UNITY_ANDROID
    // Android-specific code
#endif

#if UNITY_STANDALONE_WIN
    // Windows-specific code
#endif

#if UNITY_EDITOR
    // Editor-only code
#endif
```

## Async Pattern

Prefer async/await with `TaskCompletionSource<T>`:

```csharp
public async Task<(Result result, LobbyData lobby)> JoinLobbyAsync(string code)
{
    var tcs = new TaskCompletionSource<(Result, LobbyData)>();

    // Start EOS operation
    _lobbyInterface.JoinLobby(options, null, (ref JoinLobbyCallbackInfo info) =>
    {
        tcs.SetResult((info.ResultCode, lobbyData));
    });

    return await tcs.Task;
}
```

## Error Handling

Always check EOS results:

```csharp
var result = await SomeEOSOperation();

if (result != Result.Success)
{
    EOSDebugLogger.Log(DebugCategory.Lobby, $"Operation failed: {result}");
    return;
}
```

## SDK Gotchas

### Initialization

- `PlatformInterface.Initialize` only once per process
- Check for `Result.AlreadyConfigured` (normal in Editor)
- Cannot reinitialize after `Shutdown()`

### Tick

- Must call `Tick()` every frame
- Callbacks never fire without it

### P2P

- Max 1170 bytes per packet
- Both sides need matching `SocketId.SocketName`
- Server must `AcceptConnection` for incoming requests

### DeviceID

- No visible display names
- Generate "AngryPanda42" style names from PUID hash
