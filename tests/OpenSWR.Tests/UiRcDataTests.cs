using OpenSWR.Data;

namespace OpenSWR.Tests;

/// <summary>STRATEGY menus, legends and skins, and COMMON and TACTICAL skins.</summary>
public class UiRcDataTests
{
    static HashSet<int> BitmapIds(PeResources m) => m.OfType(ResId.Bitmap).Select(e => (int)e.Name.Id).ToHashSet();

    [GameFact]
    public void StrategyTablesDecode()
    {
        var strategy = PeResources.Load(GameDir.File("STRATEGY.DLL"));
        var bitmaps = BitmapIds(strategy);
        var strings = PeResources.Load(GameDir.File("TEXTSTRA.DLL")).OfType(ResId.String)
            .SelectMany(b => StringTable.Decode(b.Name.Id, b.Data.Span)).Select(s => s.Id).ToHashSet();
        var entries = strategy.OfType(ResId.RcData).ToList();
        Assert.Equal(96, entries.Count);
        var decoded = entries.ToDictionary(e => (int)e.Name.Id, e => (Entry: e, Value: UiRcData.Decode(e.Name.Id, e.Data.Span)));

        var menus = decoded.Where(d => d.Value.Value is MenuItem).ToList();
        var legends = decoded.Where(d => d.Value.Value is LegendPanel).ToList();
        var skins = decoded.Where(d => d.Value.Value is ScrollSkin).ToList();
        Assert.Equal((63, 27, 6), (menus.Count, legends.Count, skins.Count));

        foreach (var (id, (entry, value)) in decoded)
        {
            byte[] bytes = value switch { MenuItem m => m.ToBytes(), LegendPanel l => l.ToBytes(), ScrollSkin s => s.ToBytes(), _ => [] };
            Assert.Equal(entry.Data.ToArray(), bytes);
        }

        foreach (var (id, (entry, value)) in menus)
        {
            var m = (MenuItem)value;
            Assert.Equal(id, m.Id);
            Assert.Equal(id == 384 ? 28 : 26, entry.Data.Length);
            if (id == 384)
            {
                // the 13-word read shifts the modules (text 0 / images 2); the real image module is the raw 14th word
                Assert.Equal((0, 2, 7), (m.TextModule, m.ImageModule, m.Extra.Single()));
            }
            else Assert.Equal((2, 7), (m.TextModule, m.ImageModule));
            if (m.Label != 0) Assert.Contains(m.Label, strings);
            foreach (var b in new[] { m.Image, m.HotImage, m.DisabledImage, m.CheckImage }.Where(b => b != 0)) Assert.Contains(b, bitmaps);
        }

        foreach (var (id, (_, value)) in legends)
        {
            var l = (LegendPanel)value;
            Assert.Empty(l.Trailing); // ends exactly at the 0 terminator
            Assert.Equal((180, 7, 2), (l.Width, l.ImageModule, l.TextModule));
            foreach (var item in l.Items)
                if (item is LegendImage i) Assert.Contains(i.Bitmap, bitmaps);
                else Assert.Contains(((LegendText)item).String, strings);
        }

        foreach (var (_, (_, value)) in skins)
            Assert.All(((ScrollSkin)value).Bitmaps.Where(b => b != 0), b => Assert.Contains(b, bitmaps));
        Assert.Equal(Enumerable.Range(10749, 8), ((ScrollSkin)decoded[10748].Value).Bitmaps.Select(b => (int)b).Order());
        Assert.Equal([10656, 10685, 10686, 10687, 10688, 10689, 10690, 10691], ((ScrollSkin)decoded[10653].Value).Bitmaps.Select(b => (int)b).Order());

        // 10183 / 10184 differ only in the title string 5717 / 5718
        ushort[] a = UiRcData.Words(decoded[10183].Entry.Data.Span), b2 = UiRcData.Words(decoded[10184].Entry.Data.Span);
        Assert.Equal(a.Length, b2.Length);
        var diff = Enumerable.Range(0, a.Length).Where(i => a[i] != b2[i]).ToList();
        Assert.Equal((5717, 5718), (a[diff.Single()], b2[diff.Single()]));
        var title = ((LegendPanel)decoded[10183].Value).Items.OfType<LegendText>().First();
        Assert.Equal((1, 5717), (title.Y, title.String));
    }

    [GameTheory]
    [InlineData("COMMON.DLL", 10149)]
    [InlineData("TACTICAL.DLL", 1350)]
    public void OtherSkinsDecode(string module, int id)
    {
        var dll = PeResources.Load(GameDir.File(module));
        var data = dll.Find(ResId.RcData, (ushort)id)!.Data;
        Assert.Equal(20, data.Length);
        var skin = ScrollSkin.Decode(data.Span);
        Assert.Equal(data.ToArray(), skin.ToBytes());
        var bitmaps = BitmapIds(dll);
        Assert.All(skin.Bitmaps.Where(b => b != 0), b => Assert.Contains(b, bitmaps));
    }
}
