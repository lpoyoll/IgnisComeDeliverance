# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    KCT fix (icd14): synthetic test for the NPC pure-puppet Lua half -- the suspend registry (refcount), the copies'
    AI flag, handovers with hard limits, the lease and the host's untrack, the band match, and the safety restores
    (session end, before a save, after a load). No game, relay or agent needed.
.DESCRIPTION
    Same driver as Test-NpcSmoothSynthetic.ps1 (the real kdcmp.lua under MoonSharp, engine stubbed, fake clock);
    scenario file Test-Icd14NpcSynthetic.lua.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-Icd14NpcSynthetic.ps1
#>
[CmdletBinding()]
param(
    [string] $KdcmpLua = '',
    [string] $MoonSharpVersion = '2.0.0.0'
)
$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $toolsDir 'Test-NpcSmoothSynthetic.ps1') `
    -KdcmpLua $KdcmpLua -MoonSharpVersion $MoonSharpVersion `
    -Scenario (Join-Path $toolsDir 'Test-Icd14NpcSynthetic.lua') `
    -Title 'KCT fix (icd14) synthetic NPC pure-puppet Lua test'
exit $LASTEXITCODE
