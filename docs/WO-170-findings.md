# WO-170 — 0.48.6, unattended: the partner's figure never fights on its own, the joiner's swings, riders, no re-pause during a dialogue scene

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, this machine's Modding Tools build (1.5.5), throwaway playline only, the game minimised, no input;
**[syn]** a Lua synthetic suite; **[unit]** the engine-free .NET / native suites; **[L-field]** read from the testers' logs of
0.48.5 (the two bundles of 2026-10-10 16:23; no player name, address or path written here); **[code]** read in our code;
**[not run]**. The version is the maintainer's (**0.48.6**). Soak waived: the maintainer said so in chat for this build
("Skip/waive the soak on this build", 2026-10-10), which is also the standing rule.

## Evidence read (0.48.5, host and joiner bundles) [L-field]

* **The host's figure timeline** (native log, the joiner's figure `kcd2mp_2`): bound 16:17:53; its combat automation turned OFF at the
  joiner's first combat bit (16:18:31); combat bit on/off three times (16:18:31, :40, :48), each a swing the joiner sent; at **16:18:49.439
  the body was released** (`WO121-MOTION … released -- automation back on`, `MP-NPCWRITE … drop reason=unbound`) and **not bound again until
  16:19:08** — 19 s with its automation back on, after WO-136 had put it in a skirmish with two villagers as "its opponent"
  (`WO136-SWING avatar … at npc=tzel_woman_11 … skirmish=added`, 16:18:48.9; `… tzel_adam`, 16:18:56.9).
* **The blows**: `PIPE: LocalHit` on `tzel_adam` at 16:18:58.1, 59.1, 19:02.3, :03.4, :04.6, :05.5 (5.9–20.7 hp, ~1 s apart), **after the
  joiner's last swing (16:18:56.9)** — nothing relayed was left to cause them. The host's log never names the attacker of a LocalHit, so
  "the figure" is an inference from the timing (no relayed swing, the figure the only fighter near, automation on, in a skirmish).
* **F2's "0 received" was a log gap, not a drop**: all 4 outbound `kind=Attack` of the joiner arrived on the host as
  `MP-SWING hop=recv rsid=2..5 … kind=Attack seq=1..4` and `hop=queued … ok=1`, and the summary's `accepted=` counted them. The
  `MP-ACTION section=inbound` line is written only by kinds with a dispatch line of their own (Jump, NpcAttack …); an Attack is
  dispatched into the swing path, which wrote none.
