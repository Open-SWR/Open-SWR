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
    // label / value ids (TEXTSTRA)
    const int Status = 34406, Eta = 34662, Day = 14356, Maintenance = 34385, Yes = 34646, No = 34645, Attached = 34736, Location = 34404,
        EnrouteText = 34628, UnderConstruction = 34647, Active = 34659, Awaiting = 34632, NotAssigned = 34658, None = 34644, OnMission = 34629;

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
        void YesNo(int label, bool yes) => Row(label, text: yes ? Yes : No);
        void EtaRow(GameObject x, ViewObject xv)
        {
            if (xv.Enroute && ScreenViews.Sees(x, side) && x.Eta != 0) Row(Eta, Num(x.Eta), Day);
        }
        void MaintenanceRow() => Row(Maintenance, Num(f is >= 0x10 and < 0x40 ? game.MaintenanceCost(o.Type) : 0));
        void StatusA() => Row(Status, text: v.Enroute ? EnrouteText : !v.Completed ? UnderConstruction : Active);
        List<ViewObject> Inside(GameObject c) => ScreenViews.Existing(game, c, side);
        IEnumerable<ViewObject> Deep(GameObject c) => Inside(c).SelectMany(x => Deep(x.Object).Prepend(x));
        string Commander(GameObject c, int kind) =>
            Deep(c).Select(x => x.Object).OfType<Character>().FirstOrDefault(ch => ch.CommandKind == kind) is { } who ? name(who) : "";
        void CommanderRow(int label, GameObject c, int kind, bool colon)
        {
            string who = Commander(c, kind);
            if (who == "") Row(label, text: NotAssigned, colon: colon);
            else Row(label, who, colon: colon);
        }
        string Pair(long cur, long max) => $"{cur}:{max}";

        switch (f)
        {
            case >= 0x08 and < 0x10: // fleet, title 34616
            {
                int status = v.Enroute ? EnrouteText : !v.Completed ? UnderConstruction : o.EnrouteActive && live ? EnrouteText : Awaiting;
                Row(Status, text: status);
                EtaRow(o, v);
                CommanderRow(12358, o, 2, true);
                CommanderRow(12360, o, 3, true);
                CommanderRow(12356, o, 1, true);
                var ships = Inside(o).Where(x => x.In(0x14, 0x1C)).ToList();
                Row(34624, Num(ships.Count));
                Row(34564);
                var caps = ships.Select(x => game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Object.Type)).Where(r => r is not null).ToList();
                Row(14368, Num(caps.Sum(r => r!.FighterCapacity)), colon: true);
                Row(34565, Num(caps.Sum(r => r!.TroopCapacity)));
                Row(34566);
                var aboard = ships.SelectMany(x => Inside(x.Object)).ToList();
                Row(14368, Num(aboard.Count(x => x.In(0x1C, 0x20))), colon: true);
                Row(34565, Num(aboard.Count(x => x.In(0x10, 0x14))));
                Row(34567, Num(aboard.Count(x => x.In(0x30, 0x40))));
                Row(34625, Num(ships.Count(x => x.Damaged)));
                YesNo(34387, live ? Orders.Speed(game, o) != 0 : caps.Any(r => r!.Hyperdrive != 0)); // Inferred: the fleet can jump
                return new(34616, rows, []);
            }
            case >= 0x14 and < 0x1C when game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == o.Type) is { } ship: // title 34562
            {
                int Ch(int k) => live ? o.Critical(k) : 0;
                long Rate(uint b, int k) => b * (4 - Ch(k)) / 4;
                Row(34819, definition(ship.Common));
                Row(34563, Name(parent));
                Row(Status, text: v.Enroute ? EnrouteText : !v.Completed ? UnderConstruction : Awaiting);
                EtaRow(o, v);
                MaintenanceRow();
                Row(34564);
                Row(34457, Num(ship.FighterCapacity));
                Row(34565, Num(ship.TroopCapacity));
                Row(34566);
                var aboard = Inside(o);
                Row(34457, Num(aboard.Count(x => x.In(0x1C, 0x20))));
                Row(34565, Num(aboard.Count(x => x.In(0x10, 0x14))));
                Row(34567, Num(aboard.Count(x => x.In(0x30, 0x40))));
                YesNo(34568, v.Damaged);
                Row(34569, Pair(ship.Hyperdrive * (1 - Math.Min(1, Ch(4))), ship.Hyperdrive));
                Row(34576, Pair(ship.Hull - (live ? o.HullDamage : 0), ship.Hull));
                Row(34577, Num(ship.DamageControl));
                Row(34578, Pair(Rate(ship.ShieldRechargeRate, 0), ship.ShieldRechargeRate));
                Row(34388, Num(ship.ShieldStrength));
                Row(34579, Pair(Rate(ship.TractorBeamPower, 2), ship.TractorBeamPower));
                Row(34580, Pair(Rate(ship.Speed, 3), ship.Speed));
                Row(34390, Num(ship.Maneuverability));
                Row(34391, Num(ship.Detection));
                Row(34581, Pair(Rate(ship.WeaponRechargeRate, 1), ship.WeaponRechargeRate));
                Row(34582, Num(ship.Bombardment));
                foreach (var (header, arc) in new[] { (34583, ship.Fore), (34592, ship.Aft), (34593, ship.Starboard), (34594, ship.Port) })
                {
                    Row(header);
                    Row(34584, Num(arc.Turbolaser));
                    Row(34401, Num(arc.IonCannon));
                    Row(34585, Num(arc.LaserCannon));
                }
                return new(34562, rows, []);
            }
            case >= 0x30 and < 0x3C when o is Character c: // title 34595
            {
                var commanding = live && c.CommandKind != 0 ? c.Container is Fleet or StarSystem ? c.Container : c.Container?.Container as Fleet : null;
                if (commanding is null) Row(34596, text: None);
                else Row(34596, name(commanding));
                Row(Attached, Name(parent));
                var see = Knowledge.See(game, c, side);
                int status = v.Enroute ? EnrouteText : see.Captured ? 34630 : live && c.Injury != 0 ? 34631
                    : v.Mission is { Record.Unlisted: false } ? OnMission : Awaiting;
                Row(Status, text: status);
                EtaRow(o, v);
                int force = live ? c.Force : (int)c.Definition.JediLevel.Base;
                Row(34597, text: force < 10 ? None : force < 20 ? 34633 : force < 80 ? 34640 : force < 100 ? 34641 : force < 120 ? 34642 : 34643);
                int Skill(int i, Skill s) => live ? c.Skills[i] : (int)s.Base;
                var k = c.Definition.Skills;
                Row(34598, Num(Skill(SkillIndex.Diplomacy, k.Diplomacy)));
                Row(34599, Num(Skill(SkillIndex.Espionage, k.Espionage)));
                Row(34600, Num(Skill(SkillIndex.Combat, k.Combat)));
                Row(34601, Num(Skill(SkillIndex.Leadership, k.Leadership)));
                Row(34608);
                YesNo(34609, Skill(SkillIndex.ShipDesign, k.ShipDesign) != 0);
                YesNo(34610, Skill(SkillIndex.TroopTraining, k.TroopTraining) != 0);
                YesNo(34611, Skill(SkillIndex.FacilityDesign, k.FacilityDesign) != 0);
                Row(34615);
                YesNo(34435, c.Definition.CanBeAdmiral != 0);
                YesNo(34433, c.Definition.CanBeGeneral != 0);
                YesNo(34434, c.Definition.CanBeCommander != 0);
                return new(34595, rows, []);
            }
            case >= 0x20 and < 0x28: // defence facility / HQ, title 34403
            {
                EtaRow(o, v);
                Row(Location, Name(parent));
                StatusA();
                MaintenanceRow();
                if (f >= 0x22 && game.Definitions.DefenseFacilities.FirstOrDefault(r => r.Common.Ref == o.Type) is { } d)
                {
                    Row(34393, Num(d.AttackStrength));
                    Row(34627, Num(d.ShieldStrength));
                    Row(34409, Num(d.BombardmentDefense));
                }
                return new(34403, rows, []);
            }
            case >= 0x1C and < 0x20 when game.Definitions.Fighters.FirstOrDefault(r => r.Common.Ref == o.Type) is { } fighter: // title 34384
            {
                Row(Attached, Name(parent));
                EtaRow(o, v);
                MaintenanceRow();
                Row(34386, Pair(fighter.SquadronSize - (live ? o.SquadSizeDamage : 0), fighter.SquadronSize));
                Row(34387, Num(fighter.Hyperdrive));
                Row(34388, Pair(fighter.ShieldStrength, fighter.ShieldStrength));
                Row(34389, Num(fighter.Speed));
                Row(34390, Num(fighter.Maneuverability));
                Row(34391, Num(fighter.Detection));
                Row(34392, Pair(fighter.TorpedoRange, fighter.TorpedoRange));
                Row(34393);
                Row(34400, Pair(fighter.AttackStrength.LaserCannon, fighter.AttackStrength.LaserCannon));
                Row(34401, Pair(fighter.AttackStrength.IonCannon, fighter.AttackStrength.IonCannon));
                Row(34402, Pair(fighter.Torpedoes, fighter.Torpedoes));
                return new(34384, rows, []);
            }
            case >= 0x10 and < 0x14 when game.Definitions.Troops.FirstOrDefault(r => r.Common.Ref == o.Type) is { } troop: // title 34405
            {
                Row(Attached, Name(parent));
                Row(Status, text: v.Enroute ? EnrouteText : !v.Completed ? 34673 : Awaiting);
                EtaRow(o, v);
                MaintenanceRow();
                Row(34407, Num(troop.AttackStrength));
                Row(34408, Num(troop.DefenseStrength));
                Row(34392, Num(troop.BombardmentDefense));
                Row(34417, Num(troop.Detection));
                return new(34405, rows, []);
            }
            case >= 0x28 and < 0x30: // manufacturing facility, Mine, Refinery, title 34560
            {
                var fac = game.Definitions.Facilities.FirstOrDefault(r => r.Common.Ref == o.Type);
                Row(Location, Name(parent));
                EtaRow(o, v);
                StatusA();
                MaintenanceRow();
                Row(34561, Num(fac?.ProcessingRate ?? 0));
                Row(34392, Num(fac?.BombardmentDefense ?? 0));
                return new(34560, rows, []);
            }
            case >= 0x90 and < 0x98 when o is StarSystem s: // title 34432
            {
                var see = Knowledge.See(game, s, side);
                Row(Status, text: see.Owner switch { Owner.Alliance => 34648, Owner.Empire => 34649, _ => 34657 });
                CommanderRow(34433, s, 3, false);
                CommanderRow(34434, s, 1, false);
                Row(34436, Num(see.Support));
                Row(34437, Num(100 - see.Support));
                Row(34438, Pair(s.UsedEnergy, s.Energy));
                Row(34439, Pair(s.UsedRaw, s.RawMaterials));
                Row(34440);
                var inside = Inside(s);
                int Count(uint lo, uint hi) => inside.Count(x => x.In(lo, hi));
                Row(34441, Num(Count(0x2C, 0x2D)));
                Row(34448, Num(Count(0x2D, 0x2E)));
                Row(34449, Num(Count(0x28, 0x29)));
                Row(34450, Num(Count(0x29, 0x2A)));
                Row(34451, Num(Count(0x2A, 0x2B)));
                Row(34452, Num(Count(0x24, 0x25)));
                Row(34453, Num(Count(0x22, 0x24)));
                Row(34454, Num(Count(0x25, 0x26)));
                Row(34455);
                Row(34456, Num(Count(0x10, 0x14)));
                Row(34457, Num(Count(0x1C, 0x20)));
                return new(34432, rows, []);
            }
            case >= 0x50 and < 0x80 when o is Mission m: // title 34422
            {
                if (m.Record.Unlisted) return new(34422, rows, []);
                var members = game.Characters.Cast<GameObject>().Concat(game.Created).Where(x => Family.Of(x.Type) is >= 0x30 and < 0x40)
                    .Select(x => ScreenViews.View(game, x, side)).Where(x => x.Mission == m).ToList();
                GameObject? target = null;
                if (members.FirstOrDefault() is { } first)
                    target = first.Owner == side ? m.Objective : m.Kind is >= 0x50 and < 0x60 ? Knowledge.Location(game, first.Object, side) : null;
                if (target is null) Row(34423, text: 34087);
                else Row(34423, name(target));
                if (members.FirstOrDefault() is { } lead) EtaRow(lead.Object, lead);
                Row(34424, Num(members.Count));
                Row(34425, Num(members.Count(x => m.Decoys.Contains(x.Object))));
                return new(34422, rows, []);
            }
            case >= 0x3C and < 0x40 when game.Definitions.SpecialForces.FirstOrDefault(r => r.Common.Ref == o.Type) is { } force: // title 34416
            {
                Row(Attached, Name(parent));
                Row(Status, text: v.Enroute ? EnrouteText : v.Mission is not null ? OnMission : !v.Completed ? UnderConstruction : Awaiting);
                EtaRow(o, v);
                MaintenanceRow();
                int Skill(int i, Skill s) => live && o is Agent a ? a.Skills[i] : (int)s.Base;
                Row(34418, Num(Skill(SkillIndex.Diplomacy, force.Skills.Diplomacy)));
                Row(34419, Num(Skill(SkillIndex.Espionage, force.Skills.Espionage)));
                Row(34420, Num(Skill(SkillIndex.Combat, force.Skills.Combat)));
                Row(34421, Num(Skill(SkillIndex.Leadership, force.Skills.Leadership)));
                return new(34416, rows, []);
            }
            case >= 0xA0 and < 0xB0 when o is ManufacturingManager mm: // title by kind
            {
                var sys = mm.System;
                var owner = Knowledge.See(game, sys, side).Owner;
                uint yard = mm.Kind switch { ManufacturingKind.Construction => 0x2A, ManufacturingKind.Ship => 0x28, _ => 0x29 };
                int n = Inside(sys).Count(x => x.Family == yard && x.Owner == owner);
                Row(Location, name(sys));
                Row(Status, text: n == 0 ? 34674 : mm.Orders.Count == 0 ? 34665 : mm.Kind == ManufacturingKind.Training ? 34673 : 34672);
                if (mm.Orders.Count > 0)
                {
                    Row(34664, Num(mm.Orders.Count));
                    if (mm.Owner == sys.Owner && Economy.EstimateDays(game, mm, mm.Orders[0].Type, 1, true) is { } days) Row(34661, Num(game.Day + days));
                }
                return new(mm.Kind switch { ManufacturingKind.Construction => 6195, ManufacturingKind.Ship => 6185, _ => 6193 }, rows, []);
            }
        }
        return new(0, rows, []);
    }

    /// <summary>The page picture of a manager: GOKRES 263 construction, 262 shipyard, 264 training.</summary>
    public static int ManagerPicture(ManufacturingManager m) => m.Kind switch { ManufacturingKind.Construction => 263, ManufacturingKind.Ship => 262, _ => 264 };

    // Galaxy Overview category lists, in call order
    static readonly (uint Lo, uint Hi)[] Categories =
        [(0x20, 0x22), (0x2A, 0x2B), (0x18, 0x1C), (0x25, 0x26), (0x1C, 0x20), (0x22, 0x23), (0x2C, 0x2D), (0x24, 0x25), (0x2D, 0x2E),
         (0x28, 0x29), (0x3C, 0x40), (0x14, 0x18), (0x29, 0x2A), (0x10, 0x14), (0x23, 0x24)];

    /// <summary>
    /// Galaxy Overview (side key, menu 0x113): the side's real Existing objects per category list, grouped by definition:
    /// one row per group, picture GOKRES (the string number) + 0x4000, text "count (3 digits)  maintenance sum (4 digits) name"; rows in
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
                int maintenance = Family.Of(g.Key) is >= 0x10 and < 0x40 ? g.Count() * game.MaintenanceCost(g.Key) : 0;
                items.Add(new((int)Family.Of(g.Key), r.Common.StringNumber + 0x4000, true, 0, $"{g.Count():D3}  {maintenance:D4} {definition(r.Common)}"));
            }
        return new(0, [], [.. items.OrderBy(i => i.Id)]);
    }

    /// <summary>
    /// Objectives (null key, menu 0x114): six rows (standard victory) or only the Coruscant and HQ rows (headquarters
    /// victory), each the "not yet" or "done" text by the victory state; pictures are STRATEGY bitmaps.
    /// </summary>
    public static StatusPage Objectives(GameState game, Owner side)
    {
        var w = game.VictoryState;
        bool std = game.Settings.VictoryFlag == 0;
        bool coruscant = w.CoruscantAlliance || w.CoruscantDestroyed;
        (int Id, int Picture, int Not, int Done, bool Met, bool Std)[] rows = side == Owner.Alliance
            ? [(1, 1068, 34689, 34690, coruscant, false), (2, 11013, 34697, 34698, w.PalpatineCaptured, true),
               (3, 11055, 34701, 34702, w.VaderCaptured, true), (4, 904, 34692, 34695, w.HqGone, false),
               (5, 11028, 34708, 34711, w.LukeCaptured, true), (6, 11032, 34704, 34707, w.MothmaCaptured, true)]
            : [(1, 904, 34693, 34694, w.HqGone, false), (2, 11028, 34709, 34710, w.LukeCaptured, true),
               (3, 11032, 34705, 34706, w.MothmaCaptured, true), (4, 1068, 34688, 34691, coruscant, false),
               (5, 11013, 34696, 34699, w.PalpatineCaptured, true), (6, 11055, 34700, 34703, w.VaderCaptured, true)];
        return new(0, [], [.. rows.Where(r => std || !r.Std).Select(r => new StatusItem(r.Id, r.Picture, false, r.Met ? r.Done : r.Not))]);
    }
}
