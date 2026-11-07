# Kingdom Come: Together 0.49.2 — your saves are safe

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.49.1 plus WO-175 (2026-10-11), versioned 0.49.2 by the maintainer. The evidence is `docs/WO-175-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 268-275. Everything here was checked on one machine; none of it with two real players.

## What changed

- **Co-op plays in its own copy of your world.** Hosting one of your worlds copies it into a free save slot first; the session plays and
  saves only there, and the menu shows it as "Together: <your world>" next time. The joiner's world lands in a co-op slot too. No free slot:
  hosting says so and loads nothing. `mp_coop_playline` (on).
- **Backups before every session.** The launcher copies and checks your whole saves folder (`%LOCALAPPDATA%\KingdomComeTogether\save-backups\`,
  the last 10 kept); **RESTORE SAVES** brings one back, after backing up the current folder.
- **The weapon lock, found.** A quest fist fight (the Tachov–Zelejov meadow fight of the neighbours' feud) that ended without a winner
  left the game's own "fists only" rule on, saved forever. The mod's 5-minute save had landed in the first second of that fight. Now the
  mod never asks for a save during a conversation, a scene, a fight or a quest's arranged fight, and the game's own saves wait (at most
  10 s) while the mod is changing your inventory, equipment or body. `mp_save_guard` (on).
- **Locked saves repaired.** Every save and every load is checked; a save with the stuck fight gets a repaired copy next to it
  (`<name>_fixed`, the original untouched) and the copy is loaded. `mp_repair_combat` runs the same check. `mp_save_health` (on).
  Offline: `tools/savegame/whs_repair.py`.

## Known limits

The partner's figure is not yet kept out of quest-arranged fights by context; the launcher parts (the co-op copy, backups, restore) were
not run in the running launcher; the agent's automatic repair-and-load was not run in a live session. See the findings, §7.
