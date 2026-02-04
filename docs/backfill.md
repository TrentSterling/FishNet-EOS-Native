# Backfill & Join-in-Progress

Handle players joining active games and fill empty slots.

## Overview

The backfill system provides:
- Join-in-progress (JIP) controls
- Game phase management
- Backfill requests for empty slots
- Team balancing for new joins
- JIP spawn point handling
- Game locking

## Game Phases

```csharp
public enum GamePhase
{
    Lobby,       // Pre-game lobby
    Loading,     // Loading screen
    Warmup,      // Warmup period
    InProgress,  // Active gameplay
    Overtime,    // Overtime
    PostGame,    // Match ended
    Custom       // Custom phase
}
```

```csharp
var backfill = EOSBackfillManager.Instance;

// Set current phase
backfill.SetPhase(GamePhase.Lobby);
backfill.SetPhase(GamePhase.InProgress);

// Check phase
if (backfill.CurrentPhase == GamePhase.InProgress)
{
    // Game is active
}

// Phase change event
backfill.OnPhaseChanged += (oldPhase, newPhase) =>
{
    Debug.Log($"Phase: {oldPhase} -> {newPhase}");
};
```

## Join-in-Progress

### Configuration

```csharp
// Enable/disable JIP
backfill.AllowJoinInProgress = true;

// Time limits
backfill.JipTimeout = 300f;          // 5 min after game start
backfill.JipMinTimeRemaining = 60f;  // Need 1 min left

// Allowed phases for JIP
backfill.SetAllowedPhases(
    GamePhase.Lobby,
    GamePhase.Warmup,
    GamePhase.InProgress
);

// Or modify individually
backfill.AddAllowedPhase(GamePhase.Overtime);
backfill.RemoveAllowedPhase(GamePhase.PostGame);
```

### Checking JIP Eligibility

```csharp
// Check if joining is allowed
var result = backfill.CanJoinInProgress();

switch (result)
{
    case JoinInProgressResult.Allowed:
        Debug.Log("Can join!");
        break;
    case JoinInProgressResult.Denied_Phase:
        Debug.Log("Wrong game phase");
        break;
    case JoinInProgressResult.Denied_Full:
        Debug.Log("Game is full");
        break;
    case JoinInProgressResult.Denied_Timeout:
        Debug.Log("Too late to join");
        break;
    case JoinInProgressResult.Denied_Locked:
        Debug.Log("Game is locked");
        break;
}

// Check with specific player
var result = backfill.CanJoinInProgress(playerPuid);
```

### Processing JIP (Server)

```csharp
// When a player connects during active game
void OnPlayerConnected(string puid, string name)
{
    var jipData = backfill.ProcessJoinInProgress(puid, name);

    if (jipData != null)
    {
        // Spawn at JIP position with assigned team
        SpawnPlayer(puid, jipData.SpawnPosition, jipData.AssignedTeam);

        // Apply spawn protection
        StartCoroutine(SpawnProtection(puid, backfill.SpawnProtectionTime));
    }
}

// Events
backfill.OnPlayerJoinedInProgress += (data) =>
{
    Debug.Log($"{data.PlayerName} joined during {data.JoinedDuringPhase}");
    Debug.Log($"Assigned to team {data.AssignedTeam}");
};

backfill.OnJoinDenied += (puid, result) =>
{
    Debug.Log($"Join denied: {EOSBackfillManager.GetJipResultMessage(result)}");
};
```

## Game Locking

```csharp
// Lock game (no new joins)
backfill.LockGame();

// Unlock game
backfill.UnlockGame();

// Auto-lock on game start
backfill.LockAfterStart = true;

// Check status
if (backfill.IsGameLocked)
{
    Debug.Log("Game is locked");
}

// Events
backfill.OnGameLocked += () => Debug.Log("Game locked");
backfill.OnGameUnlocked += () => Debug.Log("Game unlocked");
```

## Backfill Requests

Request players to fill empty slots:

```csharp
// Request specific number of slots
var request = backfill.RequestBackfill(
    slots: 2,
    preferredTeam: 1,       // Fill team 1 (-1 for any)
    gameMode: "deathmatch",
    region: "us-east"
);

// Request all available slots
var request = backfill.RequestBackfillForAllSlots();

// Check status
if (backfill.IsBackfillActive)
{
    var req = backfill.ActiveBackfill;
    Debug.Log($"Backfill: {req.SlotsFilled}/{req.SlotsNeeded} slots");
    Debug.Log($"Time remaining: {req.TimeRemaining:F0}s");
}

// Cancel backfill
backfill.CancelBackfill();
```

### Auto-Backfill

```csharp
// Enable auto-backfill when players leave
backfill.AutoBackfill = true;
backfill.BackfillDelay = 5f;        // Wait 5s before requesting
backfill.BackfillTimeout = 60f;     // Give up after 60s
backfill.MinPlayersForBackfill = 2; // Need at least 2 players

// Notify when a player leaves
backfill.OnPlayerLeft(puid, team: 1);
```

### Backfill Events

```csharp
backfill.OnBackfillStarted += (request) =>
{
    Debug.Log($"Looking for {request.SlotsNeeded} players");
};

backfill.OnBackfillPlayerJoined += (request, puid) =>
{
    Debug.Log($"Backfill player joined: {request.SlotsFilled}/{request.SlotsNeeded}");
};

backfill.OnBackfillComplete += (request) =>
{
    Debug.Log("Backfill complete!");
};

backfill.OnBackfillFailed += (request) =>
{
    Debug.Log("Backfill timed out");
};
```

