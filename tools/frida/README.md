# tools/frida: runtime traces of the original exe

Differential-test tool: Frida hooks in the original `REBEXE.EXE` (32-bit, image base 0x400000), events as JSONL. Observation output only: traces go outside the repo (default `<traces dir>\`), never commit them.

Setup: `python -m pip install frida-tools` (frida 17.x).

```
python tools/frida/trace.py                       # spawn REBEXE.EXE directly (default), log until the game exits / Ctrl+C
python tools/frida/trace.py --dxwnd               # start via DxWnd (correct graphics, but the DxWnd setup blocks most new-game dialog clicks)
python tools/frida/trace.py --side empire --galaxy standard --difficulty novice --speed 4 --note "no orders"   # settings go into the first JSONL line (ev "meta")
python tools/frida/trace.py --attach              # attach to a running REBEXE.EXE (misses new-game init if late)
python tools/frida/trace.py --direct --out X.jsonl -- -fullscreen   # args after -- go to the exe (--direct only)
python tools/frida/rng_report.py TRACE.jsonl --funcs "<traces dir>\rebexe-funcs.txt" [--seq] [--all-gens] [--day N]
python tools/frida/rng_report.py TRACE.jsonl --commands > COMMANDS.jsonl   # executed command stream
python tools/frida/rng_report.py TRACE.jsonl --facts tests/OpenSWR.Tests/Vectors/facts-SEED.json   # numbers-only state-event vector (user-approved in repo)
python tools/frida/rng_report.py TRACE.jsonl --battles tests/OpenSWR.Tests/Vectors/battles-SEED.json   # numbers-only automatic-battle vector (user-approved in repo)
python tools/frida/make_vector.py TRACE.jsonl --funcs FUNCS.txt --column 1 [--side 1] --out tests/OpenSWR.Tests/Vectors/newgame-SEED.json [--days tests/OpenSWR.Tests/Vectors/days-SEED.json]   # numbers-only setup (+ post-setup day) vector (user-approved in repo)
```

- Default launch = direct spawn (raw DirectDraw: wrong colours, flipped icons, one crash in a surface fill; logic unaffected). `--dxwnd` = what `REBEXE-DXWND.cmd` does: spawns `<dxwnd dir>/dxwnd.exe /C:rebellion.ini /R:1` (`--dxwnd-dir`, `--dxwnd-args`) with Frida child gating; when DxWnd creates `REBEXE.EXE` the agent loads before the game's first instruction, then DxWnd's own injection proceeds (`dxwnd.dll` + `frida-agent.dll` coexist). DxWnd exits with the game. No elevation needed.
- Spawns set `__COMPAT_LAYER=RunAsInvoker` (inherited by DxWnd's child): the user's compat layer marks the exe RUNASADMIN and a non-elevated spawn fails with 0x2e4. Attach mode needs the same elevation as the game.
- The startup `seed` (RA 0x406152) and the game `rng_ctor` happen at new game (just before init step 2), not before the main menu; `--attach` at the main menu would still catch them, the default mode catches everything.
- Agent callbacks run in try/catch: a decode failure becomes a `hook_error` event (`name`, `msg`, `stack`; first 20 per hook) instead of a Frida script error. Vtable getters (`ref`, command/event type) are only called when the vtable and getter lie inside the exe image. The session end is logged as `detached` (`reason`, e.g. `process-terminated` = the game exited or crashed).
- Never assign `this.depth` (or `returnAddress`, `context`, `threadId`, `lastError`) in a hook: read-only `InvocationContext` properties, the assignment throws.
- `--facts`: after setup, in trace order, `[day, kind, object, old, new]`: notification runs (`event_run` priority queue) 0x300 Existing, 0x301 Usable, 0x302 owner (old 3 = created), 0x306 HyperdriveActive, 0x308 Completed, 0x30C / 0x30D mission ready / phase, 0x320 parent (SdRefs), 0x321 agent mission (SdRefs, none = 2) with `a4c` / `a50`; 0x60 / 0x64 = `mission_task` / `mission_done` changes (old != new); 0x387 = travel timer start (object from its `timer_delay`, old = base, new = fire day). `kinds` lists the kinds the trace's hooks cover. Sim side: `GameState.FactTrace`; test `CommandReplayTests.FactsMatchTheTrace`. Traces: run 1 `rng-20261016-140714`, run 2 `rng-20261016-141308`, run 3 `rng-20261016-160931`.
- `--battles`: per automatic battle (from the engine generator's `seed` at ra 0x5af6cb to the next `battle_apply` start): `day`, `draws` (engine rand(100) count), `dmg` = one row per `bt_dmg` `[unit SdRef, key +0xEC, state, d, shield before, hull before, shield after, hull after, engine draws since the previous row, [k of each bt_crit since the previous row]]` (floats are the exact float32 values), `S` (`battle_sides`), `ships` `[unit, C words, float hull]` and `fighters` `[unit, F words]` from the applied records. Traces without `bt_dmg` hooks give `draws` and the records only.
- `rebexe-funcs.txt`: function start VAs (hex, one per line) from the analyst's Ghidra export; only used to map callers to functions. Outside repo.
- Event fields: `seq` (monotonic), `ev`, `day` (clock Day when the clock exists). Events: `module`, `hook`, `verify_failed` (first bytes differ, hook skipped), `seed`, `rng_ctor`, `rand` (`gen` = `game` or generator address, `n`, `before`/`after` state, `result`, `ra`, `via` wrapper, `caller` = wrapper's caller, `step`), `init`/`init_end` (new-game step), `timer_start`, `timer_delay`, `timer_fire`, `day` (day changes only).
- Day-tick events (objects as SdRef `fac`/`side`/`char`/`mgr`): `event_run` (queue `priority`/`timed`/`fifo`, event `type` from its vtable +0x24, `target` +0x3C, notification args `a4c`/`a50`), `command_add` (pending player/AI command; `type`), `command` (command about to execute: `type`, `side`, `targets`, `arg_ref`, `arg_int`, class `fields`, clock `tick` and command `serial` before), `cmd_exec` (execute result: `ok`, `res` {category, code}, `serial` after; `depth` > 0 = sub-command of a move, with its decoded fields); order events in `event_run` also carry `hdr` (side, serial, arg_ref, command type), `p` (payload u32s from +0x50) and `lists` (node lists `[key +0x18, value +0x1C]`), `maint` (side maintenance recompute, `ra` = handler that ran it, side pairs before/after), `pair_set` (Mine/Refinery pair write), `cycle` (cycle-length recompute, result `length`), `fac_state` (state set), `fac_notif` (notification 0x315 handler, carried old/new), `refill_raw` / `refill_refined` (one wait-list entry served), `stock_raw` / `stock_refined`, `force_active` (character +0xAC bit 7 = timer 0x389 Active), `smuggling` (SmugglingPercent inputs/result), `smuggle_draw`, `mgr_point` (manager counters before/after a delivered point).
- Mission events: `force_detect` (VA 0x55e4d0: `args` [side, forceA, forceB], `res`, `p`), `loyalty` (VA 0x55e520: `args` [side, EnhancedLoyalty], `res`, `p` = 100 - loyalty; the function list maps its draws to 0x55e4d0), `decoy` (VA 0x55e410) and `foil` (VA 0x55e470: raw cdecl `args`, `res` = chance result, `p` = table value; argument meaning Inferred), `mission_prob` (VA 0x593320: `mission`, `member`, `p`, `sk` = member +0x7C...+0x94 as i16), `mission_task` / `mission_done` (TaskStatus +0x60 / CompletionStatus +0x64 old/new).
- Combat events: `destroyed` (DestroyedReason setter VA 0x4f71d0: `obj`, `old`, `new`), `assault` (resolver VA 0x58c1e0 on leave: `sys`, attacker `side`, defender `def`, `n` = [attackers, defenders, attackers left, defenders left, duels]), `troop_duel` (VA 0x55eb00 `args` [att strength, att leadership, def strength, def leadership], `att_lost`, `def_lost`), `assault_fire` (VA 0x55ead0), `assault_damage` (VA 0x55ebc0 `args` [facilities, HQs, energy, raw], `out` [facility, index, energy, raw]), `bombard` (resolver VA 0x58d800 on leave: `sys`, command type `cmd`, `n` = resolver +0x14...+0x44), `bombard_strength` / `bombard_fire` (VA 0x55e950 / 0x55e930), `bombard_hit` (VA 0x55e970 `defence`, `hit`), `battle_flag` (system Battle bit), `battle_choice` (host decision VA 0x41cfc0 once decided: `auto` = 1 automatic), `battle_apply` start (VA 0x40c780, `auto` flag) / end (`sys`, `withdraw` = the out flag, `won` = BattleWon record, `ck` = system ControlKind word +0x78), raw tactical result records `battle_sides` / `battle_fighter` / `battle_ship` / `battle_unit5` (VA 0x5969d0 / 0x5968e0 / 0x596870 / 0x596980: `key`, `v` u32s, `f` = the float hull field of ship [4] / unit5 [3]), the per-unit appliers `apply_fighter` / `apply_ship` / `apply_unit5` / `apply_fighter_parent` (`unit`), `hull_damage` (capital ship +0x60 setter VA 0x501490), `squad_damage` (fighter SquadSizeDamage +0x60 setter VA 0x5032c0), `battle_end` (VA 0x516ef0), `battle_wave` (VA 0x55dbf0 `args`, `rec` = [loser, limit, winner, delta]). Tactical-engine draws are `rand` events of non-game generators (`rng_report.py --all-gens`). Automatic-engine applications (unverified live): `bt_dmg` (VA 0x5b54d0 on leave: unit `key` +0xEC, `fam`, `id`, `st`, `d`, `before` / `after` = [shield, hull]; its engine draws precede the line) and `bt_crit` (VA 0x5b1970: `key`, `k`, `c` = counters shield, weapon, tractor, speed, hyperdrive after).
- Uprising events: `uprising_flag` / `suppressing_flag` (system flag setters VA 0x50a4a0 bit 2 / VA 0x50a7b0 bit 9: `sys`, `owner`, `support` (Alliance share), `required` +0x80, `surplus` +0x7C, current `uprising` / `suppressing` / `deathstar` bits, `old`, `new`), `upr_incident` (VA 0x50d030, same system fields plus `totals` = block +0x74 incite, +0x78 subdue, +0x7C support delta), `upr_roll` (VA 0x559ce0 `args` [owner, share, deathStar, troops, stormtroopers, t74, t78], `upris1`, `upris2`), `upr_effect` (VA 0x50d150: `value`, `owner`), `informant_roll` (VA 0x559db0 INFORMTB `value`), `subdue_end` (VA 0x50c910: system fields before, `uprising_after`). Losses: `destroyed` with `new` = 8 (facility/troop) or 10 (character death).
- `ref()` calls the object's class getter (vtable +4) from the hook; it returns null if the object is not readable.
- `rand` with `n <= 1` does not step the generator (`roll(0)`); the report labels it.
- Add a hook: one `HOOKS` entry in `agent.js` = `[VA, expected first bytes, name, {onEnter, onLeave}]`; take the bytes from the exe, mark assumed argument layouts Inferred. VAs/layouts only, never decompiled code.
- Prisoner events: `char_state` (state +0x98 setter VA 0x4ee3e0: `char`, current `state`, `flags` +0xAC, `owner`, `captor` key, `parent`, `new`, `ra` = slot function: 0x4ef4cf capture, 0x4ef537 rescue, 0x4ef591 escape/uprising, 0x4ef5d1 release), `capture` (vtable +0x20C VA 0x4ef480: `captor_arg`, character before, `after`), `escape_check` (VA 0x561990: `value` = EscapeAttempt, `flags_after`), `escape_roll` (VA 0x55e7e0 `args` [side, EnhancedCombat, EnhancedEspionage, guard average combat, troops], `res`), `prisoner_hook` (manager 0xF8 0x30A hook VA 0x546370: `char`, `old`, `new`). Deaths: `destroyed` with `new` = 9 (captured special force) / 10 (injury death).

## AI hooks (`--ai-hooks`)

Off by default. `--ai-hooks` adds these hooks, prologues (or instruction bytes) checked as usual (all checks run before the first attach, so two hooks on one VA, `support_set` and the `ai_supview` gate on 0x509c30, both install):

| Event | VA | When | Fields |
|---|---|---|---|
| `ai_sysrec` | 0x433e40 | exit, system records only (key high byte 0x90-0x97), only when the record was dirty | `side`, `key`, `share48` (the side view's support share; 100 for an own system; view +0x58 = share for side 1, 100 - share for side 2), `troops88`, `need98`, `batteries74`, `f118`, flag words `f28` / `f2c` / `f30` |
| `ai_t4targets` | 0x4b8b70 | exit | `op` (fleet-op key), `site` (task +0x5C), `targets` (task +0x34 ... +0x58) |
| `ai_t4site` | 0x4b8f80 | entry / exit | `before` / `after` (task +0x5C) |
| `ai_opstatus` | 0x479a00 | entry | `op`, `f60`, `f64`, `counters` (op +0x6C ... +0x90), `fleets` (member fleets, op +0x2C, as fleet-record lines: [key, +0x30 op key, +0x38 flags, +0x28 location, the location's system-record C word +0x30]) |
| `ai_theatercheck` | 0x4e5560 | exit | `add` / `drop` (strategy +0x14 / +0x18), `theaters` (count of the strategy's theater list), `model04` |
| `ai_sysview` | 0x433e96 (instruction after the view lookup in the record update) | every record update of a system | `side`, `key`, `view58` (the side view's support = Alliance share), `viewOwner`; the matching `ai_sysrec` follows when the record was dirty |
| `support_set` | 0x509c30 (support setter, thiscall system) | exit, only when the value changes | `sys`, `old`, `new`, `now`, `ra` (caller) |
| `ai_sitelist` | 0x41a340 | exit, only when called from the type-4 site pick VA 0x4b8f80 | `idx` (4 / 5 / 6 = facility kind, 0x14 = +0x98), `dir`, `entries` [key, value, rank] in list order |
| `ai_pick` | 0x4be980 | exit (every planner pick) | `planner` (vtable), `status`, `prio`; the last pick of a tick = where the issue cycle stopped |
| `ai_surplus` / `ai_split` | 0x478aa0 / 0x477a40 | entry state + exit | `op`, `counters` (+0x6C ... +0x90), `flagship` (+0x38), `f64`, `ships` (member ship keys), `cmd` (the returned 0x270, or null) |
| `ai_surplus_ship` | 0x478aa0 entry (read from the model's record map, no instruction hook) | every `ai_surplus` line that is logged | `key`, `f24`, `f2c` (fleet), `f7c`, `f80`, `f6c`, `f60`, `f48` (ship record fields of the test; held = VA 0x484510 not logged) |
| `ai_rescue` / `ai_abduct` | 0x47e620 / 0x483640 | exit | `op` (mission-op key +0x18), `objective` (+0x30), `target` (+0x34), `ret` / `retKey` (returned record and its key) |
| `ai_decoyn` | 0x4bd000 | exit | `op` (mission-op key), `staging` (op +0x38), `n` (decoys at staging) |
| `ai_p3budget` / `ai_p3c` | 0x4da530 / 0x4da0d0 (planner 3 facility budget / request step C) | exit | `b` (budget), `ra`, `cap` / `dem` (model +0x184 / +0x188); `before` / `build` (planner +0x58 before / after C), `model08` (model +0x08 turn bits) |
| `ai_rk` | 0x431f40 (random-key system list), gated by VA 0x41a460 called from the staging VA 0x4bd0a0, or from VA 0x41b2b0 while that runs for the staging VA 0x4bd0a0 (gate hook on 0x41b2b0 entry; its other ten callers stay silent) | entry | `caller`, `sector`, `masks` (req28, req2C, req30, excl28, excl2C, excl30), `dir`, `slots` [system key, +0x28, +0x2C, +0x30] per sector slot |
| `ai_supview` | 0x50f390 (support copy into one side view), gated by the support setter VA 0x509c30 | exit | `ra` (setter caller), `key`, `side`, `state` (1 live, 2 frozen), `ctx4`, `ctx8`, `before`, `after` (view +0x58) |
| `view_live` | 0x4f9a60 (live recompute, real object), missions [0x40, 0x80) only | exit | `key`, `owner`, `f50` (ObservedBy bits 7 / 8), `before` / `after` = the side-1 / side-2 view state nibbles (1 live, 2 frozen) |
| `view_destroyed` | 0x4fc080 (view Destroyed hook; posts 0x302 when the view has a parent), missions only | exit | `key`, view `side`, `state`, `owner`, `f50`, `ret` |
| `ai_step` | planner step VAs, planners 1-13 | exit, every step call | `planner`, `before` / `after` [phase +0x38, state +0x3C], `done`, `cmd` (type or null); printed with `--picks` |
| `ai_taskfleet` | 0x4b04c0 (fleet task state 5) | exit | `op`, `fleet` (task +0x28), `cursor` (task +0x2C), `members` [[fleet key, fleet record +0x38]] |
| `ai_regroup` | 0x4b0c90 (fleet task state 0xC) | entry + exit; the walk VA 0x41ae20 on exit when called from it | `op`, `fleet` (task fleet), `members` (op +0x2C member fleets as fleet-record lines, read on entry; > 1 = the walk runs), `walk` (null = not run, else the walk's list in order: [value = squared distance, then the entry's fleet-record line]), `cmd` (the 0x201: ArgRef = the found fleet, else the slot +0x54 system; or null) |
| `ai_t1target` | 0x47a870 (type-1 character-op target search, thiscall op, out SdRef*) | entry + exit | `op` (op key +0x18), `before` (op +0x28 on entry), `picked` (the returned key; not a system = no target) |
| `ai_t1list` | 0x419a00 (sector list) / 0x41a340 (system list), only when called from VA 0x47a870 | exit | `list` (`sector` / `system`), `stage` (1 = with req24 0x40000000 / req28 0x80000000, 2 = fallback), `sector` (system list: the picked sector), `entries` [key, value (sector +0x108 / system +0xCC), rank] in list order; precede their `ai_t1target` |
| `ai_hub` | hub step D: 0x4dd120 / 0x4e4a20 / 0x4da610 / 0x4d1ed0 / 0x4e1b00 / 0x4d67e0 / 0x4ced60 / 0x4cc360 (planners 1, 2, 3, 5, 6, 7, 10, 11; fastcall planner) | entry + exit, only when the hub set changed | `planner`, `before` / `after` (the planner's hub set: systems with C bit 28 from this planner) |
| `ai_task` | 0x4adb70 (fleet-task machine, produce state 5) | exit, every call | `before` / `after` (task +0x08), `done`, `fleet`, `cmd`; printed with `--picks` |
| `ai_mode` | 0x4bf490 (planner 13 mode choice) | entry + exit | `before` / `after` (planner +0x5C mode), `model08` (producer request bits), `model04` |
| `ai_theaters` | 0x4e5530 (strategy theaters sub-step) | entry bit + exit | `ran` (model +0x04 bit 2 on entry: the ranking ran), `theaters` (count after), `f10` (strategy +0x10) |

Every event carries `tick` (clock +0x0C) and `day`.

Run for the user (Empire, a few game days, no special orders needed):

```
python tools/frida/trace.py --ai-hooks --side empire --galaxy standard --difficulty novice --speed 4 --note "ai hooks, empire, N days"
python tools/frida/ai_report.py TRACE.jsonl --side 1 --to-tick 400 > ai.txt     # side 1 = the AI (Alliance) when the player is the Empire
```

**Reproducing an earlier run's galaxy** (`--seed N`): forces the new-game seed (the `seed` call returning to VA 0x406152 / 0x4060f0 with argument 0) to N, logged as `seed_forced`. The setup is then that run's galaxy (owners, supports, facilities, ids); the AI's UI generator is still time-seeded, so its tie flips and later decisions can differ from the original run. Same side / galaxy / difficulty as the run being reproduced. Run 1: `--seed 874334749 --side empire --galaxy standard --difficulty novice --speed 4`.

Play until about day 5 or later (enough for the Alliance AI to create a fleet operation and a theater), then quit the game. `ai_report.py` filters: `--key 0x900000NN`, `--side`, `--from-tick` / `--to-tick`. `--picks` also prints the `ai_pick` lines (many). `support_set` lines are printed with the others (filtered by `--key`).

### Recording for the open AI items

Run 3's galaxy (= run 10's galaxy), player Alliance, all AI hooks, no orders, about 300 game days, then quit:

```
python tools/frida/trace.py --ai-hooks --seed 532947196 --side alliance --galaxy standard --difficulty novice --speed 4 --note "run 3 galaxy, all AI hooks incl. ai_charop / ai_p3budget / ai_p3c / ai_rk / ai_supview / ai_regroup walk, no orders, 300 days"
python tools/frida/ai_report.py TRACE.jsonl > ai.txt
```

What it settles (the AI's UI generator is time-seeded, so the game is not run 3 / 10 tick for tick: each item is settled by the same situation recurring, not by the same tick):

- `ai_rk`: runs 3 / 9 recon staging: which of readings (a) / (b) / (c) drops or orders the staging candidates, from the slot words of the closer sector in any staging call.
- `ai_step`, `ai_charop`, `ai_p3budget`, `ai_p3c` (with the other AI hooks): run 12 tick 655 and planner 3's request step C.
- `ai_regroup` (+ `ai_opstatus` fleets): run 10 tick 331. Run 10 is this galaxy with the same AI side (Empire); its fleet op 1 had two member fleets regrouping from tick 109 on, so a 300-day run very likely has a two-member regroup again. Any `ai_regroup` line with two or more members where a member other than the task fleet is missing from `walk` shows which record word fails the filter (+0x30 != op, +0x38 bit 0, 0xF0000002). If no line has two members, a run-10 situation needs a longer run (to day 400+) of the same command.
- `ai_supview`: scope of the support copy: the setter caller (`ra`), view side / state and context words for every view copy, live and frozen.
- `view_live` / `view_destroyed`: why the other side acknowledges (0x205) the end of a failed mission: when that side's mission view turns live and whether its Destroyed hook posts 0x302. Unverified on a live run; `view_live` assumes ECX = the real object with the link block at +0x20 (views at +0x28 / +0x2C).
- `ai_t1target` / `ai_t1list`: run 12 tick 655: which sector and hub a type-1 character op takes when two hubs (in two sectors) are unclaimed at its search, with the sector +0x108 values and the stage. Any line where stage 1 is empty and stage 2 lists two sectors settles the fallback order; run 13 did not have one.
- `ai_hub` with `ai_t1target` / `ai_t1list` / `ai_charop`: the duplicate type-1 targets of runs 8 / 9 / 10 / 12. Each `ai_t1target` line after an op's first one is a failed target keep; an `ai_hub` line just before it, in which the op's target leaves a set, shows the hub move that caused it. The run-3 galaxy (run 9 / 12 / 13 command) to day 140+ and game C (`--seed 807887694 --side alliance`) to day 127 cover the four cases.
- `ai_abduct` / `ai_rescue`: run 8 tick 118 if an abduction search with the same shape recurs here; the exact situation is game C's galaxy (`--seed 807887694 --side alliance`, same other options).


## Draw tracer (`--agent draw_agent.js`)

```
python tools/frida/trace.py --agent draw_agent.js --note "what was opened"   # direct launch; output rebellion-assets/traces/draw-<time>.jsonl
python tools/frida/trace.py --agent draw_agent.js --keep-briefing            # let the game-start briefing play (skipped by default)
```

- Use the direct launch: under DxWnd the exe's DirectDraw / GDI calls arrive from DxWnd's stubs and fail the caller filter.
- API hooks (only calls whose return address is in the exe): GDI text, blits, shapes, fonts, brushes, pens, palettes (`textout`, `drawtext`, `bitblt`, `setdibits`, `patblt`, `rectangle`, `moveto` / `lineto` / `polyline`, `font`, `palette*`, `select`, `dc`, `paint`); resource loads `res` (`module:type:id`); DirectDraw vtables after `DirectDrawCreate` (`ddsurface`, `ddblt`, `ddbltfast`, `ddflip`, `ddpalette*`, `lock_summary` = Lock counts per caller every 2 s).
- Bitmap class hooks (exe VAs, prologue bytes checked). Objects get tags `b<n>`:
  - `bm_new`: VA 0x5fbd20 `res` (resource bitmap: `module:2:id`), 0x5fbc80 `mem` (from data in memory), 0x5fbda0 `canvas` (blank, template `from`), 0x5fbea0 `section` (DIB section, template `from`); `w` / `h` from the header (negative h = top-down). `bm_free` = VA 0x5fbfa0.
  - `bm_blit` VA 0x5fcc30 (this = source; dst, dx, dy, sx, sy, w, h; 0 = full): opaque copy. `bm_kblit` VA 0x5fc710 (this = source; dst, dx, dy, key, sx, sy, w, h): keyed copy. `bm_kdraw` VA 0x5fd0f0 (this = source; dst, dx, dy): keyed copy of the whole source with its own key. `bm_fill` VA 0x5fcfe0 (this = destination; x, y, w, h, colour index).
  - `bm_draw` VA 0x5fc140 (this = bitmap; dc, mode 0x10 section / 0x40 via CreateDIBitmap, rop, x, y, sx, sy, w, h): bitmap to a DC; the `bitblt` that follows is its GDI side.
  - Directions checked on a main-menu trace: cockpit 20001 -> backbuffer section (opaque), backbuffer region -> button canvas, button sprite keyed (key 252) onto the canvas, canvas -> window DC.
- Briefing skip: every `FindResourceA` on `albrief.dll` / `embrief.dll` returns NULL (as without the DLL).
- Traces contain palettes and strings from the game: keep them outside the repo like the RNG traces.
