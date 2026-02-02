# FishNet EOS Native Transport

Standalone Transport for FishNet using Epic Online Services (EOS) directly via raw C# SDK. No PlayEveryWare dependency.

## Features

### Networking
- **Zero-Config Setup** - Add one component, everything auto-wires
- **P2P Transport** - Reliable/unreliable via EOS relay (IP protection)
- **4-Digit Lobby Codes** - Easy matchmaking
- **PhysicsNetworkTransform** - Spring-based physics sync

### Voice & Chat
- **Voice Chat** - Lobby-based RTC with 3D spatial audio and pitch shifting
- **Text Chat** - Lobby attributes, survives host migration
- **Chat History** - Cloud-persisted, reloads on rejoin

### Host Migration
- **Automatic Promotion** - EOS handles host selection
- **State Persistence** - SyncVars saved and restored
- **Player Repossession** - Objects returned to original owners

### Social & Matchmaking
- **Local Friends** - Mark players as friends, cloud sync
- **Party System** - Persistent groups with follow-the-leader
- **Ranked Matchmaking** - ELO/Glicko-2 skill-based matching
- **Custom Invites** - Cross-platform invitations
- **Stats & Leaderboards** - Player statistics and rankings
- **Cloud Storage** - 400MB per player

### Spectator & Replay
- **Spectator Mode** - Watch games without participating
- **Replay System** - Record/playback with timeline controls

### Anti-Cheat
- **Easy Anti-Cheat** - EAC client integration

### Debug Tools
- **Debug Panels** - F1 (Main), F3 (Voice), F4 (Network)
- **29-Category Logging** - Fine-grained debug control
- **Toast Notifications** - Non-intrusive event popups

## Requirements

- Unity 6 (6000.0.0f1+)
- FishNet 4.x (install separately)
- EOS C# SDK (install separately from `Plugins/EOSSDK`)
- Epic Games Developer Account (free)

## Installation via Package Manager

1. Open Package Manager (Window > Package Manager)
2. Click + > Add package from git URL
3. Enter: `https://github.com/TrentSterling/FishNet-EOS-Native.git?path=Assets/FishNet.Transport.EOSNative`

## Quick Start

1. `Tools > FishNet EOS Native > Setup Wizard` - Enter credentials
2. Add `EOSNativeTransport` to any GameObject
3. Press Play - auto-initializes and logs in

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Host
var (result, lobby) = await transport.HostLobbyAsync();

// Join
var (result, lobby) = await transport.JoinLobbyAsync("1234");

// Quick Match
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();
```

## Documentation

See the [GitHub repository](https://github.com/TrentSterling/FishNet-EOS-Native) for full documentation.

## License

MIT License - See LICENSE file for details.
