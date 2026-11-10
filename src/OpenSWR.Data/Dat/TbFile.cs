namespace OpenSWR.Data.Dat;

// *TB.DAT table files. Table entries refer to SD records through SdRef.

/// <summary>Reference to an SD entity: family in the high byte, SD record Index in the low 24 bits.</summary>
public readonly record struct SdRef(uint Value)
{
    public uint Family => FamilyOf(Value);
    public uint Index => IndexOf(Value);
    /// <summary>The reference of record <paramref name="index"/> in <paramref name="family"/>.</summary>
    public static uint Make(uint family, uint index) => family << 24 | index;
    public static uint FamilyOf(uint sdRef) => sdRef >> 24;
    public static uint IndexOf(uint sdRef) => sdRef & 0xFFFFFF;
    public override string ToString() => $"{Family:x2}:{Index:x}";
}

/// <summary>IntTableEntry / SeedTableEntry (16 bytes). In SeedTableEntry files Value is an SdRef.</summary>
public readonly record struct TableEntry(uint Id, uint Unk04, int Key, int Value);

/// <summary>GeneralParamTableEntry (8 values, 44 bytes) / SideParamTableEntry (16 values, 76 bytes).</summary>
public sealed record ParamTableEntry(uint Id, uint Unk04, uint ParamId, int[] Values);

public readonly record struct SeedItem(uint Unk00, uint Unk04, SdRef Ref);

/// <summary>SeedFamilyTableEntry: 24-byte head + Count x 12-byte items.</summary>
public sealed record SeedFamilyTableEntry(uint Id, uint Unk04, int Key, uint Unk0C, uint Unk10, SeedItem[] Items)
{
    /// <summary>How many copies of the item group are placed, named by its use. Meaning in the original is unknown.</summary>
    public uint Copies => Unk0C;
}

public static class TbFile
{
    /// <summary>Entry type name stored in the file header, e.g. "IntTableEntry".</summary>
    public static string EntryType(ReadOnlySpan<byte> file) =>
        System.Text.Encoding.ASCII.GetString(file.Slice(12, (int)SdFile.U32(file, 8)));

    /// <summary>IntTableEntry or SeedTableEntry files.</summary>
    public static List<TableEntry> ReadTable(ReadOnlySpan<byte> file) =>
        Read<TableEntry>(file, ["IntTableEntry", "SeedTableEntry"], (b, at, list) =>
        {
            list.Add(new TableEntry(SdFile.U32(b, at), SdFile.U32(b, at + 4), (int)SdFile.U32(b, at + 8), (int)SdFile.U32(b, at + 12)));
            return 16;
        });

    /// <summary>GeneralParamTableEntry or SideParamTableEntry files.</summary>
    public static List<ParamTableEntry> ReadParams(ReadOnlySpan<byte> file)
    {
        int count = EntryType(file) == "SideParamTableEntry" ? 16 : 8;
        return Read<ParamTableEntry>(file, ["GeneralParamTableEntry", "SideParamTableEntry"], (b, at, list) =>
        {
            var values = new int[count];
            for (int i = 0; i < count; i++) values[i] = (int)SdFile.U32(b, at + 12 + i * 4);
            list.Add(new ParamTableEntry(SdFile.U32(b, at), SdFile.U32(b, at + 4), SdFile.U32(b, at + 8), values));
            return 12 + count * 4;
        });
    }

    public static List<SeedFamilyTableEntry> ReadSeedFamilies(ReadOnlySpan<byte> file) =>
        Read<SeedFamilyTableEntry>(file, ["SeedFamilyTableEntry"], (b, at, list) =>
        {
            uint itemCount = SdFile.U32(b, at + 20);
            if (itemCount > (b.Length - at - 24) / 12) throw new InvalidDataException($"Seed entry at {at} claims {itemCount} items.");
            var items = new SeedItem[itemCount];
            for (int i = 0; i < items.Length; i++)
            {
                int o = at + 24 + i * 12;
                items[i] = new SeedItem(SdFile.U32(b, o), SdFile.U32(b, o + 4), new SdRef(SdFile.U32(b, o + 8)));
            }
            list.Add(new SeedFamilyTableEntry(SdFile.U32(b, at), SdFile.U32(b, at + 4), (int)SdFile.U32(b, at + 8),
                SdFile.U32(b, at + 12), SdFile.U32(b, at + 16), items));
            return 24 + items.Length * 12;
        });

    delegate int EntryParser<T>(ReadOnlySpan<byte> file, int at, List<T> into);

    static List<T> Read<T>(ReadOnlySpan<byte> file, string[] types, EntryParser<T> parse)
    {
        if (SdFile.U32(file, 0) != 1) throw new InvalidDataException("TB header must start with 1.");
        string type = EntryType(file);
        if (Array.IndexOf(types, type) < 0) throw new InvalidDataException($"Entry type {type}, expected {string.Join(" or ", types)}.");

        int count = (int)SdFile.U32(file, 4), at = 12 + type.Length;
        if (count < 0 || count > file.Length) throw new InvalidDataException($"{count} entries cannot fit in {file.Length} bytes.");
        var list = new List<T>(count);
        for (int i = 0; i < count; i++) at += parse(file, at, list);
        if (at != file.Length) throw new InvalidDataException($"{count} entries end at {at}, file is {file.Length} bytes.");
        return list;
    }
}
