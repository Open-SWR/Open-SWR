using System;
using System.Collections.Generic;
using Godot;
using OpenSWR.Sim;

/// <summary>
/// In-game confirmation dialog (main-screen message, dialog kind) for the commands that ask for confirmation:
/// background 11125 (A) / 11126 (E) 424 x 331 centred in the map rect, the message picture (400 x 200, STRATEGY, by
/// command and player side, <see cref="Picture"/>) at (12, 30), read-only text box (24, 242) 322 x 70 in font 4 white, Yes button at
/// (355, 244) 51 x 35 (10926 / 10927) and No button at (355, 281) (10929 / 10930), tooltips TEXTSTRA; no title (the
/// message title is never set). Not modal: a window of the window manager (focus, raise, Alt+W; Inferred: Alt+W drops the command);
/// Enter = Yes, Esc = No only while it has the focus. Yes submits the stored command, then closes; No drops it. Silent.
/// Dialogs stack: every confirmation opens its own, on top with the focus, holding its own command; Yes / No close only
/// that dialog (openswr deviation: the original closes the first window with the dialog window id, possibly the other dialog). Alt+W, load,
/// restart, exit and game end close them and drop their commands silently; not saved. Each gets its own window-manager id
/// dialog kind, with the dialog's serial number as index.
/// </summary>
public partial class ConfirmWindow : GameWindow
{
    readonly Action _yes;
    readonly ushort _picture;

    static int _serial;

    ConfirmWindow(MainScreen screen, WindowAddress id, ushort picture, string text, Action yes) : base(screen, id, new Vector2(424, 331))
    {
        (_yes, _picture) = (yes, picture);
        Position = (screen.MapRect.Position + (screen.MapRect.Size - Size) / 2).Floor();
        AddChild(new TextBox(4) { Text = text, Position = new Vector2(24, 242), Size = new Vector2(322, 70) });
        foreach (var (y, up, down, tip, act) in new (int, int, int, int, Action)[] { (244, StrategyBitmaps.YesUp, StrategyBitmaps.YesDown, StrategyTexts.Yes, Yes), (281, StrategyBitmaps.NoUp, StrategyBitmaps.NoDown, StrategyTexts.No, No) })
        {
            var b = new FrontButton { Up = Assets.Strategy((ushort)up), Down = Assets.Strategy((ushort)down), Command = act, TooltipText = Assets.Tip(Assets.Stra(tip)) };
            b.Place(355, y, 51, 35);
            AddChild(b);
        }
    }

    protected override bool Framed => false;
    protected override string Title => "";

    /// <summary>Opens the dialog for command <paramref name="id"/> on <paramref name="selection"/>; <paramref name="yes"/> submits it.</summary>
    public static void Show(MainScreen screen, int id, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection, string text, Action yes)
    {
        var window = new WindowAddress(WindowKind.Confirm, ++_serial);
        screen.Windows.Open(window, () => new ConfirmWindow(screen, window, Picture(screen.Game!, screen.Side, id, selection), text, yes));
    }

    /// <summary>
    /// The message picture (A = side 1): scrap 1032 / 1033, retire 1034 / 1035, abort 1036 / 1037, stop
    /// construction 1038 / 1039; moves: the first moved object not a fleet, its direct parent a system whose player view has Blockade
    /// Gives 1031 / 1030 (the reversed pair), else 1018 / 1019.
    /// </summary>
    public static ushort Picture(GameState game, OpenSWR.Sim.Owner side, int id, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection)
    {
        bool a = side == OpenSWR.Sim.Owner.Alliance;
        if (id is (int)CommandType.Move or (int)CommandType.MoveVariant) return Blockaded(game, side, selection) ? (ushort)(a ? StrategyBitmaps.BlockadeMovePictureAlliance : StrategyBitmaps.BlockadeMovePictureEmpire) : (ushort)(a ? StrategyBitmaps.MovePictureAlliance : StrategyBitmaps.MovePictureEmpire);
        int first = id switch { (int)CommandType.Scrap => StrategyBitmaps.ScrapPictureAlliance, (int)CommandType.CharacterOrder => StrategyBitmaps.RetirePictureAlliance, (int)CommandType.MissionOrder => StrategyBitmaps.AbortMissionPictureAlliance, _ => StrategyBitmaps.StopConstructionPictureAlliance };
        return (ushort)(a ? first : first + 1);
    }

    /// <summary>The blockade row: a moved fleet never takes it.</summary>
    public static bool Blockaded(GameState game, OpenSWR.Sim.Owner side, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection) =>
        selection.Count > 0 && selection[0].Object is not Fleet && selection[0].Object.Container is StarSystem s && Knowledge.See(game, s, side).Blockade;

    public override void _Draw()
    {
        var back = Assets.Strategy((ushort)(A ? StrategyBitmaps.ConfirmBackgroundAlliance : StrategyBitmaps.ConfirmBackgroundEmpire));
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        if (Assets.Strategy(_picture) is { } p) { DrawTextureRect(p, new Rect2(12, 30, 400, 200), false); DrawLog.Bitmap(this, p, new Rect2(12, 30, 400, 200)); }
    }

    void Yes()
    {
        _yes();
        Screen.Windows.Close(this);
    }

    void No() => Screen.Windows.Close(this);

    public override bool HandleKey(InputEventKey key)
    {
        if (key.Keycode is Key.Enter or Key.KpEnter) Yes();
        else if (key.Keycode == Key.Escape) No();
        else return false;
        return true;
    }
}
