# WO-173 — the joiner's blows as real hits in the host's world (0.49.0)

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, on this machine's Modding Tools build (1.5.5), the game minimised the whole time, a throwaway copy of
`OLD/playline4_wo167_after` only, no key / mouse / game input injected (scripted console and Lua calls on the throwaway only); **[syn]** the
synthetic peer or a Lua synthetic suite; **[unit]** the .NET suites; **[native]** `KCDMP_NativeTests`; **[disasm]** read in the game's
machine code this session (`research/WO-162/combat-RE.md` is the design source, cited by section; no address of it is in code); **[not run]**;
**[needs two players]**. The version is the maintainer's (**0.49.0**, given in the WO). The partner was always the synthetic peer; nobody was at
the keyboard. Entity names such as `ttkc_man_5` or `tapo_kasparBandit_6` are the game's own.

## Summary

| Item | Result | Mark | Default |
|---|---|---|---|
| Phase 0 — what existed, how damage is set, what the handler does that the processor does not | answered (below) | [disasm][code] | — |
| **C1** the joiner's blow through the engine (figure Striking, RPGProcessHit, intended-target mark, the reaction tail, health brought to the joiner's measure) | built; 15-min soak 500/500 blows, **0 faults, 0 switched off, 0 applied twice**; reactions queued 13 (victim read Hit 0x40 every time), verdicts **hit only** (no blocking NPC could be staged) | [L][native][unit][syn] | **off** (`mp_real_hits`) |
| C1 fallback: WO-121's raw path, one blow's stamina capped at 35 % of the NPC's maximum | built; 166/166 capped (80 -> 56), **0 knock-outs from one blow**, 0 faults | [L][native][unit] | **on** (always, when the engine path is off or refuses) |
| **C2** the host's outcome to the joiner (W164 RealHit), down / up of the NPCs in a joiner's fight (W164 NpcDown), the copy follows at once | built; host: 540 outcomes and the bandit's knock-out / death sent; joiner: copy down **66-115 ms**, awake **27-64 ms**, on its feet **0.32-0.38 s** after the message (3 cycles) | [L][unit][syn] | **on** |
| C2 the copy's own block for a blocked outcome, the WO-165 recoil on the joiner | not built (logged only) | — | — |
| **C3** what real hits replace | measured; nothing retired (the engine path raises no AI signal; WO-136's threat stays) | [L] | unchanged |
| Gates | agent 1,423 · native 9,305 checks · 58 Lua suites / 3,951 checks · relay 63 · setup 77 · farkle 59 · logpack 14 · guards 7/7; frame +5 % on vs off in the same fight | [unit][native][syn][L] | — |

## Phase 0 — what already existed (static)

**0.1 WO-165's victim-decides replay — reused as is.** `wo165::replay` builds the hit-in (collision details with the victim's entity id as the
repeat-filter key, one 16-byte sub-hit, a fresh record), checks every pointer in the frame (both combat actors, `C_CombatRPG` by RTTI and its
back pointer, the victim's position), writes the attacker's four attack fields (`AttackType / AttackZone / AttackStrength / AttackHandSlot`),
calls `C_CombatRPG::RPGProcessHit` under `fault::guarded`, puts the fields back, and the slot hook's replay window captures the record the engine
built and measures the damage it applied (`kWatchReplay`). On the joiner it never counted a held block because the attacker was a copy that
never enters `Striking` (WO-165 P3). Here the attacker is the joiner's figure, put into `Striking` (0.2), and the victim is a real NPC whose
own state the engine reads — so the WO-165 "victim blocking" referral (flag 0x10) is not used. Two flags were added: `kFlagReact` (the reaction
tail, C1) and `kFlagIntended` (the intended-target mark, C1).

**0.2 WO-166 C3's strike window — reused on the figure.** Pipe op 9 by entity name: at the row's start the combat model's `State` := Striking (8)
and the row's four attack fields are written (plain values, each property block named first), at the row's end the prior state goes back only if
the model still reads Striking. The host now sends it for the joiner's figure (`kcd2mp_<ghost>`) with every relayed Attack row. Two changes for
figures only (`motion::is_avatar_eid`): a figure inside a reaction of its own (an NPC's blow on it: State 0x40 Hit / 0x20 Withdraw) **waits**
inside its window for the reaction to end instead of being refused (never cut off), and its window runs **0.45 s longer**, so it is still
striking when the joiner's measured hit arrives (his game reports it one sampler period after the blow lands, then the network).

**0.3 How damage is set.** The record carries **no amount** (combat-RE Q1.2): the engine computes the blow from the attacker's weapon, skills,
attack type, zone and strength and the victim's armour, subpart and block — here the figure's, not the joiner's. Measured live (punch row,
figure unarmed): health 5.1–15.4 and stamina 15–28 per landed blow against the harness's joiner_said 8 / 20 (or 0.5 / 4). So C1 lets the
engine apply its blow (and decide hit / block / broken, and its stamina and knock-out rules), then brings a **landed hit's health loss to the
joiner's measured value** (`health_correction`: the rest as a TakeDamage with the reaction suppressed, or a raise-back never above the health
before the blow and never on a body the engine killed). Blocked / perfect-blocked blows keep the engine's numbers (the joiner's measure was of an
unblocked blow on his copy). Stamina stays the engine's. Both numbers are logged (`st=<engine>/<joiner> hp=<engine>/<joiner> correction=`).

