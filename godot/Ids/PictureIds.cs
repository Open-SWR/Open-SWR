/// <summary>Offsets added to a record's picture number (its string id's place in its block) to get resource ids.</summary>
public static class PictureIds
{
    // ---- Encyclopedia (ENCYTEXT.DLL RCDATA text, ENCYBMAP.DLL picture file names) ----
    /// <summary>Entry id = picture number + this.</summary>
    public const int EncyclopediaEntryBase = 4096;
    /// <summary>Added to a mission or fleet entry id for the Empire picture.</summary>
    public const int EncyclopediaEmpireOffset = 8192;

    // ---- GOKRES.DLL picture forms ----
    /// <summary>Form offset of the Empire (side 2) picture.</summary>
    public const int GokresEmpireForm = 4096;
}
