# Achievements

Track player accomplishments with EOS achievements.

## Overview

The achievements system provides:
- EOS Developer Portal integration
- Progress-based achievements
- Stat-based auto-triggers
- Unlock popups/notifications
- Icon loading
- Offline caching

## Setup

1. Define achievements in [EOS Developer Portal](https://dev.epicgames.com/portal)
2. Add `EOSAchievements` component (auto-created)
3. Achievements load automatically on login

## Basic Usage

```csharp
var achievements = EOSAchievements.Instance;

// Check if ready
if (achievements.IsReady)
{
    // Unlock an achievement
    await achievements.UnlockAchievementAsync("first_blood");

    // Check if unlocked
    if (achievements.IsUnlocked("first_blood"))
    {
        Debug.Log("Already unlocked!");
    }
}
```

## Progress Tracking

For achievements that require progress (e.g., "Get 100 kills"):

```csharp
// Set progress directly (0.0 to 1.0)
await achievements.SetProgressAsync("kill_master", 0.5f);  // 50%

// Increment progress
await achievements.IncrementProgressAsync("kill_master", 0.01f);  // +1%

// Set progress with count (more intuitive)
int kills = 50;
int required = 100;
await achievements.SetProgressCountAsync("kill_master", kills, required);
// Automatically calculates 50/100 = 0.5

// Auto-unlocks when progress reaches 1.0
```

## Stat-Based Triggers

Link achievements to stats for automatic progress/unlock:

```csharp
// Register trigger: unlock "kill_master" when "total_kills" reaches 100
achievements.RegisterStatTrigger(
    achievementId: "kill_master",
    statName: "total_kills",
    targetValue: 100,
    isProgressive: true  // Update progress as stat increases
);

// Report stats during gameplay
achievements.IncrementStat("total_kills");  // +1
achievements.ReportStat("total_kills", 50);  // Set to 50

// Progress auto-updates: 50/100 = 50%
// Auto-unlocks at 100
```

### Multiple Triggers

```csharp
// Multiple achievements from same stat
achievements.RegisterStatTrigger("novice_killer", "total_kills", 10);
achievements.RegisterStatTrigger("kill_master", "total_kills", 100);
achievements.RegisterStatTrigger("legendary_killer", "total_kills", 1000);

// Each unlocks at its threshold
achievements.ReportStat("total_kills", 10);   // Unlocks novice_killer
achievements.ReportStat("total_kills", 100);  // Unlocks kill_master
```

## Querying Achievements

```csharp
// Get all definitions
foreach (var def in achievements.Definitions)
{
    Debug.Log($"{def.Id}: {def.DisplayName}");
}

// Get player progress
foreach (var data in achievements.PlayerAchievements)
{
    Debug.Log($"{data.Id}: {data.Progress * 100}% - Unlocked: {data.IsUnlocked}");
}

// Get specific achievement
var def = achievements.GetDefinition("first_blood");
var progress = achievements.GetProgress("kill_master");  // 0.0 to 1.0
bool unlocked = achievements.IsUnlocked("first_blood");

// Counts
int total = achievements.TotalAchievements;
int unlocked = achievements.UnlockedCount;
```

## Popups/Notifications

Achievement unlocks automatically show toast notifications:

```csharp
// Enable/disable popups
achievements.ShowPopups = true;
achievements.PopupDuration = 5f;  // seconds
```

Custom popup handling:
```csharp
achievements.OnAchievementUnlocked += (id) =>
{
    var def = achievements.GetDefinition(id);
    ShowCustomPopup(def.Value.DisplayName, def.Value.Description);
};
```

## Icon Loading

Load achievement icons from EOS:

```csharp
// Load icon async
achievements.LoadIcon("first_blood", unlocked: true, (texture) =>
{
    if (texture != null)
    {
        myImage.texture = texture;
    }
});

// Get cached icon (if already loaded)
var icon = achievements.GetCachedIcon("first_blood", unlocked: true);
```

## Offline Caching

Progress is cached locally for offline play:

```csharp
// Enable/disable (enabled by default)
achievements.EnableOfflineCache = true;

// Clear cache
achievements.ClearOfflineCache();
```

When online, cached progress syncs with server (keeping higher value).

## Events

```csharp
// Achievements loaded from server
achievements.OnAchievementsLoaded += () =>
{
    Debug.Log($"Loaded {achievements.TotalAchievements} achievements");
    RefreshUI();
};

// Achievement unlocked
achievements.OnAchievementUnlocked += (achievementId) =>
{
    Debug.Log($"Unlocked: {achievementId}");
    PlayUnlockSound();
};

// Progress changed
achievements.OnProgressChanged += (id, oldProgress, newProgress) =>
{
    Debug.Log($"{id}: {oldProgress * 100}% -> {newProgress * 100}%");
    UpdateProgressBar(id, newProgress);
};
```

## Refresh Data

```csharp
// Refresh all achievement data from server
await achievements.RefreshAsync();

// Query only definitions
await achievements.QueryDefinitionsAsync();

// Query only player progress
await achievements.QueryPlayerAchievementsAsync();
```

## Data Structures

### AchievementDefinition

```csharp
public struct AchievementDefinition
{
    public string Id;
    public string DisplayName;
    public string Description;
    public string LockedDisplayName;
    public string LockedDescription;
    public string UnlockedIconUrl;
    public string LockedIconUrl;
    public bool IsHidden;
}
```

### PlayerAchievementData

```csharp
public struct PlayerAchievementData
{
    public string Id;
    public double Progress;          // 0.0 to 1.0
    public DateTimeOffset? UnlockTime;
    public bool IsUnlocked;
    public DateTime? UnlockDateTime; // Local time
}
```

## UI Example

```csharp
void DrawAchievementsUI()
{
    var achievements = EOSAchievements.Instance;

    GUILayout.Label($"Achievements: {achievements.UnlockedCount}/{achievements.TotalAchievements}");

    foreach (var def in achievements.Definitions)
    {
        var data = achievements.GetPlayerAchievement(def.Id);
        bool unlocked = data?.IsUnlocked ?? false;
        float progress = (float)(data?.Progress ?? 0);

        GUILayout.BeginHorizontal();

        // Status icon
        GUILayout.Label(unlocked ? "[X]" : "[ ]", GUILayout.Width(30));

        // Name
        string name = unlocked ? def.DisplayName : def.LockedDisplayName;
        GUILayout.Label(name, GUILayout.Width(150));

        // Progress bar
        if (!unlocked && progress > 0)
        {
            GUILayout.HorizontalSlider(progress, 0, 1, GUILayout.Width(100));
            GUILayout.Label($"{progress * 100:F0}%", GUILayout.Width(40));
        }

        GUILayout.EndHorizontal();
    }
}
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Show Popups | true | Show unlock notifications |
| Popup Duration | 5s | How long popup displays |
| Enable Offline Cache | true | Cache progress locally |
| Stat Triggers | [] | List of stat-based triggers |

## Best Practices

1. **Use stat triggers** - Cleaner than manual tracking
2. **Set meaningful thresholds** - 10, 50, 100, 500, 1000
3. **Show progress** - Players like seeing advancement
4. **Hidden achievements** - Use for spoilers/surprises
5. **Cache icons** - Load once, reuse

## EOS Developer Portal

To create achievements:

1. Go to [dev.epicgames.com/portal](https://dev.epicgames.com/portal)
2. Select your product
3. Navigate to Game Services > Achievements
4. Click "Create Achievement"
5. Fill in:
   - Achievement ID (e.g., "first_blood")
   - Locked/Unlocked names and descriptions
   - Icons (optional)
   - Hidden flag

## Troubleshooting

**"Not Ready" or no achievements loading:**
- Ensure EOS login is complete
- Check achievements are defined in portal
- Verify product credentials match

**Progress not saving:**
- EOS doesn't store progress server-side (only unlock status)
- Use offline cache for local progress tracking
- Consider using EOS Stats for persistent progress

**Icons not loading:**
- Check URL is accessible
- Ensure CORS allows Unity requests
- Icons are optional in portal
