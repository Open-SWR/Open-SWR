using OpenSWR.Data.Dat;

namespace OpenSWR.Data.Mods;

/// <summary>
/// Checks a definition set against what the game code assumes: a set that passes keeps every
/// mechanic the game code uses defined. Mods change data, not rules: a mod that needs another rule is refused here, never patched in the sim.
/// The install's own definitions pass (tested).
/// </summary>
public static class ModValidator
{
    /// <summary>
    /// SdRefs in moddable SD files that the game code names directly: they must exist (stats may change).
    /// The HQ type 0x20000001 is the record behind the fixed HQ object 0x20000005 (setup FACLHQTB).
    /// </summary>
    public static readonly HashSet<uint> Protected =
    [
        0x10000001, 0x10000002, 0x10000003, 0x10000004, 0x10000005, 0x10000006, 0x10000007, 0x10000008, 0x10000009, 0x1000000A,
        0x1400004A, 0x14000082, 0x20000001, 0x25000004, 0x2C000001, 0x2D000002,
        0x30000240, 0x31000241, 0x32000242, 0x33000243, 0x34000280, 0x35000281,
        0x38000341, 0x38000343, 0x38000344, 0x38000380, 0x38000381, 0x38000382, 0x38000383, 0x3800038C,
        0x3C000001, 0x3C000002, 0x3C000003, 0x3C000004, 0x3C000005, 0x3C000006, 0x3C000007, 0x3C000008, 0x3C000009,
        0x90000109, 0x92000121,
    ];

    const uint Coruscant = 0x90000109, Yavin = 0x92000121;

    /// <summary>Characters placed by fixed setup rules and the side they must belong to (1 Alliance, 2 Empire).</summary>
    static readonly (uint Ref, int Side)[] FixedCharacters =
    [
        (0x30000240, 1), (0x31000241, 1), (0x32000242, 1), (0x33000243, 1), (0x38000341, 1), (0x38000343, 1), (0x38000344, 1),
        (0x34000280, 2), (0x35000281, 2), (0x38000380, 2), (0x38000381, 2), (0x38000382, 2), (0x38000383, 2), (0x3800038C, 2),
    ];

    /// <summary>Families with an object class, per moddable SD file.</summary>
    static readonly Dictionary<string, uint[]> Registered = new()
    {
        ["TROOPSD"] = [0x10], ["CAPSHPSD"] = [0x14, 0x18], ["FIGHTSD"] = [0x1C], ["ALLFACSD"] = [0x20],
        ["DEFFACSD"] = [0x22, 0x23, 0x24, 0x25], ["MANFACSD"] = [0x28, 0x29, 0x2A], ["PROFACSD"] = [0x2C, 0x2D],
        ["MJCHARSD"] = [0x30, 0x31, 0x32, 0x33, 0x34, 0x35], ["MNCHARSD"] = [0x38], ["SPECFCSD"] = [0x3C],
        ["SECTORSD"] = [0x80], ["SYSTEMSD"] = [0x90, 0x92],
    };

    /// <summary>The producer pair a buildable record of the file may name; (0, 0) = not buildable is always allowed.</summary>
    static readonly Dictionary<string, (uint, uint)> Producer = new()
    {
        ["TROOPSD"] = (0x29, 0x2A), ["SPECFCSD"] = (0x29, 0x2A), ["CAPSHPSD"] = (0x28, 0x29), ["FIGHTSD"] = (0x28, 0x29),
        ["ALLFACSD"] = (0x2A, 0x2B), ["DEFFACSD"] = (0x2A, 0x2B), ["MANFACSD"] = (0x2A, 0x2B), ["PROFACSD"] = (0x2A, 0x2B),
    };

    /// <summary>The side whose units a seed table places: its items must be available to that side.</summary>
    static readonly Dictionary<string, int> SeedSide = new()
    {
        ["CMUNALTB"] = 1, ["CMUNYVTB"] = 1, ["CMUNHQTB"] = 1, ["CMUNAFTB"] = 1, ["FACLHQTB"] = 1,
        ["CMUNEMTB"] = 2, ["CMUNCRTB"] = 2, ["CMUNEFTB"] = 2, ["FACLCRTB"] = 2,
    };

