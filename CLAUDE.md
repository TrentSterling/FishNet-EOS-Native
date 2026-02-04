# CLAUDE.md

Guidance for Claude Code working with this repository. Historical/reference docs moved to `ARCHIVE.md`.

## Project Overview

**FishNet-EOS-Native** - Standalone Transport for FishNet using Epic Online Services (EOS) directly via raw C# SDK. No PlayEveryWare dependency.

**Unity Version:** 6000.0.65f1 (Unity 6)
**Version Control:** Git

## The Golden Rule

**DO NOT use the `PlayEveryWare` namespace.** Use `Epic.OnlineServices` directly. See [ARCHIVE.md](ARCHIVE.md) for reference project links.

## Architecture

### The Stack

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

### Key Constants

| Constant | Value |
|----------|-------|
| P2P Max Packet | 1170 bytes |
| Packet Header | 7 bytes |
| Client Host ID | 32767 (short.MaxValue) |
| Connection Timeout | 25 seconds |
| Lobby Max Members | 64 |

### EOS Service Limits

| Limit | Value |
|-------|-------|
| Max players/lobby | 64 |
| Max lobbies/user | 16 |
| Create/Join rate | 30/min |
| Attribute updates | 100/min |
| Attribute value length | 1000 chars |
| Voice participants | 64 (SDK 1.16+) |

## File Structure

```
Assets/FishNet.Transport.EOSNative/
├── CORE (11 files)
│   ├── EOSManager.cs              # SDK init, Tick, device login
│   ├── EOSConfig.cs               # ScriptableObject credentials
│   ├── EOSNativeTransport.cs      # Main transport + lobby API
│   ├── EOSServer.cs / EOSClient.cs / EOSClientHost.cs
│   ├── EOSPlayerRegistry.cs       # Player cache + local friends
│   ├── EOSInputHelper.cs          # Input type detection for matchmaking
│   ├── PacketFragmenter.cs        # >1170 byte packets
│   └── Connection.cs / LocalPacket.cs
│
├── Debug/ (2 files)
│   ├── EOSDebugSettings.cs        # ScriptableObject + DebugCategory enum
│   └── EOSDebugLogger.cs          # Centralized logging utility
│
├── UI (5 files)
│   ├── EOSNativeUI.cs             # F1 - main debug UI
│   ├── DebugUI/EOSVoiceDebugPanel.cs      # F3
│   ├── DebugUI/EOSNetworkDebugPanel.cs    # F4
│   └── EOSNetworkPlayer.cs
│
├── Lobbies/ (3 files) - EOSLobbyManager, EOSLobbyChatManager, LobbyData
├── Voice/ (5 files) - EOSVoiceManager, EOSVoicePlayer, FishNetVoicePlayer, EOSVoiceZoneManager, EOSVoiceTriggerZone
├── Migration/ (5 files) - HostMigratable, HostMigrationManager, HostMigrationPlayerSpawner, HostMigrationTester, DoNotMigrate
├── Social/ (15 files) - Friends, Presence, UserInfo, CustomInvites, Stats, Leaderboards, EOSMatchHistory, EOSRankedMatchmaking, RankedData, EOSLFGManager, EOSTournamentManager, EOSSeasonManager, EOSClanManager, EOSGlobalChatManager
├── Storage/ (2 files) - EOSPlayerDataStorage, EOSTitleStorage
├── Party/ (1 file) - EOSPartyManager    # Persistent party groups
├── Replay/ (11 files) - EOSReplayRecorder, EOSReplayPlayer, EOSReplayStorage, EOSReplayViewer, ReplayDataTypes, ReplayRecordable, ReplayGhost, ReplayMigration, EOSReplaySettings, EOSReplayVoiceRecorder, EOSReplayVoicePlayer
├── AntiCheat/ (1 file) - EOSAntiCheatManager
├── EOSVoteKickManager.cs         # Player vote kick system
├── EOSMapVoteManager.cs          # Map/mode voting system
├── EOSSpectatorMode.cs           # Spectator camera system
├── Editor/ (4 files) - EOSNativeTransportEditor, EOSNativeMenu, EOSSetupWizard, EOSDebugSettingsWindow
└── Demo/ (5 files) - PlayerBall, NetworkPhysicsObject, PlayerSpawner, etc.
```

## Quick Start

### Setup

1. Add `EOSNativeTransport` component to GameObject
2. **Auto-created:** NetworkManager, EOSManager, EOSLobbyManager, EOSVoiceManager, HostMigrationManager
3. Configure credentials via `Tools > FishNet EOS Native > Setup Wizard`
4. Enter Play Mode → auto-initializes

### API

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Simple host (generates random code, or pass any string)
var (result, lobby) = await transport.HostLobbyAsync();
var (result, lobby) = await transport.HostLobbyAsync("1234");
var (result, lobby) = await transport.HostLobbyAsync("MyRoom");  // Any string works

// Host with options (recommended)
var (result, lobby) = await transport.HostLobbyAsync(new LobbyOptions
{
    LobbyName = "My Room",
    GameMode = "deathmatch",
    MaxPlayers = 8
});

// Fluent style
var (result, lobby) = await transport.HostLobbyAsync(
    new LobbyOptions()
        .WithName("My Room")
        .WithGameMode("deathmatch")
        .WithMaxPlayers(8)
);

