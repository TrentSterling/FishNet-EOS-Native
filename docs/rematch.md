# Match Rematch System

Vote to play again after a match ends.

## Overview

The rematch system provides:
- Post-match rematch voting
- Host force rematch option
- Swap teams toggle
- Keep settings toggle
- Vote timeout
- Consecutive rematch limits

## Basic Usage

```csharp
var rematch = EOSRematchManager.Instance;

// Call when match ends
rematch.OnMatchEnded(matchId);

// Propose rematch (any player)
rematch.ProposeRematch();

// Vote on rematch
rematch.VoteRematch(true);   // Yes
rematch.VoteRematch(false);  // No

// Check status
if (rematch.IsRematchActive)
{
    var data = rematch.CurrentRematch;
    Debug.Log($"Votes: {data.YesCount}/{data.RequiredVotes}");
}
```

## Match Lifecycle

```csharp
// Game ends - enable rematch
void OnGameEnded()
{
    rematch.OnMatchEnded("match_12345");
    // Auto-offer is enabled by default
}

// Rematch accepted - start new game
rematch.OnRematchAccepted += (data) =>
{
    rematch.OnRematchStarted();  // Call when new game starts
    StartNewGame(data.SwapTeams, data.KeepSettings);
};

// Reset for new session
rematch.Reset();
```

## Proposing Rematch

```csharp
// Any player can propose
if (rematch.CanProposeRematch())
{
    rematch.ProposeRematch();
}

// Check if can propose
bool can = rematch.CanProposeRematch();
// False if: disabled, match not ended, already voting, max rematches reached

// Auto-offer (enabled by default)
rematch.AutoOfferRematch = true;
rematch.AutoOfferDelay = 3f;  // Wait 3s after match end
```

## Voting

```csharp
// Cast vote
rematch.VoteRematch(true);   // Vote yes
rematch.VoteRematch(false);  // Vote no

// Check if voted
if (rematch.HasVoted())
{
    bool? myVote = rematch.GetMyVote();
    // true = yes, false = no, null = not voted
}

// Vote progress
var data = rematch.CurrentRematch;
int yes = data.YesCount;
int no = data.NoCount;
int required = data.RequiredVotes;
float percent = data.YesPercent;
float timeLeft = data.TimeRemaining;
```

## Host Controls

```csharp
// Force rematch (no vote needed)
if (rematch.HostCanForceRematch)
{
    rematch.ForceRematch();
}

// Cancel rematch vote
rematch.CancelRematch();  // Host or proposer can cancel
```

## Rematch Options

```csharp
// Toggle swap teams
rematch.SetSwapTeams(true);   // Teams switch sides
rematch.SetSwapTeams(false);  // Same teams

// Toggle keep settings
rematch.SetKeepSettings(true);  // Same map/mode
rematch.SetKeepSettings(false); // New settings

// Check current options
var data = rematch.CurrentRematch;
bool swap = data.SwapTeams;
bool keep = data.KeepSettings;
```

## Events

```csharp
// Rematch proposed
rematch.OnRematchProposed += (data) =>
{
    Debug.Log($"Rematch proposed by {data.ProposedBy}");
    ShowRematchUI();
};

// Vote cast
rematch.OnVoteCast += (data, puid, votedYes) =>
{
    string name = GetPlayerName(puid);
    Debug.Log($"{name} voted {(votedYes ? "YES" : "NO")}");
};

// Rematch accepted (enough yes votes)
rematch.OnRematchAccepted += (data) =>
{
    Debug.Log("Rematch accepted!");
    PrepareForRematch(data.SwapTeams);
};

// Rematch declined (not enough yes votes)
rematch.OnRematchDeclined += (data) =>
{
    Debug.Log("Rematch declined");
    ReturnToLobby();
};

// Rematch cancelled
rematch.OnRematchCancelled += (data) =>
{
    Debug.Log("Rematch cancelled");
    HideRematchUI();
};

// Rematch starting
rematch.OnRematchStarting += (data) =>
{
    Debug.Log("Starting rematch...");
    LoadGame();
};

// Timer tick
rematch.OnRematchTimerTick += (secondsLeft) =>
{
    UpdateTimerUI(secondsLeft);
};

// New lobby created for rematch
rematch.OnRematchLobbyCreated += (code) =>
{
    Debug.Log($"Rematch lobby: {code}");
};
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Allow Rematch | true | Enable rematch system |
| Rematch Timeout | 30s | Time to vote |
| Vote Threshold | 0.5 | Percent required (50%) |
| Host Can Force | true | Host can skip voting |
| Auto Offer | true | Auto-propose after match |
| Auto Offer Delay | 3s | Wait before auto-propose |
| Default Swap Teams | false | Default swap setting |
| Default Keep Settings | true | Default keep setting |
| Allow Swap Toggle | true | Can change swap option |
| Max Consecutive | 5 | Max rematches in a row |

### Runtime Configuration

```csharp
rematch.AllowRematch = true;
rematch.RematchTimeout = 30f;
rematch.RematchVoteThreshold = 0.5f;  // 50%
rematch.HostCanForceRematch = true;
rematch.AutoOfferRematch = true;
rematch.AutoOfferDelay = 3f;
rematch.DefaultSwapTeams = false;
rematch.DefaultKeepSettings = true;
rematch.AllowSwapTeamsToggle = true;
rematch.MaxConsecutiveRematches = 5;
```

## Rematch States

```csharp
public enum RematchState
{
    None,       // No rematch available
    Proposed,   // Voting in progress
    Accepted,   // Enough votes, starting
    Declined,   // Not enough votes
    Starting,   // Rematch beginning
    Cancelled   // Cancelled by host/timeout
}
```

```csharp
switch (rematch.CurrentState)
{
    case RematchState.None:
        // Match in progress or no vote
        break;
    case RematchState.Proposed:
        // Show voting UI
        break;
    case RematchState.Accepted:
        // Prepare for rematch
        break;
    case RematchState.Declined:
        // Return to lobby
        break;
}
```

## Rematch Data

```csharp
public class RematchData
{
    public string MatchId;
    public string ProposedBy;
    public RematchInitiator Initiator;
    public RematchState State;
    public float ProposedTime;
    public float TimeoutSeconds;
    public bool SwapTeams;
    public bool KeepSettings;
    public HashSet<string> VotesYes;
    public HashSet<string> VotesNo;
    public List<string> EligiblePlayers;
    public int RequiredVotes;
    public string NewLobbyCode;

