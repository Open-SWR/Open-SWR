namespace OpenSWR.Sim;

/// <summary>
/// The Death Star run mission object, created by the 0x0A handler.
/// Fields: done (<see cref="TacticalMission.Done"/>), init done (<see cref="TacticalMission.Started"/>), run state, timer, request pending, nine voice times and ids, and the voice index.
/// While it lives, the global run flag is set (<see cref="TacticalBattle.DeathStarRun"/>).
/// </summary>
public sealed class TacticalRun : TacticalMission
{
    internal TacticalRun() : base(6) { }
    /// <summary>Run state: 2 flying, 5 run, 6 success, 7 failure, 8 abort, 9 success pending, 10 failure pending.</summary>
    public int State { get; internal set; }
    /// <summary>Timer (ms).</summary>
    public double Timer { get; internal set; }
    /// <summary>Request pending.</summary>
    public bool Pending { get; internal set; }
    internal readonly double[] Times = new double[9];
    internal readonly int[] Ids = new int[9];
    internal int VoiceIndex;
}

public sealed partial class TacticalUnit
{
    /// <summary>Hidden (a Death Star run member out of its spatial tree, not drawn).</summary>
    public bool Hidden { get; internal set; }
    /// <summary>Saved hull and shield while the run members are hidden.</summary>
    internal double SavedHull, SavedShield;
}

public sealed partial class TacticalUi
{
    /// <summary>
    /// The Death Star panel: created with the tactical main window when the local side is index 1 and a scene
    /// exists; removed by the mode-0x11 refresh, the Empire's jump start (0x45), a run destroying the scene and command 0x7A; never
    /// recreated.
    /// </summary>
    public bool DeathStarPanel { get; internal set; }
    /// <summary>The panel's bar value: trunc(charge), passed every frame while the scene is alive; the button 0xDE02 is enabled at 100.</summary>
    public int PanelValue { get; internal set; }
    /// <summary>The superlaser target flag: cursor 0x2A instead of 0x2B while mode 0x11 is current.</summary>
    public bool TargetCursor => _mode is SuperlaserMode;
}

/// <summary>A superlaser beam (message 0x44) at the turn it ran: the beam from the Death Star scene to <paramref name="To"/>.</summary>
public readonly record struct TacticalSuperlaser(int Turn, TacticalUnit To);

/// <summary>
/// The Death Star in tactical battles:
/// the scene of the shared setup (alive / attackable, point (-W/2, 0, 0)), attack possible, the superlaser charge and fire (0x44,
/// message 3), the Alliance run (mission 6: object, 0x3C requests, hide, resolver, outcomes), the computer Alliance rule with side
/// the side flag, the scene withdraw 0x45.
/// </summary>
public sealed partial class TacticalBattle
{
    /// The current Death Star scene (the engine's scene): the last registered of the shared setup; none: null.
    internal BattleEngine.Unit? Scene => Sides[1].Scene;

    /// <summary>A Death Star scene exists.</summary>
    public bool HasScene => Scene is not null;

    /// Scene alive: finished, remaining hull not 0, not withdrawn.
    public bool SceneAlive => BattleEngine.Alive(Scene);

    /// Unfinished (the shipyard's Death Star; the display shows 5020 instead of 5010).
    public bool SceneUnfinished => Scene?.Unfinished == true;

    /// Withdrawn (set by message 0x45): the decal is removed at once, with no jump animation.
    public bool SceneWithdrawn => Scene?.Withdrawn == true;

    /// Remaining hull (0 = destroyed).
    public double SceneHull => Scene?.Hull ?? 0;

    /// The scene's gameplay position: -0.5*W*(-F1.z, 0, F1.x) = (-W/2, 0, 0).
    public V3 ScenePoint => new(-Size / 2, 0, 0);

    /// <summary>Superlaser beams (message 0x44) as they ran (the front end draws them).</summary>
    public List<TacticalSuperlaser> Superlasers { get; } = [];

