# Party System

Persistent groups that follow the leader across games.

## Overview

Parties are independent of lobbies - they're persistent social groups that can move together between games. When the party leader joins a game, members can automatically follow.

## Creating a Party

```csharp
var party = EOSPartyManager.Instance;

// Create a party
await party.CreatePartyAsync("My Squad", maxSize: 4);

// Party code is generated (6 characters)
Debug.Log($"Party code: {party.PartyCode}");  // e.g., "ABC123"
```

## Joining a Party

```csharp
// Join by code
await party.JoinPartyAsync("ABC123");

// Or accept an invite (see below)
```

## Inviting Friends

```csharp
// Invite a specific player
await party.InviteToPartyAsync(friendPuid);

// Invited players receive notification
party.OnInviteReceived += (inviterName, partyCode) =>
{
    // Show invite UI
};
```

## Following the Leader

When the party leader joins a game, members follow based on the configured mode:

```csharp
// Leader joins a game lobby
await party.LeaderJoinGameAsync("1234");

// Members are notified
party.OnLeaderJoinedGame += (gameCode) =>
{
    Debug.Log($"Leader joined game: {gameCode}");
};

// Members can manually follow
await party.FollowLeaderAsync();
```

### Follow Modes

```csharp
// Automatic - members auto-follow (default)
party.FollowMode = PartyFollowMode.Automatic;

// Confirm - prompt members before following
party.FollowMode = PartyFollowMode.Confirm;

// Ready Check - wait for everyone to ready up
party.FollowMode = PartyFollowMode.ReadyCheck;

// Manual - members must click to follow
party.FollowMode = PartyFollowMode.Manual;
```

## Ready Checks

Verify all members are ready before joining:

```csharp
// Leader starts a ready check
party.StartReadyCheck("1234");  // Game code to join

// Members respond
party.OnReadyCheckStarted += (data) =>
{
    // Show ready check UI
};

await party.RespondToReadyCheckAsync(true);  // Ready!

// Completion callback
party.OnReadyCheckCompleted += (allReady) =>
{
    if (allReady)
        Debug.Log("Everyone is ready!");
};
```

## Party Leadership

```csharp
// Promote someone to leader
await party.PromoteToLeaderAsync(memberPuid);

// Kick a member (leader only)
await party.KickMemberAsync(memberPuid);

// Check leadership
if (party.IsLeader)
{
    // Show leader controls
}
```

## Party Chat

```csharp
// Send message to party
await party.SendPartyChatAsync("Let's go!");

// Receive messages
party.OnPartyChatReceived += (senderName, message) =>
{
    Debug.Log($"[Party] {senderName}: {message}");
};
```

## Leaving / Dissolving

```csharp
// Leave the party
await party.LeavePartyAsync();

// Dissolve party entirely (leader only)
await party.DissolvePartyAsync();
```

## Configuration Options

### Full Lobby Behavior

What happens when the game lobby is full:

```csharp
// Block - can't join at all
party.FullLobbyBehavior = PartyFullLobbyBehavior.BlockJoin;

// Warn - ask leader what to do
party.FullLobbyBehavior = PartyFullLobbyBehavior.WarnAndAsk;

// Partial - join whoever fits
party.FullLobbyBehavior = PartyFullLobbyBehavior.PartialJoin;

// Leader Only - leader joins solo
party.FullLobbyBehavior = PartyFullLobbyBehavior.LeaderOnly;
```

### Persistence

```csharp
// Dissolves when leader quits
party.Persistence = PartyPersistence.SessionBased;

// Lives forever (until dissolved)
party.Persistence = PartyPersistence.Persistent;

// Expires after idle timeout
party.Persistence = PartyPersistence.TimedExpiry;
```

### Other Settings

```csharp
party.AfkTimeout = 10f;                    // Kick after 10s AFK
party.SeparatePartyVoice = true;           // Party voice channel
party.AutoPromoteOnLeaderLeave = true;     // Auto-promote on leave
party.AllowPublicJoin = false;             // Require invite
party.FriendsOnly = true;                  // Only friends can join
```

## Events Reference

```csharp
party.OnMemberJoined += (member) => { };
party.OnMemberLeft += (puid) => { };
party.OnLeaderChanged += (oldPuid, newPuid) => { };
party.OnLeaderJoinedGame += (gameCode) => { };
party.OnFollowRequested += (request) => { };  // For Confirm mode
party.OnReadyCheckStarted += (data) => { };
party.OnReadyCheckCompleted += (allReady) => { };
party.OnPartyChatReceived += (sender, message) => { };
party.OnPartyDissolved += () => { };
party.OnKicked += () => { };
```

## Party vs Lobby

| Feature | Party | Lobby |
|---------|-------|-------|
| Purpose | Social group | Game session |
| Persistence | Configurable | Session only |
| Max size | Configurable (default 4) | 64 |
| Voice | Optional separate channel | Always included |
| Survives game end | Yes | No |
