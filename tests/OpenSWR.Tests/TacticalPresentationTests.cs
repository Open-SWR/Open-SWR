using OpenSWR.Sim;
using static OpenSWR.Tests.TacticalTests;

namespace OpenSWR.Tests;

/// <summary>The sound manager, presentation events, voices, the Death Star hints, the intro.</summary>
public class TacticalPresentationTests
{
    /// <summary>A sound sink whose sounds play until the test finishes them; a WAVE outside <see cref="Have"/> is absent.</summary>
    sealed class Sink : ITacticalSoundSink
    {
        public readonly List<(string Module, int Wave)> Started = [];
        public readonly HashSet<object> Done = [];
        public Func<string, int, bool> Have = (_, _) => true;
        public object? Start(string module, int wave)
        {
            if (!Have(module, wave)) return null;
            Started.Add((module, wave));
            return Started.Count - 1;
        }
        public bool Playing(object handle) => !Done.Contains(handle);
        public void Stop(object handle) => Done.Add(handle);
        public void Finish(int i) => Done.Add(i);
    }

    /// <summary>Five effects at most, a sixth waits 225 ms (then dropped) and starts when one finishes; q = 0 drops at once.</summary>
    [Fact]
    public void EffectsPlayFiveAtATime()
    {
        var sink = new Sink();
        var picks = new List<int>();
        var m = new TacticalSounds(0, sink, n => { picks.Add(n); return n - 1; });
        for (int i = 0; i < 5; i++) Assert.NotNull(m.Play(0xC, true, 0));
        Assert.Equal(5, sink.Started.Count);
        Assert.Equal([3, 3, 3, 3, 3], picks); // one pick per start, 3 variants
        Assert.Equal(("TACTICAL.DLL", 13032), sink.Started[0]); // 0xC: 13030 + variant 2
        Assert.Null(m.Play(0xC, false, 10)); // q = 0, no room
        int? late = m.Play(0x1B, true, 10);
        Assert.NotNull(late);
        m.Update(200);
        Assert.Equal(5, sink.Started.Count);
        sink.Finish(0);
        m.Update(234); // 224 ms old: still waiting, starts
        Assert.Equal(("TACTICAL.DLL", 13061), sink.Started[^1]);
        Assert.True(m.IsPlaying(late!.Value));
        sink.Finish(1);
        m.Play(0x1B, true, 300); // room: starts at once
        m.Play(0x1B, true, 300); // full again: queued
        sink.Finish(2);
        m.Update(526); // 226 ms: stale, dropped
        Assert.Equal(7, sink.Started.Count);
        Assert.Equal(5, picks.Count); // 0x1B has one variant: no pick; a start without room picks nothing
    }

    /// <summary>
    /// Selection sound: a request dropped for room is retried every frame; a pause that cuts it off clears the
    /// handle and it plays again from the start on the first unpaused frame; a finished one is not repeated.
    /// </summary>
    [Fact]
    public void SelectionSoundRetriesReplaysAfterAPauseAndNeverRepeats()
    {
        var sink = new Sink();
        var m = new TacticalSounds(0, sink, _ => 0);
        for (int i = 0; i < 5; i++) m.Play(0x1B, false, 0); // the effect class is full
        int? h = m.Selection(null, 0x15, false, false, 0);
        Assert.Null(h); // dropped
        sink.Finish(0);
        h = m.Selection(h, 0x15, false, false, 50); // retried, starts
        Assert.NotNull(h);
        Assert.Equal(6, sink.Started.Count);
        Assert.Equal(h, m.Selection(h, 0x15, false, false, 100)); // playing: kept
        h = m.Selection(h, 0x15, true, false, 150); // pause cuts it off
        Assert.Null(h);
        Assert.Contains((object)5, sink.Done);
        Assert.Null(m.Selection(h, 0x15, true, false, 200)); // nothing while paused
        h = m.Selection(h, 0x15, false, false, 250); // first unpaused frame: from the start
        Assert.Equal(7, sink.Started.Count);
        sink.Finish(6);
        m.Update(300);
        h = m.Selection(h, 0x15, false, false, 300);
        Assert.NotNull(h); // the finished request keeps its handle
        Assert.Equal(h, m.Selection(h, 0x15, false, false, 350));
        Assert.Equal(7, sink.Started.Count); // never repeated
    }

