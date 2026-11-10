namespace OpenSWR.Sim;

/// <summary>An effect request: weapon w (0 laser, 1 turbolaser, 2 ion, 3 torpedo, 4 superlaser / scuttle), kind k (0 spark, 1 hull explosion, 2 death).</summary>
public readonly record struct TacticalEffect(TacticalUnit Unit, TacticalWeapon Weapon, TacticalEffectKind Kind);

/// <summary>
/// Presentation of a tactical battle: sound-effect
/// requests (<see cref="Sounds"/>, ids 0 to 31) and voice requests (<see cref="TacticalUi.Voices"/>) at their sites,
/// effect requests (<see cref="Effects"/>), the kill / nav / target-destroyed voices, the battle-end voice, the Death Star
/// hints, the jump-in intro and the drawn jump offsets. Nothing here draws from the battle's generator or changes
/// the simulation; the front end drains the lists.
/// </summary>
public sealed partial class TacticalBattle
{
    /// <summary>Sound-effect requests (ids 0 to 31) in the order the sites ran (all q = 1).</summary>
    public List<int> Sounds { get; } = [];

    /// <summary>Effect requests of capital ships (a fighter's are kind 3 = nothing, so none are listed).</summary>
    public List<TacticalEffect> Effects { get; } = [];

    void Effect(TacticalUnit p, TacticalWeapon w, TacticalEffectKind k)
    {
        if (!p.Fighter) Effects.Add(new TacticalEffect(p, w, k));
    }

    /// The death effect (kind 2), then the explosion sound.
    void Death(TacticalUnit p, TacticalWeapon w)
    {
        Effect(p, w, TacticalEffectKind.Death);
        Sounds.Add(Explosion(p));
    }

    /// Explosion sound: ExplosionSmall (9) a fighter or a capital ship of hull maximum (Inferred) &lt; 1100; ExplosionMedium (10) up to 2000; ExplosionLarge (11) above.
    internal static int Explosion(TacticalUnit t) => t.Fighter || t.HullMax < 1100 ? TacticalEffectSound.ExplosionSmall : t.HullMax <= 2000 ? TacticalEffectSound.ExplosionMedium : TacticalEffectSound.ExplosionLarge;

    /// The selection sound of a unit: fighter SelectionFighterAlliance side index 0 / SelectionFighterEmpire side index 1; capital ship SelectionCapitalLarge hull maximum >= 2000, SelectionCapitalMedium >= 1100, else SelectionCapitalSmall.
    public static int Selection(TacticalUnit t) => t.Fighter ? (t.Side == 0 ? TacticalEffectSound.SelectionFighterAlliance : TacticalEffectSound.SelectionFighterEmpire) : t.HullMax >= 2000 ? TacticalEffectSound.SelectionCapitalLarge : t.HullMax >= 1100 ? TacticalEffectSound.SelectionCapitalMedium : TacticalEffectSound.SelectionCapitalSmall;

    void Voice(int alliance, int empire) => Ui.Voices.Add(Local == 0 ? alliance : empire);

    /// <summary>A fighter group's c: its fighter button slot, else its colour (Inferred: the UI group of the unit).</summary>
    int FighterSlot(TacticalGroup g) => Array.IndexOf(Ui.Fighters, g) is >= 0 and var c ? c : g.Colour;

    /// <summary>
    /// Kill voices (target, shooter), every kill: shooter on the local side gives a fighter with a group FighterGroupKill + c, a
    /// capital ship in numbered task force k TaskForceKill + k, else EnemyDestroyed; otherwise (we lost the target) a fighter with a group FighterLost + c; a
    /// capital ship with a group: CharacterAboardLost per character aboard, then TaskForceShipLost + k (numbered) or UnitLost; no group UnitLost. Groups as the UI knows
    /// them (Inferred).
    /// </summary>
    void KillVoices(TacticalUnit target, TacticalUnit shooter)
    {
        if (shooter.Side == Local)
        {
            if (shooter.Fighter)
            {
                if (shooter.UiGroup is { } fg) Voice(TacticalVoice.FighterGroupKill + FighterSlot(fg), TacticalVoice.FighterGroupKillEmpire + FighterSlot(fg));
            }
            else if (Ui.SlotOf(shooter.UiGroup) is >= 0 and var k) Voice(TacticalVoice.TaskForceKill + k, TacticalVoice.TaskForceKillEmpire + k);
            else Voice(TacticalVoice.EnemyDestroyed, TacticalVoice.EnemyDestroyedEmpire);
            return;
        }
        if (target.UiGroup is not { } g) { Voice(TacticalVoice.UnitLost, TacticalVoice.UnitLostEmpire); return; }
        if (target.Fighter) { Voice(TacticalVoice.FighterLost + FighterSlot(g), TacticalVoice.FighterLostEmpire + FighterSlot(g)); return; }
        foreach (var _ in target.U.Carried.OfType<Character>()) Voice(TacticalVoice.CharacterAboardLost, TacticalVoice.CharacterAboardLostEmpire);
        if (Ui.SlotOf(g) is >= 0 and var slot) Voice(TacticalVoice.TaskForceShipLost + slot, TacticalVoice.TaskForceShipLostEmpire + slot);
        else Voice(TacticalVoice.UnitLost, TacticalVoice.UnitLostEmpire);
    }

