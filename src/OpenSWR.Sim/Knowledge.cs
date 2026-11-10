using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>Controller (view) message types: posted by a side's view when one of its fields changes.</summary>
public enum ViewMessageType
{
    /// <summary>Not set (the default value).</summary>
    None,
    Owner,
    Usable,
    Created,
    Completed,
    Enroute,
    Damaged,
    Support,
    Populated,
    Explored,
    Uprising,
    NeverBeenControlled,
    Battle,
    Blockade,
    QueueCount,
    TaskStatus,
    CompletionStatus,
    Parent,
    Destroyed,
    /// <summary>Espionage found an extra system.</summary>
    EspionageExtraSystem,
    Name,
}

/// <summary>
/// Frozen side view of an object: the values of its last refresh. Only the fields the sim reads through views
/// are kept (the mission phase checks, the command target check and the view location): owner, parent, Destroyed,
/// Enroute, Completed, BuilderKey, system Uprising / Populated / Explored / support, character Captured.
/// </summary>
public sealed class View
{
    public Owner Owner { get; set; }
    public GameObject? Parent { get; set; }
    public bool Destroyed { get; set; }
    public bool Enroute { get; set; }
    /// <summary>EnrouteActive, read by the AI ship records.</summary>
    public bool EnrouteActive { get; set; }
    public bool Uprising { get; set; }
    public bool Populated { get; set; }
    public bool Explored { get; set; }
    public bool Captured { get; set; }
    public int Support { get; set; }
    public bool Completed { get; set; }
    /// <summary>Created: copied by a full refresh; a fresh view has it clear. Gates the staging pass's posts.</summary>
    public bool Created { get; set; }
    public ManufacturingManager? BuilderKey { get; set; }
    /// <summary>System Blockade / Battle. The setters copy into live views; a frozen view keeps them.</summary>
    public bool Blockade { get; set; }
    public bool Battle { get; set; }
    /// <summary>Managers: queue count at the last full refresh (posts ViewMessageType.QueueCount on a change).</summary>
    public int QueueCount { get; set; }
    /// <summary>Missions: TaskStatus / CompletionStatus at the last full refresh (posts the TaskStatus / CompletionStatus messages on a change).</summary>
    public MissionTaskStatus TaskStatus { get; set; }
    public MissionCompletion CompletionStatus { get; set; }
    /// <summary>Characters / special forces: RoleMissionKey at the last refresh, read by the owner-change refresh.</summary>
    public Mission? RoleMission { get; set; }
    /// <summary>Damaged. The setter copies into live views, and a full refresh copies it. Read by the AI ship records.</summary>
    public bool Damaged { get; set; }
    /// <summary>Usable: copied by a full refresh and the systems' position refresh; a fresh view has it clear.</summary>
    public bool Usable { get; set; }
    /// <summary>
    /// Capital ships: the speed computed from the view's Existing, hyperdrive crit counters and
    /// DestroyedReason at the last copy. Inferred shortcut: the computed speed is stored instead of its input fields (an equivalent shortcut, Inferred).
    /// </summary>
    public int Speed { get; set; }
    /// <summary>System control: copied by a full refresh and by the updates' support part A, not by the step-24 position refresh.</summary>
    public SystemControl Control { get; set; }
    /// <summary>
    /// System NeverBeenControlled: copied by every full refresh (frozen views too) and, for live views, by the real
    /// setter's copy; the step-24 position refresh and the live to frozen transition copy nothing, so the value can differ from the
    /// real flag even while live (a fresh view: clear). A change after game-ready posts the NeverBeenControlled view message.
    /// </summary>
    public bool NeverBeenControlled { get; set; }
    /// <summary>
    /// openswr extra: the day of the last frozen-view write (full refresh, live to frozen snapshot, knowledge update); 0 = never.
    /// Read only by the openswr controller and aimatch (<see cref="Knowledge.UpdatedDay"/>); no rule of the original reads it.
    /// </summary>
    public int Updated { get; set; }
}

/// <summary>What side s sees of an object: the real values while its view is live, else the frozen view (or record defaults).</summary>
public readonly record struct Seen(Owner Owner, bool Destroyed, bool Enroute, GameObject? Location, bool Uprising, bool Populated, bool Captured,
    int Support, bool Completed, ManufacturingManager? BuilderKey, bool Explored, bool EnrouteActive, bool Blockade = false, bool Battle = false);

/// <summary>
/// Knowledge / fog of war: per side, a live view (mirrors the real object) or a frozen one (last refresh). Covers the live condition
/// ObservedBy (systems, inherited by fleets, ships, fighters, troops, facilities), the system
/// knowledge updates (modes 0-12) and setup step 24. No RNG. Views are kept only as far as the sim reads them: live
/// state per side (<see cref="GameObject.LiveSides"/>) and the frozen snapshot (<see cref="GameState.Views"/>).
/// Not kept: names, positions, every field no specced sim reader uses; view-tree drops of contents apart from the
/// parent (staging) and its parent-change post; sectors and manager views (only their TargetKey, <see cref="ManufacturingManager.ViewTargetKey"/>).
/// </summary>
public static class Knowledge
{
    static readonly Owner[] Sides = [Owner.Alliance, Owner.Empire];

