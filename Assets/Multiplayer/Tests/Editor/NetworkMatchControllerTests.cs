using FPSBasic.Multiplayer.UI;
using NUnit.Framework;
using TMPro;
using Unity.FPS.Game;
using UnityEditor;
using UnityEngine;

namespace FPSBasic.Multiplayer.Tests
{
    public sealed class NetworkMatchControllerTests
    {
        [TestCase(3, 1, MatchResult.TeamAWin)]
        [TestCase(1, 3, MatchResult.TeamBWin)]
        [TestCase(2, 2, MatchResult.Draw)]
        public void DetermineResult_UsesFinalTeamScores(
            int teamAScore,
            int teamBScore,
            MatchResult expected)
        {
            Assert.AreEqual(
                expected,
                NetworkMatchController.DetermineResult(teamAScore, teamBScore));
        }

        [Test]
        public void RoomNetworkStatePrefab_ContainsFiveMinuteMatchController()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Multiplayer/Prefabs/RoomNetworkState.prefab");

            Assert.IsNotNull(prefab);
            NetworkMatchController controller = prefab.GetComponent<NetworkMatchController>();
            Assert.IsNotNull(controller);

            var serializedController = new SerializedObject(controller);
            Assert.AreEqual(
                300d,
                serializedController.FindProperty("matchDurationSeconds").doubleValue,
                0.001d);
        }

        [Test]
        public void GameHudPrefab_ContainsWiredRedBlueMatchHud()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FPS/Prefabs/UI/GameHUD.prefab");

            Assert.IsNotNull(prefab);
            MatchHUD matchHud = prefab.GetComponentInChildren<MatchHUD>(true);
            Assert.IsNotNull(matchHud);

            var serializedHud = new SerializedObject(matchHud);
            var timer = serializedHud.FindProperty("timerText").objectReferenceValue as TMP_Text;
            var teamA = serializedHud.FindProperty("teamAScoreText").objectReferenceValue as TMP_Text;
            var teamB = serializedHud.FindProperty("teamBScoreText").objectReferenceValue as TMP_Text;

            Assert.IsNotNull(timer);
            Assert.IsNotNull(teamA);
            Assert.IsNotNull(teamB);
            Assert.AreEqual("05:00", timer.text);
            Assert.That(teamA.color.r, Is.GreaterThan(teamA.color.b));
            Assert.That(teamB.color.b, Is.GreaterThan(teamB.color.r));
        }

        [Test]
        public void GameHudPrefab_ContainsWiredMatchResultScreen()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/FPS/Prefabs/UI/GameHUD.prefab");

            Assert.IsNotNull(prefab);
            MatchResultScreen resultScreen = prefab.GetComponent<MatchResultScreen>();
            Assert.IsNotNull(resultScreen);

            var serializedScreen = new SerializedObject(resultScreen);
            Assert.IsNotNull(serializedScreen.FindProperty("redWinSprite").objectReferenceValue);
            Assert.IsNotNull(serializedScreen.FindProperty("blueWinSprite").objectReferenceValue);
            Assert.IsNotNull(serializedScreen.FindProperty("drawSprite").objectReferenceValue);
        }

        [Test]
        public void CursorUnlockRequest_PrioritySurvivesNormalRelease()
        {
            CursorUnlockRequest.SetActive(false);
            CursorUnlockRequest.SetPriorityActive(false);

            try
            {
                CursorUnlockRequest.SetActive(true);
                CursorUnlockRequest.SetPriorityActive(true);

                CursorUnlockRequest.SetActive(false);

                Assert.IsTrue(CursorUnlockRequest.IsActive);

                CursorUnlockRequest.SetPriorityActive(false);
                Assert.IsFalse(CursorUnlockRequest.IsActive);
            }
            finally
            {
                CursorUnlockRequest.SetActive(false);
                CursorUnlockRequest.SetPriorityActive(false);
            }
        }
    }
}
