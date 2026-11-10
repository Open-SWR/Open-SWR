using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>RNG draw sites: the label a draw trace gives each drawing place.</summary>
public static class DrawSite
{
    public const string Timer = nameof(Timer), Allocation = nameof(Allocation),
        CorePopulated = nameof(CorePopulated), CoreSupport = nameof(CoreSupport), CoreEnergy = nameof(CoreEnergy), CoreRaw = nameof(CoreRaw),
        RimPopulated = nameof(RimPopulated), RimSupport = nameof(RimSupport), RimEnergy = nameof(RimEnergy), RimRaw = nameof(RimRaw),
        CoreFacility = nameof(CoreFacility), RimFacility = nameof(RimFacility), Cycle = nameof(Cycle),
        AllianceFleet = nameof(AllianceFleet), AllianceUnits = nameof(AllianceUnits), EmpireUnits = nameof(EmpireUnits), UnitSystem = nameof(UnitSystem),
        Location = nameof(Location), Minor = nameof(Minor), Skills = nameof(Skills), Jedi = nameof(Jedi), ForceRank = nameof(ForceRank),
        // Draws after setup (verified).
        Smuggling = nameof(Smuggling), Autoscrap = nameof(Autoscrap), Incident = nameof(Incident), SystemEventA = nameof(SystemEventA), SystemEventB = nameof(SystemEventB),
        BountyHunters = nameof(BountyHunters), Withdraw = nameof(Withdraw),
        // Missions. The loyalty check is traced under another function name.
        Loyalty = nameof(Loyalty), Recruitment = nameof(Recruitment), Diplomacy = nameof(Diplomacy), Espionage = nameof(Espionage), Incite = nameof(Incite),
        Subdue = nameof(Subdue), Rescue = nameof(Rescue), Abduction = nameof(Abduction), Assassination = nameof(Assassination), Sabotage = nameof(Sabotage),
        DeathStarSabotage = nameof(DeathStarSabotage), Research = nameof(Research), ResearchPoints = nameof(ResearchPoints), SupportDelta = nameof(SupportDelta),
        EspionageReveal = nameof(EspionageReveal), Injury = nameof(Injury), ShipLossInjury = nameof(ShipLossInjury), RevealPick = nameof(RevealPick), Wound = nameof(Wound), Death = nameof(Death),
        // Uprisings. Never traced.
        Informant = nameof(Informant), UprisingRoll = nameof(UprisingRoll), IncidentPick = nameof(IncidentPick), CharacterPick = nameof(CharacterPick), SubdueDelta = nameof(SubdueDelta),
        // Encounters. Both picks are traced under one function name.
        EncounterCheck = nameof(EncounterCheck), DecoyPick = nameof(DecoyPick), UnitPick = nameof(UnitPick),
        // Escape. Never traced.
        Escape = nameof(Escape),
        // Force detection. Never exercised in runs 1-3. The wrapper sites are not trace-checked.
        ForceDetect = nameof(ForceDetect), ForcePick = nameof(ForcePick),
        // Galaxy resource events. Disaster: Inferred label (the draw site is not given).
        Resources = nameof(Resources), Losses = nameof(Losses), EnergyOverflow = nameof(EnergyOverflow), Disaster = nameof(Disaster),
        // Combat. Never traced: the function holding the draw. Inferred for the pick sites.
        AssaultFire = nameof(AssaultFire), AssaultPick = nameof(AssaultPick), DuelPick = nameof(DuelPick), Duel = nameof(Duel), Collateral = nameof(Collateral),
        CollateralPick = nameof(CollateralPick), BombardFire = nameof(BombardFire), BombardShot = nameof(BombardShot), BombardFacility = nameof(BombardFacility), BombardHit = nameof(BombardHit),
        // Critical-hit roll (CriticalHitRoll) and its CSCRHTTB roll (CriticalKindRoll, Inferred).
        CriticalHit = nameof(CriticalHit), CriticalKind = nameof(CriticalKind),
        // Jedi and story. Never traced: the function holding the draw.
        JediTraining = nameof(JediTraining), JediGain = nameof(JediGain), DuelWound = nameof(DuelWound), HeritageWound = nameof(HeritageWound), Palace = nameof(Palace),
        FinalBattle = nameof(FinalBattle),
        // Strategic repair (traced in run 9): chance(DamageControl) and the critical-hit pick roll(n - 1).
        RepairCheck = nameof(RepairCheck), RepairPick = nameof(RepairPick),
        // Death Star. Never traced.
        PlanetMinor = nameof(PlanetMinor),
        // Identify traitor. Never traced.
        Traitor = nameof(Traitor);
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
        var p = game.Rules;
        TimerCatalogue.Start(game, TimerType.Dagobah, Refs.MissionManager, null, p.DagobahDays, p.DagobahVariance);
        TimerCatalogue.Start(game, TimerType.BountyHunters, Refs.MissionManager, null, p.BountyHuntersDays, p.BountyHuntersVariance);
        game.StoryFlags.BountyHuntersActive = true; // its hook sets the bounty hunters timer slot Active; the start has already set it
        TimerCatalogue.Start(game, TimerType.Disaster, Refs.GameManager, null, p.DisasterDays, p.DisasterVariance);
        TimerCatalogue.Start(game, TimerType.ResourceEvent, Refs.GameManager, null, p.ResourceEventDays, p.ResourceEventVariance);
        foreach (var side in new[] { game.Sides[Owner.Alliance], game.Sides[Owner.Empire] })
            TimerCatalogue.Start(game, TimerType.Research, side.Ref, side, p.ResearchDays, p.ResearchVariance);
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