    static bool IsAbode(GameObject o) => Family.Of(o.Type) == Families.Abode;

    /// <summary>
    /// Live state per side; objects never recomputed are live for their owner's side only (views are created frozen, then the owner
    /// setter's hook runs). Manufacturing managers: live when owner = side, never cached (their ObservedBy is always 0;
    /// owner 3 = both frozen).
    /// </summary>
    static SideSet Live(GameObject o) => o is ManufacturingManager ? SideSet.Only(o.Owner) : o.LiveSides ??= SideSet.Only(o.Owner);

    public static bool IsLive(GameObject o, Owner side) => Live(o)[side];

    /// <summary>
    /// Live condition per side: owner = s or (ObservedBy_s and Completed and not Enroute). Live to frozen: the view keeps
    /// the current values (snapshot); frozen to live: full refresh of the view and of its direct contents (live content views
    /// already mirror the real objects; frozen ones whose real object left are dropped from the view tree: parent = staging). Run by
    /// the owner hook, the Usable notifier and the ObservedBy hooks.
    /// </summary>
    internal static void RecomputeLive(GameState game, GameObject o)
    {
        if (!game.Ready || o is ManufacturingManager || o.Deleted) return; // setup bypasses the setters; step 24 initialises the live states; managers: ManagerOwnerChanged
        SideSet old = Live(o), now = SideSet.None;
        foreach (var side in Sides)
            if (o.Owner == side || o.Observed[side] && o.Completed && !o.Enroute) now = now.With(side, true);
        if (now == old) return;
        o.LiveSides = now;
        foreach (var side in Sides)
        {
            bool was = old[side], live = now[side];
            if (was && !live) Snapshot(game, o, side, fromLive: true);
            else if (!was && live)
            {
                var stale = Role(game, o, side);
                game.ViewRefreshed(o, side);
                if (o is StarSystem ns) CopyNeverControlled(game, ns, side); // the full refresh's copy (posted by ViewRefreshed)
                PostRole(game, o, stale); // the role copy runs (context 1)
                // a live view mirrors the real object: its entry's frozen-period Parent / RoleMission are no state of the original
                // the next live to frozen snapshot rewrites them
                if (game.Views.TryGetValue((o, side), out var entry)) (entry.Parent, entry.RoleMission) = (null, null);
                foreach (var ((x, s), v) in game.Views.Where(e => e.Key.Item2 == side && e.Value.Parent == o && !IsLive(e.Key.Item1, side)).OrderBy(e => e.Key.Item1.Key).ToList())
                    if (x.Container != o) { Drop(game, x, side, v); Pass(game, x, side); } // the drop, then the staging pass
                Objects.ReadyTest(game, o); // the refresh copied Destroyed / parent into the view
            }
        }
    }

    /// <summary>
    /// View-tree drop: the view goes into the side's view of the staging container (no system
    /// above it: location none) and posts a parent-change notification (from the old view parent to the staging container) behind the parent-refresh gates. Drops run after the
    /// refreshes, in ascending view key order (second loop of the content refresh, verified).
    /// </summary>
    static void Drop(GameState game, GameObject x, Owner side, View v)
    {
        if (v.Parent == game.Staging) return; // already in staging, nothing
        var old = v.Parent;
        v.Parent = game.Staging;
        if (game.Controllers.ContainsKey(side) && game.Ready) game.Messages.Add((side, ViewMessageType.Parent, x, game.Staging, old));
        Objects.ReadyTest(game, x); // a view parent change: the moved object, then the old parent
        Objects.ReadyTest(game, old);
    }

    /// <summary>The day side s's view of o was last updated: today while live, else the frozen view's write day (0: never written).</summary>
    public static int UpdatedDay(GameState game, GameObject o, Owner side) =>
        IsLive(o, side) ? game.Day : game.Views.TryGetValue((o, side), out var v) ? v.Updated : 0;

    /// <summary>Side s's view of o is Destroyed: live to the real flag, frozen to the view's; null = no view (fresh or deleted).</summary>
    internal static bool? ViewDestroyed(GameState game, GameObject o, Owner side) =>
        IsLive(o, side) ? o.Destroyed : game.Views.TryGetValue((o, side), out var v) ? v.Destroyed : null;

    /// <summary>Side s's view of o has Created set (live: the real flag; fresh: clear).</summary>
    internal static bool ViewCreated(GameState game, GameObject o, Owner side) =>
        IsLive(o, side) ? o.Created : game.Views.TryGetValue((o, side), out var v) && v.Created;

    /// <summary>Side s's view of o has content views (live views of real contents, frozen views parented to o).</summary>
    internal static bool HasContentViews(GameState game, GameObject o, Owner side) =>
        o.Contents.Any(x => IsLive(x, side)) || game.Views.Any(e => e.Key.Item2 == side && e.Value.Parent == o && !IsLive(e.Key.Item1, side));

