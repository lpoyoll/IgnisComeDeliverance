# My sword won't come out (the weapon icon is empty)

If your weapons will not draw — the sword is in your inventory, you press the draw key and nothing happens, the weapon icon beside the health bar
is blank, and no fight is going on — this is how to get them back. It works alone or in a game with a partner, and with or without the mod's
other features.

1. **Back up your saves first.** Copy the whole `saves` folder (in `Saved Games\kingdomcome2`) somewhere safe. The repair never writes into a
   save, but a backup costs nothing.
2. **Load the save where it happens.** Wait until you can walk around.
3. **Open the console** (the `~` key) **and type `mp_repair_combat`, then Enter.**
   Or press **Insert** (the mod menu), go to **Help**, choose **Fix my weapons / stuck fight** and press End.
4. Stand still for about ten seconds. You will see your sword come out for a moment and go away again — that is the game testing itself. Then a
   message appears:
   - **"Your weapons are free again — save your game now (a new slot is safest)."** Press the draw key: your weapons work. Save into a **new
     slot**, not over your old one.
   - **"Nothing was wrong…"** Your weapons already work, or you were in a dialogue. Nothing was changed.
   - **"This fight belongs to a quest scene…"** A scripted fight (a village brawl, a duel) is holding you. The command does not touch it. Reload
     a save from before that fight, or send us your logs.
   - **"…and they are still stuck. Send your logs…"** Open the launcher, **Report a bug → Collect logs**, and tell us what you did before it
     happened (sleeping? stealing from someone? fighting?).
5. **After loading again, the lock can come back** — the game rebuilds your weapons from the save when it loads, and a save made after the repair
   can still load locked. The mod therefore puts your weapon back by itself after every load in the same game session (the switch is
   `mp_weapon_autofix`, on by default). If you **restart the game**, type `mp_repair_combat` once again after loading.

Things the command **never** does: it does not change your items, your quests or your saves; it does not give you a weapon you do not have; it
never touches a scripted fight. If you have no melee weapon in your inventory it tells you so.
