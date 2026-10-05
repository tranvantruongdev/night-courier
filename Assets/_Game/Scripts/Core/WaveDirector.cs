using System;

namespace NightCourier.Core
{
    /// <summary>One point on the density curve: from <see cref="seconds"/> into the ride, keep this many drones alive.</summary>
    [Serializable]
    public struct WaveKey
    {
        public float seconds;
        public int drones;
        public float haulerShare;

        public WaveKey(float seconds, int drones, float haulerShare)
        {
            this.seconds = seconds;
            this.drones = drones;
            this.haulerShare = haulerShare;
        }
    }

    /// <summary>
    /// Time → how many drones to keep alive and how many of the new ones are haulers, by linear interpolation
    /// between keys (flat before the first and after the last). Drone HP grows with the minutes ridden.
    /// </summary>
    public static class WaveDirector
    {
        public static int Drones(WaveKey[] keys, float seconds) =>
            keys.Length == 0 ? 0 : (int)MathF.Round(Lerp(keys, seconds, k => k.drones));

        public static float HaulerShare(WaveKey[] keys, float seconds) =>
            keys.Length == 0 ? 0f : Lerp(keys, seconds, k => k.haulerShare);

        /// <summary>HP × (1 + minutes × perMinute).</summary>
        public static float HpScale(float seconds, float perMinute) => 1f + seconds / 60f * perMinute;

        /// <summary>The same count all ride long (tests, stress rides).</summary>
        public static WaveKey[] Flat(int drones, float haulerShare = 0.25f) => new[] { new WaveKey(0f, drones, haulerShare) };

        private static float Lerp(WaveKey[] keys, float t, Func<WaveKey, float> value)
        {
            if (t <= keys[0].seconds)
            {
                return value(keys[0]);
            }

            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].seconds)
                {
                    var a = keys[i - 1];
                    var b = keys[i];
                    float f = (t - a.seconds) / (b.seconds - a.seconds);
                    return value(a) + (value(b) - value(a)) * f;
                }
            }

            return value(keys[keys.Length - 1]);
        }
    }
}
