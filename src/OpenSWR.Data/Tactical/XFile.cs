using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenSWR.Data.Tactical;

// DirectX .x files in TACTICAL.DLL resource type 301.

/// <summary>Data object of a binary .x file. Data holds the object's own values in file order (int, float or string).</summary>
public sealed record XNode(string Type, string? Name, List<object> Data, List<XNode> Children, List<string> References)
{
    public int Int(int i) => (int)Data[i];
    public float Float(int i) => Data[i] is float f ? f : (int)Data[i];
    public XNode? Child(string type) => Children.FirstOrDefault(c => c.Type == type);
}

public static class XFile
{
    /// <summary>"bzip" (MSZIP) .x -> equivalent "bin " .x. Other .x data is returned unchanged.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> file)
    {
        if (!file[8..12].SequenceEqual("bzip"u8)) return file.ToArray();

        // u32 total size at 16, then blocks [u16 rawSize, u16 compSize, "CK", deflate (compSize - 2 bytes)].
        // Each block is a raw deflate stream that may refer back into the previous block's output (up to 32 KiB).
        var output = new MemoryStream();
        output.Write(file[..8]);
        output.Write("bin "u8);
        output.Write(file[12..16]);
        byte[] previous = [];
        for (int p = 20; p < file.Length;)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(file[p..]), comp = BinaryPrimitives.ReadUInt16LittleEndian(file[(p + 2)..]);
            if (!file.Slice(p + 4, 2).SequenceEqual("CK"u8)) throw new InvalidDataException($"MSZIP block signature missing at {p}.");
            var block = InflateWithHistory(file.Slice(p + 6, comp - 2), previous, raw);
            output.Write(block);
            previous = block.Length >= 32768 ? block[^32768..] : block;
            p += 4 + comp;
        }
        return output.ToArray();
    }

    // .NET's DeflateStream has no preset dictionary: prepend the history as a stored deflate block, then drop it from the output.
    static byte[] InflateWithHistory(ReadOnlySpan<byte> deflate, byte[] history, int rawSize)
    {
        var input = new MemoryStream();
        if (history.Length > 0)
        {
            input.WriteByte(0); // BFINAL = 0, BTYPE = 00 (stored), padded to the byte boundary
            Span<byte> lengths = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(lengths, (ushort)history.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(lengths[2..], (ushort)~history.Length);
            input.Write(lengths);
            input.Write(history);
        }
        input.Write(deflate);
        input.Position = 0;

        var result = new byte[history.Length + rawSize];
        using var inflate = new DeflateStream(input, CompressionMode.Decompress);
        inflate.ReadExactly(result);
        return result[history.Length..];
    }

    /// <summary>Top-level data objects of a binary .x file ("xof 0303bin 0032"); template declarations are skipped.</summary>
    public static List<XNode> Parse(ReadOnlySpan<byte> file)
    {
        if (!file[..4].SequenceEqual("xof "u8) || !file[8..12].SequenceEqual("bin "u8))
            throw new InvalidDataException("Not a binary .x file.");
        if (!file[12..16].SequenceEqual("0032"u8)) throw new InvalidDataException("Only 32-bit float .x supported.");
        var reader = new Reader(file.ToArray(), 16);
        var nodes = new List<XNode>();
        while (!reader.End)
        {
            if (reader.PeekToken() == Template) reader.SkipTemplate();
            else nodes.Add(reader.ReadObject());
        }
        return nodes;
    }

    const ushort Name = 1, String = 2, Integer = 3, Guid = 5, IntegerList = 6, FloatList = 7,
        OpenBrace = 10, CloseBrace = 11, Comma = 19, Semicolon = 20, Template = 31;

    sealed class Reader(byte[] data, int position)
    {
        int _p = position;
        public bool End => _p >= data.Length;

        public ushort PeekToken() => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_p));
        ushort Token() { var t = PeekToken(); _p += 2; return t; }
        int I32() { int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_p)); _p += 4; return v; }

        string Text()
        {
            int n = I32();
            var s = Encoding.Latin1.GetString(data, _p, n);
            _p += n;
            return s;
        }

        public void SkipTemplate()
        {
            for (int depth = 0; ;)
            {
                ushort t = SkipToken();
                if (t == OpenBrace) depth++;
                else if (t == CloseBrace && --depth == 0) return;
            }
        }

        /// <summary>Moves past <paramref name="count"/> items of <paramref name="size"/> bytes; a negative or too large count is corrupt data (it would loop for ever).</summary>
        void Skip(int count, int size)
        {
            if (count < 0 || count > (data.Length - _p) / size) throw new InvalidDataException($"Bad item count {count} at {_p}.");
            _p += count * size;
        }

        ushort SkipToken()
        {
            ushort t = Token();
            switch (t)
            {
                case Name: Text(); break;
                case String: Text(); Token(); break; // followed by its terminator token
                case Integer: _p += 4; break;
                case Guid: _p += 16; break;
                case IntegerList or FloatList: Skip(I32(), 4); break;
            }
            return t;
        }

        public XNode ReadObject()
        {
            if (Token() != Name) throw new InvalidDataException($"Expected object type name at {_p - 2}.");
            string type = Text();
            string? name = null;
            if (PeekToken() == Name) { Token(); name = Text(); }
            if (PeekToken() == Guid) { Token(); _p += 16; }
            if (Token() != OpenBrace) throw new InvalidDataException($"Expected {{ after {type} at {_p - 2}.");

            var node = new XNode(type, name, [], [], []);
            while (true)
            {
                switch (PeekToken())
                {
                    case CloseBrace: Token(); return node;
                    case Name: node.Children.Add(ReadObject()); break;
                    case OpenBrace: // reference: { name }
                        Token();
                        if (Token() != Name) throw new InvalidDataException($"Expected reference name at {_p - 2}.");
                        node.References.Add(Text());
                        if (Token() != CloseBrace) throw new InvalidDataException($"Expected }} after reference at {_p - 2}.");
                        break;
                    case IntegerList:
                        Token();
                        for (int n = I32(); n > 0; n--) node.Data.Add(I32());
                        break;
                    case FloatList:
                        Token();
                        for (int n = I32(); n > 0; n--) { node.Data.Add(BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(_p))); _p += 4; }
                        break;
                    case Integer: Token(); node.Data.Add(I32()); break;
                    case String: Token(); node.Data.Add(Text()); Token(); break;
                    case Comma or Semicolon: Token(); break;
                    default: throw new InvalidDataException($"Unexpected token {PeekToken()} in {type} at {_p}.");
                }
            }
        }
    }
}

