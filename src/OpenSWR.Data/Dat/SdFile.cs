using System.Buffers.Binary;

namespace OpenSWR.Data.Dat;

// Inferred = read from data patterns only, not yet confirmed in REBEXE.EXE.

/// <summary>Family range is half-open: [FamilyMin, FamilyMax). Unk00 = version, never checked by the game.</summary>
public readonly record struct SdHeader(uint Unk00, int Count, uint FamilyMin, uint FamilyMax);

/// <summary>
/// First 24 bytes of every *SD.DAT record. ProducerFamilyMin/Max: family range of the facility type that builds the unit
/// (0/0 = not buildable). Display name = string StringId in module StringModule (2 = TEXTSTRA.DLL, 1 = TEXTCOMM.DLL).
/// </summary>
public readonly record struct SdCommon(uint Index, uint Unk04, uint ProducerFamilyMin, uint ProducerFamilyMax, uint Family,
    ushort StringId, ushort StringModule)
{
    /// <summary>The record's own reference: its family and Index.</summary>
    public uint Ref => SdRef.Make(Family, Index);
    /// <summary>StringId without its string block: the per-record number that also picks the record's pictures.</summary>
    public int StringNumber => StringId & 0xFFF;
}

public delegate T SdRecordParser<T>(SdCommon common, ReadOnlySpan<byte> rest);

public static class SdFile
{
    public const int HeaderSize = 16, CommonSize = 24;

    public static (SdHeader Header, List<T> Records) Read<T>(ReadOnlySpan<byte> file, int recordSize, SdRecordParser<T> parse)
    {
        var header = new SdHeader(U32(file, 0), (int)U32(file, 4), U32(file, 8), U32(file, 12));
        if (header.Count < 0 || file.Length != HeaderSize + (long)header.Count * recordSize)
            throw new InvalidDataException($"Expected {header.Count} records of {recordSize} bytes, file is {file.Length} bytes.");

        var records = new List<T>(header.Count);
        for (int i = 0; i < header.Count; i++)
        {
            var r = file.Slice(HeaderSize + i * recordSize, recordSize);
            var common = new SdCommon(U32(r, 0), U32(r, 4), U32(r, 8), U32(r, 12), U32(r, 16), U16(r, 20), U16(r, 22));
            records.Add(parse(common, r[CommonSize..]));
        }
        return (header, records);
    }

    internal static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
    internal static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b[at..]);

    /// <summary>u32 at a full-record offset, read from the span after the common prefix.</summary>
    internal static uint At(ReadOnlySpan<byte> rest, int recordOffset) => U32(rest, recordOffset - CommonSize);
}

/// <summary>SECTORSD.DAT record (36 bytes).</summary>
public sealed record SectorRecord(SdCommon Common, uint Group, uint GalaxySize, ushort X, ushort Y)
{
    public static List<SectorRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 36, (c, r) => new SectorRecord(c, SdFile.U32(r, 0), SdFile.U32(r, 4), SdFile.U16(r, 8), SdFile.U16(r, 10))).Records;
}

/// <summary>SYSTEMSD.DAT record (44 bytes).</summary>
public sealed record SystemRecord(SdCommon Common, uint SectorIndex, uint Unk1C, uint Unk20, ushort X, ushort Y, uint Unk28)
{
    /// <summary>The system's picture number, named by its use. Meaning in the original is unknown.</summary>
    public uint Picture => Unk1C;

    public static List<SystemRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 44, (c, r) => new SystemRecord(c, SdFile.U32(r, 0), SdFile.U32(r, 4), SdFile.U32(r, 8),
            SdFile.U16(r, 12), SdFile.U16(r, 14), SdFile.U32(r, 16))).Records;
}

/// <summary>One value per weapon type, in record order.</summary>
public readonly record struct Weapons(uint Turbolaser, uint IonCannon, uint LaserCannon)
{
    internal static Weapons At(ReadOnlySpan<byte> rest, int recordOffset) =>
        new(SdFile.At(rest, recordOffset), SdFile.At(rest, recordOffset + 4), SdFile.At(rest, recordOffset + 8));
}