// Host with EOS LobbyId as code (guaranteed unique, good for chat history)
var (result, lobby) = await transport.HostLobbyAsync(
    new LobbyOptions { UseEosLobbyId = true }
);

// Join
var (result, lobby) = await transport.JoinLobbyAsync("1234");
var (result, lobby) = await transport.JoinLobbyByNameAsync("Pro Players Only");

// Quick Match (any lobby)
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync();

// Quick Match with filters (same options used for search AND host fallback)
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(
    new LobbyOptions()
        .WithGameMode("deathmatch")
        .WithRegion("us-east")
        .WithMaxPlayers(8)
        .ExcludeFull()
);

// Leave
await transport.LeaveLobbyAsync();

// State
if (transport.IsInLobby) { }
if (transport.IsLobbyOwner) { }
```

### Lobby Search

```csharp
var options = new LobbyOptions()
    .WithGameMode("ranked")
    .WithRegion("us-east")
    .ExcludePassworded()
    .WithMaxResults(20);

var (result, lobbies) = await transport.SearchLobbiesAsync(options);

// Search by name
var (result, lobbies) = await transport.SearchLobbiesByNameAsync("Pro", exactMatch: false);
```

### Offline Mode (Singleplayer)

Run without EOS - no login required. Perfect for singleplayer, testing, or offline fallback.

```csharp
var transport = GetComponent<EOSNativeTransport>();

// Start offline (server + client locally, no network)
transport.StartOffline();

// All FishNet features work normally
// NetworkObjects, RPCs, SyncVars - all local

// Check mode
if (transport.IsOfflineMode) { }

// Stop offline mode
transport.StopOffline();
// Or just: transport.Shutdown();
```

### Testing with ParrelSync

1. Main Editor: Host lobby
2. Clone: Join with same code
3. Both show connected within seconds

### Local Friends

Local friends are players you've marked from the Recently Played list. They persist locally in PlayerPrefs and sync to EOS Cloud Storage (400MB per player) for cross-device support.

```csharp
var registry = EOSPlayerRegistry.Instance;

// Check/toggle friend status
if (registry.IsFriend(puid)) { }
registry.AddFriend(puid);
registry.RemoveFriend(puid);
registry.ToggleFriend(puid);

// Get all friends
var friends = registry.GetFriends(); // List<(string puid, string name)>

// Cloud sync (auto-syncs on add/remove, but can manually trigger)
await registry.SyncFriendsToCloudAsync();    // Upload to cloud
await registry.LoadFriendsFromCloudAsync();  // Download from cloud (merges)
await registry.FullCloudSyncAsync();         // Two-way sync

// Events
registry.OnFriendChanged += (puid, isNowFriend) => { };

// Block list
registry.BlockPlayer(puid);
registry.UnblockPlayer(puid);
if (registry.IsBlocked(puid)) { }
var blocked = registry.GetBlockedPlayers();

// Friend notes
registry.SetNote(puid, "Met in ranked match");
string note = registry.GetNote(puid);
```

**UI Integration:**
- Recently Played shows [★]/[☆] to toggle friend status + platform icon
- LOCAL FRIENDS section shows friends with status, platform, [Invite] and [Remove] buttons
- INVITES section has Quick Send buttons for friends
- Cloud sync button (☁) syncs friends across devices

### Match History

Track games played with participants and outcomes.

```csharp
var history = EOSMatchHistory.Instance;

// Start tracking when game begins
history.StartMatch("deathmatch", "dust2");

// Add participants as they join
history.AddParticipant(puid, "PlayerName", team: 1);

// Update scores during match
history.UpdateLocalScore(score: 15, team: 1);
history.UpdateParticipantScore(puid, score: 12);

// End match
history.EndMatch(MatchOutcome.Win, winnerPuid: localPuid);

// Query history
var recent = history.GetRecentMatches(10);
var (wins, losses, draws, total) = history.GetLocalStats();
```

### Spectator Mode

Watch games without participating.

```csharp
var spectator = EOSSpectatorMode.Instance;

// Join as spectator (won't spawn player)
await spectator.JoinAsSpectatorAsync("1234");

// Or enter spectator mode after joining normally
spectator.EnterSpectatorMode();

// Controls (automatic):
// - Click/Arrow keys: Cycle between players
// - F: Toggle free camera mode
// - WASD/QE: Move in free camera
// - Right-click drag: Look around

// API
spectator.CycleTarget(1);  // Next player
spectator.SetTarget(networkObject);
string name = spectator.GetCurrentTargetName();
spectator.ExitSpectatorMode();
```

### Platform Detection

Players share their platform when joining lobbies.

```csharp
// Get current platform
EOSPlatformType platform = EOSPlatformHelper.CurrentPlatform;
string platformId = EOSPlatformHelper.PlatformId; // "WIN", "AND", "IOS", etc.

// Get player's platform
string playerPlatform = EOSPlayerRegistry.Instance.GetPlatform(puid);
string icon = EOSPlayerRegistry.GetPlatformIcon(playerPlatform); // 🖥️, 📱, 👓
string name = EOSPlayerRegistry.GetPlatformName(playerPlatform); // "Windows", "Android", "Quest"

