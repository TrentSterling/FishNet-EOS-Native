using UnityEngine;
using UnityEditor;
using System.Text;

namespace FishNet.Transport.EOSNative.Editor
{
    /// <summary>
    /// Setup wizard window for configuring EOS credentials with helpful tooltips and instructions.
    /// </summary>
    public class EOSSetupWizard : EditorWindow
    {
        private EOSConfig _config;
        private Vector2 _scrollPos;
        private bool _showAdvanced = false;
        private string _validationMessage = "";
        private MessageType _validationMessageType = MessageType.None;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _subHeaderStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _linkStyle;
        private bool _stylesInitialized;

        private const string PORTAL_URL = "https://dev.epicgames.com/portal";
        private const string DOCS_URL = "https://dev.epicgames.com/docs/game-services/eos-get-started/services-quick-start";

        [MenuItem("Tools/FishNet EOS Native/Setup Wizard", priority = -100)]
        public static void ShowWindow()
        {
            var window = GetWindow<EOSSetupWizard>("EOS Setup Wizard");
            window.minSize = new Vector2(500, 600);
            window.Show();
        }

        private void OnEnable()
        {
            FindOrCreateConfig();
        }

        private void FindOrCreateConfig()
        {
            // Try to find existing config
            var guids = AssetDatabase.FindAssets("t:EOSConfig");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _config = AssetDatabase.LoadAssetAtPath<EOSConfig>(path);
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                margin = new RectOffset(0, 0, 10, 10)
            };

            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(0, 0, 15, 5)
            };

