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
        WhipCracked,
        Drafted,

        /// <summary>A scripted shift event started; value is a <see cref="ShiftEvent"/>.</summary>
        ShiftEvent,

        /// <summary>The boss went down: the shift is won.</summary>
        Won,

        /// <summary>An elite drone joined (the first one gets a drafting hint).</summary>
        EliteArrived,
    }

    public enum ShiftEvent
    {
        Ring,
        Horde,
        EliteGroup,
        Boss,
    }

    /// <summary>Something the view should react to (sound, haptics, flash, text).</summary>
    public struct RideEvent
    {
        public RideEventType type;
        public float x;
        public float y;

        /// <summary>Damage for Hit, XP for Collected, the new level for LevelUp, the radius for BellRang, the arc's centre angle (radians) for WhipCracked.</summary>
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
        public const int MaxOrbs = 64;

        private readonly float[] _orbVX = new float[MaxOrbs];
        private readonly float[] _orbVY = new float[MaxOrbs];
        private int _nextOrb;
        private float _nextElite;
        private int _shiftEvent;
        private float _xpCarry;
        private bool _bossSpawned;
        private float _bossSummon;
        private float _bossFan;
        private float _draftTime;
        private float _boostLeft;

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
            _nextElite = tuning.firstEliteAt;
            RerollsLeft = tuning.rerolls;
            TopUp();
        }

        /// <summary>Zapper orbs in flight (a ring buffer); an orb is live while its life is above 0.</summary>
        public float[] OrbX { get; } = new float[MaxOrbs];
        public float[] OrbY { get; } = new float[MaxOrbs];
        public float[] OrbLife { get; } = new float[MaxOrbs];

        /// <summary>The drafting speed burst is on.</summary>
        public bool Boosted => _boostLeft > 0f;

        /// <summary>0..1 of the time needed behind an elite to trigger the burst.</summary>
        public float DraftProgress => _draftTime / _t.draftSeconds;

        public BikeMotor Bike { get; }
        public Swarm Swarm { get; }
        public Parcels Parcels { get; }
        public Loadout Loadout { get; }
        public Arsenal Arsenal { get; }
        public RideTuning Tuning => _t;
        public float Hp { get; private set; }
        public float Time { get; private set; }
        public bool Over { get; private set; }

        /// <summary>The boss is down: the shift ended in a win (<see cref="Over"/> is also true).</summary>
        public bool Won { get; private set; }
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

        /// <summary>Drones kept alive at this moment, from the wave curve.</summary>
        public int TargetEnemyCount => WaveDirector.Drones(_t.waves, Time);

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
            SpawnElites(events);
            RunShiftEvents(events);
            RunBoss(dt, events);
            Draft(dt, events);
            FireZappers(dt);
            MoveOrbs(dt, events);
            Arsenal.Fire(dt, Bike, events);
            Reap(events);
            TopUp();
            CollectParcels(dt, events);
            ResolveContact(events);
        }

        /// <summary>Card rerolls left this ride (from the garage).</summary>
        public int RerollsLeft { get; private set; }

        /// <summary>Swaps the cards on offer for a fresh roll, if a reroll is left.</summary>
        public bool Reroll()
        {
            if (PendingLevelUps == 0 || RerollsLeft == 0)
            {
                return false;
            }

            RerollsLeft--;
            RollOffer();
            return true;
        }

        /// <summary>Takes card <paramref name="index"/> of <see cref="Offer"/> for the first pending level-up.</summary>
        public void Choose(int index)
        {
            if (PendingLevelUps == 0 || index < 0 || index >= OfferCount)
            {
                throw new InvalidOperationException($"No card {index} on offer ({OfferCount} cards, {PendingLevelUps} level-ups pending).");
            }

            var item = Offer[index];
            if (Loadout.CanEvolve(item))
            {
                Loadout.Evolve(item);
            }
            else
            {
                Loadout.Upgrade(item);
            }

            PendingLevelUps--;
            RollOffer();
        }

        /// <summary>Lighter Frame (+speed), Better Brakes (keep the bonus while braking, softer turns), Energy Gel (regen).</summary>
        private void ApplyPassives(float dt)
        {
            _boostLeft = MathF.Max(0f, _boostLeft - dt);
            Bike.SpeedScale = (1f + _t.frameSpeedPerLevel * Loadout.Level(ItemKind.LighterFrame)) * (Boosted ? 1f + _t.draftBoost : 1f);
            Bike.KeepBonusWhileBraking = Loadout.Owns(ItemKind.BetterBrakes);
            Bike.TurnLossScale = MathF.Max(0f, 1f - _t.brakesTurnLossPerLevel * Loadout.Level(ItemKind.BetterBrakes));
            Hp = MathF.Min(_t.maxHp, Hp + _t.gelRegenPerLevel * Loadout.Level(ItemKind.EnergyGel) * dt);
        }

        /// <summary>An elite every minute, spawned ahead on the bike's line and riding the same way, a little slower.</summary>
        private void SpawnElites(List<RideEvent> events)
        {
            if (Time < _nextElite)
            {
                return;
            }

            _nextElite += _t.eliteEvery;
            Vector2 at = Bike.Position + Bike.Forward * (_t.spawnDistance * 0.7f);
            Swarm.Spawn(EnemyKind.Elite, at.X, at.Y, WaveDirector.HpScale(Time, _t.hpPerMinute), Bike.Heading);
            events.Add(new RideEvent { type = RideEventType.EliteArrived, x = at.X, y = at.Y });
        }

        /// <summary>The three scripted moments of the shift, each once, in order.</summary>
        private void RunShiftEvents(List<RideEvent> events)
        {
            if (_shiftEvent > (int)ShiftEvent.EliteGroup)
            {
                return;
            }

            var next = (ShiftEvent)_shiftEvent;
            float at = next == ShiftEvent.Ring ? _t.ringAt : next == ShiftEvent.Horde ? _t.hordeAt : _t.eliteGroupAt;
            if (Time < at)
            {
                return;
            }

            _shiftEvent++;
            Vector2 p = Bike.Position;
            float hp = WaveDirector.HpScale(Time, _t.hpPerMinute);
            switch (next)
            {
                case ShiftEvent.Ring:
                    for (int k = 0; k < _t.ringCount; k++)
                    {
                        Vector2 at2 = p + Direction(k * 2f * MathF.PI / _t.ringCount) * _t.ringRadius;
                        Swarm.Spawn(EnemyKind.Scout, at2.X, at2.Y, hp);
                    }

                    break;
                case ShiftEvent.Horde:
                    // A thick crowd off to one side, all charging in at once.
                    float side = _rng.Range(0f, 2f * MathF.PI);
                    Vector2 centre = p + Direction(side) * _t.spawnDistance;
                    for (int k = 0; k < _t.hordeCount; k++)
                    {
                        var kind = k % 5 == 0 ? EnemyKind.Hauler : EnemyKind.Scout;
                        Swarm.Spawn(kind, centre.X + _rng.Range(-3f, 3f), centre.Y + _rng.Range(-3f, 3f), hp);
                    }

                    break;
                case ShiftEvent.EliteGroup:
                    // Side by side ahead on the bike's line: three slipstreams to pick from.
                    Vector2 ahead = p + Bike.Forward * (_t.spawnDistance * 0.7f);
                    var across = new Vector2(-Bike.Forward.Y, Bike.Forward.X);
                    for (int k = 0; k < _t.eliteGroupCount; k++)
                    {
                        Vector2 at2 = ahead + across * ((k - (_t.eliteGroupCount - 1) * 0.5f) * 2.5f);
                        Swarm.Spawn(EnemyKind.Elite, at2.X, at2.Y, hp, Bike.Heading);
                    }

                    break;
            }

            events.Add(new RideEvent { type = RideEventType.ShiftEvent, x = p.X, y = p.Y, value = (float)next });
        }

        /// <summary>Riding in an elite's slipstream fills the draft meter; full, it fires the speed burst.</summary>
        private void Draft(float dt, List<RideEvent> events)
        {
            bool drafting = false;
            Vector2 p = Bike.Position;
            for (int i = 0; i < Swarm.Count && !drafting; i++)
            {
                // Elites, and the Freight Hauler while it charges: its slipstream is the safe spot.
                bool slipstream = Swarm.Kind[i] == EnemyKind.Elite || (Swarm.Kind[i] == EnemyKind.Freight && Swarm.Timer[i] > 0f);
                if (!slipstream)
                {
                    continue;
                }

                float near = _t.bikeRadius + _t.Stats(Swarm.Kind[i]).radius;
                float hx = MathF.Cos(Swarm.Heading[i]), hy = MathF.Sin(Swarm.Heading[i]);
                float rx = p.X - Swarm.X[i], ry = p.Y - Swarm.Y[i];
                float behind = -(rx * hx + ry * hy);
                float lateral = MathF.Abs(rx * hy - ry * hx);
                drafting = behind > near && behind <= near + _t.draftRange && lateral <= _t.draftLateral;
            }

            _draftTime = drafting ? _draftTime + dt : 0f;
            if (_draftTime >= _t.draftSeconds)
            {
                _draftTime = 0f;
                _boostLeft = _t.draftBoostSeconds;
                events.Add(new RideEvent { type = RideEventType.Drafted, x = p.X, y = p.Y });
            }
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
                int xp = Swarm.Kind[i] switch { EnemyKind.Hauler => _t.haulerXp, EnemyKind.Elite => _t.eliteXp, _ => _t.scoutXp };
                if (!Parcels.Add(x, y, xp))
                {
                    GainXp(xp, events); // no room on the road: straight into the bag
                }

                Kills++;
                var kind = Swarm.Kind[i];
                events.Add(new RideEvent { type = RideEventType.Killed, x = x, y = y, value = (float)kind });
                Swarm.RemoveAt(i); // the last drone moves into i; it was already checked
                if (Swarm.IsBoss(kind))
                {
                    Won = true;
                    Over = true;
                    events.Add(new RideEvent { type = RideEventType.Won, x = x, y = y });
                }

                if (kind == EnemyKind.Splitter)
                {
                    // Two scouts burst out; they land past the end of the list, so this loop won't reap them.
                    float hp = WaveDirector.HpScale(Time, _t.hpPerMinute);
                    Swarm.Spawn(EnemyKind.Scout, x - 0.35f, y, hp);
                    Swarm.Spawn(EnemyKind.Scout, x + 0.35f, y, hp);
                }
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
            // XP gain from the garage can be fractional (1 XP × 1.1): the remainder carries to the next parcel.
            _xpCarry += xp * _t.xpGain;
            int whole = (int)(_xpCarry + 1e-4f); // 1.1 summed ten times is 10.9999… in float
            _xpCarry -= whole;
            Xp += whole;
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
                Vector2 at = Bike.Position + Direction(angle) * _t.spawnDistance;
                int i = Swarm.Spawn(PickKind(), at.X, at.Y, WaveDirector.HpScale(Time, _t.hpPerMinute));
                if (i >= 0)
                {
                    Swarm.Timer[i] = _rng.Range(0f, _t.zapperEvery); // zappers don't all fire at once
                }
            }
        }

        /// <summary>Zappers and splitters join the mix once their time comes; the rest is haulers and scouts by the curve.</summary>
        private EnemyKind PickKind()
        {
            double roll = _rng.NextDouble();
            if (Time >= _t.zapperFrom && roll < _t.zapperShare)
            {
                return EnemyKind.Zapper;
            }

            if (Time >= _t.splitterFrom && roll < _t.zapperShare + _t.splitterShare)
            {
                return EnemyKind.Splitter;
            }

            return _rng.Chance(WaveDirector.HaulerShare(_t.waves, Time)) ? EnemyKind.Hauler : EnemyKind.Scout;
        }

        /// <summary>Zappers in range count down and fire a slow orb at where the bike is.</summary>
        private void FireZappers(float dt)
        {
            Vector2 p = Bike.Position;
            float reach = _t.zapperRange + 0.5f;
            for (int i = 0; i < Swarm.Count; i++)
            {
                if (Swarm.Kind[i] != EnemyKind.Zapper)
                {
                    continue;
                }

                float dx = p.X - Swarm.X[i], dy = p.Y - Swarm.Y[i];
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > reach || (Swarm.Timer[i] -= dt) > 0f)
                {
                    continue;
                }

                Swarm.Timer[i] = _t.zapperEvery;
                FireOrb(Swarm.X[i], Swarm.Y[i], MathF.Atan2(dy, dx));
            }
        }

        private void FireOrb(float x, float y, float angle)
        {
            int o = _nextOrb;
            _nextOrb = (_nextOrb + 1) % MaxOrbs;
            OrbX[o] = x;
            OrbY[o] = y;
            _orbVX[o] = MathF.Cos(angle) * _t.orbSpeed;
            _orbVY[o] = MathF.Sin(angle) * _t.orbSpeed;
            OrbLife[o] = _t.orbLife;
        }

        /// <summary>The Dispatcher: arrives at the end of the shift, calls drones around itself, fires fans of orbs.</summary>
        private void RunBoss(float dt, List<RideEvent> events)
        {
            Vector2 p = Bike.Position;
            if (!_bossSpawned && Time >= _t.shiftSeconds)
            {
                _bossSpawned = true;
                Vector2 at = p + Bike.Forward * (_t.spawnDistance * 0.8f);
                Swarm.Spawn(_t.map == MapKind.HarborRing ? EnemyKind.Freight : EnemyKind.Boss, at.X, at.Y);
                _bossSummon = 1f; // first wave a second after it shows
                _bossFan = _t.bossFanEvery;
                events.Add(new RideEvent { type = RideEventType.ShiftEvent, x = at.X, y = at.Y, value = (float)ShiftEvent.Boss });
            }

            if (!Swarm.HasBoss)
            {
                return;
            }

            int b = 0;
            while (!Swarm.IsBoss(Swarm.Kind[b]))
            {
                b++;
            }

            float bx = Swarm.X[b], by = Swarm.Y[b];
            if (Swarm.Kind[b] == EnemyKind.Freight)
            {
                // The Freight Hauler lines up on the bike and charges; between charges it lumbers after it.
                if (Swarm.Timer[b] <= 0f && (_bossFan -= dt) <= 0f) // a charge holds its line to the end
                {
                    _bossFan = _t.freightChargeEvery;
                    Swarm.Heading[b] = MathF.Atan2(p.Y - by, p.X - bx);
                    Swarm.Timer[b] = _t.freightChargeSeconds;
                }

                return;
            }

            if ((_bossSummon -= dt) <= 0f)
            {
                _bossSummon = _t.bossSummonEvery;
                float r = _t.boss.radius + 0.8f;
                for (int k = 0; k < _t.bossSummonCount; k++)
                {
                    float a = k * 2f * MathF.PI / _t.bossSummonCount;
                    Swarm.Spawn(EnemyKind.Scout, bx + MathF.Cos(a) * r, by + MathF.Sin(a) * r, WaveDirector.HpScale(Time, _t.hpPerMinute));
                }
            }

            if ((_bossFan -= dt) <= 0f)
            {
                _bossFan = _t.bossFanEvery;
                float aim = MathF.Atan2(p.Y - by, p.X - bx);
                for (int k = 0; k < _t.bossFanOrbs; k++)
                {
                    FireOrb(bx, by, aim + (k - (_t.bossFanOrbs - 1) * 0.5f) * 0.25f);
                }
            }
        }

        private void MoveOrbs(float dt, List<RideEvent> events)
        {
            Vector2 p = Bike.Position;
            float reach = _t.bikeRadius + _t.orbRadius;
            for (int o = 0; o < MaxOrbs; o++)
            {
                if (OrbLife[o] <= 0f)
                {
                    continue;
                }

                OrbLife[o] -= dt;
                OrbX[o] += _orbVX[o] * dt;
                OrbY[o] += _orbVY[o] * dt;
                float dx = OrbX[o] - p.X, dy = OrbY[o] - p.Y;
                if (dx * dx + dy * dy <= reach * reach)
                {
                    OrbLife[o] = 0f;
                    if (!Invulnerable)
                    {
                        TakeHit(_t.orbDamage, events);
                    }
                }
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

                TakeHit(stats.contactDamage, events);
                return;
            }
        }

        /// <summary>A hit from anything: starts the invulnerability window, may be dodged at speed, armour applies.</summary>
        private void TakeHit(float rawDamage, List<RideEvent> events)
        {
            if (Over)
            {
                return; // the boss went down this step: nothing after the win counts
            }

            Vector2 p = Bike.Position;
            _invulnerable = _t.invulnerableSeconds;
            if (Bike.CanDodge && _rng.Chance(_t.dodgeChance))
            {
                events.Add(new RideEvent { type = RideEventType.Dodged, x = p.X, y = p.Y });
                return;
            }

            float damage = rawDamage * (1f - _t.helmetArmourPerLevel * Loadout.Level(ItemKind.Helmet));
            Hp = MathF.Max(0f, Hp - damage);
            events.Add(new RideEvent { type = RideEventType.Hit, x = p.X, y = p.Y, value = damage });
            if (Hp <= 0f)
            {
                Over = true;
                events.Add(new RideEvent { type = RideEventType.Died, x = p.X, y = p.Y });
            }
        }

        private static Vector2 Direction(float angle) => new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
