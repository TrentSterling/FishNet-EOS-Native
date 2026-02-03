# Recording & Playback

Record game sessions and play them back with timeline controls.

## Overview

The replay system captures transform data for marked objects and allows playback with full timeline controls. It integrates with Match History and Spectator Mode.

## Recording

### Auto-Recording

Enable auto-recording to capture all matches:

```csharp
var recorder = EOSReplayRecorder.Instance;

recorder.AutoRecord = true;  // Records when EOSMatchHistory.StartMatch is called
```

### Manual Recording

```csharp
// Start recording
recorder.StartRecording(matchId);

// Record custom events during gameplay
recorder.RecordEvent("goal_scored", "{\"team\":1,\"scorer\":\"Player1\"}");

// Stop and save
var replay = await recorder.StopAndSaveAsync();
Debug.Log($"Saved replay: {replay.Header.ReplayId}");
```

### Recording Settings

```csharp
recorder.FrameRate = 20f;  // Frames per second (default: 20)
```

### Marking Objects for Recording

Add `ReplayRecordable` to objects you want captured:

```csharp
// Add component
gameObject.AddComponent<ReplayRecordable>();

// Or configure in inspector
var recordable = GetComponent<ReplayRecordable>();
recordable.RecordEnabled = true;
recordable.PositionThreshold = 0.001f;  // Min position change to record
recordable.RotationThreshold = 0.1f;    // Min rotation change (degrees)
```

## Storage

### Local Replays

```csharp
var storage = EOSReplayStorage.Instance;

// List all local replays
var replays = storage.GetLocalReplays();  // List<ReplayHeader>

foreach (var header in replays)
{
    Debug.Log($"{header.MatchId} - {header.Duration:mm\\:ss}");
    Debug.Log($"  Map: {header.Map}, Mode: {header.GameMode}");
    Debug.Log($"  Recorded: {header.RecordedAt}");
}

// Load a replay
var replay = await storage.LoadLocalAsync(replayId);

// Delete a replay
storage.DeleteReplay(replayId);
```

### Cloud Storage

```csharp
// Upload to cloud
await storage.UploadToCloudAsync(replay);

// List cloud replays
var cloudReplays = await storage.GetCloudReplaysAsync();

// Download from cloud
var replay = await storage.DownloadFromCloudAsync(replayId);
```

### Favorites

Favorites are protected from auto-cleanup:

```csharp
// Mark as favorite
storage.AddFavorite(replayId);
storage.RemoveFavorite(replayId);
storage.ToggleFavorite(replayId);

// Check status
if (storage.IsFavorite(replayId))
{
    // Protected from auto-deletion
}
```

### Export/Import

Share replays as files:

```csharp
// Export to Documents/Replays folder
string path = await storage.ExportReplayAsync(replayId);
Debug.Log($"Exported to: {path}");

// Open export folder
storage.OpenExportFolder();

// Import a replay file
bool success = await storage.ImportReplayAsync(filePath);
```

## Playback

### Using EOSReplayViewer (Recommended)

The viewer includes spectator camera integration:

```csharp
var viewer = EOSReplayViewer.Instance;

// Start viewing
viewer.StartViewing(replay);

// Controls
viewer.TogglePlayPause();
viewer.Seek(30f);           // Seek to 30 seconds
viewer.SeekPercent(0.5f);   // Seek to 50%
viewer.Skip(10f);           // Skip forward 10s
viewer.Skip(-10f);          // Skip back 10s

// Speed
viewer.SetSpeed(2f);        // 2x speed
viewer.CycleSpeed();        // Cycle: 0.5x → 1x → 2x → 4x

// Target player
viewer.CycleTarget(1);      // Next player
viewer.CycleTarget(-1);     // Previous player

// Stop
viewer.StopViewing();
```

### Using EOSReplayPlayer Directly

For custom playback without the viewer:

```csharp
var player = EOSReplayPlayer.Instance;

player.LoadReplay(replay);
player.Play();
player.Pause();
player.Stop();
player.Seek(time);
player.PlaybackSpeed = 2f;

// Get player objects for custom targeting
var objects = player.GetPlayerObjects();
```

## Keyboard Shortcuts

During playback:

| Key | Action |
|-----|--------|
| Space | Play/Pause |
| ← / → | Skip -10s / +10s |
| Home / End | Jump to start / end |
| 1 | 0.5x speed |
| 2 | 1x speed |
| 3 | 2x speed |
| 4 | 4x speed |
| Tab | Cycle target player |
| Escape | Stop viewing |

## Recording Limits

Configurable in EOSReplaySettings:

```csharp
// Monitor during recording
float duration = recorder.Duration;
float maxDuration = recorder.MaxDuration;      // Default: 30 min
float estimatedKB = recorder.EstimatedSizeKB;
bool warning = recorder.IsApproachingLimit;    // Within 5 min of limit
```

| Limit | Default |
|-------|---------|
| Max duration | 30 minutes |
| Max local replays | 50 (oldest auto-deleted) |
| Max cloud replays | 10 |
| Typical file size | ~500KB per 10 minutes |

## Data Compression

The system uses several compression techniques:
- **Position**: Half-precision floats (6 bytes vs 12)
- **Rotation**: Smallest-three quaternion (4 bytes vs 16)
- **Frames**: Delta compression (only changed objects)
- **File**: GZip compression (~60% reduction)

## Events

```csharp
// Recording
recorder.OnRecordingStarted += (matchId) => { };
recorder.OnRecordingStopped += (replay) => { };
recorder.OnFrameRecorded += (frameCount, timestamp) => { };
recorder.OnQualityWarning += (warning) => { };       // HighPing, LowFrameRate
recorder.OnDurationWarning += (current, max) => { }; // Approaching limit
recorder.OnAutoStopped += (reason) => { };           // Hit max duration

// Playback
player.OnTimeChanged += (time) => { };
player.OnStateChanged += (state) => { };  // Playing, Paused, Stopped
player.OnReplayEnded += () => { };

// Viewer
viewer.OnViewingStarted += (header) => { };
viewer.OnViewingStopped += () => { };
```
