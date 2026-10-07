# itch.io page — copy and paste

Create it at itch.io → **Upload new project**. Keep it a **draft** until `v1.0.0`: the first switch to
Public puts the page on itch.io's Most Recent list, and that happens only once.

Not filled in yet. Still open: the AI generation disclosure and going Public (the owner's call).

| Field | Value |
|---|---|
| Title | Night Courier |
| Project URL | `night-courier` (must match the `ITCH_GAME` variable) |
| Short description | Ride until dawn through a city of rogue drones. You can't stop pedalling, and speed is your weapon. |
| Classification | Games |
| Kind of project | Downloadable |
| Release status | In development (Released at v1.0.0) |
| Pricing | No payments |
| Uploads | Done by CI (butler): channels `android` and `windows`. Nothing to upload by hand |
| Genre | Action |
| Tags | `roguelite`, `bullet-heaven`, `survivors-like`, `auto-shooter`, `top-down`, `cycling`, `neon`, `unity` (no platform tags: itch.io asks to leave those to the platform ticks) |
| Platforms (tick) | Windows, Android (butler sets them from the channel names) |
| Cover image | `docs/itch-cover.png`: `docs/screenshots/riding.png` scaled to 630 wide and cropped to 630 × 500 |
| Screenshots | `docs/night-courier.gif`, `docs/screenshots/riding.png`, `level-up.png` |
| Gameplay video / GIF | `docs/night-courier-trailer.mp4` (19 s) or `docs/night-courier.gif` |

## Description

> **At midnight the delivery drones of Neon Ward glitched, and now the Swarm chases anything that stops moving.**
> Kai rides a fixed-gear bike. Kai has to ride until dawn.
>
> Steer, and everything else is automatic: your weapons fire on their own, and every level-up offers three
> cards. The twist is momentum. The bike never stops, speed raises your damage and lets you dodge, and tight
> turns or braking cost speed you have to win back.
>
> - 10-minute shifts on two maps, each ending in a boss. Beat Market Street to open Harbor Ring.
> - Six weapons, six bike parts and four evolutions. Tuck in behind an elite drone to draft for a speed burst.
> - Coins buy upgrades and two more bikes in the Garage; the Codex fills in as you go.
> - Up to 300 drones on screen. English, Vietnamese and Japanese.
> - Plays offline. No ads, no sign-in, and Unity's analytics services are switched off.
>
> **Controls:** drag anywhere to steer (phone) · WASD or arrow keys (Windows) · Back/Esc pauses.
> Joystick size, opacity and a left-handed layout are in Controls.
>
> Made in Unity by Tran Van Truong, a Unity game developer. Source code and a case study:
> https://github.com/tranvantruongdev/night-courier

## Install notes (paste under the downloads)

> **Android:** download the APK, open it, and allow installs from this source when asked.
> **Windows:** unzip and run `game.exe` (the release workflow builds with `buildName: game`). Windows SmartScreen
> may warn because the game isn't code-signed: choose *More info → Run anyway*.
