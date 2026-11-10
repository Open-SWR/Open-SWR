namespace OpenSWR.Sim;

/// <summary>Tactical voice request numbers: Alliance and Empire (suffix Empire) number of each trigger.</summary>
public static class TacticalVoice
{
    public const int BattleStart = 32, BattleStartEmpire = 154;
    /// <summary>Own ship ordered to a nav point.</summary>
    public const int ShipToNav = 33, ShipToNavEmpire = 155;
    /// <summary>Task force k (or a multi-selection) to a nav point: + k.</summary>
    public const int TaskForceToNav = 34, TaskForceToNavEmpire = 156;
    /// <summary>+ colour.</summary>
    public const int FighterGroupToNav = 42, FighterGroupToNavEmpire = 164;
    /// <summary>Reached its nav point, targets pending.</summary>
    public const int UnnumberedTaskForceAtNav = 46, UnnumberedTaskForceAtNavEmpire = 168;
    /// <summary>+ k.</summary>
    public const int TaskForceAtNav = 47, TaskForceAtNavEmpire = 169;
    /// <summary>+ colour.</summary>
    public const int FighterGroupAtNav = 55, FighterGroupAtNavEmpire = 177;
    public const int ShipAttack = 59, ShipAttackEmpire = 181;
    /// <summary>+ k.</summary>
    public const int TaskForceAttack = 60, TaskForceAttackEmpire = 182;
    /// <summary>+ colour.</summary>
    public const int FighterGroupAttack = 68, FighterGroupAttackEmpire = 190;
    /// <summary>Target destroyed, nothing left to do.</summary>
    public const int UnnumberedTaskForceDone = 72, UnnumberedTaskForceDoneEmpire = 194;
    /// <summary>+ k.</summary>
    public const int TaskForceDone = 73, TaskForceDoneEmpire = 195;
    /// <summary>+ colour.</summary>
    public const int FighterGroupDone = 81, FighterGroupDoneEmpire = 203;
    /// <summary>By a shooter without a numbered task force.</summary>
    public const int EnemyDestroyed = 85, EnemyDestroyedEmpire = 207;
    /// <summary>Enemy destroyed by a ship of task force k: + k.</summary>
    public const int TaskForceKill = 86, TaskForceKillEmpire = 208;
    /// <summary>+ colour.</summary>
    public const int FighterGroupKill = 94, FighterGroupKillEmpire = 216;
    /// <summary>No numbered group.</summary>
    public const int UnitLost = 98, UnitLostEmpire = 220;
    /// <summary>+ k.</summary>
    public const int TaskForceShipLost = 99, TaskForceShipLostEmpire = 221;
    /// <summary>+ colour.</summary>
    public const int FighterLost = 107, FighterLostEmpire = 229;
    /// <summary>+ k.</summary>
    public const int TaskForceTactic = 111, TaskForceTacticEmpire = 237;
    /// <summary>Own ship: escort or mission order.</summary>
    public const int ShipMission = 119, ShipMissionEmpire = 245;
    /// <summary>+ k.</summary>
    public const int TaskForceMission = 120, TaskForceMissionEmpire = 246;
    /// <summary>+ colour.</summary>
    public const int FighterGroupMission = 128, FighterGroupMissionEmpire = 254;
    /// <summary>Multi-selection order without a free slot.</summary>
    public const int NoFreeSlot = 132, NoFreeSlotEmpire = 258;
    public const int CharacterAboardLost = 134, CharacterAboardLostEmpire = 260;
    /// <summary>+ colour.</summary>
    public const int FighterGroupRecovered = 135, FighterGroupRecoveredEmpire = 261;
    /// <summary>+ colour.</summary>
    public const int FighterGroupNoCarrier = 139, FighterGroupNoCarrierEmpire = 265;
    public const int WithdrawOrdered = 143, WithdrawOrderedEmpire = 269;
    public const int DeathStarAttackPossible = 146, DeathStarAttackPossibleEmpire = 284;
    /// <summary>Withdraw pressed while an own capital ship cannot jump.</summary>
    public const int CannotJump = 149, CannotJumpEmpire = 270;
    /// <summary>Battle end: + outcome (0 to 3).</summary>
    /// <summary>Battle end, by the end flags S (TacticalBattle.EndVoice): Alliance S[0] BattleEnd, S[1] and S[2] BattleEndS1S2, S[1] BattleEndS1, S[3] BattleEndS3;
    /// Empire S[2] BattleEndEmpire, S[3] and S[0] BattleEndEmpireS3S0, S[3] BattleEndEmpireS3, S[1] BattleEndEmpireS1. Meaning of the flags in the original is unknown beyond these uses.</summary>
    public const int BattleEnd = 150, BattleEndS1S2 = 151, BattleEndS1 = 152, BattleEndS3 = 153;
    public const int BattleEndEmpire = 280, BattleEndEmpireS3S0 = 281, BattleEndEmpireS3 = 282, BattleEndEmpireS1 = 283;
    public const int DeathStarPresent = 144, SuperlaserFiredAlliance = 145, DeathStarShielded = 147, TooFewFighters = 148;
    public const int SuperlaserFired = 273, SuperlaserCharged = 275, RunStarted = 277, RunInProgress = 285, RunCommentary = 286;
}

