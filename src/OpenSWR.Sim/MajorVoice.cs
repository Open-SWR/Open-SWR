namespace OpenSWR.Sim;

/// <summary>
/// Voice request numbers of a major character's line in the message handlers (the sprite DLL voice ids). Each name is the
/// character, the situation, and for Luke whether he has finished his Dagobah training (Trained).
/// </summary>
public static class MajorVoice
{
    public const int MothmaArrive = 240, LeiaArrive = 220, LukeArrive = 200, TrainedLukeArrive = 210, HanArrive = 230,
        PalpatineArrive = 260, VaderArrive = 250;
    public const int MothmaEscaped = 246, LeiaEscaped = 226, LukeEscaped = 206, TrainedLukeEscaped = 216, HanEscaped = 236,
        PalpatineEscaped = 266, VaderEscaped = 256;
    public const int MothmaFailed = 243, LeiaFailed = 223, LukeFailed = 203, TrainedLukeFailed = 213, HanFailed = 233,
        PalpatineFailed = 263, VaderFailed = 253;
    public const int MothmaSuccess = 242, LeiaSuccess = 222, LukeSuccess = 202, TrainedLukeSuccess = 212, HanSuccess = 232,
        PalpatineSuccess = 262, VaderSuccess = 252;
    public const int MothmaHealed = 247, LeiaHealed = 227, LukeHealed = 207, TrainedLukeHealed = 217, HanHealed = 237,
        PalpatineHealed = 267, VaderHealed = 257;
}
