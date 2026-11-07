# WO-172 — findings (0.48.8, unattended, 2026-10-10/11)

The 2026-10-10 sessions (all 0.48.6): the maintainer's host session (20:08–22:41), the joiner's two game launches of it
(20:08–21:50, 21:52–22:41), a third player's short join (22:23–22:25) and another pair's host session (19:06–20:22, the comparison).
The evidence was read with a game-time → wall-clock map per log (every Lua line carries `t=`; the agent's `[pos]` lines pair it with
the clock: one constant offset per launch, so every engine line got a clock too).

Marks: **[L]** live on this machine (the Modding Tools build, a throwaway copy of `OLD/playline4_wo167_after`, the game minimised the whole
time, no input), **[S]** synthetic/unit tests, **[F]** field logs, **[–]** not tested.

| item | cause | shipped | default | check |
|---|---|---|---|---|
| WO-171 F1 | a partner's swing near no fighter pulled the nearest *bystander* (2.5 m) into the figure's fight | the bystander fallback removed | — (always) | [S] native build; [–] live |
| WO-171 F2 | no instance in these logs (74 menu closes on the host, 0 partner swings within 1.5 s) | not built | — | — |
| WO-171 F3 | folded into C1 (a corpse first seen dead is marked dead) | C1 | on | [S] |
| W1 | **not found** | `mark_odd` takes the game's own screenshot (`mp_mark_shot`) | on | [L] not reproduced |
| C1 | the death arrived in 0.1 s; the mod then kept working the corpse | the dead gate (`mp_dead_wins`) | on | [L] [S] |
| C2 | no flip-flopping in the data | 4 s hold; with no opponent, the last to land a hit | on (rules) | [S] |
| R1 | the joiner's riding bit is the animation tag the engine cleared; the host's horse copy fell through the ground | `mp_ride_repair` | on | [L] [S] |
| FT1 | WO-167's 60 s restore put the black back, 7 times | never on a timer; lifted after the host's travel | on | [L] [S] |
| UI1 | a stuck fader (most likely) | the game's own HUD restore at travel, scene release, load, `mp_unstuck` | on | [S], [L] call only |
| H1 | the talk ended when the trade screen opened | the trade hold (`mp_trade_hold`) | on | [L] [S] |
| P1 | the old block | the joiner's pickpocket through WO-134's take (`mp_joiner_pickpocket`) | on | [S]; [–] live |

## Order with WO-171

`docs/WO-171-findings.md` does not exist and no WO-171 text is anywhere on this machine: WO-171 has not run. Its F1–F3 were done here
from their titles and this session's evidence (the WO-171 text itself was not available — say if it asked for more):

- **F1 "a swing only threatens whom it hits"** — `wo136::avatar_swing` (host): a partner's swing with no fighting NPC within 4 m fell back
  to *any* NPC within 2.5 m, made it the figure's skirmish opponent and recorded a threat on it. A bystander was pulled into a fight by a
  swing that hit nobody. The fallback is gone: with no fighter in reach the swing threatens nobody (`WO136-SWING … no fighter within 4 m …
  [n=…]`). The real hit path (`note_threat` weight 2) is unchanged.
- **F2 "no punch after a menu"** — no instance in these logs: none of the host's 74 menu closes is followed by a partner swing within 1.5 s,
  and no local attack follows a pause-gate release. **Not built.**
- **F3 "scripted corpses dead on both screens"** — part of C1: a host copy first seen dead (`NPC-DEATH … first seen already dead (by
  puppet)`) is marked dead like an applied death; the field's scripted corpses (`crimeScene_civilian_corpse_9`, `hledaniPsa_deadBody`) were
  already applied dead on the first stream in 0.1 s.

## W1 — no melee weapons visible on the host

**The cause was not found.** What was learned:

- **Ruled out in code** (an agent audited every mod path that touches an attachment, a weapon item, an equipment slot or a render
  setting): the mod sets no `ca_*`/`e_*` setting and calls no `HideAttachment`/`HideAllAttachments`/`DrawSlot`. The brand pak holds one
  texture (the launcher logo). The two table patches only add rows. Stand-in arming and the outfit message (WO-164 N / WO-167 C1–C2) are
  joiner-only (`W137Joiner`) and never ran on the host (only their config line and stats). The rider unit and the scene hold act only on a
  joiner's copies. The host's own fast-travel handling did not run.
- **Ruled out in the logs:** `Missing weapon collision for Dude` is normal engine behaviour (the joiner 8×, the comparison host 8×).
  The `Cutscene` CVar override's "restore before apply" pair appears at the menu load on all four machines; the host never applied the
  override afterwards. Every engine pause the host took was released. No streaming or memory warnings.
