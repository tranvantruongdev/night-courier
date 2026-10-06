using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NightCourier.Art;
using NightCourier.Core;
using NightCourier.UI;
using NightCourier.View;
using Template.Core.Flow;
using Template.Core.Save;
using Template.Feel;
using Template.Game.Flow;
using Template.Infra;
using Template.Infra.Audio;
using Template.Infra.Device;
using UnityEngine;

namespace NightCourier
{
    /// <summary>
    /// The Game scene: owns one <see cref="Ride"/>, steps it at a fixed rate with the joystick (or the autopilot),
    /// and turns ride events into sound, haptics and HUD updates. The camera leads the bike by its velocity.
    /// </summary>
    public sealed class RideController : MonoBehaviour
    {
        private const float StepSeconds = 1f / 120f;
        private const float LookAheadSeconds = 0.45f;
        private const float CameraSmoothing = 0.3f;
        private const float GroundTile = 16f;

        private static readonly int[] StressCounts = { 0, 100, 300, 500 };
        private static readonly string[] StressLabels = { "Normal ride", "Stress 100", "Stress 300", "Stress 500" };

        private enum Phase
        {
            Riding,
            Paused,
            Over,
        }

        /// <summary>Tests and trailer shots: the bot steers instead of the joystick.</summary>
        public static bool Autopilot;

        /// <summary>Profiling: ride with exactly this many drones and no damage (0 = a normal ride). Dev builds offer 100/300/500.</summary>
        public static int StressDrones;

        private readonly List<RideEvent> _events = new List<RideEvent>(256);
        private StateMachine<Phase> _phase;
        private Ride _ride;
        private Camera _camera;
        private Vector3 _cameraVelocity;
        private Transform _ground;
        private BikeView _bikeView;
        private SwarmView _swarmView;
        private WeaponsView _weaponsView;
        private RideHud _hud;
        private AudioService _audio;
        private AudioClip _hitSound;
        private AudioClip _dodgeSound;
        private AudioClip _crashSound;
        private AudioClip _killSound;
        private AudioClip _collectSound;
        private AudioClip _levelSound;
        private AudioClip _bellSound;
        private AudioClip _radioSound;
        private float _accumulator;
        private bool _choosing;

        public Ride Ride => _ride;
        public bool IsOver => _phase != null && _phase.Current == Phase.Over;

        private void Start()
        {
            if (!BootGuard.EnsureBooted())
            {
                return;
            }

            _phase = new StateMachine<Phase>(Phase.Riding)
                .Allow(Phase.Riding, Phase.Paused, Phase.Over)
                .Allow(Phase.Paused, Phase.Riding)
                .Allow(Phase.Over, Phase.Riding);

            _audio = Services.Get<AudioService>();
            _hitSound = ToneFactory.Blip("hit", 170f, 0.14f, 0.6f);
            _dodgeSound = ToneFactory.Blip("dodge", 1500f, 0.05f, 0.35f);
            _crashSound = ToneFactory.Blip("crash", 80f, 0.45f, 0.7f);
            _killSound = ToneFactory.Blip("pop", 900f, 0.04f, 0.3f);
            _collectSound = ToneFactory.Blip("parcel", 2100f, 0.03f, 0.2f);
            _levelSound = ToneFactory.Blip("level", 660f, 0.25f, 0.5f);
            _bellSound = ToneFactory.Blip("bell", 1320f, 0.35f, 0.45f);
            _radioSound = ToneFactory.Blip("radio", 2400f, 0.06f, 0.3f);

            _camera = Camera.main;
            _camera.orthographic = true;
            _camera.orthographicSize = 8f;
            _camera.backgroundColor = Palette.Night;

            var world = new GameObject("World").transform;
            var ground = new GameObject("Ground").AddComponent<SpriteRenderer>();
            ground.transform.SetParent(world, false);
            ground.sprite = NeonArt.MarketStreet; // map 1
            ground.drawMode = SpriteDrawMode.Tiled;
            ground.size = new Vector2(GroundTile * 3f, GroundTile * 3f);
            ground.sortingOrder = -10;
            _ground = ground.transform;
            _swarmView = new SwarmView(world);
            _weaponsView = new WeaponsView(world);
            _bikeView = new BikeView(world);

            _hud = RideHud.Create(RideTuning.Default().dodgeSpeedFraction);
            _hud.PausePressed += Pause;
            _hud.ResumePressed += Resume;
            _hud.RetryPressed += NewRide;
            _hud.HomePressed += GoHome;
            AppLifecycle.BackPressed += OnBack;
            AppLifecycle.PauseChanged += OnAppPause;

            NewRide();
        }

        private void OnDestroy()
        {
            AppLifecycle.BackPressed -= OnBack;
            AppLifecycle.PauseChanged -= OnAppPause;
            Time.timeScale = 1f;
        }

