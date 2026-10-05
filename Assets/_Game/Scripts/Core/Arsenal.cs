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
        public const int MaxSpikes = 32;
        public const int MaxShots = 64;
        private const float ShotRadius = 0.15f;
        private const float HomingRadius = 3f;

        private readonly int[] _near = new int[512];
        private float _bellTimer;
        private float _whipTimer;
        private int _lastWhipSide = -1;
        private float _spikeTimer;
        private int _nextSpike;
        private float _pannierTimer;
        private int _nextShot;

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
        public float HeadlightRange => (_t.headlightRange + _t.headlightRangePerLevel * (_loadout.Level(ItemKind.Headlight) - 1)) * Reach(ItemKind.Headlight);
        public float HeadlightHalfAngle => (_t.headlightHalfAngle + _t.headlightHalfAnglePerLevel * (_loadout.Level(ItemKind.Headlight) - 1)) * Deg2Rad;
        public float BellRadius => (_t.bellRadius + _t.bellRadiusPerLevel * (_loadout.Level(ItemKind.Bell) - 1)) * Reach(ItemKind.Bell);
        public float BellCooldown => Cooldown(_t.bellCooldown);
        public float WhipCooldown => Cooldown(_t.whipCooldown);
        public float WhipRange => _t.whipRange + _t.whipRangePerLevel * (_loadout.Level(ItemKind.ChainWhip) - 1);
        public float SpikesRadius => (_t.spikesRadius + _t.spikesRadiusPerLevel * (_loadout.Level(ItemKind.TyreSpikes) - 1)) * Reach(ItemKind.TyreSpikes);

        /// <summary>Spoke blade radius; Wheel of Blades makes them bigger.</summary>
        public float SpokeRadius => _t.spokeRadius * Reach(ItemKind.SpokeCards);

        private float Power(ItemKind weapon) => _loadout.Evolved(weapon) ? Evolutions.DamageScale : 1f;
        private float Reach(ItemKind weapon) => _loadout.Evolved(weapon) ? Evolutions.ReachScale : 1f;

        /// <summary>Spikes on the road (a ring buffer); a spike is live while its life is above 0.</summary>
        public float[] SpikeX { get; } = new float[MaxSpikes];
        public float[] SpikeY { get; } = new float[MaxSpikes];
        public float[] SpikeLife { get; } = new float[MaxSpikes];

        /// <summary>Pannier Drone shots (a ring buffer); a shot is live while its life is above 0.</summary>
        public float[] ShotX { get; } = new float[MaxShots];
        public float[] ShotY { get; } = new float[MaxShots];
        public float[] ShotLife { get; } = new float[MaxShots];
        private readonly float[] _shotVX = new float[MaxShots];
        private readonly float[] _shotVY = new float[MaxShots];
        private readonly float[] _shotDamage = new float[MaxShots];

        private float Cooldown(float seconds) => seconds * (1f - _t.gearCooldownPerLevel * _loadout.Level(ItemKind.GearRatio));

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

            if (_loadout.Owns(ItemKind.ChainWhip))
            {
                _whipTimer += dt;
                if (_whipTimer >= WhipCooldown)
                {
                    _whipTimer = 0f;
                    CrackWhip(bike, bonus, events);
                }
            }

            if (_loadout.Owns(ItemKind.TyreSpikes))
            {
                LayAndBurnSpikes(dt, bike, bonus);
            }

            if (_loadout.Owns(ItemKind.PannierDrone))
            {
                _pannierTimer += dt;
                if (_pannierTimer >= Cooldown(_t.pannierCooldown))
                {
                    _pannierTimer = 0f;
                    LaunchVolley(bike, bonus);
                }
            }

            MoveShots(dt);
        }

        /// <summary>Level-many shots fanned at the nearest drone in range; nothing in range, nothing fired.</summary>
        private void LaunchVolley(BikeMotor bike, float bonus)
        {
            Vector2 p = bike.Position;
            int target = Nearest(p.X, p.Y, _t.pannierRange);
            if (target < 0)
            {
                return;
            }

            int level = _loadout.Level(ItemKind.PannierDrone);
            float aim = MathF.Atan2(_swarm.Y[target] - p.Y, _swarm.X[target] - p.X);
            float damage = (_t.pannierDamage + _t.pannierDamagePerLevel * (level - 1)) * bonus;
            for (int s = 0; s < level; s++)
            {
                float angle = aim + (s - (level - 1) * 0.5f) * 0.25f;
                int i = _nextShot;
                _nextShot = (_nextShot + 1) % MaxShots;
                ShotX[i] = p.X;
                ShotY[i] = p.Y;
                _shotVX[i] = MathF.Cos(angle) * _t.pannierShotSpeed;
                _shotVY[i] = MathF.Sin(angle) * _t.pannierShotSpeed;
                ShotLife[i] = _t.pannierShotLife;
                _shotDamage[i] = damage;
            }
        }

        /// <summary>Shots bend toward the nearest drone around them and burst on the first one they touch.</summary>
        private void MoveShots(float dt)
        {
            float maxTurn = _t.pannierTurnRate * Deg2Rad * dt;
            for (int s = 0; s < MaxShots; s++)
            {
                if (ShotLife[s] <= 0f)
                {
                    continue;
                }

                ShotLife[s] -= dt;
                int target = Nearest(ShotX[s], ShotY[s], HomingRadius);
                if (target >= 0)
                {
                    float heading = MathF.Atan2(_shotVY[s], _shotVX[s]);
                    float want = MathF.Atan2(_swarm.Y[target] - ShotY[s], _swarm.X[target] - ShotX[s]);
                    heading += Math.Clamp(BikeMotor.WrapAngle(want - heading), -maxTurn, maxTurn);
                    _shotVX[s] = MathF.Cos(heading) * _t.pannierShotSpeed;
                    _shotVY[s] = MathF.Sin(heading) * _t.pannierShotSpeed;
                }

                ShotX[s] += _shotVX[s] * dt;
                ShotY[s] += _shotVY[s] * dt;

                int found = _swarm.Grid.Query(ShotX[s], ShotY[s], ShotRadius + _t.MaxEnemyRadius, _near);
                for (int k = 0; k < found; k++)
                {
                    int i = _near[k];
                    float dx = _swarm.X[i] - ShotX[s], dy = _swarm.Y[i] - ShotY[s];
                    float reach = ShotRadius + _t.Stats(_swarm.Kind[i]).radius;
                    if (_swarm.Hp[i] > 0f && dx * dx + dy * dy <= reach * reach)
                    {
                        Hit(i, _shotDamage[s], ItemKind.PannierDrone);
                        ShotLife[s] = 0f;
                        break;
                    }
                }
            }
        }

        /// <summary>The closest live drone within <paramref name="range"/>, or -1.</summary>
        private int Nearest(float x, float y, float range)
        {
            int found = _swarm.Grid.Query(x, y, range, _near);
            int best = -1;
            float bestD2 = range * range;
            for (int k = 0; k < found; k++)
            {
                int i = _near[k];
                float dx = _swarm.X[i] - x, dy = _swarm.Y[i] - y;
                float d2 = dx * dx + dy * dy;
                if (_swarm.Hp[i] > 0f && d2 <= bestD2)
                {
                    best = i;
                    bestD2 = d2;
                }
            }

            return best;
        }

        /// <summary>A 120° arc on the side the bike is turning toward; riding straight, it alternates sides.</summary>
        private void CrackWhip(BikeMotor bike, float bonus, List<RideEvent> events)
        {
            int side = bike.TurnSide != 0 ? bike.TurnSide : -_lastWhipSide;
            _lastWhipSide = side;
            float centre = bike.Heading + side * MathF.PI / 2f;
            float cx = MathF.Cos(centre), cy = MathF.Sin(centre);
            float range = WhipRange;
            float damage = (_t.whipDamage + _t.whipDamagePerLevel * (_loadout.Level(ItemKind.ChainWhip) - 1)) * bonus;
            Vector2 p = bike.Position;
            events.Add(new RideEvent { type = RideEventType.WhipCracked, x = p.X, y = p.Y, value = centre });

            int found = _swarm.Grid.Query(p.X, p.Y, range + _t.MaxEnemyRadius, _near);
            for (int k = 0; k < found; k++)
            {
                int i = _near[k];
                float dx = _swarm.X[i] - p.X, dy = _swarm.Y[i] - p.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d <= range + _t.Stats(_swarm.Kind[i]).radius && d > 1e-4f && (dx * cx + dy * cy) / d >= 0.5f) // cos 60°
                {
                    Hit(i, damage, ItemKind.ChainWhip);
                }
            }
        }

        /// <summary>Drops a spike behind the bike every so often; faster riding leaves longer-lived spikes.</summary>
        private void LayAndBurnSpikes(float dt, BikeMotor bike, float bonus)
        {
            _spikeTimer += dt;
            if (_spikeTimer >= _t.spikesEvery)
            {
                _spikeTimer = 0f;
                SpikeX[_nextSpike] = bike.Position.X;
                SpikeY[_nextSpike] = bike.Position.Y;
                SpikeLife[_nextSpike] = _t.spikesLifeAtCruise * bike.Speed / bike.CruiseSpeed;
                _nextSpike = (_nextSpike + 1) % MaxSpikes;
            }

            float radius = SpikesRadius;
            float damage = (_t.spikesDps + _t.spikesDpsPerLevel * (_loadout.Level(ItemKind.TyreSpikes) - 1)) * Power(ItemKind.TyreSpikes) * bonus * dt;
            for (int s = 0; s < MaxSpikes; s++)
            {
                if (SpikeLife[s] <= 0f)
                {
                    continue;
                }

                SpikeLife[s] -= dt;
                int found = _swarm.Grid.Query(SpikeX[s], SpikeY[s], radius + _t.MaxEnemyRadius, _near);
                for (int k = 0; k < found; k++)
                {
                    int i = _near[k];
                    float dx = _swarm.X[i] - SpikeX[s], dy = _swarm.Y[i] - SpikeY[s];
                    float reach = radius + _t.Stats(_swarm.Kind[i]).radius;
                    if (dx * dx + dy * dy <= reach * reach)
                    {
                        Hit(i, damage, ItemKind.TyreSpikes);
                    }
                }
            }
        }

        private void FireHeadlight(float dt, BikeMotor bike, float bonus)
        {
            int level = _loadout.Level(ItemKind.Headlight);
            float range = HeadlightRange;
            float cosHalf = MathF.Cos(HeadlightHalfAngle);
            float damage = (_t.headlightDps + _t.headlightDpsPerLevel * (level - 1)) * Power(ItemKind.Headlight) * bonus * dt;
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
            float damage = (_t.spokeDps + _t.spokeDpsPerLevel * (level - 1)) * Power(ItemKind.SpokeCards) * bonus * dt;

            for (int b = 0; b < blades; b++)
            {
                float angle = SpokeAngle + b * 2f * MathF.PI / blades;
                float bx = bike.Position.X + MathF.Cos(angle) * _t.spokeOrbit;
                float by = bike.Position.Y + MathF.Sin(angle) * _t.spokeOrbit;
                int found = _swarm.Grid.Query(bx, by, SpokeRadius + _t.MaxEnemyRadius, _near);
                for (int k = 0; k < found; k++)
                {
                    int i = _near[k];
                    float dx = _swarm.X[i] - bx, dy = _swarm.Y[i] - by;
                    float reach = SpokeRadius + _t.Stats(_swarm.Kind[i]).radius;
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
            float damage = (_t.bellDamage + _t.bellDamagePerLevel * (level - 1)) * Power(ItemKind.Bell) * bonus;
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
