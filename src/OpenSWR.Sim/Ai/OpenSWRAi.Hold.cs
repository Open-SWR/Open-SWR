namespace OpenSWR.Sim.Ai;

/// <summary>
/// Hold: a Swarm group leaves max(<see cref="SwarmGarrison"/>,
/// the system's garrison requirement) troops at a system it took, and tops up uprising own systems it passes. Views and own data only.
/// </summary>
public sealed partial class OpenSWRController
{
    bool Holding => On(V7.Hold);

    /// <summary>
    /// The garrison requirement: ceil((60 - own support share %) / 10), at least 0. An own system's view is live,
    /// so its real support share is the view's (Inferred: <see cref="StarSystem.SupportFor"/>).
    /// </summary>
    internal int HoldRequirement(GameState game, StarSystem s) => Math.Max(0, (int)Math.Ceiling((60 - s.SupportFor(Side)) / 10.0));

    /// <summary>Test hook: the troops the system keeps by the garrison rules (<see cref="Keep"/>).</summary>
    internal int KeepOf(StarSystem s) => Keep(s);

    /// <summary>
    /// Hold: an emptied group (no troops aboard, none spare at its base) returns to reload at the nearest own system with spare
    /// troops (Inferred: rule 4's "nearest own base" read as one that can reload it; none: rule 4 as before).
    /// </summary>
    internal StarSystem? HoldReload(GameState game, StarSystem from, HashSet<GameObject> used) =>
        !Holding ? null : Own(game).Where(x => x != from && SwarmSpare(game, x, used).Count > 0).OrderBy(x => Orders.Distance(from, x)).ThenBy(x => x.Key).FirstOrDefault();

    /// <summary>The troops a Swarm base keeps: <see cref="SwarmGarrison"/>, under Hold at least the garrison requirement.</summary>
    int SwarmFloor(GameState game, StarSystem s) => Holding ? Math.Max(SwarmGarrison, HoldRequirement(game, s)) : SwarmGarrison;

    /// <summary>
    /// Hold: a group whose fleet sits (not Enroute) at an own system in uprising in the side's view drops troops from its ships until
    /// the system holds the floor, before it moves on (Inferred: "passes" read as the fleet being at that system at the decision).
    /// </summary>
    void HoldUprisings(GameState game, List<Fleet> fleets, HashSet<GameObject> used)
    {
        if (!Holding) return;
        foreach (var g in _groups)
        {
            var f = fleets.FirstOrDefault(x => x.Ref == g.Fleet);
            if (f is null || f.Enroute || f.Container is not StarSystem s || s.Owner != Side || !Knowledge.See(game, s, Side).Uprising) continue;
            int missing = SwarmFloor(game, s) - s.Contents.Count(x => OwnTroop(x, Side));
            foreach (var troop in f.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents).Where(x => OwnTroop(x, Side) && x.Usable && !used.Contains(x)).OrderBy(x => x.Key).ToList())
            {
                if (missing <= 0 || !Room) break;
                if (Issue(game, Move(Side, troop, s), used)) missing--;
            }
        }
    }
}
