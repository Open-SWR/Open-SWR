using OpenSWR.Data.Dat;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

/// <summary>Advisor system value k.</summary>
public enum AdvisorValue
{
    /// <summary>100 for an own system, the side's support share when Populated.</summary>
    Support, Energy, RawMaterial, Mines, Refineries, Shipyards, TrainingFacilities, ConstructionYards, GenCores, DeathStarShields, Batteries,
    FreeRaw, FreeEnergy, Troops, Fighters, TroopMaintenance, DefenceMaintenance, ProducerMaintenance, FighterMaintenance,
    /// <summary>Own only.</summary>
    TroopNeed,
    /// <summary>Own only: troops minus max(producer need, troop need).</summary>
    TroopSurplus,
    /// <summary>Own only.</summary>
    TotalMaintenance,
}

/// <summary>Advisor sector value i.</summary>
public enum AdvisorSectorValue
{
    Explored, Populated, OwnSystems, EnemySystems, NeutralSystems, Destroyed,
    // own systems' sums (i = 6 ... 22)
    Mines, Refineries, Shipyards, TrainingFacilities, ConstructionYards, GenCores, DeathStarShields, Batteries, Fighters, FreeRaw, FreeEnergy,
    TroopMaintenance, DefenceMaintenance, ProducerMaintenance, FighterMaintenance, TotalMaintenance, Support,
    /// <summary>Neutral systems' support.</summary>
    NeutralSupport,
    /// <summary>Enemy systems' support.</summary>
    EnemySupport,
    /// <summary>Minus the troop surplus of own Uprising systems (their summed shortfall).</summary>
    UprisingShortfall,
}

/// <summary>Advisor system record.</summary>
public sealed class AdvisorSystem(StarSystem system)
{
    public StarSystem System { get; } = system;
    /// <summary>Its sector record.</summary>
    public AdvisorSector? Sector { get; set; }
    public int Key { get; set; }
    /// <summary>The view owner.</summary>
    public int Owner { get; set; }
    public bool Uprising { get; set; }
    public bool Blockade { get; set; }
    public bool Own { get; set; }
    public bool Enemy { get; set; }
    public bool Neutral { get; set; }
    /// <summary>Free energy &gt; 0.</summary>
    public bool FreeEnergy { get; set; }
    /// <summary>Free energy and free raw material &gt; 0.</summary>
    public bool FreeRaw { get; set; }
    /// <summary>An idle ship manager over a Usable shipyard (no Uprising or Blockade).</summary>
    public bool IdleShipyard { get; set; }
    /// <summary>An idle training manager over a Usable training facility.</summary>
    public bool IdleTraining { get; set; }
    /// <summary>An idle construction manager over a Usable construction yard.</summary>
    public bool IdleConstruction { get; set; }
    /// <summary>Mixes several "has something spare" cases (also a reserved ship manager); no planner reads it.</summary>
    public bool Spare { get; set; }
    /// <summary>The training manager is reserved (CommandType.SetReserved).</summary>
    public bool ReservedTraining { get; set; }
    /// <summary>The construction manager is reserved (CommandType.SetReserved).</summary>
    public bool ReservedConstruction { get; set; }
    /// <summary>Own with a troop shortfall.</summary>
    public bool TroopShortfall { get; set; }
    public bool Explored { get; set; }
    public bool Populated { get; set; }
    /// <summary>An HQ of the owner.</summary>
    public bool Headquarters { get; set; }
    // marks, only ever set (kept across updates)
    /// <summary>A core system.</summary>
    public bool Core { get; set; }
    /// <summary>Inferred: Destroyed, as the AI twin.</summary>
    public bool Destroyed { get; set; }
    public bool Coruscant { get; set; }

    /// <summary>Clears what an update rebuilds (the marks are kept).</summary>
    internal void Clear()
    {
        (Uprising, Blockade, Own, Enemy, Neutral, FreeEnergy, FreeRaw, IdleShipyard, IdleTraining) = (false, false, false, false, false, false, false, false, false);
        (IdleConstruction, Spare, ReservedTraining, ReservedConstruction, TroopShortfall, Explored, Populated, Headquarters) = (false, false, false, false, false, false, false, false);
    }
    /// <summary>The day the last Blockade was seen (enemy / neutral).</summary>
    public int BlockadeDay { get; set; }
    /// <summary>Value k.</summary>
    public ref int this[AdvisorValue k] => ref _values[(int)k];
    readonly int[] _values = new int[(int)AdvisorValue.TotalMaintenance + 1];
}

