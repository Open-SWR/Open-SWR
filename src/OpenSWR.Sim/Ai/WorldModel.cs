using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;






/// <summary>
/// The computer player's world model: system, sector, agent and (minimal) ship records built from the side's
/// views only (<see cref="Knowledge"/>), the product catalogue, dirtied by the controller messages, refreshed per think cycle
/// (agents, ships, sectors with their systems, then the aggregates); the candidate lists, product pick, budgets and the
/// producer-facility turn of the planners. The update draws nothing; list builds draw tie flips through
/// <see cref="Rand"/>. Not built yet: fleet records and ship fields beyond the planner-14 flags (round 7), the agent aggregate
/// field list (Inferred parts below).
/// </summary>
public sealed class WorldModel(Owner side)
{
    public Owner Side { get; } = side;
    // ---- model fields ----

    /// <summary>The side.</summary>
    public int SideNumber { get; set; }
    /// <summary>The game's victory flag is 1 (headquarters mode).</summary>
    public bool HeadquartersMode { get; set; }
    /// <summary>The strategy planner rebuilds its theaters at its next theater step (set by owner and HQ changes and mode changes).</summary>
    public bool TheatersDue { get; set; }
    /// <summary>An own fleet holds a Death Star.</summary>
    public bool OwnDeathStar { get; set; }
    /// <summary>An own fleet holds an interdictor.</summary>
    public bool OwnInterdictor { get; set; }
    /// <summary>An own sector with <see cref="AiSector.AssaultBase"/>.</summary>
    public bool AssaultBase { get; set; }
    /// <summary>An own sector with <see cref="AiSector.DefenseBase"/>.</summary>
    public bool DefenseBase { get; set; }
    /// <summary>Planner 1's hub step B ended without a system.</summary>
    public bool NoConstructionSite { get; set; }
    /// <summary>An own sector with <see cref="AiSector.OwnCoruscant"/>.</summary>
    public bool OwnCoruscant { get; set; }
    /// <summary>An own sector with <see cref="AiSector.OwnLowSupport"/>.</summary>
    public bool OwnLowSupport { get; set; }
    /// <summary>An own character record Captured.</summary>
    public bool OwnCaptured { get; set; }
    /// <summary>A sector with <see cref="AiSector.Headquarters"/>.</summary>
    public bool Headquarters { get; set; }
    /// <summary>The strategy's mode choice found the side strong with nothing short (mode 5). Meaning in the original is unknown.</summary>
    public bool Dominant { get; set; }
    /// <summary>The strategy's mode choice found the strength ratio below 1.5 or turn requests pending. Meaning in the original is unknown.</summary>
    public bool Outgunned { get; set; }
    /// <summary>The Death Star shield (0x25000004) became buildable.</summary>
    public bool DeathStarShieldAvailable { get; set; }
    /// <summary>Inferred: the ship research line is exhausted.</summary>
    public bool ShipLineDone { get; set; }
    /// <summary>Inferred: the troop research line is exhausted.</summary>
    public bool TroopLineDone { get; set; }
    /// <summary>Inferred: the facility research line is exhausted.</summary>
    public bool FacilityLineDone { get; set; }
    /// <summary>Agent records changed in this update.</summary>
    public bool AgentsChanged { get; set; }
    /// <summary>Sector records changed in this update.</summary>
    public bool SectorsChanged { get; set; }
    /// <summary>Fleet records changed in this update.</summary>
    public bool FleetsChanged { get; set; }
    /// <summary>
    /// The producer-facility turn's requests, one per turn slot. The eight producer-facility planners take turns in a fixed order
    /// of slots: planner 1 (slot 0), 2, 6, 10, 7, 5, 11, then 3 (slot 7).
    /// </summary>
    public bool[] TurnRequests { get; } = new bool[TurnSlots];
    /// <summary>The slot whose turn it is.</summary>
    public int Turn { get; set; }
    /// <summary>The next turn slot.</summary>
    public int NextTurn { get; set; }
    /// <summary>The number of producer-facility turn slots.</summary>
    public const int TurnSlots = 8;
    /// <summary>Own minor characters still to come.</summary>
    public int MinorCharactersToCome { get; set; }
    // catalogue summary
    /// <summary>The largest ResearchOrder of the side's ships and fighters.</summary>
    public int MaxShipOrder { get; set; }
    /// <summary>The largest ResearchOrder of the side's troops.</summary>
    public int MaxTroopOrder { get; set; }
    /// <summary>The largest ResearchOrder of all defence and production facilities.</summary>
    public int MaxFacilityOrder { get; set; }
    /// <summary>The side's minor characters.</summary>
    public int MinorCharacters { get; set; }
    /// <summary>The ship research level.</summary>
    public int ShipLevel { get; set; }
    /// <summary>The troop research level.</summary>
    public int TroopLevel { get; set; }
    /// <summary>The facility research level.</summary>
    public int FacilityLevel { get; set; }
    /// <summary>The best buildable troop DefenseStrength.</summary>
    public int BestTroopDefense { get; set; }
    /// <summary>The best buildable fighter score vs fighters.</summary>
    public int BestFighterAntiFighterFirepower { get; set; }
    /// <summary>The best buildable armed capital ship's score vs ships.</summary>
    public int BestArmedFirepower { get; set; }
    /// <summary>The best buildable anti-fighter capital ship's score vs fighters.</summary>
    public int BestAntiFighterFirepower { get; set; }
    /// <summary>The best buildable capital ship's fighter capacity.</summary>
    public int BestFighterCapacity { get; set; }
    /// <summary>The best buildable capital ship's troop capacity.</summary>
    public int BestTroopCapacity { get; set; }
    /// <summary>The best buildable bombardment ship's bombardment value.</summary>
    public int BestBombardment { get; set; }
    /// <summary>The side's maintenance capacity (cap).</summary>
    public int MaintenanceCapacity { get; set; }
    /// <summary>The side's maintenance demand (dem).</summary>
    public int MaintenanceDemand { get; set; }
    /// <summary>10 per sector record.</summary>
    public int SystemSlots { get; set; }
    // sector aggregates
    public int OwnSystems { get; set; }
    public int EnemySystems { get; set; }
    public int NeutralSystems { get; set; }
    /// <summary>Sectors with an own system.</summary>
    public int OwnSectors { get; set; }
    /// <summary>Sectors with an enemy system.</summary>
    public int EnemySectors { get; set; }
    /// <summary>Sectors with a neutral system.</summary>
    public int NeutralSectors { get; set; }
    /// <summary>Sectors without an own or neutral key system.</summary>
    public int SectorsWithoutKey { get; set; }
    /// <summary>Sectors not all held.</summary>
    public int SectorsNotHeld { get; set; }
    public int TroopSurplus { get; set; }
    /// <summary>Own Mines.</summary>
    public int Mines { get; set; }
    /// <summary>Own Refineries.</summary>
    public int Refineries { get; set; }
    /// <summary>Troops in own systems.</summary>
    public int TroopMaintenance { get; set; }
    /// <summary>Own defences.</summary>
    public int DefenceMaintenance { get; set; }
    /// <summary>Fighters in own systems.</summary>
    public int FighterMaintenance { get; set; }
    public int FreeEnergy { get; set; }
    public int FreeRawMaterial { get; set; }
    public int EnemyUsedEnergy { get; set; }
    public int ProducerMaintenance { get; set; }
    public int EnemyProducerMaintenance { get; set; }
    public int GarrisonTarget { get; set; }
    public int Troops { get; set; }
    /// <summary>Sum of sector theater balances (&lt; 0: a theater without a fleet op).</summary>
    public int TheaterBalance { get; set; }
    /// <summary>The most enemy fighters in a system.</summary>
    public int MaxEnemyFighters { get; set; }
    /// <summary>The largest enemy troops-to-take.</summary>
    public int MaxEnemyTroopsToTake { get; set; }
    /// <summary>The smallest nonzero sector enemy troops-to-take.</summary>
    public int MinEnemyTroopsToTake { get; set; }
    /// <summary>Sum of sector <see cref="AiSector.DiplomacyTargets"/>, then div 5 + 1 when positive.</summary>
    public int DiplomacyTargets { get; set; }
    /// <summary>Sum of sector enemy-supported systems, then div 6 + 1 when positive.</summary>
    public int EnemySupported { get; set; }
    public int UprisingNoFleet { get; set; }
    public int LowSupportHubs { get; set; }
    public int UnblockadedKeySystems { get; set; }
    // fleet aggregates
    /// <summary>Own fleets' capital-ship maintenance.</summary>
    public int ShipMaintenance { get; set; }
    /// <summary>Own fleets' troop maintenance.</summary>
    public int FleetTroopMaintenance { get; set; }
    /// <summary>Own fleets' fighter maintenance.</summary>
    public int FleetFighterMaintenance { get; set; }
    public int FleetFirepower { get; set; }
    public int FleetTroops { get; set; }
    public int CarrierTroopCapacity { get; set; }
    /// <summary>Own unnamed ships (gates planner 14).</summary>
    public int UnnamedShips { get; set; }
    /// <summary>Free own fleets' undamaged armed ships.</summary>
    public int FreeArmed { get; set; }
    public int FreeAntiFighter { get; set; }
    public int FreeTroopCarriers { get; set; }
    public int FreeFirepower { get; set; }
    public int FreeAntiFighterFirepower { get; set; }
    public int FreeFighterCapacity { get; set; }
    public int FreeBombardment { get; set; }
    /// <summary>Other fleets with ships.</summary>
    public int OtherFleets { get; set; }
    public int OtherFirepower { get; set; }
    public int OtherFighterCapacity { get; set; }
    public int OtherAntiFighterFirepower { get; set; }
    /// <summary>The strongest other fleet's strength vs ships.</summary>
    public int StrongestFirepower { get; set; }
    public int StrongestAntiFighterFirepower { get; set; }
    public int StrongestFighterCapacity { get; set; }
    // agent aggregates (Inferred from the planner-5 readers)
    /// <summary>Own special forces' MaintenanceCost.</summary>
    public int SpecialForceMaintenance { get; set; }
    /// <summary>Own characters with the facility-design capability.</summary>
    public int FacilityDesigners { get; set; }
    public int ShipDesigners { get; set; }
    public int TroopTrainers { get; set; }
    /// <summary>Own special forces of type k + 1.</summary>
    public ref int SpecialForces(int k) => ref _specialForces[k];
    readonly int[] _specialForces = new int[4];
    /// <summary>Own special forces of type k + 1 with <see cref="AgentCapabilities.Unused"/> (no writer: always 0).</summary>
    public ref int MarkedSpecialForces(int k) => ref _markedSpecialForces[k];
    readonly int[] _markedSpecialForces = new int[4];
    /// <summary>The largest enemy units (a sector maximum inside the agent table, zeroed only with it).</summary>
    public int MaxEnemyUnits { get; set; }
    /// <summary>The mission op to retire (planner 13 to planner 4).</summary>
    public int MissionRetireKey { get; set; }
    /// <summary>The mission kind to create.</summary>
    public int MissionRequestKind { get; set; }
    /// <summary>The mission subtype to create.</summary>
    public int MissionRequestSubtype { get; set; }
    /// <summary>System-kind mission ops per class index.</summary>
    public ref int SystemOps(int index) => ref _systemOps[index];
    readonly int[] _systemOps = new int[13];
    /// <summary>The character op to retire.</summary>
    public int CharacterRetireKey { get; set; }
    /// <summary>The character-op type to create (1 or 6).</summary>
    public int CharacterRequestType { get; set; }
    /// <summary>Type-1 character ops.</summary>
    public int Type1CharacterOps { get; set; }
    /// <summary>Type-6 character ops.</summary>
    public int Type6CharacterOps { get; set; }
    /// <summary>The garrison and defence constants.</summary>
    public ref int Constant(ModelConstant k) => ref _constants[(int)k];
    readonly int[] _constants = new int[12];

