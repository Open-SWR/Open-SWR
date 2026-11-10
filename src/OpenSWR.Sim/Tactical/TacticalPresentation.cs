namespace OpenSWR.Sim;

/// <summary>An effect request: weapon w (0 laser, 1 turbolaser, 2 ion, 3 torpedo, 4 superlaser / scuttle), kind k (0 spark, 1 hull explosion, 2 death).</summary>
public readonly record struct TacticalEffect(TacticalUnit Unit, int Weapon, int Kind);

/// <summary>
/// Presentation of a tactical battle: sound-effect
/// requests (<see cref="Sounds"/>, ids 0-0x1F) and voice requests (<see cref="TacticalUi.Voices"/>) at their sites,
/// effect requests (<see cref="Effects"/>), the kill / nav / target-destroyed voices, the battle-end voice, the Death Star
/// hints, the jump-in intro and the drawn jump offsets. Nothing here draws from the battle's generator or changes
/// the simulation; the front end drains the lists.
/// </summary>
public sealed partial class TacticalBattle
{
    /// <summary>Sound-effect requests (ids 0-0x1F) in the order the sites ran (all q = 1).</summary>
    public List<int> Sounds { get; } = [];

    /// <summary>Effect requests of capital ships (a fighter's are kind 3 = nothing, so none are listed).</summary>
    public List<TacticalEffect> Effects { get; } = [];

    void Effect(TacticalUnit p, int w, int k)
    {
        if (!p.Fighter) Effects.Add(new TacticalEffect(p, w, k));
    }

    /// The death effect (kind 2), then the explosion sound.
    void Death(TacticalUnit p, int w)
    {
        Effect(p, w, 2);
        Sounds.Add(Explosion(p));
    }

    /// Explosion sound: 9 a fighter or a capital ship of hull maximum (Inferred) &lt; 1100; 0xA up to 2000; 0xB above.
    internal static int Explosion(TacticalUnit t) => t.Fighter || t.HullMax < 1100 ? 9 : t.HullMax <= 2000 ? 0xA : 0xB;

    /// The selection sound of a unit: fighter 0x16 side index 0 / 0x15 side index 1; capital ship 0x17 hull maximum >= 2000, 0x18 >= 1100, else 0x19.
    public static int Selection(TacticalUnit t) => t.Fighter ? (t.Side == 0 ? 0x16 : 0x15) : t.HullMax >= 2000 ? 0x17 : t.HullMax >= 1100 ? 0x18 : 0x19;

    void Voice(int alliance, int empire) => Ui.Voices.Add(Local == 0 ? alliance : empire);

    /// <summary>A fighter group's c: its fighter button slot, else its colour (Inferred: the UI group of the unit).</summary>
    int FighterSlot(TacticalGroup g) => Array.IndexOf(Ui.Fighters, g) is >= 0 and var c ? c : g.Colour;

    /// <summary>
    /// Kill voices (target, shooter), every kill: shooter on the local side gives a fighter with a group 0x5E + c, a
    /// capital ship in numbered task force k 0x56 + k, else 0x55; otherwise (we lost the target) a fighter with a group 0x6B + c; a
    /// capital ship with a group: 0x86 per character aboard, then 0x63 + k (numbered) or 0x62; no group 0x62. Groups as the UI knows
    /// them (Inferred).
    /// </summary>
    void KillVoices(TacticalUnit target, TacticalUnit shooter)
    {
        if (shooter.Side == Local)
        {
            if (shooter.Fighter)
            {
                if (shooter.UiGroup is { } fg) Voice(0x5E + FighterSlot(fg), 0xD8 + FighterSlot(fg));
            }
            else if (Ui.SlotOf(shooter.UiGroup) is >= 0 and var k) Voice(0x56 + k, 0xD0 + k);
            else Voice(0x55, 0xCF);
            return;
        }
        if (target.UiGroup is not { } g) { Voice(0x62, 0xDC); return; }
        if (target.Fighter) { Voice(0x6B + FighterSlot(g), 0xE5 + FighterSlot(g)); return; }
        foreach (var _ in target.U.Carried.OfType<Character>()) Voice(0x86, 0x104);
        if (Ui.SlotOf(g) is >= 0 and var slot) Voice(0x63 + slot, 0xDD + slot);
        else Voice(0x62, 0xDC);
    }

