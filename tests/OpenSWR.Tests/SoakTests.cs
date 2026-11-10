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
    // was destroyed. Validation-phase retries of the build command (160, 3) and research (64, 44) are expected too: logged only.
    // At execution: (160, 3) = the manager's members became Suspended after the decision. (64, 35) = the mission
    // objective's owner changed after the decision (for example a recon target turned own; recon searches without a team).
    // (Missing) also covers a move sub-command whose target system was destroyed after the issue tick. Validation (144, 1) on a
    // target command [construction manager] that drags another planner's queued units to a full site is expected too: logged only.
    // Validation (160, 3) on planners 5 / 6's build commands: the AI cannot see Suspended, so a manager whose members are all
    // Suspended still offers itself, and the order repeats each run. Planner 3's build command (160, 3) is the same family: logged only.
    // At execution: (8, 7) = an assault validated with the Blockade bit, then enemy fleets arrived before the execution tick
    // (BattlePending, Blockade clear). Validation, logged only, because a stage-13 battle changes the system after the
    // stage-8 decision: (8, 7) on planner 8's assault during a pending battle (strike state 14 and its check have no
    // Blockade test); (144, 4) on planner 2's build command after a battle left the chosen site blockaded (state 9 has no site test).
    // Same family, logged only: (144, 4) on planners 6 / 5's build commands (the target command turned blockaded between the
    // target command and the build think; the failed target command leaves the queue empty, so the TargetKey keeps it) and (20, 1) on planner 10's
    // build command (a battle's finish re-dock filled the chosen carrier between the target command and the build think).
    // At execution, (144, 4) on the planner-13 HQ move [Refs.Headquarters] (seed 12345, a game whose battles the recordings
    // do not cover): the HQ's system became blockaded with withdraw(S, F) < 31 between the validation and the execution.
    // Validation (NotPlaceable) on planners 3 / 6 / 7 / 10 / 11's build commands with product 0: a load between the product
    // pick and the order (the original saves no product for these planners). Allowed only as that planner's first production
    // order after a load (product 0 = before its next pick); any other (NotPlaceable) fails the soak. The test reads the order's own
    // product (Fields[0]), i.e. the field the order copies: the template product for the first build command, the unit product for the other two.
    // Also, a pick without candidates leaves product none in the original (the count steps of planners 6 / 7 / 10 then still send
    // the order). Unreachable with the install data, reachable with mods, so a mod soak would have to accept (NotPlaceable) on such an order too.
    // (243, 1) on a computer build command, at validation and at execution: no re-check after the product pick and no
    // reservation between planners, so another order or a capacity loss can use up the free maintenance first.
    static readonly HashSet<OrderResult> Expected = [Refusal.Enroute, Refusal.Missing, Refusal.DestinationEnroute, Refusal.NoDestination, Refusal.DestinationRefused, Refusal.RecruitmentDone,
        Refusal.AllSuspended, Refusal.ObjectiveOwnerRefused, Refusal.NoBlockade, Refusal.Blockaded];

    /// <summary>
    /// The OpenSWR AI's covert mission command at execution, keyed "exec openswr 240 &lt;kind&gt;": (64, 32) = the target character or unit
    /// moved between the decision and the execution tick.
    /// </summary>
    static readonly HashSet<string> OpenSWRCovertMoved = [.. new[] { 86, 88, 97, 98, 99, 105 }.Select(k => $"exec openswr 240 {k}")];

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
        var strikes = new SortedDictionary<CommandType, int>(); // executed fleet-op commands: bombard (four orders), assault, split
        var ui = new Dictionary<Owner, UiRandom>();
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int state = seed ^ (int)side;
            ui[side] = new UiRandom(() => state);
            game.Controllers[side] = side == openswr ? new OpenSWRController(side) : new ComputerController(side) { Rand = n => ui[side].Rand(n) };
            game.Sides[side].Speed = TicksPerDay;
        }
        int battles = 0, deathStarBattles = 0, loads = 0;
        var productRefusals = new List<string>(); // (NotPlaceable) outside the first-order-after-load case
        void Validated()
        {
            foreach (var (side, c) in game.Controllers.Where(e => e.Value is ComputerController))
            {
                var ordered = new HashSet<PlannerType>(); // planners with a production order since the last load
                foreach (var (p, cmd, r) in ((ComputerController)c).IssueTrace!)
                {
                    bool production = cmd.Type is >= CommandType.ProduceFacility and <= CommandType.ProduceTroop;
                    if (r == Refusal.NotPlaceable
                        && !(loads > 0 && production && p is PlannerType.Defence or PlannerType.Troop or PlannerType.SystemFighter or PlannerType.FleetFighter or PlannerType.CapitalShip && cmd.Fields[0] == 0 && !ordered.Contains(p)))
                        productRefusals.Add($"{side} p{p} {(int)cmd.Type} day {game.Day} fields [{string.Join(", ", cmd.Fields.Select(f => f.ToString()))}]");
                    if (production) ordered.Add(p);
                }
            }
            // the OpenSWR AI's validation refusals (logged only: it remembers them)
            var traces = game.Controllers.Select(e => (e.Key, e.Value is ComputerController cc ? cc.IssueTrace! : [.. ((OpenSWRController)e.Value).IssueTrace!.Select(t => (PlannerType.None, t.Command, t.Result))]));
            foreach (var (side, trace) in traces)
                foreach (var (p, cmd, r) in trace.Where(t => !t.Result.Success))
                {
                    var key = (p == PlannerType.None && game.Controllers[side] is OpenSWRController ? $"validate openswr {(int)cmd.Type}" : $"validate p{p} {(int)cmd.Type}", r);
                    refused[key] = refused.GetValueOrDefault(key) + 1;
                    examples.TryAdd(key, $"{side} [{string.Join(", ", cmd.Targets.Select(t => t.Target.Ref.ToString()))}] → {cmd.ArgRef?.Ref} fields [{string.Join(", ", cmd.Fields.Select(f => f.ToString()))}]");
                }
        }
        void Attach()
        {
            foreach (var c in game.Controllers.Values)
                if (c is ComputerController cc) cc.IssueTrace = [];
                else ((OpenSWRController)c).IssueTrace = [];
            game.Clock.Executed = (c, r) =>
            {
                if (c.Type is CommandType.TickToken or CommandType.GameSpeed || ForcedContains(c)) return;
                if (r.Success && c.Type is >= CommandType.Bombard and <= CommandType.BombardDestroy or CommandType.Assault or CommandType.CreateFleet) strikes[c.Type] = strikes.GetValueOrDefault(c.Type) + 1;
                if (r.Success) issued[c.Side]++;
                else
                {
                    var key = (game.Controllers.GetValueOrDefault(c.Side) is OpenSWRController
                        ? c.Type == CommandType.CreateMission ? $"exec openswr 240 {Family.Of(c.Fields[2])}" : $"exec openswr {(int)c.Type}" : $"exec {(int)c.Type}", r);
                    refused[key] = refused.GetValueOrDefault(key) + 1;
                    examples.TryAdd(key, $"{c.Side} [{string.Join(", ", c.Targets.Select(t => t.Target.Ref.ToString()))}] → {c.ArgRef?.Ref} day {game.Day} fields [{string.Join(", ", c.Fields.Select(f => f.ToString()))}]");
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
                string.Join(", ", strikes.Select(x => $"{(int)x.Key} × {x.Value}")) +
                // strategic repair: units ever damaged (the repair slots), slots active now
                $"; repair slots {game.Timers.Values.Count(t => t.Type == TimerType.Repair)}, active {game.Timers.Values.Count(t => t.Type == TimerType.Repair && t.Active)}" +
                $"; timer slots {game.Timers.Count}, staged objects {game.Staging.Contents.Count}" +
                $"; systems Alliance {game.Systems.Count(s => s.Owner == Owner.Alliance)} / Empire {game.Systems.Count(s => s.Owner == Owner.Empire)}" +
                (openswr is { } o ? $" (openswr {o}: refusals remembered {((OpenSWRController)game.Controllers[o]).Refusals})" : ""));
            foreach (var ((what, r), n) in refused.OrderBy(x => x.Key.Item1)) output.WriteLine($"  refused {what} ({(int)r.Category}, {(int)r.Code}) × {n}  e.g. {examples.GetValueOrDefault((what, r))}");
            foreach (var b in breaks) output.WriteLine($"  invariant {b}");
            // entries naming a deleted object: a live view's entry is not original state a frozen one is drift
            foreach (var ((x, side), v) in game.Views.Where(e => e.Value.Parent is { Deleted: true } || e.Value.RoleMission is { Deleted: true }))
                output.WriteLine($"  stale entry {side} {Family.Of(x.Ref)}:{SdRef.IndexOf(x.Ref)} ({(Knowledge.IsLive(x, side) ? "live" : "frozen")}): parent {v.Parent?.Ref} role {v.RoleMission?.Ref}");
        }
        Assert.Empty(breaks);
        Assert.Empty(refused.Where(x => x.Key.Item1.StartsWith("exec") && !Expected.Contains(x.Key.Item2)
            && !(x.Key.Item2 == Refusal.NoMaintenance && x.Key.Item1 is "exec 210" or "exec 211" or "exec 212" or "exec openswr 210" or "exec openswr 211" or "exec openswr 212")
            && !(OpenSWRCovertMoved.Contains(x.Key.Item1) && x.Key.Item2 == Refusal.NoTargetLocation)));
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
        var c = new Command(CommandType.Move, side) { Targets = [(fleet, GroupMode.Object)], ArgRef = target };
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
