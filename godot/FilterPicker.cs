using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Sim;

/// <summary>
/// Map display filter picker "Galactic Information Display": a
/// 160 x 160 list at (370, 233) A / (420, 233) E, rows at x 30 (icon, label right of it), height 20; a click on a group row opens
/// its submenu (240 wide, rows from y 7; observed in the original: clicks only, no hover timer); a click on a row without a submenu sends
/// its mode (map filter command). Titles and legend
/// entries per mode. The zoom animation is left out.
/// </summary>
public static class FilterPicker
{
    record Row(int Mode, int Label, ushort A, ushort E, Row[]? Sub = null);

    static readonly Row[] Rows =
    [
        new(ControlIds.FilterLoyaltyGroup, StrategyTexts.FilterLoyalty, StrategyBitmaps.LoyaltyFilterAlliance, StrategyBitmaps.LoyaltyFilterEmpire, [new(FilterModes.PopularSupportVariant, StrategyTexts.FilterPopularSupport, StrategyBitmaps.LoyaltyFilterAlliance, StrategyBitmaps.LoyaltyFilterEmpire), new(FilterModes.Uprisings, StrategyTexts.FilterUprisings, StrategyBitmaps.UprisingsFilterAlliance, StrategyBitmaps.UprisingsFilterEmpire)]),
        new(ControlIds.FilterFleetsGroup, StrategyTexts.FilterFleets, StrategyBitmaps.FleetsFilterAlliance, StrategyBitmaps.FleetsFilterEmpire, [new(FilterModes.IdleFleetsVariant, StrategyTexts.FilterIdleFleets, StrategyBitmaps.FleetsFilterAlliance, StrategyBitmaps.FleetsFilterEmpire), new(FilterModes.FleetsEnroute, StrategyTexts.FilterFleetsEnroute, StrategyBitmaps.FleetsEnrouteFilterAlliance, StrategyBitmaps.FleetsEnrouteFilterEmpire)]),
        new(ControlIds.FilterPersonnelGroup, StrategyTexts.FilterPersonnel, StrategyBitmaps.PersonnelFilterAlliance, StrategyBitmaps.PersonnelFilterEmpire, [new(FilterModes.IdlePersonnel, StrategyTexts.FilterIdlePersonnel, StrategyBitmaps.IdlePersonnelFilterAlliance, StrategyBitmaps.PersonnelFilterEmpire), new(FilterModes.ActivePersonnel, StrategyTexts.FilterActivePersonnel, StrategyBitmaps.TroopersFilterAlliance, StrategyBitmaps.TroopersFilterEmpire)]),
        new(ControlIds.FilterResourcesGroup, StrategyTexts.FilterResources, StrategyBitmaps.ResourcesFilterAlliance, StrategyBitmaps.ResourcesFilterEmpire, [new(FilterModes.AvailableEnergy, StrategyTexts.FilterAvailableEnergy, StrategyBitmaps.AvailableEnergyFilter, StrategyBitmaps.AvailableEnergyFilter), new(FilterModes.AvailableRawMaterial, StrategyTexts.FilterAvailableRawMaterial, StrategyBitmaps.AvailableRawMaterialFilter, StrategyBitmaps.AvailableRawMaterialFilter), new(FilterModes.Mines, StrategyTexts.FilterMines, StrategyBitmaps.MinesFilter, StrategyBitmaps.MinesFilter), new(FilterModes.Refineries, StrategyTexts.FilterRefineries, StrategyBitmaps.RefineriesFilter, StrategyBitmaps.RefineriesFilter)]),
        new(ControlIds.FilterManufacturingGroup, StrategyTexts.FilterManufacturing, StrategyBitmaps.ManufacturingFilterAlliance, StrategyBitmaps.ManufacturingFilterEmpire, [new(FilterModes.Shipyards, StrategyTexts.FilterShipyards, StrategyBitmaps.ShipyardsFilter, StrategyBitmaps.ShipyardsFilter), new(FilterModes.IdleShipyards, StrategyTexts.FilterIdleShipyards, StrategyBitmaps.ShipyardsFilter, StrategyBitmaps.ShipyardsFilter), new(FilterModes.TrainingFacilities, StrategyTexts.FilterTrainingFacilities, StrategyBitmaps.TrainingFacilitiesFilter, StrategyBitmaps.TrainingFacilitiesFilter),
            new(FilterModes.IdleTrainingFacilities, StrategyTexts.FilterIdleTrainingFacilities, StrategyBitmaps.TrainingFacilitiesFilter, StrategyBitmaps.TrainingFacilitiesFilter), new(FilterModes.ConstructionYards, StrategyTexts.FilterConstructionYards, StrategyBitmaps.ConstructionYardsFilter, StrategyBitmaps.ConstructionYardsFilter), new(FilterModes.IdleConstructionYards, StrategyTexts.FilterIdleConstructionYards, StrategyBitmaps.ConstructionYardsFilter, StrategyBitmaps.ConstructionYardsFilter)]),
        new(ControlIds.FilterDefenseGroup, StrategyTexts.FilterDefense, StrategyBitmaps.DefenseFilterAlliance, StrategyBitmaps.DefenseFilterEmpire, [new(FilterModes.DefenseBatteries, StrategyTexts.FilterDefenseBatteries, StrategyBitmaps.DefenseBatteriesFilter, StrategyBitmaps.DefenseBatteriesFilter), new(FilterModes.PlanetaryShields, StrategyTexts.FilterShieldGenerators, StrategyBitmaps.ShieldGeneratorsFilterAlliance, StrategyBitmaps.DefenseFilterEmpire), new(FilterModes.FighterSquadrons, StrategyTexts.FilterFighterSquadrons, StrategyBitmaps.FighterSquadronsFilterAlliance, StrategyBitmaps.FighterSquadronsFilterEmpire),
            new(FilterModes.Troopers, StrategyTexts.FilterTroopers, StrategyBitmaps.TroopersFilterAlliance, StrategyBitmaps.TroopersFilterEmpire), new(FilterModes.DeathStarShields, StrategyTexts.FilterDeathStarShields, StrategyBitmaps.DeathStarShieldsFilter, StrategyBitmaps.DeathStarShieldsFilter)]),
        new(FilterModes.Off, StrategyTexts.FilterDisplayOff, 0, 0),
    ];

