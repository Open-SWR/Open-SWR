using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

public static partial class Missions
{
    // ---- encounters ----

    /// <summary>Encounter resolver state: location, pool flags, the foiled flag and the counts.</summary>
    sealed class Resolver(StarSystem location)
    {
        public StarSystem X { get; } = location;
        // AgentPass and TravelPass have no sim reader. Meaning in the original is unknown.
        public bool TravelPass, AgentPass, SystemUnits, FleetUnits, WalkShips, WalkFighters, WalkTroops, Foiled;
        public int Agents, Decoys, Units;
    }

    /// <summary>
    /// Encounter resolver, run on every phase change: if CompletionStatus = 0, the location (target once past
    /// LastPhase 4, else origin) is a system and the owner is 1 or 2: pass 1, then pass 0;
    /// each pass runs the case table and, unless done, the stages. Inferred: one resolver per phase change, its pool flags kept
    /// across both passes.
    /// </summary>
    static void Encounters(GameState game, Mission m)
    {
        if (m.CompletionStatus != MissionCompletion.None || (m.LastPhase > 4 ? m.TargetLocation : m.Origin) is not StarSystem x || m.Owner is not (Owner.Alliance or Owner.Empire)) return;
        var r = new Resolver(x);
        foreach (bool first in new[] { true, false })
            if (!CaseTable(game, m, r, first)) Stages(game, m, r);
        // the resolver ended the mission (foil or the case table's phase check): full refresh of the opposing side's view (no
        // draw); that side acknowledges it at the final move. Not kept: the view's TaskStatus / CompletionStatus
        // and their TaskStatus / CompletionStatus view fields, no-ops for every controller on a foreign mission.
        if (m.CompletionStatus != MissionCompletion.None) Knowledge.Refresh(game, m, Combat.Opp(m.Owner));
    }

    /// <summary>The phase's encounter code: 0 while a member is HyperdriveActive (AllMembersArrived in the block).</summary>
    static int EncounterCode(Mission m)
    {
        if (m.Members.Any(a => a.HyperdriveActive)) return 0;
        var e = m.Encounters;
        return m.Phase switch { 2 => e.Phase2, 3 => e.Phase3, 7 => e.Phase7, 9 => e.Phase9, _ => 0 };
    }

    /// <summary>Member filter: no remove / resign request, a parent; with skip also IsDecoying clear.</summary>
    static bool Valid(Agent a, bool skip) => !a.Removed && a.Container is not null && !(skip && a.IsDecoying);

    static IEnumerable<Agent> Of(IEnumerable<Agent> list, uint lo, uint hi, bool skip) =>
        list.Where(a => Family.Of(a.Type) >= lo && Family.Of(a.Type) < hi && Valid(a, skip));

    /// <summary>Agents: Team characters and special forces [48, 64), skipping IsDecoying.</summary>
    static List<Agent> AgentWalk(Mission m) => Of(m.Team, Families.MajorCharacter, Families.Mission, true).ToList();

    /// <summary>Decoys: Decoy characters and special forces [48, 64), no skip.</summary>
    static List<Agent> DecoyList(Mission m) => Of(m.Decoys, Families.MajorCharacter, Families.Mission, false).ToList();

    /// <summary>Foiled-member walk: Team characters only, majors [48, 56) then minors [56, 60), skipping IsDecoying.</summary>
    static List<Agent> FoiledWalk(Mission m) => [.. Of(m.Team, Families.MajorCharacter, Families.MinorCharacter, true), .. Of(m.Team, Families.MinorCharacter, Families.SpecialForce, true)];

    /// <summary>Team special forces: [60, 64), skipping IsDecoying.</summary>
    static List<Agent> ForcesWalk(Mission m) => Of(m.Team, Families.SpecialForce, Families.Mission, true).ToList();

