// Frida agent for the original game exe (32-bit, image base 0x400000).
// Emits one JSON object per event via send(); trace.py writes them as JSONL.
// VAs and argument layouts are set out in the hooks below; each hook checks its function's first bytes before
// installing (mismatch = "verify_failed" event, hook skipped).
// Calling conventions: "thiscall" = this in ECX, args on stack; read from the
// prologues listed in HOOKS (Inferred where noted).

'use strict';

const G_RNG = ptr(0x6b9054);     // game generator pointer (roll() reads it)
const G_CLOCK = ptr(0x6b2fcc);   // clock object pointer (getter VA 0x51ce00)
const G_INITSTEP = ptr(0x6b2bb4); // current init step (getter VA 0x51c000)

const mod = Process.getModuleByName('REBEXE.EXE');
let seq = 0;
function emit(ev, f) {
  f.seq = seq++;
  f.ev = ev;
  const d = day();
  if (d !== null) f.day = d;
  send(f);
}

// Surplus / split throttle (see the ai_surplus hook): per-call line buffer and the last logged state per (event, op).
let surplusBuf = null;
const lastLogged = new Map();
function surplusLine(ev, f) { if (surplusBuf !== null) surplusBuf.push([ev, f]); else emit(ev, f); }
function emitChanged(ev, f, lines) {
  const k = ev + ':' + f.op, { tick, ...rest } = f;
  const state = JSON.stringify([rest, lines.map(([e, l]) => { const { tick: t, ...r } = l; return [e, r]; })]);
  if (f.cmd === null && lastLogged.get(k) === state) return;
  lastLogged.set(k, state);
  emit(ev, f);
  for (const [e, l] of lines) emit(e, l);
}
const hex = (p) => (p === null || p === undefined ? null : '0x' + ptr(p).toString(16)); // rd() returns null on a failed read
function rd(p) { try { return p.readU32(); } catch (e) { return null; } }
function rdp(p) { try { return p.readPointer(); } catch (e) { return null; } }
function day() {
  const c = rdp(G_CLOCK);
  return c === null || c.isNull() ? null : rd(c.add(0x10));
}
function genName(self) {
  const g = rdp(G_RNG);
  return g !== null && !g.isNull() && g.equals(self) ? 'game' : hex(self);
}

// Outermost RNG wrapper (roll/between/percent/chance) caller, so a draw is
// attributed to game code instead of the wrapper.
// shortcut: single global (the sim draws on one thread), upgrade to per-thread state if a hooked rand() ever runs on another thread.
let wrap = null;
function wrapper(name) {
  return {
    onEnter() { if (wrap === null) { wrap = { name, caller: this.returnAddress }; this.own = true; } },
    onLeave() { if (this.own) wrap = null; },
  };
}

let initStep = null;
let lastEvent = null;  // event object seen by the last timer-delay call
let clock = null, lastDay = null;