// Platform checks
if (EOSPlatformHelper.IsMobile) { }
if (EOSPlatformHelper.IsVR) { }
if (EOSPlatformHelper.SupportsVoice) { }

// Platform filtering for lobby search
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().SamePlatformOnly()   // Same platform as host
    // or .DesktopOnly()                     // Windows/Mac/Linux only
    // or .MobileOnly()                      // Android/iOS/Quest only
    // or .WithPlatformFilter("WIN")         // Specific platform
);
```

### Input-Based Matchmaking

Match players by input device for fair competitive play.

```csharp
// Get current input type
InputType type = EOSInputHelper.CurrentInputType;
string typeId = EOSInputHelper.InputTypeId; // "KBM", "CTL", "TCH", "VRC"

// Input checks
if (EOSInputHelper.IsKeyboardMouse) { }
if (EOSInputHelper.IsController) { }
if (EOSInputHelper.IsTouch) { }
if (EOSInputHelper.IsVR) { }

// Display info
string name = EOSInputHelper.GetInputTypeName(type); // "Keyboard & Mouse"
string icon = EOSInputHelper.GetInputTypeIcon(type); // emoji

// Input filtering for lobby search
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().SameInputOnly()      // Same input as host
    // or .KeyboardMouseOnly()               // KBM only
    // or .ControllerOnly()                  // Controller only
    // or .FairInputOnly()                   // Compatible inputs (KBM vs KBM, CTL vs CTL/Touch)
    // or .WithInputFilter("CTL")            // Specific input type
);

// Listen for input changes
EOSInputHelper.OnInputTypeChanged += (newType) => { };

// Check if inputs are "fair" for matchmaking
bool fair = EOSInputHelper.AreInputTypesFair(InputType.Controller, InputType.Touch);
// Returns: true (controller and touch are compatible)
```

### Toast Notifications

Non-intrusive popup messages for events.

```csharp
// Show toasts
EOSToastManager.Info("Player joined");
EOSToastManager.Success("Connected", "Lobby code: 1234");
EOSToastManager.Warning("High ping detected");
EOSToastManager.Error("Connection lost");

// Configure
EOSToastManager.Instance.Position = ToastPosition.TopRight;
EOSToastManager.Instance.DefaultDuration = 3f;
EOSToastManager.ClearAll();
```

Auto-integration via `EOSToastIntegration` shows toasts for lobby events, invites, and friend changes.

### Voice Chat Zones

Control who can hear who with proximity, team, or custom zone modes.

```csharp
var zoneManager = EOSVoiceZoneManager.Instance;

// Set zone mode
zoneManager.SetZoneMode(VoiceZoneMode.Global);     // Everyone hears everyone
zoneManager.SetZoneMode(VoiceZoneMode.Proximity);  // Distance-based volume
zoneManager.SetZoneMode(VoiceZoneMode.Team);       // Teammates only
zoneManager.SetZoneMode(VoiceZoneMode.TeamProximity); // Team + distance
zoneManager.SetZoneMode(VoiceZoneMode.Custom);     // Trigger-based rooms

// Proximity settings
zoneManager.MaxHearingDistance = 30f;  // Max range
zoneManager.FadeStartDistance = 10f;   // Start fading
zoneManager.ConfigureProximity(maxDistance: 30f, fadeStart: 10f);

// Team settings
zoneManager.SetTeam(1);  // Local player's team
zoneManager.SetPlayerTeam(remotePuid, 2);  // Remote player's team
zoneManager.ConfigureTeam(allowCrossTeam: true, crossTeamMultiplier: 0.25f);

// Position tracking (auto-discovers FishNet NetworkObjects tagged "Player")
zoneManager.RegisterLocalPlayer(localTransform);
zoneManager.RegisterPlayer(remotePuid, remoteTransform);

// Custom zones (use EOSVoiceTriggerZone component on trigger colliders)
zoneManager.SetLocalZone("room1");
zoneManager.SetPlayerZone(remotePuid, "room2");

// Queries
float volume = zoneManager.GetPlayerVolume(puid);
bool canHear = zoneManager.IsPlayerInRange(puid);
var hearable = zoneManager.GetPlayersInRange();
float dist = zoneManager.GetDistanceToPlayer(puid);
```

**Events:**
```csharp
zoneManager.OnZoneModeChanged += (mode) => { };
zoneManager.OnPlayerVolumeChanged += (puid, volume) => { };
zoneManager.OnPlayerEnteredRange += (puid) => { };  // Proximity
zoneManager.OnPlayerExitedRange += (puid) => { };   // Proximity
```

### Party System

Persistent groups that follow the leader across games.

```csharp
var party = EOSPartyManager.Instance;

// Create/Join
await party.CreatePartyAsync("My Party", maxSize: 4);
await party.JoinPartyAsync("ABC123");  // 6-char party code

// Invite friends
await party.InviteToPartyAsync(friendPuid);

// Leadership
await party.PromoteToLeaderAsync(memberPuid);
await party.KickMemberAsync(memberPuid);

// Game following (leader calls this when joining a game)
await party.LeaderJoinGameAsync("1234");  // Members auto-follow

// Members can manually follow
await party.FollowLeaderAsync();

// Ready check
party.StartReadyCheck("1234");
await party.RespondToReadyCheckAsync(true);

