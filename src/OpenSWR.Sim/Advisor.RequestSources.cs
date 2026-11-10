using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

public sealed partial class Advisor
{
    // ---- request sources ----

    // Code maps to (request slot, C-3PO validity, IMP-22 validity); "a / b" validity cells read as C-3PO / IMP (Inferred: the
    // validity lives in the per-advisor handler, the message carries only the code)
    static readonly Dictionary<AdvisorCode, (SideSlot Slot, int Cv, int Iv)> Codes = new()
    {
        [AdvisorCode.GoodNews] = (AdvisorSlot.GoodNews, Validity.Normal, Validity.Normal),
        [AdvisorCode.BadNews] = (AdvisorSlot.BadNews, Validity.Normal, Validity.Normal),
        [AdvisorCode.Deployed] = (AdvisorSlot.Deployed, Validity.Brief, Validity.Normal),
        [AdvisorCode.ResearchComplete] = (AdvisorSlot.ResearchComplete, Validity.Normal, Validity.Normal),
        [AdvisorCode.ShipsArrived] = (AdvisorSlot.ShipsArrived, Validity.Normal, Validity.Normal),
        [AdvisorCode.UnitsArrived] = (AdvisorSlot.UnitsArrived, Validity.Normal, Validity.Normal),
        [AdvisorCode.ShipRepaired] = (AdvisorSlot.ShipRepaired, Validity.Normal, Validity.Normal),
        [AdvisorCode.SquadronFull] = (AdvisorSlot.SquadronFull, Validity.Normal, Validity.Normal),
        [AdvisorCode.MaintenanceShortfall] = (AdvisorSlot.MaintenanceShortfall, Validity.Normal, Validity.Normal),
        [AdvisorCode.BlockadeStarted] = (AdvisorSlot.BlockadeStarted, Validity.Normal, Validity.Normal),
        [AdvisorCode.SystemBlockaded] = (AdvisorSlot.SystemBlockaded, Validity.Normal, Validity.Brief),
        [AdvisorCode.Report] = (AdvisorSlot.Report, Validity.Normal, Validity.Normal),
        [AdvisorCode.MinorCharacterMessage] = (AdvisorSlot.MinorCharacter, Validity.Normal, Validity.Normal),
        [AdvisorCode.LukeMessage] = (AdvisorSlot.Luke, Validity.Long, Validity.Long),
        [AdvisorCode.MothmaMessage] = (AdvisorSlot.Mothma, Validity.Long, Validity.Long),
        [AdvisorCode.LeiaMessage] = (AdvisorSlot.Leia, Validity.Long, Validity.Long),
        [AdvisorCode.HanMessage] = (AdvisorSlot.Han, Validity.Long, Validity.Long),
        [AdvisorCode.PalpatineMessage] = (AdvisorSlot.Palpatine, Validity.Long, Validity.Long),
        [AdvisorCode.VaderMessage] = (AdvisorSlot.Vader, Validity.Long, Validity.Long),
        [AdvisorCode.ResourceEvent] = (AdvisorSlot.ResourceEvent, Validity.Normal, Validity.Long),
        [AdvisorCode.LukeCaptured] = (AdvisorSlot.LukeCaptured, Validity.Long, Validity.Long),
        [AdvisorCode.MothmaCaptured] = (AdvisorSlot.MothmaCaptured, Validity.Long, Validity.Long),
        [AdvisorCode.LeiaCaptured] = (AdvisorSlot.LeiaCaptured, Validity.Long, Validity.Long),
        [AdvisorCode.HanCaptured] = (AdvisorSlot.HanCaptured, Validity.Long, Validity.Long),
        [AdvisorCode.PalpatineCaptured] = (AdvisorSlot.PalpatineCaptured, Validity.Long, Validity.Long),
        [AdvisorCode.VaderCaptured] = (AdvisorSlot.VaderCaptured, Validity.Long, Validity.Long),
        [AdvisorCode.CharacterEscaped] = (AdvisorSlot.CharacterEscaped, Validity.Long, Validity.Long),
        [AdvisorCode.LukeEscaped] = (AdvisorSlot.LukeEscaped, Validity.Long, Validity.Long),
        [AdvisorCode.MothmaEscaped] = (AdvisorSlot.MothmaEscaped, Validity.Long, Validity.Long),
        [AdvisorCode.PalpatineEscaped] = (AdvisorSlot.PalpatineEscaped, Validity.Long, Validity.Long),
        [AdvisorCode.VaderEscaped] = (AdvisorSlot.VaderEscaped, Validity.Long, Validity.Long),
        [AdvisorCode.SideMessage] = (AdvisorSlot.SideMessage, Validity.Medium, Validity.Medium),
        [AdvisorCode.UnusedTip] = (AdvisorSlot.UnusedTip, Validity.Normal, Validity.None),
        [AdvisorCode.BountyHuntersFoundHan] = (AdvisorSlot.BountyHuntersFoundHan, Validity.None, Validity.Normal),
        [AdvisorCode.Bombardment] = (AdvisorSlot.Bombardment, Validity.Normal, Validity.Normal),
        [AdvisorCode.Assault] = (AdvisorSlot.Assault, Validity.Normal, Validity.Normal),
    };

