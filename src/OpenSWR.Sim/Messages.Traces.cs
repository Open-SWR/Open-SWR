using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public sealed partial class Messages
{
    /// <summary>
    /// Type MessageType.Strike strike messages from <see cref="GameState.StrikeTrace"/>: one per strike whose system view is
    /// live for the side at the end (only the end pulse validates the message). the defender is the system owner when the view was
    /// live at the start (else 0: the neutral bodies); the attacker is the owner of an attacking fleet live for the side. Lists (mode-1
    /// walks = Usable content views): L70 the system's contents (assault: Captured characters to L90 when owned by the defender, else
    /// L98), L78 the live fleets' capital ships and their contents (Captured characters to L90), the objects destroyed with a view live
    /// at the destroy or with a once-Created frozen view at the staging pass to L80 when not Completed or a system,
    /// else L88. Skipped when the side's system view is Destroyed (type MessageType.PlanetDestroyed instead). Coruscant: the classifier drops only
    /// the start pulse; the end pulse still builds the message, and the type-4 owner message and the MessageType.CoruscantCaptured event are separate groups
    /// (ViewMessageType.Owner and the victory-state view message are kind 2), so all three are built.
    /// Kind 7 for the attacker (opens the strike window; observed in the original), else 3 with advisor code AdvisorCode.Bombardment / AdvisorCode.Assault (observed in the original); category
    /// Conflict; no sound.
    /// </summary>
    void Strikes()
    {
        var strikes = _game.StrikeTrace!;
        for (; _strike < strikes.Count; _strike++)
        {
            var r = strikes[_strike];
            var s = r.System;
            if (!r.EndLive[_side]) continue;
            var view = Knowledge.See(_game, s, _side);
            if (!r.Assault && view.Destroyed) continue;
            var defender = r.StartLive[_side] ? r.Defender : Owner.None;
            var attacker = r.Fleets.Any(f => f.Live[_side]) ? r.Attacker : Owner.None;
            List<GameObject>[] l = [[], [], [], [], [], []];
            static bool Prisoner(GameObject o) => o is Character { Captured: true };
            foreach (var o in r.Contents)
                l[r.Assault && Prisoner(o) ? o.Owner == defender ? 4 : 5 : 0].Add(o);
            foreach (var f in r.Fleets.Where(f => f.Live[_side]))
            {
                l[1].AddRange(f.Ships);
                foreach (var o in f.Aboard) l[Prisoner(o) ? 4 : 1].Add(o);
            }
            foreach (var (o, live, completed) in r.Destroyed.Where(d => d.Live[_side]))
                l[!completed || o is StarSystem ? 2 : 3].Add(o);
            var att = MessageArg.Of(attacker == Owner.Alliance ? Owner.Alliance : Owner.Empire); // attacker 0 names side 2
            MessageArg? def = (defender is Owner.Alliance or Owner.Empire) ? MessageArg.Of(defender) : (MessageArg?)null;
            string sys = Name(s);
            MessageText title, body;
            int picture;
            if (r.Assault)
            {
                bool taken = view.Owner == attacker;
                title = T(TextId.Assault, sys);
                body = def is { } d ? T(taken ? TextId.TroopsTakenControlSystem : TextId.TroopsDefendedAssault, att, d, sys) : T(taken ? TextId.TroopsSeizedControlNeutral : TextId.NeutralSystemRepulsedAttack, att, sys);
                picture = attacker == Owner.Alliance ? 11160 : 11161;
            }
            else
            {
                title = T(TextId.OrbitalBombardment, sys);
                body = def is { } d ? T(TextId.OrbitalStrikeSystem, att, d, sys) : T(TextId.OrbitalStrikeNonAligned, att, sys);
                picture = l[2].Count > 0 ? 11163 : l[3].Count > 0 ? 11164 : 11162;
            }
            bool mine = attacker == _side;
            var m = Add(r.Day, MessageType.Strike, MessageCategory.Conflict, mine ? 7 : 3, title, [body], s, picture);
            (m.Assault, m.Defender, m.Attacker, m.Strike) = (r.Assault, defender, attacker, l);
            if (!mine) m.AdvisorCode = r.Assault ? AdvisorCode.Assault : AdvisorCode.Bombardment;
        }
        if (_ownsStrikes) { strikes.Clear(); _strike = 0; }
    }

    /// <summary>
    /// Type MessageType.Informants informants: one per incident for a side whose view exists and does not show the system as
    /// its own (owner 0 / 3: either side): TextId.InformantsProvideInformation / TextId.InformantsProvidedUsInformation (system name), picture 1000, sound 1100, category Support, advisor code AdvisorCode.ResourceEvent.
    /// </summary>
    void Informants()
    {
        var trace = _game.InformantTrace!;
        for (; _informant < trace.Count; _informant++)
        {
            var r = trace[_informant];
            if ((_side == Owner.Alliance ? r.AllianceView : r.EmpireView) is not { } owner || owner == _side) continue;
            Tag(Add(r.Day, MessageType.Informants, MessageCategory.Support, 3, T(TextId.InformantsProvideInformation, Name(r.System)), [T(TextId.InformantsProvidedUsInformation, Name(r.System))], r.System, 1000, 1100), AdvisorCode.ResourceEvent);
        }
        if (_ownsInformants) { trace.Clear(); _informant = 0; }
    }

    /// <summary>
    /// Type MessageType.ResourceEvent natural disaster / resources (decided at the group's close marker): only the system owner's side with a
    /// live view. F: disaster 8; energy changed 4, raw material 2, either increased 1; a facility [32, 48) destroyed MessageType.FacilityLost (one
    /// TextId.NaturalDisasterLine line each, destroy order). Resources: picture 1002, sound 1101, TextId.NewResources "New Resources" (F 1) with body TextId.NewSourcesRawMaterials (F 2) /
    /// TextId.NewSourcesSystemEnergy, else TextId.ResourcesDepleted with TextId.OperationsReducedNumberRaw / TextId.OperationsReducedSystemEnergy. Disaster: picture 1003, sound 1102, TextId.NaturalDisaster with TextId.NaturalDisasterDestroyedFacilities (facilities and a resource
    /// change) / TextId.NaturalDisasterDestroyedUnits (facilities) / TextId.NaturalDisasterDestroyedResources (resources only) + the lines; neither gives no message. Category Resource, advisor code AdvisorCode.ResourceEvent.
    /// Inferred: the bodies take the system name in slot 1 like the titles; F from the values before and after the event (every
    /// reachable setter sequence moves each value one way).
    /// </summary>
    void Resources()
    {
        var trace = _game.ResourceTrace!;
        for (; _resource < trace.Count; _resource++)
        {
            var r = trace[_resource];
            if (!r.Live[_side] || r.Owner != _side) continue;
            // the message facts as named bools: more, raw, energy, disaster, facilities lost
            bool energy = r.EnergyNew != r.EnergyOld, raw = r.RawNew != r.RawOld;
            bool more = energy && r.EnergyNew > r.EnergyOld || raw && r.RawNew > r.RawOld;
            var lost = r.Destroyed.Where(d => d.Live[_side]).Select(d => d.Unit).ToList();
            string sys = Name(r.System);
            MessageText title;
            List<MessageText> body;
            int picture, sound;
            if (!r.Disaster)
                (title, body, picture, sound) = (T(more ? TextId.NewResources : TextId.ResourcesDepleted, sys), [T(more ? raw ? TextId.NewSourcesRawMaterials : TextId.NewSourcesSystemEnergy : raw ? TextId.OperationsReducedNumberRaw : TextId.OperationsReducedSystemEnergy, sys)], 1002, 1101);
            else
            {
                int text = lost.Count > 0 ? energy || raw ? TextId.NaturalDisasterDestroyedFacilities : TextId.NaturalDisasterDestroyedUnits : energy || raw ? TextId.NaturalDisasterDestroyedResources : 0;
                if (text == 0) continue;
                (title, body, picture, sound) = (T(TextId.NaturalDisaster, sys), [T(text, sys), .. lost.Select(u => T(TextId.NaturalDisasterLine, Name(u)))], 1003, 1102);
            }
            Tag(Add(r.Day, MessageType.ResourceEvent, MessageCategory.Resource, 3, title, body, r.System, picture, sound), AdvisorCode.ResourceEvent);
        }
        if (_ownsResources) { trace.Clear(); _resource = 0; }
    }
}