    /// <summary>Classes are independent; voices one at a time, a waiting voice dropped after 8000 ms (32000 ms paused); commentary 3000 ms; oldest first.</summary>
    [Fact]
    public void VoicesWaitOldestFirstAndTimeOut()
    {
        var sink = new Sink();
        var m = new TacticalSounds(0, sink, _ => 0);
        m.Play(0x20, true, 0);
        m.Play(0x120, true, 0);
        for (int i = 0; i < 5; i++) m.Play(0, true, 0);
        Assert.Equal(7, sink.Started.Count); // one voice, one commentary line, five effects
        m.Play(0x21, true, 100);
        m.Play(0x22, true, 200);
        sink.Finish(0);
        m.Update(8000);
        Assert.Equal(("VOICEFXA.DLL", 14001 + 1), sink.Started[^1]); // 0x21 (oldest) first
        sink.Finish(7);
        m.Update(8201); // 0x22 waited 8001 ms: dropped
        Assert.Equal(8, sink.Started.Count);
        m.Paused = true;
        m.Play(0x23, true, 9000); // plays at once (room)
        m.Play(0x24, true, 9000);
        sink.Finish(8);
        m.Update(9000 + 31000);
        Assert.Equal(("VOICEFXA.DLL", 14001 + 4), sink.Started[^1]);
        m.Play(0x121, true, 50000); // commentary still playing (index 1): waits
        sink.Finish(1);
        m.Update(53001);
        Assert.Equal(10, sink.Started.Count); // dropped after 3000 ms
    }

    /// <summary>Module and WAVE by request number and side; a request the module lacks is dropped; stop by serial; stop all.</summary>
    [Fact]
    public void ResourcesStopAndMissingWaves()
    {
        Assert.Equal(("TACTICAL.DLL", 13000), TacticalSounds.Resource(0, 0, 0));
        Assert.Equal(("TACTICAL.DLL", 13021), TacticalSounds.Resource(9, 0, 1));
        Assert.Equal(("TACTICAL.DLL", 13065), TacticalSounds.Resource(0x1F, 0, 0));
        Assert.Equal(("VOICEFXA.DLL", 14122), TacticalSounds.Resource(0x99, 0, 0));
        Assert.Equal(("VOICEFXE.DLL", 15001), TacticalSounds.Resource(0x9A, 0, 1));
        Assert.Equal(("VOICEFXA.DLL", 15163), TacticalSounds.Resource(0x13C, 0, 0));
        var sink = new Sink { Have = (mod, w) => mod != "VOICEFXA.DLL" || w < 15001 || w > 15132 };
        var m = new TacticalSounds(0, sink, _ => 0);
        Assert.Null(m.Play(0x9A, true, 0)); // an Empire line: not in VOICEFXA
        Assert.Null(m.Play(0x13D, true, 0));
        int voice = m.Play(0x20, true, 0)!.Value;
        int waiting = m.Play(0x21, true, 0)!.Value;
        m.Stop(waiting);
        m.Stop(voice);
        Assert.Contains((object)0, sink.Done);
        m.Update(1);
        Assert.Single(sink.Started); // the stopped 0x21 never starts
        m.Play(0x20, true, 2);
        m.Play(0, true, 2);
        m.StopAll();
        Assert.Equal(3, sink.Done.Count);
    }

