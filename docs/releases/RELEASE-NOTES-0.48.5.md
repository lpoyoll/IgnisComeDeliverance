# Kingdom Come: Together 0.48.5 — talking and trading, the stuck scene, ambush bandits, shared fast travel

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.48.2 plus WO-167 (2026-10-10), versioned 0.48.5 by the maintainer. The evidence is `docs/WO-167-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 224–231 (and the earlier ones still pending).

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **Talking to the host's people (joiner).**
  - The mod's own talk request (the "fallback" that gave a request no talk action made to the nearest copy) is **off by default**:
    in the field it never started a conversation and twice took the person whose conversation had just ended. On, it never acts
    within 3 s of a conversation's end and never on the person just talked to. `mp_talk_fallback on|off` (default off).
  - Pressing talk again on the person whose conversation is just ending starts a fresh talk instead of pausing them.
  - After the host's own conversation or trade with someone, their copy on the joiner's screen is swept, its fight state ended (only
    when the host's person is not fighting), its shop state read, and it is woken first when the joiner talks to it next.
  - On the host, the joiner's figure bumping into someone no longer leaves that person's "watch it!" reaction running.
  - A talk that times out names the person's behaviour, minigame and behaviour-tree step; a `WO167-NOHOOK` line names someone you
    stood in front of whose talk never reached the mod.
- **The stuck conversation scene (joiner).** A conversation's scene still running 20 s after the conversation ended (the black
  screen of the jugglers' "Find a Lute") is ended the game's own way; the screen comes back, you are put on your feet and you read
  "That scene couldn't play here — your partner's world runs it." `mp_scene_release on|off` (default on). A normal scene is never
  touched.
- **Fighting (joiner).**
  - People the host's world spawns that yours never did (a road ambush's bandits) get the walk start a fresh figure gets: no T-pose.
  - Every enemy holds the weapon the host's enemy holds — drawn, put away or none — created in its inventory when it has none, set
    once and held; the old 2-second tug-of-war over the weapon is gone. `mp_copy_weapons on|off` (default on).
  - A swing counts as shown only when the game really started its animation; one that never animated is taken back and the lunge
    plays before its damage. `mp_swing_watch on|off` (default on).
  - A paired attack (a clinch, a throw) an enemy cannot play on your screen is shown as the plain attack of the same weapon.
    `mp_sync_fallback on|off` (default on).
- **Fast travel: on by default, and shared.** One travel for both, no vote:
  - when the host travels (its own pick, a quest, the console), the joiner's game runs the same travel to the same place — the
    travel map and the route on both screens — and the joiner is placed beside the host when both have arrived; the clocks end
    the same;
  - when the joiner picks a place, the host's game travels there for both, or the joiner reads "Your partner can't travel right
    now." and nothing moves;
  - a road ambush that stops the host's travel brings the joiner to where it stopped;
  - "Travelling with your partner…" on the partner's screen; the "fast travel is off" message only when the host turned travel off
    and someone picks;
  - the host's "off" no longer blocks a quest's own forced travel (the engine's switch is held only while someone picks on the
    map).
- **No silent guards.** Every error the new code catches is counted, logged with its place, switches that feature off for the
  session and says so on screen once (for example "Shared fast travel is off this session (a game error).").
- **For testers:** new log lines `WO167-TALK`, `WO167-T2`, `WO167-BUMP`, `WO167-NOHOOK`, `WO167-SCENE`, `WO167-PULSE`, `WO167-WEAPON`,
  `WO167-SHOWN`, `WO167-SYNCFALLBACK`, `WO167-SYNCGUARD`, `WO167-FT`, `WO167-FAULT`, `MP-WO167-STATS`; `mp_w167_status` prints the
  switches and counters.

## Known issues

- **The travel map's point id has not been seen yet.** A pick on the map is turned into a place both games know (an entity's id or
  name → its position); if the map uses some other id, a joiner's pick is answered "Your partner can't travel right now" and the
  host's own pick reaches the joiner as "Travelling with your partner…" followed by the leash bringing them along (0.48.2's way).
  Every pick writes its raw id to the log (`WO167-FT pick id=…`); please send it.
- **A travel the game refuses leaves its map open** (for example a pick from a spot the game can't travel from). Close it with Esc;
  the mod says so on screen.
- **A quest that forces a travel** runs on both machines (the joiner's own copy of it is no longer refused, which used to leave the
  joiner stuck on the map); the joiner is placed beside the host when both have arrived.
- **The joiner's own run of a travel can't be stopped midway:** when a road ambush stops the host's travel, the joiner arrives at the
  destination first and is then placed beside the host where it stopped.
- **Checked unattended with a stand-in partner only** — the checklist's items 224–231 are the proof with two people (talking right
  after the host's trade, the jugglers' scene, the dice player, both fast travels and a quest's forced travel, an ambush fight).
- **Not soak-tested** against the game without the mod (the maintainer's standing rule, stated in WO-161 on 2026-10-08 and applied
  to WO-167 on 2026-10-10; the waiver is recorded beside the installer). Unattended 30-minute fights with three enemies and a new
  ambush bandit every 5 minutes ran instead (the last one on this build: 1,199 blows, each applied once, 1,191 with their swing and 8 with the lunge for the 8 swings that never animated, no fault). The mod's own cost per frame against 0.48.2,
  same machine, same day, the window minimised: main menu +5 %, town +11 %, a three-enemy fight +6 % (0.62 → 0.66 ms); the frame rate −5 %, +3 %, +8 %. Not signed unless the build says so.