/// <summary>Advisor sector record: its systems in slot order.</summary>
public sealed class AdvisorSector(Sector sector)
{
    public Sector Sector { get; } = sector;
    public List<AdvisorSystem> Systems { get; } = [];
    public int Key { get; set; }
    public bool AllExplored { get; set; }
    public bool AllPopulated { get; set; }
    public bool Own { get; set; }
    public bool Enemy { get; set; }
    public bool Neutral { get; set; }
    /// <summary>An own system with free energy.</summary>
    public bool FreeEnergy { get; set; }
    /// <summary>An own system with free raw material.</summary>
    public bool FreeRaw { get; set; }
    public bool IdleShipyard { get; set; }
    public bool IdleTraining { get; set; }
    public bool IdleConstruction { get; set; }
    /// <summary>An own system with <see cref="AdvisorSystem.Spare"/>.</summary>
    public bool Spare { get; set; }
    /// <summary>An own system without <see cref="AdvisorSystem.Spare"/>.</summary>
    public bool NoSpare { get; set; }
    /// <summary>An own system without <see cref="AdvisorSystem.ReservedTraining"/>.</summary>
    public bool NoReservedTraining { get; set; }
    /// <summary>An own system without <see cref="AdvisorSystem.ReservedConstruction"/>.</summary>
    public bool NoReservedConstruction { get; set; }
    public bool TroopShortfall { get; set; }
    public bool Headquarters { get; set; }
    /// <summary>An own system with Blockade.</summary>
    public bool Blockade { get; set; }
    /// <summary>An own system with Uprising.</summary>
    public bool Uprising { get; set; }
    // marks, only ever set (kept across updates)
    /// <summary>A core sector.</summary>
    public bool Core { get; set; }
    /// <summary>The sector holds Coruscant.</summary>
    public bool Coruscant { get; set; }

    /// <summary>Clears what an update rebuilds (the marks are kept).</summary>
    internal void Clear()
    {
        (AllExplored, AllPopulated, Own, Enemy, Neutral, FreeEnergy, FreeRaw, IdleShipyard, IdleTraining) = (false, false, false, false, false, false, false, false, false);
        (IdleConstruction, Spare, NoSpare, NoReservedTraining, NoReservedConstruction, TroopShortfall, Headquarters, Blockade, Uprising)
            = (false, false, false, false, false, false, false, false, false);
    }
    /// <summary>Value i.</summary>
    public ref int this[AdvisorSectorValue i] => ref _values[(int)i];
    readonly int[] _values = new int[(int)AdvisorSectorValue.UprisingShortfall + 1];
}
/// <summary>
/// The advisor's world model: one record per system and per sector from the player's views,
/// cap / dem and the totals over the sectors (own Mines and Refineries among them). Draws nothing on update; the list builders
/// draw rand(10) per tie on <see cref="Rand"/>. shortcut: every record is recomputed in each update (the original updates only
/// records dirtied by the human controller's notifications); it differs only where a dirty mark is missed (Inferred harmless,
/// no recording exists); the marks are kept across updates but lost on load (unread by the planners).
/// </summary>
public sealed class AdvisorModel(Owner side)
{
    public Owner Side { get; } = side;
    public Func<int, int> Rand { get; set; } = n => UiRandom.Shared.Rand(n);
    public Dictionary<StarSystem, AdvisorSystem> Systems { get; } = [];
    /// <summary>Sector records in key order.</summary>
    public List<AdvisorSector> Sectors { get; } = [];
    public Catalogue? Catalogue { get; private set; }
    /// <summary>The side's maintenance capacity (cap).</summary>
    public int MaintenanceCapacity { get; set; }
    /// <summary>The side's maintenance demand (dem).</summary>
    public int MaintenanceDemand { get; set; }
    /// <summary>Own Mines (sum of sector values).</summary>
    public int Mines { get => Total(AdvisorSectorValue.Mines); set => Total(AdvisorSectorValue.Mines) = value; }
    /// <summary>Own Refineries.</summary>
    public int Refineries { get => Total(AdvisorSectorValue.Refineries); set => Total(AdvisorSectorValue.Refineries) = value; }
    /// <summary>The sum of sector value i over the sectors.</summary>
    public ref int Total(AdvisorSectorValue i) => ref _totals[(int)i];
    /// <summary>The support share of a side in per cent: the sum of the two sides' shares.</summary>
    const int SupportShareTotal = 100;
    /// <summary>The Index of Coruscant's SD record (family 144, index 265).</summary>
    const int CoruscantIndex = 265;
    /// <summary>Troop need: a support below SupportNeedLimit gives (SupportNeedBase - support) / SupportNeedStep + 1, else 1 (Inferred meaning of the constants).</summary>
    const int SupportNeedLimit = 60, SupportNeedBase = 69, SupportNeedStep = 10;
    /// <summary>The sector values summed into the totals: Explored to ProducerMaintenance.</summary>
    const int SummedValues = (int)AdvisorSectorValue.ProducerMaintenance + 1;
    readonly int[] _totals = new int[SummedValues];
    /// <summary>The sums of the sectors' own, neutral and enemy support.</summary>
    public int OwnSupport { get; set; }
    public int NeutralSupport { get; set; }
    public int EnemySupport { get; set; }

