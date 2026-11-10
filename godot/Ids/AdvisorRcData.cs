/// <summary>Advisor sprite DLL (ALSPRITE / EMSPRITE) RCDATA ids: sprite command entries and voice variant lists.</summary>
public static class AdvisorRcData
{
    // ---- Idle commands ----
    /// <summary>Secondary display idle hold, Empire.</summary>
    public const ushort SecondaryIdleEmpire = 401;
    /// <summary>Secondary display idle hold, Alliance.</summary>
    public const ushort SecondaryIdleAlliance = 402;
    /// <summary>Main display idle hold; its end opens the advisor gate. Meaning in the original is unknown.</summary>
    public const ushort MainIdle = 403;
    /// <summary>Queued on the secondary display after a relay command.</summary>
    public const ushort SecondaryEnd = 11575;

    // ---- Voice variant lists ----
    /// <summary>Tried first when Luke goes to Dagobah before his training there is done.</summary>
    public const ushort DagobahFirstVoices = 305;
}
