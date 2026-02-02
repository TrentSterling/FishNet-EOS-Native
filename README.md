# FishNet-EOS-Native

A standalone Transport layer for [FishNet](https://github.com/FirstGearGames/FishNet) that uses [Epic Online Services (EOS)](https://dev.epicgames.com/en-US/services) directly via the raw C# SDK.

## Why This Exists

Existing EOS transports for FishNet depend on the [PlayEveryWare EOS Plugin](https://github.com/PlayEveryWare/eos_plugin_for_unity), which adds significant complexity and bloat. This transport uses the raw EOS C# SDK directly, giving you:

- **No middleware dependency** - Just the official EOS SDK
- **Smaller footprint** - Only what you need for networking
- **Full control** - Direct access to EOS APIs
- **Cross-platform** - Windows, Android, iOS, Linux, macOS

## Features

### Networking
- **Zero-Config Setup** - Add one component, everything auto-wires
- **P2P Transport** - Reliable and unreliable channels via EOS relay
- **Packet Fragmentation** - Handles packets >1170 bytes automatically
- **Force Relay by Default** - Protects user IP addresses
- **4-Digit Lobby Codes** - Easy "join by code" matchmaking
- **PhysicsNetworkTransform** - Spring-based physics sync

### Voice & Chat
- **3D Spatialized Voice** - Lobby-based RTC with positional audio and pitch shifting
- **Text Chat** - Lobby attributes (survives host migration!)
- **Chat History** - Persists to cloud, reloads on rejoin
- **Voice Survives Migration** - Lobby-based, not P2P-based

### Host Migration
- **Automatic Promotion** - EOS handles host selection
- **State Persistence** - SyncVars saved and restored
- **Player Repossession** - Objects returned to original owners

### Social & Services
- **Local Friends** - Mark players as friends, cloud sync across devices
- **Epic Friends & Presence** - Epic Account integration
- **Custom Invites** - Cross-platform game invitations
- **Stats & Leaderboards** - Player statistics and rankings
- **Achievements** - Ready for backend configuration
- **Cloud Storage** - 400MB saves per player
- **Reports & Sanctions** - Player moderation
- **Block List** - Block players with cloud sync

### Matchmaking & Parties
- **Party System** - Persistent groups with follow-the-leader
- **Ranked Matchmaking** - Skill-based with ELO/Glicko-2/SimpleMMR
- **Quick Match** - Find or host lobbies automatically

### Spectator & Replay
- **Spectator Mode** - Watch games without participating
- **Replay System** - Record/playback with timeline controls, export/import
- **Match History** - Track games played with outcomes

### Anti-Cheat
- **Easy Anti-Cheat (EAC)** - Client integrity validation

### Tools
- **Setup Wizard** - Step-by-step credential configuration
- **Debug Settings** - 29 categories with group muting
- **Debug UI Suite** - F1 (Main), F3 (Voice), F4 (Network)
- **Toast Notifications** - Non-intrusive popup system
- **Connection Quality** - Ping, jitter, quality indicators
- **Platform Detection** - Cross-platform icons and filtering

## Requirements

- Unity 2022.3+ or Unity 6
- [FishNet 4.x+](https://github.com/FirstGearGames/FishNet) (install separately)
- [EOS C# SDK v1.18.1+](https://dev.epicgames.com/docs/epic-online-services/eos-get-started/services-quick-start#step-1---download-the-eos-sdk) (install separately)
- Epic Games Developer Account (free)

## Installation

### Step 1: Install Dependencies

**FishNet** (choose one):
- Unity Asset Store: [FishNet](https://assetstore.unity.com/packages/tools/network/fish-net-networking-evolved-207815)
- Git URL: `https://github.com/FirstGearGames/FishNet.git`

**EOS C# SDK**:
1. Download from [Epic Developer Portal](https://dev.epicgames.com/docs/epic-online-services/eos-get-started/services-quick-start#step-1---download-the-eos-sdk)
2. Extract and copy the `EOSSDK` folder to `Assets/Plugins/EOSSDK/`

### Step 2: Install This Transport

**Option A: Unity Package Manager (Recommended)**
1. Open Package Manager (Window > Package Manager)
2. Click **+** > **Add package from git URL**
3. Enter:
   ```
   https://github.com/TrentSterling/FishNet-EOS-Native.git?path=Assets/FishNet.Transport.EOSNative
   ```

**Option B: Manual**
1. Download or clone this repository
2. Copy `Assets/FishNet.Transport.EOSNative/` into your Unity project

### After Installation

1. Add `EOSNativeTransport` to a GameObject - **everything else is auto-created!**
2. Press Play - it works immediately with included test credentials!

> **⚡ Quick Start:** The included `SampleEOSConfig` uses PlayEveryWare's public test credentials. P2P, lobbies, and voice work instantly. For achievements, stats, leaderboards, or production, get your own FREE credentials at [dev.epicgames.com/portal](https://dev.epicgames.com/portal) and use `Tools > FishNet EOS Native > Setup Wizard`.

## Quick Start

### Setup (One-Time)

1. Create empty GameObject in your scene
2. Add Component → **EOSNativeTransport**
3. Done! Auto-created for you:
   - NetworkManager (same GameObject)
   - EOSManager, EOSLobbyManager, EOSVoiceManager (as children)
   - HostMigrationManager, EOSLobbyChatManager
   - EOSConfig assigned (if found)

### Host a Game

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Create lobby and start hosting
var (result, lobby) = await transport.HostLobbyAsync();
Debug.Log($"Share code: {lobby.JoinCode}");

// Or with custom options
var (result, lobby) = await transport.HostLobbyAsync(new LobbyCreateOptions
{
    LobbyName = "My Room",
    GameMode = "deathmatch",
    MaxPlayers = 8
});
```

### Join a Game

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Join by code
var (result, lobby) = await transport.JoinLobbyAsync("1234");

// Or quick match (finds any lobby, or hosts if none found)
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();
```

### Or Use the UI

- **Inspector**: Lobby controls in Play Mode
- **F1 Key**: Full debug panel with all controls
- **Setup Wizard**: `Tools > FishNet EOS Native > Setup Wizard`

## Debug Tools

### Debug Settings Window
`Tools > FishNet EOS Native > Debug Settings`
- Toggle logging for 29 systems across 9 groups
- Group mute toggles for quick enable/disable
- Per-category checkboxes with descriptions

### Debug Panels (Runtime)

| Key | Panel | Purpose |
|-----|-------|---------|
| F1 | EOSNativeUI | Main UI - lobby, chat, stats, invites |
| F3 | Voice Debug | RTC status, speaking indicators, levels |
| F4 | Network Debug | P2P connections, bandwidth graph, migration |

## Documentation

- [CLAUDE.md](CLAUDE.md) - Architecture reference and API guide
- [CLASSES.md](CLASSES.md) - Class architecture and detailed API
- [CHANGELOG.md](CHANGELOG.md) - Version history
- [ROADMAP.md](ROADMAP.md) - Feature roadmap
- [TODO.md](TODO.md) - Current development tasks
- [INSPIRATION.md](INSPIRATION.md) - Credits, lineage, and lessons learned
- [ARCHIVE.md](ARCHIVE.md) - Historical docs and code examples

## Current Version: 1.0.0

See [CHANGELOG.md](CHANGELOG.md) for details.

**Recent Highlights:**
- **Replay System** - Record/playback matches with timeline controls
- **Anti-Cheat (EAC)** - Easy Anti-Cheat integration
- **Ranked Matchmaking** - ELO/Glicko-2 skill-based matching
- **Party System** - Persistent groups with follow-the-leader
- **Match History** - Track games and outcomes
- **Spectator Mode** - Watch games with free camera
- **Toast Notifications** - Non-intrusive event popups
- **Local Friends** - Mark players as friends with cloud sync

## Credits & Acknowledgments

This project was built by studying these excellent open-source projects:

- **[FishyEOS](https://github.com/ETdoFresh/FishyEOS)** by ETdoFresh
- **EOSTransport for Mirror** - Raw SDK patterns, packet fragmentation, Android loading
  - [FakeByte](https://github.com/FakeByte/EpicOnlineTransport) (original creator)
  - [Katone/WeLoveJesusChrist](https://github.com/WeLoveJesusChrist/EOSTransport) (continuation)
  - [CodedImmersions](https://github.com/CodedImmersions/EOSTransport) (current maintainer)
- **[PurrNet EOS Transport](https://github.com/quentinleon/PurrNetEOSTransport)** by qwolf
- **[EOS C# SDK](https://dev.epicgames.com/docs/epic-online-services)** by Epic Games

### Special Thanks

- **Skylar (CometDev)** ([GitHub](https://github.com/SkylarSDev) | [Website](https://skylardev.xyz)) - PhysicsNetworkTransform spring-based sync, feature ideas, and relentless encouragement
- **DrewMileham** - Original spring physics method
- **[Roceh](https://github.com/Roceh)** - 3D spatialized voice patterns from FishNetEosVivoxLobbyTest
- **AFoolsDuty** - Getting me into agentic coding workflows
- **The Discord Rubber Ducks** - Wheelz, Duck, Andre, Daver 2.0

## License

MIT License - See [LICENSE](LICENSE) for details.

## Contributing

Contributions welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting PRs.
