using NUnit.Framework;
using FishNet.Transport.EOSNative;

namespace FishNet.Transport.EOSNative.Tests.Editor
{
    /// <summary>
    /// Edit Mode tests for LobbyOptions fluent API.
    /// Run via Window > General > Test Runner > Edit Mode
    /// </summary>
    [TestFixture]
    public class LobbyOptionsTests
    {
        [Test]
        public void WithGameMode_SetsGameMode()
        {
            var options = new LobbyOptions().WithGameMode("ranked");
            Assert.AreEqual("ranked", options.GameMode);
        }

        [Test]
        public void WithName_SetsLobbyName()
        {
            var options = new LobbyOptions().WithName("Test Lobby");
            Assert.AreEqual("Test Lobby", options.LobbyName);
        }

        [Test]
        public void WithMaxPlayers_SetsMaxPlayers()
        {
            var options = new LobbyOptions().WithMaxPlayers(8);
            Assert.AreEqual(8, options.MaxPlayers);
        }

        [Test]
        public void WithRegion_SetsRegion()
        {
            var options = new LobbyOptions().WithRegion("us-east");
            Assert.AreEqual("us-east", options.Region);
        }

        [Test]
        public void WithPassword_SetsPasswordAndFlag()
        {
            var options = new LobbyOptions().WithPassword("secret");
            Assert.AreEqual("secret", options.Password);
            Assert.IsTrue(options.IsPassworded);
        }

        [Test]
        public void FluentChaining_SetsAllProperties()
        {
            var options = new LobbyOptions()
                .WithName("Pro Players")
                .WithGameMode("deathmatch")
                .WithRegion("eu-west")
                .WithMaxPlayers(16);

            Assert.AreEqual("Pro Players", options.LobbyName);
            Assert.AreEqual("deathmatch", options.GameMode);
            Assert.AreEqual("eu-west", options.Region);
            Assert.AreEqual(16, options.MaxPlayers);
        }

        [Test]
        public void DefaultValues_AreCorrect()
        {
            var options = new LobbyOptions();

            Assert.IsNull(options.GameMode);
            Assert.IsNull(options.Region);
            Assert.IsNull(options.LobbyName);
            Assert.IsNull(options.Password);
            Assert.IsFalse(options.IsPassworded);
        }
    }

    /// <summary>
    /// Edit Mode tests for LobbySearchOptions.
    /// </summary>
    [TestFixture]
    public class LobbySearchOptionsTests
    {
        [Test]
        public void ExcludeFull_SetsExcludeFullLobbies()
        {
            var options = new LobbyOptions().ExcludeFull();
            Assert.IsTrue(options.ExcludeFullLobbies);
        }

        [Test]
        public void ExcludePassworded_SetsExcludePassworded()
        {
            var options = new LobbyOptions().ExcludePassworded();
            Assert.IsTrue(options.ExcludePasswordedLobbies);
        }

        [Test]
        public void WithMaxResults_SetsMaxResults()
        {
            var options = new LobbyOptions().WithMaxResults(50);
            Assert.AreEqual(50, options.MaxResults);
        }
    }

    /// <summary>
    /// Edit Mode tests for EOSInputHelper utilities.
    /// </summary>
    [TestFixture]
    public class InputHelperTests
    {
        [Test]
        public void GetInputTypeId_ReturnsCorrectIds()
        {
            Assert.AreEqual("KBM", EOSInputHelper.GetInputTypeId(InputType.KeyboardMouse));
            Assert.AreEqual("CTL", EOSInputHelper.GetInputTypeId(InputType.Controller));
            Assert.AreEqual("TCH", EOSInputHelper.GetInputTypeId(InputType.Touch));
            Assert.AreEqual("VRC", EOSInputHelper.GetInputTypeId(InputType.VRController));
            Assert.AreEqual("UNK", EOSInputHelper.GetInputTypeId(InputType.Unknown));
        }

        [Test]
        public void GetInputTypeName_ReturnsReadableNames()
        {
            Assert.AreEqual("Keyboard & Mouse", EOSInputHelper.GetInputTypeName(InputType.KeyboardMouse));
            Assert.AreEqual("Controller", EOSInputHelper.GetInputTypeName(InputType.Controller));
            Assert.AreEqual("Touch", EOSInputHelper.GetInputTypeName(InputType.Touch));
            Assert.AreEqual("VR Controller", EOSInputHelper.GetInputTypeName(InputType.VRController));
        }

        [Test]
        public void AreInputTypesFair_ReturnsTrueForSameType()
        {
            Assert.IsTrue(EOSInputHelper.AreInputTypesFair(InputType.KeyboardMouse, InputType.KeyboardMouse));
            Assert.IsTrue(EOSInputHelper.AreInputTypesFair(InputType.Controller, InputType.Controller));
        }

        [Test]
        public void AreInputTypesFair_ReturnsTrueForControllerAndTouch()
        {
            Assert.IsTrue(EOSInputHelper.AreInputTypesFair(InputType.Controller, InputType.Touch));
            Assert.IsTrue(EOSInputHelper.AreInputTypesFair(InputType.Touch, InputType.Controller));
        }

