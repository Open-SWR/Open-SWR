using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

public sealed partial class Advisor
{
    // ---- build helper ----

    /// <summary>Helper kind (<see cref="BuildHelperKind"/>): none, facilities (BuildFacilities), ships (BuildShips), troops (TrainTroops).</summary>
    public BuildHelperKind HelperKind { get; private set; }
    /// <summary>BuildShips / TrainTroops / BuildFacilities (only while the helper is idle): the kind, target reset; the screen then enters the targeting state without a command.</summary>
    public void HelperStart(int id)
    {
        HelperKind = id switch
        {
            AdvisorMenu.BuildShips => BuildHelperKind.Ships, AdvisorMenu.TrainTroops => BuildHelperKind.Troops,
            AdvisorMenu.BuildFacilities => BuildHelperKind.Facilities, _ => BuildHelperKind.None,
        };
    }

    void HelperReset() => (HelperKind, Records[Helper]) = (BuildHelperKind.None, Disabled);

    /// <summary>
    /// The target pick and the helper machine in one call (openswr: no delegation cycle yet; the
    /// original runs the machine in the next think, Inferred equivalent). Key none gives reset; not a system / fleet / capital ship gives
    /// refusal <see cref="Refusal.WrongDestinationKind"/>; troops to a system whose view the player does not own and that holds no Usable fleet of the player
    /// then a SetProductionTarget (ArgRef = the system) refused as <see cref="Refusal.DestinationOtherOwner"/>; else the producer search
    /// (<see cref="Producer"/>), nothing found gives <see cref="Refusal.AllSuspended"/>. Returns the SetProductionTarget Destination (manager to target; the caller
    /// validates and sends it, a refusal goes to <see cref="Refused"/>) and the Build command ProduceFacility / ProduceShip / ProduceTroop for the
    /// production window (not submitted). The helper resets either way.
    /// </summary>
    public (Command Destination, Command Build)? HelperPick(GameState game, GameObject? target)
    {
        var kind = HelperKind;
        HelperReset();
        if (target is null || kind == BuildHelperKind.None) return null;
        if (Family.Of(target.Type) is not ((>= Families.CoreSystem and < Families.Abode) or (>= Families.Fleet and < Families.Troop) or (>= Families.CapitalShip and < Families.Fighter)))
        {
            Refused(Refusal.WrongDestinationKind, null, game);
            return null;
        }
        if (kind == BuildHelperKind.Troops && target is StarSystem s && Knowledge.See(game, s, Side).Owner != Side && !s.Contents.OfType<Fleet>().Any(f => f.Owner == Side && f.Usable))
        {
            Refused(Refusal.DestinationOtherOwner, new Command(CommandType.SetProductionTarget, Side) { ArgRef = target }, game);
            return null;
        }
        if (Producer(game, kind, target) is not { } manager)
        {
            Refused(Refusal.AllSuspended, null, game);
            return null;
        }
        var destination = new Command(CommandType.SetProductionTarget, Side) { Targets = [(manager, GroupMode.Object)], ArgRef = target };
        var build = new Command(kind switch
        {
            BuildHelperKind.Facilities => CommandType.ProduceFacility, BuildHelperKind.Ships => CommandType.ProduceShip, _ => CommandType.ProduceTroop,
        }, Side) { Targets = [(manager, GroupMode.Object)], Fields = [0, 0] };
        return (destination, build);
    }

