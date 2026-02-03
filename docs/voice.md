# Voice Chat

Built-in voice communication using EOS Real-Time Communication (RTC).

## Overview

Voice chat is automatically available when players join a lobby. The RTC channel persists through host migration.

## Basic Usage

Voice is enabled by default. Players can control their own audio:

```csharp
var voice = EOSVoiceManager.Instance;

// Mute/unmute yourself
voice.SetLocalMuted(true);
voice.SetLocalMuted(false);
voice.ToggleLocalMute();

// Check mute state
bool isMuted = voice.IsLocalMuted;
```

## Per-Player Controls

### Muting Other Players

```csharp
// Mute a specific player (local only)
voice.MutePlayer(puid);
voice.UnmutePlayer(puid);

// Check if player is muted
bool muted = voice.IsPlayerMuted(puid);
```

### Volume Control

```csharp
// Set player volume (0.0 to 2.0, default 1.0)
voice.SetPlayerVolume(puid, 0.5f);  // 50% volume
voice.SetPlayerVolume(puid, 1.5f);  // 150% volume

float volume = voice.GetPlayerVolume(puid);
```

## Pitch Shifting

Voice effects using the SMBPitchShifter:

```csharp
// Apply pitch shift to a player
voice.SetPlayerPitch(puid, 1.5f);  // Higher pitch
voice.SetPlayerPitch(puid, 0.7f);  // Lower pitch
voice.SetPlayerPitch(puid, 1.0f);  // Normal

float pitch = voice.GetPlayerPitch(puid);
```

## 3D Spatial Audio

Configure spatial audio for immersive positional voice.

### Spatial Blend

```csharp
var voicePlayer = GetComponent<FishNetVoicePlayer>();

// 0 = 2D (no positioning), 1 = full 3D
voicePlayer.SpatialBlend = 1f;
```

### Doppler Effect

```csharp
// 0 = off, 1 = normal, higher = exaggerated
voicePlayer.DopplerLevel = 1f;

// Doppler works automatically as players move relative to listener
```

### Distance Rolloff

Configure via Inspector or code:

```csharp
// Inspector defaults (recommended to configure there)
// - Min Distance: 1
// - Max Distance: 50
// - Rolloff Mode: Logarithmic

// Or access the underlying AudioSource
voicePlayer.AudioSource.minDistance = 1f;
voicePlayer.AudioSource.maxDistance = 50f;
voicePlayer.AudioSource.rolloffMode = AudioRolloffMode.Linear;
```

### Full 3D Audio Setup

For fully immersive positional voice:

```csharp
var voicePlayer = GetComponent<FishNetVoicePlayer>();

// Enable full 3D positioning
voicePlayer.SpatialBlend = 1f;

// Enable doppler for moving players
voicePlayer.DopplerLevel = 1f;

// Configure in Inspector:
// - Min Distance: 1 (full volume within this range)
// - Max Distance: 50 (silent beyond this range)
// - Rolloff Mode: Logarithmic (natural falloff)
```

## Audio Levels

Monitor voice activity:

```csharp
// Get current speaking level (0.0 to 1.0)
float level = voice.GetPlayerAudioLevel(puid);

// Check if player is currently speaking
bool speaking = voice.IsPlayerSpeaking(puid);

// Get all currently speaking players
var speakers = voice.GetSpeakingPlayers();
```

## Push-to-Talk

Enable push-to-talk mode:

```csharp
voice.PushToTalkEnabled = true;

// In your input handler:
void Update()
{
    if (Input.GetKey(KeyCode.V))
        voice.SetLocalMuted(false);
    else if (voice.PushToTalkEnabled)
        voice.SetLocalMuted(true);
}
```

## Events

```csharp
voice.OnPlayerStartedSpeaking += (puid) => { };
voice.OnPlayerStoppedSpeaking += (puid) => { };
voice.OnPlayerJoinedVoice += (puid) => { };
voice.OnPlayerLeftVoice += (puid) => { };
voice.OnLocalMuteChanged += (isMuted) => { };
```

## Platform Support

| Platform | Voice Support |
|----------|---------------|
| Windows | Yes |
| Mac | Yes |
| Linux | Yes |
| Android | Yes |
| iOS | Yes |
| Quest | Yes |

Check support at runtime:

```csharp
if (EOSPlatformHelper.SupportsVoice)
{
    // Enable voice UI
}
```

## Debug Panel

Press **F3** to open the Voice Debug Panel showing:
- RTC connection status
- Participant list with audio levels
- Mute states
- Speaking indicators

## Troubleshooting

### No Audio

1. Check microphone permissions (especially on mobile)
2. Verify RTC is connected in F3 panel
3. Ensure player isn't muted locally

### Echo/Feedback

Enable echo cancellation in EOS Developer Portal settings.

### High Latency

RTC uses UDP with low latency by design. If experiencing delays:
1. Check network quality in F4 panel
2. Verify not routing through relay unnecessarily