// [VA, expected first bytes (hex), name, callbacks]
const HOOKS = [
  // rand(n): thiscall, n on stack; generator state at this+4 (prologue reads [esi+4]).
  [0x5f5700, '56578b7c240c', 'rand', {
    onEnter(args) {
      this.self = this.context.ecx;
      this.n = args[0].toInt32();
      this.before = rd(this.self.add(4));
      this.w = wrap;
      this.ra = this.returnAddress;
    },
    onLeave(ret) {
      const f = { gen: genName(this.self), n: this.n, before: this.before,
                  after: rd(this.self.add(4)), result: ret.toInt32(), ra: hex(this.ra) };
      if (this.w) { f.via = this.w.name; f.caller = hex(this.w.caller); } else f.caller = f.ra;
      if (initStep !== null) f.step = initStep;
      emit('rand', f);
    },
  }],
  // seed(s): thiscall, s on stack, returns the seed used (s or time-based).
  [0x5f56b0, '538b5c2408', 'seed', {
    onEnter(args) {
      this.self = this.context.ecx; this.s = args[0].toInt32(); this.ra = this.returnAddress;
      // trace.py --seed N: the new-game seed call (arg 0 = time-based) gets N instead (return 0x406152 / 0x4060f0, as in runs 1-4).
      if (globalThis.FORCE_SEED !== undefined && this.s === 0 && (this.ra.equals(ptr(0x406152)) || this.ra.equals(ptr(0x4060f0)))) {
        args[0] = ptr(globalThis.FORCE_SEED >>> 0); emit('seed_forced', { seed: globalThis.FORCE_SEED, ra: hex(this.ra) });
      }
    },
    onLeave(ret) {
      emit('seed', { gen: genName(this.self), self: hex(this.self), arg: this.s,
                     result: ret.toInt32(), state: rd(this.self.add(4)), ra: hex(this.ra) });
    },
  }],
  // generator ctor(s): thiscall; stores the seed as state after seed().
  [0x5f5680, '8b44240456', 'rng_ctor', {
    onEnter(args) { this.self = this.context.ecx; this.s = args[0].toInt32(); },
    onLeave() { emit('rng_ctor', { self: hex(this.self), arg: this.s, state: rd(this.self.add(4)) }); },
  }],
  [0x53e290, '8b0d54906b00', 'roll', wrapper('roll')],
  [0x53e2c0, '8b44240856', 'between', wrapper('between')],
  [0x53e2e0, 'a1881a6600', 'percent', wrapper('percent')],
  [0x53e2f0, 'e8ebffffff', 'chance', wrapper('chance')],
  // New-game init step switch: step number from the global the switch reads.
  [0x5136d0, '6aff68a8246400', 'init_step', {
    onEnter() { initStep = rd(G_INITSTEP); emit('init', { step: initStep }); },
    onLeave() { emit('init_end', { step: initStep }); initStep = null; },
  }],
  // Timer start: cdecl (type, target object, slot*, context); returns success.
  // Arg 2 = slot {Serial, Packed}, arg 3 = context: Inferred from the copy into
  // event +0x44 / +0x20.
  [0x53fb00, '64a1000000006aff', 'timer_start', {
    onEnter(args) {
      this.type = args[0].toInt32();
      this.target = args[1];
      this.slot = args[2];
      this.ctx = args[3];
      this.ra = this.returnAddress;
      lastEvent = null;
    },
    onLeave(ret) {
      const packed = rd(this.slot.add(4));
      const f = { type: hex(this.type), target: hex(this.target), serial: rd(this.slot),
                  packed, ctx: hex(this.ctx), ok: ret.toInt32(), ra: hex(this.ra) };
      if (packed !== null) { f.base = packed >>> 16; f.var = (packed >>> 1) & 0x7fff; f.active = packed & 1; }
      if (lastEvent) { f.event = hex(lastEvent); f.fire_day = rd(lastEvent.add(0x40)); }
      if (initStep !== null) f.step = initStep;
      emit('timer_start', f);
    },
  }],
  // Timer delay = Base + roll(Variance): thiscall on the event; Packed at +0x48.
  [0x586130, '568bf18b4648', 'timer_delay', {
    onEnter() { this.ev = this.context.ecx; lastEvent = this.ev; },
    onLeave(ret) {
      const e = this.ev, packed = rd(e.add(0x48));
      emit('timer_delay', { event: hex(e), type_24: hex(rd(e.add(0x24))), target_3c: hex(rd(e.add(0x3c))),
                            base: packed >>> 16, var: (packed >>> 1) & 0x7fff, delay: ret.toInt32() });
    },
  }],
  // Timer fire: thiscall on the event. +0x24 type (Inferred), +0x3c target
  // SdRef (Inferred), +0x40 fire day, +0x44/+0x48 slot copy.
  [0x5862a0, '6aff6898f66400', 'timer_fire', {
    onEnter() {
      const e = this.ev = this.context.ecx;
      this.f = { event: hex(e), vtable: hex(rd(e)), type_24: hex(rd(e.add(0x24))), target_3c: hex(rd(e.add(0x3c))),
                 fire_day: rd(e.add(0x40)), serial: rd(e.add(0x44)), packed: rd(e.add(0x48)) };
    },
    onLeave(ret) {
      this.f.result = ret.toInt32();
      this.f.next_fire_day = rd(this.ev.add(0x40));
      emit('timer_fire', this.f);
    },
  }],
  // Clock tick: thiscall on the clock; Day at +0x10, Tick at +0x0C. Logs day changes only.
  [0x51dea0, '568bf1e8', 'tick', {
    onEnter() { clock = this.context.ecx; },
    onLeave() {
      const d = rd(clock.add(0x10));
      if (d !== lastDay) { emit('day', { tick: rd(clock.add(0x0c)), ticks_per_day: rd(clock.add(0x1c)) }); lastDay = d; }
    },
  }],

  // ---- Day-tick hooks. Objects are logged as SdRef (ref()).
  // Run one event: thiscall on the queue; the head (queue +8) is the event about to run.
  [0x54ec90, '5356578bf933dbe8', 'event_run', {
    onEnter() {
      const q = this.context.ecx, e = rdp(q.add(8));
      if (e === null || e.isNull()) return;
      const f = { queue: queueName(q), event: hex(e), vtable: hex(rd(e)), type: evType(e),
                  target: hex(rd(e.add(0x3c))), a4c: rd(e.add(0x4c)), a50: rd(e.add(0x50)) };
      const o = ORDER_EVENTS[f.vtable];
      if (o) {  // order event: header + payload u32s + node lists
        f.hdr = [rd(e.add(0x40)), rd(e.add(0x44)), hex(rd(e.add(0x48))), hex(rd(e.add(0x4c)))];
        f.p = u32s(e, 0x50, o[0]);
        if (o.length > 1) f.lists = o.slice(1).map((off) => refList(e.add(off)));
      }
      emit('event_run', f);
    },
  }],
  // Pending command executed (dispatch with all queues empty): cmd = args[0]. Logs the decoded command and the clock's command serial (+0x18) before execution.
  [0x51dd20, '64a1000000006aff', 'command', {
    onEnter(args) {
      const c = args[0];
      emit('command', Object.assign({ cmd: hex(c), vtable: hex(rd(c)), tick: clockField(0x0c), serial: clockField(0x18) }, cmdInfo(c)));
    },
  }],
  // Command appended to the pending list: thiscall clock, arg command.
  [0x51d880, '8b44240485c07410', 'command_add', {
    onEnter(args) {
      emit('command_add', { cmd: hex(args[0]), vtable: hex(rd(args[0])), type: cmdType(args[0]), ra: hex(this.returnAddress) });
    },
  }],
  // Command execute (vtable +0x20): thiscall command, arg result* {category, code}; code -1 = success.
  // depth > 0 = sub-command of a composite (move expansion), logged with its decoded fields.
  // Calling convention of the composite / fleet variants: Inferred from their prologues (same as base).
  [0x520040, '6aff687034640064', 'cmd_exec', cmdExec()],
  [0x53c240, '6aff68f863640064', 'cmd_exec_move', cmdExec()],
  [0x520480, '6aff682835640064', 'cmd_exec_name', cmdExec()],
  [0x5816f0, '6aff6868ec640064', 'cmd_exec_assault', cmdExec()],
  [0x5820a0, '6aff6838ee640064', 'cmd_exec_bombard', cmdExec()],
  // Side maintenance recompute: thiscall side. ra = notification handler that ran it.
  [0x52fff0, '6aff687054640064', 'maint', {
    onEnter() { this.s = this.context.ecx; this.f = { side: ref(this.s), ra: hex(this.returnAddress), before: sidePairs(this.s) }; },
    onLeave() { this.f.after = sidePairs(this.s); emit('maint', this.f); },
  }],
  // Mine/Refinery (capacity, allocated) pair write: thiscall facility, arg pair* {capacity, allocated}.
  [0x55a6e0, '64a1000000006aff', 'pair_set', {
    onEnter(args) {
      const f = this.context.ecx;
      emit('pair_set', { fac: ref(f), old: [rd(f.add(0x64)), rd(f.add(0x68))], new: [rd(args[0]), rd(args[0].add(4))],
                         flags: rd(f.add(0x60)), state: rd(f.add(0x58)) });
    },
  }],
  // Mine/Refinery cycle-length recompute: thiscall facility; result in timer block (+0x54) +0x20.
  [0x55abf0, '6aff68089b640064', 'cycle', {
    onEnter() { this.f = this.context.ecx; },
    onLeave() {
      const f = this.f, tb = rdp(f.add(0x54));
      emit('cycle', { fac: ref(f), flags: rd(f.add(0x60)), modifier: rd(f.add(0x6c)), pair: [rd(f.add(0x64)), rd(f.add(0x68))],
                      length: tb === null || tb.isNull() ? null : rd(tb.add(0x20)) });
    },
  }],
  // Processing facility state set: thiscall facility, arg new state.
  [0x53a6f0, '535556578bf1e805', 'fac_state', {
    onEnter(args) {
      const f = this.context.ecx;
      emit('fac_state', { fac: ref(f), old: rd(f.add(0x58)), new: args[0].toInt32(), flags: rd(f.add(0x60)) });
    },
  }],
  // Notification 0x315 handler: cdecl (SdRef*, old, new, ctx).
  [0x516360, '83ec1c5355568b74', 'fac_notif', {
    onEnter(args) { emit('fac_notif', { fac: hex(rd(args[0])), old: args[1].toInt32(), new: args[2].toInt32() }); },
  }],
  // Refill serve (one wait-list entry): thiscall side, (more*, ctx).
  [0x52fbf0, '6aff689853640064', 'refill_raw', refill('refill_raw', 0x80, 0x78)],
  [0x52fd30, '6aff68b853640064', 'refill_refined', refill('refill_refined', 0x84, 0x7c)],
  // Stock setters: thiscall side, (new, ctx).
  [0x52ef90, '5355568bf1e866b0', 'stock_raw', stock('stock_raw', 0x78)],
  [0x52eff0, '5355568bf1e806b0', 'stock_refined', stock('stock_refined', 0x7c)],
  // Character +0xAC bit 7 setter (timer 0x389 Active): thiscall character, (value, ctx).
  [0x4eeaa0, '5355568bf1e856b5', 'force_active', {
    onEnter(args) {
      const c = this.context.ecx;
      emit('force_active', { char: ref(c), old: (rd(c.add(0xac)) >>> 7) & 1, new: args[0].toInt32(), ra: hex(this.returnAddress) });
    },
  }],
  // SmugglingPercent formula: cdecl (owner, support, flag, d, c, b, a) -> percent (argument meaning Inferred).
  [0x559c40, '8b44240c8b4c2418', 'smuggling', {
    onEnter(args) { this.a = [0, 1, 2, 3, 4, 5, 6].map((i) => args[i].toInt32()); },
    onLeave(ret) { emit('smuggling', { args: this.a, result: ret.toInt32() }); },
  }],
  // Smuggling draw on DONE: cdecl (facility, out*).
  [0x5166a0, '8b4c240456578b7c', 'smuggle_draw', {
    onEnter(args) { emit('smuggle_draw', { fac: ref(args[0]) }); },
  }],
  // Manufacturing manager point delivered: thiscall manager.
  [0x52a430, '568bf1578b7c240c', 'mgr_point', {
    onEnter() { this.m = this.context.ecx; this.before = mgrCounts(this.m); },
    onLeave() { emit('mgr_point', { mgr: ref(this.m), before: this.before, after: mgrCounts(this.m) }); },
  }],
  // Mission hooks. Force detection: cdecl (side, forceA, forceB, out res*, out p*).
  [0x55e4d0, '8b4424148b4c2408', 'force_detect', {
    onEnter(args) { this.a = [0, 1, 2].map((i) => args[i].toInt32()); this.o = [args[3], args[4]]; },
    onLeave() { emit('force_detect', { args: this.a, res: rd(this.o[0]), p: rd(this.o[1]) }); },
  }],
  // Loyalty ("not betray") check: cdecl (side, enhancedLoyalty, out res*, out p*); p = 100 - loyalty.
  [0x55e520, '8b4c24108b542408', 'loyalty', {
    onEnter(args) { this.a = [args[0].toInt32(), args[1].toInt32()]; this.o = [args[2], args[3]]; },
    onLeave() { emit('loyalty', { args: this.a, res: rd(this.o[0]), p: rd(this.o[1]) }); },
  }],
  // Decoy check: cdecl 7 args, last two = out (result*, p*) (argument meaning Inferred).
  [0x55e410, '8b4c240c568b7424', 'decoy', {
    onEnter(args) { this.a = [0, 1, 2, 3, 4].map((i) => args[i].toInt32()); this.o = [args[5], args[6]]; },
    onLeave() { emit('decoy', { args: this.a, res: rd(this.o[0]), p: rd(this.o[1]) }); },
  }],
  // Foil check: cdecl 8 args, last two = out (result*, p*) (argument meaning Inferred).
  [0x55e470, '8b4c2408568b7424', 'foil', {
    onEnter(args) { this.a = [0, 1, 2, 3, 4, 5].map((i) => args[i].toInt32()); this.o = [args[6], args[7]]; },
    onLeave() { emit('foil', { args: this.a, res: rd(this.o[0]), p: rd(this.o[1]) }); },
  }],
  // Mission success probability of one member: thiscall mission, (member, out p*).
  // sk = member +0x7C..+0x94 as i16 (character Enhanced skills, Force, ...; Inferred layout, characters only).
  [0x593320, '8b44240850c70000', 'mission_prob', {
    onEnter(args) { this.m = this.context.ecx; this.c = args[0]; this.o = args[1]; },
    onLeave() {
      const sk = []; for (let i = 0; i < 13; i++) { try { sk.push(this.c.add(0x7c + 2 * i).readS16()); } catch (e) { sk.push(null); } }
      emit('mission_prob', { mission: ref(this.m), member: ref(this.c), p: rd(this.o), sk });
    },
  }],
  // Mission TaskStatus (+0x60) / CompletionStatus (+0x64) setters: thiscall mission, (value, ctx).
  [0x521880, '535556578bf1e875', 'mission_task', {
    onEnter(args) { const m = this.context.ecx; emit('mission_task', { mission: ref(m), old: rd(m.add(0x60)), new: args[0].toInt32() }); },
  }],
  [0x521900, '535556578bf1e8f5', 'mission_done', {
    onEnter(args) { const m = this.context.ecx; emit('mission_done', { mission: ref(m), old: rd(m.add(0x64)), new: args[0].toInt32() }); },
  }],
  // Combat hooks. Any object: DestroyedReason setter, thiscall (reason, ctx).
  [0x4f71d0, '535556578bf1e825', 'destroyed', {
    onEnter(args) { const o = this.context.ecx; emit('destroyed', { obj: ref(o), old: rd(o.add(0x40)) & 0xff, new: args[0].toInt32() }); },
  }],
  // Planetary assault resolver: thiscall resolver, (system, header, result); counters at +0x14..+0x2C.
  [0x58c1e0, '6aff68a8fe640064', 'assault', {
    onEnter(args) { this.r = this.context.ecx; this.s = args[0]; },
    onLeave() { emit('assault', { sys: ref(this.s), side: rd(this.r.add(0x14)), def: rd(this.r.add(0x18)), n: [0x1c, 0x20, 0x24, 0x28, 0x2c].map((o) => rd(this.r.add(o))) }); },
  }],
  // Troop duel: cdecl (attStrength, attLeadership, defStrength, defLeadership, out attLoses*, out defLoses*).
  [0x55eb00, '8b442408538b5c24', 'troop_duel', {
    onEnter(args) { this.a = [0, 1, 2, 3].map((i) => args[i].toInt32()); this.o = [args[4], args[5]]; },
    onLeave() { emit('troop_duel', { args: this.a, att_lost: rd(this.o[0]), def_lost: rd(this.o[1]) }); },
  }],
  // Defence facility fire on landing troops: cdecl (attack, out hit*).
  [0x55ead0, '8b442404568b7424', 'assault_fire', {
    onEnter(args) { this.a = args[0].toInt32(); this.o = args[1]; },
    onLeave() { emit('assault_fire', { attack: this.a, hit: rd(this.o) }); },
  }],
  // Assault damage pick: cdecl (facilities, hq, energy, raw, out fac*, out idx*, out energy*, out raw*).
  [0x55ebc0, '8b44241c538b5c24', 'assault_damage', {
    onEnter(args) { this.a = [0, 1, 2, 3].map((i) => args[i].toInt32()); this.o = [4, 5, 6, 7].map((i) => args[i]); },
    onLeave() { emit('assault_damage', { args: this.a, out: this.o.map(rd) }); },
  }],
  // Bombardment resolver: thiscall resolver, (system, header, result); counters +0x14..+0x44.
  [0x58d800, '6aff684800650064', 'bombard', {
    onEnter(args) { this.r = this.context.ecx; this.s = args[0]; this.h = args[1]; },
    onLeave() {
      const n = []; for (let o = 0x14; o <= 0x44; o += 4) n.push(rd(this.r.add(o)));
      emit('bombard', { sys: ref(this.s), cmd: rd(this.h.add(0xc)), n });
    },
  }],
  // Bombardment strength / defence fire (value * (leadership div 40 + 1)): cdecl (value, leadership, out*).
  [0x55e950, '8b4424088b4c240c', 'bombard_strength', {
    onEnter(args) { this.a = [args[0].toInt32(), args[1].toInt32()]; this.o = args[2]; },
    onLeave() { emit('bombard_strength', { args: this.a, out: rd(this.o) }); },
  }],
  [0x55e930, '8b4424088b4c240c', 'bombard_fire', {
    onEnter(args) { this.a = [args[0].toInt32(), args[1].toInt32()]; this.o = args[2]; },
    onLeave() { emit('bombard_fire', { args: this.a, out: rd(this.o) }); },
  }],
  // Bombardment shot: cdecl (defence, out hit*).
  [0x55e970, 'a164b76b008b0d5c', 'bombard_hit', {
    onEnter(args) { this.a = args[0].toInt32(); this.o = args[1]; },
    onLeave() { emit('bombard_hit', { defence: this.a, hit: rd(this.o) }); },
  }],
  // Space battle: system Battle flag (+0x88 bit 4) setter, thiscall system, (value, ctx).
  [0x50a580, '5355568bf1e876fa', 'battle_flag', {
    onEnter(args) { const s = this.context.ecx; emit('battle_flag', { sys: ref(s), old: (rd(s.add(0x88)) >>> 4) & 1, new: args[0].toInt32() }); },
  }],
  // Controller battle decision (host): cdecl (out decided*, out auto*, ...); logged when decided.
  [0x41cfc0, 'e80bfdffff85c074', 'battle_choice', {
    onEnter(args) { this.o = [args[0], args[1]]; },
    onLeave() { const d = rd(this.o[0]); if (d) emit('battle_choice', { decided: d, auto: rd(this.o[1]) }); },
  }],
  // Battle result application (host, after tactical or automatic resolution): thiscall host, (out*).
  // Host +8 = battle system, +0xC = auto flag; *out = 1 when the BattleWon record was left unset
  // won = system +0x54 block +0x64 (BattleWon record), ck = system ControlKind +0x78.
  [0x40c780, '6aff683aa2620064', 'battle_apply', {
    onEnter(args) { this.h = this.context.ecx; this.out = args[0]; emit('battle_apply', { phase: 'start', auto: rd(this.h.add(0xc)) }); },
    onLeave() {
      const s = rdp(this.h.add(8)), ok = s !== null && !s.isNull(), b = ok ? rdp(s.add(0x54)) : null;
      emit('battle_apply', { phase: 'end', sys: ok ? ref(s) : null, withdraw: rd(this.out),
                             won: b !== null && !b.isNull() ? rd(b.add(0x64)) : null, ck: ok ? rd(s.add(0x78)) : null });
    },
  }],
  // Tactical result records read by battle_apply: cdecl (out*, key) -> out*; raw u32s, f = the float
  // field (ship [4], unit5 [3]: hull remaining).
  [0x5969d0, '83ec1c8d44240056', 'battle_sides', tacResult('battle_sides', 7)],
  [0x5968e0, '8b44240883ec2056', 'battle_fighter', tacResult('battle_fighter', 8)],
  [0x596870, '8b44240883ec2c56', 'battle_ship', tacResult('battle_ship', 11, 4)],
  [0x596980, '83ec145657e84602', 'battle_unit5', tacResult('battle_unit5', 5, 3)],
  // Automatic-battle engine per application (unverified on a live run): damage application VA 0x5b54d0
  // (thiscall unit, (float d)): unit key +0xEC, family +0x128, id +0x12C, state +0x170, shield +0x3C0 and hull +0x3CC before /
  // after. Its 0-2 engine draws are the rand events between this line's enter and leave (the leave line follows them).
  // Critical hit VA 0x5b1970 (thiscall unit, (k)): k and the five counters after (+0x370 shield, +0x37C weapon, +0x378 tractor,
  // +0x36C speed, +0x374 hyperdrive).
  [0x5b54d0, '83ec0c568bf1d986', 'bt_dmg', {
    onEnter(args) {
      const u = (this.u = this.context.ecx), f = (o) => u.add(o).readFloat();
      this.b = [f(0x3c0), f(0x3cc)];
      this.d = Memory.alloc(4).writeU32(args[0].toUInt32()).readFloat(); // float arg on the stack, raw bits
    },
    onLeave() {
      const u = this.u, f = (o) => u.add(o).readFloat();
      emit('bt_dmg', { key: rd(u.add(0xec)), fam: rd(u.add(0x128)), id: rd(u.add(0x12c)), st: rd(u.add(0x170)), d: this.d,
        before: this.b, after: [f(0x3c0), f(0x3cc)] });
    },
  }],
  [0x5b1970, '8b442404568bf157', 'bt_crit', {
    onEnter(args) { this.u = this.context.ecx; this.k = args[0].toInt32(); },
    onLeave() {
      const u = this.u;
      emit('bt_crit', { key: rd(u.add(0xec)), k: this.k, c: [0x370, 0x37c, 0x378, 0x36c, 0x374].map((o) => rd(u.add(o))) });
    },
  }],
  // Per-unit appliers: thiscall host obj, (unit, ctx).
  [0x40d260, '83ec2033c0535556', 'apply_fighter', applyUnit('apply_fighter')],
  [0x40cfc0, '8b510483ec345333', 'apply_ship', applyUnit('apply_ship')],
  [0x40d170, '83ec18538b5c2420', 'apply_unit5', applyUnit('apply_unit5')],
  [0x40d320, '6aff6870a2620064', 'apply_fighter_parent', applyUnit('apply_fighter_parent')],
  // Capital ship hull damage (+0x60) setter: thiscall ship, (value, ctx).
  [0x501490, '535556578bf1e865', 'hull_damage', {
    onEnter(args) { const s = this.context.ecx; emit('hull_damage', { ship: ref(s), old: rd(s.add(0x60)), new: args[0].toInt32() }); },
  }],
  // Fighter SquadSizeDamage (+0x60) setter: thiscall fighter, (value, ctx).
  [0x5032c0, '535556578bf1e835', 'squad_damage', {
    onEnter(args) { const s = this.context.ecx; emit('squad_damage', { fighter: ref(s), old: rd(s.add(0x60)), new: args[0].toInt32() }); },
  }],
  // System content hook +0xD0 (thiscall system, (unit, value, ctx)), from the 0x300 handler: a unit
  // [0x10, 0x20) of this location that left Existing as Destroyed adds its CombatValue (SD 48) to the
  // system's per-side record by DestroyedReason: 0xC battle L (block +0x68/+0x6C/+0x70), 0xE assault
  // (+0x48..), 0xF bombard (+0x54..). Logged after the update, losses only.
  [0x508c60, '64a1000000006aff', 'battle_loss', {
    onEnter(args) { this.s = this.context.ecx; this.u = args[0]; this.v = args[1].toInt32(); },
    onLeave() {
      const r = rd(this.u.add(0x40)) & 0xff;
      if (this.v !== 0 || ![0xc, 0xe, 0xf].includes(r) || !(rd(this.u.add(0x50)) & 8)) return;
      const b = rdp(this.s.add(0x54)), blk = (o) => (b === null || b.isNull() ? null : [1, 2, 3].map((i) => rd(b.add(o + 4 * i))));
      emit('battle_loss', { sys: ref(this.s), unit: ref(this.u), reason: r, L: blk(0x64), assault: blk(0x44), bombard: blk(0x50) });
    },
  }],
  // Battle end (game object, after battle_apply) and the post-battle support wave record:
  // cdecl (side1 strength, side2 strength, winner, L1, L2, out record*).
  [0x516ef0, '6aff689828640064', 'battle_end', {
    onEnter() { emit('battle_end', {}); },
  }],
  [0x55dbf0, '6aff68189e640064', 'battle_wave', {
    onEnter(args) { this.a = [0, 1, 2, 3, 4].map((i) => args[i].toInt32()); this.o = args[5]; },
    onLeave() { emit('battle_wave', { args: this.a, rec: [3, 4, 5, 6].map((i) => rd(this.o.add(4 * i))) }); },
  }],
  // Uprisings. System flag setters, thiscall system, (value, ctx):
  // Uprising (+0x88 bit 2) and Suppressing (bit 9). Logged with support, requirement +0x80, surplus +0x7C.
  [0x50a4a0, '5355568bf1e856fb', 'uprising_flag', sysFlag('uprising_flag', 2)],
  [0x50a7b0, '5355568bf1e846f8', 'suppressing_flag', sysFlag('suppressing_flag', 9)],
  // Uprising incident (bit-16 hook and incite success), thiscall system, (ctx); block totals +0x74/+0x78/+0x7C.
  [0x50d030, '83ec0cb801000000', 'upr_incident', {
    onEnter() {
      const s = this.context.ecx, b = rdp(s.add(0x54));
      emit('upr_incident', Object.assign(sysState(s), { totals: b === null || b.isNull() ? null : [0x74, 0x78, 0x7c].map((o) => rd(b.add(o)) | 0) }));
    },
  }],
  // Uprising roll: cdecl (owner, share, deathStar, troops, stormtroopers, t74, t78, out1*, out2*).
  [0x559ce0, '538b5c2424558b6c', 'upr_roll', {
    onEnter(args) { this.a = [0, 1, 2, 3, 4, 5, 6].map((i) => args[i].toInt32()); this.o1 = args[7]; this.o2 = args[8]; },
    onLeave() { emit('upr_roll', { args: this.a, upris1: rd(this.o1), upris2: rd(this.o2) }); },
  }],
  // Incident effect per table value: thiscall system, (value, owner, ctx).
  [0x50d150, '64a1000000006aff', 'upr_effect', {
    onEnter(args) { emit('upr_effect', { sys: ref(this.context.ecx), value: args[0].toInt32(), owner: args[1].toInt32() }); },
  }],
  // Informant roll: cdecl (out*) = INFORMTB value.
  [0x559db0, '568b742408c70600', 'informant_roll', {
    onEnter(args) { this.o = args[0]; },
    onLeave() { emit('informant_roll', { value: rd(this.o) }); },
  }],
  // Subdue end check: thiscall system, (ctx).
  [0x50c910, '6aff68b81d640064', 'subdue_end', {
    onEnter() { this.s = this.context.ecx; this.f = sysState(this.s); },
    onLeave() { this.f.uprising_after = (rd(this.s.add(0x88)) >>> 2) & 1; emit('subdue_end', this.f); },
  }],
  // Prisoners. Character state +0x98 setter: thiscall character, (value, ctx).
  // ra tells the path: capture +0x20C, rescue +0x210, escape/uprising +0x214, release +0x218.
  [0x4ee3e0, '535556578bf1e815', 'char_state', {
    onEnter(args) { emit('char_state', Object.assign(charState(this.context.ecx), { new: args[0].toInt32(), ra: hex(this.returnAddress) })); },
  }],
  // Capture (character vtable +0x20C): thiscall character, (captor SdRef*, ctx).
  [0x4ef480, '8b442404558b6c24', 'capture', {
    onEnter(args) { this.c = this.context.ecx; this.f = Object.assign(charState(this.c), { captor_arg: hex(rd(args[0])), ra: hex(this.returnAddress) }); },
    onLeave() { const a = charState(this.c); this.f.after = { state: a.state, flags: a.flags, owner: a.owner, captor: a.captor }; emit('capture', this.f); },
  }],
  // Escape check on EscapeAttempt change (manager 0xFA): (character, new value, ctx).
  [0x561990, '64a1000000006aff68d0a26400508b44', 'escape_check', {
    onEnter(args) { this.c = args[0]; this.f = Object.assign(charState(this.c), { value: args[1].toInt32() }); },
    onLeave() { this.f.flags_after = rd(this.c.add(0xac)); emit('escape_check', this.f); },
  }],
  // Escape roll: cdecl (side, EnhancedCombat, EnhancedEspionage, guard avg combat, troops, out res*);
  // key = a2 + a3 - a4 - a5 into ESCAPETB.
  [0x55e7e0, '8b4424188b4c2408', 'escape_roll', {
    onEnter(args) { this.a = [0, 1, 2, 3, 4].map((i) => args[i].toInt32()); this.o = args[5]; },
    onLeave() { emit('escape_roll', { args: this.a, res: rd(this.o) }); },
  }],
  // Manager 0xF8 hook of notification 0x30A (state changed): thiscall manager, (character SdRef*, old, new, ctx).
  [0x546370, '6aff68a077640064', 'prisoner_hook', {
    onEnter(args) { emit('prisoner_hook', { char: hex(rd(args[0])), old: args[1].toInt32(), new: args[2].toInt32() }); },
  }],
];

