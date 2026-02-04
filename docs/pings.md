# Ping & Callout System

Mark locations and objects for team communication.

## Overview

The ping system provides:
- World position pings
- Object pings (NetworkObjects)
- Multiple ping types (enemy, danger, item, etc.)
- Visual ping indicators
- Team-based visibility
- Ping cooldowns and limits

## Basic Pinging

```csharp
var pings = EOSPingManager.Instance;

// Ping a position
pings.PingPosition(worldPosition);
pings.PingPosition(worldPosition, PingType.Danger);
pings.PingPosition(worldPosition, PingType.Item, "Health Pack");

// Ping an object
pings.PingObject(networkObject);
pings.PingObject(networkObject, PingType.Enemy, "Sniper");

// Quick ping at crosshair
pings.PingAtCrosshair(Camera.main, maxDistance: 100f);
pings.PingAtCrosshair(Camera.main, 100f, PingType.Enemy);
```

## Convenience Methods

```csharp
// Enemy ping
pings.PingEnemy(position);
pings.PingEnemy(position, "Enemy sniper!");

// Danger ping
pings.PingDanger(position);
pings.PingDanger(position, "Mines!");

// Help ping
pings.PingHelp(position);

// Item ping
pings.PingItem(position);
pings.PingItem(position, "Ammo");
```

## Ping Types

```csharp
public enum PingType
{
    Default,    // Generic ping
    Location,   // "Going here"
    Enemy,      // "Enemy spotted"
    Danger,     // "Danger!"
    Item,       // "Item here"
    Help,       // "Need help"
    Attack,     // "Attack here"
    Defend,     // "Defend here"
    Custom      // Custom type
}
```

| Type | Icon | Color | Use Case |
|------|------|-------|----------|
| Default | ! | White | General marking |
| Location | > | White | Movement destination |
| Enemy | X | Red | Enemy spotted |
| Danger | !! | Orange | Hazard warning |
| Item | * | Yellow | Loot/pickup |
| Help | ? | Cyan | Request assistance |
| Attack | >> | Red | Attack order |
| Defend | [] | Blue | Defense position |

## Visibility

```csharp
public enum PingVisibility
{
    All,    // Everyone sees
    Team,   // Teammates only (default)
    Self    // Only you
}
```

```csharp
// Set local player's team
pings.SetTeam(1);  // Team 1

// Team pings are only visible to same team
pings.TeamPingsOnly = true;  // Default
```

## Querying Pings

```csharp
// Get all visible pings
var visible = pings.GetVisiblePings();

// Get pings near a position
var nearby = pings.GetPingsNearPosition(position, radius: 10f);

// Get nearest ping
var nearest = pings.GetNearestPing(playerPosition);

// All active pings (unfiltered)
var all = pings.ActivePings;
```

## Managing Pings

```csharp
// Remove a specific ping
pings.RemovePing(pingId);

// Remove all pings from a player
pings.RemovePlayerPings(puid);

// Clear all pings
pings.ClearAllPings();
```

## Cooldowns and Limits

```csharp
// Check if can ping
if (pings.CanPing())
{
    pings.PingPosition(position);
}

// Get remaining cooldown
float cooldown = pings.GetCooldownRemaining();
if (cooldown > 0)
{
    Debug.Log($"Wait {cooldown:F1}s");
}

// Configure limits
pings.PingCooldown = 0.5f;      // Seconds between pings
pings.MaxActivePings = 3;        // Max pings per player
pings.MaxPingDistance = 100f;    // Max raycast distance
```

## Visual Indicators

The system includes visual world-space indicators:

```csharp
var visualizer = EOSPingVisualizer.Instance;

// Toggle automatic indicator creation
visualizer.AutoCreateIndicators = true;

// Manual control
var indicator = visualizer.CreateIndicator(pingData);
visualizer.RemoveIndicator(pingId);
visualizer.ClearAllIndicators();

// Manager settings
pings.ShowPingIndicators = true;   // Show world indicators
pings.ShowPingOnMinimap = true;    // Show on minimap (custom impl)
pings.PlaySoundOnPing = true;      // Play sound
pings.ShowSenderName = true;       // Show "[Player] Enemy!"
```

## Events

```csharp
// Ping created
pings.OnPingCreated += (ping) =>
{
    Debug.Log($"{ping.SenderName} pinged: {ping.Type}");

    // Play sound
    if (ping.Type == PingType.Enemy)
        PlaySound("enemy_ping");
};

// Ping expired (duration ended)
pings.OnPingExpired += (ping) =>
{
    Debug.Log($"Ping expired: {ping.PingId}");
};

// Ping manually removed
pings.OnPingRemoved += (ping) =>
{
    Debug.Log($"Ping removed: {ping.PingId}");
};

// Cooldown started
pings.OnCooldownStarted += (puid, duration) =>
{
    ShowCooldownUI(duration);
};
```

## Configuration

### Inspector Settings

| Setting | Default | Description |
|---------|---------|-------------|
| Default Ping Duration | 5s | How long pings last |
| Ping Cooldown | 0.5s | Time between pings |
| Max Active Pings | 3 | Per player limit |
| Max Ping Distance | 100 | Raycast distance |
| Allow Enemy Pings | true | Enable enemy marking |
| Allow Custom Labels | true | Allow custom text |
| Team Pings Only | true | Teammates only |
| Show Sender Name | true | Show who pinged |

