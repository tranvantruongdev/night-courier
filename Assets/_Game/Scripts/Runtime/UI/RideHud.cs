using System;
using NightCourier.Art;
using NightCourier.Core;
using Template.Feel;
using Template.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NightCourier.UI
{
    /// <summary>
    /// The ride's screen: timer and HP at the top, the speed gauge bottom-left (gold in the bonus zone), pause
    /// top-right, the floating joystick over everything else, and the pause and results panels.
    /// </summary>
    public sealed class RideHud
    {
        private const float BarWidth = 520f;
        private const float GaugeWidth = 380f;

        private TextMeshProUGUI _time;
        private RectTransform _hpFill;
        private RectTransform _speedFill;
        private Image _speedFillImage;
        private TextMeshProUGUI _hint;
        private Image _hurt;
        private GameObject _pause;
        private GameObject _results;
        private TextMeshProUGUI _resultsTitle;
        private TextMeshProUGUI _resultsBody;
        private RectTransform _xpFill;
        private TextMeshProUGUI _level;
        private GameObject _levelUp;
        private readonly Button[] _cards = new Button[LevelUpRoller.Cards];
        private readonly TextMeshProUGUI[] _cardTitles = new TextMeshProUGUI[LevelUpRoller.Cards];
        private readonly TextMeshProUGUI[] _cardLines = new TextMeshProUGUI[LevelUpRoller.Cards];
        private Action<int> _pick;

        public event Action PausePressed;
        public event Action ResumePressed;
        public event Action RetryPressed;
        public event Action HomePressed;

        public FloatingJoystick Joystick { get; private set; }

        public static RideHud Create(float bonusFraction)
        {
            var hud = new RideHud();
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.CreateCanvas("Ride HUD");

            hud._hurt = UiFactory.CreatePanel(canvas.transform, Color.clear);
            hud._hurt.raycastTarget = false;

            var touch = UiFactory.CreateRect("Touch", canvas.transform);
            UiFactory.Stretch(touch);
            hud.Joystick = FloatingJoystick.Create(touch);

            var safe = UiFactory.CreateSafeArea(canvas.transform);
            hud._time = UiFactory.Place(UiFactory.CreateText(safe, "0:00", 76, Vector2.zero, new Vector2(400, 100)),
                new Vector2(0.5f, 1f), new Vector2(0f, -90f));
            hud._hpFill = Bar(safe, new Vector2(0.5f, 1f), new Vector2(-BarWidth * 0.5f, -170f), BarWidth, 26f, Palette.Hurt).rectTransform;
            hud._xpFill = Bar(safe, new Vector2(0.5f, 1f), new Vector2(-BarWidth * 0.5f, -205f), BarWidth, 14f, Palette.Parcel).rectTransform;
            hud._level = UiFactory.Place(UiFactory.CreateText(safe, "Lv 1", 40, Vector2.zero, new Vector2(200, 60), TextAlignmentOptions.Left),
                new Vector2(0.5f, 1f), new Vector2(BarWidth * 0.5f + 120f, -185f));

            var gaugeLabel = UiFactory.Place(UiFactory.CreateText(safe, "SPEED", 34, Vector2.zero, new Vector2(300, 60), TextAlignmentOptions.Left),
                new Vector2(0f, 0f), new Vector2(210f, 200f));
            gaugeLabel.color = new Color(1f, 1f, 1f, 0.6f);
            hud._speedFillImage = Bar(safe, Vector2.zero, new Vector2(60f, 150f), GaugeWidth, 30f, Palette.Bike);
            hud._speedFill = hud._speedFillImage.rectTransform;
            var mark = UiFactory.CreateRounded(safe, Vector2.zero, new Vector2(6f, 46f), Palette.Bonus, 3);
            UiFactory.Place(mark, Vector2.zero, new Vector2(60f + GaugeWidth * bonusFraction, 150f));

            UiFactory.Place(UiFactory.CreateIconButton(safe, UiTheme.Current.iconPause, Vector2.zero, 110f, () => hud.PausePressed?.Invoke(),
                ButtonStyle.Glass, "II"), new Vector2(1f, 1f), new Vector2(-100f, -100f));

            hud._hint = UiFactory.Place(UiFactory.CreateText(safe, "Drag to steer. You can't stop — keep moving!", 52, Vector2.zero,
                new Vector2(900, 200)), new Vector2(0.5f, 0.3f), Vector2.zero);

            hud._pause = Panel(canvas.transform, "Paused", out _, out _);
            Button(hud._pause.transform, "Resume", -10f, () => hud.ResumePressed?.Invoke());
            Button(hud._pause.transform, "Home", -200f, () => hud.HomePressed?.Invoke(), ButtonStyle.Secondary);

            hud._results = Panel(canvas.transform, "Shift over", out hud._resultsTitle, out hud._resultsBody);
            Button(hud._results.transform, "Ride again", -10f, () => hud.RetryPressed?.Invoke());
            Button(hud._results.transform, "Home", -200f, () => hud.HomePressed?.Invoke(), ButtonStyle.Secondary);

            hud._levelUp = Panel(canvas.transform, "Level up!", out _, out _);
            var levelCard = hud._levelUp.transform.Find("Card");
            for (int i = 0; i < LevelUpRoller.Cards; i++)
            {
                int index = i;
                float y = 160f - i * 240f;
                hud._cards[i] = UiFactory.CreateButton(levelCard, "Card", new Vector2(0f, y), new Vector2(700f, 150f),
                    () => hud._pick?.Invoke(index), ButtonStyle.Secondary);
                hud._cards[i].name = "Card " + i;
                hud._cardTitles[i] = hud._cards[i].GetComponentInChildren<TextMeshProUGUI>();
                hud._cardLines[i] = UiFactory.CreateText(levelCard, "", 34, new Vector2(0f, y - 105f), new Vector2(700f, 60f));
                hud._cardLines[i].color = UiFactory.Muted;
            }

            return hud;
        }

        public void SetLevel(int level, float xpFraction)
        {
            SetWidth(_xpFill, BarWidth, xpFraction);
            _level.text = $"{UiFactory.Localize("Lv")} {level}";
        }

        /// <summary>Shows the cards for the first pending level-up; <paramref name="pick"/> gets the chosen index.</summary>
        public void ShowLevelUp(Ride ride, Action<int> pick)
        {
            _pick = pick;
            for (int i = 0; i < LevelUpRoller.Cards; i++)
            {
                bool shown = i < ride.OfferCount;
                _cards[i].gameObject.SetActive(shown);
                _cardLines[i].gameObject.SetActive(shown);
                if (shown)
                {
                    var item = ride.Offer[i];
                    int level = ride.Loadout.Level(item);
                    string tag = level == 0 ? UiFactory.Localize("NEW") : $"{UiFactory.Localize("Lv")} {level} → {level + 1}";
                    _cardTitles[i].text = $"{UiFactory.Localize(ItemText.Name(item))}  <size=70%>{tag}</size>";
                    _cardLines[i].text = UiFactory.Localize(ItemText.Line(item));
                }
            }

            _levelUp.SetActive(true);
            Joystick.Release();
            Joystick.enabled = false;
        }

        public void HideLevelUp()
        {
            _levelUp.SetActive(false);
            Joystick.enabled = true;
        }

        public void SetTime(float seconds)
        {
            int s = (int)seconds;
            _time.text = UiFactory.Tabular($"{s / 60}:{s % 60:00}");
        }

        public void SetHp(float fraction) => SetWidth(_hpFill, BarWidth, fraction);

        public void SetSpeed(float fraction, bool bonus)
        {
            SetWidth(_speedFill, GaugeWidth, fraction);
            _speedFillImage.color = bonus ? Palette.Bonus : Palette.Bike;
        }

        public void HideHint() => _hint.gameObject.SetActive(false);

        public void FlashHurt()
        {
            _hurt.color = Color.clear;
            JuiceFx.Flash(_hurt, new Color(Palette.Hurt.r, Palette.Hurt.g, Palette.Hurt.b, 0.3f), 0.3f);
        }

        public void ShowPause(bool shown)
        {
            _pause.SetActive(shown);
            Joystick.enabled = !shown;
            if (shown)
            {
                Joystick.Release();
            }
        }

        public void ShowResults(string title, string body)
        {
            _resultsTitle.text = UiFactory.Localize(title);
            _resultsBody.text = body;
            _results.SetActive(true);
            Joystick.Release();
            Joystick.enabled = false;
        }

        public void HideResults()
        {
            _results.SetActive(false);
            Joystick.enabled = true;
        }

        /// <summary>A track with a fill anchored at its left end; returns the fill.</summary>
        private static Image Bar(Transform parent, Vector2 anchor, Vector2 leftEdge, float width, float height, Color color)
        {
            var track = UiFactory.CreateRounded(parent, Vector2.zero, new Vector2(width, height), new Color(1f, 1f, 1f, 0.12f), (int)(height * 0.5f));
            var rect = track.rectTransform;
            rect.pivot = new Vector2(0f, 0.5f);
            UiFactory.Place(track, anchor, leftEdge);

            var fill = UiFactory.CreateRounded(rect, Vector2.zero, new Vector2(width, height), color, (int)(height * 0.5f));
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = fillRect.anchorMax = fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            return fill;
        }

        private static void SetWidth(RectTransform fill, float width, float fraction) =>
            fill.sizeDelta = new Vector2(Mathf.Max(fill.sizeDelta.y, width * Mathf.Clamp01(fraction)), fill.sizeDelta.y);

        private static GameObject Panel(Transform canvas, string title, out TextMeshProUGUI titleLabel, out TextMeshProUGUI body)
        {
            var root = UiFactory.CreateRect(title, canvas);
            UiFactory.Stretch(root);
            UiFactory.CreateOverlay(root);
            var card = UiFactory.CreateCard(root, Vector2.zero, new Vector2(820f, 900f));
            titleLabel = UiFactory.CreateText(card, title, 80, new Vector2(0f, 330f), new Vector2(760f, 120f), font: UiFont.Display);
            titleLabel.color = UiFactory.Ink;
            body = UiFactory.CreateText(card, "", 50, new Vector2(0f, 170f), new Vector2(720f, 220f));
            body.color = UiFactory.Muted;
            root.gameObject.SetActive(false);
            return root.gameObject;
        }

        private static void Button(Transform panel, string label, float y, Action onClick, ButtonStyle style = ButtonStyle.Primary)
        {
            var card = panel.Find("Card");
            UiFactory.CreateButton(card, label, new Vector2(0f, y - 120f), new Vector2(560f, 150f), onClick, style);
        }
    }
}