            _instructionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                margin = new RectOffset(0, 0, 0, 10),
                padding = new RectOffset(10, 10, 5, 5)
            };
            _instructionStyle.normal.background = MakeTex(2, 2, new Color(0.2f, 0.2f, 0.2f, 0.3f));

            _linkStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.3f, 0.6f, 1f) },
                hover = { textColor = new Color(0.5f, 0.8f, 1f) },
                margin = new RectOffset(0, 0, 5, 5)
            };

            _stylesInitialized = true;
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void OnGUI()
        {
            InitStyles();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Header
            EditorGUILayout.LabelField("EOS Setup Wizard", _headerStyle);
            EditorGUILayout.Space(5);

            // Quick Links
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Developer Portal", GUILayout.Height(30)))
                Application.OpenURL(PORTAL_URL);
            if (GUILayout.Button("View Documentation", GUILayout.Height(30)))
                Application.OpenURL(DOCS_URL);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Config Selection
            DrawConfigSection();

            if (_config == null)
            {
                EditorGUILayout.HelpBox("Create or select an EOSConfig to continue.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(10);

            // Step-by-step sections
            DrawStep1_ProductSettings();
            DrawStep2_ClientCredentials();
            DrawStep3_EncryptionKey();
            DrawStep4_OptionalSettings();

            EditorGUILayout.Space(10);

            // Validation
            DrawValidation();

            EditorGUILayout.Space(20);
            EditorGUILayout.EndScrollView();
        }

        private void DrawConfigSection()
        {
            EditorGUILayout.LabelField("Configuration Asset", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            _config = (EOSConfig)EditorGUILayout.ObjectField("EOS Config", _config, typeof(EOSConfig), false);
            if (EditorGUI.EndChangeCheck() && _config != null)
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create New Config"))
            {
                CreateNewConfig();
            }
            if (_config != null && GUILayout.Button("Select in Project"))
            {
                Selection.activeObject = _config;
                EditorGUIUtility.PingObject(_config);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void CreateNewConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create EOS Config",
                "EOSConfig",
                "asset",
                "Choose location for the EOS configuration asset"
            );

            if (!string.IsNullOrEmpty(path))
            {
                _config = ScriptableObject.CreateInstance<EOSConfig>();
                AssetDatabase.CreateAsset(_config, path);
                AssetDatabase.SaveAssets();
                Selection.activeObject = _config;
            }
        }

        private void DrawStep1_ProductSettings()
        {
            EditorGUILayout.LabelField("Step 1: Product Settings", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Find these values in the EOS Developer Portal:\n" +
                "Your Product > Product Settings > SDK Credentials & Deployment",
                _instructionStyle
            );

            EditorGUI.BeginChangeCheck();

            // Product Name
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Product Name", "A friendly name for your game. Used in SDK logging."), GUILayout.Width(120));
            _config.ProductName = EditorGUILayout.TextField(_config.ProductName);
            EditorGUILayout.EndHorizontal();

            // Product ID
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Product ID *", "Found in: Product Settings > SDK Credentials\nFormat: xxxxxxxxxxxxxxxx..."), GUILayout.Width(120));
            _config.ProductId = EditorGUILayout.TextField(_config.ProductId);
            EditorGUILayout.EndHorizontal();

            // Sandbox ID
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Sandbox ID *", "Found in: Product Settings > SDK Credentials\nUsually starts with your product name"), GUILayout.Width(120));
            _config.SandboxId = EditorGUILayout.TextField(_config.SandboxId);
            EditorGUILayout.EndHorizontal();

            // Deployment ID
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Deployment ID *", "Found in: Product Settings > Deployment\nCreate one if none exist (e.g., 'dev', 'live')"), GUILayout.Width(120));
            _config.DeploymentId = EditorGUILayout.TextField(_config.DeploymentId);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStep2_ClientCredentials()
        {
            EditorGUILayout.LabelField("Step 2: Client Credentials", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Create a Client in the Developer Portal:\n" +
                "Your Product > Product Settings > Clients > Add New Client\n" +
                "Policy: Peer2Peer (for P2P games) or GameClient",
                _instructionStyle
            );

            EditorGUI.BeginChangeCheck();

            // Client ID
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Client ID *", "Found in: Product Settings > Clients\nThe ID of your client policy"), GUILayout.Width(120));
            _config.ClientId = EditorGUILayout.TextField(_config.ClientId);
            EditorGUILayout.EndHorizontal();

            // Client Secret
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Client Secret *", "Found in: Product Settings > Clients\nKeep this secret! Don't commit to public repos."), GUILayout.Width(120));
            _config.ClientSecret = EditorGUILayout.PasswordField(_config.ClientSecret);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.HelpBox(
                "Security Note: The Client Secret will be embedded in your build. " +
                "For production, consider using a backend server for authentication.",
                MessageType.Info
            );

            EditorGUILayout.EndVertical();
        }

        private void DrawStep3_EncryptionKey()
        {
            EditorGUILayout.LabelField("Step 3: Encryption Key", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "Required for P2P networking and Player Data Storage.\n" +
                "Must be exactly 64 hexadecimal characters (32 bytes).",
                _instructionStyle
            );

            EditorGUI.BeginChangeCheck();

            // Encryption Key
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Encryption Key *", "64 hex characters for AES-256 encryption\nExample: 1111111111111111111111111111111111111111111111111111111111111111"), GUILayout.Width(120));
            _config.EncryptionKey = EditorGUILayout.TextField(_config.EncryptionKey);
            EditorGUILayout.EndHorizontal();

            // Show character count
            int keyLength = _config.EncryptionKey?.Length ?? 0;
            Color oldColor = GUI.color;
            GUI.color = keyLength == 64 ? Color.green : (keyLength > 0 ? Color.yellow : Color.gray);
            EditorGUILayout.LabelField($"Characters: {keyLength}/64", EditorStyles.miniLabel);
            GUI.color = oldColor;

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Generate Random Key"))
            {
                _config.EncryptionKey = GenerateEncryptionKey();
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.HelpBox(
                "Save this key somewhere safe! If you lose it, you cannot decrypt existing player data.",
                MessageType.Warning
            );

            EditorGUILayout.EndVertical();
        }

        private void DrawStep4_OptionalSettings()
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Step 4: Optional Settings", true);

            if (!_showAdvanced) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();

            // Default Display Name
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Display Name", "Default name for DeviceID login (max 32 chars)\nPlayers see this before setting their own name"), GUILayout.Width(120));
            _config.DefaultDisplayName = EditorGUILayout.TextField(_config.DefaultDisplayName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Is Server
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Is Server", "Enable for dedicated server builds\nDisables overlay and some client features"), GUILayout.Width(120));
            _config.IsServer = EditorGUILayout.Toggle(_config.IsServer);
            EditorGUILayout.EndHorizontal();

            // Tick Budget
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Tick Budget (ms)", "Max time for SDK work per frame (0 = unlimited)\nIncrease if you see hitches, decrease for more responsive callbacks"), GUILayout.Width(120));
            _config.TickBudgetInMilliseconds = (uint)EditorGUILayout.IntField((int)_config.TickBudgetInMilliseconds);
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_config);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", _subHeaderStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (GUILayout.Button("Validate Configuration", GUILayout.Height(30)))
            {
                ValidateConfig();
            }

            if (!string.IsNullOrEmpty(_validationMessage))
            {
                EditorGUILayout.HelpBox(_validationMessage, _validationMessageType);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Quick status
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Quick Check", EditorStyles.boldLabel);

            DrawStatusRow("Product ID", !string.IsNullOrEmpty(_config.ProductId));
            DrawStatusRow("Sandbox ID", !string.IsNullOrEmpty(_config.SandboxId));
            DrawStatusRow("Deployment ID", !string.IsNullOrEmpty(_config.DeploymentId));
            DrawStatusRow("Client ID", !string.IsNullOrEmpty(_config.ClientId));
            DrawStatusRow("Client Secret", !string.IsNullOrEmpty(_config.ClientSecret));
            DrawStatusRow("Encryption Key (64 chars)", _config.EncryptionKey?.Length == 64);

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusRow(string label, bool isValid)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(200));

            Color oldColor = GUI.color;
            GUI.color = isValid ? Color.green : Color.red;
            EditorGUILayout.LabelField(isValid ? "✓" : "✗", GUILayout.Width(20));
            GUI.color = oldColor;

            EditorGUILayout.EndHorizontal();
        }

        private void ValidateConfig()
        {
            if (_config.Validate(out string error))
            {
                _validationMessage = "Configuration is valid! You're ready to use EOS.";
                _validationMessageType = MessageType.Info;
            }
            else
            {
                _validationMessage = error;
                _validationMessageType = MessageType.Error;
            }
        }

        private string GenerateEncryptionKey()
        {
            StringBuilder sb = new StringBuilder(64);
            System.Random random = new System.Random();
            const string chars = "0123456789ABCDEF";

            for (int i = 0; i < 64; i++)
            {
                sb.Append(chars[random.Next(chars.Length)]);
            }

            return sb.ToString();
        }
    }
}
