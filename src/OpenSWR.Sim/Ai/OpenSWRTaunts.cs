using System.Text.Json;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// The OpenSWR AI's chat lines to a human opponent, read from a JSON file the front end supplies (godot/Data/openswr-taunts.json):
/// an object with one array of strings per occasion, keys "battleWon", "battleLost", "systemTaken", "systemLost", "attackLaunched"
/// (posted when the attacking fleet arrives); "{system}" in a line = the system's name. A missing key or an empty array keeps that occasion silent; unknown keys and
/// non-string entries are ignored.
/// </summary>
public sealed class OpenSWRTaunts
{
    public enum Occasion { BattleWon, BattleLost, SystemTaken, SystemLost, AttackLaunched }

    public static readonly string[] Keys = ["battleWon", "battleLost", "systemTaken", "systemLost", "attackLaunched"];

    readonly string[][] _lines;

    OpenSWRTaunts(string[][] lines) => _lines = lines;

    /// <summary>The lines of an occasion (empty: silent).</summary>
    public IReadOnlyList<string> Lines(Occasion occasion) => _lines[(int)occasion];

    /// <summary>Parses the file's text; malformed JSON or a root that is not an object gives <see cref="InvalidDataException"/>.</summary>
    public static OpenSWRTaunts Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Taunt file is not valid JSON: " + e.Message, e);
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Taunt file must hold a JSON object.");
            var lines = Keys.Select(key => doc.RootElement.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => s.Length > 0).ToArray()
                : []).ToArray();
            return new OpenSWRTaunts(lines);
        }
    }
}
