namespace OpenSWR.Sim.Ai;

/// <summary>The "none" values of the op codes: request kinds, request types and retire keys (saved as ints).</summary>
public static class OpCodes
{
    /// <summary>No request: a request kind, subtype, or character request type of 0.</summary>
    public const int NoRequest = 0;
    /// <summary>No op: a retire key of 0.</summary>
    public const int NoOp = 0;
}

/// <summary>The model's garrison and defence constants; the planners raise some of them.</summary>
public enum ModelConstant
{
    /// <summary>Coruscant battery target.</summary>
    CoruscantBatteries,
    /// <summary>Coruscant GenCore target.</summary>
    CoruscantGenCores,
    /// <summary>Role-system GenCore target.</summary>
    RoleGenCores,
    /// <summary>Role-system battery target.</summary>
    RoleBatteries,
    /// <summary>GenCore target (role fallback and other systems; raised by planner 3).</summary>
    GenCores,
    /// <summary>Battery target of other systems.</summary>
    Batteries,
    /// <summary>Garrison target of other systems (raised by planner 6).</summary>
    Garrison,
    /// <summary>Role-system garrison target (raised by planner 6).</summary>
    RoleGarrison,
    /// <summary>Key-system garrison target (raised by planner 6).</summary>
    KeyGarrison,
    /// <summary>Fighter margin of other systems (raised by planner 7).</summary>
    FighterMargin,
    /// <summary>Role-system fighter margin (raised by planner 7).</summary>
    RoleFighterMargin,
    /// <summary>Key-system fighter margin (raised by planner 7).</summary>
    KeyFighterMargin,
}

/// <summary>The production roles a planner gives a system.</summary>
public enum SystemRole { Shipyard, Training, Construction, Mine, Defence }

/// <summary>The theater argument: which site field of a sector theater the type-4 site pick uses. None for a sector with no theater.</summary>
public enum TheaterArgument { None, Shipyards, Training, Construction }
