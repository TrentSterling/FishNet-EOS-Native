# Discord Rich Presence

Show your game's lobby status directly in Discord. Zero external dependencies - uses raw named pipes to communicate with the Discord client.

## Quick Start

```csharp
var discord = EOSDiscordPresence.Instance;

// Set simple presence
discord.SetPresence("In Menu");

// Show lobby info
discord.SetLobbyPresence("In Lobby", "ABC123", 4, 8);

// Clear presence
discord.ClearPresence();
```

## Setup

1. Go to [discord.com/developers](https://discord.com/developers/applications)
2. Click "New Application" and name it (e.g., "My Game")
3. Copy the **Application ID** from the General Information page
4. Add EOSDiscordPresence component to your game
5. Paste the Application ID in the Inspector

### Adding Images

1. In Discord Developer Portal, go to **Rich Presence > Art Assets**
2. Upload images (min 512x512 recommended)
3. Give them keys like `game_logo`, `status_playing`
4. Use these keys in `LargeImageKey` and `SmallImageKey`

## Configuration

| Property | Description |
|----------|-------------|
| `ApplicationId` | Your Discord Application ID |
| `LargeImageKey` | Default large image key |
| `SmallImageKey` | Default small image key |
| `AutoUpdate` | Automatically sync with lobby state |
| `UpdateInterval` | Refresh interval in seconds (default: 15) |

## Auto-Integration

When `AutoUpdate` is enabled, presence automatically updates:

- **Join Lobby**: Shows lobby code and player count
- **Leave Lobby**: Shows "In Menu"
- **Member Changes**: Updates party size in real-time

No code needed - just enable AutoUpdate and set your Application ID!

## Manual Control

### Simple Presence

```csharp
// Just details
discord.SetPresence("In Menu");

// Details + state
discord.SetPresence("Playing Deathmatch", "Score: 15-12");
```

### Lobby Presence

```csharp
discord.SetLobbyPresence(
    details: "In Lobby",
    lobbyCode: "ABC123",
    currentPlayers: 4,
    maxPlayers: 8
);
```

Shows in Discord as:
```
My Game
In Lobby
Lobby: ABC123
[====    ] 4 of 8
```

### Full Control

```csharp
discord.UpdatePresence(
    details: "Ranked Match",
    state: "Round 5 of 10",
    partyId: "lobby123",
    partySize: 4,
    partyMax: 8,
    startTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
    largeImageKey: "game_logo",
    largeImageText: "FishNet EOS Native",
    smallImageKey: "status_ranked",
    smallImageText: "Ranked Mode"
);
```

### Clear Presence

```csharp
discord.ClearPresence();
```

## Connection Management

```csharp
// Check connection
if (discord.IsConnected) { }

// Manual connect (auto-connects on Start if ApplicationId set)
discord.Connect();

// Disconnect
discord.Disconnect();
```

## Events

```csharp
discord.OnConnected += () => {
    Debug.Log("Discord connected!");
};

discord.OnDisconnected += () => {
    Debug.Log("Discord disconnected");
};

discord.OnError += (message) => {
    Debug.LogWarning($"Discord error: {message}");
};
```

## Integration Examples

### With Match History

```csharp
EOSMatchHistory.Instance.OnMatchStarted += (matchId, mode, map) => {
    discord.UpdatePresence(
        details: $"Playing {mode}",
        state: $"Map: {map}",
        startTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    );
};

EOSMatchHistory.Instance.OnMatchEnded += (result) => {
    discord.SetPresence("In Menu", "Match Complete");
};
```

### With Ranked

```csharp
var ranked = EOSRankedMatchmaking.Instance;
ranked.OnMatchFound += (lobby) => {
    discord.UpdatePresence(
        details: "Ranked Match",
        state: ranked.GetCurrentRankDisplayName(),
        partyId: lobby.Code,
        partySize: lobby.MemberCount,
        partyMax: lobby.MaxMembers,
        startTimestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        smallImageKey: "ranked_icon"
    );
};
```

### With Spectator Mode

```csharp
EOSSpectatorMode.Instance.OnSpectatorModeEntered += () => {
    discord.SetPresence("Spectating", discord.GetCurrentTargetName());
};
```

## Troubleshooting

### "Could not connect to Discord"

- Make sure Discord desktop app is running
- Discord must be running before your game starts
- Web Discord doesn't support Rich Presence

### Presence not showing

- Verify Application ID is correct
- Check Discord settings: User Settings > Activity Privacy > "Display current activity as a status message" must be ON
- Images may take a few minutes to propagate after upload

### Connection drops

- Discord client restarted - presence will auto-reconnect on next update
- Network issues - call `Connect()` to retry

## How It Works

This implementation uses Discord's IPC protocol directly:

1. Connects to `\\.\pipe\discord-ipc-0` (named pipe)
2. Sends handshake with your Application ID
3. Uses `SET_ACTIVITY` command to update presence
4. Listens for Discord responses and handles reconnection

No Discord SDK, no external packages - just raw socket communication!

## Platform Support

| Platform | Supported |
|----------|-----------|
| Windows | Yes |
| macOS | Yes |
| Linux | Yes |
| Android | No (Discord mobile) |
| iOS | No (Discord mobile) |

## See Also

- [Lobbies](lobbies.md) - Lobby system
- [Match History](match-history.md) - Track games
- [Party System](party.md) - Persistent groups
