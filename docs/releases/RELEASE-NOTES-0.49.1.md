# Kingdom Come: Together 0.49.1 — weapons that will not draw, the brawl protection

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.49.0 plus WO-174 (2026-10-11), versioned 0.49.1 by the maintainer. The evidence is `docs/WO-174-findings.md`; the player's guide is
`docs/HOW-TO-REPAIR-WEAPONS.md`; what to check live is `docs/TWO-PLAYER-CHECKLIST.md`, items 261-267. Everything here was checked on one
machine against a synthetic partner, never between two real players.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## New

- **`mp_repair_combat`** (console, and the mod menu: Help → Fix my weapons / stuck fight). For a Henry whose weapons will not draw although no
  fight is going on (the weapon icon is blank): it clears the hostile links with a partner's figure, ends a stuck fight through the game's own
  stop-fight, never touches a fight that belongs to a quest scene (it says so), and equips the best melee weapon in the inventory through the
  game's own call, re-testing the draw after each step. On the field's locked save the equip freed the weapons. On a good save it changes
  nothing and says so. It writes no save.
- **`mp_weapon_autofix`** (default on). The lock comes back when a save loads (the game rebuilds the weapon set from the save), so after a
  repair freed his weapons, every later load in that game session equips the same weapon again, silently (`WO174-REPAIR step=autofix-after-load`).
  After a game restart the command is typed once more.
- **`mp_combat_autoclean`** (default on). Before the mod's own world saves and the game's own save line, when a partner's figure leaves, and
  once after every load, the fight records between this Henry and a partner's figure are cleared; a friendly-fire punch's fight ends with the
  knockdown; the host takes a leaving partner's figure out of every skirmish before its body is removed (`WO174-CLEAN reason=…`).
- **Scripted brawls are protected.** The neighbours' brawl, quest fist fights, duels, arena and tournament fights are the game's own: a
  partner's figure is never added to their skirmish, never made a threat to or a target of their actors, and the actors get no hit-reaction
  message about him; his blow's damage stays real (`WO174-BRAWL … figure_kept_out=yes`).

## Changed

- **`mp_real_hits` is now on by default** (the host and one partner test it; `mp_real_hits off` turns it off; the capped raw path stays the
  per-blow fallback). It was off in 0.49.0.

## Known limits

- **The cause of the field lock is not found.** It came in during a night's sleep with a pickpocket and loot spree, with none of the mod's
  combat code running; the neighbours' brawl is not the cause. The lock is in the save and returns on every load until repaired (findings §3).
- The repair's equip works on the Modding Tools build; the scripted-brawl guard knows brawls by their actors' names.
- Not played by two people; the brawl's scripted end with a figure present was not run to the end.
