namespace OpenSWR.Sim;

/// <summary>
/// The tactical UI controller: the current mode, the three event queues drained once per frame (ship cycling, commands,
/// picks), the per-frame refresh (the mode's refresh step runs until it returns no new mode, then the box flags), the
/// right-panel page switch and the cameras. Plain C#: the front end feeds mouse picks, buttons and keys and draws from
/// <see cref="Box"/>, <see cref="NavStyle"/>, <see cref="Page"/> and <see cref="Camera"/>. Voices are recorded as request
/// numbers; the front end plays them through <see cref="TacticalSounds"/>.
/// </summary>
public sealed partial class TacticalUi
{
    /// <summary>The task-force buttons by slot (index 0 = task force 1).</summary>
    public static readonly TacticalCommand[] TaskForceCommands =
    [
        TacticalCommand.TaskForce1, TacticalCommand.TaskForce2, TacticalCommand.TaskForce3, TacticalCommand.TaskForce4,
        TacticalCommand.TaskForce5, TacticalCommand.TaskForce6, TacticalCommand.TaskForce7, TacticalCommand.TaskForce8,
    ];
    /// <summary>The fighter-group buttons by colour (index 0 = Red).</summary>
    public static readonly TacticalCommand[] FighterGroupCommands =
        [TacticalCommand.FighterGroup1, TacticalCommand.FighterGroup2, TacticalCommand.FighterGroup3, TacticalCommand.FighterGroup4];
    /// <summary>The nav-point list buttons (index 0 toggles list 3, index 3 toggles list 0).</summary>
    public static readonly TacticalCommand[] NavListCommands =
        [TacticalCommand.NavList4, TacticalCommand.NavList3, TacticalCommand.NavList2, TacticalCommand.NavList1];

    readonly TacticalBattle _b;
    internal TacticalUi(TacticalBattle battle)
    {
        _b = battle;
        _mode = new IdleMode(this);
        Camera = new TacticalCamera(battle);
    }

    /// <summary>True while in command, false while observing. True at creation.</summary>
    public bool InCommand { get; internal set; } = true;
    /// <summary>The info unit shown on the ship pages.</summary>
    public TacticalUnit? Info { get; internal set; }
    /// <summary>Highlight flags of side 0 and side 1.</summary>
    public bool[] Highlight { get; } = new bool[2];
    /// <summary>Whether nav list k (set k + 1) is visible.</summary>
    public bool[] NavShown { get; } = new bool[4];
    /// <summary>Task-force slots 1-8 (group or none).</summary>
    public TacticalGroup?[] TaskForces { get; } = new TacticalGroup?[8];
    /// <summary>Fighter-group slots Red / Blue / Green / Gold (from setup).</summary>
    public TacticalGroup?[] Fighters { get; } = new TacticalGroup?[4];
    /// <summary>The current mode.</summary>
    public Mode Current => _mode;
    Mode _mode;
    /// <summary>Voice requests in order (request numbers; played by the front end).</summary>
    public List<int> Voices { get; } = [];
    public TacticalCamera Camera { get; }

    readonly List<bool> _cycle = [];
    readonly List<TacticalCommand> _commands = [];
    readonly List<(object? Key, bool Ctrl)> _picks = [];

    /// <summary>Task-force slot of a group (0-7), -1 when unassigned.</summary>
    public int SlotOf(TacticalGroup? g) => g is null ? -1 : Array.IndexOf(TaskForces, g);

    /// <summary>
    /// The unit's UI task force when that group is in no task-force slot (an "unassigned" task force), else none.
    /// Inferred: only the task-force slots count, so a fighter's group (in a fighter slot) is its unassigned task force.
    /// </summary>
    public TacticalGroup? Unassigned(TacticalUnit u) => u.UiGroup is { } g && SlotOf(g) < 0 ? g : null;

    /// <summary>Bind a group to a task-force slot.</summary>
    public void Bind(int slot, TacticalGroup g) => TaskForces[slot] = g;

    /// <summary>A group leaves its HUD slot: its task-force or fighter slot is cleared.</summary>
    /// <remarks>A mode showing that very slot (mode - 1 = task-force slot,
    /// mode - 9 = fighter slot) is replaced by a new idle mode.</remarks>
    internal void Free(TacticalGroup g)
    {
        for (int i = 0; i < 8; i++)
            if (TaskForces[i] == g)
            {
                TaskForces[i] = null;
                if (_mode is TaskForceMode tf && tf.Slot == i) Switch(new IdleMode(this));
            }
        for (int c = 0; c < 4; c++)
            if (Fighters[c] == g)
            {
                Fighters[c] = null;
                if (_mode is FighterMode fm && fm.Colour == c) Switch(new IdleMode(this));
            }
    }

