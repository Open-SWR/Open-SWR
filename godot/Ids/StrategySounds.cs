/// <summary>STRATEGY.DLL WAVE ids (<see cref="Assets.PlayStrategy"/>): strategy-screen UI sounds and alarms.</summary>
public static class StrategySounds
{
    // ---- Frame and map display filters ----
    /// <summary>Galactic Information Display frame button (filter picker), pressed.</summary>
    public const ushort FilterPickerButton = 600;
    /// <summary>Map display filter changed, or the filter picker closed by a click on the map.</summary>
    public const ushort FilterChange = 601;

    // ---- Windows ----
    public const ushort SectorWindowOpen = 604;
    public const ushort SectorWindowClose = 605;
    /// <summary>System sub-window (fleet, planet, manufacturing, missions) opened.</summary>
    public const ushort SubWindowOpen = 606;
    public const ushort SubWindowClose = 607;
    /// <summary>Click of the strategy buttons that have one, played on press.</summary>
    public const ushort Click = 608;
    public const ushort MinimiseEmpire = 610;
    public const ushort MinimiseAlliance = 611;
    public const ushort RestoreEmpire = 612;
    public const ushort RestoreAlliance = 613;

    // ---- Alarms ----
    /// <summary>Planetary assault by or against the player (openswr; the original plays it on a never-controlled system view). Meaning in the original is unknown.</summary>
    public const ushort AssaultAlarm = 620;
    /// <summary>A system the player sees enters Uprising.</summary>
    public const ushort UprisingAlarm = 621;
}