    Owner Enemy => Side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
    public Dictionary<StarSystem, AiSystem> Systems { get; } = [];
    /// <summary>Sector records in key order.</summary>
    public List<AiSector> Sectors { get; } = [];
    /// <summary>Agent records by SdRef (key order).</summary>
    public SortedDictionary<uint, AiAgent> Agents { get; } = [];
    /// <summary>Ship records by SdRef (key order).</summary>
    public SortedDictionary<uint, AiShip> Ships { get; } = [];
    /// <summary>Product catalogue (built at init).</summary>
    public Catalogue Catalogue { get; private set; } = null!;
    /// <summary>The UI generator's rand(n) (the controller's source).</summary>
    public Func<int, int> Rand { get; set; } = n => UiRandom.Shared.Rand(n);
    /// <summary>Aggregates dirty (every dirty mark sets it).</summary>
    public bool Dirty { get; set; }

    GameState _game = null!;

    // ---- init ----

    public void Init(GameState game)
    {
        _game = game;
        SideNumber = (int)Side;
        // 1. settings victory flag (headquarters mode) -> the model's victory flag; catalogue; cap / dem.
        // shortcut: one catalogue field is not specced and stays 0.
        if (game.Settings.VictoryFlag == 1) HeadquartersMode = true;
        Catalogue = new Catalogue(game.Definitions);
        Budget(game);
        Summary(game);
        MinorCharactersToCome = MinorCharacters;
        (Turn, NextTurn) = (0, 1); // producer-facility turn
        // garrison and defence constants, in ModelConstant order
        int[] k = [3, 3, 2, 1, 1, 0, 0, 3, 9, 0, 6, 12];
        for (int i = 0; i < k.Length; i++) Constant((ModelConstant)i) = k[i];
        // 2. system records
        foreach (var s in game.Systems) Systems[s] = new AiSystem(s, this) { Key = (int)s.Ref, Side = (int)Side };
        // 3. sector records: slot i = the i-th system of the sector's contents (key order)
        foreach (var sector in game.Sectors.OrderBy(x => x.Ref))
        {
            var r = new AiSector(sector) { Key = (int)sector.Ref };
            foreach (var s in game.Systems.Where(s => s.Sector == sector).OrderBy(s => s.Ref).Take(10))
            {
                var sr = Systems[s];
                sr.Sector = r;
                r.Systems.Add(sr);
            }
            Sectors.Add(r);
            SystemSlots += 10;
        }
        // 4. ship records (fleet records: round 7)
        foreach (var o in game.Created.Where(o => Family.IsCapitalShip(o.Type)))
            if (HasView(o) && !See(o).Destroyed) Ships[o.Ref] = NewShip(o);
        // 5. agent records
        // every view of the side (fresh, Destroyed and enemy ones included); Inferred: unplaced characters have none
        foreach (var a in game.Characters.Where(c => c.Placed).Cast<Agent>().Concat(game.Created.OfType<Agent>()))
            CreateAgent(a);
        Dirty = true;
        // 6. a first full update
        Update(game);
        Snapshot(game, record: true);
    }

    // ---- views (the AI reads its side's views only) ----

    internal Seen See(GameObject o) => Knowledge.See(_game, o, Side);
    internal bool Live(GameObject o) => Knowledge.IsLive(o, Side);
    View? Frozen(GameObject o) => _game.Views.GetValueOrDefault((o, Side));
    bool HasView(GameObject o) => Live(o) || Frozen(o) is not null;
    internal GameObject? ViewParent(GameObject o) => o is ManufacturingManager m ? m.System : Live(o) ? o.Container : Frozen(o)?.Parent; // a manager view's parent is its system (never changes)
    /// <summary>shortcut: frozen views keep no Usable flag; read as Completed and not Enroute and not Destroyed. Keep Usable in the view when a reader needs the exact bit.</summary>
    internal bool Usable(GameObject o) => Live(o) ? o.Usable : Frozen(o) is { Completed: true, Enroute: false, Destroyed: false };

    Dictionary<GameObject, List<GameObject>>? _frozenContents;

    /// <summary>The system's direct contents in the side's view tree that are not Destroyed in the view (mode-3 walks), key order.</summary>
    internal List<GameObject> Contents(GameObject s)
    {
        _frozenContents ??= _game.Views.Where(e => e.Key.Item2 == Side && e.Value.Parent is not null && !Live(e.Key.Item1))
            .GroupBy(e => e.Value.Parent!).ToDictionary(g => g.Key, g => g.Select(e => e.Key.Item1).ToList());
        return [.. s.Contents.Where(Live).Concat(_frozenContents.GetValueOrDefault(s) ?? []).Where(x => !See(x).Destroyed).OrderBy(x => x.Key)];
    }

    // ---- notifications ----

    /// <summary>
    /// Object changed: walk from the object up its view parents to the first family with records: systems -> dirty the system
    /// record; characters / special forces -> create the agent record if missing (view present, not Destroyed), else dirty it;
    /// capital ships, fleets, missions -> their records (not built yet, nothing); 0xF1, 0xF2, 0xF8, [0x98, 0xA0) -> nothing.
    /// </summary>
    public void Changed(GameState game, GameObject o)
    {
        _game = game;
        for (GameObject? x = o; x is not null; x = ViewParent(x))
        {
            uint f = Family.Of(x.Type);
            switch (f)
            {
                case >= 0x90 and < 0x98:
                    if (x is StarSystem s && Systems.TryGetValue(s, out var sr)) DirtySystem(sr);
                    return;
                case >= 0x30 and < 0x40:
                    if (x is not Agent a) return;
                    if (Agents.TryGetValue(a.Ref, out var ar)) DirtyAgent(ar);
                    else if (HasView(a) && !See(a).Destroyed) CreateAgent(a);
                    return;
                case >= 0x14 and < 0x1C:
                    if (Ships.TryGetValue(x.Ref, out var ship)) ship.Dirty = true;
                    else if (HasView(x) && !See(x).Destroyed) Ships[x.Ref] = NewShip(x);
                    Dirty = true;
                    return;
                case >= 0x40 and < 0x80: // a mission view with a UserID -> its op (unless finished)
                    if (x is Mission { Kind: >= 0x50, Mandatory: false, UserId: > 0 } mission && Ops.TryGetValue((int)mission.UserId, out var op)
                        && !op.Finished.Contains(mission) && !See(mission).Destroyed)
                        (op.Mission, op.Running) = (mission, true);
                    return;
                case >= 0x08 and < 0x10: // fleet records (round 7)
                case 0xF1 or 0xF2 or 0xF8 or >= 0x98 and < 0xA0:
                    return;
            }
        }
    }

    /// <summary>
    /// "Name changed": the ship record's Named flag (kept across updates). Inferred: posted when a
    /// 0x203 runs (the sim's rename has no other effect).
    /// </summary>
    public void Named(GameState game, GameObject o)
    {
        _game = game;
        if (!Family.IsCapitalShip(o.Type)) return;
        Changed(game, o);
        if (Ships.TryGetValue(o.Ref, out var r)) r.Named = true;
    }

    /// <summary>The system and its sector dirty, and the aggregates flag.</summary>
    internal void DirtySystem(AiSystem r)
    {
        r.Dirty = true;
        if (r.Sector is { } sector) sector.Dirty = true;
        Dirty = true;
    }

    /// <summary>Sets the aggregates flag, then the record of its location.</summary>
    internal void DirtyAgent(AiAgent r)
    {
        r.Dirty = true;
        Dirty = true;
        if (r.Location is { } l) Changed(_game, l);
    }

    // ---- update: agents, fleets, sectors with their systems, aggregates ----

    public void Update(GameState game)
    {
        _game = game;
        _frozenContents = null;
        foreach (var r in Agents.Values.Where(r => r.Dirty).ToList())
        {
            UpdateAgent(r);
            AgentsChanged = Dirty = true;
        }
        // shortcut: every ship and fleet record is refreshed on every update (the original refreshes the dirty ones; all record
        // inputs are view fields whose changes are messaged, so the values agree), upgrade if an AI replay diverges on a ship /
        // fleet record value or Update shows in a profile; fleet records are created with a ship record
        foreach (var r in Ships.Values)
        {
            UpdateShip(r);
            if (ViewParent(r.Ship) is Fleet fleet && !FleetsById.ContainsKey(fleet.Ref))
                FleetsById[fleet.Ref] = new AiFleet(fleet) { Key = (int)fleet.Ref, Owner = (int)See(fleet).Owner, Free = true };
        }
        foreach (var f in FleetsById.Values) UpdateFleet(f);
        if (FleetsById.Count > 0) FleetsChanged = Dirty = true;
        foreach (var r in Sectors.Where(r => r.Dirty))
        {
            UpdateSector(r);
            SectorsChanged = Dirty = true;
        }
        if (!Dirty) return;
        if (SectorsChanged) SectorAggregates();
        if (FleetsChanged) FleetAggregates();
        if (AgentsChanged) AgentAggregates();
        AgentsChanged = SectorsChanged = FleetsChanged = Dirty = false;
    }

    /// <summary>cap / dem = the side's maintenance capacity and demand, copied at init.</summary>
    void Budget(GameState game) => (MaintenanceCapacity, MaintenanceDemand) = (game.Sides[Side].MaintenanceCapacity, game.Sides[Side].Demand);

    /// <summary>A new ship record: the catalogue class, free; dirty.</summary>
    AiShip NewShip(GameObject o)
    {
        var c = P(o.Type);
        return new(o)
        {
            Key = (int)o.Ref, Side = (int)Side, Free = true,
            Armed = c.Armed, AntiFighter = c.AntiFighter, TroopCarrier = c.TroopCarrier, ArmedTroopCarrier = c.ArmedTroopCarrier, SmallHull = c.SmallHull,
            MediumHull = c.MediumHull, LargeHull = c.LargeHull, DeathStar = c.DeathStar, Interdictor = c.Interdictor,
        };
    }

