using System.Text;
using OpenSWR.Data;

namespace OpenSWR.Tests;

public class PeResourcesTests
{
    const string DumpEnvVar = "OPENSWR_DUMP_DIR"; // output of rebellion-assets/dumpres.py

    /// <summary>StringTable.All keeps the first text of an id: the install's string modules repeat no id, so first = last.</summary>
    [GameFact]
    public void StringModulesRepeatNoId()
    {
        foreach (string module in new[] { "TEXTSTRA.DLL", "TEXTCOMM.DLL", "REBDLOG.DLL", "TEXTTACT.DLL" })
        {
            var pe = PeResources.Load(GameDir.File(module));
            int entries = pe.OfType(ResId.String).Sum(b => StringTable.Decode(b.Name.Id, b.Data.Span).Count);
            Assert.True(entries > 0, module);
            Assert.Equal(entries, StringTable.All(pe).Count);
        }
    }

    [GameFact]
    public void ReadsCommonDll()
    {
        var pe = PeResources.Load(GameDir.File("COMMON.DLL"));

        Assert.Equal(321, pe.OfType(ResId.Bitmap).Count());
        Assert.Equal(5, pe.OfType(ResId.Wave).Count());

        var bmp = Dib.ToBmp(pe.OfType(ResId.Bitmap).First().Data.Span);
        Assert.Equal("BM"u8.ToArray(), bmp[..2]);
        Assert.Equal(bmp.Length, BitConverter.ToInt32(bmp, 2));
        Assert.Equal("RIFF"u8.ToArray(), pe.OfType(ResId.Wave).First().Data.Span[..4].ToArray());
    }

    // Every resource dumpres.py writes byte-for-byte (bitmaps, waves, strings, raw .bin) must match ours.
    // Types 301/303 are skipped: dumpres converts them (MSZIP .x, palettized textures); separate decoders.
    [GameFact(DumpEnvVar)]
    public void MatchesDumpresOutput()
    {
        string dumpDir = Environment.GetEnvironmentVariable(DumpEnvVar)!;
        var failures = new List<string>();
        int compared = 0;

        foreach (var moduleDir in Directory.GetDirectories(dumpDir))
        {
            string module = Path.GetFileName(moduleDir);
            if (module.StartsWith('_') || !module.Contains('_')) continue; // dumps are NAME_EXT; skips e.g. traces/
            var pe = PeResources.Load(GameDir.File(module.Replace('_', '.')));

            foreach (var group in pe.Entries.GroupBy(e => e.Type))
            {
                string typeName = TypeName(group.Key);
                if (typeName is "RT_VERSION" or "RT_GROUP_ICON" or "RT_GROUP_CURSOR" or "301" or "303") continue;
                string typeDir = Path.Combine(moduleDir, typeName);

                if (group.Key == ResId.String)
                {
                    var lines = group.SelectMany(b => StringTable.Decode(b.Name.Id, b.Data.Span)).Select(s => $"{s.Id}\t{s.Text}");
                    string expected = string.Join("\n", lines).Replace("\n", "\r\n"); // Python text mode on Windows
                    if (File.ReadAllText(Path.Combine(typeDir, "strings.tsv"), Encoding.UTF8) != expected)
                        failures.Add($"{module}/{typeName}/strings.tsv");
                    compared++;
                    continue;
                }

                var perName = group.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.Count());
                foreach (var res in group)
                {
                    string stem = Path.GetFileNameWithoutExtension(res.Name.ToString())
                        + (perName[res.Name] > 1 ? $"_{res.Language}" : "");
                    (string ext, byte[] bytes) = typeName switch
                    {
                        "RT_BITMAP" => (".bmp", Dib.ToBmp(res.Data.Span)),
                        "WAVE" => (".wav", res.Data.ToArray()),
                        _ => (".bin", res.Data.ToArray()),
                    };
                    string file = Path.Combine(typeDir, stem + ext);
                    if (!File.Exists(file) || !File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes))
                        failures.Add($"{module}/{typeName}/{stem}{ext}");
                    compared++;
                }
            }
        }

        Assert.Empty(failures);
        Assert.True(compared > 1000, $"only {compared} resources compared");
    }

    // pefile's names for standard resource types, as used for dumpres.py folder names.
    static string TypeName(ResId type) => type.Name ?? type.Id switch
    {
        1 => "RT_CURSOR", 2 => "RT_BITMAP", 3 => "RT_ICON", 4 => "RT_MENU", 5 => "RT_DIALOG", 6 => "RT_STRING",
        7 => "RT_FONTDIR", 8 => "RT_FONT", 9 => "RT_ACCELERATOR", 10 => "RT_RCDATA", 11 => "RT_MESSAGETABLE",
        12 => "RT_GROUP_CURSOR", 14 => "RT_GROUP_ICON", 16 => "RT_VERSION", 17 => "RT_DLGINCLUDE",
        19 => "RT_PLUGPLAY", 20 => "RT_VXD", 21 => "RT_ANICURSOR", 22 => "RT_ANIICON", 23 => "RT_HTML", 24 => "RT_MANIFEST",
        _ => type.Id.ToString(),
    };
}
