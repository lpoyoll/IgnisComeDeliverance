-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-176 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (A) mp_arranged_guard: registered, default ON, told to the agent (w176_cfg), refused words, off/on round trip
--   (B) mp_copy_blocks: registered, default ON, told to the agent, off/on
--   (N) KCD2MP_W176Say: the joiner's notice is logged and shown on the game's own HUD
--   (R) the agent's (re)connect re-sends both switches (the agent keeps no value of its own)
-- Driven by Test-WO176Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-176-findings.md.
NOW = 0
os.clock = function() return NOW end
LOG = {}; TIMERS = {}; ENTS = {}; ERRS = {}; TOASTS = {}; CMDS = {}; CCMDS = {}; LISTENERS = {}

local function mkstub()
    return setmetatable({}, { __index = function(_, k) return function(...) return nil end end })
end
System = mkstub()
System.LogAlways = function(s) LOG[#LOG + 1] = tostring(s) end
System.GetCVarValue = function() return "0" end
CVARS = {}
System.GetCVar = function(n) return CVARS[n] ~= nil and tostring(CVARS[n]) or "0" end
System.SetCVar = function(n, v) CVARS[n] = v end
System.GetEntityByName = function(n) return ENTS[n] end
System.GetEntitiesInSphere = function(p, r) return {} end
System.ExecuteCommand = function(s) CMDS[#CMDS + 1] = tostring(s) end
System.RemoveEntity = function(eid) end
System.AddCCommand = function(name, body, help) CCMDS[name] = { body = tostring(body), help = tostring(help or "") } end
System.GetTerrainElevation = function(p) return 0 end
System.GetFrameTime = function() return 1 / 60 end
Script = mkstub()
Script.SetTimer = function(ms, f) TIMERS[#TIMERS + 1] = { ms = ms, f = f, at = NOW } end
Game = mkstub(); AI = mkstub(); Sound = mkstub(); Physics = mkstub(); Terrain = mkstub()
UIAction = mkstub()
UIAction.CallFunction = function(panel, inst, fn, text) TOASTS[#TOASTS + 1] = tostring(text) end
UIAction.RegisterElementListener = function(tbl, el, inst, ev, fn) LISTENERS[#LISTENERS + 1] = { tbl = tbl, el = el, ev = ev, fn = fn } end
Calendar = mkstub()
Calendar.GetWorldTime = function() return 1000000 end
XGenAIModule = mkstub()
MSGS = {}; ENDCOMBAT = {}
XGenAIModule.MakeTableFromType = function(t) return { kind = t } end
XGenAIModule.SendMessageToEntityData = function(to, kind, t) MSGS[#MSGS + 1] = { to = to, kind = kind, t = t } end
XGenAIModule.TryEndCombat = function(w) ENDCOMBAT[#ENDCOMBAT + 1] = w; return true end
EnvironmentModule = { GetRainIntensity = function() return 0 end }
ItemManager = mkstub()

NEXTID = 5000
PX, PY, PZ = 100, 100, 10
IN_DIALOG = false
ORIG_TALKS = 0
P_DANGER = false
BasicAIActions = { OnTalk = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end,
                   OnChatWithFocus = function(self, user, slot) ORIG_TALKS = ORIG_TALKS + 1 end }
player = { id = 1, class = "Player" }
player.GetName = function() return "Dude" end
player.GetWorldPos = function() return { x = PX, y = PY, z = PZ } end
player.GetWorldAngles = function() return { x = 0, y = 0, z = 0 } end
player.GetDirectionVector = function() return { x = 1, y = 0, z = 0 } end
player.actor = { GetHealth = function() return 100 end, IsDead = function() return false end, StandUp = function() end }
player.human = { IsInDialog = function() return IN_DIALOG end }
player.soul = { RestrictDialog = function() end, GetState = function() return 0 end,
                IsInCombatDanger = function() return P_DANGER end, IsInTenseCircumstance = function() return false end }
ENTS["Dude"] = player

local rawpcall = pcall
pcall = function(f, ...)
    local r = { rawpcall(f, ...) }
    if not r[1] then ERRS[#ERRS + 1] = tostring(r[2]) end
    return unpack(r)
end

-- @@KDCMP@@

local RESULTS = {}
local function check(name, ok, detail)
    RESULTS[#RESULTS + 1] = (ok and "PASS  " or "FAIL  ") .. name .. (detail and ("  [" .. tostring(detail) .. "]") or "")
end
local function lastLog(needle, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do if LOG[i]:find(needle, 1, true) then return LOG[i] end end
    return nil
end
local function countEvt(name, argPrefix, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " " .. (argPrefix or ""), 1, true) then n = n + 1 end
    end
    return n
end
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end

-- (A) mp_arranged_guard
do
    check("A1: mp_arranged_guard is a console command", CCMDS["mp_arranged_guard"] ~= nil and CCMDS["mp_arranged_guard"].body:find("KCD2MP_W176SetArrangedGuard", 1, true) ~= nil)
    check("A2: its help says the default (on) and what it keeps out", CCMDS["mp_arranged_guard"] ~= nil and CCMDS["mp_arranged_guard"].help:find("default on", 1, true) ~= nil
        and CCMDS["mp_arranged_guard"].help:find("arranged fight", 1, true) ~= nil)
    check("A3: default ON", KCD2MP.w176 ~= nil and KCD2MP.w176.arrangedGuard == true)
    local mark = #LOG
    check("A4: off is accepted", KCD2MP_W176SetArrangedGuard("off") == true and KCD2MP.w176.arrangedGuard == false)
    check("A5: the agent is told (w176_cfg arranged_guard=off)", countEvt("w176_cfg", "arranged_guard=off", mark) == 1)
    check("A6: the toggle is said", lastLog("WO176-TOGGLE mp_arranged_guard off", mark) ~= nil)
    mark = #LOG
    check("A7: a wrong word is refused, nothing changes", KCD2MP_W176SetArrangedGuard("maybe") == false and KCD2MP.w176.arrangedGuard == false
        and countEvt("w176_cfg", "", mark) == 0)
    check("A8: on again", KCD2MP_W176SetArrangedGuard("on") == true and KCD2MP.w176.arrangedGuard == true and countEvt("w176_cfg", "arranged_guard=on", mark) == 1)
    mark = #LOG
    check("A9: no word = say the current value and re-send it", KCD2MP_W176SetArrangedGuard("") == true and KCD2MP.w176.arrangedGuard == true
        and countEvt("w176_cfg", "arranged_guard=on", mark) == 1)
    noErrs("A")
end

-- (B) mp_copy_blocks
do
    check("B1: mp_copy_blocks is a console command", CCMDS["mp_copy_blocks"] ~= nil and CCMDS["mp_copy_blocks"].body:find("KCD2MP_W176SetCopyBlocks", 1, true) ~= nil)
    check("B2: default ON", KCD2MP.w176.copyBlocks == true and CCMDS["mp_copy_blocks"].help:find("default on", 1, true) ~= nil)
    local mark = #LOG
    check("B3: off -> the agent is told, the other switch rides along unchanged", KCD2MP_W176SetCopyBlocks("off") == true and KCD2MP.w176.copyBlocks == false
        and countEvt("w176_cfg", "arranged_guard=on copy_blocks=off", mark) == 1)
    check("B4: a wrong word is refused", KCD2MP_W176SetCopyBlocks("2") == false and KCD2MP.w176.copyBlocks == false)
    mark = #LOG
    check("B5: on again", KCD2MP_W176SetCopyBlocks("on") == true and KCD2MP.w176.copyBlocks == true and countEvt("w176_cfg", "arranged_guard=on copy_blocks=on", mark) == 1)
    noErrs("B")
end

-- (N) the joiner's notice
do
    local mark, t0 = #LOG, #TOASTS
    KCD2MP_W176Say("This fight belongs to the story -- your partner fights it.")
    check("N1: the notice is logged", lastLog("WO176-NOTICE This fight belongs to the story", mark) ~= nil)
    check("N2: and shown on the game's own HUD", #TOASTS == t0 + 1 and TOASTS[#TOASTS]:find("belongs to the story", 1, true) ~= nil, TOASTS[#TOASTS])
    noErrs("N")
end

-- (R) the agent's (re)connect re-sends both switches
do
    KCD2MP_W176SetArrangedGuard("off")
    local mark = #LOG
    KCD2MP_W176Emit()
    check("R1: the re-send carries both current values", countEvt("w176_cfg", "arranged_guard=off copy_blocks=on", mark) == 1)
    KCD2MP_W176SetArrangedGuard("on")
    noErrs("R")
end


-- (P) the figures' brain pause near a story fight (A1 live finding)
do
    local function fig(name, x, y, story)
        NEXTID = NEXTID + 1
        local e = { id = NEXTID, class = "NPC" }
        e.GetName = function() return name end
        e.GetWorldPos = function() return { x = x, y = y, z = PZ } end
        e.soul = { HasScriptContext = function(_, c) return story and (c == "combat_arrangedFight") end }
        ENTS[name] = e
        return e
    end
    local f = fig("kcd2mp_1", 100, 100, false)
    local actor = fig("tzel_man_2", 105, 100, true)
    local sphere = { f, actor }
    System.GetEntitiesInSphere = function(p, r) return sphere end
    CMDS = {}
    local mark = #LOG
    KCD2MP_W176Arm()
    check("P1: a story actor near a figure -> its brain is paused (wh_ai_PauseNPC)", #CMDS == 1 and CMDS[1] == "wh_ai_PauseNPC kcd2mp_1"
        and KCD2MP.w176.paused["kcd2mp_1"] ~= nil and lastLog("WO176-PAUSE figure=kcd2mp_1 on story=tzel_man_2", mark) ~= nil, CMDS[1])
    runTimers()
    check("P2: paused once, not again every tick", #CMDS == 1)
    sphere = { f }
    NOW = NOW + 2
    runTimers()
    check("P3: no story actor for less than 5 s -> still paused", #CMDS == 1 and KCD2MP.w176.paused["kcd2mp_1"] ~= nil)
    NOW = NOW + 4
    runTimers()
    check("P4: none for 5 s -> resumed (wh_ai_ResumeNPC)", #CMDS == 2 and CMDS[2] == "wh_ai_ResumeNPC kcd2mp_1" and KCD2MP.w176.paused["kcd2mp_1"] == nil)
    sphere = { f, actor }
    KCD2MP_W176SetArrangedGuard("off")
    runTimers()
    check("P5: mp_arranged_guard off -> never paused", #CMDS == 2)
    KCD2MP_W176SetArrangedGuard("on")
    runTimers()
    check("P6: on again -> paused", #CMDS == 3 and CMDS[3] == "wh_ai_PauseNPC kcd2mp_1")
    KCD2MP_W176SetArrangedGuard("off")
    runTimers()
    check("P7: switched off while paused -> resumed at once", #CMDS == 4 and CMDS[4] == "wh_ai_ResumeNPC kcd2mp_1")
    KCD2MP_W176SetArrangedGuard("on")
    local gen0 = KCD2MP.w176.gen
    KCD2MP_W176Arm()
    check("P8: a load re-arms the chain (a new generation; the old chain stops)", KCD2MP.w176.gen == gen0 + 1)
    TIMERS = {}
    KCD2MP.w176.storySeenAt = NOW
    KCD2MP_W176Arm()
    check("P9: a story actor seen within 30 s -> the next check in 1 s", #TIMERS >= 1 and TIMERS[#TIMERS].ms == 1000, TIMERS[#TIMERS] and TIMERS[#TIMERS].ms)
    TIMERS = {}
    KCD2MP.w176.storySeenAt = NOW - 60
    sphere = { f }
    KCD2MP_W176Arm()
    check("P10: none for 30 s -> the sphere query only every 3 s", #TIMERS >= 1 and TIMERS[#TIMERS].ms == 3000, TIMERS[#TIMERS] and TIMERS[#TIMERS].ms)
    ENTS["kcd2mp_1"] = nil; ENTS["tzel_man_2"] = nil
    System.GetEntitiesInSphere = function(p, r) return {} end
    noErrs("P")
end

OUT = table.concat(RESULTS, "\n")
