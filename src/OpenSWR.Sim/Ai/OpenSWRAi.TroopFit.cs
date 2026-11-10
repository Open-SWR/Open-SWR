namespace OpenSWR.Sim.Ai;

/// <summary>
/// TroopFit (Swarm targets within the fleet's troop
/// room; reload where spare troops are) and LandGate (no under-strength landings; wait in orbit up to <see cref="OrbitWaitDays"/>).
/// Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int OrbitWaitDays = 10;

    bool FittingTroops => On(V7.TroopFit);
    bool GatingLandings => On(V7.LandGate);

    /// <summary>LandGate: per attack target ref, the first decision day a landing there was held (saved).</summary>
    readonly SortedDictionary<uint, int> _orbitWait = [];

    /// <summary>TroopFit rule 1: the target's attack troops fit the fleet's troop room.</summary>
    internal bool FitsRoom(GameState game, Known k, Fleet f, StarSystem target) => AttackTroopsFor(k, target) <= FleetRoom(game, f);

    /// <summary>
    /// TroopFit rule 2: for a group short of troops with no spare at its base, the nearest own system whose spare troops cover
    /// what it lacks (null: none, keep today's behaviour). Lack = the group's troops - the troops aboard its fleet.
    /// </summary>
    internal StarSystem? ReloadBase(GameState game, Known k, AttackGroup g, Fleet f, HashSet<GameObject> used)
    {
        if (g.Staging is not { } s || SwarmSpare(game, s, used).Count > 0) return null;
        int lack = SwarmTroopsFor(game, k, f, g.Target) - TroopsAboard([f]);
        if (lack <= 0) return null;
        return Own(game).Where(x => x != s && SwarmSpare(game, x, used).Count >= lack).OrderBy(x => Orders.Distance(s, x)).ThenBy(x => x.Key).FirstOrDefault();
    }

    /// <summary>LandGate rule 3: a landing goes only with at least the target's attack troops aboard (the view at the target).</summary>
    internal bool LandAllowed(Known k, IEnumerable<Fleet> there, StarSystem target) => !GatingLandings || TroopsAboard(there) >= AttackTroopsFor(k, target);

    /// <summary>
    /// LandGate rule 3 at a held landing: the first held day is kept; after <see cref="OrbitWaitDays"/> the fleets return to the
    /// group's base (Inferred: its staging system) and the wait ends.
    /// </summary>
    void HoldLanding(GameState game, StarSystem target, StarSystem staging, List<Fleet> there, HashSet<GameObject> used)
    {
        if (!_orbitWait.TryGetValue(target.Ref, out int since)) _orbitWait[target.Ref] = since = game.Day;
        if (game.Day - since < OrbitWaitDays) return;
        foreach (var f in there)
            if (!Issue(game, Move(Side, f, staging), used) && !Room) break;
        _orbitWait.Remove(target.Ref);
    }

    void SaveTroopFit(System.Text.Json.Nodes.JsonObject j)
    {
        if (_orbitWait.Count > 0) j["orbitWait"] = new System.Text.Json.Nodes.JsonArray([.. _orbitWait.Select(e => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonArray(e.Key, e.Value))]);
    }

    void LoadTroopFit(System.Text.Json.Nodes.JsonObject j)
    {
        if (j["orbitWait"] is System.Text.Json.Nodes.JsonArray a) foreach (var e in a) _orbitWait[(uint)e![0]!] = (int)e[1]!;
    }
}