    /// <summary>Example: local Alliance, a live shielded Death Star: 0x20 at the first running frame, 0x90 at 6 s, 0x93 at 12 s, the monitor from 18 s.</summary>
    [GameFact]
    public void DeathStarHintsOpeningSequence()
    {
        var (_, b, _) = TacticalDeathStarTests.Make(Owner.Alliance, [0x14000042], [0x18000088], TacticalDeathStarTests.XWings(2, shield: true));
        Assert.True(b.SceneAlive);
        var at = new List<(int Ms, int Voice)>();
        for (int ms = 50; ms <= 20000; ms += 50)
        {
            int n = b.Ui.Voices.Count;
            b.Hints(50);
            at.AddRange(b.Ui.Voices.Skip(n).Select(v => (ms, v)));
            if (ms == 18050) Assert.Equal(4, b.HintState);
        }
        Assert.Equal([(50, 0x20), (6050, 0x90), (12050, 0x93)], at);
    }

    /// <summary>The opening waits for the intro; an Empire player hears 0x9A only, then the monitor.</summary>
    [GameFact]
    public void HintsWaitForTheIntro()
    {
        var (_, b, _) = TacticalDeathStarTests.Make(Owner.Empire, [0x14000042], [0x18000088]);
        b.StartIntro();
        b.Hints(50);
        Assert.Empty(b.Ui.Voices);
        b.SkipIntro();
        for (int i = 0; i < 100; i++) b.Hints(50);
        Assert.Equal([0x9A], b.Ui.Voices);
        Assert.Equal(1000, b.HintState);
    }

