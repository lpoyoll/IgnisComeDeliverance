#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-175 DEV-ONLY: send real key presses to the game, for tests of the player's own input path.

NEVER SHIPPED (tools/ is not in the installer payload; Build-Installer.ps1's no-input-tooling gate fails the build
if anything like this appears in a shipped file). Never called by the launcher, the agent, the DLL or a shipped script.

The maintainer's input policy (WO-175, 2026-10-11), enforced here:
  * only the game's own window (a KingdomCome.exe process) is ever brought to the front;
  * before EVERY key event the foreground window must belong to that process -- otherwise nothing is sent,
    all input stops and the refusal is logged;
  * send only: no hooks, no key-state polling, no raw-input registration, nothing of the user's input is read;
  * every key sent is logged (key, time, test) to tools/dev-input/keys.log, nothing else.

    python tools/dev-input/send_key.py <test-name> <key> [hold_ms] [--front] [--back]
      --front  bring the game window to the front first (allowed by the policy)
      --back   minimise the game window afterwards
"""
import ctypes
import ctypes.wintypes as wt
import datetime
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, 'keys.log')
user32 = ctypes.WinDLL('user32', use_last_error=True)

# DirectInput games read scan codes: KEYEVENTF_SCANCODE.
SCAN = {'1': 0x02, '2': 0x03, 'b': 0x30, 'r': 0x13, 'f': 0x21, 'e': 0x12, 'esc': 0x01, 'tab': 0x0F, 'i': 0x17}
KEYEVENTF_SCANCODE, KEYEVENTF_KEYUP, INPUT_KEYBOARD = 0x0008, 0x0002, 1


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [('wVk', wt.WORD), ('wScan', wt.WORD), ('dwFlags', wt.DWORD), ('time', wt.DWORD), ('dwExtraInfo', ctypes.c_size_t)]


class _U(ctypes.Union):
    _fields_ = [('ki', KEYBDINPUT), ('pad', ctypes.c_byte * 32)]


class INPUT(ctypes.Structure):
    _fields_ = [('type', wt.DWORD), ('u', _U)]


def log(test, what):
    with open(LOG, 'a', encoding='utf-8') as f:
        f.write('%s test=%s %s\n' % (datetime.datetime.now().isoformat(timespec='milliseconds'), test, what))


def game_pids():
    out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq KingdomCome.exe', '/FO', 'CSV', '/NH'], capture_output=True, text=True).stdout
    return {int(l.split('","')[1]) for l in out.splitlines() if l.startswith('"KingdomCome.exe"')}


def game_window(pids):
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(h, _):
        pid = wt.DWORD()
        user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
        if pid.value in pids and user32.IsWindowVisible(h) and user32.GetWindow(h, 4) == 0:   # GW_OWNER: a top-level window
            found.append(h)
        return True
    user32.EnumWindows(cb, 0)
    return found[0] if found else None


def foreground_is_game(pids):
    h = user32.GetForegroundWindow()
    pid = wt.DWORD()
    user32.GetWindowThreadProcessId(h, ctypes.byref(pid))
    return pid.value in pids


def send(test, key, down, pids):
    if not foreground_is_game(pids):
        log(test, 'REFUSED key=%s %s: the foreground window is not the game -- all input stopped' % (key, 'down' if down else 'up'))
        raise SystemExit('foreground is not the game: stopped')
    i = INPUT(type=INPUT_KEYBOARD)
    i.u.ki = KEYBDINPUT(0, SCAN[key], KEYEVENTF_SCANCODE | (0 if down else KEYEVENTF_KEYUP), 0, 0)
    if user32.SendInput(1, ctypes.byref(i), ctypes.sizeof(INPUT)) != 1:
        log(test, 'FAILED key=%s err=%d' % (key, ctypes.get_last_error()))
        raise SystemExit('SendInput failed')
    log(test, 'key=%s %s' % (key, 'down' if down else 'up'))


def main(argv):
    args = [a for a in argv if not a.startswith('--')]
    test, key = args[0], args[1].lower()
    hold = int(args[2]) if len(args) > 2 else 80
    if key not in SCAN:
        raise SystemExit('unknown key ' + key)
    pids = game_pids()
    if not pids:
        raise SystemExit('the game is not running')
    if '--front' in argv:
        h = game_window(pids)
        if not h:
            raise SystemExit('no game window')
        user32.ShowWindow(h, 9)   # SW_RESTORE
        user32.SetForegroundWindow(h)
        time.sleep(3.0)   # the game takes a moment to accept input after it regains focus
    send(test, key, True, pids)
    time.sleep(hold / 1000.0)
    send(test, key, False, pids)
    if '--back' in argv:
        h = game_window(pids)
        if h:
            user32.ShowWindow(h, 6)   # SW_MINIMIZE
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
