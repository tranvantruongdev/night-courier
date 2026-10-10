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
        private sealed class MemorySaveStore : ISaveStore
        {
            private string _primary;
            private string _temp;
            private string _backup;
            public int Writes;

            public bool TryLoad(out string json) { json = _primary; return _primary != null; }
            public bool TryLoadTemp(out string json) { json = _temp; return _temp != null; }
            public bool TryLoadBackup(out string json) { json = _backup; return _backup != null; }
            public void Save(string json, string lastKnownGoodJson)
            {
                if (lastKnownGoodJson != null) _backup = lastKnownGoodJson;
                _temp = json;
                _primary = _temp;
                _temp = null;
                Writes++;
            }
            public void Delete() { _primary = null; _temp = null; _backup = null; }
        }

        private SaveService _originalSaveService;
        private SaveService _testSaveService;
        private MemorySaveStore _testStore;

        [TearDown]
        public void TearDown()
        {
            RideController.Autopilot = false;
            RideController.StressDrones = 0;
            Time.timeScale = 1f;
            if (_originalSaveService != null)
            {
                Services.Register(_originalSaveService);
                _originalSaveService = null;
            }
        }

        [UnityTest]
        public IEnumerator Back_during_result_delay_keeps_win_settlement()
        {
            yield return StartGameWithSandboxSave();
            var controller = Object.FindAnyObjectByType<RideController>();
            Assert.IsNotNull(controller);
            var ride = controller.Ride;
            MarkRideWon(ride);
            var before = _testSaveService.Data.GetGame<NightCourier.Core.GarageData>();
            int earned = NightCourier.Core.Garage.CoinsFor(ride);
            int runs = _testSaveService.Data.totalRuns;

            Call(controller, "OnShiftEnded", true);
            Call(controller, "OnBack");
            yield return WaitForScene("Title", 20f);
            yield return WaitForFlowIdle(Services.Get<GameFlow>(), 20f);

            _testSaveService.Load();
            var after = _testSaveService.Data.GetGame<NightCourier.Core.GarageData>();
            Assert.AreEqual(runs + 1, _testSaveService.Data.totalRuns);
            Assert.AreEqual(before.coins + earned, after.coins);
            Assert.IsTrue(after.CanRide(NightCourier.Core.MapKind.HarborRing));
            Assert.IsTrue(after.rodeOnce);
            Assert.AreEqual(1, _testStore.Writes);
        }

        [UnityTest]
        public IEnumerator Duplicate_terminal_callback_does_not_duplicate_rewards()
        {
            yield return StartGameWithSandboxSave();
            var controller = Object.FindAnyObjectByType<RideController>();
            Assert.IsNotNull(controller);
            MarkRideWon(controller.Ride);

            Call(controller, "OnShiftEnded", true);
            Call(controller, "OnShiftEnded", true);
            Assert.AreEqual(1, _testSaveService.Data.totalRuns);
            Assert.AreEqual(1, _testStore.Writes);
            Assert.IsTrue(_testSaveService.Data.GetGame<NightCourier.Core.GarageData>().CanRide(NightCourier.Core.MapKind.HarborRing));

            yield return new WaitForSecondsRealtime(0.8f);
            Call(controller, "OnBack");
            yield return WaitForScene("Title", 20f);
            yield return WaitForFlowIdle(Services.Get<GameFlow>(), 20f);
        }

        private IEnumerator StartGameWithSandboxSave()
        {
            if (SceneManager.GetActiveScene().name != "Title" ||
                !Services.TryGet<GameFlow>(out _) || !Services.TryGet<SaveService>(out _))
            {
                SceneManager.LoadScene("Boot");
                yield return WaitForScene("Title", 20f);
                yield return WaitForFlowIdle(Services.Get<GameFlow>(), 20f);
            }

            _originalSaveService = Services.Get<SaveService>();
            _testStore = new MemorySaveStore();
            _testSaveService = new SaveService(_testStore, new SaveCodec(SaveSchema.CreateMigrator()));
            _testSaveService.Load();
            _testSaveService.Data.SetGame(new NightCourier.Core.GarageData
            {
                coins = 50,
                map = NightCourier.Core.MapKind.MarketStreet,
                rodeOnce = true,
            });
            Services.Register(_testSaveService);

            yield return Services.Get<GameFlow>().GoToAsync(AppState.Game).ToCoroutine();
            yield return WaitForScene("Game", 20f);
        }

        private static void MarkRideWon(NightCourier.Core.Ride ride)
        {
            var setter = typeof(NightCourier.Core.Ride).GetProperty(nameof(NightCourier.Core.Ride.Won)).GetSetMethod(true);
            Assert.IsNotNull(setter, "Ride.Won setter should be accessible to the PlayMode fixture");
            setter.Invoke(ride, new object[] { true });
        }

        [UnityTest]
        public IEnumerator Boots_rides_crashes_and_shows_results_without_errors()
        {
            SceneManager.LoadScene("Boot");
            yield return WaitForScene("Title", 20f);
            yield return new WaitForSeconds(0.6f);
            Capture("1-title");

            // Home in Japanese and Vietnamese (rebuilt per language), then back to the developer's language.
            var settingsService = Services.Get<Template.Infra.Settings.SettingsService>();
            string language = settingsService.Current.language;
            foreach (var code in new[] { "ja", "vi" })
            {
                settingsService.Current.language = code;
                yield return Services.Get<GameFlow>().GoToAsync(AppState.Title).ToCoroutine();
                yield return new WaitForSeconds(0.6f);
                Capture("1-title-" + code);
            }

            settingsService.Current.language = language;
            yield return Services.Get<GameFlow>().GoToAsync(AppState.Title).ToCoroutine();
            yield return new WaitForSeconds(0.4f);

            // The garage opens over Home and closes again.
            ClickButton("Button Garage");
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("1b-garage");
            ClickButton("Button Close");
            yield return new WaitForSecondsRealtime(0.2f);
            ClickButton("Button Ride");
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("1d-maps");
            ClickButton("Button Back");
            yield return new WaitForSecondsRealtime(0.2f);
            ClickButton("Button Controls");
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("1e-controls");
            ClickButton("Button Close");
            yield return new WaitForSecondsRealtime(0.2f);
            ClickButton("Button Codex");
            yield return new WaitForSecondsRealtime(0.3f);
            Capture("1c-codex");
            ClickButton("Button Close");
            yield return new WaitForSecondsRealtime(0.2f);

            // A first ride on Market Street (the save keeps earlier runs), so the first-run tips show.
            var firstSave = Services.Get<SaveService>();
            var firstGarage = firstSave.Data.GetGame<NightCourier.Core.GarageData>();
            firstGarage.rodeOnce = false;
            firstGarage.map = NightCourier.Core.MapKind.MarketStreet;
            firstSave.Data.SetGame(firstGarage);
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
            // A maxed headlight with Gear Ratio: the cards should include High Beam's EVOLVE card (weighted 50).
            for (int i = ride.Loadout.Level(NightCourier.Core.ItemKind.Headlight); i < NightCourier.Core.Loadout.MaxLevel; i++)
            {
                ride.Loadout.Upgrade(NightCourier.Core.ItemKind.Headlight);
            }

            if (!ride.Loadout.Owns(NightCourier.Core.ItemKind.GearRatio))
            {
                ride.Loadout.Upgrade(NightCourier.Core.ItemKind.GearRatio);
            }

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
            if (picked == NightCourier.Core.ItemKind.Headlight)
            {
                Assert.IsTrue(ride.Loadout.Evolved(picked), "the EVOLVE card evolves instead of levelling");
            }
            else
            {
                Assert.AreEqual(levelBefore + 1, ride.Loadout.Level(picked));
            }
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(ride.Time, frozenAt, "the ride carries on after the pick");

            // The 1:00 elite brought forward: on a first ride its arrival shows the drafting tip.
            var nextElite = typeof(NightCourier.Core.Ride).GetField("_nextElite", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(nextElite, "Ride._nextElite not found");
            nextElite.SetValue(ride, ride.Time);
            yield return new WaitForSeconds(0.6f);
            Capture("4b-draft-tip");

            // Let go of the stick: riding straight into the swarm ends the shift. Fast-forward to get there.
            RideController.Autopilot = false;
            Time.timeScale = 4f;
            // The High Beam above makes this build too strong to die on its own in time: a ring of unkillable haulers
            // around the bike ends the shift.
            // Every weapon, results in Vietnamese: the full damage chart with long names (they auto-size to one line).
            // The buttons stay English: they were built when the ride started.
            for (var w = NightCourier.Core.ItemKind.SpokeCards; w <= NightCourier.Core.ItemKind.PannierDrone; w++)
            {
                ride.Loadout.Upgrade(w);
            }

            settingsService.Current.language = "vi";
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI * 2f / 24f;
                int h = ride.Swarm.Spawn(NightCourier.Core.EnemyKind.Hauler, ride.Bike.Position.X + Mathf.Cos(a) * 1.2f, ride.Bike.Position.Y + Mathf.Sin(a) * 1.2f);
                if (h >= 0)
                {
                    ride.Swarm.Hp[h] = 1e6f;
                }
            }
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
            Capture("5-results-vi");
            settingsService.Current.language = language;
            var save = Services.Get<SaveService>().Data;
            Assert.Greater(save.totalRuns, 0, "the ride should be saved");
            Assert.GreaterOrEqual(save.bestScore, (int)ride.Time, "the best time covers this ride");

            // The profiling ride: 300 drones, no damage, the autopilot steering. Logs the editor's frame time.
            // The stress ride and the boss on map 2 (opened in the save for the test): Harbor Ring and the Freight Hauler.
            var saveService = Services.Get<SaveService>();
            var garage = saveService.Data.GetGame<NightCourier.Core.GarageData>();
            garage.map = NightCourier.Core.MapKind.HarborRing;
            garage.wonMaps |= 1;
            saveService.Data.SetGame(garage);
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
            int b = boss.Swarm.Spawn(NightCourier.Core.EnemyKind.Freight, front.X, front.Y);
            boss.Swarm.Hp[b] = 1e6f;
            // Shot mid-charge (Timer > 0), the moment its slipstream drafts. Indices move as drones die, so find it each frame.
            bool charging = false;
            float waitedForCharge = 0f;
            while (!charging && waitedForCharge < 5f)
            {
                waitedForCharge += Time.deltaTime;
                yield return null;
                for (int i = 0; i < boss.Swarm.Count; i++)
                {
                    charging |= boss.Swarm.Kind[i] == NightCourier.Core.EnemyKind.Freight && boss.Swarm.Timer[i] > 0f;
                }
            }

            Assert.IsTrue(charging, "the Freight Hauler charges within 5 s");
            yield return new WaitForSeconds(0.25f);
            Capture("6b-boss");
            for (int i = 0; i < boss.Swarm.Count; i++)
            {
                if (NightCourier.Core.Swarm.IsBoss(boss.Swarm.Kind[i]))
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

            // Left-handed: the HUD mirrors its corners (speed gauge bottom-right, pause top-left). Restored after.
            var controlsSave = Services.Get<SaveService>();
            var controls = controlsSave.Data.GetGame<NightCourier.Core.GarageData>();
            controls.leftHanded = true;
            controlsSave.Data.SetGame(controls);
            settingsService.Current.language = "ja"; // and in Japanese: the widest card text
            RideController.Autopilot = false; // still on from the stress ride; it would pick the cards itself
            yield return Services.Get<GameFlow>().GoToAsync(AppState.Game).ToCoroutine();
            yield return new WaitForSeconds(0.5f);
            Capture("8-left-handed");
            var jaRide = Object.FindAnyObjectByType<RideController>().Ride;
            jaRide.Parcels.Add(jaRide.Bike.Position.X, jaRide.Bike.Position.Y, jaRide.XpNeeded);
            float waitedForJaCards = 0f;
            while (jaRide.PendingLevelUps == 0 && waitedForJaCards < 2f)
            {
                waitedForJaCards += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.AreEqual(1, jaRide.PendingLevelUps, "collecting the XP levels up");
            yield return new WaitForSecondsRealtime(0.4f);
            Capture("9-level-up-ja");
            // The offer is random, so check every card line in Japanese, not just the three on screen: one line each.
            var hud = typeof(RideController).GetField("_hud", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(Object.FindAnyObjectByType<RideController>());
            var line = ((TMPro.TMP_Text[])hud.GetType().GetField("_cardLines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud))[0];
            var lines = new System.Collections.Generic.List<string> { "Evolved: double damage, wider reach." };
            for (var item = NightCourier.Core.ItemKind.Headlight; item <= NightCourier.Core.ItemKind.EnergyGel; item++)
            {
                lines.Add(NightCourier.UI.ItemText.Line(item));
            }

            // Weapon names on the results chart, plain and evolved.
            var chartName = ((TMPro.TMP_Text[])hud.GetType().GetField("_chartNames", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud))[0];
            var names = new System.Collections.Generic.List<string>();
            for (var item = NightCourier.Core.ItemKind.Headlight; item <= NightCourier.Core.ItemKind.PannierDrone; item++)
            {
                names.Add(NightCourier.UI.ItemText.Name(item));
                names.Add(NightCourier.UI.ItemText.EvolvedName(item));
            }

            foreach (var code in new[] { "ja", "vi" })
            {
                settingsService.Current.language = code;
                foreach (var text in lines)
                {
                    line.text = Template.UI.UiFactory.Localize(text);
                    line.ForceMeshUpdate();
                    Assert.AreEqual(1, line.textInfo.lineCount, $"one line in {code}: {line.text}");
                }

                // The results panel is hidden, so no mesh: measure the width at the smallest auto-size instead.
                chartName.fontSize = chartName.fontSizeMin;
                foreach (var text in names)
                {
                    string localized = Template.UI.UiFactory.Localize(text);
                    float width = chartName.GetPreferredValues(localized).x;
                    Assert.Greater(width, 0f, "measured, not an empty layout");
                    Assert.LessOrEqual(width, chartName.rectTransform.rect.width,
                        $"chart name fits in {code}: {localized}");
                }
            }

            FirstCard().onClick.Invoke();
            settingsService.Current.language = language;
            controls.leftHanded = false;
            controlsSave.Data.SetGame(controls);
            yield return Services.Get<GameFlow>().GoToAsync(AppState.Title).ToCoroutine();
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

        private static IEnumerator WaitForFlowIdle(GameFlow flow, float timeout)
        {
            var transitioning = typeof(GameFlow).GetField("_transitioning", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(transitioning, "GameFlow transition state is available to the fixture");
            float deadline = Time.realtimeSinceStartup + timeout;
            while ((bool)transitioning.GetValue(flow) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse((bool)transitioning.GetValue(flow), "GameFlow finishes its scene transition");
        }
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
        private static void Call(object target, string method, params object[] arguments)
        {
            var info = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{method} not found");
            info.Invoke(target, arguments);
        }
    }
}
