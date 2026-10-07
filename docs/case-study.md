# Night Courier: case study

A Survivor.io-style night ride, built solo in Unity 6.3 from my own
[mobile template](https://github.com/tranvantruongdev/unity-mobile-template). The twist: the bike never stops, and
speed is your damage, your dodge and, behind an elite drone, your escape. This page covers the three parts I care
most about, then the smaller things and the numbers.

## 1. Momentum as the core twist

`BikeMotor` (pure C#) keeps speed between 1.5 and 6 u/s. A light touch cruises, a full push accelerates, pulling
the stick back brakes while the bike swings round. Turning is rate-limited (160°/s at cruise, slower at speed), and
a full-rate turn lowers the target speed, so a tight line costs momentum you have to plan for.

Speed then feeds everything:

- **Damage** × (1 + speed / top speed × 0.5), applied to every weapon hit.
- **Dodge**: above 70% of top speed, half the contact hits miss. The gauge turns gold so you can see it.
- **Drafting**: an elite crosses your path every minute. Ride within 1.2 u behind it for 1.5 s and you get +30%
  speed for 2 s. Map 2's boss, the Freight Hauler, charges in straight lines; its slipstream during a charge drafts
  the same way, so the safest place in that fight is right behind the thing trying to hit you.
- **Better Brakes** keeps the bonus while braking, the one passive that lets you break the rule.

A test caught a real bug here: the Freight Hauler was re-aimed at the bike mid-charge, which turned it round and
removed the slipstream the whole fight is built on. A charge now holds its line to the end.

## 2. 300 drones without a physics engine

Drones are not GameObjects. `Swarm` keeps them in parallel arrays (position, HP, kind, heading, timer) and moves
them in one loop: seek the bike, then push overlapping drones apart. The neighbour search goes through
`SpatialGrid`: 2-unit cells hashed into a fixed table and counting-sorted, so a rebuild is O(n) with no allocation
and a query looks at a few cells instead of every drone.

- **Correctness**: a test compares every grid query with a brute-force scan, using a table of 64 buckets to force
  hash collisions. Collisions may add candidates; they must never lose one.
- **Garbage**: a dotnet test steps rides of 100, 300 and 500 drones, with all weapons firing and level-ups taken,
  and asserts 0 bytes allocated per step (`GC.GetAllocatedBytesForCurrentThread`). Shots, spikes, orbs, parcels and
  kill flashes all live in fixed ring buffers.
- **Drawing**: one pooled SpriteRenderer per alive drone, placed in a single loop; no `Update()` per drone.
- **A trap avoided**: queries pad their radius by the largest drone. Adding the 1.6-unit boss to that pad would have
  doubled every drone's query radius all game long. The boss is left out of the pad; weapons widen theirs only
  while a boss is alive.

Desktop baseline (dotnet, one 1/120 s step, across runs): 0.05–0.15 ms at 100 drones, 0.2–0.7 ms at 300 and
0.4–1.6 ms at 500. The spread between runs is large, so these are a floor check, not the target. The target is the
phone (see the numbers below).

## 3. Tuning with a bot, and what it found

`RideBot` steers toward the emptiest of 16 directions and picks up parcels on the way. It never aims, so it is a
pessimistic player. Tests ride it through hundreds of seeded shifts:

- **The starting weapon was too weak.** With the planned 14 dps over 3 units, the bot made 8 kills in 80 s and never
  levelled up: an approaching scout crossed the beam before it burned down. 24 dps over 3.5 units fixed it.
- **Pacing is a median, not a seed.** The plan wants the first level-up around 20 s. One seed's spawn layout moves
  that by tens of seconds, so the guard is the median over seven seeds (now 24.5 s) and the median level at 2:00.
- **Same seed, different ride.** A seed reached level 5 in `dotnet test` and level 3 in Unity's EditMode runner:
  float maths differs between .NET and Mono, and two minutes of simulation amplify it. Rides are deterministic
  within one runtime; tests shared by both runners compare medians.

## Smaller things

- **No art files.** Every sprite (bike, drones, both maps' city tiles, beams, rings) is drawn at startup from signed
  distance functions or pixel rules, so it ships with zero textures.
- **English, Vietnamese and Japanese.** Every label goes through one hook; a dotnet test scans the code for
  player-facing text and fails on any missing translation. Japanese glyphs come from a dynamic M PLUS Rounded 1c
  fallback added at startup, without touching TextMeshPro's assets on disk.
- **Recorded by tests.** The PlayMode smoke test plays the real game on autopilot (garage, routes, ride, level-up,
  crash, results, a 300-drone stress ride and both bosses) and saves screenshots in three aspect ratios. The
  trailer is another PlayMode test with `Time.captureFramerate`, turned into MP4 and GIF by ffmpeg.

## Numbers

| | |
|---|---|
| Tests | 78 core (dotnet) · 111 EditMode (Unity 6000.3.25f1) · PlayMode smoke test |
| Garbage per simulation step | 0 B at 100 / 300 / 500 drones (dotnet test) |
| Frame time on a phone | *to measure: dev build → Stress 100 / 300 / 500 → Unity Profiler* |
| Android build | signed IL2CPP ARM64 APK; the CI artifact (zipped) is ~38 MB |
| Content | 2 maps, 2 bosses, 6 weapons, 6 passives, 4 evolutions, 3 bikes, 3 languages |