    /// <summary>
    /// Ship record update: keep the kept flags, zero the counters; from the view: own, Enroute, not
    /// w / w2 = weapon recharge / second recharge, each minus its reduction.
    /// shortcut: the reductions are not modelled (0). Damaged moving / stopped from the side's view:
    /// Damaged with the view's speed != 0 / = 0 (a frozen view keeps the values of its last copy).
    /// </summary>
    void UpdateShip(AiShip r)
    {
        r.Dirty = false;
        r.ClearUnkept();
        r.ClearCounters();
        if (!HasView(r.Ship)) return;
        var v = See(r.Ship);
        if (v.Destroyed) return;
        if (v.Owner == Side) r.Own = true;
        if (v.Enroute) r.Enroute = true;
        if (!v.Completed) (r.Named, r.NotCompleted) = (false, true);
        r.Fleet = ViewParent(r.Ship) as Fleet;
        // Damaged with the view's speed != 0 / = 0
        var (damaged, speed) = Knowledge.Damage(_game, r.Ship, Side);
        if (damaged && speed != 0) r.DamagedMoving = true;
        else if (damaged) r.DamagedStopped = true;
        var q = P(r.Ship.Type);
        int w = q.WeaponRechargeRate, w2 = q.ShieldRechargeRate;
        r.Hull = q.Hull;
        r.Bombardment = r.DamagedMoving ? 0 : q.Bombardment;
        if (q.ShieldRechargeRate != 0) r.ShieldRecharge = q.Shields * w2 / q.ShieldRechargeRate;
        r.FighterCapacity = q.FighterCapacity;
        r.TroopCapacity = q.TroopCapacity;
        r.Maintenance = q.MaintenanceCost;
        if (q.WeaponRechargeRate != 0)
        {
            r.Firepower = r.FirepowerAllFighters = q.Firepower * w / q.WeaponRechargeRate;
            if (r.AntiFighter) r.AntiFighterFirepower = r.AntiFighterFirepowerAllFighters = q.AntiFighterFirepower * w / q.WeaponRechargeRate;
        }
        r.Units = 1;
        foreach (var x in Contents(r.Ship))
        {
            var s = See(x);
            bool usable = s.Completed && !s.EnrouteActive; // EnrouteActive, not Enroute
            if (s.EnrouteActive) r.AboardEnroute = true;
            uint f = Family.Of(x.Type);
            if (f is >= 0x1C and < 0x20)
            {
                var p = P(x.Type);
                r.HasFighters = true;
                r.Fighters++;
                r.FighterMaintenance += p.MaintenanceCost;
                if (!s.Completed) r.AboardNotReady = true;
                int squad = p.SquadronSize, left = squad - x.SquadSizeDamage;
                int Damaged(int strength) => squad > 0 ? strength * left / squad : 0;
                if (s.Completed)
                {
                    // fighters count whatever the ship's AntiFighter bit
                    r.FirepowerAllFighters += Damaged(p.Firepower);
                    r.AntiFighterFirepowerAllFighters += Damaged(p.AntiFighterFirepower);
                }
                if (!usable) continue;
                r.UsableFighters++;
                r.Units++;
                r.Bombardment += p.Bombardment;
                r.Firepower += Damaged(p.Firepower);
                r.AntiFighterFirepower += Damaged(p.AntiFighterFirepower);
                r.FighterShields += Damaged(p.Shields);
            }
            else if (Family.IsTroop(x.Type))
            {
                r.HasTroops = true;
                r.Troops++;
                r.TroopMaintenance += P(x.Type).MaintenanceCost;
                if (!s.Completed || s.EnrouteActive) r.AboardNotReady = true;
                if (!usable) continue;
                r.UsableTroops++;
                r.Units++;
                r.TroopAttack += P(x.Type).Firepower;
            }
            else if (f is >= 0x30 and < 0x3C && Agents.TryGetValue(x.Ref, out var a))
            {
                bool own = a.Own, captured = a.Captured;
                if (own && captured) r.OwnCaptured = true;
                else if (own) r.OwnCharacter = true;
                else if (captured) r.EnemyCaptured = true;
                else if (!s.Enroute)
                {
                    if (a.Major) r.EnemyMajor = true;
                    else r.EnemyMinor = true;
                    if (a.VictoryCharacter && _game.Settings.VictoryFlag != 1) r.VictoryCharacters++;
                }
                if (!own && a.HasCommandKind) r.EnemyCommander = true;
                if (a.CommandKind3) r.CommandKind3 = true;
            }
        }
    }

    /// <summary>
    /// Fleet record update: keep the kept flags, zero the sums; with a live view: own (the
    /// owner at creation), Enroute, location, the flags and sums over its ship records.
    /// </summary>
    void UpdateFleet(AiFleet r)
    {
        r.ClearUnkept();
        r.ClearSums();
        if (!HasView(r.Fleet) || See(r.Fleet).Destroyed) return;
        if (r.Owner == (int)Side) r.Own = true;
        if (See(r.Fleet).Enroute) r.Enroute = true;
        r.Location = Where(r.Fleet);
        foreach (var s in ShipsOf(r))
        {
            var b = s;
            bool completed = !b.NotCompleted;
            bool ready = !(b.NotCompleted || b.Enroute);
            bool undamaged = !(b.DamagedStopped || b.DamagedMoving);
            r.Ships++;
            if (completed)
            {
                r.Hull += s.Hull;
                r.Firepower += s.Firepower;
                r.AntiFighterFirepower += s.AntiFighterFirepower;
                r.ShieldRecharge += s.ShieldRecharge;
                r.FighterShields += s.FighterShields;
                r.Bombardment += s.Bombardment;
                r.TroopAttack += s.TroopAttack;
                r.Fighters += s.Fighters;
                r.UsableFighters += s.UsableFighters;
                r.FighterCapacity += s.FighterCapacity;
                r.Troops += s.Troops;
                r.UsableTroops += s.UsableTroops;
                r.TroopCapacity += s.TroopCapacity;
                r.Units += s.Units;
                r.FirepowerAllFighters += s.FirepowerAllFighters;
                r.AntiFighterFirepowerAllFighters += s.AntiFighterFirepowerAllFighters;
            }
            r.Maintenance += s.Maintenance;
            r.TroopMaintenance += s.TroopMaintenance;
            r.FighterMaintenance += s.FighterMaintenance;
            if (!b.Named) r.Unnamed++;
            if (undamaged)
            {
                if (b.Armed) r.Armed++;
                if (b.AntiFighter) r.AntiFighter++;
                if (b.TroopCarrier) r.TroopCarriers++;
            }
            if (b.EnemyMajor) r.VictoryCharacters += s.VictoryCharacters;
            if (b.TroopCarrier) r.CarrierTroopCapacity += s.TroopCapacity;
            if (ready)
            {
                r.TroopRoom += s.TroopCapacity - s.Troops;
                r.FighterRoom += s.FighterCapacity - s.Fighters;
            }
            if (b.DeathStar) r.DeathStar = true;
            if (b.Interdictor) r.Interdictor = true;
            if (b.AboardNotReady) r.AboardNotReady = true;
            if (b.AboardEnroute) r.AboardEnroute = true;
            if (b.NotCompleted) r.NotCompletedShip = true;
            if (b.Enroute) r.EnrouteShip = true;
            if (b.DamagedMoving) r.DamagedMoving = true;
            if (b.DamagedStopped) r.DamagedStopped = true;
            if (b.HasFighters) r.HasFighters = true;
            if (b.HasTroops) r.HasTroops = true;
            if (b.EnemyMajor) r.EnemyMajor = true;
            if (b.EnemyMinor) r.EnemyMinor = true;
            if (b.EnemyCaptured) r.EnemyCaptured = true;
            if (b.OwnCaptured) r.OwnCaptured = true;
            if (b.EnemyCommander) r.EnemyCommander = true;
            if (b.OwnCharacter) r.OwnCharacter = true;
            if (b.CommandKind3) r.CommandKind3 = true;
            if (ready) r.ReadyShip = true;
            if (undamaged) r.UndamagedShip = true;
        }
        if (r.TroopRoom > 0) r.HasTroopRoom = true;
        if (r.FighterRoom > 0) r.HasFighterRoom = true;
    }

    /// <summary>The fleet record's ship records (the capital ships in the fleet's view contents with a record, key order).</summary>
    public IEnumerable<AiShip> ShipsOf(AiFleet f) => Contents(f.Fleet).Where(x => Ships.ContainsKey(x.Ref)).Select(x => Ships[x.Ref]);

    /// <summary>Fleet records by fleet key, created with their first ship record (owner fixed at creation, free 0x4).</summary>
    public SortedDictionary<uint, AiFleet> FleetsById { get; } = [];

    /// <summary>The fleet records in key order.</summary>
    public List<AiFleet> FleetRecords() => [.. FleetsById.Values];

    /// <summary>
    /// Fleet aggregates: own fleets' sums, the Death Star (0x10) and interdictor (0x20) flags, free fleets' sums, and other fleets' sums.
    /// </summary>
    void FleetAggregates()
    {
        // zero every fleet aggregate
        (ShipMaintenance, FleetTroopMaintenance, FleetFighterMaintenance, FleetFirepower, FleetTroops, CarrierTroopCapacity, UnnamedShips) = (0, 0, 0, 0, 0, 0, 0);
        (FreeArmed, FreeAntiFighter, FreeTroopCarriers, FreeFirepower, FreeAntiFighterFirepower, FreeFighterCapacity, FreeBombardment) = (0, 0, 0, 0, 0, 0, 0);
        (OtherFleets, OtherFirepower, OtherFighterCapacity, OtherAntiFighterFirepower, StrongestFirepower, StrongestAntiFighterFirepower, StrongestFighterCapacity) = (0, 0, 0, 0, 0, 0, 0);
        OwnDeathStar = OwnInterdictor = false;
        AiFleet? strongest = null;
        foreach (var f in FleetsById.Values)
        {
            if (f.Own)
            {
                ShipMaintenance += f.Maintenance;
                FleetTroopMaintenance += f.TroopMaintenance;
                FleetFighterMaintenance += f.FighterMaintenance;
                FleetFirepower += f.FirepowerAllFighters;
                FleetTroops += f.Troops;
                CarrierTroopCapacity += f.CarrierTroopCapacity;
                UnnamedShips += f.Unnamed;
                if (f.DeathStar) OwnDeathStar = true;
                if (f.Interdictor) OwnInterdictor = true;
                if (f.Free)
                {
                    FreeArmed += f.Armed;
                    FreeAntiFighter += f.AntiFighter;
                    FreeTroopCarriers += f.TroopCarriers;
                    FreeFirepower += f.FirepowerAllFighters;
                    FreeAntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
                    FreeFighterCapacity += f.FighterCapacity;
                    FreeBombardment += f.Bombardment;
                }
            }
            else if (f.Ships > 0)
            {
                OtherFleets++;
                OtherFirepower += f.FirepowerAllFighters;
                OtherFighterCapacity += f.FighterCapacity;
                OtherAntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
                if (strongest is null || f.FirepowerAllFighters > strongest.FirepowerAllFighters) strongest = f;
            }
        }
        if (strongest is not null) (StrongestFirepower, StrongestAntiFighterFirepower, StrongestFighterCapacity) = (strongest.FirepowerAllFighters, strongest.AntiFighterFirepowerAllFighters, strongest.FighterCapacity);
    }

    /// <summary>
    /// Agent table (zero the agent aggregates, sector maximum included; clear 0x200). Inferred from the planner-5 readers,
    /// the per-record sums are not specced: the first aggregate is the MaintenanceCost of the own special forces,
    /// and the next four are their counts per special-force type.
    /// </summary>
    void AgentAggregates()
    {
        // zero the agent aggregates and the sector maximum inside that range (as coded)
        (SpecialForceMaintenance, FacilityDesigners, ShipDesigners, TroopTrainers, MaxEnemyUnits) = (0, 0, 0, 0, 0);
        for (int k = 0; k < 4; k++) SpecialForces(k) = MarkedSpecialForces(k) = 0;
        OwnCaptured = false;
        foreach (var r in Agents.Values)
        {
            if (!r.Own) continue;
            SpecialForceMaintenance += r.Maintenance;
            if (r.Character)
            {
                if (r.Captured) OwnCaptured = true;
                if (r.Can.FacilityDesign) FacilityDesigners++;
                if (r.Can.ShipDesign) ShipDesigners++;
                if (r.Can.TroopTraining) TroopTrainers++;
                continue;
            }
            for (int k = 0; k < 4; k++)
                if (r.Can.SpecialForceKind == k + 1)
                {
                    SpecialForces(k)++;
                    if (r.Can.Unused) MarkedSpecialForces(k)++; // no writer: stays 0
                }
        }
    }

    // ---- agent record (create and update) ----

    /// <summary>Mission type -> the capability it shows when the unit alone may run it.</summary>
    static readonly (uint Type, Action<AgentCapabilities> Set)[] Capabilities =
    [
        (0x51000010, c => c.Diplomacy = true), (0x61000011, c => c.Rescue = true), (0x69000012, c => c.Sabotage = true),
        (0x52000013, c => c.Espionage = true), (0x62000017, c => c.Abduction = true), (0x57000080, c => c.Subdue = true),
        (0x63000081, c => c.Assassination = true), (0x6A000041, c => c.DeathStarSabotage = true), (0x56000040, c => c.Incite = true),
        (0x54000015, c => c.Recon = true), (0x55000016, c => c.Recruitment = true), (0x53000020, c => c.ShipDesign = true),
        (0x53000022, c => c.TroopTraining = true), (0x53000021, c => c.FacilityDesign = true),
    ];

