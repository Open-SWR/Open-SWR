// Frida draw tracer for the original game exe (spike).
// Logs the drawing calls the exe makes: GDI / USER32 text, blits, shapes, fonts, palettes, resource loads, and the
// DirectDraw surface methods (vtable hooks after DirectDrawCreate). Only calls whose return address lies in REBEXE.EXE
// are logged. DirectDraw Lock calls are counted per caller (ra) and sent as a summary every 2 s: the callers are the
// exe's own pixel writers (internal blitters). Numbers and strings only; traces go outside the repo.
// Argument layouts are the documented Win32 / DirectDraw 1 signatures; nothing here comes from the exe's code.

'use strict';

const exe = Process.getModuleByName('REBEXE.EXE');
const exeEnd = exe.base.add(exe.size);
const fromExe = (ra) => ra.compare(exe.base) >= 0 && ra.compare(exeEnd) < 0;
const hex = (p) => '0x' + ptr(p).toString(16);
let seq = 0;
function emit(ev, f) { f.seq = seq++; f.ev = ev; f.t = Date.now(); send(f); }
function err(name, e) { send({ ev: 'hook_error', name, msg: String(e), stack: e.stack }); }

// ---- handle bookkeeping: resources, DCs, GDI objects, surfaces ----

const res = new Map();      // locked resource pointer -> {mod, type, id, size}
const hrsrc = new Map();    // HRSRC -> {mod, type, id, size}
const hglob = new Map();    // HGLOBAL -> resource
const dcs = new Map();      // HDC -> {id, kind, surf?}
const objs = new Map();     // GDI handle -> {kind, ...}
const surfs = new Map();    // surface pointer -> {id, w, h, caps}
const pals = new Map();     // DirectDraw palette pointer -> id
let nDc = 0, nSurf = 0, nPal = 0;

function resName(p) {
  return p.compare(ptr(0x10000)) < 0 ? p.toInt32() : p.readAnsiString();
}
function resOf(p) {
  if (p.isNull()) return null;
  const r = res.get(p.toString());
  if (r) return r;
  for (const [k, v] of res) { const b = ptr(k); if (p.compare(b) >= 0 && p.compare(b.add(v.size)) < 0) return v; }
  return null;
}
function resTag(r) { return r === null ? null : `${r.mod}:${r.type}:${r.id}`; }
function dcTag(h) {
  const d = dcs.get(h.toString());
  if (d) return d.surf !== undefined ? `dc${d.id}/s${d.surf}` : `dc${d.id}`;
  return hex(h);
}
function newDc(h, kind, extra) {
  const d = Object.assign({ id: ++nDc, kind }, extra || {});
  dcs.set(h.toString(), d);
  return d;
}
function objTag(h) { const o = objs.get(h.toString()); return o ? o.tag : hex(h); }
function rect(p) { return p.isNull() ? null : [p.readS32(), p.add(4).readS32(), p.add(8).readS32(), p.add(12).readS32()]; }
function bmih(p) { return p.isNull() ? null : { w: p.add(4).readS32(), h: p.add(8).readS32(), bpp: p.add(14).readU16() }; }
function text(p, n) { return p.isNull() ? null : n < 0 ? p.readAnsiString() : p.readAnsiString(n); }
function colours(p, n) { // PALETTEENTRY / RGBQUAD-like 4-byte entries as hex RRGGBB
  const out = [];
  for (let i = 0; i < n; i++) {
    const q = p.add(i * 4);
    out.push(((q.readU8() << 16) | (q.add(1).readU8() << 8) | q.add(2).readU8()).toString(16).padStart(6, '0'));
  }
  return out.join(' ');
}

// ---- hook installation ----

