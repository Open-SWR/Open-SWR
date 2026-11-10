/// <summary>
/// Tactical hit-effect flags per unit: a running effect is replaced only by a greater flag, never a spark by a spark or a
/// hull explosion by a hull explosion; Death always replaces.
/// </summary>
public static class TacticalEffectFlags
{
    // ---- Sparks ----
    public const int IonSpark = 4;
    public const int Spark = 8;

    // ---- Hull explosions ----
    public const int IonOverflow = 16;
    public const int HullExplosion = 32;
    public const int TorpedoHit = 64;

    // ---- Death ----
    public const int Death = 128;
}