    /// <summary>Submenu heights by row count (2: 55, 4: 100, 5: 122, 6: 142).</summary>
    static readonly Dictionary<int, int> SubHeights = new() { [2] = 55, [4] = 100, [5] = 122, [6] = 142 };

    /// <summary>Title string per mode (picker modes only).</summary>
    public static readonly Dictionary<int, int> Titles = new()
    {
        [FilterModes.PopularSupport] = StrategyTexts.FilterPopularSupport, [FilterModes.PopularSupportVariant] = StrategyTexts.FilterPopularSupport, [FilterModes.Uprisings] = StrategyTexts.FilterUprisings, [FilterModes.IdleFleets] = StrategyTexts.FilterIdleFleets, [FilterModes.IdleFleetsVariant] = StrategyTexts.FilterIdleFleets, [FilterModes.FleetsEnroute] = StrategyTexts.FilterFleetsEnroute, [FilterModes.IdlePersonnel] = StrategyTexts.FilterIdlePersonnel, [FilterModes.ActivePersonnel] = StrategyTexts.FilterActivePersonnel,
        [FilterModes.AvailableEnergy] = StrategyTexts.FilterAvailableEnergy, [FilterModes.AvailableRawMaterial] = StrategyTexts.FilterAvailableRawMaterial, [FilterModes.Mines] = StrategyTexts.FilterMines, [FilterModes.Refineries] = StrategyTexts.FilterRefineries, [FilterModes.Manufacturing] = StrategyTexts.FilterManufacturingTitle, [FilterModes.Shipyards] = StrategyTexts.FilterShipyards, [FilterModes.TrainingFacilities] = StrategyTexts.FilterTrainingFacilities, [FilterModes.ConstructionYards] = StrategyTexts.FilterConstructionYards,
        [FilterModes.IdleShipyards] = StrategyTexts.FilterIdleShipyards, [FilterModes.IdleTrainingFacilities] = StrategyTexts.FilterIdleTrainingFacilities, [FilterModes.IdleConstructionYards] = StrategyTexts.FilterIdleConstructionYards, [FilterModes.Troopers] = StrategyTexts.FilterTroopers, [FilterModes.FighterSquadrons] = StrategyTexts.FilterFighterSquadrons, [FilterModes.DeathStarShields] = StrategyTexts.FilterDeathStarShields, [FilterModes.PlanetaryShields] = StrategyTexts.FilterShieldGenerators, [FilterModes.DefenseBatteries] = StrategyTexts.FilterDefenseBatteries,
    };

