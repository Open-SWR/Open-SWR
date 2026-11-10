using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>
/// Force fields and their hooks, the Force-rank roll, Jedi training (mission 0x58), Force duels (pairing and resolution), Force
/// growth outside Dagobah and the discovery of Force users (timer 0x389).
/// Not modelled: UI messages.
/// </summary>
public static class Jedi
{
    // ---- Force fields ----

    /// <summary>ForceRank setter (clamped, on change). Its hook recomputes Force (rank + training), then runs the Force hook on change.</summary>
    public static void SetForceRank(GameState game, Character c, int value)
    {
        value = Math.Clamp(value, 0, 0x7FFF);
        if (value == c.ForceRank) return;
        int old = c.Force;
        c.ForceRank = value;
        if (c.Force != old) Changed(game, c, old);
    }

    /// <summary>ForceTraining setter (clamped, on change). Its hook recomputes Force, then runs the Force hook on change.</summary>
    public static void SetForceTraining(GameState game, Character c, int value)
    {
        value = Math.Clamp(value, 0, 0x7FFF);
        if (value == c.ForceBonus) return;
        int old = c.Force;
        c.ForceBonus = value;
        if (c.Force != old) Changed(game, c, old);
    }

    /// <summary>Force changed: UI 0x1E1 CharacterForce (old, new) from the character's live views, then the Force hook.</summary>
    static void Changed(GameState game, Character c, int old)
    {
        game.StoryPost(0x1E1, c, old, c.Force);
        ForceHook(game, c);
    }

    /// <summary>
    /// Force hook, only for a new Force != 0: the enhanced skills (computed on read), FastHeal, DiscoveringForceUser
    /// (timer 0x389). For Luke, then his FinalBattleReady recompute. No draw.
    /// </summary>
    static void ForceHook(GameState game, Character c)
    {
        if (c.Force != 0)
        {
            Prisoners.ForceChanged(game, c);
            TimerCatalogue.RecomputeForceActive(game, c);
        }
        if (c.Ref == Refs.Luke) Story.RecomputeLukeReady(game, c);
    }

    /// <summary>ForceAware setter. Its hook runs the Force-rank roll on change.</summary>
    public static void SetForceAware(GameState game, Character c)
    {
        if (c.KnownJedi) return;
        c.KnownJedi = true;
        RankRoll(game, c);
    }

    /// <summary>Force-rank roll: when ForceRank = 0 and ForcePotential and ForceAware, ForceRank := JediLevel base + roll(JediLevel variance).</summary>
    static void RankRoll(GameState game, Character c)
    {
        if (c.ForceRank != 0 || !c.JediPotential || !c.KnownJedi) return;
        var level = c.Definition.JediLevel;
        SetForceRank(game, c, (int)level.Base + game.Random.Roll((int)level.Variance, DrawSite.ForceRank));
    }

    // ---- Jedi training ----

    /// <summary>A resolving character: a registry lookup with a parent; Destroyed is not tested.</summary>
    static bool Resolves(Character? c) => c is { Container: not null };

    /// <summary>
    /// Teacher rule: the first Team character [0x30, 0x3C) that resolves, has IsJediTrainer and
    /// Force >= G 0xA15.
    /// </summary>
    static Character? FindTeacher(GameState game, Mission m) =>
        m.Team.OfType<Character>().FirstOrDefault(c => Resolves(c) && c.Definition.IsJediTrainer != 0 && c.Force >= game.Params.General(0xA15));

    /// <summary>Phase hook, after the standard one: entering phase 2, teacher := the rule's character if one is found (else unchanged).</summary>
    internal static void TrainingPhase(GameState game, Mission m, int phase)
    {
        if (phase == 2 && FindTeacher(game, m) is { } t) m.Teacher = t;
    }

    /// <summary>Probability: Force(teacher) - Force(student) for a character with a resolving teacher, else 0 (the teacher itself: 0).</summary>
    internal static int Probability(Mission m, Agent a) =>
        a is Character s && Resolves(m.Teacher) && m.Teacher != s ? m.Teacher!.Force - s.Force : 0;

    /// <summary>
    /// Attempt, for every collected member, never stops: chance(p); on success for a character with a resolving teacher:
    /// f' = Fs if Fs >= Ft, else Fs + roll(pct(Ft - Fs, G 0x181D)); f' != Fs gives ForceTraining += f' - Fs, TaskStatus := 3.
    /// </summary>
    internal static void Attempt(GameState game, Mission m, Agent a, int p)
    {
        if (!game.Random.Chance(p, DrawSite.JediTraining) || a is not Character s || !Resolves(m.Teacher)) return;
        int fs = s.Force, ft = m.Teacher!.Force;
        int f = fs >= ft ? fs : fs + game.Random.Roll(Pct(ft - fs, game.Params.General(0x181D)), DrawSite.JediGain);
        if (f == fs) return;
        SetForceTraining(game, s, s.ForceBonus + f - fs);
        m.TaskStatus = 3;
    }