    void CreateAgent(Agent a)
    {
        var r = new AiAgent(a) { Key = (int)a.Ref, Side = (int)Side };
        var v = See(a);
        r.Unassigned = true;
        if (a is Character c)
        {
            r.Character = true;
            // a captured character's owner is its captor
            if (v.Owner == Side && !v.Captured || v.Owner != Side && v.Captured) r.Own = true;
            else r.Enemy = true;
            if (v.Captured) r.Captured = true;
            if (c.Ref is 0x32000242 or 0x30000240 or 0x34000280 or 0x35000281) r.VictoryCharacter = true;
            if (c.Ref == 0x34000280) r.Palpatine = true;
            if (c.Ref is 0x32000242 or 0x30000240 or 0x34000280 or 0x35000281 or 0x31000241 or 0x33000243) r.Major = true;
            else
            {
                if (c.Definition.CanBeAdmiral != 0) r.Can.Admiral = true;
                if (c.Definition.CanBeCommander != 0) r.Can.Commander = true;
                if (c.Definition.CanBeGeneral != 0) r.Can.General = true;
            }
        }
        else if (Family.IsSpecialForce(a.Type))
        {
            r.SpecialForce = true;
            if (v.Owner == Side) r.Own = true;
            else r.Enemy = true;
            r.Can.SpecialForceKind = (int)(((_game.Definitions.Resolve(a.Type)?.Common.Index ?? 0) - 1) % 4) + 1;
        }
        // capability probe: the team-only type list of a 0x240 for this unit alone (never submitted)
        foreach (var type in MissionEligibility.TypesForTeam(_game, Side, [a], []))
            foreach (var (t, set) in Capabilities)
                if (type.Common.Ref == t) set(r.Can);
        Agents[a.Ref] = r;
        // the agent container count is the model's own minor characters still to come, -1 per own non-major character record created
        // (both callers, again after a 0x302 detach; nothing increments it)
        if (r.Character && !r.Major && r.Own) MinorCharactersToCome--;
        DirtyAgent(r);
    }

    void UpdateAgent(AiAgent r)
    {
        var a = r.Agent;
        r.ClearUnkept();
        r.ClearSkills();
        if (!HasView(a)) return;
        var v = See(a);
        if (v.Destroyed) return;
        r.Owner = (int)v.Owner;
        // shortcut: agent fields beyond the view snapshot (OnMission, OnMandatoryMission, Injury, CommandKind, Force, skills) read
        // from the real object. Upgrade with the view fields when the planners read them (rounds 6, 8).
        if (a.OnMission) r.OnMission = true;
        if (a.Mission is { Mandatory: true }) r.MandatoryMission = true;
        if (Family.IsSpecialForce(a.Type)) r.Maintenance = P(a.Type).MaintenanceCost;
        if (v.Enroute) r.Enroute = true;
        if (!v.Completed) r.NotCompleted = true;
        if (a is Character c)
        {
            if (v.Captured) r.Captured = true;
            if (c.Injury != 0) r.Injured = true;
            // a research capability whose line is not exhausted
            var caps = r.Can;
            if (caps.ShipDesign && !ShipLineDone || caps.TroopTraining && !TroopLineDone
                || caps.FacilityDesign && !FacilityLineDone) r.Specialist = true;
            if (c.CommandKind == 1) r.CommandKind1 = true;
            else if (c.CommandKind == 2) r.CommandKind2 = true;
            else if (c.CommandKind == 3) r.CommandKind3 = true;
        }
        for (int k = 0; k < 8; k++) r.Skill(k) = Missions.Enhanced(_game, a, k);
        if (a is Character ch)
        {
            r.Force = ch.Force;
            bool lukeOrVader = ch.Ref is 0x32000242 or 0x35000281;
            if (ch.Force is >= 1 and <= 99 && !lukeOrVader) r.ForceTrainee = true;
            if (ch.Force >= 100 && lukeOrVader) r.ForceMaster = true;
        }
        if (r.Can.Diplomacy && r.Skill(SkillIndex.Diplomacy) > 79) r.Specialist = true;
        if (r.Location is null && ViewParent(a) is { } p) r.Location = p;
    }

    // ---- system record ----

    /// <summary>Catalogue value of a unit type (0 without a record).</summary>
    Product P(uint type) => Catalogue[type] ?? Product.None;

    void UpdateSystem(AiSystem r)
    {
        if (!r.Dirty) return;
        r.Dirty = false;
        var s = r.System;
        r.ClearForUpdate();
        r.ClearCounters();
        var v = See(s);
        // 1-2
        r.Owner = (int)v.Owner;
        if (v.Destroyed) r.Destroyed = true;
        if (Family.Of(s.Type) is >= 0x92 and < 0x94) r.Rim = true;
        bool own = v.Owner == Side, enemy = v.Owner == Enemy;
        if (own) r.Own = r.OwnedOrPopulated = true;
        else if (enemy) r.Enemy = r.OwnedOrPopulated = true;
        else r.Neutral = true;
        // 3
        if (!v.Explored) { r.NotOwn = true; return; }
        // 4. shortcut: views keep no Energy / RawMaterial / used counts; read from the real system. Free energy / raw material use the
        // live counts of the owner's Existing facilities / Mines at update time (Inferred mechanism), not the stored
        // the stored fields of the system, which keep serving the sim's energy rules
        int e = s.Energy;
        int used = Math.Min(s.Contents.Count(x => Family.IsFacility(x.Type) && x.Existing && x.Owner == s.Owner), e);
        int mined = Math.Min(s.Contents.Count(x => Family.Of(x.Type) == 0x2C && x.Existing && x.Owner == s.Owner), s.RawMaterials);
        r.Explored = true;
        r.Energy = e;
        r.FreeEnergy = e - used;
        r.FreeRawMaterial = s.RawMaterials - mined;
        // 5. the view's Blockade; the day is kept for neutral or enemy systems only
        if (v.Blockade)
        {
            r.Blockade = true;
            if (!own) r.BlockadeDay = _game.Day;
        }
        // 6
        var contents = Contents(s);
        bool hq = contents.Any(x => Family.Of(x.Type) is >= 0x20 and < 0x22 && See(x).Owner == v.Owner);
        if (s.Id == 0x109) { r.KeySystem = true; r.Coruscant = true; }
        if (s.Id == 0x121) r.Yavin = true;
        if (hq) { r.KeySystem = true; r.Headquarters = true; r.HeadquartersSeen = true; }
        // 7
        if (own) r.Support = 100;
        bool role = r.HasRole, special = (r.HasRole || r.KeySystem);
        // 8. troops (against the catalogue summary: the best buildable troop DefenseStrength)
        foreach (var t in contents.Where(x => Family.IsTroop(x.Type)))
        {
            r.Troops++;
            r.TroopMaintenance += P(t.Type).MaintenanceCost;
            if (Usable(t)) r.UsableTroops++;
            if (!own) continue;
            bool attack = P(t.Type).AttackTroop, defense = P(t.Type).DefenseTroop;
            if (!special)
            {
                // TroopDefense counts for C only together with TroopAttack
                if (attack)
                {
                    r.AttackTroops = true;
                    if (defense) r.DefenseTroops = true;
                }
                else { r.GarrisonTroops++; r.DefenseGarrison = true; }
            }
            else if (P(t.Type).AntiFighterFirepower < BestTroopDefense)
            {
                if (attack) r.AttackTroops = true;
                if (defense) r.DefenseTroops = true;
            }
            else
            {
                r.GarrisonTroops++;
                if (attack) r.AttackGarrison = true;
                if (defense) r.DefenseGarrison = true;
            }
        }
        // 9. fleets [0x08, 0x10) with a fleet record
        foreach (var x in contents.Where(x => Family.Of(x.Type) is >= 0x08 and < 0x10))
        {
            if (!FleetsById.TryGetValue(x.Ref, out var f)) continue;
            var fb = f;
            if (!fb.Own && fb.ReadyShip)
            {
                r.EnemyFirepower += f.FirepowerAllFighters;
                r.EnemyAntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
                r.EnemyShieldRecharge += f.ShieldRecharge;
                r.EnemyFleetFighters += f.Fighters;
                r.EnemyUnits += f.Units;
                r.EnemyFleet = true;
                r.EnemyFleetSeen = true;
                if (fb.DeathStar)
                {
                    r.EnemyDeathStar = true;
                    if (Side == Owner.Alliance) r.EnemyDeathStarSeen = true;
                }
            }
            if (fb.EnemyMajor) r.EnemyMajorAboard = r.EnemyCharacterAboard = true;
            if (fb.EnemyMinor) r.EnemyCharacterAboard = true;
            if (fb.EnemyCommander) r.EnemyCommander = true;
            if (fb.OwnCaptured) r.OwnCapturedAboard = true;
        }
        // 10. characters, through their agent records
        foreach (var x in contents.Where(x => Family.Of(x.Type) is >= 0x30 and < 0x40))
        {
            if (!Agents.TryGetValue(x.Ref, out var a) || !a.Character) continue;
            bool captured = a.Captured, major = a.Major;
            if (!a.Enemy)
            {
                if (captured) { r.OwnCaptured = true; r.Captured++; }
                else if (major) r.OwnMajors++;
                else r.OwnMinors++;
                continue;
            }
            if (captured) { r.EnemyCaptured = true; r.Captured++; continue; }
            if (!a.Enroute) r.EnemyCharacter = true;
            if (a.HasCommandKind)
                r.EnemyCommander = true;
                if (a.CommandKind3) r.CommandKind3 = true;
            if (major)
            {
                r.EnemyMajor = true;
                r.EnemyMajors++;
                if (a.VictoryCharacter) r.VictoryCharacters++;
            }
            else r.EnemyMinors++;
        }
        // 11
        if (own && r.FreeEnergy > 0)
        {
            r.HasFreeEnergy = true;
            if (r.FreeRawMaterial > 0) r.HasFreeRawMaterial = true;
        }
        // 12. the owner's Mines / Refineries
        r.Mines = contents.Count(x => Family.Of(x.Type) == 0x2C && See(x).Owner == v.Owner);
        r.Refineries = contents.Count(x => Family.Of(x.Type) == 0x2D && See(x).Owner == v.Owner);
        // 13. production facilities: shipyards (manager kind 0), training (2), construction yards (1)
        var shipyards = contents.Where(x => Family.Of(x.Type) == 0x28).ToList();
        foreach (var x in shipyards)
        {
            r.Shipyards++;
            r.ProducerMaintenance += P(x.Type).MaintenanceCost;
            r.ShipyardMaintenance += P(x.Type).MaintenanceCost;
        }
        if (shipyards.Any(Usable))
        {
            r.UsableProducer = true;
            if (r.Shipyards > 0 && !r.Unrest && IdleManager(s, ManufacturingKind.Ship)) r.IdleShipyard = true;
        }
        var training = contents.Where(x => Family.Of(x.Type) == 0x29).ToList();
        foreach (var x in training)
        {
            r.TrainingFacilities++;
            r.ProducerMaintenance += P(x.Type).MaintenanceCost;
            r.TrainingMaintenance += P(x.Type).MaintenanceCost;
        }
        if (training.Any(Usable))
        {
            r.UsableProducer = true;
            if (r.TrainingFacilities > 0 && !r.Unrest && IdleManager(s, ManufacturingKind.Training)) r.IdleTraining = true;
        }
        var yards = contents.Where(x => Family.Of(x.Type) == 0x2A).ToList();
        foreach (var x in yards)
        {
            r.ConstructionYards++;
            r.ProducerMaintenance += P(x.Type).MaintenanceCost;
            r.ConstructionMaintenance += P(x.Type).MaintenanceCost;
        }
        if (yards.Any(Usable))
        {
            r.UsableProducer = true;
            if (r.ConstructionYards > 0 && !r.Unrest && IdleManager(s, ManufacturingKind.Construction)) r.IdleConstruction = true;
        }
        // 14. defences
        bool shieldUsable = false;
        foreach (var x in contents)
            switch (Family.Of(x.Type))
            {
                case 0x24:
                    r.GenCores++;
                    r.DefenceMaintenance += P(x.Type).MaintenanceCost;
                    if (Usable(x)) r.GenCoreStrength += P(x.Type).AntiFighterFirepower;
                    break;
                case 0x25:
                    r.DeathStarShields++;
                    r.DefenceMaintenance += P(x.Type).MaintenanceCost;
                    if (Usable(x)) { r.UsableDeathStarShield = true; shieldUsable = true; }
                    break;
                case 0x22 or 0x23:
                    r.Batteries++;
                    r.DefenceMaintenance += P(x.Type).MaintenanceCost;
                    if (Usable(x)) r.BatteryStrength += P(x.Type).Firepower;
                    break;
            }
        if (r.GenCores > 1) r.ManyGenCores = true;
        // 15
        if (!v.Populated)
        {
            if (r.HasFreeEnergy)
            {
                r.RefinerySite = true;
                if (r.HasFreeRawMaterial) r.MineSite = true;
            }
        }
        else Populated(r, v, own, enemy, e, hq, role, special, contents, shieldUsable);
        // 16
        if (own) r.OwnExplored = r.Claimed = true;
        else
        {
            r.NotOwn = true;
            if (enemy) r.Claimed = true;
        }
    }

