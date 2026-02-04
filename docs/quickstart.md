# Quick Start

Get FishNet EOS Native running in your project.

## Installation

1. Add `EOSNativeTransport` component to a GameObject in your scene
2. The following components are **auto-created** when you enter Play Mode:
   - NetworkManager
   - EOSManager
   - EOSLobbyManager
   - EOSVoiceManager
   - HostMigrationManager
3. Configure credentials via `Tools > FishNet EOS Native > Setup Wizard`
4. Enter Play Mode → auto-initializes

## Basic Usage

### Hosting a Lobby

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Simple host (generates random 4-digit code, or pass any string)
var (result, lobby) = await transport.HostLobbyAsync();
var (result, lobby) = await transport.HostLobbyAsync("1234");

// Host with options (recommended)
var (result, lobby) = await transport.HostLobbyAsync(new LobbyOptions
{
    LobbyName = "My Room",
    GameMode = "deathmatch",
    MaxPlayers = 8
});

// Fluent style
var (result, lobby) = await transport.HostLobbyAsync(
    new LobbyOptions()
        .WithName("My Room")
        .WithGameMode("deathmatch")
        .WithMaxPlayers(8)
);
```

### Joining a Lobby

```csharp
// Join by code
var (result, lobby) = await transport.JoinLobbyAsync("1234");

// Quick match - finds a lobby or hosts one
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();

// Quick match with filters (same options used for search AND host fallback)
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(
    new LobbyOptions()
        .WithGameMode("deathmatch")
        .WithRegion("us-east")
        .ExcludeFull()
);
```

### Leaving

```csharp
await transport.LeaveLobbyAsync();
```

### Checking State

```csharp
if (transport.IsInLobby)
{
    Debug.Log("Currently in a lobby");
}

if (transport.IsLobbyOwner)
{
    Debug.Log("I am the host");
}
```

## Searching for Lobbies

```csharp
var options = new LobbyOptions()
    .WithGameMode("ranked")
    .WithRegion("us-east")
    .ExcludePassworded()
    .WithMaxResults(20);

var (result, lobbies) = await transport.SearchLobbiesAsync(options);

foreach (var lobby in lobbies)
{
    Debug.Log($"{lobby.Name} - {lobby.PlayerCount}/{lobby.MaxPlayers}");
}
```

## Offline Mode (Singleplayer)

Run without EOS for singleplayer or testing:

```csharp
// No EOS login needed - works immediately
transport.StartOffline();

// All FishNet features work normally
// NetworkObjects, RPCs, SyncVars - all local

// Check mode
if (transport.IsOfflineMode) { }

// Auto-fallback when EOS unavailable
transport.OfflineFallback = true;
```

See [Offline Mode](offline.md) for details.

## Testing with ParrelSync

1. Open ParrelSync window: `Tools > ParrelSync`
2. Create a clone
3. In Main Editor: Host a lobby
4. In Clone: Join with the same code
5. Both should show connected within seconds

## Next Steps

- [Setup Wizard](setup.md) - Configure EOS credentials
- [Lobbies](lobbies.md) - Deep dive into lobby features
- [Offline Mode](offline.md) - Singleplayer without EOS
- [Voice Chat](voice.md) - Enable voice communication
