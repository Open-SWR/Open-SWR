using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>One object-menu row before the RCDATA lookup: command id, enabled, checked.</summary>
public readonly record struct CommandRow(CommandType Id, bool Enabled, bool Checked);

/// <summary>
/// Object context menu commands: each object class's static command list (a per-class list),
/// the intersection over a selection, enabled = <see cref="Orders.Validate"/> of the command built with that
/// selection, checked, the confirmation set, and the commands that wait for a target (the main screen's targeting state). UI only:
/// no state change, no draw.
/// </summary>
public static class ObjectCommands
{
    public const CommandType Encyclopedia = CommandType.Encyclopedia, Status = CommandType.Status;

    /// <summary>Commands that go through the targeting state when no target is given (CreateMission opens the mission window after it).</summary>
    public static bool NeedsTarget(CommandType id) => id is CommandType.Move or CommandType.MoveVariant or CommandType.SetProductionTarget or CommandType.CreateMission;

    /// <summary>
    /// The object's command list for selection mode <paramref name="mode"/> (systems only: the selection's group mode).
    /// Ids without an RCDATA entry (MoveObject, CreateMoveMission, CharacterManagerOrder) are kept; the menu builder drops them.
    /// </summary>
    public static CommandType[] For(GameObject o, GroupMode mode = GroupMode.Object)
    {
        uint f = Family.Of(o.Type);
        CommandType[] combatUnit = [CommandType.Move, CommandType.MoveVariant, CommandType.MoveObject, CommandType.Scrap];
        CommandType[] agent = [CommandType.Move, CommandType.MoveVariant, CommandType.CharacterOrder, CommandType.CreateMission, CommandType.MoveObject, CommandType.CreateMoveMission];
        return o switch
        {
            // Observed in the original: no Rename row on a fleet, so Rename is left out here.
            Fleet => [CommandType.Move, CommandType.MoveVariant, CommandType.MoveObject, CommandType.Bombard, CommandType.BombardVariant1, CommandType.BombardVariant2, CommandType.BombardDestroy, CommandType.Assault, CommandType.Scrap],
            Mission => [CommandType.MissionOrder],
            ManufacturingManager m => m.Kind switch
            {
                ManufacturingKind.Construction => [CommandType.CancelProduction, CommandType.SetProductionTarget, CommandType.SetReserved, CommandType.ProduceFacility],
                ManufacturingKind.Ship => [CommandType.CancelProduction, CommandType.SetProductionTarget, CommandType.SetReserved, CommandType.SetProductName, CommandType.ProduceShip],
                _ => [CommandType.CancelProduction, CommandType.SetProductionTarget, CommandType.SetReserved, CommandType.ProduceTroop],
            },
            StarSystem => GroupModes.Group(mode) switch
            {
                GroupMode.Facilities => [CommandType.Scrap, CommandType.SetProductionTarget, CommandType.SetReserved],
                GroupMode.Ground => [CommandType.Move, CommandType.MoveVariant, CommandType.Scrap],
                GroupMode.Fleets => [CommandType.Move, CommandType.MoveVariant, CommandType.Scrap, CommandType.Bombard, CommandType.BombardVariant1, CommandType.BombardVariant2, CommandType.BombardDestroy, CommandType.Assault],
                GroupMode.Missions => [CommandType.MissionOrder],
                _ => [],
            },
            _ when f is >= Families.CapitalShip and < Families.Fighter => [.. combatUnit, CommandType.Rename, CommandType.CreateFleet],         // capital ships, Death Stars
            _ when f is >= Families.Fighter and < Families.Headquarters => combatUnit,                             // fighter squadrons
            _ when f is >= Families.Troop and < Families.CapitalShip => [CommandType.Move, CommandType.MoveVariant, CommandType.MoveObject, CommandType.Scrap],           // troop regiments
            _ when f is >= Families.MajorCharacter and < Families.SpecialForce => [.. agent, CommandType.ClearCommand, CommandType.MakeCommander, CommandType.MakeAdmiral, CommandType.MakeGeneral, CommandType.CharacterManagerOrder], // characters
            _ when f is >= Families.SpecialForce and < Families.Mission => agent,                                  // special forces
            _ when f == Families.Headquarters => [CommandType.Move, CommandType.MoveVariant, CommandType.MoveObject],                                // headquarters
            _ when f is > Families.Headquarters and < Families.MajorCharacter => [CommandType.Scrap],                                 // other facilities, defences, Mines, Refineries
            _ => [],
        };
    }

    /// <summary>The first entry's list, intersected with every other entry's (order of the first list).</summary>
    public static List<CommandType> Intersect(IReadOnlyList<(GameObject Object, GroupMode Mode)> selection)
    {
        if (selection.Count == 0) return [];
        var ids = For(selection[0].Object, selection[0].Mode).ToList();
        foreach (var (o, mode) in selection.Skip(1)) ids.RemoveAll(id => !For(o, mode).Contains(id));
        return ids;
    }

    /// <summary>
    /// The system containing the selection's first object (view parents walked up to [144, 152)); a system selects itself.
    /// The real location stands in for the view parents here (own objects: the same).
    /// </summary>
    public static StarSystem? SystemOf(GameObject o) => o as StarSystem ?? o.System;

