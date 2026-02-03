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

### Auto-Install Dependencies (Duck)
- [ ] **Auto-install EOS SDK** - Detect if EOS SDK is missing and offer to install via Package Manager
- [ ] **Auto-install FishNet** - Detect if FishNet is missing and offer to install via Package Manager
- Note: Would make setup truly zero-friction - just import the transport and everything else gets pulled in automatically

### Lobby Code Flexibility (Duck)
- [ ] **Configurable lobby code format** - Not limited to 4-digit codes, allow any string
- [ ] **EOS token codes** - Option to use EOS-generated tokens (long random strings) as lobby identifiers
- [ ] **Name search via attributes** - Search lobbies by name attribute instead of just code
- Note: Make code format configurable (4-digit, 6-char alphanumeric, EOS token, custom)

### QuickMatch Improvements (Duck)
- [ ] **QuickMatch by attributes** - Filter QuickMatch by lobby attributes (game mode, map, region, etc.)
- [ ] **Attribute-based matchmaking** - Find lobbies matching specific criteria, not just "any available"
- Note: Currently QuickMatch finds any lobby; should support `QuickMatchOrHostAsync(LobbySearchOptions)`

### Voice Effects (Duck)
- [x] **Doppler effect** - Exposed in EOSVoicePlayer and FishNetVoicePlayer (spatialBlend, dopplerLevel 0-5, minDistance, maxDistance, rolloffMode)
- [x] **Pitch shift / voice changer** - SMBPitchShifter (STFT-based) integrated into EOSVoicePlayer (0.5 = octave down, 2.0 = octave up)
- Note: Current pitch shift is on playback side (per-listener). Future: add mic-side processing for "everyone hears your changed voice"

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

### v1.1.0 - Planned Enhancements
- [ ] Voice recording in replays
- [ ] Replay sharing via cloud (shareable codes)
- [ ] Server-side anti-cheat (EOSAntiCheatServer)
- [ ] Epic Account linking (upgrade from DeviceID)

### Mobile Testing
- [ ] Test on actual Android device
- [ ] Test Quest crossplay with Windows
- [ ] iOS testing

### Stats-Based Name Storage (Optional)
Store display names via 20 EOS stats for DeviceID users:
- [ ] Create stats `name01`-`name20` in Developer Portal
- [ ] Implement `IngestPlayerName()` (20 chars as ASCII stats)
- Note: Currently using deterministic "AngryPanda42" names instead
