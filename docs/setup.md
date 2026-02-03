# Setup Wizard

Configure your EOS credentials and project settings.

## Opening the Wizard

`Tools > FishNet EOS Native > Setup Wizard`

## EOS Developer Portal Setup

Before using the transport, you need credentials from the EOS Developer Portal.

1. Go to [dev.epicgames.com/portal](https://dev.epicgames.com/portal)
2. Create or select your product
3. Navigate to **Product Settings**
4. Note down:
   - Product ID
   - Sandbox ID
   - Deployment ID
   - Client ID
   - Client Secret

## Configuring Credentials

The Setup Wizard creates an `EOSConfig` ScriptableObject with your credentials.

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

## Platform-Specific Setup

### Windows

DLL files are automatically configured. Ensure the x86 and x64 DLLs are set to the correct platforms in the Unity Inspector.

### Android

1. Set minimum API level to 23+
2. Enable IL2CPP scripting backend
3. The native `.so` files will be included automatically

### iOS

1. Requires valid provisioning profile
2. Enable Push Notifications capability (for invites)

## Verification

After configuration:

1. Enter Play Mode
2. Check Console for "EOS initialized successfully"
3. Open F1 debug panel to verify connection

## Common Issues

### "Result.AlreadyConfigured"

EOS SDK can only be initialized once per process. This is normal when re-entering Play Mode.

### "Encryption key invalid"

Ensure your key is exactly 64 hex characters (0-9, A-F).

### DLL not found

Check that platform-specific DLLs are configured in Unity:
- `Plugins/x86_64/EOSSDK-Win64-Shipping.dll` → Windows x64
- `Plugins/x86/EOSSDK-Win32-Shipping.dll` → Windows x86
