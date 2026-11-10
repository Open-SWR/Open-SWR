using System.Buffers.Binary;
using System.Text;

namespace OpenSWR.Data;

/// <summary>Resource type or name: numeric id, or string name when <see cref="Name"/> is set.</summary>
public readonly record struct ResId(ushort Id, string? Name = null)
{
    public static readonly ResId Bitmap = new(2);
    public static readonly ResId String = new(6);
    public static readonly ResId RcData = new(10);
    public static readonly ResId Wave = new(0, "WAVE");

    public static implicit operator ResId(ushort id) => new(id);
    public static implicit operator ResId(string name) => new(0, name);
    public override string ToString() => Name ?? Id.ToString();
}

public sealed record PeResource(ResId Type, ResId Name, ushort Language, ReadOnlyMemory<byte> Data);

/// <summary>Reads the resource tree (.rsrc) of a PE32/PE32+ file.</summary>
public sealed class PeResources
{
    readonly byte[] _file;
    readonly (uint Va, uint Size, uint Raw)[] _sections;
    readonly List<PeResource> _entries = [];

    public IReadOnlyList<PeResource> Entries => _entries;

    public static PeResources Load(string path) => new(File.ReadAllBytes(path));

    public PeResources(byte[] file)
    {
        _file = file;
        if (U16(0) != 0x5A4D) throw new InvalidDataException("No MZ header.");
        int pe = (int)U32(0x3C);
        if (U32(pe) != 0x4550) throw new InvalidDataException("No PE header.");
        int sectionCount = U16(pe + 6);
        int opt = pe + 24;
        int dataDirs = opt + (U16(opt) == 0x20B ? 112 : 96);
        uint rsrcRva = U32(dataDirs + 2 * 8);

        int sec = opt + U16(pe + 20);
        _sections = new (uint, uint, uint)[sectionCount];
        for (int i = 0; i < sectionCount; i++, sec += 40)
            _sections[i] = (U32(sec + 12), Math.Max(U32(sec + 8), U32(sec + 16)), U32(sec + 20));

        if (rsrcRva == 0) return;
        int root = Offset(rsrcRva);
        foreach (var (type, typeDir) in Directory(root, root))
            foreach (var (name, nameDir) in Directory(root, typeDir))
                foreach (var (lang, d) in Directory(root, nameDir))
                {
                    var data = new ReadOnlyMemory<byte>(_file, Offset(U32(d)), (int)U32(d + 4));
                    _entries.Add(new PeResource(type, name, lang.Id, data));
                }
    }

    public IEnumerable<PeResource> OfType(ResId type) => _entries.Where(e => e.Type == type);

    public PeResource? Find(ResId type, ResId name) =>
        _entries.FirstOrDefault(e => e.Type == type && e.Name == name);

    // Entries of one directory: (id or name, file offset of the subdirectory or data entry).
    // Name and child fields are offsets from the resource root; the high bit only flags string / subdirectory.
    IEnumerable<(ResId Key, int Child)> Directory(int root, int dir)
    {
        int count = U16(dir + 12) + U16(dir + 14);
        for (int i = 0, e = dir + 16; i < count; i++, e += 8)
        {
            uint name = U32(e);
            ResId key = (name & 0x80000000) == 0
                ? new ResId((ushort)name)
                : new ResId(0, ReadName(root + (int)(name & 0x7FFFFFFF)));
            int child = root + (int)(U32(e + 4) & 0x7FFFFFFF);
            if (child <= dir) throw new InvalidDataException($"Resource directory at {dir} points back to {child}."); // children lie after their parent: no loops
            yield return (key, child);
        }
    }

    string ReadName(int at) => Encoding.Unicode.GetString(_file, at + 2, U16(at) * 2);

    int Offset(uint rva)
    {
        foreach (var (va, size, raw) in _sections)
            if (rva >= va && rva < va + size) return (int)(rva - va + raw);
        throw new InvalidDataException($"RVA 0x{rva:X} outside all sections.");
    }

    ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_file.AsSpan(at));
    uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan(at));
}
