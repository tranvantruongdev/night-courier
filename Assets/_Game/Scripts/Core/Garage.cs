using System;

namespace NightCourier.Core
{
    /// <summary>Permanent upgrades bought between shifts with coins earned on them.</summary>
    public enum GarageUpgrade
    {
        MaxHp,
        XpGain,
        StartSpeed,
        Reroll,
    }

    /// <summary>What the garage keeps between rides (saved through SaveData.SetGame).</summary>
    [Serializable]
    public sealed class GarageData
    {
        public int coins;
        public int[] levels = new int[Garage.UpgradeCount];

        public int Level(GarageUpgrade upgrade) => levels != null && (int)upgrade < levels.Length ? levels[(int)upgrade] : 0;
    }

    /// <summary>Prices, coin rewards, and how bought upgrades change a ride.</summary>
    public static class Garage
    {
        public static readonly int UpgradeCount = Enum.GetValues(typeof(GarageUpgrade)).Length;

        public static int MaxLevel(GarageUpgrade upgrade) => upgrade == GarageUpgrade.Reroll ? 1 : 5;

        /// <summary>40, 100, 160, 220, 280 coins per level; the reroll is a one-off 250.</summary>
        public static int Cost(GarageUpgrade upgrade, int currentLevel) =>
            upgrade == GarageUpgrade.Reroll ? 250 : 40 + 60 * currentLevel;

        public static bool CanBuy(GarageData data, GarageUpgrade upgrade) =>
            data.Level(upgrade) < MaxLevel(upgrade) && data.coins >= Cost(upgrade, data.Level(upgrade));

        public static bool TryBuy(GarageData data, GarageUpgrade upgrade)
        {
            if (!CanBuy(data, upgrade))
            {
                return false;
            }

            if (data.levels == null || data.levels.Length < UpgradeCount)
            {
                Array.Resize(ref data.levels, UpgradeCount); // saves from before an upgrade existed
            }

            data.coins -= Cost(upgrade, data.Level(upgrade));
            data.levels[(int)upgrade]++;
            return true;
        }

        /// <summary>A coin per 5 kills and per 10 seconds ridden, plus 100 for beating the boss.</summary>
        public static int CoinsFor(Ride ride) => ride.Kills / 5 + (int)(ride.Time / 10f) + (ride.Won ? 100 : 0);

        /// <summary>Per level: +10 max HP, +10% XP, +0.2 u/s cruise speed; the reroll gives one card reroll a ride.</summary>
        public static void Apply(GarageData data, RideTuning tuning)
        {
            tuning.maxHp += 10f * data.Level(GarageUpgrade.MaxHp);
            tuning.xpGain *= 1f + 0.1f * data.Level(GarageUpgrade.XpGain);
            tuning.cruiseSpeed += 0.2f * data.Level(GarageUpgrade.StartSpeed);
            tuning.rerolls += data.Level(GarageUpgrade.Reroll);
        }
    }
}
