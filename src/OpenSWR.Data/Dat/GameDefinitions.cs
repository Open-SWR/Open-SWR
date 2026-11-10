namespace OpenSWR.Data.Dat;

/// <summary>All parsed *SD.DAT and *TB.DAT content of one install (or mod set). The simulation reads definitions only through this.</summary>
public sealed class GameDefinitions
{
    public required List<SectorRecord> Sectors { get; init; }
    public required List<SystemRecord> Systems { get; init; }
    public required List<CapitalShipRecord> CapitalShips { get; init; }
    public required List<FighterRecord> Fighters { get; init; }
    public required List<TroopRecord> Troops { get; init; }
    public required List<FacilityRecord> Facilities { get; init; }
    public required List<DefenseFacilityRecord> DefenseFacilities { get; init; }
    public required List<CharacterRecord> MajorCharacters { get; init; }
    public required List<CharacterRecord> MinorCharacters { get; init; }
    public required List<SpecialForceRecord> SpecialForces { get; init; }
    public required List<MissionRecord> Missions { get; init; }

    /// <summary>Every loaded SD file as generic records with its header, for SdRef resolution.</summary>
    public required List<(string File, SdHeader Header, List<SdRecord> Records)> SdFiles { get; init; }

    /// <summary>IntTableEntry / SeedTableEntry tables by file name without extension, e.g. "SYFCCRTB".</summary>
    public required Dictionary<string, List<TableEntry>> Tables { get; init; }
    public required Dictionary<string, List<SeedFamilyTableEntry>> SeedFamilies { get; init; }
    public required List<ParamTableEntry> GeneralParams { get; init; }
    public required List<ParamTableEntry> SideParams { get; init; }

    /// <summary>Active mods in load order (id, version); empty for the plain install.</summary>
    public IReadOnlyList<(string Id, string Version)> Mods { get; init; } = [];

    /// <summary>Display names set by mods, by record SdRef; they win over the record's StringId.</summary>
    public IReadOnlyDictionary<uint, string> Names { get; init; } = new Dictionary<uint, string>();

    /// <summary>PNG pictures set by mods (absolute paths), by record SdRef: drawn in place of the record's install picture.</summary>
    public IReadOnlyDictionary<uint, string> Pictures { get; init; } = new Dictionary<uint, string>();

    /// <summary>Encyclopedia texts set by mods, by record SdRef.</summary>
    public IReadOnlyDictionary<uint, string> Descriptions { get; init; } = new Dictionary<uint, string>();

    /// <summary>glTF models set by mods (absolute paths), by record SdRef; stored for the tactical phase, not drawn.</summary>
    public IReadOnlyDictionary<uint, string> Models { get; init; } = new Dictionary<uint, string>();

    /// <summary>Galaxy background PNG of the last enabled mod that has one (absolute path), or null = the install's bitmap.</summary>
    public string? GalaxyPicture { get; init; }

    /// <summary>StringModule of records added by mods: no install string and no install picture.</summary>
    public const ushort ModStringModule = 240;

    /// <summary>The 17 SD files the game loads (BASICSD.DAT is never loaded).</summary>
    public static readonly string[] LoadedSd =
        ["ABODESD", "ALLFACSD", "CAPSHPSD", "DEFFACSD", "FIGHTSD", "FLEETSD", "MJCHARSD", "MANFACSD", "MANMGRSD",
         "MNCHARSD", "MISSNSD", "PROFACSD", "SECTORSD", "SPECFCSD", "SYSTEMSD", "TROOPSD", "UNIQUESD"];

    /// <summary>The parsed *TB.DAT content of an install.</summary>
    public sealed record TbSet(Dictionary<string, List<TableEntry>> Tables, Dictionary<string, List<SeedFamilyTableEntry>> SeedFamilies,
        List<ParamTableEntry> General, List<ParamTableEntry> Side);

    public static GameDefinitions Load(string gameDir) => Build(ReadSd(gameDir), ReadTb(gameDir));