    /// <summary>The update in one call: records built on first use (init steps 2-3), cap / dem, every sector, the totals.</summary>
    public void Update(GameState game)
    {
        if (Catalogue is null)
        {
            Catalogue = new Catalogue(game.Definitions);
            foreach (var s in game.Systems.OrderBy(s => s.Ref)) Systems[s] = new AdvisorSystem(s) { Key = (int)s.Ref };
            foreach (var sector in game.Sectors.OrderBy(x => x.Ref))
            {
                var r = new AdvisorSector(sector) { Key = (int)sector.Ref };
                if (sector.IsCore) r.Core = true;
                foreach (var s in game.Systems.Where(s => s.Sector == sector).OrderBy(s => s.Ref).Take(AiSector.SystemsPerSector))
                {
                    Systems[s].Sector = r;
                    r.Systems.Add(Systems[s]);
                }
                Sectors.Add(r);
            }
        }
        // cap / dem
        (MaintenanceCapacity, MaintenanceDemand) = (game.Sides[Side].MaintenanceCapacity, game.Sides[Side].Demand);
        foreach (var r in Sectors) UpdateSector(game, r);
        Array.Clear(_totals);
        (OwnSupport, NeutralSupport, EnemySupport) = (0, 0, 0);
        foreach (var r in Sectors)
        {
            for (int i = 0; i < SummedValues; i++) Total((AdvisorSectorValue)i) += r[(AdvisorSectorValue)i];
            // the support sums (own, neutral, enemy)
            OwnSupport += r[AdvisorSectorValue.Support];
            NeutralSupport += r[AdvisorSectorValue.NeutralSupport];
            EnemySupport += r[AdvisorSectorValue.EnemySupport];
        }
    }

    int Cost(GameObject o) => Catalogue![o.Type]?.MaintenanceCost ?? 0;

    /// <summary>The player's view of the system's manager of a kind, null when none.</summary>
    public ManufacturingManager? Manager(GameState game, StarSystem s, ManufacturingKind kind) =>
        s.Managers[(int)kind] is { } m && !Knowledge.See(game, m, Side).Destroyed ? m : null;

