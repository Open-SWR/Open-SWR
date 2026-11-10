using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;

/// <summary>
/// Tactical battle screen ("take command"). The frame is TACTICAL 1000 with the 3-D view at (17, 30), 439 x 437. The HUD is
/// <see cref="TacticalHud"/> and <see cref="TacticalPanel"/>. Units are drawn as their models. Fighters are decals. Positions are
/// interpolated between the sim's 50 ms frames. The UI controller of the sim (<see cref="TacticalUi"/>) handles modes, order tables,
/// pages, boxes and cameras. Mouse picks go to it: left uses 5 points, right uses 13 (Inferred: a ray against each shown unit's model
/// box, a fighter's decal or a nav marker, nearest hit wins). Buttons and keys go to it too. Esc and P do nothing. Selection boxes and
/// nav markers are drawn here, with the orbit and follow cameras. Presentation and sound are in TacticalScreen.Presentation.cs. The
/// jump-in intro has no turns, the local side observes, and Pause is disabled. While the Death Star targets, the cursor is 0x2A. The
/// run's outcome movie plays with the battle paused, after every sound has stopped.
/// <see cref="Finished"/> runs when the battle is done.
/// </summary>
public partial class TacticalScreen : Control
{
    internal static readonly Rect2 View = new(17, 30, 439, 437);
    internal const string Dll = "TACTICAL.DLL";
    internal readonly TacticalBattle Battle;
    internal TacticalUi Ui => Battle.Ui;
    internal readonly OpenSWR.Data.Dat.GameDefinitions Definitions;
    readonly SubViewport _viewport = new() { Size = new Vector2I(878, 874), OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, TransparentBg = true };
    readonly Camera3D _camera = new() { KeepAspect = Camera3D.KeepAspectEnum.Width };
    readonly Dictionary<TacticalUnit, Node3D> _nodes = [];
    readonly Dictionary<TacticalNavPoint, MeshInstance3D> _navNodes = [];
    readonly ImmediateMesh _boxes = new();

    // the eight corners of a unit cube as signs per axis, and its twelve edges as corner pairs
    static readonly (int X, int Y, int Z)[] CubeCorners =
        [(-1, -1, -1), (1, -1, -1), (-1, 1, -1), (1, 1, -1), (-1, -1, 1), (1, -1, 1), (-1, 1, 1), (1, 1, 1)];
    static readonly (int, int)[] CubeEdges = [(0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)];
    /// <summary>Nav marker size (a tetrahedron per point). The original's shared mesh was not read, so this size is an openswr design.</summary>
    const float NavSize = 0.8f;
    Node3D _world = null!;
    int _shotsSeen, _superSeen;
    MoviePlayer? _movie;
    TacticalHud _hud = null!;
    TacticalPanel _panel = null!;
    (int Op, double Next)? _held;

    public Action? Finished { get; set; }
    /// <summary>Page 0x1B Game Options (0xDD04) asks for the options screen (host state 0xB).</summary>
    public Action? OptionsRequested { get; set; }

    public TacticalScreen(TacticalBattle battle, OpenSWR.Data.Dat.GameDefinitions definitions)
    {
        (Battle, Definitions) = (battle, definitions);
        (_starfield, _planet, _pyro, _high, _holocube) = (FrontSettings.Display(DisplayOption.Starfield), FrontSettings.Display(DisplayOption.Planet),
            FrontSettings.Display(DisplayOption.Pyrotechnics), FrontSettings.Display(DisplayOption.HighDetailModels), FrontSettings.Display(DisplayOption.Holocube));
        _sounds = new TacticalSounds(Battle.Local, new Sink(this), n => _present.Rand(n));
        Battle.StartIntro(); // world state 2: the jump-in intro
        Size = new Vector2(640, 480);
        MouseFilter = MouseFilterEnum.Stop;
        foreach (var u in Battle.Units)
        {
            var node = u.Fighter ? Fighter(u) : TacticalModels.Ship(Definitions, u.Object.Type);
            _nodes[u] = node;
            if (!u.Fighter && Box(node) is { } box) Battle.SetBox(u, box.X, box.Y, box.Z); // the sim reads the box before the first frame
        }
    }

