using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using FishNet.Transport.EOSNative;

namespace FishNet.Transport.EOSNative.Tests.Runtime
{
    /// <summary>
    /// Play Mode tests for transport functionality.
    /// Run via Window > General > Test Runner > Play Mode
    /// </summary>
    [TestFixture]
    public class TransportTests
    {
        private GameObject _transportObject;
        private EOSNativeTransport _transport;

        [SetUp]
        public void SetUp()
        {
            _transportObject = new GameObject("TestTransport");
            _transport = _transportObject.AddComponent<EOSNativeTransport>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_transportObject != null)
            {
                Object.Destroy(_transportObject);
            }
        }

        [UnityTest]
        public IEnumerator Transport_StartsInCorrectState()
        {
            yield return null; // Wait one frame

            Assert.IsFalse(_transport.IsOfflineMode);
            Assert.IsFalse(_transport.IsInLobby);
        }

        [UnityTest]
        public IEnumerator OfflineMode_SetsCorrectState()
        {
            // Start offline mode
            _transport.StartOffline();

            yield return null; // Wait one frame

            Assert.IsTrue(_transport.IsOfflineMode);
        }

        [UnityTest]
        public IEnumerator OfflineMode_CanBeShutdown()
        {
            _transport.StartOffline();
            yield return null;

            Assert.IsTrue(_transport.IsOfflineMode);

            _transport.Shutdown();
            yield return null;

            Assert.IsFalse(_transport.IsOfflineMode);
        }
    }

    /// <summary>
    /// Play Mode tests for toast notifications.
    /// </summary>
    [TestFixture]
    public class ToastManagerTests
    {
        private GameObject _toastObject;
        private EOSToastManager _toastManager;

        [SetUp]
        public void SetUp()
        {
            _toastObject = new GameObject("TestToastManager");
            _toastManager = _toastObject.AddComponent<EOSToastManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_toastObject != null)
            {
                Object.Destroy(_toastObject);
            }
        }

        [UnityTest]
        public IEnumerator ToastManager_InstanceIsSet()
        {
            yield return null;

            Assert.IsNotNull(EOSToastManager.Instance);
        }

        [UnityTest]
        public IEnumerator ToastManager_ShowsInfo()
        {
            yield return null;

            EOSToastManager.Info("Test Title", "Test Message");

            yield return null;

            // Toast was created (visible toasts list would have entry)
            Assert.Pass("Info toast shown without error");
        }

        [UnityTest]
        public IEnumerator ToastManager_ShowsSuccess()
        {
            yield return null;

            EOSToastManager.Success("Success!", "Operation completed");

            yield return null;

            Assert.Pass("Success toast shown without error");
        }

        [UnityTest]
        public IEnumerator ToastManager_ShowsWarning()
        {
            yield return null;

            EOSToastManager.Warning("Warning", "Something happened");

            yield return null;

            Assert.Pass("Warning toast shown without error");
        }

        [UnityTest]
        public IEnumerator ToastManager_ShowsError()
        {
            yield return null;

            EOSToastManager.Error("Error", "Something went wrong");

            yield return null;

            Assert.Pass("Error toast shown without error");
        }

        [UnityTest]
        public IEnumerator ToastManager_ClearAllWorks()
        {
            yield return null;

            EOSToastManager.Info("Test 1", "");
            EOSToastManager.Info("Test 2", "");
            EOSToastManager.Info("Test 3", "");

            yield return null;

            EOSToastManager.ClearAll();

            yield return null;

            Assert.Pass("ClearAll executed without error");
        }
    }

    /// <summary>
    /// Play Mode tests for ping system.
    /// </summary>
    [TestFixture]
    public class PingManagerTests
    {
        private GameObject _pingObject;
        private EOSPingManager _pingManager;

        [SetUp]
        public void SetUp()
        {
            _pingObject = new GameObject("TestPingManager");
            _pingManager = _pingObject.AddComponent<EOSPingManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_pingObject != null)
            {
                Object.Destroy(_pingObject);
            }
        }

        [UnityTest]
        public IEnumerator PingManager_InstanceIsSet()
        {
            yield return null;

            Assert.IsNotNull(EOSPingManager.Instance);
        }

        [UnityTest]
        public IEnumerator PingManager_DefaultSettingsAreCorrect()
        {
            yield return null;

            Assert.AreEqual(5f, _pingManager.DefaultPingDuration);
            Assert.AreEqual(0.5f, _pingManager.PingCooldown);
            Assert.AreEqual(3, _pingManager.MaxActivePings);
            Assert.IsTrue(_pingManager.AllowEnemyPings);
            Assert.IsTrue(_pingManager.ShowPingIndicators);
        }

        [UnityTest]
        public IEnumerator PingManager_GetPingColor_ReturnsColors()
        {
            yield return null;

            var defaultColor = _pingManager.GetPingColor(PingType.Default);
            var enemyColor = _pingManager.GetPingColor(PingType.Enemy);
            var dangerColor = _pingManager.GetPingColor(PingType.Danger);

            Assert.AreNotEqual(enemyColor, defaultColor);
            Assert.AreNotEqual(dangerColor, defaultColor);
        }
    }

    /// <summary>
    /// Play Mode tests for achievements.
    /// </summary>
    [TestFixture]
    public class AchievementsTests
    {
        private GameObject _achievementsObject;
        private EOSAchievements _achievements;

        [SetUp]
        public void SetUp()
        {
            _achievementsObject = new GameObject("TestAchievements");
            _achievements = _achievementsObject.AddComponent<EOSAchievements>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_achievementsObject != null)
            {
                Object.Destroy(_achievementsObject);
            }
        }

        [UnityTest]
        public IEnumerator Achievements_InstanceIsSet()
        {
            yield return null;

            Assert.IsNotNull(EOSAchievements.Instance);
        }

        [UnityTest]
        public IEnumerator Achievements_DefaultSettingsAreCorrect()
        {
            yield return null;

            Assert.IsTrue(_achievements.ShowPopups);
            Assert.AreEqual(5f, _achievements.PopupDuration);
            Assert.IsTrue(_achievements.EnableOfflineCache);
        }

        [UnityTest]
        public IEnumerator Achievements_StartsNotReady()
        {
            yield return null;

            // Without EOS login, should not be ready
            Assert.IsFalse(_achievements.IsReady);
            Assert.AreEqual(0, _achievements.TotalAchievements);
            Assert.AreEqual(0, _achievements.UnlockedCount);
        }
    }

    /// <summary>
    /// Play Mode tests for auto-reconnect.
    /// </summary>
    [TestFixture]
    public class AutoReconnectTests
    {
        private GameObject _reconnectObject;
        private EOSAutoReconnect _reconnect;

        [SetUp]
        public void SetUp()
        {
            _reconnectObject = new GameObject("TestReconnect");
            _reconnect = _reconnectObject.AddComponent<EOSAutoReconnect>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_reconnectObject != null)
            {
                Object.Destroy(_reconnectObject);
            }
        }

        [UnityTest]
        public IEnumerator AutoReconnect_InstanceIsSet()
        {
            yield return null;

            Assert.IsNotNull(EOSAutoReconnect.Instance);
        }

        [UnityTest]
        public IEnumerator AutoReconnect_DefaultSettingsAreCorrect()
        {
            yield return null;

            Assert.IsTrue(_reconnect.Enabled);
            Assert.AreEqual(5, _reconnect.MaxAttempts);
            Assert.AreEqual(2f, _reconnect.InitialRetryDelay);
            Assert.IsTrue(_reconnect.UseExponentialBackoff);
            Assert.IsTrue(_reconnect.PreserveSession);
            Assert.AreEqual(120f, _reconnect.SlotReservationTime);
        }

        [UnityTest]
        public IEnumerator AutoReconnect_StartsNotReconnecting()
        {
            yield return null;

            Assert.IsFalse(_reconnect.IsReconnecting);
            Assert.AreEqual(0, _reconnect.CurrentAttempt);
        }

        [UnityTest]
        public IEnumerator AutoReconnect_SessionDataHelpers()
        {
            yield return null;

            // These should not throw without a session
            _reconnect.SetSessionData("test", "value");
            var result = _reconnect.GetSessionData("test");

            Assert.IsNull(result); // No session created yet
        }
    }

    /// <summary>
    /// Play Mode tests for session data.
    /// </summary>
    [TestFixture]
    public class ReconnectSessionDataTests
    {
        [Test]
        public void SessionData_GeneratesToken()
        {
            var session = new ReconnectSessionData();

            Assert.IsNotNull(session.ReconnectToken);
            Assert.AreEqual(16, session.ReconnectToken.Length);
        }

        [Test]
        public void SessionData_CustomDataIsInitialized()
        {
            var session = new ReconnectSessionData();

            Assert.IsNotNull(session.CustomData);
            Assert.AreEqual(0, session.CustomData.Count);
        }

        [Test]
        public void SessionData_CanStoreCustomData()
        {
            var session = new ReconnectSessionData();
            session.CustomData["loadout"] = "assault";
            session.CustomData["score"] = "100";

            Assert.AreEqual("assault", session.CustomData["loadout"]);
            Assert.AreEqual("100", session.CustomData["score"]);
        }
    }
}
