namespace OpenSWR.Sim;

/// <summary>
/// The encounter code of each encounter phase (2, 3, 7, 9): 0 no encounter, 1 agents, 2 garrison, 3 agents on the first resolver
/// and garrison on the second, 4 the system-or-fleet branch. Class init <see cref="Standard"/>, Adrift <see cref="Adrift"/>.
/// </summary>
public readonly record struct EncounterCodes(int Phase2, int Phase3, int Phase7, int Phase9)
{
    public static readonly EncounterCodes None = default, Standard = new(2, 1, 1, 4), Adrift = new(3, 0, 1, 4);
}
