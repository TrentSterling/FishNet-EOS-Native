using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Transport.EOSNative.Lobbies;
using FishNet.Transport.EOSNative.Logging;
using FishNet.Transport.EOSNative.Voice;
using UnityEngine;

namespace FishNet.Transport.EOSNative.Migration
{
    /// <summary>
    /// Host migration testing helper. Use this to verify migration works correctly.
    /// Provides test methods and a runtime verification checklist.
    /// </summary>
    public class HostMigrationTester : MonoBehaviour
    {
        #region Singleton

        private static HostMigrationTester _instance;
        public static HostMigrationTester Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<HostMigrationTester>();
                }
                return _instance;
            }
        }

        #endregion

        #region Test Results

        [Serializable]
        public class TestResult
        {
            public string TestName;
            public bool Passed;
            public string Details;
            public DateTime Timestamp;
        }

        #endregion

        #region Private Fields

        private List<TestResult> _testResults = new();
        private bool _testInProgress;
        private string _preMigrationLobbyCode;
        private int _preMigrationMemberCount;
        private int _preMigrationPlayerCount;
        private List<string> _preMigrationChatHistory = new();
        private bool _voiceWasActive;

        #endregion

        #region Public Properties

        /// <summary>All test results.</summary>
        public IReadOnlyList<TestResult> TestResults => _testResults;

        /// <summary>Whether a test is in progress.</summary>
        public bool IsTestInProgress => _testInProgress;

        /// <summary>Number of passed tests.</summary>
        public int PassedCount => _testResults.FindAll(t => t.Passed).Count;

        /// <summary>Number of failed tests.</summary>
        public int FailedCount => _testResults.FindAll(t => !t.Passed).Count;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Start()
        {
            // Subscribe to migration events
            var migrationManager = HostMigrationManager.Instance;
            if (migrationManager != null)
            {
                migrationManager.OnMigrationStarted += OnMigrationStarted;
                migrationManager.OnMigrationCompleted += OnMigrationCompleted;
            }
        }

        private void OnDestroy()
        {
            var migrationManager = HostMigrationManager.Instance;
            if (migrationManager != null)
            {
                migrationManager.OnMigrationStarted -= OnMigrationStarted;
                migrationManager.OnMigrationCompleted -= OnMigrationCompleted;
            }

            if (_instance == this)
                _instance = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Capture the current state before a migration test.
        /// Call this BEFORE triggering migration (e.g., before host disconnects).
        /// </summary>
        public void CapturePreMigrationState()
        {
            _testResults.Clear();
            _testInProgress = true;

            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager != null && lobbyManager.IsInLobby)
            {
                var lobby = lobbyManager.CurrentLobby;
                _preMigrationLobbyCode = lobby.JoinCode;
                _preMigrationMemberCount = lobby.MemberCount;
            }

            // Count player objects
            _preMigrationPlayerCount = 0;
            if (InstanceFinder.IsServerStarted)
            {
                foreach (var nob in InstanceFinder.ServerManager.Objects.Spawned.Values)
                {
                    if (nob.Owner.IsValid) _preMigrationPlayerCount++;
                }
            }

            // Capture chat history
            _preMigrationChatHistory.Clear();
            var chatManager = EOSLobbyChatManager.Instance;
            if (chatManager != null)
            {
                foreach (var msg in chatManager.Messages)
                {
                    _preMigrationChatHistory.Add(msg.ToString());
                }
            }

            // Check voice state
            var voiceManager = EOSVoiceManager.Instance;
            _voiceWasActive = voiceManager != null && voiceManager.IsConnected;

            AddResult("Pre-Migration Capture", true,
                $"Lobby: {_preMigrationLobbyCode}, Members: {_preMigrationMemberCount}, " +
                $"Players: {_preMigrationPlayerCount}, Chat: {_preMigrationChatHistory.Count} msgs, " +
                $"Voice: {_voiceWasActive}");
        }

        /// <summary>
        /// Run all verification tests after migration completes.
        /// Call this AFTER migration is complete.
        /// </summary>
        public void RunPostMigrationTests()
        {
            StartCoroutine(RunTestsCoroutine());
        }

        /// <summary>
        /// Simulate host disconnect for testing.
        /// Only works if we're not the host.
        /// </summary>
        public void SimulateHostDisconnect()
        {
            if (InstanceFinder.IsServerStarted)
            {
                Debug.LogWarning("[HostMigrationTester] Cannot simulate - we are the host!");
                return;
            }

            CapturePreMigrationState();
            Debug.Log("[HostMigrationTester] Captured state. Now disconnect the host to trigger migration.");
        }

        /// <summary>
        /// Clear all test results.
        /// </summary>
        public void ClearResults()
        {
            _testResults.Clear();
            _testInProgress = false;
        }

        /// <summary>
        /// Get a summary of test results.
        /// </summary>
        public string GetSummary()
        {
            if (_testResults.Count == 0) return "No tests run yet.";

            return $"Tests: {PassedCount}/{_testResults.Count} passed, {FailedCount} failed";
        }

        #endregion

        #region Event Handlers

        private void OnMigrationStarted()
        {
            if (!_testInProgress)
            {
                // Auto-capture if not already captured
                CapturePreMigrationState();
            }

            AddResult("Migration Started", true, "Migration process initiated");
        }

        private void OnMigrationCompleted()
        {
            AddResult("Migration Completed", true, "Migration process finished");

            // Auto-run tests after a short delay
            StartCoroutine(DelayedTests(2f));
        }

        #endregion

        #region Test Implementation

        private IEnumerator DelayedTests(float delay)
        {
            yield return new WaitForSeconds(delay);
            RunPostMigrationTests();
        }

        private IEnumerator RunTestsCoroutine()
        {
            EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationTester", "Running post-migration tests...");

            yield return new WaitForSeconds(0.5f);

            // Test 1: Lobby Preserved
            TestLobbyPreserved();
            yield return new WaitForSeconds(0.2f);

            // Test 2: Members Reconnected
            TestMembersReconnected();
            yield return new WaitForSeconds(0.2f);

            // Test 3: Player Objects Restored
            TestPlayerObjectsRestored();
            yield return new WaitForSeconds(0.2f);

            // Test 4: Chat History Preserved
            TestChatHistoryPreserved();
            yield return new WaitForSeconds(0.2f);

            // Test 5: Voice Connection
            TestVoiceConnection();
            yield return new WaitForSeconds(0.2f);

            // Test 6: Server/Client State
            TestNetworkState();

            _testInProgress = false;

            EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationTester",
                $"Tests complete: {PassedCount}/{_testResults.Count} passed");
        }

        private void TestLobbyPreserved()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            bool inLobby = lobbyManager != null && lobbyManager.IsInLobby;
            bool passed = inLobby && lobbyManager.CurrentLobby.JoinCode == _preMigrationLobbyCode;

            AddResult("Lobby Preserved", passed,
                passed ? $"Same lobby code: {lobbyManager.CurrentLobby.JoinCode}" : "Lobby changed or lost!");
        }

        private void TestMembersReconnected()
        {
            var lobbyManager = EOSLobbyManager.Instance;
            if (lobbyManager == null || !lobbyManager.IsInLobby)
            {
                AddResult("Members Reconnected", false, "No lobby found");
                return;
            }

            int currentCount = lobbyManager.CurrentLobby.MemberCount;
            // Allow for the departed host
            bool passed = currentCount >= _preMigrationMemberCount - 1;

            AddResult("Members Reconnected", passed,
                $"Members: {currentCount} (was {_preMigrationMemberCount})");
        }

        private void TestPlayerObjectsRestored()
        {
            int currentPlayerCount = 0;

            if (InstanceFinder.IsServerStarted)
            {
                foreach (var nob in InstanceFinder.ServerManager.Objects.Spawned.Values)
                {
                    if (nob.Owner.IsValid) currentPlayerCount++;
                }
            }
            else if (InstanceFinder.IsClientStarted)
            {
                foreach (var nob in InstanceFinder.ClientManager.Objects.Spawned.Values)
                {
                    if (nob.Owner.IsValid) currentPlayerCount++;
                }
            }

            // Allow for departed host's player
            bool passed = currentPlayerCount >= _preMigrationPlayerCount - 1;

            AddResult("Player Objects Restored", passed,
                $"Players: {currentPlayerCount} (was {_preMigrationPlayerCount})");
        }

        private void TestChatHistoryPreserved()
        {
            var chatManager = EOSLobbyChatManager.Instance;
            if (chatManager == null)
            {
                AddResult("Chat History Preserved", false, "No chat manager");
                return;
            }

            int currentCount = chatManager.Messages.Count;
            // Chat should be preserved (EOS lobby handles this)
            bool passed = currentCount >= _preMigrationChatHistory.Count;

            AddResult("Chat History Preserved", passed,
                $"Chat messages: {currentCount} (was {_preMigrationChatHistory.Count})");
        }

        private void TestVoiceConnection()
        {
            var voiceManager = EOSVoiceManager.Instance;
            bool currentVoiceActive = voiceManager != null && voiceManager.IsConnected;

            // Voice should survive migration (RTC is lobby-based)
            bool passed = !_voiceWasActive || currentVoiceActive;

            AddResult("Voice Connection", passed,
                passed ? (currentVoiceActive ? "Voice connected" : "Voice was not active")
                       : "Voice connection lost!");
        }

        private void TestNetworkState()
        {
            bool isServer = InstanceFinder.IsServerStarted;
            bool isClient = InstanceFinder.IsClientStarted;

            bool passed = isServer || isClient;

            string state = isServer ? "Server" : (isClient ? "Client" : "Disconnected");
            AddResult("Network State", passed, $"State: {state}");
        }

        private void AddResult(string testName, bool passed, string details)
        {
            _testResults.Add(new TestResult
            {
                TestName = testName,
                Passed = passed,
                Details = details,
                Timestamp = DateTime.Now
            });

            string icon = passed ? "[PASS]" : "[FAIL]";
            EOSDebugLogger.Log(DebugCategory.HostMigrationManager, "HostMigrationTester",
                $"{icon} {testName}: {details}");
        }

        #endregion
    }
}
