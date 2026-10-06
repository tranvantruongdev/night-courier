using System;
using System.Numerics;

namespace NightCourier.Core
{
    public enum EnemyKind : byte
    {
        Scout,
        Hauler,

        /// <summary>Cruises straight across the city; ride close behind it to draft.</summary>
        Elite,

        /// <summary>Splits into two scouts when it goes down.</summary>
        Splitter,

        /// <summary>Hangs back at range and fires slow orbs.</summary>
        Zapper,

        /// <summary>The Dispatcher, map 1's boss at 10:00: summons drones and fires fans of orbs.</summary>
        Boss,
    }

    /// <summary>
    /// All drones as parallel arrays (struct of arrays): no GameObject, Rigidbody or Update per drone. One loop
    /// seeks the target and pushes overlapping drones apart through a <see cref="SpatialGrid"/>, which turns the
    /// O(n²) neighbour check into a few cells per drone. Slots 0..Count-1 are alive; removal swaps in the last one.
    /// </summary>
    public sealed class Swarm
    {
        private readonly RideTuning _t;
        private readonly float[] _pushX;
        private readonly float[] _pushY;
        // ponytail: candidates past 256 per drone are skipped (only in extreme piles: less push, no error); grow it if drones visibly stack.
        private readonly int[] _neighbours = new int[256];
        private int _bosses;

        public Swarm(RideTuning tuning)
        {
            _t = tuning;
            int n = tuning.maxEnemies;
            X = new float[n];
            Y = new float[n];
            Hp = new float[n];
            Kind = new EnemyKind[n];
            Heading = new float[n];
            Timer = new float[n];
            _pushX = new float[n];
            _pushY = new float[n];
            Grid = new SpatialGrid(tuning.gridCellSize, n);
        }

        public float[] X { get; }
        public float[] Y { get; }
        public float[] Hp { get; }
        public EnemyKind[] Kind { get; }

        /// <summary>Radians; only elites use it (they cruise straight instead of chasing).</summary>
        public float[] Heading { get; }

        /// <summary>Seconds; zappers count down to their next shot.</summary>
        public float[] Timer { get; }
        public int Count { get; private set; }

        /// <summary>A boss is among the live drones.</summary>
        public bool HasBoss => _bosses > 0;
        public int Capacity => X.Length;

        /// <summary>Built from the positions at the start of the last <see cref="Step"/>.</summary>
        public SpatialGrid Grid { get; }

        /// <summary>Returns the new slot, or -1 when the swarm is full.</summary>
        public int Spawn(EnemyKind kind, float x, float y, float hpScale = 1f, float heading = 0f)
        {
            if (Count == Capacity)
            {
                return -1;
            }

            int i = Count++;
            X[i] = x;
            Y[i] = y;
            Kind[i] = kind;
            if (kind == EnemyKind.Boss)
            {
                _bosses++;
            }
            Hp[i] = _t.Stats(kind).hp * hpScale;
            Heading[i] = heading;
            Timer[i] = 0f;
            return i;
        }

        public void RemoveAt(int i)
        {
            if (Kind[i] == EnemyKind.Boss)
            {
                _bosses--;
            }

            int last = --Count;
            X[i] = X[last];
            Y[i] = Y[last];
            Hp[i] = Hp[last];
            Kind[i] = Kind[last];
            Heading[i] = Heading[last];
            Timer[i] = Timer[last];
        }

        public void Step(float dt, Vector2 target)
        {
            Grid.Build(X, Y, Count);
            float reach = 2f * _t.MaxEnemyRadius;

            for (int i = 0; i < Count; i++)
            {
                float ri = _t.Stats(Kind[i]).radius;
                float px = 0f, py = 0f;
                int found = Grid.Query(X[i], Y[i], reach, _neighbours);
                for (int k = 0; k < found; k++)
                {
                    int j = _neighbours[k];
                    if (j == i)
                    {
                        continue;
                    }

                    float dx = X[i] - X[j], dy = Y[i] - Y[j];
                    float minDistance = ri + _t.Stats(Kind[j]).radius;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= minDistance * minDistance)
                    {
                        continue;
                    }

                    float d = MathF.Sqrt(d2);
                    float overlap = (minDistance - d) * 0.5f;
                    if (d < 1e-4f)
                    {
                        // Same spot: split them by index so the result doesn't depend on float noise.
                        px += i < j ? overlap : -overlap;
                        continue;
                    }

                    px += dx / d * overlap;
                    py += dy / d * overlap;
                }

                _pushX[i] = px;
                _pushY[i] = py;
            }

            float push = MathF.Min(1f, _t.separation * dt);
            for (int i = 0; i < Count; i++)
            {
                float speed = _t.Stats(Kind[i]).speed;
                if (Kind[i] == EnemyKind.Boss)
                {
                    // The boss bears down on the bike and shoulders through the swarm.
                    float bx = target.X - X[i], by = target.Y - Y[i];
                    float bd = MathF.Sqrt(bx * bx + by * by);
                    if (bd > 1e-4f)
                    {
                        X[i] += bx / bd * speed * dt;
                        Y[i] += by / bd * speed * dt;
                    }

                    continue;
                }

                if (Kind[i] == EnemyKind.Elite)
                {
                    // Elites don't chase or get shoved: they hold their line, so you can tuck in behind them.
                    X[i] += MathF.Cos(Heading[i]) * speed * dt;
                    Y[i] += MathF.Sin(Heading[i]) * speed * dt;
                    continue;
                }

                float dx = target.X - X[i], dy = target.Y - Y[i];
                float d = MathF.Sqrt(dx * dx + dy * dy);
                bool holding = Kind[i] == EnemyKind.Zapper && d <= _t.zapperRange; // zappers keep their distance
                float step = d > 1e-4f && !holding ? MathF.Min(speed * dt, d) / d : 0f;
                X[i] += dx * step + _pushX[i] * push;
                Y[i] += dy * step + _pushY[i] * push;
            }
        }
    }
}
