# Kingdom Come: Together 0.48.9 — your partner on the map, smaller bug reports

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.48.8 plus WO-171 (2026-10-11), versioned 0.48.9 by the maintainer. The evidence is `docs/WO-171-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 244-252. The map marker was seen on one machine against a synthetic partner, never between two
real players.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **Your partner is a knight on the map** (`mp_partner_marker`, default on). The artist's knights, one colour per player: the host red, then
  purple, blue, green in the order players first joined (a rejoin keeps its colour). The picture turns to the partner's facing in eight steps
  (its back shows when they face north), is large on the map and small on the compass, and hovering over it shows the partner's name.
  A fifth player gets the game's plain pin. Your own figure stays the game's own.
  Switches: `mp_partner_marker_style rotating|flat|companion` (default rotating), `mp_marker_dir_offset 0..7` (if the picture is off by a
  step), `mp_marker_dir_flip on|off` (if it turns the wrong way); the mod menu has "Marker style (knight)". The art ships only in the
  launcher's per-run brand pak and is removed with it when the game exits; nothing of the game's own files or art is changed.
- **Bug-report bundles are small** (launcher: Report a bug, Collect logs). Repeated noise lines are collapsed into one counted line (never
  the lines around a "Something's wrong here" mark, never marks, faults or stats lines), identical files are stored once, and the bundle is a
  tar compressed with zstd: the field's 26-35 MB zips of 2026-10-10 are 1-9 MB. A bundle over 19 MB comes as numbered parts, each a valid
  archive; the first holds `MANIFEST.txt`. Open it with 7-Zip (the `.tzst`, then the `.tar` inside) or `tar --zstd -xf`. The current launch
  by default; "include earlier launches" adds the older ones.

## Not changed

WO-171's F1-F3 (a partner's swing, a click after a menu, quest corpses) were done in 0.48.8.

## Known limits

- Colours are worked out on each machine in the order it first sees a player: with three or more players two machines may disagree on who is
  blue and who is green.
- A name can be given to the game's text only once per game run: a colour taken over by a fourth different name keeps the third's name on the
  hover until the game restarts.
- The knight's directions follow the art's own order, read as "frame 0 faces south, turning clockwise"; the art's frames are not evenly
  spaced, so a picture may be a step off (`mp_marker_dir_offset`).
- The map marker works on the Modding Tools build only (it finds the map's type table in the module's own bytes and refuses if it does not
  match); on any other build the pin stays the game's plain icon.
