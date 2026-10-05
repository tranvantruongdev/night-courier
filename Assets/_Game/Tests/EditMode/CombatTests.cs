using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NightCourier.Core;
using NUnit.Framework;
using Template.Core.Random;

namespace NightCourier.Core.Tests
{
    public class CombatTests
    {
        private const float Dt = 1f / 120f;
        private static readonly Vector2 Up = new Vector2(0f, 1f);

        /// <summary>No spawning, the starting headlight on, drones parked.</summary>
        private static RideTuning Armed()
        {
            var t = RideTests.Quiet();
            t.headlightDps = RideTuning.Default().headlightDps;
            t.scout.speed = 0f;
            t.hauler.speed = 0f;
            return t;
        }

        [Test]
        public void XpCurveFollowsTheFormulaAndRises()
        {
            Assert.That(XpCurve.Needed(1), Is.EqualTo(12)); // 5 + 7 × 1
            Assert.That(XpCurve.Needed(2), Is.EqualTo(22)); // 5 + 7 × 2^1.3 = 22.2
            for (int level = 1; level < 40; level++)
            {
                Assert.That(XpCurve.Needed(level + 1), Is.GreaterThan(XpCurve.Needed(level)));
            }
        }

        [Test]
        public void LevelUpOffersDifferentUnmaxedItemsAndFavoursOwnedOnes()
        {
            var loadout = new Loadout();
            loadout.Upgrade(ItemKind.Headlight);
            for (int i = 0; i < Loadout.MaxLevel; i++)
            {
                loadout.Upgrade(ItemKind.Helmet); // maxed: never offered
            }

            var rng = new SeededRandom(5);
            var offer = new ItemKind[3];
            var scratch = new double[Loadout.ItemCount];
            int headlight = 0, bell = 0;
            for (int roll = 0; roll < 3000; roll++)
            {
                int n = LevelUpRoller.Roll(loadout, rng, offer, scratch);
                Assert.That(n, Is.EqualTo(3));
                Assert.That(offer.Distinct().Count(), Is.EqualTo(3));
                Assert.That(offer, Has.No.Member(ItemKind.Helmet));
                headlight += offer.Count(o => o == ItemKind.Headlight);
                bell += offer.Count(o => o == ItemKind.Bell);
            }

            Assert.That(headlight, Is.GreaterThan(bell * 1.3), $"owned {headlight} vs new {bell}");
        }

        [Test]
        public void LevelUpOffersFewerCardsNearTheEndAndNoneWhenAllIsMaxed()
        {
            var loadout = new Loadout();
            foreach (ItemKind item in Enum.GetValues(typeof(ItemKind)))
            {
                while (loadout.CanUpgrade(item) && item != ItemKind.Bell)
                {
                    loadout.Upgrade(item);
                }
            }

            foreach (ItemKind item in Enum.GetValues(typeof(ItemKind)))
            {
                if (loadout.CanEvolve(item))
                {
                    loadout.Evolve(item);
                }
            }

            var offer = new ItemKind[3];
            var scratch = new double[Loadout.ItemCount];
            Assert.That(LevelUpRoller.Roll(loadout, new SeededRandom(1), offer, scratch), Is.EqualTo(1));
            Assert.That(offer[0], Is.EqualTo(ItemKind.Bell));

            while (loadout.CanUpgrade(ItemKind.Bell))
            {
                loadout.Upgrade(ItemKind.Bell);
            }

            Assert.That(LevelUpRoller.Roll(loadout, new SeededRandom(1), offer, scratch), Is.EqualTo(1), "Bell + Helmet: Thunder Bell");
            loadout.Evolve(ItemKind.Bell);
            Assert.That(LevelUpRoller.Roll(loadout, new SeededRandom(1), offer, scratch), Is.EqualTo(0));
        }

