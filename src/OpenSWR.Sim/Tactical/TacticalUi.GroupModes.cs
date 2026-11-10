namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    /// <summary>Task force (1-8; T in slot k = id - 1); the task force ship page.</summary>
    public sealed class TaskForceMode(TacticalUi ui, int slot) : Mode(ui)
    {
        public int Slot { get; } = slot;
        public TacticalGroup? T => Ui.TaskForces[Slot];
        internal TacticalUnit? InfoAtEnter;
        public override TacticalModeId Id => TacticalModeId.TaskForce;
        public override TacticalUnit? Subject => Ui.Info ?? T?.UiLeader;
        public override TacticalUnit? Selected => Ui.Info;
        public override TacticalGroup? Shown => T;
        internal override void Enter() { Ui.Info = InfoAtEnter ?? T?.UiLeader; Ui.ShowPage(TacticalPage.TaskForceShip, Ui.Info); }

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (T is not { } t) return null;
            int k = Slot;
            if (u.Side == t.Side)
            {
                if (!ctrl)
                {
                    if (t.UiMembers.Contains(u))
                    {
                        int j = Ui.FreeSlot();
                        if (j < 0) return new OwnShipMode(Ui, u);
                        var others = t.UiMembers.Where(m => m != u).ToList();
                        return others.Count == 0 ? null : Ui.FromSelection(others, [], [u], j); // Inferred: a one-ship task force does not split
                    }
                    if (!Escortable(u)) return null;
                    B.Order(t, TacticalMessage.ReplaceTargets, u);
                    return Mission(TacticalMissionNumber.Escort);
                }
                if (t.UiTargets.Contains(u))
                {
                    B.Order(t, TacticalMessage.RemoveTarget, u);
                    B.SetMission(t, TacticalMissionNumber.None);
                    return null;
                }
                if (!t.UiMembers.Contains(u)) { B.JoinTaskForce(t, u); return null; }
                B.LeaveTaskForce(u);
                return t.UiMembers.Count == 0 ? new IdleMode(Ui) : null;
            }
            if (!ctrl)
            {
                if (t.UiNav.Count == 0) Ui.Voice(TacticalVoice.TaskForceAttack + k, TacticalVoice.TaskForceAttackEmpire + k);
                B.SetMission(t, TacticalMissionNumber.None);
                B.Order(t, TacticalMessage.ReplaceTargets, u);
                return null;
            }
            if (t.UiTargets.Contains(u)) { B.Order(t, TacticalMessage.RemoveTarget, u); return null; }
            if (!Attackable(u)) return new EnemyMode(Ui, u);
            if (t.UiNav.Count == 0 && t.UiTargets.Count == 0) Ui.Voice(TacticalVoice.TaskForceAttack + k, TacticalVoice.TaskForceAttackEmpire + k);
            if (t.UiMission == TacticalMissionNumber.Escort) { B.SetMission(t, TacticalMissionNumber.None); B.Order(t, TacticalMessage.ReplaceTargets, u); }
            else B.Order(t, TacticalMessage.AddTarget, u);
            return null;
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            if (T is not { } t) return null;
            if (!ctrl)
            {
                Ui.Voice(TacticalVoice.TaskForceToNav + Slot, TacticalVoice.TaskForceToNavEmpire + Slot);
                B.SetMission(t, TacticalMissionNumber.None);
                B.OrderNav(t, TacticalMessage.ReplaceNavPoints, n);
            }
            else if (!t.UiNav.Contains(n))
            {
                if (t.UiNav.Count == 0) Ui.Voice(TacticalVoice.TaskForceToNav + Slot, TacticalVoice.TaskForceToNavEmpire + Slot);
                B.OrderNav(t, TacticalMessage.AddNavPoint, n);
            }
            else B.OrderNav(t, TacticalMessage.RemoveNavPoint, n);
            return null;
        }

        public override Mode? EmptyTaskForce(int slot) => new IdleMode(Ui);
        public override Mode? Space() => T is { } t ? Attack(Centroid(Alive(t.UiMembers))) : null;

        public override Mode? Mission(TacticalMissionNumber m)
        {
            if (T is not { } t) return null;
            Ui.Voice(TacticalVoice.TaskForceMission + Slot, TacticalVoice.TaskForceMissionEmpire + Slot);
            B.SetMission(t, m);
            return null;
        }

        public override Mode? Tactic(TacticalTactic tactic)
        {
            if (T is not { } t) return null;
            Ui.Voice(TacticalVoice.TaskForceTactic + Slot, TacticalVoice.TaskForceTacticEmpire + Slot);
            B.SetTactic(t, tactic);
            return null;
        }

        public override Mode? Next() { Cycle(true); return null; }
        public override Mode? Previous() { Cycle(false); return null; }

        /// <summary>Info unit becomes the next / previous UI member of T after it (wraps); the mode stays.</summary>
        void Cycle(bool next)
        {
            if (T is { } t && Step([.. Alive(t.UiMembers)], Ui.Info, next) is { } u) Ui.Info = u;
        }

        public override Mode? Refresh()
        {
            if (T is not { } t || t.UiMembers.Count == 0 || t.UiMission == TacticalMissionNumber.Withdraw) return new IdleMode(Ui);
            if (Ui.Info is not { Active: true } i || !t.UiMembers.Contains(i)) Ui.Info = t.UiLeader; // the UI leader
            return null;
        }
    }

    /// <summary>Fighter group (9-12); G in colour slot c; the fighter group page.</summary>
    public sealed class FighterMode(TacticalUi ui, int colour) : Mode(ui)
    {
        public int Colour { get; } = colour;
        public TacticalGroup? G => Ui.Fighters[Colour];
        public override TacticalModeId Id => TacticalModeId.FighterGroup;
        // Inferred: a destroyed fighter can stay a UI member until its group is deleted; skip it
        public override TacticalUnit? Subject => Alive(G?.UiMembers ?? []).FirstOrDefault();
        public override TacticalGroup? Shown => G;
        internal override void Enter() { Ui.Info = Subject; Ui.ShowPage(TacticalPage.FighterGroup, G); }

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (G is not { } g) return null;
            int c = Colour;
            if (u.Side == g.Side)
            {
                if (ctrl && g.UiTargets.Contains(u)) { B.Order(g, TacticalMessage.RemoveTarget, u); B.SetMission(g, TacticalMissionNumber.None); }
                else if (Escortable(u)) { B.Order(g, TacticalMessage.ReplaceTargets, u); B.SetMission(g, TacticalMissionNumber.Escort); }
                return null;
            }
            if (!ctrl)
            {
                if (g.UiNav.Count == 0) Ui.Voice(TacticalVoice.FighterGroupAttack + c, TacticalVoice.FighterGroupAttackEmpire + c);
                B.SetMission(g, TacticalMissionNumber.None);
                B.Order(g, TacticalMessage.ReplaceTargets, u);
            }
            else if (!g.UiTargets.Contains(u))
            {
                if (g.UiNav.Count == 0 && g.UiTargets.Count == 0) Ui.Voice(TacticalVoice.FighterGroupAttack + c, TacticalVoice.FighterGroupAttackEmpire + c);
                B.Order(g, TacticalMessage.AddTarget, u);
            }
            else B.Order(g, TacticalMessage.RemoveTarget, u);
            return null;
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            if (G is not { } g) return null;
            if (!ctrl)
            {
                Ui.Voice(TacticalVoice.FighterGroupToNav + Colour, TacticalVoice.FighterGroupToNavEmpire + Colour);
                B.SetMission(g, TacticalMissionNumber.None);
                B.OrderNav(g, TacticalMessage.ReplaceNavPoints, n);
            }
            else if (!g.UiNav.Contains(n))
            {
                if (g.UiNav.Count == 0) Ui.Voice(TacticalVoice.FighterGroupToNav + Colour, TacticalVoice.FighterGroupToNavEmpire + Colour);
                B.OrderNav(g, TacticalMessage.AddNavPoint, n);
            }
            else B.OrderNav(g, TacticalMessage.RemoveNavPoint, n);
            return null;
        }

        public override Mode? EmptyTaskForce(int slot) => new IdleMode(Ui);
        public override Mode? Space() => G is { } g ? Attack(Centroid(Alive(g.UiMembers))) : null;

        /// <summary>
        /// Mission: 6 is refused (leads to idle) unless G is side index 0 and the Death Star attack is possible; the voice; the setter.
        /// </summary>
        public override Mode? Mission(TacticalMissionNumber m)
        {
            if (G is not { } g) return null;
            if (m == TacticalMissionNumber.AttackDeathStar && !(g.Side == 0 && B.AttackPossible)) return new IdleMode(Ui);
            Ui.Voice(TacticalVoice.FighterGroupMission + Colour, TacticalVoice.FighterGroupMissionEmpire + Colour);
            B.SetMission(g, m);
            return null;
        }

        public override Mode? Tactic(TacticalTactic t)
        {
            if (G is { } g) B.SetTactic(g, t);
            return null;
        }

        public override Mode? Next() => Step(Ui.Ships(B.Local), Ui.Info, true) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Previous() => Step(Ui.Ships(B.Local), Ui.Info, false) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Refresh() => G is not { } g || g.UiMembers.Count == 0 || g.UiMission is TacticalMissionNumber.Recover or TacticalMissionNumber.Withdraw ? new IdleMode(Ui) : null;
    }
}
