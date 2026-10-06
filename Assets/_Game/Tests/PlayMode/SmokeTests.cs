using System.Collections;
using System.IO;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Template.Core.Save;
using Template.Game.Flow;
using Template.Infra;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NightCourier.PlayModeTests
{
    /// <summary>
    /// End-to-end check of the real game: boot, title, a ride steered by the autopilot through the actual
    /// RideController, pause, a crash and the results, then a 300-drone stress ride. Any error or exception
    /// logged fails the test. Screenshots go to Logs/screenshots (render with graphics, i.e. without -nographics).
    /// </summary>
    public class SmokeTests
    {
        [TearDown]
        public void TearDown()
        {
            RideController.Autopilot = false;
            RideController.StressDrones = 0;
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator Boots_rides_crashes_and_shows_results_without_errors()
        {
            SceneManager.LoadScene("Boot");
            yield return WaitForScene("Title", 20f);
            yield return new WaitForSeconds(0.6f);
            Capture("1-title");

            // The garage opens over Home and closes again.
            ClickButton("Button Garage");
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("1b-garage");
            ClickButton("Button Close");
            yield return new WaitForSecondsRealtime(0.2f);

            RideController.Autopilot = true;
            Services.Get<GameFlow>().GoToAsync(AppState.Game).Forget();
            yield return WaitForScene("Game", 20f);
            var controller = Object.FindAnyObjectByType<RideController>();
            Assert.IsNotNull(controller, "RideController should exist in the Game scene");

            yield return new WaitForSeconds(6f);
            var ride = controller.Ride;
            Assert.Greater(ride.Time, 5f, "the ride runs in the real game loop");
            Assert.Greater(ride.Bike.Position.Length(), 10f, "the bike keeps moving");
            Assert.GreaterOrEqual(ride.Swarm.Count, 30, "drones keep spawning");
            var ahead = ride.Bike.Position + ride.Bike.Forward * 3.5f; // an elite in the shot (they come every minute)
            ride.Swarm.Spawn(NightCourier.Core.EnemyKind.Elite, ahead.X, ahead.Y, 1f, ride.Bike.Heading);
            var side = ride.Bike.Position + new System.Numerics.Vector2(-ride.Bike.Forward.Y, ride.Bike.Forward.X) * 4f;
            ride.Swarm.Spawn(NightCourier.Core.EnemyKind.Zapper, side.X, side.Y); // in range: an orb is in the air by the shot
            ride.Swarm.Spawn(NightCourier.Core.EnemyKind.Splitter, side.X + 1.5f, side.Y + 1.5f);
            yield return new WaitForSeconds(0.3f);
            Capture("2-riding");

            Call(controller, "Pause");
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("3-pause");
            float pausedAt = ride.Time;
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(pausedAt, ride.Time, "nothing moves while paused");
            Call(controller, "Resume");

            // A level-up: a level's worth of XP under the wheels, the cards come up, the first one is tapped.
            RideController.Autopilot = false;
            ride.Parcels.Add(ride.Bike.Position.X, ride.Bike.Position.Y, ride.XpNeeded);
            float waitedForCards = 0f;
            while (ride.PendingLevelUps == 0 && waitedForCards < 2f)
            {
                waitedForCards += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, ride.PendingLevelUps, "collecting the XP levels up");
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("4-level-up");
            float frozenAt = ride.Time;
            var picked = ride.Offer[0];
            int levelBefore = ride.Loadout.Level(picked);
            var card = FirstCard();
            Assert.IsNotNull(card, "the level-up cards are on screen");
            Assert.AreEqual(frozenAt, ride.Time, "the ride waits for a card");
            card.onClick.Invoke();
            Assert.AreEqual(levelBefore + 1, ride.Loadout.Level(picked));
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(ride.Time, frozenAt, "the ride carries on after the pick");

            // Let go of the stick: riding straight into the swarm ends the shift. Fast-forward to get there.
            RideController.Autopilot = false;
            Time.timeScale = 4f;
            float waited = 0f;
            while (!controller.IsOver && waited < 30f)
            {
                if (ride.PendingLevelUps > 0)
                {
                    FirstCard()?.onClick.Invoke(); // keep riding through level-ups
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(controller.IsOver, $"riding straight should crash; HP {ride.Hp} after {ride.Time:0.0} s");
            yield return new WaitForSecondsRealtime(1.2f); // slow motion, then the results
            Capture("5-results");
            var save = Services.Get<SaveService>().Data;
            Assert.Greater(save.totalRuns, 0, "the ride should be saved");
            Assert.GreaterOrEqual(save.bestScore, (int)ride.Time, "the best time covers this ride");

            // The profiling ride: 300 drones, no damage, the autopilot steering. Logs the editor's frame time.
            RideController.StressDrones = 300;
            RideController.Autopilot = true;
            Call(controller, "NewRide");
            Assert.AreEqual(300, controller.Ride.Swarm.Count);
            yield return new WaitForSeconds(2f);
            int frames = 0;
            float seconds = 0f;
            while (seconds < 2f)
            {
                frames++;
                seconds += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.Log($"[Smoke] 300 drones in the editor: {seconds * 1000f / frames:0.0} ms per frame over {frames} frames");
            Capture("6-stress-300");
            Assert.IsFalse(controller.IsOver, "stress rides take no damage");

            // The Dispatcher: in the shot with its orb fans, then down: the shift is won.
            var boss = controller.Ride;
            var front = boss.Bike.Position + boss.Bike.Forward * 6f;
            int b = boss.Swarm.Spawn(NightCourier.Core.EnemyKind.Boss, front.X, front.Y);
            boss.Swarm.Hp[b] = 1e6f;
            yield return new WaitForSeconds(1.8f);
            Capture("6b-boss");
            for (int i = 0; i < boss.Swarm.Count; i++)
            {
                if (boss.Swarm.Kind[i] == NightCourier.Core.EnemyKind.Boss)
                {
                    boss.Swarm.Hp[i] = 0f;
                }
            }

            float waitedForWin = 0f;
            while (!controller.IsOver && waitedForWin < 3f)
            {
                waitedForWin += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsTrue(boss.Won, "downing the boss wins the shift");
            yield return new WaitForSecondsRealtime(1.2f);
            Capture("6c-delivered");

            Services.Get<GameFlow>().GoToAsync(AppState.Title).Forget();
            yield return WaitForScene("Title", 20f);
            yield return new WaitForSeconds(0.6f);
            Capture("7-title-after-ride");
        }

        private static void ClickButton(string name)
        {
            var button = System.Linq.Enumerable.FirstOrDefault(Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None),
                b => b.name == name && b.gameObject.activeInHierarchy);
            Assert.IsNotNull(button, $"{name} is on screen");
            button.onClick.Invoke();
        }

        private static UnityEngine.UI.Button FirstCard() =>
            System.Linq.Enumerable.FirstOrDefault(Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None),
                b => b.name == "Card 0" && b.gameObject.activeInHierarchy);

        private static IEnumerator WaitForScene(string name, float timeout)
        {
            float t = 0f;
            while (SceneManager.GetActiveScene().name != name)
            {
                t += Time.unscaledDeltaTime;
                if (t > timeout)
                {
                    Assert.Fail($"Timed out waiting for scene {name}; active is {SceneManager.GetActiveScene().name}");
                }

                yield return null;
            }
        }

        /// <summary>Saves the screen at three shapes: 9:16 phone, 20:9 tall phone, 4:3 tablet.</summary>
        private static void Capture(string name)
        {
            string root = CameraShot.Folder("screenshots");
            CameraShot.Save(Path.Combine(root, name + ".png"));
            CameraShot.Save(Path.Combine(root, "tall", name + ".png"), 540, 1200);
            CameraShot.Save(Path.Combine(root, "tablet", name + ".png"), 768, 1024);
        }

        /// <summary>Calls a private method (controllers keep their handlers private).</summary>
        private static void Call(object target, string method)
        {
            var info = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{method} not found");
            info.Invoke(target, null);
        }
    }
}
