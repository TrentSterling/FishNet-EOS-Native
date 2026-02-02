# ARCHIVE.md

Historical documentation and reference material for FishNet-EOS-Native.

---

## Reference Projects & Inspiration

This transport was built by studying these excellent open-source projects. We learned from their patterns, then implemented our own code from scratch using the raw EOS C# SDK.

### Core Transport Architecture

| Project | Author | What We Learned | Link |
|---------|--------|-----------------|------|
| **EOSTransport** | FakeByte | Raw SDK init, packet fragmentation, Editor DLL loading, Android `.so` loading | [GitHub](https://github.com/FakeByte/EpicOnlineTransport) |
| **FishyEOS** | ETdoFresh | FishNet transport base, Server/Client peer pattern, ClientHost handling | [GitHub](https://github.com/ETdoFresh/FishyEOS) |
| **PurrNet EOS** | CytoidCommunity | Clean 4-file architecture, minimal transport design | [GitHub](https://github.com/CytoidCommunity/PurrNet) |

### Advanced Features

| Project | Author | What We Learned | Link |
|---------|--------|-----------------|------|
| **PlayEveryWare Plugin** | PlayEveryWare | NotifyEventHandle auto-cleanup, connection state machine | [GitHub](https://github.com/PlayEveryWare/eos_plugin_for_unity) |
| **Host Migration Gist** | (Anonymous) | FishNet-specific migration, SyncVar persistence, object tracking | [Gist](https://gist.github.com/anonymous/077d1bba823fa6b29bd7f1b67dcd9781) |
| **FishNetEosVivoxLobbyTest** | Roceh | 3D spatialized voice patterns, lobby-based RTC | [GitHub](https://github.com/Roceh/FishNetEosVivoxLobbyTest) |

### Official Resources

| Resource | Description | Link |
|----------|-------------|------|
| **EOS C# SDK** | Official Epic Online Services SDK | [Developer Portal](https://dev.epicgames.com/docs/epic-online-services) |
| **FishNet** | Networking library by FirstGearGames | [GitHub](https://github.com/FirstGearGames/FishNet) |

---

## Raw SDK Setup Guide

### SDK Initialization Code

```csharp
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;

private Epic.OnlineServices.Result InitializePlatformInterface()
{
    var initOptions = new InitializeOptions
    {
        ProductName = "YourProductName",
        ProductVersion = "1.0",
        AllocateMemoryFunction = IntPtr.Zero,
        ReallocateMemoryFunction = IntPtr.Zero,
        ReleaseMemoryFunction = IntPtr.Zero
    };

    var threadAffinity = new InitializeThreadAffinity
    {
        NetworkWork = 0, StorageIo = 0, WebSocketIo = 0,
        P2PIo = 0, HttpRequestIo = 0, RTCIo = 0
    };
    initOptions.OverrideThreadAffinity = threadAffinity;

    return PlatformInterface.Initialize(ref initOptions);
}

private PlatformInterface CreatePlatformInterface()
{
    var platformOptions = new WindowsOptions
    {
        ProductId = "YOUR_PRODUCT_ID",
        SandboxId = "YOUR_SANDBOX_ID",
        DeploymentId = "YOUR_DEPLOYMENT_ID",
        ClientCredentials = new ClientCredentials
        {
            ClientId = "YOUR_CLIENT_ID",
            ClientSecret = "YOUR_CLIENT_SECRET"
        },
        CacheDirectory = Application.temporaryCachePath,
        EncryptionKey = "YOUR_64_CHAR_HEX_KEY",
        TickBudgetInMilliseconds = 0,
        IsServer = false,
        Flags =
#if UNITY_EDITOR
            PlatformFlags.LoadingInEditor
#else
            PlatformFlags.None
#endif
    };
    return PlatformInterface.Create(ref platformOptions);
}
```

### Unity Editor Dynamic Loading (Windows)

```csharp
#if UNITY_EDITOR
[DllImport("Kernel32.dll")]
private static extern IntPtr LoadLibrary(string lpLibFileName);

[DllImport("Kernel32.dll")]
private static extern int FreeLibrary(IntPtr hLibModule);

[DllImport("Kernel32.dll")]
private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

private IntPtr m_LibraryPointer;

void Awake()
{
    var libraryPath = "Assets/SDK/Bin/x64/" + Config.LibraryName;
    m_LibraryPointer = LoadLibrary(libraryPath);
    Bindings.Hook(m_LibraryPointer, GetProcAddress);
}

void OnApplicationQuit()
{
    s_PlatformInterface?.Release();
    PlatformInterface.Shutdown();
    Bindings.Unhook();
    FreeLibrary(m_LibraryPointer);
}
#endif
```

### Auth vs Connect Interface

**Auth Interface** - Epic Account login:
- Used for: Friends, Presence, Store purchases
- Returns: `EpicAccountId`
- Login types: `AccountPortal`, `PersistentAuth`, `ExternalAuth`

**Connect Interface** - Game services login:
- Used for: P2P, Lobbies, Matchmaking, Leaderboards
- Returns: `ProductUserId` (PUID)
- Login types: `DeviceIdAccessToken` (anonymous), `Epic` (linked)

### Device Token Login Flow

```csharp
// Create device ID (first time only)
var createOptions = new CreateDeviceIdOptions() { DeviceModel = SystemInfo.deviceModel };
connectInterface.CreateDeviceId(ref createOptions, null, callback);

// Login with device token
var loginOptions = new LoginOptions()
{
    Credentials = new Credentials()
    {
        Type = ExternalCredentialType.DeviceidAccessToken,
        Token = null
    },
    UserLoginInfo = new UserLoginInfo() { DisplayName = "PlayerName" }
};
connectInterface.Login(ref loginOptions, null, callback);
```

### Android Setup

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
void Awake()
{
    using (AndroidJavaClass sys = new AndroidJavaClass("java.lang.System"))
    {
        sys.CallStatic("loadLibrary", "EOSSDK");
    }

    #if UNITY_6000_0_OR_NEWER
    using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK"))
    {
        eos.CallStatic("init", UnityEngine.Android.AndroidApplication.currentActivity);
    }
    #else
    AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
    AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
    using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK"))
    {
        eos.CallStatic("init", activity);
    }
    #endif
}
#endif
```

**Android checklist:**
- Copy `libEOSSDK.so` to `Assets/Plugins/Android/libs/arm64-v8a/`
- Copy EOS SDK `.aar` file if provided
- Ensure `AndroidManifest.xml` has internet permission
- May need ProGuard rules to prevent stripping

---

## Implementation Patterns (Historical)

Key patterns we learned from reference projects and adapted for FishNet-EOS-Native:

| Feature | Inspiration | Our Implementation |
|---------|-------------|-------------------|
| SDK Initialization | EOSTransport (FakeByte) | `EOSManager.cs` |
| Editor DLL Loading | EOSTransport (FakeByte) | `EOSManager.cs` |
| Android `.so` Loading | EOSTransport (FakeByte) | `EOSManager.cs` |
| Packet Fragmentation | EOSTransport (FakeByte) | `PacketFragmenter.cs` |
| FishNet Transport Base | FishyEOS (ETdoFresh) | `EOSNativeTransport.cs` |
| Server/Client Peers | FishyEOS + PurrNet | `EOSServer.cs`, `EOSClient.cs` |
| ClientHost Pattern | FishyEOS (ETdoFresh) | `EOSClientHost.cs` |
| Lobby Integration | Host Migration Gist | `EOSLobbyManager.cs` |
| Host Migration | Host Migration Gist | `HostMigrationManager.cs` |
| NotifyHandle Cleanup | PlayEveryWare | `NotifyEventHandle.cs` |
| 3D Spatial Voice | Roceh's VivoxLobbyTest | `EOSVoiceManager.cs`

---

## Development & Testing Patterns

### ParrelSync Clone Support

```csharp
var deviceModel = SystemInfo.deviceUniqueIdentifier;
#if UNITY_EDITOR
if (ClonesManager.IsClone())
{
    deviceModel += ClonesManager.GetCurrentProjectPath();
}
#endif

DeleteDeviceIdOptions deleteOptions = new DeleteDeviceIdOptions();
connectInterface.DeleteDeviceId(ref deleteOptions, null, null);

CreateDeviceIdOptions createOptions = new CreateDeviceIdOptions
{
    DeviceModel = deviceModel
};
```

### Async/Await Pattern for EOS Callbacks

```csharp
public async Task<Result> CreateDeviceIdAsync()
{
    var tcs = new TaskCompletionSource<Result>();
    var options = new CreateDeviceIdOptions { DeviceModel = GetDeviceModel() };
    connectInterface.CreateDeviceId(ref options, null,
        (ref CreateDeviceIdCallbackInfo data) => tcs.SetResult(data.ResultCode));
    return await tcs.Task;
}
```

---

## Build Commands

```bash
# Windows standalone build
"C:\Program Files\Unity\Hub\Editor\6000.0.65f1\Editor\Unity.exe" -batchmode -quit -projectPath . -buildWindows64Player Build/Game.exe

# Run tests
"C:\Program Files\Unity\Hub\Editor\6000.0.65f1\Editor\Unity.exe" -batchmode -quit -projectPath . -runTests -testPlatform PlayMode
```

---

## Host Migration Goals & Insights

**Target Performance:**
- <2 seconds total (PUN hangs ~1 second)
- With voice+chat staying up, migration looks like "server lagged"

**EOS Timeout Behavior:**
- Escalating ping checks: 5s → 3s → 1s → kick
- Hook host exit ASAP for recognized event

**Why EOS VoIP over Dissonance:**
- Dissonance dies when host dies, EOS VoIP stays up
- Less bandwidth, Discord-ish delay
- Voice surviving migration is huge for UX

**Object Handling Strategy:**
- Scene objects: reuse and reset (off then on)
- Instantiated prefabs: FishNet `PooledInstantiate` for recycling
- Goal: recycle instead of destroy-then-replace

**Player Experience:**
- Voice keeps going (lobby-based)
- Chat keeps going (lobby attributes)
- Brief "server lag" while FishNet reinits
- Migration happens ~once every 10 minutes max

---

## Critical Bug Fix (2026-01-29)

**EOS Channel vs Reliability Mapping**

The bug: All packets sent on EOS Channel 0 regardless of FishNet channel. Receiver mapped Channel 0 → Reliable. Since everything was Channel 0, ALL packets treated as Reliable, causing NetworkTransform (unreliable) to fail.

The fix: Use EOS Channel 0 for Reliable, Channel 1 for Unreliable:
```csharp
byte eosChannel = channel == Channel.Reliable ? (byte)0 : (byte)1;
```

---

## Feature API Examples

### Stats & Leaderboards

```csharp
var stats = EOSStats.Instance;
var leaderboards = EOSLeaderboards.Instance;

// Ingest stats
await stats.IngestStatAsync("kills", 5);
await stats.IngestStatsAsync(("score", 1500), ("playtime", 300));

// Query my stats
var (result, myStats) = await stats.QueryMyStatsAsync("kills", "deaths");

// Query leaderboard
var (result, entries) = await leaderboards.QueryRanksAsync("weekly_kills", maxResults: 10);
```

### Achievements

```csharp
var achievements = EOSAchievements.Instance;
await achievements.RefreshAsync();

if (achievements.IsUnlocked("first_kill")) { }
float progress = achievements.GetProgress("kill_100_enemies");
await achievements.UnlockAchievementAsync("first_kill");
```

### Cloud Storage

```csharp
var storage = EOSPlayerDataStorage.Instance;

await storage.WriteFileAsync("save.json", jsonContent);
var (result, content) = await storage.ReadFileAsStringAsync("save.json");
var (result, files) = await storage.QueryFileListAsync();
```

### Player Reporting

```csharp
var reports = EOSReports.Instance;
await reports.ReportPlayerAsync(targetPuid, PlayerReportsCategory.Cheating, "Using aimbot");
```

### Sanctions

```csharp
var sanctions = EOSSanctions.Instance;
bool isBanned = await sanctions.IsPlayerBannedAsync(targetPuid);
```

### Friends & Presence

```csharp
// PUID-based (DeviceID)
var registry = EOSPlayerRegistry.Instance;
var knownPlayers = registry.GetAllKnownPlayers();

// Epic Account (requires Epic login)
var friends = EOSFriends.Instance;
await friends.QueryFriendsAsync();
```

### Custom Invites

```csharp
var invites = EOSCustomInvites.Instance;
invites.SetLobbyPayload();
await invites.SendInviteAsync(recipientPuid);
```

---

## Completed Features History

### v0.1.0 - v0.5.0 (Foundation)
- EOS SDK Init, Device Token Login, Auto-Setup
- Transport Base, Server/Client, P2P Connection
- Lobby System, Remote Join, Packet Fragmentation
- Fast Disconnect, Stats Debugger

### v0.6.0 (Voice, Chat, Migration)
- Voice/RTC integration with 3D spatial
- Text Chat via lobby attributes
- Host Migration Framework

### v0.7.0 (Social Features)
- Friends, Presence, UserInfo, CustomInvites
- Cloud Storage, Reports, Sanctions
- Stats, Leaderboards, Achievements, Metrics

### v0.8.0 (Polish & Physics)
- Setup Wizard, Tools Menu
- NetworkPhysicsObject, PlayerBall improvements
- Exit playmode fixes, hierarchy cleanup

---

## Ownership Swapping Research

### FishNet PredictedOwner Component

FishNet has built-in `PredictedOwner` for instant ownership claiming:
- **Docs:** https://fish-networking.gitbook.io/docs/fishnet-building-blocks/components/prediction/predictedowner
- `TakeOwnership()` - Client immediately simulates ownership
- `PreviousOwner` - Access previous owner for rollback
- Override `OnTakeOwnership()` - Server accept/reject

### From NGO XR Sample

Key concepts from Unity's Netcode for GameObjects XR sample (`NetworkPhysicsInteractable_NGO.cs`):
- `IsMovingFaster()` - Only steal if your velocity > target velocity
- `m_MinExchangeVelocityMagnitude` - Minimum threshold
- `CheckOwnershipRoutine()` - RTT-aware timeout
- Disable NetworkTransform during ownership request
- Track average velocity over 3 frames

### Implementation TODO

1. Replace `NetworkPhysicsObject` with `PredictedOwner`-based solution
2. Add velocity tracking over 3 frames
3. Velocity-based priority (faster wins)
4. RTT-aware timeout for NetworkTransform re-enable
