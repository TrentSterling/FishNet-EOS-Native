# TODO

Current development tasks for FishNet-EOS-Native.

## In Progress - Testing (v1.0.0)

### Testing Tasks

- [ ] **Multi-Player Testing** (ParrelSync with 3+ instances)
  - [ ] Voice chat with 3 participants
  - [ ] Verify 3D spatial audio positioning
  - [ ] Test mute/unmute controls
  - [ ] Check speaking indicators

- [ ] **Host Migration Testing**
  - [ ] Test full migration flow
  - [ ] Verify voice/chat persists
  - [ ] Test player repossession
  - [x] HostMigratable SyncVar restoration

- [ ] **Replay System Testing**
  - [ ] Record a 2-minute match
  - [ ] Verify playback controls work
  - [ ] Test export/import functionality
  - [ ] Test favorites system

- [ ] **Ranked Matchmaking Testing**
  - [ ] Test match finding by skill range
  - [ ] Verify rating changes after match
  - [ ] Test tier promotions/demotions

- [ ] **Party System Testing**
  - [ ] Test party creation and joining
  - [ ] Test follow-the-leader functionality
  - [ ] Test ready check system

---

## Quick Wins (v1.0.x)

### UX Polish
- [x] **Copy Lobby Code to Clipboard** - One-click share button in UI
- [ ] **Player Color System** - Auto-assign colors on join, display in UI/nameplates
- [ ] **AFK Detection** - Track idle time, auto-kick option for hosts

### Social Enhancements
- [ ] **"Last Seen" for Friends** - Timestamp when friend was last online
- [ ] **Player Tags/Titles** - Host can assign labels ("VIP", "Noob", custom text)
- [ ] **"Time Played Together"** - Track cumulative play time with each friend

### Debug/Dev Tools
- [ ] **Fake Players for Testing** - Spawn bots that move randomly (ParrelSync alternative)
- [ ] **Screenshot Lobby State** - Copy lobby info to clipboard for bug reports
- [ ] **Auto-Reconnect** - Automatically attempt reconnect on disconnect

### Demo/Fun
- [ ] **Confetti on Join** - Particle effect when player joins lobby (demo showcase)
- [ ] **Chat Emotes/Reactions** - React to messages with 👍👎😂 etc.
- [ ] **"First to Join" Badge** - Special indicator for first player after host

---

## Completed (v1.0.0) - 2026-02-02

### Replay System
- [x] Create `EOSReplayRecorder` - Record game sessions
- [x] Create `EOSReplayPlayer` - Playback with controls
- [x] Create `EOSReplayStorage` - Local and cloud storage
- [x] Create `EOSReplayViewer` - Integrated playback UI
- [x] Add `ReplayRecordable` component for tracked objects
- [x] Add export/import for file sharing
- [x] Add favorites system (star to protect)
- [x] Add duration limits with warnings
- [x] Add keyboard shortcuts for playback

### Anti-Cheat
- [x] Create `EOSAntiCheatManager` - EAC integration
- [x] Add session management (begin/end)
- [x] Add peer registration/validation
- [x] Add integrity violation detection

### Ranked Matchmaking
- [x] Create `EOSRankedMatchmaking` - Skill-based matching
- [x] Implement ELO algorithm
- [x] Implement Glicko-2 algorithm
- [x] Implement SimpleMMR algorithm
- [x] Add tier/division display system
- [x] Add placement match handling
- [x] Add cloud persistence

### Party System
- [x] Create `EOSPartyManager` - Persistent groups
- [x] Add party codes (6 characters)
- [x] Add follow modes (Auto, Confirm, ReadyCheck, Manual)
- [x] Add ready check system
- [x] Add party chat

### Social Features
- [x] Local Friends System with cloud sync
- [x] Block List with cloud sync
- [x] Friend notes
- [x] Quick join friend's lobby
- [x] Friend online status display

### Match History & Spectator
- [x] Create `EOSMatchHistory` - Track games
- [x] Create `EOSSpectatorMode` - Watch games
- [x] Add follow/free camera modes

