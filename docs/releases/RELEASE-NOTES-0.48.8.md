# Kingdom Come: Together 0.48.8 — dead enemies, riding, travelling, trading, pickpocketing

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

0.48.6 plus WO-172 (2026-10-10/11), versioned 0.48.8 by the maintainer. The evidence is `docs/WO-172-findings.md`; what to check live is
`docs/TWO-PLAYER-CHECKLIST.md`, items 236–243 (and the earlier ones still pending). Nothing here has been played by two people yet.

**Both machines and the relay must run the same build.** It refuses every other version at the handshake.

---

## Changed

- **A dead enemy stays dead on the joiner's screen** (`mp_dead_wins`, default on). The host's death always reached the joiner in a tenth of a
  second; the mod then kept giving the corpse its activity, its tools and a new target, and the game stood it up again. A copy the host's
  death reached is now never worked again: no activity, no tool, no new target, no strike window, no swing row (`WO172-DEAD`).
- **Who enemies fight** (the host's rules). An enemy fighting nobody turns to the player who last landed a hit on it; it changes target at most
  once every 4 s (was 3), and only for a clearly bigger threat. A partner's swing that hits nobody no longer pulls a bystander into the fight.
- **Riding stays right** (`mp_ride_repair`, default on). A player's riding is sent as the game itself says it (the engine cleared the horse
  animation tag after a refused quick save, and the partner saw a man running in the air for nine minutes); a figure whose partner rides but
  is not on a horse here (or the reverse) is put right every 2 s; a horse that fell through the ground is put back on it.
- **No black screen after a forced travel** (joiner). The 60 s "faders restored" of 0.48.5 put a stuck black back (seven times in one
  session): the black, once lifted, now stays lifted until the next load. A quest's travel this game refused while the host travelled has its
  black lifted 1.5 s after the host's travel (`WO172-FT`).
- **The HUD comes back** after a shared travel, a scene release, a load and `mp_unstuck` (the game's own HUD element).
- **Trading** (joiner, `mp_trade_hold`, default on). A trader stays — on the host's screen and the joiner's — from the conversation until the
  trade screen closes (or you walk away), a cancelled haggle included. The game's own "you're wasting my time" after a cancelled haggle is the
  game's.
- **The joiner can pickpocket** (`mp_joiner_pickpocket`, default on). The game's own pickpocket runs on the joiner's copy; every item taken is
  asked of the host's world (taken out of the real person's pockets), or taken back with "Someone already took that." Players never rob each
  other.
- **Weapons invisible on the host: not found.** A mark (`mark_odd`, "Something's wrong here") now also saves the game's own screenshot
  (`mp_mark_shot`, default on): please mark the moment it happens.