    /// <summary>
    /// The command for row <paramref name="id"/> with the selection: ArgRef = <paramref name="target"/>; combat commands
    /// (Bombard to BombardDestroy, Assault) and CreateFleet take the selection's system; ProduceFacility to ProduceTroop carry an empty product
    /// (Fields [0, 0]; the production window picks it), CreateMission / CreateMoveMission the "none" mission type.
    /// </summary>
    public static Command Build(CommandType id, Owner side, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection, GameObject? target = null, string? text = null)
    {
        bool system = id is CommandType.Assault or CommandType.CreateFleet or (>= CommandType.Bombard and <= CommandType.BombardDestroy);
        return new Command(id, side)
        {
            Targets = [.. selection.Select(s => (s.Object, s.Mode))],
            ArgRef = system && target is null ? SystemOf(selection[0].Object) : target,
            Fields = id switch { >= CommandType.ProduceFacility and <= CommandType.ProduceTroop => [0, 0], CommandType.CreateMission or CommandType.CreateMoveMission => [0, 0, 0], _ => [] },
            Text = text,
        };
    }

    /// <summary>
    /// Menu rows: the intersected list, enabled by validation, then the Encyclopedia row (enabled iff one object is
    /// selected) and the Status row (one object, not a system; not a fleet: greyed in the original). The applicability test
    /// and the busy flag are not known yet: every row is kept, never busy. Checked: ClearCommand to MakeGeneral = the character's command kind;
    /// SetReserved is not shown checked (Inferred: the menu's check state for Reserved is not read).
    /// </summary>
    public static List<CommandRow> Rows(GameState game, Owner side, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection)
    {
        var rows = Intersect(selection).Select(id => new CommandRow(id, Enabled(game, side, id, selection), Checked(id, selection))).ToList();
        rows.Add(new(Encyclopedia, selection.Count == 1, false));
        rows.Add(new(Status, selection.Count == 1 && selection[0].Object is not (StarSystem or Fleet), false));
        return rows;
    }

    static bool Checked(CommandType id, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection) =>
        id is >= CommandType.ClearCommand and <= CommandType.MakeGeneral && selection.All(s => s.Object is Character c && c.CommandKind == id - CommandType.ClearCommand);

    /// <summary>
    /// Enabled = the command's validation passes. Inferred (the enable slot is not read): a command whose argument is picked
    /// after the menu passes when validation fails only for that missing argument: (NoDestination) no destination for Move / MoveVariant /
    /// SetProductionTarget, (NoTargetLocation) for Move / MoveVariant of characters and special forces (their CreateMoveMission sub-command's missing ArgRef), and
    /// (NoMissionType) the "none" mission type for CreateMission, which also needs a mission type for the selected agents
    /// (<see cref="MissionEligibility.TypesForTeam"/>).
    /// </summary>
    public static bool Enabled(GameState game, Owner side, CommandType id, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection)
    {
        if (selection.Count == 0) return false;
        var result = Orders.Validate(game, Build(id, side, selection));
        if (result.Success) return true;
        return id switch
        {
            CommandType.Move or CommandType.MoveVariant => result == Refusal.NoDestination || result == Refusal.NoTargetLocation,
            CommandType.SetProductionTarget => result == Refusal.NoDestination,
            CommandType.CreateMission => result == Refusal.NoMissionType && MissionEligibility.TypesForTeam(game, side, Agents(selection, side), []).Count > 0,
            _ => false,
        };
    }

    /// <summary>The selection's agents: agents directly, a system's own characters for the ground mode.</summary>
    static List<Agent> Agents(IReadOnlyList<(GameObject Object, GroupMode Mode)> selection, Owner side) =>
        [.. selection.SelectMany(s => s.Object is StarSystem sys
            ? sys.Contents.OfType<Agent>().Where(a => a.Owner == side && a.Existing && Family.Of(a.Type) is >= Families.MajorCharacter and < Families.SpecialForce)
            : s.Object is Agent a ? [a] : [])];

    /// <summary>
    /// Confirmation dialog first: the command is validated first, so a refused one never asks and
    /// takes the normal refusal path. Then Scrap, MoveVariant, CancelProduction, CharacterOrder, MissionOrder always; Move when the first object's
    /// view parent is a system whose view has Blockade and the object's owner (Inferred meaning: leaving an own blockaded system).
    /// </summary>
    public static bool NeedsConfirm(GameState game, Owner side, Command c, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection)
    {
        if (c.Type is not (CommandType.Scrap or CommandType.Move or CommandType.MoveVariant or CommandType.CancelProduction or CommandType.CharacterOrder or CommandType.MissionOrder) || !Orders.Validate(game, c).Success) return false;
        if (c.Type != CommandType.Move) return true;
        if (selection.Count == 0) return false;
        var o = selection[0].Object;
        if (o.Container is not StarSystem s) return false; // own objects: the live view parent is the real one
        var v = Knowledge.See(game, s, side);
        return v.Blockade && v.Owner == o.Owner;
    }
}
