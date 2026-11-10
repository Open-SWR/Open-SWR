using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    static BattleResult Result(Side[] sides)
    {
        var S = new int[7];
        bool Active(Side x) => x.Lists.Any(u => u.State == TacticalUnitState.Active);
        S[0] = sides[0].State == TacticalSideState.Withdrawn && sides[0].Units.Any(u => u.State == TacticalUnitState.Withdrawn) ? 1 : 0;
        var scene = sides[1].Scene;
        S[2] = sides[1].State == TacticalSideState.Withdrawn && (sides[1].Units.Any(u => u.State == TacticalUnitState.Withdrawn) || scene?.Withdrawn == true) ? 1 : 0;
        S[1] = !Alive(scene) && !Active(sides[1]) && S[0] == 0 ? 1 : 0; // Not computed while the scene is alive
        S[3] = !Active(sides[0]) && S[2] == 0 ? 1 : 0;
        if (S[1] != 0 && S[3] != 0) S[1] = S[3] = 0;
        return Records(sides, S, true);
    }

    /// <summary>The records F / C / D of every unit of the battle list with the summary S; both branches (the tactical battle uses the same records).</summary>
    internal static BattleResult Records(Side[] sides, int[] S, bool auto)
    {
        var scene = sides[1].Scene;
        var fighters = new Dictionary<GameObject, FighterResult>();
        var ships = new Dictionary<GameObject, ShipResult>();
        var deathStars = new Dictionary<GameObject, DeathStarResult>();
        foreach (var u in sides.SelectMany(x => x.Units))
        {
            double c4 = u.State == TacticalUnitState.Destroyed ? 0 : Math.Max(1, u.Hull);
            // fighter record; a Death Star's key resolves to the current scene
            if (u.Type == TacticalUnitType.Fighter) fighters[u.Object] = new FighterResult(u.State == TacticalUnitState.Destroyed ? 0 : Math.Max(1, (int)Math.Truncate(u.Hull / 8.0)), u.State is TacticalUnitState.Docked or TacticalUnitState.Recovered ? Resolve(u.Carrier, sides)?.Object : null);
            else ships[u.Object] = new ShipResult((int)Math.Truncate(c4), u.SpeedCh, u.ShieldCh, u.HyperCh, u.TractorCh, u.WeaponCh);
        }
        // One D record from the current scene for every Death Star (its hull, and whether that is above 0)
        foreach (var ds in sides[1].DeathStars) deathStars[ds] = new DeathStarResult((int)Math.Truncate(scene!.Hull), scene.Hull > 0);
        return new BattleResult(auto, S, fighters, ships, deathStars);
    }
}