/// <summary>CAPSHPSD.DAT record (200 bytes). Fields marked Inferred are not yet confirmed in REBEXE.EXE.</summary>
public sealed record CapitalShipRecord(SdCommon Common,
    uint Alliance, uint Empire, uint RefinedMaterialCost, uint MaintenanceCost,
    uint ResearchOrder, uint ResearchDifficulty, uint Unk30, uint Unk34,
    uint ShieldStrength, uint Unk3C, uint Maneuverability, uint Hyperdrive, uint HyperdriveIfDamaged,
    Weapons Fore, Weapons Aft, Weapons Port, Weapons Starboard, Weapons Range, Weapons AttackStrength, uint TotalAttackStrength,
    uint Hull, uint TractorBeamPower, uint TractorBeamRange, uint GravityWellA4, uint InterdictionStrength,
    uint Bombardment, uint DamageControl, uint WeaponRechargeRate, uint ShieldRechargeRate,
    uint FighterCapacity, uint TroopCapacity, uint UnkC4)
{
    /// <summary>Sublight speed, named by its use. Meaning in the original is unknown.</summary>
    public uint Speed => Unk3C;
    /// <summary>Detection in mission encounters, named by its use. Meaning in the original is unknown.</summary>
    public uint Detection => Unk34;

    public static List<CapitalShipRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 200, (c, r) => new CapitalShipRecord(c,
            SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32), SdFile.At(r, 36),
            SdFile.At(r, 40), SdFile.At(r, 44), SdFile.At(r, 48), SdFile.At(r, 52),
            SdFile.At(r, 56), SdFile.At(r, 60), SdFile.At(r, 64), SdFile.At(r, 68), SdFile.At(r, 72),
            Weapons.At(r, 76), Weapons.At(r, 88), Weapons.At(r, 100), Weapons.At(r, 112), Weapons.At(r, 124), Weapons.At(r, 136), SdFile.At(r, 148),
            SdFile.At(r, 152), SdFile.At(r, 156), SdFile.At(r, 160), SdFile.At(r, 164), SdFile.At(r, 168),
            SdFile.At(r, 172), SdFile.At(r, 176), SdFile.At(r, 180), SdFile.At(r, 184),
            SdFile.At(r, 188), SdFile.At(r, 192), SdFile.At(r, 196))).Records;
}

/// <summary>FIGHTSD.DAT record (168 bytes). Offsets 24-148 share CapitalShipRecord's layout and reader. The last four u32 are fighter-only.</summary>
public sealed record FighterRecord(SdCommon Common,
    uint Alliance, uint Empire, uint RefinedMaterialCost, uint MaintenanceCost,
    uint ResearchOrder, uint ResearchDifficulty, uint Unk30, uint Unk34,
    uint ShieldStrength, uint Unk3C, uint Maneuverability, uint Hyperdrive, uint HyperdriveIfDamaged,
    Weapons Fore, Weapons Aft, Weapons Port, Weapons Starboard, Weapons Range, Weapons AttackStrength, uint TotalAttackStrength,
    uint Unk98, uint Unk9C, uint UnkA0, uint UnkA4)
{
    // Names by the sim's use of the fields. Meaning in the original is unknown.
    public uint Speed => Unk3C;
    public uint Detection => Unk34;
    public uint Torpedoes => Unk98;
    public uint TorpedoRange => Unk9C;
    public uint SquadronSize => UnkA0;
    public uint Bombardment => UnkA4;

    public static List<FighterRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 168, (c, r) => new FighterRecord(c,
            SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32), SdFile.At(r, 36),
            SdFile.At(r, 40), SdFile.At(r, 44), SdFile.At(r, 48), SdFile.At(r, 52),
            SdFile.At(r, 56), SdFile.At(r, 60), SdFile.At(r, 64), SdFile.At(r, 68), SdFile.At(r, 72),
            Weapons.At(r, 76), Weapons.At(r, 88), Weapons.At(r, 100), Weapons.At(r, 112), Weapons.At(r, 124), Weapons.At(r, 136), SdFile.At(r, 148),
            SdFile.At(r, 152), SdFile.At(r, 156), SdFile.At(r, 160), SdFile.At(r, 164))).Records;
}

/// <summary>TROOPSD.DAT record (68 bytes).</summary>
public sealed record TroopRecord(SdCommon Common,
    uint Alliance, uint Empire, uint RefinedMaterialCost, uint MaintenanceCost,
    uint ResearchOrder, uint ResearchDifficulty, uint Unk30, uint Unk34,
    uint BombardmentDefense, uint AttackStrength, uint DefenseStrength)
{
    /// <summary>Detection in mission encounters, named by its use. Meaning in the original is unknown.</summary>
    public uint Detection => Unk34;

    public static List<TroopRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 68, (c, r) => new TroopRecord(c,
            SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32), SdFile.At(r, 36),
            SdFile.At(r, 40), SdFile.At(r, 44), SdFile.At(r, 48), SdFile.At(r, 52),
            SdFile.At(r, 56), SdFile.At(r, 60), SdFile.At(r, 64))).Records;
}

