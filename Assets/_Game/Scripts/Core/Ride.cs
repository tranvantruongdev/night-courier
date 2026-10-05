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
        Killed,
        Collected,
        LevelUp,
        BellRang,
    }

    /// <summary>Something the view should react to (sound, haptics, flash, text).</summary>
    public struct RideEvent
    {
        public RideEventType type;
        public float x;
        public float y;

        /// <summary>Damage for Hit, XP for Collected, the new level for LevelUp, the radius for BellRang.</summary>
        public float value;

        public override string ToString() => $"{type}({value:0.#}) at {x:0.00},{y:0.00}";
    }

    /// <summary>
    /// One night shift, simulated in pure C#. The view calls <see cref="Step"/> with a fixed timestep and the
    /// joystick vector, then plays the returned events. A level-up freezes the ride until <see cref="Choose"/>
    /// picks one of the offered cards. Same seed and same inputs give the same ride.
    /// </summary>
    public sealed class Ride
    {
        private readonly RideTuning _t;
        private readonly SeededRandom _rng;
        private readonly double[] _rollScratch = new double[Loadout.ItemCount];
        private float _invulnerable;

        public Ride(RideTuning tuning, ulong seed)
        {
            _t = tuning;
            _rng = new SeededRandom(seed);
            Bike = new BikeMotor(tuning, Vector2.Zero, MathF.PI / 2f);
            Swarm = new Swarm(tuning);
            Parcels = new Parcels(tuning.maxParcels);
            Loadout = new Loadout();
            Loadout.Upgrade(ItemKind.Headlight); // Kai starts with a headlight
            Arsenal = new Arsenal(tuning, Loadout, Swarm);
            Hp = tuning.maxHp;
            TopUp();
        }

        public BikeMotor Bike { get; }
        public Swarm Swarm { get; }
        public Parcels Parcels { get; }
        public Loadout Loadout { get; }
        public Arsenal Arsenal { get; }
        public RideTuning Tuning => _t;
        public float Hp { get; private set; }
        public float Time { get; private set; }
        public bool Over { get; private set; }
        public bool Invulnerable => _invulnerable > 0f;
        public int Kills { get; private set; }
        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int XpNeeded => XpCurve.Needed(Level);

        /// <summary>Level-ups waiting for a card. The ride doesn't move while this is above 0.</summary>
        public int PendingLevelUps { get; private set; }

        /// <summary>The cards on offer for the first pending level-up; <see cref="OfferCount"/> of them are valid.</summary>
        public ItemKind[] Offer { get; } = new ItemKind[LevelUpRoller.Cards];
        public int OfferCount { get; private set; }

        public float MagnetRadius => _t.magnetRadius * (1f + _t.basketMagnetPerLevel * Loadout.Level(ItemKind.BigBasket));

        /// <summary>Drones kept alive at this moment: a steady ramp until the wave director takes over.</summary>
        public int TargetEnemyCount =>
            Math.Min(_t.targetEnemies, _t.startEnemies + (int)(_t.enemiesPerSecond * Time));

        public void Step(float dt, Vector2 stick, List<RideEvent> events)
        {
            if (Over || PendingLevelUps > 0)
            {
                return;
            }

            Time += dt;
            _invulnerable = MathF.Max(0f, _invulnerable - dt);
            ApplyPassives(dt);
            Bike.Step(dt, stick);
            RecycleStragglers();
            Swarm.Step(dt, Bike.Position);
            Arsenal.Fire(dt, Bike, events);
            Reap(events);
            TopUp();
            CollectParcels(dt, events);
            ResolveContact(events);
        }

        /// <summary>Takes card <paramref name="index"/> of <see cref="Offer"/> for the first pending level-up.</summary>
        public void Choose(int index)
        {
            if (PendingLevelUps == 0 || index < 0 || index >= OfferCount)
            {
                throw new InvalidOperationException($"No card {index} on offer ({OfferCount} cards, {PendingLevelUps} level-ups pending).");
            }

            Loadout.Upgrade(Offer[index]);
            PendingLevelUps--;
            RollOffer();
        }

        /// <summary>Lighter Frame (+speed), Better Brakes (keep the bonus while braking, softer turns), Energy Gel (regen).</summary>
        private void ApplyPassives(float dt)
        {
            Bike.SpeedScale = 1f + _t.frameSpeedPerLevel * Loadout.Level(ItemKind.LighterFrame);
            Bike.KeepBonusWhileBraking = Loadout.Owns(ItemKind.BetterBrakes);
            Bike.TurnLossScale = MathF.Max(0f, 1f - _t.brakesTurnLossPerLevel * Loadout.Level(ItemKind.BetterBrakes));
            Hp = MathF.Min(_t.maxHp, Hp + _t.gelRegenPerLevel * Loadout.Level(ItemKind.EnergyGel) * dt);
        }

        private void Reap(List<RideEvent> events)
        {
            for (int i = Swarm.Count - 1; i >= 0; i--)
            {
                if (Swarm.Hp[i] > 0f)
                {
                    continue;
                }

                float x = Swarm.X[i], y = Swarm.Y[i];
                int xp = Swarm.Kind[i] == EnemyKind.Hauler ? _t.haulerXp : _t.scoutXp;
                if (!Parcels.Add(x, y, xp))
                {
                    GainXp(xp, events); // no room on the road: straight into the bag
                }

                Kills++;
                events.Add(new RideEvent { type = RideEventType.Killed, x = x, y = y, value = (float)Swarm.Kind[i] });
                Swarm.RemoveAt(i); // the last drone moves into i; it was already checked
            }
        }

        private void CollectParcels(float dt, List<RideEvent> events)
        {
            float magnet = MagnetRadius, pull = _t.parcelPullSpeed * dt;
            Vector2 p = Bike.Position;
            for (int i = Parcels.Count - 1; i >= 0; i--)
            {
                float dx = p.X - Parcels.X[i], dy = p.Y - Parcels.Y[i];
                float d2 = dx * dx + dy * dy;
                if (d2 <= _t.collectRadius * _t.collectRadius)
                {
                    int xp = Parcels.Value[i];
                    Parcels.RemoveAt(i);
                    events.Add(new RideEvent { type = RideEventType.Collected, x = p.X, y = p.Y, value = xp });
                    GainXp(xp, events);
                }
                else if (d2 <= magnet * magnet)
                {
                    float d = MathF.Sqrt(d2);
                    float move = MathF.Min(d, pull);
                    Parcels.X[i] += dx / d * move;
                    Parcels.Y[i] += dy / d * move;
                }
            }
        }

        private void GainXp(int xp, List<RideEvent> events)
        {
            Xp += xp;
            while (Xp >= XpNeeded)
            {
                Xp -= XpNeeded;
                Level++;
                PendingLevelUps++;
                events.Add(new RideEvent { type = RideEventType.LevelUp, x = Bike.Position.X, y = Bike.Position.Y, value = Level });
            }

            if (PendingLevelUps > 0 && OfferCount == 0)
            {
                RollOffer();
            }
        }

        /// <summary>Rolls the cards for the next pending level-up. With everything maxed, each level-up heals instead.</summary>
        private void RollOffer()
        {
            OfferCount = 0;
            while (PendingLevelUps > 0)
            {
                OfferCount = LevelUpRoller.Roll(Loadout, _rng, Offer, _rollScratch);
                if (OfferCount > 0)
                {
                    return;
                }

                PendingLevelUps--;
                Hp = MathF.Min(_t.maxHp, Hp + _t.maxHp * 0.2f);
            }
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

                float damage = stats.contactDamage * (1f - _t.helmetArmourPerLevel * Loadout.Level(ItemKind.Helmet));
                Hp = MathF.Max(0f, Hp - damage);
                events.Add(new RideEvent { type = RideEventType.Hit, x = p.X, y = p.Y, value = damage });
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