    void UpdateSystem(GameState game, AdvisorSystem r)
    {
        var s = r.System;
        var v = Knowledge.See(game, s, Side);
        r.Clear();
        for (var k = AdvisorValue.Support; k <= AdvisorValue.TotalMaintenance; k++) r[k] = 0;
        r.Owner = (int)v.Owner;
        // 1. Inferred: Destroyed, as the AI twin
        if (v.Destroyed) r.Destroyed = true;
        bool own = v.Owner == Side, enemy = !own && v.Owner is Owner.Alliance or Owner.Empire;
        if (own) r.Own = true;
        else if (enemy) r.Enemy = true;
        else r.Neutral = true;
        if (Family.Of(s.Type) is >= Families.CoreSystem and < Families.RimSystem) r.Core = true;
        // 2. shortcut: views keep no energy / raw material / used counts; the real system's values and stored used counts are read
        // for every view
        if (!v.Explored) return;
        r.Explored = true;
        (r[AdvisorValue.Energy], r[AdvisorValue.RawMaterial]) = (s.Energy, s.RawMaterials);
        (r[AdvisorValue.FreeRaw], r[AdvisorValue.FreeEnergy]) = (s.RawMaterials - s.UsedRaw, s.Energy - s.UsedEnergy);
        if (r[AdvisorValue.FreeEnergy] > 0)
        {
            r.FreeEnergy = true;
            if (r[AdvisorValue.FreeRaw] > 0) r.FreeRaw = true;
        }
        // 3
        if (v.Blockade)
        {
            r.Blockade = true;
            if (!own) r.BlockadeDay = game.Day;
        }
        if (own) r[AdvisorValue.Support] = SupportShareTotal;
        // 4. troops (mode 3)
        var contents = ScreenViews.Existing(game, s, Side);
        bool troopUsable = false;
        foreach (var t in contents.Where(x => x.In(Families.Troop, Families.CapitalShip)))
        {
            r[AdvisorValue.Troops]++;
            r[AdvisorValue.TroopMaintenance] += Cost(t.Object);
            if (t.Usable) troopUsable = true;
        }
        // 5
        if (!v.Populated) return;
        if (s.Id == CoruscantIndex) r.Coruscant = true;
        if (contents.Any(x => x.In(Families.Headquarters, Families.IonCannon) && x.Owner == v.Owner)) r.Headquarters = true; // an HQ of the owner
        if (v.Uprising) r.Uprising = true;
        r.Populated = true;
        r[AdvisorValue.Support] = Side == Owner.Alliance ? v.Support : SupportShareTotal - v.Support; // the side's support share
        r[AdvisorValue.Mines] = contents.Count(x => x.Family == Families.Mine && x.Owner == v.Owner);
        r[AdvisorValue.Refineries] = contents.Count(x => x.Family == Families.Refinery && x.Owner == v.Owner);
        // production facilities with their manager: reserved (CommandType.SetReserved) gives the kind's reserved flag; empty queue gives idle (also Spare
        // with more than one facility, not for yards)
        Producers(game, r, contents, Families.Shipyard, AdvisorValue.Shipyards, ManufacturingKind.Ship, true);
        Producers(game, r, contents, Families.TrainingFacility, AdvisorValue.TrainingFacilities, ManufacturingKind.Training, true);
        Producers(game, r, contents, Families.ConstructionYard, AdvisorValue.ConstructionYards, ManufacturingKind.Construction, false);
        foreach (var x in contents)
            switch (x.Family)
            {
                case Families.PlanetaryShield: r[AdvisorValue.GenCores]++; r[AdvisorValue.DefenceMaintenance] += Cost(x.Object); break;
                case Families.DeathStarShield: r[AdvisorValue.DeathStarShields]++; r[AdvisorValue.DefenceMaintenance] += Cost(x.Object); break;
                case Families.IonCannon or Families.Gun: r[AdvisorValue.Batteries]++; r[AdvisorValue.DefenceMaintenance] += Cost(x.Object); break;
                case >= Families.Fighter and < Families.Headquarters: r[AdvisorValue.Fighters]++; r[AdvisorValue.FighterMaintenance] += Cost(x.Object); break;
            }
        if (contents.Any(x => x.Family == Families.PlanetaryShield && x.Usable) && r[AdvisorValue.GenCores] > 1) r.Spare = true;
        if (contents.Any(x => x.Family is Families.IonCannon or Families.Gun && x.Usable) && r[AdvisorValue.Batteries] > 1) r.Spare = true;
        if (contents.Any(x => x.In(Families.Fighter, Families.Headquarters) && x.Usable)) r.Spare = true;
        // 6. own: troop need / surplus, total maintenance
        if (!own) return;
        int support = r[AdvisorValue.Support], need = support < SupportNeedLimit ? (SupportNeedBase - support) / SupportNeedStep + 1 : 1;
        if (v.Uprising) need *= 2;
        if (troopUsable && need < r[AdvisorValue.Troops]) r.Spare = true;
        int f = (r[AdvisorValue.Mines] + r[AdvisorValue.Refineries]) / 2 + r[AdvisorValue.ConstructionYards] + r[AdvisorValue.Shipyards]
            + r[AdvisorValue.TrainingFacilities];
        r[AdvisorValue.TroopNeed] = need;
        r[AdvisorValue.TroopSurplus] = r[AdvisorValue.Troops] - Math.Max(f, need);
        if (r[AdvisorValue.TroopSurplus] < 0) r.TroopShortfall = true;
        r[AdvisorValue.TotalMaintenance] = r[AdvisorValue.FighterMaintenance] + r[AdvisorValue.ProducerMaintenance] + r[AdvisorValue.DefenceMaintenance]
            + r[AdvisorValue.TroopMaintenance];
    }

