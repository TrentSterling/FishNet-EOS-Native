# Spectator Mode

Watch games without participating.

## Joining as Spectator

### Join Lobby as Spectator

```csharp
var spectator = EOSSpectatorMode.Instance;

// Join a game as spectator (won't spawn player object)
await spectator.JoinAsSpectatorAsync("1234");
```

### Enter Spectator Mode While Playing

```csharp
// If you're already in a game, enter spectator mode
spectator.EnterSpectatorMode();

// Exit back to playing
spectator.ExitSpectatorMode();
```

## Camera Controls

### Automatic Controls

When in spectator mode, controls are active automatically:

| Input | Action |
|-------|--------|
| Click / Left Arrow | Previous player |
| Click / Right Arrow | Next player |
| F | Toggle free camera |
| WASD | Move (free camera) |
| Q / E | Down / Up (free camera) |
| Right-click drag | Look around |
| Scroll wheel | Zoom |

### API Controls

```csharp
// Cycle through players
spectator.CycleTarget(1);   // Next
spectator.CycleTarget(-1);  // Previous

// Target specific player
spectator.SetTarget(networkObject);
spectator.SetTarget(puid);

// Get current target
string name = spectator.GetCurrentTargetName();
NetworkObject target = spectator.CurrentTarget;

// Free camera
spectator.EnableFreeCamera();
spectator.DisableFreeCamera();
spectator.ToggleFreeCamera();
```

## Camera Modes

### Follow Mode (Default)

Camera follows the targeted player with smooth interpolation:

```csharp
spectator.CameraMode = SpectatorCameraMode.Follow;

// Configure follow settings
spectator.FollowDistance = 5f;
spectator.FollowHeight = 2f;
spectator.FollowSmoothness = 5f;
```

### Free Camera Mode

Fly around the scene freely:

```csharp
spectator.CameraMode = SpectatorCameraMode.Free;

// Configure free camera
spectator.FreeMoveSpeed = 10f;
spectator.FreeLookSensitivity = 2f;
```

### First Person Mode

See through the player's eyes:

```csharp
spectator.CameraMode = SpectatorCameraMode.FirstPerson;
```

## Player List

Get available spectate targets:

```csharp
// Get all players that can be spectated
var players = spectator.GetSpectatablePlayers();

foreach (var (puid, name, networkObject) in players)
{
    Debug.Log($"{name} ({puid})");
}
```

## UI Integration

The spectator UI shows:
- Current target name
- Player list dropdown
- Camera mode toggle
- Playback controls (when viewing replay)

## Events

```csharp
spectator.OnTargetChanged += (newTarget, name) =>
{
    Debug.Log($"Now watching: {name}");
};

spectator.OnModeEntered += () =>
{
    // Show spectator UI
};

spectator.OnModeExited += () =>
{
    // Hide spectator UI
};

spectator.OnCameraModeChanged += (mode) =>
{
    Debug.Log($"Camera mode: {mode}");
};
```

## Integration with Replays

Spectator mode works with the replay system:

```csharp
var viewer = EOSReplayViewer.Instance;

// Start viewing a replay (uses spectator camera)
viewer.StartViewing(replay);

// Spectator controls work during replay
spectator.CycleTarget(1);
spectator.ToggleFreeCamera();
```

## Checking State

```csharp
if (spectator.IsSpectating)
{
    Debug.Log($"Watching: {spectator.GetCurrentTargetName()}");
}

if (spectator.IsInFreeCamera)
{
    Debug.Log("Free camera active");
}
```

## Configuration

```csharp
// Auto-spectate when joining as spectator
spectator.AutoSelectTarget = true;

// Exclude dead players from target list
spectator.ExcludeDeadPlayers = true;

// Show spectator count to players
spectator.BroadcastSpectatorCount = true;
```
