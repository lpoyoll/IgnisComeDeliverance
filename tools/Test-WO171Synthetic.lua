-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-171 synthetic test (the game-side half of the partner marker), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
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
local function lastEvt(name, fromLog)
    for i = #LOG, (fromLog or 0) + 1, -1 do
        local l = LOG[i]
        if l:find("[KCD2-MP-EVT] v1 ", 1, true) and l:find(" " .. name .. " ", 1, true) then return l end
    end
    return nil
end

local W = KCD2MP.w171
-- (C) the commands and their defaults ---------------------------------------------------------------------------------------------
check("C1: the four commands are registered with their templates",
    CCMDS["mp_partner_marker_style"] ~= nil and CCMDS["mp_partner_marker_style"].body == "KCD2MP_W171SetStyle(%line)"
    and CCMDS["mp_marker_dir_offset"] ~= nil and CCMDS["mp_marker_dir_offset"].body == "KCD2MP_W171SetDirOffset(%line)"
    and CCMDS["mp_marker_dir_flip"] ~= nil and CCMDS["mp_marker_dir_flip"].body == "KCD2MP_W171SetDirFlip(%line)"
    and CCMDS["mp_w171_status"] ~= nil)
check("C2: the defaults: rotating, offset 0, no flip", W ~= nil and W.style == "rotating" and W.offset == 0 and W.flip == false)
local mark = #LOG
check("C3: a style is said to the agent as one config event", KCD2MP_W171SetStyle("flat") == true and W.style == "flat"
    and countEvt("w171_cfg", "style=flat offset=0 flip=off", mark) == 1)
check("C4: companion and rotating", KCD2MP_W171SetStyle("companion") == true and W.style == "companion" and KCD2MP_W171SetStyle("rotating") == true and W.style == "rotating")
check("C5: a bad style changes nothing", KCD2MP_W171SetStyle("big") == false and W.style == "rotating")
check("C6: the offset takes 0..7 whole steps only", KCD2MP_W171SetDirOffset("3") == true and W.offset == 3
    and KCD2MP_W171SetDirOffset("8") == false and W.offset == 3 and KCD2MP_W171SetDirOffset("1.5") == false and W.offset == 3
    and KCD2MP_W171SetDirOffset("-1") == false and W.offset == 3 and KCD2MP_W171SetDirOffset("x") == false and W.offset == 3)
mark = #LOG
check("C7: the flip", KCD2MP_W171SetDirFlip("on") == true and W.flip == true and countEvt("w171_cfg", "style=rotating offset=3 flip=on", mark) == 1
    and KCD2MP_W171SetDirFlip("maybe") == false and W.flip == true)
KCD2MP_W171SetDirOffset("0"); KCD2MP_W171SetDirFlip("off")
mark = #LOG
KCD2MP_W171Status()
check("C8: the status line", lastLog("WO171-STATUS style=rotating offset=0 flip=off partner_marker=", mark) ~= nil)
mark = #LOG
KCD2MP_W171Emit()
check("C9: a (re)connected agent can ask for the config", countEvt("w171_cfg", "style=rotating offset=0 flip=off", mark) == 1)

-- (M) the mod menu ---------------------------------------------------------------------------------------------------------------
local M = KCD2MP.w154menu
local item
for _, it in ipairs(M.ITEMS) do if it.id == "marker_style" then item = it end end
check("M1: 'Marker style' is a Display item that remembers its choice and calls the style command's function",
    item ~= nil and item.group == "Display" and item.kind == "onoff" and item.save == "PartnerMarkerArt"
    and item.cmd == "mp_partner_marker_style" and item.fn == "KCD2MP_W171SetStyle")
check("M2: it reads on while the knight shows", item ~= nil and item.get() == true)
KCD2MP_W171SetStyle("off")
check("M3: off = the game's own plain pin (companion)", W.style == "companion" and item.get() == false)
KCD2MP_W171SetStyle("on")
check("M4: on = the knight", W.style == "rotating" and item.get() == true)
check("M5: the saved choice comes back through the menu's restore", KCD2MP_W154MenuRestore("PartnerMarkerArt", "off") == true and W.style == "companion"
    and KCD2MP_W154MenuRestore("PartnerMarkerArt", "on") == true and W.style == "rotating")
local pm
for _, it in ipairs(M.ITEMS) do if it.id == "partner_marker" then pm = it end end
check("M6: 'Partner on the map' stays the first Display switch of the pin", pm ~= nil and pm.save == "PartnerMarker" and pm.fn == "KCD2MP_W164SetPartnerPin")

check("Z: no swallowed Lua errors", #ERRS == 0, ERRS[1])
OUT = table.concat(RESULTS, string.char(10))
