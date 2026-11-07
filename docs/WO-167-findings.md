# WO-167 — 0.48.5, unattended: the last talk/trade gaps, the stuck dialogue scene, stand-ins that T-pose, weapons on copies, swings that really render, shared fast travel

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not
affiliated with or endorsed by Warhorse Studios or PLAION.

Marks: **[L]** live, this machine's Modding Tools build (1.5.5), throwaway playline only, the game minimised, no input;
**[syn]** the synthetic peer or a Lua synthetic suite; **[unit]** the engine-free .NET suites; **[native]** `KCDMP_NativeTests`;
**[L-field]** counted from the testers' logs on disk (no line copied, no player name, address or path written here; entity names
such as `tzel_woman_10` are the game's own); **[disasm]** read in the game's machine code or its shipped strings this session;
**[code]** read in our code; **[game-data]** read in the game's own scripts and UI definitions (Scripts.pak, IPL_GameData.pak);
**[not run]**; **[needs two players]**. Unattended run: nobody at the machine; every open decision takes the WO's stated default,
recorded where it is taken. The version is the maintainer's (**0.48.5**).

This file is written phase by phase: a section is appended when its phase ends.

## Inputs

The two bundles of the WO were on the maintainer's desktop, not in `logs/`; they were copied unchanged (sha1 checked) into the
git-ignored `logs/wo167/zips/` and each unpacked into its own folder (`logs/wo167/joiner-091723/`, `logs/wo167/host-091753/`).
Both machines' clocks agree to the second (the WO's "same clock"); times below are those clocks. The game's `kcd.log` has no wall
clock: its mod lines carry the game's own `t=` and were matched to the agent's and the DLL's timed lines through the events both
print (the talk events, the native holds).

## Phase 0 — locate (static and the logs)

### 0.1 Why a `request-fallback` hit the NPC whose dialogue had just ended (item 2) [L-field][code]

**09:00:17 is the player's own press, swallowed by the old talk's deferred end.** In order, on the joiner:

1. The shop dialogue with `tzel_woman_10` (started at 08:59:53) never played a line: it sat in the engine's `WAITING_FOR_TWINS`
   for 20 s, the engine's `PlayerDialogController::NPCPauseRequests timed out after 20.0 s`, then `Dialog interrupted` / `Dialog
   ends but no response was played` — and the screen was black for those 20 s (`Last fader took 20.08s`). The agent called it
   `ended why=dialog-ended commands=0`.
2. WO-144 3.2 keeps a finished talk open 2.5 s (a minigame may follow). Inside that window the player pressed talk on the same
   woman again (the game's own `OnTalk: Dude->tzel_woman_10` line, then `Soul 'Dude' requested dialog. Assigned id is 184`).
3. Our wrap found the old, ending talk entry for her (`KCD2MP_W137TalkResume` returns an existing entry and only updates its
   `via`), so the press made **no new talk entry**; the request id had nowhere to go.
4. The deferred end then fired: it **paused the copy and reset its Stance/Unstance** — right after the player's request had been
   made to it.
5. The id-less request reached `KCD2MP_W137TalkRequest`; WO-166 T1's "3 s after a dialogue" rule had just run out (2.5 s
   deferral + up to 1 s tick), so the generic fallback attached it to the nearest copy — the same woman — and resumed her 57 ms
   after the pause. The engine never attempted it (`state=request-only`); the WO-164 retry 4 s later (cancel + reissue, id 210)
   never started either; the next talk found `open_requests=2`.

**09:07:56 is a solo request.** `Soul 'Dude' requested dialog` followed at once by `Attempting to start new dialogue … with souls
'Ex: Dude'` — a line of Henry's own (no NPC in it). The fallback attached it to `tzel_woman_10` (the nearest copy), resumed her,
and the agent closed it at once as never-started.

**The fallback's record:** every `request-fallback` talk in the field bundles on disk (this one: 3; WO-166's: 1) — **0 of 4 ever
started a conversation**; each one resumed (and later re-paused) a copy the player was not talking to, or competed with their own
press. The cases it was built for (WO-137: a request that did not come through the wrapped talk actions) did not occur once.

### 0.2 What the joiner's copy keeps after the host's trade (item 3) [L-field][code]

* **The "fight frames" are our own talk hold, not combat.** `MP-FIGHTSNAP fight_frames` counts every frame the copy is "engaged":
  inside a swing hold, a blend, or with the stream's combat bit. WO-166 T3's talk hold (the stream does not move a copy while its
  request waits or its conversation runs) uses the **same hold clock as a swing hold** (`npc_native_hold <npc> 2000`, renewed once a
  second), so every held frame counts as a fight frame. The 121 / 510 / 408 / 386 … windows coincide exactly with the joiner's own
  talks to her (09:06:20.9–09:06:31, 09:06:44.9–09:07:02, 09:07:07–09:07:31, 09:07:56–09:08:03), and the large `blend_max_cm`
  values (150–180 cm) are the writer catching the copy up after each hold. **The stream carried no combat bit for her:** the host's
  state lines for her carry flags 64/0 only (never "drawn" 4 or "engaged" 32), and no combat line exists for her copy.
* **The collision bark was the host's own Henry.** All three `crime_reaction_barks.kolize` barks in the host's log (two for
  `tzel_woman_10`, one for `tzel_david`) fired with the **host's own player 0.65–0.69 m from the NPC** (the host's position line
  against the NPC's last state line); the joiner's figure was about 12 m away at 09:06:13. **0 of 3 were caused by the partner's
  figure.** (The host bumped the shopkeeper while walking up to trade.)
* **"Dialog ends but no response was played … CLEANUP" is ambient:** 258 such lines in the host's log, 42 for her and 44 for
  another serving woman — her own sweeping and muttering monologues. Not a trade trace.
* **During every failed request to her the joiner kept driving the copy with the host's work:** the DLL's WO-141 placement (the
  host's rag / broom, "a tool it does not own was asked for", applied every 1.5–10 s) and WO-143 `Bartender_CleaningTable` shots
  ran on the body while her brain was resumed for the talk, and the engine's NPC context kept reporting it "couldn't reach the
  loaded state" (the rag and the shop's area label). The agent's WO-141 talk flag (`_w141Talking`) only feeds the block tick for rows
  that are not "none", and the DLL's own reconcile and the WO-143 shot never look at it. (The other serving woman had the same
  apply rate and her talks started: this is not shown to be the cause, only what was running.)
* **What our stream sends for a non-combat NPC after a collision bark:** nothing new — the bark is a host-side monologue; no
  combat or drawn bit, no hold.
* The shop context (`shop_sellerReadyToSell`, WO-159) was already set on her copy (`-> already so`) before and after the trade.

### 0.3 What reaches our talk hook and what does not (item 4, the dice player) [L-field][game-data]

* The prompt is the game's: `BasicAIActions:GetActions` offers **Talk** only when `ActorCanTalk` = `actor:CanTalk(player)` and the
  NPC is not in combat mode (an arranged fight excepted); the hint is greyed when the NPC restricts dialogue for the player or the
  player is in combat danger / a tense circumstance. Our wraps sit on `OnTalk / OnChat / OnChatWithFocus / OnChatRequestAccepted /
  OnChatOpen` — they run only after the game offered the action **and** the player pressed it.
* The dice player (`tzel_rowdy_3`): after the dice game ended the mod paused the copy **while its dice table behaviour was still
  playing its end reaction** (the "win" monologue had started 1 s before): the engine logged the reaction's animation action failed
  (`…/(BT)dice_playEndReaction/dice_interrupt_playDiceMinigame…`) and `Node status inconsistency. Can't update` for that node; our
  detach reset his Stance/Unstance 3.7 s later and he stayed `SittingIdle`. **No `OnTalk` to him ever reached the game again** (0
  lines after the dice game): the press never reached our hook because the game offered no Talk. Which of `CanTalk` / combat mode
  refused it is **[not determined]** from the logs — T3's `WO167-NOHOOK` line names it next time.

