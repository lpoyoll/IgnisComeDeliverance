#!/usr/bin/env python3
# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-175 S4 -- tests for whs_repair.py, on SYNTHETIC saves only (a real save carries the account name).

The concept node sits at its real path (01f4/01f6/730a/01f2/036b/20a9) behind an incompressible (stored) block.
Run: python -I tools/savegame/test_whs_repair.py
"""
import os
import random
import shutil
import struct
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import whs_dump  # noqa: E402
import whs_repair as r  # noqa: E402

OPAQUE = bytes([0x14, 0x70, 0xF4, 0xAC, 0xDC, 0x62, 0x9E, 0x06])
DESC = b'<C_SaveGameDescription SaveId="7" LevelName="trosecko" />\n\x00'
TAIL = bytes((i * 3) & 0xFF for i in range(44))
STARTED = b'<_skirmishResult value="Started" />'
FLED = b'<_skirmishResult value="Fled"    />'
PATH3 = 'konecZabomysichValek/hibernovana_cast/souboj_na_poli'


def tlv(tag, payload):
    return struct.pack('<HI', tag, len(payload)) + payload


def fight(name, ctrl, res, result_first=False):
    c = '\t\t\t\t\t\t<_skirmishControler value="%s" />\n' % ctrl
    rr = '\t\t\t\t\t\t<_skirmishResult value="%s" />\n' % res
    return ('\t\t\t\t<_%s Version="ver_01_05_05">\n\t\t\t\t\t<Nodes>\n'
            '\t\t\t\t\t\t<_fightconfiguration_indifference Version="ver_01_05_05">\n\t\t\t\t\t\t\t<Nodes>\n'
            '\t\t\t\t\t\t\t\t<_timer12 value="Finished" />\n\t\t\t\t\t\t\t</Nodes>\n\t\t\t\t\t\t</_fightconfiguration_indifference>\n'
            % name + (rr + c if result_first else c + rr) + '\t\t\t\t\t</Nodes>\n\t\t\t\t</_%s>\n' % name)


def xml(*modules):
    return ('<Roots>\n\t<_konecZabomysichValek Version="ver_01_05_05">\n\t\t<Nodes>\n\t\t\t<_hibernovana_cast>\n'
            '\t\t\t\t<!-- a comment > with a bracket -->\n'
            '\t\t\t\t<_objectiveVisual10>\n\t\t\t\t\t<Logs>\n\t\t\t\t\t\t<Started UpdateTime="54508044" note="a > b" />\n'
            '\t\t\t\t\t</Logs>\n\t\t\t\t</_objectiveVisual10>\n' + ''.join(modules) +
            '\t\t\t</_hibernovana_cast>\n\t\t</Nodes>\n\t</_konecZabomysichValek>\n</Roots>\n')


def payload(text):
    return b'ConceptState\x00' + OPAQUE + b'\x00' + text.encode('utf-8')


def stream(concept):
    rnd = random.Random(1)
    filler = bytes(rnd.getrandbits(8) for _ in range(40000))
    mid = tlv(0x01F7, b'0123456789') if concept is None else \
        tlv(0x01F6, tlv(0x730A, tlv(0x01F2, tlv(0x036B, tlv(0x20A9, concept)))))
    return struct.pack('<I', 23) + tlv(0x01F4, tlv(0x01F5, filler) + mid + tlv(0x01F8, b'z' * 5000))


def save(text):
    return r.splice.deflate(DESC, stream(payload(text)), TAIL)


class Check(unittest.TestCase):
    def test_finds_the_stuck_pair_with_its_path_and_offset(self):
        f = save(xml(fight('souboj_na_poli', 'Done', 'Started')))
        c = r.check(f)
        self.assertTrue(c['readable'], c['why'])
        self.assertEqual(len(c['stuck']), 1)
        path, off = c['stuck'][0]
        self.assertEqual(path, PATH3)
        self.assertEqual(whs_dump.parse(f)['body'][off:off + len(STARTED)], STARTED)

    def test_active_controller_is_never_stuck(self):
        f = save(xml(fight('souboj_na_poli', 'Active', 'Started')))
        self.assertEqual(r.check(f)['stuck'], [])
        out, fixed = r.repair(f)
        self.assertEqual(out, f)
        self.assertEqual(fixed, [])

    def test_done_with_a_verdict_is_healthy(self):
        for v in ('Won', 'Lost', 'Fled'):
            c = r.check(save(xml(fight('souboj_na_poli', 'Done', v))))
            self.assertTrue(c['readable'], c['why'])
            self.assertEqual(c['stuck'], [], v)

    def test_either_sibling_order(self):
        c = r.check(save(xml(fight('souboj_na_poli', 'Done', 'Started', result_first=True))))
        self.assertEqual([p for p, _ in c['stuck']], [PATH3])

    def test_different_nodes_are_not_a_pair(self):
        t = xml('\t\t\t\t<_outer>\n\t\t\t\t\t<Nodes>\n\t\t\t\t\t\t<_skirmishControler value="Done" />\n'
                + fight('inner', 'Active', 'Started') + '\t\t\t\t\t</Nodes>\n\t\t\t\t</_outer>\n')
        c = r.check(save(t))
        self.assertTrue(c['readable'], c['why'])
        self.assertEqual(c['stuck'], [])

    def test_non_saves_are_not_readable(self):
        for b in (b'', bytes(random.Random(9).getrandbits(8) for _ in range(5000)),
                  b'<Roots><_x><Nodes><_skirmishControler value="Done" /></Nodes></_x></Roots>'):
            c = r.check(b)
            self.assertFalse(c['readable'])
            self.assertEqual(c['stuck'], [])
            with self.assertRaises(ValueError):
                r.repair(b)

    def test_missing_node_header_or_broken_xml_is_not_readable(self):
        self.assertIn('ConceptModule', r.check(r.splice.deflate(DESC, stream(None), TAIL))['why'])
        no_header = r.splice.deflate(DESC, stream(xml(fight('a', 'Done', 'Started')).encode()), TAIL)
        self.assertFalse(r.check(no_header)['readable'])
        full = xml(fight('souboj_na_poli', 'Done', 'Started'))
        for broken in (full[:len(full) // 2], full.replace('</_hibernovana_cast>', '</_other>'), full + '<_x unterminated'):
            c = r.check(save(broken))
            self.assertFalse(c['readable'])
            self.assertIn('malformed', c['why'])


class Repair(unittest.TestCase):
    def test_same_length_and_every_other_byte_stays(self):
        f = save(xml(fight('souboj_na_poli', 'Done', 'Started')))
        before = whs_dump.parse(f)['body']
        out, fixed = r.repair(f)
        self.assertEqual(len(fixed), 1)
        off = fixed[0][1]
        after = whs_dump.parse(out)['body']
        self.assertEqual(len(before), len(after))
        self.assertEqual(before[:off], after[:off])
        self.assertEqual(before[off + len(STARTED):], after[off + len(STARTED):])
        self.assertEqual(after[off:off + len(FLED)], FLED)

    def test_footer_description_and_tail(self):
        out, _ = r.repair(save(xml(fight('souboj_na_poli', 'Done', 'Started'))))
        s = whs_dump.parse(out)
        self.assertTrue(s['footer_ok'])
        self.assertEqual(out[8:8 + len(DESC)], DESC)
        self.assertEqual(out[-44:], TAIL)
        self.assertEqual(r.check(out)['stuck'], [])

    def test_every_stuck_fight_and_not_the_active_one(self):
        f = save(xml(fight('souboj_na_poli', 'Done', 'Started'), fight('rvacka', 'Active', 'Started'), fight('duel', 'Done', 'Started')))
        out, fixed = r.repair(f)
        self.assertEqual([p for p, _ in fixed], [PATH3, 'konecZabomysichValek/hibernovana_cast/duel'])
        body = whs_dump.parse(out)['body']
        self.assertEqual(body.count(FLED), 2)
        self.assertEqual(body.count(STARTED), 1)

    def test_bad_footer_is_refused(self):
        f = bytearray(save(xml(fight('souboj_na_poli', 'Done', 'Started'))))
        f[-60] ^= 1
        with self.assertRaises(ValueError):
            r.repair(bytes(f))
        self.assertIn('footer', r.check(bytes(f))['why'])

    def test_fled_element(self):
        self.assertEqual(r.fled_element(len(STARTED)), FLED)
        self.assertEqual(r.fled_element(31), b'<_skirmishResult value="Fled"/>')
        with self.assertRaises(ValueError):
            r.fled_element(30)

    def test_repaired_name(self):
        self.assertEqual(r.repaired_name('autosave335', ['autosave335.whs']), 'autosave335_fixed.whs')
        self.assertEqual(r.repaired_name('autosave335.WHS', ['AUTOSAVE335_FIXED.whs']), 'autosave335_fixed2.whs')
        self.assertEqual(r.repaired_name('a', ['a_fixed.whs', 'a_fixed2.whs', 'a_fixed3.whs', 'a_fixed5.whs']), 'a_fixed4.whs')


class Cli(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp(prefix='kcdmp-wo175-')

    def tearDown(self):
        shutil.rmtree(self.dir, ignore_errors=True)

    def test_repair_writes_a_new_file_next_to_the_original(self):
        src = os.path.join(self.dir, 'autosave335.whs')
        f = save(xml(fight('souboj_na_poli', 'Done', 'Started')))
        with open(src, 'wb') as h:
            h.write(f)
        with open(os.path.join(self.dir, 'autosave335_fixed.whs'), 'wb') as h:
            h.write(b'\x01\x02\x03')
        self.assertEqual(r.main(['check', src]), 1)
        self.assertEqual(r.main(['repair', src]), 0)
        with open(src, 'rb') as h:
            self.assertEqual(h.read(), f)
        out = os.path.join(self.dir, 'autosave335_fixed2.whs')
        with open(out, 'rb') as h:
            self.assertEqual(r.check(h.read())['stuck'], [])
        self.assertEqual(r.main(['check', out]), 0)
        self.assertEqual(r.main(['repair', out]), 1)                       # nothing to repair
        self.assertEqual(r.main(['repair', src, '--out', out]), 1)         # an existing target is refused
        self.assertEqual(r.main(['repair', src, '--out', src]), 1)         # never in place
        self.assertEqual(sorted(os.listdir(self.dir)), ['autosave335.whs', 'autosave335_fixed.whs', 'autosave335_fixed2.whs'])


if __name__ == '__main__':
    unittest.main()
