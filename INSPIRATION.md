# Inspiration & Sources

> "Claude eats dog food and shits gold" - Trent, 2026

This project exists because someone spent 2 years collecting notes, snippets, and prototypes trying to make EOS easier in Unity. This document credits everyone whose work made FishNet-EOS-Native possible.

---

## The Problem

Epic provides a C# SDK that's pretty raw. Then there's PlayEveryWare's wrapper, which is bloated but has tons of good samples. Then there's FishyEOS for FishNet, which isn't super well maintained and doesn't build with latest FishNet or Unity.

**The goal:** Take the best parts from everything, throw it in a robot blender, and create something that "just works."

---

## Why EOS?

Epic Online Services is genuinely awesome:

- **Free multiplayer** - No costs, ever
- **Infinite scaling** - Fortnite-sized without paying a dime
- **16 simultaneous lobbies** - Per user!
- **Player data storage** - Nice limits for cloud saves
- **Relay servers worldwide** - NAT traversal just works
- **No risk** - Game fails? Pay nothing. Game succeeds? Still pay nothing.

That's a fucking good service right there.

---

## Primary Sources

### 1. EOS-SDK-CSharp (Official Epic SDK) ⭐
[Epic Developer Portal](https://dev.epicgames.com/docs/epic-online-services)

**What it is:** The raw Epic Online Services C# SDK

**What we took:**
- All the P2P interface bindings
- Lobby interface bindings
- Connect interface for device token auth
- The actual SDK DLLs

**Credit:** [Epic Games](https://dev.epicgames.com)

---

### 2. EOSTransport for Mirror ⭐
[GitHub: CodedImmersions/EOSTransport](https://github.com/CodedImmersions/EOSTransport)

**What it is:** Mirror networking transport using raw EOS SDK

**The lineage (important!):**
1. **[FakeByte](https://github.com/FakeByte/EpicOnlineTransport)** - Original creator (2020)
2. **[Katone/WeLoveJesusChrist](https://github.com/WeLoveJesusChrist/EOSTransport)** - Took over when FakeByte left Mirror
3. **[CodedImmersions](https://github.com/CodedImmersions/EOSTransport)** - Current maintainer

**What we took:**
- `EOSManager.cs` - Raw SDK initialization without PlayEveryWare
- `Packet.cs` - Packet fragmentation for >1170 byte messages (7-byte header)
- Android `.so` loading patterns
- Editor DLL loading with LoadLibrary/Bindings.Hook

**Credit:** FakeByte (original), Katone (continuation), [CodedImmersions](https://github.com/CodedImmersions) (current)

---

### 3. FishyEOS (Original FishNet Transport) ⭐
[GitHub: ETdoFresh/FishyEOS](https://github.com/ETdoFresh/FishyEOS)

**What it is:** The original EOS transport for FishNet (uses PlayEveryWare)

**What we took:**
- FishNet Transport architecture patterns
- `ServerPeer.cs` / `ClientPeer.cs` structure
- `ClientHostPeer.cs` - The host-as-client pattern (ID 32767)
- Connection state management

**What we avoided:**
- PlayEveryWare dependency
- Complex coroutine patterns

**Credit:** [ETdoFresh](https://github.com/ETdoFresh/FishyEOS) & [FirstGearGames](https://github.com/FirstGearGames)

---

### 4. PurrNet EOS Transport ⭐
[GitHub: quentinleon/PurrNetEOSTransport](https://github.com/quentinleon/PurrNetEOSTransport)

**What it is:** Minimal 4-file transport - cleanest architecture

**What we took:**
- Simple file structure (Transport, Server, Client, Common)
- Clean `Dictionary<int, string>` for connectionId <-> ProductUserId mapping
- Straightforward Send/Receive patterns
- **ParrelSync unique device ID snippet!**

```csharp
// qwolf's ParrelSync fix
if (ClonesManager.IsClone())
    deviceModel += ClonesManager.GetCurrentProjectPath();
```

**What it's missing (that we added):**
- ClientHostPeer
- Packet fragmentation
- Auth/login handling
- Lobbies

**Credit:** [Quentin Leon (qwolf)](https://github.com/quentinleon)

---

### 5. FishNetEosVivoxLobbyTest (Tank Demo) ⭐⭐⭐
[GitHub: Roceh/FishNetEosVivoxLobbyTest](https://github.com/Roceh/FishNetEosVivoxLobbyTest/tree/EOSVoiceOnly)

**What it is:** Complete demo with FishNet, EOS lobbies, and **3D spatialized voice chat**

**What we took:**
- Manual audio output pattern for EOS RTC
- Per-player `AudioSource` switching for 3D spatialization
- `PlayerInfo.cs` audio frame buffering with `ConcurrentQueue<short[]>`
- `PlayerTank.cs` pattern: lobby = flat audio, in-game = positional audio

**The gold:**
```csharp
// Lobby: flat audio
// In-game: switch to 3D AudioSource on player object
playerInfo.SwitchAudioSource(GetComponent<AudioSource>());
```

**Credit:** [Roceh](https://github.com/Roceh)

**Also check out Roceh's other FishNet gems:**
- [RVPFishNet-Multiplayer-Car-Controller](https://github.com/Roceh/RVPFishNet-Multiplayer-Car-Controller) - Vehicle physics with client prediction
- [FishNet-Car-Controller-Prediction-Test](https://github.com/Roceh/FishNet---Car-Controller-Prediction-Test) - Car controller examples
- [UnityCustomRaycastVehicle](https://github.com/Roceh/UnityCustomRaycastVehicle) - SphereCast vehicle library

---

### 6. PlayEveryWare EOS Plugin
[GitHub: PlayEveryWare/eos_plugin_for_unity](https://github.com/PlayEveryWare/eos_plugin_for_unity)

**What it is:** The most feature-complete EOS Unity integration (247 C# files, 1,999-line EOSManager)

**What we took:**
- `NotifyEventHandle.cs` - Auto-cleanup pattern for EOS notification handles
- Connection state machine patterns from P2P samples
- Lobby/Member data class structures

**What we avoided:**
- The bloat (47 editor files, complex config system)
- Integrated platform management
- RTC overlay features

**Credit:** [PlayEveryWare](https://github.com/PlayEveryWare/eos_plugin_for_unity)

---

### 7. Host Migration Gist (Trent's Own Code!) ⭐⭐

**What it is:** FishNet-specific host migration patterns written by the project author

**What we took:**
- `FishyEOSLobbyConnector.cs` - Lobby create/join/search patterns
- `HostMigratable.cs` - NetworkBehaviour with SyncVar persistence
- `HostMigrationObjectTracker.cs` - Tracks objects for state save/restore
- `HostMigrationPlayerSpawner.cs` - Player spawning during migration
- PUID tracking for object repossession

**Origin:** Written for a melon racer clone that never got finished, but the networking code lived on.

**Credit:** Trent ([@TrentSterling](https://github.com/TrentSterling))

---

### 8. TAG_WITH_BALLS (Trent's EOS Lab) ⭐⭐

**What it is:** FishNet game with EOS integration - testing ground for EOS patterns

**What we took:**
- `StatsBatcher.cs` - **Stats-as-Name-Storage Hack!** Store player names as 20 EOS Stats (name01-name20)
- `PUIDManager.cs` - Local PUID<->Name cache for runtime lookups
- `InputControlSync.cs` - Efficient input syncing with byte compression
- `SyncedRadio.cs` - SyncTimer for time-synced audio playback

**The gold (Stats-as-Name hack):**
```csharp
// DeviceID users have NO display names in leaderboards!
// Workaround: Store name as 20 stats (1 char per stat)
for (int i = 0; i < 20; i++)
{
    string statName = $"name{(i+1):D2}";  // name01, name02, ...
    int statValue = (int)nameChars[i];    // ASCII value
    // IngestStat...
}
// Anyone can query your stats by PUID!
```

**Credit:** Trent ([@trentster222](https://github.com/trentster222))

---

## Hard-Won Debugging Lessons

### EOS Channel vs Reliability Mapping (2026-01-29) - CRITICAL

**The Bug**: NetworkTransform packets worked intermittently but caused "ArraySegment count cannot be less than 0" and IndexOutOfRange errors.

**Root Cause**: EOS P2P has TWO separate concepts:
1. `Channel` (byte): For multiplexing different streams (0, 1, 2, etc.)
2. `Reliability` (enum): For delivery guarantees (ReliableOrdered, UnreliableUnordered)

We were:
- Sending ALL data on EOS Channel 0 regardless of FishNet channel
- Setting `Reliability` correctly based on FishNet channel
- On receive, mapping `outChannel == 0` -> FishNet Reliable, else -> Unreliable

**The Problem**: Since we always sent on EOS Channel 0, the receiver ALWAYS thought packets were Reliable. FishNet then tried to parse Unreliable packets (like frequent NetworkTransform updates) as Reliable format -> corruption.

**The Fix**:
```csharp
// SEND - Use EOS Channel to encode FishNet channel
byte eosChannel = channel == Channel.Reliable ? (byte)0 : (byte)1;
var sendOptions = new SendPacketOptions
{
    Channel = eosChannel,  // 0 for Reliable, 1 for Unreliable
    Reliability = channel == Channel.Reliable
        ? PacketReliability.ReliableOrdered
        : PacketReliability.UnreliableUnordered,
    // ...
};

// RECEIVE - Map EOS Channel back to FishNet channel
Channel channel = outChannel == 0 ? Channel.Reliable : Channel.Unreliable;
```

**Key Insight**: EOS Channel and Reliability are independent. Channel is for stream multiplexing, Reliability is for delivery semantics. You need BOTH set correctly for proper FishNet integration.

**How We Found It**: Traced the data flow from FishNet → Transport → EOS P2P → Receive → FishNet. The debug logs showed packets sending/receiving correctly, but the channel mapping on receive was wrong. Many packets worked because Reliable/Unreliable sometimes have compatible formats, but complex packets like NetworkTransform failed.

---

### Device ID Conflicts with ParrelSync (2026-01-28)

**The Bug**: ParrelSync clones couldn't both be logged into EOS simultaneously.

**Root Cause**: EOS device tokens are per-machine. Both editor instances tried to use the same device ID.

**The Fix**: Append project path to device model for uniqueness:
```csharp
var deviceModel = SystemInfo.deviceUniqueIdentifier;
#if UNITY_EDITOR
if (ClonesManager.IsClone())
{
    deviceModel += ClonesManager.GetCurrentProjectPath();
}
#endif
```

---

### AcceptConnection Required on Client (2026-01-28)

**The Bug**: Client could send to server, but server responses never arrived at client.

**Root Cause**: EOS P2P is bidirectional but requires BOTH sides to accept connections. Client was sending (which auto-accepts outgoing) but never accepted incoming from server.

**The Fix**: Client must call `AcceptConnection` for the server BEFORE sending:
```csharp
// In EOSClient.Start(), before SendConnectionRequest():
var acceptOptions = new AcceptConnectionOptions
{
    LocalUserId = LocalUserId,
    RemoteUserId = _remoteUserId,
    SocketId = socketId
};
P2P.AcceptConnection(ref acceptOptions);
```

---

### Empty Packet for Connection Establishment (2026-01-28)

**The Bug**: Connection request to server never triggered `OnConnectionRequest` callback.

**Root Cause**: EOS P2P requires sending a packet to initiate the connection. Just calling AcceptConnection isn't enough.

**The Fix**: Send an empty reliable packet to trigger the connection:
```csharp
private void SendConnectionRequest()
{
    var sendOptions = new SendPacketOptions
    {
        Data = new ArraySegment<byte>(Array.Empty<byte>()),
        Reliability = PacketReliability.ReliableOrdered,
        // ...
    };
    P2P.SendPacket(ref sendOptions);
}
```

Server then receives empty packet (bytesWritten == 0) and skips it as connection handshake.

---

## EOS SDK Gotchas

1. **Initialize once per process**: `PlatformInterface.Initialize()` returns `AlreadyConfigured` on second call - handle this gracefully
2. **No re-init after shutdown**: Once `PlatformInterface.Shutdown()` is called, SDK cannot be reinitialized in same process
3. **Tick is mandatory**: `Platform.Tick()` must be called every frame or callbacks never fire
4. **Max packet size**: 1170 bytes - must fragment larger messages
5. **EncryptionKey format**: Must be exactly 64 hex characters for P2P to work
6. **Editor flag required**: Set `PlatformFlags.LoadingInEditor` in Editor or overlay causes issues

---

## DeviceID Display Name Limitation - CRITICAL

### The Problem
**PUIDs from DeviceID auth don't have display names** that other users can see.

From EOS documentation (Dec 2023):
> "EOS Product User Ids (PUIDs) don't have a display name directly, the platform account(s) the PUIDs are linked to does. In the case of DeviceId there is no platform account, the display name provided via EOS_Connect_Login is local only. This is why the display name isn't displayed in leaderboard records."

**Implications:**
- Leaderboard entries show blank names for DeviceID users
- `EOS_UserInfo_CopyBestDisplayName` only works for linked Epic accounts
- The name you pass to `Connect.Login` is **NOT** stored server-side

### The Workaround: Stats-as-Name-Storage

Use **20 EOS Stats** to store player names publicly:

```csharp
// SAVE: Convert name to 20 stats (one char per stat)
public void IngestPlayerName(string playerName)
{
    const int maxNameStats = 20;
    char[] nameChars = playerName.PadRight(maxNameStats, '\0').ToCharArray();

    List<IngestData> nameStats = new List<IngestData>();
    for (int i = 0; i < maxNameStats; i++)
    {
        nameStats.Add(new IngestData
        {
            StatName = $"name{(i + 1):D2}",  // name01, name02, ... name20
            IngestAmount = (int)nameChars[i]  // ASCII value
        });
    }

    IngestStatOptions options = new IngestStatOptions
    {
        LocalUserId = localUserId,
        TargetUserId = localUserId,
        Stats = nameStats.ToArray()
    };
    StatsHandle.IngestStat(ref options, null, callback);
}

// RETRIEVE: Query stats for any PUID, reconstruct name
public void RetrievePlayerName(ProductUserId targetPuid)
{
    char[] nameChars = new char[20];

    for (int i = 0; i < 20; i++)
    {
        int? statValue = GetStatByName(targetPuid, $"name{(i+1):D2}");
        if (statValue.HasValue)
            nameChars[i] = (char)statValue.Value;
    }

    string playerName = new string(nameChars).TrimEnd('\0');
}
```

**Why this works:**
- EOS Stats ARE publicly queryable by PUID
- Stats persist server-side (unlike Connect login display name)
- 20 stats x 1 char = 20 character names
- Works for leaderboard name display, player lookups, etc.

**Prerequisites (EOS Developer Portal):**
1. Create 20 stats: `name01`, `name02`, ... `name20`
2. Set aggregation type to `LATEST` (not SUM/MIN/MAX)
3. Make stats public in permissions

### PUID-Name Local Cache

For runtime lookups, maintain a local cache:

```csharp
[System.Serializable]
public class PUIDNamePair
{
    public string PUID;
    public string Name;
}

public class PUIDManager : MonoBehaviour
{
    public List<PUIDNamePair> PUIDsWithNames = new();

    public void AddOrUpdatePUIDWithName(string puid, string name)
    {
        var existing = PUIDsWithNames.Find(p => p.PUID == puid);
        if (existing != null) existing.Name = name;
        else PUIDsWithNames.Add(new PUIDNamePair(puid, name));
    }

    public string GetNameByPUID(string puid)
        => PUIDsWithNames.Find(p => p.PUID == puid)?.Name;
}
```

---

## Leaderboards with DeviceID

### What Leaderboard Records Contain
From EOS docs, `EOS_Leaderboards_LeaderboardRecord` has:
- `UserId` (ProductUserId)
- `Rank` (uint32)
- `Score` (int32)
- `UserDisplayName` (Utf8String) - **EMPTY for DeviceID users!**

### Solution: Query Stats for Names
When displaying leaderboard:
1. Get leaderboard records (has PUID + score)
2. For each PUID, query the `name01-name20` stats
3. Reconstruct display name from stats
4. Cache in PUIDManager for future lookups

```csharp
foreach (LeaderboardRecord record in records)
{
    string displayName = record.UserDisplayName;

    // If empty (DeviceID user), fetch from stats
    if (string.IsNullOrEmpty(displayName))
    {
        await QueryPlayerStats(record.UserId);
        displayName = RetrievePlayerNameFromStats(record.UserId);
    }

    PUIDManager.Instance.AddOrUpdatePUIDWithName(record.UserId.ToString(), displayName);
}
```

---

## Code Patterns Worth Stealing

### Efficient Input Compression (from TAG_WITH_BALLS)

```csharp
// Compress float inputs to bytes (255 levels vs 32-bit float)
private byte FloatToByte(float value)
{
    value = Mathf.Clamp(value, -1f, 1f);
    return (byte)((value + 1f) * 127.5f);  // -1..1 -> 0..255
}

private float ByteToFloat(byte value)
{
    return (value / 127.5f) - 1f;  // 0..255 -> -1..1
}

// Compress rotation to short (182.04 = 65536/360)
private short FloatToShort(float angle)
{
    angle = Mathf.Repeat(angle, 360f);
    return (short)(angle * 182.04f);
}
```

### SyncTimer for Time-Synced Audio

```csharp
[AllowMutableSyncType]
[SerializeField] public SyncTimer songTimer = new SyncTimer();

// Server starts timer
songTimer.StartTimer(clip.length);

// Clients sync audio to timer
float syncedTime = clip.length - songTimer.Remaining;
audioSource.time = syncedTime;
```

---

## Additional Inspirations

### Scamper Server Connector
Trent's previous EOS lobby management code with 6-digit join codes. Influenced the 4-digit code system (because 4 digits is "hardcore cool").

---

## The Recipe

| Ingredient | Source | Author(s) | Purpose |
|------------|--------|-----------|---------|
| SDK Bindings | EOS-SDK-CSharp | Epic Games | The foundation |
| Raw Init | EOSTransport (Mirror) | FakeByte -> Katone -> CodedImmersions | No middleware |
| Transport Structure | PurrNet | Quentin Leon (qwolf) | Clean architecture |
| ParrelSync Fix | PurrNet | Quentin Leon (qwolf) | Multi-instance testing |
| FishNet Patterns | FishyEOS | ETdoFresh & FirstGearGames | Framework compatibility |
| ClientHost | FishyEOS | ETdoFresh & FirstGearGames | Host-as-client |
| Packet Fragmentation | EOSTransport (Mirror) | FakeByte | Large messages |
| Notification Cleanup | PlayEveryWare | PlayEveryWare | Memory safety |
| Sample Credentials | PlayEveryWare | PlayEveryWare | Quick testing |
| Host Migration | Trent's Gist | Trent | Resilience |
| 3D Voice | FishNetEosVivoxLobbyTest | Roceh | Killer feature |
| 4-Digit Codes | Scamper | Trent | User-friendly |

---

## Philosophy

**"Add Component, Enter Play Mode, It Works"**

Every other EOS solution requires:
1. Install SDK
2. Install wrapper
3. Configure credentials
4. Create manager objects
5. Wire references
6. Initialize in code
7. Handle login
8. Then maybe start networking

We wanted:
1. Add `EOSNativeTransport` component
2. Press Play

Everything else should be automatic or one-click.

---

## Special Thanks

### AFoolsDuty
Without AFoolsDuty getting Trent into agentic coding, this project would not be possible. The entire "feed Claude context and let it cook" approach that made this transport come together in one session? That's the agentic workflow in action.

---

## The Discord Rubber Ducks 🦆

Big thanks to the crew who helped refine these ideas through endless conversation:

- **Wheelz** - Supportive sounding board
- **Duck** - Ironic name for a rubber duck
- **Skylar (CometDev)** ([GitHub](https://github.com/SkylarSDev) | [Website](https://skylardev.xyz)) - PhysicsNetworkTransform implementation, Setup Wizard tooltips request, and relentless encouragement to make this "the best tool around"
- **Andre** - "No, sell it somewhere" / "Devs love paying lots and lots for tools"
- **Daver 2.0** - "Quick, make a patreon!"

50k+ messages in 2025 between devs talking about VR, EOS, and everything else. Ideas get refined through conversation.

---

## Thank You

To everyone who open-sourced their EOS work:
- You saved us from reading Epic's docs
- Your bugs taught us what not to do
- Your patterns showed us what works

This project is a remix album. We just picked the best tracks.

---

## Links

### This Project
- Issues & Contributions welcome!

### EOS Resources
- [EOS Developer Portal](https://dev.epicgames.com/portal)
- [EOS Documentation](https://dev.epicgames.com/docs)

### FishNet
- [FishNet Networking](https://github.com/FirstGearGames/FishNet)
- [FishNet Discord](https://discord.gg/firstgeargames)

### Source Projects
- [PurrNetEOSTransport](https://github.com/quentinleon/PurrNetEOSTransport) - Quentin Leon
- [EOSTransport (Mirror)](https://github.com/CodedImmersions/EOSTransport) - CodedImmersions
- [FishyEOS](https://github.com/ETdoFresh/FishyEOS) - ETdoFresh
- [FishNetEosVivoxLobbyTest](https://github.com/Roceh/FishNetEosVivoxLobbyTest) - Roceh
- [PlayEveryWare EOS Plugin](https://github.com/PlayEveryWare/eos_plugin_for_unity)

---

*"3 hours later not only does the fucker work, but it also has a changelog and a readme and a roadmap/todolist"* - Trent, describing this session
