namespace OpenSWR.Sim.Ai;

/// <summary>
/// Generals: a landing troop's duel adds its fleet general's leadership (divided by the duel rule's divisor), so an attack group
/// with a general kills more defenders per troop. Each decision, every attack group fleet at a system without a general aboard
/// takes the nearest free own minor character (major characters carry the story and the victory) that can be a general and whose
/// leadership is its best skill among diplomacy, espionage and leadership (diplomats and spies stay on their missions); highest
/// leadership among equals. The character boards the fleet's first capital ship (travelling there when it is elsewhere) and is
/// made general once aboard; as a general it is no longer free for missions. Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    bool Generals => On(V7.Generals);

    /// <summary>The fleet carries an own general aboard one of its capital ships.</summary>
    internal static bool HasGeneral(Fleet f) => Combat.Commander(f, (StarSystem)f.Container!, CommandKind.General) is not null;

    /// <summary>A free minor character that can be a general and whose leadership is not below its diplomacy and espionage.</summary>
    bool GeneralCandidate(GameState game, Character c, HashSet<GameObject> used)
    {
        if (!Free(c, used) || c.Definition.CanBeGeneral == 0 || Family.Of(c.Type) < Families.MinorCharacter) return false;
        int lead = Missions.Enhanced(game, c, SkillIndex.Leadership);
        return lead >= Missions.Enhanced(game, c, SkillIndex.Diplomacy) && lead >= Missions.Enhanced(game, c, SkillIndex.Espionage);
    }

    internal void AssignGenerals(GameState game, HashSet<GameObject> used, List<Fleet> fleets)
    {
        foreach (var f in fleets.Where(f => !f.Enroute && f.Container is StarSystem && !HasGeneral(f)).Distinct().OrderBy(f => f.Key))
        {
            if (!Room) return;
            var at = (StarSystem)f.Container!;
            if (f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).OrderBy(x => x.Key).FirstOrDefault() is not { } ship) continue;
            // a general candidate already aboard (arrived, or still on its way) becomes the fleet's general
            var aboard = f.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents).OfType<Character>()
                .Where(c => c.Owner == Side && c.Existing && c.Definition.CanBeGeneral != 0 && Family.Of(c.Type) >= Families.MinorCharacter).ToList();
            if (aboard.Count > 0)
            {
                if (aboard.FirstOrDefault(c => !c.Enroute && GeneralCandidate(game, c, used)) is { } ready)
                    Issue(game, new Command(CommandType.MakeGeneral, Side) { Targets = [(ready, GroupMode.Object)] }, used);
                continue;
            }
            var pick = game.Characters.Where(c => GeneralCandidate(game, c, used))
                .OrderBy(c => Orders.Distance((StarSystem)Missions.Location(c)!, at)).ThenByDescending(c => Missions.Enhanced(game, c, SkillIndex.Leadership)).ThenBy(c => c.Key).FirstOrDefault();
            if (pick is null || !Issue(game, Move(Side, pick, ship), used)) continue;
            if (Missions.Location(pick) == at) Issue(game, new Command(CommandType.MakeGeneral, Side) { Targets = [(pick, GroupMode.Object)] }, used);
        }
    }
}
