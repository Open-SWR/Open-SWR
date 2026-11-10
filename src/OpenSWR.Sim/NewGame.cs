using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>RNG draw sites: the original function that draws, as a trace labels it.</summary>
public static class DrawSite
{
    public const string Timer = "0x586130", Allocation = "0x519d00",
        CorePopulated = "0x5596f0", CoreSupport = "0x559700", CoreEnergy = "0x559670", CoreRaw = "0x5596a0",
        RimPopulated = "0x559a10", RimSupport = "0x559a20", RimEnergy = "0x559970", RimRaw = "0x5599c0",
        CoreFacility = "0x559850", RimFacility = "0x559a60", Cycle = "0x55e2b0",
        AllianceFleet = "0x55d500", AllianceUnits = "0x55d730", EmpireUnits = "0x55d780", UnitSystem = "0x51b1f0",
        Location = "0x562710", Minor = "0x55fc80", Skills = "0x535e40", Jedi = "0x4f11f0", ForceRank = "0x4eee60",
        // Draws after setup (verified).
        Smuggling = "0x5166a0", Autoscrap = "0x530350", Incident = "0x559f90", SystemEventA = "0x556b50", SystemEventB = "0x556be0",
        BountyHunters = "0x546fa0", Withdraw = "0x504990",
        // Missions. The loyalty check is traced under another function name.
        Loyalty = "0x55e4d0", Recruitment = "0x56b9a0", Diplomacy = "0x5740a0", Espionage = "0x573540", Incite = "0x571a60",
        Subdue = "0x569c20", Rescue = "0x56ae50", Abduction = "0x576eb0", Assassination = "0x576620", Sabotage = "0x56a300",
        DeathStarSabotage = "0x574630", Research = "0x56cdb0", ResearchPoints = "0x55c9f0", SupportDelta = "0x55cac0",
        EspionageReveal = "0x55c940", Injury = "0x53e990", ShipLossInjury = "0x53ea00", RevealPick = "0x573170", Wound = "0x53eae0", Death = "0x53eb30",
        // Uprisings. Never traced.
        Informant = "0x559db0", UprisingRoll = "0x559ce0", IncidentPick = "0x559b80", CharacterPick = "0x50d150", SubdueDelta = "0x55cb10",
        // Encounters. Both picks are traced under one function name.
        EncounterCheck = "0x53e340", DecoyPick = "0x5885f0", UnitPick = "0x5885f0",
        // Escape. Never traced.
        Escape = "0x53e340",
        // Force detection. Never exercised in runs 1-3. The wrapper sites are not trace-checked.
        ForceDetect = "0x53e2f0", ForcePick = "0x53e290",
        // Galaxy resource events. Disaster: Inferred label (the draw site is not given).
        Resources = "0x559ee0", Losses = "0x559e10", EnergyOverflow = "0x50ad80", Disaster = "0x511930",
        // Combat. Never traced: the function holding the draw. Inferred for the pick sites.
        AssaultFire = "0x53e2f0", AssaultPick = "0x58c3c0", DuelPick = "0x58c500", Duel = "0x55eb00", Collateral = "0x55eb80",
        CollateralPick = "0x55ebc0", BombardFire = "0x58dc10", BombardShot = "0x58df60", BombardFacility = "0x58d080", BombardHit = "0x55e970",
        // Critical-hit roll (G 0x801) and its CSCRHTTB roll (G 0x803, Inferred).
        CriticalHit = "0x53e1b0", CriticalKind = "0x5585e0",
        // Jedi and story. Never traced: the function holding the draw.
        JediTraining = "0x5712b0", JediGain = "0x55ca20", DuelWound = "0x53ea70", HeritageWound = "0x55e810", Palace = "0x56e650",
        FinalBattle = "0x55c650",
        // Strategic repair (traced in run 9): chance(DamageControl) and the critical-hit pick roll(n - 1).
        RepairCheck = "0x558680", RepairPick = "0x501c50",
        // Death Star. Never traced.
        PlanetMinor = "0x55e9e0",
        // Identify traitor. Never traced.
        Traitor = "0x55e550";
}

