using Cysharp.Threading.Tasks;
using NightCourier.Art;
using NightCourier.Core;
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
                () => Services.Get<GameFlow>().GoToAsync(AppState.Game).Forget());
            UiFactory.CreateButton(safe, "Garage", new Vector2(0, -260), new Vector2(560, 150), OpenGarage, ButtonStyle.Secondary);
            UiFactory.CreateButton(safe, "Settings", new Vector2(0, -440), new Vector2(560, 150), () => OpenSettings().Forget(), ButtonStyle.Secondary);

            BuildGarage(canvas.transform);
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
                float y = 270f - i * 170f;
                _rowLabels[i] = UiFactory.CreateText(card, "", 40, new Vector2(-170f, y), new Vector2(480f, 140f), TextAlignmentOptions.Left);
                _rowLabels[i].color = UiFactory.Ink;
                _rowButtons[i] = UiFactory.CreateButton(card, "Buy", new Vector2(260f, y), new Vector2(260f, 120f), () => Buy(Upgrades[index]));
                _rowButtons[i].name = "Buy " + Upgrades[i];
                _rowButtons[i].gameObject.AddComponent<CanvasGroup>();
            }

            UiFactory.CreateButton(card, "Close", new Vector2(0f, -440f), new Vector2(460f, 140f), () => _garage.SetActive(false), ButtonStyle.Secondary);
            _garage = root.gameObject;
            _garage.SetActive(false);
        }

        private static string Describe(GarageUpgrade upgrade) => upgrade switch
        {
            GarageUpgrade.MaxHp => "Sturdier frame: +10 HP",
            GarageUpgrade.XpGain => "Courier app: +10% XP",
            GarageUpgrade.StartSpeed => "Better gears: +0.2 cruise",
            _ => "Spare map: 1 card reroll a ride",
        };

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