    /// <summary>
    /// Parent copy of a frozen view, run by the parent setter for side 1 then side 2 before the parent-change hook:
    /// a frozen view whose view parent OP is live for that side and is not the new real parent is dropped into staging (parent change post
    /// OP to staging); a frozen OP: nothing (the stale parent stays); no view or no view parent: nothing (Inferred: the copy needs
    /// both views). Live views move with the real object (the setter's parent-change post). After a move or a drop the staging pass
    /// runs on the view (<see cref="Pass"/>; per side: side 1 copy and pass, then side 2).
    /// </summary>
    internal static void ParentCopy(GameState game, GameObject o, GameObject parent)
    {
        if (!game.Ready) return; // setup bypasses the setters
        foreach (var side in Sides)
            if (IsLive(o, side)) Pass(game, o, side);
            else if (game.Views.TryGetValue((o, side), out var v) && v.Parent is { } op && op != parent && IsLive(op, side))
            {
                Drop(game, o, side, v);
                Pass(game, o, side);
            }
    }

    /// <summary>Side s's view parent: the real container while the view is live, else the frozen view's parent (fresh view: none).</summary>
    public static GameObject? ViewParent(GameState game, GameObject x, Owner side) =>
        IsLive(x, side) ? x.Container : game.Views.TryGetValue((x, side), out var v) ? v.Parent : null;

    /// <summary>Side s's content views of o in ascending key order: live views of its real contents and frozen views whose parent is o.</summary>
    static List<GameObject> ContentViews(GameState game, GameObject o, Owner side) =>
        o.Contents.Where(x => IsLive(x, side))
            .Concat(game.Views.Where(e => e.Key.Item2 == side && e.Value.Parent == o && !IsLive(e.Key.Item1, side)).Select(e => e.Key.Item1))
            .Distinct().OrderBy(x => x.Key).ToList();

    /// <summary>
    /// Staging pass on side s's view of x: only when the view's parent chain reaches the staging container;
    /// first on its content views (recursively), then on the view: real x Destroyed, not Locked and its real parent
    /// staging: its content views are dropped into staging (parent-change post each, ascending key, Inferred order), the side's context flag
    /// is set to the view's Created flag; characters / special forces: the class pass (<see cref="RoleClear"/>); a
    /// frozen view then takes Destroyed: a change posts the Destroyed view message (parent staging) when the flag is set (the side's
    /// controller acknowledges with AcknowledgeDestroyed); finally the view itself is dropped (nothing when already in staging).
    /// Every class. Repeating it posts nothing new.
    /// </summary>
    static void Pass(GameState game, GameObject x, Owner side)
    {
        if (!InStaging(game, x, side)) return;
        foreach (var c in ContentViews(game, x, side)) Pass(game, c, side);
        if (!x.Destroyed || x.Locked || x.Container != game.Staging) return;
        foreach (var c in ContentViews(game, x, side))
            if (game.Views.TryGetValue((c, side), out var cv)) Drop(game, c, side, cv); // live content views: none (the real object is empty at staging)
        bool live = IsLive(x, side);
        View? pv = null;
        if (!live && (!game.Views.TryGetValue((x, side), out pv) || pv.Parent is null))
            // a fresh view (no entry, or none parent) sits in the side's staging view: it takes Destroyed silently below
            // (not Created: no Destroyed view message, no strike entry, no role post; Drop finds it in staging).
            (pv ??= game.Views[(x, side)] = new View { Owner = Owner.Neutral }).Parent = game.Staging;
        bool flag = live ? x.Created : pv!.Created;
        if (x is Agent a) RoleClear(game, a, side, flag, pv);
        if (pv is null) return;
        if (!pv.Destroyed)
        {
            pv.Destroyed = true;
            if (flag && game.Controllers.ContainsKey(side) && game.Ready) game.Messages.Add((side, ViewMessageType.Destroyed, x, game.Staging, null));
            if (flag) Combat.StrikeStaged(game, x, side, pv.Completed); // this Destroyed view message joins the strike's group (UI only)
            Objects.ReadyTest(game, x); // view Destroyed write
        }
        Drop(game, x, side, pv);
    }

    /// <summary>
    /// Pass's parent-chain test: side s's view of x reaches the staging container. A non-live view without an entry or
    /// with a none parent is a fresh view, which the original parents to the side's staging view from its creation:
    /// it counts as reaching staging. Roots without a real container (systems, sectors; the galaxy is never inserted) do not
    /// (Inferred: openswr keeps no galaxy / sector parents). <see cref="ViewParent"/> stays as it is for its other callers.
    /// </summary>
    static bool InStaging(GameState game, GameObject x, Owner side)
    {
        for (GameObject? p = x; p is not null; p = ViewParent(game, p, side))
        {
            if (p == game.Staging) return true;
            if (!IsLive(p, side) && p.Container is not null && !(game.Views.TryGetValue((p, side), out var v) && v.Parent is not null)) return true;
        }
        return false;
    }