    /// <summary>
    /// One producer family: count, maintenance and the manager marks (a Usable facility, no Uprising or Blockade): reserved (a
    /// reserved ship manager counts as spare), then idle.
    /// </summary>
    void Producers(GameState game, AdvisorSystem r, List<ViewObject> contents, uint family, AdvisorValue count, ManufacturingKind kind, bool more)
    {
        var list = contents.Where(x => x.Family == family).ToList();
        r[count] = list.Count;
        r[AdvisorValue.ProducerMaintenance] += list.Sum(x => Cost(x.Object));
        if (list.Count > 0 && list.Any(x => x.Usable) && !r.Uprising && !r.Blockade && Manager(game, r.System, kind) is { } m)
        {
            if (Knowledge.Reserved(m, Side))
            {
                if (kind == ManufacturingKind.Ship) r.Spare = true;
                else if (kind == ManufacturingKind.Training) r.ReservedTraining = true;
                else r.ReservedConstruction = true;
            }
            if (Knowledge.QueueCount(game, m, Side) != 0) return;
            if (kind == ManufacturingKind.Ship) r.IdleShipyard = true;
            else if (kind == ManufacturingKind.Training) r.IdleTraining = true;
            else r.IdleConstruction = true;
            if (more && list.Count > 1) r.Spare = true;
        }
    }

    void UpdateSector(GameState game, AdvisorSector r)
    {
        r.Clear();
        for (var i = AdvisorSectorValue.Explored; i <= AdvisorSectorValue.UprisingShortfall; i++) r[i] = 0;
        foreach (var s in r.Systems)
        {
            UpdateSystem(game, s);
            if (s.Explored) r[AdvisorSectorValue.Explored]++;
            if (s.Populated) r[AdvisorSectorValue.Populated]++;
            if (s.Destroyed) r[AdvisorSectorValue.Destroyed]++;
            if (s.Coruscant) r.Coruscant = true;
            if (s.Headquarters) r.Headquarters = true;
            if (s.Own)
            {
                r.Own = true;
                r[AdvisorSectorValue.OwnSystems]++;
                r[AdvisorSectorValue.Mines] += s[AdvisorValue.Mines];
                r[AdvisorSectorValue.Refineries] += s[AdvisorValue.Refineries];
                r[AdvisorSectorValue.Shipyards] += s[AdvisorValue.Shipyards];
                r[AdvisorSectorValue.TrainingFacilities] += s[AdvisorValue.TrainingFacilities];
                r[AdvisorSectorValue.ConstructionYards] += s[AdvisorValue.ConstructionYards];
                r[AdvisorSectorValue.GenCores] += s[AdvisorValue.GenCores];
                r[AdvisorSectorValue.DeathStarShields] += s[AdvisorValue.DeathStarShields];
                r[AdvisorSectorValue.Batteries] += s[AdvisorValue.Batteries];
                r[AdvisorSectorValue.Fighters] += s[AdvisorValue.Fighters];
                r[AdvisorSectorValue.FreeRaw] += s[AdvisorValue.FreeRaw];
                r[AdvisorSectorValue.FreeEnergy] += s[AdvisorValue.FreeEnergy];
                r[AdvisorSectorValue.TroopMaintenance] += s[AdvisorValue.TroopMaintenance];
                r[AdvisorSectorValue.DefenceMaintenance] += s[AdvisorValue.DefenceMaintenance];
                r[AdvisorSectorValue.ProducerMaintenance] += s[AdvisorValue.ProducerMaintenance];
                r[AdvisorSectorValue.FighterMaintenance] += s[AdvisorValue.FighterMaintenance];
                r[AdvisorSectorValue.TotalMaintenance] += s[AdvisorValue.TotalMaintenance];
                r[AdvisorSectorValue.Support] += s[AdvisorValue.Support];
                if (s.FreeEnergy) r.FreeEnergy = true;
                if (s.FreeRaw) r.FreeRaw = true;
                if (s.IdleShipyard) r.IdleShipyard = true;
                if (s.IdleTraining) r.IdleTraining = true;
                if (s.IdleConstruction) r.IdleConstruction = true;
                if (s.Spare) r.Spare = true;
                if (s.TroopShortfall) r.TroopShortfall = true;
                if (s.Blockade) r.Blockade = true;
                if (!s.Spare) r.NoSpare = true;
                if (!s.ReservedTraining) r.NoReservedTraining = true;
                if (!s.ReservedConstruction) r.NoReservedConstruction = true;
                if (s.Uprising)
                {
                    r.Uprising = true;
                    r[AdvisorSectorValue.UprisingShortfall] -= s[AdvisorValue.TroopSurplus];
                }
            }
            else if (s.Enemy)
            {
                r.Enemy = true;
                r[AdvisorSectorValue.EnemySystems]++;
                r[AdvisorSectorValue.EnemySupport] += s[AdvisorValue.Support];
            }
            else
            {
                r.Neutral = true;
                r[AdvisorSectorValue.NeutralSystems]++;
                r[AdvisorSectorValue.NeutralSupport] += s[AdvisorValue.Support];
            }
        }
        if (r[AdvisorSectorValue.Explored] == AiSector.SystemsPerSector) r.AllExplored = true;
        if (r[AdvisorSectorValue.Populated] == AiSector.SystemsPerSector) r.AllPopulated = true;
    }

