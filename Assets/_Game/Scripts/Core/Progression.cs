using System;
using Template.Core.Random;

namespace NightCourier.Core
{
    /// <summary>Bike parts (weapons) first, then passives. Each goes from level 1 to <see cref="Loadout.MaxLevel"/>.</summary>
    public enum ItemKind : byte
    {
        Headlight,
        SpokeCards,
        Bell,
        ChainWhip,
        TyreSpikes,
        BigBasket,
        GearRatio,
        Helmet,
        LighterFrame,
        BetterBrakes,
        EnergyGel,
    }

    /// <summary>What the bike carries: a level per item, 0 = not owned.</summary>
    public sealed class Loadout
    {
        public const int MaxLevel = 5;
        public static readonly int ItemCount = Enum.GetValues(typeof(ItemKind)).Length;

        private readonly int[] _levels = new int[ItemCount];

        public int Level(ItemKind item) => _levels[(int)item];
        public bool Owns(ItemKind item) => _levels[(int)item] > 0;
        public bool CanUpgrade(ItemKind item) => _levels[(int)item] < MaxLevel;

        public void Upgrade(ItemKind item)
        {
            if (!CanUpgrade(item))
            {
                throw new InvalidOperationException($"{item} is already at level {MaxLevel}.");
            }

            _levels[(int)item]++;
        }
    }

    /// <summary>XP needed to go from <paramref name="level"/> to the next: 5 + 7 × level^1.3.</summary>
    public static class XpCurve
    {
        public static int Needed(int level) => (int)MathF.Round(5f + 7f * MathF.Pow(level, 1.3f));
    }

    /// <summary>
    /// Picks up to three different items for a level-up card. Owned items weigh <see cref="OwnedWeight"/> times
    /// more than new ones, so builds come together; maxed items are never offered.
    /// </summary>
    public static class LevelUpRoller
    {
        public const int Cards = 3;
        public const double OwnedWeight = 3.0;

        /// <summary>Fills <paramref name="offer"/> (length ≥ 3) and returns how many cards there are (0 when all is maxed).</summary>
        public static int Roll(Loadout loadout, SeededRandom rng, ItemKind[] offer, double[] scratch)
        {
            int count = 0;
            for (int i = 0; i < Loadout.ItemCount; i++)
            {
                var item = (ItemKind)i;
                scratch[i] = !loadout.CanUpgrade(item) ? 0.0 : loadout.Owns(item) ? OwnedWeight : 1.0;
            }

            while (count < Cards)
            {
                double total = 0.0;
                for (int i = 0; i < Loadout.ItemCount; i++)
                {
                    total += scratch[i];
                }

                if (total <= 0.0)
                {
                    break;
                }

                double pick = rng.NextDouble() * total;
                int chosen = 0;
                while (chosen < Loadout.ItemCount - 1 && (pick -= scratch[chosen]) >= 0.0)
                {
                    chosen++;
                }

                if (scratch[chosen] <= 0.0)
                {
                    continue; // landed on a zero-weight tail by rounding; pick again
                }

                offer[count++] = (ItemKind)chosen;
                scratch[chosen] = 0.0; // three different cards
            }

            return count;
        }
    }
}
