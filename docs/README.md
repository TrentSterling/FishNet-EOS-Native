# FishNet EOS Native

> Standalone Transport for FishNet using Epic Online Services (EOS) directly via raw C# SDK. No PlayEveryWare dependency.

## Features

- **Direct EOS Integration** - Uses `Epic.OnlineServices` directly, no wrapper plugins
- **Full Transport** - Server, Client, and ClientHost modes with P2P networking
- **Offline Mode** - Singleplayer without EOS, auto-fallback when offline
- **Lobbies** - Custom join codes (any string), search, attributes, max 64 players
- **Voice Chat** - Built-in RTC with pitch shifting and per-player controls
- **Text Chat** - Lobby-based chat with cloud-persisted history
- **Host Migration** - Automatic migration for all objects (opt-out with DoNotMigrate)
- **Party System** - Persistent groups with follow-the-leader mechanics
- **Ranked Matchmaking** - ELO/Glicko-2 skill-based matchmaking with tiers
- **Replay System** - Record and playback games with timeline controls
- **Anti-Cheat** - Easy Anti-Cheat (EAC) integration
- **Security Hardening** - Host-authority validation, rate limiting, secure methods
- **Cross-Platform** - Windows, Mac, Linux, Android, iOS, Quest

## Requirements

- **Unity 6** (6000.0.65f1+) or Unity 2022.3 LTS
- **FishNet** networking framework
- **EOS C# SDK** from [Epic Developer Portal](https://dev.epicgames.com/portal)

> **Note:** This transport uses the raw EOS C# SDK directly - no PlayEveryWare plugin needed. See [Setup Guide](setup.md) for installation.

## Quick Example

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Host a lobby
var (result, lobby) = await transport.HostLobbyAsync();
Debug.Log($"Lobby code: {lobby.Code}");

// Join a lobby
var (result, lobby) = await transport.JoinLobbyAsync("1234");

// Quick match (join or host)
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();

// Leave
await transport.LeaveLobbyAsync();
```

## Getting Started

1. [Quick Start Guide](quickstart.md) - Get up and running in 5 minutes
2. [Setup Wizard](setup.md) - Configure EOS credentials
3. [Lobbies](lobbies.md) - Learn the lobby system

## Documentation Sections

| Section | Description |
|---------|-------------|
| **Core Features** | Lobbies, Voice, Chat, Host Migration |
| **Social** | Friends, Parties, Invites, Platforms |
| **Competitive** | Ranked Matchmaking, Match History, Leaderboards |
| **Replay System** | Recording, Playback, Spectator Mode |
| **Advanced** | Security, Anti-Cheat, Cloud Storage, Architecture |

## Support

- [Troubleshooting](troubleshooting.md)
- [GitHub Issues](https://github.com/TrentSterling/FishNet-EOS-Native/issues)
- [EOS Developer Portal](https://dev.epicgames.com/portal)
