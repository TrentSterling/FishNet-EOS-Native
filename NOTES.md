# NOTES.md

Hard-won lessons, gotchas, and community insights for FishNet-EOS-Native.

---

## FishNet Internals

### SyncVar Access - Reflection Required

FishNet's `_syncTypes` is **private** with no public accessor:

```csharp
// NetworkBehaviour.SyncTypes.cs line 63
private Dictionary<uint, SyncBase> _syncTypes { get; }
```

**There is no way to iterate SyncVars without reflection.** Our `HostMigratable` uses:

```csharp
var fields = behaviour.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
foreach (var field in fields)
{
    if (field.FieldType.IsGenericType &&
        field.FieldType.GetGenericTypeDefinition() == typeof(SyncVar<>))
    {
        var syncVarInstance = field.GetValue(behaviour);
        var valueProp = field.FieldType.GetProperty("Value");
        // ...
    }
}
```

**Performance:** Fine for typical use. Would need 1000s of SyncVars to see issues, at which point FishNet itself would be the bottleneck.

**Alternatives:**
- Manual registration (users register values explicitly)
- Request FishNet expose `_syncTypes` publicly
- IL Weaving (complex, not worth it)

---

## EOS Lobby & Presence

### Member Disconnect Timeout - NOT Configurable

From `LobbyMemberStatus.cs`:
```csharp
Left = 1,         // User explicitly left - INSTANT
Disconnected = 2, // User unexpectedly left - ~30 SECOND TIMEOUT
Kicked = 3,       // User kicked - INSTANT
Promoted = 4,     // User promoted to owner
Closed = 5,       // Lobby closed
```

**The timeout for `Disconnected` is controlled by EOS backend infrastructure and cannot be configured.**

EOS uses exponential backoff for presence:
- First check: 8 seconds
- Second check: 4 seconds
- Third check: 2 seconds
- Then kicks

Total: ~15-30 seconds before `Disconnected` fires.

### Quest-Specific Issues (from Discord - Knot)

**Problem:** `OnApplicationQuit` doesn't fire reliably on Meta Quest. Host migration doesn't work when host quits the game (as opposed to using leave button).

**Workarounds:**
1. Fire `LeaveLobby()` on `OnApplicationPause(true)`
2. Fire `LeaveLobby()` on `OnApplicationFocus(false)`
3. Hook Unity's save callback on Quest (not `Application.wantsToQuit`)
4. Implement custom P2P heartbeat - detect disconnects faster than EOS

**Code example:**
```csharp
private void OnApplicationPause(bool pauseStatus)
{
    if (pauseStatus && EOSLobbyManager.Instance?.IsInLobby == true)
    {
        // Quest is being suspended - leave lobby immediately
        _ = EOSLobbyManager.Instance.LeaveLobbyAsync();
    }
}

private void OnApplicationFocus(bool hasFocus)
{
    if (!hasFocus && EOSLobbyManager.Instance?.IsInLobby == true)
    {
        // Lost focus - may be closing, leave lobby
        _ = EOSLobbyManager.Instance.LeaveLobbyAsync();
    }
}
```

### Host Migration Flag Naming

The EOS flag is confusingly named:
```csharp
// To ENABLE host migration, set DisableHostMigration to FALSE
createOptions.DisableHostMigration = false;  // Migration ENABLED
createOptions.DisableHostMigration = true;   // Migration DISABLED
```

---

## Host Migration

### Object Pooling Hack for Seamless Migration

FishNet limitation: Objects briefly disappear when host leaves (destroyed then recreated).

**Workaround:** Pool objects without `SetActive(false)`, freeze in place during migration.

```csharp
// Instead of normal pooling:
// obj.SetActive(false);

// Do this during migration:
// 1. Keep object active but freeze
rb.isKinematic = true;
rb.velocity = Vector3.zero;

// 2. Disable NetworkObject temporarily
// 3. Wait for new host to respawn
// 4. Match old object to new spawned object
// 5. Transfer visual state, destroy old
```

### SyncVar Caching Timing

**Problem:** FishNet clears SyncVars BEFORE `OnDisable` fires during disconnect.

**Solution:** Cache SyncVar values continuously in `Update()`, not just when disconnect happens.

```csharp
private void Update()
{
    // Cache state EVERY FRAME while object is valid
    if (!string.IsNullOrEmpty(_cachedOwnerPuid))
    {
        _cachedState = CaptureCurrentState();  // Has valid SyncVars
    }
}

private void OnDisable()
{
    // SyncVars may be zeroed here!
    // Use _cachedState instead of reading SyncVars directly
    SaveState(_cachedState);  // Safe - captured before FishNet cleared them
}
```

---

## P2P Networking

### Packet Size Limits

| Constant | Value | Notes |
|----------|-------|-------|
| Max Packet | 1170 bytes | EOS P2P limit |
| Header | 7 bytes | Channel + metadata |
| Usable | 1163 bytes | For payload |

Use `PacketFragmenter` for larger messages.

### Connection Timeout

Default EOS P2P connection timeout: **25 seconds**

For faster disconnect detection, implement custom heartbeat alongside EOS.

---

## Platform-Specific

### Windows Editor

Dynamic DLL loading required:
```csharp
[DllImport("Kernel32.dll")]
private static extern IntPtr LoadLibrary(string lpLibFileName);
```

### Android/Quest

- Load `.so` via `AndroidJavaClass`
- Different path for Unity 6+
- `OnApplicationQuit` unreliable - use `OnApplicationPause`

### DLL Configuration

Must configure x86/x64 DLLs in Inspector for correct platform targeting.

---

## API Design Lessons

### Unified Options Pattern

Instead of separate `CreateOptions` and `SearchOptions`, use unified `LobbyOptions`:
- Same fields work for both operations
- Implicit conversion operators handle type differences
- Cleaner API: one class, multiple uses

```csharp
var options = new LobbyOptions { GameMode = "dm", Region = "us-east" };
await transport.HostLobbyAsync(options);      // Creates with these settings
await transport.QuickMatchOrHostAsync(options); // Searches then creates if needed
```

### Double-Negative Booleans

Avoid `DisableX = false` patterns. Use positive naming:
- Bad: `DisableHostMigration = false`
- Good: `AllowHostMigration = true`

---

## Community Insights (Discord)

### From Knot (Fish Discord)

- Quest host migration requires `OnApplicationPause` handling
- Reflection for SyncVars is acceptable for typical use cases
- Goal: Match Photon's host migration UX

### From Duck (Various)

- Unified LobbyOptions API is cleaner than separate classes
- Auto-migration by default (opt-out with `DoNotMigrate`) is better UX
- QuickMatch should accept search filters
- Lobby codes should be flexible (not just 4 digits)

---

## Performance Notes

### Reflection Overhead

SyncVar reflection in `HostMigratable.Update()`:
- ~0.01ms per object with 5-10 SyncVars
- Acceptable for 100s of objects
- Would need 1000s to become bottleneck
- FishNet's own sync would lag first

### Struct Caching

`MigratableObjectState` is a struct - stack allocated, no GC pressure.
Dictionary of SyncVar values does allocate, but only during migration (not every frame).