// Character: state +0x98 (i16), flags +0xAC, owner bits, captor key (block +0x54 -> +0x30), parent pointer +0x1C (Inferred from VA 0x540450).
function charState(c) {
  const b = rdp(c.add(0x54)), p = rdp(c.add(0x1c));
  let st = null; try { st = c.add(0x98).readS16(); } catch (e) {}
  return { char: ref(c), state: st, flags: rd(c.add(0xac)), owner: (rd(c.add(0x24)) >>> 6) & 3,
           captor: b === null || b.isNull() ? null : hex(rd(b.add(0x30))), parent: p === null || p.isNull() ? null : ref(p) };
}

function sysState(s) {
  const fl = rd(s.add(0x88));
  return { sys: ref(s), owner: (rd(s.add(0x24)) >>> 6) & 3, support: rd(s.add(0x58)), surplus: rd(s.add(0x7c)) | 0,
           required: rd(s.add(0x80)), uprising: (fl >>> 2) & 1, suppressing: (fl >>> 9) & 1, deathstar: (fl >>> 11) & 1 };
}
function sysFlag(name, bit) {
  return {
    onEnter(args) { emit(name, Object.assign(sysState(this.context.ecx), { old: (rd(this.context.ecx.add(0x88)) >>> bit) & 1, new: args[0].toInt32() })); },
  };
}