### UI Improvements
- [x] Toast notification system
- [x] Connection quality indicator
- [x] Platform icons
- [x] Chat history persistence

---

## Completed (v0.9.0) - 2026-01-30

### Centralized Debug Logging
- [x] Create `EOSDebugSettings` - ScriptableObject with 29 categories
- [x] Create `EOSDebugLogger` - Conditional logging utility
- [x] Add Debug Settings Editor Window
- [x] Group muting for quick filtering

---

## Completed (v0.8.0) - 2026-01-30

### Setup Wizard & Tools Menu
- [x] Create `EOSSetupWizard` EditorWindow with step-by-step setup
- [x] Add tooltips explaining where to find each EOS credential
- [x] Add "Generate Random Key" button for encryption key
- [x] Create `Tools > FishNet EOS Native` menu
- [x] Add Setup Scene, Select Config, Validate Setup menu items

### Physics & Demo Improvements
- [x] Create `NetworkPhysicsObject` for pushable crates
- [x] Add ownership stealing on collision with player
- [x] Add jumping to `PlayerBall` (space key)
- [x] Overhaul PlayerBall physics (VelocityChange, synced inputs)
- [x] Document ownership swapping research (PredictedOwner, NGO sample)

### Bug Fixes & Polish
- [x] Fix exit playmode NullReferenceException errors
- [x] Fix DontDestroyOnLoad warnings for child objects
- [x] Make EOS subsystems children of NetworkManager

---

## Completed (v0.7.0) - 2026-01-29

### Debug Panels
- [x] Create `EOSVoiceDebugPanel` (F3) - RTC status, speaking indicators
- [x] Create `EOSNetworkDebugPanel` (F4) - P2P, bandwidth graph, migration
- [x] Rename `EOSTestUI` to `EOSNativeUI`

---

## Completed (v0.6.0) - 2026-01-29

### Voice & Chat
- [x] Create `EOSVoiceManager` - RTC voice management
- [x] Create `EOSVoicePlayer` - Per-participant audio
- [x] Create `FishNetVoicePlayer` - Auto-wires PUID from NetworkObject
- [x] Create `EOSLobbyChatManager` - Text chat via lobby attributes
- [x] Windows RTC fix - XAudio2 DLL path

### Host Migration Framework
- [x] Create `HostMigratable` NetworkBehaviour
- [x] Create `HostMigrationManager` - Object tracking, state save/restore
- [x] Create `HostMigrationPlayerSpawner` - Player repossession

### Social Features (all implemented)
- [x] `EOSFriends` - Friends list (Epic Account)
- [x] `EOSPresence` - Online status
- [x] `EOSUserInfo` - Player profiles
- [x] `EOSCustomInvites` - Cross-platform invitations
- [x] `EOSStats` - Player statistics
- [x] `EOSLeaderboards` - Rankings
- [x] `EOSAchievements` - Achievement system
- [x] `EOSMetrics` - Session telemetry

### Storage
- [x] `EOSPlayerDataStorage` - 400MB cloud saves
- [x] `EOSTitleStorage` - Read-only game config

### Moderation
- [x] `EOSReports` - Player reporting
- [x] `EOSSanctions` - Ban queries

### Platform Support
- [x] `EOSPlatformHelper` - Platform detection, crossplay filtering
- [x] `EOSDedicatedServer` - Headless server, CLI args
- [x] `EOSPlayerRegistry` - PUID→Name cache

---

## Completed (v0.5.0 and earlier) - 2026-01-29

### Foundation
- [x] EOS SDK integration (Windows Editor/Standalone)
- [x] Device token login with ParrelSync support
- [x] Auto-setup (one component creates all dependencies)
- [x] Auto-initialize on play mode

### Transport
- [x] EOSNativeTransport extending FishNet Transport
- [x] EOSServer, EOSClient, EOSClientHost
- [x] PacketFragmenter for >1170 byte messages
- [x] Channel/reliability mapping fix

### Lobbies
- [x] 4-digit join codes
- [x] Create/Join/Leave/Search
- [x] Lobby attributes (GameMode, Map, Region, etc.)
- [x] QuickMatchOrHostAsync