    /// <summary>
    /// Writes the view's frozen copy. <paramref name="fromLive"/>: the live to frozen transition, where the view already held the
    /// real values (no field changes, so no ready-test trigger; the entry's own values are no state then).
    /// </summary>
    static View Snapshot(GameState game, GameObject o, Owner side, bool fromLive = false)
    {
        if (!game.Views.TryGetValue((o, side), out var v)) game.Views[(o, side)] = v = new View();
        var (oldParent, wasDestroyed) = (v.Parent, v.Destroyed);
        v.Owner = o.Owner;
        v.Parent = o.Container;
        v.Destroyed = o.Destroyed;
        v.Enroute = o.Enroute;
        v.EnrouteActive = o.EnrouteActive;
        v.Completed = o.Completed;
        v.Created = o.Created;
        v.BuilderKey = o.BuilderKey;
        if (o is Agent a) v.RoleMission = a.Mission;
        if (o is StarSystem s) (v.Uprising, v.Populated, v.Support, v.Explored, v.Blockade, v.Battle, v.Control) = (s.Uprising, s.Populated, s.Support, s.Explored, s.Blockade, s.Battle, s.Control);
        if (o is StarSystem ns && !fromLive) v.NeverBeenControlled = ns.NeverBeenControlled; // full refresh only
        if (o is ManufacturingManager m) (m.ViewTargetKey[(int)side], v.QueueCount) = (m.TargetKey, m.Orders.Count);
        if (o is Mission ms) (v.TaskStatus, v.CompletionStatus) = (ms.TaskStatus, ms.CompletionStatus);
        if (o is Character c) v.Captured = c.Captured;
        v.Damaged = o.Damaged;
        v.Usable = o.Usable;
        if (Family.IsCapitalShip(o.Type)) v.Speed = Orders.Speed(game, o);
        v.Updated = game.Day;
        if (!fromLive && (v.Destroyed != wasDestroyed || v.Parent != oldParent)) // view Destroyed write / view parent change
        {
            Objects.ReadyTest(game, o);
            if (v.Parent != oldParent) Objects.ReadyTest(game, oldParent);
        }
        return v;
    }

    /// <summary>
    /// Core-system support copy (run from the support hook with context 3): for side 1 then side 2 a non-live
    /// view (created fresh if missing: owner 3, flags clear) takes the new support, posting the Support view message when it changed; live
    /// views mirror already. Every post-setup writer (trace run 13); called after the support hook, core systems only.
    /// </summary>
    internal static void CopySupport(GameState game, StarSystem s)
    {
        foreach (var side in Sides.Where(side => !IsLive(s, side)))
        {
            if (!game.Views.TryGetValue((s, side), out var v)) game.Views[(s, side)] = v = new View { Owner = Owner.Neutral };
            Changed(game, side, ViewMessageType.Support, s, v.Support != s.Support);
            v.Support = s.Support;
        }
    }

    /// <summary>
    /// Core-system Uprising copy (run from the Uprising hook after its body, context 3): for side
    /// 1 then side 2 a non-live view (created fresh if missing) takes the Uprising value, posting the Uprising view message when it changed; live
    /// views mirror already (their notification came from the setter).
    /// </summary>
    internal static void CopyUprising(GameState game, StarSystem s)
    {
        foreach (var side in Sides.Where(side => !IsLive(s, side)))
        {
            if (!game.Views.TryGetValue((s, side), out var v)) game.Views[(s, side)] = v = new View { Owner = Owner.Neutral };
            Changed(game, side, ViewMessageType.Uprising, s, v.Uprising != s.Uprising);
            v.Uprising = s.Uprising;
        }
    }

    /// <summary>
    /// The view copy of NeverBeenControlled into side s's view (no post here: the full refresh posts through
    /// <see cref="GameState.ViewRefreshed"/>). A missing entry stands for a fresh view (clear).
    /// </summary>
    static void CopyNeverControlled(GameState game, StarSystem s, Owner side)
    {
        if (game.Views.TryGetValue((s, side), out var v)) v.NeverBeenControlled = s.NeverBeenControlled;
        else if (s.NeverBeenControlled) game.Views[(s, side)] = new View { Owner = Owner.Neutral, NeverBeenControlled = true };
    }

    /// <summary>
    /// The real NeverBeenControlled setter, after a change: side 1 then side 2, each live view takes the value; a change
    /// of the view posts the NeverBeenControlled view message after game-ready (A = the system when the new value is set, standing in for the message's new value).
    /// Frozen views keep theirs.
    /// </summary>
    internal static void NeverControlledChanged(GameState game, StarSystem s)
    {
        foreach (var side in Sides.Where(side => IsLive(s, side)))
        {
            if (NeverControlled(game, s, side) == s.NeverBeenControlled) continue;
            CopyNeverControlled(game, s, side);
            if (game.Controllers.ContainsKey(side) && game.Ready) game.Messages.Add((side, ViewMessageType.NeverBeenControlled, s, s.NeverBeenControlled ? s : null, null));
        }
    }

    /// <summary>Side s's view value of NeverBeenControlled (live views too: the view keeps its own copy; fresh: clear).</summary>
    public static bool NeverControlled(GameState game, StarSystem s, Owner side) =>
        game.Views.TryGetValue((s, side), out var v) && v.NeverBeenControlled;

    /// <summary>A frozen view's field write that changed it posts its message to the side's controller.</summary>
    static void Changed(GameState game, Owner side, ViewMessageType type, GameObject o, bool changed)
    {
        if (changed && game.Controllers.ContainsKey(side) && game.Ready) game.Messages.Add((side, type, o, null, null));
    }

