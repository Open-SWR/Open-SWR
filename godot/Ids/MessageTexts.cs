/// <summary>TEXTSTRA.DLL message templates by name (ids in MessageTemplates); the arguments fill the placeholders in order.</summary>
public static class MessageTexts
{
    public static string BattleResultTitle(params object[] args) => Assets.Message(MessageTemplates.BattleResultTitle, args);
    public static string FleetVictorious(params object[] args) => Assets.Message(MessageTemplates.FleetVictorious, args);
    public static string FleetDefeated(params object[] args) => Assets.Message(MessageTemplates.FleetDefeated, args);
    public static string BattleIndecisive(params object[] args) => Assets.Message(MessageTemplates.BattleIndecisive, args);
    public static string FleetDestroyed(params object[] args) => Assets.Message(MessageTemplates.FleetDestroyed, args);
    public static string FleetWithdrawn(params object[] args) => Assets.Message(MessageTemplates.FleetWithdrawn, args);
    public static string FleetWithdrawnTo(params object[] args) => Assets.Message(MessageTemplates.FleetWithdrawnTo, args);
    public static string NoVictor(params object[] args) => Assets.Message(MessageTemplates.NoVictor, args);
    public static string AllShipsDestroyed(params object[] args) => Assets.Message(MessageTemplates.AllShipsDestroyed, args);
    public static string DefeatedFleetDestroyed(params object[] args) => Assets.Message(MessageTemplates.DefeatedFleetDestroyed, args);
    public static string DefeatedFleetWithdrawnTo(params object[] args) => Assets.Message(MessageTemplates.DefeatedFleetWithdrawnTo, args);
    public static string VictoriousBlockade(params object[] args) => Assets.Message(MessageTemplates.VictoriousBlockade, args);
    public static string GoToButtons(params object[] args) => Assets.Message(MessageTemplates.GoToButtons, args);
    public static string BattleStartTitle(params object[] args) => Assets.Message(MessageTemplates.BattleStartTitle, args);
    public static string FleetThreatening(params object[] args) => Assets.Message(MessageTemplates.FleetThreatening, args);
    public static string BreakBlockadeOf(params object[] args) => Assets.Message(MessageTemplates.BreakBlockadeOf, args);
    public static string FleetEnteredSystem(params object[] args) => Assets.Message(MessageTemplates.FleetEnteredSystem, args);
    public static string BreakBlockadeAt(params object[] args) => Assets.Message(MessageTemplates.BreakBlockadeAt, args);
    public static string ForcesAboutToEngage(params object[] args) => Assets.Message(MessageTemplates.ForcesAboutToEngage, args);
    public static string ConfirmAbortMission(params object[] args) => Assets.Message(MessageTemplates.ConfirmAbortMission, args);
    public static string ConfirmStopConstruction(params object[] args) => Assets.Message(MessageTemplates.ConfirmStopConstruction, args);
    public static string ConfirmTargetLine(params object[] args) => Assets.Message(MessageTemplates.ConfirmTargetLine, args);
    public static string ConfirmManagerLine(params object[] args) => Assets.Message(MessageTemplates.ConfirmManagerLine, args);
}
