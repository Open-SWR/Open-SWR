"""Spawn or attach to the original game exe with agent.js; write events as JSONL.

python tools/frida/trace.py [--dxwnd | --attach] [--game-dir DIR] [--dxwnd-dir DIR] [--out FILE]
    [--side alliance|empire] [--galaxy small|standard|large] [--difficulty novice|intermediate|expert]
    [--speed N] [--note TEXT] [--ai-hooks] [--seed N] [-- exe args]

Default: spawn REBEXE.EXE directly (raw DirectDraw: wrong colours, flipped icons,
crashed once in a surface fill; game logic unaffected). --dxwnd starts it through
DxWnd (as REBEXE-DXWND.cmd does) with Frida child gating; the user's DxWnd setup
blocks most new-game dialog clicks, so it is not the default. --attach attaches
to a running REBEXE.EXE.

The run settings are not visible in the trace; pass them so the first JSONL line
(ev "meta") records them.
"""
import argparse
import datetime
import json
import os
import queue
import sys
import threading

import frida

EXE = "REBEXE.EXE"
HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game-dir", default=os.environ.get("OPENSWR_GAME_DIR"), help="the original game's folder (default: env OPENSWR_GAME_DIR)")
    ap.add_argument("--attach", action="store_true", help=f"attach to a running {EXE} instead of spawning")
    ap.add_argument("--dxwnd", action="store_true", help=f"start {EXE} through DxWnd instead of directly")
    ap.add_argument("--dxwnd-dir", default=os.environ.get("OPENSWR_DXWND_DIR"), help="DxWnd's folder for --dxwnd (default: env OPENSWR_DXWND_DIR)")
    ap.add_argument("--dxwnd-args", default="/C:rebellion.ini /R:1", help="DxWnd config + target index")
    ap.add_argument("--out", help="JSONL output (default: <env OPENSWR_TRACE_DIR>/rng-<time>.jsonl)")
    ap.add_argument("--side", choices=["alliance", "empire"], help="player side chosen in the new-game dialog")
    ap.add_argument("--galaxy", choices=["small", "standard", "large"])
    ap.add_argument("--difficulty", choices=["novice", "intermediate", "expert"])
    ap.add_argument("--speed", type=int, help="game speed used (1 slowest .. 4 fastest)")
    ap.add_argument("--note", help="free text: orders given, days played, anything unusual")
    ap.add_argument("--ai-hooks", action="store_true", help="also install the AI record / fleet-op / theater hooks")
    ap.add_argument("--seed", type=int, help="force the game generator seed (the new-game seed call at VA 0x406152 / 0x4060f0) "
                    "to reproduce an earlier run's galaxy, e.g. 874334749 for run 1; the AI's UI generator stays time-seeded")
    ap.add_argument("--agent", default="agent.js", help="agent script in this folder, e.g. draw_agent.js (draw-call tracer)")
    ap.add_argument("--keep-briefing", action="store_true", help="draw_agent.js: let the game-start briefing play (skipped by default)")
    ap.add_argument("exe_args", nargs="*")
    a = ap.parse_args()
    if not a.attach and not a.dxwnd and not a.game_dir:
        ap.error("the game folder is needed: pass --game-dir or set OPENSWR_GAME_DIR")
    if a.dxwnd and not a.dxwnd_dir:
        ap.error("--dxwnd needs DxWnd's folder: pass --dxwnd-dir or set OPENSWR_DXWND_DIR")
    if not a.out and not os.environ.get("OPENSWR_TRACE_DIR"):
        ap.error("no output: pass --out or set OPENSWR_TRACE_DIR")

    prefix = "rng" if a.agent == "agent.js" else os.path.splitext(a.agent)[0].removesuffix("_agent")
    out = a.out or os.path.join(os.environ["OPENSWR_TRACE_DIR"],
                                datetime.datetime.now().strftime(prefix + "-%Y%m%d-%H%M%S.jsonl"))
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    f = open(out, "w", encoding="utf-8")
    mode = "attach" if a.attach else "dxwnd" if a.dxwnd else "direct"
    meta = {"ev": "meta", "launch": mode, "time": datetime.datetime.now().isoformat(timespec="seconds"), "side": a.side,
            "galaxy": a.galaxy, "difficulty": a.difficulty, "speed": a.speed, "note": a.note, "exe_args": a.exe_args, "ai_hooks": a.ai_hooks,
            "forced_seed": a.seed, "agent": a.agent}
    f.write(json.dumps(meta, separators=(",", ":")) + "\n")
    done = threading.Event()
    counts = {"events": 0}

    def on_message(msg, _data):
        if msg["type"] == "send":
            p = msg["payload"]
            f.write(json.dumps(p, separators=(",", ":")) + "\n")
            counts["events"] += 1
            if p["ev"] in ("module", "hook", "verify_failed", "error", "hook_error"):
                print(json.dumps(p), flush=True)
        else:  # script error
            print(msg, file=sys.stderr, flush=True)
            f.write(json.dumps({"ev": "script_error", "msg": msg}) + "\n")
        f.flush()

    def on_detached(reason, *_):
        print("detached:", reason, flush=True)
        f.write(json.dumps({"ev": "detached", "reason": reason}) + "\n")
        f.flush()
        done.set()

    def instrument(pid):
        session = dev.attach(pid)
        session.on("detached", on_detached)
        with open(os.path.join(HERE, a.agent), encoding="utf-8") as js:
            src = js.read()
            if a.ai_hooks:
                src = src.replace("'use strict';", "'use strict';\nglobalThis.AI_HOOKS = true;", 1)
            if a.keep_briefing:
                src = src.replace("'use strict';", "'use strict';\nglobalThis.KEEP_BRIEFING = true;", 1)
            if a.seed is not None:
                src = src.replace("'use strict';", "'use strict';\nglobalThis.FORCE_SEED = %d;" % a.seed, 1)
                if "globalThis.FORCE_SEED" not in src:
                    raise SystemExit("could not inject --seed into agent.js")
            script = session.create_script(src)
        script.on("message", on_message)
        script.load()
        return session

    # The user's compat layer marks the exe RUNASADMIN, which makes a non-elevated
    # spawn fail (0x2e4). RunAsInvoker overrides it for this launch only (inherited
    # by DxWnd's child, as in REBEXE-DXWND.cmd).
    env = dict(os.environ, __COMPAT_LAYER="RunAsInvoker")
    dev = frida.get_local_device()
    launcher = None
    if a.attach:
        session = instrument(dev.get_process(EXE).pid)
    elif not a.dxwnd:
        env["__COMPAT_LAYER"] = "RunAsInvoker DWM8And16BitMitigation HighDpiAware"
        pid = dev.spawn([os.path.join(a.game_dir, EXE), *a.exe_args], cwd=a.game_dir, env=env)
        session = instrument(pid)
        dev.resume(pid)
    else:
        if a.exe_args:
            sys.exit("exe args are not passed through DxWnd; set them in the DxWnd target (cmdline0)")
        # Child gating holds every process DxWnd creates until resumed; the game is
        # instrumented before it runs, DxWnd's own injection then proceeds as usual.
        children = queue.Queue()
        dev.on("child-added", lambda child: children.put(child))
        lpid = dev.spawn([os.path.join(a.dxwnd_dir, "dxwnd.exe"), *a.dxwnd_args.split()], cwd=a.dxwnd_dir, env=env)
        launcher = dev.attach(lpid)
        launcher.enable_child_gating()
        dev.resume(lpid)
        print("waiting for DxWnd to start the game", flush=True)
        session = None
        while session is None:
            try:
                child = children.get(timeout=0.5)
            except queue.Empty:
                continue
            if os.path.basename(child.path or "").upper() == EXE:
                session = instrument(child.pid)
            else:
                print("other child, not traced:", child.path, flush=True)
            dev.resume(child.pid)
    print(f"tracing -> {out} (Ctrl+C to stop)", flush=True)
    try:
        while not done.wait(0.5):
            pass
    except KeyboardInterrupt:
        session.detach()
    if launcher is not None:
        launcher.detach()
    f.close()
    print(f"{counts['events']} events written", flush=True)


if __name__ == "__main__":
    main()