    /// <param name="origins">Where a record (SdRef as 8 hex digits) or table (name, "G 1E32", "S 1400") was last changed; absent = the install.</param>
    public static List<string> Validate(GameDefinitions d, IReadOnlyDictionary<string, string>? origins = null)
    {
        var errors = new List<string>();
        string At(uint sdRef) => origins?.GetValueOrDefault(sdRef.ToString("X8")) ?? $"base:{sdRef:X8}";
        string Tb(string name) => origins?.GetValueOrDefault(name) ?? $"install {name}";
        void Err(uint sdRef, string field, string message) => errors.Add($"{At(sdRef)}: {field}: {message}");

        // Index unique per file, family registered and inside the file's range
        foreach (var (file, header, records) in d.SdFiles)
        {
            if (!Registered.TryGetValue(file, out var families)) continue;
            foreach (var g in records.GroupBy(r => r.Common.Index).Where(g => g.Count() > 1))
                Err(g.First().Common.Ref, "index", $"Index {g.Key} used {g.Count()} times in {file}.");
            foreach (var r in records)
                if (!families.Contains(r.Common.Family))
                    Err(r.Common.Ref, "family", $"0x{r.Common.Family:X2} has no object class in {file} (allowed: {string.Join(", ", families.Select(f => $"0x{f:X2}"))}).");
        }

        // Fixed references: records the game code names directly
        foreach (uint p in Protected)
            if (d.Resolve(p) is not { } r || r.Common.Family != new SdRef(p).Family)
                errors.Add($"base:{p:X8}: missing: the game code refers to this record directly.");
        foreach (var (p, side) in FixedCharacters)
            if (d.MajorCharacters.Concat(d.MinorCharacters).FirstOrDefault(c => c.Common.Ref == p) is { } c && (side == 1 ? (c.Alliance == 0 || c.Empire != 0) : (c.Empire == 0 || c.Alliance != 0)))
                Err(p, side == 1 ? "alliance" : "empire", $"placed by a fixed setup rule for the {(side == 1 ? "Alliance" : "Empire")}: keep alliance = {(side == 1 ? 1 : 0)}, empire = {(side == 1 ? 0 : 1)}.");

        Galaxy(d, errors, Err);
        Units(d, errors, Err);
        Tables(d, errors, Tb);
        return errors;
    }

