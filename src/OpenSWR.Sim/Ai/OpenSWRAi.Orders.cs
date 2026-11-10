using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    /// <summary>
    /// Systems taken from or lost to the other side since the last decision: a chat line, losses first. Lost = own before, now
    /// seen as the other side's (not destroyed); taken = own now, seen as the other side's before. Neutral changes say nothing.
    /// </summary>
    internal void OwnerChanges(GameState game, Known? k = null)
    {
        var now = new SortedSet<uint>(Own(game).Select(s => s.Ref));
        if (_owned is not null)
        {
            foreach (uint r in _owned.Except(now))
                if (game.Systems.First(s => s.Ref == r) is var lost && Knowledge.See(game, lost, Side) is { Destroyed: false } v && v.Owner == Enemy)
                {
                    MarkLost(lost, game.Day); // a counter-attack target
                    RecordAssaultSeen(game, k, lost); // measurement (aimatch only)
                    // the troops the side sees there now landed in the assault (a loss without troops seen is no landing)
                    if (On(V7.Experience) && k is not null && EnemyTroops(k, lost) is > 0 and var landed) RecordLanding(landed);
                    Taunt(game, OpenSWRTaunts.Occasion.SystemLost, lost);
                }
            if (On(V7.Cascade)) RecordSupportGains(game, now.Except(_owned).Where(_enemyHeld.Contains)); // cascade rule
            foreach (uint r in now.Except(_owned).Where(_enemyHeld.Contains)) Taunt(game, OpenSWRTaunts.Occasion.SystemTaken, game.Systems.First(s => s.Ref == r));
        }
        _owned = now;
        _enemyHeld = [.. game.Systems.Where(s => Knowledge.See(game, s, Side) is { Destroyed: false } v && v.Owner == Enemy).Select(s => s.Ref)];
    }

    /// <summary>The attack's chat line once an own fleet has arrived at the launch target (not at the launch: the move is no news yet).</summary>
    void Arrivals(GameState game)
    {
        if (_launchedAt is not { } r || game.Systems.First(s => s.Ref == r) is not { } at) return;
        if (!Fleets(game).Any(f => f.Container == at && !f.Enroute)) return;
        _launchedAt = null;
        Taunt(game, OpenSWRTaunts.Occasion.AttackLaunched, at);
    }

    /// <summary>
    /// One chat line (category Chat; the system named, with go-to, only when the human side sees it, else "that system") when the
    /// other side is a human and the last line is at least
    /// <see cref="TauntCooldownDays"/> old. Each occasion's list is walked in turn from an offset of the game seed (the counter
    /// wraps, so a saved counter stays valid when the file's list changed length); an occasion without lines stays silent.
    /// </summary>
    void Taunt(GameState game, OpenSWRTaunts.Occasion occasion, StarSystem s)
    {
        if (Taunts?.Lines(occasion) is not { Count: > 0 } lines) return;
        if (!game.Controllers.TryGetValue(Enemy, out var other) || other is not HumanController) return;
        if (_lastTaunt is { } last && game.Day - last < TauntCooldownDays) return;
        string line = lines[(int)((uint)(game.Settings.Seed + _taunts[(int)occasion]++) % (uint)lines.Count)];
        _lastTaunt = game.Day;
        bool seen = ScreenViews.Sees(s, Enemy);
        Messages.Post(game, new GameMessage
        {
            Day = game.Day, Category = MessageCategory.Chat, Kind = 3,
            Text = line.Replace("{system}", seen ? Names?.Invoke(s.Definition.Common) ?? "that system" : "that system"),
            Subject = seen ? s.Ref : 0, New = true,
        });
    }

    // ---- command issue ----

    /// <summary>
    /// Refusal key: type, first target, argument, and in the last slot the product (production) or the mission type (CreateMission command); a
    /// command with several targets keeps its own key (the high bit set, with the target count in that slot), apart from the lead target's single command.
    /// </summary>
    static (CommandType, uint, uint, uint) KeyOf(Command c) => (c.Type, c.Targets.Count > 0 ? c.Targets[0].Target.Ref : 0, c.ArgRef?.Ref ?? 0,
        c.Targets.Count > 1 ? 2147483648u + (uint)c.Targets.Count : c.Type == CommandType.CreateMission ? c.Fields[2] : c.Fields.FirstOrDefault());

    /// <summary>Remembered as refused (tests).</summary>
    internal bool Refused(Command c) => _refused.ContainsKey(KeyOf(c));

    /// <summary>Validation only, with the refusal remembered as by <see cref="Issue"/>; nothing goes to the outbox.</summary>
    bool Valid(GameState game, Command c)
    {
        var key = KeyOf(c);
        if (_refused.ContainsKey(key)) return false;
        var result = Orders.Validate(game, c);
        if (result.Success) return true;
        IssueTrace?.Add((c, result)); // refusals here; an issued command is traced by Issue
        _refused[key] = game.Day + RefusalDays;
        return false;
    }

    bool Room => Issued < Budget;

    /// <summary>Validate, then the outbox (counted); a refusal is remembered for <see cref="RefusalDays"/>.</summary>
    internal bool Issue(GameState game, Command c, HashSet<GameObject> used)
    {
        if (!Room || GuardRefuses(game, c) || !Valid(game, c)) return false; // Guard
        IssueTrace?.Add((c, OrderResult.Ok));
        if (TroopMoveLog is not null && c.Type == CommandType.Move) // measurement only: own troop moves by step
            foreach (var (t, _) in c.Targets)
                if (Family.IsTroop(t.Type) && Missions.Location(t) is StarSystem from) TroopMoveLog.Add(new(game.Day, t.Ref, from.Ref, _step));
        Append(c);
        Issued++;
        foreach (var (t, _) in c.Targets) used.Add(t);
        foreach (var d in c.Decoys) used.Add(d);
        return true;
    }

    static Command Move(Owner side, GameObject o, GameObject to) => new(CommandType.Move, side) { Targets = [(o, GroupMode.Object)], ArgRef = to };
}