    /// <summary>
    /// Nav point reached: it left both nav lists, the group is the local side's, the local side is not computer-controlled
    /// and the group's UI target list is non-empty: task force 0x2F + k (numbered) / 0x2E, fighter group 0x37 + c.
    /// </summary>
    void ReachedVoice(TacticalGroup g)
    {
        if (g.Side != Local || Computer[Local] || g.UiTargetList.Count == 0) return;
        if (g.Fighters) Voice(0x37 + g.Colour, 0xB1 + g.Colour);
        else if (Ui.SlotOf(g) is >= 0 and var k) Voice(0x2F + k, 0xA9 + k);
        else Voice(0x2E, 0xA8);
    }

    /// <summary>
    /// The one-time notification of a group whose current target <paramref name="p"/> is inactive: on the
    /// local side P leaves the UI target list; then the list empty, the local side not computer-controlled, no mission object, the
    /// enemy's first unit of its lists (else the second) active and the enemy side not withdrawing: 0x48 / 0x49 + k / 0x51 + c.
    /// </summary>
    void TargetLost(TacticalGroup g, TacticalUnit p)
    {
        g.Notified = true;
        if (g.Side != Local) return;
        g.UiTargetList.Remove(p);
        var enemy = Sides[1 - Local];
        var first = enemy.Ships.FirstOrDefault() ?? enemy.Fighters.FirstOrDefault();
        if (g.UiTargetList.Count > 0 || Computer[Local] || g.Object is not null || first is null || !_byUnit[first].Active || _state[1 - Local] != 0) return;
        if (g.Fighters) Voice(0x51 + g.Colour, 0xCB + g.Colour);
        else if (Ui.SlotOf(g) is >= 0 and var k) Voice(0x49 + k, 0xC3 + k);
        else Voice(0x48, 0xC2);
    }

    /// <summary>
    /// Battle-end voice (once, right after S): local Alliance S[1] and S[2] 0x97, S[0] 0x96, S[1] 0x98, S[3] 0x99; local
    /// Empire S[3] and S[0] 0x119, S[2] 0x118, S[3] 0x11A, S[1] 0x11B; else none.
    /// </summary>
    void EndVoice()
    {
        int? n = Local == 0
            ? S[1] != 0 && S[2] != 0 ? 0x97 : S[0] != 0 ? 0x96 : S[1] != 0 ? 0x98 : S[3] != 0 ? 0x99 : null
            : S[3] != 0 && S[0] != 0 ? 0x119 : S[2] != 0 ? 0x118 : S[3] != 0 ? 0x11A : S[1] != 0 ? 0x11B : null;
        if (n is { } v) Ui.Voices.Add(v);
    }

    // ---- Death Star hints ----

    int _hintState, _hintPrev;
    double _hintTimer;
    readonly int[] _hintVoices = new int[4];

    /// Hint state (0-3 opening, 1000 waiting, 4 monitor).
    public int HintState => _hintState;

    /// <summary>
    /// The hint machine, every running frame with the wall-clock frame ms (also while paused, Inferred): 1000 waits t,
    /// then the monitor (4: "attack possible" falling gives 0x94 for an Alliance player when both sides have fighters, no run is in progress and no side-index-0 group is on mission 6; rising gives 0x92 /
    /// 0x11C); the opening (0-3, not during the intro): every 6000 ms the next line of [0x20, 0x90, 0x93, 0x94] (Empire: 0x9A only).
    /// </summary>
    public void Hints(double ms)
    {
        if (Done) return;
        if (_hintState == 1000)
        {
            _hintTimer -= ms;
            if (_hintTimer > 0) return;
            _hintState = 4;
        }
        if (_hintState == 4)
        {
            int p = AttackPossible ? 1 : 0;
            if (_hintPrev == 1 && p == 0 && Local == 0 && Sides[0].Fighters.Count > 0 && Sides[1].Fighters.Count > 0 && !DeathStarRun && !RunGroup()) Ui.Voices.Add(0x94);
            if (_hintPrev == 0 && p == 1)
            {
                if (Local == 0) Ui.Voices.Add(0x92);
                else if (Sides[0].Fighters.Count > 0) Ui.Voices.Add(0x11C);
            }
            _hintPrev = p;
            return;
        }
        if (_hintState == 0)
        {
            if (InIntro) return;
            if (Local == 0) (_hintVoices[0], _hintVoices[1], _hintVoices[2], _hintVoices[3]) = (0x20, 0x90, 0x93, 0x94);
            else _hintVoices[0] = 0x9A;
        }
        _hintTimer -= ms;
        if (_hintTimer > 0) return;
        _hintTimer = 6000;
        Ui.Voices.Add(_hintVoices[_hintState]);
        _hintState = _hintState switch
        {
            0 => Local == 0 && SceneAlive ? 1 : 1000,
            1 when SceneAlive && Scene!.Shielded => 2,
            1 when Sides[0].Fighters.Count > 0 && !AttackPossible && !DeathStarRun
                && _part.Count(u => u.Side == 0 && u.Fighter && u.State != 10) <= _part.Count(u => u.Side == 1 && u.Fighter) => 3,
            _ => 1000,
        };
    }