    /// <summary>
    /// Nav point reached: it left both nav lists, the group is the local side's, the local side is not computer-controlled
    /// and the group's UI target list is non-empty: task force TaskForceAtNav + k (numbered) / UnnumberedTaskForceAtNav, fighter group FighterGroupAtNav + c.
    /// </summary>
    void ReachedVoice(TacticalGroup g)
    {
        if (g.Side != Local || Computer[Local] || g.UiTargetList.Count == 0) return;
        if (g.Fighters) Voice(TacticalVoice.FighterGroupAtNav + g.Colour, TacticalVoice.FighterGroupAtNavEmpire + g.Colour);
        else if (Ui.SlotOf(g) is >= 0 and var k) Voice(TacticalVoice.TaskForceAtNav + k, TacticalVoice.TaskForceAtNavEmpire + k);
        else Voice(TacticalVoice.UnnumberedTaskForceAtNav, TacticalVoice.UnnumberedTaskForceAtNavEmpire);
    }

    /// <summary>
    /// The one-time notification of a group whose current target <paramref name="p"/> is inactive: on the
    /// local side P leaves the UI target list; then the list empty, the local side not computer-controlled, no mission object, the
    /// enemy's first unit of its lists (else the second) active and the enemy side not withdrawing: UnnumberedTaskForceDone / TaskForceDone + k / FighterGroupDone + c.
    /// </summary>
    void TargetLost(TacticalGroup g, TacticalUnit p)
    {
        g.Notified = true;
        if (g.Side != Local) return;
        g.UiTargetList.Remove(p);
        var enemy = Sides[1 - Local];
        var first = enemy.Ships.FirstOrDefault() ?? enemy.Fighters.FirstOrDefault();
        if (g.UiTargetList.Count > 0 || Computer[Local] || g.Object is not null || first is null || !_byUnit[first].Active || _state[1 - Local] != TacticalSideState.Fighting) return;
        if (g.Fighters) Voice(TacticalVoice.FighterGroupDone + g.Colour, TacticalVoice.FighterGroupDoneEmpire + g.Colour);
        else if (Ui.SlotOf(g) is >= 0 and var k) Voice(TacticalVoice.TaskForceDone + k, TacticalVoice.TaskForceDoneEmpire + k);
        else Voice(TacticalVoice.UnnumberedTaskForceDone, TacticalVoice.UnnumberedTaskForceDoneEmpire);
    }

    /// <summary>
    /// Battle-end voice (once, right after S): local Alliance S[1] and S[2] BattleEndS1S2, S[0] BattleEnd, S[1] BattleEndS1, S[3] BattleEndS3; local
    /// Empire S[3] and S[0] BattleEndEmpireS3S0, S[2] BattleEndEmpire, S[3] BattleEndEmpireS3, S[1] BattleEndEmpireS1; else none.
    /// </summary>
    void EndVoice()
    {
        int? n = Local == 0
            ? S[1] != 0 && S[2] != 0 ? TacticalVoice.BattleEndS1S2 : S[0] != 0 ? TacticalVoice.BattleEnd : S[1] != 0 ? TacticalVoice.BattleEndS1 : S[3] != 0 ? TacticalVoice.BattleEndS3 : null
            : S[3] != 0 && S[0] != 0 ? TacticalVoice.BattleEndEmpireS3S0 : S[2] != 0 ? TacticalVoice.BattleEndEmpire : S[3] != 0 ? TacticalVoice.BattleEndEmpireS3 : S[1] != 0 ? TacticalVoice.BattleEndEmpireS1 : null;
        if (n is { } v) Ui.Voices.Add(v);
    }

    // ---- Death Star hints ----

    TacticalHintStep _hintState;
    int _hintPrev;
    double _hintTimer;

    /// <summary>Hint state: the opening lines, then Waiting and Monitor.</summary>
    public TacticalHintStep HintState => _hintState;

