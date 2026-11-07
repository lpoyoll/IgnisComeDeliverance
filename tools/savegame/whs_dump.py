#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-174 Phase 0 -- read-only dump of a KCD2 .whs save. Never shipped, never writes a save.

    python tools/savegame/whs_dump.py info   <save.whs>
    python tools/savegame/whs_dump.py find   <save.whs> <text> [--ctx N] [--max N]
    python tools/savegame/whs_dump.py count  <save.whs> <text>...
    python tools/savegame/whs_dump.py diff   <good.whs> <bad.whs> <text>   (occurrence counts + names near each)

Container (WO-174 findings s1, matches tools/Read-SaveAnatomy.py):
  ff ff ff ff | u32 length of (C_SaveGameDescription XML + 0a 00) | XML | 0a 00 |
  chunks [u32 compressed][u32 uncompressed <= 0x8000][zlib] ... | 64-byte footer
  footer = 'PBX0'-as-bytes 30 58 42 50, MD5 (16), zeros. The MD5 is taken over the whole file up to the footer
  followed by the footer with those 16 bytes zeroed (the CryEngine XMLCPB convention).
The description carries the writing machine's account name (DebugInfoHistory): this tool never prints it.
"""
import argparse
import hashlib
import re
import struct
import sys
import zlib

BUF = 0x8000
FOOTER = 64


def parse(data):
    """-> dict(desc, chunks=[(clen, rlen)], body, footer_ok, stored, computed). Raises ValueError on bad framing."""
    if len(data) < 72 or struct.unpack_from('<I', data, 0)[0] != 0xFFFFFFFF:
        raise ValueError('not a .whs save')
    xml_len = struct.unpack_from('<I', data, 4)[0]
    # the length counts the XML and its own trailing 0a 00
    desc = data[8:8 + xml_len].decode('utf-8', 'replace').rstrip('\n\x00')
    pos = 8 + xml_len
    end = len(data) - FOOTER
    chunks, body = [], bytearray()
    while pos < end:
        if pos + 8 > end:
            raise ValueError('truncated chunk header at %d' % pos)
        clen, rlen = struct.unpack_from('<II', data, pos)
        if rlen > BUF:
            raise ValueError('chunk over the 0x8000 buffer at %d' % pos)
        if clen == 0xFFFFFFFF:   # a stored chunk (WO-132): [-1][raw length][raw bytes], used when zlib would not shrink it
            part = data[pos + 8:pos + 8 + rlen]
            used = rlen
        else:
            if clen > BUF:
                raise ValueError('chunk over the 0x8000 buffer at %d' % pos)
            part = zlib.decompress(data[pos + 8:pos + 8 + clen])
            used = clen
        if len(part) != rlen:
            raise ValueError('chunk at %d holds %d bytes, header says %d' % (pos, len(part), rlen))
        chunks.append((clen, rlen))
        body += part
        pos += 8 + used
    if pos != end:
        raise ValueError('chunks do not end at the footer')
    foot = data[end:]
    ok = foot[:4] == b'0XBP'
    stored = foot[4:20]
    computed = hashlib.md5(data[:end] + foot[:4] + bytes(16) + foot[20:]).digest()
    return {'desc': desc, 'chunks': chunks, 'body': bytes(body), 'footer_ok': ok and stored == computed,
            'stored': stored.hex(), 'computed': computed.hex(), 'size': len(data)}


def load(path):
    with open(path, 'rb') as f:
        return parse(f.read())


def desc_fields(desc):
    """Header fields that do not identify the machine."""
    out = {}
    for k in ('SaveType', 'SaveId', 'LevelName', 'GameReleaseVersion', 'BuildInfo', 'GameMode', 'SaveTime',
              'Description', 'QuestName', 'QuestId'):
        m = re.search(r'\b%s="([^"]*)"' % k, desc)
        if m:
            out[k] = m.group(1)
    return out


_NAME = re.compile(rb'[A-Za-z_][A-Za-z0-9_:\.\-/ ]{2,95}')


def names_near(body, off, span=200):
    """Printable identifier-like strings within span bytes either side of off."""
    lo, hi = max(0, off - span), min(len(body), off + span)
    return [(lo + m.start(), m.group().decode('ascii')) for m in _NAME.finditer(body[lo:hi])]


def occurrences(body, text):
    key = text.encode('utf-8') if isinstance(text, str) else text
    out, i = [], body.find(key)
    while i >= 0:
        out.append(i)
        i = body.find(key, i + 1)
    return out


def cmd_info(a):
    s = load(a.save)
    f = desc_fields(s['desc'])
    print('size %d  chunks %d  body %d' % (s['size'], len(s['chunks']), len(s['body'])))
    print('footer md5 %s  %s' % (s['stored'], 'MATCHES (zeroed-footer convention)' if s['footer_ok'] else 'does not match'))
    for k, v in f.items():
        print('  %s = %s' % (k, v))
    return 0


def cmd_find(a):
    s = load(a.save)
    offs = occurrences(s['body'], a.text)
    print('%d occurrence(s) of %r' % (len(offs), a.text))
    for o in offs[:a.max]:
        near = names_near(s['body'], o, a.ctx)
        print('0x%07x  ' % o + ' | '.join('%s' % n for _, n in near))
    return 0


def cmd_count(a):
    s = load(a.save)
    for t in a.text:
        print('%6d  %s' % (len(occurrences(s['body'], t)), t))
    return 0


def cmd_diff(a):
    for label, path in (('good', a.good), ('bad', a.bad)):
        s = load(path)
        offs = occurrences(s['body'], a.text)
        names = sorted({n for o in offs for _, n in names_near(s['body'], o, 120)})
        print('%s: %d occurrence(s); %d distinct neighbour names' % (label, len(offs), len(names)))
        if label == 'good':
            gnames = set(names)
        else:
            print('  only in bad :', sorted(set(names) - gnames)[:60])
            print('  only in good:', sorted(gnames - set(names))[:60])
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('info'); p.add_argument('save'); p.set_defaults(fn=cmd_info)
    p = sub.add_parser('find'); p.add_argument('save'); p.add_argument('text')
    p.add_argument('--ctx', type=int, default=120); p.add_argument('--max', type=int, default=40); p.set_defaults(fn=cmd_find)
    p = sub.add_parser('count'); p.add_argument('save'); p.add_argument('text', nargs='+'); p.set_defaults(fn=cmd_count)
    p = sub.add_parser('diff'); p.add_argument('good'); p.add_argument('bad'); p.add_argument('text'); p.set_defaults(fn=cmd_diff)
    a = ap.parse_args(argv)
    return a.fn(a)


if __name__ == '__main__':
    sys.exit(main())
