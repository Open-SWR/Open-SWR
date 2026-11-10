using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Doctrine, behind the <see cref="OpenSWRController.Doctrine"/> parts:
/// production by role shortfall, fighters by the known enemy composition, troops by garrison / assault role, planetary guns and
/// shields by role, gathering by role, dominated units never built. Roles come from <see cref="Roles"/> (definition stats only).
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>Fighter squadrons one screening ship answers (Inferred: the template's "anti-fighter >= 1.2 x the known fighter attack" in ships).</summary>
    public const int SquadronsPerScreen = 5;

    Dictionary<uint, UnitRoles>? _roles;
    string _rolesKey = "";

    /// <summary>The roles of the side's buildable units, recomputed when the buildable set changes (research, captured yards).</summary>
    internal IReadOnlyDictionary<uint, UnitRoles> BuildableRoles(GameState game)
    {
        var types = new[] { 0x28u, 0x29u, 0x2Au }.SelectMany(m => Research.Buildable(game, Side, m)).Select(r => r.Common.Ref)
            .Where(t => Roles.KindOf(t) is not null).Distinct().Order().ToList();
        string key = string.Join(",", types);
        if (_roles is null || key != _rolesKey) (_roles, _rolesKey) = (Roles.Compute(game, types), key);
        return _roles;
    }

    /// <summary>The buildable, non-dominated unit of a kind with the best score in a role (the maintenance within <paramref name="free"/>; ties: less maintenance, lower ref).</summary>
    internal SdRecord? BestFor(GameState game, UnitKind kind, UnitRole role, int free, Func<UnitRoles, bool>? filter = null)
    {
        var roles = BuildableRoles(game);
        var pick = roles.Values.Where(r => r.Kind == kind && !r.Dominated && r[role] > 0 && r.Maintenance <= free && (filter?.Invoke(r) ?? true))
            .OrderByDescending(r => r[role]).ThenBy(r => r.Maintenance).ThenBy(r => r.Type).FirstOrDefault();
        return pick is null ? null : game.Definitions.Resolve(pick.Type);
    }

    UnitRoles? RolesOf(GameState game, uint type) => BuildableRoles(game).TryGetValue(type, out var r) ? r
        : Roles.Compute(game, Roles.SideTypes(game, Side).Append(type)).GetValueOrDefault(type);

    /// <summary>
    /// The role shortfalls of the attack groups, each 0-1, the largest over the groups: line
    /// (1.5 x the target's known strength against the group's), screen (at least one ship with main role screen, one per
    /// <see cref="SquadronsPerScreen"/> known enemy squadrons), transport (the target's attack troops against the group's troop
    /// places), interdictor (one per group when one is buildable), bombard (the target holds known defence facilities or more than 4
    /// known troops: the group's bombardment against the known planetary shields + 1, Inferred: shots = bombardment - shields). The
    /// screen share is capped at <see cref="MaxScreenShare"/> of the whole fleet's maintenance.
    /// </summary>
    internal Dictionary<UnitRole, double> RoleShortfall(GameState game, Known k)
    {
        var result = new Dictionary<UnitRole, double>();
        _bombardGap = 0;
        void Max(UnitRole r, double v) => result[r] = Math.Max(result.GetValueOrDefault(r), Math.Clamp(v, 0, 1));
        bool interdictorBuildable = BuildableRoles(game).Values.Any(r => r.Interdictor && !r.Dominated);
        // the screen cap over the whole fleet
        var allShips = Fleets(game).SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type) && x.Existing).ToList();
        int fleetMaintenance = allShips.Sum(x => game.MaintenanceCost(x.Type));
        int screenMaintenance = allShips.Where(x => RolesOf(game, x.Type)?.Main == UnitRole.Screen).Sum(x => game.MaintenanceCost(x.Type));
        bool screensFull = screenMaintenance > 0 && screenMaintenance >= MaxScreenShare * fleetMaintenance;
        foreach (var g in _groups)
        {
            var fleets = Fleets(game).Where(f => f.Container == g.Staging || f.Container == g.Target).ToList();
            var ships = fleets.SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)).ToList();
            var roles = ships.Select(x => RolesOf(game, x.Type)).OfType<UnitRoles>().ToList();
            double need = 1.5 * k.Strength(g.Target, Enemy), have = fleets.Sum(f => k.Strength(f, Side));
            Max(UnitRole.Line, need <= 0 ? 0 : (need - have) / need);
            var seen = k.Contents(g.Target).Where(v => v.Owner == Enemy).SelectMany(v => v.Object is Fleet f ? k.Contents(f).SelectMany(c => k.Contents(c.Object).Prepend(c)) : [v]);
            int squadrons = seen.Count(v => v.In(0x1C, 0x20));
            int screens = Math.Max(1, (int)Math.Ceiling(1.2 * squadrons / SquadronsPerScreen));
            if (!screensFull)
                Max(UnitRole.Screen, (double)(screens - roles.Count(r => r.Main == UnitRole.Screen)) / screens);
            int troops = AttackTroopsFor(k, g.Target), places = ships.Sum(x => TroopRoom(game, x) + x.Contents.Count(y => Family.IsTroop(y.Type)));
            Max(UnitRole.Transport, (double)(troops - places) / troops);
            if (interdictorBuildable && !roles.Any(r => r.Interdictor)) Max(UnitRole.Durability, 1); // Durability stands for the interdictor slot
            bool fortified = k.Contents(g.Target).Any(v => v.Owner == Enemy && v.In(0x22, 0x28)) || EnemyTroops(k, g.Target) > 4;
            if (fortified)
            {
                double bombardNeed = BombardNeed(game, k, g.Target), bombard = ships.Sum(x => Bombardment(game, x.Type));
                Max(UnitRole.Bombard, (bombardNeed - bombard) / bombardNeed);
                _bombardGap = Math.Max(_bombardGap, bombardNeed - bombard);
            }
        }
        return result;
    }

    /// <summary>
    /// The capital ship to build (doctrine): the role with the largest shortfall (<see cref="RoleShortfall"/>; ties in role order;
    /// the interdictor slot first among equals), then the best buildable non-dominated ship in it per maintenance that fits; with no
    /// shortfall, the best line ship. Null: none fits.
    /// </summary>
    internal SdRecord? DoctrineShip(GameState game, Known k, int free)
    {
        // bombardment is a capability: a line ship whose bombardment covers the gap, else the ship with the most bombardment
        var shortfall = RoleShortfall(game, k).Where(e => e.Value > 0).OrderByDescending(e => e.Value)
            .ThenBy(e => e.Key == UnitRole.Durability ? -1 : (int)e.Key).Select(e => e.Key).ToList();
        foreach (var role in shortfall.Append(UnitRole.Line))
        {
            var pick = role == UnitRole.Durability ? BestFor(game, UnitKind.CapitalShip, UnitRole.Durability, free, r => r.Interdictor)
                : role == UnitRole.Bombard ? BestFor(game, UnitKind.CapitalShip, UnitRole.Line, free, r => Bombardment(game, r.Type) >= _bombardGap)
                    ?? BestFor(game, UnitKind.CapitalShip, UnitRole.Bombard, free)
                : BestFor(game, UnitKind.CapitalShip, role, free, r => r.Main == role) ?? BestFor(game, UnitKind.CapitalShip, role, free);
            if (pick is not null) return pick;
        }
        return null;
    }

    /// <summary>
    /// The fighter role the known enemy calls for: bombers when its known units are mostly capital ships (by count, over 2 : 1),
    /// interceptors when mostly fighters, superiority otherwise; the best non-dominated fighter of that role (hyperdrive-less ones
    /// only go into a carrier: the caller's context).
    /// </summary>
    internal SdRecord? DoctrineFighter(GameState game, Known k, int free, bool jumping = false)
    {
        var enemy = game.Systems.SelectMany(s => k.Fleets(s, Enemy)).SelectMany(f => k.Contents(f)).ToList();
        int ships = enemy.Count(v => Family.IsCapitalShip(v.Object.Type));
        int fighters = enemy.Count(v => v.In(0x1C, 0x20)) + enemy.Sum(v => k.Contents(v.Object).Count(c => c.In(0x1C, 0x20)));
        var role = ships > 2 * fighters ? UnitRole.Bomber : fighters > 2 * ships ? UnitRole.Interceptor : UnitRole.Superiority;
        return BestFor(game, UnitKind.Fighter, role, free, r => r.Main == role && (!jumping || !r.NeedsCarrier))
            ?? BestFor(game, UnitKind.Fighter, role, free, r => !jumping || !r.NeedsCarrier);
    }

    /// <summary>
    /// The troop to train (doctrine): garrison troops for the systems' garrisons; assault troops only while the side holds fewer
    /// assault-role troops than the attack groups' troops need.
    /// </summary>
    internal SdRecord? DoctrineTroop(GameState game, int deficit, int free)
    {
        var roles = BuildableRoles(game);
        int assaultTroops = game.Created.Count(o => OwnTroop(o, Side) && (roles.TryGetValue(o.Type, out var r) ? r : RolesOf(game, o.Type))?.Main == UnitRole.Assault);
        bool assault = deficit == 0 && Need.Attack.Values.Sum() > assaultTroops;
        return assault ? BestFor(game, UnitKind.Troop, UnitRole.Assault, free) : BestFor(game, UnitKind.Troop, UnitRole.Garrison, free);
    }

    /// <summary>
    /// Planetary defence sites (doctrine): every high-value own system that is threatened or on the border, by priority (the yards
    /// and the free maintenance limit how many are built):
    /// the best gun per maintenance where the system has no own gun; else the best shield where it has none and the known enemy
    /// ships in reach have bombardment.
    /// </summary>
    internal List<(StarSystem? Site, SdRecord? Product)> DoctrineDefenceSites(GameState game, Known k)
    {
        var places = EnemyPlaces(game, k);
        var enemies = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        var gun = BestFor(game, UnitKind.Defence, UnitRole.Gun, int.MaxValue, r => r.Main == UnitRole.Gun);
        var shield = BestFor(game, UnitKind.Defence, UnitRole.Shield, int.MaxValue, r => r.Main == UnitRole.Shield);
        var result = new List<(StarSystem?, SdRecord?)>();
        bool Has(StarSystem s, UnitRole role) => s.Contents.Any(x => x.Owner == Side && x.Existing && Roles.KindOf(x.Type) == UnitKind.Defence
            && Roles.RawScores(game, x.Type)![role] > 0);
        foreach (var (s, threat) in Own(game).Where(s => HighValue(s)).Select(s => (s, threat: Threat(game, k, s, places)))
                     .Where(x => x.threat > 0 || enemies.Any(e => Orders.TravelDays(game, x.s, e, 100) <= ReachDays))
                     .OrderByDescending(x => _values.GetValueOrDefault(x.s.Ref) * (1 + x.threat)).ThenBy(x => x.s.Key))
        {
            if (s.Energy <= s.UsedEnergy) continue;
            if (gun is not null && !Has(s, UnitRole.Gun)) result.Add((s, gun));
            else if (shield is not null && !Has(s, UnitRole.Shield) && EnemyBombardInReach(game, k, s, places)) result.Add((s, shield));
        }
        return result;
    }

    /// <summary>Screens are wanted only below this share of the side's whole fleet maintenance.</summary>
    public const double MaxScreenShare = 0.25;

    /// <summary>The largest bombardment gap of the last <see cref="RoleShortfall"/> (raw bombardment points).</summary>
    double _bombardGap;

    static int Bombardment(GameState game, uint type) => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == type)?.Bombardment ?? 0);

    /// <summary>Bombardment a fortified target wants: its known planetary shields + 1.</summary>
    int BombardNeed(GameState game, Known k, StarSystem target) =>
        1 + k.Contents(target).Where(v => v.Owner == Enemy && v.In(0x24, 0x25)).Sum(v => (int)(game.Definitions.DefenseFacilities.FirstOrDefault(r => r.Common.Ref == v.Object.Type)?.ShieldStrength ?? 0));

    /// <summary>
    /// The shipyard's fighter target away from its system: its current target fleet if it still has room, else the nearest own
    /// carrier fleet with room (Completed, not Enroute); <paramref name="room"/> = the fleet's room left this decision.
    /// </summary>
    Fleet? RemoteCarrier(GameState game, ManufacturingManager m, StarSystem s, Func<Fleet, int> room) =>
        m.TargetKey is Fleet { Enroute: false } t && t.Owner == Side && room(t) > 0 ? t
        : Fleets(game).Where(f => f.Container is StarSystem x && x != s && f.Completed && !f.Enroute && room(f) > 0)
            .OrderBy(f => Orders.Distance((StarSystem)f.Container!, s)).ThenBy(f => f.Key).FirstOrDefault();

    /// <summary>
    /// The fighter yards: per system holding own carriers with free room
    /// (Completed, not Enroute), the ship manager with a working shipyard in the own system nearest to it (the carriers' own system
    /// first), mapped to the first such carrier fleet there; a manager serves one carrier group.
    /// </summary>
    internal Dictionary<ManufacturingManager, Fleet> FighterYards(GameState game)
    {
        var yards = new Dictionary<ManufacturingManager, Fleet>();
        var managers = Own(game).SelectMany(s => ScreenViews.Managers(s, Side).Where(m => m.Kind == ManufacturingKind.Ship && Economy.Members(m).Any(f => !f.Suspended))
            .Select(m => (System: s, Manager: m))).ToList();
        foreach (var group in Fleets(game).Where(f => f.Container is StarSystem && f.Completed && !f.Enroute && FighterRoom(game, f) > 0)
                     .GroupBy(f => (StarSystem)f.Container!).OrderBy(g => g.Key.Key))
        {
            var nearest = managers.Where(x => !yards.ContainsKey(x.Manager)).OrderBy(x => Orders.Distance(x.System, group.Key)).ThenBy(x => x.System.Key).Select(x => x.Manager).FirstOrDefault();
            if (nearest is not null) yards[nearest] = group.OrderBy(f => f.Key).First();
        }
        return yards;
    }

    bool EnemyBombardInReach(GameState game, Known k, StarSystem s, List<StarSystem> places) =>
        places.Where(e => Orders.TravelDays(game, e, s, 100) <= ReachDays).SelectMany(e => k.Fleets(e, Enemy)).SelectMany(f => k.Contents(f))
            .Any(v => Family.IsCapitalShip(v.Object.Type) && game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == v.Object.Type)?.Bombardment > 0);

    /// <summary>Gathering by role: 0 when the fleet brings a role the group lacks (screen, transport, interdictor, bombard), else 1.</summary>
    internal int FillsGroup(GameState game, Known k, AttackGroup g, Fleet f, List<Fleet> mine)
    {
        var have = mine.SelectMany(x => x.Contents).Where(x => Family.IsCapitalShip(x.Type)).Select(x => RolesOf(game, x.Type)).OfType<UnitRoles>().ToList();
        var bring = f.Contents.Where(x => Family.IsCapitalShip(x.Type)).Select(x => RolesOf(game, x.Type)).OfType<UnitRoles>().ToList();
        bool Lacks(Func<UnitRoles, bool> p) => !have.Any(p) && bring.Any(p);
        return Lacks(r => r.Main == UnitRole.Screen) || Lacks(r => r.Interdictor) || Lacks(r => r.Main == UnitRole.Transport)
            || Lacks(r => r[UnitRole.Bombard] > 0) ? 0 : 1;
    }
}
