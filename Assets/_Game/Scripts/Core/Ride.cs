using System;
using System.Collections.Generic;
using System.Numerics;
using Template.Core.Random;

namespace NightCourier.Core
{
    public enum RideEventType
    {
        Hit,
        Dodged,
        Died,
    }

    /// <summary>Something the view should react to (sound, haptics, flash, text).</summary>
    public struct RideEvent
    {
        public RideEventType type;
        public float x;
        public float y;

        /// <summary>Damage for Hit.</summary>
        public float value;

        public override string ToString() => $"{type}({value:0.#}) at {x:0.00},{y:0.00}";
    }

    /// <summary>
    /// One night shift, simulated in pure C#. The view calls <see cref="Step"/> with a fixed timestep and the
    /// joystick vector, then plays the returned events. Same seed and same inputs give the same ride.
    /// </summary>
    public sealed class Ride
    {
        private readonly RideTuning _t;
        private readonly SeededRandom _rng;
        private float _invulnerable;

        public Ride(RideTuning tuning, ulong seed)
        {
            _t = tuning;
            _rng = new SeededRandom(seed);
            Bike = new BikeMotor(tuning, Vector2.Zero, MathF.PI / 2f);
            Swarm = new Swarm(tuning);
            Hp = tuning.maxHp;
            TopUp();
        }

        public BikeMotor Bike { get; }
        public Swarm Swarm { get; }
        public RideTuning Tuning => _t;
        public float Hp { get; private set; }
        public float Time { get; private set; }
        public bool Over { get; private set; }
        public bool Invulnerable => _invulnerable > 0f;

        /// <summary>Drones kept alive at this moment: a steady ramp until the wave director takes over.</summary>
        public int TargetEnemyCount =>
            Math.Min(_t.targetEnemies, _t.startEnemies + (int)(_t.enemiesPerSecond * Time));

        public void Step(float dt, Vector2 stick, List<RideEvent> events)
        {
            if (Over)
            {
                return;
            }

            Time += dt;
            _invulnerable = MathF.Max(0f, _invulnerable - dt);
            Bike.Step(dt, stick);
            TopUp();
            RecycleStragglers();
            Swarm.Step(dt, Bike.Position);
            ResolveContact(events);
        }

        private void TopUp()
        {
            int target = Math.Min(TargetEnemyCount, Swarm.Capacity);
            while (Swarm.Count < target)
            {
                float angle = _rng.Range(0f, 2f * MathF.PI);
                var kind = _rng.Chance(_t.haulerShare) ? EnemyKind.Hauler : EnemyKind.Scout;
                Vector2 at = Bike.Position + Direction(angle) * _t.spawnDistance;
                Swarm.Spawn(kind, at.X, at.Y);
            }
        }

        /// <summary>Drones left far behind reappear off-screen ahead, so the swarm keeps up with a fast bike.</summary>
        private void RecycleStragglers()
        {
            float limit = _t.despawnDistance * _t.despawnDistance;
            for (int i = 0; i < Swarm.Count; i++)
            {
                float dx = Swarm.X[i] - Bike.Position.X, dy = Swarm.Y[i] - Bike.Position.Y;
                if (dx * dx + dy * dy <= limit)
                {
                    continue;
                }

                float angle = Bike.Heading + _rng.Range(-MathF.PI / 2f, MathF.PI / 2f);
                Vector2 at = Bike.Position + Direction(angle) * _t.spawnDistance;
                Swarm.X[i] = at.X;
                Swarm.Y[i] = at.Y;
            }
        }

        /// <summary>One contact per step at most; any hit or dodge starts the invulnerability window.</summary>
        private void ResolveContact(List<RideEvent> events)
        {
            if (Invulnerable)
            {
                return;
            }

            Vector2 p = Bike.Position;
            for (int i = 0; i < Swarm.Count; i++)
            {
                EnemyStats stats = _t.Stats(Swarm.Kind[i]);
                float reach = _t.bikeRadius + stats.radius;
                float dx = Swarm.X[i] - p.X, dy = Swarm.Y[i] - p.Y;
                if (dx * dx + dy * dy >= reach * reach)
                {
                    continue;
                }

                _invulnerable = _t.invulnerableSeconds;
                if (Bike.CanDodge && _rng.Chance(_t.dodgeChance))
                {
                    events.Add(new RideEvent { type = RideEventType.Dodged, x = p.X, y = p.Y });
                    return;
                }

                Hp = MathF.Max(0f, Hp - stats.contactDamage);
                events.Add(new RideEvent { type = RideEventType.Hit, x = p.X, y = p.Y, value = stats.contactDamage });
                if (Hp <= 0f)
                {
                    Over = true;
                    events.Add(new RideEvent { type = RideEventType.Died, x = p.X, y = p.Y });
                }

                return;
            }
        }

        private static Vector2 Direction(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
