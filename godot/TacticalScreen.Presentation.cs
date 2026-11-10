using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenSWR.Sim;

/// <summary>
/// Presentation of the tactical screen. The sound manager plays on bus "Sound" (<see cref="TacticalSounds"/>) and is fed with the
/// battle's sound and voice requests. This part also covers the selection sound, the Death Star hints, bolts and the superlaser beam,
/// hit and explosion sequences, the tractor and gravity loops (Show Pyrotechnics), the jump-in and jump-out offsets, and the
/// background: starfield 5030, planet and Death Star decals, holocube and two lights. Options are read once at battle start. All
/// presentation randomness comes from its own generator, never the battle's. Nothing here changes the simulation.
/// </summary>
public partial class TacticalScreen
{
    // option bits, read once at construction
    readonly bool _starfield, _planet, _pyro, _high, _holocube;
    readonly GameRandom _present = new(Math.Max(1, (int)(Time.GetTicksUsec() % int.MaxValue)));
    TacticalSounds _sounds = null!;
    int _soundsSeen, _voicesSeen, _effectsSeen;
    Node3D? _dsFrame;
    readonly List<Bolt> _bolts = [];
    (MeshInstance3D Node, TacticalUnit To, double Time)? _beam;
    readonly Dictionary<TacticalUnit, Fx> _fx = [];
    (TacticalUi.Mode? Mode, int? Handle) _selection;

    sealed class Bolt(Node3D node, Vector3 start, TacticalUnit target)
    {
        public readonly Node3D Node = node;
        public readonly Vector3 Start = start;
        public readonly TacticalUnit Target = target;
        public double Time;
    }

    /// <summary>A unit's effect sub-frames: a sequence (spark / hull / death) and a loop (tractor / gravity).</summary>
    sealed class Fx
    {
        public readonly Sprite3D Hit = Decal(), Loop = Decal();
        public int Flag, Base, Frames;
        public double Time, LoopTime;
        public bool MeshGone, LoopsOff;
    }

    /// <summary>A camera-facing effect decal: s x s world units, origin in image pixels; unlit, index 0 transparent.</summary>
    static Sprite3D Decal() => new() { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, Visible = false, AlphaCut = SpriteBase3D.AlphaCutMode.Discard };

    /// <summary>Playback for the sound manager: a pool of players on bus "Sound" (no 3-D sound).</summary>
    sealed class Sink(Node owner) : ITacticalSoundSink
    {
        readonly List<AudioStreamPlayer> _players = [];

        public object? Start(string module, int wave)
        {
            AudioStream? s;
            try { s = Assets.Wave((ushort)wave, module); }
            catch (FileNotFoundException) { s = null; } // module missing: silent
            if (s is null) return null;
            var p = _players.Find(x => !x.Playing);
            if (p is null)
            {
                owner.AddChild(p = new AudioStreamPlayer { Bus = "Sound" });
                _players.Add(p);
            }
            p.Stream = s;
            p.Play();
            return p;
        }

        public bool Playing(object handle) => GodotObject.IsInstanceValid((AudioStreamPlayer)handle) && ((AudioStreamPlayer)handle).Playing;
        public void Stop(object handle) => ((AudioStreamPlayer)handle).Stop();
    }

    static double Now => Time.GetTicksMsec();

    /// <summary>The drawn position: the interpolated sim position plus the intro / jump-out offsets.</summary>
    Vector3 Shown(TacticalUnit u) => Drawn(u) + G(Battle.IntroOffset(u) + Battle.JumpOffset(u));

    /// <summary>Per frame: hints, requests, selection sound, the manager's queue, bolts, beam, effects.</summary>
    void Present(double delta)
    {
        if (!Battle.Done) Battle.Hints(delta * 1000);
        double now = Now;
        _sounds.Paused = Battle.Paused;
        for (; _soundsSeen < Battle.Sounds.Count; _soundsSeen++) _sounds.Play(Battle.Sounds[_soundsSeen], true, now);
        for (; _voicesSeen < Ui.Voices.Count; _voicesSeen++) _sounds.Play(Ui.Voices[_voicesSeen], true, now);
        SelectionSound(now);
        _sounds.Update(now);
        Bolts(delta);
        Beam(delta);
        Effects(delta);
    }

