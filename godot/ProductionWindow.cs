using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

/// <summary>
/// Production window "Build Selection" (210 x 261, background 10800, centred in the map rect,
/// draggable by the title strip): chooses product and count for a pending production command whose managers are fixed (the
/// destination is the managers' TargetKey). Product drop-down over `Research.Buildable` of the first manager's owner and
/// family; per-class last product (<see cref="MainScreen.LastProduct"/>, not saved); count 1...255 (field, +/- with 500 ms repeat,
/// numpad + / -); estimate (<see cref="Economy.ProductionEstimate"/>): "n/a" in the four boxes and Done greyed when the
/// validation refuses; Done / Enter: validate + submit, window closes; Cancel / Close / Esc: nothing sent. Not opened when
/// nothing is buildable. The product name's cell top at the picture height (50) while the picture is centred both ways (rows 6 ... 55):
/// the name overlaps the picture's bottom 6 rows, as in the original.
/// </summary>
public partial class ProductionWindow : GameWindow
{
    public const WindowKind WindowId = WindowKind.Production;
    readonly Command _command;
    readonly List<SdRecord> _products;
    DropDownBox _box = null!;
    NameField _field = null!;
    FrontButton _done = null!;
    ProductionEstimate _estimate;
    int _count = 1;

    ProductionWindow(MainScreen screen, Command command, List<SdRecord> products) : base(screen, WindowId, new Vector2(210, 261))
    {
        (_command, _products) = (command, products);
        var r = screen.MapRect;
        Position = (r.Position + (r.Size - Size) / 2).Floor();
    }

    /// <summary>The window for a pending Build command, or null when nothing is buildable.</summary>
    public static ProductionWindow? Create(MainScreen screen, Command command)
    {
        if (command.Targets.Select(t => t.Target).OfType<ManufacturingManager>().FirstOrDefault() is not { } m) return null;
        uint family = m.Kind switch { ManufacturingKind.Ship => Families.Shipyard, ManufacturingKind.Construction => Families.ConstructionYard, _ => Families.TrainingFacility };
        var products = Research.Buildable(screen.Game!, m.Owner, family);
        return products.Count == 0 ? null : new ProductionWindow(screen, command, products);
    }

    protected override string Title => StrategyText.BuildSelection;
    protected override bool Draggable => true;

    /// <summary>Last-product slot of a product:0 facilities, 1 ships and fighters, 2 troops and special forces.</summary>
    static int Slot(uint product) => Family.Of(product) switch { >= Families.Headquarters and < Families.MajorCharacter => 0, >= Families.CapitalShip and < Families.Headquarters => 1, _ => 2 };

    static int PictureId(SdRecord r) => ModArt.PictureNumber(r.Common.StringId); // Picture id is the string id (Inferred), as in ScreenViews.PictureId

