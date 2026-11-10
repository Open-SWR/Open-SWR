using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// One row of a status page: label = TEXTSTRA id (0 = none) with an optional ':' appended; value = the TEXTSTRA
/// text <see cref="Text"/> (0 = none) followed by <see cref="Value"/> (numbers, names, "a:b").
/// </summary>
public readonly record struct StatusRow(int Label, string Value = "", int Text = 0, bool Colon = false);

/// <summary>A picture row of the Galaxy Overview / Objectives pages: item id (sort key), GOKRES or STRATEGY picture, text id or text.</summary>
public readonly record struct StatusItem(int Id, int Picture, bool Gokres, int Text, string Value = "");

/// <summary>A built page: title TEXTSTRA id (0 = none), the object rows, the picture rows (Galaxy Overview / Objectives).</summary>
public sealed record StatusPage(int Title, List<StatusRow> Rows, List<StatusItem> Items)
{
    /// <summary>The full share in per cent (support pages) and the number of critical levels a rate loses (Inferred: 4 steps of damage).</summary>
    const int FullSupport = 100, CriticalLevels = 4;
    /// <summary>GameSettings.VictoryFlag of the standard victory (any other value: headquarters victory).</summary>
    const int StandardVictory = 0;

    /// <summary>Page title ids (TEXTSTRA) by object kind; managers by manufacturing kind.</summary>
    public static class Titles
    {
        public const int Fleet = 34616, CapitalShip = 34562, Character = 34595, DefenceFacility = 34403, Fighter = 34384, Troop = 34405,
            ManufacturingFacility = 34560, StarSystem = 34432, Mission = 34422, SpecialForce = 34416;
        /// <summary>The manager titles (FacilitiesUnderConstruction, ShipConstruction, TroopsInTraining in the strategy text).</summary>
        public const int ConstructionManager = 6195, ShipManager = 6185, TrainingManager = 6193;
    }

    /// <summary>Label, status and value ids (TEXTSTRA) of the object pages, named by the row they label (Inferred from the value shown).</summary>
    public static class Text
    {
        // shared rows and statuses
        public const int Status = 34406, Location = 34404, Attached = 34736, Eta = 34662, DayUnit = 14356, Maintenance = 34385,
            Yes = 34646, No = 34645, None = 34644, Enroute = 34628, UnderConstruction = 34647, Active = 34659, Awaiting = 34632,
            NotAssigned = 34658, OnMission = 34629, Captured = 34630, Injured = 34631;
        /// <summary>Header rows with no value (a label only).</summary>
        public const int CapacityHeader = 34564, AboardHeader = 34566;
        // fleet
        public const int FleetShips = 34624, FighterSquadrons = 14368, Troops = 34565, Characters = 34567, DamagedShips = 34625, Hyperdrive = 34387,
            FleetAdmiral = 12358, FleetGeneral = 12360, FleetCommander = 12356;
        // capital ship
        public const int ShipClass = 34819, Parent = 34563, Fighters = 34457, Damaged = 34568, HyperdriveRating = 34569, Hull = 34576,
            DamageControl = 34577, ShieldRecharge = 34578, ShieldStrength = 34388, TractorBeam = 34579, Speed = 34580, Maneuverability = 34390,
            Detection = 34391, WeaponRecharge = 34581, Bombardment = 34582, ArcFore = 34583, ArcAft = 34592, ArcStarboard = 34593, ArcPort = 34594,
            Turbolaser = 34584, IonCannon = 34401, LaserCannon = 34585;
        // character
        public const int Commanding = 34596, ForceRankBand1 = 34633, ForceRankBand2 = 34640, ForceRankBand3 = 34641, ForceRankBand4 = 34642,
            ForceRankBand5 = 34643, ForceRank = 34597, Diplomacy = 34598, Espionage = 34599, Combat = 34600, Leadership = 34601,
            TechnicalSkillsHeader = 34608, ShipDesign = 34609, TroopTraining = 34610, FacilityDesign = 34611, RankHeader = 34615,
            Admiral = 34435, General = 34433, Commander = 34434;
        // defence facility, fighter, troop, manufacturing facility
        public const int AttackStrength = 34393, DefenceShield = 34627, DefenceBombardment = 34409, SquadronSize = 34386, FighterSpeed = 34389,
            TorpedoRange = 34392, FighterLaserCannon = 34400, Torpedoes = 34402, TroopAttack = 34407, TroopDefense = 34408, TroopDetection = 34417,
            InTraining = 34673, ProcessingRate = 34561, BombardmentDefense = 34392;
        // star system
        public const int OwnedByAlliance = 34648, OwnedByEmpire = 34649, NeutralSystem = 34657, PopularSupport = 34436, PopularDissent = 34437,
            Energy = 34438, RawMaterials = 34439, FacilitiesHeader = 34440, Mines = 34441, Refineries = 34448, Shipyards = 34449,
            TrainingFacilities = 34450, ConstructionYards = 34451, PlanetaryShields = 34452, IonCannons = 34453, DeathStarShields = 34454,
            ForcesHeader = 34455, TroopsInSystem = 34456;
        // mission
        public const int Target = 34423, TargetNone = 34087, Members = 34424, Decoys = 34425;
        // special force (agent skills)
        public const int AgentDiplomacy = 34418, AgentEspionage = 34419, AgentCombat = 34420, AgentLeadership = 34421;
        // manufacturing manager
        public const int ManagerIdle = 34665, NoFacility = 34674, InProduction = 34672, Queue = 34664, CompletionDay = 34661;
    }

