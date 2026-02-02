using UnityEngine;

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Configuration asset for EOS SDK credentials.
    /// Create via Assets > Create > FishNet > EOS Native > Config
    /// </summary>
    [CreateAssetMenu(fileName = "EOSConfig", menuName = "FishNet/EOS Native/Config", order = 1)]
    public class EOSConfig : ScriptableObject
    {
        [Header("Product Settings")]
        [Tooltip("The product name for the running application")]
        public string ProductName = "MyGame";

        [Tooltip("The product ID from the EOS Developer Portal")]
        public string ProductId;

        [Tooltip("The sandbox ID from the EOS Developer Portal")]
        public string SandboxId;

        [Tooltip("The deployment ID from the EOS Developer Portal")]
        public string DeploymentId;

        [Header("Client Credentials")]
        [Tooltip("The client ID from the EOS Developer Portal")]
        public string ClientId;

        [Tooltip("The client secret from the EOS Developer Portal")]
        public string ClientSecret;

        [Header("Encryption")]
        [Tooltip("A 64-character hexadecimal encryption key (32 bytes) for P2P and storage encryption")]
        public string EncryptionKey;

        [Header("Default User Settings")]
        [Tooltip("Default display name for device token login (max 32 characters)")]
        public string DefaultDisplayName = "Player";

        [Header("Advanced Settings")]
        [Tooltip("Set to true for dedicated server builds")]
        public bool IsServer = false;

        [Tooltip("Tick budget in milliseconds (0 = perform all work)")]
        public uint TickBudgetInMilliseconds = 0;

        // PlayEveryWare sample credentials - for detection
        private const string SAMPLE_CLIENT_ID = "xyza7891wPzGRvRf4SkjlIF8YuqlRLbQ";
        private const string SAMPLE_PRODUCT_ID = "f7102b835ed14b5fb6b3a05d87b3d101";

        /// <summary>
        /// Returns true if using PlayEveryWare's public sample/test credentials.
        /// These are fine for testing but won't work for achievements, stats, leaderboards, etc.
        /// </summary>
        public bool IsUsingSampleCredentials()
        {
            return ClientId == SAMPLE_CLIENT_ID || ProductId == SAMPLE_PRODUCT_ID;
        }

        /// <summary>
        /// Logs a warning if sample credentials are detected.
        /// Call this during initialization to remind developers to get their own keys.
        /// </summary>
        public void WarnIfSampleCredentials()
        {
            if (IsUsingSampleCredentials())
            {
                Debug.LogWarning(
                    "╔════════════════════════════════════════════════════════════════╗\n" +
                    "║  ⚠️  USING PLAYEVERYWARE SAMPLE EOS CREDENTIALS  ⚠️            ║\n" +
                    "╠════════════════════════════════════════════════════════════════╣\n" +
                    "║  These are PUBLIC TEST KEYS for quick testing only!           ║\n" +
                    "║                                                               ║\n" +
                    "║  ✅ WORKS: P2P connections, lobbies, voice chat, basic login  ║\n" +
                    "║  ❌ WON'T WORK: Achievements, stats, leaderboards, store,     ║\n" +
                    "║                 custom backend data, production releases      ║\n" +
                    "║                                                               ║\n" +
                    "║  Get your own FREE credentials at:                            ║\n" +
                    "║  https://dev.epicgames.com/portal                             ║\n" +
                    "║                                                               ║\n" +
                    "║  Then update your EOSConfig asset or use the Setup Wizard:    ║\n" +
                    "║  Tools > FishNet EOS Native > Setup Wizard                    ║\n" +
                    "╚════════════════════════════════════════════════════════════════╝"
                );
            }
        }

        /// <summary>
        /// Validates the configuration and returns any errors.
        /// </summary>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(ProductName))
            {
                error = "ProductName is required";
                return false;
            }

            if (string.IsNullOrEmpty(ProductId))
            {
                error = "ProductId is required";
                return false;
            }

            if (string.IsNullOrEmpty(SandboxId))
            {
                error = "SandboxId is required";
                return false;
            }

            if (string.IsNullOrEmpty(DeploymentId))
            {
                error = "DeploymentId is required";
                return false;
            }

            if (string.IsNullOrEmpty(ClientId))
            {
                error = "ClientId is required";
                return false;
            }

            if (string.IsNullOrEmpty(ClientSecret))
            {
                error = "ClientSecret is required";
                return false;
            }

            if (string.IsNullOrEmpty(EncryptionKey))
            {
                error = "EncryptionKey is required for P2P functionality";
                return false;
            }

            if (EncryptionKey.Length != 64)
            {
                error = $"EncryptionKey must be exactly 64 hexadecimal characters (currently {EncryptionKey.Length})";
                return false;
            }

            if (string.IsNullOrEmpty(DefaultDisplayName))
            {
                error = "DefaultDisplayName is required";
                return false;
            }

            if (DefaultDisplayName.Length > 32)
            {
                error = $"DefaultDisplayName must be 32 characters or less (currently {DefaultDisplayName.Length})";
                return false;
            }

            error = null;
            return true;
        }
    }
}
