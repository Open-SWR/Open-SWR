namespace OpenSWR.Sim;

/// <summary>One camera operation: zoom in and out, rotate left and right, tilt up and down, reset view, remember, reset subject.</summary>
public enum TacticalCameraOperation
{
    ZoomIn, ZoomOut, RotateLeft, RotateRight, TiltUp, TiltDown, ResetView, Remember, ResetSubject,
}

/// <summary>
/// The tactical cameras, in world coordinates (y up, left-handed as the original; the front end converts). Orbit camera:
/// distance 1.7 times W (Inferred: the scene-size argument is W), integer degrees, step clamp(z, 1, 5), field times 0.9 or
/// times 1.1 within [0.005, 1.5]; Remember / Reset View / Reset Subject. Follow camera (F1-F12): subject k1 and look-at k2,
/// field 0.1 at start within [0.005, 0.2].
/// </summary>
public sealed class TacticalCamera
{
    readonly TacticalBattle _b;
    internal TacticalCamera(TacticalBattle battle)
    {
        _b = battle;
        Reset();
    }

    /// <summary>Orbit camera fields: yaw (integer degrees in [-180, 180]), pitch (integer degrees), field f, zoom level z (starts 5), subject (none = the origin).</summary>
    public int Yaw { get; private set; }
    public int Pitch { get; private set; }
    public double Field { get; private set; }
    public int Zoom { get; private set; } = 5;
    public TacticalUnit? Subject { get; private set; }
    (int Yaw, int Pitch, double Field, int Zoom, TacticalUnit? Subject)? _remembered;

    /// <summary>The follow camera while active.</summary>
    public FollowCamera? Follow { get; private set; }

    public sealed class FollowCamera(TacticalUnit subject, TacticalUnit? lookAt)
    {
        public TacticalUnit Subject { get; } = subject;
        public TacticalUnit? LookAt { get; internal set; } = lookAt;
        public double Field { get; internal set; } = 0.1;
    }

    /// <summary>Orbit distance D = 1.7 * W.</summary>
    public double Distance => 1.7 * _b.Size;

    /// <summary>Reset values: pitch 30, yaw -30 (local Alliance) / 150 (Empire), f = 0.2, no subject.</summary>
    void Reset() => (Pitch, Yaw, Field, Subject) = (30, _b.Local == 0 ? -30 : 150, 0.2, null);

    /// <summary>One camera operation. While a follow camera is active only zoom works on it (others ignored), except ResetView (drops it) and ResetSubject (does ResetView first).</summary>
    public void Operate(TacticalCameraOperation op, TacticalUnit? modeSubject = null)
    {
        if (Follow is { } f)
        {
            switch (op)
            {
                case TacticalCameraOperation.ZoomIn: f.Field = Math.Max(0.005, f.Field * 0.9); return;
                case TacticalCameraOperation.ZoomOut: f.Field = Math.Min(0.2, f.Field * 1.1); return;
                case TacticalCameraOperation.ResetView: break;
                case TacticalCameraOperation.ResetSubject: Operate(TacticalCameraOperation.ResetView); break;
                default: return;
            }
        }
        int step = Math.Clamp(Zoom, 1, 5);
        switch (op)
        {
            case TacticalCameraOperation.ZoomIn: (Field, Zoom) = (Math.Max(0.005, Field * 0.9), Zoom - 1); break;
            case TacticalCameraOperation.ZoomOut: (Field, Zoom) = (Math.Min(1.5, Field * 1.1), Zoom + 1); break;
            case TacticalCameraOperation.RotateLeft: Yaw = Wrap(Yaw - step); break;
            case TacticalCameraOperation.RotateRight: Yaw = Wrap(Yaw + step); break;
            case TacticalCameraOperation.TiltUp: if (Pitch < 90) Pitch += step; break;
            case TacticalCameraOperation.TiltDown: if (Pitch > -90) Pitch -= step; break;
            case TacticalCameraOperation.ResetView:
                Follow = null;
                if (_remembered is { } r) (Yaw, Pitch, Field, Zoom, Subject) = r;
                else Reset();
                break;
            case TacticalCameraOperation.Remember: _remembered = (Yaw, Pitch, Field, Zoom, Subject); break;
            case TacticalCameraOperation.ResetSubject: Subject = modeSubject; break;
        }
    }

