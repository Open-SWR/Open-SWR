using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>
/// Soak run (OPENSWR_SOAK set): new games with computer controllers on both sides, every galaxy size, two difficulties, 400 days
/// through the clock (12 ticks per day) with the automatic battle engine live. Fails on an exception, a day that does not finish
/// within 3 x ticks-per-day host ticks or 60 s, a refused command with an unexpected code, or a failed save / load every 25 days:
/// the game continues from the loaded save, whose AI must keep its operations and planner states and which
/// must save byte-equal. Reports per game: days reached, battles, commands per side, refusals by code.
/// </summary>
public class SoakTests(ITestOutputHelper output)
{
    const string Soak = "OPENSWR_SOAK";
    const int Days = 400, TicksPerDay = 12;
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>Refusal codes that can follow from state changing between the decision tick and the execution tick.</summary>
    // A target or the destination went Enroute, a target was Destroyed (for example by the stage-13 battle), the destination
    // changed owner, a recruitment validated in the same think as another that took the last candidate, or a destination ship
    // was destroyed. Validation-phase retries 0x212 (0xA0, 3) and research (0x40, 0x2C) are expected too: logged only.
    // At execution: (0xA0, 3) = the manager's members became Suspended after the decision. (0x40, 0x23) = the mission
    // objective's owner changed after the decision (for example a recon target turned own; recon searches without a team).
    // (1, 0x12) also covers a 0x201 whose target system was destroyed after the issue tick. Validation (0x90, 1) on a
    // 0x214 [construction manager] that drags another planner's queued units to a full site is expected too: logged only.
    // Validation (0xA0, 3) on planners 5 / 6's 0x212: the AI cannot see Suspended, so a manager whose members are all
    // Suspended still offers itself, and the order repeats each run. Planner 3's 0x210 (0xA0, 3) is the same family: logged only.
    // At execution: (8, 7) = a 0x234 validated with the Blockade bit, then enemy fleets arrived before the execution tick
    // (BattlePending, Blockade clear). Validation, logged only, because a stage-13 battle changes the system after the
    // stage-8 decision: (8, 7) on planner 8's 0x234 during a pending battle (strike state 0xE and its check have no
    // Blockade test); (0x90, 4) on planner 2's 0x210 after a battle left the chosen site blockaded (state 9 has no site test).
    // Same family, logged only: (0x90, 4) on planners 6 / 5's 0x212 (the 0x214 target turned blockaded between the
    // 0x214 and the 0x212 think; the failed 0x214 leaves the queue empty, so the TargetKey keeps it) and (0x14, 1) on planner 10's
    // 0x211 (a battle's finish re-dock filled the chosen carrier between the 0x214 and the 0x211 think).
    // At execution, (0x90, 4) on the planner-13 HQ move 0x201 [0x20000005] (seed 12345, a game whose battles the recordings
    // do not cover): the HQ's system became blockaded with withdraw(S, F) < 31 between the validation and the execution.
    // Validation (1, 0x19) on planners 3 / 6 / 7 / 10 / 11's 0x210 / 0x211 / 0x212 with product 0: a load between the product
    // pick and the order (the original saves no product for these planners). Allowed only as that planner's first production
    // order after a load (product 0 = before its next pick); any other (1, 0x19) fails the soak. The test reads the order's own
    // product (Fields[0]), i.e. the field the order copies: the template product for a 0x210, the unit product for a 0x211 / 0x212.
    // Also, a pick without candidates leaves product none in the original (the count steps of planners 6 / 7 / 10 then still send
    // the order). Unreachable with the install data, reachable with mods, so a mod soak would have to accept (1, 0x19) on such an order too.
    // (0xF3, 1) on a computer 0x210 / 0x211 / 0x212, at validation and at execution: no re-check after the product pick and no
    // reservation between planners, so another order or a capacity loss can use up the free maintenance first.
    static readonly HashSet<OrderResult> Expected = [new(1, 0x11), new(1, 0x12), new(1, 0x21), new(1, 0x22), new(1, 0x28), new(0x40, 0x35),
        new(0xA0, 3), new(0x40, 0x23), new(8, 7), new(0x90, 4)];

    /// <summary>
    /// The OpenSWR AI's covert 0x240 at execution, keyed "exec openswr 240 &lt;kind&gt;": (0x40, 0x20) = the target character or unit
    /// moved between the decision and the execution tick.
    /// </summary>
    static readonly HashSet<string> OpenSWRCovertMoved = [.. new[] { 0x56, 0x58, 0x61, 0x62, 0x63, 0x69 }.Select(k => $"exec openswr 240 {k:X2}")];

