using System.Text.Json;
using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;

namespace OpenSWR.Data.Mods;

// Format (openswr's own design, not a reverse-engineered mechanic). Limits: checked by ModValidator.

/// <summary>A mod set that cannot be loaded: every error found (mod, file, record id, field, reason).</summary>
public sealed class ModLoadException(IReadOnlyList<string> errors) : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>mods/&lt;id&gt;/mod.json.</summary>
/// <param name="GalaxyPicture">Absolute path of the mod's galaxy background PNG (mod.json "galaxyPicture"), or null.</param>
public sealed record ModManifest(string Id, string Name, string Version, string[] Requires, string[] Conflicts, string Dir, string? GalaxyPicture = null);

/// <summary>
/// Layers mods over the install's definitions: SD records (add / override / remove, field-level merge), TB tables (replace),
/// GNPRTB / SDPRTB parameters (override). Records get string ids: install records "base:&lt;family&gt;:&lt;index&gt;" (<see cref="FamilyNames.BaseId"/>), mod records
/// "&lt;mod&gt;:&lt;name&gt;"; cross-references go by id. New records take the next free Index of their SD file (systems: the lowest
/// free Index in 100-299). The result is checked by <see cref="ModValidator"/>. With no mod enabled this is
/// <see cref="GameDefinitions.Load"/> unchanged.
/// </summary>
public static class ModLoader
{
    /// <param name="modsDir">Folder holding one sub-folder per mod (named as the mod id).</param>
    /// <param name="enabled">Mod ids in load order.</param>
    /// <exception cref="ModLoadException">Any manifest, record or validation error.</exception>
    public static GameDefinitions Load(string gameDir, string modsDir, IReadOnlyList<string> enabled)
    {
        if (enabled.Count == 0) return GameDefinitions.Load(gameDir);
        var errors = new List<string>();
        var mods = ReadManifests(modsDir, enabled, errors);
        if (errors.Count > 0) throw new ModLoadException(errors);

        var set = new ModSet(GameDefinitions.ReadSd(gameDir), GameDefinitions.ReadTb(gameDir), errors);
        foreach (var mod in mods) set.Apply(mod);
        if (errors.Count > 0) throw new ModLoadException(errors);
        var (defs, origins) = set.Finish(mods);
        if (errors.Count > 0) throw new ModLoadException(errors);
        errors.AddRange(ModValidator.Validate(defs, origins));
        if (errors.Count > 0) throw new ModLoadException(errors);
        return defs;
    }

    /// <summary>Enabled ids from a comma / semicolon / whitespace separated list (env OPENSWR_MODS, settings [mods] enabled).</summary>
    public static List<string> ParseList(string? list) =>
        (list ?? "").Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToList();

    static readonly Regex IdPattern = new("^[a-z0-9_-]+$");

