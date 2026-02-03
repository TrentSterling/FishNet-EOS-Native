# Friends System

Local friends are players you've marked from the Recently Played list. They persist locally and sync to EOS Cloud Storage for cross-device support.

## Basic Usage

```csharp
var registry = EOSPlayerRegistry.Instance;

// Check if someone is a friend
if (registry.IsFriend(puid))
{
    Debug.Log("This player is a friend!");
}

// Add/Remove friends
registry.AddFriend(puid);
registry.RemoveFriend(puid);
registry.ToggleFriend(puid);  // Toggle state

// Get all friends
var friends = registry.GetFriends();  // List<(string puid, string name)>

foreach (var (puid, name) in friends)
{
    Debug.Log($"Friend: {name}");
}
```

## Friend Events

```csharp
registry.OnFriendChanged += (puid, isNowFriend) =>
{
    if (isNowFriend)
        Debug.Log($"Added friend: {puid}");
    else
        Debug.Log($"Removed friend: {puid}");
};
```

## Cloud Sync

Friends sync to EOS Cloud Storage (400MB per player) for cross-device access.

```csharp
// Upload local friends to cloud
await registry.SyncFriendsToCloudAsync();

// Download friends from cloud (merges with local)
await registry.LoadFriendsFromCloudAsync();

// Two-way sync (recommended)
await registry.FullCloudSyncAsync();
```

Sync happens automatically when:
- Adding or removing a friend
- Launching the game (if previously synced)

## Friend Notes

Add personal notes to remember where you met someone:

```csharp
// Set a note
registry.SetNote(puid, "Met in ranked match, good teammate");

// Get a note
string note = registry.GetNote(puid);

// Notes persist with friend data
```

## Online Status

Check if friends are online and where:

```csharp
// Get friend's current status
var status = registry.GetFriendStatus(puid);

switch (status)
{
    case FriendStatus.InLobby:
        Debug.Log("Friend is in a lobby");
        break;
    case FriendStatus.InGame:
        Debug.Log("Friend is playing");
        break;
    case FriendStatus.Offline:
        Debug.Log("Friend is offline");
        break;
}

// Quick join if they're in a joinable lobby
if (registry.CanJoinFriend(puid))
{
    string lobbyCode = registry.GetFriendLobbyCode(puid);
    await transport.JoinLobbyAsync(lobbyCode);
}
```

## Block List

Block players to hide them from Recently Played and mute their chat:

```csharp
// Block a player
registry.BlockPlayer(puid);

// Unblock
registry.UnblockPlayer(puid);

// Check block status
if (registry.IsBlocked(puid))
{
    // This player is blocked
}

// Get all blocked players
var blocked = registry.GetBlockedPlayers();
```

Blocked players:
- Hidden from Recently Played list
- Chat messages filtered
- Cannot send you invites

## UI Integration

The F1 debug panel shows:

### Recently Played Section
- Star toggle [★]/[☆] to add/remove friends
- Platform icon showing their device
- Recent interaction time

### Local Friends Section
- Online status indicator
- Platform icon
- [Invite] button (if you're in a lobby)
- [Join] button (if they're in a lobby)
- [Remove] button

### Cloud Sync Button
- ☁ button triggers `FullCloudSyncAsync()`
- Shows sync status

## Events Reference

```csharp
registry.OnFriendChanged += (puid, isNowFriend) => { };
registry.OnFriendStatusChanged += (puid, status) => { };
registry.OnBlockListChanged += (puid, isBlocked) => { };
registry.OnCloudSyncCompleted += (success) => { };
```
