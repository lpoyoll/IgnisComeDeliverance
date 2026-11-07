# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-176 live harness (never shipped). Real input only through tools/dev-input/game_input.py (the maintainer's input policy:
the game window only, foreground-checked before every event, logged); the throwaway playline4 only. Uses run165.py (relay / peer /
agent / stop) and live.py.

usage:
  python run176.py bandit <n> <x> <y>          move camp bandit tapo_kasparBandit_<n> to x,y (ground), Henry 4 m south of it facing it
  python run176.py b1host <n> [blows]          (a) the host's own blows on bandit <n> by real mouse attacks (left button), random small
                                               mouse moves between them (the attack direction); WO176-HOSTHIT verdicts, the bandit's
                                               blows on Henry (WO176-HITME); writes logs/run176.b1host.json
  python run176.py b1join <agent.dll> <n> [blows] [row]
                                               (b) the synthetic joiner's figure beside bandit <n>, <blows> attributed real hits
                                               (mp_real_hits); WO173-HIT verdicts, the bandit's blows on the figure; logs/run176.b1join.json
  python run176.py b2 <agent.dll> <case> [swings]
                                               joiner role (KCDMP_TEST_JOINER_IN_WORLD=1, a synthetic host): one armed copy swinging at
                                               the player; case none|block|perfect = the player's own input (none / the block button held
                                               through the swing / the block button pressed at the row's own hit time); WO165-REPLAY
                                               verdicts, prompts screenshot at each window's middle; logs/run176.b2.<case>.json
