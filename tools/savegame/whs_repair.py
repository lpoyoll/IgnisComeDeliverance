#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-175 S4 -- the save health check and the "stuck quest fight" repair (the Python twin of
dotnet/KcdMp.Client/Wo175SaveHealth.cs, for the maintainer).

    python tools/savegame/whs_repair.py check  <save.whs>
    python tools/savegame/whs_repair.py repair <save.whs> [--out <new.whs>]

The quest graphs (ConceptModule) keep their node state as XML text in the TLV node 01f4/01f6/730a/01f2/036b/20a9:
'ConceptState' NUL, 8 opaque bytes, a NUL, then <Roots>...</Roots>. A quest fight module applies the game's
EnableAllowedWeaponsInQAM (unarmed only) while its _skirmishResult is Started; a fight that ended without a verdict
leaves <_skirmishControler value="Done" /> beside <_skirmishResult value="Started" /> under the same <Nodes>, and the
player never draws a weapon again. The repair sets that element to value="Fled", padded with spaces before '/>' to
the SAME byte length (no TLV length changes, every other stream byte kept), and re-deflates with the original
description and footer tail (Splice-SaveHenry.deflate: zlib level 3, 32 KB blocks, stored when not smaller than
0x8000, the '0XBP' MD5 footer re-signed). An Active controller (a fight in progress) is never touched.

