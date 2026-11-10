/// <summary>Message kinds (<see cref="OpenSWR.Sim.GameMessage.Kind"/>): how the message window and the main screen treat a message.</summary>
public static class MessageKinds
{
    public const int Normal = 3;
    /// <summary>Go-to opens the encyclopedia entry.</summary>
    public const int Encyclopedia = 4;
    /// <summary>Asks whether the mission continues (Yes / No).</summary>
    public const int ContinueQuestion = 5;
    /// <summary>Full-screen event: plays its event movie.</summary>
    public const int Event = 6;
    /// <summary>Bombardment or assault result for the attacker: opens the strike window.</summary>
    public const int Strike = 7;
    /// <summary>No go-to target.</summary>
    public const int NoTarget = 8;
}
