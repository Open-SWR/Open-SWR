namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    /// <summary>
    /// The five system icon levels of the ship page: v = trunc(100 * fraction), fraction 0 when the base is 0. The fractions are
    /// shield recharge against base shield recharge rate, weapon rate against 3.75 times the base weapon rate, tractor power
    /// against its base, speed against base speed (above 100 with a bonus), and working hyperdrives against the full count.
    /// Level v &gt; 74 is 4, &gt; 49 is 3, &gt; 24 is 2, v != 0 is 1, else 0.
    /// </summary>
    public int[] SystemLevels(TacticalUnit t)
    {
        var u = t.U;
        static int V(double value, double b) => b != 0 ? (int)Math.Truncate(100 * (value / b)) : 0;
        int[] v =
        [
            V(BattleEngine.ShieldRate(u), u.BaseSrr),
            V(BattleEngine.WeaponRate(u), 3.75 * u.Wrr),
            V(TacticalBattle.TractorPowerOf(t), t.TractorPower),
            V(_b.Speed(t), u.BaseSpeed),
            V(u.Hyperdrives - u.HyperCh, u.Hyperdrives),
        ];
        return [.. v.Select(x => x > 74 ? 4 : x > 49 ? 3 : x > 24 ? 2 : x != 0 ? 1 : 0)];
    }

    /// <summary>
    /// Officer strip of the ship page: the info unit's character records, then its troop and special-force records,
    /// each in list order (the setup's: characters, troop regiments, special forces aboard).
    /// </summary>
    public IReadOnlyList<GameObject> Officers(TacticalUnit t) => t.U.Carried;

    /// <summary>
    /// Officer icon: the bitmap id from the table keyed by the SD string number (Inferred argument), drawn as that
    /// TACTICAL bitmap. 0 when the key is not in the table (no icon).
    /// </summary>
    public static int OfficerIcon(int key) => OfficerIcons.GetValueOrDefault(key);
}
