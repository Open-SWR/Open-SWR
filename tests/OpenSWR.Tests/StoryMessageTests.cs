using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Story and Force message kinds, and the Sim posts behind them.</summary>
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
    static string N(GameObject o) => $"#{Definitions.Resolve(o.Type)!.Common.StringId}";
    static int Pic(GameState game, GameObject o) => Messages.Picture(game, o);

    /// <summary>The story and Force messages of an update (other kinds, e.g. 25 / 26 from the state set up here, are left out).</summary>
    static List<GameMessage> Stories(Messages log) => [.. log.Update().Where(m => m.Type is MessageType.BountyHunters or MessageType.ForceDiscovered or MessageType.ForceGrowth or MessageType.ForceEncounter
        or MessageType.Dagobah or MessageType.EmperorArrives or MessageType.Traitor or MessageType.PalaceRescue or MessageType.JabbaPrisoners)];

    static void Post(GameState game, StoryPostKind kind, GameObject? key, SideSet live, long old, long now, GameObject? other = null, int group = 0) =>
        game.StoryTrace!.Add(new StoryEvent(game.Day, kind, key, live, old, now, other, group));

    static Mission NewMission(GameState game, uint family, Owner owner) =>
        new(Definitions.Missions.First(r => Family.Of(r.Common.Ref) == family), game.NextId()) { Owner = owner };

    // Captured (either side), Alliance attacked, Empire located with a system, none without one.
    [GameFact]
    public void BountyHunters()
    {
        var (game, log, voices) = New();
        var han = game.Known.Han!;
        Post(game, StoryPostKind.BountyAttack, han, SideSet.Both, 0, 1); // the set post does not act
        Post(game, StoryPostKind.BountyAttack, han, SideSet.Both, 1, 0);
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.BountyHunters, 3, MessageCategory.Mission, TextId.HanAttackedTitle, TextId.HanAttackedBody, 1052, 1141, AdvisorCode.HanMessage, han.Ref), (m.Type, m.Kind, m.Category, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Equal([288], voices);

        han.Captured = true;
        Post(game, StoryPostKind.BountyAttack, han, SideSet.Only(Owner.Alliance), 1, 0);
        m = Assert.Single(Stories(log));
        Assert.Equal((TextId.HanCapturedTitle, TextId.HanCapturedTakenTo, 1053, 1142, AdvisorCode.HanCaptured, 0u), (m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));

        var (eg, elog, evoices) = New(Owner.Empire);
        var ehan = eg.Known.Han!;
        ehan.LiveSides = SideSet.Only(Owner.Alliance);
        var at = eg.Systems.First();
        eg.Views[(ehan, Owner.Empire)] = new View { Owner = Owner.Alliance, Parent = at };
        Post(eg, StoryPostKind.BountyAttack, ehan, SideSet.Both, 1, 0);
        m = Assert.Single(Stories(elog));
        Assert.Equal((TextId.BountyHuntersLocateSolo, TextId.BountyHuntersReportedHan, N(at), 1054, 1143, AdvisorCode.BountyHuntersFoundHan, ehan.Ref), (m.Title!.Template, m.Body![0].Template, m.Body[0].Args[0].Text, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Empty(evoices);
        eg.Views[(ehan, Owner.Empire)] = new View { Owner = Owner.Alliance };
        Post(eg, StoryPostKind.BountyAttack, ehan, SideSet.Both, 1, 0);
        Assert.Empty(Stories(elog));
        Post(eg, StoryPostKind.BountyAttack, ehan, SideSet.Only(Owner.Alliance), 1, 0); // not posted by an Empire view
        Assert.Empty(Stories(elog));
    }

    // Leia branch: the trainer text vs the other text. A discoverer of the other definition side is invalid.
    [GameFact]
    public void ForceUserDiscovered()
    {
        var (game, log, voices) = New();
        var (luke, leia, vader) = (game.Known.Luke!, game.Known.Leia!, game.Known.Vader!);
        Post(game, StoryPostKind.ForceUserDiscovered, luke, SideSet.Both, 0, 1, leia);
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.ForceDiscovered, TextId.LeiaUsesForce, TextId.MyHeritageAsMember, leia.Ref, Pic(game, leia), 1042, 1148, AdvisorCode.LeiaMessage), (m.Type, m.Title!.Template, m.Body![0].Template, m.Subject, m.Overlay, m.Picture, m.Sound, m.AdvisorCode));
        Assert.Equal([292], voices);

        var minor = game.Characters.First(c => Family.Of(c.Type) >= Families.MinorCharacter && Finders.DefinitionSide(c) == Owner.Alliance);
        luke.ForceRank = 0;
        Post(game, StoryPostKind.ForceUserDiscovered, luke, SideSet.Both, 0, 1, minor);
        m = Assert.Single(Stories(log));
        Assert.Equal((TextId.FutureJediDiscovered, N(minor), N(luke), TextId.ForceUserDetectedOnce, N(minor), Pic(game, luke), AdvisorCode.LukeMessage), (m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Body![0].Template, m.Body[0].Args[0].Text, m.Overlay, m.AdvisorCode));
        Assert.Equal(309, voices[^1]);
        Assert.NotEqual(0u, luke.Definition.IsJediTrainer);
        luke.ForceRank = game.Rules.JediTeacherMinForce;
        Post(game, StoryPostKind.ForceUserDiscovered, luke, SideSet.Both, 0, 1, minor);
        Assert.Equal(TextId.ForceUserDetectedWe, Assert.Single(Stories(log)).Body![0].Template);

        Post(game, StoryPostKind.ForceUserDiscovered, vader, SideSet.Both, 0, 1, minor); // definition side 2 != the message side
        Assert.Empty(Stories(log));
    }

    // A first Force (0 gives n) gives none. n gives m gives the two rank texts with the rank title (kind 7).
    [GameFact]
    public void ForceGrowth()
    {
        var (game, log, voices) = New();
        var luke = game.Known.Luke!;
        Post(game, StoryPostKind.ForceChanged, luke, SideSet.Both, 0, 15);
        Assert.Empty(Stories(log));
        luke.ForceRank = 85;
        luke.ForceBonus = 0;
        Post(game, StoryPostKind.ForceChanged, luke, SideSet.Both, 15, 85);
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.ForceGrowth, TextId.ForceGrowth, N(luke), TextId.MyRecentExperienceGiven, "#34641", luke.Ref, Pic(game, luke), AdvisorCode.LukeMessage), (m.Type, m.Title!.Template, m.Title.Args[0].Text, m.Body![0].Template, m.Body[0].Args[0].Text, m.Subject, m.Overlay, m.AdvisorCode));
        Assert.Equal([306], voices);
        luke.DagobahCompleted = true;
        Post(game, StoryPostKind.ForceChanged, luke, SideSet.Both, 85, 90);
        Stories(log);
        Assert.Equal(307, voices[^1]);
        Post(game, StoryPostKind.ForceChanged, game.Known.Vader!, SideSet.Both, 10, 20); // definition side of the enemy
        Assert.Empty(Stories(log));
        Assert.Equal((34644, 34633, 34640, 34641, 34642, 34643), (Messages.RankTitle(9), Messages.RankTitle(10), Messages.RankTitle(79), Messages.RankTitle(80), Messages.RankTitle(119), Messages.RankTitle(120)));
    }

    // Final battle won / lost. Picture by DagobahCompleted.
    [GameFact]
    public void FinalBattle()
    {
        var (game, log, voices) = New();
        var luke = game.Known.Luke!;
        Post(game, StoryPostKind.FinalBattle, null, SideSet.Both, 0, 1);
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.ForceEncounter, TextId.FinalBattle, TextId.LukeBroughtBeforeEmperorVaderLuke, 1063, 1138, luke.Ref, AdvisorCode.LukeMessage), (m.Type, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.Subject, m.AdvisorCode));
        Assert.Equal([284], voices);
        luke.Captured = true;
        luke.DagobahCompleted = true;
        Post(game, StoryPostKind.FinalBattle, null, SideSet.Both, 0, 1);
        m = Assert.Single(Stories(log));
        Assert.Equal((TextId.LukeBroughtBeforeEmperorVaderEmperor, 1064, 1139), (m.Body![0].Template, m.Picture, m.Sound));
        Assert.Equal(303, voices[^1]);
    }

    // Heritage: Luke captured with the DarthToEmperor mission gives TextId.LukeTakenToEmperor, else TextId.LukeCapturedVader. Free and unhurt Luke with Vader
    // injured gives TextId.LukeEscapedCaptureAfter and TextId.VaderInjuredDuringBattle.
    [GameFact]
    public void Heritage()
    {
        var (game, log, voices) = New();
        var (luke, vader, pal) = (game.Known.Luke!, game.Known.Vader!, game.Known.Palpatine!);
        void Duel(int g)
        {
            Post(game, StoryPostKind.ForceEncounter, luke, SideSet.Both, 1, 0, group: g);
            Post(game, StoryPostKind.ForceEncounter, vader, SideSet.Both, 1, 0, group: g);
            Post(game, StoryPostKind.Heritage, luke, SideSet.Both, 0, 1, group: g);
        }
        Duel(1);
        luke.Injury = vader.Injury = 0;
        vader.Injury = 5;
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.ForceEncounter, TextId.LukeDiscoversHisHeritage, 1058, 1133, luke.Ref, AdvisorCode.LukeMessage), (m.Type, m.Title!.Template, m.Picture, m.Sound, m.Subject, m.AdvisorCode));
        Assert.Equal([TextId.LukeLearnedVaderHis, TextId.LukeEscapedCaptureAfter, TextId.VaderInjuredDuringBattle], m.Body!.Select(b => b.Template));
        Assert.Equal([286], voices);

        vader.Injury = 0;
        luke.Captured = true;
        luke.LiveSides = SideSet.Only(Owner.Alliance);
        var toEmperor = NewMission(game, (uint)MissionKind.Move, Owner.Empire);
        toEmperor.TargetKey = pal.Container;
        game.Views[(luke, Owner.Empire)] = new View { Owner = Owner.Empire, RoleMission = toEmperor };
        vader.Mission = toEmperor;
        Duel(2);
        Assert.Equal([TextId.LukeLearnedVaderHis, TextId.LukeTakenToEmperor], Assert.Single(Stories(log)).Body!.Select(b => b.Template));
        toEmperor.TargetKey = null;
        Duel(3);
        Assert.Equal([TextId.LukeLearnedVaderHis, TextId.LukeCapturedVader], Assert.Single(Stories(log)).Body!.Select(b => b.Template));
    }

    // Confrontation status lines (the five status texts and the picture table).
    [GameFact]
    public void Confrontation()
    {
        var (game, log, voices) = New();
        var (leia, luke, vader, pal) = (game.Known.Leia!, game.Known.Luke!, game.Known.Vader!, game.Known.Palpatine!);
        int g = 0;
        List<int> Body(Character a, Character e, out GameMessage m)
        {
            g++;
            Post(game, StoryPostKind.ForceEncounter, a, SideSet.Both, 0, 1, e, g);
            Post(game, StoryPostKind.ForceEncounter, e, SideSet.Both, 0, 1, a, g);
            m = Assert.Single(Stories(log));
            return [.. m.Body!.Select(b => b.Template)];
        }
        Assert.Equal([TextId.Fought, TextId.BothCombatantsEscapedUninjured], Body(leia, vader, out var m));
        Assert.Equal((TextId.Confronts, N(leia), N(vader), 1055, 1130, leia.Ref, AdvisorCode.LeiaMessage), (m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Picture, m.Sound, m.Subject, m.AdvisorCode));
        Assert.Equal(311, voices[^1]);
        leia.Captured = true;
        pal.Injury = 3;
        Assert.Equal([TextId.Fought, TextId.WasInjuredTitle, TextId.BothCombatantsEscapedUninjuredTitle], Body(leia, pal, out m));
        Assert.Equal((1056, 1131, 312), (m.Picture, m.Sound, voices[^1]));
        luke.Injury = 2;
        vader.Captured = true;
        vader.LiveSides = SideSet.Both; // Captured is read from the side's view: live here
        Assert.Equal([TextId.Fought, TextId.WasInjured, TextId.CombatantCaptured], Body(luke, vader, out m));
        Assert.Equal((1059, 1134, 280), (m.Picture, m.Sound, voices[^1]));
        luke.DagobahCompleted = true;
        Body(luke, pal, out m);
        Assert.Equal((1062, 1137, 283), (m.Picture, m.Sound, voices[^1]));
    }

    // Detection for each side (own / other, overlay own, voice). Invalid without the enemy's post. Definition side 0 kills the group.
    [GameFact]
    public void Detection()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            var own = game.Characters.First(c => Family.Of(c.Type) >= Families.MinorCharacter && Finders.DefinitionSide(c) == side);
            var other = game.Characters.First(c => Family.Of(c.Type) >= Families.MinorCharacter && Finders.DefinitionSide(c) is Owner.Alliance or Owner.Empire && Finders.DefinitionSide(c) != side);
            Post(game, StoryPostKind.ForceEncounter, own, SideSet.Both, 0, 1, other, 1);
            Post(game, StoryPostKind.ForceEncounter, other, SideSet.Both, 0, 1, own, 1);
            var m = Assert.Single(Stories(log));
            Assert.Equal((MessageType.ForceEncounter, TextId.DetectsEnemy, N(own), N(other), TextId.DetectedBecauseDisturbanceForce, Pic(game, own), own.Ref, AdvisorCode.MinorCharacterMessage), (m.Type, m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Body![0].Template, m.Overlay, m.Subject, m.AdvisorCode));
            Assert.Equal((side == Owner.Alliance ? 1042 : 1043, side == Owner.Alliance ? 1148 : 1149), (m.Picture, m.Sound));
            Assert.Empty(voices);
            Post(game, StoryPostKind.ForceEncounter, own, SideSet.Both, 0, 1, other, 2);
            Post(game, StoryPostKind.ForceEncounter, other, SideSet.Both.With(side, false), 0, 1, own, 2); // the side has no live view of the enemy participant
            Assert.Empty(Stories(log));
        }
        var (g2, log2, _) = New();
        var luke = g2.Known.Luke!;
        Post(g2, StoryPostKind.ForceEncounter, luke, SideSet.Both, 0, 1, null, 1);
        Post(g2, StoryPostKind.ForceEncounter, g2.Known.Vader!, SideSet.Both, 0, 1, null, 1);
        Assert.Equal(TextId.Confronts, Assert.Single(Stories(log2)).Title!.Template); // Luke against Vader without heritage: confrontation, not detection
        Post(g2, StoryPostKind.ForceEncounter, luke, SideSet.Both, 0, 1, null, 2);
        Post(g2, StoryPostKind.ForceEncounter, g2.Known.Han!, SideSet.Both, 0, 1, null, 2); // two Alliance participants: E stays none gives detection without other
        Assert.Empty(Stories(log2));
    }

    // The Dagobah flag set with Luke on a Dagobah mission is valid. The clear is invalid. None for the Empire. The picture 1042 / sound 1148.
    [GameFact]
    public void Dagobah()
    {
        var (game, log, voices) = New();
        var luke = game.Known.Luke!;
        luke.Mission = NewMission(game, (uint)MissionKind.Dagobah, Owner.Alliance);
        Post(game, StoryPostKind.LukeDagobah, null, SideSet.Both, 0, 1);
        Post(game, StoryPostKind.LukeDagobah, null, SideSet.Both, 1, 0);
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.Dagobah, 8, TextId.LukeGoesDagobah, TextId.LukeSentDagobahTrained, 1057, 1132, AdvisorCode.LukeMessage, 0u), (m.Type, m.Kind, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Equal([285], voices);
        luke.Mission = null;
        Post(game, StoryPostKind.LukeDagobah, null, SideSet.Both, 0, 1);
        Assert.Empty(Stories(log));
        Post(game, StoryPostKind.DagobahCompleted, luke, SideSet.Both, 0, 1);
        m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.Dagobah, 8, TextId.LukeLeavesDagobah, 1042, 1148, Pic(game, luke), AdvisorCode.LukeMessage), (m.Type, m.Kind, m.Title!.Template, m.Picture, m.Sound, m.Overlay, m.AdvisorCode));

        var (eg, elog, _) = New(Owner.Empire);
        eg.Known.Luke!.Mission = NewMission(eg, (uint)MissionKind.Dagobah, Owner.Alliance);
        Post(eg, StoryPostKind.LukeDagobah, null, SideSet.Both, 0, 1);
        Post(eg, StoryPostKind.DagobahCompleted, eg.Known.Luke!, SideSet.Both, 0, 1);
        Assert.Empty(Stories(elog));
    }

    // Emperor at Coruscant newly true: Empire only.
    [GameFact]
    public void SeatOfPower()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            var pal = game.Known.Palpatine!;
            bool at = game.EmperorAtCoruscant;
            pal.Captured = at; // flip it so the next update sees a change
            log.Update();
            Assert.False(game.EmperorAtCoruscant);
            pal.Captured = false;
            Assert.True(game.EmperorAtCoruscant);
            var added = Stories(log);
            if (side == Owner.Alliance) { Assert.Empty(added); continue; }
            var m = Assert.Single(added);
            Assert.Equal((MessageType.EmperorArrives, TextId.EmperorArrivesCoruscant, TextId.ReturnedToSeatOfPower, 1068, 1147, AdvisorCode.PalpatineMessage, pal.Ref), (m.Type, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
            Assert.Equal([304], voices);
        }
    }

    // The none post is invalid. Slot 3 = the enemy side. Code and voice by the discoverer.
    [GameFact]
    public void TraitorDiscovered()
    {
        var (game, log, voices) = New();
        var (leia, minor) = (game.Known.Leia!, game.Characters.First(c => Family.Of(c.Type) >= Families.MinorCharacter && Finders.DefinitionSide(c) == Owner.Alliance));
        Post(game, StoryPostKind.Traitor, leia, SideSet.Both, 0, 0, null);
        Assert.Empty(Stories(log));
        Post(game, StoryPostKind.Traitor, leia, SideSet.Both, 0, 1, minor);
        var m = Assert.Single(Stories(log));
        Assert.Equal((MessageType.Traitor, TextId.DiscoversTraitor, N(leia), N(minor), TextId.TraitorThroughForce, Owner.Empire, leia.Ref, AdvisorCode.LeiaMessage, Pic(game, leia)),
            (m.Type, m.Title!.Template, m.Title.Args[0].Text, m.Title.Args[1].Text, m.Body![0].Template, m.Body[0].Args[2].Side, m.Subject, m.AdvisorCode, m.Overlay));
        Assert.Equal([295], voices);
        Post(game, StoryPostKind.Traitor, game.Known.Vader!, SideSet.Both, 0, 1, minor); // the enemy's definition side
        Assert.Empty(Stories(log));
    }

    // Alliance only, kind 8, category 0, the leader of a Palace mission.
    [GameFact]
    public void RescueAttempt()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log, voices) = New(side);
            var leia = game.Known.Leia!;
            var palace = NewMission(game, (uint)MissionKind.Palace, Owner.Alliance);
            palace.Leader = leia;
            Post(game, StoryPostKind.PalaceAgentEnroute, leia, SideSet.Both, 0, 1, palace);
            var added = Stories(log);
            if (side == Owner.Empire) { Assert.Empty(added); continue; }
            var m = Assert.Single(added);
            Assert.Equal((MessageType.PalaceRescue, 8, MessageCategory.None, TextId.RescueAttempt, N(leia), TextId.HanTakenToJabbasPalace, 1042, 1148, Pic(game, leia), AdvisorCode.LeiaMessage, leia.Ref),
                (m.Type, m.Kind, m.Category, m.Title!.Template, m.Title.Args[0].Text, m.Body![0].Template, m.Picture, m.Sound, m.Overlay, m.AdvisorCode, m.Subject));
            Assert.Equal([229], voices);
            Assert.True(m.Listed(MessageCategory.None) && !m.Listed(MessageCategory.Mission));
            Post(game, StoryPostKind.PalaceAgentEnroute, leia, SideSet.Both, 0, 1, NewMission(game, (uint)MissionKind.Diplomacy, Owner.Alliance)); // not a Palace mission
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
            Post(game, StoryPostKind.PickupInProgress, null, SideSet.Both, 0, 1);
            Post(game, StoryPostKind.PickupInProgress, null, SideSet.Both, 1, 0);
            var m = Assert.Single(Stories(log));
            Assert.Equal((MessageType.JabbaPrisoners, TextId.JabbaDeliversPrisoners, TextId.JabbaDeliveredLukeHan, 1066, 1145, AdvisorCode.VaderMessage, game.Known.Vader!.Ref), (m.Type, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
            game.Known.Luke!.DagobahCompleted = true;
            Post(game, StoryPostKind.PickupInProgress, null, SideSet.Both, 0, 1);
            m = Assert.Single(Stories(log));
            Assert.Equal((1067, 1146), (m.Picture, m.Sound));
            Assert.Equal([291, 313], voices);
        }
    }

    // The Sim posts: discovery on the discoverer, the pairing and the resolution in two groups (the pairing sets and the resolution clears form one group each),
    // Force changes, the bounty clear. Each duel gives two messages.
    [GameFact]
    public void SimPostsTheNotifications()
    {
        var (game, log, _) = New();
        var (luke, leia, vader) = (game.Known.Luke!, game.Known.Leia!, game.Known.Vader!);
        var trace = game.StoryTrace!;

        Objects.SetParent(game, leia, luke.Container!);
        (leia.JediPotential, leia.KnownJedi, leia.ForceRank) = (true, false, 0);
        Story.SetLukeKnowsHeritage(game, luke);
        Jedi.SetForceRank(game, luke, game.Rules.ForceDiscoveryMinForce);
        game.Clock.Flush(game);
        trace.Clear();
        Jedi.Discover(game, luke);
        var d = Assert.Single(trace, e => e.Kind == StoryPostKind.ForceUserDiscovered);
        Assert.Equal((luke, leia, true), (d.Key, d.Other, d.Live.Alliance));
        Assert.Contains(trace, e => e.Kind == StoryPostKind.ForceChanged && e.Key == leia && e.Old == 0 && e.New == leia.Force); // the rank roll
        Jedi.Discover(game, luke); // no new discovery, no post
        Assert.Single(trace, e => e.Kind == StoryPostKind.ForceUserDiscovered);

        trace.Clear();
        luke.KnowsHeritage = false;
        vader.Observed = vader.Observed.With(Owner.Alliance, true); // the Alliance sees Vader live (both participants post to it)
        Knowledge.RecomputeLive(game, vader);
        Jedi.Pair(game, luke, vader);
        Assert.Equal([StoryPostKind.ForceEncounter, StoryPostKind.ForceEncounter], trace.Select(e => e.Kind));
        int pairing = trace[0].Group;
        Assert.True(pairing != 0 && trace[1].Group == pairing && trace.All(e => e.New == 1));
        Jedi.Duel(game, vader, luke);
        var resolution = trace.Skip(2).Where(e => e.Kind is StoryPostKind.ForceEncounter or StoryPostKind.Heritage).ToList();
        Assert.Equal([StoryPostKind.ForceEncounter, StoryPostKind.ForceEncounter, StoryPostKind.Heritage], resolution.Select(e => e.Kind));
        Assert.True(resolution.All(e => e.Group == resolution[0].Group) && resolution[0].Group != pairing);
        Assert.Equal(0, game.StoryGroup);
        var duel = Stories(log).Where(m => m.Type == MessageType.ForceEncounter).ToList();
        Assert.Equal(2, duel.Count); // the pairing's confrontation and the resolution's heritage
        Assert.Equal((TextId.Confronts, TextId.LukeDiscoversHisHeritage), (duel[0].Title!.Template, duel[1].Title!.Template));
    }

    // Manager posts (the FinalBattle flag set, the LukeDagobah flag set) reach both sides. A Palace agent's EnrouteActive rising
    // posts its mission message.
    [GameFact]
    public void SimPostsTheManagerAndPalacePosts()
    {
        var (game, _, _) = New();
        var trace = game.StoryTrace!;
        Story.Set(game, StoryFlag.FinalBattle, true);
        var f = Assert.Single(trace);
        Assert.Equal((StoryPostKind.FinalBattle, SideSet.Both, 1L), (f.Kind, f.Live, f.New));
        trace.Clear();
        var leia = game.Known.Leia!;
        var palace = NewMission(game, (uint)MissionKind.Palace, Owner.Alliance);
        leia.Mission = palace;
        Objects.SetEnrouteActive(game, leia, true);
        var e = Assert.Single(trace, x => x.Kind == StoryPostKind.PalaceAgentEnroute);
        Assert.Equal((leia, palace, 1L), (e.Key, e.Other, e.New));
    }
}
