# drawdiff

Compares the original game's draw trace (Frida, `tools/frida/draw_agent.js`, README "Draw tracer") with the Open-SWR draw log
(`OPENSWR_DRAWLOG`, format in the header of `godot/DrawLog.cs`). Plain .NET, no Godot.

```
dotnet run --project tools/drawdiff -- <original.jsonl> <openswr.jsonl> [--window t0..t1] [--alias res=res]... [--out report.txt]
dotnet run --project tools/drawdiff -- --make-vector <in.jsonl> <out.jsonl> [--window t0..t1]
```

- `--window`: only original records from t0 to t1 ms after the trace's first record (state before the window is still read).
  The strategy screen of `draw-20261104-155731.jsonl` starts at about 36200.
- `--alias`: read an Open-SWR res id as an original one, e.g. `strategy.dll:2:900=strategy.dll:2:901` when the two runs played
  different sides.
- `--out`: write the report there and print only the summary.

## Model

Both logs become items: kind (bitmap / text / fill / line), res id, source rect, drawn size, key, screen position where known,
text hash, DrawText flags, font (`height/weight/face`), colour as decimal `red,green,blue` (`iN` = palette index, `none` = null brush / pen).
Open-SWR font indices map through `TextLayout` (font table) to LOGFONT height / weight; font −1 (Godot's own font) is
`?/?/godot`. Open-SWR art not from the install matches by its `src` tag (`src:…`), never present in the original; `ellipse`
(galaxy map circles) is an openswr-only kind.

Each side is reduced to unique items with counts (the original redraws much more often). Items are grouped by res id / text
hash / fill or line size, exact items matched, then the rest paired: same size first, fewest differing attributes, nearest.
Report classes per group: `missing` (original only), `extra` (Open-SWR only), `moved` (only the position differs, with delta),
`different` (attributes differ). An Open-SWR bitmap drawn at a size other than its source rect is marked `scaled`.

## Screen positions in the original trace

- A paint DC that gets the backbuffer (a 640×480 section) copied in place (x == sx, y == sy, by `bitblt` or `bm_draw`) is the
  top-level window at screen (0, 0), partial repaints included (Inferred: child windows never get it in place).
- A bitmap object drawn into the screen or into a placed object at one position only gets that origin (canvas → backbuffer →
  screen). `bm_draw` into a memory DC with a DIB section selected counts as drawing into that section's object.
- A canvas or section filled by an opaque blit of a region of a placed object is at that region (button canvases take their
  background from the backbuffer; Inferred from the blit shape).
- Resolved to a fixed point, so draws into a canvas before it is placed still resolve. Afterwards, a composite whose draws into
  placed targets disagree on its position is unplaced, with everything placed from it.

Not resolved (reported with position `(?)`, compared by size, colour and attributes only):

- GDI drawing into child-window DCs (bars, buttons): their screen position is not in the trace;
- objects drawn at several positions (sprite caches), and objects only drawn to child windows;
- text on `GetDC` window DCs.

Also: `bm_kdraw` is skipped (it runs `bm_kblit`, which is traced); DrawText with `DT_CALCRECT` draws nothing and is skipped;
`bm_fill` colours are palette indices, never mapped to RGB; non-install Open-SWR art (`res` null) is always `extra`.

## Vectors

`tests/OpenSWR.Tests/Vectors/draw/` holds numbers only: `--make-vector` keeps the records the diff reads, replaces every string
`s` by `sh` = length + first 4 bytes of SHA-256 (UTF-8) as a decimal number, and drops return addresses, `seq`, palettes. Both sides hash the same way.
With `--window`, the state alive at the window start comes first: live bitmap objects the window uses, their DCs with the last
selections and text colour, and the placement draws (composite blits, `bm_draw`, `bitblt` of a section's DC) those objects
depend on, each distinct one once; draws before the window that show something are dropped. Placement draws after the window
are not kept (shortcut: an object first placed after the window stays `(?)` in the vector; keep them too if a slice needs it).

```
drawdiff --make-vector "<traces dir>\draw-20261104-155731.jsonl" original-strategy.jsonl --window 50320..50420
drawdiff --make-vector drawlog-strategy.jsonl openswr-strategy.jsonl   # Open-SWR sample log, Alliance, strategy screen
```