// Chat
await party.SendPartyChatAsync("Let's go!");

// Leave/Dissolve
await party.LeavePartyAsync();
await party.DissolvePartyAsync();  // Leader only
```

**Configuration Options:**
```csharp
// Follow modes
party.FollowMode = PartyFollowMode.Automatic;  // Auto-follow leader
party.FollowMode = PartyFollowMode.Confirm;    // Prompt before follow
party.FollowMode = PartyFollowMode.ReadyCheck; // Ready check first
party.FollowMode = PartyFollowMode.Manual;     // Manual only

// Full lobby behavior
party.FullLobbyBehavior = PartyFullLobbyBehavior.BlockJoin;   // Can't join
party.FullLobbyBehavior = PartyFullLobbyBehavior.WarnAndAsk;  // Ask leader
party.FullLobbyBehavior = PartyFullLobbyBehavior.PartialJoin; // Join who fits
party.FullLobbyBehavior = PartyFullLobbyBehavior.LeaderOnly;  // Leader solo

// Persistence
party.Persistence = PartyPersistence.SessionBased; // Dissolves on quit
party.Persistence = PartyPersistence.Persistent;   // Lives forever
party.Persistence = PartyPersistence.TimedExpiry;  // Expires when idle

// Other settings
party.AfkTimeout = 10f;
party.SeparatePartyVoice = true;
party.AutoPromoteOnLeaderLeave = true;
party.AllowPublicJoin = false;
party.FriendsOnly = true;
```

**Events:**
```csharp
party.OnMemberJoined += (member) => { };
party.OnMemberLeft += (puid) => { };
party.OnLeaderChanged += (oldPuid, newPuid) => { };
party.OnLeaderJoinedGame += (gameCode) => { };
party.OnFollowRequested += (request) => { };  // For Confirm mode
party.OnReadyCheckStarted += (data) => { };
party.OnReadyCheckCompleted += (allReady) => { };
```

### LFG (Looking for Group)

Create and browse LFG posts to find players or groups.

```csharp
var lfg = EOSLFGManager.Instance;

// Create a post
var (result, post) = await lfg.CreatePostAsync("Looking for ranked players");

// Create with options
var (result, post) = await lfg.CreatePostAsync(new LFGPostOptions()
    .WithTitle("Need 2 for competitive")
    .WithGameMode("ranked")
    .WithDesiredSize(4)
    .RequiresVoice(true)
);

// Search for posts
var (result, posts) = await lfg.SearchPostsAsync(new LFGSearchOptions()
    .WithGameMode("ranked")
    .WithRegion("us-east")
);

// Send join request
await lfg.SendJoinRequestAsync(post.PostId);

// Accept/reject requests (post owner)
await lfg.AcceptJoinRequestAsync(request);
await lfg.RejectJoinRequestAsync(request);

// Manage your post
await lfg.UpdatePostStatusAsync(LFGStatus.Full);
await lfg.ClosePostAsync();

// Properties
if (lfg.HasActivePost) { var post = lfg.ActivePost; }
var requests = lfg.PendingRequests;
var results = lfg.SearchResults;
```

**Events:**
```csharp
lfg.OnPostCreated += (post) => { };
lfg.OnJoinRequestReceived += (request) => { };
lfg.OnJoinRequestAccepted += (post) => { };
lfg.OnSearchResultsReceived += (posts) => { };
```

### Tournament Brackets

Organize competitive tournaments with single/double elimination or round robin.

```csharp
var tm = EOSTournamentManager.Instance;

// Create tournament
var t = tm.CreateTournament(new TournamentOptions()
    .WithName("Friday Night Cup")
    .WithFormat(TournamentFormat.DoubleElimination)
    .WithSeeding(SeedingMethod.ByRating)
    .WithBestOf(3)
    .WithGrandFinalsBestOf(5)
);

// Register participants
tm.RegisterParticipant(puid, "PlayerName");
tm.RegisterTeam(teamId, "Team Alpha", memberPuids);

// Start (generates bracket)
tm.StartTournament();

// Get current matches
var matches = tm.GetCurrentRoundMatches();

// Report results
tm.ReportMatchResult(matchId, winnerId, winnerScore: 2, loserScore: 1);
// Bracket auto-advances

// Get bracket visualization data
var bracket = tm.GetBracketData(BracketType.Winners);
foreach (var round in bracket.Rounds)
{
    Debug.Log($"--- {round.RoundName} ---");
    foreach (var match in round.Matches) { }
}

// Get standings
var standings = tm.GetStandings();
```

**Formats:**
- `TournamentFormat.SingleElimination` - One loss out
- `TournamentFormat.DoubleElimination` - Losers bracket + grand finals
- `TournamentFormat.RoundRobin` - Everyone plays everyone

**Events:**
```csharp
tm.OnTournamentStarted += (t) => { };
tm.OnTournamentEnded += (t, winner) => { };
tm.OnMatchReady += (match) => { };
tm.OnMatchCompleted += (match) => { };
tm.OnParticipantEliminated += (p) => { };
tm.OnRoundAdvanced += (round) => { };
```

### Chat History

Chat messages persist to cloud and reload when rejoining the same lobby.

```csharp
var chat = EOSLobbyChatManager.Instance;

