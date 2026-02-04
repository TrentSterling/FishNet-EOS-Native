# Voice Chat Zones

Control who can hear who in voice chat with proximity, team, or custom zone modes.

## Overview

Voice zones let you implement:

- **Proximity Chat** - Volume scales with distance (PUBG, VRChat style)
- **Team Chat** - Only teammates hear each other (most competitive games)
- **Custom Zones** - Room-based chat using trigger colliders
- **Hybrid** - Team + proximity combined

## Zone Modes

| Mode | Description |
|------|-------------|
| Global | Everyone hears everyone equally (default) |
| Proximity | Volume based on distance |
| Team | Only teammates can hear each other |
| TeamProximity | Teammates only, with distance falloff |
| Custom | Trigger-based rooms/areas |

## Basic Setup

### Add the Manager

```csharp
// Auto-added to EOSVoiceManager's GameObject
var zoneManager = EOSVoiceZoneManager.Instance;

// Or add manually
gameObject.AddComponent<EOSVoiceZoneManager>();
```

### Set Zone Mode

```csharp
// Switch modes
zoneManager.SetZoneMode(VoiceZoneMode.Proximity);
zoneManager.SetZoneMode(VoiceZoneMode.Team);
zoneManager.SetZoneMode(VoiceZoneMode.Global);

// Or via property
zoneManager.ZoneMode = VoiceZoneMode.TeamProximity;
```

## Proximity Mode

Players hear each other based on distance. Great for immersive games.

### Configuration

```csharp
var zoneManager = EOSVoiceZoneManager.Instance;

// Set mode
zoneManager.SetZoneMode(VoiceZoneMode.Proximity);

// Configure distances
zoneManager.MaxHearingDistance = 30f;  // Silent beyond this
zoneManager.FadeStartDistance = 10f;   // Full volume within this

// Or use helper method
zoneManager.ConfigureProximity(
    maxDistance: 30f,
    fadeStart: 10f,
    minVol: 0f,      // Volume at max distance
    maxVol: 100f     // Volume when close
);
```

### Position Tracking

The manager needs to know player positions. Two methods:

#### Automatic (FishNet)

```csharp
// Enable auto-discovery (default)
zoneManager._useFishNetPositions = true;
zoneManager._playerTag = "Player";  // Tag your player prefab

// Manager will find NetworkObjects tagged "Player"
```

#### Manual Registration

```csharp
// Register local player
zoneManager.RegisterLocalPlayer(myPlayerTransform);

// Register remote players (call when they spawn)
zoneManager.RegisterPlayer(remotePuid, remotePlayerTransform);

// Unregister when they leave
zoneManager.UnregisterPlayer(remotePuid);
```

### Falloff Curve

```csharp
// Linear falloff (default)
zoneManager._falloffExponent = 1f;

// Quadratic (volume drops faster)
zoneManager._falloffExponent = 2f;

// Square root (volume stays higher longer)
zoneManager._falloffExponent = 0.5f;
```

## Team Mode

Only hear your teammates. Essential for competitive games.

### Basic Usage

```csharp
var zoneManager = EOSVoiceZoneManager.Instance;
zoneManager.SetZoneMode(VoiceZoneMode.Team);

// Set local player's team
zoneManager.SetTeam(1);  // Team 1

// Set other players' teams (call when you learn their team)
zoneManager.SetPlayerTeam(remotePuid, 2);  // They're on team 2
```

### Cross-Team Audio

Allow hearing enemies at reduced volume:

```csharp
zoneManager.ConfigureTeam(
    allowCrossTeam: true,
    crossTeamMultiplier: 0.25f  // Enemies at 25% volume
);
```

### Team + Proximity

Combine both - only hear teammates, and they get quieter with distance:

```csharp
zoneManager.SetZoneMode(VoiceZoneMode.TeamProximity);
zoneManager.SetTeam(1);
zoneManager.ConfigureProximity(maxDistance: 50f, fadeStart: 20f);
```

## Custom Zones (Trigger-Based)

Create room-based voice chat using Unity colliders.

### Setup Trigger Zones

1. Create a GameObject with a Collider (Box, Sphere, etc.)
2. Set collider to `Is Trigger = true`
3. Add `EOSVoiceTriggerZone` component
4. Set a unique zone name

