using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.RTC;
using Epic.OnlineServices.RTCAudio;
using FishNet.Transport.EOSNative.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishNet.Transport.EOSNative
{
    /// <summary>
    /// Singleton MonoBehaviour that manages the EOS SDK lifecycle.
    /// Handles platform-specific library loading, SDK initialization, device token login, and shutdown.
    /// </summary>
    public class EOSManager : MonoBehaviour
    {
        #region Singleton

        private static EOSManager s_Instance;

        /// <summary>
        /// The singleton instance of EOSManager.
        /// </summary>
        public static EOSManager Instance
        {
            get
            {
                if (s_Instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    s_Instance = FindFirstObjectByType<EOSManager>();
#else
                    s_Instance = FindObjectOfType<EOSManager>();
#endif
                }
                return s_Instance;
            }
        }

        #endregion

        #region Public State

        /// <summary>
        /// Whether the EOS SDK has been initialized successfully.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Whether a user is currently logged in via the Connect interface.
        /// </summary>
        public bool IsLoggedIn { get; private set; }

        /// <summary>
        /// The ProductUserId of the currently logged in user.
        /// </summary>
        public ProductUserId LocalProductUserId { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Fired when the EOS SDK is successfully initialized.
        /// </summary>
        public event Action OnInitialized;

        /// <summary>
        /// Fired when a user successfully logs in via the Connect interface.
        /// </summary>
        public event Action<ProductUserId> OnLoginSuccess;

        /// <summary>
        /// Fired when a login attempt fails.
        /// </summary>
        public event Action<Result> OnLoginFailed;

        /// <summary>
        /// Fired when the user logs out.
        /// </summary>
        public event Action OnLogout;

        /// <summary>
        /// Fired when authentication is about to expire (approximately 10 minutes before).
        /// </summary>
        public event Action OnAuthExpiring;

        #endregion

        #region Private Fields

        private PlatformInterface _platform;
        private ulong _authExpirationHandle;
        private ulong _loginStatusChangedHandle;

        // Tracks if SDK initialization failed in a way that requires Unity restart
        private static bool s_sdkCorrupted;

#if UNITY_EDITOR_WIN
        [DllImport("Kernel32.dll")]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("Kernel32.dll")]
        private static extern int FreeLibrary(IntPtr hLibModule);

        [DllImport("Kernel32.dll")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private IntPtr _libraryPointer;
#endif

#if UNITY_EDITOR_OSX
        [DllImport("libdl.dylib")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("libdl.dylib")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("libdl.dylib")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("libdl.dylib")]
        private static extern IntPtr dlerror();

        private const int RTLD_NOW = 2;
        private IntPtr _libraryPointer;

        private static IntPtr LoadLibraryOSX(string path)
        {
            dlerror();
            IntPtr handle = dlopen(path, RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                IntPtr error = dlerror();
                throw new Exception("dlopen: " + Marshal.PtrToStringAnsi(error));
            }
            return handle;
        }

        private static int FreeLibraryOSX(IntPtr handle)
        {
            return dlclose(handle);
        }

        private static IntPtr GetProcAddressOSX(IntPtr handle, string procName)
        {
            dlerror();
            IntPtr res = dlsym(handle, procName);
            IntPtr errPtr = dlerror();
            if (errPtr != IntPtr.Zero)
            {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }
#endif

#if UNITY_EDITOR_LINUX
        [DllImport("__Internal")]
        private static extern IntPtr dlopen(string filename, int flags);

        [DllImport("__Internal")]
        private static extern int dlclose(IntPtr handle);

        [DllImport("__Internal")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("__Internal")]
        private static extern IntPtr dlerror();

        private const int RTLD_NOW = 2;
        private IntPtr _libraryPointer;

        private static IntPtr LoadLibraryLinux(string path)
        {
            dlerror();
            IntPtr handle = dlopen(path, RTLD_NOW);
            if (handle == IntPtr.Zero)
            {
                IntPtr error = dlerror();
                throw new Exception("dlopen failed: " + Marshal.PtrToStringAnsi(error));
            }
            return handle;
        }

        private static int FreeLibraryLinux(IntPtr handle)
        {
            return dlclose(handle);
        }

        private static IntPtr GetProcAddressLinux(IntPtr handle, string procName)
        {
            dlerror();
            IntPtr res = dlsym(handle, procName);
            IntPtr errPtr = dlerror();
            if (errPtr != IntPtr.Zero)
            {
                throw new Exception("dlsym: " + Marshal.PtrToStringAnsi(errPtr));
            }
            return res;
        }
#endif

        #endregion

        #region Interface Accessors

        /// <summary>
        /// Gets the Connect interface for authentication operations.
        /// </summary>
        public ConnectInterface ConnectInterface => _platform?.GetConnectInterface();

        /// <summary>
        /// Gets the P2P interface for peer-to-peer networking.
        /// </summary>
        public P2PInterface P2PInterface => _platform?.GetP2PInterface();

        /// <summary>
        /// Gets the Lobby interface for lobby operations.
        /// </summary>
        public LobbyInterface LobbyInterface => _platform?.GetLobbyInterface();

        /// <summary>
        /// Gets the RTC interface for voice/video operations.
        /// </summary>
        public RTCInterface RTCInterface => _platform?.GetRTCInterface();

        /// <summary>
        /// Gets the RTCAudio interface for voice audio operations.
        /// </summary>
        public RTCAudioInterface RTCAudioInterface => RTCInterface?.GetAudioInterface();

        /// <summary>
        /// Player Data Storage interface for cloud saves (400MB per player).
        /// </summary>
        public Epic.OnlineServices.PlayerDataStorage.PlayerDataStorageInterface PlayerDataStorageInterface => _platform?.GetPlayerDataStorageInterface();

        /// <summary>
        /// Title Storage interface for game configs (read-only).
        /// </summary>
        public Epic.OnlineServices.TitleStorage.TitleStorageInterface TitleStorageInterface => _platform?.GetTitleStorageInterface();

        /// <summary>
        /// Reports interface for player behavior reporting.
        /// </summary>
        public Epic.OnlineServices.Reports.ReportsInterface ReportsInterface => _platform?.GetReportsInterface();

        /// <summary>
        /// Auth interface for Epic Account login.
        /// </summary>
        public Epic.OnlineServices.Auth.AuthInterface AuthInterface => _platform?.GetAuthInterface();

        /// <summary>
        /// Friends interface for Epic Account friends list.
        /// </summary>
        public Epic.OnlineServices.Friends.FriendsInterface FriendsInterface => _platform?.GetFriendsInterface();

        /// <summary>
        /// Presence interface for online status.
        /// </summary>
        public Epic.OnlineServices.Presence.PresenceInterface PresenceInterface => _platform?.GetPresenceInterface();

        /// <summary>
        /// User Info interface for player profiles.
        /// </summary>
        public Epic.OnlineServices.UserInfo.UserInfoInterface UserInfoInterface => _platform?.GetUserInfoInterface();

        /// <summary>
        /// Custom Invites interface for cross-platform game invitations.
        /// </summary>
        public Epic.OnlineServices.CustomInvites.CustomInvitesInterface CustomInvitesInterface => _platform?.GetCustomInvitesInterface();

        /// <summary>
        /// Metrics interface for player session telemetry.
        /// </summary>
        public Epic.OnlineServices.Metrics.MetricsInterface MetricsInterface => _platform?.GetMetricsInterface();

        /// <summary>
        /// Achievements interface for game achievements.
        /// </summary>
        public Epic.OnlineServices.Achievements.AchievementsInterface AchievementsInterface => _platform?.GetAchievementsInterface();

        /// <summary>
        /// Stats interface for player statistics.
        /// </summary>
        public Epic.OnlineServices.Stats.StatsInterface StatsInterface => _platform?.GetStatsInterface();

        /// <summary>
        /// Leaderboards interface for ranking queries.
        /// </summary>
        public Epic.OnlineServices.Leaderboards.LeaderboardsInterface LeaderboardsInterface => _platform?.GetLeaderboardsInterface();

        /// <summary>
        /// The local EpicAccountId (if logged in via Auth Interface).
        /// </summary>
        public Epic.OnlineServices.EpicAccountId LocalEpicAccountId { get; private set; }

        /// <summary>
        /// Whether logged in via Epic Account (enables social features).
        /// </summary>
        public bool IsEpicAccountLoggedIn => LocalEpicAccountId != null && LocalEpicAccountId.IsValid();

        /// <summary>
        /// Gets the Platform interface directly.
        /// </summary>
        public PlatformInterface Platform => _platform;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Duplicate instance detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            // Only call DontDestroyOnLoad if we're a root object (not a child of NetworkManager)
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
            // Subscribe to play mode changes to prevent crashes when exiting play mode
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif

            LoadNativeLibrary();
            LoadAndroidLibrary();
        }

        private void FixedUpdate()
        {
            _platform?.Tick();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!IsInitialized) return;

            if (pauseStatus)
            {
                // App is being suspended/backgrounded
                SetApplicationStatus(ApplicationStatus.BackgroundSuspended);
            }
            else
            {
                // App is being resumed/foregrounded
                SetApplicationStatus(ApplicationStatus.Foreground);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!IsInitialized) return;

            // On some platforms (especially mobile), focus changes indicate app state
            if (hasFocus)
            {
                SetApplicationStatus(ApplicationStatus.Foreground);
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            if (s_Instance == this)
            {
#if UNITY_EDITOR
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                // Skip shutdown if we already did it in OnPlayModeStateChanged
                if (!_isExitingPlayMode)
                {
                    Shutdown();
                }
#else
                Shutdown();
#endif
                s_Instance = null;
            }
        }

#if UNITY_EDITOR
        private bool _isExitingPlayMode;

        /// <summary>
        /// Editor safety pattern: properly shut down EOS before Unity tears things down.
        /// </summary>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Exiting play mode - performing clean shutdown");
                _isExitingPlayMode = true;

                // Do a proper shutdown while we still can
                Shutdown();
            }
        }
#endif

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the EOS SDK with the provided configuration.
        /// </summary>
        /// <param name="config">The EOSConfig asset containing credentials.</param>
        /// <returns>The result of the initialization.</returns>
        public Result Initialize(EOSConfig config)
        {
            if (IsInitialized)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Already initialized.");
                return Result.Success;
            }

            // Check if SDK is in a corrupted state from a previous crash
            if (s_sdkCorrupted)
            {
                EOSDebugLogger.LogError("EOSManager", "EOS SDK is in a corrupted state from a previous crash. Please restart Unity to reinitialize.");
                return Result.UnexpectedError;
            }

            if (config == null)
            {
                EOSDebugLogger.LogError("EOSManager", "Config is null.");
                return Result.InvalidParameters;
            }

            if (!config.Validate(out string error))
            {
                Debug.LogError($"[EOSManager] Config validation failed: {error}");
                return Result.InvalidParameters;
            }

            // Warn if using sample/test credentials
            config.WarnIfSampleCredentials();

            // Initialize the SDK
            var initOptions = new InitializeOptions
            {
                ProductName = config.ProductName,
                ProductVersion = Application.version
            };

            Result initResult = PlatformInterface.Initialize(ref initOptions);
            if (initResult != Result.Success && initResult != Result.AlreadyConfigured)
            {
                Debug.LogError($"[EOSManager] PlatformInterface.Initialize failed: {initResult}");
                return initResult;
            }

            // Create the platform interface using platform-specific options
            _platform = CreatePlatformInterface(config);
            if (_platform == null)
            {
                // Mark SDK as corrupted - this usually happens after a crash during the previous session
                s_sdkCorrupted = true;
                EOSDebugLogger.LogError("EOSManager", "PlatformInterface.Create returned null. EOS SDK may be in a bad state - please restart Unity.");
                return Result.UnexpectedError;
            }

            IsInitialized = true;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK initialized successfully.");

            // Set network status to Online by default on PC/mobile platforms
            // On consoles, the game may need to handle this differently based on platform network APIs
#if !UNITY_PS4 && !UNITY_PS5 && !UNITY_SWITCH && !UNITY_GAMECORE
            SetNetworkStatus(NetworkStatus.Online);
#else
            // On consoles, network starts as Disabled - game must call SetNetworkOnline()
            // when network connectivity is confirmed via platform APIs
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Console platform detected - call SetNetworkOnline() when network is available.");
#endif

            OnInitialized?.Invoke();

            return Result.Success;
        }

        private PlatformInterface CreatePlatformInterface(EOSConfig config)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Get XAudio2 DLL path for RTC/Voice support
            string xAudioDllPath = GetXAudio2DllPath();
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" XAudio2 DLL path: {xAudioDllPath}");

            // Use WindowsOptions for Windows platforms
            var platformOptions = new WindowsOptions
            {
                ProductId = config.ProductId,
                SandboxId = config.SandboxId,
                DeploymentId = config.DeploymentId,
                ClientCredentials = new ClientCredentials
                {
                    ClientId = config.ClientId,
                    ClientSecret = config.ClientSecret
                },
                EncryptionKey = config.EncryptionKey,
                CacheDirectory = Application.temporaryCachePath,
                IsServer = config.IsServer,
                TickBudgetInMilliseconds = config.TickBudgetInMilliseconds,
                Flags = GetPlatformFlags(),
                // RTC Options required for Voice/RTC functionality on Windows
                RTCOptions = new WindowsRTCOptions
                {
                    PlatformSpecificOptions = new WindowsRTCOptionsPlatformSpecificOptions
                    {
                        XAudio29DllPath = xAudioDllPath
                    }
                }
            };
            return PlatformInterface.Create(ref platformOptions);