    /// <summary>Full refresh of side s's view (a knowledge refresh copy posts per changed field) (context 3 or 2: only a frozen view changes; a live one mirrors already).</summary>
    /// Characters / special forces: a changed RoleMissionKey copy posts the agent-mission notification (FactKind.AgentMission) (view value to current).
    internal static void Refresh(GameState game, GameObject o, Owner side)
    {
        if (IsLive(o, side)) return;
        var stale = Role(game, o, side);
        game.ViewRefreshed(o, side); // every field the copy changes posts its message
        Snapshot(game, o, side);
        PostRole(game, o, stale);
    }

    /// <summary>The frozen view's RoleMissionKey (a fresh view: none).</summary>
    static Mission? Role(GameState game, GameObject o, Owner side) => game.Views.TryGetValue((o, side), out var v) ? v.RoleMission : null;

    /// <summary>Role chain hook: the copy changed the view's RoleMissionKey and posts the agent-mission notification (FactKind.AgentMission).</summary>
    static void PostRole(GameState game, GameObject o, Mission? stale)
    {
        if (o is Agent a && stale != a.Mission) Missions.PostRole(game, a, stale, a.Mission);
    }

    /// <summary>
    /// Final move of a destroyed object into staging: the parent-change hook runs the staging pass on the side-1 view, then
    /// side 2 (<see cref="Pass"/>), before the parent-change notification (FactKind.ParentChanged). The parent copy ran the same passes already (<see cref="ParentCopy"/>); this
    /// repeat reaches only views that got no pass there (a frozen view already in staging or inside a dropped view).
    /// </summary>
    internal static void AtStaging(GameState game, GameObject o)
    {
        foreach (var side in Sides) Pass(game, o, side);
    }

    /// <summary>
    /// The pass for the processing view of <paramref name="side"/> (<paramref name="pv"/> = its frozen view,
    /// null when live): real key not none: real := none (posts nothing) and copied into the side-1, then side-2 view (frozen ones
    /// included), each changed copy posts a notification when its side's flag is set (the processing side's = <paramref name="flag"/>, the other
    /// 1); live views post once together (the sim's setter posts one notification per real change). Then the processing view is copied
    /// again with both flags set: a live view mirrors (nothing); a frozen one still holding a mission while the real key was
    /// already none takes none and posts a notification. A frozen view that is not processed keeps its mission.
    /// </summary>
    static void RoleClear(GameState game, Agent a, Owner side, bool flag, View? pv)
    {
        if (a.Mission is not { } real)
        {
            if (pv is { RoleMission: { } h }) { pv.RoleMission = null; Missions.PostRole(game, a, h, null); }
            return;
        }
        a.Mission = null;
        bool live = false; // live views post through the real value once, as the setter (Missions.SetMission)
        foreach (var w in Sides)
        {
            Mission? held = live ? null : real;
            if (IsLive(a, w)) live = live || w != side || flag;
            else
            {
                if (!game.Views.TryGetValue((a, w), out var v)) continue; // fresh view: none already
                (held, v.RoleMission) = (v.RoleMission, null);
            }
            if (held is { } x && (w != side || flag)) Missions.PostRole(game, a, x, null);
        }
    }

    /// <summary>
    /// What side s sees of o. Location through the view's parents; a system or abode is its own location. A never-refreshed
    /// (fresh) view: owner 3, no flags set (not Completed), systems not Populated, support 0.
    /// </summary>
    public static Seen See(GameState game, GameObject o, Owner side)
    {
        if (side is not (Owner.Alliance or Owner.Empire) || IsLive(o, side))
        {
            var sys = o as StarSystem;
            return new(o.Owner, o.Destroyed, o.Enroute, Missions.Location(o), sys?.Uprising ?? false, sys?.Populated ?? false,
                (o as Character)?.Captured ?? false, sys?.Support ?? 0, o.Completed, o.BuilderKey, sys?.Explored ?? false,
                o.EnrouteActive, sys?.Blockade ?? false, sys?.Battle ?? false);
        }
        game.Views.TryGetValue((o, side), out var v);
        GameObject? location = o is StarSystem || IsAbode(o) ? o
            : v?.Parent is not { } p ? null
            : p is StarSystem || IsAbode(p) ? p : p.Container is null && p.System is { } managed ? managed : See(game, p, side).Location;
        return new(v?.Owner ?? Owner.Neutral, v?.Destroyed ?? false, v?.Enroute ?? false, location, v?.Uprising ?? false, v?.Populated ?? false,
            v?.Captured ?? false, v?.Support ?? 0, v?.Completed ?? false, v?.BuilderKey, v?.Explored ?? false,
            v?.EnrouteActive ?? false, v?.Blockade ?? false, v?.Battle ?? false);
    }

    /// <summary>
    /// Damaged and the speed of side s's view of a capital ship: the real values while live, else the frozen view's (a fresh view:
    /// not Damaged).
    /// </summary>
    public static (bool Damaged, int Speed) Damage(GameState game, GameObject o, Owner side)
    {
        if (IsLive(o, side)) return (o.Damaged, Orders.Speed(game, o));
        return game.Views.TryGetValue((o, side), out var v) ? (v.Damaged, v.Speed) : (false, 0);
    }

