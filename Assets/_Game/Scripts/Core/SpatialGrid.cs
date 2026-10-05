using System;

namespace NightCourier.Core
{
    /// <summary>
    /// Uniform grid over an unbounded world: cells are hashed into a fixed bucket table and the points are
    /// counting-sorted by bucket, so a rebuild is O(n + buckets) with no allocation. A query returns every
    /// point in the cells the circle touches; hash collisions add extra candidates, never miss one, so the
    /// caller does the exact distance check.
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly float _inverseCell;
        private readonly int _mask;
        private readonly int[] _bucketStart;
        private readonly int[] _cursor;
        private readonly int[] _items;
        private readonly int[] _bucketOf;
        private readonly int[] _visited;
        private int _queryStamp;

        /// <param name="buckets">Rounded up to a power of two.</param>
        public SpatialGrid(float cellSize, int capacity, int buckets = 1024)
        {
            int size = 1;
            while (size < buckets)
            {
                size <<= 1;
            }

            CellSize = cellSize;
            _inverseCell = 1f / cellSize;
            _mask = size - 1;
            _bucketStart = new int[size + 1];
            _cursor = new int[size];
            _visited = new int[size];
            _items = new int[capacity];
            _bucketOf = new int[capacity];
        }

        public float CellSize { get; }
        public int Count { get; private set; }

        public void Build(float[] xs, float[] ys, int count)
        {
            Array.Clear(_bucketStart, 0, _bucketStart.Length);
            for (int i = 0; i < count; i++)
            {
                int bucket = Bucket(Cell(xs[i]), Cell(ys[i]));
                _bucketOf[i] = bucket;
                _bucketStart[bucket + 1]++;
            }

            for (int b = 0; b <= _mask; b++)
            {
                _bucketStart[b + 1] += _bucketStart[b];
                _cursor[b] = _bucketStart[b];
            }

            for (int i = 0; i < count; i++)
            {
                _items[_cursor[_bucketOf[i]]++] = i;
            }

            Count = count;
        }

        /// <summary>Writes candidate indices near (x, y) into <paramref name="results"/>; returns how many (capped at its length).</summary>
        public int Query(float x, float y, float radius, int[] results)
        {
            if (++_queryStamp == int.MaxValue)
            {
                Array.Clear(_visited, 0, _visited.Length);
                _queryStamp = 1;
            }

            int found = 0;
            int x0 = Cell(x - radius), x1 = Cell(x + radius);
            int y0 = Cell(y - radius), y1 = Cell(y + radius);
            for (int cy = y0; cy <= y1; cy++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    int bucket = Bucket(cx, cy);
                    if (_visited[bucket] == _queryStamp)
                    {
                        continue; // another cell in range hashed here; its points are already in
                    }

                    _visited[bucket] = _queryStamp;
                    for (int k = _bucketStart[bucket]; k < _bucketStart[bucket + 1]; k++)
                    {
                        if (found == results.Length)
                        {
                            return found;
                        }

                        results[found++] = _items[k];
                    }
                }
            }

            return found;
        }

        private int Cell(float v) => (int)MathF.Floor(v * _inverseCell);

        private int Bucket(int cx, int cy) => (int)(((uint)cx * 73856093u) ^ ((uint)cy * 19349663u)) & _mask;
    }
}
