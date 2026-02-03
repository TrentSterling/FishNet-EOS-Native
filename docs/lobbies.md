# Lobbies

Lobbies are the core of EOS multiplayer. They manage player presence, attributes, and serve as the connection point for P2P networking.

## Architecture

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

## Creating a Lobby

### Simple Host

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Auto-generates 4-digit code (default)
var (result, lobby) = await transport.HostLobbyAsync();
Debug.Log($"Code: {lobby.Code}"); // e.g., "7294"
```

### With Custom Code

Lobby codes can be any string - not limited to 4 digits:

```csharp
// 4-digit code (easy to share verbally)
var (result, lobby) = await transport.HostLobbyAsync("1234");

// Alphanumeric code
var (result, lobby) = await transport.HostLobbyAsync("ABC123");

// Any custom string
var (result, lobby) = await transport.HostLobbyAsync("my-awesome-lobby");
```

### With Full Options

```csharp
var (result, lobby) = await transport.HostLobbyAsync(new LobbyCreateOptions
{
    LobbyName = "Pro Players Only",
    GameMode = "competitive",
    Region = "us-east",
    MaxPlayers = 8,
    Password = "secret123",
    IsPublic = true
});
```

## Joining a Lobby

### By Code

```csharp
var (result, lobby) = await transport.JoinLobbyAsync("1234");

if (result == Result.Success)
{
    Debug.Log($"Joined {lobby.Name}");
}
```

### Quick Match

Finds an available lobby or creates one:

```csharp
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();

if (didHost)
{
    Debug.Log("Created new lobby");
}
else
{
    Debug.Log("Joined existing lobby");
}
```

## Searching for Lobbies

```csharp
var options = new LobbySearchOptions()
    .WithGameMode("ranked")
    .WithRegion("us-east")
    .ExcludePassworded()
    .WithMaxResults(20);

var (result, lobbies) = await transport.SearchLobbiesAsync(options);

foreach (var lobby in lobbies)
{
    Debug.Log($"{lobby.Name} ({lobby.Code})");
    Debug.Log($"  Players: {lobby.PlayerCount}/{lobby.MaxPlayers}");
    Debug.Log($"  Mode: {lobby.GameMode}");
}
```

### Search Options

| Method | Description |
|--------|-------------|
| `.WithGameMode(string)` | Filter by game mode |
| `.WithRegion(string)` | Filter by region |
| `.WithMinPlayers(int)` | Minimum player count |
| `.WithMaxPlayers(int)` | Maximum player count |
| `.ExcludePassworded()` | Only show public lobbies |
| `.ExcludeFull()` | Only show lobbies with space |
| `.WithMaxResults(int)` | Limit result count |

## Lobby Attributes

Custom data attached to lobbies.

### Setting Attributes (Host Only)

```csharp
var lobby = EOSLobbyManager.Instance;

lobby.SetAttribute("map", "dust2");
lobby.SetAttribute("difficulty", 3);
lobby.SetAttribute("ranked", true);
```

### Reading Attributes

```csharp
string map = lobby.GetAttribute<string>("map");
int difficulty = lobby.GetAttribute<int>("difficulty");
bool ranked = lobby.GetAttribute<bool>("ranked");
```

## Leaving a Lobby

```csharp
await transport.LeaveLobbyAsync();
```

## State Checking

```csharp
if (transport.IsInLobby)
{
    Debug.Log($"In lobby: {transport.CurrentLobby.Code}");
}

if (transport.IsLobbyOwner)
{
    Debug.Log("I am the host");
}
```

## Service Limits

| Limit | Value |
|-------|-------|
| Max players per lobby | 64 |
| Max lobbies per user | 16 |
| Create/Join rate | 30/min |
| Attribute updates | 100/min |

## Events

```csharp
var lobby = EOSLobbyManager.Instance;

lobby.OnPlayerJoined += (puid, name) => { };
lobby.OnPlayerLeft += (puid) => { };
lobby.OnLobbyUpdated += (lobbyData) => { };
lobby.OnKicked += () => { };
```
