using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenSWR.Data;

/// <summary>
/// Front-end resources read from the install at runtime: COMMON bitmaps (colour key = palette index 252 of the original 8-bit
/// bitmap), COMMON WAVE click sounds, RT_STRING texts (TEXTCOMM, REBDLOG), the Arial font table.
/// </summary>
public static class Assets
{
    public const int ColourKey = 252;
    public static string GameDir { get; set; } = "";

    /// <summary>Parent of the sound-effect players (bus "Sound"); set by the front end. Null (a scene run alone): silent.</summary>
    public static Node? SoundRoot { get; set; }
    static readonly List<AudioStreamPlayer> Players = [];

    static readonly Dictionary<string, PeResources> Modules = [];
    static readonly Dictionary<(string, ushort), Texture2D> Bitmaps = [];
    static readonly Dictionary<(string, ushort), Vector2I> Sizes = [];
    static readonly Dictionary<(string, ushort), AudioStream?> Waves = [];
    static readonly Dictionary<string, Dictionary<int, string>> Strings = [];
    static readonly Dictionary<int, Font> Fonts = [];

    // Opaque front-end bitmaps (checked by pixel census); every other one is blitted colour-keyed.
    static bool Opaque(ushort id) => id is CommonBitmaps.MainMenuBackground or CommonBitmaps.OptionsBackground or (>= CommonBitmaps.GalaxySizeHuge and <= CommonBitmaps.GalaxySizeStandard)
        or (>= CommonBitmaps.DisplayToggleOff and <= CommonBitmaps.DisplayToggleDisabled) or (>= CommonBitmaps.MusicSliderTrack and <= CommonBitmaps.SaveSideHeadToHead);

    public static PeResources Module(string name) =>
        Modules.TryGetValue(name, out var m) ? m : Modules[name] = PeResources.Load(Path.Combine(GameDir, name));

    /// <summary>COMMON.DLL (or <paramref name="module"/>) bitmap; index 252 transparent unless the bitmap is one of COMMON's opaque ones.</summary>
    public static Texture2D Bitmap(ushort id, string module = "COMMON.DLL")
    {
        if (Bitmaps.TryGetValue((module, id), out var t)) return t;
        var res = Module(module).Find(ResId.Bitmap, id) ?? throw new FileNotFoundException($"Bitmap {id} not in {module}.");
        var b = IndexedBitmap.FromDib(res.Data.Span);
        Sizes[(module, id)] = new Vector2I(b.Width, b.Height);
        bool opaque = module == "COMMON.DLL" && Opaque(id);
        var image = Image.CreateFromData(b.Width, b.Height, false, Image.Format.Rgba8, IndexedBitmap.ToRgba(b.Pixels, b.Palette, opaque ? -1 : ColourKey));
        var texture = Bitmaps[(module, id)] = ImageTexture.CreateFromImage(image);
        DrawLog.Register(texture, module, id, Sizes[(module, id)], opaque ? -1 : ColourKey);
        return texture;
    }

    static readonly Dictionary<(string, ushort), Texture2D?> Optional = [];

    /// <summary><see cref="Bitmap"/>, or null when the module lacks the bitmap (the miss is cached too).</summary>
    public static Texture2D? TryBitmap(ushort id, string module = "COMMON.DLL")
    {
        if (Optional.TryGetValue((module, id), out var t)) return t;
        try { return Optional[(module, id)] = Module(module).Find(ResId.Bitmap, id) is null ? null : Bitmap(id, module); }
        catch (FileNotFoundException) { return Optional[(module, id)] = null; } // the module itself is missing
    }

    static readonly Dictionary<(string, ushort), IndexedBitmap?> IndexedBitmaps = [];

    /// <summary>A bitmap of <paramref name="module"/> as palette indices (for object-picture composition); null when missing.</summary>
    public static IndexedBitmap? Indexed(ushort id, string module)
    {
        if (IndexedBitmaps.TryGetValue((module, id), out var b)) return b;
        return IndexedBitmaps[(module, id)] = Module(module).Find(ResId.Bitmap, id) is { } res ? IndexedBitmap.FromDib(res.Data.Span) : null;
    }

    /// <summary>Texture of an indexed bitmap with index <paramref name="key"/> transparent (-1: opaque).</summary>
    public static Texture2D Texture(IndexedBitmap b, int key) =>
        ImageTexture.CreateFromImage(Image.CreateFromData(b.Width, b.Height, false, Image.Format.Rgba8, IndexedBitmap.ToRgba(b.Pixels, b.Palette, key)));