function tacResult(name, n, fi) {
  return {
    onEnter(args) { this.k = args[1]; },
    onLeave(r) {
      const v = []; for (let i = 0; i < n; i++) v.push(rd(r.add(4 * i)));
      const f = { key: hex(this.k), v };
      if (fi !== undefined) { try { f.f = r.add(4 * fi).readFloat(); } catch (e) { f.f = null; } }
      emit(name, f);
    },
  };
}
function applyUnit(name) {
  return {
    onEnter(args) { emit(name, { unit: ref(args[0]) }); },
  };
}

function refill(name, countOff, stockOff) {
  return {
    onEnter(args) {
      this.s = this.context.ecx; this.out = args[0];
      this.f = { side: ref(this.s), count: rd(this.s.add(countOff)), stock: rd(this.s.add(stockOff)) };
    },
    onLeave() { this.f.more = rd(this.out); this.f.count_after = rd(this.s.add(countOff)); emit(name, this.f); },
  };
}
function stock(name, off) {
  return {
    onEnter(args) { const s = this.context.ecx; emit(name, { side: ref(s), old: rd(s.add(off)), new: args[0].toInt32() }); },
  };
}
function sidePairs(s) {
  return { maint: [rd(s.add(0x58)), rd(s.add(0x5c))], mines: [rd(s.add(0x60)), rd(s.add(0x64))],
           refineries: [rd(s.add(0x68)), rd(s.add(0x6c))], demand: [rd(s.add(0x70)), rd(s.add(0x74))] };
}
function mgrCounts(m) {
  return { completed: rd(m.add(0x5c)), overflow: rd(m.add(0x60)), required: rd(m.add(0x68)), reserved: rd(m.add(0x6c)) };
}
// ---- Orders. Layouts beyond the header are Inferred where marked.
// Order event classes: vtable -> [size, list offsets...]; payload = u32s from +0x50 to size.
const ORDER_EVENTS = {
  '0x6695a8': [0x50], '0x669558': [0x50], '0x669580': [0x5c], '0x6695d0': [0x50],   // 0x200 scrap, 0x201 move, 0x202 name, 0x205 ack
  '0x669ad0': [0x58, 0x50], '0x669a30': [0x58, 0x50],                             // 0x210 production, 0x214 target (node list at +0x50)
  '0x669a80': [0x50], '0x669a58': [0x5c], '0x669aa8': [0x54],                     // 0x213 cancel, 0x215 name, 0x216 reserved
  '0x669b20': [0x50], '0x669af8': [0x50],                                         // fleet bombard / assault
  '0x661e28': [0x74, 0x64, 0x6c],                                                 // mission create (lists +0x64, +0x6c)
  '0x661af0': [0x50], '0x661f20': [0x50], '0x6698a8': [0x50], '0x669a08': [0x50], // character / mission / FA orders
};
// Command object sizes by type (factory allocation); class fields = u32s +0x44 .. size.
const CMD_SIZE = { 0x200: 0x44, 0x201: 0x4c, 0x202: 0x4c, 0x203: 0x50, 0x204: 0x44, 0x205: 0x44, 0x210: 0x4c, 0x211: 0x4c,
  0x212: 0x4c, 0x213: 0x44, 0x214: 0x44, 0x215: 0x50, 0x216: 0x44, 0x220: 0x48, 0x221: 0x48, 0x222: 0x48, 0x223: 0x48,
  0x234: 0x48, 0x240: 0x60, 0x241: 0x60, 0x242: 0x44, 0x250: 0x44, 0x260: 0x44, 0x261: 0x44, 0x262: 0x44, 0x263: 0x44,
  0x268: 0x44, 0x270: 0x44 };
