"""Summarise an RNG trace from trace.py.

python tools/frida/rng_report.py TRACE.jsonl [--funcs FILE] [--all-gens] [--seq] [--day N]
python tools/frida/rng_report.py TRACE.jsonl --commands > COMMANDS.jsonl
python tools/frida/rng_report.py TRACE.jsonl --facts FACTS.json

Prints game-generator draws grouped by init step (caller, wrapper, n, count),
timer starts with the draws between consecutive starts, and draws outside init steps
per day. --funcs: text file of function start VAs (hex, one per line) to map
callers to containing functions. --seq: also list every draw in order.
--day N: also list every event of day N in order (day-tick hooks, compact).
--commands: only print the executed command stream as JSONL.
--facts OUT: only write the numbers-only state-event vector after setup (see facts()).
--battles OUT: only write the numbers-only automatic-battle vector (see battles()).
"""
import argparse
import bisect
import json
from collections import Counter, defaultdict


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trace")
    ap.add_argument("--funcs")
    ap.add_argument("--all-gens", action="store_true", help="include non-game generators")
    ap.add_argument("--seq", action="store_true")
    ap.add_argument("--day", type=int)
    ap.add_argument("--commands", action="store_true", help="print the command stream and exit")
    ap.add_argument("--facts", help="write the state-event vector (facts()) to this file and exit")
    ap.add_argument("--battles", help="write the automatic-battle vector (battles()) to this file and exit")
    a = ap.parse_args()
    if a.battles:
        evs = [json.loads(l) for l in open(a.trace, encoding="utf-8") if l.strip()]
        seed = next(e["state"] for e in evs if e["ev"] == "rng_ctor")
        out = battles(evs)
        with open(a.battles, "w", encoding="utf-8", newline="\n") as f:
            f.write('{"seed": %d, "battles": [\n' % seed)
            f.write(",\n".join(json.dumps(b, separators=(",", ":")) for b in out))
            f.write("\n]}\n")
        print(f"{a.battles}: seed {seed}, " + ", ".join(f"day {b['day']} {b['draws']} draws {len(b['dmg'])} applications" for b in out))
        return
    if a.facts:
        evs = [json.loads(l) for l in open(a.trace, encoding="utf-8") if l.strip()]
        seed = next(e["state"] for e in evs if e["ev"] == "rng_ctor")
        rows, kinds = facts(evs)
        with open(a.facts, "w", encoding="utf-8", newline="\n") as f:
            f.write('{"seed": %d, "kinds": %s,\n "facts": [\n' % (seed, json.dumps(kinds)))
            f.write(",\n".join("  " + json.dumps(r) for r in rows))
            f.write("\n]}\n")
        print(f"{a.facts}: seed {seed}, {len(rows)} facts", Counter(hex(r[1]) for r in rows))
        return
    if a.commands:
        for r in commands(json.loads(l) for l in open(a.trace, encoding="utf-8") if l.strip()):
            print(json.dumps(r, separators=(",", ":")))
        return

    starts = []
    if a.funcs:
        starts = sorted(int(l, 16) for l in open(a.funcs) if l.strip())

    def fn(addr):
        v = int(addr, 16)
        i = bisect.bisect_right(starts, v) - 1
        return f"{addr} (fn {starts[i]:#x})" if i >= 0 else addr

    evs = [json.loads(l) for l in open(a.trace, encoding="utf-8") if l.strip()]
    for e in evs:
        if e["ev"] in ("meta", "module", "verify_failed", "error", "script_error", "seed", "rng_ctor"):
            print(json.dumps(e))
    print()

    draws = [e for e in evs if e["ev"] == "rand" and (a.all_gens or e["gen"] == "game")]
    by_step = defaultdict(Counter)
    for d in draws:
        by_step[d["step"] if "step" in d else f"- day {d.get('day')}"][(d["caller"], d.get("via", "-"), d["n"])] += 1
    print("== draws by init step (caller, via, n): count")
    for step in sorted(by_step, key=lambda s: (1, int(s.split()[-1])) if isinstance(s, str) else (0, s)):
        c = by_step[step]
        steps = sum(k for (_, _, n), k in c.items() if n > 1)
        print(f"step {step}: {steps} draws")
        for (caller, via, n), k in sorted(c.items(), key=lambda x: (x[0][0], x[0][2])):
            print(f"  {fn(caller)} via {via} n={n}: {k}" + (" (n <= 1: no generator step)" if n <= 1 else ""))

    print("\n== timer starts and draws since the previous start (seq order)")
    pending = []
    for e in evs:
        if e["ev"] == "rand" and e["n"] > 1 and (a.all_gens or e["gen"] == "game"):
            pending.append(e)
        elif e["ev"] == "timer_start":
            if pending:
                print(f"  {len(pending)} draws: " + ", ".join(f"{fn(d['caller'])}/{d.get('via', '-')}/n={d['n']}" for d in pending[:6])
                      + (" ..." if len(pending) > 6 else ""))
            pending = []
            print(f"#{e['seq']} timer_start type={e['type']} step={e.get('step')} base={e.get('base')} var={e.get('var')} "
                  f"fire_day={e.get('fire_day')} ok={e['ok']} from {fn(e['ra'])}")
    if pending:
        print(f"  {len(pending)} draws after the last start")

    if a.seq:
        print("\n== all draws")
        for d in draws:
            print(f"#{d['seq']} day={d.get('day')} step={d.get('step')} {d['gen']} n={d['n']} -> {d['result']} "
                  f"state {d['before']}->{d['after']} {fn(d['caller'])} via {d.get('via', '-')}")

    if a.day is not None:
        print(f"\n== day {a.day}")
        for e in evs:
            if e.get("day") != a.day or (e["ev"] == "rand" and e["gen"] != "game" and not a.all_gens):
                continue
            f = {k: v for k, v in e.items() if k not in ("seq", "ev", "day", "before", "after", "ra")}
            if e["ev"] == "rand":
                f["caller"] = fn(e["caller"])
            print(f"#{e['seq']} {e['ev']} " + json.dumps(f, separators=(",", ":")))


