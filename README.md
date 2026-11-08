# IgnisComeDeliverance

*An unofficial, free co-op mod for Kingdom Come: Deliverance II.*
**Not affiliated with or endorsed by Warhorse Studios or PLAION.**

> **IgnisComeDeliverance is a modified version of [Kingdom Come: Together](https://github.com/DeepFriedDepp/KingdomCome-Together).
> It is not the official version, and it is not made or supported by Kingdom Come: Together's maintainer.**
> Please report problems with this fork here, not to the upstream project.
>
> **Official repository of Kingdom Come: Together:** https://github.com/DeepFriedDepp/KingdomCome-Together

Two players share one world. The host plays in their own world, and the partner joins it with their own
character: the host's people, quests, clock and weather, with shared fights, deaths and loot.

## What this fork changes

Based on Kingdom Come: Together **0.64.1**. Every code change is marked `KCT fix` in the source. Full list, with
the cause of each bug: [docs/KCT-FIXES-sync-tpose.md](docs/KCT-FIXES-sync-tpose.md).

- **T-poses.** Six ways the joiner's copies of NPCs were left with no animation playing (in fights, after
  walking away and back, behind a full animation queue) now always get a base animation.
- **Desync.** Late or reordered network packets can no longer overwrite newer state: NPCs no longer snap back,
  corpses no longer "come alive", and a world transfer no longer holds every position back on direct (TCP) links.
  Sender timestamps no longer lose precision on PCs that have been up for days.
- **NPCs in underclothes** on the joiner's screen are dressed again after a load or a night.
- **Corpses standing up** on the joiner's screen now lie down.
- **Smoother movement:** walkers no longer freeze and flip between idle and walk; the partner no longer surges
  ahead and snaps back; ghosts are no longer lifted out of cellars.
- **Connection line in game:** the top-left line now grades the link (Good / Fair / Poor) and shows ping, jitter,
  packet loss and, when it matters, how far behind your game is.
- **Less rubber-banding:** a figure that runs out of updates waits and then catches up at 1.2x instead of jumping.
- **Quiet console:** the mod's routine lines stay out of the ~ console (errors still show); `mp_quiet off` to see all.
- **Partner badge:** a small marker coloured by their health instead of a name and numbers (`mp_badge_icon off` for the old one).
- **Launcher:** opens on two plain choices, **Host a game** and **Join a friend**, with the steps for each.
- **Name:** IgnisComeDeliverance (window, installer, shortcuts, About box).

A review of the mod's 133 newest on/off switches is in [docs/SWITCH-AUDIT.md](docs/SWITCH-AUDIT.md).

## Status

Built and tested offline: 10,286 native tests, 2,512 .NET tests and 5,088 Lua scenario checks pass.
**Not yet tested with two players in the game.** Please report what you see (the launcher's Report bug button
makes a bundle; in game, press the period key and type `mark <what you saw>` in the console).

## Install

Download from [Releases](https://github.com/lpoyoll/IgnisComeDeliverance/releases):

- **`IgnisComeDeliverance-Setup-<version>.exe`**: the installer. It is **not code-signed**, so Windows SmartScreen
  may warn: choose *More info → Run anyway* only if you downloaded it from this repository.
- **`IgnisComeDeliverance-DirectInstall-<version>.zip`**: an update for an existing IgnisComeDeliverance install.
  Close the game and the launcher, unzip, run `Apply.ps1`.

**It installs beside Kingdom Come: Together, not over it.** IgnisComeDeliverance has its own install folder
(`%LOCALAPPDATA%\IgnisComeDeliverance`), its own Start menu entry and its own uninstaller; Kingdom Come: Together stays
installed. The game has **one mod slot** (`Mods\kdcmp`), so only one of the two is active at a time: the
IgnisComeDeliverance launcher puts its own mod into the slot when it opens (the game must be closed). To go back to
Kingdom Come: Together, run its Setup again (it repairs its mod).

**Your Kingdom Come: Together saves work as they are.** Both share the game's saves and the player data in
`%LOCALAPPDATA%\KCDMP` (carried-over characters, world transfers). Your game saves are backed up before every session
(the launcher's *restore a backup*). When uninstalling either one, answer **No** to "remove the mod from your game"
if you still use the other.

**Both players need the same build.** The handshake refuses a partner on another build, including official
Kingdom Come: Together.

Requirements: Kingdom Come: Deliverance II and its free **Modding Tools** on Steam, Steam running, Windows 10/11.

## How to play

1. Both players: open the launcher. If it says *Finish setup*, press **Check setup** and follow it.
2. Host: press **Host**, send your friend the join code, press **Launch as host**.
3. Partner: press **Join**, paste the code (or pick the host from your Steam friends), then choose
   **Bring my character** or **Start fresh** when asked.

The upstream guide covers everything else (dice, features, troubleshooting):
[docs/UPSTREAM-README.md](docs/UPSTREAM-README.md), [docs/QUICKSTART.md](docs/QUICKSTART.md).

## Building from source

Needs the .NET 8 SDK, Visual Studio 2022 Build Tools with the C++ workload, and Inno Setup 6.

```powershell
dotnet build KCD2-MP.sln                                          # agent, relay, launcher
powershell -ExecutionPolicy Bypass -File native\Build-Native.ps1    # KCDMP.dll + native tests
powershell -ExecutionPolicy Bypass -File tools\Build-And-Install-Mod.ps1 -NoInstall   # kdcmp.pak (the game scripts)
powershell -ExecutionPolicy Bypass -File tools\Build-Installer.ps1 -ReleaseCandidate -SaveRoundTripWaiver "<why>"
```

`Build-Installer.ps1` runs every test suite before it compiles the Setup. Its publishing gate expects the upstream
maintainer's private denylist (`KCDMP_PUBLISH_DENYLIST`); set it to a file of your own private strings. Builds from a
very deep folder fail on Windows' 260-character path limit: build from a short path.

## Provenance and licence

- **Original project:** [marczukmichal/kcd2-multiplayer](https://github.com/marczukmichal/kcd2-multiplayer), by
  marczukmichal and its contributors, who keep the copyright on their code that remains here.
- **Kingdom Come: Together:** [DeepFriedDepp/KingdomCome-Together](https://github.com/DeepFriedDepp/KingdomCome-Together),
  maintained by DeepFriedDepp. Copyright (C) 2026 the Kingdom Come: Together contributors. This fork is based on its
  0.64.1 source.
- **IgnisComeDeliverance:** this repository, a modified version of the above. The changes are listed in
  [docs/KCT-FIXES-sync-tpose.md](docs/KCT-FIXES-sync-tpose.md) and marked `KCT fix` in the source.

Licensed under the **GNU General Public License version 3** ([LICENSE](LICENSE)) with the additional terms of its
section 7 in [NOTICE](NOTICE): keep the author credits and the official-repository notice, and mark a modified
version as different from the original. Authors: [AUTHORS](AUTHORS).

Kingdom Come: Deliverance II, its assets and its content belong to Warhorse Studios and PLAION. This is a free,
non-commercial fan project, not affiliated with or endorsed by them.
