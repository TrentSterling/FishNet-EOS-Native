# Host Migration

Seamless handoff when the host disconnects.

## How It Works

```
1. Host disconnects
2. EOS promotes new lobby owner
3. New host detected by HostMigrationManager
4. Scene objects reset to initial state
5. Players re-establish P2P connections
6. NetworkObjects repossessed
7. Game continues
```

## What Survives Migration

| Component | Survives | Notes |
|-----------|----------|-------|
| Lobby | Yes | EOS manages ownership transfer |
| Voice/RTC | Yes | Stays connected throughout |
| Chat history | Yes | Stored in lobby attributes |
| Player list | Yes | Lobby membership persists |
| NetworkObjects | Recreated | Repossessed by new host |
| Game state | Partial | Scene objects reset |

## Automatic Migration (Default)

**All NetworkObjects are automatically migrated by default.** No setup required - spawned objects are tracked and restored during migration.

```csharp
// This object will automatically migrate - no extra components needed!
public class MyGameObject : NetworkBehaviour
{
    public readonly SyncVar<int> Health = new();
    public readonly SyncVar<string> PlayerName = new();

    // SyncVars are automatically saved and restored
}
```

## Excluding Objects from Migration (Recommended)

Add the `DoNotMigrate` component to exclude specific objects:

```csharp
// In Inspector: Add Component > FishNet > EOS Native > Do Not Migrate

// Or via code:
gameObject.AddComponent<DoNotMigrate>();
```

Use `DoNotMigrate` for:
- Temporary visual effects
- Projectiles mid-flight
- Objects that should reset on host change
- Intentionally transient objects

> **Tip:** `DoNotMigrate` is the simplest way to exclude objects. Use `HostMigratable` only if you need custom SyncVar caching or migration detection.

## Advanced: HostMigratable Component (Optional)

For objects that need custom migration handling, add the `HostMigratable` component:

```csharp
[RequireComponent(typeof(NetworkObject))]
public class MyGameObject : NetworkBehaviour
{
    private HostMigratable _migratable;

    private void Awake()
    {
        _migratable = GetComponent<HostMigratable>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Check if this is a migration restore vs normal spawn
        if (_migratable.LoadState.HasValue)
        {
            // Migration restore - state already loaded
            Debug.Log("Restored from migration");
        }
        else
        {
            // Normal spawn - initialize fresh
            Debug.Log("Fresh spawn");
        }
    }
}
```

`HostMigratable` provides:
- Automatic SyncVar caching (handles FishNet clearing SyncVars on disconnect)
- `LoadState` property to detect migration spawns
- Owner PUID tracking for repossession

## Migration Events

```csharp
var migration = HostMigrationManager.Instance;

migration.OnMigrationStarted += () =>
{
    // Show "Host migrating..." UI
};

migration.OnMigrationCompleted += (newHostPuid) =>
{
    // Hide migration UI
    Debug.Log($"New host: {newHostPuid}");
};

migration.OnMigrationFailed += (reason) =>
{
    // Handle failure (rare)
};
```

## Player Spawner Integration

The `HostMigrationPlayerSpawner` handles respawning players after migration:

```csharp
public class MyPlayerSpawner : HostMigrationPlayerSpawner
{
    protected override void RespawnPlayer(NetworkConnection conn, string puid)
    {
        // Custom respawn logic
        var player = Instantiate(playerPrefab);
        ServerManager.Spawn(player, conn);
    }
}
```

## Testing Migration

Use the `HostMigrationTester` component for runtime testing:

1. Add `HostMigrationTester` to a GameObject
2. Enter Play Mode with multiple clients (ParrelSync)
3. Press the test button or call:

```csharp
var tester = HostMigrationTester.Instance;
tester.SimulateHostDisconnect();
```

### Verification Checklist

The tester provides a checklist:
- [ ] New host elected
- [ ] P2P connections re-established
- [ ] Players repossessed
- [ ] Voice chat working
- [ ] Chat history intact

## Best Practices

### Do

- Save critical game state in lobby attributes
- Use `HostMigratable` for important objects
- Test migration frequently during development
- Handle `OnMigrationStarted` to pause gameplay

### Don't

- Store game state only on the host
- Assume NetworkObject IDs persist
- Ignore migration events in UI code

## Configuration

```csharp
var migration = HostMigrationManager.Instance;

// Timeout for migration process
migration.MigrationTimeout = 10f;

// Auto-elect new host (default true)
migration.AutoElectNewHost = true;
```

## Troubleshooting

### Players Not Reconnecting

Ensure all clients have matching `SocketId.SocketName` for P2P.

### State Lost After Migration

Store state in lobby attributes or implement `HostMigratable` save/restore.

### Voice Drops During Migration

This shouldn't happen - voice uses a separate RTC channel. Check F3 panel.