    static readonly Dictionary<int, Texture2D?> EDataPictures = [];

    /// <summary>
    /// Encyclopedia picture: ENCYBMAP.DLL's RT_STRING <paramref name="id"/> is a file name
    /// ("EDATA.nnn") in the install's EData folder (registry EncyLocation not read: the relative folder), an 8-bit BMP drawn opaque
    /// (Inferred); null when the string or the file is missing. Logical size = the file's size.
    /// </summary>
    public static Texture2D? EData(int id)
    {
        if (EDataPictures.TryGetValue(id, out var t)) return t;
        string name = id is > 0 and <= 65535 ? Text("ENCYBMAP.DLL", id) : "";
        string path = Path.Combine(GameDir, "EData", name);
        if (name.Length == 0 || !File.Exists(path)) return EDataPictures[id] = null;
        var b = IndexedBitmap.FromDib(File.ReadAllBytes(path).AsSpan(14)); // BITMAPFILEHEADER, then the DIB
        var image = Image.CreateFromData(b.Width, b.Height, false, Image.Format.Rgba8, IndexedBitmap.ToRgba(b.Pixels, b.Palette, -1));
        var texture = ImageTexture.CreateFromImage(image);
        if (DrawLog.On) DrawLog.Tag(texture, "edata:" + name);
        return EDataPictures[id] = texture;
    }

    /// <summary>
    /// Object picture of a message or encyclopedia id: STRATEGY.DLL bitmap first, else the EData
    /// picture through ENCYBMAP; null for 0 or neither. Returns the texture and its logical size.
    /// </summary>
    public static (Texture2D Texture, Vector2 Size)? ObjectPicture(int id)
    {
        if (id is <= 0 or > 65535) return null;
        if (Module("STRATEGY.DLL").Find(ResId.Bitmap, (ushort)id) is not null) return (Strategy((ushort)id), BitmapSize((ushort)id, "STRATEGY.DLL"));
        return EData(id) is { } e ? (e, e.GetSize()) : null;
    }

    /// <summary>STRATEGY.DLL bitmap (battle windows), colour-keyed.</summary>
    public static Texture2D Strategy(ushort id) => Bitmap(id, "STRATEGY.DLL");

    /// <summary>Original (logical) size of a bitmap: lay out with this, never with the texture size (upscaled art).</summary>
    public static Vector2I BitmapSize(ushort id, string module = "COMMON.DLL")
    {
        if (!Sizes.ContainsKey((module, id))) Bitmap(id, module);
        return Sizes[(module, id)];
    }

    /// <summary>Scroll-bar skin RT_RCDATA; null if the module has no such entry.</summary>
    public static ScrollSkin? Skin(ushort id, string module = "STRATEGY.DLL") =>
        Module(module).Find(ResId.RcData, id) is { } r ? ScrollSkin.Decode(r.Data.Span) : null;

    /// <summary>RT_RCDATA bytes of a module (e.g. ENCYTEXT.DLL encyclopedia texts); null when missing.</summary>
    public static byte[]? RcData(int id, string module) =>
        id is > 0 and <= 65535 && Module(module).Find(ResId.RcData, (ushort)id) is { } r ? r.Data.ToArray() : null;

    /// <summary>STRATEGY.DLL popup-menu row entry; null when the id has none.</summary>
    public static MenuItem? Menu(int id) =>
        id is (>= 32 and <= 41) or (>= 256 and <= 624) && Module("STRATEGY.DLL").Find(ResId.RcData, (ushort)id) is { } r ? MenuItem.Decode(r.Data.Span) : null;

    /// <summary>STRATEGY.DLL map legend panel; null when missing.</summary>
    public static LegendPanel? Legend(int id) =>
        Module("STRATEGY.DLL").Find(ResId.RcData, (ushort)id) is { } r ? LegendPanel.Decode(r.Data.Span) : null;

    public static Texture2D[] Frames(ushort first, int count)
    {
        var frames = new Texture2D[count];
        for (int i = 0; i < count; i++) frames[i] = Bitmap((ushort)(first + i));
        return frames;
    }