    /// <summary>
    /// Case table: phase check; done if ending, no encounters in the MISSNSD record or encounter code 0. F = opp(owner) has a Usable fleet
    /// at X; C = the owner has a Usable ion cannon (family 34) at X; O = X is owned by opp(owner); T = ObjectiveKey is itself a
    /// system or sits directly in one (its real container). A = AgentPass, TravelPass if the target location differs from the origin, and with F and not C
    /// FleetUnits, WalkShips, WalkFighters. Then, also when done: every unit of the pool walk gets DetectorIsDecoyed := 0 and the counts are refreshed:
    /// units of the walk with skip, agents, decoys; every member of the decoy walk gets IsDecoying := 0. Returns done.
    /// </summary>
    static bool CaseTable(GameState game, Mission m, Resolver r, bool first)
    {
        PhaseCheck(game, m);
        int code = EncounterCode(m);
        bool done = m.CompletionStatus != MissionCompletion.None || !m.Record.HasEncounters || code == 0;
        if (!done)
        {
            var opp = Systems.Opp(m.Owner);
            bool fleet = r.X.Contents.Any(o => o is Fleet && o.Owner == opp && o.Usable);
            bool cannon = r.X.Contents.Any(o => Family.Of(o.Type) == Families.IonCannon && o.Owner == m.Owner && o.Usable);
            bool hostile = r.X.Owner == opp, direct = m.Objective is StarSystem || m.Objective?.Container is StarSystem;
            void Agents() { r.AgentPass = true; if (m.TargetLocation != m.Origin) r.TravelPass = true; if (fleet && !cannon) r.FleetUnits = r.WalkShips = r.WalkFighters = true; }
            void Garrison() { r.TravelPass = true; if (hostile) r.SystemUnits = r.WalkTroops = true; }
            switch (code, first)
            {
                case (1, true) or (3, true): Agents(); break;
                case (2, true) or (3, false): Garrison(); break;
                case (4, true):
                    r.AgentPass = r.TravelPass = true;
                    if (direct) { if (hostile) r.SystemUnits = r.WalkTroops = true; } // T chooses the branch, O inside it
                    else if (fleet) r.FleetUnits = r.WalkShips = r.WalkFighters = true;
                    break;
                default: done = true; break;
            }
        }
        foreach (var (u, _) in Walk(r, true, true, true, skip: false).ToList()) u.DetectorIsDecoyed = false;
        r.Units = Walk(r, true, true, true, skip: true).Count();
        (r.Agents, r.Decoys) = (AgentWalk(m).Count, DecoyList(m).Count);
        foreach (var d in DecoyList(m)) d.IsDecoying = false;
        return done;
    }

