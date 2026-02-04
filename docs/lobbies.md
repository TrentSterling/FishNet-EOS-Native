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

### With Options (Recommended)

Use `LobbyOptions` to configure your lobby. Works for both hosting and searching:

```csharp
var (result, lobby) = await transport.HostLobbyAsync(new LobbyOptions
{
    LobbyName = "Pro Players Only",
    GameMode = "competitive",
    Region = "us-east",
    MaxPlayers = 8,
    Password = "secret123"
});
```

Fluent style also works:

```csharp
var (result, lobby) = await transport.HostLobbyAsync(
    new LobbyOptions()
        .WithName("Pro Players Only")
        .WithGameMode("competitive")
        .WithRegion("us-east")
        .WithMaxPlayers(8)
        .WithPassword("secret123")
);
```

### LobbyOptions Fields

| Field | Used For | Description |
|-------|----------|-------------|
| `LobbyName` | Create | Display name for the lobby |
| `GameMode` | Both | Game mode filter/attribute |
| `Map` | Both | Map filter/attribute |
| `Region` | Both | Region filter/attribute |
| `MaxPlayers` | Both | Max capacity / capacity filter |
| `BucketId` | Both | Version/platform grouping |
| `Password` | Create | Password protection |
| `UseEosLobbyId` | Create | Use EOS-generated ID as code |
| `EnableVoice` | Create | Enable voice chat (default: true) |
| `AllowHostMigration` | Create | Allow host migration (default: true) |
| `MaxResults` | Search | Limit search results |
| `ExcludePasswordProtected` | Search | Only public lobbies |
| `OnlyAvailable` | Search | Only lobbies with space (default: true) |

### Fluent Builder Methods

All fluent methods return the options object for chaining:

| Method | Description |
|--------|-------------|
| `.WithName(string)` | Set lobby name |
| `.WithGameMode(string)` | Set game mode |
| `.WithMap(string)` | Set map |
| `.WithRegion(string)` | Set region |
| `.WithMaxPlayers(uint)` | Set max players |
| `.WithBucketId(string)` | Set bucket ID |
| `.WithPassword(string)` | Set password |
| `.WithVoice(bool)` | Enable/disable voice |
| `.WithHostMigration(bool)` | Enable/disable migration |
| `.WithEosLobbyId()` | Use EOS-generated code |
| `.ExcludePassworded()` | Exclude password-protected lobbies |
| `.ExcludeFull()` | Exclude full lobbies |
| `.WithMaxResults(uint)` | Limit search results |

### With EOS LobbyId as Code

Use the EOS-generated LobbyId instead of a custom code. Guarantees uniqueness - useful for features like chat history that key off the lobby code:

```csharp
var (result, lobby) = await transport.HostLobbyAsync(new LobbyOptions
{
    UseEosLobbyId = true,  // Use EOS-generated ID as the code
    LobbyName = "My Room",
    GameMode = "casual"
});

// lobby.JoinCode will be something like "a1b2c3d4e5f67890abcdef12"
// lobby.IsEosLobbyIdCode will be true
```

**When to use EOS LobbyId:**
- Chat history (no collisions with reused 4-digit codes)
- Invite links / deep links (not shared verbally)
- Internal systems that don't need human-readable codes

**When to use custom codes:**
- Verbal sharing ("Join lobby 1234!")
- Simple UI display
- Human-memorable codes

## Joining a Lobby

### By Code

```csharp
var (result, lobby) = await transport.JoinLobbyAsync("1234");

if (result == Result.Success)
{
    Debug.Log($"Joined {lobby.Name}");
}
```

### By Name

```csharp
var (result, lobby) = await transport.JoinLobbyByNameAsync("Pro Players Only");
```

## Quick Match

Finds an available lobby or creates one. This is the recommended way to implement "Play Now" functionality.

### Basic Quick Match

```csharp
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();

if (didHost)
    Debug.Log("Created new lobby");
else
    Debug.Log("Joined existing lobby");
```

### Quick Match with Filters

Use `LobbyOptions` to filter what lobbies to search for, and what settings to use if hosting:

```csharp
var options = new LobbyOptions
{
    GameMode = "deathmatch",
    Region = "us-east",
    MaxPlayers = 8
};

var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(options);
```

The same options object is used for:
1. **Searching** - Finds lobbies matching GameMode, Region, etc.
2. **Hosting** - If no match found, creates lobby with those same settings

Fluent style:

```csharp
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(
    new LobbyOptions()
        .WithGameMode("deathmatch")
        .WithRegion("us-east")
        .WithMaxPlayers(8)
        .ExcludeFull()
);
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
    Debug.Log($"{lobby.Name} ({lobby.Code})");
    Debug.Log($"  Players: {lobby.PlayerCount}/{lobby.MaxPlayers}");
    Debug.Log($"  Mode: {lobby.GameMode}");
}
```

### Search by Name

```csharp
// Search by exact name
var (result, lobbies) = await transport.SearchLobbiesByNameAsync("Pro Players Only", exactMatch: true);

// Search by name containing substring
var (result, lobbies) = await transport.SearchLobbiesByNameAsync("Pro", exactMatch: false);
```

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
| Attribute value length | 1000 chars |
| Voice participants | 64 (SDK 1.16+) |

## Events

```csharp
var lobby = EOSLobbyManager.Instance;

lobby.OnPlayerJoined += (puid, name) => { };
lobby.OnPlayerLeft += (puid) => { };
lobby.OnLobbyUpdated += (lobbyData) => { };
lobby.OnKicked += () => { };
```

## Legacy Classes

For advanced use cases, you can still use the specific option classes:

- `LobbyCreateOptions` - Create-only fields (auto-converts from `LobbyOptions`)
- `LobbySearchOptions` - Search-only fields (auto-converts from `LobbyOptions`)

`LobbyOptions` implicitly converts to either, so you rarely need these directly.