    /// <summary>
    /// Selection sound. Own-ship, enemy, fighter-group and task-force modes ask for the info unit's selection sound every frame
    /// (through <see cref="TacticalSounds.Selection"/>, q = 0: a request with no room is dropped, not queued). A dropped request is
    /// retried. A pause cuts a playing sound off, and it starts again from the beginning on the first unpaused frame. A sound that has
    /// finished is not repeated for that mode instance. The fighter mode skips it during the intro. Leaving the mode stops it.
    /// </summary>
    void SelectionSound(double now)
    {
        var mode = Ui.Current;
        if (!ReferenceEquals(mode, _selection.Mode))
        {
            if (_selection.Handle is { } old) _sounds.Stop(old);
            _selection = (mode, null);
        }
        if (mode is not (TacticalUi.OwnShipMode or TacticalUi.EnemyMode or TacticalUi.FighterMode or TacticalUi.TaskForceMode) || Ui.Info is not { } info) return;
        _selection.Handle = _sounds.Selection(_selection.Handle, TacticalBattle.Selection(info), Battle.Paused, mode is TacticalUi.FighterMode && Battle.InIntro, now);
    }

    // ---- bolts and the superlaser beam ----

    static readonly Dictionary<(int Style, float Length), ArrayMesh> BoltMeshes = [];

    /// <summary>
    /// Bolt mesh, nose -Z toward the target (LookAt): style 1 one needle z 0: L, style 2 two needles 0: L/3 and 2L/3: L, style 3 two
    /// needles at x = +/- 0.1; a needle of width 0.002 draws as a line (Inferred, openswr design: Godot rasterises nothing that thin).
    /// </summary>
    static ArrayMesh BoltMesh(int style, float length)
    {
        if (BoltMeshes.TryGetValue((style, length), out var m)) return m;
        Vector3[] v = style switch
        {
            2 => [new(0, 0, 0), new(0, 0, -0.333f * length), new(0, 0, -0.666f * length), new(0, 0, -length)],
            3 => [new(-0.1f, 0, 0), new(-0.1f, 0, -length), new(0.1f, 0, 0), new(0.1f, 0, -length)],
            _ => [new(0, 0, 0), new(0, 0, -length)],
        };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        m = new ArrayMesh();
        m.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return BoltMeshes[(style, length)] = m;
    }

    /// <summary>Length and style of a bolt by message and shot value.</summary>
    static (float Length, int Style) BoltShape(TacticalShot s) => s.Kind switch
    {
        TacticalMessage.LaserShot => (0.5f, s.Value >= 28.8 ? 2 : 1),
        TacticalMessage.TurbolaserShot => s.Value < 34.666668 ? (0.65f, 1) : TacticalTypeIndex.TurbolaserStyleTwo(s.To.TypeIndex) ? (0.75f, 2) : (1.0f, 3),
        TacticalMessage.IonShot => s.Value >= 32.0 ? (1.0f, 3) : (0.4f, 1),
        _ => (0.2f, s.Value >= 12.8 ? 3 : 1),
    };

    /// <summary>
    /// New shot displays become bolts: from the shooter toward the target, colour red (side index 0) / green (1), ion blue;
    /// each frame at start + (target now - start) * t / 1.0 s, deleted at 1.0 s (wall clock, also while paused: Inferred).
    /// </summary>
    // the three bolt colours share their materials (never changed after creation); nav markers keep their own (recoloured per frame)
    static readonly Dictionary<Color, StandardMaterial3D> BoltMaterials = [];
    static StandardMaterial3D BoltMaterial(Color c) => BoltMaterials.TryGetValue(c, out var m) ? m : BoltMaterials[c] = Flat(c);

    void Bolts(double delta)
    {
        for (; _shotsSeen < Battle.Shots.Count; _shotsSeen++)
        {
            var shot = Battle.Shots[_shotsSeen];
            var (length, style) = BoltShape(shot);
            var colour = shot.Kind == TacticalMessage.IonShot ? new Color(0, 0, 1) : shot.From.Side == 0 ? new Color(1, 0, 0) : new Color(0, 1, 0);
            var node = new MeshInstance3D { Mesh = BoltMesh(style, length), MaterialOverride = BoltMaterial(colour) };
            _world.AddChild(node);
            Vector3 from = Shown(shot.From), to = Shown(shot.To);
            node.Position = from;
            if ((to - from).LengthSquared() > 1e-8f) node.LookAt(to, Math.Abs((to - from).Normalized().Y) > 0.99f ? Vector3.Right : Vector3.Up);
            _bolts.Add(new Bolt(node, from, shot.To));
        }
        for (int i = _bolts.Count - 1; i >= 0; i--)
        {
            var b = _bolts[i];
            b.Time += delta;
            if (b.Time >= 1.0)
            {
                b.Node.QueueFree();
                _bolts.RemoveAt(i);
                continue;
            }
            b.Node.Position = b.Start + (Shown(b.Target) - b.Start) * (float)b.Time;
        }
    }

