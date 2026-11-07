# WO-175 — the 0.49.2 installer record (local, unsigned, release candidate)

Built unattended 2026-10-11 from a fresh clone of `68bbfba` (`release\c0492`, git-ignored), with the three start saves copied in (SHA-1
equal to the source copies): `tools\Build-Installer.ps1 -SoakWaiver "the maintainer's standing rule, quoted in the WO-175 run prompt
(2026-10-11): 'Ends in a release-candidate installer, always (soak waived under the standing rule, quoted and dated)'; built unattended
2026-10-11"`. Exit 0.

| | |
|---|---|
| file | `release\KingdomComeTogether-Setup-0.49.2.exe` (local only; nothing uploaded) |
| size | 106,875,381 bytes |
| sha256 | `1472d0414cf440309391a9bee6422ba9c436f0f07e8b0d98ae62ca686d7c0217` |
| header version | **0.49.2** (checked: the compile line `KCDMP.iss (version 0.49.2)` and the exe's ProductVersion) |
| signed | no (no signing settings on this machine) |
| soak | waived, as quoted above |

## The build's own gates

| gate | 0.49.1 | 0.49.2 |
|---|---|---|
| relay | 63 | **63** |
| agent (`KcdMp.Client.Tests`) | 1,455 | **1,503** (+48: save health 22, backups 12, co-op playline 14) |
| setup | 77 | **77** |
| log bundle | 14 | **14** |
| Lua synthetic suites | 59 / 3,988 checks | **60 / 4,017 checks**, 0 failed (new `Test-WO175Synthetic`, 29) |
| static checks (console placeholders, Lua locals, native guards, WO-157) | green | **green** |
| save tool (`test_whs_dump.py`) | 7 | **7** |
| save repair tool (`test_whs_repair.py`) | — | **14** (new) |
| save round trip, live (WO-175 S5) | — | **SKIPPED** — no game was running during the build (logged: "no Modding Tools game answers"); run live separately, see findings §1 |
| native | 9,318 | **9,318** |
| payload smoke | ok | **ok** (`RELAY-SMOKE ok protocol=v10 release=0.49.2`) |
| no-input-tooling (WO-175, new) | — | **0 hits** over 1,036 payload files; 37 lines of Microsoft .NET Desktop runtime files listed as named, reviewed exemptions (findings §5) |
| installer detection / the four cases | 26/26, 41/41 | **26/26, 41/41** |
| guards | 7/7 | **7/7** |