/// <summary>Playback of the tactical sound manager (the front end's players): start one WAVE, test it, stop it.</summary>
public interface ITacticalSoundSink
{
    /// <summary>Starts WAVE <paramref name="wave"/> of <paramref name="module"/> once; null when the module has no such resource.</summary>
    object? Start(string module, int wave);
    bool Playing(object handle);
    void Stop(object handle);
}

/// <summary>
/// The tactical sound manager. Request(n, q) takes a serial and the request time; n above 316 is dropped. The module is
/// TACTICAL.DLL for n below BattleStart, else the local side's voice module (VOICEFXA.DLL for side index 0, VOICEFXE.DLL otherwise).
/// Classes: effects 0 to 31 (2: at most 5 playing, a pending request dropped after 225 ms), voices BattleStart to RunInProgress (1: 1 playing,
/// 8000 ms, or 32000 ms while paused), run commentary RunCommentary to 316 (3: 1 playing, 3000 ms), independent of each other. A request
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
    public static int Class(int n) => n < 32 ? 2 : n < 286 ? 1 : 3;

    static int Limit(int c) => c == 2 ? 5 : 1;

    double Timeout(int c) => c switch { 2 => 225, 3 => 3000, _ => Paused ? 32000 : 8000 };

    /// <summary>Variants of an effect id: 4 for 0-2, 2 for 3-5, 3 for 9-19, 1 for the rest (voices too).</summary>
    public static int Variants(int n) => n switch { <= 2 => 4, <= 5 => 2, >= 9 and <= 19 => 3, _ => 1 };

    /// <summary>
    /// Module and WAVE of request n, variant v: effects are TACTICAL 13000 plus the table base (ids in order, variants contiguous);
    /// voices BattleStart to BattleEndS3 map to 14001 + (n - BattleStart), BattleStartEmpire to 316 map to 15001 + (n - BattleStartEmpire), in the local side's voice module.
    /// </summary>
    public static (string Module, int Wave) Resource(int n, int v, int local)
    {
        if (n < 32)
        {
            int b = 13000;
            for (int i = 0; i < n; i++) b += Variants(i);
            return ("TACTICAL.DLL", b + v);
        }
        return (local == 0 ? "VOICEFXA.DLL" : "VOICEFXE.DLL", n <= 153 ? 14001 + (n - 32) : 15001 + (n - 154));
    }

    /// <summary>Request(n, q): the serial of the request (started or queued), null when dropped.</summary>
    public int? Play(int n, bool queue, double now)
    {
        if (n is < 0 or > 316) return null;
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

/// <summary>
/// Sound-effect request numbers (ids 0 to 31, <see cref="TacticalBattle.Sounds"/>). The numbers are the original's; the names are
/// the sites that request them. Meaning in the original is unknown beyond these uses.
/// </summary>
public static class TacticalEffectSound
{
    /// <summary>Laser shot: a capital ship's sound; a fighter's is LaserFighter + side index (3 Alliance, 4 Empire).</summary>
    public const int LaserCapital = 0, LaserFighter = 3;
    /// <summary>Turbolaser and torpedo shots.</summary>
    public const int TurbolaserShot = 1;
    /// <summary>Ion shot: a capital ship's sound; a fighter's is IonFighter.</summary>
    public const int IonCapital = 2, IonFighter = 5;
    /// <summary>Hits on a shield: laser, the other shots (turbolaser, torpedo), and ion.</summary>
    public const int LaserShieldHit = 13, OtherShieldHit = 12, IonShieldHit = 14;
    /// <summary>Hits on the hull: laser and the other shots.</summary>
    public const int LaserHullHit = 18, OtherHullHit = 17;
    /// <summary>Ion overflow (shield broken by an ion shot).</summary>
    public const int IonOverflowHit = 19;
    /// <summary>Superlaser fired.</summary>
    public const int SuperlaserFire = 20;
    /// <summary>Tractor beam started (first holder) and released (last holder).</summary>
    public const int TractorHold = 6, TractorRelease = 7;
    /// <summary>Fighter launched from a hangar.</summary>
    public const int FighterLaunch = 27;
    /// <summary>Jump-in intro, for each unit of the local side at the start; the jump-out (hyperspace) start.</summary>
    public const int FighterJumpIn = 28, CapitalJumpIn = 30, FighterJumpOut = 29, CapitalJumpOut = 31;
    /// <summary>Explosion of a destroyed unit, by hull maximum: small (fighters and hull below 1100), medium (to 2000), large.</summary>
    public const int ExplosionSmall = 9, ExplosionMedium = 10, ExplosionLarge = 11;
    /// <summary>Selection of a unit: fighters by side, capital ships by hull maximum (2000 and above, 1100 and above, below).</summary>
    public const int SelectionFighterEmpire = 21, SelectionFighterAlliance = 22;
    public const int SelectionCapitalLarge = 23, SelectionCapitalMedium = 24, SelectionCapitalSmall = 25;
}
