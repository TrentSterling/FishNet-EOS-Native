# Security Model

This document describes the security architecture, trust assumptions, and limitations of FishNet EOS Native.

## TL;DR

**This is a client-authoritative system.** Without a dedicated backend server, determined cheaters can manipulate their game state. The host-authority model stops casual exploits but not sophisticated attacks.

**For production competitive games: Use a dedicated backend (PlayFab, Firebase, custom server).**

## Trust Model

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     EOS Cloud Services                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │ Cloud Storage│  │   Lobbies    │  │  Anti-Cheat  │      │
│  │ (per-player) │  │ (attributes) │  │    (EAC)     │      │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘      │
└─────────┼─────────────────┼─────────────────┼───────────────┘
          │                 │                 │
    ┌─────▼─────┐     ┌─────▼─────┐     ┌─────▼─────┐
    │  Client A │◄───►│   HOST    │◄───►│  Client B │
    │ (Player)  │ P2P │ (Authority)│ P2P │ (Player)  │
    └───────────┘     └───────────┘     └───────────┘
```

### Who Trusts Whom

| Entity | Trusts | Trusted By | Notes |
|--------|--------|------------|-------|
| **EOS Cloud Storage** | Anyone with valid auth | All clients | Client-writable, no server validation |
| **Lobby Attributes** | Any lobby member | All lobby members | Any member can write their own attrs |
| **Host** | EOS for auth | Clients for game state | Host validates but is still a client |
| **Clients** | Host for game state | No one | Clients request, host authorizes |
| **EAC** | EOS backend | All clients | Monitors integrity, can't prevent all cheats |

## What's Protected

### Host-Authority Validation (v2.0+)

The following systems use host-authority validation:

| System | Protection | Bypassed By |
|--------|------------|-------------|
| **Ranked Results** | Host confirms match outcome | Cheating host |
| **Achievements** | Host confirms unlock criteria | Cheating host, offline mode |
| **Reputation** | Host validates commend/report source | Cheating host |
| **Vote Kick** | Host counts votes authoritatively | Cheating host |
| **Map Voting** | Host counts votes authoritatively | Cheating host |
| **Rematch Voting** | Host counts votes authoritatively | Cheating host |

### EOS Anti-Cheat (EAC)

When enabled, EAC provides:
- Client integrity verification
- Known cheat signature detection
- Memory manipulation detection

EAC does NOT prevent:
- Network packet manipulation
- Cloud storage tampering
- Logic exploits in game code

## What's NOT Protected

### Client-Writable Data

These systems store data in EOS Cloud Storage, which is client-writable:

| Data | File | Risk |
|------|------|------|
| Ranked rating/history | `ranked_data.json` | Client can inflate rating |
| Achievement progress | PlayerPrefs cache | Client can unlock all |
| Reputation score | `reputation_data.json` | Client can boost karma |
| Clan membership/role | `clan_data.json` | Client can self-promote |
| Season rewards | `season_data.json` | Client can re-claim rewards |
| Friends list | `friends.json` | Low risk (social only) |

### Inherent P2P Limitations

- **No central authority**: The "host" is just another player
- **Host can cheat**: A malicious host has full control
- **Packet manipulation**: Determined attackers can modify network traffic
- **Memory editing**: Client memory is always readable/writable

## Attack Scenarios

### Casual Cheater (BLOCKED by host-authority)
```
Attacker: Modifies local achievement progress
Host: Rejects unlock request (criteria not met)
Result: Achievement not unlocked
```

### Sophisticated Cheater (NOT BLOCKED)
```
Attacker: Hosts their own lobby
Attacker: Modifies cloud storage directly
Attacker: Reports fake match results as host
Result: Inflated rating persists
```

### Memory Editor (NOT BLOCKED)
```
Attacker: Uses Cheat Engine or similar
Attacker: Modifies in-memory game state
Attacker: Bypasses client-side checks
Result: Depends on what host validates
```

## Recommendations for Production Games

### Minimum (Casual Games)
- Enable EOS Anti-Cheat (EAC)
- Use host-authority validation (included)
- Monitor for statistical anomalies
- Community reporting system

### Recommended (Competitive Games)
- **Dedicated match server** - Server-authoritative game state
- **Backend validation** - PlayFab, Firebase, or custom
- **Match recording** - Server records all game events
- **Anti-cheat integration** - EAC + custom detection

### Backend Options

| Service | Pros | Cons |
|---------|------|------|
| **PlayFab** | Full game services, Microsoft backed | Learning curve, vendor lock-in |
| **Firebase** | Simple, generous free tier | Limited game-specific features |
| **Custom Server** | Full control | Development/hosting cost |
| **Nakama** | Open source, self-hostable | Requires hosting |

### What to Move Server-Side

For a competitive game, these should be server-authoritative:

1. **Match Results** - Server records who played, scores, outcome
2. **Rating Calculations** - Server computes ELO/MMR changes
3. **Achievement Unlocks** - Server tracks progress, validates criteria
4. **Leaderboards** - Server validates before accepting scores
5. **Economy/Rewards** - Server controls currency, items

## Security Checklist

### Before Launch
- [ ] Enable EAC in EOS Developer Portal
- [ ] Run integrity tool during builds
- [ ] Test host migration doesn't leak state
- [ ] Verify vote systems can't be manipulated by non-host
- [ ] Test that disconnected players can't affect votes

### Ongoing
- [ ] Monitor leaderboards for anomalies
- [ ] Review cloud storage for impossible values
- [ ] Check achievement unlock rates
- [ ] Process community reports
- [ ] Ban accounts with statistical impossibilities

## Reporting Vulnerabilities

If you discover a security vulnerability:

1. **Do NOT** create a public GitHub issue
2. Contact the maintainer directly
3. Provide detailed reproduction steps
4. Allow reasonable time for a fix

## Version History

| Version | Changes |
|---------|---------|
| 2.0 | Added host-authority validation for all critical systems |
| 1.0 | Initial release (client-authoritative) |