// Messages auto-save when leaving lobby
// Messages auto-load when joining lobby with same code

// Manual control
await chat.SaveChatHistoryAsync("1234");
await chat.LoadChatHistoryAsync("1234");
await chat.DeleteChatHistoryAsync("1234");
```

### Ranked Matchmaking

Skill-based matchmaking with multiple rating algorithms and tier display.

```csharp
var ranked = EOSRankedMatchmaking.Instance;

// Find a ranked match (searches by skill range, expands if not found)
var (result, lobby) = await ranked.FindRankedMatchAsync("ranked");

// Host a ranked lobby at your skill level
var (result, lobby) = await ranked.HostRankedLobbyAsync("ranked");

// Find or host (tries to join first, hosts if none found)
var (result, lobby, didHost) = await ranked.FindOrHostRankedMatchAsync("ranked");

// Record match results (updates rating)
var ratingChange = await ranked.RecordMatchResultAsync(
    MatchOutcome.Win,
    opponentRating: 1450
);

// Rating info
int rating = ranked.CurrentRating;           // e.g., 1350
RankTier tier = ranked.CurrentTier;          // e.g., Gold
RankDivision division = ranked.CurrentDivision; // e.g., II
string display = ranked.GetCurrentRankDisplayName(); // "Gold II" or "1350"

// Player stats
var data = ranked.PlayerData;
int wins = data.Wins;
int losses = data.Losses;
float winRate = data.WinRate;
int peak = data.PeakRating;

// Placement (first 10 games)
bool placed = ranked.IsPlaced;
```

**Configuration (Inspector or code):**
```csharp
// Rating algorithms
ranked.SetAlgorithm(RatingAlgorithm.ELO);      // Standard ELO with K-factor
ranked.SetAlgorithm(RatingAlgorithm.Glicko2);  // Glicko-2 with uncertainty
ranked.SetAlgorithm(RatingAlgorithm.SimpleMMR); // Fixed points + streak bonuses

// Tier display modes
ranked.SetTierDisplayMode(TierDisplayMode.SixTier);    // Bronze→Champion
ranked.SetTierDisplayMode(TierDisplayMode.EightTier);  // Iron→Grandmaster
ranked.SetTierDisplayMode(TierDisplayMode.NumbersOnly); // Just show rating
```

**Events:**
```csharp
ranked.OnRatingChanged += (change) => {
    Debug.Log($"Rating: {change.OldRating} → {change.NewRating} ({change.Change:+0;-0})");
};
ranked.OnPromotion += (tier, division) => { };
ranked.OnDemotion += (tier, division) => { };
ranked.OnPlacementCompleted += (rating, tier, division) => { };
ranked.OnMatchFound += (lobby) => { };
```

**Tier Thresholds (6-Tier):**
| Tier | Rating |
|------|--------|
| Champion | 2200+ |
| Diamond | 1900+ |
| Platinum | 1600+ |
| Gold | 1300+ |
| Silver | 1000+ |
| Bronze | 0+ |

### Replay System

Record game sessions and play them back with timeline controls. Auto-integrates with EOSMatchHistory and EOSSpectatorMode.

```csharp
var recorder = EOSReplayRecorder.Instance;
var player = EOSReplayPlayer.Instance;
var storage = EOSReplayStorage.Instance;
var viewer = EOSReplayViewer.Instance;

// Recording (auto-starts with EOSMatchHistory.StartMatch if AutoRecord enabled)
recorder.StartRecording(matchId);
recorder.RecordEvent("goal_scored", "{\"team\":1}");  // Custom events
var replay = await recorder.StopAndSaveAsync();

// Manual control
recorder.AutoRecord = true;   // Toggle auto-recording with matches
recorder.FrameRate = 20f;     // Frames per second (default: 20)

// Storage
var replays = storage.GetLocalReplays();              // List<ReplayHeader>
var replay = await storage.LoadLocalAsync(replayId);
storage.DeleteReplay(replayId);

// Cloud storage (optional)
await storage.UploadToCloudAsync(replay);
var cloudReplays = await storage.GetCloudReplaysAsync();
var replay = await storage.DownloadFromCloudAsync(replayId);

// Playback via EOSReplayViewer (recommended - includes spectator camera)
viewer.StartViewing(replay);
viewer.TogglePlayPause();
viewer.Seek(30f);              // Seek to 30 seconds
viewer.SeekPercent(0.5f);      // Seek to 50%
viewer.Skip(10f);              // Skip forward 10 seconds
viewer.SetSpeed(2f);           // 2x speed
viewer.CycleSpeed();           // Cycle: 0.5x → 1x → 2x → 4x
viewer.CycleTarget(1);         // Next player
viewer.StopViewing();

// Or use EOSReplayPlayer directly (no camera)
player.LoadReplay(replay);
player.Play();
player.Pause();
player.Stop();
player.Seek(time);
player.PlaybackSpeed = 2f;
var objects = player.GetPlayerObjects();  // For custom camera targeting
```

**Recording Settings:**
```csharp
// Mark specific objects for recording
gameObject.AddComponent<ReplayRecordable>();

