# Invites & Presence

Send game invites and track player presence.

## Sending Invites

### To a Specific Player

```csharp
var invites = EOSCustomInvites.Instance;

// Send invite to a player
await invites.SendInviteAsync(targetPuid, lobbyCode);

// With custom message
await invites.SendInviteAsync(targetPuid, lobbyCode, "Join my game!");
```

### To Friends

Quick invite to all online friends:

```csharp
// Invite all friends
await invites.InviteAllFriendsAsync(lobbyCode);

// Or specific friends from the UI (F1 panel has quick-send buttons)
```

## Receiving Invites

```csharp
invites.OnInviteReceived += (senderPuid, senderName, lobbyCode, message) =>
{
    // Show invite notification
    ShowInvitePopup(senderName, lobbyCode, message);
};

// Accept invite
await transport.JoinLobbyAsync(lobbyCode);
```

## Toast Notifications

Invites automatically show toast notifications via `EOSToastIntegration`:

```csharp
// Toasts are auto-shown for:
// - Received invites
// - Friend status changes
// - Lobby events

// Manual toasts
EOSToastManager.Info("Player joined");
EOSToastManager.Success("Connected", "Lobby code: 1234");
EOSToastManager.Warning("High ping detected");
EOSToastManager.Error("Connection lost");

// Configure position
EOSToastManager.Instance.Position = ToastPosition.TopRight;
EOSToastManager.Instance.DefaultDuration = 3f;
```

## Presence

Track and share player status.

### Setting Your Presence

```csharp
var presence = EOSPresence.Instance;

// Set rich presence string
presence.SetRichPresence("Playing Deathmatch on Dust2");

// Set status
presence.SetStatus(PresenceStatus.Online);
presence.SetStatus(PresenceStatus.Away);
presence.SetStatus(PresenceStatus.DoNotDisturb);
```

### Reading Others' Presence

```csharp
// Get a player's presence
var status = presence.GetPlayerStatus(puid);
string richText = presence.GetPlayerRichPresence(puid);

// Subscribe to changes
presence.OnPresenceChanged += (puid, status, richPresence) =>
{
    Debug.Log($"{puid} is now: {richPresence}");
};
```

## Friend Quick Actions

The F1 UI provides quick actions for friends:

### Recently Played
- [★]/[☆] - Toggle friend status
- Platform icon (🖥️📱👓)
- Time since last interaction

### Local Friends
- Status indicator (🟢 Online, ⚪ Offline)
- [Invite] - Send game invite (if you're hosting)
- [Join] - Join their game (if they're in one)
- [Remove] - Remove from friends

### Invites Section
- Quick-send buttons for each friend
- Bulk invite option

## Custom Invite Payloads

Send additional data with invites:

```csharp
var payload = new InvitePayload
{
    LobbyCode = "1234",
    GameMode = "ranked",
    Map = "dust2",
    CustomMessage = "Need one more for ranked!"
};

await invites.SendCustomInviteAsync(targetPuid, payload);
```

## Events Reference

```csharp
// Invites
invites.OnInviteReceived += (sender, senderName, code, message) => { };
invites.OnInviteSent += (target, code) => { };
invites.OnInviteAccepted += (sender) => { };
invites.OnInviteDeclined += (sender) => { };

// Presence
presence.OnPresenceChanged += (puid, status, richPresence) => { };
presence.OnFriendOnline += (puid, name) => { };
presence.OnFriendOffline += (puid, name) => { };
```
