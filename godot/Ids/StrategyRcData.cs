/// <summary>STRATEGY.DLL RCDATA ids: scroll-bar skins (<see cref="Assets.Skin"/>) and map legend panels (<see cref="FilterPicker.Legend"/>).</summary>
public static class StrategyRcData
{
    // ---- Scroll-bar skins ----
    /// <summary>Default skin of the list control: drop-down boxes, status window.</summary>
    public const ushort DefaultScrollSkin = 10650;
    /// <summary>Lists and text boxes of the 470 x 331 panel windows: finders, encyclopedia, messages, battle reports.</summary>
    public const ushort PanelScrollSkin = 10653;
    public const ushort FleetWindowScrollSkin = 10699;
    public const ushort PlanetWindowScrollSkin = 10700;
    /// <summary>Battle-start list, mission window and system missions window lists.</summary>
    public const ushort MissionListScrollSkin = 10748;

    // ---- Map legend panels (by map display filter) ----
    public const int PopularSupportLegendAlliance = 10183;
    public const int PopularSupportLegendEmpire = 10184;
    /// <summary>First of four: Available Energy, Available Raw Material, Mines, Refineries.</summary>
    public const int ResourcesLegendBase = 10188;
    /// <summary>First of four: Manufacturing, Shipyards, Training Facilities, Construction Yards.</summary>
    public const int ManufacturingLegendBase = 10192;
    /// <summary>First of five: Troopers, Fighter Squadrons, Death Star Shields, Shield Generators, Defense Batteries.</summary>
    public const int DefenseLegendBase = 10196;
    /// <summary>Advisor-only filter mode without a title. Meaning in the original is unknown.</summary>
    public const int AdvisorModeLegend = 10201;
    public const int UprisingsLegend = 11600;
    public const int IdleFleetsLegend = 11601;
    public const int FleetsEnrouteLegend = 11602;
    public const int IdlePersonnelLegend = 11603;
    public const int ActivePersonnelLegend = 11604;
    /// <summary>First of three: Idle Shipyards, Idle Training Facilities, Idle Construction Yards.</summary>
    public const int IdleFacilitiesLegendBase = 11605;
}
