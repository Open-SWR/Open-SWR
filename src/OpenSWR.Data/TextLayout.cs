using System.Runtime.InteropServices;

namespace OpenSWR.Data;

/// <summary>
/// How DrawText places a text in its rect. Center / Right: horizontal alignment (else left). VCenter / Bottom: vertical
/// alignment, single line only (else top). WordBreak: wrap at the rect width (the only wrapping). SingleLine: line feeds do
/// not break. NoClip: draws past its rect. NoPrefix: "&" drawn literally; only the edit field and the text scroll field's
/// words pass it, every other DrawText site processes prefixes.
/// </summary>
public readonly record struct TextFormat
{
    public bool Center { get; init; }
    public bool Right { get; init; }
    public bool VCenter { get; init; }
    public bool Bottom { get; init; }
    public bool WordBreak { get; init; }
    public bool SingleLine { get; init; }
    public bool NoClip { get; init; }
    public bool NoPrefix { get; init; }

    /// <summary>Left, top, clipped, prefixes processed.</summary>
    public static readonly TextFormat TopLeft = new();
    /// <summary>Centred at the top.</summary>
    public static readonly TextFormat Centred = new() { Center = true };
    /// <summary>Wrapped at the rect width, left.</summary>
    public static readonly TextFormat Wrap = new() { WordBreak = true };
    /// <summary>Wrapped and centred.</summary>
    public static readonly TextFormat CentredWrap = new() { Center = true, WordBreak = true };
    /// <summary>One line, left, centred vertically: the list row default.</summary>
    public static readonly TextFormat Row = new() { SingleLine = true, VCenter = true };
    /// <summary>One line, centred both ways.</summary>
    public static readonly TextFormat CentredRow = new() { Center = true, SingleLine = true, VCenter = true };

    /// <summary>
    /// The Win32 DrawText format value (winuser.h DT_ constants summed), as the original passes it: the draw log writes it so
    /// it compares with traces of the original.
    /// </summary>
    public int Win32 => (Center ? 1 : 0) + (Right ? 2 : 0) + (VCenter ? 4 : 0) + (Bottom ? 8 : 0) + (WordBreak ? 16 : 0)
        + (SingleLine ? 32 : 0) + (NoClip ? 256 : 0) + (NoPrefix ? 2048 : 0);
}

/// <summary>
/// How a text item sizes its rect: a fixed width / height keeps the item's, else the text is measured; a clipped item is cut at
/// its rect, any other draws past it.
/// </summary>
public readonly record struct TextItemSize(bool FixedWidth = false, bool FixedHeight = false, bool Clipped = false)
{
    /// <summary>Fixed width and height, clipped.</summary>
    public static readonly TextItemSize FixedClipped = new(true, true, true);
    /// <summary>Fixed width and height, not clipped.</summary>
    public static readonly TextItemSize Fixed = new(true, true);
}

