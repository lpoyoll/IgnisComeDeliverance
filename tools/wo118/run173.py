# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-173 live harness (never shipped). Unattended: no focus change, no key press, no game action injected; the game minimised (a
watcher keeps it so); the throwaway playline only. Uses run165.py (relay / peer / agent / stop) and sit165.py's readers.

usage:
  python run173.py c1 <agent.dll> [npc] [blows] [every_s] [row]
        host role: the synthetic joiner's figure beside a local villager; <blows> swings, each followed by its attributed hit after the
        row's lag (jhit). Counts: WO173-HIT verdicts / reactions / knock-outs / fallbacks, the victim's state after each blow, its blows
        on the figure, faults. Writes logs/run173.c1.<tag>.json
  python run173.py soak <agent.dll> <minutes> [npc,npc,npc]
        host role: three figures' worth of blows (one synthetic joiner, three villagers in turn) for <minutes>; WO173-HIT counts,
        faults, switched-off, applied=dup, FRAME ours_us_mean
  python run173.py status         the DLL's and the agent's WO-173 counters
"""
import json, os, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import live
import run165 as R
import sit165 as S

PUNCH = '3e27732b-b152-4a27-97d2-1ebba717a588'   # the WO-165 plans' punch row
PLAIN = 'ebf29add-635b-33d1-a908-ec7a052916d5'   # a longsword CombatAttackGen row (stab, heavy)


def stamp():
    return time.strftime('%H:%M:%S')


def native_window(since, until):
    out = []
    with open(live.NLOG, encoding='utf-8', errors='replace') as f:
        for l in f:
            if len(l) > 12 and l[0] == '[' and l[9] == '.' and since <= l[1:9] <= until: out.append(l)
    return out


def alen(path):
    with open(path, encoding='utf-8', errors='replace') as f:
        return sum(1 for _ in f)


def agent_from(path, n0):
    with open(path, encoding='utf-8', errors='replace') as f:
        return f.read().splitlines()[n0:]


def field(l, k):
    m = re.search(r'\b' + re.escape(k) + r'=(\S+)', l)
    return m.group(1) if m else None


def summarize(nat, ag):
    hits = [l for l in nat if 'WO173-HIT npc=' in l and ' seq=' in l]
    res = {'wo173_hit_lines': len(hits)}
    for k in ('engine', 'reaction', 'fallback', 'knockdown', 'state', 'correction'):
        d = {}
        for l in hits:
            v = field(l, k) or '?'
            if k == 'correction': v = 'none' if v == '-' else ('raise' if v.startswith('-') else 'extra')
            d[v] = d.get(v, 0) + 1
        res[k] = d
    res['stamina_capped'] = sum('(stamina capped)' in l for l in hits)
    res['ko_full_stamina'] = 0
    for l in hits:
        if field(l, 'knockdown') == 'yes':
            sb = field(l, 'st_before') or '0/100'
            a, b = sb.split('/')
            if float(a) >= 0.9 * float(b): res['ko_full_stamina'] += 1
    res['replay_lines'] = sum('WO165-REPLAY seq=' in l for l in nat)
    res['react_armed'] = [l.strip()[15:260] for l in nat if 'WO173-REACT' in l][:3]
    res['strike_lines'] = sum('WO166-STRIKE npc=kcd2mp' in l for l in nat)
    res['strike_refused'] = [l.strip()[15:200] for l in nat if 'WO166-STRIKE' in l and 'refused' in l][:3]
    res['npc_blows_on_figure'] = sum('WO132-HITS npc hit on avatar' in l for l in nat)
    res['faults'] = [l.strip()[15:220] for l in nat if ('FAULT' in l or 'switched OFF' in l or 'faulted' in l) and 'set_send_callback' not in l][:6]
    res['agent_hits'] = sum('MP-W173 hit npc=' in l for l in ag)
    res['agent_applied'] = {k: sum(('applied=' + k) in l for l in ag if 'MP-W173 hit' in l) for k in ('engine', 'raw', 'none')}
    res['agent_samples'] = [l[:300] for l in ag if 'MP-W173' in l][:4]
    res['native_samples'] = [l.strip()[15:420] for l in hits][:5]
    # C3: the mod's own interventions in the same window
    res['c3'] = {'wo136_target_avatar_hit': sum('WO136-TARGET' in l and 'why=avatar-hit' in l for l in nat),
                 'wo136_target_all': sum('WO136-TARGET' in l for l in nat),
                 'wo136_forced': sum('WO136-FORCED' in l for l in nat),
                 'skirmish_adds': sum('WO121-HITS attributed' in l and 'skirmish add' in l for l in nat),
                 'wo136_swing': sum('WO136-SWING' in l for l in nat),
                 'brain_msgs': sum('MP-ATTRIB' in l and 'brain_msg=1' in l for l in ag),
                 'npc_blows_on_figure': res['npc_blows_on_figure'], 'hits': len(hits)}
    return res


def resummarize(since, until, agentlog, tag):
    nat = native_window(since, until)
    ag = agent_from(agentlog, 0)
    res = {'window': [since, until]}
    res.update(summarize(nat, ag))
    frames = [float(m.group(1)) for l in nat for m in [re.search(r'ours_us_mean=([0-9.]+)', l)] if m]
    res['frame_ours_us_mean'] = {'n': len(frames), 'min': min(frames) if frames else None, 'max': max(frames) if frames else None,
                                 'mean': round(sum(frames) / len(frames), 1) if frames else None}
    json.dump(res, open(os.path.join(live.LOGS, 'run173.%s.json' % tag), 'w'), indent=1)
    print(json.dumps(res, indent=1))
    return res


def setup_host(agent_dll, tag, npc, plan_lines, duration):
    R.stop('agent'); R.stop('peer')
    time.sleep(7)
    g = S.ent_pos(npc)
    if not g: raise SystemExit('no npc ' + npc)
    R.agent(tag, agent_dll, [])
    time.sleep(1)
    live.lua('local p = player:GetWorldPos(); player:SetWorldPos({x = p.x + 0.5, y = p.y, z = p.z + 0.2})')   # a state frame for the agent
    time.sleep(10)
    plan = os.path.join(live.LOGS, 'plan.run173.%s.txt' % tag)
    open(plan, 'w').write('\n'.join(plan_lines) + '\n')
    R.peer(tag, plan, ['--duration', str(duration)])
    return R.load_state()['agentlog']


def place_henry_away(g, d=30.0):
    hx, hy = g[0] - d, g[1]
    hz = S.ground(hx, hy)
    for _ in range(3):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (hx, hy, hz + 0.3)); time.sleep(1.5)


def c1(agent_dll, npc='ttkc_man_3', blows=40, every=2.0, row=PUNCH, tag=None, lag_ms=450, hp=8.0, st=20.0, henry_m=30.0):
    tag = tag or 'c1'
    g = S.ent_pos(npc)
    if not g: raise SystemExit('no npc ' + npc)
    place_henry_away(g, henry_m)
    gx, gy = g[0] + 1.6, g[1]
    gz = S.ground(gx, gy)
    t0 = 20
    total = t0 + blows * every + 25
    L = ['# WO-173 C1 (generated by run173.py): host role; the synthetic joiner\'s figure beside %s' % npc, 'start 3',
         'ghost %.2f %.2f %.2f 0 1 50 0.0005 30' % (gx, gy, gz), 'gcombat 5 %d 1' % total,
         'jhit %d %s %s %d %.1f %.1f %.2f %d' % (t0, npc, row, lag_ms, hp, st, every, blows)]
    agentlog = setup_host(agent_dll, 'r173' + tag, npc, L, total + 60)
    peer_t0 = time.time() + 3.0
    # the victim beside the figure just before the first blow (a villager walks its routine; the real joiner stands at what he hits)
    time.sleep(max(0.0, peer_t0 + t0 - 4 - time.time()))
    for _ in range(2):
        live.lua("local e = System.GetEntityByName('%s'); if e then e:SetWorldPos({x=%f, y=%f, z=%f}) end" % (npc, gx - 1.4, gy, S.ground(gx - 1.4, gy) + 0.2))
        time.sleep(0.6)
    a0 = alen(agentlog)
    hp0 = S.health(npc)
    s = stamp()
    trace = []
    tend = peer_t0 + total
    while time.time() < tend:
        f = S.ent_pos('kcd2mp_1'); n = S.ent_pos(npc)
        if f and n: trace.append(round(((f[0] - n[0]) ** 2 + (f[1] - n[1]) ** 2) ** 0.5, 2))
        time.sleep(2.0)
    e = stamp()
    hp1 = S.health(npc)
    live.cmd('mp_w173_status')
    time.sleep(2)
    nat = native_window(s, stamp())
    ag = agent_from(agentlog, a0)
    res = {'npc': npc, 'blows': blows, 'every_s': every, 'row': row, 'window': [s, e], 'npc_hp': [hp0, hp1], 'figure_npc_dist_m': trace}
    res.update(summarize(nat, ag))
    res['status'] = [l[:900] for l in ag if 'MP-WO173-STATS' in l][-1:]
    out = os.path.join(live.LOGS, 'run173.c1.%s.json' % tag)
    json.dump(res, open(out, 'w'), indent=1)
    print(json.dumps(res, indent=1))
    return res


def defend(agent_dll, provoke='ttkc_man_3', targets='ttkc_man_5,ttkc_man_1', blows=40, every=2.0, row=PUNCH, tag='defend', hp=8.0, st=20.0):
    """Three joiner blows on a townsman (a witnessed assault); then the blows go to the townsmen who come to defend him, alternating."""
    g = S.ent_pos(provoke)
    if not g: raise SystemExit('no npc ' + provoke)
    place_henry_away(g)
    gx, gy = g[0] + 1.6, g[1]
    gz = S.ground(gx, gy)
    tg = targets.split(',')
    t0 = 20
    t1 = t0 + 14
    total = t1 + blows * every + 25
    L = ['# WO-173 C1 defend (generated by run173.py)', 'start 3', 'ghost %.2f %.2f %.2f 0 1 50 0.0005 30' % (gx, gy, gz), 'gcombat 5 %d 1' % total,
         'jhit %d %s %s 450 %.1f %.1f 2.0 3' % (t0, provoke, row, hp, st)]
    for i in range(blows):
        L.append('jhit %.1f %s %s 450 %.1f %.1f' % (t1 + i * every, tg[i % len(tg)], row, hp, st))
    agentlog = setup_host(agent_dll, 'r173' + tag, provoke, L, total + 60)
    peer_t0 = time.time() + 3.0
    a0 = alen(agentlog)
    hp0 = {n: S.health(n) for n in [provoke] + tg}
    s = stamp()
    trace = {n: [] for n in tg}
    tend = peer_t0 + total
    while time.time() < tend:
        f = S.ent_pos('kcd2mp_1')
        for n in tg:
            q = S.ent_pos(n)
            if f and q: trace[n].append(round(((f[0] - q[0]) ** 2 + (f[1] - q[1]) ** 2) ** 0.5, 1))
        time.sleep(3.0)
    e = stamp()
    hp1 = {n: S.health(n) for n in [provoke] + tg}
    live.cmd('mp_w173_status')
    time.sleep(2)
    nat = native_window(s, stamp())
    ag = agent_from(agentlog, a0)
    res = {'provoke': provoke, 'targets': tg, 'blows': blows, 'window': [s, e], 'hp_before': hp0, 'hp_after': hp1, 'figure_dist_m': trace}
    res.update(summarize(nat, ag))
    res['status'] = [l[:900] for l in ag if 'MP-WO173-STATS' in l][-1:]
    json.dump(res, open(os.path.join(live.LOGS, 'run173.%s.json' % tag), 'w'), indent=1)
    print(json.dumps(res, indent=1))
    return res


def soak(agent_dll, minutes=15.0, npcs='ttkc_man_3,ttkc_man_5,ttkc_man_1', every=1.8, row=PUNCH, tag='soak', hp=0.5, st=4.0, real_hits=None):
    tg = npcs.split(',')
    g = S.ent_pos(tg[0])
    if not g: raise SystemExit('no npc ' + tg[0])
    place_henry_away(g, 30.0)
    gx, gy = g[0] + 1.6, g[1]
    gz = S.ground(gx, gy)
    t0 = 20
    blows = int(minutes * 60 / every)
    total = t0 + blows * every + 25
    L = ['# WO-173 soak (generated by run173.py)', 'start 3', 'ghost %.2f %.2f %.2f 0 1 50 0.0005 30' % (gx, gy, gz), 'gcombat 5 %d 1' % total]
    for i in range(blows):
        L.append('jhit %.1f %s %s 450 %.1f %.1f' % (t0 + i * every, tg[i % len(tg)], row, hp, st))
    if real_hits is not None: live.cmd('mp_real_hits %s' % real_hits)
    agentlog = setup_host(agent_dll, 'r173' + tag, tg[0], L, total + 60)
    if real_hits is not None: live.cmd('mp_real_hits %s' % real_hits)
    peer_t0 = time.time() + 3.0
    a0 = alen(agentlog)
    s0 = stamp()
    nxt = peer_t0 + t0 - 4
    tend = peer_t0 + total
    while time.time() < tend:
        if time.time() >= nxt:
            for k, n in enumerate(tg):
                ox, oy = (-1.4, 0.0) if k == 0 else (0.0, 1.4) if k == 1 else (0.0, -1.4)
                live.lua("local e = System.GetEntityByName('%s'); if e then e:SetWorldPos({x=%f, y=%f, z=%f}) end" % (n, gx + ox, gy + oy, S.ground(gx + ox, gy + oy) + 0.2))
            nxt = time.time() + 60.0
        time.sleep(2.0)
    e = stamp()
    live.cmd('mp_w173_status')
    time.sleep(2)
    nat = native_window(s0, stamp())
    ag = agent_from(agentlog, a0)
    res = {'npcs': tg, 'minutes': minutes, 'blows_planned': blows, 'window': [s0, e]}
    res.update(summarize(nat, ag))
    frames = [float(m.group(1)) for l in nat for m in [re.search(r'ours_us_mean=([0-9.]+)', l)] if m]
    res['frame_ours_us_mean'] = {'n': len(frames), 'min': min(frames) if frames else None, 'max': max(frames) if frames else None,
                                 'mean': round(sum(frames) / len(frames), 1) if frames else None}
    res['dup_lines'] = sum('applied=dup' in l for l in nat + ag)
    res['switched_off'] = [l.strip()[15:200] for l in nat if 'switched OFF' in l][:5]
    res['status'] = [l[:1100] for l in ag if 'MP-WO173-STATS' in l][-1:]
    json.dump(res, open(os.path.join(live.LOGS, 'run173.%s.json' % tag), 'w'), indent=1)
    print(json.dumps(res, indent=1))
    return res


SAMPLER = ("local w = KCD2MP.w173 or {} KCD2MP.w173 = w; w.tSamp = {npc = '%s', last = nil, untilT = os.clock() + %d}; "
           "if not w.origDown then w.origDown = KCD2MP_W173Down; KCD2MP_W173Down = function(n, s) System.LogAlways(string.format('W173T recv npc=%%s state=%%d t=%%.3f', tostring(n), s, os.clock())); return w.origDown(n, s) end end; "
           "local function tick() local T = w.tSamp; if not T or os.clock() > T.untilT then return end; local e = System.GetEntityByName(T.npc); local u = nil; "
           "pcall(function() u = e and e.actor and e.actor:IsUnconscious() == true end); if u ~= T.last then T.last = u; "
           "System.LogAlways(string.format('W173T copy npc=%%s unconscious=%%s t=%%.3f', T.npc, tostring(u), os.clock())) end; Script.SetTimer(100, tick) end; tick()")


def c2(agent_dll, npc='ttkc_man_3', tag='c2'):
    """Joiner role: the synthetic host streams a copy and tells this joiner its NPC went down and up (W164 NpcDown, plus the stream's
    knocked-out bit as a real host sends it) and two outcomes (W164 RealHit). Measures, from the in-game timer: the copy's unconscious
    state against the arrival of each message."""
    from run167 import release_joiner_hold, nudge, wait_until
    R.stop('agent'); R.stop('peer')
    time.sleep(5)
    release_joiner_hold()
    z = S.ground(2343.8, 2049.14)
    for _ in range(3):
        live.lua('player:SetWorldPos({x=2343.8, y=2049.14, z=%f})' % (z + 0.3)); time.sleep(1.2)
    cz = S.ground(2340.8, 2052.14)
    L = ['# WO-173 C2 joiner role (generated by run173.py)', 'start 10', 'ghost 2349.80 2049.14 109.08 0 1 2 0.0005 30', 'qmode 1 on',
         'hold %s 2340.80 2052.14 %.2f 0 drawn' % (npc, cz),
         'w164 20 7 %s blocked 1 e 8.0 20.0' % npc, 'w164 21 7 %s hit 1 e 8.0 20.0' % npc]
    cycles = [(30, 42), (60, 72), (90, 102)]
    for a, b in cycles:
        L += ['ko %d %d %s' % (a, b, npc), 'w164 %d 8 %s 1' % (a, npc), 'w164 %d 8 %s 0' % (b, npc)]
    total = 125
    path = os.path.join(live.LOGS, 'plan.run173.%s.txt' % tag)
    open(path, 'w').write(chr(10).join(L) + chr(10))
    n0 = S.klen()
    R.peer('r173' + tag, path, ['--duration', str(total + 30), '--claim-host'])
    t0 = time.time() + 10.0
    nudge()
    R.agent('r173' + tag, agent_dll, ['KCDMP_TEST_JOINER_IN_WORLD=1'])
    time.sleep(12)
    live.cmd('mp_leash off')
    live.lua(SAMPLER % (npc, total + 20))
    wait_until(t0 + total)
    k = S.kcd_lines(r'W173T|WO173-DOWN|WO135-KO npc=%s' % npc, n0)
    st = R.load_state()
    ag = [l for l in agent_from(st['agentlog'], 0) if 'WO173-' in l or ('MP-WO135 copy %s' % npc) in l]
    # pair each receipt with the copy's next change to the wanted state
    ev = []
    for l in k:
        m = re.search(r'W173T recv npc=\S+ state=(\d) t=([0-9.]+)', l)
        if m: ev.append(('recv', int(m.group(1)), float(m.group(2))))
        m = re.search(r'W173T copy npc=\S+ unconscious=(\w+) t=([0-9.]+)', l)
        if m: ev.append(('copy', m.group(1) == 'true', float(m.group(2))))
    lat = []
    for i, e in enumerate(ev):
        if e[0] != 'recv' or e[1] == 2: continue
        want = e[1] == 1
        nxt = next((x for x in ev[i + 1:] if x[0] == 'copy' and x[1] == want), None)
        lat.append({'state': e[1], 'recv_t': e[2], 'copy_t': nxt[2] if nxt else None, 'latency_s': round(nxt[2] - e[2], 3) if nxt else None})
    res = {'npc': npc, 'latencies': lat, 'outcomes_logged': [l[:260] for l in ag if 'WO173-OUTCOME' in l][:4],
           'down_lines': [l[:200] for l in ag if 'WO173-DOWN' in l][:8], 'wo135': [l[:200] for l in ag if 'MP-WO135' in l][:8],
           'kcd': k[:40]}
    json.dump(res, open(os.path.join(live.LOGS, 'run173.%s.json' % tag), 'w'), indent=1)
    print(json.dumps(res, indent=1)[:12000])
    R.stop('agent'); R.stop('peer')
    release_joiner_hold()
    return res


def status():
    live.cmd('mp_w173_status')
    time.sleep(2)
    st = R.load_state()
    if 'agentlog' in st:
        print([l[:900] for l in agent_from(st['agentlog'], 0) if 'MP-WO173-STATS' in l][-1:])


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'c1':
        a = sys.argv
        c1(a[2], a[3] if len(a) > 3 else 'ttkc_man_3', int(a[4]) if len(a) > 4 else 40, float(a[5]) if len(a) > 5 else 2.0,
           a[6] if len(a) > 6 else PUNCH, a[7] if len(a) > 7 else None, henry_m=float(os.environ.get('HENRY_M', '30')))
    elif v == 'defend':
        a = sys.argv
        defend(a[2], tag=a[3] if len(a) > 3 else 'defend', blows=int(a[4]) if len(a) > 4 else 40, row=a[5] if len(a) > 5 else PUNCH)
    elif v == 'soak':
        a = sys.argv
        soak(a[2], float(a[3]) if len(a) > 3 else 15.0, tag=a[4] if len(a) > 4 else 'soak',
             hp=float(a[5]) if len(a) > 5 else 0.5, st=float(a[6]) if len(a) > 6 else 4.0, real_hits=a[7] if len(a) > 7 else None)
    elif v == 'c2':
        c2(sys.argv[2], tag=sys.argv[3] if len(sys.argv) > 3 else 'c2')
    elif v == 'resum':
        resummarize(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5])
    elif v == 'status': status()
