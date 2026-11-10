using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>Battle record per fighter squadron: F[6] squadrons left (absolute); the carrier when F[5] != 0 (SdRef of F[4]'s C record), else null.</summary>
public readonly record struct FighterResult(int Left, GameObject? Carrier);

/// <summary>Battle record per capital ship [20, 24): trunc(C[4]) hull left, C[5] SpeedCH, C[6] ShieldRechargeRateCH, C[7] hyperdrive hits, C[8] TractorBeamPowerCH, C[9] WeaponRechargeRateCH.</summary>
public readonly record struct ShipResult(int Hull, int Speed, int Shield, int Hyperdrive, int Tractor, int Weapon);

/// <summary>Battle record per Death-Star class ship [24, 28): trunc(D[3]) hull left, D[2] != 0 alive.</summary>
public readonly record struct DeathStarResult(int Hull, bool Alive);

/// <summary>Event-log report of one hosted battle: day, system, BattleWon, the sides that took part, units lost per side, the units at the start (UI and measurement only).</summary>
public sealed record BattleReport(int Day, StarSystem System, Owner Won, Owner[] Sides, int AllianceLost, int EmpireLost, IReadOnlyList<GameObject>? Units = null);

/// <summary>
/// One attacking fleet of a strike at the fleet-flag clear (fleet Bombard and fleet Assault notifications with 0): its live sides (per side,
/// 1 Alliance, 2 Empire), its capital ships and their contents (walk mode 1, Inferred: Existing).
/// </summary>
public sealed record StrikeFleet(Fleet Fleet, SideSet Live, List<GameObject> Ships, List<GameObject> Aboard);

/// <summary>
/// Per-strike record (UI only, not saved): stands in for the system's pulse view fields, fleet Bombard and fleet Assault notifications
/// and the Destroyed view fields of one event run. Defender = the system owner at the start; live sides (Alliance,
/// Empire) of the system at the start and at the end; Contents = the system's direct Usable contents at the end (walk mode 1 =
/// Usable); Destroyed = every object destroyed in the run with the live sides of its views at the destroy
/// (the view Destroyed goes to the views live then) and its Completed flag, plus one entry per side whose once-Created frozen view
/// posts its view Destroyed at the final move's staging pass (same group; <see cref="Combat.StrikeStaged"/>, view's Completed).
/// </summary>
public sealed record StrikeReport(int Day, StarSystem System, bool Assault, Owner Attacker, Owner Defender, SideSet StartLive, SideSet EndLive,
    List<StrikeFleet> Fleets, List<GameObject> Contents, List<(GameObject Unit, SideSet Live, bool Completed)> Destroyed);

/// <summary>
/// The engine's result of one space battle: the automatic flag, the side summary S[0..6] (S[4], S[5]
/// unread) and the per-unit records keyed by unit; from the automatic engine, the tactical battle or a replay. The loss records
/// L1 / L2 are derived by the sim (<see cref="Combat.RecordLoss"/>).
/// </summary>
public sealed record BattleResult(bool Auto, int[] S, IReadOnlyDictionary<GameObject, FighterResult> Fighters,
    IReadOnlyDictionary<GameObject, ShipResult> Ships, IReadOnlyDictionary<GameObject, DeathStarResult> DeathStars);

/// <summary>
/// Planetary assault, bombardment, the space battle flow (start, recorded result, end, post-battle wave), the Blockade hook,
/// timer BlockadeSupport and WithdrawPercent.
/// Not modelled: manager 250's battle hook and its notification (no sim effect).
/// </summary>
public static class Combat
{
    // ---- strike record (UI only, no draw) ----

    internal static SideSet LiveSides(GameObject o) => new(Knowledge.IsLive(o, Owner.Alliance), Knowledge.IsLive(o, Owner.Empire));

    /// <summary>Start of a strike: the pre-run state the record needs (null without <see cref="GameState.StrikeTrace"/>).</summary>
    static Func<StrikeReport>? StrikeStart(GameState game, StarSystem s, bool assault, Owner attacker, List<Fleet> fleets)
    {
        if (game.StrikeTrace is null) return null;
        var (defender, start) = (s.Owner, LiveSides(s));
        var destroyed = game.StrikeDestroys = [];
        return () =>
        {
            game.StrikeDestroys = null;
            // the end pulses: mode-1 walks (Usable content views) of the fleets' capital ships, their
            // contents and the system's direct contents
            return new StrikeReport(game.Day, s, assault, attacker, defender, start, LiveSides(s),
                [.. fleets.Select(f =>
                {
                    var ships = f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).ToList();
                    return new StrikeFleet(f, LiveSides(f), ships, [.. ships.SelectMany(x => x.Contents.Where(y => y.Usable))]);
                })],
                [.. s.Contents.Where(x => x.Usable)], destroyed);
        };
    }

    /// <summary>
    /// Staging-pass view Destroyed of side s's once-Created frozen view of x: kind 4 under the strike's context,
    /// so it joins the strike group and x goes to L80 / L88 by the view's Completed.
    /// shortcut: the strike is matched by day (the latest strike of the day that destroyed x), not by the run's context: a locked
    /// unit whose final move runs in a later run of that day (its lock notification unlocks it) still joins it, where the original opens a group of its
    /// own (and a report already built into messages misses the entry); upgrade with a carried run context when a recording shows
    /// such a unit in a strike group of its own (open question: not settled yet).
    /// </summary>
    internal static void StrikeStaged(GameState game, GameObject x, Owner side, bool completed)
    {
        if (side is not (Owner.Alliance or Owner.Empire)
            || game.StrikeTrace?.LastOrDefault(r => r.Day == game.Day && r.Destroyed.Any(d => d.Unit == x)) is not { } report
            || report.Destroyed.Any(d => d.Unit == x && d.Live[side])) return;
        report.Destroyed.Add((x, SideSet.Only(side), completed));
    }

    static bool In(GameObject o, uint lo, uint hi) => Family.Of(o.Type) >= lo && Family.Of(o.Type) < hi;
    static bool Fighter(GameObject o) => In(o, Families.Fighter, Families.Headquarters);
    static int Sd(GameState game, GameObject o, SdField field) => game.Definitions.Resolve(o.Type)!.Get(field);

    /// <summary>opp(x): 1 gives 2, 2 gives 1, 3 gives 3, 0 gives 1 (x = 3 ? 3 : 2 - (x != 1)).</summary>
    internal static Owner Opp(Owner x) => x == Owner.Neutral ? Owner.Neutral : x == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    /// <summary>
    /// Commander of a unit kind (1 commander, 2 admiral, 3 general; CommandKind): through a fleet the first character aboard its
    /// capital ships (fleet owner, Existing, ships in key order); else the first character directly in the system owned
    /// by the system's owner (Existing, key order).
    /// </summary>
    internal static Character? Commander(Fleet? fleet, StarSystem x, int kind) => (fleet is not null
        ? fleet.Contents.Where(s => Family.IsCapitalShip(s.Type)).SelectMany(s => s.Contents.OfType<Character>()).Where(c => c.Owner == fleet.Owner)
        : x.Contents.OfType<Character>().Where(c => c.Owner == x.Owner)).FirstOrDefault(c => c.Existing && c.CommandKind == kind);

    /// <summary>EnhancedLeadership of a commander; 0 without one.</summary>
    static int Lead(GameState game, Character? c) => c is null ? 0 : Missions.Enhanced(game, c, SkillIndex.Leadership);

    // ---- fleet flags ----

    /// <summary>Fleet Blockade recompute: every Existing fleet in the system gets the system's value if Usable, else false.</summary>
    internal static void SetFleetBlockade(StarSystem s, bool value)
    {
        foreach (var f in s.Contents.OfType<Fleet>().Where(f => f.Existing)) f.Blockade = value && f.Usable;
    }

    /// <summary>Fleet Battle recompute, as <see cref="SetFleetBlockade"/>.</summary>
    internal static void SetFleetBattle(StarSystem s, bool value)
    {
        foreach (var f in s.Contents.OfType<Fleet>().Where(f => f.Existing)) f.Battle = value && f.Usable;
    }

    // ---- validation of the Assault command and the bombard commands ----

    /// <summary>
    /// Assault fleet check (called by the Assault command; its event code has no name), after the generic object check: the Existing capital ships (key order) are
    /// walked until one is Completed and not Enroute; otherwise the last one examined gives (1, 16) not Completed / (1, 17)
    /// Enroute, none at all (8, 1); then the fleet's Blockade flag clear gives (8, 7). A non-fleet target: the fleet resolver returns 0
    /// gives (1, 1) by the return-0 rule.
    /// </summary>
    internal static OrderResult AssaultFleetCheck(GameObject t)
    {
        if (t is not Fleet f) return Refusal.WrongClass;
        var result = ShipCheck(f);
        return !result.Success ? result : f.Blockade ? OrderResult.Ok : Refusal.NoBlockade;
    }

    /// <summary>
    /// Fleet ship check (part of the assault fleet check, bombard fleets without the Blockade test): the Existing capital ships (key order)
    /// until one is Completed and not Enroute; else the last one's (1, 16) / (1, 17), none (8, 1).
    /// </summary>
    internal static OrderResult ShipCheck(GameObject f)
    {
        var result = Refusal.NoShips;
        foreach (var ship in f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing))
        {
            if (ship.Completed && !ship.Enroute) return OrderResult.Ok;
            result = ship.Completed ? Refusal.Enroute : Refusal.NotCompleted;
        }
        return result;
    }

    /// <summary>
    /// Assault pre-check (after the base pre-check): among the targets the fleets that are not Destroyed, Usable and
    /// have the fleet Blockade flag; none gives (8, 7); no Usable troop (Inferred) aboard their Usable capital ships gives (8, 5). The second
    /// list is not part of the command model.
    /// </summary>
    internal static OrderResult AssaultPreCheck(IEnumerable<GameObject> targets)
    {
        var fleets = targets.OfType<Fleet>().Where(f => !f.Destroyed && f.Usable && f.Blockade).ToList();
        if (fleets.Count == 0) return Refusal.NoBlockade;
        return fleets.SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type) && x.Usable)
            .Any(ship => ship.Contents.Any(x => Family.IsTroop(x.Type) && x.Usable)) ? OrderResult.Ok : Refusal.NoTroops;
    }

    /// <summary>
    /// Bombardment command check, after the base validation: the targets in list order are scanned for a Usable fleet
    /// with fleetBombard &gt; 0 (BombardDestroy: also holding a Usable Death-Star class ship [24, 28)); the first one passes. None: (8, 6)
    /// if the last fleet examined had fleetBombard = 0 or none was examined, else (BombardDestroy) (8, 8).
    /// </summary>
    internal static OrderResult BombardCheck(GameState game, Command c, IEnumerable<GameObject> targets)
    {
        var result = Refusal.NoBombardStrength;
        foreach (var f in targets.OfType<Fleet>())
        {
            int b = FleetBombard(game, f);
            if (f.Usable && b > 0 && (c.Type != CommandType.BombardDestroy || f.Contents.Any(x => In(x, Families.DeathStar, Families.Fighter) && x.Usable))) return OrderResult.Ok;
            result = b == 0 || c.Type != CommandType.BombardDestroy ? Refusal.NoBombardStrength : Refusal.NoDeathStar;
        }
        return result;
    }

    /// <summary>
    /// System checks: the ArgRef resolves (1, 18) and is not Destroyed (1, 34); bombardment needs nothing else; assault
    /// also Blockade (144, 6) and fewer than G BombardShieldCount Usable planetary shields (family 36, any owner) (144, 5).
    /// </summary>
    internal static OrderResult SystemCheck(GameState game, Command c)
    {
        if (c.ArgRef is not StarSystem s) return Refusal.Missing;
        if (s.Destroyed) return Refusal.NoDestination;
        if (c.Type != CommandType.Assault) return OrderResult.Ok;
        return !s.Blockade ? Refusal.SystemNotBlockaded
            : s.Contents.Count(x => Family.Of(x.Type) == Families.PlanetaryShield && x.Usable) >= game.Rules.BombardShieldCount ? Refusal.ShieldsBlockBombard : OrderResult.Ok;
    }

    // ---- planetary assault ----

    /// <summary>
    /// Assault resolver event: attacker = owner of the first Usable fleet (key order) with the Assault flag.
    /// Steps: count; defence fire; ground duels; collateral damage; landing of up to 6; clear the fleets' Assault flags. The system
    /// Assault flag is set and cleared within the run: only its assault flag notification (0 gives 1) is posted (Jedi-training abort 10).
    /// </summary>
    public static void Assault(GameState game, StarSystem s)
    {
        if (s.Contents.OfType<Fleet>().FirstOrDefault(f => f.Usable && f.Assault) is not { } first) return;
        Objects.Notify(game, g => Jedi.Abort(g, s, MissionCompletion.SystemAssaulted));
        var att = first.Owner;
        foreach (var human in game.Controllers.Values.OfType<Ai.HumanController>().Where(c => c.Side == att || c.Side == s.Owner))
            human.AssaultAlarms++; // openswr deviation: the invasion alert (Ai.HumanController.AssaultAlarms)
        var record = StrikeStart(game, s, true, att, [.. s.Contents.OfType<Fleet>().Where(f => f.Owner == att && f.Assault)]);
        var g = game.Rules;
        // Usable assaulting fleets of the attacker gives their Usable capital ships gives Usable troops aboard (all side att)
        List<(GameObject Troop, Fleet Fleet)> Attackers() =>
            [.. from f in s.Contents.OfType<Fleet>() where f.Usable && f.Owner == att && f.Assault
                from ship in f.Contents where Family.IsCapitalShip(ship.Type) && ship.Usable && ship.Owner == att
                from t in ship.Contents where Family.IsTroop(t.Type) && t.Usable && t.Owner == att
                select (t, f)];
        // Usable troops directly in the system owned by the system's owner
        List<GameObject> Defenders() => [.. s.Contents.Where(t => Family.IsTroop(t.Type) && t.Usable && t.Owner == s.Owner)];
        int a0 = Attackers().Count, d0 = Defenders().Count, a = a0, d = d0, e = 0;

        // 2. defence fire: every Usable defence facility [34, 40), any owner, key order
        foreach (var f in s.Contents.Where(x => In(x, Families.IonCannon, Families.Shipyard) && x.Usable).ToList())
        {
            if (!game.Random.Chance(Sd(game, f, SdField.DefenseAttackStrength) / g.AssaultFireDivisor, DrawSite.AssaultFire)) continue;
            int i = game.Random.Roll(a0 - 1, DrawSite.AssaultPick);
            var walk = Attackers();
            if (i < 0 || i >= walk.Count) continue;
            Objects.Destroy(game, walk[i].Troop, DestroyedReason.Assault);
            if (--a == 0) break;
        }

        // 3. ground combat: one pass, each attacker troop duels at most once
        if (a > 0 && d > 0)
            foreach (var (t, f) in Attackers())
            {
                int r = game.Random.Roll(d0 - 1, DrawSite.DuelPick);
                if (r >= d) continue;
                var opponent = Defenders()[r];
                e++;
                int v = Duel(game, game.Random.Roll(g.DuelRoll, DrawSite.Duel), Sd(game, t, SdField.TroopAttackStrength), Lead(game, Commander(f, s, 3)),
                    Sd(game, opponent, SdField.TroopDefenseStrength), Lead(game, Commander(null, s, 3)));
                if (v > 0) { Objects.Destroy(game, opponent, DestroyedReason.Assault); d--; }
                else if (v < 0) { Objects.Destroy(game, t, DestroyedReason.Assault); a--; }
                if (a == 0 || d == 0) break;
            }

        // 4. collateral damage
        int n = 0;
        for (int i = 0; i < e; i++) if (game.Random.Chance(g.CollateralDamageChance, DrawSite.Collateral)) n++;
        for (; n > 0; n--) if (!Collateral(game, s)) break;

        // 5. landing: the first L troops of the attacker walk, L = ceildiv(G GarrisonSupportThreshold - 0, -G GarrisonSupportPerTroop)
        if (a > 0 && d == 0)
            foreach (var (t, _) in Attackers().Take(CeilDiv(g.GarrisonSupportThreshold, -g.GarrisonSupportPerTroop))) Objects.SetParent(game, t, s);

        // 6. clear: every fleet of the attacker in the system (mode 4)
        foreach (var f in s.Contents.OfType<Fleet>().Where(f => f.Owner == att)) f.Assault = false;
        if (record is not null) game.StrikeTrace!.Add(record());
    }

    /// <summary>
    /// Duel for the drawn r = roll(G DuelRoll): v = r + (Attack + leadA div G DuelLeadershipDivisor) - (Defense + leadD div G DuelLeadershipDivisor);
    /// returns 1 if v >= G DuelDefenderLoss (the defender dies), -1 if v <= G DuelAttackerLoss (the attacker dies), else 0. Shipped G: 10 / 30 / 6 / 4.
    /// </summary>
    public static int Duel(GameState game, int r, int attack, int leadAttacker, int defense, int leadDefender)
    {
        var g = game.Rules;
        int v = r + (attack + leadAttacker / g.DuelLeadershipDivisor) - (defense + leadDefender / g.DuelLeadershipDivisor);
        return v >= g.DuelDefenderLoss ? 1 : v <= g.DuelAttackerLoss ? -1 : 0;
    }

    /// <summary>
    /// One collateral pick: F = Usable facilities [32, 48), H = Usable headquarters [32, 34) (any owner);
    /// t = (F - H) + (Energy != 0) + (RawMaterial != 0); t = 0 gives false (stop); r = roll(t - 1): r &lt; F - H gives the r-th facility
    /// (key order, first headquarters skipped) gets DestroyedReason Assault; r = F - H gives Energy - 1 if != 0, else RawMaterial - 1;
    /// r &gt; F - H gives RawMaterial - 1 (setters with their hooks).
    /// </summary>
    public static bool Collateral(GameState game, StarSystem s)
    {
        var facilities = s.Contents.Where(x => Family.IsFacility(x.Type) && x.Usable).ToList();
        int fh = facilities.Count - facilities.Count(x => In(x, Families.Headquarters, Families.IonCannon));
        int t = fh + (s.Energy != 0 ? 1 : 0) + (s.RawMaterials != 0 ? 1 : 0);
        if (t == 0) return false;
        int r = game.Random.Roll(t - 1, DrawSite.CollateralPick);
        if (r < fh)
        {
            if (facilities.FirstOrDefault(x => In(x, Families.Headquarters, Families.IonCannon)) is { } hq) facilities.Remove(hq);
            Objects.Destroy(game, facilities[r], DestroyedReason.Assault);
        }
        else if (r == fh && s.Energy != 0) Systems.SetEnergy(game, s, s.Energy - 1);
        else Systems.SetRawMaterials(game, s, s.RawMaterials - 1);
        return true;
    }

    // ---- unit damage ----

    static int Hull(GameState game, GameObject ship) => Sd(game, ship, SdField.Hull);
    static int SquadSize(GameState game, GameObject fighter) => Sd(game, fighter, SdField.SquadronSize);

    /// <summary>Hull damage: HullDamage := min(Hull, HullDamage + x); reaching Hull gives DestroyedReason.</summary>
    internal static void DamageHull(GameState game, GameObject ship, int x, DestroyedReason reason)
    {
        int hull = Hull(game, ship), old = ship.HullDamage;
        Repair.SetHullDamage(game, ship, Math.Min(hull, old + x));
        if (ship.HullDamage != old && ship.HullDamage == hull) Objects.Destroy(game, ship, reason);
    }

    /// <summary>Squadron damage: SquadSizeDamage := min(SquadSize, damage + x); reaching SquadSize gives DestroyedReason.</summary>
    static void DamageSquadron(GameState game, GameObject fighter, int x, DestroyedReason reason)
    {
        int size = SquadSize(game, fighter), old = fighter.SquadSizeDamage;
        Repair.SetSquadSizeDamage(game, fighter, Math.Min(size, old + x));
        if (fighter.SquadSizeDamage != old && fighter.SquadSizeDamage == size) Objects.Destroy(game, fighter, reason);
    }

    /// <summary>
    /// Maximum of a hyperdrive modifier (k = 4 primary, 5 backup): 1 if the ship has that hyperdrive (Hyperdrive for primary,
    /// HyperdriveIfDamaged for backup), else 0.
    /// </summary>
    internal static int HyperdriveMax(GameState game, GameObject ship, int k) => Sd(game, ship, k == 4 ? SdField.Hyperdrive : SdField.HyperdriveIfDamaged) != 0 ? 1 : 0;

    /// <summary>
    /// Critical-hit roll after a hull loss x: h = Hull - HullDamage; h <= 0 gives kind 7; x = 0 gives kind 1 (no
    /// draw); else v = roll(G CriticalHitRoll) + (100*x) div (h + x), v >= G CriticalHitThreshold gives kind = T(CSCRHTTB, roll(G CriticalKindRoll)) (missing gives none), else
    /// kind 1. Kinds: 2-5 Shield / Weapon / Tractor / Speed CH + 1 below G CriticalDamageLimit; 6 primary hyperdrive modifier + 1 below its maximum,
    /// else backup; 7 destroyed (DestroyedReason Bombardment).
    /// </summary>
    internal static void CriticalHit(GameState game, GameObject ship, int x)
    {
        var g = game.Rules;
        int h = Hull(game, ship) - ship.HullDamage;
        int? kind = h <= 0 ? 7 : x == 0 ? 1
            : game.Random.Roll(g.CriticalHitRoll, DrawSite.CriticalHit) + 100 * x / (h + x) >= g.CriticalHitThreshold
                ? Missions.Table(game, "CSCRHTTB", game.Random.Roll(g.CriticalKindRoll, DrawSite.CriticalKind)) : 1;
        switch (kind)
        {
            case >= 2 and <= 5:
                if (ship.Critical(kind.Value - 2) < g.CriticalDamageLimit) Repair.SetCritical(game, ship, kind.Value - 2, ship.Critical(kind.Value - 2) + 1);
                break;
            case 6:
                if (ship.Critical(4) < HyperdriveMax(game, ship, 4)) Repair.SetCritical(game, ship, 4, ship.Critical(4) + 1);
                else if (ship.Critical(5) < HyperdriveMax(game, ship, 5)) Repair.SetCritical(game, ship, 5, ship.Critical(5) + 1);
                break;
            case 7:
                if (!ship.Destroyed) Objects.Destroy(game, ship, DestroyedReason.Bombardment);
                break;
        }
    }

    // ---- bombardment ----

    /// <summary>
    /// Capital ship bombardment: B - (B * HullDamage) div Hull, B = SD Bombardment, Hull = SD Hull; 0 while the
    /// ion accumulator is nonzero.
    /// </summary>
    internal static int ShipBombard(GameState game, GameObject ship)
    {
        int b = Sd(game, ship, SdField.Bombardment);
        return ship.IonAccumulator != 0 ? 0 : b - b * ship.HullDamage / Hull(game, ship);
    }

    /// <summary>Fighter bombardment: b - (b * SquadSizeDamage) div SquadSize, b = FIGHTSD 164, SquadSize = 160.</summary>
    internal static int FighterBombard(GameState game, GameObject fighter)
    {
        int b = Sd(game, fighter, SdField.FighterBombardment);
        return b - b * fighter.SquadSizeDamage / SquadSize(game, fighter);
    }

    /// <summary>fleetBombard: sum over its Usable capital ships (key order) of the ship's value plus its Usable fighters' values.</summary>
    internal static int FleetBombard(GameState game, Fleet f) =>
        f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable)
            .Sum(x => ShipBombard(game, x) + x.Contents.Where(y => Fighter(y) && y.Usable).Sum(y => FighterBombard(game, y)));

    /// <summary>Shots: max(0, B - Sh).</summary>
    public static int Shots(int bombard, int shields) => Math.Max(0, bombard - shields);

    /// <summary>Hit test: hit iff defense &lt; between(G BombardHitMin, G BombardHitMax).</summary>
    public static bool Hits(GameState game, int defense) =>
        defense < game.Random.Between(game.Rules.BombardHitMin, game.Rules.BombardHitMax, DrawSite.BombardHit);

    /// <summary>ab(a, b): a &gt; b gives (a - b, 0), else (0, b - a).</summary>
    static (int, int) Absorb(int a, int b) => a > b ? (a - b, 0) : (0, b - a);

    /// <summary>
    /// A bombarding ship applies its accumulators: current shields sh = ((Shield * (G CriticalDamageLimit - c0)) div G CriticalDamageLimit * hl) div
    /// Hull (Shield = SD ShieldStrength, c0 = ShieldRechargeRateCH, hl = Hull - HullDamage; sh = Shield if G CriticalDamageLimit = 0 or Hull = 0); (ion, sh),
    /// (tl, sh), (tl, hl) absorbed in that order; the left-overs and the shields absorbed are stored; the hull loss is applied
    /// (DestroyedReason Bombardment), then the critical-hit roll.
    /// </summary>
    internal static void ApplyAccumulators(GameState game, GameObject ship)
    {
        int hull = Hull(game, ship), hl = hull - ship.HullDamage, shield = Sd(game, ship, SdField.ShieldStrength), g800 = game.Rules.CriticalDamageLimit;
        int sh = g800 == 0 || hull == 0 ? shield : shield * (g800 - ship.Critical(0)) / g800 * hl / hull;
        int ion = ship.IonAccumulator, tl = ship.LaserAccumulator, sh0 = sh, hl0 = hl;
        (ion, sh) = Absorb(ion, sh);
        (tl, sh) = Absorb(tl, sh);
        (tl, hl) = Absorb(tl, hl);
        (ship.IonAccumulator, ship.LaserAccumulator, ship.ShieldsAbsorbed) = (ion, tl, sh0 - sh);
        DamageHull(game, ship, hl0 - hl, DestroyedReason.Bombardment);
        CriticalHit(game, ship, hl0 - hl);
    }

    /// <summary>
    /// Bombardment resolver event, for the bombard command types: attacker = owner of the first Usable fleet
    /// with the Bombard flag (another owner's bombarding fleet gives abort); defence fire into the ships' accumulators and their
    /// application; shots; aftermath; BombardDestroy with a Death-Star class ship destroys the planet instead of the shots; the system Bombard flag
    /// (step 1, cleared by the aftermath) is not kept: of its two bombard flag notifications only the 0 gives 1 has an effect (Jedi-training abort 9)
    /// and only it is posted.
    /// </summary>
    public static void Bombard(GameState game, StarSystem s, CommandType type)
    {
        var bombarding = s.Contents.OfType<Fleet>().Where(f => f.Usable && f.Bombard).ToList();
        if (bombarding.Count == 0) return;
        var a = bombarding[0].Owner;
        if (bombarding.Any(f => f.Owner != a)) return;
        Objects.Notify(game, gs => Jedi.Abort(gs, s, MissionCompletion.SystemBombarded));
        var record = StrikeStart(game, s, false, a, [.. s.Contents.OfType<Fleet>().Where(f => f.Owner == a && f.Bombard)]);
        var g = game.Rules;
        bool t = type != CommandType.BombardVariant1, p = type != CommandType.Bombard, er = type is CommandType.BombardVariant2 or CommandType.BombardDestroy;
        var defender = s.Owner;
        IEnumerable<Fleet> Fleets() => s.Contents.OfType<Fleet>().Where(f => f.Usable && f.Owner == a && f.Bombard);
        // ships walk
        List<GameObject> Ships() => [.. Fleets().SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable && x.Owner == a))];
        List<GameObject> Own(uint lo, uint hi) => [.. s.Contents.Where(x => In(x, lo, hi) && x.Usable && x.Owner == defender)];
        // target walk: null entries stand for the Energy / RawMaterial slots (in that order)
        List<GameObject?> Targets()
        {
            var list = new List<GameObject?>();
            if (t) list.AddRange([.. Own(Families.Troop, Families.CapitalShip), .. Own(Families.IonCannon, Families.Shipyard), .. Own(Families.Headquarters, Families.IonCannon)]);
            if (p) list.AddRange(Own(Families.Shipyard, Families.MajorCharacter));
            if (er) { if (s.Energy != 0) list.Add(null); if (s.RawMaterials != 0) list.Add(null); }
            return list;
        }
        // 1. planet destruction (BombardDestroy with a Death-Star class ship), counts
        bool destroyed = type == CommandType.BombardDestroy && DeathStar.DestroyPlanet(game, s, a, Ships());
        int ns = Ships().Count, nt = Targets().Count;
        // 2. defence fire: ion cannons into the ion accumulator, then planetary batteries into the laser accumulator (any owner); then every ship applies
        int lead = Lead(game, Commander(null, s, 3)) / g.DefenseLeadershipDivisor + 1;
        foreach (uint family in new uint[] { Families.IonCannon, Families.Gun })
            foreach (var gun in s.Contents.Where(x => Family.Of(x.Type) == family && x.Usable).ToList())
            {
                int i = game.Random.Roll(ns - 1, DrawSite.BombardFire);
                var walk = Ships();
                if (i < 0 || i >= walk.Count) continue;
                if (family == Families.IonCannon) walk[i].IonAccumulator += Sd(game, gun, SdField.DefenseAttackStrength) * lead;
                else walk[i].LaserAccumulator += Sd(game, gun, SdField.DefenseAttackStrength) * lead;
            }
        foreach (var ship in Ships()) ApplyAccumulators(game, ship);
        bool stop = Ships().Count == 0 || destroyed, civilian = false;
        // 3. shots
        if (!stop)
        {
            int b = Fleets().Sum(f => FleetBombard(game, f) * (Lead(game, Commander(f, s, 2)) / g.BombardLeadershipDivisor + 1));
            int sh = s.Contents.Where(x => Family.Of(x.Type) == Families.PlanetaryShield && x.Usable && x.Owner == defender).Sum(x => Sd(game, x, SdField.DefenseShieldStrength));
            int shots = Shots(b, sh);
            if (type == CommandType.Bombard && shots > 0 && game.Random.Roll(nt, DrawSite.BombardShot) < 1)
            {
                var processing = Own(Families.Shipyard, Families.MajorCharacter);
                int i = game.Random.Roll(processing.Count - 1, DrawSite.BombardFacility);
                if (i >= 0 && i < processing.Count) Hit(processing[i], energySlot: false);
            }
            for (; shots > 0 && !stop; shots--)
            {
                int i = game.Random.Roll(nt - 1, DrawSite.BombardShot);
                var targets = Targets();
                if (i < 0 || i >= targets.Count) continue;
                Hit(targets[i], energySlot: targets[i] is null && i == targets.IndexOf(null) && s.Energy != 0);
            }
        }
        // 4. aftermath: accumulators cleared
        foreach (var ship in Ships()) (ship.IonAccumulator, ship.LaserAccumulator, ship.ShieldsAbsorbed) = (0, 0, 0);
        if (destroyed)
        {
            // -20 for side a (gate Populated only, still set), and the galaxy-wide waves
            Systems.ChangeSupport(game, s, a, g.CivilianBombardSupportDelta);
            DeathStar.Waves(game, a);
        }
        else if (civilian)
        {
            Systems.ChangeSupport(game, s, a, g.CivilianBombardSupportDelta);
            // Records [a, limit, a, delta]: core [144, 146) S CoreBombardDelta / limit S CoreBombardLimit, then rim [146, 148) S RimBombardDelta / S RimBombardLimit
            Systems.Wave(game, s, a, a, g.For(a).CoreBombardLimit, g.For(a).CoreBombardDelta, Families.CoreSystem, Families.RimSystem);
            Systems.Wave(game, s, a, a, g.For(a).RimBombardLimit, g.For(a).RimBombardDelta, Families.RimSystem, Families.RimSystemEnd);
        }
        foreach (var f in s.Contents.OfType<Fleet>().Where(f => f.Owner == a)) f.Bombard = false;
        if (record is not null) game.StrikeTrace!.Add(record());

        // hit test and hit; after a hit the targets are recounted, 0 gives stop
        void Hit(GameObject? target, bool energySlot)
        {
            int def = target is null ? (energySlot ? g.EnergyBombardDefense : g.RawMaterialBombardDefense) : Sd(game, target, Family.IsTroop(target.Type) ? SdField.TroopBombardmentDefense : SdField.FacilityBombardmentDefense);
            if (!Hits(game, def)) return;
            if (target is null)
            {
                if (energySlot) Systems.SetEnergy(game, s, s.Energy - 1);
                else Systems.SetRawMaterials(game, s, s.RawMaterials - 1);
                civilian = true;
            }
            else
            {
                if (In(target, Families.Shipyard, Families.MajorCharacter)) civilian = true;
                Objects.Destroy(game, target, target.Owner == a ? DestroyedReason.OwnBombardment : DestroyedReason.Bombardment);
            }
            if (Targets().Count == 0) stop = true;
        }
    }

    // ---- Blockade and WithdrawPercent ----

    /// <summary>
    /// Blockade setter and hook (every change after the game-ready flag): fleet Blockade flags; facilities Suspended :=
    /// Uprising or Blockade; balancing of the shipyard, training and construction managers; WithdrawPercent; on set the assault /
    /// bombardment loss records := 0 and the blockading side stored (1 or 2 only); post blockade notification; timer BlockadeSupport Active := Blockade.
    /// </summary>
    internal static void SetBlockade(GameState game, StarSystem s, bool value)
    {
        if (s.Blockade == value) return;
        s.Blockade = value;
        game.ViewMessage(ViewMessageType.Blockade, s); // view copy (live views only) posts the Blockade view field on change
        if (!game.Ready) return;
        SetFleetBlockade(s, value);
        Systems.Suspend(game, s);
        UpdateWithdrawPercent(game, s);
        if (value)
        {
            Array.Clear(s.AssaultLoss);
            Array.Clear(s.BombardLoss);
            if (BlockadingSide(s) is Owner.Alliance or Owner.Empire) s.Blockader = BlockadingSide(s);
        }
        Objects.Notify(game, g => OnBlockadeChanged(g, s, value));
        var timer = TimerCatalogue.Slot(game, TimerType.BlockadeSupport, s.Ref, s);
        if (value) timer.Start(game, game.Rules.BlockadePopularDays, 0);
        else timer.SetActive(game, false);
    }

    /// <summary>
    /// blockade notification, in order: 1. manager F8 hook (both values): phase check of every research mission (family
    /// 83, key order) whose TargetLocation is the system; 2. manager FA hook (clear only): loyalty change, not
    /// modelled (deferred like the battle end's); 3.-4. set only: TargetKey reset for the system, then deployment
    /// check per Existing object not Completed or Enroute; 5. side recompute 1 and 2; 6. clear only: Blockade-end support wave.
    /// </summary>
    static void OnBlockadeChanged(GameState game, StarSystem s, bool value)
    {
        foreach (var m in game.Missions.Where(m => !m.Destroyed && m.Kind == MissionKind.Research && m.TargetLocation == s).OrderBy(m => m.Key).ToList())
            Missions.PhaseCheck(game, m);
        if (value)
        {
            Economy.ResetTargets(game, s);
            foreach (var o in s.Contents.Where(o => o.Existing && (!o.Completed || o.Enroute)).ToList()) Fleets.DeploymentCheck(game, o);
        }
        Economy.RecomputeSideTotals(game);
        if (!value) BlockadeEndWave(game, s);
    }

    /// <summary>
    /// Blockade-end support wave (blockade notification step 6): u = stored blockading side; u != 3 and not (Destroyed or u = owner) gives u := 2 - (u != 1);
    /// P_s = 0 unless the stored side is 1 or 2, else the side's assault + bombardment records, plus side 3's when s != the stored
    /// side; then the post-battle wave (system, u, P1, P2). Verified as arithmetic, never traced with nonzero losses.
    /// </summary>
    static void BlockadeEndWave(GameState game, StarSystem s)
    {
        var stored = s.Blockader;
        var u = stored;
        if (u != Owner.Neutral && !(s.Destroyed || u == s.Owner)) u = u == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        int P(Owner side) => stored is not (Owner.Alliance or Owner.Empire) ? 0
            : s.AssaultLoss[(int)side - 1] + s.BombardLoss[(int)side - 1] + (side != stored ? s.AssaultLoss[2] + s.BombardLoss[2] : 0);
        if (u is Owner.Alliance or Owner.Empire) PostBattleWave(game, s, u, P(Owner.Alliance), P(Owner.Empire)); // wave only for w in {1, 2}
    }

    /// <summary>Blockading side: owner of the first Usable fleet in the system (any owner, key order) while Blockade, else 3.</summary>
    internal static Owner BlockadingSide(StarSystem s) =>
        s.Blockade && s.Contents.OfType<Fleet>().FirstOrDefault(f => f.Usable) is { } f ? f.Owner : Owner.Neutral;

    /// <summary>Popular side: 1 if share(1) &gt; share(2), 2 if share(2) &gt; share(1), else 3.</summary>
    internal static Owner PopularSide(StarSystem s) => s.Support > 100 - s.Support ? Owner.Alliance : s.Support < 100 - s.Support ? Owner.Empire : Owner.Neutral;

    /// <summary>
    /// Timer BlockadeSupport: with Blockade and a blockading side b: support change G BlockadePopularDelta for b if b is the popular side,
    /// else G BlockadeUnpopularDelta.
    /// </summary>
    internal static void BlockadeSupport(GameState game, StarSystem s)
    {
        var b = BlockadingSide(s);
        if (!s.Blockade || b is not (Owner.Alliance or Owner.Empire)) return;
        Systems.ChangeSupport(game, s, b, (PopularSide(s) == b ? game.Rules.BlockadePopularDelta : game.Rules.BlockadeUnpopularDelta));
    }

    /// <summary>
    /// TroopRegWithdrawPercent (set on change): 100, except with Blockade and no Usable ion cannon (family
    /// 34, any owner): max(0, 100 - G BlockadeShipPenalty * S - G BlockadeFighterPenalty * F), S = Usable capital ships in the Usable fleets (any owner),
    /// F = Usable fighters aboard them plus those directly in the system. Hook: pushed to the Usable troops directly in the system.
    /// </summary>
    internal static void UpdateWithdrawPercent(GameState game, StarSystem s)
    {
        int v = s.Blockade && !s.Contents.Any(x => Family.Of(x.Type) == Families.IonCannon && x.Usable) ? Withdraw(game, s) : 100;
        if (v == s.WithdrawPercent) return;
        s.WithdrawPercent = v;
        foreach (var t in s.Contents.Where(x => Family.IsTroop(x.Type) && x.Usable)) t.WithdrawPercent = v;
    }

    /// <summary>withdraw(S, F): max(0, 100 - G BlockadeShipPenalty * S - G BlockadeFighterPenalty * F) over the system's Usable fleets (any owner).</summary>
    internal static int Withdraw(GameState game, StarSystem s)
    {
        var ships = s.Contents.OfType<Fleet>().Where(f => f.Usable).SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable)).ToList();
        int fighters = ships.Sum(x => x.Contents.Count(y => Fighter(y) && y.Usable)) + s.Contents.Count(y => Fighter(y) && y.Usable);
        return Math.Max(0, 100 - game.Rules.BlockadeShipPenalty * ships.Count - game.Rules.BlockadeFighterPenalty * fighters);
    }

    // ---- space battle ----

    /// <summary>
    /// BattlePending setter; hook on set (no ready test): BattleToHost := 1, BattleWon
    /// record, ControlKind BattleWon and BattleWithdrew := 0, post battle pending notification and set the game's BattleRequired flag (only once the game is
    /// ready: no notification exists before).
    /// battle pending notification is posted on clear too: manager F8 then regroups the system's autorouting / adrift agents.
    /// </summary>
    internal static void SetBattlePending(GameState game, StarSystem s, bool value)
    {
        if (s.BattlePending == value) return;
        s.BattlePending = value;
        if (!value) { Objects.Notify(game, g => Prisoners.Regroup(g, s)); return; }
        s.BattleToHost = true;
        (s.BattleWon, s.ControlBattleWon, s.BattleWithdrew) = (Owner.None, Owner.None, Owner.None);
        if (!game.Ready) return;
        Objects.Notify(game, g => g.BattleRequired = true);
    }

    /// <summary>
    /// Battle setter and hook (on change after the game-ready flag): on set the battle loss records :=
    /// 0; fleet Battle flags (set and clear); manager 250's battle hook and its notification and the side views have no sim effect.
    /// </summary>
    internal static void SetBattle(GameState game, StarSystem s, bool value)
    {
        if (s.Battle == value) return;
        s.Battle = value;
        game.ViewMessage(ViewMessageType.Battle, s); // view copy (live views only); Inferred: posts the Battle view field on change as the Blockade view field
        if (!game.Ready) return;
        if (value) Array.Clear(s.BattleLoss);
        SetFleetBattle(s, value);
    }

    /// <summary>
    /// System content hook from the Existing notification handler with Existing 0: a Destroyed unit of families [16, 32)
    /// adds CombatValue minus a zero from a unit hook, to its owner's battle (DestroyedReason Battle), assault (DestroyedReason Assault) or bombardment
    /// (DestroyedReason Bombardment) loss record. Inferred: its position among the Existing notification handler's steps (no draws).
    /// </summary>
    internal static void RecordLoss(GameState game, StarSystem s, GameObject o)
    {
        if (!o.Destroyed || !In(o, Families.Troop, Families.Headquarters) || o.Owner is not (Owner.Alliance or Owner.Empire or Owner.Neutral)) return;
        var record = o.DestroyedReason switch { DestroyedReason.Battle => s.BattleLoss, DestroyedReason.Assault => s.AssaultLoss, DestroyedReason.Bombardment => s.BombardLoss, _ => null };
        if (record is not null) record[(int)o.Owner - 1] += Sd(game, o, SdField.CombatValue);
    }

    /// <summary>
    /// Host stage 13 with BattleRequired set: battle start walk over the systems with
    /// BattleToHost (registry order), clearing the flag of each one examined; the first that still has BattlePending: one command
    /// serial (battle header), Battle := 1 (<see cref="SetBattle"/>); the recorded result, else the automatic engine's
    /// (<see cref="BattleEngine"/>), applied; battle end and the stage-6 controller delivery;
    /// then the walk from the start. None left, BattleRequired := 0.
    /// A system still BattlePending afterwards is not hosted again until BattlePending falls and rises.
    /// Tactical: a controller answered "take command" (answer 2, no recorded result) gives host state 7: the
    /// <see cref="GameState.Tactical"/> battle is created and the host waits (clock paused, <see cref="GameState.HostedBattle"/> kept)
    /// until the front end has finished it (<see cref="TacticalBattle.Done"/>); state 22: its result applied with the automatic flag
    /// 0; a withdraw (S[6], Get Immediate Result) gives state 8: the automatic engine on the same system, rebuilt from the game objects
    /// as they are after the first application, applied; then the battle end.
    /// </summary>
    internal static void HostBattles(GameState game)
    {
        while ((game.HostedBattle ?? game.Systems.FirstOrDefault(x => x.BattleToHost)) is { } s)
        {
            if (game.Tactical is { } tactical)
            {
                if (!tactical.Done) return; // host state 20: the front end runs the turns
                game.Tactical = null;
                game.HostedBattle = null;
                bool withdraw = ApplyBattle(game, s, tactical.Result()); // state 22, automatic flag 0
                game.Clock.Flush(game); // Inferred: the first application's notifications run before the state-8 setup
                if (withdraw) ApplyBattle(game, s, BattleEngine.Run(game, s)); // state 8 (the human's float: the 0.0 of take command)
                Finish(game, s, game.TacticalUnits ?? []);
                game.TacticalUnits = null;
                continue;
            }
            s.BattleToHost = false;
            if (!s.BattlePending) { game.HostedBattle = null; continue; }
            // host states 5 / 6: wait while a controller has not answered; the clock stays paused
            game.HostedBattle = s;
            if (!game.Controllers.Values.All(c => c.BattleAnswered(game, s))) return;
            var recorded = game.BattleResults?.Invoke(s);
            var units = game.BattleTrace is null ? [] : Units(s);
            if (recorded is null && game.Controllers.Values.FirstOrDefault(c => c.BattleTactical) is { } human)
            {
                game.Clock.NextSerial();
                SetBattle(game, s, true);
                game.Tactical = new TacticalBattle(game, s, human.Side); // host state 7
                game.TacticalUnits = units;
                return;
            }
            game.HostedBattle = null;
            var result = recorded ?? BattleEngine.Run(game, s);
            game.Clock.NextSerial();
            SetBattle(game, s, true);
            ApplyBattle(game, s, result);
            Finish(game, s, units);
        }
        game.BattleRequired = false;
    }

    /// <summary>The applied battle's end: flush, battle end, controller delivery, the report.</summary>
    static void Finish(GameState game, StarSystem s, List<GameObject> units)
    {
        game.Clock.Flush(game); // the application's notifications run before the battle end (site Inferred)
        EndBattle(game, s);
        Ai.Controller.Deliver(game); // the stage-6 delivery, after the battle end's flush
        foreach (var c in game.Controllers.Values) c.BattleDone();
        if (game.BattleTrace is { } trace)
            trace.Add(new BattleReport(game.Day, s, s.BattleWon, [.. units.Select(u => u.Owner).Distinct()],
                units.Count(u => u.Destroyed && u.Owner == Owner.Alliance), units.Count(u => u.Destroyed && u.Owner == Owner.Empire), units));
    }

    /// <summary>
    /// The battle's units for the report: ships, fighters, troops aboard and fighters / troops in the system (Usable, Alliance / Empire),
    /// over Existing fleets and Usable ships as the engine setup; units still in hyperspace (Enroute, so not Usable) are not
    /// participants.
    /// </summary>
    public static List<GameObject> Units(StarSystem s)
    {
        var list = new List<GameObject>();
        void Walk(GameObject o)
        {
            foreach (var x in o.Contents)
            {
                if (In(x, Families.Troop, Families.Headquarters) && x.Usable && x.Owner is Owner.Alliance or Owner.Empire) list.Add(x);
                if (x is Fleet { Existing: true } || Family.IsCapitalShip(x.Type) && x.Usable) Walk(x);
            }
        }
        Walk(s);
        return list;
    }

    /// <summary>
    /// Result application, no draw. 1. BattleWon from S (S[1] / S[3], both gives opp(BattleReady)); undecided, not
    /// automatic and S[6] != 0 gives withdraw, BattleWon not written (how the host uses withdraw is not verified); else undecided gives the
    /// same rule on S[0] / S[2], and the BattleWon record := w. 2. Fighter strength, walk A (Existing fleets gives Usable capital ships
    /// gives Usable fighters, then Usable fighters directly in the system): squadron damage to SquadSize - F[6] (DestroyedReason Battle).
    /// 3. Fighter placement, walk A: the carrier if it has the fighter's owner, else the system; set parent on a change.
    /// 4. Walk B per Existing fleet: capital ships [20, 24) (hyperdrive hits split, hull, the six CH counters), Death-Star class
    /// ships (hull, D[2] = 0 gives DestroyedReason Battle), then fighters over FighterCapacity aboard to the system.
    /// A unit without a record is an incomplete battle record (exception). Returns the withdraw flag (host state 8 follows).
    /// </summary>
    public static bool ApplyBattle(GameState game, StarSystem s, BattleResult r)
    {
        // 1. BattleWon
        Owner Won(int one, int two) => one != 0 && two == 0 ? Owner.Alliance : two != 0 && one == 0 ? Owner.Empire
            : one != 0 && two != 0 ? Opp(s.BattleReady) : Owner.None;
        var w = Won(r.S[1], r.S[3]);
        bool withdraw = w == Owner.None && !r.Auto && r.S[6] != 0;
        if (!withdraw)
        {
            if (w == Owner.None) w = Won(r.S[0], r.S[2]);
            s.BattleWon = w;
        }
        T Record<T>(IReadOnlyDictionary<GameObject, T> map, GameObject unit) =>
            map.TryGetValue(unit, out var v) ? v : throw new InvalidDataException($"battle record lacks {unit}");

        // walk A: Existing fleets (any owner, key order) gives Usable capital ships gives Usable fighters; then Usable fighters directly here
        List<GameObject> WalkA() =>
            [.. s.Contents.OfType<Fleet>().Where(f => f.Existing).SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable))
                .SelectMany(x => x.Contents.Where(y => Fighter(y) && y.Usable)), .. s.Contents.Where(y => Fighter(y) && y.Usable)];
        // 2. fighter strength
        foreach (var f in WalkA())
        {
            int d = Record(r.Fighters, f).Left - (SquadSize(game, f) - f.SquadSizeDamage);
            if (d > 0) Repair.SetSquadSizeDamage(game, f, Math.Max(0, f.SquadSizeDamage - d));
            else if (d < 0) DamageSquadron(game, f, -d, DestroyedReason.Battle);
        }
        // 3. fighter placement
        foreach (var f in WalkA())
        {
            GameObject parent = Record(r.Fighters, f).Carrier is { } c && c.Owner == f.Owner ? c : s;
            if (parent != f.Container) Objects.SetParent(game, f, parent);
        }
        // 4. ships, walk B
        foreach (var fleet in s.Contents.OfType<Fleet>().Where(f => f.Existing).ToList())
        {
            foreach (var ship in fleet.Contents.Where(x => In(x, Families.CapitalShip, Families.DeathStar) && x.Usable).ToList()) ApplyShip(game, ship, Record(r.Ships, ship));
            foreach (var ds in fleet.Contents.Where(x => In(x, Families.DeathStar, Families.Fighter) && x.Usable).ToList())
            {
                var rec = Record(r.DeathStars, ds);
                SetHull(game, ds, rec.Hull);
                if (!rec.Alive && ds.Existing) Objects.Destroy(game, ds, DestroyedReason.Battle);
            }
            foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).ToList())
            {
                int e = ship.Contents.Count(x => Fighter(x) && x.Existing) - Sd(game, ship, SdField.FighterCapacity);
                if (e <= 0) continue;
                foreach (var f in ship.Contents.Where(x => Fighter(x) && x.Usable).Take(e).ToList()) Objects.SetParent(game, f, s);
            }
        }
        return withdraw;
    }

    /// <summary>Hull from a record: d = r - (Hull - HullDamage); d &gt; 0 repair, d &lt; 0 damage with DestroyedReason Battle.</summary>
    static void SetHull(GameState game, GameObject ship, int remaining)
    {
        int d = remaining - (Hull(game, ship) - ship.HullDamage);
        if (d > 0) Repair.SetHullDamage(game, ship, Math.Max(0, ship.HullDamage - d));
        else if (d < 0) DamageHull(game, ship, -d, DestroyedReason.Battle);
    }

    /// <summary>
    /// Capital ship record: h = C[7] split into primary := min(h, Pmax), backup := min(h - primary, Bmax); h left over gives
    /// nothing written. Else hull, then the CH counters set in the order Shield C[6], Weapon C[9], Tractor C[8], Speed C[5] (range
    /// 0...G CriticalDamageLimit), primary, backup (0...1), stopping at the first value out of range.
    /// </summary>
    static void ApplyShip(GameState game, GameObject ship, ShipResult c)
    {
        int h = c.Hyperdrive;
        int primary = Math.Min(h, HyperdriveMax(game, ship, 4));
        h -= primary;
        int backup = Math.Min(h, HyperdriveMax(game, ship, 5));
        if (h - backup != 0) return;
        SetHull(game, ship, c.Hull);
        int max = game.Rules.CriticalDamageLimit;
        foreach (var (k, v, limit) in new[] { (0, c.Shield, max), (1, c.Weapon, max), (2, c.Tractor, max), (3, c.Speed, max), (4, primary, 1), (5, backup, 1) })
        {
            if (v < 0 || v > limit) break;
            Repair.SetCritical(game, ship, k, v);
        }
    }

    /// <summary>
    /// Battle end: Battle := 0; w = BattleWon record, or opp(BattleReady) when 0 and both sides still have a
    /// Usable fleet or fighter directly here; l = opp(w) if w != 0, else 0; w != 0: deployment check of w's Usable fighters directly
    /// in the system; l != 0: Autorouting for l's Usable fleets, then fighters (key order) directly here; flush; exactly one side
    /// with Usable fleets / fighters left gives ControlKind BattleWon := it and the post-battle wave; none gives 0; flush.
    /// </summary>
    internal static void EndBattle(GameState game, StarSystem s)
    {
        SetBattle(game, s, false);
        bool Present(Owner side) => s.Contents.Any(x => (x is Fleet || Fighter(x)) && x.Usable && x.Owner == side);
        var w = s.BattleWon;
        if (w == Owner.None && Present(Owner.Alliance) && Present(Owner.Empire)) w = Opp(s.BattleReady);
        var l = w == Owner.None ? Owner.None : Opp(w);
        if (w != Owner.None && game.Ready)
            foreach (var f in s.Contents.Where(x => Fighter(x) && x.Usable && x.Owner == w).ToList()) Fleets.DeploymentCheck(game, f);
        if (l != Owner.None)
            foreach (var o in s.Contents.Where(x => (x is Fleet || Fighter(x)) && x.Usable && x.Owner == l).ToList()) Objects.SetAutorouting(game, o);
        game.Clock.Flush(game);
        bool p1 = Present(Owner.Alliance), p2 = Present(Owner.Empire);
        if (p1 != p2)
        {
            s.ControlBattleWon = p1 ? Owner.Alliance : Owner.Empire;
            PostBattleWave(game, s, s.ControlBattleWon, s.BattleLoss[0], s.BattleLoss[1]);
        }
        else if (!p1) s.ControlBattleWon = Owner.None;
        game.Clock.Flush(game);
    }

    /// <summary>
    /// Post-battle support wave. P_s = sum of SD CombatValue over the side's Existing, Completed objects
    /// [16, 32); k = 1 if P_1 &gt; P_2 else 2; vs / vw from S BattleWinnerBase to BattleLoserScale scaled by the battle records; v by who won; v &lt; 0 flips
    /// the winner; D = clamp(v, 0, S BattleWaveMaxDelta(w)); a sector wave notification over the sector toward w, limit S BattleWaveLimit(opp w).
    /// </summary>
    static void PostBattleWave(GameState game, StarSystem s, Owner w, int l1, int l2)
    {
        var p = game.Rules;
        int P(Owner side) => game.Created.Where(o => In(o, Families.Troop, Families.Headquarters) && o.Existing && o.Completed && o.Owner == side).Sum(o => Sd(game, o, SdField.CombatValue));
        int L(Owner side) => side == Owner.Alliance ? l1 : l2;

        int p1 = P(Owner.Alliance), p2 = P(Owner.Empire);
        var k = p1 > p2 ? Owner.Alliance : Owner.Empire;
        var ok = Opp(k);
        int big = Math.Max(p1, p2), diff = big - Math.Min(p1, p2), vs = 0, vw = 0;
        if (big != 0)
        {
            vs = p.For(k).BattleWinnerBase + p.For(k).BattleWinnerScale * diff / big;
            vw = p.For(ok).BattleLoserScale + p.For(ok).BattleLoserBase - p.For(ok).BattleLoserScale * diff / big;
        }
        vs = L(k) * vs / 100;
        vw = L(ok) * vw / 100;
        int v = (k == w ? 2 * vw - vs : 2 * vs - vw) / p.BattleSupportDivisor;
        if (v < 0) (v, w) = (-v, Opp(w));
        var loser = Opp(w);
        Systems.Wave(game, s, loser, w, p.For(loser).BattleWaveLimit, Clamp(v, 0, p.For(w).BattleWaveMaxDelta));
    }
}