    /// <summary>
    /// Every mod folder (with a mod.json) in <paramref name="modsDir"/>, by folder name; a manifest that cannot be read keeps the
    /// folder name as id and name (loading it then reports the error). For the mods screen.
    /// </summary>
    public static List<ModManifest> Available(string modsDir) =>
        !Directory.Exists(modsDir) ? [] : [.. Directory.GetDirectories(modsDir).Select(d => Path.GetFileName(d))
            .Where(id => File.Exists(Path.Combine(modsDir, id, "mod.json"))).OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => ReadManifests(modsDir, [id], []).FirstOrDefault() ?? new ModManifest(id, id, "", [], [], Path.Combine(modsDir, id)))];

    /// <summary>Largest width or height of a mod PNG: it is decoded to RGBA in memory, and a header can claim any size.</summary>
    const int MaxPictureSide = 4096;

    /// <summary>
    /// A file named by a mod (picture, model, galaxy picture): a path relative to the mod folder with one of
    /// <paramref name="extensions"/>, inside the folder, existing; PNGs must start with the PNG signature. Null + an error otherwise.
    /// </summary>
    internal static string? ModFile(string modDir, JsonElement value, string[] extensions, string where, string field, List<string> errors)
    {
        string? rel = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? full = rel is null ? null : Path.GetFullPath(Path.Combine(modDir, rel));
        string root = Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (full is null || !extensions.Any(x => full.EndsWith(x, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"{where}: {field}: a {string.Join(" or ", extensions)} path relative to the mod folder.");
        else if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) errors.Add($"{where}: {field}: must lie inside the mod folder.");
        else if (!File.Exists(full)) errors.Add($"{where}: {field}: file '{rel}' not found.");
        else if (full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && PngSize(full) is null) errors.Add($"{where}: {field}: '{rel}' is not a PNG file.");
        else if (full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && Math.Max(PngSize(full)!.Value.Width, PngSize(full)!.Value.Height) > MaxPictureSide)
            errors.Add($"{where}: {field}: '{rel}' is larger than {MaxPictureSide} pixels on a side (it is decoded to memory).");
        else return full;
        return null;
    }

    /// <summary>Width and height from a PNG's IHDR chunk; null when the file is not a PNG.</summary>
    public static (int Width, int Height)? PngSize(string path)
    {
        byte[] head = new byte[24];
        using (var f = File.OpenRead(path))
            if (f.Read(head, 0, 24) < 24) return null;
        ReadOnlySpan<byte> signature = [137, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10];
        if (!head.AsSpan(0, 8).SequenceEqual(signature) || head[12] != 'I' || head[13] != 'H' || head[14] != 'D' || head[15] != 'R') return null;
        int w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16)), h = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20));
        return w > 0 && h > 0 ? (w, h) : null;
    }

    /// <summary>
    /// Every install record a mod can name, one line each: "base:&lt;family&gt;:&lt;index&gt;&lt;tab&gt;type&lt;tab&gt;name" (name from TEXTSTRA.DLL),
    /// in SD file order.
    /// </summary>
    public static List<string> BaseIds(string gameDir)
    {
        var defs = GameDefinitions.Load(gameDir);
        return ModSet.BaseIds(defs, StringTable.All(PeResources.Load(Path.Combine(gameDir, "TEXTSTRA.DLL"))));
    }

    static List<ModManifest> ReadManifests(string modsDir, IReadOnlyList<string> enabled, List<string> errors)
    {
        var list = new List<ModManifest>();
        foreach (string id in enabled)
        {
            if (!IdPattern.IsMatch(id) || id == "base") { errors.Add($"{id}: mod id must be lower-case letters, digits, '_' or '-' and not 'base'."); continue; }
            if (list.Any(m => m.Id == id)) { errors.Add($"{id}: enabled twice."); continue; }
            string dir = Path.Combine(modsDir, id), path = Path.Combine(dir, "mod.json");
            if (!File.Exists(path)) { errors.Add($"{id}: {path} not found."); continue; }
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = doc.RootElement;
                string? Str(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                string[] Arr(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
                    ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [];
                if (Str("id") != id) errors.Add($"{id} mod.json: id: must be \"{id}\" (the folder name).");
                string? version = Str("version");
                if (string.IsNullOrWhiteSpace(version)) errors.Add($"{id} mod.json: version: required (a string, e.g. \"1.0\").");
                string? galaxy = root.TryGetProperty("galaxyPicture", out var g) ? ModFile(dir, g, [".png"], $"{id} mod.json", "galaxyPicture", errors) : null;
                if (galaxy is not null && PngSize(galaxy) is var (w, h) && (long)w * 437 != (long)h * 607)
                    errors.Add($"{id} mod.json: galaxyPicture: {w}×{h} must have the galaxy's shape 607×437 (or a multiple, e.g. 1214×874).");
                list.Add(new ModManifest(id, Str("name") ?? id, version ?? "", Arr("requires"), Arr("conflicts"), dir, galaxy));
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException) { errors.Add($"{id} mod.json: {e.Message}"); }
        }
        for (int i = 0; i < list.Count; i++)
        {
            foreach (string r in list[i].Requires)
                if (list.FindIndex(m => m.Id == r) is var at && at < 0) errors.Add($"{list[i].Id} mod.json: requires: mod '{r}' is not enabled.");
                else if (at > i) errors.Add($"{list[i].Id} mod.json: requires: mod '{r}' must come before '{list[i].Id}' in the load order.");
            foreach (string c in list[i].Conflicts)
                if (list.Any(m => m.Id == c)) errors.Add($"{list[i].Id} mod.json: conflicts: mod '{c}' is enabled too.");
        }
        return list;
    }
}

/// <summary>A record field of a JSON definition: the SD field, u16 or u32, or a reference by id.</summary>
readonly record struct Field(SdField Name, bool U16 = false, bool Ref = false);

sealed record RecordType(string Name, string[] Files, uint[] AddFamilies, uint DefaultFamily, (uint Min, uint Max) Producer, bool Add, bool Remove);

sealed class ModSet
{
    const uint ModModule = GameDefinitions.ModStringModule;
    static readonly (uint, uint) None = (0, 0), Shipyard = (Families.Shipyard, Families.TrainingFacility), Training = (Families.TrainingFacility, Families.ConstructionYard), Construction = (Families.ConstructionYard, Families.ManufacturingEnd);

    static readonly RecordType[] Types =
    [
        new("sector", ["SECTORSD"], [Families.Sector], Families.Sector, None, true, true),
        new("system", ["SYSTEMSD"], [], 0, None, true, true), // family 144 / 146 follows its sector's Group
        new("capitalShip", ["CAPSHPSD"], [Families.CapitalShip, Families.DeathStar], Families.CapitalShip, Shipyard, true, true),
        new("fighter", ["FIGHTSD"], [Families.Fighter], Families.Fighter, Shipyard, true, true),
        new("troop", ["TROOPSD"], [Families.Troop], Families.Troop, Training, true, true),
        new("facility", ["DEFFACSD", "MANFACSD", "PROFACSD", "ALLFACSD"], [Families.IonCannon, Families.Gun, Families.PlanetaryShield, Families.DeathStarShield, Families.Shipyard, Families.TrainingFacility, Families.ConstructionYard, Families.Mine, Families.Refinery], 0, Construction, true, true),
        new("specialForce", ["SPECFCSD"], [Families.SpecialForce], Families.SpecialForce, Training, true, true),
        new("minorCharacter", ["MNCHARSD"], [Families.MinorCharacter], Families.MinorCharacter, None, true, true),
        new("majorCharacter", ["MJCHARSD"], [], 0, None, false, false),
        new("mission", ["MISSNSD"], [], 0, None, false, false),
    ];

