namespace OpenSWR.Sim;

/// <summary>
/// Strategy-screen window kinds (the kind part of a window id; the index is the object or dialog the window is for). The sector
/// window and the system sub-windows are kinds the sim's screen views also use; the four finders are the finder windows.
/// </summary>
public enum WindowKind
{
    None,
    Sector,
    SystemFleet,
    SystemManufacturing,
    SystemPlanet,
    SystemMissions,
    Mission,
    Messages,
    Confirm,
    SystemFinder,
    FleetFinder,
    TroopFinder,
    PersonnelFinder,
    Encyclopedia,
    Status,
    Production,
}
