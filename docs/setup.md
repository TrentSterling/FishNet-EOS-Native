# Setup Guide

Complete setup guide for FishNet EOS Native transport.

## Prerequisites

- Unity 6 (6000.0.x) or Unity 2022.3 LTS
- FishNet Networking (via Package Manager or Asset Store)
- EOS SDK (see installation below)

## Step 1: Install EOS SDK

**IMPORTANT:** The transport requires the EOS SDK to be installed first.

### Option A: EOS Unity Plugin (Recommended)

1. Download from [Epic Games GitHub](https://github.com/PlayEveryWare/eos_plugin_for_unity) or Unity Asset Store
2. Import into your project
3. The plugin includes `Epic.OnlineServices.asmdef` - no additional setup needed

### Option B: Raw EOS SDK (Advanced)

If using the raw EOS SDK without the Unity Plugin:

1. Download EOS SDK from [Epic Developer Portal](https://dev.epicgames.com/portal)
2. Copy SDK files to `Assets/Plugins/EOSSDK/`
3. **Create the asmdef file** (required for assembly references):
   - Copy `Assets/FishNet.Transport.EOSNative/Editor/EOSSDKSetup/Epic.OnlineServices.asmdef.txt`
   - Rename to `Epic.OnlineServices.asmdef`
   - Place in your EOS SDK folder (e.g., `Assets/Plugins/EOSSDK/`)
4. Configure platform settings for DLLs in Unity Inspector

### Verifying EOS SDK Installation

After installation, you should see:
- No compile errors referencing `Epic` namespace
- An `Epic.OnlineServices` assembly in your project

## Step 2: Install FishNet EOS Native

### Via Package Manager (Git URL)

1. Open Package Manager (`Window > Package Manager`)
2. Click `+` > `Add package from git URL`
3. Enter: `https://github.com/TrentSterling/FishNet-EOS-Native.git`

### Via Local Package

1. Clone or download the repository
2. In Package Manager, click `+` > `Add package from disk`
3. Select the `package.json` file

## Step 3: Configure EOS Credentials

### Opening the Setup Wizard

`Tools > FishNet EOS Native > Setup Wizard`

### Getting Credentials from EOS Portal

1. Go to [dev.epicgames.com/portal](https://dev.epicgames.com/portal)
2. Create or select your product
3. Navigate to **Product Settings**
4. Note down:
   - Product ID
   - Sandbox ID
   - Deployment ID
   - Client ID
   - Client Secret

### Required Fields

| Field | Description |
|-------|-------------|
| Product Name | Your game's name |
| Product Version | Version string (e.g., "1.0.0") |
| Product ID | From EOS Portal |
| Sandbox ID | From EOS Portal |
| Deployment ID | From EOS Portal |
| Client ID | From EOS Portal |
| Client Secret | From EOS Portal |
| Encryption Key | 64 hex characters (auto-generated) |

### Encryption Key

The encryption key must be **exactly 64 hexadecimal characters**. The wizard can auto-generate one for you.

Example valid key:
```
1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF
```

## Step 4: Add Transport to Scene

1. Create empty GameObject
2. Add `EOSNativeTransport` component
3. **Auto-created components:**
   - NetworkManager (FishNet)
   - EOSManager
   - EOSLobbyManager
   - EOSVoiceManager
   - HostMigrationManager

## Step 5: Verify Setup

1. Enter Play Mode
2. Check Console for "EOS initialized successfully"
3. Press F1 to open debug panel
4. Verify status shows "Connected"

## Platform-Specific Setup

### Windows

DLL files should be configured automatically. Verify in Unity Inspector:
- `Plugins/x86_64/EOSSDK-Win64-Shipping.dll` → Windows x64
- `Plugins/x86/EOSSDK-Win32-Shipping.dll` → Windows x86

### Android

1. Set minimum API level to 23+
2. Enable IL2CPP scripting backend
3. The native `.so` files are included automatically

### iOS

1. Requires valid provisioning profile
2. Enable Push Notifications capability (for invites)

## Troubleshooting

### "The type or namespace 'Epic' could not be found"

**Cause:** EOS SDK not installed or missing asmdef file.

**Fix:**
1. Verify EOS SDK is in your project
2. Check for `Epic.OnlineServices.asmdef` in your SDK folder
3. If missing, copy from `Editor/EOSSDKSetup/Epic.OnlineServices.asmdef.txt`

### "Result.AlreadyConfigured"

**Cause:** EOS SDK can only be initialized once per process.

**Fix:** This is normal when re-entering Play Mode. Restart Unity if persistent.

### "Encryption key invalid"

**Cause:** Key isn't exactly 64 hex characters.

**Fix:** Use the wizard's auto-generate button or create a valid key.

### "DLL not found"

**Cause:** Platform-specific DLLs not configured.

**Fix:** Check DLL platform settings in Unity Inspector.

### Sample Scripts Have Errors

**Cause:** EOS SDK samples may have compatibility issues.

**Fix:** Delete the `Samples` folder from the EOS SDK if not needed.