### 0.4 The stuck scene after the jugglers' quest dialogue (item 5) [L-field][disasm]

* **What starts it:** every conversation enqueues an interactive scene (`InteractiveSceneManager::EnqueueScene`, the dialogue
  camera); the jugglers' `ForceDialogNode` dialogues (`sedni_si_k_nam_1`, then `trialog_zadani_questu`) queued a second one ("added
  into waiting players"). A dialogue's end normally ends its scene at once (`EndScene` / `CleanupScene`, then `Last fader took …`).
* **Why it did not end:** at the trialog's end the game started **NPC scene positioning** (the `DialogIntermission` fader holds the
  screen black while the participants are fast-forwarded to their positions — XGenAIModule: "NPC is in FastForward … This will
  block fader …"). The participants are host copies, and **2 s after the dialogue ended our talk end re-paused them** (WO137 end,
  "paused again") and the stream held them: the fast-forward could not complete. The engine's own line when it finally finished:
  `[ScenePositioningManager] NPC positioning took longer than expected. Finished in 176.04 seconds.`
* **What released it:** not `mp_unstuck` itself. `wh_ui_FaderSuspend 1` only hid the black (`Last fader took 102.99s`), the scene
  kept running ("can't get up"). 1.4 s after the player's `mp_unstuck` the host's world save arrived and the joiner's WO-125
  snapshot asked the game for a QuickSave; the engine then logged `FastForward synchronization is going on while save lock is
  requested … Cancelling FF for all NPCs`, the positioning finished at once, and the game itself ran `EndScene` + `CleanupScene`
  (the player then stood). So **the game's own release is the end of the NPC fast-forward** — by the participants completing it
  (their brains running, the stream off them) or by the save request's cancel.
* The game's own API seen: `C_InteractiveSceneManager::EnqueueScene / EndScene / CleanupScene / Interrupt`,
  `C_ScenePositioningManager::FinishPositioning` (no Lua binding); `wh_ui_FaderSuspend` (a debug cvar: "Use wh_ui_FaderSuspend=1
  to continue playing").

### 0.5 Stand-ins for host spawns (items 7, 8) [L-field][code]

* `WO136-STANDIN soul … -- the host's own spawn` (7 in the bundle: 4 `taboryUCesty_bandit_*`, 3 `prepadeniNaCeste_*`) then
  `WO131-STANDIN spawn … kind=exact`: the body is spawned from **the host's soul and class** (Tables.pak: the archetype is the class),
  then goes through **the ordinary puppet start** of any copy: paused, Stance/Unstance reset, bound to the DLL writer ~150 ms after
  the spawn, gait parked, detached.
* **The WO-155 walk pulse is avatar-only** (`motion.cpp`: `if (b.avatar && !b.nudged)`). A body spawned and bound to the writer at
  once is exactly WO-155's T-pose case (a fresh spawn's locomotion graph does not start until something moves it); an ordinary
  copy was alive in this world before it was bound, a stand-in never was. This is the T-pose's mechanism.