    static void Galaxy(GameDefinitions d, List<string> errors, Action<uint, string, string> err)
    {
        var sectors = new Dictionary<uint, SectorRecord>();
        foreach (var s in d.Sectors)
        {
            sectors.TryAdd(s.Common.Index, s);
            if (s.Group is < 1 or > 3) err(s.Common.Ref, "group", "1 (core) or 2 / 3 (rim).");
            if (s.GalaxySize is < 1 or > 3) err(s.Common.Ref, "galaxySize", "1, 2 or 3 (the smallest galaxy size that has the sector).");
            if (s.X >= 1024 || s.Y >= 1024) err(s.Common.Ref, "x / y", "below 1024 (galaxy map).");
        }
        // At most 200 systems, Index 100-299 (the setup distance table)
        if (d.Systems.Count > 200) errors.Add($"SYSTEMSD: {d.Systems.Count} systems, at most 200.");
        foreach (var s in d.Systems)
        {
            uint r = s.Common.Ref;
            if (s.Common.Index is < 100 or > 299) err(r, "index", "system Index must be 100–299.");
            if (!sectors.TryGetValue(s.SectorIndex, out var sector)) { err(r, "sector", $"no sector with Index {s.SectorIndex}."); continue; }
            if ((sector.Group == 1) != (s.Common.Family == 0x90))
                err(r, "sector", $"family 0x{s.Common.Family:X2} in a sector of group {sector.Group}: group 1 sectors hold core systems (0x90) only, groups 2 / 3 rim systems (0x92) only.");
            if (s.X >= 1024 || s.Y >= 1024) err(r, "x / y", "below 1024 (galaxy map).");
            // sector window: a system lies in its sector's box of 1024/13 x 1024/10 units from the sector's x / y
            if (s.X < sector.X || s.Y < sector.Y || (s.X - sector.X) * 13 >= 1024 || (s.Y - sector.Y) * 10 >= 1024)
                err(r, "x / y", $"({s.X}, {s.Y}) must lie in its sector's box: x {sector.X}–{sector.X + 78}, y {sector.Y}–{sector.Y + 102}.");
            if (s.Unk1C is < 1 or > 200) err(r, "systemPicture", "1–200 (one of the install's system pictures).");
        }
        // the AI's and the advisor's sector records hold 10 system records: an 11th system would never be seen
        foreach (var g in d.Systems.Where(s => sectors.ContainsKey(s.SectorIndex)).GroupBy(s => s.SectorIndex).Where(g => g.Count() > 10))
            err(sectors[g.Key].Common.Ref, "systems", $"{g.Count()} systems in this sector, at most 10 (the computer player's sector records hold 10).");
        foreach (uint must in new[] { Coruscant, Yavin })
            if (d.Systems.FirstOrDefault(s => s.Common.Ref == must) is { } s && sectors.TryGetValue(s.SectorIndex, out var sec) && sec.GalaxySize != 1)
                err(must, "sector", "its sector must have galaxySize 1 (the system exists in every galaxy size).");

        // timer bases and divisors: a base below 1 puts a timer back in the queue already due (the day never ends) and a zero divisor
        // throws on the first use; a negative variance is nonsense. The limits are this project's checks, not the game's.
        foreach (var (id, min) in GeneralMinimums)
            foreach (var g in d.GeneralParams.Where(g => g.ParamId == id))
                for (int col = 1; col < g.Values.Length; col++) // column 0 is not a settings column
                    if (g.Values[col] < min) errors.Add($"general parameter 0x{id:X}, column {col}: {g.Values[col]} must be at least {min}.");
        // Setup steps 11 and 21 per galaxy size and settings column (columns 1-7 = side x difficulty, head to head)
        var side = d.SideParams.GroupBy(p => p.ParamId).ToDictionary(g => g.Key, g => g.First().Values);
        int S(uint id, int column, int s) => side.TryGetValue(id, out var v) ? v[2 * column + s - 1] : 0;
        var minors = d.MinorCharacters.Where(c => c.Common.Family == 0x38).ToList();
        for (int size = 1; size <= 3; size++)
        {
            var placed = d.Systems.Where(s => sectors.TryGetValue(s.SectorIndex, out var sec) && sec.GalaxySize <= size).ToList();
            int n = placed.Count(s => sectors[s.SectorIndex].Group == 1), rim = placed.Count - n;
            if (rim < 2) errors.Add($"galaxy size {size}: {rim} rim systems; setup needs the Alliance base (92000121) and at least one more (the headquarters draw).");
            if (n < 1) continue;
            for (int column = 1; column <= 7; column++)
            {
                // step 11 quotas: a, b (Alliance), c, d (Empire) = pct(N, S 0x1E00 / 0x1E01); the capital takes one of c
                int a = n * S(0x1E00, column, 1) / 100, b = n * S(0x1E01, column, 1) / 100, c = n * S(0x1E00, column, 2) / 100, e = n * S(0x1E01, column, 2) / 100;
                if (c < 1) errors.Add($"galaxy size {size}, settings column {column}: {n} core systems give the Empire a quota of {c}; the Empire capital (90000109) needs 1 (how setup handles a quota of 0 is not known).");
                if (a + b + c + e > n) errors.Add($"galaxy size {size}, settings column {column}: control quotas {a} + {b} + {c} + {e} exceed the {n} core systems (step 11).");
                foreach (int s in new[] { 1, 2 })
                {
                    int want = S(0x1400u + (uint)size - 1, column, s);
                    int free = minors.Count(m => (s == 1 ? m.Alliance != 0 && m.Empire == 0 : m.Empire != 0 && m.Alliance == 0) && !FixedCharacters.Any(f => f.Ref == m.Common.Ref));
                    if (free < want) errors.Add($"galaxy size {size}, settings column {column}: {(s == 1 ? "Alliance" : "Empire")} places {want} random minor characters but has {free} (how setup handles none left is not known).");
                }
            }
        }
    }

