# FishNet-EOS-Native Roadmap

## Current Status: v1.0.0 - Production Ready (2026-02-02)

### What Works Now

#### Core Networking
- **Zero-Config Setup**: Add EOSNativeTransport → everything auto-creates
- **Setup Wizard**: `Tools > FishNet EOS Native > Setup Wizard` with tooltips
- **P2P Transport**: Reliable/unreliable channels, packet fragmentation
- **4-Digit Lobby Codes**: Create/Join/QuickMatch with attributes
- **PhysicsNetworkTransform**: Spring-based physics sync

#### Voice & Chat
- **Voice Chat**: Lobby-based RTC, 3D spatial audio, pitch shifting
- **Text Chat**: Lobby attributes, survives host migration
- **Chat History**: Cloud-persisted, reloads on rejoin

#### Host Migration
- **HostMigratable**: SyncVar persistence, state caching
- **Auto Promotion**: EOS handles new host selection
- **Player Repossession**: Objects returned to original owners

#### Social & Matchmaking
- **Local Friends**: Mark players as friends, cloud sync
- **Block List**: Block players with cloud sync
- **Party System**: Persistent groups with follow-the-leader
- **Ranked Matchmaking**: ELO/Glicko-2/SimpleMMR with tier display
- **Quick Match**: Find or host automatically
- **Custom Invites**: Cross-platform game invitations
- **Epic Friends & Presence**: Epic Account integration

#### Stats & Services
- **Stats & Leaderboards**: Player statistics and rankings
- **Achievements**: Ready for backend configuration
- **Cloud Storage**: 400MB saves per player
- **Match History**: Track games played with outcomes

#### Spectator & Replay
- **Spectator Mode**: Watch games with free/follow camera
- **Replay System**: Record/playback with timeline controls
- **Export/Import**: Share replay files manually
- **Favorites**: Star replays to protect from cleanup

#### Moderation & Anti-Cheat
- **Reports & Sanctions**: Player moderation
- **Anti-Cheat (EAC)**: Easy Anti-Cheat integration

#### Tools & UI
- **Debug Settings**: 29 categories with group muting
- **Debug Panels**: F1 (Main), F3 (Voice), F4 (Network)
- **Toast Notifications**: Non-intrusive event popups
- **Connection Quality**: Ping, jitter, quality indicators
- **Platform Detection**: Cross-platform icons and filtering

---

## Architecture: The "All-in-Wonder" Stack

```
EOS Lobby (persistent layer)
├── Voice/RTC ──────── Stays connected through host migration!
├── Member presence ── Who's in the session
├── Lobby attributes ─ Game settings, chat messages
└── Host PUID ──────── Who to connect to for game state
    │
    └── FishNet P2P Connection (transient layer)
        ├── Game state sync
        ├── NetworkObjects & RPCs
        └── Reconnects on host migration
```

**Key Insight:** Voice and chat are lobby-based, NOT P2P-based!
- Join lobby = Join voice room
- Host migrates = Voice/chat keep going, FishNet has brief hiccup

---

## Feature Status

| Feature | Status | Version |
|---------|--------|---------|
| Zero-Config Setup | ✅ Done | v0.1.0 |
| 4-Digit Lobby Codes | ✅ Done | v0.2.0 |
| Remote P2P Connection | ✅ Done | v0.4.0 |
| Packet Fragmentation | ✅ Done | v0.4.0 |
| Fast Disconnect | ✅ Done | v0.5.0 |
| Voice Chat | ✅ Done | v0.6.0 |
| Text Chat | ✅ Done | v0.6.0 |
| Host Migration Framework | ✅ Done | v0.6.0 |
| Debug Panels (F1/F3/F4) | ✅ Done | v0.7.0 |
| Social Features | ✅ Done | v0.6.0 |
| Stats & Leaderboards | ✅ Done | v0.6.0 |
| Cloud Storage | ✅ Done | v0.6.0 |
| Platform Support | ✅ Done | v0.6.0 |
| Setup Wizard | ✅ Done | v0.8.0 |
| PhysicsNetworkTransform | ✅ Done | v0.8.0 |
| Centralized Debug Logging | ✅ Done | v0.9.0 |
| Local Friends System | ✅ Done | v1.0.0 |
| Toast Notifications | ✅ Done | v1.0.0 |
| Block List | ✅ Done | v1.0.0 |
| Connection Quality | ✅ Done | v1.0.0 |
| Platform Detection | ✅ Done | v1.0.0 |
| Chat History | ✅ Done | v1.0.0 |
| Match History | ✅ Done | v1.0.0 |
| Spectator Mode | ✅ Done | v1.0.0 |
| Party System | ✅ Done | v1.0.0 |
| Ranked Matchmaking | ✅ Done | v1.0.0 |
| Replay System | ✅ Done | v1.0.0 |
| Anti-Cheat (EAC) | ✅ Done | v1.0.0 |

