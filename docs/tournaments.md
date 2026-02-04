# Tournament Brackets

Organize competitive tournaments with single/double elimination or round robin formats.

## Overview

The tournament system supports:
- Single elimination (lose once, out)
- Double elimination (losers bracket with grand finals)
- Round robin (everyone plays everyone)
- Seeding (random, by rating, manual)
- Best-of series matches
- Bracket visualization data

## Creating a Tournament

```csharp
var tournament = EOSTournamentManager.Instance;

// Simple creation
var t = tournament.CreateTournament("Weekly Cup", TournamentFormat.SingleElimination);

// With options
var t = tournament.CreateTournament(new TournamentOptions()
    .WithName("Pro League Finals")
    .WithDescription("Top 8 players compete")
    .WithFormat(TournamentFormat.DoubleElimination)
    .WithSeeding(SeedingMethod.ByRating)
    .WithMaxParticipants(16)
    .WithBestOf(3)
    .WithGrandFinalsBestOf(5)
);
```

## Registration

```csharp
// Register individual players
tournament.RegisterParticipant(puid, "PlayerName");
tournament.RegisterParticipant(puid2, "PlayerName2", seed: 1);  // Manual seed

// Register teams
tournament.RegisterTeam(teamId, "Team Alpha", new List<string> { player1, player2 });

// Unregister (before tournament starts)
tournament.UnregisterParticipant(puid);

// Check participants
var participants = tournament.ActiveTournament.Participants;
```

## Starting the Tournament

```csharp
// Start generates brackets and begins round 1
bool success = tournament.StartTournament();

// Check state
if (tournament.IsTournamentActive)
{
    int round = tournament.ActiveTournament.CurrentRound;
}
```

## Reporting Results

```csharp
// Get current matches
var matches = tournament.GetCurrentRoundMatches();

foreach (var match in matches.Where(m => m.State == MatchState.Ready))
{
    // Display match: match.Participant1Id vs match.Participant2Id
}

// Report result
tournament.ReportMatchResult(matchId, winnerId, winnerScore: 2, loserScore: 1);

// Bracket automatically advances if AutoAdvance is enabled (default)
// Or manually advance:
tournament.AdvanceBracket();
```

## Bracket Visualization

```csharp
// Get winners bracket data
var bracket = tournament.GetBracketData(BracketType.Winners);

Debug.Log($"Tournament: {bracket.TournamentName}");
Debug.Log($"Total Rounds: {bracket.TotalRounds}");

foreach (var round in bracket.Rounds)
{
    Debug.Log($"--- {round.RoundName} ---");
    foreach (var match in round.Matches)
    {
        var p1 = tournament.GetParticipant(match.Participant1Id)?.Name ?? "TBD";
        var p2 = tournament.GetParticipant(match.Participant2Id)?.Name ?? "TBD";
        string status = match.State == MatchState.Completed
            ? $"Winner: {tournament.GetParticipant(match.WinnerId)?.Name}"
            : match.State.ToString();
        Debug.Log($"  {p1} vs {p2} - {status}");
    }
}

// Get losers bracket (double elimination only)
var losersBracket = tournament.GetBracketData(BracketType.Losers);
```

## Standings

```csharp
// Get current standings
var standings = tournament.GetStandings();

int place = 1;
foreach (var p in standings)
{
    Debug.Log($"{place}. {p.Name} - W:{p.Wins} L:{p.Losses}");
    place++;
}
```

## Tournament Formats

### Single Elimination
One loss and you're out. Standard bracket tournament.

```csharp
var t = tournament.CreateTournament("Quick Cup", TournamentFormat.SingleElimination);
```

- 8 players = 3 rounds (Quarter, Semi, Final)
- 16 players = 4 rounds
- Non-power-of-2 counts get byes

### Double Elimination
Two losses to be eliminated. Losers bracket gives second chances.

```csharp
var t = tournament.CreateTournament("Major", TournamentFormat.DoubleElimination);
```

- Winners bracket losers drop to losers bracket
- Losers bracket winner plays winners bracket winner in Grand Finals
- If losers bracket winner wins Grand Finals, a reset match is played

### Round Robin
Everyone plays everyone. Winner determined by record.