    // Properties
    public int YesCount { get; }
    public int NoCount { get; }
    public int TotalVotes { get; }
    public bool IsExpired { get; }
    public float TimeRemaining { get; }
    public bool HasEnoughYes { get; }
    public float YesPercent { get; }
}
```

## UI Example

```csharp
void DrawRematchUI()
{
    var rematch = EOSRematchManager.Instance;

    if (!rematch.IsRematchActive) return;

    var data = rematch.CurrentRematch;

    GUILayout.BeginArea(new Rect(Screen.width/2 - 150, 100, 300, 250));
    GUILayout.Box("REMATCH?");

    // Timer
    GUILayout.Label($"Time: {data.TimeRemaining:F0}s");

    // Vote progress
    GUILayout.Label($"Votes: {data.YesCount}/{data.RequiredVotes} needed");

    // Progress bar
    float progress = data.YesPercent;
    GUILayout.HorizontalSlider(progress, 0, 1);

    // Options
    if (rematch.AllowSwapTeamsToggle)
    {
        bool swap = GUILayout.Toggle(data.SwapTeams, "Swap Teams");
        if (swap != data.SwapTeams)
        {
            rematch.SetSwapTeams(swap);
        }
    }

    // Vote buttons
    if (!rematch.HasVoted())
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("YES"))
        {
            rematch.VoteRematch(true);
        }
        if (GUILayout.Button("NO"))
        {
            rematch.VoteRematch(false);
        }
        GUILayout.EndHorizontal();
    }
    else
    {
        bool? vote = rematch.GetMyVote();
        GUILayout.Label($"You voted: {(vote == true ? "YES" : "NO")}");
    }

    GUILayout.EndArea();
}
```

## Full Integration Example

```csharp
public class GameManager : MonoBehaviour
{
    private EOSRematchManager _rematch;

    void Start()
    {
        _rematch = EOSRematchManager.Instance;

        _rematch.OnRematchProposed += OnRematchProposed;
        _rematch.OnRematchAccepted += OnRematchAccepted;
        _rematch.OnRematchDeclined += OnRematchDeclined;
    }

    public void EndGame(string winnerId)
    {
        // Game over logic
        ShowEndGameScreen(winnerId);

        // Enable rematch voting
        _rematch.OnMatchEnded(currentMatchId);
    }

    private void OnRematchProposed(RematchData data)
    {
        ShowRematchVoteUI();
    }

    private void OnRematchAccepted(RematchData data)
    {
        HideRematchVoteUI();

        // Handle team swap
        if (data.SwapTeams)
        {
            SwapPlayerTeams();
        }

        // Keep or change settings
        if (!data.KeepSettings)
        {
            ShowMapVote();
        }
        else
        {
            StartNewGame();
        }

        _rematch.OnRematchStarted();
    }

    private void OnRematchDeclined(RematchData data)
    {
        HideRematchVoteUI();
        ReturnToMainMenu();
    }
}
```

## Best Practices

1. **Call OnMatchEnded** - Always call when game ends
2. **Call OnRematchStarted** - Track consecutive rematches
3. **Handle SwapTeams** - Actually swap player teams
4. **Show clear UI** - Display votes, timer, options
5. **Reset on new session** - Call Reset() when returning to main menu

## Vote Threshold Examples

| Players | Threshold | Required |
|---------|-----------|----------|
| 2 | 50% | 1 |
| 4 | 50% | 2 |
| 6 | 50% | 3 |
| 8 | 50% | 4 |
| 10 | 50% | 5 |

## Consecutive Rematch Limit

The system tracks consecutive rematches to prevent infinite loops:

```csharp
// Check count
int count = rematch.ConsecutiveRematches;

// Max 5 by default
if (count >= rematch.MaxConsecutiveRematches)
{
    // Can't propose more rematches
}

// Reset count
rematch.Reset();  // Resets to 0
```
