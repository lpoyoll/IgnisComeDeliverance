# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-167 Phase 3 live probes (never shipped): the engine's own console travels on the throwaway save. Unattended: no focus change,
no key press; the game minimised; results read back from kcd.log.

usage:
  python ft167.py state                        the player's position, the world time, wh_pl_FastTravelEnabled
  python ft167.py travel <cmd> <x> <y> <z> [cvar 0|1] [secs]
        one travel: FastTravelTo or FakeFastTravelTo to x y z with the engine's switch at cvar (default: as it is); waits for
        "FastTravel: started" / "ended" (or a refusal), samples the travel map's path array once mid-travel; writes a JSON line
  python ft167.py guid                         System.GetEntityByTextGUID round trip on the player's own GUID
"""
import json, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live

OUT = os.path.join(live.LOGS, 'ft167.jsonl')


def note(code):
    """Run Lua that logs one '[W167FT] <tag> ...' line; returns the line's text after the tag (the newest one)."""
    tag = 'Q%d' % (int(time.time() * 1000) % 100000000)
    live.lua(code.replace('@TAG@', tag))
    for _ in range(40):
        hits = live.tail(live.KLOG, 400, r'\[W167FT\] ' + tag + r' ')
        if hits: return hits[-1].split('[W167FT] ' + tag + ' ', 1)[1]
        time.sleep(0.25)
    return None


def state():
    s = note('local p = player:GetWorldPos(); local t = 0; pcall(function() t = Calendar.GetWorldTime() end); '
             'System.LogAlways(string.format("[W167FT] @TAG@ pos=%.1f,%.1f,%.1f time=%s cvar=%s", p.x, p.y, p.z, tostring(t), '
             'tostring(System.GetCVar("wh_pl_FastTravelEnabled"))))')
    d = {}
    for kv in (s or '').split():
        if '=' in kv:
            k, v = kv.split('=', 1); d[k] = v
    return d


def klog_since(n0):
    """kcd.log lines appended since the line count n0 (the file only grows during a session)."""
    with open(live.KLOG, 'rb') as f:
        lines = f.read().decode('utf-8', 'replace').splitlines()
    return lines[n0:], len(lines)


def klog_len():
    with open(live.KLOG, 'rb') as f:
        return f.read().count(b'\n')


ESCAPE = (2338.0, 2048.3)   # the throwaway save's village centre: where the probe puts the character back after a road event


def escape():
    """A road event set on the travel (an ambush kills an unattended character in seconds): back to the village centre at once."""
    import sit165 as S
    z = S.ground(*ESCAPE)
    for _ in range(3):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (ESCAPE[0], ESCAPE[1], z + 0.3))
        time.sleep(1.0)


def travel(cmd, x, y, z, cvar=None, secs=150, autospawn=None, debug=False):
    res = {'cmd': cmd, 'dest': [x, y, z], 'cvar_set': cvar, 'autospawn': autospawn}
    if cvar is not None:
        live.cmd('wh_pl_FastTravelEnabled %s' % cvar)
        time.sleep(0.5)
    if autospawn is not None:
        live.cmd('wh_pl_RandomEventsAutoSpawnEnabled %s' % autospawn)
    if debug:
        live.cmd('wh_pl_RandomEventsDebug 1')
    s0 = state(); res['before'] = s0
    n0 = klog_len()
    t0 = time.time()
    print(live.cmd('%s %s %s %s' % (cmd, x, y, z)))
    started = ended = None
    refusal = []
    path_sample = None
    seen = []
    event = None
    escaped = False
    while time.time() - t0 < secs:
        new, _ = klog_since(n0)
        for l in new:
            if 'FastTravel' in l or 'fast travel' in l.lower() or 'ApseOpen' in l or 'ApseClose' in l or 'andom event' in l or 'RandomEvent' in l:
                if l not in seen: seen.append(l)
            if started is None and 'FastTravel: started' in l: started = time.time() - t0
            if 'FastTravel: ended' in l: ended = time.time() - t0
            if 'invalid' in l and 'FastTravelTo' in l: refusal.append(l.strip()[:160])
            if 'unable to start fast travel' in l: refusal.append(l.strip()[:160])
            if event is None and 'Setting active random event for fast travel' in l: event = l.strip()[:200]
        if event is not None and ended is not None and not escaped:
            escape(); escaped = True
        if started is not None and path_sample is None and time.time() - t0 > started + 2:
            path_sample = note('local n, first, last = -1, "-", "-"; pcall(function() local a = UIAction.GetArray("ApseMap", -1, "FastTravelPath"); '
                               'if type(a) == "table" then n = #a; first = tostring(a[1]); last = tostring(a[#a]) end end); '
                               'System.LogAlways(string.format("[W167FT] @TAG@ path n=%s first=%s last=%s", tostring(n), first, last))')
        if ended is not None or (started is None and time.time() - t0 > 20):
            break
        time.sleep(0.5)
    if event is not None and not escaped:
        escape(); escaped = True
    time.sleep(2.0)
    s1 = state()
    if debug:
        live.cmd('wh_pl_RandomEventsDebug 0')
    if autospawn is not None:
        live.cmd('wh_pl_RandomEventsAutoSpawnEnabled 1')
    res.update({'started_s': started, 'ended_s': ended, 'refusal': refusal, 'after': s1, 'path': path_sample, 'event': event,
                'escaped': escaped, 'lines': [re.sub(r'\s+', ' ', l)[:200] for l in seen[:40]]})
    try:
        res['time_jump'] = float(s1.get('time', 'nan')) - float(s0.get('time', 'nan'))
    except ValueError:
        res['time_jump'] = None
    open(OUT, 'a').write(json.dumps(res) + '\n')
    print(json.dumps(res, indent=1))
    return res


def guid():
    s = note('local g = "-"; local ok = false; pcall(function() g = player:GetGUID() end); '
             'local e = nil; pcall(function() e = System.GetEntityByTextGUID(tostring(g)) end); '
             'System.LogAlways(string.format("[W167FT] @TAG@ guid=%s type=%s back=%s same=%s", tostring(g), type(g), tostring(e and e.GetName and e:GetName()), '
             'tostring(e ~= nil and e.id == player.id)))')
    print(s)
    open(OUT, 'a').write(json.dumps({'guid_probe': s}) + '\n')


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'state': print(state())
    elif v == 'travel':
        a = sys.argv[2:]
        travel(a[0], a[1], a[2], a[3], a[4] if len(a) > 4 and a[4] != '-' else None, int(a[5]) if len(a) > 5 else 150,
               a[6] if len(a) > 6 and a[6] != '-' else None, len(a) > 7 and a[7] == 'debug')
    elif v == 'guid': guid()