    /// <summary>
    /// An outcome movie requested by the run (201 = MDATA.201 success, 202 = MDATA.202 failure) with the world paused; the
    /// front end plays it (absent file: skipped), then clears it and resumes (Inferred: the pause counter).
    /// </summary>
    public int? Movie { get; set; }

    /// <summary>
    /// Attack possible: the scene attackable (alive, no shield facility), no run in progress (the global run flag is 0),
    /// more fighters of side index 0 than of side index 1 in the taking-part list (any state held there), and no group of side index
    /// 0's group list on UI or simulation mission 6.
    /// </summary>
    public bool AttackPossible
    {
        get
        {
            if (!SceneAlive || Scene!.Shielded || DeathStarRun) return false;
            int Fighters(int side) => _part.Count(u => u.Side == side && u.Fighter);
            return Fighters(0) > Fighters(1) && !RunGroup();
        }
    }

    /// <summary>Some group of side index 0's group list has UI mission or simulation mission 6.</summary>
    bool RunGroup() => _groups.Any(g => g.Side == 0 && Sides[0].Groups.Contains(g.G) && (g.UiMission == 6 || g.Mission == 6));

    /// <summary>The Death Star bonus 20000000.0 of the tactical strength: while the scene is alive, side index 1 always, side index 0 when the attack is possible or a group of it is on mission 6.</summary>
    bool SceneBonus(int side) => SceneAlive && (side == 1 || AttackPossible || RunGroup());

    // ---- charge and superlaser ----

    /// <summary>
    /// Charge (step 1000) in side index 1's turn, human or computer, any side state, when a scene exists:
    /// only with remaining hull and finished (a withdrawn or shielded scene still charges): charge becomes min(charge + f32(1000 *
    /// 0.33333334 * 0.001), 100.0), stored in f32 (300 turns from 0 to exactly 100.0). Crossing 100 with a local Empire: request 0x113.
    /// </summary>
    void Charge()
    {
        if (Scene is not { } sc || !(sc.Hull > 0) || sc.Unfinished) return;
        float inc = 1000f * 0.33333334f * 0.001f, before = sc.Charge;
        sc.Charge += inc;
        if (sc.Charge >= 100f) sc.Charge = 100f;
        if (before < 100f && sc.Charge >= 100f && Local == 1) Ui.Voices.Add(0x113);
    }

    /// <summary>
    /// Superlaser fire on target, no test inside: charge set to 0; post 0x44 (display: the target active, so the beam, sound 0x14
    /// and voice 0x91 for an Alliance player); insert message 3 with stamp T + 1, flag 1: the target, when still active, is
    /// destroyed (plain destroy: no draw, no shield or crit step; the death effect w = 4 and the explosion sound), else nothing.
    /// </summary>
    internal void FireSuperlaser(TacticalUnit target)
    {
        Scene!.Charge = 0;
        Post(Stamp, 0x44, () =>
        {
            if (!target.Active) return;
            Sounds.Add(0x14);
            Superlasers.Add(new TacticalSuperlaser(Turn, target)); // the superlaser flag has no reader
            if (Local == 0) Ui.Voices.Add(0x91);
        });
        Insert(Stamp + 1, 3, target, () =>
        {
            if (!target.Active) return;
            Death(target, 4);
            Destroy(target);
        });
    }

    /// <summary>
    /// Computer Empire (after the targeting and the withdraw decision): scene alive and charge exactly 100.0.
    /// the first entry of the candidate list that is a capital ship and active gets the superlaser; none means the charge stays.
    /// A shielded scene fires too.
    /// </summary>
    void ComputerSuperlaser()
    {
        if (!SceneAlive || Scene!.Charge != 100f) return;
        if (_candidates[1].Select(u => _byUnit[u]).FirstOrDefault(t => !t.Fighter && t.Active) is { } t) FireSuperlaser(t);
    }

    // ---- the computer Alliance ----

