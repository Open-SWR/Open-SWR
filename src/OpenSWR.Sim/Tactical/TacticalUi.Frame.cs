namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    void Switch(Mode? next)
    {
        if (next is null) return;
        _mode = next; // the new mode replaces and deletes the current one
        next.Enter();
    }

    /// <summary>
    /// Once per frame: the three queues in order (cycling, commands, picks), then the mode's refresh step until it returns
    /// no new mode.
    /// </summary>
    public void Frame()
    {
        Drain();
        if (_b.SceneAlive) PanelValue = (int)Math.Truncate(_b.Scene!.Charge);
        Camera.Frame();
    }

    /// <summary>The three queues drained in order (cycling, commands, picks), then the mode refresh until it returns no new mode.</summary>
    internal void Drain()
    {
        foreach (bool next in _cycle.ToList()) Switch(next ? _mode.Next() : _mode.Previous());
        _cycle.Clear();
        foreach (var id in _commands.ToList()) Dispatch(id);
        _commands.Clear();
        foreach (var (key, ctrl) in _picks.ToList()) Route(key, ctrl);
        _picks.Clear();
        for (int i = 0; i < 16 && _mode.Refresh() is { } next; i++) Switch(next);
    }

    /// <summary>
    /// Pick routing: none goes to the mode's empty-click handler (ctrl 0 only); a unit goes to the mode's unit handler when in
    /// command, else to own-ship or enemy mode; a nav point goes to the mode's nav handler when in command, else to nav-point mode.
    /// </summary>
    void Route(object? key, bool ctrl)
    {
        switch (key)
        {
            case null: if (!ctrl) Switch(_mode.Empty()); break;
            case TacticalUnit u: Switch(InCommand ? _mode.Unit(u, ctrl) : u.Side == _b.Local ? new OwnShipMode(this, u) : new EnemyMode(this, u)); break;
            case TacticalNavPoint n when !n.Hidden: Switch(InCommand ? _mode.Nav(n, ctrl) : new NavMode(this, n)); break;
        }
    }

    /// <summary>Command dispatcher.</summary>
    void Dispatch(TacticalCommand id)
    {
        int i = Array.IndexOf(TaskForceCommands, id), c = Array.IndexOf(FighterGroupCommands, id), nav = Array.IndexOf(NavListCommands, id);
        if (i >= 0)
        {
            if (TaskForces[i] is not null) Switch(_mode is TaskForceMode t && t.Slot == i ? new IdleMode(this) : new TaskForceMode(this, i));
            else Switch(_mode.EmptyTaskForce(i));
            return;
        }
        if (c >= 0)
        {
            if (Fighters[c] is not null) Switch(_mode is FighterMode f && f.Colour == c ? new IdleMode(this) : new FighterMode(this, c));
            return;
        }
        if (nav >= 0)
        {
            int list = 3 - nav; // the four buttons toggle lists 3 to 0
            NavShown[list] = !NavShown[list];
            foreach (var p in _b.NavPoints.Where(p => p.Set == list + 1)) p.Hidden = !NavShown[list];
            return;
        }
        switch (id)
        {
            case TacticalCommand.Superlaser:
                if (_mode is SuperlaserMode) Switch(new IdleMode(this));
                else if (InCommand) Switch(_mode.Superlaser());
                break;
            case TacticalCommand.HighlightAlliance: Highlight[0] = !Highlight[0]; break;
            case TacticalCommand.HighlightEmpire: Highlight[1] = !Highlight[1]; break;
            case TacticalCommand.Space: if (InCommand) Switch(_mode.Space()); break;
            case TacticalCommand.Withdraw:
                if ((InCommand || _b.Computer[_b.Local]) && _b.RetreatPossible(_b.Local))
                {
                    Locked = false;
                    DeathStarPanel = false;
                    InCommand = false;
                    _b.Withdraw(_b.Local);
                }
                break;
            case TacticalCommand.ComputerControl:
                bool computer = !_b.Computer[_b.Local];
                _b.SetComputer(_b.Local, computer); // switching to computer control re-arms the rebuild
                InCommand = !computer;
                break;
            case TacticalCommand.Abort:
                if ((InCommand || _b.Computer[_b.Local]) && _b.SideState(1 - _b.Local) == TacticalSideState.Fighting) _b.Abort(); // single-player world
                break;
        }
    }
}
