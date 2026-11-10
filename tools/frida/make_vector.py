"""Cut the new-game setup draws of a trace.py trace into a differential test vector.

python tools/frida/make_vector.py TRACE.jsonl --funcs rebexe-funcs.txt --column 1 [--side 1] [--galaxy 1] --out VECTOR.json [--days DAYS.json]

Output: {"seed", "settings": {galaxySize, column, side, victory}, "draws": [[site, n, result, step], ...]}
site = start VA of the function containing the RNG wrapper's caller; n <= 1 = no generator step.
--days: post-setup vector: {"seed", "settings",
  "draws": [[day, site, n, result], ...]  game draws after init step 25,
  "timers": [[day, step, kind, type, target, base, variance, fireDay, serial, result], ...]}
  kind 0 = start (timer_start; target SdRef from its timer_delay), 1 = fire (type from the event's latest start; result 1 = re-inserted);
  step = init step for setup starts, 0 after setup; result -1 for starts.
Numbers only: no strings or data from game files.
"""
import argparse
import bisect
import json


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trace")
    ap.add_argument("--funcs", required=True)
    ap.add_argument("--column", type=int, required=True)
    ap.add_argument("--galaxy", type=int, default=1)
    ap.add_argument("--side", type=int)
    ap.add_argument("--out", required=True)
    ap.add_argument("--days")
    a = ap.parse_args()

    starts = sorted(int(l, 16) for l in open(a.funcs) if l.strip())
    evs = [json.loads(l) for l in open(a.trace, encoding="utf-8") if l.strip()]
    seed = next(e["state"] for e in evs if e["ev"] == "rng_ctor")
    draws, x = [], seed
    for e in evs:
        if e["ev"] != "rand" or e["gen"] != "game" or not 9 <= e.get("step", 0) <= 25:
            continue
        site = starts[bisect.bisect_right(starts, int(e["caller"], 16)) - 1]
        if e["n"] > 1:  # generator check
            assert e["before"] == x, e
            x = 41358 * x % 0x7FFFFFFF
            assert (e["after"], e["result"]) == (x, x // (0x7FFFFFFE // e["n"])), e
        draws.append([f"{site:#x}", e["n"], e["result"], e["step"]])

    settings = {"galaxySize": a.galaxy, "column": a.column, "side": a.side, "victory": None}
    if a.days:
        write_days(a.days, evs, starts, seed, settings)

    out = {"seed": seed, "settings": {"galaxySize": a.galaxy, "column": a.column, "side": a.side, "victory": None}, "draws": draws}
    with open(a.out, "w", encoding="utf-8", newline="\n") as f:
        f.write('{"seed": %d, "settings": %s,\n "draws": [\n' % (seed, json.dumps(out["settings"])))
        f.write(",\n".join("  " + json.dumps(d) for d in draws))
        f.write("\n]}\n")
    print(f"{a.out}: seed {seed}, {len(draws)} draws, {sum(d[1] > 1 for d in draws)} stepping")


def write_days(path, evs, starts, seed, settings):
    end = max(i for i, e in enumerate(evs) if e["ev"] == "init_end" and e["step"] == 25)
    x = seed
    draws, timers, step, target, type_of = [], [], 0, {}, {}
    for i, e in enumerate(evs):
        if e["ev"] == "init":
            step = e["step"]
        elif e["ev"] == "init_end":
            step = 0
        elif e["ev"] == "rand" and e["gen"] == "game":
            if e["n"] > 1:  # generator check over the whole trace
                assert e["before"] == x, e
                x = 41358 * x % 0x7FFFFFFF
            if i > end:
                site = starts[bisect.bisect_right(starts, int(e["caller"], 16)) - 1]
                draws.append([e["day"], f"{site:#x}", e["n"], e["result"]])
        elif e["ev"] == "timer_delay":
            target[e["event"]] = int(e["target_3c"], 16)
        elif e["ev"] == "timer_start":
            type_of[e["event"]] = int(e["type"], 16)
            timers.append([e["day"], step, 0, int(e["type"], 16), target[e["event"]], e["base"], e["var"], e["fire_day"], e["serial"], -1])
        elif e["ev"] == "timer_fire":
            p = e["packed"]
            timers.append([e["day"], step, 1, type_of[e["event"]], int(e["target_3c"], 16), p >> 16, (p >> 1) & 0x7FFF,
                           e["fire_day"], e["serial"], e["result"]])
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write('{"seed": %d, "settings": %s,\n "draws": [\n' % (seed, json.dumps(settings)))
        f.write(",\n".join("  " + json.dumps(d) for d in draws))
        f.write('\n],\n "timers": [\n')
        f.write(",\n".join("  " + json.dumps(t) for t in timers))
        f.write("\n]}\n")
    print(f"{path}: {len(draws)} day draws ({sum(d[2] > 1 for d in draws)} stepping), {len(timers)} timer events")


if __name__ == "__main__":
    main()