/// <summary>
/// Text metrics and layout of the original's GDI text, plain .NET so the rules are
/// testable: the font table, GDI advances, DrawText emulation (which glyphs land where, clipping), cell-list insets and the
/// text scroll field's own word wrap. Drawing itself stays in the Godot front end (`Assets`).
/// </summary>
public static class TextLayout
{
    /// <summary>
    /// DrawText prefix processing:"&&" -> "&"; a single "&" is not drawn and the next character is underlined; a single
    /// "&" as the last character draws nothing. The characters drawn, each with its underline flag.
    /// </summary>
    public static List<(char C, bool Underline)> Prefixed(string text)
    {
        var chars = new List<(char, bool)>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&') chars.Add((text[i], false));
            else if (++i < text.Length) chars.Add((text[i], text[i] != '&'));
        }
        return chars;
    }

    /// <summary>The text DrawText draws with prefix processing (<see cref="Prefixed"/> without the underlines).</summary>
    public static string Unprefixed(string text) => string.Concat(Prefixed(text).Select(c => c.C));

    /// <summary>
    /// Tooltip text (the Win32 tooltip control without TTS_NOPREFIX; Inferred comctl32 behaviour): every single "&"
    /// removed, "&&" -> "&", no underline. E.g. TEXTTACT 56781 "Maneuvers & Tactics" -> "Maneuvers  Tactics".
    /// </summary>
    public static string Tooltip(string text) => text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");

    /// <summary>Width DrawText measures (DT_CALCRECT): with prefix processing unless <paramref name="noPrefix"/>.</summary>
    public static int DrawnWidth(int font, string text, bool noPrefix = false) => TextWidth(font, noPrefix ? text : Unprefixed(text));

    // Font table: index -> lfHeight (cell height), lfWeight, the GDI ppem (em px),
    // tmHeight and ascent measured with GDI on Arial (Inferred: the 1998 Arial maps the same). One face, "Arial"; weight 900
    // (font 13) maps to Arial Bold with the bold advances, no synthetic emboldening.
    static readonly (int Height, int Weight, int Ppem, int Line, int Ascent)[] FontTable =
    [
        (16, 400, 13, 16, 13), (16, 700, 13, 16, 13), (14, 700, 11, 14, 11), (8, 400, 7, 8, 6), (16, 400, 13, 16, 13),
        (16, 700, 13, 16, 13), (18, 400, 16, 18, 15), (18, 700, 15, 18, 14), (12, 400, 9, 12, 9), (12, 400, 9, 12, 9),
        (14, 400, 11, 14, 11), (24, 400, 21, 24, 19), (30, 400, 25, 28, 23), (12, 900, 10, 12, 10),
        (16, 700, 13, 16, 13), // SystemFont: the stock "System" font (no table entry; metrics measured)
    ];

    /// <summary>
    /// Pseudo index of the Windows stock "System" font (GetStockObject(SYSTEM_FONT)): the default font of a fresh memory DC, used by
    /// edit fields without a font (finder name boxes): tmHeight 16, ascent 13, weight 700, "Coruscant" 65 px.
    /// </summary>
    public const int SystemFont = 14;

    /// <summary>Font size (em px) = the GDI ppem of the table index.</summary>
    public static int Ppem(int font) => FontTable[font].Ppem;
    /// <summary>Line height = GDI tmHeight.</summary>
    public static int LineHeight(int font) => FontTable[font].Line;
    /// <summary>Baseline below the line top = GDI tmAscent.</summary>
    public static int Ascent(int font) => FontTable[font].Ascent;
    /// <summary>LOGFONT height of the index (cell height, px).</summary>
    public static int Height(int font) => FontTable[font].Height;
    /// <summary>LOGFONT weight of the index (400 / 700 / 900).</summary>
    public static int Weight(int font) => FontTable[font].Weight;

    /// <summary>Advance where GDI gives none (not Windows, or a character outside U+0020-U+00FF); the front end sets its font's.</summary>
    public static Func<int, char, int>? Fallback { get; set; }

    static readonly Dictionary<int, (int[] Widths, int Average)?> Metrics = [];

    static (int[] Widths, int Average)? Gdi(int font)
    {
        lock (Metrics)
        {
            if (!Metrics.TryGetValue(font, out var m)) Metrics[font] = m = GdiText.Measure(FontTable[font].Height, FontTable[font].Weight, font == SystemFont);
            return m;
        }
    }

    /// <summary>
    /// Integer advance of <paramref name="c"/>: GDI's hinted width (GetCharWidth32 on the table's LOGFONT) for U+0020-U+00FF on
    /// Windows; else <see cref="Fallback"/> (Inferred: within a few px of GDI), else 0. No kerning (GDI DrawText does not kern).
    /// </summary>
    public static int Advance(int font, char c) =>
        c is >= ' ' and <= 'ÿ' && Gdi(font) is { } m ? m.Widths[c - ' '] : Fallback?.Invoke(font, c) ?? 0;

    /// <summary>GDI tmAveCharWidth (the text scroll field's tab step); elsewhere the advance of 'x' (Inferred).</summary>
    public static int AverageCharWidth(int font) => Gdi(font) is { } m ? m.Average : Advance(font, 'x');

    /// <summary>Width of one line (GetTextExtentPoint32).</summary>
    public static int TextWidth(int font, string text)
    {
        int w = 0;
        foreach (char c in text) w += Advance(font, c);
        return w;
    }

    /// <summary>
    /// The lines DrawText lays out: split at line feeds; with <paramref name="width"/> >= 0 (DT_WORDBREAK) each paragraph is broken
    /// between words where the next word would pass the width (a longer word stays whole on its line). Widths with prefix
    /// processing unless <paramref name="noPrefix"/> ; the lines keep their "&", processed per laid-out line:
    /// an unpaired "&" before the break space keeps that space on its line (it draws as an underlined space advance at the line end),
    /// nothing carries to the next line. Centred and right-aligned lines (<paramref name="keepBreak"/> false) never keep the break
    /// space: they are positioned on their width without it and that "&" underlines nothing (measured).
    /// </summary>
    public static List<string> Lines(int font, string text, int width = -1, bool noPrefix = false, bool keepBreak = true)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            if (width < 0)
            {
                lines.Add(paragraph);
                continue;
            }
            string line = "";
            foreach (var word in paragraph.Split(' '))
            {
                string next = line == "" ? word : line + " " + word;
                if (line != "" && DrawnWidth(font, next, noPrefix) > width)
                {
                    lines.Add(keepBreak && !noPrefix && (line.Length - line.TrimEnd('&').Length) % 2 == 1 ? line + " " : line);
                    line = word;
                }
                else line = next;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>One drawn glyph: left of its GDI cell, line top, the character, its advance, underlined (a prefix "&" before it).</summary>
    public readonly record struct Glyph(float X, float Y, char C, int Advance, bool Underline = false);

    /// <summary>
    /// DrawText emulation: the glyphs drawn for <paramref name="text"/> in the rect as <paramref name="format"/> places them
    /// ("&" prefixes processed unless NoPrefix). Lines are tmHeight apart; never ellipsized; clipped at the rect unless NoClip.
    /// Spaces draw nothing (an underlined one draws its underline). shortcut: the clip drops whole glyphs that pass the rect (GDI clips pixels); upgrade with a clip rect per call.
    /// </summary>
    public static List<Glyph> Glyphs(float left, float top, float width, float height, string text, int font, TextFormat format)
    {
        var glyphs = new List<Glyph>();
        int line = LineHeight(font);
        bool single = format.SingleLine, clip = !format.NoClip, noPrefix = format.NoPrefix;
        var lines = single ? [text.Replace("\r", "").Replace("\n", "")] : Lines(font, text, format.WordBreak ? (int)width : -1, noPrefix, !format.Center && !format.Right);
        float y = top, right = left + width;
        if (single && format.VCenter) y += (int)(height - line) / 2;
        else if (single && format.Bottom) y = top + height - line;
        foreach (var l in lines)
        {
            if (clip && y >= top + height) break;
            var chars = noPrefix ? l.Select(c => (c, false)).ToList() : Prefixed(l);
            int w = chars.Sum(c => Advance(font, c.Item1));
            float x = format.Center ? left + (int)(width - w) / 2 : format.Right ? right - w : left;
            foreach (var (c, u) in chars)
            {
                int a = Advance(font, c);
                if ((c != ' ' || u) && (!clip || x >= left && x + a <= right)) glyphs.Add(new(x, y, c, a, u));
                x += a;
            }
            y += line;
        }
        return glyphs;
    }

    /// <summary>
    /// Rect and format a text item paints with: a non-fixed dimension is the measured text extent (DT_CALCRECT, top-left
    /// anchored); centring on a non-fixed-width item is dropped for good and the rect widened by 1 px each side (text drawn
    /// left-aligned from x - 1); an item that is not clipped draws with NoClip, so only fixed-size clipped items can be cut.
    /// </summary>
    public static (float Left, float Top, float Width, float Height, TextFormat Format) TextItem(float left, float top, float width, float height,
        string text, int font, TextFormat format, TextItemSize size)
    {
        bool single = format.SingleLine, noPrefix = format.NoPrefix;
        var lines = single ? [text.Replace("\r", "").Replace("\n", "")] : Lines(font, text, format.WordBreak && size.FixedWidth ? (int)width : -1, noPrefix);
        if (!size.FixedWidth)
        {
            width = lines.Count == 0 ? 0 : lines.Max(l => DrawnWidth(font, l, noPrefix));
            if (format.Center)
            {
                format = format with { Center = false };
                left -= 1;
                width += 2;
            }
        }
        if (!size.FixedHeight) height = lines.Count * LineHeight(font);
        if (!size.Clipped) format = format with { NoClip = true };
        return (left, top, width, height, format);
    }

    /// <summary>
    /// Text rect and format of a cell-list item: the item rect moved by the list's text
    /// offset (1 in x by default), inset by the horizontal alignment (centred: left + 2 and right - 2; right: left + 3; left: right - 3);
    /// clipped, except a word-break format, which gets NoClip. <paramref name="offset"/> = the list's text offset x (1 from the list ctor;
    /// a drop-down pop-up takes the box's offset).
    /// </summary>
    public static (float Left, float Top, float Width, float Height, TextFormat Format) CellText(float left, float top, float width, float height, TextFormat format, float offset = 1)
    {
        left += offset;
        (float l, float r) = (format.Center, format.Right) switch { (true, false) => (2f, 2f), (false, true) => (3f, 0f), _ => (0f, 3f) };
        return (left + l, top, width - l - r, height, format.WordBreak ? format with { NoClip = true } : format);
    }

    /// <summary>
    /// A text scroll field's layout: words at their x per line, each line's first character index (<see cref="Starts"/>: a
    /// scroll-down step makes <c>Starts[1]</c> the new top), whether the text scrolls, the wrap width and the whole lines that fit.
    /// </summary>
    public sealed record ScrollLayout(List<List<(int X, string Word)>> Lines, List<int> Starts, bool Scrolls, int Width, int Visible);

    /// <summary>
    /// Text scroll field: its own word wrap with TextOut, left margin 0, laid out
    /// from the character index <paramref name="start"/> (the scroll position). A word moves x by its width; a space by the
    /// space width and breaks the line when x then passes the width; a tab sets x := x + 1 + (t - (x + 1) mod t), t = 4 *
    /// tmAveCharWidth, and breaks likewise; a line feed breaks. A word that does not fit in the rest of the line goes to the next;
    /// a word wider than the whole line is shortened one character at a time until it fits (at least 1 character), then the line
    /// always breaks (an over-long word at a line start leaves that line empty), the piece goes on the new line and the rest
    /// continues as a new word. A new line starts at the word that did not fit (or after the space / tab / line feed). Lines are
    /// tmHeight apart; only whole lines are drawn (y + tmHeight <= height). Scroll test: the whole text measured
    /// with DT_WORDBREAK at the full width; the width loses <paramref name="bar"/> only when it scrolls.
    /// </summary>
    public static ScrollLayout ScrollField(int font, string text, int width, int height, int bar, int start = 0)
    {
        int h = LineHeight(font), space = Advance(font, ' '), t = Math.Max(1, 4 * AverageCharWidth(font));
        text = text.Replace("\r", "");
        bool scrolls = Lines(font, text, width).Count * h > height;
        int w = scrolls ? width - bar : width;
        var lines = new List<List<(int, string)>> { new() };
        var starts = new List<int> { start };
        int x = 0;
        void Break(int at)
        {
            lines.Add([]);
            starts.Add(at);
            x = 0;
        }
        for (int i = Math.Clamp(start, 0, text.Length); i < text.Length;)
        {
            switch (text[i])
            {
                case '\n':
                    Break(++i);
                    continue;
                case ' ':
                    x += space;
                    if (x > w) Break(i + 1);
                    i++;
                    continue;
                case '\t':
                    x += 1 + (t - (x + 1) % t);
                    if (x > w) Break(i + 1);
                    i++;
                    continue;
            }
            int j = i;
            while (j < text.Length && text[j] is not (' ' or '\t' or '\n')) j++;
            while (i < j)
            {
                string word = text[i..j];
                int width0 = TextWidth(font, word);
                if (x + width0 <= w)
                {
                    lines[^1].Add((x, word));
                    x += width0;
                    i = j;
                    break;
                }
                if (width0 <= w)
                {
                    Break(i);
                    continue;
                }
                string piece = word;
                while (piece.Length > 1 && TextWidth(font, piece) > w) piece = piece[..^1];
                Break(i);
                lines[^1].Add((x, piece));
                x += TextWidth(font, piece);
                i += piece.Length;
            }
        }
        return new(lines, starts, scrolls, w, height / h);
    }

    /// <summary>
    /// Text scroll field bar: thumb position = the height DrawText measures over the first <paramref name="start"/> characters
    /// (the scroll position) at the field's wrap width <paramref name="width"/>, range = the whole text's height; the page is the
    /// field height. Measured with <see cref="Lines"/>. Measured against GDI (TextLayoutTests): no characters measure 0; trailing
    /// blanks and one trailing line feed of the measured text open no line of their own ("a\n" = 1 line, "a\n\n" = 2), so the
    /// position is the top line's y.
    /// </summary>
    public static (int Position, int Range) ScrollRange(int font, string text, int width, int start)
    {
        text = text.Replace("\r", "");
        int h = LineHeight(font);
        string head = text[..Math.Clamp(start, 0, text.Length)].TrimEnd(' ', '\t');
        if (head.EndsWith('\n')) head = head[..^1];
        return (head.Length == 0 ? 0 : Lines(font, head, width).Count * h, Lines(font, text, width).Count * h);
    }

    /// <summary>
    /// Thumb position of a field of <paramref name="height"/>: <see cref="ScrollRange"/> clamped to range - page, as a Win32 scroll
    /// bar clamps its position.
    /// </summary>
    public static int ThumbPosition(int font, string text, int width, int height, int start)
    {
        var (position, range) = ScrollRange(font, text, width, start);
        return Math.Min(position, Math.Max(0, range - height));
    }

    /// <summary>
    /// Drag / page scroll of a text scroll field (field width <paramref name="width"/>, <paramref name="height"/>, scroll bar
    /// <paramref name="bar"/>): one-line steps (down pushes the old top on <paramref name="tops"/> and takes the next line's start
    /// while lines remain below the shown ones; up pops) until the thumb position reaches <paramref name="target"/>; a step that
    /// lands past it stays; the ends and a step that does not advance stop it.
    /// </summary>
    public static void ScrollTo(int font, string text, int width, int height, int bar, int target, ref int top, Stack<int> tops)
    {
        int Position(int at) => ThumbPosition(font, text, ScrollField(font, text, width, height, bar, at).Width, height, at);
        while (Position(top) > target && tops.Count > 0) top = tops.Pop();
        while (Position(top) < target)
        {
            var layout = ScrollField(font, text, width, height, bar, top);
            if (layout.Lines.Count <= layout.Visible || layout.Starts.Count < 2 || layout.Starts[1] == top) return;
            tops.Push(top);
            top = layout.Starts[1];
        }
    }

    /// <summary>
    /// Edit field horizontal scroll rule (`CoolStringField`), run after every edit, caret move,
    /// selection change and text set: <paramref name="x"/> = width of the text before the caret, <paramref name="scroll"/> = s
    /// the scroll value, <paramref name="width"/> = control width, e = 1. X - s &gt; w - e -> s := X + e - w (caret at x = w - 1); X - s &lt; 0 ->
    /// s := X (caret at x 0); else s unchanged (the view does not move back while the caret stays visible). The caret sits at X - s.
    /// </summary>
    public static int FieldScroll(int scroll, int x, int width) => x - scroll > width - 1 ? x + 1 - width : x - scroll < 0 ? x : scroll;

    /// <summary>
    /// Edit field hit test: <paramref name="x"/> = mouse x + s; the caret index = (smallest n >= 1 with
    /// width(first n characters) >= x) - 1, the length when x is past the end, 0 when x <= 0: before the character under the pointer.
    /// Widths are the prefix measured on one line without prefix processing = <see cref="TextWidth"/>.
    /// </summary>
    public static int FieldHit(int font, string text, float x)
    {
        if (x <= 0) return 0;
        for (int n = 1; n <= text.Length; n++)
            if (TextWidth(font, text[..n]) >= x) return n - 1;
        return text.Length;
    }

    /// <summary>GDI text metrics of this machine's Arial (Windows only; null elsewhere or on failure).</summary>
    static class GdiText
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
            uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int i);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool GetCharWidth32W(IntPtr dc, uint first, uint last, [Out] int[] widths);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextMetricsW(IntPtr dc, out TextMetric metrics);

        // TEXTMETRICW
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TextMetric
        {
            public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang,
                DigitizedAspectX, DigitizedAspectY;
            public char FirstChar, LastChar, DefaultChar, BreakChar;
            public byte Italic, Underlined, StruckOut, PitchAndFamily, FontCharSet;
        }

        const int StockSystemFont = 13, ProofQuality = 2;

        /// <summary>
        /// Widths of U+0020-U+00FF and tmAveCharWidth for the LOGFONT of the font table (positive height, ANSI charset, PROOF quality),
        /// or of the stock SYSTEM_FONT when <paramref name="stock"/>.
        /// </summary>
        public static (int[], int)? Measure(int height, int weight, bool stock = false)
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero), font = stock ? GetStockObject(StockSystemFont) : CreateFontW(height, 0, 0, 0, weight, 0, 0, 0, 0, 0, 0, ProofQuality, 0, "Arial");
                var old = SelectObject(dc, font);
                var w = new int['ÿ' - ' ' + 1];
                TextMetric tm = default;
                bool ok = GetCharWidth32W(dc, ' ', 'ÿ', w) && GetTextMetricsW(dc, out tm);
                SelectObject(dc, old);
                if (!stock) DeleteObject(font);
                DeleteDC(dc);
                return ok ? (w, tm.AveCharWidth) : null;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return null; }
        }
    }
}