* **Its weapon:** the soul's own default inventory; nothing applies the host's weapon. The drawn state is the stream's flag
  (the host's last state for `prepadeniNaCeste_bandit_7` said drawn), re-asserted every 2 s against the copy's
  `IsWeaponDrawn()` (WO-160: that read lies — true on an unarmed body): **13 `re-asserted sheathed` on that bandit, one
  draw/sheathe flip 62 ms apart, two WO-164 back-offs**. Its copy was never "in a fight" on the joiner (no WO-132/147 engagement),
  so WO-151's in-fight exemption did not apply.
* **`Cannot change sync guard on taboryUCesty_bandit_10 … for slave`** followed **the joiner's own swing** (the mod's `combat swing`
  event the same frame, then the player's own attack shout), not a row we sent; our rows to that bandit around it were plain
  `CombatAttackGen` rows. The one sync row we sent it (`CombatAttackSyncGen, clinch1…`) came 9 s later.

### 0.6 What "a row played" means (items 6, 7) [code][L-field]

* `result=ok` = the DLL's `ghost_swing` built a `C_CombatAnimAction` and `QueueAction` returned. The action's status is read once,
  right after the queue call: **309 of 309 swings on the joiner read status 1 (Pending)**; the DLL then drops its reference. Nothing
  ever reads whether the action was **installed** (status 2, WO-42 §2.4: 0 None, 1 Pending, 2 Installed, 4 Finished) — a fragment
  queued on a body whose animation never starts (the stand-in's T-pose) still counts `result=ok` and the verdict path counts it
  "shown".
* The readable state: keep one reference to the queued action and read its status in the next frames; Installed (2) or Exiting
  (3) seen = the copy's animation started.

### 0.7 Fast travel [code][game-data][disasm]

* **Every way a travel starts:** (1) the player's map pick (ApseMap `OnDoubleClicked(Id)`, `OnHighlightFastTravelPoint(Id,
  Text)`; the travel question's confirm `ApseModalDialog OnQuestionDialogConfirmClicked`); (2) **a quest's forced travel = a
  "fast travel cutscene"** (`CutsceneHandler` with a `FastTravelCutsceneData` holder, e.g. `fast_travel_k_hornikum.xml`; the data
  disables random events; the engine has `EventInitiatedFastTravelData` beside `PlayerInitiatedFastTravelData`, and the console
  `wh_pl_FakeFastTravelTo` "Fake (quest) fast travel"); (3) the console `wh_pl_FastTravelTo x y z | entity`; (4) an interrupted
  travel: `C_FastTravel::Interrupt` ("Interrupting FastTravel…"), e.g. a road encounter ("random event … stopping FastTravel"). The
  engine logs `FastTravel: started…` / `FastTravel: ended…` for all of them; the map UI is told `FastTravelStarted(DestinationId,
  FakeTravel)` and draws the path from its `FastTravelPath` array.
* **What our refusal catches today:** WO-154 sets `wh_pl_FastTravelEnabled 0` on **both** machines while a session is up and the
  host's `mp_fast_travel` is off (the default since 0.45.0); WO-114 sets it 0 on a joiner in the host's world regardless. The cvar is
  the engine's precondition for every start path we could not separate statically; whether `CheckPreconditions` exempts an
  event-initiated (quest) travel is **[not determined]** — so today a quest-forced travel can be refused on either machine, and with
  the cvar at 0 the engine parks the game on the map (WO-114: only `System.Quit()` got out without input).
* **How the host's travel moves the joiner today (WO-114):** the host's `FastTravel: started` holds the leash; 1.5 s after
  `FastTravel: ended` every joiner gets a leash pull beside the host (the WO-124 ground-safe placement); the clock follows through
  the existing time-skip messages (kind FastTravel, the host's world time at the end).
* **How a shared sleep shows the same clock (WO-140):** both machines run the game's **own** skip (`C_SkipTime`, slot 5 id 2 on the
  joiner) for the same hours, so each screen shows the game's own sleep and the host's clock wins at the end. FT2 reuses the
  shape: the joiner runs the game's own travel to the host's destination.
* **A joiner's copy of a quest that forces travel:** with the joiner's cvar at 0 it is refused and parks the joiner on the map;
  with it at 1 it would run a second, independent travel.

## Phase 1 — talking and trading (built)

### T1 — our requests never follow a dialogue [code][syn]
* **`mp_talk_fallback`, default off** (0.1: 0 of 4 field fallbacks ever started a conversation; every one took a copy the player was
  not talking to or swallowed their own press). Off: `KCD2MP_W137TalkRequest` attaches a request no talk action made to nobody; the
  game keeps it (`WO167-TALK fallback-off id=… nearest=…`, once a minute, counted). No case in 0.1 needs it, so it is off for every
  case. On: WO-166's rules plus **never to the NPC whose dialogue with this player ended in the last 10 s** (`why=last-npc`).
* **The press inside the old talk's end window** (the 09:00:17 cause): a press on a copy whose last talk is in WO-144's 2.5 s end
  window ends that talk at once **without its re-pause** and starts a fresh talk (`WO167-TALK restart …`); the request that follows
  attaches to the player's own talk, no fallback needed.
* **The WO-164 retry** (the player's own unanswered press, once) waits until 3 s after any dialogue's end (`WO167-TALK
  retry-held`): its cancel clears every request of the player.
* The forced path (a conversation the game itself starts on a copy, e.g. the haggle) makes no request and is unchanged.
* *Checks:* Lua T1–T15 — off by default and its line, the field's solo request leaves the copy paused, on: the NPC just talked to and
  the 3 s rule, the switch real, the 09:00:17 sequence (a fresh talk, no `wh_ai_PauseNPC` between, the old talk closed for the agent,
  the game's own talk once, the new request attached to it), the retry held and then run once **[syn]**; WO-166's and WO-137's
  fallback scenarios now run with the switch on **[syn]**.

### T2 — a copy the host just used is talkable again [code][syn]
* **Joiner**, at WO-160's release (the host's conversation or trade with that NPC is over): (1) the WO-164 sweep (`trigger=hostconv`,
  released the game's own way, the DLL's loaded state := its body, no cooldown); (2) a combat or alert state on the copy is ended
  **only when the host's NPC is not fighting** (no combat event from it in 10 s): the game's `XGenAIModule.TryEndCombat` and its
  `stopFight` message, and the agent lets go of the WO-132 engagement (`WO167-T2 clear …`); (3) its shop contexts are read into the
  line — the game defines **no trade-in-progress context** (`Libs/Tables/ai/ScriptContext.xml`: only `shop_sellerReadyToSell`,
  `shop_sellerGuardReadyForGossip`, `shopper_buyingProduct`, `shopGuardEnabled`), and `shop_sellerReadyToSell` is the joiner's own
  Trade option (WO-159), so nothing is cleared; (4) the next talk to it within 2 minutes **resumes it first even when the mod has it
  running** (`WO167-T2 fresh-resume …`, the WO-166 200 ms press after). One line: `WO167-T2 npc=… swept=… combat=… danger=…
  host_fighting=… ended=… shop=…`.
* **Host:** every collision bark (`crime_reaction_barks.kolize`) is attributed by distance (`WO167-BUMP npc=… by=host|figure|unknown
  d_host=… d_figure=…`); a bark with a partner's figure within 1.5 m and nearer than the host's Henry is ended at once (the NPC's own
  `InterruptDialogs`): the figure is not this player. The recorded mechanism (0.2): 0 of the field's 3 barks were the figure's — this
  is the guard and the instrument; whether a figure can trigger one at all is the live check.
* *Checks:* Lua H1–H11 (the release sweeps, ends a combat state only when the host's NPC is calm, reads the shop context, resumes the
  next talk first, lapses after 2 minutes; the field's bark by the host's own Henry stands, a figure's is ended) **[syn]**; the bark
  parser **[unit]**.
* The cause of "never attempted" after the trade (0.2) is **[not determined]**; T3's line names the copy's state at the next one.

### T3 — readiness at every timeout and every missing hook [code][syn][unit]
* The timeout line (`WO166-TALK timeout …`) gains, from the Lua: the game's own Talk-prompt rule read on the copy (`prompt=yes|no
  reason=offered|can-talk-false|copy-in-combat|dialog-restricted|player-combat-danger|player-tense`, `combat= danger= can_talk=
  restricted= shop=`), the player's minigame and the copy they face (`target=`); from the agent: the copy's behaviour (**the
  NPC-state unstance the DLL reads**), its NPC-state minigame and **the last behaviour-tree path the engine printed for it**
  (`bt=<subbrain>/<tree>@<age>`, from the engine's own NPCContext / node lines — the Warhorse brain's current node has no Lua read).
* **`WO167-NOHOOK npc=… d=… faced_s=… prompt=… reason=… …`**: the player stood still (moved < 1 m) in front of a copy within 3 m for
  1 s or more and no talk press reached the mod — logged when the focus ends, or once after a 6 s stare; once per NPC per minute;
  never while talking to it. (The dice player would read `prompt=no reason=can-talk-false` or `copy-in-combat`.)
* *Checks:* Lua N1–N6 (the dice player's case, a press that reached the hook, walking past, a long stare with the player's own
  combat danger, the timeout facts) **[syn]**; the tree-path parser on the field's own lines **[unit]**.

### T4 — release a stuck dialogue scene [code][syn][unit] — `mp_scene_release`, default **on**
* **Agent (joiner):** counts the engine's `InteractiveSceneManager::EnqueueScene / EndScene / Interrupt` lines (since the last load)
  and the end of every dialogue with this player; progress = a scene end, a new dialogue with the player, a fader that finished. A
  scene still running **20 s** after the dialogue ended with no progress (no cutscene of WO-151's running) is released **the
  engine's own way found in 0.4: a save request** (WO-151's rescue path: a QuickSave through the WO-125 snapshot path, moved out of
  the playline at once — the engine cancels every NPC's fast-forward and the positioning ends). `WO167-SCENE stuck …`, then
  `ended` or `not-ended` 6 s later. Once per stuck dialogue.
* **Lua:** ended → the faders back to normal (`wh_ui_FaderSuspend` back to 0 if the player set it), the player stood up (the unstuck
  step 1: the character's Stance/Unstance reset), and the line "That scene couldn't play here — your partner's world runs it." Not ended → the
  black is lifted (`wh_ui_FaderSuspend 1`, the field's own way) and given back when the scene ends or after 60 s; stood up, told.
* A normal scene (it ends with its dialogue, or progresses) is never touched.
* *Checks:* the rule: a held intermission released at 20 s, never before, a scene that ends or progresses never, one release per
  stuck dialogue; the scene and fader lines **[unit]**; Lua S1–S5 (faders, stand-up, the message, the lifted black given back)
  **[syn]**. Live: see the live section.

### Phase 1 gates (offline)
Lua: the new suite **52/52**; **all 54 synthetic suites** green (3,768 checks; WO-166's and WO-137's fallback scenarios now switch
the fallback on — the intended change). Agent **1,345** (0.48.2: 1,339). Static: Lua locals, console placeholders — green.

## Phase 2 — combat on the joiner's screen (built)

### C1 — stand-ins are animated, armed figures [code][native][syn][unit]
* **The walk pulse (the T-pose's cause, 0.5):** the mod tells the agent each stand-in body it spawns (`w167_standin`); once its body is
  bound the agent asks the DLL for the WO-155 walk-class pulse (avatar event **kind 3**, appended), which `motion.cpp` now gives a
  **fresh spawn** as well as an avatar (`wo167rules::wants_pulse`; a mark that comes before the bind waits up to 10 s for it).
  `WO167-PULSE npc=… asked=ok`, native `WO167-PULSE` / `WO155-NUDGE … stand-in`, `fresh_pulses=` in the motion status. Ordinary
  copies (alive in this world before their bind) are never pulsed.
* **The archetype:** a stand-in is already spawned from the host's own soul and class (Tables.pak) and goes through the ordinary
  puppet start (0.5); nothing showed it cannot be animated from the soul, so it is not re-spawned from another archetype.
* **The weapon:** C2 below (the host NPC's weapon class from its hand rows, created in the stand-in's inventory when it has none).
  Its clothing stays its soul's own (the host's random-event spawn preset is not on the wire) — **[not built]**, pocketed.
* **Strike windows** (WO-166 C3) open on its swings as on any copy (the row path is the same).
* *Checks:* the pulse rule **[native]**; the spawn's event **[syn]**; live: see the live section.

### C2 — the copy holds what the host's NPC holds [code][syn][unit] — `mp_copy_weapons`, default **on**
* **The source:** the host NPC's hand rows (WO-143's capture: the item class in its right hand) — a class the item tables call a weapon
  (`…Weapon` elements) is "armed with this", an empty hand is "nothing", a tool stays WO-143's. In a fight (the joiner's engagement
  or the host's combat event in the last 10 s) every change is told to the mod at once; and every host attack row played on a copy
  says armed (any `r_<weapon>`) or unarmed (`r_noweapon`), with the class when the hand rows named one.
* **The copy:** `KCD2MP_W167Weapon` — armed: the copy's own item of that class (created in its inventory when it has none, the
  stand-in's case) drawn from the inventory (`DrawFromInventory`, the game's own); class unknown: the game's `DrawWeapon`; nothing in
  hand: holstered. **Once per change, then held** (`WO167-WEAPON …`); the class it held is kept for its next draw.
* **The fight is gone:** with `mp_copy_weapons` on, the 2 s re-assert against the copy's own `IsWeaponDrawn()` (which lies on an
  unarmed body, WO-160) never runs, and while C2 holds a copy's hand (15 s) the stream's drawn-flag transitions do not override it:
  the field's 13 `re-asserted sheathed` on one stand-in cannot happen. Off: 0.48.2's behaviour.
* *Checks:* Lua C2–C8 (the field's stand-in: created, drawn from its inventory; held — no second draw; nothing in hand — holstered
  once; the class kept; unknown class — the game's draw; the switch) **[syn]**; the row tag and weapon-kind rules **[unit]**. The 0.5 s
  follow-through on a live copy: see the live section.

### C3 — "shown" means rendered [code][native][unit] — `mp_swing_watch`, default **on**
* **Native (`combat_construct.cpp`, `wo167_rules.h`):** `ghost_swing` hands its reference to the queued action to a watch instead of
  dropping it; each frame (main thread) the watch reads the action's status (+0x28, WO-42 §2.4) until it reads **Installed /
  Exiting** (the copy's animation started) or **Finished without that / 0.6 s** (it never animated), then drops the reference through
  the action's own virtual. The last result per body is answered over the pipe (WO-163 family op **12**; 13 status, 14 config). Two
  named fault sites (`wo167::watch_status`, `wo167::watch_release`): **any fault switches the watch off for the session**, drops every
  held reference, logs the site, and the agent says it on screen once ("Swing check is off this session (a game error).").
* **Agent:** 200 ms after a row is played on a copy (again at 450 / 700 ms while pending) it asks; installed → shown as before; **never
  animated → the row is taken back out of the played rows and refused `no-anim`**, so the hit's verdict reads `shown=no
  reason=no-anim` and **the WO-163 generic lunge plays before its damage** (`WO167-SHOWN npc=… shown=no reason=no-anim …`). Off, no
  answer, still pending: as 0.48.2.
* *Checks:* the status rule (pending, installed, exiting, finished without install, the 0.6 s limit, a non-status value) **[native]**;
  the verdict rule (`no-anim` → the lunge; a played row shown; other refusals unchanged), the played log's take-back **[unit]**. Live:
  a row on a body that cannot animate, and on a normal copy — see the live section.

### C4 — paired attacks on stand-ins [code][unit] — `mp_sync_fallback`, default **on**
* A paired (sync) row (`CombatAttackSyncGen …`) for a **stand-in**, or for an NPC the engine said has no sync data (its `Cannot change
  sync guard on <npc> … for slave` line, counted), is played as **the plain attack row of the same right-hand weapon**
  (`ActionRowCatalog.PlainForSync`: a `CombatAttackGen` row of the attack table — the same attack type, strength, zone, left hand
  scored; never a lying / sitting / mounted row; the same choice on every machine). `WO167-SYNCFALLBACK …`.
* The engine's sync-guard line is now counted against our rows (`WO167-SYNCGUARD npc=… followed_row=yes|no`): the field's one line
  followed the joiner's own swing (0.5), `followed_row=no`.
* *Checks:* the fallback picks the same-weapon plain row and never another weapon's, a lying row or a kick; none fits → the sync row
  stays; the line parser **[unit]**.

### C5 — kept
WO-166 C2 (the host's word for an unrowed blow), C3 `mp_copy_strikes` and C4 `mp_snap_fix` stay on; `mp_victim_decides` stays off.

### Phase 2 gates (offline)
Native **644** (0.48.2: 625); agent **1,350**; Lua: the WO-167 suite **62**; all 54 synthetic suites green (3,778 checks); the native
guard check clean.

## Phase 3 — one shared fast travel (built)

### FT1 — on by default; a quest's travel is never refused by us [code][syn][unit] — `mp_fast_travel`, default **on**
* **The default** is on (the maintainer, 2026-10-10): WO-154's menu row, the agent's `FastTravelDefault`, the mod's session default.
  The host can still turn it off in the mod menu (Insert).
* **Off no longer blocks the game's own travel:** WO-154 held `wh_pl_FastTravelEnabled 0` for the whole session when off — which
  would also refuse a quest's forced travel (0.7). Now "off" holds the engine's switch **only while this player picks on the travel
  map** (`KCD2MP_W167FtMapWindow`: 30 s after a highlighted travel point, the map's double click or the travel question's confirm,
  seen through the map's own element events), and gives it back otherwise. A quest's forced travel (a fast-travel cutscene) goes
  through on the host whatever the setting.
* **A joiner's own engine travel** stays switched off in the host's world (WO-114): its picks are asked of the host (FT2), and its copy
  of a quest's travel is not run there — the host's travel brings it along. Whether the engine's switch also refuses a quest's
  (event-initiated) travel, and what the game does then, is the live probe below; the decision rule: if a refused quest travel
  leaves the joiner's game stuck on the map, the joiner's switch is held only while it picks, like the host's.

### FT2 — one travel for both, no vote [code][syn][unit]
* **Wire:** W164 kind **6 Travel** (appended; WO-164's message family): `r <seq> <dest>` joiner → host (a pick, asked);
  `n <seq> <why>` host → joiner (the host's world can't travel: nothing moves); `s <seq> <dest>` host → joiners (the travel started:
  the host's own pick, a joiner's, or a quest's with `-`); `e <seq> <x,y,z> <0|1>` host → joiners (it ended there; 1 = interrupted).
  A destination is a world position `x,y,z` (`-` = not known; `@name` parsed for a named entity). Leash flag **0x0400 travelling**
  appended (the joiner's own run of the shared travel is under way: the host's pull waits for it).
* **The pick:** the map's `OnHighlightFastTravelPoint(Id, Text)`, `OnDoubleClicked(Id)` and the travel question's
  `OnQuestionDialogConfirmClicked` (which names nothing: the point last highlighted) are listened to through the game's own element
  listener. The map declares its point id as a plain string (the map element's definition) and the handler keeps it as one; its form
  is not documented, so `KCD2MP_W167FtDest` resolves it as an entity's text GUID (`System.GetEntityByTextGUID`) or an entity name to
  that entity's **world position** — the console travel takes a position on both machines — and anything else to `-`. Every pick
  logs the raw id (`WO167-FT pick id=… dest=…`), so the first two-player run shows the form. **[needs two players]**
* **A joiner's pick:** sent to the host (`r`). The host checks its own world (off, already travelling, an unknown destination, no
  player, dead, in a dialogue, in combat, in a minigame — `KCD2MP_W167FtCanTravel`), then runs the engine's own travel there
  (`wh_pl_FastTravelTo x y z`) and waits 6 s for `FastTravel: started`; the engine's own refusals (over-encumbered, outside the
  navmesh …) come back as `not-started`. Any refusal: the joiner reads **"Your partner can't travel right now."** and nothing moves.
* **The host's travel (any start — its own pick, a joiner's, a quest's, the console):** at `FastTravel: started` the host tells every
  joiner (`s`, with the destination when a pick named one within 15 s, else `-`). The joiner lets go of its own switch for this one
  travel (WO-114's 5 s re-assert waits while the run lasts, 3 min at most) and runs **the game's own travel to the same destination**:
  its own travel map, route and fades for the same route, so both screens show the travel for about the same time. Its clock jumps
  with its own run and is the host's at the end (the WO-104 / WO-140 time sync: the host's clock wins). When both runs have ended, the
  WO-114 pull places the joiner beside the host (the WO-124 ground-safe placement); the pull waits while the joiner's flag says
  travelling.
* **No vote, no prompt** on either side; the partner reads **"Travelling with your partner…"** (FT4).
* *Checks:* the wire texts and their limits, the leash flag and the pull waiting for the joiner's run **[unit]**; the pick → the
  agent, the map window hold, the destination forms (name, GUID, unknown, the origin), the host's rules, the host's travel for a
  pick, the joiner's run with its switch let go, a blind start, the refusal, the texts **[syn]**.

### FT3 — an interrupted travel [code][unit]
* The engine's `Interrupting FastTravel…` line on the host marks the travel interrupted; the end message carries `1` and the stop
  position, and the joiner is placed beside the host where it stopped (the same pull). The mod has no way to stop the joiner's own run
  midway (the engine's console has travel commands but no stop): it finishes first, then the pull brings the joiner back to the host's
  stop. **Limit, recorded.** A road encounter that stops the joiner's own run ends the same way: the pull places it beside the host.

### FT4 — the texts [syn]
* "Travelling with your partner…" on the partner's screen (both directions: the joiner when the host travels; the host when it travels
  for the joiner's pick).
* The off message (WO-166 F1) only when the host turned travel off **and** a player picks; a joiner's own engine refusal in the host's
  world says nothing when travel is on (the pick is asked of the host) — never "Only the host can fast travel" while travel is on.

### Phase 3 known limits
* The map id's form is unverified (above): an id that resolves to nothing makes a joiner's pick refused (`unknown-destination`) and a
  host's travel "blind" for the joiner (told, then placed beside the host on arrival — the 0.48.2 behaviour).
* A quest's travel on the host is blind for the joiner by construction (the quest's travel point is not on the map's pick path): told,
  then placed beside the host on arrival.
* The joiner's run is the joiner's own engine travel: when it takes a different time than the host's (its own encounters, its own
  streaming), the host's travel ends first or last and the pull evens it out; the two screens are not frame-locked.

### Phase 3 gates (offline)
Agent **1,354**; Lua: the WO-167 suite **87**, the WO-154 suite **229** (fast travel defaults on; the map-window hold); all 54
synthetic suites green.

## Live — unattended, this machine

**Setup.** The Modding Tools build (1.5.5); the throwaway `playline4` (a copy of `OLD/playline4_wo166_after`), `quicksave022`
loaded before every run and the loaded-save line checked each time. Before the first launch the whole saves folder was copied
(805 files, byte-equal by SHA-256). The installed `Mods\kdcmp` (three files) was copied with its hashes and the new pak put in its
place. The game ran minimised the whole time: a watcher minimised its window the moment it existed (at each of the two launches the
new window was in front for under 0.1 s before that, twice; nobody was at the machine and no input was sent), and the window was
checked minimised and not in front before the long runs. No key, mouse or game action was injected; the checks drive the mod's
own entry points, the engine's console (travel, cvars) and the engine's own log-line shapes. One UI call was tried once to close a
travel map the probes had left open (`UIAction.CallFunction("ApseMap", -1, "Deactivate")`, no effect; the game was restarted
instead). The synthetic peer and the relay on the loopback, the peer as the host (joiner role) or as the joiner (host role). None
of the maintainer's launcher, agent or relay was running; none was stopped. Every native DLL a new copy (`KCDMP_w167a.dll`; no
native change after it).

**Five game sessions.** The first ended frozen: a host-role travel was refused by the engine after it had opened its travel map
(the joiner run before it had left WO-114's hold at 0 when its agent was stopped), the open map runs the world at 1/1000 (the
menus' `wh_ui_ApsePauseRatio`; a real session sets it to 1, WO-138), and every later check of that session timed out; its results
are kept only where they came before the freeze (the solo probes, the first joiner-role travel). The second ran the checks again and
ended with the probe of a quest's travel under the engine's switch at 0 (it parked the map). The third ran the final build's
checks and two soaks. The fourth and fifth were fresh launches for the frame-rate comparison (0.48.2, then 0.48.5) and the final
soak. Each one: launched, minimised, the DLL injected, the throwaway save loaded and its load line checked.

### What ran

| item | what ran | result | mark |
|---|---|---|---|
| FT2 host | host role, a synthetic joiner: its pick `r 1 x,y,z`; `r 2 -`; `mp_fast_travel off` then `r 3`; this game's own console quest-style travel (`wh_pl_FakeFastTravelTo`); a console travel along the probes' route with road events on | the pick: `can-travel ok`, the engine's travel there, `s 1 x,y,z` then `e 1 …` to the joiner, "Travelling with your partner…" on this screen; `n 2 unknown-destination`; `n 3 off`; the quest-style travel told as `s 2 -` (blind) and `e 2`; the ambush stopped the travel at the event place: `INTERRUPTED by the game (a road encounter)`, `e 3 <the stop> 1`; the character put back by the harness (an ambush kills an unattended character in seconds) | [L] |
| FT2 joiner | joiner role, a synthetic host: `s 1 x,y,z`; `e 1`; `s 2 -`; `n 7 combat`; two picks through the mod's own pick entry (a resolvable entity name, a meaningless id); then a quest-style travel on this machine (no pick, no host message) | the switch 1 (armed, not held) before; for the shared run the hold let go and the same engine travel ran to the host's destination (the map and route here, landed exactly on it, the clock +~2,750 game-s like a real travel), the hold back on after; the blind start told only; "Your partner can't travel right now."; `r 1 <the entity's position>` and `r 2 -` at the host; the quest-style travel **ran** (not refused, no map left open) and said nothing to the player | [L] |
| FT3 | the road event above (host role) | `stopping FastTravel: 'Y'` counted as an interruption; the joiner told the stop position (`e … 1`) | [L] |
| FT4 | the texts in both roles | "Travelling with your partner…" on the partner's screen; the off message only for a pick while the host's travel is off ([syn] WO-154 / WO-167 F-c) | [L] / [syn] |
| T1 | joiner role, two copies within reach, the player facing one; the mod's own request entry driven as the game drives it | off by default: `WO167-TALK fallback-off id=9184 nearest=…`; on: `suppressed … why=after-dialogue` at once, `why=last-npc` 5 s and 8 s after (the console calls queued; the 10 s window had not ended); the "allowed after the windows" case stays [syn] | [L] / [syn] |
| T2 | joiner role: the synthetic host talks to a copy 12 s and finishes | `WO160-CONV copy-held` → `copy-released` → `WO167-T2 npc=… swept=true combat=false danger=false host_fighting=false ended=none shop=-`; 2.5 s later the copy reads exactly as an untouched copy. These ambient villagers have no dialogue for the player at all (`can_talk=0` on every copy, held or not, before and after), so "talkable within 2 s" needs a real conversation partner | [L] · [needs two players] |
| T3 | the same run: the player stood facing each copy (2 m) and pressed nothing | `WO167-NOHOOK npc=… d=1.8 faced_s=6.0 prompt=no reason=can-talk-false … paused=true` for both copies; the timeout line's readiness tail [syn] | [L] / [syn] |
| T4 | joiner role: a normal scene (queued, the player's conversation ended, the scene ended 6 s later), then a stuck one (queued, the conversation ended, no end) -- in the engine's own line shapes | the normal scene untouched; the stuck one released at 20–21 s: the rescue save request was skipped by WO-151 ("the world is not known": the harness joiner has no host world), the black lifted (`wh_ui_FaderSuspend 1`), the player stood up (`wh_ai_NPCStateResetElement` Stance/Unstance), told; the faders given back 60 s later. The engine ending a real scene after the save request: [needs two players] | [L] |
| C1 | joiner role: a host road-event bandit (a name this world never spawned) joins a fight; in the soaks a new one every 5 min, six per run | `WO136-STANDIN soul … kind=exact` (its own soul), `WO167-PULSE … asked=ok`, native `WO167-PULSE body=…: a fresh spawn -- the WO-155 walk-class pulse starts its locomotion graph`; every stand-in's swings seen installed by the swing watch (it animated) | [L] |
| C2 | the same fights: the host's longsword in the hand rows, armed rows, then empty hands | each fighter: `WO167-WEAPON … owned=created drawn=this-class` once, held (no second draw), `hand=- -> holstered` once; **0** `re-asserted sheathed` lines. The first soak showed a stale "drawn" mark from an earlier session (fixed: below) | [L] |
| C3 | 1,199 + 79 swings watched; a mounted row and paired rows on a standing copy | in the short runs every swing installed (even the mounted and paired rows: the engine plays a fallback fragment); in the first soak 514 swings of two unarmed copies never animated and the verdicts did not wait for the answer (fixed: below); the second soak: 1,199 blows: 1,194 shown with their swing, 5 with the lunge, 0 applied twice; 9 swings never animated (all on stand-ins, at the instant a paired row and a regular row came together) -- 4 of them were still judged shown (fixed: below); after the fix a 6-minute run of the same shape: 4 unanimated swings of 4 came with the lunge, 229 shown, the verdict's wait 73 ms on average (at most 0.75 s); the final soak: 1,199 blows: 1,191 shown with their swing, 8 with the lunge -- exactly the 8 swings that never animated (all on stand-ins, at a paired row's instant), 0 applied twice; the verdicts waited 21 ms on average, at most 0.6 s | [L] |
| C4 | paired rows on a stand-in (fallback on) and on an ordinary copy (fallback off) | the stand-in's: `WO167-SYNCFALLBACK … plain=… why=stand-in`, played as the plain longsword attack; the ordinary copy's paired row installed here and the engine printed no sync-guard line | [L] |

### Fast travel: the engine's own behaviour (probes, solo, the throwaway save)
* **A console travel is the game's own travel.** `wh_pl_FastTravelTo x y z` opened the travel map (`PlayAudio: ApseOpen`), logged
  `FastTravel: started…` / `ended…`, walked the route in about 5 s (the window minimised) and landed exactly on the point; the clock
  moved about 2,770 game-seconds for 520 m — the same as the map's own travel. `wh_pl_FakeFastTravelTo` (the quest's "fake"
  travel) prints the same lines and shows the map, but moves the clock only about 94 game-seconds.