    /// <summary>COMMON.DLL (or <paramref name="module"/>, e.g. STRATEGY.DLL window sounds) WAVE resource (PCM WAV).</summary>
    public static AudioStream? Wave(ushort id, string module = "COMMON.DLL")
    {
        if (Waves.TryGetValue((module, id), out var s)) return s;
        return Waves[(module, id)] = Module(module).Find(ResId.Wave, id) is { } r ? AudioStreamWav.LoadFromBuffer(r.Data.ToArray()) : null;
    }

    /// <summary>STRATEGY WAVE 608, the click of the strategy buttons that have one, played on press.</summary>
    public static AudioStream? Click => Wave(StrategySounds.Click, "STRATEGY.DLL");

    /// <summary>Plays a STRATEGY.DLL WAVE (window sounds).</summary>
    public static void PlayStrategy(int id) => PlaySound(Wave((ushort)id, "STRATEGY.DLL"));

    /// <summary>
    /// Plays a sound effect on a free player of the pool (one more when all are busy): overlapping sounds mix, also the same
    /// WAVE twice; no channel cap, no priority, never looped.
    /// </summary>
    public static void PlaySound(AudioStream? sound)
    {
        if (sound is null || SoundRoot is null || !GodotObject.IsInstanceValid(SoundRoot)) return;
        Players.RemoveAll(p => !GodotObject.IsInstanceValid(p));
        var player = Players.Find(p => !p.Playing);
        if (player is null)
        {
            SoundRoot.AddChild(player = new AudioStreamPlayer { Bus = "Sound" });
            Players.Add(player);
        }
        player.Stream = sound;
        player.Play();
    }

    /// <summary>RT_STRING text of a module (TEXTCOMM.DLL front-end labels, REBDLOG.DLL dialog texts); "" if missing.</summary>
    public static string Text(string module, int id)
    {
        if (!Strings.TryGetValue(module, out var table)) Strings[module] = table = StringTable.All(Module(module));
        return table.GetValueOrDefault(id, "");
    }

    public static string Comm(int id) => Text("TEXTCOMM.DLL", id);
    public static string Stra(int id) => Text("TEXTSTRA.DLL", id);

    /// <summary>
    /// TEXTSTRA.DLL RCDATA message template <paramref name="id"/>: placeholder slot n takes argument n in call order, formatted by
    /// the placeholder's kind; an argument is a string or a kind: text function (<see cref="Side"/>). "" if missing.
    /// </summary>
    public static string Message(int id, params object[] args)
    {
        if (Module("TEXTSTRA.DLL").Find(ResId.RcData, (ushort)id) is not { } r || !MessageText.IsMessage(r.Data.Span)) return "";
        var text = new System.Text.StringBuilder();
        foreach (var part in MessageText.Decode(r.Data.Span))
            text.Append(part switch
            {
                MessageLiteral l => l.Text,
                MessagePlaceholder p when p.Slot >= 1 && p.Slot <= args.Length => args[p.Slot - 1] switch
                {
                    Func<uint, string> f => f(p.Kind),
                    var a => a.ToString(),
                },
                _ => "",
            });
        return text.ToString();
    }

    /// <summary>
    /// Side name argument: TEXTSTRA by placeholder kind, side 1 / side 2:
    /// 1: 19792, 2: 19808, 4: 19824, kinds 16-25: 19840 + 2*(kind - 16), +1 for side 2. Inferred: other kinds, and ids missing
    /// from the module (19840 up), fall back to kind 1; the 32 / 33 suffix is left out.
    /// </summary>
    public static Func<uint, string> Side(bool sideOne) => kind =>
    {
        int b = kind switch { MessageArgKinds.SideFullName or MessageArgKinds.SideFullNameSuffixed => StrategyTexts.SideFullName, MessageArgKinds.SideAdjective => StrategyTexts.SideAdjective, >= MessageArgKinds.SideNameKindFirst and <= MessageArgKinds.SideNameKindLast => StrategyTexts.SideNameKindBase + 2 * (int)(kind - MessageArgKinds.SideNameKindFirst), _ => StrategyTexts.SideName };
        string t = Stra(b + (sideOne ? 0 : 1));
        return t != "" ? t : Stra(StrategyTexts.SideName + (sideOne ? 0 : 1));
    };

    public static string Dialog(int id) => Text("REBDLOG.DLL", id);

    // Font table, GDI advances and DrawText layout: OpenSWR.Data.TextLayout.
    static Assets() => TextLayout.Fallback = (index, c) => (int)Math.Round(Font(index).GetCharSize(c, FontSize(index)).X);

