#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-175 gate (input policy rule 6): nothing that can inject or capture keyboard / mouse input ships.

    python tools/Test-NoInputTooling.py <dir-or-file>...

Scans every file (exe, dll, pak, Lua, scripts, configs) byte-wise for the names of OS input injection and capture
APIs and libraries, in ASCII and UTF-16, inside zip/pak archives too (the mod's paks are stored zips). Every hit is
listed; the gate passes only with zero hits. Third-party runtime files that merely import a key-state API for their
own UI are not exempted silently: an exemption must be listed in ALLOWED below by file name with the reason, so the
reviewer sees it (none exist today).
"""
import io
import os
import re
import sys
import zipfile

APIS = [b'SendInput', b'keybd_event', b'mouse_event', b'SetWindowsHookEx', b'GetAsyncKeyState', b'GetKeyState',
        b'GetKeyboardState', b'RegisterRawInputDevices', b'pynput', b'pyautogui', b'AutoHotkey', b'SendKeys',
        b'import keyboard', b'import mouse', b'from keyboard', b'from mouse']
# PostMessage / SendMessage are everywhere in UI code; they are a hit only next to a keyboard / mouse message name.
MSG_RX = re.compile(rb'(PostMessage|SendMessage)\w*[^\n]{0,80}WM_(KEY|CHAR|SYSKEY|[LMRX]BUTTON|MOUSE)', re.I)
# Reviewed exemptions (WO-175, 2026-10-11): Microsoft's own .NET Desktop runtime, carried by the launcher's
# self-contained publish. These are the framework's public input APIs (SendKeys, WPF/WinForms keyboard state, UI
# Automation); no code of this project calls them (our own files are scanned with no exemption at all). Removing them
# needs the launcher publish to drop WinForms/WPF -- recorded as open in docs/WO-175-findings.md. A file is exempt only
# by exact name AND only if it carries Microsoft's copyright string.
_MS = 'Microsoft .NET Desktop runtime (framework input API, not called by this project)'
ALLOWED = {n: _MS for n in (
    'System.Windows.Forms.dll', 'System.Windows.Forms.Primitives.dll', 'System.Windows.Forms.Design.dll',
    'System.Windows.Forms.resources.dll', 'Microsoft.VisualBasic.Forms.dll', 'PresentationCore.dll',
    'PresentationFramework.dll', 'PenImc_cor3.dll', 'UIAutomationClient.dll', 'UIAutomationClientSideProviders.dll',
    'System.Console.dll')}
_MS_MARK = (b'Microsoft Corporation', 'Microsoft Corporation'.encode('utf-16-le'))


def scan_bytes(name, data, hits):
    for api in APIS:
        for enc in (api, api.decode().encode('utf-16-le')):
            if enc in data:
                hits.append((name, api.decode()))
                break
    if MSG_RX.search(data):
        hits.append((name, 'Post/SendMessage + WM_ input message'))
    if data[:2] == b'PK':
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                for n in z.namelist():
                    scan_bytes(name + '!' + n, z.read(n), hits)
        except zipfile.BadZipFile:
            pass


def main(argv):
    roots = argv or []
    if not roots:
        print('usage: Test-NoInputTooling.py <dir-or-file>...')
        return 2
    hits, files, microsoft = [], 0, set()
    for r in roots:
        paths = [r] if os.path.isfile(r) else [os.path.join(d, f) for d, _, fs in os.walk(r) for f in fs]
        for p in paths:
            files += 1
            with open(p, 'rb') as f:
                data = f.read()
            name = os.path.relpath(p, r) if os.path.isdir(r) else os.path.basename(p)
            if os.path.basename(p) in ALLOWED and any(m in data for m in _MS_MARK):
                microsoft.add(name)
            scan_bytes(name, data, hits)
    real = [(n, a) for n, a in hits if n.split('!')[0] not in microsoft]
    for n, a in hits:
        tag = 'ALLOWED (%s)' % ALLOWED[os.path.basename(n.split('!')[0])] if (n, a) not in real else 'HIT'
        print('%s  %s  %s' % (tag, n, a))
    print('no-input-tooling gate: %d files scanned, %d hit(s)%s' % (files, len(real), '' if real else ' -- PASS'))
    return 1 if real else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
