# Leaderboards & Stats

EOS Stats and Leaderboards integration.

## Stats

Track player statistics that persist to EOS.

### Ingesting Stats

```csharp
var stats = EOSStats.Instance;

// Increment a stat
await stats.IngestStatAsync("kills", 5);
await stats.IngestStatAsync("deaths", 2);
await stats.IngestStatAsync("games_played", 1);

// Set a stat to specific value (for high scores)
await stats.SetStatAsync("high_score", 15000);
```

### Reading Stats

```csharp
// Get your own stats
var myStats = await stats.GetStatsAsync();
int kills = myStats.GetValueOrDefault("kills", 0);

// Get another player's stats
var playerStats = await stats.GetStatsAsync(puid);
```

### Stat Definitions

Stats must be defined in the EOS Developer Portal:
1. Go to your product
2. Navigate to Game Services > Stats
3. Create stat definitions

Common stat types:
- **SUM** - Accumulates values (kills, deaths)
- **MAX** - Keeps highest value (high score)
- **MIN** - Keeps lowest value (best time)
- **LATEST** - Most recent value

## Leaderboards

### Querying Leaderboards

```csharp
var leaderboards = EOSLeaderboards.Instance;

// Get top 10
var top = await leaderboards.GetLeaderboardAsync("high_score", 10);

foreach (var entry in top)
{
    Debug.Log($"#{entry.Rank} {entry.PlayerName}: {entry.Score}");
}
```

### Leaderboard Entry

```csharp
public class LeaderboardEntry
{
    public int Rank { get; }
    public string Puid { get; }
    public string PlayerName { get; }
    public int Score { get; }
}
```

### Get Your Rank

```csharp
// Get your position on a leaderboard
var myEntry = await leaderboards.GetPlayerRankAsync("high_score");

if (myEntry != null)
{
    Debug.Log($"Your rank: #{myEntry.Rank}");
}
```

### Get Friends' Scores

```csharp
// Get just friends' entries
var friendsBoard = await leaderboards.GetFriendsLeaderboardAsync("high_score");
```

### Get Entries Around You

```csharp
// Get entries around your rank (±5)
var nearby = await leaderboards.GetLeaderboardAroundPlayerAsync("high_score", 5);
```

## Achievements

Track and unlock achievements.

### Unlocking

```csharp
var achievements = EOSAchievements.Instance;

// Unlock an achievement
await achievements.UnlockAchievementAsync("first_win");

// Progress-based achievement
await achievements.UpdateProgressAsync("win_100_games", 0.45f); // 45%
```

### Querying

```csharp
// Get all achievements with unlock status
var all = await achievements.GetAchievementsAsync();

foreach (var achievement in all)
{
    Debug.Log($"{achievement.Name}: {(achievement.IsUnlocked ? "✓" : "✗")}");
    if (!achievement.IsUnlocked && achievement.Progress > 0)
    {
        Debug.Log($"  Progress: {achievement.Progress:P0}");
    }
}
```

### Achievement Data

```csharp
public class AchievementData
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsUnlocked { get; }
    public float Progress { get; }       // 0.0 - 1.0
    public DateTime? UnlockTime { get; }
}
```

## Portal Setup

### Creating a Stat

1. EOS Developer Portal > Game Services > Stats
2. Click "Create Stat"
3. Configure:
   - Name: `kills`
   - Aggregation: `SUM`

### Creating a Leaderboard

1. EOS Developer Portal > Game Services > Leaderboards
2. Click "Create Leaderboard"
3. Configure:
   - Name: `high_score`
   - Stat: Select your stat
   - Sort: Descending (highest first)

### Creating an Achievement

1. EOS Developer Portal > Game Services > Achievements
2. Click "Create Achievement"
3. Configure:
   - ID: `first_win`
   - Display Name: "First Victory"
   - Description: "Win your first match"
   - Optional: Add icon

## Events

```csharp
stats.OnStatUpdated += (statName, newValue) => { };
leaderboards.OnRankChanged += (leaderboardName, oldRank, newRank) => { };
achievements.OnAchievementUnlocked += (achievementId) => { };
```

## Rate Limits

| Operation | Limit |
|-----------|-------|
| Stat ingestion | 100/min per user |
| Leaderboard queries | 100/min |
| Achievement updates | 100/min |
