using System;
using System.Numerics;
using NightCourier.Core;
using NUnit.Framework;

namespace NightCourier.Core.Tests
{
    public class BikeMotorTests
    {
        private const float Dt = 1f / 120f;
        private static readonly Vector2 Up = new Vector2(0f, 1f);

        private static BikeMotor NewBike(RideTuning t = null) =>
            new BikeMotor(t ?? RideTuning.Default(), Vector2.Zero, MathF.PI / 2f); // heading up

        private static void Ride(BikeMotor bike, Vector2 stick, float seconds)
        {
            for (int i = 0; i < (int)MathF.Round(seconds / Dt); i++)
            {
                bike.Step(Dt, stick);
            }
        }

        [Test]
        public void StartsAtCruiseAndReturnsToItWithNoInput()
        {
            var t = RideTuning.Default();
            var bike = NewBike(t);
            Assert.That(bike.Speed, Is.EqualTo(t.cruiseSpeed));

            Ride(bike, Up, 2f);
            Ride(bike, Vector2.Zero, 2f);

            Assert.That(bike.Speed, Is.EqualTo(t.cruiseSpeed).Within(1e-4f));
        }

        [Test]
        public void FullPushReachesMaxSpeedAtTheAccelerationRate()
        {
            var t = RideTuning.Default();
            var bike = NewBike(t);

            Ride(bike, Up, 0.5f);
            Assert.That(bike.Speed, Is.EqualTo(t.cruiseSpeed + t.acceleration * 0.5f).Within(0.01f));

            Ride(bike, Up, 1f);
            Assert.That(bike.Speed, Is.EqualTo(t.maxSpeed));
        }

        [Test]
        public void LightPushAsksForLessSpeedThanFullPush()
        {
            var light = NewBike();
            var full = NewBike();

            Ride(light, Up * 0.5f, 3f);
            Ride(full, Up, 3f);

            Assert.That(light.Speed, Is.GreaterThan(RideTuning.Default().cruiseSpeed));
            Assert.That(light.Speed, Is.LessThan(full.Speed));
        }

        [Test]
        public void PullingBackBrakesButNeverStops()
        {
            var t = RideTuning.Default();
            var bike = NewBike(t);

            bike.Step(Dt, -Up);
            Assert.That(bike.Braking, Is.True);
            Assert.That(bike.Speed, Is.LessThan(t.cruiseSpeed));

            // Braking while swinging round: speed bottoms out at the minimum, never 0, then the bike faces the stick.
            Ride(bike, -Up, 5f);
            Assert.That(bike.Speed, Is.GreaterThanOrEqualTo(t.minSpeed));
            Assert.That(Vector2.Dot(bike.Forward, -Up), Is.GreaterThan(0.999f));
            Assert.That(bike.Braking, Is.False);
        }

        [Test]
        public void SpeedStaysWithinMinAndMaxWhateverTheInput()
        {
            var t = RideTuning.Default();
            var bike = NewBike(t);
            var rng = new Template.Core.Random.SeededRandom(7);
            for (int i = 0; i < 6000; i++)
            {
                var stick = new Vector2(rng.Range(-1f, 1f), rng.Range(-1f, 1f));
                bike.Step(Dt, stick);
                Assert.That(bike.Speed, Is.InRange(t.minSpeed, t.maxSpeed));
            }
        }

        [Test]
        public void TurnRateIsLimitedAndSlowerAtSpeed()
        {
            var t = RideTuning.Default();
            var bike = NewBike(t);
            float before = bike.Heading;

            bike.Step(Dt, new Vector2(1f, 0f)); // a hard right at cruise

            float turned = MathF.Abs(BikeMotor.WrapAngle(bike.Heading - before)) * 180f / MathF.PI;
            Assert.That(turned, Is.EqualTo(t.turnRateAtCruise * Dt).Within(1e-3f));

            var fast = NewBike(t);
            Ride(fast, Up, 2f);
            Assert.That(fast.Speed, Is.EqualTo(t.maxSpeed));
            Assert.That(fast.TurnRate, Is.LessThan(bike.TurnRate));
        }

        [Test]
        public void HardTurnsCostMomentum()
        {
            var t = RideTuning.Default();
            var straight = NewBike(t);
            var circling = NewBike(t);

            Ride(straight, Up, 3f);
            for (int i = 0; i < 360; i++)
            {
                // Always ask for a direction 90° to the right of the heading: a full-rate turn.
                var right = new Vector2(MathF.Sin(circling.Heading), -MathF.Cos(circling.Heading));
                circling.Step(Dt, right);
            }

            Assert.That(circling.TurnAmount, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(circling.Speed, Is.LessThan(straight.Speed - 1f));
        }

        [Test]
        public void SpeedBonusScalesDamageAndUnlocksDodging()
        {
            var t = RideTuning.Default();
            var bike = NewBike(t);
            Assert.That(bike.DamageMultiplier, Is.EqualTo(1f + 4f / 6f * 0.5f).Within(1e-5f));
            Assert.That(bike.CanDodge, Is.False);

            Ride(bike, Up, 2f);
            Assert.That(bike.DamageMultiplier, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(bike.CanDodge, Is.True);
        }

        [Test]
        public void MovesAlongItsHeading()
        {
            var bike = NewBike();
            Ride(bike, Vector2.Zero, 1f);
            Assert.That(bike.Position.X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(bike.Position.Y, Is.EqualTo(RideTuning.Default().cruiseSpeed).Within(1e-3f));
        }
    }
}
