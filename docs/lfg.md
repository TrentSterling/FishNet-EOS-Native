# LFG (Looking for Group)

Create and browse LFG posts to find players or groups.

## Overview

The LFG system allows players to:

- **Create posts** - "Looking for players to join my group" or "Looking for a group to join"
- **Browse posts** - Search and filter available LFG posts
- **Send requests** - Apply to join posts
- **Manage requests** - Accept or reject join requests

Posts are stored as special EOS lobbies and automatically expire.

## Basic Usage

### Create a Post

```csharp
var lfg = EOSLFGManager.Instance;

// Simple post
var (result, post) = await lfg.CreatePostAsync("Looking for ranked players");

// Post with options
var (result, post) = await lfg.CreatePostAsync(new LFGPostOptions()
    .WithTitle("Need 2 for competitive")
    .WithGameMode("ranked")
    .WithDesiredSize(4)
    .RequiresVoice(true)
);

// Full options
var options = new LFGPostOptions
{
    Type = LFGType.LookingForPlayers,
    Title = "Chill casual games",
    GameMode = "casual",
    Region = "us-east",
    DesiredSize = 6,
    VoiceRequired = false,
    CrossPlatform = true,
    DurationMinutes = 60,
    Tags = new List<string> { "friendly", "no-toxicity" }
};
var (result, post) = await lfg.CreatePostAsync(options);
```

### Search for Posts

```csharp
// Search all
var (result, posts) = await lfg.SearchPostsAsync();

// Search with filters
var (result, posts) = await lfg.SearchPostsAsync(new LFGSearchOptions()
    .WithGameMode("ranked")
    .WithRegion("us-east")
    .RequiresVoice(false)
);

// Search by game mode shortcut
var (result, posts) = await lfg.SearchByGameModeAsync("competitive");

// Refresh last search
var (result, posts) = await lfg.RefreshSearchAsync();

// Stop auto-refresh
lfg.StopAutoRefresh();
```

### Join Requests

```csharp
// Send a join request
var result = await lfg.SendJoinRequestAsync(post.PostId, "I'm a Diamond player!");

// Cancel a pending request
await lfg.CancelJoinRequestAsync(post.PostId);

// Accept a request (post owner only)
await lfg.AcceptJoinRequestAsync(request);

// Reject a request (post owner only)
await lfg.RejectJoinRequestAsync(request);
```

### Manage Your Post

```csharp
// Update status
await lfg.UpdatePostStatusAsync(LFGStatus.Full);
await lfg.UpdatePostStatusAsync(LFGStatus.InGame);

// Update size (auto-updates status)
await lfg.UpdatePostSizeAsync(currentMemberCount);

// Close/delete post
await lfg.ClosePostAsync();
```

## Post Types

| Type | Description |
|------|-------------|
| LookingForPlayers | You have a group, looking for members |
| LookingForGroup | You're solo, looking for a group to join |

## Post Status

| Status | Description |
|--------|-------------|
| Open | Accepting join requests |
| Full | Desired size reached |
| Closed | No longer accepting requests |
| InGame | Group is currently in a game |

## Search Options

```csharp
var options = new LFGSearchOptions
{
    Type = LFGType.LookingForPlayers,  // Filter by post type
    GameMode = "ranked",                // Filter by game mode
    Region = "us-east",                 // Filter by region
    MinRank = 1000,                     // Minimum rank requirement
    MaxRank = 2000,                     // Maximum rank requirement
    VoiceRequired = true,               // Require voice chat
    CrossPlatform = true,               // Allow cross-platform
    Tags = new List<string> { "competitive" },  // Required tags
    MaxResults = 50,                    // Max results to return
    ExcludeFull = true,                 // Skip full posts
    ExcludeExpired = true               // Skip expired posts
};
```

## Properties

```csharp
var lfg = EOSLFGManager.Instance;

// Your active post
LFGPost post = lfg.ActivePost;
bool hasPost = lfg.HasActivePost;

// Pending requests for your post
List<LFGJoinRequest> requests = lfg.PendingRequests;

// Last search results
List<LFGPost> results = lfg.SearchResults;

// Posts you've requested to join
List<string> sent = lfg.SentRequests;
```

## Post Properties

