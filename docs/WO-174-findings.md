# WO-174 — the host's weapons are stuck (0.49.1)

2026-10-11, unattended. Marks: **[F]** read from the field files (a copy of the host's save, the host's session logs); **[L]** live, on this
machine's Modding Tools build (1.5.5), the game minimised, a throwaway `playline4` only (copies of saves, removed afterwards), no key / mouse /
game input injected (scripted console and Lua calls on the throwaway only); **[syn]** a synthetic joiner against the real agent, relay and DLL;
**[unit]** the .NET / Lua / native suites; **[not run]**.

## What shipped, in short

| | what | evidence |
|---|---|---|
| Phase 0 | `tools/savegame/whs_dump.py` (read-only, never shipped) + tests; the container and the footer **solved**; the `kcd2mp_` strings are **not** a combat table | [F] [unit] 7 tests |
| 0.4 | the timeline of the stuck session: **the neighbours' brawl is not the cause** (the weapons still draw in the saves taken with the brawl running); the lock came in during one night's sleep + a pickpocket / loot spree, with **no combat action of the mod at all** in that window | [F] [L] |
| Phase 1 | the lock named: **Henry's draw is refused although no fight, skirmish or combat state exists**; `Actor.EquipInventoryItem(sword)` then `Human.DrawWeapon()` frees it for the running session; **a save taken after that re-locks on reload** — the saved state that brings it back was **not identified** | [L] |
| Phase 2 | `mp_repair_combat` (console + mod menu *Help → Fix my weapons / stuck fight*) with its four steps; **freed the field copy** at step 4 (equip); on a good save it changes nothing and says so | [L] [unit] 29 Lua checks |
| Phase 2+ | `mp_weapon_autofix` (new, default on): because the lock returns on reload, a repair that freed his weapons is re-applied silently after every later load in that game session | [L] [unit] |
| Phase 3 | `mp_combat_autoclean` (default on): partner-figure fight records cleared before saves / on a partner leaving / after loads; the friendly-fire fight ends with the knockdown; the scripted-brawl guard (DLL + agent) keeps a figure out of a scripted brawl | [syn] [L] [unit] [native] |
| D | `mp_real_hits` default **on** in the agent, the Lua and the DLL | [unit] |

## 1. The save container and the footer (Phase 0.1–0.3)

`tools/savegame/whs_dump.py` parses the file read-only (`info`, `find`, `count`, `diff`); it never prints the account name the description
carries. The field file `autosave335`: 148 chunks, body 4,824,711 bytes, sizes match — the unit test pins it (`tools/savegame/test_whs_dump.py`).

- `ff ff ff ff`, a u32 **length of (the `C_SaveGameDescription` XML + its own trailing `0a 00`)**, the XML, then chunks
  `[u32 compressed][u32 uncompressed ≤ 0x8000][zlib]`. A chunk can also be **stored**: `[0xFFFFFFFF][raw length][raw]` (WO-132's finding; the
  first version of the tool refused 3 of the 33 saves for it).
- **The footer's 16 bytes are an MD5.** The WO's offline read tried the whole file, without the first 8 bytes, the chunk region, the body and
  the XML. The convention that matches (every save checked, the field file included) is the CryEngine XMLCPB one **already recorded in
  `tools/Read-SaveAnatomy.py`**: MD5 over the whole file up to the footer **plus the footer with those 16 bytes zeroed** (`30 58 42 50`, 16 zero
  bytes, the 44 trailing bytes). The mod's own readers (WO-96, WO-112) and the game do not enforce it (WO-115). Nothing here writes a save.
- **The `kcd2mp_` strings are not a combat or skirmish table.** The one hit sits in `01f9/1f91/11cb`, the *XBehavior per-NPC records*
  (name + id, then hash/value pairs; 1,934 bytes): a list of the NPCs' behaviour records, among them `Dude` and the figure's. They are present in
  the saves that draw weapons too (`autosave311`: 1; `316`: 2; `322`: 5) and gone when the figure is (`autosave337`: 0). The "pair table" of
  the WO's first read is that block's string pool, not a hostile-pair or history table.
