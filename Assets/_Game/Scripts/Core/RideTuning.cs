using System;

namespace NightCourier.Core
{
    [Serializable]
    public struct EnemyStats
    {
        public float speed;
        public float hp;
        public float contactDamage;
        public float radius;
    }

    /// <summary>
    /// Every gameplay number in one place. World units: 1 unit is about one bike length; the camera shows
    /// roughly 9 × 16 units on a portrait phone.
    /// </summary>
    [Serializable]
    public sealed class RideTuning
    {
        // Bike (never stops: speed stays between minSpeed and maxSpeed)
        public float maxSpeed = 6f;
        public float cruiseSpeed = 4f;
        public float minSpeed = 1.5f;
        public float acceleration = 3f;
        public float brakeDeceleration = 6f;

        /// <summary>Degrees per second at cruise speed. Faster turns slower and slower turns faster, within the scales below.</summary>
        public float turnRateAtCruise = 160f;
        public float minTurnScale = 0.6f;
        public float maxTurnScale = 1.5f;

        /// <summary>Turning at the full rate lowers the target speed by this much, so tight turns cost momentum.</summary>
        public float turnSpeedLoss = 1.5f;

        /// <summary>A stick pointing further than this from the heading (degrees) brakes while the bike swings round.</summary>
        public float brakeAngle = 135f;
        public float stickDeadZone = 0.15f;
        public float bikeRadius = 0.35f;

        // Speed bonus (the momentum twist)
        public float speedDamageBonus = 0.5f;
        public float dodgeSpeedFraction = 0.7f;
        public float dodgeChance = 0.5f;

        // Health
        public float maxHp = 100f;
        public float invulnerableSeconds = 0.5f;

        // Swarm
        public int maxEnemies = 600;
        public float gridCellSize = 2f;

        /// <summary>How hard overlapping drones push apart (fraction of the overlap removed per second).</summary>
        public float separation = 8f;

        public EnemyStats scout = new EnemyStats { speed = 2.8f, hp = 6f, contactDamage = 6f, radius = 0.3f };
        public EnemyStats hauler = new EnemyStats { speed = 1.3f, hp = 30f, contactDamage = 14f, radius = 0.55f };
        public EnemyStats elite = new EnemyStats { speed = 3.2f, hp = 120f, contactDamage = 20f, radius = 0.7f };
        public EnemyStats splitter = new EnemyStats { speed = 2.2f, hp = 18f, contactDamage = 8f, radius = 0.4f };
        public EnemyStats zapper = new EnemyStats { speed = 2f, hp = 14f, contactDamage = 6f, radius = 0.35f };

        // Splitters from 1:30, zappers from 2:00, each a share of new spawns. Zappers stop at zapperRange and fire an
        // orb every zapperEvery seconds.
        public float splitterFrom = 90f;
        public float splitterShare = 0.15f;
        public float zapperFrom = 120f;
        public float zapperShare = 0.08f;
        public float zapperRange = 5f;
        public float zapperEvery = 2.5f;
        public float orbSpeed = 4f;
        public float orbDamage = 8f;
        public float orbLife = 3f;
        public float orbRadius = 0.2f;

        // Shift events: a ring closes in at 3:00, a horde charges from one side at 6:00, an elite group at 8:30.
        public float ringAt = 180f;
        public int ringCount = 40;
        public float ringRadius = 9f;
        public float hordeAt = 360f;
        public int hordeCount = 60;
        public float eliteGroupAt = 510f;

        // The Dispatcher arrives at shiftSeconds; beating it ends the shift with a win. Every bossSummonEvery it calls
        // bossSummonCount scouts around itself; every bossFanEvery it fires bossFanOrbs orbs in a fan at the bike.
        public float shiftSeconds = 600f;
        public EnemyStats boss = new EnemyStats { speed = 1.6f, hp = 1500f, contactDamage = 30f, radius = 1.6f };
        public float bossSummonEvery = 4f;
        public int bossSummonCount = 8;
        public float bossFanEvery = 1.5f;
        public int bossFanOrbs = 5;
        public int eliteGroupCount = 3;