```csharp
var t = tournament.CreateTournament("League", TournamentFormat.RoundRobin);
```

- All matchups generated at start
- Final standings based on wins

## Seeding

Control initial bracket positions:

```csharp
// Random seeding (default)
options.WithSeeding(SeedingMethod.Random);

// Seed by player rating (highest rated = #1 seed)
options.WithSeeding(SeedingMethod.ByRating);
// Set ratings before registration:
// participant.Rating = 1500;

// Manual seeding
options.WithSeeding(SeedingMethod.Manual);
tournament.RegisterParticipant(puid, "TopPlayer", seed: 1);
tournament.RegisterParticipant(puid2, "SecondBest", seed: 2);
```

Proper seeding separates top players in the bracket so they don't meet until later rounds.

## Best-of Series

Configure match series length:

```csharp
var t = tournament.CreateTournament(new TournamentOptions()
    .WithBestOf(3)           // Bo3 for regular matches
    .WithGrandFinalsBestOf(5) // Bo5 for grand finals
);
```

Score tracking is automatic when reporting results.

## Events

```csharp
// Tournament lifecycle
tournament.OnTournamentCreated += (t) => { };
tournament.OnTournamentStarted += (t) => { };
tournament.OnTournamentEnded += (t, winner) => {
    Debug.Log($"Winner: {winner.Name}");
};

// Match events
tournament.OnMatchReady += (match) => {
    // Notify players their match is ready
};
tournament.OnMatchCompleted += (match) => { };

// Participant events
tournament.OnParticipantEliminated += (p) => {
    Debug.Log($"{p.Name} eliminated in {p.FinalPlacement} place");
};

// Round progression
tournament.OnRoundAdvanced += (round) => {
    Debug.Log($"Now in round {round}");
};
```

## Queries

```csharp
// Find specific match
var match = tournament.FindMatch(matchId);

// Get participant's matches
var matches = tournament.GetParticipantMatches(puid);

// Get matches for a round
var round2 = tournament.GetRoundMatches(2, BracketType.Winners);

// Total rounds
int total = tournament.GetTotalRounds();
```

## Cancel Tournament

```csharp
tournament.CancelTournament();
```

## Match States

| State | Description |
|-------|-------------|
| Pending | Waiting for feeder matches to complete |
| Ready | Both participants known, ready to play |
| InProgress | Match started (optional tracking) |
| Completed | Result reported |
| Bye | One participant advances automatically |

## Example Flow

```csharp
var tm = EOSTournamentManager.Instance;

// 1. Create tournament
var t = tm.CreateTournament(new TournamentOptions()
    .WithName("Friday Night Tournament")
    .WithFormat(TournamentFormat.SingleElimination)
    .WithBestOf(3)
);

// 2. Register players
tm.RegisterParticipant(player1Puid, "Player1");
tm.RegisterParticipant(player2Puid, "Player2");
tm.RegisterParticipant(player3Puid, "Player3");
tm.RegisterParticipant(player4Puid, "Player4");

// 3. Start
tm.StartTournament();

// 4. Get current matches
var matches = tm.GetCurrentRoundMatches();
// Round 1: Player1 vs Player4, Player2 vs Player3 (seeded)

// 5. Report results
tm.ReportMatchResult(matches[0].Id, player1Puid, 2, 1);
tm.ReportMatchResult(matches[1].Id, player2Puid, 2, 0);

// 6. Round advances automatically
// Round 2: Player1 vs Player2

// 7. Report final
var final = tm.GetCurrentRoundMatches()[0];
tm.ReportMatchResult(final.Id, player1Puid, 2, 1);

// 8. Tournament complete
// Winner: Player1
```

## UI Integration

The bracket data structure is designed for easy UI rendering:

```csharp
void DrawBracket()
{
    var bracket = tournament.GetBracketData();

    // Draw each round as a column
    float xOffset = 0;
    foreach (var round in bracket.Rounds)
    {
        DrawRoundHeader(xOffset, round.RoundName);

        float yOffset = 0;
        foreach (var match in round.Matches)
        {
            DrawMatch(xOffset, yOffset, match);
            yOffset += matchHeight;
        }
        xOffset += columnWidth;
    }
}
```
