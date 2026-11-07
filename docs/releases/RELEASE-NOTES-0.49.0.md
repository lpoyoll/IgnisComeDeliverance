# Kingdom Come: Together 0.49.0 — the joiner's blows, enemies that fall over

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.48.9 plus WO-173 (2026-10-11), versioned 0.49.0 by the maintainer. The evidence is `docs/WO-173-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 253-260. Everything here was checked on one machine against a synthetic partner, never between two
real players.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **One blow of the joiner never knocks an enemy out.** The joiner's hit on an NPC of the host's world was added straight onto the NPC's
  stamina; a fresh guard could drop unconscious at once on the host's screen while it kept fighting on the joiner's. One blow now takes at
  most 35 % of the NPC's highest stamina (`WO173-HIT … (stamina capped)`); a knock-out comes only from the game's own rules after a real fight.
  This is what runs by default (below).
- **Knock-outs and get-ups reach the joiner at once.** The host checks the NPCs a figure fought or a joiner hit ten times a second and tells
  the joiners when one goes down, gets up or dies (W164 NpcDown). The joiner's copy falls at once and stands up 0.3 s after it wakes (it
  used to wait for a once-a-second check and a 1.5 s stand-up).
- **New, switched off: `mp_real_hits`** (host). The joiner's blow goes through the host game's own hit processing: the joiner's figure
  strikes for his swing, the NPC's own combat rules decide hit / block / perfect block / broken block, the game's own hit reaction is
  created and played (and, as the host's own reactions always were, sent to the joiner's copy), and a landed hit takes the health the
  joiner's game measured. Anything missing on the way (no swing to strike with, a body not ready) falls back to the capped way above,
  blow by blow, and says why (`fallback=`). Switched off because one of its checks needs two people (findings, C1): the host types
  `mp_real_hits on` to try it. `mp_w173_status` prints its counters.

## Not changed

- The reverse direction (the joiner's own block deciding an enemy's blow on his screen, `mp_victim_decides`), bows, retail.

## Known limits

- With `mp_real_hits on`, a townsman fighting unarmed (the game's "combat mode 0") gets no combat hit reaction from the game's rule (its
  own test says it cannot react); armed fighters do.
- A joiner's blow whose figure on the host is not facing the NPC still lands (the joiner's game already decided he hit it).
- The real-hit path works on the Modding Tools build only: it finds the game's hit processor and reaction step in the module's own bytes and
  refuses (the capped way runs) if they do not match.
