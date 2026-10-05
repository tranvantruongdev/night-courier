using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NightCourier.Core;
using NUnit.Framework;
using Template.Core.Random;

namespace NightCourier.Core.Tests
{
    public class SwarmTests
    {
        private const float Dt = 1f / 120f;

        [Test]
        public void GridQueryFindsEveryPointABruteForceScanFinds()
        {
            var rng = new SeededRandom(3);
            const int n = 500;
            var xs = new float[n];
            var ys = new float[n];
            for (int i = 0; i < n; i++)
            {
                // Spread over negative and positive coordinates, far from the origin too.
                xs[i] = rng.Range(-40f, 40f) + 1000f * (i % 3 - 1);
                ys[i] = rng.Range(-40f, 40f);
            }

            var grid = new SpatialGrid(2f, n, buckets: 64); // few buckets: plenty of hash collisions
            grid.Build(xs, ys, n);
            var results = new int[n];

            for (int q = 0; q < 200; q++)
            {
                int anchor = rng.Range(0, n);
                float qx = xs[anchor] + rng.Range(-2f, 2f), qy = ys[anchor] + rng.Range(-2f, 2f);
                float r = rng.Range(0.1f, 5f);

                int found = grid.Query(qx, qy, r, results);
                var candidates = results.Take(found).ToList();
                Assert.That(candidates.Distinct().Count(), Is.EqualTo(found), "no duplicates");

                var near = Enumerable.Range(0, n)
                    .Where(i => (xs[i] - qx) * (xs[i] - qx) + (ys[i] - qy) * (ys[i] - qy) <= r * r);
                Assert.That(candidates, Is.SupersetOf(near));
            }
        }

        [Test]
        public void GridQueryStopsAtTheResultsBuffer()
        {
            var xs = new float[10];
            var ys = new float[10];
            var grid = new SpatialGrid(2f, 10);
            grid.Build(xs, ys, 10);

            Assert.That(grid.Query(0f, 0f, 1f, new int[4]), Is.EqualTo(4));
        }

        [Test]
        public void DronesCloseInOnTheTarget()
        {
            var t = RideTuning.Default();
            var swarm = new Swarm(t);
            swarm.Spawn(EnemyKind.Scout, 10f, 0f);
            swarm.Spawn(EnemyKind.Hauler, -10f, 0f);

            for (int i = 0; i < 120; i++)
            {
                swarm.Step(Dt, Vector2.Zero);
            }

            Assert.That(swarm.X[0], Is.EqualTo(10f - t.scout.speed).Within(1e-3f));
            Assert.That(swarm.X[1], Is.EqualTo(-10f + t.hauler.speed).Within(1e-3f));
        }

        [Test]
        public void OverlappingDronesSpreadOut()
        {
            var t = RideTuning.Default();
            var swarm = new Swarm(t);
            var rng = new SeededRandom(11);
            for (int i = 0; i < 40; i++)
            {
                swarm.Spawn(EnemyKind.Scout, rng.Range(-1f, 1f), rng.Range(-1f, 1f)); // a tight clump
            }

            // Chasing a far target, so seeking moves them together and only separation sets them apart.
            var target = new Vector2(1000f, 0f);
            for (int i = 0; i < 240; i++)
            {
                swarm.Step(Dt, target);
            }

            float closest = float.MaxValue;
            for (int a = 0; a < swarm.Count; a++)
            {
                for (int b = a + 1; b < swarm.Count; b++)
                {
                    float dx = swarm.X[a] - swarm.X[b], dy = swarm.Y[a] - swarm.Y[b];
                    closest = MathF.Min(closest, MathF.Sqrt(dx * dx + dy * dy));
                }
            }

            Assert.That(closest, Is.GreaterThan(t.scout.radius)); // overlap under half a body
        }

        [Test]
        public void DronesOnTheExactSameSpotSplit()
        {
            var t = RideTuning.Default();
            var swarm = new Swarm(t);
            swarm.Spawn(EnemyKind.Scout, 5f, 5f);
            swarm.Spawn(EnemyKind.Scout, 5f, 5f);

            for (int i = 0; i < 120; i++)
            {
                swarm.Step(Dt, new Vector2(1000f, 5f));
            }

            Assert.That(MathF.Abs(swarm.X[0] - swarm.X[1]), Is.GreaterThan(1.5f * t.scout.radius));
        }

        [Test]
        public void RemovingADroneMovesTheLastOneIntoItsSlot()
        {
            var swarm = new Swarm(RideTuning.Default());
            swarm.Spawn(EnemyKind.Scout, 1f, 1f);
            swarm.Spawn(EnemyKind.Scout, 2f, 2f);
            swarm.Spawn(EnemyKind.Hauler, 3f, 3f);

            swarm.RemoveAt(0);

            Assert.That(swarm.Count, Is.EqualTo(2));
            Assert.That(swarm.X[0], Is.EqualTo(3f));
            Assert.That(swarm.Kind[0], Is.EqualTo(EnemyKind.Hauler));
            Assert.That(swarm.Hp[0], Is.EqualTo(RideTuning.Default().hauler.hp));
        }

        [Test]
        public void SpawnRefusesWhenFull()
        {
            var t = RideTuning.Default();
            t.maxEnemies = 2;
            var swarm = new Swarm(t);

            Assert.That(swarm.Spawn(EnemyKind.Scout, 0f, 0f), Is.EqualTo(0));
            Assert.That(swarm.Spawn(EnemyKind.Scout, 0f, 0f), Is.EqualTo(1));
            Assert.That(swarm.Spawn(EnemyKind.Scout, 0f, 0f), Is.EqualTo(-1));
        }
    }
}