    /// <summary>The first free task-force slot, -1 when none.</summary>
    public int FreeSlot() => Array.IndexOf(TaskForces, null);

    // ---- right panel ----

    /// <summary>The page shown (empty to withdraw confirmation page) and the object it shows (a unit or a group).</summary>
    public TacticalPage Page { get; private set; } = TacticalPage.Empty;
    public object? PageObject { get; private set; }
    TacticalPage _previous = TacticalPage.Empty;
    object? _previousObject;
    /// <summary>
    /// Panel lock: set by the show handlers of the missions, tactics and game options pages,
    /// not the withdraw confirmation page; released by Assign / Cancel on the missions / tactics pages, Close on the options page, the hide slot of the missions / tactics pages (Inferred) and any
    /// switch to the empty page.
    /// </summary>
    public bool Locked { get; private set; }

    /// <summary>The withdraw confirmation page, shown as a child window of the options page at (0, 0); the page and its lock stay the options page's.</summary>
    public bool WithdrawShown { get; private set; }

    static readonly Dictionary<int, int> OfficerIcons = new()
    {
        // major characters
        [OfficerPicture.Mothma] = OfficerIconBitmap.Mothma, [OfficerPicture.Leia] = OfficerIconBitmap.Leia, [OfficerPicture.Luke] = OfficerIconBitmap.Luke,
        [OfficerPicture.Han] = OfficerIconBitmap.Han, [OfficerPicture.UnnamedMajorCharacter] = OfficerIconBitmap.UnnamedMajorCharacter, [OfficerPicture.Palpatine] = OfficerIconBitmap.Palpatine,
        [OfficerPicture.Vader] = OfficerIconBitmap.Vader,
        // minor characters
        [OfficerPicture.Ackbar] = OfficerIconBitmap.Ackbar, [OfficerPicture.WedgeAntilles] = OfficerIconBitmap.WedgeAntilles, [OfficerPicture.LandoCalrissian] = OfficerIconBitmap.LandoCalrissian,
        [OfficerPicture.Chewbacca] = OfficerIconBitmap.Chewbacca, [OfficerPicture.JanDodonna] = OfficerIconBitmap.JanDodonna, [OfficerPicture.CrixMadine] = OfficerIconBitmap.CrixMadine,
        [OfficerPicture.CarlistRieekan] = OfficerIconBitmap.CarlistRieekan, [OfficerPicture.Afyon] = OfficerIconBitmap.Afyon, [OfficerPicture.Drayson] = OfficerIconBitmap.Drayson,
        [OfficerPicture.BorskFeylya] = OfficerIconBitmap.BorskFeylya, [OfficerPicture.TuraRaftican] = OfficerIconBitmap.TuraRaftican, [OfficerPicture.BrenDerlin] = OfficerIconBitmap.BrenDerlin,
        [OfficerPicture.GarmBelIblis] = OfficerIconBitmap.GarmBelIblis, [OfficerPicture.TalonKarrde] = OfficerIconBitmap.TalonKarrde, [OfficerPicture.Narra] = OfficerIconBitmap.Narra,
        [OfficerPicture.HuobaNeva] = OfficerIconBitmap.HuobaNeva, [OfficerPicture.Page] = OfficerIconBitmap.Page, [OfficerPicture.SyubSnunb] = OfficerIconBitmap.SyubSnunb,
        [OfficerPicture.AdarTallon] = OfficerIconBitmap.AdarTallon, [OfficerPicture.SarinVirgilio] = OfficerIconBitmap.SarinVirgilio, [OfficerPicture.VandenWillard] = OfficerIconBitmap.VandenWillard,
        [OfficerPicture.RogetJiriss] = OfficerIconBitmap.RogetJiriss, [OfficerPicture.KaiyaAndrimetrum] = OfficerIconBitmap.KaiyaAndrimetrum, [OfficerPicture.MazerRackus] = OfficerIconBitmap.MazerRackus,
        [OfficerPicture.Orrimaarko] = OfficerIconBitmap.Orrimaarko, [OfficerPicture.Mawshiye] = OfficerIconBitmap.Mawshiye, [OfficerPicture.Jerjerrod] = OfficerIconBitmap.Jerjerrod,
        [OfficerPicture.Ozzel] = OfficerIconBitmap.Ozzel, [OfficerPicture.Piett] = OfficerIconBitmap.Piett, [OfficerPicture.Veers] = OfficerIconBitmap.Veers,
        [OfficerPicture.Brandei] = OfficerIconBitmap.Brandei, [OfficerPicture.Covell] = OfficerIconBitmap.Covell, [OfficerPicture.Dorja] = OfficerIconBitmap.Dorja,
        [OfficerPicture.BinEssada] = OfficerIconBitmap.BinEssada, [OfficerPicture.NilesFerrier] = OfficerIconBitmap.NilesFerrier, [OfficerPicture.Grammel] = OfficerIconBitmap.Grammel,
        [OfficerPicture.Griff] = OfficerIconBitmap.Griff, [OfficerPicture.Klev] = OfficerIconBitmap.Klev, [OfficerPicture.Needa] = OfficerIconBitmap.Needa,
        [OfficerPicture.BaneNothos] = OfficerIconBitmap.BaneNothos, [OfficerPicture.Orlok] = OfficerIconBitmap.Orlok, [OfficerPicture.Pellaeon] = OfficerIconBitmap.Pellaeon,
        [OfficerPicture.Screed] = OfficerIconBitmap.Screed, [OfficerPicture.Thrawn] = OfficerIconBitmap.Thrawn, [OfficerPicture.Zuggs] = OfficerIconBitmap.Zuggs,
        [OfficerPicture.Daala] = OfficerIconBitmap.Daala, [OfficerPicture.PterThanas] = OfficerIconBitmap.PterThanas, [OfficerPicture.BevelLemelisk] = OfficerIconBitmap.BevelLemelisk,
        [OfficerPicture.ShenirRix] = OfficerIconBitmap.ShenirRix, [OfficerPicture.NovalGaraint] = OfficerIconBitmap.NovalGaraint, [OfficerPicture.Garindan] = OfficerIconBitmap.Garindan,
        [OfficerPicture.Menndo] = OfficerIconBitmap.Menndo, [OfficerPicture.Labansat] = OfficerIconBitmap.Labansat, [OfficerPicture.Villar] = OfficerIconBitmap.Villar,
        // special forces
        [OfficerPicture.Guerrillas] = OfficerIconBitmap.Guerrillas, [OfficerPicture.Infiltrators] = OfficerIconBitmap.Infiltrators, [OfficerPicture.LongprobeRecon] = OfficerIconBitmap.LongprobeRecon,
        [OfficerPicture.BothanSpies] = OfficerIconBitmap.BothanSpies, [OfficerPicture.ProbeDroids] = OfficerIconBitmap.ProbeDroids, [OfficerPicture.EspionageDroids] = OfficerIconBitmap.EspionageDroids,
        [OfficerPicture.ImperialCommandos] = OfficerIconBitmap.ImperialCommandos, [OfficerPicture.NoghriCommandos] = OfficerIconBitmap.NoghriCommandos,
        // troops
        [OfficerPicture.AllianceFleetRegiment] = OfficerIconBitmap.AllianceFleetRegiment, [OfficerPicture.AllianceArmyRegiment] = OfficerIconBitmap.AllianceArmyRegiment, [OfficerPicture.SullustanRegiment] = OfficerIconBitmap.SullustanRegiment,
        [OfficerPicture.WookieeRegiment] = OfficerIconBitmap.WookieeRegiment, [OfficerPicture.MonCalamariRegiment] = OfficerIconBitmap.MonCalamariRegiment, [OfficerPicture.StormtrooperRegiment] = OfficerIconBitmap.StormtrooperRegiment,
        [OfficerPicture.ImperialFleetRegiment] = OfficerIconBitmap.ImperialFleetRegiment, [OfficerPicture.ImperialArmyRegiment] = OfficerIconBitmap.ImperialArmyRegiment, [OfficerPicture.WarDroidRegiment] = OfficerIconBitmap.WarDroidRegiment,
        [OfficerPicture.DarkTrooperRegiment] = OfficerIconBitmap.DarkTrooperRegiment,
    };

}