    // the three weapon types: JSON name, then the fields of the four arcs, the range and the attack strength
    static readonly (string Name, SdField Fore, SdField Aft, SdField Port, SdField Starboard, SdField Range, SdField Attack)[] WeaponTypes =
    [
        ("turbolaser", SdField.ForeTurbolaser, SdField.AftTurbolaser, SdField.PortTurbolaser, SdField.StarboardTurbolaser, SdField.TurbolaserRange, SdField.TurbolaserAttackStrength),
        ("ionCannon", SdField.ForeIonCannon, SdField.AftIonCannon, SdField.PortIonCannon, SdField.StarboardIonCannon, SdField.IonCannonRange, SdField.IonCannonAttackStrength),
        ("laserCannon", SdField.ForeLaserCannon, SdField.AftLaserCannon, SdField.PortLaserCannon, SdField.StarboardLaserCannon, SdField.LaserCannonRange, SdField.LaserCannonAttackStrength),
    ];
    // initialised after WeaponTypes, which BuildFields reads
    static readonly Dictionary<string, Dictionary<string, Field>> Fields = BuildFields();

    /// <summary>See <see cref="ModLoader.BaseIds"/>: install records of the moddable SD files with their type and name.</summary>
    public static List<string> BaseIds(GameDefinitions d, IReadOnlyDictionary<int, string> text)
    {
        var lines = new List<string>();
        foreach (var (file, _, records) in d.SdFiles)
            if (Types.FirstOrDefault(t => t.Files.Contains(file)) is { } type)
                foreach (var r in records)
                    lines.Add($"{FamilyNames.BaseId(r.Common.Ref)}\t{type.Name}\t{(r.Common.StringModule == 2 ? text.GetValueOrDefault(r.Common.StringId, "") : "")}");
        return lines;
    }

