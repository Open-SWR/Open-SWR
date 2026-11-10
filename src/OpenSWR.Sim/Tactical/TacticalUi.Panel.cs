namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    /// <summary>
    /// Page switch: the empty page always (releases the lock); while locked every other switch is ignored. A switch hides the
    /// options page's withdraw child window. The previous page is shown by <see cref="ShowPreviousPage"/>.
    /// </summary>
    public void ShowPage(TacticalPage page, object? obj = null, bool fromPanel = false)
    {
        if (page != TacticalPage.Empty && Locked && !fromPanel) return;
        WithdrawShown = false;
        if (page == TacticalPage.Empty) Locked = false;
        (_previous, _previousObject) = (Page, PageObject);
        (Page, PageObject) = (page, obj);
        if (page is TacticalPage.Missions or TacticalPage.Tactics or TacticalPage.Options) Locked = true;
    }

    /// <summary>Close a page (Assign / Cancel of the missions / tactics pages, the withdraw confirmation buttons): the lock released, then the previous page and its object.</summary>
    public void ClosePage()
    {
        Locked = false;
        ShowPreviousPage();
    }

    /// <summary>Show the page shown before the current one, with its object.</summary>
    public void ShowPreviousPage() => ShowPage(_previous, _previousObject);

    /// <summary>Orders buttons of the ship / fighter pages: in command, the unit's (group's) side is the local side and the local side is not withdrawing.</summary>
    public bool OrdersEnabled(int side) => InCommand && side == _b.Local && _b.SideState(_b.Local) == TacticalSideState.Fighting;

    /// <summary>Get Immediate Result enabled: neither side is withdrawing.</summary>
    public bool ImmediateEnabled => _b.SideState(0) == TacticalSideState.Fighting && _b.SideState(1) == TacticalSideState.Fighting;

    /// <summary>
    /// Withdraw button of the options page: when the local side may not retreat, the button is reset and nothing else happens.
    /// Otherwise voice CannotJump (A) / CannotJumpEmpire (E) plays when an own capital ship cannot jump, and the withdraw confirmation page is built directly as
    /// the options page's child window (no page switch, so the lock does not apply).
    /// </summary>
    public void WithdrawPressed()
    {
        if (!_b.RetreatPossible(_b.Local)) return;
        if (Ships(_b.Local).Any(u => u.Active && !_b.CanJump(u))) Voice(TacticalVoice.CannotJump, TacticalVoice.CannotJumpEmpire);
        WithdrawShown = true;
    }

    /// <summary>The withdraw confirmation page's OK (withdraw command, which unlocks the panel) or Cancel: both hide it; the options page shows again.</summary>
    public void WithdrawAnswer(bool ok)
    {
        WithdrawShown = false;
        if (ok) Command(TacticalCommand.Withdraw);
    }

    /// <summary>The TEXTTACT string of a maneuver mission (left hook, right hook, hammer or anvil).</summary>
    static int ManeuverOrderText(TacticalMissionNumber m) => m switch
    {
        TacticalMissionNumber.LeftHook => TacticalOrderText.LeftHook,
        TacticalMissionNumber.RightHook => TacticalOrderText.RightHook,
        TacticalMissionNumber.Hammer => TacticalOrderText.Hammer,
        TacticalMissionNumber.Anvil => TacticalOrderText.Anvil,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>
    /// Orders line (ship, fighter group and task force ship pages) as parts: TEXTTACT string ids (int) and unit names (a unit). A side that is not
    /// the local side gets 56767. Otherwise the UI mission of the unit's UI group picks the string: 1 Escort 56801, 2 nothing,
    /// 3 Withdraw 56803 or Disabled 56721 at speed 0, 4 56811, 5 56805, 6 56806, 7-10 56807-56810, anything else No Orders 56719.
    /// Then the simulation group: a non-empty UI nav list gives Navigating 56718 (Disabled at speed 0), else a non-empty UI
    /// target list gives Attacking 56717, and on mission 1 the first target's name is added. Inferred: the second part replaces
    /// the first, except the escorted name, which follows it.
    /// </summary>
    public List<object> OrdersLine(int side, TacticalGroup? ui, TacticalGroup? sim, TacticalUnit? unit)
    {
        if (side != _b.Local) return [TacticalOrderText.OrdersUnavailable];
        bool stopped = unit is not null && _b.Speed(unit) == 0;
        TacticalMissionNumber m = ui?.UiMission ?? TacticalMissionNumber.None;
        List<object> line = m switch
        {
            TacticalMissionNumber.Escort => [TacticalOrderText.Escort],
            TacticalMissionNumber.Recover => [],
            TacticalMissionNumber.Withdraw => [stopped ? TacticalOrderText.Disabled : TacticalOrderText.Withdraw],
            TacticalMissionNumber.AttackFighters => [TacticalOrderText.AttackFighters],
            TacticalMissionNumber.AttackCapitalShips => [TacticalOrderText.AttackCapitalShips],
            TacticalMissionNumber.AttackDeathStar => [TacticalOrderText.AttackDeathStar],
            TacticalMissionNumber.LeftHook or TacticalMissionNumber.RightHook or TacticalMissionNumber.Hammer or TacticalMissionNumber.Anvil => [ManeuverOrderText(m)],
            _ => [TacticalOrderText.NoOrders],
        };
        if (sim is null) return line;
        if (sim.UiNav.Count > 0) return [stopped ? TacticalOrderText.Disabled : TacticalOrderText.Navigating];
        if (sim.UiTargets.Count > 0) return m == TacticalMissionNumber.Escort ? [.. line, sim.UiTargets[0]] : [TacticalOrderText.Attacking];
        return line;
    }
}