public sealed record XMaterial(float[] FaceColor, float Power, float[] Specular, float[] Emissive, string? Texture);

/// <summary>Typed view of a .x Mesh object and the children used by the game's models.</summary>
public sealed record XMesh(float[] Positions, int[][] Faces, float[] Normals, int[][] NormalFaces, float[] TexCoords,
    int[] FaceMaterials, List<XMaterial> Materials)
{
    public static XMesh From(XNode mesh)
    {
        var (positions, faces) = VectorsAndFaces(mesh);
        var normals = mesh.Child("MeshNormals");
        var (normalVectors, normalFaces) = normals is null ? ([], []) : VectorsAndFaces(normals);

        float[] uv = [];
        if (mesh.Child("MeshTextureCoords") is { } coords)
            uv = Enumerable.Range(1, coords.Int(0) * 2).Select(coords.Float).ToArray();

        int[] faceMaterials = [];
        var materials = new List<XMaterial>();
        if (mesh.Child("MeshMaterialList") is { } list)
        {
            faceMaterials = Enumerable.Range(2, list.Int(1)).Select(list.Int).ToArray();
            foreach (var m in list.Children.Where(c => c.Type == "Material"))
                materials.Add(new XMaterial(
                    Enumerable.Range(0, 4).Select(m.Float).ToArray(), m.Float(4),
                    Enumerable.Range(5, 3).Select(m.Float).ToArray(), Enumerable.Range(8, 3).Select(m.Float).ToArray(),
                    m.Child("TextureFilename")?.Data[0] as string));
        }
        return new XMesh(positions, faces, normalVectors, normalFaces, uv, faceMaterials, materials);
    }

    // Shared layout of Mesh and MeshNormals: nVectors, Vector[n] (3 floats), nFaces, MeshFace[n] (count, indices).
    static (float[] Vectors, int[][] Faces) VectorsAndFaces(XNode node)
    {
        int n = node.Int(0);
        var vectors = Enumerable.Range(1, n * 3).Select(node.Float).ToArray();
        int at = 1 + n * 3, faceCount = node.Int(at++);
        var faces = new int[faceCount][];
        for (int f = 0; f < faceCount; f++)
        {
            int count = node.Int(at++);
            faces[f] = Enumerable.Range(at, count).Select(node.Int).ToArray();
            at += count;
        }
        return (vectors, faces);
    }
}