* **Road events.** On the probes' route, with this game's random events on, an ambush (`prepadeniNaCeste`, variant
  `ambush_npc_man`) stopped the travel at its event place in 2 of 2 runs — once killing the unattended character within seconds
  (Game Over; the throwaway save was loaded again; afterwards the probes put the character back at once). It prints
  `FastTravel: random event variant … stopping FastTravel: 'Y'` and **no** `Interrupting FastTravel` (FT3 now counts both). With
  the joiner's session setting (`wh_pl_RandomEventsAutoSpawnEnabled 0`, WO-164) the same route rolled no event in 3 of 3 runs: a
  joiner's run of the shared travel does not get a local ambush.
* **The engine's switch at 0 refuses every travel, the quest's too.** With `wh_pl_FastTravelEnabled 0` both `wh_pl_FastTravelTo`
  and `wh_pl_FakeFastTravelTo` are refused — the line reads `FastTravel: unable to start fast travel from/to outside navmesh!`
  (misleading; WO-114 saw the same) and repeats every frame — **after** the engine has opened its travel map, which then stays open.
  Solo, an open map runs the world at 1/1000 (the menus' `wh_ui_ApsePauseRatio`), so the game is parked there; in a session the
  WO-138 lever keeps the world running, but the player still sits on an open map. Hence FT1's change on the joiner (below) and
  the host's can-travel check of its own switch.
* **The map's point id.** The map element declares it a plain string (`OnHighlightFastTravelPoint(Id, Text)`, `OnDoubleClicked(Id)`,
  `FastTravelStarted(DestinationId, FakeTravel)`); its POI markers carry map X/Y only. `System.GetEntityByTextGUID` resolves an
  entity's text GUID in the plain `8-4-4-4-12` form (checked live on the player's own GUID; braces fail). The id's actual form
  is still unseen: every pick logs it raw. The map's `FastTravelPath` array read empty through `UIAction.GetArray` during a travel.

