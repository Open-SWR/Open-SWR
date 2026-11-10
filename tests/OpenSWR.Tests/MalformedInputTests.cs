using System.Buffers.Binary;
using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Data.Media;

namespace OpenSWR.Tests;

/// <summary>Corrupt headers must fail with InvalidDataException, not hang, crash or allocate gigabytes (no game install needed).</summary>
public class MalformedInputTests
{
    static byte[] Bytes(params uint[] words)
    {
        var b = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(i * 4), words[i]);
        return b;
    }

    [Fact]
    public void AFacilityFileWithNoRecordsIsEmpty() => Assert.Empty(FacilityRecord.Read(Bytes(1, 0, 0, 0)));

    [Fact]
    public void ARleHeaderClaimingAHugeImageIsRefused() =>
        Assert.Throws<InvalidDataException>(() => RleImage.Decode(Bytes(30000, 30000)));

    [Fact]
    public void ASptStepCountBeyondTheFileIsRefused() =>
        Assert.Throws<InvalidDataException>(() => SptFile.Read(Bytes(1, 7, 0, 4294967280u, 0)));

    [Fact]
    public void ASmackerHeaderWithTooManyFramesIsRefused()
    {
        var file = new byte[104];
        "SMK2"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => new SmackerVideo(file));
    }

    [Fact]
    public void ASmackerHeaderWithAHugeFrameSizeIsRefusedBeforeAllocating()
    {
        var file = new byte[104];
        "SMK2"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), int.MaxValue); // width x height would overflow
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => new SmackerVideo(file));
    }

    [Fact]
    public void AResourceDirectoryPointingBackAtItselfIsRefused()
    {
        // minimal PE32 with one section; the resource root's only entry points at the root itself
        var file = new byte[1024];
        void W16(int at, int v) => BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), (ushort)v);
        void W32(int at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), v);
        W16(0, 23117); W32(60, 64);
        W32(64, 17744); W16(64 + 6, 1); W16(64 + 20, 224);
        int opt = 64 + 24;
        W16(opt, 267); W32(opt + 96 + 16, 4096); // resource directory RVA
        int sec = opt + 224;
        W32(sec + 8, 512); W32(sec + 12, 4096); W32(sec + 16, 512); W32(sec + 20, 512);
        W16(512 + 14, 1); // one id entry
        W32(512 + 16, 1); W32(512 + 20, 2147483648u); // type 1, subdirectory at offset 0 = the root
        Assert.Throws<InvalidDataException>(() => new PeResources(file));
    }

    [Fact]
    public void ASmackerTreeOfOnlyOneBitsFailsInsteadOfOverflowingTheStack()
    {
        // header ok, 1 frame, tree region of 255 bytes: every bit is a node, so the reader recurses
        var file = new byte[104 + 5 + 4000];
        "SMK2"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(52), 4000);
        file.AsSpan(104 + 5).Fill(255);
        Assert.Throws<InvalidDataException>(() => new SmackerVideo(file));
    }
}
