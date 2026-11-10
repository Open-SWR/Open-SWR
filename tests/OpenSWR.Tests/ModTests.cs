using System.Text.Json;
using OpenSWR.Data.Dat;
using OpenSWR.Data.Mods;
using OpenSWR.Sim;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Mod loader and validator, with the repo's test mod under Mods/testmod.</summary>
public class ModTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions, _modded;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static GameDefinitions Modded => _modded ??= ModLoader.Load(GameDir.Path, ModsDir, ["testmod"]);
    static string ModsDir => Path.Combine(AppContext.BaseDirectory, "Mods");

    /// <summary>Every definition as JSON (records, SD files, tables, mods, names, pictures).</summary>
    static string Dump(GameDefinitions d) => JsonSerializer.Serialize(new object[] { d.Sectors, d.Systems, d.CapitalShips, d.Fighters, d.Troops,
        d.Facilities, d.DefenseFacilities, d.MajorCharacters, d.MinorCharacters, d.SpecialForces, d.Missions,
        d.SdFiles.Select(f => new { f.File, f.Header, R = f.Records.Select(r => new { r.Common, r.Body }) }), d.Tables, d.SeedFamilies,
        d.GeneralParams, d.SideParams, d.Mods.Select(m => m.Id + " " + m.Version), d.Names, d.Pictures, d.Descriptions, d.Models, d.GalaxyPicture ?? "" }, new JsonSerializerOptions { IncludeFields = true });

    [GameFact]
    public void InstallDefinitionsPassTheValidator()
    {
        var errors = ModValidator.Validate(Definitions);
        foreach (var e in errors) output.WriteLine(e);
        Assert.Empty(errors);
    }

    [GameFact]
    public void NoModsIsTheInstall() => Assert.Equal(Dump(Definitions), Dump(ModLoader.Load(GameDir.Path, ModsDir, [])));

    [GameFact]
    public void AnEmptyModKeepsEveryRecord()
    {
        // the loader's own path (SD words gives bytes gives records) reproduces the install exactly; only the mod list differs
        string dir = Path.Combine(Path.GetTempPath(), "openswr-modtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "empty"));
            File.WriteAllText(Path.Combine(dir, "empty", "mod.json"), "{\"id\": \"empty\", \"version\": \"0\"}");
            File.WriteAllText(Path.Combine(dir, "empty", "defs.json"), "{\"records\": []}");
            var d = ModLoader.Load(GameDir.Path, dir, ["empty"]);
            Assert.Equal([("empty", "0")], d.Mods);
            Assert.Equal(Dump(Definitions), Dump(GameDefinitions.Build(GameDefinitions.LoadedSd.ToDictionary(f => f, f => Bytes(d, f)),
                new GameDefinitions.TbSet(d.Tables, d.SeedFamilies, d.GeneralParams, d.SideParams))));
            foreach (string f in GameDefinitions.LoadedSd) Assert.Equal(File.ReadAllBytes(Path.Combine(GameDir.Path, "GData", f + ".DAT")), Bytes(d, f));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>An SD file rebuilt from the definitions' generic records.</summary>
    static byte[] Bytes(GameDefinitions d, string file)
    {
        var (_, h, records) = d.SdFiles.Single(x => x.File == file);
        var bytes = new List<byte>();
        foreach (uint w in new[] { h.Unk00, (uint)h.Count, h.FamilyMin, h.FamilyMax }) bytes.AddRange(BitConverter.GetBytes(w));
        foreach (var r in records)
        {
            foreach (uint w in new[] { r.Common.Index, r.Common.Unk04, r.Common.ProducerFamilyMin, r.Common.ProducerFamilyMax, r.Common.Family }) bytes.AddRange(BitConverter.GetBytes(w));
            bytes.AddRange(BitConverter.GetBytes(r.Common.StringId));
            bytes.AddRange(BitConverter.GetBytes(r.Common.StringModule));
            foreach (uint w in r.Body) bytes.AddRange(BitConverter.GetBytes(w));
        }
        return [.. bytes];
    }

    [GameFact]
    public void TestModAddsRecords()
    {
        var d = Modded;
        Assert.Equal([("testmod", "1.0")], d.Mods);
        // a new sector (next free Index 40) with five systems, all rim (group 2)
        var drift = Assert.Single(d.Sectors, s => d.Names.GetValueOrDefault(s.Common.Ref) == "Ember Drift");
        Assert.Equal(SdRef.Make(Families.Sector, 40), drift.Common.Ref);
        Assert.Equal(GameDefinitions.ModStringModule, drift.Common.StringModule);
        Assert.Equal(20, d.Sectors.Count); // sector 21 and its ten systems removed to make room (at most 200 systems)
        Assert.Equal(195, d.Systems.Count);
        Assert.DoesNotContain(d.Sectors, s => s.Common.Index == 21);
        Assert.Equal(5, d.Systems.Count(s => s.SectorIndex == 40));
        Assert.All(d.Systems.Where(s => s.SectorIndex == Families.Shipyard), s => Assert.Equal(Families.RimSystem, s.Common.Family));
        var lantern = Assert.Single(d.Systems, s => d.Names.GetValueOrDefault(s.Common.Ref) == "Lantern");
        Assert.Equal((Families.RimSystem, 40u, (ushort)215), (lantern.Common.Family, lantern.SectorIndex, lantern.X));
        Assert.All(d.Systems, s => Assert.InRange(s.Common.Index, 100u, 299u));
        // a capital ship copied from capital ship index 65 with changed fields; weapon sums follow the arcs
        var warden = Assert.Single(d.CapitalShips, s => d.Names.GetValueOrDefault(s.Common.Ref) == "Warden Picket");
        var source = d.CapitalShips.Single(s => s.Common.Ref == SdRef.Make(Families.CapitalShip, 65));
        Assert.Equal(SdRef.Make(Families.CapitalShip, 143), warden.Common.Ref);
        Assert.Equal((900u, 400u, 12u, 30u), (warden.Hull, warden.ShieldStrength, warden.ResearchOrder, warden.Fore.Turbolaser));
        Assert.Equal(source.Maneuverability, warden.Maneuverability);
        Assert.Equal(warden.Fore.Turbolaser + warden.Aft.Turbolaser + warden.Port.Turbolaser + warden.Starboard.Turbolaser, warden.AttackStrength.Turbolaser);
        Assert.Equal(warden.AttackStrength.Turbolaser + warden.AttackStrength.IonCannon + warden.AttackStrength.LaserCannon, warden.TotalAttackStrength);
        Assert.Equal(900u, d.Resolve(warden.Common.Ref)!.U32At(152)); // generic record = typed record
        Assert.EndsWith("warden.png", d.Pictures[warden.Common.Ref]);
        Assert.EndsWith("warden.glb", d.Models[warden.Common.Ref]);
        Assert.StartsWith("A light picket ship", d.Descriptions[warden.Common.Ref]);
        var cinder = Assert.Single(d.Systems, s => d.Names.GetValueOrDefault(s.Common.Ref) == "Cinder");
        Assert.EndsWith("cinder.png", d.Pictures[cinder.Common.Ref]);
        Assert.EndsWith("galaxy.png", d.GalaxyPicture);
        Assert.Equal((607, 437), ModLoader.PngSize(d.GalaxyPicture!));
        var kestrel = Assert.Single(d.Fighters, s => d.Names.GetValueOrDefault(s.Common.Ref) == "Kestrel Interceptor");
        Assert.Equal((SdRef.Make(Families.Fighter, 9), 7u), (kestrel.Common.Ref, kestrel.Maneuverability));
        var varn = Assert.Single(d.MinorCharacters, s => d.Names.GetValueOrDefault(s.Common.Ref) == "Ilsa Varn");
        Assert.Equal((SdRef.Make(Families.MinorCharacter, 924), 1u, 0u, 60u, 1u), (varn.Common.Ref, varn.Alliance, varn.Empire, varn.Skills.Leadership.Base, varn.CanBeCommander));
        // seed table by id
        Assert.Equal([warden.Common.Ref, kestrel.Common.Ref, kestrel.Common.Ref], d.SeedFamilies["CMUNAFTB"][0].Items.Select(i => i.Ref.Value));
        Assert.Empty(ModValidator.Validate(d));
    }

    [GameFact]
    public void NewGameWithTheTestMod()
    {
        var d = Modded;
        uint warden = d.CapitalShips.Single(s => d.Names.GetValueOrDefault(s.Common.Ref) == "Warden Picket").Common.Ref;
        foreach (int size in new[] { 1, 2, 3 })
        {
            var game = NewGame.Create(new GameSettings(Owner.Alliance, size, 0, 0, 874334749), d);
            Assert.Contains(game.Sectors, s => s.Definition.Common.Index == 40);
            Assert.Equal(5, game.Systems.Count(s => s.Definition.Common.StringModule == GameDefinitions.ModStringModule));
            Assert.Contains(game.Characters, c => d.Names.GetValueOrDefault(c.Type) == "Ilsa Varn");
            output.WriteLine($"size {size}: {game.Sectors.Count} sectors, {game.Systems.Count} systems, wardens {game.Systems.SelectMany(All).Count(o => o.Type == warden)}");
        }
        var standard = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 874334749), d);
        Assert.Contains(standard.Systems.SelectMany(All), o => o.Type == warden); // the Alliance starting fleet (CMUNAFTB)
    }

    static IEnumerable<GameObject> All(GameObject o) => o.Contents.SelectMany(c => All(c).Prepend(c));

    [GameFact]
    public void SavesRecordTheMods()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 874334749), Modded);
        game.Clock.RunUntilDay(game, 6);
        byte[] bytes = Save(game);
        Assert.Contains("\"mods\":[{\"id\":\"testmod\",\"version\":\"1.0\"}]", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Equal(bytes, Save(SaveGame.Load(new MemoryStream(bytes), Modded)));
        var e = Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(bytes), Definitions));
        Assert.Contains("played with mods: testmod 1.0; active mods: none", e.Message);

        byte[] plain = Save(NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 874334749), Definitions));
        Assert.DoesNotContain("\"mods\"", System.Text.Encoding.UTF8.GetString(plain));
        e = Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(plain), Modded));
        Assert.Contains("played with mods: none; active mods: testmod 1.0", e.Message);
    }

    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    /// <summary>100 days computer against computer with the test mod (the soak harness: no exception, no livelock, expected refusals only, save gives load gives save at day 100).</summary>
    [GameTheory]
    [InlineData(874334749, 1)]
    [InlineData(532947196, 3)]
    public void SoakWithTheTestMod(int seed, int galaxy) => SoakTests.Run(output, Modded, seed, galaxy, 0, 100);

    /// <summary>Loads a throwaway mod (defs.json = <paramref name="records"/>, plus the given extra files); the folder is removed afterwards.</summary>
    static T WithMod<T>(string records, Func<string, T> load, params (string Path, byte[] Bytes)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "openswr-modtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "m"));
            File.WriteAllText(Path.Combine(dir, "m", "mod.json"), "{\"id\": \"m\", \"version\": \"1\"}");
            File.WriteAllText(Path.Combine(dir, "m", "defs.json"), $"{{\"records\": [{records}]}}");
            foreach (var (path, bytes) in files) File.WriteAllBytes(Path.Combine(dir, "m", path), bytes);
            return load(dir);
        }
        finally { Directory.Delete(dir, true); }
    }

    [GameFact]
    public void ANewRecordNeverReusesTheIndexOfARemovedOne()
    {
        // the highest-Index fighter goes, a new fighter comes: its Index must not be the removed one (records naming the removed one would silently point at it)
        var gone = Definitions.Fighters.MaxBy(f => f.Common.Index)!;
        string records = $$"""{"op": "remove", "type": "fighter", "id": "{{FamilyNames.BaseId(gone.Common.Ref)}}"}, {"op": "add", "type": "fighter", "id": "x", "from": "base:fighter:3", "name": "X"}""";
        var d = WithMod(records, dir => { try { return ModLoader.Load(GameDir.Path, dir, ["m"]); } catch (ModLoadException e) { output.WriteLine(e.Message); return null; } });
        Assert.NotNull(d); // refused when the removed fighter is still referenced: pick another record for this test
        Assert.DoesNotContain(d.Fighters, f => f.Common.Index == gone.Common.Index);
    }

    [GameFact]
    public void AModPictureLargerThanTheCapIsRefused()
    {
        // a PNG header alone (signature + IHDR) claiming 5000 x 5000: the size is read from the header before anything is decoded
        byte[] png = [137, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 19, 136, 0, 0, 19, 136];
        var e = WithMod("""{"op": "add", "type": "fighter", "id": "x", "from": "base:fighter:3", "name": "X", "picture": "big.png"}""",
            dir => Assert.Throws<ModLoadException>(() => ModLoader.Load(GameDir.Path, dir, ["m"])), ("big.png", png));
        Assert.Contains("larger than 4096 pixels", e.Message);
    }

    // ---- refusals: one temporary mod per case ----

    static ModLoadException Refused(string records, string? manifest = null, params (string Id, string Manifest, string Records)[] others)
    {
        string dir = Path.Combine(Path.GetTempPath(), "openswr-modtest-" + Guid.NewGuid().ToString("N"));
        void Write(string id, string m, string r)
        {
            Directory.CreateDirectory(Path.Combine(dir, id));
            File.WriteAllText(Path.Combine(dir, id, "mod.json"), m);
            File.WriteAllText(Path.Combine(dir, id, "defs.json"), $"{{\"records\": [{r}]}}");
        }
        try
        {
            foreach (var o in others) Write(o.Id, o.Manifest, o.Records);
            Write("bad", manifest ?? "{\"id\": \"bad\", \"version\": \"1\"}", records);
            return Assert.Throws<ModLoadException>(() => ModLoader.Load(GameDir.Path, dir, [.. others.Select(o => o.Id), "bad"]));
        }
        finally { Directory.Delete(dir, true); }
    }

    [GameTheory]
    // Families with an object class only; no new major characters, missions or HQ
    [InlineData("""{"op": "add", "type": "capitalShip", "id": "x", "name": "X", "family": 21}""", "bad defs.json: bad:x: family: capitalShip family must be one of capitalShip, deathStar")]
    [InlineData("""{"op": "add", "type": "majorCharacter", "id": "x", "name": "X"}""", "bad defs.json: x: majorCharacter records cannot be added")]
    [InlineData("""{"op": "add", "type": "mission", "id": "x", "name": "X"}""", "mission records cannot be added")]
    [InlineData("""{"op": "add", "type": "facility", "id": "x", "name": "X", "family": 32}""", "facility family must be one of ionCannon, gun, planetaryShield, deathStarShield, shipyard, trainingFacility, constructionYard, mine, refinery")]
    // Hard-coded references
    [InlineData("""{"op": "remove", "type": "system", "id": "base:coreSystem:265"}""", "bad defs.json: base:coreSystem:265: cannot be removed: the game code refers to base:coreSystem:265 directly")]
    [InlineData("""{"op": "remove", "type": "minorCharacter", "id": "base:minorCharacter:835"}""", "cannot be removed")]
    [InlineData("""{"op": "override", "type": "capitalShip", "id": "base:capitalShip:65", "family": 24}""", "bad defs.json: base:capitalShip:65: family: only allowed on add")]
    // At most 200 systems (the install has 200)
    [InlineData("""{"op": "add", "type": "system", "id": "x", "name": "X", "sector": "base:sector:22", "x": 330, "y": 650}""", "bad defs.json: bad:x: no free system Index left in 100–299")]
    // Group 1 <=> core systems; galaxy sizes 1-3; Coruscant's sector in every galaxy
    [InlineData("""{"op": "override", "type": "system", "id": "base:rimSystem:130", "sector": "base:sector:23"}""", "bad defs.json: base:rimSystem:130: sector: family rimSystem in a sector of group 1")]
    [InlineData("""{"op": "override", "type": "sector", "id": "base:sector:22", "galaxySize": 4}""", "bad defs.json: base:sector:22: galaxySize: 1, 2 or 3")]
    [InlineData("""{"op": "override", "type": "sector", "id": "base:sector:36", "galaxySize": 2}""", "base:coreSystem:265: sector: its sector must have galaxySize 1")]
    [InlineData("""{"op": "override", "type": "system", "id": "base:rimSystem:130", "x": 900}""", "base:rimSystem:130: x / y: (900, 678) must lie in its sector's box")]
    // Quotas
    [InlineData("""{"op": "override", "type": "sideParam", "id": "SetupLoyalShare", "value": 0}""", "galaxy size 1, settings column 1: 30 core systems give the Empire a quota of 0; the Empire capital (base:coreSystem:265) needs 1")]
    // timer bases and divisors: a base of 0 would never end the day, a divisor of 0 throws on the first move
    [InlineData("""{"op": "override", "type": "generalParam", "id": "DagobahDays", "value": 0}""", "general parameter DagobahDays, column 1: 0 must be at least 1")]
    [InlineData("""{"op": "override", "type": "generalParam", "id": "TravelDistanceDivisor", "value": 0}""", "general parameter TravelDistanceDivisor, column 1: 0 must be at least 1")]
    // Research orders, side flags, field ranges, cross-references
    [InlineData("""{"op": "add", "type": "capitalShip", "id": "x", "name": "X", "from": "base:capitalShip:64"}""", "researchOrder: order 2 is used by base:capitalShip:64, base:capitalShip:143 for the Alliance at producer shipyard")]
    [InlineData("""{"op": "add", "type": "minorCharacter", "id": "x", "name": "X", "alliance": 1, "empire": 1}""", "bad defs.json: bad:x: alliance / empire: a character belongs to exactly one side")]
    [InlineData("""{"op": "add", "type": "capitalShip", "id": "x", "name": "X", "alliance": 1}""", "bad defs.json: bad:x: hull: must be > 0")]
    [InlineData("""{"op": "add", "type": "specialForce", "id": "x", "name": "X", "alliance": 1, "missionMask": 1}""", "missionMask: 1 shares bits with base:specialForce:1")]
    [InlineData("""{"op": "override", "type": "system", "id": "base:rimSystem:130", "sector": "nowhere"}""", "bad defs.json: base:rimSystem:130: sector: no record 'bad:nowhere'")]
    [InlineData("""{"op": "override", "type": "sector", "id": "base:sector:22", "x": 70000}""", "bad defs.json: base:sector:22: x: a decimal JSON integer 0–65535")]
    [InlineData("""{"op": "override", "type": "sector", "id": "base:sector:22", "colour": 3}""", "bad defs.json: base:sector:22: colour: unknown field for SECTORSD records")]
    [InlineData("""{"op": "add", "type": "fighter", "id": "x", "from": "base:fighter:3", "name": "X", "picture": "missing.png"}""", "bad defs.json: bad:x: picture: file 'missing.png' not found")]
    [InlineData("""{"op": "add", "type": "fighter", "id": "x", "from": "base:fighter:3"}""", "bad defs.json: bad:x: name: required on add")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "picture": "defs.json"}""", "bad defs.json: base:fighter:3: picture: a .png path relative to the mod folder")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "picture": "../x.png"}""", "bad defs.json: base:fighter:3: picture: must lie inside the mod folder")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "model": "ship.obj"}""", "bad defs.json: base:fighter:3: model: a .glb or .gltf path relative to the mod folder")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "model": "ship.glb"}""", "bad defs.json: base:fighter:3: model: file 'ship.glb' not found")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "description": 5}""", "bad defs.json: base:fighter:3: description: a string")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:capitalShip:65", "hull": 5}""", "type: 'base:capitalShip:65' is not a fighter record")]
    // Tables
    [InlineData("""{"op": "replace", "type": "table", "id": "SYFCCRTB", "entries": [{"key": 50, "value": 0}, {"key": 10, "value": "base:mine:1"}]}""", "bad defs.json: SYFCCRTB entries[1].key: keys must ascend")]
    [InlineData("""{"op": "replace", "type": "table", "id": "SYFCCRTB", "entries": [{"key": 0, "value": "base:capitalShip:65"}]}""", "SYFCCRTB key 0: value base:capitalShip:65 is not a facility type")]
    [InlineData("""{"op": "replace", "type": "seedTable", "id": "CMUNALTB", "entries": [{"key": 1, "items": ["base:capitalShip:128"]}]}""", "CMUNALTB key 1: items[0]: base:capitalShip:128 is not available to the Alliance")]
    [InlineData("""{"op": "replace", "type": "seedTable", "id": "CMUNXXTB", "entries": [{"key": 1, "items": [null]}]}""", "id: no seedTable 'CMUNXXTB'")]
    [InlineData("""{"op": "remove", "type": "system", "id": "base:rimSystem:110"}, {"op": "add", "type": "system", "id": "x", "name": "X", "sector": "base:sector:22", "x": 330, "y": 650}""", "base:sector:22: systems: 11 systems in this sector, at most 10")]
    // Values of the wrong JSON kind and bad paths are record errors, never a crash
    [InlineData("""{"op": "override", "type": "capitalShip", "id": "base:capitalShip:65", "producer": 5}""", "bad defs.json: record 0:")]
    [InlineData("""{"op": "replace", "type": "table", "id": "SYFCCRTB", "entries": [{"key": "5", "value": 0}]}""", "bad defs.json: record 0:")]
    [InlineData("""{"op": "replace", "type": "table", "id": "SYFCCRTB", "entries": [{"key": 0, "value": true}]}""", "bad defs.json: record 0:")]
    [InlineData("""{"op": "replace", "type": "seedTable", "id": "CMUNALTB", "entries": [{"key": 1, "items": [true]}]}""", "bad defs.json: record 0:")]
    // The old hex forms (no 0x prefix either) and numbers written as strings are refused, with a message that says what to write
    [InlineData("""{"op": "add", "type": "capitalShip", "id": "x", "name": "X", "family": "15"}""", "family: capitalShip family must be one of capitalShip, deathStar")]
    [InlineData("""{"op": "add", "type": "fighter", "id": "x", "name": "X", "from": "base:14000041"}""", "'base:14000041' is not a record id; write base:<family>:<index>, e.g. base:capitalShip:65")]
    [InlineData("""{"op": "override", "type": "capitalShip", "id": "base:14000041", "hull": 5}""", "'base:14000041' is not a record id; write base:<family>:<index>, e.g. base:capitalShip:65")]
    [InlineData("""{"op": "override", "type": "generalParam", "id": "1E32", "value": 50}""", "no generalParam '1E32' in GNPRTB; write the parameter name, e.g. CorePopulatedChance, or its decimal id")]
    [InlineData("""{"op": "override", "type": "generalParam", "id": "0x1E32", "value": 50}""", "no generalParam '0x1E32' in GNPRTB")]
    [InlineData("""{"op": "override", "type": "sideParam", "id": "0x1400", "value": 5}""", "no sideParam '0x1400' in SDPRTB; write the parameter name, e.g. MinorCharactersSmall")]
    [InlineData("""{"op": "add", "type": "capitalShip", "id": "x", "name": "X", "family": "0x14"}""", "family: capitalShip family must be one of capitalShip, deathStar")]
    [InlineData("""{"op": "override", "type": "capitalShip", "id": "base:capitalShip:65", "hull": "0x10"}""", "hull: a decimal JSON integer 0")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "family": 24}""", "family: only allowed on add")]
    [InlineData("""{"op": "override", "type": "fighter", "id": "base:fighter:3", "picture": "a\u0000.png"}""", "bad defs.json: record 0:")]
    public void RefusesWithAClearError(string record, string expected)
    {
        var e = Refused(record);
        output.WriteLine(e.Message);
        Assert.Contains(e.Errors, x => x.Contains(expected));
    }

    [GameFact]
    public void ReportsABadBaseIdOnce()
    {
        // the id form error says what to write; a second "no record" error for the same id would only repeat it
        var e = Refused("""
            {"op": "override", "type": "capitalShip", "id": "base:14000041", "hull": 5},
            {"op": "add", "type": "fighter", "id": "x", "name": "X", "from": "base:14000041"},
            {"op": "replace", "type": "table", "id": "SYFCCRTB", "entries": [{"key": 0, "value": "base:14000041"}]}
            """);
        output.WriteLine(e.Message);
        Assert.Equal(3, e.Errors.Count(x => x.Contains("'base:14000041' is not a record id")));
        Assert.DoesNotContain(e.Errors, x => x.Contains("no record 'base:14000041'"));
    }

    [GameFact]
    public void RefusesManifestErrors()
    {
        Assert.Contains("bad mod.json: version: required", Refused("", "{\"id\": \"bad\"}").Message);
        Assert.Contains("bad mod.json: id: must be \"bad\"", Refused("", "{\"id\": \"other\", \"version\": \"1\"}").Message);
        Assert.Contains("bad mod.json: requires: mod 'core' is not enabled", Refused("", "{\"id\": \"bad\", \"version\": \"1\", \"requires\": [\"core\"]}").Message);
        Assert.Contains("bad mod.json: conflicts: mod 'core' is enabled too",
            Refused("", "{\"id\": \"bad\", \"version\": \"1\", \"conflicts\": [\"core\"]}", ("core", "{\"id\": \"core\", \"version\": \"1\"}", "")).Message);
        Assert.Contains("bad mod.json:", Refused("", "{\"id\": \"bad\", \"version\": \"1\", \"galaxyPicture\": \"g\\u0000.png\"}").Message); // NUL in a path
    }

    [GameFact]
    public void RefusesBadPictureFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), "openswr-modtest-" + Guid.NewGuid().ToString("N")), mod = Path.Combine(dir, "art");
        try
        {
            Directory.CreateDirectory(mod);
            File.WriteAllText(Path.Combine(mod, "fake.png"), "not a picture");
            File.Copy(Path.Combine(ModsDir, "testmod", "art", "cinder.png"), Path.Combine(mod, "square.png"));
            File.WriteAllText(Path.Combine(mod, "mod.json"), "{\"id\": \"art\", \"version\": \"1\", \"galaxyPicture\": \"square.png\"}");
            File.WriteAllText(Path.Combine(mod, "defs.json"), """{"records": [{"op": "override", "type": "fighter", "id": "base:fighter:3", "picture": "fake.png"}]}""");
            var e = Assert.Throws<ModLoadException>(() => ModLoader.Load(GameDir.Path, dir, ["art"]));
            Assert.Contains("art mod.json: galaxyPicture: 37×37 must have the galaxy's shape 607×437", e.Message);
            File.WriteAllText(Path.Combine(mod, "mod.json"), "{\"id\": \"art\", \"version\": \"1\"}");
            e = Assert.Throws<ModLoadException>(() => ModLoader.Load(GameDir.Path, dir, ["art"]));
            Assert.Contains("art defs.json: base:fighter:3: picture: 'fake.png' is not a PNG file.", e.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindsTheAvailableMods()
    {
        var found = Assert.Single(ModLoader.Available(ModsDir));
        Assert.Equal(("testmod", "openswr test content", "1.0"), (found.Id, found.Name, found.Version));
        Assert.EndsWith("galaxy.png", found.GalaxyPicture);
        Assert.Empty(ModLoader.Available(Path.Combine(ModsDir, "missing")));
    }

    [GameFact]
    public void ListsTheBaseIds()
    {
        var lines = ModLoader.BaseIds(GameDir.Path);
        var d = Definitions;
        Assert.Equal(d.Sectors.Count + d.Systems.Count + d.CapitalShips.Count + d.Fighters.Count + d.Troops.Count + d.Facilities.Count
            + d.DefenseFacilities.Count + d.MajorCharacters.Count + d.MinorCharacters.Count + d.SpecialForces.Count + d.Missions.Count, lines.Count);
        Assert.Matches("^base:capitalShip:65\tcapitalShip\t.+$", lines.Single(l => l.StartsWith("base:capitalShip:65\t")));
        Assert.Matches("^base:coreSystem:265\tsystem\t.+$", lines.Single(l => l.StartsWith("base:coreSystem:265\t")));
        Assert.All(lines, l => Assert.Matches("^base:[a-zA-Z]+:[0-9]+\t[a-zA-Z]+\t", l));
    }

    [Fact]
    public void BaseIdsRoundTrip()
    {
        // a record id is its family by name and its index in decimal; the family may also be written as its number
        Assert.Equal("base:capitalShip:65", FamilyNames.BaseId(SdRef.Make(Families.CapitalShip, 65)));
        Assert.Equal("base:coreSystem:265", FamilyNames.BaseId(SdRef.Make(Families.CoreSystem, 265)));
        Assert.Equal("base:sector:22", FamilyNames.BaseId(SdRef.Make(Families.Sector, 22)));
        Assert.Equal("base:espionage:19", FamilyNames.BaseId(SdRef.Make((uint)MissionKind.Espionage, 19)));
        Assert.Equal("base:200:5", FamilyNames.BaseId(SdRef.Make(200, 5))); // a family without a name: its number
        foreach (uint sdRef in new[] { SdRef.Make(Families.CapitalShip, 65), SdRef.Make(Families.CoreSystem, 265), SdRef.Make(Families.Sector, 22), SdRef.Make(200, 5) })
            Assert.True(FamilyNames.TryParseBaseId(FamilyNames.BaseId(sdRef), out uint back) && back == sdRef);
        Assert.True(FamilyNames.TryParseBaseId("base:20:65", out uint byNumber) && byNumber == SdRef.Make(Families.CapitalShip, 65));
        foreach (string bad in new[] { "base:14000041", "base:capitalShip:4A", "base:capitalShip:+65", "base:capitalShip:16777216", "base:nosuch:1", "base:capitalShip", "capitalShip:65" })
            Assert.False(FamilyNames.TryParseBaseId(bad, out _), bad);
    }

    [GameFact]
    public void ParameterOverrideByNameAndByNumber()
    {
        // the parameter named CorePopulatedChance is row 7730; the same row by its decimal id
        static int[] Row(GameDefinitions d) => d.GeneralParams.Single(p => p.ParamId == 7730).Values;
        var byName = WithMod("""{"op": "override", "type": "generalParam", "id": "CorePopulatedChance", "value": 50}""", dir => ModLoader.Load(GameDir.Path, dir, ["m"]));
        var byNumber = WithMod("""{"op": "override", "type": "generalParam", "id": "7730", "value": 50}""", dir => ModLoader.Load(GameDir.Path, dir, ["m"]));
        Assert.Equal(Enumerable.Repeat(50, Row(byName).Length), Row(byName));
        Assert.Equal(Row(byName), Row(byNumber));
    }

    [GameFact]
    public void LaterModsBuildOnEarlierOnes()
    {
        // a second mod overrides and removes records of the first, by their ids
        string dir = Path.Combine(Path.GetTempPath(), "openswr-modtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "second"));
            File.WriteAllText(Path.Combine(dir, "second", "mod.json"), "{\"id\": \"second\", \"version\": \"2\", \"requires\": [\"testmod\"]}");
            File.WriteAllText(Path.Combine(dir, "second", "defs.json"), """
                {"records": [
                  {"op": "override", "type": "capitalShip", "id": "testmod:warden", "hull": 1200, "name": "Warden Mk II"},
                  {"op": "remove", "type": "system", "id": "testmod:kiln"},
                  {"op": "override", "type": "generalParam", "id": "CorePopulatedChance", "values": [50, 50, 50, 50, 50, 50, 50, 50]}
                ]}
                """);
            foreach (var sub in Directory.GetDirectories(ModsDir)) CopyDir(sub, Path.Combine(dir, Path.GetFileName(sub)));
            var d = ModLoader.Load(GameDir.Path, dir, ["testmod", "second"]);
            var warden = d.CapitalShips.Single(s => s.Common.Ref == SdRef.Make(Families.CapitalShip, 143));
            Assert.Equal((1200u, "Warden Mk II"), (warden.Hull, d.Names[warden.Common.Ref]));
            Assert.Equal(194, d.Systems.Count);
            Assert.Equal(50, d.GeneralParams.Single(p => p.ParamId == 7730).Values[3]);
            Assert.Equal([("testmod", "1.0"), ("second", "2")], d.Mods);
            Assert.Contains("must come before", Assert.Throws<ModLoadException>(() => ModLoader.Load(GameDir.Path, dir, ["second", "testmod"])).Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
