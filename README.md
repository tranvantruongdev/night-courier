# Night Courier

*A Survivor.io-style night ride where you can't stop pedalling.* At midnight the delivery-drone network of
Neon Ward glitched, and now the Swarm chases anything that stops moving. Kai, a night-shift courier on a
fixed-gear bike, has to ride until dawn.

> **Status (Oct 2026): week 1 of 5, movement and enemies.** The bike, the swarm, contact damage and the HUD
> work in Unity 6.3 LTS; weapons, level-ups and the wave director come next. 65 EditMode tests pass, and a
> PlayMode smoke test rides the real game on autopilot, crashes, saves the result and runs a 300-drone stress
> ride. The screenshots below come from that test.

<p>
  <img src="docs/screenshots/riding.png" width="270" alt="The cyan bike rides through a dark neon city block at full speed, with a gold trail; red diamond drones and a purple square hauler close in">
  <img src="docs/screenshots/stress-300.png" width="270" alt="Stress ride: 300 drones circle the bike">
  <img src="docs/screenshots/results.png" width="270" alt="Shift over: survived 0:22, new best, Ride again and Home buttons">
</p>

| | |
|---|---|
| **Inspired by** | Survivor.io and Vampire Survivors (auto-attack, XP, pick 1 of 3 level-ups, weapon evolutions) |
| **Twist** | Momentum: the bike never stops. Speed raises damage and gives dodges; tight turns and braking cost speed |
| **My role** | Solo: design, code, tuning. Art and sound are generated in code for now |
| **Engine** | Unity 6.3 LTS (URP 2D), C#. Built from [unity-mobile-template](https://github.com/tranvantruongdev/unity-mobile-template) |

## How it plays (so far)

- **Drag anywhere** to steer: the joystick appears under your thumb. A light push cruises at 4 u/s, a full
  push reaches 6 u/s. Pull back to brake while the bike swings round; speed never drops below 1.5 u/s.
- Turning is rate-limited (160°/s at cruise, slower at speed), and a full-rate turn lowers your speed.
- **Speed bonus:** damage × (1 + speed/max × 0.5). Above 70% of max speed the gauge turns gold and half of
  the hits miss you.
- Scouts are fast and weak, haulers slow and heavy. A hit costs HP and gives 0.5 s of invulnerability.

## How it's built

```
Assets/_Game/Scripts/Core/      rules in pure C# (no UnityEngine): BikeMotor, Swarm, SpatialGrid, Ride, RideBot
Assets/_Game/Scripts/Runtime/   Unity side: RideController, bike and swarm views, joystick, HUD, neon art
Assets/_Game/Tests/EditMode/    the same tests run in Unity and with dotnet
Assets/_Game/Tests/PlayMode/    smoke test: boot → title → autopilot ride → crash → results → 300-drone ride
Assets/_Project/                shared template: boot flow, saves, audio, haptics, UI, game feel
```

- **No physics engine for enemies.** Drones live in parallel arrays (positions, HP, kind) and move in one
  loop. Separation goes through a **spatial grid**: 2-unit cells hashed into a fixed table and
  counting-sorted, so a rebuild is O(n) with no allocation. A test checks every query against a brute-force
  scan with forced hash collisions.
- **0 B of garbage per step**, checked by a dotnet test at 100, 300 and 500 drones. Desktop baseline: 55, 280
  and 517 µs per simulation step. Phone numbers come from the Unity Profiler (dev builds have Stress
  100/300/500 buttons).
- **Drawing:** one pooled SpriteRenderer per alive drone, positioned in a single loop; no `Update()` per drone.
- **Deterministic:** same seed and same inputs give the same ride. An autopilot (steer toward the emptiest
  of 16 directions) outlives a straight ride on every tested seed, which keeps the swarm beatable.

```bash
dotnet test Tools/GameTests/NightCourier.Core.Tests.csproj   # ride rules and swarm budget, ~10 s
dotnet test Tools/CoreTests/Template.Core.Tests.csproj       # template core
```

In Unity (headless, Windows):

```bash
powershell -ExecutionPolicy Bypass -File Tools/run-unity-tests.ps1                                    # 65 EditMode tests
powershell -ExecutionPolicy Bypass -File Tools/run-unity-tests.ps1 -TestPlatform PlayMode -Graphics   # smoke test + screenshots in Logs/screenshots
```

Release setup (signing keystore, Unity login and itch.io secrets for CI) is the template's:
`Tools/setup-release-secrets.ps1`, then tag `v*`.

## Credits

Built with AI assistance (Claude Code). I designed the systems, reviewed and tested all code.
Libraries: UniTask (MIT), PrimeTween, Newtonsoft JSON (MIT). Template code: MIT (see `LICENSE`).
