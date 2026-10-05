using System;
using System.Collections.Generic;
using System.Numerics;

namespace NightCourier.Core
{
    /// <summary>
    /// Fires the bike's weapons at the swarm. Every hit goes through the swarm's grid and is scaled by the speed
    /// bonus, so riding fast is how you hit hard. Drones at 0 HP stay in place until <see cref="Ride"/> reaps them.
    /// </summary>
    public sealed class Arsenal
    {
        private const float Deg2Rad = MathF.PI / 180f;

        private readonly RideTuning _t;
        private readonly Loadout _loadout;
        private readonly Swarm _swarm;
        private readonly int[] _near = new int[512];
        private float _bellTimer;

        public Arsenal(RideTuning tuning, Loadout loadout, Swarm swarm)
        {
            _t = tuning;
            _loadout = loadout;
            _swarm = swarm;
        }

        /// <summary>Damage dealt so far, per item (for the results chart).</summary>
        public float[] DamageBy { get; } = new float[Loadout.ItemCount];

        /// <summary>Radians; blade k sits at SpokeAngle + k × 2π / blades.</summary>
        public float SpokeAngle { get; private set; }

        public int SpokeBlades => _loadout.Owns(ItemKind.SpokeCards) ? _loadout.Level(ItemKind.SpokeCards) + 1 : 0;
        public float HeadlightRange => _t.headlightRange + _t.headlightRangePerLevel * (_loadout.Level(ItemKind.Headlight) - 1);
        public float HeadlightHalfAngle => (_t.headlightHalfAngle + _t.headlightHalfAnglePerLevel * (_loadout.Level(ItemKind.Headlight) - 1)) * Deg2Rad;
        public float BellRadius => _t.bellRadius + _t.bellRadiusPerLevel * (_loadout.Level(ItemKind.Bell) - 1);
        public float BellCooldown => _t.bellCooldown * (1f - _t.gearCooldownPerLevel * _loadout.Level(ItemKind.GearRatio));

        public void Fire(float dt, BikeMotor bike, List<RideEvent> events)
        {
            float bonus = bike.DamageMultiplier;
            if (_loadout.Owns(ItemKind.Headlight))
            {
                FireHeadlight(dt, bike, bonus);
            }

            if (_loadout.Owns(ItemKind.SpokeCards))
            {
                FireSpokes(dt, bike, bonus);
            }

            if (_loadout.Owns(ItemKind.Bell))
            {
                _bellTimer += dt;
                if (_bellTimer >= BellCooldown)
                {
                    _bellTimer = 0f;
                    RingBell(bike, bonus, events);
                }
            }
        }

        private void FireHeadlight(float dt, BikeMotor bike, float bonus)
        {
            int level = _loadout.Level(ItemKind.Headlight);
            float range = HeadlightRange;
            float cosHalf = MathF.Cos(HeadlightHalfAngle);
            float damage = (_t.headlightDps + _t.headlightDpsPerLevel * (level - 1)) * bonus * dt;
            Vector2 p = bike.Position, forward = bike.Forward;

            int found = _swarm.Grid.Query(p.X, p.Y, range + _t.MaxEnemyRadius, _near);
            for (int k = 0; k < found; k++)
            {
                int i = _near[k];
                float dx = _swarm.X[i] - p.X, dy = _swarm.Y[i] - p.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float reach = range + _t.Stats(_swarm.Kind[i]).radius;
                if (d > reach || d < 1e-4f || (dx * forward.X + dy * forward.Y) / d < cosHalf)
                {
                    continue;
                }

                Hit(i, damage, ItemKind.Headlight);
            }
        }

        private void FireSpokes(float dt, BikeMotor bike, float bonus)
        {
            int level = _loadout.Level(ItemKind.SpokeCards);
            int blades = level + 1;
            SpokeAngle = BikeMotor.WrapAngle(SpokeAngle + _t.spokeTurnRate * Deg2Rad * dt);
            float damage = (_t.spokeDps + _t.spokeDpsPerLevel * (level - 1)) * bonus * dt;

            for (int b = 0; b < blades; b++)
            {
                float angle = SpokeAngle + b * 2f * MathF.PI / blades;
                float bx = bike.Position.X + MathF.Cos(angle) * _t.spokeOrbit;
                float by = bike.Position.Y + MathF.Sin(angle) * _t.spokeOrbit;
                int found = _swarm.Grid.Query(bx, by, _t.spokeRadius + _t.MaxEnemyRadius, _near);
                for (int k = 0; k < found; k++)
                {
                    int i = _near[k];
                    float dx = _swarm.X[i] - bx, dy = _swarm.Y[i] - by;
                    float reach = _t.spokeRadius + _t.Stats(_swarm.Kind[i]).radius;
                    if (dx * dx + dy * dy <= reach * reach)
                    {
                        Hit(i, damage, ItemKind.SpokeCards);
                    }
                }
            }
        }

        private void RingBell(BikeMotor bike, float bonus, List<RideEvent> events)
        {
            int level = _loadout.Level(ItemKind.Bell);
            float radius = BellRadius;
            float damage = (_t.bellDamage + _t.bellDamagePerLevel * (level - 1)) * bonus;
            Vector2 p = bike.Position;
            events.Add(new RideEvent { type = RideEventType.BellRang, x = p.X, y = p.Y, value = radius });

            int found = _swarm.Grid.Query(p.X, p.Y, radius + _t.MaxEnemyRadius, _near);
            for (int k = 0; k < found; k++)
            {
                int i = _near[k];
                float dx = _swarm.X[i] - p.X, dy = _swarm.Y[i] - p.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > radius + _t.Stats(_swarm.Kind[i]).radius)
                {
                    continue;
                }

                Hit(i, damage, ItemKind.Bell);
                if (d > 1e-4f)
                {
                    _swarm.X[i] += dx / d * _t.bellKnockback;
                    _swarm.Y[i] += dy / d * _t.bellKnockback;
                }
            }
        }

        private void Hit(int i, float damage, ItemKind source)
        {
            float dealt = MathF.Min(damage, _swarm.Hp[i]);
            if (dealt <= 0f)
            {
                return; // already down this step
            }

            _swarm.Hp[i] -= dealt;
            DamageBy[(int)source] += dealt;
        }
    }
}