```csharp
var post = lfg.ActivePost;

string id = post.PostId;
string owner = post.OwnerPuid;
string ownerName = post.OwnerName;
LFGType type = post.Type;
LFGStatus status = post.Status;
string title = post.Title;
string mode = post.GameMode;
string region = post.Region;
int minRank = post.MinRank;
int maxRank = post.MaxRank;
int current = post.CurrentSize;
int desired = post.DesiredSize;
bool voice = post.VoiceRequired;
bool crossPlay = post.CrossPlatform;
List<string> tags = post.Tags;
long created = post.CreatedAt;
long expires = post.ExpiresAt;
string lobbyId = post.LobbyId;
string custom = post.CustomData;

// Computed properties
bool expired = post.IsExpired;
bool joinable = post.IsJoinable;
TimeSpan remaining = post.TimeRemaining;
```

## Events

```csharp
var lfg = EOSLFGManager.Instance;

// Post created
lfg.OnPostCreated += (post) =>
{
    Debug.Log($"Created post: {post.Title}");
};

// Post updated
lfg.OnPostUpdated += (post) =>
{
    Debug.Log($"Post updated: {post.Status}");
};

// Post closed
lfg.OnPostClosed += () =>
{
    Debug.Log("Post closed");
};

// Received join request
lfg.OnJoinRequestReceived += (request) =>
{
    Debug.Log($"Join request from: {request.RequesterName}");
    // Show notification, update UI
};

// Your request was accepted
lfg.OnJoinRequestAccepted += (post) =>
{
    Debug.Log($"Accepted to: {post.Title}");
    // Join the group's lobby
};

// Your request was rejected
lfg.OnJoinRequestRejected += (postId) =>
{
    Debug.Log("Request rejected");
};

// Search results received
lfg.OnSearchResultsReceived += (posts) =>
{
    Debug.Log($"Found {posts.Count} posts");
};
```

## Configuration

### Inspector Settings

| Setting | Description |
|---------|-------------|
| Default Duration Minutes | How long posts last (default: 60) |
| Refresh Interval | How often to refresh search (seconds) |
| Auto Refresh | Automatically refresh search results |
| Show Toasts | Show toast notifications for LFG events |

### Post Options

| Option | Default | Description |
|--------|---------|-------------|
| Type | LookingForPlayers | Post type |
| Title | "Looking for players" | Post title/description |
| GameMode | (empty) | Game mode filter |
| Region | (empty) | Region preference |
| MinRank | 0 | Minimum rank (0 = any) |
| MaxRank | 0 | Maximum rank (0 = any) |
| DesiredSize | 4 | Target group size |
| VoiceRequired | false | Require voice chat |
| CrossPlatform | true | Allow all platforms |
| DurationMinutes | 60 | How long post lasts |
| Tags | (empty) | Custom filter tags |

## Debug UI

The F1 debug panel includes an LFG section:

- Create post form (title, mode, size)
- Active post display with pending requests
- Accept/reject request buttons
- Browse posts with search/refresh
- Join button for each post

## Integration Example

### End-of-Match Flow

```csharp
public class MatchManager : MonoBehaviour
{
    private EOSLFGManager _lfg;

    void Start()
    {
        _lfg = EOSLFGManager.Instance;
        _lfg.OnJoinRequestReceived += OnRequestReceived;
        _lfg.OnJoinRequestAccepted += OnAccepted;
    }

    public void OnMatchEnded()
    {
        // Show LFG UI to find next group
        ShowLFGPanel();
    }

    private void OnRequestReceived(LFGJoinRequest request)
    {
        // Show notification
        ShowJoinRequestNotification(request);
    }

    private void OnAccepted(LFGPost post)
    {
        // Join the group's lobby
        if (!string.IsNullOrEmpty(post.LobbyCode))
        {
            _ = JoinLobbyAsync(post.LobbyCode);
        }
    }
}
```

### Auto-LFG on Disconnect

```csharp
void OnDisconnectedFromServer()
{
    if (_lfg.HasActivePost)
    {
        // Update status so people know we're available
        _ = _lfg.UpdatePostStatusAsync(LFGStatus.Open);
    }
}
```

## Best Practices

### Do

- Set appropriate expiration times (30-60 minutes typical)
- Use clear, descriptive titles
- Set rank requirements if skill matters
- Specify voice requirements upfront
- Handle expired posts gracefully

### Don't

- Create multiple posts (only one active allowed)
- Set very short durations (< 5 minutes)
- Ignore pending requests
- Leave stale posts open

## Troubleshooting

### Posts not appearing in search

- Check that the post hasn't expired
- Verify search filters match the post
- Ensure EOS is properly initialized
- Check bucket ID matches ("lfg")

### Can't join posts

- Post may be full (check IsJoinable)
- Already sent a request to this post
- Post may have been closed/expired

### Requests not syncing

- Join requests are lobby joins under the hood
- Check lobby member notifications are working
- Verify both players are online