"""
import json, math, os, random, re, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'dev-input'))
import live
import run165 as R
import game_input as gi

DIST = float(os.environ.get("W176_DIST", "1.8"))   # Henry this far south of the bandit (within sword reach)
PLAIN = 'ebf29add-635b-33d1-a908-ec7a052916d5'   # a longsword CombatAttackGen row (stab, heavy): an armed attack row
SWORD = '00cca9e3-8ef2-46db-8cbf-86ec51930919'   # a longsword item class (MeleeWeapon, class 4)


def stamp():
    return time.strftime('%H:%M:%S')


def nlines_from(n0):
    with open(live.NLOG, encoding='utf-8', errors='replace') as f:
        return f.readlines()[n0:]


def nlen():
    with open(live.NLOG, encoding='utf-8', errors='replace') as f:
        return sum(1 for _ in f)


def ground(x, y):
    live.lua('System.LogAlways("[W176G] " .. tostring(System.GetTerrainElevation({x=%f, y=%f, z=0})))' % (x, y))
    time.sleep(0.4)
    for l in reversed(live.tail(live.KLOG, 30, r'\[W176G\]')):
        try:
            return float(l.split('[W176G] ')[1])
        except Exception:
            pass
    return None


def ent_pos(name):
    live.lua('local e=System.GetEntityByName("%s"); if e then local p=e:GetWorldPos(); System.LogAlways(string.format("[W176P] %s %%.2f %%.2f %%.2f", p.x,p.y,p.z)) else System.LogAlways("[W176P] %s none") end' % (name, name, name))
    time.sleep(0.4)
    for l in reversed(live.tail(live.KLOG, 30, r'\[W176P\] ' + re.escape(name) + ' ')):
        parts = l.split('[W176P] ')[1].split()
        if parts[1] == 'none':
            return None
        return float(parts[1]), float(parts[2]), float(parts[3])
    return None


def bandit(n, x, y):
    """The bandit at x,y; Henry 4 m south of it, facing it (yaw 0 faces +Y)."""
    name = 'tapo_kasparBandit_%d' % n
    z = ground(x, y)
    hz = ground(x, y - DIST)
    for _ in range(3):
        live.lua('local e=System.GetEntityByName("%s"); if e then e:SetWorldPos({x=%f, y=%f, z=%f}) end' % (name, x, y, z + 0.2))
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f}); player:SetWorldAngles({x=0, y=0, z=0})' % (x, y - DIST, hz + 0.3))
        time.sleep(1.2)
    return ent_pos(name)


def bandit_state(name):
    """(hp, knocked out, dead, Henry's hp, bandit x, y, z, distance to Henry)."""
    live.lua('local e=System.GetEntityByName("%s"); local hp,ko,dead=-1,false,false; pcall(function() hp=e.soul:GetState("health") end); pcall(function() ko=e.actor:IsUnconscious() end); pcall(function() dead=e.actor:IsDead() end); local q=e:GetWorldPos(); local p=player:GetWorldPos(); System.LogAlways(string.format("[W176S] %s %%.1f %%s %%s %%.1f %%.2f %%.2f %%.2f %%.2f", hp, tostring(ko), tostring(dead), player.soul:GetState("health"), q.x, q.y, q.z, math.sqrt((q.x-p.x)^2+(q.y-p.y)^2)))' % (name, name))
    time.sleep(0.25)
    for l in reversed(live.tail(live.KLOG, 40, r'\[W176S\] ' + re.escape(name) + ' ')):
        p = l.split('[W176S] ')[1].split()
        return float(p[1]), p[2] == 'true', p[3] == 'true', float(p[4]), float(p[5]), float(p[6]), float(p[7]), float(p[8])
    return None


def drawn():
    live.lua('System.LogAlways("[W176D] " .. tostring(player.human:IsWeaponDrawn()))')
    time.sleep(0.3)
    for l in reversed(live.tail(live.KLOG, 20, r'W176D')):
        return l.strip().endswith('true')
    return False


def keep_close(st):
    """Henry 1.5 m south of the bandit, facing it (yaw 0 faces +Y), when it is farther than 2.5 m (it walks home until it engages)."""
    if not st or st[7] <= 2.5: return False
    live.lua('player:SetWorldPos({x=%f, y=%f, z=%f}); player:SetWorldAngles({x=0, y=0, z=0})' % (st[4], st[5] - 1.5, st[6] + 0.2))
    time.sleep(0.4)
    return True


def b1host(n, clicks=14, every=1.25, tag=None):
    """(a) one engagement: Henry (sword drawn by key 1) attacks bandit n with the left button until it is down / dead or <clicks>; no
    camera moves (the lock-on keeps him facing it once it fights)."""
    tag = tag or 'b1host.%d' % n
    name = 'tapo_kasparBandit_%d' % n
    n0 = nlen()
    t0 = stamp()
    g = gi.Game('wo176-b1a-host-blows')
    g.front()
    g.release_all()          # a button left down by an earlier refused event goes up first (the game is in front)
    sent, stop, heals, moved, res_drawn = 0, '', 0, 0, None
    try:
        live.lua('W176KEEP = (W176KEEP or 0) + 1; local gen = W176KEEP; local function k() if gen ~= W176KEEP then return end; '
                 'if player.soul:GetState("health") < 70 then player.soul:SetState("health", 100); W176HEALS = (W176HEALS or 0) + 1 end; '
                 'Script.SetTimer(400, k) end; k()')   # Henry topped up in-game (the harness's; the bandits' own rules untouched)
        live.cmd('mp_w163_probe pair %s on 1' % name)   # the explicit hostile pair (WO-165 P6's lever): it engages the host and stays
        time.sleep(0.8)
        # one on one: the paired bandit and Henry onto open ground away from its camp (an engaged bandit stays on its opponent)
        if os.environ.get('W176_ARENA', '1') == '1':
            ax, ay = 2700.0, 2600.0
            az = ground(ax, ay); hz = ground(ax, ay - 1.5)
            for _ in range(2):
                live.lua('local e=System.GetEntityByName("%s"); if e then e:SetWorldPos({x=%f, y=%f, z=%f}) end; '
                         'player:SetWorldPos({x=%f, y=%f, z=%f}); player:SetWorldAngles({x=0, y=0, z=0})' % (name, ax, ay, az + 0.2, ax, ay - 1.5, hz + 0.2))
                time.sleep(0.6)
        g.tap('1', 120)          # draw (weapon slot 1)
        time.sleep(2.0)
        if not drawn():
            g.tap('1', 120); time.sleep(2.0)
        res_drawn = drawn()
        moved = 0
        for i in range(clicks):
            if i % 4 == 0:
                st = bandit_state(name)
                if st and (st[1] or st[2] or st[0] <= 0):
                    stop = 'bandit down'; break
                if keep_close(st): moved += 1
            g.click('left', 90)
            sent += 1
            time.sleep(every)
    except gi.NotFocused as e:
        stop = 'focus lost: %s' % e
    finally:
        g.release_all()
        time.sleep(0.8)
        g.back()
    live.lua('W176KEEP = (W176KEEP or 0) + 1')   # the keeper chain stops
    # end the fight before anything loads (a load during a running fight hung the game once, 22:15:35): the pair off, Henry far away
    live.cmd('mp_w163_probe pair %s off' % name)
    live.lua('local p=player:GetWorldPos(); player:SetWorldPos({x=p.x - 250, y=p.y - 250, z=p.z + 40})')
    time.sleep(3.0)
    nat = nlines_from(n0)
    res = {'tag': tag, 'window': [t0, stamp()], 'bandit': name, 'clicks_sent': sent, 'stop': stop, 'henry_topped_up': heals,
           'henry_moved_next_to_it': moved, 'sword_drawn': res_drawn}
    host = [l for l in nat if 'WO176-HOSTHIT' in l]
    res['host_blows_by_verdict'] = {}
    for l in host:
        m = re.search(r'outcome=(\w+)', l)
        if m: res['host_blows_by_verdict'][m.group(1)] = res['host_blows_by_verdict'].get(m.group(1), 0) + 1
    res['hits_on_henry'] = sum('WO176-HITME' in l for l in nat)
    res['seconds'] = round(sent * (every + 0.3), 1)
    res['faults'] = [l.strip()[15:200] for l in nat if ('FAULT' in l or 'faulted' in l) and 'set_send_callback' not in l and 'FAULT-SUM' not in l][:5]
    json.dump(res, open(os.path.join(live.LOGS, 'run176.%s.json' % tag), 'w'), indent=1)
    with open(os.path.join(live.LOGS, 'run176.b1a.jsonl'), 'a') as f:
        f.write(json.dumps(res) + '\n')
    print(json.dumps(res))
    return res


def b1join(n, blows=40, every=2.0, row=PLAIN, tag=None, henry_m=60.0):
    """(b): the synthetic joiner's figure on the same open ground as (a); bandit n is brought next to it right before the first blow and
    kept there until it engages; <blows> swings each followed by its attributed hit (mp_real_hits). The agent already runs as the host."""
    tag = tag or 'b1join.%d' % n
    name = 'tapo_kasparBandit_%d' % n
    ax, ay = 2700.0, 2600.0
    az = ground(ax, ay)
    gx, gy = ax, ay - 1.6
    gz = ground(gx, gy)
    hz = ground(ax - henry_m, ay - henry_m)
    for _ in range(2):
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f})' % (ax - henry_m, ay - henry_m, hz + 0.3)); time.sleep(0.8)
    t0 = 16
    total = t0 + blows * every + 20
    L = ['# WO-176 B1b (generated by run176.py): host role; the synthetic joiner figure on open ground, %s brought to it' % name, 'start 3',
         'ghost %.2f %.2f %.2f 0 1 50 0.0005 30' % (gx, gy, gz), 'gcombat 5 %d 1' % total,
         'jhit %d %s %s 450 10.0 25.0 %.2f %d' % (t0, name, row, every, blows)]
    plan = os.path.join(live.LOGS, 'plan.run176.%s.txt' % tag)
    open(plan, 'w').write('\n'.join(L) + '\n')
    n0 = nlen()
    t0s = stamp()
    R.peer(tag, plan, ['--duration', str(int(total + 10))])
    tp = time.time()
    moves = 0
    while time.time() - tp < total + 12:
        el = time.time() - tp
        if el > t0 - 4:
            st = bandit_state(name)
            if st and (st[1] or st[2] or st[0] <= 0): break
            if st:
                d = math.hypot(st[4] - gx, st[5] - gy)
                if d > 2.6 and moves < 60:
                    live.lua('local e=System.GetEntityByName("%s"); if e then e:SetWorldPos({x=%f, y=%f, z=%f}) end' % (name, ax, ay, az + 0.2))
                    moves += 1
        time.sleep(2.0)
    time.sleep(3)
    nat = nlines_from(n0)
    hits = [l for l in nat if 'WO173-HIT npc=%s seq=' % name in l]
    res = {'tag': tag, 'window': [t0s, stamp()], 'bandit': name, 'blows_planned': blows, 'wo173_hit_lines': len(hits), 'bandit_moved_back': moves}
    for k in ('engine', 'fallback', 'reaction', 'knockdown'):
        d = {}
        for l in hits:
            m = re.search(r'\b' + k + r'=(\S+)', l)
            v = m.group(1) if m else '?'
            d[v] = d.get(v, 0) + 1
        res[k] = d
    res['bandit_blows_on_figure'] = sum('WO132-HITS npc hit on avatar' in l for l in nat)
    res['dup'] = sum('applied=dup' in l for l in nat)
    res['faults'] = [l.strip()[15:200] for l in nat if ('FAULT' in l and 'FAULT-SUM' not in l) or 'faulted' in l or 'switched off' in l][:5]
    res['samples'] = [l.strip()[15:330] for l in hits[:3]]
    json.dump(res, open(os.path.join(live.LOGS, 'run176.%s.json' % tag), 'w'), indent=1)
    with open(os.path.join(live.LOGS, 'run176.b1b.jsonl'), 'a') as f:
        f.write(json.dumps(res) + '\n')
    print(json.dumps(res))
    return res


if __name__ == '__main__':
    v = sys.argv[1]
    if v == 'bandit':
        print(bandit(int(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4])))
    elif v == 'b1host':
        b1host(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 14)
    elif v == 'b1join':
        b1join(int(sys.argv[2]), int(sys.argv[3]) if len(sys.argv) > 3 else 40)
    elif v == 'near':   # Henry 1.5 m south of bandit n where it stands, facing it
        st = bandit_state('tapo_kasparBandit_%d' % int(sys.argv[2]))
        live.lua('player:SetWorldPos({x=%f, y=%f, z=%f}); player:SetWorldAngles({x=0, y=0, z=0})' % (st[4], st[5] - 1.5, st[6] + 0.2))
        time.sleep(1.0)
        print(bandit_state('tapo_kasparBandit_%d' % int(sys.argv[2])))
    elif v == 'ground':
        print(ground(float(sys.argv[2]), float(sys.argv[3])))
    elif v == 'pos':
        print(ent_pos(sys.argv[2]))
