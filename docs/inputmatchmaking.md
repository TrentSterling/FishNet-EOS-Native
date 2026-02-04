# Input-Based Matchmaking

Match players by their input device for fair competitive play.

## Overview

Input-based matchmaking groups players by their input method (keyboard/mouse, controller, touch, VR) to ensure fair competition. Players using similar input devices are matched together, preventing unfair advantages.

## Input Types

| Type | ID | Description |
|------|-----|-------------|
| KeyboardMouse | KBM | Keyboard and mouse input |
| Controller | CTL | Gamepad/controller input |
| Touch | TCH | Touch screen input |
| VRController | VRC | VR motion controllers |

## Current Input Detection

```csharp
// Get current input type
InputType type = EOSInputHelper.CurrentInputType;
string typeId = EOSInputHelper.InputTypeId;  // "KBM", "CTL", etc.

// Check specific types
if (EOSInputHelper.IsKeyboardMouse) { }
if (EOSInputHelper.IsController) { }
if (EOSInputHelper.IsTouch) { }
if (EOSInputHelper.IsVR) { }

// Get display name
string name = EOSInputHelper.GetInputTypeName(type);  // "Keyboard & Mouse"
string icon = EOSInputHelper.GetInputTypeIcon(type);  // emoji icon

// Listen for changes
EOSInputHelper.OnInputTypeChanged += (newType) =>
{
    Debug.Log($"Input changed to: {newType}");
};
```

## Searching by Input Type

### Using LobbyOptions (Recommended)

```csharp
// Same input type only
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().SameInputOnly()
);

// Keyboard/mouse only
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().KeyboardMouseOnly()
);

// Controller only
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().ControllerOnly()
);

// Fair input (same or compatible types)
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().FairInputOnly()
);

// Specific input type
var (result, lobbies) = await transport.SearchLobbiesAsync(
    new LobbyOptions().WithInputFilter("CTL")
);
```

### Using LobbySearchOptions

```csharp
var options = new LobbySearchOptions()
    .WithGameMode("ranked")
    .SameInputOnly();

var options = new LobbySearchOptions()
    .KeyboardMouseOnly()
    .ExcludePassworded();

var options = new LobbySearchOptions()
    .FairInputOnly()
    .WithRegion("us-east");
```

## Fair Input Matching

The `FairInputOnly()` filter groups compatible input types:

| Your Input | Matches With |
|------------|--------------|
| Keyboard/Mouse | Keyboard/Mouse only |
| Controller | Controller, Touch |
| Touch | Controller, Touch |
| VR Controller | VR Controller only |

**Why this grouping?**
- KBM has precision advantage over controllers - separate pool
- Controller and touch are roughly equivalent - shared pool
- VR has unique mechanics - separate pool

## Hosting with Input Type

Lobbies automatically include the host's input type:

```csharp
// HOST_INPUT_TYPE is automatically set when hosting
var (result, lobby) = await transport.HostLobbyAsync();
// Lobby will have HOST_INPUT_TYPE = "KBM" (or current input)

// Use with other options
var (result, lobby) = await transport.HostLobbyAsync(
    new LobbyOptions()
        .WithGameMode("ranked")
        .WithMaxPlayers(8)
);
// HOST_INPUT_TYPE is still auto-set
```

## Quick Match with Input Filtering

```csharp
// Find or host with same input type
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(
    new LobbyOptions()
        .WithGameMode("deathmatch")
        .SameInputOnly()
);

// Fair input matching
var (result, lobby, didHost) = await transport.QuickMatchOrHostAsync(
    new LobbyOptions()
        .WithGameMode("ranked")
        .FairInputOnly()
);
```

## Combined Filtering

Combine input filtering with other filters:

```csharp
// Competitive: same platform AND same input
var competitive = new LobbyOptions()
    .WithGameMode("ranked")
    .SamePlatformOnly()
    .SameInputOnly()
    .ExcludePassworded();

// Casual: fair input, any platform
var casual = new LobbyOptions()
    .WithGameMode("casual")
    .FairInputOnly();

// Region + input filtering
var regional = new LobbyOptions()
    .WithRegion("us-east")
    .KeyboardMouseOnly()
    .WithMaxPlayers(8);
```

## Input Type Compatibility Check

```csharp
// Check if two input types are "fair" for matchmaking
bool fair = EOSInputHelper.AreInputTypesFair(InputType.Controller, InputType.Touch);
// Returns: true (controller and touch are compatible)

fair = EOSInputHelper.AreInputTypesFair(InputType.KeyboardMouse, InputType.Controller);
// Returns: false (KBM has advantage over controller)
```

## Dynamic Input Switching

Input type is detected dynamically and updates when the player switches:

```csharp
// Track input changes
EOSInputHelper.OnInputTypeChanged += (newType) =>
{
    // Could warn player if they switch mid-match
    if (inMatch && newType != originalInput)
    {
        ShowWarning("Input type changed! Some lobbies may reject mixed input.");
    }
};

// Time since last input activity
float idleTime = EOSInputHelper.TimeSinceLastInput;
```

## Events

```csharp
// Input type changed
EOSInputHelper.OnInputTypeChanged += (InputType newType) =>
{
    Debug.Log($"Now using: {EOSInputHelper.GetInputTypeName(newType)}");
};
```

## Best Practices

### Competitive Games
```csharp
// Strict input matching for ranked
var ranked = new LobbyOptions()
    .WithGameMode("ranked")
    .SameInputOnly()        // Exact match
    .SamePlatformOnly();    // Same platform too
```

### Casual Games
```csharp
// Relaxed matching for casual
var casual = new LobbyOptions()
    .WithGameMode("casual")
    .FairInputOnly();       // Compatible types allowed
    // No platform filter - all welcome
```

### Cross-Input Option
```csharp
// Let players choose
if (playerPrefersCrossInput)
{
    // No input filter - any input welcome
    options = new LobbyOptions().WithGameMode("casual");
}
else
{
    // Input-restricted
    options = new LobbyOptions()
        .WithGameMode("casual")
        .FairInputOnly();
}
```
