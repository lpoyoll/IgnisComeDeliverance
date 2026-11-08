<#
.SYNOPSIS
  KCT diag (icd14): one-screen health report of a co-op session from the logs.

.DESCRIPTION
  Reads the agent log (MP-HEALTH, MP-HEALTH-PEER and the older MP-W207/pipe lines), the game's kcd.log (the mod's Lua MP-HEALTH
  lines and the engine-side desync symptoms) and the native DLL's log, then prints:
    1. the session: builds, roles, time span;
    2. every health area (this machine's and the partner's, from MP-HEALTH-PEER) with each numeric key's last / mean / max;
    3. alerts: keys past a threshold, with how many 10 s windows and when first/last;
    4. the older symptom counters (queue lag, NPC fights, authority violations, engine queue overflow, pipe drops);
    5. a per-minute timeline of the worst signals.
  Works on a live install (defaults) or on an unpacked log bundle (-BundleDir).

.EXAMPLE
  .\tools\Analyze-Session.ps1
  .\tools\Analyze-Session.ps1 -BundleDir C:\temp\KCDMP-logs-20261108 -Since 18:20
#>
[CmdletBinding()]
param(
    [string]$AgentLog,
    [string]$KcdLog,
    [string]$NativeLog,
    [string]$BundleDir,
    [string]$Since,              # HH:mm[:ss] -- agent-log lines before this are skipped
    [int]$TopKeys = 12,          # numeric keys shown per area (the ones with the largest max first)
    [switch]$NoTimeline
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

function Find-First([string[]]$candidates) { foreach ($c in $candidates) { if ($c -and (Test-Path -LiteralPath $c)) { return (Resolve-Path -LiteralPath $c).Path } }; return $null }

$appDir = Join-Path $env:LOCALAPPDATA 'IgnisComeDeliverance'
if ($BundleDir) {
    $AgentLog  = if ($AgentLog)  { $AgentLog }  else { Find-First @((Join-Path $BundleDir 'agent.log'), (Get-ChildItem -LiteralPath $BundleDir -Recurse -Filter 'agent.log' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName) }
    $KcdLog    = if ($KcdLog)    { $KcdLog }    else { Find-First @((Join-Path $BundleDir 'kcd.log'), (Get-ChildItem -LiteralPath $BundleDir -Recurse -Filter 'kcd.log' -ErrorAction SilentlyContinue | Select-Object -First 1).FullName) }
    $NativeLog = if ($NativeLog) { $NativeLog } else { Find-First @((Join-Path $BundleDir 'kcdmp-native.log'), (Join-Path $BundleDir 'kcdmp-native.mirror.log')) }
} else {
    if (-not $AgentLog)  { $AgentLog  = Find-First @((Join-Path $appDir 'agent.log')) }
    if (-not $NativeLog) { $NativeLog = Find-First @((Join-Path $appDir 'kcdmp-native.log')) }
    if (-not $KcdLog) {
        # the game's folder from the launcher's settings, else the usual Steam places
        $cands = @()
        foreach ($s in @((Join-Path $appDir 'settings.json'), (Join-Path $appDir 'Launcher\settings.json'))) {
            if (Test-Path -LiteralPath $s) { try { $gp = (Get-Content -LiteralPath $s -Raw | ConvertFrom-Json).GamePath; if ($gp) { $root = Split-Path (Split-Path (Split-Path $gp)); $cands += (Join-Path $root 'kcd.log') } } catch {} }
        }
        $cands += @('D:\SteamLibrary\steamapps\common\KCD2Mod\kcd.log', 'C:\Program Files (x86)\Steam\steamapps\common\KingdomComeDeliverance2\kcd.log')
        $KcdLog = Find-First $cands
    }
}

$sinceSpan = $null
if ($Since) { $sinceSpan = [TimeSpan]::Parse($Since, $inv) }

# ---------------------------------------------------------------- health lines
$areas = @{}      # "<side>|<src>|<area>" -> @{ n; keys = @{ key -> list of doubles }; times = list }
$alerts = @{}     # "<side>|<area>|<key>" -> @{ n; first; last; worst }
$timeline = @{}   # "HH:mm" -> @{ lag; fights; viol; pipe; peer }

# key pattern -> threshold (a value above it in one 10 s window is an alert)
$rules = [ordered]@{
    'queue_lag_ms'          = 5000;  'gate_wait_max'         = 2000;  'gate_wait_p95'      = 1000
    'reply_timeouts'        = 0;     'reconnects'            = 0;     'oversize_refused'   = 0
    'oversize_skipped'      = 0;     'stale_dropped'         = 50;    'dropped_stale'      = 50
    'seq_gaps'              = 0;     'events_missing'        = 0;     'handler_ms_max'     = 500
    'fight_events'          = 100;   'authority_violations'  = 20;    'handovers_timed_out' = 0
    'snaps_gt5m'            = 2;     'readopts'              = 20;    'silence_drops'      = 5
    'overflow_reseats'      = 2;     'clock_err_ms'          = 5000;  'quest_diffs'        = 0
    'quest_correct_refused' = 0;     'inbox_dropped'         = 0;     'batch_timeouts'     = 2
    'log_lines_per_s'       = 300;   'oldest_wait_ms'        = 3000;  'answer_ms_p95'      = 500
    'requests_per_frame_max'= 64;    'budget_hits'           = 200
}

function Add-Health([string]$side, [string]$src, [string]$area, [string]$body, [string]$clock) {
    $k = "$side|$src|$area"
    if (-not $areas.ContainsKey($k)) { $areas[$k] = @{ n = 0; keys = @{}; first = $clock; last = $clock } }
    $a = $areas[$k]; $a.n++; $a.last = $clock
    foreach ($m in [regex]::Matches($body, '(?<k>[A-Za-z_][\w.\-]*)=(?<v>-?\d+(?:\.\d+)?)(?=\s|$)')) {
        $key = $m.Groups['k'].Value; $v = [double]::Parse($m.Groups['v'].Value, $inv)
        if (-not $a.keys.ContainsKey($key)) { $a.keys[$key] = [System.Collections.Generic.List[double]]::new() }
        $a.keys[$key].Add($v)
        foreach ($r in $rules.Keys) {
            if ($key -eq $r -or $key.EndsWith("_$r")) {
                if ($v -gt $rules[$r]) {
                    $ak = "$side|$area|$key"
                    if (-not $alerts.ContainsKey($ak)) { $alerts[$ak] = @{ n = 0; first = $clock; last = $clock; worst = $v; limit = $rules[$r] } }
                    $al = $alerts[$ak]; $al.n++; $al.last = $clock; if ($v -gt $al.worst) { $al.worst = $v }
                }
            }
        }
    }
}

function Tl([string]$clock) {
    if (-not $clock -or $clock.Length -lt 5) { return $null }
    $min = $clock.Substring(0, 5)
    if (-not $timeline.ContainsKey($min)) { $timeline[$min] = @{ lag = 0; fights = 0; viol = 0; pipe = 0; gaps = 0 } }
    return $timeline[$min]
}

$roles = @{}; $builds = @{}; $span = @{ first = $null; last = $null }
$legacy = [ordered]@{ 'agent: queue lag >= 5 s (samples)' = 0; 'agent: queue lag max (ms)' = 0; 'agent: DLL link lost' = 0
                      'agent: event seq gaps' = 0; 'agent: Lua batches dropped/timed out' = 0; 'agent: host froze (paused: frozen)' = 0 }

if ($AgentLog) {
    $logs = @($AgentLog)
    $prev = Join-Path (Split-Path $AgentLog) 'agent.prev.log'
    foreach ($line in [System.IO.File]::ReadLines($AgentLog)) {
        if ($line.Length -lt 13) { continue }
        $clock = $line.Substring(0, 12)
        if ($sinceSpan) { $ts = [TimeSpan]::Zero; if ([TimeSpan]::TryParse($clock, $inv, [ref]$ts) -and $ts -lt $sinceSpan) { continue } }
        if (-not $span.first) { $span.first = $clock }; $span.last = $clock
        $i = $line.IndexOf('MP-HEALTH')
        if ($i -ge 0) {
            $rest = $line.Substring($i)
            if ($rest -match '^MP-HEALTH area=(?<a>[\w\-]+)\s*(?<b>.*)$') {
                Add-Health 'local' 'agent' $Matches.a $Matches.b $clock
                if ($Matches.a -eq 'agent') {
                    if ($rest -match ' role=(\w+)') { $roles['local'] = $Matches[1] }
                    if ($rest -match ' build=(\S+)') { $builds['local'] = $Matches[1] }
                    if ($rest -match ' queue_lag_ms=(\d+)') { $t = Tl $clock; if ($t -and [int]$Matches[1] -gt $t.lag) { $t.lag = [int]$Matches[1] } }
                }
                continue
            }
            if ($rest -match '^MP-HEALTH-PEER from=(?<r>\w+):\d+ src=(?<s>\w+) area=(?<a>[\w\-]+) age_s=\d+\s*(?<b>.*)$') {
                Add-Health "peer-$($Matches.r)" $Matches.s $Matches.a $Matches.b $clock
                if ($Matches.a -eq 'agent' -and $Matches.b -match 'build=(\S+)') { $builds["peer-$($Matches.r)"] = $Matches[1] }
                continue
            }
        }
        if ($line.Contains('MP-W207-Q lag_ms=')) {
            if ($line -match 'lag_ms=(\d+)') { $v = [int]$Matches[1]; if ($v -ge 5000) { $legacy['agent: queue lag >= 5 s (samples)']++ }; if ($v -gt $legacy['agent: queue lag max (ms)']) { $legacy['agent: queue lag max (ms)'] = $v }; $t = Tl $clock; if ($t -and $v -gt $t.lag) { $t.lag = $v } }
        } elseif ($line.Contains('lost the connection to KCDMP.dll')) { $legacy['agent: DLL link lost']++; $t = Tl $clock; if ($t) { $t.pipe++ } }
        elseif ($line.Contains('MP-EVT-GAP')) { $legacy['agent: event seq gaps']++; $t = Tl $clock; if ($t) { $t.gaps++ } }
        elseif ($line.Contains('MP-BATCH-DROP')) { $legacy['agent: Lua batches dropped/timed out']++ }
        elseif ($line.Contains('host is paused: frozen')) { $legacy['agent: host froze (paused: frozen)']++ }
    }
}

$kcd = [ordered]@{ 'game: NPC fighting the stream' = 0; 'game: authority violations' = 0; 'game: engine action-queue overflow' = 0
                   'game: AI "Agent is stuck"' = 0; 'game: NPC float/sink' = 0; 'game: guard stops' = 0; 'game: Lua errors' = 0 }
$lastSummary = $null
if ($KcdLog) {
    $kcdSize = (Get-Item -LiteralPath $KcdLog).Length
    foreach ($line in [System.IO.File]::ReadLines($KcdLog)) {
        if ($line.Length -lt 8) { continue }
        $c0 = $line[0]
        if ($c0 -eq '[') {
            if ($line.StartsWith('[KCD2-MP] ')) {
                if ($line.Contains('MP-SUMMARY-MOD')) { $lastSummary = $line; continue }
                if ($line.Contains('MP-HEALTH area=')) {
                    if ($line -match 'MP-HEALTH area=(?<a>[\w\-]+)\s*(?<b>.*?)(?: t=[\d.]+)?$') { Add-Health 'local' 'lua' $Matches.a $Matches.b '' }
                } elseif ($line.Contains('NPC-FIGHT ') -and $line.Contains('displaced')) { $kcd['game: NPC fighting the stream']++ }
                elseif ($line.Contains('MP-AUTHORITY-VIOLATION')) { $kcd['game: authority violations']++ }
                elseif ($line.Contains('MP-NPCZ npc')) { $kcd['game: NPC float/sink']++ }
                elseif ($line.Contains('WO139-STOP')) { $kcd['game: guard stops']++ }
            } elseif ($line.StartsWith("[Warning] Validator: Can't remove")) { $kcd['game: engine action-queue overflow']++ }
            elseif ($line.Contains('Agent is stuck')) { $kcd['game: AI "Agent is stuck"']++ }
            elseif ($line.StartsWith('[Error]') -and $line.Contains('.lua')) { $kcd['game: Lua errors']++ }
        }
    }
    $kcd['game: kcd.log size (MB)'] = [math]::Round($kcdSize / 1MB, 1)
    if ($lastSummary) {
        foreach ($sk in 'npcfight_events', 'auth_violations', 'auth_pauses', 'puppets', 'ghost_packets', 'resync_applied') {
            if ($lastSummary -match "\b$sk=(\d+)") { $kcd["game (mod totals): $sk"] = [int]$Matches[1] }
        }
    }
}

$nat = [ordered]@{ 'native: pipe dropped (oversize)' = 0; 'native: bodies dropped (silence)' = 0; 'native: bodies dropped (pipe-closed)' = 0; 'native: agent disconnects' = 0 }
if ($NativeLog) {
    foreach ($line in [System.IO.File]::ReadLines($NativeLog)) {
        if ($line.Contains('MP-HEALTH area=')) {
            $clock = if ($line.StartsWith('[') -and $line.Length -gt 13) { $line.Substring(1, 12) } else { '' }
            if ($line -match 'MP-HEALTH area=(?<a>[\w\-]+)\s*(?<b>.*)$') { Add-Health 'local' 'native' $Matches.a $Matches.b $clock }
        } elseif ($line.Contains('out of range; dropping the connection')) { $nat['native: pipe dropped (oversize)']++ }
        elseif ($line.Contains('event=drop reason=silence')) { $nat['native: bodies dropped (silence)']++ }
        elseif ($line.Contains('event=drop reason=pipe-closed')) { $nat['native: bodies dropped (pipe-closed)']++ }
        elseif ($line.Contains('PIPE: agent disconnected') -or $line.Contains('PIPE: disconnect')) { $nat['native: agent disconnects']++ }
    }
}

# ---------------------------------------------------------------- report
"== Session"
"  agent log : $AgentLog"
"  kcd.log   : $KcdLog"
"  native log: $NativeLog"
"  span      : $($span.first) .. $($span.last)"
foreach ($k in $roles.Keys) { "  role ($k): $($roles[$k])" }
foreach ($k in $builds.Keys) { "  build ($k): $($builds[$k])" }
if ($builds.Count -gt 1 -and ($builds.Values | Select-Object -Unique).Count -gt 1) { "  !! the two machines run different builds" }

""
"== Alerts (key past its threshold in a 10 s window)"
if ($alerts.Count -eq 0) { "  none" }
foreach ($e in ($alerts.GetEnumerator() | Sort-Object { $_.Value.n } -Descending)) {
    $p = $e.Key.Split('|')
    "  {0,-12} {1,-14} {2,-26} windows={3,-4} worst={4,-8} limit={5,-6} {6}..{7}" -f $p[0], $p[1], $p[2], $e.Value.n, $e.Value.worst, $e.Value.limit, $e.Value.first, $e.Value.last
}

""
"== Health areas (last / mean / max of the busiest keys)"
if ($areas.Count -eq 0) { "  no MP-HEALTH lines (a build before icd14, or no session ran)" }
foreach ($k in ($areas.Keys | Sort-Object)) {
    $a = $areas[$k]; $p = $k.Split('|')
    "  [{0}] {1}/{2}  lines={3}" -f $p[0], $p[1], $p[2], $a.n
    $rows = foreach ($kk in $a.keys.Keys) { $l = $a.keys[$kk]; [pscustomobject]@{ key = $kk; last = $l[$l.Count - 1]; mean = [math]::Round(($l | Measure-Object -Average).Average, 1); max = ($l | Measure-Object -Maximum).Maximum } }
    foreach ($r in ($rows | Where-Object { $_.max -ne 0 } | Sort-Object max -Descending | Select-Object -First $TopKeys)) {
        "      {0,-30} last={1,-10} mean={2,-10} max={3}" -f $r.key, $r.last, $r.mean, $r.max
    }
}

""
"== Symptom counters"
foreach ($h in @($legacy, $kcd, $nat)) { foreach ($k in $h.Keys) { "  {0,-42} {1}" -f $k, $h[$k] } }

if (-not $NoTimeline -and $timeline.Count -gt 0) {
    ""
    "== Timeline (per minute: worst queue lag, DLL link drops, event gaps)"
    foreach ($m in ($timeline.Keys | Sort-Object)) {
        $t = $timeline[$m]
        if ($t.lag -lt 1000 -and $t.pipe -eq 0 -and $t.gaps -eq 0) { continue }
        $bar = '#' * [math]::Min(40, [int]($t.lag / 2000))
        "  {0}  lag={1,6} ms {2,-40} pipe_drops={3} evt_gaps={4}" -f $m, $t.lag, $bar, $t.pipe, $t.gaps
    }
}