```csharp
// In scene:
// - GameObject "Room1" with BoxCollider (trigger) + EOSVoiceTriggerZone (name: "room1")
// - GameObject "Room2" with BoxCollider (trigger) + EOSVoiceTriggerZone (name: "room2")
// - GameObject "Lobby" with BoxCollider (trigger) + EOSVoiceTriggerZone (name: "lobby", isDefault: true)
```

### Enable Custom Mode

```csharp
zoneManager.SetZoneMode(VoiceZoneMode.Custom);

// Zones are detected automatically via triggers
// Players in "room1" hear only others in "room1"
// Players in "room2" hear only others in "room2"
```

### Manual Zone Assignment

```csharp
// Set local player's zone manually
zoneManager.SetLocalZone("vip-room");

// Set remote player's zone
zoneManager.SetPlayerZone(remotePuid, "main-hall");

// Get player's zone
string zone = zoneManager.GetPlayerZone(remotePuid);
```

## Events

```csharp
var zoneManager = EOSVoiceZoneManager.Instance;

// Mode changed
zoneManager.OnZoneModeChanged += (newMode) =>
{
    Debug.Log($"Voice mode: {newMode}");
};

// Player volume changed significantly
zoneManager.OnPlayerVolumeChanged += (puid, volume) =>
{
    Debug.Log($"Player {puid} volume now {volume}%");
};

// Proximity: player entered hearing range
zoneManager.OnPlayerEnteredRange += (puid) =>
{
    Debug.Log($"Can now hear {puid}");
};

// Proximity: player exited hearing range
zoneManager.OnPlayerExitedRange += (puid) =>
{
    Debug.Log($"Can no longer hear {puid}");
};
```

## Queries

```csharp
// Get player's current volume
float vol = zoneManager.GetPlayerVolume(puid);

// Check if player is audible
bool canHear = zoneManager.IsPlayerInRange(puid);

// Get all audible players
var hearable = zoneManager.GetPlayersInRange();

// Get distance to player
float dist = zoneManager.GetDistanceToPlayer(puid);

// Get player's team
int team = zoneManager.GetPlayerTeam(puid);

// Get player's zone (custom mode)
string zone = zoneManager.GetPlayerZone(puid);
```

## Inspector Settings

| Setting | Description |
|---------|-------------|
| Zone Mode | Current voice zone mode |
| Max Hearing Distance | Proximity: max range (meters) |
| Fade Start Distance | Proximity: distance before falloff starts |
| Min Volume | Minimum volume at max distance (0-100) |
| Max Volume | Maximum volume when close (0-100) |
| Falloff Exponent | Curve shape (1=linear, 2=fast drop, 0.5=slow drop) |
| Local Team | Current team number |
| Allow Cross Team Audio | Let enemies be heard at reduced volume |
| Cross Team Volume Multiplier | Enemy volume (0-1) |
| Update Interval | How often to recalculate (seconds) |
| Volume Change Threshold | Min change to trigger update |
| Player Tag | Tag for auto-discovering players |
| Use FishNet Positions | Auto-find FishNet NetworkObjects |

## Debug UI

The F1 debug panel's Voice tab includes a Voice Zones section:

- Mode selector buttons (Global/Proximity/Team/etc.)
- Current range and players in range (proximity mode)
- Team selector (team modes)
- Add manager button if not present

## Best Practices

### Proximity Chat

- Use 20-50m range for realistic conversations
- Set fade start at 1/3 of max distance
- Consider quadratic falloff for more natural sound

### Team Chat

- Sync team assignments when players spawn
- Use lobby member attributes to share team info
- Consider TeamProximity for large maps

### Custom Zones

- Mark one zone as "default" for outdoor/neutral areas
- Use overlapping triggers carefully (last entered wins)
- Consider combining with proximity for large rooms

## Troubleshooting

### Proximity not working

- Check that local player transform is registered
- Verify player tag matches your player prefab
- Ensure positions are being updated (not static)

### Team chat hearing wrong players

- Verify team assignments: `zoneManager.GetPlayerTeam(puid)`
- Check that SetPlayerTeam is called when you receive team info
- Team 0 is the default - make sure you're setting teams

### Custom zones not detecting

- Verify colliders are set to `Is Trigger`
- Check player has Rigidbody (triggers need one side with RB)
- Ensure EOSVoiceTriggerZone component is on trigger object

### Volume not updating

- Increase update interval for performance
- Decrease volume change threshold for responsiveness
- Check EOSVoiceManager.IsConnected is true