    /// <summary>
    /// Superlaser beam: green, from the Death Star decal's frame toward the target, its length growing with t / 1.0 s
    /// to the target's distance, deleted after 1.0 s. There is one slot: a new shot replaces the running beam, and the old
    /// one is dropped. The original leaves the old beam frozen until teardown (Inferred: still drawn). The tube's radius of 0.5 world
    /// units is an openswr design, since its cross-section was not read.
    /// </summary>
    void Beam(double delta)
    {
        for (; _superSeen < Battle.Superlasers.Count; _superSeen++)
            if (_dsFrame is not null)
            {
                _beam?.Node.QueueFree();
                var node = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1, RadialSegments = 8 }, MaterialOverride = Flat(new Color(0, 1, 0)) };
                _world.AddChild(node);
                _beam = (node, Battle.Superlasers[_superSeen].To, 0);
            }
        if (_beam is not { } beam) return;
        double t = beam.Time + delta;
        if (t >= 1.0)
        {
            beam.Node.QueueFree();
            _beam = null;
            return;
        }
        _beam = beam with { Time = t };
        Vector3 from = _dsFrame!.Position, to = Shown(beam.To);
        float length = from.DistanceTo(to) * (float)t;
        if (length < 1e-3f) { beam.Node.Visible = false; return; }
        beam.Node.Visible = true;
        var dir = (to - from).Normalized();
        var basis = new Basis(new Quaternion(Vector3.Up, dir)).Scaled(new Vector3(1, length, 1));
        beam.Node.Transform = new Transform3D(basis, from + dir * (length / 2));
    }

    // ---- hits, explosions, loops ----

    Fx FxOf(TacticalUnit u)
    {
        if (_fx.TryGetValue(u, out var f)) return f;
        f = _fx[u] = new Fx();
        _world.AddChild(f.Hit);
        _world.AddChild(f.Loop);
        return f;
    }

    /// <summary>
    /// Effect request: pyrotechnics off: only a death acts: the unit is hidden at once; on: the
    /// sequence (k, w): spark 3180 (ion) / 3060, 6 frames; hull 3240 (ion, 16) / 3300 (torpedo, 7) / 3120 (7); death 3360, 16; a new one
    /// replaces the running one only with a greater flag and never a spark by a spark or a hull explosion by a hull explosion. None on
    /// the unit the follow camera rides (Inferred).
    /// </summary>
    void StartEffect(TacticalEffect e)
    {
        var f = FxOf(e.Unit);
        if (!_pyro)
        {
            if (e.Kind == TacticalEffectKind.Death) f.MeshGone = true;
            return;
        }
        if (Ui.Camera.Follow?.Subject == e.Unit) return;
        var (flag, b, frames) = (e.Kind, e.Weapon) switch
        {
            (TacticalEffectKind.Spark, TacticalWeapon.Ion) => (TacticalEffectFlags.IonSpark, TacticalBitmaps.IonSparkEffect, 6),
            (TacticalEffectKind.Spark, _) => (TacticalEffectFlags.Spark, TacticalBitmaps.SparkEffect, 6),
            (TacticalEffectKind.HullExplosion, TacticalWeapon.Ion) => (TacticalEffectFlags.IonOverflow, TacticalBitmaps.IonOverflowEffect, 16),
            (TacticalEffectKind.HullExplosion, TacticalWeapon.Torpedo) => (TacticalEffectFlags.TorpedoHit, TacticalBitmaps.TorpedoHitEffect, 7),
            (TacticalEffectKind.HullExplosion, _) => (TacticalEffectFlags.HullExplosion, TacticalBitmaps.HullExplosionEffect, 7),
            _ => (TacticalEffectFlags.Death, TacticalBitmaps.DeathEffect, 16),
        };
        static bool Spark(int x) => x is TacticalEffectFlags.IonSpark or TacticalEffectFlags.Spark;
        static bool Hull(int x) => x is TacticalEffectFlags.IonOverflow or TacticalEffectFlags.HullExplosion or TacticalEffectFlags.TorpedoHit;
        int cur = f.Flag;
        if (cur != 0 && flag != TacticalEffectFlags.Death && (flag <= cur || Spark(flag) && Spark(cur) || Hull(flag) && Hull(cur))) return;
        (f.Flag, f.Base, f.Frames, f.Time) = (flag, b, frames, 0);
    }

    /// <summary>
    /// Effects per frame: new requests; sequences show frame floor(t / 0.1 s) 1.0 unit from the unit toward the camera in
    /// the horizontal plane and end after the last frame; a death at frame 11 hides the mesh and switches the loops off. Loops
    /// (pyrotechnics only): gravity well 3520-3527 on an active GravityWell ship, else tractor 3620-3627 on a held unit, cycling 0.1 s.
    /// </summary>
    void Effects(double delta)
    {
        for (; _effectsSeen < Battle.Effects.Count; _effectsSeen++) StartEffect(Battle.Effects[_effectsSeen]);
        var cam = _camera.GlobalPosition;
        foreach (var u in Battle.Units)
        {
            var f = _fx.GetValueOrDefault(u);
            int loop = !_pyro || f?.LoopsOff == true || !_nodes[u].Visible && f?.Flag != TacticalEffectFlags.Death ? 0 : Battle.GravityLoop(u) ? 2 : u.HeldBy.Count > 0 ? 1 : 0;
            if (f is null && loop == 0) continue;
            f ??= FxOf(u);
            var p = Shown(u);
            var toward = new Vector3(cam.X - p.X, 0, cam.Z - p.Z);
            var at = toward.LengthSquared() > 1e-8f ? p + toward.Normalized() : p;
            if (f.Flag != 0)
            {
                f.Time += delta;
                int frame = (int)(f.Time / 0.1);
                if (f.Flag == TacticalEffectFlags.Death && frame >= 11) (f.MeshGone, f.LoopsOff) = (true, true);
                if (frame >= f.Frames) f.Flag = 0;
                else Show(f.Hit, f.Base + frame, at);
            }
            if (f.Flag == 0) f.Hit.Visible = false;
            if (loop == 0) { f.Loop.Visible = false; continue; }
            f.LoopTime += delta;
            Show(f.Loop, (loop == 2 ? TacticalBitmaps.GravityWellLoop : TacticalBitmaps.TractorLoop) + (int)(f.LoopTime / 0.1) % 8, at);
        }

        static void Show(Sprite3D s, int image, Vector3 at)
        {
            var tex = TacticalModels.Image(image);
            s.Texture = tex;
            s.Visible = tex is not null;
            s.Position = at;
            if (tex is null) return;
            var (size, ox, oy) = DecalShape(image);
            float w = tex.GetWidth(), h = tex.GetHeight();
            s.PixelSize = size / w;
            s.Scale = new Vector3(1, w / h, 1); // square s x s whatever the image's aspect
            s.Offset = new Vector2(w / 2 - ox, oy - h / 2); // pixel (ox, oy) from the top-left at the anchor
        }
    }

    /// <summary>Decal size s and origin (ox, oy) per image range (preload, verified).</summary>
    static (float Size, int Ox, int Oy) DecalShape(int image) => image switch
    {
        >= TacticalBitmaps.DeathEffect and <= TacticalBitmaps.DeathEffect + 15 => (7.5f, 64, 64), // death
        >= TacticalBitmaps.SparkEffect and <= TacticalBitmaps.SparkEffect + 5 or >= TacticalBitmaps.IonSparkEffect and <= TacticalBitmaps.IonSparkEffect + 5 => (2.5f, 32, 32), // spark / ion spark: origin off-centre on purpose
        >= TacticalBitmaps.HullExplosionEffect and <= TacticalBitmaps.HullExplosionEffect + 6 => (5.0f, 32, 32), // hull explosion
        >= TacticalBitmaps.IonOverflowEffect and <= TacticalBitmaps.IonOverflowEffect + 15 => (5.0f, 64, 32), // ion overflow
        _ => (5.0f, 64, 64), // torpedo hull hit 3300-3306, tractor 3620-3627, gravity well 3520-3527
    };

    /// <summary>A capital ship in state 11 stays drawn until its death effect hides it (frame 11, or at once without pyrotechnics).</summary>
    bool DeadShown(TacticalUnit u) => !u.Fighter && u.State == TacticalUnitState.Destroyed && _fx.TryGetValue(u, out var f) && !f.MeshGone && f.Flag == TacticalEffectFlags.Death;

    // ---- background ----

    /// <summary>
    /// Background builder with S = W: lights (directional 0.8 from (5, 5, -1) toward the origin, ambient 0.5, no fog);
    /// the starfield 303 image 5030 as a fixed screen background (else black); planet decal (option) of the system's picture p (303
    /// image 14000 + p, palette 15000 + p, W x W) at (2W, r1 - 0.1W, r2 - 0.1W), r = rand(trunc(0.2W)); the Death Star decal (5010 /
    /// 5020, 0.5W) at (-2W, r1 - 0.2W, r2 - 0.2W) + (0, 0.11W, -0.09W), r = rand(trunc(0.4W)); holocube (option): blue wire cube of edge W.
    /// </summary>
    void Background(Node3D world)
    {
        double w = Battle.Size;
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.5f, 0.5f), AmbientLightEnergy = 1,
            },
        });
        var sun = new DirectionalLight3D { LightColor = new Color(0.8f, 0.8f, 0.8f), LightEnergy = 1 };
        world.AddChild(sun);
        sun.LookAtFromPosition(G(new V3(5, 5, -1)), Vector3.Zero, Vector3.Up);
        int picture = (int)Battle.System.Definition.Picture; // the system's picture number (Inferred)
        // An unmodded install has no 14000 range: its p is 1-26 over images 5501-5526, palettes 5531-5556 (Inferred: same order as the sector pictures).
        if (_planet && picture is >= 1 and <= 200
            && (TacticalModels.Image(TacticalBitmaps.PlanetImageBase + picture, TacticalBitmaps.PlanetPaletteBase + picture) ?? (picture <= 26 ? TacticalModels.Image(TacticalBitmaps.PlainPlanetImageBase + picture, TacticalBitmaps.PlainPlanetPaletteBase + picture) : null)) is { } planet)
        {
            int r1 = _present.Rand((int)(0.2 * w)), r2 = _present.Rand((int)(0.2 * w));
            world.AddChild(new Sprite3D
            {
                Texture = planet, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, PixelSize = (float)(w / planet.GetWidth()),
                Position = G(new V3(2 * w, r1 - 0.1 * w, r2 - 0.1 * w)), AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            });
        }
        if (Battle.HasScene)
        {
            int r1 = _present.Rand((int)(0.4 * w)), r2 = _present.Rand((int)(0.4 * w));
            var tex = TacticalModels.Image(Battle.SceneUnfinished ? TacticalBitmaps.DeathStarUnfinishedImage : TacticalBitmaps.DeathStarImage);
            var s = new Sprite3D { Texture = tex, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Shaded = false, AlphaCut = SpriteBase3D.AlphaCutMode.Discard };
            if (tex is not null) s.PixelSize = (float)(0.5 * w / tex.GetWidth());
            world.AddChild(_dsFrame = s);
            s.Position = G(new V3(-2 * w, r1 - 0.2 * w + 0.11 * w, r2 - 0.2 * w - 0.09 * w));
        }
        if (_holocube)
        {
            float h = (float)(w / 2);
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Lines);
            Vector3 C(int i) => new(CubeCorners[i].X * h, CubeCorners[i].Y * h, CubeCorners[i].Z * h);
            foreach (var (i, j) in CubeEdges)
            {
                st.AddVertex(C(i));
                st.AddVertex(C(j));
            }
            world.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Flat(new Color(0, 0, 1)) });
        }
    }

    /// <summary>The fixed screen background behind the 3-D view: starfield image 5030 (option) or black.</summary>
    Control Backdrop()
    {
        var black = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, Position = View.Position, Size = View.Size };
        if (_starfield && TacticalModels.Image(TacticalBitmaps.StarfieldImage) is { } stars)
            black.AddChild(new TextureRect { Texture = stars, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore, Size = View.Size });
        return black;
    }

    public override void _ExitTree() => _sounds?.StopAll(); // stop the sounds on teardown
}
