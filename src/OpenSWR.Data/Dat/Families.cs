namespace OpenSWR.Data.Dat;

/// <summary>SdRef families (the family part of an SdRef) and the bounds of family ranges, half-open.</summary>
public static class Families
{
    public const uint Fleet = 8;
    /// <summary>Troop regiments [Troop, CapitalShip).</summary>
    public const uint Troop = 16;
    /// <summary>Capital ships [CapitalShip, Fighter), the Death Stars included.</summary>
    public const uint CapitalShip = 20;
    /// <summary>Death Stars [DeathStar, Fighter).</summary>
    public const uint DeathStar = 24;
    /// <summary>Fighter squadrons [Fighter, Headquarters).</summary>
    public const uint Fighter = 28;
    /// <summary>Headquarters [Headquarters, IonCannon); facilities [Headquarters, MajorCharacter).</summary>
    public const uint Headquarters = 32;
    /// <summary>The second headquarters family. Meaning in the original is unknown.</summary>
    public const uint SecondHeadquarters = 33;
    /// <summary>Defences [IonCannon, Shipyard); batteries [IonCannon, PlanetaryShield).</summary>
    public const uint IonCannon = 34;
    /// <summary>Defence family 35, counted with the batteries by the map filter. Meaning in the original is unknown.</summary>
    public const uint Gun = 35;
    public const uint PlanetaryShield = 36;
    public const uint DeathStarShield = 37;
    /// <summary>Upper bound of the Death Star shields.</summary>
    public const uint DeathStarShieldEnd = 38;
    /// <summary>Manufacturing facilities [Shipyard, ManufacturingEnd); processing facilities [Shipyard, MajorCharacter).</summary>
    public const uint Shipyard = 40;
    public const uint TrainingFacility = 41;
    public const uint ConstructionYard = 42;
    /// <summary>Upper bound of the manufacturing facilities.</summary>
    public const uint ManufacturingEnd = 43;
    public const uint Mine = 44;
    public const uint Refinery = 45;
    /// <summary>Upper bound of the refineries.</summary>
    public const uint RefineryEnd = 46;
    /// <summary>Major characters [MajorCharacter, MinorCharacter); characters [MajorCharacter, SpecialForce).</summary>
    public const uint MajorCharacter = 48;
    /// <summary>One family per major character after Mothma (MajorCharacter).</summary>
    public const uint Leia = 49, Luke = 50, Han = 51, Palpatine = 52, Vader = 53;
    public const uint MinorCharacter = 56;
    /// <summary>Special forces [SpecialForce, Mission).</summary>
    public const uint SpecialForce = 60;
    /// <summary>Missions [Mission, Sector); movement missions [Mission, AgentMission).</summary>
    public const uint Mission = 64;
    /// <summary>First family of the missions that are not movement missions.</summary>
    public const uint AgentMission = 80;
    public const uint Sector = 128;
    /// <summary>Core systems [CoreSystem, RimSystem); systems [CoreSystem, Abode).</summary>
    public const uint CoreSystem = 144;
    public const uint System = 144;
    /// <summary>Rim systems [RimSystem, RimSystemEnd).</summary>
    public const uint RimSystem = 146;
    public const uint RimSystemEnd = 148;
    /// <summary>Abodes (jails, Dagobah, Hermitage, Jabba's Palace).</summary>
    public const uint Abode = 152;
    public const uint ConstructionManager = 160;
    public const uint ShipyardManager = 162;
    public const uint TrainingManager = 164;
    /// <summary>Upper bound of the manufacturing managers.</summary>
    public const uint ManagerEnd = 176;
    /// <summary>The staging container (unknown parent).</summary>
    public const uint Staging = 242;
    public const uint Side = 243;
    public const uint MissionManager = 248;
    public const uint GameManager = 249;
    public const uint CharacterManager = 250;
    /// <summary>Family 241: the view mapping gives it no record. Meaning in the original is unknown.</summary>
    public const uint Unmapped = 241;
}
