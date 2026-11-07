-- Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
-- GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
-- content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
-- WO-174 synthetic test (the game-side halves), against the real kdcmp.lua under MoonSharp (engine stubbed, fake clock).
--   (R) the rules: scene-owned names, partner figures, the weapon pick, the repair plan, the brawl decision
--   (C) mp_combat_autoclean: hostile pairs with a figure removed, logged and counted; off = nothing; the save throttle; a leaving figure
--   (P) mp_repair_combat: stuck weapon set -> equip -> draws; a fine save changes nothing; a scripted brawl is never touched;
--       a figure link that blocks the draw is cleared; the commands
-- Driven by Test-WO174Synthetic.ps1 through the WO-77 MoonSharp driver. Live evidence: docs/WO-174-findings.md.
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
local function countLog(needle, fromLog)
    local n = 0
    for i = (fromLog or 0) + 1, #LOG do if LOG[i]:find(needle, 1, true) then n = n + 1 end end
    return n
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
local function cmdCount(prefix)
    local n = 0
    for _, c in ipairs(CMDS) do if c:sub(1, #prefix) == prefix then n = n + 1 end end
    return n
end
local function noErrs(label) check(label .. ": no swallowed Lua errors", #ERRS == 0, ERRS[1]) end
local function runTimers()
    local t = TIMERS
    TIMERS = {}
    for _, x in ipairs(t) do x.f() end
end


-- the stubs the repair needs ---------------------------------------------------------------------------------------------------
HOSTILE = {}          -- "a|b" -> true
EQUIPPED = nil        -- the item id in the weapon set (nil = the set is empty)
DRAWN = false
LOCKED_BY_LINK = false -- a hostile figure link blocks the draw until it is removed
IN_COMBAT = false
SPHERE = {}
INV = {}
AI.IsPersonallyHostile = function(a, b) return HOSTILE[tostring(a) .. "|" .. tostring(b)] == true end
AI.RemovePersonallyHostile = function(a, b) HOSTILE[tostring(a) .. "|" .. tostring(b)] = nil end
player.player = { IsLockedToOpponent = function(self, id) return HOSTILE["1|" .. tostring(id)] == true end }
System.GetEntitiesInSphere = function(p, r) return SPHERE end
player.id = 1
player.this = { id = "wuid-dude" }
player.soul.IsInCombatDanger = function() return IN_COMBAT end
player.soul.IsInCombatMode = function() return false end
player.human.IsWeaponDrawn = function() return DRAWN end
player.human.DrawWeapon = function()
    if EQUIPPED ~= nil and not LOCKED_BY_LINK then DRAWN = true end
end
player.human.HolsterWeapon = function() DRAWN = false end
player.actor.EquipInventoryItem = function(self, id) EQUIPPED = id end
player.inventory = { GetInventoryTable = function() local t = {}; for i = 1, #INV do t[i] = i end; return t end }
ItemManager.GetItem = function(i) local it = INV[i]; return it and { class = it.name, id = it.id, health = it.health } or nil end
ItemManager.GetItemName = function(c) return c end
local function inv(list) INV = {}; for i, x in ipairs(list) do INV[i] = { name = x[1], health = x[2], id = "item" .. i } end end
local function drive()   -- run every timer to exhaustion (the repair is a chain of timers)
    for _ = 1, 40 do
        if #TIMERS == 0 then break end
        runTimers()
    end
end
local function figure(n, id)
    local e = { id = id, class = "NPC" }
    e.GetName = function() return "kcd2mp_" .. n end
    ENTS["kcd2mp_" .. n] = e
    return e
end
local function fresh()
    HOSTILE = {}; EQUIPPED = nil; DRAWN = false; LOCKED_BY_LINK = false; IN_COMBAT = false; SPHERE = {}; INV = {}
    for n in pairs(ENTS) do ENTS[n] = nil end
    ENTS["Dude"] = player
    TOASTS = {}; TIMERS = {}; CMDS = {}; ENDCOMBAT = {}
    KCD2MP.ghosts = {}
    KCD2MP.w174.busy = false; KCD2MP.w174.autoclean = true; KCD2MP.w174.lastClean = -1e9
    IN_DIALOG = false
end

-- (R) the rules -------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    check("R1: scene-owned names: the neighbours' brawl, a fist fight, a duel", W174.sceneOwned("rvacka_neighbour_1") and W174.sceneOwned("fistfightsChampion_1")
        and W174.sceneOwned("ttac_duel_man") and not W174.sceneOwned("ttac_man_9") and not W174.sceneOwned("kcd2mp_1") and not W174.sceneOwned(nil))
    check("R2: a partner's figure is kcd2mp_<n> and nothing else", W174.isFigure("kcd2mp_2") and W174.isFigure("kcd2mp_10") and not W174.isFigure("kcd2mp_")
        and not W174.isFigure("xkcd2mp_1") and not W174.isFigure("Dude") and not W174.isFigure(nil))
    local best, tier = W174.pickWeapon({
        { id = "a", name = "maceClubSpiked", health = 0.9 }, { id = "b", name = "huntingSwordBasic", health = 0.6 },
        { id = "c", name = "huntingSwordBasic", health = 0.1 }, { id = "d", name = "torch_weapon", health = 1 },
        { id = "e", name = "alias_zachrana_huntingSword", health = 1 }, { id = "f", name = "crossbowLightCheap01", health = 1 },
        { id = "g", name = "repairKit_weaponSmall", health = 1 }, { id = "h", name = "shortswordCleaver", health = 0.03 } })
    check("R3: the pick is the healthiest sword -- never a torch, a quest alias, a crossbow or a repair kit", best and best.id == "b" and tier == 3, best and best.id)
    best = W174.pickWeapon({ { id = "a", name = "axeWork01", health = 0.1 }, { id = "b", name = "maceClub", health = 0.9 }, { id = "c", name = "dagger_a", health = 0.2 } })
    check("R4: no sword: the better axe / mace tier, then the health", best and best.id == "b")
    check("R5: nothing to equip -> nil", W174.pickWeapon({ { id = "a", name = "torch_weapon", health = 1 }, { id = "b", name = "bsmt_ironBad", health = 1 } }) == nil
        and W174.pickWeapon({}) == nil and W174.pickWeapon(nil) == nil)
    local p1 = W174.plan({ drawOk = true, links = 0, combat = false })
    local p2 = W174.plan({ drawOk = false, links = 0, combat = false })
    local p3 = W174.plan({ drawOk = true, links = 2, combat = false })
    check("R6: the plan: a fine save = nothing; a stuck draw, a figure link or a fight = the four steps in order",
        #p1 == 1 and p1[1] == "nothing" and table.concat(p2, ",") == "figure-links,stop-fight,brawl,equip" and table.concat(p3, ",") == "figure-links,stop-fight,brawl,equip")
    check("R7: the brawl decision: a fight with a scene near is left alone; any other is not", W174.brawlDecision({ combat = true, scene = true }) == "belongs-to-a-scene"
        and W174.brawlDecision({ combat = true, scene = false }) == "no-scripted-brawl" and W174.brawlDecision({ combat = false, scene = true }) == "no-scripted-brawl")
    noErrs("R")
end

-- (C) the clean-up ----------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    fresh()
    local f = figure(2, 77)
    HOSTILE["1|77"] = true; HOSTILE["77|1"] = true
    local mark = #LOG
    local n = KCD2MP_W174Clean("load")
    check("C1: both hostile pairs with the figure are removed and counted", n == 2 and HOSTILE["1|77"] == nil and HOSTILE["77|1"] == nil
        and lastLog("WO174-CLEAN reason=load removed=2", mark) ~= nil and KCD2MP.w174.stats.load >= 1)
    mark = #LOG
    check("C2: nothing left: removed=0, still logged", KCD2MP_W174Clean("load") == 0 and lastLog("WO174-CLEAN reason=load removed=0", mark) ~= nil)
    HOSTILE["1|77"] = true
    mark = #LOG
    check("C3: a fight with a figure is ended through the agent", KCD2MP_W174Clean("leave", f) == 1 and countEvt("w154_endfights", "w174-leave", mark) == 1
        and lastLog("fight_ended=yes", mark) ~= nil)
    HOSTILE["1|77"] = true
    KCD2MP_W174SetAutoclean("off")
    mark = #LOG
    check("C4: mp_combat_autoclean off = nothing is touched", KCD2MP_W174Clean("save") == 0 and HOSTILE["1|77"] == true and lastLog("WO174-CLEAN", mark) == nil)
    check("C5: a bad word changes nothing", KCD2MP_W174SetAutoclean("maybe") == false and KCD2MP.w174.autoclean == false)
    KCD2MP_W174SetAutoclean("on")
    mark = #LOG
    KCD2MP_W174Clean("save"); KCD2MP_W174Clean("save")
    check("C6: the mod's save and the game's note of it are one save (a 2 s throttle)", countLog("WO174-CLEAN reason=save", mark) == 1)
    HOSTILE["1|77"] = true
    -- a figure leaving: RemoveGhost cleans its records first
    KCD2MP.ghosts = { ["2"] = { entity = f, entityId = f.id, spawnName = "kcd2mp_2" } }
    mark = #LOG
    pcall(KCD2MP_RemoveGhost, "2")
    check("C7: a figure that leaves (RemoveGhost) is cleaned first", HOSTILE["1|77"] == nil and lastLog("WO174-CLEAN reason=leave removed=1", mark) ~= nil)
    mark = #LOG
    KCD2MP_W174FfEnd("2")
    check("C8: a friendly-fire knockdown asks the agent to take the figure out of the punch's fight", countEvt("w174_endfig", "2", mark) == 1)
    KCD2MP_W174SetAutoclean("off"); mark = #LOG; KCD2MP_W174FfEnd("2")
    check("C9: ... unless the switch is off", countEvt("w174_endfig", "2", mark) == 0)
    KCD2MP_W174SetAutoclean("on")
    noErrs("C")
end

-- (P) the repair ------------------------------------------------------------------------------------------------------------
do
    ERRS = {}
    -- A: the field: nothing equipped, the sword in the inventory
    fresh()
    inv({ { "torch_weapon", 1 }, { "huntingSwordBasic", 0.6 }, { "maceClub", 0.08 } })
    local mark = #LOG
    KCD2MP_W174Repair(""); drive()
    check("P1: the stuck weapon set: the sword is equipped, it draws, he is told", EQUIPPED == "item2" and lastLog("WO174-REPAIR step=equip result=equipped huntingSwordBasic", mark) ~= nil
        and lastLog("WO174-REPAIR step=result result=equipped huntingSwordBasic draw_ok=yes", mark) ~= nil and TOASTS[#TOASTS] ~= nil
        and TOASTS[#TOASTS]:find("Your weapons are free again", 1, true) ~= nil and TOASTS[#TOASTS]:find("save your game now", 1, true) ~= nil)
    check("P2: it holstered what the test drew, and the repair is over", DRAWN == false and KCD2MP.w174.busy == false)
    check("P3: the safe clean-ups 1-2 ran before the equip, each with its draw test", lastLog("step=figure-links result=removed=0", mark) ~= nil
        and lastLog("step=figure-links-retest", mark) ~= nil and lastLog("step=stop-fight result=", mark) ~= nil and lastLog("step=stop-fight-retest", mark) ~= nil
        and #ENDCOMBAT >= 1 and countEvt("w154_endfights", "w174-repair", mark) == 1)
    -- B: a save that was fine
    fresh()
    EQUIPPED = "item1"
    inv({ { "huntingSwordBasic", 0.6 } })
    mark = #LOG
    KCD2MP_W174Repair(""); drive()
    check("P4: a fine save: nothing changed, he is told so", EQUIPPED == "item1" and countEvt("w154_endfights", "", mark) == 0 and #ENDCOMBAT == 0
        and lastLog("WO174-REPAIR step=nothing-to-repair", mark) ~= nil and TOASTS[#TOASTS]:find("Nothing was wrong", 1, true) ~= nil and DRAWN == false)
    -- C: a scripted brawl that holds him: never touched
    fresh()
    IN_COMBAT = true
    SPHERE = { { GetName = function() return "so_tournamentCrowdSpot" end }, { actor = {}, GetName = function() return "rvacka_neighbour_1" end } }
    inv({ { "huntingSwordBasic", 0.6 } })
    mark = #LOG
    KCD2MP_W174Repair(""); drive()
    check("P5: a stuck fight that belongs to a scene is left alone: no equip, the reason said",
        EQUIPPED == nil and lastLog("WO174-REPAIR step=brawl result=belongs-to-a-scene", mark) ~= nil
        and TOASTS[#TOASTS]:find("belongs to a quest scene", 1, true) ~= nil and TOASTS[#TOASTS]:find("rvacka_neighbour_1", 1, true) ~= nil and KCD2MP.w174.busy == false)
    -- D: the figure's hostile link is what blocks the draw
    fresh()
    figure(1, 55)
    HOSTILE["1|55"] = true; HOSTILE["55|1"] = true
    EQUIPPED = "item1"; LOCKED_BY_LINK = true
    inv({ { "huntingSwordBasic", 0.6 } })
    local oldRemove = AI.RemovePersonallyHostile
    AI.RemovePersonallyHostile = function(a, b) oldRemove(a, b); if next(HOSTILE) == nil then LOCKED_BY_LINK = false end end
    mark = #LOG
    KCD2MP_W174Repair(""); drive()
    AI.RemovePersonallyHostile = oldRemove
    check("P6: a hostile link with a figure that blocked the draw: removed (2 pairs), freed at step 1, no equip needed",
        lastLog("step=figure-links result=removed=2", mark) ~= nil and lastLog("step=figure-links-retest result=drawn draw_ok=yes", mark) ~= nil
        and lastLog("step=equip", mark) == nil and TOASTS[#TOASTS]:find("Your weapons are free again", 1, true) ~= nil)
    -- E: nothing to equip and nothing else wrong: tells him honestly
    fresh()
    inv({ { "torch_weapon", 1 } })
    mark = #LOG
    KCD2MP_W174Repair(""); drive()
    check("P7: no weapon in the inventory: not freed, and he is told what to do", lastLog("step=equip result=no-melee-weapon-in-the-inventory draw_ok=no", mark) ~= nil
        and TOASTS[#TOASTS]:find("still stuck", 1, true) ~= nil and TOASTS[#TOASTS]:find("Send your logs", 1, true) ~= nil)
    -- F: busy, dialogue
    fresh()
    inv({ { "huntingSwordBasic", 0.6 } })
    KCD2MP_W174Repair("")
    mark = #LOG
    check("P8: a second run while one is running is refused", KCD2MP_W174Repair("") == false and lastLog("WO174-REPAIR already running", mark) ~= nil)
    drive()
    fresh()
    IN_DIALOG = true
    mark = #LOG
    KCD2MP_W174Repair(""); drive()
    check("P9: in a dialogue: not now, nothing changed", EQUIPPED == nil and TOASTS[#TOASTS]:find("Not now", 1, true) ~= nil and KCD2MP.w174.busy == false)
    check("P10: the commands exist", CCMDS["mp_repair_combat"] ~= nil and CCMDS["mp_repair_combat"].body == "KCD2MP_W174Repair(%line)"
        and CCMDS["mp_combat_autoclean"] ~= nil and CCMDS["mp_combat_autoclean"].body == "KCD2MP_W174SetAutoclean(%line)")
    noErrs("P")
end

-- (A) the re-apply after a load ----------------------------------------------------------------------------------------------
do
    ERRS = {}
    fresh()
    player.inventory.FindItem = function(self, cls) for i, it in ipairs(INV) do if it.name == cls then return { id = it.id } end end return nil end
    inv({ { "torch_weapon", 1 }, { "huntingSwordBasic", 0.6 } })
    KCD2MP.w174.fixedWith = nil; KCD2MP.w174.afterLoadAt = -1e9; KCD2MP.w174.autofix = true
    local mark = #LOG
    KCD2MP_W174AfterLoad("settle"); drive()
    check("A1: no repair found a lock in this session: a load changes nothing", EQUIPPED == nil and lastLog("autofix-after-load", mark) == nil)
    KCD2MP_W174Repair(""); drive()
    check("A2: the repair remembers what freed him", KCD2MP.w174.fixedWith ~= nil and KCD2MP.w174.fixedWith.name == "huntingSwordBasic" and KCD2MP.w174.fixedWith.class == "huntingSwordBasic")
    EQUIPPED = nil; KCD2MP.w174.afterLoadAt = -1e9
    mark = #LOG
    KCD2MP_W174AfterLoad("reload"); drive()
    check("A3: a load re-equips it silently (no draw), one log line", EQUIPPED == "item2" and DRAWN == false
        and lastLog("WO174-REPAIR step=autofix-after-load result=equipped huntingSwordBasic draw_ok=- why=reload", mark) ~= nil and KCD2MP.w174.stats.refixed == 1)
    EQUIPPED = nil
    KCD2MP_W174AfterLoad("settle"); drive()
    check("A4: the agent's settle and Lua's own reload detector are one load (20 s)", EQUIPPED == nil and KCD2MP.w174.stats.refixed == 1)
    KCD2MP.w174.afterLoadAt = -1e9
    KCD2MP_W174SetAutofix("off")
    KCD2MP_W174AfterLoad("settle"); drive()
    check("A5: mp_weapon_autofix off: nothing", EQUIPPED == nil and KCD2MP_W174SetAutofix("maybe") == false and KCD2MP.w174.autofix == false)
    KCD2MP_W174SetAutofix("on"); KCD2MP.w174.afterLoadAt = -1e9
    inv({ { "torch_weapon", 1 } })
    mark = #LOG
    KCD2MP_W174AfterLoad("settle"); drive()
    check("A6: the weapon is gone from the inventory: said, nothing equipped", EQUIPPED == nil and lastLog("autofix-after-load result=refused: ", mark) ~= nil)
    ERRS = {}   -- the refusal is the pcall inside the function doing its job, not a swallowed fault
    check("A7: the switch is registered", CCMDS["mp_weapon_autofix"] ~= nil and CCMDS["mp_weapon_autofix"].body == "KCD2MP_W174SetAutofix(%line)")
    noErrs("A")
end

OUT = table.concat(RESULTS, "\n")