The output is always a NEW file (default: <base>_fixed.whs next to the input, then _fixed2...); an existing target and
the input itself are refused. Exit codes: check 0 healthy, 1 stuck, 2 unreadable; repair 0 written, 1 nothing to
repair or refused, 2 unreadable. The save description carries the account name: it is never printed.
"""
import argparse
import importlib.util
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import whs_dump  # noqa: E402

_spec = importlib.util.spec_from_file_location('splice_save_henry', os.path.join(HERE, '..', 'Splice-SaveHenry.py'))
splice = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(splice)

CONCEPT_PATH = (0x01F4, 0x01F6, 0x730A, 0x01F2, 0x036B, 0x20A9)
HEADER = b'ConceptState\x00'
OPAQUE = 8                      # after the header; left untouched
CONTROLLER = '_skirmishControler'   # the game's spelling
RESULT = '_skirmishResult'
_VALUE = re.compile(r'\svalue\s*=\s*"([^"]*)"')
_STARTED = re.compile(r'^<_skirmishResult\s+value\s*=\s*"Started"\s*/>$')


class Malformed(ValueError):
    pass


def _scan_xml(buf, start, end):
    """[(quest_path, stream_offset, element_length)] of every Done+Started pair. Raises Malformed."""
    hits, stack = [], []   # frame = [name, ctrl, res, res_off, res_len]
    i = start
    while i < end:
        lt = buf.find(b'<', i, end)
        if lt < 0:
            break
        i = lt
        if buf.startswith(b'<!--', i):
            e = buf.find(b'-->', i + 4, end)
            if e < 0:
                raise Malformed('unterminated comment at stream offset %d' % i)
            i = e + 3
            continue
        j, q = i + 1, 0
        while j < end:
            b = buf[j]
            if q:
                if b == q:
                    q = 0
            elif b in (0x22, 0x27):
                q = b
            elif b == 0x3E:
                break
            j += 1
        if j >= end:
            raise Malformed('unterminated tag at stream offset %d' % i)
        tag = buf[i + 1:j]
        if not tag:
            raise Malformed('empty tag at stream offset %d' % i)
        if tag[:1] in (b'?', b'!'):
            i = j + 1
            continue
        if tag[:1] == b'/':
            name = tag[1:].decode('latin-1').strip()
            if not stack or stack[-1][0] != name:
                raise Malformed('</%s> closes %s at stream offset %d' % (name, '<%s>' % stack[-1][0] if stack else 'nothing', i))
            f = stack.pop()
            if f[0] == 'Nodes' and f[1] == 'Done' and f[2] == 'Started':
                path = '/'.join(n[0].lstrip('_') for n in stack[1:] if n[0] != 'Nodes')
                hits.append((path, f[3], f[4]))
        else:
            self_closing = tag.endswith(b'/')
            body = tag[:-1] if self_closing else tag
            sp = 0
            while sp < len(body) and body[sp] > 0x20:
                sp += 1
            name = body[:sp].decode('latin-1')
            if not name:
                raise Malformed('nameless element at stream offset %d' % i)
            if not self_closing:
                stack.append([name, None, None, 0, 0])
            elif stack and stack[-1][0] == 'Nodes' and name in (CONTROLLER, RESULT):
                m = _VALUE.search(body.decode('latin-1'))
                top = stack[-1]
                if m and name == CONTROLLER:
                    if top[1] is None:
                        top[1] = m.group(1)
                elif m and top[2] is None:
                    top[2], top[3], top[4] = m.group(1), i, j - i + 1
        i = j + 1
    if stack:
        raise Malformed('<%s> is never closed' % stack[-1][0])
    return hits


def scan(raw):
    """(ok, why, hits) for an inflated stream. Never raises."""
    try:
        n = splice.rsa.path_get(raw, *CONCEPT_PATH)
        if n is None:
            return False, 'no ConceptModule state node (01f4/01f6/730a/01f2/036b/20a9)', []
        _, o, ln = n
        p0, p1 = o + 6, o + 6 + ln
        if not raw.startswith(HEADER, p0, p1):
            return False, 'the concept node does not start with ConceptState', []
        x = p0 + len(HEADER) + OPAQUE
        while x < p1 and x < p0 + len(HEADER) + OPAQUE + 8 and raw[x] != 0x3C:
            x += 1
        if x >= p1 or raw[x] != 0x3C:
            return False, 'the concept node holds no XML after its header', []
        return True, 'ok', _scan_xml(raw, x, p1)
    except Malformed as e:
        return False, 'the concept XML is malformed: %s' % e, []
    except Exception as e:  # noqa: BLE001 -- a check never raises
        return False, 'the stream does not parse: %s' % e, []


def check(data):
    """{'readable', 'why', 'stuck': [(quest_path, stream_offset)]} for a file image. Never raises."""
    try:
        s = whs_dump.parse(data)
    except Exception as e:  # noqa: BLE001
        return {'readable': False, 'why': 'not a readable save: %s' % e, 'stuck': []}
    ok, why, hits = scan(s['body'])
    if not ok:
        return {'readable': False, 'why': why, 'stuck': []}
    if not s['footer_ok']:
        why = 'ok (the footer MD5 does not verify: repair refuses this file)'
    return {'readable': True, 'why': why, 'stuck': [(p, o) for p, o, _ in hits]}


def fled_element(n):
    head = '<%s value="Fled"' % RESULT
    if n < len(head) + 2:
        raise ValueError('a %d-byte element cannot hold the Fled form' % n)
    return (head + ' ' * (n - len(head) - 2) + '/>').encode('ascii')


def repair(data):
    """(new file bytes, [(quest_path, stream_offset)] fixed). A healthy save comes back unchanged. Raises ValueError."""
    s = whs_dump.parse(data)
    if not s['footer_ok']:
        raise ValueError("the save's footer MD5 does not verify; refusing")
    raw = s['body']
    ok, why, hits = scan(raw)
    if not ok:
        raise ValueError(why)
    fixed = [(p, o) for p, o, _ in hits]
    if not hits:
        return bytes(data), fixed
    desc_len = int.from_bytes(data[4:8], 'little')
    desc_bytes, tail = data[8:8 + desc_len], data[-44:]
    new = bytearray(raw)
    for _, off, ln in hits:
        text = raw[off:off + ln].decode('latin-1')
        if not _STARTED.match(text):
            raise ValueError('unexpected element form at %d: %s' % (off, text))
        new[off:off + ln] = fled_element(ln)
    out = splice.deflate(desc_bytes, bytes(new), tail)
    # the gate: re-signed, inflates to exactly the edited stream, same description and tail, no stuck fight left
    o = whs_dump.parse(out)
    if not o['footer_ok']:
        raise ValueError("the repaired file's footer does not verify")
    if o['body'] != bytes(new):
        raise ValueError('the repaired file does not inflate to the edited stream')
    if out[8:8 + desc_len] != desc_bytes or out[-44:] != tail:
        raise ValueError("the repaired file's description or footer tail changed")
    re_check = check(out)
    if not re_check['readable'] or re_check['stuck']:
        raise ValueError('the repaired file still checks stuck (%s)' % re_check['why'])
    return out, fixed


def repaired_name(original_base, existing):
    """<base>_fixed.whs, then _fixed2.whs ... while taken (case-insensitive)."""
    b = os.path.basename(original_base)
    if b.lower().endswith('.whs'):
        b = b[:-4]
    taken = {os.path.basename(n).lower() for n in existing}
    k = 1
    while True:
        name = '%s_fixed%s.whs' % (b, '' if k == 1 else k)
        if name.lower() not in taken:
            return name
        k += 1


def cmd_check(a):
    with open(a.save, 'rb') as f:
        r = check(f.read())
    print('%s: %s' % (os.path.basename(a.save), r['why'] if r['readable'] else 'UNREADABLE: ' + r['why']))
    for p, o in r['stuck']:
        print('  STUCK FIGHT  %s  (stream offset %d)' % (p, o))
    if r['readable']:
        print('%d stuck fight(s)' % len(r['stuck']))
    return 2 if not r['readable'] else (1 if r['stuck'] else 0)


def cmd_repair(a):
    with open(a.save, 'rb') as f:
        data = f.read()
    r = check(data)
    if not r['readable']:
        print('%s: UNREADABLE: %s' % (os.path.basename(a.save), r['why']))
        return 2
    if not r['stuck']:
        print('%s: nothing to repair' % os.path.basename(a.save))
        return 1
    folder = os.path.dirname(os.path.abspath(a.save))
    out = a.out or os.path.join(folder, repaired_name(a.save, os.listdir(folder)))
    if os.path.abspath(out).lower() == os.path.abspath(a.save).lower():
        print('the output must not be the input; refusing')
        return 1
    if os.path.exists(out):
        print('%s exists; refusing to overwrite' % out)
        return 1
    try:
        new, fixed = repair(data)
    except ValueError as e:
        print('repair refused: %s' % e)
        return 1
    with open(out, 'xb') as f:   # 'x': never overwrites
        f.write(new)
    with open(out, 'rb') as f:
        if f.read() != new:
            print('the written file does not read back')
            return 1
    for p, o in fixed:
        print('  FIXED  %s  (stream offset %d): Started -> Fled' % (p, o))
    print('wrote %s' % out)
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('check'); p.add_argument('save'); p.set_defaults(fn=cmd_check)
    p = sub.add_parser('repair'); p.add_argument('save'); p.add_argument('--out'); p.set_defaults(fn=cmd_repair)
    a = ap.parse_args(argv)
    return a.fn(a)


if __name__ == '__main__':
    sys.exit(main())