    /// <summary>Godot font size = the GDI ppem of the table index: the em size, smaller than the table's cell height.</summary>
    public static int FontSize(int index) => TextLayout.Ppem(index);
    /// <summary>Line height = GDI tmHeight.</summary>
    public static int LineHeight(int index) => TextLayout.LineHeight(index);
    /// <summary>Baseline below the line top = GDI tmAscent.</summary>
    public static int Ascent(int index) => TextLayout.Ascent(index);

    /// <summary>
    /// Arial at the table weight. Weight 900 (font 13) maps to Arial Bold, as GDI maps it; there is no synthetic emboldening.
    /// Hinting on, integer glyph positions.
    /// </summary>
    public static Font Font(int index)
    {
        if (Fonts.TryGetValue(index, out var f)) return f;
        return Fonts[index] = new SystemFont
        {
            FontNames = ["Arial"], FontWeight = Math.Min(TextLayout.Weight(index), 700), Antialiasing = TextServer.FontAntialiasing.Gray,
            Hinting = TextServer.Hinting.Normal, SubpixelPositioning = TextServer.SubpixelPositioning.Disabled,
        };
    }

    /// <summary>Width of one line in font <paramref name="index"/> (GetTextExtentPoint32).</summary>
    public static int TextWidth(int index, string text) => TextLayout.TextWidth(index, text);

    /// <summary>The lines DrawText lays out (<see cref="TextLayout.Lines"/>).</summary>
    public static List<string> Lines(int index, string text, int width = -1) => TextLayout.Lines(index, text, width);

    /// <summary>Tooltip text: "&" stripped as the tooltip control does (Inferred; <see cref="TextLayout.Tooltip"/>).</summary>
    public static string Tip(string text) => TextLayout.Tooltip(text);

    /// <summary>
    /// DrawText emulation (<see cref="TextLayout.Glyphs"/>), transparent background, glyph by glyph at GDI advances,
    /// baseline = line top + ascent; clipped at the rect unless <see cref="TextFormat.NoClip"/>.
    /// </summary>
    public static void DrawText(CanvasItem canvas, Rect2 rect, string text, int font, Color colour, TextFormat format)
    {
        DrawLog.Text(canvas, rect, text, font, colour, format);
        var f = Font(font);
        int size = FontSize(font), ascent = Ascent(font);
        foreach (var g in TextLayout.Glyphs(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, text, font, format))
        {
            // the glyph centred in its GDI cell (Inferred: Godot's outlines are not x-hinted, so a hinted-wide GDI advance
            // such as 'w' at 11 px would leave its whole gap on the right)
            canvas.DrawChar(f, new Vector2(g.X + (g.Advance - f.GetCharSize(g.C, size).X) / 2, g.Y + ascent), g.C.ToString(), size, colour);
            // prefix underline (Inferred geometry): 1 px, one pixel below the baseline, over the advance, text colour
            if (g.Underline) canvas.DrawRect(new Rect2(g.X, g.Y + ascent + 1, g.Advance, 1), colour);
        }
    }

    /// <summary>
    /// A text item's text (<see cref="TextLayout.TextItem"/>): <paramref name="size"/> fixes the width / height.
    /// A non-fixed item is measured to its text; a centred one loses centring and grows 1 px each side. Clipped only when the size says so.
    /// </summary>
    public static void DrawTextItem(CanvasItem canvas, Rect2 rect, string text, int font, Color colour, TextFormat format, TextItemSize size)
    {
        var (x, y, w, h, f) = TextLayout.TextItem(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, text, font, format, size);
        DrawText(canvas, new Rect2(x, y, w, h), text, font, colour, f);
    }

    /// <summary>A cell-list item's text: list text offset, alignment inset, clipped unless word break.</summary>
    public static void DrawCellText(CanvasItem canvas, Rect2 rect, string text, int font, Color colour, TextFormat format, float offset = 1)
    {
        var (x, y, w, h, f) = TextLayout.CellText(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, format, offset);
        DrawText(canvas, new Rect2(x, y, w, h), text, font, colour, f);
    }

    /// <summary>Plain yes / no dialog (substitute for the REBDLOG art dialogs): message, question on the next line.</summary>
    public static void Confirm(Node parent, string text, Action yes)
    {
        var dialog = new ConfirmationDialog { DialogText = text, OkButtonText = "Yes", CancelButtonText = "No", Exclusive = true };
        dialog.Confirmed += yes;
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        parent.AddChild(dialog);
        dialog.PopupCentered();
    }
}