**0.4 What the joiner sees today.** The host's NPC hit reactions are captured from `C_CombatActorActionHit` (motion.cpp) and sent as NpcHit rows
(ActionKind 15); the joiner plays them on the copy (WO-151 1.1, `mp_npc_reactions`). Knock-outs ride the NPC stream's bit 0x02 → the Lua's
once-a-second WO-135 tick → the native knock-out / wake, then a 1.5 s `Revive(false)`. There is no stagger or knockdown bit; deaths use the dead
bit and WO-172's gate. The joiner's own blow on a copy: copies in a fight hold the "no own hit reaction" contexts (WO-151 1.1), the copy's health
is put back to the host's (FollowHp), its stamina is not, and a copy a local blow ragdolled is stood up (WO-131 2b).

**0.5 Found this WO [disasm]: what the collision handler does that `RPGProcessHit` does not.** `OnCollision` (found by its label) gates on the
attacker's State == Striking and `[combat actor+0x300]+0x30 == 0`, then (a) **sets the attacker model's "intended target" property** (+0x600,
through its setter) when the victim is its opponent or it has none — the hit core's `IsTargetIntentional` facing test is skipped when it is set;
(b) builds the hit-in and calls `RPGProcessHit`; (c) **the reaction tail**: asks the victim's combat actor whether it can react and whether its
current action handles the hit itself, picks a reaction kind from the result struct (+0x44), calls `C_CombatActorCollisionProcess::CreateHitAction`
(which reads nothing of its object but +8, the attacker's combat actor), skips the hand-over only when attacker and victim share a group word,
nobody was killed, the victim's action may not be interrupted and its flag says so, and hands the action to the victim's `C_ActionDirector::SetAction`
(a Framework.dll import); (d) signals listeners. The collision process object itself exists only inside an engine attack action (three owners create
it), so calling `OnCollision` directly would mean building engine objects — not done. (a) and (c) are replicated: every function and offset is
lifted from the handler's bytes at run time (`wo173_rules.h parse_reaction_sites`: four blocks, each exactly once, cross-checked, seven call
targets prologue-checked; census D-582..D-585). (d) is not; the agent's existing brain message (`hitReaction`, WO-121) still goes to the NPC.

## C1 — the joiner's blow as a real hit on the host