    /// <summary>A message added for the player with advisor code <paramref name="code"/>.</summary>
    public void MessageCode(AdvisorCode code)
    {
        switch (code)
        {
            case AdvisorCode.Tip: // tip: 69-72 / 66-69 by rand(4), only when none of the four is pending (IMP guard Inferred)
                int first = AdvisorSlot.Tip.For(Imp);
                if (Enumerable.Range(first, AdvisorSlot.TipCount).Any(Pending)) return;
                int r = Rand(AdvisorSlot.TipCount);
                if (r < AdvisorSlot.TipCount) Request(first + r, Validity.Normal); // r = 4 (rare out-of-range result): nothing (Inferred)
                return;
            case AdvisorCode.RandomTip: // no setter in this build
                int s = Rand(AdvisorSlot.RandomTipCount);
                if (s < AdvisorSlot.RandomTipCount) Request(AdvisorSlot.RandomTip.For(Imp) + s, Validity.Normal);
                return;
        }
        if (Codes.TryGetValue(code, out var e) && e.Slot.For(Imp) is var id and > 0) Request(id, Imp ? e.Iv : e.Cv);
    }

    /// <summary>"Random generic": rand(2) gives C-3PO 17 / 18, IMP 18 / 19; result 2 requests nothing.</summary>
    void Generic(int validity = Validity.Normal)
    {
        int r = Rand(AdvisorSlot.GenericCount);
        if (r < AdvisorSlot.GenericCount) Request(AdvisorSlot.GenericFirst.For(Imp) + r, validity);
    }

