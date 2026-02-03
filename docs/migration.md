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

## Migration Events (Important!)

These callbacks are **critical** for a smooth player experience. Always handle them in your game code.

### Available Events

| Event | When It Fires | Use For |
|-------|---------------|---------|
| `OnMigrationStarted` | Host disconnect detected | Pause game, show UI, disable input |
| `OnMigrationCompleted` | New host ready, game resuming | Hide UI, resume game, re-enable input |
| `OnMigrationFailed` | Migration couldn't complete | Show error, return to menu |

### Basic Setup

```csharp
var migration = HostMigrationManager.Instance;

migration.OnMigrationStarted += OnMigrationStarted;
migration.OnMigrationCompleted += OnMigrationCompleted;
migration.OnMigrationFailed += OnMigrationFailed;

void OnMigrationStarted()
{
    Debug.Log("Migration started - host disconnected");
}

void OnMigrationCompleted()
{
    Debug.Log("Migration complete - game resuming");
}

void OnMigrationFailed(string reason)
{
    Debug.LogError($"Migration failed: {reason}");
}
```

### Use Case: Pause Gameplay During Migration

```csharp
public class GameManager : MonoBehaviour
{
    [SerializeField] private GameObject migrationOverlay;

    private void Start()
    {
        var migration = HostMigrationManager.Instance;
        migration.OnMigrationStarted += PauseGame;
        migration.OnMigrationCompleted += ResumeGame;
        migration.OnMigrationFailed += HandleMigrationFailure;
    }

    private void PauseGame()
    {
        // Freeze gameplay
        Time.timeScale = 0f;

        // Show migration UI
        migrationOverlay.SetActive(true);

        // Disable player input
        PlayerInput.DisableAll();
    }

    private void ResumeGame()
    {
        // Resume gameplay
        Time.timeScale = 1f;

        // Hide migration UI
        migrationOverlay.SetActive(false);

        // Re-enable player input
        PlayerInput.EnableAll();
    }

    private void HandleMigrationFailure(string reason)
    {
        Time.timeScale = 1f;
        migrationOverlay.SetActive(false);

        // Show error and return to menu
        ErrorUI.Show($"Connection lost: {reason}");
        SceneManager.LoadScene("MainMenu");
    }
}
```

### Use Case: Save Custom State Before Migration

```csharp
public class MatchManager : MonoBehaviour
{
    private int _currentRound;
    private float _matchTimer;
    private Dictionary<string, int> _playerScores = new();

    private void Start()
    {
        var migration = HostMigrationManager.Instance;
        migration.OnMigrationStarted += SaveMatchState;
        migration.OnMigrationCompleted += RestoreMatchState;
    }

    private void SaveMatchState()
    {
        // Save to lobby attributes (survives migration)
        var lobby = EOSLobbyManager.Instance;
        lobby.SetAttribute("round", _currentRound);
        lobby.SetAttribute("timer", _matchTimer);
        lobby.SetAttribute("scores", JsonUtility.ToJson(_playerScores));
    }

    private void RestoreMatchState()
    {
        // Restore from lobby attributes
        var lobby = EOSLobbyManager.Instance;
        _currentRound = lobby.GetAttribute<int>("round");
        _matchTimer = lobby.GetAttribute<float>("timer");

        var scoresJson = lobby.GetAttribute<string>("scores");
        if (!string.IsNullOrEmpty(scoresJson))
            _playerScores = JsonUtility.FromJson<Dictionary<string, int>>(scoresJson);
    }
}
```

### Use Case: Countdown Timer UI

```csharp
public class MigrationUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text countdownText;

    private float _migrationStartTime;
    private bool _isMigrating;

    private void Start()
    {
        var migration = HostMigrationManager.Instance;
        migration.OnMigrationStarted += ShowMigrationUI;
        migration.OnMigrationCompleted += HideMigrationUI;
    }

    private void ShowMigrationUI()
    {
        panel.SetActive(true);
        statusText.text = "Host disconnected\nFinding new host...";
        _migrationStartTime = Time.unscaledTime;
        _isMigrating = true;
    }

    private void HideMigrationUI()
    {
        panel.SetActive(false);
        _isMigrating = false;
    }

    private void Update()
    {
        if (!_isMigrating) return;

        float elapsed = Time.unscaledTime - _migrationStartTime;
        float timeout = HostMigrationManager.Instance.MigrationTimeout;
        float remaining = timeout - elapsed;

        countdownText.text = $"Timeout in {remaining:F0}s";
    }
}
```

### Use Case: Audio Handling

```csharp
public class AudioManager : MonoBehaviour
{
    private void Start()
    {
        var migration = HostMigrationManager.Instance;
        migration.OnMigrationStarted += OnMigrationStarted;
        migration.OnMigrationCompleted += OnMigrationCompleted;
    }

    private void OnMigrationStarted()
    {
        // Mute game audio but keep voice chat
        AudioListener.volume = 0f;

        // Play migration sound effect
        PlaySound("host_disconnected");
    }

    private void OnMigrationCompleted()
    {
        // Restore game audio
        AudioListener.volume = 1f;

        // Play reconnected sound
        PlaySound("reconnected");
    }
}
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
