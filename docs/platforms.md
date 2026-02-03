# Platform Detection

Players share their platform when joining lobbies, enabling cross-platform awareness.

## Current Platform

```csharp
// Get the current platform
EOSPlatformType platform = EOSPlatformHelper.CurrentPlatform;

// Get platform ID string
string platformId = EOSPlatformHelper.PlatformId;
// Returns: "WIN", "MAC", "LNX", "AND", "IOS", "QST", etc.
```

## Platform Checks

```csharp
// Platform category checks
if (EOSPlatformHelper.IsMobile)
{
    // Android or iOS
}

if (EOSPlatformHelper.IsVR)
{
    // Quest or other VR headset
}

if (EOSPlatformHelper.IsDesktop)
{
    // Windows, Mac, or Linux
}

if (EOSPlatformHelper.SupportsVoice)
{
    // Platform has voice chat capability
}
```

## Player Platform Info

```csharp
var registry = EOSPlayerRegistry.Instance;

// Get a player's platform
string playerPlatform = registry.GetPlatform(puid);

// Get display icon
string icon = EOSPlayerRegistry.GetPlatformIcon(playerPlatform);
// Returns: 🖥️ (desktop), 📱 (mobile), 👓 (VR)

// Get display name
string name = EOSPlayerRegistry.GetPlatformName(playerPlatform);
// Returns: "Windows", "Android", "Quest", etc.
```

## Platform IDs

| Platform | ID | Icon | Name |
|----------|-----|------|------|
| Windows | WIN | 🖥️ | Windows |
| macOS | MAC | 🖥️ | Mac |
| Linux | LNX | 🖥️ | Linux |
| Android | AND | 📱 | Android |
| iOS | IOS | 📱 | iOS |
| Quest | QST | 👓 | Quest |
| PlayStation | PS | 🎮 | PlayStation |
| Xbox | XBX | 🎮 | Xbox |
| Switch | NSW | 🎮 | Switch |

## UI Integration

Platform icons appear in:
- Recently Played list
- Local Friends list
- Lobby member list
- Spectator target list

## Cross-Platform Considerations

### Input Differences

```csharp
if (EOSPlatformHelper.IsMobile || EOSPlatformHelper.IsVR)
{
    // Show touch/VR-friendly UI
    EnableLargeButtons();
}
else
{
    // Show keyboard/mouse UI
    EnableStandardUI();
}
```

### Performance Tiers

```csharp
if (EOSPlatformHelper.IsMobile)
{
    // Lower quality settings
    QualitySettings.SetQualityLevel(0);
}
```

### Feature Availability

Some features may not be available on all platforms:

```csharp
// Anti-cheat is desktop only
if (EOSPlatformHelper.IsDesktop)
{
    EOSAntiCheatManager.Instance.BeginSession();
}

// Voice is available on all platforms
if (EOSPlatformHelper.SupportsVoice)
{
    EnableVoiceUI();
}
```

## Events

```csharp
// Called when a player's platform is detected
registry.OnPlayerPlatformDetected += (puid, platform) =>
{
    Debug.Log($"Player {puid} is on {platform}");
};
```