    /// <summary>
    /// The hint machine, every running frame with the wall-clock frame ms (also while paused, Inferred): Waiting counts down t,
    /// then the monitor (Monitor: "attack possible" falling gives TooFewFighters for an Alliance player when both sides have fighters, no run is in progress and no side-index-0 group is on mission 6; rising gives DeathStarAttackPossible /
    /// DeathStarAttackPossibleEmpire); the opening (the four opening lines, not during the intro): every 6000 ms the next line of [BattleStart, DeathStarPresent, DeathStarShielded, TooFewFighters] (Empire: BattleStartEmpire only).
    /// </summary>
    public void Hints(double ms)
    {
        if (Done) return;
        if (_hintState == TacticalHintStep.Waiting)
        {
            _hintTimer -= ms;
            if (_hintTimer > 0) return;
            _hintState = TacticalHintStep.Monitor;
        }
        if (_hintState == TacticalHintStep.Monitor)
        {
            int p = AttackPossible ? 1 : 0;
            if (_hintPrev == 1 && p == 0 && Local == 0 && Sides[0].Fighters.Count > 0 && Sides[1].Fighters.Count > 0 && !DeathStarRun && !RunGroup()) Ui.Voices.Add(TacticalVoice.TooFewFighters);
            if (_hintPrev == 0 && p == 1)
            {
                if (Local == 0) Ui.Voices.Add(TacticalVoice.DeathStarAttackPossible);
                else if (Sides[0].Fighters.Count > 0) Ui.Voices.Add(TacticalVoice.DeathStarAttackPossibleEmpire);
            }
            _hintPrev = p;
            return;
        }
        if (_hintState == TacticalHintStep.Opening && InIntro) return;
        _hintTimer -= ms;
        if (_hintTimer > 0) return;
        _hintTimer = 6000;
        Ui.Voices.Add(_hintState switch
        {
            TacticalHintStep.Opening => Local == 0 ? TacticalVoice.BattleStart : TacticalVoice.BattleStartEmpire,
            TacticalHintStep.DeathStarPresent => TacticalVoice.DeathStarPresent,
            TacticalHintStep.DeathStarShielded => TacticalVoice.DeathStarShielded,
            TacticalHintStep.TooFewFighters => TacticalVoice.TooFewFighters,
            _ => throw new InvalidOperationException("no opening line for hint step " + _hintState),
        });
        _hintState = _hintState switch
        {
            TacticalHintStep.Opening => Local == 0 && SceneAlive ? TacticalHintStep.DeathStarPresent : TacticalHintStep.Waiting,
            TacticalHintStep.DeathStarPresent when SceneAlive && Scene!.Shielded => TacticalHintStep.DeathStarShielded,
            TacticalHintStep.DeathStarPresent when Sides[0].Fighters.Count > 0 && !AttackPossible && !DeathStarRun
                && _part.Count(u => u.Side == 0 && u.Fighter && u.State != TacticalUnitState.DeathStarRun) <= _part.Count(u => u.Side == 1 && u.Fighter) => TacticalHintStep.TooFewFighters,
            _ => TacticalHintStep.Waiting,
        };
    }

    // ---- jump-in intro and jump-out ----

    const double IntroLength = 1500;
    double _intro = IntroLength; // world state 2 clock; IntroLength = over (no intro)

    /// <summary>World state 2 (the jump-in intro): no turns, the UI observing.</summary>
    public bool InIntro => _intro < IntroLength;

    /// <summary>
    /// Starts the intro: the UI is taken out of command; every non-docked member of every group of the local side plays
    /// FighterJumpIn (fighter) / CapitalJumpIn (capital ship) and is shown at t = 0.
    /// </summary>
    public void StartIntro()
    {
        _intro = 0;
        Ui.InCommand = false;
        foreach (var g in _groups.Where(g => g.Side == Local))
            foreach (var u in g.Members.Where(u => u.State != TacticalUnitState.Docked)) Sounds.Add(u.Fighter ? TacticalEffectSound.FighterJumpIn : TacticalEffectSound.CapitalJumpIn);
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
        if (!InIntro || u.Side != Local || u.Group is null || u.State == TacticalUnitState.Docked) return V3.Zero;
        var (d, T) = Lane(u) switch { 0 => (400.0, 1.5), 1 => (575.0, 1.25), 2 => (650.0, 1.425), _ => (325.0, 1.225) };
        double t = _intro / 1000;
        return t >= T ? V3.Zero : Forward(u.Side) * (d * (t / T) * (t / T) - d);
    }

    /// The drawn jump-out offset (state 7): along -F, k_L * (t / 1000)^2, t = the hyperspace timer ( &lt;= 4000 ms); k = 400 / 4.0^2, 575 / 3.75^2, 650 / 3.925^2, 325 / 3.725^2.
    public V3 JumpOffset(TacticalUnit u)
    {
        if (u.State != TacticalUnitState.Accelerating) return V3.Zero;
        double k = Lane(u) switch { 0 => 400 / (4.0 * 4.0), 1 => 575 / (3.75 * 3.75), 2 => 650 / (3.925 * 3.925), _ => 325 / (3.725 * 3.725) };
        double s = u.JumpTimer / 1000;
        return Forward(u.Side) * -(k * s * s);
    }

    /// Gravity-well loop: an active (state 2) unit with its blocker flag set (a GravityWell ship).
    public bool GravityLoop(TacticalUnit u) => u.State == TacticalUnitState.Active && u.U.Blocker;
}
