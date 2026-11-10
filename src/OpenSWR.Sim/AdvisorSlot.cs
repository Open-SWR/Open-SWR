namespace OpenSWR.Sim;

/// <summary>Request slots by request (action numbers per side), and the slot counts of the draws. Names are the requests' use; see the message codes in <see cref="AdvisorCode"/>.</summary>
public static class AdvisorSlot
{
    /// <summary>C-3PO 88 slots (ids 1-87), IMP-22 86 (ids 1-85).</summary>
    public const int C3poSlots = 88, ImpSlots = 86;
    /// <summary>Tip reactions: four slots, random generic: two, random tip: two, idle chatter: six of twelve draws.</summary>
    public const int TipCount = 4, RandomTipCount = 2, GenericCount = 2, IdleDraw = 12, IdleRequests = 6;

    // ---- message codes, in code order ----
    public static readonly SideSlot GoodNews = new(52, 48), BadNews = new(53, 49), Deployed = new(56, 52), ResearchComplete = new(58, 54),
        ShipsArrived = new(59, 55), UnitsArrived = new(61, 57), ShipRepaired = new(60, 56), SquadronFull = new(62, 58),
        MaintenanceShortfall = new(57, 53), BlockadeStarted = new(65, 61), SystemBlockaded = new(67, 62), Report = new(42, 64),
        MinorCharacter = new(66, 63), Luke = new(45, 64), Mothma = new(43, 64), Leia = new(44, 64), Han = new(46, 64),
        Palpatine = new(42, 34), Vader = new(42, 33), ResourceEvent = new(55, 51), LukeCaptured = new(28, 42), MothmaCaptured = new(27, 41),
        LeiaCaptured = new(29, 43), HanCaptured = new(30, 44), PalpatineCaptured = new(32, 46), VaderCaptured = new(31, 45),
        CharacterEscaped = new(51, 47), LukeEscaped = new(48, 38), MothmaEscaped = new(47, 37), PalpatineEscaped = new(50, 40),
        VaderEscaped = new(49, 39), SideMessage = new(68, 65), UnusedTip = new(75, 0), BountyHuntersFoundHan = new(0, 72),
        Bombardment = new(25, 35), Assault = new(26, 36);
    /// <summary>Tip reactions: C-3PO 69-72, IMP-22 66-69 (first slot).</summary>
    public static readonly SideSlot Tip = new(69, 66);
    /// <summary>Random tip: C-3PO 73-74, IMP-22 70-71 (first slot).</summary>
    public static readonly SideSlot RandomTip = new(73, 70);
    /// <summary>Random generic: C-3PO 17-18, IMP-22 18-19 (first slot).</summary>
    public static readonly SideSlot GenericFirst = new(17, 18);

    // ---- status rules (the request flags) ----
    public static readonly SideSlot Hidden = new(34, 27), Shown = new(33, 26), Briefing = new(41, 74), SkipBriefing = new(40, 73),
        HelperDone = new(35, 28), ManageGarrisonsOn = new(36, 29), ManageProductionOn = new(38, 31), ManageGarrisonsOff = new(37, 30),
        ManageProductionOff = new(39, 32), StatusAllianceWins = new(86, 85), StatusEmpireWins = new(87, 84);
    /// <summary>Delegation toggle chatter: C-3PO only.</summary>
    public static readonly SideSlot ToggledOften = new(79, 0);
    /// <summary>Window-menu clicks: the first of three rows (C-3PO 76-78, IMP-22 75-77).</summary>
    public static readonly SideSlot MenuFirst = new(76, 75);
    /// <summary>Idle chatter: the first of six lines (C-3PO 80-85, IMP-22 78-83).</summary>
    public static readonly SideSlot IdleFirst = new(80, 78);

    // ---- refusals ----
    /// <summary>Repeated refusal of the same pair: the first and then the second repeat.</summary>
    public static readonly SideSlot RepeatFirst = new(15, 16), RepeatSecond = new(16, 17);
    /// <summary>System refusals of category 144 (energy, raw material, blockade) and the Death Star limit (IMP-22 only).</summary>
    public static readonly SideSlot NoEnergy = new(11, 12), NoRawMaterials = new(12, 13), Blockaded = new(21, 22), DeathStarLimit = new(0, 11);
    /// <summary>Manager refusals of category 160: all suspended, and the code 2 line whose meaning in the original is unknown.</summary>
    public static readonly SideSlot AllSuspended = new(24, 25), UnknownManager = new(2, 2);
    /// <summary>Ship room refusals (category 20): the ship has the capacity (room full) or none; fleet room.</summary>
    public static readonly SideSlot ShipHasCapacity = new(8, 8), ShipHasNoCapacity = new(7, 7), FleetRoom = new(9, 9);
    /// <summary>Maintenance refusal of the side (category 243).</summary>
    public static readonly SideSlot SideNoMaintenance = new(10, 10);
    public static readonly SideSlot NoMissionType = new(3, 3), NoFreeAgent = new(20, 21), CannotTravel = new(5, 5), NoDestination = new(6, 6),
        FleetNotInSystem = new(7, 7), FallbackDestination = new(23, 24), DestinationNotCompleted = new(22, 23), EnrouteRefused = new(1, 1),
        SystemOwner = new(2, 2);
}
