# Replay Highlights

Auto-detect and bookmark significant moments in replays.

## Overview

The highlight system provides:
- Automatic detection of multi-kills, clutches, comebacks
- Manual highlight creation
- Importance levels for filtering
- Integration with replay playback
- Custom highlight types

## Automatic Detection

```csharp
var highlights = EOSReplayHighlights.Instance;

// Report kills (auto-detects multi-kills)
highlights.ReportKill(killerPuid, victimPuid);
highlights.ReportKill(killerPuid, victimPuid, isHeadshot: true);

// Report clutch situations
highlights.ReportClutch(
    healthPercent: 0.1f,     // 10% health
    aliveTeammates: 0,        // Solo
    aliveEnemies: 3           // vs 3 enemies
);

// Report score changes (auto-detects comebacks)
highlights.ReportScoreUpdate(teamScore: 5, enemyScore: 4);

// Report objectives
highlights.ReportObjective("bomb_planted");
highlights.ReportObjective("flag_captured");
```

## Multi-Kill Detection

The system automatically tracks kills within a time window:

| Kill Count | Highlight |
|------------|-----------|
| 2 kills | Double Kill (Medium) |
| 3 kills | Triple Kill (High) |
| 4 kills | Quad Kill (High) |
| 5+ kills | Multi Kill (Epic) |

```csharp
// Configure multi-kill window (default: 4 seconds)
highlights.MultiKillWindow = 4f;

// Kills within window are combined
highlights.ReportKill(puid, victim1);  // t=0
highlights.ReportKill(puid, victim2);  // t=1.5 -> Double Kill!
highlights.ReportKill(puid, victim3);  // t=3.0 -> Triple Kill!
```

## Clutch Detection

Low health victories against multiple enemies:

```csharp
// Report when winning a fight
highlights.ReportClutch(
    healthPercent: 0.05f,    // 5% health remaining
    aliveTeammates: 0,        // No teammates alive
    aliveEnemies: 2           // Beat 2 enemies
);

// Thresholds (configurable)
highlights.ClutchHealthThreshold = 0.25f;  // 25% health
highlights.ClutchEnemyMinimum = 2;          // At least 2v1
```

| Situation | Importance |
|-----------|------------|
| 1v2 clutch | Medium |
| 1v3+ clutch | High |
| Low health 1v3+ | Epic |

## Comeback Detection

Score deficit recoveries:

```csharp
// Report each score change
highlights.ReportScoreUpdate(teamScore: 2, enemyScore: 5);  // Down 3
// ... game continues ...
highlights.ReportScoreUpdate(teamScore: 5, enemyScore: 5);  // Tied!
// Comeback highlight created automatically

// Configure threshold
highlights.ComebackThreshold = 3;  // Points behind to trigger
```

## Manual Highlights

```csharp
// Add highlight at current time
var highlight = highlights.AddHighlight("Amazing play!");

// Add with type
var highlight = highlights.AddHighlight(
    "Sick snipe",
    HighlightType.Custom
);

// Add at specific time
var highlight = highlights.AddHighlightAtTime(
    "Opening kill",
    time: 15.5f,
    HighlightType.Manual,
    HighlightImportance.Medium
);

// Add with custom data
var highlight = highlights.AddHighlight("Achievement unlocked");
highlight.CustomData["achievement_id"] = "first_blood";
```

## Highlight Types

```csharp
public enum HighlightType
{
    Manual,      // User-created
    MultiKill,   // Double, triple, etc.
    Clutch,      // Low health victory
    Headshot,    // Precision kill
    Comeback,    // Score recovery
    Objective,   // Flag cap, bomb plant
    Victory,     // Match win
    Custom       // Game-specific
}
```

## Importance Levels

```csharp
public enum HighlightImportance
{
    Low,     // Minor moments
    Medium,  // Notable plays
    High,    // Great plays
    Epic     // Best of the best
}
```

## Querying Highlights

```csharp
// Get all highlights
var all = highlights.Highlights;

// Get by time range
var midGame = highlights.GetHighlightsByTime(60f, 180f);

// Get top highlights
var best = highlights.GetTopHighlights(5);  // Top 5 by importance

// Find nearest to time
var nearest = highlights.FindNearestHighlight(currentTime);

// Filter by type
var clutches = highlights.Highlights
    .Where(h => h.Type == HighlightType.Clutch);

// Filter by importance
var epicMoments = highlights.Highlights
    .Where(h => h.Importance == HighlightImportance.Epic);
```

## Replay Integration

Highlights integrate with the replay viewer:

