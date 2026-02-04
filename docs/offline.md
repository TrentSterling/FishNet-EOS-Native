# Offline Mode

Run your game without EOS - perfect for singleplayer, testing, or when internet is unavailable.

## Overview

Offline mode provides a local transport that routes packets directly between server and client without any network. All FishNet features work normally:

- NetworkObjects spawn and sync
- RPCs execute locally
- SyncVars update instantly
- No latency (zero network round-trip)

## Quick Start

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Start offline - no EOS login needed!
transport.StartOffline();

// Your game works normally with FishNet
// Spawn objects, call RPCs, sync vars - all local
```

## Manual Offline Mode

### Starting Offline

```csharp
// Explicitly start in offline mode
transport.StartOffline();

// Check if running offline
if (transport.IsOfflineMode)
{
    Debug.Log("Running in offline/singleplayer mode");
}
```

### Stopping Offline Mode

```csharp
// Stop offline mode
transport.StopOffline();

// Or use standard shutdown
transport.Shutdown();
```

## Automatic Fallback

Enable automatic fallback to offline mode when EOS is unavailable:

```csharp
// In inspector or code
transport.OfflineFallback = true;

// Now if EOS init/login fails, game automatically runs offline
var (result, lobby) = await transport.HostLobbyAsync();
// If EOS failed, this hosts locally in offline mode
```

### Fallback Triggers

Offline fallback activates when:
- EOS SDK fails to initialize
- EOS login fails (no internet, invalid credentials)
- EOS services are unavailable

### Detecting Fallback

```csharp
// Check if we fell back to offline
if (transport.IsOfflineMode && transport.OfflineFallback)
{
    // Show "Playing Offline" indicator to user
    ShowOfflineIndicator();
}
```

## Feature Availability

| Feature | Online | Offline |
|---------|--------|---------|
| NetworkObjects | Yes | Yes |
| RPCs | Yes | Yes |
| SyncVars | Yes | Yes |
| Host Migration | Yes | N/A (no network) |
| Lobbies | Yes | No |
| Voice Chat | Yes | No |
| Friends | Yes | No |
| Leaderboards | Yes | No |
| Cloud Storage | Yes | No |
| Achievements | Yes | No |
| Matchmaking | Yes | No |
| Replays (local) | Yes | Yes |

## Use Cases

### Singleplayer Campaign

```csharp
public class GameManager : MonoBehaviour
{
    async void StartSingleplayer()
    {
        var transport = GetComponent<EOSNativeTransport>();

        // Skip EOS entirely for singleplayer
        transport.StartOffline();

        // Spawn player, load level, etc.
        NetworkManager.ServerManager.Spawn(playerPrefab);
    }
}
```

### Testing Without EOS

```csharp
// Great for unit tests or quick iteration
[Test]
public void TestPlayerSpawn()
{
    transport.StartOffline();

    var player = NetworkManager.ServerManager.Spawn(playerPrefab);
    Assert.NotNull(player);

    transport.StopOffline();
}
```

### Graceful Degradation

```csharp
public class ConnectionManager : MonoBehaviour
{
    [SerializeField] private EOSNativeTransport transport;

    async void Start()
    {
        // Enable fallback
        transport.OfflineFallback = true;

        // Try to connect online
        var result = await EOSManager.Instance.LoginAsync();

        if (transport.IsOfflineMode)
        {
            // EOS failed, we're running offline
            ShowMessage("Playing offline - online features unavailable");
        }
        else
        {
            // EOS succeeded, full features available
            ShowMessage("Connected to EOS");
        }
    }
}
```

## Transitioning Online

You can transition from offline to online mode:

```csharp
async void GoOnline()
{
    // Stop offline mode
    transport.StopOffline();

    // Initialize and login to EOS
    await EOSManager.Instance.InitializeAsync();
    await EOSManager.Instance.LoginAsync();

    // Now host a real lobby
    var (result, lobby) = await transport.HostLobbyAsync();
}
```

## Technical Details

### How It Works

1. `StartOffline()` creates `EOSOfflineServer` and `EOSOfflineClient`
2. Both use in-memory queues instead of network sockets
3. `SendToServer()` enqueues directly to server's incoming queue
4. `SendToClient()` enqueues directly to client's incoming queue
5. `IterateIncoming()` processes queues like normal network packets

### Performance

- Zero network latency
- No packet serialization overhead for network
- Memory-only packet routing
- Same FishNet tick rate as online

### Connection ID

In offline mode, the local client uses `NetworkConnection.SIMULATED_CLIENTID_VALUE` as its connection ID, which is FishNet's standard value for local/simulated connections.