        private void NewRide()
        {
            var tuning = RideTuning.Default();
            if (StressDrones > 0)
            {
                tuning.waves = WaveDirector.Flat(StressDrones);
                tuning.maxHp = float.MaxValue;
            }

            _ride = new Ride(tuning, (ulong)DateTime.UtcNow.Ticks);
            _accumulator = 0f;
            _choosing = false;
            Time.timeScale = 1f;
            _hud.HideLevelUp();
            _hud.HideResults();
            _hud.ShowPause(false);
            _phase.TryGo(Phase.Riding);
            Radio("Dispatch: Kai, the Swarm is out tonight. Keep those wheels turning.");
            _camera.transform.position = CameraTarget();
            _cameraVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (_ride == null)
            {
                return;
            }

            if (_phase.Current == Phase.Riding)
            {
                if (Autopilot || _hud.Joystick.Touched)
                {
                    _hud.HideHint();
                }

                while (Autopilot && _ride.PendingLevelUps > 0)
                {
                    _ride.Choose(0);
                }

                if (_ride.PendingLevelUps > 0)
                {
                    // The ride waits for a card; time spent choosing isn't owed back as a burst of steps.
                    _accumulator = 0f;
                    if (!_choosing)
                    {
                        _choosing = true;
                        _hud.ShowLevelUp(_ride, OnCardPicked);
                    }
                }

                _accumulator += Time.deltaTime;
                while (_accumulator >= StepSeconds && !_ride.Over && _ride.PendingLevelUps == 0)
                {
                    _accumulator -= StepSeconds;
                    var stick = Autopilot ? RideBot.Steer(_ride) : new System.Numerics.Vector2(_hud.Joystick.Value.x, _hud.Joystick.Value.y);
                    _events.Clear();
                    _ride.Step(StepSeconds, stick, _events);
                    foreach (var e in _events)
                    {
                        Handle(e);
                    }
                }
            }

            var bike = _ride.Bike;
            _bikeView.Sync(bike, _ride.Invulnerable, _ride.Boosted, Time.deltaTime);
            _swarmView.Sync(_ride.Swarm, new Vector2(bike.Position.X, bike.Position.Y));
            _weaponsView.Sync(_ride, Time.deltaTime);
            _swarmView.SyncOrbs(_ride);
            _hud.SetTime(_ride.Time);
            _hud.Tick(Time.unscaledDeltaTime);
            _hud.SetLevel(_ride.Level, _ride.Xp / (float)_ride.XpNeeded);
            _hud.SetHp(_ride.Hp / _ride.Tuning.maxHp);
            _hud.SetSpeed(bike.Speed / bike.MaxSpeed, bike.CanDodge);

            _camera.transform.position = Vector3.SmoothDamp(_camera.transform.position, CameraTarget(), ref _cameraVelocity, CameraSmoothing);
            Vector3 c = _camera.transform.position;
            _ground.position = new Vector3(Mathf.Round(c.x / GroundTile) * GroundTile, Mathf.Round(c.y / GroundTile) * GroundTile, 0f);
        }

        private Vector3 CameraTarget()
        {
            var bike = _ride.Bike;
            var lead = bike.Position + bike.Velocity * LookAheadSeconds;
            return new Vector3(lead.X, lead.Y, -10f);
        }

        private void Handle(RideEvent e)
        {
            switch (e.type)
            {
                case RideEventType.Hit:
                    _audio.PlaySfx(_hitSound, 1f, UnityEngine.Random.Range(0.9f, 1.1f));
                    Haptics.Medium();
                    _hud.FlashHurt();
                    JuiceFx.Shake(_camera.transform, 0.15f, 0.2f);
                    break;
                case RideEventType.Dodged:
                    _audio.PlaySfx(_dodgeSound, 0.8f);
                    Haptics.Light();
                    break;
                case RideEventType.Died:
                    OnCrashed().Forget();
                    break;
                case RideEventType.Killed:
                    _audio.PlaySfx(_killSound, 0.5f, UnityEngine.Random.Range(0.85f, 1.2f));
                    break;
                case RideEventType.Collected:
                    _audio.PlaySfx(_collectSound, 0.35f, UnityEngine.Random.Range(0.95f, 1.1f));
                    break;
                case RideEventType.LevelUp:
                    _audio.PlaySfx(_levelSound);
                    Haptics.Medium();
                    break;
                case RideEventType.ShiftEvent:
                    Radio(RadioLines[(int)e.value]);
                    Haptics.Medium();
                    break;
                case RideEventType.Drafted:
                    _audio.PlaySfx(_levelSound, 0.7f, 1.5f);
                    Haptics.Medium();
                    JuiceFx.Punch(_bikeView.Body, 0.3f);
                    break;
                case RideEventType.WhipCracked:
                    _weaponsView.CrackWhip(new Vector2(e.x, e.y), e.value, _ride.Arsenal.WhipRange);
                    _audio.PlaySfx(_dodgeSound, 0.5f, 0.6f);
                    break;
                case RideEventType.BellRang:
                    _weaponsView.RingBell(new Vector2(e.x, e.y), e.value);
                    _audio.PlaySfx(_bellSound, 0.8f);
                    break;
            }
        }

