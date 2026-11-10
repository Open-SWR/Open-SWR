using System.Runtime.InteropServices;
using OpenSWR.Data;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>GDI metrics, cell-list clipping, the text scroll field. GDI widths: Windows only.</summary>
public class TextLayoutTests(ITestOutputHelper output)
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
        uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(IntPtr dc, string text, int count, ref Rect rect, uint format);
    struct Rect { public int Left, Top, Right, Bottom; }

    // Win32 DrawText format values (winuser.h), summed into the one format the text scroll field measures with
    const uint DtWordBreak = 16, DtCalcRect = 1024, DtEditControl = 8192;
    const uint ProofQuality = 2;

    /// <summary>Height real GDI DrawText measures with the text scroll field's format (edit control, calc rect, word break) in the LOGFONT of TextLayout.Gdi.</summary>
    static int DrawTextHeight(int font, string text, int width)
    {
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero), f = CreateFontW(TextLayout.Height(font), 0, 0, 0, TextLayout.Weight(font), 0, 0, 0, 0, 0, 0, ProofQuality, 0, "Arial");
        var old = SelectObject(dc, f);
        var r = new Rect { Right = width };
        DrawTextW(dc, text, text.Length, ref r, DtEditControl + DtCalcRect + DtWordBreak);
        SelectObject(dc, old);
        DeleteObject(f);
        DeleteDC(dc);
        return r.Bottom;
    }

    const string Pangram = "The quick brown fox jumps over the lazy dog";

    // Font 13 (weight 900) is Arial Bold at lfHeight 12: pangram 220 px, as weight 700.
    [Fact]
    public void Font13IsTheBoldFace()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(220, TextLayout.TextWidth(13, Pangram));
        Assert.Equal(218, TextLayout.TextWidth(10, Pangram));
        Assert.Equal(286, TextLayout.TextWidth(5, Pangram));
        Assert.Equal(120, TextLayout.TextWidth(10, "Best Time To Completion:"));
    }

    // A cell text wider than its cell is cut at the inset edge. A word-break cell text is not clipped.
    [Fact]
    public void CellTextClipsAtTheInset()
    {
        if (!OperatingSystem.IsWindows()) return;
        string text = string.Concat(Enumerable.Repeat("W", 20));
        var (x, y, w, h, flags) = TextLayout.CellText(10, 0, 100, 16, TextFormat.Row); // left: text offset + 1, right - 3
        Assert.Equal((11f, 97f), (x, w));
        var glyphs = TextLayout.Glyphs(x, y, w, h, text, 10, flags);
        Assert.InRange(glyphs.Count, 1, text.Length - 1);
        Assert.True(glyphs[^1].X + glyphs[^1].Advance <= 108);
        Assert.True(glyphs[^1].X + 2 * glyphs[^1].Advance > 108);
        Assert.Equal((13f, 96f), TextLayout.CellText(10, 0, 100, 16, TextFormat.Centred) is var c ? (c.Left, c.Width) : default); // centred: +/- 2
        Assert.Equal((14f, 97f), TextLayout.CellText(10, 0, 100, 16, new TextFormat { Right = true }) is var r ? (r.Left, r.Width) : default); // right: left + 3

        var wrap = TextLayout.CellText(10, 0, 100, 16, TextFormat.Wrap);
        Assert.True(wrap.Format.NoClip);
        Assert.Equal(text.Length, TextLayout.Glyphs(wrap.Left, wrap.Top, wrap.Width, wrap.Height, text, 10, wrap.Format).Count);
    }

    // A text one line taller than k whole lines shows k lines and scrolls. The width loses the bar only then.
    [Fact]
    public void ScrollFieldShowsWholeLines()
    {
        if (!OperatingSystem.IsWindows()) return;
        int h = TextLayout.LineHeight(10), k = 3, height = k * h + h - 1;
        var fits = TextLayout.ScrollField(10, "a\nb\nc", 200, height, 16);
        Assert.False(fits.Scrolls);
        Assert.Equal((200, k, 3), (fits.Width, fits.Visible, fits.Lines.Count));
        var more = TextLayout.ScrollField(10, "a\nb\nc\nd", 200, height, 16);
        Assert.True(more.Scrolls);
        Assert.Equal((184, k, 4), (more.Width, more.Visible, more.Lines.Count));
    }

    // Words start at x = 0. Space width, the tab rule (t = 4 * tmAveCharWidth), wrap, and over-long words split.
    [Fact]
    public void ScrollFieldWraps()
    {
        if (!OperatingSystem.IsWindows()) return;
        int a = TextLayout.TextWidth(10, "ab"), space = TextLayout.Advance(10, ' '), t = 4 * TextLayout.AverageCharWidth(10);
        var line = TextLayout.ScrollField(10, "ab ab\tab", 400, 100, 16).Lines[0];
        int tab = 2 * a + space;
        tab += 1 + (t - (tab + 1) % t);
        Assert.Equal([(0, "ab"), (a + space, "ab"), (tab, "ab")], line);

        var wrapped = TextLayout.ScrollField(10, "ab ab ab", 2 * a + space + 1, 100, 16).Lines;
        Assert.Equal([[(0, "ab"), (a + space, "ab")], [(0, "ab")]], wrapped);

        // An over-long word breaks the line even at a line start (that line stays empty). The rest continues.
        var split = TextLayout.ScrollField(10, "WWWWWWWWWW", TextLayout.TextWidth(10, "WWWW"), 100, 16);
        Assert.Empty(split.Lines[0]);
        Assert.Equal(["WWWW", "WWWW", "WW"], split.Lines.Skip(1).Select(l => Assert.Single(l).Word));
        Assert.Equal([0, 0, 4, 8], split.Starts);
    }

    // A text that fits 2 lines at full width shows no bar. An over-long first word starts on line 2 and its rest continues
    // on line 3. Scrolling is by the character index of the top line.
    [Fact]
    public void ScrollFieldScrollsByCharacters()
    {
        if (!OperatingSystem.IsWindows()) return;
        int h = TextLayout.LineHeight(10), w = TextLayout.TextWidth(10, "ab ab") + 1;
        var fits = TextLayout.ScrollField(10, "ab ab ab ab", w, 2 * h, 16);
        Assert.False(fits.Scrolls);
        Assert.Equal((w, 2), (fits.Width, fits.Lines.Count));

        string text = new string('W', 30) + " ab";
        var over = TextLayout.ScrollField(10, text, 200, 10 * h, 16);
        Assert.Empty(over.Lines[0]);
        string piece = Assert.Single(over.Lines[1]).Word;
        Assert.True(TextLayout.TextWidth(10, piece) <= 200 && TextLayout.TextWidth(10, piece + "W") > 200);
        Assert.StartsWith("W", over.Lines[2][0].Word);
        Assert.Equal(30, over.Lines.Skip(1).SelectMany(l => l).Where(p => p.Word.StartsWith('W')).Sum(p => p.Word.Length));
        // scrolling down from an over-long first word does not advance (the new top is the word that did not fit)
        Assert.Equal(0, over.Starts[1]);

        var lines = TextLayout.ScrollField(10, "a\nbb\nc", 200, 10 * h, 16);
        Assert.Equal([0, 2, 5], lines.Starts);
        var scrolled = TextLayout.ScrollField(10, "a\nbb\nc", 200, 10 * h, 16, start: lines.Starts[1]);
        Assert.Equal([(0, "bb")], scrolled.Lines[0]);
    }

    // The thumb position is the DrawText height of the characters above the top line at the wrap width, the range the whole
    // text's height. Checked against real GDI DrawText for the prefixes a scroll step leaves.
    [Fact]
    public void ScrollRangeMeasuresTheTopCharacters()
    {
        if (!OperatingSystem.IsWindows()) return;
        int h = TextLayout.LineHeight(10), w = TextLayout.TextWidth(10, "ab ab") + 1;
        foreach (var (text, start) in new[] { ("a\nbb\nc", 0), ("a\nbb\nc", 2), ("a\nbb\nc", 5), ("ab ab ab ab", 6), ("a\n\nb", 2), ("a\n\nb", 3) }) // no tab: Lines measures it unlike GDI
        {
            int gdi = DrawTextHeight(10, text[..start], w), whole = DrawTextHeight(10, text, w);
            output.WriteLine($"{text.Replace("\n", "\\n").Replace("\t", "\\t")} [..{start}]: GDI {gdi} / {whole}, h {h}");
            Assert.Equal((gdi, whole), TextLayout.ScrollRange(10, text, w, start));
        }
        Assert.Equal(6, TextLayout.ScrollField(10, "ab ab ab ab", w, h, 0).Starts[1]); // start 6 above = the step from a wrapped top line
    }

    // A drag or page scroll steps line by line (push / pop of the top) until the thumb position reaches the target. A step
    // that lands past it stays; the ends stop it.
    [Fact]
    public void ScrollToStepsThroughTheStarts()
    {
        if (!OperatingSystem.IsWindows()) return;
        int h = TextLayout.LineHeight(10);
        const string text = "a\nb\nc\nd\ne";
        var tops = new Stack<int>();
        int top = 0;
        void To(int target) => TextLayout.ScrollTo(10, text, 200, 2 * h, 16, target, ref top, tops);
        To(1000);
        Assert.Equal(6, top); // the last top shows "d" / "e": range 5h - page 2h = 3h
        Assert.Equal([4, 2, 0], tops);
        To(0);
        Assert.Equal((0, 0), (top, tops.Count));
        To(2 * h);
        Assert.Equal((4, 2), (top, tops.Count));
        To(h + 1); // between two steps: stepping up to h would fall short, the position past it stays
        Assert.Equal(4, top);
        To(h);
        Assert.Equal(2, top);
    }

    // A non-fixed format-1 item loses DT_CENTER (drawn from x - 1). Only fixed bit-4 items are cut.
    [Fact]
    public void TextItemsAreSizedByTheirText()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = TextLayout.TextItem(26, 9, 97, 9, "Red Group", 8, TextFormat.Centred, new TextItemSize(Clipped: true));
        var g = TextLayout.Glyphs(name.Left, name.Top, name.Width, name.Height, "Red Group", 8, name.Format);
        Assert.Equal((25f, 9f), (g[0].X, g[0].Y));
        Assert.Equal(TextLayout.TextWidth(8, "Red Group") + 2, name.Width);
        Assert.Equal(TextLayout.LineHeight(8), name.Height);

        var ship = TextLayout.TextItem(26, 7, 97, 9, "Imperial Star Destroyer", 8, TextFormat.TopLeft, new TextItemSize(Clipped: true));
        Assert.Equal((26f, 7f), (ship.Left, ship.Top));
        Assert.Equal("Imperial Star Destroyer".Count(c => c != ' '), TextLayout.Glyphs(ship.Left, ship.Top, ship.Width, ship.Height, "Imperial Star Destroyer", 8, ship.Format).Count);

        string fleet = string.Concat(Enumerable.Repeat("W", 30));
        var cut = TextLayout.TextItem(100, 29, 128, 25, fleet, 5, TextFormat.TopLeft, TextItemSize.FixedClipped);
        var glyphs = TextLayout.Glyphs(cut.Left, cut.Top, cut.Width, cut.Height, fleet, 5, cut.Format);
        Assert.InRange(glyphs.Count, 1, 29);
        Assert.True(glyphs[^1].X + glyphs[^1].Advance <= 228);
        var whole = TextLayout.TextItem(100, 29, 128, 25, fleet, 5, TextFormat.TopLeft, TextItemSize.Fixed);
        Assert.Equal(30, TextLayout.Glyphs(whole.Left, whole.Top, whole.Width, whole.Height, fleet, 5, whole.Format).Count);
    }

    // The finder name box draws with the stock System font: "Coruscant" is 65 px, cell 16, ascent 13.
    [Fact]
    public void SystemFontMetrics()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(65, TextLayout.TextWidth(TextLayout.SystemFont, "Coruscant"));
        Assert.Equal((16, 13), (TextLayout.LineHeight(TextLayout.SystemFont), TextLayout.Ascent(TextLayout.SystemFont)));
    }

    // DT_WORDBREAK lines break on processed widths, then "&" is processed per laid-out line. An "&" before the break space
    // underlines that space's advance at the line end. Before a line feed nothing is underlined. Nothing carries over.
    [Fact]
    public void PrefixPerLine()
    {
        if (!OperatingSystem.IsWindows()) return;
        const int f = TextLayout.SystemFont;
        var wrapped = TextLayout.Glyphs(0, 0, 25, 32, "AB& CD", f, TextFormat.Wrap);
        Assert.Equal([('A', 0, false), ('B', 0, false), (' ', 0, true), ('C', 16, false), ('D', 16, false)], wrapped.Select(g => (g.C, (int)g.Y, g.Underline)));
        var space = wrapped.Single(g => g.Underline);
        Assert.Equal((TextLayout.TextWidth(f, "AB"), TextLayout.Advance(f, ' ')), ((int)space.X, space.Advance));

        var second = TextLayout.Glyphs(0, 0, 25, 32, "AB &CD", f, TextFormat.Wrap);
        Assert.Equal(('C', 16), second.Where(g => g.Underline).Select(g => (g.C, (int)g.Y)).Single());

        var feed = TextLayout.Glyphs(0, 0, 100, 32, "AB&\nCD", f, TextFormat.TopLeft);
        Assert.Equal("ABCD", string.Concat(feed.Select(g => g.C)));
        Assert.DoesNotContain(feed, g => g.Underline);

        var word = TextLayout.Glyphs(0, 0, TextLayout.TextWidth(f, "ABCD") + 1, 32, "AB&CD EF", f, TextFormat.Wrap); // "&" is no break opportunity
        Assert.Equal(('C', 0), word.Where(g => g.Underline).Select(g => (g.C, (int)g.Y)).Single());
    }

    // Centred / right-aligned word-break lines are positioned on their width without the break space (further trailing
    // spaces count). An "&" before the break space underlines nothing there.
    [Fact]
    public void CentredLinesDropTheBreakSpace()
    {
        if (!OperatingSystem.IsWindows()) return;
        const int f = TextLayout.SystemFont;
        float First(string text, int w, TextFormat format) => TextLayout.Glyphs(0, 0, w, 32, text, f, format).First().X;
        Assert.Equal(3, First("AB", 25, TextFormat.CentredWrap));
        Assert.Equal(3, First("AB CD", 25, TextFormat.CentredWrap));
        Assert.Equal(9, First("AB  CD", 41, TextFormat.CentredWrap));
        var amp = TextLayout.Glyphs(0, 0, 25, 32, "AB& CD", f, TextFormat.CentredWrap);
        Assert.Equal(3, amp.First().X);
        Assert.DoesNotContain(amp, g => g.Underline);
        var right = TextLayout.Glyphs(0, 0, 30, 32, "AB& CD", f, new TextFormat { Right = true, WordBreak = true });
        Assert.Equal(12, right.First().X);
        Assert.DoesNotContain(right, g => g.Underline);
        Assert.Equal(('C', 16), TextLayout.Glyphs(0, 0, 25, 32, "AB &CD", f, TextFormat.CentredWrap).Where(g => g.Underline).Select(g => (g.C, (int)g.Y)).Single());
    }

    // Popup-menu and picker rows have no font of their own, so they use the System font: a "Coruscant" row's text is 65 px
    // (the GameMenu.MenuLayout width term, Godot side), and the row height stays 16 + 4. The filter title is a System-font
    // text item, centred and unclipped, over the map rect width at y 40: side 1 x 55 ... 540, side 2 x 120 ... 600.
    [Fact]
    public void MenuRowsAndFilterTitleInSystemFont()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(65, TextLayout.DrawnWidth(TextLayout.SystemFont, "Coruscant"));
        Assert.Equal(TextLayout.LineHeight(0), TextLayout.LineHeight(TextLayout.SystemFont));
        var a = OpenSWR.Sim.MapFilter.Title(true, "Support");
        Assert.Equal((55f, 540f, 40f, 16f, TextFormat.Centred with { NoClip = true }), (a.Left, a.Left + a.Width, a.Top, a.Height, a.Format));
        var e = OpenSWR.Sim.MapFilter.Title(false, "Support");
        Assert.Equal((120f, 600f), (e.Left, e.Left + e.Width));
        var glyphs = TextLayout.Glyphs(a.Left, a.Top, a.Width, a.Height, "Support", TextLayout.SystemFont, a.Format);
        Assert.Equal(55 + (485 - TextLayout.TextWidth(TextLayout.SystemFont, "Support")) / 2, (int)glyphs[0].X); // centred
    }

    // Pop-up rows: centred text within cell width - 4 (box offset 0, Inferred). A battle-start unit row's text
    // (item offset 70, left) starts at cell x + 71.
    [Fact]
    public void CellTextOffsets()
    {
        var popup = TextLayout.CellText(0, 0, 195, 63, new TextFormat { Center = true, NoClip = true }, offset: 0);
        Assert.Equal((2f, 191f), (popup.Left, popup.Width));
        Assert.Equal(71f, TextLayout.CellText(70, 0, 256 - 70, 16, TextFormat.TopLeft).Left);
    }

    // Message rows: index 0 is transparent. The bar's shadow strip and corners are not drawn, and neither is the icon's black.
    // On a selected row the 16-high icon's bottom row is index 252 = (0, 0, 255).
    [GameFact]
    public void MessageRowPictures()
    {
        var strategy = PeResources.Load(GameDir.File("STRATEGY.DLL"));
        IndexedBitmap B(ushort id) => IndexedBitmap.FromDib(strategy.Find(ResId.Bitmap, id)!.Data.Span);
        var icon = B(10906);
        var selected = MessageRowPicture.Build(icon, B(10914));
        Assert.Equal((356, 21), (selected.Width, selected.Height));
        Assert.Equal(0, selected.Pixels[18 * 356 + 353]);
        Assert.Equal(252, selected.Pixels[16 * 356 + 5]);
        Assert.Equal(new byte[] { 0, 0, 255 }, selected.Palette[(252 * 3)..(252 * 3 + 3)]);
        int black = Enumerable.Range(0, icon.Pixels.Length).First(i => icon.Pixels[i] == 0);
        Assert.Equal(0, selected.Pixels[(1 + black / icon.Width) * 356 + 1 + black % icon.Width]);
        var normal = MessageRowPicture.Build(icon, null);
        Assert.Equal((icon.Width, icon.Height), (normal.Width, normal.Height));
        Assert.DoesNotContain(normal.Pixels, p => p == 252);
    }

    // Typing past the box keeps the caret at x = w - 1. Home brings s back to 0. The view does not move back while the caret
    // stays visible.
    [Fact]
    public void FieldScrollRule()
    {
        const int w = 250;
        int s = 0, x = 0;
        for (int i = 0; i < 40; i++)
        {
            x += 9;
            s = TextLayout.FieldScroll(s, x, w);
            Assert.InRange(x - s, 0, w - 1);
        }
        Assert.Equal(w - 1, x - s);
        int back = TextLayout.FieldScroll(s, x - 20, w);
        Assert.Equal(s, back); // caret still visible: s unchanged
        Assert.Equal(0, TextLayout.FieldScroll(back, 0, w)); // Home
    }

    // Hit test: a click on the middle of a glyph puts the caret before it. Past the end gives length; <= 0 gives 0.
    [Fact]
    public void FieldHitBeforeTheGlyph()
    {
        if (!OperatingSystem.IsWindows()) return;
        const int font = TextLayout.SystemFont;
        string text = "Coruscant";
        int c = TextLayout.TextWidth(font, "Co"), r = TextLayout.Advance(font, 'r');
        Assert.Equal(2, TextLayout.FieldHit(font, text, c + r / 2f)); // middle of 'r' gives before it
        Assert.Equal(2, TextLayout.FieldHit(font, text, c + r)); // its right edge still before it
        Assert.Equal(3, TextLayout.FieldHit(font, text, c + r + 1));
        Assert.Equal(text.Length, TextLayout.FieldHit(font, text, 500));
        Assert.Equal(0, TextLayout.FieldHit(font, text, 0));
    }

    // Pop-up height = row height * rows + 4, below the box (production 195 x 144, rows 70 apart; mission 200 x 117). The list is
    // at (2, 2), cells (w - 4) x row height. The pop-up opens above the box when it would pass the bottom bound, and is
    // clamped at the top.
    [Fact]
    public void DropDownPopup()
    {
        var p = DropDownLayout.Of(100, 50, 195, 63, 70, 2, 0, 480);
        Assert.Equal((100f, 113f, 195f, 144f), (p.X, p.Y, p.Width, p.Height));
        Assert.Equal((2f, 2f, 191f, 140f, 191f, 70f), (p.ListX, p.ListY, p.ListWidth, p.ListHeight, p.CellWidth, p.CellHeight));
        var m = DropDownLayout.Of(0, 0, 200, 113, 113, 1, 0, 480);
        Assert.Equal((200f, 117f, 196f, 113f), (m.Width, m.Height, m.CellWidth, m.CellHeight));
        var above = DropDownLayout.Of(0, 400, 195, 63, 70, 2, 0, 480);
        Assert.Equal((256f, 144f), (above.Y, above.Height));
        var clamped = DropDownLayout.Of(0, 100, 195, 63, 70, 2, 0, 200);
        Assert.Equal((0f, 100f), (clamped.Y, clamped.Height));
        // Bounds: top 0, bottom 461 (Inferred). A pop-up ending below 461 opens above. With too little room it is clamped at 0,
        // and its height is the box y.
        var flip = DropDownLayout.Of(10, 320, 195, 63, 70, 2, 0, 461);
        Assert.Equal((10f, 176f, 144f), (flip.X, flip.Y, flip.Height));
        Assert.Equal(113f, DropDownLayout.Of(10, 50, 195, 63, 70, 2, 0, 461).Y); // 113 + 144 = 257 <= 461: below
        var top = DropDownLayout.Of(10, 50, 195, 350, 70, 2, 0, 461);
        Assert.Equal((0f, 50f), (top.Y, top.Height));
    }

    // The finder name box (System font, 250 px): typing past the width keeps the caret at x = w - 1. Home brings s back to 0.
    // A click on the middle of a glyph puts the caret before it. Shift+Right grows the selection. Typing replaces it.
    [Fact]
    public void NameBoxEditing()
    {
        if (!OperatingSystem.IsWindows()) return;
        var f = new StringField(TextLayout.SystemFont, 250);
        foreach (char c in "Coruscant Bothawui Corellia Kuat Fondor") f.Type(c);
        Assert.True(f.X(f.Text.Length) > 250);
        Assert.Equal(249, f.CaretX);
        f.Key(StringField.Keys.Left, false);
        Assert.Equal(249 - TextLayout.Advance(TextLayout.SystemFont, 'r'), f.CaretX); // the view stays
        f.Key(StringField.Keys.Home, false);
        Assert.Equal((0, 0, 0), (f.Scroll, f.Caret, f.CaretX));
        f.MouseDown(TextLayout.TextWidth(TextLayout.SystemFont, "Co") + TextLayout.Advance(TextLayout.SystemFont, 'r') / 2f);
        f.MouseUp();
        Assert.Equal(2, f.Caret);
        f.Key(StringField.Keys.Right, true);
        f.Key(StringField.Keys.Right, true);
        Assert.Equal((2, 4), f.Selection);
        f.Type('x');
        Assert.StartsWith("Coxscant", f.Text);
        Assert.False(f.HasSelection);
        f.SetText("Coruscant Bothawui Corellia Kuat Fondor");
        Assert.Equal(249, f.CaretX); // a set text scrolls to its end
    }

    // The edit model as coded: caret -1 after a set text. Shift rules without an anchor. The text-edit message follows the tests below.
    // Double click uses isspace union ispunct separators and the trailing separator. Overwrite applies only for 0 < caret < length.
    [Fact]
    public void NameBoxEditModel()
    {
        if (!OperatingSystem.IsWindows()) return;
        int font = TextLayout.SystemFont;
        var f = new StringField(font, 250);
        f.SetText("Kuat");
        Assert.Equal((-1, f.X(4)), (f.Caret, f.CaretX)); // caret -1 = the end
        f.Key(StringField.Keys.Home, false);
        f.Key(StringField.Keys.Left, true); // caret 0 stays 0: the first character is selected
        Assert.Equal((0, 1, 0), (f.SelectionBegin, f.SelectionEnd, f.Caret));
        f.Key(StringField.Keys.End, false);
        Assert.False(f.HasSelection);
        Assert.True(f.Key(StringField.Keys.Delete, false)); // caret = length: posts the text-edit message, deletes nothing
        Assert.Equal("Kuat", f.Text);
        f.Key(StringField.Keys.Home, false);
        Assert.False(f.Key(StringField.Keys.Backspace, false)); // caret 0: nothing, no message
        f.Key(StringField.Keys.Insert, false);
        f.Type('X'); // overwrite at caret 0 inserts
        Assert.Equal("XKuat", f.Text);
        f.Type('Y'); // 0 < caret < length: replaces 'K'
        Assert.Equal("XYuat", f.Text);
        Assert.False(f.Type(''));

        f.Clear();
        Assert.True(f.Key(StringField.Keys.Backspace, false)); // caret -1 on an empty text still posts
        Assert.False(f.Key(StringField.Keys.Delete, false)); // caret -1: nothing
        f.SetText("Mon Mothma's");
        f.Key(StringField.Keys.Right, true); // degenerate caret -1 case
        Assert.Equal((-2, -1), (f.SelectionBegin, f.SelectionEnd));
        Assert.Equal((0, 12), f.Selection);
        f.MouseDown(f.X(6) + 0.5f);
        f.MouseUp();
        f.DoubleClick();
        Assert.Equal((4, 11, 11), (f.SelectionBegin, f.SelectionEnd, f.Caret)); // "Mothma'": the separator after the word included

        var limited = new StringField(font, 250, limit: 3);
        limited.SetText("Kuat");
        Assert.Equal("Ku", limited.Text); // >= L characters: the first L - 1
        Assert.True(limited.Type('a'));
        Assert.True(limited.Type('b')); // refused by the limit (caret 3), the message still posted
        Assert.Equal("Kua", limited.Text);

        // a set text narrower than s: s := its width (only the caret at x 0 shows)
        var g = new StringField(font, 250);
        foreach (char c in "Coruscant Bothawui Corellia Kuat Fondor") g.Type(c);
        foreach (char c in " Coruscant Bothawui") g.Type(c);
        int s = g.Scroll;
        g.SetText("Kuat");
        Assert.True(g.X(4) < s);
        Assert.Equal((g.X(4), 0), (g.Scroll, g.CaretX));
        g.Clear();
        Assert.Equal(0, g.Scroll);
    }

    // DrawText processes "&" prefixes. "&&" draws one "&" (no underline, the width of the processed text). "&x" draws x
    // underlined. A trailing "&" draws nothing. No-prefix text and the text scroll field keep "&" literally.
    // Tooltips strip every single "&".
    [Fact]
    public void PrefixProcessing()
    {
        var r = TextLayout.Glyphs(0, 0, 300, 16, "R&&D Capabilities", 10, TextFormat.Wrap);
        Assert.Equal(TextLayout.Glyphs(0, 0, 300, 16, "R&D Capabilities", 10, TextFormat.Wrap with { NoPrefix = true }), r);
        Assert.Equal("R&DCapabilities", string.Concat(r.Select(g => g.C)));
        Assert.DoesNotContain(r, g => g.Underline);
        Assert.Equal(TextLayout.TextWidth(10, "R&D Capabilities"), TextLayout.DrawnWidth(10, "R&&D Capabilities"));
        var item = TextLayout.TextItem(0, 0, 0, 0, "R&&D Capabilities", 10, TextFormat.Wrap, new TextItemSize());
        Assert.Equal(TextLayout.TextWidth(10, "R&D Capabilities"), item.Width);

        var ab = TextLayout.Glyphs(0, 0, 300, 16, "A&B", 10, TextFormat.Row);
        Assert.Equal([('A', false), ('B', true)], ab.Select(g => (g.C, g.Underline)));
        Assert.Equal(TextLayout.Advance(10, 'A'), ab[1].X);
        Assert.Equal("AB", string.Concat(TextLayout.Glyphs(0, 0, 300, 16, "AB&", 10, TextFormat.Row).Select(g => g.C)));

        Assert.Equal("Maneuvers  Tactics", TextLayout.Tooltip("Maneuvers & Tactics")); // TEXTTACT 56781
        Assert.Equal("R&D", TextLayout.Tooltip("R&&D"));

        Assert.Equal("R&&D", string.Concat(TextLayout.Glyphs(0, 0, 300, 16, "R&&D", 10, new TextFormat { SingleLine = true, NoPrefix = true }).Select(g => g.C)));
        var box = TextLayout.ScrollField(10, "R&&D", 200, 100, 16); // TextBox: words verbatim, drawn with NoPrefix
        Assert.Equal("R&&D", Assert.Single(box.Lines[0]).Word);
    }

    // Double click: a word start counts at index <= caret. A separator ends the walk only at index > caret.
    [Fact]
    public void DoubleClickAsymmetry()
    {
        (int, int) At(string text, int caret)
        {
            var f = new StringField(10, 400);
            f.SetText(text);
            f.Key(StringField.Keys.Home, false);
            for (int i = 0; i < caret; i++) f.Key(StringField.Keys.Right, false);
            f.DoubleClick();
            Assert.Equal(f.SelectionEnd, f.Caret);
            return (f.SelectionBegin, f.SelectionEnd);
        }
        Assert.Equal((0, 11), At("Mon Mothma's", 3));
        Assert.Equal((4, 12), At("Mon Mothma's", 10));
        Assert.Equal((11, 12), At("Mon Mothma's", 12));
        Assert.Equal((0, 3), At(" ab", 0));
    }

    // Printable means not Unicode Cc / Cf: "e" is typed, tab is ignored. A non-printable character keeps a mouse drag going.
    [Fact]
    public void TypedCharacters()
    {
        if (!OperatingSystem.IsWindows()) return;
        var f = new StringField(TextLayout.SystemFont, 250);
        Assert.True(f.Type('é'));
        Assert.Equal("é", f.Text);
        Assert.False(f.Type('\t'));
        Assert.Equal("é", f.Text);

        f.SetText("Coruscant");
        f.MouseDown(f.X(1) + 0.5f);
        Assert.False(f.Type('\b'));
        Assert.True(f.Selecting);
        f.MouseMove(f.X(5) + 0.5f, true);
        Assert.Equal((1, 5), (f.SelectionBegin, f.SelectionEnd));
        f.Type('x');
        Assert.False(f.Selecting);
    }
}