    // ---- list builders (the marks are never asked for) ----

    static int Squared(int dx, int dy) => dx * dx + dy * dy;

    /// <summary>The system reached by walking the player's view parents up from <paramref name="o"/> (a manager's parent is its system).</summary>
    public StarSystem? SystemOf(GameState game, GameObject? o) => o switch
    {
        null => null,
        StarSystem s => s,
        ManufacturingManager m => m.System,
        _ => Knowledge.Location(game, o, Side) as StarSystem,
    };

    /// <summary>Sector records (key order) by sector value i.</summary>
    public RankedList Sectorwise(Func<AdvisorSector, bool> pass, AdvisorSectorValue i, RankOrder dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, x[i])), dir, Rand);

    /// <summary>Sector records by squared distance to the sector of <paramref name="from"/>; none without a system.</summary>
    public RankedList? SectorsNear(GameState game, GameObject? from, Func<AdvisorSector, bool> pass, RankOrder dir)
    {
        if (SystemOf(game, from) is not { } at) return null;
        var p = at.Sector.Definition;
        return new(Sectors.Where(pass).Select(x => ((object)x, Squared(x.Sector.Definition.X - p.X, x.Sector.Definition.Y - p.Y))), dir, Rand);
    }

    /// <summary>The sector's systems (slot order) by system value k; none when <paramref name="sector"/> is no sector record.</summary>
    public RankedList? Systemwise(object? sector, Func<AdvisorSystem, bool> pass, AdvisorValue k, RankOrder dir) =>
        sector is AdvisorSector r ? new(r.Systems.Where(pass).Select(x => ((object)x, x[k])), dir, Rand) : null;

    /// <summary>The sector's systems by squared distance to the system of <paramref name="from"/>.</summary>
    public RankedList? SystemsNear(GameState game, object? sector, GameObject? from, Func<AdvisorSystem, bool> pass, RankOrder dir)
    {
        if (sector is not AdvisorSector r || SystemOf(game, from) is not { } at) return null;
        var p = at.Definition;
        return new(r.Systems.Where(pass).Select(x => ((object)x, Squared(x.System.Definition.X - p.X, x.System.Definition.Y - p.Y))), dir, Rand);
    }

    /// <summary>Push the lists (none skipped), pick, empty the stack.</summary>
    public static object? Pick(params RankedList?[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) if (l is not null) stack.Push(l);
        return stack.Pick();
    }
}
