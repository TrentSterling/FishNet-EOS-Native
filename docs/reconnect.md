# Auto-Reconnect System

Automatically reconnect players after unexpected disconnects.

## Overview

The reconnect system provides:
- Automatic reconnection attempts
- Exponential backoff
- Session state preservation
- Server-side slot reservation
- Late rejoin options (player/spectator)
- Toast notifications

## Basic Usage

```csharp
var reconnect = EOSAutoReconnect.Instance;

// Enable/disable
reconnect.Enabled = true;

// Configure attempts
reconnect.MaxAttempts = 5;
reconnect.InitialRetryDelay = 2f;
reconnect.UseExponentialBackoff = true;

// Show notifications
reconnect.ShowNotifications = true;
```

The system automatically:
1. Detects unexpected disconnects
2. Saves session state
3. Attempts to rejoin the same lobby
4. Restores session on success

## Manual Reconnection

```csharp
// Trigger manual reconnect
reconnect.TryReconnect();

// Reconnect to specific lobby
reconnect.TryReconnect("ABC123");

// Cancel reconnection
reconnect.CancelReconnect();

// Check status
if (reconnect.IsReconnecting)
{
    Debug.Log($"Attempt {reconnect.CurrentAttempt}/{reconnect.MaxAttempts}");
}
```

## Session Preservation

Save and restore player state across reconnects:

```csharp
// Enable session preservation
reconnect.PreserveSession = true;

// Update session data periodically (in your player code)
void Update()
{
    reconnect.UpdateSessionPosition(transform.position, transform.rotation);
}

// Store custom data
reconnect.SetSessionData("loadout", "assault");
reconnect.SetSessionData("health", health.ToString());

// Retrieve custom data
string loadout = reconnect.GetSessionData("loadout");

// Update team/score
reconnect.UpdateSessionStats(team: 1, score: 15);
```

### Session Restoration

```csharp
reconnect.OnSessionRestored += (session) =>
{
    // Restore player position
    player.transform.position = session.LastPosition;
    player.transform.rotation = session.LastRotation;

    // Restore team
    player.Team = session.Team;

    // Restore custom data
    if (session.CustomData.TryGetValue("loadout", out var loadout))
    {
        player.Loadout = loadout;
    }
};
```

## Slot Reservation (Server)

Hold slots for disconnected players:

```csharp
// Configure reservation time (default 120s)
reconnect.SlotReservationTime = 120f;

// Check if player has reservation
if (reconnect.HasReservedSlot(puid))
{
    var session = reconnect.GetReservedSlot(puid);
    Debug.Log($"{session.PlayerName} has {session.TimeUntilExpiry}s to reconnect");
}

// Get all reserved slots
var reserved = reconnect.GetReservedSlots();
foreach (var kvp in reserved)
{
    Debug.Log($"Reserved: {kvp.Value.PlayerName}");
}

// Manually release a slot
reconnect.ReleaseSlot(puid);
```

### Events (Server)

```csharp
reconnect.OnSlotReserved += (session) =>
{
    Debug.Log($"Slot reserved for {session.PlayerName}");
    // Don't fill their spot with backfill
};

reconnect.OnSlotExpired += (puid) =>
{
    Debug.Log($"Reservation expired for {puid}");
    // Now allow backfill for this slot
};
```

## Late Rejoin Options

Handle reconnecting when game is in progress:

```csharp
// Configure late rejoin mode
reconnect.LateRejoinMode = LateRejoinMode.RejoinAsPlayer;
```

### Modes

| Mode | Description |
|------|-------------|
| RejoinAsPlayer | Rejoin normally, respawn player |
| RejoinAsSpectator | Enter spectator mode instead |
| AskUser | Prompt user for choice |
| Deny | Don't allow late rejoin |

### User Choice Mode

```csharp
reconnect.LateRejoinMode = LateRejoinMode.AskUser;

reconnect.OnLateRejoinChoice += (callback) =>
{
    // Show UI for user to choose
    ShowLateRejoinDialog(
        onPlayer: () => callback(true),
        onSpectator: () => callback(false)
    );
};
```

### Bypass JIP Restrictions

```csharp
// Allow reconnecting players to bypass JIP restrictions
reconnect.AllowReconnectBypassJip = true;
```