/// <summary>MANFACSD.DAT, PROFACSD.DAT (56 bytes) and ALLFACSD.DAT (52 bytes, no ProcessingRate).</summary>
public sealed record FacilityRecord(SdCommon Common,
    uint Alliance, uint Empire, uint RefinedMaterialCost, uint MaintenanceCost,
    uint ResearchOrder, uint ResearchDifficulty, uint BombardmentDefense, uint? ProcessingRate)
{
    public static List<FacilityRecord> Read(ReadOnlySpan<byte> file)
    {
        if (SdFile.U32(file, 4) == 0) return []; // a mod removed every record
        int size = (file.Length - SdFile.HeaderSize) / (int)SdFile.U32(file, 4);
        if (size is not (52 or 56)) throw new InvalidDataException($"Unexpected facility record size {size}.");
        return SdFile.Read(file, size, (c, r) => new FacilityRecord(c,
            SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32), SdFile.At(r, 36),
            SdFile.At(r, 40), SdFile.At(r, 44), SdFile.At(r, 48), r.Length > 28 ? SdFile.At(r, 52) : null)).Records;
    }
}

/// <summary>DEFFACSD.DAT record (60 bytes).</summary>
public sealed record DefenseFacilityRecord(SdCommon Common,
    uint Alliance, uint Empire, uint RefinedMaterialCost, uint MaintenanceCost,
    uint ResearchOrder, uint ResearchDifficulty, uint BombardmentDefense, uint AttackStrength, uint ShieldStrength)
{
    public static List<DefenseFacilityRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 60, (c, r) => new DefenseFacilityRecord(c,
            SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32), SdFile.At(r, 36),
            SdFile.At(r, 40), SdFile.At(r, 44), SdFile.At(r, 48), SdFile.At(r, 52), SdFile.At(r, 56))).Records;
}

/// <summary>Base value plus random variance (Inferred from editor naming).</summary>
public readonly record struct Skill(uint Base, uint Variance)
{
    internal static Skill At(ReadOnlySpan<byte> rest, int recordOffset) => new(SdFile.At(rest, recordOffset), SdFile.At(rest, recordOffset + 4));
}

/// <summary>Eight skill pairs at record offsets 48-108 of characters and special forces. Each skill is base plus a roll from 0 to variance.</summary>
public readonly record struct Skills(Skill Diplomacy, Skill Espionage, Skill ShipDesign, Skill TroopTraining,
    Skill FacilityDesign, Skill Combat, Skill Leadership, Skill Loyalty)
{
    internal static Skills At(ReadOnlySpan<byte> r) => new(Skill.At(r, 48), Skill.At(r, 56), Skill.At(r, 64), Skill.At(r, 72),
        Skill.At(r, 80), Skill.At(r, 88), Skill.At(r, 96), Skill.At(r, 104));
}

/// <summary>MJCHARSD.DAT / MNCHARSD.DAT record (148 bytes).</summary>
public sealed record CharacterRecord(SdCommon Common, uint Alliance, uint Empire, uint Unk20, uint Unk24, uint Unk28, uint Unk2C,
    Skills Skills, uint JediProbability, uint IsKnownJedi, Skill JediLevel,
    uint CanBeAdmiral, uint CanBeCommander, uint CanBeGeneral, uint IsUnableToBetray, uint IsJediTrainer)
{
    public static List<CharacterRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 148, (c, r) => new CharacterRecord(c, SdFile.At(r, 24), SdFile.At(r, 28),
            SdFile.At(r, 32), SdFile.At(r, 36), SdFile.At(r, 40), SdFile.At(r, 44),
            Skills.At(r), SdFile.At(r, 112), SdFile.At(r, 116), Skill.At(r, 120),
            SdFile.At(r, 128), SdFile.At(r, 132), SdFile.At(r, 136), SdFile.At(r, 140), SdFile.At(r, 144))).Records;
}

