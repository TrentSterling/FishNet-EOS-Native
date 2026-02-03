# Text Chat

Lobby-based text chat with cloud-persisted history.

## Sending Messages

```csharp
var chat = EOSLobbyChatManager.Instance;

// Send a message to the lobby
chat.SendMessage("Hello everyone!");

// Send with formatting
chat.SendMessage("[Team] Ready to go!");
```

## Receiving Messages

```csharp
chat.OnMessageReceived += (sender, message, timestamp) =>
{
    Debug.Log($"[{timestamp:HH:mm}] {sender}: {message}");
};
```

## Chat History

Messages persist to cloud storage and reload when rejoining the same lobby.

### Automatic Persistence

- Messages auto-save when leaving a lobby
- Messages auto-load when joining a lobby with the same code
- History is associated with the lobby code, not lobby ID

### Manual Control

```csharp
// Force save current chat
await chat.SaveChatHistoryAsync("1234");

// Load history for a lobby
await chat.LoadChatHistoryAsync("1234");

// Delete history
await chat.DeleteChatHistoryAsync("1234");
```

## Message Events

```csharp
// New message received
chat.OnMessageReceived += (sender, message, timestamp) => { };

// Message sent confirmation
chat.OnMessageSent += (message) => { };

// History loaded
chat.OnHistoryLoaded += (messages) => { };
```

## Message Data

```csharp
public class ChatMessage
{
    public string SenderPuid { get; }
    public string SenderName { get; }
    public string Content { get; }
    public DateTime Timestamp { get; }
}

// Get recent messages
var messages = chat.GetRecentMessages(50);
```

## Rate Limiting

EOS enforces rate limits on lobby attribute updates (which includes chat):

| Limit | Value |
|-------|-------|
| Attribute updates | 100/min |

The chat system batches messages to stay within limits.

## Integration with UI

The F1 debug panel includes a chat interface. For custom UI:

```csharp
// Display messages
foreach (var msg in chat.GetRecentMessages(100))
{
    DisplayMessage(msg.SenderName, msg.Content, msg.Timestamp);
}

// Send from input field
public void OnSendClicked()
{
    if (!string.IsNullOrEmpty(inputField.text))
    {
        chat.SendMessage(inputField.text);
        inputField.text = "";
    }
}
```

## Moderation

Block players to hide their messages:

```csharp
var registry = EOSPlayerRegistry.Instance;

// Block a player
registry.BlockPlayer(puid);

// Messages from blocked players are filtered automatically
```
