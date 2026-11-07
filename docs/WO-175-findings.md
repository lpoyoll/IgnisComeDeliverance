# WO-175 — save safety: what broke the save, the fix, and the safeguards (0.49.2)

2026-10-11, unattended. Marks: **[F]** read from the field files (copies of the host's saves, the host's session logs); **[L]** live, on
this machine's Modding Tools build (1.5.5), copies of saves in the throwaway `playline4` only (never a playline above 4), the game
minimised except during the key tests; **[K]** live with real key presses under the maintainer's input policy (below); **[syn]**
synthetic (the real kdcmp.lua under MoonSharp, or the agent against synthetic saves); **[unit]** the .NET / Lua / Python suites;
**[not run]**.

## In short

| | result | evidence |
|---|---|---|
| The cause | **a quest fistfight's "unarmed only" rule left on for good.** Quest *konecZabomysichValek* (the Tachov–Zelejov feud) → the field fight `souboj_na_poli` applies the game's own `EnableAllowedWeaponsInQAM` (main hand and off hand must be empty) **while its saved result is `Started`**. In the host's session the fight ended without a verdict (`_skirmishControler=Done`, `_skirmishResult=Started`), so the rule never lifted, and every later save carried it | [F] [L] |
| The record | ConceptModule `01f4/01f6/730a/01f2/036b/20a9` (the quest state: `ConceptState\0` + 8 bytes + XML) → `…/udalosti_na_louce/jindrich_na_strane_tachova/souboj_na_poli/_skirmishResult` | [F] [L] splice bisect |
| The mod's part | its **scheduled world save** was queued during the quest's meadow dialogue and the engine wrote it **on the first frame of the scripted fight** (`autosave323` and `324` are mid-fight saves); the partner's figure was inside the fight's skirmish. The verdict-less end itself is a hole in Warhorse's graph (below); the mod's figure is a plausible contributor, **not proven** | [F] |
| The fix (1.3) | S3: the mod never asks for a save during a dialogue, a scene, a fight or a quest's arranged fight (and the game's own saves wait while a mod change is in flight) | [syn] [L] |
| The repair (1.4) | `Wo175SaveHealth` (agent) / `tools/savegame/whs_repair.py`: the stuck result set to `Fled` in a **new file next to the old one** (`<name>_fixed.whs`), MD5 footer recomputed; the agent does it after every load and save and loads the copy; `mp_repair_combat` asks for it | [L] [K] [unit] |
| Proof | the field file `autosave335`, repaired → **loaded without the mod** → draws by the player's own weapon key, HUD shows the sword → sword unequipped and re-equipped → draws → saved → reloaded → draws | [K] |
| S1–S6 | all on by default (table §5) | |

## 1. The real draw test, and what WO-174 got wrong

- **The draw key.** The game's own binding (`Libs/Config/keybindSuperactions.xml`, profile `keybinds_settings` empty = defaults): `qam_init`
  = **B** (tap: toggle the quick-access weapon; hold: the QAM wheel), `qam_1..4` = **1–4** (`weapon_slot_N`: select that weapon slot and
  draw). A tap of B drew nothing **even on a good save** (`autosave322`) — it is not a plain draw — so the test uses **1**, the player's
  own weapon-slot key. [K]
- **The HUD.** Locked: the active-weapon slot shows a crossed-out icon. Repaired: the sword icon (the game's own screenshots,
  `r_GetScreenShot 1`, client area only; two kept in `docs/wo175-shots/`). [K]
- **WO-174's window was off by one.** Re-run here (fresh process and in-session loads, mod Lua loaded, no agent, no DLL): `321` and **`322`
  draw**, **`323` is the first locked save** (323–338 all refuse). WO-174's 321→322 window (sleep, pickpocketing, a chest) is innocent. [L]
- `Actor.SimulateOnAction("weapon_slot_1", …)` does not reach the quick-access path (nothing drew on a good save), so shipped code has
  no way to run the key path; the shipped repair checks the **cause** (no stuck quest fight in the loaded file) plus the scripted draw. [L]