**What runs (host, `wo173.cpp`, op 18 of the WO-163 family).** For every attributed hit of a joiner (NpcDamage 0x31 with the attributed flag) the agent
sends the 0x19 body plus the four attack fields of that joiner's last Attack row (fresh within 2.5 s; `Wo173Rules.FieldsFor`). The DLL, on the main
thread, checks in order: `mp_real_hits` on, not faulted, the processor armed, a swing row known, the victim found and alive (WO-172's dead gate),
and **the figure striking** (a WO-166 window written within 2 s). Then `wo165::replay` with the figure as attacker (skip-filter: one message = one
blow), `kFlagIntended` (the intended-target mark around the call) and `kFlagReact` (the reaction tail in the same frame, with the result struct the
processor filled). The engine's outcome decides who applied it: hit / blocked / perfect / broken = **the engine** (the raw step is skipped; only
the victim-side history write, the skirmish add once per engagement and — switch — WO-136's threat run); filtered or the core not run = the raw
path. Every blow therefore goes exactly one way (`after_engine`, unit- and native-tested). 0.25 s later the victim's combat State is read; when the
replay's own watch reports the damage (0.6 s) a landed hit's health is brought to the joiner's measure and one line is written:

`WO173-HIT npc=ttkc_man_3 seq=125 engine=hit st=27.91/4.00 hp=5.14/0.50 joiner_said=st4.00/hp0.50 reaction=queued kind=2 state=0x40 knockdown=no dead=no st_before=129.6/160 correction=-4.64(raised back) fallback=-`

**The fallback.** Any refusal -> WO-121's raw path with one blow's stamina capped at 35 % of the NPC's highest stamina seen this session (at least
100): `fallback=<why>` and `(stamina capped)`. With `mp_real_hits off` (the shipped default) every blow takes this path.