### Found live and fixed (committed before the final runs)
* **FT3:** a road event's stop (`… stopping FastTravel: 'Y'`) counts as an interrupted travel (it was only `Interrupting
  FastTravel`).
* **FT2:** a joiner's pick and the joiner's run of the shared travel are handled off the relay's reader loop (their waits -- up to 3 s
  for the mod's answer, 6 s for the engine's start -- had run inside it: pings of 2–3 s while it waited).
* **FT2:** the host's can-travel check refuses while its own engine switch is 0 (`switch-off`): the engine would open its map and
  then refuse. A travel this mod asked for that never begins tells that player "That travel couldn't start here — close the map to
  go on."
* **FT1 (the joiner):** WO-114's joiner hold is **armed** in the host's world and holds the engine's switch at 0 only while the
  joiner picks on the travel map (30 s after a highlighted point, the double click or the travel question); the draw loop gives it
  back when that window ends, even with no agent left (a stopped agent used to leave the switch at 0 until a restart). A quest's
  own forced travel on the joiner now runs (live: it ran and landed; before, it was refused and the map stayed open), and nobody is
  told "Only the host can fast travel" for it.
* **The joiner's own run** no longer reads as the developer fly mode; the hold's release names the shared run.
* **C3:** a verdict whose swing is still watched waits for the watch's answer (at most 750 ms after the swing, off the reader
  loop). In the first soak every verdict came 220 ms after its swing, before the watch's ~600 ms answer, so 514 swings that never
  animated were still judged shown and no lunge played.
* **C2:** a copy's held weapon is forgotten for a new body or puppet record under the same name and at every fresh session. In the
  first soak two copies kept a "drawn" mark from an earlier session, their hand rows were taken as "held already", and they swung
  longsword rows with their own halberd and axe — the 514 unanimated swings above.
* **No silent guards:** every WO-167 catch in the agent and every engine action in the Lua counts the fault, logs its site,
  switches its feature off for the session and says so on screen once (`WO167-FAULT site=… feature=…`, `w167_fault` keeps both
  halves in step). Live: 0 faults in every run.
* **C3 (2):** a verdict waits for every open watch of that copy's swings, not only the newest, and the wait's cap is 1,000 ms after the
  swing: in the second soak two rows queued on one stand-in in the same instant both never animated, and the verdict resumed on the later
  answer -- or on the 750 ms cap, which ran out just before the watch's last look came back -- with the earlier row still counted as
  played. Re-checked live (above).
* **FT1 (2):** a quest's own travel on the joiner is no longer told "Only the host can fast travel" nor flagged to the host as a refused
  travel (it was WO-114's message for a travel past its old, always-held block).

### Frame rate

0.48.2 (the maintainer's installed files, byte-identical to the 0.48.2 build; its DLL, agent and relay) and 0.48.5 measured today on
this machine, each in a fresh game session, the window held minimised the whole time (a watcher re-minimised it whenever the game
restored it). The DLL's `FRAME` line every 60 s; the first window after the DLL's start and the load's own window left out.

| | 0.48.2 | 0.48.5 | frame rate | `ours_us_mean` |
|---|---|---|---|---|
| main menu, before a load | 76.6 / 77.7 fps, 377 / 374 µs | 71.3 / 75.3 fps, 401 / 390 µs | −5 % | +5 % |
| town, idle, no session | 71.1 / 71.1 fps, 571 / 564 µs | 73.0 / 72.9 / 73.3 fps, 631 / 632 / 628 µs | +3 % | +11 % |
| three enemies fighting the joiner (a swing every 1.5 s) | 6 min: 65.8–70.4 fps (mean 68.2), 614–621 µs (mean 618) | the final soak: 30 min: 70.0–76.5 fps (mean 73.6), 614–726 µs (mean 656) | +8 % | +6 % |

**Within 10 % everywhere.** The mod's own cost per frame moves about as much between two sessions of the same build (the same 0.48.5
DLL read 534–560 µs town-idle this morning, 628–681 µs this afternoon -- 659–681 µs while a second process-polling watcher ran);
the frame rate is the gate. Today's minimised game ran far faster than WO-166's (73 vs 26.5 fps in town): the frame-rate regime
changes with the window's state (minimised ~73 fps; shown behind other windows ~25 fps, the game's background limiter), which is why
both builds were measured minimised.

### The 30-minute runs (joiner role: two copies fighting the whole time, a new road-event stand-in every 5 min, C1–C3 on)

| run | blows | shown with their swing | with the lunge | applied twice | faults / switched off | stand-ins pulsed | notes |
|---|---|---|---|---|---|---|---|
| 1 (the Phase-3 build + live fixes 1) | 1,199 | 1,199 judged shown | 0 | 0 | 0 / 0 | 6 | the watch: 514 swings of two copies never animated (a stale C2 mark: unarmed copies) and the verdicts did not wait -- **failed** "every swing rendered or lunged"; fixed |
| 2 (+ live fixes 2) | 1,199 | 1,194 | 5 | 0 | 0 / 0 | 6 | 9 swings never animated (two rows on one stand-in in one instant); 4 still judged shown -- fixed (live fixes 3); a 6-minute re-check: 4 of 4 lunged |
| 3 (+ live fixes 3), an aged session | 410 when stopped | 70 | 340 | 0 | 0 / 0 | 3 | **stopped**: after ~90 min of bandit fights in one village, a villager the engine had just re-created (not yet parked) and others joined the stand-in's skirmish; stray engine blows -- not the host's -- killed the joiner (WO-113's grave and wake 345 m away), the fight disengaged and every swing after that was unanimated |
| **final** (this build, a fresh session) | 1,199 | 1,191 | 8 | 0 | 0 / 0 | 6 | 8 swings never animated (stand-ins, at a paired row's instant): **8 lunged**; every stand-in and both copies armed once (created, drawn), 0 `re-asserted`; no stray damage, no death; no fault of any site in the session. **Passes**: 0 faults, 0 switched off, 0 applied twice, every swing rendered or lunged |

### Faults

WO-167's own sites: **0** in every run (`WO167-FAULT` never written; `faults=0 off=-` in every status line). The DLL's fault total
counts WO-137's disconnect scan (pre-existing, WO-166's pocket): about 11,750 guarded reads per agent disconnect at one site, never new
during a run, never switched off. No crash in any of the four game sessions.

### Gates (final, this build)

Agent **1,357** (0.48.2: 1,339); setup **77** (77); Farkle **59** (59); relay **63** (63); native **644** (625); synthetic suites **54**
(53), 3,819 checks, all green (WO-167's own suite 98, WO-114's 65); the native guard check 7/7. Live as above; the final 30-minute run
passes on this build.

## Build (the release candidate)

A fresh clone of `e1d0261` (git-ignored start saves copied in; byte-identical to 0.48.2's), then
`tools\Build-Installer.ps1 -SoakWaiver "The maintainer's standing rule, stated in WO-161 on 2026-10-08 and applied to WO-167 on
2026-10-10: release candidates are built without the soak test (0.48.5)."` -- every build gate green inside the clone (relay 63, agent
1,357, setup 77, the Lua and payload checks). `release\KingdomComeTogether-Setup-0.48.5.exe`: **106,510,928 bytes**, SHA-256
`ad3a35ca4782d709a932296fb5a0e733ed9d8ad12222868825ba34b76f211219`, version resource 0.48.5, **unsigned** (no signing settings). The
waiver is recorded beside it (`release\SOAK-WAIVED-0.48.5.txt`). Local only; never uploaded.

