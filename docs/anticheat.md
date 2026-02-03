# Anti-Cheat (EAC)

Easy Anti-Cheat integration for client integrity validation.

## Overview

EOS Easy Anti-Cheat (EAC) validates client integrity and detects tampering. It's available on desktop platforms only.

## Setup Requirements

1. **Configure EAC in EOS Developer Portal**
   - Navigate to your product
   - Enable Easy Anti-Cheat
   - Download the EAC SDK

2. **Run Integrity Tool**
   - During build, run the EAC integrity tool
   - Generates catalog files for validation

3. **Platform Support**
   - Windows: Yes
   - Mac: Yes
   - Linux: Yes
   - Mobile/VR: No

## Basic Usage

### Auto-Session Management

```csharp
var antiCheat = EOSAntiCheatManager.Instance;

// Enable auto-start (begins session when game starts)
antiCheat.AutoStartSession = true;
```

### Manual Session Management

```csharp
// Start protection
antiCheat.BeginSession();

// End protection
antiCheat.EndSession();

// Check status
if (antiCheat.IsSessionActive)
{
    Debug.Log("Protected");
}
```

## Status Checking

```csharp
// Get current status
AntiCheatStatus status = antiCheat.Status;

switch (status)
{
    case AntiCheatStatus.NotInitialized:
        Debug.Log("EAC not initialized yet");
        break;
    case AntiCheatStatus.NotAvailable:
        Debug.Log("EAC not configured in portal");
        break;
    case AntiCheatStatus.Initialized:
        Debug.Log("Ready but no session");
        break;
    case AntiCheatStatus.Protected:
        Debug.Log("Session active, protected");
        break;
    case AntiCheatStatus.Violated:
        Debug.Log("Integrity violation detected!");
        break;
    case AntiCheatStatus.Error:
        Debug.Log("Error state");
        break;
}

// Poll for violations
AntiCheatClientViolationType violation = antiCheat.PollStatus();
if (violation != AntiCheatClientViolationType.None)
{
    // Handle violation
}
```

## Peer Management

For P2P games, register other players as peers:

```csharp
// When a player joins
IntPtr handle = antiCheat.RegisterPeer(puid);

// When a player leaves
antiCheat.UnregisterPeer(puid);
```

## P2P Message Handling

EAC validates peers through message exchange:

```csharp
// Receive message from peer (call when you get anti-cheat data)
antiCheat.ReceiveMessageFromPeer(peerHandle, data);

// Send outgoing messages to peers
while (antiCheat.TryGetOutgoingMessage(out var peer, out var data))
{
    // Send 'data' to 'peer' over your network
    SendToPeer(peer, data);
}
```

## Events

```csharp
// Session lifecycle
antiCheat.OnSessionStarted += () =>
{
    Debug.Log("Anti-cheat protection active");
};

antiCheat.OnSessionEnded += () =>
{
    Debug.Log("Anti-cheat protection ended");
};

// Local client violation
antiCheat.OnIntegrityViolation += (type, message) =>
{
    Debug.LogError($"Integrity violation: {type} - {message}");
    // Disconnect or handle appropriately
};

// Peer violation (kick cheaters)
antiCheat.OnPeerActionRequired += (handle, action, reason) =>
{
    if (action == AntiCheatCommonClientAction.RemovePlayer)
    {
        Debug.Log($"Kick peer {handle}: {reason}");
        KickPlayer(handle);
    }
};

// Peer authentication status
antiCheat.OnPeerAuthStatusChanged += (handle, status) =>
{
    Debug.Log($"Peer {handle} auth status: {status}");
};
```

## Platform Checks

```csharp
// Only use anti-cheat on supported platforms
if (EOSPlatformHelper.IsDesktop)
{
    antiCheat.BeginSession();
}
else
{
    Debug.Log("Anti-cheat not available on this platform");
}
```

## Violation Types

| Type | Description |
|------|-------------|
| Invalid executable | Modified game files |
| Integrity catalog mismatch | Files don't match catalog |
| Client/server mismatch | Version mismatch |
| Memory modification | Cheat detected in memory |
| Debugger attached | Debugging tools detected |

## Best Practices

### Do

- Start session early in game launch
- Register all peers for P2P validation
- Handle violations gracefully (disconnect, not crash)
- Test with EAC disabled during development

### Don't

- Ship development builds with EAC bypassed
- Ignore peer validation messages
- Trust clients that fail validation

## Development Mode

During development, you may want to disable EAC:

```csharp
#if UNITY_EDITOR
    antiCheat.DevelopmentMode = true;  // Bypasses checks
#endif
```

## Troubleshooting

### "EAC not available"

- Ensure EAC is enabled in EOS Developer Portal
- Check that EAC service is installed on the machine

### Violations in development

- Run the integrity tool after each build
- Ensure catalog files are up to date

### Peer validation failing

- Verify all clients are running same version
- Check that messages are being exchanged correctly