* **Why the NPC started it** (host, 16:18:55.6): the engine's `CombatShout` with `Ex: tzel_adam … COMBAT_ACTOR_SCREAM_ATTACK; Nx: kcd2mp_2 …
  COMBAT_SHOUT_OPPONENT` — the figure is the shout's opponent. Nothing before it in the agent log names a cause (the WO's "no bump bark").
* **The rider crash** (joiner, 16:04:14) [L-field]: the world's own `dummyWanderer_horse_N` copies are parked at one place
  (27.4, 1249.6) far from every player; the stand-in `dummyWanderer_nobleman_man_2` was spawned beside the player at t=286.81 and
  `npc_native … off rider` came 14 ms later (the mount: WO-164 R2's three ticks on the horse's stream), the game died next. The horse copy
  was bound `physics=none-yet` 2 s earlier.

## F2 — the joiner's swings reach the host [L][unit]

* **Shipped:** `MP-ACTION section=inbound ghost=<id> kind=Attack seq=<n> dispatch=swing-queued rsid=<n>` for every Attack dispatched, and
  `kinds=Attack:N,Jump:N …` in the periodic summary line. No wire or behaviour change (nothing was dropped).
* **Check [L]** (host role, the synthetic joiner sends 5 then 10 `Attack` actions): `MP-ACTION … accepted=15 … kinds=Attack:15`, 15
  `dispatch=swing-queued`, 15 `MP-SWING hop=queued ok=1`, 15 native `SWING: entity=… queued fragment` (all "watched"). The synthetic
  peer got a `jattack` verb for it. **[unit]** 10 inbound Attacks → `AcceptedByKind[Attack]==10`, `kinds=Attack:10`.
* Default: always on (logging only).

## F1 — the partner's figure never fights on its own [native][L][unit]

* **Found (the mechanism, inferred from the timeline above):** the figure's combat automation (the engine's four automations:
  attack, defence, guard, weapons/zone) was off only while the partner's combat bit was set, and **given back on a release** — an unbind
  (any reason) turned the automation of a body that stays in the world back on. In that state the figure sits in a skirmish
  (WO-136 added it as the villager's opponent) with automation on: its own brain attacks, and draws its weapon (the fistfight sword).
  Not proven by an engine read; the proof below is of the fix's mechanism, not of the field's blows.
* **Shipped, default ON, `mp_figure_guard on|off`** (host; the agent pushes it to the DLL, pipe op 11's fifth byte):
  1. **The automation stays off for the whole life of a figure's body** (`want_automation_off(guard, combatBit)`): at the bind, not
     only with the combat bit, and **a release for an unbind keeps it off** (`WO170-GUARD … its automation stays OFF`); only the
     combat feature switched off gives it back. The weapon follows the partner's mirrored state, never its own choice (the Weapons
     automation is off).
  2. **The backstop at the hit chokepoint** (`hits.cpp`, the CombatHit slot): a melee hit whose attacker is a figure is applied only when
     it matches a **relayed swing** — one token per swing the DLL queued on that figure (`note_figure_swing`, from the swing queue), used
     once, valid 2 s; every other one is measured and **put back** for 0.35 s (`WO170-FIGUREHIT dropped npc=… by=… why=not-relayed … put
     back`), never skipped (the engine's call wants a cause back — session 1's crash). A body the blow killed is not raised; a baseline
     moves with any TakeDamage of ours (the partner's attributed hits, friendly fire) and the watch ends if another blow lands on that
     victim, so a real blow is never put back. Counters `fig_seen / relayed / dropped / put_back / fatal / cancelled / unread` in the hit
     status line (the line is cut in the pipe's buffer: the counters after `figure_guard=` are not read by the agent yet).
  3. Missile hits by a figure are counted and passed (the partner's arrows are not relayed swings).
* **Crimes:** the host's crime ledger is fed only by the partner's own attributed hits and the mod's events, never by the engine's hit
  of a figure; a stray blow's own witness reaction (the engine's "assault" alarm) is **not** prevented by the put-back (the hook cannot skip
  the engine's call) — prevention is part 1. Not built: a crime-context mark on the victim at hit time (unproven).
* **Why the NPC started it:** `WO170-AGGRO npc=<n> target=figure ghost=<id> reason=<the engine's shout meta> shout=<dialogue>
  last_bark=<dialogue>@<s>s` once per NPC per 10 s, from the engine's own `New dialogue … (CombatShout)` lines; the NPC's last bark is the
  last dialogue the engine started for it. **Distance to the figure is not read** (the agent has no positions).
* **Checks:** **[unit]** native 24 new (the ledger: no swing → dropped, one swing → one blow, 5 swings → 5 blows with 5 strays dropped,
  per-figure tokens, bounded table, expiry; the put-back rules; the automation rules); agent 13 new (the field's shout line parsed to
  tzel_adam / ghost 2 / reason; barks remembered; the config key). **[L]** host role, a villager (`ttkc_man_3`) set on the synthetic
  joiner's figure for 60 s by the joiner's attributed hits (`avatarfight`; the joiner swings nothing):
  * the figure's automation went OFF at its bind and stayed so; when the peer left, `WO170-GUARD … stays OFF` (the field's release);
  * 4 windows (two with the guard off, one on, one with the figure's automation lever "left ON"): **0 `WO170-FIGUREHIT` lines, 0 blows of
    the figure on the villager** — the villager's own blows on the figure (23–49 per 60 s) were measured and sent to the owner exactly as
    before (`WO132-HITS npc hit on avatar … -> to the agent`); the villager's health 100 → 100;
  * **the field's failure did not reproduce, even with the guard off** (0.48.5's behaviour: automation untouched until the combat bit)
    — so these runs do **not** show that the backstop catches anything, nor that the automation was the cause; the `LocalHit`s seen in
    one window were two village apprentices brawling (`rvacka_apprentice_*`, the village's own fight), not the figure.
  * **15 relayed swings (5 + 10) produced no engine hit on the figure's slot** at all (`WO170-FIGUREHIT relayed` 0): the figure's punch
    on air does not reach the hook, so "applied once per swing" is covered by the unit ledger only.
* **Not tested:** a figure blow actually landing (and being put back or applied by a token); the real joiner; a sword in the figure's
  hand; crimes. **Default ON** (the standing rule: new mechanisms ship on with a switch; WO-170's "anything not proven safe ships off"
  would switch off the part 1 that the field timeline points at — the maintainer's call, one console word: `mp_figure_guard off`).

## F3 — riders mount only onto a ready horse [syn][L]

* **Shipped** (`KCD2MP_W170RiderReady`, the WO-164 R2 mount path): the mount call is made only when the horse copy is alive, **both bodies are bound
  to the DLL's writer** (when the writer is healthy), **both have physics** (`GetPhysicalStats().mass`), they are **within 3 m**, and no WO-138
  hold runs; else `WO170-RIDER mount=wait npc=… horse=… why=…` (once per 2 s) and after 20 s `mount=gave-up` (he stays on foot while his stream
  stays on that horse); a mount is `mount=ok`. `why` names the first unmet condition (`horse-physics=none-yet`, `rider-not-bound`, `apart-10m`,
  `w138-hold`, `horse-dead` …). The old "never a ForceMount far away" (80 m) stays. `mp_rider_unit` stays **on** (the check passed).
* **[syn]** (36 checks in `Test-WO170Synthetic`; WO-164's H1 now sets physics): the horse without physics → no mount call, one wait line, again
  after 2 s, given up at 20 s; physics arrives → mounts; 10 m apart → waits, within 3 m → mounts; the rider without physics; a body not bound;
  a WO-138 hold; a dead horse; no DLL writer; **20 spawns in a row** (every other horse pending): 0 mount calls on a pending horse, every ready one
  mounted once, 0 swallowed errors.
* **[L]** joiner role, the synthetic host streams 10 horse + rider pairs near the player (the world's parked `dummyWanderer_horse_1..10`,
  riders absent: 10 stand-ins spawned — the field's setting): every horse copy stayed `physics=none-yet` for the whole run, **100 waits, 10
  gave-up, 0 mount calls, the game alive, no Lua error**. In 0.48.5 each of these would have been mounted at once (the field crash).
  The `mount=ok` path was not reached live (the parked horses never got physics here); it is covered by the synthetic suite.
* Observation (not ours, not fixed): after the harness killed the agent, the DLL's `FAULT-SUM` counted 11,624 (one minute) and later 46,498 faults at
  `wo137::set_send_callback` — the guarded send into a pipe whose agent is gone; counted and logged as designed, noted for a later look.

## F4 — a dialogue's scene keeps its participants awake [syn][unit]

* **Found (WO-167 0.4):** after a dialogue ends the engine fast-forwards its participants behind the fader; our talk end re-paused those copies ~2 s
  after the dialogue, so the fast-forward could not finish (T4 rescued it 20 s later).
* **Shipped, default ON, `mp_scene_hold on|off`** (joiner): the agent counts the engine's `InteractiveSceneManager::EnqueueScene` minus `EndScene` /
  `Interrupt` (WO-167 T4's counters) and tells the Lua on every change (`KCD2MP_W170Scene(n)`); a talk's `dialog-ended` end with a scene running does **not**
  re-pause its copy (`WO170-SCENE hold npc=…`); the copy is paused again when the scene ends (`repause … why=scene-ended`) or after **10 s**
  (`why=10s`); a new talk of that copy drops the hold. T4's 20 s rescue stays as the backstop. Never held: a talk that never started.
* **[syn]** (S1–S7): the pure order; a dialogue ending with its scene still enqueued → no re-pause 3 s and 7 s on, paused again at `EndScene`; no scene → the
  ordinary end; a scene that never ends → paused at 10 s; talked to again; the switch off = 0.48.5.
* **Not tested live:** a real quest-giver scene (the jugglers). If the scene's enqueue line comes after the talk's end, nothing is held (T4 rescues it).

## Gates

| gate | result |
|---|---|
| agent unit tests | **1,370** (0.48.5: 1,357) |
| native tests | **668** (644) |
| Lua synthetic suites | **55** suites, **3,855** checks, 0 failing (new: `Test-WO170Synthetic`, 36) |
| Lua locals test | 6/6 |
| relay / setup / farkle | **63** / **77** / **59**, all passing (unchanged) |
| live (this machine, unattended, throwaway playline4 copy of `OLD/playline4_wo167_after`, minimised, no input) | F1 host role ×3 runs, F2 in the same, F3 joiner role 150 s |
| frame rate | not a clean A/B: the game ran at 23–30 fps (the background regime of WO-167's notes, although minimised) so fps is not comparable to 0.48.5's 73; the DLL's `ours_us_mean` was 663–731 µs in the fight windows (0.48.5's soak: 614–726 µs), 877 µs averaged over everything incl. 10 stand-ins |

## Housekeeping of the live session

Saves backed up before the first launch (828 files, 1,438,666,372 bytes, SHA-256 per file); the throwaway `playline4` was a copy of
`OLD/playline4_wo167_after` and was removed at the end; every save compared **byte-equal** to the backup (0 differ, 828 files). The installed
`Mods\kdcmp` (three files) was backed up and **restored** (hashes equal). Nothing of the maintainer's launcher, agent or relay was running or stopped. The
game was minimised by a watcher at launch (the window was in front for under a second); no key, mouse or game action was injected. Harness:
`tools/wo118/run170.py` (`f1`, `f1b`, `riders`), the synthetic peer's new `jattack` verb.

## The installer (local, unsigned, release candidate)

Fresh clone of `4c9ef42`, the maintainer's three start saves copied in, `tools\Build-Installer.ps1 -SoakWaiver "<the maintainer's 'Skip/waive the soak on this build', 2026-10-10; also the standing rule>"`:
`release\KingdomComeTogether-Setup-0.48.6.exe`, **106,511,495 bytes**, sha256 `020eaf5852b69f2f43a75674f383a3dce58fe1487f27ae0fac09d84b318b4ec7`,
header version **0.48.6** (checked), build gates green (the build's own unit, native and relay round-trip gates; `failed: 0`), published agent and relay
logged no assembly-load failure. Not on GitHub (installers are local only). The Setup was not run from this shell (AppData is redirected here): the
maintainer runs Setup and `Verify-Install`.

## Untested / open

1. A figure blow landing on the host and being put back or licensed by a token (the field's failure did not reproduce in the harness).
2. Whether the automation was the field's cause (inferred from the timeline; the guard on and the lever "left ON" showed no blows either way).
3. The real two-player run of F1, F2, F3, F4 (`docs/TWO-PLAYER-CHECKLIST.md` 232–235).
4. `WO170-AGGRO`'s distance; a crime-context mark on a stray blow's victim.
5. The `mount=ok` path live; a real quest scene for F4.