function u32s(p, from, to) { const a = []; for (let o = from; o < to; o += 4) a.push(rd(p.add(o))); return a; }
function clockField(off) { const c = rdp(G_CLOCK); return c === null || c.isNull() ? null : rd(c.add(off)); }
// SdRef list (ordered tree): root at +4, first = leftmost via +4 links, next = node +0x10; node key +0x18, value +0x1c.
function refList(list) {
  const out = [];
  let n = rdp(list.add(4));
  if (n === null) return null;
  if (!n.isNull()) for (let l = rdp(n.add(4)), j = 0; l !== null && !l.isNull() && j < 256; l = rdp(l.add(4)), j++) n = l;
  for (let i = 0; n !== null && !n.isNull() && i < 256; i++, n = rdp(n.add(0x10))) out.push([hex(rd(n.add(0x18))), hex(rd(n.add(0x1c)))]);
  return out;
}
// Command type = vtable +0x0c (thiscall getter, constant).
function cmdType(c) {
  try { return vcall(c, 0x0c); } catch (e) { return null; }
}
function cmdInfo(c) {
  const t = cmdType(c), f = { type: t === null ? null : hex(t) };
  if (t === 0x280) { f.raw = u32s(c, 0x1c, 0x30); return f; }   // speed message, not a command-base class
  if (!(t in CMD_SIZE)) return f;                                 // type 1 bookkeeping commands: no decode
  f.side = rd(c.add(0x20));
  f.targets = refList(c.add(0x2c));
  f.arg_ref = hex(rd(c.add(0x34)));
  f.arg_int = rd(c.add(0x3c));
  f.fields = u32s(c, 0x44, CMD_SIZE[t]);
  return f;
}
let execDepth = 0;
function cmdExec() {
  return {
    // Not this.depth: InvocationContext.depth is a read-only Frida property (assigning it threw, so no cmd_exec was logged).
    onEnter(args) { this.c = this.context.ecx; this.res = args[0]; this.lvl = execDepth++; this.t = cmdType(this.c); this.info = this.lvl > 0 ? cmdInfo(this.c) : null; },
    onLeave() {
      execDepth--;
      const r = [rd(this.res), rd(this.res.add(4))];
      const f = { cmd: hex(this.c), depth: this.lvl, ok: r[1] === 0xffffffff, res: r, serial: clockField(0x18) };
      if (this.info) Object.assign(f, this.info); else f.type = this.t === null ? null : hex(this.t);
      emit('cmd_exec', f);
    },
  };
}
// Call a no-argument thiscall getter from the object's vtable. Only when both the vtable and the
// getter lie inside the exe image, so a stale or garbage object never jumps into arbitrary memory.
const getters = new Map();
const inExe = (p) => p.compare(mod.base) >= 0 && p.compare(mod.base.add(mod.size)) < 0;
function vcall(o, slot) {
  const vt = o.readPointer();
  if (!inExe(vt)) throw new Error('vtable outside exe');
  const g = vt.add(slot).readPointer(), k = g.toString();
  if (!inExe(g)) throw new Error('getter outside exe');
  if (!getters.has(k)) getters.set(k, new NativeFunction(g, 'uint', ['pointer'], 'thiscall'));
  return getters.get(k)(o);
}
// Object SdRef = class family (vtable +4, thiscall getter, no args) << 24 | id (+0x18 low 24 bits), as VA 0x4025b0.
function ref(o) {
  try { return hex(((vcall(o, 4) << 24) | (o.add(0x18).readU32() & 0xffffff)) >>> 0); } catch (e) { return null; }
}
// Event type id: event vtable +0x24 (thiscall getter, e.g. VA 0x57ce80 returns 0x389).
function evType(e) {
  try { return hex(vcall(e, 0x24)); } catch (err) { return null; }
}
function queueName(q) {
  const c = rdp(G_CLOCK);
  if (c === null || c.isNull()) return hex(q);
  for (const [off, n] of [[0xa8, 'timed'], [0xac, 'fifo'], [0xb0, 'priority']]) if (q.equals(rdp(c.add(off)))) return n;
  return hex(q);
}


