using Godot;

/// <summary>STRATEGY.DLL WAVE sound effects by name (ids in StrategySounds); play with Assets.PlaySound.</summary>
public static class StrategySfx
{
    public static AudioStream? FilterChange => Assets.Wave(StrategySounds.FilterChange, "STRATEGY.DLL");
    public static AudioStream? AssaultAlarm => Assets.Wave(StrategySounds.AssaultAlarm, "STRATEGY.DLL");
    public static AudioStream? UprisingAlarm => Assets.Wave(StrategySounds.UprisingAlarm, "STRATEGY.DLL");
}