| save | scripted draw | key 1 | HUD |
|---|---|---|---|
| `322` (copy) | draws | draws | sword |
| `335` (copy, the field file) | refused | **refused** | crossed out |
| `335` repaired (`autosave935_fixed`, mod parked outside `Mods`) | — | **draws**, `Changing weapon for Dude` | **sword** |
| the same, sword unequipped + re-equipped (`Actor.UnequipInventoryItem` / `EquipInventoryItem`, the inventory screen's own calls) | — | **draws** | sword |
| the same, `Game.SaveGameViaResting` → `autosave342` → reloaded | — | **draws** | sword |

## 2. Finding the record (1.1, 1.2)

- **Bisect by save** [L]: copies of `311`–`338` in `playline4`. The game lists saves once at start, so copies go in **before** the game
  starts; in-session loads then discriminate reliably (328 refused → 322 drew → 323 refused in one session).
- **Splice bisect** [L] (scratch tool: a block of the good save copied into the locked one, chunks and footer rebuilt): on `335` with
  `322`'s blocks — Henry's soul + his side blocks (the WO-112 set): **refused**; XGenAI (`01f9/7306`): refused; CryAction state (`01f7`,
  GameState): refused; EntityModule (`01f8/7302`): refused; **ConceptModule (`01f6/730a`): draws**. The quest XML then names it.
- **The quest graph** (`Scripts.pak`, `Quests/Final/Barbora/trosecko/konecZabomysichValek/konec_zabomysich_valek/souboj_na_poli.xml`):
  `EnableAllowedWeaponsInQAM` (RequireMainHandEmpty, RequireOffHandEmpty) `IsActive ← skirmishResult.Started`;
  `combat_forceUnarmedWhileInUnarmedSkirmish` and `combat_arrangedFight` contexts while the controller is Active. The result is set to
  Won / Lost / Fled / FledButTheFightIsStillOn / Abort by the fight's events; **when the enemy side wins while the player is conscious and
  the result is `Started`** (he fled — `FledButTheFightIsStillOn` — and was pulled back in — `SoulAdded` sets `Started` again), the
  controller goes Done and **no verdict is written**: the rule stays on. The saved objective log in `335`: `FledButTheFightIsStillOn
  UpdateTime=54498130`, then `Started UpdateTime=54508044`.
- **The equipped flag.** In `323` the sword's own item record lost its "equipped" byte (param 2 `01…` → `00…`) while the hand slot still
  names it: a symptom of the fight's "hands must be empty" rule, not the cause — WO-174's equip writes it back (`autosave339/340` here)
  and the reload still locks.
- **The session** (`KCDMP-logs-20261010-224122`, lines 415,316 → 431,652) [F]: Henry asks Procek (`ttac_procek`) → the quest's fast-travel
  cutscene to the meadow → the meadow polylog (`dialogue 22936`); during it the mod's scheduled save asks 8 times (`MP-WORLDSAVE request
  … attempt=1..8`, `Savegame requested by SaveGameViaResting()`); the polylog ends → `InteractiveSceneManager::EndScene` → the queued
  save fires in the same frames the quest adds everyone (Henry included) to the fight's skirmish → `autosave323`; `324` 1 s later. Then
  the fight: two `PlayerFlee` on Dude, the partner's figure `kcd2mp_2` knocked down by a friendly-fire punch inside the fight
  (`WO155-FALL`), `SkirmishVictory on ttac_man_3 (target kcd2mp_2)`.

## 3. The fix in the mod (1.3)

- **No mod save at a bad moment** (`KCD2MP_W175SaveAllowed`, called first by `KCD2MP_HostWorldSave`): refused, logged
  `WO175-SAVE deferred reason=<dialogue|scene|combat|arranged-fight|sleep-hold|weapon-repair|loot-take|item-take|loot-open> kind=world-save:<why>`,
  asked again by the agent 5 s later (its existing 12 × 5 s loop). The arranged-fight test is the quest's own contexts
  (`combat_arrangedFight`, `combat_forceUnarmedWhileInUnarmedSkirmish`) through `soul:HasScriptContext`. [syn] 12 checks, [L] (`reason=loot-take`).
- **Not built**: keeping the partner's figure out of quest-arranged fights by context (WO-174's guard works by actor names — `rvacka`,
  `fistfight`, … — and the meadow's `tzel_*`/`ttac_*` fighters are not covered). Open, see §7.

## 4. The repair of affected saves (1.4)

- `dotnet/KcdMp.Client/Wo175SaveHealth.cs`: `Check` finds every `<_skirmishControler value="Done" />` + `<_skirmishResult value="Started" />`
  pair under one element (an Active controller — a fight in progress — is never touched); `Repair` changes that one element to
  `<_skirmishResult value="Fled"    />` (same byte length), re-deflates, recomputes the footer, re-checks its own output. `Fled` is the graph's
  own verdict for a player who left the fight (no unearned "won" reward). The game re-saves it as `value="Fled"`. [unit] 20 + 2 agent tests.
- **The agent** (`GameBridge.Wo175.cs`, mp_save_health): after a load (the Lua's `w175_check load` on the load settle; `w175_check repair`
  from `mp_repair_combat`) it checks the loaded file; stuck → `<name>_fixed.whs` beside it (a `.part`, renamed, read back; never over an
  existing file; the original is never written) → the native save list rescanned → the copy loaded, the player told once
  (*"A quest fight in this save never finished, so your weapons were locked. The mod made a repaired copy (your original is untouched) and
  loads it now."*). Without the plugin the player is told to load the copy after a restart. After a save (an always-on watch of the saves
  folder): every new engine-named save is checked; a stuck one gets its copy at once. Lines `MP-W175 …`, Lua `WO175-HEALTH when=… result=…`.
- **Offline**: `tools/savegame/whs_repair.py check|repair <save> [--out <new>]` (never in place, refuses an existing target); the agent's
  `KcdMpClient --w175 check|repair <save>`. On copies: `335` → exactly one stuck fight; `322` none; `323` (controller Active) none. [L]
- **The live proof**: §1's table, on `autosave935_fixed.whs` written by the agent's own `--w175 repair`. [K]

## 5. Safeguards (all on by default)

| | what | switch | check |
|---|---|---|---|
| S1 | **co-op plays in its own copy of the world.** Hosting one of *your worlds*: before launch the agent copies the world's newest save (bytes unchanged) into a co-op playline (the one already recorded for that world, else the lowest free one; never an index above 4; never over an existing file); at the menu pick the copy is loaded, never the original; the menu lists it as **"Together: <name>"**. The joiner's spliced world goes into a co-op playline too, never the joiner's own. No free playline: nothing loads, the player is told why (`WO175-COOP refused reason=no-free-playline`) | `mp_coop_playline` (mod-settings.json `CoopPlayline`) | [unit] 14 tests (the original's files byte-identical); **[not run] live**: the launcher was not started (it takes focus) |
| S2 | **rolling backups.** Before the launcher starts the game: the whole saves folder into `%LOCALAPPDATA%\KingdomComeTogether\save-backups\<yyyyMMdd-HHmmss>\` + `manifest.sha256`, verified; last 10 kept, the newest 3 never deleted; **RESTORE SAVES** (status bar) → pick → confirm → the current folder backed up first → byte-equal restore, verified; refuses while the game runs. `WO175-BACKUP` / `WO175-RESTORE` lines | Settings: *Back up my saves before each session* | [unit] 12 tests (round trip byte-equal, tamper detected, retention); [not run] in the running launcher |
| S3 | **no save while the mod is mid-change.** The mod's own save waits (§3). The game's autosave and manual saves wait under the script save lock `kcdmp_w175_change` while a mod transaction is in flight (a WO-134 body/chest/item take, a WO-140 held bed, a WO-174 weapon repair); max 10 s, then the mod rolls back what it can (a held bed goes back to the game's own use) and the lock goes. `WO175-SAVE deferred|resumed|max-wait` | `mp_save_guard` | [syn] 8 checks; **[L]**: lock on → `[Error] AutoSave is disabled under a script lock 'Script:kcdmp_w175_change'` → released on completion (`resumed reason=loot-take`) and at the cap (`max-wait reason=sleep-hold` → `WO140-DROP`). The transactions were flagged by hand (no partner session) |
| S4 | **a health check before every save and after every load**: the file check (§4); the WO-174 partner-figure clean-up before saves stays | `mp_save_health` | [unit] [syn] [L] (offline + the CLI); the agent's in-game load/reload of a repaired copy: **[not run] live** (needs the plugin's rescan) |
| S5 | **the save round-trip gate** in `Build-Installer.ps1`: synthetic (`Test-WO175Synthetic`, the agent's Wo175 tests, `test_whs_repair.py`, `test_whs_dump.py`) + live (`tools/Test-WO175SaveRoundTrip.py`: throwaway playline4 only; draw → save → health → reload → draw; SKIPPED with the reason when no game runs) | — | runs in the build |
| S6 | plain words: `docs/QUICKSTART.md` §"Your saves", the launcher's one-time notice on its first start, the Restore dialog's own text | — | — |

**The input policy (rule 6) gate**: `tools/Test-NoInputTooling.py` over the whole payload, in `Build-Installer.ps1`. Our own files — the
mod's paks and Lua, `KCDMP.dll`, the agent, the relay, the launcher, Setup — **0 hits** (no exemption applies to them). **Not literally
zero bytes**: the launcher's self-contained publish carries Microsoft's .NET Desktop runtime (WinForms, WPF, UI Automation,
`System.Console`), whose public APIs include `SendKeys`, `SendInput`, `GetKeyState`, `SetWindowsHookEx`; those 11 file names are listed
as reviewed exemptions, each only when it carries Microsoft's copyright string, and printed on every build. Dropping WinForms/WPF from the
launcher publish would remove them (open). The DLL's reads of the game's own input state: none (the mod sees actions only through the
game's `Player.OnAction`, kdcmp.lua's action hooks). `tools/dev-input/send_key.py` (this WO's key tool) is the gate's positive control: 1 hit.

## 6. Input policy log

Keys sent this run, all to the focused Modding Tools game window, each preceded by a foreground check (`tools/dev-input/keys.log`,
git-ignored): B ×6, 1 ×7, I ×1, Esc ×1 (the I/Esc pair proved input reaches the game: `ApseOpen` / `ApseClose`). Screenshots: the game's
own `r_GetScreenShot`. Between tests the game was minimised.

## 7. Open, plainly

- **The partner's figure in quest-arranged fights.** Not kept out by context; WO-174's name list does not cover this quest's fighters.
  Whether the figure's presence made the fight end without a verdict is **not proven**; the repair covers the result either way.
- **The graph hole is Warhorse's**: a player who flees, is pulled back in and whose side then loses while he stands can get the same lock
  in an unmodded game. The repair handles it; the mod cannot prevent it.
- **S1 live**: not run (the launcher would take focus); the first join into a co-op playline whose folder did not exist relies on the
  native rescan of a new folder, unproven since WO-159.
- **S4 in a live session**: the agent's automatic repair-and-load was not run live (needs the injected plugin's rescan); the file repair
  and its load were.
- **No two players**; the S3 transactions in the live check were flagged by hand.
- The no-input gate's Microsoft runtime exemptions (§5).

## 8. Gates and the installer

See the installer record: `docs/WO-175-installer.md`. In short: `release\KingdomComeTogether-Setup-0.49.2.exe`, 106,875,381 bytes, sha256 `1472d041…0217`, header 0.49.2, unsigned, soak waived; agent 1,503 · native 9,318 · 60 Lua suites / 4,017 checks · relay 63 · setup 77 · logpack 14 · save tools 7 + 14 · no-input gate 0 hits · installer cases 26/26, 41/41; the live round trip SKIPPED during the build (no game running).