    /// <summary>
    /// A refused player order: the same refusal as the previous one requests the repeat slots
    /// (the second repeat forgets it); otherwise the refusal is stored and dispatched by its family with the exact
    /// tests. <paramref name="game"/> gives the advisor side's views (null: every view test fails). Inferred:
    /// openswr's views keep no manager TargetKey, so the Refusal.DestinationOtherOwner / Refusal.DestinationRefused side-1 manager test reads the real TargetKey while
    /// side 1's view of the manager is live and fails otherwise.
    /// </summary>
    public void Refused(OrderResult result, Command? command, GameState? game = null)
    {
        if (_pair == result)
        {
            Request(_repeated ? AdvisorSlot.RepeatSecond : AdvisorSlot.RepeatFirst, Validity.Normal);
            if (_repeated) _pair = null;
            _repeated = !_repeated;
            return;
        }
        (_pair, _repeated) = (result, false);
        var arg = command?.ArgRef;
        uint argFamily = arg is null ? 0 : Family.Of(arg.Type);
        bool fleet = argFamily is >= Families.Fleet and < Families.Troop, system = argFamily is >= Families.CoreSystem and < Families.Abode;
        // refusal families: the first match decides; the family ranges of the refusal categories are the family ranges of their objects
        if (result.Category == Families.CoreSystem)
        {
            if (result == Refusal.NoEnergy) Request(AdvisorSlot.NoEnergy, Validity.Normal);
            else if (result == Refusal.NoRawMaterials) Request(AdvisorSlot.NoRawMaterials, Validity.Normal);
            else if (result == Refusal.Blockaded) Request(AdvisorSlot.Blockaded, Validity.Normal);
            else if (result == Refusal.DeathStarLimit && Imp) Request(AdvisorSlot.DeathStarLimit.Imp, Validity.Normal);
            else Generic();
            return;
        }
        if (result.Category == Families.ConstructionManager)
        {
            if (result == Refusal.AllSuspended) Request(AdvisorSlot.AllSuspended, Validity.Normal);
            else if (result == Refusal.UnknownManager) Request(AdvisorSlot.UnknownManager, Validity.Normal);
            else Generic();
            return;
        }
        if (result.Category is >= (int)Families.Fleet and < (int)Families.Troop or >= (int)Families.CoreSystem and < (int)Families.Abode
            or >= (int)Families.ConstructionManager and < (int)Families.ManagerEnd or >= (int)Families.CapitalShip + 1 and < (int)Families.Fighter)
        {
            Generic();
            return;
        }
        if (result.Category == Families.CapitalShip)
        {
            // V = the advisor side's capital-ship view of the ArgRef (every object has one; only families [20, 28))
            bool room = result == Refusal.NoFighterRoom || result == Refusal.NoTroopRoom;
            if (room && argFamily is >= Families.CapitalShip and < Families.Fighter)
            {
                var ship = game?.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == arg!.Type);
                uint capacity = ship is null ? 0 : result == Refusal.NoFighterRoom ? ship.FighterCapacity : ship.TroopCapacity;
                Request(capacity != 0 ? AdvisorSlot.ShipHasCapacity : AdvisorSlot.ShipHasNoCapacity, Validity.Brief);
            }
            else if (room && fleet) Request(AdvisorSlot.FleetRoom, Validity.Brief);
            else Generic();
            return;
        }
        if (result.Category == Families.Side)
        {
            if (result == Refusal.NoMaintenance) Request(AdvisorSlot.SideNoMaintenance, Validity.Normal);
            else Generic();
            return;
        }
        if (result == Refusal.NoLocation || result == Refusal.Missing || result == Refusal.RewrittenDestination) return;
        if (result == Refusal.NoMissionType)
        {
            if (arg is not null) Request(AdvisorSlot.NoMissionType, Validity.Normal);
            else Generic();
            return;
        }
        if (result == Refusal.NoFreeAgent)
        {
            if (arg is not null && command!.Type == CommandType.CreateMission && command.Targets.Count == 0 && command.Decoys.Count > 0) Request(AdvisorSlot.NoFreeAgent, Validity.Normal);
            else Generic();
            return;
        }
        if (result == Refusal.CannotTravel)
        {
            Request(AdvisorSlot.CannotTravel, Validity.Normal);
            return;
        }
        if (result == Refusal.NoDestination) // a Destroyed system view gives 6; otherwise nothing at all (no generic line, no draw)
        {
            if (system && game is not null && Knowledge.See(game, arg!, Side).Destroyed) Request(AdvisorSlot.NoDestination, Validity.Normal);
            return;
        }
        if (result == Refusal.NotInSystem || result == Refusal.WrongDestinationKind)
        {
            Request(fleet ? AdvisorSlot.FleetNotInSystem : AdvisorSlot.FallbackDestination, Validity.Normal);
            return;
        }
        if (result == Refusal.DestinationNotCompleted)
        {
            Request(AdvisorSlot.DestinationNotCompleted, Validity.Normal);
            return;
        }
        if (result == Refusal.Enroute || result == Refusal.DestinationEnroute)
        {
            Request(AdvisorSlot.EnrouteRefused, Validity.Normal);
            return;
        }
        if (result == Refusal.DestinationOtherOwner || result == Refusal.DestinationRefused)
        {
            // side 1's view of a manager first target
            // the view's TargetKey of its last refresh: a frozen Empire manager view normally holds its own system
            bool managerAimed = command?.Targets.FirstOrDefault().Target is ManufacturingManager m && Knowledge.TargetKey(m, Owner.Alliance) is { } key
                && Family.Of(key.Type) is >= Families.CoreSystem and < Families.Abode;
            Request(system || managerAimed ? AdvisorSlot.SystemOwner : AdvisorSlot.FallbackDestination, Validity.Normal);
            return;
        }
        Generic();
    }

    /// <summary>The build helper's prepared Build command, watched while <see cref="Watching"/> is set.</summary>
    public Command? Watched { get; private set; }

    /// <summary>
    /// An accepted player order: refusal memory and menu-click counter cleared; the helper's watched Build command
    /// sets <see cref="HelperDone"/> (action 35 / 28); <see cref="Watching"/> cleared. The production window builds a new command from the prepared one, so
    /// "the watched command" = the same type for the same manager (Inferred).
    /// </summary>
    public void Accepted(Command command)
    {
        (_pair, _repeated, MenuClicks) = (null, false, 0);
        if (Watching && Watched is { } w && command.Type == w.Type && command.Targets.Select(t => t.Target).SequenceEqual(w.Targets.Select(t => t.Target)))
            HelperDone = true;
        Watching = false;
        Watched = null;
    }
}