CMD_KEYS = ("type", "side", "targets", "arg_ref", "arg_int", "fields")


def cmd_fields(e):
    f = {k: e.get(k) for k in CMD_KEYS}
    if f["targets"]:
        # node [key +0x18, value +0x1C] = [object SdRef, selection mode]; mode 0/1 = the object itself,
        # else the object expands into a set by mode and command type
        f["targets"] = [[k, int(v, 16)] for k, v in f["targets"]]
    return f


def commands(evs):
    """Executed commands of type >= 0x200 with their sub-commands, FIFO order events and created objects."""
    cur, n = None, 0
    for e in evs:
        ev = e["ev"]
        if ev == "command":
            if cur:
                yield cur
            cur = None
            t = e.get("type")
            if t is None or int(t, 16) < 0x200:
                continue
            cur = {"day": e.get("day"), "tick": e.get("tick"), "n": n, **cmd_fields(e),
                   "ok": None, "serial": None, "sub": [], "events": [], "created": []}
            n += 1
        elif cur is None:
            continue
        elif ev == "day":  # a command's effects finish within its tick; later ticks belong to timed events
            yield cur
            cur = None
        elif ev == "cmd_exec":
            if e["depth"] == 0:
                cur["ok"], cur["serial"] = e["ok"], e["serial"]
            else:
                cur["sub"].append({**cmd_fields(e), "ok": e["ok"], "serial": e["serial"], "depth": e["depth"]})
        elif ev == "event_run" and e["queue"] == "fifo":
            cur["events"].append({k: e[k] for k in ("type", "target", "hdr", "p", "lists") if k in e})
        elif ev == "event_run" and e["type"] == "0x302" and e.get("a4c") == 3:  # owner 3 -> side: new object
            cur["created"].append(e["target"])
    if cur:
        yield cur


FACT_NOTIFS = {0x300, 0x301, 0x302, 0x306, 0x308, 0x30C, 0x30D, 0x320, 0x321}  # Existing, Usable, owner (old 3 = created),
# HyperdriveActive, Completed, mission ReadyForNextPhase, Phase, parent (SdRefs), agent RoleMissionKey (SdRefs, none = 2)


