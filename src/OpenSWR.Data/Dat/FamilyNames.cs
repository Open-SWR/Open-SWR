using System.Globalization;

namespace OpenSWR.Data.Dat;

/// <summary>
/// Family names and record ids as mods and messages spell them. A record of the install is "base:&lt;family&gt;:&lt;index&gt;":
/// the family by name (e.g. capitalShip), the index in decimal (e.g. base:capitalShip:65). Mission families are named
/// after the <see cref="MissionKind"/> values. A family without a name is written as its decimal number.
/// </summary>
public static class FamilyNames
{
    /// <summary>Named families of the install (one name per family; the system family is the core one).</summary>
    static readonly (uint Family, string Name)[] Fixed =
    [
        (0, "unknownFleet"), // the family of one FLEETSD record; meaning unknown
        (Families.Fleet, "fleet"),
        (Families.Troop, "troop"),
        (Families.CapitalShip, "capitalShip"),
        (Families.DeathStar, "deathStar"),
        (Families.Fighter, "fighter"),
        (Families.Headquarters, "headquarters"),
        (Families.SecondHeadquarters, "secondHeadquarters"), // no record in the install; meaning unknown
        (Families.IonCannon, "ionCannon"),
        (Families.Gun, "gun"),
        (Families.PlanetaryShield, "planetaryShield"),
        (Families.DeathStarShield, "deathStarShield"),
        (Families.Shipyard, "shipyard"),
        (Families.TrainingFacility, "trainingFacility"),
        (Families.ConstructionYard, "constructionYard"),
        (Families.Mine, "mine"),
        (Families.Refinery, "refinery"),
        (Families.MajorCharacter, "majorCharacter"),
        (Families.Leia, "leia"),
        (Families.Luke, "luke"),
        (Families.Han, "han"),
        (Families.Palpatine, "palpatine"),
        (Families.Vader, "vader"),
        (Families.MinorCharacter, "minorCharacter"),
        (Families.SpecialForce, "specialForce"),
        (Families.Sector, "sector"),
        (Families.CoreSystem, "coreSystem"), // Families.System is the same value
        (Families.RimSystem, "rimSystem"),
        (Families.Abode, "abode"),
        (Families.ConstructionManager, "constructionManager"),
        (Families.ShipyardManager, "shipyardManager"),
        (Families.TrainingManager, "trainingManager"),
        (Families.Unmapped, "unmapped"), // meaning unknown
        (Families.Staging, "staging"),
        (Families.Side, "side"),
        (Families.MissionManager, "missionManager"),
        (Families.GameManager, "gameManager"),
        (Families.CharacterManager, "characterManager"),
    ];

    static readonly Dictionary<uint, string> NameOf = Fixed.Concat(Enum.GetValues<MissionKind>().Select(k => ((uint)k, Lower(k.ToString()))))
        .ToDictionary(x => x.Item1, x => x.Item2);
    static readonly Dictionary<string, uint> FamilyOf = NameOf.ToDictionary(x => x.Value, x => x.Key);

    static string Lower(string pascal) => char.ToLowerInvariant(pascal[0]) + pascal[1..];

    /// <summary>The family's name, or its decimal number when it has none.</summary>
    public static string Name(uint family) => NameOf.GetValueOrDefault(family) ?? family.ToString(CultureInfo.InvariantCulture);

    /// <summary>A family by its name only.</summary>
    public static bool TryFamilyName(string text, out uint family) => FamilyOf.TryGetValue(text, out family);

    /// <summary>A family by its name, or by its decimal number (0 to 255).</summary>
    public static bool TryFamily(string text, out uint family) =>
        TryFamilyName(text, out family) || uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out family) && family <= 255;

    /// <summary>The id of an install record: "base:" + <see cref="Name"/> of its family + ":" + its index in decimal.</summary>
    public static string BaseId(uint sdRef)
    {
        var r = new SdRef(sdRef);
        return $"base:{Name(r.Family)}:{r.Index.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Parses a <see cref="BaseId"/> (family by name or decimal number, index in decimal); false when the text is not one.</summary>
    public static bool TryParseBaseId(string text, out uint sdRef)
    {
        sdRef = 0;
        var parts = text.Split(':');
        if (parts.Length != 3 || parts[0] != "base" || !TryFamily(parts[1], out uint family)
            || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint index)) return false;
        sdRef = SdRef.Make(family, index);
        return new SdRef(sdRef).Family == family && new SdRef(sdRef).Index == index; // an index that does not fit the 24 bits fails here
    }
}
