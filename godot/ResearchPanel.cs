using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

/// <summary>
/// Research panel (read-only): the side's three research lines (ship / troop / facility, by mission kind k) with
/// points, current ResearchOrder, the points one 0x381 accrual adds (non-suspended member facilities), the next item and its cost
/// (difficulty * CostScale div 100), what the next level unlocks, the exhausted flag and what is buildable now
/// (Research.Buildable); below, the side's running research missions. Research is driven by the sim (accrual timer, missions);
/// no order here. Keys: Esc close. Own presentation (no original assets).
/// </summary>
public partial class ResearchPanel : Control
{
    static readonly (int K, string Name, string Facility)[] Lines = [(0, "SHIP", "shipyards"), (2, "TROOP", "training facilities"), (1, "FACILITY", "construction yards")];

    readonly Label _text = new();
    GameState? _game;
    Owner _side;
    Func<SdCommon, string> _name = c => $"#{c.StringId}";

    public override void _Ready()
    {
        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.8f), Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        _text.Position = new Vector2(4, 2);
        _text.Size = Size - new Vector2(8, 4);
        _text.AddThemeFontSizeOverride("font_size", 8);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_text);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public void Open(GameState game, Owner side, Func<SdCommon, string> name)
    {
        (_game, _side, _name) = (game, side, name);
        Visible = true;
        Refresh();
    }

    public bool HandleKey(Key key)
    {
        if (!Visible || key != Key.Escape) return false;
        Visible = false;
        return true;
    }

    public void Refresh()
    {
        if (!Visible || _game is not { } game) return;
        var side = game.Sides[_side];
        var sums = Research.AccrualSums(game, _side);
        var lines = new List<string> { $"RESEARCH  {_side}   day {game.Day}   cost scale {side.CostScale}", "" };
        foreach (var (k, title, facilities) in Lines)
        {
            uint family = Research.FacilityFamily(k);
            string accrual = sums is null ? "accrual blocked (a system lacks a manager)" : $"+{sums[k]} per accrual ({facilities})";
            lines.Add($"{title}  points {side.ResearchPoints[k]}   level {side.ResearchOrder[k]}   {accrual}");
            if (Research.Next(game, side, k) is { } next)
            {
                int order = (int)next.U32At(40), cost = Research.Cost(side, next);
                var unlocks = Research.Candidates(game, _side, family).Where(r => r.U32At(40) == order).Select(r => _name(r.Common));
                lines.Add($"  next level {order}: cost {cost} ({Math.Max(0, cost - side.ResearchPoints[k])} to go) unlocks {string.Join(", ", unlocks)}");
            }
            else lines.Add(side.ResearchExhausted[k] ? "  exhausted: every item researched" : "  nothing left to research");
            lines.Add($"  buildable: {string.Join(", ", Research.Buildable(game, _side, family).Select(r => _name(r.Common)))}");
            lines.Add("");
        }
        lines.Add("RESEARCH MISSIONS");
        var missions = game.Missions.Where(m => m.Owner == _side && m.Kind == 0x53 && !m.Destroyed).ToList();
        if (missions.Count == 0) lines.Add("  none");
        foreach (var m in missions)
            lines.Add($"  {_name(m.Record.Common)}: {string.Join(", ", m.Members.Select(a => a is Character c ? _name(c.Definition.Common) : game.Definitions.Resolve(a.Type) is { } r ? _name(r.Common) : $"{a.Type:X8}"))}   phase {m.Phase}");
        lines.Add("");
        lines.Add("read-only: points come from the accrual timer and research missions   Esc close");
        _text.Text = string.Join("\n", lines);
        _text.Size = Size - new Vector2(8, 4);
    }
}