    /// <summary>The system's manager of the kind has an empty queue (Inferred: the manager's queue field is 0).</summary>
    static bool IdleManager(StarSystem s, ManufacturingKind kind) => s.Managers[(int)kind] is { } m && m.Orders.Count == 0;

    /// <summary>System update step 15, populated systems.</summary>
    void Populated(AiSystem r, Seen v, bool own, bool enemy, int e, bool hq, bool role, bool special, List<GameObject> contents, bool shieldUsable)
    {
        var s = r.System;
        bool coruscant = s.Id == 0x109, enemyFleet = r.EnemyFleet;
        // 15.1
        if (v.Uprising) r.Uprising = true;
        r.OwnedOrPopulated = r.Populated = true;
        int support = Side == Owner.Alliance ? v.Support : 100 - v.Support; // the AI side's share
        r.Support = support;
        if (support < 70) r.LowSupport = true;
        // 15.2 fighters
        foreach (var x in contents.Where(x => Family.Of(x.Type) is >= 0x1C and < 0x20))
        {
            r.Fighters++;
            r.FighterMaintenance += P(x.Type).MaintenanceCost;
            if (own && (!special || BestFighterAntiFighterFirepower <= P(x.Type).AntiFighterFirepower)) r.StrongFighters++;
            if (!Usable(x)) continue;
            // damaged strengths (each stat times x div a stat); Inferred:
            // x = squad size minus SquadSizeDamage
            if (P(x.Type).SquadronSize is > 0 and var squad)
            {
                int left = squad - x.SquadSizeDamage;
                r.FighterFirepower += P(x.Type).Firepower * left / squad;
                r.FighterAntiFighterFirepower += P(x.Type).AntiFighterFirepower * left / squad;
                r.FighterShields += P(x.Type).Shields * left / squad;
            }
            r.UsableFighters++;
            if (own) r.OwnFighters = true;
        }
        // 15.3
        if (own && !r.Unrest && !role)
        {
            r.CalmWithoutRole = true;
            int c = r.Batteries + r.DeathStarShields + r.Refineries + r.Mines + r.GenCores + (hq ? 1 : 0);
            if (c < e) r.RefinerySite = true;
            if (c < e && r.FreeRawMaterial > 0) r.MineSite = true;
        }
        bool neutral = !own && !enemy;
        if (neutral)
        {
            // 15.4
            if (support < 90) r.UnrestTroops = Math.Min(6, r.UnrestTroops + (99 - support) / 10);
            r.TroopsToTake = r.Batteries + r.UnrestTroops;
            if (!enemyFleet) r.DiplomacyTarget = true;
            if (coruscant) r.VictoryCharacters++;
            return;
        }
        if (enemy)
        {
            // 15.5
            if (support < 80) r.UnrestTroops = Math.Min(6, r.UnrestTroops + (89 - support) / 10);
            r.TroopsToTake = r.Troops + r.UnrestTroops + r.Batteries;
            if (support > 50) r.EnemySupported = true;
            r.EnemyUnits += r.UsableTroops + r.UsableFighters;
            if (coruscant) r.VictoryCharacters++;
            if (r.GenCoreStrength > 0) r.EnemyGenCores = true;
            if (shieldUsable) r.EnemyDeathStarShield = true;
            if (r.BatteryStrength > 0) r.EnemyBatteries = true;
            if (r.UsableTroops > 0) r.EnemyTroops = true;
            if (r.UsableFighters > 0) r.EnemyFighters = true;
            if ((r.Mines > 0 || r.Refineries > 0 || r.Shipyards > 0 || r.TrainingFacilities > 0 || r.ConstructionYards > 0 || r.DeathStarShields > 0
                || r.GenCores > 0) && r.Batteries == 0) r.NoBatteries = true;
            if (r.Shipyards > 0 || r.TrainingFacilities > 0 || r.ConstructionYards > 0) r.EnemyProducers = true;
            if (r.Mines > 0 || r.Refineries > 0) r.EnemyMines = true;
            return;
        }
        // 15.6 own: garrison and defence targets with the model constants
        int K(ModelConstant k) => Constant(k);
        if (support < 70) r.UnrestTroops = Math.Min(6, r.UnrestTroops + (79 - support) / 10);
        int rr = Side == Owner.Empire ? 1 : 0, g, b;
        if (coruscant)
        {
            if (e < rr + 2) (g, b) = (e - rr, 0);
            else
            {
                (g, b) = (K(ModelConstant.CoruscantGenCores), K(ModelConstant.CoruscantBatteries));
                if (e < rr + K(ModelConstant.CoruscantBatteries) + K(ModelConstant.CoruscantGenCores)) b = e - K(ModelConstant.CoruscantGenCores) - rr;
            }
            if (Side == Owner.Empire && DeathStarShieldAvailable && OwnDeathStar && r.DeathStarShields == 0) r.DeathStarShieldWanted = true;
        }
        else if (role)
            (g, b) = K(ModelConstant.RoleBatteries) + K(ModelConstant.RoleGenCores) < e ? (K(ModelConstant.RoleGenCores), K(ModelConstant.RoleBatteries)) : K(ModelConstant.GenCores) < e ? (K(ModelConstant.GenCores), 0) : (0, 0);
        else
            (g, b) = r.UnrestTroops > 0 && K(ModelConstant.Batteries) + K(ModelConstant.GenCores) < e ? (K(ModelConstant.GenCores), K(ModelConstant.Batteries)) : (0, 0);
        r.GenCoreTarget = g;
        r.BatteryTarget = b;
        int d1 = r.GenCores - g, d2 = r.Batteries - b;
        if (d1 < 0) r.GenCoreShort = true;
        if (d1 <= 0 && d2 <= 0)
        {
            r.DefenceBalance = d1 + d2;
            if (r.DefenceBalance < 0) r.DefenceShort = true;
        }
        else r.DefenceBalance = d1 <= 0 ? d2 : d2 <= 0 ? d1 : d1 + d2;
        if (v.Uprising) r.UnrestTroops *= 2;
        bool key = r.KeySystem;
        int t = key ? K(ModelConstant.KeyGarrison) : role ? K(ModelConstant.RoleGarrison) : K(ModelConstant.Garrison);
        r.GarrisonTarget = t = Math.Max(t, r.UnrestTroops);
        if (!(r.Blockade && (r.AssaultBase || r.DefenseBase)))
        {
            if (r.UsableTroops < t) { r.GarrisonShort = true; r.TroopShortfall = t - r.UsableTroops; }
            else
            {
                if (r.UsableTroops > t) { r.GarrisonSurplus = true; r.TroopSurplus = r.UsableTroops - t; }
                if (r.GarrisonTroops < t) { r.GarrisonTroopsShort = true; r.TroopShortfall = t - r.GarrisonTroops; }
            }
        }
        int fk = key ? K(ModelConstant.KeyFighterMargin) : role ? K(ModelConstant.RoleFighterMargin) : K(ModelConstant.FighterMargin);
        if (!key && !role && r.GarrisonTroops < 1) (r.FighterBalance, r.UsableFighterBalance) = (r.Fighters, r.Fighters);
        else (r.FighterBalance, r.UsableFighterBalance) = (r.StrongFighters - fk, r.UsableFighters - fk);
        if (r.FighterBalance < 0) r.FighterShort = true;
        if (r.UsableFighterBalance < 0) r.UsableFighterShort = true;
        int p = (r.AnyConstructionRole ? r.ConstructionYards : 0) + (r.ShipyardRole ? r.Shipyards : 0)
            + (r.TrainingRole ? r.TrainingFacilities : 0);
        r.FreeSlots = e - (p + Math.Max(r.GenCores, r.GenCoreTarget) + Math.Max(r.Batteries, r.BatteryTarget) + (hq ? 1 : 0));
        if (!r.Unrest)
        {
            bool Yard(uint family) => contents.Any(x => Family.Of(x.Type) == family && Usable(x));
            if (MinorCharactersToCome > 0 && s.Id != 0x121) r.CharacterSite = true;
            if (r.ShipyardRole && !ShipLineDone && Yard(0x28) && r.UsableTroops > 0) r.ShipProduction = true;
            if (r.TrainingRole && !TroopLineDone && Yard(0x29) && r.UsableTroops > 0) r.TrainingProduction = true;
            if (r.AnyConstructionRole && !FacilityLineDone && Yard(0x2A) && r.UsableTroops > 0) r.ConstructionProduction = true;
        }
        if (r.Uprising && !enemyFleet) r.UprisingNoFleet = true;
        if (r.LowSupport && !enemyFleet && !r.Uprising) r.DiplomacyTarget = true;
    }

    // ---- sector record ----

    void UpdateSector(AiSector x)
    {
        x.Dirty = false;
        x.ClearForUpdate(); // shortcut: the derived needs are cleared with the sector (the update "clears its derived flags")
        x.ClearSums();
        x.MinUnguardedGenCoreStrength = 100000;
        foreach (var y in x.Systems)
        {
            UpdateSystem(y);
            AddSystem(x, y);
        }
        if (!x.EnemyNoBatteries) x.MinUnguardedGenCoreStrength = 0;
        if (x.OwnSystems > 0) x.Support /= x.OwnSystems;
        if (x.Explored == 10) x.AllExplored = true;
        if (x.Populated == 10) x.AllPopulated = true;
        if (x.Held == 10) x.AllHeld = true;
        if (x.VictoryCharacters > 0) x.HasVictoryCharacters = true;
        // the theater kind (planner 13) and its fleet op
        if (x.TheaterKind == 0 && x.FleetOpKey != 0) x.TheaterBalance = 1;
        if (x.TheaterKind != 0)
        {
            x.Theater = true;
            if (x.FleetOpKey == 0) { x.TheaterBalance = -1; x.OpenTheater = true; }
        }
    }