/// <summary>
/// Builds the start state of a new game, in the order of the original setup.
/// </summary>
public static class NewGame
{
    /// <param name="trace">When given, receives every RNG call as (site, n, result) (see <see cref="GameRandom.Trace"/>).</param>
    /// <param name="timers">When given, receives every timer start and firing (see <see cref="Clock.TimerTrace"/>).</param>
    public static GameState Create(GameSettings settings, GameDefinitions definitions, List<(string Site, int N, int Result)>? trace = null,
        List<TimerRecord>? timers = null)
    {
        if (settings.GalaxySize is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(settings), "Galaxy size must be 1–3.");
        var game = new GameState(settings, definitions);
        game.Random.Trace = trace;
        game.Clock.TimerTrace = timers;
        UniqueObjectTimers(game);
        CreateSectors(game);
        var lists = AllocateControl(game);
        InitSystems(game, lists);
        foreach (var system in game.Systems) RandomFacilities(game, system);
        FixedFacilities(game);
        Economy.RecomputeSideTotals(game);
        StartingUnits(game);
        Economy.RecomputeSideTotals(game);
        Characters(game);
        RegisterFacilities(game);
        ProductionModifiers(game);
        Knowledge.Setup(game);
        game.Ready = true;
        return game;
    }

    // Unique object timers.
    static void UniqueObjectTimers(GameState game)
    {
        var p = game.Params;
        TimerCatalogue.Start(game, 0x392, 0xF8000006, null, p.General(0x1802), p.General(0x1803));
        TimerCatalogue.Start(game, 0x393, 0xF8000006, null, p.General(0x1804), p.General(0x1805));
        game.StoryFlags.BountyHuntersActive = true; // its hook sets the 0x393 slot Active; the start has already set it
        TimerCatalogue.Start(game, 0x38F, 0xF9000007, null, p.General(0x1E25), p.General(0x1E26));
        TimerCatalogue.Start(game, 0x390, 0xF9000007, null, p.General(0x1E27), p.General(0x1E28));
        foreach (var side in new[] { game.Sides[Owner.Alliance], game.Sides[Owner.Empire] })
            TimerCatalogue.Start(game, 0x381, side.Ref, side, p.General(0x1C01), p.General(0x1C02));
    }

    static void CreateSectors(GameState game)
    {
        foreach (var sector in game.Definitions.Sectors.OrderBy(s => s.Common.Index))
            if (sector.GalaxySize <= game.Settings.GalaxySize) game.Sectors.Add(new Sector(sector));
    }

    sealed record Lists(List<SystemRecord> A, List<SystemRecord> B, List<SystemRecord> C, List<SystemRecord> D,
        List<SystemRecord> E, List<SystemRecord> Rim, SystemRecord Hq);

    // Core systems go into control buckets A to E by share. Rim systems stay in a list, and one rim system becomes the Alliance HQ.
    static Lists AllocateControl(GameState game)
    {
        var sectors = game.Sectors.ToDictionary(s => s.Definition.Common.Index);
        var core = new List<SystemRecord>();
        var rim = new List<SystemRecord>();
        foreach (var system in game.Definitions.Systems.OrderBy(s => s.Common.Index))
            if (sectors.TryGetValue(system.SectorIndex, out var sector))
                (sector.IsCore ? core : rim).Add(system);

        var p = game.Params;
        int n = core.Count, r = rim.Count;
        // Sets both sides' cost scale to the number of systems placed (only if > 0).
        if (n + r > 0) foreach (var side in game.Sides.Values) side.CostScale = n + r;
        int a = Pct(n, p.Side(0x1E00, Owner.Alliance)), b = Pct(n, p.Side(0x1E01, Owner.Alliance));
        int c = Pct(n, p.Side(0x1E00, Owner.Empire)), d = Pct(n, p.Side(0x1E01, Owner.Empire));
        int e = n - a - b - c - d;

        core.RemoveAll(s => s.Common.Ref == Refs.Coruscant);
        rim.RemoveAll(s => s.Common.Ref == Refs.Yavin);
        var hq = rim[game.Random.Roll(r - 2, DrawSite.Allocation)];
        rim.Remove(hq);

        var buckets = new (List<SystemRecord> List, int Room)[] { ([], a), ([], b), ([], c - 1), ([], d), ([], e) };
        for (int k = n - 2; k >= 0; k--)
        {
            var system = core[game.Random.Roll(k, DrawSite.Allocation)];
            core.Remove(system);
            var bucket = buckets.First(x => x.List.Count < x.Room);
            bucket.List.Add(system);
        }
        // Buckets are processed in item id order = SYSTEMSD Index order, not draw order.
        var sorted = buckets.Select(x => x.List.OrderBy(s => s.Common.Index).ToList()).ToArray();
        return new Lists(sorted[0], sorted[1], sorted[2], sorted[3], sorted[4], rim, hq);
    }

