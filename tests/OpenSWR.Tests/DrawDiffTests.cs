using OpenSWR.DrawDiff;

namespace OpenSWR.Tests;

/// <summary>tools/drawdiff: normalising the original draw trace and the openswr draw log, chain composition, report classes.</summary>
public class DrawDiffTests
{
    static string[] Lines(string s) => s.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // Trace records are in the Frida tracer's own form (Win32 colours and DrawText flags in hex, as it writes them); openswr
    // log records and every expected value are decimal.
    // A sprite keyed into a canvas, the canvas blitted into a backbuffer section, the section drawn to the full-screen paint DC:
    // the sprite lands at canvas offset + sprite offset. The kblit comes before the canvas is placed (fixed point, not one pass).
    const string Chain = """
        {"ev":"paint","dc":"dc1","r":[0,0,640,480],"t":0}
        {"ev":"bitmap","obj":"bmp3","kind":"section","w":640,"h":480,"t":0}
        {"ev":"bm_new","bm":"b1","kind":"section","w":640,"h":480,"t":0}
        {"ev":"bm_new","bm":"b2","kind":"canvas","w":100,"h":50,"t":0}
        {"ev":"bm_new","bm":"b3","kind":"res","w":20,"h":10,"res":"strategy.dll:2:1","t":0}
        {"ev":"bm_kblit","src":"b3","dst":"b2","dx":5,"dy":6,"key":252,"sx":0,"sy":0,"w":0,"h":0,"ra":"0x1","t":1}
        {"ev":"bm_blit","src":"b2","dst":"b1","dx":30,"dy":40,"sx":0,"sy":0,"w":0,"h":0,"t":1}
        {"ev":"bm_draw","bm":"b1","dc":"dc1","mode":64,"x":0,"y":0,"sx":0,"sy":0,"w":0,"h":0,"t":2}
        {"ev":"dc","dc":"dc5","kind":"mem","from":"dc1","t":2}
        {"ev":"select","dc":"dc5","obj":"bmp3","t":2}
        {"ev":"font","font":"font4","height":14,"weight":400,"face":"Arial","t":2}
        {"ev":"select","dc":"dc5","obj":"font4","t":2}
        {"ev":"textcolor","dc":"dc5","color":"0x200ff00","t":2}
        {"ev":"drawtext","dc":"dc5","s":"Hello","r":[10,20,110,36],"fmt":"0x24","t":2}
        {"ev":"drawtext","dc":"dc5","s":"Hello","r":[10,20,10,20],"fmt":"0x424","t":2}
        """;

    [Fact]
    public void ChainComposesCanvasBackbufferScreen()
    {
        var items = TraceReader.Read(Lines(Chain));
        Assert.Equal(2, items.Count); // the DT_CALCRECT call draws nothing
        var sprite = items.Single(i => i.Kind == "bitmap");
        Assert.Equal(("strategy.dll:2:1", 35, 46, 20, 10, 252), (sprite.Res, sprite.X!.Value, sprite.Y!.Value, sprite.W, sprite.H, sprite.Key!.Value));
        Assert.Equal("0,0 20x10", sprite.Src);
        var text = items.Single(i => i.Kind == "text");
        Assert.Equal((10, 20, 100, 16), (text.X!.Value, text.Y!.Value, text.W, text.H));
        Assert.Equal(("14/400/Arial", "0,255,0", 36, Strings.Hash("Hello")), (text.Font, text.Colour, text.Fmt!.Value, text.TextHash));
    }

