namespace OpenSWR.Tests;

/// <summary>Fact that runs only when the original game install is available (OPENSWR_GAME_DIR).</summary>
public sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute(params string[] extraEnvVars) => Skip = GameDir.MissingInput(extraEnvVars);
}

public sealed class GameTheoryAttribute : TheoryAttribute
{
    public GameTheoryAttribute(params string[] extraEnvVars) => Skip = GameDir.MissingInput(extraEnvVars);
}

public static class GameDir
{
    public const string EnvVar = "OPENSWR_GAME_DIR";
    public static string Path => Environment.GetEnvironmentVariable(EnvVar)!;
    public static string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>
    /// The skip reason when a required env var is missing, else null. With OPENSWR_REQUIRE_GAME=1 nothing is skipped: a test without
    /// its input fails, so a lost env var cannot give a green run that covers almost nothing.
    /// </summary>
    internal static string? MissingInput(string[] extraEnvVars)
    {
        if (Environment.GetEnvironmentVariable("OPENSWR_REQUIRE_GAME") == "1") return null;
        foreach (var name in extraEnvVars.Prepend(EnvVar))
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                return $"{name} not set";
        return null;
    }
}