        var p = game.Rules;
        int n = core.Count, r = rim.Count;
        // Sets both sides' cost scale to the number of systems placed (only if > 0).
        if (n + r > 0) foreach (var side in game.Sides.Values) side.CostScale = n + r;
        int a = Pct(n, p.For(Owner.Alliance).SetupLoyalShare), b = Pct(n, p.For(Owner.Alliance).SetupDisloyalShare);
        int c = Pct(n, p.For(Owner.Empire).SetupLoyalShare), d = Pct(n, p.For(Owner.Empire).SetupDisloyalShare);
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
        // 126 ids 1154-1279 are consumed before the first system by objects not identified. Verified for the
        // standard galaxy. Inferred constant for the other sizes. The first system's managers get 1280.
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
            // The owner-change hook starts the informant timer (TimerType.Informant) before the class init.
            if (owner is Owner.Alliance or Owner.Empire)
                TimerCatalogue.Start(game, TimerType.Informant, record.Common.Ref, system, game.Rules.InformantDays, game.Rules.InformantVariance);
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

        // Registry order: core-family (144) systems by Index, then rim-family (146) by Index.
        game.Systems.AddRange(created.OrderBy(s => s.Definition.Common.Family).ThenBy(s => s.Definition.Common.Index));
    }

    static void InitCore(GameState game, StarSystem system)
    {
        var (p, rng) = (game.Rules, game.Random);
        bool populated = rng.Chance(p.CorePopulatedChance, DrawSite.CorePopulated);
        int support = 0;
        if (populated && system.Type != Refs.Coruscant)
        {
            support = system.Owner switch
            {
                Owner.Alliance => system.Disloyal
                    ? p.For(Owner.Alliance).DisloyalSupportAdd + rng.Roll(p.For(Owner.Alliance).DisloyalSupportRoll, DrawSite.CoreSupport)
                    : p.For(Owner.Alliance).LoyalSupportAdd + rng.Roll(p.For(Owner.Alliance).LoyalSupportRoll, DrawSite.CoreSupport),
                Owner.Empire => 100 - (system.Disloyal
                    ? p.For(Owner.Empire).DisloyalSupportAdd + rng.Roll(p.For(Owner.Empire).DisloyalSupportRoll, DrawSite.CoreSupport)
                    : p.For(Owner.Empire).LoyalSupportAdd + rng.Roll(p.For(Owner.Empire).LoyalSupportRoll, DrawSite.CoreSupport)),
                _ => rng.Roll(p.NeutralCoreSupportRoll, DrawSite.CoreSupport) + 50 - p.NeutralCoreSupportRoll / 2,
            };
        }
        system.Populated = populated;
        if (populated) system.Support = support;
        system.Energy = Clamp(p.CoreEnergyAdd + rng.Roll(p.CoreEnergyRoll, DrawSite.CoreEnergy), p.EnergyMin, p.EnergyMax);
        system.RawMaterials = Math.Min(system.Energy, Clamp(p.CoreRawMaterialsAdd + rng.Roll(p.CoreRawMaterialsRoll, DrawSite.CoreRaw), p.RawMaterialsMin, p.RawMaterialsMax));
        system.NeverBeenControlled = false;
    }

    static void InitRim(GameState game, StarSystem system, bool isHq)
    {
        var (p, rng) = (game.Rules, game.Random);
        if (isHq || system.Type == Refs.Yavin)
        {
            system.Populated = true;
            system.Support = 100;
        }
        else
        {
            system.Populated = rng.Chance(p.RimPopulatedChance, DrawSite.RimPopulated);
            if (system.Populated) system.Support = rng.Roll(p.RimSupportRoll, DrawSite.RimSupport) + 50 - p.RimSupportRoll / 2;
        }
        // The bounds 0-15 are literal values. The base and variance come from named parameters.
        int energyBase = p.RimEnergyAdd, first = rng.Roll(p.RimEnergyFirstRoll, DrawSite.RimEnergy), second = rng.Roll(p.RimEnergySecondRoll, DrawSite.RimEnergy);
        system.Energy = Clamp(energyBase + first + second, 0, 15);
        system.RawMaterials = Math.Min(system.Energy, Clamp(p.RimRawMaterialsAdd + rng.Roll(p.RimRawMaterialsRoll, DrawSite.RimRaw), 0, 15));
    }

    // Random facilities: one per energy slot, a Mine or a facility from the table. Rim systems only when populated.
    static void RandomFacilities(GameState game, StarSystem system)
    {
        if (!system.IsCore && !system.Populated) return;
        var table = game.Definitions.Tables[system.IsCore ? "SYFCCRTB" : "SYFCRMTB"];
        int perMine = (system.IsCore ? game.Rules.CoreMineFactor : game.Rules.RimMineFactor);
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
        var p = game.Rules;
        var hq = game.HeadquartersSystem!;
        var coruscant = game.Known.Coruscant!;
        ApplySeedRange(game, "FACLHQTB", p.HqFacilitiesFirstKey, p.HqFacilitiesLastKey, hq);
        ApplySeedRange(game, "FACLCRTB", p.CoruscantFacilitiesFirstKey, p.CoruscantFacilitiesLastKey, coruscant);
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
    /// shipped variances are 0) or plain object. The Alliance HQ facility (family 32, index 1) has the fixed id 5 and takes no counter id.
    /// </summary>
    internal static GameObject Instantiate(GameState game, uint type)
    {
        if (type == Refs.HeadquartersType) return new GameObject(type, Owner.Neutral, 5);
        if (Family.Of(type) is >= Families.Fleet and < Families.Troop) return new Fleet(type, Owner.Neutral, game.NextId());
        if (Family.Of(type) is >= Families.Shipyard and < Families.MajorCharacter)
        {
            uint rate = game.Definitions.Facilities.First(f => f.Common.Ref == type).ProcessingRate
                ?? throw new InvalidDataException($"Facility {Family.Of(type)}:{SdRef.IndexOf(type)} has no ProcessingRate.");
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
        var p = game.Rules;
        int threshold = p.GarrisonSupportThreshold, step = -p.GarrisonSupportPerTroop;
        foreach (var system in game.Systems.Where(s => s.Disloyal && s.Owner is Owner.Alliance or Owner.Empire))
        {
            int share = system.SupportFor(system.Owner);
            if (share >= threshold) continue;
            uint army = system.Owner == Owner.Alliance ? Refs.AllianceArmy : Refs.EmpireArmy;
            for (int i = CeilDiv(threshold - share, step); i > 0; i--) system.Add(game.Register(new GameObject(army, system.Owner, game.NextId())));
        }

        var yavin = game.Known.Yavin!;
        var hq = game.HeadquartersSystem!;
        var coruscant = game.Known.Coruscant!;
        ApplySeedRange(game, "CMUNYVTB", p.YavinUnitsFirstKey, p.YavinUnitsLastKey, yavin);
        ApplySeedRange(game, "CMUNHQTB", p.HqUnitsFirstKey, p.HqUnitsLastKey, hq);
        ApplySeedRange(game, "CMUNCRTB", p.CoruscantUnitsFirstKey, p.CoruscantUnitsLastKey, coruscant);
        var allianceFleetAt = game.Random.Chance(p.AllianceFleetAtYavinChance, DrawSite.AllianceFleet) ? yavin : hq;
        ApplySeedRange(game, "CMUNAFTB", p.AllianceFleetUnitsFirstKey, p.AllianceFleetUnitsLastKey, NewFleet(game, allianceFleetAt, Owner.Alliance));
        ApplySeedRange(game, "CMUNEFTB", p.EmpireFleetUnitsFirstKey, p.EmpireFleetUnitsLastKey, NewFleet(game, coruscant, Owner.Empire));

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
        int budget = Pct(totals.MaintenanceCapacity - totals.MaintenanceAllocated, game.Rules.For(side).StartingUnitBudget(game.Settings.GalaxySize));
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
                target = system.Contents.FirstOrDefault(o => Family.Of(o.Type) == Families.Fleet && o.Owner == side && o.Contents.Any(c => Family.IsCapitalShip(c.Type)))
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

        var coruscant = game.Known.Coruscant!;
        var yavin = game.Known.Yavin!;

        Place(game, game.Known.Palpatine!, coruscant);
        Place(game, game.Known.Vader!, RandomLocation(game, Owner.Empire));
        foreach (uint index in new uint[] { 896, 897, 898, 899, 908 })
            Place(game, Find(game, SdRef.Make(Families.MinorCharacter, index)), RandomLocation(game, Owner.Empire));
        for (int i = game.Rules.For(Owner.Empire).MinorCharacters(game.Settings.GalaxySize); i > 0; i--) PlaceRandomMinor(game, Owner.Empire);

        Place(game, game.Known.Mothma!, game.HeadquartersSystem!);
        foreach (uint sdRef in new uint[] { Refs.Luke, Refs.Leia, Refs.Han, Refs.Chewbacca, Refs.Dodonna, Refs.Wedge })
            Place(game, Find(game, sdRef), yavin);
        for (int i = game.Rules.For(Owner.Alliance).MinorCharacters(game.Settings.GalaxySize); i > 0; i--) PlaceRandomMinor(game, Owner.Alliance);
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
        var unplaced = game.Characters.Where(c => c.Owner == side && !c.Placed && Family.Of(c.Type) is >= Families.MinorCharacter and < Families.SpecialForce).ToList();
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
        // starts Vader's Force discovery timer.
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
    /// so WAITING Mines/Refineries take their point (free before game ready) and start the facility cycle timer.
    /// </summary>
    static void RegisterFacilities(GameState game)
    {
        foreach (var f in game.Facilities.OrderBy(f => f.Key)) Economy.OnStateChanged(game, f, f.State);
    }

    /// <summary>
    /// System final init in registry order: SmugglingPercent, ProductionModifier (pushed to the system's Mines
    /// and Refineries), WithdrawPercent; control, garrison requirement, surplus and Uprising,
    /// stored without their hooks (inert before the game-ready flag); Garrisoned (may start the garrison support timer); Suppressing; the 4
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
            Economy.UpdateGarrison(game, system); // starts the garrison support timer, after the facilities
            Systems.RecomputeSuppressing(game, system);
            Fleets.Upkeep(game, system);
            // CombatUnitFastRepair and the units' FastRepair (position in the final init Inferred, no draws)
            Repair.SetCombatUnitFastRepair(game, system, Repair.ShipyardActive(system));
            Repair.RefreshSystem(game, system);
        }
    }
}
