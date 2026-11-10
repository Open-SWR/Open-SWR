using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Op classes: family, subtype, type index (system: a counter; object: the fleet-op count plus 4 times the index), mission type,
/// team size, and max failures. Object classes: espionage and recon for subtypes other than 6 and 5, abduction
/// (subtype 4) and assassination, and sabotage. Team sizes are 2, 1, 2 and 2 in that order.
/// </summary>
public sealed record OpClass(MissionKind Family, int Subtype, int Index, uint MissionType, int TeamSize, int MaxFailures, bool Object = false)
{
    public static readonly OpClass Espionage = new(MissionKind.Espionage, (int)OpSubtype.None, (int)ObjectOpIndex.Espionage, Refs.EspionageMission, 2, 10, true),
        Recon = new(MissionKind.Reconnaissance, (int)OpSubtype.None, (int)ObjectOpIndex.Reconnaissance, Refs.ReconnaissanceMission, 1, 1000, true),
        Abduction = new(MissionKind.Abduction, (int)OpSubtype.Abduction, (int)ObjectOpIndex.AbductionOrAssassination, Refs.AbductionMission, 2, 5, true),
        Assassination = new(MissionKind.Assassination, (int)OpSubtype.Abduction, (int)ObjectOpIndex.AbductionOrAssassination, Refs.AssassinationMission, 2, 5, true),
        Sabotage = new(MissionKind.Sabotage, (int)OpSubtype.None, (int)ObjectOpIndex.Sabotage, Refs.SabotageMission, 2, 10, true);

    public static readonly OpClass[] All =
    [
        new(MissionKind.Recruitment, (int)OpSubtype.None, (int)SystemOpIndex.Recruitment, Refs.RecruitmentMission, 1, 1000),
        new(MissionKind.Research, (int)OpSubtype.ShipResearch, (int)SystemOpIndex.ShipResearch, Refs.ShipResearchMission, 1, 1000),
        new(MissionKind.Research, (int)OpSubtype.TroopResearch, (int)SystemOpIndex.TroopResearch, Refs.TroopResearchMission, 1, 1000),
        new(MissionKind.Research, (int)OpSubtype.FacilityResearch, (int)SystemOpIndex.FacilityResearch, Refs.FacilityResearchMission, 1, 1000),
        new(MissionKind.SubdueUprising, (int)OpSubtype.None, (int)SystemOpIndex.SubdueUprising, Refs.SubdueMission, 1, 5),
        new(MissionKind.DeathStarSabotage, (int)OpSubtype.None, (int)SystemOpIndex.DeathStarSabotage, Refs.DeathStarSabotageMission, 2, 10),
        new(MissionKind.Abduction, (int)OpSubtype.None, (int)SystemOpIndex.Abduction, Refs.AbductionMission, 2, 10),
        new(MissionKind.Rescue, (int)OpSubtype.None, (int)SystemOpIndex.Rescue, Refs.RescueMission, 2, 10),
        new(MissionKind.Reconnaissance, (int)OpSubtype.Reconnaissance, (int)SystemOpIndex.Reconnaissance, Refs.ReconnaissanceMission, 1, 1000),
        new(MissionKind.Espionage, (int)OpSubtype.Espionage, (int)SystemOpIndex.Espionage, Refs.EspionageMission, 1, 1000),
        new(MissionKind.Diplomacy, (int)OpSubtype.None, (int)SystemOpIndex.Diplomacy, Refs.DiplomacyMission, 1, 5),
        new(MissionKind.InciteUprising, (int)OpSubtype.None, (int)SystemOpIndex.InciteUprising, Refs.InciteMission, 1, 10),
    ];

    public static OpClass? Of(MissionKind family, int subtype) => All.FirstOrDefault(c => c.Family == family && c.Subtype == subtype) ?? family switch
    {
        MissionKind.Espionage => Espionage, MissionKind.Reconnaissance => Recon, MissionKind.Assassination => Assassination, MissionKind.Sabotage => Sabotage,
        MissionKind.Abduction when subtype == (int)OpSubtype.Abduction => Abduction, _ => null,
    };
}
