# Global Chat Channels

Chat across all connected players in persistent channels.

## Overview

Global chat provides:
- Multiple named channels (General, Trade, LFG, etc.)
- Subscribe/unsubscribe to channels
- Message history per channel
- User muting
- System messages for joins/leaves

## Joining Channels

```csharp
var globalChat = EOSGlobalChatManager.Instance;

// Join a channel
await globalChat.JoinChannelAsync("General");
await globalChat.JoinChannelAsync("Trade");
await globalChat.JoinChannelAsync("LFG");

// Leave a channel
await globalChat.LeaveChannelAsync("Trade");

// Check subscription
if (globalChat.IsInChannel("General"))
{
    Debug.Log("In #General");
}

// List subscribed channels
foreach (var channel in globalChat.SubscribedChannels)
{
    Debug.Log($"Subscribed to #{channel}");
}
```

## Sending Messages

```csharp
// Send to a channel
await globalChat.SendMessageAsync("General", "Hello everyone!");

// Send with validation
string channel = "Trade";
string message = "WTS Epic Sword 100g";

if (globalChat.IsInChannel(channel))
{
    await globalChat.SendMessageAsync(channel, message);
}
```

## Receiving Messages

```csharp
// Listen for messages
globalChat.OnMessageReceived += (msg) =>
{
    if (msg.IsSystem)
    {
        Debug.Log($"[{msg.Channel}] {msg.Message}");
    }
    else
    {
        Debug.Log($"[{msg.Channel}] {msg.SenderName}: {msg.Message}");
    }
};

// Formatted display
globalChat.OnMessageReceived += (msg) =>
{
    string formatted = globalChat.FormatMessage(msg);
    // "[14:32] PlayerName: Hello!"

    string withChannel = globalChat.FormatMessageWithChannel(msg);
    // "[#General] [14:32] PlayerName: Hello!"
};
```

## Message History

```csharp
// Get channel history
var messages = globalChat.GetMessageHistory("General", count: 50);

foreach (var msg in messages)
{
    Debug.Log(globalChat.FormatMessage(msg));
}

// Get recent messages from all channels
var allRecent = globalChat.GetAllRecentMessages(count: 20);

// Clear history
globalChat.ClearChannelHistory("General");
```

## Channel Users

```csharp
// Get user count
int count = globalChat.GetChannelUserCount("General");

// Get user list
var users = globalChat.GetChannelUsers("General");
foreach (var user in users)
{
    Debug.Log($"{user.Name} (joined {FormatJoinTime(user.JoinedAt)})");
}
```

## User Moderation

```csharp
// Mute a user (hide their messages)
globalChat.MuteUser(puid);

// Check if muted
if (globalChat.IsUserMuted(puid))
{
    Debug.Log("User is muted");
}

// Unmute
globalChat.UnmuteUser(puid);

// Clear all mutes
globalChat.ClearMutedUsers();

// List muted users
foreach (var muted in globalChat.MutedUsers)
{
    Debug.Log($"Muted: {muted}");
}
```

## Events

```csharp
// Channel events
globalChat.OnChannelJoined += (channel) =>
{
    Debug.Log($"Joined #{channel}");
};

globalChat.OnChannelLeft += (channel) =>
{
    Debug.Log($"Left #{channel}");
};

// User events
globalChat.OnUserJoined += (channel, puid, name) =>
{
    Debug.Log($"{name} joined #{channel}");
};

globalChat.OnUserLeft += (channel, puid) =>
{
    Debug.Log($"User left #{channel}");
};

// Member count
globalChat.OnChannelMemberCountChanged += (channel, count) =>
{
    Debug.Log($"#{channel} now has {count} users");
};

// Messages
globalChat.OnMessageReceived += (msg) => { };
```

## Default Channels

Built-in channels:
- **General** - General discussion
- **Trade** - Buying/selling
- **LFG** - Looking for group
- **Help** - Questions and assistance
- **Competitive** - Ranked/competitive talk

```csharp
// List default channels
foreach (var channel in globalChat.DefaultChannels)
{
    Debug.Log($"Available: #{channel}");
}
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Auto Join Channels | ["General"] | Channels to join on startup |
| Max Channels | 5 | Maximum simultaneous subscriptions |
| Poll Interval | 2s | How often to check for new messages |
| Show System Messages | true | Show join/leave notifications |

### Limits

| Limit | Value |
|-------|-------|
| Max message length | 500 characters |
| History per channel | 100 messages |
| Max channels | 5 |

## UI Example

```csharp
void DrawGlobalChat()
{
    var chat = EOSGlobalChatManager.Instance;

    // Channel tabs
    GUILayout.BeginHorizontal();
    foreach (var channel in chat.SubscribedChannels)
    {
        if (GUILayout.Button($"#{channel}"))
        {
            _activeChannel = channel;
        }
    }
    GUILayout.EndHorizontal();

    // Messages
    var messages = chat.GetMessageHistory(_activeChannel, 20);
    foreach (var msg in messages)
    {
        // Skip muted users
        if (!msg.IsSystem && chat.IsUserMuted(msg.SenderPuid))
            continue;

        GUILayout.Label(chat.FormatMessage(msg));
    }

    // Input
    GUILayout.BeginHorizontal();
    _chatInput = GUILayout.TextField(_chatInput, GUILayout.Width(300));
    if (GUILayout.Button("Send") && !string.IsNullOrEmpty(_chatInput))
    {
        _ = chat.SendMessageAsync(_activeChannel, _chatInput);
        _chatInput = "";
    }
    GUILayout.EndHorizontal();

    // Users
    int userCount = chat.GetChannelUserCount(_activeChannel);
    GUILayout.Label($"Users online: {userCount}");
}
```

## Display Helpers

```csharp
// Channel display name
string display = EOSGlobalChatManager.GetChannelDisplayName("General");
// "#General"

// Format message
string formatted = globalChat.FormatMessage(msg);
// "[14:32] PlayerName: Hello!"

string withChannel = globalChat.FormatMessageWithChannel(msg);
// "[#General] [14:32] PlayerName: Hello!"
```

## Best Practices

1. **Auto-join relevant channels** - Join channels matching your game's needs
2. **Respect mutes** - Check `IsUserMuted` before displaying messages
3. **Handle system messages** - Show join/leave notifications appropriately
4. **Limit history display** - Don't render all 100 messages at once
5. **Provide mute option** - Let users mute disruptive players
