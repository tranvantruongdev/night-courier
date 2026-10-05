namespace NightCourier.Core
{
    /// <summary>XP parcels dropped by drones, as parallel arrays. Removal swaps in the last one.</summary>
    public sealed class Parcels
    {
        public Parcels(int capacity)
        {
            X = new float[capacity];
            Y = new float[capacity];
            Value = new int[capacity];
        }

        public float[] X { get; }
        public float[] Y { get; }
        public int[] Value { get; }
        public int Count { get; private set; }
        public int Capacity => X.Length;

        /// <summary>False when full.</summary>
        public bool Add(float x, float y, int value)
        {
            if (Count == Capacity)
            {
                return false;
            }

            X[Count] = x;
            Y[Count] = y;
            Value[Count] = value;
            Count++;
            return true;
        }

        public void RemoveAt(int i)
        {
            int last = --Count;
            X[i] = X[last];
            Y[i] = Y[last];
            Value[i] = Value[last];
        }
    }
}