This allows reconnecting players to rejoin even if the game phase normally wouldn't allow new players.

## Events

```csharp
// Reconnect attempt started
reconnect.OnReconnectAttempt += (attempt) =>
{
    Debug.Log($"Reconnect attempt {attempt}");
    ShowReconnectingUI(attempt);
};

// Reconnection succeeded
reconnect.OnReconnected += () =>
{
    Debug.Log("Reconnected!");
    HideReconnectingUI();
};

// All attempts failed
reconnect.OnReconnectFailed += () =>
{
    Debug.Log("Failed to reconnect");
    ShowMainMenu();
};

// Session saved (on disconnect)
reconnect.OnSessionSaved += (session) =>
{
    Debug.Log($"Session saved at {session.LastPosition}");
};

// Session restored (on reconnect)
reconnect.OnSessionRestored += (session) =>
{
    Debug.Log($"Restoring session from {session.DisconnectTime}");
};
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | true | Enable auto-reconnect |
| Max Attempts | 5 | Maximum reconnection attempts |
| Initial Retry Delay | 2s | First retry delay |
| Max Retry Delay | 30s | Maximum delay (for backoff) |
| Use Exponential Backoff | true | Increase delay each attempt |
| Show Notifications | true | Show toast notifications |
| Preserve Session | true | Save/restore player state |
| Slot Reservation Time | 120s | How long to hold slots |
| Late Rejoin Mode | RejoinAsPlayer | How to handle late rejoin |
| Allow Reconnect Bypass JIP | true | Bypass JIP for reconnects |

## Exponential Backoff

With exponential backoff enabled, retry delays increase:

| Attempt | Delay (default) |
|---------|-----------------|
| 1 | 2s |
| 2 | 3s |
| 3 | 4.5s |
| 4 | 6.75s |
| 5 | 10.1s |

Delays cap at `MaxRetryDelay` (default 30s).

## Session Data Structure

```csharp
public class ReconnectSessionData
{
    public string Puid;
    public string PlayerName;
    public string LobbyCode;
    public string ReconnectToken;
    public float DisconnectTime;
    public float ReservationExpiry;
    public Vector3 LastPosition;
    public Quaternion LastRotation;
    public int Team;
    public int Score;
    public Dictionary<string, string> CustomData;
    public bool IsReserved;

    public bool IsExpired { get; }
    public float TimeUntilExpiry { get; }
}
```

## UI Example

```csharp
void OnGUI()
{
    var reconnect = EOSAutoReconnect.Instance;
    if (!reconnect.IsReconnecting) return;

    // Overlay
    GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");

    // Message
    GUILayout.BeginArea(new Rect(Screen.width/2 - 150, Screen.height/2 - 50, 300, 100));
    GUILayout.Box("Connection Lost");
    GUILayout.Label($"Reconnecting... Attempt {reconnect.CurrentAttempt}/{reconnect.MaxAttempts}");

    if (GUILayout.Button("Cancel"))
    {
        reconnect.CancelReconnect();
    }
    GUILayout.EndArea();
}
```

## Integration with Backfill

The reconnect system integrates with the backfill system:

```csharp
// When player disconnects (server)
reconnect.OnSlotReserved += (session) =>
{
    // Don't count this slot for backfill
    backfill.SetCurrentPlayers(backfill.CurrentPlayers);  // Don't decrement
};

// When reservation expires
reconnect.OnSlotExpired += (puid) =>
{
    // Now allow backfill
    backfill.OnPlayerLeft(puid);
};
```

## Best Practices

1. **Keep session fresh** - Update position/state periodically
2. **Handle restoration** - Spawn player at saved position
3. **Reserve slots** - Don't backfill disconnected players immediately
4. **Show UI** - Keep player informed during reconnect
5. **Offer cancel** - Let player give up and return to menu
6. **Test thoroughly** - Simulate disconnects in development

## Troubleshooting

**Not reconnecting:**
- Check `Enabled` is true
- Verify `_wasConnected` was set (connected at least once)
- Check lobby still exists

**Session not restored:**
- Ensure `PreserveSession` is true
- Subscribe to `OnSessionRestored` event
- Update session data before disconnect

**Slot not reserved:**
- Server-side only feature
- Check `SlotReservationTime` > 0
- Verify PUID mapping is working
