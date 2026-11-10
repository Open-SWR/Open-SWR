namespace OpenSWR.Sim;

/// <summary>One object-menu row before the RCDATA lookup: command id, enabled, checked.</summary>
public readonly record struct CommandRow(int Id, bool Enabled, bool Checked);

/// <summary>
/// Object context menu commands: each object class's static command list (a per-class list),
/// the intersection over a selection, enabled = <see cref="Orders.Validate"/> of the command built with that
/// selection, checked, the confirmation set, and the commands that wait for a target (the main screen's targeting state). UI only:
/// no state change, no draw.
/// </summary>
public static class ObjectCommands
{
    public const int Encyclopedia = 0x100, Status = 0x103;

    /// <summary>Commands that go through the targeting state when no target is given (0x240 opens the mission window after it).</summary>
    public static bool NeedsTarget(int id) => id is 0x201 or 0x202 or 0x214 or 0x240;

    /// <summary>
    /// The object's command list for selection mode <paramref name="mode"/> (systems only: the selection's group mode).
    /// Ids without an RCDATA entry (0x204, 0x241, 0x268) are kept; the menu builder drops them.
    /// </summary>
    public static int[] For(GameObject o, int mode = 0)
    {
        uint f = Family.Of(o.Type);
        int[] combatUnit = [0x201, 0x202, 0x204, 0x200];
        int[] agent = [0x201, 0x202, 0x242, 0x240, 0x204, 0x241];
        return o switch
        {
            // Observed in the original: no Rename row on a fleet, so 0x203 is left out here.
            Fleet => [0x201, 0x202, 0x204, 0x220, 0x221, 0x222, 0x223, 0x234, 0x200],
            Mission => [0x250],
            ManufacturingManager m => m.Kind switch
            {
                ManufacturingKind.Construction => [0x213, 0x214, 0x216, 0x210],
                ManufacturingKind.Ship => [0x213, 0x214, 0x216, 0x215, 0x211],
                _ => [0x213, 0x214, 0x216, 0x212],
            },
            StarSystem => GroupMode.Group(mode) switch
            {
                GroupMode.Facilities => [0x200, 0x214, 0x216],
                GroupMode.Ground => [0x201, 0x202, 0x200],
                GroupMode.Fleets => [0x201, 0x202, 0x200, 0x220, 0x221, 0x222, 0x223, 0x234],
                GroupMode.Missions => [0x250],
                _ => [],
            },
            _ when f is >= 0x14 and < 0x1C => [.. combatUnit, 0x203, 0x270],         // capital ships, Death Stars
            _ when f is >= 0x1C and < 0x20 => combatUnit,                             // fighter squadrons
            _ when f is >= 0x10 and < 0x14 => [0x201, 0x202, 0x204, 0x200],           // troop regiments
            _ when f is >= 0x30 and < 0x3C => [.. agent, 0x260, 0x261, 0x262, 0x263, 0x268], // characters
            _ when f is >= 0x3C and < 0x40 => agent,                                  // special forces
            _ when f == 0x20 => [0x201, 0x202, 0x204],                                // headquarters
            _ when f is > 0x20 and < 0x30 => [0x200],                                 // other facilities, defences, Mines, Refineries
            _ => [],
        };
    }

    /// <summary>The first entry's list, intersected with every other entry's (order of the first list).</summary>
    public static List<int> Intersect(IReadOnlyList<(GameObject Object, int Mode)> selection)
    {
        if (selection.Count == 0) return [];
        var ids = For(selection[0].Object, selection[0].Mode).ToList();
        foreach (var (o, mode) in selection.Skip(1)) ids.RemoveAll(id => !For(o, mode).Contains(id));
        return ids;
    }

    /// <summary>
    /// The system containing the selection's first object (view parents walked up to [0x90, 0x98)); a system selects itself.
    /// The real location stands in for the view parents here (own objects: the same).
    /// </summary>
    public static StarSystem? SystemOf(GameObject o) => o as StarSystem ?? o.System;