const hooked = new Set();
function hook(addr, name, cb) {
  if (addr === null || addr.isNull() || hooked.has(addr.toString())) return;
  hooked.add(addr.toString());
  Interceptor.attach(addr, {
    onEnter(args) {
      this.log = cb.any || fromExe(this.returnAddress);
      if (!this.log || !cb.onEnter) return;
      try { cb.onEnter.call(this, args); } catch (e) { err(name, e); }
    },
    onLeave(ret) {
      if (!this.log || !cb.onLeave) return;
      try { cb.onLeave.call(this, ret); } catch (e) { err(name, e); }
    },
  });
  emit('hook', { name, addr: hex(addr) });
}
// Resolve an export through the named modules (first module that has it).
function exp(mods, name) {
  for (const m of mods) {
    const mod = Process.findModuleByName(m);
    const a = mod === null ? null : mod.findExportByName(name);
    if (a !== null) return a;
  }
  return null;
}
const GDI = ['gdi32.dll', 'gdi32full.dll']; // gdi32 first: the exe's IAT points there (stub or forwarder), so the return address is the exe's
const USER = ['user32.dll'];
const KERNEL = ['kernel32.dll', 'kernelbase.dll'];

// ---- kernel32: resource loads (map locked pointers back to module:type:id) ----

const sizeofResource = new NativeFunction(exp(KERNEL, 'SizeofResource'), 'uint32', ['pointer', 'pointer'], 'stdcall');
function hookKernel() {
  hook(exp(KERNEL, 'FindResourceA'), 'FindResourceA', {
    onEnter(a) { this.m = a[0]; this.n = resName(a[1]); this.ty = resName(a[2]); },
    onLeave(r) {
      if (r.isNull()) return;
      const mod = Process.findModuleByAddress(this.m.isNull() ? exe.base : this.m);
      // Briefing skip: every briefing-DLL (module 13) lookup fails, so the game-start briefing shrinks to its ~2 s of
      // stills, as on an install without the DLL. Off with KEEP_BRIEFING (trace.py --keep-briefing).
      if (!globalThis.KEEP_BRIEFING && mod && /^(al|em)brief\.dll$/i.test(mod.name)) { r.replace(ptr(0)); return; }
      hrsrc.set(r.toString(), { mod: mod ? mod.name : hex(this.m), type: this.ty, id: this.n, size: sizeofResource(this.m, r) });
    },
  });
  hook(exp(KERNEL, 'LoadResource'), 'LoadResource', {
    onEnter(a) { this.h = a[1]; },
    onLeave(r) { const v = hrsrc.get(this.h.toString()); if (v && !r.isNull()) hglob.set(r.toString(), v); },
  });
  hook(exp(KERNEL, 'LockResource'), 'LockResource', {
    onEnter(a) { this.h = a[0]; },
    onLeave(r) {
      const v = hglob.get(this.h.toString()) || hglob.get(r.toString());
      if (!v || r.isNull()) return;
      res.set(r.toString(), v);
      emit('res', { res: resTag(v), size: v.size, ra: hex(this.returnAddress) });
    },
  });
}

// ---- gdi32 ----