    /// <summary>Location of side s's view of o; null = unknown.</summary>
    public static GameObject? Location(GameState game, GameObject o, Owner side) => See(game, o, side).Location;

    /// <summary>
    /// Command target check (class callback 0) on side s's view: location = its BuilderKey's location if the view
    /// is not Completed, else its own location; none gives Refusal.NoTargetLocation, Refusal.TargetLocationDestroyed for a Destroyed view. Null = valid.
    /// </summary>
    public static OrderResult? TargetCheck(GameState game, GameObject o, Owner side)
    {
        var v = See(game, o, side);
        var location = v.Completed ? v.Location : v.BuilderKey is { } b ? Missions.Location(b) : null;
        return location is not null ? null : v.Destroyed ? Refusal.TargetLocationDestroyed : Refusal.NoTargetLocation;
    }

    // ---- ObservedBy ----

    /// <summary>Classes that inherit ObservedBy: fleets, troops, ships, fighters, facilities (families [8, 48)).</summary>
    static bool Inherits(GameObject o) => Family.Of(o.Type) is >= Families.Fleet and < Families.MajorCharacter;

    /// <summary>
    /// ObservedBy recompute of a content: the parent's ObservedBy if the parent is set, the object is not
    /// EnrouteActive and its class inherits, else 0; systems: <see cref="RecomputeSystem"/>.
    /// </summary>
    internal static void RecomputeObserved(GameState game, GameObject o)
    {
        if (!game.Ready) return; // setup bypasses the setters; step 24 initialises the live states
        if (o is StarSystem || !Inherits(o)) return;
        Set(game, o, o.Container is { } p && !o.EnrouteActive ? p.Observed : SideSet.None);
    }

    /// <summary>System ObservedBy: side s owns it or a Usable fleet of s is directly in it. Run by the owner hook (after game-ready) and on a fleet's Usable change there.</summary>
    internal static void RecomputeSystem(GameState game, StarSystem s)
    {
        if (!game.Ready) return; // setup bypasses the setters; step 24 initialises the live states
        var observed = SideSet.None;
        foreach (var side in Sides)
            if (s.Owner == side || s.Contents.Any(f => f is Fleet && f.Usable && f.Owner == side)) observed = observed.With(side, true);
        Set(game, s, observed);
    }

    /// <summary>ObservedBy setter (on change): live recompute, then every content's recompute (mode 4, recursive).</summary>
    static void Set(GameState game, GameObject o, SideSet observed)
    {
        if (o.Observed == observed) return;
        o.Observed = observed;
        RecomputeLive(game, o);
        foreach (var x in o.Contents.ToList()) RecomputeObserved(game, x);
    }

    // ---- knowledge updates ----

    static bool In(GameObject o, uint lo, uint hi) => Family.Of(o.Type) >= lo && Family.Of(o.Type) < hi;