---

## Quick Start

### Setup
1. `Tools > FishNet EOS Native > Setup Wizard` - Enter credentials
2. Create empty GameObject → Add **EOSNativeTransport**
3. Auto-created: NetworkManager, EOSManager, EOSLobbyManager, etc.
4. Enter Play Mode → auto-initializes and logs in

### Host
```csharp
var transport = GetComponent<EOSNativeTransport>();
var (result, lobby) = await transport.HostLobbyAsync();
// Share lobby.JoinCode with friends
```

### Join
```csharp
var (result, lobby) = await transport.JoinLobbyAsync("1234");
```

### Quick Match
```csharp
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();
```

---

## Files Overview (70+ files)

```
Assets/FishNet.Transport.EOSNative/
├── Core (10 files)
│   ├── EOSManager.cs, EOSConfig.cs, EOSNativeTransport.cs
│   ├── EOSServer.cs, EOSClient.cs, EOSClientHost.cs
│   ├── EOSPlayerRegistry.cs, PacketFragmenter.cs
│   └── Connection.cs, LocalPacket.cs
│
├── Debug (2 files)
│   ├── EOSDebugSettings.cs, EOSDebugLogger.cs
│
├── UI (5 files)
│   ├── EOSNativeUI.cs (F1), EOSNetworkPlayer.cs
│   └── DebugUI/EOSVoiceDebugPanel.cs (F3), EOSNetworkDebugPanel.cs (F4)
│
├── Lobbies (3 files)
│   ├── EOSLobbyManager.cs, EOSLobbyChatManager.cs, LobbyData.cs
│
├── Voice (3 files)
│   ├── EOSVoiceManager.cs, EOSVoicePlayer.cs, FishNetVoicePlayer.cs
│
├── Migration (4 files)
│   ├── HostMigratable.cs, HostMigrationManager.cs
│   ├── HostMigrationPlayerSpawner.cs, HostMigrationTester.cs
│
├── Social (10 files)
│   ├── EOSFriends.cs, EOSPresence.cs, EOSUserInfo.cs, EOSCustomInvites.cs
│   ├── EOSStats.cs, EOSLeaderboards.cs, EOSMatchHistory.cs
│   ├── EOSRankedMatchmaking.cs, RankedData.cs
│
├── Storage (2 files)
│   ├── EOSPlayerDataStorage.cs, EOSTitleStorage.cs
│
├── Party (1 file)
│   └── EOSPartyManager.cs
│
├── Replay (9 files)
│   ├── EOSReplayRecorder.cs, EOSReplayPlayer.cs, EOSReplayStorage.cs
│   ├── EOSReplayViewer.cs, EOSReplaySettings.cs, ReplayDataTypes.cs
│   ├── ReplayRecordable.cs, ReplayGhost.cs, ReplayMigration.cs
│
├── AntiCheat (1 file)
│   └── EOSAntiCheatManager.cs
│
├── EOSSpectatorMode.cs
│
├── Editor (4 files)
│   ├── EOSNativeTransportEditor.cs, EOSNativeMenu.cs
│   ├── EOSSetupWizard.cs, EOSDebugSettingsWindow.cs
│
└── Demo (5 files)
    ├── PlayerBall.cs, NetworkPhysicsObject.cs, PhysicsNetworkTransform.cs
    ├── PlayerSpawner.cs, EOSChatUI.cs
```

---

## Future Roadmap

### v1.1.0 - Enhancements (Planned)
- **Voice Recording in Replays** - Record voice chat for playback
- **Replay Sharing via Cloud** - Share replays with friends via codes
- **Server-side Anti-Cheat** - EOSAntiCheatServer for dedicated servers
- **Epic Account Linking** - Upgrade from DeviceID to Epic Account
- **Mobile Testing** - Android, Quest, iOS verification

---

## Connection Security

**Default: Force Relay** (protects user IP addresses)

```csharp
[SerializeField] private RelayControl _relayControl = RelayControl.ForceRelays;
```

Options:
- `ForceRelays` - All traffic through Epic relay servers (default, secure)
- `AllowRelays` - Direct NAT if possible, relay fallback
- `NoRelays` - Direct only (not recommended)