function hookGdi() {
  hook(exp(GDI, 'CreateCompatibleDC'), 'CreateCompatibleDC', {
    onEnter(a) { this.src = a[0]; },
    onLeave(r) { const d = newDc(r, 'mem'); emit('dc', { dc: `dc${d.id}`, kind: 'mem', from: this.src.isNull() ? null : dcTag(this.src) }); },
  });
  hook(exp(GDI, 'CreateICA'), 'CreateICA', { onLeave(r) { const d = newDc(r, 'ic'); emit('dc', { dc: `dc${d.id}`, kind: 'ic' }); } });
  hook(exp(GDI, 'DeleteDC'), 'DeleteDC', { onEnter(a) { dcs.delete(a[0].toString()); } });
  hook(exp(GDI, 'SelectObject'), 'SelectObject', {
    onEnter(a) { emit('select', { dc: dcTag(a[0]), obj: objTag(a[1]), ra: hex(this.returnAddress) }); },
  });
  hook(exp(GDI, 'DeleteObject'), 'DeleteObject', { onEnter(a) { objs.delete(a[0].toString()); } });
  hook(exp(GDI, 'GetStockObject'), 'GetStockObject', {
    onEnter(a) { this.i = a[0].toInt32(); },
    onLeave(r) { objs.set(r.toString(), { tag: `stock${this.i}` }); },
  });
  hook(exp(GDI, 'CreateFontIndirectA'), 'CreateFontIndirectA', {
    onEnter(a) {
      const p = a[0];
      this.f = { height: p.readS32(), width: p.add(4).readS32(), esc: p.add(8).readS32(), weight: p.add(16).readS32(),
        italic: p.add(20).readU8(), underline: p.add(21).readU8(), charset: p.add(23).readU8(), outPrec: p.add(24).readU8(),
        clipPrec: p.add(25).readU8(), quality: p.add(26).readU8(), pitch: p.add(27).readU8(), face: p.add(28).readAnsiString() };
    },
    onLeave(r) { const tag = `font${objs.size}`; objs.set(r.toString(), { tag }); emit('font', Object.assign({ font: tag, ra: hex(this.returnAddress) }, this.f)); },
  });
  for (const [name, kind] of [['CreateSolidBrush', 'brush'], ['CreatePen', 'pen'], ['CreateBrushIndirect', 'brush']]) {
    hook(exp(GDI, name), name, {
      onEnter(a) {
        this.v = name === 'CreatePen' ? { style: a[0].toInt32(), width: a[1].toInt32(), color: hex(a[2]) }
          : name === 'CreateSolidBrush' ? { color: hex(a[0]) }
          : { style: a[0].readU32(), color: hex(a[0].add(4).readU32()), hatch: a[0].add(8).readU32() };
      },
      onLeave(r) { const tag = `${kind}${objs.size}`; objs.set(r.toString(), { tag }); emit(kind, Object.assign({ obj: tag }, this.v)); },
    });
  }
  hook(exp(GDI, 'CreateCompatibleBitmap'), 'CreateCompatibleBitmap', {
    onEnter(a) { this.w = a[1].toInt32(); this.h = a[2].toInt32(); },
    onLeave(r) { const tag = `bmp${objs.size}`; objs.set(r.toString(), { tag }); emit('bitmap', { obj: tag, kind: 'compatible', w: this.w, h: this.h }); },
  });
  hook(exp(GDI, 'CreateDIBitmap'), 'CreateDIBitmap', {
    onEnter(a) { this.hdr = bmih(a[1]); this.res = resTag(resOf(a[1])) || resTag(resOf(a[3])) || resTag(resOf(a[4])); },
    onLeave(r) { const tag = `bmp${objs.size}`; objs.set(r.toString(), { tag }); emit('bitmap', Object.assign({ obj: tag, kind: 'dib', res: this.res, ra: hex(this.returnAddress) }, this.hdr)); },
  });
  hook(exp(GDI, 'CreateDIBSection'), 'CreateDIBSection', {
    onEnter(a) { this.hdr = bmih(a[1]); this.res = resTag(resOf(a[1])); },
    onLeave(r) { const tag = `bmp${objs.size}`; objs.set(r.toString(), { tag }); emit('bitmap', Object.assign({ obj: tag, kind: 'section', res: this.res, ra: hex(this.returnAddress) }, this.hdr)); },
  });
  hook(exp(GDI, 'BitBlt'), 'BitBlt', {
    onEnter(a) {
      emit('bitblt', { dst: dcTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32(), w: a[3].toInt32(), h: a[4].toInt32(),
        src: a[5].isNull() ? null : dcTag(a[5]), sx: a[6].toInt32(), sy: a[7].toInt32(), rop: hex(a[8]), ra: hex(this.returnAddress) });
    },
  });
  hook(exp(GDI, 'SetDIBitsToDevice'), 'SetDIBitsToDevice', {
    onEnter(a) {
      emit('setdibits', { dst: dcTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32(), w: a[3].toInt32(), h: a[4].toInt32(),
        sx: a[5].toInt32(), sy: a[6].toInt32(), start: a[7].toInt32(), lines: a[8].toInt32(), res: resTag(resOf(a[9])) || resTag(resOf(a[10])),
        hdr: bmih(a[10]), use: a[11].toInt32(), ra: hex(this.returnAddress) });
    },
  });
  hook(exp(GDI, 'PatBlt'), 'PatBlt', {
    onEnter(a) { emit('patblt', { dc: dcTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32(), w: a[3].toInt32(), h: a[4].toInt32(), rop: hex(a[5]), ra: hex(this.returnAddress) }); },
  });
  hook(exp(GDI, 'Rectangle'), 'Rectangle', {
    onEnter(a) { emit('rectangle', { dc: dcTag(a[0]), r: [a[1].toInt32(), a[2].toInt32(), a[3].toInt32(), a[4].toInt32()], ra: hex(this.returnAddress) }); },
  });
  hook(exp(GDI, 'MoveToEx'), 'MoveToEx', { onEnter(a) { emit('moveto', { dc: dcTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32() }); } });
  hook(exp(GDI, 'LineTo'), 'LineTo', { onEnter(a) { emit('lineto', { dc: dcTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32(), ra: hex(this.returnAddress) }); } });
  hook(exp(GDI, 'Polyline'), 'Polyline', {
    onEnter(a) {
      const n = a[2].toInt32(), pts = [];
      for (let i = 0; i < n; i++) pts.push([a[1].add(i * 8).readS32(), a[1].add(i * 8 + 4).readS32()]);
      emit('polyline', { dc: dcTag(a[0]), pts, ra: hex(this.returnAddress) });
    },
  });
  hook(exp(GDI, 'TextOutA'), 'TextOutA', {
    onEnter(a) { emit('textout', { dc: dcTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32(), s: text(a[3], a[4].toInt32()), ra: hex(this.returnAddress) }); },
  });
  for (const name of ['SetTextColor', 'SetBkColor']) {
    hook(exp(GDI, name), name, { onEnter(a) { emit(name === 'SetTextColor' ? 'textcolor' : 'bkcolor', { dc: dcTag(a[0]), color: hex(a[1]) }); } });
  }
  for (const [name, ev] of [['SetBkMode', 'bkmode'], ['SetTextAlign', 'textalign'], ['SetROP2', 'rop2'], ['SetStretchBltMode', 'stretchmode']]) {
    hook(exp(GDI, name), name, { onEnter(a) { emit(ev, { dc: dcTag(a[0]), v: a[1].toInt32() }); } });
  }
  hook(exp(GDI, 'CreatePalette'), 'CreatePalette', {
    onEnter(a) { this.n = a[0].add(2).readU16(); this.c = colours(a[0].add(4), this.n); },
    onLeave(r) { const tag = `hpal${objs.size}`; objs.set(r.toString(), { tag }); emit('palette', { obj: tag, n: this.n, colours: this.c, ra: hex(this.returnAddress) }); },
  });
  hook(exp(GDI, 'SetPaletteEntries'), 'SetPaletteEntries', {
    onEnter(a) { const s = a[1].toInt32(), n = a[2].toInt32(); emit('palette_set', { obj: objTag(a[0]), start: s, n, colours: colours(a[3], n), ra: hex(this.returnAddress) }); },
  });
  hook(exp(GDI, 'SelectPalette'), 'SelectPalette', { onEnter(a) { emit('palette_select', { dc: dcTag(a[0]), obj: objTag(a[1]), bg: a[2].toInt32() }); } });
  hook(exp(GDI, 'RealizePalette'), 'RealizePalette', { onEnter(a) { emit('palette_realize', { dc: dcTag(a[0]) }); } });
}

