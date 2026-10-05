using System.Collections.Generic;
using NightCourier.Art;
using NightCourier.Core;
using UnityEngine;

namespace NightCourier.View
{
    /// <summary>The bike's weapons and the XP parcels on the road: headlight beam, spoke blades, bell ring.</summary>
    public sealed class WeaponsView
    {
        private const float RingSeconds = 0.3f;
        private const float WhipSeconds = 0.15f;

        private readonly Transform _root;
        private readonly SpriteRenderer _beam;
        private readonly SpriteRenderer[] _blades = new SpriteRenderer[Loadout.MaxLevel + 1];
        private readonly SpriteRenderer _ring;
        private readonly SpriteRenderer _whip;
        private readonly SpriteRenderer[] _spikes = new SpriteRenderer[Arsenal.MaxSpikes];
        private readonly List<SpriteRenderer> _parcels = new List<SpriteRenderer>();
        private float _whipAge = WhipSeconds;
        private int _parcelsShown;
        private float _ringAge = RingSeconds;
        private float _ringRadius;

        public WeaponsView(Transform parent)
        {
            _root = new GameObject("Weapons").transform;
            _root.SetParent(parent, false);
            _beam = Renderer("Headlight", null, 7);
            for (int i = 0; i < _blades.Length; i++)
            {
                _blades[i] = Renderer("Spoke", NeonArt.Scout, 11);
                _blades[i].color = Color.white;
                _blades[i].transform.localScale = Vector3.one * 0.7f;
                _blades[i].enabled = false;
            }

            _ring = Renderer("Bell", NeonArt.Ring, 6);
            _ring.enabled = false;
            _whip = Renderer("Whip", NeonArt.Beam(45f), 7);
            _whip.enabled = false;
            for (int i = 0; i < _spikes.Length; i++)
            {
                _spikes[i] = Renderer("Spike", NeonArt.Dot, 3);
                _spikes[i].enabled = false;
            }
        }

        public void CrackWhip(Vector2 at, float angle, float range)
        {
            _whip.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg));
            _whip.transform.localScale = new Vector3(range, range * 2f, 1f);
            _whipAge = 0f;
            _whip.enabled = true;
        }

        public void RingBell(Vector2 at, float radius)
        {
            _ring.transform.position = at;
            _ringRadius = radius;
            _ringAge = 0f;
            _ring.enabled = true;
        }

        public void Sync(Ride ride, float dt)
        {
            var bike = ride.Bike;
            var arsenal = ride.Arsenal;
            var position = new Vector3(bike.Position.X, bike.Position.Y, 0f);

            _beam.enabled = ride.Loadout.Owns(ItemKind.Headlight);
            if (_beam.enabled)
            {
                float range = arsenal.HeadlightRange;
                _beam.sprite = NeonArt.Beam(arsenal.HeadlightHalfAngle * Mathf.Rad2Deg);
                _beam.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, bike.Heading * Mathf.Rad2Deg));
                _beam.transform.localScale = new Vector3(range, range * 2f, 1f);
                var c = Palette.Bonus;
                c.a = bike.CanDodge ? 0.4f : 0.22f;
                _beam.color = c;
            }

            int blades = arsenal.SpokeBlades;
            for (int b = 0; b < _blades.Length; b++)
            {
                _blades[b].enabled = b < blades;
                if (b < blades)
                {
                    float angle = arsenal.SpokeAngle + b * 2f * Mathf.PI / blades;
                    var at = position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * ride.Tuning.spokeOrbit;
                    _blades[b].transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg * 3f));
                }
            }

            if (_ringAge < RingSeconds)
            {
                _ringAge += dt;
                float t = Mathf.Clamp01(_ringAge / RingSeconds);
                _ring.transform.localScale = Vector3.one * (_ringRadius * (1f - (1f - t) * (1f - t)));
                _ring.color = new Color(1f, 0.95f, 0.6f, 1f - t);
                _ring.enabled = t < 1f;
            }

            if (_whipAge < WhipSeconds)
            {
                _whipAge += dt;
                _whip.color = new Color(1f, 1f, 1f, 0.6f * (1f - Mathf.Clamp01(_whipAge / WhipSeconds)));
                _whip.enabled = _whipAge < WhipSeconds;
            }

            float spikeSize = arsenal.SpikesRadius * 2.4f;
            for (int s = 0; s < _spikes.Length; s++)
            {
                float life = arsenal.SpikeLife[s];
                _spikes[s].enabled = life > 0f;
                if (life > 0f)
                {
                    _spikes[s].transform.position = new Vector3(arsenal.SpikeX[s], arsenal.SpikeY[s], 0f);
                    _spikes[s].transform.localScale = Vector3.one * spikeSize;
                    _spikes[s].color = new Color(1f, 0.55f, 0.2f, 0.75f * Mathf.Clamp01(life / 0.4f));
                }
            }

            SyncParcels(ride.Parcels);
        }

        private void SyncParcels(Parcels parcels)
        {
            while (_parcels.Count < parcels.Count)
            {
                var r = Renderer("Parcel", NeonArt.Dot, 4);
                r.color = Palette.Parcel;
                r.transform.localScale = Vector3.one * 0.45f;
                _parcels.Add(r);
            }

            for (int i = 0; i < parcels.Count; i++)
            {
                _parcels[i].transform.position = new Vector3(parcels.X[i], parcels.Y[i], 0f);
                _parcels[i].enabled = true;
            }

            for (int i = parcels.Count; i < _parcelsShown; i++)
            {
                _parcels[i].enabled = false;
            }

            _parcelsShown = parcels.Count;
        }

        private SpriteRenderer Renderer(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = order;
            return r;
        }
    }
}
