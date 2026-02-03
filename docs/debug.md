# Debug Tools

Runtime debugging and development tools.

## Debug Settings Window

`Tools > FishNet EOS Native > Debug Settings`

### Categories (29 total)

Organized into 9 groups:

| Group | Categories |
|-------|------------|
| Core | SDK, Transport, Connection |
| Lobby | Lobby, Search, Attributes |
| Voice | RTC, VoicePlayer, VoiceLevels |
| Migration | Migration, Ownership, PlayerSpawner |
| Social | Friends, Presence, Invites, Party |
| Stats | Stats, Leaderboards, Achievements |
| Storage | PlayerStorage, TitleStorage, Replays |
| Moderation | Reports, Sanctions, AntiCheat |
| Demo | PlayerBall, Physics, UI |

### Group Muting

Toggle entire groups on/off for quick filtering:

```csharp
// Code access
EOSDebugLogger.MuteGroup("Voice");
EOSDebugLogger.UnmuteGroup("Voice");
```

### Settings Storage

Stored in `Resources/EOSDebugSettings.asset`

## Runtime Debug Panels

### F1 - Main UI

The primary debug interface showing:

- **Lobby Status**: Current lobby, code, members
- **Chat**: Send/receive messages
- **Stats**: Local player stats
- **Invites**: Pending invites, quick-send
- **Local Friends**: Friend list with status
- **Recently Played**: Recent players with friend toggle
- **Match History**: Recent matches
- **Replays**: Saved replays

### F3 - Voice Debug

Voice and RTC diagnostics:

- RTC connection status
- Participant list
- Audio levels (visual meter)
- Mute states
- Speaking indicators
- Pitch shift values

### F4 - Network Debug

P2P and connection info:

- Connection status per peer
- Bandwidth usage (in/out)
- Ping (RTT from FishNet TimeManager)
- Jitter measurement
- Connection quality rating (●●●●○)
- Host migration status

## Logging

### Using the Logger

```csharp
using FishNet.Transport.EOSNative;

// Log with category
EOSDebugLogger.Log(DebugCategory.Lobby, "Player joined");

// With log level
EOSDebugLogger.Log(DebugCategory.Connection, "Connected", LogLevel.Info);
EOSDebugLogger.Log(DebugCategory.Connection, "Failed", LogLevel.Error);

// Conditional (only if category enabled)
EOSDebugLogger.LogIf(DebugCategory.Voice, "Audio level changed");
```

### Categories

```csharp
public enum DebugCategory
{
    // Core
    SDK,
    Transport,
    Connection,

    // Lobby
    Lobby,
    Search,
    Attributes,

    // Voice
    RTC,
    VoicePlayer,
    VoiceLevels,

    // Migration
    Migration,
    Ownership,
    PlayerSpawner,

    // Social
    Friends,
    Presence,
    Invites,
    Party,

    // Stats
    Stats,
    Leaderboards,
    Achievements,

    // Storage
    PlayerStorage,
    TitleStorage,
    Replays,

    // Moderation
    Reports,
    Sanctions,
    AntiCheat,

    // Demo
    PlayerBall,
    Physics,
    UI
}
```

## Connection Quality Indicator

Shows real-time connection quality:

```
●●●●● Excellent (< 50ms)
●●●●○ Good (50-100ms)
●●●○○ Fair (100-150ms)
●●○○○ Poor (150-250ms)
●○○○○ Bad (> 250ms)
```

Based on:
- Ping (RTT)
- Jitter (ping variance)
- Packet loss (if available)

## Ping Display

Shows actual RTT from FishNet TimeManager:

```csharp
// Get current ping
float ping = NetworkManager.TimeManager.RoundTripTime * 1000f;

// Color coding
// Green: < 50ms
// Yellow: 50-100ms
// Orange: 100-200ms
// Red: > 200ms
```

## Host Migration Tester

Runtime verification for migration:

```csharp
var tester = HostMigrationTester.Instance;

// Simulate host disconnect
tester.SimulateHostDisconnect();
```

Verification checklist:
- [ ] New host elected
- [ ] P2P connections re-established
- [ ] Players repossessed
- [ ] Voice chat working
- [ ] Chat history intact
- [ ] NetworkObjects restored

## Toast Notifications

Visual feedback for events:

```csharp
EOSToastManager.Info("Message");
EOSToastManager.Success("Title", "Details");
EOSToastManager.Warning("Warning message");
EOSToastManager.Error("Error message");
```

Configure position:
```csharp
EOSToastManager.Instance.Position = ToastPosition.TopRight;
EOSToastManager.Instance.DefaultDuration = 3f;
```

## Development Tips

### Enable Verbose Logging

In Debug Settings window, enable all categories for full output.

### ParrelSync Testing

1. Use ParrelSync for multi-client testing
2. Main editor hosts, clone joins
3. Check F4 panel on both for connection status

### Network Simulation

Test poor conditions:
```csharp
// In Editor, simulate latency
NetworkManager.TransportManager.LatencySimulation.Mode = LatencySimulationType.Outgoing;
NetworkManager.TransportManager.LatencySimulation.Latency = 200; // 200ms
```
