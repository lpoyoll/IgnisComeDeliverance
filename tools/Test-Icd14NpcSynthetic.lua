-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- KCT fix (icd14) synthetic test, against the real kdcmp.lua under MoonSharp. Harness copied from Test-WO108Synthetic.lua.
--
--   (a) defaults, console commands, the pure rules (lease, handover limit, band, AI-off)
--   (b) B refcount: the engine pause on the first reason, the resume only when the last goes; a W167 fresh resume never
--       wakes a parked body; the deferred hand-off (park -> stream) costs no resume
--   (c) A + D: a stream copy is held (reason puppet) and AI-ignorant; one late heartbeat (4 s) releases nothing; past the
--       lease (6.5 s) it is frozen in place, shown (reason released, no resume); the next packet re-adopts it
--   (d) D: the host's explicit untrack releases at once; the host emits npc_untrack for a name leaving its tracked set
--   (e) C: a handover lends the brain (resume), records reasons meanwhile, takes it back at its end; a hard limit ends it
--       (the kind's own end runs: a conversation's end)
--   (f) safety: the session end (agent disconnect) gives every hold and AI flag back
--   (g) safety: before a save the AI flags come off (and parked bodies are shown), re-applied 3 s later
--   (h) after a load every held name is paused again at once; the MP-HEALTH line says what
--   (i) the MP-HEALTH area=npc line and its keys; switches off give things back
--   (j) E: the host's far band covers the joiner's sweep
--
-- What this proves: the Lua half. What it does NOT prove: the engine's answer to AI.SetIgnorant on a world NPC, or a live
-- two-machine session.

NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; SPHERE = {}
CMDS = {}; DRAWS = {}; SPAWNS = {}; CCMDS = {}; IGN = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function() return SPHERE end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.DrawText = function(x, y, text, size) DRAWS[#DRAWS + 1] = { x = x, y = y, text = tostring(text) } end
System.RemoveEntity = function(eid) for n, e in pairs(ENTS) do if e.id == eid then ENTS[n] = nil end end end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
AI.SetIgnorant = function(id, v) IGN[#IGN + 1] = { id = id, v = v } end
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
WORLD_T = 1000
Calendar = { GetWorldTime = function() return WORLD_T end, SetWorldTime = function(t) WORLD_T = t end }
XGenAIModule = mkstub()

player = { id = 1, GetName = function(self) return "Dude" end,
           GetWorldPos = function() return { x = 0, y = 0, z = 0 } end,
           GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end,
           actor = { GetHealth = function() return 100 end, IsDead = function() return false end } }

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
local function cmdCount(pat)
    local n = 0
    for _, c in ipairs(CMDS) do if string.find(c, pat, 1, true) then n = n + 1 end end
    return n
end
local function ignCount(id, v)
    local n = 0
    for _, x in ipairs(IGN) do if x.id == id and x.v == v then n = n + 1 end end
    return n
end
local function reasons(name) return table.concat(KCD2MP_NpcHeldReasons(name), ",") end

local H = KCD2MP.npcHold

-- ---------------------------------------------------------------- (a)
do
    check("a: every switch ships on", H.on and H.aiOff and H.handover and H.lease and H.bandMatch)
    for _, c in ipairs({ "mp_npc_hold", "mp_copy_ai_off", "mp_npc_handover", "mp_npc_lease", "mp_npc_band_match" }) do
        check("a: console command " .. c .. " registered", CCMDS[c] ~= nil)
    end
    check("a: the lease is 3 heartbeats + 0.5 s (6.5 s at 2 s)", KCD2MP_NpcLeaseS(2.0) == 6.5 and KCD2MP_NpcLeaseS(1.0) == 3.5)
    check("a: the lease is under the DLL's own silence drop (npc_drive kSilenceS = 7 s)", KCD2MP_NpcLeaseS(KCD2MP.npcSync.heartbeatS) < 7.0)
    check("a: the release time is the lease with mp_npc_lease on", KCD2MP_NpcReleaseAfterS() == 6.5)
    H.lease = false
    check("a: ...and the old 3 s with it off", KCD2MP_NpcReleaseAfterS() == 3.0)
    H.lease = true
    check("a: a handover past its limit is due, not at it", KCD2MP_NpcHandoverDue(120.1, 120) and not KCD2MP_NpcHandoverDue(120, 120))
    check("a: the band covers the sweep when matched", KCD2MP_NpcBandM(150, 220, true) == 220 and KCD2MP_NpcBandM(250, 220, true) == 250)
    check("a: ...as before unmatched, and no band in separate worlds", KCD2MP_NpcBandM(150, 220, false) == 150 and KCD2MP_NpcBandM(0, 220, true) == 0)
    check("a: AI off only on a copy here, not lent, not a hostile-engage enemy",
        KCD2MP_NpcAiOffWanted(true, false, false, true, false) and not KCD2MP_NpcAiOffWanted(false, false, false, true, false)
        and not KCD2MP_NpcAiOffWanted(true, true, false, true, false) and not KCD2MP_NpcAiOffWanted(true, false, true, true, false)
        and not KCD2MP_NpcAiOffWanted(true, false, false, false, false) and not KCD2MP_NpcAiOffWanted(true, false, false, true, true))
end

local NEXTID = 0x0D0000
local function mkEntity(name, x, y, z)
    NEXTID = NEXTID + 1
    local e = { class = "NPC", id = "userdata: " .. string.format("%016X", NEXTID), px = x or 0, py = y or 0, pz = z or 0, rz = 0, hidden = false, dead = false, hp = 100 }
    e.GetName = function(self) return name end
    e.GetWorldPos = function(self, t) t = t or {}; t.x, t.y, t.z = self.px, self.py, self.pz; return t end
    e.GetWorldAngles = function(self, t) t = t or {}; t.x, t.y, t.z = 0, 0, self.rz; return t end
    e.SetWorldPos = function(self, pos) self.px, self.py, self.pz = pos.x, pos.y, pos.z end
    e.SetWorldAngles = function(self, a) self.rz = a.z end
    e.StartAnimation = function() end
    e.Hide = function(self, v) self.hidden = (v == 1) end
    e.IsHidden = function(self) return self.hidden end
    e.actor = { IsDead = function() return e.dead end, IsUnconscious = function() return false end, GetHealth = function() return e.hp end }
    e.human = { IsWeaponDrawn = function() return false end, DrawWeapon = function() return true end, HolsterWeapon = function() return true end,
                IsInDialog = function() return false end }
    e.soul = { GetId = function() return "userdata: 05000000000001DC" end, IsPublicEnemy = function() return e.enemy == true end }
    ENTS[name] = e
    return e
end

local function reset()
    H.held = {}; H.lent = {}; H.untracked = {}; H.takenCorpse = {}; H.saveUntil = nil; H.lastTracked = nil
    H.on, H.aiOff, H.handover, H.lease, H.bandMatch = true, true, true, true, true
    if KCD2MP.npcSilence then KCD2MP.npcSilence.tickAt = nil; KCD2MP.npcSilence.agentAt = -1e9; KCD2MP.npcSilence.silent = {} end
    KCD2MP.wo102.authorityHost = true; KCD2MP.wo102.authorityPause = true
    KCD2MP.wo102.npcScanNative = false; KCD2MP.wo102.posNative = false
    KCD2MP.hitSensorOn = false
    KCD2MP.npcPuppets = {}; KCD2MP.npcTracked = {}; KCD2MP.dragging = {}; KCD2MP.dragWatch = {}
    KCD2MP.npcPuppetRunning = false; KCD2MP._npcDivergeUntil = {}
    KCD2MP._npcPaused = {}; KCD2MP._npcPauseExec = {}; KCD2MP._npcEverPaused = {}; KCD2MP._npcResumePending = {}
    KCD2MP.wo1025.resumeDwellS = 10.0
    KCD2MP.npcSync.enabled = true; KCD2MP.npcSyncRunning = true
    KCD2MP.ghosts = {}
    KCD2MP.w131.parked = {}; KCD2MP.w131.aliveAt = nil; KCD2MP.w131.active = false
    ENTS = {}; SPHERE = {}; TIMERS = {}; ERRS = {}; CMDS = {}; IGN = {}
    clearLog()
end

local function tick(name, sx, sy, flags)
    NOW = NOW + 0.05
    KCD2MP_ApplyNpcState(name, sx, sy, 0, 0, 100, flags or 0, 1)
    KCD2MP.npcPuppetRunning = true
    KCD2MP_NpcPuppetTick("ext")
end
local function quiet(s)   -- s seconds with no packet, the puppet tick running
    local stop = NOW + s
    while NOW < stop do NOW = NOW + 0.25; KCD2MP.npcPuppetRunning = true; KCD2MP_NpcPuppetTick("ext") end
end

-- ---------------------------------------------------------------- (b) refcount
do
    reset(); NOW = 100
    mkEntity("b_npc", 5, 0, 0)
    KCD2MP_NpcHold("b_npc", "park")
    check("b: the first reason pauses", cmdCount("wh_ai_PauseNPC b_npc") == 1)
    KCD2MP_NpcHold("b_npc", "join")
    check("b: a second reason pauses nothing more", cmdCount("wh_ai_PauseNPC b_npc") == 1 and reasons("b_npc") == "join,park", reasons("b_npc"))
    KCD2MP_NpcHold("b_npc", "join")
    check("b: the same reason twice counts once", H.held.b_npc.n == 2)
    local ok, _, how = KCD2MP_NpcUnhold("b_npc", "park")
    check("b: one owner's resume while another holds it: kept, no resume", cmdCount("wh_ai_ResumeNPC b_npc") == 0 and how == "kept" and reasons("b_npc") == "join")
    KCD2MP_NpcUnhold("b_npc", "join")
    check("b: the last reason resumes, once", cmdCount("wh_ai_ResumeNPC b_npc") == 1 and H.held.b_npc == nil and reasons("b_npc") == "")
    KCD2MP_NpcUnhold("b_npc", "join")
    check("b: an unhold with nothing held still resumes (idempotent, the owners' old safety)", cmdCount("wh_ai_ResumeNPC b_npc") == 2)
    -- a W167 fresh resume (the host used the copy last) never wakes a parked body
    CMDS = {}
    KCD2MP_NpcHold("b_npc", "park")
    KCD2MP.w167.talk.fresh["b_npc"] = NOW
    KCD2MP_W167FreshResume("b_npc")
    check("b: W167's fresh resume of a parked copy: kept (was the old Node-status inconsistency)", cmdCount("wh_ai_ResumeNPC b_npc") == 0 and reasons("b_npc") == "park")
    -- the deferred hand-off: park -> stream with no resume in between
    CMDS = {}
    KCD2MP_NpcUnhold("b_npc", "park", true)
    check("b: a deferred unhold keeps the engine pause", cmdCount("wh_ai_ResumeNPC b_npc") == 0 and H.held.b_npc ~= nil)
    KCD2MP_NpcHold("b_npc", "puppet")
    NOW = NOW + 2; KCD2MP_NpcHoldTick()
    check("b: ...the next owner took it: no pause, no resume", #CMDS == 0 and reasons("b_npc") == "puppet", table.concat(CMDS, "|"))
    KCD2MP_NpcUnhold("b_npc", "puppet", true)
    NOW = NOW + 0.5; KCD2MP_NpcHoldTick()
    check("b: an orphan inside the grace: still paused", cmdCount("wh_ai_ResumeNPC b_npc") == 0)
    NOW = NOW + 1; KCD2MP_NpcHoldTick()
    check("b: ...past it, nobody took it: resumed", cmdCount("wh_ai_ResumeNPC b_npc") == 1 and H.held.b_npc == nil)
    -- the registry off: every owner straight to the engine, as before
    H.on = false; CMDS = {}
    KCD2MP_NpcHold("b_npc", "park"); KCD2MP_NpcHold("b_npc", "join"); KCD2MP_NpcUnhold("b_npc", "park")
    check("b: mp_npc_hold off: every call goes to the engine (no count)", cmdCount("wh_ai_PauseNPC b_npc") == 2 and cmdCount("wh_ai_ResumeNPC b_npc") == 1 and next(H.held) == nil)
    H.on = true
    check("b: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (c) A + D: the copy, the lease, the freeze, the re-adopt
do
    reset(); NOW = 200
    local e = mkEntity("c_npc", 10, 0, 0)
    tick("c_npc", 10, 0)
    check("c: a stream copy is held for the stream", reasons("c_npc") == "puppet" and cmdCount("wh_ai_PauseNPC c_npc") == 1, reasons("c_npc"))
    check("c: A: ...and AI-ignorant here", ignCount(e.id, 1) == 1 and H.held.c_npc.ai == true)
    quiet(4.0)
    check("c: D: one late heartbeat (4 s quiet) releases nothing (was the 3 s rule)", KCD2MP.npcPuppets.c_npc ~= nil)
    tick("c_npc", 10, 0)
    quiet(6.0)
    check("c: D: 6 s quiet: still the stream's", KCD2MP.npcPuppets.c_npc ~= nil)
    quiet(1.0)
    check("c: D: past the lease (6.5 s): released", KCD2MP.npcPuppets.c_npc == nil and H.stats.releasesLease >= 1)
    check("c: D: ...frozen where it stands, shown: reason released, no resume, not hidden",
        reasons("c_npc") == "released" and cmdCount("wh_ai_ResumeNPC c_npc") == 0 and not e.hidden and KCD2MP._npcPaused.c_npc == nil, reasons("c_npc"))
    check("c: D: ...logged", logCount("MP-NPC-LEASE release npc=c_npc") == 1)
    check("c: A: a released copy stays AI-ignorant (still the host's)", H.held.c_npc.ai == true and ignCount(e.id, 0) == 0)
    local re0 = H.stats.readopts
    CMDS = {}
    tick("c_npc", 10.2, 0)
    check("c: D: the next packet re-adopts it: no resume between, the stream's reason again",
        KCD2MP.npcPuppets.c_npc ~= nil and reasons("c_npc") == "puppet" and cmdCount("wh_ai_ResumeNPC c_npc") == 0 and H.stats.readopts == re0 + 1, reasons("c_npc"))
    -- a released copy nobody re-adopts: given back after releasedVisibleS (no copy guard here)
    quiet(7.0)
    check("c: released again", reasons("c_npc") == "released")
    CMDS = {}
    NOW = NOW + H.releasedVisibleS + 1; KCD2MP_NpcHoldTick()
    check("c: D: past releasedVisibleS without a guard: given back (resumed, AI flag off)",
        cmdCount("wh_ai_ResumeNPC c_npc") == 1 and H.held.c_npc == nil and ignCount(e.id, 0) == 1)
    -- a public enemy under mp_hostile_engage is never made ignorant (WO-147's local fight is the agent's handover)
    reset(); NOW = 300
    local foe = mkEntity("c_foe", 8, 0, 0); foe.enemy = true
    tick("c_foe", 8, 0)
    check("c: A: a hostile-engage enemy copy is held but not ignorant", reasons("c_foe") == "puppet" and ignCount(foe.id, 1) == 0)
    -- the lease off: the old 3 s rule, released the old way (dwell)
    reset(); NOW = 400; H.lease = false
    mkEntity("c_old", 1, 0, 0)
    tick("c_old", 1, 0)
    quiet(3.5)
    check("c: mp_npc_lease off: released after 3 s, the old dwell", KCD2MP.npcPuppets.c_old == nil and KCD2MP._npcResumePending.c_old ~= nil and reasons("c_old") == "puppet")
    H.lease = true
    check("c: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (d) the host's untrack
do
    reset(); NOW = 500
    mkEntity("d_npc", 3, 0, 0)
    tick("d_npc", 3, 0)
    local u0 = H.stats.releasesUntrack
    check("d: the host's untrack is taken", KCD2MP_NpcHostUntracked("d_npc") == true)
    NOW = NOW + 0.1; KCD2MP.npcPuppetRunning = true; KCD2MP_NpcPuppetTick("ext")
    check("d: ...and releases at once (no lease wait), frozen", KCD2MP.npcPuppets.d_npc == nil and H.stats.releasesUntrack == u0 + 1 and reasons("d_npc") == "released")
    -- an untrack older than a later packet does not release
    tick("d_npc", 3, 0)
    H.untracked.d_npc = NOW - 1
    NOW = NOW + 0.1; KCD2MP_NpcPuppetTick("ext")
    check("d: an untrack before the newest packet is stale: kept", KCD2MP.npcPuppets.d_npc ~= nil)
    -- host: a name leaving the tracked set is announced
    LOG = {}
    KCD2MP_NpcUntrackDiff({ "h1", "h2", "h3" })
    check("d: host: the first set announces nothing", logCount("npc_untrack") == 0)
    KCD2MP_NpcUntrackDiff({ "h1", "h3" })
    check("d: host: h2 left the set: npc_untrack h2, once", logCount("npc_untrack h2") == 1 and logCount("npc_untrack") == 1, lastLog("npc_untrack"))
    KCD2MP_NpcUntrackDiff({ "h1", "h3" })
    check("d: host: an unchanged set: nothing", logCount("npc_untrack") == 1)
    check("d: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (e) handovers
do
    reset(); NOW = 600
    local e = mkEntity("e_npc", 2, 0, 0)
    KCD2MP_NpcHold("e_npc", "puppet")
    CMDS = {}
    KCD2MP_NpcHandoverBegin("e_npc", "scene")
    check("e: a handover lends the brain (resume), the AI flag off", cmdCount("wh_ai_ResumeNPC e_npc") == 1 and H.held.e_npc.ai == false)
    check("e: ...the reasons stay recorded, the handover with them", reasons("e_npc") == "puppet,handover:scene", reasons("e_npc"))
    KCD2MP_NpcHold("e_npc", "park")
    check("e: a hold while lent pauses nothing", cmdCount("wh_ai_PauseNPC e_npc") == 0)
    KCD2MP_NpcHandoverEnd("e_npc", "scene", "scene-ended")
    check("e: the end takes it back (paused, AI flag on)", cmdCount("wh_ai_PauseNPC e_npc") == 1 and H.lent.e_npc == nil and H.held.e_npc.ai == true)
    -- the hard limit
    CMDS = {}; LOG = {}
    local t0 = H.stats.handoversTimedOut
    KCD2MP_NpcHandoverBegin("e_npc", "scene")
    NOW = NOW + 100; KCD2MP_NpcHoldTick()
    check("e: inside the limit (scene 125 s): still lent", H.lent.e_npc ~= nil)
    NOW = NOW + 30; KCD2MP_NpcHoldTick()
    check("e: past it: timed out, taken back", H.lent.e_npc == nil and H.stats.handoversTimedOut == t0 + 1 and cmdCount("wh_ai_PauseNPC e_npc") == 1
        and logCount("MP-NPC-HANDOVER timeout npc=e_npc kind=scene") == 1)
    -- a conversation's limit runs the conversation's own end
    local ended = nil
    local was = KCD2MP_W137TalkEndNow
    KCD2MP_W137TalkEndNow = function(n, why) ended = tostring(n) .. ":" .. tostring(why); KCD2MP_NpcHandoverEnd(n, "talk", why) end
    KCD2MP_NpcHandoverBegin("e_npc", "talk")
    NOW = NOW + H.HANDOVER_MAX_S.talk + 1; KCD2MP_NpcHoldTick()
    KCD2MP_W137TalkEndNow = was
    check("e: a talk past its limit: the conversation's own end runs", ended == "e_npc:handover-timeout" and H.lent.e_npc == nil, ended)
    -- two kinds at once: back only when both are over
    CMDS = {}
    KCD2MP_NpcHandoverBegin("e_npc", "talk"); KCD2MP_NpcHandoverBegin("e_npc", "scene")
    KCD2MP_NpcHandoverEnd("e_npc", "talk", "x")
    check("e: two kinds: one ends, still lent", H.lent.e_npc ~= nil and cmdCount("wh_ai_PauseNPC e_npc") == 0)
    KCD2MP_NpcHandoverEnd("e_npc", "scene", "x")
    check("e: ...both over: taken back", H.lent.e_npc == nil and cmdCount("wh_ai_PauseNPC e_npc") == 1)
    -- a corpse: its own dead-body behaviour, for corpse's limit, then paused like any copy
    reset(); NOW = 700
    local c = mkEntity("e_dead", 4, 0, 0); c.dead = true
    check("e: a corpse is not paused (its handover begins)", KCD2MP_W137DeadBody("e_dead", { dead = true }) == true and KCD2MP_NpcHandoverActive("e_dead", "corpse"))
    NOW = NOW + H.HANDOVER_MAX_S.corpse + 1; KCD2MP_NpcHoldTick()
    check("e: ...past its limit it is taken back: the pause gate lets it through", KCD2MP_W137DeadBody("e_dead", { dead = true }) == false and not KCD2MP_NpcHandoverActive("e_dead"))
    -- the switch off: every handover ends now
    reset(); NOW = 800
    mkEntity("e_sw", 1, 0, 0)
    KCD2MP_NpcHold("e_sw", "puppet"); KCD2MP_NpcHandoverBegin("e_sw", "talk")
    CMDS = {}
    KCD2MP_NpcHoldSet("handover", "off")
    check("e: mp_npc_handover off: the handover ends, taken back", H.lent.e_sw == nil and cmdCount("wh_ai_PauseNPC e_sw") == 1)
    check("e: ...and no new one begins", KCD2MP_NpcHandoverBegin("e_sw", "talk") == false)
    H.handover = true
    check("e: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (f) the session's end
do
    reset(); NOW = 900
    local a = mkEntity("f_a", 1, 0, 0); local b = mkEntity("f_b", 2, 0, 0)
    tick("f_a", 1, 0); tick("f_b", 2, 0)
    KCD2MP_NpcHold("f_b", "join")
    KCD2MP_NpcHandoverBegin("f_a", "talk")
    local e0 = H.stats.aiRestoredOnEnd
    CMDS = {}
    KCD2MP_Wo102ResumeAll("agent-disconnect")
    check("f: the agent's disconnect: every hold and handover gone", next(H.held) == nil and next(H.lent) == nil)
    check("f: ...every body resumed", cmdCount("wh_ai_ResumeNPC f_a") >= 1 and cmdCount("wh_ai_ResumeNPC f_b") >= 1)
    check("f: ...every AI flag given back", ignCount(b.id, 0) >= 1 and H.stats.aiRestoredOnEnd >= e0 + 1)
    -- the copy guard going inactive keeps the stream's own holds (the session may go on)
    reset(); NOW = 950
    mkEntity("f_c", 1, 0, 0); mkEntity("f_p", 2, 0, 0)
    tick("f_c", 1, 0)
    KCD2MP_NpcHold("f_p", "park")
    KCD2MP_W131UnparkAll("inactive")
    check("f: the guard inactive: parks given back, the stream's copy still held", H.held.f_p == nil and reasons("f_c") == "puppet")
    check("f: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (g) before a save
do
    reset(); NOW = 1000
    local e = mkEntity("g_npc", 1, 0, 0)
    tick("g_npc", 1, 0)
    local parked = mkEntity("g_park", 9, 0, 0); parked.hidden = true
    KCD2MP.w131.parked.g_park = { at = NOW, why = "test" }
    local s0 = H.stats.aiRestoredOnSave
    KCD2MP_NpcBeforeSave("test")
    check("g: before a save the copy's AI flag comes off", ignCount(e.id, 0) == 1 and H.held.g_npc.ai == false and H.stats.aiRestoredOnSave == s0 + 1)
    check("g: ...a parked copy is shown for the save (the guard re-hides it)", parked.hidden == false and KCD2MP.w131.reassert == true)
    check("g: ...logged", logCount("MP-NPC-HOLD before-save cause=test ai_restored=1 parked_shown=1") == 1, lastLog("before-save"))
    KCD2MP_NpcHold("g_npc", "join")
    check("g: inside the save window nothing sets it again", H.held.g_npc.ai == false)
    NOW = NOW + 3.5; KCD2MP_NpcHoldTick()
    check("g: 3 s later: re-applied", H.held.g_npc.ai == true and ignCount(e.id, 1) == 2)
    -- the mod's own save path calls it
    local hit = false
    local was = KCD2MP_NpcBeforeSave
    KCD2MP_NpcBeforeSave = function() hit = true end
    KCD2MP_W178RequestSave("test")
    KCD2MP_NpcBeforeSave = was
    check("g: the mod's own save (W178RequestSave) runs it", hit)
    KCD2MP.w131.reassert = false
    check("g: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (h) after a load
do
    reset(); NOW = 1100
    mkEntity("h_a", 1, 0, 0); mkEntity("h_b", 2, 0, 0); mkEntity("h_c", 3, 0, 0)
    KCD2MP_NpcHold("h_a", "puppet"); KCD2MP_NpcHold("h_b", "park"); KCD2MP_NpcHold("h_c", "puppet")
    KCD2MP_NpcHandoverBegin("h_c", "talk")
    KCD2MP_NpcHoldEngineForgot()
    CMDS = {}; LOG = {}
    local n = KCD2MP_NpcHoldAfterLoad("test")
    check("h: a load: every held, not lent name paused again at once", n == 2 and cmdCount("wh_ai_PauseNPC h_a") == 1 and cmdCount("wh_ai_PauseNPC h_b") == 1 and cmdCount("wh_ai_PauseNPC h_c") == 0)
    local l = lastLog("MP-HEALTH area=npc after_load=1") or ""
    check("h: the first line after the load says what was re-asserted", l:find("reasserted=2 reasserted_by_reason=park:1,puppet:2 handovers_active=1", 1, true) ~= nil, l)
    check("h: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (i) the health line, the switches
do
    reset(); NOW = 1200
    local e = mkEntity("i_npc", 1, 0, 0)
    tick("i_npc", 1, 0)
    local l = KCD2MP_NpcHoldHealthLine()
    local keys = { "copies=1", "copies_ai_off=1", "holds_by_reason=puppet:1", "handovers_active=0", "handovers_timed_out=", "releases_untrack=",
                   "releases_lease=", "readopts=", "snaps_gt5m=", "fight_events=", "authority_violations=", "ai_restored_on_save=", "ai_restored_on_end=" }
    local miss = nil
    for _, k in ipairs(keys) do if not l:find(k, 1, true) then miss = k end end
    check("h/i: MP-HEALTH area=npc carries every key", l:find("^MP%-HEALTH area=npc ") ~= nil and miss == nil, miss or l)
    LOG = {}; H.healthAt = NOW - 11
    KCD2MP_NpcHoldTick()
    check("i: the line goes out every 10 s", logCount("MP-HEALTH area=npc copies=") == 1)
    -- a > 5 m first packet after a gap snaps (counted)
    local s0 = H.stats.snaps
    tick("i_npc", 30, 0)
    check("i: a > 5 m jump in the stream is counted (snaps_gt5m)", H.stats.snaps == s0 + 1)
    -- mp_copy_ai_off off: the flags come off at once
    IGN = {}
    KCD2MP_NpcHoldSet("aiOff", "off")
    check("i: mp_copy_ai_off off: the flag off at once", ignCount(e.id, 0) == 1 and H.held.i_npc.ai == false)
    KCD2MP_NpcHoldSet("aiOff", "on")
    check("i: ...on: back", ignCount(e.id, 1) == 1 and H.held.i_npc.ai == true)
    -- mp_npc_hold off: everything given back
    CMDS = {}
    KCD2MP_NpcHoldSet("on", "off")
    check("i: mp_npc_hold off: every hold given back", next(H.held) == nil and cmdCount("wh_ai_ResumeNPC i_npc") == 1)
    check("i: a bad value is refused", KCD2MP_NpcHoldSet("lease", "maybe") == false)
    H.on = true
    check("i: no Lua errors", #ERRS == 0, ERRS[1])
end

-- ---------------------------------------------------------------- (j) E: the band
do
    reset()
    check("j: the host's far band covers the joiner's 220 m sweep", KCD2MP_W131FarBandM() == KCD2MP.w131.guardRadiusM and KCD2MP.w131.guardRadiusM == 220)
    H.bandMatch = false
    check("j: mp_npc_band_match off: the old 150 m", KCD2MP_W131FarBandM() == 150)
    H.bandMatch = true
    check("j: no Lua errors", #ERRS == 0, ERRS[1])
end

-- Summary.
local pass, fail = 0, 0
for _, r in ipairs(RESULTS) do if r:sub(1, 4) == "PASS" then pass = pass + 1 else fail = fail + 1 end end
OUT = table.concat(RESULTS, "\n") .. string.format("\n%d passed, %d failed", pass, fail)
