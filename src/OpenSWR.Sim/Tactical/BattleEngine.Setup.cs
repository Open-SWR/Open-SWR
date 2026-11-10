using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    /// <summary>Type index of a class: its string number in the engine table; not found gives 0 (a mod class: Inferred harmless).</summary>
    public static int TypeIndex(GameDefinitions defs, uint type) =>
        defs.Resolve(type) is { } r && Array.IndexOf(TypeStrings, r.Common.StringNumber) is var i and >= 0 ? i : 0;

    /// <summary>SD field; 0 beyond the record (fighter and Death Star records are shorter than CAPSHPSD).</summary>
    static int Sd(GameState game, GameObject o, SdField field) => game.Definitions.Resolve(o.Type) is { } r ? r.GetOrZero(field) : 0;

    static void Setup(GameState game, StarSystem s, Side[] sides)
    {
        var shipUnits = new Dictionary<GameObject, Unit>();
        Unit? Add(GameObject o, Unit? carrier = null)
        {
            if (o.Owner is not (Owner.Alliance or Owner.Empire)) return null;
            var u = new Unit(o, o.Owner == Owner.Alliance ? 0 : 1) { Carrier = carrier };
            var side = sides[u.Side];
            u.Key = ++side.Keys;
            u.TypeIndex = TypeIndex(game.Definitions, o.Type);
            uint f = Family.Of(o.Type);
            u.Type = f is >= Families.Fighter and < Families.Headquarters ? TacticalUnitType.Fighter : TacticalUnitType.CapitalShip;
            // per arc in engine order (fore, starboard, aft, port; the SD order is fore, aft, port, starboard): turbolaser, ion, laser
            SdField[][] sdArc =
            [
                [SdField.ForeTurbolaser, SdField.ForeIonCannon, SdField.ForeLaserCannon],
                [SdField.StarboardTurbolaser, SdField.StarboardIonCannon, SdField.StarboardLaserCannon],
                [SdField.AftTurbolaser, SdField.AftIonCannon, SdField.AftLaserCannon],
                [SdField.PortTurbolaser, SdField.PortIonCannon, SdField.PortLaserCannon],
            ];
            double scale = 1;
            if (u.Type == TacticalUnitType.Fighter)
            {
                int sq = Sd(game, o, SdField.SquadronSize);
                (u.HullMax, u.Hull, scale) = (sq * 8.0, (sq - o.SquadSizeDamage) * 8.0, sq);
                u.Torpedo = Sd(game, o, SdField.Torpedoes) * sq;
                u.TorpedoRange = Sd(game, o, SdField.TorpedoRange);
                u.BaseSpeed = Sd(game, o, SdField.Speed) * 0.4;
            }
            else
            {
                (u.HullMax, u.Hull) = (Sd(game, o, SdField.Hull), Sd(game, o, SdField.Hull) - o.HullDamage);
                (u.ShieldCh, u.WeaponCh, u.TractorCh, u.SpeedCh, u.HyperCh) = (o.Critical(0), o.Critical(1), o.Critical(2), o.Critical(3), o.Critical(4) + o.Critical(5));
                u.Blocker = Sd(game, o, SdField.GravityWell) != 0;
                u.BaseSpeed = Sd(game, o, SdField.Speed) * 0.4 * 0.6;
                shipUnits[o] = u;
            }
            u.Hyperdrives = (Sd(game, o, SdField.Hyperdrive) != 0 ? 1 : 0) + (Sd(game, o, SdField.HyperdriveIfDamaged) != 0 ? 1 : 0);
            for (int a = 0; a < 4; a++)
                (u.Arcs[a, 2], u.Arcs[a, 1], u.Arcs[a, 0]) = (Sd(game, o, sdArc[a][0]) * scale, Sd(game, o, sdArc[a][1]) * scale, Sd(game, o, sdArc[a][2]) * scale);
            (u.TlRange, u.IonRange, u.LaserRange) = (Sd(game, o, SdField.TurbolaserRange), Sd(game, o, SdField.IonCannonRange), Sd(game, o, SdField.LaserCannonRange));
            u.ShieldMax = u.Shield = Sd(game, o, SdField.ShieldStrength) * scale;
            (u.Wrr, u.Srr) = (Sd(game, o, SdField.WeaponRechargeRate), Sd(game, o, SdField.ShieldRechargeRate));
            if (u.Type == TacticalUnitType.CapitalShip) u.BaseSrr = u.Srr; // a fighter's is rewritten by every recharge read, so only ships keep it
            u.Maneuverability = Sd(game, o, SdField.Maneuverability);
            if (u.Type == TacticalUnitType.CapitalShip) u.Capacity = Sd(game, o, SdField.FighterCapacity); // Inferred mapping
            side.Units.Add(u);
            side.Battle.Add(u);
            // deploy: ships go to the non-fighter list; a fighter aboard a found carrier (a Death Star's: the current scene's hangar) that
            // cannot jump itself is docked: state 1, pending in the shared pending list; other fighters go to the fighter list
            if (u.Type == TacticalUnitType.CapitalShip) side.Ships.Add(u);
            else if (Resolve(carrier, sides) is { } c && !CanJump(u, side))
            {
                u.State = TacticalUnitState.Docked;
                side.Pending.Add(u);
                // setup dock: +1 only below the capacity; a full hangar writes nothing, the
                // fighter keeps its carrier key (Inferred)
                if (c.Docked < c.Capacity) c.Docked++;
                else u.Overflow = true;
            }
            else side.Fighters.Add(u);
            if (u.State == TacticalUnitState.Active) side.Undocked.Add(u);
            return u;
        }
        // Each registration replaces the current scene; side index 1's next key is used whatever the owner
        Unit Scene(GameObject ds, bool unfinished)
        {
            bool Fighter(GameObject y) => Family.Of(y.Type) is >= Families.Fighter and < Families.Headquarters;
            var sc = new Unit(ds, 1) { Type = TacticalUnitType.DeathStarScene, Key = ++sides[1].Keys, Charge = 100f, Unfinished = unfinished };
            (sc.HullMax, sc.Hull) = (F(Sd(game, ds, SdField.Hull)), F(Sd(game, ds, SdField.Hull) - ds.HullDamage));
            sc.Shielded = s.Contents.Any(x => Family.Of(x.Type) == Families.DeathStarShield && x.Usable); // any owner
            // capacity = FighterCapacity - fighters aboard (Inferred: Existing) + Usable fighters aboard
            sc.Capacity = Sd(game, ds, SdField.FighterCapacity) - ds.Contents.Count(y => Fighter(y) && y.Existing) + ds.Contents.Count(y => Fighter(y) && y.Usable);
            sides[1].Scene = sc;
            sides[1].DeathStars.Add(ds);
            return sc;
        }
        foreach (var fleet in s.Contents.OfType<Fleet>().Where(f => f.Existing).ToList())
        {
            var ships = fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).ToList();
            var units = new Dictionary<GameObject, Unit?>();
            foreach (var x in ships.Where(x => Family.Of(x.Type) < Families.DeathStar)) units[x] = Add(x);
            foreach (var x in ships.Where(x => Family.Of(x.Type) >= Families.DeathStar)) units[x] = Scene(x, false); // Usable implies Completed (Inferred)
            // Inside every fleet's iteration: the ship manager's unit under construction (the queue
            // head, Inferred) when of the Death-Star class and not Completed
            if (s.Managers[(int)ManufacturingKind.Ship] is { } mm && mm.Orders.FirstOrDefault() is { } q
                && Family.Of(q.Type) is >= Families.DeathStar and < Families.Fighter && !q.Completed)
                Scene(q, true);
            // Per ship: its fighters, then its
            // characters, troop regiments and special forces, Usable as the fighters (Inferred);
            // each record takes its side's next key and names the carrying ship's unit
            foreach (var ship in ships)
            {
                foreach (var f in ship.Contents.Where(y => Family.Of(y.Type) is >= Families.Fighter and < Families.Headquarters && y.Usable)) Add(f, units[ship]);
                foreach (var p in ship.Contents.OfType<Character>().Where(y => y.Usable)
                    .Concat(ship.Contents.Where(y => Family.IsTroop(y.Type) && y.Usable))
                    .Concat(ship.Contents.Where(y => Family.IsSpecialForce(y.Type) && y.Usable)))
                {
                    if (p.Owner is not (Owner.Alliance or Owner.Empire)) continue;
                    sides[p.Owner == Owner.Alliance ? 0 : 1].Keys++;
                    units[ship]?.Carried.Add(p);
                }
            }
        }
        foreach (var f in s.Contents.Where(y => Family.Of(y.Type) is >= Families.Fighter and < Families.Headquarters && y.Usable).ToList()) Add(f);
        foreach (var side in sides) Officers(game, s, side, shipUnits);
    }

    /// <summary>
    /// Commander / admiral: commander = first the first character directly in the system of
    /// the system's owner with command kind 1, then per Usable fleet of the side (key order) the first character with the kind aboard
    /// its capital ships (Existing, fleet owner); a strictly greater Combat (admiral: Leadership, fleets only) replaces. A bonus only
    /// when the pick has a record, i.e. sits aboard a ship unit: commander clamp(f32(Combat / 20) + 1, 1, 9), admiral
    /// clamp(9 - Leadership / 10, 1, 9).
    /// </summary>
    static void Officers(GameState game, StarSystem s, Side side, Dictionary<GameObject, Unit> shipUnits)
    {
        var owner = side.Index == 0 ? Owner.Alliance : Owner.Empire;
        Character? Pick(int kind, int skill, Character? pick)
        {
            foreach (var fleet in s.Contents.OfType<Fleet>().Where(f => f.Owner == owner && f.Usable))
                if (fleet.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents.OfType<Character>())
                        .FirstOrDefault(c => c.Existing && c.Owner == fleet.Owner && c.CommandKind == kind) is { } c
                    && (pick is null || c.Skills[skill] > pick.Skills[skill]))
                    pick = c;
            return pick;
        }
        bool Record(Character? c) => c?.Container is { } ship && shipUnits.ContainsKey(ship);
        var commander = Pick(CommandKind.Commander, SkillIndex.Combat, s.Contents.OfType<Character>().FirstOrDefault(c => c.Owner == s.Owner && c.CommandKind == CommandKind.Commander));
        var admiral = Pick(CommandKind.Admiral, SkillIndex.Leadership, null);
        if (Record(commander)) (side.Commander, side.CommanderShip) = (Math.Clamp(F(commander!.Skills[SkillIndex.Combat] / 20.0) + 1, 1, 9), shipUnits[commander.Container!]);
        if (Record(admiral)) (side.AdmiralShip, side.AdmiralValue) = (shipUnits[admiral!.Container!], Math.Clamp(9 - admiral.Skills[SkillIndex.Leadership] / 10, 1, 9));
    }
}