    static Dictionary<string, Dictionary<string, Field>> BuildFields()
    {
        static Dictionary<string, Field> Of(params (string Name, SdField Field)[] fields) => fields.ToDictionary(f => f.Name, f => new Field(f.Field));
        static IEnumerable<(string, SdField)> Unit() =>
            [("alliance", SdField.Alliance), ("empire", SdField.Empire), ("refinedMaterialCost", SdField.RefinedMaterialCost),
             ("maintenanceCost", SdField.MaintenanceCost), ("researchOrder", SdField.ResearchOrder), ("researchDifficulty", SdField.ResearchDifficulty)];
        static IEnumerable<(string, SdField)> Weapons() =>
        [
            .. WeaponTypes.Select(w => ($"fore.{w.Name}", w.Fore)), .. WeaponTypes.Select(w => ($"aft.{w.Name}", w.Aft)),
            .. WeaponTypes.Select(w => ($"port.{w.Name}", w.Port)), .. WeaponTypes.Select(w => ($"starboard.{w.Name}", w.Starboard)),
            .. WeaponTypes.Select(w => ($"range.{w.Name}", w.Range)), .. WeaponTypes.Select(w => ($"attackStrength.{w.Name}", w.Attack)),
        ];
        static IEnumerable<(string, SdField)> Craft() =>
            [.. Unit(), ("unk30", SdField.CombatValue), ("unk34", SdField.Detection), ("shieldStrength", SdField.ShieldStrength), ("unk3C", SdField.Speed),
             ("maneuverability", SdField.Maneuverability), ("hyperdrive", SdField.Hyperdrive), ("hyperdriveIfDamaged", SdField.HyperdriveIfDamaged),
             .. Weapons(), ("totalAttackStrength", SdField.TotalAttackStrength)];
        static IEnumerable<(string, SdField)> Skills() =>
        [
            ("diplomacy.base", SdField.DiplomacyBase), ("diplomacy.variance", SdField.DiplomacyVariance),
            ("espionage.base", SdField.EspionageBase), ("espionage.variance", SdField.EspionageVariance),
            ("shipDesign.base", SdField.ShipDesignBase), ("shipDesign.variance", SdField.ShipDesignVariance),
            ("troopTraining.base", SdField.TroopTrainingBase), ("troopTraining.variance", SdField.TroopTrainingVariance),
            ("facilityDesign.base", SdField.FacilityDesignBase), ("facilityDesign.variance", SdField.FacilityDesignVariance),
            ("combat.base", SdField.CombatBase), ("combat.variance", SdField.CombatVariance),
            ("leadership.base", SdField.LeadershipBase), ("leadership.variance", SdField.LeadershipVariance),
            ("loyalty.base", SdField.LoyaltyBase), ("loyalty.variance", SdField.LoyaltyVariance),
        ];
        var character = Of([("alliance", SdField.Alliance), ("empire", SdField.Empire), ("unk20", SdField.CharacterUnk20), ("unk24", SdField.CharacterUnk24),
            ("unk28", SdField.CharacterUnk28), ("unk2C", SdField.CharacterUnk2C), .. Skills(),
            ("jediProbability", SdField.JediProbability), ("isKnownJedi", SdField.IsKnownJedi), ("jediLevel.base", SdField.JediLevelBase),
            ("jediLevel.variance", SdField.JediLevelVariance), ("canBeAdmiral", SdField.CanBeAdmiral), ("canBeCommander", SdField.CanBeCommander),
            ("canBeGeneral", SdField.CanBeGeneral), ("isUnableToBetray", SdField.IsUnableToBetray), ("isJediTrainer", SdField.IsJediTrainer)]);
        var producer = Of([.. Unit(), ("bombardmentDefense", SdField.FacilityBombardmentDefense), ("processingRate", SdField.ProcessingRate)]);
        return new()
        {
            ["SECTORSD"] = new()
            {
                ["group"] = new(SdField.SectorGroup), ["galaxySize"] = new(SdField.SectorGalaxySize),
                ["x"] = new(SdField.SectorX, U16: true), ["y"] = new(SdField.SectorY, U16: true),
            },
            ["SYSTEMSD"] = new()
            {
                ["sector"] = new(SdField.SystemSector, Ref: true), ["systemPicture"] = new(SdField.SystemPicture), ["unk20"] = new(SdField.SystemUnk20),
                ["x"] = new(SdField.SystemX, U16: true), ["y"] = new(SdField.SystemY, U16: true), ["unk28"] = new(SdField.SystemUnk28),
            },
            ["CAPSHPSD"] = Of([.. Craft(), ("hull", SdField.Hull), ("tractorBeamPower", SdField.TractorBeamPower), ("tractorBeamRange", SdField.TractorBeamRange),
                ("gravityWell", SdField.GravityWell), ("interdictionStrength", SdField.InterdictionStrength), ("bombardment", SdField.Bombardment),
                ("damageControl", SdField.DamageControl), ("weaponRechargeRate", SdField.WeaponRechargeRate), ("shieldRechargeRate", SdField.ShieldRechargeRate),
                ("fighterCapacity", SdField.FighterCapacity), ("troopCapacity", SdField.TroopCapacity), ("unkC4", SdField.CapitalShipUnkC4)]),
            ["FIGHTSD"] = Of([.. Craft(), ("unk98", SdField.Torpedoes), ("unk9C", SdField.TorpedoRange), ("squadronSize", SdField.SquadronSize),
                ("unkA4", SdField.FighterBombardment)]),
            ["TROOPSD"] = Of([.. Unit(), ("unk30", SdField.CombatValue), ("unk34", SdField.Detection), ("bombardmentDefense", SdField.TroopBombardmentDefense),
                ("attackStrength", SdField.TroopAttackStrength), ("defenseStrength", SdField.TroopDefenseStrength)]),
            ["ALLFACSD"] = Of([.. Unit(), ("bombardmentDefense", SdField.FacilityBombardmentDefense)]),
            ["MANFACSD"] = producer,
            ["PROFACSD"] = producer,
            ["DEFFACSD"] = Of([.. Unit(), ("bombardmentDefense", SdField.FacilityBombardmentDefense), ("attackStrength", SdField.DefenseAttackStrength),
                ("shieldStrength", SdField.DefenseShieldStrength)]),
            ["MJCHARSD"] = character,
            ["MNCHARSD"] = character,
            ["SPECFCSD"] = Of([.. Unit(), .. Skills(), ("missionMask", SdField.SpecialForceMissionMask)]),
            ["MISSNSD"] = Of(("alliance", SdField.Alliance), ("empire", SdField.Empire), ("specialForceMask", SdField.MissionSpecialForceMask)),
        };
    }

    static readonly string[] Reserved = ["op", "type", "id", "from", "name", "picture", "description", "model", "family", "producer"];

    readonly Dictionary<string, SdTable> _sd;
    readonly GameDefinitions.TbSet _tb;
    readonly List<string> _errors;
    readonly Dictionary<string, (string File, uint Index)> _ids = [];
    readonly Dictionary<(string File, uint Index), string> _names = [], _pictures = [], _descriptions = [], _models = [], _origin = [];
    readonly HashSet<(string File, uint Index)> _addedSystems = [];
    readonly List<(string File, uint Index, SdField Field, string Id, string Where)> _refs = [];
    readonly Dictionary<string, (string Type, List<RawEntry> Entries, string Where)> _tables = [];
    readonly Dictionary<string, string> _tableOrigin = [];

    /// <summary>A table entry before id resolution: Value / Items hold ids (string) or literal numbers.</summary>
    sealed record RawEntry(int Key, object? Value, uint Count, object?[] Items);

    public ModSet(Dictionary<string, byte[]> sd, GameDefinitions.TbSet tb, List<string> errors)
    {
        _sd = sd.ToDictionary(x => x.Key, x => SdTable.Read(x.Value));
        _tb = new GameDefinitions.TbSet(tb.Tables.ToDictionary(x => x.Key, x => x.Value.ToList()),
            tb.SeedFamilies.ToDictionary(x => x.Key, x => x.Value.ToList()), [.. tb.General], [.. tb.Side]);
        _errors = errors;
        foreach (string file in Fields.Keys)
            foreach (var r in _sd[file].Records) _ids[FamilyNames.BaseId(r.Ref)] = (file, r.Index);
    }