    /// <summary>Objective texts of the Objectives page (TEXTSTRA): the "not yet" and "done" text per side and objective.</summary>
    public static class ObjectiveText
    {
        public const int AllianceCoruscantNot = 34689, AllianceCoruscantDone = 34690, AlliancePalpatineNot = 34697, AlliancePalpatineDone = 34698,
            AllianceVaderNot = 34701, AllianceVaderDone = 34702, AllianceHqNot = 34692, AllianceHqDone = 34695, AllianceLukeNot = 34708,
            AllianceLukeDone = 34711, AllianceMothmaNot = 34704, AllianceMothmaDone = 34707;
        public const int EmpireHqNot = 34693, EmpireHqDone = 34694, EmpireLukeNot = 34709, EmpireLukeDone = 34710, EmpireMothmaNot = 34705,
            EmpireMothmaDone = 34706, EmpireCoruscantNot = 34688, EmpireCoruscantDone = 34691, EmpirePalpatineNot = 34696, EmpirePalpatineDone = 34699,
            EmpireVaderNot = 34700, EmpireVaderDone = 34703;
    }

    /// <summary>STRATEGY bitmaps of the Objectives page pictures.</summary>
    public static class ObjectivePicture
    {
        public const int Coruscant = 1068, Hq = 904, Palpatine = 11013, Vader = 11055, Luke = 11028, Mothma = 11032;
    }

    /// <summary>GOKRES pictures of the manager pages: construction, ship, training.</summary>
    public static class ManagerPictures
    {
        public const int Construction = 263, Ship = 262, Training = 264;
    }

    /// <summary>Added to a definition's string number to get its Galaxy Overview picture (GOKRES).</summary>
    const int OverviewPictureOffset = 16384;