        /// <summary>Dispatch's line for each <see cref="ShiftEvent"/>, in enum order.</summary>
        private static readonly string[] RadioLines =
        {
            "Dispatch: They're circling you. Break out!",
            "Dispatch: Big wave coming from one side. Cut across it, not into it.",
            "Dispatch: Three heavy drones on your line. Draft them!",
        };

        private void Radio(string line)
        {
            _hud.ShowRadio(line);
            _audio.PlaySfx(_radioSound, 0.6f);
        }

        private void OnCardPicked(int index)
        {
            if (!_choosing || index >= _ride.OfferCount)
            {
                return;
            }

            _ride.Choose(index);
            _hud.HideLevelUp();
            _choosing = false; // another pending level-up shows its cards next frame
        }

        private async UniTaskVoid OnCrashed()
        {
            _phase.Go(Phase.Over);
            _audio.PlaySfx(_crashSound);
            Haptics.Heavy();

            // A moment of slow motion, then the results.
            Time.timeScale = JuiceFx.ReduceMotion ? 1f : 0.3f;
            await UniTask.Delay(TimeSpan.FromSeconds(0.7f), ignoreTimeScale: true, cancellationToken: this.GetCancellationTokenOnDestroy());
            Time.timeScale = 1f;

            var save = Services.Get<SaveService>();
            int seconds = (int)_ride.Time;
            bool newBest = seconds > save.Data.bestScore;
            save.Data.bestScore = Math.Max(save.Data.bestScore, seconds);
            save.Data.totalRuns++;
            save.MarkDirty();
            save.Save();

            string best = newBest ? Loc("New best!") : $"{Loc("Best")} {Clock(save.Data.bestScore)}";
            var chart = new List<(string name, float damage)>();
            foreach (ItemKind item in Enum.GetValues(typeof(ItemKind)))
            {
                float dealt = _ride.Arsenal.DamageBy[(int)item];
                if (dealt > 0f)
                {
                    chart.Add((_ride.Loadout.Evolved(item) ? ItemText.EvolvedName(item) : ItemText.Name(item), dealt));
                }
            }

            chart.Sort((a, b) => b.damage.CompareTo(a.damage));
            _hud.ShowResults("Shift over",
                $"{Loc("Survived")} {Clock(seconds)}  ·  {_ride.Kills} {Loc("kills")}  ·  {Loc("Lv")} {_ride.Level}\n{best}", chart);
        }

        private static string Loc(string text) => Template.UI.UiFactory.Localize(text);

        private static string Clock(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

        private void Pause()
        {
            if (_phase.TryGo(Phase.Paused))
            {
                Time.timeScale = 0f;
                _hud.ShowPause(true);
            }
        }

        private void Resume()
        {
            if (_phase.Current != Phase.Paused)
            {
                return;
            }

            Time.timeScale = 1f;
            _hud.ShowPause(false);
            _phase.Go(Phase.Riding);
        }

        private void OnBack()
        {
            if (_choosing)
            {
                return; // the cards wait for a choice
            }

            switch (_phase.Current)
            {
                case Phase.Riding:
                    Pause();
                    break;
                case Phase.Paused:
                    Resume();
                    break;
                case Phase.Over:
                    GoHome();
                    break;
            }
        }

        private void OnAppPause(bool paused)
        {
            if (paused && _phase.Current == Phase.Riding && !_choosing)
            {
                Pause();
            }
        }

        private void GoHome()
        {
            Time.timeScale = 1f;
            Services.Get<GameFlow>().GoToAsync(AppState.Title).Forget();
        }

        private void OnGUI()
        {
            if (!Debug.isDebugBuild)
            {
                return;
            }

            // Profiling rides for the phone: fixed drone counts, no damage. Bottom-right, away from the gauge.
            float scale = Mathf.Max(1f, Screen.dpi / 160f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;
            GUILayout.BeginArea(new Rect(w - 130f, h - 150f, 120f, 140f));
            for (int i = 0; i < StressCounts.Length; i++)
            {
                if (GUILayout.Button(StressLabels[i]))
                {
                    StressDrones = StressCounts[i];
                    NewRide();
                }
            }

            GUILayout.EndArea();
        }
    }
}