**Live runs [L]** (host role; the synthetic joiner's figure; Henry 30-80 m away; townsmen of Troskowitz and a camp bandit; the punch row):

| run | blows | engine | raw (why) | reactions | knock-outs | faults / dup |
|---|---|---|---|---|---|---|
| probes 1-3 (no intended mark yet) | 28 | 12 hit, damage 0 (`intentional 0`: the figure did not face the victim; distance up to 37 m) | 16 (no window, a first blow before the victim had a combat actor) | 0 | 0 | 0 / 0 |
| probe 4 (intended mark, victim beside the figure) | 12 | 8 hit, hp 5.1-13.5 st 0-28 | 4 | 1 created (skipped by my first, too strict group rule) | 0 | 0 / 0 |
| defend (townsmen who came to defend, in melee) | 33 | 15 hit | 18 (14 no window, 3 dead, 1 first blow) | **2 queued -> State 0x40** | 0 | 0 / 0 |
| camp bandit (it never engaged: idle) | 40 | 12 hit | 28 (27 dead) | 0 (cannot react) | 2, both at stamina 0 | 0 / 0 |
| **soak 15 min, 3 townsmen** | **500** | **257 hit** | 243 (241 no window, 2 first blows) | **13 queued -> State 0x40 (13/13)**, 244 "cannot react" | **0** | **0 / 0**, 0 switched off |
| fallback 5 min, `mp_real_hits off`, stamina 80 per blow | 166 | — | 166 (switch off), **166 capped** (80 -> 56 / 58.3) | — | **0** | 0 / 0 |

The host's agent sent the engine-queued reactions to the joiner on its own: `MP-ACTION section=outbound kind=NpcHit … table=combat_action_hit
spec="CombatHitGen, …"` (captured from `C_CombatActorActionHit` as the host's own reactions always were).

**The WO's checks.**
- *40 blows on a bandit that fights back; blocks/parries at the host's rate:* **not met.** No armed NPC that fights back was reachable unattended:
  the townsmen fight unarmed in the game's combat mode 0 (their model reads Idle while they punch; the engine's own can-react test says no and
  they never block); the camp bandit (`tapo_kasparBandit_6`) stayed idle beside the figure. Verdicts seen: **hit only** (0 blocked, 0 pb, 0 broken).
  The host's own 40 blows need key input (the no-input rule).
- *Every blow applied exactly once:* **met** (779 blows over all runs, each one `WO173-HIT` line, engine or raw, never both; `applied=dup` 0).
- *Hit reactions after a landed blow:* **met where the engine allows it** (every queued reaction was followed by State 0x40 Hit, 15/15); the
  engine refuses a reaction for an NPC outside combat mode, as it would for the host's own blow on it.
- *No knock-out from a single blow at full stamina:* **met** (0 across all runs; the two knock-outs came at stamina 0 after many blows).
- *0 faults:* **met** (the session's only counted faults are the known `wo137::set_send_callback` agent-disconnect scan, recorded since WO-160).
- *500 synthesized hits in 15 min, 0 faults, 0 switched off, 0 dup:* **met** (500 / 0 / 0 / 0).
- *Frame cost within 10 % of 0.48.8:* not a clean A/B against 0.48.8 (one build in the session). Same session, same fight: **819 µs on vs 778 µs
  off (+5 %)**; WO-167 joiner fight 635-640 µs at 73 fps; WO-172 recorded 496-689 µs across its live runs.
- *The bandit's attack rate at the figure >= 70 % of its rate at the host:* not measured (needs the host fighting). The townsmen landed 312 blows on
  the figure in the 15-min soak (0.33 / s).

**Why the switch ships off.** The WO: "the new path ships on only if every check below passes; otherwise the fallback ships on and the new path ships
off with its switch." The block-rate check could not be run, so `mp_real_hits` defaults **off** in the agent, the Lua and the DLL; `mp_real_hits on`
(host console) turns it on. This follows the WO's explicit public-release rule; it differs from the maintainer's standing "new mechanisms ship on"
rule (memory of WO-165) — flagged for the maintainer, one switch either way.

**What the soak taught.** (1) In a 3-on-1 fight the townsmen hit the figure about once a second; the figure's host-side reaction chain (Hit /
Withdraw) then outlasted the whole 0.8 s window in 243 of 540 swings even though the window now waits for it (`figure_waited=257`) — those blows go
the capped raw way (`fallback=no-swing-window`). Writing Striking over the figure's own reaction would raise the engine share; not done (it would cut
an engine action off). (2) Without the intended-target mark the engine judges the blow by the figure's streamed facing (unintended -> no damage, no
reaction). (3) The first blow on a townsman not yet in a fight finds no combat actor (`replay-refused: no-victim-combat-actor`) and goes raw.

## C2 — the outcome on the joiner's screen

**Host.** After each joiner blow the agent sends that joiner `W164 RealHit "<npc> <verdict> <react> <e|r> <hp> <st>"` (kind 7, append-only; the
host's engine outcome, the reaction tail's result, engine or raw). Every NPC that hit a figure (WO-132's 0x9A) or that a joiner hit is watched for 2
minutes; ten times a second the DLL answers op 21 (IsUnconscious / IsDead per name) and every change goes to every joiner as `W164 NpcDown
"<npc> <0|1|2>"` (kind 8): a knock-out again every 3 s while it holds, a death once (WO-172's path still carries deaths). Live: 540 outcomes reached
the synthetic joiner; the camp bandit's knock-out and death were sent (`MP-W173 down npc=tapo_kasparBandit_6 state=knocked-out -> 1 joiner(s)`).

**Joiner.** `WO173-OUTCOME npc=… verdict=… reaction=… by=engine|raw down=yes|no applied_on_copy=…` is logged; the copy is noted in a fight so it holds
WO-151's "no own hit reaction" contexts (the joiner's local reaction on a host NPC is suppressed; the host NPC's own reaction rows play on it). A down
/ up goes straight to WO-135's native knock-out / wake (no wait for the once-a-second tick); an "up" stands the copy up after 0.3 s instead of 1.5 s.

**Live [L]** (joiner role, a synthetic host streaming the copy with the knocked-out bit as a real host does, 3 cycles; an in-game 100 ms timer):

| message | copy follows (from the message's arrival) |
|---|---|
| knocked out | unconscious after **66 / 96 / 115 ms** |
| up | awake after **27 / 54 / 64 ms**, `Revive` (on its feet) after **315-383 ms** |

With the host's 100 ms poll the get-up reaches the joiner's copy in under 0.5 s. `WO173-OUTCOME … verdict=blocked reaction=queued by=engine
applied_on_copy=host-reaction-row` logged for both test outcomes. Not built: a blocked outcome playing the copy's own block row, and the WO-165
recoil on the joiner's player. T-pose after a get-up: not measurable by script.

## C3 — what real hits replace

Counted in the same fight type (three townsmen on the figure), per minute:

| intervention | `mp_real_hits on` (15.8 min, 500 blows) | `off` (5.8 min, 166 blows) |
|---|---|---|
| `WO136-TARGET … why=avatar-hit` | 4 (0.25 / min) | 3 (0.52 / min) |
| `WO136-TARGET` all | 32 (2.0 / min) | 14 (2.4 / min) |
| `WO136-FORCED` (forced target writes) | 7 (0.44 / min) | 5 (0.86 / min) |
| skirmish adds (WO-121) | 3 | 3 |
| `WO136-SWING` | 65 (4.1 / min) | 10 (1.7 / min; no figure strikes) |
| brain `hitReaction` messages | 500 | 166 |

The mod's retargets and forced targets roughly halve per minute with real hits on. **Nothing was turned off**: the engine path raises no AI hit
signal (the collision handler's listener tail is not replicated), so WO-136's avatar-hit threat (`threat`, op 20) and the brain message stay as
the crutch; the townsmen already fought the figure in both runs. `decide()` interventions: no separate line exists beyond `WO136-TARGET`.

## Gates

| gate | result |
|---|---|
| agent unit tests | **1,423** (0.48.8: 1,376; `Wo173Tests` 10) |
| native tests | **9,305 checks, 0 failing** (the runner's own total; `wo173_tests`: the reaction sites on the real bytes, moved / doubled / cut / disagreeing variants, the ladder, the cap, the correction, down / up) |
| Lua synthetic suites | **58** suites, **3,951** checks, 0 failing (new `Test-WO173Synthetic`, 16) |
| relay / setup / farkle / logpack | 63 · 77 · 59 · 14, all passing |
| `Test-NativeGuards` | 7/7 (411 site declarations, 319 guarded calls) |
| live (unattended, minimised, no input) | C1 probes, defend, bandit, 15-min soak, 5-min fallback run (host role); C2 down / up (joiner role); WO-167 joiner fight regression: 68 enemy blows, all shown and applied once, 0 dup |
| frame cost | same fight: 819 µs on / 778 µs off (+5 %); joiner fight 635-640 µs at 73 fps; town before the runs 568-672 µs |

The soak was run (15 min, not waived) for C1; the installer build's own frame-rate soak is waived under the standing rule (below).

## Housekeeping of the live session

- Saves backed up before the launch (826 files, SHA-256 each). The throwaway `playline4` was a copy of `OLD/playline4_wo167_after` and was removed
  at the end; every save compared **byte-equal** to the backup (826 files, 0 differences). Nothing was saved into a real slot.
- The installed `Mods\kdcmp` (three files) was backed up and restored (hashes equal).
- The game was minimised by a watcher for the whole session (four game launches, one per DLL build; a DLL is never injected twice into one game).
  No key, mouse or game input was injected; scripted calls on the throwaway only (teleports of Henry and of the victim NPC, console switches, loads).
- Harness: `tools/wo118/run173.py` (`c1`, `defend`, `soak`, `c2`, `resum`, `status`); synthetic peer verbs `jhit` (a swing and its attributed hit after
  the row's lag) and `ko` (the knocked-out bit on a streamed NPC).

## Untested

- Two real players: everything here (checklist 253-260).
- Blocks, parries, perfect blocks and broken blocks by the engine against the figure: no armed NPC that fights back was reachable unattended
  (the townsmen fight unarmed in the game's combat mode 0 and never block; the camp bandit stayed idle). Verdicts seen: hit only.
- The host's own 40 blows on the same NPC (the block-rate baseline) and the NPC's attack rate at the host: both need key input.
- A blocked / perfect-blocked outcome shown on the joiner's copy as a block (the copy's own block row) and the WO-165 recoil on the joiner: not
  built; the joiner logs the outcome (`WO173-OUTCOME`) and plays the host NPC's own reaction rows as before.
- T-pose after a get-up: not measurable by script; the WO-155 gait pulse is unchanged.

## The installer (local, unsigned, release candidate)

Fresh clone of `3528880` (`release\c0490`), the three start saves copied in (SHA-1 equal to the 0.48.5 record), `tools\Build-Installer.ps1
-SoakWaiver "<the maintainer's standing rule, quoted in the WO-173 run prompt (2026-10-11): 'Ends in a release-candidate installer, always (soak
waived under the standing rule, quoted and dated)'; built unattended 2026-10-11>"`: `release\KingdomComeTogether-Setup-0.49.0.exe`,
**106,815,230 bytes**, sha256 `c5a79e9f3e0a84a69e5c4ac0c82452dead7605c66607c9f38e38cfde5b06455b`, header version **0.49.0** (checked), build gates
green (native 9,305, the installer cases 26/26 and 41/41, no FAIL line), unsigned (no signing settings on this machine). Local only: nothing uploaded.
