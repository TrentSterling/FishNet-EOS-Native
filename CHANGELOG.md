# Changelog

All notable changes to FishNet-EOS-Native will be documented in this file.

## [1.0.1] - 2026-02-03 - Auto-Migration & Documentation

### Added
- **Auto-Migration System** - All NetworkObjects now automatically migrate during host migration
  - `DoNotMigrate` component to opt-out specific objects from migration
  - HostMigrationManager auto-tracks all spawned NetworkObjects
  - SyncVar data captured via reflection for all objects (not just HostMigratable)
  - `HostMigratable` component now optional (for backwards compatibility and advanced features)
- **3D Spatial Audio Documentation** - Added docs for SpatialBlend, DopplerLevel, distance rolloff

### Changed
- **BREAKING: Migration behavior inverted** - Objects now migrate by default (was opt-in)
  - Before: Add `HostMigratable` to opt-in
  - After: Add `DoNotMigrate` to opt-out
- Updated docs/migration.md with new usage patterns
- Updated ROADMAP.md with community-requested features (Duck's suggestions)
- Updated TODO.md with completed migration flip and new community requests

#### QuickMatch with Search Options
- **LobbySearchOptions parameter** - `QuickMatchOrHostAsync` now accepts optional filters
- Filter methods:
  - `.WithGameMode(string)` - Filter by game mode
  - `.WithRegion(string)` - Filter by region
  - `.WithMinPlayers(int)` - Minimum player count
  - `.WithMaxPlayers(int)` - Maximum player count
  - `.ExcludeFull()` - Only lobbies with space
  - `.ExcludePassworded()` - Only public lobbies
  - `.WithBucketId(string)` - Filter by version/platform bucket
  - `.WithMaxResults(int)` - Limit search results
- Search filters automatically copied to host options for fallback hosting

### Community Requests Logged (Duck)
- Voice logging for replay playback
- PlayFab/LootLocker optional integrations
- Device bans via Meta attestation
- Cross-provider identity integration

---

## [1.0.0] - 2026-02-02 - Production Ready Release

### Added

#### Replay System
- **EOSReplayRecorder** - Record game sessions with configurable frame rate
- **EOSReplayPlayer** - Playback with play/pause/seek/speed controls
- **EOSReplayStorage** - Local and cloud storage with auto-cleanup
- **EOSReplayViewer** - Integrated playback with spectator camera
- **ReplayRecordable** - Mark objects for recording
- **ReplayGhost** - Visual representation during playback
- **Replay Export/Import** - Share replay files manually
- **Replay Favorites** - Star replays to protect from cleanup
- **Duration Limits** - Auto-stop at 30 minutes with warnings
- **Keyboard Shortcuts** - Space, arrows, 1-4 for speed, Tab for targets
- **Data Compression** - Half-float positions, smallest-three quaternions

#### Anti-Cheat
- **EOSAntiCheatManager** - Easy Anti-Cheat (EAC) integration
- P2P mode session management
- Peer registration and validation
- Integrity violation detection
- UI section in F1 debug panel

#### Ranked Matchmaking
- **EOSRankedMatchmaking** - Skill-based matchmaking system
- **ELO Algorithm** - Standard ELO with configurable K-factor
- **Glicko-2 Algorithm** - Rating with uncertainty tracking
- **SimpleMMR Algorithm** - Fixed points with streak bonuses
- **Tier System** - Bronze→Champion (6-tier) or Iron→Grandmaster (8-tier)
- **Division System** - I, II, III, IV per tier
- **Placement Matches** - First 10 games for initial rating
- **Cloud Persistence** - Ratings sync across devices

#### Party System
- **EOSPartyManager** - Persistent party groups
- **6-Character Party Codes** - Easy sharing
- **Follow Modes** - Automatic, Confirm, ReadyCheck, Manual
- **Ready Check** - Synchronize before joining games
- **Party Chat** - In-party messaging
- **Leadership** - Promote, kick, dissolve controls
- **Configurable Persistence** - Session-based, persistent, or timed expiry
- **Full Lobby Behavior** - Block, warn, partial join, or leader only

#### Match History
- **EOSMatchHistory** - Track games played
- Participant tracking with teams
- Score and outcome recording
- Win/loss/draw statistics
- Local persistence

#### Spectator Mode
- **EOSSpectatorMode** - Watch games without participating
- Follow player camera with smooth transitions
- Free camera mode (WASD/QE movement)
- Target cycling with Tab key
- Integration with replay system

#### Social Enhancements
- **Local Friends System** - Mark players as friends locally
- **Cloud Sync** - Friends sync across devices via EOS Cloud Storage
- **Block List** - Block players with cloud sync
- **Friend Notes** - Add personal notes to friends
- **Quick Join** - One-click join friend's lobby
- **Friend Online Status** - Shows "In Lobby", "In Game", or "Offline"

#### UI Improvements
- **EOSToastManager** - Non-intrusive popup notifications
- **Toast Integration** - Auto-shows for lobby events, invites, friend changes
- **Connection Quality Indicator** - Ping, jitter, quality rating (●●●●○)
- **Ping Display** - Actual RTT from FishNet TimeManager (color-coded)
- **Platform Icons** - Windows/Mac/Linux/Android/iOS/Quest icons
- **Invite UX** - Quick-send buttons for friends, star toggles in Recently Played

#### Chat Improvements
- **Lobby Chat History** - Chat persists to cloud
- **History Reload** - Messages reload when rejoining same lobby
- **Manual Control** - Save/load/delete chat history APIs

#### Testing Tools
- **Host Migration Tester** - Runtime verification checklist

### Changed
- Updated all documentation to reflect 1.0.0 release
- Improved file structure documentation
- Enhanced API documentation in CLAUDE.md

---

## [0.9.1] - 2026-01-31 - Host Migration & Physics Fixes

### Fixed
- **Host Migration SyncVar Restoration** - All SyncVars (color, etc.) now properly restored after migration
  - Added continuous state caching in `HostMigratable.Update()`
  - FishNet clears SyncVars before OnDisable, so we now use cached snapshot
  - Position already worked (was cached), now all SyncVars work the same way
- **PhysicsNetworkTransform Invalid Force** - Objects falling off map no longer spam console
  - Added `IsValidVector()` validation before `AddForce`/`AddTorque`
  - Silently skips NaN/Infinity forces instead of throwing errors

---

## [0.9.0] - 2026-01-30 - Centralized Debug Logging

### Added
- **Debug Settings Window** - `Tools > FishNet EOS Native > Debug Settings`
  - Global ON/OFF toggle with color status indicator
  - 29 debug categories organized into 9 collapsible groups
  - Per-category checkboxes with descriptive tooltips
  - "Enable All" / "Disable All" buttons (global and per-group)
  - Group mute toggles (temporarily disable without losing selections)
  - "Unmute All" button when groups are muted
  - Live enabled category counter (e.g., "15/29")
  - Unicode icons for each group (●, ◈, 🔊, ↻, 👥, 📊, 📁, ⚠, 🎮)
  - Auto-creates settings asset if missing
- **EOSDebugSettings.cs** - ScriptableObject storing debug preferences
  - `DebugCategory` flags enum with all 29 systems
  - Group shortcuts: AllCore, AllLobby, AllVoice, AllMigration, etc.
  - `IsCategoryEnabled()` checks global + category + mute state
  - Singleton accessor `EOSDebugSettings.Instance`
- **EOSDebugLogger.cs** - Centralized logging utility
  - `Log(category, className, message)` - conditional on settings
  - `LogWarning(category, className, message)` - conditional
  - `LogError(className, message)` - always logs (errors never silenced)
  - `[Conditional("UNITY_EDITOR")]` attributes for release builds

### Changed
- **All 30 classes** now use centralized `EOSDebugLogger` instead of direct `Debug.Log`
  - Core: EOSManager, EOSNativeTransport, EOSServer, EOSClient, EOSClientHost, PacketFragmenter
  - Lobby: EOSLobbyManager, EOSLobbyChatManager
  - Voice: EOSVoiceManager, EOSVoicePlayer, FishNetVoicePlayer
  - Migration: HostMigrationManager, HostMigratable, HostMigrationPlayerSpawner
  - Social: EOSFriends, EOSPresence, EOSUserInfo, EOSCustomInvites
  - Stats: EOSStats, EOSLeaderboards, EOSAchievements
  - Storage: EOSPlayerDataStorage, EOSTitleStorage
  - Moderation: EOSReports, EOSSanctions, EOSMetrics
  - Demo: NetworkPhysicsObject, PlayerBall, PhysicsNetworkTransform
- **Namespace** - Debug utilities in `FishNet.Transport.EOSNative.Logging`

### Debug Categories (29 total)
| Group | Categories |
|-------|------------|
| Core (6) | EOSManager, Transport, Server, Client, ClientHost, PacketFragmenter |
| Lobby (2) | LobbyManager, LobbyChatManager |
| Voice (3) | VoiceManager, VoicePlayer, FishNetVoicePlayer |
| Migration (3) | HostMigrationManager, HostMigratable, HostMigrationPlayerSpawner |
| Social (4) | Friends, Presence, UserInfo, CustomInvites |
| Stats (3) | Stats, Leaderboards, Achievements |
| Storage (2) | PlayerDataStorage, TitleStorage |
| Moderation (3) | Reports, Sanctions, Metrics |
| Demo (3) | NetworkPhysicsObject, PlayerBall, PhysicsNetworkTransform |

---

## [0.8.0] - 2026-01-30 - Polish, Physics & Tools Menu

### Added
- **Setup Wizard** - `Tools > FishNet EOS Native > Setup Wizard`
  - Step-by-step EOS credential configuration
  - Helpful tooltips explaining where to find each value in Developer Portal
  - Quick links to EOS Developer Portal and Documentation
  - Generate Random Key button for encryption key
  - Real-time validation with checkmarks for each field
- **Tools Menu** - `Tools > FishNet EOS Native`
  - Setup Scene - Creates all required components
  - Select Config - Pings EOSConfig in Project
  - Create New Config - Creates new EOSConfig asset
  - Validate Setup - Checks for missing components
  - Log Platform Info - Prints platform detection to console
- **NetworkPhysicsObject** - For pushable crates/objects
  - Forces non-kinematic so physics works on all clients
  - Steals ownership on collision with player
  - ServerRpc-based ownership transfer
- **PlayerBall Jumping** - Space key to jump
  - SphereCast ground detection
  - Synced jump input across all clients
  - Air control preserved (friction only when grounded)
- **Ownership Swapping Research** - Documented in CLAUDE.md
  - FishNet PredictedOwner component docs
  - NGO sample saved to `SAMPLES/OwnershipSwapping/`
  - Implementation TODO for future

### Changed
- **Auto-setup creates EOS subsystems as children** of NetworkManager (cleaner hierarchy!)
- **Auto-setup adds HostMigrationPlayerSpawner** with PlayerBallPrefab auto-assigned
- **PlayerBall physics overhaul**
  - `ForceMode.VelocityChange` - Ignores mass, responsive movement
  - Synced inputs on ALL clients - Boxes get pushed properly
  - Forces rigidbody non-kinematic every FixedUpdate
  - Proper acceleration/friction/braking
- **Main tab default** - F1 UI now shows Main tab first (not Network)
- **Renamed methods** to avoid Unity conflicts:
  - `SendMessage` → `SendChatMessage` (EOSLobbyChatManager)
  - `BroadcastMessage` → `BroadcastChatMessage` (EOSDedicatedServer)
- **Deprecated `Demo/PlayerSpawner`** - Use HostMigrationPlayerSpawner instead

### Fixed
- **Exit playmode errors** - `IsReady` properties now use try-catch for SDK shutdown
  - EOSMetrics, EOSAchievements, EOSCustomInvites
- **LeaveLobbySync null check** - No more errors when leaving during shutdown
- **DontDestroyOnLoad warnings** - Child objects skip the call (parent handles it)
- **Bandwidth graph scrollbar** - Reduced width from 320 to 280px
- **EOSMetrics duplicate session** - Skips BeginSession if already active (host server+client)
- **HostMigrationPlayerSpawner warning** - ClientHost (32767) no longer warns about missing PUID
- **HostMigrationManager warning** - Unregistered prefabs silently skipped (not a warning)
- **FishNet deprecated API** - UpgradeFromMirrorMenu uses NamedBuildTarget

### Removed
- **Separate "EOS Native" menu** - Consolidated into "FishNet EOS Native"
- **EOS Platform Info window** - Use "Log Platform Info" menu instead
- **Unused fields** - `s_sdkEverInitialized`, `_noPacketLogCounter`

### Confirmed Working
- **VOIP works!** - Accidental mic test: heard voice echo through TV while testing 🎤

---

## [0.7.0] - 2026-01-29 - Debug Panels & UI Rename

### Added
- **Voice Debug Panel (F3)** - `EOSVoiceDebugPanel`
  - RTC room connection status
  - Local mic status with level indicator
  - Participant list with speaking indicators
  - Per-participant mute controls
  - Audio level bars with peak detection
- **Network Debug Panel (F4)** - `EOSNetworkDebugPanel`
  - P2P connection status (server/client, socket, relay)
  - Live bandwidth graph (in/out KB/s over time)
  - Total data transferred
  - Host migration status and debug controls
  - Connection list with PUIDs
  - Lobby/FishNet sync status

### Changed
- **Renamed `EOSTestUI` to `EOSNativeUI`** - Better reflects its purpose as the main UI
- Auto-setup now creates all debug panels (F1 main, F3 voice, F4 network)
- `DebugUI/` folder created for debug panel components
- Updated all documentation references

### Removed
- `EOSStatsDebugger` marked as deprecated (stats now in EOSNativeUI and EOSNetworkDebugPanel)

---

## [0.6.0] - 2026-01-29 - Voice, Chat & Host Migration

### Added
- **Voice Chat System** - Lobby-based EOS RTC integration
  - `EOSVoiceManager` - Auto-connects when lobby has voice enabled
  - `EOSVoicePlayer` - Per-participant audio playback with 3D spatial support
  - `FishNetVoicePlayer` - Auto-wires PUID from NetworkObject owner
- **Text Chat** - Lobby member attributes (survives host migration!)
  - `EOSLobbyChatManager` - Send/receive via lobby attributes
  - `EOSChatUI` - Standalone chat UI component
  - Chat integrated into EOSNativeUI (F1)
- **Host Migration Framework**
  - `HostMigratable` - NetworkBehaviour component for state persistence
  - `HostMigrationManager` - Tracks objects, orchestrates migration flow
  - `HostMigrationPlayerSpawner` - Handles repossession when players reconnect
- **Windows RTC Support** - XAudio2 DLL path in platform options (required for voice)

### Fixed
- `LeaveLobbySync` null callback causing ArgumentNullException on exit
- Voice lobbies now work (was getting InvalidRequest without RTCOptions)

### Changed
- Auto-setup now creates: EOSVoiceManager, EOSLobbyChatManager, HostMigrationManager
- `LobbyCreateOptions.EnableVoice` defaults to true (RTC room auto-created)

---

## [0.5.0] - 2026-01-29 - Polish & Cleanup

### Added
- **QuickMatchOrHostAsync()** - Auto-hosts when no lobbies found
- **Bandwidth tracking** - `TotalBytesSent`/`TotalBytesReceived` on server and client
- **Enhanced EOSNativeUI** - Redesigned with dark theme, live stats, bandwidth display
- **Mismatch detection** - Lobby vs FishNet connection count sync status [OK/WAITING/MISMATCH]
- **Optional auto-connect** - `JoinLobbyAsync(code, autoConnect: false)` parameter
- **Connect to Host button** - Manual connection when in lobby but not connected

### Changed
- EOSNativeUI consolidated all stats (removed separate F2 overlay)
- Improved GUI styling with color-coded status indicators
- PlayerBallPrefab physics: Angular drag 0.05 → 0.5, Linear drag 0 → 0.1

### Fixed
- Mismatch logic now correctly accounts for clienthost in ServerManager.Clients count
- EOSStatsDebugger now uses new Input System (was using legacy Input)

### Removed
- Verbose per-packet debug logs (PacketFragmenter, EOSServer, EOSClient)

---

## [0.4.0] - 2026-01-29 - Full P2P Working! 🎉

### Added
- **PacketFragmenter.cs** - Handles packets >1170 bytes with 7-byte header (packetId, fragmentId, lastFragment)
- **INSPIRATION.md** - Documentation of inspirations and hard-won debugging lessons

### Fixed
- **CRITICAL: EOS Channel/Reliability Mapping Bug** - NetworkTransform and other unreliable packets now work correctly
  - Bug: All packets sent on EOS Channel 0, receiver always mapped to FishNet Reliable
  - Fix: Use EOS Channel 0 for Reliable, Channel 1 for Unreliable
  - This was causing "ArraySegment count cannot be less than 0" errors on NetworkTransform updates

### Tested & Confirmed Working
- ParrelSync host + clone full P2P connection
- Bidirectional NetworkObject sync (NetworkTransform)
- Lobby join by 4-digit code
- "Connect to Host" button
- Both reliable and unreliable channels

---

## [0.3.0] - 2026-01-29 - Remote Lobby Join & Documentation Overhaul

### Added
- **ParrelSync Support** - Clone projects work with unique device IDs
- **Remote Lobby Join** - Successfully tested ParrelSync clone joining lobby via 4-digit code
- **Relay Control Setting** - `RelayControl` field with `ForceRelays` default (protects user IPs)
- **CHANGELOG.md** - This file!

### Changed
- **Documentation Overhaul** - All markdown files updated with current status:
  - CLAUDE.md: Added architecture vision, layer survival diagram, updated priorities
  - README.md: Updated features list, added relay/voice/migration mentions
  - ROADMAP.md: Added voice integration patterns, connection security section
  - TODO.md: Reorganized phases, added voice/lobby browser phases
- **EOSNativeTransport** - Added `RelayControlSetting` property and `ApplyRelayControl()` method
- **StartClient renamed to StartClientOnly** - Fixed duplicate method name conflict

### Tested
- ParrelSync clone joins lobby "1234" successfully
- Both instances show correct member count (2/4)
- Host sees "You are the host", clone sees host PUID

---

## [0.2.0] - 2026-01-29 - Lobby System & Inspector UI

### Added
- **4-Digit Lobby Codes** - Human-friendly join codes (e.g., "1234")
- **EOSLobbyManager** - Create, join, search, leave lobbies
- **LobbyData.cs** - Data structures, options, attributes
- **Custom Inspector** - Runtime status, PUID display, lobby controls
- **EOSNativeUI** - In-game debug panel (F1 to toggle)
- **Lobby API on Transport** - `CreateLobbyAsync()`, `JoinLobbyAsync()`, `LeaveLobbyAsync()`
- **Inspector Lobby Controls** - Create/Join/Leave buttons, "Create & Host" one-click

### Changed
- **EOSNativeTransport** - Integrated lobby API directly (all-in-one experience)
- **EOSNativeTransportEditor** - Full lobby controls matching test UI

---

## [0.1.0] - 2026-01-29 - Foundation & Auto-Setup

### Added
- **Auto-Setup** - Adding EOSNativeTransport creates NetworkManager, EOSManager, wires everything
- **Auto-Initialize** - EOS SDK init + device token login on Start()
- **EOSManager** - Singleton for SDK lifecycle, Tick, device login
- **EOSConfig** - ScriptableObject for EOS credentials
- **EOSNativeTransport** - Main transport extending FishNet Transport
- **EOSServer** - Server peer with connection mapping
- **EOSClient** - Client peer for remote connections
- **EOSClientHost** - Host-as-client (connection ID 32767)
- **NotifyEventHandle** - Auto-cleanup for EOS notification handles
- **Connection.cs** - Connection state container
- **LocalPacket.cs** - Internal packet queue

### Tested
- EOS SDK initializes in Windows Editor
- Device token login works
- Server starts and listens
- ClientHost connects locally (ID 32767)

---

## [0.0.0] - 2026-01-29 - Initial Setup

### Added
- Project structure
- FishNet 4.x integration
- EOS C# SDK v1.18.1.2 in Plugins/EOSSDK
- Reference samples in /SAMPLES folder
- CLAUDE.md with architecture guidance
