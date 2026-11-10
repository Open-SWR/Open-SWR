using System.Buffers.Binary;
using OpenSWR.Data.Dat;

namespace OpenSWR.Data.Advisor;


/// <summary>Sprite command entry referenced by an SPT step: words [Type, ResId, W2, W3].</summary>
public readonly record struct SpriteCommand(ushort Type, ushort ResId, ushort W2, ushort W3)
{
    public const ushort Clip = 4, Still = 17, Hold = 18, VoiceClip = 19, Relay = 20, SecondaryStill = 21;

    /// <summary>Step parameters replace W2/W3 when nonzero.</summary>
    public (uint Param1, uint Param2) Parameters(SptStep step) => (step.Param1 != 0 ? step.Param1 : W2, step.Param2 != 0 ? step.Param2 : W3);
}

/// <summary>Clip entry [Sound2, VoiceWave, VoiceModule, FrameCount, BaseBitmap]. VoiceModule 9 = the sprite DLL itself.</summary>
public readonly record struct AdvisorClip(ushort Sound2, ushort VoiceWave, ushort VoiceModule, ushort FrameCount, ushort BaseBitmap);

/// <summary>Palette-index frames of one clip or still, all the size of the base bitmap.</summary>
public sealed record IndexedFrames(int Width, int Height, byte[] Palette, List<byte[]> Frames);

/// <summary>Reads advisor resources from ALSPRITE.DLL / EMSPRITE.DLL (module 9).</summary>
public sealed class SpriteDll(PeResources dll)
{
    /// <summary>Every advisor display runs at this fixed frame period.</summary>
    public const int FramePeriodMs = 67;

    /// <summary>Background palette index of advisor frames. Inferred: every base bitmap's border is index 0.</summary>
    public const int TransparentIndex = 0;

    /// <summary>Whether the DLL has RCDATA <paramref name="rcId"/> (a missing entry enqueues nothing).</summary>
    public bool Has(ushort rcId) => dll.Find(ResId.RcData, rcId) is not null;

    public ushort[] Words(ushort rcId)
    {
        var data = (dll.Find(ResId.RcData, rcId) ?? throw new KeyNotFoundException($"RCDATA {rcId} not in sprite DLL.")).Data.Span;
        var words = new ushort[data.Length / 2];
        for (int i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
        return words;
    }

    public SpriteCommand Command(ushort rcId)
    {
        var w = Words(rcId);
        return new SpriteCommand(w[0], w[1], w.Length > 2 ? w[2] : (ushort)0, w.Length > 3 ? w[3] : (ushort)0);
    }

    public AdvisorClip Clip(ushort rcId)
    {
        var w = Words(rcId);
        return new AdvisorClip(w[0], w[1], w[2], w[3], w[4]);
    }

    /// <summary>Full frames of a clip: base bitmap, then each type-302 delta added onto the previous frame.</summary>
    public IndexedFrames ClipFrames(AdvisorClip clip)
    {
        var first = Bitmap(clip.BaseBitmap);
        var frames = new List<byte[]> { first.Pixels };
        var current = first.Pixels.ToArray();
        for (int i = 1; i < clip.FrameCount; i++)
        {
            var delta = DeltaSprite.Decode((dll.Find(new ResId(302), (ushort)(clip.BaseBitmap + i))
                ?? throw new KeyNotFoundException($"Sprite {clip.BaseBitmap + i} missing.")).Data.Span);
            delta.ApplyTo(current);
            frames.Add(current.ToArray());
        }
        return new IndexedFrames(first.Width, first.Height, first.Palette, frames);
    }

    public IndexedBitmap Bitmap(ushort id) =>
        IndexedBitmap.FromDib((dll.Find(ResId.Bitmap, id) ?? throw new KeyNotFoundException($"Bitmap {id} not in sprite DLL.")).Data.Span);

    /// <summary>WAVE resource (a complete RIFF file), or null when absent.</summary>
    public byte[]? Wave(ushort id) => dll.Find(ResId.Wave, id)?.Data.ToArray();
}
