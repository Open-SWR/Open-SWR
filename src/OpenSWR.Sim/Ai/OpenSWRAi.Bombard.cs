using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Bombard: own fleets with bombardment in an enemy-held system bombard its garrison
/// (military-only Bombard command: no support cost unless its one extra shot hits a production facility) at most once every <see cref="BombardDays"/> per system. Commands only, no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>Days between two bombardments of one system.</summary>
    public const int BombardDays = 5;

    /// <summary>System ref, mapped to the day of the last bombardment issued there. Saved.</summary>
    readonly SortedDictionary<uint, int> _bombarded = [];

    /// <summary>Measurement: bombardments issued by this step.</summary>
    public int Bombards { get; private set; }

    /// <summary>
    /// Rule 1-3: per enemy-held system (view, key order) holding own Usable non-Enroute fleets with fleetBombard &gt; 0, unless its
    /// known enemy fleets are stronger than those fleets, the side's support there is at least 50 % (the bombarder loses support
    /// under the combat rules), the view shows no enemy troops or defence facilities, or it was bombarded less than
    /// <see cref="BombardDays"/> ago. The bombarding fleets stay used for this decision, except carriers whose troops already
    /// reach the attack troops: those may still land after the bombardment.
    /// </summary>
    internal void Bombard(GameState game, Known k, HashSet<GameObject> used)
    {
        foreach (var r in _bombarded.Where(e => game.Day - e.Value >= BombardDays).Select(e => e.Key).ToList()) _bombarded.Remove(r);
        foreach (var s in game.Systems.Where(s => !_bombarded.ContainsKey(s.Ref) && Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } v && o == Enemy
                     && (Side == Owner.Alliance ? v.Support : 100 - v.Support) < 50).OrderBy(s => s.Key))
        {
            var there = Fleets(game).Where(f => f.Container == s && f.Usable && !f.Enroute && !used.Contains(f) && Combat.FleetBombard(game, f) > 0).OrderBy(f => f.Key).ToList();
            if (there.Count == 0 || !EnemyGarrisonSeen(k, s)
                || k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy)) > there.Sum(f => k.Strength(f, Side))) continue;
            if (!IssueBombard(game, s, there, used, CommandType.Bombard)) { if (!Room) return; continue; }
            _bombarded[s.Ref] = game.Day;
            Bombards++;
            var carrying = there.Where(Carrying).ToList();
            if (carrying.Count > 0 && TroopsAboard(carrying) >= AttackTroopsFor(k, s)) used.ExceptWith(carrying);
        }
    }

    /// <summary>The view shows enemy troops (families 16 to 19) or defence facilities (families 34 to 39) in the system.</summary>
    bool EnemyGarrisonSeen(Known k, StarSystem s) => k.Contents(s).Any(v => v.Owner == Enemy && (v.In(Families.Troop, Families.CapitalShip) || v.In(Families.IonCannon, Families.Shipyard)));

    /// <summary>
    /// A bombardment order by these fleets: Bombard = troops, defence, HQ, one extra shot at a production facility;
    /// BombardVariant2 = those, production facilities and energy / raw materials. Only production / energy / raw-material hits cost support.
    /// </summary>
    bool IssueBombard(GameState game, StarSystem s, List<Fleet> fleets, HashSet<GameObject> used, CommandType type) =>
        Issue(game, new Command(type, Side) { Targets = [.. fleets.Select(f => ((GameObject)f, GroupMode.Object))], ArgRef = s }, used);

    void SaveBombard(JsonObject j)
    {
        if (_bombarded.Count > 0) j["bombarded"] = new JsonArray([.. _bombarded.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
    }

    void LoadBombard(JsonObject j)
    {
        if (j["bombarded"] is JsonArray a) foreach (var e in a) _bombarded[(uint)e![0]!] = (int)e[1]!;
    }
}