// ---- Optional AI hooks; installed only with trace.py --ai-hooks.
// Prologues checked below; calling conventions per entry (Inferred where marked).
const u32s_at = (o, from, to) => { const r = {}; for (let k = from; k <= to; k += 4) r['0x' + k.toString(16)] = rd(o.add(k)) | 0; return r; };
function treeKeys(t) {
  const out = []; let n = rdp(t.add(4));
  while (n !== null && !n.isNull()) { const l = rdp(n.add(4)); if (l === null || l.isNull()) break; n = l; }
  for (let k = 0; n !== null && !n.isNull() && k < 64; n = rdp(n.add(0x10)), k++) out.push(hex(rd(n.add(0x18))));
  return out;
}
// Model record maps (VA 0x4f5940): binary tree whose nodes are the records (root +4, left +4, right +8, key +0x18); model +0x2C systems,
// +0x58 ships, +0x78 fleets.
function mapFind(map, key) {
  let n = map === null ? null : rdp(map.add(4)), g = 0;
  while (n !== null && !n.isNull() && g++ < 64) { const nk = rd(n.add(0x18)) >>> 0; if (nk === key) return n; n = rdp(n.add(key > nk ? 8 : 4)); }
  return null;
}
// Fleet record words of the regroup walk: [key, +0x30 (op key), +0x38 (flags), +0x28 (location), the location's
// system-record C word +0x30 (null without a record)]; mdl = the model.
function fleetRec(mdl, k) {
  const key = parseInt(k, 16) >>> 0, ok = mdl !== null && !mdl.isNull();
  const r = ok ? mapFind(mdl.add(0x78), key) : null;
  if (r === null) return [k, null];
  const loc = rd(r.add(0x28)) >>> 0, s = mapFind(mdl.add(0x2c), loc);
  return [k, rd(r.add(0x30)), hex(rd(r.add(0x38))), hex(loc), s === null ? null : hex(rd(s.add(0x30)))];
}
function aiOpState(o) {
  return { tick: clockField(0x0c), op: rd(o.add(0x18)), counters: u32s_at(o, 0x6c, 0x90), flagship: hex(rd(o.add(0x38))), f64: hex(rd(o.add(0x64))), ships: treeKeys(o.add(0x24)) };
}
if (globalThis.AI_HOOKS) HOOKS.push(
  // System record update (VA 0x433e40): ECX = record; returns at once when +0x24 (dirty) is 0.
  // +0x48 = the side view's support share (100 for an own system); view +0x58 = +0x48 for side 1, 100 - +0x48 for side 2.
  [0x433e40, '64a1000000006aff', 'ai_sysrec', {
    onEnter() { this.r = this.context.ecx; this.dirty = rd(this.r.add(0x24)); },
    onLeave() {
      if (!this.dirty) return;
      const r = this.r, key = rd(r.add(0x18)), hi = key >>> 24;
      if (hi < 0x90 || hi > 0x97) return;
      emit('ai_sysrec', { tick: clockField(0x0c), side: rd(r.add(0x1c)), key: hex(key), f28: hex(rd(r.add(0x28))), f2c: hex(rd(r.add(0x2c))),
        f30: hex(rd(r.add(0x30))), share48: rd(r.add(0x48)) | 0, troops88: rd(r.add(0x88)) | 0, need98: rd(r.add(0x98)) | 0,
        batteries74: rd(r.add(0x74)) | 0, f118: rd(r.add(0x118)) | 0 });
    },
  }],
  // Record update, right after the view lookup (VA 0x433e96, instruction hook: EAX = the side's view of the system from VA 0x4f3220,
  // ESI = the record, key +0x18, side +0x1C): view +0x58 support (Alliance share), owner bits 6-7 of +0x24. Precedes its ai_sysrec.
  [0x433e96, '8bd883c4088d4c24', 'ai_sysview', {
    onEnter() {
      const v = this.context.eax, r = this.context.esi, key = rd(r.add(0x18)), hi = key >>> 24;
      if (hi < 0x90 || hi > 0x97) return;
      emit('ai_sysview', { tick: clockField(0x0c), side: rd(r.add(0x1c)), key: hex(key),
        view58: v.isNull() ? null : rd(v.add(0x58)) | 0, viewOwner: v.isNull() ? null : (rd(v.add(0x24)) >>> 6) & 3 });
    },
  }],
  // Real support setter (VA 0x509c30, thiscall system, (value, ctx)); logged on every call that changes it.
  [0x509c30, '535556578bf1e8c5', 'support_set', {
    onEnter(args) { this.s = this.context.ecx; this.old = rd(this.s.add(0x58)); this.v = args[0].toInt32(); this.ra = this.returnAddress; },
    onLeave() { if (this.old !== this.v) emit('support_set', { tick: clockField(0x0c), sys: ref(this.s), old: this.old, new: this.v, now: rd(this.s.add(0x58)), ra: hex(this.ra) }); },
  }],
  // System ranked list (VA 0x41a340, thiscall model, (sector*, req28, req2C, req30, excl28, excl2C, excl30, idx, dir) -> list), only when
  // called from the type-4 site pick VA 0x4b8f80. List: first entry at +8, next +8, entry key +0x0C, value +0x10, rank +0x14.
  [0x41a340, '518b442408568bf1', 'ai_sitelist', {
    onEnter(args) { const r = this.returnAddress; this.on = r.compare(ptr(0x4b8f80)) >= 0 && r.compare(ptr(0x4b9180)) < 0; if (this.on) { this.idx = args[7].toInt32(); this.dir = args[8].toInt32(); } },
    onLeave(ret) {
      if (!this.on) return;
      const e = [];
      for (let n = ret.isNull() ? null : rdp(ret.add(8)), k = 0; n !== null && !n.isNull() && k < 40; n = rdp(n.add(8)), k++)
        e.push([hex(rd(n.add(0x0c))), rd(n.add(0x10)) | 0, rd(n.add(0x14)) | 0]);
      emit('ai_sitelist', { tick: clockField(0x0c), idx: this.idx, dir: this.dir, entries: e });
    },
  }],
  // Planner pick (VA 0x4be980, ECX = planner set -> planner or 0): the picked planner's vtable, status +0x1C, priority +0x34.
  // The last pick of a tick shows where the issue cycle ended (with the command events of the next tick).
  [0x4be980, '5356578bf1e85604', 'ai_pick', {
    onLeave(ret) {
      emit('ai_pick', { tick: clockField(0x0c), planner: ret.isNull() ? null : hex(rd(ret)), status: ret.isNull() ? null : rd(ret.add(0x1c)), prio: ret.isNull() ? null : rd(ret.add(0x34)) });
    },
  }],
  // Fleet-op surplus builder (VA 0x478aa0, ECX = fleet op -> 0x270 or 0) and damaged-ship split (VA 0x477a40, same shape):
  // op key, counters +0x6C ... +0x90, flagship +0x38, member ship keys (key tree at +0x24: root +4, leftmost via +4, next +0x10, key +0x18),
  // the returned command.
  // Both can run ~1000 times per tick (run 3 galaxy); that event volume crashed the game. A call is logged (with its
  // ai_surplus_ship / ai_surplus_held lines) only when it returns a command or its op state / ship lines differ from the
  // op's last logged call.
  [0x478aa0, '6aff68f232630064', 'ai_surplus', {
    onEnter() {
      this.o = this.context.ecx; this.f = aiOpState(this.o); surplusBuf = [];
      // per-ship fields of the test, read on entry without calling game code: the model's record map (op +0x58 -> +0x58) is a
      // binary tree whose nodes are the records (root +4, left +4, right +8, key +0x18; VA 0x4f5940)
      const mdl = rdp(this.o.add(0x58)), map = mdl === null || mdl.isNull() ? null : mdl.add(0x58);
      for (const k of this.f.ships) {
        const key = parseInt(k, 16) >>> 0;
        let n = map === null ? null : rdp(map.add(4)), guard = 0;
        while (n !== null && !n.isNull() && guard++ < 64) { const nk = rd(n.add(0x18)); if (nk === key) break; n = rdp(n.add(key > nk ? 8 : 4)); }
        surplusLine('ai_surplus_ship', n === null || n.isNull() ? { tick: this.f.tick, key: k, rec: null } : { tick: this.f.tick, key: k,
          f24: hex(rd(n.add(0x24))), f2c: hex(rd(n.add(0x2c))), f7c: rd(n.add(0x7c)) | 0, f80: rd(n.add(0x80)) | 0, f6c: rd(n.add(0x6c)) | 0,
          f60: rd(n.add(0x60)) | 0, f48: rd(n.add(0x48)) | 0 });
      }
    },
    onLeave(ret) { this.f.cmd = ret.isNull() ? null : cmdInfo(ret); const b = surplusBuf; surplusBuf = null; emitChanged('ai_surplus', this.f, b); },
  }],
  [0x477a40, '6aff682a31630064', 'ai_split', {
    onEnter() { this.o = this.context.ecx; this.f = aiOpState(this.o); },
    onLeave(ret) { this.f.cmd = ret.isNull() ? null : cmdInfo(ret); emitChanged('ai_split', this.f, []); },
  }],
  // Type-4 task targets (VA 0x4b8b70): ECX = task; task +0x10 = its fleet op (key +0x18).
  [0x4b8b70, '5556578bf1e80604', 'ai_t4targets', {
    onEnter() { this.t = this.context.ecx; },
    onLeave() {
      const t = this.t, op = rdp(t.add(0x10));
      emit('ai_t4targets', { tick: clockField(0x0c), op: op === null || op.isNull() ? null : rd(op.add(0x18)), site: hex(rd(t.add(0x5c))),
        targets: u32s_at(t, 0x34, 0x58) });
    },
  }],
  // Type-4 site pick (VA 0x4b8f80): ECX = task; the site is task +0x5C.
  [0x4b8f80, '6aff68d097630064', 'ai_t4site', {
    onEnter() { this.t = this.context.ecx; this.before = rd(this.t.add(0x5c)); },
    onLeave() { emit('ai_t4site', { tick: clockField(0x0c), before: hex(this.before), after: hex(rd(this.t.add(0x5c))) }); },
  }],
  // Fleet-op status (VA 0x479a00, produce state 6): ECX = fleet op; logged on entry, with the member fleets (tree at op +0x2C) as
  // fleetRec lines (model = op +0x58).
  [0x479a00, '8b4164a9ff040000', 'ai_opstatus', {
    onEnter() {
      const o = this.context.ecx, mdl = rdp(o.add(0x58));
      emit('ai_opstatus', { tick: clockField(0x0c), op: rd(o.add(0x18)), f60: hex(rd(o.add(0x60))), f64: hex(rd(o.add(0x64))), counters: u32s_at(o, 0x6c, 0x90),
        fleets: treeKeys(o.add(0x2c)).map((k) => fleetRec(mdl, k)) });
    },
  }],
  // Theater check (VA 0x4e5560): ECX = strategy object; +0x14 add / +0x18 drop; theaters = threaded tree at +0x2C
  // (root at +4, leftmost via +4, next via node +0x10, as VA 0x5f5060); model at +0x0C (bit 2 of model +0x04 = rebuild).
  [0x4e5560, '6aff6848e3630064', 'ai_theatercheck', {
    onEnter() { this.s = this.context.ecx; },
    onLeave() {
      const s = this.s, list = rdp(s.add(0x2c));
      let n = 0, node = list === null || list.isNull() ? null : rdp(list.add(4));
      while (node !== null && !node.isNull()) { const l = rdp(node.add(4)); if (l === null || l.isNull()) break; node = l; }
      while (node !== null && !node.isNull() && n < 100) { n++; node = rdp(node.add(0x10)); }
      const m = rdp(s.add(0x0c));
      emit('ai_theatercheck', { tick: clockField(0x0c), add: rd(s.add(0x14)), drop: rd(s.add(0x18)), theaters: n,
        model04: m === null || m.isNull() ? null : hex(rd(m.add(4))) });
    },
  }],
  // Planner step, planners 1-13 (step VAs; planners 5 / 6 hooked first, then all): ECX = planner; phase +0x38, state +0x3C,
  // done +0x20; one line per step call (frame) with the state before and after and the returned command type. Planner 14 is left out
  // (one frame per ship record).
  ...[[0x4dabe0, 1, '568bf15733ff8d4e'], [0x4e2120, 2, '53568bf15733ff8d'], [0x4d6d30, 3, '568bf15733ff8d4e'], [0x4d2460, 4, '568bf15733ff8d4e'],
      [0x4cfed0, 5, '568bf15733ff8d4e'], [0x4dd7a0, 6, '568bf15733ff8d4e'], [0x4d3100, 7, '568bf15733ff8d4e'], [0x4cf600, 8, '568bf15733ff8d4e'],
      [0x4cf290, 9, '568bf15733ff8d4e'], [0x4cc9c0, 10, '568bf15733ff8d4e'], [0x4c8b30, 11, '568bf15733ff8d4e'], [0x4c84a0, 12, '568bf15733ff8d4e'],
      [0x4bf320, 13, '568bf15733ff8d4e']].map(([va, n, sig]) => [va, sig, 'ai_step', {
    onEnter() { this.p = this.context.ecx; this.b = [rd(this.p.add(0x38)), rd(this.p.add(0x3c))]; },
    onLeave(ret) {
      const p = this.p;
      emit('ai_step', { tick: clockField(0x0c), planner: n, before: this.b, after: [rd(p.add(0x38)), rd(p.add(0x3c))], done: rd(p.add(0x20)),
        cmd: ret.isNull() ? null : cmdInfo(ret).type });
    },
  }]),
  // Fleet task: ECX = task (+0x08 machine state, +0x0C model, +0x10 fleet op, +0x28 task fleet, +0x2C cursor key).
  // State 5 (VA 0x4b04c0) on exit: task fleet, cursor, the op's member-fleet keys (tree at op +0x2C) with each fleet record's +0x38
  // (model +0x78 = fleet-record map, the binary tree of VA 0x4f5940 / 0x419e40: root +4, left +4, right +8, key +0x18).
  [0x4b04c0, '6aff68988d630064', 'ai_taskfleet', {
    onEnter() { this.t = this.context.ecx; },
    onLeave() {
      const t = this.t, op = rdp(t.add(0x10)), mdl = rdp(t.add(0x0c));
      const keys = op === null || op.isNull() ? [] : treeKeys(op.add(0x2c));
      const map = mdl === null || mdl.isNull() ? null : mdl.add(0x78);
      const members = keys.map((k) => {
        const key = parseInt(k, 16) >>> 0;
        let n = map === null ? null : rdp(map.add(4)), g = 0;
        while (n !== null && !n.isNull() && g++ < 64) { const nk = rd(n.add(0x18)); if (nk === key) break; n = rdp(n.add(key > nk ? 8 : 4)); }
        return [k, n === null || n.isNull() ? null : hex(rd(n.add(0x38)))];
      });
      emit('ai_taskfleet', { tick: clockField(0x0c), op: op === null || op.isNull() ? null : rd(op.add(0x18)), fleet: hex(rd(t.add(0x28))),
        cursor: hex(rd(t.add(0x2c))), members });
    },
  }],
  // Regroup (VA 0x4b0c90, task state 0xC; ECX = task, +0x0C model, +0x10 fleet op, +0x28 task fleet). Entry: the op's member
  // fleets (tree at op +0x2C; more than one = the walk runs) as fleetRec lines. The walk VA 0x41ae20 (thiscall model) called from it
  // (return address 0x4b0d4c) logs its returned list on exit (first entry +8, next +8, key +0x0C, value +0x10 = squared distance), each
  // entry with its fleetRec words. Exit: one line with members, walk (null = not run) and the returned 0x201 (ArgRef = the found fleet,
  // else the slot +0x54 system) or null.
  [0x4b0c90, '6aff68ab8e630064', 'ai_regroup', {
    onEnter() {
      const t = this.context.ecx, op = rdp(t.add(0x10)), mdl = rdp(t.add(0x0c));
      this.t = t; this.op = op === null || op.isNull() ? null : rd(op.add(0x18));
      this.members = op === null || op.isNull() ? [] : treeKeys(op.add(0x2c)).map((k) => fleetRec(mdl, k));
      this.prevWalk = globalThis.regroupWalk; globalThis.regroupWalk = null;
    },
    onLeave(ret) {
      emit('ai_regroup', { tick: clockField(0x0c), op: this.op, fleet: hex(rd(this.t.add(0x28))), members: this.members,
        walk: globalThis.regroupWalk, cmd: ret.isNull() ? null : cmdInfo(ret) });
      globalThis.regroupWalk = this.prevWalk;
    },
  }],
  [0x41ae20, '6aff6845b5620064', 'ai_regroup_walk', {
    onEnter() { this.on = this.returnAddress.toUInt32() === 0x4b0d4c; this.m = this.context.ecx; },
    onLeave(ret) {
      if (!this.on) return;
      const e = [];
      for (let n = ret.isNull() ? null : rdp(ret.add(8)), k = 0; n !== null && !n.isNull() && k < 40; n = rdp(n.add(8)), k++)
        e.push([rd(n.add(0x10)) | 0, ...fleetRec(this.m, hex(rd(n.add(0x0c))))]);
      globalThis.regroupWalk = e;
    },
  }],
  // Character-op produce: VA 0x4bbb00 (types 1 / 6) and VA 0x4bc480 (types 3-5), thiscall op, out finished*. Op: key +0x18,
  // state +0x1C, flags +0x20, target / flagship +0x28, character +0x2C, agent-record map pointer +0x40 (tree of VA 0x4f5940: root +4,
  // left +4, right +8, key +0x18). On exit: vtable (type), state before / after, flags before / after, finished, the character's record
  // +0x30 / +0x38 / +0x28 (its op), the returned command type.
  ...[[0x4bbb00, 'ai_charop'], [0x4bc480, 'ai_charop']].map(([va, name]) => [va, '5356578b7c24108b', name, {
    onEnter(args) { this.o = this.context.ecx; this.out = args[0]; this.b = [rd(this.o.add(0x1c)), hex(rd(this.o.add(0x20)))]; },
    onLeave(ret) {
      const o = this.o, key = rd(o.add(0x2c)) >>> 0, map = rdp(o.add(0x40));
      let n = map === null || map.isNull() ? null : rdp(map.add(4)), g = 0;
      while (n !== null && !n.isNull() && g++ < 64) { const nk = rd(n.add(0x18)) >>> 0; if (nk === key) break; n = rdp(n.add(key > nk ? 8 : 4)); }
      const rec = n === null || n.isNull() ? null : [hex(rd(n.add(0x30))), hex(rd(n.add(0x38))), rd(n.add(0x28))];
      emit('ai_charop', { tick: clockField(0x0c), op: rd(o.add(0x18)), vt: hex(rd(o)), before: this.b, after: [rd(o.add(0x1c)), hex(rd(o.add(0x20)))],
        fin: rd(this.out), target: hex(rd(o.add(0x28))), char: hex(key), rec, cmd: ret.isNull() ? null : cmdInfo(ret).type });
    },
  }]),
  // Type-1 character-op target search (VA 0x47a870, thiscall op, (SdRef* out) -> out; ret 4): op key +0x18,
  // op target +0x28 on entry; the returned system key (out word, none = not a system). Its four lists follow as ai_t1list lines.
  [0x47a870, '6aff681f35630064', 'ai_t1target', {
    onEnter(args) { this.o = this.context.ecx; this.out = args[0]; this.before = hex(rd(this.o.add(0x28))); },
    onLeave() { emit('ai_t1target', { tick: clockField(0x0c), op: rd(this.o.add(0x18)), before: this.before, picked: hex(rd(this.out)) }); },
  }],
  // The search's lists, only when called from VA 0x47a870: sector list VA 0x419a00 (thiscall model, (req24, req28, req2C, excl24,
  // excl28, excl2C, idx, dir)) and system list VA 0x41a340 (thiscall model, (SdRef* sector, req28, req2C, req30, excl28, excl2C,
  // excl30, idx, dir)). Stage 1 = with req24 0x40000000 / req28 0x80000000, else the fallback. List layout as ai_sitelist (same list
  // class, VA 0x41be80 / 0x41c070): entries [key, value (sector +0x108 / system +0xCC), rank] in list order.
  ...[[0x419a00, '6aff683db2620064', 'sector'], [0x41a340, '518b442408568bf1', 'system']].map(([va, bytes, list]) => [va, bytes, 'ai_t1list', {
    onEnter(args) {
      const r = this.returnAddress;
      this.on = r.compare(ptr(0x47a870)) >= 0 && r.compare(ptr(0x47aa90)) < 0;
      if (!this.on) return;
      this.stage = (list === 'sector' ? args[0] : args[1]).toUInt32() !== 0 ? 1 : 2;
      this.sector = list === 'system' ? hex(rd(args[0])) : null;
    },
    onLeave(ret) {
      if (!this.on) return;
      const e = [];
      for (let n = ret.isNull() ? null : rdp(ret.add(8)), k = 0; n !== null && !n.isNull() && k < 40; n = rdp(n.add(8)), k++)
        e.push([hex(rd(n.add(0x0c))), rd(n.add(0x10)) | 0, rd(n.add(0x14)) | 0]);
      emit('ai_t1list', { tick: clockField(0x0c), list, stage: this.stage, sector: this.sector, entries: e });
    },
  }]),
  // Hub step D: VA 0x4dd120 and its clones, fastcall ECX = planner; the planner's hub set (tree, key +0x18) at the
  // offset per clone. Entry: the set before; exit: the set after. A system leaving a set loses record C bit 28 (direct write), which
  // fails a type-1 character op's target keep (VA 0x47a480) if no other set re-marks it before planner 12 runs.
  ...[[0x4dd120, '6aff6833d5630064', 1, 0x60], [0x4e4a20, '6aff6853e2630064', 2, 0x68], [0x4da610, '6aff6823d0630064', 3, 0x74],
    [0x4d1ed0, '6aff6893c0630064', 5, 0x80], [0x4e1b00, '6aff68a3dc630064', 6, 0x84], [0x4d67e0, '6aff6813c8630064', 7, 0x74],
    [0x4ced60, '6aff6893ba630064', 10, 0x7c], [0x4cc360, '6aff68b3b5630064', 11, 0x80]].map(([va, bytes, planner, off]) => [va, bytes, 'ai_hub', {
    onEnter() { this.p = this.context.ecx; this.before = treeKeys(this.p.add(off)); },
    onLeave() {
      const after = treeKeys(this.p.add(off));
      if (after.join() !== this.before.join()) emit('ai_hub', { tick: clockField(0x0c), planner, before: this.before, after });
    },
  }]),
  // Fleet-task machine (VA 0x4adb70, produce state 5; thiscall task, out done*): task vtable (= type, classes 0x65c2c0,
  // 0x65c268, 0x65c160, 0x65c210, 0x65c108, 0x65c1b8 for types 1-6), state +0x08 before / after, done, command type.
  [0x4adb70, '538b5c240856578b', 'ai_task', {
    onEnter(args) { this.t = this.context.ecx; this.out = args[0]; this.b = rd(this.t.add(8)); },
    onLeave(ret) { emit('ai_task', { tick: clockField(0x0c), vt: hex(rd(this.t)), before: this.b, after: rd(this.t.add(8)), done: rd(this.out),
      fleet: hex(rd(this.t.add(0x28))), cmd: ret.isNull() ? null : cmdInfo(ret).type }); },
  }],
  // Mode choice (VA 0x4bf490): ECX = planner 13; mode +0x5C; model +0x2C (request bits +0x08, flags +0x04).
  [0x4bf490, '83ec085355568bf1', 'ai_mode', {
    onEnter() { this.p = this.context.ecx; this.before = rd(this.p.add(0x5c)); },
    onLeave() {
      const p = this.p, m = rdp(p.add(0x2c)), ok = m !== null && !m.isNull();
      emit('ai_mode', { tick: clockField(0x0c), before: this.before, after: rd(p.add(0x5c)),
        model08: ok ? hex(rd(m.add(8))) : null, model04: ok ? hex(rd(m.add(4))) : null });
    },
  }],
  // Theaters sub-step (VA 0x4e5530): ECX = strategy object; runs the ranking (vtable +0x14) only when model +0x04 bit 2
  // is set on entry, then clears it. Logs the entry bit, the theater count after (tree at +0x2C) and strategy +0x10.
  [0x4e5530, '568bf18b4e0c83c1', 'ai_theaters', {
    onEnter() { this.s = this.context.ecx; const m = rdp(this.s.add(0x0c)); this.bit2 = m === null || m.isNull() ? null : (rd(m.add(4)) >>> 1) & 1; },
    onLeave() {
      const s = this.s, list = rdp(s.add(0x2c));
      let n = 0, node = list === null || list.isNull() ? null : rdp(list.add(4));
      while (node !== null && !node.isNull()) { const l = rdp(node.add(4)); if (l === null || l.isNull()) break; node = l; }
      while (node !== null && !node.isNull() && n < 100) { n++; node = rdp(node.add(0x10)); }
      emit('ai_theaters', { tick: clockField(0x0c), ran: this.bit2, theaters: n, f10: hex(rd(s.add(0x10))) });
    },
  }],
  // The per-ship instruction hooks at VA 0x478b2c / 0x478b58 (ai_surplus_ship / ai_surplus_held) are removed: with them
  // the run-3-seed game hung in an endless surplus-builder loop at ticks 44-60 (58641 calls in one tick); without --ai-hooks it ran on
  // ai_surplus_ship is now read on the builder's entry (record-map walk, no mid-function hook); ai_surplus_held is gone.
  // Rescue (VA 0x47e620) / abduction (VA 0x483640) target search, ECX = mission op (key +0x18, objective +0x30, target +0x34):
  // returns the target system record or 0 (game C tick 118).
  [0x47e620, '6aff68e63c630064', 'ai_rescue', {
    onEnter() { this.o = this.context.ecx; },
    onLeave(ret) { const o = this.o; emit('ai_rescue', { tick: clockField(0x0c), op: rd(o.add(0x18)), objective: hex(rd(o.add(0x30))),
      target: hex(rd(o.add(0x34))), ret: hex(ret.toUInt32()), retKey: ret.toUInt32() < 0x10000 ? null : hex(rd(ret.add(0x18))) }); },
  }],
  [0x483640, '6aff682645630064', 'ai_abduct', {
    onEnter() { this.o = this.context.ecx; },
    onLeave(ret) { const o = this.o; emit('ai_abduct', { tick: clockField(0x0c), op: rd(o.add(0x18)), objective: hex(rd(o.add(0x30))),
      target: hex(rd(o.add(0x34))), ret: hex(ret.toUInt32()), retKey: ret.toUInt32() < 0x10000 ? null : hex(rd(ret.add(0x18))) }); },
  }],
  // Decoys at staging (VA 0x4bd000, fastcall ECX = mission op): n = decoys (op +0x44) with agent +0x38 = op +0x38 and
  // +0x30 & 0x74000F1 = 0. Called by the rescue / abduction / Death Star searches before their S +0xBC div 2 <= n test. The
  // ai_abduct line of run 13 tick 889 threw in onLeave (ret is not a record pointer there), hence the retKey guard above.
  [0x4bd000, '6aff68b89c630064', 'ai_decoyn', {
    onEnter() { this.o = this.context.ecx; },
    onLeave(ret) { emit('ai_decoyn', { tick: clockField(0x0c), op: rd(this.o.add(0x18)), staging: hex(rd(this.o.add(0x38))), n: ret.toInt32() }); },
  }],
  // Planner 3 facility budget (VA 0x4da530, ECX = planner 3; model at +0x2C): the returned budget b with the model's capacity
  // (+0x184) and demand (+0x188); and planner 3's request step C (VA 0x4da0d0): the joined system +0x58 and model +0x08 after it
  // (the implementation's C joins a system in runs 3 / 9 / 10 where the original goes on).
  [0x4da530, '64a1000000006aff', 'ai_p3budget', {
    onEnter() { this.p = this.context.ecx; this.ra = this.returnAddress; },
    onLeave(ret) { const m = rdp(this.p.add(0x2c)); emit('ai_p3budget', { tick: clockField(0x0c), b: ret.toInt32(), ra: hex(this.ra),
      cap: m === null || m.isNull() ? null : rd(m.add(0x184)) | 0, dem: m === null || m.isNull() ? null : rd(m.add(0x188)) | 0 }); },
  }],
  [0x4da0d0, '6aff689bcf630064', 'ai_p3c', {
    onEnter() { this.p = this.context.ecx; this.before = rd(this.p.add(0x58)); },
    onLeave() { const m = rdp(this.p.add(0x2c)); emit('ai_p3c', { tick: clockField(0x0c), before: hex(this.before), build: hex(rd(this.p.add(0x58))),
      model08: m === null || m.isNull() ? null : hex(rd(m.add(0x08))) }); },
  }],
  // Random-key system lists built for mission-op staging: VA 0x41a460 (thiscall model) is a gate only; when its
  // caller lies in the staging function VA 0x4bd0a0 or in VA 0x41b2b0 (nearest own system), the inner builder VA 0x431f40 (thiscall
  // sector record; args req28, req2C, req30, excl28, excl2C, excl30, dir) logs the sector, the masks and every slot (+0x48 + 4i, i < 10:
  // system key +0x18 and words +0x28 / +0x2C / +0x30), so the dropped candidates can be read off.
  // VA 0x41b2b0 has eleven callers (fleet-op builders and tasks among them, some called hundreds of times per tick); only its call
  // from the staging VA 0x4bd0a0 opens the gate (bounded volume: about 20 ai_rk lines per staging call).
  [0x41b2b0, '6aff68bfb5620064', 'ai_rk_stage', {
    onEnter() { const ra = this.returnAddress.toUInt32(); this.on = ra >= 0x4bd0a0 && ra < 0x4bd570; if (this.on) globalThis.rkStage = 1; },
    onLeave() { if (this.on) globalThis.rkStage = 0; },
  }],
  [0x41a460, '518b442408568bf1', 'ai_rk_gate', {
    onEnter() { const ra = this.returnAddress.toUInt32();
      this.on = (ra >= 0x4bd0a0 && ra < 0x4bd570) || (globalThis.rkStage === 1 && ra >= 0x41b2b0 && ra < 0x41b7b0);
      if (this.on) globalThis.rkCaller = ra; },
    onLeave() { if (this.on) globalThis.rkCaller = 0; },
  }],
  [0x431f40, '6aff6825d0620064', 'ai_rk', {
    onEnter(args) {
      if (!globalThis.rkCaller) return;
      const s = this.context.ecx, masks = [0, 1, 2, 3, 4, 5].map((i) => hex(args[i].toUInt32())), slots = [];
      for (let i = 0; i < 10; i++) {
        const r = rdp(s.add(0x48 + 4 * i));
        slots.push(r === null || r.isNull() ? null : [hex(rd(r.add(0x18))), hex(rd(r.add(0x28))), hex(rd(r.add(0x2c))), hex(rd(r.add(0x30)))]);
      }
      emit('ai_rk', { tick: clockField(0x0c), caller: hex(globalThis.rkCaller), sector: hex(rd(s.add(0x18))), masks, dir: args[6].toInt32(), slots });
    },
  }],
  // Support copies into the side views: the support setter VA 0x509c30 (thiscall system; value, context) is a gate that
  // keeps its return address; inside it, each view copy VA 0x50f390 (thiscall view; context) logs the view's side (+0x24 bits 4-5) and
  // state (bits 0-3), the context words +4 / +8 and the view's +0x58 before / after, so the context that lets a frozen view (state 2)
  // follow a write can be read off per caller.
  [0x509c30, '535556578bf1e8c5', 'ai_supset_gate', {
    onEnter() { this.prev = globalThis.supRa || 0; globalThis.supRa = this.returnAddress.toUInt32(); },
    onLeave() { globalThis.supRa = this.prev; }, // nested setter calls (hook cascade) restore the outer caller
  }],
  [0x50f390, '51535556578bf1e8', 'ai_supview', {
    onEnter(args) {
      this.on = !!globalThis.supRa;
      if (!this.on) return;
      this.v = this.context.ecx; this.c = args[0]; this.before = rd(this.v.add(0x58));
    },
    onLeave() {
      if (!this.on) return;
      const w = rd(this.v.add(0x24));
      emit('ai_supview', { tick: clockField(0x0c), ra: hex(globalThis.supRa), key: hex(rd(this.v.add(0x18))), side: (w >> 4) & 3, state: w & 0xf,
        ctx4: rd(this.c.add(4)), ctx8: rd(this.c.add(8)), before: this.before, after: rd(this.v.add(0x58)) });
    },
  }],
  // Mission views (unverified on a live run): which side's view of a mission is live when it is destroyed, and why. Missions only (family
  // [0x40, 0x80)) to bound the volume. view_live = live recompute VA 0x4f9a60 (thiscall real object; link block +0x20, view k at
  // link +0x24 + 4k): both views' +0x24 state nibble before / after, the object's owner bits and +0x50 (ObservedBy bits 7 / 8).
  // view_destroyed = Destroyed hook VA 0x4fc080 (thiscall view; posts message 0x302 when the view has a parent): view side, state,
  // owner and its return value.
  [0x4f9a60, '6aff6838ff630064', 'view_live', {
    onEnter() {
      const o = this.context.ecx, key = rd(o.add(0x18));
      this.on = key !== null && (key >>> 24) >= 0x40 && (key >>> 24) < 0x80;
      if (!this.on) return;
      const link = rdp(o.add(0x20));
      this.o = o; this.key = key;
      this.views = [1, 2].map((k) => (link === null || link.isNull() ? null : rdp(link.add(0x24 + 4 * k))));
      this.before = this.views.map((v) => (v === null || v.isNull() ? null : rd(v.add(0x24)) & 0xf));
    },
    onLeave() {
      if (!this.on) return;
      const w = rd(this.o.add(0x24));
      emit('view_live', { tick: clockField(0x0c), key: hex(this.key), owner: (w >> 6) & 3, f50: hex(rd(this.o.add(0x50))),
        before: this.before, after: this.views.map((v) => (v === null || v.isNull() ? null : rd(v.add(0x24)) & 0xf)) });
    },
  }],
  [0x4fc080, '6aff68b001640064', 'view_destroyed', {
    onEnter() {
      const v = this.context.ecx, key = rd(v.add(0x18));
      this.on = key !== null && (key >>> 24) >= 0x40 && (key >>> 24) < 0x80;
      this.v = v; this.key = key;
    },
    onLeave(ret) {
      if (!this.on) return;
      const w = rd(this.v.add(0x24));
      emit('view_destroyed', { tick: clockField(0x0c), key: hex(this.key), side: (w >> 4) & 3, state: w & 0xf, owner: (w >> 6) & 3,
        f50: hex(rd(this.v.add(0x50))), ret: ret.toInt32() });
    },
  }],
);