def facts(evs):
    """State events after setup (game ready) in trace order, numbers only: [day, kind, object, old, new].
    kind in FACT_NOTIFS: notification run (event_run, a4c = old, a50 = new);
    0x60 / 0x64: mission TaskStatus / CompletionStatus change (mission_task / mission_done, old != new);
    0x387: travel timer start (object from its timer_delay, old = base, new = fire day).
    Also returns the kinds the trace's hooks cover (older traces lack mission_task / mission_done)."""
    end = max(i for i, e in enumerate(evs) if e["ev"] == "init_end" and e["step"] == 25)
    target, rows = {}, []
    for i, e in enumerate(evs):
        ev = e["ev"]
        if ev == "timer_delay":
            target[e["event"]] = int(e["target_3c"], 16)
        if i <= end:
            continue
        if ev == "event_run" and e["queue"] == "priority" and int(e["type"], 16) in FACT_NOTIFS:
            rows.append([e["day"], int(e["type"], 16), int(e["target"], 16), e["a4c"], e["a50"]])
        elif ev in ("mission_task", "mission_done") and e["old"] != e["new"]:
            rows.append([e["day"], 0x60 if ev == "mission_task" else 0x64, int(e["mission"], 16), e["old"], e["new"]])
        elif ev == "timer_start" and e["type"] == "0x387":
            rows.append([e["day"], 0x387, target[e["event"]], e["base"], e["fire_day"]])
    hooks = {e["name"] for e in evs if e["ev"] == "hook"}
    kinds = FACT_NOTIFS | {0x387} | {k for k, h in ((0x60, "mission_task"), (0x64, "mission_done")) if h in hooks}
    return rows, sorted(kinds)


def battles(evs):
    """Automatic battles, numbers only. A battle starts at the engine generator's seed event
    (ra 0x5af6cb) and its stream ends at the next battle_apply start. Per battle: day, draws = engine rand(100) count, dmg =
    one row per bt_dmg in order [unit SdRef, key +0xEC, state, d, shield before, hull before, shield after, hull after,
    engine draws since the previous bt_dmg (they precede the line), [k of each bt_crit since the previous bt_dmg]];
    S = battle_sides; ships = [unit, C record words, float hull] and fighters = [unit, F record words] as applied
    (apply_ship / apply_fighter followed by its record)."""
    out, cur, gen, draws, crits, unit, kind = [], None, None, 0, [], None, None
    for e in evs:
        ev = e.get("ev")
        if ev == "seed" and e.get("ra") == "0x5af6cb":
            cur = {"day": e.get("day", 0), "draws": 0, "dmg": [], "S": [], "ships": [], "fighters": []}
            out.append(cur)
            gen, draws, crits, unit, kind = e["gen"], 0, [], None, None
        if cur is None:
            continue
        if ev == "rand" and e["gen"] == gen and gen is not None:
            cur["draws"] += 1
            draws += 1
        elif ev == "bt_crit" and gen is not None:
            crits.append(e["k"])
        elif ev == "bt_dmg" and gen is not None:
            cur["dmg"].append([(e["fam"] << 24) | e["id"], e["key"], e["st"], e["d"], *e["before"], *e["after"], draws, crits])
            draws, crits = 0, []
        elif ev == "battle_apply" and e.get("phase") == "start":
            gen = None
        elif ev == "battle_apply" and e.get("phase") == "end":
            cur = None
        elif ev == "battle_sides":
            cur["S"] = e["v"]
        elif ev in ("apply_ship", "apply_fighter", "apply_fighter_parent"):
            unit, kind = int(e["unit"], 16), ev
        elif ev == "battle_ship" and kind == "apply_ship":
            cur["ships"].append([unit, e["v"], e["f"]])
        elif ev == "battle_fighter" and kind == "apply_fighter":
            cur["fighters"].append([unit, e["v"]])
    return out


if __name__ == "__main__":
    main()