    /// <summary>Raw bytes of the loaded SD files by name (without extension).</summary>
    public static Dictionary<string, byte[]> ReadSd(string gameDir) =>
        LoadedSd.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(gameDir, "GData", f + ".DAT")));

    public static TbSet ReadTb(string gameDir)
    {
        var tables = new Dictionary<string, List<TableEntry>>();
        var families = new Dictionary<string, List<SeedFamilyTableEntry>>();
        List<ParamTableEntry> general = [], side = [];
        foreach (var path in Directory.GetFiles(Path.Combine(gameDir, "GData"), "*TB.DAT"))
        {
            var bytes = File.ReadAllBytes(path);
            string name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            switch (TbFile.EntryType(bytes))
            {
                case "SeedFamilyTableEntry": families[name] = TbFile.ReadSeedFamilies(bytes); break;
                case "GeneralParamTableEntry": general = TbFile.ReadParams(bytes); break;
                case "SideParamTableEntry": side = TbFile.ReadParams(bytes); break;
                default: tables[name] = TbFile.ReadTable(bytes); break;
            }
        }
        return new TbSet(tables, families, general, side);
    }

    /// <summary>Definitions from SD file bytes (every name of <see cref="LoadedSd"/>) and parsed tables.</summary>
    public static GameDefinitions Build(IReadOnlyDictionary<string, byte[]> sd, TbSet tb,
        IReadOnlyList<(string Id, string Version)>? mods = null, IReadOnlyDictionary<uint, string>? names = null,
        IReadOnlyDictionary<uint, string>? pictures = null, IReadOnlyDictionary<uint, string>? descriptions = null,
        IReadOnlyDictionary<uint, string>? models = null, string? galaxyPicture = null)
    {
        byte[] Dat(string name) => sd[name];
        return new GameDefinitions
        {
            Sectors = SectorRecord.Read(Dat("SECTORSD")),
            Systems = SystemRecord.Read(Dat("SYSTEMSD")),
            CapitalShips = CapitalShipRecord.Read(Dat("CAPSHPSD")),
            Fighters = FighterRecord.Read(Dat("FIGHTSD")),
            Troops = TroopRecord.Read(Dat("TROOPSD")),
            Facilities = [.. FacilityRecord.Read(Dat("ALLFACSD")), .. FacilityRecord.Read(Dat("MANFACSD")), .. FacilityRecord.Read(Dat("PROFACSD"))],
            DefenseFacilities = DefenseFacilityRecord.Read(Dat("DEFFACSD")),
            MajorCharacters = CharacterRecord.Read(Dat("MJCHARSD")),
            MinorCharacters = CharacterRecord.Read(Dat("MNCHARSD")),
            SpecialForces = SpecialForceRecord.Read(Dat("SPECFCSD")),
            Missions = MissionRecord.Read(Dat("MISSNSD")),
            SdFiles = LoadedSd.Select(f => { var (h, r) = SdRecord.Read(Dat(f)); return (f, h, r); }).ToList(),
            Tables = tb.Tables,
            SeedFamilies = tb.SeedFamilies,
            GeneralParams = tb.General,
            SideParams = tb.Side,
            Mods = mods ?? [],
            Names = names ?? new Dictionary<uint, string>(),
            Pictures = pictures ?? new Dictionary<uint, string>(),
            Descriptions = descriptions ?? new Dictionary<uint, string>(),
            Models = models ?? new Dictionary<uint, string>(),
            GalaxyPicture = galaxyPicture,
        };
    }

    /// <summary>
    /// Resolves an SdRef: the first loaded file whose half-open family range contains the family byte, then the record
    /// with that Index. The record's own Family is not compared.
    /// </summary>
    public SdRecord? Resolve(uint sdRef)
    {
        uint family = sdRef >> 24, index = sdRef & 16777215;
        // per-file Index maps built on first use (hot path: maintenance sums); the first record of an Index wins, as a scan would.
        // shortcut: assumes the SD files no longer change after the first Resolve (true: mods build a new GameDefinitions from
        // their own tables), upgrade (reset _byIndex) if anything ever edits SdFiles in place
        _byIndex ??= [.. SdFiles.Select(f =>
        {
            var map = new Dictionary<uint, SdRecord>();
            foreach (var r in f.Records) map.TryAdd(r.Common.Index, r);
            return map;
        })];
        for (int i = 0; i < SdFiles.Count; i++)
            if (family >= SdFiles[i].Header.FamilyMin && family < SdFiles[i].Header.FamilyMax)
                return _byIndex[i].GetValueOrDefault(index);
        return null;
    }

    Dictionary<uint, SdRecord>[]? _byIndex;
}
