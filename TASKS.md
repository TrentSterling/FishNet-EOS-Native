# FishNet-EOS-Native Tasks

Last updated: 2026-02-02

## Completed

- [x] #12 - Add replay prefab references to EOSReplayPlayer
- [x] #13 - Implement Anti-Cheat Integration (EOS EAC)
- [x] #14 - Add keyboard shortcuts for replay playback
- [x] #15 - Add timeline event markers to replay UI
- [x] #16 - Add replay file versioning and migration
- [x] #19 - Create sample ReplayRecordable prefabs
- [x] #20 - Add connection quality warnings during recording
- [x] #21 - Add replay export/import for manual sharing
- [x] #22 - Add replay duration/size warnings
- [x] #23 - Add replay favorites/star system
- [x] #28 - Update CLAUDE.md with new replay features
- [x] #29 - Update all markdown documentation for v1.0.0 release

## Pending

- [ ] #11 - Test Replay System in Unity Editor
  - Manual testing required
  - Start a match, play for 2 minutes, end match
  - Verify replay file created in persistentDataPath/Replays/
  - Load saved replay from UI, verify playback
  - Test play/pause/seek controls and keyboard shortcuts
  - Test export/import functionality
  - Verify favorites system works correctly

- [ ] #17 - Add replay sharing via cloud
  - Allow players to share replays with friends
  - Generate shareable replay codes
  - Upload replay to shared storage
  - Download replays from codes
  - Consider using EOS TitleStorage or custom endpoint
  - Note: May require external infrastructure

- [ ] #18 - Add voice chat recording to replays
  - Optionally record voice chat with replays
  - Hook into EOSVoiceManager.OnAudioFrameReceived
  - Store compressed audio chunks in replay file
  - Sync audio playback during replay viewing
  - Add privacy toggle for voice recording

## Future Ideas

- Replay thumbnails/preview images
- Replay rename functionality
- Replay statistics (heatmaps, kill feeds)
- Replay clip export (trim to highlight)
- Replay comparison mode (side-by-side)
- Server-side anti-cheat (EOSAntiCheatServer)
- Anti-cheat message integration with P2P transport

## Recent Session Summary (2026-02-02)

### Documentation Update
- Updated README.md with all new features
- Updated CHANGELOG.md with v1.0.0 release notes
- Updated ROADMAP.md with current feature status
- Updated CLASSES.md with new class documentation
- Updated TODO.md with completed tasks
- Updated package README.md for UPM users
- Updated TASKS.md with completion status

### Previous Session (2026-01-31)

#### Replay System Enhancements
- Connection quality warnings during recording
- Export/import for manual file sharing
- Duration limits with auto-stop
- Favorites system (star to protect from cleanup)

#### Anti-Cheat Integration
- Created EOSAntiCheatManager.cs
- P2P mode session management
- Peer registration/validation
- UI section in F1 debug panel
