namespace OpenSWR.Data.Dat;

// Advisor action scripts (*.SPT). A script is a list of 14-byte steps.

/// <summary>
/// One 14-byte step. RcId = RT_RCDATA id of a sprite command entry; Flags bit 0 = verbose-commentary only,
/// bit 1 = resource from the briefing DLL; Param1/Param2 override the entry's words 2/3 when nonzero.
/// </summary>
public readonly record struct SptStep(ushort RcId, uint Flags, uint Param1, uint Param2)
{
    /// <summary>Verbose-commentary only: plays only with Translate Counterpart on.</summary>
    public bool Commentary => (Flags & 1) != 0;
    /// <summary>The resource comes from the briefing DLL.</summary>
    public bool Briefing => (Flags & 2) != 0;
}

/// <summary>Id = request slot id; Enabled 0 = plays no steps; Cursor = runtime step position (0 in files).</summary>
public sealed record SptAction(uint Id, uint Enabled, uint Cursor, SptStep[] Steps);

/// <summary>Advisor action scripts (C3POACT.SPT, IMP22ACT.SPT): u32 count, then actions.</summary>
public static class SptFile
{
    public static List<SptAction> Read(ReadOnlySpan<byte> file)
    {
        int count = (int)SdFile.U32(file, 0), at = 4;
        if (count < 0 || count > file.Length) throw new InvalidDataException($"{count} actions cannot fit in {file.Length} bytes.");
        var actions = new List<SptAction>(count);
        for (int i = 0; i < count; i++)
        {
            uint stepCount = SdFile.U32(file, at + 8);
            if (stepCount > (file.Length - at - 16) / 14) throw new InvalidDataException($"Action at {at} claims {stepCount} steps.");
            var steps = new SptStep[stepCount];
            for (int j = 0; j < steps.Length; j++)
            {
                int o = at + 16 + j * 14;
                steps[j] = new SptStep(SdFile.U16(file, o), SdFile.U32(file, o + 2), SdFile.U32(file, o + 6), SdFile.U32(file, o + 10));
            }
            actions.Add(new SptAction(SdFile.U32(file, at), SdFile.U32(file, at + 4), SdFile.U32(file, at + 12), steps));
            at += 16 + steps.Length * 14;
        }
        if (at != file.Length) throw new InvalidDataException($"{count} actions end at {at}, file is {file.Length} bytes.");
        return actions;
    }
}