    /// <summary>Adds one system record to its sector's flags and sums.</summary>
    static void AddSystem(AiSector x, AiSystem y)
    {
        x.VictoryCharacters += y.VictoryCharacters;
        if (y.Explored) x.Explored++;
        if (y.Populated) x.Populated++;
        if (y.OwnedOrPopulated) x.Held++;
        if (y.Destroyed) x.Destroyed++;
        if (y.Rim) x.Rim = true;
        if (y.Coruscant) x.Coruscant = true;
        if (y.Headquarters) x.Headquarters = true;
        if (y.EnemyFleet) x.EnemyFleet = true;
        // systems counted per target, with the sector's target mark
        if (y.OwnExplored) x.OwnExploredSystems++;
        if (y.CharacterSite) { x.CharacterSites++; x.HasCharacterSite = true; }
        if (y.ShipProduction) { x.ShipProduction++; x.HasShipProduction = true; }
        if (y.TrainingProduction) { x.TrainingProduction++; x.HasTrainingProduction = true; }
        if (y.ConstructionProduction) { x.ConstructionProduction++; x.HasConstructionProduction = true; }
        if (y.UprisingNoFleet) { x.UprisingNoFleet++; x.HasUprisingNoFleet = true; }
        if (y.DiplomacyTarget)
        {
            x.DiplomacyTargets++;
            x.HasDiplomacyTarget = true;
            if (!y.DiplomacyMark) x.OpenDiplomacyTarget = true;
        }
        if (y.NotOwn) { x.NotOwn++; x.HasNotOwn = true; }
        if ((y.EnemyCharacter || y.EnemyCharacterAboard)) { x.EnemyCharacters++; x.HasEnemyCharacter = true; }
        if ((y.EnemyMajor || y.EnemyMajorAboard)) { x.EnemyMajors++; x.HasEnemyMajor = true; }
        if (y.EnemySupported) { x.EnemySupported++; x.HasEnemySupported = true; }
        if (y.Claimed) { x.Claimed++; x.HasClaimed = true; }
        if ((y.OwnCaptured || y.OwnCapturedAboard)) { x.OwnCaptured++; x.HasOwnCaptured = true; }
        if (y.EnemyDeathStarSeen) { x.EnemyDeathStars++; x.HasEnemyDeathStar = true; }
        if (y.EnemyMines) x.HasEnemyMines = true;
        if (y.EnemyProducers) x.HasEnemyProducers = true;
        if (y.EnemyFleetSeen) x.EnemyFleetSeen = true;
        bool role = y.HasRole;
        if (y.Own)
        {
            // own system
            x.Own = true;
            x.OwnSystems++;
            x.Support += y.Support;
            x.Energy += y.Energy;
            x.Mines += y.Mines;
            x.Refineries += y.Refineries;
            x.GenCores += y.GenCores;
            x.DeathStarShields += y.DeathStarShields;
            x.Batteries += y.Batteries;
            x.Fighters += y.Fighters;
            x.FreeRawMaterial += y.FreeRawMaterial;
            x.FreeEnergy += y.FreeEnergy;
            x.GarrisonTarget += y.GarrisonTarget;
            x.Troops += y.Troops;
            x.TroopMaintenance += y.TroopMaintenance;
            x.DefenceMaintenance += y.DefenceMaintenance;
            x.FighterMaintenance += y.FighterMaintenance;
            x.ProducerMaintenance += y.ProducerMaintenance;
            x.FighterFirepower += y.FighterFirepower;
            x.EnemyFirepower += y.EnemyFirepower;
            x.EnemyAntiFighterFirepower += y.EnemyAntiFighterFirepower;
            x.EnemyFleetFighters += y.EnemyFleetFighters;
            if (y.ShipyardRole) x.RoleShipyards += y.Shipyards; else x.Shipyards += y.Shipyards;
            if (y.TrainingRole) x.RoleTrainingFacilities += y.TrainingFacilities; else x.TrainingFacilities += y.TrainingFacilities;
            if (y.AnyConstructionRole) x.RoleConstructionYards += y.ConstructionYards; else x.ConstructionYards += y.ConstructionYards;
            bool calm = !y.Unrest, unblockaded = !y.Blockade;
            if (unblockaded) x.OwnUnblockaded = true;
            if (calm && !y.Yavin && y.FreeSlots > 0)
            {
                if (!y.ShipyardRole) x.ShipyardSlot = true;
                if (!y.TrainingRole) x.TrainingSlot = true;
                if (!y.AnyConstructionRole) x.ConstructionSlot = true;
            }
            if (calm)
            {
                if (y.MineSite) x.MineSite = true;
                if (y.RefinerySite) x.RefinerySite = true;
                if (y.HasFreeEnergy) x.HasFreeEnergy = true;
                if (y.HasFreeRawMaterial) x.HasFreeRawMaterial = true;
                if (y.ConstructionRole && y.IdleConstruction) x.IdleConstructionRole = true;
            }
            if (y.IdleConstruction && !y.AnyConstructionRole) x.IdleConstruction = true;
            if (role)
            {
                x.HasRoleSystem = true;
                if (y.ShipyardRole && unblockaded) x.ShipyardRole = true;
            }
            if (y.Base)
            {
                x.Base = true;
                if (y.AssaultBase) x.AssaultBase = true;
                else if (y.DefenseBase) x.DefenseBase = true;
            }
            if (y.Hub)
            {
                x.LowSupportHubs++;
                if (!y.CharacterClaim) x.Hub = true;
            }
            if (y.DefenceBalance < 0)
            {
                x.DefenceShort = true;
                x.DefenceShortSystems++;
                if (role) x.RoleDefenceShort = true;
            }
            if (y.DefenceBalance > 0) { x.DefenceSurplus = true; x.DefenceSurplusSystems++; }
            if (!y.KeySystem && y.Batteries > 0 && y.GenCores > 0) x.DefendedNonKey = true;
            if (y.GarrisonSurplus)
            {
                x.GarrisonSurplus = true;
                x.TroopSurplus += y.TroopSurplus;
                if (y.AttackGarrison) x.AttackGarrisonSurplus = true;
                if (y.DefenseGarrison) x.DefenseGarrisonSurplus = true;
                if (y.AttackTroops) x.AttackTroopSurplus = true;
                if (y.DefenseTroops) x.DefenseTroopSurplus = true;
            }
            if (y.GarrisonShort)
            {
                x.GarrisonShort = true;
                x.TroopShortfall += y.TroopShortfall;
                if (role) x.RoleGarrisonShort = true;
            }
            if (y.GarrisonTroopsShort) x.GarrisonTroopsShort = true;
            if (y.UsableFighterBalance > 0 && !y.Base && unblockaded)
            {
                x.HasUsableFighterSurplus = true;
                x.UsableFighterSurplus += y.UsableFighterBalance;
            }
            if (y.FighterBalance < 0)
            {
                x.FighterShort = true;
                x.FighterShortSystems++;
                if (role) x.RoleFighterShort = true;
                if (y.UsableFighterShort) x.UsableFighterShort = true;
            }
            if (y.OwnFighters) x.OwnFighters = true;
            if (y.Uprising) x.OwnUprising = true;
            if (y.KeySystem)
            {
                x.KeySystem = true;
                if (y.Coruscant) x.OwnCoruscant = true;
                if (unblockaded) x.UnblockadedKeySystems++;
            }
            if (y.LowSupport) x.OwnLowSupport = true;
        }
        else if (y.Enemy)
        {
            // enemy system
            x.Enemy = true;
            x.EnemySystems++;
            x.EnemyMines += y.Mines;
            x.EnemyRefineries += y.Refineries;
            x.EnemyGenCores += y.GenCores;
            x.EnemyDeathStarShields += y.DeathStarShields;
            x.EnemyProducerMaintenance += y.ProducerMaintenance;
            x.OtherShipyards += y.Shipyards;
            x.OtherTrainingFacilities += y.TrainingFacilities;
            x.OtherConstructionYards += y.ConstructionYards;
            x.EnemyFirepower += y.EnemyFirepower;
            x.EnemyAntiFighterFirepower += y.EnemyAntiFighterFirepower;
            x.EnemyFleetFighters += y.EnemyFleetFighters;
            x.EnemyUsedEnergy += y.Energy - y.FreeEnergy;
            x.EnemyTroopsToTake += y.TroopsToTake;
            x.MaxEnemyTroopsToTake = Math.Max(x.MaxEnemyTroopsToTake, y.TroopsToTake);
            x.MaxEnemyFighters = Math.Max(x.MaxEnemyFighters, y.Fighters);
            x.MaxEnemyFighterAntiFighterFirepower = Math.Max(x.MaxEnemyFighterAntiFighterFirepower, y.FighterAntiFighterFirepower);
            x.MaxEnemyUnits = Math.Max(x.MaxEnemyUnits, y.EnemyUnits);
            if (y.KeySystem) x.EnemyKeySystem = true;
            if (y.NoBatteries)
            {
                x.EnemyNoBatteries = true;
                x.MinUnguardedGenCoreStrength = Math.Min(x.MinUnguardedGenCoreStrength, y.GenCoreStrength);
            }
            if (!y.ManyGenCores) x.EnemyFewGenCores = true;
            if (!y.CommandKind3) x.EnemyNoCommandKind3 = true;
            if (y.EnemyMines || y.EnemyProducers || y.EnemyGenCores || y.EnemyBatteries || y.EnemyTroops || y.EnemyFighters || y.EnemyFleetSeen
                || y.HeadquartersSeen) x.EnemyAssets = true;
        }
        else if (y.Neutral)
        {
            // neutral system
            x.Neutral = true;
            x.NeutralSystems++;
            x.OtherShipyards += y.Shipyards;
            x.OtherTrainingFacilities += y.TrainingFacilities;
            x.OtherConstructionYards += y.ConstructionYards;
            x.EnemyFirepower += y.EnemyFirepower;
            x.EnemyAntiFighterFirepower += y.EnemyAntiFighterFirepower;
            x.EnemyFleetFighters += y.EnemyFleetFighters;
            if (y.KeySystem) x.KeySystem = true;
            if (y.OwnedOrPopulated) x.NeutralPopulated = true;
        }
    }

    // ---- catalogue summary ----


    /// <summary>
    /// Init: the largest ResearchOrder of the side's ships + fighters / troops / all defence and production
    /// facilities, the side's minor characters (into the model's minor-character count), the research levels, then the six
    /// ship-line bests and the troop best. Inferred: the order of the seven lists (ship line first) and the minor-character side
    /// flag (SD Alliance / Empire flags).
    /// </summary>
    void Summary(GameState game, (int Ship, int Troop, int Facility) levels = default)
    {
        var products = Catalogue.Products.ToList();
        int MaxOrder(Func<Product, bool> f) => products.Where(f).Select(p => p.ResearchOrder).DefaultIfEmpty(0).Max();
        bool Own(Product p) => p.Side == (int)Side;
        MaxShipOrder = MaxOrder(p => Own(p) && Family.Of(p.Type) is >= 0x14 and < 0x20);
        MaxTroopOrder = MaxOrder(p => Own(p) && Family.IsTroop(p.Type));
        MaxFacilityOrder = MaxOrder(p => Family.Of(p.Type) is >= 0x22 and < 0x2C);
        MinorCharacters = game.Definitions.MinorCharacters.Count(c => (Side == Owner.Alliance ? c.Alliance : c.Empire) != 0);
        // init: the catalogue constructor zeroes the levels and the lists run at level 0; the research messages
        // (Observe) bring them up to the side's levels later. Load: the saved levels.
        (ShipLevel, TroopLevel, FacilityLevel) = levels;
        ShipBests();
        TroopBest();
    }

    /// <summary>
    /// The advisor's model init (on its own catalogue): the same seven catalogue lists
    /// for <paramref name="side"/> on the UI generator; the results are unused.
    /// </summary>
    public static void CatalogueLists(GameState game, Owner side, Func<int, int> rand)
    {
        var advisor = new WorldModel(side) { Rand = rand, Catalogue = new Catalogue(game.Definitions) };
        advisor.Summary(game);
    }

    /// <summary>Ranked list (dir 1) of the side's records with the mask and ResearchOrder <= level by the criterion; the best one's value.</summary>
    int Best(int level, ProductFilter filter, Criterion criterion)
    {
        var stack = new ListStack();
        stack.Push(new RankedList(Catalogue.Products.Where(p => p.Side == (int)Side && p.Is(filter) && p.ResearchOrder <= level)
            .Select(p => ((object)p, p.Value(criterion))), 1, Rand));
        return stack.Pick() is Product best ? best.Value(criterion) : 0;
    }

    void ShipBests()
    {
        int level = ShipLevel;
        BestFighterAntiFighterFirepower = Best(level, ProductFilter.Fighter, Criterion.AntiFighterFirepower);
        BestArmedFirepower = Best(level, ProductFilter.Armed, Criterion.Firepower);
        BestAntiFighterFirepower = Best(level, ProductFilter.AntiFighter, Criterion.AntiFighterFirepower);
        BestFighterCapacity = Best(level, ProductFilter.CapitalShip, Criterion.FighterCapacity);
        BestTroopCapacity = Best(level, ProductFilter.CapitalShip, Criterion.TroopCapacity);
        BestBombardment = Best(level, ProductFilter.Bombarder, Criterion.Bombardment);
    }