    /// <summary>
    /// The intro: no turns for 1500 intro ms (min(frame ms x 0.4, 200) per call), the UI observing, the jump-in sound per
    /// local group member; the drawn offset -d*F at t = 0, 0 at the end.
    /// </summary>
    [GameFact]
    public void IntroHoldsTheTurnsAndDrawsTheJumpIn()
    {
        var (_, b, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000042]);
        b.StartIntro();
        Assert.Equal([0x1E, 0x1E], b.Sounds);
        Assert.False(b.Ui.InCommand);
        var u = a[0];
        double d = ((u.Key + u.TypeIndex) % 4) switch { 0 => 400, 1 => 575, 2 => 650, _ => 325 };
        Assert.Equal(new V3(0, 0, -d), b.IntroOffset(u)); // side index 0: F = +z
        b.Advance(100); // 40
        for (int i = 0; i < 7; i++) b.Advance(1000); // + 7 * min(400, 200) = 1440
        Assert.True(b.InIntro);
        Assert.Equal(0, b.Turn);
        b.Advance(1000);
        Assert.False(b.InIntro);
        Assert.True(b.Ui.InCommand);
        Assert.Equal(V3.Zero, b.IntroOffset(u));
    }

    /// <summary>Shot sound and value; shield hit 0xC + spark; hull hit 0x11 + hull explosion; kill: voice, death, explosion sound; ion 0xE / 0x13; fighters no effect.</summary>
    [GameFact]
    public void HitsReportSoundsEffectsAndKillVoices()
    {
        var (game, b, a, e) = Scene(new Script([0], []), [0x14000042], [0x14000086, 0x14000086], o => { if (o.Owner == Owner.Empire) { o.SetCritical(0, 4); o.SetCritical(1, 4); } });
        long draws = game.Random.Draws;
        var (shooter, target) = (a[0], e[0]);
        Place(shooter, 0, 0);
        Place(target, 0, 30);
        Tf(b, shooter).G.Target = target.U;
        double s0 = target.Shield;
        for (int turn = 0; turn <= 5; turn++) b.Step();
        var shot = b.Shots[0];
        Assert.Equal(0x0E, shot.Kind);
        Assert.Equal(s0 - target.Shield, shot.Value, 3); // the shield is stored in f32
        Assert.Equal([1, 2, 0xC], b.Sounds); // turbolaser shot (turn 4), ion shot and the turbolaser hit on the shield (turn 5)
        Assert.Equal(new TacticalEffect(target, 1, 0), b.Effects[0]);

        b.Sounds.Clear();
        b.Effects.Clear();
        target.U.Shield = 0;
        b.Hit(target, 10, shooter, ratio: true, 0);
        Assert.Equal([0x12], b.Sounds);
        Assert.Equal([new TacticalEffect(target, 0, 1)], b.Effects);
        b.Sounds.Clear();
        int voices = b.Ui.Voices.Count;
        b.Hit(target, 1e9, shooter, ratio: true, 1);
        Assert.Equal([0x11, TacticalBattle.Explosion(target)], b.Sounds);
        Assert.Equal(new TacticalEffect(target, 1, 2), b.Effects[^1]);
        int k = b.Ui.SlotOf(shooter.UiGroup);
        Assert.Equal([k >= 0 ? 0x56 + k : 0x55], b.Ui.Voices.Skip(voices));

        b.Sounds.Clear();
        var other = e[1];
        b.IonHit(other, 1, shooter);
        other.U.Shield = 0;
        b.IonHit(other, 1, shooter);
        Assert.Equal([0xE, 0x13], b.Sounds);
        Assert.Equal([new TacticalEffect(other, 2, 0), new TacticalEffect(other, 2, 1)], b.Effects.TakeLast(2));
        Assert.Equal(draws, game.Random.Draws); // the game generator untouched
    }

    /// <summary>A local task force's current target found inactive: it leaves the UI list, then (list empty) 0x49 + k; once.</summary>
    [GameFact]
    public void TargetDestroyedVoiceOnce()
    {
        var (_, b, a, e) = Scene(new Script([], []), [0x14000042], [0x14000042, 0x14000042]);
        var g = a[0].Group!;
        int k = b.Ui.SlotOf(g);
        Assert.True(k >= 0);
        g.G.Target = e[0].U;
        g.UiTargetList.Add(e[0]);
        b.Destroy(e[0]);
        for (int i = 0; i < 5; i++) b.Advance(1000);
        Assert.Empty(g.UiTargets);
        Assert.Equal([0x49 + k], b.Ui.Voices);
    }

    /// <summary>The battle-end voice once with S (local Alliance: S[1] and S[2] -> 0x97), the jump-out sound, the scuttle death and explosion.</summary>
    [GameFact]
    public void WithdrawJumpScuttleAndEndVoice()
    {
        int n = 0;
        var (_, b, _, e) = Scene(new Script([], []), [0x14000042], [0x14000085, 0x14000085], o => { if (o.Owner == Owner.Empire && n++ == 1) o.SetCritical(3, 4); });
        b.Withdraw(1);
        while (!b.Ending && b.Turn < 300) b.Advance(1000);
        Assert.Equal((9, 11), (e[0].State, e[1].State));
        Assert.Contains(0x1F, b.Sounds);
        Assert.Contains(new TacticalEffect(e[1], 4, 2), b.Effects);
        Assert.Contains(TacticalBattle.Explosion(e[1]), b.Sounds);
        Assert.Equal((1, 1), (b.S[1], b.S[2]));
        Assert.Equal(0x97, b.Ui.Voices[^1]);
        Assert.Single(b.Ui.Voices, 0x97);
    }

    /// <summary>The superlaser's sound 0x14 and the target's death (w = 4); 0x113 when the charge crosses 100 for a local Empire.</summary>
    [GameFact]
    public void SuperlaserSoundsAndCharged()
    {
        var (_, b, _) = TacticalDeathStarTests.Make(Owner.Alliance, [0x14000042], [0x18000088]);
        for (int i = 0; i < 6; i++) b.Step();
        var target = b.Superlasers[0].To;
        Assert.Contains(0x14, b.Sounds);
        Assert.Contains(new TacticalEffect(target, 4, 2), b.Effects);
        Assert.DoesNotContain(0x113, b.Ui.Voices);

        var (_, emp, _) = TacticalDeathStarTests.Make(Owner.Empire, [0x14000042], [0x18000088]);
        emp.Scene!.Charge = 99.9f;
        emp.Step();
        Assert.Equal(100f, emp.Scene.Charge);
        Assert.Contains(0x113, emp.Ui.Voices);
    }
}
