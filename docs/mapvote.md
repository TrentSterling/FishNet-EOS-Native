# Map/Mode Voting

Let players vote on the next map or game mode.

## Overview

The map/mode voting system allows democratic selection of game settings. Features include:

- Support for maps, modes, or any custom options
- Configurable vote duration and tie breakers
- Live or hidden vote counts
- Allows vote changing
- Preset templates for common setups

## Basic Usage

### Simple Map Vote

```csharp
var mapVote = EOSMapVoteManager.Instance;

// Start a map vote with string names
await mapVote.StartMapVoteAsync("Vote for Next Map",
    "Dust 2", "Inferno", "Mirage", "Nuke");
```

### Simple Mode Vote

```csharp
// Start a mode vote
await mapVote.StartModeVoteAsync("Choose Game Mode",
    "Deathmatch", "Team DM", "Capture the Flag");
```

### Custom Options

```csharp
// Create custom options with IDs and display names
var options = new List<VoteOption>
{
    new VoteOption("dust2", "Dust 2", "map"),
    new VoteOption("inferno", "Inferno", "map"),
    new VoteOption("mirage", "Mirage", "map"),
    new VoteOption("nuke", "Nuke", "map"),
};

await mapVote.StartVoteAsync("Vote for Next Map", options);
```

### Full Option Configuration

```csharp
var options = new List<VoteOption>
{
    new VoteOption
    {
        Id = "dust2",
        DisplayName = "Dust 2",
        Description = "Classic desert map",
        Category = "map",
        ImageUrl = "https://example.com/dust2.png"
    },
    // ... more options
};
```

## Casting Votes

```csharp
// Vote by index (0-based)
await mapVote.CastVoteAsync(0);  // Vote for first option
await mapVote.CastVoteAsync(2);  // Vote for third option

// Vote by option ID
await mapVote.CastVoteByIdAsync("dust2");
await mapVote.CastVoteByIdAsync("inferno");
```

## Configuration

### Timing

```csharp
// Vote duration (10-120 seconds)
mapVote.VoteDuration = 30f;

// Auto-start timer when vote begins
mapVote.AutoStartTimer = true;
```

### Vote Behavior

```csharp
// Allow players to change their vote
mapVote.AllowVoteChange = true;

// Show vote counts in real-time (false = reveal at end)
mapVote.ShowLiveResults = true;

// Show toast notifications
mapVote.ShowToasts = true;
```

### Tie Breaking

```csharp
// Random selection from tied options
mapVote.TieBreakerMode = TieBreaker.Random;

// First option in list wins ties
mapVote.TieBreakerMode = TieBreaker.FirstOption;

// Host must choose the winner
mapVote.TieBreakerMode = TieBreaker.HostChoice;

// Start a new vote with only tied options
mapVote.TieBreakerMode = TieBreaker.Revote;
```

## Checking Status

```csharp
// Is a vote active?
if (mapVote.IsVoteActive)
{
    var vote = mapVote.CurrentVote;

    // Vote info
    Debug.Log($"Title: {vote.Title}");
    Debug.Log($"Total votes: {vote.TotalVotes}");
    Debug.Log($"Time left: {mapVote.TimeRemaining}s");
}

// Get my vote (-1 if not voted)
int myVote = mapVote.GetMyVote();

// Check if I've voted
if (mapVote.HasVoted())
{
    Debug.Log("Already voted");
}

// Get vote counts per option
int[] counts = mapVote.GetVoteCounts();
for (int i = 0; i < counts.Length; i++)
{
    Debug.Log($"Option {i}: {counts[i]} votes");
}

// Get current leader(s)
List<int> leaders = mapVote.GetCurrentLeaders();
if (leaders.Count > 1)
{
    Debug.Log("Currently tied!");
}
```

## Host Controls

```csharp
// Extend the timer
mapVote.ExtendTimer(15f);  // Add 15 seconds

// End vote immediately (triggers result calculation)
mapVote.EndVoteNow();

// Cancel the vote entirely
await mapVote.CancelVoteAsync();

// Resolve a tie (when TieBreaker is HostChoice)
await mapVote.ResolveTieAsync(winnerIndex);
```

## Events

