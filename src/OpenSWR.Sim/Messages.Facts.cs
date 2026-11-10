using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public sealed partial class Messages
{
    void Facts()
    {
        var facts = _game.FactTrace!;
        var objects = _game.Created.Concat(_game.Characters).Concat(_game.Systems).Concat(_game.Missions).GroupBy(o => o.Ref).ToDictionary(g => g.Key, g => g.First());
        var deployed = new List<(int Day, GameObject Unit)>();
        var arrived = new List<(int Day, GameObject Unit)>();
        var lost = new List<(int Day, GameObject Unit)>();
        for (; _fact < facts.Count; _fact++)
        {
            var f = facts[_fact];
            if (!objects.TryGetValue(f.Object, out var o)) continue;
            switch (f.Kind)
            {
                case FactKind.Completed when f.New == 1 && o.Owner == _side && o is not Character && Family.Of(o.Type) is >= Families.Troop and < Families.MajorCharacter:
                    deployed.Add((f.Day, o));
                    break;
                // agents report only from a movement mission [64, 80) (message type MessageType.PersonnelArrived); a member arriving at a mission's
                // target stays silent. A movement mission already ended at the build has left Mission empty (Inferred: keep those)
                case FactKind.HyperdriveChanged when f.New == 0 && o.Owner == _side && !o.Destroyed && o is not Agent { Mission: { Kind: not (>= MissionKind.Move and <= MissionKind.Adrift) } }:
                    arrived.Add((f.Day, o));
                    break;
                case FactKind.ExistingChanged when f.New == 0 && o.Destroyed && (o.Owner == _side || o.DestroyedReason == DestroyedReason.Sabotage && DeathStar.IsDeathStar(o.Type)) && Family.Of(o.Type) is >= Families.Troop and < Families.Mission:
                    lost.Add((f.Day, o));
                    break;
                case FactKind.TaskStatus when f.New != 0 && o is Mission m && m.Owner == _side && !m.Destroyed && m.Kind is not (>= MissionKind.Move and <= MissionKind.Adrift) && !(m.Kind == MissionKind.DeathStarSabotage && (MissionTaskStatus)f.New == MissionTaskStatus.Succeeded):
                    MissionReport(f.Day, m, (MissionTaskStatus)f.New);
                    break;
                case FactKind.CompletionStatus when f.New != 0 && o is Mission m && m.Owner == _side && m.Kind is not (>= MissionKind.Move and <= MissionKind.Adrift) && m.TaskStatus == MissionTaskStatus.Pending:
                    MissionEnded(f.Day, m, (MissionCompletion)f.New);
                    break;
            }
        }
        if (_ownsTrace) { facts.Clear(); _fact = 0; }
        Deployed(deployed);
        Arrived(arrived);
        Lost(lost);
    }

    string Name(GameObject? o) => o switch
    {
        null => "?",
        StarSystem s => _name(s.Definition.Common),
        Character c => _name(c.Definition.Common),
        Fleet f => $"{(_game.Definitions.Resolve(Refs.Fleet) is { } r ? _name(r.Common) : "Fleet")} {f.Id}",
        Mission m => _name(m.Record.Common),
        _ => _game.Definitions.Resolve(o.Type) is { } r ? _name(r.Common) : $"{Family.Of(o.Type)}:{SdRef.IndexOf(o.Type)}",
    };

    GameObject? Where(GameObject o) => Missions.Location(o);

    /// <summary>
    /// Type MessageType.Deployed (trigger: units completed at a manufacturing facility, category Manufacturing; kind 3: one group per side and context): one message per day and
    /// manufacturing manager (BuilderKey), units in completion order: one fighter squadron TextId.SquadronDeployed / TextId.NewSquadronDeployed, one Death Star TextId.NewDeathStarDeployedTitle /
    /// TextId.NewDeathStarDeployed, one capital ship TextId.NewNamedUnitDeployedTitle / TextId.NewNamedUnitDeployed, one other unit TextId.NewUnitDeployedTitle / TextId.NewUnitDeployed; several TextId.DeployedTo / TextId.FollowingUnitsDeployed + one TextId.FollowingUnitsDeployedLine line each;
    /// texts, picture and location from the first deployed unit. Inferred approximation: the original groups by the
    /// context of the facility timer that delivered the point = the order command that woke the facility while the refined stock
    /// lasts (one command may name several managers, two commands to one manager give two messages), but the refill event of a
    /// Refinery chain when the facility had to wait for refined points (then one message across managers and systems).
    /// shortcut: grouped per manager, not per context; upgrade (carry a context through events and timer slots) when a recording
    /// shows two messages for one manager on one day, or one message across managers (open question).
    /// </summary>
    void Deployed(List<(int Day, GameObject Unit)> units)
    {
        foreach (var g in units.GroupBy(u => (u.Day, u.Unit.BuilderKey as GameObject ?? Where(u.Unit))))
        {
            var list = g.Select(u => u.Unit).ToList();
            var first = list[0];
            var at = Where(first);
            uint f = Family.Of(first.Type);
            int pic = Pic(first); // picture = the first deployed unit of the bundle (the handler covers only the first deployed notification)
            if (list.Count > 1)
                Tag(Add(g.Key.Day, MessageType.Deployed, MessageCategory.Manufacturing, 3, T(TextId.DeployedTo, Name(first), Name(at)),
                    [T(TextId.FollowingUnitsDeployed, "", Name(at)), .. list.Select(u => T(TextId.FollowingUnitsDeployedLine, Name(u)))], at, pic), AdvisorCode.Deployed);
            else if (f is >= Families.Fighter and < Families.Headquarters) // squadron: sound 1111 / 1112 by the message side; other cases none
                Tag(Add(g.Key.Day, MessageType.Deployed, MessageCategory.Manufacturing, 3, T(TextId.SquadronDeployed, Name(first), Name(at)), [T(TextId.NewSquadronDeployed, Name(first), Name(at))], first, pic, _side == Owner.Alliance ? 1111 : 1112), AdvisorCode.Deployed);
            else if (f is >= Families.DeathStar and < Families.Fighter) Tag(Add(g.Key.Day, MessageType.Deployed, MessageCategory.Manufacturing, 3, T(TextId.NewDeathStarDeployedTitle, Name(first), Name(at)), [T(TextId.NewDeathStarDeployed, Name(first), Name(at))], first, pic), AdvisorCode.Deployed);
            else if (f is >= Families.CapitalShip and < Families.DeathStar) Tag(Add(g.Key.Day, MessageType.Deployed, MessageCategory.Manufacturing, 3, T(TextId.NewNamedUnitDeployedTitle, Name(first), Name(at)), [T(TextId.NewNamedUnitDeployed, Name(first), Name(at))], first, pic), AdvisorCode.Deployed);
            else Tag(Add(g.Key.Day, MessageType.Deployed, MessageCategory.Manufacturing, 3, T(TextId.NewUnitDeployedTitle, Name(first), Name(at)), [T(TextId.NewUnitDeployed, Name(first), Name(at))], first, pic), AdvisorCode.Deployed);
        }
    }

    /// <summary>
    /// Arrivals (FactKind.HyperdriveChanged to 0) of own objects, type MessageType.Arrived, none when the
    /// location system's BattlePending is set (an arrival into a pending battle): every fleet to TextId.FleetArrives / TextId.Arrived (category Fleet,
    /// picture 1018 / 1019, sound 1114, code AdvisorCode.ShipsArrived); capital ships / Death Stars [20, 28) travelling alone (delivery, solo moves) to 
    /// TextId.ShipsArrive / TextId.FollowingShipsArrived + one TextId.FollowingUnitsArrivedLine line each (category Fleet, 1018 / 1019, 1114, code AdvisorCode.ShipsArrived); the Alliance HQ to TextId.HeadquartersArrives / TextId.AllianceHeadquartersArrived (category Defense, 1022, 1117,
    /// code AdvisorCode.UnitsArrived; an Empire HQ gives none); agents to type MessageType.PersonnelArrived (category Mission): one TextId.Arrives / TextId.IHaveArrived, several TextId.PersonnelArrive / TextId.FollowingPersonnelArrived + TextId.FollowingPersonnelArrivedLine
    /// lines; other units to TextId.UnitsArrive / TextId.FollowingUnitsArrived + TextId.FollowingUnitsArrivedLine lines (1020 / 1021, 1115 / 1116, code AdvisorCode.UnitsArrived; category by the first unit's family).
    /// Grouped per day and location (Inferred stand-in for the original's bundle per event context, whose first arrival picks the
    /// case); BattlePending read at the build (a battle with the player's arriving unit holds the clock and builds the messages
    /// first, MainScreen.EventFirst; the view of the system is live through the own unit there, Inferred).
    /// </summary>
    void Arrived(List<(int Day, GameObject Unit)> units)
    {
        foreach (var g in units.GroupBy(u => (u.Day, Where(u.Unit))))
        {
            var at = g.Key.Item2;
            bool valid = at is StarSystem { BattlePending: false }; // type MessageType.Arrived: no S, or a pending battle there, gives none
            int e = _side == Owner.Alliance ? 0 : 1;
            foreach (var fleet in g.Select(u => u.Unit).OfType<Fleet>().Where(_ => valid))
                Tag(Add(g.Key.Day, MessageType.Arrived, MessageCategory.Fleet, 3, T(TextId.FleetArrives, Name(fleet), Name(at)), [T(TextId.Arrived, Name(fleet), Name(at))], fleet, 1018 + e, 1114), AdvisorCode.ShipsArrived);
            var ships = g.Select(u => u.Unit).Where(u => valid && Family.Of(u.Type) is >= Families.CapitalShip and < Families.Fighter).ToList();
            if (ships.Count > 0)
                Tag(Add(g.Key.Day, MessageType.Arrived, MessageCategory.Fleet, 3, T(TextId.ShipsArrive, Name(ships[0]), Name(at)), [T(TextId.FollowingShipsArrived, Name(ships[0]), Name(at)), .. ships.Select(u => T(TextId.FollowingUnitsArrivedLine, Name(u)))], at, 1018 + e, 1114), AdvisorCode.ShipsArrived);
            if (valid && _side == Owner.Alliance) // case 4 for side 2 falls into the default branch: invalid
                foreach (var hq in g.Select(u => u.Unit).Where(u => Family.Of(u.Type) is >= Families.Headquarters and < Families.IonCannon))
                    Tag(Add(g.Key.Day, MessageType.Arrived, MessageCategory.Defense, 3, T(TextId.HeadquartersArrives, Name(hq), Name(at)), [T(TextId.AllianceHeadquartersArrived, Name(hq), Name(at))], hq, 1022, 1117), AdvisorCode.UnitsArrived);
            // type MessageType.PersonnelArrived: picture 1042 / 1043, sound 1148 / 1149 by the message side; code = the reported agent's (Inferred: the first
            // member) character code whatever the count, AdvisorCode.MinorCharacterMessage for anyone but a major character
            var agents = g.Select(u => u.Unit).OfType<Agent>().ToList();
            int picture = _side == Owner.Alliance ? 1042 : 1043, sound = _side == Owner.Alliance ? 1148 : 1149;
            // overlay = the arriving leader (Inferred: the first agent, a character or a special force)
            int overlay = agents.FirstOrDefault() is { } leader ? Pic(leader) : 0;
            if (agents.Count == 1)
                Over(Tag(Add(g.Key.Day, MessageType.PersonnelArrived, MessageCategory.Mission, 3, T(TextId.Arrives, Name(agents[0]), Name(at)), [T(TextId.IHaveArrived, Name(agents[0]), Name(at))], agents[0], picture, sound), Code(agents[0]), agents[0], ArriveVoices), overlay);
            else if (agents.Count > 1)
                Over(Tag(Add(g.Key.Day, MessageType.PersonnelArrived, MessageCategory.Mission, 3, T(TextId.PersonnelArrive, "", Name(at)), [T(TextId.FollowingPersonnelArrived, "", Name(at)), .. agents.Select(a => T(TextId.FollowingPersonnelArrivedLine, Name(a)))], at, picture, sound), Code(agents[0])), overlay);
            var units2 = g.Select(u => u.Unit).Where(u => valid && u is not (Fleet or Agent) && Family.Of(u.Type) is not (>= Families.CapitalShip and < Families.Fighter or >= Families.Headquarters and < Families.IonCannon or >= Families.MajorCharacter and < Families.Mission)).ToList();
            if (units2.Count > 0)
                Tag(Add(g.Key.Day, MessageType.Arrived, UnitCategory(units2[0]), 3, T(TextId.UnitsArrive, Name(units2[0]), Name(at)), [T(TextId.FollowingUnitsArrived, Name(units2[0]), Name(at)), .. units2.Select(u => T(TextId.FollowingUnitsArrivedLine, Name(u)))], at,
                    Family.IsFacility(units2[0].Type) ? Pic(units2[0]) : 1020 + e, _side == Owner.Alliance ? 1115 : 1116), AdvisorCode.UnitsArrived); // openswr deviation (player request): a facility shows its own picture, the original 1020 / 1021
        }
    }

    /// <summary>
    /// Type MessageType.Arrived units case category by the first unit's family: defences [34, 40) Defense, processing
    /// facilities [40, 48) Manufacturing, troops / fighters Defense in a system (container, Inferred: the parent) else Fleet; other families keep
    /// the constructor's value, not read (Inferred: Fleet).
    /// </summary>
    static MessageCategory UnitCategory(GameObject u) => Family.Of(u.Type) switch
    {
        >= Families.IonCannon and < Families.Shipyard => MessageCategory.Defense,
        >= Families.Shipyard and < Families.MajorCharacter => MessageCategory.Manufacturing,
        >= Families.Troop and < Families.CapitalShip or >= Families.Fighter and < Families.Headquarters => u.Container is StarSystem ? MessageCategory.Defense : MessageCategory.Fleet,
        _ => MessageCategory.Fleet,
    };

    /// <summary>
    /// Losses (FactKind.ExistingChanged, Destroyed) by DestroyedReason, per day and location: DestroyedReason.MaintenanceScrap to MessageType.MaintenanceScrap (category Manufacturing,
    /// TextId.MaintenanceShortfall / TextId.FollowingUnitsDestroyedDue + TextId.FollowingUnitsDestroyedDueLine lines, picture 1013, sound 1110); DestroyedReason.Sabotage to MessageType.Sabotage (category Conflict, TextId.SaboteursStrike / TextId.FollowingUnitsWereDestroyed + TextId.FollowingUnitsWereDestroyedLine lines);
    /// DestroyedReason.Assassination to MessageType.Assassination (category Mission, kind 8, TextId.KilledImperialAssassinsTitle / TextId.KilledImperialAssassins, picture 1070, sound 1140); DestroyedReason.RanBlockade to MessageType.EvacuationLosses (category Conflict,
    /// TextId.EvacuationLosses / TextId.FollowingUnitsWereLost + TextId.FollowingUnitsWereLostLine lines, picture 1031 / 1030 and sound 1120 / 1119 for message side A / E); DestroyedReason.Overflow (facility destroyed on arrival, Inferred) to MessageType.FacilityLost
    /// (category Manufacturing, TextId.FacilityLost / TextId.NewCouldNotDeployed). Scrap (DestroyedReason.Scrapped), cancelled production (DestroyedReason.ProductionCancelled), battle, bombardment and assault losses: none.
    /// </summary>
    void Lost(List<(int Day, GameObject Unit)> units)
    {
        foreach (var g in units.GroupBy(u => (u.Day, Where(u.Unit) ?? u.Unit.Container, u.Unit.DestroyedReason)))
        {
            var list = g.Select(u => u.Unit).ToList();
            var at = g.Key.Item2;
            switch (g.Key.DestroyedReason)
            {
                case DestroyedReason.MaintenanceScrap:
                    Tag(Add(g.Key.Day, MessageType.MaintenanceScrap, MessageCategory.Manufacturing, 3, T(TextId.MaintenanceShortfall), [T(TextId.FollowingUnitsDestroyedDue, "", "", Name(at)), .. list.Select(u => T(TextId.FollowingUnitsDestroyedDueLine, Name(u)))], at, 1013, 1110), AdvisorCode.MaintenanceShortfall);
                    break;
                case DestroyedReason.Sabotage when list.Any(u => DeathStar.IsDeathStar(u.Type)):
                    // type MessageType.DeathStarSabotaged (the sabotage destruction of a Death Star, category Mission, kind 6, scene EventScene.DeathStarSabotaged; both sides, Inferred)
                    foreach (var u in list.Where(u => DeathStar.IsDeathStar(u.Type)))
                        Event(g.Key.Day, MessageType.DeathStarSabotaged, MessageCategory.Mission, EventScene.DeathStarSabotaged, T(TextId.DeathStarSabotaged), T(TextId.RebelAllianceSabotagedDeath, Name(at)));
                    break;
                case DestroyedReason.Sabotage when list.Any(u => u.Owner == _side):
                    Add(g.Key.Day, MessageType.Sabotage, MessageCategory.Conflict, 3, T(TextId.SaboteursStrike, "", "", Name(at)), [T(TextId.FollowingUnitsWereDestroyed, "", "", Name(at)), .. list.Select(u => T(TextId.FollowingUnitsWereDestroyedLine, Name(u)))], at, 1013, 1110);
                    break;
                case DestroyedReason.Assassination:
                    foreach (var u in list)
                        Add(g.Key.Day, MessageType.Assassination, MessageCategory.Mission, 8, T(TextId.KilledImperialAssassinsTitle, Name(u)), [T(TextId.KilledImperialAssassins, Name(u), "", Name(at))], at, 1070, 1140);
                    break;
                case DestroyedReason.RanBlockade:
                    Tag(Add(g.Key.Day, MessageType.EvacuationLosses, MessageCategory.Conflict, 3, T(TextId.EvacuationLosses), [T(TextId.FollowingUnitsWereLost, "", Name(at)), .. list.Select(u => T(TextId.FollowingUnitsWereLostLine, Name(u)))], at,
                        _side == Owner.Alliance ? 1031 : 1030, _side == Owner.Alliance ? 1120 : 1119), AdvisorCode.BlockadeLosses);
                    break;
                case DestroyedReason.Overflow when list.All(u => Family.IsFacility(u.Type)):
                    foreach (var u in list)
                        Tag(Add(g.Key.Day, MessageType.FacilityLost, MessageCategory.Manufacturing, 3, T(TextId.FacilityLost), [T(TextId.NewCouldNotDeployed, Name(u), Name(at))], at), AdvisorCode.Deployed);
                    break;
            }
        }
    }

    /// <summary>
    /// A kind-6 full-screen event: not listed; subject = the scene id. Not New (kind 6 is only queued on
    /// add, so it lights no category button; Inferred).
    /// </summary>
    void Event(int day, MessageType type, MessageCategory category, uint scene, MessageText title, MessageText body)
    {
        var m = Add(day, type, category, 6, title, [body]);
        (m.Subject, m.New, m.Read) = (scene, false, false);
    }
}
