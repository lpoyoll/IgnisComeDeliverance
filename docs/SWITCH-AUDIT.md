# Switch audit (WO-200 to WO-214)

Read-only audit of the 133 named runtime switches of WO-200..WO-214 (older families sampled). Each switch was searched by exact
name in C#, native and Lua (declarations, help tables and status prints excluded), and every C#→DLL bit followed to the native
`on(kBit…)` / `fix_on(kFix…)` that reads it. Evidence is the WO findings docs (mostly one-PC bench runs, not field data).
Nothing has been changed yet on the strength of this audit.

**Counts:** retire-on 31 · keep 92 · dead 6 · default-off 3 · superseded 1.

## Dead (the switch does nothing)
| switch | WO | why |
|---|---|---|
| `both_are_henry` | 201 | read nowhere; V.5 was never built. `mp_w201_status` still shows it "on". |
| `near_safe` | 202 | its bit is only logged (wo202.cpp:131); the check it gated runs unconditionally (wo201_near.cpp:285). |
| `state_unreliable` | 213 | only the environment twin (`TwinOff`) is read; the console switch changes a value nothing reads. |
| `steam_cfg` | 213 | same. |
| `link_budget` | 213 | same. |
| `npc_compact` | 213 | same. |

Partly dead the same way: `link_sched` and `figure_band` (the scheduler reads only the environment twin). Fix: read `W213On(name)`
at connect time instead of `TwinOff`, or drop the console switches. Superseded and inert: `blow_joiner_values` (WO-210 retired it).

## Default off (on purpose)
`figure_is_player` (reaches the save; waits for its decode check), `research_death_from_health` (research), `allow_fly` (testers).

## Retire (hard-code on, delete the off branch)
Top ten (a true twin, a large effect, no stated downside): `copy_no_idle_turn` (0 vs 204 overflows), `cutscene_clock_v2`
(1 vs 25 hard seeks), `epoch_check` (0 vs 5 stale rows), `skirmish_keep` (twin crashed the host), `ui_lua_main_only` (twin crashed
the host), `plan_in_fight` (3 crashes in 28 fights vs 0), `target_in_fight` (0 vs 7/7), `one_bind` (1.8 vs 6.9 binds a body),
`send_turn` (267 turns sent vs 0), `clock_quiet_in_skip` (73 s vs 22,091 s apart).

Free (their old off-path was already deleted in WO-201, so off restores nothing): `joiner_no_puppet_quests`,
`copy_health_from_host`, `rider_from_host`.

Also retire-on: `s1_replay`, `death_engine_only`, `rejoin_recover`, `anim_shown`, `conv_no_barks`, `listen_map`,
`conv_cam_player_view`, `conv_autojoin`, `copy_opponent_mirror`, `copy_attack_ai` (goal not met, but off is strictly worse),
`forced_talk_once`, `link_hold`, `line_latency`, `combat_end`, `draw_event`, `figure_hands`, `figure_turn`, `anim_hold`.
Environment-only fixes ready to hard-code: `conv_op_frame`, `conv_voice_make`, `conv_voice_by_name`, `native_bind_order`,
`owner_queue_cap`, `death_leave_clears`, `no_joiner_values`, `handover_queue_cap`, `seat_same_object`, `snapshot_after_drop`.

## Keep (92)
Field-unverified, "twin read the same", or no result of their own -- e.g. `walk_from_host` (feet still slide), `npc_bands` (~3 fps
on the host), `copy_state_always`, `owner_base`, the conversation family (`conv_lines`/`conv_voice` still fail on a real pair).
Retiring these needs the two-player session data first.

## Not covered
Families before WO-187 (sampled only), WO-203's `mp_join_conversation` / `mp_conv_camera`, `KCD2MP.w177.*`, and the native reads
of each WO-200 tap/gate/value bit beyond `copy_neutral_always`. Odd name: `W210("no_raw_blows")` is read (Wo173.cs:117) but is not
in Wo210Rules.SwitchNames; the findings call it `no_raw_window` / `no_joiner_values`.