    /// <summary>
    /// System knowledge update (mode, side), side 1 / 2 only, frozen views written. System parts per mode (base:
    /// owner, Populated; A: support; B: Uprising; full: everything kept) and contents (system ranges L1, fleets' contents L2,
    /// their ships' contents L3; full refreshes); mode 6 also the commanders (characters with CommandKind != 0) directly in the
    /// system and aboard every capital ship of its fleets.
    /// </summary>
    public static void Update(GameState game, StarSystem s, int mode, Owner side)
    {
        if (side is not (Owner.Alliance or Owner.Empire) || mode is < 1 or > 12) return;
        bool full = mode is 8 or 10 or 11;
        if (full) Refresh(game, s, side);
        else if (!IsLive(s, side))
        {
            if (!game.Views.TryGetValue((s, side), out var v)) game.Views[(s, side)] = v = new View();
            // each changed field posts its message
            Changed(game, side, ViewMessageType.Owner, s, v.Owner != s.Owner);
            Changed(game, side, ViewMessageType.Populated, s, v.Populated != s.Populated);
            Changed(game, side, ViewMessageType.Explored, s, v.Explored != s.Explored);
            (v.Owner, v.Populated, v.Explored) = (s.Owner, s.Populated, s.Explored); // base part: Explored, Populated, owner
            v.Updated = game.Day;
            if (mode is 1 or 6) { Changed(game, side, ViewMessageType.Support, s, v.Support != s.Support); (v.Support, v.Control) = (s.Support, s.Control); } // part A: support, control word
            if (mode == 6) { Changed(game, side, ViewMessageType.Uprising, s, v.Uprising != s.Uprising); v.Uprising = s.Uprising; }
        }
        if (mode == 6)
            foreach (var container in new GameObject[] { s }.Concat(s.Contents.OfType<Fleet>().SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type)))).ToList())
            {
                // refresh the Existing commanders directly in it; drop view commanders no longer there (real CommandKind, Inferred)
                foreach (var c in container.Contents.OfType<Character>().Where(c => Family.Of(c.Type) < Families.SpecialForce && c.Existing && c.CommandKind != 0).ToList())
                    Refresh(game, c, side);
                foreach (var ((x, _), v) in game.Views.Where(e => e.Key.Item2 == side && e.Value.Parent == container && !IsLive(e.Key.Item1, side)).OrderBy(e => e.Key.Item1.Key).ToList())
                    if (x is Character { CommandKind: not 0 } && x.Container != container) { Drop(game, x, side, v); Pass(game, x, side); }
            }
        Func<GameObject, bool> l1 = mode switch
        {
            3 => o => In(o, Families.Troop, Families.Headquarters) || In(o, Families.IonCannon, Families.Shipyard),
            4 => o => In(o, Families.ConstructionManager, Families.ManagerEnd) || In(o, Families.Shipyard, Families.MajorCharacter),
            5 or 6 or 7 => o => o is Fleet,
            8 => o => In(o, Families.Headquarters, Families.MajorCharacter) || In(o, Families.Troop, Families.Headquarters) || In(o, Families.ConstructionManager, Families.ManagerEnd) || o is Fleet,
            9 => o => In(o, Families.MajorCharacter, Families.Mission),
            10 => _ => true,
            11 => o => In(o, Families.Troop, Families.Mission) || In(o, Families.ConstructionManager, Families.ManagerEnd),
            12 => o => In(o, Families.Headquarters, Families.MajorCharacter),
            _ => _ => false,
        };
        bool l2 = mode is >= 5 and <= 8 or 10;
        Func<GameObject, bool>? l3 = mode switch { 7 or 10 => _ => true, 8 => o => In(o, Families.Troop, Families.Headquarters), _ => null };
        List(s, l1);
        // managers are not contents here: the ranges holding [160, 176) fully refresh their frozen views (the manager refresh with flag 1:
        // TargetKey and queue count among them)
        if (mode is 4 or 8 or 10 or 11) foreach (var m in s.Managers.OfType<ManufacturingManager>()) Refresh(game, m, side);
        if (!l2) return;
        foreach (var f in s.Contents.OfType<Fleet>().ToList()) // Inferred: the real fleets (the original walks the view's)
        {
            List(f, _ => true);
            if (l3 is null) continue;
            foreach (var ship in f.Contents.Where(x => Family.IsCapitalShip(x.Type)).ToList()) List(ship, l3);
        }

        // refresh the real contents in range, then drop (+ staging pass) the side's content views in range whose real
        // object is no longer in the container, ascending key
        void List(GameObject container, Func<GameObject, bool> range)
        {
            foreach (var o in container.Contents.Where(range).ToList()) Refresh(game, o, side);
            foreach (var x in ContentViews(game, container, side).Where(x => range(x) && x.Container != container))
            {
                Drop(game, x, side, game.Views[(x, side)]);
                Pass(game, x, side);
            }
        }
    }

    /// <summary>Manager refresh (full and position): a frozen view takes the TargetKey (live views mirror it).</summary>
    static void CopyTargetKey(ManufacturingManager m, Owner side)
    {
        if (!IsLive(m, side)) m.ViewTargetKey[(int)side] = m.TargetKey;
    }

    /// <summary>Side s's view of a manager's TargetKey: the real one while live, else the frozen view's (null: never refreshed).</summary>
    public static GameObject? TargetKey(ManufacturingManager m, Owner side) => IsLive(m, side) ? m.TargetKey : m.ViewTargetKey[(int)side];

    /// <summary>
    /// Side s's view of a manager's Reserved: the real one while live. shortcut: a frozen view reads false (the copy
    /// into frozen views is not kept: no reader of a foreign manager's Reserved); keep a per-side copy when one appears.
    /// </summary>
    public static bool Reserved(ManufacturingManager m, Owner side) => IsLive(m, side) && m.Reserved;

    /// <summary>Side s's view of a manager's queue count: the real one while live, else the frozen view's (fresh view: 0).</summary>
    public static int QueueCount(GameState game, ManufacturingManager m, Owner side) =>
        IsLive(m, side) ? m.Orders.Count : game.Views.TryGetValue((m, side), out var v) ? v.QueueCount : 0;

    /// <summary>
    /// Manager owner change, steps 1-2 (the manager's owner already the
    /// new one (it follows its system; destroyed: 3): 1. old side o in {1, 2}: its still-live view takes the new owner and posts
    /// a ViewMessageType.Owner post to o, then freezes with the values it has now (queue count and TargetKey before the cancel); 2. new side n in {1, 2}:
    /// frozen to live full refresh, posting per differing field (Parent, Owner, Usable, Created, Completed, QueueCount; the other manager posts and the
    /// DeploymentKey post are not kept: no reader). The owner-change post is the caller's.
    /// </summary>
    internal static void ManagerOwnerChanged(GameState game, ManufacturingManager m, Owner old)
    {
        if (!game.Ready) return;
        if (old is Owner.Alliance or Owner.Empire)
        {
            Changed(game, old, ViewMessageType.Owner, m, true);
            Snapshot(game, m, old, fromLive: true);
        }
        if (m.Owner is Owner.Alliance or Owner.Empire) game.ViewRefreshed(m, m.Owner);
    }

    /// <summary>Side s's view of a system's control: real while live, else the frozen view's (a fresh view: no control).</summary>
    public static SystemControl Control(GameState game, StarSystem s, Owner side) =>
        IsLive(s, side) ? s.Control : game.Views.TryGetValue((s, side), out var v) ? v.Control : default;

    /// <summary>Update at an object's location if it is a system (duel pairing / final battle: mode 6).</summary>
    internal static void UpdateAt(GameState game, GameObject o, int mode, params Owner[] sides)
    {
        if (o.System is not { } s) return;
        foreach (var side in sides) Update(game, s, mode, side);
    }

    /// <summary>
    /// Update for an object (object, side): with a location and a parent: mode 5 on the location only if it is a system and the direct
    /// parent is not (aboard something); then always a full refresh of the object's own view for the side.
    /// </summary>
    internal static void Reveal(GameState game, GameObject o, Owner side)
    {
        if (o.Container is not { } parent || Missions.Location(o) is not { } location) return;
        if (location is StarSystem s && parent is not StarSystem) Update(game, s, 5, side);
        Refresh(game, o, side);
    }

    /// <summary>Core-system owner-change hook: modes 12 and 4 for both sides.</summary>
    internal static void CoreOwnerFollowUp(GameState game, StarSystem s)
    {
        foreach (var side in Sides) { Update(game, s, 12, side); Update(game, s, 4, side); }
    }

    /// <summary>
    /// Character / special-force owner-change hook: old owner 1 or 2 to full refresh of that side's view; a
    /// stale RoleMissionKey in it is copied and posts the agent-mission notification (FactKind.AgentMission) (view value to current, its lock handler included).
    /// Managers: the same hook after the cancelled units' notifications, so the old owner's frozen view ends with the queue count
    /// and TargetKey of that moment. Other classes post no owner-change notification here.
    /// </summary>
    internal static void OwnerFollowUp(GameState game, GameObject o, Owner old)
    {
        if (old is Owner.Alliance or Owner.Empire) Refresh(game, o, old);
    }

    /// <summary>
    /// Mission finalize part (classes [81, 116) except 88, at phase 10 and on entering 11), only if LastPhase >= 5
    /// or TargetLocation = Origin: a system target location to mode 6 for the owner; then the owner's view of a non-system,
    /// non-abode ObjectiveKey seen at the target location gets a full refresh.
    /// </summary>
    internal static void Finalize(GameState game, Mission m)
    {
        if (m.Kind is < MissionKind.Diplomacy or > MissionKind.Pickup or MissionKind.JediTraining || !(m.LastPhase >= 5 || m.TargetLocation == m.Origin)) return;
        if (m.TargetLocation is StarSystem s) Update(game, s, 6, m.Owner);
        if (m.Objective is { } o && o is not StarSystem && !IsAbode(o) && m.TargetLocation is { } t && See(game, o, m.Owner).Location == t)
            Refresh(game, o, m.Owner);
    }

    /// <summary>
    /// Setup step 24 (sides Inferred both): frozen views only. Every system: the "position" refresh (context A,
    /// systems: parent and Usable / Created / Completed / Existing; owner 3, not Populated stay), so every system view
    /// is a valid mission target; core systems [144, 146) and their facilities [32, 48): full refresh; Yavin: mode 10 for
    /// side 2. Manager refreshes keep only the TargetKey of every frozen manager view (Inferred: every system's managers); sector
    /// refreshes are not kept (a sector is not an object here).
    /// </summary>
    internal static void Setup(GameState game)
    {
        // live states after setup: every view live for its owner's side only (system ObservedBy is computed only after
        // game-ready, so ObservedBy is clear for every side)
        foreach (var o in game.Systems.Concat(game.Created).Concat(game.Characters)) o.LiveSides = SideSet.Only(o.Owner);
        // manager refresh hooks: ObservedBy forced for both sides; live for both
        // (Inferred: Completed and not Enroute, consistent with the run-3 acknowledgement), so a refreshed mission view is dropped
        // at the final move.
        game.MissionManager.Observed = SideSet.Both;
        game.MissionManager.LiveSides = SideSet.Both;
        foreach (var s in game.Systems)
            foreach (var side in Sides.Where(side => !IsLive(s, side)))
            {
                if (!game.Views.TryGetValue((s, side), out var v)) game.Views[(s, side)] = v = new View { Owner = Owner.Neutral };
                (v.Completed, v.Created) = (s.Completed, s.Created);
                v.Usable = s.Usable;
            }
        foreach (var m in game.Systems.SelectMany(s => s.Managers).OfType<ManufacturingManager>())
            foreach (var side in Sides) CopyTargetKey(m, side);
        foreach (var s in game.Systems.Where(s => Family.Of(s.Type) is >= Families.CoreSystem and < Families.RimSystem))
            foreach (var side in Sides)
            {
                Refresh(game, s, side);
                foreach (var f in s.Contents.Where(x => In(x, Families.Headquarters, Families.MajorCharacter)).ToList()) Refresh(game, f, side);
            }
        Update(game, game.Known.Yavin!, 10, Owner.Empire);
    }
}