// ---- user32 ----

function hookUser() {
  hook(exp(USER, 'DrawTextA'), 'DrawTextA', {
    onEnter(a) { emit('drawtext', { dc: dcTag(a[0]), s: text(a[1], a[2].toInt32()), r: rect(a[3]), fmt: hex(a[4]), ra: hex(this.returnAddress) }); },
  });
  for (const name of ['GetDC', 'GetWindowDC']) {
    hook(exp(USER, name), name, { onLeave(r) { const d = newDc(r, name === 'GetDC' ? 'window' : 'windowframe'); emit('dc', { dc: `dc${d.id}`, kind: d.kind }); } });
  }
  hook(exp(USER, 'BeginPaint'), 'BeginPaint', {
    onEnter(a) { this.ps = a[1]; },
    onLeave(r) { const d = newDc(r, 'paint'); emit('paint', { dc: `dc${d.id}`, r: rect(this.ps.add(8)) }); },
  });
  hook(exp(USER, 'EndPaint'), 'EndPaint', { onEnter() { emit('paint_end', {}); } });
}

// ---- DirectDraw (COM, stdcall, this = args[0]); DirectDraw 1 vtable slots ----

const lockCounts = new Map(); // "ra s<id>" -> count
function surfTag(p) { const s = surfs.get(p.toString()); return s ? `s${s.id}` : (p.isNull() ? null : hex(p)); }