## Pocket (not done here; for the maintainer)

1. **The travel map's point id** (FT2): its form is unseen; picks resolve a text GUID or an entity name to a position, anything else
   is refused as `unknown-destination` (and the host's own pick then reaches the joiner as a blind start). The first two-player
   run's `WO167-FT pick id=…` line settles it; a table from the map's own data (the POI markers' X/Y) is the fallback if it is a
   bare marker number.
2. **A refused travel leaves the engine's map open** (both roles): the engine opens its travel map before its own checks; no
   scripted close exists (`UIAction.CallFunction("ApseMap", -1, "Deactivate")` does nothing; WO-114 found only `System.Quit()`).
   The player is told to close it. A pre-check of the navmesh or the load the engine checks would avoid it.
3. **The joiner's run cannot be stopped midway** (FT3): an interrupted host travel is followed by the pull only after the
   joiner's own run has ended; a different route time on the two machines is evened out by the pull, not frame-locked.
4. **A quest's forced travel on the joiner now runs there** (FT1 armed hold): it lands where the host's same quest travel lands and
   the leash evens out any difference; the WO's "not run there" was traded for "never refused" (the refusal parks the joiner on an
   open map). A quest that forces a travel on only one machine is not known to exist; if it does, the pull brings the joiner back.
5. **T2's "talkable within 2 s"** needs a person with a dialogue: the harness's copies (ambient villagers) read `can_talk=0`
   held or not. The two-player checklist item 225 is the proof.
6. **T4's engine end of a real stuck scene** after the save request: the harness joiner has no host world, so WO-151 skipped the
   rescue save; the black-lift / stand-up / tell path ran live.
7. **Stand-in clothing** (C1): a stand-in wears its soul's own clothes; the host's random-event spawn preset is not on the wire.
8. **The WO-137 disconnect scan** (pre-existing, WO-166's pocket): every agent disconnect counts ~11,750 guarded reads at one
   WO-137 site (`wo137::set_send_callback`, mislabelled); the harness's many agent restarts took one session's count to 83,020 —
   none new during any soak, none switched off.
9. **The harness's agent start**: the agent falls back to HTTP (no mod events) when no state frame comes within 6 s — the mod
   writes frames only when the player moves (a heartbeat every ~10 s). The launcher starts the agent at the main menu (another
   path), so not a field issue; the harness steps the character half a metre first.
10. **A pre-existing silent catch** in WO-114's `Wo114SetFastTravelBlockAsync` (`catch { }`) — not WO-167's; left as it was.
11. **Hostile stand-ins near villagers in an aged session** (the third soak): after ~90 minutes of bandit stand-ins fighting in one
    village, a villager the engine had just re-created (the guard re-parks a re-created body on its next 1 s sweep) and other
    villagers joined the stand-in's skirmish, and their engine blows -- not the host's -- killed the joiner. Never seen in a fresh
    session (the first, second and final soaks). Leads: park a body at its creation line (`Connected to the soul NPC was created
    <name>`), and keep a stand-in out of the joiner's own skirmishes.
12. **The frame-rate regime** follows the window: minimised ~73 fps, shown behind other windows ~25 fps (the game restores its own
    window now and then); measure minimised with a watcher.