```csharp
// Vote started
mapVote.OnVoteStarted += (voteData) =>
{
    Debug.Log($"Vote started: {voteData.Title}");
    foreach (var option in voteData.Options)
    {
        Debug.Log($"  - {option.DisplayName}");
    }
};

// Someone cast a vote
mapVote.OnVoteCast += (voterPuid, optionIndex) =>
{
    Debug.Log($"Player {voterPuid} voted for option {optionIndex}");
};

// Timer tick (every second)
mapVote.OnTimerTick += (secondsRemaining) =>
{
    Debug.Log($"{secondsRemaining} seconds left");
};

// Vote ended
mapVote.OnVoteEnded += (voteData, winningOption, winningIndex) =>
{
    Debug.Log($"Winner: {winningOption.DisplayName}");

    // Load the winning map/mode
    LoadMap(winningOption.Id);
};

// Tie needs host decision
mapVote.OnTieNeedsDecision += (tiedOptions) =>
{
    Debug.Log("Tie! Host must choose:");
    foreach (var option in tiedOptions)
    {
        Debug.Log($"  - {option.DisplayName}");
    }
};
```

## Preset Templates

The manager includes common presets for quick setup:

```csharp
// FPS maps
var fpsMaps = EOSMapVoteManager.CommonMaps.FPS;
// Contains: Dust 2, Inferno, Mirage, Nuke

// Arena maps
var arenaMaps = EOSMapVoteManager.CommonMaps.Arena;
// Contains: Colosseum, The Pit, Tower, Arena

// Standard modes
var standardModes = EOSMapVoteManager.CommonModes.Standard;
// Contains: Deathmatch, Team Deathmatch, CTF, KOTH

// Casual modes
var casualModes = EOSMapVoteManager.CommonModes.Casual;
// Contains: Free For All, Infection, Gun Game

// Use presets
await mapVote.StartVoteAsync("Vote for Map", fpsMaps.ToList());
```

## UI Integration

The F1 debug panel includes a Map/Mode Voting section:

- Enable/disable toggle
- Active vote display with option buttons
- Live vote counts (if enabled)
- Timer display
- Host controls (End Now, +15s, Cancel)
- Quick start buttons for demo votes

## How It Works

1. **Host starts vote** - Options broadcast via lobby attribute
2. **Players vote** - Selections stored as member attributes
3. **Timer counts down** - Tick events fired every second
4. **Time expires** - Host calculates result
5. **Winner determined** - Based on votes and tie breaker
6. **Result broadcast** - OnVoteEnded fired with winner

## Implementation Example

### End-of-Match Flow

```csharp
public class MatchManager : MonoBehaviour
{
    private EOSMapVoteManager _mapVote;

    void Start()
    {
        _mapVote = EOSMapVoteManager.Instance;
        _mapVote.OnVoteEnded += HandleVoteEnded;
    }

    public void OnMatchEnded()
    {
        // Start map vote at end of match
        var maps = new[] { "Map A", "Map B", "Map C", "Random" };
        _ = _mapVote.StartMapVoteAsync("Vote for Next Map", maps);
    }

    private void HandleVoteEnded(MapVoteData data, VoteOption winner, int index)
    {
        string nextMap = winner.Id;

        if (nextMap == "random")
        {
            nextMap = GetRandomMap();
        }

        // Load the winning map
        StartCoroutine(LoadMapCoroutine(nextMap));
    }
}
```

### Custom UI Integration

```csharp
void OnGUI()
{
    if (!_mapVote.IsVoteActive) return;

    var vote = _mapVote.CurrentVote;
    var counts = _mapVote.GetVoteCounts();
    int myVote = _mapVote.GetMyVote();

    GUILayout.Label($"{vote.Title} - {_mapVote.TimeRemaining:0}s");

    for (int i = 0; i < vote.Options.Count; i++)
    {
        string label = $"{vote.Options[i].DisplayName} ({counts[i]} votes)";

        if (myVote == i)
            label += " [YOUR VOTE]";

        if (GUILayout.Button(label))
        {
            _ = _mapVote.CastVoteAsync(i);
        }
    }
}
```

## Best Practices

### Do

- Keep option count reasonable (3-5 is ideal)
- Use clear, recognizable option names
- Show timer prominently in your UI
- Handle the "no votes" edge case (first option wins)

### Don't

- Use more than 8 options (can overwhelm players)
- Set very short timers (15s minimum recommended)
- Hide live results in casual games (frustrates players)
- Forget to handle the OnVoteEnded event

## Troubleshooting

### Votes not syncing

- Ensure all players are in the same lobby
- Check that member attribute updates are working
- Verify lobby attribute limit isn't exceeded

### Timer not accurate

- Timer is local to each client
- Small variations are normal
- Host determines final end time

### Tie issues

- With TieBreaker.HostChoice, ensure you call ResolveTieAsync
- Random tie breaking may feel unfair - consider Revote mode
- FirstOption can be predictable - shuffle options first