// Customize recording behavior
var recordable = GetComponent<ReplayRecordable>();
recordable.RecordEnabled = true;           // Include in replays
recordable.PositionThreshold = 0.001f;     // Min position change
recordable.RotationThreshold = 0.1f;       // Min rotation change (degrees)
```

**Events:**
```csharp
recorder.OnRecordingStarted += (matchId) => { };
recorder.OnRecordingStopped += (replay) => { };
recorder.OnFrameRecorded += (frameCount, timestamp) => { };
recorder.OnQualityWarning += (warning) => { };      // HighPing, VeryHighPing, LowFrameRate
recorder.OnDurationWarning += (current, max) => { }; // Approaching time limit
recorder.OnAutoStopped += (reason) => { };          // Hit max duration

player.OnTimeChanged += (time) => { };
player.OnStateChanged += (state) => { };  // Playing, Paused, Stopped
player.OnReplayEnded += () => { };

viewer.OnViewingStarted += (header) => { };
viewer.OnViewingStopped += () => { };
```

**Favorites & Export:**
```csharp
// Mark replays as favorites (protected from auto-cleanup)
storage.ToggleFavorite(replayId);
storage.AddFavorite(replayId);
storage.RemoveFavorite(replayId);
if (storage.IsFavorite(replayId)) { }

// Export for manual sharing (copies to Documents/Replays)
string path = await storage.ExportReplayAsync(replayId);
storage.OpenExportFolder();  // Opens in file browser

// Import shared replay files
bool success = await storage.ImportReplayAsync(filePath);
```

**Recording Limits (configurable in EOSReplaySettings):**
```csharp
// Monitor during recording
float duration = recorder.Duration;
float maxDuration = recorder.MaxDuration;      // Default: 30 minutes
float estimatedKB = recorder.EstimatedSizeKB;
bool warning = recorder.IsApproachingLimit;    // Within 5 min of limit
```

**Keyboard Shortcuts (during playback):**
| Key | Action |
|-----|--------|
| Space | Play/Pause |
| ← / → | Skip -10s / +10s |
| Home / End | Jump to start / end |
| 1-4 | Set speed (0.5x, 1x, 2x, 4x) |
| Tab | Cycle target player |
| Escape | Stop viewing |

**Storage Limits:**
| Storage | Limit |
|---------|-------|
| Local replays | 50 (oldest auto-deleted, favorites exempt) |
| Cloud replays | 10 (oldest auto-deleted) |
| Max duration | 30 minutes (auto-stops) |
| File size | ~500KB for 10-min match |

**Voice Recording:**
```csharp
var voiceRecorder = EOSReplayVoiceRecorder.Instance;
var voicePlayer = EOSReplayVoicePlayer.Instance;

// Voice recording is automatic when replay recording starts
voiceRecorder.Enabled = true;  // Enabled by default

// Voice playback during replay viewing
voicePlayer.MasterVolume = 0.8f;
voicePlayer.SetSpeakerVolume(puid, 0.5f);
voicePlayer.SetSpeakerMuted(puid, true);

// Events
voicePlayer.OnSpeakerStarted += (puid, name) => { };
voicePlayer.OnSpeakerStopped += (puid) => { };
```

**Data Compression:**
- Position: Half-precision floats (6 bytes vs 12)
- Rotation: Smallest-three quaternion (4 bytes vs 16)
- Frames: Delta compression (only changed objects)
- File: GZip compression (~60% reduction)

### Anti-Cheat (EOS EAC)

Easy Anti-Cheat integration for client integrity validation.

```csharp
var antiCheat = EOSAntiCheatManager.Instance;

// Session lifecycle (auto-managed when AutoStartSession is true)
antiCheat.BeginSession();   // Start protection
antiCheat.EndSession();     // End protection

// Peer registration (call when players join/leave)
IntPtr handle = antiCheat.RegisterPeer(puid);
antiCheat.UnregisterPeer(puid);

// Status checking
if (antiCheat.IsSessionActive) { }
if (antiCheat.Status == AntiCheatStatus.Protected) { }
AntiCheatClientViolationType violation = antiCheat.PollStatus();

// Message handling (for P2P anti-cheat messages)
antiCheat.ReceiveMessageFromPeer(peerHandle, data);
while (antiCheat.TryGetOutgoingMessage(out var peer, out var data))
{
    // Send data to peer over network
}
```

**Events:**
```csharp
antiCheat.OnSessionStarted += () => { };
antiCheat.OnSessionEnded += () => { };
antiCheat.OnIntegrityViolation += (type, message) => { /* Local client cheating */ };
antiCheat.OnPeerActionRequired += (handle, action, reason) => { /* Kick cheater */ };
antiCheat.OnPeerAuthStatusChanged += (handle, status) => { };
```

**Status Values:**
| Status | Description |
|--------|-------------|
| NotInitialized | EAC not yet initialized |
| NotAvailable | EAC not configured in portal |
| Initialized | Ready but no session active |
| Protected | Session active, protection enabled |
| Violated | Integrity violation detected |
| Error | Error state |

**Requirements:**
- Configure EAC in EOS Developer Portal
- Run integrity tool during build to generate catalogs
- Windows/Mac/Linux only (mobile not supported)

### Vote Kick

Allow players to vote to remove disruptive players.

```csharp
var voteKick = EOSVoteKickManager.Instance;

