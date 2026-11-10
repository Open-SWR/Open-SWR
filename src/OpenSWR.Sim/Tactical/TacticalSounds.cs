namespace OpenSWR.Sim;

/// <summary>Playback of the tactical sound manager (the front end's players): start one WAVE, test it, stop it.</summary>
public interface ITacticalSoundSink
{
    /// <summary>Starts WAVE <paramref name="wave"/> of <paramref name="module"/> once; null when the module has no such resource.</summary>
    object? Start(string module, int wave);
    bool Playing(object handle);
    void Stop(object handle);
}

/// <summary>
/// The tactical sound manager. Request(n, q) takes a serial and the request time; n above 0x13C is dropped. The module is
/// TACTICAL.DLL for n below 0x20, else the local side's voice module (VOICEFXA.DLL for side index 0, VOICEFXE.DLL otherwise).
/// Classes: effects 0-0x1F (2: at most 5 playing, a pending request dropped after 225 ms), voices 0x20-0x11D (1: 1 playing,
/// 8000 ms, or 32000 ms while paused), run commentary 0x11E-0x13C (3: 1 playing, 3000 ms), independent of each other. A request
/// starts when its class has room; otherwise q = 0 drops it and q = 1 queues it. Every frame, and before each request, finished
/// sounds leave the playing list and the pending queue is walked oldest first: stale requests are dropped, and the others start
/// while their class has room. The variant is picked when a request tries to start (also on a retry), from the presentation
/// generator, never the battle's. A request whose WAVE the loaded module lacks is dropped silently. No 3-D sound.
/// </summary>
public sealed class TacticalSounds(int local, ITacticalSoundSink sink, Func<int, int> pick)
{
    sealed record Request(int Serial, int N, double Time);

    readonly List<Request> _pending = [];
    readonly List<(int Serial, int Class, object Handle)> _playing = [];
    int _serial;

    /// <summary>The world's pause counter is not zero: the voice timeout is 32000 ms.</summary>
    public bool Paused { get; set; }

    /// <summary>Class of request n: effects 2, voices 1, run commentary 3.</summary>
    public static int Class(int n) => n < 0x20 ? 2 : n < 0x11E ? 1 : 3;

    static int Limit(int c) => c == 2 ? 5 : 1;

    double Timeout(int c) => c switch { 2 => 225, 3 => 3000, _ => Paused ? 32000 : 8000 };

    /// <summary>Variants of an effect id: 4 for 0-2, 2 for 3-5, 3 for 9-0x13, 1 for the rest (voices too).</summary>
    public static int Variants(int n) => n switch { <= 2 => 4, <= 5 => 2, >= 9 and <= 0x13 => 3, _ => 1 };

    /// <summary>
    /// Module and WAVE of request n, variant v: effects are TACTICAL 13000 plus the table base (ids in order, variants contiguous);
    /// voices 0x20-0x99 map to 14001 + (n - 0x20), 0x9A-0x13C to 15001 + (n - 0x9A), in the local side's voice module.
    /// </summary>
    public static (string Module, int Wave) Resource(int n, int v, int local)
    {
        if (n < 0x20)
        {
            int b = 13000;
            for (int i = 0; i < n; i++) b += Variants(i);
            return ("TACTICAL.DLL", b + v);
        }
        return (local == 0 ? "VOICEFXA.DLL" : "VOICEFXE.DLL", n <= 0x99 ? 14001 + (n - 0x20) : 15001 + (n - 0x9A));
    }

    /// <summary>Request(n, q): the serial of the request (started or queued), null when dropped.</summary>
    public int? Play(int n, bool queue, double now)
    {
        if (n is < 0 or > 0x13C) return null;
        Update(now);
        var r = new Request(++_serial, n, now);
        var started = TryStart(r);
        if (started is true) return r.Serial;
        if (started is null || !queue) return null;
        _pending.Add(r);
        return r.Serial;
    }

    /// <summary>
    /// Per frame: finished sounds leave the playing list; the pending queue oldest first: stale (now - request time &gt;
    /// its class timeout) dropped, the others start while their class has room.
    /// </summary>
    public void Update(double now)
    {
        _playing.RemoveAll(p => !sink.Playing(p.Handle));
        foreach (var r in _pending.OrderBy(r => r.Serial).ToList())
        {
            int c = Class(r.N);
            if (now - r.Time > Timeout(c)) { _pending.Remove(r); continue; }
            if (TryStart(r) is not false) _pending.Remove(r);
        }
    }

    /// <summary>true started, false no room in its class, null no resource (dropped).</summary>
    bool? TryStart(Request r)
    {
        int c = Class(r.N);
        if (_playing.Count(p => p.Class == c) >= Limit(c)) return false;
        int n = Variants(r.N);
        var (module, wave) = Resource(r.N, n > 1 ? pick(n) : 0, local);
        if (sink.Start(module, wave) is not { } h) return null;
        _playing.Add((r.Serial, c, h));
        return true;
    }

    /// <summary>A request is playing (started and not finished at the last update).</summary>
    public bool IsPlaying(int serial) => _playing.Any(p => p.Serial == serial);

    /// <summary>Stops a request by its serial, playing or pending.</summary>
    public void Stop(int serial)
    {
        foreach (var p in _playing.Where(p => p.Serial == serial).ToList())
        {
            sink.Stop(p.Handle);
            _playing.Remove(p);
        }
        _pending.RemoveAll(r => r.Serial == serial);
    }

    /// <summary>
    /// One frame of a mode's selection sound. <paramref name="handle"/> is the mode's sound handle (null = 0). A playing sound
    /// is kept, or stopped and cleared when paused. A sound that is not playing (never asked, dropped, or finished; a finished one
    /// keeps its handle, so it is never repeated) is requested with q = 0 only while the handle is null, not paused and not
    /// <paramref name="skip"/>. A request dropped for room returns null and is asked again next frame. Returns the new handle.
    /// </summary>
    public int? Selection(int? handle, int n, bool paused, bool skip, double now)
    {
        if (handle is { } h && IsPlaying(h))
        {
            if (!paused) return h;
            Stop(h);
            return null;
        }
        return handle is null && !paused && !skip ? Play(n, false, now) : handle;
    }

    /// <summary>Teardown, and before the outcome movies: everything stops.</summary>
    public void StopAll()
    {
        foreach (var p in _playing) sink.Stop(p.Handle);
        _playing.Clear();
        _pending.Clear();
    }
}
