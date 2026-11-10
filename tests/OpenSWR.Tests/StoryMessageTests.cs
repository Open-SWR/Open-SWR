using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Story and Force messages, types 0x1B, 0x1C, 0x1D, 0x24-0x29, and the Sim posts behind them.</summary>
public class StoryMessageTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static string Name(SdCommon c) => $"#{c.StringId}";

    static (GameState Game, Messages Log, List<int> Voices) New(Owner side = Owner.Alliance)
    {
        var game = NewGame.Create(new GameSettings(side, 1, 0, 0, 532947196), Definitions);
        var voices = new List<int>();
        return (game, new Messages(game, side, Name, list => { voices.Add(list); return list; }), voices);
    }

    static Character C(GameState game, uint type) => game.Characters.Single(c => c.Type == type);
    static string N(GameObject o) => $"#{Definitions.Resolve(o.Type)!.Common.StringId}";
    static int Pic(GameState game, GameObject o) => Messages.Picture(game, o);

    /// <summary>The story and Force messages of an update (other types, e.g. 0x19 / 0x1A from the state set up here, are left out).</summary>
    static List<GameMessage> Stories(Messages log) => [.. log.Update().Where(m => m.Type is 0x1B or 0x1C or 0x1D or (>= 0x24 and <= 0x29))];

    static void Post(GameState game, int kind, GameObject? key, SideSet live, long old, long now, GameObject? other = null, int group = 0) =>
        game.StoryTrace!.Add(new StoryEvent(game.Day, kind, key, live, old, now, other, group));

    static Mission NewMission(GameState game, uint family, Owner owner) =>
        new(Definitions.Missions.First(r => Family.Of(r.Common.Ref) == family), game.NextId()) { Owner = owner };

    // Captured (either side), Alliance attacked, Empire located with a system, none without one.
    [GameFact]
    public void BountyHunters()
    {
        var (game, log, voices) = New();
        var han = C(game, Refs.Han);
        Post(game, 0x212, han, SideSet.Both, 0, 1); // the set post does not act
        Post(game, 0x212, han, SideSet.Both, 1, 0);
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x1B, 3, MessageCategory.Mission, 0x7168, 0x7169, 1052, 1141, 0x19, han.Ref), (m.Type, m.Kind, m.Category, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Equal([0x120], voices);

        han.Captured = true;
        Post(game, 0x212, han, SideSet.Only(Owner.Alliance), 1, 0);
        m = Assert.Single(Stories(log));
        Assert.Equal((0x716C, 0x716D, 1053, 1142, 0x21, 0u), (m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));

        var (eg, elog, evoices) = New(Owner.Empire);
        var ehan = C(eg, Refs.Han);
        ehan.LiveSides = SideSet.Only(Owner.Alliance);
        var at = eg.Systems.First();
        eg.Views[(ehan, Owner.Empire)] = new View { Owner = Owner.Alliance, Parent = at };
        Post(eg, 0x212, ehan, SideSet.Both, 1, 0);
        m = Assert.Single(Stories(elog));
        Assert.Equal((0x716A, 0x716B, N(at), 1054, 1143, 0x2D, ehan.Ref), (m.Title!.Template, m.Body![0].Template, m.Body[0].Args[0].Text, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Empty(evoices);
        eg.Views[(ehan, Owner.Empire)] = new View { Owner = Owner.Alliance };
        Post(eg, 0x212, ehan, SideSet.Both, 1, 0);
        Assert.Empty(Stories(elog));
        Post(eg, 0x212, ehan, SideSet.Only(Owner.Alliance), 1, 0); // not posted by an Empire view
        Assert.Empty(Stories(elog));
    }

    // Leia branch: 0x7179 (trainer) vs 0x717A. A discoverer of the other definition side is invalid.
    [GameFact]
    public void ForceUserDiscovered()
    {
        var (game, log, voices) = New();
        var (luke, leia, vader) = (C(game, Refs.Luke), C(game, Refs.Leia), C(game, Refs.Vader));
        Post(game, 0x362, luke, SideSet.Both, 0, 1, leia);
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x1C, 0x71D8, 0x71D9, leia.Ref, Pic(game, leia), 1042, 1148, 0x18), (m.Type, m.Title!.Template, m.Body![0].Template, m.Subject, m.Overlay, m.Picture, m.Sound, m.AdvisorCode));
        Assert.Equal([0x124], voices);

        var minor = game.Characters.First(c => Family.Of(c.Type) >= 0x38 && Finders.DefinitionSide(c) == Owner.Alliance);
        luke.ForceRank = 0;
        Post(game, 0x362, luke, SideSet.Both, 0, 1, minor);
        m = Assert.Single(Stories(log));
        Assert.Equal((0x7178, N(minor), N(luke), 0x717A, N(minor), Pic(game, luke), 0x16), (m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Body![0].Template, m.Body[0].Args[0].Text, m.Overlay, m.AdvisorCode));
        Assert.Equal(0x135, voices[^1]);
        Assert.NotEqual(0u, luke.Definition.IsJediTrainer);
        luke.ForceRank = game.Params.General(0xA15);
        Post(game, 0x362, luke, SideSet.Both, 0, 1, minor);
        Assert.Equal(0x7179, Assert.Single(Stories(log)).Body![0].Template);

        Post(game, 0x362, vader, SideSet.Both, 0, 1, minor); // definition side 2 != the message side
        Assert.Empty(Stories(log));
    }

    // A first Force (0 -> n) gives none. n -> m gives 0x7180 / 0x7181 with the rank title (kind 7).
    [GameFact]
    public void ForceGrowth()
    {
        var (game, log, voices) = New();
        var luke = C(game, Refs.Luke);
        Post(game, 0x1E1, luke, SideSet.Both, 0, 15);
        Assert.Empty(Stories(log));
        luke.ForceRank = 85;
        luke.ForceBonus = 0;
        Post(game, 0x1E1, luke, SideSet.Both, 15, 85);
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x1D, 0x7180, N(luke), 0x7181, "#34641", luke.Ref, Pic(game, luke), 0x16), (m.Type, m.Title!.Template, m.Title.Args[0].Text, m.Body![0].Template, m.Body[0].Args[0].Text, m.Subject, m.Overlay, m.AdvisorCode));
        Assert.Equal([0x132], voices);
        luke.DagobahCompleted = true;
        Post(game, 0x1E1, luke, SideSet.Both, 85, 90);
        Stories(log);
        Assert.Equal(0x133, voices[^1]);
        Post(game, 0x1E1, C(game, Refs.Vader), SideSet.Both, 10, 20); // definition side of the enemy
        Assert.Empty(Stories(log));
        Assert.Equal((34644, 34633, 34640, 34641, 34642, 34643), (Messages.RankTitle(9), Messages.RankTitle(10), Messages.RankTitle(79), Messages.RankTitle(80), Messages.RankTitle(119), Messages.RankTitle(120)));
    }

    // Final battle won / lost. Picture by DagobahCompleted.
    [GameFact]
    public void FinalBattle()
    {
        var (game, log, voices) = New();
        var luke = C(game, Refs.Luke);
        Post(game, 0x220, null, SideSet.Both, 0, 1);
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x24, 0x71E0, 0x71E1, 1063, 1138, luke.Ref, 0x16), (m.Type, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.Subject, m.AdvisorCode));
        Assert.Equal([0x11C], voices);
        luke.Captured = true;
        luke.DagobahCompleted = true;
        Post(game, 0x220, null, SideSet.Both, 0, 1);
        m = Assert.Single(Stories(log));
        Assert.Equal((0x71E2, 1064, 1139), (m.Body![0].Template, m.Picture, m.Sound));
        Assert.Equal(0x12F, voices[^1]);
    }

    // Heritage: Luke captured with the DarthToEmperor mission gives 0x71A2, else 0x71A3. Free and unhurt Luke with Vader
    // injured gives 0x71A5 + 0x71A7.
    [GameFact]
    public void Heritage()
    {
        var (game, log, voices) = New();
        var (luke, vader, pal) = (C(game, Refs.Luke), C(game, Refs.Vader), C(game, Refs.Palpatine));
        void Duel(int g)
        {
            Post(game, 0x360, luke, SideSet.Both, 1, 0, group: g);
            Post(game, 0x360, vader, SideSet.Both, 1, 0, group: g);
            Post(game, 0x211, luke, SideSet.Both, 0, 1, group: g);
        }
        Duel(1);
        luke.Injury = vader.Injury = 0;
        vader.Injury = 5;
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x24, 0x71A0, 1058, 1133, luke.Ref, 0x16), (m.Type, m.Title!.Template, m.Picture, m.Sound, m.Subject, m.AdvisorCode));
        Assert.Equal([0x71A1, 0x71A5, 0x71A7], m.Body!.Select(b => b.Template));
        Assert.Equal([0x11E], voices);

        vader.Injury = 0;
        luke.Captured = true;
        luke.LiveSides = SideSet.Only(Owner.Alliance);
        var toEmperor = NewMission(game, 0x41, Owner.Empire);
        toEmperor.TargetKey = pal.Container;
        game.Views[(luke, Owner.Empire)] = new View { Owner = Owner.Empire, RoleMission = toEmperor };
        vader.Mission = toEmperor;
        Duel(2);
        Assert.Equal([0x71A1, 0x71A2], Assert.Single(Stories(log)).Body!.Select(b => b.Template));
        toEmperor.TargetKey = null;
        Duel(3);
        Assert.Equal([0x71A1, 0x71A3], Assert.Single(Stories(log)).Body!.Select(b => b.Template));
    }

    // Confrontation status lines (0x71AA / 0x71AB / 0x71AC / 0x71AD, 0x71AE) and the picture table.
    [GameFact]
    public void Confrontation()
    {
        var (game, log, voices) = New();
        var (leia, luke, vader, pal) = (C(game, Refs.Leia), C(game, Refs.Luke), C(game, Refs.Vader), C(game, Refs.Palpatine));
        int g = 0;
        List<int> Body(Character a, Character e, out GameMessage m)
        {
            g++;
            Post(game, 0x360, a, SideSet.Both, 0, 1, e, g);
            Post(game, 0x360, e, SideSet.Both, 0, 1, a, g);
            m = Assert.Single(Stories(log));
            return [.. m.Body!.Select(b => b.Template)];
        }
        Assert.Equal([0x71A9, 0x71AE], Body(leia, vader, out var m));
        Assert.Equal((0x71A8, N(leia), N(vader), 1055, 1130, leia.Ref, 0x18), (m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Picture, m.Sound, m.Subject, m.AdvisorCode));
        Assert.Equal(0x137, voices[^1]);
        leia.Captured = true;
        pal.Injury = 3;
        Assert.Equal([0x71A9, 0x71AA, 0x71AD], Body(leia, pal, out m));
        Assert.Equal((1056, 1131, 0x138), (m.Picture, m.Sound, voices[^1]));
        luke.Injury = 2;
        vader.Captured = true;
        vader.LiveSides = SideSet.Both; // Captured is read from the side's view: live here
        Assert.Equal([0x71A9, 0x71AB, 0x71AC], Body(luke, vader, out m));
        Assert.Equal((1059, 1134, 0x118), (m.Picture, m.Sound, voices[^1]));
        luke.DagobahCompleted = true;
        Body(luke, pal, out m);
        Assert.Equal((1062, 1137, 0x11B), (m.Picture, m.Sound, voices[^1]));
    }

    // Detection for each side (own / other, overlay own, voice). Invalid without the enemy's post. Definition side 0 kills the group.
    [GameFact]
    public void Detection()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            var own = game.Characters.First(c => Family.Of(c.Type) >= 0x38 && Finders.DefinitionSide(c) == side);
            var other = game.Characters.First(c => Family.Of(c.Type) >= 0x38 && Finders.DefinitionSide(c) is Owner.Alliance or Owner.Empire && Finders.DefinitionSide(c) != side);
            Post(game, 0x360, own, SideSet.Both, 0, 1, other, 1);
            Post(game, 0x360, other, SideSet.Both, 0, 1, own, 1);
            var m = Assert.Single(Stories(log));
            Assert.Equal((0x24, 0x71B0, N(own), N(other), 0x71B1, Pic(game, own), own.Ref, 0x15), (m.Type, m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Body![0].Template, m.Overlay, m.Subject, m.AdvisorCode));
            Assert.Equal((side == Owner.Alliance ? 1042 : 1043, side == Owner.Alliance ? 1148 : 1149), (m.Picture, m.Sound));
            Assert.Empty(voices);
            Post(game, 0x360, own, SideSet.Both, 0, 1, other, 2);
            Post(game, 0x360, other, SideSet.Both.With(side, false), 0, 1, own, 2); // the side has no live view of the enemy participant
            Assert.Empty(Stories(log));
        }
        var (g2, log2, _) = New();
        var luke = C(g2, Refs.Luke);
        Post(g2, 0x360, luke, SideSet.Both, 0, 1, null, 1);
        Post(g2, 0x360, C(g2, Refs.Vader), SideSet.Both, 0, 1, null, 1);
        Assert.Equal(0x71A8, Assert.Single(Stories(log2)).Title!.Template); // Luke against Vader without heritage: confrontation, not detection
        Post(g2, 0x360, luke, SideSet.Both, 0, 1, null, 2);
        Post(g2, 0x360, C(g2, Refs.Han), SideSet.Both, 0, 1, null, 2); // two Alliance participants: E stays none -> detection without other
        Assert.Empty(Stories(log2));
    }

    // 0x221 set with Luke on a Dagobah mission is valid. The clear is invalid. None for the Empire. 0x210 picture 1042 / sound 1148.
    [GameFact]
    public void Dagobah()
    {
        var (game, log, voices) = New();
        var luke = C(game, Refs.Luke);
        luke.Mission = NewMission(game, 0x71, Owner.Alliance);
        Post(game, 0x221, null, SideSet.Both, 0, 1);
        Post(game, 0x221, null, SideSet.Both, 1, 0);
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x25, 8, 0x71B8, 0x71B9, 1057, 1132, 0x16, 0u), (m.Type, m.Kind, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Equal([0x11D], voices);
        luke.Mission = null;
        Post(game, 0x221, null, SideSet.Both, 0, 1);
        Assert.Empty(Stories(log));
        Post(game, 0x210, luke, SideSet.Both, 0, 1);
        m = Assert.Single(Stories(log));
        Assert.Equal((0x25, 8, 0x71BA, 1042, 1148, Pic(game, luke), 0x16), (m.Type, m.Kind, m.Title!.Template, m.Picture, m.Sound, m.Overlay, m.AdvisorCode));

        var (eg, elog, _) = New(Owner.Empire);
        C(eg, Refs.Luke).Mission = NewMission(eg, 0x71, Owner.Alliance);
        Post(eg, 0x221, null, SideSet.Both, 0, 1);
        Post(eg, 0x210, C(eg, Refs.Luke), SideSet.Both, 0, 1);
        Assert.Empty(Stories(elog));
    }

    // Emperor at Coruscant newly true: Empire only.
    [GameFact]
    public void SeatOfPower()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            var pal = C(game, Refs.Palpatine);
            bool at = game.EmperorAtCoruscant;
            pal.Captured = at; // flip it so the next update sees a change
            log.Update();
            Assert.False(game.EmperorAtCoruscant);
            pal.Captured = false;
            Assert.True(game.EmperorAtCoruscant);
            var added = Stories(log);
            if (side == Owner.Alliance) { Assert.Empty(added); continue; }
            var m = Assert.Single(added);
            Assert.Equal((0x26, 0x71C8, 0x71C9, 1068, 1147, 0x1A, pal.Ref), (m.Type, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
            Assert.Equal([0x130], voices);
        }
    }

    // The none post is invalid. Slot 3 = the enemy side. Code and voice by the discoverer.
    [GameFact]
    public void TraitorDiscovered()
    {
        var (game, log, voices) = New();
        var (leia, minor) = (C(game, Refs.Leia), game.Characters.First(c => Family.Of(c.Type) >= 0x38 && Finders.DefinitionSide(c) == Owner.Alliance));
        Post(game, 0x361, leia, SideSet.Both, 0, 0, null);
        Assert.Empty(Stories(log));
        Post(game, 0x361, leia, SideSet.Both, 0, 1, minor);
        var m = Assert.Single(Stories(log));
        Assert.Equal((0x27, 0x71E8, N(leia), N(minor), 0x71E9, Owner.Empire, leia.Ref, 0x18, Pic(game, leia)),
            (m.Type, m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Body![0].Template, m.Body[0].Args[2].Side, m.Subject, m.AdvisorCode, m.Overlay));
        Assert.Equal([0x127], voices);
        Post(game, 0x361, C(game, Refs.Vader), SideSet.Both, 0, 1, minor); // the enemy's definition side
        Assert.Empty(Stories(log));
    }

    // Alliance only, kind 8, category 0, the leader of a Palace mission.
    [GameFact]
    public void RescueAttempt()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            var leia = C(game, Refs.Leia);
            var palace = NewMission(game, 0x64, Owner.Alliance);
            palace.Leader = leia;
            Post(game, 0x200, leia, SideSet.Both, 0, 1, palace);
            var added = Stories(log);
            if (side == Owner.Empire) { Assert.Empty(added); continue; }
            var m = Assert.Single(added);
            Assert.Equal((0x28, 8, 0, 0x71EA, N(leia), 0x71EB, 1042, 1148, Pic(game, leia), 0x18, leia.Ref),
                (m.Type, m.Kind, m.Category, m.Title!.Template, m.Title.Args[0].Text, m.Body![0].Template, m.Picture, m.Sound, m.Overlay, m.AdvisorCode, m.Subject));
            Assert.Equal([0xE5], voices);
            Assert.True(m.Listed(0) && !m.Listed(MessageCategory.Mission));
            Post(game, 0x200, leia, SideSet.Both, 0, 1, NewMission(game, 0x51, Owner.Alliance)); // not a Palace mission
            Assert.Empty(Stories(log));
        }
    }

    // Jabba delivers the prisoners: picture / sound / voice by Luke's DagobahCompleted, either side.
    [GameFact]
    public void JabbaDelivers()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            Post(game, 0x231, null, SideSet.Both, 0, 1);
            Post(game, 0x231, null, SideSet.Both, 1, 0);
            var m = Assert.Single(Stories(log));
            Assert.Equal((0x29, 0x7200, 0x7201, 1066, 1145, 0x1B, C(game, Refs.Vader).Ref), (m.Type, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
            C(game, Refs.Luke).DagobahCompleted = true;
            Post(game, 0x231, null, SideSet.Both, 0, 1);
            m = Assert.Single(Stories(log));
            Assert.Equal((1067, 1146), (m.Picture, m.Sound));
            Assert.Equal([0x123, 0x139], voices);
        }
    }

    // The Sim posts: discovery on the discoverer, the pairing and the resolution in two groups (0x211 with the resolution),
    // Force changes, the bounty clear. Each duel gives two messages.
    [GameFact]
    public void SimPostsTheNotifications()
    {
        var (game, log, _) = New();
        var (luke, leia, vader) = (C(game, Refs.Luke), C(game, Refs.Leia), C(game, Refs.Vader));
        var trace = game.StoryTrace!;

        Objects.SetParent(game, leia, luke.Container!);
        (leia.JediPotential, leia.KnownJedi, leia.ForceRank) = (true, false, 0);
        Story.SetLukeKnowsHeritage(game, luke);
        Jedi.SetForceRank(game, luke, game.Params.General(0xA14));
        game.Clock.Flush(game);
        trace.Clear();
        Jedi.Discover(game, luke);
        var d = Assert.Single(trace, e => e.Kind == 0x362);
        Assert.Equal((luke, leia, true), (d.Key, d.Other, d.Live.Alliance));
        Assert.Contains(trace, e => e.Kind == 0x1E1 && e.Key == leia && e.Old == 0 && e.New == leia.Force); // the rank roll
        Jedi.Discover(game, luke); // no new discovery, no post
        Assert.Single(trace, e => e.Kind == 0x362);

        trace.Clear();
        luke.KnowsHeritage = false;
        vader.Observed = vader.Observed.With(Owner.Alliance, true); // the Alliance sees Vader live (both participants post to it)
        Knowledge.RecomputeLive(game, vader);
        Jedi.Pair(game, luke, vader);
        Assert.Equal([0x360, 0x360], trace.Select(e => e.Kind));
        int pairing = trace[0].Group;
        Assert.True(pairing != 0 && trace[1].Group == pairing && trace.All(e => e.New == 1));
        Jedi.Duel(game, vader, luke);
        var resolution = trace.Skip(2).Where(e => e.Kind is 0x360 or 0x211).ToList();
        Assert.Equal([0x360, 0x360, 0x211], resolution.Select(e => e.Kind));
        Assert.True(resolution.All(e => e.Group == resolution[0].Group) && resolution[0].Group != pairing);
        Assert.Equal(0, game.StoryGroup);
        var duel = Stories(log).Where(m => m.Type == 0x24).ToList();
        Assert.Equal(2, duel.Count); // the pairing's confrontation and the resolution's heritage
        Assert.Equal((0x71A8, 0x71A0), (duel[0].Title!.Template, duel[1].Title!.Template));
    }

    // Manager posts (0x220 FinalBattle set, 0x221 LukeDagobah) reach both sides. A Palace agent's EnrouteActive rising
    // posts 0x200 with its mission.
    [GameFact]
    public void SimPostsTheManagerAndPalacePosts()
    {
        var (game, _, _) = New();
        var trace = game.StoryTrace!;
        Story.Set(game, StoryFlag.FinalBattle, true);
        var f = Assert.Single(trace);
        Assert.Equal((0x220, SideSet.Both, 1L), (f.Kind, f.Live, f.New));
        trace.Clear();
        var leia = C(game, Refs.Leia);
        var palace = NewMission(game, 0x64, Owner.Alliance);
        leia.Mission = palace;
        Objects.SetEnrouteActive(game, leia, true);
        var e = Assert.Single(trace, x => x.Kind == 0x200);
        Assert.Equal((leia, palace, 1L), (e.Key, e.Other, e.New));
    }
}
