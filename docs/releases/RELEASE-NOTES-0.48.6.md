# Kingdom Come: Together 0.48.6 — the partner's figure, riders, the dialogue scene

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.48.5 plus WO-170 (2026-10-10), versioned 0.48.6 by the maintainer. The evidence is `docs/WO-170-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 232–235 (and the earlier ones still pending). Nothing here has been played by two people yet.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **The partner's figure never fights on its own (host).** The figure's combat automation stays off for the whole life of its body (0.48.5
  gave it back when the body was released, and the figure then fought a villager itself); a blow of the figure's that no swing of the
  partner's matches is measured and put back (`mp_figure_guard on|off`, default on). The host's log names why an NPC took the figure on
  (`WO170-AGGRO`).
- **Riders (joiner).** A riding noble is put on his horse only when both are really there (bound, with physics, within 3 m); otherwise he
  stays on foot, asked again every 2 s for 20 s (`WO170-RIDER mount=wait|ok|gave-up`). In 0.48.5 a stand-in rider mounted a horse that had no
  physics yet and the game crashed.
- **The dialogue scene (joiner).** The people of a conversation's scene are not paused again while the engine's scene still runs (10 s at most;
  `mp_scene_hold on|off`, default on), so the long black screen after a quest-giver's conversation should not come. The 20 s rescue stays.
- **The host's log counts the joiner's swings it receives** (`MP-ACTION … kind=Attack … dispatch=swing-queued`, `kinds=Attack:N`) — the joiner's
  swings were arriving in 0.48.5; only the count said otherwise.

## Not changed

Protocol v10; every 0.48.5 switch and default.

## Known, not verified

A figure blow landing on the host and being put back was not reproduced in the single-machine harness; the two-player run is the test.