        [Test]
        public void AMaxedWeaponWithItsPassiveEvolvesAndHitsTwiceAsHard()
        {
            var t = Armed();
            var ride = new Ride(t, 1);
            for (int i = 1; i < Loadout.MaxLevel; i++)
            {
                ride.Loadout.Upgrade(ItemKind.Headlight);
            }

            Assert.That(ride.Loadout.CanEvolve(ItemKind.Headlight), Is.False, "needs Gear Ratio too");
            ride.Loadout.Upgrade(ItemKind.GearRatio);
            Assert.That(ride.Loadout.CanEvolve(ItemKind.Headlight), Is.True);

            float DamageInOneStep()
            {
                while (ride.Swarm.Count > 0)
                {
                    ride.Swarm.RemoveAt(0); // one target only
                }

                ride.Swarm.Spawn(EnemyKind.Hauler, ride.Bike.Position.X, ride.Bike.Position.Y + 2f);
                ride.Swarm.Hp[ride.Swarm.Count - 1] = 1e6f;
                float before = ride.Arsenal.DamageBy[(int)ItemKind.Headlight];
                RideTests.Run(ride, Dt, r => Vector2.Zero);
                return ride.Arsenal.DamageBy[(int)ItemKind.Headlight] - before;
            }

            float plain = DamageInOneStep();
            float range = ride.Arsenal.HeadlightRange;
            ride.Parcels.Add(ride.Bike.Position.X, ride.Bike.Position.Y, ride.XpNeeded);
            ride.Step(Dt, Vector2.Zero, new List<RideEvent>());
            int card = Array.IndexOf(ride.Offer, ItemKind.Headlight, 0, ride.OfferCount);
            Assert.That(card, Is.GreaterThanOrEqualTo(0), "the High Beam card is on offer");
            ride.Choose(card);
            Assert.That(ride.Loadout.Evolved(ItemKind.Headlight), Is.True);
            Assert.That(ride.Arsenal.HeadlightRange, Is.EqualTo(range * Evolutions.ReachScale).Within(1e-4f));
            Assert.That(DamageInOneStep(), Is.EqualTo(plain * 2f).Within(plain * 0.02f));
        }

        [Test]
        public void HeadlightBurnsDronesAheadButNotBehind()
        {
            var ride = new Ride(Armed(), 1);
            ride.Swarm.Spawn(EnemyKind.Hauler, 0f, 2.5f);  // ahead
            ride.Swarm.Spawn(EnemyKind.Hauler, 0f, -2.5f); // behind

            RideTests.Run(ride, 0.25f, r => Vector2.Zero);

            Assert.That(ride.Swarm.Hp[0], Is.LessThan(ride.Tuning.hauler.hp));
            Assert.That(ride.Swarm.Hp[1], Is.EqualTo(ride.Tuning.hauler.hp));
            Assert.That(ride.Arsenal.DamageBy[(int)ItemKind.Headlight], Is.EqualTo(ride.Tuning.hauler.hp - ride.Swarm.Hp[0]).Within(1e-3f));
        }

        [Test]
        public void SpeedMultipliesDamage()
        {
            float DamageAt(float push)
            {
                var ride = new Ride(Armed(), 1);
                RideTests.Run(ride, 2f, r => Up * push); // settle at this speed
                ride.Swarm.Spawn(EnemyKind.Hauler, ride.Bike.Position.X, ride.Bike.Position.Y + 2f);
                float before = ride.Arsenal.DamageBy[(int)ItemKind.Headlight];
                RideTests.Run(ride, Dt, r => Up * push);
                return ride.Arsenal.DamageBy[(int)ItemKind.Headlight] - before;
            }

            float cruise = DamageAt(0f), max = DamageAt(1f);
            Assert.That(max / cruise, Is.EqualTo(1.5f / (1f + 4f / 6f * 0.5f)).Within(1e-3f));
        }

        [Test]
        public void KillsDropParcelsThatTheMagnetPullsInForXp()
        {
            var t = Armed();
            var ride = new Ride(t, 1);
            ride.Swarm.Spawn(EnemyKind.Scout, 0f, 3f); // burns down well before the bike reaches it

            var events = RideTests.Run(ride, 1f, r => Vector2.Zero);

            Assert.That(ride.Kills, Is.EqualTo(1));
            Assert.That(events.Count(e => e.type == RideEventType.Killed), Is.EqualTo(1));
            Assert.That(events.Count(e => e.type == RideEventType.Hit), Is.EqualTo(0));
            Assert.That(events.Count(e => e.type == RideEventType.Collected), Is.EqualTo(1), "the bike rides over its parcel");
            Assert.That(ride.Xp, Is.EqualTo(t.scoutXp));
            Assert.That(ride.Parcels.Count, Is.EqualTo(0));
        }

        [Test]
        public void ParcelsOutsideTheMagnetStayPut()
        {
            var ride = new Ride(Armed(), 1);
            ride.Parcels.Add(5f, 0f, 1);  // 5 to the side: out of reach
            ride.Parcels.Add(1.2f, 0f, 1); // inside the 1.5 magnet

            RideTests.Run(ride, Dt * 5, r => Vector2.Zero);

            Assert.That(ride.Parcels.X[0], Is.EqualTo(5f));
            Assert.That(ride.Parcels.X[1], Is.LessThan(1.2f));
        }