        [Test]
        public void AreInputTypesFair_ReturnsFalseForKbmVsController()
        {
            Assert.IsFalse(EOSInputHelper.AreInputTypesFair(InputType.KeyboardMouse, InputType.Controller));
            Assert.IsFalse(EOSInputHelper.AreInputTypesFair(InputType.Controller, InputType.KeyboardMouse));
        }
    }

    /// <summary>
    /// Edit Mode tests for achievement data structures.
    /// </summary>
    [TestFixture]
    public class AchievementDataTests
    {
        [Test]
        public void AchievementDefinition_StoresValues()
        {
            var def = new AchievementDefinition
            {
                Id = "first_blood",
                DisplayName = "First Blood",
                Description = "Get your first kill",
                IsHidden = false
            };

            Assert.AreEqual("first_blood", def.Id);
            Assert.AreEqual("First Blood", def.DisplayName);
            Assert.AreEqual("Get your first kill", def.Description);
            Assert.IsFalse(def.IsHidden);
        }

        [Test]
        public void PlayerAchievementData_CalculatesUnlockStatus()
        {
            var unlocked = new PlayerAchievementData
            {
                Id = "test",
                Progress = 1.0,
                IsUnlocked = true,
                UnlockTime = System.DateTimeOffset.Now
            };

            var inProgress = new PlayerAchievementData
            {
                Id = "test2",
                Progress = 0.5,
                IsUnlocked = false
            };

            Assert.IsTrue(unlocked.IsUnlocked);
            Assert.AreEqual(1.0, unlocked.Progress);
            Assert.IsFalse(inProgress.IsUnlocked);
            Assert.AreEqual(0.5, inProgress.Progress);
        }
    }

    /// <summary>
    /// Edit Mode tests for ping system enums and data.
    /// </summary>
    [TestFixture]
    public class PingSystemTests
    {
        [Test]
        public void GetPingIcon_ReturnsCorrectIcons()
        {
            Assert.AreEqual("!", EOSPingManager.GetPingIcon(PingType.Default));
            Assert.AreEqual("X", EOSPingManager.GetPingIcon(PingType.Enemy));
            Assert.AreEqual("!!", EOSPingManager.GetPingIcon(PingType.Danger));
            Assert.AreEqual("*", EOSPingManager.GetPingIcon(PingType.Item));
            Assert.AreEqual("?", EOSPingManager.GetPingIcon(PingType.Help));
        }

        [Test]
        public void GetPingTypeName_ReturnsReadableNames()
        {
            Assert.AreEqual("Ping", EOSPingManager.GetPingTypeName(PingType.Default));
            Assert.AreEqual("Enemy", EOSPingManager.GetPingTypeName(PingType.Enemy));
            Assert.AreEqual("Danger", EOSPingManager.GetPingTypeName(PingType.Danger));
            Assert.AreEqual("Item", EOSPingManager.GetPingTypeName(PingType.Item));
            Assert.AreEqual("Need Help", EOSPingManager.GetPingTypeName(PingType.Help));
        }

        [Test]
        public void PingData_GeneratesUniqueId()
        {
            var ping1 = new PingData();
            var ping2 = new PingData();

            Assert.IsNotNull(ping1.PingId);
            Assert.IsNotNull(ping2.PingId);
            Assert.AreNotEqual(ping1.PingId, ping2.PingId);
        }

        [Test]
        public void PingData_DefaultVisibilityIsTeam()
        {
            var ping = new PingData();
            Assert.AreEqual(PingVisibility.Team, ping.Visibility);
        }
    }

    /// <summary>
    /// Edit Mode tests for reputation system.
    /// </summary>
    [TestFixture]
    public class ReputationTests
    {
        [Test]
        public void GetLevelName_ReturnsCorrectNames()
        {
            Assert.AreEqual("Exemplary", EOSReputationManager.GetLevelName(ReputationLevel.Exemplary));
            Assert.AreEqual("Excellent", EOSReputationManager.GetLevelName(ReputationLevel.Excellent));
            Assert.AreEqual("Good", EOSReputationManager.GetLevelName(ReputationLevel.Good));
            Assert.AreEqual("Neutral", EOSReputationManager.GetLevelName(ReputationLevel.Neutral));
            Assert.AreEqual("Caution", EOSReputationManager.GetLevelName(ReputationLevel.Caution));
            Assert.AreEqual("Poor", EOSReputationManager.GetLevelName(ReputationLevel.Poor));
            Assert.AreEqual("Restricted", EOSReputationManager.GetLevelName(ReputationLevel.Restricted));
        }

        [Test]
        public void GetLevelIcon_ReturnsCorrectIcons()
        {
            Assert.AreEqual("++", EOSReputationManager.GetLevelIcon(ReputationLevel.Exemplary));
            Assert.AreEqual("+", EOSReputationManager.GetLevelIcon(ReputationLevel.Excellent));
            Assert.AreEqual("o", EOSReputationManager.GetLevelIcon(ReputationLevel.Good));
            Assert.AreEqual("~", EOSReputationManager.GetLevelIcon(ReputationLevel.Neutral));
            Assert.AreEqual("-", EOSReputationManager.GetLevelIcon(ReputationLevel.Caution));
            Assert.AreEqual("--", EOSReputationManager.GetLevelIcon(ReputationLevel.Poor));
            Assert.AreEqual("X", EOSReputationManager.GetLevelIcon(ReputationLevel.Restricted));
        }
    }

