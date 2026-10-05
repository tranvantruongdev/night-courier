using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NightCourier.Core;
using NUnit.Framework;

namespace NightCourier.Core.Tests
{
    public class RideTests
    {
        private const float Dt = 1f / 120f;
        private static readonly Vector2 Up = new Vector2(0f, 1f);

        /// <summary>No automatic spawning and a harmless headlight, so a test places its own drones.</summary>
        internal static RideTuning Quiet()
        {
            var t = RideTuning.Default();
            t.startEnemies = 0;
            t.enemiesPerSecond = 0f;
            t.headlightDps = 0f;
            return t;
        }

        /// <summary>Steps the ride, taking the first card at every level-up.</summary>
        internal static List<RideEvent> Run(Ride ride, float seconds, Func<Ride, Vector2> stick)
        {
            var events = new List<RideEvent>();
            for (int i = 0; i < (int)MathF.Round(seconds / Dt); i++)
            {
                ride.Step(Dt, stick(ride), events);
                while (ride.PendingLevelUps > 0)
                {
                    ride.Choose(0);
                }
            }

            return events;
        }

        [Test]
        public void SameSeedAndInputsGiveTheSameRide()
        {
            var a = new Ride(RideTuning.Default(), 42);
            var b = new Ride(RideTuning.Default(), 42);

            Run(a, 20f, RideBot.Steer);
            Run(b, 20f, RideBot.Steer);

            Assert.That(b.Bike.Position, Is.EqualTo(a.Bike.Position));
            Assert.That(b.Hp, Is.EqualTo(a.Hp));
            Assert.That(b.Swarm.Count, Is.EqualTo(a.Swarm.Count));
            for (int i = 0; i < a.Swarm.Count; i++)
            {
                Assert.That(b.Swarm.X[i], Is.EqualTo(a.Swarm.X[i]));
                Assert.That(b.Swarm.Y[i], Is.EqualTo(a.Swarm.Y[i]));
            }
        }

        [Test]
        public void DroneCountRampsUpToTheTarget()
        {
            var t = RideTuning.Default();
            t.maxHp = 1e9f; // survive the whole ramp
            var ride = new Ride(t, 1);
            Assert.That(ride.Swarm.Count, Is.EqualTo(t.startEnemies));

            Run(ride, 10f, r => Up);
            Assert.That(ride.Swarm.Count, Is.EqualTo(ride.TargetEnemyCount));
            Assert.That(ride.Swarm.Count, Is.InRange(t.startEnemies + 19, t.startEnemies + 20)); // 2 a second

            Run(ride, 200f, r => Up);
            Assert.That(ride.Swarm.Count, Is.EqualTo(t.targetEnemies));
        }

        [Test]
        public void DronesSpawnOffScreenAroundTheBike()
        {
            var t = RideTuning.Default();
            var ride = new Ride(t, 5);

            for (int i = 0; i < ride.Swarm.Count; i++)
            {
                float d = Vector2.Distance(new Vector2(ride.Swarm.X[i], ride.Swarm.Y[i]), ride.Bike.Position);
                Assert.That(d, Is.EqualTo(t.spawnDistance).Within(1e-3f));
            }
        }

        [Test]
        public void DronesLeftFarBehindReappearAhead()
        {
            var t = Quiet();
            var ride = new Ride(t, 1);
            ride.Swarm.Spawn(EnemyKind.Hauler, 0f, -t.despawnDistance - 1f); // behind a bike heading up

            Run(ride, Dt, r => Up);

            float dy = ride.Swarm.Y[0] - ride.Bike.Position.Y;
            float d = Vector2.Distance(new Vector2(ride.Swarm.X[0], ride.Swarm.Y[0]), ride.Bike.Position);
            Assert.That(dy, Is.GreaterThanOrEqualTo(-1e-3f));
            Assert.That(d, Is.LessThan(t.spawnDistance + 0.1f));
        }

        [Test]
        public void ContactHurtsThenGivesHalfASecondOfInvulnerability()
        {
            var t = Quiet();
            t.scout.speed = 0f; // parked on the road
            var ride = new Ride(t, 1);
            ride.Swarm.Spawn(EnemyKind.Scout, 0f, 0.5f);

            var events = Run(ride, Dt, r => Vector2.Zero);
            Assert.That(events.Select(e => e.type), Is.EqualTo(new[] { RideEventType.Hit }));
            Assert.That(events[0].value, Is.EqualTo(t.scout.contactDamage));
            Assert.That(ride.Hp, Is.EqualTo(t.maxHp - t.scout.contactDamage));
            Assert.That(ride.Invulnerable, Is.True);

            // A second drone right on the bike can't hit during the window.
            ride.Swarm.Spawn(EnemyKind.Hauler, ride.Bike.Position.X, ride.Bike.Position.Y + 0.1f);
            events = Run(ride, t.invulnerableSeconds - 2f * Dt, r => Vector2.Zero);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void FastBikesDodge()
        {
            var t = Quiet();
            t.dodgeChance = 1f;
            t.scout.speed = 0f;
            var ride = new Ride(t, 1);
            Run(ride, 2f, r => Up); // full push: max speed
            Assert.That(ride.Bike.CanDodge, Is.True);

            ride.Swarm.Spawn(EnemyKind.Scout, ride.Bike.Position.X, ride.Bike.Position.Y + 0.3f);
            var events = Run(ride, Dt, r => Up);

            Assert.That(events.Select(e => e.type), Is.EqualTo(new[] { RideEventType.Dodged }));
            Assert.That(ride.Hp, Is.EqualTo(t.maxHp));
        }

        [Test]
        public void TheRideEndsAtZeroHp()
        {
            var t = Quiet();
            t.maxHp = 10f;
            t.hauler.speed = 0f;
            var ride = new Ride(t, 1);
            ride.Swarm.Spawn(EnemyKind.Hauler, 0f, 0.2f);

            var events = Run(ride, Dt, r => Vector2.Zero);

            Assert.That(events.Select(e => e.type), Is.EqualTo(new[] { RideEventType.Hit, RideEventType.Died }));
            Assert.That(ride.Over, Is.True);
            Assert.That(ride.Hp, Is.EqualTo(0f));

            float time = ride.Time;
            Run(ride, 1f, r => Up);
            Assert.That(ride.Time, Is.EqualTo(time), "nothing moves after the end");
        }

        [Test]
        public void TheBotOutlastsRidingStraight([Values(1UL, 2UL, 3UL, 4UL, 5UL)] ulong seed)
        {
            var straight = new Ride(RideTuning.Default(), seed);
            var bot = new Ride(RideTuning.Default(), seed);

            Run(straight, 120f, r => Vector2.Zero);
            Run(bot, 120f, RideBot.Steer);

            TestContext.WriteLine($"seed {seed}: straight {straight.Time:0.0} s, bot {bot.Time:0.0} s, hp {bot.Hp}");
            Assert.That(bot.Time, Is.GreaterThan(straight.Time));
        }
    }
}
