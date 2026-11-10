using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    // ---- step ----

    /// <summary>The printed code of a side state in the state trace: the output code of the trace text, kept so the text does not change.</summary>
    static int TraceSideCode(TacticalSideState state) => state switch
    {
        TacticalSideState.Fighting => 0,
        TacticalSideState.WithdrawOrdered => 2,
        TacticalSideState.Jumping => 3,
        TacticalSideState.JumpFinished => 4,
        TacticalSideState.Withdrawn => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    static bool Step(Side[] sides, GameRandom rand)
    {
        foreach (var side in sides) side.Strength = Strength(side, sides);
        bool done2 = Update(sides[1], sides, rand), done1 = Update(sides[0], sides, rand);
        foreach (var side in sides)
        {
            side.Strength = Strength(side, sides);
            if (side.Strength >= side.Lowest) side.Stall += Dt;
            else (side.Lowest, side.Stall) = (side.Strength, 0);
        }
        StateTrace?.Add($"step {stepNo}: " + string.Join(" | ", sides.Select(x =>
            $"side {x.Index + 1} str {x.Strength:G6} low {x.Lowest:G6} stall {x.Stall} state {TraceSideCode(x.State)} retreat {RetreatPossible(x, sides)} groups ["
            + string.Join(", ", x.Groups.Select(g => $"{string.Join("+", g.Members.Select(a => $"{Family.Of(a.Object.Ref)}:{SdRef.IndexOf(a.Object.Ref)}"))}->{g.Target?.Object.Ref}"))
            + "] units " + string.Join(", ", x.Units.Select(u => $"{Family.Of(u.Object.Ref)}:{SdRef.IndexOf(u.Object.Ref)} st{(int)u.State} h{u.Hull:G6} s{u.Shield:G6} ch{u.ShieldCh}{u.WeaponCh}{u.TractorCh}{u.SpeedCh}{u.HyperCh}")))));
        if (done1 || done2) return true;
        if (sides.All(x => x.Stall >= Stalemate))
        {
            // Step-end strengths (side index 0's is -1 while the scene is alive), side 2 first, then side 1
            double a = Alive(sides[1].Scene) ? -1 : sides[0].Strength, b = sides[1].Strength;
            if (b <= a) Lose(sides[1]);
            if (a <= b) Lose(sides[0]); // side 1 withdrawing: the step returns at once
        }
        return false;

        // a side whose retreat is possible is set withdrawing; else every unit of its non-fighter and fighter lists is destroyed
        void Lose(Side loser)
        {
            if (RetreatPossible(loser, sides)) loser.Withdrawing = true;
            else foreach (var u in loser.Lists.ToList()) Kill(u, sides, rand);
        }
    }

    /// <summary>
    /// Side update: side index 1 launches every pending unit (state 2, appended to side index 1's
    /// fighter list, then the pending list is emptied); candidates and targeting; damage once per group with a target
    /// (no target state test); withdraw; recharge of every unit of both lists with the "done" count.
    /// </summary>
    static bool Update(Side side, Side[] sides, GameRandom rand)
    {
        var enemy = sides[1 - side.Index];
        var scene = sides[1].Scene;
        if (side.Index == 1 && Alive(scene)) Superlaser(scene!, side, enemy, sides, rand);
        // Alliance attack: the scene is attackable and side index 1's fighter list is shorter than side index 0's
        if (side.Index == 0 && Attackable(scene) && sides[1].Fighters.Count < side.Fighters.Count && side.Groups.FirstOrDefault(g => g.Formation) is { } fg)
            Attack(fg, scene!, sides, rand);
        if (side.Index == 1)
        {
            foreach (var u in side.Pending)
            {
                u.State = TacticalUnitState.Active;
                if (!u.Overflow && Resolve(u.Carrier, sides) is { Docked: > 0 } c) c.Docked--; // launch -1; an overflow fighter was never counted (Inferred)
                side.Fighters.Add(u); // the updating side's list, whatever the unit's side (Inferred harmless)
            }
            side.Pending.Clear();
        }
        Target(side, enemy, sides);
        foreach (var g in side.Groups.ToList())
        {
            if (g.Target is not { } t) continue;
            Damage(t, GroupRate(g, t, sides) * Dt, rand);
            if (t.Hull <= 0) Kill(t, sides, rand);
        }
        if (Withdraw(side, sides))
        {
            foreach (var u in side.Fighters.ToList())
                if (CanJump(u, side)) Retreat(u, sides);
                else if (!Land(u, side, sides)) Kill(u, sides, rand);
            if (side.Index == 1 && Alive(scene)) scene!.Withdrawn = true; // Between the fighter loop and the non-fighter loop
            foreach (var u in side.Ships.ToList())
                if (CanJump(u, side)) Retreat(u, sides);
                else Kill(u, sides, rand);
            side.State = TacticalSideState.Withdrawn;
        }
        bool active = false;
        foreach (var u in side.Lists)
        {
            active = active || u.State == TacticalUnitState.Active;
            Recharge(u);
        }
        return !active && !(side.Index == 1 && Alive(scene)); // Side index 1 with a live scene is never done
    }

    /// <summary>
    /// Superlaser (head of side index 1's update): below 100 the charge grows by f32(dt * 0.33333334f * 0.001f) (only
    /// with hull and finished), capped at 100; at exactly 100 the candidate list is rebuilt and its first capital ship (type 0) is
    /// destroyed with its draws, charge := 0; none gives the charge stays 100.
    /// </summary>
    static void Superlaser(Unit scene, Side side, Side enemy, Side[] sides, GameRandom rand)
    {
        if (scene.Charge != 100f && scene.Hull > 0 && !scene.Unfinished)
        {
            float inc = (float)Dt * 0.33333334f * 0.001f;
            scene.Charge += inc;
            if (scene.Charge >= 100f) scene.Charge = 100f;
        }
        if (scene.Charge != 100f || Candidates(side, enemy).FirstOrDefault(u => u.Type == TacticalUnitType.CapitalShip) is not { } t) return;
        scene.Charge = 0;
        Kill(t, sides, rand);
    }

    /// <summary>
    /// Alliance attack: the formation's members by ascending key, walked circularly (next taken first);
    /// visit 101 destroys every member left. Per visit a = trunc(agility), r1 = 1 + rand(9); r1 &gt; a: t = trunc(hull / 8), t <= 1
    /// destroyed, else r2 = 1 + rand(t - 1) and f32(8*r2) damage. Not destroyed (hull &gt; 0): value = f32((b + hull * f32(torpedo *
    /// 0.53333336f) / hull max) * 0.5), r3 = 1 + rand(99) <= trunc(value) gives the Death Star is destroyed (hull and charge 0, its hangar's
    /// units destroyed) and the call ends. An empty (drained) formation does nothing (Inferred).
    /// </summary>
    static void Attack(Group g, Unit scene, Side[] sides, GameRandom rand)
    {
        var copy = g.Members.OrderBy(u => u.Key).ToList();
        var side = sides[0];
        int visits = 0;
        var cur = copy.FirstOrDefault();
        while (cur is not null)
        {
            var next = copy[(copy.IndexOf(cur) + 1) % copy.Count];
            if (++visits > 100)
            {
                foreach (var u in copy) Kill(u, sides, rand);
                return;
            }
            int a = (int)Math.Truncate(Agility(cur, side));
            bool dead = false;
            if (1 + rand.Rand(9) > a)
            {
                int t = (int)Math.Truncate(cur.Hull / 8.0);
                if (t > 1) Damage(cur, F((1 + rand.Rand(t - 1)) * 8.0), rand);
                if (t <= 1 || cur.Hull <= 0) { Kill(cur, sides, rand); dead = true; }
            }
            if (dead)
            {
                copy.Remove(cur);
                if (copy.Count == 0) return;
            }
            else if (cur.Hull > 0)
            {
                double value = F((side.Commander + F(Scale(cur, F(cur.Torpedo * Torpedo)))) * 0.5);
                if (1 + rand.Rand(99) <= Math.Truncate(value))
                {
                    (scene.Hull, scene.Charge) = (0, 0);
                    // the scene hangar's lists are empty whenever the attack runs (Inferred for the second)
                    foreach (var u in sides.SelectMany(x => x.Units).Where(u => u.State is TacticalUnitState.Docked or TacticalUnitState.Recovered && u.Carrier is { Type: TacticalUnitType.DeathStarScene } && !u.Overflow).ToList()) Kill(u, sides, rand);
                    return;
                }
            }
            cur = next;
        }
    }
}