    /// <summary>
    /// Edit Mode tests for highlight system.
    /// </summary>
    [TestFixture]
    public class HighlightTests
    {
        [Test]
        public void ReplayHighlight_GeneratesUniqueId()
        {
            var h1 = new ReplayHighlight("Test 1", 0f, HighlightType.Manual, HighlightImportance.Medium);
            var h2 = new ReplayHighlight("Test 2", 1f, HighlightType.Manual, HighlightImportance.Medium);

            Assert.IsNotNull(h1.Id);
            Assert.IsNotNull(h2.Id);
            Assert.AreNotEqual(h1.Id, h2.Id);
        }

        [Test]
        public void ReplayHighlight_StoresValues()
        {
            var highlight = new ReplayHighlight(
                "Triple Kill!",
                45.5f,
                HighlightType.MultiKill,
                HighlightImportance.High
            );

            Assert.AreEqual("Triple Kill!", highlight.Title);
            Assert.AreEqual(45.5f, highlight.Timestamp);
            Assert.AreEqual(HighlightType.MultiKill, highlight.Type);
            Assert.AreEqual(HighlightImportance.High, highlight.Importance);
        }

        [Test]
        public void ReplayHighlight_CustomDataIsInitialized()
        {
            var highlight = new ReplayHighlight("Test", 0f, HighlightType.Manual, HighlightImportance.Low);
            Assert.IsNotNull(highlight.CustomData);
            Assert.AreEqual(0, highlight.CustomData.Count);
        }
    }

    /// <summary>
    /// Edit Mode tests for backfill/JIP system.
    /// </summary>
    [TestFixture]
    public class BackfillTests
    {
        [Test]
        public void GetPhaseName_ReturnsCorrectNames()
        {
            Assert.AreEqual("Lobby", EOSBackfillManager.GetPhaseName(GamePhase.Lobby));
            Assert.AreEqual("Loading", EOSBackfillManager.GetPhaseName(GamePhase.Loading));
            Assert.AreEqual("Warmup", EOSBackfillManager.GetPhaseName(GamePhase.Warmup));
            Assert.AreEqual("In Progress", EOSBackfillManager.GetPhaseName(GamePhase.InProgress));
            Assert.AreEqual("Overtime", EOSBackfillManager.GetPhaseName(GamePhase.Overtime));
            Assert.AreEqual("Post Game", EOSBackfillManager.GetPhaseName(GamePhase.PostGame));
        }

        [Test]
        public void GetJipResultMessage_ReturnsCorrectMessages()
        {
            Assert.AreEqual("Join allowed", EOSBackfillManager.GetJipResultMessage(JoinInProgressResult.Allowed));
            Assert.AreEqual("Game is full", EOSBackfillManager.GetJipResultMessage(JoinInProgressResult.Denied_Full));
            Assert.AreEqual("Game is locked", EOSBackfillManager.GetJipResultMessage(JoinInProgressResult.Denied_Locked));
        }

        [Test]
        public void BackfillRequest_GeneratesUniqueId()
        {
            var r1 = new BackfillRequest();
            var r2 = new BackfillRequest();

            Assert.IsNotNull(r1.RequestId);
            Assert.IsNotNull(r2.RequestId);
            Assert.AreNotEqual(r1.RequestId, r2.RequestId);
        }
    }

    /// <summary>
    /// Edit Mode tests for rematch system.
    /// </summary>
    [TestFixture]
    public class RematchTests
    {
        [Test]
        public void GetStateName_ReturnsCorrectNames()
        {
            Assert.AreEqual("No Rematch", EOSRematchManager.GetStateName(RematchState.None));
            Assert.AreEqual("Voting", EOSRematchManager.GetStateName(RematchState.Proposed));
            Assert.AreEqual("Accepted", EOSRematchManager.GetStateName(RematchState.Accepted));
            Assert.AreEqual("Declined", EOSRematchManager.GetStateName(RematchState.Declined));
            Assert.AreEqual("Starting", EOSRematchManager.GetStateName(RematchState.Starting));
            Assert.AreEqual("Cancelled", EOSRematchManager.GetStateName(RematchState.Cancelled));
        }

        [Test]
        public void RematchData_TracksVotes()
        {
            var data = new RematchData();
            data.EligiblePlayers.Add("player1");
            data.EligiblePlayers.Add("player2");
            data.EligiblePlayers.Add("player3");
            data.RequiredVotes = 2;

            data.VotesYes.Add("player1");

            Assert.AreEqual(1, data.YesCount);
            Assert.AreEqual(0, data.NoCount);
            Assert.IsFalse(data.HasEnoughYes);

            data.VotesYes.Add("player2");

            Assert.AreEqual(2, data.YesCount);
            Assert.IsTrue(data.HasEnoughYes);
        }
    }
}
