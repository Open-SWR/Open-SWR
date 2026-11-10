/// <summary>PE resource type ids as numbers (OpenSWR.Data.ResId has Bitmap, String, RcData and Wave as ResId values).</summary>
public static class ResourceTypes
{
    // ---- Standard types ----
    public const ushort Bitmap = 2;
    public const ushort Accelerator = 9;

    // ---- Game-specific types ----
    /// <summary>TACTICAL.DLL DirectX .x model.</summary>
    public const ushort TacticalModel = 301;
    /// <summary>Advisor sprite DLL delta frame.</summary>
    public const ushort SpriteDelta = 302;
    /// <summary>TACTICAL.DLL RLE image or 256-colour palette.</summary>
    public const ushort TacticalImage = 303;
}
