using NightCourier.Art;
using NightCourier.Core;
using UnityEngine;

namespace NightCourier.View
{
    /// <summary>
    /// Kai's bike: the sprite, a halo that turns gold in the speed-bonus zone, an afterimage trail that
    /// stretches with speed, and a blink while invulnerable.
    /// </summary>
    public sealed class BikeView
    {
        private const int TrailLength = 12;
        private const float TrailEvery = 0.025f;

        private readonly SpriteRenderer _bike;
        private readonly SpriteRenderer _halo;
        private readonly SpriteRenderer[] _trail = new SpriteRenderer[TrailLength];
        private int _nextTrail;
        private float _trailTimer;

        public BikeView(Transform parent)
        {
            var root = new GameObject("Bike").transform;
            root.SetParent(parent, false);
            _halo = Renderer(root, "Halo", NeonArt.Dot, 9);
            _halo.transform.localScale = Vector3.one * 1.8f;
            _bike = Renderer(root, "Body", NeonArt.Bike, 10);
            _bike.color = Palette.Bike;
            for (int i = 0; i < TrailLength; i++)
            {
                _trail[i] = Renderer(root, "Trail", NeonArt.Dot, 8);
                _trail[i].color = Color.clear;
            }
        }

        public Transform Body => _bike.transform;

        public void Sync(BikeMotor bike, bool invulnerable, float dt)
        {
            var position = new Vector3(bike.Position.X, bike.Position.Y, 0f);
            _bike.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, bike.Heading * Mathf.Rad2Deg));
            _halo.transform.position = position;

            bool blink = invulnerable && Mathf.Repeat(Time.time * 16f, 2f) < 1f;
            _bike.color = blink ? new Color(1f, 1f, 1f, 0.35f) : Palette.Bike;
            Color haloColor = bike.CanDodge ? Palette.Bonus : Palette.Bike;
            haloColor.a = bike.CanDodge ? 0.55f : 0.2f;
            _halo.color = haloColor;

            Color trailColor = bike.CanDodge ? Palette.Bonus : Palette.Bike;
            for (int i = 0; i < TrailLength; i++)
            {
                var c = _trail[i].color;
                c.a = Mathf.Max(0f, c.a - dt * 2.2f);
                _trail[i].color = c;
            }

            _trailTimer += dt;
            if (_trailTimer >= TrailEvery)
            {
                _trailTimer = 0f;
                var dot = _trail[_nextTrail];
                _nextTrail = (_nextTrail + 1) % TrailLength;
                dot.transform.position = position;
                dot.transform.localScale = Vector3.one * (0.35f + 0.1f * bike.Speed);
                trailColor.a = 0.18f + 0.06f * bike.Speed;
                dot.color = trailColor;
            }
        }

        private static SpriteRenderer Renderer(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = order;
            return r;
        }
    }
}