### UI & Inspector
- [x] Custom inspector with lobby controls
- [x] EOSNativeUI (F1) - Main debug UI
- [x] Bandwidth tracking, mismatch detection

### Testing Confirmed
- [x] ParrelSync host + clone P2P connection
- [x] Bidirectional NetworkTransform sync
- [x] Fast disconnect detection (heartbeat timeout)

---

## Community Requests (Discord)

### Documentation Improvements (Duck)
- [x] **Improve host migration callbacks docs** - Make OnMigrationStarted/OnMigrationCompleted more prominent and add use case examples

### Host Migration UX Flip (Duck) ⭐ DONE
- [x] **Invert migration opt-in to opt-out** - Everything migrates by default now
- [x] **Create `DoNotMigrate` component** - Add component to exclude specific objects from migration
- [x] **Auto-migrate all NetworkObjects** - HostMigrationManager auto-tracks all spawned NetworkObjects
- Note: HostMigratable still works for backwards compatibility and advanced SyncVar caching

### Auto-Install Dependencies (Duck)
- [ ] **Auto-install EOS SDK** - Detect if EOS SDK is missing and offer to install via Package Manager
- [ ] **Auto-install FishNet** - Detect if FishNet is missing and offer to install via Package Manager
- Note: Would make setup truly zero-friction - just import the transport and everything else gets pulled in automatically

### Lobby Code Flexibility (Duck) ✅ DONE
- [x] **Configurable lobby code format** - Not limited to 4-digit codes, allow any string
- [x] **Documentation updated** - All docs now clarify codes can be any string
- [x] **EOS LobbyId codes** - `UseEosLobbyId = true` uses EOS-generated ID as join code (guaranteed unique)
- [x] **Name search via attributes** - `SearchLobbiesByNameAsync()`, `JoinLobbyByNameAsync()`
- Note: EOS LobbyId is auto-detected on join - works seamlessly with custom codes

### Unified LobbyOptions API (Duck) ✅ DONE
- [x] **Single `LobbyOptions` class** - Unified options for host, join, quickmatch
- [x] **BucketId field** - Already exists in LobbyCreateOptions
- [x] **Fluent builder pattern** - Added to LobbyCreateOptions and LobbyOptions
- [x] **Apply to all methods** - LobbyCreateOptions for host, LobbySearchOptions for search/join
- [x] **Implicit conversion** - LobbyOptions auto-converts to LobbyCreateOptions or LobbySearchOptions
- Example:
```csharp
// One options class works for both hosting and quick matching
var options = new LobbyOptions
{
    LobbyName = "Pro Players Only",
    GameMode = "competitive",
    Region = "us-east",
    MaxPlayers = 8
};
var (result, lobby) = await transport.HostLobbyAsync(options);
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(options);
```
- Note: Clean abstraction - one options class rules them all

### QuickMatch Improvements (Duck) ✅ DONE
- [x] **QuickMatch by attributes** - Filter QuickMatch by lobby attributes (game mode, map, region, etc.)
- [x] **Attribute-based matchmaking** - Find lobbies matching specific criteria, not just "any available"
- [x] **Bucket-based matching** - Use EOS bucket IDs for regional/mode-based matchmaking pools
- [x] **Search options as optional field** - QuickMatch accepts optional LobbySearchOptions parameter
- Search methods supported:
  - `.WithGameMode(string)` ✅
  - `.WithRegion(string)` ✅
  - `.WithMinPlayers(int)` ✅
  - `.WithMaxPlayers(int)` ✅
  - `.ExcludePassworded()` ✅
  - `.ExcludeFull()` ✅
  - `.WithMaxResults(int)` ✅
  - `.WithBucketId(string)` ✅
- Example: `transport.QuickMatchOrHostAsync(new LobbySearchOptions().WithGameMode("deathmatch").WithRegion("us-east"))`