```csharp
var viewer = EOSReplayViewer.Instance;

// Jump to highlight
viewer.SeekToHighlight(highlight);

// Get highlights for current replay
var replayHighlights = viewer.GetHighlights();

// Navigate highlights
viewer.NextHighlight();
viewer.PreviousHighlight();
```

## Events

```csharp
// Listen for new highlights
highlights.OnHighlightCreated += (highlight) =>
{
    Debug.Log($"New highlight: {highlight.Title} ({highlight.Importance})");

    // Show notification
    EOSToastManager.Success($"{highlight.Title}!", highlight.Type.ToString());
};

// Listen for specific types
highlights.OnHighlightCreated += (h) =>
{
    if (h.Type == HighlightType.MultiKill)
    {
        PlayMultiKillSound(h.CustomData["killCount"]);
    }
};
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Multi Kill Window | 4s | Time window for multi-kills |
| Clutch Health Threshold | 0.25 | Max health % for clutch |
| Clutch Enemy Minimum | 2 | Min enemies for clutch |
| Comeback Threshold | 3 | Points behind for comeback |
| Auto Detect Enabled | true | Enable auto-detection |

### Runtime Configuration

```csharp
var highlights = EOSReplayHighlights.Instance;

// Adjust thresholds
highlights.MultiKillWindow = 5f;
highlights.ClutchHealthThreshold = 0.3f;
highlights.ClutchEnemyMinimum = 2;
highlights.ComebackThreshold = 4;

// Toggle auto-detection
highlights.AutoDetectEnabled = true;
```

## Custom Detection

Add game-specific highlight detection:

```csharp
// In your game code
void OnAceAchieved(string playerPuid)
{
    var h = EOSReplayHighlights.Instance.AddHighlight(
        "ACE!",
        HighlightType.Custom,
        HighlightImportance.Epic
    );
    h.CustomData["player"] = playerPuid;
    h.CustomData["type"] = "ace";
}

void OnDefuse(float timeRemaining)
{
    var importance = timeRemaining < 1f
        ? HighlightImportance.Epic
        : HighlightImportance.High;

    var h = EOSReplayHighlights.Instance.AddHighlight(
        $"Defuse with {timeRemaining:F1}s left!",
        HighlightType.Objective,
        importance
    );
}
```

## UI Example

```csharp
void DrawHighlightTimeline(Replay replay)
{
    var highlights = EOSReplayHighlights.Instance;
    float duration = replay.Duration;

    // Draw timeline bar
    Rect timelineRect = GUILayoutUtility.GetRect(400, 30);
    GUI.Box(timelineRect, "");

    // Draw highlight markers
    foreach (var h in highlights.Highlights)
    {
        float x = (h.Timestamp / duration) * timelineRect.width;
        Color color = GetImportanceColor(h.Importance);

        // Draw marker
        GUI.color = color;
        if (GUI.Button(new Rect(timelineRect.x + x - 5, timelineRect.y, 10, 30), ""))
        {
            EOSReplayViewer.Instance.Seek(h.Timestamp);
        }
    }
    GUI.color = Color.white;

    // List highlights
    GUILayout.Label("--- Highlights ---");
    foreach (var h in highlights.GetTopHighlights(10))
    {
        string time = FormatTime(h.Timestamp);
        string icon = GetImportanceIcon(h.Importance);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"{icon} [{time}] {h.Title}");
        if (GUILayout.Button("Jump", GUILayout.Width(50)))
        {
            EOSReplayViewer.Instance.Seek(h.Timestamp);
        }
        GUILayout.EndHorizontal();
    }
}

Color GetImportanceColor(HighlightImportance importance) => importance switch
{
    HighlightImportance.Epic => Color.yellow,
    HighlightImportance.High => Color.red,
    HighlightImportance.Medium => Color.cyan,
    _ => Color.white
};

string GetImportanceIcon(HighlightImportance importance) => importance switch
{
    HighlightImportance.Epic => "★★★",
    HighlightImportance.High => "★★",
    HighlightImportance.Medium => "★",
    _ => "·"
};
```

## Best Practices

1. **Report events immediately** - Call report methods as events happen
2. **Use appropriate importance** - Reserve Epic for truly special moments
3. **Add context** - Use CustomData for filtering/display
4. **Integrate with UI** - Show highlight notifications in-game
5. **Allow manual bookmarks** - Let players mark their own highlights

## Data Structure

```csharp
public class ReplayHighlight
{
    public string Id { get; }
    public string Title { get; set; }
    public float Timestamp { get; }
    public HighlightType Type { get; }
    public HighlightImportance Importance { get; set; }
    public Dictionary<string, string> CustomData { get; }
}
```

Highlights are stored with the replay and persist through save/load.