    /// <summary>
    /// The command for row <paramref name="id"/> with the selection: ArgRef = <paramref name="target"/>; combat commands
    /// (0x220-0x223, 0x234) and 0x270 Create Fleet take the selection's system; 0x210-0x212 carry an empty product
    /// (Fields [0, 0]; the production window picks it), 0x240 / 0x241 the "none" mission type.
    /// </summary>
    public static Command Build(int id, Owner side, IReadOnlyList<(GameObject Object, int Mode)> selection, GameObject? target = null, string? text = null)
    {
        bool system = id is 0x234 or 0x270 or (>= 0x220 and <= 0x223);
        return new Command(id, side)
        {
            Targets = [.. selection.Select(s => (s.Object, s.Mode))],
            ArgRef = system && target is null ? SystemOf(selection[0].Object) : target,
            Fields = id switch { >= 0x210 and <= 0x212 => [0, 0], 0x240 or 0x241 => [0, 0, 0], _ => [] },
            Text = text,
        };
    }

    /// <summary>
    /// Menu rows: the intersected list, enabled by validation, then 0x100 Encyclopedia (enabled iff one object is
    /// selected) and 0x103 Status (one object, not a system; not a fleet: greyed in the original). The applicability test
    /// and the busy flag are not known yet: every row is kept, never busy. Checked: 0x260-0x263 = the character's command kind;
    /// 0x216 is not shown checked (Inferred: the menu's check state for Reserved is not read).
    /// </summary>
    public static List<CommandRow> Rows(GameState game, Owner side, IReadOnlyList<(GameObject Object, int Mode)> selection)
    {
        var rows = Intersect(selection).Select(id => new CommandRow(id, Enabled(game, side, id, selection), Checked(id, selection))).ToList();
        rows.Add(new(Encyclopedia, selection.Count == 1, false));
        rows.Add(new(Status, selection.Count == 1 && selection[0].Object is not (StarSystem or Fleet), false));
        return rows;
    }

    static bool Checked(int id, IReadOnlyList<(GameObject Object, int Mode)> selection) =>
        id is >= 0x260 and <= 0x263 && selection.All(s => s.Object is Character c && c.CommandKind == id - 0x260);

    /// <summary>
    /// Enabled = the command's validation passes. Inferred (the enable slot is not read): a command whose argument is picked
    /// after the menu passes when validation fails only for that missing argument: (1, 0x22) no destination for 0x201 / 0x202 /
    /// 0x214, (0x40, 0x20) for 0x201 / 0x202 of characters and special forces (their 0x241 sub-command's missing ArgRef), and
    /// (0x40, 0x92) the "none" mission type for 0x240, which also needs a mission type for the selected agents
    /// (<see cref="MissionEligibility.TypesForTeam"/>).
    /// </summary>
    public static bool Enabled(GameState game, Owner side, int id, IReadOnlyList<(GameObject Object, int Mode)> selection)
    {
        if (selection.Count == 0) return false;
        var result = Orders.Validate(game, Build(id, side, selection));
        if (result.Success) return true;
        return id switch
        {
            0x201 or 0x202 => result == new OrderResult(1, 0x22) || result == new OrderResult(0x40, 0x20),
            0x214 => result == new OrderResult(1, 0x22),
            0x240 => result == new OrderResult(0x40, 0x92) && MissionEligibility.TypesForTeam(game, side, Agents(selection, side), []).Count > 0,
            _ => false,
        };
    }

    /// <summary>The selection's agents: agents directly, a system's own characters for mode 8.</summary>
    static List<Agent> Agents(IReadOnlyList<(GameObject Object, int Mode)> selection, Owner side) =>
        [.. selection.SelectMany(s => s.Object is StarSystem sys
            ? sys.Contents.OfType<Agent>().Where(a => a.Owner == side && a.Existing && Family.Of(a.Type) is >= 0x30 and < 0x3C)
            : s.Object is Agent a ? [a] : [])];

    /// <summary>
    /// Confirmation dialog first: the command is validated first, so a refused one never asks and
    /// takes the normal refusal path. Then 0x200, 0x202, 0x213, 0x242, 0x250 always; 0x201 when the first object's
    /// view parent is a system whose view has Blockade and the object's owner (Inferred meaning: leaving an own blockaded system).
    /// </summary>
    public static bool NeedsConfirm(GameState game, Owner side, Command c, IReadOnlyList<(GameObject Object, int Mode)> selection)
    {
        if (c.Type is not (0x200 or 0x201 or 0x202 or 0x213 or 0x242 or 0x250) || !Orders.Validate(game, c).Success) return false;
        if (c.Type != 0x201) return true;
        if (selection.Count == 0) return false;
        var o = selection[0].Object;
        if (o.Container is not StarSystem s) return false; // own objects: the live view parent is the real one
        var v = Knowledge.See(game, s, side);
        return v.Blockade && v.Owner == o.Owner;
    }
}