    /// <summary>
    /// Side index 0's Death Star case: scene alive, side flag 0, no run in progress; attack not possible means
    /// withdraw when its unit list is empty and it may retreat; attack possible means the first unit of the list whose simulation
    /// group is not on UI mission 6: side flag set to 1, then mission 6 through the UI setter (a refusal leaves the flag at 1, as read).
    /// </summary>
    void ComputerRun()
    {
        if (!SceneAlive || _deathStarGiven[0] || DeathStarRun) return;
        if (!AttackPossible)
        {
            if (Sides[0].Fighters.Count == 0 && RetreatPossible(0)) Withdraw(0);
            return;
        }
        if (Sides[0].Fighters.FirstOrDefault() is not { } u || _byUnit[u].Group is not { } g || g.UiMission == 6) return;
        _deathStarGiven[0] = true;
        SetMission(g, 6);
    }

    // ---- scene withdraw ----

    /// <summary>
    /// Side index 1 reaching state 3 with a live scene: a local Empire's UI queues drained and refreshed once (mode 0x11
    /// left) and the Death Star panel removed; post 0x45 (handler: if the scene is alive, withdrawn is set, and the scene
    /// hangar's launchable fighters not in state 3 taken along into its landed list in state 5; the Death Star decal goes at once, no jump animation).
    /// </summary>
    void SceneWithdraw()
    {
        if (Local == 1)
        {
            Ui.Drain();
            Ui.DeathStarPanel = false;
        }
        var sc = Scene!;
        Post(Stamp, 0x45, () =>
        {
            if (!BattleEngine.Alive(sc)) return;
            sc.Withdrawn = true;
            if (FindHangar(sc) is { } h) TakeAlong(h);
        });
    }

    // ---- the Alliance run, mission 6 ----

    static readonly int[] RunVoices = [0x139, 0x12D, 0x131, 0x135]; // start voice by group colour; + 1 run, + 2 failure

    /// The 0x0A handler's mission-6 object (the global run flag is set while it lives; request state 2).
    TacticalRun NewRun(TacticalGroup g)
    {
        var r = new TacticalRun();
        Request(g, r, 2);
        return r;
    }

    /// Request run state s: pending set; post 0x3C(group, s), flag 1 (Inferred: no dedupe, a plain post).
    void Request(TacticalGroup g, TacticalRun r, int s)
    {
        r.Pending = true;
        PostRun(g, s);
    }

    /// Post 0x3C(group, s) at the dispatch: if the group exists, has a mission object and simulation mission 6, set the state; else dropped.
    void PostRun(TacticalGroup g, int s) => Post(Stamp, 0x3C, () =>
    {
        if (_groups.Contains(g) && g.Object is TacticalRun r && g.Mission == 6) SetRunState(g, r, s);
    });

    /// Set state s: state becomes s, pending cleared; 5 run, 6 success, 7 failure, 8 abort; others nothing more.
    void SetRunState(TacticalGroup g, TacticalRun r, int s)
    {
        int old = r.State;
        (r.State, r.Pending) = (s, false);
        switch (s)
        {
            case 5: RunStart(g, r); break;
            case 6: RunSuccess(g); break;
            case 7: RunFailure(g); break;
            case 8:
                if (old is not (2 or 1)) foreach (var m in g.Members.ToList()) Restore(m);
                MissionMessage(g, 3); // direct handler call: object deleted, global run flag cleared, withdraw
                break;
        }
    }

    /// <summary>
    /// Run step (called every turn by the group step, step 1000): first call init (state 2, scratch 0, voice index 0, the start
    /// voice by colour for an Alliance player, 0x115 for an Empire player); not pending and state 9 / 10: one voice of the
    /// schedule when due, timer down by 1000, at 0 or below request 6 (from 9) or 7 (from 10) and timer set to 120000.
    /// </summary>
    void RunStep(TacticalGroup g, TacticalRun r)
    {
        if (!r.Started)
        {
            r.Started = true;
            (r.State, r.Scratch, r.VoiceIndex) = (2, 0, 0);
            Ui.Voices.Add(Local == 0 ? RunVoices[Math.Max(0, g.Colour) % 4] : 0x115);
        }
        if (r.Pending || r.State is not (9 or 10)) return;
        if (r.VoiceIndex < 9 && r.Timer <= r.Times[r.VoiceIndex])
        {
            if (r.Ids[r.VoiceIndex] != 0x13D) Ui.Voices.Add(r.Ids[r.VoiceIndex]);
            r.VoiceIndex++;
        }
        r.Timer -= TurnMs;
        if (r.Timer > 0) return;
        Request(g, r, r.State == 9 ? 6 : 7);
        r.Timer = 120000;
    }

