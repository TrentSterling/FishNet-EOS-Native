# Seasons & Ranked Resets

Organize competitive play into seasons with soft resets and rewards.

## Overview

The season system provides:
- Configurable season durations
- Soft rating resets between seasons
- Season rewards based on peak/final rating
- Historical season records
- Auto season transitions

## Current Season

```csharp
var seasons = EOSSeasonManager.Instance;

// Season info
Season current = seasons.CurrentSeason;
int seasonNumber = current.Number;
string name = current.Name;
DateTime start = current.StartDate;
DateTime end = current.EndDate;

// Time remaining
TimeSpan remaining = seasons.TimeRemaining;
int daysLeft = seasons.DaysRemaining;
float progress = seasons.SeasonProgress;  // 0-1
string timeStr = seasons.GetTimeRemainingString();  // "15d 3h"

// Display name
string display = seasons.GetSeasonDisplayName();  // "Season 5"
```

## Season Status

```csharp
// Check season state
if (seasons.IsSeasonActive)
{
    Debug.Log($"Season {seasons.CurrentSeason.Number} active");
    Debug.Log($"Days remaining: {seasons.DaysRemaining}");
}
```

## Soft Resets

When a season ends, ratings are "soft reset" toward a target:

```csharp
// Configure reset behavior (Inspector or code)
// _softResetPercentage = 0.5  // Keep 50% of difference from target
// _softResetTarget = 1200     // Target rating
// _minimumResetRating = 800   // Floor

// Examples with 50% reset toward 1200:
// 2000 -> 1200 + (2000-1200)*0.5 = 1600
// 1600 -> 1200 + (1600-1200)*0.5 = 1400
// 1000 -> 1200 + (1000-1200)*0.5 = 1100
// 600  -> 800 (minimum floor)

// Preview reset
int newRating = seasons.PreviewResetRating(currentRating);
```

## Season Rewards

Rewards are based on peak or final rating:

```csharp
// Get reward tier for a rating
SeasonRewardTier tier = seasons.GetRewardTier(1850);
// Returns: SeasonRewardTier.Diamond

// Calculate current rewards
SeasonRewards rewards = seasons.CalculateCurrentRewards();
Debug.Log($"Tier: {rewards.RewardTier}");
Debug.Log($"Games: {rewards.GamesPlayed}");

// Check pending rewards
if (seasons.HasPendingRewards)
{
    var pending = seasons.PendingRewards;
    Debug.Log($"Unclaimed: {pending.RewardTier} from Season {pending.SeasonNumber}");

    // Claim rewards
    await seasons.ClaimRewardsAsync();
}

// Reward descriptions
string desc = EOSSeasonManager.GetRewardDescription(SeasonRewardTier.Diamond);
// "Diamond Border, Title, 500 Points"
```

### Reward Tiers

| Tier | Rating | Rewards |
|------|--------|---------|
| Champion | 2200+ | Champion Border, Exclusive Title, 1000 Points |
| Diamond | 1900+ | Diamond Border, Title, 500 Points |
| Platinum | 1600+ | Platinum Border, Title, 300 Points |
| Gold | 1300+ | Gold Border, 200 Points |
| Silver | 1000+ | Silver Border, 100 Points |
| Bronze | 700+ | Bronze Border, 50 Points |

## Season History

```csharp
// View past seasons
var history = seasons.SeasonHistory;

foreach (var record in history)
{
    Debug.Log($"Season {record.SeasonNumber}: {record.SeasonName}");
    Debug.Log($"  Final: {record.FinalRating}, Peak: {record.PeakRating}");
    Debug.Log($"  Record: {record.Wins}W / {record.GamesPlayed - record.Wins}L");
    Debug.Log($"  Reward: {record.RewardTier}");
}

// Get specific season
SeasonRecord s3 = seasons.GetSeasonRecord(3);
```

## Manual Season Control

```csharp
// Start a new season manually
await seasons.StartNewSeasonAsync();

// Or with specific number
await seasons.StartNewSeasonAsync(seasonNumber: 5);
```

## Events

```csharp
// Season lifecycle
seasons.OnSeasonStarted += (season) =>
{
    Debug.Log($"Season {season.Number} has begun!");
};

seasons.OnSeasonEnded += (season, rewards) =>
{
    Debug.Log($"Season {season.Number} ended!");
    Debug.Log($"You earned: {rewards.RewardTier}");
};

// Reset notification
seasons.OnSeasonReset += (oldRating, newRating) =>
{
    Debug.Log($"Rating reset: {oldRating} -> {newRating}");
};

// Rewards
seasons.OnRewardsClaimed += (rewards) =>
{
    Debug.Log($"Claimed {rewards.RewardTier} rewards!");
};

// Time updates (every minute)
seasons.OnTimeRemainingUpdated += (remaining) =>
{
    UpdateSeasonTimer(remaining);
};

// Data loaded
seasons.OnDataLoaded += (data) => { };
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Season Duration Days | 90 | Length of each season |
| Soft Reset Percentage | 0.5 | How much rating to keep (0-1) |
| Soft Reset Target | 1200 | Target rating for reset calculation |
| Minimum Reset Rating | 800 | Floor for post-reset rating |
| Use Peak Rating For Rewards | true | Use peak instead of final |
| Auto Start New Season | true | Auto-start when season ends |
| Auto Check Season End | true | Periodically check for end |
| Season Check Interval | 60s | How often to check |
| Enable Rewards | true | Award season rewards |

### Reset Calculation

The soft reset formula:

```
newRating = target + (oldRating - target) * percentage
```

With default settings (50% reset toward 1200):

| Old Rating | New Rating |
|------------|------------|
| 2400 | 1800 |
| 2000 | 1600 |
| 1600 | 1400 |
| 1200 | 1200 |
| 1000 | 1100 |
| 800 | 1000 |
| 600 | 800 (minimum) |

## Integration with Ranked

The season system integrates with EOSRankedMatchmaking:

```csharp
// Ranked matchmaking respects seasons
var ranked = EOSRankedMatchmaking.Instance;
var seasons = EOSSeasonManager.Instance;

// Season-specific stats
var record = seasons.GetSeasonRecord(seasons.CurrentSeason.Number);

// Rating info includes season context
string display = ranked.GetCurrentRankDisplayName();
```

## UI Example

```csharp
void DrawSeasonInfo()
{
    var seasons = EOSSeasonManager.Instance;

    // Header
    GUILayout.Label($"=== {seasons.GetSeasonDisplayName()} ===");

    // Progress bar
    GUILayout.Label($"Progress: {seasons.SeasonProgress:P0}");
    GUILayout.Label($"Time remaining: {seasons.GetTimeRemainingString()}");

    // Current standing
    var rewards = seasons.CalculateCurrentRewards();
    if (rewards != null)
    {
        GUILayout.Label($"Current Tier: {rewards.RewardTier}");
        GUILayout.Label($"Games: {rewards.GamesPlayed}");
    }

    // Pending rewards
    if (seasons.HasPendingRewards)
    {
        if (GUILayout.Button("Claim Rewards"))
        {
            _ = seasons.ClaimRewardsAsync();
        }
    }
}
```

## Data Persistence

Season data is stored in EOS Cloud Storage (`season_data.json`):
- Current season info
- Player season history
- Pending rewards
- Reset information

Data syncs across devices automatically.
