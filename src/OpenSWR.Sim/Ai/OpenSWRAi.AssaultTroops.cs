using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// AssaultTroops: a landing troop kills its defender only when its attack strength beats the defender's defence by a margin, so a
/// cheap garrison troop sent in an assault dies without a kill. Rule 1: while the attack groups' troops need more than the side's
/// assault-role troops (and no garrison at risk is short), training builds the best assault troop, also while the side's total
/// troop need is met. Rule 2: an assault loads the spare troops highest attack strength first, so the troops that stay are the
/// defenders. Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    bool AssaultTroops => On(V7.AssaultTroops);

    /// <summary>The order troops leave in: key order; with AssaultTroops and an assault's game the highest attack strength first.</summary>
    List<GameObject> Leaving(GameState? assault, IEnumerable<GameObject> troops) => AssaultTroops && assault is not null
        ? [.. troops.OrderByDescending(x => TroopAttack(assault, x)).ThenBy(x => x.Key)]
        : [.. troops.OrderBy(x => x.Key)];

    static int TroopAttack(GameState game, GameObject troop) => game.Definitions.Resolve(troop.Type)?.GetOrZero(SdField.TroopAttackStrength) ?? 0;
}