// Start a vote kick
var (success, error) = await voteKick.StartVoteKickAsync(targetPuid, "Reason");
var (success, error) = await voteKick.StartVoteKickAsync(connectionId, "Reason");

// Cast your vote
await voteKick.CastVoteAsync(true);   // Vote YES (kick)
await voteKick.CastVoteAsync(false);  // Vote NO (keep)

// Host actions
await voteKick.VetoVoteAsync();       // Cancel vote, keep player
await voteKick.CancelVoteAsync();     // Initiator can cancel their own vote

// Check status
if (voteKick.IsVoteActive) { }
bool canKick = voteKick.CanBeVoteKicked(puid);
bool voted = voteKick.HasVoted();
int required = voteKick.GetRequiredYesVotes();
float cooldown = voteKick.GetCooldownRemaining();
```

**Configuration (Inspector or code):**
```csharp
// Threshold options
voteKick.Threshold = VoteThreshold.Majority;      // >50%
voteKick.Threshold = VoteThreshold.TwoThirds;     // >=67%
voteKick.Threshold = VoteThreshold.ThreeQuarters; // >=75%
voteKick.Threshold = VoteThreshold.Unanimous;     // 100%
voteKick.Threshold = VoteThreshold.Custom;
voteKick.CustomThresholdPercent = 60;

// Other settings
voteKick.VoteTimeout = 30f;           // Seconds before vote expires
voteKick.VoteCooldown = 60f;          // Cooldown between starting votes
voteKick.MinPlayersForVote = 3;       // Minimum players required
voteKick.HostImmunity = true;         // Host cannot be vote kicked
voteKick.HostCanVeto = true;          // Host can veto any vote
voteKick.HostVoteWeight = 1;          // Host vote counts as N votes
voteKick.RequireReason = false;       // Require reason when starting
```

**Events:**
```csharp
voteKick.OnVoteStarted += (voteData) => { };
voteKick.OnVoteCast += (voterPuid, votedYes) => { };
voteKick.OnVoteProgress += (yesVotes, noVotes, totalEligible) => { };
voteKick.OnVoteEnded += (voteData, result) => { };  // Passed, Failed, Vetoed, TimedOut, Cancelled
voteKick.OnPlayerVoteKicked += (puid, name) => { };
```

**Vote Results:**
| Result | Description |
|--------|-------------|
| Passed | Enough YES votes, player kicked |
| Failed | Too many NO votes or abstains |
| Vetoed | Host used veto power |
| TimedOut | Vote expired before conclusion |
| Cancelled | Initiator or target left |

### Map/Mode Voting

Let players vote on the next map or game mode.

```csharp
var mapVote = EOSMapVoteManager.Instance;

// Simple map vote
await mapVote.StartMapVoteAsync("Vote for Next Map", "Dust 2", "Inferno", "Mirage");

// Simple mode vote
await mapVote.StartModeVoteAsync("Choose Game Mode", "Deathmatch", "CTF", "KOTH");

// Custom options
var options = new List<VoteOption>
{
    new VoteOption("dust2", "Dust 2", "map"),
    new VoteOption("inferno", "Inferno", "map"),
    new VoteOption("mirage", "Mirage", "map"),
};
await mapVote.StartVoteAsync("Vote for Next Map", options);

// Cast vote (by index or ID)
await mapVote.CastVoteAsync(0);          // Vote for first option
await mapVote.CastVoteByIdAsync("dust2"); // Vote by ID

// Check status
if (mapVote.IsVoteActive) { }
int myVote = mapVote.GetMyVote();         // -1 if not voted
int[] counts = mapVote.GetVoteCounts();   // Votes per option
List<int> leaders = mapVote.GetCurrentLeaders();

// Host controls
mapVote.ExtendTimer(15f);   // Add time
mapVote.EndVoteNow();       // End immediately
await mapVote.CancelVoteAsync();
```

**Configuration:**
```csharp
mapVote.VoteDuration = 30f;              // Seconds for voting
mapVote.AllowVoteChange = true;          // Can change vote
mapVote.ShowLiveResults = true;          // Show counts in real-time
mapVote.TieBreakerMode = TieBreaker.Random;  // Random, FirstOption, HostChoice, Revote
```

**Events:**
```csharp
mapVote.OnVoteStarted += (voteData) => { };
mapVote.OnVoteCast += (voterPuid, optionIndex) => { };
mapVote.OnTimerTick += (secondsRemaining) => { };
mapVote.OnVoteEnded += (voteData, winningOption, winningIndex) => { };
mapVote.OnTieNeedsDecision += (tiedOptions) => { };  // For HostChoice mode
```

## Coding Standards

- **Namespace:** `FishNet.Transport.EOSNative`
- **Platform Defines:** `#if UNITY_ANDROID`, `#if UNITY_STANDALONE_WIN`, `#if UNITY_EDITOR`
- **Error Handling:** Always check `result != Result.Success`
- **Async Pattern:** Prefer async/await with `TaskCompletionSource<T>` over coroutines

## Known Gotchas

### SDK Initialization
- `PlatformInterface.Initialize` only once per process - check `Result.AlreadyConfigured`
- After `Shutdown()`, cannot reinitialize - SDK calls fail
- `Tick()` must be called every frame or callbacks never fire
- `EncryptionKey` must be exactly 64 hex characters