#else
            // Use generic Options for other platforms
            var platformOptions = new Options
            {
                ProductId = config.ProductId,
                SandboxId = config.SandboxId,
                DeploymentId = config.DeploymentId,
                ClientCredentials = new ClientCredentials
                {
                    ClientId = config.ClientId,
                    ClientSecret = config.ClientSecret
                },
                EncryptionKey = config.EncryptionKey,
                CacheDirectory = Application.temporaryCachePath,
                IsServer = config.IsServer,
                TickBudgetInMilliseconds = config.TickBudgetInMilliseconds,
                Flags = GetPlatformFlags()
            };
            return PlatformInterface.Create(ref platformOptions);
#endif
        }

        /// <summary>
        /// Initializes the EOS SDK asynchronously.
        /// </summary>
        public async Task<Result> InitializeAsync(EOSConfig config)
        {
            return await Task.Run(() => Initialize(config));
        }

        private PlatformFlags GetPlatformFlags()
        {
#if UNITY_EDITOR
            return PlatformFlags.LoadingInEditor | PlatformFlags.DisableOverlay | PlatformFlags.DisableSocialOverlay;
#elif UNITY_SERVER
            return PlatformFlags.None;
#else
            return PlatformFlags.None;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// Gets the absolute path to the xaudio2_9redist.dll required for RTC/Voice on Windows.
        /// </summary>
        private string GetXAudio2DllPath()
        {
            // In Editor: Assets/Plugins/EOSSDK/Windows/x64/xaudio2_9redist.dll
            // In Build: <GameFolder>/<GameName>_Data/Plugins/x86_64/xaudio2_9redist.dll

#if UNITY_EDITOR
            // Editor path - use Assets folder
            return System.IO.Path.GetFullPath("Assets/Plugins/EOSSDK/Windows/x64/xaudio2_9redist.dll");
#else
            // Build path - DLL is copied to Plugins folder
            string dataPath = Application.dataPath;
            return System.IO.Path.Combine(dataPath, "Plugins", "x86_64", "xaudio2_9redist.dll");
#endif
        }
#endif

        #endregion

        #region Device Token Login

        /// <summary>
        /// Logs in using a device token (anonymous authentication).
        /// Creates a new device ID if one doesn't exist.
        /// </summary>
        /// <param name="displayName">The display name for the user.</param>
        /// <returns>The result of the login operation.</returns>
        public async Task<Result> LoginWithDeviceTokenAsync(string displayName)
        {
            if (!IsInitialized)
            {
                EOSDebugLogger.LogError("EOSManager", "Cannot login - SDK not initialized.");
                return Result.NotConfigured;
            }

            if (IsLoggedIn)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Already logged in.");
                return Result.Success;
            }

            if (string.IsNullOrEmpty(displayName) || displayName.Length > 32)
            {
                EOSDebugLogger.LogError("EOSManager", "Display name must be 1-32 characters.");
                return Result.InvalidParameters;
            }

            // Delete existing device ID (for clean Editor re-runs)
            await DeleteDeviceIdAsync();

            // Create device ID with ParrelSync support
            Result createResult = await CreateDeviceIdAsync();
            if (createResult != Result.Success && createResult != Result.DuplicateNotAllowed)
            {
                Debug.LogError($"[EOSManager] CreateDeviceId failed: {createResult}");
                OnLoginFailed?.Invoke(createResult);
                return createResult;
            }

            // Login with device token
            LoginCallbackInfo loginResult = await ConnectLoginAsync(displayName);

            if (loginResult.ResultCode == Result.InvalidUser)
            {
                // User doesn't exist, create one
                if (loginResult.ContinuanceToken == null)
                {
                    EOSDebugLogger.LogError("EOSManager", "ContinuanceToken is null, cannot create user.");
                    OnLoginFailed?.Invoke(Result.InvalidUser);
                    return Result.InvalidUser;
                }

                Result createUserResult = await CreateUserAsync(loginResult.ContinuanceToken);
                if (createUserResult != Result.Success)
                {
                    Debug.LogError($"[EOSManager] CreateUser failed: {createUserResult}");
                    OnLoginFailed?.Invoke(createUserResult);
                    return createUserResult;
                }
            }
            else if (loginResult.ResultCode != Result.Success)
            {
                Debug.LogError($"[EOSManager] Login failed: {loginResult.ResultCode}");
                OnLoginFailed?.Invoke(loginResult.ResultCode);
                return loginResult.ResultCode;
            }
            else
            {
                LocalProductUserId = loginResult.LocalUserId;
            }

            // Setup auth expiration notification
            SetupAuthExpirationNotification();
            SetupLoginStatusChangedNotification();

            IsLoggedIn = true;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Logged in successfully. ProductUserId: {LocalProductUserId}");
            OnLoginSuccess?.Invoke(LocalProductUserId);

            return Result.Success;
        }

        private string GetDeviceModel()
        {
            string model = SystemInfo.deviceUniqueIdentifier;

#if UNITY_EDITOR
            // ParrelSync support - make each clone unique
            if (IsParrelSyncClone())
            {
                string clonePath = GetParrelSyncProjectPath();
                if (!string.IsNullOrEmpty(clonePath))
                {
                    model += clonePath;
                }
            }
#endif

            // Truncate to max length if needed
            if (model.Length > 64)
            {
                model = model.Substring(0, 64);
            }

            return model;
        }

        private Task<Result> DeleteDeviceIdAsync()
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new DeleteDeviceIdOptions();
            ConnectInterface.DeleteDeviceId(ref options, null, (ref DeleteDeviceIdCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            return tcs.Task;
        }

        private Task<Result> CreateDeviceIdAsync()
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new CreateDeviceIdOptions
            {
                DeviceModel = GetDeviceModel()
            };

            ConnectInterface.CreateDeviceId(ref options, null, (ref CreateDeviceIdCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            return tcs.Task;
        }

        private Task<LoginCallbackInfo> ConnectLoginAsync(string displayName)
        {
            var tcs = new TaskCompletionSource<LoginCallbackInfo>();

            var options = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials
                {
                    Type = ExternalCredentialType.DeviceidAccessToken,
                    Token = null
                },
                UserLoginInfo = new UserLoginInfo
                {
                    DisplayName = displayName
                }
            };

            ConnectInterface.Login(ref options, null, (ref LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            return tcs.Task;
        }

        private Task<Result> CreateUserAsync(ContinuanceToken continuanceToken)
        {
            var tcs = new TaskCompletionSource<Result>();

            var options = new CreateUserOptions
            {
                ContinuanceToken = continuanceToken
            };

            ConnectInterface.CreateUser(ref options, null, (ref CreateUserCallbackInfo data) =>
            {
                if (data.ResultCode == Result.Success)
                {
                    LocalProductUserId = data.LocalUserId;
                }
                tcs.SetResult(data.ResultCode);
            });

            return tcs.Task;
        }

        private void SetupAuthExpirationNotification()
        {
            var options = new AddNotifyAuthExpirationOptions();
            _authExpirationHandle = ConnectInterface.AddNotifyAuthExpiration(ref options, null, OnAuthExpirationCallback);
        }

        private void OnAuthExpirationCallback(ref AuthExpirationCallbackInfo data)
        {
            EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Auth token is about to expire.");
            OnAuthExpiring?.Invoke();
        }

        private void SetupLoginStatusChangedNotification()
        {
            var options = new AddNotifyLoginStatusChangedOptions();
            _loginStatusChangedHandle = ConnectInterface.AddNotifyLoginStatusChanged(ref options, null, OnLoginStatusChangedCallback);
        }

        private void OnLoginStatusChangedCallback(ref LoginStatusChangedCallbackInfo data)
        {
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Login status changed: {data.PreviousStatus} -> {data.CurrentStatus}");

            if (data.CurrentStatus == LoginStatus.NotLoggedIn && IsLoggedIn)
            {
                IsLoggedIn = false;
                LocalProductUserId = null;
                OnLogout?.Invoke();
            }
        }

        #endregion

        #region Epic Account Login

        /// <summary>
        /// Logs in using Epic Account (opens Epic Games launcher overlay).
        /// This enables social features like Friends, Presence, etc.
        /// </summary>
        /// <returns>Result of the login operation.</returns>
        public async Task<Result> LoginWithEpicAccountAsync()
        {
            if (!IsInitialized)
            {
                EOSDebugLogger.LogError("EOSManager", "Cannot login - SDK not initialized.");
                return Result.NotConfigured;
            }

            if (IsEpicAccountLoggedIn)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Already logged in with Epic Account.");
                return Result.Success;
            }

            // Auth login with Account Portal (opens Epic overlay)
            var authResult = await AuthLoginAsync();
            if (authResult.ResultCode != Result.Success)
            {
                Debug.LogWarning($"[EOSManager] Epic Account login failed: {authResult.ResultCode}");
                return authResult.ResultCode;
            }

            LocalEpicAccountId = authResult.LocalUserId;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Epic Account logged in: {LocalEpicAccountId}");

            // Now connect to game services with this Epic account
            if (!IsLoggedIn)
            {
                var connectResult = await ConnectLoginWithEpicAsync(authResult.LocalUserId);
                if (connectResult != Result.Success)
                {
                    Debug.LogWarning($"[EOSManager] Connect login failed: {connectResult}");
                    // Auth succeeded but Connect failed - partial success
                }
            }

            return Result.Success;
        }

        /// <summary>
        /// Logs in using persistent auth (previously logged in Epic Account).
        /// Silent login - no overlay shown.
        /// </summary>
        public async Task<Result> LoginWithPersistentAuthAsync()
        {
            if (!IsInitialized)
                return Result.NotConfigured;

            var result = await AuthLoginPersistentAsync();
            if (result.ResultCode != Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Persistent auth not available: {result.ResultCode}");
                return result.ResultCode;
            }

            LocalEpicAccountId = result.LocalUserId;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Persistent auth succeeded: {LocalEpicAccountId}");

            // Connect to game services
            if (!IsLoggedIn)
            {
                await ConnectLoginWithEpicAsync(result.LocalUserId);
            }

            return Result.Success;
        }

        /// <summary>
        /// Try persistent auth first, fall back to device token.
        /// </summary>
        public async Task<Result> LoginSmartAsync(string displayName = "Player")
        {
            // Try persistent Epic auth first (silent)
            var persistentResult = await LoginWithPersistentAuthAsync();
            if (persistentResult == Result.Success)
            {
                return Result.Success;
            }

            // Fall back to device token
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "No persistent auth, using device token...");
            return await LoginWithDeviceTokenAsync(displayName);
        }

        private Task<Epic.OnlineServices.Auth.LoginCallbackInfo> AuthLoginAsync()
        {
            var tcs = new TaskCompletionSource<Epic.OnlineServices.Auth.LoginCallbackInfo>();

            var options = new Epic.OnlineServices.Auth.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Auth.Credentials
                {
                    Type = Epic.OnlineServices.Auth.LoginCredentialType.AccountPortal
                },
                ScopeFlags = Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile |
                             Epic.OnlineServices.Auth.AuthScopeFlags.FriendsList |
                             Epic.OnlineServices.Auth.AuthScopeFlags.Presence
            };

            AuthInterface.Login(ref options, null, (ref Epic.OnlineServices.Auth.LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            return tcs.Task;
        }

        private Task<Epic.OnlineServices.Auth.LoginCallbackInfo> AuthLoginPersistentAsync()
        {
            var tcs = new TaskCompletionSource<Epic.OnlineServices.Auth.LoginCallbackInfo>();

            var options = new Epic.OnlineServices.Auth.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Auth.Credentials
                {
                    Type = Epic.OnlineServices.Auth.LoginCredentialType.PersistentAuth
                },
                ScopeFlags = Epic.OnlineServices.Auth.AuthScopeFlags.BasicProfile |
                             Epic.OnlineServices.Auth.AuthScopeFlags.FriendsList |
                             Epic.OnlineServices.Auth.AuthScopeFlags.Presence
            };

            AuthInterface.Login(ref options, null, (ref Epic.OnlineServices.Auth.LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            return tcs.Task;
        }

        private async Task<Result> ConnectLoginWithEpicAsync(Epic.OnlineServices.EpicAccountId epicAccountId)
        {
            // Get auth token from Auth interface
            var copyOptions = new Epic.OnlineServices.Auth.CopyUserAuthTokenOptions();
            var copyResult = AuthInterface.CopyUserAuthToken(ref copyOptions, epicAccountId, out var authToken);
            if (copyResult != Result.Success || !authToken.HasValue)
            {
                Debug.LogWarning($"[EOSManager] Failed to get auth token: {copyResult}");
                return copyResult;
            }

            // Login to Connect with Epic token
            var tcs = new TaskCompletionSource<LoginCallbackInfo>();
            var loginOptions = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials
                {
                    Type = ExternalCredentialType.Epic,
                    Token = authToken.Value.AccessToken
                }
            };

            ConnectInterface.Login(ref loginOptions, null, (ref LoginCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            var loginResult = await tcs.Task;

            if (loginResult.ResultCode == Result.InvalidUser)
            {
                // Create user if needed
                if (loginResult.ContinuanceToken != null)
                {
                    var createResult = await CreateUserAsync(loginResult.ContinuanceToken);
                    if (createResult != Result.Success)
                        return createResult;
                }
            }
            else if (loginResult.ResultCode != Result.Success)
            {
                return loginResult.ResultCode;
            }
            else
            {
                LocalProductUserId = loginResult.LocalUserId;
            }

            IsLoggedIn = true;
            SetupAuthExpirationNotification();
            SetupLoginStatusChangedNotification();

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Connect login with Epic succeeded: {LocalProductUserId}");
            OnLoginSuccess?.Invoke(LocalProductUserId);
            return Result.Success;
        }

        /// <summary>
        /// Logout from Epic Account (keeps device token if active).
        /// </summary>
        public async Task LogoutEpicAccountAsync()
        {
            if (!IsEpicAccountLoggedIn)
                return;

            var tcs = new TaskCompletionSource<Epic.OnlineServices.Auth.LogoutCallbackInfo>();
            var options = new Epic.OnlineServices.Auth.LogoutOptions
            {
                LocalUserId = LocalEpicAccountId
            };

            AuthInterface.Logout(ref options, null, (ref Epic.OnlineServices.Auth.LogoutCallbackInfo data) =>
            {
                tcs.SetResult(data);
            });

            await tcs.Task;
            LocalEpicAccountId = null;
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Logged out from Epic Account");
        }

        #endregion

        #region ParrelSync Support

        private bool IsParrelSyncClone()
        {
#if UNITY_EDITOR
            try
            {
                Type clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
                if (clonesManagerType == null)
                    return false;

                MethodInfo isCloneMethod = clonesManagerType.GetMethod("IsClone",
                    BindingFlags.Public | BindingFlags.Static);
                if (isCloneMethod == null)
                    return false;

                return (bool)isCloneMethod.Invoke(null, null);
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }

        private string GetParrelSyncProjectPath()
        {
#if UNITY_EDITOR
            try
            {
                Type clonesManagerType = Type.GetType("ParrelSync.ClonesManager, ParrelSync");
                if (clonesManagerType == null)
                    return null;

                MethodInfo getPathMethod = clonesManagerType.GetMethod("GetCurrentProjectPath",
                    BindingFlags.Public | BindingFlags.Static);
                if (getPathMethod == null)
                    return null;

                return (string)getPathMethod.Invoke(null, null);
            }
            catch
            {
                return null;
            }
#else
            return null;
#endif
        }

        #endregion

        #region Native Library Loading

        private void LoadNativeLibrary()
        {
#if UNITY_EDITOR
            string libraryName = Common.LIBRARY_NAME;

#if UNITY_EDITOR_OSX
            // Remove .dylib extension for Unity asset search
            if (libraryName.EndsWith(".dylib"))
            {
                libraryName = libraryName.Substring(0, libraryName.Length - 6);
            }
#endif

            string[] libs = UnityEditor.AssetDatabase.FindAssets(libraryName);
            if (libs.Length == 0)
            {
                throw new System.IO.FileNotFoundException(
                    $"EOS SDK library '{Common.LIBRARY_NAME}' not found in project.",
                    Common.LIBRARY_NAME);
            }

            string libraryPath = UnityEditor.AssetDatabase.GUIDToAssetPath(libs[0]);
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Loading EOS SDK from: {libraryPath}");

#if UNITY_EDITOR_WIN
            _libraryPointer = LoadLibrary(libraryPath);
            if (_libraryPointer == IntPtr.Zero)
            {
                throw new Exception($"Failed to load EOS SDK library: {libraryPath}");
            }
            Bindings.Hook(_libraryPointer, GetProcAddress);
            WindowsBindings.Hook(_libraryPointer, GetProcAddress);
#elif UNITY_EDITOR_OSX
            _libraryPointer = LoadLibraryOSX(libraryPath);
            Bindings.Hook(_libraryPointer, GetProcAddressOSX);
#elif UNITY_EDITOR_LINUX
            _libraryPointer = LoadLibraryLinux(libraryPath);
            Bindings.Hook(_libraryPointer, GetProcAddressLinux);
#endif

            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK library loaded successfully.");
#endif
        }

        private void LoadAndroidLibrary()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass sys = new AndroidJavaClass("java.lang.System"))
                {
                    sys.CallStatic("loadLibrary", "EOSSDK");
                }

#if UNITY_6000_0_OR_NEWER
                using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK"))
                {
                    eos.CallStatic("init", UnityEngine.Android.AndroidApplication.currentActivity);
                }
#else
                AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using (AndroidJavaClass eos = new AndroidJavaClass("com.epicgames.mobile.eossdk.EOSSDK"))
                {
                    eos.CallStatic("init", activity);
                }
#endif

                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Android EOS SDK loaded successfully.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EOSManager] Failed to load Android EOS SDK: {e}");
                throw;
            }
#endif
        }

        private void UnloadNativeLibrary()
        {
#if UNITY_EDITOR
#if UNITY_EDITOR_WIN
            if (_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                WindowsBindings.Unhook();

                // Call FreeLibrary once - don't loop as it can hang if SDK isn't fully shut down
                FreeLibrary(_libraryPointer);
                _libraryPointer = IntPtr.Zero;
            }
#elif UNITY_EDITOR_OSX
            if (_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                FreeLibraryOSX(_libraryPointer);
                _libraryPointer = IntPtr.Zero;
            }
#elif UNITY_EDITOR_LINUX
            if (_libraryPointer != IntPtr.Zero)
            {
                Bindings.Unhook();
                FreeLibraryLinux(_libraryPointer);
                _libraryPointer = IntPtr.Zero;
            }
#endif
            EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK library unloaded.");
#endif
        }

        #endregion

        #region Shutdown

        /// <summary>
        /// Shuts down the EOS SDK and releases all resources.
        /// </summary>
        public void Shutdown()
        {
            if (_platform != null)
            {
                // Remove notifications
                if (_authExpirationHandle != 0)
                {
                    ConnectInterface?.RemoveNotifyAuthExpiration(_authExpirationHandle);
                    _authExpirationHandle = 0;
                }

                if (_loginStatusChangedHandle != 0)
                {
                    ConnectInterface?.RemoveNotifyLoginStatusChanged(_loginStatusChangedHandle);
                    _loginStatusChangedHandle = 0;
                }

                // Release platform
                _platform.Release();
                PlatformInterface.Shutdown();
                _platform = null;

                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "EOS SDK shut down.");
            }

            UnloadNativeLibrary();

            IsInitialized = false;
            IsLoggedIn = false;
            LocalProductUserId = null;
        }

        /// <summary>
        /// Logs out the current user.
        /// </summary>
        public async Task LogoutAsync()
        {
            if (!IsLoggedIn || LocalProductUserId == null)
            {
                return;
            }

            var tcs = new TaskCompletionSource<Result>();

            var options = new LogoutOptions
            {
                LocalUserId = LocalProductUserId
            };

            ConnectInterface.Logout(ref options, null, (ref LogoutCallbackInfo data) =>
            {
                tcs.SetResult(data.ResultCode);
            });

            Result result = await tcs.Task;

            if (result == Result.Success)
            {
                IsLoggedIn = false;
                LocalProductUserId = null;
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "Logged out successfully.");
                OnLogout?.Invoke();
            }
            else
            {
                Debug.LogError($"[EOSManager] Logout failed: {result}");
            }
        }

        #endregion

        #region Application and Network Status

        /// <summary>
        /// Sets the application status. Call this when your app is suspended/resumed.
        /// The SDK automatically handles this via OnApplicationPause, but you can call manually if needed.
        /// </summary>
        /// <param name="status">The new application status.</param>
        /// <returns>The result of the operation.</returns>
        public Result SetApplicationStatus(ApplicationStatus status)
        {
            if (_platform == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Cannot set application status - platform not initialized.");
                return Result.NotConfigured;
            }

            Result result = _platform.SetApplicationStatus(status);
            if (result == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Application status set to: {status}");
            }
            else
            {
                Debug.LogWarning($"[EOSManager] Failed to set application status: {result}");
            }

            return result;
        }

        /// <summary>
        /// Gets the current application status.
        /// </summary>
        /// <returns>The current application status.</returns>
        public ApplicationStatus GetApplicationStatus()
        {
            if (_platform == null)
            {
                return ApplicationStatus.Foreground;
            }

            return _platform.GetApplicationStatus();
        }

        /// <summary>
        /// Sets the network status. You MUST call this when network availability changes.
        /// On consoles (PS4, PS5, Switch, Xbox), the default is Disabled - you must set to Online when network is available.
        /// </summary>
        /// <param name="status">The new network status.</param>
        /// <returns>The result of the operation.</returns>
        public Result SetNetworkStatus(NetworkStatus status)
        {
            if (_platform == null)
            {
                EOSDebugLogger.LogWarning(DebugCategory.EOSManager, "EOSManager", "Cannot set network status - platform not initialized.");
                return Result.NotConfigured;
            }

            Result result = _platform.SetNetworkStatus(status);
            if (result == Result.Success)
            {
                EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", $" Network status set to: {status}");
            }
            else
            {
                Debug.LogWarning($"[EOSManager] Failed to set network status: {result}");
            }

            return result;
        }

        /// <summary>
        /// Gets the current network status.
        /// </summary>
        /// <returns>The current network status.</returns>
        public NetworkStatus GetNetworkStatus()
        {
            if (_platform == null)
            {
                return NetworkStatus.Disabled;
            }

            return _platform.GetNetworkStatus();
        }

        /// <summary>
        /// Convenience method to set the network status to Online.
        /// Call this after initialization when network is available.
        /// </summary>
        public void SetNetworkOnline()
        {
            SetNetworkStatus(NetworkStatus.Online);
        }

        /// <summary>
        /// Convenience method to set the network status to Offline.
        /// </summary>
        public void SetNetworkOffline()
        {
            SetNetworkStatus(NetworkStatus.Offline);
        }

        /// <summary>
        /// Convenience method to set the network status to Disabled.
        /// </summary>
        public void SetNetworkDisabled()
        {
            SetNetworkStatus(NetworkStatus.Disabled);
        }

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(EOSManager))]
    public class EOSManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var manager = (EOSManager)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Runtime Status", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(true))
            {
                // SDK Status
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("EOS SDK");
                var initStyle = new GUIStyle(EditorStyles.label);
                initStyle.normal.textColor = manager.IsInitialized ? Color.green : Color.gray;
                EditorGUILayout.LabelField(manager.IsInitialized ? "Initialized" : "Not Initialized", initStyle);
                EditorGUILayout.EndHorizontal();

                // Login Status
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Login");
                var loginStyle = new GUIStyle(EditorStyles.label);
                loginStyle.normal.textColor = manager.IsLoggedIn ? Color.green : Color.gray;
                EditorGUILayout.LabelField(manager.IsLoggedIn ? "Logged In" : "Not Logged In", loginStyle);
                EditorGUILayout.EndHorizontal();

                // PUID
                if (manager.IsLoggedIn && manager.LocalProductUserId != null)
                {
                    string puid = manager.LocalProductUserId.ToString();
                    string shortPuid = puid.Length > 20 ? puid.Substring(0, 10) + "..." + puid.Substring(puid.Length - 8) : puid;
                    EditorGUILayout.TextField("PUID", shortPuid);
                }

                // Network/App Status
                if (manager.IsInitialized)
                {
                    EditorGUILayout.TextField("Network", manager.GetNetworkStatus().ToString());
                    EditorGUILayout.TextField("App Status", manager.GetApplicationStatus().ToString());
                }
            }

            // Interfaces status
            if (Application.isPlaying && manager.IsInitialized)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Interfaces", EditorStyles.boldLabel);

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.Toggle("Connect", manager.ConnectInterface != null);
                    EditorGUILayout.Toggle("P2P", manager.P2PInterface != null);
                    EditorGUILayout.Toggle("Lobby", manager.LobbyInterface != null);
                    EditorGUILayout.Toggle("RTC", manager.RTCInterface != null);
                    EditorGUILayout.Toggle("RTC Audio", manager.RTCAudioInterface != null);
                }

                // Copy PUID button
                if (manager.IsLoggedIn && manager.LocalProductUserId != null)
                {
                    EditorGUILayout.Space(5);
                    if (GUILayout.Button("Copy Full PUID to Clipboard"))
                    {
                        GUIUtility.systemCopyBuffer = manager.LocalProductUserId.ToString();
                        EOSDebugLogger.Log(DebugCategory.EOSManager, "EOSManager", "PUID copied to clipboard");
                    }
                }
            }
            else if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see runtime status.", MessageType.Info);
            }

            if (Application.isPlaying)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