    /// <summary>
    /// Producer search: (1) sectors in key order holding an own system and a system with the kind's idle flag,
    /// by squared distance (SD X / Y) to the sector of the target's location (player's view), nearest first, rand(10) per tie
    /// (<see cref="Rand"/>), picked alone; (2) that sector's systems in slot order with the idle flag and own, by squared distance
    /// to the target's system, the same way; (3) the picked system's manager of the kind (facilities construction, ships
    /// shipyard, troops training). Idle flag: the view Populated and Explored, without
    /// Uprising and Blockade, >= 1 Existing facility of the kind of which one is Usable, the manager with an empty queue (a queue makes
    /// it busy whatever its progress). Inferred: the flags
    /// are computed from the player's views at pick time (the original reads its advisor model as last updated); facilities and
    /// queues are read only where the player sees the system live (own or observed); an unknown target location counts distance 0.
    /// </summary>
    public ManufacturingManager? Producer(GameState game, BuildHelperKind kind, GameObject target)
    {
        var mk = kind switch
        {
            BuildHelperKind.Facilities => ManufacturingKind.Construction, BuildHelperKind.Ships => ManufacturingKind.Ship, _ => ManufacturingKind.Training,
        };
        uint family = kind switch
        {
            BuildHelperKind.Facilities => Families.ConstructionYard, BuildHelperKind.Ships => Families.Shipyard, _ => Families.TrainingFacility,
        };
        var at = target as StarSystem ?? Knowledge.Location(game, target, Side) as StarSystem;
        bool Own(StarSystem x) => Knowledge.See(game, x, Side).Owner == Side;
        bool Idle(StarSystem x)
        {
            var v = Knowledge.See(game, x, Side);
            if (!v.Populated || !v.Explored || v.Uprising || v.Blockade || !(x.Owner == Side || Knowledge.IsLive(x, Side))) return false;
            var yards = x.Contents.Where(o => Family.Of(o.Type) == family && o.Existing).ToList(); // mode 3, then >= 1 Usable
            return yards.Count > 0 && yards.Any(o => o.Usable) && x.Managers[(int)mk] is { Destroyed: false, Orders.Count: 0 };
        }
        static int Squared(int dx, int dy) => dx * dx + dy * dy;
        var systems = game.Systems.ToLookup(x => x.Sector);
        var sectors = new Ai.ListStack();
        sectors.Push(new Ai.RankedList(game.Sectors.OrderBy(x => x.Ref).Where(x => systems[x].Any(Own) && systems[x].Any(Idle)).Select(x => ((object)x,
            at is null ? 0 : Squared(x.Definition.X - at.Sector.Definition.X, x.Definition.Y - at.Sector.Definition.Y))), RankOrder.Descending, Rand));
        if (sectors.Pick() is not Sector sector) return null;
        var list = new Ai.ListStack();
        list.Push(new Ai.RankedList(systems[sector].Where(x => Idle(x) && Own(x)).Select(x => ((object)x,
            at is null ? 0 : Squared(x.Definition.X - at.Definition.X, x.Definition.Y - at.Definition.Y))), RankOrder.Descending, Rand));
        return list.Pick() is StarSystem picked ? picked.Managers[(int)mk] : null;
    }

    /// <summary>The production window opened with the helper's Build command: watched.</summary>
    public void Watch(Command build)
    {
        Watching = true;
        Watched = build;
    }

    /// <summary>Right click on an advisor display opened its menu: counter +1, <see cref="MenuOpened"/>.</summary>
    public void MenuOpen()
    {
        MenuClicks++;
        MenuOpened = true;
    }

    /// <summary>The message window opened / closed.</summary>
    public void MessageWindow(bool open) => MessageWindowOpen = open;

    /// <summary>Esc / mouse button while the briefing hooks are installed.</summary>
    public void Skip() => SkipBriefing = true;

    /// <summary>
    /// Window-menu toggles: ManageGarrisons / ManageProduction flip record Garrisons / Production between disabled and enabled (toggle
    /// counter +/- 1; enabling with the counter above 3 sets ToggledOften, else ManageGarrisonsOn / ManageProductionOn; disabling sets
    /// ManageGarrisonsOff / ManageProductionOff); the Translate row toggles Translate, the Tips row TipsOff. An enabled record is run by the delegation cycle; the toggle
    /// itself leaves its machine alone (the command slot resets it only when the record is the cycle's current one).
    /// </summary>
    public void Toggle(int id)
    {
        switch (id)
        {
            case AdvisorMenu.ManageGarrisons or AdvisorMenu.ManageProduction:
                int record = id == AdvisorMenu.ManageGarrisons ? Garrisons : Production;
                if (Records[record] == Disabled)
                {
                    Records[record] = RecordEnabled;
                    if (++ToggleCounter > ToggleLimit) ToggledOften = true;
                    else if (id == AdvisorMenu.ManageGarrisons) ManageGarrisonsOn = true;
                    else ManageProductionOn = true;
                }
                else
                {
                    Records[record] = Disabled;
                    ToggleCounter--;
                    if (id == AdvisorMenu.ManageGarrisons) ManageGarrisonsOff = true;
                    else ManageProductionOff = true;
                }
                break;
            case AdvisorMenu.Translate: Translate = !Translate; break;
            case AdvisorMenu.Tips: TipsOff = !TipsOff; break;
        }
    }

    /// <summary>Window-menu row state: (enabled, checked). "Advisor active" is always true in this build; a further menu row has no entry.</summary>
    public (bool Enabled, bool Checked) Row(int id) => id switch
    {
        AdvisorMenu.BuildShips or AdvisorMenu.TrainTroops or AdvisorMenu.BuildFacilities => (Records[Helper] == Disabled, false),
        AdvisorMenu.GalaxyOverview or AdvisorMenu.Objectives => (true, false),
        AdvisorMenu.ManageGarrisons => (true, Records[Garrisons] != Disabled),
        AdvisorMenu.ManageProduction => (true, Records[Production] != Disabled),
        AdvisorMenu.Translate => (true, Translate),
        AdvisorMenu.Tips => (true, !TipsOff),
        _ => (false, false),
    };
}
