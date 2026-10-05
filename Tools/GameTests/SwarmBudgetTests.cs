using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using NightCourier.Core;
using NUnit.Framework;

namespace NightCourier.Core.Tests
{
    /// <summary>
    /// dotnet only: Unity's Mono doesn't report per-thread allocations. The frame-time numbers that matter
    /// come from the phone; this guards the "0 B of garbage per frame" rule and logs a desktop baseline.
    /// </summary>
    public class SwarmBudgetTests
    {
        private const float Dt = 1f / 120f;

        [TestCase(100)]
        [TestCase(300)]
        [TestCase(500)]
        public void SteppingTheRideAllocatesNothing(int drones)
        {
            var t = RideTuning.Default();
            t.startEnemies = drones;
            t.targetEnemies = drones;
            t.maxHp = 1e9f;
            var ride = new Ride(t, 9);
            var events = new List<RideEvent>(64);
            var stick = new Vector2(0.3f, 1f);

            for (int i = 0; i < 120; i++)
            {
                events.Clear();
                ride.Step(Dt, stick, events);
            }

            var clock = new Stopwatch(); // allocated before the count starts
            long before = GC.GetAllocatedBytesForCurrentThread();
            clock.Start();
            const int steps = 1200;
            for (int i = 0; i < steps; i++)
            {
                events.Clear();
                ride.Step(Dt, stick, events);
            }

            clock.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"{drones} drones: {clock.Elapsed.TotalMilliseconds * 1000.0 / steps:0.0} µs per step, {allocated} B allocated");
            Assert.That(ride.Swarm.Count, Is.EqualTo(drones));
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