    // Sets up each system: owner, managers, class init and timers.
    static void InitSystems(GameState game, Lists lists)
    {
        var sectors = game.Sectors.ToDictionary(s => s.Definition.Common.Index);
        var created = new List<StarSystem>();
        // 126 ids 0x482-0x4FF are consumed before the first system by objects not identified. Verified for the
        // standard galaxy. Inferred constant for the other sizes. The first system's managers get 0x500.
        game.SkipIds(126);
        void Init(SystemRecord record, Owner owner, bool disloyal)
        {
            // NeverBeenControlled: set by the base init; only the core class init clears it.
            // Explored: set by the base init, never cleared.
            var system = new StarSystem(record, sectors[record.SectorIndex]) { Owner = owner, Disloyal = disloyal, InitIndex = created.Count, NeverBeenControlled = true, Explored = true };
            // Owner init, before the class init: managers shipyard, training, construction (counter ids) and one
            // command serial.
            foreach (var kind in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction })
                system.Managers[(int)kind] = new ManufacturingManager(system, kind, game.NextId());
            game.Clock.NextSerial();
            // The owner-change hook starts incident timer 0x38E before the class init.
            if (owner is Owner.Alliance or Owner.Empire)
                TimerCatalogue.Start(game, 0x38E, record.Common.Ref, system, game.Params.General(0x1E17), game.Params.General(0x1E18));
            if (system.IsCore) InitCore(game, system);
            else InitRim(game, system, isHq: record == lists.Hq);
            if (record == lists.Hq) game.HeadquartersSystem = system;
            created.Add(system);
        }

        foreach (var s in lists.A) Init(s, Owner.Alliance, false);
        foreach (var s in lists.B) Init(s, Owner.Alliance, true);
        foreach (var s in lists.C) Init(s, Owner.Empire, false);
        foreach (var s in lists.D) Init(s, Owner.Empire, true);
        foreach (var s in lists.E) Init(s, Owner.Neutral, false);
        foreach (var s in lists.Rim) Init(s, Owner.Neutral, false);
        var specials = game.Definitions.Systems.ToDictionary(s => s.Common.Ref);
        Init(specials[Refs.Coruscant], Owner.Empire, false);
        Init(specials[Refs.Yavin], Owner.Alliance, false);
        Init(lists.Hq, Owner.Alliance, false);