    uint RefOf((string File, uint Index) key) => _sd[key.File].Find(key.Index) is { } r ? r.Ref : 0;

    public void Apply(ModManifest mod)
    {
        foreach (string path in Directory.GetFiles(mod.Dir, "*.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string file = Path.GetRelativePath(mod.Dir, path).Replace('\\', '/');
            if (file == "mod.json") continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { _errors.Add($"{mod.Id} {file}: {e.Message}"); continue; }
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
                {
                    _errors.Add($"{mod.Id} {file}: expected an object {{\"records\": [ … ]}}.");
                    continue;
                }
                int i = 0;
                foreach (var e in records.EnumerateArray())
                {
                    // a value of the wrong JSON kind or a bad path inside a record: an error of this record, never a crash
                    try { ApplyRecord(mod, file, e, i); }
                    catch (Exception x) when (x is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
                    {
                        _errors.Add($"{mod.Id} {file}: record {i}: {x.Message}");
                    }
                    i++;
                }
            }
        }
    }

    void ApplyRecord(ModManifest mod, string file, JsonElement e, int position)
    {
        string? Str(string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string where = $"{mod.Id} {file}: record {position}";
        if (e.ValueKind != JsonValueKind.Object) { _errors.Add($"{where}: not a JSON object."); return; }
        string? op = Str("op"), typeName = Str("type"), rawId = Str("id");
        if (rawId is not null) where = $"{mod.Id} {file}: {rawId}";
        if (op is not ("add" or "override" or "remove" or "replace")) { _errors.Add($"{where}: op: must be add, override, remove or replace."); return; }
        if (typeName is "table" or "seedTable" or "generalParam" or "sideParam") { ApplyTable(mod, file, e, op, typeName, rawId, where); return; }
        var type = Types.FirstOrDefault(t => t.Name == typeName);
        if (type is null) { _errors.Add($"{where}: type: unknown '{typeName}' (sector, system, capitalShip, fighter, troop, facility, specialForce, minorCharacter, majorCharacter, mission, table, seedTable, generalParam, sideParam)."); return; }
        if (rawId is null) { _errors.Add($"{where}: id: required."); return; }

        if (op == "add")
        {
            if (!type.Add) { _errors.Add($"{where}: {type.Name} records cannot be added."); return; }
            string? id = NewId(mod, rawId, where);
            if (id is null) return;
            if (_ids.ContainsKey(id)) { _errors.Add($"{where}: id: '{id}' already exists."); return; }
            Add(mod, file, e, type, id, $"{mod.Id} {file}: {id}");
            return;
        }
        if (op == "replace") { _errors.Add($"{where}: op: replace is for tables; use add, override or remove."); return; }
        if (FullId(mod, rawId, where) is not { } full) return;
        if (!_ids.TryGetValue(full, out var key)) { _errors.Add($"{where}: id: no record '{full}' (removed, misspelt, or from a mod loaded later)."); return; }
        if (!type.Files.Contains(key.File)) { _errors.Add($"{where}: type: '{full}' is not a {type.Name} record."); return; }
        where = $"{mod.Id} {file}: {full}";
        if (op == "remove")
        {
            if (!type.Remove) { _errors.Add($"{where}: {type.Name} records cannot be removed."); return; }
            uint sdRef = RefOf(key);
            if (ModValidator.Protected.Contains(sdRef)) { _errors.Add($"{where}: cannot be removed: the game code refers to {FamilyNames.BaseId(sdRef)} directly."); return; }
            _sd[key.File].Records.Remove(_sd[key.File].Find(key.Index)!);
            _ids.Remove(full);
            _names.Remove(key); _pictures.Remove(key); _descriptions.Remove(key); _models.Remove(key); _origin.Remove(key); _addedSystems.Remove(key);
            _refs.RemoveAll(r => (r.File, r.Index) == key); // a later add may reuse the Index
            return;
        }
        foreach (string k in new[] { "from", "family" })
            if (e.TryGetProperty(k, out _)) { _errors.Add($"{where}: {k}: only allowed on add (changing a record's family would change its SdRef)."); return; }
        var words = _sd[key.File].Find(key.Index)!;
        if (e.TryGetProperty("producer", out var producer)) SetProducer(words, producer, where);
        SetFields(mod, e, key, words, where);
        _origin[key] = $"{mod.Id} {file}: {full}";
    }

    string? NewId(ModManifest mod, string raw, string where)
    {
        string local = raw.StartsWith(mod.Id + ":") ? raw[(mod.Id.Length + 1)..] : raw;
        if (local.Contains(':') || !Regex.IsMatch(local, "^[A-Za-z0-9_.-]+$"))
        {
            _errors.Add($"{where}: id: a new record's id is a plain name (letters, digits, '_', '.', '-'), optionally prefixed '{mod.Id}:'.");
            return null;
        }
        return $"{mod.Id}:{local}";
    }

    /// <summary>"base:capitalShip:65" (normalised), "othermod:name", or a plain name of the current mod. A bad base id is an error and gives null.</summary>
    string? FullId(ModManifest mod, string raw, string where)
    {
        if (raw.StartsWith("base:"))
        {
            if (FamilyNames.TryParseBaseId(raw, out uint v)) return FamilyNames.BaseId(v);
            _errors.Add($"{where}: '{raw}' is not a record id; write base:<family>:<index>, e.g. base:capitalShip:65 (family by name, index in decimal).");
            return null;
        }
        return raw.Contains(':') ? raw : $"{mod.Id}:{raw}";
    }

    void Add(ModManifest mod, string file, JsonElement e, RecordType type, string id, string where)
    {
        SdEditRecord? source = null;
        string? sourceFile = null;
        if (e.TryGetProperty("from", out var from))
        {
            string? fromId = from.ValueKind == JsonValueKind.String ? FullId(mod, from.GetString()!, where) : "";
            if (fromId is null) return;
            if (!_ids.TryGetValue(fromId, out var fk)) { _errors.Add($"{where}: from: no record '{fromId}' (it must be loaded before this record)."); return; }
            if (!type.Files.Contains(fk.File)) { _errors.Add($"{where}: from: '{fromId}' is not a {type.Name} record."); return; }
            (source, sourceFile) = (_sd[fk.File].Find(fk.Index), fk.File);
        }

        uint family = source?.Family ?? type.DefaultFamily;
        if (e.TryGetProperty("family", out var fam))
        {
            if (type.Name == "system") { _errors.Add($"{where}: family: a system's family follows its sector's group (1 → core coreSystem, 2 / 3 → rimSystem)."); return; }
            if (!TryFamily(fam, out family) || !type.AddFamilies.Contains(family))
            {
                _errors.Add($"{where}: family: {type.Name} family must be one of {string.Join(", ", type.AddFamilies.Select(FamilyNames.Name))}.");
                return;
            }
        }
        string? target = type.Name == "system" ? "SYSTEMSD"
            : GameDefinitions.LoadedSd.FirstOrDefault(f => _sd[f].Header.FamilyMin <= family && family < _sd[f].Header.FamilyMax && type.Files.Contains(f));
        if (target is null || (type.Name != "system" && !type.AddFamilies.Contains(family)))
        {
            _errors.Add($"{where}: family: required for a new {type.Name} (one of {string.Join(", ", type.AddFamilies.Select(FamilyNames.Name))}), or use from.");
            return;
        }
        if (sourceFile is not null && sourceFile != target) { _errors.Add($"{where}: from: '{sourceFile}' records have another layout than family {FamilyNames.Name(family)}."); return; }

        var table = _sd[target];
        uint index = target == "SYSTEMSD"
            ? Enumerable.Range(100, 200).Select(i => (uint)i).FirstOrDefault(i => table.Find(i) is null)
            : Math.Max(table.Peak, table.Records.Count == 0 ? 0 : table.Records.Max(r => r.Index)) + 1;
        if (index == 0) { _errors.Add($"{where}: no free system Index left in 100–299 (at most 200 systems)."); return; }

        var words = source?.Clone() ?? table.NewRecord();
        if (source is null)
        {
            words.Unk04 = 1; // always 1
            words.Producer = type.Producer;
            if (target == "SYSTEMSD")
            {
                words.Set(SdField.SystemPicture, 1); // Inferred default
                words.Set(SdField.SystemUnk20, 1); // always 1
            }
        }
        words.Index = index;
        words.Family = family;
        (words.StringId, words.StringModule) = (0, (ushort)ModModule);
        table.Records.Add(words);
        table.Peak = Math.Max(table.Peak, index);
        var key = (target, index);
        _ids[id] = key;
        _origin[key] = where;
        if (target == "SYSTEMSD") _addedSystems.Add(key);
        if (e.TryGetProperty("producer", out var producer)) SetProducer(words, producer, where);
        if (!e.TryGetProperty("name", out _)) _errors.Add($"{where}: name: required on add.");
        if (target == "SYSTEMSD" && source is null && !e.TryGetProperty("sector", out _)) _errors.Add($"{where}: sector: required on add.");
        SetFields(mod, e, key, words, where);
    }

    void SetProducer(SdEditRecord words, JsonElement v, string where)
    {
        (uint, uint)? pair = v.GetString() switch { "none" => None, "shipyard" => Shipyard, "training" => Training, "construction" => Construction, _ => null };
        if (pair is null) _errors.Add($"{where}: producer: one of none, shipyard, training, construction.");
        else words.Producer = pair.Value;
    }

    void SetFields(ModManifest mod, JsonElement e, (string File, uint Index) key, SdEditRecord words, string where)
    {
        var fields = Fields[key.File];
        var set = new HashSet<string>();
        foreach (var p in e.EnumerateObject())
        {
            if (Reserved.Contains(p.Name))
            {
                if (p.Name == "name")
                {
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Trim().Length > 0) _names[key] = p.Value.GetString()!.Trim();
                    else _errors.Add($"{where}: name: a non-empty string.");
                }
                else if (p.Name == "picture")
                {
                    if (ModLoader.ModFile(mod.Dir, p.Value, [".png"], where, "picture", _errors) is { } full) _pictures[key] = full;
                }
                else if (p.Name == "model")
                {
                    if (ModLoader.ModFile(mod.Dir, p.Value, [".glb", ".gltf"], where, "model", _errors) is { } full) _models[key] = full;
                }
                else if (p.Name == "description")
                {
                    if (p.Value.ValueKind == JsonValueKind.String) _descriptions[key] = p.Value.GetString()!;
                    else _errors.Add($"{where}: description: a string.");
                }
                continue;
            }
            foreach (var (name, value) in Flatten(p.Name, p.Value))
            {
                if (!fields.TryGetValue(name, out var f))
                {
                    _errors.Add($"{where}: {name}: unknown field for {key.File} records.");
                    continue;
                }
                set.Add(name);
                if (f.Ref)
                {
                    if (value.ValueKind != JsonValueKind.String) { _errors.Add($"{where}: {name}: a record id string."); continue; }
                    if (FullId(mod, value.GetString()!, where) is { } refId) _refs.Add((key.File, key.Index, f.Name, refId, $"{where}: {name}"));
                    continue;
                }
                if (!TryNumber(value, f.U16 ? 65535 : int.MaxValue, out uint n)) { _errors.Add($"{where}: {name}: a decimal JSON integer 0–{(f.U16 ? 65535 : int.MaxValue)}."); continue; }
                if (f.U16) words.SetU16(f.Name, (ushort)n);
                else words.Set(f.Name, n);
            }
        }
        // Weapon sums (shipped data: AttackStrength = sum of the four arcs, TotalAttackStrength = sum of AttackStrength) follow
        // the arcs when a mod sets an arc but not the sums.
        if (key.File is "CAPSHPSD" or "FIGHTSD" && set.Any(s => s.Split('.')[0] is "fore" or "aft" or "port" or "starboard"))
        {
            foreach (var w in WeaponTypes)
                if (!set.Contains($"attackStrength.{w.Name}"))
                    words.Set(w.Attack, words.Get(w.Fore) + words.Get(w.Aft) + words.Get(w.Port) + words.Get(w.Starboard));
            if (!set.Contains("totalAttackStrength"))
                words.Set(SdField.TotalAttackStrength, WeaponTypes.Aggregate(0u, (sum, w) => sum + words.Get(w.Attack)));
        }
    }

    static IEnumerable<(string, JsonElement)> Flatten(string name, JsonElement v) =>
        v.ValueKind == JsonValueKind.Object ? v.EnumerateObject().SelectMany(p => Flatten($"{name}.{p.Name}", p.Value)) : [(name, v)];

    /// <summary>A decimal JSON integer (or a boolean, as 0 / 1), 0 to max.</summary>
    static bool TryNumber(JsonElement v, long max, out uint n)
    {
        n = 0;
        long x;
        if (v.ValueKind == JsonValueKind.Number) { if (!v.TryGetInt64(out x)) return false; }
        else if (v.ValueKind is JsonValueKind.True or JsonValueKind.False) x = v.GetBoolean() ? 1 : 0;
        else return false;
        if (x < 0 || x > max) return false;
        n = (uint)x;
        return true;
    }

    /// <summary>A family of a record: its name (e.g. "capitalShip") or a decimal number 0 to 255.</summary>
    static bool TryFamily(JsonElement v, out uint family) =>
        v.ValueKind == JsonValueKind.String ? FamilyNames.TryFamilyName(v.GetString()!, out family) : TryNumber(v, 255, out family);


    void ApplyTable(ModManifest mod, string file, JsonElement e, string op, string type, string? id, string where)
    {
        if (id is null) { _errors.Add($"{where}: id: required (table name, e.g. \"SYFCCRTB\", or parameter name, e.g. \"HyperdriveModifier\", or its decimal id)."); return; }
        if (type is "generalParam" or "sideParam")
        {
            if (op != "override") { _errors.Add($"{where}: op: parameters can only be overridden."); return; }
            bool general = type == "generalParam";
            var list = general ? _tb.General : _tb.Side;
            uint pid;
            bool known = general ? ParamIds.TryParse<GeneralParam>(id, out pid) : ParamIds.TryParse<SideParam>(id, out pid);
            int at = known ? list.FindIndex(p => p.ParamId == pid) : -1;
            if (at < 0) { _errors.Add($"{where}: id: no {type} '{id}' in {(general ? "GNPRTB" : "SDPRTB")}; write the parameter name, e.g. {(general ? "CorePopulatedChance" : "MinorCharactersSmall")}, or its decimal id."); return; }
            var values = (int[])list[at].Values.Clone();
            if (e.TryGetProperty("value", out var one))
            {
                if (!TryNumber(one, int.MaxValue, out uint n)) { _errors.Add($"{where}: value: an integer ≥ 0."); return; }
                Array.Fill(values, (int)n);
            }
            else if (e.TryGetProperty("values", out var many) && many.ValueKind == JsonValueKind.Array && many.GetArrayLength() == values.Length)
            {
                int i = 0;
                foreach (var v in many.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out values[i])) { _errors.Add($"{where}: values[{i}]: an integer."); return; }
                    i++;
                }
            }
            else { _errors.Add($"{where}: value (every column) or values (array of {values.Length}) required."); return; }
            list[at] = list[at] with { Values = values };
            _tableOrigin[$"{(general ? "G" : "S")} {list[at].ParamId}"] = $"{mod.Id} {file}";
            return;
        }

