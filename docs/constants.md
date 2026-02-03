# API Constants

Reference for key constants and limits.

## Network Constants

| Constant | Value | Description |
|----------|-------|-------------|
| `P2P_MAX_PACKET_SIZE` | 1170 bytes | Maximum single packet size |
| `PACKET_HEADER_SIZE` | 7 bytes | Reserved header space |
| `CLIENT_HOST_ID` | 32767 | ID for host acting as client (`short.MaxValue`) |
| `CONNECTION_TIMEOUT` | 25 seconds | Time before connection attempt fails |
| `RELIABLE_CHANNEL` | 0 | FishNet reliable channel |
| `UNRELIABLE_CHANNEL` | 1 | FishNet unreliable channel |

## EOS Service Limits

### Lobbies

| Limit | Value |
|-------|-------|
| Max players per lobby | 64 |
| Max lobbies per user | 16 |
| Create/Join rate | 30/min |
| Attribute updates | 100/min |
| Max attribute key length | 64 chars |
| Max attribute value length | 256 chars |

### Stats & Leaderboards

| Limit | Value |
|-------|-------|
| Stat ingestion | 100/min per user |
| Leaderboard queries | 100/min |
| Achievement updates | 100/min |

### Storage

| Limit | Player Data | Title Storage |
|-------|-------------|---------------|
| Total storage | 400 MB | Varies by plan |
| Max file size | 200 MB | 200 MB |
| Max files | Unlimited | Unlimited |

### Voice (RTC)

| Limit | Value |
|-------|-------|
| Max participants | 16 per room |
| Audio sample rate | 48000 Hz |
| Audio channels | 1 (mono) |

## Lobby Codes

| Property | Value |
|----------|-------|
| Length | 4 digits |
| Characters | 0-9 |
| Range | 0000-9999 |

## Party Codes

| Property | Value |
|----------|-------|
| Length | 6 characters |
| Characters | A-Z, 0-9 |

## Replay Limits

| Limit | Default |
|-------|---------|
| Max recording duration | 30 minutes |
| Max local replays | 50 |
| Max cloud replays | 10 |
| Recording frame rate | 20 FPS |

## Rating Defaults

### ELO

| Constant | Value |
|----------|-------|
| Starting rating | 1200 |
| K-factor (new) | 40 |
| K-factor (established) | 20 |
| K-factor (high rated) | 10 |

### Tier Thresholds (6-Tier)

| Tier | Min Rating |
|------|------------|
| Champion | 2200 |
| Diamond | 1900 |
| Platinum | 1600 |
| Gold | 1300 |
| Silver | 1000 |
| Bronze | 0 |

### Tier Thresholds (8-Tier)

| Tier | Min Rating |
|------|------------|
| Grandmaster | 2500 |
| Master | 2200 |
| Diamond | 1900 |
| Platinum | 1600 |
| Gold | 1300 |
| Silver | 1000 |
| Bronze | 700 |
| Iron | 0 |

## Platform IDs

| Platform | ID |
|----------|-----|
| Windows | WIN |
| macOS | MAC |
| Linux | LNX |
| Android | AND |
| iOS | IOS |
| Quest | QST |
| PlayStation | PS |
| Xbox | XBX |
| Switch | NSW |

## Debug Panel Keys

| Key | Panel |
|-----|-------|
| F1 | Main UI |
| F3 | Voice Debug |
| F4 | Network Debug |

## Timeout Values

| Operation | Timeout |
|-----------|---------|
| P2P connection | 25s |
| Lobby join | 30s |
| Cloud storage operation | 60s |
| Voice connection | 15s |
| Host migration | 10s |

## Quality Thresholds

### Ping

| Quality | RTT (ms) |
|---------|----------|
| Excellent | < 50 |
| Good | 50-100 |
| Fair | 100-150 |
| Poor | 150-250 |
| Bad | > 250 |

### Replay Quality Warnings

| Warning | Trigger |
|---------|---------|
| HighPing | RTT > 150ms |
| VeryHighPing | RTT > 300ms |
| LowFrameRate | < 15 FPS |