    static void Units(GameDefinitions d, List<string> errors, Action<uint, string, string> err)
    {
        foreach (var (file, _, records) in d.SdFiles)
        {
            if (!Producer.TryGetValue(file, out var pair)) continue;
            foreach (var r in records)
            {
                uint p = r.Common.Ref;
                if ((r.Common.ProducerFamilyMin, r.Common.ProducerFamilyMax) is var got && got != (0u, 0u) && got != pair)
                    err(p, "producer", $"{file} records are built by family 0x{pair.Item1:X2} (\"{(pair.Item1 == 0x28 ? "shipyard" : pair.Item1 == 0x29 ? "training" : "construction")}\") or none.");
                if (r.U32At(24) == 0 && r.U32At(28) == 0) err(p, "alliance / empire", "at least one side must have the unit.");
                if (r.U32At(44) > 100) err(p, "researchDifficulty", "0–100.");
            }
        }
        // Per side and producer family, each research order >= 1 is used at most once among buildable records
        var buildable = d.SdFiles.SelectMany(f => f.Records).Where(r => r.Body.Length >= 6 && r.U32At(32) != 0).ToList();
        foreach (int side in new[] { 1, 2 })
            foreach (uint f in new uint[] { 0x28, 0x29, 0x2A })
                foreach (var g in buildable.Where(r => r.Common.ProducerFamilyMin <= f && f < r.Common.ProducerFamilyMax && r.U32At(side == 1 ? 24 : 28) != 0 && r.U32At(40) >= 1)
                             .GroupBy(r => r.U32At(40)).Where(g => g.Count() > 1))
                    err(g.Last().Common.Ref, "researchOrder", $"order {g.Key} is used by {string.Join(", ", g.Select(r => r.Common.Ref.ToString("X8")))} for the {(side == 1 ? "Alliance" : "Empire")} at producer 0x{f:X2}; one record per order.");

        foreach (var s in d.CapitalShips.Where(s => s.Hull == 0)) err(s.Common.Ref, "hull", "must be > 0.");
        foreach (var s in d.Fighters.Where(s => s.UnkA0 == 0)) err(s.Common.Ref, "squadronSize", "must be > 0.");
        foreach (var f in d.Facilities.Where(f => f.ProcessingRate == 0)) err(f.Common.Ref, "processingRate", "must be > 0 (production cycle length).");
        foreach (var c in d.MajorCharacters.Concat(d.MinorCharacters))
        {
            if ((c.Alliance != 0) == (c.Empire != 0)) err(c.Common.Ref, "alliance / empire", "a character belongs to exactly one side.");
            if (c.JediProbability > 100) err(c.Common.Ref, "jediProbability", "0–100.");
        }
        var forces = d.SpecialForces;
        foreach (var s in forces)
        {
            if (s.MissionMask == 0) err(s.Common.Ref, "missionMask", "needs one bit of its own (missions open to it set that bit in their specialForceMask).");
            foreach (var o in forces.Where(o => o != s && o.SharesMissions(s) && o.Common.Index < s.Common.Index))
                err(s.Common.Ref, "missionMask", $"0x{s.MissionMask:X} shares bits with {o.Common.Ref:X8} (0x{o.MissionMask:X}); each force needs its own bit.");
        }
    }

    // timer base (first Start argument) >= 1, variance >= 0, divisors >= 1
    static readonly (uint Id, int Min)[] GeneralMinimums =
    [
        (0x1802, 1), (0x1803, 0), (0x1804, 1), (0x1805, 0), (0x1C00, 1), (0x1C01, 1), (0x1C02, 0), (0x1E0C, 1), (0x1E0E, 1), (0x1E12, 1),
        (0x1E13, 1), (0x1E14, 0), (0x1E15, 1), (0x1E16, 0), (0x1E17, 1), (0x1E18, 0), (0x1E25, 1), (0x1E26, 0), (0x1E27, 1), (0x1E28, 0),
        (0xA16, 1), (0xA17, 0), (0xA18, 1), (0xA19, 0),
        (0x1400, 1), (0x1420, 1), (0x180A, 1), (0x180B, 1),
    ];