/// <summary>SPECFCSD.DAT record (116 bytes).</summary>
public sealed record SpecialForceRecord(SdCommon Common,
    uint Alliance, uint Empire, uint RefinedMaterialCost, uint MaintenanceCost,
    uint ResearchOrder, uint ResearchDifficulty, Skills Skills, uint MissionMask)
{
    /// <summary>Both forces' mission masks share a mission.</summary>
    public bool SharesMissions(SpecialForceRecord other) => (MissionMask & other.MissionMask) != 0;

    public static List<SpecialForceRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 116, (c, r) => new SpecialForceRecord(c,
            SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32), SdFile.At(r, 36),
            SdFile.At(r, 40), SdFile.At(r, 44), Skills.At(r), SdFile.At(r, 112))).Records;
}

/// <summary>MISSNSD.DAT record (112 bytes).</summary>
public sealed record MissionRecord(SdCommon Common, uint Alliance, uint Empire, uint SpecialForceMask, uint Unk24, uint Unk28, uint Unk2C,
    uint Unk30, uint Unk34, uint Unk38, uint Unk3C, uint Unk40, uint Unk44, uint Unk48, uint Unk4C, uint Unk50, uint Unk54, uint Unk58, uint Unk5C, uint Unk60, uint Unk64, uint Unk68, uint Unk6C)
{
    // Names by the sim's use of the fields. Meaning in the original is unknown.

    /// <summary>A special force with this MissionMask may join: its mask lies inside SpecialForceMask.</summary>
    public bool AllowsSpecialForce(uint missionMask) => (missionMask & ~SpecialForceMask) == 0;
    /// <summary>Characters may join (Unk24 holds the character marker 0x10000).</summary>
    public bool AllowsCharacters => (Unk24 & 0x10000) != 0;
    /// <summary>Duration in days: base and random variance of the mission timer.</summary>
    public int DurationBase => (int)Unk28;
    public int DurationVariance => (int)Unk2C;
    /// <summary>Repeating type: phase 10 returns to phase 8.</summary>
    public bool Repeats => Unk30 != 0;
    /// <summary>Left out of the mission lists (movement and story missions).</summary>
    public bool Unlisted => Unk34 != 0;
    /// <summary>The mission meets encounters.</summary>
    public bool HasEncounters => Unk38 != 0;
    /// <summary>Members resign on injury and on evasion.</summary>
    public bool ResignOnInjury => Unk3C != 0;
    /// <summary>Member clean-up rule (0, 1 or 2).</summary>
    public int CleanUpRule => (int)Unk40;
    public bool RefusesDestroyedTarget => Unk44 != 0;
    public bool NeedsExploredTarget => Unk48 != 0;
    public bool NeedsPopulatedTarget => Unk4C != 0;
    public bool RefusesDestroyedObjective => Unk50 != 0;
    /// <summary>The objective may be the side's own, the enemy's, or neither side's.</summary>
    public bool AllowsOwnObjective => Unk54 != 0;
    public bool AllowsNeutralObjective => Unk58 != 0;
    public bool AllowsEnemyObjective => Unk5C != 0;
    /// <summary>A target system may be in Uprising, or not.</summary>
    public bool AllowsUprisingSystem => Unk60 != 0;
    public bool AllowsCalmSystem => Unk64 != 0;
    /// <summary>A target character may be Captured, or not.</summary>
    public bool AllowsCapturedCharacter => Unk68 != 0;
    public bool AllowsFreeCharacter => Unk6C != 0;

    public static List<MissionRecord> Read(ReadOnlySpan<byte> file) =>
        SdFile.Read(file, 112, (c, r) => new MissionRecord(c, SdFile.At(r, 24), SdFile.At(r, 28), SdFile.At(r, 32),
            SdFile.At(r, 36), SdFile.At(r, 40), SdFile.At(r, 44),
            SdFile.At(r, 48), SdFile.At(r, 52), SdFile.At(r, 56), SdFile.At(r, 60), SdFile.At(r, 64), SdFile.At(r, 68), SdFile.At(r, 72), SdFile.At(r, 76), SdFile.At(r, 80), SdFile.At(r, 84), SdFile.At(r, 88), SdFile.At(r, 92), SdFile.At(r, 96), SdFile.At(r, 100), SdFile.At(r, 104), SdFile.At(r, 108))).Records;
}

/// <summary>
/// An *SD.DAT file opened for editing (the mod loader): its header and records, written back with <see cref="ToBytes"/>.
/// </summary>
public sealed class SdTable
{
    public required SdHeader Header { get; init; }
    /// <summary>Record size in bytes.</summary>
    public required int Size { get; init; }
    public required List<SdEditRecord> Records { get; init; }
    /// <summary>Highest Index ever held, removed records included: a new record never reuses the Index of a removed one.</summary>
    public uint Peak { get; set; }

