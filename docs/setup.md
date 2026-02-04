# Setup Guide

Complete setup guide for FishNet EOS Native transport.

## Prerequisites

- Unity 6 (6000.0.x) or Unity 2022.3 LTS
- FishNet Networking (via Package Manager or Asset Store)
- EOS SDK (see installation below)

## Step 1: Install EOS C# SDK

**IMPORTANT:** This transport uses the raw EOS C# SDK directly - no PlayEveryWare plugin needed.

### Download the SDK

1. Go to [Epic Developer Portal](https://dev.epicgames.com/portal)
2. Navigate to your product → SDK & Release Notes
3. Download the **EOS C# SDK** (not the Unity Plugin)

### Install the SDK

1. Extract the SDK zip
2. Copy the C# SDK files to `Assets/Plugins/EOSSDK/`
   - Include the `Core/` folder with all `.cs` files
   - Include platform DLLs (`EOSSDK-Win64-Shipping.dll`, etc.)

3. **Create the asmdef file** (required for assembly references):
   - Copy `Assets/FishNet.Transport.EOSNative/Editor/EOSSDKSetup/Epic.OnlineServices.asmdef.txt`
   - Rename to `Epic.OnlineServices.asmdef` (remove the `.txt`)
   - Place in your SDK folder: `Assets/Plugins/EOSSDK/Epic.OnlineServices.asmdef`

4. Configure platform settings for DLLs in Unity Inspector:
   - `EOSSDK-Win64-Shipping.dll` → Windows x64 only
   - `EOSSDK-Win32-Shipping.dll` → Windows x86 only

### Folder Structure

```
Assets/Plugins/EOSSDK/
├── Epic.OnlineServices.asmdef    ← You create this
├── Core/
│   ├── *.cs                      ← C# SDK source files
├── Generated/
│   ├── *.cs                      ← Generated bindings
├── x86_64/
│   └── EOSSDK-Win64-Shipping.dll
├── x86/
│   └── EOSSDK-Win32-Shipping.dll
└── ... (other platform libs)
```

### Verifying Installation

After setup, you should see:
- No compile errors referencing `Epic` namespace
- `Epic.OnlineServices` assembly visible in project

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

**Cause:** EOS C# SDK not installed or missing asmdef file.

**Fix:**
1. Verify EOS C# SDK files are in `Assets/Plugins/EOSSDK/`
2. Check for `Epic.OnlineServices.asmdef` in your SDK folder
3. If missing, copy from `Editor/EOSSDKSetup/Epic.OnlineServices.asmdef.txt` and rename (remove `.txt`)
4. Make sure the asmdef is in the same folder as your SDK `.cs` files

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

**Cause:** EOS SDK samples use old C# syntax that doesn't compile in modern Unity.

**Fix:** Delete the `Samples` folder from the EOS SDK - it's not needed.

```
Assets/Plugins/EOSSDK/Samples/  ← DELETE THIS ENTIRE FOLDER
```

### DLLs Not Loading in Editor

**Cause:** The transport manually loads EOS DLLs in the editor. The DLL must be findable.

**Fix:** Ensure the DLL is in your Assets folder with correct platform settings:
- `EOSSDK-Win64-Shipping.dll` - Set to Editor + Windows x64
- Check Console for "Loading EOS SDK from:" message on play