    /// <summary>Legend entry per mode; 0 = none (the window stays empty, Inferred).</summary>
    public static int Legend(int mode, bool sideOne) => mode switch
    {
        FilterModes.PopularSupportVariant => sideOne ? StrategyRcData.PopularSupportLegendAlliance : StrategyRcData.PopularSupportLegendEmpire,
        FilterModes.Uprisings => StrategyRcData.UprisingsLegend,
        FilterModes.IdleFleetsVariant => StrategyRcData.IdleFleetsLegend,
        FilterModes.FleetsEnroute => StrategyRcData.FleetsEnrouteLegend,
        FilterModes.AdvisorUntitled => StrategyRcData.AdvisorModeLegend,
        FilterModes.IdlePersonnel => StrategyRcData.IdlePersonnelLegend,
        FilterModes.ActivePersonnel => StrategyRcData.ActivePersonnelLegend,
        >= FilterModes.AvailableEnergy and <= FilterModes.Refineries => StrategyRcData.ResourcesLegendBase + mode - FilterModes.AvailableEnergy,
        >= FilterModes.Manufacturing and <= FilterModes.ConstructionYards => StrategyRcData.ManufacturingLegendBase + mode - FilterModes.Manufacturing,
        >= FilterModes.IdleShipyards and <= FilterModes.IdleConstructionYards => StrategyRcData.IdleFacilitiesLegendBase + mode - FilterModes.IdleShipyards,
        >= FilterModes.Troopers and <= FilterModes.DefenseBatteries => StrategyRcData.DefenseLegendBase + mode - FilterModes.Troopers,
        _ => 0,
    };

    /// <summary>Alt+1 ... Alt+9.</summary>
    public static readonly int[] AltModes = [FilterModes.PopularSupportVariant, FilterModes.Uprisings, FilterModes.IdleFleetsVariant, FilterModes.FleetsEnroute, FilterModes.IdlePersonnel, FilterModes.ActivePersonnel, FilterModes.IdleShipyards, FilterModes.IdleTrainingFacilities, FilterModes.IdleConstructionYards];

    static GameMenu.Entry Entry(Row r, bool sideOne) =>
        new(r.Mode, Assets.Stra(r.Label), sideOne ? r.A : r.E, 0, 0, 0, true, false, [.. (r.Sub ?? []).Select(s => Entry(s, sideOne))]);

    static Vector2 Img(ushort id) => (Vector2)Assets.BitmapSize(id, "STRATEGY.DLL");

    /// <summary>Fixed layout: picker 160 x 160, rows at y 10 + 22*i; submenus 240 wide, rows at y 7 + 22*i; label at 30 + icon width (Display Off: + the width of 10115).</summary>
    static GameMenu.Level Layout(List<GameMenu.Entry> rows)
    {
        bool main = rows.Any(r => r.Id == FilterModes.Off);
        float width = main ? 160 : 240, top = main ? 10 : 7;
        var geometry = rows.Select((r, i) => (new Rect2(0, top + 22 * i, width, 20), 30f, 30 + Img(r.Image != 0 ? r.Image : (ushort)StrategyBitmaps.DefenseFilterAlliance).X)).ToList();
        return new GameMenu.Level(rows, new Vector2(width, main ? 160 : SubHeights.GetValueOrDefault(rows.Count, (int)(top + 22 * rows.Count))), geometry);
    }

    /// <summary>The picker as a popup list; the arrow bitmaps 10117 / 10118 (A) and 10128 / 10129 (E) mark the group rows (Inferred: normal / highlighted).</summary>
    public static GameMenu Create(bool sideOne, Action<int> command) =>
        new([.. Rows.Select(r => Entry(r, sideOne))], sideOne ? new Vector2(370, 233) : new Vector2(420, 233), sideOne, command, Layout,
            sideOne ? ((ushort)StrategyBitmaps.SubmenuArrowAlliance, (ushort)StrategyBitmaps.SubmenuArrowHighlightedAlliance) : ((ushort)StrategyBitmaps.SubmenuArrowEmpire, (ushort)StrategyBitmaps.SubmenuArrowHighlightedEmpire), clickOpens: true);
}
