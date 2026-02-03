# Ranked Matchmaking

Skill-based matchmaking with multiple rating algorithms and tier display.

## Quick Start

```csharp
var ranked = EOSRankedMatchmaking.Instance;

// Find a ranked match (auto-expands search if needed)
var (result, lobby) = await ranked.FindRankedMatchAsync("ranked");

// Or host a ranked lobby at your skill level
var (result, lobby) = await ranked.HostRankedLobbyAsync("ranked");

// Find or host (tries join first, hosts if none found)
var (result, lobby, didHost) = await ranked.FindOrHostRankedMatchAsync("ranked");
```

## Recording Results

```csharp
// After match ends, record the result
var ratingChange = await ranked.RecordMatchResultAsync(
    MatchOutcome.Win,
    opponentRating: 1450
);

Debug.Log($"Rating change: {ratingChange.Change:+0;-0}");
Debug.Log($"New rating: {ratingChange.NewRating}");
```

## Rating Info

```csharp
// Current rating
int rating = ranked.CurrentRating;  // e.g., 1350

// Tier info
RankTier tier = ranked.CurrentTier;           // e.g., Gold
RankDivision division = ranked.CurrentDivision; // e.g., II

// Display string
string display = ranked.GetCurrentRankDisplayName();
// Returns "Gold II" or just "1350" depending on display mode
```

## Player Stats

```csharp
var data = ranked.PlayerData;

int wins = data.Wins;
int losses = data.Losses;
int draws = data.Draws;
float winRate = data.WinRate;      // 0.0 - 1.0
int peak = data.PeakRating;        // Highest rating achieved
int gamesPlayed = data.GamesPlayed;
```

## Placement Matches

New players must complete 10 placement matches:

```csharp
if (!ranked.IsPlaced)
{
    int remaining = 10 - ranked.PlayerData.GamesPlayed;
    Debug.Log($"Placement matches remaining: {remaining}");
}

// After placement
ranked.OnPlacementCompleted += (rating, tier, division) =>
{
    Debug.Log($"Placed at {tier} {division} ({rating})");
};
```

## Rating Algorithms

### ELO (Default)

Standard ELO with configurable K-factor:

```csharp
ranked.SetAlgorithm(RatingAlgorithm.ELO);

// K-factor affects volatility
// Higher K = faster rating changes
// Lower K = more stable ratings
```

### Glicko-2

Adds rating deviation (uncertainty) and volatility:

```csharp
ranked.SetAlgorithm(RatingAlgorithm.Glicko2);

// More accurate for irregular play patterns
// Uncertainty decreases with more games
```

### Simple MMR

Fixed point gains with streak bonuses:

```csharp
ranked.SetAlgorithm(RatingAlgorithm.SimpleMMR);

// Win: +25 points (base)
// Loss: -20 points (base)
// Streak bonus: +5 per consecutive win/loss
```

## Tier Display Modes

### 6-Tier System

```csharp
ranked.SetTierDisplayMode(TierDisplayMode.SixTier);
```

| Tier | Rating |
|------|--------|
| Champion | 2200+ |
| Diamond | 1900+ |
| Platinum | 1600+ |
| Gold | 1300+ |
| Silver | 1000+ |
| Bronze | 0+ |

Each tier has divisions I, II, III (highest to lowest).

### 8-Tier System

```csharp
ranked.SetTierDisplayMode(TierDisplayMode.EightTier);
```

| Tier | Rating |
|------|--------|
| Grandmaster | 2500+ |
| Master | 2200+ |
| Diamond | 1900+ |
| Platinum | 1600+ |
| Gold | 1300+ |
| Silver | 1000+ |
| Bronze | 700+ |
| Iron | 0+ |

### Numbers Only

```csharp
ranked.SetTierDisplayMode(TierDisplayMode.NumbersOnly);
// Just shows "1350" instead of "Gold II"
```

## Events

```csharp
// Rating changed
ranked.OnRatingChanged += (change) =>
{
    Debug.Log($"Rating: {change.OldRating} → {change.NewRating}");
    Debug.Log($"Change: {change.Change:+0;-0}");
};

// Tier changed
ranked.OnPromotion += (tier, division) =>
{
    Debug.Log($"Promoted to {tier} {division}!");
};

ranked.OnDemotion += (tier, division) =>
{
    Debug.Log($"Demoted to {tier} {division}");
};

// Placement completed
ranked.OnPlacementCompleted += (rating, tier, division) => { };

// Match found
ranked.OnMatchFound += (lobby) =>
{
    Debug.Log($"Found match: {lobby.Code}");
};
```

## Matchmaking Range

The system searches for players within a skill range, expanding over time:

```
0-10s:  ±50 rating
10-20s: ±100 rating
20-30s: ±200 rating
30s+:   ±400 rating
```

## Cloud Persistence

Rating data automatically syncs to EOS Cloud Storage:

```csharp
// Manual sync (usually not needed)
await ranked.SyncToCloudAsync();
await ranked.LoadFromCloudAsync();
```

## Integration with Match History

Ranked matches are automatically tracked in EOSMatchHistory:

```csharp
var history = EOSMatchHistory.Instance;
var recentRanked = history.GetRecentMatches(10)
    .Where(m => m.GameMode == "ranked");
```
