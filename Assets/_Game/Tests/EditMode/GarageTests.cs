using System.Collections.Generic;
using System.Numerics;
using NightCourier.Core;
using NUnit.Framework;

namespace NightCourier.Core.Tests
{
    public class GarageTests
    {
        private const float Dt = 1f / 120f;

        [Test]
        public void UpgradesCostMoreEachLevelAndStopAtTheirMax()
        {
            var data = new GarageData { coins = 10000 };
            int spent = 0;
            while (Garage.CanBuy(data, GarageUpgrade.MaxHp))
            {
                spent += Garage.Cost(GarageUpgrade.MaxHp, data.Level(GarageUpgrade.MaxHp));
                Assert.That(Garage.TryBuy(data, GarageUpgrade.MaxHp), Is.True);
            }

            Assert.That(data.Level(GarageUpgrade.MaxHp), Is.EqualTo(5));
            Assert.That(spent, Is.EqualTo(40 + 100 + 160 + 220 + 280));
            Assert.That(data.coins, Is.EqualTo(10000 - spent));
            Assert.That(Garage.TryBuy(data, GarageUpgrade.MaxHp), Is.False, "maxed");
        }

        [Test]
        public void YouCantBuyWhatYouCantAfford()
        {
            var data = new GarageData { coins = 39 };
            Assert.That(Garage.TryBuy(data, GarageUpgrade.XpGain), Is.False);
            Assert.That(data.coins, Is.EqualTo(39));
        }

        [Test]
        public void OldSavesWithoutLevelsStillWork()
        {
            var data = new GarageData { coins = 100, levels = null };
            Assert.That(data.Level(GarageUpgrade.StartSpeed), Is.EqualTo(0));
            Assert.That(Garage.TryBuy(data, GarageUpgrade.StartSpeed), Is.True);
            Assert.That(data.Level(GarageUpgrade.StartSpeed), Is.EqualTo(1));
        }

        [Test]
        public void CoinsComeFromKillsTimeAndTheWin()
        {
            var t = RideTests.Quiet();
            var ride = new Ride(t, 1);
            for (int i = 0; i < 12; i++)
            {
                ride.Swarm.Spawn(EnemyKind.Scout, 30f + i, 0f);
                ride.Swarm.Hp[i] = 0f;
            }

            RideTests.Run(ride, 25f, r => Vector2.Zero);
            Assert.That(Garage.CoinsFor(ride), Is.EqualTo(12 / 5 + 2));

            ride.Swarm.Spawn(EnemyKind.Boss, 60f, 0f);
            ride.Swarm.Hp[0] = 0f;
            RideTests.Run(ride, Dt, r => Vector2.Zero);
            Assert.That(ride.Won, Is.True);
            Assert.That(Garage.CoinsFor(ride), Is.EqualTo(13 / 5 + 2 + 100));
        }

        [Test]
        public void BoughtUpgradesChangeTheRide()
        {
            var data = new GarageData { coins = 10000 };
            Garage.TryBuy(data, GarageUpgrade.MaxHp);
            Garage.TryBuy(data, GarageUpgrade.MaxHp);
            Garage.TryBuy(data, GarageUpgrade.XpGain);
            Garage.TryBuy(data, GarageUpgrade.StartSpeed);
            Garage.TryBuy(data, GarageUpgrade.Reroll);

            var t = RideTests.Quiet();
            Garage.Apply(data, t);
            var ride = new Ride(t, 1);

            Assert.That(ride.Hp, Is.EqualTo(120f));
            Assert.That(ride.Bike.Speed, Is.EqualTo(4.2f).Within(1e-4f), "starts at the faster cruise");
            Assert.That(ride.RerollsLeft, Is.EqualTo(1));

            // +10% XP: ten 1-XP parcels give 11.
            for (int i = 0; i < 10; i++)
            {
                ride.Parcels.Add(ride.Bike.Position.X, ride.Bike.Position.Y + 0.1f, 1);
            }

            RideTests.Run(ride, Dt, r => Vector2.Zero);
            Assert.That(ride.Xp, Is.EqualTo(11));
        }

