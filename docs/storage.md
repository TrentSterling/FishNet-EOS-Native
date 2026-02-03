# Cloud Storage

EOS provides two storage systems: Player Data Storage (per-player) and Title Storage (shared).

## Player Data Storage

400MB per player for save data, settings, and other personal data.

### Writing Data

```csharp
var storage = EOSPlayerDataStorage.Instance;

// Write string data
await storage.WriteFileAsync("save.json", jsonString);

// Write binary data
await storage.WriteFileAsync("settings.dat", bytes);
```

### Reading Data

```csharp
// Read as string
string json = await storage.ReadFileAsStringAsync("save.json");

// Read as bytes
byte[] data = await storage.ReadFileAsBytesAsync("settings.dat");
```

### Listing Files

```csharp
var files = await storage.GetFileListAsync();

foreach (var file in files)
{
    Debug.Log($"{file.Filename} - {file.Size} bytes");
}
```

### Deleting Files

```csharp
await storage.DeleteFileAsync("old_save.json");
```

### File Metadata

```csharp
var metadata = await storage.GetFileMetadataAsync("save.json");

Debug.Log($"Size: {metadata.Size}");
Debug.Log($"MD5: {metadata.MD5Hash}");
Debug.Log($"Last modified: {metadata.LastModified}");
```

## Title Storage

Read-only storage for game data that all players can access. Content is uploaded through the EOS Developer Portal.

### Reading Title Data

```csharp
var titleStorage = EOSTitleStorage.Instance;

// Read file
string config = await titleStorage.ReadFileAsStringAsync("config.json");
byte[] levelData = await titleStorage.ReadFileAsBytesAsync("level1.dat");
```

### Listing Title Files

```csharp
var files = await titleStorage.GetFileListAsync();
```

### Use Cases

- Game configuration
- Level data
- Localization files
- Shared assets
- Patch notes

## Caching

Both storage systems cache files locally:

```csharp
// Clear local cache
await storage.ClearCacheAsync();

// Force refresh from cloud
string data = await storage.ReadFileAsStringAsync("save.json", forceRefresh: true);
```

## Progress Tracking

For large files, track transfer progress:

```csharp
storage.OnTransferProgress += (filename, bytesTransferred, totalBytes) =>
{
    float percent = (float)bytesTransferred / totalBytes;
    Debug.Log($"{filename}: {percent:P0}");
};
```

## Error Handling

```csharp
try
{
    var data = await storage.ReadFileAsStringAsync("save.json");
}
catch (EOSStorageException ex) when (ex.Result == Result.NotFound)
{
    Debug.Log("File doesn't exist");
}
catch (EOSStorageException ex)
{
    Debug.LogError($"Storage error: {ex.Result}");
}
```

## Events

```csharp
storage.OnFileWritten += (filename) => { };
storage.OnFileDeleted += (filename) => { };
storage.OnTransferProgress += (filename, transferred, total) => { };
storage.OnError += (filename, result) => { };
```

## Storage Limits

### Player Data Storage

| Limit | Value |
|-------|-------|
| Total storage | 400 MB per player |
| Max file size | 200 MB |
| Max files | No limit |

### Title Storage

| Limit | Value |
|-------|-------|
| Total storage | Varies by plan |
| Max file size | 200 MB |
| Managed via | Developer Portal |

## Integration Examples

### Saving Game Progress

```csharp
public class SaveManager : MonoBehaviour
{
    public async Task SaveGameAsync(GameState state)
    {
        string json = JsonUtility.ToJson(state);
        await EOSPlayerDataStorage.Instance.WriteFileAsync("game_save.json", json);
    }

    public async Task<GameState> LoadGameAsync()
    {
        try
        {
            string json = await EOSPlayerDataStorage.Instance.ReadFileAsStringAsync("game_save.json");
            return JsonUtility.FromJson<GameState>(json);
        }
        catch
        {
            return new GameState();  // Default state
        }
    }
}
```

### Loading Title Config

```csharp
public class ConfigManager : MonoBehaviour
{
    private async void Start()
    {
        string configJson = await EOSTitleStorage.Instance.ReadFileAsStringAsync("game_config.json");
        GameConfig.Instance.Load(configJson);
    }
}
```