function hookSurfaceVtable(vt) {
  const slot = (i) => vt.add(i * 4).readPointer();
  hook(slot(0), 'Surface.QueryInterface', { onEnter(a) { this.out = a[2]; }, onLeave() { const p = this.out.readPointer(); if (!p.isNull()) hookSurfaceVtable(p.readPointer()); } });
  hook(slot(5), 'Surface.Blt', {
    onEnter(a) {
      emit('ddblt', { dst: surfTag(a[0]), dr: rect(a[1]), src: surfTag(a[2]), sr: rect(a[3]), flags: hex(a[4]),
        fill: a[5].isNull() ? null : hex(a[5].add(80).readU32()), ra: hex(this.returnAddress) }); // DDBLTFX.dwFillColor at +80
    },
  });
  hook(slot(7), 'Surface.BltFast', {
    onEnter(a) { emit('ddbltfast', { dst: surfTag(a[0]), x: a[1].toInt32(), y: a[2].toInt32(), src: surfTag(a[3]), sr: rect(a[4]), trans: hex(a[5]), ra: hex(this.returnAddress) }); },
  });
  hook(slot(11), 'Surface.Flip', { onEnter(a) { emit('ddflip', { surf: surfTag(a[0]), flags: hex(a[2]) }); } });
  hook(slot(12), 'Surface.GetAttachedSurface', {
    onEnter(a) { this.out = a[2]; this.parent = a[0]; },
    onLeave() { const p = this.out.readPointer(); if (!p.isNull() && !surfs.has(p.toString())) { const id = ++nSurf; surfs.set(p.toString(), { id }); emit('ddsurface', { surf: `s${id}`, attachedTo: surfTag(this.parent) }); } },
  });
  hook(slot(17), 'Surface.GetDC', {
    onEnter(a) { this.s = a[0]; this.out = a[1]; },
    onLeave() { const h = this.out.readPointer(); const s = surfs.get(this.s.toString()); newDc(h, 'surface', { surf: s ? s.id : -1 }); },
  });
  hook(slot(25), 'Surface.Lock', {
    onEnter(a) { const k = `${hex(this.returnAddress)} ${surfTag(a[0])}`; lockCounts.set(k, (lockCounts.get(k) || 0) + 1); },
  });
  hook(slot(29), 'Surface.SetColorKey', {
    onEnter(a) { emit('ddcolorkey', { surf: surfTag(a[0]), flags: hex(a[1]), key: a[2].isNull() ? null : [a[2].readU32(), a[2].add(4).readU32()] }); },
  });
  hook(slot(31), 'Surface.SetPalette', { onEnter(a) { emit('ddsetpalette', { surf: surfTag(a[0]), pal: pals.has(a[1].toString()) ? `p${pals.get(a[1].toString())}` : hex(a[1]) }); } });
}

function hookPaletteVtable(vt) {
  hook(vt.add(6 * 4).readPointer(), 'Palette.SetEntries', {
    onEnter(a) { const s = a[2].toInt32(), n = a[3].toInt32(); emit('ddpalette_set', { pal: `p${pals.get(a[0].toString())}`, start: s, n, colours: colours(a[4], n), ra: hex(this.returnAddress) }); },
  });
}

function hookDirectDrawVtable(vt) {
  const slot = (i) => vt.add(i * 4).readPointer();
  hook(slot(0), 'DirectDraw.QueryInterface', { any: true, onEnter(a) { this.out = a[2]; }, onLeave() { const p = this.out.readPointer(); if (!p.isNull()) hookDirectDrawVtable(p.readPointer()); } });
  hook(slot(5), 'DirectDraw.CreatePalette', {
    onEnter(a) { this.flags = a[1].toInt32(); this.c = colours(a[2], 256); this.out = a[3]; },
    onLeave() { const p = this.out.readPointer(); if (p.isNull()) return; const id = ++nPal; pals.set(p.toString(), id); hookPaletteVtable(p.readPointer()); emit('ddpalette', { pal: `p${id}`, flags: hex(this.flags), colours: this.c, ra: hex(this.returnAddress) }); },
  });
  hook(slot(6), 'DirectDraw.CreateSurface', {
    any: true,
    onEnter(a) {
      const d = a[1];
      this.desc = { flags: hex(d.add(4).readU32()), h: d.add(8).readU32(), w: d.add(12).readU32(), backBuffers: d.add(20).readU32(), caps: hex(d.add(104).readU32()) };
      this.out = a[2];
    },
    onLeave(ret) {
      // d3drm calls this with a null out pointer during the tactical setup
      if (ret.toInt32() !== 0 || this.out.isNull()) return;
      const p = this.out.readPointer(); if (p.isNull()) return;
      const id = ++nSurf; surfs.set(p.toString(), Object.assign({ id }, this.desc));
      hookSurfaceVtable(p.readPointer());
      emit('ddsurface', Object.assign({ surf: `s${id}`, ra: hex(this.returnAddress) }, this.desc));
    },
  });
  hook(slot(21), 'DirectDraw.SetDisplayMode', { onEnter(a) { emit('ddmode', { w: a[1].toInt32(), h: a[2].toInt32(), bpp: a[3].toInt32() }); } });
}