- `rvacka_neighbour_1/_2` (2 hits from `autosave323` on, 8–10 in `317`–`322`) are the brawl **random event's** saved record
  (`Barbora.trosecko.eventPlaces.ttac_villagePub.rvacka`, variant `neighbours`); `isUnarmed`, `weaponChangeMode`, `weaponSlot` are entries of the
  XBehavior *variable-name table* (0x32cc..), a schema, not state; `fistfightsChampion` is a world layer that is enabled in every save.

## 2. The stuck session's timeline (0.4)

The host's 2026-10-10 launch (20:08–22:41, `KCDMP-logs-20261010-224122`); the named `HOST-…` bundles of the WO are not in `logs/`, the other
five 2026-10-10 bundles and the two 2026-10-11 ones were read. Saves are autosave numbers; lines are `kcd.log` lines of that launch.

| when | what | mod's actions |
|---|---|---|
| ~21:44 (line 359,850) | the neighbours' brawl random event starts at the Tachov pub (`rvacka` / `neighbours`); `rvacka_neighbour_1/_2` polylog | a friendly-fire knockdown of the partner's figure just before (`WO155-FALL`, line 360,458); NPC-SYNC tracks the brawlers |
| 21:47 `autosave317` (363,689) | saved with the quest name `koneczabomysichvalek` at **its start objective** (`zacatek_questu`); the event is saved running | the mod's scheduled world save |
| 364,602–364,745 | Henry is added to the brawl's skirmish (thread 14644), removed 143 lines later | — |
| 367,010 | the maintainer **reloads `autosave317`**; the brawl event is created again | — |
| 377,733–381,366 | the brawl runs: `PerfectBlock`, `SoulUnconscious` on `rvacka_neighbour_1`, `SkirmishVictory` on `rvacka_neighbour_2`, `rvacka_neighbour_1 Dropped weapons when combat is ending` — **it ends by its own script** | no WO-121 attributed-hit, WO-136 target or WO-165 line in the 359,000–430,000 window names a brawler; **no** skirmish line names a figure (`kcd2mp_` in a skirmish line: 0) |
| `autosave318` … `321` (374,136 … 398,264) | quest name kept, objective cleared (`quest:''`); the weapons **draw** in every one of them [L] | — |
| **398,264 → 415,274 (`321` → `322`, 21:57–22:01)** | night turns to day (the quest state's `_whatTime` IsNight → IsDay, timers run down 25 million ms): **a night's sleep** at `BedTrigger9`; a **pickpocket spree** (195 `pickpocket_timer` audio lines; `ttkc_tradersMom`…), the game's *"Ignoring draw of item in DrawItemLogic. Item is no longer in owner's inventory"*, the inventory screen (4 `MoveItem`), a chest looted (the mod only logged 38 `WO134-CHEST take` lines); the quest state gains *"an NPC wakes from unconsciousness, knows the player knocked him out and searched his pockets"* | **WO-140** held the lie-down for 4.3 s (`WO140-HOLD … WO140-GO`), **WO-164-REST** topped the rest up; **0** lines of WO-121 / 136 / 155 / 165 / 170, **0** skirmish events |
| `autosave322` … `335` | the weapons **do not draw** [L] | — |

**Why the brawl is rejected as the cause.** It saved running in `317`–`322` and the weapons drew in `317`–`321`; it ended by its own script
before `319`; and the game reports no combat, no combat danger and no skirmish for Henry on the locked copy (§3). **Why the mod's combat code
is rejected:** nothing of it ran in the window the lock came in. **What is left, unproven:** the sleep (the maintainer's own diagnosis names the
brawl; the data says a night's sleep + pickpocketing + looting happened between the last good and the first bad save — the mod's only
interventions on his Henry there were WO-140's held lie-down and WO-164's rest). The same lock appears **without** the mod's code running
(the field: Modding Tools alone; here: the copy loaded with no agent and no DLL), so it is the game's state, the mod's part (if any) is how it
got there.

## 3. The lock, named (Phase 1) [L]

Method: copies of `autosave275 311 316 317 318 319 320 321 322 325 328 329 330 331 332 335` in `playline4`, mod Lua only (no agent, no DLL),
a **holster → `DrawWeapon()` → read `IsWeaponDrawn` 2.5 s later** cycle on each (a save can load with the weapon already out, which hides a
lock; `autosave311` is the control that draws). **Never a playline above 4: a `wh_sys_LoadGame 5 …` crashed the game at once.**

| | good (`311 316 317 318 319 320 321`) | locked (`322 325 328 329 330 331 332 335`) |
|---|---|---|
| `IsWeaponDrawn` after `DrawWeapon()` | true | **false** |
| `Soul:IsInCombatDanger()` / `IsInCombatMode()` | false / false | false / false |
| the player's script contexts (all 648 of `ScriptContext.xml` through `HasScriptContext`) | `UnconsciousHolsterInsteadDropWeapons` only | the same one |
| `IsPickpocketing`, `IsCarryingCorpse`, mounted, in dialogue, unconscious, hands | none | none, hands empty |
| the inventory | the same 120 items, the sword `huntingSwordBasic` in it | the same |
| the equipment list (`f1301/0x6`) | 12 pieces + the sword | `322`–`332` identical to `321`; `335` has the sword only (the armour list is empty there) |
| the soul record, the buffs (hangover, oversleep), `0x12FF`, the entity `Dude` | — | identical or irrelevant |
| `ToggleWeaponSet(1/2/3)` (3 s each) | the sword set draws | nothing draws |
| **`Actor.EquipInventoryItem(sword)`, then `Human.DrawWeapon()`** | — | **draws** |

So the state that holds the lock is **an empty / unusable weapon set** (the game's HUD icon is blank; `Human.DrawWeapon` is "draw the active
weapon set" and there is none): a fight state (the WO's hypothesis) is **rejected** — there is none, and no step of `StopFight` is involved.
What was ruled out in the file, in case it is looked for again: Henry's soul fields, the buff list, the equipment list, the item records'
equip parameters, the GUIModule and PlayerModule blocks, `ConceptModule`'s quest XML (the 321 → 322 diff is the sleep, the knocked-out NPC and
the bathhouse's check), the entity `Dude`.

**What the equip does and does not do.** After the equip the running game is whole: plain `DrawWeapon()` and `ToggleWeaponSet` work as in a
good save. A save taken then (`Game.SaveGameViaResting`, `autosave336`) **loads locked again** and the saved equipment list is unchanged
(`f1301/0x6` 153 B before and after). So the game rebuilds the weapon set at load from saved state that the equip does not change, and the
part of it that fails was not found in the save. This is the one open result of the WO; it is why Phase 2 got its re-apply.

## 4. `mp_repair_combat` (Phase 2) [L] [unit]

Console `mp_repair_combat`, or *Insert → Help → Fix my weapons / stuck fight* (menu item 16). Solo, host or joiner, with or without a session.
Nothing is written into a save; every step is the game's own call; one line each: `WO174-REPAIR step=… result=… draw_ok=yes|no`.

| step | does | on the field copy |
|---|---|---|
| baseline | a draw test: `DrawWeapon()`, read after 2.2 s, holster what it drew; refuses in a dialogue / when dead | refused |
| 1 figure-links | `AI.RemovePersonallyHostile` both ways between this Henry and every partner figure (`kcd2mp_*`) that is a hostile pair | 0 (none), retest refused |
| 2 stop-fight | `XGenAIModule.TryEndCombat` and the agent's `StopFight` on this Henry (WO-154's path) | asked, retest refused |
| 3 brawl | if a fight is still on **and** an actor of a scripted brawl or scene (names: `rvacka`, `fistfight`, `brawl`, `duel`, `arena`, `tournament`; actors only) is within 40 m: **stop, touch nothing**, say so (*"This fight belongs to a quest scene … reload a save from before it, or send your logs"*) | no scripted brawl |
| 4 equip | the best melee weapon in the inventory (sword > axe/mace > dagger, then health; never a torch, a quest alias, a bow, a shield, ammunition or a smith's kit) through `Actor.EquipInventoryItem`, then the draw test | **`huntingSwordBasic` equipped, draws → freed** |
| result | *"Your weapons are free again — save your game now (a new slot is safest)."* or what was tried and *"send the log"* | |

On a good save (`autosave311`): *"Nothing was wrong: your weapons draw and no fight is stuck. I changed nothing."* — no step runs. Steps 1–2 are
the WO's "always run the safe cleanups" and run whenever the baseline fails, a figure link exists or a fight is on.

**The honest limit (and `mp_weapon_autofix`).** The reload re-locks (§3), so the save made after a repair is not enough on its own; the how-to
says so. `mp_weapon_autofix` (console, default on) makes it painless: once a repair has freed his weapons, every load in that game session
(the agent's load-settle call, and Lua's own reload detector that needs no agent) equips the same weapon again, silently, one log line
(`WO174-REPAIR step=autofix-after-load …`). Live: the throwaway `autosave336` reloaded locked, the re-apply ran, the next plain `DrawWeapon()`
drew. It does not survive a game restart (a Lua global): run the command once per game start. **The check the WO asked for — save into the
throwaway, reload without the mod, it still draws — FAILED** (`autosave336`, reloaded locked, with and without the re-apply); I did not park the
mod folder for a third launch because the failure was the same with the mod's code idle.

## 5. Never again (Phase 3)

**What the mod can and cannot have done.** The lock came in with no combat code of the mod running (§2). The prevention below is hygiene the WO
asked for and it works as measured, but it is **not shown to prevent this lock**.

- **`mp_combat_autoclean` (default on)** [syn] [L] [unit]. `WO174-CLEAN reason=save|leave|load removed=<n>`. Before the mod's own world save
  (`KCD2MP_HostWorldSave`) and on the game's own save line (the log tail's `InitiateSaveGame()` of a loadable kind, anchored at the line start,
  2 s throttle), when a figure leaves (`KCD2MP_RemoveGhost` calls it first), and once after every load (the WO-136 settle). It removes the
  hostile pairs between Henry and the figures and, when he is locked in melee with one (`Player.IsLockedToOpponent`), asks the agent to end it.
- **The friendly-fire punch ends with the knockdown.** The WO-155 fall now schedules `w174_endfig` 1.2 s later: the agent takes the figure out
  of the skirmish through WO-132's own leave-fight op (RemoveSoulFromSkirmish). Live: *"MP-W132 peer 1 friendly-fire knockdown: the punch's
  fight ends: its avatar left its skirmish -> removed"*.
- **A partner leaving** (the host): before its body goes (`GameBridge.cs`, before the entity id is forgotten) the figure leaves every skirmish.
  Live: *"MP-W132 peer 1 partner left: … -> removed"*, then `WO174-CLEAN reason=leave`, then `Removed ghost`.
- **The saved result.** A throwaway from `autosave311`: the synthetic figure spawned, a friendly-fire fall, the partner leaves, a save
  (`autosave338`): **0** `kcd2mp_` strings in the body (the original `311` has 1) — checked with `whs_dump.py count`. The draw check after it: the
  copy derives from a good save, it draws; it says nothing about the field lock.
- **The scripted-brawl guard** [L] [unit] [native]. `wo174_rules.h` (the names; 13 native checks) and `hits::scene_owned_eid` gate every place the
  mod could put a figure into a skirmish or turn an actor on one: the WO-121 attributed hit (**after** the real damage: the blow stays real; no
  combat history, no skirmish, no threat), WO-136's retarget and threat, WO-165's host lock, WO-163's pair, WO-132's copy engage; the agent
  adds the same names to its engagement and to the hit-reaction brain message (`MP-ATTRIB … brain_msg=0`). `WO174-BRAWL skirmish=scripted
  quest=by-actor-name npc=… site=… figure_kept_out=yes` (once per actor per 10 s). Live, on the real brawl (`autosave317`, the neighbours
  fighting beside Henry, a synthetic figure hitting both): the line for both neighbours, `skirmish=0 history=0 brain_msg=0`, **0** skirmish
  events naming the figure, *"Soul 'kcd2mp_1' is not in any skirmish"*. **Not run to the end:** the brawl did not reach its scripted end in
  that window (the brawlers talked; bystanders fought Henry), so *"the brawl ends by its own script, the quest advances"* is **[not run]** — the
  guard only ever *removes* the mod's influence, so it cannot make the end later than the game's own.
- A partner who **crashes** mid-brawl: the same leave path (the relay's disconnect) — [syn] for a clean disconnect only.

## 6. D — `mp_real_hits` on by default

The agent (`Wo173Rules.DefaultRealHits = true`), the Lua (`KCD2MP.w173.realHits = true`, the help text) and the DLL (`g_on{true}`); `mp_real_hits
off` still turns it off; the capped raw path stays the per-blow fallback. WO-173's tests updated: `Wo173Tests` (asserts the default true),
`Test-WO173Synthetic.lua` S2. The WO-173 public-release rule is not met (the block-rate check never ran); 0.49.1 is for the maintainer and one
partner, who chose it.

## 7. Gates

| | before (0.49.0) | now |
|---|---|---|
| agent | 1,423 | **1,455** (+32: the save line, the ghost id, the scene words) |
| native | 9,305 checks | **9,318** (+13) |
| Lua synthetic | 58 suites / 3,951 checks | **59 suites / 3,988 checks** (new `Test-WO174Synthetic`, 37; `Test-WO154Synthetic` item numbers moved by one) |
| save tool | — | 7 tests |
| relay 63 · setup 77 · farkle 59 · logpack 14 · guards 7/7 | | run by `Build-Installer.ps1` (see the installer record) |

Saves: the whole saves folder backed up byte-equal first (826 files, SHA-256 each, `saves-backup-wo174-…`); at the end the folder equals the
backup (the throwaway `playline4` removed); the maintainer's `Mods\kdcmp` pak was replaced for the live runs and **restored byte-for-byte**
(sha256 `fc0c8146…`, keys `42a4da74…`).

## 8. Untested / open

- **The persisted cause of the lock** — not found (§3). Where to look next: a save from before and after the *very sleep* with the game's own
  bed use as the only difference (the maintainer plays the sleep once without the mod's WO-140 hold: `mp_sleep_vote off`), then
  `mp_repair_combat` is the answer again if the weapons are gone after it.
- No two real players; the partner crash path; the brawl's scripted end and the quest's advance with a figure present; the repair on a
  **joiner**; `mp_weapon_autofix` across a *game restart* (by design it does not); the game's own non-mod save line trigger (the line is
  matched and the Lua answers — only the mod's own save was seen to clean live: `WO174-CLEAN reason=save`).
- The scene-name list is a heuristic by actor names; a scripted fight whose actors carry other names is not protected.
- The native part has no new engine dependency (no `wo145` census rows): the guard only reads an entity's name through the existing
  `engine::entity_name`.

## 9. The installer (local, unsigned, release candidate)

Fresh clone of `ebd9056` (`release\c0491`, git-ignored), the three start saves copied in (SHA-1 equal to the source copies), `tools\Build-Installer.ps1
-SoakWaiver "<the maintainer's standing rule, quoted in the WO-174 run prompt (2026-10-11): 'Ends in a release-candidate installer, always (soak
waived under the standing rule, quoted and dated)'; built unattended 2026-10-11>"`: `release\KingdomComeTogether-Setup-0.49.1.exe`,
**106,821,681 bytes**, sha256 `542f4bedf1b3377ec3b5813a2bc02f429aedde4459ba43f7158f8091ac90d963`, header version **0.49.1** (checked), the build's
own gates green (agent 1,455 · native 9,318 · 59 Lua suites / 3,988 checks · relay 63 · setup 77 · logpack 14 · the installer cases 26/26 and 41/41,
no FAIL line), unsigned (no signing settings on this machine). Local only: nothing uploaded. **The first build attempt of this run stopped at the
gate** (`Test-WO154GameSynthetic`: the repair's Lua called `Player.IsLockedToOpponent` — there is no such global in the game's Lua, the call is
`player.player:IsLockedToOpponent` — and used `AI` / `player` unguarded): fixed in `ebd9056` and the installer rebuilt from it; the gate did its job.