    /// <summary>The model box the sim reads: the _MEDIUM mesh (child "1") of the .x LODs, else the extent over the node's meshes (glTF, placeholder; Inferred).</summary>
    static Vector3? Box(Node3D node)
    {
        if (node.GetChildCount() == 3 && node.GetChild(1) is MeshInstance3D { Mesh: { } medium }) return medium.GetAabb().Size;
        var meshes = node.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => m.Mesh is not null).ToList();
        if (node is MeshInstance3D { Mesh: not null } self) meshes.Add(self);
        return meshes.Count == 0 ? null : meshes.Select(m => m.Mesh.GetAabb()).Aggregate((a, b) => a.Merge(b)).Size;
    }

    public override void _Ready()
    {
        AddChild(_viewport);
        var world = _world = new Node3D();
        _viewport.AddChild(world);
        Background(world);
        world.AddChild(_camera);
        foreach (var node in _nodes.Values) world.AddChild(node);
        var tetra = Tetrahedron();
        foreach (var p in Battle.NavPoints)
        {
            var m = new MeshInstance3D { Mesh = tetra, Visible = false, MaterialOverride = Flat(Colors.White) };
            world.AddChild(m);
            m.Position = G(p.Position);
            _navNodes[p] = m;
        }
        world.AddChild(new MeshInstance3D { Mesh = _boxes, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true } });
        AddChild(Backdrop());
        var view = new TextureRect { Texture = _viewport.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(view);
        (view.Position, view.Size) = (View.Position, View.Size); // drawn at the window's resolution (the viewport has twice the logical size)
        AddChild(_overlay);
        _overlay.Draw += DrawOverlay;
        AddChild(_hud = new TacticalHud(this));
        AddChild(_panel = new TacticalPanel(this));
        UpdateCamera();
        Place();
    }

    public override void _Draw()
    {
        var frame = Assets.Bitmap(1000, Dll);
        DrawTextureRect(frame, new Rect2(0, 0, 640, 480), false);
        DrawLog.Bitmap(this, frame, new Rect2(0, 0, 640, 480));
    }

    static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

    /// <summary>A tetrahedron of edge about <see cref="NavSize"/>, one per nav point. The size is an openswr design.</summary>
    static ArrayMesh Tetrahedron()
    {
        float s = NavSize / 2;
        Vector3[] v = [new(s, s, s), new(-s, -s, s), new(-s, s, -s), new(s, -s, -s)];
        int[][] faces = [[0, 1, 2], [0, 3, 1], [0, 2, 3], [1, 3, 2]];
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var f in faces) foreach (int i in f) st.AddVertex(v[i]);
        var mesh = new ArrayMesh();
        st.Commit(mesh);
        return mesh;
    }

    /// <summary>
    /// A fighter squadron: camera-facing decals of 0.3 x 0.3 world units. The near image is the 128 x 128 image (base), the mid image is
    /// the 32 x 32 image (base + 5), and the far image is a dot. The far image (4200 + base mod 10) cannot be decoded, so the dot is a quad
    /// in palette colour 249, which is their run value (Inferred). A class without an image gets the dot only.
    /// </summary>
    Node3D Fighter(TacticalUnit u)
    {
        var node = new Node3D();
        int? image = TacticalModels.FighterImage(Definitions, u.Object.Type, Battle.Local);
        foreach (int k in new[] { 0, 5 })
        {
            var tex = image is { } i ? TacticalModels.Image(i + k) : null;
            var sprite = new Sprite3D { Name = k == 0 ? "near" : "mid", Texture = tex, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, Visible = false };
            if (tex is not null) sprite.PixelSize = 0.3f / tex.GetWidth();
            node.AddChild(sprite);
        }
        var dot = new StandardMaterial3D { AlbedoColor = TacticalModels.PaletteColour(249), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled };
        node.AddChild(new MeshInstance3D { Name = "dot", Mesh = new QuadMesh { Size = new Vector2(0.3f, 0.3f), Material = dot } });
        return node;
    }

    /// <summary>Fighter level: s = tan(f * 0.5) * depth; high detail: s &lt; 5: 128 x 128, &lt; 10: 32 x 32, else dot; off: s &lt; 10: 32 x 32, else dot.</summary>
    static void FighterLod(Node3D node, float depth, float f, bool high)
    {
        double size = Math.Tan(f * 0.5) * depth;
        int level = high && size < 5 ? 0 : size < 10 ? 1 : 2;
        var (near, mid) = (node.GetNode<Sprite3D>("near"), node.GetNode<Sprite3D>("mid"));
        near.Visible = level == 0 && near.Texture is not null;
        mid.Visible = level == 1 && mid.Texture is not null;
        node.GetNode<Node3D>("dot").Visible = !near.Visible && !mid.Visible;
    }

    static Vector3 G(V3 p) => TacticalModels.ToGodot(p.X, p.Y, p.Z);

    /// <summary>Unit frame axes in the world: +x = normalise(-f.z, 0, f.x), +y up, +z = -f. The model's nose points to -z.</summary>
    static (V3 X, V3 Y, V3 Z) Frame(TacticalUnit u)
    {
        var f = u.Facing.IsZero ? new V3(0, 0, 1) : u.Facing.Normalised();
        return (new V3(-f.Z, 0, f.X).Normalised(), new V3(0, 1, 0), -f);
    }

    /// <summary>Nodes from the units (hidden while docked, landed, jumped, withdrawn or dead: a dead capital ship stays until its death effect hides it), LODs, nav markers and boxes.</summary>
    void Place()
    {
        var (_, _, field) = Ui.Camera.Pose();
        bool high = _high; // "Use High Detail Models", read at battle start
        foreach (var (u, node) in _nodes)
        {
            node.Visible = (u.State is not (1 or 5 or 8 or 9 or 11) || DeadShown(u)) && !u.Hidden; // a run member is hidden (state 10)
            var p = Shown(u);
            node.Position = p;
            var h = G(u.Facing);
            if (h.LengthSquared() > 0) node.LookAt(p - h, Math.Abs(h.Normalized().Y) > 0.999f ? Vector3.Right : Vector3.Up); // the nose is model -z in D3D, which is +Z here
            float depth = Math.Abs((p - _camera.GlobalPosition).Dot(-_camera.GlobalBasis.Z));
            if (u.Fighter) FighterLod(node, depth, (float)field, high);
            else TacticalModels.Lod(node, depth, (float)field, high);
        }
        if (_dsFrame is not null) _dsFrame.Visible = Battle.SceneHull > 0 && !Battle.SceneWithdrawn; // removed at once: no explosion, no jump
        foreach (var (p, node) in _navNodes)
        {
            var style = Ui.NavStyle(p);
            node.Visible = style != TacticalNavStyle.Hidden;
            ((StandardMaterial3D)node.MaterialOverride).AlbedoColor = style switch
            {
                TacticalNavStyle.First => new Color(1, 1, 0),
                TacticalNavStyle.Other => new Color(1, 0, 1),
                _ => p.Layer switch { 0 => new Color(0, 0.54f, 1), 1 => new Color(1, 0, 0), _ => new Color(1, 1, 1) },
            };
        }
        Boxes();
    }

    /// <summary>
    /// Selection boxes: a wireframe cube scaled to the model's _MEDIUM box and centred on the unit, in its frame;
    /// yellow selected, white primary, green / red marked (own / enemy); rebuilt every frame.
    /// </summary>
    void Boxes()
    {
        _boxes.ClearSurfaces();
        bool any = false;
        foreach (var (u, node) in _nodes)
        {
            if (!node.Visible) continue;
            var style = Ui.Box(u);
            if (style is TacticalBox.Hidden or TacticalBox.Normal) continue;
            var colour = style switch
            {
                TacticalBox.Selected => new Color(1, 1, 0),
                TacticalBox.Primary => new Color(1, 1, 1),
                _ => u.Side == Battle.Local ? new Color(0, 1, 0) : new Color(1, 0, 0),
            };
            if (!any) _boxes.SurfaceBegin(Mesh.PrimitiveType.Lines);
            any = true;
            var (ax, ay, az) = Frame(u);
            var c = node.Position;
            var b = u.Box * 0.5;
            Vector3 Corner(int i) => c + G(ax * (CubeCorners[i].X * b.X) + ay * (CubeCorners[i].Y * b.Y) + az * (CubeCorners[i].Z * b.Z));
            foreach (var (i, j) in CubeEdges)
            {
                _boxes.SurfaceSetColor(colour);
                _boxes.SurfaceAddVertex(Corner(i));
                _boxes.SurfaceSetColor(colour);
                _boxes.SurfaceAddVertex(Corner(j));
            }
        }
        if (any) _boxes.SurfaceEnd();
    }

    /// <summary>The drawn position: interpolated from the last frame step's start to the current position by the time since.</summary>
    Vector3 Drawn(TacticalUnit u) => G(u.Previous).Lerp(G(u.Position), (float)Battle.FrameFraction);

    /// <summary>The camera pose of the Sim's orbit / follow camera; f = the D3D RM field: horizontal field of view 2*atan f (Inferred); clips 1.0 / 2.5*D.</summary>
    void UpdateCamera()
    {
        var (eye, at, field) = Ui.Camera.Pose();
        Vector3 e = G(eye), a = G(at);
        if ((a - e).LengthSquared() < 1e-9f) a = e + Vector3.Forward;
        var dir = (a - e).Normalized();
        _camera.LookAtFromPosition(e, a, Math.Abs(dir.Y) > 0.999f ? Vector3.Forward : Vector3.Up);
        float near = Ui.Camera.Follow is null ? 1.0f : 0.05f; // follow camera clips: Inferred
        // a frustum of width 2*f at distance 1: Godot's perspective FOV stops at 1 deg (f ~ 0.0087), the zoom floor is f = 0.005
        _camera.SetFrustum((float)(2 * field * near), Vector2.Zero, near, (float)(2.5 * Ui.Camera.Distance));
    }

    internal string Label(OpenSWR.Sim.GameObject o) => Definitions.Resolve(o.Type) is { } r && Assets.Stra(r.Common.StringId) is { Length: > 0 } n ? n : $"{o.Type:X8}";

    internal static string Text(int id) => Assets.Text("TEXTTACT.DLL", id);


    public override void _Process(double delta)
    {
        PlayMovie();
        Ui.Frame(); // the UI queues are drained before the turn logic, so orders take the current stamp base
        Battle.Advance(delta * 1000);
        MouseDefaultCursorShape = Ui.TargetCursor ? CursorShape.Cross : CursorShape.Arrow; // cursors 0x2A / 0x2B (RT_CURSOR not decoded: Godot shapes)
        if (_held is { } h && Time.GetTicksMsec() >= h.Next)
        {
            Operate(h.Op);
            _held = (h.Op, h.Next + 100); // camera buttons repeat every 100 ms while held
        }
        UpdateCamera();
        Present(delta);
        Place();
        _hud.Refresh();
        _panel.Refresh();
        _overlay.QueueRedraw();
        if (Battle.Done) Finish();
    }

    /// <summary>A camera operation: 9 takes the current mode's subject.</summary>
    internal void Operate(int op) => Ui.Camera.Operate(op, Ui.Current.Subject);

    /// <summary>A repeating camera button or key pressed (the operation at once, then every 100 ms) / released.</summary>
    internal void Hold(int op)
    {
        Operate(op);
        _held = (op, Time.GetTicksMsec() + 100);
    }

    internal void Release() => _held = null;

    /// <summary>Pause button 0xAD22: paused: resume, else pause; both only after the intro (world state &gt; 2).</summary>
    internal void TogglePause()
    {
        if (!Battle.InIntro) Battle.Paused = !Battle.Paused;
    }

    /// <summary>
    /// The run's outcome movie (MDATA.201 success, MDATA.202 failure) over the whole screen with the world paused. Any key or a
    /// left click skips it (openswr; the original does not). A missing file ends at once. Afterwards the battle resumes (Inferred: the pause counter).
    /// </summary>
    void PlayMovie()
    {
        if (_movie is not null || Battle.Movie is not { } id) return;
        _sounds.StopAll(); // every sound stops before a movie
        _movie = new MoviePlayer
        {
            File = $"MDATA.{id}",
            Finished = () => Callable.From(() =>
            {
                _movie?.QueueFree();
                _movie = null;
                Battle.Movie = null;
                Battle.Paused = false;
            }).CallDeferred(),
        };
        AddChild(_movie);
    }

    bool _finished;
    void Finish()
    {
        if (_finished) return;
        _finished = true;
        Finished?.Invoke();
    }

    readonly Control _overlay = new() { Position = View.Position, Size = View.Size, MouseFilter = MouseFilterEnum.Ignore };

    /// <summary>
    /// Text overlays in the 3-D view, each in a
    /// view-wide rect at (0, 0): " Battle Paused" (56816) while paused, font 6, red (local Alliance) / green (local Empire), top left;
    /// " Observe Battle " (56818) while observing (Inferred: H +0 = 0), font 6, cyan, DT_RIGHT; both can show on the same line. The
    /// rect is one font line high (format 0 / 2 draw at the top of the original's 30-high rect).
    /// </summary>
    void DrawOverlay()
    {
        var line = new Rect2(0, 0, View.Size.X, Assets.LineHeight(6));
        if (Battle.Paused) Assets.DrawText(_overlay, line, Text(56816), 6, Battle.Local == 0 ? new Color(1, 0, 0) : new Color(0, 1, 0), TextFormat.TopLeft);
        if (!Ui.InCommand && !Battle.Done) Assets.DrawText(_overlay, line, Text(56818), 6, new Color(0, 1, 1), new TextFormat { Right = true });
    }

    // ---- input ----

    /// <summary>
    /// The first shown object hit at logical point <paramref name="p"/> (D3D RM viewport pick; Inferred: the nearest hit
    /// of a ray against each shown capital ship's model box in its frame, each shown fighter's decal (a sphere of 0.15) and each
    /// shown nav marker (a sphere of half the marker size)).
    /// </summary>
    object? PickAt(Vector2 p)
    {
        var at = (p - View.Position) * 2;
        var o = _camera.ProjectRayOrigin(at);
        var d = _camera.ProjectRayNormal(at);
        V3 ro = new(o.X, o.Y, -o.Z), rd = new(d.X, d.Y, -d.Z);
        object? best = null;
        double near = double.MaxValue;
        void Sphere(object key, V3 c, double r)
        {
            var oc = ro - c;
            double b = oc.Dot(rd), q = oc.Dot(oc) - r * r, disc = b * b - q;
            if (disc < 0) return;
            double t = -b - Math.Sqrt(disc);
            if (t > 0 && t < near) (best, near) = (key, t);
        }
        foreach (var (u, node) in _nodes)
        {
            if (!node.Visible) continue;
            if (u.Fighter) { Sphere(u, u.Position, 0.15); continue; }
            var (ax, ay, az) = Frame(u);
            var rel = ro - u.Position;
            double t0 = double.MinValue, t1 = double.MaxValue;
            bool hit = true;
            foreach (var (axis, half) in new[] { (ax, u.Box.X / 2), (ay, u.Box.Y / 2), (az, u.Box.Z / 2) })
            {
                double e = axis.Dot(rel), f = axis.Dot(rd);
                if (Math.Abs(f) < 1e-12) { if (Math.Abs(e) > half) hit = false; continue; }
                double ta = (-half - e) / f, tb = (half - e) / f;
                if (ta > tb) (ta, tb) = (tb, ta);
                (t0, t1) = (Math.Max(t0, ta), Math.Min(t1, tb));
            }
            if (hit && t0 <= t1 && t1 > 0 && Math.Max(t0, 0) < near) (best, near) = (u, Math.Max(t0, 0));
        }
        foreach (var n in Battle.NavPoints)
            if (!n.Hidden) Sphere(n, n.Position, NavSize / 2);
        return best;
    }

    /// <summary>Left: (x, y) then the four diagonal neighbours; right: those five, then the ring at +/- 2.</summary>
    object? Pick(Vector2 p, bool right)
    {
        (int, int)[] points = right
            ? [(0, 0), (-1, -1), (1, -1), (1, 1), (-1, 1), (-2, -2), (0, -2), (2, -2), (2, 0), (2, 2), (0, 2), (-2, 2), (-2, 0)]
            : [(0, 0), (-1, -1), (1, -1), (1, 1), (-1, 1)];
        foreach (var (dx, dy) in points)
            if (PickAt(p + new Vector2(dx, dy)) is { } hit) return hit;
        return null;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } m || !View.HasPoint(m.Position)) return;
        if (m.ButtonIndex == MouseButton.Left) Ui.LeftDown(Pick(m.Position, false), m.CtrlPressed);
        else if (m.ButtonIndex == MouseButton.Right) Ui.RightDown(Pick(m.Position, true), m.CtrlPressed);
        AcceptEvent();
    }

    /// <summary>Keys (main window): key down presses the button, key up releases it; Esc, P and letters do nothing.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k) return;
        int? op = k.Keycode switch
        {
            Key.Pageup or Key.Kp9 or Key.KpAdd => TacticalCamera.ZoomIn,
            Key.Pagedown or Key.Kp3 or Key.KpSubtract => TacticalCamera.ZoomOut,
            Key.Left or Key.Kp4 => TacticalCamera.RotateLeft,
            Key.Right or Key.Kp6 => TacticalCamera.RotateRight,
            Key.Up or Key.Kp8 => TacticalCamera.TiltUp,
            Key.Down or Key.Kp2 => TacticalCamera.TiltDown,
            _ => null,
        };
        if (op is { } repeat)
        {
            if (!k.Pressed) Release();
            else if (!k.Echo) Hold(repeat);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!k.Pressed || k.Echo) return;
        switch (k.Keycode)
        {
            case >= Key.Key1 and <= Key.Key8: Ui.PressTaskForce((int)(k.Keycode - Key.Key1)); break;
            case Key.Space: Ui.Command(0x20); break;
            case Key.Enter or Key.KpEnter or Key.KpPeriod: Operate(TacticalCamera.ResetSubject); break;
            case Key.Home or Key.Kp7: Operate(TacticalCamera.Remember); break;
            case Key.End or Key.Kp1: Operate(TacticalCamera.ResetView); break;
            case >= Key.F1 and <= Key.F12: Ui.Camera.FollowGroup((int)(k.Keycode - Key.F1), Ui); break;
            case Key.Tab when Ui.Page is 0x16 or 0x18: Ui.Cycle(!k.ShiftPressed); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Env OPENSWR_DEMO_TACTICAL, comma-separated steps: view (the opening camera) | abort (Get Immediate Result) | closing (a task
    /// force of the own ships sent at the nearest enemy, 25 s) | fire (the same until an own shot shows) | select (own-ship mode of
    /// the first ship) | tf (a numbered task force of every own ship, slot 1, targets the nearest enemy and a second one) | multi |
    /// enemy | fighter (fighter group Red) | nav (all four nav sets shown, the shown group's nav points set) | follow (F1) | close / close&lt;N&gt;
    /// (orbit around the first own ship, zoomed in 12 / N steps) | missions | maneuvers | options | withdraw (pages 0x19 / 0x1A / 0x1B / 0x1C) |
    /// highlight | pause | observe | zoom&lt;N&gt; (the mode's subject, N zoom steps) | launch (until an enemy fighter launches) | tractor (closing until a tractor hold) | retreat (the
    /// own withdraw until the first jump) | superlaser (Empire player: mode 0x11 and a right click on the first enemy ship, else
    /// the computer's; until the beam) | dsrun (Alliance player: fighter group Red on Attack Death Star until the run starts) |
    /// run (10 s) | run&lt;N&gt; (N s) | intro&lt;N&gt; (N wall ms into the jump-in intro; without an intro step the demo skips the intro) | spark / hull / explode[&lt;N&gt;] (an effect on the first
    /// enemy ship, close up, N zoom steps) | turn&lt;N&gt; / tilt&lt;N&gt; (N orbit steps left / down). Both sides as in play.
    /// </summary>
    public void Demo(string modes)
    {
        var steps = modes.Split(',').Select(s => s.Trim()).Where(s => s != "").ToArray();
        if (!steps.Any(s => s.StartsWith("intro"))) Battle.SkipIntro();
        foreach (var mode in steps) DemoStep(mode);
        Ui.Frame();
        (_soundsSeen, _voicesSeen) = (Battle.Sounds.Count, Ui.Voices.Count); // the demo's backlog stays silent
        if (IsInsideTree())
        {
            UpdateCamera();
            Place();
        }
    }

    void DemoStep(string mode)
    {
        var own = Battle.Units.Where(u => u.Side == Battle.Local && !u.Fighter && u.Active).ToList();
        var enemies = Battle.Units.Where(u => u.Side != Battle.Local && !u.Fighter && u.Active).ToList();
        void Run(int seconds, Func<bool>? stop = null)
        {
            for (int i = 0; i < seconds && !Battle.Ending && !Battle.Done && !(stop?.Invoke() ?? false); i++)
            {
                Ui.Frame();
                (_shotsSeen, _effectsSeen) = (Battle.Shots.Count, Battle.Effects.Count); // only the last second's bolts and effects are drawn
                Battle.Advance(1000);
            }
            Battle.Advance(TacticalBattle.FrameMs * 0.5); // halfway into a frame: interpolated
        }
        void Click(TacticalUnit? u, bool right, bool ctrl = false)
        {
            if (u is null) return;
            if (right) Ui.RightDown(u, ctrl);
            else Ui.LeftDown(u, ctrl);
            Ui.Frame();
        }
        switch (mode)
        {
            case "abort": Battle.Abort(); Finish(); break;
            case "select": Click(own.FirstOrDefault(), false); break;
            case "enemy": Click(enemies.FirstOrDefault(), false); break;
            case "multi":
                Click(own.FirstOrDefault(), false);
                foreach (var u in own.Skip(1)) Click(u, false, ctrl: true);
                break;
            case "tf" or "closing" or "fire":
                Click(own.FirstOrDefault(), false);
                foreach (var u in own.Skip(1)) Click(u, false, ctrl: true);
                Ui.PressTaskForce(0);
                Ui.Frame();
                Ui.Command(0x20);
                Ui.Frame();
                if (mode == "tf") Click(enemies.Skip(1).FirstOrDefault(), true, ctrl: true);
                if (mode == "closing") Run(25);
                if (mode == "fire") Run(240, () => Battle.Shots.Any(s => s.From.Side == Battle.Local));
                if (mode == "fire") _shotsSeen = Math.Max(0, Battle.Shots.FindLastIndex(s => s.From.Side == Battle.Local)); // that turn's beams drawn
                break;
            case "fighter": Ui.Command(0xB); Ui.Frame(); break;
            case "nav":
                for (int id = 0x10; id <= 0x13; id++) Ui.Command(id);
                Ui.Frame();
                var pts = Battle.NavPoints.Where(p => p.Set == 2 && p.Layer == 2).Take(2).ToList();
                Ui.RightDown(pts[0], false);
                Ui.RightDown(pts[1], true);
                Ui.Frame();
                break;
            case "follow": Ui.Camera.FollowGroup(0, Ui); break;
            case ['c', 'l', 'o', 's', 'e', .. var z]:
                if (own.FirstOrDefault() is { } first)
                {
                    Click(first, false);
                    Operate(TacticalCamera.ResetSubject);
                    for (int i = 0, n = int.TryParse(z, out int k) ? k : 12; i < n; i++) Operate(TacticalCamera.ZoomIn);
                }
                break;
            case "missions": Ui.ShowPage(0x19, Ui.PageObject); break;
            case "maneuvers": Ui.ShowPage(0x1A, Ui.PageObject); break;
            case "options": Ui.ShowPage(0x1B); break;
            case "gameoptions": if (!Battle.Paused) TogglePause(); OptionsRequested?.Invoke(); break; // the Game Options button (front end only)
            case "withdraw": Ui.ShowPage(0x1B); Ui.WithdrawPressed(); break;
            case "highlight": Ui.Command(0x14); Ui.Command(0x15); Ui.Frame(); break;
            case "pause": TogglePause(); break;
            case "observe": Ui.Command(0x7B); Ui.Frame(); break;
            case ['z', 'o', 'o', 'm', .. var zn] when int.TryParse(zn, out int steps): // the current mode's subject, zoomed in N steps
                Operate(TacticalCamera.ResetSubject);
                for (int i = 0; i < steps; i++) Operate(TacticalCamera.ZoomIn);
                break;
            case "launch": // until an enemy fighter launches from its carrier
                Run(60, () => Battle.Units.Any(u => u.Fighter && u.Side != Battle.Local && u.Active && u.Carrier is not null));
                GD.Print($"demo launch: turn {Battle.Turn}, launched {Battle.Units.Count(u => u.Fighter && u.Active && u.Carrier is not null)}");
                break;
            case "tractor": // own ships at the nearest enemy until some unit is held
                DemoStep("closing");
                Run(240, () => Battle.Units.Any(u => u.Holding is not null));
                GD.Print($"demo tractor: turn {Battle.Turn}, " + string.Join(", ", Battle.Units.Where(u => u.Holding is not null).Select(u => $"{Label(u.Object)} holds {Label(u.Holding!.Object)}, speed {Battle.Speed(u.Holding!):0.00}")));
                break;
            case "retreat": // the local side's world withdraw (0x7A), then until its first jump
                Ui.Command(0x7A);
                Ui.Frame();
                Run(60, () => Battle.Units.Any(u => u.Side == Battle.Local && u.State is 6 or 7 or 8 or 9));
                GD.Print($"demo retreat: turn {Battle.Turn}, side state {Battle.SideState(Battle.Local)}, states " + string.Join(" ", Battle.Units.Where(u => u.Side == Battle.Local).Select(u => u.State)));
                break;
            case "superlaser":
                if (Battle.Local == 1)
                {
                    Ui.Command(0xF);
                    Ui.Frame();
                    Click(enemies.FirstOrDefault() ?? Battle.Units.FirstOrDefault(u => u.Side != Battle.Local && u.Active), true);
                }
                Run(10, () => Battle.Superlasers.Count > 0);
                _superSeen = Math.Max(0, Battle.Superlasers.Count - 1);
                GD.Print($"demo superlaser: turn {Battle.Turn}, beams {Battle.Superlasers.Count}, panel {Ui.DeathStarPanel} {Ui.PanelValue}");
                break;
            case "dsrun":
                GD.Print($"demo dsrun: fighters {Battle.Taking.Count(u => u.Fighter && u.Side == 0)} / {Battle.Taking.Count(u => u.Fighter && u.Side == 1)}, scene alive {Battle.SceneAlive}, attack possible {Battle.AttackPossible}");
                Ui.Command(0xB);
                Ui.Frame();
                Ui.AssignMission(6);
                Ui.Frame();
                Run(120, () => Battle.Units.Any(u => u.Hidden));
                GD.Print($"demo dsrun: turn {Battle.Turn}, attack possible {Battle.AttackPossible}, run {Battle.DeathStarRun}, hidden {Battle.Units.Count(u => u.Hidden)}");
                break;
            case "run": Run(10); break;
            case var fx when fx.TrimEnd("0123456789".ToCharArray()) is "spark" or "hull" or "explode": // presentation only: an effect request on the first enemy ship, viewed from close by. A number is the zoom steps (default 14)
                if (enemies.FirstOrDefault() is { } victim)
                {
                    var kind = fx.TrimEnd("0123456789".ToCharArray());
                    Click(victim, false);
                    Battle.Effects.Add(new TacticalEffect(victim, kind == "spark" ? 2 : 1, kind switch { "spark" => 0, "hull" => 1, _ => 2 }));
                    Operate(TacticalCamera.ResetSubject);
                    for (int i = 0, n = int.TryParse(fx[kind.Length..], out int k) ? k : 14; i < n; i++) Operate(TacticalCamera.ZoomIn);
                }
                break;
            case ['t', 'u', 'r', 'n', .. var tn] when int.TryParse(tn, out int turns): // orbit N steps to the left (background check)
                for (int i = 0; i < turns; i++) Operate(TacticalCamera.RotateLeft);
                break;
            case ['t', 'i', 'l', 't', .. var tl] when int.TryParse(tl, out int tilts): // orbit N steps down
                for (int i = 0; i < tilts; i++) Operate(TacticalCamera.TiltDown);
                break;
            case ['i', 'n', 't', 'r', 'o', .. var ims] when int.TryParse(ims, out int wall): // N ms of the jump-in intro
                for (int t = 0; t < wall && Battle.InIntro; t += 50) Battle.Advance(50);
                break;
            case ['r', 'u', 'n', .. var n] when int.TryParse(n, out int seconds): Run(seconds); break;
        }
    }
}

/// <summary>A HUD button (TACTICAL bitmaps, TEXTTACT tooltip) with an optional caption and the camera repeat (press / release).</summary>
public partial class HudButton : FrontButton
{
    public string Caption { get; set; } = "";
    public Action? Pressed { get; set; }
    public Action? Released { get; set; }

    protected override void OnPress() => Pressed?.Invoke();

    protected override void OnRelease(bool inside)
    {
        Released?.Invoke();
        base.OnRelease(inside);
    }

    public override void _Draw()
    {
        base._Draw();
        if (Caption != "") Assets.DrawText(this, new Rect2(Vector2.Zero, Size), Caption, 3, Disabled ? new Color(0.5f, 0.5f, 0.5f) : Colors.White, new TextFormat { Center = true, VCenter = true, SingleLine = true, NoClip = true }); // button label: drawn without clipping
    }
}