function hookDirectDraw() {
  hook(exp(['ddraw.dll'], 'DirectDrawCreate'), 'DirectDrawCreate', {
    any: true, // DxWnd calls it from its own stub
    onEnter(a) { this.out = a[1]; },
    onLeave() { const p = this.out.readPointer(); if (!p.isNull()) hookDirectDrawVtable(p.readPointer()); },
  });
}

// ---- the exe's bitmap class: objects tagged with their origin, internal blits, screen draws ----
// Object layout used here (40 bytes): +0 BITMAPINFOHEADER*, +4 bits, +8 key colour, +0xC kind byte (1 / 2 memory data,
// 4 canvas, 0x10 DIB section), +0x18 HMODULE. Methods are thiscall (this = ECX). Blit directions checked on a main-menu
// trace (cockpit resource -> backbuffer section, backbuffer -> button canvas, sprite keyed onto canvas with key 252).

const bms = new Map(); // object pointer -> tag
let nBm = 0;
function bmTag(p) { return p.isNull() ? null : bms.get(p.toString()) || hex(p); }
function bmSize(p) {
  const h = rdp(p); if (h === null || h.isNull()) return [null, null];
  return [h.add(4).readS32(), h.add(8).readS32()];
}
function rdp(p) { try { return p.readPointer(); } catch (e) { return null; } }
function bmNew(obj, kind, extra) {
  const tag = `b${++nBm}`;
  bms.set(obj.toString(), tag);
  const [w, h] = bmSize(obj);
  emit('bm_new', Object.assign({ bm: tag, kind, w, h }, extra));
}

