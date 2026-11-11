using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    /// <summary>
    /// The optional steps, for ablation runs. Predict: every use of the battle predictor; PredictBattle: only the engage-or-withdraw
    /// float (BattleFloat). Doctrine (<see cref="Doctrine"/>) is the five parts: DoctrineProduction (shipyards by role shortfall,
    /// fighters by enemy and hangar room), DoctrineGather (fleets join groups by role), DoctrineTroops (garrison / assault troops by
    /// role), DoctrineDefence (planetary guns and shields), DoctrineFighterYard (one shipyard per carrier group takes only fighter
    /// orders while hangars have room). EconomyFirst, Recruit, CovertExperience, Teams, Intel, Agents, Decoys, ExploreTrial,
    /// ExplorePrior, PoolUseful, EspionageIdle, Swarm, TroopFit, LandGate, Hold, Holdable, Pacify, Guard, Hunt, Siege, HuntStill,
    /// Bombard, Wings, Cascade, RimFirst and LeanCost are the newer parts; each is described where it is implemented.
    /// </summary>
    public enum V7
    {
        Experience, LateEconomy, Fighters, Watch, Predict, PredictBattle,
        DoctrineProduction, DoctrineGather, DoctrineTroops, DoctrineDefence, DoctrineFighterYard, EconomyFirst,
        Recruit, CovertExperience, Teams, Intel, Agents, Decoys,
        ExploreTrial, ExplorePrior, PoolUseful, EspionageIdle, TroopFit, Swarm, LandGate, Hold, Holdable, Pacify,
        Guard, Hunt, Siege, HuntStill, Bombard, Wings, Cascade, RimFirst, LeanCost,
    }

    /// <summary>
    /// The features of an aimatch command line: comma-separated step names, "all" (<see cref="AllFeatures"/>) and "doctrine"
    /// (<see cref="Doctrine"/>) among them; "none" for no step. Null for an unknown name.
    /// </summary>
    public static ImmutableHashSet<V7>? ParseFeatures(string list)
    {
        if (list.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        var set = ImmutableHashSet<V7>.Empty;
        foreach (var name in list.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase)) set = set.Union(AllFeatures);
            else if (name.Equals("doctrine", StringComparison.OrdinalIgnoreCase)) set = set.Union(Doctrine);
            else if (Enum.TryParse(name, ignoreCase: true, out V7 f) && Enum.IsDefined(f) && !int.TryParse(name, out _)) set = set.Add(f);
            else return null;
        return set;
    }

    bool On(V7 f) => Features.Contains(f);
}
