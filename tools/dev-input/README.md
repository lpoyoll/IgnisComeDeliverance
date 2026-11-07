# tools/dev-input — DEV ONLY, never shipped

`send_key.py` sends real key presses to the game for tests that need the player's own input path (WO-175's real
draw test: the game's draw key, `qam_init`, default **B**, read from `Libs/Config/keybindSuperactions.xml` and the
profile's `keybinds_settings`).

The maintainer's input policy (WO-175, 2026-10-11) is enforced in the script: only the game's own window
(`KingdomCome.exe`) is brought to the front; before every key event the foreground window must belong to the game,
otherwise nothing is sent and input stops; send only (no hooks, no key-state polling, no raw input); every key sent
is logged to `keys.log` (git-ignored). Nothing here is called by the launcher, the agent, the DLL or any shipped
script, and `tools/Build-Installer.ps1`'s no-input-tooling gate fails a build whose payload contains input injection
or capture.