    public static SdTable Read(ReadOnlySpan<byte> file)
    {
        var (header, records) = SdRecord.Read(file);
        int size = header.Count == 0 ? SdFile.CommonSize : (file.Length - SdFile.HeaderSize) / header.Count;
        return new SdTable
        {
            Header = header, Size = size,
            Peak = records.Count == 0 ? 0 : records.Max(r => r.Common.Index),
            Records = records.Select(r => new SdEditRecord([r.Common.Index, r.Common.Unk04, r.Common.ProducerFamilyMin, r.Common.ProducerFamilyMax,
                r.Common.Family, (uint)r.Common.StringId | (uint)r.Common.StringModule << 16, .. r.Body])).ToList(),
        };
    }

    /// <summary>A zeroed record of this file's size (not added).</summary>
    public SdEditRecord NewRecord() => new(new uint[Size / 4]);

    public SdEditRecord? Find(uint index) => Records.FirstOrDefault(r => r.Index == index);

    public byte[] ToBytes()
    {
        var bytes = new byte[SdFile.HeaderSize + Records.Count * Size];
        var span = bytes.AsSpan();
        uint[] head = [Header.Unk00, (uint)Records.Count, Header.FamilyMin, Header.FamilyMax];
        for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(span[(i * 4)..], head[i]);
        for (int r = 0; r < Records.Count; r++)
            for (int w = 0; w < Size / 4; w++) BinaryPrimitives.WriteUInt32LittleEndian(span[(SdFile.HeaderSize + r * Size + w * 4)..], Records[r].Words[w]);
        return bytes;
    }
}

/// <summary>One editable *SD.DAT record: the common prefix by name, the other fields by <see cref="SdField"/>.</summary>
public sealed class SdEditRecord
{
    internal SdEditRecord(uint[] words) => Words = words;
    internal uint[] Words { get; }

    public SdEditRecord Clone() => new((uint[])Words.Clone());

    public uint Index { get => Words[0]; set => Words[0] = value; }
    public uint Unk04 { get => Words[1]; set => Words[1] = value; }
    public (uint Min, uint Max) Producer { get => (Words[2], Words[3]); set => (Words[2], Words[3]) = value; }
    public uint Family { get => Words[4]; set => Words[4] = value; }
    public ushort StringId { get => (ushort)(Words[5] & 0xFFFF); set => Words[5] = Words[5] & 0xFFFF0000 | value; }
    public ushort StringModule { get => (ushort)(Words[5] >> 16); set => Words[5] = Words[5] & 0xFFFF | (uint)value << 16; }
    /// <summary>The record's own reference: its family and Index.</summary>
    public uint Ref => SdRef.Make(Family, Index);

    /// <summary>A u32 field.</summary>
    public uint Get(SdField field) => Words[(int)field / 4];
    public void Set(SdField field, uint value) => Words[(int)field / 4] = value;

    /// <summary>A u16 field (sector and system X / Y).</summary>
    public ushort GetU16(SdField field) => (ushort)((int)field % 4 == 0 ? Words[(int)field / 4] & 0xFFFF : Words[(int)field / 4] >> 16);
    public void SetU16(SdField field, ushort value)
    {
        int w = (int)field / 4;
        Words[w] = (int)field % 4 == 0 ? Words[w] & 0xFFFF0000 | value : Words[w] & 0xFFFF | (uint)value << 16;
    }
}

/// <summary>Any *SD.DAT record without a dedicated parser; body after the common prefix kept as raw u32 words.</summary>
public sealed record SdRecord(SdCommon Common, uint[] Body)
{
    /// <summary>u32 at a record offset (>= 24), counted from the start of the record.</summary>
    public uint U32At(int offset) => Body[(offset - SdFile.CommonSize) / 4];

    public static (SdHeader Header, List<SdRecord> Records) Read(ReadOnlySpan<byte> file)
    {
        int count = (int)SdFile.U32(file, 4);
        int size = count == 0 ? SdFile.CommonSize : (file.Length - SdFile.HeaderSize) / count;
        if (size % 4 != 0) throw new InvalidDataException($"Record size {size} is not a multiple of 4.");
        return SdFile.Read(file, size, (c, r) =>
        {
            var body = new uint[r.Length / 4];
            for (int i = 0; i < body.Length; i++) body[i] = SdFile.U32(r, i * 4);
            return new SdRecord(c, body);
        });
    }
}
