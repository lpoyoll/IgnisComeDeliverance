-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- KCT fix (icd14) agent C synthetic test, against the real kdcmp.lua under MoonSharp (harness from Test-WO151Synthetic.lua).
--   (a) defaults: the mp_icd_* switches registered, corpse_revive off, the icd14_cfg mirror
--   (b) the crime ledger: a joiner's crime -> this world's witnesses forget (now, +2 s, +6 s, every 10 s); off -> nothing;
--       the session's end -> nothing more (no state was changed: nothing to restore); a stop consumes what it planted
--   (c) the area snapshot (joiner): a door set to the host's state; a door not here retried, applied when it streams in;
--       given up after 5 tries (snapshot_not_found); a host corpse alive here -> icd14_kill; a joiner corpse alive at the
--       host: revive off -> counted only, on -> the engine's Revive; the asks at a join and after a jump
--   (d) the area snapshot (host): doors and corpses around the joiner's spot -> icd14_snap_part

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; LOCKS = {}; MSGS = {}
WUIDS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetCVar = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function(p) return 0 end
WRONG_ENTITY = nil   -- what System.GetEntity answers (the live L4 trap: an unrelated entity for a WUID)
System.GetEntity = function(id) if WRONG_ENTITY then return WRONG_ENTITY end for _, e in pairs(ENTS) do if e.id == id then return e end end return nil end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
Physics.RayWorldIntersection = function() return nil end   -- nothing in the way: every witness in range sees
Game.AddSaveLock = function(name, desc) if LOCKS[name] then return false end LOCKS[name] = true; return true end
Game.RemoveSaveLock = function(name) local had = LOCKS[name] == true; LOCKS[name] = nil; return had end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end,
             GetWorldTimeRatio = function() return 15 end, SetWorldTimeRatio = function() end }
XGenAIModule = mkstub()
XGenAIModule.GetEntityByWUID = function(w) return WUIDS[w] end
XGenAIModule.MakeTableFromType = function(kind) return { kind = kind, information = {} } end
XGenAIModule.SendMessageToEntityData = function(id, kind, t) MSGS[#MSGS + 1] = { id = id, kind = kind, t = t } end

MOUNTED = false
HORSE_WUID = nil
player = { id = 1, this = { id = 777 }, GetName = function(self) return "Dude" end,
           px = 0, py = 0, pz = 0,
           GetWorldPos = function(self) return { x = player.px, y = player.py, z = player.pz } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           GetLinkedParent = function() return nil end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end },
           human = { IsMounted = function() return MOUNTED end, GetHorse = function() return HORSE_WUID end,
                     IsInDialog = function() return false end, IsRiding = function() return nil end },
           inventory = { GetCountOfClass = function() return 0 end } }

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return table.unpack(r)
end

-- @@KDCMP@@

-- Part 2: scenarios.

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function logCount(pat)
    local n = 0
    for _, l in ipairs(LOG) do if string.find(l, pat, 1, true) then n = n + 1 end end
    return n
end
local function lastLog(pat)
    for i = #LOG, 1, -1 do if string.find(LOG[i], pat, 1, true) then return LOG[i] end end
    return nil
