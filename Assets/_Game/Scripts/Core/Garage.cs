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

    /// <summary>Kai's bikes: the fixie everyone starts with, a fast fragile racer, a tanky cargo bike.</summary>
    public enum BikeModel
    {
        Fixie,
        Racer,
        Cargo,
    }

    /// <summary>What the garage keeps between rides (saved through SaveData.SetGame).</summary>
    [Serializable]
    public sealed class GarageData
    {
        public int coins;
        public int[] levels = new int[Garage.UpgradeCount];
        public BikeModel bike;

        /// <summary>Bit per <see cref="BikeModel"/>; the fixie is always owned.</summary>
        public int ownedBikes = 1;

        /// <summary>The first shift has been ridden: no more first-run hints or the lighter first swarm.</summary>
        public bool rodeOnce;

        public int Level(GarageUpgrade upgrade) => levels != null && (int)upgrade < levels.Length ? levels[(int)upgrade] : 0;
        public bool Owns(BikeModel model) => model == BikeModel.Fixie || (ownedBikes & (1 << (int)model)) != 0;
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

        public const int BikeCost = 300;

        /// <summary>Rides an owned bike, or buys and rides one you can afford. False when neither.</summary>
        public static bool SelectBike(GarageData data, BikeModel model)
        {
            if (!data.Owns(model))
            {
                if (data.coins < BikeCost)
                {
                    return false;
                }

                data.coins -= BikeCost;
                data.ownedBikes |= 1 << (int)model;
            }

            data.bike = model;
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

            if (!data.rodeOnce)
            {
                // The first shift is lighter: 20% fewer drones all the way, so a new rider feels strong by minute 3.
                var waves = (WaveKey[])tuning.waves.Clone();
                for (int i = 0; i < waves.Length; i++)
                {
                    waves[i].drones = (int)(waves[i].drones * 0.8f);
                }

                tuning.waves = waves;
            }

            switch (data.bike)
            {
                case BikeModel.Racer: // faster, fragile
                    tuning.maxSpeed *= 1.15f;
                    tuning.cruiseSpeed *= 1.1f;
                    tuning.maxHp *= 0.7f;
                    break;
                case BikeModel.Cargo: // tanky, slower turns
                    tuning.maxHp *= 1.4f;
                    tuning.turnRateAtCruise *= 0.75f;
                    tuning.maxSpeed *= 0.92f;
                    break;
            }
        }
    }
}
