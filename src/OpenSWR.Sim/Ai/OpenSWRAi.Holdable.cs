namespace OpenSWR.Sim.Ai;

/// <summary>
/// Holdable (Swarm targets with own support share at
/// least <see cref="HoldShare"/>, or a fleet that can leave the requirement and still hold its attack troops) and Pacify (own
/// systems below <see cref="SafeShare"/> get diplomacy first, lowest share first, at most <see cref="PacifyMax"/> at once).
/// Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int HoldShare = 30, PacifyMax = 4;

    bool Holdable => On(V7.Holdable);
    bool Pacifying => On(V7.Pacify);

    /// <summary>The side's known support share of a system (percent), from its view.</summary>
    int KnownShare(GameState game, StarSystem s)
    {
        var v = Knowledge.See(game, s, Side);
        return Side == Owner.Alliance ? v.Support : 100 - v.Support;
    }

    /// <summary>
    /// Holdable: a Swarm target is allowed at a known own share of at least <see cref="HoldShare"/>; below, only when the fleet's
    /// troop room covers the requirement ceil((60 - share) / 10) plus the target's attack troops (Inferred: "leave its requirement
    /// and still hold its attack troops" read as room >= both together).
    /// </summary>
    internal bool HoldableTarget(GameState game, Known k, Fleet f, StarSystem target)
    {
        int share = KnownShare(game, target);
        if (share >= HoldShare) return true;
        int requirement = Math.Max(0, (int)Math.Ceiling((60 - share) / 10.0));
        return FleetRoom(game, f) >= requirement + AttackTroopsFor(k, target);
    }

    /// <summary>Pacify: own populated systems the views show below <see cref="SafeShare"/> and not in uprising, lowest share first.</summary>
    internal List<StarSystem> PacifyTargets(GameState game) =>
        [.. game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Populated: true, Uprising: false, Destroyed: false } && o == Side && KnownShare(game, s) < SafeShare)
            .OrderBy(s => KnownShare(game, s)).ThenBy(s => s.Key)];
}
