using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

/// <summary>
/// Mod pictures: the PNG of <see cref="GameDefinitions.Pictures"/> is drawn in place of a record's
/// install picture, scaled (aspect kept) into the logical size of the install pictures of the same family and form, so a PNG
/// of any resolution draws at the same logical size (upscale-ready). Install bitmaps otherwise. Textures are cached per path; a
/// PNG that cannot be read is reported once and the install picture (if any) is drawn instead.
/// </summary>
public static class ModArt
{
    static readonly Dictionary<string, Texture2D?> Textures = [];
    static readonly Dictionary<(uint Family, int Form), Vector2> Boxes = [];

    /// <summary>
    /// Picture number of a record (Inferred): string ids come in blocks of 4096 per kind of text, and the picture is the id's place
    /// in its block.
    /// </summary>
    public static int PictureNumber(int stringId) => stringId % 4096;

    /// <summary>The definition key of an object (its record's SdRef): the key of <see cref="GameDefinitions.Pictures"/>.</summary>
    public static uint Ref(GameObject o) => o switch
    {
        Character c => c.Definition.Common.Ref,
        Mission m => m.Record.Common.Ref,
        StarSystem s => s.Definition.Common.Ref,
        _ => o.Type,
    };

    /// <summary>Forgets the cached pictures (a mod PNG may have been edited).</summary>
    public static void Forget() => Textures.Clear();

    /// <summary>The mod picture of a record, or null.</summary>
    public static Texture2D? Texture(GameDefinitions d, uint sdRef)
    {
        if (!d.Pictures.TryGetValue(sdRef, out var path)) return null;
        if (Textures.TryGetValue(path, out var known)) return known;
        var image = Image.LoadFromFile(path);
        Texture2D? t = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
        if (t is null) GD.PushError($"mod picture '{path}' cannot be read; the install picture is drawn.");
        else if (DrawLog.On) DrawLog.Tag(t, "mod:" + System.IO.Path.GetFileName(path));
        return Textures[path] = t;
    }

    /// <summary><paramref name="t"/> scaled (aspect kept) to fit <paramref name="box"/>, whole pixels.</summary>
    public static Vector2 Fit(Texture2D t, Vector2 box)
    {
        var size = t.GetSize();
        float k = Mathf.Min(box.X / size.X, box.Y / size.Y);
        return (size * k).Floor();
    }

    /// <summary>
    /// Mod picture of a record in form <paramref name="form"/> (the offset added to the picture id p: 0 small, 0x4000 large,
    /// + 0x1000 side 2, ...), with its logical size = the size of the GOKRES picture p + form of the first install record of the
    /// same family (the PNG's own size when there is none). Null without a mod picture.
    /// </summary>
    public static (Texture2D Texture, Vector2 Size)? Picture(GameDefinitions d, uint sdRef, int form)
    {
        if (Texture(d, sdRef) is not { } t) return null;
        uint family = new SdRef(sdRef).Family;
        if (!Boxes.TryGetValue((family, form), out var box))
        {
            box = Vector2.Zero;
            foreach (var (_, _, records) in d.SdFiles)
                foreach (var r in records)
                    if (box == Vector2.Zero && new SdRef(r.Common.Ref).Family == family && r.Common.StringModule != GameDefinitions.ModStringModule
                        && Install(true, PictureNumber(r.Common.StringId) + form) is { } install)
                        box = install.Size;
            Boxes[(family, form)] = box;
        }
        return (t, box == Vector2.Zero ? t.GetSize() : Fit(t, box));
    }

    static readonly Dictionary<(bool, int), Texture2D> Keyed = [];

    /// <summary>
    /// A GOKRES.DLL (or STRATEGY.DLL) bitmap at its logical size, keyed on its own bottom-left index (252 for
    /// most, e.g. 105 / 72 / 73 for the manager pictures 262-264); null when missing.
    /// </summary>
    public static (Texture2D Texture, Vector2 Size)? Install(bool gokres, int id)
    {
        if (id is <= 0 or > 0xFFFF || Assets.Indexed((ushort)id, gokres ? "GOKRES.DLL" : "STRATEGY.DLL") is not { } b) return null;
        if (!Keyed.TryGetValue((gokres, id), out var t))
        {
            Keyed[(gokres, id)] = t = Assets.Texture(b, b.Key);
            DrawLog.Register(t, gokres ? "GOKRES.DLL" : "STRATEGY.DLL", (ushort)id, new Vector2I(b.Width, b.Height), b.Key);
        }
        return (t, new Vector2(b.Width, b.Height));
    }