    /// <summary>
    /// Mission-6 flight branch of the fighter group per frame: navigator heading set to (-F0.z, 0, F0.x); only in
    /// run state 2 the fighter mover toward 0.5*W*heading = (-W/2, 0, 0), r = 0.2; arrived and not done: mark done, request 5.
    /// </summary>
    void RunFrame(TacticalGroup g, double dt)
    {
        var n = g.Nav!;
        var f = Forward(0);
        n.Heading = new V3(-f.Z, 0, f.X);
        if (g.Object is not TacticalRun { State: 2 } r) return;
        if (FighterMove(n, n.Heading * (0.5 * Size), dt, 0.2) || r.Done) return;
        r.Done = true;
        Request(g, r, 5);
    }

    /// <summary>
    /// State 5: hide every member (out of its tree, saved hull / shield, state 10, hidden), timer 120000, the nine voice
    /// times (Alliance player) and the run voice; then the resolver at once.
    /// </summary>
    void RunStart(TacticalGroup g, TacticalRun r)
    {
        foreach (var m in g.Members)
        {
            (m.SavedHull, m.SavedShield) = (m.U.Hull, m.U.Shield);
            m.U.State = 10;
            m.Hidden = true;
        }
        r.Timer = 120000;
        if (Local == 0)
        {
            r.VoiceIndex = 0;
            for (int i = 0; i < 9; i++) r.Times[i] = 120000 - 12000 * i;
            Ui.Voices.Add(RunVoices[Math.Max(0, g.Colour) % 4] + 1);
        }
        else Ui.Voices.Add(0x11D); // the nine times stay uninitialised (as read; here 0)
        Resolve(g, r);
    }

    /// <summary>
    /// Resolver: Q = the members by ascending key; per visit U = Q's first: a = trunc(agility), r1 =
    /// rand(1, 10) &gt; a means hit (t = trunc(hull / 8): t &lt;= 1 gives 100000.0, else r2 = rand(1, t), r2 * 8.0, then 100000.0 when emptied);
    /// hull &gt; 0: v = torpedo value + min(commander bonus, 9), r3 = rand(1, 100) &lt;= trunc(v * 0.5) means success; visit 1001 means
    /// failure; a surviving U goes back to Q's front (lowest key), so the same squadron is visited again; Q empty means failure.
    /// </summary>
    void Resolve(TacticalGroup g, TacticalRun r)
    {
        int[] ids = [0x11E, 0x11F, 0x120, 0x128, 0x121, 0x122, 0x129, 0x13D, 0x13D];
        ids.CopyTo(r.Ids, 0);
        bool hit = false;
        int visits = 0;
        var q = g.Members.OrderBy(m => m.Key).ToList();
        while (q.Count > 0)
        {
            var u = q[0];
            q.RemoveAt(0);
            bool keep = false;
            int a = (int)Math.Truncate(Maneuverability(u));
            if (_between(1, 10) > a)
            {
                hit = true;
                int t = (int)Math.Truncate(u.U.Hull / 8.0);
                if (t <= 1) Take(u, 100000.0);
                else
                {
                    Take(u, _between(1, t) * 8.0);
                    if (u.U.Hull <= 0) Take(u, 100000.0);
                }
            }
            if (u.U.Hull > 0)
            {
                keep = true;
                double b = Math.Min(Commander(0), 9.0), v = u.U.Hf * u.U.Torpedo * Torpedo + b;
                if (_between(1, 100) <= Math.Truncate(v * 0.5))
                {
                    if (hit) new[] { 0x123, 0x124, 0x125, 0x12A, 0x12B, 0x126 }.CopyTo(r.Ids, 1);
                    (r.Pending, r.State, r.Timer) = (false, 9, 120000);
                    foreach (var m in g.Members) (m.U.Hull, m.U.Shield) = (m.SavedHull, m.SavedShield);
                    return;
                }
            }
            if (++visits > 1000) break;
            if (keep) q.Insert(0, u);
        }
        new[] { 0x123, 0x124, 0x125, 0x12A, 0x12B, 0x126, 0x12C, 0x127 }.CopyTo(r.Ids, 1);
        (r.Pending, r.Timer, r.State) = (false, 120000, 10);
    }

