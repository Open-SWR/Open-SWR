using OpenSWR.Data.Dat;

namespace OpenSWR.Tests;

public class TbFileTests
{
    static byte[] Dat(string name) => File.ReadAllBytes(GameDir.File(Path.Combine("GData", name)));

    [GameFact]
    public void AllTbFilesParseToEnd()
    {
        var files = Directory.GetFiles(GameDir.File("GData"), "*TB.DAT");
        Assert.Equal(33, files.Length);
        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            int count = TbFile.EntryType(bytes) switch
            {
                "IntTableEntry" or "SeedTableEntry" => TbFile.ReadTable(bytes).Count,
                "GeneralParamTableEntry" or "SideParamTableEntry" => TbFile.ReadParams(bytes).Count,
                "SeedFamilyTableEntry" => TbFile.ReadSeedFamilies(bytes).Count,
                var t => throw new Xunit.Sdk.XunitException($"{path}: unknown entry type {t}"),
            };
            Assert.Equal((int)BitConverter.ToUInt32(bytes, 4), count);
        }
    }

    [GameFact]
    public void SeedReferencesResolveToSdRecords()
    {
        var entities = Directory.GetFiles(GameDir.File("GData"), "*SD.DAT")
            .SelectMany(p => SdRecord.Read(File.ReadAllBytes(p)).Records)
            .Select(r => (r.Common.Family, r.Common.Index)).ToHashSet();

        var refs = new List<SdRef>();
        foreach (var path in Directory.GetFiles(GameDir.File("GData"), "*TB.DAT"))
        {
            var bytes = File.ReadAllBytes(path);
            switch (TbFile.EntryType(bytes))
            {
                case "SeedFamilyTableEntry":
                    refs.AddRange(TbFile.ReadSeedFamilies(bytes).SelectMany(e => e.Items).Select(i => i.Ref));
                    break;
                case "SeedTableEntry":
                    refs.AddRange(TbFile.ReadTable(bytes).Select(e => new SdRef((uint)e.Value)));
                    break;
            }
        }
        Assert.NotEmpty(refs);
        Assert.All(refs.Where(r => r.Value != 0), r => Assert.Contains((r.Family, r.Index), entities));

        // CMUNAFTB first entry: one Corellian Corvette (capital ship family 0x14, Index 0x45)
        var corvette = TbFile.ReadSeedFamilies(Dat("CMUNAFTB.DAT"))[0].Items.Single().Ref;
        Assert.Equal((0x14u, 0x45u), (corvette.Family, corvette.Index));
    }

    [GameFact]
    public void ProbabilityTablesAreSortedByKey()
    {
        var abduction = TbFile.ReadTable(Dat("ABDCMSTB.DAT"));
        Assert.Equal(12, abduction.Count);
        Assert.Equal(new TableEntry(1, 1, -100, 5), abduction[0]);
        Assert.Equal(new TableEntry(12, 1, 100, 99), abduction[^1]);
        Assert.True(abduction.Zip(abduction.Skip(1)).All(p => p.First.Key < p.Second.Key));

        var general = TbFile.ReadParams(Dat("GNPRTB.DAT"));
        Assert.Equal(213, general.Count);
        Assert.All(general, p => Assert.Equal(8, p.Values.Length));
        Assert.All(TbFile.ReadParams(Dat("SDPRTB.DAT")), p => Assert.Equal(16, p.Values.Length));
    }
}