    /// <summary>
    /// Unit (detector) walk, any owner, every iterator Usable (mode 1): with SystemUnits the fighters (WalkFighters and b), then troops
    /// (WalkTroops and c) directly in X; with FleetUnits for every Usable fleet in X and every Usable capital ship of it (key order): the ship
    /// (WalkShips and a), its fighters (WalkFighters and b), its troops (WalkTroops and c). Skip = leave out DetectorIsDecoyed units. Fleet = the fleet the
    /// unit was reached through (null: directly in X).
    /// </summary>
    static IEnumerable<(GameObject Unit, Fleet? Fleet)> Walk(Resolver r, bool ships, bool fighters, bool troops, bool skip)
    {
        static bool Fighter(GameObject o) => Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters && o.Usable;
        static bool Troop(GameObject o) => Family.IsTroop(o.Type) && o.Usable;
        bool Keep(GameObject u) => !(skip && u.DetectorIsDecoyed);
        if (r.SystemUnits)
        {
            if (r.WalkFighters && fighters) foreach (var u in r.X.Contents.Where(Fighter).ToList()) if (Keep(u)) yield return (u, null);
            if (r.WalkTroops && troops) foreach (var u in r.X.Contents.Where(Troop).ToList()) if (Keep(u)) yield return (u, null);
        }
        if (!r.FleetUnits) yield break;
        foreach (var fleet in r.X.Contents.OfType<Fleet>().Where(f => f.Usable).ToList())
            foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).ToList())
            {
                if (r.WalkShips && ships && Keep(ship)) yield return (ship, fleet);
                if (r.WalkFighters && fighters) foreach (var u in ship.Contents.Where(Fighter).ToList()) if (Keep(u)) yield return (u, fleet);
                if (r.WalkTroops && troops) foreach (var u in ship.Contents.Where(Troop).ToList()) if (Keep(u)) yield return (u, fleet);
            }
    }

    /// <summary>A detecting unit's detection value (the name Detection is Inferred).</summary>
    static int Detection(GameState game, GameObject unit) => game.Definitions.Resolve(unit.Type)!.Get(SdField.Detection);

    /// <summary>
    /// Defender of a detecting unit: kind = 2 capital ship, 1 fighter, 3 troop; reached through
    /// a fleet: the first character aboard the fleet's capital ships (key order) of the fleet's owner, Existing, with CommandKind =
    /// kind; else the first character directly in X of X's owner, Existing, with CommandKind = kind.
    /// </summary>
    static Character? Defender(Resolver r, GameObject unit, Fleet? fleet) =>
        Combat.Commander(fleet, r.X, Family.IsCapitalShip(unit.Type) ? 2 : Family.IsTroop(unit.Type) ? 3 : 1);

    /// <summary>The defender's enhanced skill (espionage or combat); 0 without defender.</summary>
    static int DefenderSkill(GameState game, Resolver r, GameObject unit, Fleet? fleet, int skill) =>
        Defender(r, unit, fleet) is { } d ? Enhanced(game, d, skill) : 0;

    /// <summary>
    /// Stages: 1 Force detection; 2 loyalty (standard classes in phase 9, movement classes never; every member draws,
    /// a betrayal foils the mission, then chance(Force) per other Team character); 3 decoy (with SystemUnits walk all, with FleetUnits ships only then
    /// fighters only; at its start the Force duels); 4 foil (agents &gt; 0: the average EnhancedEspionage over the agent walk, computed at the first unit, one
    /// check per unit of walk all until one foils); 5 foiled: ResignOnInjury gives CompletionStatus 4 past phase 4,
    /// else 3; then (also without it) every member of the foiled-member walk is processed, then every Team special force
    /// (the foil's a5 is their count).
    /// </summary>
    static void Stages(GameState game, Mission m, Resolver r)
    {
        ForceDetection(game, m, r);
        if (m.Kind is not (>= MissionKind.Move and <= MissionKind.Adrift) && m.Phase == 9 && !r.Foiled && Betrayal(game, m) is not null) r.Foiled = true;
        // decoy stage start, before its foiled test: Force duels; a visited member with a remove / resign request afterwards lowers
        // the decoy (Decoy member) or agent count, whether or not the duel ran
        foreach (var a in FoiledWalk(m).OfType<Character>().ToList())
        {
            if (a.EncounterKey is not { Container: not null } b) continue;
            Jedi.Duel(game, a, b);
            if (a.Removed) { if (m.Decoys.Contains(a)) r.Decoys--; else r.Agents--; }
        }
        if (!r.Foiled)
        {
            if (r.SystemUnits) DecoyWalk(game, m, r, Walk(r, true, true, true, skip: true));
            if (r.FleetUnits)
            {
                DecoyWalk(game, m, r, Walk(r, true, false, false, skip: true));
                DecoyWalk(game, m, r, Walk(r, false, true, false, skip: true));
            }
        }
        if (!r.Foiled && r.Agents > 0)
        {
            int? average = null;
            int forces = 0;
            foreach (var (u, fleet) in Walk(r, true, true, true, skip: true))
            {
                if (average is null)
                {
                    var agents = AgentWalk(m);
                    average = agents.Count == 0 ? 0 : agents.Sum(a => Enhanced(game, a, SkillIndex.Espionage)) / agents.Count;
                    forces = ForcesWalk(m).Count; // a5: count of the forces walk
                }
                int defender = DefenderSkill(game, r, u, fleet, SkillIndex.Espionage);
                if (!game.Random.Chance(FoilChance(game, average.Value, defender, Detection(game, u), forces), DrawSite.EncounterCheck)) continue;
                r.Foiled = true;
                break;
            }
        }
        if (!r.Foiled) return;
        if (m.Record.ResignOnInjury) SetCompletion(game, m, m.Phase > 4 ? MissionCompletion.Foiled : MissionCompletion.FoiledEnRoute);
        foreach (var a in FoiledWalk(m)) FoiledMember(game, m, r, a);
        foreach (var a in ForcesWalk(m)) FoiledMember(game, m, r, a); // foiled-member step for each Team special force
    }

    /// <summary>
    /// Decoy stage callback per visited unit: no decoy gives stop; else pick roll(decoys - 1) and check
    /// chance(T(FDECOYTB through a fleet / TDECOYTB, EnhancedEspionage(decoy) - Detection(unit) - pct(defender's EnhancedEspionage,
    /// G DecoyKeyPercent))): success gives the decoy IsDecoying, the unit DetectorIsDecoyed; failure gives the decoy is foiled like an agent with the
    /// visiting unit (no unit pick).
    /// </summary>
    static void DecoyWalk(GameState game, Mission m, Resolver r, IEnumerable<(GameObject Unit, Fleet? Fleet)> walk)
    {
        foreach (var (u, fleet) in walk)
        {
            if (r.Decoys == 0) return;
            int i = game.Random.Roll(r.Decoys - 1, DrawSite.DecoyPick);
            if (DecoyList(m).ElementAtOrDefault(i) is not { } d) return;
            int defender = DefenderSkill(game, r, u, fleet, SkillIndex.Espionage);
            if (game.Random.Chance(DecoyChance(game, Enhanced(game, d, SkillIndex.Espionage), defender, Detection(game, u), fleet is not null), DrawSite.EncounterCheck))
            {
                (d.IsDecoying, u.DetectorIsDecoyed) = (true, true);
                r.Units--; // a decoyed unit leaves the unit count
            }
            else FoiledMember(game, m, r, d, (u, fleet));
        }
    }

    /// <summary>
    /// Foiled member: a failed decoy uses the visiting unit; else without units (unit count 0) no draw, skipped, and
    /// roll(n - 1) picks one of the n units of walk all (with skip); evasion chance(T(RLEVADTB, EnhancedCombat - v)), v = the
    /// EnhancedCombat of the unit's defender (0 without): success gives the evasion hook (gated resign request, then the injury check for
    /// characters; special forces: no draw); failure gives captured by the defender character, else by the unit
    /// (<see cref="Prisoners.Capture"/>: injury check, then State Captured; special forces DestroyedReason 9). Then (both outcomes) the second resign check, and a removed member lowers the decoy (Decoy member) or agent
    /// count.
    /// </summary>
    static void FoiledMember(GameState game, Mission m, Resolver r, Agent a, (GameObject Unit, Fleet? Fleet)? visiting = null)
    {
        bool decoy = m.Decoys.Contains(a); // membership read before the processing
        if (visiting is null && r.Units == 0) return;
        var (unit, fleet) = visiting ?? Walk(r, true, true, true, skip: true).ElementAtOrDefault(game.Random.Roll(r.Units - 1, DrawSite.UnitPick));
        int v = unit is null ? 0 : DefenderSkill(game, r, unit, fleet, SkillIndex.Combat);
        if (game.Random.Chance(Table(game, "RLEVADTB", Enhanced(game, a, SkillIndex.Combat) - v) ?? 0, DrawSite.EncounterCheck))
            Evaded(game, a);
        else Prisoners.Capture(game, a, unit is null ? null : Defender(r, unit, fleet) ?? unit);
        // Tail of the foiled-member step. On the capture path Inferred whether the hooks already set the remove flag or cleared the ref
        if (!a.RemoveRequest && a.ResignOnInjury && a.Mission is not null) a.ResignRequest = true;
        if (a.Removed) { if (decoy) r.Decoys--; else r.Agents--; }
    }
}