    /// <summary>Leave, after the standard leave: when the teacher left a running mission, CompletionStatus is 11.</summary>
    internal static void Left(GameState game, Mission m, Agent a)
    {
        if (m.Kind == 0x58 && m.Teacher == a && m.CompletionStatus == 0) Missions.SetCompletion(game, m, 11);
    }

    /// <summary>
    /// System Bombard (0x310, status 9) / Assault (0x30F, status 10) set (the 1 to 0 posts have no effect): every Usable Jedi-training mission (key order) whose
    /// TargetLocationKey is the system and whose Phase &gt; 6, while CompletionStatus = 0, gets the status.
    /// </summary>
    internal static void Abort(GameState game, StarSystem s, int status)
    {
        foreach (var m in game.Missions.Where(m => m.Usable && m.Kind == 0x58 && m.TargetLocation == s && m.Phase > 6).OrderBy(m => m.Key).ToList())
            if (m.CompletionStatus == 0) Missions.SetCompletion(game, m, status);
    }

    // ---- duels ----

    /// <summary>
    /// Pairing, after a successful Force detection of member b by opponent a: only if both EncounterKeys are none:
    /// one command serial; knowledge mode 6 of b's location for both sides; the keys point at each other, both encounter serials := it.
    /// </summary>
    internal static void Pair(GameState game, Character b, Character a)
    {
        if (b.EncounterKey is not null || a.EncounterKey is not null) return;
        uint serial = game.Clock.NextSerial();
        Knowledge.UpdateAt(game, b, 6, Owner.Alliance, Owner.Empire);
        (b.EncounterKey, a.EncounterKey, b.EncounterSerial, a.EncounterSerial) = (a, b, serial, serial);
        // UI 0x360 sets: the pairing starts a new context, so both posts form one group (Inferred)
        game.StoryGroup = game.NextStoryGroup();
        game.StoryPost(0x360, b, 0, 1, a);
        game.StoryPost(0x360, a, 0, 1, b);
        game.StoryGroup = 0;
    }


    /// <summary>
    /// Duel, only for a mutual pair with equal nonzero serials: 1. evasion of A chance(T(RLEVADTB, EnhCombat(A) -
    /// EnhCombat(B))): success is an evasion, failure a capture (captor B); dA = A's Injury gain. 2.-4. wounds: on A only if
    /// dA = 0, q = max(G 0xA0B, eB - eA); on B always, q = max(G 0xA0B, eA - eB). 5. base Combat += G 0xA12 for A if dB != 0, for
    /// B if dA != 0. 6. keys and serials cleared. 7. Force growth by role.
    /// </summary>
    internal static void Duel(GameState game, Character a, Character b)
    {
        if (a.EncounterKey != b || b.EncounterKey != a || a.EncounterSerial == 0 || a.EncounterSerial != b.EncounterSerial) return;
        var g = game.Params;
        int E(Character c) => Missions.Enhanced(game, c, SkillIndex.Combat);
        int before = a.Injury;
        if (game.Random.Chance(Missions.Table(game, "RLEVADTB", E(a) - E(b)) ?? 0, DrawSite.EncounterCheck)) Missions.Evaded(game, a);
        else Prisoners.Capture(game, a, b);
        int dA = a.Injury - before;
        int eB = E(b), eA = E(a);
        if (dA == 0) dA = Wound(game, a, eB - eA);
        int dB = Wound(game, b, eA - eB);
        if (dB != 0) a.Skills[SkillIndex.Combat] += g.General(0xA12);
        if (dA != 0) b.Skills[SkillIndex.Combat] += g.General(0xA12);
        (a.EncounterKey, b.EncounterKey, a.EncounterSerial, b.EncounterSerial) = (null, null, 0, 0);
        // UI 0x360 clears and Luke's 0x211 form the resolution's group (Inferred context)
        game.StoryGroup = game.NextStoryGroup();
        game.StoryPost(0x360, a, 1, 0);
        game.StoryPost(0x360, b, 1, 0);
        Growth(game, a, b);
        game.StoryGroup = 0;
    }

