"""Print the optional AI hook events (trace.py --ai-hooks) of a trace readably.

python tools/frida/ai_report.py TRACE.jsonl [--key 0x90000112] [--side 1] [--from-tick N] [--to-tick N] [--picks]
"""
import argparse
import json


def fleet_rec(f):
    """[key, +0x30, +0x38, location, location C] of agent.js fleetRec ([key, None] = no record)."""
    if len(f) < 5 or f[1] is None:
        return f"{f[0]} no record"
    return f"{f[0]} +0x30 {f[1]} +0x38 {f[2]} at {f[3]} C {f[4]}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("trace")
    ap.add_argument("--key", help="only system records with this key (ai_sysrec)")
    ap.add_argument("--side", type=int, help="only system records of this side (ai_sysrec)")
    ap.add_argument("--picks", action="store_true", help="also print every planner pick (ai_pick, many lines)")
    ap.add_argument("--from-tick", type=int, default=0)
    ap.add_argument("--to-tick", type=int, default=1 << 30)
    a = ap.parse_args()
    for line in open(a.trace, encoding="utf-8"):
        e = json.loads(line)
        ev = e.get("ev", "")
        if ev == "support_set" and a.from_tick <= (e.get("tick") or 0) <= a.to_tick:
            if not a.key or e["sys"] and e["sys"].lower() == a.key.lower():
                print(f"day {e.get('day')} tick {e.get('tick') or 0:>5} support_set     {e['sys']} {e['old']} -> {e['new']} (ra {e['ra']})")
            continue
        if not ev.startswith("ai_"):
            continue
        t = e.get("tick") or 0
        if not a.from_tick <= t <= a.to_tick:
            continue
        head = f"day {e.get('day')} tick {t:>5} {ev:<16}"
        if ev == "ai_sysrec":
            if a.key and e["key"].lower() != a.key.lower() or a.side and e["side"] != a.side:
                continue
            print(f"{head} side {e['side']} {e['key']} share {e['share48']} troops {e['troops88']} need {e['need98']} "
                  f"batteries {e['batteries74']} +0x118 {e['f118']}  (+0x28 {e['f28']} +0x2C {e['f2c']} +0x30 {e['f30']})")
        elif ev == "ai_t4targets":
            tg = " ".join(f"{k}={v}" for k, v in e["targets"].items())
            print(f"{head} op {e['op']} site {e['site']} {tg}")
        elif ev == "ai_t4site":
            print(f"{head} site {e['before']} -> {e['after']}")
        elif ev == "ai_opstatus":
            c = " ".join(f"{k}={v}" for k, v in e["counters"].items())
            print(f"{head} op {e['op']} +0x60 {e['f60']} +0x64 {e['f64']} {c}")
            for f in e.get("fleets") or []:
                print(f"{'':>31} fleet {fleet_rec(f)}")
        elif ev == "ai_sysview":
            if a.key and e["key"].lower() != a.key.lower() or a.side and e["side"] != a.side:
                continue
            print(f"{head} side {e['side']} {e['key']} view+0x58 {e['view58']} view owner {e['viewOwner']}")
        elif ev == "ai_sitelist":
            ent = " ".join(f"{k}:{v}/r{r}" for k, v, r in e["entries"])
            print(f"{head} idx {e['idx']} dir {e['dir']} [{ent}]")
        elif ev == "ai_t1list":
            ent = " ".join(f"{k}:{v}/r{r}" for k, v, r in e["entries"])
            print(f"{head} {e['list']} stage {e['stage']}{' sector ' + e['sector'] if e['sector'] else ''} [{ent}]")
        elif ev == "ai_hub":
            print(f"{head} planner {e['planner']} hub {','.join(e['before']) or '-'} -> {','.join(e['after']) or '-'}")
        elif ev == "ai_t1target":
            print(f"{head} op {e['op']} target {e['before']} -> picked {e['picked']}")
        elif ev == "ai_pick":
            if a.picks:
                print(f"{head} planner vtable {e['planner']} status {e['status']} prio {e['prio']}")
        elif ev in ("ai_surplus", "ai_split"):
            c = " ".join(f"{k}={v}" for k, v in e["counters"].items())
            cmd = e.get("cmd") or {}
            print(f"{head} op {e['op']} {c} flagship {e['flagship']} +0x64 {e['f64']} ships {','.join(e['ships'])} -> "
                  f"{cmd.get('type')} {[t[0] for t in cmd.get('targets') or []]} {cmd.get('arg_ref')}")
        elif ev == "ai_step":
            if a.picks:
                print(f"{head} planner {e['planner']} {e['before']} -> {e['after']} done {e['done']} cmd {e['cmd']}")
        elif ev == "ai_taskfleet":
            print(f"{head} op {e['op']} task fleet {e['fleet']} cursor {e['cursor']} members {e['members']}")
        elif ev == "ai_regroup":
            cmd = e.get("cmd") or {}
            print(f"{head} op {e.get('op')} fleet {e['fleet']} -> {cmd.get('type')} {[t[0] for t in cmd.get('targets') or []]} {cmd.get('arg_ref')}")
            for f in e.get("members") or []:
                print(f"{'':>31} member {fleet_rec(f)}")
            walk = e.get("walk")
            print(f"{'':>31} walk {'not run' if walk is None else len(walk)}")
            for w in walk or []:
                print(f"{'':>31}   d2 {w[0]} {fleet_rec(w[1:])}")
        elif ev == "ai_charop":
            if a.picks:
                print(f"{head} op {e['op']} ({e['vt']}) {e['before']} -> {e['after']} fin {e['fin']} char {e['char']} rec {e['rec']} target {e['target']} cmd {e['cmd']}")
        elif ev == "ai_task":
            if a.picks:
                print(f"{head} task {e.get('vt', '')} {e['before']} -> {e['after']} done {e['done']} fleet {e['fleet']} cmd {e['cmd']}")
        elif ev == "ai_mode":
            print(f"{head} mode {e['before']} -> {e['after']} model+0x08 {e['model08']} model+0x04 {e['model04']}")
        elif ev == "ai_theaters":
            print(f"{head} ranked {e['ran']} theaters {e['theaters']} strategy+0x10 {e['f10']}")
        elif ev == "ai_surplus_ship":
            print(f"{head} ship {e.get('key')} +0x24 {e.get('f24')} +0x7C {e.get('f7c')} +0x80 {e.get('f80')} +0x6C {e.get('f6c')} +0x60 {e.get('f60')} +0x48 {e.get('f48')}")
        elif ev in ("ai_rescue", "ai_abduct"):
            print(f"{head} op {e['op']} objective {e['objective']} target {e['target']} ret {e['ret']} ({e['retKey']})")
        elif ev == "ai_decoyn":
            print(f"{head} op {e['op']} decoys at staging {e['staging']}: {e['n']}")
        elif ev == "ai_theatercheck":
            print(f"{head} theaters {e['theaters']} add {e['add']} drop {e['drop']} model+0x04 {e['model04']}")
        else:
            print(head, e)


if __name__ == "__main__":
    main()