const EXE_HOOKS = [
  // [VA, expected first bytes, name, callbacks]
  [0x5fbd20, '6aff68ab5e65', 'bm_ctor_res', { // (module, resource id, kind): RT_BITMAP from a DLL
    onEnter(a) { this.o = this.context.ecx; this.m = a[0]; this.id = a[1].toInt32() & 0xffff; },
    onLeave() { const m = Process.findModuleByAddress(this.m); bmNew(this.o, 'res', { res: `${m ? m.name.toLowerCase() : hex(this.m)}:2:${this.id}`, ra: hex(this.returnAddress) }); },
  }],
  [0x5fbc80, '6aff688b5e65', 'bm_ctor_mem', { // (module, data, kind): from data in memory (Inferred: RCDATA / decoded)
    onEnter(a) { this.o = this.context.ecx; this.d = a[1]; this.k = a[2].toInt32(); },
    onLeave() { bmNew(this.o, 'mem', { res: resTag(resOf(this.d)), mode: this.k, ra: hex(this.returnAddress) }); },
  }],
  [0x5fbda0, '6aff68cb5e65', 'bm_ctor_canvas', { // (w, h, template object): blank memory bitmap
    onEnter(a) { this.o = this.context.ecx; this.t = a[2]; },
    onLeave() { bmNew(this.o, 'canvas', { from: bmTag(this.t), ra: hex(this.returnAddress) }); },
  }],
  [0x5fbea0, '6aff68eb5e65', 'bm_ctor_section', { // (w, h, template object, hdc): DIB section
    onEnter(a) { this.o = this.context.ecx; this.t = a[2]; },
    onLeave() { bmNew(this.o, 'section', { from: bmTag(this.t), ra: hex(this.returnAddress) }); },
  }],
  [0x5fbfa0, '6aff680b5f65', 'bm_dtor', { onEnter() { const k = this.context.ecx.toString(); if (bms.has(k)) { emit('bm_free', { bm: bms.get(k) }); bms.delete(k); } } }],
  [0x5fcc30, '83ec20535556', 'bm_blit', { // this = source; (dst, dx, dy, sx, sy, w, h), 0 = full: opaque copy
    onEnter(a) { emit('bm_blit', { src: bmTag(this.context.ecx), dst: bmTag(a[0]), dx: a[1].toInt32(), dy: a[2].toInt32(), sx: a[3].toInt32(), sy: a[4].toInt32(), w: a[5].toInt32(), h: a[6].toInt32(), ra: hex(this.returnAddress) }); },
  }],
  [0x5fc710, '83ec20535556', 'bm_kblit', { // this = source; (dst, dx, dy, key colour, sx, sy, w, h): keyed copy (key 252 = blue)
    onEnter(a) { emit('bm_kblit', { src: bmTag(this.context.ecx), dst: bmTag(a[0]), dx: a[1].toInt32(), dy: a[2].toInt32(), key: a[3].toInt32() & 0xff, sx: a[4].toInt32(), sy: a[5].toInt32(), w: a[6].toInt32(), h: a[7].toInt32(), ra: hex(this.returnAddress) }); },
  }],
  [0x5fd0f0, '56578bf133ff', 'bm_kdraw', { // this = source sprite (key colour +8); (dst, dx, dy): keyed copy of the whole sprite
    onEnter(a) { emit('bm_kdraw', { src: bmTag(this.context.ecx), dst: bmTag(a[0]), dx: a[1].toInt32(), dy: a[2].toInt32(), ra: hex(this.returnAddress) }); },
  }],
  [0x5fcfe0, '51558be9e8d7', 'bm_fill', { // this = destination; (x, y, w, h, colour index)
    onEnter(a) { emit('bm_fill', { dst: bmTag(this.context.ecx), x: a[0].toInt32(), y: a[1].toInt32(), w: a[2].toInt32(), h: a[3].toInt32(), c: a[4].toInt32() & 0xff, ra: hex(this.returnAddress) }); },
  }],
  [0x5fc140, '83ec2455578b', 'bm_draw', { // this = bitmap; (hdc, mode 0x10 / 0x40, rop, x, y, sx, sy, w, h): to a DC
    onEnter(a) { emit('bm_draw', { bm: bmTag(this.context.ecx), dc: dcTag(a[0]), mode: a[1].toInt32() & 0xff, rop: hex(a[2]), x: a[3].toInt32(), y: a[4].toInt32(), sx: a[5].toInt32(), sy: a[6].toInt32(), w: a[7].toInt32(), h: a[8].toInt32(), ra: hex(this.returnAddress) }); },
  }],
];
function hookExe() {
  for (const [va, bytes, name, cb] of EXE_HOOKS) {
    const p = ptr(va), got = Array.from(new Uint8Array(p.readByteArray(bytes.length / 2))).map((b) => b.toString(16).padStart(2, '0')).join('');
    if (got !== bytes) { emit('verify_failed', { name, va: hex(va), expected: bytes, got }); continue; }
    hook(p, name, cb);
  }
}

// ---- install: modules may load after the agent (spawn), so retry until each group is in ----

const groups = [
  { mods: KERNEL, fn: hookKernel },
  { mods: GDI, fn: hookGdi },
  { mods: USER, fn: hookUser },
  { mods: ['ddraw.dll'], fn: hookDirectDraw },
];
function tryInstall() {
  for (const g of groups) {
    if (g.done || !g.mods.some((m) => Process.findModuleByName(m) !== null)) continue;
    g.done = true;
    try { g.fn(); } catch (e) { err('install', e); }
  }
}
try { hookExe(); } catch (e) { err('hookExe', e); }
tryInstall();
const poll = setInterval(() => { tryInstall(); if (groups.every((g) => g.done)) clearInterval(poll); }, 5);
setInterval(() => {
  if (lockCounts.size === 0) return;
  emit('lock_summary', { counts: Object.fromEntries(lockCounts) });
  lockCounts.clear();
}, 2000);
emit('module', { name: exe.name, base: hex(exe.base), size: exe.size });