        [Test]
        public void ALevelUpFreezesTheRideUntilACardIsChosen()
        {
            var ride = new Ride(Armed(), 1);
            ride.Parcels.Add(0f, 0f, XpCurve.Needed(1)); // exactly one level's worth, right under the wheels
            var events = new List<RideEvent>();

            ride.Step(Dt, Vector2.Zero, events);

            Assert.That(events.Select(e => e.type), Has.Member(RideEventType.LevelUp));
            Assert.That(ride.Level, Is.EqualTo(2));
            Assert.That(ride.PendingLevelUps, Is.EqualTo(1));
            Assert.That(ride.OfferCount, Is.EqualTo(3));

            float time = ride.Time;
            ride.Step(Dt, Up, events);
            Assert.That(ride.Time, Is.EqualTo(time), "frozen while the cards are up");

            var picked = ride.Offer[1];
            int before = ride.Loadout.Level(picked);
            ride.Choose(1);
            Assert.That(ride.Loadout.Level(picked), Is.EqualTo(before + 1));
            Assert.That(ride.PendingLevelUps, Is.EqualTo(0));
            ride.Step(Dt, Up, events);
            Assert.That(ride.Time, Is.GreaterThan(time));
        }

        [Test]
        public void SpokeCardsCutDronesAroundTheBike()
        {
            var t = Armed();
            t.headlightDps = 0f;
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.SpokeCards);
            Assert.That(ride.Arsenal.SpokeBlades, Is.EqualTo(2));
            ride.Swarm.Spawn(EnemyKind.Hauler, -t.spokeOrbit, 0f); // where the second blade starts

            RideTests.Run(ride, 0.2f, r => Vector2.Zero);

            Assert.That(ride.Arsenal.DamageBy[(int)ItemKind.SpokeCards], Is.GreaterThan(0f));
        }

        [Test]
        public void TheBellHitsEverythingInRangeAndPushesItBack()
        {
            var t = Armed();
            t.headlightDps = 0f;
            t.bellCooldown = 0.05f; // rings before the bike rides away
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.Bell);
            ride.Swarm.Spawn(EnemyKind.Hauler, 2f, 0f); // in range
            ride.Swarm.Spawn(EnemyKind.Hauler, 6f, 0f); // out of range

            var events = RideTests.Run(ride, t.bellCooldown + Dt, r => Vector2.Zero);