    void TroopBest() => BestTroopDefense = Best(TroopLevel, ProductFilter.DefenseTroop, Criterion.TroopDefense);

    // ---- load ----

    /// <summary>
    /// The model's load effects: the catalogue rebuilt with its seven summary lists at the saved research
    /// levels (tie draws), every record dirty (systems, sectors, ships; agents with their location),
    /// the aggregates flag, the tree key counters := highest live key + 1, the observation snapshot from
    /// the loaded state (no messages for unchanged fields). Fleet records are refreshed on every update anyway (shortcut above).
    /// </summary>
    internal void AfterLoad(GameState game)
    {
        _game = game;
        _frozenContents = null;
        Catalogue = new Catalogue(game.Definitions);
        Summary(game, (ShipLevel, TroopLevel, FacilityLevel));
        foreach (var r in Systems.Values) r.Dirty = true;
        foreach (var r in Sectors) r.Dirty = true;
        foreach (var r in Ships.Values) r.Dirty = true;
        foreach (var r in Agents.Values.ToList()) DirtyAgent(r);
        Dirty = true;
        NextOpKey = Ops.Count == 0 ? 1 : Ops.Keys.Max() + 1;
        NextCharacterOpKey = CharacterOps.Count == 0 ? 1 : CharacterOps.Keys.Max() + 1;
        NextFleetOpKey = FleetOps.Count == 0 ? 1 : FleetOps.Keys.Max() + 1;
        NextTaskKey = Tasks.Count == 0 ? 1 : Tasks.Max(t => t.Key) + 1;
        _systemFields.Clear();
        _queues.Clear();
        _unitFields.Clear();
        Snapshot(game, record: true);
    }

    // ---- observed field changes (messages without a sim notification here) ----

    readonly Dictionary<GameObject, (Owner, int, int, int, bool, bool, bool)> _systemFields = [];
    readonly Dictionary<GameObject, int> _queues = [];
    readonly Dictionary<GameObject, (int, int, int, int, (bool, bool, bool), uint, bool, int, bool, bool, CriticalHitCounts)> _unitFields = [];

    /// <summary>
    /// Stage 6: the messages of field changes the sim has no hook for, from the side's live views, compared with the previous
    /// tick: side MaintState 0x600 / MaintRequired 0x120 (cap / dem, aggregates flag), research orders 0x125-0x127, system fields
    /// 0x100 / 0x140-0x155 (support, energy, raw material, Populated, Explored, Uprising; ControlKind = owner
    /// -> TheatersDue set), manager queue count 0x160, fleet Battle / Bombard / Assault (new != 0), hull / squadron damage,
    /// Damaged 0x106 and crit counters 0x1C1 ... 0x1C6, character Force / CommandKind. shortcut: a change reverted within one tick posts nothing here (the original posts both).
    /// </summary>
    public void Observe(GameState game)
    {
        _game = game;
        var side = game.Sides[Side];
        if (side.MaintenanceCapacity != MaintenanceCapacity) { MaintenanceCapacity = side.MaintenanceCapacity; Dirty = true; }
        if (side.Demand != MaintenanceDemand) { MaintenanceDemand = side.Demand; Dirty = true; }
        ResearchLevel(0, ShipLevel);
        ResearchLevel(2, TroopLevel);
        ResearchLevel(1, FacilityLevel);
        Snapshot(game, record: false);
    }

    /// <summary>Research-order messages: line k 0 ship, 2 troop, 1 facility.</summary>
    void ResearchLevel(int k, int old)
    {
        int level = _game.Sides[Side].ResearchOrder[k];
        if (level == old) return;
        switch (k)
        {
            case 0:
                ShipLevel = level;
                if (level == MaxShipOrder) ShipLineDone = true;
                ShipBests();
                break;
            case 2:
                TroopLevel = level;
                if (level == MaxTroopOrder) TroopLineDone = true;
                TroopBest();
                break;
            default:
                FacilityLevel = level;
                if (level == MaxFacilityOrder) FacilityLineDone = true;
                if (Research.Candidates(_game, Side, 0x2A).Any(r => r.Common.Ref == 0x25000004 && r.Get(SdField.ResearchOrder) == level)) DeathStarShieldAvailable = true;
                break;
        }
    }

    void Snapshot(GameState game, bool record)
    {
        void Diff<T>(Dictionary<GameObject, T> seen, GameObject o, T now, Action<T> changed)
        {
            if (seen.TryGetValue(o, out var was) && !EqualityComparer<T>.Default.Equals(was, now) && !record) changed(was);
            seen[o] = now;
        }
        foreach (var s in game.Systems.Where(Live))
        {
            Diff(_systemFields, s, (s.Owner, s.Support, s.Energy, s.RawMaterials, s.Populated, s.Explored, s.Uprising), was =>
            {
                if (was.Item1 != s.Owner) TheatersDue = true;
                Changed(game, s);
            });
            foreach (var mm in s.Managers.Where(x => x is not null)) Diff(_queues, mm, mm.Orders.Count, _ => Changed(game, mm));
        }
        foreach (var o in game.Created.Concat(game.Characters).Where(Live))
        {
            var bits = o is Fleet fb ? (fb.Battle, fb.Bombard, fb.Assault) : default;
            var c = o as Character;
            var agent = o as Agent; // role MissionKey 0x380 / OnMission: changed
            // Injury / Captured: every change dirties the agent record
            Diff(_unitFields, o, (o.HullDamage, o.SquadSizeDamage, c?.Force ?? 0, c?.CommandKind ?? 0, bits, agent?.Mission?.Ref ?? 0, agent?.OnMission ?? false,
                c?.Injury ?? 0, c?.Captured ?? false, o.Damaged, o.CriticalHits), was =>
            {
                // Damaged 0x106, crit counters 0x1C1 ... 0x1C6: changed
                if (was.Item5 == bits || bits != (false, false, false) || was.Item6 != (agent?.Mission?.Ref ?? 0) || was.Item7 != (agent?.OnMission ?? false)
                    || was.Item8 != (c?.Injury ?? 0) || was.Item9 != (c?.Captured ?? false) || was.Item10 != o.Damaged
                    || was.Item11 != o.CriticalHits) Changed(game, o);
            });
        }
    }

    /// <summary>
    /// Message 0x302: the destroyed object's records are detached: agent, ship,
    /// fleet. Inferred: detached = removed from the model's trees; holders (ops, tasks) drop them on their next
    /// refresh. Missions keep their op link (not specced).
    /// </summary>
    public void Detach(GameObject o)
    {
        Agents.Remove(o.Ref);
        Ships.Remove(o.Ref);
        FleetsById.Remove(o.Ref);
        Dirty = true;
    }

    /// <summary>
    /// Message 0x300 Parent: a system -> dirty; a capital ship -> its ship record; fleets, agents, missions -> changed;
    /// headquarters [0x20, 0x22) -> TheatersDue set; then changed(new parent) and changed(old parent).
    /// </summary>
    public void ParentChanged(GameState game, GameObject o, GameObject? parent, GameObject? old)
    {
        _game = game;
        uint f = Family.Of(o.Type);
        if (o is StarSystem s && Systems.TryGetValue(s, out var r)) DirtySystem(r);
        else if (f is >= 0x20 and < 0x22) TheatersDue = true;
        else if (f is >= 0x30 and < 0x40 && Agents.TryGetValue(o.Ref, out var agent))
        {
            // agent location := the parent key of the message (Inferred: the new parent), record dirty
            agent.Location = parent;
            DirtyAgent(agent);
        }
        else if (f is >= 0x08 and < 0x10 or >= 0x14 and < 0x1C or >= 0x30 and < 0x80) Changed(game, o);
        if (parent is not null) Changed(game, parent);
        if (old is not null) Changed(game, old);
    }


    // ---- planner queries ----


