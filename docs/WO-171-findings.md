# WO-171 — findings (0.48.9, 2026-10-11)

Player map markers with the artist's knights (M) and smaller bug-report bundles (L). F1–F3 were done in WO-172 (0.48.8) and are not repeated here.
Marks: **[L]** live on this machine (the Modding Tools build, a throwaway copy of `OLD/playline4_wo167_after`, a synthetic partner, the maintainer
looking at the map and the compass), **[S]** synthetic/unit, **[F]** field logs, **[–]** not tested.

| item | result | default | check |
|---|---|---|---|
| M markers | the partner is the artist's knight in their colour, turned to their facing, with their name on hover; large on the map, small on the compass | on (`mp_partner_marker`, `mp_partner_marker_style rotating`) | [L] both roles, one synthetic partner, seen by the maintainer on the map and the compass; [S] slots, octants, throttles, names, files; [–] two real players, 3–4 players live |
| L bundles | a tar compressed with zstd, repeated noise collapsed, identical files stored once, parts of at most 19 MB | on (the launcher's Collect Logs) | [F] on the five 2026-10-10 bundles' contents; [S] 14 tests; [–] the launcher's dialog by hand |
| F1–F3 | done in WO-172 | — | — |

WO-168 (written earlier; it may have run without the art): `docs/WO-168-findings.md` does not exist and none of its slot / rotation / label code is on
`main`. Everything here was built from the WO-171 text.

## M0 — what the game's map does (static, then live)

Read from the game's own data (read only) and the Modding Tools build's `GUIModule.dll`:

- **The game's own figurine** is `Libs/UI/Textures/Maps/player.dds` (and `godwin.dds`): DXT5, **4096 × 256 = 16 frames of 256 × 256**, no mips. The
  Flash symbol `mc_player` picks a frame from the player's heading (`fc_setPlayer`). Frame 5 is the pure back view (facing north), frame 13 the front
  (south); the index grows clockwise seen from above, 22.5° per frame. It is one object, not a marker type, so a second one cannot be added by data.
- **Every other map marker** is a C_UIMap mark with a *type* (the 97-entry POI enum, 0x00–0x60). The map does not draw a mark's own picture: it builds
  one Flash group per **type name** (GUIModule's type → name function, a jump table of 13-byte stubs) and loads
  `Libs/UI/Textures/Icons/Map/<name>_icon.dds` (the compass loads the same file, 1:1; the map scales it down). A group's **hover text is the
  localization key `@ui_maplegend_<name>`**; a mark's own name string is not used for either. So a picture can only be chosen by the type's *name*,
  never per mark, and new types cannot be added by data. The probe that proved the key: a Lua listener on the map's `OnShowTooltipPos` logged
  `@ui_maplegend_kct_p0` for our marker and `@ui_maplegend_Hub` for a game pin.
- **A name for the hover** comes from the engine's localization, which accepts a loose file (`System.LoadLocalizationXml`, rows of key and text) at
  run time and **refuses a key it already has with a modal error window** ("String name hash collision … or it's already loaded"). Found live.
- **Route taken (B, by data + a table patch):** the game has unused type ids (0x21, 0x23, 0x28, 0x29, 0x36, 0x3C–0x44: its table gives them the shared
  default stub). Four of them (0x3C–0x3F, one per player slot) have their jump-table entries pointed at four stubs of ours that return the DLL's own
  per-slot string (`respawn_actions.cpp: pic_types_install`). The string is the picture's name; the pin's mark is made with that slot's type, so the map
  builds a group named after the picture, loads our texture and asks for our tooltip key. No code of the game is patched (four 32-bit table
  entries, restored on any failed check); everything is found in the function's own bytes (the string "In level is poi with invalid type" anchors the
  function; a parser reads its `cmp / lea / mov [rdx+rax*4+TABLE]` shape; the function is then *called* for 0x30 and 0x09 and for the four types
  before and after the patch). Any disagreement leaves the game's table alone and the pins keep the game's plain icon.
  Routes not taken: A (a picture per mark: the map has no such field), C (the figurine's own mechanism: it is one Flash object), replacing a game
  icon through the pak (the rules forbid replacing the game's marker art), a mark type outside the table (the map never draws types above 0x60).
- **Label (the name):** the agent writes `Mods/kdcmp/Localization/kct_n_<game run>_<colour><revision>.xml` (eight keys `ui_maplegend_kct_<c><n>M`
  with the player's name) and the mod loads it once. A colour taken over by a different name in the same game run uses the next **revision** of the
  pictures (`kct_p3b`, `c`, `d`: the same art under other names, so a key is never loaded twice); the fourth different name keeps the third's text until
  the game restarts. Files of an earlier game run are deleted at the next start.
- **Two sizes:** the compass draws an icon 1:1 and the map scales it down, so one size is wrong in one of them (128 px overflowed the compass strip, 64 px
  was tiny on the map). Each picture exists as 64 px (`kct_p3`) and 128 px (`kct_p3M`). The DLL's slot string is the small name while the map adds the
  mark (the compass marker is made then) and the large name afterwards (what the map reads when it builds its groups). Seen right by the maintainer.
- **Facing:** the stream's yaw is the engine's: radians, 0 faces +Y, counter-clockwise positive, so the compass heading is its negative; the octant
  boundaries lie 22.5° from each centre (5° of hold at an edge so a boundary does not flicker); `mp_marker_dir_offset` rotates by whole steps and
  `mp_marker_dir_flip` mirrors the turning. A mounted partner's stream is the horse's too (the same sample feeds the horse copy).
  **The art's frame convention is an assumption** (below).

## M1 — the art

32 images, 4 colours × 8 frames, 1800 × 2200 RGBA PNG, named `0.png`–`7.png` in a folder per colour (red, purple, blue, green); exactly 4 × 8, none
missing or extra. Copied to `assets/map-markers/source/`; `tools/Build-MapMarkers.py` writes the pak's pictures (DXT5, no mips; the game's own map
icons are 64 × 64 BC7) under `docs/branding/pak-source/Libs/UI/Textures/Icons/Map/kct_*`: every frame cut with **one box** (the union of the 32
frames' solid pixels, 1512 × 1886 inside a 1886 × 1886 square, so the pedestal never moves between frames), scaled down only, with a Lanczos filter in
premultiplied alpha (no dark fringes): **1800 × 2200 → 128 × 128** (the map) and **64 × 64** (the compass). `docs/branding/map-markers-contact-sheet.png`
shows all 32 (4 rows × 8 columns, each cell labelled colour / frame / assumed facing / file).

| colour | player | frames 0–7 (assumed facing) |
|---|---|---|
| red | 1, the host | S, SW, W, NW, N, NE, E, SE |
| purple | 2 | the same |
| blue | 3 | the same |
| green | 4 | the same |

**The assumption.** The file names are 0–7, no directions. Sorted order read as N, NE, E, … is contradicted by the art itself: frame 0 is a front view
(it would show the back). The convention used: frame 0 faces south (the viewer) and the frames turn clockwise, one octant each, so frame *n* faces octant
(*n* + 4) mod 8 and a knight facing north shows frame 4 (a back view). That matches the game's own figurine (back at north, clockwise index). The
frames themselves are **not evenly spaced** (frames 4–7 are all back-ish, frame 3 a profile), so a picture may be off by a step: `mp_marker_dir_offset`
(try 1–7), `mp_marker_dir_flip on` (turns the wrong way). The maintainer saw the knight turning through its poses on the map and the compass;
whether each pose matches the real facing is for the two-player session (checklist 245). Artist credit: none was given; `AUTHORS` has a
`TODO: artist credit` line.

The pak holds 768 files, all ours (`kct_*`: 32 pictures × 4 revisions × 2 sizes × 3 variants — picture, undiscovered, shadow); nothing of the game's
is replaced. `tools/Publish-Release.ps1` now packs every `.dds` under `pak-source`; the launcher still places the pak only for a game it starts and
removes it when that game exits (WO-159). The art's source path is not written anywhere in the repository.

## M2 / M3 — behaviour and switches

- **Slots:** the host is 1 (red); the others take 2, 3, 4 in the order a machine first sees them, **keyed by name** (a crash-rejoin comes back with a
  new ghost id and keeps its colour); a fifth gets the game's plain pin. On a joiner the host is the lowest live id when the leash fields do not say.
  Slots are worked out on each machine, so with three or more players two machines may disagree on blue and green.
- **One pin per partner**, through the existing safe path (readiness gate, never saved, stale-owner cleanup): moved when the partner moved more than
  2 m, at most once a second and 60 a minute (the field's 0.47.6 rate was 81 a minute; the DLL's own gate is 2 m too); the picture changes only when the
  octant changes, at most 4 a second (the tick is 1 s, so at most once a second); set again every 5 s (the DLL forgets its pins at a load). The marker
  is made again when the picture or the name changes (the map reads both when a mark is made).
- **Log:** `WO171-MARK slot=… colour=… dir=… added|moved|turned|removed`, `WO171-NAMES …`, `MP-WO171-STATS …` (agent); `WO171-MARK picture types armed …`,
  `WO164-MAPMARK pin … added (type 0x3D, picture n, title "…")` (native).
- **Switches:** `mp_partner_marker` (on), `mp_partner_marker_style rotating|flat|companion` (rotating), `mp_marker_dir_offset 0..7`,
  `mp_marker_dir_flip on|off`, `mp_w171_status`. Mod menu → Display: "Partner on the map" (existing) and **"Marker style (knight)"** (on = the knight,
  off = the game's plain pin; `flat` is console-only — the menu keeps on/off items only; remembered as `PartnerMarkerArt`).
- **New pipe op** `0x2C` PinLabel `[owner][n][utf8 ≤ 31]`; the picture code rides the unused id field of MirrorGrave op 3 (1..128 = revision × 32 +
  colour × 8 + frame + 1).

### Live checks [L] (the Modding Tools build, one synthetic partner)

| check | result |
|---|---|
| host role, the partner stands and turns once in 40–60 s | the pin turned through all 8 pictures in order (`kct_p4` N, `p5` NE, … `p3` NW), purple, the name from the partner's own name |
| host role, the partner walks a 12 m circle (period 50 s) | the pin moved (29 moves in the last minute, cap 60) and turned through all 8 pictures |
| joiner role (the synthetic partner is the host) | **red**, slot 1 (a first run came out purple: the host id was unknown to the leash fields in the harness — fixed, then re-run) |
| a load of the throwaway save | the pin went with the load (the map's mark count 18 with the pin → 17 after the load's rescan, the game's own marks unchanged) and came back within 5 s |
| a rejoin (the partner leaves, comes back, same name) | **purple again**, slot 2 |
| the map, hovered by the maintainer | the knight, large, with a scroll "synth-host" and a 13-letter test name |
| the compass | the knight, small, like the other icons |
| faults | none of ours (`FAULT-SUM` lists only `wo137::set_send_callback`, a synthetic-harness artefact seen before WO-171 changes: the DLL's NPC sender with a stand-in peer) |
| the game's own markers | unchanged: the map holds 17 of the game's own marks without our pin (18 with it); a game pin and the figurine look as before in the maintainer's screenshots |
| the brand pak / saves | the brand pak, the Localization folder and the dev pak were taken out and the installed mod restored (hashes equal the backup); 826 save files byte-equal to the backup; the throwaway playline removed |

**What the maintainer's looks found and what changed because of them:** (1) the pin was the game's plain golden pin with no name — the first design swapped
a name literal for the one call that makes the mark, which the map never reads (it groups by type name at draw time); (2) the knight overflowed the
compass at 128 px and was tiny on the map at 64 px — two sizes; (3) a second load of a key crashed into the engine's modal error — each key loads once,
with revisions for replacements.

**Frame rate (spot check, this machine, the window in front):** the DLL's own FRAME line, first two minutes with the marker running: 56.7 and 65.2 fps,
`ours_us_mean` 622 and 604 µs. The windows after that (21–26 fps, 815–929 µs) were taken while the Lua test suites and a load were running on the same
machine and are not comparable. WO-172's minimised numbers were 62–78 fps and 496–689 µs. Not a clean A/B (menu and map-open numbers were not taken
by script).

## L — smaller bug-report bundles

`KcdMp.LogPack` (a library; the launcher's Report Bug → Collect Logs calls it, falling back to the old zip, and says so, if it cannot run):

- **Codec:** a tar of one zstd frame per file (ZstdSharp, level 19, 64 MB window), a `.tzst`. **7-Zip 24 opens it: the `.tzst`, then the `.tar`
  inside** (also `tar --zstd -xf`, `zstd -d`). Checked with 7-Zip 24.05 (`t`, `l`, `x`) on a 3-part bundle.
- **Current launch by default;** `history\` only when "include earlier launches" is ticked (the dialog's new tick). The engine's one previous
  `kcd.log` and the agent's `*.prev*` stay (they are small after collapsing).
- **Collapse** (`NoiseRules`): 32 explicit noise classes (the position and event streams, the combat pipe frames, the ghost and position lines, pings,
  the animation warnings, item-moved and combat-init errors, OSH, animation-queue overflow, call stacks, the planner / NPC scan lines …) and byte-for-byte
  repeats of the line before. A run of seven or more keeps its **first and last line** and one line `[kct-pack] N more <class> between <t1> and <t2>`;
  shorter runs are written out. **Never collapsed:** `MP-MARK`, `FAULT`, `STATS`, `SUMMARY` lines, and every line within **5 minutes of an
  `MP-MARK odd`** (time from the line's clock; the game's `kcd.log` has none, so its `t=` plus the file header's start time; the windows are taken
  from every log of the bundle and apply to all of them). Line ends become LF. Identical files (the native mirror log, `logbackups\kcd.log` = the newest history
  copy, `settings.launcher-dir.json`) are stored once and named in the manifest.
- **Parts** of at most 19 MB (the manifest, the closing blocks and 256 KB reserved in part 1): biggest first into the first part with room; a file that alone
  would not fit is cut at line ends into `….pieceKofN`. `…-part1of3.tzst`; each is a complete valid archive; **part 1 starts with `MANIFEST.txt`** (how
  to open, every file, sizes, sha256 of the original, which part, the collapse counts).
- **Privacy:** unchanged: the launcher's own filter of `mod-settings.json` runs before packing; no other personal-data scrub existed before and none was added.
- **Time:** level 19 costs 7–77 s on these bundles (the dialog says "a long session can take a minute" and runs off the UI thread).

### Field bundles (the contents of the five 2026-10-10 bundles, run through the same code; the original zips were the launcher's old deflate)

| bundle | old zip → uncompressed | current launch only (original → packed) | with earlier launches (original → packed) | marks | time (cur / earlier) |
|---|---|---|---|---|---|
| 202216 (joiner) | 15.4 MB → 149.6 MB | 57.5 MB → **2.1 MB** | 149.6 MB → **4.0 MB** | 0 | 14 s / 28 s |
| 215020 (host) | 27.2 MB → 245.6 MB | 135.5 MB → **4.7 MB** | 245.6 MB → **7.8 MB** | 3 / 6 | 34 s / 61 s |
| 222506 | 10.3 MB → 85.2 MB | 28.9 MB → **1.1 MB** | 85.2 MB → **2.5 MB** | 0 | 7 s / 18 s |
| 224122 (host's session) | 26.5 MB → 219.7 MB | 157.6 MB → **7.5 MB** | 219.7 MB → **9.6 MB** | 16 / 17 | 69 s / 77 s |
| 224132 | 35.2 MB → 316.4 MB | 122.2 MB → **4.7 MB** | 316.4 MB → **9.0 MB** | 5 / 7 | 34 s / 64 s |

Every run checked (`dotnet/KcdMp.LogPack.Tool`, all ten): **every line within 5 minutes of every `mark_odd` is present, line for line and in order** (e.g.
agent.log 119,005 window lines, 0 missing), **every `MP-MARK` / `FAULT` / `STATS` / `SUMMARY` line is present** (3,399 in one agent log, 0 missing),
and **the counts add up**: lines read = lines kept + lines collapsed for every file (1,296,160 = 884,485 + 411,675 for the largest run) and the output has
exactly kept + summary lines. Every file is either byte-equal or named in the manifest as the same as another. Splitting: the largest bundle with a
4 MB limit came out as 3 parts (3.7 / 3.9 / 1.4 MB), all three open in 7-Zip and by the tool. No field bundle needed a split at 19 MB (the biggest is
9.6 MB). **Not done by hand:** the launcher's dialog itself (it builds; `ReportBugModal` runs the packer on a background task).

Pocketed, not built: a direct upload from the launcher to storage the maintainer controls (short-lived write-only size-capped links, a short code for
#help, auto-delete after 30 days, optional encryption with the maintainer's public key).

## Gates

| gate | result |
|---|---|
| agent unit tests | **1,413** (0.48.8: 1,376) |
| native tests | **9,220 checks** in the harness's own count (it reported 1,287 before WO-171; the name tables are looped) |
| Lua synthetic suites | **57** suites, **3,935** checks, 0 failing (new `Test-WO171Synthetic`, 16; the WO-154 menu suite's counts updated for the new item) |
| Lua locals / relay / setup / farkle / log bundle | 6/6 · 63 · 77 · 59 · 14, all passing |
| native guards (no raw `__try`) | 7 passed |
| live (unattended except the maintainer's looks) | M, both roles; load; rejoin |

## Not done / untested

- **Two real players.** Everything above is one machine and a synthetic partner. The art's facing convention, the colours on both machines, three or
  four players live (the slot logic is unit-tested for four), a mounted partner on the map, the map open at the moment a picture changes (the group is
  built when the mark is added, so an open map may show the compass's small picture until the next change — not seen), a hover on a distant marker.
- **Retail / any other build:** the map marker is Modding Tools only; the type-table parser refuses on bytes it does not match (no marker picture, the plain pin).
- The launcher's Collect Logs dialog by hand; the bundle on a tester's machine (7-Zip, Discord).
- F2/F3 were not rebuilt (WO-172: no case found / done).

## Housekeeping of the live session

- Saves backed up before anything (826 files, SHA-256 per file); the throwaway `playline4` was a copy of `OLD/playline4_wo167_after` (it did not exist
  before) and was removed; every save compared **byte-equal** to the backup afterwards.
- The installed `Mods\kdcmp` (two paks and the manifest) was backed up and restored (hashes equal); the brand pak and the `Localization` folder written by the run
  were removed.
- The game window was in front for the part the maintainer watched (they asked for it); no key, mouse or game input was sent by the harness.
- Harness: `tools/wo118/run171.py` (`spin`, `circle`, `load`, `rejoin`), the synthetic peer's new `gcircle` / `gspin` verbs, `PEER_NAME` in `run165.py`.