    /// <summary>
    /// The page of an object as <paramref name="side"/> sees it (dispatch by family, first match). Values come from the side's
    /// view; enemy objects whose view is frozen show the definition (SD) values for capacities, hull, ratings and skills (our
    /// views keep none of them). <paramref name="name"/> = an object's display name, <paramref name="definition"/> = a definition's name.
    /// Inferred: the damage getters (rates: base * (4 - CH) div 4, and the shield recharge uses the shield CH counter; hyperdrive: base * (1 - modifier);
    /// weapons / bombardment / damage control: no damage), the commanders (characters inside with CommandKind 1 commander / 2 admiral
    /// / 3 general), the ETA (the stored arrival day).
    /// </summary>
    public static StatusPage Build(GameState game, Owner side, GameObject o, Func<GameObject, string> name, Func<SdCommon, string> definition)
    {
        var rows = new List<StatusRow>();
        var v = ScreenViews.View(game, o, side);
        bool live = ScreenViews.Sees(o, side);
        uint f = v.Family;
        var record = o is StarSystem or Mission or ManufacturingManager ? null : game.Definitions.Resolve(o.Type);
        GameObject? parent = live ? o.Container : game.Views.TryGetValue((o, side), out var view) ? view.Parent : null;
        string Name(GameObject? x) => x is null ? "" : name(x);
        string Num(long n) => n.ToString();
        void Row(int label, string value = "", int text = 0, bool colon = false) => rows.Add(new(label, value, text, colon));
        void YesNo(int label, bool yes) => Row(label, text: yes ? Text.Yes : Text.No);
        void EtaRow(GameObject x, ViewObject xv)
        {
            if (xv.Enroute && ScreenViews.Sees(x, side) && x.Eta != 0) Row(Text.Eta, Num(x.Eta), Text.DayUnit);
        }
        void MaintenanceRow() => Row(Text.Maintenance, Num(f is >= Families.Troop and < Families.Mission ? game.MaintenanceCost(o.Type) : 0));
        void StatusA() => Row(Text.Status, text: v.Enroute ? Text.Enroute : !v.Completed ? Text.UnderConstruction : Text.Active);
        List<ViewObject> Inside(GameObject c) => ScreenViews.Existing(game, c, side);
        IEnumerable<ViewObject> Deep(GameObject c) => Inside(c).SelectMany(x => Deep(x.Object).Prepend(x));
        string Commander(GameObject c, int kind) =>
            Deep(c).Select(x => x.Object).OfType<Character>().FirstOrDefault(ch => ch.CommandKind == kind) is { } who ? name(who) : "";
        void CommanderRow(int label, GameObject c, int kind, bool colon)
        {
            string who = Commander(c, kind);
            if (who == "") Row(label, text: Text.NotAssigned, colon: colon);
            else Row(label, who, colon: colon);
        }
        string Pair(long cur, long max) => $"{cur}:{max}";

        switch (f)
        {
            case >= Families.Fleet and < Families.Troop: // fleet, title 34616
            {
                int status = v.Enroute ? Text.Enroute : !v.Completed ? Text.UnderConstruction : o.EnrouteActive && live ? Text.Enroute : Text.Awaiting;
                Row(Text.Status, text: status);
                EtaRow(o, v);
                CommanderRow(Text.FleetAdmiral, o, CommandKind.Admiral, true);
                CommanderRow(Text.FleetGeneral, o, CommandKind.General, true);
                CommanderRow(Text.FleetCommander, o, CommandKind.Commander, true);
                var ships = Inside(o).Where(x => x.In(Families.CapitalShip, Families.Fighter)).ToList();
                Row(Text.FleetShips, Num(ships.Count));
                Row(Text.CapacityHeader);
                var caps = ships.Select(x => game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Object.Type)).Where(r => r is not null).ToList();
                Row(Text.FighterSquadrons, Num(caps.Sum(r => r!.FighterCapacity)), colon: true);
                Row(Text.Troops, Num(caps.Sum(r => r!.TroopCapacity)));
                Row(Text.AboardHeader);
                var aboard = ships.SelectMany(x => Inside(x.Object)).ToList();
                Row(Text.FighterSquadrons, Num(aboard.Count(x => x.In(Families.Fighter, Families.Headquarters))), colon: true);
                Row(Text.Troops, Num(aboard.Count(x => x.In(Families.Troop, Families.CapitalShip))));
                Row(Text.Characters, Num(aboard.Count(x => x.In(Families.MajorCharacter, Families.Mission))));
                Row(Text.DamagedShips, Num(ships.Count(x => x.Damaged)));
                YesNo(Text.Hyperdrive, live ? Orders.Speed(game, o) != 0 : caps.Any(r => r!.Hyperdrive != 0)); // Inferred: the fleet can jump
                return new(Titles.Fleet, rows, []);
            }
            case >= Families.CapitalShip and < Families.Fighter when game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == o.Type) is { } ship: // title 34562
            {
                int Ch(CriticalKind k) => live ? o.Critical((int)k) : 0;
                long Rate(uint b, CriticalKind k) => b * (CriticalLevels - Ch(k)) / CriticalLevels;
                Row(Text.ShipClass, definition(ship.Common));
                Row(Text.Parent, Name(parent));
                Row(Text.Status, text: v.Enroute ? Text.Enroute : !v.Completed ? Text.UnderConstruction : Text.Awaiting);
                EtaRow(o, v);
                MaintenanceRow();
                Row(Text.CapacityHeader);
                Row(Text.Fighters, Num(ship.FighterCapacity));
                Row(Text.Troops, Num(ship.TroopCapacity));
                Row(Text.AboardHeader);
                var aboard = Inside(o);
                Row(Text.Fighters, Num(aboard.Count(x => x.In(Families.Fighter, Families.Headquarters))));
                Row(Text.Troops, Num(aboard.Count(x => x.In(Families.Troop, Families.CapitalShip))));
                Row(Text.Characters, Num(aboard.Count(x => x.In(Families.MajorCharacter, Families.Mission))));
                YesNo(Text.Damaged, v.Damaged);
                Row(Text.HyperdriveRating, Pair(ship.Hyperdrive * (1 - Math.Min(1, Ch(CriticalKind.Hyperdrive))), ship.Hyperdrive));
                Row(Text.Hull, Pair(ship.Hull - (live ? o.HullDamage : 0), ship.Hull));
                Row(Text.DamageControl, Num(ship.DamageControl));
                Row(Text.ShieldRecharge, Pair(Rate(ship.ShieldRechargeRate, CriticalKind.Shield), ship.ShieldRechargeRate));
                Row(Text.ShieldStrength, Num(ship.ShieldStrength));
                Row(Text.TractorBeam, Pair(Rate(ship.TractorBeamPower, CriticalKind.Tractor), ship.TractorBeamPower));
                Row(Text.Speed, Pair(Rate(ship.Speed, CriticalKind.Speed), ship.Speed));
                Row(Text.Maneuverability, Num(ship.Maneuverability));
                Row(Text.Detection, Num(ship.Detection));
                Row(Text.WeaponRecharge, Pair(Rate(ship.WeaponRechargeRate, CriticalKind.Weapon), ship.WeaponRechargeRate));
                Row(Text.Bombardment, Num(ship.Bombardment));
                foreach (var (header, arc) in new[] { (Text.ArcFore, ship.Fore), (Text.ArcAft, ship.Aft), (Text.ArcStarboard, ship.Starboard), (Text.ArcPort, ship.Port) })
                {
                    Row(header);
                    Row(Text.Turbolaser, Num(arc.Turbolaser));
                    Row(Text.IonCannon, Num(arc.IonCannon));
                    Row(Text.LaserCannon, Num(arc.LaserCannon));
                }
                return new(Titles.CapitalShip, rows, []);
            }
            case >= Families.MajorCharacter and < Families.SpecialForce when o is Character c: // title 34595
            {
                var commanding = live && c.CommandKind != 0 ? c.Container is Fleet or StarSystem ? c.Container : c.Container?.Container as Fleet : null;
                if (commanding is null) Row(Text.Commanding, text: Text.None);
                else Row(Text.Commanding, name(commanding));
                Row(Text.Attached, Name(parent));
                var see = Knowledge.See(game, c, side);
                int status = v.Enroute ? Text.Enroute : see.Captured ? Text.Captured : live && c.Injury != 0 ? Text.Injured
                    : v.Mission is { Record.Unlisted: false } ? Text.OnMission : Text.Awaiting;
                Row(Text.Status, text: status);
                EtaRow(o, v);
                int force = live ? c.Force : (int)c.Definition.JediLevel.Base;
                Row(Text.ForceRank, text: force < 10 ? Text.None : force < 20 ? Text.ForceRankBand1 : force < 80 ? Text.ForceRankBand2
                    : force < 100 ? Text.ForceRankBand3 : force < 120 ? Text.ForceRankBand4 : Text.ForceRankBand5);
                int Skill(int i, Skill s) => live ? c.Skills[i] : (int)s.Base;
                var k = c.Definition.Skills;
                Row(Text.Diplomacy, Num(Skill(SkillIndex.Diplomacy, k.Diplomacy)));
                Row(Text.Espionage, Num(Skill(SkillIndex.Espionage, k.Espionage)));
                Row(Text.Combat, Num(Skill(SkillIndex.Combat, k.Combat)));
                Row(Text.Leadership, Num(Skill(SkillIndex.Leadership, k.Leadership)));
                Row(Text.TechnicalSkillsHeader);
                YesNo(Text.ShipDesign, Skill(SkillIndex.ShipDesign, k.ShipDesign) != 0);
                YesNo(Text.TroopTraining, Skill(SkillIndex.TroopTraining, k.TroopTraining) != 0);
                YesNo(Text.FacilityDesign, Skill(SkillIndex.FacilityDesign, k.FacilityDesign) != 0);
                Row(Text.RankHeader);
                YesNo(Text.Admiral, c.Definition.CanBeAdmiral != 0);
                YesNo(Text.General, c.Definition.CanBeGeneral != 0);
                YesNo(Text.Commander, c.Definition.CanBeCommander != 0);
                return new(Titles.Character, rows, []);
            }
            case >= Families.Headquarters and < Families.Shipyard: // defence facility / HQ, title 34403
            {
                EtaRow(o, v);
                Row(Text.Location, Name(parent));
                StatusA();
                MaintenanceRow();
                if (f >= Families.IonCannon && game.Definitions.DefenseFacilities.FirstOrDefault(r => r.Common.Ref == o.Type) is { } d)
                {
                    Row(Text.AttackStrength, Num(d.AttackStrength));
                    Row(Text.DefenceShield, Num(d.ShieldStrength));
                    Row(Text.DefenceBombardment, Num(d.BombardmentDefense));
                }
                return new(Titles.DefenceFacility, rows, []);
            }
            case >= Families.Fighter and < Families.Headquarters when game.Definitions.Fighters.FirstOrDefault(r => r.Common.Ref == o.Type) is { } fighter: // title 34384
            {
                Row(Text.Attached, Name(parent));
                EtaRow(o, v);
                MaintenanceRow();
                Row(Text.SquadronSize, Pair(fighter.SquadronSize - (live ? o.SquadSizeDamage : 0), fighter.SquadronSize));
                Row(Text.Hyperdrive, Num(fighter.Hyperdrive));
                Row(Text.ShieldStrength, Pair(fighter.ShieldStrength, fighter.ShieldStrength));
                Row(Text.FighterSpeed, Num(fighter.Speed));
                Row(Text.Maneuverability, Num(fighter.Maneuverability));
                Row(Text.Detection, Num(fighter.Detection));
                Row(Text.TorpedoRange, Pair(fighter.TorpedoRange, fighter.TorpedoRange));
                Row(Text.AttackStrength);
                Row(Text.FighterLaserCannon, Pair(fighter.AttackStrength.LaserCannon, fighter.AttackStrength.LaserCannon));
                Row(Text.IonCannon, Pair(fighter.AttackStrength.IonCannon, fighter.AttackStrength.IonCannon));
                Row(Text.Torpedoes, Pair(fighter.Torpedoes, fighter.Torpedoes));
                return new(Titles.Fighter, rows, []);
            }
            case >= Families.Troop and < Families.CapitalShip when game.Definitions.Troops.FirstOrDefault(r => r.Common.Ref == o.Type) is { } troop: // title 34405
            {
                Row(Text.Attached, Name(parent));
                Row(Text.Status, text: v.Enroute ? Text.Enroute : !v.Completed ? Text.InTraining : Text.Awaiting);
                EtaRow(o, v);
                MaintenanceRow();
                Row(Text.TroopAttack, Num(troop.AttackStrength));
                Row(Text.TroopDefense, Num(troop.DefenseStrength));
                Row(Text.BombardmentDefense, Num(troop.BombardmentDefense));
                Row(Text.TroopDetection, Num(troop.Detection));
                return new(Titles.Troop, rows, []);
            }
            case >= Families.Shipyard and < Families.MajorCharacter: // manufacturing facility, Mine, Refinery, title 34560
            {
                var fac = game.Definitions.Facilities.FirstOrDefault(r => r.Common.Ref == o.Type);
                Row(Text.Location, Name(parent));
                EtaRow(o, v);
                StatusA();
                MaintenanceRow();
                Row(Text.ProcessingRate, Num(fac?.ProcessingRate ?? 0));
                Row(Text.BombardmentDefense, Num(fac?.BombardmentDefense ?? 0));
                return new(Titles.ManufacturingFacility, rows, []);
            }
            case >= Families.CoreSystem and < Families.Abode when o is StarSystem s: // title 34432
            {
                var see = Knowledge.See(game, s, side);
                Row(Text.Status, text: see.Owner switch { Owner.Alliance => Text.OwnedByAlliance, Owner.Empire => Text.OwnedByEmpire, _ => Text.NeutralSystem });
                CommanderRow(Text.General, s, CommandKind.General, false);
                CommanderRow(Text.Commander, s, CommandKind.Commander, false);
                Row(Text.PopularSupport, Num(see.Support));
                Row(Text.PopularDissent, Num(FullSupport - see.Support));
                Row(Text.Energy, Pair(s.UsedEnergy, s.Energy));
                Row(Text.RawMaterials, Pair(s.UsedRaw, s.RawMaterials));
                Row(Text.FacilitiesHeader);
                var inside = Inside(s);
                int Count(uint lo, uint hi) => inside.Count(x => x.In(lo, hi));
                Row(Text.Mines, Num(Count(Families.Mine, Families.Refinery)));
                Row(Text.Refineries, Num(Count(Families.Refinery, Families.RefineryEnd)));
                Row(Text.Shipyards, Num(Count(Families.Shipyard, Families.TrainingFacility)));
                Row(Text.TrainingFacilities, Num(Count(Families.TrainingFacility, Families.ConstructionYard)));
                Row(Text.ConstructionYards, Num(Count(Families.ConstructionYard, Families.ManufacturingEnd)));
                Row(Text.PlanetaryShields, Num(Count(Families.PlanetaryShield, Families.DeathStarShield)));
                Row(Text.IonCannons, Num(Count(Families.IonCannon, Families.PlanetaryShield)));
                Row(Text.DeathStarShields, Num(Count(Families.DeathStarShield, Families.DeathStarShieldEnd)));
                Row(Text.ForcesHeader);
                Row(Text.TroopsInSystem, Num(Count(Families.Troop, Families.CapitalShip)));
                Row(Text.Fighters, Num(Count(Families.Fighter, Families.Headquarters)));
                return new(Titles.StarSystem, rows, []);
            }
            case >= Families.AgentMission and < Families.Sector when o is Mission m: // title 34422
            {
                if (m.Record.Unlisted) return new(Titles.Mission, rows, []);
                var members = game.Characters.Cast<GameObject>().Concat(game.Created).Where(x => Family.Of(x.Type) is >= Families.MajorCharacter and < Families.Mission)
                    .Select(x => ScreenViews.View(game, x, side)).Where(x => x.Mission == m).ToList();
                GameObject? target = null;
                if (members.FirstOrDefault() is { } first)
                    target = first.Owner == side ? m.Objective : m.Kind is >= MissionKind.Diplomacy and <= MissionKind.JediTraining ? Knowledge.Location(game, first.Object, side) : null;
                if (target is null) Row(Text.Target, text: Text.TargetNone);
                else Row(Text.Target, name(target));
                if (members.FirstOrDefault() is { } lead) EtaRow(lead.Object, lead);
                Row(Text.Members, Num(members.Count));
                Row(Text.Decoys, Num(members.Count(x => m.Decoys.Contains(x.Object))));
                return new(Titles.Mission, rows, []);
            }
            case >= Families.SpecialForce and < Families.Mission when game.Definitions.SpecialForces.FirstOrDefault(r => r.Common.Ref == o.Type) is { } force: // title 34416
            {
                Row(Text.Attached, Name(parent));
                Row(Text.Status, text: v.Enroute ? Text.Enroute : v.Mission is not null ? Text.OnMission : !v.Completed ? Text.UnderConstruction : Text.Awaiting);
                EtaRow(o, v);
                MaintenanceRow();
                int Skill(int i, Skill s) => live && o is Agent a ? a.Skills[i] : (int)s.Base;
                Row(Text.AgentDiplomacy, Num(Skill(SkillIndex.Diplomacy, force.Skills.Diplomacy)));
                Row(Text.AgentEspionage, Num(Skill(SkillIndex.Espionage, force.Skills.Espionage)));
                Row(Text.AgentCombat, Num(Skill(SkillIndex.Combat, force.Skills.Combat)));
                Row(Text.AgentLeadership, Num(Skill(SkillIndex.Leadership, force.Skills.Leadership)));
                return new(Titles.SpecialForce, rows, []);
            }
            case >= Families.ConstructionManager and < Families.ManagerEnd when o is ManufacturingManager mm: // title by kind
            {
                var sys = mm.System;
                var owner = Knowledge.See(game, sys, side).Owner;
                uint yard = mm.Kind switch { ManufacturingKind.Construction => Families.ConstructionYard, ManufacturingKind.Ship => Families.Shipyard, _ => Families.TrainingFacility };
                int n = Inside(sys).Count(x => x.Family == yard && x.Owner == owner);
                Row(Text.Location, name(sys));
                Row(Text.Status, text: n == 0 ? Text.NoFacility : mm.Orders.Count == 0 ? Text.ManagerIdle : mm.Kind == ManufacturingKind.Training ? Text.InTraining : Text.InProduction);
                if (mm.Orders.Count > 0)
                {
                    Row(Text.Queue, Num(mm.Orders.Count));
                    if (mm.Owner == sys.Owner && Economy.EstimateDays(game, mm, mm.Orders[0].Type, 1, true) is { } days) Row(Text.CompletionDay, Num(game.Day + days));
                }
                return new(mm.Kind switch { ManufacturingKind.Construction => Titles.ConstructionManager, ManufacturingKind.Ship => Titles.ShipManager, _ => Titles.TrainingManager }, rows, []);
            }
        }
        return new(0, rows, []);
    }

    /// <summary>The page picture of a manager: GOKRES 263 construction, 262 shipyard, 264 training.</summary>
    public static int ManagerPicture(ManufacturingManager m) => m.Kind switch
    {
        ManufacturingKind.Construction => ManagerPictures.Construction, ManufacturingKind.Ship => ManagerPictures.Ship, _ => ManagerPictures.Training,
    };

    // Galaxy Overview category lists (family ranges [lo, hi)), in call order
    static readonly (uint Lo, uint Hi)[] Categories =
        [(Families.Headquarters, Families.IonCannon), (Families.ConstructionYard, Families.ManufacturingEnd), (Families.DeathStar, Families.Fighter),
         (Families.DeathStarShield, Families.DeathStarShieldEnd), (Families.Fighter, Families.Headquarters), (Families.IonCannon, Families.Gun),
         (Families.Mine, Families.Refinery), (Families.PlanetaryShield, Families.DeathStarShield), (Families.Refinery, Families.RefineryEnd),
         (Families.Shipyard, Families.TrainingFacility), (Families.SpecialForce, Families.Mission), (Families.CapitalShip, Families.DeathStar),
         (Families.TrainingFacility, Families.ConstructionYard), (Families.Troop, Families.CapitalShip), (Families.Gun, Families.PlanetaryShield)];

    /// <summary>
    /// Galaxy Overview (side key, AdvisorMenu.GalaxyOverview): the side's real Existing objects per category list, grouped by definition:
    /// one row per group, picture GOKRES (the string number) + 16384, text "count (3 digits)  maintenance sum (4 digits) name"; rows in
    /// ascending family (stable within a family). Inferred: definition order inside a list (first seen).
    /// </summary>
    public static StatusPage Overview(GameState game, Owner side, Func<SdCommon, string> definition)
    {
        var all = game.Created.Cast<GameObject>().Concat(game.Characters).Where(o => o.Owner == side && o.Existing).ToList();
        var items = new List<StatusItem>();
        foreach (var (lo, hi) in Categories)
            foreach (var g in all.Where(o => Family.Of(o.Type) >= lo && Family.Of(o.Type) < hi).GroupBy(o => o.Type))
            {
                if (game.Definitions.Resolve(g.Key) is not { } r) continue;
                int maintenance = Family.Of(g.Key) is >= Families.Troop and < Families.Mission ? g.Count() * game.MaintenanceCost(g.Key) : 0;
                items.Add(new((int)Family.Of(g.Key), r.Common.StringNumber + OverviewPictureOffset, true, 0, $"{g.Count():D3}  {maintenance:D4} {definition(r.Common)}"));
            }
        return new(0, [], [.. items.OrderBy(i => i.Id)]);
    }

    /// <summary>
    /// Objectives (null key, AdvisorMenu.Objectives): six rows (standard victory) or only the Coruscant and HQ rows (headquarters
    /// victory), each the "not yet" or "done" text by the victory state; pictures are STRATEGY bitmaps.
    /// </summary>
    public static StatusPage Objectives(GameState game, Owner side)
    {
        var w = game.VictoryState;
        bool std = game.Settings.VictoryFlag == StandardVictory;
        bool coruscant = w.CoruscantAlliance || w.CoruscantDestroyed;
        // row order is the sort key (1 to 6); Standard = the objective exists only in the standard victory
        (int Picture, int Not, int Done, bool Met, bool Standard)[] rows = side == Owner.Alliance
            ? [(ObjectivePicture.Coruscant, ObjectiveText.AllianceCoruscantNot, ObjectiveText.AllianceCoruscantDone, coruscant, false),
               (ObjectivePicture.Palpatine, ObjectiveText.AlliancePalpatineNot, ObjectiveText.AlliancePalpatineDone, w.PalpatineCaptured, true),
               (ObjectivePicture.Vader, ObjectiveText.AllianceVaderNot, ObjectiveText.AllianceVaderDone, w.VaderCaptured, true),
               (ObjectivePicture.Hq, ObjectiveText.AllianceHqNot, ObjectiveText.AllianceHqDone, w.HqGone, false),
               (ObjectivePicture.Luke, ObjectiveText.AllianceLukeNot, ObjectiveText.AllianceLukeDone, w.LukeCaptured, true),
               (ObjectivePicture.Mothma, ObjectiveText.AllianceMothmaNot, ObjectiveText.AllianceMothmaDone, w.MothmaCaptured, true)]
            : [(ObjectivePicture.Hq, ObjectiveText.EmpireHqNot, ObjectiveText.EmpireHqDone, w.HqGone, false),
               (ObjectivePicture.Luke, ObjectiveText.EmpireLukeNot, ObjectiveText.EmpireLukeDone, w.LukeCaptured, true),
               (ObjectivePicture.Mothma, ObjectiveText.EmpireMothmaNot, ObjectiveText.EmpireMothmaDone, w.MothmaCaptured, true),
               (ObjectivePicture.Coruscant, ObjectiveText.EmpireCoruscantNot, ObjectiveText.EmpireCoruscantDone, coruscant, false),
               (ObjectivePicture.Palpatine, ObjectiveText.EmpirePalpatineNot, ObjectiveText.EmpirePalpatineDone, w.PalpatineCaptured, true),
               (ObjectivePicture.Vader, ObjectiveText.EmpireVaderNot, ObjectiveText.EmpireVaderDone, w.VaderCaptured, true)];
        return new(0, [], [.. rows.Select((r, i) => (Row: r, Order: i + 1)).Where(x => std || !x.Row.Standard)
            .Select(x => new StatusItem(x.Order, x.Row.Picture, false, x.Row.Met ? x.Row.Done : x.Row.Not))]);
    }
}