    [GameTheory(Soak)]
    [InlineData(874334749, 1, 0)]
    [InlineData(401859171, 2, 0)]
    [InlineData(532947196, 3, 0)]
    [InlineData(445898697, 1, 2)]
    [InlineData(12345, 2, 2)]
    [InlineData(99991, 3, 2)]
    public void ComputerAgainstComputer(int seed, int galaxy, int difficulty) => Run(output, Definitions, seed, galaxy, difficulty, Days);

    /// <summary>The OpenSWR AI on one side against the original AI: as above, and both sides still own systems at the end.</summary>
    [GameTheory(Soak)]
    [InlineData(874334749, 1, 0, Owner.Alliance)]
    [InlineData(401859171, 2, 2, Owner.Empire)]
    public void OpenSWRAgainstComputer(int seed, int galaxy, int difficulty, Owner openswr)
    {
        var game = Run(output, Definitions, seed, galaxy, difficulty, Days, openswr);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire }) Assert.Contains(game.Systems, s => s.Owner == side && !s.Destroyed);
    }

    /// <summary>One soak game (also used by the mod tests with modded definitions); a save / load checkpoint every 25 days.</summary>
    internal static GameState Run(ITestOutputHelper output, GameDefinitions definitions, int seed, int galaxy, int difficulty, int days, Owner? openswr = null)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, galaxy, difficulty, 0, seed), definitions);
        var issued = new Dictionary<Owner, int> { [Owner.Alliance] = 0, [Owner.Empire] = 0 };
        var refused = new Dictionary<(string, OrderResult), int>();
        var examples = new Dictionary<(string, OrderResult), string>();
        var strikes = new SortedDictionary<int, int>(); // executed fleet-op commands: bombard 0x220-0x223, assault 0x234, split 0x270
        var ui = new Dictionary<Owner, UiRandom>();
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int state = seed ^ (int)side;
            ui[side] = new UiRandom(() => state);
            game.Controllers[side] = side == openswr ? new OpenSWRController(side) : new ComputerController(side) { Rand = n => ui[side].Rand(n) };
            game.Sides[side].Speed = TicksPerDay;
        }
        int battles = 0, deathStarBattles = 0, loads = 0;
        var productRefusals = new List<string>(); // (1, 0x19) outside the first-order-after-load case
        void Validated()
        {
            foreach (var (side, c) in game.Controllers.Where(e => e.Value is ComputerController))
            {
                var ordered = new HashSet<int>(); // planners with a production order since the last load
                foreach (var (p, cmd, r) in ((ComputerController)c).IssueTrace!)
                {
                    bool production = cmd.Type is >= 0x210 and <= 0x212;
                    if (r == new OrderResult(1, 0x19)
                        && !(loads > 0 && production && p is 3 or 6 or 7 or 10 or 11 && cmd.Fields[0] == 0 && !ordered.Contains(p)))
                        productRefusals.Add($"{side} p{p} {cmd.Type:X} day {game.Day} fields [{string.Join(", ", cmd.Fields.Select(f => f.ToString("X")))}]");
                    if (production) ordered.Add(p);
                }
            }
            // the OpenSWR AI's validation refusals (logged only: it remembers them)
            var traces = game.Controllers.Select(e => (e.Key, e.Value is ComputerController cc ? cc.IssueTrace! : [.. ((OpenSWRController)e.Value).IssueTrace!.Select(t => (0, t.Command, t.Result))]));
            foreach (var (side, trace) in traces)
                foreach (var (p, cmd, r) in trace.Where(t => !t.Result.Success))
                {
                    var key = (p == 0 && game.Controllers[side] is OpenSWRController ? $"validate openswr {cmd.Type:X}" : $"validate p{p} {cmd.Type:X}", r);
                    refused[key] = refused.GetValueOrDefault(key) + 1;
                    examples.TryAdd(key, $"{side} [{string.Join(", ", cmd.Targets.Select(t => t.Target.Ref.ToString("X8")))}] → {cmd.ArgRef?.Ref:X8} fields [{string.Join(", ", cmd.Fields.Select(f => f.ToString("X")))}]");
                }
        }
        void Attach()
        {
            foreach (var c in game.Controllers.Values)
                if (c is ComputerController cc) cc.IssueTrace = [];
                else ((OpenSWRController)c).IssueTrace = [];
            game.Clock.Executed = (c, r) =>
            {
                if (c.Type is 1 or 0x280 || ForcedContains(c)) return;
                if (r.Success && c.Type is >= 0x220 and <= 0x223 or 0x234 or 0x270) strikes[c.Type] = strikes.GetValueOrDefault(c.Type) + 1;
                if (r.Success) issued[c.Side]++;
                else
                {
                    var key = (game.Controllers.GetValueOrDefault(c.Side) is OpenSWRController
                        ? c.Type == 0x240 ? $"exec openswr 240 {Family.Of(c.Fields[2]):X2}" : $"exec openswr {c.Type:X}" : $"exec {c.Type:X}", r);
                    refused[key] = refused.GetValueOrDefault(key) + 1;
                    examples.TryAdd(key, $"{c.Side} [{string.Join(", ", c.Targets.Select(t => t.Target.Ref.ToString("X8")))}] → {c.ArgRef?.Ref:X8} day {game.Day} fields [{string.Join(", ", c.Fields.Select(f => f.ToString("X")))}]");
                }
            };
            game.BattleResults = s =>
            {
                battles++;
                // a Death Star scene: a Usable Death Star in a fleet, or one under construction at the ship manager
                if (s.Contents.OfType<Fleet>().Any(f => f.Existing && f.Contents.Any(x => DeathStar.IsDeathStar(x.Type) && x.Usable))
                    || s.Managers[(int)ManufacturingKind.Ship]?.Orders.FirstOrDefault() is { } q && DeathStar.IsDeathStar(q.Type))
                    deathStarBattles++;
                return null;
            };
        }
        Attach();
        var invariants = new ViewInvariants(game);
        var breaks = new List<string>(); // Invariant breaks, checked between ticks
        try
        {
            while (game.Day <= days && !game.GameOver)
            {
                int day = game.Day, start = game.Clock.Tick;
                var step = Task.Run(() =>
                {
                    while (game.Day == day && !game.GameOver)
                    {
                        game.Clock.Step(game);
                        if (breaks.Count < 20) breaks.AddRange(invariants.Check($"day {game.Day} tick {game.Clock.Tick}"));
                        if (game.Clock.Tick - start > 3 * TicksPerDay) throw new InvalidOperationException($"day {day} did not finish in {3 * TicksPerDay} ticks");
                    }
                });
                if (!step.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException($"day {day} still running after 60 s (livelock)");
                step.GetAwaiter().GetResult();
                if (game.Day % 25 == 0 && game.Day != day && !game.GameOver)
                {
                    Validated();
                    game = Checkpoint(output, game, definitions, side => n => ui[side].Rand(n));
                    loads++;
                    Attach();
                    invariants = new ViewInvariants(game);
                }
                if (game.Day % 25 == 0 && game.Day != day) ForceBattle(game, game.Day / 25 % 2 == 0 ? Owner.Alliance : Owner.Empire);
            }
        }
        finally
        {
            Validated();
            output.WriteLine($"seed {seed} galaxy {galaxy} difficulty {difficulty}: day {game.Day}{(game.GameOver ? $" (game over, {game.Winner})" : "")}, " +
                $"battles {battles} (Death Star {deathStarBattles}), commands Alliance {issued[Owner.Alliance]} / Empire {issued[Owner.Empire]}; " +
                $"fleet-op ships {string.Join(" / ", game.Controllers.Values.OfType<ComputerController>().Select(c => c.Model.FleetOps.Values.Sum(o => o.Ships.Count)))}; " +
                string.Join(", ", strikes.Select(x => $"{x.Key:X} × {x.Value}")) +
                // strategic repair: units ever damaged (0x380 slots), slots active now
                $"; repair slots {game.Timers.Values.Count(t => t.Type == 0x380)}, active {game.Timers.Values.Count(t => t.Type == 0x380 && t.Active)}" +
                $"; timer slots {game.Timers.Count}, staged objects {game.Staging.Contents.Count}" +
                $"; systems Alliance {game.Systems.Count(s => s.Owner == Owner.Alliance)} / Empire {game.Systems.Count(s => s.Owner == Owner.Empire)}" +
                (openswr is { } o ? $" (openswr {o}: refusals remembered {((OpenSWRController)game.Controllers[o]).Refusals})" : ""));
            foreach (var ((what, r), n) in refused.OrderBy(x => x.Key.Item1)) output.WriteLine($"  refused {what} ({r.Category:X}, {r.Code:X}) × {n}  e.g. {examples.GetValueOrDefault((what, r))}");
            foreach (var b in breaks) output.WriteLine($"  invariant {b}");
            // entries naming a deleted object: a live view's entry is not original state a frozen one is drift
            foreach (var ((x, side), v) in game.Views.Where(e => e.Value.Parent is { Deleted: true } || e.Value.RoleMission is { Deleted: true }))
                output.WriteLine($"  stale entry {side} {x.Ref:X8} ({(Knowledge.IsLive(x, side) ? "live" : "frozen")}): parent {v.Parent?.Ref:X8} role {v.RoleMission?.Ref:X8}");
        }
        Assert.Empty(breaks);
        Assert.Empty(refused.Where(x => x.Key.Item1.StartsWith("exec") && !Expected.Contains(x.Key.Item2)
            && !(x.Key.Item2 == new OrderResult(0xF3, 1) && x.Key.Item1 is "exec 210" or "exec 211" or "exec 212" or "exec openswr 210" or "exec openswr 211" or "exec openswr 212")
            && !(OpenSWRCovertMoved.Contains(x.Key.Item1) && x.Key.Item2 == new OrderResult(0x40, 0x20))));
        Assert.Empty(productRefusals);
        return game;
    }

    /// <summary>The engine needs battles the AIs do not start yet: the side's first idle fleet with capital ships is sent to a system holding an enemy fleet.</summary>
    static readonly HashSet<Command> Forced = [];
    static bool ForcedContains(Command c) { lock (Forced) return Forced.Contains(c); }

    static void ForceBattle(GameState game, Owner side)
    {
        bool Armed(Fleet f) => f.Usable && !f.Enroute && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Usable);
        var fleet = game.Systems.SelectMany(x => x.Contents.OfType<Fleet>()).FirstOrDefault(f => f.Owner == side && Armed(f));
        var target = game.Systems.FirstOrDefault(x => x.Contents.OfType<Fleet>().Any(f => f.Owner != side && f.Owner is Owner.Alliance or Owner.Empire && Armed(f)));
        if (fleet is null || target is null || fleet.Container == target) return;
        var c = new Command(0x201, side) { Targets = [(fleet, 0)], ArgRef = target };
        lock (Forced) Forced.Add(c);
        game.Clock.Submit(c);
    }

    /// <summary>
    /// Save and load (the game continues from the loaded copy, same UI generators). The load keeps every operation, task and
    /// planner state (only its load effects change the AI), and the loaded game saves byte-equal (the UI generator
    /// state left out: the load's catalogue lists draw).
    /// </summary>
    static GameState Checkpoint(ITestOutputHelper output, GameState game, GameDefinitions definitions, Func<Owner, Func<int, int>> rand)
    {
        var first = Save(game);
        using var stream = new MemoryStream(first);
        var loaded = SaveGame.Load(stream, definitions, rand);
        foreach (var side in game.Controllers.Keys)
        {
            if (game.Controllers[side] is OpenSWRController x)
            {
                var y = Assert.IsType<OpenSWRController>(loaded.Controllers[side]);
                Assert.Equal((x.Issued, x.Decided, x.Staging?.Ref, x.Target?.Ref, x.Refusals, x.Save().ToJsonString()), (y.Issued, y.Decided, y.Staging?.Ref, y.Target?.Ref, y.Refusals, y.Save().ToJsonString()));
                continue;
            }
            var (a, b) = ((ComputerController)game.Controllers[side], (ComputerController)loaded.Controllers[side]);
            Assert.True(b.Started);
            Assert.Equal((a.Model.Turn, a.Model.NextTurn), (b.Model.Turn, b.Model.NextTurn)); // producer-facility turn
            Assert.Equal(a.Model.Ops.Keys, b.Model.Ops.Keys);
            Assert.Equal(a.Model.FleetOps.Keys, b.Model.FleetOps.Keys);
            Assert.Equal(a.Model.CharacterOps.Keys, b.Model.CharacterOps.Keys);
            Assert.Equal(a.Model.Tasks.Select(t => t.Key), b.Model.Tasks.Select(t => t.Key));
            Assert.Equal(a.Planners.Select(p => (p.Type, p.Status, p.Phase, p.State, p.Priority, p.Done)), b.Planners.Select(p => (p.Type, p.Status, p.Phase, p.State, p.Priority, p.Done)));
        }
        Assert.Equal(Text(first), Text(Save(loaded)));
        // The save leaves out the dead timer slots of objects in staging
        var staged = loaded.Staging.Contents.Select(o => o.Ref).ToHashSet();
        Assert.DoesNotContain(loaded.Timers.Values, t => staged.Contains(t.Target) && !t.Active && !loaded.Clock.TimedEvents.OfType<TimerEvent>().Any(e => e.Slot == t));
        output.WriteLine($"  day {game.Day}: timer slots {game.Timers.Count}, saved {loaded.Timers.Count}, {first.Length} bytes");
        return loaded;
    }

    static string Text(byte[] save) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(save), "\"uiRngState\":-?[0-9]+", "");

    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }
}