    static int Wrap(int deg) => deg > 180 ? deg - 360 : deg < -180 ? deg + 360 : deg;

    /// <summary>
    /// Follow camera on F1-F12 (follow operation, index 0-11): task force i (used slots only): k1 = the info unit when the
    /// TF mode of that slot is current and has one, else the leader; k2 = its current target; fighter group (index 8-11): k1 = the
    /// leader, k2 = its target (on Recover the carrier, Inferred).
    /// </summary>
    public void FollowGroup(int index, TacticalUi ui)
    {
        var g = index < 8 ? ui.TaskForces[index] : ui.Fighters[index - 8];
        if (g is null) return;
        var k1 = index < 8 && ui.Current is TacticalUi.TaskForceMode m && m.Slot == index && ui.Info is { } info ? info : g.UiLeader; // Inferred: "the leader" is the UI leader; the simulation and navigator leaders are separate fields that can differ from it.
        if (k1 is null) return;
        Follow = new FollowCamera(k1, LookAt(g));
    }

    TacticalUnit? LookAt(TacticalGroup g) =>
        g.Fighters && g.Mission == TacticalMissionNumber.Recover && g.UiLeader?.U.Carrier is { } c ? _b.Units.FirstOrDefault(u => u.U == c) : _b.TargetList(g).FirstOrDefault();

    /// <summary>Per frame: when k1 is gone (not active, Inferred) the orbit camera returns; k2 follows the group's current target.</summary>
    internal void Frame()
    {
        if (Follow is not { } f) return;
        if (!f.Subject.Active) { Follow = null; return; }
        if (f.Subject.Group is { } g) f.LookAt = LookAt(g);
    }

    /// <summary>
    /// The camera pose: eye, look-at point and field. Orbit: the view axis rotated by yaw about y and pitch about x,
    /// the eye D back along it from the subject (none: the origin); rotation senses Inferred (a positive pitch looks down, yaw -30
    /// looks toward +z from behind side 0). Follow: s = max(dx, dy) of k1's model box; with
    /// k2 the eye at (x*s/L, y*s/L, 2s) in k1's frame, (x, y) = k2's position in k1's frame, L = sqrt(x^2 + y^2), looking at k2; without
    /// k2 at (-s, s, 2s) looking along k1's nose. k1's frame is the unit's model frame: +x = normalise(-f.z, 0, f.x), +y up, +z = -f, so the nose points to -z.
    /// </summary>
    public (V3 Eye, V3 At, double Field) Pose()
    {
        if (Follow is { } f)
        {
            var u = f.Subject;
            var fwd = u.Facing.IsZero ? new V3(0, 0, 1) : u.Facing.Normalised();
            var ax = new V3(-fwd.Z, 0, fwd.X).Normalised();
            var ay = new V3(0, 1, 0);
            var az = -fwd;
            V3 ToWorld(V3 l) => u.Position + ax * l.X + ay * l.Y + az * l.Z;
            double s = Math.Max(u.Box.X, u.Box.Y);
            if (f.LookAt is { } t)
            {
                var d = t.Position - u.Position;
                double x = d.Dot(ax), y = d.Dot(ay), l = Math.Sqrt(x * x + y * y);
                var local = l == 0 ? new V3(0, 0, 2 * s) : new V3(x * s / l, y * s / l, 2 * s);
                return (ToWorld(local), t.Position, f.Field);
            }
            var eye = ToWorld(new V3(-s, s, 2 * s));
            return (eye, eye + fwd, f.Field);
        }
        double yaw = Yaw * Math.PI / 180, pitch = Pitch * Math.PI / 180;
        var axis = new V3(Math.Sin(yaw) * Math.Cos(pitch), -Math.Sin(pitch), Math.Cos(yaw) * Math.Cos(pitch));
        var at = Subject?.Position ?? V3.Zero;
        return (at - axis * Distance, at, Field);
    }
}