    public override void _Ready()
    {
        base._Ready();
        _box = new DropDownBox { Position = new Vector2(6, 22), Size = new Vector2(195, 63), Count = _products.Count, DrawItem = DrawProduct, Changed = Estimate,
            // Centred, unclipped, font 10, white; items with own colours grey / white, text offset y = picture height
            ItemText = i => (Screen.DisplayName(_products[i].Common), Picture(_products[i])?.Size.Y ?? 0), Format = new TextFormat { Center = true, NoClip = true }, Font = 10,
            Colour = Colors.White, RowColours = (Color.Color8(128, 128, 128), Colors.White),
            RowHeight = 70, VisibleRows = 2 }; // pop-up 195 x 144, rows 70 apart (each 63-px item leaves a 7-px gap)
        AddChild(_box);
        if (Screen.LastProduct[Slot(_products[0].Common.Ref)] is { } last && _products.FindIndex(p => PictureId(p) == PictureId(last) && (PictureId(p) != 0 || p.Common.Ref == last.Common.Ref)) is var i and >= 0)
            _box.Select(i);
        Add(StrategyBitmaps.DropDownUp, StrategyBitmaps.DropDownDown, 79, 90, StrategyTexts.ItemsToBuild, () => _box.Open());
        Add(StrategyBitmaps.EncyclopediaUp, StrategyBitmaps.EncyclopediaDown, 5, 224, StrategyTexts.ProductionEncyclopedia, () => Screen.OpenEncyclopedia(null, _products[_box.Selected].Common.Ref));
        _done = Add(StrategyBitmaps.DoneUp, StrategyBitmaps.DoneDown, 73, 224, StrategyTexts.ProductionDone, Done);
        _done.DisabledImage = StrategyArt.DoneDisabled;
        Add(StrategyBitmaps.CancelUp, StrategyBitmaps.CancelDown, 141, 224, StrategyTexts.ProductionCancel, () => Screen.Windows.Close(this));
        AddChild(new RepeatButton { Up = StrategyArt.IncreaseUp, Down = StrategyArt.IncreaseDown, Command = () => Step(1), TooltipText = Assets.Tip(StrategyText.Increase), Position = new Vector2(189, 196), Size = Assets.BitmapSize(StrategyBitmaps.IncreaseUp, "STRATEGY.DLL") });
        AddChild(new RepeatButton { Up = StrategyArt.DecreaseUp, Down = StrategyArt.DecreaseDown, Command = () => Step(-1), TooltipText = Assets.Tip(StrategyText.Decrease), Position = new Vector2(189, 205), Size = Assets.BitmapSize(StrategyBitmaps.DecreaseUp, "STRATEGY.DLL") });
        // numeric edit field: font 4, white, at most 3 digits (a self-drawn field: a LineEdit grows past 45 x 17 over the +/- buttons)
        _field = new NameField(new Vector2(141, 196), new Vector2(45, 17), 4, 3)
        {
            Shadow = false, Accept = char.IsAsciiDigit, TooltipText = Assets.Tip(StrategyText.EditNumber),
            TextChanged = _ => { _count = Parse(); Estimate(); },   // text changed
            Submitted = Done,                                       // submitted
            OtherKey = k =>
            {
                if (k.Keycode is Key.KpAdd or Key.KpSubtract) Step(k.Keycode == Key.KpAdd ? 1 : -1);
                else if (k.Keycode == Key.Escape) Screen.Windows.Close(this); // Esc: closes, nothing sent
                else return false;
                return true;
            },
        };
        _field.SetText("1");
        AddChild(_field);
        _field.CallDeferred(Control.MethodName.GrabFocus);
        Estimate();
    }

    FrontButton Add(int up, int down, int x, int y, int tip, Action command)
    {
        var b = new FrontButton { Up = Assets.Strategy((ushort)up), Down = Assets.Strategy((ushort)down), Command = command, TooltipText = Assets.Tip(Assets.Stra(tip)) };
        var size = Assets.BitmapSize((ushort)up, "STRATEGY.DLL");
        b.Place(x, y, size.X, size.Y);
        AddChild(b);
        return b;
    }

    int Parse() => int.TryParse(_field.Text, out int n) ? Math.Clamp(n, 1, 255) : 1;

    /// <summary>+/- (and numpad): count +/- 1 within 1...255, field rewritten, estimate refreshed.</summary>
    void Step(int d)
    {
        _count = Math.Clamp(Parse() + d, 1, 255);
        _field.SetText(_count.ToString());
        Estimate();
    }

    Command Pending => new(_command.Type, _command.Side) { Targets = _command.Targets, Fields = [_products[_box.Selected].Common.Ref, (uint)_count] };

    void Estimate()
    {
        _estimate = Economy.ProductionEstimate(Screen.Game!, Pending);
        _done.Disabled = !_estimate.Valid;
        QueueRedraw();
    }

    /// <summary>Done / Enter: count clamped, product into its class slot, validate + submit (no confirmation); the window closes either way.</summary>
    void Done()
    {
        _count = Parse();
        var product = _products[_box.Selected];
        Screen.LastProduct[Slot(product.Common.Ref)] = product;
        Screen.Windows.Close(this);
        Screen.Submit(Pending);
    }

