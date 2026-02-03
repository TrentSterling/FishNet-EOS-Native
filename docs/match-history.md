# Match History

Track games played with participants and outcomes.

## Recording Matches

### Start a Match

```csharp
var history = EOSMatchHistory.Instance;

// Start tracking when game begins
history.StartMatch("deathmatch", "dust2");
// Parameters: gameMode, mapName (optional)
```

### Add Participants

```csharp
// Add players as they join
history.AddParticipant(puid, "PlayerName", team: 1);

// Local player is added automatically
```

### Update Scores

```csharp
// Update your score
history.UpdateLocalScore(score: 15, team: 1);

// Update another player's score
history.UpdateParticipantScore(puid, score: 12);
```

### End Match

```csharp
// End with outcome
history.EndMatch(MatchOutcome.Win, winnerPuid: localPuid);

// Outcomes: Win, Loss, Draw, Abandoned
```

## Querying History

```csharp
// Get recent matches
var recent = history.GetRecentMatches(10);

foreach (var match in recent)
{
    Debug.Log($"{match.GameMode} on {match.Map}");
    Debug.Log($"Outcome: {match.Outcome}");
    Debug.Log($"Duration: {match.Duration}");
    Debug.Log($"Players: {match.Participants.Count}");
}
```

## Match Data

```csharp
public class MatchRecord
{
    public string MatchId { get; }
    public string GameMode { get; }
    public string Map { get; }
    public DateTime StartTime { get; }
    public DateTime EndTime { get; }
    public TimeSpan Duration { get; }
    public MatchOutcome Outcome { get; }
    public string WinnerPuid { get; }
    public List<MatchParticipant> Participants { get; }
}

public class MatchParticipant
{
    public string Puid { get; }
    public string Name { get; }
    public int Team { get; }
    public int Score { get; }
}
```

## Local Stats

```csharp
// Get overall stats
var (wins, losses, draws, total) = history.GetLocalStats();

Debug.Log($"Record: {wins}W - {losses}L - {draws}D");
Debug.Log($"Win rate: {(float)wins / total:P0}");
```

## Filtering

```csharp
// By game mode
var rankedMatches = history.GetMatchesByMode("ranked");

// By map
var dust2Matches = history.GetMatchesByMap("dust2");

// By date range
var thisWeek = history.GetMatchesInRange(
    DateTime.Now.AddDays(-7),
    DateTime.Now
);

// By outcome
var wins = history.GetRecentMatches(100)
    .Where(m => m.Outcome == MatchOutcome.Win);
```

## Recently Played With

```csharp
// Get players you've played with recently
var recentPlayers = history.GetRecentlyPlayedWith(20);

foreach (var (puid, name, lastPlayed) in recentPlayers)
{
    Debug.Log($"{name} - last played {lastPlayed}");
}
```

## Events

```csharp
history.OnMatchStarted += (matchId) => { };
history.OnMatchEnded += (record) => { };
history.OnParticipantAdded += (puid, name) => { };
```

## Integration

### With Ranked

Ranked matches automatically integrate:

```csharp
var ranked = EOSRankedMatchmaking.Instance;

// When recording ranked result, match is auto-logged
await ranked.RecordMatchResultAsync(MatchOutcome.Win, 1450);
// This also calls history.EndMatch() internally
```

### With Replays

Match history entries link to replays:

```csharp
var match = history.GetRecentMatches(1).First();

if (match.HasReplay)
{
    var replay = await EOSReplayStorage.Instance
        .LoadLocalAsync(match.ReplayId);
    EOSReplayViewer.Instance.StartViewing(replay);
}
```

## UI Integration

The F1 debug panel shows:
- Recent matches list
- W/L/D record
- Expandable match details with participants
- Replay button (if available)

## Cloud Sync

Match history syncs to cloud automatically:

```csharp
// Manual sync (usually not needed)
await history.SyncToCloudAsync();
```

Storage limits:
- Local: Last 100 matches
- Cloud: Last 50 matches
