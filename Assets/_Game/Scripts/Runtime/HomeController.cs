using Cysharp.Threading.Tasks;
using NightCourier.Art;
using NightCourier.Core;
using NightCourier.UI;
using Template.Core.Save;
using Template.Core.Settings;
using Template.Game.Flow;
using Template.Infra;
using Template.Infra.Settings;
using Template.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NightCourier
{
    /// <summary>
    /// The Title scene: best shift, coins, Ride, the Garage (permanent upgrades bought with coins) and Settings.
    /// </summary>
    public sealed class HomeController : MonoBehaviour
    {
        private static readonly GarageUpgrade[] Upgrades = { GarageUpgrade.MaxHp, GarageUpgrade.XpGain, GarageUpgrade.StartSpeed, GarageUpgrade.Reroll };

        private ScreenStack _stack;
        private SettingsPanelView _settingsView;
        private SettingsPresenter _settingsPresenter;
        private GameObject _garage;
        private TextMeshProUGUI _coins;
        private readonly TextMeshProUGUI[] _rowLabels = new TextMeshProUGUI[4];
        private readonly Button[] _rowButtons = new Button[4];
        private static readonly BikeModel[] Bikes = { BikeModel.Fixie, BikeModel.Racer, BikeModel.Cargo };
        private readonly Button[] _bikeButtons = new Button[3];
        private TextMeshProUGUI _bikeLabel;
        private GameObject _codex;
        private TextMeshProUGUI _codexText;
        private GameObject _maps;
        private Button _harborButton;
        private TextMeshProUGUI _harborNote;

        private void Start()
        {
            if (!BootGuard.EnsureBooted())
            {
                return;
            }

            UiFactory.EnsureEventSystem();
            Camera.main.backgroundColor = Palette.Night;
            var canvas = UiFactory.CreateCanvas("Home UI");
            _stack = canvas.gameObject.AddComponent<ScreenStack>();
            _stack.RootBackPressed += Application.Quit;
            var safe = UiFactory.CreateSafeArea(canvas.transform);

            var save = Services.Get<SaveService>();
            var title = UiFactory.CreateText(safe, "Night Courier", 110, new Vector2(0, 560), new Vector2(1000, 200), font: UiFont.Display);
            title.color = Palette.Bike;
            int best = save.Data.bestScore;
            UiFactory.CreateText(safe, $"{UiFactory.Localize("Best shift")} {best / 60}:{best % 60:00}", 52, new Vector2(0, 380), new Vector2(900, 90));
            _coins = UiFactory.CreateText(safe, "", 52, new Vector2(0, 290), new Vector2(900, 90));
            _coins.color = Palette.Bonus;

            UiFactory.CreateButton(safe, "Ride", new Vector2(0, -60), new Vector2(560, 170),
                OpenMaps);
            UiFactory.CreateButton(safe, "Garage", new Vector2(0, -240), new Vector2(560, 140), OpenGarage, ButtonStyle.Secondary);
            UiFactory.CreateButton(safe, "Codex", new Vector2(0, -400), new Vector2(560, 140), OpenCodex, ButtonStyle.Secondary);
            UiFactory.CreateButton(safe, "Settings", new Vector2(0, -560), new Vector2(560, 140), () => OpenSettings().Forget(), ButtonStyle.Secondary);

            BuildGarage(canvas.transform);
            BuildCodex(canvas.transform);
            BuildMaps(canvas.transform);
            _settingsView = SettingsPanelView.Create(safe);
            _settingsView.CloseRequested += () => CloseSettings().Forget();
            Refresh();
        }

        private void BuildGarage(Transform canvas)
        {
            var root = UiFactory.CreateRect("Garage", canvas);
            UiFactory.Stretch(root);
            UiFactory.CreateOverlay(root);
            var card = UiFactory.CreateCard(root, Vector2.zero, new Vector2(900f, 1100f));
            var heading = UiFactory.CreateText(card, "Garage", 80, new Vector2(0f, 450f), new Vector2(800f, 120f), font: UiFont.Display);
            heading.color = UiFactory.Ink;
            for (int i = 0; i < Upgrades.Length; i++)
            {
                int index = i;
                float y = 330f - i * 150f;
                _rowLabels[i] = UiFactory.CreateText(card, "", 40, new Vector2(-170f, y), new Vector2(480f, 140f), TextAlignmentOptions.Left);
                _rowLabels[i].color = UiFactory.Ink;
                _rowButtons[i] = UiFactory.CreateButton(card, "Buy", new Vector2(260f, y), new Vector2(260f, 120f), () => Buy(Upgrades[index]));
                _rowButtons[i].name = "Buy " + Upgrades[i];
                _rowButtons[i].gameObject.AddComponent<CanvasGroup>();
            }

            _bikeLabel = UiFactory.CreateText(card, "", 40, new Vector2(0f, -230f), new Vector2(820f, 70f));
            _bikeLabel.color = UiFactory.Ink;
            for (int i = 0; i < Bikes.Length; i++)
            {
                var model = Bikes[i];
                _bikeButtons[i] = UiFactory.CreateButton(card, model.ToString(), new Vector2(-270f + i * 270f, -320f), new Vector2(250f, 110f),
                    () => PickBike(model), ButtonStyle.Secondary);
                _bikeButtons[i].name = "Bike " + model;
                _bikeButtons[i].gameObject.AddComponent<CanvasGroup>();
            }

            UiFactory.CreateButton(card, "Close", new Vector2(0f, -465f), new Vector2(460f, 120f), () => _garage.SetActive(false), ButtonStyle.Secondary);
            _garage = root.gameObject;
            _garage.SetActive(false);
        }

        private void BuildMaps(Transform canvas)
        {
            var root = UiFactory.CreateRect("Maps", canvas);
            UiFactory.Stretch(root);
            UiFactory.CreateOverlay(root);
            var card = UiFactory.CreateCard(root, Vector2.zero, new Vector2(860f, 760f));
            var heading = UiFactory.CreateText(card, "Tonight's route", 70, new Vector2(0f, 270f), new Vector2(800f, 110f), font: UiFont.Display);
            heading.color = UiFactory.Ink;
            UiFactory.CreateButton(card, "Market Street", new Vector2(0f, 110f), new Vector2(620f, 150f), () => Ride(MapKind.MarketStreet));
            _harborButton = UiFactory.CreateButton(card, "Harbor Ring", new Vector2(0f, -80f), new Vector2(620f, 150f), () => Ride(MapKind.HarborRing));
            _harborButton.gameObject.AddComponent<CanvasGroup>();
            _harborNote = UiFactory.CreateText(card, "", 32, new Vector2(0f, -185f), new Vector2(760f, 60f));
            _harborNote.color = UiFactory.Muted;
            UiFactory.CreateButton(card, "Back", new Vector2(0f, -290f), new Vector2(400f, 110f), () => _maps.SetActive(false), ButtonStyle.Secondary);
            _maps = root.gameObject;
            _maps.SetActive(false);
        }

        private void OpenMaps()
        {
            bool open = Services.Get<SaveService>().Data.GetGame<GarageData>().CanRide(MapKind.HarborRing);
            _harborButton.interactable = open;
            _harborButton.GetComponent<CanvasGroup>().alpha = open ? 1f : 0.45f;
            _harborNote.text = open ? "" : UiFactory.Localize("Beat Market Street to open");
            _maps.SetActive(true);
        }

        private void Ride(MapKind map)
        {
            var save = Services.Get<SaveService>();
            var data = save.Data.GetGame<GarageData>();
            if (!data.CanRide(map))
            {
                return;
            }

            data.map = map;
            save.Data.SetGame(data);
            save.MarkDirty();
            save.Save();
            Services.Get<GameFlow>().GoToAsync(AppState.Game).Forget();
        }

        private void BuildCodex(Transform canvas)
        {
            var root = UiFactory.CreateRect("Codex", canvas);
            UiFactory.Stretch(root);
            UiFactory.CreateOverlay(root);
            var card = UiFactory.CreateCard(root, Vector2.zero, new Vector2(900f, 1300f));
            var heading = UiFactory.CreateText(card, "Codex", 80, new Vector2(0f, 550f), new Vector2(800f, 120f), font: UiFont.Display);
            heading.color = UiFactory.Ink;
            _codexText = UiFactory.CreateText(card, "", 36, new Vector2(0f, 20f), new Vector2(800f, 900f), TextAlignmentOptions.TopLeft);
            _codexText.color = UiFactory.Ink;
            UiFactory.CreateButton(card, "Close", new Vector2(0f, -560f), new Vector2(460f, 120f), () => _codex.SetActive(false), ButtonStyle.Secondary);
            _codex = root.gameObject;
            _codex.SetActive(false);
        }

        /// <summary>Everything ever carried, by name ("???" until seen), then the evolutions with their recipes.</summary>
        private void OpenCodex()
        {
            var data = Services.Get<SaveService>().Data.GetGame<GarageData>();
            var text = new System.Text.StringBuilder();
            text.Append("<b>").Append(UiFactory.Localize("Bike parts")).Append("</b>\n");
            for (int i = 0; i < Loadout.ItemCount; i++)
            {
                var item = (ItemKind)i;
                text.Append(data.Seen(item) ? UiFactory.Localize(ItemText.Name(item)) : "???").Append(i % 2 == 0 ? "\t\t" : "\n");
            }

            text.Append("\n<b>").Append(UiFactory.Localize("Evolutions")).Append("</b>\n");
            for (int i = 0; i < Loadout.ItemCount; i++)
            {
                var weapon = (ItemKind)i;
                if (Evolutions.PassiveFor(weapon) is ItemKind passive)
                {
                    string name = data.SeenEvolution(weapon) ? UiFactory.Localize(ItemText.EvolvedName(weapon)) : "???";
                    text.Append(name).Append("  <size=80%><color=#666>")
                        .Append(UiFactory.Localize(ItemText.Name(weapon))).Append(" Lv 5 + ").Append(UiFactory.Localize(ItemText.Name(passive)))
                        .Append("</color></size>\n");
                }
            }

            _codexText.text = text.ToString();
            _codex.SetActive(true);
        }

        private static string Describe(GarageUpgrade upgrade) => upgrade switch
        {
            GarageUpgrade.MaxHp => "Sturdier frame: +10 HP",
            GarageUpgrade.XpGain => "Courier app: +10% XP",
            GarageUpgrade.StartSpeed => "Better gears: +0.2 cruise",
            _ => "Spare map: 1 card reroll a ride",
        };

        private static string BikeLine(BikeModel model) => model switch
        {
            BikeModel.Racer => "Racer: faster, fragile",
            BikeModel.Cargo => "Cargo: tanky, slow to turn",
            _ => "Fixie: balanced",
        };

        private void PickBike(BikeModel model)
        {
            var save = Services.Get<SaveService>();
            var data = save.Data.GetGame<GarageData>();
            if (Garage.SelectBike(data, model))
            {
                save.Data.SetGame(data);
                save.MarkDirty();
                save.Save();
            }

            Refresh();
        }

        private void OpenGarage()
        {
            Refresh();
            _garage.SetActive(true);
        }

        private void Buy(GarageUpgrade upgrade)
        {
            var save = Services.Get<SaveService>();
            var data = save.Data.GetGame<GarageData>();
            if (Garage.TryBuy(data, upgrade))
            {
                save.Data.SetGame(data);
                save.MarkDirty();
                save.Save();
                Template.Infra.Device.Haptics.Medium();
            }

            Refresh();
        }

        private void Refresh()
        {
            var data = Services.Get<SaveService>().Data.GetGame<GarageData>();
            _coins.text = $"{data.coins} {UiFactory.Localize("coins")}";
            _bikeLabel.text = UiFactory.Localize(BikeLine(data.bike));
            for (int i = 0; i < Bikes.Length; i++)
            {
                var model = Bikes[i];
                bool owned = data.Owns(model);
                _bikeButtons[i].GetComponentInChildren<TextMeshProUGUI>().text =
                    owned ? UiFactory.Localize(model.ToString()) : $"{UiFactory.Localize(model.ToString())} {Garage.BikeCost}";
                _bikeButtons[i].GetComponent<CanvasGroup>().alpha = model == data.bike ? 1f : owned || data.coins >= Garage.BikeCost ? 0.7f : 0.4f;
            }
            for (int i = 0; i < Upgrades.Length; i++)
            {
                var upgrade = Upgrades[i];
                int level = data.Level(upgrade);
                bool maxed = level >= Garage.MaxLevel(upgrade);
                _rowLabels[i].text = $"{UiFactory.Localize(Describe(upgrade))}\n<size=75%>{UiFactory.Localize("Lv")} {level}/{Garage.MaxLevel(upgrade)}</size>";
                bool affordable = Garage.CanBuy(data, upgrade);
                _rowButtons[i].interactable = affordable;
                _rowButtons[i].GetComponent<CanvasGroup>().alpha = affordable ? 1f : 0.45f; // the template button has no disabled look
                _rowButtons[i].GetComponentInChildren<TextMeshProUGUI>().text = maxed ? UiFactory.Localize("Max") : $"{Garage.Cost(upgrade, level)}";
            }
        }

        private async UniTaskVoid OpenSettings()
        {
            var settings = Services.Get<SettingsService>();
            _settingsPresenter = new SettingsPresenter(settings.Current);
            _settingsPresenter.SettingsChanged += _ => settings.Apply();
            _settingsPresenter.Attach(_settingsView);
            await _stack.PushAsync(_settingsView);
        }

        private async UniTaskVoid CloseSettings()
        {
            _settingsPresenter?.Dispose();
            _settingsPresenter = null;
            Services.Get<SettingsService>().Commit();
            await _stack.PopAsync();
        }

        private void OnDestroy()
        {
            _settingsPresenter?.Dispose();
        }
    }
}
