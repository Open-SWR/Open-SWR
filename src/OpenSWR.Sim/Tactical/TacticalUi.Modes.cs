namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    // ---- modes ----

    /// <summary>A mode object: subject, id, and handlers for unit, nav point, empty task-force button, empty click, Space, mission, tactic, superlaser, refresh, next and previous. Handlers return a new mode or none.</summary>
    public abstract class Mode(TacticalUi ui)
    {
        protected readonly TacticalUi Ui = ui;
        protected TacticalBattle B => Ui._b;
        /// <summary>Mode id: Idle, TaskForce (slot in the mode), FighterGroup (colour in the mode), NavPoint, OwnShip, Enemy, Multi, Superlaser.</summary>
        public abstract TacticalModeId Id { get; }
        /// <summary>Subject for Reset Camera Subject (the ResetSubject camera operation).</summary>
        public virtual TacticalUnit? Subject => null;
        /// <summary>The unit drawn selected.</summary>
        public virtual TacticalUnit? Selected => null;
        /// <summary>Drawn selected (yellow).</summary>
        public virtual bool IsSelected(TacticalUnit u) => Selected == u;
        /// <summary>The shown group.</summary>
        public virtual TacticalGroup? Shown => null;
        /// <summary>The constructor's side effects: info unit and page.</summary>
        internal virtual void Enter() { Ui.Info = null; Ui.ShowPage(TacticalPage.Empty); }
        public virtual Mode? Unit(TacticalUnit u, bool ctrl) => null;
        public virtual Mode? Nav(TacticalNavPoint n, bool ctrl) => null;
        public virtual Mode? EmptyTaskForce(int slot) => null;
        public virtual Mode? Empty() => new IdleMode(Ui);
        public virtual Mode? Space() => null;
        public virtual Mode? Mission(TacticalMissionNumber m) => null;
        public virtual Mode? Tactic(TacticalTactic t) => null;
        /// <summary>
        /// Enters the superlaser target mode only when a Death Star scene exists, its charge is 100.0 and its hull (SD Hull) &gt; 0
        /// (no alive test: an unfinished scene enters and the next refresh leaves).
        /// </summary>
        public virtual Mode? Superlaser() => B.Scene is { } s && s.Charge == 100f && s.HullMax > 0 ? new SuperlaserMode(Ui) : null;
        public virtual Mode? Refresh() => null;
        public virtual Mode? Next() => null;
        public virtual Mode? Previous() => null;
        /// <summary>The selection selects: an own unit gives own-ship mode, an enemy gives enemy mode.</summary>
        protected Mode Select(TacticalUnit u) => u.Side == B.Local ? new OwnShipMode(Ui, u) : new EnemyMode(Ui, u);
        protected Mode? Attack(V3 at) => B.Nearest(at, B.Local) is { } n ? Unit(n, false) : null;
    }

    /// <summary>Idle (0): own unit selects own-ship mode, enemy selects enemy mode, nav point selects nav-point mode; the empty page.</summary>
    public sealed class IdleMode(TacticalUi ui) : Mode(ui)
    {
        public override TacticalModeId Id => TacticalModeId.Idle;
        public override Mode? Unit(TacticalUnit u, bool ctrl) => Select(u);
        public override Mode? Nav(TacticalNavPoint n, bool ctrl) => new NavMode(Ui, n);
    }

    /// <summary>Enemy ship mode: the unit marked; unit selects own-ship or enemy mode, nav selects nav mode; the ship page.</summary>
    public sealed class EnemyMode(TacticalUi ui, TacticalUnit u) : Mode(ui)
    {
        public TacticalUnit U { get; } = u;
        public override TacticalModeId Id => TacticalModeId.Enemy;
        public override TacticalUnit? Subject => U;
        internal override void Enter() { Ui.Info = U; Ui.ShowPage(TacticalPage.Ship, U); }
        public override Mode? Unit(TacticalUnit u, bool ctrl) => Select(u);
        public override Mode? Nav(TacticalNavPoint n, bool ctrl) => new NavMode(Ui, n);
    }

    /// <summary>Nav point mode: the empty page; the point marked (magenta), with the first-point mark cleared; unit selects own-ship or enemy mode, nav selects nav mode.</summary>
    public sealed class NavMode(TacticalUi ui, TacticalNavPoint n) : Mode(ui)
    {
        public TacticalNavPoint N { get; } = n;
        public override TacticalModeId Id => TacticalModeId.NavPoint;
        public override Mode? Unit(TacticalUnit u, bool ctrl) => Select(u);
        public override Mode? Nav(TacticalNavPoint n, bool ctrl) => new NavMode(Ui, n);
    }

    /// <summary>Own ship mode: one selected ship S; the ship page.</summary>
    public sealed class OwnShipMode(TacticalUi ui, TacticalUnit s) : Mode(ui)
    {
        public TacticalUnit S { get; } = s;
        public override TacticalModeId Id => TacticalModeId.OwnShip;
        public override TacticalUnit? Subject => S;
        public override TacticalUnit? Selected => S;
        public override TacticalGroup? Shown => S.UiGroup;
        internal override void Enter() { Ui.Info = S; Ui.ShowPage(TacticalPage.Ship, S); }

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (u.Side == S.Side)
            {
                if (ctrl) return u != S ? new MultiMode(Ui, [S, u]) : null;
                if (u == S || !Escortable(u)) return null;
                var t = Ui.TaskForceOf(S);
                Ui.Voice(TacticalVoice.ShipMission, TacticalVoice.ShipMissionEmpire);
                B.Order(t, TacticalMessage.ReplaceTargets, u);
                B.SetMission(t, TacticalMissionNumber.Escort);
                return null;
            }
            if (!Attackable(u)) return new EnemyMode(Ui, u);
            var g = Ui.TaskForceOf(S);
            if (!ctrl)
            {
                if (g.UiNav.Count == 0) Ui.Voice(TacticalVoice.ShipAttack, TacticalVoice.ShipAttackEmpire);
                B.SetMission(g, TacticalMissionNumber.None);
                B.Order(g, TacticalMessage.ReplaceTargets, u);
            }
            else if (!g.UiTargets.Contains(u))
            {
                if (g.UiTargets.Count == 0 && g.UiNav.Count == 0) Ui.Voice(TacticalVoice.ShipAttack, TacticalVoice.ShipAttackEmpire);
                if (g.UiMission == TacticalMissionNumber.Escort) { B.SetMission(g, TacticalMissionNumber.None); B.Order(g, TacticalMessage.ReplaceTargets, u); }
                else B.Order(g, TacticalMessage.AddTarget, u);
            }
            else B.Order(g, TacticalMessage.RemoveTarget, u);
            return null;
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            var g = Ui.TaskForceOf(S);
            if (!ctrl)
            {
                B.SetMission(g, TacticalMissionNumber.None);
                B.OrderNav(g, TacticalMessage.ReplaceNavPoints, n);
                Ui.Voice(TacticalVoice.ShipToNav, TacticalVoice.ShipToNavEmpire);
            }
            else if (!g.UiNav.Contains(n))
            {
                if (g.UiNav.Count == 0) Ui.Voice(TacticalVoice.ShipToNav, TacticalVoice.ShipToNavEmpire);
                B.OrderNav(g, TacticalMessage.AddNavPoint, n);
            }
            else B.OrderNav(g, TacticalMessage.RemoveNavPoint, n);
            return null;
        }

        public override Mode? EmptyTaskForce(int slot) => Ui.FromSelection([S], [], [], slot);
        public override Mode? Space() => Attack(S.Position);

        public override Mode? Mission(TacticalMissionNumber m)
        {
            var g = B.CreateTaskForce(S);
            Ui.Voice(TacticalVoice.ShipMission, TacticalVoice.ShipMissionEmpire);
            B.SetMission(g, m);
            return null;
        }

        public override Mode? Tactic(TacticalTactic t)
        {
            B.SetTactic(B.CreateTaskForce(S), t);
            return null;
        }

        public override Mode? Next() => Step(Ui.Ships(S.Side), S, true) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Previous() => Step(Ui.Ships(S.Side), S, false) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Refresh() => S.Active ? null : new IdleMode(Ui);
    }

    /// <summary>Multi-selection mode: two or more own units; the task force ship page.</summary>
    public sealed class MultiMode(TacticalUi ui, List<TacticalUnit> selection) : Mode(ui)
    {
        public List<TacticalUnit> Selection { get; } = selection;
        public override TacticalModeId Id => TacticalModeId.Multi;
        public override TacticalUnit? Subject => Selection.FirstOrDefault();
        public override TacticalUnit? Selected => Ui.Info;
        /// <summary>The multi-selection mode's per-frame slot: every selected active unit is drawn selected (yellow).</summary>
        public override bool IsSelected(TacticalUnit u) => u.Active && Selection.Contains(u);
        internal override void Enter() { Ui.Info = Selection[^1]; Ui.ShowPage(TacticalPage.TaskForceShip, Ui.Info); } // Inferred: the unit added last (ctrl 1 add)

        int Side => Selection[0].Side;

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            int k = Ui.FreeSlot();
            if (u.Side == B.Local)
            {
                if (ctrl)
                {
                    if (!Selection.Contains(u)) { Selection.Add(u); Ui.Info = u; return null; }
                    Selection.Remove(u);
                    if (Selection.Count == 1) return new OwnShipMode(Ui, Selection[0]);
                    Ui.Info = Selection[0];
                    return null;
                }
                if (!Escortable(u)) return null;
                if (k < 0) return new OwnShipMode(Ui, u);
                Selection.Remove(u);
                return Selection.Count == 0 ? null : Ui.FromSelection(Selection, [], [u], k);
            }
            if (!Attackable(u)) return new EnemyMode(Ui, u);
            if (k < 0)
            {
                Ui.Voices.Add(Side == 0 ? TacticalVoice.NoFreeSlot : TacticalVoice.NoFreeSlotEmpire);
                foreach (var s in Selection) B.Order(B.CreateTaskForce(s), TacticalMessage.AddTarget, u);
                return new OwnShipMode(Ui, Selection[0]);
            }
            Ui.Voice(TacticalVoice.TaskForceAttack + k, TacticalVoice.TaskForceAttackEmpire + k);
            return Ui.FromSelection(Selection, [], [u], k);
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            int k = Ui.FreeSlot();
            if (k < 0)
            {
                Ui.Voices.Add(Side == 0 ? TacticalVoice.NoFreeSlot : TacticalVoice.NoFreeSlotEmpire);
                foreach (var s in Selection) B.OrderNav(B.CreateTaskForce(s), TacticalMessage.AddNavPoint, n);
                return new OwnShipMode(Ui, Selection[0]);
            }
            Ui.Voice(TacticalVoice.TaskForceToNav + k, TacticalVoice.TaskForceToNavEmpire + k);
            return Ui.FromSelection(Selection, [n], [], k);
        }

        public override Mode? EmptyTaskForce(int slot) => Ui.FromSelection(Selection, [], [], slot);
        public override Mode? Space() => Attack(Centroid(Selection));

        public override Mode? Mission(TacticalMissionNumber m) => Order(TacticalVoice.TaskForceMission, TacticalVoice.TaskForceMissionEmpire, g => B.SetMission(g, m));
        public override Mode? Tactic(TacticalTactic t) => Order(TacticalVoice.TaskForceTactic, TacticalVoice.TaskForceTacticEmpire, g => B.SetTactic(g, t));

        /// <summary>Mission / tactic: no free slot gives every unit its own task force with it, then own-ship mode of the first; else a numbered task force in slot k with the voice, giving task-force mode k.</summary>
        Mode Order(int alliance, int empire, Action<TacticalGroup> set)
        {
            int k = Ui.FreeSlot();
            if (k < 0)
            {
                foreach (var s in Selection) set(B.CreateTaskForce(s));
                return new OwnShipMode(Ui, Selection[0]);
            }
            var mode = (TaskForceMode)Ui.FromSelection(Selection, [], [], k);
            Ui.Voice(alliance + k, empire + k);
            set(Ui.TaskForces[k]!);
            return mode;
        }

        public override Mode? Next() => Step(Ui.Ships(B.Local), Ui.Info, true) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Previous() => Step(Ui.Ships(B.Local), Ui.Info, false) is { } u ? new OwnShipMode(Ui, u) : null;

        public override Mode? Refresh()
        {
            Selection.RemoveAll(u => !u.Active);
            if (Selection.Count == 0) return new IdleMode(Ui);
            if (Selection.Count == 1) return new OwnShipMode(Ui, Selection[0]);
            if (Ui.Info is null || !Selection.Contains(Ui.Info)) Ui.Info = Selection[0];
            return null;
        }
    }

    /// <summary>
    /// Superlaser target mode: a right-clicked unit: own unit or no scene gives idle; any enemy unit (capital ship or fighter;
    /// no state, alive or charge test) fires the superlaser, plays voice SuperlaserFired, and gives idle. Space picks the enemy capital
    /// ship nearest to the scene point, else the nearest fighter, then acts as a right click. Per frame: no scene, hull at or
    /// below 0, or charge under 100 gives idle; a scene not withdrawn, with side index 1 in state 0 and alive, stays while in
    /// command, else idle; otherwise the Death Star panel is removed and idle. Leaving the mode clears the target cursor and
    /// unpresses the panel button.
    /// </summary>
    public sealed class SuperlaserMode(TacticalUi ui) : Mode(ui)
    {
        public override TacticalModeId Id => TacticalModeId.Superlaser;
        internal override void Enter() { }
        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (u.Side == B.Local || B.Scene is null) return new IdleMode(Ui);
            B.FireSuperlaser(u);
            Ui.Voices.Add(TacticalVoice.SuperlaserFired);
            return new IdleMode(Ui);
        }
        public override Mode? Space() => B.Nearest(B.ScenePoint, B.Local) is { } n ? Unit(n, false) : null;
        public override Mode? Superlaser() => null;
        public override Mode? Refresh()
        {
            if (B.Scene is not { } s || !(s.HullMax > 0) || s.Charge < 100f) return new IdleMode(Ui);
            if (!s.Withdrawn && B.SideState(1) == TacticalSideState.Fighting && B.SceneAlive) return Ui.InCommand ? null : new IdleMode(Ui);
            Ui.DeathStarPanel = false;
            return new IdleMode(Ui);
        }
    }
}