        [Test]
        public void BikesAreBoughtOnceThenRiddenFreely()
        {
            var data = new GarageData { coins = 350 };
            Assert.That(data.Owns(BikeModel.Fixie), Is.True);
            Assert.That(Garage.SelectBike(data, BikeModel.Racer), Is.True);
            Assert.That(data.coins, Is.EqualTo(50));
            Assert.That(Garage.SelectBike(data, BikeModel.Cargo), Is.False, "can't afford the second");
            Assert.That(data.bike, Is.EqualTo(BikeModel.Racer));

            Assert.That(Garage.SelectBike(data, BikeModel.Fixie), Is.True);
            Assert.That(Garage.SelectBike(data, BikeModel.Racer), Is.True, "owned: free");
            Assert.That(data.coins, Is.EqualTo(50));
        }

        [Test]
        public void EachBikeRidesDifferently()
        {
            RideTuning With(BikeModel model)
            {
                var t = RideTuning.Default();
                Garage.Apply(new GarageData { bike = model, ownedBikes = 7 }, t);
                return t;
            }

            var fixie = With(BikeModel.Fixie);
            var racer = With(BikeModel.Racer);
            var cargo = With(BikeModel.Cargo);
            Assert.That(racer.maxSpeed, Is.GreaterThan(fixie.maxSpeed));
            Assert.That(racer.maxHp, Is.LessThan(fixie.maxHp));
            Assert.That(cargo.maxHp, Is.GreaterThan(fixie.maxHp));
            Assert.That(cargo.turnRateAtCruise, Is.LessThan(fixie.turnRateAtCruise));
            Assert.That(racer.cruiseSpeed, Is.LessThan(racer.maxSpeed), "cruise stays under top speed");
        }

        [Test]
        public void TheCodexRemembersEverythingEverCarried()
        {
            var data = new GarageData();
            var first = new Loadout();
            first.Upgrade(ItemKind.Headlight);
            first.Upgrade(ItemKind.Bell);
            Garage.RecordCodex(data, first);

            var second = new Loadout();
            for (int i = 0; i < Loadout.MaxLevel; i++)
            {
                second.Upgrade(ItemKind.Headlight);
            }

            second.Upgrade(ItemKind.GearRatio);
            second.Evolve(ItemKind.Headlight);
            Garage.RecordCodex(data, second);

            Assert.That(data.Seen(ItemKind.Bell), Is.True, "kept from the first ride");
            Assert.That(data.Seen(ItemKind.GearRatio), Is.True);
            Assert.That(data.Seen(ItemKind.Helmet), Is.False);
            Assert.That(data.SeenEvolution(ItemKind.Headlight), Is.True);
            Assert.That(data.SeenEvolution(ItemKind.Bell), Is.False);
        }

        [Test]
        public void TheFirstShiftIsLighter()
        {
            var first = RideTuning.Default();
            Garage.Apply(new GarageData(), first);
            var later = RideTuning.Default();
            Garage.Apply(new GarageData { rodeOnce = true }, later);

            Assert.That(first.waves[0].drones, Is.EqualTo((int)(later.waves[0].drones * 0.8f)));
            Assert.That(first.waves[first.waves.Length - 1].drones, Is.LessThan(later.waves[later.waves.Length - 1].drones));
            Assert.That(RideTuning.Default().waves[0].drones, Is.EqualTo(later.waves[0].drones), "the defaults aren't touched");
        }

        [Test]
        public void ARerollSwapsTheCardsOnce()
        {
            var t = RideTests.Quiet();
            t.rerolls = 1;
            var ride = new Ride(t, 3);
            ride.Parcels.Add(0f, 0f, XpCurve.Needed(1));
            ride.Step(Dt, Vector2.Zero, new List<RideEvent>());
            Assert.That(ride.PendingLevelUps, Is.EqualTo(1));

            var before = (ItemKind[])ride.Offer.Clone();
            bool changed = false;
            Assert.That(ride.Reroll(), Is.True);
            for (int i = 0; i < ride.OfferCount; i++)
            {
                changed |= ride.Offer[i] != before[i];
            }

            Assert.That(changed, Is.True, "a fresh roll");
            Assert.That(ride.Reroll(), Is.False, "only one a ride");
            Assert.That(ride.PendingLevelUps, Is.EqualTo(1), "rerolling doesn't spend the level-up");
        }
    }
}
