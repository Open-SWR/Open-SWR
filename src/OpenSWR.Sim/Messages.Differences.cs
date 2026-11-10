using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public sealed partial class Messages
{
    void Differences()
    {
        int day = _game.Day;
        EnemyMissions(day);
        foreach (var s in _game.Systems)
        {
            if (!_systems.TryGetValue(s, out var was)) continue;
            var now = Knowledge.See(_game, s, _side);
            // type MessageType.PlanetDestroyed: the system view turned Destroyed (Death Star; category Event, kind 6, scene EventScene.PlanetDestroyed): TextId.DeathStarDestroyedSystem owned, TextId.DeathStarDestroyedNeutral neutral
            if (now.Destroyed && !was.Destroyed)
                Event(day, MessageType.PlanetDestroyed, MessageCategory.Event, EventScene.PlanetDestroyed, T(TextId.Destroyed, "", Name(s)),
                    was.Owner is Owner.Alliance or Owner.Empire ? T(TextId.DeathStarDestroyedSystem, MessageArg.Of(was.Owner), "", Name(s)) : T(TextId.DeathStarDestroyedNeutral, "", Name(s)));
            // MessageType.SystemJoins: owner change of the system view (category Support; picture 1005; sound 1104 joins either side, 1105 neutral)
            if (now.Owner != was.Owner && !now.Destroyed)
            {
                // advisor code AdvisorCode.GoodNews joins us / AdvisorCode.BadNews joins the enemy; declaring neutrality none
                if (now.Owner == _side) Tag(Add(day, MessageType.SystemJoins, MessageCategory.Support, 3, T(TextId.Joins, Name(s)), [T(TextId.PopularSupportCausedWorld, Name(s), MessageArg.Of(_side))], s, 1005, 1104), AdvisorCode.GoodNews);
                else if (now.Owner == Enemy) Tag(Add(day, MessageType.SystemJoins, MessageCategory.Support, 3, T(TextId.JoinsEnemy, Name(s)), [T(TextId.PopularDissentCausedWorld, Name(s), MessageArg.Of(Enemy))], s, 1005, 1104), AdvisorCode.BadNews);
                else if (was.Owner is Owner.Alliance or Owner.Empire) Add(day, MessageType.SystemJoins, MessageCategory.Support, 3, T(TextId.DeclaresNeutrality, Name(s)), [T(TextId.DividedLoyaltyCausedWorld, Name(s), MessageArg.Of(was.Owner))], s, 1005, 1105);
            }
            // MessageType.Uprising: Uprising of the system view, owner side 1 or 2: picture 1010 begins, ends 1011 Alliance- / 1012 Empire-owned;
            // sound 1108 / 1109; code AdvisorCode.GoodNews (good news) when (view owner = message side) != the new Uprising value, else AdvisorCode.BadNews
            if (now.Uprising != was.Uprising && now.Owner is Owner.Alliance or Owner.Empire && !now.Destroyed)
            {
                var code = (now.Owner == _side) != now.Uprising ? AdvisorCode.GoodNews : AdvisorCode.BadNews;
                if (now.Uprising) Tag(Add(day, MessageType.Uprising, MessageCategory.Support, 3, T(TextId.UprisingBegins, Name(s)), [T(TextId.UprisingBegunControlledSystem, MessageArg.Of(now.Owner), Name(s))], s, 1010, 1108), code);
                else Tag(Add(day, MessageType.Uprising, MessageCategory.Support, 3, T(TextId.UprisingEnds, Name(s)), [T(TextId.UprisingEnded, "", Name(s))], s, now.Owner == Owner.Alliance ? 1011 : 1012, 1109), code);
            }
            NearUprising(day, s, now);
            Smuggling(day, s, now);
            // MessageType.Blockade: Blockade set in the view: an own fleet blockades (TextId.FleetInitiatesBlockade / TextId.InitiatedBlockade) or the own system is blockaded (TextId.UnderBlockade / TextId.ShipsDetectedWorldUnder)
            if (now.Blockade && !was.Blockade)
            {
                if (now.Owner == _side) Tag(Add(day, MessageType.Blockade, MessageCategory.Conflict, 3, T(TextId.UnderBlockade, Name(s)), [T(TextId.ShipsDetectedWorldUnder, MessageArg.Of(Enemy), Name(s))], s, 1028, 1118), AdvisorCode.SystemBlockaded);
                else if (s.Contents.OfType<Fleet>().FirstOrDefault(f => f.Owner == _side && f.Existing) is { } fleet)
                    Tag(Add(day, MessageType.Blockade, MessageCategory.Conflict, 3, T(TextId.FleetInitiatesBlockade, Name(s)), [T(TextId.InitiatedBlockade, "", Name(s), Name(fleet))], fleet, 1027, 1118), AdvisorCode.BlockadeStarted);
            }
        }
        foreach (var o in _game.Created.Where(Repairable))
            if (_damaged.GetValueOrDefault(o) && !ViewDamaged(o)) Repaired(day, o);
        // victory facts newly set: Coruscant held by the Alliance gives type MessageType.CoruscantCaptured (Support, scene EventScene.CoruscantCaptured) only when the side's view of
        // Coruscant shows the Alliance; HQ gone gives type MessageType.HeadquartersDestroyed (full-screen event, scene EventScene.HeadquartersDestroyed)
        var (victoryNow, victoryBefore) = (_game.VictoryState, _victory);
        if (victoryNow.CoruscantAlliance && !victoryBefore.CoruscantAlliance && _game.Known.Coruscant is { } cor
            && Knowledge.See(_game, cor, _side).Owner == Owner.Alliance)
            Event(day, MessageType.CoruscantCaptured, MessageCategory.Support, EventScene.CoruscantCaptured, T(TextId.CoruscantCaptured), T(TextId.AllianceSeizedImperialCapital));
        if (victoryNow.HqGone && !victoryBefore.HqGone) Event(day, MessageType.HeadquartersDestroyed, MessageCategory.Event, EventScene.HeadquartersDestroyed, T(TextId.AllianceHeadquartersDestroyed), T(TextId.EmpireDestroyedAllianceHeadquarters));
        var side = _game.Sides[_side];
        // MessageType.ResearchComplete: research order raised to one message per newly buildable item (kind 4, encyclopedia key = the item)
        for (int k = 0; k < 3 && k < _research.Length; k++)
            if (side.ResearchOrder[k] > _research[k])
                foreach (var r in Research.Candidates(_game, _side, Research.FacilityFamily(k)).Where(r => r.Get(SdField.ResearchOrder) > _research[k] && r.Get(SdField.ResearchOrder) <= side.ResearchOrder[k]))
                    Tag(Add(day, MessageType.ResearchComplete, MessageCategory.Manufacturing, 4, T(TextId.ResearchComplete, _name(r.Common)), [T(TextId.RDReportsNow, _name(r.Common))], null, 1017, 1113), AdvisorCode.ResearchComplete).Encyclopedia = r.Common.Ref;
        // type MessageType.ResearchExhausted research exhausted (kind 8), MessageType.RecruitmentDone recruitment done (kind 8)
        for (int k = 0; k < 3 && k < _exhausted.Length; k++)
            if (side.ResearchExhausted[k] && !_exhausted[k]) Add(day, MessageType.ResearchExhausted, MessageCategory.Manufacturing, 8, T(TextId.ResearchExhausted), [T(ExhaustedBody[k])], null, 1017, 1113);
        if (side.RecruitmentDone && !_recruitment) Add(day, MessageType.RecruitmentDone, MessageCategory.Mission, 8, T(TextId.RecruitmentDone), [T(TextId.NoMoreRecruitCandidates)]);
        // characters: type MessageType.CapturedOrEscaped captured / escaped (TextId.WasCapturedByTitle own captured by a side, TextId.WasCapturedAtTitle enemy captured by us; TextId.Escaped escaped), MessageType.InjuredOrHealed injured / healed / killed
        foreach (var c in _game.Characters.Where(c => c.Placed))
        {
            if (!_characters.TryGetValue(c, out var was)) continue;
            var at = Where(c);
            if (c.Destroyed && was.Owner == _side)
            {
                if (c.DestroyedReason != DestroyedReason.Assassination) Over(Tag(Add(day, MessageType.InjuredOrHealed, MessageCategory.Mission, 8, T(TextId.Killed, Name(c)), [T(TextId.Killed, Name(c))], null, 1050, 1140), Code(c)), Pic(c));
                continue;
            }
            if (c.Captured != was.Captured)
            {
                // picture captured 1048 for an Alliance character, 1049 otherwise; escaped 1029; no overlay
                int captured = Finders.DefinitionSide(c) == Owner.Alliance ? 1048 : 1049;
                if (c.Captured && was.Owner == _side) Tag(Add(day, MessageType.CapturedOrEscaped, MessageCategory.Mission, 3, T(TextId.WasCapturedByTitle, Name(c)), [T(TextId.WasCapturedBy, Name(c), Name(at), MessageArg.Of(c.Owner))], c, captured, 1100), CapturedCode(c));
                else if (c.Captured && c.Owner == _side) Tag(Add(day, MessageType.CapturedOrEscaped, MessageCategory.Mission, 3, T(TextId.WasCapturedAtTitle, Name(c)), [T(TextId.WasCapturedAt, Name(c), Name(at))], c, captured, 1100), CapturedCode(c));
                else if (!c.Captured && c.State == PrisonerState.Escaped && c.Owner == _side)
                    Tag(Add(day, MessageType.CapturedOrEscaped, MessageCategory.Mission, 3, T(TextId.Escaped, Name(c)), [T(TextId.EscapedImprisonment, Name(c), Name(at))], c, 1029, 1129), EscapedCode(c), c, EscapedVoices);
            }
            if (c.Owner == _side && !c.Captured)
            {
                // overlay = the character
                if (c.Injury > 0 && was.Injury == 0) Over(Tag(Add(day, MessageType.InjuredOrHealed, MessageCategory.Mission, 3, T(TextId.Injured, Name(c)), [T(TextId.HasBeen, Name(c))], c, 1050, 1140), Code(c)), Pic(c));
                else if (c.Injury == 0 && was.Injury > 0) Over(Tag(Add(day, MessageType.InjuredOrHealed, MessageCategory.Mission, 3, T(TextId.Healed, Name(c)), [T(TextId.HealedAllInjuries, Name(c))], c, 1051, 1140), Code(c), c, HealedVoices), Pic(c));
            }
        }
        // type MessageType.QueueEmpty ("... Idle on {1}"): the side's manager view count goes to 0 with view owner = the side. A view is live
        // when the manager is owned by the side; a live view counts the real queue, a frozen one keeps the count it had.
        // After an owner change the old owner gets none (its view now shows the new owner).
        // The new owner gets one when its frozen view count or the real queue count was not 0 before the
        // change. This holds on every owner-change path; a change to neutral gives nobody one. shortcut: both counts
        // are the ones before the update (a queue filled and cancelled inside one update, or an owner change and its reversal, is
        // not seen), upgrade if a recording shows such a case. The kind's facility family
        // (construction yard Families.ConstructionYard TextId.ConstructionYardIdle / TextId.ConstructionYardsCompletedAssigned, training Families.TrainingFacility TextId.TrainingFacilityIdle / TextId.TrainingFacilitiesNoLonger, shipyard Families.Shipyard TextId.ShipyardIdle / TextId.ShipyardsAvailableNewConstruction) must be among the
        // system's Usable contents (walk mode 1, key order); the first one gives the picture. Advisor
        // code AdvisorCode.Deployed, sound 1101.
        foreach (var (m, was) in _queues)
        {
            if (was.Count == 0 && (was.SystemOwner == m.System.Owner || was.Real == 0) || m.Owner != _side || m.Destroyed || Knowledge.QueueCount(_game, m, _side) != 0) continue;
            var (family, title, body) = m.Kind switch
            {
                ManufacturingKind.Construction => (42u, TextId.ConstructionYardIdle, TextId.ConstructionYardsCompletedAssigned), ManufacturingKind.Training => (41u, TextId.TrainingFacilityIdle, TextId.TrainingFacilitiesNoLonger), _ => (40u, TextId.ShipyardIdle, TextId.ShipyardsAvailableNewConstruction),
            };
            if (m.System.Contents.Where(x => x.Usable && Family.Of(x.Type) == family).OrderBy(x => x.Key).FirstOrDefault() is not { } yard) continue;
            Tag(Add(day, MessageType.QueueEmpty, MessageCategory.Manufacturing, 3, T(title, Name(m.System)), [T(body, Name(m.System))], m.System, Pic(yard), 1101), AdvisorCode.Deployed);
        }
    }

    // ---- economy and unrest: types MessageType.Repaired, MessageType.Smuggling, MessageType.NearUprising, MessageType.Informants, MessageType.ResourceEvent ----
    // shortcut (types MessageType.Repaired, MessageType.Smuggling, MessageType.NearUprising): diffs between updates stand for the notifications, so a value that changes and changes back
    // inside one update gives no message (the original gives one per notification); upgrade if a recording shows such a case.

    bool HasView(GameObject o) => Knowledge.IsLive(o, _side) || _game.Views.ContainsKey((o, _side));

    /// <summary>
    /// Type MessageType.NearUprising near uprising: the side's own system (view owner = side), Garrisoned, no Uprising, surplus old != 0
    /// to new = 0: TextId.NearUprising / TextId.UnrestPushedCloseUprising (system name), picture 1008 / 1009, sound 1106 / 1107 by the owner, category Support, advisor code AdvisorCode.BadNews.
    /// Own systems are live views, so the real surplus stands for the view copy (frozen views are not diffed).
    /// </summary>
    void NearUprising(int day, StarSystem s, Seen now)
    {
        if (!_surplus.TryGetValue(s, out int old) || !Knowledge.IsLive(s, _side) || old == 0 || s.GarrisonSurplus != 0) return;
        if (now.Owner != _side || !s.Garrisoned || now.Uprising) return;
        bool a = _side == Owner.Alliance;
        Tag(Add(day, MessageType.NearUprising, MessageCategory.Support, 3, T(TextId.NearUprising, Name(s)), [T(TextId.UnrestPushedCloseUprising, Name(s))], s, a ? 1008 : 1009, a ? 1106 : 1107), AdvisorCode.BadNews);
    }

    /// <summary>
    /// Type MessageType.Smuggling: only a begin (old 0 to nonzero) or an end (nonzero to 0); valid for view owner 1 with
    /// old >= 0 or view owner 2 with old <= 0. Own (view owner = side): TextId.SmugglingLosses + 0 begin, + 1 end; enemy: TextId.SmugglingBenefits + 0 begin, + 1 end;
    /// body = title + 4 (system name). Picture 1004, sound 1103 on a begin only, category Support, no advisor code.
    /// shortcut: the side's view value = the real value while live, else the last live value (frozen views keep no copy), so the
    /// first sight of an enemy system that already smuggles gives no "Smuggling Benefits"; upgrade with a View snapshot field.
    /// </summary>
    void Smuggling(int day, StarSystem s, Seen now)
    {
        if (_smuggling.GetValueOrDefault(s) is not int old || !Knowledge.IsLive(s, _side)) return;
        int value = s.SmugglingPercent;
        if (old == value || old != 0 && value != 0) return;
        if (!(now.Owner == Owner.Alliance && old >= 0 || now.Owner == Owner.Empire && old <= 0)) return;
        bool begin = old == 0;
        int title = (now.Owner == _side ? TextId.SmugglingLosses : TextId.SmugglingBenefits) + (begin ? 0 : 1);
        Add(day, MessageType.Smuggling, MessageCategory.Support, 3, T(title, Name(s)), [T(title + 4, Name(s))], s, 1004, begin ? 1103 : 0);
    }

    /// <summary>
    /// Type MessageType.Repaired (ViewMessageType.Damaged to 0): O = the side's view of a capital ship / Death Star [20, 28) or a squadron
    /// [28, 32), owner not the enemy. Attached to P: a ship's view parent (the fleet), category Fleet; a squadron's view parent Q,
    /// or Q's parent when Q is a capital ship (category Fleet), else Q itself (category Defense). Squadron TextId.SquadronFullStrength / TextId.SquadronAttachedReplacedAll (O, P),
    /// sound 1111 / 1112, code AdvisorCode.SquadronFull; capital ship TextId.CapitalShipRepaired / TextId.AttachedRepairedAllBattle, sound 1118, code AdvisorCode.ShipRepaired; picture = O's object picture; subject O.
    /// </summary>
    void Repaired(int day, GameObject o)
    {
        if (!HasView(o) || Knowledge.See(_game, o, _side).Owner == Enemy) return;
        bool squadron = Family.Of(o.Type) >= Families.Fighter;
        var p = Knowledge.ViewParent(_game, o, _side);
        var category = MessageCategory.Fleet;
        if (squadron && p is not null)
        {
            if (Family.Of(p.Type) is >= Families.CapitalShip and < Families.Fighter) p = Knowledge.ViewParent(_game, p, _side);
            else category = MessageCategory.Defense;
        }
        if (p is null || !HasView(p)) return;
        if (squadron) Tag(Add(day, MessageType.Repaired, category, 3, T(TextId.SquadronFullStrength, Name(o), Name(p)), [T(TextId.SquadronAttachedReplacedAll, Name(o), Name(p))], o, Pic(o), _side == Owner.Alliance ? 1111 : 1112), AdvisorCode.SquadronFull);
        else Tag(Add(day, MessageType.Repaired, category, 3, T(TextId.CapitalShipRepaired, Name(o), Name(p)), [T(TextId.AttachedRepairedAllBattle, Name(o), Name(p))], o, Pic(o), 1118), AdvisorCode.ShipRepaired);
    }
}