### P2P Connections
- Max packet 1170 bytes - use PacketFragmenter
- Both sides need matching `SocketId.SocketName`
- Server must call `AcceptConnection` for incoming requests

### FishNet-Specific
- `CLIENT_HOST_ID = short.MaxValue` (32767) for host acting as client
- Transport inherits `FishNet.Transporting.Transport`

### DeviceID Limitation
PUIDs from DeviceID auth have no visible display names. We use deterministic "AngryPanda42" style names from PUID hash.

### Platform Issues
- **Windows Editor:** Dynamic DLL loading via `LoadLibrary`/`GetProcAddress`
- **Android:** Load `.so` via `AndroidJavaClass`, different path for Unity 6+
- **DLL Config:** Must configure x86/x64 DLLs in Inspector for correct platform

## Implementation Status

### Done
- EOS SDK Init, Device Token Login, Auto-Setup
- Transport, Server/Client/ClientHost, P2P
- Lobbies (custom join codes, search, attributes)
- Packet Fragmentation, Fast Disconnect
- Voice/RTC with Pitch Shifting (SMBPitchShifter) and 3D Spatial Audio (SpatialBlend, DopplerLevel)
- Text Chat (lobby-based, survives migration)
- Host Migration Framework (auto-migration by default, DoNotMigrate opt-out, scene object reset, player repossession)
- Cloud Storage, Stats, Leaderboards, Achievements
- Friends, Presence, CustomInvites
- Reports, Sanctions, Metrics
- Setup Wizard, Tools Menu
- **PhysicsNetworkTransform** - Spring-based physics sync (replaces ownership swapping)
- **PlayerBall simplified** - Uses PhysicsNetworkTransform, no input syncing
- **Centralized Debug Logging** - 29 categories, 9 groups, group muting, Editor window
- **Local Friends System** - Mark recently played players as friends, with cloud sync across devices
- **Ping Display** - Shows actual RTT from FishNet TimeManager (color-coded)
- **Invite UX** - Quick-send buttons for friends, star toggles in Recently Played
- **Friend Online Status** - Shows "In Lobby", "In Game", or "Offline" for local friends with Join button
- **Toast Notifications** - Non-intrusive popup system for events (EOSToastManager)
- **Block List System** - Block players with cloud sync, hidden from Recently Played
- **Quick Join Friend** - One-click join friend's current lobby
- **Friend Notes** - Add personal notes to friends (persists locally)
- **Connection Quality Indicator** - Shows ping, jitter, and quality rating (●●●●○)
- **Platform ID Mapping** - Shows platform icons for players (Windows/Mac/Linux/Android/iOS/Quest)
- **Lobby Chat History** - Chat persists to cloud, loads on rejoin
- **Match History** - Tracks games played, participants, outcomes (EOSMatchHistory)
- **Spectator Mode** - Watch games without participating, free camera or follow players
- **Host Migration Tester** - Runtime verification checklist for migration testing
- **Party System** - Persistent groups with follow-the-leader, ready checks, configurable modes
- **Ranked Matchmaking** - Skill-based matchmaking with ELO/Glicko-2/SimpleMMR, tier display, cloud-persisted ratings
- **Replay System** - Record/playback games with timeline controls, favorites, export/import, duration limits, quality warnings
- **Anti-Cheat (EAC)** - Easy Anti-Cheat integration with session management, peer validation, violation detection
- **Vote Kick** - Player voting to remove disruptive players, configurable thresholds, host veto, cooldowns
- **Map/Mode Voting** - End-of-match voting for next map/mode, tie breakers, timer, live results
- **Voice Chat Zones** - Proximity, team, and custom zone-based voice chat with trigger zones
- **LFG System** - Create/browse LFG posts, send/manage join requests, auto-expiring posts
- **Platform Filtering** - Filter lobby searches by host platform (same, desktop, mobile, specific)
- **Voice Recording in Replays** - Capture and playback voice chat during replay recording/viewing
- **Input-Based Matchmaking** - Match players by input device (KBM/Controller/Touch/VR) for fair competitive play
- **Tournament Brackets** - Single/double elimination, round robin, seeding, best-of series, bracket visualization
- **Seasons & Ranked Resets** - Competitive seasons with soft resets, reward tiers, season history, auto-transitions
- **Teams & Clans** - Persistent player groups with roles, invites, chat, member management, clan history
- **Global Chat Channels** - Cross-lobby chat channels with history, muting, system messages

### Next Up

(No major features currently planned - see GitHub issues for community requests)

## Debug Tools

### Debug Settings Window
`Tools > FishNet EOS Native > Debug Settings`
- 29 categories in 9 groups (Core, Lobby, Voice, Migration, Social, Stats, Storage, Moderation, Demo)
- Group mute toggles for quick filtering
- Settings stored in `Resources/EOSDebugSettings.asset`

### Runtime Debug Panels
- **F1** - Main UI (lobby, chat, stats, invites, local friends, recently played, match history, replays)
- **F3** - Voice debug (RTC, participants, levels)
- **F4** - Network debug (P2P, bandwidth, ping/jitter/quality, migration)

## Official Docs

- Portal: `dev.epicgames.com/portal`
- API Reference: `dev.epicgames.com/docs/api-ref`
- Credentials: Portal → Your Product → Product Settings