### Voice Effects (Duck)
- [x] **Doppler effect** - Exposed in EOSVoicePlayer and FishNetVoicePlayer (spatialBlend, dopplerLevel 0-5, minDistance, maxDistance, rolloffMode)
- [x] **Pitch shift / voice changer** - SMBPitchShifter (STFT-based) integrated into EOSVoicePlayer (0.5 = octave down, 2.0 = octave up)
- [ ] **Voice logging for replay** - Record voice chat chunks during gameplay for playback in replays ("skinwalker" use case - mimic other players' voices)
- Note: Current pitch shift is on playback side (per-listener). Future: add mic-side processing for "everyone hears your changed voice"

### Backend Integrations (Duck)
- [ ] **PlayFab integration** - Optional separate Unity package for PlayFab backend services
- [ ] **LootLocker integration** - Optional separate Unity package for LootLocker backend services
- Note: Not required to have installed, but available as add-on packages for devs who want those backends

### Device Bans & Identity (Duck)
- [ ] **Device bans via Meta attestation** - Hardware-level bans using platform attestation (Quest/Meta devices)
- [ ] **Cross-provider identity integration** - Unified account linking across all identity providers (Epic, Steam, Discord, PlayStation, Xbox, etc.)
- Note: Integrates "all the account shit across all the identity providers"

### Authentication Improvements
- [ ] **Nonce support** - Prevent replay attacks on auth
- [ ] **Third-party PUID binding** - Link external accounts (Steam, Discord, etc.) to PUID
- [ ] **Token management** - Refresh tokens, session persistence
- [ ] **Epic Account linking** - Upgrade from DeviceID to Epic Account

### Already Implemented (from Discord)
- [x] Setup Wizard with tooltips (CometDev request) - v0.8.0
- [x] Sanctions toggle during handshake (Duck request) - EOSSanctions.cs

---

## Future Considerations

### Offline Mode (FishNet Pro Killer Feature) ✅ DONE
- [x] **Implement EOSOfflineServer/Client** - Local loopback, no EOS needed
- [x] `transport.StartOffline()` - Starts server + client locally
- [x] `transport.IsOfflineMode` - Check if running offline
- [x] `transport.StopOffline()` - Stop offline mode
- [x] `transport.OfflineFallback = true` - Auto-fallback if EOS fails
- [ ] Optional simulated latency/packet loss for testing (future)
- Note: FishNet Pro charges for this - we give it free with EOS transport!

### v1.1.0 - Planned Enhancements
- [ ] Voice recording in replays
- [ ] Replay sharing via cloud (shareable codes)
- [ ] Server-side anti-cheat (EOSAntiCheatServer)
- [ ] Epic Account linking (upgrade from DeviceID)

### Mobile Testing
- [ ] Test on actual Android device
- [ ] Test Quest crossplay with Windows
- [ ] iOS testing

### Quest-Specific Issues (from Discord - Knot)
- **OnApplicationQuit doesn't fire on Quest** - Can't send leave message on quit
- **Host migration timeout** - If host quits without leave, EOS takes ~30 seconds to timeout
- Workaround: Fire leave on `OnApplicationPause` / `OnApplicationFocus(false)`
- Quest has a "save event" before quit - investigate for leave message
- Goal: Match Photon's host migration UX

### Host Migration Optimization Notes (from Discord)
- **Object pooling hack** - Pool objects without SetActive(false), freeze in place during migration
- **SyncVar caching** - We cache to structs every frame (generics, not reflection)
- **Knot's approach** - Uses reflection to get SyncVar<> fields, saves on OnPromoted
- **Our approach** - Save continuously, OnPromoted was "too late" for some reason (order issue?)
- **Performance** - Reflection is fine for <1000 SyncVars, FishNet would lag first
- **Investigate** - Can we get SyncVars without reflection on nested NetworkBehaviours?

### Stats-Based Name Storage (Optional)
Store display names via 20 EOS stats for DeviceID users:
- [ ] Create stats `name01`-`name20` in Developer Portal
- [ ] Implement `IngestPlayerName()` (20 chars as ASCII stats)
- Note: Currently using deterministic "AngryPanda42" names instead
