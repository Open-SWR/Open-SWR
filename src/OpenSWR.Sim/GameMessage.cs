namespace OpenSWR.Sim;

/// <summary>
/// Message object: id (increasing), day of creation, category, kind (3 normal, 4 encyclopedia, 5
/// continue-question, 7 strike, 8 no target), message type, title / body templates (the body = parts in order: text,
/// then the per-unit lines), pictures, sound / voice, subject (go-to key, a Ref), encyclopedia key, mission (kind 5), new and
/// read. <see cref="Text"/>: free text for openswr adaptations and older saves (no template).
/// </summary>
public sealed class GameMessage
{
    public int Id { get; set; }
    public int Day { get; set; }
    public MessageCategory Category { get; set; }
    public int Kind { get; set; } = 3;
    public MessageType Type { get; set; }
    public MessageText? Title { get; set; }
    public List<MessageText>? Body { get; set; }
    public string? Text { get; set; }
    public int Picture { get; set; }
    public int Overlay { get; set; }
    public int Sound { get; set; }
    /// <summary>Voice: a WAVE of the player side's sprite DLL (module 9), picked at creation; 0 none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Voice { get; set; }
    /// <summary>Advisor code (0 none): fed to the advisor when the message is added (kinds 3, 4, 5, 8, alert on).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public AdvisorCode AdvisorCode { get; set; }
    public uint Subject { get; set; }
    public uint Encyclopedia { get; set; }
    public uint Mission { get; set; }
    /// <summary>Strike assault flag (type MessageType.Strike): 1 = assault, 0 = bombardment. Saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Assault { get; set; }
    /// <summary>Strike defender side (the system view's owner at the start; Neutral 3; None without the start pulse). Saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public Owner Defender { get; set; }
    /// <summary>Strike attacker side (the fleet view's owner; None names side 2). Not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Owner Attacker { get; set; }
    /// <summary>Strike key lists L70, L78, L80, L88, L90, L98; not saved: null after a load.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<GameObject>[]? Strike { get; set; }
    /// <summary>New: lights the category's button until the tab exit.</summary>
    public bool New { get; set; }
    /// <summary>Displayed (row font 10). Displaying only marks read; New (lit button) is cleared by the tab exit.</summary>
    public bool Read { get; set; }

    /// <summary>Listed in the message window (kinds 3, 4, 5, 7, 8) on the tab of <paramref name="category"/> (None = All: everything but Advice).</summary>
    public bool Listed(MessageCategory category) => Kind is 3 or 4 or 5 or 7 or 8 && (category == MessageCategory.None ? Category != MessageCategory.Advice : Category == category);
}