emit('module', { base: hex(mod.base), size: mod.size });
if (!mod.base.equals(ptr(0x400000))) emit('error', { msg: 'image base differs from 0x400000; VAs not rebased' });

// Every callback runs inside try/catch: a decode error becomes a "hook_error" event (first 20 per
// hook, then counted silently) instead of a Frida script error or an aborted callback.
const hookErrors = {};
function guarded(name, cb) {
  const safe = (fn) => fn && function (args) {
    try { return fn.call(this, args); } catch (e) {
      const k = (hookErrors[name] = (hookErrors[name] || 0) + 1);
      if (k <= 20) try { emit('hook_error', { name, n: k, msg: String(e), stack: e.stack }); } catch (_) {}
    }
  };
  const out = {};
  if (cb.onEnter) out.onEnter = safe(cb.onEnter);
  if (cb.onLeave) out.onLeave = safe(cb.onLeave);
  return out;
}

// Verify every prologue before attaching any: an attached hook rewrites its first bytes, so a second hook on the same VA
// (support_set + ai_supset_gate on 0x509c30) would otherwise fail its check.
const verified = HOOKS.filter(([va, bytes, name]) => {
  const got = Array.from(new Uint8Array(ptr(va).readByteArray(bytes.length / 2)))
    .map((b) => b.toString(16).padStart(2, '0')).join('');
  if (got !== bytes) emit('verify_failed', { name, va: hex(va), expected: bytes, got });
  return got === bytes;
});
for (const [va, , name, cb] of verified) {
  Interceptor.attach(ptr(va), guarded(name, cb));
  emit('hook', { name, va: hex(va) });
}
