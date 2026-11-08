# Sync, T-pose and joiner-world fixes (code review pass)

Every change is marked in the code with a `KCT fix` comment. Verified offline: the .NET solution builds, all 2,512 .NET tests pass,
and all 80 Lua synthetic scenarios pass (5,088 checks). **Not verified in game.** The native DLL changes need a rebuild with MSVC
(this pass had no C++ compiler) and a two-player run of `docs/TESTER-SCRIPT-0.64.1.md`.

## T-poses (native, `native/KCDMP/wo208.cpp` — needs a DLL rebuild)
| # | Cause | Fix |
|---|---|---|
| 1 | In combat mode the hand-over stopped our base and installed nothing; rule 1 and the watchdog cancelled each other for seconds | the base is kept (bottom priority: the copy's own footwork replaces it); no base: guard idle / nudge |
| 2 | WO-214 C.1 "a base at the let-go" was never wired; a refused hand-over at let-go left the copy empty, and nothing checked it again | the let-go falls back to the neutral idle, else a nudge (counted as `atLetGo`) |
| 3 | A base of ours that never started kept blocking rule 1 and made every reseat report "installed" | dropped after 1 s on an empty body |
| 4 | A long queue (look poses) blocked rule 1 forever | an empty body over 1 s gets its base regardless |
| 5 | `overflow_reseat` stopped the running base, then a hand-over refused by the same long queue | a running base is kept |
| 6 | Rule 1's host-base install left stale `baseAt`/`baseStarted` | reset |
| 7 | The "stream quiet 3 s, let go" rule was unreachable (`lastState = now` every tick): copies out of the host's stream stayed on a frozen state | uses the newest arrival; 5 s (2.5 resends) |
| — | `npc_drive.cpp` hit gate read a cached entity pointer unchecked | re-validated |

## NPC copies in underclothes (joiner)
WO-201 B23's re-dress walks only DLL activity entries; the host sent a "none" row (dressed, walking) once and never again, and the
joiner's load forgot it, so copies undressed by the night or the save stayed so all day.
- `GameBridge.Wo141.cs` re-sends "none" rows after a load (the documented `copy_look_from_host` switch now gates it — it was read nowhere).
- `wo141_rules.h` the host refreshes "none" rows every 30 s (native; test updated in `native/tests/wo141_rules_tests.cpp`).

## Corpses standing on the joiner
Under host authority a body dead on its first packet was only frozen unless that packet had the resync flag — which the latest-wins
queues could coalesce away. Now a first-packet death is applied (`GameBridge.cs`), and resync/swing-cue samples are never coalesced.

## Desync (network, .NET)
- `NpcCompact.Decoder`: a late older delta was merged and re-sent under a newer seq (copies snapped back). Per-field seqs now.
- `LinkScheduler`: an older sample no longer replaces a newer waiting one; an expired never-repeated state is sent late instead of
  dropped; frames with once-only content (swing cue, resync, BodyState2) are events; on one-lane paths (TCP) a state frame is
  interleaved after every 16 KB of events, so a world transfer no longer holds every position until it ends (`EventBurstBytes`).
- `Wo202.StateKey`: the joiner's inbound queue keeps the same once-only frames in order.
- `GameBridge.cs`: dead/drawn bookkeeping follows sample order (a late "alive" no longer revives a corpse's mark); the peer sender
  clock only moves forward.
- `Wo147`: "superseded under lag" uses seq order, and the newest seq is the newest by seq, not by arrival.
- `ActionChannel`: the seq counter is locked, and the seq is re-stamped in wire order (`StampForWire`) so a reliable action is never
  dropped as stale because two threads raced.
- `MonoClock`: hold/freshness timers no longer use the wall clock (a Windows time sync stretched or cut them). Save-file comparisons
  keep the wall clock.

## Lua (`kdcmp.lua`)
- Puppet sample ring evicted by age (was 3 samples: too shallow at 20 Hz or with catch-up lag → walkers held, stepped, flipped idle/walk).
- Sequence check reset on an owner change, and a restarted sender's counter is accepted (copies froze until the new counter passed the old).
- Ghost dead-reckoning speed clamped (9 m/s on foot, 15 riding): burst arrivals read as 40 m/s ("two steps forward, one back").
- Ghost floor snap ignores the unreliable terrain fallback beyond ±0.3 m (ghosts lifted out of cellars / pulled off bridges).
- Legacy puppet speed uses the real elapsed time; horse clip keep-alive 4 s (queue flood).

## Looked at, not changed
- Sender timestamps are ms since boot; in a float32 Lua they lose precision after hours of uptime. Not changed: the DLL rejects a
  backward stamp (`npc_drive.cpp:459`), so rebasing on agent start would freeze copies after an agent restart. Needs a coordinated change.
- Foot sliding on copies (WO-201 B10) and the engine's own look-pose floods: need in-game probing.
- Tests fail to launch from very deep folders (Windows 260-char path limit): a system setting (LongPathsEnabled), not a code fix.

## Second pass

### Fixes
- `kdcmp.lua`: a sample the seq check will reject no longer moves the legacy path's target (copies stepped back).
- `wo141.cpp` (native): the re-dress tests the HOST's stance -- a copy that loaded in bed was never dressed.
- `wo208.cpp` (native): a hand-over counts only a RUNNING base (the copy's own, or ours) as "installed".
- `ClientSession.cs` (relay, old fallback queue): one waiting slot per NPC, so its samples never go out of order.
- **Sender timestamps** now count from the agent's start (+1 s), not the PC's boot (float32 Lua lost whole ms after 4.7 h of
  uptime). The DLL (`npc_drive.cpp` sender_stamp / push_sample) and the Lua (`mp_sender_stamp`) take a jump of over 10 s back as a
  restarted sender and start that stream's clock and order over. **Both players need this build** (an older receiver would freeze
  copies after the sender's agent restarts).

### IgnisComeDeliverance
- Installer (`installer/KCDMP.iss`): the name players see, the Start menu / desktop shortcuts and the Setup file
  (`IgnisComeDeliverance-Setup-<version>.exe`) are renamed; the old Kingdom Come: Together shortcuts are removed on install.
  Unchanged on purpose: the AppId, `%LOCALAPPDATA%\KCDMP`, `HKCU\Software\KCDMP`, the `kdcmp` mod folder and the launcher exe --
  so it upgrades over a Kingdom Come: Together install, and **its saves, Henry files and save backups are used as they are**.
  `tools/Build-Installer.ps1` and the installer tests use the new file name. `AppPublisherURL` still points at the upstream repo:
  set it to your fork's URL.
- Launcher: a start screen (`Components/PlayHub.razor`) with two choices, **Host a game** and **Join a friend**, each with its steps,
  over the existing host / Steam-join / setup flows; setup problems show first; the server list is under "Direct connect".
- A player on IgnisComeDeliverance cannot join one on official Kingdom Come: Together (or another build): the handshake checks
  the build id, by design.

### Building the installer
Needs Visual Studio's C++ tools (the DLL), the .NET 8 SDK and Inno Setup 6, then `tools\Build-Installer.ps1`. Its release gates
(the frame-rate soak, the save round trip) need the game; for a test build pass `-ReleaseCandidate` and the waiver text it asks for.

### Side by side with Kingdom Come: Together (0.64.1-icd2)
- The installer has its own AppId and folder (`%LOCALAPPDATA%\IgnisComeDeliverance`, `HKCU\Software\IgnisComeDeliverance`)
  and no longer removes Kingdom Come: Together's shortcuts; uninstalling it leaves `HKCU\Software\KCDMP` (shared).
- Shared on purpose: the game's one mod slot `Mods\kdcmp`, `HKCU\Software\KCDMP\ModsPath`, and the player data in
  `%LOCALAPPDATA%\KCDMP` (so both use the same saves and characters).
- `ModInstall.Placed` now counts a mod file that differs from this install's as not placed, so the launcher's checklist
  puts its own mod back (before, it only warned and ran this agent against the other install's game scripts).
  Test `AnotherInstallsPakIsReplacedByThisInstallsOwn` replaces `AHandBuiltPakWarnsButNeverBlocksAndIsNeverOverwritten`.
  Side effect for developers: a hand-built pak dropped into an installed copy is replaced by the launcher; develop with
  a development build (no install manifest), which is never touched.
- `Apply-DirectInstall.ps1` updates only an IgnisComeDeliverance install (its own InstallDir), never `%LOCALAPPDATA%\KCDMP`.

## Connection line in game (QoL)

The top-left line used to say only `Ping: 63 ms  clock +0.02 s`. It now says how good the link is:

    Connection: Good  ping 63 ms  jitter 8 ms  loss 0%

- **Grade:** Poor at 250 ms or more ping, 10% or more loss, 120 ms or more jitter, or the game 1 s or more behind (the
  same limits as the existing "struggling" toast); Fair at 120 ms, 2%, 40 ms or 0.5 s; Good otherwise. A worse grade
  shows at once, a better one only after about 6 s of better samples, so it doesn't flicker.
- **Jitter:** the median change between the last 8 pings, so one spike doesn't count but a ping that keeps swinging does.
- **Loss:** Steam's measured delivery quality (the worse of the two directions), sent by the agent with each ping. Not
  shown on a direct (TCP) link, which can't lose packets.
- **"your game 1.2 s behind"** appears only when this game, not the network, is late (in the background or busy).
- **The clock offset** is shown only when it is 1 s or more.
- Hidden as before by `mp_ping_line off` or `mp_clean_screen on`.

Code: `KCD2MP_ShowPing` / `KCD2MP_IcdConnSample` / `KCD2MP_IcdConnGrade` in kdcmp.lua; `GameBridge.LinkLossPct` in
GameBridge.Wo202Net.cs. Tests: tools/Test-WO98Synthetic.lua (h2), dotnet/KcdMp.Client.Tests/IcdConnTests.cs.

## icd5: catch-up instead of rubber-banding, quiet console, icon badge, rename

**What was already there.** Most of the usual anti-lag tricks were already built (WO-213, WO-201, WO-210): an adaptive
play-out delay per stream (`mp_playout_adaptive`, `mp_figure_playout`), 150 ms of walk-on prediction, a soft blend after
binds, swing holds, silences and the end of a prediction, sending by distance and combat (`mp_npc_bands`), and the joiner's
blows on copies playing their impact at once while the host judges them (`mp_copy_hit_local_none`).

**What was missing: the jump after a hold** (`mp_catch_up`, on). When samples stop, a moving body walks on for at most
150 ms and then holds, but its render clock ran on, so the next sample was already "in the past" and the body jumped to it.
Now the clock is held at the edge of what is known while the body waits, and the wait is replayed at 1.2x once samples
come (`wo213rules::catch_debt_step`; at most 0.4 s is replayed, never before the oldest sample held). A body that still
stood past its stream blends back from where it stands (`catch-up` blend). The ring holds 32 samples (was 16) so a 30 Hz
figure keeps enough history. Native test: a 400 ms dropout never moves the body more than 1.2x a frame's walk.
Counters: `catch_frames`, `catch_resumes` on `MP-NPCWRITE-COST`.

**Quiet console** (`mp_quiet`, on; the mod menu's "Quiet console"). The mod's routine lines go to kcd.log only; errors,
warnings, failures and the mod's important lines still show on the ~ console. kcd.log must keep every line (the agent and
the launcher read it), so it is proven first: 20 s into a session the console's verbosity goes to 1 and a probe is
written with `System.Log`; the agent sees the probe in the file and confirms; without a confirm in 5 s the console is put
back exactly as it was (`MP-QUIET on|unavailable|unneeded`).

**Partner badge as an icon** (`mp_badge_icon`, on; "Partner badge: icon"). A small "v" over the partner coloured by
health (green / amber / red), a short bar only while hurt, grey in a menu or reconnecting, a grey "x" when dead. Off: the
old name and numbers.

**Rename.** The mod menu title, the mod's toasts, the "could not connect" and "did not load" messages and Discord
presence say IgnisComeDeliverance.

## icd6: auto-join and the Co-op options window

**Auto-join** (`AutoConnect` in the launcher's settings, on; "Connect automatically" in Co-op options). The launcher
already pressed CONNECT itself when the host loaded a world through the mod's Start Game, and when a joiner pressed Join
Game on the game's menu (WO-159). What was left: a joiner who had already pressed **Join** in the launcher still had to
press Join Game again in the game. Now the launcher's Join is the join: a joiner with a character to bring waits for the
host from the start and joins with that character the moment the host is ready (Cancel on the game's menu, then Join
Game, to start fresh instead). A joiner with no character of their own still picks a new one on the game's menu.
`MenuTakeoverRule.JoinerPreArm`.

**Co-op options window** (start screen: "Co-op options"). Every choice the in-game mod menu remembers, as switches with
a line of explanation each, in three groups: World rules (the host's: friendly fire, shared crime, fast travel, keep
players together, quicksave), Playing together, On screen. It reads and writes the agent's own `mod-settings.json` (the
same file the in-game menu writes), keeping every other key, so the two never disagree; changes apply at the next
session. Defaults are the mod's own. `Models/CoopOptions.cs`, `Components/Modals/CoopOptionsModal.razor`; a test keeps
its list identical to the agent's `Wo154MenuRules.Settings`.

The ~130 engineering switches (`mp_...` commands) are not on this page: they are for testing a fix, not for playing
(see docs/SWITCH-AUDIT.md).

## icd9: quiet console made safe

Found in a live join (2026-11-08): the quiet console's probe reached kcd.log as `<Lua> [KCD2-MP] MP-QUIET probe ...` --
`System.Log` writes a `<Lua> ` prefix -- and the agent never confirmed it, so quiet mode switched itself off as designed.
That prefix is the danger: the agent's event channel accepts `[KCD2-MP-EVT]` only at the start of a line
(`LogTailGameTransport`, "event tag not at line start, ignored"). Had quiet mode engaged, every event the mod sends the
agent would have been dropped. Now:

- only the mod's chat lines (`[KCD2-MP] ...`) are ever written the quiet way; `[KCD2-MP-EVT]` and `[KCD2-MP-DATA]` are
  never touched (tested);
- the probe waits for a loaded world (both live probes fired mid-load, during a join, when the agent's log reader was far
  behind) and allows 15 s for the agent's confirm (was 5 s).

The same evening's join itself worked end to end: the joiner's launcher Join auto-connected from the main menu, the
host's world arrived in 6 s, the load took 118 s (106 s of it the game reading the save), and the joiner was placed 3.4 m
beside the host.

## icd9: the let-go loop that flooded NPC animation queues (a regression from this fork's first pass)

Live, 2026-11-08 (joiner, 3 minutes after joining): the game stopped answering the agent (its queue 150 s behind, ping
20 ms), kcd.log grew to 32 MB with `Mannequin Error, attempting to queue too many actions on entity 'ttkc_man_31' (queue
size = 459, but only up to 10 allowed)` and each queue listed, for every copy of the host's NPCs nearby; the native log
had 5,507 `WO208-OWN let go ... (the host's stream is quiet)`, the same copy about 50 times a second.

Cause: two of this fork's first-pass fixes together. `oc.lastState` became the newest state's arrival (so a copy the host
stopped streaming is let go), and the let-go gives a copy with no base a neutral idle. But a copy's state queue always
keeps its picked entry: the tick after the let-go owned the copy again from that same old state, found it quiet, let it go
and queued another idle -- every frame. Fixed:

- a quiet let-go drops the copy's states too (`g_copyStates.erase`): only a new state from the host owns it again;
- the let-go's base is given at most once per copy every 10 s, whatever loop ever lets a copy go again and again.

## icd10: the join's "still loading" heartbeat never waits behind the game

Live, 2026-11-08: two of three joins ended "The join ended (timeout)". Each time the joiner's game took 112-118 s to load
the host's world (about 104 s of it the game reading the save), and the host gave up 60 s into the load -- its rule:
60 s without the joiner's "still loading" (WO-204 IV.1, sent every 5 s). The joiner's session loop, which sent it, printed
its every-2-s link line twice in that minute: it was stalled, because it awaited two calls into the DLL, which a loading
game does not answer. The one join that held was a load in which the loop happened not to block.

- the heartbeat runs in its own loop that waits on nothing but the clock (`Wo204HeartbeatLoopAsync`), and logs
  `MP-JOIN joiner: told the host 'still loading' (...)` every 30 s;
- the session loop no longer awaits its two DLL calls (one of each in flight at most).

## icd11: the launcher updates itself; builds carry their release number

**Updater** (`Pages/Home.Update.cs`, rules in `Models/UpdateRules.cs`; "Check for updates" in Co-op options, on). When the
launcher opens it reads this fork's GitHub releases once; when a newer `icdN` has a Setup it shows a banner on the start
screen ("A new version is available: icdN", What's new, UPDATE NOW). Update now refuses while the game or a session runs,
downloads the Setup from this repository's own release download address only, checks its SHA-256 against GitHub's own
digest of the uploaded file (or the release notes' `Setup:` hash) and its size, deletes it on any mismatch, and otherwise
closes the launcher and runs the Setup once the launcher has gone. A release without a checksum is never offered.

**Build ids.** Every build from icd1 to icd10 carried the same build id (`6e21f1d-2611071805`, a stale BUILD_ID in the build
tree), so the handshake's "both players need the same build" could not tell them apart. From icd11 the id is
`icdN-<commit>`; the updater reads N from it. A build before icd11 has no number and is never offered an update
automatically -- install icd11 by hand once.

## icd12: the host pauses while the joiner catches up; copies held while the joiner is behind

Live, 2026-11-08: the joiner's game fell 30 s behind the host's world (its window in the background, then its own log flood).
Its copies of the host's NPCs went quiet, were handed back to its own AI and re-adopted in bulk (one villager 17 times), the AI
and the mod fought over them, their action queues overflowed every frame and kcd.log reached 265 MB -- which slowed the game
further. Two fixes:

- **The host pauses while the joiner catches up** (Co-op options, "Keeping in step (host)": on, 5 s by default, 2-60 s). The
  joiner reports how far behind its game is every 2 s (W203 kind 13, "behind", from the heartbeat loop that never waits on
  the game); the host holds its world with the engine's own whole-world pause (the one a join uses) while that is over the
  setting, and runs again under 1 s -- 60 s at most at a time, then 30 s before the next; never during a join or a link
  hold; released if the joiner stops reporting for 10 s. Toasts on the host say why. `IcdCatchUpRules`, tested.
- **Copies are held while the joiner is behind** (`KCD2MP_NpcSilenceRelease`): a quiet copy is late, not gone, while the
  agent reports this game 2 s or more behind -- it stays where it is (60 s at most) instead of going back to the AI.

## icd13 -- the partner's figure no longer freezes; the joiner no longer falls minutes behind

- **Invisible partner (frozen figure).** The agent's pipe to KCDMP.dll broke and reconnected once (18:29:09 in the field, right after the
  host's 11 s freeze ended and a burst of held traffic arrived). The DLL drops every body it writes when its pipe closes, and that drop's
  message died with the pipe, so Lua kept the host's figure marked native-owned: nobody wrote it, and it stood 55 m back while the top-bar
  marker followed the real stream. Now (a) the agent tells Lua when the pipe reopens (`KCD2MP_NpcNativePipeLost`: every copy and figure
  back to Lua, bound again), and (b) a bound figure more than 6 m from its stream for 2 s is taken back (`native=stuck`). The agent also
  logs why a pipe read ended.
- **The joiner falling 30-70 s behind.** The frame processor handled the host's NPC animation states (0x7B) inline, each waiting ~300 ms
  for the DLL pipe; every frame behind them (positions included) waited too. They have their own lane now, and animation states older
  than 3 s / activity rows older than 15 s are dropped instead of replayed (the activity lane had reached 330 s). `MP-W207-STATS` shows
  `anim_lane_frames`, `anim_lane_max_wait_ms` and `lane_too_old`.