        // Registry order: core-family (0x90) systems by Index, then rim-family (0x92) by Index.
        game.Systems.AddRange(created.OrderBy(s => s.Definition.Common.Family).ThenBy(s => s.Definition.Common.Index));
    }

    static void InitCore(GameState game, StarSystem system)
    {
        var (p, rng) = (game.Params, game.Random);
        bool populated = rng.Chance(p.General(0x1E32), DrawSite.CorePopulated);
        int support = 0;
        if (populated && system.Type != Refs.Coruscant)
        {
            support = system.Owner switch
            {
                Owner.Alliance => system.Disloyal
                    ? p.Side(0x1E04, Owner.Alliance) + rng.Roll(p.Side(0x1E05, Owner.Alliance), DrawSite.CoreSupport)
                    : p.Side(0x1E02, Owner.Alliance) + rng.Roll(p.Side(0x1E03, Owner.Alliance), DrawSite.CoreSupport),
                Owner.Empire => 100 - (system.Disloyal
                    ? p.Side(0x1E04, Owner.Empire) + rng.Roll(p.Side(0x1E05, Owner.Empire), DrawSite.CoreSupport)
                    : p.Side(0x1E02, Owner.Empire) + rng.Roll(p.Side(0x1E03, Owner.Empire), DrawSite.CoreSupport)),
                _ => rng.Roll(p.General(0x1E54), DrawSite.CoreSupport) + 50 - p.General(0x1E54) / 2,
            };
        }
        system.Populated = populated;
        if (populated) system.Support = support;
        system.Energy = Clamp(p.General(0x1E29) + rng.Roll(p.General(0x1E2A), DrawSite.CoreEnergy), p.General(0x1E21), p.General(0x1E22));
        system.RawMaterials = Math.Min(system.Energy, Clamp(p.General(0x1E2B) + rng.Roll(p.General(0x1E2C), DrawSite.CoreRaw), p.General(0x1E1F), p.General(0x1E20)));
        system.NeverBeenControlled = false;
    }

    static void InitRim(GameState game, StarSystem system, bool isHq)
    {
        var (p, rng) = (game.Params, game.Random);
        if (isHq || system.Type == Refs.Yavin)
        {
            system.Populated = true;
            system.Support = 100;
        }
        else
        {
            system.Populated = rng.Chance(p.General(0x1E33), DrawSite.RimPopulated);
            if (system.Populated) system.Support = rng.Roll(p.General(0x1E55), DrawSite.RimSupport) + 50 - p.General(0x1E55) / 2;
        }
        // The bounds 0-15 are literal values. The base and variance come from named parameters.
        int energyBase = p.General(0x1E2D), first = rng.Roll(p.General(0x1E2E), DrawSite.RimEnergy), second = rng.Roll(p.General(0x1E2F), DrawSite.RimEnergy);
        system.Energy = Clamp(energyBase + first + second, 0, 15);
        system.RawMaterials = Math.Min(system.Energy, Clamp(p.General(0x1E30) + rng.Roll(p.General(0x1E31), DrawSite.RimRaw), 0, 15));
    }

    // Random facilities: one per energy slot, a Mine or a facility from the table. Rim systems only when populated.
    static void RandomFacilities(GameState game, StarSystem system)
    {
        if (!system.IsCore && !system.Populated) return;
        var table = game.Definitions.Tables[system.IsCore ? "SYFCCRTB" : "SYFCRMTB"];
        int perMine = game.Params.General(system.IsCore ? 0x1E56u : 0x1E57u);
        string site = system.IsCore ? DrawSite.CoreFacility : DrawSite.RimFacility;
        for (int slot = 0; slot < system.Energy; slot++)
        {
            int mines = system.Contents.Count(o => o.Type == Refs.Mine && o.Owner == system.Owner);
            if (game.Random.Chance((system.RawMaterials - mines) * perMine, site))
            {
                system.Add(Create(game, Refs.Mine, system.Owner));
                continue;
            }
            if (TableLookup.Threshold(table, game.Random.Roll(100, site)) is { Value: not 0 } entry)
                system.Add(Create(game, (uint)entry.Value, system.Owner));
        }
    }

    // Fixed facilities at the HQ and at Coruscant, from the seed tables.
    static void FixedFacilities(GameState game)
    {
        var p = game.Params;
        var hq = game.HeadquartersSystem!;
        var coruscant = game.System(Refs.Coruscant);
        ApplySeedRange(game, "FACLHQTB", p.General(0x1414), p.General(0x1415), hq);
        ApplySeedRange(game, "FACLCRTB", p.General(0x1416), p.General(0x1417), coruscant);
        foreach (var system in new[] { hq, coruscant })
        {
            int facilities = system.Contents.Count(o => Family.IsFacility(o.Type) && o.Owner == system.Owner);
            if (system.Energy < facilities) system.Energy = facilities;
            int mineCapacity = system.Contents.Count(o => o.Type == Refs.Mine); // capacity 1 per Mine
            if (system.RawMaterials < mineCapacity) system.RawMaterials = mineCapacity;
        }
    }

    /// <summary>Applies SeedFamily entries with Key == k for each k in [lo, hi].</summary>
    static void ApplySeedRange(GameState game, string table, int lo, int hi, GameObject target)
    {
        var entries = game.Definitions.SeedFamilies[table];
        for (int k = lo; k <= hi; k++)
            if (TableLookup.Threshold(entries, e => e.Key, k) is { } entry && entry.Key == k)
                ApplySeed(game, entry, target);
    }

    static void ApplySeed(GameState game, SeedFamilyTableEntry entry, GameObject target)
    {
        for (int copy = 0; copy < entry.Copies; copy++)
        {
            var container = entry.Items[0].Ref.Value != 0 ? target.Add(Create(game, entry.Items[0].Ref.Value, target.Owner)) : target;
            foreach (var item in entry.Items.Skip(1)) container.Add(Create(game, item.Ref.Value, target.Owner));
        }
    }

    static GameObject Create(GameState game, uint type, Owner owner)
    {
        var o = Instantiate(game, type);
        o.Owner = owner;
        game.Register(o);
        // A new processing facility starts IDLE. A Mine/Refinery gets its pair (50, 0) on activation; in a system of owner 1 or 2
        // it becomes WAITING with OnStartupCycle set, which computes its cycle length at once (one draw); owner 3 stays IDLE.
        // Capacity 50. The IDLE to WAITING call at activation is Inferred.
        if (o is Facility { IsMineOrRefinery: true } f)
        {
            f.Capacity = 50;
            if (owner is Owner.Alliance or Owner.Empire) Economy.SetState(game, f, FacilityState.Waiting);
        }
        return o;
    }

    /// <summary>
    /// A new object of the type's family with the next auto id, owner 3, setup flags: processing facility (cycle length =
    /// ProcessingRate; Mines/Refineries recompute theirs), fleet, special force (skills rolled at creation;
    /// shipped variances are 0) or plain object. The Alliance HQ facility (0x20000001) has the fixed id 5 and takes no counter id.
    /// </summary>
    internal static GameObject Instantiate(GameState game, uint type)
    {
        if (type == 0x20000001) return new GameObject(type, Owner.Neutral, 5);
        if (Family.Of(type) is >= 0x08 and < 0x10) return new Fleet(type, Owner.Neutral, game.NextId());
        if (Family.Of(type) is >= 0x28 and < 0x30)
        {
            uint rate = game.Definitions.Facilities.First(f => f.Common.Ref == type).ProcessingRate
                ?? throw new InvalidDataException($"Facility {type:X8} has no ProcessingRate.");
            var facility = new Facility(type, Owner.Neutral, game.NextId(), (int)rate);
            if (!facility.IsMineOrRefinery) facility.CycleLength = facility.ProcessingRate;
            return facility;
        }
        if (!Family.IsSpecialForce(type)) return new GameObject(type, Owner.Neutral, game.NextId());
        var agent = new Agent(type, Owner.Neutral, game.NextId());
        RollSkills(game, agent, game.Definitions.SpecialForces.First(s => s.Common.Ref == type).Skills);
        return agent;
    }

    // Starting units: disloyal garrisons, seeded fleets and units, then the budgeted units.
    static void StartingUnits(GameState game)
    {
        var p = game.Params;
        int threshold = p.General(0x1E51), step = -p.General(0x1E52);
        foreach (var system in game.Systems.Where(s => s.Disloyal && s.Owner is Owner.Alliance or Owner.Empire))
        {
            int share = system.SupportFor(system.Owner);
            if (share >= threshold) continue;
            uint army = system.Owner == Owner.Alliance ? Refs.AllianceArmy : Refs.EmpireArmy;
            for (int i = CeilDiv(threshold - share, step); i > 0; i--) system.Add(game.Register(new GameObject(army, system.Owner, game.NextId())));
        }

        var yavin = game.System(Refs.Yavin);
        var hq = game.HeadquartersSystem!;
        var coruscant = game.System(Refs.Coruscant);
        ApplySeedRange(game, "CMUNYVTB", p.General(0x140A), p.General(0x140B), yavin);
        ApplySeedRange(game, "CMUNHQTB", p.General(0x140C), p.General(0x140D), hq);
        ApplySeedRange(game, "CMUNCRTB", p.General(0x140E), p.General(0x140F), coruscant);
        var allianceFleetAt = game.Random.Chance(p.General(0x1409), DrawSite.AllianceFleet) ? yavin : hq;
        ApplySeedRange(game, "CMUNAFTB", p.General(0x1410), p.General(0x1411), NewFleet(game, allianceFleetAt, Owner.Alliance));
        ApplySeedRange(game, "CMUNEFTB", p.General(0x1412), p.General(0x1413), NewFleet(game, coruscant, Owner.Empire));

        BudgetedUnits(game, Owner.Alliance, "CMUNALTB", DrawSite.AllianceUnits);
        BudgetedUnits(game, Owner.Empire, "CMUNEMTB", DrawSite.EmpireUnits);
    }

    /// <summary>A real fleet (the owner init sets its flag).</summary>
    static GameObject NewFleet(GameState game, StarSystem system, Owner owner) =>
        system.Add(game.Register(new Fleet(Refs.Fleet, owner, game.NextId()) { Real = true }));

    static void BudgetedUnits(GameState game, Owner side, string table, string site)
    {
        var entries = game.Definitions.SeedFamilies[table];
        var totals = game.Sides[side]; // as left by the side totals recompute
        int budget = Pct(totals.MaintenanceCapacity - totals.MaintenanceAllocated, game.Params.Side(0x1430u + (uint)game.Settings.GalaxySize - 1, side));
        while (true)
        {
            var entry = TableLookup.Threshold(entries, e => e.Key, game.Random.Between(1, 100, site))!;
            int cost = (int)entry.Copies * entry.Items.Sum(i => game.MaintenanceCost(i.Ref.Value));
            budget -= cost;
            if (budget < 0) return;
            if (cost <= 0) throw new InvalidDataException($"{table} entry {entry.Id} costs nothing; the budget loop would not end.");

            var systems = game.SystemsOf(side).ToList();
            var system = systems[game.Random.Roll(systems.Count - 1, DrawSite.UnitSystem)];
            GameObject target = system;
            if (entry.Items[0].Ref.Value != 0 && Family.IsCapitalShip(entry.Items[0].Ref.Value))
                target = system.Contents.FirstOrDefault(o => Family.Of(o.Type) == Family.Fleet && o.Owner == side && o.Contents.Any(c => Family.IsCapitalShip(c.Type)))
                    ?? NewFleet(game, system, side);
            ApplySeed(game, entry, target);
        }
    }

    static Character Unplaced(Character c)
    {
        c.ClearFlags();
        return c;
    }

    // Creates the character objects and places them.
    static void Characters(GameState game)
    {
        foreach (var record in game.Definitions.MajorCharacters.OrderBy(c => c.Common.Index).Concat(game.Definitions.MinorCharacters.OrderBy(c => c.Common.Index)))
        {
            Owner owner = (record.Alliance != 0, record.Empire != 0) switch { (true, false) => Owner.Alliance, (false, true) => Owner.Empire, _ => Owner.None };
            if (owner != Owner.None) game.Characters.Add(Unplaced(new Character(record, owner))); // unplaced: no flags
        }

        var p = game.Params;
        uint minorsParam = 0x1400u + (uint)game.Settings.GalaxySize - 1;
        var coruscant = game.System(Refs.Coruscant);
        var yavin = game.System(Refs.Yavin);

        Place(game, Find(game, 0x34000280), coruscant);
        Place(game, Find(game, Refs.Vader), RandomLocation(game, Owner.Empire));
        foreach (uint index in new uint[] { 0x380, 0x381, 0x382, 0x383, 0x38C })
            Place(game, Find(game, SdRef.Make(0x38, index)), RandomLocation(game, Owner.Empire));
        for (int i = p.Side(minorsParam, Owner.Empire); i > 0; i--) PlaceRandomMinor(game, Owner.Empire);

        Place(game, Find(game, 0x30000240), game.HeadquartersSystem!);
        foreach (uint sdRef in new uint[] { 0x32000242, 0x31000241, 0x33000243, 0x38000343, 0x38000344, 0x38000341 })
            Place(game, Find(game, sdRef), yavin);
        for (int i = p.Side(minorsParam, Owner.Alliance); i > 0; i--) PlaceRandomMinor(game, Owner.Alliance);
    }

    static Character Find(GameState game, uint sdRef) => game.Characters.First(c => c.Type == sdRef);

    // Random location: a side system, or a capital ship in a side fleet.
    static GameObject RandomLocation(GameState game, Owner side)
    {
        var systems = game.SystemsOf(side).ToList();
        var fleets = game.FleetsOf(side).Where(f => f.Usable).ToList();
        int r = game.Random.Roll(systems.Count + fleets.Count - 1, DrawSite.Location);
        if (r < systems.Count) return systems[r];
        var ships = fleets[r - systems.Count].Contents.Where(o => Family.IsCapitalShip(o.Type)).ToList();
        return ships[game.Random.Roll(ships.Count - 1, DrawSite.Location)];
    }

    // Places one random unplaced minor of the side at a random location.
    static void PlaceRandomMinor(GameState game, Owner side)
    {
        var location = RandomLocation(game, side);
        var unplaced = game.Characters.Where(c => c.Owner == side && !c.Placed && Family.Of(c.Type) is >= 0x38 and < 0x3C).ToList();
        if (unplaced.Count == 0) return;
        Place(game, unplaced[game.Random.Roll(unplaced.Count - 1, DrawSite.Minor)], location);
        if (unplaced.Count == 1) game.Sides[side].RecruitmentDone = true; // last minor placed
    }

    // Places a character in a location and runs its setup.
    static void Place(GameState game, Character character, GameObject location)
    {
        location.Add(character);
        character.SetSetupFlags(); // placement: Created, Completed (gives Constructed, Existing, Usable)
        StatRoll(game, character);
        // Placement (the Usable flag) and the Force rank feed one of the character's flags. Recomputed after the stat roll, which
        // starts Vader's timer 0x389.
        TimerCatalogue.RecomputeForceActive(game, character);
        Prisoners.RecomputeCanHeal(game, character); // placement set-parent hook. Injury is 0, so no timer.
    }

    /// <summary>Stat roll: 8 skills, known-Jedi flag, JediPotential chance, Force rank.</summary>
    internal static void StatRoll(GameState game, Character character)
    {
        var d = character.Definition;
        RollSkills(game, character, d.Skills);
        character.KnownJedi = d.IsKnownJedi != 0;
        character.JediPotential = game.Random.Chance((int)d.JediProbability, DrawSite.Jedi);
        if (character.ForceRank == 0 && character.KnownJedi && character.JediPotential)
        {
            character.ForceRank = (int)d.JediLevel.Base + game.Random.Roll((int)d.JediLevel.Variance, DrawSite.ForceRank);
            Prisoners.ForceChanged(game, character);
        }
    }

    static void RollSkills(GameState game, Agent agent, Skills skills)
    {
        Skill[] all = [skills.Diplomacy, skills.Espionage, skills.ShipDesign, skills.TroopTraining, skills.FacilityDesign, skills.Combat, skills.Leadership, skills.Loyalty];
        for (int i = 0; i < all.Length; i++) agent.Skills[i] = (int)all[i].Base + game.Random.Roll((int)all[i].Variance, DrawSite.Skills);
    }

    /// <summary>
    /// Processing facilities in registry order (family, then id): registered through the facility state handler
    /// so WAITING Mines/Refineries take their point (free before game ready) and start timer 0x394.
    /// </summary>
    static void RegisterFacilities(GameState game)
    {
        foreach (var f in game.Facilities.OrderBy(f => f.Key)) Economy.OnStateChanged(game, f, f.State);
    }

    /// <summary>
    /// System final init in registry order: SmugglingPercent, ProductionModifier (pushed to the system's Mines
    /// and Refineries), WithdrawPercent; control, garrison requirement, surplus and Uprising,
    /// stored without their hooks (inert before the game-ready flag); Garrisoned (may start timer 0x385); Suppressing; the 4
    /// placeholder fleets; CombatUnitFastRepair and the FastRepair refresh.
    /// </summary>
    static void ProductionModifiers(GameState game)
    {
        foreach (var system in game.Systems)
        {
            Systems.UpdateUsedEnergy(system); // facilities and Mines checks (position in the final init Inferred)
            Systems.UpdateUsedRaw(system);
            Economy.UpdateSmuggling(game, system);
            Systems.UpdateProductionModifier(game, system);
            Combat.UpdateWithdrawPercent(game, system);
            Systems.StoreControl(game, system, Systems.Control(game, system));
            system.GarrisonRequirement = Systems.Requirement(game, system);
            system.GarrisonSurplus = Systems.Surplus(system);
            Systems.RecomputeUprising(game, system);
            Economy.UpdateGarrison(game, system); // starts timer 0x385, after the facilities
            Systems.RecomputeSuppressing(game, system);
            Fleets.Upkeep(game, system);
            // CombatUnitFastRepair and the units' FastRepair (position in the final init Inferred, no draws)
            Repair.SetCombatUnitFastRepair(game, system, Repair.ShipyardActive(system));
            Repair.RefreshSystem(game, system);
        }
    }
}
