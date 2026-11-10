namespace OpenSWR.Sim;

/// <summary>
/// Officer picture numbers: the StringNumber (low 12 bits of the StringId) of the character, troop or special force
/// record, as in <see cref="SdCommon.StringNumber"/>. Keys of the officer icon table.
/// </summary>
public static class OfficerPicture
{
    // major characters (UnnamedMajorCharacter has no string in the install; meaning in the original is unknown)
    public const int Mothma = 2112,
        Leia = 2113, Luke = 2114, Han = 2115, UnnamedMajorCharacter = 2128, Palpatine = 2176, Vader = 2177;
    // minor characters
    public const int Ackbar = 2624,
        WedgeAntilles = 2625, LandoCalrissian = 2626, Chewbacca = 2627, JanDodonna = 2628, CrixMadine = 2629, CarlistRieekan = 2630,
        Afyon = 2631, Drayson = 2632, BorskFeylya = 2633, TuraRaftican = 2634, BrenDerlin = 2635, GarmBelIblis = 2636,
        TalonKarrde = 2637, Narra = 2638, HuobaNeva = 2639, Page = 2640, SyubSnunb = 2641, AdarTallon = 2642,
        SarinVirgilio = 2643, VandenWillard = 2644, RogetJiriss = 2645, KaiyaAndrimetrum = 2646, MazerRackus = 2647, Orrimaarko = 2648,
        Mawshiye = 2649, Jerjerrod = 2688, Ozzel = 2689, Piett = 2690, Veers = 2691, Brandei = 2692,
        Covell = 2693, Dorja = 2694, BinEssada = 2695, NilesFerrier = 2696, Grammel = 2697, Griff = 2698,
        Klev = 2699, Needa = 2700, BaneNothos = 2701, Orlok = 2702, Pellaeon = 2703, Screed = 2704,
        Thrawn = 2705, Zuggs = 2706, Daala = 2707, PterThanas = 2708, BevelLemelisk = 2709, ShenirRix = 2710,
        NovalGaraint = 2711, Garindan = 2712, Menndo = 2713, Labansat = 2714, Villar = 2715;
    // special forces
    public const int Guerrillas = 1344,
        Infiltrators = 1345, LongprobeRecon = 1346, BothanSpies = 1347, ProbeDroids = 1408, EspionageDroids = 1409, ImperialCommandos = 1410,
        NoghriCommandos = 1411;
    // troops
    public const int AllianceFleetRegiment = 1088,
        AllianceArmyRegiment = 1089, SullustanRegiment = 1090, WookieeRegiment = 1091, MonCalamariRegiment = 1092, StormtrooperRegiment = 1152, ImperialFleetRegiment = 1153,
        ImperialArmyRegiment = 1154, WarDroidRegiment = 1155, DarkTrooperRegiment = 1156;
}