    /// <summary>The 0x3C handler's set state at once (tests: without the turn latency).</summary>
    internal void RunTo(TacticalGroup g, int s) => SetRunState(g, (TacticalRun)g.Object!, s);

    /// <summary>Posts 0x3C(group, s) (tests: a request at a chosen turn).</summary>
    internal void RequestRun(TacticalGroup g, int s) => PostRun(g, s);

    /// "Takes d": hull &lt;= 0 means hull set to 0; else shield set to 0 and hull set to max(hull - d, 0). No crit, no destroy, no draw.
    static void Take(TacticalUnit t, double d)
    {
        var u = t.U;
        if (u.Hull <= 0) { u.Hull = 0; return; }
        u.Shield = 0;
        u.Hull = Math.Max(u.Hull - d, 0);
    }

    /// Restore: hull and shield set back to the saved values, state 2, back into its tree, visible.
    static void Restore(TacticalUnit t)
    {
        if (t.State != 10) return; // Inferred: only hidden run members (every member of a started run is)
        (t.U.Hull, t.U.Shield, t.U.State, t.Hidden) = (t.SavedHull, t.SavedShield, 2, false);
    }

    /// <summary>
    /// State 6: the first n div 2 members take 100000.0 and are destroyed, the others restored; a scene gets charge 0, remaining
    /// hull 0 (destroyed), every fighter of its hangar's launchable and landed lists destroyed, an Empire player's Death Star panel
    /// removed; world paused with movie MDATA.201; an Alliance player's group gets its fighter button back; mission cleared by a direct
    /// handler call (object deleted, global run flag cleared). No alive test: a withdrawn scene is destroyed too (as read).
    /// </summary>
    void RunSuccess(TacticalGroup g)
    {
        var members = g.Members.ToList();
        int k = members.Count / 2;
        for (int i = 0; i < members.Count; i++)
        {
            if (i < k)
            {
                Take(members[i], 100000.0);
                Destroy(members[i]);
            }
            else Restore(members[i]);
        }
        if (Scene is { } sc)
        {
            (sc.Charge, sc.Hull) = (0, 0); // the decal is deleted at once (no explosion)
            if (FindHangar(sc) is { } h)
                foreach (var f in h.Launchable.Concat(h.Landed).ToList()) Destroy(f);
            if (Local == 1) Ui.DeathStarPanel = false;
        }
        (Paused, Movie) = (true, 201);
        if (Local == 0 && g.Colour >= 0) Ui.Fighters[g.Colour] = g;
        MissionMessage(g, 0);
    }

    /// <summary>
    /// State 7: an Alliance player's failure voice by colour; world paused with movie MDATA.202; then every member destroyed
    /// (the current first member, n times); each leaves its group (UI and simulation) and the emptied group is deleted
    /// with its mission object (Inferred: group deletion deletes the object; the global run flag is cleared).
    /// </summary>
    void RunFailure(TacticalGroup g)
    {
        if (Local == 0) Ui.Voices.Add(RunVoices[Math.Max(0, g.Colour) % 4] + 2);
        (Paused, Movie) = (true, 202);
        foreach (var m in g.Members.ToList())
        {
            Take(m, 100000.0);
            Destroy(m); // world remove: out of the group
            g.UiMemberList.Remove(m);
            if (m.UiGroup == g) m.UiGroup = null;
        }
        DeleteGroup(g);
    }
}
