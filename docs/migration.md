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

## Making Objects Migratable

Add the `HostMigratable` component to objects that need special handling:

```csharp
[RequireComponent(typeof(NetworkObject))]
public class MyGameObject : NetworkBehaviour
{
    private HostMigratable _migratable;

    private void Awake()
    {
        _migratable = GetComponent<HostMigratable>();
        _migratable.OnMigrationStarted += HandleMigrationStarted;
        _migratable.OnMigrationCompleted += HandleMigrationCompleted;
    }

    private void HandleMigrationStarted()
    {
        // Save critical state
    }

    private void HandleMigrationCompleted()
    {
        // Restore or reinitialize
    }
}
```

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
