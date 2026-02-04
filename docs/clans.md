# Teams & Clans

Create and manage persistent player groups with roles, chat, and member management.

## Overview

The clan system provides:
- Create/join/leave clans with tag and name
- Role-based permissions (Leader, Officer, Member)
- Invite and join request management
- Clan chat
- Member promotion/demotion/kick
- Clan history tracking

## Creating a Clan

```csharp
var clans = EOSClanManager.Instance;

// Create a clan
var (result, clan) = await clans.CreateClanAsync(
    name: "Elite Gamers",
    tag: "EG",
    description: "Top competitive players"
);

if (result == Result.Success)
{
    Debug.Log($"Created [{clan.Tag}] {clan.Name}");
}
```

## Clan Info

```csharp
// Check if in a clan
if (clans.IsInClan)
{
    var clan = clans.CurrentClan;
    Debug.Log($"Clan: [{clan.Tag}] {clan.Name}");
    Debug.Log($"Members: {clan.MemberCount}/{clan.MaxMembers}");
    Debug.Log($"Leader: {clan.LeaderName}");
    Debug.Log($"Description: {clan.Description}");
}

// My role
ClanRole role = clans.MyRole;
if (clans.IsLeader) { }   // Leader permissions
if (clans.IsOfficer) { }  // Officer or higher

// Get formatted tag
string tag = clans.GetFormattedTag();  // "[EG]"
```

## Joining & Leaving

```csharp
// Request to join a clan
await clans.RequestJoinAsync(clanId, "I want to join!");

// Accept a pending invite
if (clans.PendingInvites.Count > 0)
{
    var invite = clans.PendingInvites[0];
    await clans.AcceptInviteAsync(invite);
    // or
    await clans.DeclineInviteAsync(invite);
}

// Leave clan
await clans.LeaveClanAsync();

// Disband clan (leader only)
await clans.DisbandClanAsync();
```

## Member Management

### Inviting Players

```csharp
// Invite a player (officer+)
await clans.InvitePlayerAsync(targetPuid, "PlayerName");
```

### Handling Join Requests

```csharp
// Get pending requests (officer+)
var requests = clans.PendingRequests;

foreach (var request in requests)
{
    Debug.Log($"{request.RequesterName} wants to join");
    Debug.Log($"Message: {request.Message}");

    // Accept or reject
    await clans.AcceptJoinRequestAsync(request);
    // or
    await clans.RejectJoinRequestAsync(request);
}
```

### Roles & Permissions

```csharp
// Promote member (leader: to officer, officer: to member)
await clans.PromoteMemberAsync(memberPuid);

// Demote officer to member (leader only)
await clans.DemoteMemberAsync(memberPuid);

// Kick member (officer+, leader can kick officers)
await clans.KickMemberAsync(memberPuid);

// Get member info
var member = clans.GetMember(puid);
Debug.Log($"{member.Name} - {EOSClanManager.GetRoleName(member.Role)}");
```

### Role Hierarchy

| Role | Can Invite | Can Accept | Can Kick Members | Can Kick Officers | Can Promote | Can Demote |
|------|-----------|------------|------------------|-------------------|-------------|------------|
| Leader | Yes | Yes | Yes | Yes | Yes | Yes |
| Officer | Yes | Yes | Yes | No | No | No |
| Member | No | No | No | No | No | No |

## Clan Settings

```csharp
// Update settings (leader/officer)
await clans.UpdateClanSettingsAsync(new ClanSettings
{
    Name = "New Clan Name",
    Description = "Updated description",
    AllowOpenJoin = false,  // Require invite/request
    RequireApproval = true, // Requests need approval
    MaxMembers = 100
});
```

## Clan Chat

```csharp
// Send message
await clans.SendChatAsync("Hello team!");

// Read chat history
foreach (var msg in clans.ChatHistory)
{
    Debug.Log($"[{msg.SenderName}] {msg.Message}");
}

// Clear chat (officer+)
await clans.ClearChatAsync();
```

## Events

```csharp
// Clan lifecycle
clans.OnClanCreated += (clan) => Debug.Log($"Created {clan.Name}");
clans.OnClanJoined += (clan) => Debug.Log($"Joined {clan.Name}");
clans.OnClanLeft += () => Debug.Log("Left clan");
clans.OnClanUpdated += (clan) => { };

// Member events
clans.OnMemberJoined += (member) =>
{
    Debug.Log($"{member.Name} joined the clan!");
};

clans.OnMemberLeft += (puid) => { };
clans.OnMemberRoleChanged += (puid, newRole) => { };

// Invites and requests
clans.OnInviteReceived += (invite) =>
{
    Debug.Log($"Invited to [{invite.ClanTag}] {invite.ClanName}");
};

clans.OnJoinRequestReceived += (request) =>
{
    Debug.Log($"{request.RequesterName} wants to join");
};

// Chat
clans.OnChatMessageReceived += (msg) =>
{
    Debug.Log($"[{msg.SenderName}] {msg.Message}");
};

// Data
clans.OnDataLoaded += (data) => { };
```

## Clan History

```csharp
// View past clans
var history = clans.PlayerData.ClanHistory;

foreach (var entry in history)
{
    Debug.Log($"[{entry.ClanTag}] {entry.ClanName}");
    Debug.Log($"Reason: {entry.LeaveReason}");
}
```

## Display Helpers

```csharp
// Get formatted tag
string tag = clans.GetFormattedTag();  // "[EG]"
string memberTag = clans.GetMemberTag(puid);

// Role display
string roleName = EOSClanManager.GetRoleName(ClanRole.Officer);  // "Officer"
string roleIcon = EOSClanManager.GetRoleIcon(ClanRole.Leader);   // "C" (Crown)
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Max Clan Size | 50 | Maximum members per clan |
| Default Allow Open Join | false | New clans allow anyone to join |
| Default Require Approval | true | Join requests need approval |
| Max Pending Invites | 10 | Max invites player can have |
| Max Pending Requests | 20 | Max requests clan can have |
| Invite Expiry Hours | 72 | Invites expire after this time |

### Limits

| Limit | Value |
|-------|-------|
| Max tag length | 6 characters |
| Max name length | 32 characters |
| Chat history | 100 messages |

## UI Example

```csharp
void DrawClanUI()
{
    var clans = EOSClanManager.Instance;

    if (!clans.IsInClan)
    {
        // Show create/join UI
        if (GUILayout.Button("Create Clan"))
        {
            _ = clans.CreateClanAsync("My Clan", "MC");
        }
        return;
    }

    // Clan info
    var clan = clans.CurrentClan;
    GUILayout.Label($"[{clan.Tag}] {clan.Name}");
    GUILayout.Label($"Members: {clan.MemberCount}/{clan.MaxMembers}");
    GUILayout.Label($"Your role: {EOSClanManager.GetRoleName(clans.MyRole)}");

    // Member list
    GUILayout.Label("--- Members ---");
    foreach (var member in clan.Members)
    {
        string icon = EOSClanManager.GetRoleIcon(member.Role);
        GUILayout.Label($"[{icon}] {member.Name}");
    }

    // Actions
    if (clans.IsOfficer && clan.PendingRequests.Count > 0)
    {
        GUILayout.Label($"Pending requests: {clan.PendingRequests.Count}");
    }

    if (GUILayout.Button("Leave Clan"))
    {
        _ = clans.LeaveClanAsync();
    }
}
```

## Data Persistence

Clan data is stored in EOS Cloud Storage (`clan_data.json`):
- Current clan membership
- Pending invites
- Clan history
- Chat messages

Data syncs across devices automatically.
