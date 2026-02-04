# Security Model

Understanding the trust model and hardening options for FishNet EOS Native.

## Quick Summary

| What | Trust Level | Notes |
|------|-------------|-------|
| Transport (P2P) | Host-Authoritative | Host validates connections |
| Lobby Attributes | Client-Writable | Any member can write their own |
| Cloud Storage | Client-Writable | 400MB per player, no server validation |
| Votes (kick/map) | Host-Validated | Host counts, can veto |
| Ranked Results | Optionally Host-Validated | Use `*Secure()` methods |
| Achievements | Optionally Host-Validated | Use `*Secure()` methods |
| Reputation | Optionally Host-Validated | Use `*Secure()` methods |

## Host Authority Validation

Version 2.0 adds host-authority validation for critical systems.

### Setup

Add `HostAuthorityValidator` to your NetworkManager:

```csharp
// On a NetworkObject that exists in all scenes
gameObject.AddComponent<HostAuthorityValidator>();
```

### Secure Methods

Use the `*Secure` variants instead of direct calls:

```csharp
// Ranked - instead of RecordMatchResultAsync
EOSRankedMatchmaking.Instance.RecordMatchResultSecure(MatchOutcome.Win, opponentRating);

// Achievements - instead of UnlockAchievementAsync
EOSAchievements.Instance.UnlockAchievementSecure("first_blood");

// Reputation - instead of CommendPlayerAsync/ReportPlayerAsync
EOSReputationManager.Instance.CommendPlayerSecure(targetPuid, "Teamwork");
EOSReputationManager.Instance.ReportPlayerSecure(targetPuid, "Toxicity");
```

### How It Works

1. Client calls `*Secure()` method
2. Request sent to host via ServerRpc
3. Host validates:
   - Player is in lobby
   - Not rate limited
   - Request is reasonable
4. If approved, host broadcasts confirmation
5. Client applies the change locally

### Events

```csharp
var validator = HostAuthorityValidator.Instance;

validator.OnValidationRejected += (requesterPuid, actionType, reason) => {
    Debug.Log($"Rejected {actionType} from {requesterPuid}: {reason}");
};

validator.OnMatchResultValidated += (puid, outcome, change) => { };
validator.OnAchievementValidated += (puid, achievementId) => { };
validator.OnReputationValidated += (fromPuid, toPuid, isPositive) => { };
```

## Vote Security

Vote systems (kick, map, rematch) now include:

### Automatic Validation
- **Lobby membership check**: Votes only counted from current lobby members
- **Rate limiting**: Prevents vote spam attacks
- **Host authority**: Host does final vote counting

### Vote Kick Protections
- Host immunity (configurable)
- Host veto power
- Minimum player requirements
- Vote cooldowns

## Rate Limiting

Built-in rate limits prevent abuse:

| Action | Limit |
|--------|-------|
| Match results | 5/minute |
| Achievement unlocks | 10/minute |
| Reputation changes | 20/minute |
| Votes | 10-20/minute |

## What's NOT Protected

The following remain client-authoritative:

| System | Risk | Mitigation |
|--------|------|------------|
| Cloud Storage | Direct file manipulation | Use backend server |
| Clan roles | Self-promotion | Server-side role management |
| Season rewards | Double-claim | Server-side reward tracking |
| Tournament results | Fake wins | Server-side brackets |

For production competitive games, these require a dedicated backend.

## Backend Recommendations

For serious competitive games:

### Minimum
```
Game Client <-> Host (P2P) <-> Host validates
```
- Host authority for in-match actions
- Protects against casual cheating

### Recommended
```
Game Client <-> Dedicated Server <-> Database
```
- Server-authoritative game state
- Tamper-proof progression

### Services to Consider
- **PlayFab**: Full game backend, leaderboards
- **Firebase**: Auth, database, functions
- **Nakama**: Open source, self-hostable
- **Custom**: Full control, most work

## Checklist

### For Development/Testing
- [ ] `HostAuthorityValidator` attached to NetworkManager
- [ ] Using `*Secure()` methods for sensitive actions
- [ ] EOS Anti-Cheat (EAC) configured (optional)

### For Production
- [ ] All of the above
- [ ] Dedicated backend for rankings/rewards
- [ ] Server-side validation for all progression
- [ ] Monitoring for statistical anomalies
- [ ] Player reporting and moderation tools

## Offline Mode

Offline mode (`transport.StartOffline()`) bypasses all network validation since there's no host. This is expected behavior for singleplayer.

## See Also

- [SECURITY.md](https://github.com/your-repo/SECURITY.md) - Full security documentation
- [Anti-Cheat](anticheat.md) - EOS Easy Anti-Cheat integration
- [Vote Kick](votekick.md) - Vote kick system details
