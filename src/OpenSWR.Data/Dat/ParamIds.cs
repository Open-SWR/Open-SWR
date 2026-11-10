using System.Globalization;

namespace OpenSWR.Data.Dat;

/// <summary>Parameter ids as mods write them: the enum name (e.g. HyperdriveModifier) or a decimal id (see Parameters.cs).</summary>
public static class ParamIds
{
    /// <summary>The id of a parameter by its enum name, or a decimal id.</summary>
    public static bool TryParse<T>(string text, out uint id) where T : struct, Enum
    {
        if (Enum.GetNames<T>().Contains(text)) { id = Convert.ToUInt32(Enum.Parse<T>(text)); return true; }
        return uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    /// <summary>The enum name of an id, or its decimal id when the enum has no name for it.</summary>
    public static string Name<T>(uint id) where T : struct, Enum =>
        Enum.GetName(typeof(T), Enum.ToObject(typeof(T), id)) ?? id.ToString(CultureInfo.InvariantCulture);
}