    static readonly Dictionary<string, (Texture2D, Vector2)?> Composed = [];

    /// <summary>
    /// The drawable layers of an object picture (<see cref="ScreenViews.Picture"/>): one picture composed in
    /// palette indices (fill with the base's key, frame opaque, the rest keyed on their own keys; transparent = the result's key).
    /// No base bitmap: nothing. With a mod picture the base is the mod PNG at its place in the order: the STRATEGY layers under it
    /// composed at its size (fill 252: shortcut, any index works there since the fill stays the key; upgrade if a mod picture
    /// ever needs frames of another size), then the PNG; the GOKRES variants of p (Enroute / Damaged / Captured) are left out, so
    /// nothing lies over the PNG.
    /// </summary>
    public static List<(Texture2D Texture, Vector2 Size)> Layers(GameState game, GameObject o, List<ScreenViews.PictureLayer> layers)
    {
        int at = layers.FindIndex(l => l.Role == ScreenViews.PictureRole.Base);
        if (at < 0) return [];
        var b = layers[at];
        if ((b.Gokres ? Picture(game.Definitions, Ref(o), b.Id - ScreenViews.PictureId(game, o)) : null) is { } mod)
        {
            var under = layers.Take(at).Where(l => !l.Gokres).ToList();
            return under.Count > 0 && Compose(under, (int)mod.Size.X, (int)mod.Size.Y, Assets.ColourKey) is { } u ? [u, mod] : [mod];
        }
        if (Assets.Indexed((ushort)b.Id, b.Gokres ? "GOKRES.DLL" : "STRATEGY.DLL") is not { } bb) return [];
        return Compose(layers, bb.Width, bb.Height, bb.Key) is { } c ? [c] : [];
    }

    static (Texture2D, Vector2)? Compose(List<ScreenViews.PictureLayer> layers, int w, int h, int fill)
    {
        string id = $"{w}x{h}:{fill}:" + string.Join(",", layers.Select(l => $"{(l.Gokres ? "G" : "S")}{l.Id}{(int)l.Role}"));
        if (Composed.TryGetValue(id, out var known)) return known;
        var parts = layers.Select(l => (l.Id is > 0 and <= 0xFFFF ? Assets.Indexed((ushort)l.Id, l.Gokres ? "GOKRES.DLL" : "STRATEGY.DLL") : null, l.Role == ScreenViews.PictureRole.Frame))
            .Where(p => p.Item1 is not null).Select(p => (p.Item1!, p.Item2)).ToList();
        if (parts.Count == 0) return Composed[id] = null;
        var (picture, key) = IndexedBitmap.Compose(w, h, fill, parts[0].Item1.Palette, parts);
        var texture = Assets.Texture(picture, key);
        if (DrawLog.On) DrawLog.Tag(texture, "composed:" + string.Join("+", layers.Select(l => (l.Gokres ? "G" : "S") + l.Id)), key);
        return Composed[id] = (texture, new Vector2(w, h));
    }

    /// <summary>Sector-window system picture (14000 + p, 37 x 37; Destroyed 10240): a system's mod picture fits the same 37 x 37.</summary>
    public static (Texture2D Texture, Vector2 Size) SystemPicture(GameDefinitions d, StarSystem s, int picture, bool destroyed)
    {
        if (!destroyed && Texture(d, s.Definition.Common.Ref) is { } t) return (t, Fit(t, new Vector2(37, 37)));
        ushort id = (ushort)(destroyed ? 10240 : 14000 + picture);
        // An unmodded install has no 14000 range: its SYSTEMSD p is 1-26 over bitmaps 10212-10234, 10237-10239 (Inferred from the art: Bespin, Hoth, Coruscant).
        if (!destroyed && Assets.Module("STRATEGY.DLL").Find(ResId.Bitmap, id) is null)
            id = (ushort)(picture <= 23 ? 10211 + picture : 10213 + picture);
        return (Assets.Strategy(id), Assets.BitmapSize(id, "STRATEGY.DLL"));
    }
}