    [Fact]
    public void CanvasTakingItsBackgroundFromTheBackbufferIsPlacedThere()
    {
        var items = TraceReader.Read(Lines("""
            {"ev":"paint","dc":"dc1","r":[0,0,640,480],"t":0}
            {"ev":"bm_new","bm":"b1","kind":"section","w":640,"h":480,"t":0}
            {"ev":"bm_draw","bm":"b1","dc":"dc1","x":0,"y":0,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_new","bm":"b2","kind":"canvas","w":40,"h":30,"t":0}
            {"ev":"bm_new","bm":"b3","kind":"res","w":20,"h":10,"res":"strategy.dll:2:2","t":0}
            {"ev":"bm_blit","src":"b1","dst":"b2","dx":0,"dy":0,"sx":200,"sy":100,"w":0,"h":0,"t":0}
            {"ev":"bm_kblit","src":"b3","dst":"b2","dx":2,"dy":3,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"paint","dc":"dc2","r":[0,0,40,30],"t":0}
            {"ev":"bm_draw","bm":"b2","dc":"dc2","x":0,"y":0,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_new","bm":"b4","kind":"canvas","w":40,"h":30,"t":0}
            {"ev":"bm_kblit","src":"b3","dst":"b4","dx":0,"dy":0,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            """));
        Assert.Equal((202, 103), (items[0].X!.Value, items[0].Y!.Value));
        Assert.Null(items[1].X); // a canvas with no placed source or target stays unresolved
    }

    static Report Diff(string openSWR) => Differ.Compare(TraceReader.Read(Lines(Chain)), OpenSWRLogReader.Read(Lines(openSWR)));

    const string Text = """{"ev":"drawtext","s":"Hello","r":[10,20,110,36],"fmt":36,"font":10,"c":"0,255,0"}""";

    [Fact]
    public void SameDrawsMatch()
    {
        var r = Diff("""{"ev":"bm_kblit","res":"strategy.dll:2:1","dx":35,"dy":46,"dw":20,"dh":10,"sx":0,"sy":0,"w":20,"h":10,"key":252}""" + "\n" + Text);
        Assert.Equal("matched 2, missing 0, extra 0, moved 0, different 0", r.Summary);
    }

    [Fact]
    public void ReportClasses()
    {
        var r = Diff("""
            {"ev":"bm_kblit","res":"strategy.dll:2:1","dx":36,"dy":44,"dw":20,"dh":10,"sx":0,"sy":0,"w":20,"h":10,"key":252}
            {"ev":"bm_kblit","res":"strategy.dll:2:9","dx":0,"dy":0,"dw":27,"dh":16,"sx":0,"sy":0,"w":29,"h":18,"key":252}
            """);
        Assert.Equal("matched 0, missing 1, extra 1, moved 1, different 0", r.Summary);
        Assert.Equal("(35,46) -> (36,44) delta (1,-2)", r.Findings.Single(f => f.Class == "moved").Detail);
        Assert.Equal(Strings.Hash("Hello"), r.Findings.Single(f => f.Class == "missing").Original!.TextHash);
        Assert.EndsWith("(scaled: drawn 27x16 from 0,0 29x18)", r.Findings.Single(f => f.Class == "extra").Detail);

        r = Diff("""
            {"ev":"bm_kblit","res":"strategy.dll:2:1","dx":35,"dy":46,"dw":20,"dh":9,"sx":0,"sy":0,"w":20,"h":10,"key":252}
            {"ev":"drawtext","sh":"HASH","r":[10,20,110,36],"fmt":292,"font":4,"c":"255,0,0"}
            """.Replace("HASH", Strings.Hash("Hello")));
        Assert.Equal("matched 0, missing 0, extra 0, moved 0, different 2", r.Summary);
        Assert.StartsWith("size 20x10 -> 20x9 at (35,46)", r.Findings.Single(f => f.Original!.Kind == "bitmap").Detail);
        Assert.Equal("fmt 36 -> 292, font 14/400/Arial -> 16/400/Arial, colour 0,255,0 -> 255,0,0 at (10,20) -> (10,20) delta (0,0)",
            r.Findings.Single(f => f.Original!.Kind == "text").Detail);
    }

    [Fact]
    public void VectorKeepsNoStrings()
    {
        var v = Vector.Make(Lines(Chain + """

            {"ev":"palette","obj":"hpal4","n":1,"colours":"000000","t":3}
            """)).ToList();
        Assert.DoesNotContain(v, l => l.Contains("Hello") || l.Contains("\"s\"") || l.Contains("\"ra\"") || l.Contains("palette"));
        Assert.Contains(v, l => l.Contains($"\"sh\":\"{Strings.Hash("Hello")}\""));
        var a = TraceReader.Read(Lines(Chain));
        var b = TraceReader.Read(v);
        Assert.Equal(a.Select(i => i.Full), b.Select(i => i.Full));
    }