    // ---- jump-in intro and jump-out ----

    const double IntroLength = 1500;
    double _intro = IntroLength; // world state 2 clock; IntroLength = over (no intro)

    /// <summary>World state 2 (the jump-in intro): no turns, the UI observing.</summary>
    public bool InIntro => _intro < IntroLength;

    /// <summary>
    /// Starts the intro: the UI is taken out of command; every non-docked member of every group of the local side plays
    /// 0x1C (fighter) / 0x1E (capital ship) and is shown at t = 0.
    /// </summary>
    public void StartIntro()
    {
        _intro = 0;
        Ui.InCommand = false;
        foreach (var g in _groups.Where(g => g.Side == Local))
            foreach (var u in g.Members.Where(u => u.State != 1)) Sounds.Add(u.Fighter ? 0x1C : 0x1E);
    }

    /// <summary>Ends the intro at once (front-end demos).</summary>
    public void SkipIntro()
    {
        if (!InIntro) return;
        _intro = IntroLength;
        Ui.InCommand = !Computer[Local];
    }

    /// Intro clock: + min(ms * 0.4, 200) (the cap is on the product); at 1500 world state 4 and the UI in command (unless the local side is computer-controlled).
    void IntroStep(double ms)
    {
        _intro = Math.Min(IntroLength, _intro + Math.Min(ms * 0.4, 200));
        if (!InIntro) Ui.InCommand = !Computer[Local];
    }

    /// <summary>Lane L = (unit key + type index) modulo 4.</summary>
    static int Lane(TacticalUnit u) => (u.Key + u.TypeIndex) % 4; // keys are positive

    /// <summary>
    /// The drawn intro offset of a unit: local side, in a group, not docked: F * (d * (t / T)^2 - d), 0 once t >= T; lanes 0-3:
    /// d 400 / 575 / 650 / 325, T 1.5 / 1.25 / 1.425 / 1.225 s of the intro clock.
    /// </summary>
    public V3 IntroOffset(TacticalUnit u)
    {
        if (!InIntro || u.Side != Local || u.Group is null || u.State == 1) return V3.Zero;
        var (d, T) = Lane(u) switch { 0 => (400.0, 1.5), 1 => (575.0, 1.25), 2 => (650.0, 1.425), _ => (325.0, 1.225) };
        double t = _intro / 1000;
        return t >= T ? V3.Zero : Forward(u.Side) * (d * (t / T) * (t / T) - d);
    }

    /// The drawn jump-out offset (state 7): along -F, k_L * (t / 1000)^2, t = the hyperspace timer ( &lt;= 4000 ms); k = 400 / 4.0^2, 575 / 3.75^2, 650 / 3.925^2, 325 / 3.725^2.
    public V3 JumpOffset(TacticalUnit u)
    {
        if (u.State != 7) return V3.Zero;
        double k = Lane(u) switch { 0 => 400 / (4.0 * 4.0), 1 => 575 / (3.75 * 3.75), 2 => 650 / (3.925 * 3.925), _ => 325 / (3.725 * 3.725) };
        double s = u.JumpTimer / 1000;
        return Forward(u.Side) * -(k * s * s);
    }

    /// Gravity-well loop: an active (state 2) unit with its blocker flag set (a GravityWell ship).
    public bool GravityLoop(TacticalUnit u) => u.State == 2 && u.U.Blocker;
}