### Runtime Configuration

```csharp
pings.DefaultPingDuration = 5f;
pings.PingCooldown = 0.5f;
pings.MaxActivePings = 3;
pings.MaxPingDistance = 100f;
pings.AllowEnemyPings = true;
pings.AllowCustomLabels = true;
pings.TeamPingsOnly = true;
pings.ShowSenderName = true;
```

## Ping Data

```csharp
public class PingData
{
    public string PingId;           // Unique identifier
    public string SenderPuid;       // Who created it
    public string SenderName;       // Display name
    public int SenderTeam;          // Team number
    public PingType Type;           // Ping type
    public Vector3 WorldPosition;   // World location
    public string TargetObjectId;   // NetworkObject ID (if any)
    public string CustomLabel;      // Custom text
    public float CreatedTime;       // When created
    public float Duration;          // How long it lasts
    public PingVisibility Visibility;
    public Dictionary<string, string> CustomData;

    // Properties
    public bool IsExpired { get; }
    public float TimeRemaining { get; }
}
```

## Input Integration

### Keyboard Example

```csharp
void Update()
{
    // Quick ping
    if (Input.GetKeyDown(KeyCode.G))
    {
        EOSPingManager.Instance.PingAtCrosshair(Camera.main);
    }

    // Ping wheel (hold)
    if (Input.GetKey(KeyCode.G))
    {
        ShowPingWheel();
    }
    else if (Input.GetKeyUp(KeyCode.G))
    {
        var selectedType = GetSelectedPingType();
        EOSPingManager.Instance.PingAtCrosshair(Camera.main, 100f, selectedType);
        HidePingWheel();
    }
}
```

### Mouse Example

```csharp
void Update()
{
    // Middle click to ping
    if (Input.GetMouseButtonDown(2))
    {
        EOSPingManager.Instance.PingAtCrosshair(Camera.main);
    }
}
```

## UI Example

### Ping Wheel

```csharp
void DrawPingWheel()
{
    GUILayout.BeginArea(new Rect(Screen.width/2 - 100, Screen.height/2 - 100, 200, 200));

    string[] types = { "Ping", "Enemy", "Danger", "Item", "Help" };
    PingType[] pingTypes = {
        PingType.Default, PingType.Enemy, PingType.Danger,
        PingType.Item, PingType.Help
    };

    for (int i = 0; i < types.Length; i++)
    {
        if (GUILayout.Button(types[i]))
        {
            EOSPingManager.Instance.PingAtCrosshair(Camera.main, 100f, pingTypes[i]);
        }
    }

    GUILayout.EndArea();
}
```

### Active Pings Display

```csharp
void DrawActivePings()
{
    var pings = EOSPingManager.Instance.GetVisiblePings();

    GUILayout.Label($"Active Pings: {pings.Count}");

    foreach (var ping in pings)
    {
        string icon = EOSPingManager.GetPingIcon(ping.Type);
        string label = ping.CustomLabel ?? EOSPingManager.GetPingTypeName(ping.Type);
        float distance = Vector3.Distance(transform.position, ping.WorldPosition);

        GUILayout.Label($"[{icon}] {ping.SenderName}: {label} ({distance:F0}m)");
    }
}
```

## Custom Ping Indicator Prefab

Create a custom indicator:

1. Create a prefab with:
   - `EOSPingIndicator` component
   - TextMesh for label
   - SpriteRenderer for icon
   - LineRenderer for ground line (optional)

2. Assign to EOSPingVisualizer:
```csharp
visualizer.PingIndicatorPrefab = myCustomPrefab;
```

### Indicator Settings

```csharp
// On EOSPingIndicator component:
[SerializeField] private TextMesh _labelText;
[SerializeField] private SpriteRenderer _iconRenderer;
[SerializeField] private LineRenderer _lineRenderer;
[SerializeField] private float _fadeStartTime = 1f;  // Fade last 1s
[SerializeField] private float _bobSpeed = 2f;        // Bob animation
[SerializeField] private float _bobAmount = 0.2f;     // Bob height
```

## Best Practices

1. **Use appropriate types** - Enemy pings for enemies, item pings for loot
2. **Set team properly** - Call `SetTeam()` when player joins a team
3. **Don't spam** - Cooldown and limits prevent abuse
4. **Add sounds** - Play distinct sounds for different ping types
5. **Minimap integration** - Show pings on your minimap

## Use Cases

### Tactical Shooter

```csharp
// Quick enemy ping on aim
if (Input.GetKeyDown(KeyCode.Z))
{
    pings.PingAtCrosshair(Camera.main, 200f, PingType.Enemy);
}
```

### Battle Royale

```csharp
// Ping loot for squadmates
void OnLootSpotted(GameObject loot)
{
    string name = loot.GetComponent<LootItem>().DisplayName;
    pings.PingPosition(loot.transform.position, PingType.Item, name);
}
```

### MOBA

```csharp
// Danger ping jungle
pings.PingDanger(junglePosition, "Missing mid!");

// Attack ping
pings.PingPosition(towerPosition, PingType.Attack);
```