    // State before the window (objects, DCs, placement draws) is kept; draws before it are not
    const string Windowed = """
        {"ev":"paint","dc":"dc1","r":[0,0,640,480],"t":0}
        {"ev":"bitmap","obj":"bmp3","kind":"section","w":640,"h":480,"t":0}
        {"ev":"bm_new","bm":"b1","kind":"section","w":640,"h":480,"t":0}
        {"ev":"bm_draw","bm":"b1","dc":"dc1","x":0,"y":0,"sx":0,"sy":0,"w":0,"h":0,"t":0}
        {"ev":"bm_new","bm":"b2","kind":"canvas","w":100,"h":50,"t":0}
        {"ev":"bm_blit","src":"b2","dst":"b1","dx":30,"dy":40,"sx":0,"sy":0,"w":0,"h":0,"t":1}
        {"ev":"bm_new","bm":"b7","kind":"canvas","w":9,"h":9,"t":1}
        {"ev":"bm_free","bm":"b7","t":1}
        {"ev":"bm_new","bm":"b9","kind":"res","w":20,"h":10,"res":"strategy.dll:2:9","t":1}
        {"ev":"bm_kblit","src":"b9","dst":"b2","dx":0,"dy":0,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":1}
        {"ev":"dc","dc":"dc4","kind":"mem","t":1}
        {"ev":"drawtext","dc":"dc4","s":"Secret","r":[0,0,50,16],"fmt":"0x0","t":1}
        {"ev":"bm_new","bm":"b3","kind":"res","w":20,"h":10,"res":"strategy.dll:2:1","t":10}
        {"ev":"bm_kblit","src":"b3","dst":"b2","dx":5,"dy":6,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":10}
        {"ev":"dc","dc":"dc5","kind":"mem","from":"dc1","t":10}
        {"ev":"select","dc":"dc5","obj":"bmp3","t":10}
        {"ev":"drawtext","dc":"dc5","s":"Hello","r":[10,20,110,36],"fmt":"0x24","t":10}
        """;

    [Fact]
    public void WindowedVectorKeepsTheStateBeforeIt()
    {
        var v = Vector.Make(Lines(Windowed), (10, 10)).ToList();
        Assert.DoesNotContain(v, l => l.Contains(Strings.Hash("Secret")) || l.Contains("\"b7\"") || l.Contains("strategy.dll:2:9\""));
        var a = TraceReader.Read(Lines(Windowed), (10, 10));
        var b = TraceReader.Read(v);
        Assert.Equal(a.Select(i => i.Full), b.Select(i => i.Full));
        Assert.Equal(["(35,46)", "(10,20)"], b.Select(i => i.Position));
    }

    // A canvas drawn into two targets that end up placed at different positions (placed in the first round by one of them), and a
    // canvas placed from it, are not placed
    [Fact]
    public void DisagreeingPlacementsAreDropped()
    {
        var items = TraceReader.Read(Lines("""
            {"ev":"paint","dc":"dc1","r":[0,0,640,480],"t":0}
            {"ev":"bm_new","bm":"b1","kind":"section","w":640,"h":480,"t":0}
            {"ev":"bm_draw","bm":"b1","dc":"dc1","x":0,"y":0,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_new","bm":"b2","kind":"canvas","w":40,"h":30,"t":0}
            {"ev":"bm_blit","src":"b2","dst":"b1","dx":10,"dy":10,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_new","bm":"b5","kind":"canvas","w":60,"h":60,"t":0}
            {"ev":"bm_blit","src":"b2","dst":"b5","dx":0,"dy":0,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_blit","src":"b5","dst":"b1","dx":100,"dy":100,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_new","bm":"b4","kind":"canvas","w":10,"h":10,"t":0}
            {"ev":"bm_blit","src":"b2","dst":"b4","dx":0,"dy":0,"sx":3,"sy":3,"w":10,"h":10,"t":0}
            {"ev":"bm_new","bm":"b3","kind":"res","w":20,"h":10,"res":"strategy.dll:2:1","t":0}
            {"ev":"bm_kblit","src":"b3","dst":"b2","dx":0,"dy":0,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_kblit","src":"b3","dst":"b4","dx":0,"dy":0,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"bm_kblit","src":"b3","dst":"b1","dx":7,"dy":8,"key":252,"sx":0,"sy":0,"w":0,"h":0,"t":0}
            """));
        Assert.Equal(["(?)", "(?)", "(7,8)"], items.Select(i => i.Position));
    }