    public override bool HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter: Done(); return true;
            case Key.KpAdd: Step(1); return true;
            case Key.KpSubtract: Step(-1); return true;
        }
        return false;
    }

    /// <summary>Headless demos: product index, count, then Done.</summary>
    public void Demo(int product, int count, bool done)
    {
        _box.Select(product);
        _field.SetText(count.ToString());
        _count = Parse();
        Estimate();
        if (done) Done();
    }

    public void DemoOpenList() => _box.Open();

    (Texture2D Texture, Vector2 Size)? Picture(SdRecord p) => ModArt.Picture(Game.Definitions, p.Common.Ref, 0) ?? ModArt.Install(true, PictureId(p));

    void DrawProduct(CanvasItem c, int i, Rect2 r)
    {
        if (Picture(_products[i]) is var (t, size)) CellList.Centred(c, t, size, r);
    }

    static string Clamp(int v) => Math.Min(v, 9999).ToString();

    public override void _Draw()
    {
        var back = StrategyArt.ProductionBackground;
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        var strip = (ushort)(A ? StrategyBitmaps.ProductionTitleStripAlliance : StrategyBitmaps.ProductionTitleStripEmpire);
        var (stripImage, stripAt) = (Assets.Strategy(strip), new Rect2(new Vector2(2, 2), Assets.BitmapSize(strip, "STRATEGY.DLL")));
        DrawTextureRect(stripImage, stripAt, false);
        DrawLog.Bitmap(this, stripImage, stripAt);
        Assets.DrawText(this, new Rect2(2, 2, 202, 14), Title, 5, Colors.Black, new TextFormat { Center = true, SingleLine = true });
        var e = _estimate;
        string na = StrategyText.NotAvailable, days = StrategyText.Days;
        // value boxes = text items (one line, centred or right-aligned): unclipped; title and labels = direct window texts: clipped at
        // their rects, never ellipsized
        Assets.DrawText(this, new Rect2(36, 110, 64, 23), e.Valid ? Clamp(e.Refined) : na, 4, Colors.White, new TextFormat { Center = true, VCenter = true, SingleLine = true, NoClip = true });
        Assets.DrawText(this, new Rect2(138, 110, 64, 23), e.Valid ? Clamp(e.Maintenance) : na, 4, Colors.White, new TextFormat { Center = true, VCenter = true, SingleLine = true, NoClip = true });
        var label = Color.Color8(255, 251, 240);
        Assets.DrawText(this, new Rect2(10, 145, 125, 15), StrategyText.BestTimeToCompletion, 10, label, TextFormat.Row);
        Assets.DrawText(this, new Rect2(140, 145, 60, 15), e.Valid ? Clamp(e.BuildDays) + days : na, 10, Colors.White, new TextFormat { Right = true, VCenter = true, SingleLine = true, NoClip = true });
        Assets.DrawText(this, new Rect2(10, 165, 125, 15), StrategyText.BestTimeToDeployment, 10, label, TextFormat.Row);
        Assets.DrawText(this, new Rect2(140, 165, 60, 15), e.Valid ? Clamp(e.DeliveryDays) + days : na, 10, Colors.White, new TextFormat { Right = true, VCenter = true, SingleLine = true, NoClip = true });
        Assets.DrawText(this, new Rect2(20, 196, 118, 15), StrategyText.NumberToBuild, 4, label, new TextFormat { Right = true, SingleLine = true });
    }
}

/// <summary>Bitmap button with auto-repeat: the command on press, then every 500 ms while held (+/-).</summary>
public partial class RepeatButton : FrontButton
{
    Timer? _timer;

    protected override void OnPress()
    {
        Command?.Invoke();
        if (_timer is null)
        {
            _timer = new Timer { WaitTime = 0.5 };
            _timer.Timeout += () => { if (Held && Inside) Command?.Invoke(); };
            AddChild(_timer);
        }
        _timer.Start();
    }

    protected override void OnRelease(bool inside) => _timer?.Stop();
}