## Team Balancing

```csharp
// Enable team balancing
backfill.BalanceTeams = true;

// Set team counts
backfill.SetTeamCounts(new Dictionary<int, int>
{
    { 0, 4 },  // Team 0 has 4 players
    { 1, 3 }   // Team 1 has 3 players
});

// Update single team
backfill.SetTeamCount(1, 4);

// Get best team for new player
int team = backfill.GetTeamForNewPlayer();
// Returns 1 (team with fewer players)
```

## Spawn Points

```csharp
// Set JIP-specific spawn points
backfill.SetJipSpawnPoints(jipSpawnTransforms);
backfill.UseSpecialJipSpawns = true;

// Get spawn position for team
Vector3 spawnPos = backfill.GetJipSpawnPosition(team: 1);

// Spawn protection time
backfill.SpawnProtectionTime = 3f;  // 3 seconds invuln
```

## Player Tracking

```csharp
// Set player counts
backfill.SetMaxPlayers(16);
backfill.SetCurrentPlayers(12);

// Check availability
int available = backfill.AvailableSlots;    // 4
int current = backfill.CurrentPlayers;       // 12
int max = backfill.MaxPlayers;               // 16

// Time tracking
float elapsed = backfill.TimeSinceGameStart;
backfill.SetEstimatedTimeRemaining(180f);   // 3 minutes left
```

## Full Example

```csharp
public class GameManager : MonoBehaviour
{
    private EOSBackfillManager _backfill;

    void Start()
    {
        _backfill = EOSBackfillManager.Instance;

        // Configure
        _backfill.AllowJoinInProgress = true;
        _backfill.JipTimeout = 600f;  // 10 minutes
        _backfill.AutoBackfill = true;
        _backfill.BalanceTeams = true;

        // Events
        _backfill.OnPlayerJoinedInProgress += OnJip;
        _backfill.OnBackfillComplete += OnBackfillDone;
    }

    public void StartGame()
    {
        _backfill.SetPhase(GamePhase.Warmup);

        // After warmup
        _backfill.SetPhase(GamePhase.InProgress);
    }

    public void OnPlayerDisconnected(string puid, int team)
    {
        _backfill.OnPlayerLeft(puid, team);
        // Auto-backfill will trigger if enabled
    }

    private void OnJip(JoinInProgressData data)
    {
        // Spawn the late joiner
        var player = SpawnPlayer(data.AssignedTeam);
        player.transform.position = data.SpawnPosition;

        // Show catch-up info
        SendGameState(data.PlayerPuid);

        // Notify others
        ChatManager.BroadcastSystem($"{data.PlayerName} joined the game");
    }

    private void OnBackfillDone(BackfillRequest request)
    {
        Debug.Log($"Filled {request.SlotsFilled} slots");
    }

    public void EndGame()
    {
        _backfill.SetPhase(GamePhase.PostGame);
        _backfill.LockGame();
        _backfill.CancelBackfill();
    }

    public void ResetForNewGame()
    {
        _backfill.ResetForNewGame();
    }
}
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Allow Join In Progress | true | Enable JIP |
| JIP Timeout | 300s | Max time after start |
| JIP Min Time Remaining | 60s | Min time left needed |
| Lock After Start | false | Lock on game start |
| Auto Backfill | false | Auto-request on leave |
| Backfill Delay | 5s | Wait before requesting |
| Backfill Timeout | 60s | Time to find players |
| Min Players For Backfill | 2 | Min to trigger backfill |
| Balance Teams | true | Fill smaller team |
| Spawn Protection Time | 3s | JIP invuln time |

### Allowed Phases (Default)

- Lobby
- Warmup
- InProgress

## JIP Data Structure

```csharp
public class JoinInProgressData
{
    public string PlayerPuid;
    public string PlayerName;
    public float JoinedTime;
    public int AssignedTeam;
    public Vector3 SpawnPosition;
    public bool IsBackfill;
    public string BackfillRequestId;
    public GamePhase JoinedDuringPhase;
}
```

## Backfill Request Structure

```csharp
public class BackfillRequest
{
    public string RequestId;
    public int SlotsNeeded;
    public int SlotsFilled;
    public float RequestedTime;
    public float TimeoutSeconds;
    public int PreferredTeam;
    public string GameMode;
    public string Region;
    public BackfillStatus Status;

    public bool IsExpired { get; }
    public float TimeRemaining { get; }
    public bool IsComplete { get; }
}
```

## Best Practices

1. **Set phase correctly** - Update phase as game progresses
2. **Track player counts** - Call `SetCurrentPlayers` and `OnPlayerLeft`
3. **Handle JIP spawning** - Use dedicated spawn points
4. **Give spawn protection** - JIP players need time to orient
5. **Send game state** - Catch up late joiners on score, time, etc.
6. **Balance teams** - Use team balancing for fair matches

## Lobby Integration

The system automatically updates lobby attributes:

| Attribute | Values | Description |
|-----------|--------|-------------|
| JIP_ALLOWED | "0"/"1" | Whether JIP is allowed |
| BACKFILL_NEEDED | "0"/"1" | Actively seeking players |
| BACKFILL_SLOTS | number | Slots to fill |

These can be used in lobby searches to find games needing players.
