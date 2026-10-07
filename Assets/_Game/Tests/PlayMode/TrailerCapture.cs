using System.Collections;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using NightCourier.Core;
using NUnit.Framework;
using Template.Game.Flow;
using Template.Infra;
using Template.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace NightCourier.PlayModeTests
{
    /// <summary>
    /// Records the trailer's frames: a full build tearing through a dense swarm behind an elite, the Freight Hauler's
    /// charge on Harbor Ring, the results with the damage chart, and an end card. Explicit, so normal runs skip it:
    ///   Tools/run-unity-tests.ps1 -TestPlatform PlayMode -Graphics -TestFilter NightCourier.PlayModeTests.TrailerCapture
    /// then Tools/make-trailer.ps1 turns Logs/frames into docs/night-courier-trailer.mp4 and docs/night-courier.gif.
    /// Puts the developer's save back afterwards.
    /// </summary>
    [Explicit("Records trailer frames; run on demand")]
    public class TrailerCapture
    {
        private const int Fps = 30;
        private const int Width = 720;
        private const int Height = 1280;

        private int _frame;
        private string _folder;
        private string _savePath;
        private System.Collections.Generic.Dictionary<string, byte[]> _keptSave;

        [SetUp]
        public void SetUp()
        {
            _folder = CameraShot.Folder("frames");
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }

            Directory.CreateDirectory(_folder);
            _frame = 0;
            _savePath = new Template.Infra.Save.FileSaveStore().FilePath;
            _keptSave = SaveFiles().Where(File.Exists).ToDictionary(p => p, p => File.ReadAllBytes(p));
            Time.captureFramerate = Fps; // game time advances 1/30 s per frame, however long a frame takes to save
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            RideController.Autopilot = false;
            RideController.StressDrones = 0;
            foreach (var path in SaveFiles().Where(File.Exists))
            {
                File.Delete(path);
            }

            foreach (var kept in _keptSave)
            {
                File.WriteAllBytes(kept.Key, kept.Value);
            }
        }

        private string[] SaveFiles() => new[] { _savePath, _savePath + ".tmp", _savePath + ".bak" };

        [UnityTest]
        public IEnumerator Records_trailer_frames()
        {
            Assume.That(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "needs graphics (run without -nographics)");

            SceneManager.LoadScene("Boot");
            yield return WaitForScene("Title");

            // Harbor Ring opened in the save for the shoot.
            var save = Services.Get<Template.Core.Save.SaveService>();
            var garage = save.Data.GetGame<GarageData>();
            garage.map = MapKind.HarborRing;
            garage.wonMaps |= 1;
            garage.rodeOnce = true;
            save.Data.SetGame(garage);

            RideController.Autopilot = true;
            RideController.StressDrones = 220; // a dense, damage-free swarm: the shots never end early
            yield return Services.Get<GameFlow>().GoToAsync(AppState.Game).ToCoroutine();
            var controller = Object.FindAnyObjectByType<RideController>();
            var ride = controller.Ride;

            // A full build: maxed headlight evolved into High Beam, blades, bell, whip, spikes, the drone.
            foreach (var (item, levels) in new[] { (ItemKind.Headlight, 4), (ItemKind.GearRatio, 2), (ItemKind.SpokeCards, 4), (ItemKind.Bell, 3),
                         (ItemKind.ChainWhip, 3), (ItemKind.TyreSpikes, 3), (ItemKind.PannierDrone, 3) })
            {
                for (int i = 0; i < levels; i++)
                {
                    ride.Loadout.Upgrade(item);
                }
            }

            ride.Loadout.Evolve(ItemKind.Headlight);
            yield return Record(1.5f);

            // 0–6 s: an elite ahead to draft, the build shredding the swarm.
            var ahead = ride.Bike.Position + ride.Bike.Forward * 3f;
            ride.Swarm.Spawn(EnemyKind.Elite, ahead.X, ahead.Y, 1f, ride.Bike.Heading);
            yield return Record(5f);

            // 6–12 s: the Freight Hauler arrives and charges.
            var front = ride.Bike.Position + ride.Bike.Forward * 7f;
            int boss = ride.Swarm.Spawn(EnemyKind.Freight, front.X, front.Y);
            ride.Swarm.Hp[boss] = 1e6f;
            yield return Record(6f);

            // 12–16 s: down it goes, then the results and their damage chart.
            for (int i = 0; i < ride.Swarm.Count; i++)
            {
                if (Swarm.IsBoss(ride.Swarm.Kind[i]))
                {
                    ride.Swarm.Hp[i] = 0f;
                }
            }

            yield return Record(4f);

            // 16–18.5 s: the end card.
            EndCard();
            yield return Record(2.5f);
        }

        private static void EndCard()
        {
            var canvas = UiFactory.CreateCanvas("End card", 2000);
            UiFactory.CreatePanel(canvas.transform, new Color(0.04f, 0.06f, 0.15f, 1f));
            UiFactory.CreateText(canvas.transform, "Night Courier", 120, new Vector2(0f, 160f), new Vector2(1000f, 200f),
                TextAlignmentOptions.Center, UiFont.Display).color = new Color(0.24f, 0.95f, 1f);
            UiFactory.CreateText(canvas.transform, "You can't stop pedalling.", 56, new Vector2(0f, 20f), new Vector2(980f, 100f)).color = Color.white;
            UiFactory.CreateText(canvas.transform, "Unity 6 · pure C# core · 300 drones on a spatial grid, 0 B garbage a frame", 34,
                new Vector2(0f, -110f), new Vector2(980f, 110f)).color = new Color(1f, 1f, 1f, 0.7f);
            UiFactory.CreateText(canvas.transform, "github.com/tranvantruongdev/night-courier", 40, new Vector2(0f, -260f), new Vector2(1000f, 70f))
                .color = new Color(1f, 0.82f, 0.24f);
        }

        private static IEnumerator WaitForScene(string name)
        {
            for (int i = 0; i < Fps * 30 && SceneManager.GetActiveScene().name != name; i++)
            {
                yield return null;
            }

            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
            yield return null;
        }

        private IEnumerator Record(float seconds)
        {
            for (int i = 0; i < Mathf.RoundToInt(seconds * Fps); i++)
            {
                yield return null; // CameraShot renders the camera itself, so any point in the frame works
                CameraShot.Save(Path.Combine(_folder, $"frame_{_frame++:D4}.tga"), Width, Height);
            }
        }
    }
}