    // A partial repaint of the top-level window (small rcPaint) gets the backbuffer copied in place: it is the screen. A shifted
    // copy into a paint DC is not.
    [Fact]
    public void ScreenIsThePaintDcGettingTheBackbufferInPlace()
    {
        var items = TraceReader.Read(Lines("""
            {"ev":"bitmap","obj":"bmp3","kind":"section","w":640,"h":480,"t":0}
            {"ev":"bm_new","bm":"b1","kind":"section","w":640,"h":480,"t":0}
            {"ev":"dc","dc":"dc2","kind":"mem","t":0}
            {"ev":"select","dc":"dc2","obj":"bmp3","t":0}
            {"ev":"paint","dc":"dc1","r":[0,347,107,480],"t":0}
            {"ev":"bitblt","dst":"dc1","x":0,"y":347,"w":107,"h":133,"src":"dc2","sx":0,"sy":347,"t":0}
            {"ev":"paint","dc":"dc3","r":[0,0,40,30],"t":0}
            {"ev":"bitblt","dst":"dc3","x":0,"y":0,"w":40,"h":30,"src":"dc2","sx":200,"sy":100,"t":0}
            {"ev":"bm_new","bm":"b3","kind":"res","w":20,"h":10,"res":"strategy.dll:2:1","t":0}
            {"ev":"bm_kblit","src":"b3","dst":"b1","dx":5,"dy":350,"key":252,"sx":4,"sy":0,"w":0,"h":0,"t":0}
            {"ev":"textcolor","dc":"dc3","color":"0x1000005","t":0}
            {"ev":"drawtext","dc":"dc3","s":"x","r":[1,2,11,18],"fmt":"0x0","t":0}
            """));
        Assert.Equal((5, 350), (items[0].X!.Value, items[0].Y!.Value));
        Assert.Equal("4,0 16x10", items[0].Src); // w = 0: from sx to the bitmap edge (Inferred)
        Assert.Equal(("(?)", "i5"), (items[1].Position, items[1].Colour)); // PALETTEINDEX colour
    }

    [Fact]
    public void OpenSWRLogExtras()
    {
        var items = OpenSWRLogReader.Read(Lines("""
            {"ev":"drawtext","s":"Coruscant","r":[1,2,51,18],"fmt":0,"font":-1,"c":"255,255,255","node":"Label"}
            {"ev":"bm_blit","res":null,"src":"composed:list","dx":0,"dy":0,"dw":10,"dh":10,"sx":0,"sy":0,"w":10,"h":10,"node":"X"}
            {"ev":"bm_blit","res":null,"dx":0,"dy":0,"dw":10,"dh":10,"sx":0,"sy":0,"w":10,"h":10,"node":"X"}
            {"ev":"ellipse","r":[10,20,30,40],"pen":"0,255,0","node":"GalaxyMap"}
            """));
        Assert.Equal("?/?/godot", items[0].Font);
        Assert.Equal(["bitmap src:composed:list", "bitmap (not from the install)", "ellipse 20x20"], items.Skip(1).Select(i => i.Group));
        Assert.Equal(("none", "0,255,0", "(10,20)"), (items[3].Colour, items[3].Pen, items[3].Position));
        var r = Differ.Compare([], items);
        Assert.Equal("matched 0, missing 0, extra 4, moved 0, different 0", r.Summary);
    }
}