    static void Tables(GameDefinitions d, List<string> errors, Func<string, string> at)
    {
        foreach (var (name, entries) in d.Tables)
        {
            for (int i = 1; i < entries.Count; i++)
                if (entries[i].Key <= entries[i - 1].Key) { errors.Add($"{at(name)}: {name} entries[{i}].key: keys must ascend."); break; }
            if (name is "SYFCCRTB" or "SYFCRMTB")
                foreach (var e in entries.Where(e => e.Value != 0))
                    if (d.Resolve((uint)e.Value) is null || new SdRef((uint)e.Value).Family is < 0x20 or >= 0x30)
                        errors.Add($"{at(name)}: {name} key {e.Key}: value {(uint)e.Value:X8} is not a facility type.");
        }
        // the side availability and maintenance cost of every unit type (the first record of a ref, as Resolve); a character's place for the cost is its Unk24
        var units = new Dictionary<uint, (uint Alliance, uint Empire, uint Maintenance)>();
        foreach (var u in d.CapitalShips) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.MaintenanceCost));
        foreach (var u in d.Fighters) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.MaintenanceCost));
        foreach (var u in d.Troops) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.MaintenanceCost));
        foreach (var u in d.Facilities) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.MaintenanceCost));
        foreach (var u in d.DefenseFacilities) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.MaintenanceCost));
        foreach (var u in d.SpecialForces) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.MaintenanceCost));
        foreach (var u in d.MajorCharacters.Concat(d.MinorCharacters)) units.TryAdd(u.Common.Ref, (u.Alliance, u.Empire, u.Unk24));
        foreach (var (name, entries) in d.SeedFamilies)
        {
            if (entries.Count == 0) { errors.Add($"{at(name)}: {name}: needs at least one entry."); continue; }
            for (int i = 1; i < entries.Count; i++)
                if (entries[i].Key <= entries[i - 1].Key) { errors.Add($"{at(name)}: {name} entries[{i}].key: keys must ascend."); break; }
            foreach (var e in entries)
            {
                string where = $"{at(name)}: {name} key {e.Key}";
                if (e.Items.Length == 0) { errors.Add($"{where}: items: at least the container slot."); continue; }
                // items[0] is the container of the other items (or null: they go to the target itself)
                uint first = e.Items[0].Ref.Value;
                if (first != 0 && e.Items.Length > 1 && new SdRef(first).Family is < 0x14 or >= 0x1C)
                    errors.Add($"{where}: items[0]: {first:X8} holds the other items, so it must be a capital ship type (or null).");
                for (int i = 0; i < e.Items.Length; i++)
                {
                    uint r = e.Items[i].Ref.Value;
                    if (i == 0 && r == 0) continue;
                    if (d.Resolve(r) is null || new SdRef(r).Family is < 0x10 or >= 0x40) { errors.Add($"{where}: items[{i}]: {r:X8} is not a unit type."); continue; }
                    if (SeedSide.TryGetValue(name, out int side) && units.TryGetValue(r, out var unit) && (side == 1 ? unit.Alliance : unit.Empire) == 0)
                        errors.Add($"{where}: items[{i}]: {r:X8} is not available to the {(side == 1 ? "Alliance" : "Empire")}.");
                }
                // the starting-unit budget loop subtracts each entry's cost and stops below zero
                if (name is "CMUNALTB" or "CMUNEMTB" && e.Unk0C * e.Items.Sum(it => units.TryGetValue(it.Ref.Value, out var unit) ? unit.Maintenance : 0) == 0)
                    errors.Add($"{where}: costs no maintenance; the starting-unit budget loop would not end.");
            }
        }
    }
}