        string name = id.ToUpperInvariant();
        bool seed = type == "seedTable";
        if (seed ? !_tb.SeedFamilies.ContainsKey(name) : !_tb.Tables.ContainsKey(name))
        {
            _errors.Add($"{where}: id: no {type} '{id}' ({string.Join(", ", (seed ? _tb.SeedFamilies.Keys.AsEnumerable() : _tb.Tables.Keys).Order())}).");
            return;
        }
        if (op != "replace") { _errors.Add($"{where}: op: tables can only be replaced as a whole."); return; }
        if (!e.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
        {
            _errors.Add($"{where}: entries: a non-empty array.");
            return;
        }
        var raw = new List<RawEntry>();
        int k = 0;
        foreach (var x in entries.EnumerateArray())
        {
            string at = $"{where}: entries[{k++}]";
            if (x.ValueKind != JsonValueKind.Object || !x.TryGetProperty("key", out var keyEl) || !keyEl.TryGetInt32(out int key))
            {
                _errors.Add($"{at}: key: an integer.");
                continue;
            }
            // a bad base id is already reported: 0 keeps the resolver from reporting it again
            object? Value(JsonElement v) => v.ValueKind == JsonValueKind.String ? (object?)FullId(mod, v.GetString()!, where) ?? 0
                : v.ValueKind == JsonValueKind.Null ? null : v.TryGetInt32(out int i) ? i : (object)"";
            if (seed)
            {
                uint count = 1;
                if (x.TryGetProperty("count", out var c) && !TryNumber(c, 1000, out count)) _errors.Add($"{at}: count: an integer 0–1000.");
                if (!x.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                {
                    _errors.Add($"{at}: items: a non-empty array (first item = container ship / fleet id, or null).");
                    continue;
                }
                raw.Add(new RawEntry(key, null, count, items.EnumerateArray().Select(Value).ToArray()));
            }
            else raw.Add(new RawEntry(key, x.TryGetProperty("value", out var v) ? Value(v) : 0, 0, []));
        }
        _tables[name] = (type, raw, where);
        _tableOrigin[name] = $"{mod.Id} {file}";
    }

    /// <summary>Resolves references, sets added systems' families, builds the definitions. Origins: record id (<see cref="FamilyNames.BaseId"/>) or table name gives where it was last changed.</summary>
    public (GameDefinitions, Dictionary<string, string>) Finish(IReadOnlyList<ModManifest> mods)
    {
        foreach (var (file, index, field, id, where) in _refs)
        {
            var words = _sd[file].Find(index);
            if (words is null) continue; // the referring record was removed later
            if (!_ids.TryGetValue(id, out var target)) { _errors.Add($"{where}: no record '{id}'."); continue; }
            if (target.File != "SECTORSD") { _errors.Add($"{where}: '{id}' is not a sector."); continue; }
            words.Set(field, target.Index);
        }
        var sectors = _sd["SECTORSD"].Records.ToDictionary(r => r.Index, r => r.Get(SdField.SectorGroup));
        foreach (var key in _addedSystems)
            if (_sd["SYSTEMSD"].Find(key.Index) is { } words)
                words.Family = sectors.TryGetValue(words.Get(SdField.SystemSector), out uint group) && group == 1 ? Families.CoreSystem : Families.RimSystem;

        uint Resolve(object? v, string where)
        {
            switch (v)
            {
                case null: return 0;
                case int i: return (uint)i;
                case string id when _ids.TryGetValue(id, out var key): return RefOf(key);
                default: _errors.Add($"{where}: no record '{v}'."); return 0;
            }
        }
        foreach (var (name, (type, entries, where)) in _tables)
            if (type == "seedTable")
                _tb.SeedFamilies[name] = entries.Select((x, i) => new SeedFamilyTableEntry((uint)i + 1, 1, x.Key, x.Count, 1,
                    x.Items.Select((it, j) => new SeedItem(1, 0, new SdRef(Resolve(it, $"{where}: entries[{i}].items[{j}]")))).ToArray())).ToList();
            else
                _tb.Tables[name] = entries.Select((x, i) => new TableEntry((uint)i + 1, 1, x.Key, (int)Resolve(x.Value, $"{where}: entries[{i}].value"))).ToList();

        var defs = GameDefinitions.Build(_sd.ToDictionary(x => x.Key, x => x.Value.ToBytes()), _tb,
            mods.Select(m => (m.Id, m.Version)).ToList(),
            _names.ToDictionary(x => RefOf(x.Key), x => x.Value), _pictures.ToDictionary(x => RefOf(x.Key), x => x.Value),
            _descriptions.ToDictionary(x => RefOf(x.Key), x => x.Value), _models.ToDictionary(x => RefOf(x.Key), x => x.Value),
            mods.LastOrDefault(m => m.GalaxyPicture is not null)?.GalaxyPicture);
        var origins = new Dictionary<string, string>(_tableOrigin);
        foreach (var (key, where) in _origin) origins[FamilyNames.BaseId(RefOf(key))] = where;
        return (defs, origins);
    }
}