        // Elites and drafting: an elite every minute from 1:00; riding within draftRange behind one (and within
        // draftLateral of its line) for draftSeconds gives +draftBoost speed for draftBoostSeconds.
        public float firstEliteAt = 60f;
        public float eliteEvery = 60f;
        public int eliteXp = 25;
        public float draftRange = 1.2f;
        public float draftLateral = 0.5f;
        public float draftSeconds = 1.5f;
        public float draftBoost = 0.3f;
        public float draftBoostSeconds = 2f;

        // Spawning: a steady ramp for now; the wave director replaces it.
        public float spawnDistance = 13f;
        public float despawnDistance = 22f;
        /// <summary>Drones kept alive over the 10-minute shift; a busy start so the first level-up comes early.</summary>
        public WaveKey[] waves =
        {
            new WaveKey(0f, 35, 0.1f),
            new WaveKey(60f, 80, 0.2f),
            new WaveKey(180f, 160, 0.25f),
            new WaveKey(360f, 240, 0.3f),
            new WaveKey(600f, 300, 0.35f),
        };

        /// <summary>Drone HP × (1 + minutes × this).</summary>
        public float hpPerMinute = 0.15f;

        // Weapons: level-1 values, plus the per-level step for each level above 1.
        public float headlightRange = 3.5f;
        public float headlightRangePerLevel = 0.3f;
        public float headlightHalfAngle = 22f;
        public float headlightHalfAnglePerLevel = 5f;
        public float headlightDps = 24f;
        public float headlightDpsPerLevel = 6f;

        /// <summary>Blades = level + 1, orbiting the bike.</summary>
        public float spokeOrbit = 1.5f;
        public float spokeTurnRate = 240f;
        public float spokeRadius = 0.35f;
        public float spokeDps = 30f;
        public float spokeDpsPerLevel = 6f;

        public float bellCooldown = 3f;
        public float bellRadius = 2.2f;
        public float bellRadiusPerLevel = 0.3f;
        public float bellDamage = 18f;
        public float bellDamagePerLevel = 8f;
        public float bellKnockback = 1.2f;

        /// <summary>Cracks on the side the bike turns toward (alternating when riding straight), a 120° arc.</summary>
        public float whipCooldown = 1.2f;
        public float whipRange = 2.2f;
        public float whipRangePerLevel = 0.25f;
        public float whipDamage = 16f;
        public float whipDamagePerLevel = 7f;

        /// <summary>Drops a spike every so often; each lasts longer the faster the bike went.</summary>
        public float spikesEvery = 0.12f;
        public float spikesLifeAtCruise = 1.2f;
        public float spikesRadius = 0.4f;
        public float spikesRadiusPerLevel = 0.05f;
        public float spikesDps = 18f;
        public float spikesDpsPerLevel = 6f;

        /// <summary>Volleys of level-many homing shots at the nearest drone in range.</summary>
        public float pannierCooldown = 0.8f;
        public float pannierRange = 6f;
        public float pannierDamage = 10f;
        public float pannierDamagePerLevel = 4f;
        public float pannierShotSpeed = 9f;
        public float pannierShotLife = 1.2f;
        public float pannierTurnRate = 360f;

        // Passives, per level
        public float basketMagnetPerLevel = 0.25f;
        public float gearCooldownPerLevel = 0.08f;
        public float helmetArmourPerLevel = 0.08f;
        public float frameSpeedPerLevel = 0.06f;
        public float brakesTurnLossPerLevel = 0.15f;
        public float gelRegenPerLevel = 0.4f;

        // Parcels (XP drops)
        public int maxParcels = 400;
        public float magnetRadius = 1.5f;
        public float parcelPullSpeed = 9f;
        public float collectRadius = 0.45f;
        public int scoutXp = 2;
        public int haulerXp = 5;

        public EnemyStats Stats(EnemyKind kind) => kind switch
        {
            EnemyKind.Hauler => hauler,
            EnemyKind.Elite => elite,
            EnemyKind.Splitter => splitter,
            EnemyKind.Zapper => zapper,
            EnemyKind.Boss => boss,
            _ => scout,
        };

        /// <summary>Largest drone radius, the boss left out (queries pad for it only while one is alive: <see cref="Swarm.HasBoss"/>).</summary>
        public float MaxEnemyRadius => Math.Max(Math.Max(scout.radius, hauler.radius), Math.Max(elite.radius, splitter.radius));

        public static RideTuning Default() => new RideTuning();
    }
}
