namespace OpenSWR.Sim.Ai;

/// <summary>
/// Measurement only (aimatch; no effect on play): why Swarm groups lack troops, the troop room of their fleets, and the troops
/// landed at each own assault against the need. Recorded only when <see cref="TroopRoomLog"/> is set.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>One Swarm group decision: its fleet's capital ships and troop room, and, when it lacked troops, the split (a / b / c).</summary>
    public sealed record SwarmTroopDecision(int Day, uint Fleet, uint Target, int Ships, int Room, int Need, char? Short);

    /// <summary>One own troop landing: troops landed against the target's need, and the outcome (null while open).</summary>
    public sealed class AssaultLanding(int day, uint target, int landed, int need)
    {
        public int Day { get; } = day;
        public uint Target { get; } = target;
        public int Landed { get; } = landed;
        public int Need { get; } = need;
        public bool? Won { get; set; }
    }

    /// <summary>The Swarm group decisions (null = off).</summary>
    public List<SwarmTroopDecision>? TroopRoomLog { get; set; }
    public List<AssaultLanding> OwnLandings { get; } = [];

    /// <summary>A fleet's troop room: the troop capacity of its existing capital ships.</summary>
    int FleetRoom(GameState game, Fleet f) => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)
        .Sum(x => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type)?.TroopCapacity ?? 0));

    /// <summary>
    /// A Swarm group's decision (measurement): (a) the fleet's room is below the troops it needs (it can never launch at that target),
    /// (b) room enough but no spare troops at its base, (c) other; null when the lack of troops was not the reason.
    /// </summary>
    void RecordSwarmTroops(GameState game, AttackGroup g, Fleet f, StarSystem staging, int need, bool shortOfTroops, HashSet<GameObject> used)
    {
        if (TroopRoomLog is null) return;
        int room = FleetRoom(game, f), ships = f.Contents.Count(x => Family.IsCapitalShip(x.Type) && x.Existing);
        char? kind = !shortOfTroops ? null : room < need ? 'a' : SwarmSpare(game, staging, used).Count == 0 ? 'b' : 'c';
        TroopRoomLog.Add(new(game.Day, f.Ref, g.Target.Ref, ships, room, need, kind));
    }

    /// <summary>An own troop landing (measurement): the troops aboard the landing fleets against the target's need.</summary>
    void RecordOwnLanding(GameState game, StarSystem target, int landed, int need)
    {
        if (TroopRoomLog is not null) OwnLandings.Add(new(game.Day, target.Ref, landed, need));
    }

    /// <summary>The landings' outcomes (measurement): won when the target is the side's 3 days on (Inferred), else lost.</summary>
    void ResolveLandings(GameState game)
    {
        if (TroopRoomLog is null) return;
        foreach (var l in OwnLandings.Where(l => l.Won is null && game.Day - l.Day >= 3))
            l.Won = game.Systems.First(s => s.Ref == l.Target).Owner == Side;
    }
}
