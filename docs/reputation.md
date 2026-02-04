# Reputation & Karma

Track player behavior with commendations and reports.

## Overview

The reputation system provides:
- Reputation score tracking (karma)
- Commendation categories (Teamwork, Skill, etc.)
- Report categories (Toxicity, AFK, etc.)
- Daily feedback limits
- Reputation levels with badges
- Feedback history

## Current Reputation

```csharp
var rep = EOSReputationManager.Instance;

// Score and level
int score = rep.CurrentReputation;           // e.g., 250
ReputationLevel level = rep.CurrentLevel;    // e.g., Excellent

// Display
string display = rep.GetFormattedReputation();
// "[+] Excellent (250)"

// Level name and icon
string name = EOSReputationManager.GetLevelName(level);  // "Excellent"
string icon = EOSReputationManager.GetLevelIcon(level);  // "+"
```

## Reputation Levels

| Level | Score | Icon | Description |
|-------|-------|------|-------------|
| Exemplary | 500+ | ++ | Model community member |
| Excellent | 300+ | + | Consistently positive |
| Good | 150+ | o | Above average |
| Neutral | 50+ | ~ | Standard standing |
| Caution | 0+ | - | Some negative feedback |
| Poor | -50+ | -- | Many negative reports |
| Restricted | < -50 | X | Under restriction |

## Commending Players

```csharp
// Commend a player
await rep.CommendPlayerAsync(
    targetPuid,
    category: "Teamwork",
    comment: "Great callouts!"  // optional
);

// Check if can commend
if (rep.CanGiveFeedback(targetPuid, isCommend: true))
{
    // Show commend button
}

// Daily commend limit
int remaining = rep.GetRemainingDailyCommends();
Debug.Log($"Commends remaining today: {remaining}");
```

### Commend Categories

Default categories:
- **Teamwork** - Cooperative play
- **Skill** - Impressive plays
- **Communication** - Good callouts
- **Sportsmanship** - Fair play
- **Leadership** - Shot calling

```csharp
// Available categories
foreach (var category in rep.CommendCategories)
{
    Debug.Log(category);
}
```

## Reporting Players

```csharp
// Report a player
await rep.ReportPlayerAsync(
    targetPuid,
    category: "Toxicity",
    comment: "Verbal abuse in voice chat"
);

// Check daily limit
int remaining = rep.GetRemainingDailyReports();
```

### Report Categories

Default categories:
- **Toxicity** - Verbal abuse
- **AFK** - Away from keyboard
- **Griefing** - Intentional sabotage
- **Cheating** - Suspected hacking
- **Harassment** - Targeted abuse

## Receiving Feedback

```csharp
// Listen for commendations
rep.OnCommendationReceived += (feedback) =>
{
    Debug.Log($"Commended by {feedback.FromName} for {feedback.Category}");
};

// Listen for reports
rep.OnReportReceived += (feedback) =>
{
    Debug.Log($"Reported for {feedback.Category}");
};

// Listen for score changes
rep.OnReputationChanged += (oldScore, newScore) =>
{
    int change = newScore - oldScore;
    Debug.Log($"Reputation: {oldScore} -> {newScore} ({change:+0;-0})");
};

// Listen for level changes
rep.OnLevelChanged += (newLevel) =>
{
    Debug.Log($"New reputation level: {EOSReputationManager.GetLevelName(newLevel)}");
};
```

## Feedback History

```csharp
// View recent feedback
foreach (var feedback in rep.RecentFeedback)
{
    string type = feedback.IsPositive ? "Commend" : "Report";
    Debug.Log($"[{type}] {feedback.Category} from {feedback.FromName}");
}

// Category stats
int teamworkCommends = rep.GetCategoryCommends("Teamwork");

// Top categories
var topCategories = rep.GetTopCategories(3);
foreach (var (category, count) in topCategories)
{
    Debug.Log($"{category}: {count} commends");
}
```

## Level Progress

```csharp
// Progress to next level
var (current, needed, progress) = rep.GetLevelProgress();
Debug.Log($"Progress: {current}/{needed} ({progress:P0})");

// Current stats
Debug.Log($"Total commends: {rep.PlayerData.TotalCommends}");
Debug.Log($"Total reports: {rep.PlayerData.TotalReports}");
Debug.Log($"Commend ratio: {rep.PlayerData.CommendRatio:P0}");
```

## Events

```csharp
// All events
rep.OnReputationChanged += (old, newScore) => { };
rep.OnCommendationReceived += (feedback) => { };
rep.OnReportReceived += (feedback) => { };
rep.OnLevelChanged += (level) => { };
rep.OnDataLoaded += (data) => { };
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Commend Points | 10 | Points gained per commendation |
| Report Penalty | 5 | Points lost per report |
| Min Reputation | -100 | Lowest possible score |
| Max Reputation | 1000 | Highest possible score |
| Default Reputation | 100 | Starting score |
| Feedback Cooldown Hours | 24 | Hours before can feedback same player |
| Max Commends Per Day | 10 | Daily commend limit |
| Max Reports Per Day | 5 | Daily report limit |

### Limits

| Limit | Value |
|-------|-------|
| Recent feedback history | 50 entries |
| Daily commends | 10 |
| Daily reports | 5 |
| Same-player cooldown | 24 hours |

## UI Example

```csharp
void DrawReputationUI()
{
    var rep = EOSReputationManager.Instance;

    // Current status
    GUILayout.Label($"Reputation: {rep.GetFormattedReputation()}");

    // Progress bar
    var (current, needed, progress) = rep.GetLevelProgress();
    GUILayout.Label($"Progress: {progress:P0}");

    // Stats
    GUILayout.Label($"Commends: {rep.PlayerData.TotalCommends}");
    GUILayout.Label($"Reports: {rep.PlayerData.TotalReports}");

    // Top categories
    GUILayout.Label("--- Top Categories ---");
    foreach (var (category, count) in rep.GetTopCategories(3))
    {
        GUILayout.Label($"{category}: {count}");
    }
}

void DrawCommendUI(string targetPuid)
{
    var rep = EOSReputationManager.Instance;

    if (!rep.CanGiveFeedback(targetPuid, true))
    {
        GUILayout.Label("Cannot commend (cooldown or limit)");
        return;
    }

    GUILayout.Label("Commend for:");
    foreach (var category in rep.CommendCategories)
    {
        if (GUILayout.Button(category))
        {
            _ = rep.CommendPlayerAsync(targetPuid, category);
        }
    }
}
```

## Use Cases

### Post-Match Feedback
```csharp
// Show commend UI after match ends
void OnMatchEnd()
{
    foreach (var teammate in teammates)
    {
        if (rep.CanGiveFeedback(teammate.Puid, true))
        {
            ShowCommendPrompt(teammate);
        }
    }
}
```

### Matchmaking Filters
```csharp
// Filter by reputation in matchmaking
if (player.ReputationLevel >= ReputationLevel.Good)
{
    // Allow into high-reputation queue
}
```

### Badge Display
```csharp
// Show reputation badge next to name
string badge = EOSReputationManager.GetLevelIcon(player.ReputationLevel);
string display = $"[{badge}] {player.Name}";
```

## Data Persistence

Reputation data is stored in EOS Cloud Storage (`reputation_data.json`):
- Current score
- Total commends/reports
- Category breakdown
- Recent feedback history

Data syncs across devices automatically.