    /// <summary>Duel wound: q = max(G 0xA0B, d), chance(q); success gives w = roll(q) + roll(G 0xA0D) + G 0xA0C, and the character takes a wound of w.</summary>
    static int Wound(GameState game, Character c, int d)
    {
        var g = game.Params;
        int q = Math.Max(g.General(0xA0B), d);
        if (!game.Random.Chance(q, DrawSite.DuelWound)) return 0;
        int w = game.Random.Roll(q, DrawSite.DuelWound) + game.Random.Roll(g.General(0xA0D), DrawSite.DuelWound) + g.General(0xA0C);
        Missions.ApplyWound(game, c, w);
        return w;
    }

    /// <summary>
    /// Step 7: Luke with Vader (KnowsHeritage; heritage wound roll(G 0xC08) + G 0xC07 while not Captured and Force &lt; G 0xC05;
    /// rank + max(G 0xC0C, pct(delta F, G 0xC00))), Luke with Palpatine (G 0xC0D, G 0xC01), Leia (ForceAware) with Vader (G 0xC0E,
    /// G 0xC02) or Palpatine (G 0xC0F, G 0xC03); gains only while not Captured; the pct term only for delta F &gt; 0. A pair counts
    /// with either character as A or B.
    /// </summary>
    static void Growth(GameState game, Character a, Character b)
    {
        var g = game.Params;
        Character? Who(uint r) => a.Ref == r ? a : b.Ref == r ? b : null;
        var (luke, leia, vader, pal) = (Who(Refs.Luke), Who(Refs.Leia), Who(Refs.Vader), Who(Refs.Palpatine));
        void Gain(Character c, Character other, uint min, uint pct)
        {
            int d = other.Force - c.Force;
            SetForceRank(game, c, c.ForceRank + Math.Max(g.General(min), d > 0 ? Pct(d, g.General(pct)) : 0));
        }
        if (luke is not null && vader is not null)
        {
            Story.SetLukeKnowsHeritage(game, luke);
            if (!luke.Captured && luke.Force < g.General(0xC05))
                Missions.ApplyWound(game, luke, game.Random.Roll(g.General(0xC08), DrawSite.HeritageWound) + g.General(0xC07));
            if (!luke.Captured) Gain(luke, vader, 0xC0C, 0xC00);
        }
        if (luke is not null && pal is not null && !luke.Captured) Gain(luke, pal, 0xC0D, 0xC01);
        if (leia is not null && vader is not null && !leia.Captured && leia.KnownJedi) Gain(leia, vader, 0xC0E, 0xC02);
        if (leia is not null && pal is not null && !leia.Captured && leia.KnownJedi) Gain(leia, pal, 0xC0F, 0xC03);
    }

    // ---- other Force growth ----

    /// <summary>Evasion success extra (Luke, Leia), on every success (the base returns nonzero for real characters): ForceRank += G 0xC09 (Luke) / G 0xC0A (Leia, only if ForceAware).</summary>
    internal static void EvasionGain(GameState game, Agent a)
    {
        if (a is not Character c) return;
        if (c.Ref == Refs.Luke) SetForceRank(game, c, c.ForceRank + game.Params.General(0xC09));
        else if (c.Ref == Refs.Leia && c.KnownJedi) SetForceRank(game, c, c.ForceRank + game.Params.General(0xC0A));
    }

    /// <summary>
    /// Timer 0x389 action, discoverer T with DiscoveringForceUser: every character [0x30, 0x3C) directly
    /// in T's parent, Usable, of T's owner (key order), not Captured, not OnMission, != T, ForcePotential and not ForceAware: Leia
    /// only when T is Luke knowing his heritage (Leia KnowsHeritage := 1); then ForceAware := 1 (rank roll) and
    /// T's ForceUserDiscoveredKey := it (written on the discoverer; a change posts UI 0x362 from T's live views).
    /// </summary>
    internal static void Discover(GameState game, Character t)
    {
        if (!t.ForceActive || t.Container is not { } parent) return;
        foreach (var c in parent.Contents.OfType<Character>().Where(c => Family.Of(c.Type) < 0x3C && c.Usable && c.Owner == t.Owner).ToList())
        {
            if (c.Captured || c.OnMission || c == t || !c.JediPotential || c.KnownJedi) continue;
            if (c.Ref == Refs.Leia)
            {
                if (t.Ref != Refs.Luke || !t.KnowsHeritage) continue;
                c.KnowsHeritage = true;
            }
            SetForceAware(game, c);
            if (t.DiscoveredForceUser == c) continue;
            t.DiscoveredForceUser = c;
            game.StoryPost(0x362, t, 0, 1, c);
        }
    }
}