end
local function clearLog() LOG = {} end
local function evts(name)
    local out = {}
    for _, l in ipairs(LOG) do
        local a = l:match("%[KCD2%-MP%-EVT%] v1 %d+ " .. name:gsub("_", "%%_") .. " (.*)$")
        if a then out[#out + 1] = a end
    end
    return out
end
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function runTimers()
    local due = TIMERS; TIMERS = {}
    for _, t in ipairs(due) do pcall(t.f) end
end

local NEXTID = 0x0E0000
local function mkEntity(name, x, y, z, cls)
    NEXTID = NEXTID + 1
    local e = { class = cls or "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x or 0, py = y or 0, pz = z or 0, rz = 0,
                writes = {}, anims = {}, dead = false, hp = 100, hidden = false, ctx = {} }
    e.this = { id = "this:" .. name }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetWorldAngles = function(self, t) t = t or {}; t.x, t.y, t.z = 0, 0, self.rz; return t end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z; self.writes[#self.writes + 1] = { x = pos.x, y = pos.y, z = pos.z, at = NOW } end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function(self, slot, anim) self.anims[#self.anims + 1] = tostring(anim) end
    e.GetAnimationLength = function() return 0.8 end
    e.GetDirectionVector = function() return { x = 0, y = 1, z = 0 } end
    e.SetFlags = function() end
    e.Hide = function(self, v) self.hidden = (v == 1 or v == true) end
    e.IsHidden = function(self) return self.hidden end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end,
                GetCurrentAnimationState = function() return "MotionIdle" end, StandUp = function() end }
    e.human = { IsWeaponDrawn = function() return false end, DrawWeapon = function() return true end, HolsterWeapon = function() return true end,
                IsInDialog = function() return e.inDialog == true end, IsSleeping = function() return false end }
    e.soul = { GetId = function() return "userdata: 05000000000001DC" end, IsPublicEnemy = function() return false end,
               IsInCombatMode = function() return false end, GetFactionID = function() return "trosecko_settlements_zelejov_folk" end,
               HasScriptContext = function(self, c) return e.ctx[c] == true end }
    ENTS[name] = e
    return e
end
mkEntityLate = mkEntity

-- the roles, without the session machinery (each scenario says which it is)
ROLE_J, ROLE_H, CRIME_SESSION = false, false, false
KCD2MP_W134JoinerActive = function() return ROLE_J end
KCD2MP_W134HostActive = function() return ROLE_H end
KCD2MP_W139JoinerActive = function() return CRIME_SESSION end
local I = KCD2MP.icd14
local function forgets() local n = 0; for _, m in ipairs(MSGS) do if m.kind == "crime:forgetCrimesData" then n = n + 1 end end; return n end
local function tick() KCD2MP_Icd14Tick(I.gen) end
System.GetEntitiesInSphereByClass = function(pos, r, cls)
    local out = {}
    for _, e in pairs(ENTS) do
        if e.class == cls and ((e.px - pos.x) ^ 2 + (e.py - pos.y) ^ 2 + (e.pz - pos.z) ^ 2) <= r * r then out[#out + 1] = e end
    end
    return out
end
System.GetEntitiesInSphere = function(pos, r)
    local out = {}
    for _, e in pairs(ENTS) do
        if ((e.px - pos.x) ^ 2 + (e.py - pos.y) ^ 2 + (e.pz - pos.z) ^ 2) <= r * r then out[#out + 1] = e end
    end
    return out
end
AnimDoor = {}
function AnimDoor:DoPlayAnimation(direction, forceTime, useSound, customAnim, usePlayerAnim, userId)
    self.nDirection = direction; self.curAnim = "door_anim"; self.plays = (self.plays or 0) + 1
end
function AnimDoor:Lock() self.bLocked = true end
function AnimDoor:Unlock() self.bLocked = false end
local function mkDoor(name, x, y, z, dir, locked)
    local d = mkEntity(name, x, y, z, "AnimDoor")
    setmetatable(d, { __index = AnimDoor })
    d.nDirection = dir or -1; d.bLocked = locked == true
    return d
end

-- (a) defaults
do
    for _, c in ipairs({ "mp_icd_quest_push", "mp_icd_clock_follow", "mp_icd_crime_ledger", "mp_icd_area_snapshot", "mp_icd_corpse_revive", "mp_icd_status" }) do
        check("a: " .. c .. " is registered", CCMDS[c] ~= nil)
    end
    check("a: everything on but corpse_revive", KCD2MP_Icd14IsOn("quest_push") and KCD2MP_Icd14IsOn("clock_follow") and KCD2MP_Icd14IsOn("crime_ledger")
        and KCD2MP_Icd14IsOn("area_snapshot") and not KCD2MP_Icd14IsOn("corpse_revive"))
    clearLog()
    KCD2MP_Icd14Set("crime_ledger", "off")
    check("a: a toggle mirrors itself to the agent", evts("icd14_cfg")[1] == "crime_ledger=off", evts("icd14_cfg")[1])
    KCD2MP_Icd14Set("crime_ledger", "on")
    check("a: an unknown switch is refused", KCD2MP_Icd14Set("nope", "on") == false)
    check("a: no Lua errors at load", #ERRS == 0, ERRS[1])
end

-- (b) the crime ledger
do
    ERRS = {}; MSGS = {}; NOW = 100; ROLE_J = true; CRIME_SESSION = true
    KCD2MP_Icd14Arm()
    player.px, player.py, player.pz = 0, 0, 0
    local w1 = mkEntity("b_witness1", 5, 0, 0)
    local w2 = mkEntity("b_witness2", 10, 0, 0)
    local far = mkEntity("b_far", 300, 0, 0)
    local guard = mkEntity("b_guard", 3, 0, 0)
    KCD2MP.w139.stop = { guard = "b_guard", id = 7 }
    MSGS = {}
    KCD2MP_Icd14CrimeSeen("theft")
    check("b: on: the joiner's crime -> each near witness forgets (not the far one, not the stop's guard)", forgets() == 2, forgets())
    local ids = {}
    for _, m in ipairs(MSGS) do ids[m.id] = true end
    check("b: ...the two witnesses", ids[w1.this.id] and ids[w2.this.id] and not ids[guard.this.id] and not ids[far.this.id])
    check("b: ...logged", logCount("ICD14-CRIME suppressed why=crime:theft") == 1)
    MSGS = {}; NOW = 102.5; tick()
    check("b: the follow-up sweep 2 s later", forgets() == 2, forgets())
    MSGS = {}; NOW = 106.5; tick()
    check("b: the follow-up sweep 6 s later", forgets() == 2, forgets())
    MSGS = {}; NOW = 108; tick()
    check("b: nothing in between", forgets() == 0, forgets())
    MSGS = {}; NOW = 117; tick()
    check("b: the periodic sweep every 10 s", forgets() == 2, forgets())
    KCD2MP.w139.stop = nil
    -- a stop consumes what it planted
    local cleared0 = I.st.crimeCleared
    MSGS = {}
    local ok = KCD2MP_Icd14StopConsumed(guard, { guard = "b_guard", id = 7, planted = { "theft", "trespass" } }, "paid")
    check("b: a stop's end -> its guard copy forgets the planted crimes", ok == true and forgets() == 1 and MSGS[1].id == guard.this.id)
    check("b: ...crime_cleared counts them", I.st.crimeCleared - cleared0 == 2, I.st.crimeCleared - cleared0)
    -- off: today's behaviour
    KCD2MP_Icd14Set("crime_ledger", "off")
    MSGS = {}; NOW = 140
    KCD2MP_Icd14CrimeSeen("theft"); tick()
    check("b: off -> no witness forgets anything", forgets() == 0, forgets())
    check("b: off -> a stop's end sends nothing", KCD2MP_Icd14StopConsumed(guard, { guard = "b_guard", planted = { "theft" } }, "paid") == false and forgets() == 0)
    KCD2MP_Icd14Set("crime_ledger", "on")
    -- the session's end
    CRIME_SESSION = false
    MSGS = {}; NOW = 200
    KCD2MP_Icd14CrimeSeen("theft"); tick(); NOW = 230; tick()
    check("b: after the session -> nothing is sent any more (the game's own crime as before)", forgets() == 0 and #I.crimeFollow == 0, forgets())
    CRIME_SESSION = true; NOW = 260; MSGS = {}; tick()
    check("b: a new session -> the sweeps again (the guard too, its stop over)", forgets() == 3, forgets())
    CRIME_SESSION = false
    ENTS = {}
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (c) the area snapshot on the joiner
do
    ERRS = {}; NOW = 1000; ROLE_J = true; ROLE_H = false; CRIME_SESSION = false
    KCD2MP_Icd14Arm()
    clearLog()
    local W = KCD2MP.w151
    local dn = "AnimDoor[structures/c_door1]_guid"
    local d = mkDoor(dn, 10, 0, 0, -1, false)
    local key = W.doorNameHash(dn)
    local fixed0 = I.st.doorsFixed
    KCD2MP_Icd14SnapApply(1, 1, 1, string.format("D:%s:1:0:10.0:0.0:0.0", key))
    check("c: a door closed here, open at the host -> opened", d.nDirection == 1 and I.st.doorsFixed - fixed0 == 1, d.nDirection)
    check("c: ...one line with old and new", logCount("ICD14-SNAP door " .. key .. " old=dir:-1,locked:false new=dir:1,locked:false set") == 1)
    KCD2MP_Icd14SnapApply(2, 1, 1, string.format("D:%s:1:0:10.0:0.0:0.0", key))
    check("c: the same state again -> nothing", I.st.doorsFixed - fixed0 == 1)
    -- not here yet: retried, applied when it streams in
    local dn2 = "AnimDoor[structures/c_door2]_guid"
    local key2 = W.doorNameHash(dn2)
    local r0, nf0 = I.st.snapRetries, I.st.snapNotFound
    KCD2MP_Icd14SnapApply(3, 1, 1, string.format("D:%s:-1:1:50.0:0.0:0.0", key2))
    check("c: a door not here -> kept for a retry", I.later["D:" .. key2] ~= nil and I.later["D:" .. key2].tries == 1)
    NOW = 1003; tick()
    check("c: ...not before its 6 s", I.st.snapRetries == r0)
    local d2 = mkDoor(dn2, 50, 0, 0, 1, false)
    NOW = 1007; tick()
    check("c: ...it streamed in -> set (closed and locked)", d2.nDirection == -1 and d2.bLocked == true and I.later["D:" .. key2] == nil
        and I.st.snapRetries - r0 == 1, tostring(d2.nDirection) .. " " .. tostring(d2.bLocked))
    -- never here: given up after 5 tries
    local key3 = W.doorNameHash("AnimDoor[nowhere]")
    KCD2MP_Icd14SnapApply(4, 1, 1, string.format("D:%s:1:0:90.0:0.0:0.0", key3))
    for i = 1, 6 do NOW = NOW + 7; tick() end
    check("c: a door never here -> given up after 5 tries", I.later["D:" .. key3] == nil and I.st.snapNotFound - nf0 == 1, I.st.snapNotFound - nf0)
    check("c: ...logged once", logCount("ICD14-SNAP not found here after 5 tries: D:" .. key3) == 1)
    -- WO-151's own "no such door here" feeds the same retry
    KCD2MP_Icd14DoorLater(key3, 1, false, 90, 0, 0)
    check("c: WO-151's not-found door -> the snapshot's retry", I.later["D:" .. key3] ~= nil)
    I.later = {}
    -- a host corpse alive here
    mkEntity("c_bandit", 20, 0, 0)
    clearLog()
    local cf0 = I.st.corpsesFixed
    KCD2MP_Icd14SnapApply(5, 1, 1, "C:c_bandit:20.0:0.0:0.0")
    check("c: dead at the host, alive here -> its death is applied (icd14_kill)", evts("icd14_kill")[1] == "c_bandit" and I.st.corpsesFixed - cf0 == 1)
    KCD2MP_Icd14SnapApply(6, 1, 1, "C:c_bandit:20.0:0.0:0.0")
    check("c: ...asked once within 20 s", #evts("icd14_kill") == 1)
    -- a joiner corpse alive at the host
    local v = mkEntity("c_villager", 25, 0, 0)
    v.dead = true
    v.actor.Revive = function() v.dead = false; v.revived = true end
    local rw0, rv0 = I.st.reviveWanted, I.st.revives
    KCD2MP_Icd14SnapApply(7, 1, 1, "A:c_villager:25.0:0.0:0.0")
    check("c: revive off (default) -> left dead, counted", v.dead and not v.revived and I.st.reviveWanted - rw0 == 1)
    KCD2MP_Icd14Set("corpse_revive", "on")
    KCD2MP_Icd14SnapApply(8, 1, 1, "A:c_villager:25.0:0.0:0.0")
    check("c: revive on -> the engine's Revive", v.revived == true and not v.dead and I.st.revives - rv0 == 1)
    KCD2MP_Icd14Set("corpse_revive", "off")
    -- off: nothing applied
    KCD2MP_Icd14Set("area_snapshot", "off")
    d.nDirection = -1
    KCD2MP_Icd14SnapApply(9, 1, 1, string.format("D:%s:1:0:10.0:0.0:0.0", key))
    check("c: off -> nothing applied", d.nDirection == -1)
    KCD2MP_Icd14Set("area_snapshot", "on")
    -- a bad token is ignored
    check("c: a malformed token is skipped", KCD2MP_Icd14SnapApply(10, 1, 1, "D:zz:1 C:bad\"name:1:2:3 X:1") == 0)
    -- the joiner asks at the join and after a jump
    I.wasJoiner = false; clearLog(); player.px = 0
    tick()
    local a = evts("icd14_snap_ask")
    check("c: the first joiner tick asks (join)", #a == 1 and a[1]:find(" join ", 1, true) ~= nil, a[1])
    player.px = 200; tick()
    a = evts("icd14_snap_ask")
    check("c: a jump of 80 m asks (moved)", #a == 2 and a[2]:find(" moved ", 1, true) ~= nil, a[2])
    ENTS = {}; player.px = 0
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- (d) the host builds the snapshot
do
    ERRS = {}; NOW = 3000; ROLE_J = false; ROLE_H = true
    clearLog()
    local W = KCD2MP.w151
    local dn = "AnimDoor[structures/d_door]_guid"
    mkDoor(dn, 5, 0, 0, 1, true)
    local c = mkEntity("d_corpse", 8, 0, 0); c.dead = true
    mkEntity("d_alive", 9, 0, 0)
    mkEntity("d_far", 500, 0, 0).dead = true
    local n = KCD2MP_Icd14SnapBuild(2, 11, 0, 0, 0, 60, "join", "d_alive,d_corpse")
    local p = evts("icd14_snap_part")
    check("d: the host's door, its corpse and the asked one alive there", n == 3 and #p == 1, tostring(n) .. " " .. tostring(p[1]))
    local s = p[1] or ""
    check("d: ...addressed to the asking peer, one part", s:find("^2 11 1 1 ") ~= nil, s)
    check("d: ...the door item (open, locked)", s:find("D:" .. W.doorNameHash(dn) .. ":1:1:5.0:0.0:0.0", 1, true) ~= nil, s)
    check("d: ...the corpse item", s:find("C:d_corpse:8.0:0.0:0.0", 1, true) ~= nil, s)
    check("d: ...the asked one alive here", s:find("A:d_alive:9.0:0.0:0.0", 1, true) ~= nil and s:find("d_far", 1, true) == nil, s)
    ROLE_H = false
    check("d: not the host -> nothing built", KCD2MP_Icd14SnapBuild(2, 12, 0, 0, 0, 60, "join", "") == 0)
    ENTS = {}
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