            Assert.That(events.Count(e => e.type == RideEventType.BellRang), Is.EqualTo(1));
            Assert.That(ride.Swarm.Hp[0], Is.LessThan(t.hauler.hp));
            Assert.That(ride.Swarm.Hp[1], Is.EqualTo(t.hauler.hp));
            Assert.That(ride.Swarm.X[0], Is.GreaterThan(2f + t.bellKnockback * 0.9f));
        }

        [Test]
        public void GearRatioShortensCooldownsAndHelmetSoftensHits()
        {
            var t = Armed();
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.Bell);
            float plain = ride.Arsenal.BellCooldown;
            ride.Loadout.Upgrade(ItemKind.GearRatio);
            Assert.That(ride.Arsenal.BellCooldown, Is.EqualTo(plain * (1f - t.gearCooldownPerLevel)).Within(1e-5f));

            ride.Loadout.Upgrade(ItemKind.Helmet);
            ride.Loadout.Upgrade(ItemKind.Helmet);
            ride.Swarm.Spawn(EnemyKind.Hauler, 0f, 0.3f);
            ride.Swarm.Hp[0] = 1e6f; // survives the headlight
            var events = RideTests.Run(ride, Dt, r => Vector2.Zero);

            var hit = events.Single(e => e.type == RideEventType.Hit);
            Assert.That(hit.value, Is.EqualTo(t.hauler.contactDamage * (1f - 2f * t.helmetArmourPerLevel)).Within(1e-4f));
        }

        [Test]
        public void TheWhipCracksOnTheSideTheBikeTurnsToward()
        {
            var t = Armed();
            t.headlightDps = 0f;
            t.whipCooldown = 0.05f;
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.ChainWhip);
            ride.Swarm.Spawn(EnemyKind.Hauler, -2f, 0f); // left of a bike heading up
            ride.Swarm.Spawn(EnemyKind.Hauler, 2f, 0f);  // right

            var events = RideTests.Run(ride, t.whipCooldown + Dt, r => new Vector2(-1f, 0.3f)); // steering left

            Assert.That(events.Count(e => e.type == RideEventType.WhipCracked), Is.EqualTo(1));
            Assert.That(ride.Swarm.Hp[0], Is.LessThan(t.hauler.hp));
            Assert.That(ride.Swarm.Hp[1], Is.EqualTo(t.hauler.hp));
        }

        [Test]
        public void TyreSpikesBurnWhatFollowsTheBike()
        {
            var t = Armed();
            t.headlightDps = 0f;
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.TyreSpikes);
            RideTests.Run(ride, 1f, r => Vector2.Zero); // lays spikes from y = 0 to y = 4
            ride.Swarm.Spawn(EnemyKind.Hauler, 0.2f, 3f); // on the trail, behind the bike

            RideTests.Run(ride, 0.2f, r => Vector2.Zero);

            Assert.That(ride.Arsenal.DamageBy[(int)ItemKind.TyreSpikes], Is.GreaterThan(0f));
            Assert.That(ride.Swarm.Hp[0], Is.LessThan(t.hauler.hp));
        }

        [Test]
        public void ThePannierDroneHomesInOnDronesTheHeadlightMisses()
        {
            var t = Armed();
            t.headlightDps = 0f;
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.PannierDrone);
            ride.Swarm.Spawn(EnemyKind.Hauler, 4f, -1f); // off to the side and behind

            RideTests.Run(ride, t.pannierCooldown + 0.8f, r => Vector2.Zero);

            Assert.That(ride.Arsenal.DamageBy[(int)ItemKind.PannierDrone], Is.EqualTo(t.pannierDamage * ride.Bike.DamageMultiplier).Within(0.01f),
                "one volley of one shot, one hit");
        }

        [Test]
        public void ThePannierDroneHoldsFireWithNothingInRange()
        {
            var t = Armed();
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.PannierDrone);
            ride.Swarm.Spawn(EnemyKind.Hauler, 20f, 0f);

            RideTests.Run(ride, 2f, r => Vector2.Zero);

            Assert.That(ride.Arsenal.ShotLife, Has.All.LessThanOrEqualTo(0f));
        }

        [Test]
        public void LighterFrameRaisesTopSpeed()
        {
            var t = Armed();
            var ride = new Ride(t, 1);
            ride.Loadout.Upgrade(ItemKind.LighterFrame);
            ride.Loadout.Upgrade(ItemKind.LighterFrame);

            RideTests.Run(ride, 3f, r => Up);

            Assert.That(ride.Bike.Speed, Is.EqualTo(t.maxSpeed * (1f + 2f * t.frameSpeedPerLevel)).Within(1e-4f));
            Assert.That(ride.Bike.DamageMultiplier, Is.EqualTo(1.5f).Within(1e-4f), "the bonus is relative to your own top speed");
        }

        [Test]
        public void BetterBrakesKeepTheBonusWhileBraking()
        {
            float MultiplierWhileBraking(bool brakes)
            {
                var ride = new Ride(Armed(), 1);
                if (brakes)
                {
                    ride.Loadout.Upgrade(ItemKind.BetterBrakes);
                }

                RideTests.Run(ride, 2f, r => Up);  // full speed
                RideTests.Run(ride, 0.2f, r => -Up); // pull back
                Assert.That(ride.Bike.Braking, Is.True);
                return ride.Bike.DamageMultiplier;
            }

            Assert.That(MultiplierWhileBraking(true), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(MultiplierWhileBraking(false), Is.LessThan(1.45f));
        }

        [Test]
        public void EnergyGelHealsOverTime()
        {
            var t = Armed();
            var ride = new Ride(t, 1);
            ride.Swarm.Spawn(EnemyKind.Hauler, 0f, 0.3f);
            ride.Swarm.Hp[0] = 1e6f;
            RideTests.Run(ride, Dt, r => Vector2.Zero);
            float hurt = ride.Hp;
            Assert.That(hurt, Is.EqualTo(t.maxHp - t.hauler.contactDamage));

            for (int i = 0; i < Loadout.MaxLevel; i++)
            {
                ride.Loadout.Upgrade(ItemKind.EnergyGel);
            }

            RideTests.Run(ride, 5f, r => Vector2.Zero); // rides away from the parked hauler
            Assert.That(ride.Hp, Is.EqualTo(MathF.Min(t.maxHp, hurt + 5f * t.gelRegenPerLevel * 5f)).Within(0.05f));
        }

        /// <summary>
        /// Pacing guard. The plan wants the first level-up around 20 s for a player; this bot never aims the
        /// headlight, so it gets 15–40 s. Tighten the bound when the wave director shapes the early density.
        /// </summary>
        [Test]
        public void TheBotLevelsUpEarlyAndKeepsLevelling([Values(1UL, 3UL, 5UL)] ulong seed)
        {
            var ride = new Ride(RideTuning.Default(), seed);
            var events = new List<RideEvent>();
            float firstLevelUp = float.NaN;
            while (!ride.Over && ride.Time < 120f)
            {
                events.Clear();
                ride.Step(Dt, RideBot.Steer(ride), events);
                if (float.IsNaN(firstLevelUp) && events.Any(e => e.type == RideEventType.LevelUp))
                {
                    firstLevelUp = ride.Time;
                }

                while (ride.PendingLevelUps > 0)
                {
                    ride.Choose(0);
                }
            }

            TestContext.WriteLine($"seed {seed}: first level-up at {firstLevelUp:0.0} s; level {ride.Level} after {ride.Time:0} s, {ride.Kills} kills");
            Assert.That(firstLevelUp, Is.LessThanOrEqualTo(45f));
            Assert.That(ride.Level, Is.GreaterThanOrEqualTo(4));
        }
    }
}
