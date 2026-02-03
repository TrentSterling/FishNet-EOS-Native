# Troubleshooting

Common issues and solutions.

## SDK Initialization

### "Result.AlreadyConfigured"

**Cause**: EOS SDK can only be initialized once per process.

**Solution**: This is normal behavior in the Unity Editor. The SDK persists across play mode sessions. No action needed.

### "Cannot reinitialize after Shutdown"

**Cause**: Called `EOSManager.Shutdown()` then tried to use EOS again.

**Solution**: Avoid calling `Shutdown()` unless the application is closing. In Editor, exiting play mode handles cleanup.

### "Callbacks never fire"

**Cause**: `Tick()` is not being called.

**Solution**: Ensure `EOSManager` is in the scene and active. It calls `Tick()` in `Update()`.

## Connection Issues

### "Connection timeout"

**Causes**:
- NAT traversal failed
- Firewall blocking UDP
- Wrong SocketId

**Solutions**:
1. Check both clients have matching `SocketId.SocketName`
2. Verify firewall allows UDP traffic
3. Try enabling relay fallback
4. Check F4 debug panel for connection status

### "Server not accepting connections"

**Cause**: Server didn't call `AcceptConnection`.

**Solution**: This is handled automatically by `EOSServer`. Verify the server is running and in the correct state.

### "Max packet size exceeded"

**Cause**: Sending packet > 1170 bytes without fragmentation.

**Solution**: `PacketFragmenter` handles this automatically. If you see this error, ensure you're using the transport's `Send` methods, not direct P2P calls.

## Lobby Issues

### "Failed to create lobby"

**Causes**:
- Rate limit exceeded (30/min)
- Already in max lobbies (16)
- Invalid attributes

**Solutions**:
1. Wait and retry
2. Leave other lobbies first
3. Check attribute key/value lengths (64/256 char max)

### "Lobby code not found"

**Causes**:
- Typo in code
- Lobby was destroyed
- Lobby is private/hidden

**Solutions**:
1. Verify the exact 4-digit code
2. Have host confirm lobby still exists
3. Check lobby visibility settings

### "Lobby join failed"

**Causes**:
- Lobby is full
- Lobby requires password
- Rate limit exceeded

**Solutions**:
1. Check `lobby.PlayerCount < lobby.MaxPlayers`
2. Provide password if required
3. Wait before retrying

## Voice Issues

### "No voice audio"

**Causes**:
- Microphone permissions denied
- User is muted
- RTC not connected

**Solutions**:
1. Check platform microphone permissions
2. Verify `IsLocalMuted` is false
3. Check F3 panel for RTC status
4. Verify lobby has RTC enabled

### "Echo/feedback"

**Cause**: Echo cancellation not enabled.

**Solution**: Configure echo cancellation in EOS Developer Portal settings.

### "High voice latency"

**Cause**: Network conditions or relay routing.

**Solution**:
1. Check F4 panel for ping
2. Ensure direct P2P connection when possible
3. Voice has higher latency through relay

## Host Migration

### "Players not reconnecting after migration"

**Cause**: SocketId mismatch after new host elected.

**Solution**: Ensure all clients reconnect using the new host's PUID. This is handled automatically by `HostMigrationManager`.

### "Game state lost after migration"

**Cause**: State stored only on original host.

**Solution**:
1. Use lobby attributes for critical state
2. Implement `HostMigratable` on important objects
3. Save state in `OnMigrationStarted`, restore in `OnMigrationCompleted`

## DLL Issues

### "DLL not found" (Windows)

**Cause**: Platform-specific DLLs not configured correctly.

**Solution**:
1. Select DLL in Project window
2. Check Inspector for platform settings
3. Ensure `EOSSDK-Win64-Shipping.dll` → Windows x64
4. Ensure `EOSSDK-Win32-Shipping.dll` → Windows x86

### "EntryPointNotFoundException"

**Cause**: Wrong DLL version or architecture mismatch.

**Solution**:
1. Verify DLL matches your EOS SDK version
2. Check 32-bit vs 64-bit match
3. Re-download SDK if needed

## Android Issues

### "Native library failed to load"

**Causes**:
- Wrong `.so` architecture
- Missing library file
- Incorrect Unity settings

**Solutions**:
1. Include all architectures: armeabi-v7a, arm64-v8a
2. Check `Plugins/Android/` folder structure
3. Enable IL2CPP scripting backend
4. Set API level 23+

## Credentials Issues

### "Invalid encryption key"

**Cause**: Key is not exactly 64 hex characters.

**Solution**: Use the Setup Wizard to generate a valid key, or manually create a 64-character hex string (0-9, A-F only).

### "Authentication failed"

**Causes**:
- Invalid credentials
- Wrong sandbox/deployment
- Portal configuration issue

**Solutions**:
1. Verify all credentials match EOS Developer Portal
2. Check you're using the correct sandbox
3. Ensure DeviceID auth is enabled in portal

## Anti-Cheat Issues

### "EAC not available"

**Cause**: EAC not configured in portal or not supported platform.

**Solution**:
1. Enable EAC in EOS Developer Portal
2. Install EAC service on development machine
3. Note: Mobile/VR not supported

### "Integrity violation in development"

**Cause**: Catalog files outdated after code changes.

**Solution**: Run the EAC integrity tool after each build to regenerate catalogs.

## Performance Issues

### "High memory usage"

**Causes**:
- Too many replays stored
- Large lobby attribute values
- Debug logging excessive

**Solutions**:
1. Replays auto-cleanup (50 local max)
2. Keep attribute values small
3. Mute verbose debug categories

### "Frame rate drops during network activity"

**Cause**: Main thread blocked by network operations.

**Solution**: All network operations should be async. Ensure you're using `await` properly and not calling `.Result` on tasks.

## Getting Help

1. Check F1/F3/F4 debug panels for status
2. Enable relevant debug categories
3. Check Console for error messages
4. Review [GitHub Issues](https://github.com/TrentSterling/FishNet-EOS-Native/issues)
5. Consult [EOS Documentation](https://dev.epicgames.com/docs)