    /// <summary>Sector list: sector records in key order passing the masks, value at <paramref name="offset"/>.</summary>
    public RankedList SectorList(Func<AiSector, bool> pass, Func<AiSector, int> value, int dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, value(x))), dir, Rand);

    /// <summary>System list: the sector's system slots in slot order that pass.</summary>
    public RankedList SystemList(AiSector sector, Func<AiSystem, bool> pass, Func<AiSystem, int> value, int dir) =>
        new(sector.Systems.Where(pass).Select(x => ((object)x, value(x))), dir, Rand);

    /// <summary>
    /// Sector by squared distance to the sector containing <paramref name="reference"/> (the reference's location
    /// through the view parents). Inferred: sector and system positions are the SD X / Y.
    /// </summary>
    public RankedList SectorDistanceList(GameObject reference, Func<AiSector, bool> pass, int dir)
    {
        var at = LocationOf(reference)?.Sector;
        return new(Sectors.Where(pass).Select(x => ((object)x, at is null ? 0 : Squared(
            x.Sector.Definition.X - at.Definition.X, x.Sector.Definition.Y - at.Definition.Y))), dir, Rand);
    }

    /// <summary>The sector's systems by squared distance to the system containing <paramref name="reference"/>.</summary>
    public RankedList SystemDistanceList(AiSector sector, GameObject reference, Func<AiSystem, bool> pass, int dir)
    {
        var at = LocationOf(reference);
        return new(sector.Systems.Where(pass).Select(x => ((object)x, at is null ? 0 : Squared(
            x.System.Definition.X - at.Definition.X, x.System.Definition.Y - at.Definition.Y))), dir, Rand);
    }

    static int Squared(int dx, int dy) => dx * dx + dy * dy;

    /// <summary>Sector list: value is the sector's field, or 100 when it is 0 (not specced: reads 0, so 100).</summary>
    public RankedList SectorDefaultList(Func<AiSector, bool> pass, int dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, x.Entry == 0 ? 100 : x.Entry)), dir, Rand);

    /// <summary>Fleet list: fleet records in key order that pass, valued by <paramref name="value"/>.</summary>
    public RankedList FleetList(Func<AiFleet, bool> pass, Func<AiFleet, int> value, int dir) =>
        new(FleetRecords().Where(pass).Select(f => ((object)f, value(f))), dir, Rand);

    /// <summary>Fleet list: fleet records that pass, by squared distance of their location to the reference's system.</summary>
    public RankedList FleetDistanceList(GameObject reference, Func<AiFleet, bool> pass, int dir)
    {
        var at = LocationOf(reference);
        return new(FleetRecords().Where(pass).Select(f => ((object)f,
            at is not null && LocationOf(f.Fleet) is { } l ? Squared(l.Definition.X - at.Definition.X, l.Definition.Y - at.Definition.Y) : 0)), dir, Rand);
    }

    /// <summary>Sector list with a sector as the reference (squared distance of sector positions).</summary>
    public RankedList SectorDistanceFrom(AiSector at, Func<AiSector, bool> pass, int dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, Squared(
            x.Sector.Definition.X - at.Sector.Definition.X, x.Sector.Definition.Y - at.Sector.Definition.Y))), dir, Rand);

    /// <summary>Sector list: sum of days since S of its systems owned or populated and not own, div (the two sector fields added).</summary>
    public RankedList SectorStalenessList(Func<AiSector, bool> pass, int dir) =>
        new(Sectors.Where(pass).Select(x =>
        {
            int sum = x.Systems.Where(r => r.OwnedOrPopulated && !r.Own).Sum(r => _game.Day - r.BlockadeDay),
                d = x.NeutralSystems + x.EnemySystems;
            return ((object)x, d == 0 ? sum : sum / d);
        }), dir, Rand);

    /// <summary>Dirties the sector record and sets the aggregates flag.</summary>
    public void DirtySector(AiSector s)
    {
        s.Dirty = true;
        Dirty = true;
    }

    /// <summary>Fleet list: fleet records that pass with an op entry &gt; min, valued by the op entry.</summary>
    public RankedList FleetMinList(int min, Func<AiFleet, bool> pass, int dir) =>
        new(FleetRecords().Where(f => pass(f) && f.OpEntry > min).Select(f => ((object)f, f.OpEntry)), dir, Rand);

    /// <summary>The own, usable troops aboard the fleet's ships picked by <see cref="ShipList"/> by usable troops (ships in list order, contents in order); the unload / deploy planners share it.</summary>
    public List<GameObject> TroopsAboard(AiFleet fleet, Func<AiShip, bool> pass)
    {
        var ships = new ListStack();
        ships.Push(ShipList(fleet, pass, s => s.UsableTroops, 1));
        var troops = new List<GameObject>();
        while (ships.Pick() is AiShip s)
            troops.AddRange(Contents(s.Ship).Where(x => Family.IsTroop(x.Type) && Usable(x) && See(x).Owner == Side));
        return troops;
    }

    /// <summary>Ship list: the fleet's ship records that pass, valued by <paramref name="value"/>.</summary>
    public RankedList ShipList(AiFleet fleet, Func<AiShip, bool> pass, Func<AiShip, int> value, int dir) =>
        new(Contents(fleet.Fleet).Where(x => Ships.ContainsKey(x.Ref)).Select(x => Ships[x.Ref])
            .Where(pass).Select(s => ((object)s, value(s))), dir, Rand);

    /// <summary>Agent list: agent records in key order that pass, valued by <paramref name="value"/> within [lo, hi].</summary>
    public RankedList AgentList(Func<AiAgent, bool> pass, Func<AiAgent, int> value, int lo, int hi, int dir, bool minPri = false) =>
        new(Agents.Values.Where(a => pass(a) && value(a) >= lo && value(a) <= hi && (!minPri || a.Priority > 0 || a.Unassigned))
            .Select(a => ((object)a, value(a))), dir, Rand);

    /// <summary>Agent list: agents that pass, by squared distance of their location system to the reference.</summary>
    public RankedList AgentDistanceList(StarSystem reference, Func<AiAgent, bool> pass, int dir) =>
        new(Agents.Values.Where(pass).Select(a => ((object)a, LocationOf(a.Agent) is { } l
                ? Squared(l.Definition.X - reference.Definition.X, l.Definition.Y - reference.Definition.Y) : 0)), dir, Rand);

    /// <summary>Random-key sector list: per passing sector in key order one rand(20) as its value, then its insertion.</summary>
    public RankedList SectorRandomList(Func<AiSector, bool> pass, int dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, Rand(20))), dir, Rand);

    /// <summary>Random-key system list: per passing slot in slot order one rand(10) as its value, then its insertion.</summary>
    public RankedList SystemRandomList(AiSector sector, Func<AiSystem, bool> pass, int dir) =>
        new(sector.Systems.Where(pass).Select(x => ((object)x, Rand(10))), dir, Rand);

    /// <summary>System list: value = days since S (own systems 0).</summary>
    public RankedList SystemDaysList(AiSector sector, Func<AiSystem, bool> pass, int dir) =>
        new(sector.Systems.Where(pass).Select(x => ((object)x, x.Own ? 0 : _game.Day - x.BlockadeDay)), dir, Rand);

    /// <summary>Fighter order key: SD TotalAttackStrength x squadron size.</summary>
    public int FighterKey(GameObject o) => _game.Definitions.Fighters.FirstOrDefault(f => f.Common.Ref == o.Type) is { } f ? (int)(f.TotalAttackStrength * f.SquadronSize) : 0;

    /// <summary>The location system of an object through its view parents (null outside systems).</summary>
    public StarSystem? Where(GameObject o) => LocationOf(o);

    // ---- round 6 containers ----

    /// <summary>Mission operations by key 1, 2, 3 ... in creation order (= the missions' UserID).</summary>
    public SortedDictionary<int, MissionOp> Ops { get; } = [];
    internal int NextOpKey { get; set; } = 1;
    /// <summary>Fleet tasks: garrison tasks created by planner 13 step 5 (keys 1, 2, ...), run by planner 9.</summary>
    public List<GarrisonTask> Tasks { get; } = [];
    internal int NextTaskKey { get; set; } = 1;
    /// <summary>Fleet operations by key.</summary>
    public SortedDictionary<int, FleetOp> FleetOps { get; } = [];
    /// <summary>The production planners' shared site slots (not saved).</summary>
    public Dictionary<int, StarSystem?> SiteSlots { get; } = [];
    internal int NextFleetOpKey { get; set; } = 1;
    public int FleetOperationCount => FleetOps.Count;
    /// <summary>Character operations by key.</summary>
    public SortedDictionary<int, CharacterOp> CharacterOps { get; } = [];
    internal int NextCharacterOpKey { get; set; } = 1;

    StarSystem? LocationOf(GameObject o)
    {
        for (GameObject? x = o; x is not null; x = ViewParent(x))
            if (x is StarSystem s) return s;
        return null;
    }

    /// <summary>
    /// Product pick: the side's buildable list for the producer family in its order, filtered by the class
    /// mask; criterion 0 -> one rand(count) (count -> none), else the ranked list (dir) by the criterion value and the pick.
    /// Returns the product and its MaintenanceCost (0 when none).
    /// </summary>
    public (Product? Product, int Cost) PickProduct(uint producer, ProductFilter filter, Criterion criterion, int dir)
    {
        var candidates = Research.Buildable(_game, Side, producer).Select(r => Catalogue[r.Common.Ref])
            .Where(p => p is not null && p.Is(filter)).Cast<Product>().ToList();
        Product? chosen;
        if (criterion == Criterion.None)
        {
            if (candidates.Count == 0) return (null, 0);
            int i = Rand(candidates.Count);
            chosen = i < candidates.Count ? candidates[i] : null;
        }
        else
        {
            var stack = new ListStack();
            stack.Push(new RankedList(candidates.Select(p => ((object)p, p.Value(criterion))), dir, Rand));
            chosen = stack.Pick() as Product;
        }
        return (chosen, chosen?.MaintenanceCost ?? 0);
    }

    /// <summary>The advisor model's product pick (the same pick over the advisor's catalogue); no model state, no init draws.</summary>
    internal static (Product? Product, int Cost) PickProduct(GameState game, Owner side, Catalogue catalogue, Func<int, int> rand, uint producer, ProductFilter filter, Criterion criterion, int dir) =>
        new WorldModel(side) { _game = game, Catalogue = catalogue, Rand = rand }.PickProduct(producer, filter, criterion, dir);

    /// <summary>cap - dem &lt; budget -> 0 (both budgets).</summary>
    int Clamp(int budget) => MaintenanceCapacity - MaintenanceDemand < budget ? 0 : budget;

    /// <summary>Facility budget: pct(cap, p)*g div 100 - sum the field over the planner's systems.</summary>
    public int FacilityBudget(int p, int g, Func<AiSystem, int> field, IEnumerable<StarSystem> systems) =>
        Clamp(GameMath.Pct(MaintenanceCapacity, p) * g / 100 - systems.Sum(s => field(Systems[s])));

    /// <summary>Unit budget: (p*cap div 100)*f div 100 - U.</summary>
    public int UnitBudget(int p, int f, int used) => Clamp(p * MaintenanceCapacity / 100 * f / 100 - used);

    /// <summary>
    /// Advance the producer-facility turn: no requests -> the turn := the next turn, the next turn moves on one slot (after the last
    /// the first); otherwise the turn stays when its own slot is requested, else moves on (wrapping) to the next requesting slot; that
    /// request is consumed.
    /// </summary>
    public void AdvanceTurn()
    {
        if (!TurnRequests.Contains(true))
        {
            Turn = NextTurn;
            NextTurn = (NextTurn + 1) % TurnSlots;
            return;
        }
        int t = Turn;
        while (!TurnRequests[t]) t = (t + 1) % TurnSlots; // the current slot is tested first
        Turn = t;
        TurnRequests[t] = false;
    }

    /// <summary>Adds 1 to the constant at <paramref name="offset"/>, then every system record is dirtied.</summary>
    public void RaiseConstant(ModelConstant k)
    {
        Constant(k)++;
        foreach (var r in Systems.Values) DirtySystem(r);
    }

    /// <summary>The system record of a system object (null for anything else).</summary>
    public AiSystem? Record(GameObject? o) => o is StarSystem s ? Systems.GetValueOrDefault(s) : null;

    // ---- model aggregates over the sectors ----

    void SectorAggregates()
    {
        // zero every sector aggregate
        (OwnSystems, EnemySystems, NeutralSystems, OwnSectors, EnemySectors, NeutralSectors, SectorsWithoutKey, SectorsNotHeld, TroopSurplus) = (0, 0, 0, 0, 0, 0, 0, 0, 0);
        (Mines, Refineries, TroopMaintenance, DefenceMaintenance, FighterMaintenance, FreeEnergy, FreeRawMaterial, EnemyUsedEnergy) = (0, 0, 0, 0, 0, 0, 0, 0);
        (ProducerMaintenance, EnemyProducerMaintenance, GarrisonTarget, Troops, TheaterBalance, MaxEnemyFighters, MaxEnemyTroopsToTake) = (0, 0, 0, 0, 0, 0, 0);
        (MinEnemyTroopsToTake, DiplomacyTargets, EnemySupported, UprisingNoFleet, LowSupportHubs, UnblockadedKeySystems) = (0, 0, 0, 0, 0, 0);
        AssaultBase = DefenseBase = OwnCoruscant = OwnLowSupport = Headquarters = false;
        int smallest = 0;
        foreach (var x in Sectors)
        {
            OwnSystems += x.OwnSystems;
            EnemySystems += x.EnemySystems;
            NeutralSystems += x.NeutralSystems;
            Mines += x.Mines;
            Refineries += x.Refineries;
            TroopMaintenance += x.TroopMaintenance;
            DefenceMaintenance += x.DefenceMaintenance;
            FighterMaintenance += x.FighterMaintenance;
            FreeEnergy += x.FreeEnergy;
            FreeRawMaterial += x.FreeRawMaterial;
            EnemyUsedEnergy += x.EnemyUsedEnergy;
            ProducerMaintenance += x.ProducerMaintenance;
            EnemyProducerMaintenance += x.EnemyProducerMaintenance;
            TroopSurplus += x.TroopSurplus;
            GarrisonTarget += x.GarrisonTarget;
            Troops += x.Troops;
            TheaterBalance += x.TheaterBalance;
            UprisingNoFleet += x.UprisingNoFleet;
            LowSupportHubs += x.LowSupportHubs;
            UnblockadedKeySystems += x.UnblockadedKeySystems;
            DiplomacyTargets += x.DiplomacyTargets;
            EnemySupported += x.EnemySupported;
            MaxEnemyTroopsToTake = Math.Max(MaxEnemyTroopsToTake, x.MaxEnemyTroopsToTake);
            MaxEnemyFighters = Math.Max(MaxEnemyFighters, x.MaxEnemyFighters);
            MaxEnemyUnits = Math.Max(MaxEnemyUnits, x.MaxEnemyUnits);
            if (x.Own)
            {
                OwnSectors++;
                if (x.AssaultBase) AssaultBase = true;
                if (x.DefenseBase) DefenseBase = true;
                if (x.OwnCoruscant) OwnCoruscant = true;
                if (x.OwnLowSupport) OwnLowSupport = true;
            }
            if (x.Enemy)
            {
                EnemySectors++;
                if (x.EnemyTroopsToTake != 0 && (smallest == 0 || x.EnemyTroopsToTake < smallest)) smallest = x.EnemyTroopsToTake;
            }
            if (x.Neutral) NeutralSectors++;
            if (!x.KeySystem) SectorsWithoutKey++;
            if (!x.AllHeld) SectorsNotHeld++;
            if (x.Headquarters) Headquarters = true;
        }
        MinEnemyTroopsToTake = smallest;
        if (DiplomacyTargets > 0) DiplomacyTargets = DiplomacyTargets / 5 + 1;
        if (EnemySupported > 0) EnemySupported = EnemySupported / 6 + 1;
    }
}