- **The one host-only difference:** the host's and the joiner's engine settings are identical (both low spec) except two settings of a
  third-party photo-mode mod installed on the host since 2026-09-29: `wh_photomode_CameraSize = -1` and
  `wh_photomode_MaxDistance = inf`. It is present in every host launch since, including before WO-161 §9.2's first host-only "no weapon
  visible". It may be unrelated. **[L] On this machine, with that mod loaded, weapons rendered:** the game's own screenshot
  (`r_GetScreenShot 1`) works with the window minimised. A villager's spear and the bailiff's son's sword were visible solo, and the sword
  was still visible 88 s into a host session with a synthetic joiner's figure (spawned, rode 30 s, swung 6×). Not reproduced.
- **Shipped for next time:** a mark (`mark_odd`, the menu's "Something's wrong here") also takes the game's own screenshot into its
  screenshots folder (`mp_mark_shot on|off`, default on). The field bug left no trace in any log; a picture at the mark is the evidence.
- **Checks for the maintainer** (the checklist's items 236–237): note the first moment weapons vanish with `mark_odd`; whether a save + reload
  brings them back; whether it recurs with `mp_oneshots off` and `mp_hand_items off` on the host (the WO-143 hand-content gate is the
  only engine-wide hook on what NPCs hold — read-only, it always runs the original); one session with the photo-mode mod moved out of `Mods`.

## C1 — the host's dead enemy alive on the joiner's screen

**[F] Cause.** The death reached the joiner every time, in 0.1 s (`MP-OWNERDEATH … applied=dead … after_s=0.1`; tvez_man_13–17, tvid_man_6,
prepadeniNaCeste_bandit_5, banditLeader_6). The mod then kept working the corpse:
- WO-141 activity applies every 1.5 s on the corpse (`WO141-APPLY … -> ok (exec 1, try 1..8)`), with the held sword asked back into its hand.
  The engine logged `Changing weapon` ×6, then `Changing skeleton for prepadeniNaCeste_banditLeader_6`: the body was re-created standing.
- WO-143 temporary tools (`WO143-HANDS … asking the mod for a temporary one`) every 15 s.
- A WO-136 retarget that put the dead bandit back into the joiner's skirmish (`WO136-TARGET npc=…banditLeader_6 none -> host … asked (leave + re-add)`,
  6 s after its death): "it keeps fighting".
- WO-151's fight set kept "the host's health" on it.

The joiner's marks (22:27:12, 22:36:06) name exactly these corpses (`copy npc=tvez_man_13 … dead=true`, `…banditLeader_6 d=1.6 dead=true`).

**Shipped (`mp_dead_wins`, default on).**
- **Native** (`wo172.cpp`): every activity apply, hand tool, retarget (and its redo), strike window and strike restore asks first. A body whose
  soul reads `IsDead` (cached 0.25 s), or that the mod marked dead in the last 3 s (pipe op 15), is skipped — counted, said once per body
  and kind (`WO172-DEAD npc=… skipped=activity|retarget|strike`). A strike window open on a body that dies ends without its prior state
  going back.
- **Agent:** the host's dead bit marks the copy at once. No row plays on it (`dispatch=dropped-dead`), its WO-132 engagement is released,
  and the DLL is told.
- **Lua:** the death observer tells the agent (`w172_dead`; `w172_alive` when a reload revives it); the copy's talk and scene hold end.

**[L] Live, joiner role:** two copies fight the player with rows every 2 s; one dies mid-swing on the stream.
- Death applied 71 ms after the stream's dead bit.
- The strike window in flight was skipped (`skipped=strike`).
- All 6 later rows were dropped (`dropped-dead`); the living copy kept its 14 rows.
- One row slipped through in the ~50 ms before the Lua's event reached the agent; the agent now marks the copy at the dead bit itself.

**[S]** native `wo172_tests` (8), Lua (D1–D6).

## C2 — who enemies fight, the jitter

**[F] Data.**
- Target switches: at most 8 per minute across all NPCs (22:34). The worst NPC switched host → avatar 3 times in 30 minutes: no
  flip-flopping.
- `MP-FIGHTSNAP` corr p90 in fight windows: the joiner 24 cm (second launch) and 30 cm (first), the host 12 cm, the comparison host 6 cm.

**Shipped (the rules, `wo136_rules.h`).**
- A switch at most every **4 s** (was 3).
- An NPC fighting nobody turns to **the last player to land a hit** within 6 s (was: the larger count). With only swings, the larger count.
- The damage hysteresis is unchanged.

**Not built:** splitting several enemies between two players (the engine picks targets at a fight's start; the mod only arbitrates on
hits), and a jitter change (no synthetic run showed a p90 gain to justify one). **[S]** native rules +5 checks.

## R1 — riding falls apart

**[F] Two causes, one each way.**
1. **The joiner was shown on foot on the host's screen.** At 22:16:20 the joiner's game tried a quick save (refused by the host-only lock).
   At 22:16:26 the engine logged `ForceIdleState clears 'horse' tags in global state of Dude`. The joiner's riding bit is that animation tag
   (`local_state.cpp`), so it read `riding=False` from 22:16:26 to 22:25:47. His positions show horse speed at saddle height, and his own
   mount check kept naming `tvid_horse_1` every 30 s: the host drew a man running fast in the air.
2. **The host's horse sank on the joiner's screen.** The host got off at 22:16:27 (z 31.8). The joiner's copy of the horse, let go by the
   ride writer, fell through the terrain during the 3 s hand-back and was bound "where the rider left it": `anchor=(1089.70,3015.86,-13.40)`.

**Shipped (`mp_ride_repair`, default on).**
- This player's own mount answer (`player.human:IsMounted()`) goes to the agent on a change and every 2 s while mounted. The riding bit sent
  is the tag OR a fresh answer (`WO172-RIDE flag repaired …`).
- Every 2 s, a partner's figure whose stream rides but is not mounted (or the reverse), twice in a row, is mounted (dismounted) again the
  riding start's own way. At most 5 tries per state (`WO172-RIDE remount|dismount|gave-up`).
- A horse body under the terrain (more than 0.5 m) is put on it: at a release (then +1.5 s, +3.5 s), when the stream takes a ridden horse
  back, and at a horse puppet's start (`WO172-RIDE horse ground-snapped …`).

**[L] Live, joiner role:**
- The figure was taken off its horse while the host still rode: `WO172-RIDE remount` 3.7 s later, mounted 0.7 s after that.
- After a load the first mount did not take; remounted 2.7 s later.
- Stops dismounted cleanly.
- The snap was not exercised live: the harness horse is a proxy, removed on dismount. The proxy horse sat about 2 m under the terrain while
  ridden because the synthetic rider streams at ground height, not at saddle height (a harness artifact; a real rider streams saddle
  height).

**[S]** Lua R1–R16, agent riding-bit tests.

## FT1 — the joiner's black screen after a forced travel

**[F] Cause.**
- The quest's travel ran on both machines. The joiner's own was refused 9× by the engine switch (`FastTravel: unable to start … outside
  navmesh!`, `WO114-FASTTRAVEL refused here (engine-refused)`) before the host's travel (20:25:37). The joiner typed the field's way out
  (`wh_ui_FaderSuspend 1`) at 20:29:21.
- In the second launch he typed it **7 times**, each within seconds of WO-167's own `faders restored (wh_ui_FaderSuspend 0) why=60 s after
  the black was lifted`. Setting the suspend back to 0 showed again the fader the game never faded back in.
- **The fader's name is not in any log** (no `Faders are faded out` warning on any machine).

**Shipped.**
- A black lifted the field's way is never put back on a timer: WO-167's 60 s restore now keeps it (`WO172-FT fader kept lifted`). It is
  given back at the next load (the mod's reload reset); a scene the engine really ended still restores at once.
- A quest travel this game refused, followed by the host's travel: 1.5 s after the host's travel ends (or the leash places this player),
  the black is lifted and held until the next load (`WO172-FT fader restored name=quest-travel-refused …`), and the HUD is shown.

**[L] Live, joiner role:**
- The quest's travel was refused here, then the host travelled; lifted 1.5 s after the travel ended (the suspend held at 1).
- The first run found two gaps, both fixed: the refusal came through the agent's own path and returned before the record, and the
  travel-end call was missing.
- The screenshot after showed the game's travel map open, not black. That is WO-167's known parked map after a refused travel (no
  scripted close), already on record.

**[S]** Lua F1–F7.

## UI1 — the lost HUD

**[F]** Nothing hid the HUD by name in the joiner's logs: no HUD setting set, no cutscene state. The HUD is down while a fader is active,
and `wh_ui_FaderSuspend 1` only stops drawing the black, so a stuck fader (FT1) is the likely cause; a reload alone did not clear the
suspend he had typed. **Shipped:** the game's own HUD element is shown (`UIAction.ShowElement("hud", -1)`) after a shared travel, a scene
release, a load (2 s after the reset) and as `mp_unstuck`'s first step (`WO172-HUD restored why=…`). The mod never stores a HUD-off state
(it had none). **[L]** the call runs (`ok=true`, the HUD intact in the screenshots); whether it brings back a HUD the game hid was not
testable here (no hidden HUD to test on).

## H1 — haggling: the trader walks off

**[F] Cause** (the joiner, 21:25, a bartender who trades):
- 21:25:36.2: the shop chat's dialogue ended the moment the trade screen opened (`Dialog ends but no response was played`).
- 21:25:39.5: the mod's deferred end ran (`WO137-TALK end … why=dialog-ended held_s=5.0 -- paused again`). The copy was paused and the
  host's hold released, so the trader went back to her schedule on the host's screen; with the copy paused, the haggle option went.
- The haggle at 21:25:58 started a second forced conversation.
- The game's "fed up with you" after a cancelled haggle is vanilla behaviour: recorded, left alone.

**Shipped (`mp_trade_hold`, default on).**
- A trader's talk (`shop_sellerReadyToSell`, within 4 m) is not ended at the dialogue's end: it is held, the host's NPC and this copy, until
  the trade screen closes. The close is the screen's own UI sound `PlayAudio: ApseClose`, now forwarded by the agent, settled 0.5 s. The
  hold also ends when the player walks away (4 m for 1 s) or after 300 s.
- A cancelled haggle releases nothing (`WO172-TRADE hold|release npc=… why=screen-closed|walked-away|max-time`).

**[L] Live, joiner role:**
- Held 49 s through a scripted shop end and a cancelled haggle; released on the close sound, `w137_talk off` to the host after it.
- The first run showed the agent never forwarded that sound (fixed, plus a test).
- The opening/closing of a real trade screen was stood in by the same log line.

**[S]** Lua H1–H9.

## P1 — the joiner's pickpocket

**Shipped (`mp_joiner_pickpocket`, default on, the standing rule).** A joiner's pickpocket on a host copy runs the game's own minigame on the
copy (his skill, his stealth). Every item it takes is WO-134's take: sent to the host, which takes it out of its NPC's pockets (`WO134-BODY
host-take`), or it is taken back off the joiner ("Someone already took that."). The copy's own list is **never** rewritten: WO-134's list sync
deletes and re-creates every item, which would strip a living body's clothes. Groschen are an inventory item in this game and are diffed
like any item. Players never rob each other (WO-139 Phase 4, checked first). Off = the old block and its message.

**[S]** Lua P1–P6: a take reaches the host; a kept take stays once; a stale one is taken back with the plain message; 20 tries hold
exactly the items the host gave up.

**Not built / untested:**
- The copy stays paused (WO-102) during the minigame, so being caught is likely rarer than in single player.
- A caught attempt's crime is the game's own, on the joiner's copy; whether WO-139 then makes it the avatar's crime in the host's world was
  not tested.
- No live pickpocket: it needs the minigame's input.

## Gates

| gate | result |
|---|---|
| agent unit tests | **1,376** (0.48.6: 1,370) |
| native tests | **682** (668) |
| Lua synthetic suites | **56** suites, **3,913** checks, 0 failing (new `Test-WO172Synthetic`, 58; WO-134 B6 and WO-167 S5 updated for the intended changes) |
| Lua locals / relay / setup / farkle | 6/6 · 63 · 77 · 59, all passing (unchanged) |
| live (unattended, minimised, no input) | C1, R1, FT1, H1 joiner role; W1 host role with screenshots |
| frame rate (minimised, this machine) | 62–78 fps, `ours_us_mean` 496–689 µs across the live runs (0.48.6's WO-170 run: 663–731 µs in fights); not a clean A/B |

## Housekeeping of the live session

- Saves backed up before the launch (826 files, SHA-256 per file). The throwaway `playline4` was a copy of `OLD/playline4_wo167_after` and
  was removed at the end; every save compared **byte-equal** to the backup (0 differ, 826 files).
- The installed `Mods\kdcmp` (three files) was backed up and restored (hashes equal).
- The two manual screenshots were removed from the game's screenshots folder.
- The game was minimised by a watcher at launch (the window was in front for under a second, two minimise calls); no key, mouse or game
  input was injected. Scripted game calls on the throwaway only (teleports, a draw, travel commands, a load).
- Harness: `tools/wo118/run172.py` (`c1`, `r1`, `ft1`, `h1`, `w1`, `shot`).

## The installer (local, unsigned, release candidate)

Fresh clone of `e75111d`, the three start saves copied in (SHA-1 as recorded for 0.48.5), `tools\Build-Installer.ps1 -SoakWaiver "<the
standing rule in the WO-172 run prompt (2026-10-10): 'Ends in a release-candidate installer, always (soak waived under the standing
rule)'; built unattended 2026-10-11, no soak run>"`: `release\KingdomComeTogether-Setup-0.48.8.exe`, **106,519,331 bytes**, sha256
`712743b38d2132c359c5ddb486f9777397cfa90688f253579c8a3b4ba2318ebb`, header version **0.48.8** (checked), build gates green (native 682,
the build's unit and round-trip gates `failed: 0`, 26/26, 41/41), unsigned (no signing settings on this machine). Local only: nothing
uploaded.
